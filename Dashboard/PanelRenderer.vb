Imports System.Drawing
Imports System.Drawing.Drawing2D

Public Class PanelRenderer

    Public Shared MintPrimary As Color = Color.FromArgb(0, 200, 170)
    Public Shared MintLight As Color = Color.FromArgb(220, 255, 245)

    Public Shared Sub Draw(panel As Panel, e As PaintEventArgs, Optional drawBackground As Boolean = True)
        Dim g = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim rect As Rectangle = panel.ClientRectangle
        rect.Inflate(-1, -1)

        Dim radius As Integer = 14

        If drawBackground Then
            ' soft shadow — single, subtle
            Dim shadowRect As New Rectangle(rect.X + 1, rect.Y + 2, rect.Width - 2, rect.Height - 2)
            Using shadowBrush As New SolidBrush(Color.FromArgb(14, 0, 0, 0))
                g.FillRoundedRectangle(shadowBrush, shadowRect, radius)
            End Using

            ' flat white fill — no gradient
            Using fillBrush As New SolidBrush(Color.White)
                g.FillRoundedRectangle(fillBrush, rect, radius)
            End Using
        End If

        ' hairline border
        Using pen As New Pen(Color.FromArgb(170, 185, 178), 1.0F)
            g.DrawRoundedRectangle(pen, rect, radius)
        End Using
    End Sub

End Class