Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Printing
Imports System.Drawing.Text
Imports System.Windows.Forms

' =============================================================================
'  ReceiptPrinter  —  thermal-style slips: sales receipt, return slip, cash
'  count, goods receipt.
'
'  ONE RENDERER, TWO OUTPUTS
'  The preview used to be drawn by one routine (green header card, Segoe UI,
'  a decorative barcode) and the printout by another (plain text), so the
'  cashier never saw what would actually come out of the printer. Render()
'  below now lays out and paints the slip for BOTH: the preview is that exact
'  drawing on a bitmap, the printout is the same drawing on the printer.
'
'  WHAT IT LOOKS LIKE
'  An 80mm thermal receipt: black on white only (thermal heads cannot print
'  colour or grey cleanly), monospace columns, the store name in large bold
'  capitals, dashed and double rules, a big TOTAL line, and a real Code 128
'  barcode of the receipt number that a scanner can read straight into the
'  Returns screen's receipt lookup.
' =============================================================================
Public Module ReceiptPrinter

    ' =========================================================================
    '  LINE STYLES  (values only appended to, never reordered)
    ' =========================================================================
    Public Enum LineStyle
        Normal          ' label left, value right (or wrapped text)
        Bold            ' as Normal, bold — item names, sub-totals
        Muted           ' small centred note — header details, footers
        Title           ' store name: large, bold, centred
        Section         ' bold capital heading
        Divider         ' ---------- rule
        Spacer          ' half-line gap
        Total           ' large bold amount line between double rules
        Center          ' centred body text
        DoubleDivider   ' ========== rule
        Barcode         ' Code 128 barcode of .Right (or .Left) + its text
    End Enum

    Public Class RLine
        Public Property Left As String
        Public Property Right As String
        Public Property Style As LineStyle
        Public Sub New(Optional left As String = "",
                       Optional right As String = "",
                       Optional style As LineStyle = LineStyle.Normal)
            Me.Left = If(left, "")
            Me.Right = If(right, "")
            Me.Style = style
        End Sub
    End Class

    Public Function Divider() As RLine
        Return New RLine("", "", LineStyle.Divider)
    End Function
    Public Function DoubleDivider() As RLine
        Return New RLine("", "", LineStyle.DoubleDivider)
    End Function
    Public Function Spacer() As RLine
        Return New RLine("", "", LineStyle.Spacer)
    End Function
    Public Function Section(label As String) As RLine
        Return New RLine(label, "", LineStyle.Section)
    End Function
    Public Function Center(text As String) As RLine
        Return New RLine(text, "", LineStyle.Center)
    End Function
    Public Function Total(label As String, value As String) As RLine
        Return New RLine(label, value, LineStyle.Total)
    End Function
    Public Function Barcode(data As String) As RLine
        Return New RLine("", data, LineStyle.Barcode)
    End Function

    ' =========================================================================
    '  GEOMETRY  (layout units: preview pixels at 1x = printer 1/100 inch)
    ' =========================================================================
    Private Const RECEIPT_W As Integer = 280      ' 2.8in, the printable width of an 80mm roll
    Private Const PAD_X As Integer = 12
    Private Const PAD_Y As Integer = 14
    Private Const BARCODE_H As Single = 34
    Private Const PREVIEW_SCALE As Single = 1.4F
    Private Const DLG_RADIUS As Integer = 12

    Private ReadOnly InkColor As Color = Color.Black
    Private ReadOnly MintDark As Color = Theme.Dark
    Private ReadOnly MintPrimary As Color = Theme.Primary
    Private ReadOnly DeskBg As Color = Color.FromArgb(226, 232, 230)
    Private ReadOnly BorderClr As Color = Color.FromArgb(208, 220, 216)
    Private ReadOnly SubText As Color = Theme.SubText

    ' Placeholder names callers pass when they have no store name of their own.
    Private Function ResolveStoreName(name As String) As String
        If String.IsNullOrWhiteSpace(name) OrElse
           String.Equals(name, "PAINT STORE", StringComparison.OrdinalIgnoreCase) Then
            Return StoreSettings.StoreName
        End If
        Return name
    End Function

    ' Everything one slip needs, passed to the renderer as a unit.
    Private Class SlipDoc
        Public Lines As List(Of RLine)
        Public ReceiptNo As String
        Public StoreName As String
        Public Tagline As String
        Public IsInternal As Boolean
    End Class

    ' =========================================================================
    '  PREVIEW WINDOW
    ' =========================================================================
    ''' <param name="internal">
    ''' True for an in-house document (a cash count) rather than a customer
    ''' receipt: signature lines instead of a barcode.
    ''' </param>
    Public Sub Preview(documentName As String,
                       lines As List(Of RLine),
                       Optional receiptNo As String = "",
                       Optional storeName As String = "",
                       Optional storeTagline As String = "",
                       Optional [internal] As Boolean = False)

        If lines Is Nothing OrElse lines.Count = 0 Then Return

        Dim doc As New SlipDoc With {
            .Lines = lines, .ReceiptNo = If(receiptNo, ""),
            .StoreName = ResolveStoreName(storeName),
            .Tagline = If(String.IsNullOrWhiteSpace(storeTagline), documentName, storeTagline),
            .IsInternal = [internal]}

        ' ── Render the slip once at preview resolution ───────────────────────
        Dim slipH As Single
        Using probe As New Bitmap(1, 1)
            Using g As Graphics = Graphics.FromImage(probe)
                slipH = Render(g, doc, PAD_X, RECEIPT_W - PAD_X * 2, False, PREVIEW_SCALE)
            End Using
        End Using
        Dim bmpW As Integer = CInt(RECEIPT_W * PREVIEW_SCALE)
        Dim bmpH As Integer = CInt(Math.Ceiling(slipH * PREVIEW_SCALE))
        Dim bmpSlip As New Bitmap(bmpW, bmpH)
        Using g As Graphics = Graphics.FromImage(bmpSlip)
            g.Clear(Color.White)
            g.ScaleTransform(PREVIEW_SCALE, PREVIEW_SCALE)
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit
            Render(g, doc, PAD_X, RECEIPT_W - PAD_X * 2, True, PREVIEW_SCALE)
        End Using

        ' ── Dialog shell ──────────────────────────────────────────────────────
        ' Fixed at 820px it ran past the bottom of a 1366x768 laptop screen,
        ' taking the Print button with it. It now fits the working area.
        Dim DIALOG_W As Integer = 520
        Dim workH As Integer = Screen.PrimaryScreen.WorkingArea.Height
        Dim DIALOG_H As Integer = Math.Max(420, Math.Min(820, workH - 40))
        Dim DialogBorder As Color = Color.FromArgb(120, 150, 142)

        Dim f As New Form With {
            .Text = documentName,
            .FormBorderStyle = FormBorderStyle.None,
            .StartPosition = FormStartPosition.CenterParent,
            .BackColor = DialogBorder,
            .Padding = New Padding(1),
            .Width = DIALOG_W,
            .Height = DIALOG_H,
            .Font = New Font("Segoe UI", 9.5F),
            .KeyPreview = True,
            .ShowInTaskbar = False
        }
        Using gp As GraphicsPath = RoundedPath(New Rectangle(0, 0, f.Width, f.Height), DLG_RADIUS)
            f.Region = New Region(gp)
        End Using
        AddHandler f.KeyDown, Sub(s, e) If e.KeyCode = Keys.Escape Then f.Close()

        ' ── Top bar ───────────────────────────────────────────────────────────
        Dim topBar As New Panel With {.Dock = DockStyle.Top, .Height = 52, .BackColor = MintDark}
        AddHandler topBar.Paint,
            Sub(s, e)
                Dim g = e.Graphics
                Using bg As New SolidBrush(DialogBorder)
                    g.FillRectangle(bg, 0, 0, topBar.Width, topBar.Height)
                End Using
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim r As Integer = DLG_RADIUS, d As Integer = DLG_RADIUS * 2
                Using path As New GraphicsPath()
                    path.AddArc(0, 0, d, d, 180, 90)
                    path.AddArc(topBar.Width - d, 0, d, d, 270, 90)
                    path.AddLine(topBar.Width, r, topBar.Width, topBar.Height)
                    path.AddLine(topBar.Width, topBar.Height, 0, topBar.Height)
                    path.CloseFigure()
                    Using br As New SolidBrush(MintDark)
                        g.FillPath(br, path)
                    End Using
                End Using
            End Sub
        Dim lblDocName As New Label With {
            .Text = documentName, .Font = New Font("Segoe UI Semibold", 12),
            .ForeColor = Color.White, .BackColor = Color.Transparent, .AutoSize = True,
            .Location = New Point(16, 7), .UseMnemonic = False}
        Dim lblHint As New Label With {
            .Text = "This is exactly how it will print",
            .Font = New Font("Segoe UI", 8.5F), .ForeColor = Color.FromArgb(160, 215, 200),
            .BackColor = Color.Transparent, .AutoSize = True, .Location = New Point(16, 29)}
        Dim btnX As New Button With {
            .Size = New Size(28, 28), .FlatStyle = FlatStyle.Flat,
            .BackColor = MintDark, .Cursor = Cursors.Hand, .TabStop = False}
        btnX.FlatAppearance.BorderSize = 0
        btnX.FlatAppearance.MouseOverBackColor = MintDark
        btnX.FlatAppearance.MouseDownBackColor = MintDark
        Dim xHover As Boolean = False
        AddHandler btnX.Paint,
            Sub(s, e)
                Dim g = e.Graphics
                Using bg As New SolidBrush(MintDark)
                    g.FillRectangle(bg, btnX.ClientRectangle)
                End Using
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim rc = btnX.ClientRectangle : rc.Inflate(-1, -1)
                Using br As New SolidBrush(If(xHover, Color.White, Color.FromArgb(48, 255, 255, 255)))
                    g.FillEllipse(br, rc)
                End Using
                Dim cx As Single = rc.Left + rc.Width / 2.0F, cy As Single = rc.Top + rc.Height / 2.0F
                Dim arm As Single = rc.Width * 0.22F
                Using pen As New Pen(If(xHover, MintDark, Color.White), 2.0F) With {
                        .StartCap = LineCap.Round, .EndCap = LineCap.Round}
                    g.DrawLine(pen, cx - arm, cy - arm, cx + arm, cy + arm)
                    g.DrawLine(pen, cx - arm, cy + arm, cx + arm, cy - arm)
                End Using
            End Sub
        AddHandler btnX.MouseEnter,
            Sub()
                xHover = True
                btnX.Invalidate()
            End Sub
        AddHandler btnX.MouseLeave,
            Sub()
                xHover = False
                btnX.Invalidate()
            End Sub
        AddHandler btnX.Click, Sub() f.Close()
        AddHandler topBar.Resize, Sub() btnX.Location = New Point(topBar.Width - 42, 12)
        topBar.Controls.AddRange({lblDocName, lblHint, btnX})

        Dim dragging As Boolean = False
        Dim dragAt As Point
        AddHandler topBar.MouseDown,
            Sub(s, e)
                dragging = True
                dragAt = e.Location
            End Sub
        AddHandler topBar.MouseMove,
            Sub(s, e)
                If dragging Then f.Location = New Point(f.Left + e.X - dragAt.X, f.Top + e.Y - dragAt.Y)
            End Sub
        AddHandler topBar.MouseUp, Sub(s, e) dragging = False

        ' ── Bottom bar ────────────────────────────────────────────────────────
        Dim bottomBar As New Panel With {.Dock = DockStyle.Bottom, .Height = 60, .BackColor = Color.White}
        AddHandler bottomBar.Paint,
            Sub(s, e)
                Dim g = e.Graphics
                Using bg As New SolidBrush(DialogBorder)
                    g.FillRectangle(bg, 0, 0, bottomBar.Width, bottomBar.Height)
                End Using
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim w = bottomBar.Width, h = bottomBar.Height
                Dim r As Integer = DLG_RADIUS, d As Integer = DLG_RADIUS * 2
                Using path As New GraphicsPath()
                    path.AddLine(0, 0, w, 0)
                    path.AddLine(w, 0, w, h - r)
                    path.AddArc(w - d, h - d, d, d, 0, 90)
                    path.AddLine(w - r, h, r, h)
                    path.AddArc(0, h - d, d, d, 90, 90)
                    path.CloseFigure()
                    Using br As New SolidBrush(Color.White)
                        g.FillPath(br, path)
                    End Using
                End Using
                Using pen As New Pen(BorderClr)
                    g.DrawLine(pen, 0, 0, w, 0)
                End Using
            End Sub

        Dim btnPrint As Button = MakeBarButton("Print", 130, MintPrimary, MintDark, Color.White)
        Dim btnClose As Button = MakeBarButton("Close", 96, Color.FromArgb(238, 243, 241),
                                               Color.FromArgb(220, 232, 228), SubText)
        AddHandler btnClose.Click, Sub() f.Close()
        AddHandler bottomBar.Resize,
            Sub()
                Dim cy As Integer = (bottomBar.Height - btnPrint.Height) \ 2
                btnPrint.Location = New Point(bottomBar.Width - btnPrint.Width - 24, cy)
                btnClose.Location = New Point(btnPrint.Left - btnClose.Width - 8, cy)
            End Sub
        bottomBar.Controls.AddRange({btnPrint, btnClose})

        ' ── Desk + paper ──────────────────────────────────────────────────────
        Dim desk As New Panel With {.Dock = DockStyle.Fill, .BackColor = DeskBg, .AutoScroll = True}
        Dim paper As New Panel With {
            .Name = "receiptPaper",
            .Width = bmpW + 20, .Height = bmpH + 24, .BackColor = Color.Transparent}
        AddHandler paper.Paint,
            Sub(s, e)
                Dim g = e.Graphics
                Const ox As Integer = 10, oy As Integer = 8
                ' soft drop shadow
                For i As Integer = 6 To 1 Step -1
                    Using br As New SolidBrush(Color.FromArgb(10, 20, 40, 34))
                        g.FillRectangle(br, ox - i + 2, oy - i + 5, bmpW + i * 2 - 4, bmpH + i * 2 - 4)
                    End Using
                Next
                ' 1:1 with no resampling: a sized DrawImage interpolates even at
                ' the same size, which smeared every bar edge by half a pixel
                ' and softened the text.
                Dim oldInterp = g.InterpolationMode, oldOffset = g.PixelOffsetMode
                g.InterpolationMode = InterpolationMode.NearestNeighbor
                g.PixelOffsetMode = PixelOffsetMode.Half
                g.DrawImage(bmpSlip, New Rectangle(ox, oy, bmpW, bmpH),
                            New Rectangle(0, 0, bmpW, bmpH), GraphicsUnit.Pixel)
                g.InterpolationMode = oldInterp
                g.PixelOffsetMode = oldOffset
                ' torn top and bottom edges, as the cutter leaves a thermal roll
                g.SmoothingMode = SmoothingMode.AntiAlias
                Using br As New SolidBrush(DeskBg)
                    Const tooth As Integer = 8
                    For x As Integer = 0 To bmpW Step tooth
                        g.FillPolygon(br, New Point() {
                            New Point(ox + x, oy - 1), New Point(ox + x + tooth \ 2, oy + 4),
                            New Point(ox + x + tooth, oy - 1)})
                        g.FillPolygon(br, New Point() {
                            New Point(ox + x, oy + bmpH + 1), New Point(ox + x + tooth \ 2, oy + bmpH - 4),
                            New Point(ox + x + tooth, oy + bmpH + 1)})
                    Next
                End Using
            End Sub
        AddHandler desk.Resize,
            Sub()
                paper.Location = New Point(Math.Max(8, (desk.ClientSize.Width - paper.Width) \ 2),
                                           Math.Max(16, (desk.ClientSize.Height - paper.Height) \ 2))
            End Sub
        desk.Controls.Add(paper)
        ' The wheel scrolls the paper even while the pointer is over it.
        AddHandler paper.MouseEnter, Sub() desk.Focus()

        ' ── Print ─────────────────────────────────────────────────────────────
        AddHandler btnPrint.Click,
            Sub()
                Try
                    PrintSlip(documentName, doc)
                Catch ex As Exception
                    AppModal.Show("Print failed:" & Environment.NewLine & ErrorLog.ForUser(ex),
                                  "Print Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
                End Try
            End Sub

        f.Controls.AddRange({desk, bottomBar, topBar})
        f.AcceptButton = btnPrint
        f.ShowDialog()
        f.Dispose()
        bmpSlip.Dispose()
    End Sub

    Private Function MakeBarButton(text As String, width As Integer, rest As Color,
                                   hover As Color, fore As Color) As Button
        Dim b As New Button With {
            .Text = text, .Font = New Font("Segoe UI Semibold", 10), .FlatStyle = FlatStyle.Flat,
            .BackColor = rest, .ForeColor = fore, .Cursor = Cursors.Hand, .Size = New Size(width, 38)}
        b.FlatAppearance.BorderSize = 0
        b.FlatAppearance.MouseOverBackColor = hover
        b.FlatAppearance.MouseDownBackColor = hover
        AddHandler b.Paint,
            Sub(s, e)
                Dim g = e.Graphics
                g.FillBackground(Brushes.White, b.ClientRectangle)
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim rc = b.ClientRectangle : rc.Inflate(-1, -1)
                Using path As GraphicsPath = RoundedPath(rc, 8)
                    Using br As New SolidBrush(b.BackColor)
                        g.FillPath(br, path)
                    End Using
                End Using
                TextRenderer.DrawText(g, b.Text, b.Font, b.ClientRectangle, b.ForeColor,
                    TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub
        AddHandler b.MouseEnter,
            Sub()
                b.BackColor = hover
                b.Invalidate()
            End Sub
        AddHandler b.MouseLeave,
            Sub()
                b.BackColor = rest
                b.Invalidate()
            End Sub
        Return b
    End Function

    Private Function RoundedPath(rect As Rectangle, radius As Integer) As GraphicsPath
        Dim path As New GraphicsPath()
        Dim d As Integer = radius * 2
        path.AddArc(rect.X, rect.Y, d, d, 180, 90)
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90)
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90)
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90)
        path.CloseFigure()
        Return path
    End Function

    ' =========================================================================
    '  PRINT
    ' =========================================================================
    Private Sub PrintSlip(documentName As String, doc As SlipDoc)
        Using pdoc As New PrintDocument()
            pdoc.DocumentName = documentName
            pdoc.OriginAtMargins = False
            pdoc.DefaultPageSettings.Margins = New Margins(8, 8, 8, 8)

            ' One continuous slip: the page is as long as the receipt, which a
            ' thermal roll driver cuts after. A driver that ignores custom
            ' sizes still works -- the slip then continues on further pages.
            Dim contentW As Integer = RECEIPT_W - 16
            Dim slipH As Single
            Using probe As New Bitmap(1, 1)
                Using g As Graphics = Graphics.FromImage(probe)
                    slipH = Render(g, doc, 0, contentW, False, 2.0F)
                End Using
            End Using
            ' The probe above is a 96-dpi bitmap; the printer lays out at 100
            ' units per inch, so allow for the slightly taller text.
            pdoc.DefaultPageSettings.PaperSize =
                New PaperSize("Receipt", RECEIPT_W, Math.Max(300, CInt(Math.Ceiling(slipH * 1.06F)) + 40))

            Dim page As Integer = 0
            Dim printedH As Single = -1
            AddHandler pdoc.PrintPage,
                Sub(ps, pe)
                    Dim mb As Rectangle = pe.MarginBounds
                    Dim g As Graphics = pe.Graphics
                    Dim devPerUnit As Single = g.DpiX / 100.0F
                    ' Measured on the printer itself, so paging is exact.
                    If printedH < 0 Then printedH = Render(g, doc, mb.Left, mb.Width, False, devPerUnit, topY:=mb.Top)
                    Dim state = g.Save()
                    g.SetClip(mb)
                    g.TranslateTransform(0, -page * CSng(mb.Height))
                    Render(g, doc, mb.Left, mb.Width, True, devPerUnit, topY:=mb.Top)
                    g.Restore(state)
                    page += 1
                    pe.HasMorePages = page * mb.Height < printedH - PAD_Y
                End Sub

            Using pd As New PrintDialog With {.Document = pdoc, .UseEXDialog = True}
                If pd.ShowDialog() = DialogResult.OK Then pdoc.Print()
            End Using
        End Using
    End Sub

    ' =========================================================================
    '  RENDERER — lays the slip out and (when draw=True) paints it.
    '  Returns the height used. devPerUnit = device pixels per layout unit,
    '  used to snap barcode bars to whole printer dots so they scan.
    ' =========================================================================
    Private Function Render(g As Graphics, doc As SlipDoc, x0 As Single, w As Single,
                            draw As Boolean, devPerUnit As Single,
                            Optional topY As Single = PAD_Y) As Single
        Using fBody As New Font("Consolas", 8.0F),
              fBold As New Font("Consolas", 8.0F, FontStyle.Bold),
              fSmall As New Font("Consolas", 7.0F),
              fStore As New Font("Consolas", 12.0F, FontStyle.Bold),
              fTotal As New Font("Consolas", 11.0F, FontStyle.Bold),
              inkBr As New SolidBrush(InkColor)

            Dim y As Single = topY
            Dim bodyH As Single = fBody.GetHeight(g)

            ' ── small layout helpers ─────────────────────────────────────────
            Dim centred As Func(Of String, Font, Single) =
                Function(text As String, fnt As Font) As Single
                    If String.IsNullOrEmpty(text) Then Return 0
                    Dim sf As New StringFormat With {.Alignment = StringAlignment.Center}
                    Dim h As Single = g.MeasureString(text, fnt, CInt(w), sf).Height
                    If draw Then g.DrawString(text, fnt, inkBr, New RectangleF(x0, y, w, h + 1), sf)
                    Return h
                End Function

            Dim wrapped As Func(Of String, Font, Single) =
                Function(text As String, fnt As Font) As Single
                    Dim h As Single = Math.Max(fnt.GetHeight(g), g.MeasureString(text, fnt, CInt(w)).Height)
                    If draw Then g.DrawString(text, fnt, inkBr, New RectangleF(x0, y, w, h + 1))
                    Return h
                End Function

            ' Label on the left, amount flush right. A label too long for the
            ' space left by the amount wraps instead of running under it.
            Dim pair As Func(Of String, String, Font, Single) =
                Function(left As String, right As String, fnt As Font) As Single
                    Dim rw As Single = g.MeasureString(right, fnt).Width
                    Dim lw As Single = Math.Max(20.0F, w - rw - 6)
                    Dim h As Single = Math.Max(fnt.GetHeight(g), g.MeasureString(left, fnt, CInt(lw)).Height)
                    If draw Then
                        g.DrawString(left, fnt, inkBr, New RectangleF(x0, y, lw, h + 1))
                        Dim rf As New StringFormat With {.Alignment = StringAlignment.Far}
                        g.DrawString(right, fnt, inkBr, New RectangleF(x0, y, w, fnt.GetHeight(g) + 1), rf)
                    End If
                    Return h
                End Function

            ' A rule typed out of characters, like a receipt printer prints it.
            Dim rule As Func(Of Char, Single) =
                Function(ch As Char) As Single
                    If draw Then
                        Dim probe As String = New String(ch, 20)
                        Dim cw As Single = g.MeasureString(probe, fBody, PointF.Empty,
                                                           StringFormat.GenericTypographic).Width / 20.0F
                        Dim n As Integer = Math.Max(4, CInt(Math.Floor(w / Math.Max(1.0F, cw))))
                        Dim sf As New StringFormat(StringFormat.GenericTypographic) With {.Alignment = StringAlignment.Center}
                        g.DrawString(New String(ch, n), fBody, inkBr, New RectangleF(x0 - 2, y, w + 4, bodyH + 1), sf)
                    End If
                    Return bodyH
                End Function

            ' ── auto header for slips that do not carry their own ───────────
            Dim hasTitle As Boolean = doc.Lines.Any(Function(l) l.Style = LineStyle.Title)
            If Not hasTitle Then
                y += DrawLogo(g, x0, y, w, draw)
                y += centred(doc.StoreName.ToUpperInvariant(), fStore) + 2
                y += centred(doc.Tagline.ToUpperInvariant(), fBold)
                y += rule("="c)
                ' Only what the slip's own lines do not already say (the cash
                ' count prints its session number and print time itself).
                Dim saysNo As Boolean = doc.Lines.Any(Function(l) l.Right = doc.ReceiptNo)
                Dim saysWhen As Boolean = doc.Lines.Any(
                    Function(l) l.Left.StartsWith("Printed", StringComparison.OrdinalIgnoreCase) OrElse
                                l.Left.StartsWith("Date", StringComparison.OrdinalIgnoreCase))
                If doc.ReceiptNo <> "" AndAlso Not saysNo Then y += pair("No.", doc.ReceiptNo, fBody)
                If Not saysWhen Then y += pair("Printed", DateTime.Now.ToString("MMM d, yyyy  h:mm tt"), fBody)
                If (doc.ReceiptNo <> "" AndAlso Not saysNo) OrElse Not saysWhen Then y += rule("-"c)
            End If

            ' ── body ─────────────────────────────────────────────────────────
            Dim firstTitle As Boolean = True
            For Each ln In doc.Lines
                Select Case ln.Style
                    Case LineStyle.Title
                        If firstTitle Then y += DrawLogo(g, x0, y, w, draw)
                        firstTitle = False
                        y += centred(ln.Left.ToUpperInvariant(), fStore) + 2
                    Case LineStyle.Muted
                        y += centred(ln.Left, fSmall)
                    Case LineStyle.Center
                        y += centred(ln.Left, fBody)
                    Case LineStyle.Spacer
                        y += bodyH * 0.5F
                    Case LineStyle.Divider
                        y += rule("-"c)
                    Case LineStyle.DoubleDivider
                        y += rule("="c)
                    Case LineStyle.Section
                        y += 2
                        y += wrapped(ln.Left.ToUpperInvariant(), fBold)
                    Case LineStyle.Total
                        y += 2
                        y += pair(ln.Left, ln.Right, fTotal)
                        y += 2
                    Case LineStyle.Barcode
                        Dim data As String = If(ln.Right <> "", ln.Right, ln.Left)
                        y += 4
                        y += DrawCode128(g, data, x0, y, w, devPerUnit, draw)
                        y += 1
                        y += centred(data, fSmall)
                    Case LineStyle.Bold
                        y += If(ln.Right = "", wrapped(ln.Left, fBold), pair(ln.Left, ln.Right, fBold))
                    Case Else
                        y += If(ln.Right = "", wrapped(ln.Left, fBody), pair(ln.Left, ln.Right, fBody))
                End Select
            Next

            ' ── footer ───────────────────────────────────────────────────────
            If doc.IsInternal Then
                ' A cash count is handed from the cashier to whoever verifies
                ' it, so the paper has somewhere for both of them to sign.
                y += rule("-"c)
                For Each who In {"Counted by", "Verified by"}
                    y += bodyH * 1.6F
                    If draw Then
                        Using pen As New Pen(InkColor, 0.75F)
                            g.DrawLine(pen, x0, y, x0 + w, y)
                        End Using
                    End If
                    y += 2
                    y += wrapped(who, fSmall)
                Next
                y += bodyH * 0.5F
                y += centred("Internal document - not a customer receipt", fSmall)
            ElseIf doc.ReceiptNo <> "" AndAlso
                   Not doc.Lines.Any(Function(l) l.Style = LineStyle.Barcode) Then
                y += rule("-"c)
                y += 2
                y += DrawCode128(g, doc.ReceiptNo, x0, y, w, devPerUnit, draw)
                y += 1
                y += centred(doc.ReceiptNo, fSmall)
            End If

            Return (y - topY) + PAD_Y * 2
        End Using
    End Function

    ' The store mark (paint.png, black line art) above the name, as a thermal
    ' printer prints a logo bitmap. Returns the height used (0 if no file).
    Private Function DrawLogo(g As Graphics, x0 As Single, y As Single, w As Single, draw As Boolean) As Single
        Const SIZE As Integer = 30
        Dim img As Image = LoadLogoImage()
        If img Is Nothing Then Return 0
        Try
            If draw Then
                Dim src As Rectangle = GetOpaqueBounds(img)
                Dim aspect As Single = src.Width / CSng(Math.Max(1, src.Height))
                Dim dw As Single = If(aspect >= 1, SIZE, SIZE * aspect)
                Dim dh As Single = If(aspect >= 1, SIZE / aspect, SIZE)
                Dim old = g.InterpolationMode
                g.InterpolationMode = InterpolationMode.HighQualityBicubic
                g.DrawImage(img, New RectangleF(x0 + (w - dw) / 2.0F, y + (SIZE - dh) / 2.0F, dw, dh),
                            src, GraphicsUnit.Pixel)
                g.InterpolationMode = old
            End If
            Return SIZE + 4
        Finally
            img.Dispose()
        End Try
    End Function

    ' =========================================================================
    '  CODE 128 BARCODE
    '
    '  A real symbol (Code B for text, Code C for runs of digits), so a USB
    '  scanner reads the receipt number into the Returns lookup. Bars are
    '  snapped to whole device pixels -- printer dots on paper -- because a
    '  bar drawn 2.4 dots wide comes out as 2 or 3 and the code no longer reads.
    ' =========================================================================
    Private ReadOnly Code128Patterns As String() = {
        "11011001100", "11001101100", "11001100110", "10010011000", "10010001100",
        "10001001100", "10011001000", "10011000100", "10001100100", "11001001000",
        "11001000100", "11000100100", "10110011100", "10011011100", "10011001110",
        "10111001100", "10011101100", "10011100110", "11001110010", "11001011100",
        "11001001110", "11011100100", "11001110100", "11101101110", "11101001100",
        "11100101100", "11100100110", "11101100100", "11100110100", "11100110010",
        "11011011000", "11011000110", "11000110110", "10100011000", "10001011000",
        "10001000110", "10110001000", "10001101000", "10001100010", "11010001000",
        "11000101000", "11000100010", "10110111000", "10110001110", "10001101110",
        "10111011000", "10111000110", "10001110110", "11101110110", "11010001110",
        "11000101110", "11011101000", "11011100010", "11011101110", "11101011000",
        "11101000110", "11100010110", "11101101000", "11101100010", "11100011010",
        "11101111010", "11001000010", "11110001010", "10100110000", "10100001100",
        "10010110000", "10010000110", "10000101100", "10000100110", "10110010000",
        "10110000100", "10011010000", "10011000010", "10000110100", "10000110010",
        "11000010010", "11001010000", "11110111010", "11000010100", "10001111010",
        "10100111100", "10010111100", "10010011110", "10111100100", "10011110100",
        "10011110010", "11110100100", "11110010100", "11110010010", "11011011110",
        "11011110110", "11110110110", "10101111000", "10100011110", "10001011110",
        "10111101000", "10111100010", "11110101000", "11110100010", "10111011110",
        "10111101110", "11101011110", "11110101110", "11010000100", "11010010000",
        "11010011100"}
    Private Const CODE128_STOP As String = "1100011101011"
    Private Const START_B As Integer = 104, START_C As Integer = 105
    Private Const TO_CODE_B As Integer = 100, TO_CODE_C As Integer = 99

    ' Symbol values (start ... data ... checksum), without the stop pattern.
    Public Function Code128Values(data As String) As List(Of Integer)
        Dim s As String = If(data, "")
        Dim vals As New List(Of Integer)
        Dim digitRun = Function(at As Integer) As Integer
                           Dim n As Integer = 0
                           While at + n < s.Length AndAlso Char.IsDigit(s(at + n))
                               n += 1
                           End While
                           Return n
                       End Function
        Dim inC As Boolean = digitRun(0) >= 4 AndAlso digitRun(0) Mod 2 = 0 OrElse
                             (digitRun(0) = s.Length AndAlso s.Length >= 2 AndAlso s.Length Mod 2 = 0)
        vals.Add(If(inC, START_C, START_B))
        Dim i As Integer = 0
        While i < s.Length
            If inC Then
                If i + 1 < s.Length AndAlso Char.IsDigit(s(i)) AndAlso Char.IsDigit(s(i + 1)) Then
                    vals.Add((AscW(s(i)) - 48) * 10 + (AscW(s(i + 1)) - 48))
                    i += 2
                Else
                    vals.Add(TO_CODE_B) : inC = False
                End If
            Else
                Dim run As Integer = digitRun(i)
                If run >= 4 Then
                    ' An odd run sends its first digit in Code B so the rest pairs up.
                    If run Mod 2 = 1 Then
                        vals.Add(AscW(s(i)) - 32) : i += 1
                    End If
                    vals.Add(TO_CODE_C) : inC = True
                Else
                    Dim c As Integer = AscW(s(i))
                    If c < 32 OrElse c > 126 Then c = AscW("?"c)
                    vals.Add(c - 32)
                    i += 1
                End If
            End If
        End While
        Dim sum As Integer = vals(0)
        For k As Integer = 1 To vals.Count - 1
            sum += vals(k) * k
        Next
        vals.Add(sum Mod 103)
        Return vals
    End Function

    ' The full module string (1 = bar, 0 = space) including the stop pattern.
    Public Function Code128Modules(data As String) As String
        Dim sb As New System.Text.StringBuilder()
        For Each v In Code128Values(data)
            sb.Append(Code128Patterns(v))
        Next
        sb.Append(CODE128_STOP)
        Return sb.ToString()
    End Function

    Private Function DrawCode128(g As Graphics, data As String, x0 As Single, y As Single,
                                 w As Single, devPerUnit As Single, draw As Boolean) As Single
        If String.IsNullOrEmpty(data) Then Return 0
        Dim modules As String = Code128Modules(data)
        ' 10-module quiet zone each side; widest whole-pixel module that fits.
        Dim availDev As Single = w * devPerUnit
        Dim modDev As Integer = Math.Max(1, CInt(Math.Floor(availDev / (modules.Length + 20))))
        modDev = Math.Min(modDev, 4)
        Dim modW As Single = modDev / devPerUnit
        Dim totalW As Single = modules.Length * modW
        If draw Then
            Dim startDev As Single = CSng(Math.Round((x0 + (w - totalW) / 2.0F) * devPerUnit))
            Dim xs As Single = startDev / devPerUnit
            Dim oldSmooth = g.SmoothingMode, oldOffset = g.PixelOffsetMode
            g.SmoothingMode = SmoothingMode.None
            g.PixelOffsetMode = PixelOffsetMode.Half
            Dim i As Integer = 0
            While i < modules.Length
                If modules(i) = "1"c Then
                    Dim j As Integer = i
                    While j < modules.Length AndAlso modules(j) = "1"c
                        j += 1
                    End While
                    g.FillRectangle(Brushes.Black, xs + i * modW, y, (j - i) * modW, BARCODE_H)
                    i = j
                Else
                    i += 1
                End If
            End While
            g.SmoothingMode = oldSmooth
            g.PixelOffsetMode = oldOffset
        End If
        Return BARCODE_H
    End Function

    ' =========================================================================
    '  LOGO HELPERS
    ' =========================================================================
    ' paint.png next to the executable or a few folders up. Loaded via a copy
    ' so the file is never locked. Nothing if absent.
    Private Function LoadLogoImage() As Image
        Try
            Dim dir As String = Application.StartupPath
            For i As Integer = 0 To 6
                If String.IsNullOrEmpty(dir) Then Exit For
                Dim p As String = IO.Path.Combine(dir, "paint.png")
                If IO.File.Exists(p) Then
                    Using fs As New IO.FileStream(p, IO.FileMode.Open, IO.FileAccess.Read)
                        Using src As Image = Image.FromStream(fs)
                            Return New Bitmap(src)
                        End Using
                    End Using
                End If
                dir = IO.Path.GetDirectoryName(dir)
            Next
        Catch
        End Try
        Return Nothing
    End Function

    ' Tight bounds of the visible (non-transparent) pixels, so an icon with
    ' uneven transparent padding still centres.
    Private Function GetOpaqueBounds(img As Image) As Rectangle
        Try
            Dim bmp = TryCast(img, Bitmap)
            If bmp Is Nothing Then Return New Rectangle(0, 0, img.Width, img.Height)
            Const MAX_SCAN As Integer = 160
            Dim scale As Single = 1.0F
            Dim scan As Bitmap = bmp
            Dim owned As Boolean = False
            If bmp.Width > MAX_SCAN OrElse bmp.Height > MAX_SCAN Then
                scale = Math.Max(bmp.Width, bmp.Height) / CSng(MAX_SCAN)
                scan = New Bitmap(bmp, Math.Max(1, CInt(bmp.Width / scale)), Math.Max(1, CInt(bmp.Height / scale)))
                owned = True
            End If
            Dim minX = scan.Width, minY = scan.Height, maxX = -1, maxY = -1
            For yy As Integer = 0 To scan.Height - 1
                For xx As Integer = 0 To scan.Width - 1
                    If scan.GetPixel(xx, yy).A > 10 Then
                        minX = Math.Min(minX, xx)
                        maxX = Math.Max(maxX, xx)
                        minY = Math.Min(minY, yy)
                        maxY = Math.Max(maxY, yy)
                    End If
                Next
            Next
            If owned Then scan.Dispose()
            If maxX < minX Then Return New Rectangle(0, 0, img.Width, img.Height)
            Return New Rectangle(CInt(minX * scale), CInt(minY * scale),
                                 CInt((maxX - minX + 1) * scale), CInt((maxY - minY + 1) * scale))
        Catch
            Return New Rectangle(0, 0, img.Width, img.Height)
        End Try
    End Function

End Module
