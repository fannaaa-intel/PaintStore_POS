Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports MySql.Data.MySqlClient

' =============================================================================
'  AdminUserPanelBuilder  —  Admin ▸ User Management
'
'  LAYOUT  mirrors AccountantSalesHistoryPanelBuilder:
'  ──────────────────────────────────────────────────
'    TopRegion (Dock = Top, 72px):
'      └─ TitleBar : "User Management" title + Search box + "+ New User" btn
'    TableWrapper (Dock = Fill, 24px side spacers + 16px top/bottom):
'      └─ listArea (BufferedPanel, Fill, PanelRenderer card):
'           ├─ ColHeader   (Dock = Top, 44px)
'           └─ _scrollHost (Dock = Fill, AutoScroll)
'
'  Rows carry three inline actions: Edit / Enable-Disable / Reset Password.
'
'  SQL:
'    SELECT u.user_id, u.full_name, u.username, u.is_active, u.created_at,
'           u.sales_quota, u.role_id, r.role_name
'    FROM users u JOIN roles r ON u.role_id = r.role_id
'    ORDER BY u.full_name
' =============================================================================
Public Class AdminUserPanelBuilder

    ' ── PALETTE  (matches the Accountant/Inventory panels exactly) ────────────
    Private Shared ReadOnly MintDark As Color = Theme.Dark
    Private Shared ReadOnly MintPrimary As Color = Theme.Primary
    Private Shared ReadOnly MintBg As Color = Theme.PageBg
    Private Shared ReadOnly CardBg As Color = Color.White
    Private Shared ReadOnly SubText As Color = Theme.SubText
    Private Shared ReadOnly FaintText As Color = Theme.FaintText
    Private Shared ReadOnly RowDivider As Color = Theme.RowDivider
    Private Shared ReadOnly RowHover As Color = Theme.RowHover
    Private Shared ReadOnly AccentRed As Color = Theme.Danger
    Private Shared ReadOnly AccentOrange As Color = Color.FromArgb(230, 126, 34)
    Private Shared ReadOnly AccentBlue As Color = Color.FromArgb(70, 120, 200)
    Private Shared ReadOnly FieldBg As Color = Theme.PageBg
    Private Shared ReadOnly FieldBorder As Color = Theme.BorderMuted
    Private Shared ReadOnly BorderClr As Color = Color.FromArgb(160, 195, 185)
    Private Shared ReadOnly DividerClr As Color = Color.FromArgb(230, 238, 235)
    Private Shared ReadOnly TitleClr As Color = Color.FromArgb(25, 40, 35)

    ' Status badge colours — same treatment as the sales-history status badges
    Private Shared ReadOnly BadgeActive As (fore As Color, back As Color) =
        (Color.FromArgb(0, 120, 92), Color.FromArgb(220, 248, 240))
    Private Shared ReadOnly BadgeDisabled As (fore As Color, back As Color) =
        (Color.FromArgb(120, 120, 120), Color.FromArgb(238, 238, 238))

    ' Cashier is the only role that carries a sales quota.
    Private Const CASHIER_ROLE_ID As Integer = 2

    ' The system has exactly one Admin — the role is never offered by this form.
    Private Const ADMIN_ROLE_ID As Integer = 1

    ' ── DATA MODEL ────────────────────────────────────────────────────────────
    Private Class UserRecord
        Public UserId As Integer
        Public FullName As String
        Public Username As String
        Public Email As String = ""
        Public RoleId As Integer
        Public RoleName As String
        Public IsActive As Boolean
        Public CreatedAt As DateTime
        Public Quota As Decimal

        ' "Approved" on every row when the approval columns are absent, so the
        ' rest of the panel can treat status as always present.
        Public Status As String = "Approved"
        Public RequestedRoleId As Integer = 0
        Public RequestedRoleName As String = ""
        Public RejectReason As String = ""
        Public ReviewedAt As DateTime = DateTime.MinValue

        Public ReadOnly Property IsPending As Boolean
            Get
                Return String.Equals(Status, "Pending", StringComparison.OrdinalIgnoreCase)
            End Get
        End Property

        Public ReadOnly Property IsRejected As Boolean
            Get
                Return String.Equals(Status, "Rejected", StringComparison.OrdinalIgnoreCase)
            End Get
        End Property
    End Class

    ' One row of the Password Requests tab.
    Private Class ResetRequest
        Public RequestId As Integer
        Public UserId As Integer          ' 0 when the username matched nothing
        Public UsernameTyped As String
        Public FullName As String         ' "" when unmatched
        Public RoleName As String
        Public RequestedAt As DateTime
    End Class

    ' Which tab is showing. Each is a different question the admin is asking,
    ' so they get different columns and different row actions rather than one
    ' grid with a filter dropdown.
    Private Enum Tab
        AllUsers
        ResetRequests
        Disabled
    End Enum

    Private Class RoleOption
        Public RoleId As Integer
        Public RoleName As String
        Public Overrides Function ToString() As String
            Return RoleName
        End Function
    End Class

    ' Values collected by the create/edit modal.
    Private Class UserFormResult
        Public FullName As String
        Public Username As String
        Public Email As String = ""
        Public RoleId As Integer
        Public RoleName As String
        Public Password As String
        Public Quota As Decimal
    End Class

    ' ── STATE ─────────────────────────────────────────────────────────────────
    Private Shared _container As Panel
    Private Shared _searchQuery As String = ""
    Private Shared _allRows As New List(Of UserRecord)
    Private Shared _visibleRows As New List(Of UserRecord)
    Private Shared _resetRows As New List(Of ResetRequest)
    Private Shared _scrollHost As Panel
    Private Shared _reflowAttached As Boolean = False

    Private Shared _tab As Tab = Tab.AllUsers
    Private Shared _roleFilter As Integer = 0        ' 0 = every role
    Private Shared _colHeaderHost As Panel           ' swapped per tab
    Private Shared _tabStrip As Panel
    Private Shared _tileStrip As Panel

    Private Const SIDE_PAD As Integer = 24
    Private Const ROW_H As Integer = 64
    Private Const SEARCH_PLACEHOLDER As String = "Search by name or username…"

    ' One ToolTip for the whole panel; a per-row instance would leak a
    ' native handle every time the list re-renders.
    Private Shared ReadOnly _tips As New ToolTip()

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Shared Sub LoadUsers(container As Panel)
        _container = container
        ' These are Shared, so without an explicit reset the panel re-opens on
        ' whatever tab and role chip were left behind last time - while the
        ' search box, which IS reset, disagrees with them. Entering the screen
        ' should always look the same.
        _searchQuery = ""
        _tab = Tab.AllUsers
        _roleFilter = 0
        LoadData()
        Render()
    End Sub

    ' Open the panel already filtered to one role - used by the Roles screen's
    ' user count, so "1 user" is a way in rather than a dead figure.
    Public Shared Sub LoadUsersForRole(container As Panel, roleId As Integer)
        _container = container
        _searchQuery = ""
        _tab = Tab.AllUsers
        _roleFilter = roleId
        LoadData()
        Render()
    End Sub

    ' Reload from the DB and repaint, keeping the current search text.
    ' A full re-render, not just the rows: approving or rejecting changes the
    ' tab badge counts and the summary tiles too, and leaving those stale is
    ' how an admin ends up clicking a "Pending 3" tab that is actually empty.
    Private Shared Sub Refresh()
        LoadData()
        Render()
    End Sub

    ' =========================================================================
    '  DATA
    ' =========================================================================
    Private Shared Sub LoadData()
        _allRows.Clear()
        Dim hasApproval As Boolean = AccountRequests.HasApprovalColumns()
        Dim hasEmail As Boolean = AccountRequests.HasEmailColumn()

        Try
            Using conn = Db.GetConnection()
                conn.Open()

                ' The approval columns are optional. On an un-migrated database
                ' the extra projection is dropped and every row defaults to
                ' Approved, which is exactly the old behaviour.
                Dim sql As String =
                    "SELECT u.user_id, u.full_name, u.username, u.is_active, " &
                    "u.created_at, u.sales_quota, u.role_id, r.role_name" &
                    If(hasEmail, ", u.email", "") &
                    If(hasApproval,
                       ", u.status, u.requested_role_id, u.reject_reason, " &
                       "u.reviewed_at, rq.role_name AS requested_role_name", "") &
                    " FROM users u JOIN roles r ON u.role_id = r.role_id " &
                    If(hasApproval, "LEFT JOIN roles rq ON u.requested_role_id = rq.role_id ", "") &
                    "ORDER BY u.full_name"

                Using cmd As New MySqlCommand(sql, conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            Dim rec As New UserRecord With {
                                .UserId = Convert.ToInt32(rdr("user_id")),
                                .FullName = rdr("full_name").ToString(),
                                .Username = rdr("username").ToString(),
                                .RoleId = Convert.ToInt32(rdr("role_id")),
                                .RoleName = rdr("role_name").ToString(),
                                .IsActive = (Not IsDBNull(rdr("is_active"))) AndAlso Convert.ToBoolean(rdr("is_active")),
                                .CreatedAt = If(IsDBNull(rdr("created_at")), DateTime.MinValue, Convert.ToDateTime(rdr("created_at"))),
                                .Quota = If(IsDBNull(rdr("sales_quota")), 0D, Convert.ToDecimal(rdr("sales_quota")))
                            }
                            If hasEmail AndAlso Not IsDBNull(rdr("email")) Then
                                rec.Email = rdr("email").ToString()
                            End If
                            If hasApproval Then
                                rec.Status = If(IsDBNull(rdr("status")), "Approved", rdr("status").ToString())
                                rec.RequestedRoleId = If(IsDBNull(rdr("requested_role_id")), 0, Convert.ToInt32(rdr("requested_role_id")))
                                rec.RequestedRoleName = If(IsDBNull(rdr("requested_role_name")), "", rdr("requested_role_name").ToString())
                                rec.RejectReason = If(IsDBNull(rdr("reject_reason")), "", rdr("reject_reason").ToString())
                                rec.ReviewedAt = If(IsDBNull(rdr("reviewed_at")), DateTime.MinValue, Convert.ToDateTime(rdr("reviewed_at")))
                            End If
                            _allRows.Add(rec)
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load users: " & ex.Message, "Database Error",
                          MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try

        LoadResetRequests()
        ApplySearch()
    End Sub

    ' Open password-reset requests, newest first.
    Private Shared Sub LoadResetRequests()
        _resetRows.Clear()
        If Not AccountRequests.HasResetTable() Then Return

        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Dim sql As String =
                    "SELECT p.request_id, p.user_id, p.username_typed, p.requested_at, " &
                    "u.full_name, r.role_name " &
                    "FROM password_reset_requests p " &
                    "LEFT JOIN users u ON p.user_id = u.user_id " &
                    "LEFT JOIN roles r ON u.role_id = r.role_id " &
                    "WHERE p.status = 'Pending' " &
                    "ORDER BY p.requested_at DESC"
                Using cmd As New MySqlCommand(sql, conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            _resetRows.Add(New ResetRequest With {
                                .RequestId = Convert.ToInt32(rdr("request_id")),
                                .UserId = If(IsDBNull(rdr("user_id")), 0, Convert.ToInt32(rdr("user_id"))),
                                .UsernameTyped = rdr("username_typed").ToString(),
                                .FullName = If(IsDBNull(rdr("full_name")), "", rdr("full_name").ToString()),
                                .RoleName = If(IsDBNull(rdr("role_name")), "", rdr("role_name").ToString()),
                                .RequestedAt = If(IsDBNull(rdr("requested_at")), DateTime.MinValue, Convert.ToDateTime(rdr("requested_at")))
                            })
                        End While
                    End Using
                End Using
            End Using
        Catch
            ' A missing or unreadable request table must not stop the user list
            ' from rendering; the tab simply shows as empty.
        End Try
    End Sub

    ' Tab first, then role chip, then the search text - so the counts on the
    ' tabs always describe the whole tab, not what the search happens to leave.
    Private Shared Sub ApplySearch()
        _visibleRows.Clear()

        Dim q As String = If(_searchQuery, "").Trim().ToLower()

        For Each r In _allRows
            If Not InCurrentTab(r) Then Continue For
            If _roleFilter <> 0 AndAlso EffectiveRoleId(r) <> _roleFilter Then Continue For
            If q <> "" AndAlso Not (r.FullName.ToLower().Contains(q) OrElse
                                    r.Username.ToLower().Contains(q)) Then Continue For
            _visibleRows.Add(r)
        Next
    End Sub

    ' The role a row actually shows. A pending application displays the role it
    ' ASKED for, so that is what a role chip has to match on that tab -
    ' filtering by role_id there would compare against a grant that has not
    ' happened yet and hide rows whose visible badge matches the chip.
    Private Shared Function EffectiveRoleId(r As UserRecord) As Integer
        Return r.RoleId
    End Function

    ' Self sign-up is gone - only an admin creates accounts - so there are no
    ' applications to review. Rows left over from the old sign-up screen
    ' (status Pending / Rejected) were never accounts: they stay out of every
    ' tab and cannot sign in.
    Private Shared Function InCurrentTab(r As UserRecord) As Boolean
        Select Case _tab
            Case Tab.Disabled
                Return (Not r.IsActive) AndAlso (Not r.IsPending) AndAlso (Not r.IsRejected)
            Case Tab.AllUsers
                Return (Not r.IsPending) AndAlso (Not r.IsRejected)
            Case Else
                Return True
        End Select
    End Function

    ' Counts a tab by running its own membership test, so a badge can never
    ' promise rows the tab does not then show.
    Private Shared Function TabCount(t As Tab) As Integer
        Dim saved As Tab = _tab
        Dim n As Integer = 0
        Try
            _tab = t
            For Each r In _allRows
                If InCurrentTab(r) Then n += 1
            Next
        Finally
            _tab = saved
        End Try
        Return n
    End Function

    Private Shared Function ActiveCount() As Integer
        Dim n As Integer = 0
        For Each r In _allRows
            If r.IsActive AndAlso Not r.IsPending AndAlso Not r.IsRejected Then n += 1
        Next
        Return n
    End Function

    Private Shared Function DisabledCount() As Integer
        Dim n As Integer = 0
        For Each r In _allRows
            If (Not r.IsActive) AndAlso (Not r.IsPending) AndAlso (Not r.IsRejected) Then n += 1
        Next
        Return n
    End Function

    Private Shared Function LoadRoles() As List(Of RoleOption)
        Dim list As New List(Of RoleOption)
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT role_id, role_name FROM roles " &
                    "WHERE role_id <> " & ADMIN_ROLE_ID & " ORDER BY role_id", conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            list.Add(New RoleOption With {
                                .RoleId = Convert.ToInt32(rdr("role_id")),
                                .RoleName = rdr("role_name").ToString()
                            })
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load roles: " & ex.Message, "Database Error",
                          MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
        Return list
    End Function

    ' Username uniqueness — checked before INSERT.
    Private Shared Function UsernameTaken(username As String) As Boolean
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT COUNT(*) FROM users WHERE username = @u", conn)
                    cmd.Parameters.AddWithValue("@u", username)
                    Return Convert.ToInt32(cmd.ExecuteScalar()) > 0
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not verify the username: " & ex.Message, "Database Error",
                          MessageBoxButtons.OK, MessageBoxIcon.Error)
            ' Fail closed — treat as taken so we never insert a duplicate blind.
            Return True
        End Try
    End Function

    ' =========================================================================
    '  RENDER
    ' =========================================================================
    Private Shared Sub Render()
        ' Re-filter FIRST. Render is what a tab switch and a role chip both call,
        ' but ApplySearch was only ever run from LoadData and the search box -
        ' so switching tabs repainted the stale _visibleRows from load time and
        ' every tab showed the full user list. Pending and Disabled both listed
        ' six approved, active users because of this.
        ApplySearch()

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
            .Padding = New Padding(0, 4, 0, 8)
        }

        ' The header is rebuilt whenever the tab changes, so it lives in a host
        ' panel the tab switch can empty instead of being added to listArea
        ' directly (which would fight the Fill/Top docking order).
        _colHeaderHost = New Panel With {
            .Height = 44,
            .Dock = DockStyle.Top,
            .BackColor = Color.Transparent
        }
        _colHeaderHost.Controls.Add(BuildColHeader())

        listArea.Controls.Add(_colHeaderHost)
        listArea.Controls.Add(_scrollHost)
        _colHeaderHost.BringToFront()
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

        _tabStrip = BuildTabStrip()
        _tabStrip.Dock = DockStyle.Top

        _tileStrip = BuildTileStrip()
        _tileStrip.Dock = DockStyle.Top

        ' Docked Top children stack in REVERSE order of addition, so the last
        ' one added ends up nearest the top of the panel.
        _container.Controls.Add(tableWrapper)
        _container.Controls.Add(_tabStrip)
        _container.Controls.Add(_tileStrip)
        _container.Controls.Add(topRegion)

        _container.ResumeLayout()

        PopulateRows()
    End Sub

    ' =========================================================================
    '  SUMMARY TILES  —  the shape of the user base at a glance
    ' =========================================================================
    ' =========================================================================
    '  SUMMARY STRIP
    '
    '  Same geometry as the Admin oversight panels (Sales / Inventory /
    '  Procurement): a 110px band of equal white cards in a TableLayoutPanel.
    '  The earlier version used 74px hand-positioned tiles with tinted
    '  backgrounds, which read as four coloured blocks rather than as figures
    '  and did not line up with any other screen in the system. Colour belongs
    '  on the number, not behind it.
    ' =========================================================================
    ' The Cashier home stat row is the reference every KPI row in the system
    ' follows: a 140px band of four equal white cards, caption 11pt at (22,14),
    ' value 26pt bold at (20,36), sub-label 10pt at (22,100), 12px between
    ' cards. Matching it exactly is the point - a card that is nearly the same
    ' size reads as a mistake, not as a variation.
    Private Const SUMMARY_H As Integer = 140

    Private Shared Function BuildTileStrip() As Panel
        Dim bar As New Panel With {
            .Height = SUMMARY_H + 12,
            .BackColor = MintBg,
            .Padding = New Padding(SIDE_PAD, 0, SIDE_PAD, 12)
        }

        Dim grid As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .ColumnCount = 4,
            .RowCount = 1,
            .BackColor = Color.Transparent,
            .CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        }
        For i As Integer = 0 To 3
            grid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 25.0F))
        Next
        grid.RowStyles.Add(New RowStyle(SizeType.Percent, 100))

        ' Leftover sign-up rows are not accounts, so Total = Active + Disabled.
        Dim accountTotal As Integer = ActiveCount() + DisabledCount()

        Dim defs = {
            ("Total Users", accountTotal.ToString("N0"), "accounts on file",
             MintPrimary, ""),
            ("Active", ActiveCount().ToString("N0"), "able to log in",
             MintDark, "Live"),
            ("Disabled", DisabledCount().ToString("N0"), "switched off by an admin",
             AccentBlue, ""),
            ("Password Requests", _resetRows.Count.ToString("N0"), "waiting for a reset",
             AccentOrange, If(_resetRows.Count > 0, "Review", ""))
        }

        For i As Integer = 0 To 3
            Dim d = defs(i)
            Dim card = MakeSummaryCard(d.Item1, d.Item4, d.Item2, d.Item3, d.Item5)
            card.Margin = New Padding(0, 0, If(i < 3, 12, 0), 0)
            grid.Controls.Add(card, i, 0)
        Next

        bar.Controls.Add(grid)
        Return bar
    End Function

    Private Shared Function MakeSummaryCard(caption As String, valueClr As Color,
                                            value As String, subLabel As String,
                                            badgeText As String) As Panel
        Dim card As New BufferedPanel With {.Dock = DockStyle.Fill, .BackColor = CardBg}
        AddHandler card.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)

        card.Controls.Add(New Label With {
            .Text = caption,
            .Font = New Font("Segoe UI", 11),
            .ForeColor = SubText,
            .AutoSize = True,
            .Location = New Point(22, 14),
            .BackColor = Color.Transparent
        })
        card.Controls.Add(New Label With {
            .Text = value,
            .Font = New Font("Segoe UI", 26, FontStyle.Bold),
            .ForeColor = valueClr,
            .AutoSize = True,
            .Location = New Point(20, 36),
            .BackColor = Color.Transparent
        })
        card.Controls.Add(New Label With {
            .Text = subLabel,
            .Font = New Font("Segoe UI", 10),
            .ForeColor = SubText,
            .AutoSize = True,
            .Location = New Point(22, 100),
            .BackColor = Color.Transparent
        })

        ' Same corner badge the Cashier cards use, on the two that carry a
        ' state worth calling out.
        If badgeText <> "" Then
            Dim badge As New Label With {
                .Text = badgeText,
                .Font = New Font("Segoe UI", 9, FontStyle.Bold),
                .ForeColor = MintPrimary,
                .BackColor = Color.FromArgb(236, 248, 244),
                .AutoSize = True,
                .Padding = New Padding(6, 3, 6, 3),
                .Anchor = AnchorStyles.Right Or AnchorStyles.Top
            }
            card.Controls.Add(badge)
            AddHandler card.Resize,
                Sub()
                    badge.Left = card.Width - badge.Width - 14
                    badge.Top = 14
                End Sub
        End If

        Return card
    End Function

    ' =========================================================================
    '  TAB STRIP  —  All Users, Password Requests, Disabled
    ' =========================================================================
    Private Shared Function BuildTabStrip() As Panel
        Dim strip As New Panel With {
            .Height = 52,
            .BackColor = MintBg,
            .Padding = New Padding(SIDE_PAD, 0, SIDE_PAD, 0)
        }

        Dim defs = {
            (Tab.AllUsers, "All Users", TabCount(Tab.AllUsers)),
            (Tab.ResetRequests, "Password Requests", _resetRows.Count),
            (Tab.Disabled, "Disabled", DisabledCount())
        }

        Dim x As Integer = SIDE_PAD
        For Each d In defs
            Dim tabId = d.Item1
            Dim caption = d.Item2
            Dim count = d.Item3
            Dim isSel As Boolean = (_tab = tabId)

            Dim btn As New BufferedPanel With {
                .Height = 38,
                .Top = 6,
                .Left = x,
                .BackColor = Color.Transparent,
                .Cursor = Cursors.Hand
            }

            ' Width follows the caption plus room for the count pill.
            Dim capW As Integer = TextRenderer.MeasureText(caption,
                New Font("Segoe UI Semibold", 10)).Width
            Dim pillW As Integer = If(count > 0, 30, 0)
            btn.Width = capW + pillW + 34

            ' A count that needs attention is amber; the rest stay neutral so
            ' the badge means "there is work here", not merely "there are rows".
            Dim urgent As Boolean = (tabId = Tab.ResetRequests) AndAlso count > 0

            AddHandler btn.Paint,
                Sub(sn, e)
                    Dim g = e.Graphics
                    g.SmoothingMode = SmoothingMode.AntiAlias
                    Dim rc = btn.ClientRectangle : rc.Inflate(-1, -1)
                    If isSel Then
                        Using b As New SolidBrush(CardBg)
                            g.FillRoundedRectangle(b, rc, 10)
                        End Using
                        Using pn As New Pen(MintPrimary, 1.4F)
                            g.DrawRoundedRectangle(pn, rc, 10)
                        End Using
                    End If

                    Dim fg As Color = If(isSel, MintDark, SubText)
                    TextRenderer.DrawText(g, caption,
                        New Font("Segoe UI Semibold", 10),
                        New Rectangle(16, 0, capW + 4, btn.Height), fg,
                        TextFormatFlags.Left Or TextFormatFlags.VerticalCenter)

                    If count > 0 Then
                        Dim px As Integer = 16 + capW + 8
                        Dim pillRect As New Rectangle(px, (btn.Height - 20) \ 2, 26, 20)
                        Using b As New SolidBrush(If(urgent,
                                Color.FromArgb(255, 236, 205), Color.FromArgb(232, 238, 236)))
                            g.FillRoundedRectangle(b, pillRect, 9)
                        End Using
                        TextRenderer.DrawText(g, count.ToString(),
                            New Font("Segoe UI", 8.5F, FontStyle.Bold), pillRect,
                            If(urgent, Color.FromArgb(170, 105, 15), SubText),
                            TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
                    End If
                End Sub

            AddHandler btn.Click, Sub() SwitchTab(tabId)
            AddHandler btn.MouseEnter, Sub() btn.Invalidate()
            AddHandler btn.MouseLeave, Sub() btn.Invalidate()

            strip.Controls.Add(btn)
            x += btn.Width + 8
        Next

        ' ── ROLE CHIPS ───────────────────────────────────────────────────────
        ' Right-aligned, and only where filtering by role is meaningful. The
        ' pending queue is short and sorted by decision, not by department.
        ' Every tab with a role column gets the chips. Password Requests is the
        ' exception - its rows are requests, and one may not match an account
        ' at all, so it has no role to filter on.
        If _tab <> Tab.ResetRequests Then
            Dim chipDefs As New List(Of Tuple(Of Integer, String))
            chipDefs.Add(Tuple.Create(0, "All roles"))
            For Each ro In LoadRoles()
                chipDefs.Add(Tuple.Create(ro.RoleId, ro.RoleName))
            Next
            ' LoadRoles omits Admin, but admins do appear in this list, so the
            ' chip has to be offered or they could never be filtered to.
            chipDefs.Add(Tuple.Create(ADMIN_ROLE_ID, "Admin"))

            Dim chips As New List(Of Panel)
            For Each cd In chipDefs
                Dim rid = cd.Item1
                Dim nm = cd.Item2
                Dim sel As Boolean = (_roleFilter = rid)

                Dim chip As New BufferedPanel With {
                    .Height = 26,
                    .Top = 12,
                    .BackColor = Color.Transparent,
                    .Cursor = Cursors.Hand
                }
                chip.Width = TextRenderer.MeasureText(nm,
                    New Font("Segoe UI", 9)).Width + 24

                AddHandler chip.Paint,
                    Sub(sn, e)
                        Dim g = e.Graphics
                        g.SmoothingMode = SmoothingMode.AntiAlias
                        Dim rc = chip.ClientRectangle : rc.Inflate(-1, -1)
                        Using b As New SolidBrush(If(sel, MintPrimary, CardBg))
                            g.FillRoundedRectangle(b, rc, 12)
                        End Using
                        If Not sel Then
                            Using pn As New Pen(DividerClr, 1)
                                g.DrawRoundedRectangle(pn, rc, 12)
                            End Using
                        End If
                        TextRenderer.DrawText(g, nm, New Font("Segoe UI", 9),
                            chip.ClientRectangle, If(sel, Color.White, SubText),
                            TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
                    End Sub

                ' Render() rebuilds the strip, which is what repaints the chips
                ' in their new selected state - so it is the only call needed.
                AddHandler chip.Click,
                    Sub()
                        _roleFilter = If(_roleFilter = rid, 0, rid)
                        Render()
                    End Sub

                chips.Add(chip)
                strip.Controls.Add(chip)
            Next

            ' Laid out from the right edge so the chips stay put as the window
            ' resizes, and never collide with the tabs on the left.
            AddHandler strip.Resize,
                Sub()
                    Dim rx As Integer = strip.Width - SIDE_PAD
                    For i As Integer = chips.Count - 1 To 0 Step -1
                        rx -= chips(i).Width
                        chips(i).Left = rx
                        rx -= 6
                    Next
                End Sub
        End If

        ' Hairline under the strip so the tabs sit on the table, not float.
        AddHandler strip.Paint,
            Sub(sn, e)
                Using pn As New Pen(DividerClr, 1)
                    e.Graphics.DrawLine(pn, SIDE_PAD, strip.Height - 1,
                                        strip.Width - SIDE_PAD, strip.Height - 1)
                End Using
            End Sub

        Return strip
    End Function

    ' Switching tab resets the role chip: a role filter carried over from
    ' All Users would silently hide pending applications.
    Private Shared Sub SwitchTab(t As Tab)
        If _tab = t Then Return
        _tab = t
        ' Both filters reset. A role chip carried over would hide pending
        ' applications; a search term carried over would be worse still,
        ' because Render() rebuilds the search box showing its placeholder -
        ' the filter would stay live with nothing on screen to say so.
        _roleFilter = 0
        _searchQuery = ""
        Render()
    End Sub

    ' =========================================================================
    '  TOP REGION  —  72px title bar: title + search + "+ New User"
    ' =========================================================================
    Private Shared Function BuildTopRegion() As Panel
        Dim region As New Panel With {
            .BackColor = MintBg,
            .AutoSize = False,
            .Height = 72
        }

        Dim titleBar As New Panel With {
            .Height = 72,
            .Dock = DockStyle.Top,
            .BackColor = Color.Transparent
        }

        Dim lblTitle As New Label With {
            .Text = "User Management",
            .Font = New Font("Segoe UI", 28, FontStyle.Bold),
            .ForeColor = MintDark,
            .AutoSize = True,
            .Location = New Point(SIDE_PAD, 16)
        }

        ' ── "+ New User" — primary action ────────────────────────────────────
        Dim btnNew As New Button With {
            .Text = "+  New User",
            .FlatStyle = FlatStyle.Flat,
            .Cursor = Cursors.Hand,
            .Size = New Size(126, 34),
            .Font = New Font("Segoe UI Semibold", 9.5F),
            .BackColor = MintPrimary,
            .ForeColor = Color.White,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        btnNew.FlatAppearance.BorderSize = 0
        AddHandler btnNew.Paint,
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
        AddHandler btnNew.MouseEnter, Sub()
                                          btnNew.BackColor = MintDark
                                          btnNew.Invalidate()
                                      End Sub
        AddHandler btnNew.MouseLeave, Sub()
                                          btnNew.BackColor = MintPrimary
                                          btnNew.Invalidate()
                                      End Sub
        AddHandler btnNew.Click, Sub() CreateUser()

        ' ── Refresh ──────────────────────────────────────────────────────────

        ' ── Search box ───────────────────────────────────────────────────────
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
            .ForeColor = If(_searchQuery = "", SubText, Color.FromArgb(45, 45, 45)),
            .Text = If(_searchQuery = "", SEARCH_PLACEHOLDER, _searchQuery),
            .Location = New Point(34, 9),
            .Width = 196
        }
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
                ApplySearch()
                PopulateRows()
            End Sub
        AddHandler searchWrap.Click, Sub() txtSearch.Focus()
        searchWrap.Controls.Add(txtSearch)

        AddHandler titleBar.Resize,
            Sub()
                btnNew.Left = titleBar.Width - btnNew.Width - SIDE_PAD
                btnNew.Top = (titleBar.Height - btnNew.Height) \ 2
                searchWrap.Left = btnNew.Left - searchWrap.Width - 10
                searchWrap.Top = (titleBar.Height - searchWrap.Height) \ 2
            End Sub

        titleBar.Controls.Add(lblTitle)
        titleBar.Controls.Add(searchWrap)
        titleBar.Controls.Add(btnNew)

        region.Controls.Add(titleBar)
        Return region
    End Function

    ' =========================================================================
    '  COLUMN HEADER  —  44px, proportional label layout
    ' =========================================================================
    Private Shared Function BuildColHeader() As Panel
        Dim hdr As New Panel With {.Height = 44, .Dock = DockStyle.Fill,
                                   .BackColor = Color.Transparent}

        ' Each tab answers a different question, so each gets its own columns
        ' rather than one grid with blank cells where a column does not apply.
        Dim captions As String()
        Dim stops As Double()
        Select Case _tab
            Case Tab.ResetRequests
                captions = {"ACCOUNT", "USERNAME TYPED", "ROLE", "REQUESTED", "ACTIONS"}
                stops = {0.0, 0.24, 0.44, 0.58, 0.72}
            Case Else
                captions = {"FULL NAME", "USERNAME", "ROLE", "STATUS", "CREATED", "ACTIONS"}
                stops = {0.0, 0.2, 0.34, 0.45, 0.56, 0.7}
        End Select

        Dim labels(captions.Length - 1) As Label
        For i As Integer = 0 To captions.Length - 1
            labels(i) = MakeColLabel(captions(i), New Point(0, 13))
        Next
        hdr.Controls.AddRange(labels)

        AddHandler hdr.Resize,
            Sub()
                Dim w = hdr.Width
                For i As Integer = 0 To labels.Length - 1
                    labels(i).Left = If(i = 0, 24, CInt(w * stops(i)))
                Next
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
    '  POPULATE ROWS
    ' =========================================================================
    Private Shared Sub PopulateRows()
        If _scrollHost Is Nothing Then Return

        _scrollHost.SuspendLayout()
        _scrollHost.Controls.Clear()

        If _reflowAttached Then
            RemoveHandler _scrollHost.Resize, AddressOf ReflowScrollHost
            _reflowAttached = False
        End If

        ' The password-request tab reads a different list entirely.
        If _tab = Tab.ResetRequests Then
            ' Honour the search box here too. It stays enabled on this tab, so
            ' a query that filtered nothing would read as a broken control.
            Dim rq As String = If(_searchQuery, "").Trim().ToLower()
            Dim shown As New List(Of ResetRequest)
            For Each q In _resetRows
                If rq = "" OrElse q.UsernameTyped.ToLower().Contains(rq) OrElse
                   q.FullName.ToLower().Contains(rq) Then
                    shown.Add(q)
                End If
            Next

            If shown.Count = 0 Then
                _scrollHost.Controls.Add(BuildEmptyRow(
                    If(rq <> "",
                       "No password requests match " & Chr(34) & _searchQuery & Chr(34) & ".",
                       "No password reset requests. When someone uses " &
                       Chr(34) & "Forgot password?" & Chr(34) &
                       " on the login screen, it appears here.")))
            Else
                Dim ry As Integer = 4
                For Each q As ResetRequest In shown
                    Dim row As Panel = BuildResetRow(q, ROW_H)
                    row.Location = New Point(0, ry)
                    row.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
                    _scrollHost.Controls.Add(row)
                    row.Width = _scrollHost.ClientSize.Width - 4
                    ry += ROW_H
                Next
                If Not _reflowAttached Then
                    _reflowAttached = True
                    AddHandler _scrollHost.Resize, AddressOf ReflowScrollHost
                End If
            End If

            _scrollHost.ResumeLayout()
            _scrollHost.Invalidate(True)
            Return
        End If

        If _visibleRows.Count = 0 Then
            _scrollHost.Controls.Add(BuildEmptyRow(EmptyMessage()))
        Else
            Dim y As Integer = 4
            For Each r As UserRecord In _visibleRows
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

    ' Says why the list is empty - a search that matched nothing reads very
    ' differently from a queue that is genuinely clear.
    Private Shared Function EmptyMessage() As String
        If Not String.IsNullOrWhiteSpace(_searchQuery) Then
            Return "No results for " & Chr(34) & _searchQuery & Chr(34) & "."
        End If
        Select Case _tab
            Case Tab.Disabled
                Return "No disabled accounts."
            Case Else
                Return "No users found."
        End Select
    End Function

    ' ── PASSWORD REQUEST ROW ─────────────────────────────────────────────────
    Private Shared Function BuildResetRow(q As ResetRequest, height As Integer) As Panel
        Dim p As New BufferedPanel With {
            .Height = height,
            .BackColor = Color.Transparent
        }

        ' A request whose username matched no account is shown, not hidden:
        ' a burst of them is worth an admin's attention.
        Dim matched As Boolean = (q.UserId > 0)

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
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 20, p.Height - 1, p.Width - 20, p.Height - 1)
                End Using
            End Sub

        Dim lblName As New Label With {
            .Text = If(matched, q.FullName, "Unknown account"),
            .Font = New Font("Segoe UI", 11, FontStyle.Bold),
            .ForeColor = If(matched, MintDark, FaintText),
            .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblUser As New Label With {
            .Text = q.UsernameTyped,
            .Font = New Font("Segoe UI", 11),
            .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblRole As New Label With {
            .Text = If(q.RoleName <> "", q.RoleName, "-"),
            .Font = New Font("Segoe UI", 11),
            .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblWhen As New Label With {
            .Text = RelativeTime(q.RequestedAt),
            .Font = New Font("Segoe UI", 11),
            .ForeColor = FaintText, .AutoSize = True, .BackColor = Color.Transparent
        }

        Dim btnIssue As Button = MakeActionButton("Issue Password",
            Color.FromArgb(232, 238, 250), Color.FromArgb(45, 80, 150))
        btnIssue.Width = 120
        Dim btnDismiss As Button = MakeActionButton("Dismiss",
            Color.FromArgb(240, 242, 241), SubText)

        ' Nothing to reset when the username never matched an account.
        btnIssue.Enabled = matched
        If Not matched Then btnIssue.ForeColor = FaintText

        AddHandler btnIssue.Click, Sub() IssueResetPassword(q)
        AddHandler btnDismiss.Click, Sub() DismissResetRequest(q)

        p.Controls.AddRange({lblName, lblUser, lblRole, lblWhen, btnIssue, btnDismiss})

        AddHandler p.Resize,
            Sub()
                Dim w = p.Width
                lblName.Left = 24 : lblName.Top = (height - lblName.Height) \ 2
                lblUser.Left = CInt(w * 0.24) : lblUser.Top = (height - lblUser.Height) \ 2
                lblRole.Left = CInt(w * 0.44) : lblRole.Top = (height - lblRole.Height) \ 2
                lblWhen.Left = CInt(w * 0.58) : lblWhen.Top = (height - lblWhen.Height) \ 2

                Dim ax As Integer = CInt(w * 0.72)
                Dim ay As Integer = (height - btnIssue.Height) \ 2
                btnIssue.Left = ax : btnIssue.Top = ay
                btnDismiss.Left = btnIssue.Right + 6 : btnDismiss.Top = ay
            End Sub

        PropagateHover(p)
        Return p
    End Function

    ' "3 hours ago" reads faster than a timestamp when triaging a queue.
    Private Shared Function RelativeTime(t As DateTime) As String
        If t = DateTime.MinValue Then Return "-"
        Dim span As TimeSpan = DateTime.Now - t
        If span.TotalMinutes < 1 Then Return "just now"
        If span.TotalMinutes < 60 Then Return CInt(span.TotalMinutes) & " min ago"
        If span.TotalHours < 24 Then Return CInt(span.TotalHours) & " hr ago"
        If span.TotalDays < 7 Then Return CInt(span.TotalDays) & "d ago"
        Return t.ToString("MM/dd/yy")
    End Function

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

    ' ── Single data row (64px) ───────────────────────────────────────────────
    Private Shared Function BuildDataRow(r As UserRecord, height As Integer) As Panel
        Dim p As New BufferedPanel With {
            .Height = height,
            .BackColor = Color.Transparent
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
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 20, p.Height - 1, p.Width - 20, p.Height - 1)
                End Using
            End Sub

        Dim lblName As New Label With {
            .Text = r.FullName,
            .Font = New Font("Segoe UI", 11, FontStyle.Bold),
            .ForeColor = If(r.IsActive, MintDark, FaintText),
            .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblUser As New Label With {
            .Text = r.Username,
            .Font = New Font("Segoe UI", 11),
            .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent
        }
        ' Email under the name. A missing one is flagged, because without it
        ' the person cannot receive the code needed to change their password.
        Dim noEmail As Boolean = AccountRequests.HasEmailColumn() AndAlso r.Email = ""
        Dim lblEmail As New Label With {
            .Text = If(noEmail, "No email - click Edit to add", r.Email),
            .Font = New Font("Segoe UI", 8.5F),
            .ForeColor = If(noEmail, Theme.Warning, FaintText),
            .AutoSize = True, .BackColor = Color.Transparent,
            .Visible = AccountRequests.HasEmailColumn()
        }
        Dim lblRole As New Label With {
            .Text = r.RoleName,
            .Font = New Font("Segoe UI", 11),
            .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblCreated As New Label With {
            .Text = If(r.CreatedAt = DateTime.MinValue, "—", r.CreatedAt.ToString("MM/dd/yy")),
            .Font = New Font("Segoe UI", 11),
            .ForeColor = FaintText, .AutoSize = True, .BackColor = Color.Transparent
        }

        Dim badgeColors = If(r.IsActive, BadgeActive, BadgeDisabled)
        Dim badge As New Label With {
            .Text = If(r.IsActive, "Active", "Disabled"),
            .Font = New Font("Segoe UI", 9, FontStyle.Bold),
            .ForeColor = badgeColors.fore,
            .BackColor = badgeColors.back,
            .AutoSize = True,
            .Padding = New Padding(10, 4, 10, 4)
        }
        RoundLabel(badge, 10)

        ' ── Row actions ──────────────────────────────────────────────────────
        Dim btnEdit As Button = MakeActionButton("Edit", Color.FromArgb(220, 235, 232), MintDark)
        Dim btnToggle As Button = MakeActionButton(
            If(r.IsActive, "Disable", "Enable"),
            If(r.IsActive, Color.FromArgb(255, 232, 232), Color.FromArgb(224, 244, 236)),
            If(r.IsActive, AccentRed, Color.FromArgb(0, 120, 92)))
        Dim btnReset As Button = MakeActionButton("Reset PW", Color.FromArgb(232, 238, 250), Color.FromArgb(45, 80, 150))

        AddHandler btnEdit.Click, Sub() EditUser(r)
        AddHandler btnToggle.Click, Sub() ToggleActive(r)
        AddHandler btnReset.Click, Sub() ResetPassword(r)

        p.Controls.AddRange({lblName, lblEmail, lblUser, lblRole, badge, lblCreated,
                             btnEdit, btnToggle, btnReset})

        AddHandler p.Resize,
            Sub()
                Dim w = p.Width
                If lblEmail.Visible Then
                    Dim blockH As Integer = lblName.Height + lblEmail.Height
                    lblName.Left = 24 : lblName.Top = (height - blockH) \ 2
                    lblEmail.Left = 25 : lblEmail.Top = lblName.Bottom
                Else
                    lblName.Left = 24 : lblName.Top = (height - lblName.Height) \ 2
                End If
                lblUser.Left = CInt(w * 0.2) : lblUser.Top = (height - lblUser.Height) \ 2
                lblRole.Left = CInt(w * 0.34) : lblRole.Top = (height - lblRole.Height) \ 2
                badge.Left = CInt(w * 0.45) : badge.Top = (height - badge.Height) \ 2
                lblCreated.Left = CInt(w * 0.56) : lblCreated.Top = (height - lblCreated.Height) \ 2

                Dim ax As Integer = CInt(w * 0.7)
                Dim ay As Integer = (height - btnEdit.Height) \ 2
                btnEdit.Left = ax : btnEdit.Top = ay
                btnToggle.Left = btnEdit.Right + 6 : btnToggle.Top = ay
                btnReset.Left = btnToggle.Right + 6 : btnReset.Top = ay
            End Sub

        PropagateHover(p)
        Return p
    End Function

    ' Small pill-style row action button.
    Private Shared Function MakeActionButton(text As String, backClr As Color, foreClr As Color) As Button
        Dim b As New Button With {
            .Text = text,
            .FlatStyle = FlatStyle.Flat,
            .Cursor = Cursors.Hand,
            .Size = New Size(84, 30),
            .Font = New Font("Segoe UI Semibold", 9.0F),
            .BackColor = backClr,
            .ForeColor = foreClr
        }
        b.FlatAppearance.BorderSize = 0

        Dim normalClr As Color = backClr
        Dim hoverClr As Color = Color.FromArgb(Math.Max(0, backClr.R - 14),
                                               Math.Max(0, backClr.G - 14),
                                               Math.Max(0, backClr.B - 14))

        AddHandler b.Paint,
            Sub(s, e)
                Dim bb = CType(s, Button)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect = bb.ClientRectangle : rect.Inflate(-1, -1)
                Using br As New SolidBrush(bb.BackColor)
                    g.FillRoundedRectangle(br, rect, 7)
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
    '  ACTION 1 — CREATE
    ' =========================================================================
    Private Shared Sub CreateUser()
        Dim res As UserFormResult = ShowUserForm("New User", True, Nothing)
        If res Is Nothing Then Return

        Dim hasEmail As Boolean = AccountRequests.HasEmailColumn()
        Dim newId As Integer
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "INSERT INTO users (role_id, full_name, username, " &
                    If(hasEmail, "email, ", "") & "password_hash, is_active, sales_quota) " &
                    "VALUES (@rid, @fn, @un, " & If(hasEmail, "@em, ", "") & "@pw, 1, @q)", conn)
                    cmd.Parameters.AddWithValue("@rid", res.RoleId)
                    If hasEmail Then cmd.Parameters.AddWithValue("@em", res.Email)
                    cmd.Parameters.AddWithValue("@fn", res.FullName)
                    cmd.Parameters.AddWithValue("@un", res.Username)
                    cmd.Parameters.AddWithValue("@pw", PasswordHelper.HashPassword(res.Password))
                    cmd.Parameters.AddWithValue("@q", res.Quota)
                    cmd.ExecuteNonQuery()
                End Using
                Using cmd As New MySqlCommand("SELECT LAST_INSERT_ID()", conn)
                    newId = Convert.ToInt32(cmd.ExecuteScalar())
                End Using

                ActivityLog.Record("USER_CREATE", "user", newId,
                                   "Created " & res.Username & " (" & res.RoleName & ")")
            End Using
        Catch ex As MySqlException When ex.Number = 1062
            ' Unique username / email index - someone took it since the check.
            AppModal.Show("That username or email is already used by another account.",
                          "User Not Created", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        Catch ex As Exception
            AppModal.Show("Could not create the user: " & ex.Message, "Database Error",
                          MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try

        ' Tell the person their account exists. Sent in the background: the
        ' account is already created, so a slow or failed email must not hold
        ' the admin up (a failure is written to the activity log instead).
        Dim notice As String = ""
        If hasEmail AndAlso res.Email <> "" Then
            Mailer.SendInBackground(res.Email,
                "Your " & StoreSettings.StoreName & " account was created",
                Mailer.AccountCreatedBody(res.FullName, res.Username, res.RoleName),
                "user", newId)
            notice = vbCrLf & vbCrLf & "A notice is being emailed to " & res.Email & "."
        End If

        AppModal.Show("User """ & res.Username & """ was created." & notice, "User Created",
                      MessageBoxButtons.OK, MessageBoxIcon.Information)
        Refresh()
    End Sub

    ' =========================================================================
    '  ACTION 2 — EDIT  (name / role / quota only — never the password)
    ' =========================================================================
    Private Shared Sub EditUser(r As UserRecord)
        Dim res As UserFormResult = ShowUserForm("Edit User", False, r)
        If res Is Nothing Then Return

        Dim hasEmail As Boolean = AccountRequests.HasEmailColumn()
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "UPDATE users SET full_name=@fn, role_id=@rid, sales_quota=@q" &
                    If(hasEmail, ", email=@em", "") & " WHERE user_id=@id", conn)
                    If hasEmail Then
                        cmd.Parameters.AddWithValue("@em",
                            If(res.Email = "", CObj(DBNull.Value), CObj(res.Email)))
                    End If
                    cmd.Parameters.AddWithValue("@fn", res.FullName)
                    cmd.Parameters.AddWithValue("@rid", res.RoleId)
                    cmd.Parameters.AddWithValue("@q", res.Quota)
                    cmd.Parameters.AddWithValue("@id", r.UserId)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        Catch ex As MySqlException When ex.Number = 1062
            AppModal.Show("That email is already used by another account.",
                          "User Not Saved", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        Catch ex As Exception
            AppModal.Show("Could not update the user: " & ex.Message, "Database Error",
                          MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try

        ActivityLog.Record("USER_EDIT", "user", r.UserId, r.Username)
        Refresh()
    End Sub

    ' =========================================================================
    '  ACTION 3 — ENABLE / DISABLE
    ' =========================================================================
    Private Shared Sub ToggleActive(r As UserRecord)
        Dim newState As Boolean = Not r.IsActive

        ' Guard — an admin must never lock themselves out.
        If Not newState AndAlso r.UserId = Session.CurrentUserId Then
            AppModal.Show("You cannot disable the account you are currently signed in with.",
                          "Action Blocked", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        ' Guard — nor may the LAST enabled admin be disabled. Blocking only
        ' self-disable still allowed one admin to disable the other and leave
        ' the store with no way back into user management at all.
        If Not newState AndAlso r.RoleId = ADMIN_ROLE_ID AndAlso CountActiveAdmins() <= 1 Then
            AppModal.Show("This is the only enabled administrator account." & Environment.NewLine &
                          "Disabling it would leave no one able to manage users." & Environment.NewLine &
                          Environment.NewLine &
                          "Enable another administrator first.",
                          "Action Blocked", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        Dim verb As String = If(newState, "enable", "disable")
        If AppModal.Show("Are you sure you want to " & verb & " """ & r.Username & """?",
                         "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then
            Return
        End If

        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "UPDATE users SET is_active=@a WHERE user_id=@id", conn)
                    cmd.Parameters.AddWithValue("@a", If(newState, 1, 0))
                    cmd.Parameters.AddWithValue("@id", r.UserId)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not update the account status: " & ex.Message, "Database Error",
                          MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try

        ActivityLog.Record(If(newState, "USER_ENABLE", "USER_DISABLE"), "user", r.UserId, r.Username)
        Refresh()
    End Sub

    ' Number of enabled admin accounts. Returns 2 on error so a database
    ' problem blocks the disable rather than waving through a lockout.
    ' =========================================================================
    '  ACTION - ISSUE A PASSWORD FOR A RESET REQUEST
    '
    '  There is no mail server in this system, so the new password is shown to
    '  the admin to hand over in person, and the account is flagged to force a
    '  change at next sign-in.
    ' =========================================================================
    Private Shared Sub IssueResetPassword(q As ResetRequest)
        If q.UserId <= 0 Then Return

        If Not ConfirmActingAdmin("Issue a new password for " & Chr(34) & q.UsernameTyped & Chr(34) & ".") Then
            Return
        End If

        Dim temp As String = AccountRequests.GenerateTempPassword()

        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Dim sql As String =
                    "UPDATE users SET password_hash=@pw" &
                    If(AccountRequests.HasApprovalColumns(), ", must_change_password=1", "") &
                    " WHERE user_id=@id"
                Using cmd As New MySqlCommand(sql, conn)
                    cmd.Parameters.AddWithValue("@pw", PasswordHelper.HashPassword(temp))
                    cmd.Parameters.AddWithValue("@id", q.UserId)
                    cmd.ExecuteNonQuery()
                End Using

                Using cmd As New MySqlCommand(
                    "UPDATE password_reset_requests SET status='Handled', " &
                    "handled_by=@by, handled_at=NOW() WHERE request_id=@rid", conn)
                    cmd.Parameters.AddWithValue("@by",
                        If(Session.CurrentUserId = 0, CObj(DBNull.Value), CObj(Session.CurrentUserId)))
                    cmd.Parameters.AddWithValue("@rid", q.RequestId)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not issue a new password: " & ex.Message, "Database Error",
                          MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try

        ActivityLog.Record("USER_PASSWORD_RESET", "user", q.UserId,
                           "Reset password for " & q.UsernameTyped)

        AppModal.Show(
            "Temporary password for " & q.UsernameTyped & ":" &
            Environment.NewLine & Environment.NewLine &
            "        " & temp & Environment.NewLine & Environment.NewLine &
            "Give this to them directly. They will be asked to choose a new " &
            "password the next time they log in.",
            "Password Issued", MessageBoxButtons.OK, MessageBoxIcon.Information)
        Refresh()
    End Sub

    ' Closes a request without touching the account - for duplicates, or a
    ' username that never existed.
    Private Shared Sub DismissResetRequest(q As ResetRequest)
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "UPDATE password_reset_requests SET status='Dismissed', " &
                    "handled_by=@by, handled_at=NOW() WHERE request_id=@rid", conn)
                    cmd.Parameters.AddWithValue("@by",
                        If(Session.CurrentUserId = 0, CObj(DBNull.Value), CObj(Session.CurrentUserId)))
                    cmd.Parameters.AddWithValue("@rid", q.RequestId)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not dismiss the request: " & ex.Message, "Database Error",
                          MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try

        ActivityLog.Record("USER_RESET_DISMISS", "user",
                           If(q.UserId > 0, CType(q.UserId, Integer?), Nothing),
                           "Dismissed reset request for " & q.UsernameTyped)
        Refresh()
    End Sub

    Private Shared Function CountActiveAdmins() As Integer
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT COUNT(*) FROM users WHERE role_id=@rid AND is_active=1", conn)
                    cmd.Parameters.AddWithValue("@rid", ADMIN_ROLE_ID)
                    Return Convert.ToInt32(cmd.ExecuteScalar())
                End Using
            End Using
        Catch
            Return 2
        End Try
    End Function

    ' =========================================================================
    '  ACTION 4 — RESET PASSWORD
    ' =========================================================================
    Private Shared Sub ResetPassword(r As UserRecord)
        ' Re-authenticate first. Setting another person's password is the single
        ' most abusable action on this screen, and an admin session left open at
        ' the counter would otherwise hand it to whoever walks past - including
        ' the ability to take over the admin account itself.
        If Not ConfirmActingAdmin("Reset the password for " & Chr(34) & r.Username & Chr(34) & ".") Then
            Return
        End If

        Dim pw As String = ShowPasswordPrompt(
            "Set a new password",
            "Choose the new password for " & Chr(34) & r.Username & Chr(34) &
            ". Tell it to them directly - it is not sent anywhere.",
            fieldLabel:="New password for " & r.Username,
            okText:="Set password")
        If pw Is Nothing Then Return

        ' Same rule the sign-up screen enforces, so a reset cannot quietly
        ' install a weaker password than the user could have chosen themselves.
        Dim pwErr As String = AccountRequests.ValidatePassword(pw)
        If pwErr <> "" Then
            AppModal.Show(pwErr, "Reset Password",
                          MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "UPDATE users SET password_hash=@pw WHERE user_id=@id", conn)
                    cmd.Parameters.AddWithValue("@pw", PasswordHelper.HashPassword(pw))
                    cmd.Parameters.AddWithValue("@id", r.UserId)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not reset the password: " & ex.Message, "Database Error",
                          MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try

        ActivityLog.Record("USER_RESET_PW", "user", r.UserId, r.Username)
        AppModal.Show("Password for """ & r.Username & """ has been reset.", "Reset Password",
                      MessageBoxButtons.OK, MessageBoxIcon.Information)
    End Sub

    ' =========================================================================
    '  CREATE / EDIT MODAL
    '  Styled after AppModal + PaymentPromptModal: borderless rounded card,
    '  themed field boxes, secondary-left / primary-right button row.
    '  The sales-quota field only exists for the Cashier role — when another
    '  role is selected the field is hidden and the card shrinks to match.
    ' =========================================================================
    Private Shared Function ShowUserForm(caption As String,
                                         isCreate As Boolean,
                                         existing As UserRecord) As UserFormResult

        ' Two columns inside a card wide enough to hold them, matching the
        ' Suppliers form. The old 420px single column stacked five identical
        ' boxes with no grouping, so nothing on it said which fields belonged
        ' together or which mattered.
        Const CARD_W As Integer = 720
        Const PAD As Integer = 32
        Const FIELD_H As Integer = 44
        Const GUTTER As Integer = 24
        Dim colW As Integer = (CARD_W - PAD * 2 - GUTTER) \ 2
        Dim colRX As Integer = PAD + colW + GUTTER

        Dim roles As List(Of RoleOption) = LoadRoles()
        If roles.Count = 0 Then
            AppModal.Show("No roles are defined, so a user cannot be created.", "User",
                          MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return Nothing
        End If

        ' An existing Admin keeps their own role as a locked option. Without this
        ' the dropdown would fall back to the first entry and quietly demote them
        ' on save. Admin stays unreachable for everyone else — it is never in the
        ' list for a non-admin user.
        Dim editingAdmin As Boolean =
            (existing IsNot Nothing AndAlso existing.RoleId = ADMIN_ROLE_ID)
        If editingAdmin Then
            roles.Insert(0, New RoleOption With {
                .RoleId = existing.RoleId,
                .RoleName = existing.RoleName})
        End If

        Dim f As New Form With {
            .FormBorderStyle = FormBorderStyle.None,
            .StartPosition = FormStartPosition.CenterParent,
            .BackColor = CardBg,
            .ShowInTaskbar = False,
            .Width = CARD_W,
            .KeyPreview = True
        }

        ' ── HEADER BAND ──────────────────────────────────────────────────────
        ' A tinted band with an avatar, title and subtitle, so the dialog opens
        ' by saying who is being edited rather than with a bare word.
        Const HEADER_H As Integer = 84
        ' Inset 1px each side so the card's own border, drawn by f.Paint,
        ' stays visible down the full height instead of being painted over
        ' wherever a band sits.
        Dim header As New BufferedPanel With {
            .Location = New Point(1, 1),
            .Size = New Size(CARD_W - 2, HEADER_H - 1),
            .BackColor = Color.Transparent
        }
        AddHandler header.Paint,
            Sub(s, e)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                ' The form is clipped to a rounded region, so a plain rectangle
                ' here paints square corners right up to the clip and the band
                ' reads as a slab sitting on top of the card. Rounding the top
                ' two corners to the same radius makes it part of the card.
                Using path As GraphicsPath = TopRoundedRect(
                        New Rectangle(0, 0, header.Width, header.Height), 10)
                    Using br As New SolidBrush(Color.FromArgb(240, 249, 246))
                        g.FillPath(br, path)
                    End Using
                End Using
                Using pen As New Pen(DividerClr, 1)
                    g.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1)
                End Using
            End Sub

        header.Controls.Add(New Label With {
            .Text = caption,
            .Font = New Font("Segoe UI", 16, FontStyle.Bold),
            .ForeColor = MintDark,
            .AutoSize = True,
            .BackColor = Color.Transparent,
            .Location = New Point(PAD, 24)
        })
        header.Controls.Add(New Label With {
            .Text = If(isCreate,
                       "Create a staff account. Fields marked * are required.",
                       "Update this account's details. The username cannot be changed."),
            .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = SubText,
            .AutoSize = True,
            .BackColor = Color.Transparent,
            .Location = New Point(PAD, 52)
        })
        f.Controls.Add(header)

        Dim y As Integer = HEADER_H + 24

        ' ── SECTION: IDENTITY ────────────────────────────────────────────────
        y = AddFormSection(f, "Identity", "Who this person is.", y, PAD, CARD_W)

        Dim txtName As New TextBox With {
            .Font = New Font("Segoe UI", 11.0F),
            .BorderStyle = BorderStyle.None,
            .BackColor = FieldBg,
            .ForeColor = Color.FromArgb(35, 50, 46),
            .Text = If(existing IsNot Nothing, existing.FullName, "")
        }
        AddField(f, "Full name *", txtName, PAD, y, colW, FIELD_H)

        Dim txtUser As New TextBox With {
            .Font = New Font("Segoe UI", 11.0F),
            .BorderStyle = BorderStyle.None,
            .BackColor = FieldBg,
            .ForeColor = If(isCreate, Color.FromArgb(35, 50, 46), FaintText),
            .Text = If(existing IsNot Nothing, existing.Username, ""),
            .ReadOnly = Not isCreate
        }
        AddField(f, If(isCreate, "Username *", "Username (cannot be changed)"),
                 txtUser, colRX, y, colW, FIELD_H)
        y += 20 + FIELD_H + 22

        ' Email: where the "account created" notice and password-change codes
        ' go. Required for new accounts; optional on edit only so an older
        ' account without one can still be edited - the list flags those.
        Dim hasEmail As Boolean = AccountRequests.HasEmailColumn()
        Dim txtEmail As New TextBox With {
            .Font = New Font("Segoe UI", 11.0F),
            .BorderStyle = BorderStyle.None,
            .BackColor = FieldBg,
            .ForeColor = Color.FromArgb(35, 50, 46),
            .Text = If(existing IsNot Nothing, existing.Email, "")
        }
        If hasEmail Then
            AddField(f, If(isCreate, "Email *", "Email"), txtEmail, PAD, y, CARD_W - PAD * 2, FIELD_H)
            f.Controls.Add(New Label With {
                .Text = "Account notices and password-change codes are sent here.",
                .Font = New Font("Segoe UI", 8.5F),
                .ForeColor = FaintText,
                .AutoSize = True,
                .BackColor = Color.Transparent,
                .Location = New Point(PAD + 2, y + 20 + FIELD_H + 3)
            })
            y += 20 + FIELD_H + 22
        End If

        ' ── SECTION: ACCESS ──────────────────────────────────────────────────
        y = AddFormSection(f, "Access", "What this account may do.", y, PAD, CARD_W)

        Dim cboRole As New ComboBox With {
            .Font = New Font("Segoe UI", 11.0F),
            .DropDownStyle = ComboBoxStyle.DropDownList,
            .FlatStyle = FlatStyle.Flat,
            .BackColor = FieldBg,
            .ForeColor = Color.FromArgb(35, 50, 46)
        }
        For Each ro In roles
            cboRole.Items.Add(ro)
        Next
        cboRole.Enabled = Not editingAdmin
        AddField(f, If(editingAdmin, "Role (the Admin role cannot be changed)", "Role *"),
                 cboRole, PAD, y, colW, FIELD_H)

        ' Quota shares the Access row rather than trailing the form: it is a
        ' property of the Cashier role, so it belongs beside the role that
        ' turns it on.
        Dim txtQuota As New TextBox With {
            .Font = New Font("Segoe UI", 11.0F),
            .BorderStyle = BorderStyle.None,
            .BackColor = FieldBg,
            .ForeColor = Color.FromArgb(35, 50, 46),
            .Text = If(existing IsNot Nothing, existing.Quota.ToString("0.##"), "0")
        }
        Dim quotaUI = AddField(f, "Sales quota", txtQuota, colRX, y, colW, FIELD_H)

        ' A hint under the quota explains why it comes and goes.
        Dim lblQuotaHint As New Label With {
            .Text = "Shift sales target. 0 = no target.",
            .Font = New Font("Segoe UI", 8.5F),
            .ForeColor = FaintText,
            .AutoSize = True,
            .BackColor = Color.Transparent,
            .Location = New Point(colRX + 2, y + 20 + FIELD_H + 3)
        }
        f.Controls.Add(lblQuotaHint)
        y += 20 + FIELD_H + 22

        ' ── SECTION: PASSWORD (create only) ──────────────────────────────────
        Dim txtPass As TextBox = Nothing
        If isCreate Then
            y = AddFormSection(f, "Password", "The staff member can change this later.",
                               y, PAD, CARD_W)
            txtPass = New TextBox With {
                .Font = New Font("Segoe UI", 11.0F),
                .BorderStyle = BorderStyle.None,
                .BackColor = FieldBg,
                .ForeColor = Color.FromArgb(35, 50, 46),
                .UseSystemPasswordChar = True
            }
            AddField(f, "Initial password *", txtPass, PAD, y, colW, FIELD_H)
            ' Under its own field, matching the quota hint. At the old y + 26 it
            ' sat level with the middle of the box and floated in the empty
            ' right-hand column, reading as an orphaned line of text.
            f.Controls.Add(New Label With {
                .Text = "At least 8 characters, with a letter and a number.",
                .Font = New Font("Segoe UI", 8.5F),
                .ForeColor = FaintText,
                .AutoSize = True,
                .BackColor = Color.Transparent,
                .Location = New Point(PAD + 2, y + 20 + FIELD_H + 3)
            })
            y += 20 + FIELD_H + 22
        End If

        ' ── ERROR LINE ───────────────────────────────────────────────────────
        ' Validation reports here instead of stacking a second modal on top of
        ' this one, which is what the old form did for every empty field.
        Dim lblFormErr As New Label With {
            .Text = "",
            .Font = New Font("Segoe UI Semibold", 9),
            .ForeColor = AccentRed,
            .AutoSize = False,
            .Size = New Size(CARD_W - PAD * 2 - 240, 20),
            .Location = New Point(PAD, y),
            .BackColor = Color.Transparent,
            .TextAlign = ContentAlignment.MiddleLeft
        }
        f.Controls.Add(lblFormErr)

        ' ── FOOTER BAND ──────────────────────────────────────────────────────
        Dim footerTop As Integer = y + 26
        Const FOOTER_H As Integer = 76
        ' +1 for the band's 1px top inset, so the card's bottom border is not
        ' left orphaned below the footer.
        Dim baseCardH As Integer = footerTop + FOOTER_H

        Dim footer As New BufferedPanel With {
            .Location = New Point(1, footerTop),
            .Size = New Size(CARD_W - 2, FOOTER_H - 1),
            .BackColor = Color.Transparent
        }
        AddHandler footer.Paint,
            Sub(s, e)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Using path As GraphicsPath = BottomRoundedRect(
                        New Rectangle(0, 0, footer.Width, footer.Height), 10)
                    Using br As New SolidBrush(Color.FromArgb(249, 252, 251))
                        g.FillPath(br, path)
                    End Using
                End Using
                Using pen As New Pen(DividerClr, 1)
                    g.DrawLine(pen, 0, 0, footer.Width, 0)
                End Using
            End Sub
        f.Controls.Add(footer)

        Dim btnH As Integer = 42, btnW As Integer = 120, gap As Integer = 10
        Dim btnTop As Integer = (FOOTER_H - btnH) \ 2

        Dim btnCancel As New Button With {
            .Text = "Cancel",
            .Size = New Size(btnW, btnH),
            .FlatStyle = FlatStyle.Flat,
            .Font = New Font("Segoe UI Semibold", 9.5F),
            .Cursor = Cursors.Hand,
            .DialogResult = DialogResult.Cancel,
            .BackColor = Color.FromArgb(243, 246, 244),
            .ForeColor = Color.FromArgb(70, 86, 80),
            .Location = New Point(CARD_W - PAD - btnW * 2 - gap, btnTop)
        }
        btnCancel.FlatAppearance.BorderSize = 0

        Dim btnOk As New Button With {
            .Text = If(isCreate, "Create user", "Save changes"),
            .Size = New Size(btnW, btnH),
            .FlatStyle = FlatStyle.Flat,
            .Font = New Font("Segoe UI Semibold", 9.5F),
            .Cursor = Cursors.Hand,
            .BackColor = MintPrimary,
            .ForeColor = Color.White,
            .Location = New Point(CARD_W - PAD - btnW, btnTop)
        }
        btnOk.FlatAppearance.BorderSize = 0

        Dim paintPill As Action(Of Object, PaintEventArgs, Boolean) =
            Sub(s As Object, e As PaintEventArgs, outlined As Boolean)
                Dim b = CType(s, Button)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Using bg As New SolidBrush(Color.FromArgb(249, 252, 251))
                    g.FillRectangle(bg, b.ClientRectangle)
                End Using
                Dim rect = b.ClientRectangle : rect.Inflate(-1, -1)
                Using br As New SolidBrush(b.BackColor)
                    g.FillRoundedRectangle(br, rect, 8)
                End Using
                If outlined Then
                    Using p As New Pen(Color.FromArgb(200, 215, 210), 1.0F)
                        g.DrawRoundedRectangle(p, rect, 8)
                    End Using
                End If
                TextRenderer.DrawText(g, b.Text, b.Font, b.ClientRectangle, b.ForeColor,
                    TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub

        AddHandler btnOk.Paint, Sub(s, e) paintPill(s, e, False)
        AddHandler btnCancel.Paint, Sub(s, e) paintPill(s, e, True)
        AddHandler btnOk.MouseEnter, Sub()
                                         btnOk.BackColor = MintDark
                                         btnOk.Invalidate()
                                     End Sub
        AddHandler btnOk.MouseLeave, Sub()
                                         btnOk.BackColor = MintPrimary
                                         btnOk.Invalidate()
                                     End Sub
        AddHandler btnCancel.MouseEnter, Sub()
                                             btnCancel.BackColor = Color.FromArgb(231, 235, 233)
                                             btnCancel.Invalidate()
                                         End Sub
        AddHandler btnCancel.MouseLeave, Sub()
                                             btnCancel.BackColor = Color.FromArgb(243, 246, 244)
                                             btnCancel.Invalidate()
                                         End Sub

        footer.Controls.Add(btnCancel)
        footer.Controls.Add(btnOk)

        f.Height = baseCardH
        ApplyRoundedRegion(f, CARD_W, baseCardH)

        ' ── Quota show/hide ──────────────────────────────────────────────────
        ' Quota now sits in its own column, so hiding it leaves the rows below
        ' untouched — no card resize, no shifting buttons.
        Dim applyQuotaVisibility As Action(Of Boolean) =
            Sub(show As Boolean)
                quotaUI.Lbl.Visible = show
                quotaUI.Box.Visible = show
                lblQuotaHint.Visible = show
            End Sub

        AddHandler cboRole.SelectedIndexChanged,
            Sub()
                Dim sel = TryCast(cboRole.SelectedItem, RoleOption)
                applyQuotaVisibility(sel IsNot Nothing AndAlso sel.RoleId = CASHIER_ROLE_ID)
            End Sub

        ' Preselect the role (existing user's role, else the first entry)
        Dim preselect As Integer = 0
        If existing IsNot Nothing Then
            For i As Integer = 0 To roles.Count - 1
                If roles(i).RoleId = existing.RoleId Then
                    preselect = i
                    Exit For
                End If
            Next
        End If
        cboRole.SelectedIndex = preselect
        applyQuotaVisibility(roles(preselect).RoleId = CASHIER_ROLE_ID)

        ' ── Card chrome ──────────────────────────────────────────────────────
        AddHandler f.Paint,
            Sub(s, e)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim card As New Rectangle(0, 0, f.Width - 1, f.Height - 1)
                Using br As New SolidBrush(CardBg)
                    g.FillRectangle(br, card)
                End Using
                Using pen As New Pen(BorderClr, 1.2F)
                    g.DrawRoundedRectangle(pen, card, 10)
                End Using
            End Sub

        ' ── Validation + accept ──────────────────────────────────────────────
        Dim result As UserFormResult = Nothing

        AddHandler btnOk.Click,
            Sub()
                Dim fullName As String = txtName.Text.Trim()
                Dim username As String = txtUser.Text.Trim()
                Dim sel = TryCast(cboRole.SelectedItem, RoleOption)

                lblFormErr.Text = ""

                If fullName = "" Then
                    lblFormErr.Text = "Full name is required."
                    txtName.Focus()
                    Return
                End If
                If username = "" Then
                    lblFormErr.Text = "Username is required."
                    txtUser.Focus()
                    Return
                End If
                If sel Is Nothing Then
                    lblFormErr.Text = "Please choose a role."
                    cboRole.Focus()
                    Return
                End If

                Dim email As String = txtEmail.Text.Trim()
                If hasEmail Then
                    If email = "" AndAlso isCreate Then
                        lblFormErr.Text = "Email is required."
                        txtEmail.Focus()
                        Return
                    End If
                    If email <> "" AndAlso Not Mailer.IsValidAddress(email) Then
                        lblFormErr.Text = "That email address does not look right."
                        txtEmail.Focus()
                        Return
                    End If
                    If email <> "" AndAlso AccountRequests.EmailTaken(email,
                            If(existing IsNot Nothing, existing.UserId, 0)) Then
                        lblFormErr.Text = "That email is already used by another account."
                        txtEmail.Focus()
                        Return
                    End If
                End If

                Dim password As String = ""
                If isCreate Then
                    password = txtPass.Text
                    Dim pwErr As String = AccountRequests.ValidatePassword(password)
                    If pwErr <> "" Then
                        lblFormErr.Text = pwErr
                        txtPass.Focus()
                        Return
                    End If
                    Dim userErr As String = AccountRequests.ValidateUsername(username)
                    If userErr <> "" Then
                        lblFormErr.Text = userErr
                        txtUser.Focus()
                        Return
                    End If
                    If UsernameTaken(username) Then
                        lblFormErr.Text = "The username """ & username & """ is already taken."
                        txtUser.Focus()
                        Return
                    End If
                End If

                ' Quota only applies to Cashier — anything else stores 0.
                Dim quota As Decimal = 0D
                If sel.RoleId = CASHIER_ROLE_ID Then
                    If Not Decimal.TryParse(txtQuota.Text.Trim(), quota) OrElse quota < 0D Then
                        lblFormErr.Text = "Sales quota must be a number of 0 or more."
                        txtQuota.Focus()
                        Return
                    End If
                End If

                result = New UserFormResult With {
                    .FullName = fullName,
                    .Username = username,
                    .Email = email,
                    .RoleId = sel.RoleId,
                    .RoleName = sel.RoleName,
                    .Password = password,
                    .Quota = quota
                }
                f.DialogResult = DialogResult.OK
                f.Close()
            End Sub

        f.AcceptButton = btnOk
        f.CancelButton = btnCancel
        AddHandler f.KeyDown,
            Sub(s, e)
                If e.KeyCode = Keys.Escape Then
                    f.DialogResult = DialogResult.Cancel
                    f.Close()
                End If
            End Sub
        AddHandler f.Shown, Sub() txtName.Focus()

        Try
            Dim owner As Form = Form.ActiveForm
            If owner Is Nothing OrElse owner Is f Then
                f.StartPosition = FormStartPosition.CenterScreen
                f.ShowDialog()
            Else
                f.ShowDialog(owner)
            End If
            Return result
        Finally
            f.Dispose()
        End Try
    End Function

    ' ── FORM SECTION HEADER ──────────────────────────────────────────────────
    ' A titled band grouping the fields under it, matching the Suppliers form.
    ' Returns the y the section's first field starts at.
    Private Shared Function AddFormSection(parent As Control, title As String,
                                           caption As String, y As Integer,
                                           padX As Integer, cardW As Integer) As Integer
        parent.Controls.Add(New Label With {
            .Text = title.ToUpperInvariant(),
            .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold),
            .ForeColor = MintDark,
            .AutoSize = True,
            .Location = New Point(padX, y),
            .BackColor = Color.Transparent
        })
        parent.Controls.Add(New Label With {
            .Text = caption,
            .Font = New Font("Segoe UI", 9),
            .ForeColor = FaintText,
            .AutoSize = True,
            .BackColor = Color.Transparent,
            .Location = New Point(padX, y + 17)
        })
        Return y + 44
    End Function

    ' =========================================================================
    '  RE-AUTHENTICATION
    '
    '  Asks the signed-in admin to re-enter their OWN password before a
    '  password-changing action goes through. Returns True only on a verified
    '  match.
    '
    '  Fails closed: if the session has no user id, or the account cannot be
    '  read, the answer is No. An action this sensitive should never proceed
    '  because a lookup failed.
    ' =========================================================================
    Private Shared Function ConfirmActingAdmin(whatFor As String) As Boolean
        If Session.CurrentUserId = 0 Then
            AppModal.Show("You must be signed in to do that.", "Confirm Identity",
                          MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return False
        End If

        Dim typed As String = ShowPasswordPrompt(
            "Confirm it's you",
            "You are about to: " & whatFor & Environment.NewLine & Environment.NewLine &
            "For security, re-enter the password for your own account (" &
            Session.CurrentUsername & ").",
            fieldLabel:="Your current password",
            okText:="Confirm")
        If typed Is Nothing Then Return False

        If String.IsNullOrEmpty(typed) Then
            AppModal.Show("Your password is required to continue.", "Confirm Identity",
                          MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return False
        End If

        Dim storedHash As String = Nothing
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT password_hash FROM users WHERE user_id=@id LIMIT 1", conn)
                    cmd.Parameters.AddWithValue("@id", Session.CurrentUserId)
                    Dim res = cmd.ExecuteScalar()
                    If res IsNot Nothing AndAlso Not IsDBNull(res) Then
                        storedHash = res.ToString()
                    End If
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not verify your password: " & ex.Message, "Database Error",
                          MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return False
        End Try

        If String.IsNullOrEmpty(storedHash) OrElse
           Not PasswordHelper.VerifyPassword(typed, storedHash) Then
            ActivityLog.Record("ADMIN_REAUTH_FAILED", "user", Session.CurrentUserId,
                               "Failed confirmation for: " & whatFor)
            AppModal.Show("That password is not correct.", "Confirm Identity",
                          MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return False
        End If

        Return True
    End Function

    ' =========================================================================
    '  MASKED PASSWORD PROMPT  —  same card style as the user form.
    '  AppModal.Prompt would echo the password in cleartext, so reset uses
    '  this instead.
    ' =========================================================================
    Private Shared Function ShowPasswordPrompt(caption As String, message As String,
                                               Optional fieldLabel As String = "New password",
                                               Optional okText As String = "Reset") As String
        Const CARD_W As Integer = 400
        Const PAD As Integer = 24
        Const FIELD_H As Integer = 44

        Dim f As New Form With {
            .FormBorderStyle = FormBorderStyle.None,
            .StartPosition = FormStartPosition.CenterParent,
            .BackColor = CardBg,
            .ShowInTaskbar = False,
            .Width = CARD_W,
            .KeyPreview = True
        }

        Dim lblTitle As New Label With {
            .Text = caption,
            .Font = New Font("Segoe UI Semibold", 12.0F),
            .ForeColor = TitleClr,
            .AutoSize = False,
            .Location = New Point(PAD, PAD),
            .Size = New Size(CARD_W - PAD * 2, 22),
            .BackColor = Color.Transparent
        }

        Dim bodyFont As New Font("Segoe UI", 9.5F)
        Dim textW As Integer = CARD_W - PAD * 2
        Dim measured As Size = TextRenderer.MeasureText(
            message, bodyFont, New Size(textW, 0),
            TextFormatFlags.WordBreak Or TextFormatFlags.TextBoxControl)
        Dim lblBody As New Label With {
            .Text = message,
            .Font = bodyFont,
            .ForeColor = SubText,
            .AutoSize = False,
            .Location = New Point(PAD, lblTitle.Bottom + 8),
            .Size = New Size(textW, Math.Max(20, measured.Height + 4)),
            .BackColor = Color.Transparent
        }

        f.Controls.Add(lblTitle)
        f.Controls.Add(lblBody)

        Dim txtPw As New TextBox With {
            .Font = New Font("Segoe UI", 11.0F),
            .BorderStyle = BorderStyle.None,
            .BackColor = FieldBg,
            .ForeColor = Color.FromArgb(35, 50, 46),
            .UseSystemPasswordChar = True
        }
        Dim ui = AddField(f, fieldLabel, txtPw, PAD, lblBody.Bottom + 10, textW, FIELD_H)

        Dim dividerY As Integer = ui.Box.Bottom + 18
        Dim btnH As Integer = 38, btnW As Integer = 104, gap As Integer = 8
        Dim btnTop As Integer = dividerY + 16

        Dim btnCancel As New Button With {
            .Text = "Cancel",
            .Size = New Size(btnW, btnH),
            .FlatStyle = FlatStyle.Flat,
            .Font = New Font("Segoe UI Semibold", 9.5F),
            .Cursor = Cursors.Hand,
            .DialogResult = DialogResult.Cancel,
            .BackColor = Color.FromArgb(243, 246, 244),
            .ForeColor = Color.FromArgb(70, 86, 80),
            .Location = New Point(CARD_W - PAD - btnW * 2 - gap, btnTop)
        }
        btnCancel.FlatAppearance.BorderSize = 0

        Dim btnOk As New Button With {
            .Text = okText,
            .Size = New Size(btnW, btnH),
            .FlatStyle = FlatStyle.Flat,
            .Font = New Font("Segoe UI Semibold", 9.5F),
            .Cursor = Cursors.Hand,
            .DialogResult = DialogResult.OK,
            .BackColor = MintPrimary,
            .ForeColor = Color.White,
            .Location = New Point(CARD_W - PAD - btnW, btnTop)
        }
        btnOk.FlatAppearance.BorderSize = 0

        For Each b As Button In {btnOk, btnCancel}
            Dim bb As Button = b
            Dim isPrimary As Boolean = (b Is btnOk)
            AddHandler bb.Paint,
                Sub(s, e)
                    Dim g = e.Graphics
                    g.SmoothingMode = SmoothingMode.AntiAlias
                    Using bg As New SolidBrush(CardBg)
                        g.FillRectangle(bg, bb.ClientRectangle)
                    End Using
                    Dim rect = bb.ClientRectangle : rect.Inflate(-1, -1)
                    Using br As New SolidBrush(bb.BackColor)
                        g.FillRoundedRectangle(br, rect, 7)
                    End Using
                    If Not isPrimary Then
                        Using p As New Pen(Color.FromArgb(200, 215, 210), 1.0F)
                            g.DrawRoundedRectangle(p, rect, 7)
                        End Using
                    End If
                    TextRenderer.DrawText(g, bb.Text, bb.Font, bb.ClientRectangle, bb.ForeColor,
                        TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
                End Sub
        Next

        f.Controls.Add(btnCancel)
        f.Controls.Add(btnOk)

        Dim cardH As Integer = btnTop + btnH + PAD
        f.Height = cardH

        AddHandler f.Paint,
            Sub(s, e)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim card As New Rectangle(0, 0, f.Width - 1, f.Height - 1)
                Using br As New SolidBrush(CardBg)
                    g.FillRectangle(br, card)
                End Using
                Using pen As New Pen(DividerClr, 1.0F)
                    g.DrawLine(pen, PAD, dividerY, f.Width - PAD, dividerY)
                End Using
                Using pen As New Pen(BorderClr, 1.2F)
                    g.DrawRoundedRectangle(pen, card, 10)
                End Using
            End Sub

        ApplyRoundedRegion(f, CARD_W, cardH)

        f.AcceptButton = btnOk
        f.CancelButton = btnCancel
        AddHandler f.Shown, Sub() txtPw.Focus()

        Try
            Dim owner As Form = Form.ActiveForm
            Dim res As DialogResult
            If owner Is Nothing OrElse owner Is f Then
                f.StartPosition = FormStartPosition.CenterScreen
                res = f.ShowDialog()
            Else
                res = f.ShowDialog(owner)
            End If
            Return If(res = DialogResult.OK, txtPw.Text, Nothing)
        Finally
            f.Dispose()
        End Try
    End Function

    ' =========================================================================
    '  MODAL FIELD HELPERS
    ' =========================================================================
    Private Class FieldUI
        Public Lbl As Label
        Public Box As Panel
    End Class

    ' Builds "label + rounded field box + input" and adds it to the host.
    Private Shared Function AddField(host As Control, labelText As String, input As Control,
                                     x As Integer, top As Integer,
                                     w As Integer, h As Integer) As FieldUI
        Dim lbl As New Label With {
            .Text = labelText,
            .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = SubText,
            .AutoSize = True,
            .Location = New Point(x, top),
            .BackColor = Color.Transparent
        }

        Dim box As New Panel With {
            .Location = New Point(x, top + 20),
            .Size = New Size(w, h),
            .BackColor = Color.Transparent
        }

        Dim focused As Boolean = False
        AddHandler box.Paint,
            Sub(s, e)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect = box.ClientRectangle : rect.Inflate(-1, -1)
                Using br As New SolidBrush(FieldBg)
                    g.FillRoundedRectangle(br, rect, 9)
                End Using
                Using p As New Pen(If(focused, MintPrimary, FieldBorder), If(focused, 1.8F, 1.0F))
                    g.DrawRoundedRectangle(p, rect, 9)
                End Using
            End Sub

        input.Location = New Point(14, (h - input.PreferredSize.Height) \ 2)
        input.Width = w - 28
        AddHandler input.Enter, Sub()
                                    focused = True
                                    box.Invalidate()
                                End Sub
        AddHandler input.Leave, Sub()
                                    focused = False
                                    box.Invalidate()
                                End Sub
        box.Controls.Add(input)

        host.Controls.Add(lbl)
        host.Controls.Add(box)

        Return New FieldUI With {.Lbl = lbl, .Box = box}
    End Function

    Private Shared Sub ApplyRoundedRegion(f As Form, w As Integer, h As Integer)
        Using gp As New GraphicsPath()
            Dim d As Integer = 20
            gp.AddArc(0, 0, d, d, 180, 90)
            gp.AddArc(w - d, 0, d, d, 270, 90)
            gp.AddArc(w - d, h - d, d, d, 0, 90)
            gp.AddArc(0, h - d, d, d, 90, 90)
            gp.CloseFigure()
            f.Region = New Region(gp)
        End Using
    End Sub

    ' =========================================================================
    '  SHARED HELPERS  (same implementations as the Accountant panels)
    ' =========================================================================
    ' A rectangle with only its TOP two corners rounded - for a header band
    ' that has to sit flush inside a rounded card.
    Private Shared Function TopRoundedRect(rect As Rectangle, radius As Integer) As GraphicsPath
        ' radius is a RADIUS, so the arc box is twice it - the same convention
        ' GraphicsExtensions.DrawRoundedRectangle uses. Treating it as the
        ' diameter here gave the bands a corner half the size of the card's,
        ' which showed as a notch at each top and bottom edge.
        Dim d As Integer = radius * 2
        Dim path As New GraphicsPath()
        path.AddArc(rect.X, rect.Y, d, d, 180, 90)
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90)
        path.AddLine(rect.Right, rect.Bottom, rect.X, rect.Bottom)
        path.CloseFigure()
        Return path
    End Function

    ' The same for a footer band: BOTTOM two corners only.
    Private Shared Function BottomRoundedRect(rect As Rectangle, radius As Integer) As GraphicsPath
        Dim d As Integer = radius * 2
        Dim path As New GraphicsPath()
        path.AddLine(rect.X, rect.Y, rect.Right, rect.Y)
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90)
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90)
        path.CloseFigure()
        Return path
    End Function

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
