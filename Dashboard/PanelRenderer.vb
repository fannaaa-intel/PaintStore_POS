Imports System.Drawing
Imports System.Drawing.Drawing2D

Public Class PanelRenderer

    Public Shared MintPrimary As Color = Color.FromArgb(0, 200, 170)
    Public Shared MintLight As Color = Color.FromArgb(220, 255, 245)

    ' How far the shadow reaches past the card on each side. A caller that
    ' wants the lifted look insets its content by this much so the shadow has
    ' somewhere to fall: a control cannot paint outside its own bounds, so
    ' without the inset the card edge would clip it away.
    Public Const ShadowPad As Integer = 12

    Public Shared Sub Draw(panel As Panel, e As PaintEventArgs, Optional drawBackground As Boolean = True)
        Draw(panel, e, drawBackground, False)
    End Sub

    ''' <summary>
    ''' Paint a card. When <paramref name="elevated"/> is set the card is drawn
    ''' inset by ShadowPad with a soft drop shadow around it, which is what
    ''' separates a white card from the near-white page behind it — at the old
    ''' single 14-alpha layer the two read as one flat surface.
    ''' </summary>
    Public Shared Sub Draw(panel As Panel, e As PaintEventArgs,
                           drawBackground As Boolean, elevated As Boolean)
        Dim g = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias

        Dim rect As Rectangle = panel.ClientRectangle
        If elevated Then
            ' Leave room on every side for the shadow, and lift the card a
            ' touch above centre so the shadow reads as cast downward.
            rect.Inflate(-ShadowPad, -ShadowPad)
            rect.Offset(0, -2)
        Else
            rect.Inflate(-1, -1)
        End If
        If rect.Width <= 0 OrElse rect.Height <= 0 Then Return

        Dim radius As Integer = 14

        If drawBackground Then
            If elevated Then
                ' Layered shadow: several increasingly large, increasingly
                ' faint rounded rects stacked under the card. One solid pass
                ' gives a hard edge that looks like a misaligned second card;
                ' stacking approximates a real blur without the cost of one.
                For i As Integer = ShadowPad To 1 Step -1
                    ' Quadratic falloff — the opacity drops off faster further
                    ' out, which is how a real penumbra behaves.
                    Dim t As Double = i / CDbl(ShadowPad)
                    Dim alpha As Integer = CInt(Math.Round(20 * (1.0 - t) * (1.0 - t))) + 1
                    Dim sr As New Rectangle(rect.X - i, rect.Y - i + 3,
                                            rect.Width + i * 2, rect.Height + i * 2)
                    Using sb As New SolidBrush(Color.FromArgb(alpha, 22, 48, 40))
                        g.FillRoundedRectangle(sb, sr, radius + i)
                    End Using
                Next
            Else
                ' soft shadow — single, subtle
                Dim shadowRect As New Rectangle(rect.X + 1, rect.Y + 2, rect.Width - 2, rect.Height - 2)
                Using shadowBrush As New SolidBrush(Color.FromArgb(14, 0, 0, 0))
                    g.FillRoundedRectangle(shadowBrush, shadowRect, radius)
                End Using
            End If

            ' flat white fill — no gradient
            Using fillBrush As New SolidBrush(Color.White)
                g.FillRoundedRectangle(fillBrush, rect, radius)
            End Using
        End If

        ' hairline border. Lighter on an elevated card: the shadow is already
        ' doing the separating, and the darker line then reads as a hard
        ' outline drawn on top of it.
        Using pen As New Pen(If(elevated,
                                Color.FromArgb(214, 226, 221),
                                Color.FromArgb(170, 185, 178)), 1.0F)
            g.DrawRoundedRectangle(pen, rect, radius)
        End Using
    End Sub

End Class