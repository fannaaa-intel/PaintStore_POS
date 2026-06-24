Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Runtime.CompilerServices

Module GraphicsExtensions

    <Extension>
    Public Sub FillRoundedRectangle(g As Graphics, brush As Brush, rect As Rectangle, radius As Integer)
        Using path As GraphicsPath = RoundedRect(rect, radius)
            g.FillPath(brush, path)
        End Using
    End Sub

    <Extension>
    Public Sub DrawRoundedRectangle(g As Graphics, pen As Pen, rect As Rectangle, radius As Integer)
        Using path As GraphicsPath = RoundedRect(rect, radius)
            g.DrawPath(pen, path)
        End Using
    End Sub

    Private Function RoundedRect(rect As Rectangle, radius As Integer) As GraphicsPath
        Dim path As New GraphicsPath()
        Dim d As Integer = radius * 2

        path.AddArc(rect.X, rect.Y, d, d, 180, 90)
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90)
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90)
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90)

        path.CloseFigure()
        Return path
    End Function

End Module