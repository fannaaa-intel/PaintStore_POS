Imports MySql.Data.MySqlClient

' =============================================================================
'  DeliveryIssues — problems found when a delivery is checked in.
'
'  Receive Goods records, per product line, whether what arrived was:
'
'    Defective   units arrived damaged or wrong. The receiver chooses what
'                happens to them:
'                  Return to Supplier  not stocked; the PO stays open for the
'                                      replacement units
'                  Accept as-is        stocked anyway; kept on record
'    Missing     units never arrived (short delivery). Not stocked; the PO
'                stays open, awaiting the rest from the supplier.
'
'  Stored one row per problem line in goods_receipt_issues (migrations.sql
'  section 22), linked to the goods_receipts row of the delivery it belongs
'  to. Receive Goods writes it; Delivery Tracking, the PO details window and
'  Procurement Reports read it.
'
'  Every reader is tolerant of an un-migrated database: HasTable() is False
'  and the screens simply show no problems, as before.
' =============================================================================
Public Module DeliveryIssues

    ' Values stored in goods_receipt_issues.issue_type / .action_taken. They
    ' must match the ENUMs in migrations.sql section 22 exactly.
    Public Const TYPE_DEFECTIVE As String = "Defective"
    Public Const TYPE_MISSING As String = "Missing"

    Public Const ACTION_RETURN As String = "Return to Supplier"
    Public Const ACTION_ACCEPT As String = "Accept as-is"
    Public Const ACTION_AWAITING As String = "Awaiting Supplier"

    Public Class Issue
        Public IssueId As Integer
        Public PoId As Integer
        Public ProductId As Integer
        Public ProductName As String = ""
        Public IssueType As String = ""
        Public Qty As Integer
        Public Details As String = ""
        Public Action As String = ""
        Public RecordedAt As DateTime

        ' One line a person can read: "Defective 2 - dented lids - Return to Supplier".
        Public ReadOnly Property Summary As String
            Get
                Dim s As String = IssueType & " " & Qty.ToString("N0")
                If Details <> "" Then s &= " " & ChrW(8212) & " " & Details
                s &= "   " & ChrW(8226) & "   " & Action
                Return s
            End Get
        End Property
    End Class

    ''' <summary>
    ''' Units that do NOT go into stock for this problem: defective units sent
    ''' back and units that never arrived. Accepted-as-is units are stocked.
    ''' </summary>
    Public Function UnitsNotStocked(issueType As String, action As String, qty As Integer) As Integer
        If String.Equals(issueType, TYPE_MISSING, StringComparison.OrdinalIgnoreCase) Then Return qty
        If String.Equals(action, ACTION_RETURN, StringComparison.OrdinalIgnoreCase) Then Return qty
        Return 0
    End Function

    ''' <summary>True when goods_receipt_issues exists (migration 22 applied).</summary>
    Public Function HasTable() As Boolean
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Return HasTable(conn)
            End Using
        Catch swallowed As Exception
            ErrorLog.Write("DeliveryIssues.HasTable", swallowed)
            Return False
        End Try
    End Function

    Public Function HasTable(conn As MySqlConnection, Optional tx As MySqlTransaction = Nothing) As Boolean
        Try
            Using cmd As New MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.tables " &
                "WHERE table_schema = DATABASE() AND table_name = 'goods_receipt_issues'", conn, tx)
                Return Convert.ToInt32(cmd.ExecuteScalar()) > 0
            End Using
        Catch swallowed As Exception
            ErrorLog.Write("DeliveryIssues.HasTable", swallowed)
            Return False
        End Try
    End Function

    ''' <summary>Writes one problem line inside the caller's receipt transaction.</summary>
    Public Sub Save(conn As MySqlConnection, tx As MySqlTransaction,
                    receiptId As Integer, poId As Integer, poItemId As Integer,
                    productId As Integer, issueType As String, qty As Integer,
                    details As String, action As String)
        Using cmd As New MySqlCommand(
            "INSERT INTO goods_receipt_issues " &
            "(receipt_id, po_id, po_item_id, product_id, issue_type, qty, details, " &
            " action_taken, recorded_by, recorded_at) " &
            "VALUES (@rid, @po, @poi, @pid, @type, @qty, @det, @act, @by, NOW())", conn, tx)
            cmd.Parameters.AddWithValue("@rid", If(receiptId > 0, CObj(receiptId), CObj(DBNull.Value)))
            cmd.Parameters.AddWithValue("@po", poId)
            cmd.Parameters.AddWithValue("@poi", If(poItemId > 0, CObj(poItemId), CObj(DBNull.Value)))
            cmd.Parameters.AddWithValue("@pid", productId)
            cmd.Parameters.AddWithValue("@type", issueType)
            cmd.Parameters.AddWithValue("@qty", qty)
            cmd.Parameters.AddWithValue("@det", If(String.IsNullOrWhiteSpace(details), CObj(DBNull.Value), CObj(details.Trim())))
            cmd.Parameters.AddWithValue("@act", action)
            cmd.Parameters.AddWithValue("@by", If(Session.CurrentUserId = 0, CObj(DBNull.Value), CObj(Session.CurrentUserId)))
            cmd.ExecuteNonQuery()
        End Using
    End Sub

    ''' <summary>Every problem recorded against a PO, oldest first. Empty when none or un-migrated.</summary>
    Public Function ForPo(poId As Integer) As List(Of Issue)
        Dim result As New List(Of Issue)
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                If Not HasTable(conn) Then Return result
                Using cmd As New MySqlCommand(
                    "SELECT gi.issue_id, gi.po_id, gi.product_id, p.name AS pname, gi.issue_type, " &
                    "gi.qty, COALESCE(gi.details,'') AS details, gi.action_taken, gi.recorded_at " &
                    "FROM goods_receipt_issues gi JOIN products p ON p.product_id = gi.product_id " &
                    "WHERE gi.po_id = @po ORDER BY gi.recorded_at, gi.issue_id", conn)
                    cmd.Parameters.AddWithValue("@po", poId)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            result.Add(New Issue With {
                                .IssueId = Convert.ToInt32(rdr("issue_id")),
                                .PoId = Convert.ToInt32(rdr("po_id")),
                                .ProductId = Convert.ToInt32(rdr("product_id")),
                                .ProductName = rdr("pname").ToString(),
                                .IssueType = rdr("issue_type").ToString(),
                                .Qty = Convert.ToInt32(rdr("qty")),
                                .Details = rdr("details").ToString(),
                                .Action = rdr("action_taken").ToString(),
                                .RecordedAt = Convert.ToDateTime(rdr("recorded_at"))
                            })
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            ErrorLog.Write("DeliveryIssues.ForPo (PO " & poId & ")", ex)
            result.Clear()
        End Try
        Return result
    End Function

    ''' <summary>How many problem lines each PO has, for badges on a list.</summary>
    Public Function CountsByPo() As Dictionary(Of Integer, Integer)
        Dim result As New Dictionary(Of Integer, Integer)
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                If Not HasTable(conn) Then Return result
                Using cmd As New MySqlCommand(
                    "SELECT po_id, COUNT(*) AS n FROM goods_receipt_issues GROUP BY po_id", conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            result(Convert.ToInt32(rdr("po_id"))) = Convert.ToInt32(rdr("n"))
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            ErrorLog.Write("DeliveryIssues.CountsByPo error", ex)
        End Try
        Return result
    End Function

End Module
