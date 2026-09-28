Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports MySql.Data.MySqlClient

' =============================================================================
'  AdminProcurementOverviewPanelBuilder  —  Admin ▸ Procurement Overview
'                                            (READ-ONLY)
'
'  Store-wide oversight of purchasing. The Procurement role keeps every
'  operational screen (raising POs, receiving, quoting); nothing here writes.
'
'  This is where the admin dashboard's "Pending POs" tile lands.
'
'  THREE TABS, each with its own filter, summary and paged list:
'    0  Purchase Orders  (default)
'    1  Purchase Requests
'    2  Quotations
'  The tab strip reuses the pill styling from AccountantSalesHistoryPanelBuilder.
'
'  LAYOUT:
'    TopRegion (Dock = Top, 180px):
'      ├─ TitleBar  (72px) : title + "Read-only" pill + Refresh
'      ├─ TabBar    (52px) : the three segment pills
'      └─ FilterBar (56px) : per-tab status (+ date range on the PO tab)
'    SummaryStrip (Dock = Top, 110px) : 4 cards over the FILTERED set
'    TableWrapper (Dock = Fill) : card with header / rows / bottomPad / pager
'
'  Schema was verified against the live database before writing:
'    purchase_orders   po_id po_no supplier_id created_by order_date
'                      expected_date status total_cost received_at
'    purchase_requests request_id product_id requested_by qty_requested
'                      status note created_at reviewed_by reviewed_at
'    quotations        quotation_id quotation_no request_id supplier_id
'                      created_by created_at valid_until status notes
'                      total_amount
' =============================================================================
Public Class AdminProcurementOverviewPanelBuilder

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
    Private Shared ReadOnly AccentAmber As Color = Color.FromArgb(210, 140, 40)
    Private Shared ReadOnly Peso As String = ChrW(8369)

    Private Shared ReadOnly BadgeGood As (fore As Color, back As Color) =
        (Color.FromArgb(0, 120, 92), Color.FromArgb(220, 248, 240))
    Private Shared ReadOnly BadgeWarn As (fore As Color, back As Color) =
        (Color.FromArgb(165, 110, 20), Color.FromArgb(255, 244, 224))
    Private Shared ReadOnly BadgeBad As (fore As Color, back As Color) =
        (Color.FromArgb(180, 40, 40), Color.FromArgb(255, 233, 233))
    Private Shared ReadOnly BadgeInfo As (fore As Color, back As Color) =
        (Color.FromArgb(45, 80, 150), Color.FromArgb(232, 238, 250))
    Private Shared ReadOnly BadgeNeutral As (fore As Color, back As Color) =
        (Color.FromArgb(120, 120, 120), Color.FromArgb(238, 238, 238))

    ' Must match the admin dashboard's Pending POs tile exactly.
    Private Const PENDING_PO_SQL As String =
        "po.status IN ('Draft','Ordered','Partially Received')"

    ' Status values, verified against the live ENUM definitions.
    Private Shared ReadOnly PoStatuses() As String =
        {"Draft", "Ordered", "Partially Received", "Received", "Cancelled"}
    Private Shared ReadOnly PrStatuses() As String =
        {"Pending", "Approved", "Rejected", "Quoted", "Converted"}
    Private Shared ReadOnly QuoteStatuses() As String =
        {"Draft", "Sent", "Accepted", "Rejected", "Expired"}

    Private Shared ReadOnly TabLabels() As String =
        {"Purchase Orders", "Purchase Requests", "Price Quotations"}

    ' ── DATA MODEL ────────────────────────────────────────────────────────────
    Private Class PoRow
        Public PoNo As String
        Public Supplier As String
        Public CreatedBy As String
        Public OrderDate As DateTime
        Public ExpectedDate As DateTime
        Public Status As String
        Public TotalCost As Decimal
    End Class

    Private Class PrRow
        Public RequestId As Integer
        Public ProductName As String
        Public QtyRequested As Integer
        Public RequestedBy As String
        Public Status As String
        Public CreatedAt As DateTime
        Public Note As String

        ' A request is a basket of products, so approving it means seeing all of
        ' them. ItemCount is how many lines it carries and ItemSummary lists
        ' them; a single-product request reads exactly as it always did.
        Public ItemCount As Integer
        Public ItemSummary As String
        Public TotalUnits As Integer

        Public ReadOnly Property IsMultiLine As Boolean
            Get
                Return ItemCount > 1
            End Get
        End Property
    End Class

    Private Class QuoteRow
        Public QuotationNo As String
        Public Supplier As String
        Public CreatedBy As String
        Public CreatedAt As DateTime
        Public ValidUntil As DateTime
        Public Status As String
        Public TotalAmount As Decimal
    End Class

    ' ── STATE ─────────────────────────────────────────────────────────────────
    Private Const ALL_STATUS As String = "All statuses"
    Private Const SIDE_PAD As Integer = 24
    Private Const ROW_H As Integer = 72
    ' Matches the house KPI band used by Users / Suppliers / Categories and the
    ' other oversight screens: 140px of card, caption 11pt at (22,14), value
    ' 26pt bold at (20,36), sub-label 10pt at (22,100), 12px between cards.
    ' A card that is nearly the same size as the rest reads as a mistake.
    Private Const SUMMARY_H As Integer = 140
    Private Const MAX_PAGE_SIZE As Integer = 50
    ' topRegion 180 + summary (140+12) + spacers 16+16 + card padding 2
    ' + colHeader 44 + pagerBar 52 + bottomPad 12
    ' Keep in step with SUMMARY_H or the table misjudges its viewport.
    Private Const CHROME_H As Integer = 474
    Private Shared _pageSize As Integer = MAX_PAGE_SIZE

    Private Shared _container As Panel
    Private Shared _scrollHost As Panel

    Private Shared _activeTab As Integer = 0
    Private Shared _statusFilter As String = ALL_STATUS
    Private Shared _fromDate As DateTime
    Private Shared _toDate As DateTime

    Private Shared _poRows As New List(Of PoRow)
    Private Shared _prRows As New List(Of PrRow)
    Private Shared _quoteRows As New List(Of QuoteRow)

    Private Shared _totalRows As Integer = 0
    Private Shared _currentPage As Integer = 0
    Private Shared _reflowAttached As Boolean = False

    ' Four generic summary slots, filled per tab.
    Private Shared _sumCaptions(3) As String
    Private Shared _sumSubLabels(3) As Label
    Private Shared _sumValues(3) As String
    Private Shared _sumColors(3) As Color
    Private Shared _sumLabels(3) As Label

    Private Shared _lblPageInfo As Label
    Private Shared _btnPrev, _btnNext As Button

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    ' startTab / startStatus let the dashboard open straight onto, say, the
    ' requests waiting for approval (tab 1, "Pending").
    Public Shared Sub LoadProcurementOverview(container As Panel,
                                              Optional startTab As Integer = 0,
                                              Optional startStatus As String = Nothing)
        _container = container
        _activeTab = Math.Max(0, Math.Min(TabLabels.Length - 1, startTab))
        ResetFiltersForTab()
        If Not String.IsNullOrEmpty(startStatus) AndAlso
           Array.IndexOf(StatusesForTab(), startStatus) >= 0 Then
            _statusFilter = startStatus
        End If
        ComputePageSize()
        LoadAll()
        Render()
    End Sub

    Private Shared Sub ResetFiltersForTab()
        _statusFilter = ALL_STATUS
        _toDate = DateTime.Today
        _fromDate = DateTime.Today.AddDays(-29)     ' POs: last 30 days
        _currentPage = 0
    End Sub

    Private Shared Sub ComputePageSize()
        _pageSize = MAX_PAGE_SIZE
        If _container Is Nothing Then Return
        Dim viewport As Integer = _container.ClientSize.Height - CHROME_H
        If viewport < 120 Then Return          ' not laid out yet — keep the ceiling
        Dim fits As Integer = (viewport - 4) \ ROW_H
        _pageSize = Math.Max(4, Math.Min(MAX_PAGE_SIZE, fits))
    End Sub

    ' Switching tab rebuilds the whole panel — the filter bar differs per tab.
    Private Shared Sub SwitchTab(index As Integer)
        If index = _activeTab Then Return
        _activeTab = index
        ResetFiltersForTab()
        LoadAll()
        Render()
    End Sub

    ' Filter change inside a tab: re-query only, so the controls keep state.
    Private Shared Sub ReQuery(Optional resetPage As Boolean = True)
        If resetPage Then _currentPage = 0
        LoadAll()
        PopulateRows()
        UpdateSummaryLabels()
        UpdatePager()
    End Sub

    Private Shared Sub LoadAll()
        Select Case _activeTab
            Case 0 : LoadPurchaseOrders()
            Case 1 : LoadPurchaseRequests()
            Case Else : LoadQuotations()
        End Select
    End Sub

    ' =========================================================================
    '  TAB 0 — PURCHASE ORDERS
    ' =========================================================================
    Private Shared Function PoJoins() As String
        Return "FROM purchase_orders po " &
               "JOIN suppliers s ON s.supplier_id = po.supplier_id " &
               "LEFT JOIN users u ON u.user_id = po.created_by "
    End Function

    Private Shared Function PoWhere() As String
        Dim sb As New System.Text.StringBuilder(
            "WHERE po.order_date >= @fromD AND po.order_date < @toD ")
        If _statusFilter <> ALL_STATUS Then sb.Append("AND po.status = @st ")
        Return sb.ToString()
    End Function

    Private Shared Sub AddPoParams(cmd As MySqlCommand)
        cmd.Parameters.AddWithValue("@fromD", _fromDate.Date)
        ' Exclusive upper bound so same-day orders are kept.
        cmd.Parameters.AddWithValue("@toD", _toDate.Date.AddDays(1))
        If _statusFilter <> ALL_STATUS Then cmd.Parameters.AddWithValue("@st", _statusFilter)
    End Sub

    Private Shared Sub LoadPurchaseOrders()
        _poRows.Clear()
        _totalRows = 0
        SetSummary(0, "Total POs", MintDark, "0")
        SetSummary(1, "Pending", AccentAmber, "0")
        SetSummary(2, "Received", MintPrimary, "0")
        SetSummary(3, "Total Cost", MintDeep, Money(0D))

        Try
            Using conn = Db.GetConnection()
                conn.Open()

                ' Summary over the filtered set — same joins + WHERE as the list.
                Using cmd As New MySqlCommand(
                    "SELECT COUNT(*) AS n, " &
                    "SUM(CASE WHEN " & PENDING_PO_SQL & " THEN 1 ELSE 0 END) AS pending_n, " &
                    "SUM(CASE WHEN po.status = 'Received' THEN 1 ELSE 0 END) AS recv_n, " &
                    "COALESCE(SUM(po.total_cost),0) AS total_cost " &
                    PoJoins() & PoWhere(), conn)
                    AddPoParams(cmd)
                    Using rdr = cmd.ExecuteReader()
                        If rdr.Read() Then
                            _totalRows = SafeInt(rdr("n"))
                            SetSummary(0, "Total POs", MintDark, _totalRows.ToString("N0"))
                            SetSummary(1, "Pending", AccentAmber, SafeInt(rdr("pending_n")).ToString("N0"))
                            SetSummary(2, "Received", MintPrimary, SafeInt(rdr("recv_n")).ToString("N0"))
                            SetSummary(3, "Total Cost", MintDeep, Money(SafeDec(rdr("total_cost"))))
                        End If
                    End Using
                End Using

                ClampPage()

                Using cmd As New MySqlCommand(
                    "SELECT po.po_no, s.name AS sname, " &
                    "COALESCE(u.full_name,'—') AS uname, " &
                    "po.order_date, po.expected_date, po.status, po.total_cost " &
                    PoJoins() & PoWhere() &
                    "ORDER BY po.order_date DESC, po.po_id DESC " &
                    "LIMIT @lim OFFSET @off", conn)
                    AddPoParams(cmd)
                    AddPageParams(cmd)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            _poRows.Add(New PoRow With {
                                .PoNo = SafeStr(rdr("po_no")),
                                .Supplier = SafeStr(rdr("sname")),
                                .CreatedBy = SafeStr(rdr("uname")),
                                .OrderDate = SafeDate(rdr("order_date")),
                                .ExpectedDate = SafeDate(rdr("expected_date")),
                                .Status = SafeStr(rdr("status")),
                                .TotalCost = SafeDec(rdr("total_cost"))
                            })
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load purchase orders: " & ex.Message, "Database Error",
                          MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    ' =========================================================================
    '  TAB 1 — PURCHASE REQUESTS
    ' =========================================================================
    Private Shared Function PrJoins() As String
        Return "FROM purchase_requests pr " &
               "JOIN products p ON p.product_id = pr.product_id " &
               "LEFT JOIN users u ON u.user_id = pr.requested_by "
    End Function

    Private Shared Function PrWhere() As String
        If _statusFilter <> ALL_STATUS Then Return "WHERE pr.status = @st "
        Return "WHERE 1 = 1 "
    End Function

    Private Shared Sub AddPrParams(cmd As MySqlCommand)
        If _statusFilter <> ALL_STATUS Then cmd.Parameters.AddWithValue("@st", _statusFilter)
    End Sub

    Private Shared Sub LoadPurchaseRequests()
        _prRows.Clear()
        _totalRows = 0
        SetSummary(0, "Total Requests", MintDark, "0")
        SetSummary(1, "Waiting Approval", AccentAmber, "0")
        SetSummary(2, "Approved", MintPrimary, "0")
        SetSummary(3, "Rejected", AccentRed, "0")

        Try
            Using conn = Db.GetConnection()
                conn.Open()

                Using cmd As New MySqlCommand(
                    "SELECT COUNT(*) AS n, " &
                    "SUM(CASE WHEN pr.status = 'Pending' THEN 1 ELSE 0 END) AS pend_n, " &
                    "SUM(CASE WHEN pr.status = 'Approved' THEN 1 ELSE 0 END) AS appr_n, " &
                    "SUM(CASE WHEN pr.status = 'Rejected' THEN 1 ELSE 0 END) AS rej_n " &
                    PrJoins() & PrWhere(), conn)
                    AddPrParams(cmd)
                    Using rdr = cmd.ExecuteReader()
                        If rdr.Read() Then
                            _totalRows = SafeInt(rdr("n"))
                            SetSummary(0, "Total Requests", MintDark, _totalRows.ToString("N0"))
                            SetSummary(1, "Waiting Approval", AccentAmber, SafeInt(rdr("pend_n")).ToString("N0"))
                            SetSummary(2, "Approved", MintPrimary, SafeInt(rdr("appr_n")).ToString("N0"))
                            SetSummary(3, "Rejected", AccentRed, SafeInt(rdr("rej_n")).ToString("N0"))
                        End If
                    End Using
                End Using

                ClampPage()

                ' The line counts come from purchase_request_items, which every
                ' request has had since migration 14 backfilled one line per
                ' existing row. COALESCE keeps the old single-product figures
                ' correct even if a request somehow has no lines.
                Using cmd As New MySqlCommand(
                    "SELECT pr.request_id, p.name AS pname, pr.qty_requested, " &
                    "COALESCE(u.full_name,'—') AS uname, " &
                    "pr.status, pr.created_at, COALESCE(pr.note,'') AS note, " &
                    "(SELECT COUNT(*) FROM purchase_request_items pri " &
                    " WHERE pri.request_id = pr.request_id) AS n_items, " &
                    "(SELECT COALESCE(SUM(pri.qty_requested),0) FROM purchase_request_items pri " &
                    " WHERE pri.request_id = pr.request_id) AS n_units " &
                    PrJoins() & PrWhere() &
                    "ORDER BY pr.created_at DESC, pr.request_id DESC " &
                    "LIMIT @lim OFFSET @off", conn)
                    AddPrParams(cmd)
                    AddPageParams(cmd)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            Dim nItems As Integer = SafeInt(rdr("n_items"))
                            Dim nUnits As Integer = SafeInt(rdr("n_units"))
                            _prRows.Add(New PrRow With {
                                .RequestId = SafeInt(rdr("request_id")),
                                .ProductName = SafeStr(rdr("pname")),
                                .QtyRequested = SafeInt(rdr("qty_requested")),
                                .RequestedBy = SafeStr(rdr("uname")),
                                .Status = SafeStr(rdr("status")),
                                .CreatedAt = SafeDate(rdr("created_at")),
                                .Note = SafeStr(rdr("note")),
                                .ItemCount = Math.Max(nItems, 1),
                                .TotalUnits = If(nUnits > 0, nUnits, SafeInt(rdr("qty_requested")))
                            })
                        End While
                    End Using
                End Using

                ' Second pass for the product names on multi-line requests. Kept
                ' separate so single-product requests, still the common case,
                ' cost exactly what they always did.
                For Each row In _prRows
                    If row.ItemCount > 1 Then
                        row.ItemSummary = LoadRequestItemSummary(conn, row.RequestId)
                    End If
                Next
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load purchase requests: " & ex.Message, "Database Error",
                          MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    ' The products on one request, as a single readable line. The approver is
    ' deciding on the basket, so the basket has to be visible on the row rather
    ' than only behind a click.
    Private Shared Function LoadRequestItemSummary(conn As MySqlConnection,
                                                   requestId As Integer) As String
        Dim parts As New List(Of String)
        Try
            Using cmd As New MySqlCommand(
                "SELECT p.name AS pname, pri.qty_requested " &
                "FROM purchase_request_items pri " &
                "JOIN products p ON p.product_id = pri.product_id " &
                "WHERE pri.request_id = @rid " &
                "ORDER BY pri.pr_item_id", conn)
                cmd.Parameters.AddWithValue("@rid", requestId)
                Using rdr = cmd.ExecuteReader()
                    While rdr.Read()
                        parts.Add(SafeStr(rdr("pname")) & " " & ChrW(215) & " " &
                                  SafeInt(rdr("qty_requested")))
                    End While
                End Using
            End Using
        Catch
            ' Non-fatal: the row still renders with its count and unit total.
            Return ""
        End Try
        Return String.Join("   " & ChrW(8226) & "   ", parts)
    End Function

    ' =========================================================================
    '  TAB 2 — QUOTATIONS
    ' =========================================================================
    Private Shared Function QuoteJoins() As String
        Return "FROM quotations q " &
               "JOIN suppliers s ON s.supplier_id = q.supplier_id " &
               "LEFT JOIN users u ON u.user_id = q.created_by "
    End Function

    Private Shared Function QuoteWhere() As String
        If _statusFilter <> ALL_STATUS Then Return "WHERE q.status = @st "
        Return "WHERE 1 = 1 "
    End Function

    Private Shared Sub AddQuoteParams(cmd As MySqlCommand)
        If _statusFilter <> ALL_STATUS Then cmd.Parameters.AddWithValue("@st", _statusFilter)
    End Sub

    Private Shared Sub LoadQuotations()
        _quoteRows.Clear()
        _totalRows = 0
        SetSummary(0, "Total Price Quotations", MintDark, "0")
        SetSummary(1, "Sent", AccentAmber, "0")
        SetSummary(2, "Accepted", MintPrimary, "0")
        SetSummary(3, "Total Amount", MintDeep, Money(0D))

        Try
            Using conn = Db.GetConnection()
                conn.Open()

                Using cmd As New MySqlCommand(
                    "SELECT COUNT(*) AS n, " &
                    "SUM(CASE WHEN q.status = 'Sent' THEN 1 ELSE 0 END) AS sent_n, " &
                    "SUM(CASE WHEN q.status = 'Accepted' THEN 1 ELSE 0 END) AS acc_n, " &
                    "COALESCE(SUM(q.total_amount),0) AS total_amt " &
                    QuoteJoins() & QuoteWhere(), conn)
                    AddQuoteParams(cmd)
                    Using rdr = cmd.ExecuteReader()
                        If rdr.Read() Then
                            _totalRows = SafeInt(rdr("n"))
                            SetSummary(0, "Total Price Quotations", MintDark, _totalRows.ToString("N0"))
                            SetSummary(1, "Sent", AccentAmber, SafeInt(rdr("sent_n")).ToString("N0"))
                            SetSummary(2, "Accepted", MintPrimary, SafeInt(rdr("acc_n")).ToString("N0"))
                            SetSummary(3, "Total Amount", MintDeep, Money(SafeDec(rdr("total_amt"))))
                        End If
                    End Using
                End Using

                ClampPage()

                Using cmd As New MySqlCommand(
                    "SELECT q.quotation_no, s.name AS sname, " &
                    "COALESCE(u.full_name,'—') AS uname, " &
                    "q.created_at, q.valid_until, q.status, q.total_amount " &
                    QuoteJoins() & QuoteWhere() &
                    "ORDER BY q.created_at DESC, q.quotation_id DESC " &
                    "LIMIT @lim OFFSET @off", conn)
                    AddQuoteParams(cmd)
                    AddPageParams(cmd)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            _quoteRows.Add(New QuoteRow With {
                                .QuotationNo = SafeStr(rdr("quotation_no")),
                                .Supplier = SafeStr(rdr("sname")),
                                .CreatedBy = SafeStr(rdr("uname")),
                                .CreatedAt = SafeDate(rdr("created_at")),
                                .ValidUntil = SafeDate(rdr("valid_until")),
                                .Status = SafeStr(rdr("status")),
                                .TotalAmount = SafeDec(rdr("total_amount"))
                            })
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load quotations: " & ex.Message, "Database Error",
                          MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    ' ── Shared query plumbing ────────────────────────────────────────────────
    Private Shared Sub ClampPage()
        Dim totalPages As Integer = Math.Max(1, CInt(Math.Ceiling(_totalRows / _pageSize)))
        If _currentPage > totalPages - 1 Then _currentPage = totalPages - 1
        If _currentPage < 0 Then _currentPage = 0
    End Sub

    Private Shared Sub AddPageParams(cmd As MySqlCommand)
        cmd.Parameters.AddWithValue("@lim", _pageSize)
        cmd.Parameters.AddWithValue("@off", _currentPage * _pageSize)
    End Sub

    ' One line of context under each figure, keyed by the caption the tab set.
    Private Shared Function SubLabelFor(caption As String) As String
        Select Case caption
            Case "Total POs" : Return "in the date range"
            Case "Total Requests" : Return "raised by staff"
            Case "Total Price Quotations" : Return "from suppliers"
            Case "Pending" : Return "awaiting action"
            Case "Waiting Approval" : Return "click a request to review it"
            Case "Rejected" : Return "sent back with a reason"
            Case "Received" : Return "delivered in full"
            Case "Approved" : Return "cleared to order"
            Case "Sent" : Return "out for pricing"
            Case "Accepted" : Return "agreed with supplier"
            Case "Converted" : Return "now purchase orders"
            Case "Total Cost" : Return "ordered value"
            Case "Total Amount" : Return "quoted value"
            Case Else : Return ""
        End Select
    End Function

    Private Shared Sub SetSummary(i As Integer, caption As String, clr As Color, value As String)
        _sumCaptions(i) = caption
        _sumColors(i) = clr
        _sumValues(i) = value
    End Sub

    Private Shared Function CurrentRowCount() As Integer
        Select Case _activeTab
            Case 0 : Return _poRows.Count
            Case 1 : Return _prRows.Count
            Case Else : Return _quoteRows.Count
        End Select
    End Function

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

    Private Shared Function SafeDate(o As Object) As DateTime
        If o Is Nothing OrElse IsDBNull(o) Then Return DateTime.MinValue
        Return Convert.ToDateTime(o)
    End Function

    Private Shared Function Money(v As Decimal) As String
        Return Peso & v.ToString("N2")
    End Function

    Private Shared Function DateOrDash(d As DateTime, fmt As String) As String
        If d = DateTime.MinValue Then Return "—"
        Return d.ToString(fmt)
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

        ' bottomPad added before pagerBar so the gap sits between the last row
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
    '  TOP REGION  —  title 72 + tabs 52 + filters 56
    ' =========================================================================
    Private Shared Function BuildTopRegion() As Panel
        Dim region As New Panel With {.BackColor = MintBg, .AutoSize = False, .Height = 180}

        ' ── Title bar ────────────────────────────────────────────────────────
        Dim titleBar As New Panel With {
            .Height = 72, .Dock = DockStyle.Top, .BackColor = Color.Transparent
        }
        titleBar.Controls.Add(New Label With {
            .Text = "Procurement Overview",
            .Font = New Font("Segoe UI", 28, FontStyle.Bold),
            .ForeColor = MintDark,
            .AutoSize = True,
            .Location = New Point(SIDE_PAD, 16)
        })

        ' Purchase requests are decided here; the other two tabs only watch.
        Dim pill As New Label With {
            .Text = If(_activeTab = 1, "Approve / reject requests", "Read-only"),
            .Font = New Font("Segoe UI Semibold", 9.0F),
            .ForeColor = SubText,
            .BackColor = Color.FromArgb(232, 238, 236),
            .AutoSize = True,
            .Padding = New Padding(10, 5, 10, 5),
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        RoundLabel(pill, 12)

        AddHandler titleBar.Resize,
            Sub()
                pill.Left = titleBar.Width - pill.Width - SIDE_PAD
                pill.Top = (titleBar.Height - pill.Height) \ 2
            End Sub

        titleBar.Controls.Add(pill)

        ' ── Tab strip (segment pills) ────────────────────────────────────────
        Dim tabBar As New Panel With {
            .Height = 52, .Dock = DockStyle.Top, .BackColor = Color.Transparent
        }
        Dim tabHost As New FlowLayoutPanel With {
            .FlowDirection = FlowDirection.LeftToRight,
            .AutoSize = True,
            .BackColor = Color.Transparent,
            .WrapContents = False,
            .Location = New Point(SIDE_PAD, 8)
        }

        For i As Integer = 0 To TabLabels.Length - 1
            Dim idx As Integer = i
            Dim isActive As Boolean = (i = _activeTab)

            Dim tab As New Button With {
                .Text = TabLabels(i),
                .FlatStyle = FlatStyle.Flat,
                .Cursor = Cursors.Hand,
                .Size = New Size(160, 34),
                .Margin = New Padding(0, 0, 6, 0)
            }
            tab.FlatAppearance.BorderSize = 0

            Dim styleTab As Action(Of Boolean, Boolean) =
                Sub(active As Boolean, hover As Boolean)
                    If active Then
                        tab.BackColor = MintPrimary
                        tab.ForeColor = Color.White
                        tab.Font = New Font("Segoe UI Semibold", 9.5F)
                    ElseIf hover Then
                        tab.BackColor = Color.FromArgb(210, 240, 235)
                        tab.ForeColor = MintDark
                        tab.Font = New Font("Segoe UI", 9.5F)
                    Else
                        tab.BackColor = Color.FromArgb(220, 235, 232)
                        tab.ForeColor = SubText
                        tab.Font = New Font("Segoe UI", 9.5F)
                    End If
                    tab.Invalidate()
                End Sub

            AddHandler tab.Paint,
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

            AddHandler tab.MouseEnter, Sub() If idx <> _activeTab Then styleTab(False, True)
            AddHandler tab.MouseLeave, Sub() If idx <> _activeTab Then styleTab(False, False)
            AddHandler tab.Click, Sub() SwitchTab(idx)

            styleTab(isActive, False)
            tabHost.Controls.Add(tab)
        Next

        tabBar.Controls.Add(tabHost)

        ' ── Filter bar (per tab) ─────────────────────────────────────────────
        Dim filterBar As New Panel With {
            .Height = 56, .Dock = DockStyle.Top, .BackColor = Color.Transparent
        }
        Dim x As Integer = SIDE_PAD

        ' Date range only applies to purchase orders (order_date).
        If _activeTab = 0 Then
            Dim lblFrom As New Label With {
                .Text = "From", .Font = New Font("Segoe UI", 9.5F), .ForeColor = SubText,
                .AutoSize = True, .Location = New Point(x, 19), .BackColor = Color.Transparent
            }
            ' Declared first so each picker can nudge the other when the
            ' range inverts; assigned immediately below.
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
            boxFrom.Location = New Point(x + 44, 11)

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

            filterBar.Controls.AddRange({lblFrom, boxFrom, lblTo, boxTo})
            x = boxTo.Right + 24
        End If

        Dim lblStatus As New Label With {
            .Text = "Status", .Font = New Font("Segoe UI", 9.5F), .ForeColor = SubText,
            .AutoSize = True, .Location = New Point(x, 19), .BackColor = Color.Transparent
        }
        Dim statusNames As New List(Of String) From {ALL_STATUS}
        For Each st In StatusesForTab()
            statusNames.Add(st)
        Next
        Dim boxStatus As Panel = FilterFields.MakeSelectField(statusNames, 186,
            Math.Max(0, statusNames.IndexOf(_statusFilter)),
            Sub(i As Integer)
                _statusFilter = If(i >= 0 AndAlso i < statusNames.Count,
                                   statusNames(i), ALL_STATUS)
                ReQuery()
            End Sub)
        boxStatus.Location = New Point(x + 52, 11)

        filterBar.Controls.Add(lblStatus)
        filterBar.Controls.Add(boxStatus)

        region.Controls.Add(filterBar)
        region.Controls.Add(tabBar)
        region.Controls.Add(titleBar)
        Return region
    End Function

    Private Shared Function StatusesForTab() As String()
        Select Case _activeTab
            Case 0 : Return PoStatuses
            Case 1 : Return PrStatuses
            Case Else : Return QuoteStatuses
        End Select
    End Function

    ' =========================================================================
    '  SUMMARY STRIP
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

        For i As Integer = 0 To 3
            Dim card As Panel = MakeSummaryCard(i)
            card.Margin = New Padding(0, 0, If(i < 3, 12, 0), 0)
            grid.Controls.Add(card, i, 0)
        Next

        bar.Controls.Add(grid)
        Return bar
    End Function

    Private Shared Function MakeSummaryCard(i As Integer) As Panel
        Dim card As New BufferedPanel With {.Dock = DockStyle.Fill, .BackColor = CardBg}
        AddHandler card.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)

        card.Controls.Add(New Label With {
            .Text = If(_sumCaptions(i), ""),
            .Font = New Font("Segoe UI", 11),
            .ForeColor = SubText,
            .AutoSize = True,
            .Location = New Point(22, 14),
            .BackColor = Color.Transparent
        })

        _sumLabels(i) = New Label With {
            .Text = If(_sumValues(i), "—"),
            .Font = New Font("Segoe UI", 26, FontStyle.Bold),
            .ForeColor = _sumColors(i),
            .AutoSize = True,
            .Location = New Point(20, 36),
            .BackColor = Color.Transparent
        }
        card.Controls.Add(_sumLabels(i))

        ' The captions are already tab-specific, so the sub-label is derived
        ' from the caption rather than threaded through all 24 SetSummary calls.
        _sumSubLabels(i) = New Label With {
            .Text = SubLabelFor(If(_sumCaptions(i), "")),
            .Font = New Font("Segoe UI", 10),
            .ForeColor = SubText,
            .AutoSize = True,
            .Location = New Point(22, 100),
            .BackColor = Color.Transparent
        }
        card.Controls.Add(_sumSubLabels(i))
        Return card
    End Function

    ' Filters change the figures, so labels update in place — rebuilding the
    ' strip would rebuild the filter bar and drop the dropdown selection.
    Private Shared Sub UpdateSummaryLabels()
        For i As Integer = 0 To 3
            If _sumLabels(i) IsNot Nothing Then
                _sumLabels(i).Text = If(_sumValues(i), "—")
                _sumLabels(i).ForeColor = _sumColors(i)
            End If
            If _sumSubLabels(i) IsNot Nothing Then
                _sumSubLabels(i).Text = SubLabelFor(If(_sumCaptions(i), ""))
            End If
        Next
    End Sub

    ' =========================================================================
    '  COLUMN HEADER  —  per tab
    ' =========================================================================
    Private Shared Function BuildColHeader() As Panel
        Dim hdr As New Panel With {.Height = 44, .BackColor = Color.Transparent}

        Dim texts As String()
        Dim stops As Double()
        Select Case _activeTab
            Case 0
                texts = {"PO NO / BY", "SUPPLIER", "ORDERED / EXPECTED", "STATUS", "TOTAL COST"}
                stops = {0.0, 0.22, 0.42, 0.62, 0.78}
            Case 1
                texts = {"REQUEST / PRODUCTS", "QTY", "REQUESTED BY", "STATUS", "CREATED"}
                stops = {0.0, 0.32, 0.4, 0.56, 0.7}
            Case Else
                texts = {"QUOTATION / BY", "SUPPLIER", "CREATED / VALID UNTIL", "STATUS", "TOTAL"}
                stops = {0.0, 0.22, 0.42, 0.62, 0.78}
        End Select

        Dim labels(texts.Length - 1) As Label
        For i As Integer = 0 To texts.Length - 1
            labels(i) = MakeColLabel(texts(i), New Point(24, 13))
        Next
        hdr.Controls.AddRange(labels)

        AddHandler hdr.Resize,
            Sub()
                Dim w = hdr.Width
                For i As Integer = 0 To labels.Length - 1
                    labels(i).Left = If(stops(i) = 0.0, 24, CInt(w * stops(i)))
                Next
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

        If CurrentRowCount() = 0 Then
            _scrollHost.Controls.Add(BuildEmptyRow("Nothing matches the current filters."))
        Else
            Dim y As Integer = 4
            Select Case _activeTab
                Case 0
                    For Each r In _poRows
                        AddRow(BuildPoRow(r, ROW_H), y)
                        y += ROW_H
                    Next
                Case 1
                    For Each r In _prRows
                        AddRow(BuildPrRow(r, ROW_H), y)
                        y += ROW_H
                    Next
                Case Else
                    For Each r In _quoteRows
                        AddRow(BuildQuoteRow(r, ROW_H), y)
                        y += ROW_H
                    Next
            End Select

            If Not _reflowAttached Then
                _reflowAttached = True
                AddHandler _scrollHost.Resize, AddressOf ReflowScrollHost
            End If
        End If

        _scrollHost.ResumeLayout()
        _scrollHost.Invalidate(True)
    End Sub

    Private Shared Sub AddRow(row As Panel, y As Integer)
        row.Location = New Point(0, y)
        row.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        _scrollHost.Controls.Add(row)
        row.Width = _scrollHost.ClientSize.Width - 4
    End Sub

    Private Shared Sub ReflowScrollHost(sender As Object, e As EventArgs)
        If _scrollHost Is Nothing Then Return
        Dim w As Integer = _scrollHost.ClientSize.Width - 4
        For Each c As Control In _scrollHost.Controls
            c.Width = w
        Next
    End Sub

    ' Shared row chrome: hover pill and bottom divider.
    Private Shared Function NewRowShell(height As Integer, accent As Color) As BufferedPanel
        Dim p As New BufferedPanel With {.Height = height, .BackColor = Color.Transparent}
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
        Return p
    End Function

    Private Shared Function MakeBadge(text As String, clr As (fore As Color, back As Color)) As Label
        Dim b As New Label With {
            .Text = If(text = "", "—", text),
            .Font = New Font("Segoe UI", 9, FontStyle.Bold),
            .ForeColor = clr.fore,
            .BackColor = clr.back,
            .AutoSize = True,
            .Padding = New Padding(10, 4, 10, 4)
        }
        RoundLabel(b, 10)
        Return b
    End Function

    Private Shared Function Strong(text As String) As Label
        Return New Label With {
            .Text = If(text = "", "—", text),
            .Font = New Font("Segoe UI", 11, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .BackColor = Color.Transparent
        }
    End Function

    Private Shared Function Normal(text As String) As Label
        Return New Label With {
            .Text = If(text = "", "—", text),
            .Font = New Font("Segoe UI", 10.5F),
            .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent
        }
    End Function

    Private Shared Function Faint(text As String) As Label
        Return New Label With {
            .Text = If(text = "", "—", text),
            .Font = New Font("Segoe UI", 9),
            .ForeColor = FaintText, .AutoSize = True, .BackColor = Color.Transparent
        }
    End Function

    ' ── Tab 0 row ────────────────────────────────────────────────────────────
    Private Shared Function BuildPoRow(r As PoRow, height As Integer) As Panel
        Dim badgeClr = PoBadge(r.Status)
        Dim accent As Color = badgeClr.fore
        Dim p As BufferedPanel = NewRowShell(height, accent)

        Dim lblNo = Strong(r.PoNo)
        Dim lblBy = Faint("by " & If(r.CreatedBy = "", "—", r.CreatedBy))
        Dim lblSupplier = Normal(r.Supplier)
        Dim lblOrdered = Normal(DateOrDash(r.OrderDate, "MMM d, yyyy"))
        Dim lblExpected = Faint("exp. " & DateOrDash(r.ExpectedDate, "MMM d, yyyy"))
        Dim badge = MakeBadge(r.Status, badgeClr)
        Dim lblTotal As New Label With {
            .Text = Money(r.TotalCost),
            .Font = New Font("Segoe UI", 12, FontStyle.Bold),
            .ForeColor = MintPrimary, .AutoSize = True, .BackColor = Color.Transparent
        }

        p.Controls.AddRange({lblNo, lblBy, lblSupplier, lblOrdered, lblExpected, badge, lblTotal})

        AddHandler p.Resize,
            Sub()
                Dim w = p.Width
                Dim t1 As Integer = (height \ 2) - 17
                Dim t2 As Integer = (height \ 2) + 2
                lblNo.Left = 24 : lblNo.Top = t1
                lblBy.Left = 24 : lblBy.Top = t2
                lblSupplier.Left = CInt(w * 0.22) : lblSupplier.Top = (height - lblSupplier.Height) \ 2
                lblOrdered.Left = CInt(w * 0.42) : lblOrdered.Top = t1
                lblExpected.Left = CInt(w * 0.42) : lblExpected.Top = t2
                badge.Left = CInt(w * 0.62) : badge.Top = (height - badge.Height) \ 2
                lblTotal.Left = CInt(w * 0.78) : lblTotal.Top = (height - lblTotal.Height) \ 2
            End Sub

        PropagateHover(p)
        Return p
    End Function

    ' ── Tab 1 row ────────────────────────────────────────────────────────────
    Private Shared Function BuildPrRow(r As PrRow, height As Integer) As Panel
        Dim badgeClr = PrBadge(r.Status)
        Dim p As BufferedPanel = NewRowShell(height, badgeClr.fore)

        ' A basket is titled by its size, not by whichever product happens to be
        ' first — "3 products" is what the approver is actually deciding on.
        Dim titleText As String =
            RequestReviewModal.RequestNo(r.RequestId) & "   " &
            If(r.IsMultiLine,
               r.ItemCount & " products",
               r.ProductName)
        Dim lblProduct = Strong(titleText)

        ' The line that carried the note now carries the basket contents when
        ' there is a basket: what is being approved outranks the note about it.
        Dim subText As String
        If r.IsMultiLine AndAlso r.ItemSummary <> "" Then
            subText = r.ItemSummary
        ElseIf r.Note = "" Then
            subText = "no note"
        Else
            subText = r.Note
        End If

        Dim lblNote As New Label With {
            .Text = subText,
            .Font = New Font("Segoe UI", 9),
            .ForeColor = If(r.IsMultiLine, SubText, FaintText),
            .AutoSize = False,
            .AutoEllipsis = True,
            .Height = 16,
            .BackColor = Color.Transparent
        }

        ' Total units across every line, so the number beside a basket is the
        ' whole basket rather than just its first product.
        Dim lblQty As New Label With {
            .Text = If(r.IsMultiLine, r.TotalUnits, r.QtyRequested).ToString("N0"),
            .Font = New Font("Segoe UI", 13, FontStyle.Bold),
            .ForeColor = MintPrimary, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblBy = Normal(r.RequestedBy)
        Dim badge = MakeBadge(r.Status, badgeClr)
        Dim lblWhen = Normal(DateOrDash(r.CreatedAt, "MMM d, yyyy  h:mm tt"))

        ' Waiting requests carry a Review button; any row opens the request.
        Dim btnReview As Label = Nothing
        If r.Status = "Pending" Then
            btnReview = New Label With {
                .Text = "Review  " & ChrW(8250), .Font = New Font("Segoe UI Semibold", 9.5F),
                .ForeColor = Color.White, .BackColor = MintPrimary,
                .AutoSize = True, .Padding = New Padding(14, 6, 14, 6), .Cursor = Cursors.Hand
            }
            RoundLabel(btnReview, 12)
        End If

        p.Controls.AddRange({lblProduct, lblNote, lblQty, lblBy, badge, lblWhen})
        If btnReview IsNot Nothing Then p.Controls.Add(btnReview)
        p.Cursor = Cursors.Hand

        AddHandler p.Resize,
            Sub()
                Dim w = p.Width
                Dim t1 As Integer = (height \ 2) - 17
                Dim t2 As Integer = (height \ 2) + 2
                lblProduct.Left = 24 : lblProduct.Top = t1
                lblNote.Left = 24 : lblNote.Top = t2
                lblNote.Width = Math.Max(60, CInt(w * 0.32) - 44)
                lblQty.Left = CInt(w * 0.32) : lblQty.Top = (height - lblQty.Height) \ 2
                lblBy.Left = CInt(w * 0.4) : lblBy.Top = (height - lblBy.Height) \ 2
                badge.Left = CInt(w * 0.56) : badge.Top = (height - badge.Height) \ 2
                lblWhen.Left = CInt(w * 0.7) : lblWhen.Top = (height - lblWhen.Height) \ 2
                If btnReview IsNot Nothing Then
                    btnReview.Left = w - btnReview.Width - 28
                    btnReview.Top = (height - btnReview.Height) \ 2
                End If
            End Sub

        Dim capId As Integer = r.RequestId
        Dim openReview As Action =
            Sub()
                If RequestReviewModal.Show(capId) IsNot Nothing Then ReQuery(resetPage:=False)
            End Sub
        AddHandler p.Click, Sub() openReview()
        For Each ctrl As Control In p.Controls
            AddHandler ctrl.Click, Sub() openReview()
            ctrl.Cursor = Cursors.Hand
        Next

        PropagateHover(p)
        Return p
    End Function

    ' ── Tab 2 row ────────────────────────────────────────────────────────────
    Private Shared Function BuildQuoteRow(r As QuoteRow, height As Integer) As Panel
        Dim badgeClr = QuoteBadge(r.Status)
        Dim p As BufferedPanel = NewRowShell(height, badgeClr.fore)

        Dim lblNo = Strong(r.QuotationNo)
        Dim lblBy = Faint("by " & If(r.CreatedBy = "", "—", r.CreatedBy))
        Dim lblSupplier = Normal(r.Supplier)
        Dim lblCreated = Normal(DateOrDash(r.CreatedAt, "MMM d, yyyy"))
        Dim expired As Boolean = (r.ValidUntil <> DateTime.MinValue AndAlso
                                  r.ValidUntil.Date < DateTime.Today)
        Dim lblValid As New Label With {
            .Text = "valid to " & DateOrDash(r.ValidUntil, "MMM d, yyyy"),
            .Font = New Font("Segoe UI", 9),
            .ForeColor = If(expired, AccentRed, FaintText),
            .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim badge = MakeBadge(r.Status, badgeClr)
        Dim lblTotal As New Label With {
            .Text = Money(r.TotalAmount),
            .Font = New Font("Segoe UI", 12, FontStyle.Bold),
            .ForeColor = MintPrimary, .AutoSize = True, .BackColor = Color.Transparent
        }

        p.Controls.AddRange({lblNo, lblBy, lblSupplier, lblCreated, lblValid, badge, lblTotal})

        AddHandler p.Resize,
            Sub()
                Dim w = p.Width
                Dim t1 As Integer = (height \ 2) - 17
                Dim t2 As Integer = (height \ 2) + 2
                lblNo.Left = 24 : lblNo.Top = t1
                lblBy.Left = 24 : lblBy.Top = t2
                lblSupplier.Left = CInt(w * 0.22) : lblSupplier.Top = (height - lblSupplier.Height) \ 2
                lblCreated.Left = CInt(w * 0.42) : lblCreated.Top = t1
                lblValid.Left = CInt(w * 0.42) : lblValid.Top = t2
                badge.Left = CInt(w * 0.62) : badge.Top = (height - badge.Height) \ 2
                lblTotal.Left = CInt(w * 0.78) : lblTotal.Top = (height - lblTotal.Height) \ 2
            End Sub

        PropagateHover(p)
        Return p
    End Function

    ' ── Status badge colours ─────────────────────────────────────────────────
    Private Shared Function PoBadge(status As String) As (fore As Color, back As Color)
        Select Case status
            Case "Received" : Return BadgeGood
            Case "Draft" : Return BadgeNeutral
            Case "Ordered" : Return BadgeInfo
            Case "Partially Received" : Return BadgeWarn
            Case "Cancelled" : Return BadgeBad
            Case Else : Return BadgeNeutral
        End Select
    End Function

    Private Shared Function PrBadge(status As String) As (fore As Color, back As Color)
        Select Case status
            Case "Approved", "Converted" : Return BadgeGood
            Case "Pending" : Return BadgeWarn
            Case "Quoted" : Return BadgeInfo
            Case "Rejected" : Return BadgeBad
            Case Else : Return BadgeNeutral
        End Select
    End Function

    Private Shared Function QuoteBadge(status As String) As (fore As Color, back As Color)
        Select Case status
            Case "Accepted" : Return BadgeGood
            Case "Sent" : Return BadgeInfo
            Case "Draft" : Return BadgeNeutral
            Case "Rejected" : Return BadgeBad
            Case "Expired" : Return BadgeWarn
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
            _lblPageInfo.Text = "Nothing to show"
        Else
            Dim firstRow As Integer = _currentPage * _pageSize + 1
            Dim lastRow As Integer = Math.Min(_totalRows, firstRow + CurrentRowCount() - 1)
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
