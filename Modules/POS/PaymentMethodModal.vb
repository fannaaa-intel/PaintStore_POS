Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms

' =============================================================================
'  PaymentMethodModal - add or edit a payment method with all its fields.
'
'  Replaces the single-line name-only prompt. A method affects both the till
'  and the accountant reports, so the form collects what decides how it
'  behaves: Name, Type (Cash / Reference), Active, and an optional Note.
'
'  Returns a filled Result on OK, or Nothing if cancelled. Name validation
'  stays with the caller, which owns the DB.
' =============================================================================
Public Module PaymentMethodModal

    Private ReadOnly CardBg As Color = Color.White
    Private ReadOnly TitleClr As Color = Color.FromArgb(25, 40, 35)
    Private ReadOnly BorderClr As Color = Color.FromArgb(160, 195, 185)
    Private ReadOnly DividerClr As Color = Color.FromArgb(230, 238, 235)
    Private ReadOnly MintPrimary As Color = Theme.Primary
    Private ReadOnly MintDark As Color = Theme.Dark
    Private ReadOnly SubText As Color = Theme.SubText
    Private ReadOnly FieldBg As Color = Theme.PageBg
    Private ReadOnly FieldBorder As Color = Theme.BorderMuted

    Private Const RADIUS As Integer = 10
    Private Const CARD_W As Integer = 440
    Private Const PAD As Integer = 24
    Private Const FIELD_H As Integer = 40

    Public Class Result
        Public Name As String = ""
        Public Type As String = "Reference"
        Public IsActive As Boolean = True
        Public Note As String = ""
    End Class

    Private Function RoundedPath(rect As Rectangle, radius As Integer) As GraphicsPath
        Dim path As New GraphicsPath()
        Dim d As Integer = radius * 2
        path.AddArc(rect.X, rect.Y, d, d, 180, 90)
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90)
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90)
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90)
        path.CloseFigure()
        Return path
    End Function

    ' isEdit changes only the title. showTypeActive is False on an un-migrated
    ' database - then only the name is asked, like the old prompt.
    Public Function Prompt(isEdit As Boolean, showTypeActive As Boolean,
                           seed As Result) As Result

        If seed Is Nothing Then seed = New Result()
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
            .Text = If(isEdit, "Edit payment method", "Add payment method"),
            .Font = New Font("Segoe UI Semibold", 12.0F),
            .ForeColor = TitleClr,
            .AutoSize = False,
            .Location = New Point(PAD, PAD),
            .Size = New Size(CARD_W - PAD * 2, 22),
            .BackColor = Color.Transparent
        }

        Dim lblName As New Label With {
            .Text = "Name", .Font = New Font("Segoe UI Semibold", 9.0F), .ForeColor = SubText,
            .AutoSize = True, .Location = New Point(PAD, lblTitle.Bottom + 14), .BackColor = Color.Transparent
        }
        Dim txtName As TextBox = MakeField(PAD, lblName.Bottom + 4, CARD_W - PAD * 2, f, seed.Name)

        Dim y As Integer = txtName.Parent.Bottom + 12

        Dim rbCash As RadioButton = Nothing
        Dim rbRef As RadioButton = Nothing
        Dim chkActive As CheckBox = Nothing
        Dim txtNote As TextBox = Nothing

        f.Controls.Add(lblName)

        If showTypeActive Then
            Dim lblType As New Label With {
                .Text = "Type", .Font = New Font("Segoe UI Semibold", 9.0F), .ForeColor = SubText,
                .AutoSize = True, .Location = New Point(PAD, y), .BackColor = Color.Transparent
            }
            rbCash = New RadioButton With {
                .Text = "Cash  -  amount tendered, gives change",
                .Font = New Font("Segoe UI", 9.5F), .ForeColor = TitleClr,
                .AutoSize = True, .Location = New Point(PAD + 4, lblType.Bottom + 6),
                .BackColor = Color.Transparent
            }
            rbRef = New RadioButton With {
                .Text = "Reference  -  asks for a reference number",
                .Font = New Font("Segoe UI", 9.5F), .ForeColor = TitleClr,
                .AutoSize = True, .Location = New Point(PAD + 4, rbCash.Bottom + 4),
                .BackColor = Color.Transparent
            }
            rbCash.Checked = String.Equals(seed.Type, "Cash", StringComparison.OrdinalIgnoreCase)
            rbRef.Checked = Not rbCash.Checked

            chkActive = New CheckBox With {
                .Text = "Active  -  shown to the cashier at checkout",
                .Font = New Font("Segoe UI", 9.5F), .ForeColor = TitleClr,
                .AutoSize = True, .Location = New Point(PAD + 4, rbRef.Bottom + 10),
                .Checked = seed.IsActive, .BackColor = Color.Transparent
            }

            Dim lblNote As New Label With {
                .Text = "Note (optional)", .Font = New Font("Segoe UI Semibold", 9.0F), .ForeColor = SubText,
                .AutoSize = True, .Location = New Point(PAD, chkActive.Bottom + 12), .BackColor = Color.Transparent
            }
            txtNote = MakeField(PAD, lblNote.Bottom + 4, CARD_W - PAD * 2, f, seed.Note)

            f.Controls.AddRange({lblType, rbCash, rbRef, chkActive, lblNote})
            y = txtNote.Parent.Bottom + 12
        End If

        Dim dividerY As Integer = y + 6
        Dim btnY As Integer = dividerY + 16
        Dim cardH As Integer = btnY + 38 + PAD

        Dim btnOk As Button = MakeButton("Save", True,
            New Point(CARD_W - PAD - 110, btnY), New Size(110, 38))
        Dim btnCancel As Button = MakeButton("Cancel", False,
            New Point(CARD_W - PAD - 110 - 8 - 96, btnY), New Size(96, 38))

        AddHandler btnCancel.Click, Sub() f.Close()
        AddHandler btnOk.Click,
            Sub()
                Dim r As New Result With {.Name = txtName.Text.Trim()}
                If showTypeActive Then
                    r.Type = If(rbCash IsNot Nothing AndAlso rbCash.Checked, "Cash", "Reference")
                    r.IsActive = chkActive Is Nothing OrElse chkActive.Checked
                    r.Note = If(txtNote Is Nothing, "", txtNote.Text.Trim())
                End If
                out = r
                f.Close()
            End Sub

        f.Height = cardH
        f.Controls.AddRange({lblTitle, btnOk, btnCancel})

        Dim dividerYLocal As Integer = dividerY
        AddHandler f.Paint,
            Sub(s As Object, e As PaintEventArgs)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim card As New Rectangle(0, 0, f.Width - 1, f.Height - 1)
                Using pen As New Pen(DividerClr, 1.0F)
                    g.DrawLine(pen, PAD, dividerYLocal, f.Width - PAD, dividerYLocal)
                End Using
                Using path As GraphicsPath = RoundedPath(card, RADIUS)
                    Using pen As New Pen(BorderClr, 1.2F)
                        g.DrawPath(pen, path)
                    End Using
                End Using
            End Sub

        Using gp As GraphicsPath = RoundedPath(New Rectangle(0, 0, CARD_W, cardH), RADIUS)
            f.Region = New System.Drawing.Region(gp)
        End Using

        f.AcceptButton = btnOk
        AddHandler f.KeyDown,
            Sub(s As Object, e As KeyEventArgs)
                If e.KeyCode = Keys.Escape Then f.Close()
            End Sub
        AddHandler f.Shown, Sub() txtName.Focus()

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

    Private Function MakeField(x As Integer, y As Integer, w As Integer, host As Form,
                               seedText As String) As TextBox
        Dim pnl As New Panel With {.Location = New Point(x, y), .Size = New Size(w, FIELD_H), .BackColor = FieldBg}
        Dim txt As New TextBox With {
            .BorderStyle = BorderStyle.None, .Font = New Font("Segoe UI", 10.5F),
            .BackColor = FieldBg, .ForeColor = TitleClr, .Text = If(seedText, ""),
            .Location = New Point(12, (FIELD_H - 20) \ 2), .Width = w - 24
        }
        AddHandler pnl.Paint,
            Sub(s As Object, e As PaintEventArgs)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim rc As New Rectangle(0, 0, pnl.Width - 1, pnl.Height - 1)
                Using path As GraphicsPath = RoundedPath(rc, 8)
                    Using b As New SolidBrush(FieldBg) : e.Graphics.FillPath(b, path) : End Using
                    Using pen As New Pen(If(txt.Focused, MintPrimary, FieldBorder), 1.2F)
                        e.Graphics.DrawPath(pen, path)
                    End Using
                End Using
            End Sub
        AddHandler txt.GotFocus, Sub() pnl.Invalidate()
        AddHandler txt.LostFocus, Sub() pnl.Invalidate()
        pnl.Controls.Add(txt)
        host.Controls.Add(pnl)
        Return txt
    End Function

    Private Function MakeButton(caption As String, isPrimary As Boolean, loc As Point, size As Size) As Button
        Dim b As New Button With {
            .Text = caption, .Size = size, .Location = loc, .FlatStyle = FlatStyle.Flat,
            .Font = New Font("Segoe UI Semibold", 9.5F), .Cursor = Cursors.Hand,
            .BackColor = If(isPrimary, MintPrimary, Color.FromArgb(243, 246, 244)),
            .ForeColor = If(isPrimary, Color.White, Color.FromArgb(70, 86, 80))
        }
        b.FlatAppearance.BorderSize = 0
        Dim normalClr As Color = b.BackColor
        Dim hoverClr As Color = If(isPrimary, MintDark, Color.FromArgb(231, 234, 232))
        AddHandler b.Paint,
            Sub(s As Object, e As PaintEventArgs)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                g.PixelOffsetMode = PixelOffsetMode.HighQuality
                Using bg As New SolidBrush(CardBg) : g.FillRectangle(bg, b.ClientRectangle) : End Using
                Dim rect = b.ClientRectangle : rect.Width -= 1 : rect.Height -= 1
                Using path As GraphicsPath = RoundedPath(rect, 7)
                    Using br As New SolidBrush(b.BackColor) : g.FillPath(br, path) : End Using
                    If Not isPrimary Then
                        Using pen As New Pen(Color.FromArgb(200, 215, 210), 1.0F) : g.DrawPath(pen, path) : End Using
                    End If
                End Using
                TextRenderer.DrawText(g, b.Text, b.Font, b.ClientRectangle, b.ForeColor,
                    TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub
        AddHandler b.MouseEnter, Sub()
                                     b.BackColor = hoverClr
                                     b.Invalidate()
                                 End Sub
        AddHandler b.MouseLeave, Sub()
                                     b.BackColor = normalClr
                                     b.Invalidate()
                                 End Sub
        Return b
    End Function

End Module
