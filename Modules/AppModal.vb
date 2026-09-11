Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms

Public Module AppModal

    Private ReadOnly CardBg As Color = Color.White
    Private ReadOnly TitleClr As Color = Color.FromArgb(25, 40, 35)
    Private ReadOnly BodyClr As Color = Color.FromArgb(80, 100, 94)
    Private ReadOnly BorderClr As Color = Color.FromArgb(160, 195, 185)
    Private ReadOnly DividerClr As Color = Color.FromArgb(230, 238, 235)
    Private ReadOnly MintPrimary As Color = Theme.Primary
    Private ReadOnly FieldBg As Color = Theme.PageBg
    Private ReadOnly FieldBorder As Color = Theme.BorderMuted
    Private ReadOnly MintDark As Color = Theme.Dark
    Private ReadOnly LightBtn As Color = Theme.FieldBg
    Private ReadOnly LightBtnBorder As Color = Color.FromArgb(200, 215, 210)
    Private ReadOnly LightBtnText As Color = Color.FromArgb(70, 86, 80)

    Private Const RADIUS As Integer = 10
    Private Const BTN_RADIUS As Integer = 7

    ' =====================================================================
    '  Shared helper — builds a rounded-rectangle path. Used for both the
    '  card border and the buttons so every shape uses the same curve.
    ' =====================================================================
    ' radius is the TRUE visual corner radius (not a diameter/bounding-box
    ' size). Every caller passes the same radius for both drawing a border
    ' AND filling a button background — if those ever drift apart, the
    ' visible curves on the card and the buttons won't match.
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

    ' =====================================================================
    '  Shared helper — paints a themed rounded button.
    '  IMPORTANT: this does NOT use Button.Region to clip the corners.
    '  Region clipping is always hard-edged/aliased, regardless of the
    '  Graphics object's SmoothingMode — so a clipped region produces
    '  visibly jagged/stepped corners, especially at small radii.
    '  Instead we paint the full control rect in the surrounding card's
    '  background color first (erasing the square corners into the
    '  background), then draw the rounded shape on top with an
    '  anti-aliased FillPath/DrawPath. The only edge that's actually
    '  visible is that anti-aliased curve, so it comes out smooth.
    ' =====================================================================
    Private Sub PaintRoundedButton(bb As Button, e As PaintEventArgs, isPrimary As Boolean)
        Dim g = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias
        g.PixelOffsetMode = PixelOffsetMode.HighQuality

        ' 1) Erase the control's square corners into the card background.
        Using bgBrush As New SolidBrush(CardBg)
            g.FillRectangle(bgBrush, bb.ClientRectangle)
        End Using

        Dim rect = bb.ClientRectangle
        rect.Width -= 1 : rect.Height -= 1
        Using path As GraphicsPath = RoundedPath(rect, BTN_RADIUS)
            ' 2) Anti-aliased rounded fill — the only visible edge.
            Using br As New SolidBrush(bb.BackColor)
                g.FillPath(br, path)
            End Using
            Dim penClr As Color = If(isPrimary,
                Color.FromArgb(0, MintDark),
                LightBtnBorder)
            Using pen As New Pen(penClr, 1.0F)
                g.DrawPath(pen, path)
            End Using
        End Using

        TextRenderer.DrawText(g, bb.Text, bb.Font, bb.ClientRectangle, bb.ForeColor,
            TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
    End Sub

    ' =====================================================================
    '  Shared helper — builds one themed button (spec -> wired-up Button).
    '  Used by both Show() and Prompt() so the two dialogs stay identical.
    ' =====================================================================
    Private Function BuildButton(sp As (txt As String, result As DialogResult, primary As Boolean),
                                  loc As Point, size As Size) As Button
        Dim isPrimary As Boolean = sp.primary
        Dim b As New Button With {
            .Text = sp.txt,
            .Size = size,
            .Location = loc,
            .FlatStyle = FlatStyle.Flat,
            .Font = New Font("Segoe UI Semibold", 9.5F),
            .Cursor = Cursors.Hand,
            .DialogResult = sp.result,
            .BackColor = If(isPrimary, MintPrimary, LightBtn),
            .ForeColor = If(isPrimary, Color.White, LightBtnText)
        }
        b.FlatAppearance.BorderSize = 0

        Dim normalClr As Color = b.BackColor
        Dim hoverClr As Color = If(isPrimary, MintDark,
            Color.FromArgb(Math.Max(0, normalClr.R - 12),
                           Math.Max(0, normalClr.G - 12),
                           Math.Max(0, normalClr.B - 12)))

        AddHandler b.Paint,
            Sub(s As Object, e As PaintEventArgs)
                PaintRoundedButton(CType(s, Button), e, isPrimary)
            End Sub
        AddHandler b.MouseEnter,
            Sub()
                b.BackColor = hoverClr
                b.Invalidate()
            End Sub
        AddHandler b.MouseLeave,
            Sub()
                b.BackColor = normalClr
                b.Invalidate()
            End Sub

        Return b
    End Function

    Public Function Show(text As String,
                         Optional caption As String = "",
                         Optional buttons As MessageBoxButtons = MessageBoxButtons.OK,
                         Optional icon As MessageBoxIcon = MessageBoxIcon.None) As DialogResult

        Dim accent As Color
        Select Case icon
            Case MessageBoxIcon.Information : accent = Color.FromArgb(45, 127, 240)
            Case MessageBoxIcon.Warning : accent = Color.FromArgb(220, 155, 50)
            Case MessageBoxIcon.Error : accent = Color.FromArgb(200, 65, 65)
            Case Else : accent = MintPrimary
        End Select

        If String.IsNullOrEmpty(caption) Then caption = "Paint Store POS"

        Const CARD_W As Integer = 400
        Const PAD As Integer = 24

        Dim f As New Form With {
            .FormBorderStyle = FormBorderStyle.None,
            .StartPosition = FormStartPosition.CenterParent,
            .BackColor = CardBg,
            .ShowInTaskbar = False,
            .Width = CARD_W,
            .KeyPreview = True
        }

        ' -- Title
        Dim lblTitle As New Label With {
            .Text = caption,
            .Font = New Font("Segoe UI Semibold", 12.0F),
            .ForeColor = TitleClr,
            .AutoSize = False,
            .Location = New Point(PAD, PAD),
            .Size = New Size(CARD_W - PAD * 2, 22),
            .BackColor = Color.Transparent
        }

        ' -- Body
        Dim bodyFont As New Font("Segoe UI", 9.5F)
        Dim textW As Integer = CARD_W - PAD * 2
        Dim measured As Size = TextRenderer.MeasureText(
            text, bodyFont, New Size(textW, 0),
            TextFormatFlags.WordBreak Or TextFormatFlags.TextBoxControl)
        Dim lblBody As New Label With {
            .Text = text,
            .Font = bodyFont,
            .ForeColor = BodyClr,
            .AutoSize = False,
            .Location = New Point(PAD, lblTitle.Bottom + 8),
            .Size = New Size(textW, Math.Max(20, measured.Height + 4)),
            .BackColor = Color.Transparent
        }

        ' -- Divider y
        Dim dividerY As Integer = lblBody.Bottom + 18
        Dim btnRowTop As Integer = dividerY + 16
        Dim btnH As Integer = 36
        Dim btnW As Integer = 100
        Dim gap As Integer = 8

        ' -- Button specs: secondary left, primary right
        Dim specs As New List(Of (txt As String, result As DialogResult, primary As Boolean))
        Select Case buttons
            Case MessageBoxButtons.OKCancel
                specs.Add(("Cancel", DialogResult.Cancel, False))
                specs.Add(("OK", DialogResult.OK, True))
            Case MessageBoxButtons.YesNo
                specs.Add(("No", DialogResult.No, False))
                specs.Add(("Yes", DialogResult.Yes, True))
            Case MessageBoxButtons.YesNoCancel
                specs.Add(("Cancel", DialogResult.Cancel, False))
                specs.Add(("No", DialogResult.No, False))
                specs.Add(("Yes", DialogResult.Yes, True))
            Case MessageBoxButtons.RetryCancel
                specs.Add(("Cancel", DialogResult.Cancel, False))
                specs.Add(("Retry", DialogResult.Retry, True))
            Case Else
                specs.Add(("OK", DialogResult.OK, True))
        End Select

        Dim totalBtnW As Integer = specs.Count * btnW + (specs.Count - 1) * gap
        Dim x As Integer = CARD_W - PAD - totalBtnW
        Dim defaultBtn As Button = Nothing

        For Each sp In specs
            Dim b As Button = BuildButton(sp, New Point(x, btnRowTop), New Size(btnW, btnH))
            f.Controls.Add(b)
            If sp.primary Then defaultBtn = b
            x += btnW + gap
        Next

        f.Controls.Add(lblTitle)
        f.Controls.Add(lblBody)

        Dim cardH As Integer = btnRowTop + btnH + PAD
        f.Height = cardH

        Dim accentLocal As Color = accent
        Dim dividerYLocal As Integer = dividerY

        ' Draw card border + divider
        AddHandler f.Paint,
            Sub(s As Object, e As PaintEventArgs)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias

                Dim card As New Rectangle(0, 0, f.Width - 1, f.Height - 1)

                ' Card fill
                Using br As New SolidBrush(CardBg)
                    g.FillRectangle(br, card)
                End Using

                ' Divider
                Using pen As New Pen(DividerClr, 1.0F)
                    g.DrawLine(pen, PAD, dividerYLocal, f.Width - PAD, dividerYLocal)
                End Using

                ' Card border — rounded to match the form's clipped corners
                Using path As GraphicsPath = RoundedPath(card, RADIUS)
                    Using pen As New Pen(BorderClr, 1.2F)
                        g.DrawPath(pen, path)
                    End Using
                End Using
            End Sub

        ' Round the form corners via region
        Dim rgn As New System.Drawing.Region(New Rectangle(0, 0, CARD_W, cardH))
        Using gp As GraphicsPath = RoundedPath(New Rectangle(0, 0, CARD_W, cardH), RADIUS)
            rgn = New System.Drawing.Region(gp)
        End Using
        f.Region = rgn

        If defaultBtn IsNot Nothing Then f.AcceptButton = defaultBtn
        Dim escResult As DialogResult = If(buttons = MessageBoxButtons.OK, DialogResult.OK, DialogResult.Cancel)
        AddHandler f.KeyDown,
            Sub(s As Object, e As KeyEventArgs)
                If e.KeyCode = Keys.Escape Then
                    f.DialogResult = escResult
                    f.Close()
                End If
            End Sub

        Try
            Dim owner As Form = Form.ActiveForm
            If owner Is Nothing OrElse owner Is f Then
                f.StartPosition = FormStartPosition.CenterScreen
                Return f.ShowDialog()
            Else
                Return f.ShowDialog(owner)
            End If
        Finally
            f.Dispose()
        End Try
    End Function

    ' =====================================================================
    '  Prompt  —  themed single-line text input.
    '  Returns the entered text, or Nothing if the user cancels/closes.
    ' =====================================================================
    Public Function Prompt(message As String,
                           Optional caption As String = "",
                           Optional defaultValue As String = "") As String

        If String.IsNullOrEmpty(caption) Then caption = "Paint Store POS"

        Const CARD_W As Integer = 400
        Const PAD As Integer = 24

        Dim f As New Form With {
            .FormBorderStyle = FormBorderStyle.None,
            .StartPosition = FormStartPosition.CenterParent,
            .BackColor = CardBg,
            .ShowInTaskbar = False,
            .Width = CARD_W,
            .KeyPreview = True
        }

        Dim lblTitle As New Label With {
            .Text = caption,
            .Font = New Font("Segoe UI Semibold", 12.0F),
            .ForeColor = TitleClr,
            .AutoSize = False,
            .Location = New Point(PAD, PAD),
            .Size = New Size(CARD_W - PAD * 2, 22),
            .BackColor = Color.Transparent
        }

        Dim bodyFont As New Font("Segoe UI", 9.5F)
        Dim textW As Integer = CARD_W - PAD * 2
        Dim measured As Size = TextRenderer.MeasureText(
            message, bodyFont, New Size(textW, 0),
            TextFormatFlags.WordBreak Or TextFormatFlags.TextBoxControl)
        Dim lblBody As New Label With {
            .Text = message,
            .Font = bodyFont,
            .ForeColor = BodyClr,
            .AutoSize = False,
            .Location = New Point(PAD, lblTitle.Bottom + 8),
            .Size = New Size(textW, Math.Max(20, measured.Height + 4)),
            .BackColor = Color.Transparent
        }

        ' The field sits on a rounded, mint-bordered panel so it matches every
        ' other input in the app. It used to be a bare FixedSingle box, which is
        ' the grey Windows rectangle that looked out of place on this card.
        Const FIELD_H As Integer = 40
        Dim fieldPanel As New Panel With {
            .Location = New Point(PAD, lblBody.Bottom + 12),
            .Size = New Size(textW, FIELD_H),
            .BackColor = FieldBg
        }
        Dim txt As New TextBox With {
            .Font = New Font("Segoe UI", 11.0F),
            .Text = defaultValue,
            .BorderStyle = BorderStyle.None,
            .BackColor = FieldBg,
            .ForeColor = TitleClr,
            .Location = New Point(12, (FIELD_H - 20) \ 2),
            .Width = textW - 24
        }
        AddHandler fieldPanel.Paint,
            Sub(sender As Object, ev As PaintEventArgs)
                ev.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim rc As New Rectangle(0, 0, fieldPanel.Width - 1, fieldPanel.Height - 1)
                Using path As GraphicsPath = RoundedPath(rc, 8)
                    Using b As New SolidBrush(FieldBg)
                        ev.Graphics.FillPath(b, path)
                    End Using
                    Using pen As New Pen(If(txt.Focused, MintPrimary, FieldBorder), 1.2F)
                        ev.Graphics.DrawPath(pen, path)
                    End Using
                End Using
            End Sub
        AddHandler txt.GotFocus, Sub() fieldPanel.Invalidate()
        AddHandler txt.LostFocus, Sub() fieldPanel.Invalidate()
        fieldPanel.Controls.Add(txt)

        Dim dividerY As Integer = fieldPanel.Bottom + 18
        Dim btnRowTop As Integer = dividerY + 16
        Dim btnH As Integer = 36
        Dim btnW As Integer = 100
        Dim gap As Integer = 8

        Dim specs As New List(Of (txt As String, result As DialogResult, primary As Boolean))
        specs.Add(("Cancel", DialogResult.Cancel, False))
        specs.Add(("OK", DialogResult.OK, True))

        Dim totalBtnW As Integer = specs.Count * btnW + (specs.Count - 1) * gap
        Dim x As Integer = CARD_W - PAD - totalBtnW
        Dim defaultBtn As Button = Nothing

        For Each sp In specs
            Dim b As Button = BuildButton(sp, New Point(x, btnRowTop), New Size(btnW, btnH))
            f.Controls.Add(b)
            If sp.primary Then defaultBtn = b
            x += btnW + gap
        Next

        f.Controls.Add(lblTitle)
        f.Controls.Add(lblBody)
        f.Controls.Add(fieldPanel)

        Dim cardH As Integer = btnRowTop + btnH + PAD
        f.Height = cardH

        Dim dividerYLocal As Integer = dividerY
        AddHandler f.Paint,
            Sub(s As Object, e As PaintEventArgs)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim card As New Rectangle(0, 0, f.Width - 1, f.Height - 1)
                Using br As New SolidBrush(CardBg)
                    g.FillRectangle(br, card)
                End Using
                Using pen As New Pen(DividerClr, 1.0F)
                    g.DrawLine(pen, PAD, dividerYLocal, f.Width - PAD, dividerYLocal)
                End Using
                Using path As GraphicsPath = RoundedPath(card, RADIUS)
                    Using pen As New Pen(BorderClr, 1.2F)
                        g.DrawPath(pen, path)
                    End Using
                End Using
            End Sub

        Dim rgn As New System.Drawing.Region(New Rectangle(0, 0, CARD_W, cardH))
        Using gp As GraphicsPath = RoundedPath(New Rectangle(0, 0, CARD_W, cardH), RADIUS)
            rgn = New System.Drawing.Region(gp)
        End Using
        f.Region = rgn

        If defaultBtn IsNot Nothing Then f.AcceptButton = defaultBtn
        AddHandler f.KeyDown,
            Sub(s As Object, e As KeyEventArgs)
                If e.KeyCode = Keys.Escape Then
                    f.DialogResult = DialogResult.Cancel
                    f.Close()
                End If
            End Sub
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

            If result = DialogResult.OK Then
                Return txt.Text
            Else
                Return Nothing
            End If
        Finally
            f.Dispose()
        End Try
    End Function

End Module