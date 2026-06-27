Partial Class AccountantReportsPanelBuilder

    Private components As System.ComponentModel.IContainer

    Protected Overrides Sub Finalize()
        If components IsNot Nothing Then
            components.Dispose()
        End If
        MyBase.Finalize()
    End Sub

    Private Sub InitializeComponent()
        components = New System.ComponentModel.Container()
    End Sub

End Class