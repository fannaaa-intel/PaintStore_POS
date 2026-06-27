Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports MySql.Data.MySqlClient

' =============================================================================
'  AccountantDashPanelBuilder  —  Screen 1: Accountant Dashboard
'
'  LAYOUT
'  ------
'    Top bar   (Dock = Top, 180px) : title + subtitle + date filter pills
'    KPI row   (Dock = Top, 120px) : 4 summary cards
'    Body      (Dock = Fill)       : 3-column grid
'                left  40% — Recent Sales list
'                center 20% — Top Payment Method + Total Transactions stacked
'                right 40% — Recent POs list
' =============================================================================
Public Class AccountantDashPanelBuilder

    ' ── PALETTE ──────────────────────────────────────────────────────────────
    Private Shared ReadOnly MintDark As Color = Color.FromArgb(0, 80, 72)
    Private Shared ReadOnly MintPrimary As Color = Color.FromArgb(0, 200, 170)
    Private Shared ReadOnly MintDeep As Color = Color.FromArgb(0, 130, 109)
    Private Shared ReadOnly MintBg As Color = Color.FromArgb(244, 248, 246)
    Private Shared ReadOnly CardBg As Color = Color.White
    Private Shared ReadOnly SubText As Color = Color.FromArgb(90, 114, 107)
    Private Shared ReadOnly FaintText As Color = Color.FromArgb(150, 165, 160)
    Private Shared ReadOnly RowDivider As Color = Color.FromArgb(238, 242, 240)
    Private Shared ReadOnly RowHover As Color = Color.FromArgb(224, 242, 235)
    Private Shared ReadOnly AccentOrange As Color = Color.FromArgb(255, 160, 50)
    Private Shared ReadOnly AccentBlue As Color = Color.FromArgb(80, 160, 255)
    Private Shared ReadOnly AccentRed As Color = Color.FromArgb(220, 80, 80)
    Private Shared ReadOnly Peso As String = ChrW(8369)

    ' ── DATA MODELS ──────────────────────────────────────────────────────────
    Private Class SaleRow
        Public SaleNo As String
        Public Cashier As String
        Public PayMethod As String
        Public Total As Decimal
        Public SaleDate As DateTime
    End Class

    Private Class PoRow
        Public PoNo As String
        Public Supplier As String
        Public TotalCost As Decimal
        Public Status As String
        Public OrderDate As DateTime
    End Class

    ' ── STATE ────────────────────────────────────────────────────────────────
    Private Shared _container As Panel
    Private Shared _filter As String = "month"

    Private Shared _revenue As Decimal
    Private Shared _expenses As Decimal
    Private Shared _returns As Decimal
    Private Shared _recentSales As New List(Of SaleRow)
    Private Shared _recentPos As New List(Of PoRow)
    Private Shared _topMethod As String
    Private Shared _txnCount As Integer

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Shared Sub LoadDash(container As Panel, Optional filter As String = "month")
        _container = container
        _filter = filter
        LoadData()
        Render()
    End Sub

    ' =========================================================================
    '  DATA
    ' =========================================================================
    Private Shared Sub LoadData()
        _revenue = 0 : _expenses = 0 : _returns = 0
        _recentSales.Clear() : _recentPos.Clear()
        _topMethod = "N/A" : _txnCount = 0

        Dim dateClause As String = BuildDateClause()
        Dim poDateClause As String = BuildDateClause("order_date")

        Try
            Using conn = Db.GetConnection()
                conn.Open()

                ' ── Revenue ──
                Using cmd As New MySqlCommand(
                    "SELECT COALESCE(SUM(total),0) FROM sales WHERE status='Completed'" &
                    If(dateClause <> "", " AND " & dateClause, ""), conn)
                    _revenue = Convert.ToDecimal(cmd.ExecuteScalar())
                End Using

                ' ── Expenses ──
                Using cmd As New MySqlCommand(
                    "SELECT COALESCE(SUM(total_cost),0) FROM purchase_orders WHERE 1=1" &
                    If(poDateClause <> "", " AND " & poDateClause, ""), conn)
                    _expenses = Convert.ToDecimal(cmd.ExecuteScalar())
                End Using

                ' ── Returns ──
                Using cmd As New MySqlCommand(
                    "SELECT COALESCE(SUM(total_refund),0) FROM returns WHERE 1=1" &
                    If(dateClause <> "", " AND return_datetime BETWEEN @d1 AND @d2", ""), conn)
                    If dateClause <> "" Then
                        AddDateParams(cmd)
                    End If
                    _returns = Convert.ToDecimal(cmd.ExecuteScalar())
                End Using

                ' ── Transaction count ──
                Using cmd As New MySqlCommand(
                    "SELECT COUNT(*) FROM sales WHERE status='Completed'" &
                    If(dateClause <> "", " AND " & dateClause, ""), conn)
                    _txnCount = Convert.ToInt32(cmd.ExecuteScalar())
                End Using

                ' ── Top payment method ──
                Using cmd As New MySqlCommand(
                    "SELECT pm.name, COUNT(*) AS cnt FROM sales s " &
                    "JOIN payment_methods pm ON s.method_id = pm.method_id " &
                    "WHERE s.status='Completed'" &
                    If(dateClause <> "", " AND " & dateClause, "") &
                    " GROUP BY pm.name ORDER BY cnt DESC LIMIT 1", conn)
                    Dim res = cmd.ExecuteScalar()
                    _topMethod = If(res IsNot Nothing AndAlso Not IsDBNull(res), res.ToString(), "N/A")
                End Using

                ' ── Recent Sales (last 5) ──
                Using cmd As New MySqlCommand(
                    "SELECT s.sale_no, u.full_name, pm.name AS method, s.total, s.sale_datetime " &
                    "FROM sales s " &
                    "JOIN users u ON s.user_id = u.user_id " &
                    "LEFT JOIN payment_methods pm ON s.method_id = pm.method_id " &
                    "ORDER BY s.sale_datetime DESC LIMIT 5", conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            _recentSales.Add(New SaleRow With {
                                .SaleNo = rdr("sale_no").ToString(),
                                .Cashier = rdr("full_name").ToString(),
                                .PayMethod = If(IsDBNull(rdr("method")), "—", rdr("method").ToString()),
                                .Total = If(IsDBNull(rdr("total")), 0D, Convert.ToDecimal(rdr("total"))),
                                .SaleDate = If(IsDBNull(rdr("sale_datetime")), DateTime.Now, Convert.ToDateTime(rdr("sale_datetime")))
                            })
                        End While
                    End Using
                End Using

                ' ── Recent POs (last 5) ──
                Using cmd As New MySqlCommand(
                    "SELECT po.po_no, s.name AS sname, po.total_cost, po.status, po.order_date " &
                    "FROM purchase_orders po " &
                    "JOIN suppliers s ON po.supplier_id = s.supplier_id " &
                    "ORDER BY po.order_date DESC LIMIT 5", conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            _recentPos.Add(New PoRow With {
                                .PoNo = rdr("po_no").ToString(),
                                .Supplier = rdr("sname").ToString(),
                                .TotalCost = If(IsDBNull(rdr("total_cost")), 0D, Convert.ToDecimal(rdr("total_cost"))),
                                .Status = rdr("status").ToString(),
                                .OrderDate = If(IsDBNull(rdr("order_date")), DateTime.Today, Convert.ToDateTime(rdr("order_date")))
                            })
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            Debug.WriteLine("AccountantDash LoadData error: " & ex.Message)
        End Try
    End Sub

    Private Shared Function BuildDateClause(Optional col As String = "sale_datetime") As String
        Select Case _filter
            Case "today" : Return col & " >= CURDATE()"
            Case "week" : Return col & " >= CURDATE() - INTERVAL 7 DAY"
            Case "month" : Return col & " >= CURDATE() - INTERVAL 30 DAY"
            Case Else : Return ""
        End Select
    End Function

    Private Shared Sub AddDateParams(cmd As MySqlCommand)
        Select Case _filter
            Case "today"
                cmd.Parameters.AddWithValue("@d1", DateTime.Today)
                cmd.Parameters.AddWithValue("@d2", DateTime.Today.AddDays(1))
            Case "week"
                cmd.Parameters.AddWithValue("@d1", DateTime.Today.AddDays(-7))
                cmd.Parameters.AddWithValue("@d2", DateTime.Now)
            Case "month"
                cmd.Parameters.AddWithValue("@d1", DateTime.Today.AddDays(-30))
                cmd.Parameters.AddWithValue("@d2", DateTime.Now)
            Case Else
                cmd.Parameters.AddWithValue("@d1", New DateTime(2000, 1, 1))
                cmd.Parameters.AddWithValue("@d2", DateTime.Now)
        End Select
    End Sub

    ' =========================================================================
    '  RENDER
    ' =========================================================================
    Private Shared Sub Render()
        _container.SuspendLayout()
        _container.Controls.Clear()
        _container.BackColor = MintBg

        ' ── Body (Fill first so Dock stacks correctly) ──
        Dim body As Panel = BuildBody()
        body.Dock = DockStyle.Fill

        ' ── KPI row ──
        Dim kpiRow As Panel = BuildKpiRow()
        kpiRow.Dock = DockStyle.Top
        kpiRow.Height = 130

        ' ── Top bar ──
        Dim topBar As Panel = BuildTopBar()
        topBar.Dock = DockStyle.Top
        topBar.Height = 100

        _container.Controls.Add(body)
        _container.Controls.Add(kpiRow)
        _container.Controls.Add(topBar)
        _container.ResumeLayout()
    End Sub

    ' =========================================================================
    '  TOP BAR
    ' =========================================================================
    Private Shared Function BuildTopBar() As Panel
        Dim bar As New Panel With {.BackColor = MintBg, .Padding = New Padding(28, 18, 28, 0)}

        Dim lblTitle As New Label With {
            .Text = "Dashboard",
            .Font = New Font("Segoe UI", 22, FontStyle.Bold),
            .ForeColor = MintDark,
            .AutoSize = True,
            .Location = New Point(28, 16)
        }

        ' ── Date filter pills ──
        Dim filters() As String = {"Today", "This Week", "This Month", "All Time"}
        Dim keys() As String = {"today", "week", "month", "all"}
        Dim pillX As Integer = 28
        Dim pillY As Integer = 62

        For i As Integer = 0 To filters.Length - 1
            Dim key As String = keys(i)
            Dim lbl As String = filters(i)
            Dim isActive As Boolean = (_filter = key)

            Dim pill As New Label With {
                .Text = lbl,
                .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold),
                .AutoSize = True,
                .Padding = New Padding(14, 5, 14, 5),
                .Cursor = Cursors.Hand,
                .Location = New Point(pillX, pillY),
                .ForeColor = If(isActive, Color.White, SubText),
                .BackColor = If(isActive, MintPrimary, Color.FromArgb(230, 238, 235))
            }
            RoundLabel(pill, 12)

            Dim capKey As String = key
            AddHandler pill.Click,
                Sub()
                    LoadDash(_container, capKey)
                End Sub

            bar.Controls.Add(pill)
            pillX += pill.PreferredWidth + 32 + 8
        Next

        bar.Controls.Add(lblTitle)
        Return bar
    End Function

    ' =========================================================================
    '  KPI ROW
    ' =========================================================================
    Private Shared Function BuildKpiRow() As Panel
        Dim row As New Panel With {.BackColor = MintBg, .Padding = New Padding(24, 8, 24, 8)}

        Dim grid As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .ColumnCount = 4, .RowCount = 1,
            .BackColor = Color.Transparent,
            .CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        }
        For i = 0 To 3
            grid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 25.0F))
        Next
        grid.RowStyles.Add(New RowStyle(SizeType.Percent, 100))

        Dim netProfit As Decimal = _revenue - _expenses
        Dim netColor As Color = If(netProfit >= 0, AccentBlue, AccentRed)

        Dim c1 As Panel = MakeKpiCard("Total Revenue", Peso & _revenue.ToString("N2"), MintPrimary, "From completed sales")
        Dim c2 As Panel = MakeKpiCard("Total Expenses", Peso & _expenses.ToString("N2"), AccentOrange, "From purchase orders")
        Dim c3 As Panel = MakeKpiCard("Net Profit", Peso & netProfit.ToString("N2"), netColor, "Revenue minus expenses")
        Dim c4 As Panel = MakeKpiCard("Total Returns", Peso & _returns.ToString("N2"), AccentRed, "Refunded to customers")

        c1.Margin = New Padding(0, 0, 6, 0)
        c2.Margin = New Padding(6, 0, 6, 0)
        c3.Margin = New Padding(6, 0, 6, 0)
        c4.Margin = New Padding(6, 0, 0, 0)

        grid.Controls.Add(c1, 0, 0)
        grid.Controls.Add(c2, 1, 0)
        grid.Controls.Add(c3, 2, 0)
        grid.Controls.Add(c4, 3, 0)

        row.Controls.Add(grid)
        Return row
    End Function

    Private Shared Function MakeKpiCard(caption As String, value As String,
                                         valueClr As Color, subLabel As String) As Panel
        Dim card As New BufferedPanel With {.Dock = DockStyle.Fill, .BackColor = CardBg}
        AddHandler card.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)

        Dim lblCap As New Label With {
            .Text = caption, .Font = New Font("Segoe UI", 10),
            .ForeColor = SubText, .AutoSize = True,
            .Location = New Point(20, 14), .BackColor = Color.Transparent
        }
        Dim lblVal As New Label With {
            .Text = value, .Font = New Font("Segoe UI", 20, FontStyle.Bold),
            .ForeColor = valueClr, .AutoSize = True,
            .Location = New Point(18, 36), .BackColor = Color.Transparent
        }
        Dim lblSub As New Label With {
            .Text = subLabel, .Font = New Font("Segoe UI", 9),
            .ForeColor = FaintText, .AutoSize = True,
            .Location = New Point(20, 82), .BackColor = Color.Transparent
        }
        card.Controls.AddRange({lblCap, lblVal, lblSub})
        Return card
    End Function

    ' =========================================================================
    '  BODY — 3 columns
    ' =========================================================================
    Private Shared Function BuildBody() As Panel
        Dim body As New Panel With {.BackColor = MintBg, .Padding = New Padding(24, 4, 24, 20)}

        Dim grid As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .ColumnCount = 3, .RowCount = 1,
            .BackColor = Color.Transparent,
            .CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        }
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 40.0F))
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 20.0F))
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 40.0F))
        grid.RowStyles.Add(New RowStyle(SizeType.Percent, 100))

        Dim leftCard As Panel = BuildRecentSalesCard()
        leftCard.Margin = New Padding(0, 0, 6, 0)

        Dim centerCol As Panel = BuildCenterColumn()
        centerCol.Margin = New Padding(6, 0, 6, 0)

        Dim rightCard As Panel = BuildRecentPosCard()
        rightCard.Margin = New Padding(6, 0, 0, 0)

        grid.Controls.Add(leftCard, 0, 0)
        grid.Controls.Add(centerCol, 1, 0)
        grid.Controls.Add(rightCard, 2, 0)

        body.Controls.Add(grid)
        Return body
    End Function

    ' =========================================================================
    '  LEFT — RECENT SALES
    ' =========================================================================
    Private Shared Function BuildRecentSalesCard() As Panel
        Dim card As New BufferedPanel With {.Dock = DockStyle.Fill, .BackColor = CardBg}
        AddHandler card.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)

        ' Header
        Dim header As New Panel With {.Height = 56, .Dock = DockStyle.Top, .BackColor = Color.Transparent}
        Dim lblH As New Label With {
            .Text = "Recent Sales", .Font = New Font("Segoe UI", 13, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True,
            .Location = New Point(18, 16), .BackColor = Color.Transparent
        }
        Dim lblCount As New Label With {
            .Text = _recentSales.Count & " shown",
            .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = FaintText, .AutoSize = True,
            .BackColor = Color.Transparent,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        header.Controls.AddRange({lblH, lblCount})
        AddHandler header.Resize, Sub()
                                      lblCount.Left = header.Width - lblCount.Width - 18
                                      lblCount.Top = 20
                                  End Sub

        Dim divider As New Panel With {.Height = 1, .BackColor = RowDivider, .Dock = DockStyle.Bottom}
        header.Controls.Add(divider)
        card.Controls.Add(header)

        ' Column headers
        Dim colHdr As New Panel With {.Height = 32, .Dock = DockStyle.Top, .BackColor = Color.Transparent}
        Dim chNo As New Label With {.Text = "SALE NO", .Font = New Font("Segoe UI", 9, FontStyle.Bold), .ForeColor = SubText, .AutoSize = True, .Location = New Point(18, 8), .BackColor = Color.Transparent}
        Dim chCashier As New Label With {.Text = "CASHIER", .Font = New Font("Segoe UI", 9, FontStyle.Bold), .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent}
        Dim chTotal As New Label With {.Text = "TOTAL", .Font = New Font("Segoe UI", 9, FontStyle.Bold), .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent, .Anchor = AnchorStyles.Top Or AnchorStyles.Right}
        colHdr.Controls.AddRange({chNo, chCashier, chTotal})
        AddHandler colHdr.Resize, Sub()
                                      chCashier.Left = CInt(colHdr.Width * 0.42)
                                      chCashier.Top = 8
                                      chTotal.Left = colHdr.Width - chTotal.Width - 18
                                      chTotal.Top = 8
                                  End Sub
        AddHandler colHdr.Paint, Sub(s, e)
                                     Using pen As New Pen(RowDivider, 1)
                                         e.Graphics.DrawLine(pen, 16, colHdr.Height - 1, colHdr.Width - 16, colHdr.Height - 1)
                                     End Using
                                 End Sub
        card.Controls.Add(colHdr)
        colHdr.BringToFront()

        ' Rows
        Dim rowsHost As New Panel With {
            .Dock = DockStyle.Fill, .BackColor = Color.Transparent,
            .AutoScroll = True, .Padding = New Padding(0, 2, 0, 8)
        }
        card.Controls.Add(rowsHost)
        rowsHost.BringToFront()

        If _recentSales.Count = 0 Then
            rowsHost.Controls.Add(BuildEmptyRow("No sales found for this period."))
        Else
            Dim y As Integer = 4
            For Each sale In _recentSales
                Dim row As Panel = BuildSaleRow(sale, 52)
                row.Location = New Point(0, y)
                row.Width = rowsHost.ClientSize.Width - 4
                row.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
                rowsHost.Controls.Add(row)
                y += 52
            Next
            AddHandler rowsHost.Resize, Sub() ReflowRows(rowsHost)
        End If

        Return card
    End Function

    Private Shared Function BuildSaleRow(sale As SaleRow, height As Integer) As Panel
        Dim row As New BufferedPanel With {.Height = height, .BackColor = Color.Transparent}
        AddHandler row.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                If TypeOf row.Tag Is Boolean AndAlso CBool(row.Tag) Then
                    Dim rect As New Rectangle(8, 3, row.Width - 16, row.Height - 6)
                    Using path As GraphicsPath = RoundRect(rect, 10)
                        Using b As New SolidBrush(RowHover)
                            e.Graphics.FillPath(b, path)
                        End Using
                    End Using
                End If
                ' Left accent bar
                Using br As New SolidBrush(MintPrimary)
                    e.Graphics.FillRectangle(br, New Rectangle(0, 10, 3, height - 20))
                End Using
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 16, row.Height - 1, row.Width - 16, row.Height - 1)
                End Using
            End Sub

        Dim lblNo As New Label With {
            .Text = sale.SaleNo, .Font = New Font("Segoe UI", 10, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblCashier As New Label With {
            .Text = sale.Cashier, .Font = New Font("Segoe UI", 10),
            .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblTotal As New Label With {
            .Text = Peso & sale.Total.ToString("N2"),
            .Font = New Font("Segoe UI", 10.5F, FontStyle.Bold),
            .ForeColor = MintPrimary, .AutoSize = True,
            .BackColor = Color.Transparent,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }

        row.Controls.AddRange({lblNo, lblCashier, lblTotal})

        AddHandler row.Resize,
            Sub()
                Dim w As Integer = row.Width
                lblNo.Left = 14 : lblNo.Top = (height - lblNo.Height) \ 2
                lblCashier.Left = CInt(w * 0.42) : lblCashier.Top = (height - lblCashier.Height) \ 2
                lblTotal.Left = w - lblTotal.Width - 18 : lblTotal.Top = (height - lblTotal.Height) \ 2
            End Sub

        PropagateHover(row)
        Return row
    End Function

    ' =========================================================================
    '  CENTER COLUMN — two stacked stat cards
    ' =========================================================================
    Private Shared Function BuildCenterColumn() As Panel
        Dim col As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.Transparent}

        ' Card 1 — Top Payment Method
        Dim card1 As New BufferedPanel With {
            .Dock = DockStyle.Top,
            .Height = 140,
            .BackColor = CardBg,
            .Margin = New Padding(0, 0, 0, 10)
        }
        AddHandler card1.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)

        Dim lblCapMethod As New Label With {
            .Text = "Top Payment", .Font = New Font("Segoe UI", 10),
            .ForeColor = SubText, .AutoSize = True,
            .Location = New Point(16, 18), .BackColor = Color.Transparent
        }
        Dim lblMethod As New Label With {
            .Text = _topMethod, .Font = New Font("Segoe UI", 16, FontStyle.Bold),
            .ForeColor = MintDeep, .AutoSize = True,
            .Location = New Point(14, 44), .BackColor = Color.Transparent
        }
        Dim lblMethodSub As New Label With {
            .Text = "Most used method", .Font = New Font("Segoe UI", 9),
            .ForeColor = FaintText, .AutoSize = True,
            .Location = New Point(16, 90), .BackColor = Color.Transparent
        }
        card1.Controls.AddRange({lblCapMethod, lblMethod, lblMethodSub})

        ' Card 2 — Total Transactions
        Dim card2 As New BufferedPanel With {
            .Dock = DockStyle.Top,
            .Height = 140,
            .BackColor = CardBg
        }
        AddHandler card2.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)

        Dim lblCapTxn As New Label With {
            .Text = "Transactions", .Font = New Font("Segoe UI", 10),
            .ForeColor = SubText, .AutoSize = True,
            .Location = New Point(16, 18), .BackColor = Color.Transparent
        }
        Dim lblTxn As New Label With {
            .Text = _txnCount.ToString("N0"),
            .Font = New Font("Segoe UI", 28, FontStyle.Bold),
            .ForeColor = AccentBlue, .AutoSize = True,
            .Location = New Point(14, 40), .BackColor = Color.Transparent
        }
        Dim lblTxnSub As New Label With {
            .Text = "Completed sales", .Font = New Font("Segoe UI", 9),
            .ForeColor = FaintText, .AutoSize = True,
            .Location = New Point(16, 90), .BackColor = Color.Transparent
        }
        card2.Controls.AddRange({lblCapTxn, lblTxn, lblTxnSub})

        ' Spacer between cards
        Dim spacer As New Panel With {.Dock = DockStyle.Top, .Height = 12, .BackColor = Color.Transparent}

        col.Controls.Add(card2)
        col.Controls.Add(spacer)
        col.Controls.Add(card1)

        Return col
    End Function

    ' =========================================================================
    '  RIGHT — RECENT POs
    ' =========================================================================
    Private Shared Function BuildRecentPosCard() As Panel
        Dim card As New BufferedPanel With {.Dock = DockStyle.Fill, .BackColor = CardBg}
        AddHandler card.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)

        ' Header
        Dim header As New Panel With {.Height = 56, .Dock = DockStyle.Top, .BackColor = Color.Transparent}
        Dim lblH As New Label With {
            .Text = "Recent Purchase Orders",
            .Font = New Font("Segoe UI", 13, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True,
            .Location = New Point(18, 16), .BackColor = Color.Transparent
        }
        Dim lblCount As New Label With {
            .Text = _recentPos.Count & " shown",
            .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = FaintText, .AutoSize = True,
            .BackColor = Color.Transparent,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        header.Controls.AddRange({lblH, lblCount})
        AddHandler header.Resize, Sub()
                                      lblCount.Left = header.Width - lblCount.Width - 18
                                      lblCount.Top = 20
                                  End Sub
        Dim divider As New Panel With {.Height = 1, .BackColor = RowDivider, .Dock = DockStyle.Bottom}
        header.Controls.Add(divider)
        card.Controls.Add(header)

        ' Column headers
        Dim colHdr As New Panel With {.Height = 32, .Dock = DockStyle.Top, .BackColor = Color.Transparent}
        Dim chPo As New Label With {.Text = "PO NO", .Font = New Font("Segoe UI", 9, FontStyle.Bold), .ForeColor = SubText, .AutoSize = True, .Location = New Point(18, 8), .BackColor = Color.Transparent}
        Dim chSupplier As New Label With {.Text = "SUPPLIER", .Font = New Font("Segoe UI", 9, FontStyle.Bold), .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent}
        Dim chStatus As New Label With {.Text = "STATUS", .Font = New Font("Segoe UI", 9, FontStyle.Bold), .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent, .Anchor = AnchorStyles.Top Or AnchorStyles.Right}
        colHdr.Controls.AddRange({chPo, chSupplier, chStatus})
        AddHandler colHdr.Resize, Sub()
                                      chSupplier.Left = CInt(colHdr.Width * 0.38)
                                      chSupplier.Top = 8
                                      chStatus.Left = colHdr.Width - chStatus.Width - 18
                                      chStatus.Top = 8
                                  End Sub
        AddHandler colHdr.Paint, Sub(s, e)
                                     Using pen As New Pen(RowDivider, 1)
                                         e.Graphics.DrawLine(pen, 16, colHdr.Height - 1, colHdr.Width - 16, colHdr.Height - 1)
                                     End Using
                                 End Sub
        card.Controls.Add(colHdr)
        colHdr.BringToFront()

        ' Rows
        Dim rowsHost As New Panel With {
            .Dock = DockStyle.Fill, .BackColor = Color.Transparent,
            .AutoScroll = True, .Padding = New Padding(0, 2, 0, 8)
        }
        card.Controls.Add(rowsHost)
        rowsHost.BringToFront()

        If _recentPos.Count = 0 Then
            rowsHost.Controls.Add(BuildEmptyRow("No purchase orders found."))
        Else
            Dim y As Integer = 4
            For Each po In _recentPos
                Dim row As Panel = BuildPoRow(po, 52)
                row.Location = New Point(0, y)
                row.Width = rowsHost.ClientSize.Width - 4
                row.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
                rowsHost.Controls.Add(row)
                y += 52
            Next
            AddHandler rowsHost.Resize, Sub() ReflowRows(rowsHost)
        End If

        Return card
    End Function

    Private Shared Function BuildPoRow(po As PoRow, height As Integer) As Panel
        Dim row As New BufferedPanel With {.Height = height, .BackColor = Color.Transparent}
        AddHandler row.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                If TypeOf row.Tag Is Boolean AndAlso CBool(row.Tag) Then
                    Dim rect As New Rectangle(8, 3, row.Width - 16, row.Height - 6)
                    Using path As GraphicsPath = RoundRect(rect, 10)
                        Using b As New SolidBrush(RowHover)
                            e.Graphics.FillPath(b, path)
                        End Using
                    End Using
                End If
                ' Left accent bar — orange for POs
                Using br As New SolidBrush(AccentOrange)
                    e.Graphics.FillRectangle(br, New Rectangle(0, 10, 3, height - 20))
                End Using
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 16, row.Height - 1, row.Width - 16, row.Height - 1)
                End Using
            End Sub

        Dim lblPo As New Label With {
            .Text = po.PoNo, .Font = New Font("Segoe UI", 10, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblSupplier As New Label With {
            .Text = po.Supplier, .Font = New Font("Segoe UI", 10),
            .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent
        }

        Dim badgeInfo = GetPoBadge(po.Status)
        Dim badge As New Label With {
            .Text = po.Status,
            .Font = New Font("Segoe UI", 8.25F, FontStyle.Bold),
            .ForeColor = badgeInfo.fore,
            .BackColor = badgeInfo.back,
            .AutoSize = True,
            .Padding = New Padding(8, 3, 8, 3),
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        RoundLabel(badge, 10)

        row.Controls.AddRange({lblPo, lblSupplier, badge})

        AddHandler row.Resize,
            Sub()
                Dim w As Integer = row.Width
                lblPo.Left = 14 : lblPo.Top = (height - lblPo.Height) \ 2
                lblSupplier.Left = CInt(w * 0.38) : lblSupplier.Top = (height - lblSupplier.Height) \ 2
                badge.Left = w - badge.Width - 18 : badge.Top = (height - badge.Height) \ 2
            End Sub

        PropagateHover(row)
        Return row
    End Function

    Private Shared Function GetPoBadge(status As String) As (fore As Color, back As Color)
        Select Case status
            Case "Draft" : Return (Color.FromArgb(120, 120, 120), Color.FromArgb(238, 238, 238))
            Case "Ordered" : Return (AccentOrange, Color.FromArgb(255, 240, 220))
            Case "Partially Received" : Return (AccentBlue, Color.FromArgb(220, 235, 255))
            Case "Received" : Return (MintDeep, Color.FromArgb(220, 248, 240))
            Case "Cancelled" : Return (AccentRed, Color.FromArgb(255, 230, 230))
            Case Else : Return (SubText, Color.FromArgb(238, 238, 238))
        End Select
    End Function

    ' =========================================================================
    '  SHARED HELPERS
    ' =========================================================================
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
        Dim r As Integer = radius
        Dim path As New GraphicsPath()
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