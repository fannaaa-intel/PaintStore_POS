Imports System.Drawing
Imports System.Drawing.Drawing2D


Public Class ACCOUNTANT_STAFF

    Private MintPrimary As Color = Theme.Primary
    Private MintDarkTop As Color = Color.FromArgb(0, 80, 72)
    Private MintMid As Color = Color.FromArgb(0, 170, 150)
    Private MintLight As Color = Color.FromArgb(220, 255, 245)
    Private MintGlow As Color = Color.FromArgb(0, 255, 200)
    Private NavBg As Color = Color.FromArgb(250, 252, 251)
    Private NavHover As Color = Color.FromArgb(238, 248, 244)

    Private MainContainer As Panel
    Private _btnSettings As Button          ' pinned above Logout

    Private Const NAV_OPEN_WIDTH As Integer = 260
    Private Const NAV_CLOSED_WIDTH As Integer = 56
    Private navIsOpen As Boolean = True
    Private btnHamburger As Button

    ' Captions are cleared from the buttons when the rail collapses, so the real
    ' label has to live somewhere else. Icons are cached in both layouts:
    ' "padded" = 40x24 with the glyph left-aligned (caption sits beside it),
    ' "bare"   = 24x24 for the centred icon-only rail.
    Private navCaptions As New Dictionary(Of Button, String)
    Private navIconPadded As New Dictionary(Of Button, Image)
    Private navIconBare As New Dictionary(Of Button, Image)
    Private NavTips As New ToolTip()


    Private Sub ACCOUNTANT_STAFF_Load(sender As Object, e As EventArgs) Handles MyBase.Load

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
                    pe.Graphics.DrawLine(pen, Nav.Width - 1, 0, Nav.Width - 1, Nav.Height)
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

        ' ── HAMBURGER BUTTON ────────────────────────────────────────────────────
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

        Dim burgerFile As String = "burger-menu.png"
        If IO.File.Exists(burgerFile) Then
            btnHamburger.Image = New Bitmap(Image.FromFile(burgerFile), New Size(22, 22))
        Else
            btnHamburger.Text = "≡"
            btnHamburger.Font = New Font("Segoe UI", 16, FontStyle.Bold)
            btnHamburger.ForeColor = Color.FromArgb(0, 80, 72)
        End If

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
        Dim logoPath As New GraphicsPath()
        logoPath.AddEllipse(0, 0, logoBox.Width, logoBox.Height)
        logoBox.Region = New Region(logoPath)
        If IO.File.Exists("paint.png") Then logoBox.Image = Image.FromFile("paint.png")
        Nav.Controls.Add(logoBox)

        Dim lblLogo As New Label With {
            .Text = "Paint POS",
            .Font = New Font("Segoe UI Semibold", 14, FontStyle.Bold),
            .ForeColor = Color.FromArgb(30, 45, 43),
            .AutoSize = True,
            .Location = New Point(64, 22)
        }
        Nav.Controls.Add(lblLogo)

        home.Text = "Dashboard"
        incomeExpenses.Text = "Income & Expenses"
        payables.Text = "Payables"
        salesHistory.Text = "Sales History"
        ledger.Text = "Ledger"
        reports.Text = "Reports"
        logout.Text = "Logout"

        Dim lblMenu As New Label With {
            .Text = "MENU",
            .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold),
            .ForeColor = Color.FromArgb(120, 138, 134),
            .AutoSize = True,
            .Location = New Point(22, 96)
}
        Nav.Controls.Add(lblMenu)

        Dim navButtons() As Button = {home, incomeExpenses, payables, salesHistory, ledger, reports}
        Dim topPos As Integer = 128
        For Each btn In navButtons
            StyleButton(btn)
            btn.Left = 14
            btn.Top = topPos
            btn.Width = Nav.Width - 28
            btn.Height = 48
            RoundButton(btn, 12)
            topPos += 58
        Next


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
                MainContainer.Controls.Clear()
                SettingsPanelBuilder.LoadSettings(MainContainer)
                MainContainer.ResumeLayout()
                MainContainer.Refresh()
            End Sub

        StyleButton(logout)
        logout.Left = 14
        logout.Width = Nav.Width - 28
        logout.Height = 48
        logout.Top = Nav.Height - 110
        logout.Anchor = AnchorStyles.Bottom Or AnchorStyles.Left
        RoundButton(logout, 12)

        SetActive(home)
        TintLogout()

        ' ── LOAD HOME ───────────────────────────────────────────────────────────
        MainContainer.SuspendLayout()
        MainContainer.Controls.Clear()
        AccountantDashPanelBuilder.LoadDash(MainContainer)
        MainContainer.ResumeLayout()
        btnHamburger.Location = New Point(NAV_OPEN_WIDTH - 46, 18)
        btnHamburger.BringToFront()
    End Sub

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

        Dim width As Integer = If(navIsOpen, NAV_OPEN_WIDTH, NAV_CLOSED_WIDTH)
        Nav.Width = width

        For Each ctrl As Control In Nav.Controls
            If ctrl Is btnHamburger Then Continue For
            ' Any button in the rail is navigation and must survive the collapse.
            ctrl.Visible = navIsOpen OrElse TypeOf ctrl Is Button
        Next

        For Each ctrl As Control In Nav.Controls
            If ctrl Is btnHamburger Then Continue For
            Dim btn = TryCast(ctrl, Button)
            If btn IsNot Nothing Then ApplyNavButtonState(btn, width)
        Next

        btnHamburger.BackColor = If(navIsOpen, Color.White, Color.Transparent)
        btnHamburger.Location = If(navIsOpen,
                                   New Point(NAV_OPEN_WIDTH - 46, 18),
                                   New Point((NAV_CLOSED_WIDTH - btnHamburger.Width) \ 2, 18))
        btnHamburger.BringToFront()

        Nav.ResumeLayout()
        Nav.Invalidate()
        MainContainer.PerformLayout()
        MainContainer.Refresh()
    End Sub

    ' Re-lay one nav button for the current rail width. Collapsed: no caption and
    ' the icon is centred; expanded: caption returns and the icon sits left.
    Private Sub ApplyNavButtonState(btn As Button, navWidth As Integer)
        If btn Is Nothing Then Return

        If navIsOpen Then
            btn.Text = NavCaption(btn)
            btn.Left = 14
            btn.Width = navWidth - 28
            btn.TextImageRelation = TextImageRelation.ImageBeforeText
            btn.ImageAlign = ContentAlignment.MiddleLeft
            btn.TextAlign = ContentAlignment.MiddleLeft
            btn.Padding = New Padding(20, 0, 4, 0)
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

        btn.Parent = Nav
        ' "Income & Expenses" must show its "&", not turn it into a
        ' keyboard-shortcut underline.
        btn.UseMnemonic = False

        Dim iconPath As String = ""
        Select Case btn.Name.ToLower()
            Case "home" : iconPath = "home.png"
            Case "incomeexpenses" : iconPath = "wallet-income.png"
            Case "payables" : iconPath = "task-checklist.png"
            Case "saleshistory" : iconPath = "time-past.png"
            Case "ledger" : iconPath = "coins.png"
            Case "reports" : iconPath = "newspaper.png"
            Case "logout" : iconPath = "logout.png"
            Case "account" : iconPath = "user.png"
            Case "settings" : iconPath = "settings.png"
        End Select

        If IO.File.Exists(iconPath) Then
            Dim src As New Bitmap(Image.FromFile(iconPath), New Size(24, 24))
            Dim padded As New Bitmap(40, 24)
            Using g As Graphics = Graphics.FromImage(padded)
                g.InterpolationMode = InterpolationMode.HighQualityBicubic
                ' Recolor to black while keeping each pixel's original alpha,
                ' so the icon shape stays and only its color changes.
                Dim blackMatrix As New Imaging.ColorMatrix(New Single()() {
                    New Single() {0, 0, 0, 0, 0},
                    New Single() {0, 0, 0, 0, 0},
                    New Single() {0, 0, 0, 0, 0},
                    New Single() {0, 0, 0, 1, 0},
                    New Single() {0, 0, 0, 0, 1}
                })
                Using attrs As New Imaging.ImageAttributes()
                    attrs.SetColorMatrix(blackMatrix)
                    g.DrawImage(src, New Rectangle(0, 0, 24, 24),
                                0, 0, 24, 24, GraphicsUnit.Pixel, attrs)
                End Using
            End Using
            navIconPadded(btn) = padded
            navIconBare(btn) = src
            btn.Image = padded
        End If

        btn.Font = New Font("Segoe UI Semibold", 12, FontStyle.Regular)
        btn.ForeColor = Color.FromArgb(90, 105, 102)
        btn.TextImageRelation = TextImageRelation.ImageBeforeText
        btn.ImageAlign = ContentAlignment.MiddleLeft
        btn.TextAlign = ContentAlignment.MiddleLeft
        btn.Padding = New Padding(20, 0, 4, 0)
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
        For Each ctrl As Control In Nav.Controls
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
        Dim path As New GraphicsPath()
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

    ' ===========================================================================
    ' BUTTON CLICK HANDLERS
    ' ===========================================================================
    Private Sub home_Click(sender As Object, e As EventArgs) Handles home.Click
        SetActive(home)
        MainContainer.SuspendLayout()
        MainContainer.Controls.Clear()
        AccountantDashPanelBuilder.LoadDash(MainContainer)
        MainContainer.ResumeLayout()
    End Sub

    Private Sub incomeExpenses_Click(sender As Object, e As EventArgs) Handles incomeExpenses.Click
        SetActive(incomeExpenses)
        MainContainer.SuspendLayout()
        MainContainer.Controls.Clear()
        AccountantIncomePanelBuilder.LoadIncome(MainContainer)
        MainContainer.ResumeLayout()
        MainContainer.Refresh()
    End Sub

    Private Sub payables_Click(sender As Object, e As EventArgs) Handles payables.Click
        SetActive(payables)
        MainContainer.SuspendLayout()
        MainContainer.Controls.Clear()
        AccountantPayablesPanelBuilder.LoadPayables(MainContainer)
        MainContainer.ResumeLayout()
        MainContainer.Refresh()
    End Sub

    Private Sub salesHistory_Click(sender As Object, e As EventArgs) Handles salesHistory.Click
        SetActive(salesHistory)
        MainContainer.SuspendLayout()
        MainContainer.Controls.Clear()
        AccountantSalesHistoryPanelBuilder.LoadSalesHistory(MainContainer)
        MainContainer.ResumeLayout()
        MainContainer.Refresh()
    End Sub

    Private Sub ledger_Click(sender As Object, e As EventArgs) Handles ledger.Click
        SetActive(ledger)
        MainContainer.SuspendLayout()
        MainContainer.Controls.Clear()
        AccountantLedgerPanelBuilder.LoadLedger(MainContainer)
        MainContainer.ResumeLayout()
        MainContainer.Refresh()
    End Sub

    Private Sub reports_Click(sender As Object, e As EventArgs) Handles reports.Click
        SetActive(reports)
        MainContainer.SuspendLayout()
        MainContainer.Controls.Clear()
        AccountantReportsPanelBuilder.LoadReports(MainContainer)
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

End Class