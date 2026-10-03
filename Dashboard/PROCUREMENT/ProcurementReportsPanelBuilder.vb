Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Globalization
Imports System.Windows.Forms
Imports MySql.Data.MySqlClient

' =============================================================================
'  ProcurementReportsPanelBuilder  —  Procurement ▸ Reports
'
'  Every procurement transaction in ONE table, newest first:
'
'    Purchase Request   raised by Inventory, approved / rejected by the admin
'    Price Quotation    a supplier's quoted price
'    Purchase Order     what was ordered, from whom, for how much
'    Delivery           goods received at the dock, and their condition
'    Defect / Short     a defective or missing line found at receiving
'    Payment            what was paid to the supplier (Accountant)
'
'  Search (reference, supplier, product, person), a From / To date range and a
'  Type filter narrow it. Clicking a row that belongs to a PO opens the PO with
'  its items; a request row opens the request. The same filtered list can be
'  printed or saved as Excel / PDF (with the report signature block).
'
'  LAYOUT (same frame as Inventory ▸ Transaction History):
'    TitleBar (72)  : title + Save as Excel / Save as PDF / Print
'    FilterBar (56) : search, From / To, Type
'    SummaryStrip   : Transactions, Purchase Orders, Paid to Suppliers, Deliveries
'    Table card     : column header, rows, pager
'
'  Optional tables (payments, receipts, problems, quotations) are probed, so an
'  older database simply shows fewer kinds of transaction.
' =============================================================================
Public Class ProcurementReportsPanelBuilder

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

    Private Const SEARCH_PLACEHOLDER As String = "Search P.O., supplier, product…"
    Private Const SIDE_PAD As Integer = 24
    Private Const ROW_H As Integer = 62
    Private Const SUMMARY_H As Integer = 140
    Private Const MAX_PAGE_SIZE As Integer = 50
    Private Const EXPORT_LIMIT As Integer = 5000
    ' title 72 + filters 56 + summary (140+12) + spacers 16+16 + card padding 2
    ' + colHeader 44 + pagerBar 52 + bottomPad 12
    Private Const CHROME_H As Integer = 422

    ' Column x positions as a share of the row width (header and rows alike).
    Private Const X_REF As Single = 0.12F
    Private Const X_TYPE As Single = 0.24F
    Private Const X_PARTY As Single = 0.37F
    Private Const X_DETAILS As Single = 0.52F
    Private Const X_STATUS As Single = 0.73F

    ' Transaction kinds — also the Type filter entries.
    Private Const K_REQUEST As String = "Purchase Request"
    Private Const K_QUOTE As String = "Price Quotation"
    Private Const K_PO As String = "Purchase Order"
    Private Const K_DELIVERY As String = "Delivery"
    Private Const K_ISSUE As String = "Defect / Short"
    Private Const K_PAYMENT As String = "Payment"
    Private Const ALL_TYPES As String = "All types"

    Private Class TxnRow
        Public Dt As DateTime
        Public PoId As Integer
        Public RecId As Integer
        Public Ref As String = ""
        Public Kind As String = ""
        Public Party As String = ""
        Public Details As String = ""
        Public Status As String = ""
        Public Amount As Decimal?
    End Class

    ' ── STATE ─────────────────────────────────────────────────────────────────
    Private Shared _container As Panel
    Private Shared _scrollHost As Panel
    Private Shared _fromDate As Date
    Private Shared _toDate As Date
    Private Shared _search As String = ""
    Private Shared _type As String = ALL_TYPES
    Private Shared _kinds As New List(Of String)
    Private Shared _rows As New List(Of TxnRow)
    Private Shared _pageSize As Integer = MAX_PAGE_SIZE
    Private Shared _currentPage As Integer = 0
    Private Shared _totalRows As Integer = 0
    Private Shared _poCount, _deliveryCount, _deliveryBad, _payCount As Integer
    Private Shared _poAmount, _payAmount As Decimal
    Private Shared _reflowAttached As Boolean = False
    Private Shared _loadFailed As Boolean = False

    Private Shared _lblCount, _lblPo, _lblPaid, _lblDel As Label
    Private Shared _subPo, _subPaid, _subDel As Label
    Private Shared _lblPageInfo As Label
    Private Shared _btnPrev, _btnNext As Button
    Private Shared _searchTimer As Timer

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Shared Sub LoadReports(container As Panel)
        _container = container
        _toDate = Date.Today
        _fromDate = New Date(Date.Today.Year, Date.Today.Month, 1)   ' this month so far
        _search = ""
        _type = ALL_TYPES
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

    ' Text columns come from tables with different collations; normalising
    ' them lets the UNION and the LIKE search work across all of them.
    Private Shared Function T(expr As String) As String
        Return "CONVERT(" & expr & " USING utf8mb4) COLLATE utf8mb4_unicode_ci"
    End Function

    Private Shared Function TableExists(conn As MySqlConnection, table As String) As Boolean
        Using cmd As New MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.tables " &
            "WHERE table_schema = DATABASE() AND table_name = @t", conn)
            cmd.Parameters.AddWithValue("@t", table)
            Return Convert.ToInt32(cmd.ExecuteScalar()) > 0
        End Using
    End Function

    ' One SELECT per kind of transaction, all with the same columns:
    ' dt, po_id, rec_id, ref, kind, party, details, status, amount, extra.
    ' "extra" is searched but not shown (e.g. the products on a PO).
    Private Shared Function BuildUnion(conn As MySqlConnection) As String
        Dim parts As New List(Of String)
        _kinds = New List(Of String)

        _kinds.Add(K_REQUEST)
        parts.Add(
            "SELECT pr.created_at AS dt, 0 AS po_id, pr.request_id AS rec_id, " &
            T("CONCAT('REQ-', LPAD(pr.request_id, 4, '0'))") & " AS ref, " &
            T("'" & K_REQUEST & "'") & " AS kind, " &
            T("COALESCE(u.full_name, '-')") & " AS party, " &
            T("CONCAT((SELECT COUNT(*) FROM purchase_request_items pri WHERE pri.request_id = pr.request_id), " &
              "' product(s), ', (SELECT COALESCE(SUM(pri.qty_requested),0) FROM purchase_request_items pri " &
              "WHERE pri.request_id = pr.request_id), ' unit(s)')") & " AS details, " &
            T("pr.status") & " AS status, NULL AS amount, " &
            T("COALESCE((SELECT GROUP_CONCAT(p.name SEPARATOR ' ') FROM purchase_request_items pri " &
              "JOIN products p ON p.product_id = pri.product_id WHERE pri.request_id = pr.request_id), '')") & " AS extra " &
            "FROM purchase_requests pr LEFT JOIN users u ON u.user_id = pr.requested_by")

        If TableExists(conn, "quotations") Then
            _kinds.Add(K_QUOTE)
            parts.Add(
                "SELECT q.created_at, 0, q.quotation_id, " & T("q.quotation_no") & ", " &
                T("'" & K_QUOTE & "'") & ", " & T("s.name") & ", " &
                T("CONCAT('For REQ-', LPAD(q.request_id, 4, '0'))") & ", " & T("q.status") & ", " &
                "q.total_amount, " &
                T("COALESCE((SELECT GROUP_CONCAT(p.name SEPARATOR ' ') FROM quotation_items qi " &
                  "JOIN products p ON p.product_id = qi.product_id WHERE qi.quotation_id = q.quotation_id), '')") & " " &
                "FROM quotations q JOIN suppliers s ON s.supplier_id = q.supplier_id")
        End If

        _kinds.Add(K_PO)
        parts.Add(
            "SELECT po.order_date, po.po_id, po.po_id, " & T("po.po_no") & ", " &
            T("'" & K_PO & "'") & ", " & T("s.name") & ", " &
            T("CONCAT((SELECT COUNT(*) FROM po_items pi WHERE pi.po_id = po.po_id), ' item(s)', " &
              "IF(po.expected_date IS NULL, '', CONCAT(', expected ', DATE_FORMAT(po.expected_date, '%b %e, %Y'))))") & ", " &
            T("po.status") & ", po.total_cost, " &
            T("COALESCE((SELECT GROUP_CONCAT(p.name SEPARATOR ' ') FROM po_items pi " &
              "JOIN products p ON p.product_id = pi.product_id WHERE pi.po_id = po.po_id), '')") & " " &
            "FROM purchase_orders po JOIN suppliers s ON s.supplier_id = po.supplier_id")

        If TableExists(conn, "goods_receipts") Then
            _kinds.Add(K_DELIVERY)
            parts.Add(
                "SELECT gr.received_at, gr.po_id, gr.receipt_id, " & T("po.po_no") & ", " &
                T("'" & K_DELIVERY & "'") & ", " & T("s.name") & ", " &
                T("CONCAT(gr.total_units, ' unit(s), received by ', gr.receiver_name)") & ", " &
                T("IF(gr.condition_note = 'Good', 'Good condition', 'With defect / short')") & ", NULL, " &
                T("COALESCE(gr.remarks, '')") & " " &
                "FROM goods_receipts gr JOIN purchase_orders po ON po.po_id = gr.po_id " &
                "JOIN suppliers s ON s.supplier_id = po.supplier_id " &
                "WHERE gr.phase = 'receive'")
        End If

        If TableExists(conn, "goods_receipt_issues") Then
            _kinds.Add(K_ISSUE)
            parts.Add(
                "SELECT gi.recorded_at, gi.po_id, gi.issue_id, " & T("po.po_no") & ", " &
                T("'" & K_ISSUE & "'") & ", " & T("s.name") & ", " &
                T("CONCAT(p.name, ': ', gi.issue_type, ' ', gi.qty, IF(gi.details IS NULL, '', CONCAT(' - ', gi.details)))") & ", " &
                T("gi.action_taken") & ", NULL, " & T("p.sku") & " " &
                "FROM goods_receipt_issues gi JOIN purchase_orders po ON po.po_id = gi.po_id " &
                "JOIN suppliers s ON s.supplier_id = po.supplier_id " &
                "JOIN products p ON p.product_id = gi.product_id")
        End If

        If TableExists(conn, "supplier_payments") Then
            _kinds.Add(K_PAYMENT)
            parts.Add(
                "SELECT sp.paid_on, sp.po_id, sp.payment_id, " & T("po.po_no") & ", " &
                T("'" & K_PAYMENT & "'") & ", " & T("s.name") & ", " &
                T("CONCAT(sp.method, IF(COALESCE(sp.reference_no, '') = '', '', CONCAT(', ref ', sp.reference_no)))") & ", " &
                T("'Paid'") & ", sp.amount_paid, " & T("COALESCE(sp.remarks, '')") & " " &
                "FROM supplier_payments sp JOIN purchase_orders po ON po.po_id = sp.po_id " &
                "JOIN suppliers s ON s.supplier_id = po.supplier_id")
        End If

        Return "(" & String.Join(" UNION ALL ", parts) & ") t"
    End Function

    Private Shared Function WhereClause(includeType As Boolean) As String
        Dim sb As New System.Text.StringBuilder("WHERE t.dt >= @fromD AND t.dt < @toD ")
        If includeType AndAlso _type <> ALL_TYPES Then sb.Append("AND t.kind = @kind ")
        If _search <> "" Then
            sb.Append("AND (t.ref LIKE @q OR t.party LIKE @q OR t.details LIKE @q " &
                      "OR t.status LIKE @q OR t.extra LIKE @q) ")
        End If
        Return sb.ToString()
    End Function

    Private Shared Sub AddParams(cmd As MySqlCommand)
        cmd.Parameters.AddWithValue("@fromD", _fromDate.Date)
        cmd.Parameters.AddWithValue("@toD", _toDate.Date.AddDays(1))    ' whole last day
        cmd.Parameters.AddWithValue("@kind", _type)
        cmd.Parameters.AddWithValue("@q", "%" & _search & "%")
    End Sub

    Private Shared Function ReadRows(cmd As MySqlCommand) As List(Of TxnRow)
        Dim result As New List(Of TxnRow)
        Using rdr = cmd.ExecuteReader()
            While rdr.Read()
                result.Add(New TxnRow With {
                    .Dt = Convert.ToDateTime(rdr("dt")),
                    .PoId = Convert.ToInt32(rdr("po_id")),
                    .RecId = Convert.ToInt32(rdr("rec_id")),
                    .Ref = rdr("ref").ToString(),
                    .Kind = rdr("kind").ToString(),
                    .Party = rdr("party").ToString(),
                    .Details = rdr("details").ToString(),
                    .Status = rdr("status").ToString(),
                    .Amount = If(IsDBNull(rdr("amount")), CType(Nothing, Decimal?), Convert.ToDecimal(rdr("amount")))
                })
            End While
        End Using
        Return result
    End Function

    Private Shared Sub LoadData()
        _rows.Clear()
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Dim src As String = BuildUnion(conn)

                ' Summary cards follow the dates and search, not the Type pick,
                ' so they keep describing the whole period.
                Using cmd As New MySqlCommand(
                    "SELECT " &
                    "SUM(t.kind = '" & K_PO & "') AS po_n, " &
                    "COALESCE(SUM(CASE WHEN t.kind = '" & K_PO & "' AND t.status <> 'Cancelled' THEN t.amount END), 0) AS po_amt, " &
                    "SUM(t.kind = '" & K_PAYMENT & "') AS pay_n, " &
                    "COALESCE(SUM(CASE WHEN t.kind = '" & K_PAYMENT & "' THEN t.amount END), 0) AS pay_amt, " &
                    "SUM(t.kind = '" & K_DELIVERY & "') AS del_n, " &
                    "SUM(t.kind = '" & K_DELIVERY & "' AND t.status <> 'Good condition') AS del_bad " &
                    "FROM " & src & " " & WhereClause(False), conn)
                    AddParams(cmd)
                    Using rdr = cmd.ExecuteReader()
                        If rdr.Read() Then
                            _poCount = SafeInt(rdr("po_n"))
                            _poAmount = SafeDec(rdr("po_amt"))
                            _payCount = SafeInt(rdr("pay_n"))
                            _payAmount = SafeDec(rdr("pay_amt"))
                            _deliveryCount = SafeInt(rdr("del_n"))
                            _deliveryBad = SafeInt(rdr("del_bad"))
                        End If
                    End Using
                End Using

                Using cmd As New MySqlCommand(
                    "SELECT COUNT(*) FROM " & src & " " & WhereClause(True), conn)
                    AddParams(cmd)
                    _totalRows = Convert.ToInt32(cmd.ExecuteScalar())
                End Using

                Dim totalPages As Integer = Math.Max(1, CInt(Math.Ceiling(_totalRows / _pageSize)))
                If _currentPage > totalPages - 1 Then _currentPage = totalPages - 1
                If _currentPage < 0 Then _currentPage = 0

                Using cmd As New MySqlCommand(
                    "SELECT * FROM " & src & " " & WhereClause(True) &
                    "ORDER BY t.dt DESC, t.ref DESC LIMIT @lim OFFSET @off", conn)
                    AddParams(cmd)
                    cmd.Parameters.AddWithValue("@lim", _pageSize)
                    cmd.Parameters.AddWithValue("@off", _currentPage * _pageSize)
                    _rows = ReadRows(cmd)
                End Using
            End Using
            _loadFailed = False
        Catch ex As Exception
            _totalRows = 0
            _poCount = 0 : _poAmount = 0 : _payCount = 0 : _payAmount = 0
            _deliveryCount = 0 : _deliveryBad = 0
            If Not _loadFailed Then
                AppModal.Show("Could not load the procurement report:" & Environment.NewLine & ErrorLog.ForUser(ex),
                              "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End If
            _loadFailed = True
        End Try
    End Sub

    ' Every matching row (not just the page on screen), for print / export.
    Private Shared Function LoadAllForExport() As List(Of TxnRow)
        Using conn = Db.GetConnection()
            conn.Open()
            Using cmd As New MySqlCommand(
                "SELECT * FROM " & BuildUnion(conn) & " " & WhereClause(True) &
                "ORDER BY t.dt DESC, t.ref DESC LIMIT " & EXPORT_LIMIT, conn)
                AddParams(cmd)
                Return ReadRows(cmd)
            End Using
        End Using
    End Function

    Private Shared Sub UpdateSummaryLabels()
        If _lblCount IsNot Nothing Then _lblCount.Text = _totalRows.ToString("N0")
        If _lblPo IsNot Nothing Then _lblPo.Text = Money(_poAmount)
        If _subPo IsNot Nothing Then _subPo.Text = _poCount.ToString("N0") & If(_poCount = 1, " order placed", " orders placed")
        If _lblPaid IsNot Nothing Then _lblPaid.Text = Money(_payAmount)
        If _subPaid IsNot Nothing Then _subPaid.Text = _payCount.ToString("N0") & If(_payCount = 1, " payment", " payments")
        If _lblDel IsNot Nothing Then _lblDel.Text = _deliveryCount.ToString("N0")
        If _subDel IsNot Nothing Then
            _subDel.Text = If(_deliveryBad = 0, "all in good condition",
                              _deliveryBad.ToString("N0") & " with defect / short")
            _subDel.ForeColor = If(_deliveryBad = 0, SubText, AccentRed)
        End If
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
    '  TOP REGION — title + export buttons, then search and filters
    ' =========================================================================
    Private Shared Function BuildTopRegion() As Panel
        Dim region As New Panel With {.BackColor = MintBg, .Height = 128}

        Dim titleBar As New Panel With {.Height = 72, .Dock = DockStyle.Top, .BackColor = Color.Transparent}
        titleBar.Controls.Add(New Label With {
            .Text = "Reports",
            .Font = New Font("Segoe UI", 28, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True,
            .Location = New Point(SIDE_PAD, 16)
        })

        Dim btnPrint As Button = MakeActionButton("Print", 100, MintDark, MintDeep)
        Dim btnPdf As Button = MakeActionButton("Save as PDF", 130, MintDark, MintDeep)
        Dim btnExcel As Button = MakeActionButton("Save as Excel", 140, MintPrimary, MintDeep)
        AddHandler btnPrint.Click, Sub() PrintReport()
        AddHandler btnPdf.Click, Sub() SaveReport(ReportExport.ExportFormat.Pdf, btnPdf)
        AddHandler btnExcel.Click, Sub() SaveReport(ReportExport.ExportFormat.Excel, btnExcel)
        AddHandler titleBar.Resize,
            Sub()
                Dim cy As Integer = (titleBar.Height - btnPrint.Height) \ 2 + 2
                btnPrint.Location = New Point(titleBar.Width - btnPrint.Width - SIDE_PAD, cy)
                btnPdf.Location = New Point(btnPrint.Left - btnPdf.Width - 10, cy)
                btnExcel.Location = New Point(btnPdf.Left - btnExcel.Width - 10, cy)
            End Sub
        titleBar.Controls.AddRange({btnExcel, btnPdf, btnPrint})

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

        addLabel("Type")
        Dim typeNames As New List(Of String) From {ALL_TYPES}
        typeNames.AddRange(_kinds)
        Dim boxType As Panel = FilterFields.MakeSelectField(typeNames, 180,
            Math.Max(0, typeNames.IndexOf(_type)),
            Sub(i As Integer)
                _type = If(i >= 0 AndAlso i < typeNames.Count, typeNames(i), ALL_TYPES)
                ReQuery()
            End Sub)
        boxType.Location = New Point(x, TOP_Y)
        filterBar.Controls.Add(boxType)

        region.Controls.Add(filterBar)
        region.Controls.Add(titleBar)
        Return region
    End Function

    Private Shared Function BuildSearchBox() As Panel
        Dim searchWrap As New Panel With {.Size = New Size(280, FilterFields.FIELD_H), .BackColor = CardBg, .Cursor = Cursors.IBeam}
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
            .Location = New Point(34, 9), .Width = 234
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

        Dim subCount As Label = Nothing
        Dim c1 = MakeSummaryCard("Transactions", MintDark, "in this period", _lblCount, subCount)
        Dim c2 = MakeSummaryCard("Purchase Orders", MintPrimary, "", _lblPo, _subPo)
        Dim c3 = MakeSummaryCard("Paid to Suppliers", MintDeep, "", _lblPaid, _subPaid)
        Dim c4 = MakeSummaryCard("Deliveries", AccentAmber, "", _lblDel, _subDel)
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
                                            ByRef valueLabel As Label, ByRef subLabelCtl As Label) As Panel
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
        subLabelCtl = lblSub
        AddHandler card.Resize, Sub() lblSub.Width = Math.Max(40, card.Width - 36)
        Return card
    End Function

    ' =========================================================================
    '  COLUMN HEADER — same x positions as BuildDataRow
    ' =========================================================================
    Private Shared Function BuildColHeader() As Panel
        Dim hdr As New Panel With {.Height = 44, .BackColor = Color.Transparent}
        Dim labels As Label() = {
            MakeColLabel("DATE"), MakeColLabel("REFERENCE"), MakeColLabel("TYPE"),
            MakeColLabel("SUPPLIER / BY"), MakeColLabel("DETAILS"), MakeColLabel("STATUS"),
            MakeColLabel("AMOUNT")
        }
        hdr.Controls.AddRange(labels)
        AddHandler hdr.Resize,
            Sub()
                Dim w = hdr.Width
                labels(0).Left = 24
                labels(1).Left = CInt(w * X_REF)
                labels(2).Left = CInt(w * X_TYPE)
                labels(3).Left = CInt(w * X_PARTY)
                labels(4).Left = CInt(w * X_DETAILS)
                labels(5).Left = CInt(w * X_STATUS)
                labels(6).Left = w - labels(6).Width - 28
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
                If(_loadFailed, "The report could not be loaded.",
                   "No transactions match these filters. Try a wider date range or a different search.")))
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

    Private Shared Function BuildDataRow(r As TxnRow) As Panel
        Dim canOpen As Boolean = r.PoId > 0 OrElse r.Kind = K_REQUEST
        Dim p As New BufferedPanel With {
            .Height = ROW_H, .BackColor = Color.Transparent,
            .Cursor = If(canOpen, Cursors.Hand, Cursors.Default)
        }
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

        Dim lblDate As Label = mk(r.Dt.ToString("MMM d, yyyy"), New Font("Segoe UI", 10.5F), Theme.TextDark)
        ' Purchase orders are dated by day only; the others carry a time.
        Dim lblTime As Label = mk(If(r.Kind = K_PO OrElse r.Kind = K_PAYMENT, "", r.Dt.ToString("h:mm tt")),
                                  New Font("Segoe UI", 9), FaintText)

        Dim lblRef As Label = mk(r.Ref, New Font("Segoe UI", 10.5F, FontStyle.Bold),
                                 If(canOpen, MintPrimary, MintDark))
        If canOpen Then lblRef.Font = New Font("Segoe UI", 10.5F, FontStyle.Bold Or FontStyle.Underline)

        Dim kc = KindColors(r.Kind)
        Dim badge As Label = mk(r.Kind, New Font("Segoe UI", 8.5F, FontStyle.Bold), kc.fore)
        badge.BackColor = kc.back
        badge.Padding = New Padding(9, 4, 9, 4)
        RoundLabel(badge, 10)

        Dim lblParty As Label = mk(If(r.Party = "", ChrW(8212), r.Party), New Font("Segoe UI", 10), SubText)
        lblParty.AutoSize = False : lblParty.AutoEllipsis = True : lblParty.Height = 20

        Dim lblDetails As Label = mk(r.Details, New Font("Segoe UI", 9.5F), SubText)
        lblDetails.AutoSize = False : lblDetails.AutoEllipsis = True : lblDetails.Height = 20

        Dim lblStatus As Label = mk(r.Status, New Font("Segoe UI Semibold", 9.5F), StatusColor(r))
        lblStatus.AutoSize = False : lblStatus.AutoEllipsis = True : lblStatus.Height = 20

        Dim lblAmt As Label = mk(If(r.Amount.HasValue, Money(r.Amount.Value), ChrW(8212)),
                                 New Font("Segoe UI", 11, FontStyle.Bold),
                                 If(r.Amount.HasValue, If(r.Kind = K_PAYMENT, MintDeep, MintDark), FaintText))

        p.Controls.AddRange({lblDate, lblTime, lblRef, badge, lblParty, lblDetails, lblStatus, lblAmt})

        ' Details can be longer than their column; hovering shows all of it.
        Dim tip As New ToolTip()
        tip.SetToolTip(lblDetails, r.Details)
        If canOpen Then tip.SetToolTip(lblRef, If(r.PoId > 0, "Show the items on " & r.Ref, "Show " & r.Ref))
        AddHandler p.Disposed, Sub() tip.Dispose()

        AddHandler p.Resize,
            Sub()
                Dim w = p.Width
                Dim top1 As Integer = (ROW_H \ 2) - 19
                Dim top2 As Integer = (ROW_H \ 2) + 1
                Dim mid As Integer = (ROW_H - 20) \ 2
                If lblTime.Text = "" Then
                    lblDate.Location = New Point(24, (ROW_H - lblDate.Height) \ 2)
                Else
                    lblDate.Location = New Point(24, top1)
                    lblTime.Location = New Point(24, top2)
                End If
                lblRef.Location = New Point(CInt(w * X_REF), (ROW_H - lblRef.Height) \ 2)
                badge.Location = New Point(CInt(w * X_TYPE), (ROW_H - badge.Height) \ 2)

                Dim xParty As Integer = CInt(w * X_PARTY)
                Dim xDet As Integer = CInt(w * X_DETAILS)
                Dim xSt As Integer = CInt(w * X_STATUS)
                lblParty.Location = New Point(xParty, mid)
                lblParty.Width = Math.Max(40, xDet - xParty - 12)
                lblDetails.Location = New Point(xDet, mid)
                lblDetails.Width = Math.Max(40, xSt - xDet - 12)
                lblAmt.Location = New Point(w - lblAmt.Width - 28, (ROW_H - lblAmt.Height) \ 2)
                lblStatus.Location = New Point(xSt, mid)
                lblStatus.Width = Math.Max(40, lblAmt.Left - xSt - 12)
            End Sub

        If canOpen Then
            Dim open As Action =
                Sub()
                    If r.PoId > 0 Then
                        PoDetailsModal.Show(r.PoId)
                    Else
                        RequestReviewModal.Show(r.RecId)
                    End If
                End Sub
            AddHandler p.Click, Sub() open()
            For Each ctrl As Control In p.Controls
                AddHandler ctrl.Click, Sub() open()
                ctrl.Cursor = Cursors.Hand
            Next
        End If

        PropagateHover(p)
        Return p
    End Function

    Private Shared Function KindColors(kind As String) As (fore As Color, back As Color)
        Select Case kind
            Case K_PO : Return (Color.FromArgb(30, 100, 160), Color.FromArgb(226, 240, 252))
            Case K_DELIVERY : Return (Theme.Forest, Color.FromArgb(220, 248, 240))
            Case K_PAYMENT : Return (Color.FromArgb(0, 110, 90), Color.FromArgb(214, 242, 234))
            Case K_ISSUE : Return (Color.FromArgb(180, 40, 40), Color.FromArgb(255, 233, 233))
            Case K_QUOTE : Return (Color.FromArgb(110, 70, 160), Color.FromArgb(240, 232, 250))
            Case Else : Return (Color.FromArgb(165, 110, 20), Color.FromArgb(255, 244, 224))
        End Select
    End Function

    Private Shared Function StatusColor(r As TxnRow) As Color
        Select Case r.Status
            Case "Rejected", "Cancelled", "Return to Supplier", "With defect / short", "Expired"
                Return AccentRed
            Case "Pending", "Partially Received", "Awaiting Supplier", "Accept as-is", "Draft", "Sent"
                Return AccentAmber
            Case "Received", "Paid", "Approved", "Converted", "Accepted", "Good condition"
                Return MintDeep
            Case Else
                Return SubText
        End Select
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
            _lblPageInfo.Text = "No transactions"
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
    '  PRINT / EXPORT — the same filtered list, every page of it
    ' =========================================================================
    Private Shared Function BuildReportDoc() As ReportPrinter.ReportDoc
        Dim rows As List(Of TxnRow)
        Try
            rows = LoadAllForExport()
        Catch ex As Exception
            AppModal.Show("Could not build the report:" & Environment.NewLine & ErrorLog.ForUser(ex),
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return Nothing
        End Try

        Dim title As String = "Procurement Transactions"
        If _type <> ALL_TYPES Then title &= " - " & _type
        Dim period As String = _fromDate.ToString("MMM d, yyyy") & " - " & _toDate.ToString("MMM d, yyyy")
        If _search <> "" Then period &= "   (search: " & _search & ")"

        Dim doc As New ReportPrinter.ReportDoc With {
            .Title = title, .PeriodLabel = "Period", .PeriodValue = period,
            .Columns = New List(Of ReportPrinter.Column) From {
                ReportPrinter.Col("Date", 1.0F),
                ReportPrinter.Col("Reference", 1.0F),
                ReportPrinter.Col("Type", 1.1F),
                ReportPrinter.Col("Supplier / By", 1.4F),
                ReportPrinter.Col("Details", 2.2F),
                ReportPrinter.Col("Status", 1.1F),
                ReportPrinter.Col("Amount", 1.1F, ReportPrinter.ColumnKind.Money)
            }
        }
        doc.Rows.Add(ReportPrinter.TableHeader())
        If rows.Count = 0 Then
            doc.Rows.Add(ReportPrinter.TableRow("No transactions match these filters.", "", "", "", "", "", ""))
        Else
            For Each r In rows
                doc.Rows.Add(ReportPrinter.TableRow(
                    r.Dt.ToString("MMM d, yyyy"), r.Ref, r.Kind, r.Party, r.Details, r.Status,
                    If(r.Amount.HasValue, Money(r.Amount.Value), "")))
            Next
        End If
        doc.Rows.Add(ReportPrinter.TableTotal("TOTAL",
            rows.Count.ToString("N0") & If(rows.Count = 1, " transaction", " transactions"),
            "", "", "POs " & Money(_poAmount) & "   Paid " & Money(_payAmount), "", ""))
        Return doc
    End Function

    Private Shared Sub PrintReport()
        Dim doc As ReportPrinter.ReportDoc = BuildReportDoc()
        If doc Is Nothing Then Return
        ActivityLog.Record("REPORT_EXPORT", "report", Nothing,
                           Left(doc.Title & " (Print) - " & doc.PeriodValue, 250))
        ReportPrinter.Preview(doc, companyName:=ReportPrinter.CompanyName(),
                              preparedBy:=Session.CurrentFullName)
    End Sub

    Private Shared Sub SaveReport(format As ReportExport.ExportFormat, owner As Control)
        Dim doc As ReportPrinter.ReportDoc = BuildReportDoc()
        If doc Is Nothing Then Return
        ReportExport.SaveWithDialog(doc, format, ReportPrinter.CompanyName(), Session.CurrentFullName,
                                    owner.FindForm())
    End Sub

    ' =========================================================================
    '  HELPERS
    ' =========================================================================
    Private Shared Function Money(v As Decimal) As String
        If v < 0 Then Return "(" & Peso & (-v).ToString("N2", CultureInfo.InvariantCulture) & ")"
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
