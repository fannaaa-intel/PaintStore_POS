Imports System.Windows.Forms
Imports System.Drawing

Public Class SmoothPanel
    Inherits Panel

    Public Sub New()
        Me.SetStyle(ControlStyles.UserPaint Or
                    ControlStyles.AllPaintingInWmPaint Or
                    ControlStyles.OptimizedDoubleBuffer Or
                    ControlStyles.ResizeRedraw, True)
        Me.UpdateStyles()
        Me.DoubleBuffered = True
    End Sub
End Class

Public Class SmoothFlowLayoutPanel
    Inherits FlowLayoutPanel

    Public Sub New()
        Me.SetStyle(ControlStyles.UserPaint Or
                    ControlStyles.AllPaintingInWmPaint Or
                    ControlStyles.OptimizedDoubleBuffer Or
                    ControlStyles.ResizeRedraw, True)
        Me.UpdateStyles()
        Me.DoubleBuffered = True
    End Sub
End Class

Public Class SmoothTableLayoutPanel
    Inherits TableLayoutPanel

    Public Sub New()
        Me.SetStyle(ControlStyles.UserPaint Or
                    ControlStyles.AllPaintingInWmPaint Or
                    ControlStyles.OptimizedDoubleBuffer Or
                    ControlStyles.ResizeRedraw, True)
        Me.UpdateStyles()
        Me.DoubleBuffered = True
    End Sub
End Class
' =============================================================================
'  QuietScrollFlowPanel
'  A FlowLayoutPanel that scrolls its own content with the mouse wheel but never
'  shows a scrollbar, and never lets the wheel chain to a scrolling ancestor.
'
'  Scrolling is done by WinForms itself (AutoScroll stays on) rather than by
'  shifting child .Top values by hand — a FlowLayoutPanel re-lays-out its
'  children on every layout pass, so hand-positioned offsets get reverted and
'  the control paints in a torn, half-scrolled state.
'
'  ShowScrollBar is disabled via the Win32 call because setting AutoScroll=False
'  is what removes the scrolling behaviour along with the bar.
' =============================================================================
Public Class QuietScrollFlowPanel
    Inherits FlowLayoutPanel

    <System.Runtime.InteropServices.DllImport("user32.dll")>
    Private Shared Function ShowScrollBar(hWnd As IntPtr, wBar As Integer,
                                          bShow As Boolean) As Boolean
    End Function

    Private Const SB_BOTH As Integer = 3
    Private Const WM_NCCALCSIZE As Integer = &H83

    Public Sub New()
        Me.SetStyle(ControlStyles.UserPaint Or
                    ControlStyles.AllPaintingInWmPaint Or
                    ControlStyles.OptimizedDoubleBuffer Or
                    ControlStyles.ResizeRedraw, True)
        Me.UpdateStyles()
        Me.DoubleBuffered = True
        Me.AutoScroll = True
        Me.FlowDirection = FlowDirection.TopDown
        Me.WrapContents = False
    End Sub

    ' True when there is content above or below the current viewport.
    Private ReadOnly Property CanScroll As Boolean
        Get
            Return Me.VerticalScroll.Maximum > Me.ClientSize.Height AndAlso
                   Me.DisplayRectangle.Height > Me.ClientSize.Height
        End Get
    End Property

    Protected Overrides Sub OnMouseWheel(e As MouseEventArgs)
        ' A list with nothing to scroll must let the wheel through to the page,
        ' otherwise hovering a short list makes the dashboard feel frozen.
        If Not CanScroll Then
            MyBase.OnMouseWheel(e)
            Return
        End If

        Dim pos As Integer = -Me.AutoScrollPosition.Y
        Dim maxPos As Integer = Math.Max(Me.DisplayRectangle.Height - Me.ClientSize.Height, 0)
        Dim [next] As Integer = Math.Max(0, Math.Min(pos - CInt(e.Delta / 120.0 * 48), maxPos))
        Me.AutoScrollPosition = New Point(0, [next])

        ' Consumed here: not calling MyBase.OnMouseWheel is what stops the wheel
        ' chaining on to the page's scroll panel while this list is scrolling.
        Dim h = TryCast(e, HandledMouseEventArgs)
        If h IsNot Nothing Then h.Handled = True
    End Sub

    Protected Overrides Sub WndProc(ByRef m As Message)
        MyBase.WndProc(m)
        ' Re-hide the bars after every frame change; WinForms re-shows them
        ' whenever the scrollable area is recalculated.
        If m.Msg = WM_NCCALCSIZE AndAlso Me.IsHandleCreated Then
            ShowScrollBar(Me.Handle, SB_BOTH, False)
        End If
    End Sub

    Protected Overrides Sub OnLayout(levent As LayoutEventArgs)
        MyBase.OnLayout(levent)
        If Me.IsHandleCreated Then ShowScrollBar(Me.Handle, SB_BOTH, False)
    End Sub

    ' Child controls receive the wheel instead of this panel, so every descendant
    ' relays it back here. Done in the control itself so callers just add tiles.
    Protected Overrides Sub OnControlAdded(e As ControlEventArgs)
        MyBase.OnControlAdded(e)
        HookDescendants(e.Control)
    End Sub

    Private Sub HookDescendants(c As Control)
        AddHandler c.MouseWheel, AddressOf RelayWheel
        AddHandler c.ControlAdded, Sub(s, ev) HookDescendants(ev.Control)
        For Each child As Control In c.Controls
            HookDescendants(child)
        Next
    End Sub

    Private Sub RelayWheel(sender As Object, e As MouseEventArgs)
        Me.OnMouseWheel(e)
    End Sub
