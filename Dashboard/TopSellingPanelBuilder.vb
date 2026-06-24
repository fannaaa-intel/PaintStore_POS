Imports System.Drawing
Imports System.Windows.Forms

Public Class TopSellingPanelBuilder

    Public Shared Sub LoadTopSelling(panel As Panel)

        panel.Controls.Clear()

        ' Slightly taller for bigger content
        panel.Height = 455

        ' =========================
        ' ===== TITLE =====
        ' =========================
        Dim titleTop As New Label With {
            .Text = "Top Selling Products",
            .Font = New Font("Segoe UI", 14, FontStyle.Bold), ' bigger
            .ForeColor = Color.FromArgb(90, 100, 110),
            .BackColor = Color.Transparent,
            .AutoSize = True,
            .Location = New Point(18, 18)
        }

        panel.Controls.Add(titleTop)

        ' Divider
        Dim lineTopSell As New Panel With {
            .Size = New Size(panel.Width - 2, 1),
            .Location = New Point(1, 50),
            .BackColor = Color.FromArgb(210, 210, 210)
        }

        panel.Controls.Add(lineTopSell)

        ' =========================
        ' ===== ITEMS =====
        ' =========================
        Dim y As Integer = 65

        AddItem(panel, y, "1.", "Ocean Breeze", Color.FromArgb(140, 180, 200))
        AddItem(panel, y, "2.", "Soft Sage", Color.FromArgb(150, 180, 160))
        AddItem(panel, y, "3.", "Warm Ivory", Color.FromArgb(220, 210, 195))
        AddItem(panel, y, "4.", "Charcoal Gray", Color.FromArgb(110, 110, 110))
        AddItem(panel, y, "5.", "Sunny Yellow", Color.FromArgb(235, 200, 130))

    End Sub


    Private Shared Sub AddItem(panelParent As Panel,
                               ByRef y As Integer,
                               rank As String,
                               text As String,
                               barColor As Color)

        Dim rowHeight As Integer = 46 ' increased spacing

        ' Rank
        Dim lblRank As New Label With {
            .Text = rank,
            .Font = New Font("Segoe UI", 14, FontStyle.Bold), ' bigger
            .ForeColor = Color.FromArgb(90, 100, 110),
            .AutoSize = True,
            .BackColor = Color.Transparent,
            .Location = New Point(18, y + 4)
        }

        ' Name
        Dim lblName As New Label With {
            .Text = text,
            .Font = New Font("Segoe UI", 14, FontStyle.Bold), ' bigger
            .ForeColor = Color.FromArgb(90, 100, 110),
            .AutoSize = True,
            .BackColor = Color.Transparent,
            .Location = New Point(50, y + 4)
        }

        ' Color bar (slightly bigger)
        Dim bar As New Panel With {
            .Size = New Size(70, 20),
            .BackColor = barColor,
            .Location = New Point(panelParent.Width - 90, y + 4)
        }

        panelParent.Controls.Add(lblRank)
        panelParent.Controls.Add(lblName)
        panelParent.Controls.Add(bar)

        ' Divider
        Dim line As New Panel With {
            .Size = New Size(panelParent.Width - 30, 1),
            .Location = New Point(15, y + rowHeight),
            .BackColor = Color.FromArgb(225, 225, 225)
        }

        panelParent.Controls.Add(line)

        y += rowHeight

    End Sub

End Class
