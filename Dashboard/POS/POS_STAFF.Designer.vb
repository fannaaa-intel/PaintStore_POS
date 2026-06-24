<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()> _
Partial Class POS_STAFF
    Inherits System.Windows.Forms.Form

    'Form overrides dispose to clean up the component list.
    <System.Diagnostics.DebuggerNonUserCode()> _
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
    <System.Diagnostics.DebuggerStepThrough()> _
    Private Sub InitializeComponent()
        Me.Nav = New System.Windows.Forms.Panel()
        Me.returns = New System.Windows.Forms.Button()
        Me.logout = New System.Windows.Forms.Button()
        Me.transaction = New System.Windows.Forms.Button()
        Me.Settings = New System.Windows.Forms.Button()
        Me.Reports = New System.Windows.Forms.Button()
        Me.sales = New System.Windows.Forms.Button()
        Me.home = New System.Windows.Forms.Button()
        Me.Nav.SuspendLayout()
        Me.SuspendLayout()
        '
        'Nav
        '
        Me.Nav.Controls.Add(Me.returns)
        Me.Nav.Controls.Add(Me.logout)
        Me.Nav.Controls.Add(Me.transaction)
        Me.Nav.Controls.Add(Me.Settings)
        Me.Nav.Controls.Add(Me.Reports)
        Me.Nav.Controls.Add(Me.sales)
        Me.Nav.Controls.Add(Me.home)
        Me.Nav.Dock = System.Windows.Forms.DockStyle.Left
        Me.Nav.Location = New System.Drawing.Point(0, 0)
        Me.Nav.Name = "Nav"
        Me.Nav.Size = New System.Drawing.Size(212, 853)
        Me.Nav.TabIndex = 1
        '
        'returns
        '
        Me.returns.Location = New System.Drawing.Point(4, 356)
        Me.returns.Name = "returns"
        Me.returns.Size = New System.Drawing.Size(91, 23)
        Me.returns.TabIndex = 7
        Me.returns.Text = "Returns"
        Me.returns.UseVisualStyleBackColor = True
        '
        'logout
        '
        Me.logout.Location = New System.Drawing.Point(4, 503)
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
        Me.Reports.Location = New System.Drawing.Point(3, 288)
        Me.Reports.Name = "Reports"
        Me.Reports.Size = New System.Drawing.Size(100, 23)
        Me.Reports.TabIndex = 3
        Me.Reports.Text = "Reports"
        Me.Reports.UseVisualStyleBackColor = True
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
        'POS_STAFF
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(6.0!, 13.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        Me.ClientSize = New System.Drawing.Size(1924, 853)
        Me.Controls.Add(Me.Nav)
        Me.Name = "POS_STAFF"
        Me.Text = "POS_STAFF"
        Me.WindowState = System.Windows.Forms.FormWindowState.Maximized
        Me.Nav.ResumeLayout(False)
        Me.ResumeLayout(False)

    End Sub

    Friend WithEvents Nav As Panel
    Friend WithEvents transaction As Button
    Friend WithEvents Settings As Button
    Friend WithEvents Reports As Button
    Friend WithEvents sales As Button
    Friend WithEvents home As Button
    Friend WithEvents logout As Button
    Friend WithEvents returns As Button
End Class
