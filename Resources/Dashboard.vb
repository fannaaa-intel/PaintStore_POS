Imports System.Drawing.Drawing2D

Public Class Dashboard

    Private MintPrimary As Color = Color.FromArgb(0, 200, 170)
    Private MintDarkTop As Color = Color.FromArgb(0, 120, 110)
    Private MintMid As Color = Color.FromArgb(0, 170, 150)
    Private MintLight As Color = Color.FromArgb(220, 255, 245)
    Private MintGlow As Color = Color.FromArgb(0, 255, 200)
    Private MintActive As Color = Color.FromArgb(140, 0, 200, 170)

    Private ActiveIndicator As Panel
    Private MainContainer As Panel

    Private _navBackgroundCache As Bitmap
    Private _mainBackgroundCache As Bitmap

    Private Sub Nav_Paint(sender As Object, e As PaintEventArgs) Handles Nav.Paint
        Dim rect As Rectangle = Nav.ClientRectangle
        If rect.Width <= 0 OrElse rect.Height <= 0 Then Return

        If _navBackgroundCache Is Nothing OrElse
           _navBackgroundCache.Width <> rect.Width OrElse
           _navBackgroundCache.Height <> rect.Height Then

            If _navBackgroundCache IsNot Nothing Then _navBackgroundCache.Dispose()
            _navBackgroundCache = New Bitmap(rect.Width, rect.Height)

            Using g As Graphics = Graphics.FromImage(_navBackgroundCache)
                g.SmoothingMode = SmoothingMode.AntiAlias

                Using brush As New LinearGradientBrush(rect, MintDarkTop, MintLight, 60.0F)
                    Dim blend As New ColorBlend()
                    blend.Colors = {MintDarkTop, MintMid, MintLight}
                    blend.Positions = {0.0F, 0.4F, 1.0F}
                    brush.InterpolationColors = blend
                    g.FillRectangle(brush, rect)
                End Using
            End Using
        End If

        e.Graphics.DrawImageUnscaled(_navBackgroundCache, 0, 0)
    End Sub

    Private Sub bgPanel_Paint(sender As Object, e As PaintEventArgs)
        Dim pnl = CType(sender, Panel)
        Dim rect = pnl.ClientRectangle
        If rect.Width <= 0 OrElse rect.Height <= 0 Then Return

        If _mainBackgroundCache Is Nothing OrElse
           _mainBackgroundCache.Width <> rect.Width OrElse
           _mainBackgroundCache.Height <> rect.Height Then

            If _mainBackgroundCache IsNot Nothing Then _mainBackgroundCache.Dispose()
            _mainBackgroundCache = New Bitmap(rect.Width, rect.Height)

            Using g As Graphics = Graphics.FromImage(_mainBackgroundCache)
                g.SmoothingMode = SmoothingMode.AntiAlias

                Using brush As New LinearGradientBrush(rect,
                    Color.FromArgb(220, 240, 235),
                    Color.FromArgb(185, 215, 210), 90.0F)

                    g.FillRectangle(brush, rect)
                End Using
            End Using
        End If

        e.Graphics.DrawImageUnscaled(_mainBackgroundCache, 0, 0)
    End Sub

    Private Sub Dashboard_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        Me.SetStyle(ControlStyles.AllPaintingInWmPaint Or
                    ControlStyles.OptimizedDoubleBuffer Or
                    ControlStyles.ResizeRedraw Or
                    ControlStyles.UserPaint, True)
        Me.DoubleBuffered = True
        Me.UpdateStyles()

        Me.SuspendLayout()
        Nav.SuspendLayout()

        ' NAV
        Nav.Dock = DockStyle.Left
        Nav.Width = 120

        ' MAIN CONTAINER
        MainContainer = New SmoothPanel With {
            .Dock = DockStyle.Fill,
            .BackColor = Color.FromArgb(220, 240, 235)
        }

        If Not Me.Controls.Contains(MainContainer) Then
            Me.Controls.Add(MainContainer)
        End If

        Nav.BringToFront()

        ' INDICATOR
        ActiveIndicator = New SmoothPanel With {
            .Width = 5,
            .Height = 50,
            .BackColor = MintPrimary
        }
        Nav.Controls.Add(ActiveIndicator)

        ' BUTTONS
        StyleButton(home, "home.png")
        StyleButton(transaction, "transaction.png")
        StyleButton(sales, "sales.png")
        StyleButton(Inventory, "inventory.png")
        StyleButton(Reports, "report.png")
        StyleButton(Settings, "settings.png")
        StyleButton(logout, "logout.png")

        Nav.ResumeLayout()
        Me.ResumeLayout()

        ' LOAD DEFAULT SCREEN
        LoadDashboardScreen()

        SetActive(home)
    End Sub

    Private Sub LoadDashboardScreen()
        MainContainer.SuspendLayout()
        MainContainer.Controls.Clear()

        If _mainBackgroundCache IsNot Nothing Then
            _mainBackgroundCache.Dispose()
            _mainBackgroundCache = Nothing
        End If

        Dim PanelDashboard As New SmoothPanel With {
            .Dock = DockStyle.Fill,
            .BackColor = Color.FromArgb(220, 240, 235)
        }

        PanelDashboard.SuspendLayout()
        MainContainer.Controls.Add(PanelDashboard)

        PanelDashboard.Controls.AddRange({
            TotalSales, tran_saction, avg_order_value, sales_trend,
            Inventory_Stats, Analytics, Top_Selling,
            Payment, Mix, Categories, Recent
        })

        Dim bgPanel As New SmoothPanel With {
            .Name = "bgPanel",
            .Dock = DockStyle.Fill,
            .BackColor = Color.FromArgb(220, 240, 235)
        }
        AddHandler bgPanel.Paint, AddressOf bgPanel_Paint
        PanelDashboard.Controls.Add(bgPanel)
        bgPanel.SendToBack()

        DashboardCards.LoadCards(TotalSales, tran_saction, avg_order_value, sales_trend, MintPrimary, MintDarkTop)
        InventoryPanelBuilder.LoadInventory(Inventory_Stats)
        AnalyticsPanelBuilder.LoadAnalytics(Analytics)
        TopSellingPanelBuilder.LoadTopSelling(Top_Selling)
        PaymentPanelBuilder.LoadPayment(Payment)
        MixPanelBuilder.LoadMix(Mix)
        CategoriesPanelBuilder.LoadCategories(Categories)
        RecentPanelBuilder.LoadRecent(Recent)

        sales_trend.Padding = New Padding(3)

        AddHandler sales_trend.Paint,
            Sub(s, ev)
                PanelRenderer.Draw(CType(s, Panel), ev)
            End Sub

        For Each ctrl As Control In PanelDashboard.Controls
            If TypeOf ctrl Is Panel AndAlso
               ctrl.Name <> "bgPanel" AndAlso
               ctrl.Name <> "Analytics" Then

                AddHandler ctrl.Paint,
                    Sub(s, ev)
                        PanelRenderer.Draw(CType(s, Panel), ev, True)
                    End Sub
            End If
        Next

        PanelDashboard.ResumeLayout()
        MainContainer.ResumeLayout()
        MainContainer.Refresh()
    End Sub

    Private Sub StyleButton(btn As Button, imagePath As String)
        btn.Parent = Nav

        If IO.File.Exists(imagePath) Then
            btn.Image = New Bitmap(Image.FromFile(imagePath), New Size(24, 24))
        End If

        btn.Font = New Font("Segoe UI", 9, FontStyle.Bold)
        btn.ForeColor = Color.White
        btn.TextImageRelation = TextImageRelation.ImageAboveText
        btn.Padding = New Padding(0, 10, 0, 6)
        btn.FlatStyle = FlatStyle.Flat
        btn.FlatAppearance.BorderSize = 0
        btn.Width = Nav.Width
        btn.Height = 70
        btn.UseVisualStyleBackColor = False
        btn.BackColor = Color.Transparent

        AddHandler btn.MouseEnter,
            Sub()
                If btn.Tag <> "active" Then
                    btn.BackColor = Color.FromArgb(90, 0, 200, 170)
                End If
            End Sub

        AddHandler btn.MouseLeave,
            Sub()
                If btn.Tag <> "active" Then
                    btn.BackColor = Color.Transparent
                End If
            End Sub
    End Sub

    Private Sub SetActive(btn As Button)
        For Each ctrl As Control In Nav.Controls
            If TypeOf ctrl Is Button Then
                ctrl.BackColor = Color.Transparent
                ctrl.Tag = ""
            End If
        Next

        btn.BackColor = MintActive
        btn.Tag = "active"

        ActiveIndicator.Height = btn.Height
        ActiveIndicator.Top = btn.Top
        ActiveIndicator.BackColor = MintGlow
        ActiveIndicator.BringToFront()
    End Sub

    Private Sub home_Click(sender As Object, e As EventArgs) Handles home.Click
        SetActive(home)
        LoadDashboardScreen()
    End Sub

    Private Sub transaction_Click(sender As Object, e As EventArgs) Handles transaction.Click
        SetActive(transaction)
        MainContainer.SuspendLayout()
        MainContainer.Controls.Clear()
        MainContainer.ResumeLayout()
        MainContainer.Refresh()
    End Sub

    Private Sub sales_Click(sender As Object, e As EventArgs) Handles sales.Click
        SetActive(sales)
        MainContainer.SuspendLayout()
        MainContainer.Controls.Clear()
        MainContainer.ResumeLayout()
        MainContainer.Refresh()
    End Sub

    Private Sub Inventory_Click(sender As Object, e As EventArgs) Handles Inventory.Click
        SetActive(Inventory)
        MainContainer.SuspendLayout()
        MainContainer.Controls.Clear()
        MainContainer.ResumeLayout()
        MainContainer.Refresh()
    End Sub

    Private Sub Reports_Click(sender As Object, e As EventArgs) Handles Reports.Click
        SetActive(Reports)
        MainContainer.SuspendLayout()
        MainContainer.Controls.Clear()
        MainContainer.ResumeLayout()
        MainContainer.Refresh()
    End Sub

    Private Sub Settings_Click(sender As Object, e As EventArgs) Handles Settings.Click
        SetActive(Settings)
        MainContainer.SuspendLayout()
        MainContainer.Controls.Clear()
        MainContainer.ResumeLayout()
        MainContainer.Refresh()
    End Sub

    Protected Overrides Sub OnFormClosed(e As FormClosedEventArgs)
        If _navBackgroundCache IsNot Nothing Then _navBackgroundCache.Dispose()
        If _mainBackgroundCache IsNot Nothing Then _mainBackgroundCache.Dispose()
        MyBase.OnFormClosed(e)
    End Sub

    Private Sub logout_Click(sender As Object, e As EventArgs) Handles logout.Click
        Dim f As New LogIn
        f.Show()
        Me.Hide()
    End Sub
End Class