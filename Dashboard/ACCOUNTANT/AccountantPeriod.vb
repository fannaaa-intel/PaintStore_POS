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
