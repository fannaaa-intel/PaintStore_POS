Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms

Public Module PaymentPromptModal

    Public Enum FieldKind
        AmountTendered
        ReferenceNumber
    End Enum

    Private ReadOnly CardBg As Color = Color.White
    Private ReadOnly SubText As Color = Color.FromArgb(120, 140, 134)
    Private ReadOnly FieldBg As Color = Color.FromArgb(244, 248, 246)
    Private ReadOnly FieldBorder As Color = Color.FromArgb(210, 225, 220)
    Private ReadOnly BorderClr As Color = Color.FromArgb(160, 195, 185)

    Private Const RADIUS As Integer = 10
    Private Const CARD_W As Integer = 380
    Private Const PAD As Integer = 24

    Public Function Show(methodName As String,
                         grandTotal As Decimal,
                         kind As FieldKind,
                         Optional defaultValue As String = "") As String

        Dim normalClr As Color, darkClr As Color, title As String
        Select Case methodName
            Case "Cash"
                normalClr = Color.FromArgb(0, 148, 74) : darkClr = Color.FromArgb(0, 90, 50)
                title = "Cash Payment"
            Case "Card"
                normalClr = Color.FromArgb(30, 100, 190) : darkClr = Color.FromArgb(15, 60, 130)
                title = "Card Payment"
            Case Else
                normalClr = Color.FromArgb(100, 50, 180) : darkClr = Color.FromArgb(60, 25, 120)
                title = methodName & " Payment"
        End Select

        Dim f As New Form With {
            .FormBorderStyle = FormBorderStyle.None,
            .StartPosition = FormStartPosition.CenterParent,
            .BackColor = CardBg,
            .ShowInTaskbar = False,
            .Width = CARD_W,
            .KeyPreview = True
        }

        ' === Header (gradient, themed per method) ===
        Dim headerH As Integer = 78
        Dim header As New Panel With {.Size = New Size(CARD_W, headerH), .Location = New Point(0, 0), .BackColor = Color.Transparent}
        AddHandler header.Paint,
            Sub(s, e)
                Dim g = e.Graphics : g.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect = header.ClientRectangle
                Using path As New GraphicsPath()
                    Dim r = RADIUS
                    path.AddArc(rect.X, rect.Y, r * 2, r * 2, 180, 90)
                    path.AddArc(rect.Right - r * 2, rect.Y, r * 2, r * 2, 270, 90)
                    path.AddLine(rect.Right, rect.Bottom, rect.X, rect.Bottom)
                    path.CloseFigure()
                    Using br As New LinearGradientBrush(rect, normalClr, darkClr, 0.0F)
                        g.FillPath(br, path)
                    End Using
                End Using
                DrawMethodIcon(g, methodName, New Rectangle(PAD, headerH \ 2 - 14, 28, 28))
            End Sub

        Dim lblTitle As New Label With {
            .Text = title, .Font = New Font("Segoe UI Semibold", 13.0F),
            .ForeColor = Color.White, .BackColor = Color.Transparent, .AutoSize = False,
            .Location = New Point(PAD + 38, 16), .Size = New Size(CARD_W - PAD * 2 - 38, 22)
        }
        Dim lblGrand As New Label With {
            .Text = "Grand Total:  P " & grandTotal.ToString("N2"),
            .Font = New Font("Segoe UI", 9.5F), .ForeColor = Color.FromArgb(225, 240, 235),
            .BackColor = Color.Transparent, .AutoSize = False,
            .Location = New Point(PAD + 38, 40), .Size = New Size(CARD_W - PAD * 2 - 38, 18)
        }
        header.Controls.AddRange({lblTitle, lblGrand})

        ' === Field ===
        Dim fieldLabel As String = If(kind = FieldKind.AmountTendered, "Amount Tendered",
            If(methodName = "Card", "Card Approval / Reference No.", methodName & " Reference No."))
        Dim lblField As New Label With {
            .Text = fieldLabel, .Font = New Font("Segoe UI", 9.5F), .ForeColor = SubText,
            .AutoSize = True, .Location = New Point(PAD, headerH + 18), .BackColor = Color.Transparent
        }

        Dim fieldBoxH As Integer = 50
        Dim fieldBox As New Panel With {.Location = New Point(PAD, lblField.Bottom + 6), .Size = New Size(CARD_W - PAD * 2, fieldBoxH), .BackColor = Color.Transparent}
        Dim focused As Boolean = False
        AddHandler fieldBox.Paint,
            Sub(s, e)
                Dim g = e.Graphics : g.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect = fieldBox.ClientRectangle : rect.Inflate(-1, -1)
                Using br As New SolidBrush(FieldBg) : g.FillRoundedRectangle(br, rect, 9) : End Using
                Using p As New Pen(If(focused, normalClr, FieldBorder), If(focused, 1.8F, 1.0F)) : g.DrawRoundedRectangle(p, rect, 9) : End Using
            End Sub

        Dim txtX As Integer = 14
        If kind = FieldKind.AmountTendered Then
            Dim lblPrefix As New Label With {.Text = "P", .Font = New Font("Segoe UI Semibold", 16.0F), .ForeColor = normalClr, .BackColor = Color.Transparent, .AutoSize = True, .Location = New Point(14, 12)}
            fieldBox.Controls.Add(lblPrefix)
            txtX = 14 + lblPrefix.PreferredWidth + 8
        End If

        Dim txt As New TextBox With {
            .Font = New Font("Segoe UI Semibold", 16.0F), .BorderStyle = BorderStyle.None,
            .BackColor = FieldBg, .ForeColor = Color.FromArgb(35, 50, 46),
            .Text = defaultValue, .Location = New Point(txtX, 11), .Width = CARD_W - PAD * 2 - txtX - 14
        }
        AddHandler txt.Enter,
           Sub()
               focused = True
               fieldBox.Invalidate()
           End Sub
        AddHandler txt.Leave,
            Sub()
                focused = False
                fieldBox.Invalidate()
            End Sub
        fieldBox.Controls.Add(txt)

        ' Live change preview — Cash only. Purely visual; the actual
        ' insufficient-amount check still happens in TransactionPanelBuilder.
        Dim lblChange As Label = Nothing
        If kind = FieldKind.AmountTendered Then
            lblChange = New Label With {
                .Text = "", .Font = New Font("Segoe UI Semibold", 10.0F), .ForeColor = SubText,
                .AutoSize = False, .Location = New Point(PAD, fieldBox.Bottom + 8),
                .Size = New Size(CARD_W - PAD * 2, 20), .BackColor = Color.Transparent
            }
            Dim updateChange As Action =
                Sub()
                    Dim v As Decimal
                    If Decimal.TryParse(txt.Text.Trim(), v) Then
                        Dim diff = v - grandTotal
                        If diff >= 0 Then
                            lblChange.Text = "Change Due:  P " & diff.ToString("N2")
                            lblChange.ForeColor = Color.FromArgb(0, 130, 70)
                        Else
                            lblChange.Text = "Short by:  P " & Math.Abs(diff).ToString("N2")
                            lblChange.ForeColor = Color.FromArgb(190, 60, 60)
                        End If
                    Else
                        lblChange.Text = ""
                    End If
                End Sub
            AddHandler txt.TextChanged, Sub() updateChange()
            updateChange()
        End If

        ' === Buttons ===
        Dim btnTop As Integer = If(lblChange IsNot Nothing, lblChange.Bottom + 18, fieldBox.Bottom + 22)
        Dim btnH As Integer = 38, btnW As Integer = 104, gap As Integer = 8

        Dim btnCancel As New Button With {
            .Text = "Cancel", .Size = New Size(btnW, btnH), .FlatStyle = FlatStyle.Flat,
            .Font = New Font("Segoe UI Semibold", 9.5F), .Cursor = Cursors.Hand,
            .DialogResult = DialogResult.Cancel, .BackColor = Color.FromArgb(243, 246, 244), .ForeColor = Color.FromArgb(70, 86, 80),
            .Location = New Point(CARD_W - PAD - btnW * 2 - gap, btnTop)
        }
        btnCancel.FlatAppearance.BorderSize = 0
        Dim btnOk As New Button With {
            .Text = "Confirm", .Size = New Size(btnW, btnH), .FlatStyle = FlatStyle.Flat,
            .Font = New Font("Segoe UI Semibold", 9.5F), .Cursor = Cursors.Hand,
            .DialogResult = DialogResult.OK, .ForeColor = Color.White,
            .Location = New Point(CARD_W - PAD - btnW, btnTop)
        }
        btnOk.FlatAppearance.BorderSize = 0
        AddHandler btnOk.Paint,
            Sub(s, e)
                Dim b = CType(s, Button) : Dim g = e.Graphics : g.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect = b.ClientRectangle : rect.Inflate(-1, -1)
                Using br As New LinearGradientBrush(rect, normalClr, darkClr, 90.0F) : g.FillRoundedRectangle(br, rect, 7) : End Using
                TextRenderer.DrawText(g, b.Text, b.Font, b.ClientRectangle, Color.White, TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub
        AddHandler btnCancel.Paint,
            Sub(s, e)
                Dim b = CType(s, Button) : Dim g = e.Graphics : g.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect = b.ClientRectangle : rect.Inflate(-1, -1)
                Using br As New SolidBrush(b.BackColor) : g.FillRoundedRectangle(br, rect, 7) : End Using
                Using p As New Pen(Color.FromArgb(200, 215, 210), 1.0F) : g.DrawRoundedRectangle(p, rect, 7) : End Using
                TextRenderer.DrawText(g, b.Text, b.Font, b.ClientRectangle, b.ForeColor, TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub
        AddHandler btnCancel.MouseEnter,
            Sub()
                btnCancel.BackColor = Color.FromArgb(231, 235, 233)
                btnCancel.Invalidate()
            End Sub
        AddHandler btnCancel.MouseLeave,
            Sub()
                btnCancel.BackColor = Color.FromArgb(243, 246, 244)
                btnCancel.Invalidate()
            End Sub

        f.Controls.Add(header)
        f.Controls.Add(lblField)
        f.Controls.Add(fieldBox)
        If lblChange IsNot Nothing Then f.Controls.Add(lblChange)
        f.Controls.Add(btnCancel)
        f.Controls.Add(btnOk)

        Dim cardH As Integer = btnTop + btnH + PAD
        f.Height = cardH

        AddHandler f.Paint,
            Sub(s, e)
                Dim g = e.Graphics : g.SmoothingMode = SmoothingMode.AntiAlias
                Dim card As New Rectangle(0, 0, f.Width - 1, f.Height - 1)
                Using p As New Pen(BorderClr, 1.2F)
                    g.DrawRoundedRectangle(p, card, RADIUS)
                End Using
            End Sub

        Dim rgn As New System.Drawing.Region(New Rectangle(0, 0, CARD_W, cardH))
        Using gp As New GraphicsPath()
            Dim d = RADIUS * 2
            gp.AddArc(0, 0, d, d, 180, 90)
            gp.AddArc(CARD_W - d, 0, d, d, 270, 90)
            gp.AddArc(CARD_W - d, cardH - d, d, d, 0, 90)
            gp.AddArc(0, cardH - d, d, d, 90, 90)
            gp.CloseFigure()
            rgn = New System.Drawing.Region(gp)
        End Using
        f.Region = rgn

        f.AcceptButton = btnOk
        f.CancelButton = btnCancel
        AddHandler f.Shown,
           Sub()
               txt.Focus()
               txt.SelectAll()
           End Sub

        Dim result As DialogResult
        Try
            Dim owner As Form = Form.ActiveForm
            If owner Is Nothing OrElse owner Is f Then
                f.StartPosition = FormStartPosition.CenterScreen
                result = f.ShowDialog()
            Else
                result = f.ShowDialog(owner)
            End If
            Return If(result = DialogResult.OK, txt.Text, Nothing)
        Finally
            f.Dispose()
        End Try
    End Function

    Private Sub DrawMethodIcon(g As Graphics, methodName As String, r As Rectangle)
        g.SmoothingMode = SmoothingMode.AntiAlias
        Using pen As New Pen(Color.White, 1.8F)
            Select Case methodName
                Case "Cash"
                    Dim rr As New Rectangle(r.X, r.Y + 4, r.Width, r.Height - 8)
                    g.DrawRoundedRectangle(pen, rr, 4)
                    g.DrawEllipse(pen, rr.X + rr.Width \ 2 - 4, rr.Y + rr.Height \ 2 - 4, 8, 8)
                Case "Card"
                    Dim rr As New Rectangle(r.X, r.Y + 2, r.Width, r.Height - 4)
                    g.DrawRoundedRectangle(pen, rr, 4)
                    g.DrawLine(pen, rr.X, rr.Y + 8, rr.Right, rr.Y + 8)
                Case Else ' E-Wallet
                    Dim rr As New Rectangle(r.X, r.Y + 2, r.Width - 4, r.Height - 4)
                    g.DrawRoundedRectangle(pen, rr, 4)
                    g.DrawEllipse(pen, rr.Right - 6, rr.Y + rr.Height \ 2 - 3, 6, 6)
            End Select
        End Using
    End Sub

End Module