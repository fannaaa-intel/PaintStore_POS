Imports System.Drawing
Imports System.Windows.Forms

Public Class CategoriesPanelBuilder

    Public Shared Sub LoadCategories(panel As Panel)

        panel.Controls.Clear()

        ' Grow upward instead of downward
        Dim oldHeight As Integer = panel.Height
        Dim newHeight As Integer = 170


        panel.Height = newHeight

        ' =========================
        ' ===== TITLE =====
        ' =========================
        Dim title As New Label With {
        .Text = "Paint Categories",
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

        ' =========================
        ' ===== ITEMS =====
        ' =========================
        Dim y As Integer = 65

        AddItem(panel, y, "Latex Paints")
        AddItem(panel, y, "Enamel Paints")
        AddItem(panel, y, "Wood Coatings")

    End Sub


    Private Shared Sub AddItem(panelParent As Panel,
                           ByRef y As Integer,
                           text As String)

        Dim rowHeight As Integer = 24 ' reduced from 40

        ' Bullet (slightly smaller to match)
        Dim bullet As New Panel With {
        .Size = New Size(8, 8),
        .BackColor = Color.FromArgb(120, 130, 140),
        .Location = New Point(25, y + 8)
    }

        panelParent.Controls.Add(bullet)

        ' Text (slightly reduced font)
        Dim lbl As New Label With {
        .Text = text,
        .Font = New Font("Segoe UI", 11, FontStyle.Bold), ' was 11
        .ForeColor = Color.FromArgb(90, 100, 110),
        .BackColor = Color.Transparent,
        .AutoSize = True,
        .Location = New Point(45, y + 5)
    }

        panelParent.Controls.Add(lbl)

        ' Divider (adjusted position)
        Dim line As New Panel With {
        .Size = New Size(panelParent.Width - 40, 1),
        .Location = New Point(20, y + rowHeight),
        .BackColor = Color.FromArgb(225, 225, 225)
    }

        panelParent.Controls.Add(line)

        y += rowHeight + 2 ' tighter spacing

    End Sub

End Class