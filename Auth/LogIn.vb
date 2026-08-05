Imports System.Drawing.Drawing2D
Imports MySql.Data.MySqlClient
Public Class LogIn
    Inherits System.Windows.Forms.Form

    Private _signUpForm As SignUp

    ' ===== COLORS =====
    Private MintDark As Color = Color.FromArgb(10, 79, 66)
    Private MintPrimary As Color = Color.FromArgb(0, 168, 142)
    Private MintBg As Color = Color.FromArgb(244, 248, 246)
    Private CardBg As Color = Color.White
    Private CardBorder As Color = Color.FromArgb(205, 212, 208)
    Private SubText As Color = Color.FromArgb(90, 114, 107)
    Private TextOnDark As Color = Color.White
    Private SubTextOnDark As Color = Color.FromArgb(210, 235, 227)
    Private CheckOnDark As Color = Color.FromArgb(220, 240, 232)

    Private loginCard As Panel
    Private pnlUser As Panel
    Private pnlPass As Panel
    Private txtUser As TextBox
    Private txtPass As TextBox
    Private btnTogglePass As Button

    Private Sub LogIn_Load(sender As Object, e As EventArgs) Handles MyBase.Load
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

        ' Pre-build SignUp so first navigation is instant
        _signUpForm = New SignUp(Me)
        _signUpForm.Show()
        _signUpForm.Hide()
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
            .Text = "Sign in to continue to your dashboard.",
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
        AddHandler lnkForgot.LinkClicked,
            Sub() AppModal.Show("Please contact your administrator.", "Forgot Password", MessageBoxButtons.OK, MessageBoxIcon.Information)
        curY += 30

        ' ===== SIGN IN BUTTON =====
        Dim btnLogin As New Button With {
            .Text = "Sign in",
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
                    Dim isHover = btn.ClientRectangle.Contains(btn.PointToClient(Cursor.Position))
                    Dim fillColor = If(isHover, Color.FromArgb(0, 140, 118), MintPrimary)
                    Using br As New SolidBrush(fillColor)
                        pe.Graphics.FillPath(br, path)
                    End Using
                End Using
                TextRenderer.DrawText(pe.Graphics, btn.Text, btn.Font, rect, Color.White,
                    TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub
        AddHandler btnLogin.MouseEnter, Sub() btnLogin.Invalidate()
        AddHandler btnLogin.MouseLeave, Sub() btnLogin.Invalidate()
        AddHandler btnLogin.Click, AddressOf HandleLogin
        loginCard.Controls.Add(btnLogin)
        curY += 56

        ' ===== SIGN UP LINK =====
        Dim pnlSignup As New FlowLayoutPanel With {
            .AutoSize = True,
            .BackColor = Color.Transparent,
            .FlowDirection = FlowDirection.LeftToRight
        }
        Dim lblNo As New Label With {
            .Text = "Don't have an account?",
            .Font = New Font("Segoe UI", 9),
            .ForeColor = SubText,
            .AutoSize = True,
            .BackColor = Color.Transparent,
            .Margin = New Padding(0, 3, 0, 0)
        }
        Dim lnkSignUp As New LinkLabel With {
            .Text = " Sign up",
            .Font = New Font("Segoe UI", 9, FontStyle.Bold),
            .AutoSize = True,
            .BackColor = Color.Transparent,
            .Margin = New Padding(0, 3, 0, 0)
        }
        lnkSignUp.LinkColor = MintPrimary
        lnkSignUp.ActiveLinkColor = MintDark
        AddHandler lnkSignUp.LinkClicked,
            Sub()
                _signUpForm.Show()
                Me.Hide()
            End Sub
        pnlSignup.Controls.AddRange({lblNo, lnkSignUp})
        loginCard.Controls.Add(pnlSignup)
        pnlSignup.Location = New Point(padL + (fieldW - pnlSignup.PreferredSize.Width) \ 2, curY)
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
        Dim username = txtUser.Text.Trim()
        Dim password = txtPass.Text.Trim()
        If username.Equals("e.g. jdelacruz", StringComparison.OrdinalIgnoreCase) Then username = ""
        If password = "Enter your password" Then password = ""

        If username = "" OrElse password = "" Then
            AppModal.Show("Please enter your username and password.", "Login",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        Try
            Using cn = Db.GetConnection()
                cn.Open()
                Dim sql = "SELECT u.user_id, u.full_name, u.username, u.password_hash, u.is_active, r.role_name " &
                      "FROM users u JOIN roles r ON u.role_id = r.role_id " &
                      "WHERE u.username = @u LIMIT 1"
                Using cmd As New MySqlCommand(sql, cn)
                    cmd.Parameters.AddWithValue("@u", username)
                    Using rdr = cmd.ExecuteReader()
                        If Not rdr.Read() Then
                            AppModal.Show("Invalid credentials. Please try again.", "Login Failed",
                                        MessageBoxButtons.OK, MessageBoxIcon.Error)
                            Return
                        End If

                        Dim isActive = Convert.ToBoolean(rdr("is_active"))
                        Dim storedHash = rdr("password_hash").ToString()

                        If Not isActive Then
                            AppModal.Show("This account is disabled. Contact your administrator.",
                                        "Login Failed", MessageBoxButtons.OK, MessageBoxIcon.Error)
                            Return
                        End If

                        If Not PasswordHelper.VerifyPassword(password, storedHash) Then
                            AppModal.Show("Invalid credentials. Please try again.", "Login Failed",
                                        MessageBoxButtons.OK, MessageBoxIcon.Error)
                            Return
                        End If

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
        End Try

        ActivityLog.Record("LOGIN")
        RouteByRole(Session.CurrentRole)
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

    Private Sub LogIn_Load_1(sender As Object, e As EventArgs) Handles MyBase.Load

    End Sub
End Class