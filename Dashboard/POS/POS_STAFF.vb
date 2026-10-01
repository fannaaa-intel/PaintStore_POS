Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms.VisualStyles.VisualStyleElement.StartPanel
Imports MySql.Data.MySqlClient

Public Class POS_STAFF

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
    ' =========================================================================
    '  KEYBOARD SHORTCUTS
    '
    '  ProcessCmdKey rather than KeyPreview/KeyDown: function keys reach this
    '  override before any focused control gets a chance to swallow them, so a
    '  shortcut works even while the cashier is typing in the search box.
    '
    '  The POS screen had no shortcuts at all -- every checkout needed the
    '  mouse, which is the slowest part of serving a queue.
    '
    '    F2  discount      F3  search      F4  hold       F5  retrieve
    '    F6  void          F7  reprint     F9  pay        Esc clear search
    ' =========================================================================
    Protected Overrides Function ProcessCmdKey(ByRef msg As Message, keyData As Keys) As Boolean
        If TransactionPanelBuilder.HandleShortcut(keyData) Then Return True
        Return MyBase.ProcessCmdKey(msg, keyData)
    End Function

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
                HomePanelBuilder.StopTimer()
                SetActive(_btnSettings)
                MainContainer.SuspendLayout()
                ScreenHost.Clear(MainContainer)
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

        ' ── OPEN THE SHIFT ──────────────────────────────────────────────────────
        ' Automatic: the cashier signing in IS the start of their shift, so there
        ' is nothing to confirm. Must run before the first panel loads -- the
        ' Cash Drawer reads Session.ShiftId and silently discards counts if it
        ' is still zero.
        EnsureShiftOpen()

        ' ── LOAD HOME ───────────────────────────────────────────────────────────
        shiftStartTime = ShiftStartedAt()
        MainContainer.SuspendLayout()
        ScreenHost.Clear(MainContainer)
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
            Dim isNavBtn As Boolean = (Array.IndexOf(navButtons, ctrl) >= 0) OrElse
                                      (ctrl Is logout) OrElse (ctrl Is _btnSettings)
            ' Nav buttons survive the collapse; captions/logo do not.
            ctrl.Visible = navIsOpen OrElse isNavBtn
        Next

        For Each btn In navButtons
            ApplyNavButtonState(btn, width)
        Next
        ApplyNavButtonState(_btnSettings, width)
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

    ' ===========================================================================
    ' SHIFT BOOTSTRAP
    '
    ' Make sure this cashier has an open shift, and load their quota.
    '
    ' This used to live inside CloseShiftOverlay, i.e. behind the Continue button
    ' of the Start Shift overlay. That overlay's only caller was removed at some
    ' point, so ShowStartShiftOverlay became dead code -- and the shift INSERT
    ' went with it. The result: no cashier could ever open a shift, the shifts
    ' table stayed empty, and every cash-drawer count was silently discarded
    ' because SaveDenomCount returns immediately when there is no shift id.
    '
    ' Opening a shift is a data concern, not a UI one, so it runs on form load
    ' regardless of whether any overlay is shown.
    ' ===========================================================================
    Private Sub EnsureShiftOpen()
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                ' Reuse the cashier's open shift if there is one. Starting a new
                ' shift unconditionally meant logging out and back in orphaned the
                ' previous shift: it stayed Open forever, its cash was never
                ' reconciled, and the Cash screen (which takes the most recent
                ' open shift) silently stopped reporting against it.
                Session.ShiftId = 0
                Using cmd As New MySqlCommand(
                    "SELECT shift_id FROM shifts " &
                    "WHERE user_id=@uid AND status='Open' AND ended_at IS NULL " &
                    "ORDER BY started_at DESC LIMIT 1", conn)
                    cmd.Parameters.AddWithValue("@uid", Session.CurrentUserId)
                    Dim existing = cmd.ExecuteScalar()
                    If existing IsNot Nothing AndAlso Not IsDBNull(existing) Then
                        Session.ShiftId = CInt(existing)
                    End If
                End Using

                If Session.ShiftId = 0 Then
                    ' INITIAL CASH: what is physically in the drawer before the
                    ' first sale. The Cash Drawer adds it to "expected", so a
                    ' till started with change in it no longer reads over by
                    ' exactly that amount when counted.
                    ' The cashier COUNTS the drawer and it is compared with
                    ' what the last shift on this register left, so cash
                    ' taken before the shift cannot be hidden by simply
                    ' declaring a smaller opening amount.
                    Dim last = LastDrawerHandover(conn)
                    Dim opening As OpeningCashModal.Result = OpeningCashModal.Show(last.left, last.info)
                    If opening Is Nothing Then
                        AppModal.Show("The drawer was not counted, so no shift was started." & Environment.NewLine &
                                      "Log out and back in to count it.", "Shift Not Started",
                                      MessageBoxButtons.OK, MessageBoxIcon.Warning)
                        Return
                    End If
                    Dim openingCash As Decimal = opening.Counted
                    Dim diff As Decimal? = If(last.left.HasValue, openingCash - last.left.Value, CType(Nothing, Decimal?))
                    Dim hasCheck As Boolean = HasOpeningCheckColumns(conn)

                    Using cmd As New MySqlCommand(
                        "INSERT INTO shifts (user_id, register_no, opening_float, started_at, status" &
                        If(hasCheck, ", expected_opening, opening_variance", "") & ") " &
                        "VALUES (@uid, '01', @float, NOW(), 'Open'" &
                        If(hasCheck, ", @exp, @var", "") & ")", conn)
                        cmd.Parameters.AddWithValue("@uid", Session.CurrentUserId)
                        cmd.Parameters.AddWithValue("@float", openingCash)
                        If hasCheck Then
                            cmd.Parameters.AddWithValue("@exp", If(last.left.HasValue, CObj(last.left.Value), DBNull.Value))
                            cmd.Parameters.AddWithValue("@var", If(diff.HasValue, CObj(diff.Value), DBNull.Value))
                        End If
                        cmd.ExecuteNonQuery()
                    End Using
                    Using cmd As New MySqlCommand("SELECT LAST_INSERT_ID()", conn)
                        Session.ShiftId = CInt(cmd.ExecuteScalar())
                    End Using
                    ' Opening a drawer starts a period of cash accountability;
                    ' the close is logged, so the open belongs in the trail too.
                    ' The count can never be below what the last shift left (the
                    ' window refuses it), so any difference is cash ADDED -- noted
                    ' here so the admin can see where the extra came from.
                    ActivityLog.Record("SHIFT_OPEN", "shift", Session.ShiftId,
                                       "Register 01, initial cash " & ChrW(8369) &
                                       openingCash.ToString("N2") &
                                       If(diff.HasValue AndAlso diff.Value > 0D,
                                          " (last shift left " & ChrW(8369) & last.left.Value.ToString("N2") &
                                          ", added " & ChrW(8369) & diff.Value.ToString("N2") & ")", ""))
                End If
                ' Load quota for this user
                Using cmd As New MySqlCommand(
                    "SELECT sales_quota FROM users WHERE user_id=@uid", conn)
                    cmd.Parameters.AddWithValue("@uid", Session.CurrentUserId)
                    Dim result = cmd.ExecuteScalar()
                    Session.UserQuota = If(result IsNot Nothing, CDec(result), 0D)
                End Using
            End Using
        Catch ex As Exception
            ' Without a shift, sales and drawer counts have nothing to attach to
            ' and are silently dropped. That is exactly the failure this whole
            ' fix is about, so it must not be swallowed again.
            System.Diagnostics.Debug.WriteLine("EnsureShiftOpen DB error: " & ex.Message)
            AppModal.Show("Could not open a cash shift:" & Environment.NewLine & Environment.NewLine &
                          ex.Message & Environment.NewLine & Environment.NewLine &
                          "Sales and drawer counts cannot be recorded until this is fixed.",
                          "Shift Not Opened", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    ' What the last CLOSED shift on this register counted into the drawer --
    ' i.e. what the next cashier should find. Also notes when another shift on
    ' the register is still open, since then the drawer is mid-shift.
    Private Function LastDrawerHandover(conn As MySqlConnection) As (left As Decimal?, info As String)
        Dim leftCash As Decimal? = Nothing
        Dim info As String = ""
        Using cmd As New MySqlCommand(
            "SELECT s.counted_cash, s.ended_at, u.full_name FROM shifts s " &
            "JOIN users u ON u.user_id = s.user_id " &
            "WHERE s.register_no = '01' AND s.status = 'Closed' AND s.counted_cash IS NOT NULL " &
            "ORDER BY s.ended_at DESC, s.shift_id DESC LIMIT 1", conn)
            Using rdr = cmd.ExecuteReader()
                If rdr.Read() Then
                    leftCash = Convert.ToDecimal(rdr("counted_cash"))
                    info = "closed by " & rdr("full_name").ToString() &
                           If(IsDBNull(rdr("ended_at")), "",
                              ", " & Convert.ToDateTime(rdr("ended_at")).ToString("MMM d, h:mm tt"))
                End If
            End Using
        End Using
        Using cmd As New MySqlCommand(
            "SELECT u.full_name FROM shifts s JOIN users u ON u.user_id = s.user_id " &
            "WHERE s.register_no = '01' AND s.status = 'Open' AND s.user_id <> @uid " &
            "ORDER BY s.started_at DESC LIMIT 1", conn)
            cmd.Parameters.AddWithValue("@uid", Session.CurrentUserId)
            Dim other = cmd.ExecuteScalar()
            If other IsNot Nothing AndAlso Not IsDBNull(other) Then
                info &= If(info = "", "", "; ") & "note: " & other.ToString() & " still has an open shift"
            End If
        End Using
        Return (leftCash, info)
    End Function

    ' migrations.sql section 21. Without it the check still runs and is logged;
    ' only the columns on the shift row are skipped.
    Private Function HasOpeningCheckColumns(conn As MySqlConnection) As Boolean
        Try
            Using cmd As New MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() " &
                "AND table_name = 'shifts' AND column_name = 'opening_variance'", conn)
                Return Convert.ToInt32(cmd.ExecuteScalar()) > 0
            End Using
        Catch
            Return False
        End Try
    End Function

    ' When the cashier signs back in to a shift that is still open, the shift
    ' began earlier than this login. The dashboard's Shift Start and Shift Hours
    ' use the real start, matching the Cash Drawer's "Opened" time.
    Private Function ShiftStartedAt() As DateTime
        If Session.ShiftId = 0 Then Return DateTime.Now
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand("SELECT started_at FROM shifts WHERE shift_id=@id", conn)
                    cmd.Parameters.AddWithValue("@id", Session.ShiftId)
                    Dim v = cmd.ExecuteScalar()
                    If v IsNot Nothing AndAlso Not IsDBNull(v) Then Return Convert.ToDateTime(v)
                End Using
            End Using
        Catch
        End Try
        Return DateTime.Now
    End Function

    Private Sub CloseShiftOverlay(sender As Object, e As EventArgs)
        If ShiftTimer IsNot Nothing Then ShiftTimer.Stop() : ShiftTimer.Dispose()

        EnsureShiftOpen()
        shiftStartTime = ShiftStartedAt()

        ShiftOverlay.Dispose()
        SetActive(home)
        MainContainer.SuspendLayout()
        ScreenHost.Clear(MainContainer)
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
                ' Same teardown as the sidebar Logout: this used to open LogIn
                ' directly, leaving the shift Open and the previous cashier's
                ' session and cart live for whoever signed in next.
                ActivityLog.Record("LOGOUT")
                EndSessionAndShowLogin()
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
            Case "account" : iconPath = "user.png"
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
        ScreenHost.Clear(MainContainer)   ' safe — burger is on Form, not MainContainer
        HomePanelBuilder.LoadHome(MainContainer, shiftStartTime)
        MainContainer.ResumeLayout()
    End Sub

    Private Sub transaction_Click(sender As Object, e As EventArgs) Handles transaction.Click
        SetActive(transaction)
        HomePanelBuilder.StopTimer()
        MainContainer.SuspendLayout()
        ScreenHost.Clear(MainContainer)
        TransactionPanelBuilder.LoadTransaction(MainContainer)
        MainContainer.ResumeLayout()
        MainContainer.Refresh()
    End Sub

    Private Sub sales_Click(sender As Object, e As EventArgs) Handles sales.Click
        SetActive(sales)
        HomePanelBuilder.StopTimer()
        MainContainer.SuspendLayout()
        ScreenHost.Clear(MainContainer)
        SalesPanelBuilder.LoadSales(MainContainer)
        MainContainer.ResumeLayout()
        MainContainer.Refresh()
    End Sub

    Private Sub returns_Click(sender As Object, e As EventArgs) Handles returns.Click
        SetActive(returns)
        HomePanelBuilder.StopTimer()
        MainContainer.SuspendLayout()
        ScreenHost.Clear(MainContainer)
        ReturnsPanelBuilder.LoadReturns(MainContainer)
        MainContainer.ResumeLayout()
        MainContainer.Refresh()
    End Sub

    Private Sub Reports_Click(sender As Object, e As EventArgs) Handles Reports.Click
        SetActive(Reports)
        HomePanelBuilder.StopTimer()
        MainContainer.SuspendLayout()
        ScreenHost.Clear(MainContainer)
        CashPanelBuilder.LoadCash(MainContainer)
        MainContainer.ResumeLayout()
        MainContainer.Refresh()
    End Sub

    ' True once a cash count has been recorded against the shift.
    Private Function ShiftHasBeenCounted(shiftId As Integer) As Boolean
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT counted_cash FROM shifts WHERE shift_id=@sid", conn)
                    cmd.Parameters.AddWithValue("@sid", shiftId)
                    Dim o = cmd.ExecuteScalar()
                    Return o IsNot Nothing AndAlso Not IsDBNull(o)
                End Using
            End Using
        Catch
            ' Unreachable database: don't add a scary warning on top of it.
            Return True
        End Try
    End Function

    Private Sub logout_Click(sender As Object, e As EventArgs) Handles logout.Click
        ' Logging out ends the shift. If it was never counted on the Cash screen
        ' it closes with no counted/expected/variance figures, so say that plainly
        ' instead of discarding the reconciliation silently.
        Dim msg As String = "Are you sure you want to log out?"
        If Session.ShiftId > 0 AndAlso Not ShiftHasBeenCounted(Session.ShiftId) Then
            msg &= Environment.NewLine & Environment.NewLine &
                   "Your drawer has not been counted yet. Logging out now closes " &
                   "this shift without a cash count or variance on record." &
                   Environment.NewLine & Environment.NewLine &
                   "Use CASH > Close Drawer first if you want it reconciled."
        End If

        If AppModal.Show(msg, "Logout",
                         MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then
            Return
        End If

        ActivityLog.Record("LOGOUT")
        EndSessionAndShowLogin()
    End Sub

    ' A hard close -- Alt+F4, the taskbar, Windows shutting down -- bypasses both
    ' logout paths. Without this the shift stays Open with no ended_at, so the
    ' next sign-in reopens it and its cash is never reconciled.
    Private Sub POS_STAFF_FormClosing(sender As Object, e As FormClosingEventArgs) _
            Handles MyBase.FormClosing
        ' Logout has already closed the shift and cleared the session by the time
        ' it calls Me.Close(); ShiftId = 0 is what tells us this is a hard exit.
        If Session.ShiftId <= 0 Then Return

        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "UPDATE shifts SET ended_at=NOW(), status='Closed' " &
                    "WHERE shift_id=@sid AND status='Open'", conn)
                    cmd.Parameters.AddWithValue("@sid", Session.ShiftId)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
            ActivityLog.Record("SHIFT_CLOSED_ON_EXIT")
        Catch ex As Exception
            ' Nothing useful can be shown while the form is tearing down.
            System.Diagnostics.Debug.WriteLine("exit shift close error: " & ex.Message)
        End Try

        Session.ShiftId = 0
    End Sub

    ' Close the shift, drop the till's Shared state and the signed-in identity,
    ' then hand back to the login screen.
    '
    ' Both ways out of the POS come through here -- the sidebar Logout button and
    ' the "Shift Ended" overlay. The overlay used to just open LogIn and close the
    ' form, leaving the shift Open, nothing in the activity log, and the previous
    ' cashier's session and cart live for whoever signed in next.
    Private Sub EndSessionAndShowLogin()
        If Session.ShiftId > 0 Then
            Try
                Using conn = Db.GetConnection()
                    conn.Open()
                    ' AND status='Open' protects a shift that was already closed
                    ' properly from the Cash screen: without it, logging out wiped
                    ' the counted/expected/variance figures recorded there.
                    Using cmd As New MySqlCommand(
                        "UPDATE shifts SET ended_at=NOW(), status='Closed' " &
                        "WHERE shift_id=@sid AND status='Open'", conn)
                        cmd.Parameters.AddWithValue("@sid", Session.ShiftId)
                        cmd.ExecuteNonQuery()
                    End Using
                End Using
            Catch ex As Exception
                System.Diagnostics.Debug.WriteLine("logout shift close error: " & ex.Message)
            End Try
            Session.ShiftId = 0
        End If

        ' Panel builders keep their working data in Shared fields, which outlive
        ' this form. Clear the till state too, or the next cashier inherits the
        ' previous one's cart and can reprint their receipt and refund slip.
        TransactionPanelBuilder.ResetForNewSession()
        ReturnsPanelBuilder.ResetForNewSession()
        ' Cash was missing from this list: its shift id, denomination counts and
        ' reconciliation figures are Shared too, and survived into the next
        ' cashier's session along with label references to disposed controls.
        CashPanelBuilder.ResetForNewSession()

        ' Drop the signed-in identity before returning to the login screen, so
        ' nothing done afterwards is attributed to the user who just left.
        Session.Clear()

        Dim loginForm As New LogIn()
        loginForm.Show()
        Me.Close()
    End Sub

End Class
