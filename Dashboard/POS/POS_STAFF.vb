Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms.VisualStyles.VisualStyleElement.StartPanel
Imports MySql.Data.MySqlClient

Public Class POS_STAFF

    Private MintPrimary As Color = Color.FromArgb(0, 200, 170)
    Private MintDarkTop As Color = Color.FromArgb(0, 120, 110)
    Private MintMid As Color = Color.FromArgb(0, 170, 150)
    Private MintLight As Color = Color.FromArgb(220, 255, 245)
    Private MintGlow As Color = Color.FromArgb(0, 255, 200)
    Private NavBg As Color = Color.FromArgb(250, 252, 251)
    Private NavHover As Color = Color.FromArgb(238, 248, 244)

    Private MainContainer As Panel

    ' ── Hamburger toggle state ──────────────────────────────────────────────────
    Private Const NAV_OPEN_WIDTH As Integer = 260
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

    ' ── Shift overlay references ────────────────────────────────────────────────
    Private ShiftOverlay As Panel
    Private ShiftBox As Panel
    Private ShiftTimer As Timer
    Private lblShiftTime As Label
    Private shiftStartTime As DateTime

    ' ===========================================================================
    ' FORM LOAD
    ' ===========================================================================
    Private Sub POS_STAFF_Load(sender As Object, e As EventArgs) Handles MyBase.Load

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
            .Text = "Paint POS",
            .Font = New Font("Segoe UI Semibold", 14, FontStyle.Bold),
            .ForeColor = Color.FromArgb(30, 45, 43),
            .AutoSize = True,
           .Location = New Point(64, 22)
        }
        Nav.Controls.Add(lblLogo)

        home.Text = "Dashboard"
        transaction.Text = "New Transaction"
        sales.Text = "Sales History"
        returns.Text = "Returns"
        Reports.Text = "Cash Drawer"

        ' Settings is not part of the cashier nav.
        Settings.Visible = False
        Nav.Controls.Remove(Settings)
        logout.Text = "Logout"

        Dim lblMenu As New Label With {
            .Text = "MENU",
            .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold),
            .ForeColor = Color.FromArgb(120, 138, 134),
            .AutoSize = True,
            .Location = New Point(22, 96)
        }
        Nav.Controls.Add(lblMenu)

        Dim navButtons() As Button = {home, transaction, sales, returns, Reports}
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
        shiftStartTime = DateTime.Now
        MainContainer.SuspendLayout()
        MainContainer.Controls.Clear()
        HomePanelBuilder.LoadHome(MainContainer, shiftStartTime)
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

    ' Collapsed nav is an ICON RAIL: the nav buttons stay visible and clickable,
    ' only their captions are dropped. Labels/logo (which are text-only) hide.
    Private Sub ApplyNavState()
        Nav.SuspendLayout()

        Dim navButtons() As Button = {home, transaction, sales, returns, Reports}
        Dim width As Integer = If(navIsOpen, NAV_OPEN_WIDTH, NAV_CLOSED_WIDTH)
        Nav.Width = width

        For Each ctrl As Control In Nav.Controls
            If ctrl Is btnHamburger Then Continue For
            Dim isNavBtn As Boolean = (Array.IndexOf(navButtons, ctrl) >= 0) OrElse (ctrl Is logout)
            ' Nav buttons survive the collapse; captions/logo do not.
            ctrl.Visible = navIsOpen OrElse isNavBtn
        Next

        For Each btn In navButtons
            ApplyNavButtonState(btn, width)
        Next
        ApplyNavButtonState(logout, width)

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
    ' ===========================================================================
    ' HELPER: reload active panel after nav toggle so content re-layouts
    ' ===========================================================================
    Private Sub ReloadCurrentPanel()
        ' Simply refresh — the scroll/wrap panels inside already use
        ' DockStyle.Fill + Resize handlers, so they reflow automatically.
        MainContainer.PerformLayout()
        MainContainer.Invalidate(True)
    End Sub

    ' ===========================================================================
    ' PLACEHOLDER
    ' ===========================================================================
    Private Sub LoadPlaceholder(title As String)
        MainContainer.SuspendLayout()
        MainContainer.Controls.Clear()


        Dim panel As New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = Color.FromArgb(220, 240, 235)
        }
        Dim lblTitle As New Label With {
            .Text = title,
            .Font = New Font("Segoe UI", 18, FontStyle.Bold),
            .ForeColor = MintDarkTop,
            .AutoSize = True,
            .Location = New Point(30, 30)
        }
        Dim lblInfo As New Label With {
            .Text = "Ready for integration...",
            .Font = New Font("Segoe UI", 10),
            .ForeColor = Color.Gray,
            .AutoSize = True,
            .Location = New Point(32, 70)
        }
        panel.Controls.Add(lblTitle)
        panel.Controls.Add(lblInfo)
        MainContainer.Controls.Add(panel)
        MainContainer.ResumeLayout()
    End Sub

    ' ===========================================================================
    ' SHIFT OVERLAY — unchanged
    ' ===========================================================================
    Private Sub ShowStartShiftOverlay()
        ShiftOverlay = New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = Color.FromArgb(25, 0, 200, 170)
        }
        Me.Controls.Add(ShiftOverlay)
        ShiftOverlay.BringToFront()

        ShiftBox = New Panel With {
            .Size = New Size(320, 200),
            .BackColor = Color.White
        }
        AddHandler ShiftBox.Paint, Sub(s, ev) PanelRenderer.Draw(CType(s, Panel), ev, True)

        ShiftOverlay.Controls.Add(ShiftBox)
        ShiftBox.Left = (ShiftOverlay.Width - ShiftBox.Width) \ 2
        ShiftBox.Top = (ShiftOverlay.Height - ShiftBox.Height) \ 2
        AddHandler ShiftOverlay.Resize,
            Sub()
                ShiftBox.Left = (ShiftOverlay.Width - ShiftBox.Width) \ 2
                ShiftBox.Top = (ShiftOverlay.Height - ShiftBox.Height) \ 2
            End Sub

        Dim lblTitle As New Label With {
            .Text = "Start Shift", .Font = New Font("Segoe UI Semibold", 16, FontStyle.Bold),
            .ForeColor = MintDarkTop, .AutoSize = True
        }
        ShiftBox.Controls.Add(lblTitle)
        lblTitle.Location = New Point((ShiftBox.Width - lblTitle.PreferredWidth) \ 2, 20)

        Dim lblDate As New Label With {
            .Text = DateTime.Now.ToString("MMMM dd, yyyy"),
            .Font = New Font("Segoe UI", 10), .ForeColor = Color.Gray, .AutoSize = True
        }
        ShiftBox.Controls.Add(lblDate)
        lblDate.Location = New Point((ShiftBox.Width - lblDate.PreferredWidth) \ 2, 70)

        lblShiftTime = New Label With {
            .Text = DateTime.Now.ToString("hh:mm tt"),
            .Font = New Font("Segoe UI", 11, FontStyle.Bold),
            .ForeColor = MintPrimary, .AutoSize = True
        }
        ShiftBox.Controls.Add(lblShiftTime)
        lblShiftTime.Location = New Point((ShiftBox.Width - lblShiftTime.PreferredWidth) \ 2, 95)

        Dim btnContinue As New Button With {.Text = "Start Shift", .Size = New Size(160, 45)}
        ApplyModernButton(btnContinue)
        ShiftBox.Controls.Add(btnContinue)
        btnContinue.Location = New Point((ShiftBox.Width - btnContinue.Width) \ 2, 130)
        AddHandler btnContinue.Click, AddressOf CloseShiftOverlay

        ShiftTimer = New Timer With {.Interval = 1000}
        AddHandler ShiftTimer.Tick,
            Sub()
                lblShiftTime.Text = DateTime.Now.ToString("hh:mm tt")
                lblShiftTime.Left = (ShiftBox.Width - lblShiftTime.PreferredWidth) \ 2
            End Sub
        ShiftTimer.Start()
    End Sub

    Private Sub CloseShiftOverlay(sender As Object, e As EventArgs)
        shiftStartTime = DateTime.Now
        If ShiftTimer IsNot Nothing Then ShiftTimer.Stop() : ShiftTimer.Dispose()

        ' INSERT shift into DB and store the ID
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "INSERT INTO shifts (user_id, register_no, opening_float, started_at, status) " &
                    "VALUES (@uid, '01', 0, NOW(), 'Open')", conn)
                    cmd.Parameters.AddWithValue("@uid", Session.CurrentUserId)
                    cmd.ExecuteNonQuery()
                End Using
                Using cmd As New MySqlCommand("SELECT LAST_INSERT_ID()", conn)
                    Session.ShiftId = CInt(cmd.ExecuteScalar())
                End Using
                ' Load quota for this user
                Using cmd As New MySqlCommand(
                    "SELECT sales_quota FROM users WHERE user_id=@uid", conn)
                    cmd.Parameters.AddWithValue("@uid", Session.CurrentUserId)
                    Dim result = cmd.ExecuteScalar()
                    Session.UserQuota = If(result IsNot Nothing, CDec(result), 0D)
                End Using
            End Using
        Catch ex As Exception
            System.Diagnostics.Debug.WriteLine("CloseShiftOverlay DB error: " & ex.Message)
        End Try

        ShiftOverlay.Dispose()
        SetActive(home)
        MainContainer.SuspendLayout()
        MainContainer.Controls.Clear()
        HomePanelBuilder.LoadHome(MainContainer, shiftStartTime)
        MainContainer.ResumeLayout()
        btnHamburger.BringToFront()
    End Sub

    ' ===========================================================================
    ' END SHIFT OVERLAY — unchanged
    ' ===========================================================================
    Private Sub ShowEndShiftOverlay()
        Dim endTime As DateTime = DateTime.Now
        Dim duration As TimeSpan = endTime - shiftStartTime
        Dim totalTime As String = $"{duration.Hours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}"

        Dim endOverlay As New Panel With {
            .Dock = DockStyle.Fill, .BackColor = Color.FromArgb(25, 0, 200, 170)
        }
        Me.Controls.Add(endOverlay)
        endOverlay.BringToFront()

        Dim endBox As New Panel With {.Size = New Size(320, 220), .BackColor = Color.White}
        endOverlay.Controls.Add(endBox)
        endBox.Left = (endOverlay.Width - endBox.Width) \ 2
        endBox.Top = (endOverlay.Height - endBox.Height) \ 2
        AddHandler endOverlay.Resize,
            Sub()
                endBox.Left = (endOverlay.Width - endBox.Width) \ 2
                endBox.Top = (endOverlay.Height - endBox.Height) \ 2
            End Sub
        AddHandler endBox.Paint, Sub(s, ev) PanelRenderer.Draw(CType(s, Panel), ev, True)

        Dim lblTitle As New Label With {
            .Text = "Shift Ended", .Font = New Font("Segoe UI", 16, FontStyle.Bold),
            .ForeColor = MintDarkTop, .AutoSize = True
        }
        endBox.Controls.Add(lblTitle)
        lblTitle.Location = New Point((endBox.Width - lblTitle.Width) \ 2, 20)

        For Each info In {("Date: " & endTime.ToString("MMMM dd, yyyy"), 70),
                          ("Time: " & endTime.ToString("hh:mm tt"), 95)}
            Dim lbl As New Label With {
                .Text = info.Item1, .Font = New Font("Segoe UI", 10),
                .ForeColor = Color.Gray, .AutoSize = True
            }
            endBox.Controls.Add(lbl)
            lbl.Location = New Point((endBox.Width - lbl.Width) \ 2, info.Item2)
        Next

        Dim lblTotal As New Label With {
            .Text = "Total Time: " & totalTime,
            .Font = New Font("Segoe UI", 11, FontStyle.Bold),
            .ForeColor = MintPrimary, .AutoSize = True
        }
        endBox.Controls.Add(lblTotal)
        lblTotal.Location = New Point((endBox.Width - lblTotal.Width) \ 2, 125)

        Dim btnOk As New Button With {.Text = "Logout", .Size = New Size(160, 45)}
        ApplyModernButton(btnOk)
        endBox.Controls.Add(btnOk)
        btnOk.Location = New Point((endBox.Width - btnOk.Width) \ 2, 160)
        AddHandler btnOk.Click,
    Sub(sender As Object, e As EventArgs)
        Dim lf As New LogIn()
        lf.Show()
        Me.Close()
    End Sub

    End Sub

    ' ===========================================================================
    ' STYLE HELPERS
    ' ===========================================================================
    Private Sub StyleButton(btn As Button)
        btn.Parent = Nav

        Dim iconPath As String = ""
        Select Case btn.Name.ToLower()
            Case "home" : iconPath = "home.png"
            Case "transaction" : iconPath = "transaction.png"
            Case "sales" : iconPath = "recent.png"
            Case "returns" : iconPath = "return.png"
            Case "inventory" : iconPath = "inventory.png"
            Case "reports" : iconPath = "coins.png"
            Case "settings" : iconPath = "settings.png"
            Case "logout" : iconPath = "logout.png"
        End Select

        ' Remember the caption before anything can clear it, so the collapsed rail
        ' can put it back (and show it as a tooltip meanwhile).
        navCaptions(btn) = btn.Text

        If IO.File.Exists(iconPath) Then
            Dim src As Bitmap
            Using img = Image.FromFile(iconPath)
                src = New Bitmap(img, New Size(24, 24))
            End Using
            Dim padded As New Bitmap(40, 24)
            Using g As Graphics = Graphics.FromImage(padded)
                g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic
                g.DrawImage(src, 0, 0, 24, 24)
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
            Dim bare As Image = Nothing
            If navIconBare.TryGetValue(logout, bare) AndAlso bare IsNot Nothing Then
                navIconBare(logout) = TintLogoutIcon(bare, Color.White)
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
    Private Sub home_Click(sender As Object, e As EventArgs) Handles home.Click
        SetActive(home)
        HomePanelBuilder.StopTimer()
        MainContainer.SuspendLayout()
        MainContainer.Controls.Clear()   ' safe — burger is on Form, not MainContainer
        HomePanelBuilder.LoadHome(MainContainer, shiftStartTime)
        MainContainer.ResumeLayout()
    End Sub

    Private Sub transaction_Click(sender As Object, e As EventArgs) Handles transaction.Click
        SetActive(transaction)
        HomePanelBuilder.StopTimer()
        MainContainer.SuspendLayout()
        MainContainer.Controls.Clear()
        TransactionPanelBuilder.LoadTransaction(MainContainer)
        MainContainer.ResumeLayout()
        MainContainer.Refresh()
    End Sub

    Private Sub sales_Click(sender As Object, e As EventArgs) Handles sales.Click
        SetActive(sales)
        HomePanelBuilder.StopTimer()
        MainContainer.SuspendLayout()
        MainContainer.Controls.Clear()
        SalesPanelBuilder.LoadSales(MainContainer)
        MainContainer.ResumeLayout()
        MainContainer.Refresh()
    End Sub

    Private Sub returns_Click(sender As Object, e As EventArgs) Handles returns.Click
        SetActive(returns)
        HomePanelBuilder.StopTimer()
        MainContainer.SuspendLayout()
        MainContainer.Controls.Clear()
        ReturnsPanelBuilder.LoadReturns(MainContainer)
        MainContainer.ResumeLayout()
        MainContainer.Refresh()
    End Sub

    Private Sub Reports_Click(sender As Object, e As EventArgs) Handles Reports.Click
        SetActive(Reports)
        HomePanelBuilder.StopTimer()
        MainContainer.SuspendLayout()
        MainContainer.Controls.Clear()
        CashPanelBuilder.LoadCash(MainContainer)
        MainContainer.ResumeLayout()
        MainContainer.Refresh()
    End Sub

    Private Sub logout_Click(sender As Object, e As EventArgs) Handles logout.Click
        If AppModal.Show("Are you sure you want to log out?", "Logout",
                         MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then
            Return
        End If

        ActivityLog.Record("LOGOUT")

        ' Close the active shift on logout
        If Session.ShiftId > 0 Then
            Try
                Using conn = Db.GetConnection()
                    conn.Open()
                    Using cmd As New MySqlCommand(
                        "UPDATE shifts SET ended_at=NOW(), status='Closed' " &
                        "WHERE shift_id=@sid", conn)
                        cmd.Parameters.AddWithValue("@sid", Session.ShiftId)
                        cmd.ExecuteNonQuery()
                    End Using
                End Using
            Catch ex As Exception
                System.Diagnostics.Debug.WriteLine("logout shift close error: " & ex.Message)
            End Try
            Session.ShiftId = 0
        End If

        Dim loginForm As New LogIn()
        loginForm.Show()
        Me.Close()
    End Sub

End Class