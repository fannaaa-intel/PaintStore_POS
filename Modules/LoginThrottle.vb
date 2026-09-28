' =============================================================================
'  LoginThrottle  —  slows down password guessing against the sign-in form.
'
'  Before this, a wrong password cost an attacker nothing: the form accepted
'  attempts as fast as they could be typed or scripted, so a weak password fell
'  to a dictionary run in seconds. PBKDF2 makes each *guess* expensive to
'  compute, but nothing stopped an unlimited number of them.
'
'  State is kept in memory rather than in a users column on purpose:
'    * it needs no migration, so it protects an un-migrated database too;
'    * a lockout cannot be cleared by editing a row;
'    * closing the app clears it, which is the right trade for a till that an
'      owner must always be able to get back into.
'
'  Counting is per-username so one cashier fat-fingering their password can
'  never lock out the rest of the shift.
' =============================================================================
Public Module LoginThrottle

    ' After this many consecutive failures the account pauses.
    Private Const MaxAttempts As Integer = 5
    ' How long that pause lasts.
    Private Const LockoutMinutes As Integer = 5
    ' A stale run of failures should not count forever: someone who got it wrong
    ' twice this morning is not mid-attack this afternoon.
    Private Const AttemptWindowMinutes As Integer = 15

    Private Class Attempt
        Public Count As Integer
        Public FirstFailed As DateTime
        Public LockedUntil As DateTime
    End Class

    Private ReadOnly _attempts As New Dictionary(Of String, Attempt)(StringComparer.OrdinalIgnoreCase)

    ' How much longer this username is locked out. Zero means it may try now.
    Public Function LockoutRemaining(username As String) As TimeSpan
        If String.IsNullOrWhiteSpace(username) Then Return TimeSpan.Zero
        SyncLock _attempts
            Dim a As Attempt = Nothing
            If Not _attempts.TryGetValue(username.Trim(), a) Then Return TimeSpan.Zero
            If a.LockedUntil > DateTime.Now Then Return a.LockedUntil - DateTime.Now
            Return TimeSpan.Zero
        End SyncLock
    End Function

    Public Function IsLockedOut(username As String) As Boolean
        Return LockoutRemaining(username) > TimeSpan.Zero
    End Function

    ' Call on every failed sign-in. Returns True if this failure triggered the
    ' lockout, so the caller can say so rather than repeating "invalid".
    Public Function RecordFailure(username As String) As Boolean
        If String.IsNullOrWhiteSpace(username) Then Return False
        Dim key = username.Trim()
        SyncLock _attempts
            Dim a As Attempt = Nothing
            If Not _attempts.TryGetValue(key, a) Then
                a = New Attempt With {.Count = 0, .FirstFailed = DateTime.Now}
                _attempts(key) = a
            End If

            ' Start a fresh run once the window has passed.
            If DateTime.Now - a.FirstFailed > TimeSpan.FromMinutes(AttemptWindowMinutes) Then
                a.Count = 0
                a.FirstFailed = DateTime.Now
            End If

            a.Count += 1
            If a.Count >= MaxAttempts Then
                a.LockedUntil = DateTime.Now.AddMinutes(LockoutMinutes)
                a.Count = 0
                a.FirstFailed = DateTime.Now
                ' Worth an audit row: repeated failures are what a break-in
                ' attempt looks like from the inside.
                ActivityLog.Record("LOGIN_LOCKOUT", "user", Nothing,
                                   "Too many failed log-ins for '" & key & "' — locked for " &
                                   LockoutMinutes.ToString() & " minutes")
                Return True
            End If
            Return False
        End SyncLock
    End Function

    ' Call on a successful sign-in so the next typo starts from zero.
    Public Sub RecordSuccess(username As String)
        If String.IsNullOrWhiteSpace(username) Then Return
        SyncLock _attempts
            _attempts.Remove(username.Trim())
        End SyncLock
    End Sub

    ' Attempts remaining before lockout, for the warning message.
    Public Function AttemptsLeft(username As String) As Integer
        If String.IsNullOrWhiteSpace(username) Then Return MaxAttempts
        SyncLock _attempts
            Dim a As Attempt = Nothing
            If Not _attempts.TryGetValue(username.Trim(), a) Then Return MaxAttempts
            If DateTime.Now - a.FirstFailed > TimeSpan.FromMinutes(AttemptWindowMinutes) Then Return MaxAttempts
            Return Math.Max(0, MaxAttempts - a.Count)
        End SyncLock
    End Function

End Module
