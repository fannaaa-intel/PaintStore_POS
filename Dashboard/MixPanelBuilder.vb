Imports System.Drawing
Imports System.Windows.Forms

Public Class MixPanelBuilder

    Public Shared Sub LoadMix(panel As Panel)

        panel.Controls.Clear()

        ' Grow upward instead of downward
        Dim oldHeight As Integer = panel.Height
        Dim newHeight As Integer = 170


        panel.Height = newHeight

        ' =========================
        ' ===== TITLE =====
        ' =========================
        Dim title As New Label With {
        .Text = "Color Mixing Records",
        .Font = New Font("Segoe UI", 12, FontStyle.Bold), ' upgraded from 9
        .ForeColor = Color.FromArgb(80, 90, 100),
        .BackColor = Color.Transparent,
        .AutoSize = True,
        .Location = New Point(14, 14)
    }

        panel.Controls.Add(title)

        ' Divider
        Dim lineTop As New Panel With {
        .Size = New Size(panel.Width - 28, 1),
        .Location = New Point(14, 38),
        .BackColor = Color.FromArgb(210, 210, 210)
    }

        panel.Controls.Add(lineTop)

        ' =========================
        ' ===== ITEMS =====
        ' =========================
        Dim y As Integer = 50

        AddItem(panel, y, "Soft Green Formula", Color.FromArgb(140, 180, 160))
        AddItem(panel, y, "Sky Blue Mix", Color.FromArgb(130, 170, 200))

    End Sub


    Private Shared Sub AddItem(panelParent As Panel,
                           ByRef y As Integer,
                           text As String,
                           boxColor As Color)

        Dim rowHeight As Integer = 32

        ' Color box (slightly bigger)
        Dim colorBox As New Panel With {
        .Size = New Size(14, 14),
        .BackColor = boxColor,
        .Location = New Point(16, y + 6)
    }

        panelParent.Controls.Add(colorBox)

        ' Text (bigger font)
        Dim lbl As New Label With {
        .Text = text,
        .Font = New Font("Segoe UI", 12, FontStyle.Bold),
        .ForeColor = Color.FromArgb(90, 100, 110),
        .BackColor = Color.Transparent,
        .AutoSize = True,
        .Location = New Point(40, y + 5)
    }

        panelParent.Controls.Add(lbl)

        ' Divider
        Dim line As New Panel With {
        .Size = New Size(panelParent.Width - 28, 1),
        .Location = New Point(14, y + rowHeight),
        .BackColor = Color.FromArgb(225, 225, 225)
    }

        panelParent.Controls.Add(line)

        y += rowHeight

    End Sub

    Private Sub AddMixItem(panelParent As Panel,
                     ByRef y As Integer,
                     text As String,
                     boxColor As Color)

        ' 🎨 COLOR BOX
        Dim colorBox As New Panel()
        colorBox.Size = New Size(12, 12)
        colorBox.BackColor = boxColor
        colorBox.Location = New Point(15, y + 3)
        panelParent.Controls.Add(colorBox)

        ' 📝 TEXT
        Dim lbl As New Label()
        lbl.Text = text
        lbl.Font = New Font("Segoe UI", 11, FontStyle.Bold)
        lbl.ForeColor = Color.FromArgb(90, 100, 110)
        lbl.BackColor = Color.Transparent
        lbl.AutoSize = True
        lbl.Location = New Point(35, y)
        panelParent.Controls.Add(lbl)

        ' ➖ DIVIDER
        Dim line As New Panel()
        line.Size = New Size(panelParent.Width - 24, 1)
        line.Location = New Point(12, y + 22)
        line.BackColor = Color.FromArgb(225, 225, 225)
        panelParent.Controls.Add(line)

        y += 28
    End Sub

End Class