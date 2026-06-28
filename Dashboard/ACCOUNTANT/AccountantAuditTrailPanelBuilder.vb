Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports MySql.Data.MySqlClient

' =============================================================================
'  AccountantAuditTrailPanelBuilder  —  Audit Trail (Read-Only)
'
'  LAYOUT  (Dock stacking — added in reverse order so Top bars stack down)
'  ------
'    TopBar      (Dock = Top,    64px)  : dark gradient — title + subtitle
'    FilterBar   (Dock = Top,    52px)  : search box (left) + date pills (right)
'    ColHeader   (Dock = Top,    34px)  : column label row
'    SummaryBar  (Dock = Bottom, 48px)  : pinned totals — event count per module
'    ScrollHost  (Dock = Fill)          : scrollable audit rows
'
'  All data sourced from EXISTING tables — no new table required:
'
'    Sales       : sales JOIN users JOIN payment_methods
'    Returns     : returns JOIN users JOIN sales
'    Purchase Orders : purchase_orders JOIN users JOIN suppliers
'    Stock Movements : stock_movements JOIN users JOIN products  (Adjustment only)
'    Purchase Requests: purchase_requests JOIN users (requester + reviewer)
' =============================================================================
Public Class AccountantAuditTrailPanelBuilder

    ' ── PALETTE  (matches other Accountant panels exactly) ────────────────────
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

    ' Module badge colours
    Private Shared ReadOnly BadgeSale As (fore As Color, back As Color) =
        (Color.FromArgb(0, 120, 92), Color.FromArgb(220, 248, 240))
    Private Shared ReadOnly BadgeReturn As (fore As Color, back As Color) =
        (Color.FromArgb(180, 40, 40), Color.FromArgb(255, 230, 230))
    Private Shared ReadOnly BadgePO As (fore As Color, back As Color) =
        (Color.FromArgb(30, 90, 180), Color.FromArgb(225, 235, 255))
    Private Shared ReadOnly BadgeStock As (fore As Color, back As Color) =
        (Color.FromArgb(140, 80, 0), Color.FromArgb(255, 240, 210))
    Private Shared ReadOnly BadgeRequest As (fore As Color, back As Color) =
        (Color.FromArgb(100, 40, 160), Color.FromArgb(240, 228, 255))

    ' ── DATA MODEL ────────────────────────────────────────────────────────────
    Private Class AuditRow
        Public EventDate As DateTime
        Public UserName As String
        Public UserRole As String
        Public [Module] As String   ' "Sales" | "Returns" | "Purchase Orders" | "Stock" | "Requests"
        Public Action As String     ' e.g. "Sale Completed", "Return Processed"
        Public Detail As String     ' human-readable one-liner
    End Class

    ' ── STATE ─────────────────────────────────────────────────────────────────
    Private Shared _container As Panel
    Private Shared _dateFilter As String = "all"
    Private Shared _searchQuery As String = ""
    Private Shared _moduleFilter As String = "all"

    Private Shared _allRows As New List(Of AuditRow)
    Private Shared _visibleRows As New List(Of AuditRow)

    ' Live-update references
    Private Shared _scrollHost As Panel
    Private Shared _lblCount As Label
    Private Shared _lblModBreakdown As Label

    ' Pill button lists
    Private Shared _datePills As New List(Of Button)
    Private Shared _modPills As New List(Of Button)

    Private Shared ReadOnly Peso As String = ChrW(8369)

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Shared Sub LoadAuditTrail(container As Panel,
                                     Optional dateFilter As String = "all")
        _container = container
        _dateFilter = dateFilter
        _searchQuery = ""
        _moduleFilter = "all"
        _datePills.Clear()
        _modPills.Clear()

        LoadData()
        Render()
    End Sub

    ' =========================================================================
    '  DATA  — unions five financial event sources into one chronological list
    ' =========================================================================
    Private Shared Sub LoadData()
        _allRows.Clear()

        Dim dateWhere As String = BuildDateWhere()

        Try
            Using conn = Db.GetConnection()
                conn.Open()

                ' ── 1. SALES ─────────────────────────────────────────────────
                Dim sqlSales As String =
                    "SELECT s.sale_datetime AS event_date, " &
                    "       u.full_name AS user_name, " &
                    "       r.role_name AS user_role, " &
                    "       'Sales' AS module, " &
                    "       'Sale Completed' AS action, " &
                    "       CONCAT(s.sale_no, ' · ', COALESCE(pm.name,'Cash'), " &
                    "              ' · " & Peso & "', FORMAT(s.total,2)) AS detail " &
                    "FROM sales s " &
                    "JOIN users u ON s.user_id = u.user_id " &
                    "JOIN roles r ON u.role_id = r.role_id " &
                    "LEFT JOIN payment_methods pm ON s.method_id = pm.method_id " &
                    "WHERE s.status = 'Completed' " &
                    dateWhere.Replace("WHERE ", "AND ")

                ReadRows(conn, sqlSales, "Sales")

                ' ── 2. RETURNS ───────────────────────────────────────────────
                Dim sqlReturns As String =
                    "SELECT rt.return_datetime AS event_date, " &
                    "       u.full_name AS user_name, " &
                    "       ro.role_name AS user_role, " &
                    "       'Returns' AS module, " &
                    "       'Return Processed' AS action, " &
                    "       CONCAT(rt.return_no, ' · Ref: ', s.sale_no, " &
                    "              ' · " & Peso & "', FORMAT(rt.total_refund,2)) AS detail " &
                    "FROM returns rt " &
                    "JOIN users u ON rt.processed_by = u.user_id " &
                    "JOIN roles ro ON u.role_id = ro.role_id " &
                    "JOIN sales s ON rt.sale_id = s.sale_id " &
                    BuildDateWhereCol("rt.return_datetime")

                ReadRows(conn, sqlReturns, "Returns")

                ' ── 3. PURCHASE ORDERS ───────────────────────────────────────
                Dim sqlPO As String =
                    "SELECT po.order_date AS event_date, " &
                    "       u.full_name AS user_name, " &
                    "       ro.role_name AS user_role, " &
                    "       'Purchase Orders' AS module, " &
                    "       CONCAT('PO ', po.status) AS action, " &
                    "       CONCAT(po.po_no, ' · ', sup.name, " &
                    "              ' · " & Peso & "', FORMAT(po.total_cost,2)) AS detail " &
                    "FROM purchase_orders po " &
                    "JOIN users u ON po.created_by = u.user_id " &
                    "JOIN roles ro ON u.role_id = ro.role_id " &
                    "JOIN suppliers sup ON po.supplier_id = sup.supplier_id " &
                    BuildDateWhereCol("po.order_date")

                ReadRows(conn, sqlPO, "Purchase Orders")

                ' ── 4. STOCK ADJUSTMENTS ─────────────────────────────────────
                Dim sqlStock As String =
                    "SELECT sm.created_at AS event_date, " &
                    "       COALESCE(u.full_name,'System') AS user_name, " &
                    "       COALESCE(ro.role_name,'—') AS user_role, " &
                    "       'Stock' AS module, " &
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
                    BuildDateWhereCol("sm.created_at").Replace("WHERE ", "AND ")

                ReadRows(conn, sqlStock, "Stock")

                ' ── 5. PURCHASE REQUESTS ─────────────────────────────────────
                Dim sqlReq As String =
                    "SELECT pr.reviewed_at AS event_date, " &
                    "       COALESCE(rv.full_name,'—') AS user_name, " &
                    "       COALESCE(ro.role_name,'—') AS user_role, " &
                    "       'Requests' AS module, " &
                    "       CONCAT('Request ', pr.status) AS action, " &
                    "       CONCAT(p.name, ' · Qty: ', pr.qty_requested, " &
                    "              ' · By: ', req.full_name) AS detail " &
                    "FROM purchase_requests pr " &
                    "JOIN products p ON pr.product_id = p.product_id " &
                    "JOIN users req ON pr.requested_by = req.user_id " &
                    "LEFT JOIN users rv ON pr.reviewed_by = rv.user_id " &
                    "LEFT JOIN roles ro ON rv.role_id = ro.role_id " &
                    "WHERE pr.reviewed_at IS NOT NULL " &
                    BuildDateWhereCol("pr.reviewed_at").Replace("WHERE ", "AND ")

                ReadRows(conn, sqlReq, "Requests")

            End Using
        Catch ex As Exception
            Debug.WriteLine("AuditTrail LoadData error: " & ex.Message)
        End Try

        ' Sort all sources combined — newest first
        _allRows.Sort(Function(a, b) b.EventDate.CompareTo(a.EventDate))

        ApplyFilters()
    End Sub

    Private Shared Sub ReadRows(conn As MySqlConnection, sql As String, moduleName As String)
        Try
            Using cmd As New MySqlCommand(sql, conn)
                Using rdr = cmd.ExecuteReader()
                    While rdr.Read()
                        Dim dateVal As DateTime = DateTime.MinValue
                        If Not IsDBNull(rdr("event_date")) Then
                            dateVal = Convert.ToDateTime(rdr("event_date"))
                        Else
                            Continue While   ' skip rows with no date (unreviewed requests etc.)
                        End If

                        _allRows.Add(New AuditRow With {
                            .EventDate = dateVal,
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
            Debug.WriteLine($"AuditTrail ReadRows ({moduleName}) error: " & ex.Message)
        End Try
    End Sub

    ' ── Date WHERE helpers ────────────────────────────────────────────────────
    Private Shared Function BuildDateWhere() As String
        Return BuildDateWhereCol("s.sale_datetime")
    End Function

    Private Shared Function BuildDateWhereCol(col As String) As String
        Select Case _dateFilter
            Case "today" : Return $"WHERE DATE({col}) = CURDATE() "
            Case "week" : Return $"WHERE {col} >= CURDATE() - INTERVAL 7 DAY "
            Case "month" : Return $"WHERE {col} >= CURDATE() - INTERVAL 30 DAY "
            Case Else : Return ""
        End Select
    End Function

    ' ── Apply search + module filter ──────────────────────────────────────────
    Private Shared Sub ApplyFilters()
        _visibleRows.Clear()
        For Each r In _allRows
            ' Module filter
            If _moduleFilter <> "all" AndAlso r.[Module] <> _moduleFilter Then Continue For

            ' Search filter
            If Not String.IsNullOrWhiteSpace(_searchQuery) Then
                Dim q As String = _searchQuery.ToLower()
                If Not (r.UserName.ToLower().Contains(q) OrElse
                        r.Action.ToLower().Contains(q) OrElse
                        r.Detail.ToLower().Contains(q)) Then
                    Continue For
                End If
            End If

            _visibleRows.Add(r)
        Next
    End Sub

    ' =========================================================================
    '  RENDER
    ' =========================================================================
    Private Shared Sub Render()
        _container.SuspendLayout()
        _container.Controls.Clear()
        _container.BackColor = MintBg

        ' 5. SUMMARY BAR — Dock Bottom
        Dim summaryBar As Panel = BuildSummaryBar()
        summaryBar.Dock = DockStyle.Bottom

        ' 4. SCROLL HOST — Dock Fill
        _scrollHost = New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = CardBg,
            .AutoScroll = True,
            .Padding = New Padding(0, 2, 0, 4)
        }

        ' 3. COLUMN HEADER — Dock Top (innermost)
        Dim colHeader As Panel = BuildColumnHeader()
        colHeader.Dock = DockStyle.Top

        ' 2. MODULE FILTER BAR — Dock Top
        Dim modBar As Panel = BuildModuleFilterBar()
        modBar.Dock = DockStyle.Top

        ' 1. TOP BAR — Dock Top (outermost)
        Dim topBar As Panel = BuildTopBar()
        topBar.Dock = DockStyle.Top

        ' Add in correct Dock stacking order
        _container.Controls.Add(_scrollHost)
        _container.Controls.Add(summaryBar)
        _container.Controls.Add(colHeader)
        _container.Controls.Add(modBar)
        _container.Controls.Add(topBar)

        _container.ResumeLayout()

        PopulateRows()
    End Sub

    ' =========================================================================
    '  1. TOP BAR  — dark gradient, 64 px  (title + search)
    ' =========================================================================
    Private Shared Function BuildTopBar() As Panel
        Dim bar As New Panel With {.Height = 64, .BackColor = MintDark}

        AddHandler bar.Paint,
            Sub(s, e)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim rc = New Rectangle(0, 0, bar.Width, bar.Height)
                Using br As New LinearGradientBrush(rc, MintDark, HeaderBg,
                                                    LinearGradientMode.Horizontal)
                    g.FillRectangle(br, rc)
                End Using
            End Sub

        Dim lblTitle As New Label With {
            .Text = "Audit Trail",
            .Font = New Font("Segoe UI", 15, FontStyle.Bold),
            .ForeColor = Color.White,
            .BackColor = Color.Transparent,
            .AutoSize = True,
            .Location = New Point(22, 10)
        }

        Dim lblSub As New Label With {
            .Text = "Financial Activity Log  —  Read Only",
            .Font = New Font("Segoe UI", 9),
            .ForeColor = Color.FromArgb(160, 210, 200),
            .BackColor = Color.Transparent,
            .AutoSize = True,
            .Location = New Point(24, 38)
        }

        ' ── Inline search box (right side of top bar) ─────────────────────
        Dim searchWrap As New Panel With {
            .Size = New Size(260, 32),
            .BackColor = Color.FromArgb(0, 100, 88),
            .Cursor = Cursors.IBeam
        }

        Dim searchFocused As Boolean = False

        AddHandler searchWrap.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim rc = searchWrap.ClientRectangle : rc.Inflate(-1, -1)
                Using b As New SolidBrush(Color.FromArgb(0, 100, 88))
                    e.Graphics.FillRoundedRectangle(b, rc, 8)
                End Using
                Using p As New Pen(If(searchFocused,
                                      MintPrimary,
                                      Color.FromArgb(0, 160, 140)),
                                   If(searchFocused, 1.8F, 1.0F))
                    e.Graphics.DrawRoundedRectangle(p, rc, 8)
                End Using
                Using p As New Pen(Color.FromArgb(0, 200, 170), 1.4F)
                    e.Graphics.DrawEllipse(p, 10, 10, 12, 12)
                    e.Graphics.DrawLine(p, 20, 20, 25, 25)
                End Using
            End Sub

        Dim txtSearch As New TextBox With {
            .Font = New Font("Segoe UI", 10),
            .BorderStyle = BorderStyle.None,
            .BackColor = Color.FromArgb(0, 100, 88),
            .ForeColor = Color.FromArgb(200, 240, 230),
            .Text = "Search user, action, detail…",
            .Location = New Point(32, 8),
            .Width = 220
        }

        Dim btnClear As New Label With {
            .Text = "✕",
            .Font = New Font("Segoe UI", 9),
            .ForeColor = Color.FromArgb(0, 200, 170),
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
                txtSearch.ForeColor = Color.FromArgb(200, 240, 230)
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
                    txtSearch.ForeColor = Color.White
                End If
                searchWrap.Invalidate()
            End Sub

        AddHandler txtSearch.Leave,
            Sub()
                searchFocused = False
                If String.IsNullOrWhiteSpace(txtSearch.Text) Then
                    txtSearch.Text = "Search user, action, detail…"
                    txtSearch.ForeColor = Color.FromArgb(200, 240, 230)
                    btnClear.Visible = False
                End If
                searchWrap.Invalidate()
            End Sub

        AddHandler txtSearch.TextChanged,
            Sub()
                Dim isPlaceholder As Boolean =
                    (txtSearch.Text = "Search user, action, detail…")
                btnClear.Visible = Not isPlaceholder AndAlso txtSearch.Text.Length > 0
                If isPlaceholder Then Return
                _searchQuery = txtSearch.Text.Trim()
                ApplyFilters()
                PopulateRows()
            End Sub

        AddHandler searchWrap.Click, Sub() txtSearch.Focus()
        searchWrap.Controls.Add(txtSearch)
        searchWrap.Controls.Add(btnClear)

        AddHandler bar.Resize,
            Sub()
                searchWrap.Location = New Point(bar.Width - searchWrap.Width - 22,
                                                 (bar.Height - searchWrap.Height) \ 2)
            End Sub

        bar.Controls.AddRange({lblTitle, lblSub, searchWrap})
        Return bar
    End Function

    ' =========================================================================
    '  2. MODULE FILTER BAR  — date pills left, module pills right, 52 px
    ' =========================================================================
    Private Shared Function BuildModuleFilterBar() As Panel
        Dim bar As New Panel With {
            .Height = 52,
            .BackColor = MintBg
        }

        ' ── Date pills ────────────────────────────────────────────────────────
        Dim dateLabels() As String = {"Today", "This Week", "This Month", "All Time"}
        Dim dateKeys() As String = {"today", "week", "month", "all"}

        Dim dateHost As New FlowLayoutPanel With {
            .FlowDirection = FlowDirection.LeftToRight,
            .AutoSize = True,
            .BackColor = Color.Transparent,
            .WrapContents = False
        }

        For i As Integer = 0 To dateLabels.Length - 1
            Dim capKey As String = dateKeys(i)
            Dim capLabel As String = dateLabels(i)
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
            RoundBtn(pill, 12)
            _datePills.Add(pill)

            AddHandler pill.Click,
                Sub()
                    _dateFilter = capKey
                    StyleDatePills(capKey)
                    LoadData()
                    PopulateRows()
                End Sub

            dateHost.Controls.Add(pill)
        Next

        ' ── Module pills ──────────────────────────────────────────────────────
        Dim modLabels() As String = {"All", "Sales", "Returns", "Purchase Orders", "Stock", "Requests"}
        Dim modKeys() As String = {"all", "Sales", "Returns", "Purchase Orders", "Stock", "Requests"}

        Dim modHost As New FlowLayoutPanel With {
            .FlowDirection = FlowDirection.LeftToRight,
            .AutoSize = True,
            .BackColor = Color.Transparent,
            .WrapContents = False
        }

        For i As Integer = 0 To modLabels.Length - 1
            Dim capKey As String = modKeys(i)
            Dim isActive As Boolean = (_moduleFilter = capKey)

            Dim pill As New Button With {
                .Text = modLabels(i),
                .FlatStyle = FlatStyle.Flat,
                .Font = New Font("Segoe UI", 9.0F,
                                      If(isActive, FontStyle.Bold, FontStyle.Regular)),
                .ForeColor = If(isActive, Color.White, SubText),
                .BackColor = If(isActive, MintDeep, Color.FromArgb(230, 238, 235)),
                .AutoSize = True,
                .MinimumSize = New Size(50, 28),
                .Padding = New Padding(8, 0, 8, 0),
                .Cursor = Cursors.Hand,
                .Margin = New Padding(0, 0, 5, 0)
            }
            pill.FlatAppearance.BorderSize = 0
            RoundBtn(pill, 12)
            _modPills.Add(pill)

            AddHandler pill.Click,
                Sub()
                    _moduleFilter = capKey
                    StyleModPills(capKey)
                    ApplyFilters()
                    PopulateRows()
                End Sub

            modHost.Controls.Add(pill)
        Next

        bar.Controls.Add(dateHost)
        bar.Controls.Add(modHost)

        AddHandler bar.Resize,
            Sub()
                Dim midY As Integer = (bar.Height - dateHost.Height) \ 2
                dateHost.Location = New Point(22, midY)
                modHost.Location = New Point(bar.Width - modHost.Width - 22,
                                             (bar.Height - modHost.Height) \ 2)
            End Sub

        Return bar
    End Function

    Private Shared Sub StyleDatePills(activeKey As String)
        Dim keys() As String = {"today", "week", "month", "all"}
        For i As Integer = 0 To Math.Min(_datePills.Count - 1, keys.Length - 1)
            Dim isAct As Boolean = (keys(i) = activeKey)
            _datePills(i).BackColor = If(isAct, MintPrimary, Color.FromArgb(230, 238, 235))
            _datePills(i).ForeColor = If(isAct, Color.White, SubText)
            _datePills(i).Font = New Font("Segoe UI", 9.5F,
                                          If(isAct, FontStyle.Bold, FontStyle.Regular))
            _datePills(i).Invalidate()
        Next
    End Sub

    Private Shared Sub StyleModPills(activeKey As String)
        Dim keys() As String = {"all", "Sales", "Returns", "Purchase Orders", "Stock", "Requests"}
        For i As Integer = 0 To Math.Min(_modPills.Count - 1, keys.Length - 1)
            Dim isAct As Boolean = (keys(i) = activeKey)
            _modPills(i).BackColor = If(isAct, MintDeep, Color.FromArgb(230, 238, 235))
            _modPills(i).ForeColor = If(isAct, Color.White, SubText)
            _modPills(i).Font = New Font("Segoe UI", 9.0F,
                                         If(isAct, FontStyle.Bold, FontStyle.Regular))
            _modPills(i).Invalidate()
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
            .ColumnCount = 5,
            .RowCount = 1,
            .BackColor = Color.Transparent,
            .Padding = New Padding(4, 0, 4, 0)
        }
        grid.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        SetColumnStyles(grid)

        Dim hdrTexts() As String = {"Date & Time", "User", "Module", "Action", "Detail"}
        Dim hdrAligns() As ContentAlignment = {
            ContentAlignment.MiddleLeft,
            ContentAlignment.MiddleLeft,
            ContentAlignment.MiddleCenter,
            ContentAlignment.MiddleLeft,
            ContentAlignment.MiddleLeft}

        For idx = 0 To hdrTexts.Length - 1
            grid.Controls.Add(New Label With {
                .Text = hdrTexts(idx),
                .Dock = DockStyle.Fill,
                .TextAlign = hdrAligns(idx),
                .Font = New Font("Segoe UI Semibold", 9),
                .ForeColor = Color.FromArgb(122, 152, 144),
                .BackColor = Color.Transparent,
                .Padding = New Padding(If(idx = 0, 18, 4), 0, If(idx = 4, 18, 4), 0)
            }, idx, 0)
        Next

        hdr.Controls.Add(grid)
        Return hdr
    End Function

    Private Shared Sub SetColumnStyles(grid As TableLayoutPanel)
        grid.ColumnStyles.Clear()
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 140))  ' Date & Time
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 160))  ' User
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 140))  ' Module badge
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 170))  ' Action
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))   ' Detail (flex)
    End Sub

    ' =========================================================================
    '  4. SCROLL HOST  — populated separately so filters can re-fill
    ' =========================================================================
    Private Shared Sub PopulateRows()
        If _scrollHost Is Nothing Then Return

        _scrollHost.SuspendLayout()
        _scrollHost.Controls.Clear()

        If _visibleRows.Count = 0 Then
            _scrollHost.Controls.Add(BuildEmptyRow(
                If(String.IsNullOrWhiteSpace(_searchQuery) AndAlso _moduleFilter = "all",
                   "No financial activity found for the selected period.",
                   "No results match the current filter.")))
        Else
            For i As Integer = _visibleRows.Count - 1 To 0 Step -1
                _scrollHost.Controls.Add(BuildDataRow(_visibleRows(i)))
            Next
        End If

        _scrollHost.ResumeLayout()
        _scrollHost.Invalidate(True)

        UpdateSummary()
    End Sub

    ' ── Single data row ───────────────────────────────────────────────────────
    Private Shared Function BuildDataRow(r As AuditRow) As Panel
        Const ROW_H As Integer = 48

        Dim row As New BufferedPanel With {
            .Height = ROW_H,
            .Dock = DockStyle.Top,
            .BackColor = Color.Transparent
        }

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

                ' Coloured left accent bar per module
                Dim accentColor As Color = GetModuleColors(r.[Module]).fore
                Using br As New SolidBrush(accentColor)
                    e.Graphics.FillRectangle(br, New Rectangle(0, 10, 3, ROW_H - 20))
                End Using

                Using p As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(p, 16, ROW_H - 1, row.Width - 16, ROW_H - 1)
                End Using
            End Sub

        Dim grid As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .ColumnCount = 5,
            .RowCount = 1,
            .BackColor = Color.Transparent,
            .Margin = New Padding(0),
            .Padding = New Padding(4, 0, 4, 0)
        }
        grid.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        SetColumnStyles(grid)

        ' Col 0 — Date & Time
        grid.Controls.Add(New Label With {
            .Text = r.EventDate.ToString("MM/dd/yy  h:mm tt"),
            .Dock = DockStyle.Fill,
            .TextAlign = ContentAlignment.MiddleLeft,
            .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = SubText,
            .BackColor = Color.Transparent,
            .Padding = New Padding(18, 0, 4, 0)
        }, 0, 0)

        ' Col 1 — User (name + role underneath)
        Dim userWrap As New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = Color.Transparent
        }
        Dim lblName As New Label With {
            .Text = r.UserName,
            .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold),
            .ForeColor = Color.FromArgb(45, 45, 45),
            .BackColor = Color.Transparent,
            .AutoSize = True,
            .Location = New Point(4, 8)
        }
        Dim lblRole As New Label With {
            .Text = r.UserRole,
            .Font = New Font("Segoe UI", 8),
            .ForeColor = FaintText,
            .BackColor = Color.Transparent,
            .AutoSize = True,
            .Location = New Point(4, 27)
        }
        userWrap.Controls.AddRange({lblName, lblRole})
        grid.Controls.Add(userWrap, 1, 0)

        ' Col 2 — Module badge (centered)
        Dim modColors = GetModuleColors(r.[Module])
        Dim badgeWrap As New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = Color.Transparent
        }
        Dim badge As New Label With {
            .Text = r.[Module],
            .Font = New Font("Segoe UI", 8.25F, FontStyle.Bold),
            .ForeColor = modColors.fore,
            .BackColor = Color.Transparent,
            .AutoSize = True,
            .Padding = New Padding(8, 3, 8, 3)
        }
        RoundLbl(badge, 10)

        AddHandler badgeWrap.Resize,
            Sub()
                badge.Location = New Point(
                    Math.Max(0, (badgeWrap.Width - badge.Width) \ 2),
                    (ROW_H - badge.Height) \ 2)
            End Sub

        AddHandler badgeWrap.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim rc As New Rectangle(
                    Math.Max(0, (badgeWrap.Width - badge.Width) \ 2),
                    (ROW_H - badge.Height) \ 2,
                    badge.Width, badge.Height)
                Using br As New SolidBrush(modColors.back)
                    e.Graphics.FillRoundedRectangle(br, rc, 10)
                End Using
                rc.Inflate(-1, -1)
                Using p As New Pen(modColors.fore, 0.6F)
                    e.Graphics.DrawRoundedRectangle(p, rc, 10)
                End Using
            End Sub

        badgeWrap.Controls.Add(badge)
        grid.Controls.Add(badgeWrap, 2, 0)

        ' Col 3 — Action
        grid.Controls.Add(New Label With {
            .Text = r.Action,
            .Dock = DockStyle.Fill,
            .TextAlign = ContentAlignment.MiddleLeft,
            .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = MintDeep,
            .BackColor = Color.Transparent
        }, 3, 0)

        ' Col 4 — Detail (flex)
        grid.Controls.Add(New Label With {
            .Text = r.Detail,
            .Dock = DockStyle.Fill,
            .TextAlign = ContentAlignment.MiddleLeft,
            .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = Color.FromArgb(60, 70, 68),
            .BackColor = Color.Transparent,
            .Padding = New Padding(4, 0, 18, 0)
        }, 4, 0)

        row.Controls.Add(grid)

        ' Hover propagation
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

    ' Empty state
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
    '  5. SUMMARY BAR  — pinned at bottom, 48 px
    ' =========================================================================
    Private Shared Function BuildSummaryBar() As Panel
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

        Dim lblCntCap As New Label With {
            .Text = "Total Events:",
            .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = SubText,
            .AutoSize = True,
            .BackColor = Color.Transparent
        }
        _lblCount = New Label With {
            .Text = "0",
            .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold),
            .ForeColor = MintDark,
            .AutoSize = True,
            .BackColor = Color.Transparent
        }

        Dim lblBreakCap As New Label With {
            .Text = "Breakdown:",
            .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = SubText,
            .AutoSize = True,
            .BackColor = Color.Transparent
        }
        _lblModBreakdown = New Label With {
            .Text = "",
            .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = MintDeep,
            .AutoSize = True,
            .BackColor = Color.Transparent
        }

        bar.Controls.AddRange({lblCntCap, _lblCount, lblBreakCap, _lblModBreakdown})

        AddHandler bar.Resize,
            Sub()
                Dim midY As Integer = (bar.Height - lblCntCap.Height) \ 2

                lblCntCap.Location = New Point(22, midY)
                _lblCount.Location = New Point(lblCntCap.Right + 4,
                                               midY + (lblCntCap.Height - _lblCount.Height) \ 2)

                lblBreakCap.Location = New Point(_lblCount.Right + 24, midY)
                _lblModBreakdown.Location = New Point(lblBreakCap.Right + 4,
                                                      midY + (lblBreakCap.Height - _lblModBreakdown.Height) \ 2)
            End Sub

        Return bar
    End Function

    Private Shared Sub UpdateSummary()
        If _lblCount Is Nothing Then Return

        _lblCount.Text = _visibleRows.Count.ToString("N0")

        ' Build breakdown string: Sales: 3  Returns: 1  ...
        Dim groups = _visibleRows.GroupBy(Function(r) r.[Module]).
                                  OrderBy(Function(g) g.Key).
                                  Select(Function(g) $"{g.Key}: {g.Count()}")
        _lblModBreakdown.Text = If(groups.Any(), String.Join("   ", groups), "—")
    End Sub

    ' =========================================================================
    '  MODULE BADGE COLOURS
    ' =========================================================================
    Private Shared Function GetModuleColors(moduleName As String) As (fore As Color, back As Color)
        Select Case moduleName
            Case "Sales" : Return BadgeSale
            Case "Returns" : Return BadgeReturn
            Case "Purchase Orders" : Return BadgePO
            Case "Stock" : Return BadgeStock
            Case "Requests" : Return BadgeRequest
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

    Private Shared Sub RoundLbl(lbl As Label, radius As Integer)
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