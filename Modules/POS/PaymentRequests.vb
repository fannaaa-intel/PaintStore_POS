Imports MySql.Data.MySqlClient

' =============================================================================
'  PaymentRequests - cashiers ask, the admin decides (migrations.sql section 19).
'
'  A cashier cannot change payment methods directly: a method decides how money
'  is counted in the drawer and in the books. Instead they file a request from
'  Settings > Payment Methods:
'     Add     - a new method (name, type, optional note) plus why it is needed
'     Remove  - an existing method plus why it should go
'
'  The admin sees pending requests in Settings > Payment Requests and either
'  Approves (the change is made there and then) or Rejects with a reason. The
'  cashier sees the outcome and the reason under My Requests, and gets an email
'  when the account has one.
'
'  REMOVE, WHEN SALES USE THE METHOD
'  A method that past sales point at cannot be deleted without breaking those
'  records, so approving its removal HIDES it from the till instead (is_active
'  = 0). Either way the cashier stops seeing it, which is what they asked for.
' =============================================================================
Public Module PaymentRequests

    Public Class Request
        Public RequestId As Integer
        Public Action As String = ""          ' Add / Remove
        Public MethodId As Integer = 0        ' Remove only
        Public Name As String = ""
        Public Type As String = "Reference"
        Public Note As String = ""
        Public Reason As String = ""
        Public Status As String = "Pending"   ' Pending / Approved / Rejected
        Public RequestedById As Integer
        Public RequestedBy As String = ""
        Public RequestedAt As DateTime
        Public ReviewedBy As String = ""
        Public ReviewedAt As DateTime = DateTime.MinValue
        Public RejectReason As String = ""
        Public Outcome As String = ""         ' what approving actually did

        Public ReadOnly Property Headline As String
            Get
                If Action = "Add" Then
                    Return "Add  " & Name & "  (" & Type & ")"
                End If
                Return "Remove  " & Name
            End Get
        End Property
    End Class

    Private _checked As Boolean = False
    Private _present As Boolean = False

    Public Function HasTable() As Boolean
        If _checked Then Return _present
        Try
            Using cn = Db.GetConnection()
                cn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT COUNT(*) FROM information_schema.tables " &
                    "WHERE table_schema = DATABASE() AND table_name = 'payment_method_requests'", cn)
                    _present = Convert.ToInt32(cmd.ExecuteScalar()) > 0
                End Using
            End Using
            _checked = True
        Catch swallowed As Exception
            ErrorLog.Write("PaymentRequests.HasTable", swallowed)
            _present = False
        End Try
        Return _present
    End Function

    ' -------------------------------------------------------------------------
    '  CASHIER SIDE
    ' -------------------------------------------------------------------------
    Public Function SubmitAdd(name As String, type As String, note As String, reason As String) As String
        If Not Permissions.Can(Permissions.RequestPaymentMethod) Then Return Permissions.DeniedMessage("request payment methods")
        If Not HasTable() Then Return NotSetUp()
        name = If(name, "").Trim()
        Try
            Using cn = Db.GetConnection()
                cn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT COUNT(*) FROM payment_methods WHERE LOWER(name) = LOWER(@n)", cn)
                    cmd.Parameters.AddWithValue("@n", name)
                    If Convert.ToInt32(cmd.ExecuteScalar()) > 0 Then
                        Return "A payment method called """ & name & """ already exists."
                    End If
                End Using
                Using cmd As New MySqlCommand(
                    "SELECT COUNT(*) FROM payment_method_requests " &
                    "WHERE action = 'Add' AND status = 'Pending' AND LOWER(name) = LOWER(@n)", cn)
                    cmd.Parameters.AddWithValue("@n", name)
                    If Convert.ToInt32(cmd.ExecuteScalar()) > 0 Then
                        Return "Someone already asked for """ & name & """. It is waiting for the administrator."
                    End If
                End Using
                Using cmd As New MySqlCommand(
                    "INSERT INTO payment_method_requests (action, name, type, note, reason, requested_by) " &
                    "VALUES ('Add', @n, @t, @note, @r, @uid)", cn)
                    cmd.Parameters.AddWithValue("@n", name)
                    cmd.Parameters.AddWithValue("@t", type)
                    cmd.Parameters.AddWithValue("@note", If(String.IsNullOrWhiteSpace(note), CObj(DBNull.Value), CObj(note.Trim())))
                    cmd.Parameters.AddWithValue("@r", reason.Trim())
                    cmd.Parameters.AddWithValue("@uid", Session.CurrentUserId)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
            ActivityLog.Record("PAYREQ_ADD", "payment_method", Nothing, name & " - " & Left(reason, 150))
            Return ""
        Catch ex As Exception
            Return "Could not send the request: " & ErrorLog.ForUser(ex)
        End Try
    End Function

    Public Function SubmitRemove(methodId As Integer, name As String, reason As String) As String
        If Not Permissions.Can(Permissions.RequestPaymentMethod) Then Return Permissions.DeniedMessage("request payment methods")
        If Not HasTable() Then Return NotSetUp()
        Try
            Using cn = Db.GetConnection()
                cn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT COUNT(*) FROM payment_method_requests " &
                    "WHERE action = 'Remove' AND status = 'Pending' AND method_id = @id", cn)
                    cmd.Parameters.AddWithValue("@id", methodId)
                    If Convert.ToInt32(cmd.ExecuteScalar()) > 0 Then
                        Return "Removing """ & name & """ has already been requested. It is waiting for the administrator."
                    End If
                End Using
                Using cmd As New MySqlCommand(
                    "INSERT INTO payment_method_requests (action, method_id, name, reason, requested_by) " &
                    "VALUES ('Remove', @id, @n, @r, @uid)", cn)
                    cmd.Parameters.AddWithValue("@id", methodId)
                    cmd.Parameters.AddWithValue("@n", name)
                    cmd.Parameters.AddWithValue("@r", reason.Trim())
                    cmd.Parameters.AddWithValue("@uid", Session.CurrentUserId)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
            ActivityLog.Record("PAYREQ_REMOVE", "payment_method", methodId, name & " - " & Left(reason, 150))
            Return ""
        Catch ex As Exception
            Return "Could not send the request: " & ErrorLog.ForUser(ex)
        End Try
    End Function

    ' Method ids the cashier's screen should mark "removal requested".
    Public Function PendingRemovalIds() As HashSet(Of Integer)
        Dim ids As New HashSet(Of Integer)
        If Not HasTable() Then Return ids
        Try
            Using cn = Db.GetConnection()
                cn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT method_id FROM payment_method_requests " &
                    "WHERE action = 'Remove' AND status = 'Pending' AND method_id IS NOT NULL", cn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            ids.Add(Convert.ToInt32(rdr(0)))
                        End While
                    End Using
                End Using
            End Using
        Catch swallowed As Exception
            ErrorLog.Write("PaymentRequests.PendingRemovalIds", swallowed)
        End Try
        Return ids
    End Function

    ' -------------------------------------------------------------------------
    '  LISTS
    ' -------------------------------------------------------------------------
    ' One user's own requests, newest first. Never anyone else's.
    Public Function ListMine(userId As Integer) As List(Of Request)
        Return Query("WHERE r.requested_by = @uid ORDER BY r.request_id DESC LIMIT 50", userId)
    End Function

    Public Function ListPending() As List(Of Request)
        Return Query("WHERE r.status = 'Pending' ORDER BY r.request_id", 0)
    End Function

    Public Function ListDecided() As List(Of Request)
        Return Query("WHERE r.status <> 'Pending' ORDER BY r.reviewed_at DESC, r.request_id DESC LIMIT 50", 0)
    End Function

    Public Function PendingCount() As Integer
        If Not HasTable() Then Return 0
        Try
            Using cn = Db.GetConnection()
                cn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT COUNT(*) FROM payment_method_requests WHERE status = 'Pending'", cn)
                    Return Convert.ToInt32(cmd.ExecuteScalar())
                End Using
            End Using
        Catch swallowed As Exception
            ErrorLog.Write("PaymentRequests.PendingCount", swallowed)
            Return 0
        End Try
    End Function

    Private Function Query(tail As String, userId As Integer) As List(Of Request)
        Dim list As New List(Of Request)
        If Not HasTable() Then Return list
        Using cn = Db.GetConnection()
            cn.Open()
            Using cmd As New MySqlCommand(
                "SELECT r.*, COALESCE(u.full_name, 'Unknown') AS by_name, " &
                "       COALESCE(v.full_name, '') AS reviewer_name " &
                "FROM payment_method_requests r " &
                "LEFT JOIN users u ON u.user_id = r.requested_by " &
                "LEFT JOIN users v ON v.user_id = r.reviewed_by " & tail, cn)
                cmd.Parameters.AddWithValue("@uid", userId)
                Using rdr = cmd.ExecuteReader()
                    While rdr.Read()
                        list.Add(New Request With {
                            .RequestId = Convert.ToInt32(rdr("request_id")),
                            .Action = rdr("action").ToString(),
                            .MethodId = If(IsDBNull(rdr("method_id")), 0, Convert.ToInt32(rdr("method_id"))),
                            .Name = rdr("name").ToString(),
                            .Type = If(IsDBNull(rdr("type")), "Reference", rdr("type").ToString()),
                            .Note = If(IsDBNull(rdr("note")), "", rdr("note").ToString()),
                            .Reason = rdr("reason").ToString(),
                            .Status = rdr("status").ToString(),
                            .RequestedById = If(IsDBNull(rdr("requested_by")), 0, Convert.ToInt32(rdr("requested_by"))),
                            .RequestedBy = rdr("by_name").ToString(),
                            .RequestedAt = Convert.ToDateTime(rdr("requested_at")),
                            .ReviewedBy = rdr("reviewer_name").ToString(),
                            .ReviewedAt = If(IsDBNull(rdr("reviewed_at")), DateTime.MinValue, Convert.ToDateTime(rdr("reviewed_at"))),
                            .RejectReason = If(IsDBNull(rdr("reject_reason")), "", rdr("reject_reason").ToString()),
                            .Outcome = If(IsDBNull(rdr("outcome")), "", rdr("outcome").ToString())
                        })
                    End While
                End Using
            End Using
        End Using
        Return list
    End Function

    ' -------------------------------------------------------------------------
    '  ADMIN SIDE
    ' -------------------------------------------------------------------------
    ' Makes the change and marks the request approved, in one transaction.
    ' Returns "" on success (outcome says what was done), else why not.
    Public Function Approve(req As Request, ByRef outcome As String) As String
        If Not Permissions.Can(Permissions.ManageSettings) Then Return Permissions.DeniedMessage("approve payment method requests")
        outcome = ""
        Try
            Using cn = Db.GetConnection()
                cn.Open()
                Using tx = cn.BeginTransaction()
                    ' Still pending? Another admin may have decided it already.
                    Using cmd As New MySqlCommand(
                        "SELECT status FROM payment_method_requests WHERE request_id = @id FOR UPDATE", cn, tx)
                        cmd.Parameters.AddWithValue("@id", req.RequestId)
                        If Convert.ToString(cmd.ExecuteScalar()) <> "Pending" Then
                            Return "This request was already decided."
                        End If
                    End Using

                    If req.Action = "Add" Then
                        Using cmd As New MySqlCommand(
                            "SELECT COUNT(*) FROM payment_methods WHERE LOWER(name) = LOWER(@n)", cn, tx)
                            cmd.Parameters.AddWithValue("@n", req.Name)
                            If Convert.ToInt32(cmd.ExecuteScalar()) > 0 Then
                                Return "A payment method called """ & req.Name & """ already exists. Reject this request instead."
                            End If
                        End Using
                        Dim type As String = req.Type
                        If type = "Cheque" AndAlso Not PaymentMethods.SupportsCheque() Then type = "Reference"
                        Using cmd As New MySqlCommand(
                            "INSERT INTO payment_methods (name, type, is_active, note) VALUES (@n, @t, 1, @note)", cn, tx)
                            cmd.Parameters.AddWithValue("@n", req.Name)
                            cmd.Parameters.AddWithValue("@t", type)
                            cmd.Parameters.AddWithValue("@note", If(req.Note = "", CObj(DBNull.Value), CObj(req.Note)))
                            cmd.ExecuteNonQuery()
                        End Using
                        outcome = "Added """ & req.Name & """ - the till offers it now."
                    Else
                        Dim exists As Boolean, active As Boolean, inUse As Integer, othersActive As Integer
                        Using cmd As New MySqlCommand(
                            "SELECT is_active FROM payment_methods WHERE method_id = @id", cn, tx)
                            cmd.Parameters.AddWithValue("@id", req.MethodId)
                            Dim o = cmd.ExecuteScalar()
                            exists = o IsNot Nothing AndAlso Not IsDBNull(o)
                            active = exists AndAlso Convert.ToBoolean(o)
                        End Using
                        If Not exists Then
                            outcome = """" & req.Name & """ had already been removed."
                        Else
                            Using cmd As New MySqlCommand(
                                "SELECT (SELECT COUNT(*) FROM sales WHERE method_id = @id), " &
                                "       (SELECT COUNT(*) FROM payment_methods WHERE is_active = 1 AND method_id <> @id)", cn, tx)
                                cmd.Parameters.AddWithValue("@id", req.MethodId)
                                Using rdr = cmd.ExecuteReader()
                                    rdr.Read()
                                    inUse = Convert.ToInt32(rdr(0))
                                    othersActive = Convert.ToInt32(rdr(1))
                                End Using
                            End Using
                            If othersActive = 0 Then
                                Return "This is the only payment method the till has left, so it cannot be removed."
                            End If
                            If inUse > 0 Then
                                ' Past sales point at it: hide instead of delete.
                                Using cmd As New MySqlCommand(
                                    "UPDATE payment_methods SET is_active = 0 WHERE method_id = @id", cn, tx)
                                    cmd.Parameters.AddWithValue("@id", req.MethodId)
                                    cmd.ExecuteNonQuery()
                                End Using
                                outcome = """" & req.Name & """ is hidden from the till. It was used by " &
                                          inUse.ToString("N0") & If(inUse = 1, " sale", " sales") &
                                          ", so it is kept for those records."
                            Else
                                Archive.Capture(cn, tx, "payment_methods", "method_id", req.MethodId, req.Name)
                                Using cmd As New MySqlCommand(
                                    "DELETE FROM payment_methods WHERE method_id = @id", cn, tx)
                                    cmd.Parameters.AddWithValue("@id", req.MethodId)
                                    cmd.ExecuteNonQuery()
                                End Using
                                outcome = "Removed """ & req.Name & """" &
                                          If(Archive.HasArchiveTable(), " (it is in the Recycle Bin).", ".")
                            End If
                        End If
                    End If

                    Using cmd As New MySqlCommand(
                        "UPDATE payment_method_requests SET status = 'Approved', reviewed_by = @uid, " &
                        "reviewed_at = NOW(), outcome = @o WHERE request_id = @id", cn, tx)
                        cmd.Parameters.AddWithValue("@uid", Session.CurrentUserId)
                        cmd.Parameters.AddWithValue("@o", Left(outcome, 255))
                        cmd.Parameters.AddWithValue("@id", req.RequestId)
                        cmd.ExecuteNonQuery()
                    End Using
                    tx.Commit()
                End Using
            End Using
        Catch ex As Exception
            Return "Could not approve: " & ErrorLog.ForUser(ex)
        End Try

        PaymentMethods.ResetSchemaCache()
        ActivityLog.Record("PAYREQ_APPROVE", "payment_method", If(req.MethodId = 0, CType(Nothing, Integer?), req.MethodId),
                           req.Headline & " - " & Left(outcome, 150))
        NotifyRequester(req, "approved", outcome)
        Return ""
    End Function

    Public Function Reject(req As Request, reason As String) As String
        If Not Permissions.Can(Permissions.ManageSettings) Then Return Permissions.DeniedMessage("reject payment method requests")
        If String.IsNullOrWhiteSpace(reason) Then Return "A reason is required so the cashier knows why."
        Try
            Using cn = Db.GetConnection()
                cn.Open()
                Using cmd As New MySqlCommand(
                    "UPDATE payment_method_requests SET status = 'Rejected', reviewed_by = @uid, " &
                    "reviewed_at = NOW(), reject_reason = @r WHERE request_id = @id AND status = 'Pending'", cn)
                    cmd.Parameters.AddWithValue("@uid", Session.CurrentUserId)
                    cmd.Parameters.AddWithValue("@r", reason.Trim())
                    cmd.Parameters.AddWithValue("@id", req.RequestId)
                    If cmd.ExecuteNonQuery() = 0 Then Return "This request was already decided."
                End Using
            End Using
        Catch ex As Exception
            Return "Could not reject: " & ErrorLog.ForUser(ex)
        End Try
        ActivityLog.Record("PAYREQ_REJECT", "payment_method", If(req.MethodId = 0, CType(Nothing, Integer?), req.MethodId),
                           req.Headline & " - " & Left(reason, 150))
        NotifyRequester(req, "rejected", "Reason: " & reason.Trim())
        Return ""
    End Function

    ' Emails the cashier the decision when their account has an email.
    Private Sub NotifyRequester(req As Request, verb As String, detail As String)
        Dim email As String = AccountRequests.GetEmail(req.RequestedById)
        If email = "" OrElse Not Mailer.IsConfigured() Then Return
        Mailer.SendInBackground(email,
            "Your payment method request was " & verb,
            "Hello " & req.RequestedBy & "," & vbCrLf & vbCrLf &
            "Your request """ & req.Headline.Replace("  ", " ") & """ was " & verb & " by the administrator." & vbCrLf & vbCrLf &
            detail & vbCrLf & vbCrLf &
            "You can see all your requests in Settings > Payment Methods." & vbCrLf & vbCrLf &
            "- " & StoreSettings.StoreName,
            "payment_method_request", req.RequestId)
    End Sub

    Private Function NotSetUp() As String
        Return "Payment method requests are not set up yet. Ask the administrator to run " &
               "migrations.sql section 19."
    End Function

End Module
