Imports System.Web.Script.Serialization
Imports MySql.Data.MySqlClient

' =============================================================================
'  Archive - the Recycle Bin (migrations.sql section 18).
'
'  Every screen that deletes a record calls Capture FIRST, inside the same
'  transaction as its DELETE. The row (and any child rows going with it, e.g.
'  a colour formula's components) is copied into archive.data_json. If the
'  delete then fails the copy rolls back with it, and if the copy fails the
'  delete never happens - so there is never a deleted record with no copy.
'
'  Admin > Settings > Recycle Bin lists what is there and can:
'    Restore        - put the record back under its original id
'    Delete forever - drop the copy (the row stays, marked Purged, as history)
'
'  SOFT DELETES
'  Products are never truly deleted - sales point at them - so "delete" sets
'  is_active = 0. They are archived with Soft = True, and Restore simply turns
'  them back on.
'
'  UN-MIGRATED DATABASE
'  Capture does nothing when the archive table is absent, so every delete
'  behaves exactly as it did before.
' =============================================================================
Public Module Archive

    Public Class Entry
        Public ArchiveId As Integer
        Public SourceTable As String
        Public RecordId As Integer
        Public Label As String
        Public DeletedBy As String
        Public DeletedAt As DateTime
    End Class

    Private _checked As Boolean = False
    Private _present As Boolean = False

    Public Function HasArchiveTable() As Boolean
        If _checked Then Return _present
        Try
            Using cn = Db.GetConnection()
                cn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT COUNT(*) FROM information_schema.tables " &
                    "WHERE table_schema = DATABASE() AND table_name = 'archive'", cn)
                    _present = Convert.ToInt32(cmd.ExecuteScalar()) > 0
                End Using
            End Using
            _checked = True
        Catch swallowed As Exception
            ErrorLog.Write("Archive.HasArchiveTable", swallowed)
            _present = False
        End Try
        Return _present
    End Function

    ' What a table is called in the bin.
    Public Function KindName(sourceTable As String) As String
        Select Case sourceTable
            Case "categories" : Return "Category"
            Case "color_formulas" : Return "Colour formula"
            Case "payment_methods" : Return "Payment method"
            Case "suppliers" : Return "Supplier"
            Case "quotations" : Return "Price quotation"
            Case "purchase_requests" : Return "Purchase request"
            Case "products" : Return "Product"
            Case Else : Return sourceTable
        End Select
    End Function

    ' -------------------------------------------------------------------------
    '  CAPTURE - call inside the deleting transaction, before the DELETE.
    '
    '  children: tables whose rows go with this one, as (table, fk column).
    '  soft:     the caller only deactivates the row (products).
    ' -------------------------------------------------------------------------
    Public Sub Capture(conn As MySqlConnection, tx As MySqlTransaction,
                       table As String, keyColumn As String, id As Integer,
                       label As String,
                       Optional children As (table As String, fk As String)() = Nothing,
                       Optional soft As Boolean = False)
        If Not HasArchiveTable() Then Return

        Dim doc As New Dictionary(Of String, Object)
        doc("table") = table
        doc("key") = keyColumn
        doc("soft") = soft
        doc("row") = ReadRows(conn, tx, table, keyColumn, id).FirstOrDefault()
        If doc("row") Is Nothing Then Return   ' nothing to delete, nothing to keep

        Dim kids As New List(Of Object)
        If children IsNot Nothing Then
            For Each c In children
                kids.Add(New Dictionary(Of String, Object) From {
                    {"table", c.table},
                    {"rows", ReadRows(conn, tx, c.table, c.fk, id)}
                })
            Next
        End If
        doc("children") = kids

        Using cmd As New MySqlCommand(
            "INSERT INTO archive (source_table, record_id, label, data_json, deleted_by) " &
            "VALUES (@t, @id, @label, @json, @uid)", conn, tx)
            cmd.Parameters.AddWithValue("@t", table)
            cmd.Parameters.AddWithValue("@id", id)
            cmd.Parameters.AddWithValue("@label", Left(If(label, ""), 200))
            cmd.Parameters.AddWithValue("@json", NewSerializer().Serialize(doc))
            cmd.Parameters.AddWithValue("@uid",
                If(Session.CurrentUserId = 0, CObj(DBNull.Value), CObj(Session.CurrentUserId)))
            cmd.ExecuteNonQuery()
        End Using
    End Sub

    ' Every column of the matching rows, as name -> value. Dates become text
    ' so they survive JSON and go back into MySQL unchanged.
    Private Function ReadRows(conn As MySqlConnection, tx As MySqlTransaction,
                              table As String, col As String, id As Integer) As List(Of Dictionary(Of String, Object))
        Dim rows As New List(Of Dictionary(Of String, Object))
        ' Table/column names come from the calling code, never from input.
        Using cmd As New MySqlCommand(
            "SELECT * FROM `" & table & "` WHERE `" & col & "` = @id", conn, tx)
            cmd.Parameters.AddWithValue("@id", id)
            Using rdr = cmd.ExecuteReader()
                While rdr.Read()
                    Dim row As New Dictionary(Of String, Object)
                    For i As Integer = 0 To rdr.FieldCount - 1
                        Dim v As Object = rdr.GetValue(i)
                        If IsDBNull(v) Then
                            v = Nothing
                        ElseIf TypeOf v Is DateTime Then
                            v = CType(v, DateTime).ToString("yyyy-MM-dd HH:mm:ss")
                        ElseIf TypeOf v Is TimeSpan Then
                            v = v.ToString()
                        End If
                        row(rdr.GetName(i)) = v
                    Next
                    rows.Add(row)
                End While
            End Using
        End Using
        Return rows
    End Function

    Private Function NewSerializer() As JavaScriptSerializer
        Return New JavaScriptSerializer With {.MaxJsonLength = Integer.MaxValue}
    End Function

    ' -------------------------------------------------------------------------
    '  BIN
    ' -------------------------------------------------------------------------
    Public Function ListArchived() As List(Of Entry)
        Dim list As New List(Of Entry)
        If Not HasArchiveTable() Then Return list
        Using cn = Db.GetConnection()
            cn.Open()
            Using cmd As New MySqlCommand(
                "SELECT a.archive_id, a.source_table, a.record_id, a.label, a.deleted_at, " &
                "       COALESCE(u.full_name, 'Unknown') AS deleted_by " &
                "FROM archive a LEFT JOIN users u ON u.user_id = a.deleted_by " &
                "WHERE a.status = 'Archived' ORDER BY a.deleted_at DESC, a.archive_id DESC", cn)
                Using rdr = cmd.ExecuteReader()
                    While rdr.Read()
                        list.Add(New Entry With {
                            .ArchiveId = Convert.ToInt32(rdr("archive_id")),
                            .SourceTable = rdr("source_table").ToString(),
                            .RecordId = Convert.ToInt32(rdr("record_id")),
                            .Label = rdr("label").ToString(),
                            .DeletedAt = Convert.ToDateTime(rdr("deleted_at")),
                            .DeletedBy = rdr("deleted_by").ToString()
                        })
                    End While
                End Using
            End Using
        End Using
        Return list
    End Function

    ' Puts the record back. Returns "" on success, else why it could not.
    Public Function Restore(archiveId As Integer) As String
        If Not Permissions.Can(Permissions.RestoreData) Then Return Permissions.DeniedMessage("restore from the Recycle Bin")
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using tx = conn.BeginTransaction()
                    Dim json As String = Nothing
                    Dim label As String = ""
                    Using cmd As New MySqlCommand(
                        "SELECT data_json, label FROM archive " &
                        "WHERE archive_id = @id AND status = 'Archived' FOR UPDATE", conn, tx)
                        cmd.Parameters.AddWithValue("@id", archiveId)
                        Using rdr = cmd.ExecuteReader()
                            If rdr.Read() Then
                                json = If(IsDBNull(rdr("data_json")), Nothing, rdr("data_json").ToString())
                                label = rdr("label").ToString()
                            End If
                        End Using
                    End Using
                    If String.IsNullOrEmpty(json) Then
                        Return "That item is no longer in the Recycle Bin."
                    End If

                    Dim doc = CType(NewSerializer().DeserializeObject(json), Dictionary(Of String, Object))
                    Dim table As String = doc("table").ToString()
                    Dim keyCol As String = doc("key").ToString()
                    Dim row = CType(doc("row"), Dictionary(Of String, Object))
                    Dim soft As Boolean = doc.ContainsKey("soft") AndAlso CBool(doc("soft"))

                    If soft Then
                        ' Deactivated, not deleted: switching it back on is the restore.
                        Using cmd As New MySqlCommand(
                            "UPDATE `" & table & "` SET is_active = 1 WHERE `" & keyCol & "` = @id", conn, tx)
                            cmd.Parameters.AddWithValue("@id", row(keyCol))
                            If cmd.ExecuteNonQuery() = 0 Then
                                Return "The original record no longer exists, so it cannot be restored."
                            End If
                        End Using
                    Else
                        InsertRow(conn, tx, table, row)
                        If doc.ContainsKey("children") AndAlso doc("children") IsNot Nothing Then
                            For Each kidObj In CType(doc("children"), Object())
                                Dim kid = CType(kidObj, Dictionary(Of String, Object))
                                For Each r In CType(kid("rows"), Object())
                                    InsertRow(conn, tx, kid("table").ToString(), CType(r, Dictionary(Of String, Object)))
                                Next
                            Next
                        End If
                        AfterRestore(conn, tx, table, row)
                    End If

                    Using cmd As New MySqlCommand(
                        "UPDATE archive SET status = 'Restored', handled_by = @uid, handled_at = NOW() " &
                        "WHERE archive_id = @id", conn, tx)
                        cmd.Parameters.AddWithValue("@uid", Session.CurrentUserId)
                        cmd.Parameters.AddWithValue("@id", archiveId)
                        cmd.ExecuteNonQuery()
                    End Using
                    tx.Commit()

                    ActivityLog.Record("ARCHIVE_RESTORE", table, Convert.ToInt32(row(keyCol)),
                                       KindName(table) & ": " & label)
                End Using
            End Using
            Return ""
        Catch ex As MySqlException When ex.Number = 1062
            Return "Something with the same name or number was created after this was deleted, " &
                   "so it cannot be put back as it was. Rename or remove the newer one first."
        Catch ex As MySqlException When ex.Number = 1452
            Return "It refers to something that has since been deleted (for example its " &
                   "product or supplier), so it cannot be put back."
        Catch ex As Exception
            Return "Could not restore: " & ErrorLog.ForUser(ex)
        End Try
    End Function

    ' Drops the copy for good. The row stays (Purged) so the bin's history
    ' still shows that something was deleted and by whom.
    Public Function Purge(archiveId As Integer) As String
        If Not Permissions.Can(Permissions.RestoreData) Then Return Permissions.DeniedMessage("delete from the Recycle Bin")
        Try
            Dim label As String = ""
            Dim table As String = ""
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT label, source_table FROM archive WHERE archive_id = @id", conn)
                    cmd.Parameters.AddWithValue("@id", archiveId)
                    Using rdr = cmd.ExecuteReader()
                        If rdr.Read() Then
                            label = rdr("label").ToString()
                            table = rdr("source_table").ToString()
                        End If
                    End Using
                End Using
                Using cmd As New MySqlCommand(
                    "UPDATE archive SET status = 'Purged', data_json = NULL, " &
                    "handled_by = @uid, handled_at = NOW() " &
                    "WHERE archive_id = @id AND status = 'Archived'", conn)
                    cmd.Parameters.AddWithValue("@uid", Session.CurrentUserId)
                    cmd.Parameters.AddWithValue("@id", archiveId)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
            ActivityLog.Record("ARCHIVE_PURGE", table, Nothing, KindName(table) & ": " & label)
            Return ""
        Catch ex As Exception
            Return "Could not delete it: " & ErrorLog.ForUser(ex)
        End Try
    End Function

    Private Sub InsertRow(conn As MySqlConnection, tx As MySqlTransaction,
                          table As String, row As Dictionary(Of String, Object))
        Dim cols As New List(Of String)
        Dim pars As New List(Of String)
        Using cmd As New MySqlCommand() With {.Connection = conn, .Transaction = tx}
            Dim i As Integer = 0
            For Each kv In row
                cols.Add("`" & kv.Key.Replace("`", "") & "`")
                pars.Add("@p" & i.ToString())
                cmd.Parameters.AddWithValue("@p" & i.ToString(), If(kv.Value, DBNull.Value))
                i += 1
            Next
            cmd.CommandText = "INSERT INTO `" & table & "` (" & String.Join(", ", cols) &
                              ") VALUES (" & String.Join(", ", pars) & ")"
            cmd.ExecuteNonQuery()
        End Using
    End Sub

    ' Side effects the original delete undid, put back.
    Private Sub AfterRestore(conn As MySqlConnection, tx As MySqlTransaction,
                             table As String, row As Dictionary(Of String, Object))
        Select Case table
            Case "quotations"
                ' Deleting a request's last quote handed the request back to
                ' 'Approved'. With a quote on it again it is 'Quoted'.
                If row.ContainsKey("request_id") AndAlso row("request_id") IsNot Nothing Then
                    Using cmd As New MySqlCommand(
                        "UPDATE purchase_requests SET status = 'Quoted' " &
                        "WHERE request_id = @rid AND status = 'Approved'", conn, tx)
                        cmd.Parameters.AddWithValue("@rid", row("request_id"))
                        cmd.ExecuteNonQuery()
                    End Using
                End If
        End Select
    End Sub

End Module
