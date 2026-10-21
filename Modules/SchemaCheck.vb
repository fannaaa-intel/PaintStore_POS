Imports MySql.Data.MySqlClient

' =============================================================================
'  SchemaCheck - is this laptop's database as new as this build of the app?
'
'  "git pull" updates the code but not the database. When migrations.sql is
'  not run afterwards, screens that use a newer column fail one by one with
'  "Unknown column" or "Table doesn't exist" - and on a client laptop that
'  looks like the app is broken. This checks once, at the log-in screen, for
'  one table or column from each section of migrations.sql and names the
'  sections that are missing, so the fix is obvious.
'
'  When adding a section to migrations.sql, add one of its columns or tables
'  to Markers below.
' =============================================================================
Public Module SchemaCheck

    ' (migrations.sql section, "table.column" or "table")
    Private ReadOnly Markers As (Section As Integer, Name As String)() = {
        (1, "products.image_path"),
        (2, "purchase_orders.paid_at"),
        (3, "suppliers.image_path"),
        (4, "suppliers.payment_terms"),
        (6, "users.must_change_password"),
        (7, "password_reset_requests"),
        (8, "payment_methods.note"),
        (9, "suppliers.bir_no"),
        (10, "goods_receipts"),
        (11, "products.cost_price"),
        (11, "sale_items.unit_cost"),
        (12, "goods_receipts.phase"),
        (13, "supplier_payments"),
        (14, "purchase_request_items"),
        (15, "users.email"),
        (16, "password_otp"),
        (17, "sales.cheque_account_name"),
        (18, "archive"),
        (19, "payment_method_requests"),
        (20, "store_settings.sig_approved_position"),
        (21, "shifts.opening_variance"),
        (22, "goods_receipt_issues"),
        (22, "purchase_requests.review_note"),
        (23, "purchase_requests.purpose"),
        (23, "supplier_payments.invoice_date"),
        (24, "sales.exempt_id_no")
    }

    ' Section numbers of migrations.sql not yet applied, in order. Empty when
    ' the database is up to date or could not be reached (the log-in itself
    ' reports a database that is down).
    Public Function MissingSections() As List(Of Integer)
        Dim missing As New List(Of Integer)
        Dim present As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT table_name, column_name FROM information_schema.columns " &
                    "WHERE table_schema = DATABASE()", conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            Dim t As String = rdr.GetString(0)
                            present.Add(t)
                            present.Add(t & "." & rdr.GetString(1))
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            ErrorLog.Write("SchemaCheck", ex)
            Return missing
        End Try

        For Each m In Markers
            If Not present.Contains(m.Name) AndAlso Not missing.Contains(m.Section) Then
                missing.Add(m.Section)
            End If
        Next
        Return missing
    End Function

    ' Tells the user, once, which updates the database still needs.
    Public Sub WarnIfOutdated()
        Dim missing As List(Of Integer) = MissingSections()
        If missing.Count = 0 Then Return
        ErrorLog.Write("SchemaCheck", "migrations.sql sections missing: " & String.Join(", ", missing))
        AppModal.Show("This laptop's database is missing " & missing.Count &
                      If(missing.Count = 1, " update", " updates") &
                      " (migrations.sql section " & String.Join(", ", missing) & "). " &
                      "Some screens will show errors until it is updated." &
                      Environment.NewLine & Environment.NewLine &
                      "What to do: open http://localhost/phpmyadmin, click paint_store_pos on the left, " &
                      "open the SQL tab, paste everything from migrations.sql (in the app folder) and press Go. " &
                      "It is safe to run more than once and keeps all existing data.",
                      "Database Update Needed", MessageBoxButtons.OK, MessageBoxIcon.Warning)
    End Sub

End Module
