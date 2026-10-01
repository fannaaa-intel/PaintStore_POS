Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Globalization
Imports System.Windows.Forms
Imports MySql.Data.MySqlClient

' =============================================================================
'  ProcurementRequestsPanelBuilder  —  Procurement ▸ Purchase Requests
'
'  Every purchase request, from Inventory and from Procurement, newest first.
'  Same rules as before: a request waits for the ADMIN to approve or reject it,
'  and only approved requests can be ordered.
'
'    + New Request    the request form (PurchaseRequestModal): purpose, products,
'                     quantities and estimated unit costs. Sent for approval.
'    Row click        the request in full (RequestReviewModal, read-only here).
'    Create PO        on an Approved request: opens the New PO form with it.
'    Edit / Remove    on the user's own request while it is still Pending.
'
'  Search (request no., person, purpose, product), From / To (date sent) and a
'  Status filter narrow the list. Same frame as Procurement ▸ Reports.
' =============================================================================
Public Class ProcurementRequestsPanelBuilder

    ' ── PALETTE ───────────────────────────────────────────────────────────────
    Private Shared ReadOnly MintDark As Color = Theme.Dark
    Private Shared ReadOnly MintPrimary As Color = Theme.Primary
    Private Shared ReadOnly MintDeep As Color = Theme.Deep
    Private Shared ReadOnly MintBg As Color = Theme.PageBg
    Private Shared ReadOnly CardBg As Color = Color.White
    Private Shared ReadOnly SubText As Color = Theme.SubText
    Private Shared ReadOnly FaintText As Color = Theme.FaintText
    Private Shared ReadOnly RowDivider As Color = Theme.RowDivider
    Private Shared ReadOnly RowHover As Color = Theme.RowHover
    Private Shared ReadOnly AccentRed As Color = Theme.Danger
    Private Shared ReadOnly AccentAmber As Color = Theme.Warning
    Private Shared ReadOnly Peso As String = ChrW(8369)

    Private Const SEARCH_PLACEHOLDER As String = "Search request no., person, purpose, product…"
    Private Const SIDE_PAD As Integer = 24
    Private Const ROW_H As Integer = 64
    Private Const SUMMARY_H As Integer = 140
    Private Const MAX_PAGE_SIZE As Integer = 50
    ' title 72 + filters 56 + summary (140+12) + spacers 16+16 + card padding 2
    ' + colHeader 44 + pagerBar 52 + bottomPad 12
    Private Const CHROME_H As Integer = 422

    ' Column x positions as a share of the row width (header and rows alike).
    Private Const X_REF As Single = 0.12F
    Private Const X_BY As Single = 0.22F
    Private Const X_PURPOSE As Single = 0.36F
    Private Const X_TOTAL_END As Single = 0.73F
    Private Const X_STATUS As Single = 0.75F
    Private Const X_ACTION As Single = 0.86F

    Private Const ALL_STATUSES As String = "All statuses"
    ' Filter label -> stored status. "Ordered" reads better than "Converted".
    Private Shared ReadOnly StatusFilters As String(,) = {
        {"Pending", "Pending"}, {"Approved", "Approved"}, {"Rejected", "Rejected"},
        {"Quoted", "Quoted"}, {"Ordered", "Converted"}}

    Private Class ReqRow
        Public RequestId As Integer
        Public CreatedAt As DateTime
        Public Status As String = ""
        Public RequestedById As Integer
        Public RequestedBy As String = ""
        Public Purpose As String = ""
        Public ItemCount As Integer
        Public Units As Integer
        Public EstTotal As Decimal
    End Class

    ' ── STATE ─────────────────────────────────────────────────────────────────
    Private Shared _container As Panel
    Private Shared _scrollHost As Panel
    Private Shared _fromDate As Date
    Private Shared _toDate As Date
    Private Shared _search As String = ""
    Private Shared _status As String = ""          ' stored value, "" = all
    Private Shared _rows As New List(Of ReqRow)
    Private Shared _pageSize As Integer = MAX_PAGE_SIZE
    Private Shared _currentPage As Integer = 0
    Private Shared _totalRows As Integer = 0
    Private Shared _nPending, _nApproved, _nRejected As Integer
    Private Shared _estOpen As Decimal
    Private Shared _reflowAttached As Boolean = False
    Private Shared _loadFailed As Boolean = False

    Private Shared _lblPending, _lblApproved, _lblRejected, _lblValue As Label
    Private Shared _lblPageInfo As Label
    Private Shared _btnPrev, _btnNext As Button
    Private Shared _searchTimer As Timer

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Shared Sub LoadRequests(container As Panel)
        _container = container
        _toDate = Date.Today
        ' The whole year so far: a request waiting from last month must not
        ' drop out of sight just because the month changed.
        _fromDate = New Date(Date.Today.Year, 1, 1)
        _search = ""
        _status = ""
        _currentPage = 0

        ComputePageSize()
        LoadData()
        Render()
    End Sub

    Private Shared Sub ComputePageSize()
        _pageSize = MAX_PAGE_SIZE
        If _container Is Nothing Then Return
        Dim viewport As Integer = _container.ClientSize.Height - CHROME_H
        If viewport < 120 Then Return
        _pageSize = Math.Max(4, Math.Min(MAX_PAGE_SIZE, (viewport - 4) \ ROW_H))
    End Sub

    Private Shared Sub ReQuery(Optional resetPage As Boolean = True)
        If resetPage Then _currentPage = 0
        LoadData()
        PopulateRows()
        UpdateSummaryLabels()
        UpdatePager()
    End Sub

    ' =========================================================================
    '  DATA
    ' =========================================================================
    Private Shared Function WhereClause(includeStatus As Boolean) As String
        Dim sb As New System.Text.StringBuilder("WHERE pr.created_at >= @fromD AND pr.created_at < @toD ")
        If includeStatus AndAlso _status <> "" Then sb.Append("AND pr.status = @st ")
        If _search <> "" Then
            sb.Append("AND (CONCAT('REQ-', LPAD(pr.request_id, 4, '0')) LIKE @q " &
                      "OR COALESCE(u.full_name, '') LIKE @q OR COALESCE(pr.note, '') LIKE @q " &
                      "OR pr.status LIKE @q " &
                      "OR EXISTS (SELECT 1 FROM purchase_request_items si JOIN products sp ON sp.product_id = si.product_id " &
                      "           WHERE si.request_id = pr.request_id AND (sp.name LIKE @q OR sp.sku LIKE @q))")
            sb.Append(If(_hasPurpose, " OR COALESCE(pr.purpose, '') LIKE @q) ", ") "))
        End If
        Return sb.ToString()
    End Function

    Private Shared _hasPurpose As Boolean
    Private Shared _hasLinePrice As Boolean

    Private Shared Sub AddParams(cmd As MySqlCommand)
        cmd.Parameters.AddWithValue("@fromD", _fromDate.Date)
        cmd.Parameters.AddWithValue("@toD", _toDate.Date.AddDays(1))    ' whole last day
        cmd.Parameters.AddWithValue("@st", _status)
        cmd.Parameters.AddWithValue("@q", "%" & _search & "%")
    End Sub

    Private Shared Sub LoadData()
        _rows.Clear()
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                _hasPurpose = SupplierPayments.ColumnExists(conn, "purchase_requests", "purpose")
                _hasLinePrice = SupplierPayments.ColumnExists(conn, "purchase_request_items", "unit_price")
                Const FROM_SQL As String =
                    "FROM purchase_requests pr LEFT JOIN users u ON u.user_id = pr.requested_by "
                Dim estSql As String =
                    "(SELECT COALESCE(SUM(pri.qty_requested * " & RequestReviewModal.EstPriceSql(_hasLinePrice) & "), 0) " &
                    " FROM purchase_request_items pri JOIN products p ON p.product_id = pri.product_id " &
                    " WHERE pri.request_id = pr.request_id)"

                ' Summary cards follow the dates and search, not the Status pick,
                ' so they keep describing the whole period.
                Using cmd As New MySqlCommand(
                    "SELECT SUM(pr.status = 'Pending') AS n_pend, SUM(pr.status = 'Approved') AS n_appr, " &
                    "SUM(pr.status = 'Rejected') AS n_rej, " &
                    "COALESCE(SUM(CASE WHEN pr.status IN ('Pending','Approved','Quoted') THEN " & estSql & " END), 0) AS est_open " &
                    FROM_SQL & WhereClause(False), conn)
                    AddParams(cmd)
                    Using rdr = cmd.ExecuteReader()
                        If rdr.Read() Then
                            _nPending = SafeInt(rdr("n_pend"))
                            _nApproved = SafeInt(rdr("n_appr"))
                            _nRejected = SafeInt(rdr("n_rej"))
                            _estOpen = SafeDec(rdr("est_open"))
                        End If
                    End Using
                End Using

                Using cmd As New MySqlCommand("SELECT COUNT(*) " & FROM_SQL & WhereClause(True), conn)
                    AddParams(cmd)
                    _totalRows = Convert.ToInt32(cmd.ExecuteScalar())
                End Using

                Dim totalPages As Integer = Math.Max(1, CInt(Math.Ceiling(_totalRows / _pageSize)))
                If _currentPage > totalPages - 1 Then _currentPage = totalPages - 1
                If _currentPage < 0 Then _currentPage = 0

                Using cmd As New MySqlCommand(
                    "SELECT pr.request_id, pr.created_at, pr.status, pr.requested_by, " &
                    "COALESCE(u.full_name, 'Unknown') AS reqby, COALESCE(pr.note, '') AS note, " &
                    If(_hasPurpose, "COALESCE(pr.purpose, '')", "''") & " AS purpose, " &
                    "(SELECT COUNT(*) FROM purchase_request_items pri WHERE pri.request_id = pr.request_id) AS n_items, " &
                    "(SELECT COALESCE(SUM(pri.qty_requested), 0) FROM purchase_request_items pri " &
                    " WHERE pri.request_id = pr.request_id) AS n_units, " &
                    estSql & " AS est_total " &
                    FROM_SQL & WhereClause(True) &
                    "ORDER BY pr.created_at DESC, pr.request_id DESC LIMIT @lim OFFSET @off", conn)
                    AddParams(cmd)
                    cmd.Parameters.AddWithValue("@lim", _pageSize)
                    cmd.Parameters.AddWithValue("@off", _currentPage * _pageSize)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            Dim purpose As String = rdr("purpose").ToString()
                            If purpose = "" Then purpose = RequestReviewModal.TypedNote(rdr("note").ToString())
                            _rows.Add(New ReqRow With {
                                .RequestId = SafeInt(rdr("request_id")),
                                .CreatedAt = Convert.ToDateTime(rdr("created_at")),
                                .Status = rdr("status").ToString(),
                                .RequestedById = SafeInt(rdr("requested_by")),
                                .RequestedBy = rdr("reqby").ToString(),
                                .Purpose = purpose,
                                .ItemCount = SafeInt(rdr("n_items")),
                                .Units = SafeInt(rdr("n_units")),
                                .EstTotal = SafeDec(rdr("est_total"))
                            })
                        End While
                    End Using
                End Using
            End Using
            _loadFailed = False
        Catch ex As Exception
            _totalRows = 0
            _nPending = 0 : _nApproved = 0 : _nRejected = 0 : _estOpen = 0D
            If Not _loadFailed Then
                AppModal.Show("Could not load purchase requests:" & Environment.NewLine & ex.Message,
                              "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End If
            _loadFailed = True
        End Try
    End Sub

    Private Shared Sub UpdateSummaryLabels()
        If _lblPending IsNot Nothing Then _lblPending.Text = _nPending.ToString("N0")
        If _lblApproved IsNot Nothing Then _lblApproved.Text = _nApproved.ToString("N0")
        If _lblRejected IsNot Nothing Then _lblRejected.Text = _nRejected.ToString("N0")
        If _lblValue IsNot Nothing Then _lblValue.Text = Money(_estOpen)
    End Sub

    ' =========================================================================
    '  RENDER
    ' =========================================================================
    Private Shared Sub Render()
        _container.SuspendLayout()
        _container.Controls.Clear()
        _reflowAttached = False
        _container.BackColor = MintBg

        Dim tableWrapper As New Panel With {.Dock = DockStyle.Fill, .BackColor = MintBg}
        Dim listArea As New BufferedPanel With {
            .Dock = DockStyle.Fill, .BackColor = CardBg, .Padding = New Padding(1)
        }
        AddHandler listArea.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)

        _scrollHost = New Panel With {
            .Dock = DockStyle.Fill, .BackColor = Color.Transparent, .AutoScroll = True
        }
        Dim colHdr As Panel = BuildColHeader()
        colHdr.Dock = DockStyle.Top
        Dim pagerBar As Panel = BuildPagerBar()
        pagerBar.Dock = DockStyle.Bottom
        Dim bottomPad As New Panel With {.Dock = DockStyle.Bottom, .Height = 12, .BackColor = Color.Transparent}

        listArea.Controls.Add(bottomPad)
        listArea.Controls.Add(pagerBar)
        listArea.Controls.Add(colHdr)
        listArea.Controls.Add(_scrollHost)
        colHdr.BringToFront()
        _scrollHost.BringToFront()
        _scrollHost.AutoScrollMinSize = New Size(0, 1)

        tableWrapper.Controls.Add(listArea)
        tableWrapper.Controls.Add(New Panel With {.Dock = DockStyle.Bottom, .Height = 16, .BackColor = MintBg})
        tableWrapper.Controls.Add(New Panel With {.Dock = DockStyle.Top, .Height = 16, .BackColor = MintBg})
        tableWrapper.Controls.Add(New Panel With {.Dock = DockStyle.Left, .Width = SIDE_PAD, .BackColor = MintBg})
        tableWrapper.Controls.Add(New Panel With {.Dock = DockStyle.Right, .Width = SIDE_PAD, .BackColor = MintBg})

        Dim summary As Panel = BuildSummaryStrip()
        summary.Dock = DockStyle.Top
        Dim topRegion As Panel = BuildTopRegion()
        topRegion.Dock = DockStyle.Top

        _container.Controls.Add(tableWrapper)
        _container.Controls.Add(summary)
        _container.Controls.Add(topRegion)
        _container.ResumeLayout()

        PopulateRows()
        UpdateSummaryLabels()
        UpdatePager()
    End Sub

    ' =========================================================================
    '  TOP REGION — title + New Request, then search and filters
    ' =========================================================================
    Private Shared Function BuildTopRegion() As Panel
        Dim region As New Panel With {.BackColor = MintBg, .Height = 128}

        Dim titleBar As New Panel With {.Height = 72, .Dock = DockStyle.Top, .BackColor = Color.Transparent}
        titleBar.Controls.Add(New Label With {
            .Text = "Purchase Requests",
            .Font = New Font("Segoe UI", 28, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True,
            .Location = New Point(SIDE_PAD, 16)
        })

        Dim btnNew As Button = MakeActionButton("+  New Request", 160, MintPrimary, MintDeep)
        AddHandler btnNew.Click,
            Sub()
                If PurchaseRequestModal.Show() Then ReQuery()
            End Sub
        AddHandler titleBar.Resize,
            Sub()
                btnNew.Location = New Point(titleBar.Width - btnNew.Width - SIDE_PAD,
                                            (titleBar.Height - btnNew.Height) \ 2 + 2)
            End Sub
        titleBar.Controls.Add(btnNew)

        ' ── Filter bar ──────────────────────────────────────────────────────
        Dim filterBar As New Panel With {.Height = 56, .Dock = DockStyle.Top, .BackColor = Color.Transparent}
        Dim x As Integer = SIDE_PAD
        Const TOP_Y As Integer = 11

        Dim searchWrap As Panel = BuildSearchBox()
        searchWrap.Location = New Point(x, TOP_Y)
        filterBar.Controls.Add(searchWrap)
        x += searchWrap.Width + 24

        Dim addLabel As Action(Of String) =
            Sub(text As String)
                Dim l As New Label With {
                    .Text = text, .Font = New Font("Segoe UI", 9.5F), .ForeColor = SubText,
                    .AutoSize = True, .BackColor = Color.Transparent
                }
                l.Location = New Point(x, TOP_Y + (FilterFields.FIELD_H - l.PreferredHeight) \ 2)
                filterBar.Controls.Add(l)
                x += l.PreferredWidth + 8
            End Sub

        addLabel("From")
        Dim setFrom As Action(Of Date) = Nothing, setTo As Action(Of Date) = Nothing
        Dim boxFrom As Panel = FilterFields.MakeDateField(136, _fromDate,
            Sub(d As Date)
                _fromDate = d.Date
                If _fromDate > _toDate Then
                    _toDate = _fromDate
                    setTo(_toDate)
                End If
                ReQuery()
            End Sub, setFrom)
        boxFrom.Location = New Point(x, TOP_Y)
        filterBar.Controls.Add(boxFrom)
        x += boxFrom.Width + 16

        addLabel("To")
        Dim boxTo As Panel = FilterFields.MakeDateField(136, _toDate,
            Sub(d As Date)
                _toDate = d.Date
                If _toDate < _fromDate Then
                    _fromDate = _toDate
                    setFrom(_fromDate)
                End If
                ReQuery()
            End Sub, setTo)
        boxTo.Location = New Point(x, TOP_Y)
        filterBar.Controls.Add(boxTo)
        x += boxTo.Width + 24

        addLabel("Status")
        Dim names As New List(Of String) From {ALL_STATUSES}
        For i As Integer = 0 To StatusFilters.GetLength(0) - 1
            names.Add(StatusFilters(i, 0))
        Next
        Dim boxStatus As Panel = FilterFields.MakeSelectField(names, 160, 0,
            Sub(i As Integer)
                _status = If(i >= 1 AndAlso i <= StatusFilters.GetLength(0), StatusFilters(i - 1, 1), "")
                ReQuery()
            End Sub)
        boxStatus.Location = New Point(x, TOP_Y)
        filterBar.Controls.Add(boxStatus)

        region.Controls.Add(filterBar)
        region.Controls.Add(titleBar)
        Return region
    End Function

    Private Shared Function BuildSearchBox() As Panel
        Dim searchWrap As New Panel With {.Size = New Size(300, FilterFields.FIELD_H), .BackColor = CardBg, .Cursor = Cursors.IBeam}
        Dim focused As Boolean = False
        AddHandler searchWrap.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim rc = searchWrap.ClientRectangle : rc.Inflate(-1, -1)
                Using b As New SolidBrush(CardBg)
                    e.Graphics.FillRoundedRectangle(b, rc, 8)
                End Using
                Using p As New Pen(If(focused, MintPrimary, Theme.Border), If(focused, 1.8F, 1.0F))
                    e.Graphics.DrawRoundedRectangle(p, rc, 8)
                End Using
                Using p As New Pen(Color.FromArgb(160, 180, 178), 1.4F)
                    e.Graphics.DrawEllipse(p, 10, 10, 12, 12)
                    e.Graphics.DrawLine(p, 20, 20, 25, 25)
                End Using
            End Sub

        Dim txt As New TextBox With {
            .Font = New Font("Segoe UI", 10), .BorderStyle = BorderStyle.None,
            .BackColor = CardBg, .ForeColor = SubText, .Text = SEARCH_PLACEHOLDER,
            .Location = New Point(34, 9), .Width = 254
        }

        ' Debounced: every keystroke would otherwise run three queries.
        If _searchTimer IsNot Nothing Then
            _searchTimer.Stop()
            _searchTimer.Dispose()
        End If
        _searchTimer = New Timer With {.Interval = 350}
        AddHandler _searchTimer.Tick,
            Sub()
                _searchTimer.Stop()
                ReQuery()
            End Sub

        AddHandler txt.Enter,
            Sub()
                focused = True
                If txt.Text = SEARCH_PLACEHOLDER Then
                    txt.Text = ""
                    txt.ForeColor = Theme.TextDark
                End If
                searchWrap.Invalidate()
            End Sub
        AddHandler txt.Leave,
            Sub()
                focused = False
                If String.IsNullOrWhiteSpace(txt.Text) Then
                    txt.Text = SEARCH_PLACEHOLDER
                    txt.ForeColor = SubText
                End If
                searchWrap.Invalidate()
            End Sub
        AddHandler txt.TextChanged,
            Sub()
                If txt.Text = SEARCH_PLACEHOLDER Then Return
                _search = txt.Text.Trim()
                _searchTimer.Stop()
                _searchTimer.Start()
            End Sub
        AddHandler searchWrap.Click, Sub() txt.Focus()
        searchWrap.Controls.Add(txt)
        Return searchWrap
    End Function

    ' =========================================================================
    '  SUMMARY STRIP
    ' =========================================================================
    Private Shared Function BuildSummaryStrip() As Panel
        Dim bar As New Panel With {
            .Height = SUMMARY_H + 12, .BackColor = MintBg,
            .Padding = New Padding(SIDE_PAD, 0, SIDE_PAD, 12)
        }
        Dim grid As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 4, .RowCount = 1,
            .BackColor = Color.Transparent
        }
        For i As Integer = 0 To 3
            grid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 25.0F))
        Next
        grid.RowStyles.Add(New RowStyle(SizeType.Percent, 100))

        Dim c1 = MakeSummaryCard("Waiting for Approval", AccentAmber, "sent to the admin", _lblPending)
        Dim c2 = MakeSummaryCard("Approved", MintDeep, "ready to order", _lblApproved)
        Dim c3 = MakeSummaryCard("Rejected", AccentRed, "reason shown on the request", _lblRejected)
        Dim c4 = MakeSummaryCard("Estimated Value", MintPrimary, "waiting, approved or quoted", _lblValue)
        c1.Margin = New Padding(0, 0, 12, 0)
        c2.Margin = New Padding(0, 0, 12, 0)
        c3.Margin = New Padding(0, 0, 12, 0)
        c4.Margin = New Padding(0)
        grid.Controls.Add(c1, 0, 0)
        grid.Controls.Add(c2, 1, 0)
        grid.Controls.Add(c3, 2, 0)
        grid.Controls.Add(c4, 3, 0)
        bar.Controls.Add(grid)
        Return bar
    End Function

    Private Shared Function MakeSummaryCard(caption As String, valueClr As Color, subLabel As String,
                                            ByRef valueLabel As Label) As Panel
        Dim card As New BufferedPanel With {.Dock = DockStyle.Fill, .BackColor = CardBg}
        AddHandler card.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)
        card.Controls.Add(New Label With {
            .Text = caption, .Font = New Font("Segoe UI", 11), .ForeColor = SubText,
            .AutoSize = True, .Location = New Point(22, 14), .BackColor = Color.Transparent
        })
        valueLabel = New Label With {
            .Text = "0", .Font = New Font("Segoe UI", 24, FontStyle.Bold), .ForeColor = valueClr,
            .AutoSize = True, .Location = New Point(20, 38), .BackColor = Color.Transparent
        }
        card.Controls.Add(valueLabel)
        Dim lblSub As New Label With {
            .Text = subLabel, .Font = New Font("Segoe UI", 10), .ForeColor = SubText,
            .AutoSize = False, .AutoEllipsis = True, .Height = 22,
            .Location = New Point(22, 100), .BackColor = Color.Transparent
        }
        card.Controls.Add(lblSub)
        AddHandler card.Resize, Sub() lblSub.Width = Math.Max(40, card.Width - 36)
        Return card
    End Function

    ' =========================================================================
    '  COLUMN HEADER — same x positions as BuildDataRow
    ' =========================================================================
    Private Shared Function BuildColHeader() As Panel
        Dim hdr As New Panel With {.Height = 44, .BackColor = Color.Transparent}
        Dim labels As Label() = {
            MakeColLabel("DATE"), MakeColLabel("REQUEST NO."), MakeColLabel("REQUESTED BY"),
            MakeColLabel("PURPOSE / ITEMS"), MakeColLabel("EST. TOTAL"), MakeColLabel("STATUS"),
            MakeColLabel("ACTION")
        }
        hdr.Controls.AddRange(labels)
        AddHandler hdr.Resize,
            Sub()
                Dim w = hdr.Width
                labels(0).Left = 24
                labels(1).Left = CInt(w * X_REF)
                labels(2).Left = CInt(w * X_BY)
                labels(3).Left = CInt(w * X_PURPOSE)
                labels(4).Left = CInt(w * X_TOTAL_END) - labels(4).Width
                labels(5).Left = CInt(w * X_STATUS)
                labels(6).Left = CInt(w * X_ACTION)
            End Sub
        AddHandler hdr.Paint,
            Sub(s, e)
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 20, hdr.Height - 1, hdr.Width - 20, hdr.Height - 1)
                End Using
            End Sub
        Return hdr
    End Function

    Private Shared Function MakeColLabel(text As String) As Label
        Return New Label With {
            .Text = text, .Font = New Font("Segoe UI", 10, FontStyle.Bold), .ForeColor = SubText,
            .AutoSize = True, .Location = New Point(0, 13), .BackColor = Color.Transparent
        }
    End Function

    ' =========================================================================
    '  ROWS
    ' =========================================================================
    Private Shared Sub PopulateRows()
        If _scrollHost Is Nothing Then Return
        _scrollHost.SuspendLayout()
        For Each c As Control In _scrollHost.Controls.Cast(Of Control).ToList()
            c.Dispose()
        Next
        _scrollHost.Controls.Clear()

        If _reflowAttached Then
            RemoveHandler _scrollHost.Resize, AddressOf ReflowScrollHost
            _reflowAttached = False
        End If

        If _rows.Count = 0 Then
            _scrollHost.Controls.Add(BuildEmptyRow(
                If(_loadFailed, "The requests could not be loaded.",
                   "No purchase requests match these filters. Use + New Request to send one.")))
        Else
            Dim y As Integer = 4
            For Each r In _rows
                Dim row As Panel = BuildDataRow(r)
                row.Location = New Point(0, y)
                row.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
                _scrollHost.Controls.Add(row)
                row.Width = _scrollHost.ClientSize.Width - 4
                y += ROW_H
            Next
            _reflowAttached = True
            AddHandler _scrollHost.Resize, AddressOf ReflowScrollHost
        End If

        _scrollHost.ResumeLayout()
        _scrollHost.Invalidate(True)
    End Sub

    Private Shared Sub ReflowScrollHost(sender As Object, e As EventArgs)
        If _scrollHost Is Nothing Then Return
        Dim w As Integer = _scrollHost.ClientSize.Width - 4
        For Each c As Control In _scrollHost.Controls
            c.Width = w
        Next
    End Sub

    Private Shared Function StatusLabel(status As String) As String
        Return If(status = "Converted", "Ordered", status)
    End Function

    Private Shared Function BuildDataRow(r As ReqRow) As Panel
        Dim p As New BufferedPanel With {.Height = ROW_H, .BackColor = Color.Transparent, .Cursor = Cursors.Hand}
        AddHandler p.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                If TypeOf p.Tag Is Boolean AndAlso CBool(p.Tag) Then
                    Using br As New SolidBrush(RowHover)
                        e.Graphics.FillRoundedRectangle(br, New Rectangle(8, 3, p.Width - 16, ROW_H - 6), 10)
                    End Using
                End If
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 20, p.Height - 1, p.Width - 20, p.Height - 1)
                End Using
            End Sub

        Dim mk = Function(text As String, fnt As Font, clr As Color) As Label
                     Return New Label With {
                         .Text = text, .Font = fnt, .ForeColor = clr, .AutoSize = True,
                         .BackColor = Color.Transparent, .UseMnemonic = False
                     }
                 End Function

        Dim lblDate As Label = mk(r.CreatedAt.ToString("MMM d, yyyy"), New Font("Segoe UI", 10.5F), Theme.TextDark)
        Dim lblTime As Label = mk(r.CreatedAt.ToString("h:mm tt"), New Font("Segoe UI", 9), FaintText)
        Dim lblRef As Label = mk(RequestReviewModal.RequestNo(r.RequestId),
                                 New Font("Segoe UI", 10.5F, FontStyle.Bold Or FontStyle.Underline), MintPrimary)
        Dim lblBy As Label = mk(r.RequestedBy, New Font("Segoe UI", 10), SubText)
        lblBy.AutoSize = False : lblBy.AutoEllipsis = True : lblBy.Height = 20

        Dim lblPurpose As Label = mk(If(r.Purpose = "", "No purpose written", r.Purpose),
                                     New Font("Segoe UI Semibold", 10),
                                     If(r.Purpose = "", FaintText, MintDark))
        lblPurpose.AutoSize = False : lblPurpose.AutoEllipsis = True : lblPurpose.Height = 20
        Dim lblItems As Label = mk(r.ItemCount & If(r.ItemCount = 1, " product", " products") & ",  " &
                                   r.Units.ToString("N0") & If(r.Units = 1, " unit", " units"),
                                   New Font("Segoe UI", 9), FaintText)

        Dim lblTotal As Label = mk(Money(r.EstTotal), New Font("Segoe UI", 11, FontStyle.Bold), MintDark)

        Dim bc = RequestReviewModal.BadgeColors(r.Status)
        Dim badge As Label = mk(StatusLabel(r.Status), New Font("Segoe UI", 8.5F, FontStyle.Bold), bc.fore)
        badge.BackColor = bc.back
        badge.Padding = New Padding(9, 4, 9, 4)
        RoundLabel(badge, 10)

        p.Controls.AddRange({lblDate, lblTime, lblRef, lblBy, lblPurpose, lblItems, lblTotal, badge})

        ' ── Actions ─────────────────────────────────────────────────────────
        Dim actions As New List(Of Label)
        Dim isMine As Boolean = (r.RequestedById = Session.CurrentUserId)
        If r.Status = "Approved" Then
            Dim lnkPo As Label = MakeLink("Create PO", MintPrimary)
            AddHandler lnkPo.Click,
                Sub()
                    Dim frm = TryCast(p.FindForm(), PROCUREMENT_STAFF)
                    If frm IsNot Nothing Then frm.OpenPOWithRequest(r.RequestId)
                End Sub
            actions.Add(lnkPo)
        ElseIf r.Status = "Pending" AndAlso isMine Then
            Dim lnkEdit As Label = MakeLink("Edit", MintPrimary)
            AddHandler lnkEdit.Click,
                Sub()
                    If PurchaseRequestModal.Edit(r.RequestId) Then ReQuery(resetPage:=False)
                End Sub
            Dim lnkRemove As Label = MakeLink("Remove", AccentRed)
            AddHandler lnkRemove.Click,
                Sub()
                    If MyRequestsModal.RemoveRequest(r.RequestId, r.ItemCount) Then ReQuery(resetPage:=False)
                End Sub
            actions.Add(lnkEdit)
            actions.Add(lnkRemove)
        End If
        For Each a In actions
            p.Controls.Add(a)
        Next

        Dim tip As New ToolTip()
        tip.SetToolTip(lblPurpose, r.Purpose)
        tip.SetToolTip(lblRef, "Show " & RequestReviewModal.RequestNo(r.RequestId))
        AddHandler p.Disposed, Sub() tip.Dispose()

        AddHandler p.Resize,
            Sub()
                Dim w = p.Width
                Dim top1 As Integer = (ROW_H \ 2) - 19
                Dim top2 As Integer = (ROW_H \ 2) + 1
                Dim mid As Integer = (ROW_H - 20) \ 2
                lblDate.Location = New Point(24, top1)
                lblTime.Location = New Point(24, top2)
                lblRef.Location = New Point(CInt(w * X_REF), (ROW_H - lblRef.Height) \ 2)

                Dim xBy As Integer = CInt(w * X_BY)
                Dim xPur As Integer = CInt(w * X_PURPOSE)
                Dim xEnd As Integer = CInt(w * X_TOTAL_END)
                lblBy.Location = New Point(xBy, mid)
                lblBy.Width = Math.Max(40, xPur - xBy - 12)
                lblTotal.Location = New Point(xEnd - lblTotal.Width, (ROW_H - lblTotal.Height) \ 2)
                lblPurpose.Location = New Point(xPur, top1)
                lblPurpose.Width = Math.Max(40, lblTotal.Left - xPur - 20)
                lblItems.Location = New Point(xPur, top2 + 1)
                badge.Location = New Point(CInt(w * X_STATUS), (ROW_H - badge.Height) \ 2)

                Dim ax As Integer = CInt(w * X_ACTION)
                For Each a In actions
                    a.Location = New Point(ax, (ROW_H - a.Height) \ 2)
                    ax += a.Width + 14
                Next
            End Sub

        ' Row click shows the request; the action links do their own thing.
        Dim open As Action = Sub() RequestReviewModal.Show(r.RequestId)
        AddHandler p.Click, Sub() open()
        For Each ctrl As Control In p.Controls
            If actions.Contains(TryCast(ctrl, Label)) Then Continue For
            AddHandler ctrl.Click, Sub() open()
            ctrl.Cursor = Cursors.Hand
        Next

        PropagateHover(p)
        Return p
    End Function

    Private Shared Function MakeLink(text As String, clr As Color) As Label
        Dim lbl As New Label With {
            .Text = text, .Font = New Font("Segoe UI Semibold", 10), .ForeColor = clr,
            .AutoSize = True, .Cursor = Cursors.Hand, .BackColor = Color.Transparent
        }
        AddHandler lbl.MouseEnter, Sub() lbl.Font = New Font("Segoe UI Semibold", 10, FontStyle.Underline)
        AddHandler lbl.MouseLeave, Sub() lbl.Font = New Font("Segoe UI Semibold", 10)
        Return lbl
    End Function

    Private Shared Function BuildEmptyRow(message As String) As Panel
        Dim p As New Panel With {.Height = 90, .Dock = DockStyle.Top, .BackColor = Color.Transparent}
        p.Controls.Add(New Label With {
            .Text = message, .Font = New Font("Segoe UI", 10.5F), .ForeColor = FaintText,
            .AutoSize = True, .Location = New Point(22, 28), .BackColor = Color.Transparent
        })
        Return p
    End Function

    ' =========================================================================
    '  PAGER
    ' =========================================================================
    Private Shared Function BuildPagerBar() As Panel
        Dim wrapper As New Panel With {.Height = 52, .BackColor = Color.Transparent}
        AddHandler wrapper.Paint,
            Sub(s, e)
                Using dp As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(dp, 0, 0, wrapper.Width, 0)
                End Using
            End Sub

        _btnPrev = MakePagerButton("‹")
        _btnNext = MakePagerButton("›")
        _lblPageInfo = New Label With {
            .Font = New Font("Segoe UI", 9.5F), .ForeColor = SubText,
            .AutoSize = True, .BackColor = Color.Transparent
        }

        Dim place As Action =
            Sub()
                Dim cx As Integer = wrapper.Width \ 2
                _btnPrev.Left = cx - _btnPrev.Width - _lblPageInfo.Width \ 2 - 8
                _btnPrev.Top = (wrapper.Height - _btnPrev.Height) \ 2
                _lblPageInfo.Left = cx - _lblPageInfo.Width \ 2
                _lblPageInfo.Top = (wrapper.Height - _lblPageInfo.Height) \ 2
                _btnNext.Left = cx + _lblPageInfo.Width \ 2 + 8
                _btnNext.Top = (wrapper.Height - _btnNext.Height) \ 2
            End Sub
        AddHandler wrapper.Resize, Sub() place()
        AddHandler _lblPageInfo.SizeChanged, Sub() place()

        AddHandler _btnPrev.Click,
            Sub()
                If _currentPage > 0 Then
                    _currentPage -= 1
                    ReQuery(resetPage:=False)
                End If
            End Sub
        AddHandler _btnNext.Click,
            Sub()
                Dim totalPages As Integer = Math.Max(1, CInt(Math.Ceiling(_totalRows / _pageSize)))
                If _currentPage < totalPages - 1 Then
                    _currentPage += 1
                    ReQuery(resetPage:=False)
                End If
            End Sub

        wrapper.Controls.AddRange({_btnPrev, _lblPageInfo, _btnNext})
        Return wrapper
    End Function

    Private Shared Function MakePagerButton(glyph As String) As Button
        Dim b As New Button With {
            .Text = glyph, .FlatStyle = FlatStyle.Flat, .Cursor = Cursors.Hand,
            .Size = New Size(32, 32), .BackColor = Theme.Border,
            .ForeColor = MintDark, .Font = New Font("Segoe UI", 14, FontStyle.Bold),
            .TextAlign = ContentAlignment.MiddleCenter
        }
        b.FlatAppearance.BorderSize = 0
        Return b
    End Function

    Private Shared Sub UpdatePager()
        If _lblPageInfo Is Nothing Then Return
        Dim totalPages As Integer = Math.Max(1, CInt(Math.Ceiling(_totalRows / _pageSize)))
        If _totalRows = 0 Then
            _lblPageInfo.Text = "No requests"
        Else
            Dim firstRow As Integer = _currentPage * _pageSize + 1
            Dim lastRow As Integer = Math.Min(_totalRows, firstRow + _rows.Count - 1)
            _lblPageInfo.Text =
                $"Showing {firstRow:N0}–{lastRow:N0} of {_totalRows:N0}   ·   Page {_currentPage + 1} of {totalPages}"
        End If
        _btnPrev.Enabled = (_currentPage > 0)
        _btnNext.Enabled = (_currentPage < totalPages - 1)
    End Sub

    ' =========================================================================
    '  HELPERS
    ' =========================================================================
    Private Shared Function Money(v As Decimal) As String
        Return Peso & v.ToString("N2", CultureInfo.InvariantCulture)
    End Function

    Private Shared Function SafeInt(o As Object) As Integer
        If o Is Nothing OrElse IsDBNull(o) Then Return 0
        Return Convert.ToInt32(o)
    End Function

    Private Shared Function SafeDec(o As Object) As Decimal
        If o Is Nothing OrElse IsDBNull(o) Then Return 0D
        Return Convert.ToDecimal(o)
    End Function

    Private Shared Function MakeActionButton(text As String, width As Integer,
                                             rest As Color, hover As Color) As Button
        Dim b As New Button With {
            .Text = text, .FlatStyle = FlatStyle.Flat, .Font = New Font("Segoe UI Semibold", 10),
            .ForeColor = Color.White, .BackColor = rest, .Size = New Size(width, 40),
            .Cursor = Cursors.Hand, .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        b.FlatAppearance.BorderSize = 0
        AddHandler b.Paint,
            Sub(s, e)
                Dim bb = CType(s, Button)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Using bg As New SolidBrush(bb.Parent.BackColor)
                    e.Graphics.FillRectangle(bg, bb.ClientRectangle)
                End Using
                Dim rect = bb.ClientRectangle : rect.Inflate(-1, -1)
                Using br As New SolidBrush(bb.BackColor)
                    e.Graphics.FillRoundedRectangle(br, rect, 8)
                End Using
                TextRenderer.DrawText(e.Graphics, bb.Text, bb.Font, bb.ClientRectangle,
                    bb.ForeColor, TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub
        AddHandler b.MouseEnter, Sub()
                                     b.BackColor = hover
                                     b.Invalidate()
                                 End Sub
        AddHandler b.MouseLeave, Sub()
                                     b.BackColor = rest
                                     b.Invalidate()
                                 End Sub
        Return b
    End Function

    Private Shared Sub RoundLabel(lbl As Label, radius As Integer)
        Dim apply As Action =
            Sub()
                If lbl.Width <= radius OrElse lbl.Height <= radius Then Return
                Dim path As New GraphicsPath()
                path.AddArc(0, 0, radius, radius, 180, 90)
                path.AddArc(lbl.Width - radius, 0, radius, radius, 270, 90)
                path.AddArc(lbl.Width - radius, lbl.Height - radius, radius, radius, 0, 90)
                path.AddArc(0, lbl.Height - radius, radius, radius, 90, 90)
                path.CloseFigure()
                lbl.Region = New Region(path)
            End Sub
        AddHandler lbl.Resize, Sub() apply()
        apply()
    End Sub

    Private Shared Sub PropagateHover(row As Panel)
        AddHandler row.MouseEnter, Sub() SetRowHover(row, True)
        AddHandler row.MouseLeave, Sub() SetRowHover(row, False)
        For Each ctrl As Control In row.Controls
            AddHandler ctrl.MouseEnter, Sub() SetRowHover(row, True)
            AddHandler ctrl.MouseLeave,
                Sub()
                    Dim pos = row.PointToClient(Cursor.Position)
                    SetRowHover(row, row.ClientRectangle.Contains(pos))
                End Sub
        Next
    End Sub

    Private Shared Sub SetRowHover(row As Panel, on_ As Boolean)
        row.Tag = on_
        row.Invalidate()
    End Sub

End Class
