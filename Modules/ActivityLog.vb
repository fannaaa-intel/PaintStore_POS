Imports MySql.Data.MySqlClient

Public Module ActivityLog
    ' Fire-and-forget audit trail. Never let a logging failure break the caller.
    Public Sub Record(action As String,
                      Optional entityType As String = Nothing,
                      Optional entityId As Integer? = Nothing,
                      Optional details As String = Nothing)
        Try
            Using cn = Db.GetConnection()
                cn.Open()
                Dim sql = "INSERT INTO activity_logs (user_id, action, entity_type, entity_id, details) " &
                          "VALUES (@uid, @action, @etype, @eid, @details)"
                Using cmd As New MySqlCommand(sql, cn)
                    cmd.Parameters.AddWithValue("@uid",
                        If(Session.CurrentUserId = 0, CObj(DBNull.Value), CObj(Session.CurrentUserId)))
                    cmd.Parameters.AddWithValue("@action", action)
                    cmd.Parameters.AddWithValue("@etype",
                        If(String.IsNullOrEmpty(entityType), CObj(DBNull.Value), CObj(entityType)))
                    cmd.Parameters.AddWithValue("@eid",
                        If(entityId.HasValue, CObj(entityId.Value), CObj(DBNull.Value)))
                    cmd.Parameters.AddWithValue("@details",
                        If(String.IsNullOrEmpty(details), CObj(DBNull.Value), CObj(details)))
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        Catch
        End Try
    End Sub
End Module
