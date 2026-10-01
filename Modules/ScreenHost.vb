Imports System.Collections.Generic
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

    Private Sub DisposeAll(controls As List(Of Control))
        For Each c In controls
            ' A screen re-added before this ran (none do today) must survive.
            If c.Parent Is Nothing Then c.Dispose()
        Next
    End Sub

End Module
