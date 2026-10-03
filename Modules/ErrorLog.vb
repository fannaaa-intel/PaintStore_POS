Imports System.IO
Imports System.Windows.Forms

' =============================================================================
'  ErrorLog - a plain text file of errors the screens chose not to show.
'
'  Many loaders deliberately carry on when something small fails (a badge
'  count, a logo, an optional column). Before this, those failures vanished:
'  an empty Catch left no trace, so "the numbers look wrong" could never be
'  traced back to a cause. Now each one is written here.
'
'  File: %LOCALAPPDATA%\PaintStore_POS\Logs\errors-yyyy-MM.log (one per month).
'  Writing never throws - a logging failure must not break the caller.
' =============================================================================
Public Module ErrorLog

    Private ReadOnly _lock As New Object()

    Public ReadOnly Property Folder As String
        Get
            Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                "PaintStore_POS", "Logs")
        End Get
    End Property

    Public Sub Write(where As String, ex As Exception)
        If ex Is Nothing Then Return
        Write(where, ex.GetType().Name & ": " & ex.Message)
    End Sub

    ' Plain-language text for an error shown in a dialog, with what to do next.
    ' The technical detail goes to the log file instead of the screen.
    ' Messages the app wrote itself ("Insufficient stock for ...") pass through
    ' unchanged - they are already meant for the user.
    Public Function ForUser(ex As Exception) As String
        If ex Is Nothing Then Return ""
        Write("Shown to user", ex)
        Dim my = TryCast(ex, MySql.Data.MySqlClient.MySqlException)
        If my Is Nothing AndAlso ex.InnerException IsNot Nothing Then
            my = TryCast(ex.InnerException, MySql.Data.MySqlClient.MySqlException)
        End If
        If my Is Nothing Then
            If TypeOf ex Is UnauthorizedAccessException Then
                Return "Windows did not allow access to that file or folder. Choose another folder, or ask the administrator."
            End If
            If TypeOf ex Is IOException Then
                Return ex.Message & " Close any program that has the file open, then try again."
            End If
            Return ex.Message
        End If
        Select Case my.Number
            Case 0, 1042, 2002, 2003, 2013
                Return "Cannot reach the database. Check that XAMPP is open and MySQL is running, then try again."
            Case 1045
                Return "The database refused the app's login. Ask the administrator to check the database account in App.config."
            Case 1205, 1213
                Return "Someone else was saving the same record at the same moment. Nothing was saved - please try again."
            Case 1062
                Return "That already exists (same name or number). Use a different one."
            Case 1451
                Return "This is still used by other records, so it cannot be removed."
            Case 1452
                Return "Something this refers to no longer exists. Reopen the screen and try again."
            Case 1406, 1264
                Return "One of the values is too long or too large. Shorten it and try again."
            Case 1054, 1146
                Return "The database is missing an update. Ask the administrator to run the latest migrations.sql."
            Case Else
                Return "The database reported a problem (code " & my.Number & "). Try again; if it keeps happening, tell the administrator."
        End Select
    End Function

    ' A screen could not read from the database and is showing what it has
    ' (zeros, an empty list). Writes the log line and tells the user once -
    ' at most every WarnEveryMinutes, so a dead database on a refresh timer
    ' does not bury the screen in dialogs. The notice is shown after the
    ' current event finishes, never in the middle of a paint or a layout.
    Private Const WarnEveryMinutes As Integer = 2
    Private _lastWarn As DateTime = DateTime.MinValue

    Public Sub WarnUser(where As String, ex As Exception)
        Write(where, ex)
        Try
            If DateTime.Now - _lastWarn < TimeSpan.FromMinutes(WarnEveryMinutes) Then Return
            _lastWarn = DateTime.Now
            Dim host As Form = Nothing
            For Each f As Form In Application.OpenForms
                If f.Visible AndAlso Not f.IsDisposed AndAlso f.IsHandleCreated Then host = f : Exit For
            Next
            If host Is Nothing Then Return
            host.BeginInvoke(New Action(
                Sub()
                    AppModal.Show("Some information on this screen could not be loaded, so parts of it " &
                                  "may show zeros or be empty." & Environment.NewLine & Environment.NewLine &
                                  "What to do: check that XAMPP is open and MySQL is running, then open " &
                                  "this screen again. If it keeps happening, tell the administrator.",
                                  "Could Not Load", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                End Sub))
        Catch
            ' Never let the warning itself break the screen.
        End Try
    End Sub

    Public Sub Write(where As String, message As String)
        Try
            Dim line As String = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") & "  [" & where & "]  " &
                                 If(String.IsNullOrEmpty(Session.CurrentUsername), "", Session.CurrentUsername & "  ") &
                                 If(message, "").Replace(Environment.NewLine, " | ")
            System.Diagnostics.Debug.WriteLine(line)
            SyncLock _lock
                Directory.CreateDirectory(Folder)
                File.AppendAllText(Path.Combine(Folder, "errors-" & DateTime.Now.ToString("yyyy-MM") & ".log"),
                                   line & Environment.NewLine)
            End SyncLock
        Catch
            ' Nowhere left to report to.
        End Try
    End Sub

End Module
