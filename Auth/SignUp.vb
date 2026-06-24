Imports System.Drawing.Drawing2D

Public Class SignUp
    Inherits System.Windows.Forms.Form

    Private _loginForm As LogIn

    Public Sub New(loginRef As LogIn)
        InitializeComponent()
        _loginForm = loginRef
    End Sub

    ' ===== COLORS =====
    Private MintDark As Color = Color.FromArgb(10, 79, 66)
    Private MintPrimary As Color = Color.FromArgb(0, 168, 142)
    Private MintBg As Color = Color.FromArgb(244, 248, 246)
    Private CardBg As Color = Color.White
    Private CardBorder As Color = Color.FromArgb(228, 232, 230)
    Private SubText As Color = Color.FromArgb(90, 114, 107)
    Private TileBg As Color = Color.FromArgb(236, 248, 244)

    Private signupCard As Panel

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

    Private Sub BuildSignUpCard()
        signupCard = New Panel With {
            .Size = New Size(420, 580),
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

        Dim curY As Integer = 36

        ' ===== LOGO + TITLE =====
        Dim logoFile As String = "C:\Users\DELL\source\repos\PaintStore_POS\paint.png"
        Dim logoBox As New PictureBox With {
            .Size = New Size(52, 52),
            .SizeMode = PictureBoxSizeMode.Zoom,
            .BackColor = TileBg,
            .Location = New Point((signupCard.Width - 52) \ 2, curY)
        }
        Dim lp As New GraphicsPath()
        lp.AddEllipse(0, 0, 52, 52)
        logoBox.Region = New Region(lp)
        If IO.File.Exists(logoFile) Then logoBox.Image = Image.FromFile(logoFile)
        signupCard.Controls.Add(logoBox)
        curY += 62

        Dim lblStore As New Label With {
            .Text = "Rainbow Paint Store",
            .Font = New Font("Segoe UI", 13, FontStyle.Bold),
            .ForeColor = MintDark,
            .AutoSize = True,
            .BackColor = Color.Transparent
        }
        signupCard.Controls.Add(lblStore)
        lblStore.Location = New Point((signupCard.Width - lblStore.PreferredWidth) \ 2, curY)
        curY += 26

        Dim lblSub As New Label With {
            .Text = "Create a new account",
            .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = SubText,
            .AutoSize = True,
            .BackColor = Color.Transparent
        }
        signupCard.Controls.Add(lblSub)
        lblSub.Location = New Point((signupCard.Width - lblSub.PreferredWidth) \ 2, curY)
        curY += 32

        ' ===== DIVIDER =====
        Dim div As New Panel With {
            .Height = 1, .Width = 340, .Left = 40, .Top = curY, .BackColor = CardBorder
        }
        signupCard.Controls.Add(div)
        curY += 18

        ' ===== FIELDS =====
        Dim fields() As (label As String, placeholder As String, isPass As Boolean) = {
            ("Full Name", "Enter full name", False),
            ("Username", "Choose a username", False),
            ("Password", "Create password", True),
            ("Confirm Password", "Re-enter password", True)
        }

        Dim txtBoxes(3) As TextBox
        Dim panels(3) As Panel

        For i = 0 To 3
            Dim capI = i
            Dim fd = fields(i)

            Dim lbl As New Label With {
                .Text = fd.label,
                .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold),
                .ForeColor = SubText,
                .AutoSize = True,
                .BackColor = Color.Transparent,
                .Location = New Point(40, curY)
            }
            signupCard.Controls.Add(lbl)
            curY += 22

            Dim txt As New TextBox()
            Dim pnl = MakeField(fd.placeholder, curY, txt, fd.isPass)
            txtBoxes(capI) = txt
            panels(capI) = pnl
            signupCard.Controls.Add(pnl)
            curY += 54
        Next

        ' ===== CREATE BUTTON =====
        Dim btnCreate As New Button With {
            .Text = "Create Account",
            .Size = New Size(340, 46),
            .Location = New Point(40, curY),
            .FlatStyle = FlatStyle.Flat,
            .ForeColor = Color.White,
            .Font = New Font("Segoe UI", 11, FontStyle.Bold),
            .Cursor = Cursors.Hand,
            .BackColor = Color.Transparent
        }
        btnCreate.FlatAppearance.BorderSize = 0
        AddHandler btnCreate.Paint,
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
        AddHandler btnCreate.MouseEnter, Sub() btnCreate.Invalidate()
        AddHandler btnCreate.MouseLeave, Sub() btnCreate.Invalidate()
        AddHandler btnCreate.Click,
            Sub()
                AppModal.Show("Account creation is not available yet. Please contact your administrator.",
                    "Sign Up", MessageBoxButtons.OK, MessageBoxIcon.Information)
            End Sub
        signupCard.Controls.Add(btnCreate)
        curY += 54

        ' ===== BACK TO LOGIN =====
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
        AddHandler lnkLogin.LinkClicked,
            Sub()
                _loginForm.Show()
                Me.Hide()
            End Sub
        pnlBack.Controls.AddRange({lblAlready, lnkLogin})
        signupCard.Controls.Add(pnlBack)
        pnlBack.Location = New Point((signupCard.Width - pnlBack.PreferredSize.Width) \ 2, curY)

        ' ===== CLOSE BUTTON =====
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
        AddHandler btnClose.Click, Sub() Application.Exit()
        signupCard.Controls.Add(btnClose)
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
            .Width = 300,
            .Font = New Font("Segoe UI", 10.5F)
        }
        If isPass Then txt.UseSystemPasswordChar = False
        txtRef = txt

        AddHandler txt.GotFocus,
            Sub()
                If txt.Text = placeholder Then
                    txt.Text = ""
                    txt.ForeColor = MintDark
                    If isPass Then txt.UseSystemPasswordChar = True
                End If
                pnl.Invalidate()
            End Sub

        AddHandler txt.LostFocus,
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

    ' ===== CENTER =====
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

    Private Sub SignUp_Load_1(sender As Object, e As EventArgs) Handles MyBase.Load

    End Sub
End Class