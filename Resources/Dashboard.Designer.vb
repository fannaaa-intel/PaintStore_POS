<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class Dashboard
    Inherits System.Windows.Forms.Form

    'Form overrides dispose to clean up the component list.
    <System.Diagnostics.DebuggerNonUserCode()>
    Protected Overrides Sub Dispose(ByVal disposing As Boolean)
        Try
            If disposing AndAlso components IsNot Nothing Then
                components.Dispose()
            End If
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub

    'Required by the Windows Form Designer
    Private components As System.ComponentModel.IContainer

    'NOTE: The following procedure is required by the Windows Form Designer
    'It can be modified using the Windows Form Designer.  
    'Do not modify it using the code editor.
    <System.Diagnostics.DebuggerStepThrough()>
    Private Sub InitializeComponent()
        Me.Nav = New System.Windows.Forms.Panel()
        Me.logout = New System.Windows.Forms.Button()
        Me.transaction = New System.Windows.Forms.Button()
        Me.Settings = New System.Windows.Forms.Button()
        Me.Reports = New System.Windows.Forms.Button()
        Me.Inventory = New System.Windows.Forms.Button()
        Me.sales = New System.Windows.Forms.Button()
        Me.home = New System.Windows.Forms.Button()
        Me.TotalSales = New System.Windows.Forms.Panel()
        Me.tran_saction = New System.Windows.Forms.Panel()
        Me.avg_order_value = New System.Windows.Forms.Panel()
        Me.sales_trend = New System.Windows.Forms.Panel()
        Me.Inventory_Stats = New System.Windows.Forms.Panel()
        Me.Analytics = New System.Windows.Forms.Panel()
        Me.Top_Selling = New System.Windows.Forms.Panel()
        Me.Payment = New System.Windows.Forms.Panel()
        Me.Mix = New System.Windows.Forms.Panel()
        Me.Categories = New System.Windows.Forms.Panel()
        Me.Recent = New System.Windows.Forms.Panel()
        Me.Nav.SuspendLayout()
        Me.SuspendLayout()
        '
        'Nav
        '
        Me.Nav.Controls.Add(Me.logout)
        Me.Nav.Controls.Add(Me.transaction)
        Me.Nav.Controls.Add(Me.Mix)
        Me.Nav.Controls.Add(Me.Settings)
        Me.Nav.Controls.Add(Me.Payment)
        Me.Nav.Controls.Add(Me.Reports)
        Me.Nav.Controls.Add(Me.Inventory)
        Me.Nav.Controls.Add(Me.sales)
        Me.Nav.Controls.Add(Me.home)
        Me.Nav.Controls.Add(Me.TotalSales)
        Me.Nav.Dock = System.Windows.Forms.DockStyle.Left
        Me.Nav.Location = New System.Drawing.Point(0, 0)
        Me.Nav.Name = "Nav"
        Me.Nav.Size = New System.Drawing.Size(212, 1061)
        Me.Nav.TabIndex = 0
        '
        'logout
        '
        Me.logout.Location = New System.Drawing.Point(4, 511)
        Me.logout.Name = "logout"
        Me.logout.Size = New System.Drawing.Size(100, 23)
        Me.logout.TabIndex = 6
        Me.logout.Text = "Logout"
        Me.logout.UseVisualStyleBackColor = True
        '
        'transaction
        '
        Me.transaction.Location = New System.Drawing.Point(4, 115)
        Me.transaction.Name = "transaction"
        Me.transaction.Size = New System.Drawing.Size(100, 23)
        Me.transaction.TabIndex = 5
        Me.transaction.Text = "Transaction"
        Me.transaction.UseVisualStyleBackColor = True
        '
        'Settings
        '
        Me.Settings.Location = New System.Drawing.Point(4, 431)
        Me.Settings.Name = "Settings"
        Me.Settings.Size = New System.Drawing.Size(100, 23)
        Me.Settings.TabIndex = 4
        Me.Settings.Text = "Settings"
        Me.Settings.UseVisualStyleBackColor = True
        '
        'Reports
        '
        Me.Reports.Location = New System.Drawing.Point(4, 353)
        Me.Reports.Name = "Reports"
        Me.Reports.Size = New System.Drawing.Size(100, 23)
        Me.Reports.TabIndex = 3
        Me.Reports.Text = "Reports"
        Me.Reports.UseVisualStyleBackColor = True
        '
        'Inventory
        '
        Me.Inventory.Location = New System.Drawing.Point(4, 275)
        Me.Inventory.Name = "Inventory"
        Me.Inventory.Size = New System.Drawing.Size(100, 23)
        Me.Inventory.TabIndex = 2
        Me.Inventory.Text = "Inventory"
        Me.Inventory.UseVisualStyleBackColor = True
        '
        'sales
        '
        Me.sales.Location = New System.Drawing.Point(4, 195)
        Me.sales.Name = "sales"
        Me.sales.Size = New System.Drawing.Size(100, 23)
        Me.sales.TabIndex = 1
        Me.sales.Text = "Sales"
        Me.sales.UseVisualStyleBackColor = True
        '
        'home
        '
        Me.home.Location = New System.Drawing.Point(4, 36)
        Me.home.Name = "home"
        Me.home.Size = New System.Drawing.Size(104, 23)
        Me.home.TabIndex = 0
        Me.home.Text = "Dashboard"
        Me.home.UseVisualStyleBackColor = True
        '
        'TotalSales
        '
        Me.TotalSales.Location = New System.Drawing.Point(146, 36)
        Me.TotalSales.Name = "TotalSales"
        Me.TotalSales.Size = New System.Drawing.Size(292, 110)
        Me.TotalSales.TabIndex = 1
        '
        'tran_saction
        '
        Me.tran_saction.Location = New System.Drawing.Point(497, 36)
        Me.tran_saction.Name = "tran_saction"
        Me.tran_saction.Size = New System.Drawing.Size(301, 110)
        Me.tran_saction.TabIndex = 2
        '
        'avg_order_value
        '
        Me.avg_order_value.Location = New System.Drawing.Point(1199, 36)
        Me.avg_order_value.Name = "avg_order_value"
        Me.avg_order_value.Size = New System.Drawing.Size(319, 110)
        Me.avg_order_value.TabIndex = 3
        '
        'sales_trend
        '
        Me.sales_trend.Location = New System.Drawing.Point(852, 36)
        Me.sales_trend.Name = "sales_trend"
        Me.sales_trend.Size = New System.Drawing.Size(294, 110)
        Me.sales_trend.TabIndex = 4
        '
        'Inventory_Stats
        '
        Me.Inventory_Stats.Location = New System.Drawing.Point(1567, 36)
        Me.Inventory_Stats.Name = "Inventory_Stats"
        Me.Inventory_Stats.Size = New System.Drawing.Size(318, 601)
        Me.Inventory_Stats.TabIndex = 5
        '
        'Analytics
        '
        Me.Analytics.Location = New System.Drawing.Point(146, 186)
        Me.Analytics.Name = "Analytics"
        Me.Analytics.Size = New System.Drawing.Size(1000, 451)
        Me.Analytics.TabIndex = 6
        '
        'Top_Selling
        '
        Me.Top_Selling.Location = New System.Drawing.Point(1199, 186)
        Me.Top_Selling.Name = "Top_Selling"
        Me.Top_Selling.Size = New System.Drawing.Size(319, 454)
        Me.Top_Selling.TabIndex = 7
        '
        'Payment
        '
        Me.Payment.Location = New System.Drawing.Point(146, 674)
        Me.Payment.Name = "Payment"
        Me.Payment.Size = New System.Drawing.Size(1172, 178)
        Me.Payment.TabIndex = 8
        '
        'Mix
        '
        Me.Mix.Location = New System.Drawing.Point(146, 858)
        Me.Mix.Name = "Mix"
        Me.Mix.Size = New System.Drawing.Size(366, 153)
        Me.Mix.TabIndex = 9
        '
        'Categories
        '
        Me.Categories.Location = New System.Drawing.Point(554, 858)
        Me.Categories.Name = "Categories"
        Me.Categories.Size = New System.Drawing.Size(764, 153)
        Me.Categories.TabIndex = 10
        '
        'Recent
        '
        Me.Recent.Location = New System.Drawing.Point(1376, 674)
        Me.Recent.Name = "Recent"
        Me.Recent.Size = New System.Drawing.Size(509, 356)
        Me.Recent.TabIndex = 11
        '
        'Dashboard
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(6.0!, 13.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        Me.ClientSize = New System.Drawing.Size(1924, 1061)
        Me.Controls.Add(Me.Recent)
        Me.Controls.Add(Me.Categories)
        Me.Controls.Add(Me.Top_Selling)
        Me.Controls.Add(Me.Analytics)
        Me.Controls.Add(Me.Inventory_Stats)
        Me.Controls.Add(Me.avg_order_value)
        Me.Controls.Add(Me.sales_trend)
        Me.Controls.Add(Me.tran_saction)
        Me.Controls.Add(Me.Nav)
        Me.Name = "Dashboard"
        Me.Text = "Dashboard"
        Me.WindowState = System.Windows.Forms.FormWindowState.Maximized
        Me.Nav.ResumeLayout(False)
        Me.ResumeLayout(False)

    End Sub

    Friend WithEvents Nav As Panel
    Friend WithEvents Settings As Button
    Friend WithEvents Reports As Button
    Friend WithEvents Inventory As Button
    Friend WithEvents sales As Button
    Friend WithEvents home As Button
    Friend WithEvents transaction As Button
    Friend WithEvents TotalSales As Panel
    Friend WithEvents tran_saction As Panel
    Friend WithEvents avg_order_value As Panel
    Friend WithEvents sales_trend As Panel
    Friend WithEvents Inventory_Stats As Panel
    Friend WithEvents Analytics As Panel
    Friend WithEvents Top_Selling As Panel
    Friend WithEvents Payment As Panel
    Friend WithEvents Mix As Panel
    Friend WithEvents Categories As Panel
    Friend WithEvents Recent As Panel
    Friend WithEvents logout As Button
End Class
