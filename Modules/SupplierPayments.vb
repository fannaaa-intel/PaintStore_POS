Imports System.Drawing
Imports System.Windows.Forms
Imports MySql.Data.MySqlClient

' =============================================================================
'  SupplierPayments
'
'  One set of rules for paying suppliers, shared by Procurement > Payment
'  Records, Accountant > Payables and the PO details window.
'
'  A purchase order can be paid in parts. Its payment status is never stored;
'  it is worked out from what has been recorded against it:
'
'    Unpaid    nothing recorded yet
'    Partial   something paid, a balance is still owed
'    Paid      purchase_orders.paid_at is set -- the payments reached the PO
'              total, or the last one was marked final (the supplier accepted
'              less, with the reason written in remarks)
'
'  Payment numbers are written from the row id, like request numbers
'  ("PAY-0007"), so they can never clash or skip.
'
'  Migration 23 adds supplier_payments.invoice_no / invoice_date. Without it
'  the invoice number is kept in reference_no, as before.
' =============================================================================
Public Module SupplierPayments

    Public Const STATUS_UNPAID As String = "Unpaid"
    Public Const STATUS_PARTIAL As String = "Partial"
    Public Const STATUS_PAID As String = "Paid"

    ''' <summary>"PAY-0007" — the one way a payment number is written.</summary>
    Public Function PaymentNo(paymentId As Integer) As String
        Return "PAY-" & paymentId.ToString("0000")
    End Function

    Public Function StatusOf(isPaid As Boolean, amountPaid As Decimal) As String
        If isPaid Then Return STATUS_PAID
        Return If(amountPaid > 0D, STATUS_PARTIAL, STATUS_UNPAID)
    End Function

    ''' <summary>Badge colours for a payment status, matching the Paid / Unpaid pills in Payables.</summary>
    Public Function StatusColors(status As String) As (fore As Color, back As Color)
        Select Case status
            Case STATUS_PAID : Return (Theme.Deep, Color.FromArgb(214, 242, 234))
            Case STATUS_PARTIAL : Return (Color.FromArgb(165, 110, 20), Color.FromArgb(255, 244, 224))
            Case Else : Return (Theme.Danger, Color.FromArgb(253, 232, 232))
        End Select
    End Function

    Public Function ColumnExists(conn As MySqlConnection, table As String, column As String,
                                 Optional tx As MySqlTransaction = Nothing) As Boolean
        Try
            Using cmd As New MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() " &
                "AND table_name = @t AND column_name = @c", conn, tx)
                cmd.Parameters.AddWithValue("@t", table)
                cmd.Parameters.AddWithValue("@c", column)
                Return Convert.ToInt32(cmd.ExecuteScalar()) > 0
            End Using
        Catch swallowed As Exception
            ErrorLog.Write("SupplierPayments.ColumnExists", swallowed)
            Return False
        End Try
    End Function

    Public Function HasInvoiceColumns(conn As MySqlConnection, Optional tx As MySqlTransaction = Nothing) As Boolean
        Return ColumnExists(conn, "supplier_payments", "invoice_no", tx)
    End Function

    ' ── A purchase order as the payment form sees it ─────────────────────────
    Public Class PayablePo
        Public PoId As Integer
        Public PoNo As String = ""
        Public Supplier As String = ""
        Public Total As Decimal
        Public Paid As Decimal
        Public IsPaid As Boolean
        Public Delivery As String = ""

        Public ReadOnly Property Balance As Decimal
            Get
                Return Math.Max(0D, Total - Paid)
            End Get
        End Property

        Public ReadOnly Property Status As String
            Get
                Return StatusOf(IsPaid, Paid)
            End Get
        End Property
    End Class

    Private Const PO_SELECT As String =
        "SELECT po.po_id, po.po_no, s.name AS sname, po.total_cost, po.status, " &
        "(po.paid_at IS NOT NULL) AS is_paid, " &
        "COALESCE((SELECT SUM(sp.amount_paid) FROM supplier_payments sp WHERE sp.po_id = po.po_id), 0) AS paid " &
        "FROM purchase_orders po JOIN suppliers s ON s.supplier_id = po.supplier_id "

    Private Function ReadPo(rdr As MySqlDataReader) As PayablePo
        Return New PayablePo With {
            .PoId = Convert.ToInt32(rdr("po_id")),
            .PoNo = rdr("po_no").ToString(),
            .Supplier = rdr("sname").ToString(),
            .Total = Convert.ToDecimal(rdr("total_cost")),
            .Paid = Convert.ToDecimal(rdr("paid")),
            .IsPaid = Convert.ToInt32(rdr("is_paid")) = 1,
            .Delivery = rdr("status").ToString()
        }
    End Function

    ''' <summary>Orders that can still take a payment: placed, not cancelled, not yet fully paid.</summary>
    Public Function LoadOpenPos() As List(Of PayablePo)
        Dim result As New List(Of PayablePo)
        Using conn = Db.GetConnection()
            conn.Open()
            Using cmd As New MySqlCommand(
                PO_SELECT &
                "WHERE po.status IN ('Ordered','Partially Received','Received') AND po.paid_at IS NULL " &
                "ORDER BY po.order_date, po.po_id", conn)
                Using rdr = cmd.ExecuteReader()
                    While rdr.Read()
                        result.Add(ReadPo(rdr))
                    End While
                End Using
            End Using
        End Using
        Return result
    End Function

    Public Function LoadPo(poId As Integer) As PayablePo
        Using conn = Db.GetConnection()
            conn.Open()
            Using cmd As New MySqlCommand(PO_SELECT & "WHERE po.po_id = @id", conn)
                cmd.Parameters.AddWithValue("@id", poId)
                Using rdr = cmd.ExecuteReader()
                    If rdr.Read() Then Return ReadPo(rdr)
                End Using
            End Using
        End Using
        Return Nothing
    End Function

End Module
