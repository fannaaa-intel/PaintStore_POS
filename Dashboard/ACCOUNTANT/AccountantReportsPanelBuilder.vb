Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports MySql.Data.MySqlClient

' =============================================================================
'  AccountantReportsPanelBuilder  —  Screen 5: Financial Reports
' =============================================================================
Public Class AccountantReportsPanelBuilder

    ' ── PALETTE ───────────────────────────────────────────────────────────────
    Private Shared ReadOnly MintDark As Color = Theme.Dark
    Private Shared ReadOnly MintPrimary As Color = Theme.Primary
    Private Shared ReadOnly MintDeep As Color = Theme.Deep
    Private Shared ReadOnly MintBg As Color = Theme.PageBg
    Private Shared ReadOnly CardBg As Color = Color.White
    Private Shared ReadOnly SubText As Color = Theme.SubText
    Private Shared ReadOnly FaintText As Color = Theme.FaintText
    Private Shared ReadOnly RowDivider As Color = Color.FromArgb(220, 230, 226)
    Private Shared ReadOnly CardBorder As Color = Color.FromArgb(180, 210, 200)
    Private Shared ReadOnly AccentRed As Color = Color.FromArgb(220, 80, 80)
    Private Shared ReadOnly HighlightBg As Color = Color.FromArgb(210, 245, 238)
    Private Shared ReadOnly SectionBg As Color = Color.FromArgb(240, 248, 245)
    Private Shared ReadOnly TotalBg As Color = Color.FromArgb(245, 250, 248)
    Private Shared ReadOnly Peso As String = ChrW(8369)

    Private Const TAB_INCOME As String = "income"
    Private Const TAB_TRIAL As String = "trial"
    Private Const TAB_BALANCE As String = "balance"

    ' ── STATE ─────────────────────────────────────────────────────────────────
    Private Shared _container As Panel
    Private Shared _activeTab As String = TAB_INCOME

    Private Shared _revenue As Decimal
    Private Shared _returns As Decimal
    Private Shared _poCost As Decimal
    Private Shared _cogs As Decimal
    Private Shared _unpaidPos As Decimal
    Private Shared _stockValue As Decimal
    Private Shared _trialRows As New List(Of (Account As String, Debit As Decimal, Credit As Decimal))

    Private Shared _contentHost As Panel

    ' The period is the one shared by every Accountant screen.
    Private Shared ReadOnly Property _dateFrom As DateTime
        Get
            Return AccountantPeriod.FromDate
        End Get
    End Property
    Private Shared ReadOnly Property _dateTo As DateTime
        Get
            Return AccountantPeriod.ToDate
        End Get
    End Property

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Shared Sub LoadReports(container As Panel,
                                  Optional tab As String = TAB_INCOME)
        _container = container
        _activeTab = tab
        LoadData()
        Render()
    End Sub

    ' =========================================================================
    '  DATA
    ' =========================================================================
    Private Shared Sub LoadData()
        _revenue = 0 : _returns = 0 : _poCost = 0
        _cogs = 0 : _unpaidPos = 0 : _stockValue = 0

        Dim fromVal As DateTime = _dateFrom.Date
        Dim toVal As DateTime = _dateTo.Date.AddDays(1).AddSeconds(-1)

        Try
            Using conn = Db.GetConnection()
                conn.Open()

                ' Revenue must exclude sales that never took money: this query
                ' had no status filter at all, so Voided sales and carts merely
                ' parked OnHold were being reported as income.
                ' 'Refunded' sales stay in GROSS revenue because Returns are
                ' deducted on their own line; dropping them too counted the
                ' same refund twice.
                Using cmd As New MySqlCommand(
                    "SELECT COALESCE(SUM(total),0) FROM sales " &
                    "WHERE status IN ('Completed','Partially Refunded','Refunded') " &
                    "AND sale_datetime BETWEEN @from AND @to", conn)
                    cmd.Parameters.AddWithValue("@from", fromVal)
                    cmd.Parameters.AddWithValue("@to", toVal)
                    _revenue = Convert.ToDecimal(cmd.ExecuteScalar())
                End Using

                Using cmd As New MySqlCommand(
                    "SELECT COALESCE(SUM(total_refund),0) FROM returns " &
                    "WHERE return_datetime BETWEEN @from AND @to", conn)
                    cmd.Parameters.AddWithValue("@from", fromVal)
                    cmd.Parameters.AddWithValue("@to", toVal)
                    _returns = Convert.ToDecimal(cmd.ExecuteScalar())
                End Using

                Using cmd As New MySqlCommand(
                    "SELECT COALESCE(SUM(total_cost),0) FROM purchase_orders " &
                    "WHERE status = 'Received' AND order_date BETWEEN @from AND @to", conn)
                    cmd.Parameters.AddWithValue("@from", fromVal)
                    cmd.Parameters.AddWithValue("@to", toVal)
                    _poCost = Convert.ToDecimal(cmd.ExecuteScalar())
                End Using

                ' True Cost of Goods Sold: items actually SOLD in the period,
                ' valued at each product's average purchase cost.
                Using cmd As New MySqlCommand(
                    "SELECT COALESCE(SUM(si.qty * ac.avg_cost), 0) " &
                    "FROM sale_items si " &
                    "JOIN sales s ON s.sale_id = si.sale_id " &
                    "JOIN (SELECT product_id, SUM(unit_cost * qty) / NULLIF(SUM(qty),0) AS avg_cost " &
                    "      FROM po_items GROUP BY product_id) ac ON ac.product_id = si.product_id " &
                    "WHERE si.product_id IS NOT NULL " &
                    "AND s.sale_datetime BETWEEN @from AND @to", conn)
                    cmd.Parameters.AddWithValue("@from", fromVal)
                    cmd.Parameters.AddWithValue("@to", toVal)
                    _cogs = Convert.ToDecimal(cmd.ExecuteScalar())
                End Using

                ' Same rule as the Payables screen's "Unpaid": an order placed
                ' with a supplier and not yet settled, as of the report date.
                Using cmd As New MySqlCommand(
                    "SELECT COALESCE(SUM(total_cost),0) FROM purchase_orders " &
                    "WHERE status IN ('Ordered','Partially Received','Received') " &
                    "AND order_date <= @to" &
                    If(AccountantPayablesPanelBuilder.HasPaidAtColumn(conn),
                       " AND (paid_at IS NULL OR paid_at > @to)", " AND status <> 'Received'"), conn)
                    cmd.Parameters.AddWithValue("@to", toVal)
                    _unpaidPos = Convert.ToDecimal(cmd.ExecuteScalar())
                End Using

                Using cmd As New MySqlCommand(
                    "SELECT COALESCE(SUM(stock_qty * unit_price),0) " &
                    "FROM products WHERE is_active = 1", conn)
                    _stockValue = Convert.ToDecimal(cmd.ExecuteScalar())
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Failed to load report data." & vbCrLf & ex.Message,
                          "Data Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try

        ' Trial balance posted with the SAME rules as the ledger, so the two reconcile.
        _trialRows = AccountantLedgerPanelBuilder.GetTrialBalance(_dateFrom, _dateTo)
    End Sub

    ' =========================================================================
    '  RENDER
    ' =========================================================================
    Private Shared Sub Render()
        _container.SuspendLayout()
        _container.Controls.Clear()
        _container.BackColor = MintBg

        _contentHost = New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = MintBg,
            .AutoScroll = True,
            .Padding = New Padding(24, 8, 24, 24)
        }

        ' Report tabs and the From / To period share one row, so the report
        ' card below gets as much of the screen as possible.
        Dim tabRow As Panel = BuildTabRow()
        tabRow.Dock = DockStyle.Top
        tabRow.Height = 60

        Dim titleBar As Panel = BuildTitleBar()
        titleBar.Dock = DockStyle.Top
        titleBar.Height = 76

        _container.Controls.Add(_contentHost)
        _container.Controls.Add(tabRow)
        _container.Controls.Add(titleBar)

        _container.ResumeLayout()

        Dim onVisible As EventHandler = Nothing
        onVisible = Sub(s, e)
                        If _container.Visible Then
                            SwapContent()
                            RemoveHandler _container.VisibleChanged, onVisible
                        End If
                    End Sub
        AddHandler _container.VisibleChanged, onVisible

        SwapContent()
    End Sub

    ' =========================================================================
    '  1. TITLE BAR
    ' =========================================================================
    Private Shared Function BuildTitleBar() As Panel
        Const SIDE_PAD As Integer = 28

        Dim bar As New Panel With {
            .BackColor = MintBg,
            .Height = 76
        }


        Dim lblTitle As New Label With {
            .Text = "Reports",
            .Font = New Font("Segoe UI", 28, FontStyle.Bold),
            .ForeColor = MintDark,
            .AutoSize = True,
            .BackColor = Color.Transparent,
            .Location = New Point(SIDE_PAD - 4, 14)
        }

        ' Generate the report on screen as Excel (editable), PDF, or print.
        Dim btnPrint As Button = MakeActionButton("Print", 110, MintDark, MintDeep)
        Dim btnPdf As Button = MakeActionButton("Save as PDF", 140, MintDark, MintDeep)
        Dim btnExcel As Button = MakeActionButton("Save as Excel", 150, MintPrimary, MintDeep)
        AddHandler btnPrint.Click, Sub() PrintActiveTab()
        AddHandler btnPdf.Click,
            Sub() ReportExport.SaveWithDialog(BuildReportDoc(), ReportExport.ExportFormat.Pdf,
                                              ReportPrinter.CompanyName(), Session.CurrentFullName,
                                              btnPdf.FindForm())
        AddHandler btnExcel.Click,
            Sub() ReportExport.SaveWithDialog(BuildReportDoc(), ReportExport.ExportFormat.Excel,
                                              ReportPrinter.CompanyName(), Session.CurrentFullName,
                                              btnExcel.FindForm())

        AddHandler bar.Resize,
            Sub()
                Dim cy As Integer = (bar.Height - btnPrint.Height) \ 2
                btnPrint.Location = New Point(bar.Width - btnPrint.Width - SIDE_PAD, cy)
                btnPdf.Location = New Point(btnPrint.Left - btnPdf.Width - 10, cy)
                btnExcel.Location = New Point(btnPdf.Left - btnExcel.Width - 10, cy)
            End Sub

        bar.Controls.AddRange({lblTitle, btnExcel, btnPdf, btnPrint})
        Return bar
    End Function

    Private Shared Function MakeActionButton(text As String, width As Integer,
                                             rest As Color, hover As Color) As Button
        Dim b As New Button With {
            .Text = text,
            .FlatStyle = FlatStyle.Flat,
            .Font = New Font("Segoe UI Semibold", 10),
            .ForeColor = Color.White,
            .BackColor = rest,
            .Size = New Size(width, 40),
            .Cursor = Cursors.Hand,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
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

    ' =========================================================================
    '  2. TAB ROW
    ' =========================================================================
    Private Shared Function BuildTabRow() As Panel
        Const SIDE_PAD As Integer = 24

        Dim bar As New Panel With {
            .BackColor = MintBg,
            .Height = 60
        }


        Dim tabs() As (key As String, label As String) = {
            (TAB_INCOME, "Income Statement"),
            (TAB_TRIAL, "Trial Balance"),
            (TAB_BALANCE, "Balance Sheet")
        }

        Dim host As New FlowLayoutPanel With {
            .FlowDirection = FlowDirection.LeftToRight,
            .AutoSize = True,
            .BackColor = Color.Transparent,
            .WrapContents = False,
            .Location = New Point(SIDE_PAD, 11)
        }

        For Each t In tabs
            Dim capKey As String = t.key
            Dim capLabel As String = t.label
            Dim isActive As Boolean = (_activeTab = capKey)

            Dim b As New Button With {
                .Text = capLabel,
                .FlatStyle = FlatStyle.Flat,
                .Cursor = Cursors.Hand,
                .Size = New Size(160, 38),
                .Margin = New Padding(0, 0, 8, 0),
                .Tag = capKey
            }
            b.FlatAppearance.BorderSize = 0

            Dim applyTabStyle As Action(Of Boolean) =
                Sub(active As Boolean)
                    If active Then
                        b.BackColor = MintPrimary
                        b.ForeColor = Color.White
                        b.Font = New Font("Segoe UI Semibold", 10)
                    Else
                        b.BackColor = Color.FromArgb(220, 235, 232)
                        b.ForeColor = SubText
                        b.Font = New Font("Segoe UI", 10)
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
                    If capKey <> _activeTab Then
                        b.BackColor = Color.FromArgb(210, 240, 235)
                        b.ForeColor = MintDark
                        b.Invalidate()
                    End If
                End Sub
            AddHandler b.MouseLeave,
                Sub()
                    If capKey <> _activeTab Then
                        b.BackColor = Color.FromArgb(220, 235, 232)
                        b.ForeColor = SubText
                        b.Invalidate()
                    End If
                End Sub

            AddHandler b.Click,
                Sub()
                    If _activeTab = capKey Then Return
                    _activeTab = capKey
                    For Each ctrl As Control In host.Controls
                        If TypeOf ctrl Is Button Then
                            Dim btn = CType(ctrl, Button)
                            Dim isNowActive As Boolean = (CStr(btn.Tag) = _activeTab)
                            If isNowActive Then
                                btn.BackColor = MintPrimary
                                btn.ForeColor = Color.White
                                btn.Font = New Font("Segoe UI Semibold", 10)
                            Else
                                btn.BackColor = Color.FromArgb(220, 235, 232)
                                btn.ForeColor = SubText
                                btn.Font = New Font("Segoe UI", 10)
                            End If
                            btn.Invalidate()
                        End If
                    Next
                    SwapContent()
                End Sub

            host.Controls.Add(b)
        Next

        ' From / To on the right. Picking a date reloads the figures at once.
        Dim period As Panel = AccountantPeriod.BuildFields(
            Sub()
                LoadData()
                SwapContent()
            End Sub)
        AddHandler bar.Resize,
            Sub()
                period.Location = New Point(bar.Width - period.Width - SIDE_PAD,
                                            (bar.Height - period.Height) \ 2)
                host.Top = (bar.Height - host.PreferredSize.Height) \ 2
            End Sub

        bar.Controls.Add(host)
        bar.Controls.Add(period)
        Return bar
    End Function

    Private Shared Function FormatRange() As String
        Return AccountantPeriod.Text()
    End Function

    ' =========================================================================
    '  CONTENT SWAP
    ' =========================================================================
    Private Shared Sub SwapContent()
        If _contentHost Is Nothing Then Return

        _contentHost.SuspendLayout()
        _contentHost.Controls.Clear()

        Dim content As Panel
        Select Case _activeTab
            Case TAB_TRIAL : content = BuildTrialBalance()
            Case TAB_BALANCE : content = BuildBalanceSheet()
            Case Else : content = BuildIncomeStatement()
        End Select

        content.Dock = DockStyle.Top
        If _contentHost.ClientSize.Width > 0 Then
            content.Width = _contentHost.ClientSize.Width
        End If

        _contentHost.Controls.Add(content)
        _contentHost.ResumeLayout()
        _contentHost.Invalidate(True)
    End Sub

    ' =========================================================================
    '  CARD WRAPPER
    '  Draws the card border + rounded corners. All inner rows just use solid
    '  backgrounds; all divider lines are drawn here from a stored Y-list.
    ' =========================================================================
    Private Shared Function MakeCard(dividerYs As List(Of Integer),
                              sectionYs As List(Of Integer)) As Panel
        Dim card As New BufferedPanel With {.BackColor = Color.Transparent}

        AddHandler card.Paint,
    Sub(s, e)
        ' Let PanelRenderer draw shadow + white fill + border
        PanelRenderer.Draw(card, e, drawBackground:=True)

        Dim g = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias

        ' Row dividers
        Using pen As New Pen(RowDivider, 1)
            For Each y In dividerYs
                g.DrawLine(pen, 24, y, card.Width - 24, y)
            Next
        End Using

        ' Section header bottom borders
        Using pen As New Pen(Color.FromArgb(200, 230, 222), 1)
            For Each y In sectionYs
                g.DrawLine(pen, 0, y, card.Width, y)
            Next
        End Using
    End Sub

        AddHandler card.ControlAdded,
    Sub()
        card.Invalidate()
    End Sub

        AddHandler card.Resize,
    Sub()
        For Each child As Control In card.Controls
            child.Left = 2
            child.Width = card.Width - 4
        Next
        card.Invalidate()
    End Sub

        Return card
    End Function

    ' =========================================================================
    '  TAB 1 — INCOME STATEMENT
    ' =========================================================================
    Private Shared Function BuildIncomeStatement() As Panel
        Dim dividers As New List(Of Integer)
        Dim sections As New List(Of Integer)

        Dim netRevenue As Decimal = _revenue - _returns
        Dim netIncome As Decimal = netRevenue - _cogs


        Dim rows() As (lbl As String, val As Decimal, bold As Boolean,
                   sep As Boolean, highlight As Boolean, sub_ As Boolean) = {
        ("Gross Revenue", _revenue, False, False, False, False),
        ("Less: Returns", _returns, False, False, False, True),
        ("Net Revenue", netRevenue, True, True, False, False),
("Less: Cost of Goods Sold", _cogs, False, False, False, True),
        ("Net Income", netIncome, True, True, True, False)
    }

        Const HEADING_H As Integer = 76
        Const ROW_H As Integer = 58
        Const HIGHLIGHT_H As Integer = 86

        Dim card As Panel = MakeCard(dividers, sections)

        Dim hdrPanel As Panel = MakeSectionHeading("Income Statement",
        _dateFrom.ToString("MMM d, yyyy") & " — " & _dateTo.ToString("MMM d, yyyy"), HEADING_H)
        hdrPanel.Top = 2 : hdrPanel.Left = 2
        hdrPanel.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        card.Controls.Add(hdrPanel)
        sections.Add(HEADING_H + 3)

        Dim y As Integer = HEADING_H + 4

        For Each r In rows
            Dim rowH As Integer = If(r.highlight, HIGHLIGHT_H, ROW_H)
            Dim rowBg As Color = If(r.highlight, HighlightBg, CardBg)

            Dim row As Panel = MakeStatRow(r.lbl, r.val, r.bold, r.highlight,
                                        r.sub_, rowH, rowBg)
            row.Top = y : row.Left = 2
            row.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
            card.Controls.Add(row)

            If Not r.highlight Then
                dividers.Add(y + rowH - 1)
            End If
            If r.sep Then
                dividers.Add(y)
            End If

            y += rowH
        Next

        card.Height = y + 20

        AddHandler card.ParentChanged,
        Sub()
            If card.Parent IsNot Nothing Then
                card.Width = card.Parent.ClientSize.Width
                AddHandler card.Parent.Resize,
                    Sub() card.Width = card.Parent.ClientSize.Width
            End If
        End Sub

        Return card
    End Function

    Private Shared Function MakeStatRow(label As String, value As Decimal,
                                         bold As Boolean, highlight As Boolean,
                                         subtracted As Boolean, h As Integer,
                                         bg As Color) As Panel
        Dim row As New Panel With {.Height = h, .BackColor = bg}

        Dim labelFont As Font = If(bold,
            New Font("Segoe UI Semibold", If(highlight, 15.0F, 12.5F), FontStyle.Bold),
            New Font("Segoe UI", 12.5F))
        Dim valueFont As Font = If(bold,
            New Font("Segoe UI", If(highlight, 19.0F, 13.0F), FontStyle.Bold),
            New Font("Segoe UI", 12.5F))

        Dim labelColor As Color = If(highlight, MintDark,
                                     If(bold, Color.FromArgb(45, 45, 45), SubText))
        Dim valueColor As Color
        If highlight Then
            valueColor = If(value >= 0, MintPrimary, AccentRed)
        ElseIf subtracted Then
            valueColor = AccentRed
        Else
            valueColor = If(bold, MintDeep, Color.FromArgb(45, 45, 45))
        End If

        Dim valueStr As String = If(subtracted AndAlso value > 0,
                                    "(" & Peso & value.ToString("N2") & ")",
                                    Peso & value.ToString("N2"))

        Dim lblLabel As New Label With {
            .Text = label, .Font = labelFont, .ForeColor = labelColor,
            .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblValue As New Label With {
            .Text = valueStr, .Font = valueFont, .ForeColor = valueColor,
            .AutoSize = True, .BackColor = Color.Transparent
        }

        row.Controls.AddRange({lblLabel, lblValue})
        AddHandler row.Resize,
            Sub()
                lblLabel.Location = New Point(28, (h - lblLabel.Height) \ 2)
                lblValue.Location = New Point(row.Width - lblValue.Width - 28,
                                               (h - lblValue.Height) \ 2)
            End Sub
        Return row
    End Function

    ' =========================================================================
    '  TAB 2 — TRIAL BALANCE
    ' =========================================================================
    Private Shared Function BuildTrialBalance() As Panel
        Dim dividers As New List(Of Integer)
        Dim sections As New List(Of Integer)

        Const HEADING_H As Integer = 76
        Const COL_HDR_H As Integer = 46
        Const ROW_H As Integer = 58
        Const TOTAL_H As Integer = 62

        Dim card As Panel = MakeCard(dividers, sections)

        Dim hdrPanel As Panel = MakeSectionHeading("Trial Balance",
            _dateFrom.ToString("MMM d, yyyy") & " — " & _dateTo.ToString("MMM d, yyyy"), HEADING_H)
        hdrPanel.Top = 2 : hdrPanel.Left = 2
        hdrPanel.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        card.Controls.Add(hdrPanel)
        sections.Add(HEADING_H + 3)

        ' Column header
        Dim colHdr As Panel = MakeTrialColHeader(COL_HDR_H)
        colHdr.Top = HEADING_H + 4 : colHdr.Left = 2
        colHdr.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        card.Controls.Add(colHdr)
        sections.Add(HEADING_H + COL_HDR_H + 1)

        Dim rows = _trialRows

        Dim totalDebit As Decimal = 0
        Dim totalCredit As Decimal = 0
        Dim y As Integer = HEADING_H + COL_HDR_H + 2

        For Each r In rows
            Dim row As Panel = MakeTrialDataRow(r.account, r.debit, r.credit, False, ROW_H, CardBg)
            row.Top = y : row.Left = 0
            row.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
            card.Controls.Add(row)
            dividers.Add(y + ROW_H - 1)
            y += ROW_H
            totalDebit += r.debit
            totalCredit += r.credit
        Next

        ' Totals row
        Const TOTALS_H As Integer = TOTAL_H + 10
        Dim totRow As Panel = MakeTrialDataRow("Totals", totalDebit, totalCredit, True, TOTALS_H, HighlightBg)
        totRow.Top = y : totRow.Left = 0
        totRow.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        card.Controls.Add(totRow)
        dividers.Add(y)                      ' line above totals
        dividers.Add(y + TOTALS_H - 1)      ' line below totals
        y += TOTALS_H

        ' Badge
        Dim isBalanced As Boolean = (totalDebit = totalCredit)
        Dim badgeRow As Panel = MakeBalanceBadge(isBalanced, 48)
        badgeRow.Top = y + 8 : badgeRow.Left = 0
        badgeRow.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        card.Controls.Add(badgeRow)
        y += 8 + badgeRow.Height + 20

        card.Height = y + 8

        AddHandler card.ParentChanged,
            Sub()
                If card.Parent IsNot Nothing Then
                    card.Width = card.Parent.ClientSize.Width
                    AddHandler card.Parent.Resize,
                        Sub() card.Width = card.Parent.ClientSize.Width
                End If
            End Sub
        Return card
    End Function

    Private Shared Function MakeTrialColHeader(h As Integer) As Panel
        Dim hdr As New Panel With {.Height = h, .BackColor = SectionBg}

        Dim mk = Function(txt As String) As Label
                     Return New Label With {
                         .Text = txt,
                         .Font = New Font("Segoe UI Semibold", 11, FontStyle.Bold),
                         .ForeColor = Color.FromArgb(100, 140, 130),
                         .AutoSize = True,
                         .BackColor = Color.Transparent
                     }
                 End Function

        Dim chAccount As Label = mk("ACCOUNT")
        Dim chDebit As Label = mk("DEBIT")
        Dim chCredit As Label = mk("CREDIT")

        hdr.Controls.AddRange({chAccount, chDebit, chCredit})
        AddHandler hdr.Resize,
            Sub()
                chAccount.Location = New Point(28, (h - chAccount.Height) \ 2)
                chDebit.Location = New Point(CInt(hdr.Width * 0.55), (h - chDebit.Height) \ 2)
                chCredit.Location = New Point(hdr.Width - chCredit.Width - 28, (h - chCredit.Height) \ 2)
            End Sub
        Return hdr
    End Function

    Private Shared Function MakeTrialDataRow(account As String,
                                              debit As Decimal, credit As Decimal,
                                              isTotals As Boolean,
                                              h As Integer, bg As Color) As Panel
        Dim row As New Panel With {.Height = h, .BackColor = bg}
        Dim fnt As Font = If(isTotals,
            New Font("Segoe UI Semibold", 14.0F, FontStyle.Bold),
            New Font("Segoe UI", 12.5F))

        Dim lblAccount As New Label With {
            .Text = account, .Font = fnt,
            .ForeColor = If(isTotals, MintDark, Color.FromArgb(45, 45, 45)),
            .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblDebit As New Label With {
            .Text = If(debit = 0, "—", Peso & debit.ToString("N2")),
            .Font = fnt,
            .ForeColor = If(isTotals, MintDark, If(debit > 0, Color.FromArgb(45, 45, 45), FaintText)),
            .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblCredit As New Label With {
            .Text = If(credit = 0, "—", Peso & credit.ToString("N2")),
            .Font = fnt,
            .ForeColor = If(isTotals, MintDeep, If(credit > 0, MintDeep, FaintText)),
            .AutoSize = True, .BackColor = Color.Transparent
        }

        row.Controls.AddRange({lblAccount, lblDebit, lblCredit})
        AddHandler row.Resize,
            Sub()
                Dim midY As Integer = (h - lblAccount.Height) \ 2
                lblAccount.Location = New Point(28, midY)
                lblDebit.Location = New Point(CInt(row.Width * 0.55), midY)
                lblCredit.Location = New Point(row.Width - lblCredit.Width - 28, midY)
            End Sub
        Return row
    End Function

    Private Shared Function MakeBalanceBadge(balanced As Boolean, h As Integer) As Panel
        Dim row As New Panel With {.Height = h, .BackColor = CardBg}

        Dim badge As New Label With {
            .Text = If(balanced, "✓  Balanced", "⚠  Out of Balance"),
            .Font = New Font("Segoe UI Semibold", 11, FontStyle.Bold),
            .ForeColor = Color.White,
            .BackColor = If(balanced, MintPrimary, AccentRed),
            .AutoSize = True,
            .Padding = New Padding(14, 6, 14, 6),
            .Cursor = Cursors.Default
        }
        RoundLabel(badge, 12)

        row.Controls.Add(badge)
        AddHandler row.Resize, Sub() badge.Location = New Point(28, (h - badge.Height) \ 2)
        Return row
    End Function

    ' =========================================================================
    '  TAB 3 — BALANCE SHEET
    ' =========================================================================
    Private Shared Function BuildBalanceSheet() As Panel
        Dim dividers As New List(Of Integer)
        Dim sections As New List(Of Integer)

        Const HEADING_H As Integer = 76
        Const SECTION_H As Integer = 48
        Const ROW_H As Integer = 58
        Const TOTAL_H As Integer = 62
        Const EQUITY_H As Integer = 88
        Const GAP As Integer = 12

        Dim netRevenue As Decimal = _revenue - _returns
        Dim cashFromSales As Decimal = netRevenue
        Dim totalAssets As Decimal = cashFromSales + _stockValue
        Dim totalLiab As Decimal = _unpaidPos
        Dim netEquity As Decimal = totalAssets - totalLiab

        Dim card As Panel = MakeCard(dividers, sections)

        ' Heading
        Dim hdrPanel As Panel = MakeSectionHeading("Balance Sheet",
            "As of " & _dateTo.ToString("MMMM d, yyyy"), HEADING_H)
        hdrPanel.Top = 2 : hdrPanel.Left = 2
        hdrPanel.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        card.Controls.Add(hdrPanel)
        sections.Add(HEADING_H + 1)

        Dim y As Integer = HEADING_H + 2

        ' ── ASSETS ───────────────────────────────────────────────────────────
        Dim aHdr As Panel = MakeSheetSectionRow("Assets", SECTION_H)
        aHdr.Top = y : aHdr.Left = 0
        aHdr.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        card.Controls.Add(aHdr)
        sections.Add(y + SECTION_H - 1)
        y += SECTION_H

        Dim assetData() As (String, Decimal, Boolean) = {
            ("Cash from Sales (Net Revenue)", cashFromSales, False),
            ("Stock Value (Active Products)", _stockValue, False),
            ("Total Assets", totalAssets, True)
        }
        For Each r In assetData
            Dim isTot As Boolean = r.Item3
            Dim rH As Integer = If(isTot, TOTAL_H, ROW_H)
            Dim rBg As Color = If(isTot, TotalBg, CardBg)
            Dim valClr As Color = If(isTot, MintDeep, Color.FromArgb(45, 45, 45))
            Dim row As Panel = MakeSheetDataRow(r.Item1, r.Item2, isTot, rH, rBg, valClr)
            row.Top = y : row.Left = 0
            row.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
            card.Controls.Add(row)
            If isTot Then
                dividers.Add(y)
            End If
            dividers.Add(y + rH - 1)
            y += rH
        Next

        y += GAP

        ' ── LIABILITIES ──────────────────────────────────────────────────────
        Dim lHdr As Panel = MakeSheetSectionRow("Liabilities", SECTION_H)
        lHdr.Top = y : lHdr.Left = 0
        lHdr.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        card.Controls.Add(lHdr)
        sections.Add(y + SECTION_H - 1)
        y += SECTION_H

        Dim liabData() As (String, Decimal, Boolean) = {
            ("Unpaid Purchase Orders", _unpaidPos, False),
            ("Total Liabilities", totalLiab, True)
        }
        For Each r In liabData
            Dim isTot As Boolean = r.Item3
            Dim rH As Integer = If(isTot, TOTAL_H, ROW_H)
            Dim rBg As Color = If(isTot, TotalBg, CardBg)
            Dim valClr As Color = If(isTot, MintDeep, Color.FromArgb(45, 45, 45))
            Dim row As Panel = MakeSheetDataRow(r.Item1, r.Item2, isTot, rH, rBg, valClr)
            row.Top = y : row.Left = 0
            row.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
            card.Controls.Add(row)
            If isTot Then dividers.Add(y)
            dividers.Add(y + rH - 1)
            y += rH
        Next

        y += GAP

        ' ── EQUITY ───────────────────────────────────────────────────────────
        Dim eHdr As Panel = MakeSheetSectionRow("Equity", SECTION_H)
        eHdr.Top = y : eHdr.Left = 0
        eHdr.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        card.Controls.Add(eHdr)
        sections.Add(y + SECTION_H - 1)
        y += SECTION_H

        Dim equityClr As Color = If(netEquity >= 0, MintPrimary, AccentRed)
        Dim eqRow As Panel = MakeSheetDataRow("Net Equity  (Assets − Liabilities)",
                                               netEquity, True, EQUITY_H, HighlightBg, equityClr)
        eqRow.Top = y : eqRow.Left = 0
        eqRow.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        card.Controls.Add(eqRow)
        dividers.Add(y)
        dividers.Add(y + EQUITY_H - 1)
        y += EQUITY_H

        card.Height = y + 24

        AddHandler card.ParentChanged,
            Sub()
                If card.Parent IsNot Nothing Then
                    card.Width = card.Parent.ClientSize.Width
                    AddHandler card.Parent.Resize,
                        Sub() card.Width = card.Parent.ClientSize.Width
                End If
            End Sub
        Return card
    End Function

    Private Shared Function MakeSheetSectionRow(title As String, h As Integer) As Panel
        Dim hdr As New Panel With {.Height = h, .BackColor = SectionBg}

        Dim lbl As New Label With {
            .Text = title.ToUpper(),
            .Font = New Font("Segoe UI Semibold", 11, FontStyle.Bold),
            .ForeColor = MintDeep,
            .AutoSize = True,
            .BackColor = Color.Transparent
        }
        lbl.Location = New Point(28, (h - lbl.PreferredHeight) \ 2)
        hdr.Controls.Add(lbl)
        Return hdr
    End Function

    Private Shared Function MakeSheetDataRow(label As String, value As Decimal,
                                              bold As Boolean, h As Integer,
                                              bg As Color, valClr As Color) As Panel
        Dim row As New Panel With {.Height = h, .BackColor = bg}

        Dim fnt As Font = If(bold,
            New Font("Segoe UI Semibold", If(bg.Equals(HighlightBg), 16.0F, 12.5F), FontStyle.Bold),
            New Font("Segoe UI", 12.5F))

        Dim lblLabel As New Label With {
            .Text = label, .Font = fnt,
            .ForeColor = If(bg.Equals(HighlightBg), MintDark,
                            If(bold, Color.FromArgb(45, 45, 45), SubText)),
            .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblVal As New Label With {
            .Text = Peso & value.ToString("N2"),
            .Font = fnt, .ForeColor = valClr,
            .AutoSize = True, .BackColor = Color.Transparent
        }

        row.Controls.AddRange({lblLabel, lblVal})
        AddHandler row.Resize,
            Sub()
                Dim midY As Integer = (h - lblLabel.Height) \ 2
                lblLabel.Location = New Point(28, midY)
                lblVal.Location = New Point(row.Width - lblVal.Width - 28, midY)
            End Sub
        Return row
    End Function

    ' =========================================================================
    '  SECTION HEADING  — card title row
    ' =========================================================================
    Private Shared Function MakeSectionHeading(title As String,
                                            subtitle As String,
                                            h As Integer) As Panel
        Dim hdr As New Panel With {.Height = h - 2, .BackColor = Color.Transparent}

        Dim lblTitle As New Label With {
            .Text = title,
            .Font = New Font("Segoe UI Semibold", 17, FontStyle.Bold),
            .ForeColor = MintDark,
            .AutoSize = True,
            .BackColor = Color.Transparent
        }
        lblTitle.Location = New Point(26, (h - lblTitle.PreferredHeight) \ 2)
        Dim lblSub As New Label With {
            .Text = subtitle,
            .Font = New Font("Segoe UI", 11),
            .ForeColor = FaintText,
            .AutoSize = True,
            .BackColor = Color.Transparent,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        hdr.Controls.AddRange({lblTitle, lblSub})
        AddHandler hdr.Resize,
            Sub()
                lblSub.Location = New Point(hdr.Width - lblSub.Width - 24,
                                             (h - lblSub.Height) \ 2)
            End Sub
        Return hdr
    End Function

    ' =========================================================================
    '  PRINT
    ' =========================================================================
    Private Shared Sub PrintActiveTab()
        Dim doc As ReportPrinter.ReportDoc = BuildReportDoc()

        ' Financial statements leaving the system is a reportable event;
        ' the Admin export records one, so this one does too.
        ActivityLog.Record("REPORT_EXPORT", "report", Nothing,
                           doc.Title & " (Print) - " & doc.PeriodValue)

        ReportPrinter.Preview(doc, companyName:=ReportPrinter.CompanyName(),
                              preparedBy:=Session.CurrentFullName)
    End Sub

    ' The report on the active tab, as rows for print, PDF and Excel alike.
    ' Signatories are added on output (ReportPrinter.EnsureSignatures).
    Private Shared Function BuildReportDoc() As ReportPrinter.ReportDoc
        Dim doc As New ReportPrinter.ReportDoc()
        Dim rangeStr As String = FormatRange()

        Select Case _activeTab

            Case TAB_INCOME
                doc.Title = "Income Statement"
                doc.PeriodLabel = "Period"
                doc.PeriodValue = rangeStr
                doc.Rows.Add(ReportPrinter.Data("Gross Revenue", Peso & _revenue.ToString("N2")))
                doc.Rows.Add(ReportPrinter.Data("Less: Returns",
                If(_returns > 0, "(" & Peso & _returns.ToString("N2") & ")", Peso & _returns.ToString("N2")), _returns > 0))
                doc.Rows.Add(ReportPrinter.Total("Net Revenue", Peso & (_revenue - _returns).ToString("N2")))
                doc.Rows.Add(ReportPrinter.Data("Less: Cost of Goods Sold",
                If(_cogs > 0, "(" & Peso & _cogs.ToString("N2") & ")", Peso & _cogs.ToString("N2")), _cogs > 0))
                Dim netIncome As Decimal = _revenue - _returns - _cogs
                doc.Rows.Add(ReportPrinter.Highlight("NET INCOME", Peso & netIncome.ToString("N2"), netIncome < 0))

            Case TAB_TRIAL
                doc.Title = "Trial Balance"
                doc.PeriodLabel = "Period"
                doc.PeriodValue = rangeStr
                doc.Rows.Add(ReportPrinter.TwoColHeader("DEBIT", "CREDIT"))
                Dim tD As Decimal = 0D, tC As Decimal = 0D
                For Each r In _trialRows
                    doc.Rows.Add(ReportPrinter.TwoCol(r.Account,
                        If(r.Debit > 0, Peso & r.Debit.ToString("N2"), "—"),
                        If(r.Credit > 0, Peso & r.Credit.ToString("N2"), "—")))
                    tD += r.Debit : tC += r.Credit
                Next
                doc.Rows.Add(ReportPrinter.TwoColTotal("Totals", Peso & tD.ToString("N2"), Peso & tC.ToString("N2")))
                doc.Rows.Add(ReportPrinter.Spacer())
                doc.Rows.Add(ReportPrinter.Status(If(tD = tC, "✓  Balanced", "⚠  Out of Balance"), tD = tC))

            Case TAB_BALANCE
                doc.Title = "Balance Sheet"
                doc.PeriodLabel = "As of"
                doc.PeriodValue = _dateTo.ToString("MMMM d, yyyy")
                Dim net As Decimal = _revenue - _returns
                Dim totA As Decimal = net + _stockValue
                Dim totL As Decimal = _unpaidPos
                Dim equity As Decimal = totA - totL

                doc.Rows.Add(ReportPrinter.Section("Assets"))
                doc.Rows.Add(ReportPrinter.Data("Cash from Sales (Net Revenue)", Peso & net.ToString("N2")))
                doc.Rows.Add(ReportPrinter.Data("Stock Value (Active Products)", Peso & _stockValue.ToString("N2")))
                doc.Rows.Add(ReportPrinter.Total("Total Assets", Peso & totA.ToString("N2")))
                doc.Rows.Add(ReportPrinter.Spacer())
                doc.Rows.Add(ReportPrinter.Section("Liabilities"))
                doc.Rows.Add(ReportPrinter.Data("Unpaid Purchase Orders", Peso & _unpaidPos.ToString("N2")))
                doc.Rows.Add(ReportPrinter.Total("Total Liabilities", Peso & totL.ToString("N2")))
                doc.Rows.Add(ReportPrinter.Spacer())
                doc.Rows.Add(ReportPrinter.Section("Equity"))
                doc.Rows.Add(ReportPrinter.Highlight("NET EQUITY  (Assets − Liabilities)", Peso & equity.ToString("N2"), equity < 0))

            Case Else
                doc.Title = "Report"
        End Select

        Return doc
    End Function

    ' =========================================================================
    '  HELPERS
    ' =========================================================================
    Private Shared Sub RoundLabel(lbl As Label, radius As Integer)
        AddHandler lbl.Resize,
            Sub()
                Dim r As Integer = radius
                Dim path As New GraphicsPath()
                path.AddArc(0, 0, r, r, 180, 90)
                path.AddArc(lbl.Width - r, 0, r, r, 270, 90)
                path.AddArc(lbl.Width - r, lbl.Height - r, r, r, 0, 90)
                path.AddArc(0, lbl.Height - r, r, r, 90, 90)
                path.CloseFigure()
                lbl.Region = New Region(path)
            End Sub
    End Sub

    Private Shared Sub RoundBtn(btn As Button, radius As Integer)
        AddHandler btn.Resize,
            Sub()
                Dim r As Integer = radius
                Dim path As New GraphicsPath()
                path.AddArc(0, 0, r, r, 180, 90)
                path.AddArc(btn.Width - r, 0, r, r, 270, 90)
                path.AddArc(btn.Width - r, btn.Height - r, r, r, 0, 90)
                path.AddArc(0, btn.Height - r, r, r, 90, 90)
                path.CloseFigure()
                btn.Region = New Region(path)
            End Sub
    End Sub

End Class