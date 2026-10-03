Imports MySql.Data.MySqlClient

' =============================================================================
'  Credentials - checks a username + password outside the login screen.
'
'  Used where someone has to prove who they are while another person is
'  already signed in: an Admin approving a cashier's void, and the idle lock
'  screen. It applies the same rules as LogIn: active, Approved accounts only,
'  and the same LoginThrottle, so these boxes cannot be used to guess
'  passwords faster than the login form can.
' =============================================================================
Public Module Credentials

    Public Class Result
        Public Ok As Boolean
        Public UserId As Integer
        Public FullName As String = ""
        Public Role As String = ""
        Public Message As String = ""
    End Class

    ' requiredRole = "" accepts any role.
    Public Function Check(username As String, password As String,
                          Optional requiredRole As String = "") As Result
        Dim r As New Result()
        Dim user As String = If(username, "").Trim()
        If user = "" OrElse String.IsNullOrEmpty(password) Then
            r.Message = "Enter the username and password."
            Return r
        End If

        If LoginThrottle.IsLockedOut(user) Then
            Dim wait = LoginThrottle.LockoutRemaining(user)
            r.Message = "Too many wrong passwords. Try again in " &
                        Math.Max(1, CInt(Math.Ceiling(wait.TotalMinutes))).ToString() & " minute(s)."
            Return r
        End If

        Dim found As Boolean = False
        Dim hash As String = ""
        Dim active As Boolean = False
        Dim status As String = "Approved"
        Try
            Using cn = Db.GetConnection()
                cn.Open()
                Dim hasStatus As Boolean = AccountRequests.HasApprovalColumns()
                Using cmd As New MySqlCommand(
                    "SELECT u.user_id, u.full_name, u.password_hash, u.is_active, " &
                    If(hasStatus, "u.status, ", "") & "r.role_name " &
                    "FROM users u JOIN roles r ON u.role_id = r.role_id " &
                    "WHERE u.username = @u LIMIT 1", cn)
                    cmd.Parameters.AddWithValue("@u", user)
                    Using rdr = cmd.ExecuteReader()
                        If rdr.Read() Then
                            found = True
                            r.UserId = Convert.ToInt32(rdr("user_id"))
                            r.FullName = rdr("full_name").ToString()
                            r.Role = rdr("role_name").ToString()
                            hash = rdr("password_hash").ToString()
                            active = Convert.ToBoolean(rdr("is_active"))
                            If hasStatus AndAlso Not IsDBNull(rdr("status")) Then status = rdr("status").ToString()
                        End If
                    End Using
                End Using
            End Using
        Catch ex As Exception
            r.Message = "Could not reach the database. Check that XAMPP (MySQL) is running, then try again."
            ErrorLog.Write("Credentials.Check", ex)
            Return r
        End Try

        Dim good As Boolean = found AndAlso active AndAlso
                              String.Equals(status, "Approved", StringComparison.OrdinalIgnoreCase) AndAlso
                              PasswordHelper.VerifyPassword(password, hash)
        If Not good Then
            If LoginThrottle.RecordFailure(user) Then
                r.Message = "Too many wrong passwords. This account is paused for a few minutes."
            Else
                r.Message = "Wrong username or password."
            End If
            r.UserId = 0
            Return r
        End If

        If requiredRole <> "" AndAlso Not String.Equals(r.Role, requiredRole, StringComparison.OrdinalIgnoreCase) Then
            r.Message = "That account is not an " & requiredRole & " account."
            r.UserId = 0
            Return r
        End If

        LoginThrottle.RecordSuccess(user)
        r.Ok = True
        Return r
    End Function

End Module
