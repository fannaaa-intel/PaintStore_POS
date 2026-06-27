<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class PROCUREMENT_STAFF
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
        Me.dashboard = New System.Windows.Forms.Button()
        Me.purchaseOrders = New System.Windows.Forms.Button()
        Me.suppliers = New System.Windows.Forms.Button()
        Me.receiveGoods = New System.Windows.Forms.Button()
        Me.deliveryTracking = New System.Windows.Forms.Button()
        Me.logout = New System.Windows.Forms.Button()
        Me.Nav.SuspendLayout()
        Me.SuspendLayout()
        '
        'Nav
        '
        Me.Nav.Controls.Add(Me.logout)
        Me.Nav.Controls.Add(Me.deliveryTracking)
        Me.Nav.Controls.Add(Me.receiveGoods)
        Me.Nav.Controls.Add(Me.suppliers)
        Me.Nav.Controls.Add(Me.purchaseOrders)
        Me.Nav.Controls.Add(Me.dashboard)
        Me.Nav.Dock = System.Windows.Forms.DockStyle.Left
        Me.Nav.Location = New System.Drawing.Point(0, 0)
        Me.Nav.Name = "Nav"
        Me.Nav.Size = New System.Drawing.Size(212, 853)
        Me.Nav.TabIndex = 1
        '
        'dashboard
        '
        Me.dashboard.Location = New System.Drawing.Point(4, 36)
        Me.dashboard.Name = "dashboard"
        Me.dashboard.Size = New System.Drawing.Size(104, 23)
        Me.dashboard.TabIndex = 0
        Me.dashboard.Text = "Dashboard"
        Me.dashboard.UseVisualStyleBackColor = True
        '
        'purchaseOrders
        '
        Me.purchaseOrders.Location = New System.Drawing.Point(4, 115)
        Me.purchaseOrders.Name = "purchaseOrders"
        Me.purchaseOrders.Size = New System.Drawing.Size(100, 23)
        Me.purchaseOrders.TabIndex = 1
        Me.purchaseOrders.Text = "Purchase Orders"
        Me.purchaseOrders.UseVisualStyleBackColor = True
        '
        'suppliers
        '
        Me.suppliers.Location = New System.Drawing.Point(4, 195)
        Me.suppliers.Name = "suppliers"
        Me.suppliers.Size = New System.Drawing.Size(100, 23)
        Me.suppliers.TabIndex = 2
        Me.suppliers.Text = "Suppliers"
        Me.suppliers.UseVisualStyleBackColor = True
        '
        'receiveGoods
        '
        Me.receiveGoods.Location = New System.Drawing.Point(4, 275)
        Me.receiveGoods.Name = "receiveGoods"
        Me.receiveGoods.Size = New System.Drawing.Size(100, 23)
        Me.receiveGoods.TabIndex = 3
        Me.receiveGoods.Text = "Receive Goods"
        Me.receiveGoods.UseVisualStyleBackColor = True
        '
        'deliveryTracking
        '
        Me.deliveryTracking.Location = New System.Drawing.Point(4, 355)
        Me.deliveryTracking.Name = "deliveryTracking"
        Me.deliveryTracking.Size = New System.Drawing.Size(100, 23)
        Me.deliveryTracking.TabIndex = 4
        Me.deliveryTracking.Text = "Delivery Tracking"
        Me.deliveryTracking.UseVisualStyleBackColor = True

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
        'PROCUREMENT_STAFF
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(6.0!, 13.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        Me.ClientSize = New System.Drawing.Size(1924, 853)
        Me.Controls.Add(Me.Nav)
        Me.Name = "PROCUREMENT_STAFF"
        Me.Text = "PROCUREMENT_STAFF"
        Me.WindowState = System.Windows.Forms.FormWindowState.Maximized
        Me.Nav.ResumeLayout(False)
        Me.ResumeLayout(False)

    End Sub

    Friend WithEvents Nav As Panel
    Friend WithEvents dashboard As Button
    Friend WithEvents purchaseOrders As Button
    Friend WithEvents suppliers As Button
    Friend WithEvents receiveGoods As Button
    Friend WithEvents deliveryTracking As Button
    Friend WithEvents logout As Button
End Class