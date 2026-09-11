Imports System.Drawing.Drawing2D
Imports MySql.Data.MySqlClient

' =============================================================================
'  SignUp — request an account
'
'  Replaces the previous mock-up, whose Create button only showed a message box.
'  A submission now writes a real users row with status='Pending'; the account
'  cannot log in until an admin approves it in Admin > User Management.
'
'  LAYOUT
'    A single card, wider than the old one (560px) and split into two labelled
'    groups so the form reads as a form rather than a stack of boxes:
'      "Your details"  — full name, requested role
'      "Sign-in"       — username, password, confirm password
'
'  VALIDATION
'    Every field validates on blur and shows its reason underneath in red
'    instead of interrupting with a modal. The submit button stays disabled
'    until the whole form is valid, so the error state is visible rather than
'    discovered by pressing the button.
' =============================================================================
Public Class SignUp
    Inherits System.Windows.Forms.Form

    Private _loginForm As LogIn

    Public Sub New(loginRef As LogIn)
        InitializeComponent()
        _loginForm = loginRef
    End Sub

    ' ===== COLORS =====
    Private MintDark As Color = Theme.Dark
    Private MintPrimary As Color = Theme.Primary
    Private MintBg As Color = Theme.PageBg
    Private CardBg As Color = Color.White
    Private CardBorder As Color = Theme.BorderSoft
    Private SubText As Color = Theme.SubText
    Private FaintText As Color = Theme.FaintText
    Private TileBg As Color = Theme.TileBg
    Private ErrClr As Color = Color.FromArgb(198, 40, 40)
    Private OkClr As Color = Color.FromArgb(0, 140, 100)

    Private signupCard As Panel

    ' Field controls, kept as members so validation can reach them.
    Private txtFullName As TextBox
    Private txtUsername As TextBox
    Private txtPassword As TextBox
    Private txtConfirm As TextBox
    Private cboRole As ComboBox

    Private lblErrName As Label
    Private lblErrUser As Label
    Private lblErrPass As Label
    Private lblErrConfirm As Label

    Private pnlName As Panel
    Private pnlUser As Panel
    Private pnlPass As Panel
    Private pnlConfirm As Panel

    Private btnCreate As Button
    Private meterBar As Panel
    Private lblMeter As Label

    ' Username availability is checked as the user types, but only after they
    ' pause — a query per keystroke would hammer the database.
    Private userCheckTimer As Timer
    Private _usernameAvailable As Boolean = False

    Private _submitting As Boolean = False

    Private Class RoleItem
        Public RoleId As Integer
        Public RoleName As String
        Public Overrides Function ToString() As String
            Return RoleName
        End Function
    End Class

    Private Sub SignUp_Load(sender As Object, e As EventArgs) Handles MyBase.Load
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

        BuildSignUpCard()
    End Sub

    ' Clear the form whenever it is shown again, so a previous attempt's text
    ' and error messages are never inherited by the next person at the counter.
    Protected Overrides Sub OnVisibleChanged(e As EventArgs)
        MyBase.OnVisibleChanged(e)
        If Me.Visible AndAlso txtFullName IsNot Nothing Then ResetForm()
    End Sub

    ' =========================================================================
    '  CARD
    ' =========================================================================
    Private Sub BuildSignUpCard()
        Const CARD_W As Integer = 560
        Const CARD_H As Integer = 690
        Const PAD As Integer = 44
        Dim fieldW As Integer = CARD_W - (PAD * 2)
        Dim halfW As Integer = (fieldW - 16) \ 2

        signupCard = New Panel With {
            .Size = New Size(CARD_W, CARD_H),
            .BackColor = CardBg
        }
        CenterPanel(signupCard)
        Me.Controls.Add(signupCard)

        AddHandler signupCard.Paint,
            Sub(s As Object, pe As PaintEventArgs)
                Dim g = pe.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect = signupCard.ClientRectangle
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

        AddHandler Me.Resize, Sub() CenterPanel(signupCard)

        Dim curY As Integer = 30

        ' ── HEADER: logo left, wordmark beside it ────────────────────────────
        Dim logoBox As New PictureBox With {
            .Size = New Size(46, 46),
            .SizeMode = PictureBoxSizeMode.Zoom,
            .BackColor = TileBg,
            .Location = New Point(PAD, curY)
        }
        Dim lp As New GraphicsPath()
        lp.AddEllipse(0, 0, 46, 46)
        logoBox.Region = New Region(lp)
        If IO.File.Exists("paint.png") Then logoBox.Image = Image.FromFile("paint.png")
        signupCard.Controls.Add(logoBox)

        signupCard.Controls.Add(New Label With {
            .Text = "Rainbow Paint Store",
            .Font = New Font("Segoe UI", 14, FontStyle.Bold),
            .ForeColor = MintDark,
            .AutoSize = True,
            .BackColor = Color.Transparent,
            .Location = New Point(PAD + 58, curY + 2)
        })
        signupCard.Controls.Add(New Label With {
            .Text = "Request a staff account",
            .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = SubText,
            .AutoSize = True,
            .BackColor = Color.Transparent,
            .Location = New Point(PAD + 58, curY + 26)
        })
        curY += 62

        ' ── APPROVAL NOTICE ──────────────────────────────────────────────────
        ' Sets the expectation up front: this does not create a usable login.
        Dim notice As New Panel With {
            .Size = New Size(fieldW, 46),
            .Location = New Point(PAD, curY),
            .BackColor = TileBg
        }
        AddHandler notice.Paint,
            Sub(s As Object, pe As PaintEventArgs)
                Dim g = pe.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect = notice.ClientRectangle
                rect.Inflate(-1, -1)
                Using path As GraphicsPath = Rounded(rect, 10)
                    Using fill As New SolidBrush(TileBg)
                        g.FillPath(fill, path)
                    End Using
                End Using
            End Sub
        notice.Controls.Add(New Label With {
            .Text = "An administrator reviews every request before the account can sign in.",
            .Font = New Font("Segoe UI", 9),
            .ForeColor = MintDark,
            .AutoSize = True,
            .BackColor = Color.Transparent,
            .Location = New Point(16, 14)
        })
        signupCard.Controls.Add(notice)
        curY += 60

        ' ── GROUP 1: YOUR DETAILS ────────────────────────────────────────────
        curY = AddGroupHeader("YOUR DETAILS", PAD, curY, fieldW)

        txtFullName = New TextBox()
        pnlName = MakeField("e.g. Juan Dela Cruz", PAD, curY + 20, fieldW, txtFullName, False)
        AddLabel("Full name", PAD, curY)
        signupCard.Controls.Add(pnlName)
        lblErrName = AddErrorLabel(PAD, curY + 68)
        curY += 90

        ' Role + a hint about what it means, side by side.
        AddLabel("Role you are applying for", PAD, curY)
        cboRole = New ComboBox With {
            .DropDownStyle = ComboBoxStyle.DropDownList,
            .Font = New Font("Segoe UI", 10.5F),
            .Size = New Size(halfW, 30),
            .Location = New Point(PAD + 12, curY + 28),
            .FlatStyle = FlatStyle.Flat,
            .BackColor = TileBg,
            .ForeColor = MintDark
        }
        Dim roleHost As Panel = MakeShell(PAD, curY + 20, halfW + 24, 46)
        roleHost.Controls.Add(cboRole)
        cboRole.Location = New Point(12, 8)
        cboRole.Width = halfW
        signupCard.Controls.Add(roleHost)

        Dim lblRoleHint As New Label With {
            .Text = "The administrator may assign" & vbCrLf & "a different role on approval.",
            .Font = New Font("Segoe UI", 8.5F),
            .ForeColor = FaintText,
            .AutoSize = True,
            .BackColor = Color.Transparent,
            .Location = New Point(PAD + halfW + 40, curY + 26)
        }
        signupCard.Controls.Add(lblRoleHint)
        LoadRoles()
        curY += 84

        ' ── GROUP 2: SIGN-IN ─────────────────────────────────────────────────
        curY = AddGroupHeader("SIGN-IN DETAILS", PAD, curY, fieldW)

        txtUsername = New TextBox()
        AddLabel("Username", PAD, curY)
        pnlUser = MakeField("Choose a username", PAD, curY + 20, fieldW, txtUsername, False)
        signupCard.Controls.Add(pnlUser)
        lblErrUser = AddErrorLabel(PAD, curY + 68)
        curY += 90

        ' Password and confirm sit side by side — they are one decision.
        txtPassword = New TextBox()
        AddLabel("Password", PAD, curY)
        pnlPass = MakeField("At least 8 characters", PAD, curY + 20, halfW, txtPassword, True)
        signupCard.Controls.Add(pnlPass)

        txtConfirm = New TextBox()
        AddLabel("Confirm password", PAD + halfW + 16, curY)
        pnlConfirm = MakeField("Re-enter password", PAD + halfW + 16, curY + 20, halfW, txtConfirm, True)
        signupCard.Controls.Add(pnlConfirm)

        lblErrPass = AddErrorLabel(PAD, curY + 68)
        lblErrPass.MaximumSize = New Size(halfW, 0)
        lblErrConfirm = AddErrorLabel(PAD + halfW + 16, curY + 68)
        lblErrConfirm.MaximumSize = New Size(halfW, 0)
        curY += 74

        ' ── STRENGTH METER ───────────────────────────────────────────────────
        meterBar = New Panel With {
            .Size = New Size(halfW, 5),
            .Location = New Point(PAD, curY),
            .BackColor = Color.Transparent
        }
        AddHandler meterBar.Paint, AddressOf PaintMeter
        signupCard.Controls.Add(meterBar)

        lblMeter = New Label With {
            .Text = "",
            .Font = New Font("Segoe UI", 8.5F, FontStyle.Bold),
            .ForeColor = FaintText,
            .AutoSize = True,
            .BackColor = Color.Transparent,
            .Location = New Point(PAD + halfW + 16, curY - 4)
        }
        signupCard.Controls.Add(lblMeter)
        curY += 26

        ' ── SUBMIT ───────────────────────────────────────────────────────────
        btnCreate = New Button With {
            .Text = "Submit request",
            .Size = New Size(fieldW, 46),
            .Location = New Point(PAD, curY),
            .FlatStyle = FlatStyle.Flat,
            .ForeColor = Color.White,
            .Font = New Font("Segoe UI", 11, FontStyle.Bold),
            .Cursor = Cursors.Hand,
            .BackColor = Color.Transparent,
            .Enabled = False
        }
        btnCreate.FlatAppearance.BorderSize = 0
        AddHandler btnCreate.Paint,
            Sub(s As Object, pe As PaintEventArgs)
                Dim btn = CType(s, Button)
                Dim rect = btn.ClientRectangle
                pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Using path As GraphicsPath = Rounded(rect, 12)
                    If Not btn.Enabled Then
                        Using br As New SolidBrush(Color.FromArgb(206, 218, 214))
                            pe.Graphics.FillPath(br, path)
                        End Using
                    Else
                        Dim isHover = btn.ClientRectangle.Contains(btn.PointToClient(Cursor.Position))
                        Dim c1 = If(isHover, Color.FromArgb(0, 140, 118), MintPrimary)
                        Dim c2 = If(isHover, MintPrimary, Color.FromArgb(0, 140, 118))
                        Using br As New LinearGradientBrush(rect, c1, c2, LinearGradientMode.Horizontal)
                            pe.Graphics.FillPath(br, path)
                        End Using
                    End If
                End Using
                TextRenderer.DrawText(pe.Graphics, btn.Text, btn.Font, rect,
                    If(btn.Enabled, Color.White, Color.FromArgb(245, 250, 248)),
                    TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub
        AddHandler btnCreate.MouseEnter, Sub() btnCreate.Invalidate()
        AddHandler btnCreate.MouseLeave, Sub() btnCreate.Invalidate()
        AddHandler btnCreate.Click, AddressOf SubmitRequest
        signupCard.Controls.Add(btnCreate)
        curY += 56

        ' ── BACK TO LOGIN ────────────────────────────────────────────────────
        Dim pnlBack As New FlowLayoutPanel With {
            .AutoSize = True,
            .BackColor = Color.Transparent,
            .FlowDirection = FlowDirection.LeftToRight
        }
        Dim lblAlready As New Label With {
            .Text = "Already have an account?",
            .Font = New Font("Segoe UI", 9),
            .ForeColor = SubText,
            .AutoSize = True,
            .BackColor = Color.Transparent,
            .Margin = New Padding(0, 3, 0, 0)
        }
        Dim lnkLogin As New LinkLabel With {
            .Text = " Log In",
            .Font = New Font("Segoe UI", 9, FontStyle.Bold),
            .AutoSize = True,
            .BackColor = Color.Transparent,
            .Margin = New Padding(0, 3, 0, 0)
        }
        lnkLogin.LinkColor = MintPrimary
        lnkLogin.ActiveLinkColor = MintDark
        AddHandler lnkLogin.LinkClicked, Sub() BackToLogin()
        pnlBack.Controls.AddRange({lblAlready, lnkLogin})
        signupCard.Controls.Add(pnlBack)
        pnlBack.Location = New Point((signupCard.Width - pnlBack.PreferredSize.Width) \ 2, curY)

        ' ── CLOSE ────────────────────────────────────────────────────────────
        Dim btnClose As New Button With {
            .Text = "×",
            .Size = New Size(28, 28),
            .Location = New Point(signupCard.Width - 36, 8),
            .FlatStyle = FlatStyle.Flat,
            .ForeColor = SubText,
            .Font = New Font("Segoe UI", 13),
            .BackColor = Color.Transparent,
            .Cursor = Cursors.Hand
        }
        btnClose.FlatAppearance.BorderSize = 0
        AddHandler btnClose.Click, Sub() BackToLogin()
        signupCard.Controls.Add(btnClose)
        btnClose.BringToFront()

        ' ── VALIDATION WIRING ────────────────────────────────────────────────
        userCheckTimer = New Timer With {.Interval = 450}
        AddHandler userCheckTimer.Tick, AddressOf CheckUsernameAvailability

        AddHandler txtFullName.TextChanged, Sub() Revalidate(False)
        AddHandler txtFullName.Leave, Sub() Revalidate(True)

        AddHandler txtUsername.TextChanged,
            Sub()
                _usernameAvailable = False
                userCheckTimer.Stop()
                userCheckTimer.Start()
                Revalidate(False)
            End Sub
        AddHandler txtUsername.Leave, Sub() Revalidate(True)

        AddHandler txtPassword.TextChanged,
            Sub()
                meterBar.Invalidate()
                UpdateMeterLabel()
                Revalidate(False)
            End Sub
        AddHandler txtPassword.Leave, Sub() Revalidate(True)
        AddHandler txtConfirm.TextChanged, Sub() Revalidate(False)
        AddHandler txtConfirm.Leave, Sub() Revalidate(True)
        AddHandler cboRole.SelectedIndexChanged, Sub() Revalidate(False)
    End Sub

    ' =========================================================================
    '  SMALL BUILDERS
    ' =========================================================================

    ' Section caption with a hairline running to the right of the words.
    Private Function AddGroupHeader(text As String, x As Integer, y As Integer, w As Integer) As Integer
        Dim lbl As New Label With {
            .Text = text,
            .Font = New Font("Segoe UI", 8.5F, FontStyle.Bold),
            .ForeColor = FaintText,
            .AutoSize = True,
            .BackColor = Color.Transparent,
            .Location = New Point(x, y)
        }
        signupCard.Controls.Add(lbl)

        Dim rule As New Panel With {
            .Height = 1,
            .BackColor = CardBorder,
            .Location = New Point(x + lbl.PreferredWidth + 10, y + 7),
            .Width = w - lbl.PreferredWidth - 10
        }
        signupCard.Controls.Add(rule)
        Return y + 24
    End Function

    Private Sub AddLabel(text As String, x As Integer, y As Integer)
        signupCard.Controls.Add(New Label With {
            .Text = text,
            .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold),
            .ForeColor = SubText,
            .AutoSize = True,
            .BackColor = Color.Transparent,
            .Location = New Point(x, y)
        })
    End Sub

    Private Function AddErrorLabel(x As Integer, y As Integer) As Label
        Dim l As New Label With {
            .Text = "",
            .Font = New Font("Segoe UI", 8.5F),
            .ForeColor = ErrClr,
            .AutoSize = True,
            .BackColor = Color.Transparent,
            .Location = New Point(x + 2, y)
        }
        signupCard.Controls.Add(l)
        Return l
    End Function

    ' A rounded shell with no textbox — used to host the role combo so it
    ' matches the height and border of the real fields.
    Private Function MakeShell(x As Integer, y As Integer, w As Integer, h As Integer) As Panel
        Dim pnl As New Panel With {
            .Size = New Size(w, h),
            .Location = New Point(x, y),
            .BackColor = TileBg
        }
        AddHandler pnl.Paint,
            Sub(s As Object, pe As PaintEventArgs)
                Dim g = pe.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect = pnl.ClientRectangle
                rect.Inflate(-1, -1)
                Using path As GraphicsPath = Rounded(rect, 10)
                    Using fill As New SolidBrush(TileBg)
                        g.FillPath(fill, path)
                    End Using
                    Using pen As New Pen(CardBorder, 1.2F)
                        g.DrawPath(pen, path)
                    End Using
                End Using
            End Sub
        Return pnl
    End Function

    ' =========================================================================
    '  FIELD FACTORY
    '
    '  The old version toggled UseSystemPasswordChar on focus so the grey
    '  placeholder stayed readable, which meant a password typed without ever
    '  losing focus could still be shown in clear text. Here the placeholder is
    '  drawn by the panel instead of living in .Text, so the masking flag is set
    '  once and never changes, and .Text is always the real value.
    ' =========================================================================
    Private Function MakeField(placeholder As String, x As Integer, y As Integer,
                               w As Integer, ByRef txtRef As TextBox,
                               isPass As Boolean) As Panel
        Dim pnl As New Panel With {
            .Size = New Size(w, 46),
            .Location = New Point(x, y),
            .BackColor = TileBg
        }

        Dim txt As TextBox = txtRef
        txt.BorderStyle = BorderStyle.None
        txt.BackColor = TileBg
        txt.ForeColor = MintDark
        txt.Location = New Point(14, 14)
        txt.Width = w - (If(isPass, 56, 28))
        txt.Font = New Font("Segoe UI", 10.5F)
        txt.UseSystemPasswordChar = isPass

        pnl.Controls.Add(txt)

        ' Eye toggle, password fields only.
        If isPass Then
            Dim btnEye As New Button With {
                .Size = New Size(28, 28),
                .Location = New Point(w - 38, 9),
                .FlatStyle = FlatStyle.Flat,
                .BackColor = Color.Transparent,
                .ForeColor = SubText,
                .Font = New Font("Segoe UI", 10),
                .Cursor = Cursors.Hand,
                .Text = "👁",
                .TabStop = False
            }
            btnEye.FlatAppearance.BorderSize = 0
            AddHandler btnEye.Click,
                Sub()
                    txt.UseSystemPasswordChar = Not txt.UseSystemPasswordChar
                    btnEye.ForeColor = If(txt.UseSystemPasswordChar, SubText, MintPrimary)
                End Sub
            pnl.Controls.Add(btnEye)
            btnEye.BringToFront()
        End If

        AddHandler txt.GotFocus, Sub() pnl.Invalidate()
        AddHandler txt.LostFocus, Sub() pnl.Invalidate()
        AddHandler txt.TextChanged, Sub() pnl.Invalidate()

        AddHandler pnl.Paint,
            Sub(s As Object, pe As PaintEventArgs)
                Dim g = pe.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect = pnl.ClientRectangle
                rect.Inflate(-1, -1)
                Using path As GraphicsPath = Rounded(rect, 10)
                    Using fill As New SolidBrush(TileBg)
                        g.FillPath(fill, path)
                    End Using
                    ' Tag carries the invalid flag set by Revalidate.
                    Dim bad As Boolean = (TypeOf pnl.Tag Is Boolean AndAlso CBool(pnl.Tag))
                    Dim focused = txt.Focused
                    Dim clr = If(bad, ErrClr, If(focused, MintPrimary, CardBorder))
                    Using pen As New Pen(clr, If(focused OrElse bad, 1.8F, 1.2F))
                        g.DrawPath(pen, path)
                    End Using
                End Using

                ' Placeholder painted behind an empty box, so .Text stays clean.
                If txt.Text = "" AndAlso Not txt.Focused Then
                    TextRenderer.DrawText(g, placeholder, txt.Font,
                        New Rectangle(txt.Left, txt.Top - 1, txt.Width, txt.Height + 2),
                        FaintText, TextFormatFlags.Left Or TextFormatFlags.VerticalCenter)
                End If
            End Sub

        Return pnl
    End Function

    ' =========================================================================
    '  ROLES
    ' =========================================================================
    Private Sub LoadRoles()
        cboRole.Items.Clear()
        Try
            Using cn = Db.GetConnection()
                cn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT role_id, role_name FROM roles WHERE role_id <> @admin ORDER BY role_id", cn)
                    cmd.Parameters.AddWithValue("@admin", AccountRequests.ADMIN_ROLE_ID)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            cboRole.Items.Add(New RoleItem With {
                                .RoleId = Convert.ToInt32(rdr("role_id")),
                                .RoleName = rdr("role_name").ToString()
                            })
                        End While
                    End Using
                End Using
            End Using
        Catch
            ' Leave the combo empty; Revalidate keeps submit disabled and the
            ' role error explains it, rather than a modal on a form that is
            ' merely being displayed.
        End Try
        If cboRole.Items.Count > 0 Then cboRole.SelectedIndex = 0
    End Sub

    ' =========================================================================
    '  VALIDATION
    '
    '  showAll:=False validates quietly while typing — it updates the submit
    '  button but only clears messages, never introduces one, so an error does
    '  not appear under a field the user is still halfway through.
    '  showAll:=True (on blur, and on submit) shows every reason.
    ' =========================================================================
    Private Function Revalidate(showAll As Boolean) As Boolean
        Dim ok As Boolean = True

        ' Full name
        Dim nameVal As String = txtFullName.Text.Trim()
        Dim nameErr As String = ""
        If nameVal = "" Then
            nameErr = "Full name is required."
        ElseIf nameVal.Length < 2 Then
            nameErr = "Please enter your full name."
        ElseIf nameVal.Length > 100 Then
            nameErr = "Use 100 characters or fewer."
        End If
        If nameErr <> "" Then ok = False
        SetFieldError(pnlName, lblErrName, nameErr, showAll OrElse nameVal <> "")

        ' Username — format first, then availability.
        Dim userVal As String = txtUsername.Text.Trim()
        Dim userErr As String = AccountRequests.ValidateUsername(userVal)
        If userErr = "" AndAlso Not _usernameAvailable Then
            ' Not yet confirmed free. Not an error while the check is pending;
            ' it just holds the submit button.
            ok = False
        End If
        If userErr <> "" Then ok = False
        SetFieldError(pnlUser, lblErrUser, userErr, showAll OrElse userVal <> "")
        If userErr = "" AndAlso _usernameAvailable AndAlso userVal <> "" Then
            lblErrUser.ForeColor = OkClr
            lblErrUser.Text = "Username is available."
        End If

        ' Password
        Dim passVal As String = txtPassword.Text
        Dim passErr As String = AccountRequests.ValidatePassword(passVal)
        If passErr <> "" Then ok = False
        SetFieldError(pnlPass, lblErrPass, passErr, showAll OrElse passVal <> "")

        ' Confirm
        Dim confVal As String = txtConfirm.Text
        Dim confErr As String = ""
        If confVal = "" Then
            confErr = "Please confirm."
        ElseIf confVal <> passVal Then
            confErr = "Passwords do not match."
        End If
        If confErr <> "" Then ok = False
        SetFieldError(pnlConfirm, lblErrConfirm, confErr, showAll OrElse confVal <> "")

        ' Role
        If cboRole.SelectedItem Is Nothing Then ok = False

        btnCreate.Enabled = ok AndAlso Not _submitting
        btnCreate.Invalidate()
        Return ok
    End Function

    ' Shows or hides one field's error and red border.
    Private Sub SetFieldError(pnl As Panel, lbl As Label, message As String, visible As Boolean)
        Dim bad As Boolean = (message <> "") AndAlso visible
        pnl.Tag = bad
        lbl.ForeColor = ErrClr
        lbl.Text = If(bad, message, "")
        pnl.Invalidate()
    End Sub

    ' =========================================================================
    '  USERNAME AVAILABILITY
    ' =========================================================================
    Private Sub CheckUsernameAvailability(sender As Object, e As EventArgs)
        userCheckTimer.Stop()

        Dim u As String = txtUsername.Text.Trim()
        If AccountRequests.ValidateUsername(u) <> "" Then
            _usernameAvailable = False
            Revalidate(False)
            Return
        End If

        _usernameAvailable = Not AccountRequests.UsernameTaken(u)
        If Not _usernameAvailable Then
            SetFieldError(pnlUser, lblErrUser, "That username is already taken.", True)
            btnCreate.Enabled = False
            btnCreate.Invalidate()
        Else
            Revalidate(False)
        End If
    End Sub

    ' =========================================================================
    '  STRENGTH METER
    ' =========================================================================
    Private Sub PaintMeter(sender As Object, e As PaintEventArgs)
        Dim g = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim score As Integer = AccountRequests.PasswordStrength(txtPassword.Text)
        Dim segW As Integer = (meterBar.Width - 18) \ 4

        For i As Integer = 0 To 3
            Dim filled As Boolean = (i < score)
            Dim clr As Color
            If Not filled Then
                clr = Color.FromArgb(226, 235, 232)
            ElseIf score <= 1 Then
                clr = Color.FromArgb(214, 90, 70)
            ElseIf score = 2 Then
                clr = Color.FromArgb(226, 160, 60)
            ElseIf score = 3 Then
                clr = Color.FromArgb(120, 180, 90)
            Else
                clr = OkClr
            End If
            Using br As New SolidBrush(clr)
                Using p As GraphicsPath = Rounded(
                    New Rectangle(i * (segW + 6), 0, segW, 5), 3)
                    g.FillPath(br, p)
                End Using
            End Using
        Next
    End Sub

    Private Sub UpdateMeterLabel()
        If txtPassword.Text = "" Then
            lblMeter.Text = ""
            Return
        End If
        Select Case AccountRequests.PasswordStrength(txtPassword.Text)
            Case 0, 1 : lblMeter.Text = "Weak" : lblMeter.ForeColor = Color.FromArgb(214, 90, 70)
            Case 2 : lblMeter.Text = "Fair" : lblMeter.ForeColor = Color.FromArgb(200, 140, 40)
            Case 3 : lblMeter.Text = "Good" : lblMeter.ForeColor = Color.FromArgb(90, 150, 70)
            Case Else : lblMeter.Text = "Strong" : lblMeter.ForeColor = OkClr
        End Select
    End Sub

    ' =========================================================================
    '  SUBMIT
    ' =========================================================================
    Private Sub SubmitRequest(sender As Object, e As EventArgs)
        If _submitting Then Return

        ' Re-run the availability check synchronously: the debounce timer may
        ' not have fired if the user typed and clicked straight away.
        userCheckTimer.Stop()
        Dim uname As String = txtUsername.Text.Trim()
        If AccountRequests.ValidateUsername(uname) = "" Then
            _usernameAvailable = Not AccountRequests.UsernameTaken(uname)
            If Not _usernameAvailable Then
                SetFieldError(pnlUser, lblErrUser, "That username is already taken.", True)
                txtUsername.Focus()
                Return
            End If
        End If

        If Not Revalidate(True) Then
            AppModal.Show("Please correct the highlighted fields.", "Sign Up",
                          MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        _submitting = True
        btnCreate.Enabled = False
        btnCreate.Text = "Submitting…"
        btnCreate.Invalidate()
        Application.DoEvents()

        Dim role = CType(cboRole.SelectedItem, RoleItem)
        Dim err As String = AccountRequests.SubmitSignUp(
            txtFullName.Text.Trim(), uname, txtPassword.Text, role.RoleId)

        _submitting = False
        btnCreate.Text = "Submit request"
        btnCreate.Enabled = True
        btnCreate.Invalidate()

        If err <> "" Then
            AppModal.Show(err, "Sign Up", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End If

        Dim msg As String
        If AccountRequests.HasApprovalColumns() Then
            msg = "Your request has been submitted." & vbCrLf & vbCrLf &
                  "An administrator will review it shortly. You will be able to " &
                  "sign in once your account is approved."
        Else
            msg = "Your request has been submitted." & vbCrLf & vbCrLf &
                  "An administrator must enable your account before you can sign in."
        End If
        AppModal.Show(msg, "Request Submitted", MessageBoxButtons.OK, MessageBoxIcon.Information)

        ResetForm()
        BackToLogin()
    End Sub

    ' =========================================================================
    '  HELPERS
    ' =========================================================================
    Private Sub ResetForm()
        _submitting = False
        _usernameAvailable = False
        If userCheckTimer IsNot Nothing Then userCheckTimer.Stop()

        txtFullName.Text = ""
        txtUsername.Text = ""
        txtPassword.Text = ""
        txtConfirm.Text = ""
        If cboRole.Items.Count > 0 Then cboRole.SelectedIndex = 0

        For Each pair In {(pnlName, lblErrName), (pnlUser, lblErrUser),
                          (pnlPass, lblErrPass), (pnlConfirm, lblErrConfirm)}
            SetFieldError(pair.Item1, pair.Item2, "", False)
        Next

        lblMeter.Text = ""
        If meterBar IsNot Nothing Then meterBar.Invalidate()
        btnCreate.Enabled = False
        btnCreate.Invalidate()
    End Sub

    Private Sub BackToLogin()
        _loginForm.Show()
        Me.Hide()
    End Sub

    Private Shared Function Rounded(rect As Rectangle, radius As Integer) As GraphicsPath
        Dim r As Integer = radius
        Dim path As New GraphicsPath()
        path.AddArc(rect.X, rect.Y, r, r, 180, 90)
        path.AddArc(rect.Right - r, rect.Y, r, r, 270, 90)
        path.AddArc(rect.Right - r, rect.Bottom - r, r, r, 0, 90)
        path.AddArc(rect.X, rect.Bottom - r, r, r, 90, 90)
        path.CloseFigure()
        Return path
    End Function

    Private Sub CenterPanel(pnl As Panel)
        pnl.Left = (Me.ClientSize.Width - pnl.Width) \ 2
        pnl.Top = (Me.ClientSize.Height - pnl.Height) \ 2
    End Sub

    Private Sub InitializeComponent()
        Me.SuspendLayout()
        Me.ClientSize = New System.Drawing.Size(1280, 720)
        Me.Name = "SignUp"
        Me.Text = "Sign Up"
        Me.ResumeLayout(False)
    End Sub

End Class
