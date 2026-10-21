Imports Microsoft.VisualBasic.ApplicationServices

Namespace My

    ' =========================================================================
    '  Last-resort handler for errors no screen caught.
    '
    '  Without it .NET shows its own "Unhandled exception has occurred" box,
    '  whose Quit button closes the POS mid-sale and whose text means nothing
    '  to staff - and nothing was written down, so a crash on a client laptop
    '  could not be traced afterwards. Now the full error (with the line it
    '  came from) goes to the ErrorLog file and the app keeps running.
    '
    '  Only active outside Visual Studio: under the debugger the error still
    '  stops on the offending line as before.
    ' =========================================================================
    Partial Friend Class MyApplication

        ' Icons and the logo are loaded by bare file name ("paint.png"), which
        ' Windows resolves against the current folder. A desktop shortcut with
        ' another "Start in" folder left every screen without icons.
        Private Sub MyApplication_Startup(sender As Object, e As StartupEventArgs) Handles Me.Startup
            Try
                IO.Directory.SetCurrentDirectory(System.Windows.Forms.Application.StartupPath)
            Catch ex As Exception
                ErrorLog.Write("Startup", ex)
            End Try
        End Sub

        Private _lastShownText As String = ""
        Private _lastShownAt As DateTime = DateTime.MinValue

        Private Sub MyApplication_UnhandledException(sender As Object,
                                                     e As UnhandledExceptionEventArgs) Handles Me.UnhandledException
            e.ExitApplication = False
            ErrorLog.Write("Unhandled", e.Exception.ToString())

            ' A paint or timer error repeats on every repaint/tick; tell the
            ' user once rather than stacking a dialog per repetition.
            Dim text As String = e.Exception.GetType().Name & e.Exception.Message
            If text = _lastShownText AndAlso DateTime.Now - _lastShownAt < TimeSpan.FromMinutes(1) Then Return
            _lastShownText = text
            _lastShownAt = DateTime.Now

            Try
                AppModal.Show("Something went wrong on this screen, but your work so far is safe." &
                              Environment.NewLine & Environment.NewLine &
                              "What to do: open the screen again from the menu. If it keeps " &
                              "happening, tell the administrator - the details were saved in " &
                              ErrorLog.Folder & ".",
                              "Unexpected Error", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Catch
                ' The dialog itself failed; the log line above is what matters.
            End Try
        End Sub

    End Class

End Namespace
