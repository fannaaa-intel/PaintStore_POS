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
    Private CardBorder As Color = Color.FromArgb(228, 232, 230)
    Private SubText As Color = Color.FromArgb(90, 114, 107)
    Private TileBg As Color = Color.FromArgb(236, 248, 244)

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
            End Sub

        BuildLoginCard()

        ' Pre-build SignUp so first navigation is instant
        _signUpForm = New SignUp(Me)
        _signUpForm.Show()
        _signUpForm.Hide()
    End Sub

    Private Sub BuildLoginCard()
        loginCard = New Panel With {
            .Size = New Size(420, 520),
            .BackColor = CardBg
        }
        CenterPanel(loginCard)
        Me.Controls.Add(loginCard)

        AddHandler loginCard.Paint,
            Sub(s As Object, pe As PaintEventArgs)
                Dim g = pe.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect = loginCard.ClientRectangle
                rect.Inflate(-1, -1)
                Dim r = 18
                Using path As New GraphicsPath()
                    path.AddArc(rect.X, rect.Y, r, r, 180, 90)
                    path.AddArc(rect.Right - r, rect.Y, r, r, 270, 90)
                    path.AddArc(rect.Right - r, rect.Bottom - r, r, r, 0, 90)
                    path.AddArc(rect.X, rect.Bottom - r, r, r, 90, 90)
                    path.CloseFigure()
                    Using fill As New SolidBrush(CardBg)
                        g.FillPath(fill, path)
                    End Using
                    Using pen As New Pen(CardBorder, 1.5F)
                        g.DrawPath(pen, path)
                    End Using
                End Using
            End Sub

        AddHandler Me.Resize, Sub() CenterPanel(loginCard)

        Dim curY As Integer = 40

        ' ===== LOGO + TITLE =====
        Dim logoFile As String = "paint.png"
        Dim logoBox As New PictureBox With {
            .Size = New Size(56, 56),
            .SizeMode = PictureBoxSizeMode.Zoom,
            .BackColor = TileBg,
            .Location = New Point((loginCard.Width - 56) \ 2, curY)
        }
        Dim lp As New GraphicsPath()
        lp.AddEllipse(0, 0, 56, 56)
        logoBox.Region = New Region(lp)
        If IO.File.Exists(logoFile) Then logoBox.Image = Image.FromFile(logoFile)
        loginCard.Controls.Add(logoBox)
        curY += 66

        Dim lblStore As New Label With {
            .Text = "Rainbow Paint Store",
            .Font = New Font("Segoe UI", 13, FontStyle.Bold),
            .ForeColor = MintDark,
            .AutoSize = True,
            .BackColor = Color.Transparent
        }
        loginCard.Controls.Add(lblStore)
        lblStore.Location = New Point((loginCard.Width - lblStore.PreferredWidth) \ 2, curY)
        curY += 28

        Dim lblSub As New Label With {
            .Text = "Point of Sale System",
            .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = SubText,
            .AutoSize = True,
            .BackColor = Color.Transparent
        }
        loginCard.Controls.Add(lblSub)
        lblSub.Location = New Point((loginCard.Width - lblSub.PreferredWidth) \ 2, curY)
        curY += 36

        ' ===== DIVIDER =====
        Dim divTop As New Panel With {
            .Height = 1, .Width = 340, .Left = 40, .Top = curY, .BackColor = CardBorder
        }
        loginCard.Controls.Add(divTop)
        curY += 20

        ' ===== USERNAME =====
        Dim lblUser As New Label With {
            .Text = "Username",
            .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold),
            .ForeColor = SubText,
            .AutoSize = True,
            .BackColor = Color.Transparent,
            .Location = New Point(40, curY)
        }
        loginCard.Controls.Add(lblUser)
        curY += 22

        pnlUser = MakeField("Enter username", curY, txtUser, False)
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
            .Location = New Point(40, curY)
        }
        loginCard.Controls.Add(lblPass)
        curY += 22

        pnlPass = MakeField("Password", curY, txtPass, True)
        loginCard.Controls.Add(pnlPass)
        txtPass = CType(pnlPass.Controls(0), TextBox)

        ' Eye toggle
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
        curY += 56

        ' ===== FORGOT =====
        Dim lnkForgot As New LinkLabel With {
            .Text = "Forgot Password?",
            .Font = New Font("Segoe UI", 9),
            .AutoSize = True,
            .BackColor = Color.Transparent,
            .Location = New Point(loginCard.Width - 160, curY)
        }
        lnkForgot.LinkColor = MintPrimary
        lnkForgot.ActiveLinkColor = MintDark
        AddHandler lnkForgot.LinkClicked,
            Sub() AppModal.Show("Please contact your administrator.", "Forgot Password", MessageBoxButtons.OK, MessageBoxIcon.Information)
        loginCard.Controls.Add(lnkForgot)
        curY += 28

        ' ===== LOGIN BUTTON =====
        Dim btnLogin As New Button With {
            .Text = "Login",
            .Size = New Size(340, 46),
            .Location = New Point(40, curY),
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
                Dim r2 = 12
                Using path As New GraphicsPath()
                    path.AddArc(0, 0, r2, r2, 180, 90)
                    path.AddArc(rect.Width - r2, 0, r2, r2, 270, 90)
                    path.AddArc(rect.Width - r2, rect.Height - r2, r2, r2, 0, 90)
                    path.AddArc(0, rect.Height - r2, r2, r2, 90, 90)
                    path.CloseFigure()
                    Dim isHover = btn.ClientRectangle.Contains(btn.PointToClient(Cursor.Position))
                    Dim c1 = If(isHover, Color.FromArgb(0, 140, 118), MintPrimary)
                    Dim c2 = If(isHover, MintPrimary, Color.FromArgb(0, 140, 118))
                    Using br As New LinearGradientBrush(rect, c1, c2, LinearGradientMode.Horizontal)
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
            .Text = " Sign Up",
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
        pnlSignup.Location = New Point((loginCard.Width - pnlSignup.PreferredSize.Width) \ 2, curY)

        ' ===== CLOSE BUTTON =====
        Dim btnClose As New Button With {
            .Text = "×",
            .Size = New Size(28, 28),
            .Location = New Point(loginCard.Width - 36, 8),
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

    ' ===== FIELD FACTORY =====
    Private Function MakeField(placeholder As String, y As Integer, ByRef txtRef As TextBox, isPass As Boolean) As Panel
        Dim pnl As New Panel With {
        .Size = New Size(340, 46),
        .Location = New Point(40, y),
        .BackColor = TileBg
    }

        Dim txt As New TextBox With {
        .BorderStyle = BorderStyle.None,
        .BackColor = TileBg,
        .ForeColor = SubText,
        .Text = placeholder,
        .Location = New Point(14, 14),
        .Width = 290,
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
            Dim r = 10
            Using path As New GraphicsPath()
                path.AddArc(rect.X, rect.Y, r, r, 180, 90)
                path.AddArc(rect.Right - r, rect.Y, r, r, 270, 90)
                path.AddArc(rect.Right - r, rect.Bottom - r, r, r, 0, 90)
                path.AddArc(rect.X, rect.Bottom - r, r, r, 90, 90)
                path.CloseFigure()
                Using fill As New SolidBrush(TileBg)
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
            If txtPass.Text <> "Password" Then
                txtPass.UseSystemPasswordChar = True
            End If
        End If
    End Sub

    ' ===== LOGIN LOGIC =====
    Private Sub HandleLogin(sender As Object, e As EventArgs)
        Dim username = txtUser.Text.Trim()
        Dim password = txtPass.Text.Trim()
        If username.Equals("Enter username", StringComparison.OrdinalIgnoreCase) Then username = ""
        If password = "Password" Then password = ""

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

        RouteByRole(Session.CurrentRole)
    End Sub

    ' ===== ROLE ROUTING =====
    Private Sub RouteByRole(role As String)
        Select Case role
            Case "Admin"
                Dim dash As New Dashboard()
                dash.Show()
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
                ' Dedicated Accountant screen isn't built yet.
                ' For now we open the dashboard so you can still get in.
                AppModal.Show("Welcome, " & Session.CurrentFullName & " (" & role & ")." & vbCrLf &
                            "A dedicated " & role & " screen isn't built yet — opening the dashboard.",
                            "Logged in", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Dim dash As New Dashboard()
                dash.Show()
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