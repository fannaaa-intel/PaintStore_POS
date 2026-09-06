Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports MySql.Data.MySqlClient

' =============================================================================
'  AdminSalesOverviewPanelBuilder  —  Admin ▸ Sales Overview  (READ-ONLY)
'
'  Store-wide oversight: every sale, every cashier, every shift. The POS and
'  Accountant sales screens are scoped views; this one has no scope filter of
'  its own beyond what the admin picks.
'
'  LAYOUT:
'    TopRegion (Dock = Top, 128px):
'      ├─ TitleBar  (72px) : title + search + "Read-only" pill + Refresh
'      └─ FilterBar (56px) : From / To + Cashier + Status
'    SummaryStrip (Dock = Top, 110px) : 4 cards over the FILTERED set
'    TableWrapper (Dock = Fill, 24px side spacers + 16px top/bottom):
'      └─ listArea (BufferedPanel, PanelRenderer card):
'           ├─ ColHeader   (Dock = Top,    44px)
'           ├─ _scrollHost (Dock = Fill,   AutoScroll)
'           ├─ bottomPad   (Dock = Bottom, 12px)
'           └─ pagerBar    (Dock = Bottom, 52px)
'
'  Paging is done in SQL (COUNT + LIMIT/OFFSET) and the page size is computed
'  from the viewport, so a full page fits without scrolling.
'
'  The summary strip aggregates the WHOLE filtered set in its own query — not
'  the current page — so the totals do not change as you page through.
' =============================================================================
Public Class AdminSalesOverviewPanelBuilder

    ' ── PALETTE ───────────────────────────────────────────────────────────────
    Private Shared ReadOnly MintDark As Color = Color.FromArgb(10, 79, 66)
    Private Shared ReadOnly MintPrimary As Color = Color.FromArgb(0, 168, 142)
    Private Shared ReadOnly MintDeep As Color = Color.FromArgb(0, 130, 109)
    Private Shared ReadOnly MintBg As Color = Color.FromArgb(244, 248, 246)
    Private Shared ReadOnly CardBg As Color = Color.White
    Private Shared ReadOnly SubText As Color = Color.FromArgb(90, 114, 107)
    Private Shared ReadOnly FaintText As Color = Color.FromArgb(150, 165, 160)
    Private Shared ReadOnly RowDivider As Color = Color.FromArgb(238, 242, 240)
    Private Shared ReadOnly RowHover As Color = Color.FromArgb(224, 242, 235)
    Private Shared ReadOnly AccentRed As Color = Color.FromArgb(210, 60, 60)
    Private Shared ReadOnly Peso As String = ChrW(8369)

    Private Shared ReadOnly BadgeCompleted As (fore As Color, back As Color) =
        (Color.FromArgb(0, 120, 92), Color.FromArgb(220, 248, 240))
    Private Shared ReadOnly BadgeReturned As (fore As Color, back As Color) =
        (Color.FromArgb(180, 40, 40), Color.FromArgb(255, 230, 230))
    Private Shared ReadOnly BadgeNeutral As (fore As Color, back As Color) =
        (Color.FromArgb(120, 120, 120), Color.FromArgb(238, 238, 238))
    Private Shared ReadOnly BadgeAmber As (fore As Color, back As Color) =
        (Color.FromArgb(165, 110, 20), Color.FromArgb(255, 244, 224))

    ' ── DATA MODEL ────────────────────────────────────────────────────────────
    Private Class SaleRow
        Public SaleNo As String
        Public SaleAt As DateTime
        Public Cashier As String
        Public Method As String
        Public DiscountType As String
        Public Subtotal As Decimal
        Public Discount As Decimal
        Public Vat As Decimal
        Public Total As Decimal
        Public Status As String
    End Class

    Private Class CashierOption
        Public UserId As Integer
        Public Name As String
        Public Overrides Function ToString() As String
            Return Name
        End Function
    End Class

    ' ── STATE ─────────────────────────────────────────────────────────────────
    Private Const ALL_CASHIERS As String = "All cashiers"
    Private Const ALL_STATUS As String = "All statuses"
    Private Const SEARCH_PLACEHOLDER As String = "Search sale no. or cashier…"
    Private Const SIDE_PAD As Integer = 24
    Private Const ROW_H As Integer = 76
    ' Matches the house KPI band used by Users / Suppliers / Categories:
    ' 140px of card, caption 11pt at (22,14), value 26pt bold at (20,36),
    ' sub-label 10pt at (22,100), 12px between cards. A card that is nearly
    ' the same size as the rest of the system reads as a mistake.
    Private Const SUMMARY_H As Integer = 140

    Private Const MAX_PAGE_SIZE As Integer = 50
    ' Fixed chrome above and below the row viewport:
    '   topRegion 128 + summary (140+12) + spacers 16+16 + card padding 2
    '   + colHeader 44 + pagerBar 52 + bottomPad 12
    ' Keep this in step with SUMMARY_H or the table misjudges its viewport.
    Private Const CHROME_H As Integer = 422
    Private Shared _pageSize As Integer = MAX_PAGE_SIZE

    Private Shared _container As Panel
    Private Shared _scrollHost As Panel

    Private Shared _searchQuery As String = ""
    Private Shared _fromDate As DateTime
    Private Shared _toDate As DateTime
    Private Shared _cashierId As Integer = 0          ' 0 = all
    Private Shared _statusFilter As String = ALL_STATUS

    Private Shared _rows As New List(Of SaleRow)
    Private Shared _cashiers As New List(Of CashierOption)
    Private Shared _statuses As New List(Of String)
    Private Shared _totalRows As Integer = 0
    Private Shared _currentPage As Integer = 0
    Private Shared _reflowAttached As Boolean = False

    ' Summary figures over the filtered set
    Private Shared _sumTotal As Decimal = 0D
    Private Shared _sumCount As Integer = 0
    Private Shared _sumAvg As Decimal = 0D
    Private Shared _sumVat As Decimal = 0D

    Private Shared _lblSumTotal, _lblSumCount, _lblSumAvg, _lblSumVat As Label
    Private Shared _lblPageInfo As Label
    Private Shared _btnPrev, _btnNext As Button
    Private Shared _searchTimer As Timer

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Shared Sub LoadSalesOverview(container As Panel)
        _container = container
        _searchQuery = ""
        _cashierId = 0
        _statusFilter = ALL_STATUS
        _toDate = DateTime.Today
        _fromDate = DateTime.Today.AddDays(-6)      ' last 7 days inclusive
        _currentPage = 0

        ComputePageSize()
        LoadFilterOptions()
        LoadPage()
        Render()
    End Sub

    ' A page that cannot fit on screen defeats the pager, so size the page to
    ' the viewport rather than paging 50 rows into a much smaller area.
    Private Shared Sub ComputePageSize()
        _pageSize = MAX_PAGE_SIZE
        If _container Is Nothing Then Return
        Dim viewport As Integer = _container.ClientSize.Height - CHROME_H
        If viewport < 120 Then Return          ' not laid out yet — keep the ceiling
        Dim fits As Integer = (viewport - 4) \ ROW_H
        _pageSize = Math.Max(4, Math.Min(MAX_PAGE_SIZE, fits))
    End Sub

    ' Re-query with current filters, without rebuilding the filter controls.
    Private Shared Sub ReQuery(Optional resetPage As Boolean = True)
        If resetPage Then _currentPage = 0
        LoadPage()
        PopulateRows()
        UpdateSummaryLabels()
        UpdatePager()
    End Sub

    ' =========================================================================
    '  DATA
    ' =========================================================================
    Private Shared Sub LoadFilterOptions()
        _cashiers.Clear()
        _cashiers.Add(New CashierOption With {.UserId = 0, .Name = ALL_CASHIERS})
        _statuses.Clear()
        _statuses.Add(ALL_STATUS)

        Try
            Using conn = Db.GetConnection()
                conn.Open()

                ' Only cashiers who actually have sales.
                Using cmd As New MySqlCommand(
                    "SELECT DISTINCT u.user_id, u.full_name FROM sales s " &
                    "JOIN users u ON u.user_id = s.user_id ORDER BY u.full_name", conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            _cashiers.Add(New CashierOption With {
                                .UserId = SafeInt(rdr("user_id")),
                                .Name = SafeStr(rdr("full_name"))
                            })
                        End While
                    End Using
                End Using

                ' Statuses straight from the data, so the list can never drift
                ' from what the table actually contains.
                Using cmd As New MySqlCommand(
                    "SELECT DISTINCT status FROM sales " &
                    "WHERE status IS NOT NULL AND status <> '' ORDER BY status", conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            _statuses.Add(SafeStr(rdr("status")))
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load the filter options: " & ex.Message, "Database Error",
                          MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    Private Shared Function BuildWhere() As String
        Dim sb As New System.Text.StringBuilder(
            "WHERE s.sale_datetime >= @fromD AND s.sale_datetime < @toD ")
        If _cashierId > 0 Then sb.Append("AND s.user_id = @uid ")
        If _statusFilter <> ALL_STATUS Then sb.Append("AND s.status = @st ")
        If _searchQuery <> "" Then
            sb.Append("AND (s.sale_no LIKE @q OR COALESCE(u.full_name,'') LIKE @q) ")
        End If
        Return sb.ToString()
    End Function

    Private Shared Sub AddWhereParams(cmd As MySqlCommand)
        cmd.Parameters.AddWithValue("@fromD", _fromDate.Date)
        ' Exclusive upper bound one day past "To" so same-day sales are kept.
        cmd.Parameters.AddWithValue("@toD", _toDate.Date.AddDays(1))
        If _cashierId > 0 Then cmd.Parameters.AddWithValue("@uid", _cashierId)
        If _statusFilter <> ALL_STATUS Then cmd.Parameters.AddWithValue("@st", _statusFilter)
        If _searchQuery <> "" Then cmd.Parameters.AddWithValue("@q", "%" & _searchQuery & "%")
    End Sub

    Private Shared Sub LoadPage()
        _rows.Clear()
        _totalRows = 0
        _sumTotal = 0D : _sumCount = 0 : _sumAvg = 0D : _sumVat = 0D

        Dim joins As String =
            "FROM sales s " &
            "JOIN users u ON u.user_id = s.user_id " &
            "LEFT JOIN payment_methods pm ON pm.method_id = s.method_id "

        Try
            Using conn = Db.GetConnection()
                conn.Open()

                ' 1) Summary over the WHOLE filtered set (not just this page).
                Using cmd As New MySqlCommand(
                    "SELECT COUNT(*) AS n, " &
                    "COALESCE(SUM(s.total),0) AS sum_total, " &
                    "COALESCE(AVG(s.total),0) AS avg_total, " &
                    "COALESCE(SUM(s.vat_amount),0) AS sum_vat " &
                    joins & BuildWhere(), conn)
                    AddWhereParams(cmd)
                    Using rdr = cmd.ExecuteReader()
                        If rdr.Read() Then
                            _sumCount = SafeInt(rdr("n"))
                            _sumTotal = SafeDec(rdr("sum_total"))
                            _sumAvg = SafeDec(rdr("avg_total"))
                            _sumVat = SafeDec(rdr("sum_vat"))
                        End If
                    End Using
                End Using

                _totalRows = _sumCount

                Dim totalPages As Integer =
                    Math.Max(1, CInt(Math.Ceiling(_totalRows / _pageSize)))
                If _currentPage > totalPages - 1 Then _currentPage = totalPages - 1
                If _currentPage < 0 Then _currentPage = 0

                ' 2) Just this page.
                Using cmd As New MySqlCommand(
                    "SELECT s.sale_no, s.sale_datetime, u.full_name, " &
                    "COALESCE(pm.name,'—') AS method_name, " &
                    "COALESCE(s.discount_type,'None') AS discount_type, " &
                    "s.subtotal, s.discount_amount, s.vat_amount, s.total, s.status " &
                    joins & BuildWhere() &
                    "ORDER BY s.sale_datetime DESC, s.sale_id DESC " &
                    "LIMIT @lim OFFSET @off", conn)
                    AddWhereParams(cmd)
                    cmd.Parameters.AddWithValue("@lim", _pageSize)
                    cmd.Parameters.AddWithValue("@off", _currentPage * _pageSize)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            _rows.Add(New SaleRow With {
                                .SaleNo = SafeStr(rdr("sale_no")),
                                .SaleAt = If(IsDBNull(rdr("sale_datetime")), DateTime.MinValue,
                                             Convert.ToDateTime(rdr("sale_datetime"))),
                                .Cashier = SafeStr(rdr("full_name")),
                                .Method = SafeStr(rdr("method_name")),
                                .DiscountType = SafeStr(rdr("discount_type")),
                                .Subtotal = SafeDec(rdr("subtotal")),
                                .Discount = SafeDec(rdr("discount_amount")),
                                .Vat = SafeDec(rdr("vat_amount")),
                                .Total = SafeDec(rdr("total")),
                                .Status = SafeStr(rdr("status"))
                            })
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load sales: " & ex.Message, "Database Error",
                          MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    Private Shared Function SafeInt(o As Object) As Integer
        If o Is Nothing OrElse IsDBNull(o) Then Return 0
        Return Convert.ToInt32(o)
    End Function

    Private Shared Function SafeDec(o As Object) As Decimal
        If o Is Nothing OrElse IsDBNull(o) Then Return 0D
        Return Convert.ToDecimal(o)
    End Function

    Private Shared Function SafeStr(o As Object) As String
        If o Is Nothing OrElse IsDBNull(o) Then Return ""
        Return o.ToString()
    End Function

    Private Shared Function Money(v As Decimal) As String
        Return Peso & v.ToString("N2")
    End Function

    ' =========================================================================
    '  RENDER
    ' =========================================================================
    Private Shared Sub Render()
        _container.SuspendLayout()
        _container.Controls.Clear()
        _reflowAttached = False
        _container.BackColor = MintBg

        Dim tableWrapper As New Panel With {
            .Dock = DockStyle.Fill, .BackColor = MintBg, .Padding = New Padding(0)
        }

        Dim listArea As New BufferedPanel With {
            .Dock = DockStyle.Fill, .BackColor = CardBg, .Padding = New Padding(1)
        }
        AddHandler listArea.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)

        _scrollHost = New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = Color.Transparent,
            .AutoScroll = True,
            .Padding = New Padding(0)
        }

        Dim colHdr As Panel = BuildColHeader()
        colHdr.Dock = DockStyle.Top

        Dim pagerBar As Panel = BuildPagerBar()
        pagerBar.Dock = DockStyle.Bottom

        Dim bottomPad As New Panel With {
            .Dock = DockStyle.Bottom, .Height = 12, .BackColor = Color.Transparent
        }

        ' bottomPad added before pagerBar so the gap lands between the last row
        ' and the pager rule, not below the pager.
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
    '  TOP REGION  —  72px title bar + 56px filter bar
    ' =========================================================================
    Private Shared Function BuildTopRegion() As Panel
        Dim region As New Panel With {.BackColor = MintBg, .AutoSize = False, .Height = 128}

        ' ── Title bar ────────────────────────────────────────────────────────
        Dim titleBar As New Panel With {
            .Height = 72, .Dock = DockStyle.Top, .BackColor = Color.Transparent
        }

        titleBar.Controls.Add(New Label With {
            .Text = "Sales Overview",
            .Font = New Font("Segoe UI", 28, FontStyle.Bold),
            .ForeColor = MintDark,
            .AutoSize = True,
            .Location = New Point(SIDE_PAD, 16)
        })

        Dim pill As New Label With {
            .Text = "Read-only",
            .Font = New Font("Segoe UI Semibold", 9.0F),
            .ForeColor = SubText,
            .BackColor = Color.FromArgb(232, 238, 236),
            .AutoSize = True,
            .Padding = New Padding(10, 5, 10, 5),
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        RoundLabel(pill, 12)

        Dim btnRefresh As Button = MakePillButton("⟳  Refresh", 100,
                                                  Color.FromArgb(220, 235, 232), MintDark)
        AddHandler btnRefresh.Click, Sub() LoadSalesOverview(_container)

        ' ── Search ───────────────────────────────────────────────────────────
        Dim searchWrap As New Panel With {
            .Size = New Size(250, 34),
            .BackColor = CardBg,
            .Cursor = Cursors.IBeam,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        Dim searchFocused As Boolean = False
        AddHandler searchWrap.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim rc = searchWrap.ClientRectangle : rc.Inflate(-1, -1)
                Using b As New SolidBrush(CardBg)
                    e.Graphics.FillRoundedRectangle(b, rc, 8)
                End Using
                Using p As New Pen(If(searchFocused, MintPrimary, RowDivider),
                                   If(searchFocused, 1.8F, 1.0F))
                    e.Graphics.DrawRoundedRectangle(p, rc, 8)
                End Using
                Using p As New Pen(Color.FromArgb(160, 180, 178), 1.4F)
                    e.Graphics.DrawEllipse(p, 10, 10, 12, 12)
                    e.Graphics.DrawLine(p, 20, 20, 25, 25)
                End Using
            End Sub

        Dim txtSearch As New TextBox With {
            .Font = New Font("Segoe UI", 10),
            .BorderStyle = BorderStyle.None,
            .BackColor = CardBg,
            .ForeColor = SubText,
            .Text = SEARCH_PLACEHOLDER,
            .Location = New Point(34, 9),
            .Width = 204
        }

        ' Debounced — this search hits the DB, so no query per keystroke.
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

        AddHandler txtSearch.Enter,
            Sub()
                searchFocused = True
                If txtSearch.Text = SEARCH_PLACEHOLDER Then
                    txtSearch.Text = ""
                    txtSearch.ForeColor = Color.FromArgb(45, 45, 45)
                End If
                searchWrap.Invalidate()
            End Sub
        AddHandler txtSearch.Leave,
            Sub()
                searchFocused = False
                If String.IsNullOrWhiteSpace(txtSearch.Text) Then
                    txtSearch.Text = SEARCH_PLACEHOLDER
                    txtSearch.ForeColor = SubText
                End If
                searchWrap.Invalidate()
            End Sub
        AddHandler txtSearch.TextChanged,
            Sub()
                If txtSearch.Text = SEARCH_PLACEHOLDER Then Return
                _searchQuery = txtSearch.Text.Trim()
                _searchTimer.Stop()
                _searchTimer.Start()
            End Sub
        AddHandler searchWrap.Click, Sub() txtSearch.Focus()
        searchWrap.Controls.Add(txtSearch)

        AddHandler titleBar.Resize,
            Sub()
                btnRefresh.Left = titleBar.Width - btnRefresh.Width - SIDE_PAD
                btnRefresh.Top = (titleBar.Height - btnRefresh.Height) \ 2
                pill.Left = btnRefresh.Left - pill.Width - 10
                pill.Top = (titleBar.Height - pill.Height) \ 2
                searchWrap.Left = pill.Left - searchWrap.Width - 10
                searchWrap.Top = (titleBar.Height - searchWrap.Height) \ 2
            End Sub

        titleBar.Controls.Add(searchWrap)
        titleBar.Controls.Add(pill)
        titleBar.Controls.Add(btnRefresh)

        ' ── Filter bar ───────────────────────────────────────────────────────
        Dim filterBar As New Panel With {
            .Height = 56, .Dock = DockStyle.Top, .BackColor = Color.Transparent
        }

        Dim lblFrom As New Label With {
            .Text = "From", .Font = New Font("Segoe UI", 9.5F), .ForeColor = SubText,
            .AutoSize = True, .Location = New Point(SIDE_PAD, 19), .BackColor = Color.Transparent
        }
        ' Declared before the fields so each picker can nudge the other when
        ' the range inverts; assigned immediately below.
        Dim boxFrom As Panel = Nothing, boxTo As Panel = Nothing
        Dim setFrom As Action(Of Date) = Nothing, setTo As Action(Of Date) = Nothing

        boxFrom = FilterFields.MakeDateField(132, _fromDate,
            Sub(d As Date)
                _fromDate = d
                If _fromDate > _toDate Then
                    _toDate = _fromDate
                    setTo(_toDate)
                End If
                ReQuery()
            End Sub, setFrom)
        boxFrom.Location = New Point(SIDE_PAD + 44, 11)

        Dim lblTo As New Label With {
            .Text = "To", .Font = New Font("Segoe UI", 9.5F), .ForeColor = SubText,
            .AutoSize = True, .Location = New Point(boxFrom.Right + 16, 19),
            .BackColor = Color.Transparent
        }

        boxTo = FilterFields.MakeDateField(132, _toDate,
            Sub(d As Date)
                _toDate = d
                If _toDate < _fromDate Then
                    _fromDate = _toDate
                    setFrom(_fromDate)
                End If
                ReQuery()
            End Sub, setTo)
        boxTo.Location = New Point(boxFrom.Right + 44, 11)

        Dim lblCashier As New Label With {
            .Text = "Cashier", .Font = New Font("Segoe UI", 9.5F), .ForeColor = SubText,
            .AutoSize = True, .Location = New Point(boxTo.Right + 24, 19),
            .BackColor = Color.Transparent
        }
        ' _cashiers and this name list stay in the same order, so the picked
        ' index maps straight back to the CashierOption behind it.
        Dim cashierNames As New List(Of String)
        For Each c In _cashiers
            cashierNames.Add(c.Name)
        Next
        Dim boxCashier As Panel = FilterFields.MakeSelectField(cashierNames, 186, 0,
            Sub(i As Integer)
                _cashierId = If(i >= 0 AndAlso i < _cashiers.Count, _cashiers(i).UserId, 0)
                ReQuery()
            End Sub)
        boxCashier.Location = New Point(boxTo.Right + 78, 11)

        Dim lblStatus As New Label With {
            .Text = "Status", .Font = New Font("Segoe UI", 9.5F), .ForeColor = SubText,
            .AutoSize = True, .Location = New Point(boxCashier.Right + 24, 19),
            .BackColor = Color.Transparent
        }
        Dim statusNames As New List(Of String)(_statuses)
        Dim boxStatus As Panel = FilterFields.MakeSelectField(statusNames, 158, 0,
            Sub(i As Integer)
                _statusFilter = If(i >= 0 AndAlso i < statusNames.Count,
                                   statusNames(i), ALL_STATUS)
                ReQuery()
            End Sub)
        boxStatus.Location = New Point(boxCashier.Right + 72, 11)

        filterBar.Controls.AddRange({lblFrom, boxFrom, lblTo, boxTo,
                                     lblCashier, boxCashier, lblStatus, boxStatus})

        region.Controls.Add(filterBar)
        region.Controls.Add(titleBar)
        Return region
    End Function

    ' =========================================================================
    '  SUMMARY STRIP  —  over the filtered set
    ' =========================================================================
    Private Shared Function BuildSummaryStrip() As Panel
        ' +12 so the 12px bottom padding does not eat into the card: the cards
        ' must be a full SUMMARY_H tall or the sub-label at y=100 clips.
        Dim bar As New Panel With {
            .Height = SUMMARY_H + 12,
            .BackColor = MintBg,
            .Padding = New Padding(SIDE_PAD, 0, SIDE_PAD, 12)
        }

        Dim grid As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .ColumnCount = 4,
            .RowCount = 1,
            .BackColor = Color.Transparent,
            .CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        }
        For i As Integer = 0 To 3
            grid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 25.0F))
        Next
        grid.RowStyles.Add(New RowStyle(SizeType.Percent, 100))

        Dim c1 = MakeSummaryCard("Total Sales", MintPrimary, _lblSumTotal,
                                 "over the filtered set")
        Dim c2 = MakeSummaryCard("Transactions", MintDark, _lblSumCount,
                                 "sales in range")
        Dim c3 = MakeSummaryCard("Average Sale", MintDeep, _lblSumAvg,
                                 "per transaction")
        Dim c4 = MakeSummaryCard("Total VAT", MintDeep, _lblSumVat,
                                 "included in total")

        c1.Margin = New Padding(0, 0, 12, 0)
        c2.Margin = New Padding(0, 0, 12, 0)
        c3.Margin = New Padding(0, 0, 12, 0)
        c4.Margin = New Padding(0, 0, 0, 0)

        grid.Controls.Add(c1, 0, 0)
        grid.Controls.Add(c2, 1, 0)
        grid.Controls.Add(c3, 2, 0)
        grid.Controls.Add(c4, 3, 0)

        bar.Controls.Add(grid)
        Return bar
    End Function

    Private Shared Function MakeSummaryCard(caption As String, valueClr As Color,
                                            ByRef valueLabel As Label,
                                            subLabel As String) As Panel
        Dim card As New BufferedPanel With {.Dock = DockStyle.Fill, .BackColor = CardBg}
        AddHandler card.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)

        card.Controls.Add(New Label With {
            .Text = caption,
            .Font = New Font("Segoe UI", 11),
            .ForeColor = SubText,
            .AutoSize = True,
            .Location = New Point(22, 14),
            .BackColor = Color.Transparent
        })

        valueLabel = New Label With {
            .Text = "—",
            .Font = New Font("Segoe UI", 26, FontStyle.Bold),
            .ForeColor = valueClr,
            .AutoSize = True,
            .Location = New Point(20, 36),
            .BackColor = Color.Transparent
        }
        card.Controls.Add(valueLabel)

        card.Controls.Add(New Label With {
            .Text = subLabel,
            .Font = New Font("Segoe UI", 10),
            .ForeColor = SubText,
            .AutoSize = True,
            .Location = New Point(22, 100),
            .BackColor = Color.Transparent
        })
        Return card
    End Function

    ' Filters change the figures, so the labels are updated in place rather
    ' than by rebuilding the strip (which would also rebuild the filter bar).
    Private Shared Sub UpdateSummaryLabels()
        If _lblSumTotal IsNot Nothing Then _lblSumTotal.Text = Money(_sumTotal)
        If _lblSumCount IsNot Nothing Then _lblSumCount.Text = _sumCount.ToString("N0")
        If _lblSumAvg IsNot Nothing Then _lblSumAvg.Text = Money(_sumAvg)
        If _lblSumVat IsNot Nothing Then _lblSumVat.Text = Money(_sumVat)
    End Sub

    ' =========================================================================
    '  COLUMN HEADER  —  44px (must match BuildDataRow)
    ' =========================================================================
    Private Shared Function BuildColHeader() As Panel
        Dim hdr As New Panel With {.Height = 44, .BackColor = Color.Transparent}

        Dim labels As Label() = {
            MakeColLabel("SALE", New Point(24, 13)),
            MakeColLabel("CASHIER", New Point(0, 13)),
            MakeColLabel("DISCOUNT", New Point(0, 13)),
            MakeColLabel("SUBTOTAL", New Point(0, 13)),
            MakeColLabel("VAT", New Point(0, 13)),
            MakeColLabel("TOTAL", New Point(0, 13)),
            MakeColLabel("STATUS", New Point(0, 13))
        }
        hdr.Controls.AddRange(labels)

        AddHandler hdr.Resize,
            Sub()
                Dim w = hdr.Width
                labels(0).Left = 24
                labels(1).Left = CInt(w * 0.2)
                labels(2).Left = CInt(w * 0.38)
                labels(3).Left = CInt(w * 0.52)
                labels(4).Left = CInt(w * 0.63)
                labels(5).Left = CInt(w * 0.73)
                labels(6).Left = CInt(w * 0.86)
            End Sub

        AddHandler hdr.Paint,
            Sub(s, e)
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 20, hdr.Height - 1, hdr.Width - 20, hdr.Height - 1)
                End Using
            End Sub

        Return hdr
    End Function

    Private Shared Function MakeColLabel(text As String, loc As Point) As Label
        Return New Label With {
            .Text = text,
            .Font = New Font("Segoe UI", 10, FontStyle.Bold),
            .ForeColor = SubText,
            .AutoSize = True,
            .Location = loc,
            .BackColor = Color.Transparent
        }
    End Function

    ' =========================================================================
    '  ROWS
    ' =========================================================================
    Private Shared Sub PopulateRows()
        If _scrollHost Is Nothing Then Return

        _scrollHost.SuspendLayout()
        _scrollHost.Controls.Clear()

        If _reflowAttached Then
            RemoveHandler _scrollHost.Resize, AddressOf ReflowScrollHost
            _reflowAttached = False
        End If

        If _rows.Count = 0 Then
            _scrollHost.Controls.Add(BuildEmptyRow("No sales match the current filters."))
        Else
            Dim y As Integer = 4
            For Each r As SaleRow In _rows
                Dim row As Panel = BuildDataRow(r, ROW_H)
                row.Location = New Point(0, y)
                row.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
                _scrollHost.Controls.Add(row)
                row.Width = _scrollHost.ClientSize.Width - 4
                y += ROW_H
            Next
            If Not _reflowAttached Then
                _reflowAttached = True
                AddHandler _scrollHost.Resize, AddressOf ReflowScrollHost
            End If
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

    Private Shared Function BuildDataRow(r As SaleRow, height As Integer) As Panel
        Dim p As New BufferedPanel With {.Height = height, .BackColor = Color.Transparent}
        Dim isVoided As Boolean = (r.Status.ToLower() = "voided")

        AddHandler p.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                If TypeOf p.Tag Is Boolean AndAlso CBool(p.Tag) Then
                    Dim rect As New Rectangle(8, 3, p.Width - 16, height - 6)
                    Using br As New SolidBrush(RowHover)
                        e.Graphics.FillRoundedRectangle(br, rect, 10)
                    End Using
                End If
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 20, p.Height - 1, p.Width - 20, p.Height - 1)
                End Using
            End Sub

        ' Sale column: number over absolute timestamp
        Dim lblNo As New Label With {
            .Text = If(r.SaleNo = "", "—", r.SaleNo),
            .Font = New Font("Segoe UI", 11, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblWhen As New Label With {
            .Text = If(r.SaleAt = DateTime.MinValue, "—",
                       r.SaleAt.ToString("MMM d, yyyy  h:mm tt")),
            .Font = New Font("Segoe UI", 9),
            .ForeColor = FaintText, .AutoSize = True, .BackColor = Color.Transparent
        }

        ' Cashier column: name over payment method
        Dim lblCashier As New Label With {
            .Text = If(r.Cashier = "", "—", r.Cashier),
            .Font = New Font("Segoe UI", 10.5F),
            .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblMethod As New Label With {
            .Text = If(r.Method = "", "—", r.Method),
            .Font = New Font("Segoe UI", 9),
            .ForeColor = FaintText, .AutoSize = True, .BackColor = Color.Transparent
        }

        ' Discount column: type over amount
        Dim hasDisc As Boolean = (r.Discount > 0D)
        Dim lblDiscType As New Label With {
            .Text = If(r.DiscountType = "", "None", r.DiscountType),
            .Font = New Font("Segoe UI", 10),
            .ForeColor = If(hasDisc, SubText, FaintText),
            .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblDiscAmt As New Label With {
            .Text = If(hasDisc, "−" & Money(r.Discount), "—"),
            .Font = New Font("Segoe UI", 9),
            .ForeColor = If(hasDisc, AccentRed, FaintText),
            .AutoSize = True, .BackColor = Color.Transparent
        }

        Dim lblSubtotal As New Label With {
            .Text = Money(r.Subtotal),
            .Font = New Font("Segoe UI", 10.5F),
            .ForeColor = Color.FromArgb(45, 60, 56),
            .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblVat As New Label With {
            .Text = Money(r.Vat),
            .Font = New Font("Segoe UI", 10.5F),
            .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblTotal As New Label With {
            .Text = Money(r.Total),
            .Font = New Font("Segoe UI", 12,
                             If(isVoided, FontStyle.Strikeout Or FontStyle.Bold, FontStyle.Bold)),
            .ForeColor = If(isVoided, FaintText, MintPrimary),
            .AutoSize = True, .BackColor = Color.Transparent
        }

        Dim badgeClr = GetStatusBadge(r.Status)
        Dim badge As New Label With {
            .Text = If(r.Status = "", "—", r.Status),
            .Font = New Font("Segoe UI", 9, FontStyle.Bold),
            .ForeColor = badgeClr.fore,
            .BackColor = badgeClr.back,
            .AutoSize = True,
            .Padding = New Padding(10, 4, 10, 4)
        }
        RoundLabel(badge, 10)

        p.Controls.AddRange({lblNo, lblWhen, lblCashier, lblMethod, lblDiscType, lblDiscAmt,
                             lblSubtotal, lblVat, lblTotal, badge})

        AddHandler p.Resize,
            Sub()
                Dim w = p.Width
                Dim top1 As Integer = (height \ 2) - 18
                Dim top2 As Integer = (height \ 2) + 2

                lblNo.Left = 24 : lblNo.Top = top1
                lblWhen.Left = 24 : lblWhen.Top = top2

                Dim cx As Integer = CInt(w * 0.2)
                lblCashier.Left = cx : lblCashier.Top = top1
                lblMethod.Left = cx : lblMethod.Top = top2

                Dim dx As Integer = CInt(w * 0.38)
                lblDiscType.Left = dx : lblDiscType.Top = top1
                lblDiscAmt.Left = dx : lblDiscAmt.Top = top2

                lblSubtotal.Left = CInt(w * 0.52)
                lblSubtotal.Top = (height - lblSubtotal.Height) \ 2
                lblVat.Left = CInt(w * 0.63)
                lblVat.Top = (height - lblVat.Height) \ 2
                lblTotal.Left = CInt(w * 0.73)
                lblTotal.Top = (height - lblTotal.Height) \ 2
                badge.Left = CInt(w * 0.86)
                badge.Top = (height - badge.Height) \ 2
            End Sub

        PropagateHover(p)
        Return p
    End Function

    Private Shared Function GetStatusBadge(status As String) As (fore As Color, back As Color)
        Select Case status.ToLower()
            Case "completed" : Return BadgeCompleted
            Case "returned", "refunded", "partially refunded" : Return BadgeReturned
            Case "voided", "cancelled" : Return BadgeNeutral
            Case "onhold", "held", "pending" : Return BadgeAmber
            Case Else : Return BadgeNeutral
        End Select
    End Function

    Private Shared Function BuildEmptyRow(message As String) As Panel
        Dim p As New Panel With {.Height = 90, .Dock = DockStyle.Top, .BackColor = Color.Transparent}
        p.Controls.Add(New Label With {
            .Text = message,
            .Font = New Font("Segoe UI", 10.5F),
            .ForeColor = FaintText,
            .AutoSize = True,
            .Location = New Point(22, 28),
            .BackColor = Color.Transparent
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

        _btnPrev = New Button With {
            .Text = "‹", .FlatStyle = FlatStyle.Flat, .Cursor = Cursors.Hand,
            .Size = New Size(32, 32), .BackColor = Color.FromArgb(220, 235, 232),
            .ForeColor = MintDark, .Font = New Font("Segoe UI", 14, FontStyle.Bold),
            .TextAlign = ContentAlignment.MiddleCenter
        }
        _btnPrev.FlatAppearance.BorderSize = 0

        _btnNext = New Button With {
            .Text = "›", .FlatStyle = FlatStyle.Flat, .Cursor = Cursors.Hand,
            .Size = New Size(32, 32), .BackColor = Color.FromArgb(220, 235, 232),
            .ForeColor = MintDark, .Font = New Font("Segoe UI", 14, FontStyle.Bold),
            .TextAlign = ContentAlignment.MiddleCenter
        }
        _btnNext.FlatAppearance.BorderSize = 0

        _lblPageInfo = New Label With {
            .Font = New Font("Segoe UI", 9.5F), .ForeColor = SubText,
            .AutoSize = True, .BackColor = Color.Transparent
        }

        AddHandler wrapper.Resize,
            Sub()
                Dim cx As Integer = wrapper.Width \ 2
                _btnPrev.Left = cx - _btnPrev.Width - _lblPageInfo.Width \ 2 - 8
                _btnPrev.Top = (wrapper.Height - _btnPrev.Height) \ 2
                _lblPageInfo.Left = cx - _lblPageInfo.Width \ 2
                _lblPageInfo.Top = (wrapper.Height - _lblPageInfo.Height) \ 2
                _btnNext.Left = cx + _lblPageInfo.Width \ 2 + 8
                _btnNext.Top = (wrapper.Height - _btnNext.Height) \ 2
            End Sub

        AddHandler _btnPrev.Click,
            Sub()
                If _currentPage > 0 Then
                    _currentPage -= 1
                    ReQuery(resetPage:=False)
                End If
            End Sub
        AddHandler _btnNext.Click,
            Sub()
                Dim totalPages As Integer =
                    Math.Max(1, CInt(Math.Ceiling(_totalRows / _pageSize)))
                If _currentPage < totalPages - 1 Then
                    _currentPage += 1
                    ReQuery(resetPage:=False)
                End If
            End Sub

        wrapper.Controls.AddRange({_btnPrev, _lblPageInfo, _btnNext})
        Return wrapper
    End Function

    Private Shared Sub UpdatePager()
        If _lblPageInfo Is Nothing Then Return
        Dim totalPages As Integer = Math.Max(1, CInt(Math.Ceiling(_totalRows / _pageSize)))
        If _totalRows = 0 Then
            _lblPageInfo.Text = "No sales"
        Else
            Dim firstRow As Integer = _currentPage * _pageSize + 1
            Dim lastRow As Integer = Math.Min(_totalRows, firstRow + _rows.Count - 1)
            _lblPageInfo.Text =
                $"Showing {firstRow:N0}–{lastRow:N0} of {_totalRows:N0}   ·   Page {_currentPage + 1} of {totalPages}"
        End If
        _btnPrev.Enabled = (_currentPage > 0)
        _btnNext.Enabled = (_currentPage < totalPages - 1)
        If _lblPageInfo.Parent IsNot Nothing Then _lblPageInfo.Parent.PerformLayout()
    End Sub

    ' =========================================================================
    '  SHARED HELPERS
    ' =========================================================================
    Private Shared Function MakePillButton(text As String, width As Integer,
                                           backClr As Color, foreClr As Color) As Button
        Dim b As New Button With {
            .Text = text,
            .Size = New Size(width, 34),
            .FlatStyle = FlatStyle.Flat,
            .Font = New Font("Segoe UI", 9.5F),
            .Cursor = Cursors.Hand,
            .BackColor = backClr,
            .ForeColor = foreClr,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        b.FlatAppearance.BorderSize = 0
        Dim normalClr As Color = backClr
        Dim hoverClr As Color = Color.FromArgb(Math.Max(0, backClr.R - 12),
                                               Math.Max(0, backClr.G - 12),
                                               Math.Max(0, backClr.B - 12))
        AddHandler b.Paint,
            Sub(s, e)
                Dim bb = CType(s, Button)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect = bb.ClientRectangle : rect.Inflate(-1, -1)
                Using br As New SolidBrush(bb.BackColor)
                    g.FillRoundedRectangle(br, rect, 8)
                End Using
                TextRenderer.DrawText(g, bb.Text, bb.Font, bb.ClientRectangle, bb.ForeColor,
                    TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub
        AddHandler b.MouseEnter, Sub()
                                     b.BackColor = hoverClr
                                     b.Invalidate()
                                 End Sub
        AddHandler b.MouseLeave, Sub()
                                     b.BackColor = normalClr
                                     b.Invalidate()
                                 End Sub
        Return b
    End Function

    Private Shared Sub RoundLabel(lbl As Label, radius As Integer)
        Dim apply As Action =
            Sub()
                If lbl.Width <= radius OrElse lbl.Height <= radius Then Return
                Dim path As New GraphicsPath()
                Dim r As Integer = radius
                path.AddArc(0, 0, r, r, 180, 90)
                path.AddArc(lbl.Width - r, 0, r, r, 270, 90)
                path.AddArc(lbl.Width - r, lbl.Height - r, r, r, 0, 90)
                path.AddArc(0, lbl.Height - r, r, r, 90, 90)
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
