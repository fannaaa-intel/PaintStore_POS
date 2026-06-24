Imports System.Drawing
Imports System.Windows.Forms

Public Class InventoryPanelBuilder

    Public Shared Sub LoadInventory(panel As Panel)

        panel.Controls.Clear()

        ' =========================
        ' ===== TITLE =====
        ' =========================
        Dim invTitle As New Label With {
            .Text = "Fast Moving Today",
            .Font = New Font("Segoe UI", 14, FontStyle.Bold),
            .BackColor = Color.Transparent,
            .ForeColor = Color.FromArgb(80, 90, 100),
            .AutoSize = True,
            .Location = New Point(20, 18)
        }

        panel.Controls.Add(invTitle)

        ' Divider
        Dim topLine As New Panel With {
            .Size = New Size(panel.Width - 2, 1),
            .Location = New Point(1, 52),
            .BackColor = Color.FromArgb(210, 210, 210)
        }

        panel.Controls.Add(topLine)

        ' Subtitle
        Dim subTitle As New Label With {
            .Text = "Based on today's sales",
            .Font = New Font("Segoe UI", 12, FontStyle.Bold),
            .ForeColor = Color.FromArgb(120, 130, 140),
            .BackColor = Color.Transparent,
            .AutoSize = True,
            .Location = New Point(18, 66)
        }

        panel.Controls.Add(subTitle)

        ' =========================
        ' ===== ITEMS =====
        ' =========================
        Dim y As Integer = 92

        ' name, sold count, color
        AddFastMovingItem(panel, y, "Ocean Breeze", 15, Color.FromArgb(122, 163, 186), True)
        AddFastMovingItem(panel, y, "Soft Sage", 12, Color.FromArgb(133, 186, 147))
        AddFastMovingItem(panel, y, "Charcoal Gray", 10, Color.FromArgb(140, 140, 140))
        AddFastMovingItem(panel, y, "Warm Ivory", 9, Color.FromArgb(200, 190, 170))
        AddFastMovingItem(panel, y, "Sunny Yellow", 8, Color.FromArgb(229, 190, 120))

    End Sub


    Private Shared Sub AddFastMovingItem(panel As Panel,
                                         ByRef y As Integer,
                                         name As String,
                                         sold As Integer,
                                         barColor As Color,
                                         Optional highlight As Boolean = False)

        Dim rowHeight As Integer = 55

        Dim row As New Panel With {
            .Size = New Size(panel.Width - 28, rowHeight),
            .Location = New Point(14, y),
            .BackColor = If(highlight, Color.FromArgb(235, 245, 250), Color.Transparent)
        }

        ' Product Name
        Dim lblName As New Label With {
            .Text = name,
            .Font = New Font("Segoe UI", 12, FontStyle.Bold),
            .ForeColor = Color.FromArgb(90, 100, 110),
            .AutoSize = True,
            .Location = New Point(10, 6)
        }

        ' Sold Count (right side)
        Dim lblSold As New Label With {
            .Text = sold.ToString() & " sold",
            .Font = New Font("Segoe UI", 12, FontStyle.Regular),
            .ForeColor = Color.FromArgb(110, 120, 130),
            .AutoSize = True
        }

        lblSold.Location = New Point(row.Width - lblSold.Width - 10, 6)

        ' Progress Background
        Dim barBg As New Panel With {
            .Size = New Size(row.Width - 20, 8),
            .Location = New Point(10, 28),
            .BackColor = Color.FromArgb(220, 225, 230)
        }

        ' Progress Fill (relative width)
        Dim maxValue As Integer = 15 ' highest in list
        Dim fillWidth As Integer = CInt((sold / maxValue) * barBg.Width)

        Dim barFill As New Panel With {
            .Size = New Size(fillWidth, 8),
            .Location = New Point(0, 0),
            .BackColor = barColor
        }

        barBg.Controls.Add(barFill)

        row.Controls.Add(lblName)
        row.Controls.Add(lblSold)
        row.Controls.Add(barBg)

        panel.Controls.Add(row)

        ' Divider
        If Not highlight Then
            Dim line As New Panel With {
                .Size = New Size(panel.Width - 40, 1),
                .Location = New Point(20, y + rowHeight),
                .BackColor = Color.FromArgb(225, 225, 225)
            }
            panel.Controls.Add(line)
        End If

        y += rowHeight + 6

    End Sub

End Class