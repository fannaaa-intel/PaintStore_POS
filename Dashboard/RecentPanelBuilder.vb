Imports System.Drawing
Imports System.Windows.Forms

Public Class RecentPanelBuilder

    Public Shared Sub LoadRecent(panel As Panel)

        panel.Controls.Clear()

        ' =========================
        ' ===== TITLE =====
        ' =========================
        Dim title As New Label With {
            .Text = "Recent Transactions",
            .Font = New Font("Segoe UI", 14, FontStyle.Bold),
            .ForeColor = Color.FromArgb(80, 90, 100),
            .BackColor = Color.Transparent,
            .AutoSize = True,
            .Location = New Point(15, 15)
        }
        panel.Controls.Add(title)

        ' Top line.Font = New Font
        Dim lineTop As New Panel With {
            .Size = New Size(panel.Width - 30, 1),
            .Location = New Point(15, 40),
            .BackColor = Color.FromArgb(210, 210, 210)
        }
        panel.Controls.Add(lineTop)

        ' Header
        Dim y As Integer = 50
        AddHeader(panel, y)

        Dim lineHeader As New Panel With {
            .Size = New Size(panel.Width - 30, 1),
            .Location = New Point(15, y + 20),
            .BackColor = Color.FromArgb(200, 200, 200)
        }
        panel.Controls.Add(lineHeader)

        y += 25

        ' Rows
        AddTransactionRow(panel, y, "11:45 AM", "Reyes Hardware", "Cash", "₱1,200")
        AddTransactionRow(panel, y, "11:20 AM", "Maria Santos", "GCash", "₱850")
        AddTransactionRow(panel, y, "10:50 AM", "Juan Dela Cruz", "Card", "₱2,100")
        AddTransactionRow(panel, y, "10:15 AM", "Carla Gomez", "Cash", "₱500")

    End Sub

    Private Shared Sub AddHeader(panelParent As Panel, y As Integer)
        AddHeaderLabel(panelParent, "Time", 15, y)
        AddHeaderLabel(panelParent, "Customer", 90, y)
        AddHeaderLabel(panelParent, "Payment Method", 220, y)
        AddHeaderLabel(panelParent, "Amount", panelParent.Width - 90, y)
    End Sub

    Private Shared Sub AddHeaderLabel(panelParent As Panel, text As String, x As Integer, y As Integer)
        Dim lbl As New Label With {
            .Text = text,
            .Font = New Font("Segoe UI", 11, FontStyle.Bold),
            .ForeColor = Color.FromArgb(120, 130, 140),
            .BackColor = Color.Transparent,
            .AutoSize = True,
            .Location = New Point(x, y)
        }
        panelParent.Controls.Add(lbl)
    End Sub

    Private Shared Sub AddTransactionRow(panelParent As Panel,
                                         ByRef y As Integer,
                                         time As String,
                                         customer As String,
                                         payment As String,
                                         amount As String)

        AddRowLabel(panelParent, time, 15, y)
        AddRowLabel(panelParent, customer, 90, y)
        AddRowLabel(panelParent, payment, 220, y)

        Dim lblAmount As New Label With {
            .Text = amount,
            .Font = New Font("Segoe UI", 12, FontStyle.Bold),
            .ForeColor = Color.FromArgb(70, 80, 90),
            .BackColor = Color.Transparent,
            .AutoSize = True,
            .Location = New Point(panelParent.Width - 80, y)
        }
        panelParent.Controls.Add(lblAmount)

        Dim line As New Panel With {
            .Size = New Size(panelParent.Width - 30, 1),
            .Location = New Point(15, y + 24),
            .BackColor = Color.FromArgb(225, 225, 225)
        }
        panelParent.Controls.Add(line)

        y += 32
    End Sub

    Private Shared Sub AddRowLabel(panelParent As Panel, text As String, x As Integer, y As Integer)
        Dim lbl As New Label With {
            .Text = text,
            .Font = New Font("Segoe UI", 12, FontStyle.Bold),
            .ForeColor = Color.FromArgb(90, 100, 110),
            .BackColor = Color.Transparent,
            .AutoSize = True,
            .Location = New Point(x, y)
        }
        panelParent.Controls.Add(lbl)
    End Sub

End Class