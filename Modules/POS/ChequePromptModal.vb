Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms

' =============================================================================
'  ChequePromptModal - what the till asks when the customer pays by cheque.
'
'  A cheque is only traceable by the account it is drawn on and its number,
'  so both are required before the sale can be confirmed. The check number is
'  stored in sales.reference_no (shown by every report that lists references)
'  and the account name in sales.cheque_account_name (migrations.sql 17).
' =============================================================================
Public Module ChequePromptModal

    Public Class Result
        Public AccountName As String = ""
        Public ChequeNo As String = ""
    End Class

    Private ReadOnly CardBg As Color = Color.White
    Private ReadOnly TitleClr As Color = Color.FromArgb(25, 40, 35)
    Private ReadOnly SubText As Color = Theme.SubText
    Private ReadOnly BorderClr As Color = Color.FromArgb(160, 195, 185)
    Private ReadOnly DividerClr As Color = Color.FromArgb(230, 238, 235)
    Private ReadOnly ErrClr As Color = Color.FromArgb(200, 65, 65)

    Private Const CARD_W As Integer = 420
    Private Const PAD As Integer = 24

    ' Returns Nothing when the cashier cancels.
    Public Function Show(methodName As String, grandTotal As Decimal) As Result
        Dim out As Result = Nothing

        Dim f As New Form With {
            .FormBorderStyle = FormBorderStyle.None,
            .StartPosition = FormStartPosition.CenterParent,
            .BackColor = CardBg,
            .ShowInTaskbar = False,
            .Width = CARD_W,
            .KeyPreview = True
        }

        Dim lblTitle As New Label With {
            .Text = methodName & " Payment",
            .Font = New Font("Segoe UI Semibold", 13.0F), .ForeColor = TitleClr,
            .AutoSize = True, .Location = New Point(PAD, PAD), .BackColor = Color.Transparent
        }
        Dim lblTotal As New Label With {
            .Text = "Grand Total:  P " & grandTotal.ToString("N2") &
                    "   -   the cheque must be for this amount.",
            .Font = New Font("Segoe UI", 9.5F), .ForeColor = SubText,
            .AutoSize = True, .Location = New Point(PAD, lblTitle.Bottom + 6),
            .BackColor = Color.Transparent
        }

        Dim lblAcct As New Label With {
            .Text = "Account name (as printed on the cheque)",
            .Font = New Font("Segoe UI Semibold", 9.0F), .ForeColor = SubText,
            .AutoSize = True, .Location = New Point(PAD, lblTotal.Bottom + 18),
            .BackColor = Color.Transparent
        }
        f.Controls.AddRange({lblTitle, lblTotal, lblAcct})
        Dim txtAcct As TextBox = PaymentMethodModal.MakeField(PAD, lblAcct.Bottom + 4, CARD_W - PAD * 2, f, "")
        txtAcct.MaxLength = 120

        Dim lblNo As New Label With {
            .Text = "Check number",
            .Font = New Font("Segoe UI Semibold", 9.0F), .ForeColor = SubText,
            .AutoSize = True, .Location = New Point(PAD, txtAcct.Parent.Bottom + 12),
            .BackColor = Color.Transparent
        }
        f.Controls.Add(lblNo)
        Dim txtNo As TextBox = PaymentMethodModal.MakeField(PAD, lblNo.Bottom + 4, CARD_W - PAD * 2, f, "")
        txtNo.MaxLength = 40

        Dim lblErr As New Label With {
            .Text = "", .Font = New Font("Segoe UI", 9.0F), .ForeColor = ErrClr,
            .AutoSize = False, .Size = New Size(CARD_W - PAD * 2, 20),
            .Location = New Point(PAD, txtNo.Parent.Bottom + 8), .BackColor = Color.Transparent
        }
        f.Controls.Add(lblErr)

        Dim dividerY As Integer = lblErr.Bottom + 8
        Dim btnY As Integer = dividerY + 16
        Dim cardH As Integer = btnY + 38 + PAD
        Dim btnOk As Button = PaymentMethodModal.MakeButton("Continue", True,
            New Point(CARD_W - PAD - 120, btnY), New Size(120, 38))
        Dim btnCancel As Button = PaymentMethodModal.MakeButton("Cancel", False,
            New Point(CARD_W - PAD - 120 - 8 - 96, btnY), New Size(96, 38))
        f.Controls.AddRange({btnOk, btnCancel})

        AddHandler btnCancel.Click, Sub() f.Close()
        AddHandler btnOk.Click,
            Sub()
                Dim acct As String = txtAcct.Text.Trim()
                Dim num As String = txtNo.Text.Trim()
                If acct = "" Then
                    lblErr.Text = "Enter the account name on the cheque."
                    txtAcct.Focus()
                    Return
                End If
                If num = "" Then
                    lblErr.Text = "Enter the check number."
                    txtNo.Focus()
                    Return
                End If
                out = New Result With {.AccountName = acct, .ChequeNo = num}
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
        AddHandler f.Shown, Sub() txtAcct.Focus()

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
