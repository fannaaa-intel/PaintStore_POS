Imports MySql.Data.MySqlClient

' =============================================================================
'  ReportSignatories  —  the names printed in the signature block at the foot
'  of every generated report (print, PDF and Excel).
'
'    Prepared by   the signed-in user, with their role as the position
'    Checked by    set in Settings > Report Signatories
'    Approved by   set in Settings > Report Signatories
'
'  Stored in store_settings (migration 20). On a database without those
'  columns the block still prints, with blank Checked / Approved lines to be
'  filled in by hand, so a report never fails over a missing migration.
' =============================================================================
Public Module ReportSignatories

    Public Class Signer
        Public Caption As String = ""     ' "Prepared by"
        Public Name As String = ""        ' blank = a line to sign on
        Public Position As String = ""    ' "Accountant"
    End Class

    Public Class Settings
        Public CheckedName As String = ""
        Public CheckedPosition As String = ""
        Public ApprovedName As String = ""
        Public ApprovedPosition As String = ""
    End Class

    Private _hasColumns As Boolean? = Nothing

    Public Function HasColumns() As Boolean
        If _hasColumns.HasValue Then Return _hasColumns.Value
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT COUNT(*) FROM information_schema.columns " &
                    "WHERE table_schema = DATABASE() AND table_name = 'store_settings' " &
                    "AND column_name = 'sig_approved_name'", conn)
                    _hasColumns = Convert.ToInt32(cmd.ExecuteScalar()) > 0
                End Using
            End Using
        Catch swallowed As Exception
            ErrorLog.Write("ReportSignatories.HasColumns", swallowed)
            Return False    ' not cached: the database may simply be down
        End Try
        Return _hasColumns.Value
    End Function

    Public Function Load() As Settings
        Dim s As New Settings()
        If Not HasColumns() Then Return s
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT sig_checked_name, sig_checked_position, " &
                    "sig_approved_name, sig_approved_position " &
                    "FROM store_settings WHERE setting_id = 1", conn)
                    Using rdr = cmd.ExecuteReader()
                        If rdr.Read() Then
                            s.CheckedName = Text(rdr("sig_checked_name"))
                            s.CheckedPosition = Text(rdr("sig_checked_position"))
                            s.ApprovedName = Text(rdr("sig_approved_name"))
                            s.ApprovedPosition = Text(rdr("sig_approved_position"))
                        End If
                    End Using
                End Using
            End Using
        Catch swallowed As Exception
            ErrorLog.Write("ReportSignatories.Load", swallowed)
            ' Fall back to blank lines rather than blocking the report.
        End Try
        Return s
    End Function

    ' Returns "" on success, otherwise the reason it could not be saved.
    Public Function Save(s As Settings) As String
        If Not Permissions.Can(Permissions.EditSignatories) Then Return Permissions.DeniedMessage("change report signatories")
        If Not HasColumns() Then
            Return "Report signatories are not set up in the database yet. " &
                   "Run migrations.sql section 20 in phpMyAdmin first."
        End If
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "UPDATE store_settings SET sig_checked_name = @cn, sig_checked_position = @cp, " &
                    "sig_approved_name = @an, sig_approved_position = @ap WHERE setting_id = 1", conn)
                    cmd.Parameters.AddWithValue("@cn", s.CheckedName.Trim())
                    cmd.Parameters.AddWithValue("@cp", s.CheckedPosition.Trim())
                    cmd.Parameters.AddWithValue("@an", s.ApprovedName.Trim())
                    cmd.Parameters.AddWithValue("@ap", s.ApprovedPosition.Trim())
                    If cmd.ExecuteNonQuery() = 0 Then Return "The store settings row is missing."
                End Using
            End Using
            ActivityLog.Record("SIGNATORIES_UPDATE", "store_settings", 1,
                               "Checked by: " & s.CheckedName & "; Approved by: " & s.ApprovedName)
            Return ""
        Catch ex As Exception
            Return "Could not save the signatories: " & ErrorLog.ForUser(ex)
        End Try
    End Function

    ' The three signers for a report generated now, by the signed-in user.
    Public Function ForReport() As List(Of Signer)
        Dim s As Settings = Load()
        Return New List(Of Signer) From {
            New Signer With {.Caption = "Prepared by", .Name = If(Session.CurrentFullName, ""),
                             .Position = If(Session.CurrentRole, "")},
            New Signer With {.Caption = "Checked by", .Name = s.CheckedName, .Position = s.CheckedPosition},
            New Signer With {.Caption = "Approved by", .Name = s.ApprovedName, .Position = s.ApprovedPosition}
        }
    End Function

    Private Function Text(v As Object) As String
        Return If(v Is Nothing OrElse IsDBNull(v), "", v.ToString())
    End Function

End Module
