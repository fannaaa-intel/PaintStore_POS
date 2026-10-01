Imports System.Windows.Forms

' =============================================================================
'  LiteralAmpersands
'
'  Labels and buttons treat "&" as a keyboard-shortcut marker and hide it, so
'  a supplier named "Pacific Coatings & Industrial" showed on screen as
'  "Pacific Coatings  Industrial". No screen in this app uses shortcut
'  underlines, so every label and button on every window shows "&" as typed.
'
'  Install once at start-up. Each open window is hooked the first time the app
'  goes idle, and anything added to it later (screens are rebuilt on every nav
'  click) is fixed the moment it is added, before it paints.
' =============================================================================
Public Module LiteralAmpersands

    Private _installed As Boolean
    Private ReadOnly _hooked As New HashSet(Of Form)

    Public Sub Install()
        If _installed Then Return
        _installed = True
        AddHandler Application.Idle, AddressOf HookOpenForms
    End Sub

    Private Sub HookOpenForms(sender As Object, e As EventArgs)
        For Each f As Form In Application.OpenForms
            If _hooked.Add(f) Then
                AddHandler f.FormClosed, Sub() _hooked.Remove(f)
                Apply(f)
            End If
        Next
    End Sub

    Private Sub Apply(c As Control)
        Dim lbl = TryCast(c, Label)
        If lbl IsNot Nothing Then lbl.UseMnemonic = False
        Dim btn = TryCast(c, ButtonBase)
        If btn IsNot Nothing Then btn.UseMnemonic = False

        RemoveHandler c.ControlAdded, AddressOf OnControlAdded
        AddHandler c.ControlAdded, AddressOf OnControlAdded
        For Each child As Control In c.Controls
            Apply(child)
        Next
    End Sub

    Private Sub OnControlAdded(sender As Object, e As ControlEventArgs)
        Apply(e.Control)
    End Sub

End Module
