Imports System.Collections.Generic
Imports System.Reflection
Imports System.Windows.Forms

' =============================================================================
'  ScreenHost
'
'  The dashboards swap screens with MainContainer.Controls.Clear(). Clear()
'  only detaches the old screen: its window handles, and the 1-second clock
'  timers several screens start (they stop themselves once their label is
'  disposed), lived on until the app closed. Every menu click leaked a little,
'  and after a few hundred clicks Windows refused to create more windows
'  ("Error creating window handle").
'
'  Clear detaches the old screen at once, then disposes it once the current
'  click has finished: the click that asked for the new screen often came
'  from a control on the old one, which must not be disposed mid-event.
' =============================================================================
Public Module ScreenHost

    Public Sub Clear(container As Control)
        Dim old As New List(Of Control)
        For Each c As Control In container.Controls
            old.Add(c)
        Next
        container.Controls.Clear()
        If old.Count = 0 Then Return

        If container.IsHandleCreated AndAlso Not container.IsDisposed Then
            Try
                container.BeginInvoke(New MethodInvoker(Sub() DisposeAll(old)))
                Return
            Catch
                ' Handle died between the check and here: dispose right away.
            End Try
        End If
        DisposeAll(old)
    End Sub

    ' =========================================================================
    '  SIDEBAR BUTTONS
    '
    '  A sidebar button kept keyboard focus after it was clicked. A focused flat
    '  button becomes the form's "default" button, and WinForms then draws a
    '  dark 1px frame round it even with BorderSize = 0 -- the box that showed
    '  round "Dashboard" after switching screens. It also meant Enter (a barcode
    '  scanner ends every scan with one) clicked that nav button again and
    '  reloaded the screen. Sidebar buttons are mouse targets, so they no longer
    '  take focus at all.
    '
    '  Button.PerformClick does nothing on a control that cannot take focus, so
    '  code that opens a screen "as if its nav button were clicked" uses Press.
    ' =========================================================================
    Private ReadOnly SetStyleMethod As MethodInfo =
        GetType(Control).GetMethod("SetStyle", BindingFlags.Instance Or BindingFlags.NonPublic)
    Private ReadOnly OnClickMethod As MethodInfo =
        GetType(Control).GetMethod("OnClick", BindingFlags.Instance Or BindingFlags.NonPublic)

    Public Sub MakeNavButton(btn As Button)
        If btn Is Nothing Then Return
        SetStyleMethod.Invoke(btn, New Object() {ControlStyles.Selectable, False})
        btn.TabStop = False
    End Sub

    Public Sub Press(btn As Button)
        If btn Is Nothing OrElse btn.IsDisposed OrElse Not btn.Enabled Then Return
        OnClickMethod.Invoke(btn, New Object() {EventArgs.Empty})
    End Sub

    Private Sub DisposeAll(controls As List(Of Control))
        For Each c In controls
            ' A screen re-added before this ran (none do today) must survive.
            If c.Parent Is Nothing Then c.Dispose()
        Next
    End Sub

End Module
