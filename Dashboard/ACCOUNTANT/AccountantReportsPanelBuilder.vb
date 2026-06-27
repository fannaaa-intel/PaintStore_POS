Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports MySql.Data.MySqlClient

' =============================================================================
'  AccountantReportsPanelBuilder  —  Screen 5: Financial Reports
'
'  LAYOUT  (Dock stacking — added reverse so Top bars stack correctly)
'  ------
'    TopBar       (Dock = Top,    64px)  : dark gradient — title + subtitle
'    TabRow       (Dock = Top,    44px)  : Income Statement | Trial Balance | Balance Sheet
'    DateRow      (Dock = Top,    52px)  : From date | To date | Apply pill
'    PrintBar     (Dock = Bottom, 52px)  : Print button pinned right
'    ContentHost  (Dock = Fill)          : swapped per active tab
'
'  Tab switching:
'    Active tab   → bold label, MintPrimary 3px underline drawn in Paint
'    Inactive tab → normal weight, no underline, SubText colour
'    Click        → swap ContentHost, no full re-render
'
'  DB queries (parameterised @from / @to):
'    Revenue   : SELECT SUM(total)        FROM sales            WHERE sale_datetime BETWEEN @from AND @to
'    Returns   : SELECT SUM(total_refund) FROM returns          WHERE return_datetime BETWEEN @from AND @to
'    PO Costs  : SELECT SUM(total_cost)   FROM purchase_orders  WHERE status='Received' AND order_date BETWEEN @from AND @to
'    Unpaid POs: SELECT SUM(total_cost)   FROM purchase_orders  WHERE status NOT IN ('Received','Cancelled')
'    Stock Val : SELECT SUM(stock_qty*unit_price) FROM products WHERE is_active=1
' =============================================================================
Public Class AccountantReportsPanelBuilder

    ' ── PALETTE  (exact match — all Accountant panels) ────────────────────────
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
    Private Shared ReadOnly AccentRed As Color = Color.FromArgb(220, 80, 80)
    Private Shared ReadOnly HighlightBg As Color = Color.FromArgb(236, 252, 248)
    Private Shared ReadOnly Peso As String = ChrW(8369)

    ' Tab identifiers
    Private Const TAB_INCOME As String = "income"
    Private Const TAB_TRIAL As String = "trial"
    Private Const TAB_BALANCE As String = "balance"

    ' ── STATE ─────────────────────────────────────────────────────────────────
    Private Shared _container As Panel
    Private Shared _activeTab As String = TAB_INCOME
    Private Shared _dateFrom As DateTime = DateTime.Today.AddDays(-30)
    Private Shared _dateTo As DateTime = DateTime.Today

    ' DB figures — loaded once per Apply
    Private Shared _revenue As Decimal
    Private Shared _returns As Decimal
    Private Shared _poCost As Decimal
    Private Shared _unpaidPos As Decimal
    Private Shared _stockValue As Decimal

    ' Live-swap content host
    Private Shared _contentHost As Panel

    ' Tab label references (for underline repaint)
    Private Shared _tabLabels As New Dictionary(Of String, Label)

    ' Date pickers (kept for Apply handler)
    Private Shared _dtFrom As DateTimePicker
    Private Shared _dtTo As DateTimePicker

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Shared Sub LoadReports(container As Panel,
                                  Optional tab As String = TAB_INCOME)
        _container = container
        _activeTab = tab
        _tabLabels.Clear()

        LoadData()
        Render()
    End Sub

    ' =========================================================================
    '  DATA  — runs on first load and every Apply click
    ' =========================================================================
    Private Shared Sub LoadData()
        _revenue = 0
        _returns = 0
        _poCost = 0
        _unpaidPos = 0
        _stockValue = 0

        ' Widen @to to end-of-day so rows from the To date are included
        Dim fromVal As DateTime = _dateFrom.Date
        Dim toVal As DateTime = _dateTo.Date.AddDays(1).AddSeconds(-1)

        Try
            Using conn = Db.GetConnection()
                conn.Open()

                ' ── Revenue (completed sales in range) ────────────────────────
                Using cmd As New MySqlCommand(
                    "SELECT COALESCE(SUM(total),0) FROM sales " &
                    "WHERE sale_datetime BETWEEN @from AND @to", conn)
                    cmd.Parameters.AddWithValue("@from", fromVal)
                    cmd.Parameters.AddWithValue("@to", toVal)
                    _revenue = Convert.ToDecimal(cmd.ExecuteScalar())
                End Using

                ' ── Returns in range ─────────────────────────────────────────
                Using cmd As New MySqlCommand(
                    "SELECT COALESCE(SUM(total_refund),0) FROM returns " &
                    "WHERE return_datetime BETWEEN @from AND @to", conn)
                    cmd.Parameters.AddWithValue("@from", fromVal)
                    cmd.Parameters.AddWithValue("@to", toVal)
                    _returns = Convert.ToDecimal(cmd.ExecuteScalar())
                End Using

                ' ── Cost of received POs in range ────────────────────────────
                Using cmd As New MySqlCommand(
                    "SELECT COALESCE(SUM(total_cost),0) FROM purchase_orders " &
                    "WHERE status = 'Received' AND order_date BETWEEN @from AND @to", conn)
                    cmd.Parameters.AddWithValue("@from", fromVal)
                    cmd.Parameters.AddWithValue("@to", toVal)
                    _poCost = Convert.ToDecimal(cmd.ExecuteScalar())
                End Using

                ' ── Unpaid POs (no date filter — current liability) ───────────
                Using cmd As New MySqlCommand(
                    "SELECT COALESCE(SUM(total_cost),0) FROM purchase_orders " &
                    "WHERE status NOT IN ('Received','Cancelled')", conn)
                    _unpaidPos = Convert.ToDecimal(cmd.ExecuteScalar())
                End Using

                ' ── Active stock value (no date filter — current asset) ───────
                Using cmd As New MySqlCommand(
                    "SELECT COALESCE(SUM(stock_qty * unit_price),0) " &
                    "FROM products WHERE is_active = 1", conn)
                    _stockValue = Convert.ToDecimal(cmd.ExecuteScalar())
                End Using
            End Using
        Catch ex As Exception
            Debug.WriteLine("AccountantReports LoadData error: " & ex.Message)
        End Try
    End Sub

    ' =========================================================================
    '  RENDER  — builds chrome; swaps content separately
    ' =========================================================================
    Private Shared Sub Render()
        _container.SuspendLayout()
        _container.Controls.Clear()
        _container.BackColor = MintBg

        ' ── Print bar (Bottom — add first) ────────────────────────────────────
        Dim printBar As Panel = BuildPrintBar()
        printBar.Dock = DockStyle.Bottom
        printBar.Height = 52

        ' ── Content host (Fill — add before Top panels) ───────────────────────
        _contentHost = New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = MintBg,
            .AutoScroll = True,
            .Padding = New Padding(24, 18, 24, 18)
        }

        ' ── Date range row (Top) ─────────────────────────────────────────────
        Dim dateRow As Panel = BuildDateRow()
        dateRow.Dock = DockStyle.Top
        dateRow.Height = 52

        ' ── Tab row (Top) ────────────────────────────────────────────────────
        Dim tabRow As Panel = BuildTabRow()
        tabRow.Dock = DockStyle.Top
        tabRow.Height = 44

        ' ── Top bar (Top — outermost) ─────────────────────────────────────────
        Dim topBar As Panel = BuildTopBar()
        topBar.Dock = DockStyle.Top
        topBar.Height = 64

        ' Add in Dock resolution order (Fill/Bottom first, then Top last-added = topmost)
        _container.Controls.Add(_contentHost)
        _container.Controls.Add(printBar)
        _container.Controls.Add(dateRow)
        _container.Controls.Add(tabRow)
        _container.Controls.Add(topBar)

        _container.ResumeLayout()

        SwapContent()
    End Sub

    ' =========================================================================
    '  1. TOP BAR  — 64 px dark gradient
    ' =========================================================================
    Private Shared Function BuildTopBar() As Panel
        Dim bar As New BufferedPanel With {.BackColor = MintDark}

        AddHandler bar.Paint,
            Sub(s, e)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Using br As New LinearGradientBrush(
                        bar.ClientRectangle, MintDark, HeaderBg, 0.0F)
                    g.FillRectangle(br, bar.ClientRectangle)
                End Using
            End Sub

        Dim lblTitle As New Label With {
            .Text = "Reports",
            .Font = New Font("Segoe UI Semibold", 13, FontStyle.Bold),
            .ForeColor = Color.White,
            .BackColor = Color.Transparent,
            .AutoSize = True,
            .Location = New Point(22, 10)
        }
        Dim lblSub As New Label With {
            .Text = "Financial Statements",
            .Font = New Font("Segoe UI", 9),
            .ForeColor = Color.FromArgb(160, 215, 200),
            .BackColor = Color.Transparent,
            .AutoSize = True,
            .Location = New Point(24, 36)
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
    '  2. TAB ROW  — Income Statement | Trial Balance | Balance Sheet
    '     Active tab: MintDark bold label + 3 px MintPrimary underline
    '     Inactive  : SubText normal, no underline
    ' =========================================================================
    Private Shared Function BuildTabRow() As Panel
        Dim bar As New Panel With {
            .BackColor = Color.FromArgb(250, 252, 251)
        }

        AddHandler bar.Paint,
            Sub(s, e)
                ' Bottom separator
                Using p As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(p, 0, bar.Height - 1, bar.Width, bar.Height - 1)
                End Using
            End Sub

        Dim tabs() As (key As String, label As String) = {
            (TAB_INCOME, "Income Statement"),
            (TAB_TRIAL, "Trial Balance"),
            (TAB_BALANCE, "Balance Sheet")
        }

        Dim x As Integer = 24

        For Each t In tabs
            Dim capKey As String = t.key
            Dim capLabel As String = t.label
            Dim isActive As Boolean = (_activeTab = capKey)

            Dim lbl As New Label With {
                .Text = capLabel,
                .Font = New Font("Segoe UI", 10.5F,
                                      If(isActive, FontStyle.Bold, FontStyle.Regular)),
                .ForeColor = If(isActive, MintDark, SubText),
                .BackColor = Color.Transparent,
                .AutoSize = True,
                .Cursor = Cursors.Hand,
                .Location = New Point(x, 10),
                .Tag = capKey
            }

            ' Underline painted per label
            AddHandler lbl.Paint,
                Sub(s, e)
                    Dim l = CType(s, Label)
                    If CStr(l.Tag) = _activeTab Then
                        Using br As New SolidBrush(MintPrimary)
                            e.Graphics.FillRectangle(br,
                                New Rectangle(0, l.Height - 3, l.Width, 3))
                        End Using
                    End If
                End Sub

            AddHandler lbl.Click,
                Sub()
                    If _activeTab = capKey Then Return
                    _activeTab = capKey
                    RefreshTabStyles()
                    SwapContent()
                End Sub

            AddHandler lbl.MouseEnter,
    Sub()
        If capKey <> _activeTab Then
            For Each c As Control In bar.Controls
                If TypeOf c Is Label AndAlso CStr(CType(c, Label).Tag) = capKey Then
                    CType(c, Label).ForeColor = MintDeep
                    CType(c, Label).Invalidate()
                End If
            Next
        End If
    End Sub
            AddHandler lbl.MouseLeave,
    Sub()
        If capKey <> _activeTab Then
            For Each c As Control In bar.Controls
                If TypeOf c Is Label AndAlso CStr(CType(c, Label).Tag) = capKey Then
                    CType(c, Label).ForeColor = SubText
                    CType(c, Label).Invalidate()
                End If
            Next
        End If
    End Sub

            _tabLabels(capKey) = lbl
            bar.Controls.Add(lbl)
            x += lbl.PreferredWidth + 36
        Next

        Return bar
    End Function

    ' Restyle all tab labels after a switch (no full re-render)
    Private Shared Sub RefreshTabStyles()
        For Each kvp In _tabLabels
            Dim isActive As Boolean = (kvp.Key = _activeTab)
            kvp.Value.Font = New Font("Segoe UI", 10.5F,
                                          If(isActive, FontStyle.Bold, FontStyle.Regular))
            kvp.Value.ForeColor = If(isActive, MintDark, SubText)
            kvp.Value.Invalidate()
        Next
    End Sub

    ' =========================================================================
    '  3. DATE RANGE ROW  — From/To DateTimePickers + Apply pill
    ' =========================================================================
    Private Shared Function BuildDateRow() As Panel
        Dim bar As New Panel With {
            .BackColor = Color.FromArgb(247, 250, 249),
            .Padding = New Padding(24, 10, 24, 10)
        }

        AddHandler bar.Paint,
            Sub(s, e)
                Using p As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(p, 0, bar.Height - 1, bar.Width, bar.Height - 1)
                End Using
            End Sub

        ' "From" label
        Dim lblFrom As New Label With {
            .Text = "From:",
            .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = SubText,
            .AutoSize = True,
            .BackColor = Color.Transparent,
            .Location = New Point(24, 16)
        }

        ' From date picker
        _dtFrom = New DateTimePicker With {
            .Format = DateTimePickerFormat.Short,
            .Value = _dateFrom,
            .Font = New Font("Segoe UI", 9.5F),
            .Width = 110,
            .Location = New Point(68, 12)
        }

        ' "To" label
        Dim lblTo As New Label With {
            .Text = "To:",
            .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = SubText,
            .AutoSize = True,
            .BackColor = Color.Transparent,
            .Location = New Point(192, 16)
        }

        ' To date picker
        _dtTo = New DateTimePicker With {
            .Format = DateTimePickerFormat.Short,
            .Value = _dateTo,
            .Font = New Font("Segoe UI", 9.5F),
            .Width = 110,
            .Location = New Point(214, 12)
        }

        ' Apply pill button
        Dim btnApply As New Button With {
            .Text = "Apply",
            .FlatStyle = FlatStyle.Flat,
            .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold),
            .ForeColor = Color.White,
            .BackColor = MintPrimary,
            .Size = New Size(78, 30),
            .Cursor = Cursors.Hand,
            .Location = New Point(338, 10)
        }
        btnApply.FlatAppearance.BorderSize = 0
        RoundBtn(btnApply, 12)

        AddHandler btnApply.MouseEnter, Sub() btnApply.BackColor = MintDeep
        AddHandler btnApply.MouseLeave, Sub() btnApply.BackColor = MintPrimary
        AddHandler btnApply.Click,
            Sub()
                ' Validate date range
                If _dtFrom.Value.Date > _dtTo.Value.Date Then
                    MessageBox.Show("'From' date cannot be after 'To' date.",
                                    "Invalid Range",
                                    MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    Return
                End If
                _dateFrom = _dtFrom.Value.Date
                _dateTo = _dtTo.Value.Date
                LoadData()
                SwapContent()
            End Sub

        ' Range summary label (updates after Apply)
        Dim lblRange As New Label With {
            .Text = FormatRange(),
            .Font = New Font("Segoe UI", 9),
            .ForeColor = FaintText,
            .AutoSize = True,
            .BackColor = Color.Transparent,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        AddHandler btnApply.Click,
            Sub()
                lblRange.Text = FormatRange()
                AddHandler bar.Resize, Sub() PositionRangeLabel(bar, lblRange)
                PositionRangeLabel(bar, lblRange)
            End Sub
        AddHandler bar.Resize, Sub() PositionRangeLabel(bar, lblRange)

        bar.Controls.AddRange({lblFrom, _dtFrom, lblTo, _dtTo, btnApply, lblRange})
        Return bar
    End Function

    Private Shared Function FormatRange() As String
        Return _dateFrom.ToString("MMM d, yyyy") & "  —  " & _dateTo.ToString("MMM d, yyyy")
    End Function
    Private Shared Sub PositionRangeLabel(bar As Panel, lbl As Label)
        lbl.Location = New Point(bar.Width - lbl.Width - 24, (bar.Height - lbl.Height) \ 2)
    End Sub

    ' =========================================================================
    '  4. PRINT BAR  — pinned bottom, Print button right
    ' =========================================================================
    Private Shared Function BuildPrintBar() As Panel
        Dim bar As New Panel With {
            .BackColor = Color.FromArgb(247, 250, 249)
        }
        AddHandler bar.Paint,
            Sub(s, e)
                Using p As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(p, 0, 0, bar.Width, 0)
                End Using
            End Sub

        Dim btnPrint As New Button With {
            .Text = "⎙  Print Report",
            .FlatStyle = FlatStyle.Flat,
            .Font = New Font("Segoe UI Semibold", 10),
            .ForeColor = Color.White,
            .BackColor = MintDark,
            .Size = New Size(148, 34),
            .Cursor = Cursors.Hand,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        btnPrint.FlatAppearance.BorderSize = 0
        RoundBtn(btnPrint, 10)

        AddHandler btnPrint.MouseEnter, Sub() btnPrint.BackColor = HeaderBg
        AddHandler btnPrint.MouseLeave, Sub() btnPrint.BackColor = MintDark
        AddHandler btnPrint.Click, Sub() PrintActiveTab()

        bar.Controls.Add(btnPrint)
        AddHandler bar.Resize,
            Sub()
                btnPrint.Location = New Point(bar.Width - btnPrint.Width - 22,
                                               (bar.Height - btnPrint.Height) \ 2)
            End Sub

        Return bar
    End Function

    ' =========================================================================
    '  CONTENT SWAP  — clears _contentHost and rebuilds for the active tab
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
        _contentHost.Controls.Add(content)
        _contentHost.ResumeLayout()
        _contentHost.Invalidate(True)
    End Sub

    ' =========================================================================
    '  TAB 1 — INCOME STATEMENT
    ' =========================================================================
    Private Shared Function BuildIncomeStatement() As Panel
        Dim card As New BufferedPanel With {
            .BackColor = CardBg,
            .Padding = New Padding(0, 0, 0, 18)
        }
        AddHandler card.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)

        ' Header
        card.Controls.Add(MakeSectionHeading("Income Statement",
            _dateFrom.ToString("MMM d, yyyy") & " — " & _dateTo.ToString("MMM d, yyyy")))

        ' Computed figures
        Dim netRevenue As Decimal = _revenue - _returns
        Dim netIncome As Decimal = netRevenue - _poCost

        ' Row data  (label, value, isBold, isSeparator, isHighlight, isSubtracted)
        Dim rows() As (lbl As String, val As Decimal, bold As Boolean,
                       sep As Boolean, highlight As Boolean, sub_ As Boolean) = {
            ("Gross Revenue", _revenue, False, False, False, False),
            ("Less: Returns", _returns, False, False, False, True),
            ("Net Revenue", netRevenue, True, True, False, False),
            ("Less: Cost of Purchases", _poCost, False, False, False, True),
            ("Net Income", netIncome, True, True, True, False)
        }

        Dim y As Integer = 68
        For Each r In rows
            Dim row As Panel = MakeStatementRow(r.lbl, r.val, r.bold,
                                                r.sep, r.highlight, r.sub_)
            row.Top = y
            row.Left = 0
            row.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
            card.Controls.Add(row)
            y += row.Height
        Next

        card.Height = y + 18
        AddHandler card.ParentChanged,
      Sub()
          If card.Parent IsNot Nothing Then
              AddHandler card.Parent.Resize,
                  Sub() card.Width = card.Parent.ClientSize.Width
          End If
      End Sub
        Return card
    End Function

    Private Shared Function MakeStatementRow(
            label As String, value As Decimal,
            bold As Boolean, topSep As Boolean,
            highlight As Boolean, subtracted As Boolean) As Panel

        Dim h As Integer = If(highlight, 56, 42)

        Dim row As New Panel With {
            .Height = h,
            .BackColor = If(highlight, HighlightBg, Color.Transparent),
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        }

        AddHandler row.Paint,
            Sub(s, e)
                ' Top separator line (for sub-total rows)
                If topSep Then
                    Using p As New Pen(RowDivider, 1)
                        e.Graphics.DrawLine(p, 24, 0, row.Width - 24, 0)
                    End Using
                End If
                ' Bottom divider for normal rows
                If Not highlight Then
                    Using p As New Pen(RowDivider, 1)
                        e.Graphics.DrawLine(p, 24, h - 1, row.Width - 24, h - 1)
                    End Using
                End If
                ' MintPrimary left accent line
                Using br As New SolidBrush(MintPrimary)
                    e.Graphics.FillRectangle(br, New Rectangle(0, 10, 3, h - 20))
                End Using
                ' Highlight border (bottom)
                If highlight Then
                    Using p As New Pen(Color.FromArgb(180, 230, 218), 1)
                        e.Graphics.DrawLine(p, 0, h - 1, row.Width, h - 1)
                    End Using
                End If
            End Sub

        Dim labelFont As Font = If(bold,
            New Font("Segoe UI Semibold", If(highlight, 12.0F, 10.5F), FontStyle.Bold),
            New Font("Segoe UI", 10.5F))

        Dim valueFont As Font = If(bold,
            New Font("Segoe UI", If(highlight, 13.0F, 11.0F), FontStyle.Bold),
            New Font("Segoe UI", 10.5F))

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
            .Text = label,
            .Font = labelFont,
            .ForeColor = labelColor,
            .AutoSize = True,
            .BackColor = Color.Transparent,
            .Anchor = AnchorStyles.Left Or AnchorStyles.Top
        }
        Dim lblValue As New Label With {
            .Text = valueStr,
            .Font = valueFont,
            .ForeColor = valueColor,
            .AutoSize = True,
            .BackColor = Color.Transparent,
            .Anchor = AnchorStyles.Right Or AnchorStyles.Top
        }

        row.Controls.Add(lblLabel)
        row.Controls.Add(lblValue)

        AddHandler row.Resize,
            Sub()
                lblLabel.Location = New Point(24, (h - lblLabel.Height) \ 2)
                lblValue.Location = New Point(row.Width - lblValue.Width - 24,
                                               (h - lblValue.Height) \ 2)
            End Sub

        Return row
    End Function

    ' =========================================================================
    '  TAB 2 — TRIAL BALANCE
    ' =========================================================================
    Private Shared Function BuildTrialBalance() As Panel
        Dim card As New BufferedPanel With {
            .BackColor = CardBg,
            .Padding = New Padding(0, 0, 0, 18)
        }
        AddHandler card.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)

        card.Controls.Add(MakeSectionHeading("Trial Balance",
            _dateFrom.ToString("MMM d, yyyy") & " — " & _dateTo.ToString("MMM d, yyyy")))

        ' Column header
        Dim colHdr As Panel = MakeTrialColumnHeader()
        colHdr.Top = 68
        colHdr.Left = 0
        colHdr.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        card.Controls.Add(colHdr)

        ' Data rows  (account, debit, credit)
        Dim rows() As (account As String, debit As Decimal, credit As Decimal) = {
            ("Sales Revenue", 0D, _revenue),
            ("Purchase Expenses", _poCost, 0D),
            ("Returns", _returns, 0D)
        }

        Dim totalDebit As Decimal = 0
        Dim totalCredit As Decimal = 0
        Dim y As Integer = 68 + colHdr.Height

        For Each r In rows
            Dim row As Panel = MakeTrialRow(r.account, r.debit, r.credit, False)
            row.Top = y
            row.Left = 0
            row.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
            card.Controls.Add(row)
            y += row.Height
            totalDebit += r.debit
            totalCredit += r.credit
        Next

        ' Totals row
        Dim totalsRow As Panel = MakeTrialRow("Totals", totalDebit, totalCredit, True)
        totalsRow.Top = y
        totalsRow.Left = 0
        totalsRow.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        card.Controls.Add(totalsRow)
        y += totalsRow.Height

        ' Balance / Out-of-balance badge
        Dim isBalanced As Boolean = (totalDebit = totalCredit)
        Dim badgeRow As Panel = MakeBalanceBadge(isBalanced)
        badgeRow.Top = y + 12
        badgeRow.Left = 0
        badgeRow.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        card.Controls.Add(badgeRow)
        y += badgeRow.Height + 24

        card.Height = y + 18
        Return card
    End Function

    Private Shared Function MakeTrialColumnHeader() As Panel
        Dim hdr As New Panel With {
            .Height = 34,
            .BackColor = Color.FromArgb(247, 250, 249)
        }
        AddHandler hdr.Paint,
            Sub(s, e)
                Using p As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(p, 0, 0, hdr.Width, 0)
                    e.Graphics.DrawLine(p, 0, hdr.Height - 1, hdr.Width, hdr.Height - 1)
                End Using
            End Sub

        Dim mk = Function(txt As String) As Label
                     Return New Label With {
                         .Text = txt,
                         .Font = New Font("Segoe UI Semibold", 9),
                         .ForeColor = Color.FromArgb(122, 152, 144),
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
                chAccount.Location = New Point(24, (hdr.Height - chAccount.Height) \ 2)
                chDebit.Location = New Point(CInt(hdr.Width * 0.55),
                                               (hdr.Height - chDebit.Height) \ 2)
                chCredit.Location = New Point(hdr.Width - chCredit.Width - 24,
                                               (hdr.Height - chCredit.Height) \ 2)
            End Sub
        Return hdr
    End Function

    Private Shared Function MakeTrialRow(account As String,
                                          debit As Decimal, credit As Decimal,
                                          isTotals As Boolean) As Panel
        Dim h As Integer = If(isTotals, 46, 42)

        Dim row As New Panel With {
            .Height = h,
            .BackColor = If(isTotals, Color.FromArgb(247, 250, 249), Color.Transparent)
        }

        AddHandler row.Paint,
            Sub(s, e)
                Using p As New Pen(RowDivider, If(isTotals, 1.5F, 1.0F))
                    e.Graphics.DrawLine(p, 0, 0, row.Width, 0)
                    e.Graphics.DrawLine(p, 0, h - 1, row.Width, h - 1)
                End Using
                If Not isTotals Then
                    Using br As New SolidBrush(MintPrimary)
                        e.Graphics.FillRectangle(br, New Rectangle(0, 10, 3, h - 20))
                    End Using
                End If
            End Sub

        Dim fnt As Font = If(isTotals,
            New Font("Segoe UI Semibold", 10.5F, FontStyle.Bold),
            New Font("Segoe UI", 10.5F))

        Dim lblAccount As New Label With {
            .Text = account,
            .Font = fnt,
            .ForeColor = If(isTotals, MintDark, Color.FromArgb(45, 45, 45)),
            .AutoSize = True,
            .BackColor = Color.Transparent
        }
        Dim lblDebit As New Label With {
            .Text = If(debit = 0, "—", Peso & debit.ToString("N2")),
            .Font = fnt,
            .ForeColor = If(isTotals, MintDark,
                            If(debit > 0, Color.FromArgb(45, 45, 45), FaintText)),
            .AutoSize = True,
            .BackColor = Color.Transparent
        }
        Dim lblCredit As New Label With {
            .Text = If(credit = 0, "—", Peso & credit.ToString("N2")),
            .Font = fnt,
            .ForeColor = If(isTotals, MintDark,
                            If(credit > 0, MintDeep, FaintText)),
            .AutoSize = True,
            .BackColor = Color.Transparent
        }

        row.Controls.AddRange({lblAccount, lblDebit, lblCredit})
        AddHandler row.Resize,
            Sub()
                Dim midY As Integer = (h - lblAccount.Height) \ 2
                lblAccount.Location = New Point(24, midY)
                lblDebit.Location = New Point(CInt(row.Width * 0.55), midY)
                lblCredit.Location = New Point(row.Width - lblCredit.Width - 24, midY)
            End Sub

        Return row
    End Function

    Private Shared Function MakeBalanceBadge(balanced As Boolean) As Panel
        Dim row As New Panel With {
            .Height = 38,
            .BackColor = Color.Transparent
        }

        Dim badgeFore As Color = If(balanced, Color.White, Color.White)
        Dim badgeBack As Color = If(balanced, MintPrimary, AccentRed)
        Dim badgeText As String = If(balanced, "✓  Balanced", "⚠  Out of Balance")

        Dim badge As New Label With {
            .Text = badgeText,
            .Font = New Font("Segoe UI Semibold", 9.5F, FontStyle.Bold),
            .ForeColor = badgeFore,
            .BackColor = badgeBack,
            .AutoSize = True,
            .Padding = New Padding(14, 5, 14, 5),
            .Cursor = Cursors.Default
        }
        RoundLabel(badge, 12)

        row.Controls.Add(badge)
        AddHandler row.Resize,
            Sub() badge.Location = New Point(24, (row.Height - badge.Height) \ 2)

        Return row
    End Function

    ' =========================================================================
    '  TAB 3 — BALANCE SHEET
    ' =========================================================================
    Private Shared Function BuildBalanceSheet() As Panel
        Dim card As New BufferedPanel With {
            .BackColor = CardBg,
            .Padding = New Padding(0, 0, 0, 18)
        }
        AddHandler card.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)

        card.Controls.Add(MakeSectionHeading("Balance Sheet",
            "As of " & _dateTo.ToString("MMMM d, yyyy")))

        Dim netRevenue As Decimal = _revenue - _returns
        Dim cashFromSales As Decimal = netRevenue
        Dim totalAssets As Decimal = cashFromSales + _stockValue
        Dim totalLiab As Decimal = _unpaidPos
        Dim netEquity As Decimal = totalAssets - totalLiab

        Dim y As Integer = 68

        ' ── ASSETS ───────────────────────────────────────────────────────────
        Dim assetsHdr As Panel = MakeSheetSectionHeader("Assets")
        assetsHdr.Top = y : assetsHdr.Left = 0
        assetsHdr.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        card.Controls.Add(assetsHdr)
        y += assetsHdr.Height

        Dim assetRows() As (String, Decimal, Boolean, Boolean) = {
            ("Cash from Sales (Net Revenue)", cashFromSales, False, False),
            ("Stock Value (Active Products)", _stockValue, False, False),
            ("Total Assets", totalAssets, True, True)
        }
        For Each r In assetRows
            Dim row = MakeSheetRow(r.Item1, r.Item2, r.Item3, r.Item4, False)
            row.Top = y : row.Left = 0
            row.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
            card.Controls.Add(row)
            y += row.Height
        Next

        y += 12  ' gap between sections

        ' ── LIABILITIES ──────────────────────────────────────────────────────
        Dim liabHdr As Panel = MakeSheetSectionHeader("Liabilities")
        liabHdr.Top = y : liabHdr.Left = 0
        liabHdr.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        card.Controls.Add(liabHdr)
        y += liabHdr.Height

        Dim liabRows() As (String, Decimal, Boolean, Boolean) = {
            ("Unpaid Purchase Orders", _unpaidPos, False, False),
            ("Total Liabilities", totalLiab, True, True)
        }
        For Each r In liabRows
            Dim row = MakeSheetRow(r.Item1, r.Item2, r.Item3, r.Item4, False)
            row.Top = y : row.Left = 0
            row.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
            card.Controls.Add(row)
            y += row.Height
        Next

        y += 12

        ' ── EQUITY ───────────────────────────────────────────────────────────
        Dim equityHdr As Panel = MakeSheetSectionHeader("Equity")
        equityHdr.Top = y : equityHdr.Left = 0
        equityHdr.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        card.Controls.Add(equityHdr)
        y += equityHdr.Height

        ' Net Equity — highlighted, large, colour by sign
        Dim equityRow = MakeSheetRow("Net Equity  (Assets − Liabilities)",
                                     netEquity, True, True, True)
        equityRow.Top = y : equityRow.Left = 0
        equityRow.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        card.Controls.Add(equityRow)
        y += equityRow.Height

        card.Height = y + 24
        Return card
    End Function

    Private Shared Function MakeSheetSectionHeader(title As String) As Panel
        Dim hdr As New Panel With {
            .Height = 36,
            .BackColor = Color.FromArgb(240, 248, 245)
        }
        AddHandler hdr.Paint,
            Sub(s, e)
                Using br As New SolidBrush(MintPrimary)
                    e.Graphics.FillRectangle(br, New Rectangle(0, 0, 4, hdr.Height))
                End Using
                Using p As New Pen(Color.FromArgb(200, 230, 222), 1)
                    e.Graphics.DrawLine(p, 0, hdr.Height - 1, hdr.Width, hdr.Height - 1)
                End Using
            End Sub

        Dim lbl As New Label With {
            .Text = title.ToUpper(),
            .Font = New Font("Segoe UI Semibold", 9.5F, FontStyle.Bold),
            .ForeColor = MintDeep,
            .AutoSize = True,
            .BackColor = Color.Transparent,
            .Location = New Point(16, (36 - 14) \ 2)
        }
        hdr.Controls.Add(lbl)
        Return hdr
    End Function

    Private Shared Function MakeSheetRow(label As String, value As Decimal,
                                          bold As Boolean, isTotalRow As Boolean,
                                          isEquity As Boolean) As Panel
        Dim h As Integer = If(isEquity, 58, If(isTotalRow, 46, 42))

        Dim row As New Panel With {
            .Height = h,
            .BackColor = If(isEquity, HighlightBg,
                            If(isTotalRow, Color.FromArgb(247, 250, 249),
                               Color.Transparent))
        }

        Dim equityPositive As Boolean = (isEquity AndAlso value >= 0)

        AddHandler row.Paint,
            Sub(s, e)
                Using p As New Pen(RowDivider, If(isTotalRow OrElse isEquity, 1.5F, 1.0F))
                    If isTotalRow OrElse isEquity Then
                        e.Graphics.DrawLine(p, 0, 0, row.Width, 0)
                    End If
                    e.Graphics.DrawLine(p, 0, h - 1, row.Width, h - 1)
                End Using
                ' Left accent bar
                Dim accentClr As Color = If(isEquity,
                    If(equityPositive, MintPrimary, AccentRed),
                    MintPrimary)
                Using br As New SolidBrush(accentClr)
                    e.Graphics.FillRectangle(br, New Rectangle(0, 8, 3, h - 16))
                End Using
            End Sub

        Dim fnt As Font = If(bold OrElse isEquity,
            New Font("Segoe UI Semibold", If(isEquity, 12.0F, 10.5F), FontStyle.Bold),
            New Font("Segoe UI", 10.5F))

        Dim lblColor As Color = If(isEquity, MintDark,
                                   If(bold, Color.FromArgb(45, 45, 45), SubText))
        Dim valColor As Color
        If isEquity Then
            valColor = If(equityPositive, MintPrimary, AccentRed)
        ElseIf bold Then
            valColor = MintDeep
        Else
            valColor = Color.FromArgb(45, 45, 45)
        End If

        Dim lblLabel As New Label With {
            .Text = label,
            .Font = fnt,
            .ForeColor = lblColor,
            .AutoSize = True,
            .BackColor = Color.Transparent
        }
        Dim lblVal As New Label With {
            .Text = Peso & value.ToString("N2"),
            .Font = fnt,
            .ForeColor = valColor,
            .AutoSize = True,
            .BackColor = Color.Transparent
        }

        row.Controls.Add(lblLabel)
        row.Controls.Add(lblVal)

        AddHandler row.Resize,
            Sub()
                Dim midY As Integer = (h - lblLabel.Height) \ 2
                lblLabel.Location = New Point(24, midY)
                lblVal.Location = New Point(row.Width - lblVal.Width - 24, midY)
            End Sub

        Return row
    End Function

    ' =========================================================================
    '  PRINT  — builds RLine list for active tab and calls ReceiptPrinter.Preview
    ' =========================================================================
    Private Shared Sub PrintActiveTab()
        Dim lines As New List(Of ReceiptPrinter.RLine)
        Dim docName As String
        Dim rangeStr As String = FormatRange()

        Select Case _activeTab

            Case TAB_INCOME
                docName = "Income Statement"
                lines.Add(ReceiptPrinter.Section("Income Statement"))
                lines.Add(ReceiptPrinter.Divider())
                lines.Add(New ReceiptPrinter.RLine("Period", rangeStr))
                lines.Add(ReceiptPrinter.Spacer())
                lines.Add(New ReceiptPrinter.RLine("Gross Revenue",
                    Peso & _revenue.ToString("N2")))
                lines.Add(New ReceiptPrinter.RLine("Less: Returns",
                    "(" & Peso & _returns.ToString("N2") & ")"))
                lines.Add(ReceiptPrinter.Divider())
                lines.Add(New ReceiptPrinter.RLine("Net Revenue",
                    Peso & (_revenue - _returns).ToString("N2")))
                lines.Add(New ReceiptPrinter.RLine("Less: Cost of Purchases",
                    "(" & Peso & _poCost.ToString("N2") & ")"))
                lines.Add(ReceiptPrinter.Divider())
                lines.Add(New ReceiptPrinter.RLine("NET INCOME",
                    Peso & (_revenue - _returns - _poCost).ToString("N2")))

            Case TAB_TRIAL
                docName = "Trial Balance"
                Dim totalD As Decimal = _poCost + _returns
                Dim totalC As Decimal = _revenue
                lines.Add(ReceiptPrinter.Section("Trial Balance"))
                lines.Add(ReceiptPrinter.Divider())
                lines.Add(New ReceiptPrinter.RLine("Period", rangeStr))
                lines.Add(ReceiptPrinter.Spacer())
                lines.Add(New ReceiptPrinter.RLine("ACCOUNT", "DEBIT / CREDIT"))
                lines.Add(ReceiptPrinter.Divider())
                lines.Add(New ReceiptPrinter.RLine("Sales Revenue",
                    "CR " & Peso & _revenue.ToString("N2")))
                lines.Add(New ReceiptPrinter.RLine("Purchase Expenses",
                    "DR " & Peso & _poCost.ToString("N2")))
                lines.Add(New ReceiptPrinter.RLine("Returns",
                    "DR " & Peso & _returns.ToString("N2")))
                lines.Add(ReceiptPrinter.Divider())
                lines.Add(New ReceiptPrinter.RLine("Total Debits",
                    Peso & totalD.ToString("N2")))
                lines.Add(New ReceiptPrinter.RLine("Total Credits",
                    Peso & totalC.ToString("N2")))
                lines.Add(ReceiptPrinter.Spacer())
                lines.Add(New ReceiptPrinter.RLine("Status",
                    If(totalD = totalC, "BALANCED", "OUT OF BALANCE")))

            Case TAB_BALANCE
                docName = "Balance Sheet"
                Dim net As Decimal = _revenue - _returns
                Dim totA As Decimal = net + _stockValue
                Dim totL As Decimal = _unpaidPos
                Dim equity As Decimal = totA - totL
                lines.Add(ReceiptPrinter.Section("Balance Sheet"))
                lines.Add(ReceiptPrinter.Divider())
                lines.Add(New ReceiptPrinter.RLine("As of", _dateTo.ToString("MMMM d, yyyy")))
                lines.Add(ReceiptPrinter.Spacer())
                lines.Add(ReceiptPrinter.Section("Assets"))
                lines.Add(New ReceiptPrinter.RLine("Cash from Sales", Peso & net.ToString("N2")))
                lines.Add(New ReceiptPrinter.RLine("Stock Value", Peso & _stockValue.ToString("N2")))
                lines.Add(New ReceiptPrinter.RLine("Total Assets", Peso & totA.ToString("N2")))
                lines.Add(ReceiptPrinter.Spacer())
                lines.Add(ReceiptPrinter.Section("Liabilities"))
                lines.Add(New ReceiptPrinter.RLine("Unpaid POs", Peso & _unpaidPos.ToString("N2")))
                lines.Add(New ReceiptPrinter.RLine("Total Liabilities", Peso & totL.ToString("N2")))
                lines.Add(ReceiptPrinter.Spacer())
                lines.Add(ReceiptPrinter.Section("Equity"))
                lines.Add(ReceiptPrinter.Divider())
                lines.Add(New ReceiptPrinter.RLine("NET EQUITY", Peso & equity.ToString("N2")))

            Case Else
                docName = "Report"
        End Select

        ReceiptPrinter.Preview(docName, lines,
                               receiptNo:="",
                               storeName:="PAINT STORE",
                               storeTagline:=docName)
    End Sub

    ' =========================================================================
    '  SHARED HELPERS
    ' =========================================================================

    ' Shared section heading (card header style — title left, date range right)
    Private Shared Function MakeSectionHeading(title As String,
                                                subtitle As String) As Panel
        Dim hdr As New Panel With {
            .Height = 58,
            .Dock = DockStyle.Top,
            .BackColor = Color.Transparent
        }
        AddHandler hdr.Paint,
            Sub(s, e)
                Using p As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(p, 0, hdr.Height - 1, hdr.Width, hdr.Height - 1)
                End Using
            End Sub

        Dim lblTitle As New Label With {
            .Text = title,
            .Font = New Font("Segoe UI Semibold", 14, FontStyle.Bold),
            .ForeColor = MintDark,
            .AutoSize = True,
            .BackColor = Color.Transparent,
            .Location = New Point(22, 14)
        }
        Dim lblSub As New Label With {
            .Text = subtitle,
            .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = FaintText,
            .AutoSize = True,
            .BackColor = Color.Transparent,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        hdr.Controls.AddRange({lblTitle, lblSub})
        AddHandler hdr.Resize,
            Sub()
                lblSub.Location = New Point(hdr.Width - lblSub.Width - 22,
                                             (hdr.Height - lblSub.Height) \ 2)
            End Sub
        Return hdr
    End Function

    ' Clip a Label to a rounded region
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

    ' Clip a Button to a rounded region
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