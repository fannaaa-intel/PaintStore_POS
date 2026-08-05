Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports MySql.Data.MySqlClient

' =============================================================================
'  AdminFinancialReportsPanelBuilder  —  Admin ▸ Financial Reports (READ-ONLY)
'
'  A consolidated, high-level financial summary over a selectable date range.
'  The Accountant role keeps the detailed working panels (income statement,
'  ledger, payables); this screen is the roll-up, not a second ledger.
'
'  EXPORT reuses Modules/ReportPrinter.vb — the app's only export mechanism.
'  It is print-based: build a ReportPrinter.ReportDoc, then Preview() opens a
'  formal print-preview window with a Print button (PrintDialog -> PrintDocument).
'  There is no CSV writer or PDF library in this codebase; users produce a PDF
'  by choosing a "Print to PDF" printer in the standard dialog.
'
'  LAYOUT:
'    TopRegion (Dock = Top, 128px):
'      ├─ TitleBar  (72px) : title + "Read-only" pill + Export + Refresh
'      └─ FilterBar (56px) : From / To date range
'    Host (Dock = Fill, AutoScroll):
'      ├─ Summary cards (6, in two rows of three)
'      ├─ Sales by Payment Method
'      ├─ Sales by Day
'      └─ Purchasing Outflow
'
'  "To" is applied as an exclusive upper bound (AddDays(1)) so same-day rows
'  are never dropped — same convention as the other ADMIN panels.
' =============================================================================
Public Class AdminFinancialReportsPanelBuilder

    ' ── PALETTE ───────────────────────────────────────────────────────────────
    Private Shared ReadOnly MintDark As Color = Color.FromArgb(10, 79, 66)
    Private Shared ReadOnly MintPrimary As Color = Color.FromArgb(0, 168, 142)
    Private Shared ReadOnly MintDeep As Color = Color.FromArgb(0, 130, 109)
    Private Shared ReadOnly MintBg As Color = Color.FromArgb(244, 248, 246)
    Private Shared ReadOnly CardBg As Color = Color.White
    Private Shared ReadOnly SubText As Color = Color.FromArgb(90, 114, 107)
    Private Shared ReadOnly FaintText As Color = Color.FromArgb(150, 165, 160)
    Private Shared ReadOnly RowDivider As Color = Color.FromArgb(238, 242, 240)
    Private Shared ReadOnly AccentRed As Color = Color.FromArgb(210, 60, 60)
    Private Shared ReadOnly AccentAmber As Color = Color.FromArgb(210, 140, 40)
    Private Shared ReadOnly Peso As String = ChrW(8369)

    ' ── DATA MODEL ────────────────────────────────────────────────────────────
    Private Class MethodRow
        Public Method As String
        Public Count As Integer
        Public Total As Decimal
    End Class

    Private Class DayRow
        Public Day As DateTime
        Public Count As Integer
        Public Total As Decimal
    End Class

    ' ── STATE ─────────────────────────────────────────────────────────────────
    Private Const SIDE_PAD As Integer = 24
    Private Const BREAK_ROW_H As Integer = 46
    Private Const CARD_HDR_H As Integer = 56
    Private Const PAD_BOTTOM As Integer = 14
    Private Const DAY_CARD_MAX As Integer = 420

    Private Shared _container As Panel
    Private Shared _fromDate As DateTime
    Private Shared _toDate As DateTime

    Private Shared _gross, _vat, _discounts, _refunds As Decimal
    Private Shared _txCount As Integer
    Private Shared _poCount As Integer
    Private Shared _poCost As Decimal
    Private Shared _methods As New List(Of MethodRow)
    Private Shared _days As New List(Of DayRow)

    Private Shared ReadOnly Property NetSales As Decimal
        Get
            Return _gross - _refunds
        End Get
    End Property

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Shared Sub LoadFinancialReports(container As Panel)
        _container = container
        ' Default range: this calendar month to date.
        Dim today As DateTime = DateTime.Today
        _fromDate = New DateTime(today.Year, today.Month, 1)
        _toDate = today
        LoadAll()
        Render()
    End Sub

    ' Range change re-queries and re-renders. Unlike the paged panels there are
    ' no filter controls to lose focus from mid-edit — the date pickers are
    ' rebuilt with the current values.
    Private Shared Sub ReloadForRange()
        LoadAll()
        Render()
    End Sub

    ' =========================================================================
    '  DATA
    ' =========================================================================
    Private Shared Sub AddRangeParams(cmd As MySqlCommand)
        cmd.Parameters.AddWithValue("@fromD", _fromDate.Date)
        ' Exclusive upper bound so same-day rows are included.
        cmd.Parameters.AddWithValue("@toD", _toDate.Date.AddDays(1))
    End Sub

    Private Shared Sub LoadAll()
        _gross = 0D : _vat = 0D : _discounts = 0D : _refunds = 0D
        _txCount = 0 : _poCount = 0 : _poCost = 0D
        _methods.Clear()
        _days.Clear()

        Try
            Using conn = Db.GetConnection()
                conn.Open()

                ' ── Sales totals ──────────────────────────────────────────────
                Using cmd As New MySqlCommand(
                    "SELECT COUNT(*) AS n, " &
                    "COALESCE(SUM(total),0) AS gross, " &
                    "COALESCE(SUM(vat_amount),0) AS vat, " &
                    "COALESCE(SUM(discount_amount),0) AS disc " &
                    "FROM sales " &
                    "WHERE status = 'Completed' " &
                    "AND sale_datetime >= @fromD AND sale_datetime < @toD", conn)
                    AddRangeParams(cmd)
                    Using rdr = cmd.ExecuteReader()
                        If rdr.Read() Then
                            _txCount = SafeInt(rdr("n"))
                            _gross = SafeDec(rdr("gross"))
                            _vat = SafeDec(rdr("vat"))
                            _discounts = SafeDec(rdr("disc"))
                        End If
                    End Using
                End Using

                ' ── Refunds ───────────────────────────────────────────────────
                Using cmd As New MySqlCommand(
                    "SELECT COALESCE(SUM(total_refund),0) AS refunds FROM returns " &
                    "WHERE return_datetime >= @fromD AND return_datetime < @toD", conn)
                    AddRangeParams(cmd)
                    _refunds = SafeDec(cmd.ExecuteScalar())
                End Using

                ' ── Sales by payment method ───────────────────────────────────
                Using cmd As New MySqlCommand(
                    "SELECT COALESCE(pm.name,'Unspecified') AS mname, " &
                    "COUNT(*) AS n, COALESCE(SUM(s.total),0) AS t " &
                    "FROM sales s " &
                    "LEFT JOIN payment_methods pm ON pm.method_id = s.method_id " &
                    "WHERE s.status = 'Completed' " &
                    "AND s.sale_datetime >= @fromD AND s.sale_datetime < @toD " &
                    "GROUP BY COALESCE(pm.name,'Unspecified') " &
                    "ORDER BY t DESC", conn)
                    AddRangeParams(cmd)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            _methods.Add(New MethodRow With {
                                .Method = SafeStr(rdr("mname")),
                                .Count = SafeInt(rdr("n")),
                                .Total = SafeDec(rdr("t"))
                            })
                        End While
                    End Using
                End Using

                ' ── Sales by day ──────────────────────────────────────────────
                Using cmd As New MySqlCommand(
                    "SELECT DATE(sale_datetime) AS d, COUNT(*) AS n, " &
                    "COALESCE(SUM(total),0) AS t " &
                    "FROM sales " &
                    "WHERE status = 'Completed' " &
                    "AND sale_datetime >= @fromD AND sale_datetime < @toD " &
                    "GROUP BY DATE(sale_datetime) ORDER BY d DESC", conn)
                    AddRangeParams(cmd)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            _days.Add(New DayRow With {
                                .Day = SafeDate(rdr("d")),
                                .Count = SafeInt(rdr("n")),
                                .Total = SafeDec(rdr("t"))
                            })
                        End While
                    End Using
                End Using

                ' ── Purchasing outflow (ordered cost, not cash paid) ──────────
                Using cmd As New MySqlCommand(
                    "SELECT COUNT(*) AS n, COALESCE(SUM(total_cost),0) AS t " &
                    "FROM purchase_orders " &
                    "WHERE status <> 'Cancelled' " &
                    "AND order_date >= @fromD AND order_date < @toD", conn)
                    AddRangeParams(cmd)
                    Using rdr = cmd.ExecuteReader()
                        If rdr.Read() Then
                            _poCount = SafeInt(rdr("n"))
                            _poCost = SafeDec(rdr("t"))
                        End If
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not build the financial report: " & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
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

    Private Shared Function SafeDate(o As Object) As DateTime
        If o Is Nothing OrElse IsDBNull(o) Then Return DateTime.MinValue
        Return Convert.ToDateTime(o)
    End Function

    Private Shared Function Money(v As Decimal) As String
        Return Peso & v.ToString("N2")
    End Function

    Private Shared Function RangeLabel() As String
        Return _fromDate.ToString("MMM d, yyyy") & "  to  " & _toDate.ToString("MMM d, yyyy")
    End Function

    ' =========================================================================
    '  RENDER
    ' =========================================================================
    Private Shared Sub Render()
        _container.SuspendLayout()
        _container.Controls.Clear()
        _container.BackColor = MintBg

        Dim host As New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = MintBg,
            .AutoScroll = True,
            .Padding = New Padding(SIDE_PAD, 12, SIDE_PAD, SIDE_PAD)
        }

        ' Docked Top children stack in REVERSE add order — trailing spacer first.
        host.Controls.Add(Spacer())
        host.Controls.Add(BuildOutflowCard())
        host.Controls.Add(Spacer())
        host.Controls.Add(BuildByDayCard())
        host.Controls.Add(Spacer())
        host.Controls.Add(BuildByMethodCard())
        host.Controls.Add(Spacer())
        host.Controls.Add(BuildSummaryCards())

        Dim topRegion As Panel = BuildTopRegion()
        topRegion.Dock = DockStyle.Top

        _container.Controls.Add(host)
        _container.Controls.Add(topRegion)
        _container.ResumeLayout()
    End Sub

    Private Shared Function Spacer() As Panel
        Return New Panel With {.Dock = DockStyle.Top, .Height = 16, .BackColor = MintBg}
    End Function

    ' =========================================================================
    '  TOP REGION
    ' =========================================================================
    Private Shared Function BuildTopRegion() As Panel
        Dim region As New Panel With {.BackColor = MintBg, .AutoSize = False, .Height = 128}

        Dim titleBar As New Panel With {
            .Height = 72, .Dock = DockStyle.Top, .BackColor = Color.Transparent
        }
        titleBar.Controls.Add(New Label With {
            .Text = "Financial Reports",
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

        Dim btnExport As Button = MakePillButton("Export / Print", 130, MintPrimary, Color.White, True)
        AddHandler btnExport.Click, Sub() ExportReport()

        Dim btnRefresh As Button = MakePillButton("⟳  Refresh", 100,
                                                  Color.FromArgb(220, 235, 232), MintDark, False)
        AddHandler btnRefresh.Click, Sub() ReloadForRange()

        AddHandler titleBar.Resize,
            Sub()
                btnExport.Left = titleBar.Width - btnExport.Width - SIDE_PAD
                btnExport.Top = (titleBar.Height - btnExport.Height) \ 2
                btnRefresh.Left = btnExport.Left - btnRefresh.Width - 10
                btnRefresh.Top = (titleBar.Height - btnRefresh.Height) \ 2
                pill.Left = btnRefresh.Left - pill.Width - 10
                pill.Top = (titleBar.Height - pill.Height) \ 2
            End Sub

        titleBar.Controls.Add(pill)
        titleBar.Controls.Add(btnRefresh)
        titleBar.Controls.Add(btnExport)

        ' ── Date range ───────────────────────────────────────────────────────
        Dim filterBar As New Panel With {
            .Height = 56, .Dock = DockStyle.Top, .BackColor = Color.Transparent
        }

        Dim lblFrom As New Label With {
            .Text = "From", .Font = New Font("Segoe UI", 9.5F), .ForeColor = SubText,
            .AutoSize = True, .Location = New Point(SIDE_PAD, 19), .BackColor = Color.Transparent
        }
        Dim dtpFrom As New DateTimePicker With {
            .Format = DateTimePickerFormat.Short,
            .Font = New Font("Segoe UI", 9.5F),
            .Size = New Size(120, 26),
            .Location = New Point(SIDE_PAD + 40, 15),
            .Value = _fromDate
        }
        Dim lblTo As New Label With {
            .Text = "To", .Font = New Font("Segoe UI", 9.5F), .ForeColor = SubText,
            .AutoSize = True, .Location = New Point(dtpFrom.Right + 14, 19),
            .BackColor = Color.Transparent
        }
        Dim dtpTo As New DateTimePicker With {
            .Format = DateTimePickerFormat.Short,
            .Font = New Font("Segoe UI", 9.5F),
            .Size = New Size(120, 26),
            .Location = New Point(dtpFrom.Right + 44, 15),
            .Value = _toDate
        }
        Dim lblRange As New Label With {
            .Text = "Reporting period: " & RangeLabel(),
            .Font = New Font("Segoe UI", 9.5F, FontStyle.Italic),
            .ForeColor = FaintText,
            .AutoSize = True,
            .Location = New Point(dtpTo.Right + 20, 20),
            .BackColor = Color.Transparent
        }

        AddHandler dtpFrom.ValueChanged,
            Sub()
                _fromDate = dtpFrom.Value.Date
                If _fromDate > _toDate Then _toDate = _fromDate
                ReloadForRange()
            End Sub
        AddHandler dtpTo.ValueChanged,
            Sub()
                _toDate = dtpTo.Value.Date
                If _toDate < _fromDate Then _fromDate = _toDate
                ReloadForRange()
            End Sub

        filterBar.Controls.AddRange({lblFrom, dtpFrom, lblTo, dtpTo, lblRange})

        region.Controls.Add(filterBar)
        region.Controls.Add(titleBar)
        Return region
    End Function

    ' =========================================================================
    '  SUMMARY CARDS  —  six figures in two rows of three
    ' =========================================================================
    Private Shared Function BuildSummaryCards() As Panel
        Dim wrap As New Panel With {
            .Dock = DockStyle.Top, .Height = 232, .BackColor = MintBg
        }

        Dim grid As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .ColumnCount = 3,
            .RowCount = 2,
            .BackColor = Color.Transparent,
            .CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        }
        For i As Integer = 0 To 2
            grid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 33.33F))
        Next
        grid.RowStyles.Add(New RowStyle(SizeType.Percent, 50))
        grid.RowStyles.Add(New RowStyle(SizeType.Percent, 50))

        grid.Controls.Add(SummaryCard("Gross Sales", Money(_gross), MintPrimary,
                               "completed sales in range"), 0, 0)
        grid.Controls.Add(SummaryCard("Refunds", Money(_refunds), AccentRed,
                               "returns processed in range"), 1, 0)
        grid.Controls.Add(SummaryCard("Net Sales", Money(NetSales), MintDark,
                               "gross less refunds"), 2, 0)
        grid.Controls.Add(SummaryCard("Total VAT", Money(_vat), MintDeep,
                               "tax component of sales"), 0, 1)
        grid.Controls.Add(SummaryCard("Total Discounts", Money(_discounts), AccentAmber,
                               "given away in range"), 1, 1)
        grid.Controls.Add(SummaryCard("Transactions", _txCount.ToString("N0"), MintDeep,
                               "completed sale count"), 2, 1)

        wrap.Controls.Add(grid)
        Return wrap
    End Function

    Private Shared Function SummaryCard(caption As String, value As String,
                                 valueClr As Color, subLabel As String) As Panel
        Dim card As New BufferedPanel With {.Dock = DockStyle.Fill, .BackColor = CardBg}
        card.Margin = New Padding(6)
        AddHandler card.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)

        card.Controls.Add(New Label With {
            .Text = caption,
            .Font = New Font("Segoe UI", 10.5F),
            .ForeColor = SubText,
            .AutoSize = True,
            .Location = New Point(20, 14),
            .BackColor = Color.Transparent
        })
        card.Controls.Add(New Label With {
            .Text = value,
            .Font = New Font("Segoe UI", 20, FontStyle.Bold),
            .ForeColor = valueClr,
            .AutoSize = True,
            .Location = New Point(18, 36),
            .BackColor = Color.Transparent
        })
        card.Controls.Add(New Label With {
            .Text = subLabel,
            .Font = New Font("Segoe UI", 9),
            .ForeColor = FaintText,
            .AutoSize = True,
            .Location = New Point(20, 74),
            .BackColor = Color.Transparent
        })
        Return card
    End Function

    ' =========================================================================
    '  BREAKDOWN CARDS
    ' =========================================================================
    Private Shared Function BuildByMethodCard() As Panel
        Dim rows As Integer = Math.Max(1, _methods.Count)
        Dim cardH As Integer = CARD_HDR_H + 4 + rows * BREAK_ROW_H + PAD_BOTTOM + 2

        Dim wrap As New Panel With {.Dock = DockStyle.Top, .Height = cardH, .BackColor = MintBg}
        Dim card As New BufferedPanel With {.Dock = DockStyle.Fill, .BackColor = CardBg}
        AddHandler card.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)

        Dim header As Panel = CardHeader("Sales by Payment Method",
                                         "completed sales, grouped by method")
        Dim bottomPad As New Panel With {
            .Dock = DockStyle.Bottom, .Height = PAD_BOTTOM, .BackColor = Color.Transparent
        }
        Dim list As New Panel With {
            .Dock = DockStyle.Fill, .BackColor = Color.Transparent, .AutoScroll = True
        }

        If _methods.Count = 0 Then
            list.Controls.Add(EmptyNote("No completed sales in this period."))
        Else
            Dim y As Integer = 4
            For Each m In _methods
                Dim share As Decimal = If(_gross = 0D, 0D, m.Total / _gross * 100D)
                Dim row As Panel = BreakdownRow(m.Method,
                                                m.Count.ToString("N0") & If(m.Count = 1, " txn", " txns"),
                                                Money(m.Total),
                                                share)
                row.Location = New Point(12, y)
                row.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
                list.Controls.Add(row)
                row.Width = list.ClientSize.Width - 24
                y += BREAK_ROW_H
            Next
            AddHandler list.Resize,
                Sub()
                    For Each c As Control In list.Controls
                        c.Width = list.ClientSize.Width - 24
                    Next
                End Sub
        End If

        card.Controls.Add(bottomPad)
        card.Controls.Add(header)
        card.Controls.Add(list)
        header.BringToFront()
        list.BringToFront()
        list.AutoScrollMinSize = New Size(0, 1)

        wrap.Controls.Add(card)
        Return wrap
    End Function

    Private Shared Function BuildByDayCard() As Panel
        Dim rows As Integer = Math.Max(1, _days.Count)
        Dim cardH As Integer = CARD_HDR_H + 4 + rows * BREAK_ROW_H + PAD_BOTTOM + 2
        ' Long ranges would otherwise push the rest of the page off-screen.
        If cardH > DAY_CARD_MAX Then cardH = DAY_CARD_MAX

        Dim wrap As New Panel With {.Dock = DockStyle.Top, .Height = cardH, .BackColor = MintBg}
        Dim card As New BufferedPanel With {.Dock = DockStyle.Fill, .BackColor = CardBg}
        AddHandler card.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)

        Dim header As Panel = CardHeader("Sales by Day", "newest first")
        Dim bottomPad As New Panel With {
            .Dock = DockStyle.Bottom, .Height = PAD_BOTTOM, .BackColor = Color.Transparent
        }
        Dim list As New Panel With {
            .Dock = DockStyle.Fill, .BackColor = Color.Transparent, .AutoScroll = True
        }

        If _days.Count = 0 Then
            list.Controls.Add(EmptyNote("No completed sales in this period."))
        Else
            Dim peak As Decimal = 0D
            For Each d In _days
                If d.Total > peak Then peak = d.Total
            Next

            Dim y As Integer = 4
            For Each d In _days
                Dim share As Decimal = If(peak = 0D, 0D, d.Total / peak * 100D)
                Dim row As Panel = BreakdownRow(d.Day.ToString("ddd, MMM d, yyyy"),
                                                d.Count.ToString("N0") & If(d.Count = 1, " txn", " txns"),
                                                Money(d.Total),
                                                share)
                row.Location = New Point(12, y)
                row.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
                list.Controls.Add(row)
                row.Width = list.ClientSize.Width - 24
                y += BREAK_ROW_H
            Next
            AddHandler list.Resize,
                Sub()
                    For Each c As Control In list.Controls
                        c.Width = list.ClientSize.Width - 24
                    Next
                End Sub
        End If

        card.Controls.Add(bottomPad)
        card.Controls.Add(header)
        card.Controls.Add(list)
        header.BringToFront()
        list.BringToFront()
        list.AutoScrollMinSize = New Size(0, 1)

        wrap.Controls.Add(card)
        Return wrap
    End Function

    Private Shared Function BuildOutflowCard() As Panel
        Dim wrap As New Panel With {.Dock = DockStyle.Top, .Height = 168, .BackColor = MintBg}
        Dim card As New BufferedPanel With {.Dock = DockStyle.Fill, .BackColor = CardBg}
        AddHandler card.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)

        Dim header As Panel = CardHeader("Purchasing Outflow",
                                         "value of purchase orders placed in this period")
        header.Dock = DockStyle.Top
        card.Controls.Add(header)

        Dim lblValue As New Label With {
            .Text = Money(_poCost),
            .Font = New Font("Segoe UI", 22, FontStyle.Bold),
            .ForeColor = AccentAmber,
            .AutoSize = True,
            .Location = New Point(22, 74),
            .BackColor = Color.Transparent
        }
        Dim lblCount As New Label With {
            .Text = "across " & _poCount.ToString("N0") &
                    If(_poCount = 1, " purchase order", " purchase orders") &
                    "  (excludes Cancelled)",
            .Font = New Font("Segoe UI", 10),
            .ForeColor = SubText,
            .AutoSize = True,
            .Location = New Point(24, 116),
            .BackColor = Color.Transparent
        }

        ' This is a commitment figure, not cash movement — say so on screen so
        ' it is never read as an expense paid.
        Dim lblCaveat As New Label With {
            .Text = "Ordered cost, not cash paid — payment timing is not tracked here.",
            .Font = New Font("Segoe UI", 9, FontStyle.Italic),
            .ForeColor = FaintText,
            .AutoSize = True,
            .BackColor = Color.Transparent
        }
        card.Controls.AddRange({lblValue, lblCount, lblCaveat})

        AddHandler card.Resize,
            Sub()
                lblCaveat.Left = 24
                lblCaveat.Top = card.Height - lblCaveat.Height - 16
            End Sub

        wrap.Controls.Add(card)
        Return wrap
    End Function

    Private Shared Function CardHeader(title As String, subtitle As String) As Panel
        Dim header As New Panel With {
            .Dock = DockStyle.Top, .Height = CARD_HDR_H, .BackColor = Color.Transparent
        }
        header.Controls.Add(New Label With {
            .Text = title,
            .Font = New Font("Segoe UI Semibold", 13),
            .ForeColor = MintDark,
            .AutoSize = True,
            .Location = New Point(24, 12),
            .BackColor = Color.Transparent
        })
        header.Controls.Add(New Label With {
            .Text = subtitle,
            .Font = New Font("Segoe UI", 9),
            .ForeColor = FaintText,
            .AutoSize = True,
            .Location = New Point(24, 34),
            .BackColor = Color.Transparent
        })
        AddHandler header.Paint,
            Sub(s, e)
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 22, header.Height - 1, header.Width - 22, header.Height - 1)
                End Using
            End Sub
        Return header
    End Function

    ' A breakdown line: label, sub-figure, amount, and a proportional bar.
    Private Shared Function BreakdownRow(label As String, subFigure As String,
                                         amount As String, sharePct As Decimal) As Panel
        Dim p As New BufferedPanel With {
            .Height = BREAK_ROW_H, .BackColor = Color.Transparent
        }

        Dim lblName As New Label With {
            .Text = label,
            .Font = New Font("Segoe UI", 10.5F, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblSub As New Label With {
            .Text = subFigure,
            .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = FaintText, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblAmount As New Label With {
            .Text = amount,
            .Font = New Font("Segoe UI", 11.5F, FontStyle.Bold),
            .ForeColor = MintPrimary, .AutoSize = True, .BackColor = Color.Transparent
        }

        Dim pct As Single = CSng(Math.Max(0D, Math.Min(100D, sharePct)))
        AddHandler p.Paint,
            Sub(s, e)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                ' Proportional bar sitting under the label
                Dim barX As Integer = 10
                Dim barW As Integer = CInt((p.Width * 0.42) - barX)
                If barW > 20 Then
                    Dim track As New Rectangle(barX, BREAK_ROW_H - 14, barW, 5)
                    Using br As New SolidBrush(Color.FromArgb(232, 240, 237))
                        g.FillRoundedRectangle(br, track, 2)
                    End Using
                    Dim fillW As Integer = CInt(barW * pct / 100.0F)
                    If fillW > 3 Then
                        Dim fill As New Rectangle(barX, BREAK_ROW_H - 14, fillW, 5)
                        Using br As New SolidBrush(MintPrimary)
                            g.FillRoundedRectangle(br, fill, 2)
                        End Using
                    End If
                End If
                Using pen As New Pen(RowDivider, 1)
                    g.DrawLine(pen, 10, p.Height - 1, p.Width - 10, p.Height - 1)
                End Using
            End Sub

        p.Controls.AddRange({lblName, lblSub, lblAmount})

        AddHandler p.Resize,
            Sub()
                lblName.Left = 10
                lblName.Top = 6
                lblSub.Left = CInt(p.Width * 0.46)
                lblSub.Top = (BREAK_ROW_H - lblSub.Height) \ 2 - 3
                lblAmount.Left = p.Width - lblAmount.PreferredWidth - 14
                lblAmount.Top = (BREAK_ROW_H - lblAmount.Height) \ 2 - 3
            End Sub

        Return p
    End Function

    Private Shared Function EmptyNote(message As String) As Panel
        Dim p As New Panel With {
            .Height = 60, .Dock = DockStyle.Top, .BackColor = Color.Transparent
        }
        p.Controls.Add(New Label With {
            .Text = message,
            .Font = New Font("Segoe UI", 10.5F),
            .ForeColor = FaintText,
            .AutoSize = True,
            .Location = New Point(22, 18),
            .BackColor = Color.Transparent
        })
        Return p
    End Function

    ' =========================================================================
    '  EXPORT  —  reuses Modules/ReportPrinter.vb (the app's only export path)
    '
    '  Note: ReportPrinter.TwoColHeader hardcodes its first column caption to
    '  "ACCOUNT", which suits a trial balance but not these breakdowns, so the
    '  breakdown sections use Section + TwoCol rows and skip that header rather
    '  than changing a printer the Accountant panels depend on.
    ' =========================================================================
    Private Shared Sub ExportReport()
        Try
            Dim doc As New ReportPrinter.ReportDoc()
            doc.Title = "Financial Summary"
            doc.PeriodLabel = "Period"
            doc.PeriodValue = RangeLabel()

            ' ── Sales summary ────────────────────────────────────────────────
            doc.Rows.Add(ReportPrinter.Section("Sales Summary"))
            doc.Rows.Add(ReportPrinter.Data("Gross Sales", Money(_gross)))
            doc.Rows.Add(ReportPrinter.Data("Less: Refunds", "(" & Money(_refunds) & ")", True))
            doc.Rows.Add(ReportPrinter.Highlight("NET SALES", Money(NetSales), NetSales < 0D))
            doc.Rows.Add(ReportPrinter.Spacer())
            doc.Rows.Add(ReportPrinter.Data("Total VAT", Money(_vat)))
            doc.Rows.Add(ReportPrinter.Data("Total Discounts", Money(_discounts)))
            doc.Rows.Add(ReportPrinter.Data("Completed Transactions", _txCount.ToString("N0")))
            doc.Rows.Add(ReportPrinter.Spacer())

            ' ── By payment method ────────────────────────────────────────────
            doc.Rows.Add(ReportPrinter.Section("Sales by Payment Method"))
            If _methods.Count = 0 Then
                doc.Rows.Add(ReportPrinter.Data("No completed sales in this period", ""))
            Else
                Dim mTotal As Decimal = 0D
                Dim mCount As Integer = 0
                For Each m In _methods
                    doc.Rows.Add(ReportPrinter.TwoCol(m.Method, m.Count.ToString("N0"), Money(m.Total)))
                    mTotal += m.Total
                    mCount += m.Count
                Next
                doc.Rows.Add(ReportPrinter.TwoColTotal("Total", mCount.ToString("N0"), Money(mTotal)))
            End If
            doc.Rows.Add(ReportPrinter.Spacer())

            ' ── By day ───────────────────────────────────────────────────────
            doc.Rows.Add(ReportPrinter.Section("Sales by Day"))
            If _days.Count = 0 Then
                doc.Rows.Add(ReportPrinter.Data("No completed sales in this period", ""))
            Else
                Dim dTotal As Decimal = 0D
                Dim dCount As Integer = 0
                For Each d In _days
                    doc.Rows.Add(ReportPrinter.TwoCol(d.Day.ToString("MMM d, yyyy"),
                                                      d.Count.ToString("N0"), Money(d.Total)))
                    dTotal += d.Total
                    dCount += d.Count
                Next
                doc.Rows.Add(ReportPrinter.TwoColTotal("Total", dCount.ToString("N0"), Money(dTotal)))
            End If
            doc.Rows.Add(ReportPrinter.Spacer())

            ' ── Purchasing outflow ───────────────────────────────────────────
            doc.Rows.Add(ReportPrinter.Section("Purchasing Outflow"))
            doc.Rows.Add(ReportPrinter.Data("Purchase Orders Placed (excl. Cancelled)",
                                            _poCount.ToString("N0")))
            doc.Rows.Add(ReportPrinter.Data("Ordered Cost (not cash paid)", Money(_poCost)))

            ' Letterhead follows the configured store name rather than a
            ' hardcoded string.
            ReportPrinter.Preview(doc,
                                  companyName:=StoreSettings.StoreName,
                                  preparedBy:=If(String.IsNullOrWhiteSpace(Session.CurrentFullName),
                                                 "Administrator", Session.CurrentFullName))
        Catch ex As Exception
            AppModal.Show("Could not build the report for export: " & ex.Message,
                          "Export Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try

        ActivityLog.Record("REPORT_EXPORT", "report", Nothing,
                           "Financial report " & _fromDate.ToString("yyyy-MM-dd") &
                           " to " & _toDate.ToString("yyyy-MM-dd"))
    End Sub

    ' =========================================================================
    '  SHARED HELPERS
    ' =========================================================================
    Private Shared Function MakePillButton(text As String, width As Integer,
                                           backClr As Color, foreClr As Color,
                                           isPrimary As Boolean) As Button
        Dim b As New Button With {
            .Text = text,
            .Size = New Size(width, 34),
            .FlatStyle = FlatStyle.Flat,
            .Font = If(isPrimary, New Font("Segoe UI Semibold", 9.5F), New Font("Segoe UI", 9.5F)),
            .Cursor = Cursors.Hand,
            .BackColor = backClr,
            .ForeColor = foreClr,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        b.FlatAppearance.BorderSize = 0
        Dim normalClr As Color = backClr
        Dim hoverClr As Color = If(isPrimary, MintDark,
            Color.FromArgb(Math.Max(0, backClr.R - 12),
                           Math.Max(0, backClr.G - 12),
                           Math.Max(0, backClr.B - 12)))
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

End Class
