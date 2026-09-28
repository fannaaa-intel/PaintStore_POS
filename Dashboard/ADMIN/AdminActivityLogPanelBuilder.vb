Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports MySql.Data.MySqlClient

' =============================================================================
'  AdminActivityLogPanelBuilder  —  Admin ▸ Activity Log (full audit trail)
'
'  The complete version of the dashboard's Recent Activity feed. Read-only.
'
'  LAYOUT  mirrors AdminUserPanelBuilder:
'  ─────────────────────────────────────
'    TopRegion (Dock = Top, 128px):
'      ├─ TitleBar  (72px) : "Activity Log" + search + Refresh
'      └─ FilterBar (56px) : From / To date pickers + Action dropdown
'    TableWrapper (Dock = Fill, 24px side spacers + 16px top/bottom):
'      └─ listArea (BufferedPanel, PanelRenderer card):
'           ├─ ColHeader   (Dock = Top,    44px)
'           ├─ _scrollHost (Dock = Fill,   AutoScroll)
'           ├─ bottomPad   (Dock = Bottom, 12px)  ← keeps the last row off the
'           └─ pagerBar    (Dock = Bottom, 52px)     pager rule / card edge
'
'  PAGING is done in SQL (COUNT + LIMIT/OFFSET), never by pulling the whole
'  table — activity_logs grows without bound.
' =============================================================================
Public Class AdminActivityLogPanelBuilder

    ' ── PALETTE  (matches AdminUserPanelBuilder / the Accountant panels) ──────
    Private Shared ReadOnly MintDark As Color = Theme.Dark
    Private Shared ReadOnly MintPrimary As Color = Theme.Primary
    Private Shared ReadOnly MintBg As Color = Theme.PageBg
    Private Shared ReadOnly CardBg As Color = Color.White
    Private Shared ReadOnly SubText As Color = Theme.SubText
    Private Shared ReadOnly FaintText As Color = Theme.FaintText
    Private Shared ReadOnly RowDivider As Color = Theme.RowDivider
    Private Shared ReadOnly RowHover As Color = Theme.RowHover

    ' Glyph tints — same families as the dashboard feed
    Private Shared ReadOnly TintMint As (fore As Color, back As Color) =
        (Color.FromArgb(0, 120, 92), Color.FromArgb(222, 246, 238))
    Private Shared ReadOnly TintRed As (fore As Color, back As Color) =
        (Color.FromArgb(180, 40, 40), Color.FromArgb(255, 233, 233))
    Private Shared ReadOnly TintBlue As (fore As Color, back As Color) =
        (Color.FromArgb(45, 80, 150), Color.FromArgb(232, 238, 250))
    Private Shared ReadOnly TintAmber As (fore As Color, back As Color) =
        (Color.FromArgb(170, 105, 20), Color.FromArgb(255, 243, 224))
    Private Shared ReadOnly TintGrey As (fore As Color, back As Color) =
        (Color.FromArgb(90, 114, 107), Color.FromArgb(238, 242, 240))

    ' ── DATA MODEL ────────────────────────────────────────────────────────────
    Private Class LogEntry
        Public LogId As Integer
        Public Action As String
        Public EntityType As String
        Public EntityId As Integer
        Public Details As String
        Public CreatedAt As DateTime
        Public FullName As String
        Public HasUser As Boolean
    End Class

    ' ── STATE ─────────────────────────────────────────────────────────────────
    Private Const ALL_ACTIONS As String = "All actions"
    ' Ceiling — a page is never larger than this, same as before.
    Private Const MAX_PAGE_SIZE As Integer = 50
    ' Fixed chrome above and below the row viewport:
    '   topRegion 128 + spacerTop 16 + spacerBottom 16 + card padding 2
    '   + colHeader 44 + pagerBar 52 + bottomPad 12
    Private Const CHROME_H As Integer = 270
    ' Actual rows per page — sized so a full page fits without scrolling.
    Private Shared _pageSize As Integer = MAX_PAGE_SIZE
    Private Const SIDE_PAD As Integer = 24
    Private Const ROW_H As Integer = 56
    Private Const DAY_SEP_H As Integer = 40
    Private Const PAD_BOTTOM As Integer = 12
    Private Const SEARCH_PLACEHOLDER As String = "Search user, action or details…"

    Private Shared _container As Panel
    Private Shared _scrollHost As Panel

    Private Shared _searchQuery As String = ""
    Private Shared _actionFilter As String = ALL_ACTIONS
    Private Shared _fromDate As DateTime
    Private Shared _toDate As DateTime

    Private Shared _rows As New List(Of LogEntry)
    Private Shared _actions As New List(Of String)
    Private Shared _totalRows As Integer = 0
    Private Shared _currentPage As Integer = 0
    Private Shared _reflowAttached As Boolean = False

    Private Shared _lblPageInfo As Label
    Private Shared _btnPrev As Button
    Private Shared _btnNext As Button

    ' Debounce so typing in the search box doesn't fire a query per keystroke.
    Private Shared _searchTimer As Timer

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Shared Sub LoadActivityLog(container As Panel)
        _container = container
        _searchQuery = ""
        _actionFilter = ALL_ACTIONS
        _toDate = DateTime.Today
        _fromDate = DateTime.Today.AddDays(-6)      ' last 7 days inclusive
        _currentPage = 0

        ComputePageSize()
        LoadActions()
        LoadPage()
        Render()
    End Sub

    ' A page that cannot fit on screen defeats the point of the pager, so the
    ' page is sized to the viewport rather than paging 50 rows (2800px at
    ' ROW_H=56) into an area that only has room for a dozen or so.
    Private Shared Sub ComputePageSize()
        _pageSize = MAX_PAGE_SIZE
        If _container Is Nothing Then Return
        Dim viewport As Integer = _container.ClientSize.Height - CHROME_H
        If viewport < 120 Then Return          ' not laid out yet — keep the ceiling
        ' Day separators occupy the same column as the rows, so a page of rows
        ' spanning several days is taller than rows alone. Reserve room for a
        ' couple of headings rather than pushing the last rows below the fold.
        Dim usable As Integer = viewport - 4 - DAY_SEP_H * 2
        Dim fits As Integer = usable \ ROW_H
        _pageSize = Math.Max(5, Math.Min(MAX_PAGE_SIZE, fits))
    End Sub

    ' Re-query with the current filters, without rebuilding the filter controls
    ' (rebuilding would drop focus and reset the date pickers mid-edit).
    Private Shared Sub ReQuery(Optional resetPage As Boolean = True)
        If resetPage Then _currentPage = 0
        LoadPage()
        PopulateRows()
        UpdatePager()
    End Sub

    ' =========================================================================
    '  DATA
    ' =========================================================================
    Private Shared Sub LoadActions()
        _actions.Clear()
        _actions.Add(ALL_ACTIONS)
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT DISTINCT action FROM activity_logs " &
                    "WHERE action IS NOT NULL AND action <> '' ORDER BY action", conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            _actions.Add(rdr("action").ToString())
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load the action list: " & ex.Message, "Database Error",
                          MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    ' Builds the shared WHERE clause. Every value is bound as a parameter.
    Private Shared Function BuildWhere() As String
        Dim sb As New System.Text.StringBuilder(
            "WHERE a.created_at >= @fromD AND a.created_at < @toD ")
        If _actionFilter <> ALL_ACTIONS Then
            sb.Append("AND a.action = @act ")
        End If
        If _searchQuery <> "" Then
            sb.Append("AND (COALESCE(u.full_name,'') LIKE @q " &
                      "OR a.action LIKE @q " &
                      "OR COALESCE(a.details,'') LIKE @q) ")
        End If
        Return sb.ToString()
    End Function

    Private Shared Sub AddWhereParams(cmd As MySqlCommand)
        cmd.Parameters.AddWithValue("@fromD", _fromDate.Date)
        ' Exclusive upper bound one day past "To" so the whole day is included.
        cmd.Parameters.AddWithValue("@toD", _toDate.Date.AddDays(1))
        If _actionFilter <> ALL_ACTIONS Then
            cmd.Parameters.AddWithValue("@act", _actionFilter)
        End If
        If _searchQuery <> "" Then
            cmd.Parameters.AddWithValue("@q", "%" & _searchQuery & "%")
        End If
    End Sub

    Private Shared Sub LoadPage()
        _rows.Clear()
        _totalRows = 0

        Try
            Using conn = Db.GetConnection()
                conn.Open()

                ' 1) Total matching rows — drives the pager.
                Using cmd As New MySqlCommand(
                    "SELECT COUNT(*) FROM activity_logs a " &
                    "LEFT JOIN users u ON a.user_id = u.user_id " & BuildWhere(), conn)
                    AddWhereParams(cmd)
                    _totalRows = Convert.ToInt32(cmd.ExecuteScalar())
                End Using

                ' Clamp the page if filters shrank the result set.
                Dim totalPages As Integer =
                    Math.Max(1, CInt(Math.Ceiling(_totalRows / _pageSize)))
                If _currentPage > totalPages - 1 Then _currentPage = totalPages - 1
                If _currentPage < 0 Then _currentPage = 0

                ' 2) Just this page.
                Using cmd As New MySqlCommand(
                    "SELECT a.log_id, a.action, a.entity_type, a.entity_id, a.details, " &
                    "a.created_at, a.user_id, u.full_name " &
                    "FROM activity_logs a " &
                    "LEFT JOIN users u ON a.user_id = u.user_id " &
                    BuildWhere() &
                    "ORDER BY a.created_at DESC, a.log_id DESC " &
                    "LIMIT @lim OFFSET @off", conn)
                    AddWhereParams(cmd)
                    cmd.Parameters.AddWithValue("@lim", _pageSize)
                    cmd.Parameters.AddWithValue("@off", _currentPage * _pageSize)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            _rows.Add(New LogEntry With {
                                .LogId = SafeInt(rdr("log_id")),
                                .Action = SafeStr(rdr("action")),
                                .EntityType = SafeStr(rdr("entity_type")),
                                .EntityId = SafeInt(rdr("entity_id")),
                                .Details = SafeStr(rdr("details")),
                                .CreatedAt = If(IsDBNull(rdr("created_at")),
                                                DateTime.MinValue,
                                                Convert.ToDateTime(rdr("created_at"))),
                                .HasUser = Not IsDBNull(rdr("user_id")),
                                .FullName = SafeStr(rdr("full_name"))
                            })
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load the activity log: " & ex.Message, "Database Error",
                          MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    Private Shared Function SafeInt(o As Object) As Integer
        If o Is Nothing OrElse IsDBNull(o) Then Return 0
        Return Convert.ToInt32(o)
    End Function

    Private Shared Function SafeStr(o As Object) As String
        If o Is Nothing OrElse IsDBNull(o) Then Return ""
        Return o.ToString()
    End Function

    ' =========================================================================
    '  RENDER
    ' =========================================================================
    Private Shared Sub Render()
        _container.SuspendLayout()
        _container.Controls.Clear()
        _reflowAttached = False
        _container.BackColor = MintBg

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
            .Padding = New Padding(0)
        }

        Dim colHdr As Panel = BuildColHeader()
        colHdr.Dock = DockStyle.Top

        Dim pagerBar As Panel = BuildPagerBar()
        pagerBar.Dock = DockStyle.Bottom

        ' Gap between the last row and the pager rule, so nothing collides with
        ' the card edge (same bottom-padding fix as the Recent Activity card).
        Dim bottomPad As New Panel With {
            .Dock = DockStyle.Bottom,
            .Height = PAD_BOTTOM,
            .BackColor = Color.Transparent
        }

        ' Dock order matters: the LAST index is laid out first, so pagerBar must
        ' be added after bottomPad to end up outermost at the bottom.
        listArea.Controls.Add(bottomPad)
        listArea.Controls.Add(pagerBar)
        listArea.Controls.Add(colHdr)
        listArea.Controls.Add(_scrollHost)
        colHdr.BringToFront()
        _scrollHost.BringToFront()
        _scrollHost.AutoScrollMinSize = New Size(0, 1)

        Dim spacerBottom As New Panel With {.Dock = DockStyle.Bottom, .Height = 16, .BackColor = MintBg}
        Dim spacerTop As New Panel With {.Dock = DockStyle.Top, .Height = 16, .BackColor = MintBg}
        Dim spacerLeft As New Panel With {.Dock = DockStyle.Left, .Width = SIDE_PAD, .BackColor = MintBg}
        Dim spacerRight As New Panel With {.Dock = DockStyle.Right, .Width = SIDE_PAD, .BackColor = MintBg}

        tableWrapper.Controls.Add(listArea)
        tableWrapper.Controls.Add(spacerBottom)
        tableWrapper.Controls.Add(spacerTop)
        tableWrapper.Controls.Add(spacerLeft)
        tableWrapper.Controls.Add(spacerRight)

        Dim topRegion As Panel = BuildTopRegion()
        topRegion.Dock = DockStyle.Top

        _container.Controls.Add(tableWrapper)
        _container.Controls.Add(topRegion)

        _container.ResumeLayout()

        PopulateRows()
        UpdatePager()
    End Sub

    ' =========================================================================
    '  TOP REGION  —  72px title bar + 56px filter bar
    ' =========================================================================
    Private Shared Function BuildTopRegion() As Panel
        Dim region As New Panel With {
            .BackColor = MintBg, .AutoSize = False, .Height = 128
        }

        ' ── Zone 1: title bar ────────────────────────────────────────────────
        Dim titleBar As New Panel With {
            .Height = 72, .Dock = DockStyle.Top, .BackColor = Color.Transparent
        }

        Dim lblTitle As New Label With {
            .Text = "Activity Log",
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
            .ForeColor = MintDark,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        btnRefresh.FlatAppearance.BorderSize = 0
        AddHandler btnRefresh.Paint, AddressOf PaintPillButton
        AddHandler btnRefresh.MouseEnter, Sub()
                                              btnRefresh.BackColor = Color.FromArgb(210, 240, 235)
                                              btnRefresh.Invalidate()
                                          End Sub
        AddHandler btnRefresh.MouseLeave, Sub()
                                              btnRefresh.BackColor = Color.FromArgb(220, 235, 232)
                                              btnRefresh.Invalidate()
                                          End Sub
        AddHandler btnRefresh.Click, Sub() LoadActivityLog(_container)

        ' ── Search box ───────────────────────────────────────────────────────
        Dim searchWrap As New Panel With {
            .Size = New Size(260, 34),
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
            .Text = SEARCH_PLACEHOLDER,
            .Location = New Point(34, 9),
            .Width = 214
        }

        ' 350ms debounce — the search hits the DB, unlike the client-side filter
        ' on the user list, so firing per keystroke would hammer it.
        If _searchTimer IsNot Nothing Then
            _searchTimer.Stop()
            _searchTimer.Dispose()
        End If
        _searchTimer = New Timer With {.Interval = 350}
        AddHandler _searchTimer.Tick,
            Sub()
                _searchTimer.Stop()
                ReQuery()
            End Sub

        AddHandler txtSearch.Enter,
            Sub()
                searchFocused = True
                If txtSearch.Text = SEARCH_PLACEHOLDER Then
                    txtSearch.Text = ""
                    txtSearch.ForeColor = Color.FromArgb(45, 45, 45)
                End If
                searchWrap.Invalidate()
            End Sub
        AddHandler txtSearch.Leave,
            Sub()
                searchFocused = False
                If String.IsNullOrWhiteSpace(txtSearch.Text) Then
                    txtSearch.Text = SEARCH_PLACEHOLDER
                    txtSearch.ForeColor = SubText
                End If
                searchWrap.Invalidate()
            End Sub
        AddHandler txtSearch.TextChanged,
            Sub()
                If txtSearch.Text = SEARCH_PLACEHOLDER Then Return
                _searchQuery = txtSearch.Text.Trim()
                _searchTimer.Stop()
                _searchTimer.Start()
            End Sub
        AddHandler searchWrap.Click, Sub() txtSearch.Focus()
        searchWrap.Controls.Add(txtSearch)

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

        ' ── Zone 2: filter bar ───────────────────────────────────────────────
        Dim filterBar As New Panel With {
            .Height = 56, .Dock = DockStyle.Top, .BackColor = Color.Transparent
        }

        Dim lblFrom As New Label With {
            .Text = "From", .Font = New Font("Segoe UI", 9.5F), .ForeColor = SubText,
            .AutoSize = True, .Location = New Point(SIDE_PAD, 19), .BackColor = Color.Transparent
        }
        ' Declared first so each picker can nudge the other when the range
        ' inverts; assigned immediately below.
        Dim boxFrom As Panel = Nothing, boxTo As Panel = Nothing
        Dim setFrom As Action(Of Date) = Nothing, setTo As Action(Of Date) = Nothing

        boxFrom = FilterFields.MakeDateField(132, _fromDate,
            Sub(d As Date)
                _fromDate = d
                If _fromDate > _toDate Then
                    _toDate = _fromDate
                    setTo(_toDate)
                End If
                ReQuery()
            End Sub, setFrom)
        boxFrom.Location = New Point(SIDE_PAD + 44, 11)

        Dim lblTo As New Label With {
            .Text = "To", .Font = New Font("Segoe UI", 9.5F), .ForeColor = SubText,
            .AutoSize = True, .Location = New Point(boxFrom.Right + 16, 19),
            .BackColor = Color.Transparent
        }

        boxTo = FilterFields.MakeDateField(132, _toDate,
            Sub(d As Date)
                _toDate = d
                If _toDate < _fromDate Then
                    _fromDate = _toDate
                    setFrom(_fromDate)
                End If
                ReQuery()
            End Sub, setTo)
        boxTo.Location = New Point(boxFrom.Right + 44, 11)

        Dim lblAction As New Label With {
            .Text = "Action", .Font = New Font("Segoe UI", 9.5F), .ForeColor = SubText,
            .AutoSize = True, .Location = New Point(boxTo.Right + 24, 19),
            .BackColor = Color.Transparent
        }
        ' _actions holds the raw enum values (LOGIN, PAYMETHOD_DELETE). The rows
        ' show friendly labels, so the filter shows them too and the picked
        ' index maps back to the raw value the query needs.
        Dim actionNames As New List(Of String)
        For i As Integer = 0 To _actions.Count - 1
            actionNames.Add(If(i = 0, _actions(i), FriendlyAction(_actions(i))))
        Next
        Dim selIdx As Integer = _actions.IndexOf(_actionFilter)
        If selIdx < 0 Then selIdx = 0
        Dim boxAction As Panel = FilterFields.MakeSelectField(actionNames, 196, selIdx,
            Sub(i As Integer)
                _actionFilter = If(i >= 0 AndAlso i < _actions.Count,
                                   _actions(i), ALL_ACTIONS)
                ReQuery()
            End Sub)
        boxAction.Location = New Point(boxTo.Right + 76, 11)

        filterBar.Controls.AddRange({lblFrom, boxFrom, lblTo, boxTo,
                                     lblAction, boxAction})

        region.Controls.Add(filterBar)
        region.Controls.Add(titleBar)
        Return region
    End Function

    Private Shared Sub PaintPillButton(s As Object, e As PaintEventArgs)
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

    ' =========================================================================
    '  COLUMN HEADER  —  44px, proportional (must match BuildDataRow)
    ' =========================================================================
    Private Shared Function BuildColHeader() As Panel
        Dim hdr As New Panel With {.Height = 44, .BackColor = Color.Transparent}

        Dim labels As Label() = {
            MakeColLabel("TIME", New Point(30, 13)),
            MakeColLabel("USER", New Point(0, 13)),
            MakeColLabel("ACTION", New Point(0, 13)),
            MakeColLabel("DETAILS", New Point(0, 13))
        }
        hdr.Controls.AddRange(labels)

        AddHandler hdr.Resize,
            Sub()
                Dim w = hdr.Width
                labels(0).Left = 30
                labels(1).Left = CInt(w * 0.16)
                labels(2).Left = CInt(w * 0.38)
                labels(3).Left = CInt(w * 0.56)
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
    '  ROWS
    ' =========================================================================
    Private Shared Sub PopulateRows()
        If _scrollHost Is Nothing Then Return

        _scrollHost.SuspendLayout()
        _scrollHost.Controls.Clear()

        If _reflowAttached Then
            RemoveHandler _scrollHost.Resize, AddressOf ReflowScrollHost
            _reflowAttached = False
        End If

        If _rows.Count = 0 Then
            _scrollHost.Controls.Add(BuildEmptyRow(
                If(_searchQuery = "" AndAlso _actionFilter = ALL_ACTIONS,
                   "No activity recorded in this date range.",
                   "No activity matches the current filters.")))
        Else
            Dim y As Integer = 4
            Dim lastDay As Date = Date.MinValue
            For Each r As LogEntry In _rows
                ' One date heading per day instead of the same date repeated
                ' down every row: the eye finds "yesterday" in one jump.
                Dim thisDay As Date = r.CreatedAt.Date
                If thisDay <> lastDay Then
                    lastDay = thisDay
                    Dim sep As Panel = BuildDaySeparator(thisDay)
                    sep.Location = New Point(0, y)
                    sep.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
                    _scrollHost.Controls.Add(sep)
                    sep.Width = _scrollHost.ClientSize.Width - 4
                    y += DAY_SEP_H
                End If

                Dim row As Panel = BuildDataRow(r, ROW_H)
                row.Location = New Point(0, y)
                row.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
                _scrollHost.Controls.Add(row)
                row.Width = _scrollHost.ClientSize.Width - 4
                y += ROW_H
            Next

            If Not _reflowAttached Then
                _reflowAttached = True
                AddHandler _scrollHost.Resize, AddressOf ReflowScrollHost
            End If
        End If

        _scrollHost.ResumeLayout()
        _scrollHost.Invalidate(True)
    End Sub

    Private Shared Sub ReflowScrollHost(sender As Object, e As EventArgs)
        If _scrollHost Is Nothing Then Return
        Dim w As Integer = _scrollHost.ClientSize.Width - 4
        For Each c As Control In _scrollHost.Controls
            c.Width = w
        Next
    End Sub

    Private Shared Function BuildDataRow(r As LogEntry, height As Integer) As Panel
        Dim p As New BufferedPanel With {
            .Height = height, .BackColor = Color.Transparent
        }

        AddHandler p.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                If TypeOf p.Tag Is Boolean AndAlso CBool(p.Tag) Then
                    Dim rect As New Rectangle(8, 3, p.Width - 16, height - 6)
                    Using br As New SolidBrush(RowHover)
                        e.Graphics.FillRoundedRectangle(br, rect, 10)
                    End Using
                End If
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 20, p.Height - 1, p.Width - 20, p.Height - 1)
                End Using
                ' An audit trail is read to find the unusual. Routine sign-ins
                ' stay quiet; anything that changed an account or a setting
                ' carries a short accent bar so it can be found by eye.
                Dim sev = SeverityOf(r.Action)
                If sev.notable Then
                    Using br As New SolidBrush(sev.fore)
                        e.Graphics.FillRoundedRectangle(br,
                            New Rectangle(20, (height - 20) \ 2, 3, 20), 2)
                    End Using
                End If
            End Sub

        ' The date lives on the day separator above, so the row carries only
        ' the time -- 13 repetitions of "Sep 6, 2026" told the reader nothing.
        Dim lblWhen As New Label With {
            .Text = If(r.CreatedAt = DateTime.MinValue, "—",
                       r.CreatedAt.ToString("h:mm:ss tt")),
            .Font = New Font("Consolas", 10),
            .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblUser As New Label With {
            .Text = If(Not r.HasUser, "System",
                       If(r.FullName = "", "—", r.FullName)),
            .Font = New Font("Segoe UI", 10.5F),
            .ForeColor = If(r.HasUser, MintDark, FaintText),
            .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblAction As New Label With {
            .Text = FriendlyAction(r.Action),
            .Font = New Font("Segoe UI Semibold", 10.5F),
            .ForeColor = SeverityOf(r.Action).fore,
            .AutoSize = True, .BackColor = Color.Transparent
        }
        ' Details are empty on the great majority of rows (every login and
        ' logout), so an em-dash column of its own was mostly blank ink. It
        ' now sits beside the action and simply is not drawn when absent.
        Dim lblDetails As New Label With {
            .Text = r.Details,
            .Font = New Font("Segoe UI", 10.5F),
            .ForeColor = Color.FromArgb(45, 60, 56),
            .AutoSize = False,
            .AutoEllipsis = True,
            .Height = 20,
            .Visible = (r.Details <> ""),
            .BackColor = Color.Transparent
        }

        p.Controls.AddRange({lblWhen, lblUser, lblAction, lblDetails})

        AddHandler p.Resize,
            Sub()
                Dim w = p.Width
                lblWhen.Left = 30 : lblWhen.Top = (height - lblWhen.Height) \ 2
                lblUser.Left = CInt(w * 0.16) : lblUser.Top = (height - lblUser.Height) \ 2
                lblAction.Left = CInt(w * 0.38) : lblAction.Top = (height - lblAction.Height) \ 2
                lblDetails.Left = CInt(w * 0.56)
                lblDetails.Top = (height - lblDetails.Height) \ 2
                lblDetails.Width = Math.Max(40, w - lblDetails.Left - 24)
            End Sub

        PropagateHover(p)
        Return p
    End Function

    ' A quiet date heading between days. "Today" and "Yesterday" are named
    ' rather than dated, because that is how anyone investigating an incident
    ' actually thinks about when it happened.
    Private Shared Function BuildDaySeparator(d As Date) As Panel
        Dim p As New BufferedPanel With {
            .Height = DAY_SEP_H, .BackColor = Color.Transparent
        }

        Dim caption As String
        If d = Date.Today Then
            caption = "Today  ·  " & d.ToString("MMM d, yyyy")
        ElseIf d = Date.Today.AddDays(-1) Then
            caption = "Yesterday  ·  " & d.ToString("MMM d, yyyy")
        Else
            caption = d.ToString("dddd  ·  MMM d, yyyy")
        End If

        Dim lbl As New Label With {
            .Text = caption,
            .Font = New Font("Segoe UI Semibold", 9.5F),
            .ForeColor = TintMint.fore,
            .AutoSize = True,
            .Location = New Point(30, DAY_SEP_H - 22),
            .BackColor = Color.Transparent
        }
        p.Controls.Add(lbl)

        AddHandler p.Paint,
            Sub(sn, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                ' Rule running from just after the caption to the right margin.
                Dim x As Integer = lbl.Right + 12
                Dim yy As Integer = DAY_SEP_H - 22 + lbl.Height \ 2
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, x, yy, p.Width - 24, yy)
                End Using
            End Sub

        Return p
    End Function

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
    '  PAGER  —  mirrors AccountantSalesHistoryPanelBuilder
    ' =========================================================================
    Private Shared Function BuildPagerBar() As Panel
        Dim wrapper As New Panel With {.Height = 52, .BackColor = Color.Transparent}

        AddHandler wrapper.Paint,
            Sub(s, e)
                Using dp As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(dp, 0, 0, wrapper.Width, 0)
                End Using
            End Sub

        _btnPrev = New Button With {
            .Text = "‹", .FlatStyle = FlatStyle.Flat, .Cursor = Cursors.Hand,
            .Size = New Size(32, 32), .BackColor = Color.FromArgb(220, 235, 232),
            .ForeColor = MintDark, .Font = New Font("Segoe UI", 14, FontStyle.Bold),
            .TextAlign = ContentAlignment.MiddleCenter
        }
        _btnPrev.FlatAppearance.BorderSize = 0

        _btnNext = New Button With {
            .Text = "›", .FlatStyle = FlatStyle.Flat, .Cursor = Cursors.Hand,
            .Size = New Size(32, 32), .BackColor = Color.FromArgb(220, 235, 232),
            .ForeColor = MintDark, .Font = New Font("Segoe UI", 14, FontStyle.Bold),
            .TextAlign = ContentAlignment.MiddleCenter
        }
        _btnNext.FlatAppearance.BorderSize = 0

        _lblPageInfo = New Label With {
            .Font = New Font("Segoe UI", 9.5F), .ForeColor = SubText,
            .AutoSize = True, .BackColor = Color.Transparent
        }

        AddHandler wrapper.Resize,
            Sub()
                Dim cx As Integer = wrapper.Width \ 2
                _btnPrev.Left = cx - _btnPrev.Width - _lblPageInfo.Width \ 2 - 8
                _btnPrev.Top = (wrapper.Height - _btnPrev.Height) \ 2
                _lblPageInfo.Left = cx - _lblPageInfo.Width \ 2
                _lblPageInfo.Top = (wrapper.Height - _lblPageInfo.Height) \ 2
                _btnNext.Left = cx + _lblPageInfo.Width \ 2 + 8
                _btnNext.Top = (wrapper.Height - _btnNext.Height) \ 2
            End Sub

        AddHandler _btnPrev.Click,
            Sub()
                If _currentPage > 0 Then
                    _currentPage -= 1
                    ReQuery(resetPage:=False)
                End If
            End Sub
        AddHandler _btnNext.Click,
            Sub()
                Dim totalPages As Integer =
                    Math.Max(1, CInt(Math.Ceiling(_totalRows / _pageSize)))
                If _currentPage < totalPages - 1 Then
                    _currentPage += 1
                    ReQuery(resetPage:=False)
                End If
            End Sub

        wrapper.Controls.AddRange({_btnPrev, _lblPageInfo, _btnNext})
        Return wrapper
    End Function

    Private Shared Sub UpdatePager()
        If _lblPageInfo Is Nothing Then Return

        Dim totalPages As Integer = Math.Max(1, CInt(Math.Ceiling(_totalRows / _pageSize)))
        If _totalRows = 0 Then
            _lblPageInfo.Text = "No entries"
        Else
            Dim firstRow As Integer = _currentPage * _pageSize + 1
            Dim lastRow As Integer = Math.Min(_totalRows, firstRow + _rows.Count - 1)
            _lblPageInfo.Text =
                $"Showing {firstRow:N0}–{lastRow:N0} of {_totalRows:N0}   ·   Page {_currentPage + 1} of {totalPages}"
        End If

        _btnPrev.Enabled = (_currentPage > 0)
        _btnNext.Enabled = (_currentPage < totalPages - 1)
        If _lblPageInfo.Parent IsNot Nothing Then _lblPageInfo.Parent.PerformLayout()
    End Sub

    ' =========================================================================
    '  ACTION PRESENTATION
    ' =========================================================================
    ' What the action means for an auditor: colour, and whether it is worth
    ' flagging. Sign-in traffic is the bulk of the table and is deliberately
    ' unflagged, so the rows that changed something stand out.
    Private Shared Function SeverityOf(action As String) As (fore As Color, notable As Boolean)
        Select Case action.ToUpper()
            ' Routine traffic: the bulk of the table, deliberately unflagged so
            ' the rows that changed something can be found by eye.
            Case "LOGIN", "LOGOUT", "SHIFT_OPEN", "SHIFT_CLOSED_ON_EXIT",
                 "SALE_COMPLETE", "PASSWORD_CHANGE"
                Return (SubText, False)

            ' Reversals, deletions and failed access: what an audit hunts for.
            Case "SALE_VOID", "SALE_REFUND", "USER_DISABLE", "USER_DELETE",
                 "USER_REJECT", "ADMIN_REAUTH_FAILED", "PRODUCT_DELETE",
                 "CATEGORY_DELETE", "FORMULA_DELETE", "PAYMETHOD_DELETE",
                 "SUPPLIER_DELETE", "QUOTE_DELETE", "QUOTE_REJECTED",
                 "PO_CANCELLED"
                Return (TintRed.fore, True)

            ' Cash moving without a sale behind it.
            Case "CASH_PAYIN", "CASH_PAYOUT", "CASH_DROP", "SHIFT_CLOSE",
                 "PO_PAID", "STOCK_ADJUST"
                Return (TintAmber.fore, True)

            ' Access granted or accounts created.
            Case "USER_CREATE", "USER_ENABLE", "USER_APPROVE"
                Return (TintMint.fore, True)

            Case "USER_RESET_PW", "USER_PASSWORD_RESET", "REPORT_EXPORT"
                Return (TintBlue.fore, True)

            Case Else
                ' Anything else is a change to master data or settings.
                Return (MintDark, True)
        End Select
    End Function

    ' Raw enum -> readable label.
    ' Every action the system records, in the words a person would use. The
    ' fallback below only catches genuinely new actions; anything listed here
    ' reads properly in both the table and the Action filter.
    ' Public: Settings > My Activity shows the same wording for a user's own log.
    Public Shared Function FriendlyAction(action As String) As String
        Select Case action.ToUpper()
            ' ── Access ──────────────────────────────────────────────────
            Case "LOGIN" : Return "Signed in"
            Case "LOGOUT" : Return "Signed out"
            Case "PASSWORD_CHANGE" : Return "Changed own password"
            Case "PASSWORD_OTP_SENT" : Return "Sent password code"
            Case "ADMIN_REAUTH_FAILED" : Return "Failed admin re-auth"
            Case "EMAIL_FAILED" : Return "Email not sent"

            ' ── Accounts ────────────────────────────────────────────────
            Case "USER_CREATE" : Return "Created user"
            Case "USER_EDIT" : Return "Edited user"
            Case "USER_ENABLE" : Return "Enabled user"
            Case "USER_DISABLE" : Return "Disabled user"
            Case "USER_RESET_PW" : Return "Reset password"
            Case "USER_PASSWORD_RESET" : Return "Reset password"
            Case "USER_RESET_DISMISS" : Return "Dismissed reset request"
            Case "USER_APPROVE" : Return "Approved sign-up"
            Case "USER_REJECT" : Return "Rejected sign-up"
            Case "SIGN-UP REQUESTED" : Return "Requested sign-up"

            ' ── Selling ─────────────────────────────────────────────────
            Case "SALE_COMPLETE" : Return "Completed sale"
            Case "SALE_VOID" : Return "Voided sale"
            Case "SALE_REFUND" : Return "Refunded sale"
            Case "SALE_HOLD" : Return "Held sale"
            Case "SALE_RETRIEVE" : Return "Retrieved held sale"

            ' ── Cash drawer ─────────────────────────────────────────────
            Case "SHIFT_OPEN" : Return "Opened drawer"
            Case "SHIFT_CLOSE" : Return "Closed drawer"
            Case "SHIFT_CLOSED_ON_EXIT" : Return "Drawer closed on exit"
            Case "CASH_PAYIN" : Return "Cash pay-in"
            Case "CASH_PAYOUT" : Return "Cash pay-out"
            Case "CASH_DROP" : Return "Cash drop"

            ' ── Stock ───────────────────────────────────────────────────
            Case "PRODUCT_ADD" : Return "Added product"
            Case "PRODUCT_EDIT" : Return "Edited product"
            Case "PRODUCT_DELETE" : Return "Deleted product"
            Case "STOCK_ADJUST" : Return "Adjusted stock"
            Case "STOCK_PUTAWAY" : Return "Put away stock"
            Case "REQUEST_RAISE" : Return "Raised purchase request"

            ' ── Buying ──────────────────────────────────────────────────
            Case "PO_CREATE" : Return "Created purchase order"
            Case "PO_ORDERED" : Return "Marked PO ordered"
            Case "PO_RECEIVED" : Return "Received purchase order"
            Case "PO_CANCELLED" : Return "Cancelled purchase order"
            Case "PO_PAID" : Return "Paid purchase order"
            Case "QUOTE_CREATE" : Return "Created quotation"
            Case "QUOTE_ACCEPTED" : Return "Accepted quotation"
            ' The status change path records "QUOTE_" & the new status, so the
            ' rest of the quotation enum needs labels of its own.
            Case "QUOTE_SENT" : Return "Sent quotation"
            Case "QUOTE_REJECTED" : Return "Rejected quotation"
            Case "QUOTE_DRAFT" : Return "Reverted quotation to draft"
            Case "QUOTE_EXPIRED" : Return "Expired quotation"
            Case "QUOTE_DELETE" : Return "Deleted quotation"
            Case "SUPPLIER_ADD" : Return "Added supplier"
            Case "SUPPLIER_EDIT" : Return "Edited supplier"
            Case "SUPPLIER_DELETE" : Return "Deleted supplier"

            ' ── Master data & settings ──────────────────────────────────
            Case "CATEGORY_ADD" : Return "Added category"
            Case "CATEGORY_EDIT" : Return "Edited category"
            Case "CATEGORY_DELETE" : Return "Deleted category"
            Case "CATEGORY_MERGE" : Return "Merged categories"
            Case "FORMULA_ADD" : Return "Added colour formula"
            Case "FORMULA_EDIT" : Return "Edited colour formula"
            Case "FORMULA_DELETE" : Return "Deleted colour formula"
            Case "PAYMETHOD_ADD" : Return "Added payment method"
            Case "PAYMETHOD_EDIT" : Return "Edited payment method"
            Case "PAYMETHOD_DELETE" : Return "Deleted payment method"
            Case "SETTINGS_UPDATE" : Return "Updated store settings"
            Case "REPORT_EXPORT" : Return "Exported report"
            Case "BACKUP_CREATE" : Return "Backed up database"
            Case "BACKUP_RESTORE" : Return "Restored database"
            Case "ARCHIVE_RESTORE" : Return "Restored from Recycle Bin"
            Case "ARCHIVE_PURGE" : Return "Deleted forever from Recycle Bin"

            Case Else
                ' Genuinely new action: "SOME_THING" -> "Some thing"
                If action = "" Then Return "—"
                Dim s As String = action.Replace("_", " ").ToLower()
                Return Char.ToUpper(s(0)) & s.Substring(1)
        End Select
    End Function

    ' =========================================================================
    '  SHARED HELPERS  (same implementations as the other panels)
    ' =========================================================================
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
