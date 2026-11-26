Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Globalization
Imports System.Windows.Forms

' =============================================================================
'  InventoryDashCharts  —  the two charts on the Inventory dashboard
'  (owner's revision, Nov 2026: they replace the Quick Actions bar and the
'  Low & Out of Stock / Recent Stock Movements lists).
'
'  LowStockChart   one horizontal bar per item that needs restocking (the
'                  LowStockAlert rule), worst first. The bar is stock on hand;
'                  the dark tick is the restock level, at the same x on every
'                  row, so "how far from restock" reads straight down the
'                  column. Status is never colour alone: each row also says
'                  Out of stock / Critical / Low in words (Stock Levels' tiers).
'                  Clicking a row opens Stock Adjustment for that item.
'
'  MovementChart   stock in (up) and stock out (down) per day over the last
'                  14 days, from the same stock_movements rows the old Recent
'                  Stock Movements card listed (dock receipts left out, as in
'                  every movement total). One shared axis, a legend, and a
'                  tooltip per day. Clicking opens Transaction History.
'
'  Both paint themselves with GDI+ (no chart library), double-buffered.
'  Colours: series from the validated categorical palette (aqua / orange,
'  an adjacent pair); low / out use the reserved status amber / red.
' =============================================================================

Friend NotInheritable Class DashChartColors
    Public Shared ReadOnly StockIn As Color = ColorTranslator.FromHtml("#1baf7a")
    Public Shared ReadOnly StockOut As Color = ColorTranslator.FromHtml("#eb6834")
    Public Shared ReadOnly StatusLow As Color = ColorTranslator.FromHtml("#fab219")
    Public Shared ReadOnly StatusCritical As Color = ColorTranslator.FromHtml("#ec835a")
    Public Shared ReadOnly StatusOut As Color = ColorTranslator.FromHtml("#d03b3b")
    Public Shared ReadOnly Track As Color = Color.FromArgb(238, 242, 240)
    Public Shared ReadOnly Grid As Color = Color.FromArgb(232, 236, 234)
    Public Shared ReadOnly Baseline As Color = Color.FromArgb(185, 196, 192)
    Public Shared ReadOnly HoverWash As Color = Color.FromArgb(242, 248, 246)
    Public Shared ReadOnly Ink As Color = Theme.DeepInk
    Public Shared ReadOnly Ink2 As Color = Theme.SubText
    Public Shared ReadOnly Ink3 As Color = Theme.FaintText
End Class

