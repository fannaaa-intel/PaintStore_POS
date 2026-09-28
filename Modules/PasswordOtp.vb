Imports System.Security.Cryptography
Imports MySql.Data.MySqlClient

' =============================================================================
'  PasswordOtp - the emailed 6-digit code behind the two-step password change.
'
'  Knowing a password is no longer enough to change it: the code sent to the
'  account's email has to be typed back as well. Someone who learned a
'  cashier's password (or found a till left signed in) cannot lock the owner
'  out without also having the owner's mailbox.
'
'  RULES
'    - Only a PBKDF2 hash of the code is stored (migrations.sql section 16).
'    - A code expires after CodeMinutes and allows MaxAttempts wrong guesses.
'    - Sending a new code retires every earlier unused one for that user.
'    - Expiry is judged by the database clock (NOW()), never this PC's clock,
'      so two tills with different times cannot disagree.
' =============================================================================
Public Module PasswordOtp

    Public Const CodeMinutes As Integer = 10
    Public Const MaxAttempts As Integer = 5
    Public Const ResendSeconds As Integer = 60

    Private _checked As Boolean = False
    Private _present As Boolean = False

    ' True once migrations.sql section 16 has created the table.
    Public Function HasOtpTable() As Boolean
        If _checked Then Return _present
        Try
            Using cn = Db.GetConnection()
                cn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT COUNT(*) FROM information_schema.tables " &
                    "WHERE table_schema = DATABASE() AND table_name = 'password_otp'", cn)
                    _present = Convert.ToInt32(cmd.ExecuteScalar()) > 0
                End Using
            End Using
            _checked = True
        Catch
            _present = False
        End Try
        Return _present
    End Function

    ' "maria.santos@gmail.com" -> "ma*********@gmail.com". Shown on screen so
    ' the user knows which inbox to check without the till displaying the
    ' whole address to whoever is standing behind them.
    Public Function MaskEmail(email As String) As String
        If String.IsNullOrWhiteSpace(email) Then Return ""
        Dim at As Integer = email.IndexOf("@"c)
        If at <= 0 Then Return email
        Dim keep As Integer = Math.Min(2, at)
        Return email.Substring(0, keep) & New String("*"c, Math.Max(3, at - keep)) & email.Substring(at)
    End Function

    ' Creates a code, stores its hash and emails it. Returns "" on success,
    ' otherwise the message to show.
    Public Function Issue(userId As Integer, fullName As String, email As String) As String
        If Not HasOtpTable() Then
            Return "Verification codes are not set up yet. Ask the administrator to run the " &
                   "latest database update (migrations.sql section 16)."
        End If

        Dim code As String = NewCode()

        Try
            Using cn = Db.GetConnection()
                cn.Open()
                ' Earlier codes stop working the moment a new one is asked for,
                ' so only the newest email in the inbox is ever valid.
                Using cmd As New MySqlCommand(
                    "UPDATE password_otp SET used_at = NOW() " &
                    "WHERE user_id = @uid AND used_at IS NULL", cn)
                    cmd.Parameters.AddWithValue("@uid", userId)
                    cmd.ExecuteNonQuery()
                End Using
                Using cmd As New MySqlCommand(
                    "INSERT INTO password_otp (user_id, code_hash, expires_at) " &
                    "VALUES (@uid, @h, NOW() + INTERVAL @m MINUTE)", cn)
                    cmd.Parameters.AddWithValue("@uid", userId)
                    cmd.Parameters.AddWithValue("@h", PasswordHelper.HashPassword(code))
                    cmd.Parameters.AddWithValue("@m", CodeMinutes)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        Catch ex As Exception
            Return "Could not create a verification code: " & ex.Message
        End Try

        Dim mailErr As String = Mailer.Send(email,
            "Your password change code: " & code,
            Mailer.OtpBody(fullName, code, CodeMinutes))
        If mailErr <> "" Then Return mailErr

        ActivityLog.RecordAs(userId, "PASSWORD_OTP_SENT", "user", userId, MaskEmail(email))
        Return ""
    End Function

    ' Checks a typed code against the newest live one. Returns "" when it is
    ' correct (and marks it used), otherwise the message to show.
    Public Function Verify(userId As Integer, typed As String) As String
        Dim entry As String = If(typed, "").Trim()
        If entry.Length <> 6 OrElse Not entry.All(Function(c) Char.IsDigit(c)) Then
            Return "Enter the 6-digit code from the email."
        End If

        Try
            Using cn = Db.GetConnection()
                cn.Open()
                Dim otpId As Integer = 0
                Dim hash As String = ""
                Dim attempts As Integer = 0
                Dim expired As Boolean = False

                Using cmd As New MySqlCommand(
                    "SELECT otp_id, code_hash, attempts, (expires_at < NOW()) AS expired " &
                    "FROM password_otp WHERE user_id = @uid AND used_at IS NULL " &
                    "ORDER BY otp_id DESC LIMIT 1", cn)
                    cmd.Parameters.AddWithValue("@uid", userId)
                    Using rdr = cmd.ExecuteReader()
                        If Not rdr.Read() Then
                            Return "That code is no longer valid. Press Resend code to get a new one."
                        End If
                        otpId = Convert.ToInt32(rdr("otp_id"))
                        hash = rdr("code_hash").ToString()
                        attempts = Convert.ToInt32(rdr("attempts"))
                        expired = Convert.ToInt32(rdr("expired")) = 1
                    End Using
                End Using

                If expired Then
                    Return "That code has expired. Press Resend code to get a new one."
                End If
                If attempts >= MaxAttempts Then
                    Return "Too many wrong codes. Press Resend code to get a new one."
                End If

                If Not PasswordHelper.VerifyPassword(entry, hash) Then
                    Using cmd As New MySqlCommand(
                        "UPDATE password_otp SET attempts = attempts + 1 WHERE otp_id = @id", cn)
                        cmd.Parameters.AddWithValue("@id", otpId)
                        cmd.ExecuteNonQuery()
                    End Using
                    Dim left As Integer = MaxAttempts - attempts - 1
                    If left <= 0 Then
                        Return "Too many wrong codes. Press Resend code to get a new one."
                    End If
                    Return "That code is not correct. " & left.ToString() &
                           If(left = 1, " try", " tries") & " left."
                End If

                ' Marked used in the same statement that checks it is still
                ' unused, so one code can never approve two changes.
                Using cmd As New MySqlCommand(
                    "UPDATE password_otp SET used_at = NOW() " &
                    "WHERE otp_id = @id AND used_at IS NULL", cn)
                    cmd.Parameters.AddWithValue("@id", otpId)
                    If cmd.ExecuteNonQuery() = 0 Then
                        Return "That code was already used. Press Resend code to get a new one."
                    End If
                End Using
            End Using
            Return ""
        Catch ex As Exception
            Return "Could not check the code: " & ex.Message
        End Try
    End Function

    ' Six digits from a cryptographic source; Random is predictable.
    Private Function NewCode() As String
        Dim bytes(3) As Byte
        Using rng As New RNGCryptoServiceProvider()
            rng.GetBytes(bytes)
        End Using
        Dim n As UInteger = BitConverter.ToUInt32(bytes, 0) Mod 1000000UI
        Return n.ToString("000000")
    End Function

End Module
