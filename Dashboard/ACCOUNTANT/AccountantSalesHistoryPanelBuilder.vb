Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports System.Linq
Imports MySql.Data.MySqlClient

' =============================================================================
'  AccountantSalesHistoryPanelBuilder  —  Screen 4: Sales History (Read-Only)
'
'  LAYOUT  mirrors AccountantIncomePanelBuilder (Document 2):
'  ──────────────────────────────────────────────────────────
'    TopRegion (Dock = Top, 289px total):
'      ├─ TitleBar   (72px)  : "Sales History" title + Search box + Refresh btn
'      ├─ PillBar    (52px)  : date-filter pills (left)
'      └─ KpiBar    (165px)  : 3 KPI cards — Transactions / Revenue / Discounts
'    TableWrapper (Dock = Fill, padded 24px sides + 16px top/bottom):
'      └─ listArea (BufferedPanel, Fill):
'           ├─ ColHeader (Dock = Top,  44px)
'           └─ _scrollHost (Dock = Fill, AutoScroll)
'
'  SQL:
'    SELECT s.sale_no, s.sale_datetime, u.full_name, pm.name,
'           s.subtotal, s.discount_amount, s.total, s.status
'    FROM sales s
'    JOIN users u ON s.user_id = u.user_id
'    LEFT JOIN payment_methods pm ON s.method_id = pm.method_id
'    WHERE [date filter] AND (s.sale_no LIKE @q OR u.full_name LIKE @q)
'    ORDER BY s.sale_datetime DESC
' =============================================================================
Public Class AccountantSalesHistoryPanelBuilder

    ' ── PALETTE  (matches AccountantIncomePanelBuilder exactly) ───────────────
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
    Private Shared _dateFilter As String = "all"
    Private Shared _searchQuery As String = ""

    Private Shared _allRows As New List(Of SaleRecord)
    Private Shared _visibleRows As New List(Of SaleRecord)

    Private Shared _scrollHost As Panel
    Private Shared _pillBtns As New List(Of Button)
    Private Shared _activeFilterIdx As Integer = 3
    Private Shared _reflowAttached As Boolean = False

    ' ── PAGING ───────────────────────────────────────────────────────────────
    Private Const PAGE_SIZE As Integer = 50
    Private Shared _currentPage As Integer = 0
    Private Shared _lblPageInfo As Label
    Private Shared _btnPrev As Button
    Private Shared _btnNext As Button

    Private Shared ReadOnly FilterKeys() As String = {"today", "week", "month", "all"}
    Private Shared ReadOnly FilterLabels() As String = {"Today", "This Week", "This Month", "All Time"}

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Shared Sub LoadSalesHistory(container As Panel,
                                       Optional dateFilter As String = "all")
        _container = container
        _dateFilter = dateFilter
        _searchQuery = ""
        _pillBtns.Clear()
        _currentPage = 0
        Dim idx As Integer = Array.IndexOf(FilterKeys, dateFilter)
        _activeFilterIdx = If(idx >= 0, idx, 3)

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
            Case Else : Return ""
        End Select
    End Function

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
    '  RENDER  — mirrors Document 2 structure exactly
    ' =========================================================================
    Private Shared Sub Render()
        _container.SuspendLayout()
        _container.Controls.Clear()
        _reflowAttached = False
        _container.BackColor = MintBg

        ' ── Table wrapper (Fill) with 24px side spacers + 16px top/bottom ─────
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

        _scrollHost = New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = Color.Transparent,
            .AutoScroll = True,
            .Padding = New Padding(0, 4, 0, 8)
        }

        Dim colHdr As Panel = BuildColHeader()
        colHdr.Dock = DockStyle.Top

        Dim pagerBar As Panel = BuildPagerBar()
        pagerBar.Dock = DockStyle.Bottom

        listArea.Controls.Add(pagerBar)
        listArea.Controls.Add(colHdr)
        listArea.Controls.Add(_scrollHost)
        colHdr.BringToFront()
        _scrollHost.BringToFront()
        _scrollHost.AutoScrollMinSize = New Size(0, 1)

        Dim spacerBottom As New Panel With {.Dock = DockStyle.Bottom, .Height = 16, .BackColor = MintBg}
        Dim spacerTop As New Panel With {.Dock = DockStyle.Top, .Height = 16, .BackColor = MintBg}
        Dim spacerLeft As New Panel With {.Dock = DockStyle.Left, .Width = 24, .BackColor = MintBg}
        Dim spacerRight As New Panel With {.Dock = DockStyle.Right, .Width = 24, .BackColor = MintBg}

        tableWrapper.Controls.Add(listArea)
        tableWrapper.Controls.Add(spacerBottom)
        tableWrapper.Controls.Add(spacerTop)
        tableWrapper.Controls.Add(spacerLeft)
        tableWrapper.Controls.Add(spacerRight)

        ' ── Top region (mirrors Document 2's BuildTopRegion) ──────────────────
        Dim topRegion As Panel = BuildTopRegion()
        topRegion.Dock = DockStyle.Top

        _container.Controls.Add(tableWrapper)
        _container.Controls.Add(topRegion)

        _container.ResumeLayout()

        PopulateRows()
    End Sub

    ' =========================================================================
    '  TOP REGION  —  72 + 52 + 165 = 289 px  (mirrors Document 2)
    ' =========================================================================
    Private Shared Function BuildTopRegion() As Panel
        Const SIDE_PAD As Integer = 24

        Dim region As New Panel With {
            .BackColor = MintBg,
            .AutoSize = False,
            .Height = 289
        }

        ' ── Zone 1: Title bar (72px) — title left, search + refresh right ─────
        Dim titleBar As New Panel With {
            .Height = 72,
            .Dock = DockStyle.Top,
            .BackColor = Color.Transparent
        }

        Dim lblTitle As New Label With {
            .Text = "Sales History",
            .Font = New Font("Segoe UI", 28, FontStyle.Bold),
            .ForeColor = MintDark,
            .AutoSize = True,
            .Location = New Point(SIDE_PAD, 16)
        }

        ' Refresh button — mirrors Document 2 style exactly
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
        AddHandler btnRefresh.Click, Sub() LoadSalesHistory(_container, _dateFilter)

        ' Search box — sits left of Refresh in the title bar
        Dim searchWrap As New Panel With {
            .Size = New Size(240, 34),
            .BackColor = CardBg,
            .Cursor = Cursors.IBeam,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
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
            .Location = New Point(34, 9),
            .Width = 196
        }

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
                Dim isPlaceholder As Boolean = (txtSearch.Text = "Search by sale no. or cashier…")
                btnClear.Visible = Not isPlaceholder AndAlso txtSearch.Text.Length > 0
                If isPlaceholder Then Return
                _searchQuery = txtSearch.Text.Trim()
                ApplySearch()
                PopulateRows()
            End Sub

        AddHandler searchWrap.Click, Sub() txtSearch.Focus()

        searchWrap.Controls.Add(txtSearch)
        searchWrap.Controls.Add(btnClear)

        ' Position search + refresh on title bar resize
        AddHandler titleBar.Resize,
            Sub()
                btnRefresh.Left = titleBar.Width - btnRefresh.Width - SIDE_PAD
                btnRefresh.Top = (titleBar.Height - btnRefresh.Height) \ 2
                searchWrap.Left = btnRefresh.Left - searchWrap.Width - 10
                searchWrap.Top = (titleBar.Height - searchWrap.Height) \ 2
            End Sub

        titleBar.Controls.Add(lblTitle)
        titleBar.Controls.Add(searchWrap)
        titleBar.Controls.Add(btnRefresh)

        ' ── Zone 2: Pill/filter bar (52px) — date pills left ─────────────────
        Dim pillBar As New Panel With {
            .Height = 52,
            .Dock = DockStyle.Top,
            .BackColor = Color.Transparent
        }

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
            Dim isActive As Boolean = (i = _activeFilterIdx)

            Dim pill As New Button With {
                .Text = FilterLabels(i),
                .FlatStyle = FlatStyle.Flat,
                .Cursor = Cursors.Hand,
                .Size = New Size(96, 34),
                .Margin = New Padding(0, 0, 6, 0)
            }
            pill.FlatAppearance.BorderSize = 0

            Dim styleBtn As Action(Of Boolean, Boolean) =
                Sub(active As Boolean, hover As Boolean)
                    If active Then
                        pill.BackColor = MintPrimary
                        pill.ForeColor = Color.White
                        pill.Font = New Font("Segoe UI Semibold", 9.5F)
                    ElseIf hover Then
                        pill.BackColor = Color.FromArgb(210, 240, 235)
                        pill.ForeColor = MintDark
                        pill.Font = New Font("Segoe UI", 9.5F)
                    Else
                        pill.BackColor = Color.FromArgb(220, 235, 232)
                        pill.ForeColor = SubText
                        pill.Font = New Font("Segoe UI", 9.5F)
                    End If
                    pill.Invalidate()
                End Sub

            AddHandler pill.Paint,
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

            AddHandler pill.MouseEnter, Sub() If idx <> _activeFilterIdx Then styleBtn(False, True)
            AddHandler pill.MouseLeave, Sub() If idx <> _activeFilterIdx Then styleBtn(False, False)
            AddHandler pill.Click,
                Sub()
                    _dateFilter = capKey
                    _activeFilterIdx = idx
                    _searchQuery = ""
                    LoadSalesHistory(_container, capKey)
                End Sub

            styleBtn(isActive, False)
            _pillBtns.Add(pill)
            filterHost.Controls.Add(pill)
        Next

        pillBar.Controls.Add(filterHost)

        ' ── Zone 3: KPI cards bar (165px) ─────────────────────────────────────
        Dim kpiBar As Panel = BuildKpiBar()
        kpiBar.Dock = DockStyle.Top

        ' Stack zones: kpiBar added first (innermost Top), then pillBar, then titleBar
        region.Controls.Add(kpiBar)
        region.Controls.Add(pillBar)
        region.Controls.Add(titleBar)

        Return region
    End Function

    ' =========================================================================
    '  KPI BAR  —  165px, 3 cards: Transactions / Revenue / Discounts
    '  Mirrors Document 2's BuildKpiBar / MakeKpiCard exactly
    ' =========================================================================
    Private Shared Function BuildKpiBar() As Panel
        Const SIDE_PAD As Integer = 24

        Dim txnCount As Integer = _visibleRows.Count
        Dim totalRevenue As Decimal = _visibleRows.Sum(Function(r) r.Total)
        Dim totalDiscount As Decimal = _visibleRows.Sum(Function(r) r.Discount)

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

        Dim c1 = MakeKpiCard("Total Transactions",
                              txnCount.ToString("N0"),
                              MintPrimary,
                              "All transactions in selected period")
        Dim c2 = MakeKpiCard("Total Revenue",
                              Peso & totalRevenue.ToString("N2"),
                              MintDeep,
                              "Sum of all transaction totals")
        Dim c3 = MakeKpiCard("Total Discounts",
                              Peso & totalDiscount.ToString("N2"),
                              AccentRed,
                              "Sum of all discounts applied")

        c1.Margin = New Padding(0, 0, 6, 0)
        c2.Margin = New Padding(6, 0, 6, 0)
        c3.Margin = New Padding(6, 0, 0, 0)

        kpiRow.Controls.Add(c1, 0, 0)
        kpiRow.Controls.Add(c2, 1, 0)
        kpiRow.Controls.Add(c3, 2, 0)

        bar.Controls.Add(kpiRow)
        Return bar
    End Function

    ' Mirrors Document 2's MakeKpiCard exactly
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

    ' =========================================================================
    '  COLUMN HEADER  —  44px, label-based proportional layout (mirrors Doc 2)
    ' =========================================================================
    Private Shared Function BuildColHeader() As Panel
        Dim hdr As New Panel With {.Height = 44, .BackColor = Color.Transparent}

        Dim labels As Label() = {
            MakeColLabel("SALE NO", New Point(24, 13)),
            MakeColLabel("DATE & TIME", New Point(0, 13)),
            MakeColLabel("CASHIER", New Point(0, 13)),
            MakeColLabel("PAYMENT", New Point(0, 13)),
            MakeColLabel("SUBTOTAL", New Point(0, 13)),
            MakeColLabel("DISCOUNT", New Point(0, 13)),
            MakeColLabel("TOTAL", New Point(0, 13)),
            MakeColLabel("STATUS", New Point(0, 13))
        }
        hdr.Controls.AddRange(labels)

        AddHandler hdr.Resize,
            Sub()
                Dim w = hdr.Width
                labels(0).Left = 24
                labels(1).Left = CInt(w * 0.13)
                labels(2).Left = CInt(w * 0.26)
                labels(3).Left = CInt(w * 0.46)
                labels(4).Left = CInt(w * 0.58)
                labels(5).Left = CInt(w * 0.68)
                labels(6).Left = CInt(w * 0.77)
                labels(7).Left = CInt(w * 0.87)
            End Sub

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
    '  POPULATE ROWS  —  fills _scrollHost
    ' =========================================================================
    Private Shared Sub PopulateRows()
        If _scrollHost Is Nothing Then Return

        _scrollHost.SuspendLayout()
        _scrollHost.Controls.Clear()

        If _reflowAttached Then
            RemoveHandler _scrollHost.Resize, AddressOf ReflowScrollHost
            _reflowAttached = False
        End If

        If _visibleRows.Count = 0 Then
            _scrollHost.Controls.Add(BuildEmptyRow(
                If(String.IsNullOrWhiteSpace(_searchQuery),
                   "No transactions found for the selected period.",
                   $"No results for ""{_searchQuery}"".")))
            UpdatePager(0)
        Else
            Dim pageRows = _visibleRows.Skip(_currentPage * PAGE_SIZE).Take(PAGE_SIZE).ToList()
            Dim y As Integer = 4
            For Each r As SaleRecord In pageRows
                Dim row As Panel = BuildDataRow(r, 64)
                row.Location = New Point(0, y)
                row.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
                _scrollHost.Controls.Add(row)
                row.Width = _scrollHost.ClientSize.Width - 4
                y += 64
            Next

            If Not _reflowAttached Then
                _reflowAttached = True
                AddHandler _scrollHost.Resize, AddressOf ReflowScrollHost
            End If

            UpdatePager(_visibleRows.Count)
        End If

        _scrollHost.ResumeLayout()
        _scrollHost.Invalidate(True)
    End Sub

    Private Shared Sub ReflowScrollHost(sender As Object, e As EventArgs)
        ReflowRows(_scrollHost)
    End Sub

    ' ── Single data row  (height 64px, matches Document 2 row height) ─────────
    Private Shared Function BuildDataRow(r As SaleRecord, height As Integer) As Panel
        Dim p As New BufferedPanel With {
            .Height = height,
            .BackColor = Color.Transparent,
            .Cursor = Cursors.Hand
        }

        AddHandler p.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                If TypeOf p.Tag Is Boolean AndAlso CBool(p.Tag) Then
                    Dim rect As New Rectangle(8, 3, p.Width - 16, height - 6)
                    Using path As GraphicsPath = RoundRect(rect, 10)
                        Using b As New SolidBrush(RowHover)
                            e.Graphics.FillPath(b, path)
                        End Using
                    End Using
                End If
                ' Row bottom divider
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 20, p.Height - 1, p.Width - 20, p.Height - 1)
                End Using
            End Sub

        ' ── Labels ───────────────────────────────────────────────────────────
        Dim lblNo As New Label With {
            .Text = r.SaleNo,
            .Font = New Font("Segoe UI", 11, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblDate As New Label With {
            .Text = r.SaleDate.ToString("MM/dd/yy  h:mm tt"),
            .Font = New Font("Segoe UI", 11),
            .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblCashier As New Label With {
            .Text = r.Cashier,
            .Font = New Font("Segoe UI", 11),
            .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblPayment As New Label With {
            .Text = r.PayMethod,
            .Font = New Font("Segoe UI", 11),
            .ForeColor = FaintText, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblSubtotal As New Label With {
            .Text = Peso & r.Subtotal.ToString("N2"),
            .Font = New Font("Segoe UI", 11),
            .ForeColor = Color.FromArgb(45, 45, 45),
            .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim discFore As Color = If(r.Discount > 0, AccentRed, FaintText)
        Dim lblDiscount As New Label With {
            .Text = If(r.Discount > 0, "−" & Peso & r.Discount.ToString("N2"), "—"),
            .Font = New Font("Segoe UI", 11),
            .ForeColor = discFore, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim isVoided As Boolean = (r.Status.ToLower() = "voided")
        Dim lblTotal As New Label With {
            .Text = Peso & r.Total.ToString("N2"),
            .Font = New Font("Segoe UI", 12,
                             If(isVoided, FontStyle.Strikeout Or FontStyle.Bold, FontStyle.Bold)),
            .ForeColor = If(isVoided, FaintText, MintPrimary),
            .AutoSize = True,
            .BackColor = Color.Transparent
        }

        ' Status badge
        Dim badgeColors = GetStatusBadge(r.Status)
        Dim badge As New Label With {
            .Text = r.Status,
            .Font = New Font("Segoe UI", 9, FontStyle.Bold),
            .ForeColor = badgeColors.fore,
            .BackColor = badgeColors.back,
            .AutoSize = True,
            .Padding = New Padding(10, 4, 10, 4)
        }
        RoundLabel(badge, 10)

        p.Controls.AddRange({lblNo, lblDate, lblCashier, lblPayment,
                              lblSubtotal, lblDiscount, lblTotal, badge})

        AddHandler p.Resize,
            Sub()
                Dim w = p.Width
                Dim midY As Integer = (height - lblNo.Height) \ 2

                lblNo.Left = 24 : lblNo.Top = midY
                lblDate.Left = CInt(w * 0.13) : lblDate.Top = (height - lblDate.Height) \ 2
                lblCashier.Left = CInt(w * 0.26) : lblCashier.Top = (height - lblCashier.Height) \ 2
                lblPayment.Left = CInt(w * 0.46) : lblPayment.Top = (height - lblPayment.Height) \ 2
                lblSubtotal.Left = CInt(w * 0.58) : lblSubtotal.Top = (height - lblSubtotal.Height) \ 2
                lblDiscount.Left = CInt(w * 0.68) : lblDiscount.Top = (height - lblDiscount.Height) \ 2
                lblTotal.Left = CInt(w * 0.77) : lblTotal.Top = (height - lblTotal.Height) \ 2
                badge.Left = CInt(w * 0.87) : badge.Top = (height - badge.Height) \ 2
            End Sub

        ' Row click → AppModal detail view (mirrors Income panel)
        Dim openDetail As Action =
            Sub()
                Dim sb As New System.Text.StringBuilder()
                sb.AppendLine($"Sale No   : {r.SaleNo}")
                sb.AppendLine($"Date      : {r.SaleDate:MMM dd, yyyy  hh:mm tt}")
                sb.AppendLine($"Cashier   : {r.Cashier}")
                sb.AppendLine($"Payment   : {r.PayMethod}")
                sb.AppendLine($"Subtotal  : {Peso}{r.Subtotal:N2}")
                sb.AppendLine($"Discount  : {If(r.Discount > 0, Peso & r.Discount.ToString("N2"), "—")}")
                sb.AppendLine($"Total     : {Peso}{r.Total:N2}")
                sb.AppendLine($"Status    : {r.Status}")
                AppModal.Show(sb.ToString(),
                              caption:=$"Sale Details — {r.SaleNo}",
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
    '  PAGER BAR  —  mirrors AccountantIncomePanelBuilder exactly
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
                Dim totalPages As Integer = Math.Max(1, CInt(Math.Ceiling(_visibleRows.Count / PAGE_SIZE)))
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
        If _lblPageInfo.Parent IsNot Nothing Then
            _lblPageInfo.Parent.PerformLayout()
        End If
    End Sub

    Private Shared Sub ReflowRows(host As Panel)
        If host Is Nothing Then Return
        Dim w As Integer = host.ClientSize.Width - 4
        For Each c As Control In host.Controls
            c.Width = w
        Next
    End Sub

    ' Empty-state placeholder
    Private Shared Function BuildEmptyRow(message As String) As Panel
        Dim p As New Panel With {
            .Height = 90, .Dock = DockStyle.Top, .BackColor = Color.Transparent
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
    '  STATUS BADGE
    ' =========================================================================
    Private Shared Function GetStatusBadge(status As String) As (fore As Color, back As Color)
        Select Case status.ToLower()
            Case "completed" : Return BadgeCompleted
            Case "returned", "refunded", "partially refunded" : Return BadgeReturned
            Case "voided" : Return BadgeVoided
            Case Else : Return (SubText, Color.FromArgb(238, 238, 238))
        End Select
    End Function

    ' =========================================================================
    '  SHARED HELPERS
    ' =========================================================================
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