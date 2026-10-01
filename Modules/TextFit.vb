Imports System.Windows.Forms

' =============================================================================
'  TextFit
'
'  Long names (suppliers, products) used to be AutoSize labels, so a name like
'  "Pacific Coatings & Industrial Paint Distribution Corporation" ran straight
'  over the next column. Clamp fixes a label's width and ends the text with
'  "..." instead; hovering it still shows the full name (AutoEllipsis tooltip).
' =============================================================================
Public Module TextFit

    Public Sub Clamp(lbl As Label, width As Integer)
        If lbl.AutoSize Then
            lbl.AutoSize = False
            lbl.AutoEllipsis = True
            lbl.Height = lbl.PreferredHeight
        End If
        lbl.Width = Math.Max(24, width)
    End Sub

End Module
