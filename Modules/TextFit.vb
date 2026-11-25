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

    ' A big KPI figure ("₱1,234,567.00") keeps its full value: when it is wider
    ' than its card, the type steps down (to minSize at most) instead of the
    ' figure running past the card's edge. Re-checked whenever the card or the
    ' value changes, so a figure that later fits grows back to full size.
    Public Sub FitWidth(lbl As Label, host As Control, Optional rightPad As Integer = 16,
                        Optional minSize As Single = 14.0F)
        Dim full As Single = lbl.Font.Size
        Dim style As System.Drawing.FontStyle = lbl.Font.Style
        Dim family As String = lbl.Font.FontFamily.Name
        Dim busy As Boolean = False
        Dim apply As Action =
            Sub()
                If busy OrElse host.Width <= 0 Then Return
                busy = True
                Try
                    Dim avail As Integer = host.Width - lbl.Left - rightPad
                    Dim size As Single = full
                    Do
                        Using f As New System.Drawing.Font(family, size, style)
                            If TextRenderer.MeasureText(lbl.Text, f).Width <= avail OrElse size <= minSize Then Exit Do
                        End Using
                        size -= 1.0F
                    Loop
                    If Math.Abs(lbl.Font.Size - size) > 0.01F Then
                        lbl.Font = If(style = System.Drawing.FontStyle.Bold AndAlso family = "Segoe UI",
                                      BoldCache(size), New System.Drawing.Font(family, size, style))
                    End If
                Finally
                    busy = False
                End Try
            End Sub
        AddHandler host.Resize, Sub() apply()
        AddHandler lbl.TextChanged, Sub() apply()
        apply()
    End Sub

    Private ReadOnly _bold As New Dictionary(Of Single, System.Drawing.Font)
    Private Function BoldCache(size As Single) As System.Drawing.Font
        Dim f As System.Drawing.Font = Nothing
        If Not _bold.TryGetValue(size, f) Then
            f = New System.Drawing.Font("Segoe UI", size, System.Drawing.FontStyle.Bold)
            _bold(size) = f
        End If
        Return f
    End Function

End Module
