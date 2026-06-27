Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports MySql.Data.MySqlClient

' =============================================================================
'  AccountantSalesHistoryPanelBuilder  —  Screen 4: Sales History (Read-Only)
'
'  LAYOUT  (Dock stacking — added in reverse order so Top bars stack down)
'  ------
'    TopBar      (Dock = Top,    64px)  : dark gradient — title + subtitle
'    FilterBar   (Dock = Top,    52px)  : search box (left) + date pills (right)
'    ColHeader   (Dock = Top,    34px)  : column label row
'    TotalsBar   (Dock = Bottom, 48px)  : pinned totals — txns / revenue / discounts
'    ScrollHost  (Dock = Fill)          : scrollable sale rows
'
'  All data sourced from:
'    SELECT s.sale_no, s.sale_datetime, u.full_name, pm.name,
'           s.subtotal, s.discount_amount, s.total, s.status
'    FROM sales s
'    JOIN users u ON s.user_id = u.user_id
'    LEFT JOIN payment_methods pm ON s.method_id = pm.method_id
'    WHERE [date filter] AND (s.sale_no LIKE @q OR u.full_name LIKE @q)
'    ORDER BY s.sale_datetime DESC
' =============================================================================
Public Class AccountantSalesHistoryPanelBuilder

    ' ── PALETTE  (matches AccountantDashPanelBuilder exactly) ─────────────────
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
    Private Shared ReadOnly Peso As String = ChrW(8369)

    ' Status badge colours
    Private Shared ReadOnly BadgeCompleted As (fore As Color, back As Color) =
        (Color.FromArgb(0, 120, 92), Color.FromArgb(220, 248, 240))
    Private Shared ReadOnly BadgeReturned As (fore As Color, back As Color) =
        (Color.FromArgb(180, 40, 40), Color.FromArgb(255, 230, 230))
    Private Shared ReadOnly BadgeVoided As (fore As Color, back As Color) =
        (Color.FromArgb(120, 120, 120), Color.FromArgb(238, 238, 238))

    ' ── DATA MODEL ────────────────────────────────────────────────────────────
    Private Class SaleRecord
        Public SaleNo As String
        Public SaleDate As DateTime
        Public Cashier As String
        Public PayMethod As String
        Public Subtotal As Decimal
        Public Discount As Decimal
        Public Total As Decimal
        Public Status As String
    End Class

    ' ── STATE ─────────────────────────────────────────────────────────────────
    Private Shared _container As Panel
    Private Shared _dateFilter As String = "all"   ' today / week / month / all
    Private Shared _searchQuery As String = ""

    Private Shared _allRows As New List(Of SaleRecord)
    Private Shared _visibleRows As New List(Of SaleRecord)

    ' Live-update references
    Private Shared _scrollHost As Panel
    Private Shared _lblTxns As Label
    Private Shared _lblRevenue As Label
    Private Shared _lblDiscount As Label

    ' ── DATE-FILTER PILL BUTTONS (kept to re-style on switch) ─────────────────
    Private Shared _pillBtns As New List(Of Button)

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Shared Sub LoadSalesHistory(container As Panel,
                                       Optional dateFilter As String = "all")
        _container = container
        _dateFilter = dateFilter
        _searchQuery = ""
        _pillBtns.Clear()

        LoadData()
        Render()
    End Sub

    ' =========================================================================
    '  DATA
    ' =========================================================================
    Private Shared Sub LoadData()
        _allRows.Clear()

        Dim dateWhere As String = BuildDateWhere()

        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Dim sql As String =
                    "SELECT s.sale_no, s.sale_datetime, u.full_name, " &
                    "COALESCE(pm.name,'—') AS method_name, " &
                    "s.subtotal, s.discount_amount, s.total, s.status " &
                    "FROM sales s " &
                    "JOIN users u ON s.user_id = u.user_id " &
                    "LEFT JOIN payment_methods pm ON s.method_id = pm.method_id " &
                    dateWhere &
                    "ORDER BY s.sale_datetime DESC"

                Using cmd As New MySqlCommand(sql, conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            _allRows.Add(New SaleRecord With {
                                .SaleNo = rdr("sale_no").ToString(),
                                .SaleDate = Convert.ToDateTime(rdr("sale_datetime")),
                                .Cashier = rdr("full_name").ToString(),
                                .PayMethod = rdr("method_name").ToString(),
                                .Subtotal = If(IsDBNull(rdr("subtotal")), 0D, Convert.ToDecimal(rdr("subtotal"))),
                                .Discount = If(IsDBNull(rdr("discount_amount")), 0D, Convert.ToDecimal(rdr("discount_amount"))),
                                .Total = If(IsDBNull(rdr("total")), 0D, Convert.ToDecimal(rdr("total"))),
                                .Status = rdr("status").ToString()
                            })
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            Debug.WriteLine("AccountantSalesHistory LoadData error: " & ex.Message)
        End Try

        ApplySearch()
    End Sub

    Private Shared Function BuildDateWhere() As String
        Select Case _dateFilter
            Case "today" : Return "WHERE DATE(s.sale_datetime) = CURDATE() "
            Case "week" : Return "WHERE s.sale_datetime >= CURDATE() - INTERVAL 7 DAY "
            Case "month" : Return "WHERE s.sale_datetime >= CURDATE() - INTERVAL 30 DAY "
            Case Else : Return ""   ' All Time — no WHERE clause
        End Select
    End Function

    ' Filter _allRows by _searchQuery → _visibleRows
    Private Shared Sub ApplySearch()
        _visibleRows.Clear()
        If String.IsNullOrWhiteSpace(_searchQuery) Then
            _visibleRows.AddRange(_allRows)
        Else
            Dim q As String = _searchQuery.ToLower()
            For Each r In _allRows
                If r.SaleNo.ToLower().Contains(q) OrElse r.Cashier.ToLower().Contains(q) Then
                    _visibleRows.Add(r)
                End If
            Next
        End If
    End Sub

    ' =========================================================================
    '  RENDER
    ' =========================================================================
    Private Shared Sub Render()
        _container.SuspendLayout()
        _container.Controls.Clear()
        _container.BackColor = MintBg

        ' Add in reverse Dock order so stacking resolves correctly:
        '   TotalsBar (Bottom) first, then Fill, then Top bars top-to-bottom.

        ' 5. TOTALS BAR — Dock Bottom
        Dim totalsBar As Panel = BuildTotalsBar()
        totalsBar.Dock = DockStyle.Bottom

        ' 4. SCROLL HOST — Dock Fill (added before Top panels so it fills remainder)
        _scrollHost = New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = CardBg,
            .AutoScroll = True,
            .Padding = New Padding(0, 2, 0, 4)
        }

        ' 3. COLUMN HEADER — Dock Top (innermost Top, added last among Tops)
        Dim colHeader As Panel = BuildColumnHeader()
        colHeader.Dock = DockStyle.Top

        ' 2. FILTER BAR — Dock Top
        Dim filterBar As Panel = BuildFilterBar()
        filterBar.Dock = DockStyle.Top

        ' 1. TOP BAR — Dock Top (outermost / first visible)
        Dim topBar As Panel = BuildTopBar()
        topBar.Dock = DockStyle.Top

        ' Add in the order that gives correct Dock stacking:
        ' Fill → Bottom → then Top panels bottom-up (last added = topmost)
        _container.Controls.Add(_scrollHost)
        _container.Controls.Add(totalsBar)
        _container.Controls.Add(colHeader)
        _container.Controls.Add(filterBar)
        _container.Controls.Add(topBar)

        _container.ResumeLayout()

        PopulateRows()
    End Sub

    ' =========================================================================
    '  1. TOP BAR  — dark gradient, 64 px
    ' =========================================================================
    Private Shared Function BuildTopBar() As Panel
        Dim bar As New Panel With {.Height = 64, .BackColor = MintDark}

        AddHandler bar.Paint,
            Sub(s, e)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim rc = New Rectangle(0, 0, bar.Width, bar.Height)
                Using br As New LinearGradientBrush(rc, MintDark, HeaderBg, LinearGradientMode.Horizontal)
                    g.FillRectangle(br, rc)
                End Using
            End Sub

        Dim lblTitle As New Label With {
            .Text = "Sales History",
            .Font = New Font("Segoe UI", 15, FontStyle.Bold),
            .ForeColor = Color.White,
            .BackColor = Color.Transparent,
            .AutoSize = True,
            .Location = New Point(22, 10)
        }

        Dim lblSub As New Label With {
            .Text = "All Transactions  —  Read Only",
            .Font = New Font("Segoe UI", 9),
            .ForeColor = Color.FromArgb(160, 210, 200),
            .BackColor = Color.Transparent,
            .AutoSize = True,
            .Location = New Point(24, 38)
        }

        bar.Controls.AddRange({lblTitle, lblSub})
        Return bar
    End Function

    ' =========================================================================
    '  2. FILTER BAR  — search box left, date pills right, 52 px
    ' =========================================================================
    Private Shared Function BuildFilterBar() As Panel
        Dim bar As New Panel With {
            .Height = 52,
            .BackColor = MintBg,
            .Padding = New Padding(22, 10, 22, 10)
        }

        ' ── Search box ────────────────────────────────────────────────────────
        Dim searchWrap As New Panel With {
            .Size = New Size(240, 32),
            .BackColor = CardBg,
            .Location = New Point(22, 10),
            .Cursor = Cursors.IBeam
        }

        Dim searchFocused As Boolean = False

        AddHandler searchWrap.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim rc = searchWrap.ClientRectangle : rc.Inflate(-1, -1)
                Using b As New SolidBrush(CardBg)
                    e.Graphics.FillRoundedRectangle(b, rc, 8)
                End Using
                Using p As New Pen(If(searchFocused, MintPrimary, RowDivider),
                                   If(searchFocused, 1.8F, 1.0F))
                    e.Graphics.DrawRoundedRectangle(p, rc, 8)
                End Using
                ' Magnifier icon
                Using p As New Pen(Color.FromArgb(160, 180, 178), 1.4F)
                    e.Graphics.DrawEllipse(p, 10, 10, 12, 12)
                    e.Graphics.DrawLine(p, 20, 20, 25, 25)
                End Using
            End Sub

        Dim txtSearch As New TextBox With {
            .Font = New Font("Segoe UI", 10),
            .BorderStyle = BorderStyle.None,
            .BackColor = CardBg,
            .ForeColor = SubText,
            .Text = "Search by sale no. or cashier…",
            .Location = New Point(32, 8),
            .Width = 200
        }

        ' Clear / X button
        Dim btnClear As New Label With {
            .Text = "✕",
            .Font = New Font("Segoe UI", 9),
            .ForeColor = FaintText,
            .BackColor = Color.Transparent,
            .AutoSize = True,
            .Cursor = Cursors.Hand,
            .Visible = False
        }
        AddHandler searchWrap.Resize,
            Sub() btnClear.Location = New Point(searchWrap.Width - btnClear.Width - 8,
                                                 (searchWrap.Height - btnClear.Height) \ 2)
        AddHandler btnClear.Click,
            Sub()
                txtSearch.Text = ""
                txtSearch.ForeColor = SubText
                txtSearch.Text = "Search by sale no. or cashier…"
                btnClear.Visible = False
                searchWrap.Invalidate()
                _searchQuery = ""
                ApplySearch()
                PopulateRows()
            End Sub

        AddHandler txtSearch.Enter,
            Sub()
                searchFocused = True
                If txtSearch.Text = "Search by sale no. or cashier…" Then
                    txtSearch.Text = ""
                    txtSearch.ForeColor = Color.FromArgb(45, 45, 45)
                End If
                searchWrap.Invalidate()
            End Sub

        AddHandler txtSearch.Leave,
            Sub()
                searchFocused = False
                If String.IsNullOrWhiteSpace(txtSearch.Text) Then
                    txtSearch.Text = "Search by sale no. or cashier…"
                    txtSearch.ForeColor = SubText
                    btnClear.Visible = False
                End If
                searchWrap.Invalidate()
            End Sub

        AddHandler txtSearch.TextChanged,
            Sub()
                Dim isPlaceholder As Boolean =
                    (txtSearch.Text = "Search by sale no. or cashier…")
                btnClear.Visible = Not isPlaceholder AndAlso txtSearch.Text.Length > 0
                If isPlaceholder Then Return
                _searchQuery = txtSearch.Text.Trim()
                ApplySearch()
                PopulateRows()
            End Sub

        AddHandler searchWrap.Click, Sub() txtSearch.Focus()

        searchWrap.Controls.Add(txtSearch)
        searchWrap.Controls.Add(btnClear)
        bar.Controls.Add(searchWrap)

        ' ── Date filter pills ─────────────────────────────────────────────────
        Dim labels() As String = {"Today", "This Week", "This Month", "All Time"}
        Dim keys() As String = {"today", "week", "month", "all"}

        Dim pillHost As New FlowLayoutPanel With {
            .FlowDirection = FlowDirection.LeftToRight,
            .AutoSize = True,
            .BackColor = Color.Transparent,
            .WrapContents = False,
            .Anchor = AnchorStyles.Right Or AnchorStyles.Top
        }

        For i As Integer = 0 To labels.Length - 1
            Dim capKey As String = keys(i)
            Dim capLabel As String = labels(i)
            Dim isActive As Boolean = (_dateFilter = capKey)

            Dim pill As New Button With {
                .Text = capLabel,
                .FlatStyle = FlatStyle.Flat,
                .Font = New Font("Segoe UI", 9.5F,
                                      If(isActive, FontStyle.Bold, FontStyle.Regular)),
                .ForeColor = If(isActive, Color.White, SubText),
                .BackColor = If(isActive, MintPrimary, Color.FromArgb(230, 238, 235)),
                .Size = New Size(90, 30),
                .Cursor = Cursors.Hand,
                .Margin = New Padding(0, 0, 6, 0)
            }
            pill.FlatAppearance.BorderSize = 0
            RoundLabel_Btn(pill, 12)
            _pillBtns.Add(pill)

            AddHandler pill.Click,
                Sub()
                    _dateFilter = capKey
                    _searchQuery = ""
                    StylePills(capKey)
                    LoadData()
                    PopulateRows()
                End Sub

            pillHost.Controls.Add(pill)
        Next

        bar.Controls.Add(pillHost)

        AddHandler bar.Resize,
            Sub()
                pillHost.Location = New Point(
                    bar.Width - pillHost.Width - 22,
                    (bar.Height - pillHost.Height) \ 2)
            End Sub

        Return bar
    End Function

    ' Re-apply active / inactive pill styles after a filter click
    Private Shared Sub StylePills(activeKey As String)
        Dim keys() As String = {"today", "week", "month", "all"}
        For i As Integer = 0 To Math.Min(_pillBtns.Count - 1, keys.Length - 1)
            Dim isAct As Boolean = (keys(i) = activeKey)
            _pillBtns(i).BackColor = If(isAct, MintPrimary, Color.FromArgb(230, 238, 235))
            _pillBtns(i).ForeColor = If(isAct, Color.White, SubText)
            _pillBtns(i).Font = New Font("Segoe UI", 9.5F,
                                               If(isAct, FontStyle.Bold, FontStyle.Regular))
            _pillBtns(i).Invalidate()
        Next
    End Sub

    ' =========================================================================
    '  3. COLUMN HEADER  — 34 px
    ' =========================================================================
    Private Shared Function BuildColumnHeader() As Panel
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

        Dim grid As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .ColumnCount = 8,
            .RowCount = 1,
            .BackColor = Color.Transparent,
            .Padding = New Padding(4, 0, 4, 0)
        }
        grid.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        SetColumnStyles(grid)

        Dim hdrTexts() As String = {"Sale No", "Date & Time", "Cashier",
                                               "Payment", "Subtotal", "Discount", "Total", "Status"}
        Dim hdrAligns() As ContentAlignment = {
            ContentAlignment.MiddleLeft, ContentAlignment.MiddleLeft,
            ContentAlignment.MiddleLeft, ContentAlignment.MiddleLeft,
            ContentAlignment.MiddleRight, ContentAlignment.MiddleRight,
            ContentAlignment.MiddleRight, ContentAlignment.MiddleCenter}

        For idx = 0 To hdrTexts.Length - 1
            grid.Controls.Add(New Label With {
                .Text = hdrTexts(idx),
                .Dock = DockStyle.Fill,
                .TextAlign = hdrAligns(idx),
                .Font = New Font("Segoe UI Semibold", 9),
                .ForeColor = Color.FromArgb(122, 152, 144),
                .BackColor = Color.Transparent,
                .Padding = New Padding(If(idx = 0, 18, 4), 0, If(idx = 7, 18, 4), 0)
            }, idx, 0)
        Next

        hdr.Controls.Add(grid)
        Return hdr
    End Function

    ' Shared column proportions — used in header AND each data row
    Private Shared Sub SetColumnStyles(grid As TableLayoutPanel)
        grid.ColumnStyles.Clear()
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 96))   ' Sale No
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 140))   ' Date & Time
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))   ' Cashier (flex)
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 110))   ' Payment
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 90))   ' Subtotal
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 80))   ' Discount
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 90))   ' Total
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 100))   ' Status
    End Sub

    ' =========================================================================
    '  4. SCROLL HOST  — populated separately so search/filter can re-fill it
    ' =========================================================================
    Private Shared Sub PopulateRows()
        If _scrollHost Is Nothing Then Return

        _scrollHost.SuspendLayout()
        _scrollHost.Controls.Clear()

        If _visibleRows.Count = 0 Then
            _scrollHost.Controls.Add(BuildEmptyRow(
                If(String.IsNullOrWhiteSpace(_searchQuery),
                   "No transactions found for the selected period.",
                   $"No results for ""{_searchQuery}"".")))
        Else
            ' Add rows in reverse so DockStyle.Top stacks them top-first
            For i As Integer = _visibleRows.Count - 1 To 0 Step -1
                _scrollHost.Controls.Add(BuildDataRow(_visibleRows(i)))
            Next
        End If

        _scrollHost.ResumeLayout()
        _scrollHost.Invalidate(True)

        UpdateTotals()
    End Sub

    ' ── Single data row ───────────────────────────────────────────────────────
    Private Shared Function BuildDataRow(r As SaleRecord) As Panel
        Const ROW_H As Integer = 48

        Dim row As New BufferedPanel With {
            .Height = ROW_H,
            .Dock = DockStyle.Top,
            .BackColor = Color.Transparent
        }

        AddHandler row.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias

                ' Hover highlight
                If TypeOf row.Tag Is Boolean AndAlso CBool(row.Tag) Then
                    Dim rect As New Rectangle(8, 3, row.Width - 16, ROW_H - 6)
                    Using path As GraphicsPath = RoundRect(rect, 10)
                        Using b As New SolidBrush(RowHover)
                            e.Graphics.FillPath(b, path)
                        End Using
                    End Using
                End If

                ' MintPrimary left accent bar (always visible, per spec)
                Using br As New SolidBrush(MintPrimary)
                    e.Graphics.FillRectangle(br, New Rectangle(0, 10, 3, ROW_H - 20))
                End Using

                ' Row bottom divider
                Using p As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(p, 16, ROW_H - 1, row.Width - 16, ROW_H - 1)
                End Using
            End Sub

        ' Grid matches the column header widths
        Dim grid As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .ColumnCount = 8,
            .RowCount = 1,
            .BackColor = Color.Transparent,
            .Margin = New Padding(0),
            .Padding = New Padding(4, 0, 4, 0)
        }
        grid.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        SetColumnStyles(grid)

        ' Col 0 — Sale No
        grid.Controls.Add(New Label With {
            .Text = r.SaleNo,
            .Dock = DockStyle.Fill,
            .TextAlign = ContentAlignment.MiddleLeft,
            .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold),
            .ForeColor = MintDeep,
            .BackColor = Color.Transparent,
            .Padding = New Padding(18, 0, 4, 0)
        }, 0, 0)

        ' Col 1 — Date & Time
        grid.Controls.Add(New Label With {
            .Text = r.SaleDate.ToString("MM/dd/yy  h:mm tt"),
            .Dock = DockStyle.Fill,
            .TextAlign = ContentAlignment.MiddleLeft,
            .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = SubText,
            .BackColor = Color.Transparent
        }, 1, 0)

        ' Col 2 — Cashier
        grid.Controls.Add(New Label With {
            .Text = r.Cashier,
            .Dock = DockStyle.Fill,
            .TextAlign = ContentAlignment.MiddleLeft,
            .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = Color.FromArgb(45, 45, 45),
            .BackColor = Color.Transparent
        }, 2, 0)

        ' Col 3 — Payment Method
        grid.Controls.Add(New Label With {
            .Text = r.PayMethod,
            .Dock = DockStyle.Fill,
            .TextAlign = ContentAlignment.MiddleLeft,
            .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = SubText,
            .BackColor = Color.Transparent
        }, 3, 0)

        ' Col 4 — Subtotal
        grid.Controls.Add(New Label With {
            .Text = Peso & r.Subtotal.ToString("N2"),
            .Dock = DockStyle.Fill,
            .TextAlign = ContentAlignment.MiddleRight,
            .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = Color.FromArgb(45, 45, 45),
            .BackColor = Color.Transparent
        }, 4, 0)

        ' Col 5 — Discount
        Dim discountFore As Color = If(r.Discount > 0, AccentRed, FaintText)
        grid.Controls.Add(New Label With {
            .Text = If(r.Discount > 0, "−" & Peso & r.Discount.ToString("N2"), "—"),
            .Dock = DockStyle.Fill,
            .TextAlign = ContentAlignment.MiddleRight,
            .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = discountFore,
            .BackColor = Color.Transparent
        }, 5, 0)

        ' Col 6 — Total
        Dim isVoided As Boolean = (r.Status.ToLower() = "voided")
        grid.Controls.Add(New Label With {
            .Text = Peso & r.Total.ToString("N2"),
            .Dock = DockStyle.Fill,
            .TextAlign = ContentAlignment.MiddleRight,
            .Font = New Font("Segoe UI", 9.5F,
                                  If(isVoided, FontStyle.Strikeout, FontStyle.Bold)),
            .ForeColor = If(isVoided, FaintText, MintPrimary),
            .BackColor = Color.Transparent
        }, 6, 0)

        ' Col 7 — Status badge
        Dim badgeColors = GetStatusBadge(r.Status)
        Dim badgeWrap As New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = Color.Transparent
        }
        Dim badge As New Label With {
            .Text = r.Status,
            .Font = New Font("Segoe UI", 8.25F, FontStyle.Bold),
            .ForeColor = badgeColors.fore,
            .BackColor = Color.Transparent,
            .AutoSize = True,
            .Padding = New Padding(8, 3, 8, 3)
        }
        RoundLabel(badge, 10)

        AddHandler badgeWrap.Resize,
            Sub() badge.Location = New Point(
                    Math.Max(0, (badgeWrap.Width - badge.Width) \ 2 - 9),
                    (ROW_H - badge.Height) \ 2)
        AddHandler badgeWrap.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim rc As New Rectangle(
                    Math.Max(0, (badgeWrap.Width - badge.Width) \ 2 - 9),
                    (ROW_H - badge.Height) \ 2,
                    badge.Width, badge.Height)
                Using br As New SolidBrush(badgeColors.back)
                    e.Graphics.FillRoundedRectangle(br, rc, 10)
                End Using
                rc.Inflate(-1, -1)
                Using p As New Pen(badgeColors.fore, 0.6F)
                    e.Graphics.DrawRoundedRectangle(p, rc, 10)
                End Using
            End Sub

        badgeWrap.Controls.Add(badge)
        grid.Controls.Add(badgeWrap, 7, 0)

        row.Controls.Add(grid)

        ' Hover — propagate from grid children too
        PropagateHover(row)
        For Each c As Control In grid.Controls
            AddHandler c.MouseEnter, Sub() SetRowHover(row, True)
            AddHandler c.MouseLeave,
                Sub()
                    Dim pos = row.PointToClient(Cursor.Position)
                    SetRowHover(row, row.ClientRectangle.Contains(pos))
                End Sub
        Next

        Return row
    End Function

    ' Empty-state placeholder
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

    ' =========================================================================
    '  5. TOTALS BAR  — pinned at bottom, 48 px
    ' =========================================================================
    Private Shared Function BuildTotalsBar() As Panel
        Dim bar As New Panel With {
            .Height = 48,
            .BackColor = Color.FromArgb(236, 246, 242)
        }

        AddHandler bar.Paint,
            Sub(s, e)
                Using p As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(p, 0, 0, bar.Width, 0)
                End Using
                Using br As New LinearGradientBrush(
                        New Rectangle(0, 0, bar.Width, bar.Height),
                        Color.FromArgb(230, 244, 240),
                        Color.FromArgb(220, 238, 234),
                        LinearGradientMode.Vertical)
                    e.Graphics.FillRectangle(br, 0, 1, bar.Width, bar.Height - 1)
                End Using
            End Sub

        ' ── Total Transactions ────────────────────────────────────────────────
        Dim lblTxnCap As New Label With {
            .Text = "Total Transactions:",
            .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = SubText,
            .AutoSize = True,
            .BackColor = Color.Transparent,
            .Location = New Point(22, 15)
        }
        _lblTxns = New Label With {
            .Text = "0",
            .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold),
            .ForeColor = MintDark,
            .AutoSize = True,
            .BackColor = Color.Transparent
        }
        AddHandler lblTxnCap.Resize,
            Sub() _lblTxns.Location = New Point(
                    lblTxnCap.Right + 4,
                    lblTxnCap.Top + (lblTxnCap.Height - _lblTxns.Height) \ 2)

        ' ── Total Revenue ────────────────────────────────────────────────────
        Dim lblRevCap As New Label With {
            .Text = "Total Revenue:",
            .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = SubText,
            .AutoSize = True,
            .BackColor = Color.Transparent
        }
        _lblRevenue = New Label With {
            .Text = Peso & "0.00",
            .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold),
            .ForeColor = MintDeep,
            .AutoSize = True,
            .BackColor = Color.Transparent
        }

        ' ── Total Discounts ──────────────────────────────────────────────────
        Dim lblDiscCap As New Label With {
            .Text = "Total Discounts:",
            .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = SubText,
            .AutoSize = True,
            .BackColor = Color.Transparent
        }
        _lblDiscount = New Label With {
            .Text = Peso & "0.00",
            .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold),
            .ForeColor = AccentRed,
            .AutoSize = True,
            .BackColor = Color.Transparent
        }

        bar.Controls.AddRange({lblTxnCap, _lblTxns,
                                lblRevCap, _lblRevenue,
                                lblDiscCap, _lblDiscount})

        ' Position all groups on Resize
        AddHandler bar.Resize,
            Sub()
                Dim midY As Integer = (bar.Height - lblTxnCap.Height) \ 2

                ' Group 1: Transactions (left-anchored)
                lblTxnCap.Location = New Point(22, midY)
                _lblTxns.Location = New Point(lblTxnCap.Right + 4,
                                               midY + (lblTxnCap.Height - _lblTxns.Height) \ 2)

                ' Group 2: Revenue (centre)
                Dim g2W As Integer = lblRevCap.Width + 4 + _lblRevenue.Width
                Dim g2X As Integer = (bar.Width - g2W) \ 2
                lblRevCap.Location = New Point(g2X, midY)
                _lblRevenue.Location = New Point(g2X + lblRevCap.Width + 4,
                                                  midY + (lblRevCap.Height - _lblRevenue.Height) \ 2)

                ' Group 3: Discounts (right-anchored)
                _lblDiscount.Location = New Point(bar.Width - _lblDiscount.Width - 22, midY)
                lblDiscCap.Location = New Point(_lblDiscount.Left - lblDiscCap.Width - 4, midY)
            End Sub

        Return bar
    End Function

    ' Push live totals into the pinned bar
    Private Shared Sub UpdateTotals()
        If _lblTxns Is Nothing Then Return

        Dim count As Integer = _visibleRows.Count
        Dim revenue As Decimal = _visibleRows.Sum(Function(r) r.Total)
        Dim discount As Decimal = _visibleRows.Sum(Function(r) r.Discount)

        _lblTxns.Text = count.ToString("N0")
        _lblRevenue.Text = Peso & revenue.ToString("N2")
        _lblDiscount.Text = Peso & discount.ToString("N2")
    End Sub

    ' =========================================================================
    '  STATUS BADGE COLOURS
    ' =========================================================================
    Private Shared Function GetStatusBadge(status As String) As (fore As Color, back As Color)
        Select Case status.ToLower()
            Case "completed" : Return BadgeCompleted
            Case "returned", "refunded",
                 "partially refunded" : Return BadgeReturned
            Case "voided" : Return BadgeVoided
            Case Else : Return (SubText, Color.FromArgb(238, 238, 238))
        End Select
    End Function

    ' =========================================================================
    '  SHARED HELPERS  (same pattern as AccountantDashPanelBuilder)
    ' =========================================================================

    ' GraphicsPath rounded rectangle (for hover region)
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

    ' Clip a Button to a rounded region (pill date-filter buttons)
    Private Shared Sub RoundLabel_Btn(btn As Button, radius As Integer)
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