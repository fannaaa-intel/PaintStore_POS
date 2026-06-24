Imports System.Drawing
Imports System.Windows.Forms

Public Class PaymentPanelBuilder

    Public Shared Sub LoadPayment(panel As Panel)

        panel.Controls.Clear()

        ' Balanced height for 2 rows
        panel.Height = 160

        ' =========================
        ' ===== TITLE =====
        ' =========================
        Dim payTitle As New Label With {
            .Text = "Payment Breakdown",
            .Font = New Font("Segoe UI", 12, FontStyle.Bold),
            .ForeColor = Color.FromArgb(90, 100, 110),
            .BackColor = Color.Transparent,
            .AutoSize = True,
            .Location = New Point(20, 18)
        }

        panel.Controls.Add(payTitle)

        ' Divider
        Dim payLine As New Panel With {
            .Size = New Size(panel.Width - 40, 1),
            .Location = New Point(20, 50),
            .BackColor = Color.FromArgb(210, 210, 210)
        }

        panel.Controls.Add(payLine)

        ' =========================
        ' ===== ROWS =====
        ' =========================
        Dim y As Integer = 75

        AddRow(panel, y, "Cash", "₱8,500", 0.85F, Color.FromArgb(150, 180, 195))
        AddRow(panel, y, "GCash", "₱5,200", 0.7F, Color.FromArgb(150, 185, 165))

    End Sub


    Private Shared Sub AddRow(panelParent As Panel,
                              ByRef y As Integer,
                              label As String,
                              value As String,
                              percent As Single,
                              barColor As Color)

        Dim rowHeight As Integer = 40

        Dim lblName As New Label With {
            .Text = label,
            .Font = New Font("Segoe UI", 11, FontStyle.Bold),
            .ForeColor = Color.FromArgb(70, 80, 90),
            .AutoSize = True,
            .Location = New Point(20, y),
            .BackColor = Color.Transparent
        }

        panelParent.Controls.Add(lblName)

        Dim lblValue As New Label With {
            .Text = value,
            .Font = New Font("Segoe UI", 11, FontStyle.Bold),
            .ForeColor = Color.FromArgb(110, 120, 130),
            .AutoSize = True,
            .BackColor = Color.Transparent
        }

        lblValue.Location = New Point(panelParent.Width - lblValue.Width - 20, y)
        panelParent.Controls.Add(lblValue)

        Dim barWidth As Integer = panelParent.Width - 40

        Dim barBg As New Panel With {
            .Size = New Size(barWidth, 10),
            .Location = New Point(20, y + 22),
            .BackColor = Color.FromArgb(220, 225, 230)
        }

        panelParent.Controls.Add(barBg)

        Dim barFill As New Panel With {
            .Size = New Size(CInt(barWidth * percent), 10),
            .Location = barBg.Location,
            .BackColor = barColor
        }

        panelParent.Controls.Add(barFill)
        barFill.BringToFront()

        y += rowHeight

    End Sub

End Class