Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms

Public Module SaleCompleteModal

    Private ReadOnly CardBg As Color = Color.White
    Private ReadOnly SubText As Color = Color.FromArgb(120, 140, 134)
    Private ReadOnly TextDark As Color = Color.FromArgb(35, 50, 46)
    Private ReadOnly RowDivider As Color = Color.FromArgb(232, 238, 236)
    Private ReadOnly BorderClr As Color = Color.FromArgb(160, 195, 185)

    Private Const RADIUS As Integer = 10
    Private Const CARD_W As Integer = 380
    Private Const PAD As Integer = 24

    Public Sub Show(methodName As String, saleNo As String, itemCount As Integer, total As Decimal)

        Dim normalClr As Color, darkClr As Color
        Select Case methodName
            Case "Cash"
                normalClr = Color.FromArgb(0, 148, 74) : darkClr = Color.FromArgb(0, 90, 50)
            Case "Card"
                normalClr = Color.FromArgb(30, 100, 190) : darkClr = Color.FromArgb(15, 60, 130)
            Case Else
                normalClr = Color.FromArgb(100, 50, 180) : darkClr = Color.FromArgb(60, 25, 120)
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
                DrawSuccessIcon(g, New Rectangle(PAD, headerH \ 2 - 14, 28, 28))
            End Sub
        Dim lblTitle As New Label With {
            .Text = "Sale Complete", .Font = New Font("Segoe UI Semibold", 13.0F),
            .ForeColor = Color.White, .BackColor = Color.Transparent, .AutoSize = False,
            .Location = New Point(PAD + 38, 16), .Size = New Size(CARD_W - PAD * 2 - 38, 22)
        }
        Dim lblSub As New Label With {
            .Text = "Sale posted successfully", .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = Color.FromArgb(225, 240, 235), .BackColor = Color.Transparent, .AutoSize = False,
            .Location = New Point(PAD + 38, 40), .Size = New Size(CARD_W - PAD * 2 - 38, 18)
        }
        header.Controls.AddRange({lblTitle, lblSub})

        ' === Summary rows ===
        Dim rows As New List(Of (label As String, val As String, emphasize As Boolean))
        rows.Add(("Sale No.", saleNo, False))
        rows.Add(("Items", itemCount.ToString(), False))
        rows.Add(("Total", "P " & total.ToString("N2"), True))
        rows.Add(("Payment Method", methodName, False))

        Dim y As Integer = headerH + 18
        Dim rowH As Integer = 34
        For Each rw In rows
            Dim lbl As New Label With {
                .Text = rw.label, .Font = New Font("Segoe UI", 9.5F), .ForeColor = SubText,
                .AutoSize = False, .Location = New Point(PAD, y), .Size = New Size(150, rowH),
                .BackColor = Color.Transparent, .TextAlign = ContentAlignment.MiddleLeft
            }
            Dim valLbl As New Label With {
                .Text = rw.val, .Font = New Font("Segoe UI Semibold", If(rw.emphasize, 12.0F, 10.5F)),
                .ForeColor = If(rw.emphasize, normalClr, TextDark),
                .AutoSize = False, .Location = New Point(PAD + 140, y), .Size = New Size(CARD_W - PAD * 2 - 140, rowH),
                .BackColor = Color.Transparent, .TextAlign = ContentAlignment.MiddleRight
            }
            f.Controls.AddRange({lbl, valLbl})
            y += rowH
        Next

        Dim dividerY As Integer = y + 4
        y = dividerY + 18

        ' === Button ===
        Dim btnH As Integer = 40
        Dim btnW As Integer = CARD_W - PAD * 2
        Dim btnTop As Integer = y

        Dim btnDone As New Button With {
            .Text = "Done", .Size = New Size(btnW, btnH), .FlatStyle = FlatStyle.Flat,
            .Font = New Font("Segoe UI Semibold", 10.0F), .Cursor = Cursors.Hand,
            .DialogResult = DialogResult.OK, .ForeColor = Color.White,
            .Location = New Point(PAD, btnTop)
        }
        btnDone.FlatAppearance.BorderSize = 0
        AddHandler btnDone.Paint,
            Sub(s, e)
                Dim b = CType(s, Button) : Dim g = e.Graphics : g.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect = b.ClientRectangle : rect.Inflate(-1, -1)
                Using br As New LinearGradientBrush(rect, normalClr, darkClr, 90.0F) : g.FillRoundedRectangle(br, rect, 7) : End Using
                TextRenderer.DrawText(g, b.Text, b.Font, b.ClientRectangle, Color.White, TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub

        f.Controls.Add(header)
        f.Controls.Add(btnDone)

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

        f.AcceptButton = btnDone
        f.CancelButton = btnDone

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
    End Sub

    Private Sub DrawSuccessIcon(g As Graphics, r As Rectangle)
        g.SmoothingMode = SmoothingMode.AntiAlias
        Using penCircle As New Pen(Color.White, 1.8F)
            g.DrawEllipse(penCircle, r)
        End Using
        Using pen As New Pen(Color.White, 2.2F)
            pen.StartCap = Drawing2D.LineCap.Round
            pen.EndCap = Drawing2D.LineCap.Round
            pen.LineJoin = Drawing2D.LineJoin.Round
            Dim cy = r.Y + r.Height \ 2
            g.DrawLines(pen, {
                New Point(r.X + 7, cy + 1),
                New Point(r.X + r.Width \ 2 - 1, r.Bottom - 8),
                New Point(r.Right - 6, r.Y + 8)
            })
        End Using
    End Sub

End Module