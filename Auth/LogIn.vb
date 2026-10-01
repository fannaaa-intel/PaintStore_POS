Imports System.Drawing.Drawing2D
Imports MySql.Data.MySqlClient
Public Class LogIn
    Inherits System.Windows.Forms.Form

    ' ===== COLORS =====
    Private MintDark As Color = Theme.Dark
    Private MintPrimary As Color = Theme.Primary
    Private MintBg As Color = Theme.PageBg
    Private CardBg As Color = Color.White
    Private CardBorder As Color = Color.FromArgb(205, 212, 208)
    Private SubText As Color = Theme.SubText
    Private TextOnDark As Color = Color.White
    Private SubTextOnDark As Color = Color.FromArgb(210, 235, 227)
    Private CheckOnDark As Color = Color.FromArgb(220, 240, 232)

    Private loginCard As Panel
    Private pnlUser As Panel
    Private pnlPass As Panel
    Private txtUser As TextBox
    Private txtPass As TextBox
    Private btnTogglePass As Button
    Private _btnLogin As Button

    Private Sub LogIn_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        LiteralAmpersands.Install()
        Me.FormBorderStyle = FormBorderStyle.None
        Me.WindowState = FormWindowState.Maximized
        Me.DoubleBuffered = True
        Me.BackColor = MintBg

        AddHandler Me.Paint,
            Sub(s As Object, pe As PaintEventArgs)
                Dim g = pe.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Using br As New SolidBrush(Color.FromArgb(18, 0, 168, 142))
                    g.FillEllipse(br, -80, -80, 340, 340)
                End Using
                Using br As New SolidBrush(Color.FromArgb(12, 0, 168, 142))
                    g.FillEllipse(br, Me.Width - 200, Me.Height - 200, 340, 340)
                End Using

                ' floating shadow behind the card — several soft, offset layers fake a blur
                If loginCard IsNot Nothing Then
                    For i As Integer = 10 To 1 Step -1
                        Dim spread As Integer = i * 2
                        Dim sRect As New Rectangle(
                            loginCard.Left - spread,
                            loginCard.Top - spread + i,
                            loginCard.Width + spread * 2,
                            loginCard.Height + spread * 2)
                        Dim r3 As Integer = 18 + spread
                        Using shadowPath As New GraphicsPath()
                            shadowPath.AddArc(sRect.X, sRect.Y, r3, r3, 180, 90)
                            shadowPath.AddArc(sRect.Right - r3, sRect.Y, r3, r3, 270, 90)
                            shadowPath.AddArc(sRect.Right - r3, sRect.Bottom - r3, r3, r3, 0, 90)
                            shadowPath.AddArc(sRect.X, sRect.Bottom - r3, r3, r3, 90, 90)
                            shadowPath.CloseFigure()
                            Using shBrush As New SolidBrush(Color.FromArgb(3, 10, 40, 34))
                                g.FillPath(shBrush, shadowPath)
                            End Using
                        End Using
                    Next
                End If
            End Sub

        BuildLoginCard()
    End Sub

    ' ===== CARD SHELL (split panel) =====
    Private Sub BuildLoginCard()
        Dim cardW As Integer = 900
        Dim cardH As Integer = 520
        Dim splitX As Integer = CInt(cardW * 0.4)

        loginCard = New Panel With {
            .Size = New Size(cardW, cardH),
            .BackColor = Me.BackColor
        }
        CenterPanel(loginCard)
        Me.Controls.Add(loginCard)

        Dim cardRect As New Rectangle(1, 1, cardW - 2, cardH - 2)
        Dim r As Integer = 18
        Dim cardPath As New GraphicsPath()
        cardPath.AddArc(cardRect.X, cardRect.Y, r, r, 180, 90)
        cardPath.AddArc(cardRect.Right - r, cardRect.Y, r, r, 270, 90)
        cardPath.AddArc(cardRect.Right - r, cardRect.Bottom - r, r, r, 0, 90)
        cardPath.AddArc(cardRect.X, cardRect.Bottom - r, r, r, 90, 90)
        cardPath.CloseFigure()

        ' clip the panel itself to the rounded shape so its literal square corners
        ' don't show through against the shadow drawn behind it
        loginCard.Region = New Region(cardPath)

        AddHandler loginCard.Paint,
            Sub(s As Object, pe As PaintEventArgs)
                Dim g = pe.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias

                Using leftBrush As New SolidBrush(MintDark)
                    g.FillRectangle(leftBrush, cardRect.X, cardRect.Y, splitX - cardRect.X, cardRect.Height)
                End Using
                Using c1 As New SolidBrush(Color.FromArgb(28, 255, 255, 255))
                    g.FillEllipse(c1, cardRect.X - 60, cardRect.Y - 60, 200, 200)
                End Using
                Using c2 As New SolidBrush(Color.FromArgb(20, 255, 255, 255))
                    g.FillEllipse(c2, cardRect.X - 80, cardRect.Bottom - 160, 240, 240)
                End Using
                Using rightBrush As New SolidBrush(CardBg)
                    g.FillRectangle(rightBrush, splitX, cardRect.Y, cardRect.Right - splitX, cardRect.Height)
                End Using

                Using pen As New Pen(CardBorder, 1.8F)
                    g.DrawPath(pen, cardPath)
                End Using
            End Sub


        AddHandler Me.Resize, Sub() CenterPanel(loginCard)

        BuildLeftPanel(splitX, cardH)
        BuildRightPanel(splitX, cardW)

        ' ===== CLOSE BUTTON =====
        Dim btnClose As New Button With {
            .Text = "×",
            .Size = New Size(28, 28),
            .Location = New Point(cardW - 36, 8),
            .FlatStyle = FlatStyle.Flat,
            .ForeColor = SubText,
            .Font = New Font("Segoe UI", 13),
            .BackColor = Color.Transparent,
            .Cursor = Cursors.Hand
        }
        btnClose.FlatAppearance.BorderSize = 0
        AddHandler btnClose.Click, Sub() Application.Exit()
        loginCard.Controls.Add(btnClose)
        btnClose.BringToFront()
    End Sub

    ' ===== LEFT PANEL (brand) =====
    Private Sub BuildLeftPanel(splitX As Integer, cardH As Integer)
        Dim pad As Integer = 40
        Dim curY As Integer = pad

        Dim logoTile As New Panel With {
            .Size = New Size(40, 40),
            .Location = New Point(pad, curY),
            .BackColor = Color.Transparent
        }
        AddHandler logoTile.Paint,
            Sub(s As Object, pe As PaintEventArgs)
                Dim g = pe.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim rc = logoTile.ClientRectangle
                Dim r = 10
                Using path As New GraphicsPath()
                    path.AddArc(rc.X, rc.Y, r, r, 180, 90)
                    path.AddArc(rc.Right - r, rc.Y, r, r, 270, 90)
                    path.AddArc(rc.Right - r, rc.Bottom - r, r, r, 0, 90)
                    path.AddArc(rc.X, rc.Bottom - r, r, r, 90, 90)
                    path.CloseFigure()
                    Using br As New SolidBrush(Color.White)
                        g.FillPath(br, path)
                    End Using
                End Using
            End Sub
        loginCard.Controls.Add(logoTile)

        Dim logoFile As String = "paint.png"
        If IO.File.Exists(logoFile) Then
            Dim logoImg As New PictureBox With {
                .Size = New Size(22, 22),
                .SizeMode = PictureBoxSizeMode.Zoom,
                .BackColor = Color.Transparent,
                .Location = New Point(9, 9),
                .Image = Image.FromFile(logoFile)
            }
            logoTile.Controls.Add(logoImg)
        End If
        curY += 56

        Dim lblStore As New Label With {
            .Text = "Rainbow Paint Store",
            .Font = New Font("Segoe UI", 15, FontStyle.Bold),
            .ForeColor = TextOnDark,
            .AutoSize = True,
            .BackColor = Color.Transparent,
            .Location = New Point(pad, curY)
        }
        loginCard.Controls.Add(lblStore)
        curY += 32

        Dim lblSub As New Label With {
            .Text = "Point of sale, inventory, and procurement in one place.",
            .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = SubTextOnDark,
            .AutoSize = False,
            .Size = New Size(splitX - pad * 2, 60),
            .BackColor = Color.Transparent,
            .Location = New Point(pad, curY)
        }
        loginCard.Controls.Add(lblSub)

        ' feature checklist, anchored near the bottom of the panel
        Dim features() As String = {"Real-time inventory sync", "Role-based dashboards", "Built for branch staff"}
        Dim featY As Integer = cardH - pad - (features.Length * 28)
        For Each f As String In features
            Dim lblCheck As New Label With {
                .Text = "✓  " & f,
                .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold),
                .ForeColor = CheckOnDark,
                .AutoSize = True,
                .BackColor = Color.Transparent,
                .Location = New Point(pad, featY)
            }
            loginCard.Controls.Add(lblCheck)
            featY += 28
        Next
    End Sub

    ' ===== RIGHT PANEL (form) =====
    Private Sub BuildRightPanel(splitX As Integer, cardW As Integer)
        Dim padL As Integer = splitX + 48
        Dim fieldW As Integer = cardW - padL - 48
        Dim curY As Integer = 95

        Dim lblWelcome As New Label With {
            .Text = "Welcome back",
            .Font = New Font("Segoe UI", 14, FontStyle.Bold),
            .ForeColor = MintDark,
            .AutoSize = True,
            .BackColor = Color.Transparent,
            .Location = New Point(padL, curY)
        }
        loginCard.Controls.Add(lblWelcome)
        curY += 30

        Dim lblWelcomeSub As New Label With {
            .Text = "Log in to continue to your dashboard.",
            .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = SubText,
            .AutoSize = True,
            .BackColor = Color.Transparent,
            .Location = New Point(padL, curY)
        }
        loginCard.Controls.Add(lblWelcomeSub)
        curY += 44

        ' ===== USERNAME =====
        Dim lblUser As New Label With {
            .Text = "Username",
            .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold),
            .ForeColor = SubText,
            .AutoSize = True,
            .BackColor = Color.Transparent,
            .Location = New Point(padL, curY)
        }
        loginCard.Controls.Add(lblUser)
        curY += 22

        pnlUser = MakeField("e.g. jdelacruz", padL, curY, fieldW, False)
        loginCard.Controls.Add(pnlUser)
        txtUser = CType(pnlUser.Controls(0), TextBox)
        curY += 56

        ' ===== PASSWORD =====
        Dim lblPass As New Label With {
            .Text = "Password",
            .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold),
            .ForeColor = SubText,
            .AutoSize = True,
            .BackColor = Color.Transparent,
            .Location = New Point(padL, curY)
        }
        loginCard.Controls.Add(lblPass)
        curY += 22

        pnlPass = MakeField("Enter your password", padL, curY, fieldW, True)
        loginCard.Controls.Add(pnlPass)
        txtPass = CType(pnlPass.Controls(0), TextBox)

        btnTogglePass = New Button With {
            .Size = New Size(28, 28),
            .Location = New Point(pnlPass.Left + pnlPass.Width - 36, pnlPass.Top + 9),
            .FlatStyle = FlatStyle.Flat,
            .BackColor = Color.Transparent,
            .ForeColor = SubText,
            .Font = New Font("Segoe UI", 11),
            .Cursor = Cursors.Hand,
            .Text = "👁"
        }
        btnTogglePass.FlatAppearance.BorderSize = 0
        AddHandler btnTogglePass.Click, AddressOf TogglePass
        loginCard.Controls.Add(btnTogglePass)
        btnTogglePass.BringToFront()
        curY += 48

        ' ===== FORGOT =====
        Dim lnkForgot As New LinkLabel With {
            .Text = "Forgot password?",
            .Font = New Font("Segoe UI", 9),
            .AutoSize = True,
            .BackColor = Color.Transparent
        }
        lnkForgot.LinkColor = MintPrimary
        lnkForgot.ActiveLinkColor = MintDark
        loginCard.Controls.Add(lnkForgot)
        lnkForgot.Location = New Point(padL + fieldW - lnkForgot.PreferredWidth, curY)
        AddHandler lnkForgot.LinkClicked, Sub() ShowForgotPassword()
        curY += 30

        ' ===== SIGN IN BUTTON =====
        Dim btnLogin As New Button With {
            .Text = "Log In",
            .Size = New Size(fieldW, 46),
            .Location = New Point(padL, curY),
            .FlatStyle = FlatStyle.Flat,
            .ForeColor = Color.White,
            .Font = New Font("Segoe UI", 11, FontStyle.Bold),
            .Cursor = Cursors.Hand,
            .BackColor = Color.Transparent
        }
        btnLogin.FlatAppearance.BorderSize = 0
        AddHandler btnLogin.Paint,
            Sub(s As Object, pe As PaintEventArgs)
                Dim btn = CType(s, Button)
                Dim rect = btn.ClientRectangle
                pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim r2 = 10
                Using path As New GraphicsPath()
                    path.AddArc(0, 0, r2, r2, 180, 90)
                    path.AddArc(rect.Width - r2, 0, r2, r2, 270, 90)
                    path.AddArc(rect.Width - r2, rect.Height - r2, r2, r2, 0, 90)
                    path.AddArc(0, rect.Height - r2, r2, r2, 90, 90)
                    path.CloseFigure()
                    ' A disabled button is the "signing in" state; it must read as
                    ' inert rather than still inviting a second click.
                    Dim isHover = btn.Enabled AndAlso
                                  btn.ClientRectangle.Contains(btn.PointToClient(Cursor.Position))
                    Dim fillColor As Color
                    If Not btn.Enabled Then
                        ' MintPrimary blended 45% toward white -- derived from the
                        ' palette rather than picked, so it stays on-brand while
                        ' reading clearly as unavailable.
                        fillColor = Color.FromArgb(115, 207, 193)
                    ElseIf isHover Then
                        fillColor = Color.FromArgb(0, 140, 118)
                    Else
                        fillColor = MintPrimary
                    End If
                    Using br As New SolidBrush(fillColor)
                        pe.Graphics.FillPath(br, path)
                    End Using
                End Using
                TextRenderer.DrawText(pe.Graphics, btn.Text, btn.Font, rect, Color.White,
                    TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub
        AddHandler btnLogin.MouseEnter, Sub() btnLogin.Invalidate()
        AddHandler btnLogin.MouseLeave, Sub() btnLogin.Invalidate()
        _btnLogin = btnLogin
        AddHandler btnLogin.Click, AddressOf HandleLogin
        loginCard.Controls.Add(btnLogin)

        ' Enter submits the form from either field instead of forcing a click.
        Me.AcceptButton = btnLogin
        curY += 56

        ' ===== NO SELF SIGN-UP =====
        ' Accounts are created only by an administrator, who also records the
        ' person's email. Say so here, so nobody hunts for a sign-up link.
        Dim lblNoAccount As New Label With {
            .Text = "No account? Ask your administrator to create one.",
            .Font = New Font("Segoe UI", 9),
            .ForeColor = SubText,
            .AutoSize = True,
            .BackColor = Color.Transparent
        }
        loginCard.Controls.Add(lblNoAccount)
        lblNoAccount.Location = New Point(padL + (fieldW - lblNoAccount.PreferredWidth) \ 2, curY + 3)
    End Sub

    ' ===== FIELD FACTORY =====
    Private Function MakeField(placeholder As String, x As Integer, y As Integer, w As Integer, isPass As Boolean) As Panel
        Dim pnl As New Panel With {
            .Size = New Size(w, 46),
            .Location = New Point(x, y),
            .BackColor = Color.Transparent
        }

        Dim txt As New TextBox With {
            .BorderStyle = BorderStyle.None,
            .BackColor = CardBg,
            .ForeColor = SubText,
            .Text = placeholder,
            .Location = New Point(14, 14),
            .Width = w - 60,
            .Font = New Font("Segoe UI", 10.5F)
        }
        If isPass Then txt.UseSystemPasswordChar = False

        AddHandler txt.Enter,
            Sub()
                If txt.Text = placeholder Then
                    txt.Text = ""
                    txt.ForeColor = MintDark
                    If isPass Then txt.UseSystemPasswordChar = True
                End If
                pnl.Invalidate()
            End Sub

        AddHandler txt.Leave,
            Sub()
                If txt.Text = "" Then
                    txt.Text = placeholder
                    txt.ForeColor = SubText
                    If isPass Then txt.UseSystemPasswordChar = False
                End If
                pnl.Invalidate()
            End Sub

        pnl.Controls.Add(txt)

        AddHandler pnl.Paint,
            Sub(s As Object, pe As PaintEventArgs)
                Dim g = pe.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect = pnl.ClientRectangle
                rect.Inflate(-1, -1)
                Dim r = 8
                Using path As New GraphicsPath()
                    path.AddArc(rect.X, rect.Y, r, r, 180, 90)
                    path.AddArc(rect.Right - r, rect.Y, r, r, 270, 90)
                    path.AddArc(rect.Right - r, rect.Bottom - r, r, r, 0, 90)
                    path.AddArc(rect.X, rect.Bottom - r, r, r, 90, 90)
                    path.CloseFigure()
                    Using fill As New SolidBrush(CardBg)
                        g.FillPath(fill, path)
                    End Using
                    Dim isFocused = txt.Focused
                    Using pen As New Pen(If(isFocused, MintPrimary, CardBorder), If(isFocused, 1.8F, 1.2F))
                        g.DrawPath(pen, path)
                    End Using
                End Using
            End Sub

        Return pnl
    End Function

    ' ===== TOGGLE PASSWORD =====
    Private Sub TogglePass(sender As Object, e As EventArgs)
        If txtPass.UseSystemPasswordChar Then
            txtPass.UseSystemPasswordChar = False
        Else
            If txtPass.Text <> "Enter your password" Then
                txtPass.UseSystemPasswordChar = True
            End If
        End If
    End Sub

    ' ===== LOGIN LOGIC ===== (unchanged from original)
    Private Sub HandleLogin(sender As Object, e As EventArgs)
        ' Guard against a second submit while the first is still in flight — the
        ' Enter key repeats happily and each press opened its own connection.
        If _btnLogin IsNot Nothing AndAlso Not _btnLogin.Enabled Then Return

        Dim username = txtUser.Text.Trim()
        ' The password is deliberately NOT trimmed: leading/trailing spaces are
        ' legal characters, and trimming here rejected passwords that SignUp had
        ' hashed untrimmed.
        Dim password = txtPass.Text
        If username.Equals("e.g. jdelacruz", StringComparison.OrdinalIgnoreCase) Then username = ""
        If password = "Enter your password" Then password = ""

        If username = "" OrElse password = "" Then
            AppModal.Show("Please enter your username and password.", "Login",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning)
            ' Put the caret where the missing value belongs.
            If username = "" Then txtUser.Focus() Else txtPass.Focus()
            Return
        End If

        ' Refuse while this username is in its cooling-off period. Checked before
        ' the connection is opened so a locked-out attacker cannot even make the
        ' app do work, and before any per-account message so it leaks nothing
        ' about whether the username is real.
        If LoginThrottle.IsLockedOut(username) Then
            Dim wait = LoginThrottle.LockoutRemaining(username)
            Dim mins As Integer = CInt(Math.Ceiling(wait.TotalMinutes))
            AppModal.Show("Too many failed log-in attempts." & vbCrLf &
                        "Please wait " & mins.ToString() & " minute" & If(mins = 1, "", "s") & " and try again.",
                        "Temporarily Locked", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            txtPass.Text = ""
            Return
        End If

        SetBusy(True)
        Try
            Using cn = Db.GetConnection()
                cn.Open()
                ' status only exists once migrations.sql section 6 is applied.
                ' Selecting it unconditionally would break every login on an
                ' un-migrated database, so it is added to the projection only
                ' when the column is actually there.
                Dim hasStatus As Boolean = AccountRequests.HasApprovalColumns()
                Dim sql = "SELECT u.user_id, u.full_name, u.username, u.password_hash, u.is_active, " &
                      If(hasStatus, "u.status, ", "") &
                      "r.role_name " &
                      "FROM users u JOIN roles r ON u.role_id = r.role_id " &
                      "WHERE u.username = @u LIMIT 1"
                Using cmd As New MySqlCommand(sql, cn)
                    cmd.Parameters.AddWithValue("@u", username)
                    Using rdr = cmd.ExecuteReader()
                        If Not rdr.Read() Then
                            ' Counted like any other failure: if only real usernames
                            ' were throttled, the lockout itself would reveal which
                            ' names exist.
                            ShowLoginFailed(username)
                            Return
                        End If

                        Dim isActive = Convert.ToBoolean(rdr("is_active"))
                        Dim storedHash = rdr("password_hash").ToString()

                        ' Self sign-up was removed: accounts are made by an admin.
                        ' Rows left by the old sign-up screen (Pending / Rejected)
                        ' were never approved accounts, so they are refused with
                        ' the same wording as a bad password - nobody can tell
                        ' the username exists.
                        If hasStatus Then
                            Dim status As String = If(IsDBNull(rdr("status")), "Approved", rdr("status").ToString())
                            If Not String.Equals(status, "Approved", StringComparison.OrdinalIgnoreCase) Then
                                ShowLoginFailed(username)
                                Return
                            End If
                        End If

                        If Not isActive Then
                            AppModal.Show("This account is disabled. Contact your administrator.",
                                        "Login Failed", MessageBoxButtons.OK, MessageBoxIcon.Error)
                            Return
                        End If

                        If Not PasswordHelper.VerifyPassword(password, storedHash) Then
                            ShowLoginFailed(username)
                            Return
                        End If

                        ' Cleared so an earlier run of typos does not count against
                        ' the next genuine mistake.
                        LoginThrottle.RecordSuccess(username)

                        ' success — remember who is logged in
                        Session.CurrentUserId = Convert.ToInt32(rdr("user_id"))
                        Session.CurrentFullName = rdr("full_name").ToString()
                        Session.CurrentUsername = rdr("username").ToString()
                        Session.CurrentRole = rdr("role_name").ToString()
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not reach the database: " & ex.Message, "Login Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        Finally
            ' Runs on every exit path, including the early Returns above, so the
            ' form can never be left stuck on "Logging in...".
            SetBusy(False)
        End Try

        ' A password an admin issued over the counter has been spoken aloud, so
        ' it is replaced before the session goes anywhere. This sits AFTER the
        ' Try block on purpose: the reader and connection are closed by now, and
        ' SetBusy(False) has already run in the Finally, so the dialog does not
        ' appear over a form still reading "Logging in...".
        '
        ' Nothing has been routed yet either - Session was populated, but no
        ' dashboard exists - so declining leaves the user on the login screen
        ' with the temporary password still requiring a change next time.
        If AccountRequests.MustChangePassword(Session.CurrentUserId) Then
            If Not ChangePasswordModal.Prompt(Session.CurrentUserId, Session.CurrentFullName) Then
                Session.Clear()
                txtPass.Text = ""
                txtUser.Focus()
                Return
            End If
            ActivityLog.Record("PASSWORD_CHANGE", "user", Session.CurrentUserId,
                               "Set own password after admin reset")
        End If

        ActivityLog.Record("LOGIN")
        RouteByRole(Session.CurrentRole)
    End Sub

    ' One place that reports a failed sign-in, so every rejection path counts
    ' toward the lockout and they all read identically to the person trying.
    Private Sub ShowLoginFailed(username As String)
        Dim nowLocked As Boolean = LoginThrottle.RecordFailure(username)
        If nowLocked Then
            AppModal.Show("Too many failed log-in attempts." & vbCrLf &
                        "This account is locked for a few minutes.",
                        "Temporarily Locked", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        Else
            Dim left As Integer = LoginThrottle.AttemptsLeft(username)
            Dim warn As String = ""
            ' Only warn near the end: a count shown from the first typo is noise,
            ' and it tells an attacker exactly how much room they have left.
            If left <= 2 Then
                warn = vbCrLf & vbCrLf & left.ToString() & " attempt" & If(left = 1, "", "s") &
                       " remaining before this account is locked."
            End If
            AppModal.Show("Invalid credentials. Please try again." & warn, "Login Failed",
                        MessageBoxButtons.OK, MessageBoxIcon.Error)
        End If
        txtPass.Text = ""
    End Sub

    ' Visual busy state for the sign-in round trip.
    Private Sub SetBusy(busy As Boolean)
        If _btnLogin IsNot Nothing Then
            _btnLogin.Enabled = Not busy
            _btnLogin.Text = If(busy, "Logging in...", "Log In")
            _btnLogin.Cursor = If(busy, Cursors.Default, Cursors.Hand)
            _btnLogin.Invalidate()
        End If
        Me.Cursor = If(busy, Cursors.WaitCursor, Cursors.Default)
        ' Repaint before the blocking database call starts.
        If busy Then Application.DoEvents()
    End Sub

    ' ===== ROLE ROUTING ===== (CHANGE THIS IF ADMIN IS ON DEVELOPMENT)
    Private Sub RouteByRole(role As String)
        Select Case role
            Case "Admin"
                Dim adminForm As New ADMIN_STAFF()
                adminForm.Show()
                Me.Hide()
            Case "Cashier"
                Dim staff As New POS_STAFF()
                staff.Show()
                Me.Hide()
            Case "Inventory"
                Dim inv As New INVENTORY_STAFF()
                inv.Show()
                Me.Hide()
            Case "Procurement"
                Dim proc As New PROCUREMENT_STAFF()
                proc.Show()
                Me.Hide()
            Case "Accountant"
                Dim accForm As New ACCOUNTANT_STAFF()
                accForm.Show()
                Me.Hide()
            Case Else
                AppModal.Show("Unknown role: " & role, "Login",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End Select
    End Sub

    ' =========================================================================
    '  FORGOT PASSWORD
    '
    '  Files a request the admin can act on in Admin > User Management >
    '  Password Requests, rather than the old dead-end notice.
    '
    '  The confirmation is deliberately identical whether or not the username
    '  exists. Saying "no such user" would turn this box into a way to test
    '  which usernames are real.
    ' =========================================================================
    Private Sub ShowForgotPassword()
        If Not AccountRequests.HasResetTable() Then
            ' Un-migrated database - keep the previous behaviour rather than
            ' promising a request that cannot be recorded.
            AppModal.Show("Please contact your administrator.", "Forgot Password",
                          MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If

        Dim MintDarkC As Color = Color.FromArgb(10, 79, 66)
        Dim MintPrimaryC As Color = Color.FromArgb(0, 168, 142)
        Dim SubTextC As Color = Color.FromArgb(90, 114, 107)
        Dim TileBgC As Color = Color.FromArgb(236, 248, 244)
        Dim BorderC As Color = Color.FromArgb(228, 232, 230)

        Dim W As Integer = 420
        Dim H As Integer = 290

        Using dlg As New Form With {
            .FormBorderStyle = FormBorderStyle.None,
            .StartPosition = FormStartPosition.CenterParent,
            .ClientSize = New Size(W, H),
            .BackColor = Color.White
        }
            Using p As New Drawing2D.GraphicsPath()
                p.AddArc(0, 0, 18, 18, 180, 90)
                p.AddArc(W - 18, 0, 18, 18, 270, 90)
                p.AddArc(W - 18, H - 18, 18, 18, 0, 90)
                p.AddArc(0, H - 18, 18, 18, 90, 90)
                p.CloseFigure()
                dlg.Region = New Region(p)
            End Using

            AddHandler dlg.Paint,
                Sub(s As Object, pe As PaintEventArgs)
                    pe.Graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
                    Using pen As New Pen(BorderC, 1.5F)
                        pe.Graphics.DrawRectangle(pen, 0, 0, W - 1, H - 1)
                    End Using
                End Sub

            dlg.Controls.Add(New Label With {
                .Text = "Reset your password",
                .Font = New Font("Segoe UI", 13, FontStyle.Bold),
                .ForeColor = MintDarkC,
                .AutoSize = True,
                .Location = New Point(32, 30)
            })
            dlg.Controls.Add(New Label With {
                .Text = "Enter your username. We will notify your" & vbCrLf &
                        "administrator, who will issue a new password.",
                .Font = New Font("Segoe UI", 9.5F),
                .ForeColor = SubTextC,
                .AutoSize = True,
                .Location = New Point(32, 62)
            })

            Dim shell As New Panel With {
                .Size = New Size(W - 64, 46),
                .Location = New Point(32, 116),
                .BackColor = TileBgC
            }
            Dim txt As New TextBox With {
                .BorderStyle = BorderStyle.None,
                .BackColor = TileBgC,
                .ForeColor = MintDarkC,
                .Font = New Font("Segoe UI", 10.5F),
                .Location = New Point(14, 14),
                .Width = shell.Width - 28
            }
            shell.Controls.Add(txt)
            AddHandler shell.Paint,
                Sub(s As Object, pe As PaintEventArgs)
                    Dim g = pe.Graphics
                    g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
                    Dim rect = shell.ClientRectangle
                    rect.Inflate(-1, -1)
                    Using path As New Drawing2D.GraphicsPath()
                        path.AddArc(rect.X, rect.Y, 10, 10, 180, 90)
                        path.AddArc(rect.Right - 10, rect.Y, 10, 10, 270, 90)
                        path.AddArc(rect.Right - 10, rect.Bottom - 10, 10, 10, 0, 90)
                        path.AddArc(rect.X, rect.Bottom - 10, 10, 10, 90, 90)
                        path.CloseFigure()
                        Using fill As New SolidBrush(TileBgC)
                            g.FillPath(fill, path)
                        End Using
                        Using pen As New Pen(If(txt.Focused, MintPrimaryC, BorderC),
                                             If(txt.Focused, 1.8F, 1.2F))
                            g.DrawPath(pen, path)
                        End Using
                    End Using
                    If txt.Text = "" AndAlso Not txt.Focused Then
                        TextRenderer.DrawText(g, "Your username", txt.Font,
                            New Rectangle(txt.Left, txt.Top - 1, txt.Width, txt.Height + 2),
                            Color.FromArgb(150, 165, 160),
                            TextFormatFlags.Left Or TextFormatFlags.VerticalCenter)
                    End If
                End Sub
            AddHandler txt.GotFocus, Sub() shell.Invalidate()
            AddHandler txt.LostFocus, Sub() shell.Invalidate()
            AddHandler txt.TextChanged, Sub() shell.Invalidate()
            dlg.Controls.Add(shell)

            Dim lblErr As New Label With {
                .Text = "",
                .Font = New Font("Segoe UI", 8.5F),
                .ForeColor = Color.FromArgb(198, 40, 40),
                .AutoSize = True,
                .Location = New Point(34, 166)
            }
            dlg.Controls.Add(lblErr)

            Dim btnCancel As New Button With {
                .Text = "Cancel",
                .Size = New Size(120, 42),
                .Location = New Point(32, 200),
                .FlatStyle = FlatStyle.Flat,
                .BackColor = Color.FromArgb(240, 245, 243),
                .ForeColor = SubTextC,
                .Font = New Font("Segoe UI Semibold", 10),
                .Cursor = Cursors.Hand
            }
            btnCancel.FlatAppearance.BorderSize = 0
            AddHandler btnCancel.Click, Sub() dlg.Close()
            dlg.Controls.Add(btnCancel)

            Dim btnSend As New Button With {
                .Text = "Send request",
                .Size = New Size(W - 64 - 132, 42),
                .Location = New Point(164, 200),
                .FlatStyle = FlatStyle.Flat,
                .BackColor = MintPrimaryC,
                .ForeColor = Color.White,
                .Font = New Font("Segoe UI Semibold", 10),
                .Cursor = Cursors.Hand
            }
            btnSend.FlatAppearance.BorderSize = 0
            AddHandler btnSend.Click,
                Sub()
                    Dim u As String = txt.Text.Trim()
                    If u = "" Then
                        lblErr.Text = "Please enter your username."
                        txt.Focus()
                        Return
                    End If
                    AccountRequests.FileResetRequest(u)
                    dlg.Close()
                    AppModal.Show(
                        "If that username belongs to an account, your administrator " &
                        "has been notified." & vbCrLf & vbCrLf &
                        "They will issue you a new password.",
                        "Request Sent", MessageBoxButtons.OK, MessageBoxIcon.Information)
                End Sub
            dlg.Controls.Add(btnSend)

            AddHandler txt.KeyDown,
                Sub(s As Object, ke As KeyEventArgs)
                    If ke.KeyCode = Keys.Enter Then
                        ke.SuppressKeyPress = True
                        btnSend.PerformClick()
                    End If
                End Sub

            dlg.ShowDialog(Me)
        End Using
    End Sub

    ' ===== CENTER PANEL =====
    Private Sub CenterPanel(pnl As Panel)
        pnl.Left = (Me.ClientSize.Width - pnl.Width) \ 2
        pnl.Top = (Me.ClientSize.Height - pnl.Height) \ 2
    End Sub

    Private Sub InitializeComponent()
        Me.SuspendLayout()
        '
        'LogIn
        '
        Me.ClientSize = New System.Drawing.Size(284, 261)
        Me.Name = "LogIn"
        Me.ResumeLayout(False)

    End Sub

End Class