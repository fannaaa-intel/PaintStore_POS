Imports MySql.Data.MySqlClient

' =============================================================================
'  AccountRequests — self-service sign-up, admin approval, password resets.
'
'  One place that knows how the approval columns work, shared by the sign-up
'  screen, the login screen and Admin > User Management, so the three can never
'  disagree about what "Pending" means.
'
'  SCHEMA TOLERANCE
'  ----------------
'  Everything here works on a database where migrations.sql sections 6 and 7
'  have NOT been applied yet. HasApprovalColumns / HasResetTable are probed once
'  and cached; when they are false the callers fall back to the old behaviour
'  (sign-up creates an active account, "forgot password" shows the old notice)
'  rather than throwing. This mirrors how the supplier and payables panels treat
'  their optional columns.
' =============================================================================
Public Module AccountRequests

    ' The system's Admin role is never self-assignable from the sign-up screen.
    Public Const ADMIN_ROLE_ID As Integer = 1

    Private _approvalChecked As Boolean = False
    Private _approvalPresent As Boolean = False
    Private _resetChecked As Boolean = False
    Private _resetPresent As Boolean = False

    ' -------------------------------------------------------------------------
    '  SCHEMA PROBES
    ' -------------------------------------------------------------------------

    ' True when users.status and its companion columns exist.
    Public Function HasApprovalColumns() As Boolean
        If _approvalChecked Then Return _approvalPresent
        _approvalChecked = True
        Try
            Using cn = Db.GetConnection()
                cn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT COUNT(*) FROM information_schema.columns " &
                    "WHERE table_schema = DATABASE() AND table_name = 'users' " &
                    "AND column_name IN ('status','requested_role_id','reviewed_by'," &
                    "'reviewed_at','reject_reason','must_change_password')", cn)
                    _approvalPresent = Convert.ToInt32(cmd.ExecuteScalar()) = 6
                End Using
            End Using
        Catch
            _approvalPresent = False
        End Try
        Return _approvalPresent
    End Function

    ' True when the password_reset_requests table exists.
    Public Function HasResetTable() As Boolean
        If _resetChecked Then Return _resetPresent
        _resetChecked = True
        Try
            Using cn = Db.GetConnection()
                cn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT COUNT(*) FROM information_schema.tables " &
                    "WHERE table_schema = DATABASE() " &
                    "AND table_name = 'password_reset_requests'", cn)
                    _resetPresent = Convert.ToInt32(cmd.ExecuteScalar()) = 1
                End Using
            End Using
        Catch
            _resetPresent = False
        End Try
        Return _resetPresent
    End Function

    ' Called after migrations are applied mid-session so the probes re-run.
    Public Sub ResetSchemaCache()
        _approvalChecked = False
        _resetChecked = False
    End Sub

    ' -------------------------------------------------------------------------
    '  USERNAME AVAILABILITY
    ' -------------------------------------------------------------------------

    ' A rejected application keeps its username reserved: the row stays for the
    ' audit trail, and the person cannot immediately re-apply under the same
    ' name. Any row at all therefore counts as taken.
    Public Function UsernameTaken(username As String) As Boolean
        Try
            Using cn = Db.GetConnection()
                cn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT COUNT(*) FROM users WHERE username = @u", cn)
                    cmd.Parameters.AddWithValue("@u", username)
                    Return Convert.ToInt32(cmd.ExecuteScalar()) > 0
                End Using
            End Using
        Catch
            ' Fail closed — never insert a duplicate on the strength of a
            ' failed lookup.
            Return True
        End Try
    End Function

    ' -------------------------------------------------------------------------
    '  PASSWORD RULES
    ' -------------------------------------------------------------------------

    ' Returns "" when acceptable, otherwise the reason to show under the field.
    Public Function ValidatePassword(pw As String) As String
        If String.IsNullOrEmpty(pw) Then Return "Password is required."
        If pw.Length < 8 Then Return "Use at least 8 characters."

        Dim hasLetter As Boolean = False
        Dim hasDigit As Boolean = False
        For Each c As Char In pw
            If Char.IsLetter(c) Then hasLetter = True
            If Char.IsDigit(c) Then hasDigit = True
        Next
        If Not hasLetter OrElse Not hasDigit Then
            Return "Include at least one letter and one number."
        End If
        Return ""
    End Function

    ' 0-4, drives the strength meter on the sign-up card.
    Public Function PasswordStrength(pw As String) As Integer
        If String.IsNullOrEmpty(pw) Then Return 0
        Dim score As Integer = 0
        If pw.Length >= 8 Then score += 1
        If pw.Length >= 12 Then score += 1

        Dim hasLower = False, hasUpper = False, hasDigit = False, hasSymbol = False
        For Each c As Char In pw
            If Char.IsLower(c) Then hasLower = True
            If Char.IsUpper(c) Then hasUpper = True
            If Char.IsDigit(c) Then hasDigit = True
            If Not Char.IsLetterOrDigit(c) Then hasSymbol = True
        Next

        Dim classes As Integer = 0
        If hasLower Then classes += 1
        If hasUpper Then classes += 1
        If hasDigit Then classes += 1
        If hasSymbol Then classes += 1

        If classes >= 2 Then score += 1
        If classes >= 3 Then score += 1

        Return Math.Min(4, score)
    End Function

    ' Username rules — letters, digits, dot, underscore, hyphen.
    Public Function ValidateUsername(u As String) As String
        If String.IsNullOrWhiteSpace(u) Then Return "Username is required."
        If u.Length < 4 Then Return "Use at least 4 characters."
        If u.Length > 50 Then Return "Use 50 characters or fewer."
        For Each c As Char In u
            If Not (Char.IsLetterOrDigit(c) OrElse c = "."c OrElse c = "_"c OrElse c = "-"c) Then
                Return "Letters, numbers, dot, underscore and hyphen only."
            End If
        Next
        Return ""
    End Function

    ' -------------------------------------------------------------------------
    '  SIGN-UP
    ' -------------------------------------------------------------------------

    ' Creates the application row. Returns "" on success, else the error to show.
    '
    ' role_id is set to the requested role immediately so the row is valid
    ' against the roles foreign key, but status='Pending' is what actually
    ' gates login — the admin can still change role_id when approving.
    Public Function SubmitSignUp(fullName As String,
                                 username As String,
                                 password As String,
                                 requestedRoleId As Integer) As String

        If requestedRoleId = ADMIN_ROLE_ID Then
            Return "That role cannot be requested here."
        End If

        Try
            Dim hash As String = PasswordHelper.HashPassword(password)

            Using cn = Db.GetConnection()
                cn.Open()

                Dim sql As String
                If HasApprovalColumns() Then
                    ' is_active = 0 as well as status='Pending': a database that
                    ' is later rolled back to the old schema still refuses the
                    ' login rather than silently letting an unapproved account in.
                    sql = "INSERT INTO users (role_id, full_name, username, password_hash, " &
                          "is_active, status, requested_role_id, sales_quota) " &
                          "VALUES (@r, @f, @u, @h, 0, 'Pending', @rr, 0)"
                Else
                    ' No approval columns: keep the account inactive so an admin
                    ' still has to enable it by hand before it can be used.
                    sql = "INSERT INTO users (role_id, full_name, username, password_hash, " &
                          "is_active, sales_quota) VALUES (@r, @f, @u, @h, 0, 0)"
                End If

                Using cmd As New MySqlCommand(sql, cn)
                    cmd.Parameters.AddWithValue("@r", requestedRoleId)
                    cmd.Parameters.AddWithValue("@f", fullName)
                    cmd.Parameters.AddWithValue("@u", username)
                    cmd.Parameters.AddWithValue("@h", hash)
                    If HasApprovalColumns() Then
                        cmd.Parameters.AddWithValue("@rr", requestedRoleId)
                    End If
                    cmd.ExecuteNonQuery()
                End Using
            End Using

            ActivityLog.Record("Sign-up requested", "users", Nothing,
                               username & " requested access")
            Return ""

        Catch ex As MySqlException When ex.Number = 1062
            ' Unique index on username — someone claimed it between the
            ' availability check and this insert.
            Return "That username was just taken. Please choose another."
        Catch ex As Exception
            Return "Could not submit your request: " & ex.Message
        End Try
    End Function

    ' -------------------------------------------------------------------------
    '  PASSWORD RESET REQUESTS
    ' -------------------------------------------------------------------------

    ' Files a reset request. Deliberately returns nothing useful about whether
    ' the username exists — the login screen shows one neutral message either
    ' way, so this cannot be used to enumerate valid usernames.
    Public Sub FileResetRequest(usernameTyped As String)
        If Not HasResetTable() Then Return

        Try
            Using cn = Db.GetConnection()
                cn.Open()

                Dim uid As Object = DBNull.Value
                Using find As New MySqlCommand(
                    "SELECT user_id FROM users WHERE username = @u LIMIT 1", cn)
                    find.Parameters.AddWithValue("@u", usernameTyped)
                    Dim res = find.ExecuteScalar()
                    If res IsNot Nothing AndAlso Not IsDBNull(res) Then uid = res
                End Using

                ' One open request per username is enough; re-asking should not
                ' pile up duplicate rows for the admin to wade through.
                Using dup As New MySqlCommand(
                    "SELECT COUNT(*) FROM password_reset_requests " &
                    "WHERE username_typed = @u AND status = 'Pending'", cn)
                    dup.Parameters.AddWithValue("@u", usernameTyped)
                    If Convert.ToInt32(dup.ExecuteScalar()) > 0 Then Return
                End Using

                Using cmd As New MySqlCommand(
                    "INSERT INTO password_reset_requests (user_id, username_typed) " &
                    "VALUES (@uid, @u)", cn)
                    cmd.Parameters.AddWithValue("@uid", uid)
                    cmd.Parameters.AddWithValue("@u", usernameTyped)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        Catch
            ' Silent: the user is told "if the account exists we have notified
            ' your administrator" regardless, so surfacing an error here would
            ' only leak that something went wrong for this particular name.
        End Try
    End Sub

    ' A readable temporary password an admin can dictate over the counter.
    ' Excludes look-alike characters (0/O, 1/l/I) so it survives being read out.
    Public Function GenerateTempPassword() As String
        Const letters As String = "ABCDEFGHJKMNPQRSTUVWXYZ"
        Const digits As String = "23456789"
        Dim rnd As New Random()
        Dim sb As New Text.StringBuilder()
        For i As Integer = 1 To 4
            sb.Append(letters(rnd.Next(letters.Length)))
        Next
        sb.Append("-"c)
        For i As Integer = 1 To 4
            sb.Append(digits(rnd.Next(digits.Length)))
        Next
        Return sb.ToString()
    End Function

End Module