End Class

' =============================================================================
'  QuietScrollPanel
'  A plain Panel that scrolls its content with the mouse wheel but never shows a
'  scrollbar. Same trick as QuietScrollFlowPanel, but for a panel whose children
'  are positioned by hand (absolute .Top) rather than flow-stacked - which is
'  what the Admin nav list needs: 12 buttons in named sections that must keep
'  their exact positions while still scrolling on a short screen.
' =============================================================================
Public Class QuietScrollPanel
    Inherits Panel

    <System.Runtime.InteropServices.DllImport("user32.dll")>
    Private Shared Function ShowScrollBar(hWnd As IntPtr, wBar As Integer,
                                          bShow As Boolean) As Boolean
    End Function

    Private Const SB_BOTH As Integer = 3
    Private Const WM_NCCALCSIZE As Integer = &H83

    Public Sub New()
        Me.SetStyle(ControlStyles.OptimizedDoubleBuffer Or
                    ControlStyles.ResizeRedraw, True)
        Me.UpdateStyles()
        Me.DoubleBuffered = True
        Me.AutoScroll = True
    End Sub

    ' True when there is content above or below the current viewport.
    Private ReadOnly Property CanScroll As Boolean
        Get
            Return Me.DisplayRectangle.Height > Me.ClientSize.Height
        End Get
    End Property

    Protected Overrides Sub OnMouseWheel(e As MouseEventArgs)
        ' Nothing to scroll: let the wheel through so hovering a short list does
        ' not feel frozen.
        If Not CanScroll Then
            MyBase.OnMouseWheel(e)
            Return
        End If

        Dim pos As Integer = -Me.AutoScrollPosition.Y
        Dim maxPos As Integer = Math.Max(Me.DisplayRectangle.Height - Me.ClientSize.Height, 0)
        Dim [next] As Integer = Math.Max(0, Math.Min(pos - CInt(e.Delta / 120.0 * 48), maxPos))
        Me.AutoScrollPosition = New Point(0, [next])

        Dim h = TryCast(e, HandledMouseEventArgs)
        If h IsNot Nothing Then h.Handled = True
    End Sub

    Protected Overrides Sub WndProc(ByRef m As Message)
        MyBase.WndProc(m)
        ' Re-hide the bars after every frame change; WinForms re-shows them
        ' whenever the scrollable area is recalculated.
        If m.Msg = WM_NCCALCSIZE AndAlso Me.IsHandleCreated Then
            ShowScrollBar(Me.Handle, SB_BOTH, False)
        End If
    End Sub

    Protected Overrides Sub OnLayout(levent As LayoutEventArgs)
        MyBase.OnLayout(levent)
        If Me.IsHandleCreated Then ShowScrollBar(Me.Handle, SB_BOTH, False)
    End Sub

    ' Child controls receive the wheel instead of this panel, so every descendant
    ' relays it back here.
    Protected Overrides Sub OnControlAdded(e As ControlEventArgs)
        MyBase.OnControlAdded(e)
        HookDescendants(e.Control)
    End Sub

    Private Sub HookDescendants(c As Control)
        AddHandler c.MouseWheel, AddressOf RelayWheel
        AddHandler c.ControlAdded, Sub(s, ev) HookDescendants(ev.Control)
        For Each child As Control In c.Controls
            HookDescendants(child)
        Next
    End Sub

    Private Sub RelayWheel(sender As Object, e As MouseEventArgs)
        Me.OnMouseWheel(e)
    End Sub
