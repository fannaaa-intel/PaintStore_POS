Imports MySql.Data.MySqlClient

' =============================================================================
'  PaymentMethods — one place that knows how a payment method behaves.
'
'  A method used to be just a name, and the till GUESSED its behaviour from
'  that name (anything containing "cash" -> cash). migrations.sql section 8
'  adds real columns (type, is_active, note); this module reads them and,
'  crucially, falls back to the old name-guess when they are absent, so an
'  un-migrated database behaves exactly as before.
'
'  SHARED BY: the cashier's payment bar, the cash drawer, and Admin > Store
'  Settings, so the three can never disagree about what a method is.
' =============================================================================
Public Module PaymentMethods

    Public Class Method
        Public MethodId As Integer
        Public Name As String = ""
        Public Type As String = "Reference"   ' 'Cash' or 'Reference'
        Public IsActive As Boolean = True
        Public Note As String = ""

        ' True when this method takes physical cash: checkout asks for the amount
        ' tendered and gives change, and the cash drawer counts it.
        Public ReadOnly Property IsCash As Boolean
            Get
                Return String.Equals(Type, "Cash", StringComparison.OrdinalIgnoreCase)
            End Get
        End Property

        ' True for a cheque: checkout asks for the account name and check number.
        Public ReadOnly Property IsCheque As Boolean
            Get
                Return String.Equals(Type, "Cheque", StringComparison.OrdinalIgnoreCase)
            End Get
        End Property
    End Class

    Private _checked As Boolean = False
    Private _present As Boolean = False

    ' True once migrations.sql section 8 has added the behaviour columns. Probed
    ' once and cached, like AccountRequests.HasApprovalColumns.
    Public Function HasBehaviourColumns() As Boolean
        If _checked Then Return _present
        _checked = True
        Try
            Using cn = Db.GetConnection()
                cn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT COUNT(*) FROM information_schema.columns " &
                    "WHERE table_schema = DATABASE() AND table_name = 'payment_methods' " &
                    "AND column_name IN ('type','is_active','note')", cn)
                    _present = Convert.ToInt32(cmd.ExecuteScalar()) = 3
                End Using
            End Using
        Catch swallowed As Exception
            ErrorLog.Write("PaymentMethods.HasBehaviourColumns", swallowed)
            _present = False
        End Try
        Return _present
    End Function

    Public Sub ResetSchemaCache()
        _checked = False
        _chequeChecked = False
    End Sub

    ' -------------------------------------------------------------------------
    '  CHEQUE SUPPORT (migrations.sql section 17)
    ' -------------------------------------------------------------------------
    Private _chequeChecked As Boolean = False
    Private _chequePresent As Boolean = False

    ' True once payment_methods.type accepts 'Cheque' AND sales has the
    ' cheque_account_name column. Until then Cheque is not offered anywhere,
    ' because saving it would be refused by the database.
    Public Function SupportsCheque() As Boolean
        If _chequeChecked Then Return _chequePresent
        Try
            Using cn = Db.GetConnection()
                cn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT " &
                    " (SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() " &
                    "   AND table_name = 'payment_methods' AND column_name = 'type' " &
                    "   AND column_type LIKE '%''Cheque''%') + " &
                    " (SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() " &
                    "   AND table_name = 'sales' AND column_name = 'cheque_account_name')", cn)
                    _chequePresent = Convert.ToInt32(cmd.ExecuteScalar()) = 2
                End Using
            End Using
            _chequeChecked = True
        Catch swallowed As Exception
            ErrorLog.Write("PaymentMethods.SupportsCheque", swallowed)
            _chequePresent = False
        End Try
        Return _chequePresent
    End Function

    ' How a type reads in lists: what the cashier will be asked for.
    Public Function TypeDescription(type As String) As String
        Select Case If(type, "").ToLowerInvariant()
            Case "cash" : Return "Cash - gives change"
            Case "cheque" : Return "Cheque - account name + check no."
            Case Else : Return "Reference - needs a ref. no."
        End Select
    End Function

    ' The safe fallback used when the columns are absent: the same rule the app
    ' has always used, but with the "gcash" hole closed so an e-wallet whose
    ' name merely contains "cash" is not mistaken for physical cash.
    Public Function NameLooksLikeCash(name As String) As Boolean
        Dim n As String = If(name, "").ToLower()
        If n.IndexOf("gcash") >= 0 Then Return False
        Return n.IndexOf("cash") >= 0
    End Function

    ' All methods in method_id order. When the behaviour columns are present the
    ' type/active/note come straight from the row; otherwise type is inferred
    ' from the name, everything is active, and the note is blank.
    '
    ' activeOnly:=True is what the till passes, so a retired method never shows
    ' as a checkout button; Admin passes False to manage every method.
    Public Function LoadAll(activeOnly As Boolean) As List(Of Method)
        Dim list As New List(Of Method)
        Dim has As Boolean = HasBehaviourColumns()

        Try
            Using cn = Db.GetConnection()
                cn.Open()
                Dim sql As String =
                    "SELECT method_id, name" &
                    If(has, ", type, is_active, note", "") &
                    " FROM payment_methods ORDER BY method_id"
                Using cmd As New MySqlCommand(sql, cn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            Dim m As New Method With {
                                .MethodId = Convert.ToInt32(rdr("method_id")),
                                .Name = rdr("name").ToString()
                            }
                            If has Then
                                m.Type = If(IsDBNull(rdr("type")), "Reference", rdr("type").ToString())
                                m.IsActive = (Not IsDBNull(rdr("is_active"))) AndAlso Convert.ToBoolean(rdr("is_active"))
                                m.Note = If(IsDBNull(rdr("note")), "", rdr("note").ToString())
                            Else
                                m.Type = If(NameLooksLikeCash(m.Name), "Cash", "Reference")
                                m.IsActive = True
                                m.Note = ""
                            End If
                            If (Not activeOnly) OrElse m.IsActive Then list.Add(m)
                        End While
                    End Using
                End Using
            End Using
        Catch swallowed As Exception
            ErrorLog.Write("PaymentMethods.LoadAll", swallowed)
            ' A read failure returns an empty list; callers keep the till usable
            ' with their own default rather than crashing.
        End Try
        Return list
    End Function

End Module
