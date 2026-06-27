<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class ACCOUNTANT_STAFF
    Inherits System.Windows.Forms.Form

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

    Private components As System.ComponentModel.IContainer

    <System.Diagnostics.DebuggerStepThrough()>
    Private Sub InitializeComponent()
        Me.Nav = New System.Windows.Forms.Panel()
        Me.logout = New System.Windows.Forms.Button()
        Me.reports = New System.Windows.Forms.Button()
        Me.salesHistory = New System.Windows.Forms.Button()
        Me.payables = New System.Windows.Forms.Button()
        Me.incomeExpenses = New System.Windows.Forms.Button()
        Me.home = New System.Windows.Forms.Button()
        Me.Nav.SuspendLayout()
        Me.SuspendLayout()
        '
        'Nav
        '
        Me.Nav.Controls.Add(Me.logout)
        Me.Nav.Controls.Add(Me.reports)
        Me.Nav.Controls.Add(Me.salesHistory)
        Me.Nav.Controls.Add(Me.payables)
        Me.Nav.Controls.Add(Me.incomeExpenses)
        Me.Nav.Controls.Add(Me.home)
        Me.Nav.Dock = System.Windows.Forms.DockStyle.Left
        Me.Nav.Location = New System.Drawing.Point(0, 0)
        Me.Nav.Name = "Nav"
        Me.Nav.Size = New System.Drawing.Size(212, 853)
        Me.Nav.TabIndex = 1
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
        'reports
        '
        Me.reports.Location = New System.Drawing.Point(4, 355)
        Me.reports.Name = "reports"
        Me.reports.Size = New System.Drawing.Size(100, 23)
        Me.reports.TabIndex = 4
        Me.reports.Text = "Reports"
        Me.reports.UseVisualStyleBackColor = True
        '
        'salesHistory
        '
        Me.salesHistory.Location = New System.Drawing.Point(4, 275)
        Me.salesHistory.Name = "salesHistory"
        Me.salesHistory.Size = New System.Drawing.Size(100, 23)
        Me.salesHistory.TabIndex = 3
        Me.salesHistory.Text = "Sales History"
        Me.salesHistory.UseVisualStyleBackColor = True
        '
        'payables
        '
        Me.payables.Location = New System.Drawing.Point(4, 195)
        Me.payables.Name = "payables"
        Me.payables.Size = New System.Drawing.Size(100, 23)
        Me.payables.TabIndex = 2
        Me.payables.Text = "Payables"
        Me.payables.UseVisualStyleBackColor = True
        '
        'incomeExpenses
        '
        Me.incomeExpenses.Location = New System.Drawing.Point(4, 115)
        Me.incomeExpenses.Name = "incomeExpenses"
        Me.incomeExpenses.Size = New System.Drawing.Size(100, 23)
        Me.incomeExpenses.TabIndex = 1
        Me.incomeExpenses.Text = "Income & Expenses"
        Me.incomeExpenses.UseVisualStyleBackColor = True
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
        'ACCOUNTANT_STAFF
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(6.0!, 13.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        Me.ClientSize = New System.Drawing.Size(1924, 853)
        Me.Controls.Add(Me.Nav)
        Me.Name = "ACCOUNTANT_STAFF"
        Me.Text = "ACCOUNTANT_STAFF"
        Me.WindowState = System.Windows.Forms.FormWindowState.Maximized
        Me.Nav.ResumeLayout(False)
        Me.ResumeLayout(False)

    End Sub

    Friend WithEvents Nav As Panel
    Friend WithEvents home As Button
    Friend WithEvents incomeExpenses As Button
    Friend WithEvents payables As Button
    Friend WithEvents salesHistory As Button
    Friend WithEvents reports As Button
    Friend WithEvents logout As Button
End Class