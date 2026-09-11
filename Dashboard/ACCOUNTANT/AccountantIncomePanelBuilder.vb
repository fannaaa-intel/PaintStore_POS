Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports System.Linq
Imports MySql.Data.MySqlClient

' =============================================================================
'  AccountantIncomePanelBuilder  —  Screen 2: Income & Expenses
'
'  FIXES APPLIED:
'    1. Row click → AppModal.Show with sale/PO details
'    2. Visible DB error panel instead of silent Debug.WriteLine
'    3. Refresh button added to title bar
'    4. Tab hover state fixed — uses shared _activeTab check, not captured bool
'    5. AddHandler _rowsHost.Resize stacking fixed — RemoveHandler before Add
'    6. Cancelled POs excluded from Expense total and rows
'    7. Pagination added (PAGE_SIZE = 50) for both income and expense lists
' =============================================================================
Public Class AccountantIncomePanelBuilder

    ' ── PALETTE ──────────────────────────────────────────────────────────────
    Private Shared ReadOnly MintDark As Color = Theme.Dark
    Private Shared ReadOnly MintPrimary As Color = Theme.Primary
    Private Shared ReadOnly MintDeep As Color = Theme.Deep
    Private Shared ReadOnly MintBg As Color = Theme.PageBg
    Private Shared ReadOnly CardBg As Color = Color.White
    Private Shared ReadOnly SubText As Color = Theme.SubText
    Private Shared ReadOnly FaintText As Color = Theme.FaintText
    Private Shared ReadOnly RowDivider As Color = Theme.RowDivider
    Private Shared ReadOnly RowHover As Color = Theme.RowHover
    Private Shared ReadOnly AccentOrange As Color = Theme.Warning
    Private Shared ReadOnly AccentRed As Color = Theme.Danger
    Private Shared ReadOnly Peso As String = ChrW(8369)

    ' ── PAGING ───────────────────────────────────────────────────────────────
    Private Const PAGE_SIZE As Integer = 50
    Private Shared _currentPage As Integer = 0
    Private Shared _lblPageInfo As Label
    Private Shared _btnPrev As Button
    Private Shared _btnNext As Button

    ' ── DATA MODELS ──────────────────────────────────────────────────────────
    Private Class IncomeRow
        Public SaleNo As String
        Public SaleDate As DateTime
        Public Cashier As String
        Public PayMethod As String
        Public Total As Decimal
    End Class

    Private Class ExpenseRow
        Public PoNo As String
        Public OrderDate As DateTime
        Public Supplier As String
        Public Status As String
        Public TotalCost As Decimal
    End Class

    ' ── STATE ────────────────────────────────────────────────────────────────
    Private Shared _container As Panel
    Private Shared _filter As String = "month"
    Private Shared _activeTab As String = "income"
    Private Shared _loadError As String = ""   ' FIX 2

    Private Shared _incomeRows As New List(Of IncomeRow)
    Private Shared _expenseRows As New List(Of ExpenseRow)
    Private Shared _totalIncome As Decimal
    Private Shared _totalExpenses As Decimal
    ' Money handed back to customers in the period. Sales are recorded gross, so
    ' without this Net Profit ignored refunds entirely.
    Private Shared _totalRefunds As Decimal

    Private Shared _rowsHost As Panel

    ' FIX 5: keep a reference to the reflow handler so we can remove it
    Private Shared _reflowHandler As EventHandler = Nothing

    ' Filter keys
    Private Shared ReadOnly FilterKeys() As String = {"today", "week", "month", "all"}
    Private Shared ReadOnly FilterLabels() As String = {"Today", "This Week", "This Month", "All Time"}
    Private Shared _filterBtns() As Button
    Private Shared _activeFilterIdx As Integer = 2

    Private Shared _tabIncome As Button
    Private Shared _tabExpenses As Button

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Shared Sub LoadIncome(container As Panel,
                                  Optional filter As String = "month",
                                  Optional tab As String = "income")
        _container = container
        _filter = filter
        _activeTab = tab
        _currentPage = 0
        Dim idx As Integer = Array.IndexOf(FilterKeys, filter)
        _activeFilterIdx = If(idx >= 0, idx, 2)
        LoadData()
        Render()
    End Sub

    ' =========================================================================
    '  DATA
    ' =========================================================================
    Private Shared Sub LoadData()
        _incomeRows.Clear()
        _expenseRows.Clear()
        _totalIncome = 0
        _totalExpenses = 0
        _totalRefunds = 0
        _loadError = ""   ' FIX 2: reset each load

        Dim dateWhere As String = BuildDateWhere()
        Dim poDateWhere As String = BuildPoDateWhere()

        Try
            Using conn = Db.GetConnection()
                conn.Open()

                ' ── Income rows ──
                Using cmd As New MySqlCommand(
                    "SELECT s.sale_no, s.sale_datetime, u.full_name, " &
                    "COALESCE(pm.name,'—') AS method, s.total " &
                    "FROM sales s " &
                    "JOIN users u ON s.user_id = u.user_id " &
                    "LEFT JOIN payment_methods pm ON s.method_id = pm.method_id " &
                    "WHERE s.status IN ('Completed','Partially Refunded')" & dateWhere &
                    " ORDER BY s.sale_datetime DESC", conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            _incomeRows.Add(New IncomeRow With {
                                .SaleNo = rdr("sale_no").ToString(),
                                .SaleDate = Convert.ToDateTime(rdr("sale_datetime")),
                                .Cashier = rdr("full_name").ToString(),
                                .PayMethod = rdr("method").ToString(),
                                .Total = Convert.ToDecimal(rdr("total"))
                            })
                        End While
                    End Using
                End Using

                ' ── Expense rows — FIX 6: exclude Cancelled ──
                Using cmd As New MySqlCommand(
                    "SELECT po.po_no, po.order_date, s.name AS sname, " &
                    "po.status, po.total_cost " &
                    "FROM purchase_orders po " &
                    "JOIN suppliers s ON po.supplier_id = s.supplier_id " &
                    "WHERE po.status <> 'Cancelled'" & poDateWhere &
                    " ORDER BY po.order_date DESC", conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            _expenseRows.Add(New ExpenseRow With {
                                .PoNo = rdr("po_no").ToString(),
                                .OrderDate = Convert.ToDateTime(rdr("order_date")),
                                .Supplier = rdr("sname").ToString(),
                                .Status = rdr("status").ToString(),
                                .TotalCost = Convert.ToDecimal(rdr("total_cost"))
                            })
                        End While
                    End Using
                End Using

                ' ── Totals ──
                Using cmd As New MySqlCommand(
                    "SELECT COALESCE(SUM(total),0) FROM sales " &
                    "WHERE status IN ('Completed','Partially Refunded')" & dateWhere, conn)
                    _totalIncome = Convert.ToDecimal(cmd.ExecuteScalar())
                End Using

                ' FIX 6: exclude Cancelled from expense total
                Using cmd As New MySqlCommand(
                    "SELECT COALESCE(SUM(total_cost),0) FROM purchase_orders " &
                    "WHERE status <> 'Cancelled'" & poDateWhere, conn)
                    _totalExpenses = Convert.ToDecimal(cmd.ExecuteScalar())
                End Using

                ' Refunds for the same period. Sales are stored gross, so money
                ' handed back has to come off the top or Net Profit reports a
                ' store that refunded heavily as having earned just as much.
                Using cmd As New MySqlCommand(
                    "SELECT COALESCE(SUM(total_refund),0) FROM returns " &
                    "WHERE 1=1" & BuildRefundDateWhere(), conn)
                    _totalRefunds = Convert.ToDecimal(cmd.ExecuteScalar())
                End Using

            End Using
        Catch ex As Exception
            ' FIX 2: store error for visible display
            _loadError = "Could not load data: " & ex.Message
            Debug.WriteLine("AccountantIncome LoadData error: " & ex.Message)
        End Try
    End Sub

    Private Shared Function BuildDateWhere() As String
        Select Case _filter
            Case "today" : Return " AND DATE(sale_datetime) = CURDATE()"
            Case "week" : Return " AND sale_datetime >= CURDATE() - INTERVAL 7 DAY"
            Case "month" : Return " AND sale_datetime >= CURDATE() - INTERVAL 30 DAY"
            Case Else : Return ""
        End Select
    End Function

    ' Same windows as BuildDateWhere, against the returns table's own date.
    Private Shared Function BuildRefundDateWhere() As String
        Select Case _filter
            Case "today" : Return " AND DATE(return_datetime) = CURDATE()"
            Case "week" : Return " AND return_datetime >= CURDATE() - INTERVAL 7 DAY"
            Case "month" : Return " AND return_datetime >= CURDATE() - INTERVAL 30 DAY"
            Case Else : Return ""
        End Select
    End Function

    Private Shared Function BuildPoDateWhere() As String
        Select Case _filter
            Case "today" : Return " AND order_date = CURDATE()"
            Case "week" : Return " AND order_date >= CURDATE() - INTERVAL 7 DAY"
            Case "month" : Return " AND order_date >= CURDATE() - INTERVAL 30 DAY"
            Case Else : Return ""
        End Select
    End Function

    ' =========================================================================
    '  RENDER
    ' =========================================================================
    Private Shared Sub Render()
        _container.SuspendLayout()
        _container.Controls.Clear()
        _container.BackColor = MintBg

        Dim tableWrapper As New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = MintBg,
            .Padding = New Padding(0)
        }

        Dim listArea As New BufferedPanel With {
            .Dock = DockStyle.Fill,
            .BackColor = CardBg,
            .Padding = New Padding(1)
        }
        AddHandler listArea.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)

        _rowsHost = New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = Color.Transparent,
            .AutoScroll = True,
            .Padding = New Padding(0, 4, 0, 8)
        }

        Dim colHdr As Panel = BuildColHeader()
        colHdr.Dock = DockStyle.Top

        ' FIX 7: pager bar
        Dim pagerBar As Panel = BuildPagerBar()
        pagerBar.Dock = DockStyle.Bottom

        listArea.Controls.Add(pagerBar)
        listArea.Controls.Add(colHdr)
        listArea.Controls.Add(_rowsHost)
        colHdr.BringToFront()
        _rowsHost.BringToFront()

        Dim spacerBottom As New Panel With {.Dock = DockStyle.Bottom, .Height = 16, .BackColor = MintBg}
        Dim spacerTop As New Panel With {.Dock = DockStyle.Top, .Height = 16, .BackColor = MintBg}
        Dim spacerLeft As New Panel With {.Dock = DockStyle.Left, .Width = 24, .BackColor = MintBg}
        Dim spacerRight As New Panel With {.Dock = DockStyle.Right, .Width = 24, .BackColor = MintBg}

        tableWrapper.Controls.Add(listArea)
        tableWrapper.Controls.Add(spacerBottom)
        tableWrapper.Controls.Add(spacerTop)
        tableWrapper.Controls.Add(spacerLeft)
        tableWrapper.Controls.Add(spacerRight)

        Dim topRegion As Panel = BuildTopRegion()
        topRegion.Dock = DockStyle.Top

        _container.Controls.Add(tableWrapper)
        _container.Controls.Add(topRegion)

        PopulateRows()
        _container.ResumeLayout()
    End Sub

    ' =========================================================================
    '  TOP REGION
    ' =========================================================================
    Private Shared Function BuildTopRegion() As Panel
        Const SIDE_PAD As Integer = 24

        Dim region As New Panel With {
            .BackColor = MintBg,
            .AutoSize = False
        }

        ' ── Row 1: Title bar (72px) — FIX 3: includes Refresh button ────────
        Dim titleBar As New Panel With {
            .Height = 72,
            .Dock = DockStyle.Top,
            .BackColor = Color.Transparent
        }
        Dim lblTitle As New Label With {
            .Text = "Income && Expenses",
            .Font = New Font("Segoe UI", 28, FontStyle.Bold),
            .ForeColor = MintDark,
            .AutoSize = True,
            .Location = New Point(SIDE_PAD, 16)
        }

        ' FIX 3: Refresh button
        Dim btnRefresh As New Button With {
            .Text = "⟳  Refresh",
            .FlatStyle = FlatStyle.Flat,
            .Cursor = Cursors.Hand,
            .Size = New Size(100, 34),
            .Font = New Font("Segoe UI", 9.5F),
            .BackColor = Color.FromArgb(220, 235, 232),
            .ForeColor = MintDark,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        btnRefresh.FlatAppearance.BorderSize = 0

        AddHandler titleBar.Resize,
            Sub()
                btnRefresh.Left = titleBar.Width - btnRefresh.Width - SIDE_PAD
                btnRefresh.Top = (titleBar.Height - btnRefresh.Height) \ 2
            End Sub

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
        AddHandler btnRefresh.Click, Sub() LoadIncome(_container, _filter, _activeTab)

        titleBar.Controls.Add(lblTitle)
        titleBar.Controls.Add(btnRefresh)

        ' ── Row 2: Filter pills + Tab buttons (52px) ─────────────────────────
        Dim pillBar As New Panel With {
            .Height = 52,
            .Dock = DockStyle.Top,
            .BackColor = Color.Transparent
        }

        ' FIX 4: tabs created WITHOUT capturing isActive as a bool —
        '         hover handlers check _activeTab at call time instead
        _tabIncome = MakeTabButton("Income", "income")
        _tabExpenses = MakeTabButton("Expenses", "expenses")

        AddHandler _tabIncome.Click, Sub() LoadIncome(_container, _filter, "income")
        AddHandler _tabExpenses.Click, Sub() LoadIncome(_container, _filter, "expenses")

        ReDim _filterBtns(FilterLabels.Length - 1)
        Dim filterHost As New FlowLayoutPanel With {
            .FlowDirection = FlowDirection.LeftToRight,
            .AutoSize = True,
            .BackColor = Color.Transparent,
            .WrapContents = False,
            .Location = New Point(SIDE_PAD, 10)
        }

        For i As Integer = 0 To FilterLabels.Length - 1
            Dim idx As Integer = i
            Dim capKey As String = FilterKeys(i)
            Dim b As New Button With {
                .Text = FilterLabels(i),
                .FlatStyle = FlatStyle.Flat,
                .Cursor = Cursors.Hand,
                .Size = New Size(96, 34),
                .Font = New Font("Segoe UI", 9.5F),
                .Margin = New Padding(0, 0, 6, 0)
            }
            b.FlatAppearance.BorderSize = 0

            Dim styleBtn As Action(Of Boolean, Boolean) =
                Sub(active As Boolean, hover As Boolean)
                    If active Then
                        b.BackColor = MintPrimary
                        b.ForeColor = Color.White
                        b.Font = New Font("Segoe UI Semibold", 9.5F)
                    ElseIf hover Then
                        b.BackColor = Color.FromArgb(210, 240, 235)
                        b.ForeColor = MintDark
                        b.Font = New Font("Segoe UI", 9.5F)
                    Else
                        b.BackColor = Color.FromArgb(220, 235, 232)
                        b.ForeColor = SubText
                        b.Font = New Font("Segoe UI", 9.5F)
                    End If
                    b.Invalidate()
                End Sub

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

            AddHandler b.MouseEnter, Sub() If idx <> _activeFilterIdx Then styleBtn(False, True)
            AddHandler b.MouseLeave, Sub() If idx <> _activeFilterIdx Then styleBtn(False, False)
            AddHandler b.Click,
                Sub()
                    If _filterBtns(_activeFilterIdx) IsNot Nothing Then
                        _filterBtns(_activeFilterIdx).BackColor = Color.FromArgb(220, 235, 232)
                        _filterBtns(_activeFilterIdx).ForeColor = SubText
                        _filterBtns(_activeFilterIdx).Font = New Font("Segoe UI", 9.5F)
                        _filterBtns(_activeFilterIdx).Invalidate()
                    End If
                    LoadIncome(_container, capKey, _activeTab)
                End Sub

            styleBtn(i = _activeFilterIdx, False)
            _filterBtns(i) = b
            filterHost.Controls.Add(b)
        Next

        pillBar.Controls.AddRange({filterHost, _tabIncome, _tabExpenses})

        Dim PositionTabs As Action =
            Sub()
                _tabExpenses.Left = pillBar.Width - _tabExpenses.Width - SIDE_PAD
                _tabExpenses.Top = (pillBar.Height - _tabExpenses.Height) \ 2
                _tabIncome.Left = _tabExpenses.Left - _tabIncome.Width - 6
                _tabIncome.Top = (pillBar.Height - _tabIncome.Height) \ 2
            End Sub

        AddHandler pillBar.Resize, Sub() PositionTabs()

        ' ── Row 3: KPI cards (165px) ─────────────────────────────────────────
        Dim kpiBar As Panel = BuildKpiBar()
        kpiBar.Dock = DockStyle.Top

        region.Controls.Add(kpiBar)
        region.Controls.Add(pillBar)
        region.Controls.Add(titleBar)

        region.Height = 72 + 52 + 165
        Return region
    End Function

    ' =========================================================================
    '  TAB BUTTON FACTORY — FIX 4: no captured isActive bool
    ' =========================================================================
    Private Shared Function MakeTabButton(text As String, tabKey As String) As Button
        Dim isIncome As Boolean = (tabKey = "income")
        Dim activeColor As Color = If(isIncome, MintPrimary, AccentOrange)

        Dim b As New Button With {
            .Text = text,
            .FlatStyle = FlatStyle.Flat,
            .Cursor = Cursors.Hand,
            .Size = New Size(110, 32),
            .Margin = New Padding(0)
        }
        b.FlatAppearance.BorderSize = 0

        Dim applyStyle As Action =
            Sub()
                Dim isActive As Boolean = (_activeTab = tabKey)
                If isActive Then
                    b.BackColor = activeColor
                    b.ForeColor = Color.White
                    b.Font = New Font("Segoe UI Semibold", 9.5F)
                Else
                    b.BackColor = Color.FromArgb(220, 235, 232)
                    b.ForeColor = SubText
                    b.Font = New Font("Segoe UI", 9.5F)
                End If
                b.Invalidate()
            End Sub

        applyStyle()

        AddHandler b.Paint,
            Sub(s, e)
                Dim bb = CType(s, Button)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect = bb.ClientRectangle
                rect.Inflate(-1, -1)
                Using br As New SolidBrush(bb.BackColor)
                    g.FillRoundedRectangle(br, rect, 8)
                End Using
                TextRenderer.DrawText(g, bb.Text, bb.Font, bb.ClientRectangle,
                    bb.ForeColor,
                    TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub

        ' FIX 4: hover checks _activeTab live instead of captured bool
        AddHandler b.MouseEnter,
            Sub()
                If _activeTab <> tabKey Then
                    b.BackColor = Color.FromArgb(210, 240, 235)
                    b.ForeColor = MintDark
                    b.Invalidate()
                End If
            End Sub
        AddHandler b.MouseLeave,
            Sub()
                If _activeTab <> tabKey Then
                    b.BackColor = Color.FromArgb(220, 235, 232)
                    b.ForeColor = SubText
                    b.Invalidate()
                End If
            End Sub

        Return b
    End Function

    ' =========================================================================
    '  COLUMN HEADER
    ' =========================================================================
    Private Shared Function BuildColHeader() As Panel
        Dim hdr As New Panel With {.Height = 44, .BackColor = Color.Transparent}

        Dim labels As Label()
        If _activeTab = "income" Then
            labels = {
                MakeColLabel("SALE NO", New Point(24, 13)),
                MakeColLabel("DATE", New Point(0, 13)),
                MakeColLabel("CASHIER", New Point(0, 13)),
                MakeColLabel("METHOD", New Point(0, 13)),
                MakeColLabel("TOTAL", New Point(0, 13), AnchorStyles.Top Or AnchorStyles.Right)
            }
            hdr.Controls.AddRange(labels)
            AddHandler hdr.Resize,
                Sub()
                    Dim w = hdr.Width
                    labels(0).Left = 24
                    labels(1).Left = CInt(w * 0.22)
                    labels(2).Left = CInt(w * 0.38)
                    labels(3).Left = CInt(w * 0.6)
                    labels(4).Left = w - labels(4).Width - 24
                End Sub
        Else
            labels = {
                MakeColLabel("PO NO", New Point(24, 13)),
                MakeColLabel("DATE", New Point(0, 13)),
                MakeColLabel("SUPPLIER", New Point(0, 13)),
                MakeColLabel("STATUS", New Point(0, 13)),
                MakeColLabel("AMOUNT", New Point(0, 13), AnchorStyles.Top Or AnchorStyles.Right)
            }
            hdr.Controls.AddRange(labels)
            AddHandler hdr.Resize,
                Sub()
                    Dim w = hdr.Width
                    labels(0).Left = 24
                    labels(1).Left = CInt(w * 0.22)
                    labels(2).Left = CInt(w * 0.38)
                    labels(3).Left = CInt(w * 0.62)
                    labels(4).Left = w - labels(4).Width - 24
                End Sub
        End If

        AddHandler hdr.Paint,
            Sub(s, e)
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 20, hdr.Height - 1, hdr.Width - 20, hdr.Height - 1)
                End Using
            End Sub

        Return hdr
    End Function

    Private Shared Function MakeColLabel(text As String, loc As Point,
                                          Optional anchor As AnchorStyles = AnchorStyles.Top Or AnchorStyles.Left) As Label
        Return New Label With {
            .Text = text,
            .Font = New Font("Segoe UI", 10, FontStyle.Bold),
            .ForeColor = SubText,
            .AutoSize = True,
            .Location = loc,
            .BackColor = Color.Transparent,
            .Anchor = anchor
        }
    End Function

    ' =========================================================================
    '  KPI BAR
    ' =========================================================================
    Private Shared Function BuildKpiBar() As Panel
        Const SIDE_PAD As Integer = 24

        Dim net As Decimal = _totalIncome - _totalRefunds - _totalExpenses
        Dim netColor As Color
        Dim netSign As String
        Dim netSub As String

        If net > 0 Then
            netColor = MintDeep
            netSign = "+"
            netSub = "Profitable — earning more than spending"
        ElseIf net < 0 Then
            netColor = AccentRed
            netSign = "-"
            netSub = "At a loss — spending more than earning"
        Else
            netColor = FaintText
            netSign = ""
            netSub = "Break-even — income equals expenses"
        End If

        Dim bar As New Panel With {
            .Height = 165,
            .BackColor = Color.Transparent,
            .Padding = New Padding(SIDE_PAD, 16, SIDE_PAD, 0)
        }

        Dim kpiRow As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .ColumnCount = 3,
            .RowCount = 1,
            .BackColor = Color.Transparent,
            .CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        }
        For i = 0 To 2
            kpiRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 33.33F))
        Next
        kpiRow.RowStyles.Add(New RowStyle(SizeType.Percent, 100))

        ' Refunds are shown on the income card rather than as a fourth tile, so
        ' the deduction behind Net Profit is visible without changing the layout.
        Dim incomeSub As String = If(_totalRefunds > 0,
            "Gross sales, less " & Peso & _totalRefunds.ToString("N2") & " refunded",
            "From completed sales")
        Dim c1 = MakeKpiCard("Total Income", Peso & (_totalIncome - _totalRefunds).ToString("N2"), MintPrimary, incomeSub)
        Dim c2 = MakeKpiCard("Total Expenses", Peso & _totalExpenses.ToString("N2"), AccentOrange, "From purchase orders (excl. cancelled)")
        Dim c3 = MakeKpiCard("Net Profit", netSign & Peso & Math.Abs(net).ToString("N2"), netColor, netSub)

        c1.Margin = New Padding(0, 0, 6, 0)
        c2.Margin = New Padding(6, 0, 6, 0)
        c3.Margin = New Padding(6, 0, 0, 0)

        kpiRow.Controls.Add(c1, 0, 0)
        kpiRow.Controls.Add(c2, 1, 0)
        kpiRow.Controls.Add(c3, 2, 0)

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
            .ForeColor = SubText, .AutoSize = True,
            .Location = New Point(22, 14), .BackColor = Color.Transparent
        }
        Dim lblVal As New Label With {
            .Text = value,
            .Font = New Font("Segoe UI", 26, FontStyle.Bold),
            .ForeColor = valueClr, .AutoSize = True,
            .Location = New Point(20, 36), .BackColor = Color.Transparent
        }
        Dim lblSub As New Label With {
            .Text = subLabel,
            .Font = New Font("Segoe UI", 10),
            .ForeColor = SubText, .AutoSize = True,
            .Location = New Point(22, 100), .BackColor = Color.Transparent
        }
        card.Controls.AddRange({lblCap, lblVal, lblSub})
        Return card
    End Function

    ' =========================================================================
    '  FIX 7: PAGER BAR
    ' =========================================================================
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
            .BackColor = Color.Transparent,
            .Height = 44,
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Bottom
        }

        AddHandler wrapper.Resize,
            Sub()
                bar.Left = 0
                bar.Width = wrapper.Width
                bar.Top = 0
                bar.Height = wrapper.Height
            End Sub

        _btnPrev = New Button With {
            .Text = "‹",
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
            .Text = "›",
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
                    PopulateRows()
                End If
            End Sub
        AddHandler _btnNext.Click,
            Sub()
                Dim total As Integer = If(_activeTab = "income", _incomeRows.Count, _expenseRows.Count)
                Dim totalPages As Integer = Math.Max(1, CInt(Math.Ceiling(total / PAGE_SIZE)))
                If _currentPage < totalPages - 1 Then
                    _currentPage += 1
                    PopulateRows()
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
        ' Re-layout so button positions recalculate around new label width
        If _lblPageInfo.Parent IsNot Nothing Then
            _lblPageInfo.Parent.PerformLayout()
        End If
    End Sub

    ' =========================================================================
    '  POPULATE ROWS — FIX 2, 5, 7
    ' =========================================================================
    Private Shared Sub PopulateRows()
        _rowsHost.Controls.Clear()

        ' FIX 5: remove previous Resize handler before adding new one
        If _reflowHandler IsNot Nothing Then
            RemoveHandler _rowsHost.Resize, _reflowHandler
            _reflowHandler = Nothing
        End If

        ' FIX 2: show DB error visibly
        If _loadError <> "" Then
            Dim errPanel As New Panel With {
                .Height = 80, .Dock = DockStyle.Top,
                .BackColor = Color.FromArgb(255, 240, 240)
            }
            Dim errIcon As New Label With {
                .Text = "⚠",
                .Font = New Font("Segoe UI", 14),
                .ForeColor = AccentRed, .AutoSize = True,
                .Location = New Point(24, 22), .BackColor = Color.Transparent
            }
            Dim errLbl As New Label With {
                .Text = _loadError,
                .Font = New Font("Segoe UI", 10),
                .ForeColor = AccentRed, .AutoSize = True,
                .Location = New Point(50, 26), .BackColor = Color.Transparent
            }
            errPanel.Controls.AddRange({errIcon, errLbl})
            _rowsHost.Controls.Add(errPanel)
            UpdatePager(0)
            Return
        End If

        If _activeTab = "income" Then
            If _incomeRows.Count = 0 Then
                _rowsHost.Controls.Add(BuildEmptyRow("No income records found for this period."))
                UpdatePager(0)
            Else
                Dim pageRows = _incomeRows.Skip(_currentPage * PAGE_SIZE).Take(PAGE_SIZE).ToList()
                Dim y As Integer = 4
                For Each row In pageRows
                    Dim r As Panel = BuildIncomeRow(row, 64)
                    r.Location = New Point(0, y)
                    r.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
                    _rowsHost.Controls.Add(r)
                    r.Width = _rowsHost.ClientSize.Width - 4
                    y += 64
                Next
                _reflowHandler = Sub(s, e) ReflowRows(_rowsHost)
                AddHandler _rowsHost.Resize, _reflowHandler
                UpdatePager(_incomeRows.Count)
            End If
        Else
            If _expenseRows.Count = 0 Then
                _rowsHost.Controls.Add(BuildEmptyRow("No expense records found for this period."))
                UpdatePager(0)
            Else
                Dim pageRows = _expenseRows.Skip(_currentPage * PAGE_SIZE).Take(PAGE_SIZE).ToList()
                Dim y As Integer = 4
                For Each row In pageRows
                    Dim r As Panel = BuildExpenseRow(row, 64)
                    r.Location = New Point(0, y)
                    r.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
                    _rowsHost.Controls.Add(r)
                    r.Width = _rowsHost.ClientSize.Width - 4
                    y += 64
                Next
                _reflowHandler = Sub(s, e) ReflowRows(_rowsHost)
                AddHandler _rowsHost.Resize, _reflowHandler
                UpdatePager(_expenseRows.Count)
            End If
        End If
    End Sub

    ' =========================================================================
    '  INCOME ROW — FIX 1: row click uses AppModal
    ' =========================================================================
    Private Shared Function BuildIncomeRow(row As IncomeRow, height As Integer) As Panel
        Dim p As New BufferedPanel With {
            .Height = height, .BackColor = Color.Transparent, .Cursor = Cursors.Hand
        }
        AddHandler p.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                If TypeOf p.Tag Is Boolean AndAlso CBool(p.Tag) Then
                    Dim rect As New Rectangle(8, 3, p.Width - 16, p.Height - 6)
                    Using path As GraphicsPath = RoundRect(rect, 10)
                        Using b As New SolidBrush(RowHover)
                            e.Graphics.FillPath(b, path)
                        End Using
                    End Using
                End If
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 20, p.Height - 1, p.Width - 20, p.Height - 1)
                End Using
            End Sub

        Dim lblNo As New Label With {
    .Text = row.SaleNo, .Font = New Font("Segoe UI", 11, FontStyle.Bold),
    .ForeColor = MintDark, .AutoSize = True, .BackColor = Color.Transparent
}
        Dim lblDate As New Label With {
            .Text = row.SaleDate.ToString("MMM dd, yyyy"),
            .Font = New Font("Segoe UI", 11),
            .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblCashier As New Label With {
            .Text = row.Cashier, .Font = New Font("Segoe UI", 11),
            .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblMethod As New Label With {
            .Text = row.PayMethod, .Font = New Font("Segoe UI", 11),
            .ForeColor = FaintText, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblTotal As New Label With {
            .Text = Peso & row.Total.ToString("N2"),
            .Font = New Font("Segoe UI", 12, FontStyle.Bold),
            .ForeColor = MintPrimary, .AutoSize = True,
            .BackColor = Color.Transparent,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }

        p.Controls.AddRange({lblNo, lblDate, lblCashier, lblMethod, lblTotal})

        AddHandler p.Resize,
            Sub()
                Dim w = p.Width
                lblNo.Left = 24 : lblNo.Top = (height - lblNo.Height) \ 2
                lblDate.Left = CInt(w * 0.22) : lblDate.Top = (height - lblDate.Height) \ 2
                lblCashier.Left = CInt(w * 0.38) : lblCashier.Top = (height - lblCashier.Height) \ 2
                lblMethod.Left = CInt(w * 0.6) : lblMethod.Top = (height - lblMethod.Height) \ 2
                lblTotal.Left = w - lblTotal.Width - 24 : lblTotal.Top = (height - lblTotal.Height) \ 2
            End Sub

        ' FIX 1: click opens AppModal
        Dim openDetail As Action =
            Sub()
                Dim sb As New System.Text.StringBuilder()
                sb.AppendLine($"Sale No  : {row.SaleNo}")
                sb.AppendLine($"Date     : {row.SaleDate:MMM dd, yyyy  hh:mm tt}")
                sb.AppendLine($"Cashier  : {row.Cashier}")
                sb.AppendLine($"Method   : {row.PayMethod}")
                sb.AppendLine($"Total    : {Peso}{row.Total:N2}")
                AppModal.Show(sb.ToString(),
                              caption:=$"Sale Details — {row.SaleNo}",
                              buttons:=MessageBoxButtons.OK,
                              icon:=MessageBoxIcon.Information)
            End Sub

        AddHandler p.Click, Sub() openDetail()
        For Each ctrl As Control In p.Controls
            AddHandler ctrl.Click, Sub() openDetail()
        Next

        PropagateHover(p)
        Return p
    End Function

    ' =========================================================================
    '  EXPENSE ROW — FIX 1: row click uses AppModal
    ' =========================================================================
    Private Shared Function BuildExpenseRow(row As ExpenseRow, height As Integer) As Panel
        Dim p As New BufferedPanel With {
            .Height = height, .BackColor = Color.Transparent, .Cursor = Cursors.Hand
        }
        AddHandler p.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                If TypeOf p.Tag Is Boolean AndAlso CBool(p.Tag) Then
                    Dim rect As New Rectangle(8, 3, p.Width - 16, p.Height - 6)
                    Using path As GraphicsPath = RoundRect(rect, 10)
                        Using b As New SolidBrush(Color.FromArgb(255, 242, 225))
                            e.Graphics.FillPath(b, path)
                        End Using
                    End Using
                End If
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 20, p.Height - 1, p.Width - 20, p.Height - 1)
                End Using
            End Sub

        Dim lblPo As New Label With {
            .Text = row.PoNo, .Font = New Font("Segoe UI", 11, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblDate As New Label With {
            .Text = row.OrderDate.ToString("MMM dd, yyyy"),
            .Font = New Font("Segoe UI", 11),
            .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblSupplier As New Label With {
            .Text = row.Supplier, .Font = New Font("Segoe UI", 11),
            .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent
        }

        Dim badgeColors = GetPoBadge(row.Status)
        Dim badge As New Label With {
            .Text = row.Status,
            .Font = New Font("Segoe UI", 9, FontStyle.Bold),
            .ForeColor = badgeColors.fore,
            .BackColor = badgeColors.back,
            .AutoSize = True,
            .Padding = New Padding(10, 4, 10, 4)
        }
        RoundLabel(badge, 10)

        Dim lblAmount As New Label With {
            .Text = Peso & row.TotalCost.ToString("N2"),
            .Font = New Font("Segoe UI", 12, FontStyle.Bold),
            .ForeColor = AccentOrange, .AutoSize = True,
            .BackColor = Color.Transparent,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }

        p.Controls.AddRange({lblPo, lblDate, lblSupplier, badge, lblAmount})

        AddHandler p.Resize,
            Sub()
                Dim w = p.Width
                lblPo.Left = 24 : lblPo.Top = (height - lblPo.Height) \ 2
                lblDate.Left = CInt(w * 0.22) : lblDate.Top = (height - lblDate.Height) \ 2
                lblSupplier.Left = CInt(w * 0.38) : lblSupplier.Top = (height - lblSupplier.Height) \ 2
                badge.Left = CInt(w * 0.62) : badge.Top = (height - badge.Height) \ 2
                lblAmount.Left = w - lblAmount.Width - 24 : lblAmount.Top = (height - lblAmount.Height) \ 2
            End Sub

        ' FIX 1: click opens AppModal
        Dim openDetail As Action =
            Sub()
                Dim sb As New System.Text.StringBuilder()
                sb.AppendLine($"PO No    : {row.PoNo}")
                sb.AppendLine($"Date     : {row.OrderDate:MMM dd, yyyy}")
                sb.AppendLine($"Supplier : {row.Supplier}")
                sb.AppendLine($"Status   : {row.Status}")
                sb.AppendLine($"Amount   : {Peso}{row.TotalCost:N2}")
                AppModal.Show(sb.ToString(),
                              caption:=$"PO Details — {row.PoNo}",
                              buttons:=MessageBoxButtons.OK,
                              icon:=MessageBoxIcon.Information)
            End Sub

        AddHandler p.Click, Sub() openDetail()
        For Each ctrl As Control In p.Controls
            AddHandler ctrl.Click, Sub() openDetail()
        Next

        PropagateHover(p)
        Return p
    End Function

    ' =========================================================================
    '  SHARED HELPERS
    ' =========================================================================
    Private Shared Function GetPoBadge(status As String) As (fore As Color, back As Color)
        Select Case status
            Case "Draft" : Return (Color.FromArgb(120, 120, 120), Color.FromArgb(238, 238, 238))
            Case "Ordered" : Return (AccentOrange, Color.FromArgb(255, 240, 220))
            Case "Partially Received" : Return (Color.FromArgb(80, 160, 255), Color.FromArgb(220, 235, 255))
            Case "Received" : Return (MintDeep, Color.FromArgb(220, 248, 240))
            Case "Cancelled" : Return (AccentRed, Color.FromArgb(255, 230, 230))
            Case Else : Return (SubText, Color.FromArgb(238, 238, 238))
        End Select
    End Function

    Private Shared Function BuildEmptyRow(message As String) As Panel
        Dim p As New Panel With {.Height = 90, .Dock = DockStyle.Top, .BackColor = Color.Transparent}
        Dim lbl As New Label With {
            .Text = message, .Font = New Font("Segoe UI", 10.5F),
            .ForeColor = SubText, .AutoSize = True,
            .Location = New Point(20, 28), .BackColor = Color.Transparent
        }
        p.Controls.Add(lbl)
        Return p
    End Function

    Private Shared Sub ReflowRows(host As Panel)
        If host Is Nothing Then Return
        Dim w As Integer = host.ClientSize.Width - 4
        For Each c As Control In host.Controls
            c.Width = w
        Next
    End Sub

    Private Shared Function RoundRect(rect As Rectangle, radius As Integer) As GraphicsPath
        Dim path As New GraphicsPath()
        Dim r As Integer = radius
        path.AddArc(rect.X, rect.Y, r, r, 180, 90)
        path.AddArc(rect.Right - r, rect.Y, r, r, 270, 90)
        path.AddArc(rect.Right - r, rect.Bottom - r, r, r, 0, 90)
        path.AddArc(rect.X, rect.Bottom - r, r, r, 90, 90)
        path.CloseFigure()
        Return path
    End Function

    Private Shared Sub RoundLabel(lbl As Label, radius As Integer)
        AddHandler lbl.Resize,
            Sub()
                Dim path As New GraphicsPath()
                Dim r As Integer = radius
                path.AddArc(0, 0, r, r, 180, 90)
                path.AddArc(lbl.Width - r, 0, r, r, 270, 90)
                path.AddArc(lbl.Width - r, lbl.Height - r, r, r, 0, 90)
                path.AddArc(0, lbl.Height - r, r, r, 90, 90)
                path.CloseFigure()
                lbl.Region = New Region(path)
            End Sub
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