End Class


' ─────────────────────────────────────────────────────────────────────────────
'  CardTableBody
'  Scrollable row host that lives inside a rounded card. Cards are drawn by
'  PanelRenderer with a 14px radius + hairline border; a plain square child
'  panel painted over that erases the corners and the side border, which is
'  what made the POS tables look like broken containers.
'
'  This control clips itself to the card's rounded bottom corners, redraws the
'  side/bottom hairline so the outline stays continuous, and renders a centred
'  empty-state message when no rows are present instead of a blank void.
' ─────────────────────────────────────────────────────────────────────────────
Public Class CardTableBody
    Inherits Panel

    Private _radius As Integer = 14
    Private _roundBottom As Boolean = False
    Private _emptyText As String = "No records to display."
    Private ReadOnly _border As Color = Color.FromArgb(170, 185, 178)

    ''' <summary>Corner radius of the parent card. Must match PanelRenderer.</summary>
    Public Property CornerRadius As Integer
        Get
            Return _radius
        End Get
        Set(value As Integer)
            _radius = value
            ApplyRegion()
        End Set
    End Property

    ''' <summary>True when this panel is the last element in the card (no footer below).</summary>
    Public Property RoundBottom As Boolean
        Get
            Return _roundBottom
        End Get
        Set(value As Boolean)
            _roundBottom = value
            ApplyRegion()
        End Set
    End Property

    ''' <summary>Message shown centred when the panel holds no rows.</summary>
    Public Property EmptyText As String
        Get
            Return _emptyText
        End Get
        Set(value As String)
            _emptyText = value
            Invalidate()
        End Set
    End Property

    Public Sub New()
        Me.DoubleBuffered = True
        Me.SetStyle(ControlStyles.OptimizedDoubleBuffer Or
                    ControlStyles.AllPaintingInWmPaint Or
                    ControlStyles.UserPaint Or
                    ControlStyles.ResizeRedraw, True)
        Me.AutoScroll = True
        Me.BackColor = Color.White
    End Sub

    ' Rows are added/removed constantly; keep the empty-state in sync.
    Protected Overrides Sub OnControlAdded(e As ControlEventArgs)
        MyBase.OnControlAdded(e)
        Invalidate()
    End Sub

    Protected Overrides Sub OnControlRemoved(e As ControlEventArgs)
        MyBase.OnControlRemoved(e)
        Invalidate()
    End Sub

    Protected Overrides Sub OnResize(eventargs As EventArgs)
        MyBase.OnResize(eventargs)
        ApplyRegion()
    End Sub

    ' Clip the bottom corners so the card's rounded edge shows through.
    Private Sub ApplyRegion()
        If Width <= 0 OrElse Height <= 0 Then Return

        Dim old = Me.Region
        If Not _roundBottom OrElse _radius <= 0 Then
            Me.Region = Nothing
        Else
            Dim d As Integer = _radius * 2
            Using gp As New Drawing2D.GraphicsPath()
                gp.AddLine(0, 0, Width, 0)
                gp.AddLine(Width, 0, Width, Height - _radius)
                gp.AddArc(New Rectangle(Width - d, Height - d, d, d), 0, 90)
                gp.AddArc(New Rectangle(0, Height - d, d, d), 90, 90)
                gp.AddLine(0, Height - _radius, 0, 0)
                gp.CloseFigure()
                Me.Region = New Region(gp)
            End Using
        End If
        If old IsNot Nothing Then old.Dispose()
    End Sub

    Protected Overrides Sub OnPaintBackground(e As PaintEventArgs)
        Using b As New SolidBrush(Me.BackColor)
            e.Graphics.FillRectangle(b, e.ClipRectangle)
        End Using
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        MyBase.OnPaint(e)
        Dim g = e.Graphics
        g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias

        ' Empty state — centred, so an empty table reads as intentional.
        If Me.Controls.Count = 0 AndAlso Not String.IsNullOrEmpty(_emptyText) Then
            Using f As New Font("Segoe UI", 10.5F),
                  br As New SolidBrush(Color.FromArgb(150, 168, 163)),
                  sf As New StringFormat With {
                      .Alignment = StringAlignment.Center,
                      .LineAlignment = StringAlignment.Center
                  }
                g.DrawString(_emptyText, f, br, New RectangleF(0, 0, Width, Height), sf)
            End Using
        End If

        ' This panel is inset inside the card, so the card's own hairline is
        ' already visible to either side -- drawing side borders here would
        ' double them up. Only round off the bottom when nothing follows.
        If _roundBottom AndAlso _radius > 0 Then
            Using p As New Pen(_border, 1.0F)
                Dim d As Integer = _radius * 2
                Dim bottom As Integer = Height - 1
                Dim rx As Integer = Width - 1
                g.DrawArc(p, New Rectangle(0, bottom - d, d, d), 90, 90)
                g.DrawArc(p, New Rectangle(rx - d, bottom - d, d, d), 0, 90)
                g.DrawLine(p, _radius, bottom, rx - _radius, bottom)
            End Using
        End If
    End Sub
End Class

' ─────────────────────────────────────────────────────────────────────────────
'  CardFooterPanel
'  Bottom strip of a rounded card (totals, paging counts). Clips to the card's
'  bottom radius and redraws the side/bottom hairline, so the footer no longer
'  squares off the card and hangs outside its outline.
' ─────────────────────────────────────────────────────────────────────────────
Public Class CardFooterPanel
    Inherits Panel

    Private _radius As Integer = 14
    Private ReadOnly _border As Color = Color.FromArgb(170, 185, 178)
    Private ReadOnly _divider As Color = Color.FromArgb(214, 227, 223)

    Public Property CornerRadius As Integer
        Get
            Return _radius
        End Get
        Set(value As Integer)
            _radius = value
            ApplyRegion()
        End Set
    End Property

    Public Sub New()
        Me.DoubleBuffered = True
        Me.SetStyle(ControlStyles.OptimizedDoubleBuffer Or
                    ControlStyles.AllPaintingInWmPaint Or
                    ControlStyles.UserPaint Or
                    ControlStyles.ResizeRedraw, True)
        Me.BackColor = Color.White
    End Sub

    Protected Overrides Sub OnResize(eventargs As EventArgs)
        MyBase.OnResize(eventargs)
        ApplyRegion()
    End Sub

    Private Sub ApplyRegion()
        If Width <= 0 OrElse Height <= 0 Then Return

        Dim old = Me.Region
        If _radius <= 0 Then
            Me.Region = Nothing
        Else
            Dim d As Integer = _radius * 2
            Using gp As New Drawing2D.GraphicsPath()
                gp.AddLine(0, 0, Width, 0)
                gp.AddLine(Width, 0, Width, Height - _radius)
                gp.AddArc(New Rectangle(Width - d, Height - d, d, d), 0, 90)
                gp.AddArc(New Rectangle(0, Height - d, d, d), 90, 90)
                gp.AddLine(0, Height - _radius, 0, 0)
                gp.CloseFigure()
                Me.Region = New Region(gp)
            End Using
        End If
        If old IsNot Nothing Then old.Dispose()
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        MyBase.OnPaint(e)
        Dim g = e.Graphics
        g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias

        ' Divider separating the footer from the rows above it.
        Using p As New Pen(_divider, 1.0F)
            g.DrawLine(p, 0, 0, Width, 0)
        End Using

        ' The footer is inset inside the card, so the card's hairline shows to
        ' either side already; just round off the bottom corners to follow it.
        Using p As New Pen(_border, 1.0F)
            Dim d As Integer = _radius * 2
            Dim bottom As Integer = Height - 1
            Dim rx As Integer = Width - 1
            g.DrawArc(p, New Rectangle(0, bottom - d, d, d), 90, 90)
            g.DrawArc(p, New Rectangle(rx - d, bottom - d, d, d), 0, 90)
            g.DrawLine(p, _radius, bottom, rx - _radius, bottom)
        End Using
    End Sub
End Class
