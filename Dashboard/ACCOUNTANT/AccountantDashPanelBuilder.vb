Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports MySql.Data.MySqlClient

Public Class AccountantDashPanelBuilder

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
    Private Shared ReadOnly AccentBlue As Color = Color.FromArgb(80, 160, 255)
    Private Shared ReadOnly AccentRed As Color = Theme.Danger
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

    Private Shared _revenue As Decimal
    Private Shared _expenses As Decimal
    ' Cost of goods actually SOLD in the period, from the cost snapshot on
    ' each sale line. This is what gross profit must be measured against --
    ' _expenses (purchase orders raised) is cash spent on stock, most of
    ' which is still sitting on the shelf.
    Private Shared _cogs As Decimal

    Private Shared _recentSales As New List(Of SaleRow)
    Private Shared _recentPos As New List(Of PoRow)
    Private Shared _topMethod As String
    Private Shared _txnCount As Integer
    Private Shared _vatCollected As Decimal
    Private Shared _inventoryValue As Decimal

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Shared Sub LoadDash(container As Panel)
        _container = container
        LoadData()
        Render()
    End Sub

    ' =========================================================================
    '  DATA
    ' =========================================================================
    Private Shared Sub LoadData()
        _revenue = 0 : _expenses = 0 : _cogs = 0
        _recentSales.Clear() : _recentPos.Clear()
        _topMethod = "N/A" : _txnCount = 0 : _vatCollected = 0 : _inventoryValue = 0

        Dim saleClause As String = BuildDateClause("s.sale_datetime")
        Dim poClause As String = BuildDateClause("po.order_date")


        Try
            Using conn = Db.GetConnection()
                conn.Open()

                ' Revenue
                Using cmd As New MySqlCommand(
                    "SELECT COALESCE(SUM(total),0) FROM sales s WHERE status IN ('Completed','Partially Refunded')" &
                    If(saleClause <> "", " AND " & saleClause, ""), conn)
                    _revenue = Convert.ToDecimal(cmd.ExecuteScalar())
                End Using
                ' Expenses
                Using cmd As New MySqlCommand(
                    "SELECT COALESCE(SUM(total_cost),0) FROM purchase_orders po " &
                    "WHERE po.status <> 'Cancelled'" &
                    If(poClause <> "", " AND " & poClause, ""), conn)
                    _expenses = Convert.ToDecimal(cmd.ExecuteScalar())
                End Using

                ' Cost of goods sold: the per-unit cost captured on each sale line at
                ' the moment it was sold, so re-pricing stock later cannot rewrite a
                ' period that has already been reported.
                Using cmd As New MySqlCommand(
                    "SELECT COALESCE(SUM(si.qty * si.unit_cost),0) " &
                    "FROM sale_items si JOIN sales s ON s.sale_id = si.sale_id " &
                    "WHERE s.status IN ('Completed','Partially Refunded')" &
                    If(saleClause <> "", " AND " & saleClause, ""), conn)
                    _cogs = Convert.ToDecimal(cmd.ExecuteScalar())
                End Using

                ' Inventory Value
                ' Valued at COST, not sell price: stock on the shelf is money spent,
                ' and valuing it at retail books profit that has not been earned yet.
                Using cmd As New MySqlCommand(
                    "SELECT COALESCE(SUM(stock_qty * COALESCE(NULLIF(cost_price,0), unit_price)),0) FROM products " &
                    "WHERE is_active = 1", conn)
                    _inventoryValue = Convert.ToDecimal(cmd.ExecuteScalar())
                End Using



                ' VAT Collected
                Using cmd As New MySqlCommand(
                    "SELECT COALESCE(SUM(vat_amount),0) FROM sales s WHERE status IN ('Completed','Partially Refunded')" &
                    If(saleClause <> "", " AND " & saleClause, ""), conn)
                    _vatCollected = Convert.ToDecimal(cmd.ExecuteScalar())
                End Using

                ' Transaction count
                Using cmd As New MySqlCommand(
                    "SELECT COUNT(*) FROM sales s WHERE status IN ('Completed','Partially Refunded')" &
                    If(saleClause <> "", " AND " & saleClause, ""), conn)
                    _txnCount = Convert.ToInt32(cmd.ExecuteScalar())
                End Using

                ' Top payment method
                Using cmd As New MySqlCommand(
                    "SELECT pm.name FROM sales s " &
                    "JOIN payment_methods pm ON s.method_id = pm.method_id " &
               "WHERE s.status IN ('Completed','Partially Refunded')" &
                    If(saleClause <> "", " AND " & saleClause, "") &
                    " GROUP BY pm.name ORDER BY COUNT(*) DESC LIMIT 1", conn)
                    Dim res = cmd.ExecuteScalar()
                    _topMethod = If(res IsNot Nothing AndAlso Not IsDBNull(res), res.ToString(), "N/A")
                End Using

                ' Recent Sales — filtered
                Using cmd As New MySqlCommand(
                    "SELECT s.sale_no, u.full_name, pm.name AS method, s.total, s.sale_datetime " &
                    "FROM sales s " &
                    "JOIN users u ON s.user_id = u.user_id " &
                    "LEFT JOIN payment_methods pm ON s.method_id = pm.method_id " &
                    "WHERE s.status IN ('Completed','Partially Refunded')" &
                    If(saleClause <> "", " AND " & saleClause, "") &
                    " ORDER BY s.sale_datetime DESC LIMIT 6", conn)
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

                ' Recent POs — filtered
                Using cmd As New MySqlCommand(
                    "SELECT po.po_no, s.name AS sname, po.total_cost, po.status, po.order_date " &
        "FROM purchase_orders po " &
                    "JOIN suppliers s ON po.supplier_id = s.supplier_id " &
                    "WHERE po.status <> 'Cancelled'" &
                    If(poClause <> "", " AND " & poClause, "") &
                    " ORDER BY po.order_date DESC LIMIT 6", conn)
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
            AppModal.Show("Dashboard data could not be loaded. Check your database connection and try again.", "Load Error")
        End Try
    End Sub

    ' The From / To period shared by the Accountant screens.
    Private Shared Function BuildDateClause(Optional col As String = "sale_datetime") As String
        Return AccountantPeriod.SqlRange(col)
    End Function

    ' =========================================================================
    '  RENDER
    ' =========================================================================
    Private Shared Sub Render()
        _container.SuspendLayout()
        _container.Controls.Clear()
        _container.BackColor = MintBg

        ' Body fills remaining space
        Dim body As Panel = BuildBody()
        body.Dock = DockStyle.Fill

        ' Top region: title + filter pills + KPI cards — all in one panel
        ' matching ProcurementDashPanelBuilder exactly: Height=256
        Dim topRegion As Panel = BuildTopRegion()
        topRegion.Dock = DockStyle.Top

        _container.Controls.Add(body)
        _container.Controls.Add(topRegion)
        _container.ResumeLayout()
    End Sub

    ' =========================================================================
    '  TOP REGION  (title + filter pills + 4 KPI cards)
    '  Uses Dock=Top stacking for each row — no absolute positioning, no width
    '  timing issues. The region auto-sizes to fit all content.
    ' =========================================================================
    Private Shared Function BuildTopRegion() As Panel
        Const SIDE_PAD As Integer = 24

        Dim region As New Panel With {
            .BackColor = MintBg,
            .Padding = New Padding(0),
            .AutoSize = False
        }

        ' ── Row 1: Title bar (Dock=Top, Height=72) ──────────────────────────
        Dim titleBar As New Panel With {
            .Height = 72,
            .Dock = DockStyle.Top,
            .BackColor = Color.Transparent
        }
        Dim lblTitle As New Label With {
            .Text = "Dashboard",
            .Font = New Font("Segoe UI", 28, FontStyle.Bold),
            .ForeColor = MintDark,
            .AutoSize = True,
            .Location = New Point(SIDE_PAD, 16)
        }

        titleBar.Controls.Add(lblTitle)

        ' ── Row 2: Filter pills bar (Dock=Top, Height=52) ───────────────────
        Dim pillBar As New Panel With {
            .Height = 52,
            .Dock = DockStyle.Top,
            .BackColor = Color.Transparent
        }

        ' From / To replaces the old Today / This Week / This Month / All Time.
        Dim period As Panel = AccountantPeriod.BuildFields(Sub() LoadDash(_container))
        period.Location = New Point(SIDE_PAD, 9)
        pillBar.Controls.Add(period)

        ' ── Row 3: KPI cards (Dock=Top, Height=165) ─────────────────────────
        ' Use Dock=Top + Padding for side gutters so width is always correct.
        Dim kpiBar As New Panel With {
            .Height = 165,
            .Dock = DockStyle.Top,
            .BackColor = Color.Transparent,
            .Padding = New Padding(SIDE_PAD, 8, SIDE_PAD, 0)
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

        ' Gross profit = revenue - cost of what was SOLD. The previous figure
        ' subtracted every purchase order raised in the period, so restocking
        ' showed up as a loss and a quiet month looked unusually profitable.
        Dim grossProfit As Decimal = _revenue - _cogs
        Dim marginPct As Decimal = If(_revenue > 0D, Math.Round(grossProfit * 100D / _revenue, 1), 0D)
        Dim netClr As Color
        Dim netSign As String
        Dim netSub As String

        If grossProfit > 0 Then
            netClr = MintDeep
            netSign = "+"
            netSub = marginPct.ToString("0.#") & "% margin on goods sold"
        ElseIf grossProfit < 0 Then
            netClr = AccentRed
            netSign = "-"
            netSub = "Selling below cost"
        Else
            netClr = FaintText
            netSign = ""
            netSub = "Break-even on goods sold"
        End If

        Dim c1 = MakeKpiCard("Total Revenue", Peso & _revenue.ToString("N2"), MintPrimary, "From completed sales")
        Dim c2 = MakeKpiCard("Cost of Goods Sold", Peso & _cogs.ToString("N2"), AccentOrange, "Cost of what was sold")
        Dim c3 = MakeKpiCard("Gross Profit", netSign & Peso & Math.Abs(grossProfit).ToString("N2"), netClr, netSub)
        Dim c4 = MakeKpiCard("Inventory Value", Peso & _inventoryValue.ToString("N2"), AccentBlue, "Current stock at cost")

        c1.Margin = New Padding(0, 0, 6, 0)
        c2.Margin = New Padding(6, 0, 6, 0)
        c3.Margin = New Padding(6, 0, 6, 0)
        c4.Margin = New Padding(6, 0, 0, 0)

        kpiRow.Controls.Add(c1, 0, 0)
        kpiRow.Controls.Add(c2, 1, 0)
        kpiRow.Controls.Add(c3, 2, 0)
        kpiRow.Controls.Add(c4, 3, 0)

        kpiBar.Controls.Add(kpiRow)

        ' In WinForms, Dock=Top controls stack so that the LAST added control
        ' appears at the TOP. So we add in reverse visual order:
        ' kpiBar first (bottom), pillBar second (middle), titleBar last (top).
        region.Controls.Add(kpiBar)
        region.Controls.Add(pillBar)
        region.Controls.Add(titleBar)

        ' Total region height = titleBar(72) + pillBar(52) + kpiBar(165) = 289
        region.Height = 72 + 52 + 165

        Return region
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
    '  BODY — 3 equal columns
    ' =========================================================================
    Private Shared Function BuildBody() As Panel
        Dim body As New Panel With {.BackColor = MintBg, .Padding = New Padding(24, 16, 24, 20)}

        Dim grid As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .ColumnCount = 3, .RowCount = 1,
            .BackColor = Color.Transparent,
            .CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        }
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 33.33F))
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 33.33F))
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 33.34F))
        grid.RowStyles.Add(New RowStyle(SizeType.Percent, 100))

        Dim leftCard As Panel = BuildRecentSalesCard()
        leftCard.Margin = New Padding(0, 0, 8, 0)

        Dim centerCard As Panel = BuildRecentPosCard()
        centerCard.Margin = New Padding(8, 0, 8, 0)

        Dim rightCard As Panel = BuildStatsCard()
        rightCard.Margin = New Padding(8, 0, 0, 0)

        grid.Controls.Add(leftCard, 0, 0)
        grid.Controls.Add(centerCard, 1, 0)
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

        Dim header As New Panel With {.Height = 56, .Dock = DockStyle.Top, .BackColor = Color.Transparent}
        Dim lblH As New Label With {
            .Text = "Recent Sales",
            .Font = New Font("Segoe UI", 15, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True,
            .Location = New Point(18, 16), .BackColor = Color.Transparent
        }
        Dim lblCount As New Label With {
            .Text = _recentSales.Count & " shown",
            .Font = New Font("Segoe UI", 10.5F),
            .ForeColor = FaintText, .AutoSize = True,
            .BackColor = Color.Transparent,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        header.Controls.AddRange({lblH, lblCount})
        AddHandler header.Resize, Sub()
                                      lblCount.Left = header.Width - lblCount.Width - 18
                                      lblCount.Top = 20
                                  End Sub
        AddHandler header.Paint,
            Sub(s, e)
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 16, header.Height - 1, header.Width - 16, header.Height - 1)
                End Using
            End Sub
        card.Controls.Add(header)

        Dim colHdr As New Panel With {.Height = 34, .Dock = DockStyle.Top, .BackColor = Color.Transparent}
        Dim chNo As New Label With {.Text = "RECEIPT #", .Font = New Font("Segoe UI", 10.0F, FontStyle.Bold), .ForeColor = SubText, .AutoSize = True, .Location = New Point(18, 9), .BackColor = Color.Transparent}
        Dim chMethod As New Label With {.Text = "METHOD", .Font = New Font("Segoe UI", 10.0F, FontStyle.Bold), .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent}
        Dim chTotal As New Label With {.Text = "TOTAL", .Font = New Font("Segoe UI", 10.0F, FontStyle.Bold), .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent, .Anchor = AnchorStyles.Top Or AnchorStyles.Right}
        colHdr.Controls.AddRange({chNo, chMethod, chTotal})
        AddHandler colHdr.Resize,
            Sub()
                chMethod.Left = CInt(colHdr.Width * 0.48)
                chMethod.Top = 9
                chTotal.Left = colHdr.Width - chTotal.Width - 18
                chTotal.Top = 9
            End Sub
        AddHandler colHdr.Paint,
            Sub(s, e)
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 16, colHdr.Height - 1, colHdr.Width - 16, colHdr.Height - 1)
                End Using
            End Sub
        card.Controls.Add(colHdr)
        colHdr.BringToFront()

        Dim rowsHost As New Panel With {
            .Dock = DockStyle.Fill, .BackColor = Color.Transparent,
            .AutoScroll = True, .Padding = New Padding(0, 2, 0, 8)
        }
        card.Controls.Add(rowsHost)
        rowsHost.BringToFront()

        If _recentSales.Count = 0 Then
            rowsHost.Controls.Add(BuildEmptyRow("No sales for this period."))
        Else
            Dim y As Integer = 4
            For Each sale In _recentSales
                Dim row As Panel = BuildSaleRow(sale, 58)
                row.Location = New Point(0, y)
                row.Width = rowsHost.ClientSize.Width - 4
                row.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
                rowsHost.Controls.Add(row)
                y += 58
            Next
            Dim onSalesResize As EventHandler = Sub(s, e) ReflowRows(rowsHost)
            AddHandler rowsHost.Resize, onSalesResize
            AddHandler rowsHost.VisibleChanged,
                Sub()
                    If rowsHost.Visible Then ReflowRows(rowsHost)
                End Sub
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
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 16, row.Height - 1, row.Width - 16, row.Height - 1)
                End Using
            End Sub

        Dim lblNo As New Label With {
            .Text = sale.SaleNo,
            .Font = New Font("Segoe UI", 13, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblDate As New Label With {
            .Text = sale.SaleDate.ToString("MMM dd  hh:mm tt"),
            .Font = New Font("Segoe UI", 10.5F),
            .ForeColor = FaintText, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim methodBadge As New Label With {
            .Text = sale.PayMethod,
            .Font = New Font("Segoe UI", 10.0F, FontStyle.Bold),
            .ForeColor = MintDeep,
            .BackColor = Color.FromArgb(220, 248, 242),
            .AutoSize = True,
            .Padding = New Padding(7, 3, 7, 3)
        }
        RoundLabel(methodBadge, 8)

        Dim lblTotal As New Label With {
            .Text = Peso & sale.Total.ToString("N2"),
            .Font = New Font("Segoe UI", 13, FontStyle.Bold),
            .ForeColor = MintPrimary, .AutoSize = True,
            .BackColor = Color.Transparent,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }

        row.Controls.AddRange({lblNo, lblDate, methodBadge, lblTotal})

        AddHandler row.Resize,
            Sub()
                Dim w As Integer = row.Width
                lblNo.Left = 14 : lblNo.Top = 10
                lblDate.Left = 14 : lblDate.Top = 32
                methodBadge.Left = CInt(w * 0.48) : methodBadge.Top = (height - methodBadge.Height) \ 2
                lblTotal.Left = w - lblTotal.Width - 16 : lblTotal.Top = (height - lblTotal.Height) \ 2
            End Sub

        PropagateHover(row)
        Return row
    End Function

    ' =========================================================================
    '  CENTER — RECENT POs
    ' =========================================================================
    Private Shared Function BuildRecentPosCard() As Panel
        Dim card As New BufferedPanel With {.Dock = DockStyle.Fill, .BackColor = CardBg}
        AddHandler card.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)

        Dim header As New Panel With {.Height = 56, .Dock = DockStyle.Top, .BackColor = Color.Transparent}
        Dim lblH As New Label With {
            .Text = "Recent Purchase Orders",
            .Font = New Font("Segoe UI", 15, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True,
            .Location = New Point(18, 16), .BackColor = Color.Transparent
        }
        Dim lblCount As New Label With {
            .Text = _recentPos.Count & " shown",
            .Font = New Font("Segoe UI", 10.5F),
            .ForeColor = FaintText, .AutoSize = True,
            .BackColor = Color.Transparent,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        header.Controls.AddRange({lblH, lblCount})
        AddHandler header.Resize, Sub()
                                      lblCount.Left = header.Width - lblCount.Width - 18
                                      lblCount.Top = 20
                                  End Sub
        AddHandler header.Paint,
            Sub(s, e)
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 16, header.Height - 1, header.Width - 16, header.Height - 1)
                End Using
            End Sub
        card.Controls.Add(header)

        Dim colHdr As New Panel With {.Height = 34, .Dock = DockStyle.Top, .BackColor = Color.Transparent}
        Dim chPo As New Label With {.Text = "PO NO.", .Font = New Font("Segoe UI", 10.0F, FontStyle.Bold), .ForeColor = SubText, .AutoSize = True, .Location = New Point(18, 9), .BackColor = Color.Transparent}
        Dim chSup As New Label With {.Text = "SUPPLIER", .Font = New Font("Segoe UI", 10.0F, FontStyle.Bold), .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent}
        Dim chStat As New Label With {.Text = "STATUS", .Font = New Font("Segoe UI", 10.0F, FontStyle.Bold), .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent, .Anchor = AnchorStyles.Top Or AnchorStyles.Right}
        colHdr.Controls.AddRange({chPo, chSup, chStat})
        AddHandler colHdr.Resize,
            Sub()
                chSup.Left = CInt(colHdr.Width * 0.42)
                chSup.Top = 9
                chStat.Left = colHdr.Width - chStat.Width - 18
                chStat.Top = 9
            End Sub
        AddHandler colHdr.Paint,
            Sub(s, e)
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 16, colHdr.Height - 1, colHdr.Width - 16, colHdr.Height - 1)
                End Using
            End Sub
        card.Controls.Add(colHdr)
        colHdr.BringToFront()

        Dim rowsHost As New Panel With {
            .Dock = DockStyle.Fill, .BackColor = Color.Transparent,
            .AutoScroll = True, .Padding = New Padding(0, 2, 0, 8)
        }
        card.Controls.Add(rowsHost)
        rowsHost.BringToFront()

        If _recentPos.Count = 0 Then
            rowsHost.Controls.Add(BuildEmptyRow("No purchase orders for this period."))
        Else
            Dim y As Integer = 4
            For Each po In _recentPos
                Dim row As Panel = BuildPoRow(po, 58)
                row.Location = New Point(0, y)
                row.Width = rowsHost.ClientSize.Width - 4
                row.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
                rowsHost.Controls.Add(row)
                y += 58
            Next
            Dim onPosResize As EventHandler = Sub(s, e) ReflowRows(rowsHost)
            AddHandler rowsHost.Resize, onPosResize
            AddHandler rowsHost.VisibleChanged,
                Sub()
                    If rowsHost.Visible Then ReflowRows(rowsHost)
                End Sub
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
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 16, row.Height - 1, row.Width - 16, row.Height - 1)
                End Using
            End Sub

        Dim lblPo As New Label With {
            .Text = po.PoNo,
            .Font = New Font("Segoe UI", 13, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblDate As New Label With {
            .Text = po.OrderDate.ToString("MMM dd, yyyy"),
            .Font = New Font("Segoe UI", 10.5F),
            .ForeColor = FaintText, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblSupplier As New Label With {
            .Text = po.Supplier,
            .Font = New Font("Segoe UI", 12),
            .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent
        }

        Dim badgeInfo = GetPoBadge(po.Status)
        Dim badge As New Label With {
            .Text = po.Status,
            .Font = New Font("Segoe UI", 10.0F, FontStyle.Bold),
            .ForeColor = badgeInfo.fore,
            .BackColor = badgeInfo.back,
            .AutoSize = True,
            .Padding = New Padding(8, 3, 8, 3),
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        RoundLabel(badge, 8)

        row.Controls.AddRange({lblPo, lblDate, lblSupplier, badge})

        AddHandler row.Resize,
            Sub()
                Dim w As Integer = row.Width
                lblPo.Left = 14 : lblPo.Top = 10
                lblDate.Left = 14 : lblDate.Top = 32
                lblSupplier.Left = CInt(w * 0.42) : lblSupplier.Top = (height - lblSupplier.Height) \ 2
                badge.Left = w - badge.Width - 14 : badge.Top = (height - badge.Height) \ 2
            End Sub

        PropagateHover(row)
        Return row
    End Function

    Private Shared Function GetPoBadge(status As String) As (fore As Color, back As Color)
        Select Case status
            Case "Draft" : Return (Color.FromArgb(100, 100, 100), Color.FromArgb(238, 238, 238))
            Case "Ordered" : Return (AccentOrange, Color.FromArgb(252, 240, 228))
            Case "Partially Received" : Return (AccentBlue, Color.FromArgb(220, 235, 255))
            Case "Received" : Return (MintDeep, Color.FromArgb(220, 248, 240))
            Case "Cancelled" : Return (AccentRed, Color.FromArgb(251, 234, 234))
            Case Else : Return (SubText, Color.FromArgb(238, 238, 238))
        End Select
    End Function

    ' =========================================================================
    '  RIGHT — PERIOD SUMMARY
    ' =========================================================================
    Private Shared Function BuildStatsCard() As Panel
        Dim card As New BufferedPanel With {.Dock = DockStyle.Fill, .BackColor = CardBg}
        AddHandler card.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)

        ' Header
        Dim header As New Panel With {.Height = 56, .Dock = DockStyle.Top, .BackColor = Color.Transparent}
        Dim lblH As New Label With {
            .Text = "Period Summary",
            .Font = New Font("Segoe UI", 13, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True,
            .Location = New Point(18, 16), .BackColor = Color.Transparent
        }
        AddHandler header.Paint,
            Sub(s, e)
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 16, header.Height - 1, header.Width - 16, header.Height - 1)
                End Using
            End Sub
        header.Controls.Add(lblH)
        card.Controls.Add(header)

        ' Section 1 — Top Payment Method (fixed 88px)
        Dim sec1 As New Panel With {.Height = 88, .Dock = DockStyle.Top, .BackColor = Color.Transparent}
        Dim lblCapMethod As New Label With {
            .Text = "TOP PAYMENT METHOD",
            .Font = New Font("Segoe UI", 9, FontStyle.Bold),
            .ForeColor = FaintText, .AutoSize = True,
            .Location = New Point(18, 12), .BackColor = Color.Transparent
        }
        Dim lblMethod As New Label With {
            .Text = _topMethod,
            .Font = New Font("Segoe UI", 22, FontStyle.Bold),
            .ForeColor = MintDeep, .AutoSize = True,
            .Location = New Point(18, 34), .BackColor = Color.Transparent
        }
        AddHandler sec1.Paint,
            Sub(s, e)
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 16, sec1.Height - 1, sec1.Width - 16, sec1.Height - 1)
                End Using
            End Sub
        sec1.Controls.AddRange({lblCapMethod, lblMethod})
        card.Controls.Add(sec1)
        sec1.BringToFront()

        ' Section 2 — Transactions with functioning arc (fixed 100px)
        ' Arc sweeps 0–260° proportional to txn count; caps at 50 txns = full arc
        Dim sec2 As New BufferedPanel With {.Height = 100, .Dock = DockStyle.Top, .BackColor = Color.Transparent}
        Dim lblCapTxn As New Label With {
            .Text = "TRANSACTIONS",
            .Font = New Font("Segoe UI", 9, FontStyle.Bold),
            .ForeColor = FaintText, .AutoSize = True,
            .Location = New Point(18, 14), .BackColor = Color.Transparent
        }
        Dim lblTxnBig As New Label With {
            .Text = _txnCount.ToString("N0"),
            .Font = New Font("Segoe UI", 32, FontStyle.Bold),
            .ForeColor = MintPrimary, .AutoSize = True,
            .Location = New Point(18, 34), .BackColor = Color.Transparent
        }
        AddHandler sec2.Paint,
            Sub(s, e)
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 16, sec2.Height - 1, sec2.Width - 16, sec2.Height - 1)
                End Using
            End Sub
        sec2.Controls.AddRange({lblCapTxn, lblTxnBig})
        card.Controls.Add(sec2)
        sec2.BringToFront()

        ' Section 3 — Revenue vs Expenses bars (fixed 168px)
        Dim sec3 As New BufferedPanel With {.Height = 168, .Dock = DockStyle.Top, .BackColor = Color.Transparent}
        Dim lblCapBars As New Label With {
            .Text = "REVENUE VS EXPENSES",
            .Font = New Font("Segoe UI", 9, FontStyle.Bold),
            .ForeColor = FaintText, .AutoSize = True,
            .Location = New Point(18, 16), .BackColor = Color.Transparent
        }
        Dim lblRevLbl As New Label With {
            .Text = "Revenue",
            .Font = New Font("Segoe UI", 10.5F),
            .ForeColor = SubText, .AutoSize = True,
            .Location = New Point(18, 44), .BackColor = Color.Transparent
        }
        Dim lblRevVal As New Label With {
            .Text = Peso & _revenue.ToString("N2"),
            .Font = New Font("Segoe UI", 10.5F, FontStyle.Bold),
            .ForeColor = MintPrimary, .AutoSize = True,
            .BackColor = Color.Transparent,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        Dim lblExpLbl As New Label With {
            .Text = "Expenses",
            .Font = New Font("Segoe UI", 10.5F),
            .ForeColor = SubText, .AutoSize = True,
            .Location = New Point(18, 106), .BackColor = Color.Transparent
        }
        Dim lblExpVal As New Label With {
            .Text = Peso & _expenses.ToString("N2"),
            .Font = New Font("Segoe UI", 10.5F, FontStyle.Bold),
            .ForeColor = AccentOrange, .AutoSize = True,
            .BackColor = Color.Transparent,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }

        AddHandler sec3.Resize,
            Sub()
                lblRevVal.Left = sec3.Width - lblRevVal.Width - 18 : lblRevVal.Top = 44
                lblExpVal.Left = sec3.Width - lblExpVal.Width - 18 : lblExpVal.Top = 106
            End Sub

        AddHandler sec3.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim bw As Integer = sec3.Width - 36
                If bw < 4 Then Return
                Dim maxVal As Decimal = Math.Max(_revenue, Math.Max(_expenses, 1))
                ' Revenue bar
                Using br As New SolidBrush(Color.FromArgb(215, 240, 235))
                    e.Graphics.FillRectangle(br, New Rectangle(18, 68, bw, 8))
                End Using
                Dim revFill As Integer = CInt(bw * CSng(_revenue / maxVal))
                If revFill > 0 Then
                    Using br As New SolidBrush(MintPrimary)
                        e.Graphics.FillRectangle(br, New Rectangle(18, 68, revFill, 8))
                    End Using
                End If
                ' Expenses bar
                Using br As New SolidBrush(Color.FromArgb(252, 235, 215))
                    e.Graphics.FillRectangle(br, New Rectangle(18, 130, bw, 8))
                End Using
                Dim expFill As Integer = CInt(bw * CSng(_expenses / maxVal))
                If expFill > 0 Then
                    Using br As New SolidBrush(AccentOrange)
                        e.Graphics.FillRectangle(br, New Rectangle(18, 130, expFill, 8))
                    End Using
                End If
            End Sub

        sec3.Controls.AddRange({lblCapBars, lblRevLbl, lblRevVal, lblExpLbl, lblExpVal})
        card.Controls.Add(sec3)
        sec3.BringToFront()

        ' Section 4 — Net Profit / Returns / VAT summary (fills remaining space)
        ' Styled to match sec3: plain colored value labels, no pill backgrounds
        Dim sec4 As New BufferedPanel With {.Dock = DockStyle.Fill, .BackColor = Color.Transparent}

        Dim lblCapSummary As New Label With {
            .Text = "NET SUMMARY",
            .Font = New Font("Segoe UI", 9, FontStyle.Bold),
            .ForeColor = FaintText, .AutoSize = True,
            .Location = New Point(18, 16), .BackColor = Color.Transparent
        }

        Dim netProfit As Decimal = _revenue - _expenses
        Dim netClr As Color
        Dim netSign As String

        If netProfit > 0 Then
            netClr = MintDeep
            netSign = "+"
        ElseIf netProfit < 0 Then
            netClr = AccentRed
            netSign = "-"
        Else
            netClr = FaintText
            netSign = ""
        End If



        Dim lblNetLbl As New Label With {
            .Text = "Net Profit",
            .Font = New Font("Segoe UI", 10.5F),
            .ForeColor = SubText, .AutoSize = True,
            .Location = New Point(18, 44), .BackColor = Color.Transparent
        }
        Dim lblNetVal As New Label With {
          .Text = netSign & Peso & Math.Abs(netProfit).ToString("N2"),
            .Font = New Font("Segoe UI", 10.5F, FontStyle.Bold),
            .ForeColor = netClr, .AutoSize = True,
            .BackColor = Color.Transparent,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }

        ' Inventory Value row
        Dim lblRetLbl As New Label With {
            .Text = "Inventory Value",
            .Font = New Font("Segoe UI", 10.5F),
            .ForeColor = SubText, .AutoSize = True,
            .Location = New Point(18, 82), .BackColor = Color.Transparent
        }
        Dim lblRetVal As New Label With {
            .Text = Peso & _inventoryValue.ToString("N2"),
            .Font = New Font("Segoe UI", 10.5F, FontStyle.Bold),
            .ForeColor = AccentBlue, .AutoSize = True,
            .BackColor = Color.Transparent,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }

        ' VAT Collected row
        Dim lblVatLbl As New Label With {
            .Text = "VAT Collected",
            .Font = New Font("Segoe UI", 10.5F),
            .ForeColor = SubText, .AutoSize = True,
            .Location = New Point(18, 120), .BackColor = Color.Transparent
        }
        Dim lblVatVal As New Label With {
            .Text = Peso & _vatCollected.ToString("N2"),
            .Font = New Font("Segoe UI", 10.5F, FontStyle.Bold),
            .ForeColor = MintPrimary, .AutoSize = True,
            .BackColor = Color.Transparent,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }

        AddHandler sec4.Resize,
            Sub()
                lblNetVal.Left = sec4.Width - lblNetVal.Width - 18 : lblNetVal.Top = 44
                lblRetVal.Left = sec4.Width - lblRetVal.Width - 18 : lblRetVal.Top = 82
                lblVatVal.Left = sec4.Width - lblVatVal.Width - 18 : lblVatVal.Top = 120
            End Sub

        AddHandler sec4.Paint,
            Sub(s, e)
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 16, 0, sec4.Width - 16, 0)
                End Using
            End Sub

        sec4.Controls.AddRange({lblCapSummary, lblNetLbl, lblNetVal, lblRetLbl, lblRetVal, lblVatLbl, lblVatVal})
        card.Controls.Add(sec4)
        sec4.BringToFront()

        Return card
    End Function

    ' =========================================================================
    '  SHARED HELPERS
    ' =========================================================================
    Private Shared Function BuildEmptyRow(message As String) As Panel
        Dim p As New Panel With {.Height = 90, .Dock = DockStyle.Top, .BackColor = Color.Transparent}
        Dim lbl As New Label With {
            .Text = message,
            .Font = New Font("Segoe UI", 10.5F),
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