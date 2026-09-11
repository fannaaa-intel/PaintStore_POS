Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Printing
Imports System.Windows.Forms

Public Module ReceiptPrinter

    ' =========================================================================
    '  LINE STYLES
    ' =========================================================================
    Public Enum LineStyle
        Normal      ' Left label (gray) + right value (ink)
        Bold        ' Both columns in bold ink — sub-totals, categories
        Muted       ' Centered italic note — counts, remarks
        Title       ' Consumed by the header block; not drawn inline
        Section     ' Uppercase mint category heading (NEW)
        Divider     ' Dashed horizontal rule
        Spacer      ' Vertical gap
    End Enum

    Public Class RLine
        Public Property Left As String
        Public Property Right As String
        Public Property Style As LineStyle
        Public Sub New(Optional left As String = "",
                       Optional right As String = "",
                       Optional style As LineStyle = LineStyle.Normal)
            Me.Left = left
            Me.Right = right
            Me.Style = style
        End Sub
    End Class

    ' Convenience constructors
    Public Function Divider() As RLine
        Return New RLine("", "", LineStyle.Divider)
    End Function
    Public Function Spacer() As RLine
        Return New RLine("", "", LineStyle.Spacer)
    End Function
    Public Function Section(label As String) As RLine
        Return New RLine(label, "", LineStyle.Section)
    End Function

    ' =========================================================================
    '  COLOUR PALETTE
    ' =========================================================================
    Private ReadOnly MintDark As Color = Theme.Dark  ' #0A4F42
    Private ReadOnly MintPrimary As Color = Theme.Primary  ' #00A88E
    Private ReadOnly MintStripe As Color = Theme.Primary  ' dash stripe
    Private ReadOnly BgColor As Color = Color.FromArgb(237, 241, 240)   ' scroll area
    Private ReadOnly CardBg As Color = Color.White
    Private ReadOnly BorderClr As Color = Color.FromArgb(208, 220, 216)
    Private ReadOnly SubText As Color = Theme.SubText
    Private ReadOnly ReceiptInk As Color = Color.FromArgb(28, 32, 30)
    Private ReadOnly ReceiptGray As Color = Color.FromArgb(115, 128, 123)
    Private ReadOnly ReceiptDiv As Color = Color.FromArgb(200, 218, 212)
    Private ReadOnly MetaBg As Color = Color.FromArgb(247, 250, 249)   ' meta row bg
    Private ReadOnly SectionInk As Color = Theme.Dark  ' section head

    ' =========================================================================
    '  CONSTANTS
    ' =========================================================================
    Private Const RECEIPT_W As Integer = 280   ' narrower = crisper on 80mm thermal
    Private Const PAD_X As Integer = 18
    Private Const HEADER_H As Integer = 88    ' taller for logo mark + store name
    Private Const DLG_RADIUS As Integer = 12   ' shared corner radius for the dialog shell

    ' On-screen preview only — the bitmap is still rendered at RECEIPT_W (the
    ' real 80mm-thermal-accurate width) and PrintPage() below is completely
    ' untouched, so actual print output is unaffected. This just makes the
    ' receipt easier to read on screen by drawing the finished bitmap larger,
    ' the same way a browser zooms a rendered image without re-flowing it.
    Private Const PREVIEW_SCALE As Single = 1.4F

    ' =========================================================================
    '  Local rounded-rect path helper for the bottom-bar buttons.
    ' =========================================================================
    ' Previously btnPrint/btnClose used the external FillRoundedRectangle/
    ' DrawRoundedRectangle extension (defined elsewhere, in Dashboard\
    ' GraphicsExtensions.vb) to draw their rounded fill. That produced a
    ' faint pale horizontal sliver right along the top edge of both buttons
    ' — most likely that extension method builds its path or strokes its
    ' edge in a way that doesn't blend cleanly with the erase-to-background
    ' fill used here. Rather than guess at code we don't have visibility
    ' into, both buttons now build and fill their own GraphicsPath directly
    ' with this local helper — the same proven, artifact-free pattern
    ' already used for AppModal's buttons and for topBar/bottomBar above.
    Private Function ButtonRoundedPath(rect As Rectangle, radius As Integer) As GraphicsPath
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
    '  PREVIEW WINDOW
    ' =========================================================================
    Public Sub Preview(documentName As String,
                       lines As List(Of RLine),
                       Optional receiptNo As String = "",
                  Optional storeName As String = "PAINT STORE",
                       Optional storeTagline As String = "Official Sales Receipt")

        If lines Is Nothing OrElse lines.Count = 0 Then Return

        Dim contentW As Integer = RECEIPT_W - PAD_X * 2

        ' ── Pre-measure receipt height ────────────────────────────────────────
        Dim totalH As Integer = HEADER_H + 22 + 18  ' header + meta + top-pad
        Using bmp As New Bitmap(1, 1)
            Using g As Graphics = Graphics.FromImage(bmp)
                For Each ln In lines
                    totalH += MeasureLineH(g, ln, contentW)
                Next
            End Using
        End Using
        totalH += MeasureFooterH() + 20  ' barcode footer + bottom pad
        Dim receiptH As Integer = Math.Max(420, totalH)

        ' ── Render receipt bitmap ─────────────────────────────────────────────
        ' Rendered directly at PREVIEW_SCALE resolution (via ScaleTransform)
        ' rather than drawn small and stretched later — this keeps text and
        ' lines genuinely crisp instead of a blurred upscale. All the drawing
        ' code in DrawReceiptToBitmap still thinks in the original RECEIPT_W/
        ' receiptH coordinate space; the transform handles the rest.
        Dim bmpReceipt As New Bitmap(CInt(RECEIPT_W * PREVIEW_SCALE), CInt(receiptH * PREVIEW_SCALE))
        DrawReceiptToBitmap(bmpReceipt, lines, receiptNo, storeName, storeTagline, receiptH, PREVIEW_SCALE)

        ' =========================================================================
        '  DIALOG FORM
        ' =========================================================================
        Dim DIALOG_W As Integer = 520
        Dim DIALOG_H As Integer = 820

        ' A defined frame colour so the container reads clearly against a
        ' white background (e.g. the transaction builder) instead of blending in.
        Dim DialogBorder As Color = Color.FromArgb(120, 150, 142)

        Dim f As New Form With {
            .Text = "",
            .FormBorderStyle = FormBorderStyle.None,
            .StartPosition = FormStartPosition.CenterParent,
            .BackColor = DialogBorder,
            .Padding = New Padding(1),
            .Width = DIALOG_W,
            .Height = DIALOG_H,
            .Font = New Font("Segoe UI", 9.5F)
        }

        ' Rounded form region (r=12)
        Using gp As New GraphicsPath()
            Dim d = DLG_RADIUS * 2
            gp.AddArc(0, 0, d, d, 180, 90)
            gp.AddArc(f.Width - d, 0, d, d, 270, 90)
            gp.AddArc(f.Width - d, f.Height - d, d, d, 0, 90)
            gp.AddArc(0, f.Height - d, d, d, 90, 90)
            gp.CloseFigure()
            f.Region = New System.Drawing.Region(gp)
        End Using

        ' ── Top bar (52 px) ───────────────────────────────────────────────────
        ' Same corner-clip issue as bottomBar: a docked Panel paints a plain
        ' square rectangle regardless of the Form's rounded Region. It mostly
        ' "worked" before because MintDark is opaque and the eye doesn't catch
        ' a square-vs-round seam between two solid fills of different shapes
        ' at this size — but it's the same underlying bug. Fixed the same way:
        ' erase to the outer frame color, then fill the true rounded-top shape.
        Dim topBar As New Panel With {.Dock = DockStyle.Top, .Height = 52, .BackColor = MintDark}
        AddHandler topBar.Paint,
            Sub(s, e)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias

                Dim r As Integer = DLG_RADIUS
                Dim d As Integer = r * 2
                Dim w As Integer = topBar.Width
                Dim h As Integer = topBar.Height

                ' 1) Erase the panel's square corners into the outer frame color.
                Using bgBrush As New SolidBrush(DialogBorder)
                    g.FillRectangle(bgBrush, 0, 0, w, h)
                End Using

                ' 2) Fill the panel's true shape: rounded top, square bottom.
                Using path As New GraphicsPath()
                    path.AddArc(0, 0, d, d, 180, 90)                        ' top-left corner
                    path.AddArc(w - d, 0, d, d, 270, 90)                    ' top-right corner
                    path.AddLine(w, r, w, h)                                ' right edge
                    path.AddLine(w, h, 0, h)                                ' bottom edge
                    path.CloseFigure()
                    Using br As New SolidBrush(MintDark)
                        g.FillPath(br, path)
                    End Using
                End Using

                ' Decorative dash stripe at very bottom of bar
                Using pen As New Pen(MintStripe, 2.0F) With {.DashStyle = DashStyle.Dash}
                    pen.DashPattern = {4.0F, 3.0F}
                    g.DrawLine(pen, 0, topBar.Height - 1, topBar.Width, topBar.Height - 1)
                End Using
            End Sub

        Dim lblDocName As New Label With {
            .Text = documentName,
            .Font = New Font("Segoe UI Semibold", 12),
            .ForeColor = Color.White,
            .BackColor = Color.Transparent,
            .AutoSize = True,
            .Location = New Point(16, 8)}
        Dim lblHint As New Label With {
            .Text = "Review your receipt before printing",
            .Font = New Font("Segoe UI", 8.5F),
            .ForeColor = Color.FromArgb(160, 215, 200),
            .BackColor = Color.Transparent,
            .AutoSize = True,
            .Location = New Point(16, 30)}

        ' Circular close button
        Dim btnX As New Button With {
            .Text = "×",
            .Size = New Size(28, 28),
            .FlatStyle = FlatStyle.Flat,
            .Font = New Font("Segoe UI", 14),
            .BackColor = MintDark,
            .Cursor = Cursors.Hand}
        btnX.FlatAppearance.BorderSize = 0
        ' Suppress the default grey flat-hover rectangle — we paint our own circle.
        btnX.FlatAppearance.MouseOverBackColor = MintDark
        btnX.FlatAppearance.MouseDownBackColor = MintDark
        Dim xHover As Boolean = False
        AddHandler btnX.Paint,
            Sub(s, e)
                Dim b = CType(s, Button)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim rc = b.ClientRectangle : rc.Inflate(-1, -1)

                ' Circle: subtle when idle, bright white when hovered.
                Dim fillCol = If(xHover, Color.FromArgb(245, 255, 255, 255),
                                          Color.FromArgb(48, 255, 255, 255))
                Using br As New SolidBrush(fillCol)
                    g.FillEllipse(br, rc)
                End Using
                Dim ringCol = If(xHover, Color.FromArgb(255, 255, 255, 255),
                                          Color.FromArgb(110, 255, 255, 255))
                Using pen As New Pen(ringCol, 1.0F)
                    g.DrawEllipse(pen, rc)
                End Using

                ' Glyph: white when idle, dark-green when the circle lights up.
                ' Drawn as two crossing lines so it sits exactly centred (the
                ' '×' character's font metrics leave it looking slightly off).
                Dim glyphCol = If(xHover, MintDark, Color.FromArgb(235, 250, 245))
                Dim gcx As Single = rc.Left + rc.Width / 2.0F
                Dim gcy As Single = rc.Top + rc.Height / 2.0F
                Dim arm As Single = rc.Width * 0.22F
                Using gpen As New Pen(glyphCol, 2.0F) With {
                        .StartCap = LineCap.Round, .EndCap = LineCap.Round}
                    g.DrawLine(gpen, gcx - arm, gcy - arm, gcx + arm, gcy + arm)
                    g.DrawLine(gpen, gcx - arm, gcy + arm, gcx + arm, gcy - arm)
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

        ' Drag by top bar
        Dim _drag As Boolean = False
        Dim _dragPt As Point
        AddHandler topBar.MouseDown,
      Sub(s, e)
          _drag = True
          _dragPt = e.Location
      End Sub
        AddHandler topBar.MouseMove,
            Sub(s, e)
                If _drag Then f.Location = New Point(f.Left + e.X - _dragPt.X, f.Top + e.Y - _dragPt.Y)
            End Sub
        AddHandler topBar.MouseUp, Sub(s, e) _drag = False

        ' ── Bottom action bar ─────────────────────────────────────────────────
        ' NOTE on rounded-corner fix: a docked Panel always paints a perfectly
        ' square rectangle — it has no idea the Form above it is clipped to a
        ' rounded Region. That's why the bottom corners of the dialog looked
        ' sharp/broken: this panel's white square corners sat exactly on top
        ' of where the curve should have been, with no contrasting color to
        ' reveal the clip underneath.
        '
        ' Fix: explicitly carve this panel's own two BOTTOM corners using an
        ' anti-aliased path, the same way every other rounded surface in this
        ' codebase is built (AppModal, SaleCompleteModal) — paint the full
        ' background in the outer frame color (DialogBorder) first, so the
        ' "removed" corner pixels blend into the 1px frame around the dialog,
        ' then fill the panel's real (rounded-at-bottom) shape on top. Only
        ' the bottom corners are rounded here since this panel sits flush
        ' against the Form's bottom edge; the top edge stays a plain straight
        ' line because the scroll area above it is a plain rectangle too.
        Dim bottomBar As New Panel With {
            .Dock = DockStyle.Bottom,
            .Height = 60,
            .BackColor = CardBg}
        AddHandler bottomBar.Paint,
            Sub(s, e)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias

                Dim r As Integer = DLG_RADIUS
                Dim d As Integer = r * 2
                Dim w As Integer = bottomBar.Width
                Dim h As Integer = bottomBar.Height

                ' 1) Erase the panel's square corners into the outer frame color.
                Using bgBrush As New SolidBrush(DialogBorder)
                    g.FillRectangle(bgBrush, 0, 0, w, h)
                End Using

                ' 2) Fill the panel's true shape: square top, rounded bottom.
                Using path As New GraphicsPath()
                    path.AddLine(0, 0, w, 0)                                ' top edge
                    path.AddLine(w, 0, w, h - r)                            ' right edge
                    path.AddArc(w - d, h - d, d, d, 0, 90)                  ' bottom-right corner
                    path.AddLine(w - r, h, r, h)                            ' bottom edge
                    path.AddArc(0, h - d, d, d, 90, 90)                     ' bottom-left corner
                    path.CloseFigure()
                    Using br As New SolidBrush(CardBg)
                        g.FillPath(br, path)
                    End Using
                End Using


            End Sub

        ' Print button (solid mint pill)
        Dim btnPrint As New Button With {
            .Text = "🖨  PRINT",
            .Font = New Font("Segoe UI Semibold", 10),
            .FlatStyle = FlatStyle.Flat,
            .BackColor = MintPrimary,
            .ForeColor = Color.White,
            .Cursor = Cursors.Hand,
            .Size = New Size(120, 38)}
        btnPrint.FlatAppearance.BorderSize = 0
        btnPrint.FlatAppearance.BorderColor = CardBg
        btnPrint.FlatAppearance.MouseOverBackColor = MintDark
        btnPrint.FlatAppearance.MouseDownBackColor = MintDark
        AddHandler btnPrint.Paint,
            Sub(s, e)
                Dim b = CType(s, Button)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                ' Erase the button's default square fill into the bottom bar's
                ' own background first — Button.OnPaint always fills a flat
                ' square before this handler runs, so without this the corner
                ' pixels are still that square fill showing through underneath
                ' the rounded shape, not a true curve.
                ' REPLACE with:
                Dim eraseRect = b.ClientRectangle
                eraseRect.Inflate(1, 1)
                Using bgBrush As New SolidBrush(CardBg)
                    g.FillRectangle(bgBrush, eraseRect)
                End Using
                Dim rc = b.ClientRectangle : rc.Inflate(-1, -1)
                Using path As GraphicsPath = ButtonRoundedPath(rc, 8)
                    Using br As New SolidBrush(b.BackColor)
                        g.FillPath(br, path)
                    End Using
                End Using
                TextRenderer.DrawText(g, b.Text, b.Font, b.ClientRectangle, Color.White,
                    TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub
        AddHandler btnPrint.MouseEnter,
         Sub()
             btnPrint.BackColor = MintDark
             btnPrint.Invalidate()
         End Sub
        AddHandler btnPrint.MouseLeave,
            Sub()
                btnPrint.BackColor = MintPrimary
                btnPrint.Invalidate()
            End Sub

        ' Close button (subtle ghost)
        Dim btnClose As New Button With {
            .Text = "CLOSE",
            .Font = New Font("Segoe UI Semibold", 10),
            .FlatStyle = FlatStyle.Flat,
            .BackColor = Color.FromArgb(238, 243, 241),
            .ForeColor = SubText,
            .Cursor = Cursors.Hand,
            .Size = New Size(90, 38)}
        btnClose.FlatAppearance.BorderSize = 1
        btnClose.FlatAppearance.BorderColor = BorderClr
        AddHandler btnClose.Paint,
            Sub(s, e)
                Dim b = CType(s, Button)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias

                Dim eraseRect = b.ClientRectangle
                eraseRect.Inflate(1, 1)
                Using bgBrush As New SolidBrush(CardBg)
                    g.FillRectangle(bgBrush, eraseRect)
                End Using
                Dim rc = b.ClientRectangle : rc.Inflate(-1, -1)
                Using path As GraphicsPath = ButtonRoundedPath(rc, 8)
                    Using br As New SolidBrush(b.BackColor)
                        g.FillPath(br, path)
                    End Using

                End Using
                TextRenderer.DrawText(g, b.Text, b.Font, b.ClientRectangle, b.ForeColor,
                    TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub
        AddHandler btnClose.MouseEnter,
     Sub()
         btnClose.BackColor = Color.FromArgb(220, 232, 228)
         btnClose.Invalidate()
     End Sub
        AddHandler btnClose.MouseLeave,
            Sub()
                btnClose.BackColor = Color.FromArgb(238, 243, 241)
                btnClose.Invalidate()
            End Sub
        AddHandler btnClose.Click, Sub() f.Close()

        AddHandler bottomBar.Resize,
            Sub()
                Dim cy = (bottomBar.Height - btnPrint.Height) \ 2
                ' Right margin bumped from 16 to 24px: at 16px the button's
                ' right edge (x=384 in a 400px dialog) sat only 8px from
                ' where bottomBar's own rounded bottom-right corner starts
                ' arcing (x=376) — close enough that the two separate curves
                ' visually read as one broken/double outline wrapping the
                ' button. 24px clears it with real breathing room.
                btnPrint.Location = New Point(bottomBar.Width - btnPrint.Width - 24, cy)
                btnClose.Location = New Point(btnPrint.Left - btnClose.Width - 8, cy)
            End Sub
        bottomBar.Controls.AddRange({btnPrint, btnClose})

        ' ── Scroll area ───────────────────────────────────────────────────────
        Dim scroll As New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = BgColor,
            .AutoScroll = True}

        ' Receipt card with layered shadow.
        ' dispW/dispH are bmpReceipt's actual pixel size (already rendered
        ' natively at PREVIEW_SCALE in Preview() above) — used here purely
        ' for layout: sizing the card, shadow, and border around it.
        Dim dispW As Integer = CInt(RECEIPT_W * PREVIEW_SCALE)
        Dim dispH As Integer = CInt(receiptH * PREVIEW_SCALE)
        Dim cardW As Integer = dispW + 14
        Dim cardH As Integer = dispH + 14
        Dim receiptCard As New Panel With {
            .Width = cardW,
            .Height = cardH,
            .BackColor = Color.Transparent}

        AddHandler receiptCard.Paint,
            Sub(s, e)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim ox = 6, oy = 6

                ' Layered drop shadow — sized to the scaled-up card.
                For i As Integer = 7 To 1 Step -1
                    Dim a = CInt(6 + (7 - i) * 8)
                    Using pen As New Pen(Color.FromArgb(Math.Min(55, a), 10, 60, 50), 1.0F)
                        g.DrawRectangle(pen,
                            ox - i, oy - i + 3,
                            dispW + i * 2 - 1,
                            dispH + i * 2 - 1)
                    End Using
                Next

                ' White card
                g.FillRectangle(Brushes.White, ox, oy, dispW, dispH)

                ' Receipt image — bmpReceipt was rendered natively at
                ' PREVIEW_SCALE resolution (see Preview()), so it's drawn
                ' here 1:1, no further stretching needed.
                g.DrawImage(bmpReceipt, ox, oy)

                ' 0.5pt border
                Using pen As New Pen(BorderClr, 0.5F)
                    g.DrawRectangle(pen, ox, oy, dispW - 1, dispH - 1)
                End Using
            End Sub

        AddHandler scroll.Resize,
            Sub()
                Dim availW = scroll.ClientSize.Width
                Dim availH = scroll.ClientSize.Height
                Dim cx = Math.Max(16, (availW - receiptCard.Width) \ 2)
                ' Centre vertically when the receipt fits; otherwise keep a top
                ' margin (≥34) so it never collides with the green header and can
                ' still scroll when the receipt is tall.
                Dim cy = Math.Max(34, (availH - receiptCard.Height) \ 2)
                receiptCard.Location = New Point(cx, cy)
            End Sub
        scroll.Controls.Add(receiptCard)

        ' ── Print action ──────────────────────────────────────────────────────
        AddHandler btnPrint.Click,
            Sub()
                Dim ni As Integer = 0
                ' A fresh PrintDocument per click, so it must be disposed per
                ' click too -- it owns unmanaged printer/spooler resources and
                ' reprinting repeatedly leaked one every time.
                Using doc As New PrintDocument()
                    doc.DocumentName = documentName
                    doc.DefaultPageSettings.PaperSize = New PaperSize("Receipt", RECEIPT_W, 1100)
                    doc.DefaultPageSettings.Margins = New Margins(12, 12, 12, 12)
                    doc.OriginAtMargins = True
                    AddHandler doc.PrintPage,
                        Sub(ps, pe)
                            PrintPage(pe, lines, ni, storeName, storeTagline, receiptNo)
                        End Sub
                    Try
                        Using pd As New PrintDialog With {.Document = doc}
                            If pd.ShowDialog() = DialogResult.OK Then doc.Print()
                        End Using
                    Catch ex As Exception
                        AppModal.Show("Print failed:" & Environment.NewLine & ex.Message,
                            "Print Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
                    End Try
                End Using
            End Sub

        f.Controls.AddRange({topBar, bottomBar, scroll})
        f.ShowDialog()
        bmpReceipt.Dispose()
    End Sub

    ' =========================================================================
    '  DRAW RECEIPT BITMAP
    ' =========================================================================
    Private Sub DrawReceiptToBitmap(bmp As Bitmap,
                                     lines As List(Of RLine),
                                     receiptNo As String,
                                     storeName As String,
                                     storeTagline As String,
                                     receiptH As Integer,
                                     Optional scale As Single = 1.0F)
        Using g As Graphics = Graphics.FromImage(bmp)
            g.Clear(Color.White)
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit
            g.SmoothingMode = SmoothingMode.AntiAlias

            ' Everything below this point still thinks in the original
            ' RECEIPT_W / receiptH coordinate space (same numbers as before).
            ' This transform stretches that coordinate space to match the
            ' larger bitmap, so text/lines/the barcode are drawn natively at
            ' the higher resolution instead of being scaled as a finished
            ' image — the result is crisp at any PREVIEW_SCALE, not blurry.
            If scale <> 1.0F Then g.ScaleTransform(scale, scale)

            Dim contentW As Integer = RECEIPT_W - PAD_X * 2

            ' ── Fonts ──────────────────────────────────────────────────────────
            Using fNormal As New Font("Consolas", 8.5F),
                  fBold As New Font("Consolas", 8.5F, FontStyle.Bold),
                  fMuted As New Font("Consolas", 8.0F, FontStyle.Italic),
                  fSection As New Font("Segoe UI", 7.5F, FontStyle.Bold),
                  fTitle As New Font("Segoe UI Semibold", 13.5F),
                  fSub As New Font("Segoe UI", 8.0F),
                  fMeta As New Font("Segoe UI", 7.5F)

                Dim blk As New SolidBrush(ReceiptInk)
                Dim gry As New SolidBrush(ReceiptGray)
                Dim mnt As New SolidBrush(SectionInk)
                Dim wht As New SolidBrush(Color.White)
                Dim halfW As New SolidBrush(Color.FromArgb(180, 220, 210))
                Dim cfmt As New StringFormat With {.Alignment = StringAlignment.Center}
                Dim lfmt As New StringFormat With {.Alignment = StringAlignment.Near,
                                                    .Trimming = StringTrimming.EllipsisCharacter}
                Dim rfmt As New StringFormat With {.Alignment = StringAlignment.Far}

                ' ── Header block ───────────────────────────────────────────────
                Using hBr As New SolidBrush(MintDark)
                    g.FillRectangle(hBr, 0, 0, bmp.Width, HEADER_H)
                End Using

                ' Logo circle
                Dim cR = 18
                Dim cX = RECEIPT_W \ 2
                Dim cY = 18
                Dim circleCx = cX
                Dim circleCy = cY + cR          ' true centre of the circle
                Using cBr As New SolidBrush(Color.FromArgb(30, 255, 255, 255))
                    g.FillEllipse(cBr, cX - cR, cY, cR * 2, cR * 2)
                End Using
                Using cPen As New Pen(Color.FromArgb(80, 255, 255, 255), 1.0F)
                    g.DrawEllipse(cPen, cX - cR, cY, cR * 2, cR * 2)
                End Using
                ' Logo: paint.png recoloured white, centred in the circle.
                ' Falls back to the drawn drop-icon if the file can't be located.
                Dim logoImg As Image = LoadLogoImage()
                If logoImg IsNot Nothing Then
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic
                    DrawLogoWhite(g, logoImg, circleCx, circleCy, 24)
                    logoImg.Dispose()
                Else
                    DrawDropIcon(g, circleCx, circleCy, 7, Color.FromArgb(200, 255, 255, 255))
                End If

                ' Store name
                Using fStore As New Font("Segoe UI Semibold", 11.5F)
                    Dim storeRect As New RectangleF(0, cY + cR * 2 + 6, RECEIPT_W, 22)
                    g.DrawString(storeName, fStore, wht, storeRect, cfmt)
                End Using

                ' Tagline
                Dim tagRect As New RectangleF(0, cY + cR * 2 + 30, RECEIPT_W, 16)
                g.DrawString(storeTagline, fSub, halfW, tagRect, cfmt)

                ' Decorative stripe at header bottom
                Using pen As New Pen(MintStripe, 2.0F) With {.DashStyle = DashStyle.Dash}
                    pen.DashPattern = {4.0F, 3.0F}
                    g.DrawLine(pen, 0, HEADER_H - 1, bmp.Width, HEADER_H - 1)
                End Using

                ' ── Meta row (date / receipt no) ───────────────────────────────
                Dim metaH = 20
                Using metaBr As New SolidBrush(MetaBg)
                    g.FillRectangle(metaBr, 0, HEADER_H, bmp.Width, metaH)
                End Using
                Using pen As New Pen(ReceiptDiv, 0.5F) With {.DashStyle = DashStyle.Dash}
                    g.DrawLine(pen, 0, HEADER_H + metaH, bmp.Width, HEADER_H + metaH)
                End Using
                Dim dateStr = "Date: " & DateTime.Now.ToString("MMM dd, yyyy")
                Dim recStr = If(String.IsNullOrEmpty(receiptNo), "", "Receipt #" & receiptNo)
                Dim metaRect As New RectangleF(PAD_X, HEADER_H + 3, contentW, 14)
                g.DrawString(dateStr, fMeta, gry, metaRect, lfmt)
                g.DrawString(recStr, fMeta, gry, metaRect, rfmt)

                ' ── Body lines ─────────────────────────────────────────────────
                Dim y As Single = HEADER_H + metaH + 10

                For Each ln In lines
                    If ln.Style = LineStyle.Title Then Continue For  ' header block handles it

                    Dim lh As Single

                    Select Case ln.Style

                        Case LineStyle.Spacer
                            y += fNormal.GetHeight(g) * 0.45F

                        Case LineStyle.Divider
                            lh = fNormal.GetHeight(g)
                            Using pen As New Pen(ReceiptDiv, 0.75F) With {.DashStyle = DashStyle.Dash}
                                pen.DashPattern = {3.0F, 3.0F}
                                g.DrawLine(pen, PAD_X, y + lh / 2, PAD_X + contentW, y + lh / 2)
                            End Using
                            y += lh

                        Case LineStyle.Section
                            ' Uppercase mint label, slight extra top spacing
                            y += 4
                            lh = fSection.GetHeight(g) + 2
                            Using br As New SolidBrush(SectionInk)
                                Dim txt = ln.Left.ToUpper()
                                g.DrawString(txt, fSection, br,
                                    New RectangleF(PAD_X, y, contentW, lh), lfmt)
                            End Using
                            y += lh
                            ' Hairline underline in mint
                            Using pen As New Pen(Color.FromArgb(80, MintPrimary), 0.5F)
                                g.DrawLine(pen, PAD_X, y, PAD_X + contentW, y)
                            End Using
                            y += 3

                        Case LineStyle.Bold
                            lh = fBold.GetHeight(g) + 3
                            If String.IsNullOrEmpty(ln.Right) Then
                                g.DrawString(ln.Left, fBold, blk,
                                    New RectangleF(PAD_X, y, contentW, lh), lfmt)
                            Else
                                Dim rw = g.MeasureString(ln.Right, fBold).Width + 2
                                g.DrawString(ln.Left, fBold, blk,
                                    New RectangleF(PAD_X, y, contentW - rw, lh), lfmt)
                                g.DrawString(ln.Right, fBold, blk,
                                    New RectangleF(PAD_X, y, contentW, lh), rfmt)
                            End If
                            y += lh

                        Case LineStyle.Muted
                            lh = fMuted.GetHeight(g) + 2
                            g.DrawString(ln.Left, fMuted, gry,
                                New RectangleF(PAD_X, y, contentW, lh), cfmt)
                            y += lh

                        Case Else   ' Normal — left in gray, right in ink
                            lh = fNormal.GetHeight(g) + 2
                            If String.IsNullOrEmpty(ln.Right) Then
                                Dim sz = g.MeasureString(ln.Left, fNormal, contentW)
                                Dim h = Math.Max(lh, sz.Height)
                                g.DrawString(ln.Left, fNormal, gry,
                                    New RectangleF(PAD_X, y, contentW, h), lfmt)
                                y += h
                            Else
                                Dim rw = g.MeasureString(ln.Right, fNormal).Width + 2
                                g.DrawString(ln.Left, fNormal, gry,
                                    New RectangleF(PAD_X, y, contentW - rw, lh), lfmt)
                                g.DrawString(ln.Right, fNormal, blk,
                                    New RectangleF(PAD_X, y, contentW, lh), rfmt)
                                y += lh
                            End If

                    End Select
                Next

                ' ── Footer ─────────────────────────────────────────────────────
                Dim footerY As Integer = receiptH - MeasureFooterH()

                ' Top divider of footer
                Using pen As New Pen(ReceiptDiv, 0.5F) With {.DashStyle = DashStyle.Dash}
                    pen.DashPattern = {3.0F, 3.0F}
                    g.DrawLine(pen, PAD_X, footerY, PAD_X + contentW, footerY)
                End Using
                footerY += 10

                ' Barcode
                DrawBarcode(g, receiptNo, PAD_X, footerY, contentW)
                footerY += 32

                ' Serial number
                Dim serial = BuildSerial(receiptNo)
                Using fSerial As New Font("Consolas", 7.0F)
                    g.DrawString(serial, fSerial, gry,
                        New RectangleF(0, footerY, bmp.Width, 14), cfmt)
                End Using
                footerY += 14

                ' Thank-you note
                Dim thankRect As New RectangleF(0, footerY + 4, bmp.Width, 30)
                Using fThank As New Font("Segoe UI", 8.0F)
                    Using tBr As New SolidBrush(Color.FromArgb(148, 160, 155))
                        g.DrawString("Thank you for your purchase!", fThank, tBr, thankRect, cfmt)
                    End Using
                End Using
            End Using
        End Using
    End Sub

    ' =========================================================================
    '  PRINT PAGE HANDLER
    ' =========================================================================
    Private Sub PrintPage(pe As PrintPageEventArgs,
                          lines As List(Of RLine),
                          ByRef ni As Integer,
                          storeName As String,
                          storeTagline As String,
                          receiptNo As String)

        Using fN As New Font("Consolas", 8.0F),
              fB As New Font("Consolas", 8.0F, FontStyle.Bold),
              fT As New Font("Segoe UI Semibold", 11.0F),
              fM As New Font("Consolas", 7.5F, FontStyle.Italic),
              fSc As New Font("Segoe UI", 7.0F, FontStyle.Bold),
              fMt As New Font("Segoe UI", 7.0F)

            Dim pg = pe.Graphics
            Dim lx = CSng(pe.MarginBounds.Left)
            Dim w = CSng(pe.MarginBounds.Width)
            Dim y = CSng(pe.MarginBounds.Top)
            Dim bot = CSng(pe.MarginBounds.Bottom)
            Dim blk As New SolidBrush(Color.Black)
            Dim gry As New SolidBrush(Color.FromArgb(110, 110, 110))
            Dim mnt As New SolidBrush(Color.FromArgb(10, 79, 66))
            Dim cf As New StringFormat With {.Alignment = StringAlignment.Center}
            Dim lf As New StringFormat With {.Alignment = StringAlignment.Near,
                                              .Trimming = StringTrimming.EllipsisCharacter}
            Dim rf As New StringFormat With {.Alignment = StringAlignment.Far}

            ' Print header on first page only
            If ni = 0 Then
                ' Store name
                Dim sh As Single = fT.GetHeight(pg) + 4
                pg.DrawString(storeName, fT, blk, New RectangleF(lx, y, w, sh), cf)
                y += sh
                ' Tagline
                Dim sth = fMt.GetHeight(pg) + 2
                pg.DrawString(storeTagline, fMt, gry, New RectangleF(lx, y, w, sth), cf)
                y += sth
                ' Meta (date + receipt no)
                Dim metaH = fMt.GetHeight(pg) + 2
                Dim dateStr = "Date: " & DateTime.Now.ToString("MMM dd, yyyy")
                Dim recStr = If(String.IsNullOrEmpty(receiptNo), "", "#" & receiptNo)
                pg.DrawString(dateStr, fMt, gry, New RectangleF(lx, y, w, metaH), lf)
                pg.DrawString(recStr, fMt, gry, New RectangleF(lx, y, w, metaH), rf)
                y += metaH
                ' Header divider
                Using pen As New Pen(Color.FromArgb(180, 180, 180), 0.5F)
                    pg.DrawLine(pen, lx, y + 2, lx + w, y + 2)
                End Using
                y += 8
            End If

            While ni < lines.Count
                Dim ln = lines(ni)
                Dim lh As Single

                Select Case ln.Style
                    Case LineStyle.Spacer
                        y += fN.GetHeight(pg) * 0.45F

                    Case LineStyle.Divider
                        lh = fN.GetHeight(pg)
                        If y + lh > bot Then Exit While
                        Using pen As New Pen(Color.FromArgb(180, 180, 180), 0.5F) With {.DashStyle = DashStyle.Dash}
                            pg.DrawLine(pen, lx, y + lh / 2, lx + w, y + lh / 2)
                        End Using
                        y += lh

                    Case LineStyle.Section
                        lh = fSc.GetHeight(pg) + 4
                        If y + lh > bot Then Exit While
                        pg.DrawString(ln.Left.ToUpper(), fSc, mnt,
                            New RectangleF(lx, y, w, lh), lf)
                        Using pen As New Pen(Color.FromArgb(60, 0, 168, 142), 0.5F)
                            pg.DrawLine(pen, lx, y + lh, lx + w, y + lh)
                        End Using
                        y += lh + 2

                    Case LineStyle.Title
                        ' Printed inline if not at top (fallback)
                        lh = fT.GetHeight(pg) + 4
                        If y + lh > bot Then Exit While
                        pg.DrawString(ln.Left, fT, blk, New RectangleF(lx, y, w, lh), cf)
                        y += lh

                    Case LineStyle.Bold
                        Dim fnt = fB
                        lh = fnt.GetHeight(pg) + 1
                        If y + lh > bot Then Exit While
                        If String.IsNullOrEmpty(ln.Right) Then
                            Dim h = Math.Max(lh, pg.MeasureString(ln.Left, fnt, CInt(w)).Height)
                            pg.DrawString(ln.Left, fnt, blk, New RectangleF(lx, y, w, h), lf)
                            y += h
                        Else
                            Dim rw = pg.MeasureString(ln.Right, fnt).Width + 2
                            pg.DrawString(ln.Left, fnt, blk, New RectangleF(lx, y, w - rw, lh), lf)
                            pg.DrawString(ln.Right, fnt, blk, New RectangleF(lx, y, w, lh), rf)
                            y += lh
                        End If

                    Case LineStyle.Muted
                        lh = fM.GetHeight(pg) + 1
                        If y + lh > bot Then Exit While
                        pg.DrawString(ln.Left, fM, gry, New RectangleF(lx, y, w, lh), cf)
                        y += lh

                    Case Else   ' Normal
                        lh = fN.GetHeight(pg) + 1
                        If y + lh > bot Then Exit While
                        If String.IsNullOrEmpty(ln.Right) Then
                            Dim h = Math.Max(lh, pg.MeasureString(ln.Left, fN, CInt(w)).Height)
                            pg.DrawString(ln.Left, fN, gry, New RectangleF(lx, y, w, h), lf)
                            y += h
                        Else
                            Dim rw = pg.MeasureString(ln.Right, fN).Width + 2
                            pg.DrawString(ln.Left, fN, gry, New RectangleF(lx, y, w - rw, lh), lf)
                            pg.DrawString(ln.Right, fN, blk, New RectangleF(lx, y, w, lh), rf)
                            y += lh
                        End If
                End Select

                ni += 1
            End While

            pe.HasMorePages = (ni < lines.Count)
        End Using
    End Sub

    ' =========================================================================
    '  MEASURE HELPERS
    ' =========================================================================
    Private Function MeasureLineH(g As Graphics, ln As RLine, contentW As Integer) As Integer
        If ln.Style = LineStyle.Title Then Return 0
        Using fN As New Font("Consolas", 8.5F),
              fB As New Font("Consolas", 8.5F, FontStyle.Bold),
              fM As New Font("Consolas", 8.0F, FontStyle.Italic),
              fSc As New Font("Segoe UI", 7.5F, FontStyle.Bold)
            Select Case ln.Style
                Case LineStyle.Spacer : Return CInt(fN.GetHeight(g) * 0.45F)
                Case LineStyle.Divider : Return CInt(fN.GetHeight(g))
                Case LineStyle.Section : Return CInt(fSc.GetHeight(g) + 2 + 3 + 4) ' label + hairline gap + top pad
                Case LineStyle.Bold : Return CInt(fB.GetHeight(g) + 3)
                Case LineStyle.Muted : Return CInt(fM.GetHeight(g) + 2)
                Case Else
                    If String.IsNullOrEmpty(ln.Right) Then
                        Dim sz = g.MeasureString(ln.Left, fN, contentW)
                        Return CInt(Math.Max(fN.GetHeight(g), sz.Height) + 2)
                    End If
                    Return CInt(fN.GetHeight(g) + 2)
            End Select
        End Using
    End Function

    Private Function MeasureFooterH() As Integer
        Return 10 + 10 + 32 + 14 + 20  ' divider gap + pad + barcode + serial + thanks
    End Function

    ' =========================================================================
    '  BARCODE HELPER
    ' =========================================================================
    ''' <summary>
    ''' Draws a Code39-style barcode pseudo-graphic derived from the receipt number.
    ''' Not a standards-compliant barcode — purely decorative / scannable aesthetic.
    ''' </summary>
    Private Sub DrawBarcode(g As Graphics, receiptNo As String,
                             x As Integer, y As Integer, totalW As Integer)
        ' Build bar widths from character codes (2 widths: narrow=2, wide=5)
        Dim src = If(String.IsNullOrEmpty(receiptNo), "000000", receiptNo)
        Dim bars As New List(Of Integer)
        For Each c In src
            Dim v = Asc(c)
            For b As Integer = 0 To 6
                bars.Add(If((v >> b) And 1, 5, 2))
            Next
        Next
        ' Pad / trim to fill totalW
        Dim totalBars As Integer = bars.Sum()
        Dim scale As Single = CSng(totalW) / Math.Max(totalBars, 1)

        Dim cx As Single = x
        For i As Integer = 0 To bars.Count - 1
            Dim bw = Math.Max(1, CInt(bars(i) * scale))
            If i Mod 2 = 0 Then  ' even = black bar
                g.FillRectangle(Brushes.Black, cx, y, bw - 1, 26)
            End If
            cx += bw
            If cx >= x + totalW Then Exit For
        Next
    End Sub

    Private Function BuildSerial(receiptNo As String) As String
        Dim d = DateTime.Now
        Return If(String.IsNullOrEmpty(receiptNo), "000000", receiptNo) &
               "-" & d.ToString("yyyy") &
               "-" & d.ToString("MMdd") &
               "-POS"
    End Function

    ' =========================================================================
    '  LOGO HELPERS (paint.png for header circle)
    ' =========================================================================
    ' Looks for paint.png next to the executable and a few parent folders up
    ' (covers running from bin\Debug while the file sits at the project root).
    ' Loaded via a stream so the file is never locked. Returns Nothing if absent.
    Private Function LoadLogoImage() As Image
        Try
            Dim curDir As String = Application.StartupPath
            For i As Integer = 0 To 6
                If String.IsNullOrEmpty(curDir) Then Exit For
                Dim p As String = System.IO.Path.Combine(curDir, "paint.png")
                If System.IO.File.Exists(p) Then
                    Using fs As New System.IO.FileStream(p, IO.FileMode.Open, IO.FileAccess.Read)
                        Return New Bitmap(Image.FromStream(fs))
                    End Using
                End If
                curDir = System.IO.Path.GetDirectoryName(curDir)
            Next
        Catch
            ' Ignore — caller falls back to the drawn drop-icon.
        End Try
        Return Nothing
    End Function

    ' Draws the logo recoloured to white (paint.png is black line-art), scaled
    ' into a box of the given size and centred on (cx, cy).
    '
    ' IMPORTANT: this centers on the image's actual VISIBLE (non-transparent)
    ' content, not its full canvas. If paint.png has asymmetric transparent
    ' padding around the artwork — e.g. more empty space on one side than
    ' the other — centering on the raw canvas bounds reproduces that
    ' asymmetry on screen (the icon looks shifted inside its circle even
    ' though the destination box itself is correctly centered). Measuring
    ' the real ink bounding box first and centering on THAT fixes it
    ' regardless of how the source PNG happens to be cropped/padded.
    Private Sub DrawLogoWhite(g As Graphics, img As Image, cx As Integer, cy As Integer, boxSize As Integer)
        Dim srcRect As Rectangle = GetOpaqueBounds(img)

        ' Fit srcRect into boxSize preserving its aspect ratio (letterboxed,
        ' not stretched) — otherwise a non-square ink bounding box would
        ' still get squeezed into a square destination and look distorted.
        Dim srcAspect As Single = srcRect.Width / CSng(Math.Max(1, srcRect.Height))
        Dim fitW As Integer = boxSize
        Dim fitH As Integer = boxSize
        If srcAspect > 1.0F Then
            fitH = CInt(boxSize / srcAspect)
        ElseIf srcAspect < 1.0F Then
            fitW = CInt(boxSize * srcAspect)
        End If

        Dim dest As New Rectangle(cx - fitW \ 2, cy - fitH \ 2, fitW, fitH)
        Dim cm As New System.Drawing.Imaging.ColorMatrix(New Single()() {
            New Single() {0, 0, 0, 0, 0},
            New Single() {0, 0, 0, 0, 0},
            New Single() {0, 0, 0, 0, 0},
            New Single() {0, 0, 0, 1, 0},
            New Single() {1, 1, 1, 0, 1}})
        Using ia As New System.Drawing.Imaging.ImageAttributes()
            ia.SetColorMatrix(cm)
            g.DrawImage(img, dest, srcRect.X, srcRect.Y, srcRect.Width, srcRect.Height,
                        GraphicsUnit.Pixel, ia)
        End Using
    End Sub

    ' Scans a Bitmap's alpha channel to find the tight bounding box of its
    ' actual visible (non-transparent) pixels. Falls back to the full image
    ' bounds for non-Bitmap images (e.g. some Image.FromStream results) or
    ' if anything goes wrong reading pixel data.
    Private Function GetOpaqueBounds(img As Image) As Rectangle
        Try
            Dim bmp = TryCast(img, Bitmap)
            If bmp Is Nothing Then Return New Rectangle(0, 0, img.Width, img.Height)

            ' GetPixel in a double loop is slow on large bitmaps. This only
            ' needs an approximate bounding box for centering a small icon,
            ' so scan a capped-size working copy and scale the result back
            ' up — keeps this fast even if paint.png turns out to be large.
            Const MAX_SCAN As Integer = 200
            Dim scanBmp As Bitmap = bmp
            Dim scale As Single = 1.0F
            Dim disposeScan As Boolean = False
            If bmp.Width > MAX_SCAN OrElse bmp.Height > MAX_SCAN Then
                scale = Math.Max(bmp.Width, bmp.Height) / CSng(MAX_SCAN)
                Dim sw As Integer = Math.Max(1, CInt(bmp.Width / scale))
                Dim sh As Integer = Math.Max(1, CInt(bmp.Height / scale))
                scanBmp = New Bitmap(bmp, sw, sh)
                disposeScan = True
            End If

            Dim minX As Integer = scanBmp.Width
            Dim minY As Integer = scanBmp.Height
            Dim maxX As Integer = -1
            Dim maxY As Integer = -1

            For y As Integer = 0 To scanBmp.Height - 1
                For x As Integer = 0 To scanBmp.Width - 1
                    If scanBmp.GetPixel(x, y).A > 10 Then
                        If x < minX Then minX = x
                        If x > maxX Then maxX = x
                        If y < minY Then minY = y
                        If y > maxY Then maxY = y
                    End If
                Next
            Next

            If disposeScan Then scanBmp.Dispose()

            If maxX < minX OrElse maxY < minY Then
                ' Fully transparent image — nothing to bound, use full canvas.
                Return New Rectangle(0, 0, img.Width, img.Height)
            End If

            Return New Rectangle(
                CInt(minX * scale), CInt(minY * scale),
                CInt((maxX - minX + 1) * scale), CInt((maxY - minY + 1) * scale))
        Catch
            Return New Rectangle(0, 0, img.Width, img.Height)
        End Try
    End Function

    ' =========================================================================
    '  DROP-ICON HELPER (paint drop for header circle — fallback)
    ' =========================================================================
    Private Sub DrawDropIcon(g As Graphics, cx As Integer, cy As Integer,
                              r As Integer, col As Color)
        Using br As New SolidBrush(col)
            ' Body: small ellipse
            g.FillEllipse(br, cx - r, cy - r, r * 2, r * 2)
            ' Tip: triangle above
            Dim tip() As PointF = {
                New PointF(cx, cy - r - r),
                New PointF(cx - r * 0.6F, cy - r + 2),
                New PointF(cx + r * 0.6F, cy - r + 2)
            }
            g.FillPolygon(br, tip)
        End Using
    End Sub



End Module