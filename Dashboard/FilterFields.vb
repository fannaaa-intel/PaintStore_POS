Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms

' =============================================================================
'  FilterFields  —  the filter-bar field chrome shared by the Admin oversight
'  screens (Sales Overview, Inventory Overview).
'
'  Why these are hand-drawn rather than styled WinForms controls:
'
'  A ComboBox always paints its own border and grey 3D arrow button, and a
'  DateTimePicker its own sunken frame, so putting either inside a rounded
'  panel yields two nested borders and a system arrow on a custom field.
'  Worse, the window each one drops open is drawn by Windows: DrawItem reaches
'  the item rows but never the popup frame, which is why an owner-drawn combo
'  still shows a hard blue rectangle around its list.
'
'  So neither native popup is used. A field is a Panel that paints its own
'  face and opens a borderless Form for its list or calendar.
' =============================================================================
Public NotInheritable Class FilterFields

    Private Sub New()
    End Sub

    ' ── Palette (matches the Admin oversight screens) ────────────────────────
    Private Shared ReadOnly MintDark As Color = Color.FromArgb(10, 79, 66)
    Private Shared ReadOnly MintPrimary As Color = Color.FromArgb(0, 168, 142)
    Private Shared ReadOnly CardBg As Color = Color.White
    Private Shared ReadOnly SubText As Color = Color.FromArgb(90, 114, 107)
    Private Shared ReadOnly RowHover As Color = Color.FromArgb(224, 242, 235)
    Private Shared ReadOnly FieldBorder As Color = Color.FromArgb(214, 226, 222)

    Public Const FIELD_H As Integer = 34
    ' Breathing room between the calendar and its popup border.
    Private Const PAD_CAL As Integer = 8

    ' One popup at a time, dismissed on the next click anywhere else.
    Private Shared _openPopup As Form

    Public Shared Sub ClosePopup()
        If _openPopup IsNot Nothing Then
            Dim f = _openPopup
            _openPopup = Nothing
            f.Close()
            f.Dispose()
        End If
    End Sub

    ' A borderless popup anchored under a field. Deactivate closes it, so
    ' clicking anywhere else dismisses it like a real drop-down.
    Private Shared Function MakePopup(anchor As Control, w As Integer, h As Integer) As Form
        ClosePopup()
        Dim pt As Point = anchor.PointToScreen(New Point(0, anchor.Height + 4))
        Dim f As New Form With {
            .FormBorderStyle = FormBorderStyle.None,
            .ShowInTaskbar = False,
            .StartPosition = FormStartPosition.Manual,
            .Location = pt,
            .Size = New Size(w, h),
            .BackColor = CardBg
        }
        AddHandler f.Deactivate, Sub() ClosePopup()
        AddHandler f.Paint,
            Sub(sn, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim rc = f.ClientRectangle : rc.Inflate(-1, -1)
                Using br As New SolidBrush(CardBg)
                    e.Graphics.FillRoundedRectangle(br, rc, 10)
                End Using
                Using pn As New Pen(FieldBorder, 1.0F)
                    e.Graphics.DrawRoundedRectangle(pn, rc, 10)
                End Using
            End Sub
        _openPopup = f
        Return f
    End Function

    ' Shared field surface: rounded white body and a hairline border that turns
    ' mint while the field is hovered or open.
    Private Shared Sub PaintField(box As Panel, g As Graphics,
                                  active As Boolean, withChevron As Boolean)
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim rc = box.ClientRectangle : rc.Inflate(-1, -1)
        Using br As New SolidBrush(CardBg)
            g.FillRoundedRectangle(br, rc, 9)
        End Using
        Using pn As New Pen(If(active, MintPrimary, FieldBorder), If(active, 1.6F, 1.0F))
            g.DrawRoundedRectangle(pn, rc, 9)
        End Using
        If Not withChevron Then Return
        ' A chevron, not a button: the whole field is the click target, so a
        ' raised 3D box on the right edge would be misleading as well as ugly.
        Dim cx As Integer = box.Width - 18
        Dim cy As Integer = box.Height \ 2 - 1
        Using pn As New Pen(If(active, MintPrimary, SubText), 1.6F)
            pn.StartCap = LineCap.Round
            pn.EndCap = LineCap.Round
            g.DrawLines(pn, {New Point(cx - 4, cy - 2), New Point(cx, cy + 2),
                             New Point(cx + 4, cy - 2)})
        End Using
    End Sub

    ' A drop-down field: the face is painted here and the list is a rounded
    ' popup of hover-highlighted rows. The picked index is reported to onPick.
    Public Shared Function MakeSelectField(items As List(Of String), w As Integer,
                                           selected As Integer,
                                           onPick As Action(Of Integer)) As Panel
        Dim box As New BufferedPanel With {
            .Size = New Size(w, FIELD_H),
            .BackColor = Color.Transparent,
            .Cursor = Cursors.Hand
        }
        Dim idx As Integer = selected
        Dim hot As Boolean = False
        Dim isOpen As Boolean = False
        Dim fnt As New Font("Segoe UI", 9.5F)

        AddHandler box.Paint,
            Sub(sn, e)
                PaintField(box, e.Graphics, hot OrElse isOpen, True)
                Dim txt As String = If(idx >= 0 AndAlso idx < items.Count, items(idx), "")
                TextRenderer.DrawText(e.Graphics, txt, fnt,
                    New Rectangle(13, 0, w - 34, FIELD_H), MintDark,
                    TextFormatFlags.Left Or TextFormatFlags.VerticalCenter Or
                    TextFormatFlags.EndEllipsis)
            End Sub

        AddHandler box.MouseEnter, Sub()
                                       hot = True
                                       box.Invalidate()
                                   End Sub
        AddHandler box.MouseLeave, Sub()
                                       hot = False
                                       box.Invalidate()
                                   End Sub

        AddHandler box.Click,
            Sub()
                If isOpen Then
                    ClosePopup()
                    Return
                End If
                Const ROW_H As Integer = 30
                Dim h As Integer = items.Count * ROW_H + 12
                Dim pop As Form = MakePopup(box, w, h)
                isOpen = True
                box.Invalidate()

                Dim hoverIdx As Integer = -1
                Dim list As New BufferedPanel With {
                    .Dock = DockStyle.Fill, .BackColor = Color.Transparent
                }
                AddHandler list.Paint,
                    Sub(sn2, e2)
                        e2.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                        For i As Integer = 0 To items.Count - 1
                            Dim rr As New Rectangle(6, 6 + i * ROW_H, w - 12, ROW_H)
                            If i = hoverIdx OrElse i = idx Then
                                Using bg As New SolidBrush(RowHover)
                                    e2.Graphics.FillRoundedRectangle(bg, rr, 6)
                                End Using
                            End If
                            TextRenderer.DrawText(e2.Graphics, items(i), fnt,
                                New Rectangle(rr.X + 8, rr.Y, rr.Width - 10, rr.Height),
                                MintDark,
                                TextFormatFlags.Left Or TextFormatFlags.VerticalCenter)
                        Next
                    End Sub
                AddHandler list.MouseMove,
                    Sub(sn2, e2)
                        Dim i As Integer = (e2.Y - 6) \ ROW_H
                        If i < 0 OrElse i >= items.Count Then i = -1
                        If i <> hoverIdx Then
                            hoverIdx = i
                            list.Invalidate()
                        End If
                    End Sub
                AddHandler list.MouseLeave, Sub()
                                                hoverIdx = -1
                                                list.Invalidate()
                                            End Sub
                AddHandler list.MouseClick,
                    Sub(sn2, e2)
                        Dim i As Integer = (e2.Y - 6) \ ROW_H
                        If i < 0 OrElse i >= items.Count Then Return
                        idx = i
                        ClosePopup()
                        box.Invalidate()
                        onPick(i)
                    End Sub
                AddHandler pop.FormClosed, Sub()
                                               isOpen = False
                                               box.Invalidate()
                                           End Sub
                pop.Controls.Add(list)
                pop.Show()
            End Sub

        Return box
    End Function

    ' A date field: the face is painted here and the picker is a MonthCalendar
    ' inside the same rounded popup. setter moves the field from outside
    ' without re-entering onPick, so a From/To pair can correct each other.
    Public Shared Function MakeDateField(w As Integer, value As Date,
                                         onPick As Action(Of Date),
                                         ByRef setter As Action(Of Date)) As Panel
        Dim box As New BufferedPanel With {
            .Size = New Size(w, FIELD_H),
            .BackColor = Color.Transparent,
            .Cursor = Cursors.Hand
        }
        Dim cur As Date = value
        Dim hot As Boolean = False
        Dim isOpen As Boolean = False
        Dim fnt As New Font("Segoe UI", 9.5F)

        AddHandler box.Paint,
            Sub(sn, e)
                PaintField(box, e.Graphics, hot OrElse isOpen, False)
                TextRenderer.DrawText(e.Graphics, cur.ToString("MMM d, yyyy"), fnt,
                    New Rectangle(13, 0, w - 32, FIELD_H), MintDark,
                    TextFormatFlags.Left Or TextFormatFlags.VerticalCenter)
                Dim gx As Integer = w - 27
                Dim gy As Integer = FIELD_H \ 2 - 7
                Using pn As New Pen(If(hot OrElse isOpen, MintPrimary, SubText), 1.3F)
                    e.Graphics.DrawRectangle(pn, gx, gy + 2, 13, 11)
                    e.Graphics.DrawLine(pn, gx, gy + 6, gx + 13, gy + 6)
                    e.Graphics.DrawLine(pn, gx + 4, gy, gx + 4, gy + 3)
                    e.Graphics.DrawLine(pn, gx + 9, gy, gx + 9, gy + 3)
                End Using
            End Sub

        AddHandler box.MouseEnter, Sub()
                                       hot = True
                                       box.Invalidate()
                                   End Sub
        AddHandler box.MouseLeave, Sub()
                                       hot = False
                                       box.Invalidate()
                                   End Sub

        setter = Sub(d As Date)
                     cur = d
                     box.Invalidate()
                 End Sub

        AddHandler box.Click,
            Sub()
                If isOpen Then
                    ClosePopup()
                    Return
                End If
                Dim cal As New MonthCalendar With {
                    .MaxSelectionCount = 1,
                    .SelectionStart = cur,
                    .SelectionEnd = cur,
                    .TitleBackColor = MintPrimary,
                    .TitleForeColor = Color.White,
                    .TrailingForeColor = Color.FromArgb(170, 186, 181),
                    .ForeColor = MintDark,
                    .BackColor = CardBg,
                    .Location = New Point(PAD_CAL, PAD_CAL)
                }
                Dim pop As Form = MakePopup(box, 100, 100)
                isOpen = True
                box.Invalidate()
                AddHandler cal.DateSelected,
                    Sub(sn2, e2)
                        cur = e2.Start.Date
                        ClosePopup()
                        box.Invalidate()
                        onPick(cur)
                    End Sub
                AddHandler pop.FormClosed, Sub()
                                               isOpen = False
                                               box.Invalidate()
                                           End Sub
                ' A MonthCalendar is a native control: the form does not lay it
                ' out, it resizes ITSELF to whole month boxes once it has a
                ' handle, and AutoScaleMode.Font (inherited from the owner form)
                ' grows it again on Show -- here from 178x155 to 206x195.
                ' Sizing the popup before that happens clips the Saturday
                ' column and the bottom row. So: show first, then fit.
                pop.Controls.Add(cal)
                pop.Show()
                pop.Size = New Size(cal.Width + PAD_CAL * 2, cal.Height + PAD_CAL * 2)

                ' Position once the true size is known, so a field near an edge
                ' still opens fully on screen.
                Dim scr As Rectangle = Screen.FromControl(box).WorkingArea
                Dim anchorPt As Point = box.PointToScreen(Point.Empty)
                Dim px As Integer = Math.Min(anchorPt.X, scr.Right - pop.Width - 8)
                Dim py As Integer = anchorPt.Y + box.Height + 4
                If py + pop.Height > scr.Bottom Then
                    py = anchorPt.Y - pop.Height - 4
                End If
                pop.Location = New Point(Math.Max(scr.Left + 8, px), py)
            End Sub

        Return box
    End Function

End Class
