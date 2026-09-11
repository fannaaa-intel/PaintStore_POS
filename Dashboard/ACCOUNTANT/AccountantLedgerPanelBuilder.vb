Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports System.Linq
Imports MySql.Data.MySqlClient

Public Class AccountantLedgerPanelBuilder

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
    Private Shared ReadOnly Peso As String = ChrW(8369)

    Private Shared ReadOnly BadgePOS As (fore As Color, back As Color) =
        (Color.FromArgb(0, 120, 92), Color.FromArgb(220, 248, 240))
    Private Shared ReadOnly BadgeCash As (fore As Color, back As Color) =
        (Color.FromArgb(140, 80, 0), Color.FromArgb(255, 240, 210))
    Private Shared ReadOnly BadgeProcurement As (fore As Color, back As Color) =
        (Color.FromArgb(30, 90, 180), Color.FromArgb(225, 235, 255))

    Private Shared ReadOnly FilterKeys() As String = {"today", "week", "month", "all"}
    Private Shared ReadOnly FilterLabels() As String = {"Today", "This Week", "This Month", "All Time"}

    Private Const TAB_JOURNAL As String = "journal"
    Private Const TAB_LEDGER As String = "ledger"

    Private Shared ReadOnly AccountOrder() As String = {
        "Cash", "Sales Revenue", "Output VAT Payable", "Sales Returns", "Inventory",
        "Input VAT", "Accounts Payable", "COGS", "Expense", "Other Income", "Bank"
    }

    Private Class LedgerLine
        Public Account As String
        Public Debit As Decimal = 0
        Public Credit As Decimal = 0
    End Class

    Private Class LedgerTxn
        Public TxnDate As DateTime
        Public Ref As String
        Public SourceKey As String
        Public SourceLabel As String
        Public Lines As New List(Of LedgerLine)
    End Class

    Private Class FlatLine
        Public TxnDate As DateTime
        Public Ref As String
        Public SourceKey As String
        Public SourceLabel As String
        Public Debit As Decimal
        Public Credit As Decimal
    End Class

    Private Shared _container As Panel
    Private Shared _contentArea As Panel
    Private Shared _activeMainTab As String = TAB_JOURNAL
    Private Shared _dateFilter As String = "all"
    Private Shared _activeFilterIdx As Integer = 3
    Private Shared _sourceFilter As String = "all"
    Private Shared _kpiPanels(3) As Panel
    Private Shared _mainTabButtons(1) As Button

    Private Shared _allTxns As New List(Of LedgerTxn)
    Private Shared _visibleTxns As New List(Of LedgerTxn)

    Private Shared _scrollHost As Panel
    Private Shared _reflowAttached As Boolean = False

    Private Shared _visibleHandler As EventHandler = Nothing

    Private Const PAGE_SIZE As Integer = 50
    Private Shared _currentPage As Integer = 0
    Private Shared _lblPageInfo As Label
    Private Shared _btnPrev As Button
    Private Shared _btnNext As Button

    Public Shared Sub LoadLedger(container As Panel,
                                 Optional mainTab As String = TAB_JOURNAL,
                                 Optional dateFilter As String = "all")
        _container = container
        _activeMainTab = If(mainTab = TAB_LEDGER, TAB_LEDGER, TAB_JOURNAL)
        _dateFilter = dateFilter
        Dim idx As Integer = Array.IndexOf(FilterKeys, dateFilter)
        _activeFilterIdx = If(idx >= 0, idx, 3)
        _sourceFilter = "all"
        _reflowAttached = False
        _currentPage = 0

        LoadData()
        Render()
    End Sub

    Private Shared Sub LoadData()
        _allTxns.Clear()

        Dim avgCost As New Dictionary(Of Integer, Decimal)
        Dim saleItemsBySale As New Dictionary(Of Integer, List(Of (ProductId As Integer, Qty As Integer)))

        Try
            Using conn = Db.GetConnection()
                conn.Open()

                LoadAvgCosts(conn, avgCost)
                LoadSaleItemMap(conn, saleItemsBySale)
                LoadSalesTxns(conn, avgCost, saleItemsBySale)
                LoadReturnTxns(conn)
                LoadPurchaseOrderTxns(conn)
                LoadCashMovementTxns(conn)
            End Using
        Catch ex As Exception
            Debug.WriteLine("Ledger LoadData connection error: " & ex.Message)
        End Try

        _allTxns.Sort(Function(a, b) b.TxnDate.CompareTo(a.TxnDate))
        ApplyFilters()
    End Sub

    Private Shared Sub LoadAvgCosts(conn As MySqlConnection, avgCost As Dictionary(Of Integer, Decimal))
        Try
            Using cmd As New MySqlCommand(
                "SELECT product_id, SUM(unit_cost * qty) AS total_cost, SUM(qty) AS total_qty " &
                "FROM po_items GROUP BY product_id", conn)
                Using rdr = cmd.ExecuteReader()
                    While rdr.Read()
                        Dim pid As Integer = Convert.ToInt32(rdr("product_id"))
                        Dim totalQty As Decimal = Convert.ToDecimal(rdr("total_qty"))
                        If totalQty > 0 Then
                            avgCost(pid) = Convert.ToDecimal(rdr("total_cost")) / totalQty
                        End If
                    End While
                End Using
            End Using
        Catch ex As Exception
            Debug.WriteLine("Ledger LoadAvgCosts error: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub LoadSaleItemMap(conn As MySqlConnection,
                                        map As Dictionary(Of Integer, List(Of (ProductId As Integer, Qty As Integer))))
        Try
            Using cmd As New MySqlCommand(
                "SELECT sale_id, product_id, qty FROM sale_items WHERE product_id IS NOT NULL", conn)
                Using rdr = cmd.ExecuteReader()
                    While rdr.Read()
                        Dim sid As Integer = Convert.ToInt32(rdr("sale_id"))
                        Dim pid As Integer = Convert.ToInt32(rdr("product_id"))
                        Dim qty As Integer = Convert.ToInt32(rdr("qty"))
                        If Not map.ContainsKey(sid) Then map(sid) = New List(Of (ProductId As Integer, Qty As Integer))
                        map(sid).Add((pid, qty))
                    End While
                End Using
            End Using
        Catch ex As Exception
            Debug.WriteLine("Ledger LoadSaleItemMap error: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub LoadSalesTxns(conn As MySqlConnection,
                                      avgCost As Dictionary(Of Integer, Decimal),
                                      saleItemsBySale As Dictionary(Of Integer, List(Of (ProductId As Integer, Qty As Integer))))
        Try
            Using cmd As New MySqlCommand(
                "SELECT s.sale_id, s.sale_no, s.sale_datetime, s.total, s.vat_amount " &
                "FROM sales s WHERE s.status IN ('Completed','Partially Refunded') " &
                BuildDateWhereCol("s.sale_datetime").Replace("WHERE ", "AND "), conn)
                Using rdr = cmd.ExecuteReader()
                    While rdr.Read()
                        If IsDBNull(rdr("sale_datetime")) Then Continue While

                        Dim saleId As Integer = Convert.ToInt32(rdr("sale_id"))
                        Dim total As Decimal = Convert.ToDecimal(rdr("total"))
                        Dim vat As Decimal = Convert.ToDecimal(rdr("vat_amount"))
                        ' Back VAT out of the VAT-inclusive total when the stored
                        ' amount is missing/zero (older seeded sales).
                        If vat <= 0D AndAlso total > 0D Then
                            vat = StoreSettings.ExtractInclusiveVat(total)
                        End If

                        Dim txn As New LedgerTxn With {
                            .TxnDate = Convert.ToDateTime(rdr("sale_datetime")),
                            .Ref = rdr("sale_no").ToString(),
                            .SourceKey = "POS",
                            .SourceLabel = "Sale"
                        }

                        Dim revenue As Decimal = total - vat

                        Dim cogs As Decimal = 0
                        If saleItemsBySale.ContainsKey(saleId) Then
                            For Each item In saleItemsBySale(saleId)
                                If avgCost.ContainsKey(item.ProductId) Then
                                    cogs += avgCost(item.ProductId) * item.Qty
                                End If
                            Next
                        End If

                        txn.Lines.Add(New LedgerLine With {.Account = "Cash", .Debit = total})
                        txn.Lines.Add(New LedgerLine With {.Account = "Sales Revenue", .Credit = revenue})
                        txn.Lines.Add(New LedgerLine With {.Account = "Output VAT Payable", .Credit = vat})
                        txn.Lines.Add(New LedgerLine With {.Account = "COGS", .Debit = cogs})
                        txn.Lines.Add(New LedgerLine With {.Account = "Inventory", .Credit = cogs})

                        _allTxns.Add(txn)
                    End While
                End Using
            End Using
        Catch ex As Exception
            Debug.WriteLine("Ledger LoadSalesTxns error: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub LoadReturnTxns(conn As MySqlConnection)
        Try
            Using cmd As New MySqlCommand(
                "SELECT rt.return_no, rt.return_datetime, rt.total_refund " &
                "FROM returns rt " & BuildDateWhereCol("rt.return_datetime"), conn)
                Using rdr = cmd.ExecuteReader()
                    While rdr.Read()
                        If IsDBNull(rdr("return_datetime")) Then Continue While

                        Dim refund As Decimal = Convert.ToDecimal(rdr("total_refund"))
                        Dim txn As New LedgerTxn With {
                            .TxnDate = Convert.ToDateTime(rdr("return_datetime")),
                            .Ref = rdr("return_no").ToString(),
                            .SourceKey = "POS",
                            .SourceLabel = "Return"
                        }
                        txn.Lines.Add(New LedgerLine With {.Account = "Sales Returns", .Debit = refund})
                        txn.Lines.Add(New LedgerLine With {.Account = "Cash", .Credit = refund})
                        _allTxns.Add(txn)
                    End While
                End Using
            End Using
        Catch ex As Exception
            Debug.WriteLine("Ledger LoadReturnTxns error: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub LoadPurchaseOrderTxns(conn As MySqlConnection)
        Try
            Using cmd As New MySqlCommand(
                "SELECT po.po_no, po.received_at, po.total_cost " &
                "FROM purchase_orders po " &
                "WHERE po.status IN ('Received','Partially Received') AND po.received_at IS NOT NULL " &
                BuildDateWhereCol("po.received_at").Replace("WHERE ", "AND "), conn)
                Using rdr = cmd.ExecuteReader()
                    While rdr.Read()
                        If IsDBNull(rdr("received_at")) Then Continue While

                        Dim cost As Decimal = Convert.ToDecimal(rdr("total_cost"))
                        ' Supplier invoice total is VAT-inclusive: split into net
                        ' inventory cost + recoverable Input VAT. The rate comes
                        ' from store settings -- hardcoding 12/112 mis-stated the
                        ' ledger for any store on a different rate.
                        Dim inputVat As Decimal = StoreSettings.ExtractInclusiveVat(cost)
                        Dim netCost As Decimal = cost - inputVat
                        Dim txn As New LedgerTxn With {
                            .TxnDate = Convert.ToDateTime(rdr("received_at")),
                            .Ref = rdr("po_no").ToString(),
                            .SourceKey = "Procurement",
                            .SourceLabel = "Purchase Order"
                        }
                        txn.Lines.Add(New LedgerLine With {.Account = "Inventory", .Debit = netCost})
                        txn.Lines.Add(New LedgerLine With {.Account = "Input VAT", .Debit = inputVat})
                        txn.Lines.Add(New LedgerLine With {.Account = "Accounts Payable", .Credit = cost})
                        _allTxns.Add(txn)
                    End While
                End Using
            End Using
        Catch ex As Exception
            Debug.WriteLine("Ledger LoadPurchaseOrderTxns error: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub LoadCashMovementTxns(conn As MySqlConnection)
        Try
            Using cmd As New MySqlCommand(
                "SELECT cm.movement_id, cm.movement_type, cm.created_at, cm.amount " &
                "FROM cash_movements cm " & BuildDateWhereCol("cm.created_at"), conn)
                Using rdr = cmd.ExecuteReader()
                    While rdr.Read()
                        If IsDBNull(rdr("created_at")) Then Continue While

                        Dim amt As Decimal = Convert.ToDecimal(rdr("amount"))
                        Dim mtype As String = rdr("movement_type").ToString()
                        Dim txn As New LedgerTxn With {
                            .TxnDate = Convert.ToDateTime(rdr("created_at")),
                            .Ref = "CM-" & rdr("movement_id").ToString(),
                            .SourceKey = "Cash",
                            .SourceLabel = "Cash " & ChrW(183) & " " & mtype
                        }

                        Select Case mtype
                            Case "PayIn"
                                txn.Lines.Add(New LedgerLine With {.Account = "Cash", .Debit = amt})
                                txn.Lines.Add(New LedgerLine With {.Account = "Other Income", .Credit = amt})
                            Case "PayOut"
                                txn.Lines.Add(New LedgerLine With {.Account = "Expense", .Debit = amt})
                                txn.Lines.Add(New LedgerLine With {.Account = "Cash", .Credit = amt})
                            Case "Drop"
                                txn.Lines.Add(New LedgerLine With {.Account = "Bank", .Debit = amt})
                                txn.Lines.Add(New LedgerLine With {.Account = "Cash", .Credit = amt})
                            Case "Refund"
                                txn.Lines.Add(New LedgerLine With {.Account = "Sales Returns", .Debit = amt})
                                txn.Lines.Add(New LedgerLine With {.Account = "Cash", .Credit = amt})
                            Case Else
                                Continue While
                        End Select

                        _allTxns.Add(txn)
                    End While
                End Using
            End Using
        Catch ex As Exception
            Debug.WriteLine("Ledger LoadCashMovementTxns error: " & ex.Message)
        End Try
    End Sub

    ' =====================================================================
    '  TRIAL BALANCE (public) — account-level debit/credit totals for an
    '  explicit date range, posted with the SAME rules as the journal above,
    '  so Reports' Trial Balance always reconciles with this ledger.
    ' =====================================================================
    Public Shared Function GetTrialBalance(fromDate As DateTime, toDate As DateTime) _
            As List(Of (Account As String, Debit As Decimal, Credit As Decimal))

        Dim drTotals As New Dictionary(Of String, Decimal)
        Dim crTotals As New Dictionary(Of String, Decimal)
        For Each a In AccountOrder
            drTotals(a) = 0D : crTotals(a) = 0D
        Next

        Dim postDr = Sub(acct As String, amt As Decimal)
                         If Not drTotals.ContainsKey(acct) Then drTotals(acct) = 0D : crTotals(acct) = 0D
                         drTotals(acct) += amt
                     End Sub
        Dim postCr = Sub(acct As String, amt As Decimal)
                         If Not crTotals.ContainsKey(acct) Then drTotals(acct) = 0D : crTotals(acct) = 0D
                         crTotals(acct) += amt
                     End Sub

        Dim fromVal As DateTime = fromDate.Date
        Dim toVal As DateTime = toDate.Date.AddDays(1).AddSeconds(-1)

        Try
            Using conn = Db.GetConnection()
                conn.Open()

                ' ── SALES: Dr Cash / Cr Sales Revenue (net) / Cr Output VAT ──
                ' 'Partially Refunded' counts as a sale: the original revenue
                ' stands and the refund is posted separately under Sales Returns
                ' below. Filtering to 'Completed' alone dropped the revenue while
                ' still deducting the refund, so the books did not balance.
                Using cmd As New MySqlCommand(
                    "SELECT COALESCE(SUM(total),0) AS t, " &
                    "COALESCE(SUM(CASE WHEN vat_amount <= 0 THEN ROUND(total*12/112,2) ELSE vat_amount END),0) AS v " &
                    "FROM sales WHERE status IN ('Completed','Partially Refunded') AND sale_datetime BETWEEN @from AND @to", conn)
                    cmd.Parameters.AddWithValue("@from", fromVal)
                    cmd.Parameters.AddWithValue("@to", toVal)
                    Using rdr = cmd.ExecuteReader()
                        If rdr.Read() Then
                            Dim t As Decimal = Convert.ToDecimal(rdr("t"))
                            Dim v As Decimal = Convert.ToDecimal(rdr("v"))
                            postDr("Cash", t)
                            postCr("Sales Revenue", t - v)
                            postCr("Output VAT Payable", v)
                        End If
                    End Using
                End Using

                ' ── COGS: Dr COGS / Cr Inventory (cost of items SOLD) ──
                Dim periodCogs As Decimal = 0D
                Using cmd As New MySqlCommand(
                    "SELECT COALESCE(SUM(si.qty * ac.avg_cost), 0) " &
                    "FROM sale_items si " &
                    "JOIN sales s ON s.sale_id = si.sale_id " &
                    "JOIN (SELECT product_id, SUM(unit_cost * qty) / NULLIF(SUM(qty),0) AS avg_cost " &
                    "      FROM po_items GROUP BY product_id) ac ON ac.product_id = si.product_id " &
                    "WHERE si.product_id IS NOT NULL AND s.status IN ('Completed','Partially Refunded') " &
                    "AND s.sale_datetime BETWEEN @from AND @to", conn)
                    cmd.Parameters.AddWithValue("@from", fromVal)
                    cmd.Parameters.AddWithValue("@to", toVal)
                    periodCogs = Convert.ToDecimal(cmd.ExecuteScalar())
                End Using
                postDr("COGS", periodCogs)
                postCr("Inventory", periodCogs)

                ' ── RETURNS: Dr Sales Returns / Cr Cash ──
                Using cmd As New MySqlCommand(
                    "SELECT COALESCE(SUM(total_refund),0) FROM returns " &
                    "WHERE return_datetime BETWEEN @from AND @to", conn)
                    cmd.Parameters.AddWithValue("@from", fromVal)
                    cmd.Parameters.AddWithValue("@to", toVal)
                    Dim refund As Decimal = Convert.ToDecimal(cmd.ExecuteScalar())
                    postDr("Sales Returns", refund)
                    postCr("Cash", refund)
                End Using

                ' ── PURCHASE ORDERS: Dr Inventory (net) + Dr Input VAT / Cr AP ──
                Using cmd As New MySqlCommand(
                    "SELECT total_cost FROM purchase_orders " &
                    "WHERE status IN ('Received','Partially Received') AND received_at IS NOT NULL " &
                    "AND received_at BETWEEN @from AND @to", conn)
                    cmd.Parameters.AddWithValue("@from", fromVal)
                    cmd.Parameters.AddWithValue("@to", toVal)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            Dim cost As Decimal = Convert.ToDecimal(rdr("total_cost"))
                            Dim inputVat As Decimal = StoreSettings.ExtractInclusiveVat(cost)
                            postDr("Inventory", cost - inputVat)
                            postDr("Input VAT", inputVat)
                            postCr("Accounts Payable", cost)
                        End While
                    End Using
                End Using

                ' ── CASH MOVEMENTS ──
                Using cmd As New MySqlCommand(
                    "SELECT movement_type, COALESCE(SUM(amount),0) AS amt FROM cash_movements " &
                    "WHERE created_at BETWEEN @from AND @to GROUP BY movement_type", conn)
                    cmd.Parameters.AddWithValue("@from", fromVal)
                    cmd.Parameters.AddWithValue("@to", toVal)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            Dim amt As Decimal = Convert.ToDecimal(rdr("amt"))
                            Select Case rdr("movement_type").ToString()
                                Case "PayIn" : postDr("Cash", amt) : postCr("Other Income", amt)
                                Case "PayOut" : postDr("Expense", amt) : postCr("Cash", amt)
                                Case "Drop" : postDr("Bank", amt) : postCr("Cash", amt)
                                Case "Refund" : postDr("Sales Returns", amt) : postCr("Cash", amt)
                            End Select
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            Debug.WriteLine("GetTrialBalance error: " & ex.Message)
        End Try

        ' Net each account into its normal-balance column.
        Dim ordered As New List(Of String)(AccountOrder)
        For Each acct In drTotals.Keys
            If Not ordered.Contains(acct) Then ordered.Add(acct)
        Next

        Dim result As New List(Of (Account As String, Debit As Decimal, Credit As Decimal))
        For Each acct In ordered
            Dim net As Decimal = drTotals(acct) - crTotals(acct)
            If net = 0D Then Continue For
            If net > 0D Then
                result.Add((acct, net, 0D))
            Else
                result.Add((acct, 0D, -net))
            End If
        Next
        Return result
    End Function

    Private Shared Function BuildDateWhereCol(col As String) As String
        Select Case _dateFilter
            Case "today" : Return $"WHERE DATE({col}) = CURDATE() "
            Case "week" : Return $"WHERE {col} >= CURDATE() - INTERVAL 7 DAY "
            Case "month" : Return $"WHERE {col} >= CURDATE() - INTERVAL 30 DAY "
            Case Else : Return ""
        End Select
    End Function

    Private Shared Sub ApplyFilters()
        _visibleTxns.Clear()
        For Each t In _allTxns
            If _sourceFilter <> "all" AndAlso t.SourceKey <> _sourceFilter Then Continue For
            _visibleTxns.Add(t)
        Next
    End Sub

    Private Shared Sub Render()
        _container.SuspendLayout()
        _container.Controls.Clear()
        _container.BackColor = MintBg

        _contentArea = New Panel With {
    .Dock = DockStyle.Fill,
    .BackColor = MintBg,
    .Padding = New Padding(24, 16, 24, 16)
}

        ' Date-filter pills removed entirely per request. TopRegion is back
        ' to the simple 3-zone layout: TitleBar -> TabRow -> KpiBar.
        Dim titleBar As Panel = BuildTitleBar()
        titleBar.Dock = DockStyle.Top
        titleBar.Height = 72

        Dim tabRow As Panel = BuildMainTabRow()
        tabRow.Dock = DockStyle.Top
        tabRow.Height = 56

        Dim kpiBar As Panel = BuildKpiBar()
        kpiBar.Dock = DockStyle.Top
        kpiBar.Height = 165

        Dim topRegion As New Panel With {
            .BackColor = MintBg,
            .AutoSize = False,
            .Dock = DockStyle.Top,
            .Height = 72 + 56 + 165
        }

        ' Add order: later-added docks closer to the parent edge (topmost),
        ' so kpiBar first, titleBar last gives Title -> Tabs -> KPI
        ' top-to-bottom.
        topRegion.Controls.Add(kpiBar)
        topRegion.Controls.Add(tabRow)
        topRegion.Controls.Add(titleBar)

        _container.Controls.Add(_contentArea)
        _container.Controls.Add(topRegion)

        _container.ResumeLayout()

        If _visibleHandler IsNot Nothing Then
            RemoveHandler _container.VisibleChanged, _visibleHandler
            _visibleHandler = Nothing
        End If

        If _container.Visible Then
            RenderTabContent()
        Else
            _visibleHandler = Sub(s, e)
                                  If _container.Visible Then
                                      RenderTabContent()
                                      RemoveHandler _container.VisibleChanged, _visibleHandler
                                      _visibleHandler = Nothing
                                  End If
                              End Sub
            AddHandler _container.VisibleChanged, _visibleHandler
            RenderTabContent()
        End If
    End Sub

    Private Shared Function BuildTitleBar() As Panel
        Const SIDE_PAD As Integer = 24

        Dim titleBar As New Panel With {
            .Height = 72,
            .Dock = DockStyle.Top,
            .BackColor = Color.Transparent
        }

        Dim lblTitle As New Label With {
            .Text = "Ledger",
            .Font = New Font("Segoe UI", 28, FontStyle.Bold),
            .ForeColor = MintDark,
            .AutoSize = True,
            .Location = New Point(SIDE_PAD, 16)
        }

        Dim btnRefresh As New Button With {
            .Text = ChrW(8635) & "  Refresh",
            .FlatStyle = FlatStyle.Flat,
            .Cursor = Cursors.Hand,
            .Size = New Size(100, 34),
            .Font = New Font("Segoe UI", 9.5F),
            .BackColor = Color.FromArgb(220, 235, 232),
            .ForeColor = SubText,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        btnRefresh.FlatAppearance.BorderSize = 0

        AddHandler btnRefresh.Paint,
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

        AddHandler btnRefresh.MouseEnter,
            Sub()
                btnRefresh.BackColor = Color.FromArgb(210, 240, 235)
                btnRefresh.ForeColor = MintDark
                btnRefresh.Invalidate()
            End Sub
        AddHandler btnRefresh.MouseLeave,
            Sub()
                btnRefresh.BackColor = Color.FromArgb(220, 235, 232)
                btnRefresh.ForeColor = SubText
                btnRefresh.Invalidate()
            End Sub
        AddHandler btnRefresh.Click,
            Sub() LoadLedger(_container, _activeMainTab, FilterKeys(_activeFilterIdx))

        AddHandler titleBar.Resize,
            Sub()
                btnRefresh.Left = titleBar.Width - btnRefresh.Width - SIDE_PAD
                btnRefresh.Top = (titleBar.Height - btnRefresh.Height) \ 2
            End Sub

        titleBar.Controls.Add(lblTitle)
        titleBar.Controls.Add(btnRefresh)

        Return titleBar
    End Function

    Private Shared Function BuildMainTabRow() As Panel
        Const SIDE_PAD As Integer = 24

        Dim bar As New Panel With {
            .BackColor = Color.Transparent,
            .Height = 56
        }

        Dim tabs() As (key As String, label As String) = {
            (TAB_JOURNAL, "Journal Entries"),
            (TAB_LEDGER, "General Ledger")
        }

        Dim host As New FlowLayoutPanel With {
            .FlowDirection = FlowDirection.LeftToRight,
            .AutoSize = True,
            .BackColor = Color.Transparent,
            .WrapContents = False,
            .Location = New Point(SIDE_PAD, 11)
        }

        For tabIdx As Integer = 0 To tabs.Length - 1
            Dim capKey As String = tabs(tabIdx).key
            Dim capLabel As String = tabs(tabIdx).label
            Dim isActive As Boolean = (_activeMainTab = capKey)

            Dim b As New Button With {
                .Text = capLabel,
                .FlatStyle = FlatStyle.Flat,
                .Cursor = Cursors.Hand,
                .Size = New Size(148, 36),
                .Margin = New Padding(0, 0, 8, 0),
                .Tag = capKey
            }
            b.FlatAppearance.BorderSize = 0

            Dim applyTabStyle As Action(Of Boolean) =
                Sub(active As Boolean)
                    If active Then
                        b.BackColor = MintPrimary
                        b.ForeColor = Color.White
                        b.Font = New Font("Segoe UI Semibold", 9.5F, FontStyle.Bold)
                    Else
                        b.BackColor = Color.FromArgb(220, 235, 232)
                        b.ForeColor = SubText
                        b.Font = New Font("Segoe UI", 9.5F)
                    End If
                    b.Invalidate()
                End Sub
            applyTabStyle(isActive)

            AddHandler b.Paint,
                Sub(s, e)
                    Dim bb = CType(s, Button)
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                    Dim rect = bb.ClientRectangle : rect.Inflate(-1, -1)
                    Using br As New SolidBrush(bb.BackColor)
                        e.Graphics.FillRoundedRectangle(br, rect, 8)
                    End Using
                    TextRenderer.DrawText(e.Graphics, bb.Text, bb.Font,
                        bb.ClientRectangle, bb.ForeColor,
                        TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
                End Sub

            AddHandler b.MouseEnter,
                Sub()
                    If capKey <> _activeMainTab Then
                        b.BackColor = Color.FromArgb(210, 240, 235)
                        b.ForeColor = MintDark
                        b.Invalidate()
                    End If
                End Sub
            AddHandler b.MouseLeave,
                Sub()
                    If capKey <> _activeMainTab Then
                        b.BackColor = Color.FromArgb(220, 235, 232)
                        b.ForeColor = SubText
                        b.Invalidate()
                    End If
                End Sub

            AddHandler b.Click,
                Sub()
                    If _activeMainTab = capKey Then Return
                    _activeMainTab = capKey
                    For Each btn2 As Button In _mainTabButtons
                        If btn2 Is Nothing Then Continue For
                        Dim nowActive As Boolean = (CStr(btn2.Tag) = _activeMainTab)
                        If nowActive Then
                            btn2.BackColor = MintPrimary
                            btn2.ForeColor = Color.White
                            btn2.Font = New Font("Segoe UI Semibold", 9.5F, FontStyle.Bold)
                        Else
                            btn2.BackColor = Color.FromArgb(220, 235, 232)
                            btn2.ForeColor = SubText
                            btn2.Font = New Font("Segoe UI", 9.5F)
                        End If
                        btn2.Invalidate()
                    Next
                    _currentPage = 0
                    RenderTabContent()
                End Sub

            _mainTabButtons(tabIdx) = b
            host.Controls.Add(b)
        Next

        bar.Controls.Add(host)
        Return bar
    End Function

    Private Shared Function BuildKpiBar() As Panel
        Const SIDE_PAD As Integer = 24

        Dim totalCount As Integer = _visibleTxns.Count
        Dim posCount As Integer = _visibleTxns.Where(Function(t) t.SourceKey = "POS").Count()
        Dim procCount As Integer = _visibleTxns.Where(Function(t) t.SourceKey = "Procurement").Count()
        Dim cashCount As Integer = _visibleTxns.Where(Function(t) t.SourceKey = "Cash").Count()

        Dim bar As New Panel With {
            .Height = 165,
            .BackColor = Color.Transparent,
            .Padding = New Padding(SIDE_PAD, 16, SIDE_PAD, 0)
        }

        Dim kpiRow As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .ColumnCount = 4,
            .RowCount = 1,
            .BackColor = Color.Transparent,
            .CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        }
        For i = 0 To 3
            kpiRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 25.0F))
        Next
        kpiRow.RowStyles.Add(New RowStyle(SizeType.Percent, 100))

        Dim kpiDef() As (caption As String, value As String, clr As Color, sub1 As String, srcKey As String) = {
            ("Total Entries", totalCount.ToString("N0"), MintPrimary, "All ledger transactions in selected period", "all"),
            ("Sales & Returns", posCount.ToString("N0"), MintDeep, "Completed sales & processed returns", "POS"),
            ("Purchase Orders", procCount.ToString("N0"), Color.FromArgb(30, 90, 180), "Received purchase orders", "Procurement"),
            ("Cash Movements", cashCount.ToString("N0"), Color.FromArgb(140, 80, 0), "Pay-ins, pay-outs, drops & refunds", "Cash")
        }

        For i As Integer = 0 To 3
            Dim kd = kpiDef(i)
            Dim capSrcKey As String = kd.srcKey
            Dim capIdx As Integer = i

            Dim card As New BufferedPanel With {
                .Dock = DockStyle.Fill,
                .BackColor = CardBg,
                .Cursor = Cursors.Hand,
                .Margin = New Padding(If(i = 0, 0, 6), 0, If(i = 3, 0, 6), 0)
            }

            AddHandler card.Paint,
                Sub(s, e)
                    Dim p = CType(s, Panel)
                    Dim g = e.Graphics
                    g.SmoothingMode = SmoothingMode.AntiAlias
                    Dim isActive As Boolean = (_sourceFilter = capSrcKey)
                    Dim rect As New Rectangle(1, 1, p.Width - 3, p.Height - 3)
                    Dim shadowRect As New Rectangle(rect.X + 1, rect.Y + 2, rect.Width - 2, rect.Height - 2)
                    Using shadowBrush As New SolidBrush(Color.FromArgb(14, 0, 0, 0))
                        Using sp = RoundRect(shadowRect, 14)
                            g.FillPath(shadowBrush, sp)
                        End Using
                    End Using
                    Using path = RoundRect(rect, 14)
                        Using br As New SolidBrush(If(isActive, Color.FromArgb(236, 248, 244), CardBg))
                            g.FillPath(br, path)
                        End Using
                        Using pen As New Pen(If(isActive, MintPrimary, Color.FromArgb(210, 220, 216)),
                                             If(isActive, 2.0F, 1.0F))
                            g.DrawPath(pen, path)
                        End Using
                    End Using
                End Sub

            Dim lblCap As New Label With {
                .Text = kd.caption,
                .Font = New Font("Segoe UI", 11),
                .ForeColor = SubText,
                .AutoSize = True,
                .Location = New Point(22, 14),
                .BackColor = Color.Transparent
            }
            Dim lblVal As New Label With {
                .Text = kd.value,
                .Font = New Font("Segoe UI", 26, FontStyle.Bold),
                .ForeColor = kd.clr,
                .AutoSize = True,
                .Location = New Point(20, 36),
                .BackColor = Color.Transparent
            }
            Dim lblSub As New Label With {
                .Text = kd.sub1,
                .Font = New Font("Segoe UI", 10),
                .ForeColor = SubText,
                .AutoSize = True,
                .Location = New Point(22, 100),
                .BackColor = Color.Transparent
            }
            card.Controls.AddRange({lblCap, lblVal, lblSub})

            Dim onClick As EventHandler =
                Sub(s, e)
                    _sourceFilter = If(_sourceFilter = capSrcKey, "all", capSrcKey)
                    For j As Integer = 0 To 3
                        If _kpiPanels(j) IsNot Nothing Then _kpiPanels(j).Invalidate()
                    Next
                    _currentPage = 0
                    ApplyFilters()
                    RenderTabContent()
                End Sub

            AddHandler card.Click, onClick
            For Each child As Control In card.Controls
                child.Cursor = Cursors.Hand
                AddHandler child.Click, onClick
            Next

            _kpiPanels(capIdx) = card
            kpiRow.Controls.Add(card, i, 0)
        Next

        bar.Controls.Add(kpiRow)
        Return bar
    End Function

    Private Shared Function MakeKpiCard(caption As String, value As String,
                                         valueClr As Color, subLabel As String) As Panel
        Dim card As New BufferedPanel With {.Dock = DockStyle.Fill, .BackColor = CardBg}
        AddHandler card.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)

        Dim lblCap As New Label With {
            .Text = caption,
            .Font = New Font("Segoe UI", 11),
            .ForeColor = SubText,
            .AutoSize = True,
            .Location = New Point(22, 14),
            .BackColor = Color.Transparent
        }
        Dim lblVal As New Label With {
            .Text = value,
            .Font = New Font("Segoe UI", 26, FontStyle.Bold),
            .ForeColor = valueClr,
            .AutoSize = True,
            .Location = New Point(20, 36),
            .BackColor = Color.Transparent
        }
        Dim lblSub As New Label With {
            .Text = subLabel,
            .Font = New Font("Segoe UI", 10),
            .ForeColor = SubText,
            .AutoSize = True,
            .Location = New Point(22, 100),
            .BackColor = Color.Transparent
        }
        card.Controls.AddRange({lblCap, lblVal, lblSub})
        Return card
    End Function

    Private Shared Sub RenderTabContent()
        If _contentArea Is Nothing Then Return

        If _reflowAttached AndAlso _scrollHost IsNot Nothing Then
            RemoveHandler _scrollHost.Resize, AddressOf ReflowScrollHost
        End If
        _reflowAttached = False
        _scrollHost = Nothing

        _contentArea.SuspendLayout()
        _contentArea.Controls.Clear()

        Dim content As Panel
        If _activeMainTab = TAB_LEDGER Then
            content = BuildGeneralLedgerContent()
        Else
            content = BuildJournalContent()
        End If
        content.Dock = DockStyle.Fill
        _contentArea.Controls.Add(content)

        _contentArea.ResumeLayout()
    End Sub

    Private Shared Function BuildJournalContent() As Panel
        Dim wrap As New Panel With {.Dock = DockStyle.Fill, .BackColor = MintBg}

        ' No Region on listArea — Regions are aliased and were clipping
        ' PanelRenderer's anti-aliased rounded border (the "broken corner" look).
        ' BackColor is MintBg so the tiny outside-corner triangles blend into the
        ' background; PanelRenderer paints the white rounded card on top.
        Dim listArea As New BufferedPanel With {
        .Dock = DockStyle.Fill,
        .BackColor = MintBg
    }
        AddHandler listArea.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)

        _scrollHost = New Panel With {
        .BackColor = CardBg,
        .AutoScroll = True,
        .Padding = New Padding(0, 4, 0, 0)
    }

        ' colHdr is Transparent now (see BuildJournalColHeader) so the card's
        ' white AA fill forms the top corners under it — no colHdr Region needed.
        Dim colHdr As Panel = BuildJournalColHeader()

        Dim pagerBar As Panel = BuildPagerBar()

        ' Children are NOT docked — each is manually inset 2px from every edge
        ' of listArea, mirroring AccountantReportsPanelBuilder.MakeCard's
        ' card.Resize handler.
        listArea.Controls.Add(_scrollHost)
        listArea.Controls.Add(colHdr)
        listArea.Controls.Add(pagerBar)
        ' Z-order matters: pagerBar must be on top so _scrollHost (opaque
        ' white) never paints over it. colHdr also stays above _scrollHost
        ' for the same reason. _scrollHost is brought to front FIRST, then
        ' colHdr and pagerBar are brought to front after, so they end up
        ' above it in the final stack.
        _scrollHost.BringToFront()
        colHdr.BringToFront()
        pagerBar.BringToFront()
        _scrollHost.AutoScrollMinSize = New Size(0, 1)

        Dim layoutChildren As Action =
    Sub()
        colHdr.Left = 2 : colHdr.Top = 2

        _scrollHost.Left = 2
        _scrollHost.Top = colHdr.Bottom
        ' Reserve pagerBar's full height (52) at the bottom so the scroll
        ' host never extends underneath it.
        _scrollHost.Width = Math.Max(0, listArea.Width - 4)
        _scrollHost.Height = Math.Max(0, listArea.Height - colHdr.Bottom - pagerBar.Height - 2)

        ' pagerBar is manually positioned (not Docked) so it can't be fought
        ' over by WinForms' automatic dock-layout pass running after our
        ' manual code — this is what makes it reliably pinned at the very
        ' bottom, on top, regardless of _scrollHost's content length.
        pagerBar.Left = 2
        pagerBar.Width = Math.Max(0, listArea.Width - 4)
        pagerBar.Top = listArea.Height - pagerBar.Height - 2

        ' Header must use the ROWS' usable width (_scrollHost client area,
        ' which excludes the vertical scrollbar) so the percentage-based
        ' DEBIT/CREDIT columns line up whether or not the scrollbar shows.
        colHdr.Width = Math.Max(0, _scrollHost.ClientSize.Width - 4)
    End Sub
        AddHandler listArea.Resize, Sub() layoutChildren()
        ' Re-fit header + rows when the scrollbar appears/disappears (client size
        ' change without a listArea resize).
        AddHandler _scrollHost.ClientSizeChanged,
    Sub()
        colHdr.Width = Math.Max(0, _scrollHost.ClientSize.Width - 4)
        ReflowRows(_scrollHost)
    End Sub
        layoutChildren()

        wrap.Controls.Add(listArea)

        PopulateJournalRows()

        Return wrap
    End Function
    Private Shared Sub ApplyTopRoundedRegionNow(panel As Panel, radius As Integer)
        If panel.Width > 0 AndAlso panel.Height > 0 Then
            Dim r As Integer = radius
            Dim path As New GraphicsPath()
            path.AddArc(0, 0, r, r, 180, 90)
            path.AddArc(panel.Width - r, 0, r, r, 270, 90)
            path.AddLine(panel.Width, r, panel.Width, panel.Height)
            path.AddLine(panel.Width, panel.Height, 0, panel.Height)
            path.AddLine(0, panel.Height, 0, r)
            path.CloseFigure()
            panel.Region = New Region(path)
        End If
    End Sub

    Private Shared Sub ApplyBottomRoundedRegionNow(panel As Panel, radius As Integer)
        If panel.Width > 0 AndAlso panel.Height > 0 Then
            Dim r As Integer = radius
            Dim path As New GraphicsPath()
            path.AddLine(0, 0, panel.Width, 0)
            path.AddLine(panel.Width, 0, panel.Width, panel.Height - r)
            path.AddArc(panel.Width - r, panel.Height - r, r, r, 0, 90)
            path.AddArc(0, panel.Height - r, r, r, 90, 90)
            path.CloseFigure()
            panel.Region = New Region(path)
        End If
    End Sub

    Private Shared Function BuildJournalColHeader() As Panel
        ' Transparent so it composites onto the card's white AA fill (rounds the
        ' top corners). listArea no longer has a Region, so transparency works.
        Dim hdr As New Panel With {.Height = 44, .BackColor = Color.Transparent}

        Dim lblDate = MakeColLabel("DATE & TIME", New Point(24, 13))
        Dim lblRef = MakeColLabel("REFERENCE", New Point(0, 13))
        Dim lblSrc = MakeColLabel("SOURCE", New Point(0, 13))
        Dim lblAcct = MakeColLabel("ACCOUNT", New Point(0, 13))
        Dim lblDeb = MakeColLabelRight("DEBIT")
        Dim lblCred = MakeColLabelRight("CREDIT")
        hdr.Controls.AddRange({lblDate, lblRef, lblSrc, lblAcct, lblDeb, lblCred})

        AddHandler hdr.Resize,
        Sub()
            Dim w = hdr.Width
            lblDate.Left = 24
            lblRef.Left = CInt(w * 0.16)
            lblSrc.Left = CInt(w * 0.3)
            lblAcct.Left = CInt(w * 0.46)

            Dim drX = CInt(w * 0.68)
            Dim crX = CInt(w * 0.84)
            lblDeb.Top = 13 : lblDeb.Left = drX : lblDeb.Width = Math.Max(80, crX - drX - 12)
            lblCred.Top = 13 : lblCred.Left = crX : lblCred.Width = Math.Max(80, w - crX - 20)
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

    Private Shared Function MakeColLabelRight(text As String) As Label
        Return New Label With {
        .Text = text,
        .Font = New Font("Segoe UI", 10, FontStyle.Bold),
        .ForeColor = SubText,
        .AutoSize = False,
        .Height = 18,
        .TextAlign = ContentAlignment.MiddleRight,
        .BackColor = Color.Transparent
    }
    End Function

    Private Shared Sub PopulateJournalRows()
        If _scrollHost Is Nothing Then Return

        _scrollHost.SuspendLayout()
        _scrollHost.Controls.Clear()

        If _reflowAttached Then
            RemoveHandler _scrollHost.Resize, AddressOf ReflowScrollHost
            _reflowAttached = False
        End If

        If _visibleTxns.Count = 0 Then
            _scrollHost.Controls.Add(BuildEmptyRow(
                If(_sourceFilter = "all",
                   "No ledger entries found for the selected period.",
                   "No entries match the current filter.")))
            UpdatePager(0)
        Else
            Dim pageTxns = _visibleTxns.Skip(_currentPage * PAGE_SIZE).Take(PAGE_SIZE).ToList()
            Dim y As Integer = 4
            For Each t As LedgerTxn In pageTxns
                Dim row As Panel = BuildTxnBlock(t)
                row.Location = New Point(0, y)
                row.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
                _scrollHost.Controls.Add(row)
                row.Width = _scrollHost.ClientSize.Width - 4
                y += row.Height
            Next

            If Not _reflowAttached Then
                _reflowAttached = True
                AddHandler _scrollHost.Resize, AddressOf ReflowScrollHost
            End If

            ' Trailing filler closes off the card surface right after the
            ' last row, so short content never leaves a tall dead gap
            ' between the data and the pager bar pinned below listArea.
            Dim filler As New Panel With {
                .Height = Math.Max(0, _scrollHost.ClientSize.Height - y),
                .BackColor = CardBg,
                .Location = New Point(0, y),
                .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
            }
            _scrollHost.Controls.Add(filler)

            _scrollHost.AutoScrollMinSize = New Size(0, y + 8)
            UpdatePager(_visibleTxns.Count)
        End If

        _scrollHost.ResumeLayout()
        _scrollHost.Invalidate(True)
    End Sub

    Private Shared Sub ReflowScrollHost(sender As Object, e As EventArgs)
        ReflowRows(_scrollHost)
    End Sub

    Private Shared Sub ReflowRows(host As Panel)
        If host Is Nothing Then Return
        Dim w As Integer = host.ClientSize.Width - 4
        For Each c As Control In host.Controls
            c.Width = w
        Next
    End Sub

    Private Shared Function BuildTxnBlock(t As LedgerTxn) As Panel
        Const LINE_H As Integer = 28

        Dim srcColors = GetSourceColors(t.SourceKey)

        ' Build the date/time labels FIRST so we can measure their actual
        ' rendered height and size HEADER_H to fit exactly — this is what
        ' prevents the time label from overhanging into row 0's space.
        Dim lblDate As New Label With {
            .Text = t.TxnDate.ToString("MM/dd/yy"),
            .Font = New Font("Segoe UI", 11, FontStyle.Bold),
            .ForeColor = Color.FromArgb(45, 45, 45),
            .AutoSize = True,
            .BackColor = Color.Transparent
        }
        Dim lblTime As New Label With {
            .Text = t.TxnDate.ToString("h:mm tt"),
            .Font = New Font("Segoe UI", 9),
            .ForeColor = FaintText,
            .AutoSize = True,
            .BackColor = Color.Transparent
        }

        Dim HEADER_H As Integer = Math.Max(34, 8 + lblDate.Height + lblTime.Height)
        ' Row 0 starts centered in the header band; each subsequent row is a
        ' uniform LINE_H step below it. Total height must reach past the
        ' last row's bottom edge.
        Dim row0YCalc As Integer = Math.Max(0, (HEADER_H - LINE_H) \ 2)
        Dim totalH As Integer = row0YCalc + (t.Lines.Count * LINE_H) + 8

        Dim block As New BufferedPanel With {
            .Height = totalH,
            .BackColor = Color.Transparent,
            .Cursor = Cursors.Hand
        }

        AddHandler block.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                If TypeOf block.Tag Is Boolean AndAlso CBool(block.Tag) Then
                    Dim rect As New Rectangle(8, 3, block.Width - 16, totalH - 6)
                    Using path As GraphicsPath = RoundRect(rect, 10)
                        Using b As New SolidBrush(RowHover)
                            e.Graphics.FillPath(b, path)
                        End Using
                    End Using
                End If
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 20, totalH - 1, block.Width - 20, totalH - 1)
                End Using
            End Sub

        Dim lblRef As New Label With {
            .Text = t.Ref,
            .Font = New Font("Segoe UI", 11, FontStyle.Bold),
            .ForeColor = Color.FromArgb(45, 45, 45),
            .AutoSize = True,
            .BackColor = Color.Transparent
        }
        Dim badge As New Label With {
            .Text = t.SourceLabel,
            .Font = New Font("Segoe UI", 9, FontStyle.Bold),
            .ForeColor = srcColors.fore,
            .BackColor = Color.Transparent,
            .AutoSize = True,
            .Padding = New Padding(10, 4, 10, 4)
        }
        AddHandler badge.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim rc = badge.ClientRectangle : rc.Inflate(-1, -1)
                Using br As New SolidBrush(srcColors.back)
                    e.Graphics.FillRoundedRectangle(br, rc, 10)
                End Using
                Using p As New Pen(srcColors.fore, 0.7F)
                    e.Graphics.DrawRoundedRectangle(p, rc, 10)
                End Using
                TextRenderer.DrawText(e.Graphics, badge.Text, badge.Font, badge.ClientRectangle,
                    badge.ForeColor,
                    TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub

        block.Controls.AddRange({lblDate, lblTime, lblRef, badge})

        Dim lineCtrls As New List(Of (acct As Label, dr As Label, cr As Label))
        For Each ln In t.Lines
            Dim hasAcct As Boolean = Not String.IsNullOrEmpty(ln.Account)
            Dim hasDr As Boolean = ln.Debit > 0
            Dim hasCr As Boolean = ln.Credit > 0
            Dim lineIsEmpty As Boolean = Not hasDr AndAlso Not hasCr

            Dim lblAcct As New Label With {
        .Text = If(hasAcct, ln.Account, "—"),
        .Font = New Font("Segoe UI", 11),
        .ForeColor = If(Not hasAcct OrElse lineIsEmpty, FaintText, MintDeep),
        .AutoSize = True,
        .BackColor = Color.Transparent
    }
            Dim lblDr As New Label With {
        .Text = If(hasDr, Peso & ln.Debit.ToString("N2"), ChrW(8212)),
        .Font = New Font("Segoe UI", 11),
        .ForeColor = If(hasDr, Color.FromArgb(60, 70, 68), FaintText),
        .AutoSize = False,
        .TextAlign = ContentAlignment.MiddleRight,
        .BackColor = Color.Transparent,
        .Height = LINE_H
    }
            Dim lblCr As New Label With {
        .Text = If(hasCr, Peso & ln.Credit.ToString("N2"), ChrW(8212)),
        .Font = New Font("Segoe UI", 11),
        .ForeColor = If(hasCr, Color.FromArgb(60, 70, 68), FaintText),
        .AutoSize = False,
        .TextAlign = ContentAlignment.MiddleRight,
        .BackColor = Color.Transparent,
        .Height = LINE_H
    }
            block.Controls.AddRange({lblAcct, lblDr, lblCr})
            lineCtrls.Add((lblAcct, lblDr, lblCr))
        Next

        Dim doLayout As Action =
            Sub()
                Dim w = block.Width
                lblDate.Left = 24 : lblDate.Top = 5
                lblTime.Left = 24 : lblTime.Top = 5 + lblDate.Height

                lblRef.Left = CInt(w * 0.16) : lblRef.Top = (HEADER_H - lblRef.Height) \ 2

                Dim badgeX = CInt(w * 0.3)
                badge.Location = New Point(badgeX, Math.Max(0, (HEADER_H - badge.Height) \ 2))

                Dim acctX = CInt(w * 0.46)
                Dim drX = CInt(w * 0.68)
                Dim crX = CInt(w * 0.84)
                Dim drWidth = Math.Max(80, crX - drX - 12)
                Dim crWidth = Math.Max(80, w - crX - 20)

                ' Row 0 (first account line) starts vertically centered
                ' within the header band, aligned beside the badge.
                ' Every row AFTER that steps by a uniform LINE_H from row 0's
                ' own position — not from HEADER_H — so the gap between row 0
                ' and row 1 matches the gap between row 1 and row 2, etc.
                Dim row0Y As Integer = (HEADER_H - LINE_H) \ 2
                If row0Y < 0 Then row0Y = 0

                For i As Integer = 0 To lineCtrls.Count - 1
                    Dim rowY As Integer = row0Y + (i * LINE_H)
                    lineCtrls(i).acct.Left = acctX
                    lineCtrls(i).acct.Top = rowY + (LINE_H - lineCtrls(i).acct.Height) \ 2
                    lineCtrls(i).dr.Left = drX : lineCtrls(i).dr.Top = rowY : lineCtrls(i).dr.Width = drWidth
                    lineCtrls(i).cr.Left = crX : lineCtrls(i).cr.Top = rowY : lineCtrls(i).cr.Width = crWidth
                Next
            End Sub

        AddHandler block.Resize, Sub() doLayout()
        doLayout()

        Dim openDetail As Action =
            Sub()
                Dim sb As New System.Text.StringBuilder()
                sb.AppendLine($"Date   : {t.TxnDate:MMM dd, yyyy  hh:mm tt}")
                sb.AppendLine($"Ref    : {t.Ref}")
                sb.AppendLine($"Source : {t.SourceLabel}")
                sb.AppendLine()
                For Each ln In t.Lines
                    If ln.Debit > 0 Then
                        sb.AppendLine($"  Dr   {ln.Account,-20} {Peso}{ln.Debit:N2}")
                    Else
                        sb.AppendLine($"  Cr   {ln.Account,-20} {Peso}{ln.Credit:N2}")
                    End If
                Next
                AppModal.Show(sb.ToString(),
                              caption:=$"Journal Entry - {t.Ref}",
                              buttons:=MessageBoxButtons.OK,
                              icon:=MessageBoxIcon.Information)
            End Sub

        AddHandler block.Click, Sub() openDetail()
        For Each ctrl As Control In block.Controls
            AddHandler ctrl.Click, Sub() openDetail()
        Next

        PropagateHover(block)
        Return block
    End Function

    ' =========================================================================
    '  GENERAL LEDGER TAB — restructured to mirror AccountantReportsPanelBuilder's
    '  MakeCard pattern: ONE container (glCard) owns ALL divider-line drawing
    '  for the entire visible list, painted in a single Paint pass from a
    '  shared list of Y-coordinates. Individual rows/section headers are now
    '  plain Panels with solid backgrounds ONLY — they no longer paint their
    '  own divider lines. This eliminates the fragmented/partial-line artifact
    '  that occurred when N independent BufferedPanels each drew their own
    '  inset divider stroke with slightly different endpoints.
    ' =========================================================================
    Private Shared _glDividerYs As New List(Of Integer)
    Private Shared _glCard As Panel

    Private Shared Function BuildGeneralLedgerContent() As Panel
        Dim wrap As New Panel With {.Dock = DockStyle.Fill, .BackColor = MintBg}

        ' No Region — same rationale as BuildJournalContent.
        Dim listArea As New BufferedPanel With {
        .Dock = DockStyle.Fill,
        .BackColor = MintBg
    }
        AddHandler listArea.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)

        _scrollHost = New Panel With {
        .BackColor = CardBg,
        .AutoScroll = True,
        .Padding = New Padding(0, 4, 0, 0)
    }

        Dim colHdr As Panel = BuildGLColHeader()

        listArea.Controls.Add(_scrollHost)
        listArea.Controls.Add(colHdr)
        colHdr.BringToFront()
        _scrollHost.BringToFront()
        _scrollHost.AutoScrollMinSize = New Size(0, 1)

        Dim layoutChildren As Action =
    Sub()
        colHdr.Left = 2 : colHdr.Top = 2

        _scrollHost.Left = 2
        _scrollHost.Top = colHdr.Bottom
        _scrollHost.Width = Math.Max(0, listArea.Width - 4)
        _scrollHost.Height = Math.Max(0, listArea.Height - colHdr.Bottom - 16)

        ' Match header width to the rows' usable width (client area minus
        ' scrollbar) — otherwise DEBIT/CREDIT/BALANCE headers sit right of
        ' the values once the scrollbar appears.
        colHdr.Width = Math.Max(0, _scrollHost.ClientSize.Width - 4)
    End Sub
        AddHandler listArea.Resize, Sub() layoutChildren()
        AddHandler _scrollHost.ClientSizeChanged,
    Sub()
        colHdr.Width = Math.Max(0, _scrollHost.ClientSize.Width - 4)
        ReflowRows(_scrollHost)   ' resizes _glCard (its rows are anchored L/R)
    End Sub
        layoutChildren()

        wrap.Controls.Add(listArea)

        PopulateGeneralLedgerRows()

        Return wrap
    End Function

    Private Shared Function BuildGLColHeader() As Panel
        ' Transparent — see BuildJournalColHeader.
        Dim hdr As New Panel With {.Height = 44, .BackColor = Color.Transparent}

        Dim lblDate = MakeColLabel("DATE", New Point(24, 13))
        Dim lblRef = MakeColLabel("REFERENCE", New Point(0, 13))
        Dim lblSrc = MakeColLabel("SOURCE", New Point(0, 13))
        Dim lblDeb = MakeColLabelRight("DEBIT")
        Dim lblCred = MakeColLabelRight("CREDIT")
        Dim lblBal = MakeColLabelRight("BALANCE")
        hdr.Controls.AddRange({lblDate, lblRef, lblSrc, lblDeb, lblCred, lblBal})

        AddHandler hdr.Resize,
        Sub()
            Dim w = hdr.Width
            lblDate.Left = 24
            lblRef.Left = CInt(w * 0.2)
            lblSrc.Left = CInt(w * 0.38)

            Dim drX = CInt(w * 0.58)
            Dim crX = CInt(w * 0.74)
            Dim balX = CInt(w * 0.88)
            lblDeb.Top = 13 : lblDeb.Left = drX : lblDeb.Width = Math.Max(80, crX - drX - 12)
            lblCred.Top = 13 : lblCred.Left = crX : lblCred.Width = Math.Max(80, balX - crX - 12)
            lblBal.Top = 13 : lblBal.Left = balX : lblBal.Width = Math.Max(80, w - balX - 20)
        End Sub

        AddHandler hdr.Paint,
        Sub(s, e)
            Using pen As New Pen(RowDivider, 1)
                e.Graphics.DrawLine(pen, 20, hdr.Height - 1, hdr.Width - 20, hdr.Height - 1)
            End Using
        End Sub

        Return hdr
    End Function

    Private Shared Sub PopulateGeneralLedgerRows()
        If _scrollHost Is Nothing Then Return

        _scrollHost.SuspendLayout()
        _scrollHost.Controls.Clear()

        If _reflowAttached Then
            RemoveHandler _scrollHost.Resize, AddressOf ReflowScrollHost
            _reflowAttached = False
        End If

        If _visibleTxns.Count = 0 Then
            _scrollHost.Controls.Add(BuildEmptyRow(
                If(_sourceFilter = "all",
                   "No ledger entries found for the selected period.",
                   "No entries match the current filter.")))
            _scrollHost.AutoScrollMinSize = New Size(0, 1)
            _scrollHost.ResumeLayout()
            _scrollHost.Invalidate(True)
            Return
        End If

        Dim byAccount As New Dictionary(Of String, List(Of FlatLine))
        For Each acct In AccountOrder
            byAccount(acct) = New List(Of FlatLine)
        Next
        For Each t In _visibleTxns
            For Each ln In t.Lines
                If Not byAccount.ContainsKey(ln.Account) Then
                    byAccount(ln.Account) = New List(Of FlatLine)
                End If
                byAccount(ln.Account).Add(New FlatLine With {
                    .TxnDate = t.TxnDate,
                    .Ref = t.Ref,
                    .SourceKey = t.SourceKey,
                    .SourceLabel = t.SourceLabel,
                    .Debit = ln.Debit,
                    .Credit = ln.Credit
                })
            Next
        Next

        ' ── Build ONE glCard that hosts every section header + row, and owns
        ' ── ALL divider drawing centrally (mirrors Reports' MakeCard).
        _glDividerYs = New List(Of Integer)

        _glCard = New BufferedPanel With {
            .BackColor = CardBg,
            .Location = New Point(0, 4)
        }
        AddHandler _glCard.Paint,
            Sub(s, e)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Using pen As New Pen(RowDivider, 1)
                    For Each dividerY In _glDividerYs
                        g.DrawLine(pen, 20, dividerY, _glCard.Width - 20, dividerY)
                    Next
                End Using
            End Sub

        Dim y As Integer = 0
        For Each acct In AccountOrder
            Dim lines = byAccount(acct).OrderBy(Function(f) f.TxnDate).ToList()
            Dim endingBalance As Decimal = lines.Sum(Function(f) f.Debit - f.Credit)

            Dim sectionHdr As Panel = BuildAccountSectionHeader(acct, endingBalance)
            sectionHdr.Top = y : sectionHdr.Left = 0
            sectionHdr.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
            _glCard.Controls.Add(sectionHdr)
            If y > 0 Then _glDividerYs.Add(y) ' line above each section (not before the first)
            y += sectionHdr.Height
            _glDividerYs.Add(y - 1) ' line below the section header

            If lines.Count = 0 Then
                Dim emptyRow As Panel = BuildEmptyRow("No activity for this account in the selected period.")
                emptyRow.Height = 50
                emptyRow.Top = y : emptyRow.Left = 0
                emptyRow.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
                _glCard.Controls.Add(emptyRow)
                y += emptyRow.Height
            Else
                Dim running As Decimal = 0
                For i As Integer = 0 To lines.Count - 1
                    Dim f = lines(i)
                    running += (f.Debit - f.Credit)
                    Dim lineRow As Panel = BuildGLLineRow(f, running)
                    lineRow.Top = y : lineRow.Left = 0
                    lineRow.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
                    _glCard.Controls.Add(lineRow)
                    y += lineRow.Height
                    ' Divider after every data row except the very last row overall
                    _glDividerYs.Add(y - 1)
                Next
            End If
        Next

        _glCard.Height = y
        _glCard.Width = _scrollHost.ClientSize.Width - 4
        _glCard.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        _scrollHost.Controls.Add(_glCard)

        ' Trailing filler closes off the card surface right after _glCard,
        ' so short content never leaves a tall dead gap below the last
        ' account section.
        Dim glFiller As New Panel With {
            .Height = Math.Max(0, _scrollHost.ClientSize.Height - y),
            .BackColor = CardBg,
            .Location = New Point(0, y),
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        }
        _scrollHost.Controls.Add(glFiller)

        If Not _reflowAttached Then
            _reflowAttached = True
            AddHandler _scrollHost.Resize, AddressOf ReflowScrollHost
        End If

        _scrollHost.AutoScrollMinSize = New Size(0, y + 12)
        _scrollHost.ResumeLayout()
        _scrollHost.Invalidate(True)
    End Sub

    Private Shared Function BuildAccountSectionHeader(acctName As String, endingBalance As Decimal) As Panel
        ' Plain Panel now (not BufferedPanel) — no self-painted divider lines.
        ' All divider drawing for the General Ledger tab happens once, centrally,
        ' in _glCard's Paint handler (see PopulateGeneralLedgerRows).
        Dim p As New Panel With {.Height = 46, .BackColor = Color.FromArgb(244, 248, 246)}

        Dim lblName As New Label With {
            .Text = acctName,
            .Font = New Font("Segoe UI Semibold", 11, FontStyle.Bold),
            .ForeColor = MintDark,
            .AutoSize = True,
            .Location = New Point(24, 12),
            .BackColor = Color.Transparent
        }

        Dim balSign As String = If(endingBalance < 0, "-", "")
        Dim lblBalance As New Label With {
            .Text = $"Balance: {balSign}{Peso}{Math.Abs(endingBalance):N2}",
            .Font = New Font("Segoe UI Semibold", 10.5F, FontStyle.Bold),
            .ForeColor = If(endingBalance >= 0, MintDeep, AccentRed),
            .AutoSize = True,
            .BackColor = Color.Transparent
        }

        AddHandler p.Resize,
            Sub()
                lblBalance.Left = p.Width - lblBalance.Width - 24
                lblBalance.Top = 12
            End Sub

        p.Controls.Add(lblName)
        p.Controls.Add(lblBalance)
        Return p
    End Function

Private Shared Function BuildGLLineRow(f As FlatLine, running As Decimal) As Panel
        Const ROW_H As Integer = 50
        Dim srcColors = GetSourceColors(f.SourceKey)

        ' Plain Panel now (not BufferedPanel) — no self-painted divider line.
        ' Hover highlight still needs custom drawing, so we keep a lightweight
        ' Paint handler for JUST the hover fill; the divider line for this row
        ' is instead contributed to _glCard's shared _glDividerYs list.
        Dim row As New Panel With {.Height = ROW_H, .BackColor = CardBg, .Cursor = Cursors.Hand}

        AddHandler row.Paint,
            Sub(s, e)
                If TypeOf row.Tag Is Boolean AndAlso CBool(row.Tag) Then
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                    Dim rect As New Rectangle(8, 3, row.Width - 16, ROW_H - 6)
                    Using path As GraphicsPath = RoundRect(rect, 10)
                        Using b As New SolidBrush(RowHover)
                            e.Graphics.FillPath(b, path)
                        End Using
                    End Using
                End If
            End Sub

        Dim lblDate As New Label With {
            .Text = f.TxnDate.ToString("MM/dd/yy"),
            .Font = New Font("Segoe UI", 11),
            .ForeColor = Color.FromArgb(45, 45, 45),
            .AutoSize = True,
            .BackColor = Color.Transparent
        }
        Dim lblRef As New Label With {
            .Text = f.Ref,
            .Font = New Font("Segoe UI", 11),
            .ForeColor = SubText,
            .AutoSize = True,
            .BackColor = Color.Transparent
        }
        Dim badge As New Label With {
            .Text = f.SourceLabel,
            .Font = New Font("Segoe UI", 9, FontStyle.Bold),
            .ForeColor = srcColors.fore,
            .BackColor = Color.Transparent,
            .AutoSize = True,
            .Padding = New Padding(8, 3, 8, 3)
        }
        AddHandler badge.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim rc = badge.ClientRectangle : rc.Inflate(-1, -1)
                Using br As New SolidBrush(srcColors.back)
                    e.Graphics.FillRoundedRectangle(br, rc, 9)
                End Using
                TextRenderer.DrawText(e.Graphics, badge.Text, badge.Font, badge.ClientRectangle,
                    badge.ForeColor, TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub

        Dim hasDr As Boolean = f.Debit > 0
        Dim hasCr As Boolean = f.Credit > 0

        Dim lblDr As New Label With {
    .Text = If(hasDr, Peso & f.Debit.ToString("N2"), ChrW(8212)),
    .Font = New Font("Segoe UI", 11),
    .ForeColor = If(hasDr, Color.FromArgb(60, 70, 68), FaintText),
    .AutoSize = False,
    .TextAlign = ContentAlignment.MiddleRight,
    .BackColor = Color.Transparent,
    .Height = ROW_H - 10
}
        Dim lblCr As New Label With {
    .Text = If(hasCr, Peso & f.Credit.ToString("N2"), ChrW(8212)),
    .Font = New Font("Segoe UI", 11),
    .ForeColor = If(hasCr, Color.FromArgb(60, 70, 68), FaintText),
    .AutoSize = False,
    .TextAlign = ContentAlignment.MiddleRight,
    .BackColor = Color.Transparent,
    .Height = ROW_H - 10
}
        Dim balSign As String = If(running < 0, "-", "")
        Dim lblBal As New Label With {
            .Text = $"{balSign}{Peso}{Math.Abs(running):N2}",
            .Font = New Font("Segoe UI", 12, FontStyle.Bold),
            .ForeColor = If(running >= 0, MintDark, AccentRed),
            .AutoSize = False,
            .TextAlign = ContentAlignment.MiddleRight,
            .BackColor = Color.Transparent,
            .Height = ROW_H - 10
        }

        row.Controls.AddRange({lblDate, lblRef, badge, lblDr, lblCr, lblBal})

        AddHandler row.Resize,
            Sub()
                Dim w = row.Width
                lblDate.Left = 24 : lblDate.Top = (ROW_H - lblDate.Height) \ 2
                lblRef.Left = CInt(w * 0.2) : lblRef.Top = (ROW_H - lblRef.Height) \ 2
                badge.Location = New Point(CInt(w * 0.38), Math.Max(0, (ROW_H - badge.Height) \ 2))

                Dim drX = CInt(w * 0.58)
                Dim crX = CInt(w * 0.74)
                Dim balX = CInt(w * 0.88)
                lblDr.Left = drX : lblDr.Top = (ROW_H - lblDr.Height) \ 2 : lblDr.Width = Math.Max(80, crX - drX - 12)
                lblCr.Left = crX : lblCr.Top = (ROW_H - lblCr.Height) \ 2 : lblCr.Width = Math.Max(80, balX - crX - 12)
                lblBal.Left = balX : lblBal.Top = (ROW_H - lblBal.Height) \ 2 : lblBal.Width = Math.Max(80, w - balX - 20)
            End Sub

        PropagateHover(row)
        ' Hovering a row must also repaint glCard (glCard doesn't own the hover
        ' fill, but its bounds encompass all rows — invalidating the row itself
        ' is sufficient since it's a direct child panel above glCard's own paint).
        Return row
    End Function

    Private Shared Function GetSourceColors(sourceKey As String) As (fore As Color, back As Color)
        Select Case sourceKey
            Case "POS" : Return BadgePOS
            Case "Procurement" : Return BadgeProcurement
            Case "Cash" : Return BadgeCash
            Case Else : Return (SubText, Color.FromArgb(238, 238, 238))
        End Select
    End Function

    Private Shared Function BuildPagerBar() As Panel
        Dim wrapper As New Panel With {
    .Height = 52,
    .BackColor = Color.Transparent
}

        AddHandler wrapper.Paint,
    Sub(s, e)
        Using dp As New Pen(RowDivider, 1)
            e.Graphics.DrawLine(dp, 0, 0, wrapper.Width, 0)
        End Using
    End Sub

        Dim bar As New Panel With {
        .BackColor = Color.FromArgb(248, 251, 249),
        .Height = 44,
        .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Bottom
    }

        AddHandler wrapper.Resize,
        Sub()
            bar.Left = 16
            bar.Width = wrapper.Width - 32
            bar.Top = wrapper.Height - bar.Height - 4
        End Sub

        AddHandler bar.Paint,
        Sub(s, e)
            Dim g = e.Graphics
            g.SmoothingMode = SmoothingMode.AntiAlias
            Dim w = bar.Width
            Dim h = bar.Height
            Dim r As Integer = 10
            If w <= 0 OrElse h <= 0 Then Return

            Using gp As New GraphicsPath()
                gp.AddArc(0, 0, r, r, 180, 90)
                gp.AddArc(w - r, 0, r, r, 270, 90)
                gp.AddArc(w - r, h - r, r, r, 0, 90)
                gp.AddArc(0, h - r, r, r, 90, 90)
                gp.CloseFigure()

                Using fill As New SolidBrush(Color.FromArgb(248, 251, 249))
                    g.FillPath(fill, gp)
                End Using
                Using pen As New Pen(RowDivider, 1)
                    g.DrawPath(pen, gp)
                End Using
            End Using
        End Sub

        _btnPrev = New Button With {
        .Text = ChrW(8249),
        .FlatStyle = FlatStyle.Flat,
        .Cursor = Cursors.Hand,
        .Size = New Size(32, 32),
        .BackColor = Color.FromArgb(220, 235, 232),
        .ForeColor = MintDark,
        .Font = New Font("Segoe UI", 14, FontStyle.Bold),
        .TextAlign = ContentAlignment.MiddleCenter
    }
        _btnPrev.FlatAppearance.BorderSize = 0

        _btnNext = New Button With {
        .Text = ChrW(8250),
        .FlatStyle = FlatStyle.Flat,
        .Cursor = Cursors.Hand,
        .Size = New Size(32, 32),
        .BackColor = Color.FromArgb(220, 235, 232),
        .ForeColor = MintDark,
        .Font = New Font("Segoe UI", 14, FontStyle.Bold),
        .TextAlign = ContentAlignment.MiddleCenter
    }
        _btnNext.FlatAppearance.BorderSize = 0

        _lblPageInfo = New Label With {
        .Font = New Font("Segoe UI", 9.5F),
        .ForeColor = SubText,
        .AutoSize = True,
        .BackColor = Color.Transparent
    }

        AddHandler bar.Resize,
        Sub()
            Dim cx As Integer = bar.Width \ 2
            _btnPrev.Left = cx - _btnPrev.Width - _lblPageInfo.Width \ 2 - 8
            _btnPrev.Top = (bar.Height - _btnPrev.Height) \ 2
            _lblPageInfo.Left = cx - _lblPageInfo.Width \ 2
            _lblPageInfo.Top = (bar.Height - _lblPageInfo.Height) \ 2
            _btnNext.Left = cx + _lblPageInfo.Width \ 2 + 8
            _btnNext.Top = (bar.Height - _btnNext.Height) \ 2
            bar.Invalidate()
        End Sub

        AddHandler _btnPrev.Click,
        Sub()
            If _currentPage > 0 Then
                _currentPage -= 1
                PopulateJournalRows()
            End If
        End Sub
        AddHandler _btnNext.Click,
        Sub()
            Dim totalPages As Integer = Math.Max(1, CInt(Math.Ceiling(_visibleTxns.Count / PAGE_SIZE)))
            If _currentPage < totalPages - 1 Then
                _currentPage += 1
                PopulateJournalRows()
            End If
        End Sub

        bar.Controls.AddRange({_btnPrev, _lblPageInfo, _btnNext})
        wrapper.Controls.Add(bar)
        Return wrapper
    End Function

    Private Shared Sub UpdatePager(totalRows As Integer)
        If _lblPageInfo Is Nothing Then Return
        Dim totalPages As Integer = Math.Max(1, CInt(Math.Ceiling(totalRows / PAGE_SIZE)))
        _lblPageInfo.Text = $"Page {_currentPage + 1} of {totalPages}  ({totalRows} total)"
        _btnPrev.Enabled = (_currentPage > 0)
        _btnNext.Enabled = (_currentPage < totalPages - 1)
        If _lblPageInfo.Parent IsNot Nothing Then _lblPageInfo.Parent.PerformLayout()
    End Sub

    Private Shared Sub ApplyRoundedRegion(panel As Panel)
        Dim applyRegion As Action =
            Sub()
                If panel.Width > 0 AndAlso panel.Height > 0 Then
                    panel.Region = New Region(RoundRect(New Rectangle(0, 0, panel.Width, panel.Height), 14))
                End If
            End Sub
        AddHandler panel.Resize, Sub() applyRegion()
        applyRegion()
    End Sub

    Private Shared Sub ApplyTopRoundedRegion(panel As Panel, radius As Integer)
        Dim applyRegion As Action =
            Sub()
                If panel.Width > 0 AndAlso panel.Height > 0 Then
                    Dim r As Integer = radius
                    Dim path As New GraphicsPath()
                    path.AddArc(0, 0, r, r, 180, 90)
                    path.AddArc(panel.Width - r, 0, r, r, 270, 90)
                    path.AddLine(panel.Width, r, panel.Width, panel.Height)
                    path.AddLine(panel.Width, panel.Height, 0, panel.Height)
                    path.AddLine(0, panel.Height, 0, r)
                    path.CloseFigure()
                    panel.Region = New Region(path)
                End If
            End Sub
        AddHandler panel.Resize, Sub() applyRegion()
        applyRegion()
    End Sub

    Private Shared Sub ApplyBottomRoundedRegion(panel As Panel, radius As Integer)
        Dim applyRegion As Action =
            Sub()
                If panel.Width > 0 AndAlso panel.Height > 0 Then
                    Dim r As Integer = radius
                    Dim path As New GraphicsPath()
                    path.AddLine(0, 0, panel.Width, 0)
                    path.AddLine(panel.Width, 0, panel.Width, panel.Height - r)
                    path.AddArc(panel.Width - r, panel.Height - r, r, r, 0, 90)
                    path.AddArc(0, panel.Height - r, r, r, 90, 90)
                    path.CloseFigure()
                    panel.Region = New Region(path)
                End If
            End Sub
        AddHandler panel.Resize, Sub() applyRegion()
        applyRegion()
    End Sub

    Private Shared Function RoundRect(rect As Rectangle, radius As Integer) As GraphicsPath
        Dim r As Integer = radius
        Dim path As New GraphicsPath()
        path.AddArc(rect.X, rect.Y, r, r, 180, 90)
        path.AddArc(rect.Right - r, rect.Y, r, r, 270, 90)
        path.AddArc(rect.Right - r, rect.Bottom - r, r, r, 0, 90)
        path.AddArc(rect.X, rect.Bottom - r, r, r, 90, 90)
        path.CloseFigure()
        Return path
    End Function

    Private Shared Function BuildEmptyRow(message As String) As Panel
        Dim p As New Panel With {
            .Height = 90,
            .Dock = DockStyle.Top,
            .BackColor = Color.Transparent
        }
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

    Private Sub AccountantLedgerPanelBuilder_Load(sender As Object, e As EventArgs) Handles MyBase.Load

    End Sub
End Class