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
    Private Shared ReadOnly MintDark As Color = Color.FromArgb(10, 79, 66)
    Private Shared ReadOnly MintPrimary As Color = Color.FromArgb(0, 168, 142)
    Private Shared ReadOnly MintBg As Color = Color.FromArgb(244, 248, 246)
    Private Shared ReadOnly CardBg As Color = Color.White
    Private Shared ReadOnly SubText As Color = Color.FromArgb(90, 114, 107)
    Private Shared ReadOnly FaintText As Color = Color.FromArgb(150, 165, 160)
    Private Shared ReadOnly RowDivider As Color = Color.FromArgb(238, 242, 240)
    Private Shared ReadOnly RowHover As Color = Color.FromArgb(224, 242, 235)
    Private Shared ReadOnly AccentRed As Color = Color.FromArgb(210, 60, 60)
    Private Shared ReadOnly FieldBg As Color = Color.FromArgb(244, 248, 246)
    Private Shared ReadOnly FieldBorder As Color = Color.FromArgb(210, 225, 220)
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
        Public RoleId As Integer
        Public RoleName As String
        Public IsActive As Boolean
        Public CreatedAt As DateTime
        Public Quota As Decimal
    End Class

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
    Private Shared _scrollHost As Panel
    Private Shared _reflowAttached As Boolean = False

    Private Const SIDE_PAD As Integer = 24
    Private Const ROW_H As Integer = 64
    Private Const SEARCH_PLACEHOLDER As String = "Search by name or username…"

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Shared Sub LoadUsers(container As Panel)
        _container = container
        _searchQuery = ""
        LoadData()
        Render()
    End Sub

    ' Reload from the DB and repaint, keeping the current search text.
    Private Shared Sub Refresh()
        LoadData()
        PopulateRows()
    End Sub

    ' =========================================================================
    '  DATA
    ' =========================================================================
    Private Shared Sub LoadData()
        _allRows.Clear()

        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Dim sql As String =
                    "SELECT u.user_id, u.full_name, u.username, u.is_active, " &
                    "u.created_at, u.sales_quota, u.role_id, r.role_name " &
                    "FROM users u JOIN roles r ON u.role_id = r.role_id " &
                    "ORDER BY u.full_name"

                Using cmd As New MySqlCommand(sql, conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            _allRows.Add(New UserRecord With {
                                .UserId = Convert.ToInt32(rdr("user_id")),
                                .FullName = rdr("full_name").ToString(),
                                .Username = rdr("username").ToString(),
                                .RoleId = Convert.ToInt32(rdr("role_id")),
                                .RoleName = rdr("role_name").ToString(),
                                .IsActive = (Not IsDBNull(rdr("is_active"))) AndAlso Convert.ToBoolean(rdr("is_active")),
                                .CreatedAt = If(IsDBNull(rdr("created_at")), DateTime.MinValue, Convert.ToDateTime(rdr("created_at"))),
                                .Quota = If(IsDBNull(rdr("sales_quota")), 0D, Convert.ToDecimal(rdr("sales_quota")))
                            })
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load users: " & ex.Message, "Database Error",
                          MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try

        ApplySearch()
    End Sub

    Private Shared Sub ApplySearch()
        _visibleRows.Clear()
        If String.IsNullOrWhiteSpace(_searchQuery) Then
            _visibleRows.AddRange(_allRows)
        Else
            Dim q As String = _searchQuery.ToLower()
            For Each r In _allRows
                If r.FullName.ToLower().Contains(q) OrElse r.Username.ToLower().Contains(q) Then
                    _visibleRows.Add(r)
                End If
            Next
        End If
    End Sub

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

        Dim colHdr As Panel = BuildColHeader()
        colHdr.Dock = DockStyle.Top

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
        AddHandler btnRefresh.MouseEnter, Sub()
                                              btnRefresh.BackColor = Color.FromArgb(210, 240, 235)
                                              btnRefresh.Invalidate()
                                          End Sub
        AddHandler btnRefresh.MouseLeave, Sub()
                                              btnRefresh.BackColor = Color.FromArgb(220, 235, 232)
                                              btnRefresh.Invalidate()
                                          End Sub
        AddHandler btnRefresh.Click, Sub() Refresh()

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
            .ForeColor = SubText,
            .Text = SEARCH_PLACEHOLDER,
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
                btnRefresh.Left = btnNew.Left - btnRefresh.Width - 10
                btnRefresh.Top = (titleBar.Height - btnRefresh.Height) \ 2
                searchWrap.Left = btnRefresh.Left - searchWrap.Width - 10
                searchWrap.Top = (titleBar.Height - searchWrap.Height) \ 2
            End Sub

        titleBar.Controls.Add(lblTitle)
        titleBar.Controls.Add(searchWrap)
        titleBar.Controls.Add(btnRefresh)
        titleBar.Controls.Add(btnNew)

        region.Controls.Add(titleBar)
        Return region
    End Function

    ' =========================================================================
    '  COLUMN HEADER  —  44px, proportional label layout
    ' =========================================================================
    Private Shared Function BuildColHeader() As Panel
        Dim hdr As New Panel With {.Height = 44, .BackColor = Color.Transparent}

        Dim labels As Label() = {
            MakeColLabel("FULL NAME", New Point(24, 13)),
            MakeColLabel("USERNAME", New Point(0, 13)),
            MakeColLabel("ROLE", New Point(0, 13)),
            MakeColLabel("STATUS", New Point(0, 13)),
            MakeColLabel("CREATED", New Point(0, 13)),
            MakeColLabel("ACTIONS", New Point(0, 13))
        }
        hdr.Controls.AddRange(labels)

        AddHandler hdr.Resize,
            Sub()
                Dim w = hdr.Width
                labels(0).Left = 24
                labels(1).Left = CInt(w * 0.2)
                labels(2).Left = CInt(w * 0.34)
                labels(3).Left = CInt(w * 0.45)
                labels(4).Left = CInt(w * 0.56)
                labels(5).Left = CInt(w * 0.7)
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

        If _visibleRows.Count = 0 Then
            _scrollHost.Controls.Add(BuildEmptyRow(
                If(String.IsNullOrWhiteSpace(_searchQuery),
                   "No users found.",
                   $"No results for ""{_searchQuery}"".")))
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
                ' Left accent bar — mint for active, grey for disabled
                Using br As New SolidBrush(If(r.IsActive, MintPrimary, Color.FromArgb(200, 208, 205)))
                    e.Graphics.FillRectangle(br, New Rectangle(0, 12, 3, height - 24))
                End Using
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

        p.Controls.AddRange({lblName, lblUser, lblRole, badge, lblCreated,
                             btnEdit, btnToggle, btnReset})

        AddHandler p.Resize,
            Sub()
                Dim w = p.Width
                lblName.Left = 24 : lblName.Top = (height - lblName.Height) \ 2
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

        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Dim newId As Integer
                Using cmd As New MySqlCommand(
                    "INSERT INTO users (role_id, full_name, username, password_hash, is_active, sales_quota) " &
                    "VALUES (@rid, @fn, @un, @pw, 1, @q)", conn)
                    cmd.Parameters.AddWithValue("@rid", res.RoleId)
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
        Catch ex As Exception
            AppModal.Show("Could not create the user: " & ex.Message, "Database Error",
                          MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try

        AppModal.Show("User """ & res.Username & """ was created.", "User Created",
                      MessageBoxButtons.OK, MessageBoxIcon.Information)
        Refresh()
    End Sub

    ' =========================================================================
    '  ACTION 2 — EDIT  (name / role / quota only — never the password)
    ' =========================================================================
    Private Shared Sub EditUser(r As UserRecord)
        Dim res As UserFormResult = ShowUserForm("Edit User", False, r)
        If res Is Nothing Then Return

        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "UPDATE users SET full_name=@fn, role_id=@rid, sales_quota=@q " &
                    "WHERE user_id=@id", conn)
                    cmd.Parameters.AddWithValue("@fn", res.FullName)
                    cmd.Parameters.AddWithValue("@rid", res.RoleId)
                    cmd.Parameters.AddWithValue("@q", res.Quota)
                    cmd.Parameters.AddWithValue("@id", r.UserId)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
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

    ' =========================================================================
    '  ACTION 4 — RESET PASSWORD
    ' =========================================================================
    Private Shared Sub ResetPassword(r As UserRecord)
        Dim pw As String = ShowPasswordPrompt("Reset Password",
                                              "Enter a new password for """ & r.Username & """.")
        If pw Is Nothing Then Return

        If String.IsNullOrWhiteSpace(pw) Then
            AppModal.Show("The password cannot be empty.", "Reset Password",
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

        Const CARD_W As Integer = 420
        Const PAD As Integer = 24
        Const FIELD_H As Integer = 44
        Const BLOCK_H As Integer = 20 + FIELD_H + 14   ' label + box + gap

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

        Dim lblTitle As New Label With {
            .Text = caption,
            .Font = New Font("Segoe UI Semibold", 12.0F),
            .ForeColor = TitleClr,
            .AutoSize = False,
            .Location = New Point(PAD, PAD),
            .Size = New Size(CARD_W - PAD * 2, 22),
            .BackColor = Color.Transparent
        }
        f.Controls.Add(lblTitle)

        Dim fieldW As Integer = CARD_W - PAD * 2
        Dim y As Integer = lblTitle.Bottom + 14

        ' ── Full name ────────────────────────────────────────────────────────
        Dim txtName As New TextBox With {
            .Font = New Font("Segoe UI", 11.0F),
            .BorderStyle = BorderStyle.None,
            .BackColor = FieldBg,
            .ForeColor = Color.FromArgb(35, 50, 46),
            .Text = If(existing IsNot Nothing, existing.FullName, "")
        }
        AddField(f, "Full name", txtName, PAD, y, fieldW, FIELD_H)
        y += BLOCK_H

        ' ── Username (immutable once created) ────────────────────────────────
        Dim txtUser As New TextBox With {
            .Font = New Font("Segoe UI", 11.0F),
            .BorderStyle = BorderStyle.None,
            .BackColor = FieldBg,
            .ForeColor = If(isCreate, Color.FromArgb(35, 50, 46), FaintText),
            .Text = If(existing IsNot Nothing, existing.Username, ""),
            .ReadOnly = Not isCreate
        }
        AddField(f, If(isCreate, "Username", "Username (cannot be changed)"),
                 txtUser, PAD, y, fieldW, FIELD_H)
        y += BLOCK_H

        ' ── Role ─────────────────────────────────────────────────────────────
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
        AddField(f, If(editingAdmin, "Role (the Admin role cannot be changed)", "Role"),
                 cboRole, PAD, y, fieldW, FIELD_H)
        y += BLOCK_H

        ' ── Password (create only) ───────────────────────────────────────────
        Dim txtPass As TextBox = Nothing
        If isCreate Then
            txtPass = New TextBox With {
                .Font = New Font("Segoe UI", 11.0F),
                .BorderStyle = BorderStyle.None,
                .BackColor = FieldBg,
                .ForeColor = Color.FromArgb(35, 50, 46),
                .UseSystemPasswordChar = True
            }
            AddField(f, "Initial password", txtPass, PAD, y, fieldW, FIELD_H)
            y += BLOCK_H
        End If

        ' ── Sales quota — Cashier only, and always the LAST field so hiding it
        '    simply shrinks the card from the bottom. ──────────────────────────
        Dim txtQuota As New TextBox With {
            .Font = New Font("Segoe UI", 11.0F),
            .BorderStyle = BorderStyle.None,
            .BackColor = FieldBg,
            .ForeColor = Color.FromArgb(35, 50, 46),
            .Text = If(existing IsNot Nothing, existing.Quota.ToString("0.##"), "0")
        }
        Dim quotaUI = AddField(f, "Sales quota", txtQuota, PAD, y, fieldW, FIELD_H)
        y += BLOCK_H

        ' ── Button row / card metrics (computed WITH the quota field shown) ───
        Dim baseDividerY As Integer = y + 4
        Dim btnH As Integer = 38, btnW As Integer = 104, gap As Integer = 8
        Dim baseBtnTop As Integer = baseDividerY + 16
        Dim baseCardH As Integer = baseBtnTop + btnH + PAD

        Dim dividerY As Integer = baseDividerY

        Dim btnCancel As New Button With {
            .Text = "Cancel",
            .Size = New Size(btnW, btnH),
            .FlatStyle = FlatStyle.Flat,
            .Font = New Font("Segoe UI Semibold", 9.5F),
            .Cursor = Cursors.Hand,
            .DialogResult = DialogResult.Cancel,
            .BackColor = Color.FromArgb(243, 246, 244),
            .ForeColor = Color.FromArgb(70, 86, 80),
            .Location = New Point(CARD_W - PAD - btnW * 2 - gap, baseBtnTop)
        }
        btnCancel.FlatAppearance.BorderSize = 0

        Dim btnOk As New Button With {
            .Text = If(isCreate, "Create", "Save"),
            .Size = New Size(btnW, btnH),
            .FlatStyle = FlatStyle.Flat,
            .Font = New Font("Segoe UI Semibold", 9.5F),
            .Cursor = Cursors.Hand,
            .BackColor = MintPrimary,
            .ForeColor = Color.White,
            .Location = New Point(CARD_W - PAD - btnW, baseBtnTop)
        }
        btnOk.FlatAppearance.BorderSize = 0

        AddHandler btnOk.Paint,
            Sub(s, e)
                Dim b = CType(s, Button)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Using bg As New SolidBrush(CardBg)
                    g.FillRectangle(bg, b.ClientRectangle)
                End Using
                Dim rect = b.ClientRectangle : rect.Inflate(-1, -1)
                Using br As New SolidBrush(b.BackColor)
                    g.FillRoundedRectangle(br, rect, 7)
                End Using
                TextRenderer.DrawText(g, b.Text, b.Font, b.ClientRectangle, b.ForeColor,
                    TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub
        AddHandler btnCancel.Paint,
            Sub(s, e)
                Dim b = CType(s, Button)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Using bg As New SolidBrush(CardBg)
                    g.FillRectangle(bg, b.ClientRectangle)
                End Using
                Dim rect = b.ClientRectangle : rect.Inflate(-1, -1)
                Using br As New SolidBrush(b.BackColor)
                    g.FillRoundedRectangle(br, rect, 7)
                End Using
                Using p As New Pen(Color.FromArgb(200, 215, 210), 1.0F)
                    g.DrawRoundedRectangle(p, rect, 7)
                End Using
                TextRenderer.DrawText(g, b.Text, b.Font, b.ClientRectangle, b.ForeColor,
                    TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub
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

        f.Controls.Add(btnCancel)
        f.Controls.Add(btnOk)

        ' ── Quota show/hide — resizes the card so no empty gap is left ────────
        Dim applyQuotaVisibility As Action(Of Boolean) =
            Sub(show As Boolean)
                quotaUI.Lbl.Visible = show
                quotaUI.Box.Visible = show
                Dim shift As Integer = If(show, 0, BLOCK_H)
                dividerY = baseDividerY - shift
                btnCancel.Top = baseBtnTop - shift
                btnOk.Top = baseBtnTop - shift
                f.Height = baseCardH - shift
                ApplyRoundedRegion(f, CARD_W, f.Height)
                f.Invalidate()
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
                Using pen As New Pen(DividerClr, 1.0F)
                    g.DrawLine(pen, PAD, dividerY, f.Width - PAD, dividerY)
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

                If fullName = "" Then
                    AppModal.Show("Full name is required.", "Missing Information",
                                  MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    Return
                End If
                If username = "" Then
                    AppModal.Show("Username is required.", "Missing Information",
                                  MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    Return
                End If
                If sel Is Nothing Then
                    AppModal.Show("Please choose a role.", "Missing Information",
                                  MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    Return
                End If

                Dim password As String = ""
                If isCreate Then
                    password = txtPass.Text
                    If String.IsNullOrWhiteSpace(password) Then
                        AppModal.Show("An initial password is required.", "Missing Information",
                                      MessageBoxButtons.OK, MessageBoxIcon.Warning)
                        Return
                    End If
                    If UsernameTaken(username) Then
                        AppModal.Show("The username """ & username & """ is already taken.",
                                      "Username Unavailable",
                                      MessageBoxButtons.OK, MessageBoxIcon.Warning)
                        Return
                    End If
                End If

                ' Quota only applies to Cashier — anything else stores 0.
                Dim quota As Decimal = 0D
                If sel.RoleId = CASHIER_ROLE_ID Then
                    If Not Decimal.TryParse(txtQuota.Text.Trim(), quota) OrElse quota < 0D Then
                        AppModal.Show("Sales quota must be a number of 0 or more.", "Invalid Quota",
                                      MessageBoxButtons.OK, MessageBoxIcon.Warning)
                        Return
                    End If
                End If

                result = New UserFormResult With {
                    .FullName = fullName,
                    .Username = username,
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

    ' =========================================================================
    '  MASKED PASSWORD PROMPT  —  same card style as the user form.
    '  AppModal.Prompt would echo the password in cleartext, so reset uses
    '  this instead.
    ' =========================================================================
    Private Shared Function ShowPasswordPrompt(caption As String, message As String) As String
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
        Dim ui = AddField(f, "New password", txtPw, PAD, lblBody.Bottom + 10, textW, FIELD_H)

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
            .Text = "Reset",
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
