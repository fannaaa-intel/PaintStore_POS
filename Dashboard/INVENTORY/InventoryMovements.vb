Imports System.Drawing
Imports System.Globalization
Imports System.Text
Imports MySql.Data.MySqlClient

' =============================================================================
'  InventoryMovements  —  reads stock_movements for Transaction History and the
'  Inventory Reports, so both screens name, filter and total movements the
'  same way.
'
'  WHAT A ROW MEANS  (who writes it -> label)
'     Sale        POS sale                                     -> Sale
'     'void'      POS void, stock put back                     -> Sale voided
'     'return'    POS return, stock put back                   -> Customer return
'     'receive'   Procurement Receive PO, goods at the dock    -> Received (dock)
'     'putaway'   Inventory Incoming Stock, goods on the shelf -> Stocked in
'     'correction' / 'damage'   Inventory Stock Adjustment     -> Correction / Damage / loss
'     'newitem' / Initial       opening qty of a new item      -> Opening stock
'
'  SHELF vs DOCK
'  A Receive PO row does NOT change products.stock_qty -- the same units are
'  counted again when they are put away (see InventoryPutAwayPanelBuilder).
'  So dock rows are listed but left out of the Units In / Out / Net figures,
'  or every delivery would be counted twice. Very old 'receive' rows written
'  by Stock Adjustment carry no PO id and did change stock, so only rows WITH
'  a reference_id count as dock rows.
' =============================================================================
Public NotInheritable Class InventoryMovements

    Public Const DOCK_SQL As String =
        "(COALESCE(m.reference_type,'') = 'receive' AND m.reference_id IS NOT NULL)"

    Public Class MovementRow
        Public CreatedAt As DateTime
        Public ProductName As String = ""
        Public Sku As String = ""
        Public Category As String = ""
        Public MovementType As String = ""
        Public RefType As String = ""
        Public IsDock As Boolean
        Public QtyChange As Integer
        Public UserName As String = ""
        Public Note As String = ""
        Public UnitValue As Decimal      ' cost price, or selling price when no cost is recorded

        Public ReadOnly Property TypeLabel As String
            Get
                Return LabelFor(MovementType, RefType, IsDock)
            End Get
        End Property
    End Class

    ' Type filter choices, in the order the drop-down lists them.
    Public Shared ReadOnly TypeFilters As String() = {
        "All types", "Sales", "Customer returns", "Sales voided", "Stocked in",
        "Received (dock)", "Corrections", "Damage / loss", "Opening stock"
    }

    Private Shared Function TypeFilterSql(index As Integer) As String
        Select Case index
            Case 1 : Return "m.movement_type = 'Sale'"
            Case 2 : Return "(COALESCE(m.reference_type,'') = 'return' OR m.movement_type = 'Return')"
            Case 3 : Return "COALESCE(m.reference_type,'') = 'void'"
            Case 4 : Return "(m.movement_type = 'Purchase' AND NOT " & DOCK_SQL & ")"
            Case 5 : Return DOCK_SQL
            Case 6 : Return "COALESCE(m.reference_type,'') = 'correction'"
            Case 7 : Return "COALESCE(m.reference_type,'') = 'damage'"
            Case 8 : Return "(m.movement_type = 'Initial' OR COALESCE(m.reference_type,'') = 'newitem')"
            Case Else : Return ""
        End Select
    End Function

    Public Shared Function LabelFor(movementType As String, refType As String, isDock As Boolean) As String
        If isDock Then Return "Received (dock)"
        Select Case If(refType, "").ToLowerInvariant()
            Case "putaway" : Return "Stocked in"
            Case "receive" : Return "Received stock"
            Case "correction" : Return "Correction"
            Case "damage" : Return "Damage / loss"
            Case "void" : Return "Sale voided"
            Case "return" : Return "Customer return"
            Case "newitem" : Return "Opening stock"
        End Select
        Select Case movementType
            Case "Sale" : Return "Sale"
            Case "Purchase" : Return "Stocked in"
            Case "Return" : Return "Customer return"
            Case "Initial" : Return "Opening stock"
            Case Else : Return "Adjustment"
        End Select
    End Function

    ' Badge colours by label: money-out red, stock-in green, the rest neutral.
    Public Shared Function BadgeColors(label As String) As (fore As Color, back As Color)
        Select Case label
            Case "Sale"
                Return (Color.FromArgb(45, 80, 150), Color.FromArgb(228, 236, 250))
            Case "Damage / loss"
                Return (Color.FromArgb(180, 40, 40), Color.FromArgb(255, 233, 233))
            Case "Stocked in", "Received stock", "Opening stock"
                Return (Color.FromArgb(0, 120, 92), Color.FromArgb(220, 248, 240))
            Case "Customer return", "Sale voided"
                Return (Color.FromArgb(165, 110, 20), Color.FromArgb(255, 244, 224))
            Case "Received (dock)"
                Return (Color.FromArgb(110, 120, 118), Color.FromArgb(236, 240, 239))
            Case Else
                Return (Color.FromArgb(95, 70, 150), Color.FromArgb(240, 234, 250))
        End Select
    End Function

    ' "+5" / "-3" / "0", ASCII signs so Excel reads it back as a number.
    Public Shared Function Signed(n As Integer) As String
        If n > 0 Then Return "+" & n.ToString("N0", CultureInfo.InvariantCulture)
        Return n.ToString("N0", CultureInfo.InvariantCulture)
    End Function

    ' ── FILTER ────────────────────────────────────────────────────────────────
    Public Class Filter
        Public FromDate As Date = Date.Today.AddDays(-29)
        Public ToDate As Date = Date.Today
        Public Search As String = ""
        Public TypeIndex As Integer = 0
        Public CategoryId As Integer = 0
        ' Only what Inventory staff adjusted by hand (Stock Adjustment).
        Public AdjustmentsOnly As Boolean = False

        Public Function WhereSql() As String
            Dim sb As New StringBuilder("WHERE m.created_at >= @from AND m.created_at < @to ")
            If CategoryId > 0 Then sb.Append("AND p.category_id = @cat ")
            If Search <> "" Then sb.Append("AND (p.name LIKE @q OR p.sku LIKE @q) ")
            Dim t As String = TypeFilterSql(TypeIndex)
            If t <> "" Then sb.Append("AND ").Append(t).Append(" ")
            If AdjustmentsOnly Then
                sb.Append("AND COALESCE(m.reference_type,'') IN ('correction','damage') ")
            End If
            Return sb.ToString()
        End Function

        Public Sub AddParams(cmd As MySqlCommand)
            cmd.Parameters.AddWithValue("@from", FromDate.Date)
            cmd.Parameters.AddWithValue("@to", ToDate.Date.AddDays(1))
            If CategoryId > 0 Then cmd.Parameters.AddWithValue("@cat", CategoryId)
            If Search <> "" Then cmd.Parameters.AddWithValue("@q", "%" & Search & "%")
        End Sub

        Public Function PeriodText() As String
            If FromDate.Date = ToDate.Date Then Return FromDate.ToString("MMM d, yyyy")
            Return FromDate.ToString("MMM d, yyyy") & " to " & ToDate.ToString("MMM d, yyyy")
        End Function
    End Class

    Private Const JOINS As String =
        "FROM stock_movements m " &
        "JOIN products p ON p.product_id = m.product_id " &
        "LEFT JOIN categories c ON c.category_id = p.category_id " &
        "LEFT JOIN users u ON u.user_id = m.user_id "

    ' Totals over everything the filter matches (not just one page).
    ' unitsIn / unitsOut leave dock rows out; see SHELF vs DOCK above.
    Public Shared Sub LoadSummary(f As Filter, ByRef count As Integer,
                           ByRef unitsIn As Integer, ByRef unitsOut As Integer)
        count = 0 : unitsIn = 0 : unitsOut = 0
        Using conn = Db.GetConnection()
            conn.Open()
            Using cmd As New MySqlCommand(
                "SELECT COUNT(*) AS n, " &
                "COALESCE(SUM(CASE WHEN m.qty_change > 0 AND NOT " & DOCK_SQL & " THEN m.qty_change END), 0) AS ins, " &
                "COALESCE(SUM(CASE WHEN m.qty_change < 0 AND NOT " & DOCK_SQL & " THEN m.qty_change END), 0) AS outs " &
                JOINS & f.WhereSql(), conn)
                f.AddParams(cmd)
                Using rdr = cmd.ExecuteReader()
                    If rdr.Read() Then
                        count = Convert.ToInt32(rdr("n"))
                        unitsIn = Convert.ToInt32(rdr("ins"))
                        unitsOut = Convert.ToInt32(rdr("outs"))
                    End If
                End Using
            End Using
        End Using
    End Sub

    ' Newest first. limit <= 0 returns every match (for reports).
    Public Shared Function Load(f As Filter, Optional limit As Integer = 0,
                         Optional offset As Integer = 0) As List(Of MovementRow)
        Dim list As New List(Of MovementRow)
        Using conn = Db.GetConnection()
            conn.Open()
            Using cmd As New MySqlCommand(
                "SELECT m.created_at, m.movement_type, m.qty_change, m.reference_type, " &
                " m.reference_id, m.note, p.name AS pname, p.sku, " &
                " COALESCE(c.name, '') AS cname, " &
                " COALESCE(NULLIF(u.full_name, ''), u.username, '') AS uname, " &
                " IF(p.cost_price > 0, p.cost_price, p.unit_price) AS unit_value " &
                JOINS & f.WhereSql() &
                "ORDER BY m.created_at DESC, m.movement_id DESC" &
                If(limit > 0, " LIMIT @lim OFFSET @off", ""), conn)
                f.AddParams(cmd)
                If limit > 0 Then
                    cmd.Parameters.AddWithValue("@lim", limit)
                    cmd.Parameters.AddWithValue("@off", Math.Max(0, offset))
                End If
                Using rdr = cmd.ExecuteReader()
                    While rdr.Read()
                        Dim refType As String = If(IsDBNull(rdr("reference_type")), "", rdr("reference_type").ToString())
                        list.Add(New MovementRow With {
                            .CreatedAt = Convert.ToDateTime(rdr("created_at")),
                            .MovementType = rdr("movement_type").ToString(),
                            .QtyChange = Convert.ToInt32(rdr("qty_change")),
                            .RefType = refType,
                            .IsDock = refType.Equals("receive", StringComparison.OrdinalIgnoreCase) AndAlso
                                      Not IsDBNull(rdr("reference_id")),
                            .Note = If(IsDBNull(rdr("note")), "", rdr("note").ToString()),
                            .ProductName = rdr("pname").ToString(),
                            .Sku = If(IsDBNull(rdr("sku")), "", rdr("sku").ToString()),
                            .Category = rdr("cname").ToString(),
                            .UserName = rdr("uname").ToString(),
                            .UnitValue = If(IsDBNull(rdr("unit_value")), 0D, Convert.ToDecimal(rdr("unit_value")))
                        })
                    End While
                End Using
            End Using
        End Using
        Return list
    End Function

    ' Categories for a filter drop-down: index 0 is "All categories" (id 0).
    Public Shared Function LoadCategories() As List(Of (id As Integer, name As String))
        Dim list As New List(Of (id As Integer, name As String)) From {(0, "All categories")}
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand("SELECT category_id, name FROM categories ORDER BY name", conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            list.Add((Convert.ToInt32(rdr("category_id")), rdr("name").ToString()))
                        End While
                    End Using
                End Using
            End Using
        Catch
            ' The filter still works with just "All categories".
        End Try
        Return list
    End Function

End Class
