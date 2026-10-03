Public Module Session
    Public CurrentUserId As Integer
    Public CurrentFullName As String
    Public CurrentUsername As String
    Public CurrentRole As String
    Public ShiftId As Integer
    Public UserQuota As Decimal

    ' Wipe the signed-in identity on logout.
    '
    ' These are module-level (process-wide) fields, so without this the previous
    ' cashier's id, name and role survived logout: anything logged before the
    ' next successful login -- an activity_logs row, a sale, a stock movement --
    ' was attributed to whoever used the till last.
    Public Sub [Clear]()
        CurrentUserId = 0
        CurrentFullName = String.Empty
        CurrentUsername = String.Empty
        CurrentRole = String.Empty
        ShiftId = 0
        UserQuota = 0D
        Permissions.Clear()
    End Sub
End Module