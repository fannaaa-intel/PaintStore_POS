<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class INVENTORY_STAFF
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
        Me.home = New System.Windows.Forms.Button()
        Me.itemList = New System.Windows.Forms.Button()
        Me.stockLevels = New System.Windows.Forms.Button()
        Me.stockAdjustment = New System.Windows.Forms.Button()
        Me.lowStock = New System.Windows.Forms.Button()
        Me.logout = New System.Windows.Forms.Button()
        Me.Nav.SuspendLayout()
        Me.SuspendLayout()
        '
        'Nav
        '
        Me.Nav.Controls.Add(Me.logout)
        Me.Nav.Controls.Add(Me.lowStock)
        Me.Nav.Controls.Add(Me.stockAdjustment)
        Me.Nav.Controls.Add(Me.stockLevels)
        Me.Nav.Controls.Add(Me.itemList)
        Me.Nav.Controls.Add(Me.home)
        Me.Nav.Dock = System.Windows.Forms.DockStyle.Left
        Me.Nav.Location = New System.Drawing.Point(0, 0)
        Me.Nav.Name = "Nav"
        Me.Nav.Size = New System.Drawing.Size(212, 853)
        Me.Nav.TabIndex = 1
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
        'itemList
        '
        Me.itemList.Location = New System.Drawing.Point(4, 115)
        Me.itemList.Name = "itemList"
        Me.itemList.Size = New System.Drawing.Size(100, 23)
        Me.itemList.TabIndex = 1
        Me.itemList.Text = "Item List"
        Me.itemList.UseVisualStyleBackColor = True
        '
        'stockLevels
        '
        Me.stockLevels.Location = New System.Drawing.Point(4, 195)
        Me.stockLevels.Name = "stockLevels"
        Me.stockLevels.Size = New System.Drawing.Size(100, 23)
        Me.stockLevels.TabIndex = 2
        Me.stockLevels.Text = "Stock Levels"
        Me.stockLevels.UseVisualStyleBackColor = True
        '
        'stockAdjustment
        '
        Me.stockAdjustment.Location = New System.Drawing.Point(4, 275)
        Me.stockAdjustment.Name = "stockAdjustment"
        Me.stockAdjustment.Size = New System.Drawing.Size(100, 23)
        Me.stockAdjustment.TabIndex = 3
        Me.stockAdjustment.Text = "Stock Adjustment"
        Me.stockAdjustment.UseVisualStyleBackColor = True
        '
        'lowStock
        '
        Me.lowStock.Location = New System.Drawing.Point(4, 355)
        Me.lowStock.Name = "lowStock"
        Me.lowStock.Size = New System.Drawing.Size(100, 23)
        Me.lowStock.TabIndex = 4
        Me.lowStock.Text = "Low Stock Alerts"
        Me.lowStock.UseVisualStyleBackColor = True
        '
        'logout
        '
        Me.logout.Location = New System.Drawing.Point(4, 503)
        Me.logout.Name = "logout"
        Me.logout.Size = New System.Drawing.Size(100, 23)
        Me.logout.TabIndex = 5
        Me.logout.Text = "Logout"
        Me.logout.UseVisualStyleBackColor = True
        '
        'INVENTORY_STAFF
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(6.0!, 13.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        Me.ClientSize = New System.Drawing.Size(1924, 853)
        Me.Controls.Add(Me.Nav)
        Me.Name = "INVENTORY_STAFF"
        Me.Text = "INVENTORY_STAFF"
        Me.WindowState = System.Windows.Forms.FormWindowState.Maximized
        Me.Nav.ResumeLayout(False)
        Me.ResumeLayout(False)

    End Sub

    Friend WithEvents Nav As Panel
    Friend WithEvents home As Button
    Friend WithEvents itemList As Button
    Friend WithEvents stockLevels As Button
    Friend WithEvents stockAdjustment As Button
    Friend WithEvents lowStock As Button
    Friend WithEvents logout As Button
End Class