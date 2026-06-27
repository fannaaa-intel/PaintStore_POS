Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports MySql.Data.MySqlClient

' =============================================================================
'  AccountantIncomePanelBuilder  —  Screen 2: Income & Expenses
'
'  LAYOUT
'  ------
'    Top bar   (Dock = Top, 64px)  : dark gradient — title + date
'    Filter bar (Dock = Top, 48px) : date filter pills (left) + tab toggle (right)
'    Summary bar (Dock = Top, 72px): Total In | Total Out | Net — always visible
'    List area  (Dock = Fill)      : scrollable rows switching on tab
' =============================================================================
Public Class AccountantIncomePanelBuilder

    ' ── PALETTE ──────────────────────────────────────────────────────────────
    Private Shared ReadOnly MintDark As Color = Color.FromArgb(0, 80, 72)
    Private Shared ReadOnly MintPrimary As Color = Color.FromArgb(0, 200, 170)
    Private Shared ReadOnly MintDeep As Color = Color.FromArgb(0, 130, 109)
    Private Shared ReadOnly MintBg As Color = Color.FromArgb(244, 248, 246)
    Private Shared ReadOnly HeaderBg As Color = Color.FromArgb(0, 60, 54)
    Private Shared ReadOnly CardBg As Color = Color.White
    Private Shared ReadOnly SubText As Color = Color.FromArgb(90, 114, 107)
    Private Shared ReadOnly FaintText As Color = Color.FromArgb(150, 165, 160)
    Private Shared ReadOnly RowDivider As Color = Color.FromArgb(238, 242, 240)
    Private Shared ReadOnly RowHover As Color = Color.FromArgb(224, 242, 235)
    Private Shared ReadOnly AccentOrange As Color = Color.FromArgb(255, 160, 50)
    Private Shared ReadOnly AccentRed As Color = Color.FromArgb(220, 80, 80)
    Private Shared ReadOnly Peso As String = ChrW(8369)

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

    Private Shared _incomeRows As New List(Of IncomeRow)
    Private Shared _expenseRows As New List(Of ExpenseRow)
    Private Shared _totalIncome As Decimal
    Private Shared _totalExpenses As Decimal

    Private Shared _rowsHost As Panel
    Private Shared _lblTotalIn As Label
    Private Shared _lblTotalOut As Label
    Private Shared _lblNet As Label

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Shared Sub LoadIncome(container As Panel,
                                  Optional filter As String = "month",
                                  Optional tab As String = "income")
        _container = container
        _filter = filter
        _activeTab = tab
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
                    "WHERE s.status = 'Completed'" & dateWhere &
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

                ' ── Expense rows ──
                Using cmd As New MySqlCommand(
                    "SELECT po.po_no, po.order_date, s.name AS sname, " &
                    "po.status, po.total_cost " &
                    "FROM purchase_orders po " &
                    "JOIN suppliers s ON po.supplier_id = s.supplier_id " &
                    "WHERE 1=1" & poDateWhere &
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
                    "WHERE status='Completed'" & dateWhere, conn)
                    _totalIncome = Convert.ToDecimal(cmd.ExecuteScalar())
                End Using

                Using cmd As New MySqlCommand(
                    "SELECT COALESCE(SUM(total_cost),0) FROM purchase_orders " &
                    "WHERE 1=1" & poDateWhere, conn)
                    _totalExpenses = Convert.ToDecimal(cmd.ExecuteScalar())
                End Using

            End Using
        Catch ex As Exception
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

        ' Summary bar (bottom — add first so Fill doesn't eat it)
        Dim summaryBar As Panel = BuildSummaryBar()
        summaryBar.Dock = DockStyle.Bottom
        summaryBar.Height = 72

        ' List area
        Dim listArea As New BufferedPanel With {
            .Dock = DockStyle.Fill,
            .BackColor = CardBg,
            .Padding = New Padding(0)
        }
        AddHandler listArea.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)
        _rowsHost = New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = Color.Transparent,
            .AutoScroll = True,
            .Padding = New Padding(0, 4, 0, 8)
        }
        listArea.Controls.Add(_rowsHost)

        ' Column header strip
        Dim colHdr As Panel = BuildColHeader()
        colHdr.Dock = DockStyle.Top
        listArea.Controls.Add(colHdr)
        colHdr.BringToFront()

        ' Filter + tab bar
        Dim filterBar As Panel = BuildFilterBar()
        filterBar.Dock = DockStyle.Top
        filterBar.Height = 52

        ' Top dark bar
        Dim topBar As Panel = BuildTopBar()
        topBar.Dock = DockStyle.Top
        topBar.Height = 64

        _container.Controls.Add(summaryBar)
        _container.Controls.Add(listArea)
        _container.Controls.Add(filterBar)
        _container.Controls.Add(topBar)

        PopulateRows()
        _container.ResumeLayout()
    End Sub

    ' =========================================================================
    '  TOP BAR
    ' =========================================================================
    Private Shared Function BuildTopBar() As Panel
        Dim bar As New BufferedPanel With {.BackColor = MintDark}
        AddHandler bar.Paint,
            Sub(s, e)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Using br As New LinearGradientBrush(bar.ClientRectangle, MintDark, HeaderBg, 0.0F)
                    g.FillRectangle(br, bar.ClientRectangle)
                End Using
            End Sub

        Dim lblTitle As New Label With {
            .Text = "Income & Expenses",
            .Font = New Font("Segoe UI Semibold", 13, FontStyle.Bold),
            .ForeColor = Color.White, .BackColor = Color.Transparent,
            .AutoSize = True, .Location = New Point(22, 10)
        }
        Dim lblSub As New Label With {
            .Text = "Sales income vs purchase order expenses",
            .Font = New Font("Segoe UI", 9),
            .ForeColor = Color.FromArgb(160, 215, 200),
            .BackColor = Color.Transparent,
            .AutoSize = True, .Location = New Point(24, 36)
        }
        Dim lblDate As New Label With {
            .Text = DateTime.Now.ToString("MMMM dd, yyyy").ToUpper(),
            .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = Color.FromArgb(200, 240, 225),
            .BackColor = Color.Transparent,
            .AutoSize = True,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        bar.Controls.AddRange({lblTitle, lblSub, lblDate})
        AddHandler bar.Resize,
            Sub()
                lblDate.Left = bar.Width - lblDate.Width - 22
                lblDate.Top = (bar.Height - lblDate.Height) \ 2
            End Sub
        Return bar
    End Function

    ' =========================================================================
    '  FILTER BAR
    ' =========================================================================
    Private Shared Function BuildFilterBar() As Panel
        Dim bar As New Panel With {.BackColor = Color.FromArgb(250, 252, 251), .Padding = New Padding(18, 8, 18, 8)}

        ' Date filter pills — left side
        Dim filters() As String = {"Today", "This Week", "This Month", "All Time"}
        Dim keys() As String = {"today", "week", "month", "all"}
        Dim pillX As Integer = 18

        For i As Integer = 0 To filters.Length - 1
            Dim key As String = keys(i)
            Dim lbl As String = filters(i)
            Dim isActive As Boolean = (_filter = key)

            Dim pill As New Label With {
                .Text = lbl,
                .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold),
                .AutoSize = True,
                .Padding = New Padding(12, 4, 12, 4),
                .Cursor = Cursors.Hand,
                .Location = New Point(pillX, 10),
                .ForeColor = If(isActive, Color.White, SubText),
                .BackColor = If(isActive, MintPrimary, Color.FromArgb(230, 238, 235))
            }
            RoundLabel(pill, 12)

            Dim capKey As String = key
            AddHandler pill.Click, Sub() LoadIncome(_container, capKey, _activeTab)
            bar.Controls.Add(pill)
            pillX += pill.PreferredWidth + 28 + 8
        Next

        ' Tab toggle — right side
        Dim tabIncome As New Label With {
            .Text = "Income",
            .Font = New Font("Segoe UI", 10, If(_activeTab = "income", FontStyle.Bold, FontStyle.Regular)),
            .AutoSize = True,
            .Padding = New Padding(16, 4, 16, 4),
            .Cursor = Cursors.Hand,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right,
            .ForeColor = If(_activeTab = "income", Color.White, SubText),
            .BackColor = If(_activeTab = "income", MintPrimary, Color.FromArgb(230, 238, 235))
        }
        Dim tabExpenses As New Label With {
            .Text = "Expenses",
            .Font = New Font("Segoe UI", 10, If(_activeTab = "expenses", FontStyle.Bold, FontStyle.Regular)),
            .AutoSize = True,
            .Padding = New Padding(16, 4, 16, 4),
            .Cursor = Cursors.Hand,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right,
            .ForeColor = If(_activeTab = "expenses", Color.White, SubText),
            .BackColor = If(_activeTab = "expenses", AccentOrange, Color.FromArgb(230, 238, 235))
        }
        RoundLabel(tabIncome, 12)
        RoundLabel(tabExpenses, 12)

        AddHandler tabIncome.Click, Sub() LoadIncome(_container, _filter, "income")
        AddHandler tabExpenses.Click, Sub() LoadIncome(_container, _filter, "expenses")

        bar.Controls.AddRange({tabIncome, tabExpenses})

        AddHandler bar.Resize,
            Sub()
                tabExpenses.Left = bar.Width - tabExpenses.Width - 18
                tabExpenses.Top = 10
                tabIncome.Left = tabExpenses.Left - tabIncome.Width - 8
                tabIncome.Top = 10
            End Sub

        ' Bottom divider
        AddHandler bar.Paint,
            Sub(s, e)
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 0, bar.Height - 1, bar.Width, bar.Height - 1)
                End Using
            End Sub

        Return bar
    End Function

    ' =========================================================================
    '  COLUMN HEADER
    ' =========================================================================
    Private Shared Function BuildColHeader() As Panel
        Dim hdr As New Panel With {.Height = 34, .BackColor = Color.FromArgb(248, 250, 249)}

        Dim labels As Label()
        If _activeTab = "income" Then
            labels = {
                MakeColLabel("SALE NO", New Point(18, 9)),
                MakeColLabel("DATE", New Point(0, 9)),
                MakeColLabel("CASHIER", New Point(0, 9)),
                MakeColLabel("METHOD", New Point(0, 9)),
                MakeColLabel("TOTAL", New Point(0, 9), AnchorStyles.Top Or AnchorStyles.Right)
            }
            hdr.Controls.AddRange(labels)
            AddHandler hdr.Resize,
                Sub()
                    Dim w = hdr.Width
                    labels(0).Left = 18
                    labels(1).Left = CInt(w * 0.22)
                    labels(2).Left = CInt(w * 0.38)
                    labels(3).Left = CInt(w * 0.6)
                    labels(4).Left = w - labels(4).Width - 18
                End Sub
        Else
            labels = {
                MakeColLabel("PO NO", New Point(18, 9)),
                MakeColLabel("DATE", New Point(0, 9)),
                MakeColLabel("SUPPLIER", New Point(0, 9)),
                MakeColLabel("STATUS", New Point(0, 9)),
                MakeColLabel("AMOUNT", New Point(0, 9), AnchorStyles.Top Or AnchorStyles.Right)
            }
            hdr.Controls.AddRange(labels)
            AddHandler hdr.Resize,
                Sub()
                    Dim w = hdr.Width
                    labels(0).Left = 18
                    labels(1).Left = CInt(w * 0.22)
                    labels(2).Left = CInt(w * 0.38)
                    labels(3).Left = CInt(w * 0.62)
                    labels(4).Left = w - labels(4).Width - 18
                End Sub
        End If

        AddHandler hdr.Paint,
            Sub(s, e)
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 16, hdr.Height - 1, hdr.Width - 16, hdr.Height - 1)
                End Using
            End Sub

        Return hdr
    End Function

    Private Shared Function MakeColLabel(text As String, loc As Point,
                                          Optional anchor As AnchorStyles = AnchorStyles.Top Or AnchorStyles.Left) As Label
        Return New Label With {
            .Text = text,
            .Font = New Font("Segoe UI", 9, FontStyle.Bold),
            .ForeColor = SubText,
            .AutoSize = True,
            .Location = loc,
            .BackColor = Color.Transparent,
            .Anchor = anchor
        }
    End Function

    ' =========================================================================
    '  SUMMARY BAR
    ' =========================================================================
    Private Shared Function BuildSummaryBar() As Panel
        Dim bar As New BufferedPanel With {.BackColor = CardBg}
        AddHandler bar.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)

        Dim net As Decimal = _totalIncome - _totalExpenses
        Dim netColor As Color = If(net >= 0, MintDeep, AccentRed)

        ' Three stat blocks
        Dim lblInCap As New Label With {
            .Text = "Total Income", .Font = New Font("Segoe UI", 9),
            .ForeColor = FaintText, .AutoSize = True, .BackColor = Color.Transparent
        }
        _lblTotalIn = New Label With {
            .Text = Peso & _totalIncome.ToString("N2"),
            .Font = New Font("Segoe UI", 15, FontStyle.Bold),
            .ForeColor = MintPrimary, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblOutCap As New Label With {
            .Text = "Total Expenses", .Font = New Font("Segoe UI", 9),
            .ForeColor = FaintText, .AutoSize = True, .BackColor = Color.Transparent
        }
        _lblTotalOut = New Label With {
            .Text = Peso & _totalExpenses.ToString("N2"),
            .Font = New Font("Segoe UI", 15, FontStyle.Bold),
            .ForeColor = AccentOrange, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblNetCap As New Label With {
            .Text = "Net", .Font = New Font("Segoe UI", 9),
            .ForeColor = FaintText, .AutoSize = True, .BackColor = Color.Transparent
        }
        _lblNet = New Label With {
            .Text = Peso & net.ToString("N2"),
            .Font = New Font("Segoe UI", 15, FontStyle.Bold),
            .ForeColor = netColor, .AutoSize = True, .BackColor = Color.Transparent
        }

        bar.Controls.AddRange({lblInCap, _lblTotalIn, lblOutCap, _lblTotalOut, lblNetCap, _lblNet})

        AddHandler bar.Resize,
            Sub()
                Dim w = bar.Width
                Dim third = w \ 3

                lblInCap.Left = 28 : lblInCap.Top = 12
                _lblTotalIn.Left = 28 : _lblTotalIn.Top = 30

                lblOutCap.Left = third + 16 : lblOutCap.Top = 12
                _lblTotalOut.Left = third + 16 : _lblTotalOut.Top = 30

                lblNetCap.Left = (third * 2) + 16 : lblNetCap.Top = 12
                _lblNet.Left = (third * 2) + 16 : _lblNet.Top = 30

                ' Vertical dividers
                bar.Invalidate()
            End Sub

        AddHandler bar.Paint,
            Sub(s, e)
                Dim w = bar.Width
                Dim third = w \ 3
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, third, 12, third, bar.Height - 12)
                    e.Graphics.DrawLine(pen, third * 2, 12, third * 2, bar.Height - 12)
                End Using
            End Sub

        Return bar
    End Function

    ' =========================================================================
    '  POPULATE ROWS
    ' =========================================================================
    Private Shared Sub PopulateRows()
        _rowsHost.Controls.Clear()

        If _activeTab = "income" Then
            If _incomeRows.Count = 0 Then
                _rowsHost.Controls.Add(BuildEmptyRow("No income records found for this period."))
            Else
                Dim y As Integer = 4
                For Each row In _incomeRows
                    Dim r As Panel = BuildIncomeRow(row, 52)
                    r.Location = New Point(0, y)
                    r.Width = _rowsHost.ClientSize.Width - 4
                    r.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
                    _rowsHost.Controls.Add(r)
                    y += 52
                Next
                AddHandler _rowsHost.Resize, Sub() ReflowRows(_rowsHost)
            End If
        Else
            If _expenseRows.Count = 0 Then
                _rowsHost.Controls.Add(BuildEmptyRow("No expense records found for this period."))
            Else
                Dim y As Integer = 4
                For Each row In _expenseRows
                    Dim r As Panel = BuildExpenseRow(row, 52)
                    r.Location = New Point(0, y)
                    r.Width = _rowsHost.ClientSize.Width - 4
                    r.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
                    _rowsHost.Controls.Add(r)
                    y += 52
                Next
                AddHandler _rowsHost.Resize, Sub() ReflowRows(_rowsHost)
            End If
        End If
    End Sub

    ' =========================================================================
    '  INCOME ROW
    ' =========================================================================
    Private Shared Function BuildIncomeRow(row As IncomeRow, height As Integer) As Panel
        Dim p As New BufferedPanel With {.Height = height, .BackColor = Color.Transparent}
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
                Using br As New SolidBrush(MintPrimary)
                    e.Graphics.FillRectangle(br, New Rectangle(0, 10, 3, height - 20))
                End Using
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 16, p.Height - 1, p.Width - 16, p.Height - 1)
                End Using
            End Sub

        Dim lblNo As New Label With {
            .Text = row.SaleNo, .Font = New Font("Segoe UI", 10, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblDate As New Label With {
            .Text = row.SaleDate.ToString("MMM dd, yyyy"),
            .Font = New Font("Segoe UI", 10),
            .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblCashier As New Label With {
            .Text = row.Cashier, .Font = New Font("Segoe UI", 10),
            .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblMethod As New Label With {
            .Text = row.PayMethod, .Font = New Font("Segoe UI", 10),
            .ForeColor = FaintText, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblTotal As New Label With {
            .Text = Peso & row.Total.ToString("N2"),
            .Font = New Font("Segoe UI", 10.5F, FontStyle.Bold),
            .ForeColor = MintPrimary, .AutoSize = True,
            .BackColor = Color.Transparent,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }

        p.Controls.AddRange({lblNo, lblDate, lblCashier, lblMethod, lblTotal})

        AddHandler p.Resize,
            Sub()
                Dim w = p.Width
                lblNo.Left = 14 : lblNo.Top = (height - lblNo.Height) \ 2
                lblDate.Left = CInt(w * 0.22) : lblDate.Top = (height - lblDate.Height) \ 2
                lblCashier.Left = CInt(w * 0.38) : lblCashier.Top = (height - lblCashier.Height) \ 2
                lblMethod.Left = CInt(w * 0.6) : lblMethod.Top = (height - lblMethod.Height) \ 2
                lblTotal.Left = w - lblTotal.Width - 18 : lblTotal.Top = (height - lblTotal.Height) \ 2
            End Sub

        PropagateHover(p)
        Return p
    End Function

    ' =========================================================================
    '  EXPENSE ROW
    ' =========================================================================
    Private Shared Function BuildExpenseRow(row As ExpenseRow, height As Integer) As Panel
        Dim p As New BufferedPanel With {.Height = height, .BackColor = Color.Transparent}
        AddHandler p.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                If TypeOf p.Tag Is Boolean AndAlso CBool(p.Tag) Then
                    Dim rect As New Rectangle(8, 3, p.Width - 16, p.Height - 6)
                    Using path As GraphicsPath = RoundRect(rect, 10)
                        Using b As New SolidBrush(Color.FromArgb(255, 245, 230))
                            e.Graphics.FillPath(b, path)
                        End Using
                    End Using
                End If
                Using br As New SolidBrush(AccentOrange)
                    e.Graphics.FillRectangle(br, New Rectangle(0, 10, 3, height - 20))
                End Using
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 16, p.Height - 1, p.Width - 16, p.Height - 1)
                End Using
            End Sub

        Dim lblPo As New Label With {
            .Text = row.PoNo, .Font = New Font("Segoe UI", 10, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblDate As New Label With {
            .Text = row.OrderDate.ToString("MMM dd, yyyy"),
            .Font = New Font("Segoe UI", 10),
            .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblSupplier As New Label With {
            .Text = row.Supplier, .Font = New Font("Segoe UI", 10),
            .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent
        }

        Dim badgeColors = GetPoBadge(row.Status)
        Dim badge As New Label With {
            .Text = row.Status,
            .Font = New Font("Segoe UI", 8.25F, FontStyle.Bold),
            .ForeColor = badgeColors.fore,
            .BackColor = badgeColors.back,
            .AutoSize = True,
            .Padding = New Padding(8, 3, 8, 3)
        }
        RoundLabel(badge, 10)

        Dim lblAmount As New Label With {
            .Text = Peso & row.TotalCost.ToString("N2"),
            .Font = New Font("Segoe UI", 10.5F, FontStyle.Bold),
            .ForeColor = AccentOrange, .AutoSize = True,
            .BackColor = Color.Transparent,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }

        p.Controls.AddRange({lblPo, lblDate, lblSupplier, badge, lblAmount})

        AddHandler p.Resize,
            Sub()
                Dim w = p.Width
                lblPo.Left = 14 : lblPo.Top = (height - lblPo.Height) \ 2
                lblDate.Left = CInt(w * 0.22) : lblDate.Top = (height - lblDate.Height) \ 2
                lblSupplier.Left = CInt(w * 0.38) : lblSupplier.Top = (height - lblSupplier.Height) \ 2
                badge.Left = CInt(w * 0.62) : badge.Top = (height - badge.Height) \ 2
                lblAmount.Left = w - lblAmount.Width - 18 : lblAmount.Top = (height - lblAmount.Height) \ 2
            End Sub

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