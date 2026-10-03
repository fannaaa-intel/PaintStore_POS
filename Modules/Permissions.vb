Imports MySql.Data.MySqlClient

' =============================================================================
'  Permissions  —  what the signed-in user is allowed to do.
'
'  WHY THIS EXISTS
'  Session.CurrentRole used to be read only to decide which dashboard form to
'  open at login. After that the role was never consulted again: any code path
'  reached from a form could do anything, so "what may a Cashier do" existed
'  only as the implicit shape of the POS form. Every action that changes money,
'  stock, users or settings now asks this module first.
'
'  This module turns that into an explicit, queryable rule:
'
'      If Not Permissions.Can(Permissions.VoidSale) Then ... refuse
'
'  DESIGN NOTES
'  Permissions are granted to roles in the role_permissions table, so a new role
'  or a changed grant is a data edit rather than a recompile. The grants are
'  cached per session after the first read -- this is consulted on UI paths and
'  must not cost a round trip each time.
'
'  The fallback matters: if the table has not been migrated yet, DefaultsFor
'  reproduces exactly what each role could already reach through its own
'  dashboard. An un-migrated database therefore behaves as before rather than
'  locking everyone out of their own screens.
' =============================================================================
Public Module Permissions

    ' ── Permission keys ──────────────────────────────────────────────────────
    ' Named constants rather than loose strings so a typo is a compile error.
    Public Const SellItems As String = "pos.sell"
    Public Const VoidSale As String = "pos.void"
    Public Const ApplyDiscount As String = "pos.discount"
    Public Const RefundSale As String = "pos.refund"
    Public Const OpenDrawer As String = "pos.drawer"

    Public Const ViewInventory As String = "inventory.view"
    Public Const EditProduct As String = "inventory.edit"
    Public Const AdjustStock As String = "inventory.adjust"

    Public Const ViewProcurement As String = "procurement.view"
    Public Const ManagePo As String = "procurement.po"
    Public Const ReceiveGoods As String = "procurement.receive"

    Public Const ViewFinancials As String = "finance.view"
    Public Const ManagePayables As String = "finance.payables"

    Public Const RequestPurchase As String = "procurement.request"
    Public Const ApproveRequests As String = "procurement.approve"
    Public Const ManageSuppliers As String = "procurement.suppliers"

    Public Const EditSignatories As String = "finance.signatories"

    Public Const RequestPaymentMethod As String = "pos.paymethod.request"

    Public Const ManageUsers As String = "admin.users"
    Public Const ManageSettings As String = "admin.settings"
    Public Const ViewActivityLog As String = "admin.activitylog"

    Public Const BackupData As String = "data.backup"
    Public Const RestoreData As String = "data.restore"

    Private _loaded As Boolean = False
    Private _loadedForRole As String = Nothing
    Private ReadOnly _granted As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

    ' ── The question every caller asks ───────────────────────────────────────
    Public Function Can(permission As String) As Boolean
        EnsureLoaded()
        SyncLock _granted
            Return _granted.Contains(permission)
        End SyncLock
    End Function

    ' Refuse an action and say why, in one call:
    '
    '     If Not Permissions.Require(Permissions.VoidSale, "void a sale") Then Return
    '
    ' Returns True when allowed, so the caller reads as a guard clause.
    Public Function Require(permission As String, actionDescription As String) As Boolean
        If Can(permission) Then Return True
        AppModal.Show("Your role (" & If(Session.CurrentRole, "unknown") & ") is not allowed to " &
                      actionDescription & "." & Environment.NewLine &
                      "Ask an administrator if you need this.",
                      "Not Permitted", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        ' Attempted-but-blocked actions are exactly what an audit trail is for.
        ActivityLog.Record("PERMISSION_DENIED", "permission", Nothing,
                           Session.CurrentRole & " attempted: " & permission)
        Return False
    End Function

    ' For module functions that report problems as a returned message rather
    ' than a dialog. Logs the attempt the same way Require does.
    Public Function DeniedMessage(actionDescription As String) As String
        ActivityLog.Record("PERMISSION_DENIED", "permission", Nothing,
                           If(Session.CurrentRole, "") & " attempted: " & actionDescription)
        Return "Your role (" & If(String.IsNullOrEmpty(Session.CurrentRole), "not signed in", Session.CurrentRole) &
               ") is not allowed to " & actionDescription & ". Ask an administrator if you need this."
    End Function

    ' Call on logout / role change so the next user does not inherit these.
    Public Sub [Clear]()
        SyncLock _granted
            _granted.Clear()
            _loaded = False
            _loadedForRole = Nothing
        End SyncLock
    End Sub

    ' ── Loading ──────────────────────────────────────────────────────────────
    Private Sub EnsureLoaded()
        ' Reload when the signed-in role changes, so a logout/login as someone
        ' else cannot keep the previous user's grants.
        If _loaded AndAlso String.Equals(_loadedForRole, Session.CurrentRole, StringComparison.OrdinalIgnoreCase) Then Return

        SyncLock _granted
            _granted.Clear()
            _loaded = True
            _loadedForRole = Session.CurrentRole

            Dim role As String = If(Session.CurrentRole, "")
            If role = "" Then Return   ' nobody signed in: no grants at all

            Dim fromTable As Boolean = False
            Try
                Using cn = Db.GetConnection()
                    cn.Open()

                    ' The table is optional until migrations section 12 is applied.
                    Dim hasTable As Boolean = False
                    Using cmd As New MySqlCommand(
                        "SELECT COUNT(*) FROM information_schema.tables " &
                        "WHERE table_schema = DATABASE() AND table_name = 'role_permissions'", cn)
                        hasTable = Convert.ToInt32(cmd.ExecuteScalar()) > 0
                    End Using

                    If hasTable Then
                        Using cmd As New MySqlCommand(
                            "SELECT rp.permission FROM role_permissions rp " &
                            "JOIN roles r ON r.role_id = rp.role_id " &
                            "WHERE r.role_name = @r", cn)
                            cmd.Parameters.AddWithValue("@r", role)
                            Using rdr = cmd.ExecuteReader()
                                While rdr.Read()
                                    _granted.Add(rdr.GetString(0))
                                    fromTable = True
                                End While
                            End Using
                        End Using
                    End If
                End Using
            Catch ex As Exception
                ' Never block sign-in over this: fall through to the defaults.
                ErrorLog.Write("Permissions load error", ex)
            End Try

            ' No table, or a role with no rows yet: use the built-in defaults so
            ' behaviour matches what the role could already do.
            If Not fromTable Then
                For Each perm In DefaultsFor(role)
                    _granted.Add(perm)
                Next
            End If
        End SyncLock
    End Sub

    ' ── Built-in defaults ────────────────────────────────────────────────────
    '  These mirror the access each dashboard already granted implicitly, so
    '  turning permissions on changes nothing until someone edits the grants.
    Public Function DefaultsFor(role As String) As String()
        Select Case If(role, "").Trim().ToLowerInvariant()

            Case "admin"
                ' Admin keeps everything, including the permissions themselves.
                Return New String() {
                    SellItems, VoidSale, ApplyDiscount, RefundSale, OpenDrawer,
                    ViewInventory, EditProduct, AdjustStock,
                    ViewProcurement, ManagePo, ReceiveGoods,
                    RequestPurchase, ApproveRequests, ManageSuppliers,
                    ViewFinancials, ManagePayables, EditSignatories,
                    RequestPaymentMethod,
                    ManageUsers, ManageSettings, ViewActivityLog,
                    BackupData, RestoreData}

            Case "cashier"
                ' VoidSale only lets the cashier START a void: a completed
                ' sale still needs an Admin's password (AdminApprovalModal).
                Return New String() {
                    SellItems, VoidSale, ApplyDiscount, RefundSale, OpenDrawer,
                    ViewInventory, RequestPaymentMethod}

            Case "inventory"
                ' ReceiveGoods = put-away of delivered stock onto the shelf.
                Return New String() {
                    ViewInventory, EditProduct, AdjustStock, ReceiveGoods,
                    RequestPurchase, BackupData}

            Case "procurement"
                Return New String() {
                    ViewProcurement, ManagePo, ReceiveGoods, ViewInventory,
                    RequestPurchase, ManageSuppliers, ManagePayables}

            Case "accountant"
                Return New String() {
                    ViewFinancials, ManagePayables, ViewInventory, EditSignatories}

            Case Else
                ' An unrecognised role gets nothing rather than everything.
                Return New String() {}
        End Select
    End Function

End Module
