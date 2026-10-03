Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms

' =============================================================================
'  AdminApprovalModal - "an Admin has to OK this, and you have to say why".
'
'  A cashier voiding their own completed sale is the classic way cash goes
'  missing: ring it up, keep the money, void it, and the stock comes back.
'  So a void now needs a written reason AND an Admin typing their own
'  username and password at the till. Both end up in the activity log.
' =============================================================================
Public Module AdminApprovalModal

    Public Class Result
        Public Reason As String = ""
        Public AdminId As Integer
        Public AdminName As String = ""
    End Class

    Private ReadOnly CardBg As Color = Color.White
    Private ReadOnly TitleClr As Color = Color.FromArgb(25, 40, 35)
    Private ReadOnly SubText As Color = Theme.SubText
    Private ReadOnly BorderClr As Color = Color.FromArgb(160, 195, 185)
    Private ReadOnly DividerClr As Color = Color.FromArgb(230, 238, 235)
    Private ReadOnly ErrClr As Color = Color.FromArgb(200, 65, 65)

    Private Const CARD_W As Integer = 440
    Private Const PAD As Integer = 24
    Private Const MinReason As Integer = 5

    ' Returns Nothing when cancelled.
    Public Function Show(title As String, intro As String, actionLabel As String) As Result
        Dim out As Result = Nothing

        Dim f As New Form With {
            .FormBorderStyle = FormBorderStyle.None,
            .StartPosition = FormStartPosition.CenterParent,
            .BackColor = CardBg, .ShowInTaskbar = False,
            .Width = CARD_W, .KeyPreview = True
        }

        Dim lblTitle As New Label With {
            .Text = title, .Font = New Font("Segoe UI Semibold", 13.0F), .ForeColor = TitleClr,
            .AutoSize = True, .Location = New Point(PAD, PAD), .BackColor = Color.Transparent
        }
        Dim lblIntro As New Label With {
            .Text = intro, .Font = New Font("Segoe UI", 9.5F), .ForeColor = SubText,
            .AutoSize = False, .Size = New Size(CARD_W - PAD * 2, 40),
            .Location = New Point(PAD, lblTitle.Bottom + 6), .BackColor = Color.Transparent
        }
        f.Controls.AddRange({lblTitle, lblIntro})

        Dim y As Integer = lblIntro.Bottom + 10
        Dim addLabel = Function(text As String, top As Integer) As Label
                           Dim l As New Label With {
                               .Text = text, .Font = New Font("Segoe UI Semibold", 9.0F), .ForeColor = SubText,
                               .AutoSize = True, .Location = New Point(PAD, top), .BackColor = Color.Transparent}
                           f.Controls.Add(l)
                           Return l
                       End Function

        Dim lblReason = addLabel("Reason (required)", y)
        Dim txtReason As TextBox = PaymentMethodModal.MakeField(PAD, lblReason.Bottom + 4, CARD_W - PAD * 2, f, "")
        txtReason.MaxLength = 200

        Dim lblUser = addLabel("Admin username", txtReason.Parent.Bottom + 12)
        Dim txtUser As TextBox = PaymentMethodModal.MakeField(PAD, lblUser.Bottom + 4, CARD_W - PAD * 2, f, "")
        txtUser.MaxLength = 50

        Dim lblPass = addLabel("Admin password", txtUser.Parent.Bottom + 12)
        Dim txtPass As TextBox = PaymentMethodModal.MakeField(PAD, lblPass.Bottom + 4, CARD_W - PAD * 2, f, "")
        txtPass.UseSystemPasswordChar = True

        Dim lblErr As New Label With {
            .Text = "", .Font = New Font("Segoe UI", 9.0F), .ForeColor = ErrClr,
            .AutoSize = False, .Size = New Size(CARD_W - PAD * 2, 36),
            .Location = New Point(PAD, txtPass.Parent.Bottom + 8), .BackColor = Color.Transparent
        }
        f.Controls.Add(lblErr)

        Dim dividerY As Integer = lblErr.Bottom + 6
        Dim btnY As Integer = dividerY + 16
        Dim cardH As Integer = btnY + 38 + PAD
        Dim btnOk As Button = PaymentMethodModal.MakeButton(actionLabel, True,
            New Point(CARD_W - PAD - 140, btnY), New Size(140, 38))
        Dim btnCancel As Button = PaymentMethodModal.MakeButton("Cancel", False,
            New Point(CARD_W - PAD - 140 - 8 - 96, btnY), New Size(96, 38))
        f.Controls.AddRange({btnOk, btnCancel})

        AddHandler btnCancel.Click, Sub() f.Close()
        AddHandler btnOk.Click,
            Sub()
                Dim reason As String = txtReason.Text.Trim()
                If reason.Length < MinReason Then
                    lblErr.Text = "Type a short reason (at least " & MinReason & " letters)."
                    txtReason.Focus()
                    Return
                End If
                If txtUser.Text.Trim() = "" OrElse txtPass.Text = "" Then
                    lblErr.Text = "An Admin must type their username and password."
                    If txtUser.Text.Trim() = "" Then txtUser.Focus() Else txtPass.Focus()
                    Return
                End If

                f.Cursor = Cursors.WaitCursor
                Dim chk = Credentials.Check(txtUser.Text, txtPass.Text, "Admin")
                f.Cursor = Cursors.Default
                If Not chk.Ok Then
                    lblErr.Text = chk.Message
                    txtPass.Clear()
                    txtPass.Focus()
                    ActivityLog.Record("APPROVAL_DENIED", "user", Nothing,
                                       title & " - admin check failed for '" & txtUser.Text.Trim() & "'")
                    Return
                End If
                out = New Result With {.Reason = reason, .AdminId = chk.UserId, .AdminName = chk.FullName}
                f.Close()
            End Sub

        f.Height = cardH
        AddHandler f.Paint,
            Sub(s As Object, e As PaintEventArgs)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Using pen As New Pen(DividerClr, 1.0F)
                    g.DrawLine(pen, PAD, dividerY, f.Width - PAD, dividerY)
                End Using
                Using path As GraphicsPath = PaymentMethodModal.RoundedPath(New Rectangle(0, 0, f.Width - 1, f.Height - 1), 10)
                    Using pen As New Pen(BorderClr, 1.2F)
                        g.DrawPath(pen, path)
                    End Using
                End Using
            End Sub
        Using gp As GraphicsPath = PaymentMethodModal.RoundedPath(New Rectangle(0, 0, CARD_W, cardH), 10)
            f.Region = New Region(gp)
        End Using

        f.AcceptButton = btnOk
        AddHandler f.KeyDown,
            Sub(s As Object, e As KeyEventArgs)
                If e.KeyCode = Keys.Escape Then f.Close()
            End Sub
        AddHandler f.Shown, Sub() txtReason.Focus()

        Try
            Dim owner As Form = Form.ActiveForm
            If owner Is Nothing OrElse owner Is f Then
                f.StartPosition = FormStartPosition.CenterScreen
                f.ShowDialog()
            Else
                f.ShowDialog(owner)
            End If
        Finally
            f.Dispose()
        End Try
        Return out
    End Function

End Module
