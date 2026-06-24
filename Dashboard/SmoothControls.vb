Imports System.Windows.Forms

Public Class SmoothPanel
    Inherits Panel

    Public Sub New()
        Me.SetStyle(ControlStyles.UserPaint Or
                    ControlStyles.AllPaintingInWmPaint Or
                    ControlStyles.OptimizedDoubleBuffer Or
                    ControlStyles.ResizeRedraw, True)
        Me.UpdateStyles()
        Me.DoubleBuffered = True
    End Sub
End Class

Public Class SmoothFlowLayoutPanel
    Inherits FlowLayoutPanel

    Public Sub New()
        Me.SetStyle(ControlStyles.UserPaint Or
                    ControlStyles.AllPaintingInWmPaint Or
                    ControlStyles.OptimizedDoubleBuffer Or
                    ControlStyles.ResizeRedraw, True)
        Me.UpdateStyles()
        Me.DoubleBuffered = True
    End Sub
End Class

Public Class SmoothTableLayoutPanel
    Inherits TableLayoutPanel

    Public Sub New()
        Me.SetStyle(ControlStyles.UserPaint Or
                    ControlStyles.AllPaintingInWmPaint Or
                    ControlStyles.OptimizedDoubleBuffer Or
                    ControlStyles.ResizeRedraw, True)
        Me.UpdateStyles()
        Me.DoubleBuffered = True
    End Sub
End Class