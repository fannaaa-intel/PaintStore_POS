' =============================================================================
'  DocNumber
'
'  Document numbers (sale, hold, return, PO, quotation) were built from a
'  timestamp alone: "TXN-" & Now.ToString("yyyyMMddHHmmss"). Every one of those
'  columns carries a UNIQUE constraint, so two tills completing a sale inside
'  the same second collided. The transaction rolled back correctly -- no data
'  was corrupted -- but the customer had already paid and the cashier was shown
'  a raw duplicate-key error.
'
'  Appending the cashier's user id makes a collision between two tills
'  impossible, because a given user can only be ringing up one sale at a time.
'  The two-digit year keeps the result inside the varchar(20) these columns use.
' =============================================================================
Public Module DocNumber

    Private Const MaxLength As Integer = 20

    ''' <summary>
    ''' Build a document number: prefix + timestamp + the current user id,
    ''' e.g. "TXN-260905092300-7". If the column width forces a trim, leading
    ''' digits come off the timestamp so the user id -- the part that makes the
    ''' number unique between tills -- is always kept.
    ''' </summary>
    Public Function Generate(prefix As String) As String
        Dim stamp As String = DateTime.Now.ToString("yyMMddHHmmss")
        Dim suffix As String = If(Session.CurrentUserId > 0,
                                  "-" & Session.CurrentUserId.ToString(), "")

        ' If prefix + stamp + suffix would overflow the column, drop leading
        ' digits from the STAMP rather than truncating the tail. The user id is
        ' what makes the number unique between tills, so it must survive; the
        ' leading year digits are the least significant part to lose.
        Dim room As Integer = MaxLength - prefix.Length - suffix.Length
        If room < stamp.Length Then
            stamp = If(room > 0, stamp.Substring(stamp.Length - room), "")
        End If

        Return prefix & stamp & suffix
    End Function

End Module
