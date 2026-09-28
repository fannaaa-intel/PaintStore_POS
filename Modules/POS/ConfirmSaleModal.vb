Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms

Public Module ConfirmSaleModal

    Private ReadOnly CardBg As Color = Color.White
    Private ReadOnly SubText As Color = Color.FromArgb(120, 140, 134)
    Private ReadOnly TextDark As Color = Theme.DeepInk
    Private ReadOnly RowDivider As Color = Color.FromArgb(232, 238, 236)
    Private ReadOnly BorderClr As Color = Color.FromArgb(160, 195, 185)

    Private Const RADIUS As Integer = 10
    Private Const CARD_W As Integer = 380
    Private Const PAD As Integer = 24

    ' Returns True if the cashier confirmed (Yes), False if cancelled/closed.
    Public Function Show(methodName As String,
                         grandTotal As Decimal,
                         tendered As Decimal,
                         changeDue As Decimal,
                         referenceNo As String,
                         Optional chequeAccountName As String = "") As Boolean

        Dim normalClr As Color, darkClr As Color, title As String
        Select Case methodName
            Case "Cash"
                normalClr = Color.FromArgb(0, 148, 74) : darkClr = Color.FromArgb(0, 90, 50)
                title = "Confirm Cash Sale"
            Case "Card"
                normalClr = Color.FromArgb(30, 100, 190) : darkClr = Color.FromArgb(15, 60, 130)
                title = "Confirm Card Sale"
            Case Else
                normalClr = Color.FromArgb(100, 50, 180) : darkClr = Color.FromArgb(60, 25, 120)
                title = "Confirm " & methodName & " Sale"
        End Select

        Dim f As New Form With {
            .FormBorderStyle = FormBorderStyle.None,
            .StartPosition = FormStartPosition.CenterParent,
            .BackColor = CardBg,
            .ShowInTaskbar = False,
            .Width = CARD_W,
            .KeyPreview = True
        }

        ' === Header ===
        Dim headerH As Integer = 70
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
                DrawCheckIcon(g, New Rectangle(PAD, headerH \ 2 - 14, 28, 28))
            End Sub
        Dim lblTitle As New Label With {
            .Text = title, .Font = New Font("Segoe UI Semibold", 13.0F),
            .ForeColor = Color.White, .BackColor = Color.Transparent, .AutoSize = False,
            .Location = New Point(PAD + 38, (headerH - 22) \ 2), .Size = New Size(CARD_W - PAD * 2 - 38, 22)
        }
        header.Controls.Add(lblTitle)

        ' === Summary rows ===
        Dim rows As New List(Of (label As String, val As String, emphasize As Boolean))
        rows.Add(("Payment Method", methodName, False))
        rows.Add(("Grand Total", "P " & grandTotal.ToString("N2"), False))
        If methodName = "Cash" Then
            rows.Add(("Amount Tendered", "P " & tendered.ToString("N2"), False))
            rows.Add(("Change Due", "P " & changeDue.ToString("N2"), True))
        ElseIf Not String.IsNullOrEmpty(chequeAccountName) Then
            rows.Add(("Account Name", chequeAccountName, False))
            rows.Add(("Check No.", referenceNo, False))
        Else
            rows.Add(("Reference No.", referenceNo, False))
        End If

        Dim y As Integer = headerH + 18
        Dim rowH As Integer = 34
        For Each rw In rows
            Dim lbl As New Label With {
                .Text = rw.label, .Font = New Font("Segoe UI", 9.5F), .ForeColor = SubText,
                .AutoSize = False, .Location = New Point(PAD, y), .Size = New Size(160, rowH),
                .BackColor = Color.Transparent, .TextAlign = ContentAlignment.MiddleLeft
            }
            Dim valLbl As New Label With {
                .Text = rw.val, .Font = New Font("Segoe UI Semibold", If(rw.emphasize, 12.0F, 10.5F)),
                .ForeColor = If(rw.emphasize, normalClr, TextDark),
                .AutoSize = False, .Location = New Point(PAD + 150, y), .Size = New Size(CARD_W - PAD * 2 - 150, rowH),
                .BackColor = Color.Transparent, .TextAlign = ContentAlignment.MiddleRight
            }
            f.Controls.AddRange({lbl, valLbl})
            y += rowH
        Next

        Dim dividerY As Integer = y + 4
        y = dividerY + 18

        Dim lblQuestion As New Label With {
            .Text = "Post this sale?", .Font = New Font("Segoe UI", 9.5F), .ForeColor = SubText,
            .AutoSize = False, .Location = New Point(PAD, y), .Size = New Size(CARD_W - PAD * 2, 20),
            .BackColor = Color.Transparent
        }
        f.Controls.Add(lblQuestion)
        y = lblQuestion.Bottom + 16

        ' === Buttons ===
        Dim btnH As Integer = 38, gap As Integer = 8
        Dim btnOkW As Integer = 140, btnCancelW As Integer = 96
        Dim btnTop As Integer = y

        Dim btnOk As New Button With {
            .Text = "Confirm Sale", .Size = New Size(btnOkW, btnH), .FlatStyle = FlatStyle.Flat,
            .Font = New Font("Segoe UI Semibold", 9.5F), .Cursor = Cursors.Hand,
            .DialogResult = DialogResult.OK, .ForeColor = Color.White,
            .Location = New Point(CARD_W - PAD - btnOkW, btnTop)
        }
        btnOk.FlatAppearance.BorderSize = 0

        Dim btnCancel As New Button With {
            .Text = "Cancel", .Size = New Size(btnCancelW, btnH), .FlatStyle = FlatStyle.Flat,
            .Font = New Font("Segoe UI Semibold", 9.5F), .Cursor = Cursors.Hand,
            .DialogResult = DialogResult.Cancel, .BackColor = Color.FromArgb(243, 246, 244), .ForeColor = Color.FromArgb(70, 86, 80),
            .Location = New Point(btnOk.Left - gap - btnCancelW, btnTop)
        }
        btnCancel.FlatAppearance.BorderSize = 0

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
                Using pen As New Pen(RowDivider, 1.0F)
                    g.DrawLine(pen, PAD, dividerY, f.Width - PAD, dividerY)
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

        Dim result As DialogResult
        Try
            Dim owner As Form = Form.ActiveForm
            If owner Is Nothing OrElse owner Is f Then
                f.StartPosition = FormStartPosition.CenterScreen
                result = f.ShowDialog()
            Else
                result = f.ShowDialog(owner)
            End If
            Return result = DialogResult.OK
        Finally
            f.Dispose()
        End Try
    End Function

    Private Sub DrawCheckIcon(g As Graphics, r As Rectangle)
        g.SmoothingMode = SmoothingMode.AntiAlias
        Using pen As New Pen(Color.White, 2.4F)
            pen.StartCap = Drawing2D.LineCap.Round
            pen.EndCap = Drawing2D.LineCap.Round
            pen.LineJoin = Drawing2D.LineJoin.Round
            Dim cy = r.Y + r.Height \ 2
            g.DrawLines(pen, {
                New Point(r.X + 6, cy + 2),
                New Point(r.X + r.Width \ 2 - 1, r.Bottom - 7),
                New Point(r.Right - 5, r.Y + 7)
            })
        End Using
    End Sub

End Module