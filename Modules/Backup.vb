Imports System.Configuration
Imports System.IO
Imports MySql.Data.MySqlClient

' =============================================================================
'  Backup - whole-database backup and restore (Settings > Database Backup).
'  Admin and Inventory can back up; only the Admin can restore.
'
'  A backup is a plain .sql file written by mysqldump: it can be restored
'  from this screen, or pasted into phpMyAdmin if this PC is lost.
'
'  TOOLS
'  The XAMPP tools are preferred (C:\xampp\mysql\bin) - migrations.sql explains
'  why the MySQL 8.0 client must not be used against this MariaDB database.
'  Connection details come from the same PosDb connection string the app uses,
'  so a backup always targets the database the POS is actually running on.
'
'  RESTORE
'  Restoring replaces EVERYTHING with the file's contents. So before it runs,
'  a safety backup of the current data is taken automatically; if the restore
'  was a mistake, that file puts things back.
' =============================================================================
Public Module Backup

    Private ReadOnly ToolDirs As String() = {
        "C:\xampp\mysql\bin",
        "D:\xampp\mysql\bin"
    }

    ' Default folder: "Backups" beside the program.
    Public ReadOnly Property DefaultFolder As String
        Get
            Return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Backups")
        End Get
    End Property

    ' Full path of a MySQL tool (mysqldump.exe / mysql.exe), or "" if absent.
    Public Function FindTool(exeName As String) As String
        For Each d In ToolDirs
            Dim p As String = Path.Combine(d, exeName)
            If File.Exists(p) Then Return p
        Next
        Return ""
    End Function

    Private Function ConnInfo() As MySqlConnectionStringBuilder
        Return New MySqlConnectionStringBuilder(
            ConfigurationManager.ConnectionStrings("PosDb").ConnectionString)
    End Function

    ' Backups already in a folder, newest first.
    Public Function ListBackups(folder As String) As List(Of FileInfo)
        If Not Directory.Exists(folder) Then Return New List(Of FileInfo)
        Return New DirectoryInfo(folder).GetFiles("*.sql").
            OrderByDescending(Function(f) f.LastWriteTime).ToList()
    End Function

    ' Writes a backup into the folder. Returns the file path; errorMessage is
    ' "" on success.
    Public Function Create(folder As String, ByRef errorMessage As String,
                           Optional tag As String = "") As String
        errorMessage = ""
        If Not Permissions.Can(Permissions.BackupData) Then
            errorMessage = Permissions.DeniedMessage("make a backup")
            Return ""
        End If
        Dim dump As String = FindTool("mysqldump.exe")
        If dump = "" Then
            errorMessage = "mysqldump.exe was not found in C:\xampp\mysql\bin. Is XAMPP installed?"
            Return ""
        End If

        Dim cs = ConnInfo()
        Try
            Directory.CreateDirectory(folder)
        Catch ex As Exception
            errorMessage = "Cannot use that folder: " & ErrorLog.ForUser(ex)
            Return ""
        End Try

        Dim fileName As String = cs.Database & "_" & DateTime.Now.ToString("yyyy-MM-dd_HHmmss") &
                                 If(tag = "", "", "_" & tag) & ".sql"
        Dim outPath As String = Path.Combine(folder, fileName)

        ' --single-transaction: a consistent snapshot without locking the tills.
        ' --routines/--events/--triggers: everything, not just the tables.
        Dim args As String =
            "--host=" & Quote(cs.Server) & " --port=" & cs.Port.ToString() &
            " --user=" & Quote(cs.UserID) &
            " --single-transaction --routines --events --triggers" &
            " --default-character-set=utf8mb4" &
            " --result-file=" & Quote(outPath) & " " & Quote(cs.Database)

        Dim err As String = Run(dump, args, cs.Password, stdinFile:=Nothing)
        If err <> "" Then
            Try : File.Delete(outPath) : Catch : End Try
            errorMessage = "Backup failed: " & err
            Return ""
        End If
        If Not File.Exists(outPath) OrElse New FileInfo(outPath).Length = 0 Then
            errorMessage = "Backup failed: the file came out empty."
            Return ""
        End If

        ActivityLog.Record("BACKUP_CREATE", "database", Nothing, fileName)
        Return outPath
    End Function

    ' Replaces the database with a backup file. Takes a safety backup first.
    ' Returns "" on success, else what went wrong; safetyPath is set to the
    ' safety backup's location either way (when one was made).
    Public Function RestoreFrom(sqlFile As String, ByRef safetyPath As String) As String
        If Not Permissions.Can(Permissions.RestoreData) Then Return Permissions.DeniedMessage("restore a backup")
        safetyPath = ""
        ' Backing up is open to Inventory staff; replacing every record is not.
        If Not String.Equals(Session.CurrentRole, "Admin", StringComparison.OrdinalIgnoreCase) Then
            Return "Only the Admin can restore a backup."
        End If
        Dim mysql As String = FindTool("mysql.exe")
        If mysql = "" Then Return "mysql.exe was not found in C:\xampp\mysql\bin. Is XAMPP installed?"
        If Not File.Exists(sqlFile) Then Return "That backup file no longer exists."

        Dim safetyErr As String = ""
        safetyPath = Create(DefaultFolder, safetyErr, tag:="before-restore")
        If safetyErr <> "" Then
            Return "Restore was NOT started, because a safety copy of the current data " &
                   "could not be made first. " & safetyErr
        End If

        Dim cs = ConnInfo()
        Dim args As String =
            "--host=" & Quote(cs.Server) & " --port=" & cs.Port.ToString() &
            " --user=" & Quote(cs.UserID) &
            " --default-character-set=utf8mb4 " & Quote(cs.Database)

        Dim err As String = Run(mysql, args, cs.Password, stdinFile:=sqlFile)
        If err <> "" Then Return "Restore failed: " & err

        ' The restored database may have a different shape; let every screen
        ' re-check which optional columns and tables exist.
        AccountRequests.ResetSchemaCache()
        PaymentMethods.ResetSchemaCache()
        ActivityLog.Record("BACKUP_RESTORE", "database", Nothing, Path.GetFileName(sqlFile))
        Return ""
    End Function

    ' Runs a MySQL tool and waits. The password goes through MYSQL_PWD rather
    ' than the command line, where any other program could read it.
    Private Function Run(exe As String, args As String, password As String,
                         stdinFile As String) As String
        Dim psi As New ProcessStartInfo(exe, args) With {
            .UseShellExecute = False,
            .CreateNoWindow = True,
            .RedirectStandardError = True,
            .RedirectStandardOutput = True,
            .RedirectStandardInput = stdinFile IsNot Nothing
        }
        If Not String.IsNullOrEmpty(password) Then psi.EnvironmentVariables("MYSQL_PWD") = password

        Try
            Using p As Process = Process.Start(psi)
                ' Read stderr/stdout on other threads so a chatty tool cannot
                ' fill its pipe and deadlock while we wait for it.
                Dim errTask = p.StandardError.ReadToEndAsync()
                Dim outTask = p.StandardOutput.ReadToEndAsync()
                If stdinFile IsNot Nothing Then
                    Using src As New FileStream(stdinFile, FileMode.Open, FileAccess.Read)
                        src.CopyTo(p.StandardInput.BaseStream)
                    End Using
                    p.StandardInput.Close()
                End If
                p.WaitForExit()
                Dim stderr As String = errTask.Result.Trim()
                If p.ExitCode <> 0 Then
                    Return If(stderr = "", "exit code " & p.ExitCode.ToString(), stderr)
                End If
            End Using
            Return ""
        Catch ex As Exception
            Return ex.Message
        End Try
    End Function

    ' Days since the last backup made from ANY computer (read from the
    ' activity log, so a backup saved to a USB folder still counts).
    ' -1 = never backed up; Nothing = could not tell (database unreachable).
    Public Function DaysSinceLastBackup() As Integer?
        Try
            Using cn = Db.GetConnection()
                cn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT DATEDIFF(NOW(), MAX(created_at)) FROM activity_logs " &
                    "WHERE action = 'BACKUP_CREATE'", cn)
                    Dim o = cmd.ExecuteScalar()
                    If o Is Nothing OrElse IsDBNull(o) Then Return -1
                    Return Convert.ToInt32(o)
                End Using
            End Using
        Catch ex As Exception
            ErrorLog.Write("Backup.DaysSinceLastBackup", ex)
            Return Nothing
        End Try
    End Function

    ' Shown to the Admin after sign-in when the newest backup is more than a
    ' day old (or there is none). Offers to make one on the spot into the
    ' default folder; the Settings screen is still the place to pick another.
    Public Sub RemindIfStale()
        If Not Permissions.Can(Permissions.BackupData) Then Return
        Dim days As Integer? = DaysSinceLastBackup()
        If Not days.HasValue OrElse (days.Value >= 0 AndAlso days.Value < 1) Then Return

        Dim msg As String = If(days.Value < 0,
            "This database has never been backed up.",
            "The last backup was " & days.Value & If(days.Value = 1, " day", " days") & " ago.") &
            Environment.NewLine & Environment.NewLine &
            "If this computer breaks or is stolen, every sale and stock record since then is lost." &
            Environment.NewLine & Environment.NewLine &
            "Back up now? (Takes a few seconds. Copy the file to a USB drive afterwards.)"
        If AppModal.Show(msg, "Backup Reminder", MessageBoxButtons.YesNo,
                         MessageBoxIcon.Warning) <> DialogResult.Yes Then Return

        Dim err As String = ""
        Dim path As String
        Cursor.Current = Cursors.WaitCursor
        Try
            path = Create(DefaultFolder, err)
        Finally
            Cursor.Current = Cursors.Default
        End Try
        If err <> "" Then
            AppModal.Show(err, "Backup", MessageBoxButtons.OK, MessageBoxIcon.Error)
        Else
            AppModal.Show("Backup saved:" & Environment.NewLine & path, "Backup Complete",
                          MessageBoxButtons.OK, MessageBoxIcon.Information)
        End If
    End Sub

    Private Function Quote(s As String) As String
        Return """" & If(s, "").Replace("""", "") & """"
    End Function

End Module
