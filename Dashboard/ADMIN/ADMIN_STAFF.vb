Imports System.Drawing
Imports System.Drawing.Drawing2D

Public Class ADMIN_STAFF

    Private MintPrimary As Color = Theme.Primary
    Private MintDarkTop As Color = Color.FromArgb(0, 120, 110)
    Private MintMid As Color = Color.FromArgb(0, 170, 150)
    Private MintLight As Color = Color.FromArgb(220, 255, 245)
    Private MintGlow As Color = Color.FromArgb(0, 255, 200)
    Private NavBg As Color = Color.FromArgb(250, 252, 251)
    Private NavHover As Color = Color.FromArgb(238, 248, 244)

    Private MainContainer As Panel
    Private _btnSettings As Button          ' pinned above Logout

    ' ── Hamburger toggle state ──────────────────────────────────────────────────
    ' 310 not 260: the longest label ("Procurement Overview") needs 178px of text
    ' room. Anything narrower makes the button wrap to two lines, which throws its
    ' baseline out of line with the single-line rows.
    Private Const NAV_OPEN_WIDTH As Integer = 310
    Private Const NAV_CLOSED_WIDTH As Integer = 56
    Private navIsOpen As Boolean = True
    Private btnHamburger As Button          ' always-visible toggle button

    ' Captions are cleared from the buttons when the rail collapses, so the real
    ' label has to live somewhere else. Icons are cached in both layouts:
    ' "padded" = 40x24 with the glyph left-aligned (caption sits beside it),
    ' "bare"   = 24x24 for the centred icon-only rail.
    Private navCaptions As New Dictionary(Of Button, String)
    Private navIconPadded As New Dictionary(Of Button, Image)
    Private navIconBare As New Dictionary(Of Button, Image)
    Private NavTips As New ToolTip()

    ' ── Grouped nav list ────────────────────────────────────────────────────────
    ' Admin has 12 nav buttons + 4 section headers — far more than the other
    ' dashboards — so the buttons live in a scrollable panel between the logo and
    ' the pinned Logout instead of sitting directly on Nav. Keeps them from
    ' colliding with Logout on shorter screens.
    Private NavList As QuietScrollPanel
    Private Const NAV_LIST_TOP As Integer = 92
    ' Clears BOTH pinned buttons at the foot of the rail: My Account starts at
    ' Nav.Height - 168, so the scrolling list has to stop above that or its
    ' last item slides underneath.
    Private Const NAV_LIST_BOTTOM_GAP As Integer = 178

    ' ===========================================================================
    ' FORM LOAD
    ' ===========================================================================
    Private Sub ADMIN_STAFF_Load(sender As Object, e As EventArgs) Handles MyBase.Load

        Me.SetStyle(ControlStyles.AllPaintingInWmPaint Or
                    ControlStyles.OptimizedDoubleBuffer Or
                    ControlStyles.ResizeRedraw Or
                    ControlStyles.UserPaint, True)
        Me.DoubleBuffered = True

        ' ── NAV PANEL ───────────────────────────────────────────────────────────
        Nav.Dock = DockStyle.Left
        Nav.Width = NAV_OPEN_WIDTH
        Nav.BackColor = NavBg
        AddHandler Nav.Paint,
    Sub(s As Object, pe As PaintEventArgs)
        Using pen As New Pen(Color.FromArgb(220, 230, 226), 1)
            ' Right-hand edge divider.
            pe.Graphics.DrawLine(pen, Nav.Width - 1, 0, Nav.Width - 1, Nav.Height)
            ' Separator above the pinned My Account / Logout region, so the
            ' scrolling list reads as a distinct block and its last row never
            ' looks like it is colliding with the account button.
            Dim sepY As Integer = Nav.Height - 176
            pe.Graphics.DrawLine(pen, 16, sepY, Nav.Width - 16, sepY)
        End Using
    End Sub
        ' Without this the divider keeps the x it was painted at for the previous
        ' width, so after the nav is resized the line is stranded INSIDE the panel
        ' and runs straight through the icon column.
        AddHandler Nav.Resize, Sub() Nav.Invalidate()

        ' ── MAIN CONTAINER ──────────────────────────────────────────────────────
        MainContainer = New Panel With {
    .Dock = DockStyle.Fill,
    .BackColor = Color.White
}
        Me.Controls.Add(MainContainer)
        Me.Controls.Add(Nav)
        MainContainer.Padding = New Padding(0)

        ' ── HAMBURGER BUTTON — lives on the FORM, always visible ────────────────
        btnHamburger = New Button With {
    .Size = New Size(36, 36),
.Location = New Point(NAV_OPEN_WIDTH - 46, 18),
    .FlatStyle = FlatStyle.Flat,
    .BackColor = Color.White,
    .Cursor = Cursors.Hand,
  .Anchor = AnchorStyles.Top Or AnchorStyles.Right,
    .TabStop = False
}
        btnHamburger.FlatAppearance.BorderSize = 0
        btnHamburger.FlatAppearance.MouseOverBackColor = Color.FromArgb(230, 248, 242)
        btnHamburger.FlatAppearance.MouseDownBackColor = Color.FromArgb(210, 240, 232)

        ' Load icon — try full path first, then relative
        Dim burgerFull As String = "burger-menu.png"
        Dim burgerRel As String = "burger-menu.png"
        If IO.File.Exists(burgerFull) Then
            Using img = Image.FromFile(burgerFull)
                btnHamburger.Image = New Bitmap(img, New Size(22, 22))
            End Using
        ElseIf IO.File.Exists(burgerRel) Then
            Using img = Image.FromFile(burgerRel)
                btnHamburger.Image = New Bitmap(img, New Size(22, 22))
            End Using
        Else
            ' Unicode fallback — three-bar "≡" drawn as text
            btnHamburger.Text = "≡"
            btnHamburger.Font = New Font("Segoe UI", 16, FontStyle.Bold)
            btnHamburger.ForeColor = Color.FromArgb(10, 79, 66)
        End If

        ' Round the hamburger button corners
        AddHandler btnHamburger.Paint,
            Sub(s As Object, pe As PaintEventArgs)
                Dim b = CType(s, Button)
                pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim r As Integer = 10
                Dim rect = b.ClientRectangle
                rect.Inflate(-1, -1)
                Using path As New GraphicsPath()
                    path.AddArc(rect.X, rect.Y, r, r, 180, 90)
                    path.AddArc(rect.Right - r, rect.Y, r, r, 270, 90)
                    path.AddArc(rect.Right - r, rect.Bottom - r, r, r, 0, 90)
                    path.AddArc(rect.X, rect.Bottom - r, r, r, 90, 90)
                    path.CloseFigure()
                    Using br As New SolidBrush(b.BackColor)
                        pe.Graphics.FillPath(br, path)
                    End Using
                End Using
                If b.Image IsNot Nothing Then
                    Dim ix As Integer = (b.Width - b.Image.Width) \ 2
                    Dim iy As Integer = (b.Height - b.Image.Height) \ 2
                    pe.Graphics.DrawImage(b.Image, ix, iy)
                ElseIf b.Text <> "" Then
                    TextRenderer.DrawText(pe.Graphics, b.Text, b.Font,
                        b.ClientRectangle, b.ForeColor,
                        TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
                End If
            End Sub

        AddHandler btnHamburger.Click, AddressOf ToggleNav
        Nav.Controls.Add(btnHamburger)
        btnHamburger.BringToFront()

        ' ── NAV CONTENTS ────────────────────────────────────────────────────────
        Dim logoBox As New PictureBox With {
            .Size = New Size(38, 38),
     .Location = New Point(18, 14),
            .SizeMode = PictureBoxSizeMode.Zoom,
            .BackColor = MintLight
        }
        Dim logoPath2 As New Drawing2D.GraphicsPath()
        logoPath2.AddEllipse(0, 0, logoBox.Width, logoBox.Height)
        logoBox.Region = New Region(logoPath2)

        Dim logoFile As String = "paint.png"
        If IO.File.Exists(logoFile) Then logoBox.Image = Image.FromFile(logoFile)
        Nav.Controls.Add(logoBox)

        Dim lblLogo As New Label With {
            .Text = "Paint Admin",
            .Font = New Font("Segoe UI Semibold", 14, FontStyle.Bold),
            .ForeColor = Color.FromArgb(30, 45, 43),
            .AutoSize = True,
           .Location = New Point(64, 22)
        }
        Nav.Controls.Add(lblLogo)

        dashHome.Text = "Dashboard"
        userMgmt.Text = "User Management"
        roles.Text = "Roles"
        ' Store Settings moved into Settings > Store & Payments.
        storeSettings.Text = "Store Settings"
        storeSettings.Visible = False
        Nav.Controls.Remove(storeSettings)
        ' Field stays "products"; the screen manages the category list, since
        ' product CRUD already lives in Inventory ▸ Item List.
        products.Text = "Categories"
        suppliers.Text = "Suppliers"
        colorFormulas.Text = "Color Formulas"
        salesOverview.Text = "Sales Overview"
        inventoryOverview.Text = "Inventory Overview"
        procurementOverview.Text = "Procurement Overview"
        financialReports.Text = "Financial Reports"
        activityLogNav.Text = "Activity Log"
        logout.Text = "Logout"

        ' ── SCROLLABLE GROUPED NAV LIST ─────────────────────────────────────────
        ' Width is Nav.Width - 1, not Nav.Width: that leaves the rightmost pixel
        ' column free so the divider drawn by Nav's own Paint handler at
        ' x = Nav.Width - 1 is never painted over by this panel's background.
        NavList = New QuietScrollPanel With {
            .Location = New Point(0, NAV_LIST_TOP),
            .Size = New Size(Nav.Width - 1, Nav.Height - NAV_LIST_TOP - NAV_LIST_BOTTOM_GAP),
            .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Bottom,
            .BackColor = NavBg,
            .AutoScroll = True
        }
        Nav.Controls.Add(NavList)

        Dim sections = {
            ("OVERVIEW", New Button() {dashHome}),
            ("ADMINISTRATION", New Button() {userMgmt, roles}),
            ("MASTER DATA", New Button() {products, suppliers, colorFormulas}),
            ("OVERSIGHT", New Button() {salesOverview, inventoryOverview,
                                        procurementOverview, financialReports, activityLogNav})
        }

        ' NavList hides its scrollbar (QuietScrollPanel), so buttons use the
        ' same 28px side allowance as everywhere else - no reserve needed.
        Dim btnWidth As Integer = Nav.Width - 28
        Dim y As Integer = 8
        For Each sec In sections
            NavList.Controls.Add(MakeSectionHeader(sec.Item1, y))
            y += 28
            For Each btn As Button In sec.Item2
                StyleButton(btn)
                btn.Left = 14
                btn.Top = y
                btn.Width = btnWidth
                btn.Height = 48
                RoundButton(btn, 12)
                y += 54
            Next
            y += 10
        Next

        ' A trailing spacer so the scrollable extent runs 12px past the last
        ' button. AutoScroll then lets the final row clear the bottom edge
        ' instead of being cut mid-height when the panel is not an exact
        ' multiple of the row step.
        NavList.Controls.Add(New Panel With {
            .Left = 0, .Top = y, .Width = 1, .Height = 12, .BackColor = NavBg
        })

        ' ── SETTINGS ────────────────────────────────────────────────────────────
        ' Replaces the old My Account button. Settings holds My Account (with
        ' Change Password) and My Activity for everyone, plus role-specific tabs
        ' (see SettingsPanelBuilder). Pinned just above Logout on every dashboard.
        _btnSettings = New Button With {
            .Name = "settings",
            .Text = "Settings"
        }
        Nav.Controls.Add(_btnSettings)
        StyleButton(_btnSettings)
        ' StyleButton parents nav buttons into the scrolling NavList. This one
        ' is pinned to the rail beside Logout instead, so it goes back to Nav
        ' the same way Logout does - otherwise Nav.Height - 168 would position
        ' it against the wrong container and scroll away with the menu.
        _btnSettings.Parent = Nav
        _btnSettings.Left = 14
        _btnSettings.Width = Nav.Width - 28
        _btnSettings.Height = 48
        _btnSettings.Top = Nav.Height - 168
        _btnSettings.Anchor = AnchorStyles.Bottom Or AnchorStyles.Left
        RoundButton(_btnSettings, 12)
        AddHandler _btnSettings.Click,
            Sub()
                SetActive(_btnSettings)
                MainContainer.SuspendLayout()
                ScreenHost.Clear(MainContainer)
                SettingsPanelBuilder.LoadSettings(MainContainer)
                MainContainer.ResumeLayout()
                MainContainer.Refresh()
            End Sub

        StyleButton(logout)
        logout.Parent = Nav
        logout.Left = 14
        logout.Width = Nav.Width - 28
        logout.Height = 48
        logout.Top = Nav.Height - 110
        logout.Anchor = AnchorStyles.Bottom Or AnchorStyles.Left
        RoundButton(logout, 12)

        SetActive(dashHome)
        TintLogout()

        ' ── LOAD DEFAULT PANEL ──────────────────────────────────────────────────
        ' Health tiles on the dashboard jump to a nav section through this hook.
        AdminDashPanelBuilder.NavigateTo =
            Sub(key As String)
                Select Case key
                    Case "inventory" : inventoryOverview.PerformClick()
                    Case "procurement" : procurementOverview.PerformClick()
                    Case "requests"
                        ' Straight to the purchase requests waiting for approval.
                        SetActive(procurementOverview)
                        MainContainer.SuspendLayout()
                        ScreenHost.Clear(MainContainer)
                        AdminProcurementOverviewPanelBuilder.LoadProcurementOverview(MainContainer, 1, "Pending")
                        MainContainer.ResumeLayout()
                        MainContainer.Refresh()
                End Select
            End Sub
        ' Roles is read-only, so its user count is the one actionable thing on
        ' the screen: clicking it opens User Management filtered to that role.
        AdminRolesPanelBuilder.NavigateToUsers =
            Sub(roleId As Integer)
                SetActive(userMgmt)
                MainContainer.SuspendLayout()
                ScreenHost.Clear(MainContainer)
                AdminUserPanelBuilder.LoadUsersForRole(MainContainer, roleId)
                MainContainer.ResumeLayout()
                MainContainer.Refresh()
            End Sub

        AdminDashPanelBuilder.LoadDashboard(MainContainer)
        btnHamburger.Location = New Point(NAV_OPEN_WIDTH - 46, 18)
        btnHamburger.BringToFront()
    End Sub

    ' ===========================================================================
    ' SECTION HEADER — same treatment as the "MENU" label on the other dashboards
    ' ===========================================================================
    Private Function MakeSectionHeader(text As String, top As Integer) As Label
        Return New Label With {
            .Text = text,
            .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold),
            .ForeColor = Color.FromArgb(120, 138, 134),
            .AutoSize = True,
            .Location = New Point(22, top),
            .BackColor = NavBg
        }
    End Function

    ' ===========================================================================
    ' HAMBURGER TOGGLE
    ' ===========================================================================
    Private Sub ToggleNav(sender As Object, e As EventArgs)
        navIsOpen = Not navIsOpen
        ApplyNavState()
    End Sub

    ' Collapsed nav is an ICON RAIL: every nav button stays visible and clickable,
    ' only its caption is dropped. Text-only chrome (logo wordmark, "MENU") hides.
    Private Sub ApplyNavState()
        Nav.SuspendLayout()
        NavList.SuspendLayout()

        Dim width As Integer = If(navIsOpen, NAV_OPEN_WIDTH, NAV_CLOSED_WIDTH)
        Nav.Width = width

        ' NavList is a child panel, so it does not follow Nav.Width on its own.
        ' Leave the last pixel column free for the divider Nav paints there.
        NavList.Width = width - 1

        For Each ctrl As Control In Nav.Controls
            If ctrl Is btnHamburger OrElse ctrl Is NavList Then Continue For
            ' Any button in the rail is navigation and must survive the collapse.
            ctrl.Visible = navIsOpen OrElse TypeOf ctrl Is Button
        Next

        For Each ctrl As Control In Nav.Controls
            If ctrl Is btnHamburger Then Continue For
            Dim btn = TryCast(ctrl, Button)
            If btn IsNot Nothing Then ApplyNavButtonState(btn, width)
        Next

        ' Admin groups its 12 nav buttons inside NavList rather than parenting
        ' them straight onto Nav (the other dashboards have few enough to skip
        ' the scroll panel). Walking only Nav.Controls above therefore misses
        ' every one of them, which is why the collapsed rail came up blank:
        ' the buttons kept their full open width and were clipped out of view.
        For Each ctrl As Control In NavList.Controls
            Dim btn = TryCast(ctrl, Button)
            If btn IsNot Nothing Then
                ApplyNavButtonState(btn, width)
            Else
                ' Section captions ("OVERVIEW", "ADMINISTRATION", ...) are text
                ' only — there is no room for them on a 56px rail.
                ctrl.Visible = navIsOpen
            End If
        Next

        ' AutoScroll stays on so a genuinely short screen can still wheel
        ' through the list; QuietScrollPanel keeps the bar itself hidden.
        NavList.AutoScroll = True

        btnHamburger.BackColor = If(navIsOpen, Color.White, Color.Transparent)
        btnHamburger.Location = If(navIsOpen,
                                   New Point(NAV_OPEN_WIDTH - 46, 18),
                                   New Point((NAV_CLOSED_WIDTH - btnHamburger.Width) \ 2, 18))
        btnHamburger.BringToFront()

        NavList.ResumeLayout()
        Nav.ResumeLayout()
        Nav.Invalidate()
        NavList.Invalidate()
        MainContainer.PerformLayout()
        MainContainer.Refresh()
    End Sub

    ' Re-lay one nav button for the current rail width. Collapsed: no caption and
    ' the icon is centred; expanded: caption returns and the icon sits left.
    Private Sub ApplyNavButtonState(btn As Button, navWidth As Integer)
        If btn Is Nothing Then Return

        ' No scrollbar reserve any more - NavList hides its bar - so list
        ' buttons and the pinned ones (My Account, Logout) share one width.

        If navIsOpen Then
            btn.Text = NavCaption(btn)
            btn.Left = 14
            btn.Width = navWidth - 28
            btn.TextImageRelation = TextImageRelation.ImageBeforeText
            btn.ImageAlign = ContentAlignment.MiddleLeft
            btn.TextAlign = ContentAlignment.MiddleLeft
            btn.Padding = New Padding(20, 0, 14, 0)
            btn.Image = NavIcon(btn, padded:=True)
        Else
            btn.Text = ""
            btn.Left = 4
            btn.Width = navWidth - 8
            btn.ImageAlign = ContentAlignment.MiddleCenter
            btn.Padding = New Padding(0)
            btn.Image = NavIcon(btn, padded:=False)
        End If

        RoundButton(btn, 12)
        ' Tooltip so a collapsed icon still says what it does.
        NavTips.SetToolTip(btn, NavCaption(btn))
    End Sub

    Private Function NavCaption(btn As Button) As String
        Dim caption As String = ""
        If btn IsNot Nothing AndAlso navCaptions.TryGetValue(btn, caption) Then Return caption
        Return ""
    End Function

    Private Function NavIcon(btn As Button, padded As Boolean) As Image
        Dim img As Image = Nothing
        Dim store = If(padded, navIconPadded, navIconBare)
        If btn IsNot Nothing AndAlso store.TryGetValue(btn, img) Then Return img
        Return Nothing
    End Function

    ' ===========================================================================
    ' STYLE HELPERS
    ' ===========================================================================
    Private Sub StyleButton(btn As Button)
        ' Remember the caption before anything can clear it, so the collapsed rail
        ' can put it back (and show it as a tooltip meanwhile).
        navCaptions(btn) = btn.Text

        If btn IsNot logout Then btn.Parent = NavList

        Dim iconPath As String = ""
        Select Case btn.Name.ToLower()
            Case "dashhome" : iconPath = "home.png"
            Case "usermgmt" : iconPath = "group.png"
            Case "roles" : iconPath = "user.png"
            Case "storesettings" : iconPath = "settings.png"
            Case "products" : iconPath = "inventory.png"
            Case "suppliers" : iconPath = "express-delivery.png"
            Case "colorformulas" : iconPath = "paint.png"
            Case "salesoverview" : iconPath = "sales.png"
            Case "inventoryoverview" : iconPath = "clipboard.png"
            Case "procurementoverview" : iconPath = "quotation.png"
            Case "financialreports" : iconPath = "coins.png"
            Case "activitylognav" : iconPath = "recent.png"
            Case "account" : iconPath = "user.png"
            Case "settings" : iconPath = "settings.png"
            Case "logout" : iconPath = "logout.png"
        End Select

        ' Every button gets a 40x24 image — blank when the icon file is missing —
        ' so the text offset is identical on every row rather than jumping left
        ' by the image width whenever a load fails.
        Dim padded As New Bitmap(40, 24)
        If IO.File.Exists(iconPath) Then
            Dim src As Bitmap
            Using img = Image.FromFile(iconPath)
                src = New Bitmap(img, New Size(24, 24))
            End Using
            Using g As Graphics = Graphics.FromImage(padded)
                g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic
                g.DrawImage(src, 0, 0, 24, 24)
            End Using
        End If
        navIconPadded(btn) = padded
        navIconBare(btn) = padded.Clone(New Rectangle(0, 0, 24, 24), padded.PixelFormat)
        btn.Image = padded

        ' Hard guarantee against a second line, which would shift the baseline.
        btn.AutoEllipsis = True
        btn.Font = New Font("Segoe UI Semibold", 12, FontStyle.Regular)
        btn.ForeColor = Color.FromArgb(90, 105, 102)
        btn.TextImageRelation = TextImageRelation.ImageBeforeText
        btn.ImageAlign = ContentAlignment.MiddleLeft
        btn.TextAlign = ContentAlignment.MiddleLeft
        btn.Padding = New Padding(20, 0, 14, 0)
        btn.FlatStyle = FlatStyle.Flat
        btn.FlatAppearance.BorderSize = 0
        btn.FlatAppearance.MouseOverBackColor = NavBg
        btn.FlatAppearance.MouseDownBackColor = NavHover
        btn.BackColor = NavBg
        btn.Cursor = Cursors.Hand

        AddHandler btn.MouseEnter, Sub() If btn.Tag <> "active" Then btn.BackColor = NavHover
        AddHandler btn.MouseLeave, Sub() If btn.Tag <> "active" Then btn.BackColor = NavBg
    End Sub

    Private Sub SetActive(btn As Button)
        ' Settings is pinned on Nav itself, not in NavList, so it is reset
        ' alongside the list's buttons.
        Dim pinned As Control() = If(_btnSettings Is Nothing, New Control() {}, New Control() {_btnSettings})
        For Each ctrl As Control In NavList.Controls.Cast(Of Control)().Concat(pinned)
            If TypeOf ctrl Is Button AndAlso ctrl IsNot logout Then
                ctrl.BackColor = NavBg
                ctrl.ForeColor = Color.FromArgb(90, 105, 102)
                ctrl.Font = New Font("Segoe UI Semibold", 12, FontStyle.Regular)
                ctrl.Tag = ""
            End If
        Next
        btn.BackColor = MintLight
        btn.ForeColor = MintDarkTop
        btn.Font = New Font("Segoe UI Semibold", 12, FontStyle.Bold)
        btn.Tag = "active"
    End Sub

    Private Sub RoundButton(btn As Button, radius As Integer)
        Dim r As Integer = radius
        Dim w As Integer = btn.Width
        Dim h As Integer = btn.Height
        Dim path As New Drawing2D.GraphicsPath()
        path.AddArc(0, 0, r, r, 180, 90)
        path.AddArc(w - r, 0, r, r, 270, 90)
        path.AddArc(w - r, h - r, r, r, 0, 90)
        path.AddArc(0, h - r, r, r, 90, 90)
        path.CloseFigure()
        btn.Region = New Region(path)
    End Sub

    Private Sub TintLogout()
        ' Solid red so Logout reads as a real, deliberate action rather than a
        ' pale tinted strip. White label + white-tinted icon for contrast.
        Dim redBg As Color = Color.FromArgb(198, 40, 40)
        Dim redHoverBg As Color = Color.FromArgb(176, 30, 30)
        Dim redDownBg As Color = Color.FromArgb(154, 24, 24)
        logout.ForeColor = Color.White
        logout.Font = New Font("Segoe UI Semibold", 12, FontStyle.Bold)
        logout.BackColor = redBg
        logout.FlatStyle = FlatStyle.Flat
        logout.FlatAppearance.BorderSize = 0
        logout.FlatAppearance.MouseOverBackColor = redHoverBg
        logout.FlatAppearance.MouseDownBackColor = redDownBg
        logout.Cursor = Cursors.Hand
        If logout.Image IsNot Nothing Then
            logout.Image = TintLogoutIcon(logout.Image, Color.White)
            ' Keep the cached variants in step, or the collapsed rail would fall
            ' back to the untinted (dark-on-red) logout glyph.
            navIconPadded(logout) = logout.Image
            Dim bareLogout As Image = Nothing
            If navIconBare.TryGetValue(logout, bareLogout) AndAlso bareLogout IsNot Nothing Then
                navIconBare(logout) = TintLogoutIcon(bareLogout, Color.White)
            End If
        End If
        AddHandler logout.MouseEnter, Sub() logout.BackColor = redHoverBg
        AddHandler logout.MouseLeave, Sub() logout.BackColor = redBg
    End Sub

    ' Re-colours the logout glyph to a solid target colour, preserving alpha.
    Private Shared Function TintLogoutIcon(src As Image, target As Color) As Image
        Dim bmp As New Bitmap(src.Width, src.Height)
        Using g As Graphics = Graphics.FromImage(bmp)
            Dim cm As New Imaging.ColorMatrix(New Single()() {
                New Single() {0, 0, 0, 0, 0},
                New Single() {0, 0, 0, 0, 0},
                New Single() {0, 0, 0, 0, 0},
                New Single() {0, 0, 0, 1, 0},
                New Single() {target.R / 255.0F, target.G / 255.0F, target.B / 255.0F, 0, 1}})
            Using ia As New Imaging.ImageAttributes()
                ia.SetColorMatrix(cm)
                g.DrawImage(src, New Rectangle(0, 0, src.Width, src.Height),
                            0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia)
            End Using
        End Using
        Return bmp
    End Function

    Private Sub ApplyModernButton(btn As Button)
        btn.FlatStyle = FlatStyle.Flat
        btn.FlatAppearance.BorderSize = 0
        btn.Font = New Font("Segoe UI Semibold", 10, FontStyle.Bold)
        btn.ForeColor = Color.White
        btn.Cursor = Cursors.Hand

        AddHandler btn.Paint,
            Sub(s, ev)
                Dim rect As Rectangle = btn.ClientRectangle
                Dim radius As Integer = 12
                Using path As New Drawing2D.GraphicsPath()
                    path.AddArc(0, 0, radius, radius, 180, 90)
                    path.AddArc(rect.Width - radius, 0, radius, radius, 270, 90)
                    path.AddArc(rect.Width - radius, rect.Height - radius, radius, radius, 0, 90)
                    path.AddArc(0, rect.Height - radius, radius, radius, 90, 90)
                    path.CloseFigure()
                    ev.Graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
                    Using brush As New Drawing2D.LinearGradientBrush(rect, MintMid, MintDarkTop, 90.0F)
                        ev.Graphics.FillPath(brush, path)
                    End Using
                    Using pen As New Pen(MintGlow, 2)
                        ev.Graphics.DrawPath(pen, path)
                    End Using
                End Using
                TextRenderer.DrawText(ev.Graphics, btn.Text, btn.Font, rect, Color.White,
                    TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub
    End Sub

    ' ===========================================================================
    ' BUTTON CLICK HANDLERS
    ' ===========================================================================
    Private Sub dashHome_Click(sender As Object, e As EventArgs) Handles dashHome.Click
        SetActive(dashHome)
        MainContainer.SuspendLayout()
        ScreenHost.Clear(MainContainer)
        AdminDashPanelBuilder.LoadDashboard(MainContainer)
        MainContainer.ResumeLayout()
        MainContainer.Refresh()
    End Sub

    Private Sub userMgmt_Click(sender As Object, e As EventArgs) Handles userMgmt.Click
        SetActive(userMgmt)
        MainContainer.SuspendLayout()
        ScreenHost.Clear(MainContainer)
        AdminUserPanelBuilder.LoadUsers(MainContainer)
        MainContainer.ResumeLayout()
        MainContainer.Refresh()
    End Sub

    Private Sub roles_Click(sender As Object, e As EventArgs) Handles roles.Click
        SetActive(roles)
        MainContainer.SuspendLayout()
        ScreenHost.Clear(MainContainer)
        AdminRolesPanelBuilder.LoadRoles(MainContainer)
        MainContainer.ResumeLayout()
        MainContainer.Refresh()
    End Sub

    Private Sub storeSettings_Click(sender As Object, e As EventArgs) Handles storeSettings.Click
        SetActive(storeSettings)
        MainContainer.SuspendLayout()
        ScreenHost.Clear(MainContainer)
        AdminStoreSettingsPanelBuilder.LoadStoreSettings(MainContainer)
        MainContainer.ResumeLayout()
        MainContainer.Refresh()
    End Sub

    Private Sub products_Click(sender As Object, e As EventArgs) Handles products.Click
        SetActive(products)
        MainContainer.SuspendLayout()
        ScreenHost.Clear(MainContainer)
        AdminCategoriesPanelBuilder.LoadCategories(MainContainer)
        MainContainer.ResumeLayout()
        MainContainer.Refresh()
    End Sub

    Private Sub suppliers_Click(sender As Object, e As EventArgs) Handles suppliers.Click
        SetActive(suppliers)
        MainContainer.SuspendLayout()
        ScreenHost.Clear(MainContainer)
        AdminSuppliersPanelBuilder.LoadSuppliers(MainContainer)
        MainContainer.ResumeLayout()
        MainContainer.Refresh()
    End Sub

    Private Sub colorFormulas_Click(sender As Object, e As EventArgs) Handles colorFormulas.Click
        SetActive(colorFormulas)
        MainContainer.SuspendLayout()
        ScreenHost.Clear(MainContainer)
        AdminColorFormulasPanelBuilder.LoadColorFormulas(MainContainer)
        MainContainer.ResumeLayout()
        MainContainer.Refresh()
    End Sub

    Private Sub salesOverview_Click(sender As Object, e As EventArgs) Handles salesOverview.Click
        SetActive(salesOverview)
        MainContainer.SuspendLayout()
        ScreenHost.Clear(MainContainer)
        AdminSalesOverviewPanelBuilder.LoadSalesOverview(MainContainer)
        MainContainer.ResumeLayout()
        MainContainer.Refresh()
    End Sub

    Private Sub inventoryOverview_Click(sender As Object, e As EventArgs) Handles inventoryOverview.Click
        SetActive(inventoryOverview)
        MainContainer.SuspendLayout()
        ScreenHost.Clear(MainContainer)
        AdminInventoryOverviewPanelBuilder.LoadInventoryOverview(MainContainer)
        MainContainer.ResumeLayout()
        MainContainer.Refresh()
    End Sub

    Private Sub procurementOverview_Click(sender As Object, e As EventArgs) Handles procurementOverview.Click
        SetActive(procurementOverview)
        MainContainer.SuspendLayout()
        ScreenHost.Clear(MainContainer)
        AdminProcurementOverviewPanelBuilder.LoadProcurementOverview(MainContainer)
        MainContainer.ResumeLayout()
        MainContainer.Refresh()
    End Sub

    Private Sub financialReports_Click(sender As Object, e As EventArgs) Handles financialReports.Click
        SetActive(financialReports)
        MainContainer.SuspendLayout()
        ScreenHost.Clear(MainContainer)
        AdminFinancialReportsPanelBuilder.LoadFinancialReports(MainContainer)
        MainContainer.ResumeLayout()
        MainContainer.Refresh()
    End Sub

    Private Sub activityLogNav_Click(sender As Object, e As EventArgs) Handles activityLogNav.Click
        SetActive(activityLogNav)
        MainContainer.SuspendLayout()
        ScreenHost.Clear(MainContainer)
        AdminActivityLogPanelBuilder.LoadActivityLog(MainContainer)
        MainContainer.ResumeLayout()
        MainContainer.Refresh()
    End Sub

    Private Sub logout_Click(sender As Object, e As EventArgs) Handles logout.Click
        If AppModal.Show("Are you sure you want to log out?", "Logout",
                         MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then
            Return
        End If
        ActivityLog.Record("LOGOUT")
        ' Drop the signed-in identity before returning to the login screen, so
        ' nothing done afterwards is attributed to the user who just left.
        Session.Clear()

        Dim loginForm As New LogIn()
        loginForm.Show()
        Me.Close()
    End Sub

    ' Locks the screen after a few idle minutes (see IdleLock.vb).
    Private Sub AttachIdleLock(sender As Object, e As EventArgs) Handles MyBase.Shown
        IdleLock.Attach(Me, AddressOf SignOutFromLock)
        ' After the dashboard has painted, so the reminder sits on top of it.
        BeginInvoke(New Action(AddressOf Backup.RemindIfStale))
    End Sub

    ' An Admin signed this user out from the lock screen: no "are you sure".
    Private Sub SignOutFromLock()
        ActivityLog.Record("LOGOUT")
        Session.Clear()
        Dim loginForm As New LogIn()
        loginForm.Show()
        Me.Close()
    End Sub

End Class
