Imports System.Drawing
Imports System.Windows.Forms

Public Class DashboardCards

    Public Shared Sub LoadCards(
    TotalSales As Panel,
    tran_saction As Panel,
    avg_order_value As Panel,
    sales_trend As Panel,
    MintPrimary As Color,
    MintDarkTop As Color
)

        ' Optional: make panels taller (you can also set this in designer)
        TotalSales.Height = 130
        tran_saction.Height = 130
        avg_order_value.Height = 130
        sales_trend.Height = 130


        ' =========================
        ' ===== TODAY SALES =====
        ' =========================
        TotalSales.Controls.Clear()

        Dim tsTitle As New Label With {
        .Text = "Today's Sales",
        .Font = New Font("Segoe UI", 12, FontStyle.Bold),
        .ForeColor = Color.FromArgb(80, 90, 100),
        .BackColor = Color.Transparent,
        .AutoSize = True,
        .Location = New Point(20, 20)
    }

        Dim tsValue As New Label With {
        .Text = "₱18,750",
        .Font = New Font("Segoe UI", 26, FontStyle.Bold),
        .ForeColor = MintDarkTop,
        .BackColor = Color.Transparent,
        .AutoSize = True,
        .Location = New Point(20, 65)
    }

        TotalSales.Controls.Add(tsTitle)
        TotalSales.Controls.Add(tsValue)


        ' =========================
        ' ===== TRANSACTIONS =====
        ' =========================
        tran_saction.Controls.Clear()

        Dim trTitle As New Label With {
        .Text = "Transactions",
        .Font = New Font("Segoe UI", 12, FontStyle.Bold),
        .ForeColor = Color.FromArgb(80, 90, 100),
        .BackColor = Color.Transparent,
        .AutoSize = True,
        .Location = New Point(20, 20)
    }

        Dim trValue As New Label With {
        .Text = "32",
        .Font = New Font("Segoe UI", 26, FontStyle.Bold),
        .ForeColor = MintDarkTop,
        .BackColor = Color.Transparent,
        .AutoSize = True,
        .Location = New Point(20, 65)
    }

        tran_saction.Controls.Add(trTitle)
        tran_saction.Controls.Add(trValue)


        ' =========================
        ' ===== AVG ORDER VALUE =====
        ' =========================
        avg_order_value.Controls.Clear()

        Dim avgTitle As New Label With {
        .Text = "Avg. Order Value",
        .BackColor = Color.Transparent,
        .Font = New Font("Segoe UI", 12, FontStyle.Bold),
        .ForeColor = Color.FromArgb(80, 90, 100),
        .AutoSize = True,
        .Location = New Point(20, 20)
    }

        Dim avgValue As New Label With {
        .Text = "₱585",
        .Font = New Font("Segoe UI", 26, FontStyle.Bold),
        .ForeColor = MintDarkTop,
        .BackColor = Color.Transparent,
        .AutoSize = True,
        .Location = New Point(20, 65)
    }

        avg_order_value.Controls.Add(avgTitle)
        avg_order_value.Controls.Add(avgValue)


        ' =========================
        ' ===== SALES TREND =====
        ' =========================
        sales_trend.Controls.Clear()

        Dim stTitle As New Label With {
        .Text = "Sales Trend",
        .BackColor = Color.Transparent,
        .Font = New Font("Segoe UI", 12, FontStyle.Bold),
        .ForeColor = Color.FromArgb(80, 90, 100),
        .AutoSize = True,
        .Location = New Point(20, 20)
    }

        Dim arrow As New PictureBox With {
        .Size = New Size(24, 24),
        .Location = New Point(20, 68),
        .BackColor = Color.Transparent
    }

        If IO.File.Exists("C:\Users\DELL\source\repos\PaintStore_POS\arrow.png") Then
            arrow.Image = Image.FromFile("C:\Users\DELL\source\repos\PaintStore_POS\arrow.png")
            arrow.SizeMode = PictureBoxSizeMode.StretchImage
        End If

        Dim stValue As New Label With {
        .Text = "+5.2%",
        .Font = New Font("Segoe UI", 18, FontStyle.Bold),
        .ForeColor = MintPrimary,
        .AutoSize = True,
        .BackColor = Color.Transparent,
        .Location = New Point(55, 68)
    }

        sales_trend.Controls.Add(stTitle)
        sales_trend.Controls.Add(arrow)
        sales_trend.Controls.Add(stValue)

    End Sub

End Class