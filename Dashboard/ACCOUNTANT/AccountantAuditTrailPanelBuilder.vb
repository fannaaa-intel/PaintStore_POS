Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports System.Linq
Imports MySql.Data.MySqlClient

' =============================================================================
'  AccountantAuditTrailPanelBuilder  —  Audit Trail (Read-Only)
'
'  LAYOUT  (mirrors AccountantSalesHistoryPanelBuilder Document 2 EXACTLY)
'  ──────────────────────────────────────────────────────────────────────────────
'    TopRegion    (Dock = Top,  289px)
'      ├─ TitleBar  (72px) : "Audit Trail" 28pt MintDark + search box + Refresh
'      ├─ PillBar   (52px) : date pills (left) · module pills (right)
'      └─ KpiBar   (165px) : 3 KPI cards — Total Events / POS / Procurement
'    TableWrapper (Dock = Fill)
'      spacers: Left 24px, Right 24px, Top 16px, Bottom 16px
'      └─ listArea (BufferedPanel + PanelRenderer card shadow)
'           ├─ ColHeader  (Dock = Top,  44px)
'           ├─ _scrollHost (Dock = Fill, AutoScroll)
'           └─ SummaryBar (Dock = Bottom, 44px) — INSIDE container
'
'  Modules:
'    POS         : sales JOIN users JOIN payment_methods
'    POS         : returns JOIN users JOIN sales
'    Inventory   : stock_movements JOIN users JOIN products (Adjustment)
'    Procurement : purchase_orders JOIN users JOIN suppliers
'    Procurement : purchase_requests JOIN users (requester + reviewer)
' =============================================================================
Public Class AccountantAuditTrailPanelBuilder

    ' ── PALETTE  (matches AccountantSalesHistoryPanelBuilder exactly) ─────────
    Private Shared ReadOnly MintDark As Color = Color.FromArgb(10, 79, 66)
    Private Shared ReadOnly MintPrimary As Color = Color.FromArgb(0, 168, 142)
    Private Shared ReadOnly MintDeep As Color = Color.FromArgb(0, 130, 109)
    Private Shared ReadOnly MintBg As Color = Color.FromArgb(244, 248, 246)
    Private Shared ReadOnly CardBg As Color = Color.White
    Private Shared ReadOnly SubText As Color = Color.FromArgb(90, 114, 107)
    Private Shared ReadOnly FaintText As Color = Color.FromArgb(150, 165, 160)
    Private Shared ReadOnly RowDivider As Color = Color.FromArgb(238, 242, 240)
    Private Shared ReadOnly RowHover As Color = Color.FromArgb(224, 242, 235)
    Private Shared ReadOnly AccentRed As Color = Color.FromArgb(210, 60, 60)
    Private Shared ReadOnly Peso As String = ChrW(8369)

    ' Module badge colours
    Private Shared ReadOnly BadgePOS As (fore As Color, back As Color) =
        (Color.FromArgb(0, 120, 92), Color.FromArgb(220, 248, 240))
    Private Shared ReadOnly BadgeInventory As (fore As Color, back As Color) =
        (Color.FromArgb(140, 80, 0), Color.FromArgb(255, 240, 210))
    Private Shared ReadOnly BadgeProcurement As (fore As Color, back As Color) =
        (Color.FromArgb(30, 90, 180), Color.FromArgb(225, 235, 255))

    ' Filter key / label arrays
    Private Shared ReadOnly FilterKeys() As String = {"today", "week", "month", "all"}
    Private Shared ReadOnly FilterLabels() As String = {"Today", "This Week", "This Month", "All Time"}

    ' ── DATA MODEL ────────────────────────────────────────────────────────────
    Private Class AuditRow
        Public EventDate As DateTime
        Public UserName As String
        Public UserRole As String
        Public [Module] As String   ' "POS", "Inventory", "Procurement"
        Public Action As String
        Public Detail As String
    End Class

    ' ── STATE ─────────────────────────────────────────────────────────────────
    Private Shared _container As Panel
    Private Shared _dateFilter As String = "all"
    Private Shared _activeFilterIdx As Integer = 3
    Private Shared _searchQuery As String = ""
    Private Shared _moduleFilter As String = "all"
    Private Shared _kpiPanels(3) As Panel

    Private Shared _allRows As New List(Of AuditRow)
    Private Shared _visibleRows As New List(Of AuditRow)

    ' Live-update references
    Private Shared _scrollHost As Panel
    Private Shared _reflowAttached As Boolean = False

    ' Pill references for restyling
    Private Shared _datePills() As Button

    ' Pager
    Private Const PAGE_SIZE As Integer = 50
    Private Shared _currentPage As Integer = 0
    Private Shared _lblPageInfo As Label
    Private Shared _btnPrev As Button
    Private Shared _btnNext As Button

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Shared Sub LoadAuditTrail(container As Panel,
                                     Optional dateFilter As String = "all")
        _container = container
        _dateFilter = dateFilter
        Dim idx As Integer = Array.IndexOf(FilterKeys, dateFilter)
        _activeFilterIdx = If(idx >= 0, idx, 3)
        _searchQuery = ""
        _moduleFilter = "all"
        _reflowAttached = False
        _currentPage = 0

        LoadData()
        Render()
    End Sub

    ' =========================================================================
    '  DATA  — unions five financial event sources mapped to three modules
    ' =========================================================================
    Private Shared Sub LoadData()
        _allRows.Clear()

        Try
            Using conn = Db.GetConnection()
                conn.Open()

                ' ── 1. POS: SALES ─────────────────────────────────────────────
                ReadRows(conn,
                    "SELECT s.sale_datetime AS event_date, " &
                    "       u.full_name AS user_name, " &
                    "       r.role_name AS user_role, " &
                    "       'POS' AS module, " &
                    "       'Sale Completed' AS action, " &
                    "       CONCAT(s.sale_no, ' · ', COALESCE(pm.name,'Cash'), " &
                    "              ' · " & Peso & "', FORMAT(s.total,2)) AS detail " &
                    "FROM sales s " &
                    "JOIN users u ON s.user_id = u.user_id " &
                    "JOIN roles r ON u.role_id = r.role_id " &
                    "LEFT JOIN payment_methods pm ON s.method_id = pm.method_id " &
                    "WHERE s.status = 'Completed' " &
                    BuildDateWhereCol("s.sale_datetime").Replace("WHERE ", "AND "),
                    "POS")

                ' ── 2. POS: RETURNS ───────────────────────────────────────────
                ReadRows(conn,
                    "SELECT rt.return_datetime AS event_date, " &
                    "       u.full_name AS user_name, " &
                    "       ro.role_name AS user_role, " &
                    "       'POS' AS module, " &
                    "       'Return Processed' AS action, " &
                    "       CONCAT(rt.return_no, ' · Ref: ', s.sale_no, " &
                    "              ' · " & Peso & "', FORMAT(rt.total_refund,2)) AS detail " &
                    "FROM returns rt " &
                    "JOIN users u ON rt.processed_by = u.user_id " &
                    "JOIN roles ro ON u.role_id = ro.role_id " &
                    "JOIN sales s ON rt.sale_id = s.sale_id " &
                    BuildDateWhereCol("rt.return_datetime"),
                    "POS")

                ' ── 3. INVENTORY: STOCK ADJUSTMENTS ──────────────────────────
                ReadRows(conn,
                    "SELECT sm.created_at AS event_date, " &
                    "       COALESCE(u.full_name,'System') AS user_name, " &
                    "       COALESCE(ro.role_name,'—') AS user_role, " &
                    "       'Inventory' AS module, " &
                    "       'Stock Adjustment' AS action, " &
                    "       CONCAT(p.name, ' · ', " &
                    "              IF(sm.qty_change > 0, '+',''), sm.qty_change, ' units', " &
                    "              IF(sm.note IS NOT NULL AND sm.note <> '', " &
                    "                 CONCAT(' · ', sm.note), '')) AS detail " &
                    "FROM stock_movements sm " &
                    "LEFT JOIN users u ON sm.user_id = u.user_id " &
                    "LEFT JOIN roles ro ON u.role_id = ro.role_id " &
                    "JOIN products p ON sm.product_id = p.product_id " &
                    "WHERE sm.movement_type = 'Adjustment' " &
                    BuildDateWhereCol("sm.created_at").Replace("WHERE ", "AND "),
                    "Inventory")

                ' ── 4. PROCUREMENT: PURCHASE ORDERS ──────────────────────────
                ReadRows(conn,
                    "SELECT po.order_date AS event_date, " &
                    "       u.full_name AS user_name, " &
                    "       ro.role_name AS user_role, " &
                    "       'Procurement' AS module, " &
                    "       CONCAT('PO ', po.status) AS action, " &
                    "       CONCAT(po.po_no, ' · ', sup.name, " &
                    "              ' · " & Peso & "', FORMAT(po.total_cost,2)) AS detail " &
                    "FROM purchase_orders po " &
                    "JOIN users u ON po.created_by = u.user_id " &
                    "JOIN roles ro ON u.role_id = ro.role_id " &
                    "JOIN suppliers sup ON po.supplier_id = sup.supplier_id " &
                    BuildDateWhereCol("po.order_date"),
                    "Procurement")

                ' ── 5. PROCUREMENT: PURCHASE REQUESTS ────────────────────────
                ReadRows(conn,
                    "SELECT pr.reviewed_at AS event_date, " &
                    "       COALESCE(rv.full_name,'—') AS user_name, " &
                    "       COALESCE(ro.role_name,'—') AS user_role, " &
                    "       'Procurement' AS module, " &
                    "       CONCAT('Request ', pr.status) AS action, " &
                    "       CONCAT(p.name, ' · Qty: ', pr.qty_requested, " &
                    "              ' · By: ', req.full_name) AS detail " &
                    "FROM purchase_requests pr " &
                    "JOIN products p ON pr.product_id = p.product_id " &
                    "JOIN users req ON pr.requested_by = req.user_id " &
                    "LEFT JOIN users rv ON pr.reviewed_by = rv.user_id " &
                    "LEFT JOIN roles ro ON rv.role_id = ro.role_id " &
                    "WHERE pr.reviewed_at IS NOT NULL " &
                    BuildDateWhereCol("pr.reviewed_at").Replace("WHERE ", "AND "),
                    "Procurement")

            End Using
        Catch ex As Exception
            Debug.WriteLine("AuditTrail LoadData error: " & ex.Message)
        End Try

        _allRows.Sort(Function(a, b) b.EventDate.CompareTo(a.EventDate))
        ApplyFilters()
    End Sub

    Private Shared Sub ReadRows(conn As MySqlConnection, sql As String, moduleName As String)
        Try
            Using cmd As New MySqlCommand(sql, conn)
                Using rdr = cmd.ExecuteReader()
                    While rdr.Read()
                        If IsDBNull(rdr("event_date")) Then Continue While
                        _allRows.Add(New AuditRow With {
                            .EventDate = Convert.ToDateTime(rdr("event_date")),
                            .UserName = rdr("user_name").ToString(),
                            .UserRole = rdr("user_role").ToString(),
                            .[Module] = moduleName,
                            .Action = rdr("action").ToString(),
                            .Detail = rdr("detail").ToString()
                        })
                    End While
                End Using
            End Using
        Catch ex As Exception
            Debug.WriteLine($"AuditTrail ReadRows ({moduleName}): " & ex.Message)
        End Try
    End Sub

    Private Shared Function BuildDateWhereCol(col As String) As String
        Select Case _dateFilter
            Case "today" : Return $"WHERE DATE({col}) = CURDATE() "
            Case "week" : Return $"WHERE {col} >= CURDATE() - INTERVAL 7 DAY "
            Case "month" : Return $"WHERE {col} >= CURDATE() - INTERVAL 30 DAY "
            Case Else : Return ""
        End Select
    End Function

    Private Shared Sub ApplyFilters()
        _visibleRows.Clear()
        For Each r In _allRows
            If _moduleFilter <> "all" AndAlso r.[Module] <> _moduleFilter Then Continue For
            If Not String.IsNullOrWhiteSpace(_searchQuery) Then
                Dim q As String = _searchQuery.ToLower()
                If Not (r.UserName.ToLower().Contains(q) OrElse
                        r.Action.ToLower().Contains(q) OrElse
                        r.Detail.ToLower().Contains(q)) Then Continue For
            End If
            _visibleRows.Add(r)
        Next
    End Sub

    ' =========================================================================
    '  RENDER  — mirrors AccountantSalesHistoryPanelBuilder exactly
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
            .Padding = New Padding(0, 4, 0, 0)
        }

        Dim colHdr As Panel = BuildColHeader()
        colHdr.Dock = DockStyle.Top

        ' Pager bar INSIDE the container (Dock = Bottom)
        Dim pagerBar As Panel = BuildPagerBar()
        pagerBar.Dock = DockStyle.Bottom

        listArea.Controls.Add(pagerBar)     ' Bottom — reserves space first
        listArea.Controls.Add(colHdr)       ' Top — reserves space second  
        listArea.Controls.Add(_scrollHost)  ' Fill — takes remaining middle space
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

        ' ── Top region ────────────────────────────────────────────────────────
        Dim topRegion As Panel = BuildTopRegion()
        topRegion.Dock = DockStyle.Top

        _container.Controls.Add(tableWrapper)
        _container.Controls.Add(topRegion)

        _container.ResumeLayout()

        PopulateRows()
    End Sub

    ' =========================================================================
    '  TOP REGION  (72px title + 52px pills + 165px KPI = 289px)
    '  Mirrors AccountantSalesHistoryPanelBuilder.BuildTopRegion EXACTLY
    ' =========================================================================
    Private Shared Function BuildTopRegion() As Panel
        Const SIDE_PAD As Integer = 24

        Dim region As New Panel With {
            .BackColor = MintBg,
            .AutoSize = False,
            .Height = 254
        }

        ' ── Zone 1: Title bar (72px) ──────────────────────────────────────────
        Dim titleBar As New Panel With {
            .Height = 72,
            .Dock = DockStyle.Top,
            .BackColor = Color.Transparent
        }

        Dim lblTitle As New Label With {
            .Text = "Audit Trail",
            .Font = New Font("Segoe UI", 28, FontStyle.Bold),
            .ForeColor = MintDark,
            .AutoSize = True,
            .Location = New Point(SIDE_PAD, 16)
        }

        Dim btnRefresh As New Button With {
            .Text = "⟳  Refresh",
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
        AddHandler btnRefresh.Click, Sub() LoadAuditTrail(_container, FilterKeys(_activeFilterIdx))

        ' Search box
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
            .Text = "Search user, action, detail…",
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
                txtSearch.Text = "Search user, action, detail…"
                txtSearch.ForeColor = SubText
                btnClear.Visible = False
                searchWrap.Invalidate()
                _searchQuery = ""
                ApplyFilters()
                PopulateRows()
            End Sub
        AddHandler txtSearch.Enter,
            Sub()
                searchFocused = True
                If txtSearch.Text = "Search user, action, detail…" Then
                    txtSearch.Text = ""
                    txtSearch.ForeColor = Color.FromArgb(45, 45, 45)
                End If
                searchWrap.Invalidate()
            End Sub
        AddHandler txtSearch.Leave,
            Sub()
                searchFocused = False
                If String.IsNullOrWhiteSpace(txtSearch.Text) Then
                    txtSearch.Text = "Search user, action, detail…"
                    txtSearch.ForeColor = SubText
                    btnClear.Visible = False
                End If
                searchWrap.Invalidate()
            End Sub
        AddHandler txtSearch.TextChanged,
            Sub()
                Dim isPlaceholder As Boolean = (txtSearch.Text = "Search user, action, detail…")
                btnClear.Visible = Not isPlaceholder AndAlso txtSearch.Text.Length > 0
                If isPlaceholder Then Return
                _searchQuery = txtSearch.Text.Trim()
                ApplyFilters()
                _currentPage = 0
                PopulateRows()
            End Sub
        AddHandler searchWrap.Click, Sub() txtSearch.Focus()
        searchWrap.Controls.Add(txtSearch)
        searchWrap.Controls.Add(btnClear)

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

        ' ── Zone 2: Pill bar (52px) — date pills LEFT, module pills RIGHT ─────
        Dim pillBar As New Panel With {
            .Height = 20,
            .Dock = DockStyle.Top,
            .BackColor = Color.Transparent
        }

        ' Date pills (left)
        Dim filterHost As New FlowLayoutPanel With {
            .FlowDirection = FlowDirection.LeftToRight,
            .AutoSize = True,
            .BackColor = Color.Transparent,
            .WrapContents = False,
            .Location = New Point(SIDE_PAD, 10)
        }

        ReDim _datePills(FilterLabels.Length - 1)

        For i As Integer = 0 To FilterLabels.Length - 1
            Dim idx As Integer = i
            Dim capKey As String = FilterKeys(i)

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
                    For j As Integer = 0 To _datePills.Length - 1
                        If j = _activeFilterIdx Then
                            _datePills(j).BackColor = MintPrimary
                            _datePills(j).ForeColor = Color.White
                            _datePills(j).Font = New Font("Segoe UI Semibold", 9.5F)
                        Else
                            _datePills(j).BackColor = Color.FromArgb(220, 235, 232)
                            _datePills(j).ForeColor = SubText
                            _datePills(j).Font = New Font("Segoe UI", 9.5F)
                        End If
                        _datePills(j).Invalidate()
                    Next
                    _currentPage = 0
                    LoadData()
                    PopulateRows()
                End Sub

            styleBtn(i = _activeFilterIdx, False)
            _datePills(i) = pill
            filterHost.Controls.Add(pill)
        Next

        pillBar.Controls.Add(filterHost)

        AddHandler pillBar.Resize,
            Sub()
                Dim midY As Integer = (pillBar.Height - filterHost.Height) \ 2
                filterHost.Top = If(filterHost.Height > 0, midY, 10)
            End Sub

        ' ── Zone 3: KPI bar (165px) — mirrors Doc 2 BuildKpiBar ──────────────
        Dim kpiBar As Panel = BuildKpiBar()
        kpiBar.Dock = DockStyle.Top
        kpiBar.Height = 162

        ' Stack: kpiBar first (innermost Top), then pillBar, then titleBar
        region.Controls.Add(kpiBar)
        region.Controls.Add(pillBar)
        region.Controls.Add(titleBar)

        Return region
    End Function

    ' =========================================================================
    '  KPI BAR  —  165px, 3 cards: Total Events / Inventory / Procurement
    '  Mirrors AccountantSalesHistoryPanelBuilder.BuildKpiBar EXACTLY
    ' =========================================================================
    Private Shared Function BuildKpiBar() As Panel
        Const SIDE_PAD As Integer = 24

        Dim totalEvents As Integer = _visibleRows.Count
        Dim posCount As Integer = _visibleRows.Where(Function(r) r.[Module] = "POS").Count()
        Dim invCount As Integer = _visibleRows.Where(Function(r) r.[Module] = "Inventory").Count()
        Dim procCount As Integer = _visibleRows.Where(Function(r) r.[Module] = "Procurement").Count()

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

        Dim kpiDef() As (caption As String, value As String, clr As Color, sub1 As String, modKey As String) = {
            ("Total Events", totalEvents.ToString("N0"), MintPrimary, "All audit events in selected period", "all"),
            ("POS Events", posCount.ToString("N0"), MintDeep, "Sales & returns at point of sale", "POS"),
            ("Inventory Events", invCount.ToString("N0"), Color.FromArgb(140, 80, 0), "Stock adjustments", "Inventory"),
            ("Procurement Events", procCount.ToString("N0"), Color.FromArgb(30, 90, 180), "Purchase orders & requests", "Procurement")
        }

        For i As Integer = 0 To 3
            Dim kd = kpiDef(i)
            Dim capModKey As String = kd.modKey
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
                    Dim isActive As Boolean = (_moduleFilter = capModKey)
                    Dim rect As New Rectangle(1, 1, p.Width - 3, p.Height - 3)
                    ' shadow
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
                    _moduleFilter = If(_moduleFilter = capModKey, "all", capModKey)
                    For j As Integer = 0 To 3
                        If _kpiPanels(j) IsNot Nothing Then _kpiPanels(j).Invalidate()
                    Next
                    _currentPage = 0
                    ApplyFilters()
                    PopulateRows()
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

    ' =========================================================================
    '  COLUMN HEADER  — 44px, matches Doc 2 style (label-based proportional)
    ' =========================================================================
    Private Shared Function BuildColHeader() As Panel
        Dim hdr As New Panel With {.Height = 44, .BackColor = Color.Transparent}

        Dim labels As Label() = {
            MakeColLabel("DATE & TIME", New Point(24, 13)),
            MakeColLabel("USER", New Point(0, 13)),
            MakeColLabel("MODULE", New Point(0, 13)),
            MakeColLabel("ACTION", New Point(0, 13)),
            MakeColLabel("DETAIL", New Point(0, 13))
        }
        hdr.Controls.AddRange(labels)

        AddHandler hdr.Resize,
            Sub()
                Dim w = hdr.Width
                labels(0).Left = 24
                labels(1).Left = CInt(w * 0.16)
                labels(2).Left = CInt(w * 0.35)
                labels(3).Left = CInt(w * 0.49)
                labels(4).Left = CInt(w * 0.65)
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

    ' =========================================================================
    '  POPULATE ROWS  — mirrors Doc 2 PopulateRows with paging
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
                If(String.IsNullOrWhiteSpace(_searchQuery) AndAlso _moduleFilter = "all",
                   "No audit events found for the selected period.",
                   "No results match the current filter.")))
            UpdatePager(0)
        Else
            Dim pageRows = _visibleRows.Skip(_currentPage * PAGE_SIZE).Take(PAGE_SIZE).ToList()
            Dim y As Integer = 4
            For Each r As AuditRow In pageRows
                Dim row As Panel = BuildDataRow(r)
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

            _scrollHost.AutoScrollMinSize = New Size(0, y + 8)
            UpdatePager(_visibleRows.Count)
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

    ' ── Single data row (64px, matches Doc 2 row height) ─────────────────────
    Private Shared Function BuildDataRow(r As AuditRow) As Panel
        Const ROW_H As Integer = 64

        Dim row As New BufferedPanel With {
            .Height = ROW_H,
            .BackColor = Color.Transparent,
            .Cursor = Cursors.Hand
        }

        Dim modColors = GetModuleColors(r.[Module])

        AddHandler row.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                If TypeOf row.Tag Is Boolean AndAlso CBool(row.Tag) Then
                    Dim rect As New Rectangle(8, 3, row.Width - 16, ROW_H - 6)
                    Using path As GraphicsPath = RoundRect(rect, 10)
                        Using b As New SolidBrush(RowHover)
                            e.Graphics.FillPath(b, path)
                        End Using
                    End Using
                End If
                ' Coloured left accent bar matching module
                Using br As New SolidBrush(modColors.fore)
                    e.Graphics.FillRectangle(br, New Rectangle(0, 12, 3, ROW_H - 24))
                End Using
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 20, ROW_H - 1, row.Width - 20, ROW_H - 1)
                End Using
            End Sub

        ' Col 0 — Date & Time (stacked: date bold top, time faint below)
        Dim lblDate As New Label With {
            .Text = r.EventDate.ToString("MM/dd/yy"),
            .Font = New Font("Segoe UI", 10, FontStyle.Bold),
            .ForeColor = Color.FromArgb(45, 45, 45),
            .AutoSize = True,
            .BackColor = Color.Transparent
        }
        Dim lblTime As New Label With {
            .Text = r.EventDate.ToString("h:mm tt"),
            .Font = New Font("Segoe UI", 9),
            .ForeColor = FaintText,
            .AutoSize = True,
            .BackColor = Color.Transparent
        }

        ' Col 1 — User (name bold top, role faint below)
        Dim lblUser As New Label With {
            .Text = r.UserName,
            .Font = New Font("Segoe UI", 10, FontStyle.Bold),
            .ForeColor = Color.FromArgb(45, 45, 45),
            .AutoSize = True,
            .BackColor = Color.Transparent
        }
        Dim lblRole As New Label With {
            .Text = r.UserRole,
            .Font = New Font("Segoe UI", 8.5F),
            .ForeColor = FaintText,
            .AutoSize = True,
            .BackColor = Color.Transparent
        }

        ' Col 2 — Module badge
        Dim badge As New Label With {
            .Text = r.[Module],
            .Font = New Font("Segoe UI", 8.5F, FontStyle.Bold),
            .ForeColor = modColors.fore,
            .BackColor = Color.Transparent,
            .AutoSize = True,
            .Padding = New Padding(10, 4, 10, 4)
        }

        AddHandler badge.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim rc = badge.ClientRectangle : rc.Inflate(-1, -1)
                Using br As New SolidBrush(modColors.back)
                    e.Graphics.FillRoundedRectangle(br, rc, 10)
                End Using
                Using p As New Pen(modColors.fore, 0.7F)
                    e.Graphics.DrawRoundedRectangle(p, rc, 10)
                End Using
                TextRenderer.DrawText(e.Graphics, badge.Text, badge.Font, badge.ClientRectangle,
                    badge.ForeColor,
                    TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub

        ' Col 3 — Action
        Dim lblAction As New Label With {
            .Text = r.Action,
            .Font = New Font("Segoe UI", 10),
            .ForeColor = MintDeep,
            .AutoSize = True,
            .BackColor = Color.Transparent
        }

        ' Col 4 — Detail
        Dim lblDetail As New Label With {
            .Text = r.Detail,
            .Font = New Font("Segoe UI", 10),
            .ForeColor = Color.FromArgb(60, 70, 68),
            .AutoSize = True,
            .BackColor = Color.Transparent
        }

        row.Controls.AddRange({lblDate, lblTime, lblUser, lblRole, badge, lblAction, lblDetail})

        AddHandler row.Resize,
            Sub()
                Dim w = row.Width
                Dim topY As Integer = 10
                Dim botY As Integer = 36

                lblDate.Left = 24 : lblDate.Top = topY
                lblTime.Left = 24 : lblTime.Top = botY

                lblUser.Left = CInt(w * 0.16) : lblUser.Top = topY
                lblRole.Left = CInt(w * 0.16) : lblRole.Top = botY

                Dim badgeX = CInt(w * 0.35)
                Dim badgeY = (ROW_H - badge.Height) \ 2
                badge.Location = New Point(badgeX, Math.Max(0, badgeY))

                lblAction.Left = CInt(w * 0.49) : lblAction.Top = (ROW_H - lblAction.Height) \ 2
                lblDetail.Left = CInt(w * 0.65) : lblDetail.Top = (ROW_H - lblDetail.Height) \ 2
                lblDetail.MaximumSize = New Size(w - CInt(w * 0.65) - 20, 0)
            End Sub

        ' Row click → detail modal
        Dim openDetail As Action =
            Sub()
                Dim sb As New System.Text.StringBuilder()
                sb.AppendLine($"Date/Time : {r.EventDate:MMM dd, yyyy  hh:mm tt}")
                sb.AppendLine($"User      : {r.UserName} ({r.UserRole})")
                sb.AppendLine($"Module    : {r.[Module]}")
                sb.AppendLine($"Action    : {r.Action}")
                sb.AppendLine($"Detail    : {r.Detail}")
                AppModal.Show(sb.ToString(),
                              caption:=$"Audit Event — {r.[Module]}",
                              buttons:=MessageBoxButtons.OK,
                              icon:=MessageBoxIcon.Information)
            End Sub

        AddHandler row.Click, Sub() openDetail()
        For Each ctrl As Control In row.Controls
            AddHandler ctrl.Click, Sub() openDetail()
        Next

        PropagateHover(row)
        Return row
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

    ' =========================================================================
    '  PAGER BAR — inside container, Dock Bottom (above summary)
    '  Mirrors Doc 2 BuildPagerBar exactly
    ' =========================================================================
    Private Shared Function BuildPagerBar() As Panel
        ' Use a wrapper so the bar itself has side breathing room
        Dim wrapper As New Panel With {
    .Height = 52,
    .BackColor = Color.Transparent,  ' ← was CardBg, change to Transparent
    .Dock = DockStyle.Bottom
}

        AddHandler wrapper.Paint,
    Sub(s, e)
        ' Full-width top divider — connects to card's left and right borders
        Using dp As New Pen(RowDivider, 1)
            e.Graphics.DrawLine(dp, 0, 0, wrapper.Width, 0)
        End Using
    End Sub

        Dim bar As New Panel With {
        .BackColor = Color.FromArgb(248, 251, 249),
        .Height = 44,
        .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Bottom
    }

        ' Position bar inset 16px each side, 4px from bottom
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
        If _lblPageInfo.Parent IsNot Nothing Then _lblPageInfo.Parent.PerformLayout()
    End Sub




    ' =========================================================================
    '  MODULE BADGE COLOURS
    ' =========================================================================
    Private Shared Function GetModuleColors(moduleName As String) As (fore As Color, back As Color)
        Select Case moduleName
            Case "POS" : Return BadgePOS
            Case "Inventory" : Return BadgeInventory
            Case "Procurement" : Return BadgeProcurement
            Case Else : Return (SubText, Color.FromArgb(238, 238, 238))
        End Select
    End Function

    ' =========================================================================
    '  HELPERS
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

    Private Sub AccountantAuditTrailPanelBuilder_Load(sender As Object, e As EventArgs) Handles MyBase.Load

    End Sub
End Class