Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms

Public Class AnalyticsPanelBuilder

    Public Shared Sub LoadAnalytics(panel As Panel)

        panel.Controls.Clear()

        ' Make panel slightly bigger (optional but recommended)
        panel.Height = 450

        ' =========================
        ' ===== TITLE =====
        ' =========================
        Dim title As New Label With {
            .Text = "Sales Trend",
            .Font = New Font("Segoe UI", 13, FontStyle.Bold),
            .ForeColor = Color.FromArgb(80, 90, 100),
            .BackColor = Color.Transparent,
            .AutoSize = True,
            .Location = New Point(20, 18)
        }

        panel.Controls.Add(title)

        ' Divider
        Dim lineTop As New Panel With {
            .Size = New Size(panel.Width - 40, 1),
            .Location = New Point(20, 50),
            .BackColor = Color.FromArgb(210, 210, 210)
        }

        panel.Controls.Add(lineTop)

        AddHandler panel.Paint, Sub(s, e)

                                    ' Background
                                    PanelRenderer.Draw(panel, e, True)

                                    ' Graph
                                    DrawSalesGraph(s, e)

                                    ' Border
                                    PanelRenderer.Draw(panel, e, False)

                                End Sub
    End Sub


    ' =========================
    ' ===== GRAPH DRAW =====
    ' =========================
    Private Shared Sub DrawSalesGraph(sender As Object, e As PaintEventArgs)

        Dim panel As Panel = CType(sender, Panel)
        Dim g = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias

        ' More breathing space
        Dim leftMargin As Integer = 70
        Dim bottomMargin As Integer = 60
        Dim topMargin As Integer = 70
        Dim rightMargin As Integer = 40

        Dim rect As Rectangle = panel.ClientRectangle

        Dim graphWidth = rect.Width - leftMargin - rightMargin
        Dim graphHeight = rect.Height - topMargin - bottomMargin

        ' DATA
        Dim values() As Integer = {8000, 11000, 14000, 12000, 13500, 16000, 14000, 17500}
        Dim days() As String = {"Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"}

        Dim maxVal As Integer = 20000

        ' GRID
        Dim ySteps() As Integer = {20000, 15000, 10000, 5000}

        For Each stepVal In ySteps

            Dim y = topMargin + graphHeight - (stepVal / maxVal * graphHeight)

            g.DrawLine(New Pen(Color.FromArgb(220, 220, 220)),
                       CSng(leftMargin), CSng(y),
                       CSng(leftMargin + graphWidth), CSng(y))

            Dim lbl As String = (stepVal / 1000).ToString() & "k"

            g.DrawString(lbl,
                         New Font("Segoe UI", 9), ' bigger
                         New SolidBrush(Color.FromArgb(120, 130, 140)),
                         20, y - 10)
        Next

        ' LINE (slightly thicker)
        Dim pts As New List(Of PointF)

        For i = 0 To values.Length - 1
            Dim x = leftMargin + (i / (values.Length - 1)) * graphWidth
            Dim y = topMargin + graphHeight - (values(i) / maxVal * graphHeight)
            pts.Add(New PointF(x, y))
        Next

        g.DrawLines(New Pen(Color.FromArgb(120, 160, 180), 3), pts.ToArray())

        ' POINTS (slightly bigger)
        For Each p In pts
            g.FillEllipse(New SolidBrush(Color.FromArgb(120, 160, 180)),
                          p.X - 5, p.Y - 5, 10, 10)
        Next

        ' X LABELS (bigger + lower)
        For i = 0 To days.Length - 1
            Dim x = leftMargin + (i / (days.Length - 1)) * graphWidth

            g.DrawString(days(i),
                         New Font("Segoe UI", 9),
                         New SolidBrush(Color.FromArgb(120, 130, 140)),
                         x - 12, rect.Height - 40)
        Next

    End Sub

End Class