' ─────────────────────────────────────────────────────────────────────────────
'  LOW & OUT OF STOCK
' ─────────────────────────────────────────────────────────────────────────────
Public Class LowStockChart
    Inherits Control

    Public Class Item
        Public ProductId As Integer
        Public Name As String = ""
        Public Sku As String = ""
        Public Qty As Integer
        Public Restock As Integer
    End Class

    Public Event ItemClicked(productId As Integer)

    Private ReadOnly _items As New List(Of Item)
    Private _hover As Integer = -1
    Private ReadOnly _tip As New ToolTip With {.InitialDelay = 250, .ReshowDelay = 100}
    Private Const ROW_H As Integer = 44
    Private Const BAR_H As Integer = 14
    ' Where the restock tick sits on every row's track (fraction of the track).
    Private Const RESTOCK_AT As Single = 0.8F

    Public Sub New()
        SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or
                 ControlStyles.UserPaint Or ControlStyles.ResizeRedraw, True)
        BackColor = Color.White
    End Sub

    Public Sub SetItems(items As IEnumerable(Of Item))
        _items.Clear()
        _items.AddRange(items)
        _hover = -1
        Invalidate()
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then _tip.Dispose()
        MyBase.Dispose(disposing)
    End Sub

    Private Function RowAt(y As Integer) As Integer
        Dim i As Integer = (y - 4) \ ROW_H
        If y < 4 OrElse i < 0 OrElse i >= _items.Count Then Return -1
        Return i
    End Function

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        MyBase.OnMouseMove(e)
        Dim i As Integer = RowAt(e.Y)
        If i = _hover Then Return
        _hover = i
        Cursor = If(i >= 0, Cursors.Hand, Cursors.Default)
        If i >= 0 Then
            Dim it = _items(i)
            _tip.SetToolTip(Me, it.Name & "  (" & it.Sku & ")" & vbCrLf &
                                If(it.Qty <= 0, "Out of stock", it.Qty.ToString("N0") & " on hand") &
                                ", restock level " & it.Restock.ToString("N0") & vbCrLf &
                                "Click to adjust this item's stock")
        Else
            _tip.SetToolTip(Me, "")
        End If
        Invalidate()
    End Sub

    Protected Overrides Sub OnMouseLeave(e As EventArgs)
        MyBase.OnMouseLeave(e)
        If _hover <> -1 Then
            _hover = -1
            Invalidate()
        End If
    End Sub

    Protected Overrides Sub OnMouseClick(e As MouseEventArgs)
        MyBase.OnMouseClick(e)
        Dim i As Integer = RowAt(e.Y)
        If i >= 0 Then RaiseEvent ItemClicked(_items(i).ProductId)
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim g = e.Graphics
        g.Clear(BackColor)
        g.SmoothingMode = SmoothingMode.AntiAlias

        If _items.Count = 0 Then
            TextRenderer.DrawText(g, "Every item is comfortably above its restock level.", Theme.Regular(10.5F),
                                  ClientRectangle, DashChartColors.Ink2,
                                  TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            Return
        End If

        ' Columns: name | track | value + status word.
        Dim labelW As Integer = CInt(Math.Min(230, Math.Max(120, Width * 0.34)))
        Const VALUE_W As Integer = 96
        Dim trackL As Integer = labelW + 14
        Dim trackR As Integer = Width - VALUE_W - 8
        If trackR - trackL < 40 Then trackR = trackL + 40
        Dim trackW As Integer = trackR - trackL

        For i As Integer = 0 To _items.Count - 1
            Dim it = _items(i)
            Dim top As Integer = 4 + i * ROW_H
            If top > Height Then Exit For

            If i = _hover Then
                Using br As New SolidBrush(DashChartColors.HoverWash)
                    g.FillRectangle(br, 0, top, Width, ROW_H)
                End Using
            End If

            ' Name and SKU, text tokens only.
            TextRenderer.DrawText(g, it.Name, Theme.Semibold(10), New Rectangle(4, top + 4, labelW - 4, 20),
                                  DashChartColors.Ink, TextFormatFlags.Left Or TextFormatFlags.EndEllipsis Or
                                  TextFormatFlags.NoPrefix Or TextFormatFlags.SingleLine)
            TextRenderer.DrawText(g, it.Sku, Theme.Regular(8.5F), New Rectangle(4, top + 23, labelW - 4, 16),
                                  DashChartColors.Ink3, TextFormatFlags.Left Or TextFormatFlags.EndEllipsis Or
                                  TextFormatFlags.NoPrefix Or TextFormatFlags.SingleLine)

            ' Track: the whole row's scale, restock level at RESTOCK_AT.
            Dim barY As Integer = top + (ROW_H - BAR_H) \ 2
            Using br As New SolidBrush(DashChartColors.Track)
                g.FillRoundedRectangle(br, New Rectangle(trackL, barY, trackW, BAR_H), 4)
            End Using

            Dim restock As Integer = Math.Max(1, it.Restock)
            Dim frac As Single = Math.Min(1.0F, CSng(Math.Max(0, it.Qty)) / restock * RESTOCK_AT)
            Dim fillW As Integer = CInt(trackW * frac)
            ' Same tiers as Stock Levels: Out (0), Critical (at or below the
            ' restock level), Low (within 25% above it).
            Dim isOut As Boolean = it.Qty <= 0
            Dim isCritical As Boolean = Not isOut AndAlso it.Qty <= it.Restock
            Dim tierClr As Color = If(isOut, DashChartColors.StatusOut,
                                   If(isCritical, DashChartColors.StatusCritical, DashChartColors.StatusLow))
            Dim tierWord As String = If(isOut, "Out of stock", If(isCritical, "Critical", "Low"))
            If fillW >= 10 Then
                Using br As New SolidBrush(tierClr)
                    g.FillRoundedRectangle(br, New Rectangle(trackL, barY, fillW, BAR_H), 4)
                End Using
            ElseIf isOut Then
                ' Nothing on hand: a red stub at the baseline so the row is not blank.
                Using br As New SolidBrush(DashChartColors.StatusOut)
                    g.FillEllipse(br, trackL - 1, barY + BAR_H \ 2 - 4, 8, 8)
                End Using
            ElseIf fillW > 0 Then
                Using br As New SolidBrush(tierClr)
                    g.FillRectangle(br, trackL, barY, fillW, BAR_H)
                End Using
            End If

            ' Restock tick.
            Dim tickX As Integer = trackL + CInt(trackW * RESTOCK_AT)
            Using pen As New Pen(DashChartColors.Ink, 2)
                g.DrawLine(pen, tickX, barY - 4, tickX, barY + BAR_H + 4)
            End Using

            ' "3 / 10" and the status word.
            Dim valRect As New Rectangle(trackR + 8, top + 4, VALUE_W, 20)
            TextRenderer.DrawText(g, it.Qty.ToString("N0") & " / " & it.Restock.ToString("N0"),
                                  Theme.Semibold(10), valRect, DashChartColors.Ink,
                                  TextFormatFlags.Left Or TextFormatFlags.NoPrefix)
            Dim stRect As New Rectangle(trackR + 8 + 12, top + 23, VALUE_W - 12, 16)
            Using br As New SolidBrush(tierClr)
                g.FillEllipse(br, trackR + 8, top + 27, 8, 8)
            End Using
            TextRenderer.DrawText(g, tierWord, Theme.Regular(8.5F), stRect,
                                  DashChartColors.Ink2, TextFormatFlags.Left Or TextFormatFlags.NoPrefix)
        Next
    End Sub
End Class

' ─────────────────────────────────────────────────────────────────────────────
'  STOCK MOVEMENTS, LAST N DAYS
' ─────────────────────────────────────────────────────────────────────────────
Public Class MovementChart
    Inherits Control

    Public Class DayPoint
        Public Day As Date
        Public UnitsIn As Integer      ' >= 0
        Public UnitsOut As Integer     ' <= 0
    End Class

    Public Event ChartClicked()

    Private ReadOnly _days As New List(Of DayPoint)
    Private _hover As Integer = -1
    Private ReadOnly _tip As New ToolTip With {.InitialDelay = 150, .ReshowDelay = 50}
    Private _plot As Rectangle

    Private Const LEGEND_H As Integer = 30
    Private Const AXIS_W As Integer = 40
    Private Const XLABEL_H As Integer = 26

    Public Sub New()
        SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or
                 ControlStyles.UserPaint Or ControlStyles.ResizeRedraw, True)
        BackColor = Color.White
    End Sub

    Public Sub SetDays(days As IEnumerable(Of DayPoint))
        _days.Clear()
        _days.AddRange(days)
        _hover = -1
        Invalidate()
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then _tip.Dispose()
        MyBase.Dispose(disposing)
    End Sub

    Private ReadOnly Property HasData As Boolean
        Get
            Return _days.Any(Function(d) d.UnitsIn <> 0 OrElse d.UnitsOut <> 0)
        End Get
    End Property

    Private Function SlotAt(x As Integer, y As Integer) As Integer
        If _days.Count = 0 OrElse _plot.Width <= 0 Then Return -1
        If x < _plot.Left OrElse x >= _plot.Right OrElse y < _plot.Top - 6 OrElse y > _plot.Bottom + XLABEL_H Then Return -1
        Dim slotW As Single = _plot.Width / CSng(_days.Count)
        Return Math.Min(_days.Count - 1, CInt(Math.Floor((x - _plot.Left) / slotW)))
    End Function

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        MyBase.OnMouseMove(e)
        Dim i As Integer = SlotAt(e.X, e.Y)
        If i = _hover Then Return
        _hover = i
        Cursor = If(HasData, Cursors.Hand, Cursors.Default)
        If i >= 0 Then
            Dim d = _days(i)
            _tip.SetToolTip(Me, d.Day.ToString("ddd, MMM d", CultureInfo.InvariantCulture) & vbCrLf &
                                "Stock in:  " & Signed(d.UnitsIn) & vbCrLf &
                                "Stock out: " & Signed(d.UnitsOut) & vbCrLf &
                                "Click for Transaction History")
        Else
            _tip.SetToolTip(Me, If(HasData, "Click for Transaction History", ""))
        End If
        Invalidate()
    End Sub

    Protected Overrides Sub OnMouseLeave(e As EventArgs)
        MyBase.OnMouseLeave(e)
        If _hover <> -1 Then
            _hover = -1
            Invalidate()
        End If
    End Sub

    Protected Overrides Sub OnMouseClick(e As MouseEventArgs)
        MyBase.OnMouseClick(e)
        RaiseEvent ChartClicked()
    End Sub

    Private Shared Function Signed(n As Integer) As String
        If n > 0 Then Return "+" & n.ToString("N0", CultureInfo.InvariantCulture)
        Return n.ToString("N0", CultureInfo.InvariantCulture)
    End Function

    ' A clean axis step: 1, 2, 5, 10, 20, 50 ...
    Private Shared Function NiceCeiling(v As Integer) As Integer
        If v <= 0 Then Return 0
        Dim p As Integer = 1
        While p * 10 <= v
            p *= 10
        End While
        For Each m In {1, 2, 5, 10}
            If m * p >= v Then Return m * p
        Next
        Return 10 * p
    End Function

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim g = e.Graphics
        g.Clear(BackColor)
        g.SmoothingMode = SmoothingMode.AntiAlias

        ' Legend (two series, so always shown) with the period totals.
        Dim lx As Integer = 4
        For Each s In {(DashChartColors.StockIn, "Stock in"), (DashChartColors.StockOut, "Stock out")}
            Using br As New SolidBrush(s.Item1)
                g.FillRoundedRectangle(br, New Rectangle(lx, 9, 12, 12), 3)
            End Using
            TextRenderer.DrawText(g, s.Item2, Theme.Regular(9.5F), New Point(lx + 17, 6), DashChartColors.Ink2)
            lx += 17 + TextRenderer.MeasureText(s.Item2, Theme.Regular(9.5F)).Width + 14
        Next
        Dim totIn As Integer = _days.Sum(Function(d) d.UnitsIn)
        Dim totOut As Integer = _days.Sum(Function(d) d.UnitsOut)
        ' Plain unit counts: the legend already says which way they went, and
        ' "Out -293" read like a negative amount.
        Dim totals As String = "In " & Math.Abs(totIn).ToString("N0") & "   Out " & Math.Abs(totOut).ToString("N0")
        TextRenderer.DrawText(g, totals, Theme.Semibold(9.5F), New Rectangle(0, 6, Width - 4, 20),
                              DashChartColors.Ink, TextFormatFlags.Right Or TextFormatFlags.NoPrefix)

        _plot = New Rectangle(AXIS_W, LEGEND_H + 10, Math.Max(10, Width - AXIS_W - 6),
                              Math.Max(20, Height - LEGEND_H - 10 - XLABEL_H))

        If Not HasData Then
            TextRenderer.DrawText(g, "No stock moved in the last " & _days.Count & " days.", Theme.Regular(10.5F),
                                  _plot, DashChartColors.Ink2,
                                  TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            Return
        End If

        ' One axis from -maxOut to +maxIn, both rounded up to clean numbers.
        Dim maxIn As Integer = NiceCeiling(_days.Max(Function(d) d.UnitsIn))
        Dim maxOut As Integer = NiceCeiling(-_days.Min(Function(d) d.UnitsOut))
        If maxIn = 0 AndAlso maxOut = 0 Then maxIn = 1
        Dim span As Single = maxIn + maxOut
        Dim zeroY As Integer = _plot.Top + CInt(_plot.Height * maxIn / span)
        Dim yFor = Function(v As Integer) As Integer
                       Return zeroY - CInt(_plot.Height * v / span)
                   End Function

        ' Recessive gridlines at the extremes, the baseline a step darker.
        Using gp As New Pen(DashChartColors.Grid, 1)
            If maxIn > 0 Then g.DrawLine(gp, _plot.Left, yFor(maxIn), _plot.Right, yFor(maxIn))
            If maxOut > 0 Then g.DrawLine(gp, _plot.Left, yFor(-maxOut), _plot.Right, yFor(-maxOut))
        End Using
        Dim axisFlags As TextFormatFlags = TextFormatFlags.Right Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPrefix
        If maxIn > 0 Then
            TextRenderer.DrawText(g, Signed(maxIn), Theme.Regular(8.5F),
                                  New Rectangle(0, yFor(maxIn) - 8, AXIS_W - 6, 16), DashChartColors.Ink3, axisFlags)
        End If
        If maxOut > 0 Then
            TextRenderer.DrawText(g, Signed(-maxOut), Theme.Regular(8.5F),
                                  New Rectangle(0, yFor(-maxOut) - 8, AXIS_W - 6, 16), DashChartColors.Ink3, axisFlags)
        End If
        TextRenderer.DrawText(g, "0", Theme.Regular(8.5F), New Rectangle(0, zeroY - 8, AXIS_W - 6, 16),
                              DashChartColors.Ink3, axisFlags)

        Dim slotW As Single = _plot.Width / CSng(_days.Count)
        Dim barW As Integer = CInt(Math.Max(4, Math.Min(20, slotW * 0.56)))

        For i As Integer = 0 To _days.Count - 1
            Dim d = _days(i)
            Dim cx As Integer = _plot.Left + CInt(slotW * i + slotW / 2)

            If i = _hover Then
                Using br As New SolidBrush(DashChartColors.HoverWash)
                    g.FillRectangle(br, CInt(_plot.Left + slotW * i), _plot.Top - 6, CInt(slotW), _plot.Height + 12)
                End Using
            End If

            ' In: grows up from the baseline, rounded at the data end only.
            If d.UnitsIn > 0 Then
                Dim h As Integer = Math.Max(2, zeroY - yFor(d.UnitsIn) - 1)
                FillBar(g, DashChartColors.StockIn, New Rectangle(cx - barW \ 2, zeroY - 1 - h, barW, h), roundTop:=True)
            End If
            ' Out: grows down from the baseline.
            If d.UnitsOut < 0 Then
                Dim h As Integer = Math.Max(2, yFor(d.UnitsOut) - zeroY - 1)
                FillBar(g, DashChartColors.StockOut, New Rectangle(cx - barW \ 2, zeroY + 2, barW, h), roundTop:=False)
            End If

            ' Day labels: every other day plus today, so they never collide.
            Dim isLast As Boolean = (i = _days.Count - 1)
            If isLast OrElse (i Mod 2 = (_days.Count - 1) Mod 2 AndAlso slotW * 2 >= 44) OrElse slotW >= 44 Then
                Dim txt As String = If(isLast AndAlso d.Day = Date.Today, "Today",
                                       d.Day.ToString("MMM d", CultureInfo.InvariantCulture))
                TextRenderer.DrawText(g, txt, Theme.Regular(8.5F),
                                      New Rectangle(cx - 40, _plot.Bottom + 6, 80, 16),
                                      If(i = _hover, DashChartColors.Ink, DashChartColors.Ink3),
                                      TextFormatFlags.HorizontalCenter Or TextFormatFlags.NoPrefix)
            End If
        Next

        Using bp As New Pen(DashChartColors.Baseline, 1)
            g.DrawLine(bp, _plot.Left, zeroY, _plot.Right, zeroY)
        End Using
    End Sub

    ' A column with 4px rounded corners at its data end and a square baseline end.
    Private Shared Sub FillBar(g As Graphics, clr As Color, r As Rectangle, roundTop As Boolean)
        If r.Width <= 0 OrElse r.Height <= 0 Then Return
        Dim rad As Integer = Math.Min(4, Math.Min(r.Width \ 2, r.Height))
        Using br As New SolidBrush(clr), path As New GraphicsPath()
            If rad < 1 Then
                g.FillRectangle(br, r)
                Return
            End If
            Dim d As Integer = rad * 2
            If roundTop Then
                path.AddArc(r.Left, r.Top, d, d, 180, 90)
                path.AddArc(r.Right - d, r.Top, d, d, 270, 90)
                path.AddLine(r.Right, r.Bottom, r.Left, r.Bottom)
            Else
                path.AddLine(r.Left, r.Top, r.Right, r.Top)
                path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90)
                path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90)
            End If
            path.CloseFigure()
            g.FillPath(br, path)
        End Using
    End Sub
End Class
