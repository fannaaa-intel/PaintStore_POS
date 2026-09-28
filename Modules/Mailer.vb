Imports System.Configuration
Imports System.Net
Imports System.Net.Mail

' =============================================================================
'  Mailer - sends the store's outgoing email through Gmail.
'
'  Two messages go out today:
'    - "your account was created" when an admin makes a new account
'    - the 6-digit code that confirms a password change (PasswordOtp)
'
'  CONFIGURATION
'  -------------
'  The sender lives in mail.config beside PaintStore_POS.exe (merged into
'  appSettings by App.config). It holds a Google App Password, so it is kept
'  out of App.config and out of git. See mail.config.example.
'
'  Nothing here throws: Send returns "" on success or a message the caller can
'  show, because a mail failure must never crash the screen that asked for it.
' =============================================================================
Public Module Mailer

    Private Const SmtpHost As String = "smtp.gmail.com"
    Private Const SmtpPort As Integer = 587

    Private ReadOnly Property FromAddress As String
        Get
            Return If(ConfigurationManager.AppSettings("MailFrom"), "").Trim()
        End Get
    End Property

    Private ReadOnly Property FromName As String
        Get
            Dim n As String = If(ConfigurationManager.AppSettings("MailFromName"), "").Trim()
            Return If(n = "", "Paint Store POS", n)
        End Get
    End Property

    ' Google shows the App Password in four groups of four; SMTP wants it
    ' without the spaces.
    Private ReadOnly Property AppPassword As String
        Get
            Return If(ConfigurationManager.AppSettings("MailAppPassword"), "").Replace(" ", "")
        End Get
    End Property

    ' True when a sender has been configured at all. Screens use it to explain
    ' "email is not set up" rather than reporting a raw SMTP error.
    Public Function IsConfigured() As Boolean
        Return FromAddress <> "" AndAlso AppPassword <> ""
    End Function

    ' Loose shape check - one @, a dot after it, no spaces. The real proof an
    ' address works is the code arriving there.
    Public Function IsValidAddress(address As String) As Boolean
        If String.IsNullOrWhiteSpace(address) Then Return False
        Dim a As String = address.Trim()
        If a.Contains(" ") OrElse a.Length > 120 Then Return False
        Dim at As Integer = a.IndexOf("@"c)
        If at <= 0 OrElse at <> a.LastIndexOf("@"c) Then Return False
        Dim dot As Integer = a.LastIndexOf("."c)
        Return dot > at + 1 AndAlso dot < a.Length - 1
    End Function

    ' Sends one plain-text message. Returns "" on success, else what went wrong.
    ' Blocks for a second or two, so callers show a wait cursor.
    Public Function Send(toAddress As String, subject As String, body As String) As String
        If Not IsConfigured() Then
            Return "Email is not set up on this computer (mail.config is missing)."
        End If
        If Not IsValidAddress(toAddress) Then
            Return "The email address on this account is not valid."
        End If

        Try
            ' .NET 4.7.2 normally negotiates TLS 1.2 on its own, but an older
            ' Windows default would make Gmail refuse the connection.
            ServicePointManager.SecurityProtocol =
                ServicePointManager.SecurityProtocol Or SecurityProtocolType.Tls12

            Using msg As New MailMessage()
                msg.From = New MailAddress(FromAddress, FromName)
                msg.To.Add(toAddress.Trim())
                msg.Subject = subject
                msg.Body = body
                msg.IsBodyHtml = False
                Using client As New SmtpClient(SmtpHost, SmtpPort)
                    client.EnableSsl = True
                    client.DeliveryMethod = SmtpDeliveryMethod.Network
                    client.UseDefaultCredentials = False
                    client.Credentials = New NetworkCredential(FromAddress, AppPassword)
                    client.Timeout = 20000
                    client.Send(msg)
                End Using
            End Using
            Return ""
        Catch ex As SmtpException
            Return "The email could not be sent. Check the internet connection and try again." &
                   " (" & ex.Message & ")"
        Catch ex As Exception
            Return "The email could not be sent: " & ex.Message
        End Try
    End Function

    ' Same as Send, but off the UI thread and without reporting back. For
    ' notices whose failure should not block the admin (the account exists
    ' either way); a failure is written to the activity log instead.
    Public Sub SendInBackground(toAddress As String, subject As String, body As String,
                                logEntity As String, logId As Integer)
        Dim actorId As Integer = Session.CurrentUserId
        Threading.Tasks.Task.Run(
            Sub()
                Dim err As String = Send(toAddress, subject, body)
                If err <> "" Then
                    ActivityLog.RecordAs(actorId, "EMAIL_FAILED", logEntity, logId,
                                         Left(subject & ": " & err, 255))
                End If
            End Sub)
    End Sub

    ' -------------------------------------------------------------------------
    '  MESSAGE TEMPLATES
    ' -------------------------------------------------------------------------

    Public Function AccountCreatedBody(fullName As String, username As String,
                                       roleName As String) As String
        Return "Hello " & fullName & "," & vbCrLf & vbCrLf &
               "An account has been created for you on the " & StoreSettings.StoreName &
               " system." & vbCrLf & vbCrLf &
               "  Username : " & username & vbCrLf &
               "  Role     : " & roleName & vbCrLf & vbCrLf &
               "Your administrator will give you your first password in person." & vbCrLf &
               "Change it after you sign in: Settings > Change Password." & vbCrLf & vbCrLf &
               "If you were not expecting this account, tell the store administrator." & vbCrLf & vbCrLf &
               "- " & StoreSettings.StoreName
    End Function

    Public Function OtpBody(fullName As String, code As String, minutes As Integer) As String
        Return "Hello " & fullName & "," & vbCrLf & vbCrLf &
               "Your verification code to change your password is:" & vbCrLf & vbCrLf &
               "        " & code & vbCrLf & vbCrLf &
               "It expires in " & minutes.ToString() & " minutes and works once." & vbCrLf & vbCrLf &
               "If you did not ask to change your password, do not share this code," & vbCrLf &
               "and tell the store administrator - someone may know your password." & vbCrLf & vbCrLf &
               "- " & StoreSettings.StoreName
    End Function

End Module
