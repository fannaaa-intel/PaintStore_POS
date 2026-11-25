Imports System.Drawing
Imports System.Globalization
Imports System.Windows.Forms

' =============================================================================
'  AccountantPeriod  —  the one From / To date range shared by every
'  Accountant screen (Dashboard, Income & Expenses, Payables, Sales History,
'  Ledger, Reports).
'
'  It replaces the old Today / This Week / This Month / All Time pills. The
'  range is kept here rather than per screen so that moving from Dashboard to
'  Ledger to Reports keeps looking at the same period -- the accountant picks
'  it once instead of re-picking it on every screen.
' =============================================================================
Public NotInheritable Class AccountantPeriod

    Private Sub New()
    End Sub

    Private Shared ReadOnly SubText As Color = Theme.SubText
    Private Shared ReadOnly FaintText As Color = Theme.FaintText

    ' Default: the current month so far.
    Private Shared _from As Date = New Date(Date.Today.Year, Date.Today.Month, 1)
    Private Shared _to As Date = Date.Today

    Public Shared ReadOnly Property FromDate As Date
        Get
            Return _from
        End Get
    End Property

    Public Shared ReadOnly Property ToDate As Date
        Get
            Return _to
        End Get
    End Property

    ' SQL condition for a DATE or DATETIME column falling inside the range,
    ' To-day included. The dates come from a calendar, never from typed text,
    ' so writing them as literals is safe.
    Public Shared Function SqlRange(col As String) As String
        Return col & " >= '" & _from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) &
               "' AND " & col & " < '" & _to.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) & "'"
    End Function

    ' ── COST OF GOODS SOLD — one rule for every Accountant screen ─────────────
    ' Before Nov 2026 the Dashboard, the Income Statement and the Trial Balance
    ' each costed sales differently (captured cost / average PO cost with no
    ' status filter, so voided and held sales were costed / average cost on
    ' two statuses only), so the same period showed three gross profits.
    '
    '   unit cost  = the cost captured on the sale line when it was sold, or the
    '                product's average purchase cost for older lines without one
    '   sales      = the same statuses that count as revenue
    '   returns    = returned units go back on the shelf, so their cost comes
    '                off in the period they were returned (like the refund)
    Public Const SaleLineCostSql As String = "COALESCE(NULLIF(si.unit_cost, 0), ac.avg_cost, 0)"
    Public Const AvgCostJoinSql As String =
        "LEFT JOIN (SELECT product_id, SUM(unit_cost * qty) / NULLIF(SUM(qty), 0) AS avg_cost " &
        "           FROM po_items GROUP BY product_id) ac ON ac.product_id = si.product_id "
    Public Const RevenueStatusesSql As String = "('Completed','Partially Refunded','Refunded')"

    ' fromDay / toDay are calendar days, both included.
    Public Shared Function CostOfGoodsSold(conn As MySql.Data.MySqlClient.MySqlConnection,
                                           fromDay As Date, toDay As Date) As Decimal
        Dim sold As Decimal, returned As Decimal
        Using cmd As New MySql.Data.MySqlClient.MySqlCommand(
            "SELECT COALESCE(SUM(si.qty * " & SaleLineCostSql & "), 0) " &
            "FROM sale_items si JOIN sales s ON s.sale_id = si.sale_id " & AvgCostJoinSql &
            "WHERE s.status IN " & RevenueStatusesSql & " " &
            "AND s.sale_datetime >= @from AND s.sale_datetime < @to", conn)
            cmd.Parameters.AddWithValue("@from", fromDay.Date)
            cmd.Parameters.AddWithValue("@to", toDay.Date.AddDays(1))
            sold = Convert.ToDecimal(cmd.ExecuteScalar())
        End Using
        Using cmd As New MySql.Data.MySqlClient.MySqlCommand(
            "SELECT COALESCE(SUM(ri.qty_returned * " & SaleLineCostSql & "), 0) " &
            "FROM return_items ri JOIN returns r ON r.return_id = ri.return_id " &
            "JOIN sale_items si ON si.sale_item_id = ri.sale_item_id " & AvgCostJoinSql &
            "WHERE r.return_datetime >= @from AND r.return_datetime < @to", conn)
            cmd.Parameters.AddWithValue("@from", fromDay.Date)
            cmd.Parameters.AddWithValue("@to", toDay.Date.AddDays(1))
            returned = Convert.ToDecimal(cmd.ExecuteScalar())
        End Using
        Return sold - returned
    End Function

    ' "Sep 1, 2026 to Sep 28, 2026", or one date when From = To.
    Public Shared Function Text() As String
        If _from = _to Then Return _from.ToString("MMM d, yyyy")
        Return _from.ToString("MMM d, yyyy") & " to " & _to.ToString("MMM d, yyyy")
    End Function

    ' The From [date]  To [date] fields. onChange runs after either date is
    ' picked, with the range already corrected so From is never after To.
    Public Shared Function BuildFields(onChange As Action) As Panel
        Const GAP As Integer = 10
        Const FIELD_W As Integer = 140

        Dim host As New Panel With {
            .BackColor = Color.Transparent,
            .Height = FilterFields.FIELD_H
        }

        Dim lblFrom As New Label With {
            .Text = "From", .Font = New Font("Segoe UI Semibold", 10), .ForeColor = SubText,
            .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblTo As New Label With {
            .Text = "To", .Font = New Font("Segoe UI Semibold", 10), .ForeColor = SubText,
            .AutoSize = True, .BackColor = Color.Transparent
        }

        ' Declared first so each picker can move the other when the range would
        ' otherwise turn inside out.
        Dim setFrom As Action(Of Date) = Nothing, setTo As Action(Of Date) = Nothing

        Dim boxFrom As Panel = FilterFields.MakeDateField(FIELD_W, _from,
            Sub(d As Date)
                _from = d.Date
                If _from > _to Then
                    _to = _from
                    setTo(_to)
                End If
                onChange()
            End Sub, setFrom)

        Dim boxTo As Panel = FilterFields.MakeDateField(FIELD_W, _to,
            Sub(d As Date)
                _to = d.Date
                If _to < _from Then
                    _from = _to
                    setFrom(_from)
                End If
                onChange()
            End Sub, setTo)

        host.Controls.AddRange({lblFrom, boxFrom, lblTo, boxTo})

        ' Measured once the labels have their real size.
        Dim x As Integer = 0
        lblFrom.Location = New Point(x, (FilterFields.FIELD_H - lblFrom.PreferredHeight) \ 2)
        x += lblFrom.PreferredWidth + GAP
        boxFrom.Location = New Point(x, 0)
        x += FIELD_W + GAP * 2
        lblTo.Location = New Point(x, (FilterFields.FIELD_H - lblTo.PreferredHeight) \ 2)
        x += lblTo.PreferredWidth + GAP
        boxTo.Location = New Point(x, 0)
        x += FIELD_W

        host.Width = x
        Return host
    End Function

End Class
