Imports System.Drawing
Imports System.Drawing.Drawing2D

Imports System.Windows.Forms
Imports MySql.Data.MySqlClient

' =============================================================================
'  ProcurementPOPanelBuilder
'  Screen 2 — Purchase Orders.
'
'  THREE INLINE VIEWS (swapped inside the same container):
'    1. List    — filterable list of POs (All-active / Draft / Ordered /
'                 Received / Cancelled). "New PO" opens the builder.
'    2. New PO  — supplier + dates, a Pending-Requests picker on the left and a
'                 live line-items editor on the right. Save writes
'                 purchase_orders + po_items in ONE transaction and flips any
'                 sourced requests to 'Converted'.
'    3. Detail  — read of one PO's lines, with "Mark as Ordered" (Draft) or
'                 "Receive Goods" (Ordered → navigates to the Receive screen).
'
'  Shared-only, no instance state. Helpers (RoundRect / RoundControl /
'  RoundLabel / MakeLink / SafeInt / HexToColor) copied verbatim from
'  InventoryLowStockPanelBuilder. The save transaction mirrors
'  InventoryStockAdjustmentPanelBuilder.CommitAdjustment().
'
'  NEW PO FORM — UI/UX pass:
'    • Larger, more readable type across the whole form (labels, fields,
'      combos, date pickers, line rows, totals).
'    • Inputs wrapped in rounded, bordered "field" cards with a focus glow.
'    • Date-picker calendars themed to the mint palette.
'    • "Back to list" is a tappable pill; buttons have hover states.
'    • RESTORED: the line-row rendering loop in RebuildLines (was accidentally
'      dropped when the empty-state placeholder was pasted in).
' =============================================================================
Public Class ProcurementPOPanelBuilder

    ' ── PALETTE (exact spec values) ──────────────────────────────────────────────
    Private Shared ReadOnly MintDark As Color = Color.FromArgb(10, 79, 66)
    Private Shared ReadOnly MintPrimary As Color = Color.FromArgb(0, 168, 142)
    Private Shared ReadOnly MintDeep As Color = Color.FromArgb(0, 130, 109)
    Private Shared ReadOnly MintBg As Color = Color.FromArgb(244, 248, 246)
    Private Shared ReadOnly CardBg As Color = Color.White
    Private Shared ReadOnly CardBorder As Color = Color.FromArgb(228, 232, 230)
    Private Shared ReadOnly SubText As Color = Color.FromArgb(90, 114, 107)
    Private Shared ReadOnly FaintText As Color = Color.FromArgb(150, 165, 160)
    Private Shared ReadOnly TileBg As Color = Color.FromArgb(236, 248, 244)
    Private Shared ReadOnly RowDivider As Color = Color.FromArgb(238, 242, 240)
    Private Shared ReadOnly PositiveClr As Color = Color.FromArgb(0, 160, 106)
    Private Shared ReadOnly NegativeClr As Color = Color.FromArgb(210, 60, 60)
    Private Shared ReadOnly WarningClr As Color = Color.FromArgb(232, 120, 30)
    Private Shared ReadOnly GoldValue As Color = Color.FromArgb(255, 196, 120)
    Private Shared ReadOnly TotalDeep As Color = Color.FromArgb(0, 60, 50)
    Private Shared ReadOnly Peso As String = ChrW(8369)

    Private Shared ReadOnly InputBorder As Color = Color.FromArgb(214, 224, 220)
    Private Shared ReadOnly InputFocus As Color = Color.FromArgb(0, 168, 142)
    Private Shared ReadOnly MintHover As Color = Color.FromArgb(0, 150, 127)
    Private Shared ReadOnly DeepHover As Color = Color.FromArgb(0, 112, 94)

    ' ── DATA MODELS ──────────────────────────────────────────────────────────────
    Private Class PORow
        Public PoId As Integer
        Public PoNo As String
        Public SupplierName As String
        Public OrderDate As DateTime
        Public HasExpected As Boolean
        Public ExpectedDate As DateTime
        Public Status As String
        Public ItemCount As Integer
        Public TotalCost As Decimal
    End Class

    Private Class POLine
        Public ProductId As Integer
        Public ProductName As String
        Public Sku As String
        Public Qty As Integer
        Public UnitCost As Decimal
        Public RequestId As Integer
        Public ColorHex As String
        Public ImagePath As String
        Public ReadOnly Property LineTotal As Decimal
            Get
                Return Math.Round(UnitCost * Qty, 2)
            End Get
        End Property
    End Class

    Private Class ProdItem
        Public Id As Integer
        Public Name As String
        Public Sku As String
        Public Price As Decimal
        Public ImagePath As String
        Public ColorHex As String
        Public Overrides Function ToString() As String
            Return Name & "  (" & Sku & ")"
        End Function
    End Class

    Private Class SupItem
        Public Id As Integer
        Public Name As String
        Public Overrides Function ToString() As String
            Return Name
        End Function
    End Class

    Private Class PendingReqLite
        Public RequestId As Integer
        Public ProductId As Integer
        Public ProductName As String
        Public Sku As String
        Public UnitPrice As Decimal
        Public QtyRequested As Integer
        Public RequestedBy As String
        Public ImagePath As String
        Public ColorHex As String
    End Class

    ' ── QUOTE DATA MODEL ─────────────────────────────────────────────────────────
    Private Class QuoteItem
        Public ProductId As Integer
        Public ProductName As String
        Public Sku As String
        Public Qty As Integer
        Public UnitPrice As Decimal
        Public ValidUntil As DateTime
        Public HasValidUntil As Boolean
        Public Notes As String
        Public ReadOnly Property LineTotal As Decimal
            Get
                Return Math.Round(UnitPrice * Qty, 2)
            End Get
        End Property
    End Class

    Private Class ApprovedReqLite
        Public RequestId As Integer
        Public ProductId As Integer
        Public ProductName As String
        Public Sku As String
        Public QtyRequested As Integer
        Public UnitPrice As Decimal
        Public Overrides Function ToString() As String
            Return ProductName & "  (" & Sku & ")  ×" & QtyRequested
        End Function
    End Class

    ' ── STATE ────────────────────────────────────────────────────────────────────
    Private Shared containerRef As Panel
    Private Shared currentFilter As String = "All"   ' All = active (not Cancelled/Received)

    ' New-PO form state
    Private Shared newLines As New List(Of POLine)
    Private Shared linesHostRef As Panel
    Private Shared lblGrandRef As Label
    Private Shared cmbSupplierRef As ComboBox
    Private Shared dtOrderRef As DateTimePicker
    Private Shared dtExpectedRef As DateTimePicker
    Private Shared cmbProductRef As ComboBox
    Private Shared btnSaveRef As Button
    Private Shared isOnNewPOForm As Boolean = False

    ' Quote-within-PO state
    Private Shared quoteItems As New List(Of QuoteItem)
    Private Shared quoteHostPanelRef As Panel  ' the collapsible quote panel inside Settings card
    Private Shared quoteExpanded As Boolean = False

    ' =========================================================================
    '  ENTRY POINTS
    ' =========================================================================
    Public Shared Sub LoadPO(container As Panel)
        containerRef = container
        currentFilter = "All"
        RenderList()
    End Sub

    ' Pre-loads the New PO form with one pending request already added as a line.
    Public Shared Sub LoadPOWithRequest(container As Panel, requestId As Integer)
        containerRef = container
        LoadNewPOForm(requestId)
    End Sub

    ' =========================================================================
    '  VIEW 1 — PO LIST
    ' =========================================================================
    Private Shared Sub RenderList()
        Dim container As Panel = containerRef
        If container Is Nothing Then Return
        isOnNewPOForm = False

        Dim rows As List(Of PORow) = LoadList(currentFilter)

        container.SuspendLayout()
        container.Controls.Clear()
        container.BackColor = MintBg

        ' ---- Scrolling list ----
        Dim listHost As New Panel With {
           .Dock = DockStyle.Fill, .BackColor = MintBg, .Padding = New Padding(24, 0, 24, 20)
        }
        Dim rowsHost As New Panel With {
            .Dock = DockStyle.Fill, .BackColor = Color.Transparent, .AutoScroll = True
        }
        listHost.Controls.Add(rowsHost)

        ' ---- Top region ----
        Dim topRegion As Panel = BuildListTop(rows.Count)
        topRegion.Dock = DockStyle.Top

        container.Controls.Add(listHost)
        container.Controls.Add(topRegion)
        container.ResumeLayout()

        ' ---- Fill rows ----
        If rows.Count = 0 Then
            Dim empty As Panel = BuildEmptyCard("No purchase orders",
                "Nothing matches this filter. Use " & ChrW(8220) & "New PO" & ChrW(8221) & " to create one.")
            empty.Location = New Point(0, 16)
            empty.Width = rowsHost.ClientSize.Width - 4
            empty.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
            rowsHost.Controls.Add(empty)
        Else
            Dim y As Integer = 8
            For Each po In rows
                Dim card As Panel = BuildPOCard(po)
                card.Location = New Point(0, y)
                card.Width = rowsHost.ClientSize.Width - 4
                card.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
                rowsHost.Controls.Add(card)
                y += card.Height + 12
            Next
            Dim onPickerResize As EventHandler = Sub(s, e)
                                                     Dim w As Integer = rowsHost.ClientSize.Width - 4
                                                     For Each c As Control In rowsHost.Controls
                                                         c.Width = w
                                                     Next
                                                 End Sub
            AddHandler rowsHost.Resize, onPickerResize
            AddHandler rowsHost.VisibleChanged,
                Sub()
                    If rowsHost.Visible Then
                        Dim w As Integer = rowsHost.ClientSize.Width - 4
                        For Each c As Control In rowsHost.Controls
                            c.Width = w
                        Next
                    End If
                End Sub
        End If
    End Sub

    Private Shared Function BuildListTop(Optional totalCount As Integer = 0) As Panel
        Dim region As New Panel With {.Height = 140, .BackColor = MintBg, .Padding = New Padding(24, 22, 24, 0)}

        Dim lblTitle As New Label With {
            .Text = "Purchase Orders", .Font = New Font("Segoe UI", 24, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(18, 9)
        }
        Dim subTxt As String = If(currentFilter = "All",
            If(totalCount = 0, "No active orders",
               totalCount & If(totalCount = 1, " active order", " active orders")),
            If(totalCount = 0, "No " & currentFilter.ToLower() & " orders",
               totalCount & " " & currentFilter.ToLower() & If(totalCount = 1, " order", " orders")))
        Dim lblSub As New Label With {
                    .Text = subTxt, .Font = New Font("Segoe UI", 11),
                    .ForeColor = SubText, .AutoSize = True,
                    .Location = New Point(24, 55)
                }
        region.Controls.Add(lblTitle)
        region.Controls.Add(lblSub)

        Dim btnRefresh As New Button With {
            .Text = "↻  Refresh",
            .FlatStyle = FlatStyle.Flat,
            .Cursor = Cursors.Hand,
            .Size = New Size(100, 34),
            .Font = New Font("Segoe UI", 9.5F),
            .BackColor = Color.FromArgb(220, 235, 232),
            .ForeColor = SubText,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        btnRefresh.FlatAppearance.BorderSize = 0
        AddHandler btnRefresh.Paint,
            Sub(s, e)
                Dim bb = CType(s, Button)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect = bb.ClientRectangle : rect.Inflate(-1, -1)
                Using br As New SolidBrush(bb.BackColor)
                    e.Graphics.FillRoundedRectangle(br, rect, 8)
                End Using
                TextRenderer.DrawText(e.Graphics, bb.Text, bb.Font, bb.ClientRectangle, bb.ForeColor,
                    TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub
        AddHandler btnRefresh.MouseEnter,
            Sub()
                btnRefresh.BackColor = Color.FromArgb(210, 240, 235)
                btnRefresh.ForeColor = MintDark
                btnRefresh.Invalidate()
            End Sub
        AddHandler btnRefresh.MouseLeave,
            Sub()
                btnRefresh.BackColor = Color.FromArgb(220, 235, 232)
                btnRefresh.ForeColor = SubText
                btnRefresh.Invalidate()
            End Sub
        AddHandler btnRefresh.Click, Sub() RenderList()
        region.Controls.Add(btnRefresh)

        ' ---- New PO button (top-right) ----
        Dim btnNew As New Button With {
            .Text = "+ New PO", .Size = New Size(130, 40),
            .FlatStyle = FlatStyle.Flat, .BackColor = MintDeep, .ForeColor = Color.White,
            .Font = New Font("Segoe UI Semibold", 10), .Cursor = Cursors.Hand,
            .TextAlign = ContentAlignment.MiddleCenter,
            .Padding = New Padding(0),
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        btnNew.FlatAppearance.BorderSize = 0
        RoundControl(btnNew, 10)
        AddHover(btnNew, MintDeep, DeepHover)
        AddHandler btnNew.Click, Sub() LoadNewPOForm(0)
        region.Controls.Add(btnNew)

        ' Position both buttons: [Refresh] [+New PO] right-aligned with 8px gap
        AddHandler region.Resize,
            Sub()
                btnNew.Top = 22
                btnNew.Left = region.Width - 24 - btnNew.Width
                btnRefresh.Top = 22
                btnRefresh.Left = btnNew.Left - 8 - btnRefresh.Width
            End Sub
        btnNew.Top = 22
        btnNew.Left = 700
        btnRefresh.Top = 22
        btnRefresh.Left = 592

        ' ---- Filter tabs ----
        Dim tabs() As String = {"All", "Draft", "Ordered", "Received", "Cancelled"}
        Dim tabRow As New FlowLayoutPanel With {
           .Left = 24, .Top = 88, .Height = 44, .AutoSize = False, .Width = 700,
            .BackColor = Color.Transparent, .WrapContents = False,
            .Anchor = AnchorStyles.Left Or AnchorStyles.Top
        }
        For Each t In tabs
            tabRow.Controls.Add(BuildFilterTab(t))
        Next
        region.Controls.Add(tabRow)

        Return region
    End Function

    Private Shared Function BuildFilterTab(name As String) As Label
        Dim isActive As Boolean = String.Equals(name, currentFilter, StringComparison.OrdinalIgnoreCase)
        Dim tab As New Label With {
            .Text = If(name = "All", "All Active", name),
            .Font = New Font(If(isActive, "Segoe UI Semibold", "Segoe UI"), 9.5F),
            .ForeColor = If(isActive, Color.White, SubText),
            .BackColor = If(isActive, MintPrimary, Color.FromArgb(220, 235, 232)),
            .AutoSize = False, .Size = New Size(108, 34),
            .TextAlign = ContentAlignment.MiddleCenter, .Cursor = Cursors.Hand,
            .Margin = New Padding(0, 0, 8, 0)
        }
        RoundLabel(tab, 8)

        AddHandler tab.MouseEnter,
            Sub()
                If Not String.Equals(name, currentFilter, StringComparison.OrdinalIgnoreCase) Then
                    tab.BackColor = Color.FromArgb(210, 240, 235)
                    tab.ForeColor = MintDark
                    tab.Invalidate()
                End If
            End Sub
        AddHandler tab.MouseLeave,
            Sub()
                If Not String.Equals(name, currentFilter, StringComparison.OrdinalIgnoreCase) Then
                    tab.BackColor = Color.FromArgb(220, 235, 232)
                    tab.ForeColor = SubText
                    tab.Invalidate()
                End If
            End Sub
        AddHandler tab.Click,
            Sub()
                currentFilter = name
                RenderList()
            End Sub
        Return tab
    End Function

    ' Rounded 40px thumbnail for a PO line:
    '   paint (color_hex) -> swatch · other (image_path) -> photo · else initial
    ' Overload: draw a thumb from raw fields (used by the pending picker).
    Private Shared Sub PaintLineThumb(g As Graphics, box As Rectangle,
                                      colorHex As String, imagePath As String, productName As String)
        g.SmoothingMode = SmoothingMode.AntiAlias
        Using path As GraphicsPath = RoundRect(box, 11)
            ' 1 — paint colour swatch
            If Not String.IsNullOrWhiteSpace(colorHex) Then
                Using b As New SolidBrush(HexToColor(colorHex, MintPrimary))
                    g.FillPath(b, path)
                End Using
                Using pen As New Pen(Color.FromArgb(45, 0, 0, 0), 1.0F)
                    g.DrawPath(pen, path)
                End Using
                Using hi As New SolidBrush(Color.FromArgb(70, 255, 255, 255))
                    g.FillEllipse(hi, box.X + 7, box.Y + 6, 16, 9)
                End Using
                Return
            End If

            ' 2 — product photo, centre-cropped
            If Not String.IsNullOrWhiteSpace(imagePath) Then
                Try
                    Dim full As String = imagePath
                    If Not IO.Path.IsPathRooted(full) Then full = IO.Path.Combine(Application.StartupPath, full)
                    If IO.File.Exists(full) Then
                        Using img As Image = Image.FromFile(full)
                            g.SetClip(path)
                            Dim src As Rectangle = CoverCrop(img.Size, box.Size)
                            g.DrawImage(img, box, src.X, src.Y, src.Width, src.Height, GraphicsUnit.Pixel)
                            g.ResetClip()
                        End Using
                        Using pen As New Pen(Color.FromArgb(30, 0, 0, 0), 1.0F)
                            g.DrawPath(pen, path)
                        End Using
                        Return
                    End If
                Catch
                End Try
            End If

            ' 3 — fallback initial avatar
            Using br As New LinearGradientBrush(box, MintPrimary, MintDeep, 90.0F)
                g.FillPath(br, path)
            End Using
            Dim ini As String = If(String.IsNullOrWhiteSpace(productName), "?",
                                   productName.Trim().Substring(0, 1).ToUpperInvariant())
            Using f As New Font("Segoe UI Semibold", 15, FontStyle.Bold), tb As New SolidBrush(Color.White)
                Dim sz = g.MeasureString(ini, f)
                g.DrawString(ini, f, tb, box.X + (box.Width - sz.Width) / 2.0F, box.Y + (box.Height - sz.Height) / 2.0F)
            End Using
        End Using
    End Sub

    ' Source rect that centre-crops an image to the target's aspect ratio.
    Private Shared Function CoverCrop(srcSize As Size, dstSize As Size) As Rectangle
        If srcSize.Width <= 0 OrElse srcSize.Height <= 0 Then Return New Rectangle(0, 0, 1, 1)
        Dim sa As Double = srcSize.Width / CDbl(srcSize.Height)
        Dim da As Double = dstSize.Width / CDbl(dstSize.Height)
        Dim cw, ch As Integer
        If sa > da Then ch = srcSize.Height : cw = CInt(srcSize.Height * da) Else cw = srcSize.Width : ch = CInt(srcSize.Width / da)
        Return New Rectangle((srcSize.Width - cw) \ 2, (srcSize.Height - ch) \ 2, cw, ch)
    End Function

    Private Shared Function BuildPOCard(po As PORow) As Panel
        Dim card As New BufferedPanel With {.Height = 150, .BackColor = CardBg, .Cursor = Cursors.Hand}
        AddHandler card.Paint,
            Sub(s, e)
                PaintCard(CType(s, Panel), e)
                ' Left accent bar tinted by status
                Dim st2 = StatusInfo(po.Status)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Using br As New SolidBrush(st2.fore)
                    Using path = RoundRect(New Rectangle(0, 18, 6, card.Height - 36), 3)
                        e.Graphics.FillPath(br, path)
                    End Using
                End Using
            End Sub

        ' ════════ LEFT COLUMN — identity + meta ════════
        Dim lblPo As New Label With {
            .Text = po.PoNo, .Font = New Font("Segoe UI", 19, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True,
            .Location = New Point(30, 24), .BackColor = Color.Transparent
        }
        Dim lblSupplier As New Label With {
            .Text = po.SupplierName, .Font = New Font("Segoe UI", 13),
            .ForeColor = SubText, .AutoSize = True,
            .Location = New Point(32, 62), .BackColor = Color.Transparent
        }
        card.Controls.AddRange({lblPo, lblSupplier})

        Dim metaY As Integer = 102
        Dim ordered As (cap As Label, val As Label) = MakeMetaPair(
            "ORDERED", po.OrderDate.ToString("MMM d, yyyy"), 32, metaY)
        Dim expected As (cap As Label, val As Label) = MakeMetaPair(
            "EXPECTED", If(po.HasExpected, po.ExpectedDate.ToString("MMM d, yyyy"), "—"), 230, metaY)
        Dim items As (cap As Label, val As Label) = MakeMetaPair(
            "ITEMS", po.ItemCount & If(po.ItemCount = 1, " item", " items"), 428, metaY)
        card.Controls.AddRange({ordered.cap, ordered.val, expected.cap, expected.val,
                                 items.cap, items.val})



        ' Status badge — top-right of card

        Dim st = StatusInfo(po.Status)
        Dim badgeFore As Color = Color.White
        Dim badgeBack As Color
        Select Case po.Status
            Case "Ordered" : badgeBack = MintDeep
            Case "Draft" : badgeBack = Color.FromArgb(115, 130, 126)
            Case "Partially Received" : badgeBack = WarningClr
            Case "Received" : badgeBack = PositiveClr
            Case "Cancelled" : badgeBack = NegativeClr
            Case Else : badgeBack = SubText
        End Select

        Dim badge As New Label With {
            .Text = st.text,
            .Font = New Font("Segoe UI Semibold", 12.0F, FontStyle.Bold),
            .ForeColor = badgeFore,
            .BackColor = badgeBack,
            .AutoSize = True,
            .Padding = New Padding(16, 8, 16, 8),
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        RoundLabel(badge, 14)
        card.Controls.Add(badge)

        ' "View >" pill — bottom-right, same drawn chevron as Suppliers screen
        Dim vpH As Integer = 44
        Dim vpW As Integer = 110
        Dim viewPill As New BufferedPanel With {
            .Size = New Size(vpW, vpH),
            .BackColor = TileBg,
            .Cursor = Cursors.Hand,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }

        ' Pill-shaped region (fully rounded ends)
        Dim vpRgn As New GraphicsPath()
        vpRgn.AddArc(0, 0, vpH, vpH, 180, 90)
        vpRgn.AddArc(vpW - vpH, 0, vpH, vpH, 270, 90)
        vpRgn.AddArc(vpW - vpH, 0, vpH, vpH, 0, 90)
        vpRgn.AddArc(0, 0, vpH, vpH, 90, 90)
        vpRgn.CloseFigure()
        viewPill.Region = New Region(vpRgn)

        AddHandler viewPill.Paint,
            Sub(s, e)
                Dim b As BufferedPanel = CType(s, BufferedPanel)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                ' Pill fill
                Using path As GraphicsPath = RoundRect(New Rectangle(0, 0, b.Width - 1, b.Height - 1), vpH \ 2)
                    Using br As New SolidBrush(b.BackColor)
                        e.Graphics.FillPath(br, path)
                    End Using
                End Using
                ' "View" text
                Using f As New Font("Segoe UI Semibold", 13.0F, FontStyle.Bold),
                      br As New SolidBrush(MintDeep)
                    Dim sf As New StringFormat With {
                        .Alignment = StringAlignment.Near,
                        .LineAlignment = StringAlignment.Center
                    }
                    e.Graphics.DrawString("View", f, br, New RectangleF(16, 0, 56, b.Height), sf)
                End Using
                ' Thick ">" chevron — rounded pen
                Dim chX As Single = b.Width - 24
                Dim midY As Single = b.Height / 2.0F
                Dim arm As Single = 11
                Using pen As New Pen(MintDeep, 3.0F)
                    pen.LineJoin = LineJoin.Round
                    pen.StartCap = LineCap.Round
                    pen.EndCap = LineCap.Round
                    e.Graphics.DrawLines(pen, New PointF() {
                        New PointF(chX, midY - arm),
                        New PointF(chX + arm * 0.7F, midY),
                        New PointF(chX, midY + arm)
                    })
                End Using
            End Sub

        AddHandler viewPill.MouseEnter,
            Sub()
                viewPill.BackColor = Color.FromArgb(180, 230, 218)
                viewPill.Invalidate()
            End Sub
        AddHandler viewPill.MouseLeave,
            Sub()
                viewPill.BackColor = TileBg
                viewPill.Invalidate()
            End Sub
        card.Controls.Add(viewPill)

        Dim capId As Integer = po.PoId
        AddHandler viewPill.Click, Sub() LoadDetail(capId)
        AddHandler card.Click, Sub() LoadDetail(capId)
        AddHandler lblPo.Click, Sub() LoadDetail(capId)
        AddHandler lblSupplier.Click, Sub() LoadDetail(capId)

        AddHandler card.Resize,
            Sub()
                Const rPad As Integer = 24
                ' Badge — top-right
                badge.Left = card.Width - badge.Width - rPad
                badge.Top = 22
                ' View pill — bottom-right
                viewPill.Left = card.Width - viewPill.Width - rPad
                viewPill.Top = card.Height - viewPill.Height - 20
                ' Limit left-column text width so it never overlaps the right column
                Dim rightStop As Integer = badge.Left - 20
                lblPo.MaximumSize = New Size(Math.Max(80, rightStop - 30), 34)
                lblSupplier.MaximumSize = New Size(Math.Max(80, rightStop - 32), 26)
            End Sub

        Return card
    End Function

    ' Re-tints a (typically dark/black) icon to a solid target colour, preserving alpha.
    Private Shared Function TintImage(src As Image, target As Color) As Image
        Dim bmp As New Bitmap(src.Width, src.Height)
        Using g As Graphics = Graphics.FromImage(bmp)
            Dim cm As New Imaging.ColorMatrix(New Single()() {
                New Single() {0, 0, 0, 0, 0},
                New Single() {0, 0, 0, 0, 0},
                New Single() {0, 0, 0, 0, 0},
                New Single() {0, 0, 0, 1, 0},
                New Single() {target.R / 255.0F, target.G / 255.0F, target.B / 255.0F, 0, 1}
            })
            Dim ia As New Imaging.ImageAttributes()
            ia.SetColorMatrix(cm)
            g.DrawImage(src, New Rectangle(0, 0, bmp.Width, bmp.Height),
                        0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia)
        End Using
        src.Dispose()
        Return bmp
    End Function

    ' Stacked label pair: small grey caption above a readable value.
    Private Shared Function MakeMetaPair(caption As String, value As String, x As Integer, y As Integer) As (cap As Label, val As Label)
        Dim cap As New Label With {
            .Text = caption, .Font = New Font("Segoe UI", 9, FontStyle.Bold),
            .ForeColor = FaintText, .AutoSize = True,
            .Location = New Point(x, y), .BackColor = Color.Transparent
        }
        Dim val As New Label With {
            .Text = value, .Font = New Font("Segoe UI", 12, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True,
            .Location = New Point(x, y + 17), .BackColor = Color.Transparent
        }
        Return (cap, val)
    End Function

    ' =========================================================================
    '  VIEW 2 — NEW PO FORM
    ' =========================================================================
    Private Shared Sub LoadNewPOForm(preloadRequestId As Integer)
        Dim container As Panel = containerRef
        If container Is Nothing Then Return

        newLines = New List(Of POLine)()
        quoteItems = New List(Of QuoteItem)()
        quoteExpanded = False
        isOnNewPOForm = True

        container.SuspendLayout()
        container.Controls.Clear()
        container.BackColor = MintBg

        ' ---- Main 2-column area ----
        Dim mainGrid As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 1,
            .BackColor = MintBg, .Padding = New Padding(24, 4, 24, 20),
            .CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        }
        mainGrid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 34.0F))
        mainGrid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 66.0F))
        mainGrid.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))

        Dim leftCard As Panel = BuildPendingPicker()
        leftCard.Margin = New Padding(0, 0, 8, 0)
        Dim rightHost As Panel = BuildLineEditor()
        rightHost.Margin = New Padding(8, 0, 0, 0)
        mainGrid.Controls.Add(leftCard, 0, 0)
        mainGrid.Controls.Add(rightHost, 1, 0)

        ' ---- Header bar (title + back + save) ----
        Dim header As Panel = BuildFormHeader()
        header.Dock = DockStyle.Top

        container.Controls.Add(mainGrid)
        container.Controls.Add(header)
        container.ResumeLayout()

        ' ---- Optional preload ----
        If preloadRequestId > 0 Then
            AddLineFromRequestId(preloadRequestId)
        End If
        RebuildLines()
        RecomputeGrand()
    End Sub

    Private Shared Function BuildFormHeader() As Panel
        Dim region As New Panel With {.Height = 80, .BackColor = MintBg, .Padding = New Padding(0, 0, 24, 0)}

        ' ← chevron icon (previous.png)
        Dim iconPath As String = IO.Path.Combine(Application.StartupPath, "previous.png")
        Dim picBack As New PictureBox With {
            .Size = New Size(22, 22),
            .Location = New Point(24, 34),
            .BackColor = Color.Transparent,
            .SizeMode = PictureBoxSizeMode.Zoom,
            .Cursor = Cursors.Hand
        }
        If IO.File.Exists(iconPath) Then
            Try
                Dim raw As Image = Image.FromFile(iconPath)
                ' Tint to MintDeep by re-drawing with a colour matrix
                Dim bmp As New Bitmap(raw.Width, raw.Height)
                Using g As Graphics = Graphics.FromImage(bmp)
                    Dim cm As New Imaging.ColorMatrix(New Single()() {
                        New Single() {0, 0, 0, 0, 0},
                        New Single() {0, 0, 0, 0, 0},
                        New Single() {0, 0, 0, 0, 0},
                        New Single() {0, 0, 0, 1, 0},
                        New Single() {CSng(0 / 255.0F), CSng(130 / 255.0F), CSng(109 / 255.0F), 0, 1}
                    })
                    Dim ia As New Imaging.ImageAttributes()
                    ia.SetColorMatrix(cm)
                    g.DrawImage(raw, New Rectangle(0, 0, bmp.Width, bmp.Height),
                                0, 0, raw.Width, raw.Height, GraphicsUnit.Pixel, ia)
                End Using
                picBack.Image = bmp
                raw.Dispose()
            Catch
                picBack.Image = Nothing
            End Try
        End If
        AddHandler picBack.Click, Sub() TryLeaveNewPO()
        AddHandler picBack.MouseEnter,
            Sub()
                picBack.BackColor = Color.FromArgb(30, 0, 130, 109)
                picBack.Padding = New Padding(2)
            End Sub
        AddHandler picBack.MouseLeave,
            Sub()
                picBack.BackColor = Color.Transparent
                picBack.Padding = New Padding(0)
            End Sub

        Dim lblTitle As New Label With {
            .Text = "New Purchase Order", .Font = New Font("Segoe UI", 22, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True,
            .Location = New Point(picBack.Right + 10, 22),
            .BackColor = Color.Transparent
        }


        btnSaveRef = New Button With {
            .Text = "Save Purchase Order", .Size = New Size(210, 44),
            .FlatStyle = FlatStyle.Flat, .BackColor = MintDeep, .ForeColor = Color.White,
            .Font = New Font("Segoe UI Semibold", 11), .Cursor = Cursors.Hand,
            .TextAlign = ContentAlignment.MiddleCenter,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        btnSaveRef.FlatAppearance.BorderSize = 0
        RoundControl(btnSaveRef, 12)
        AddHover(btnSaveRef, MintDeep, DeepHover)
        AddHandler btnSaveRef.Click, Sub() CommitNewPO()

        region.Controls.AddRange({picBack, lblTitle, btnSaveRef})
        AddHandler region.Resize, Sub()
                                      btnSaveRef.Top = 18
                                      btnSaveRef.Left = region.Width - 24 - btnSaveRef.Width
                                  End Sub
        btnSaveRef.Top = 18
        btnSaveRef.Left = 700
        Return region
    End Function

    ' ---- Left: pending-requests picker ----
    Private Shared Function BuildPendingPicker() As Panel
        Dim card As New BufferedPanel With {.Dock = DockStyle.Fill, .BackColor = CardBg}
        AddHandler card.Paint, Sub(s, e) PaintCard(CType(s, Panel), e)

        Dim pendings As List(Of PendingReqLite) = LoadPendingLite()

        Dim header As Panel = BuildCardHeader("Pending Requests",
            If(pendings.Count = 0, "Nothing waiting",
               pendings.Count & If(pendings.Count = 1, " request waiting", " requests waiting") &
               "  " & ChrW(8226) & "  tap to add"))
        header.Dock = DockStyle.Top

        Dim rowsHost As New Panel With {
            .Dock = DockStyle.Fill, .BackColor = Color.Transparent, .AutoScroll = True,
            .Padding = New Padding(14, 12, 14, 12)
        }
        card.Controls.Add(rowsHost)
        card.Controls.Add(header)
        rowsHost.BringToFront()

        If pendings.Count = 0 Then
            Dim lbl As New Label With {
                .Text = "No pending requests right now." & Environment.NewLine &
                        "Use the product search to add items manually.",
                .Font = New Font("Segoe UI", 11), .ForeColor = SubText, .AutoSize = False,
                .TextAlign = ContentAlignment.TopCenter, .Dock = DockStyle.Top, .Height = 60,
                .BackColor = Color.Transparent
            }
            rowsHost.Controls.Add(lbl)
        Else
            Dim y As Integer = 4
            For Each req In pendings
                Dim row As Panel = BuildPendingPickRow(req, 78)
                row.Location = New Point(0, y)
                row.Width = rowsHost.ClientSize.Width - 4
                row.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
                rowsHost.Controls.Add(row)
                y += 88
            Next
            Dim onListResize As EventHandler = Sub(s, e)
                                                   Dim w As Integer = rowsHost.ClientSize.Width - 4
                                                   For Each c As Control In rowsHost.Controls
                                                       c.Width = w
                                                   Next
                                               End Sub
            AddHandler rowsHost.Resize, onListResize
            AddHandler rowsHost.VisibleChanged,
                Sub()
                    If rowsHost.Visible Then
                        Dim w As Integer = rowsHost.ClientSize.Width - 4
                        For Each c As Control In rowsHost.Controls
                            c.Width = w
                        Next
                    End If
                End Sub
        End If

        Return card
    End Function

    Private Shared Function BuildPendingPickRow(req As PendingReqLite, height As Integer) As Panel
        Dim row As New BufferedPanel With {.Height = height, .BackColor = Color.Transparent, .Cursor = Cursors.Hand}
        Dim hovered As Boolean = False
        Dim initial As String = If(String.IsNullOrWhiteSpace(req.ProductName), "?",
                                   req.ProductName.Trim().Substring(0, 1).ToUpperInvariant())

        AddHandler row.Paint,
            Sub(s, e)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias

                ' card tile

                Dim rect As New Rectangle(8, 3, row.Width - 14, row.Height - 6)
                Using path As GraphicsPath = RoundRect(rect, 12)
                    Using b As New SolidBrush(If(hovered, Color.FromArgb(228, 246, 239), TileBg))
                        g.FillPath(b, path)
                    End Using
                    Using pen As New Pen(If(hovered, Color.FromArgb(140, 212, 192), Color.FromArgb(223, 238, 233)), 1.2F)
                        g.DrawPath(pen, path)
                    End Using
                End Using

                ' avatar (rounded square, mint, white initial)
                ' thumbnail — paint swatch · photo · initial
                Dim av As New Rectangle(16, (row.Height - 44) \ 2, 44, 44)
                PaintLineThumb(g, av, req.ColorHex, req.ImagePath, req.ProductName)
                Using path As GraphicsPath = RoundRect(av, 13)
                    e.Graphics.SetClip(path)
                    Dim imgLoaded As Boolean = False
                    If Not String.IsNullOrWhiteSpace(req.ImagePath) Then
                        Try
                            Dim fullPath As String = req.ImagePath
                            If Not IO.Path.IsPathRooted(fullPath) Then
                                fullPath = IO.Path.Combine(Application.StartupPath, fullPath)
                            End If
                            If IO.File.Exists(fullPath) Then
                                Using img As Image = Image.FromFile(fullPath)
                                    g.DrawImage(img, av)
                                End Using
                                imgLoaded = True
                            End If
                        Catch
                        End Try
                    End If
                    e.Graphics.ResetClip()
                    If Not imgLoaded Then
                        Using br As New LinearGradientBrush(av, MintPrimary, MintDeep, 90.0F)
                            g.FillPath(br, path)
                        End Using
                        Using f As New Font("Segoe UI Semibold", 16, FontStyle.Bold), br As New SolidBrush(Color.White)
                            Dim sz = g.MeasureString(initial, f)
                            g.DrawString(initial, f, br, av.X + (av.Width - sz.Width) / 2.0F, av.Y + (av.Height - sz.Height) / 2.0F)
                        End Using
                    End If
                End Using
            End Sub
        AddHandler row.MouseEnter, Sub()
                                       hovered = True
                                       row.Invalidate()
                                   End Sub
        AddHandler row.MouseLeave, Sub()
                                       hovered = False
                                       row.Invalidate()
                                   End Sub

        Const textX As Integer = 74

        Dim lblName As New Label With {
            .Text = req.ProductName, .Font = New Font("Segoe UI", 12, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(textX, 16),
            .BackColor = Color.Transparent
        }

        ' Qty chip
        Dim pillQty As New Label With {
            .Text = "Qty " & req.QtyRequested, .Font = New Font("Segoe UI Semibold", 8.5F),
            .ForeColor = MintDeep, .BackColor = Color.FromArgb(210, 238, 229),
            .AutoSize = True, .Padding = New Padding(8, 3, 8, 3),
            .Location = New Point(textX, 43)
        }
        RoundLabel(pillQty, 9)

        Dim lblBy As New Label With {
            .Text = ChrW(8226) & "  " & req.RequestedBy, .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent
        }

        ' Add pill
        Dim lblAdd As New Label With {
            .Text = "+  Add", .Font = New Font("Segoe UI Semibold", 10),
            .ForeColor = Color.White, .BackColor = MintPrimary,
            .AutoSize = False, .TextAlign = ContentAlignment.MiddleCenter,
            .Cursor = Cursors.Hand, .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        RoundLabel(lblAdd, 9)
        lblAdd.Size = New Size(74, 34)   ' set AFTER RoundLabel so region applies
        AddHandler lblAdd.MouseEnter, Sub()
                                          lblAdd.BackColor = MintHover
                                          hovered = True : row.Invalidate()
                                      End Sub
        AddHandler lblAdd.MouseLeave, Sub()
                                          lblAdd.BackColor = MintPrimary
                                          hovered = False : row.Invalidate()
                                      End Sub

        row.Controls.AddRange({lblName, pillQty, lblBy, lblAdd})

        Dim capReq As PendingReqLite = req
        Dim doAdd As Action = Sub() AddLineFromRequest(capReq)
        AddHandler row.Click, Sub() doAdd()
        AddHandler lblName.Click, Sub() doAdd()
        AddHandler pillQty.Click, Sub() doAdd()
        AddHandler lblBy.Click, Sub() doAdd()
        AddHandler lblAdd.Click, Sub() doAdd()

        AddHandler row.Resize,
            Sub()
                lblAdd.Left = row.Width - lblAdd.Width - 18
                lblAdd.Top = (height - lblAdd.Height) \ 2
                lblBy.Left = pillQty.Right + 8
                lblBy.Top = pillQty.Top + (pillQty.Height - lblBy.Height) \ 2
            End Sub
        ' initial placement
        lblBy.Left = pillQty.Right + 8
        lblBy.Top = pillQty.Top + (pillQty.Height - lblBy.Height) \ 2

        Return row
    End Function


    ' ---- Right: supplier/date settings + add bar + lines + grand total ----
    ' ---- Right: settings card on top + one unified Items card (add + rows + total) ----
    Private Shared Function BuildLineEditor() As Panel
        Dim host As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.Transparent}

        ' Items card fills the rest — contains header(+add), rows, and grand-total footer
        Dim itemsCard As New BufferedPanel With {.Dock = DockStyle.Fill, .BackColor = CardBg}
        AddHandler itemsCard.Paint, Sub(s, e) PaintCard(CType(s, Panel), e)

        ' --- grand-total footer (bottom, inside the card): dark mint gradient
        '     "TOTAL" card, mirroring the POS Transaction screen ---
        ' Inset the gradient pill well inside the card so it never overlaps the
        ' card's rounded bottom border, and keep the footer short enough that it
        ' doesn't clip the scrolling line rows above it.
        Dim grandBar As New BufferedPanel With {
            .Height = 84, .Dock = DockStyle.Bottom, .BackColor = Color.Transparent,
            .Padding = New Padding(20, 10, 22, 16)
        }
        AddHandler grandBar.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim r As New Rectangle(
                    grandBar.Padding.Left, grandBar.Padding.Top,
                    Math.Max(0, grandBar.Width - grandBar.Padding.Horizontal - 1),
                    Math.Max(0, grandBar.Height - grandBar.Padding.Vertical - 1))
                If r.Width <= 4 OrElse r.Height <= 4 Then Return
                Using path As GraphicsPath = RoundRect(r, 14)
                    Using br As New LinearGradientBrush(r, MintDark, TotalDeep, 90.0F)
                        e.Graphics.FillPath(br, path)
                    End Using
                End Using
            End Sub

        Dim lblCap As New Label With {
            .Text = "TOTAL", .Font = New Font("Segoe UI", 14, FontStyle.Bold),
            .ForeColor = Color.FromArgb(200, 240, 225), .AutoSize = True, .BackColor = Color.Transparent
        }
        lblGrandRef = New Label With {
            .Text = Peso & "0.00", .Font = New Font("Segoe UI", 22, FontStyle.Bold),
            .ForeColor = GoldValue, .AutoSize = True, .BackColor = Color.Transparent
        }
        grandBar.Controls.AddRange({lblCap, lblGrandRef})

        ' Center both labels inside the gradient card (not the whole footer panel)
        Dim layoutGrand As Action =
            Sub()
                Dim padL As Integer = grandBar.Padding.Left
                Dim padR As Integer = grandBar.Padding.Right
                Dim padT As Integer = grandBar.Padding.Top
                Dim cardH As Integer = grandBar.Height - grandBar.Padding.Vertical
                Dim pillRight As Integer = grandBar.Width - padR

                lblCap.Left = padL + 18
                lblCap.Top = padT + (cardH - lblCap.Height) \ 2

                Dim valX As Integer = pillRight - 18 - lblGrandRef.Width
                ' never let the value cross into the caption
                Dim minX As Integer = lblCap.Right + 16
                If valX < minX Then valX = minX
                lblGrandRef.Left = valX
                lblGrandRef.Top = padT + (cardH - lblGrandRef.Height) \ 2
            End Sub
        grandBar.Tag = layoutGrand          ' RecomputeGrand re-runs this on text change
        AddHandler grandBar.Resize, Sub() layoutGrand()

        ' --- header with title + inline add control (top, inside the card) ---
        Dim headerPanel As New Panel With {.Height = 92, .Dock = DockStyle.Top, .BackColor = Color.Transparent}
        Dim lblItems As New Label With {
        .Text = "Line Items", .Font = New Font("Segoe UI", 15, FontStyle.Bold),
        .ForeColor = MintDark, .AutoSize = True, .Location = New Point(20, 14), .BackColor = Color.Transparent
    }
        cmbProductRef = New ComboBox With {
        .DropDownStyle = ComboBoxStyle.DropDown, .FlatStyle = FlatStyle.Flat,
        .Font = New Font("Segoe UI", 11), .BackColor = CardBg,
        .AutoCompleteMode = AutoCompleteMode.SuggestAppend,
        .AutoCompleteSource = AutoCompleteSource.ListItems
    }
        For Each p In LoadProducts() : cmbProductRef.Items.Add(p) : Next
        Dim prodField As Panel = MakeField(cmbProductRef, 360)
        prodField.Location = New Point(20, 46)

        Dim btnAdd As New Button With {
        .Text = "+  Add Product", .Size = New Size(150, 46),
        .FlatStyle = FlatStyle.Flat, .BackColor = MintDeep, .ForeColor = Color.White,
        .Font = New Font("Segoe UI Semibold", 11), .Cursor = Cursors.Hand,
        .TextAlign = ContentAlignment.MiddleCenter, .Location = New Point(388, 46),
        .Anchor = AnchorStyles.Top Or AnchorStyles.Left
    }
        btnAdd.FlatAppearance.BorderSize = 0
        RoundControl(btnAdd, 10)
        AddHover(btnAdd, MintDeep, DeepHover)
        AddHandler btnAdd.Click, Sub() AddLineFromCombo()

        Dim headDiv As New Panel With {.Height = 1, .BackColor = RowDivider, .Dock = DockStyle.Bottom}
        headerPanel.Controls.AddRange({lblItems, prodField, btnAdd, headDiv})

        ' --- rows region (fill, inside the card) ---
        linesHostRef = New Panel With {
        .Dock = DockStyle.Fill, .BackColor = Color.Transparent, .AutoScroll = True,
        .Padding = New Padding(0)
    }
        AddHandler linesHostRef.Resize,
        Sub()
            Dim w As Integer = linesHostRef.ClientSize.Width - 28
            For Each c As Control In linesHostRef.Controls
                c.Width = w
                c.Left = 14
            Next
        End Sub

        itemsCard.Controls.Add(linesHostRef)   ' fill first
        itemsCard.Controls.Add(headerPanel)    ' top
        itemsCard.Controls.Add(grandBar)       ' bottom
        linesHostRef.BringToFront()

        ' Settings card on top of the whole right column
        Dim settings As Panel = BuildSettingsCard()
        settings.Dock = DockStyle.Top

        Dim spacer As New Panel With {.Height = 14, .Dock = DockStyle.Top, .BackColor = Color.Transparent}

        host.Controls.Add(itemsCard)
        host.Controls.Add(spacer)
        host.Controls.Add(settings)
        Return host
    End Function

    Private Shared Function BuildSettingsCard() As Panel
        ' Outer wrapper — grows when quote panel expands
        Dim wrapper As New Panel With {.BackColor = Color.Transparent, .Height = 225}

        Dim card As New BufferedPanel With {.Height = 225, .BackColor = CardBg, .Dock = DockStyle.Top}
        AddHandler card.Paint, Sub(s, e) PaintCard(CType(s, Panel), e)

        Dim lblSection As New Label With {
            .Text = "Order Details", .Font = New Font("Segoe UI", 15, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(22, 16),
            .BackColor = Color.Transparent
        }
        Dim lblHint As New Label With {
            .Text = "Who you're ordering from, and when.",
            .Font = New Font("Segoe UI", 9.5F), .ForeColor = FaintText, .AutoSize = True,
            .Location = New Point(22, 42), .BackColor = Color.Transparent
        }

        Dim lblSup As Label = MakeFieldLabel("Supplier")
        Dim lblOrder As Label = MakeFieldLabel("Order Date")
        Dim lblExp As Label = MakeFieldLabel("Expected Date")

        ' --- Supplier ---
        cmbSupplierRef = New ComboBox With {
            .DropDownStyle = ComboBoxStyle.DropDownList, .FlatStyle = FlatStyle.Flat,
            .Font = New Font("Segoe UI", 11), .BackColor = CardBg
        }
        For Each s In LoadSuppliers() : cmbSupplierRef.Items.Add(s) : Next
        If cmbSupplierRef.Items.Count > 0 Then cmbSupplierRef.SelectedIndex = 0
        Dim supField As Panel = MakeField(cmbSupplierRef, 320, 48)

        ' --- Order Date ---
        dtOrderRef = New DateTimePicker With {
            .Format = DateTimePickerFormat.Custom, .CustomFormat = "MMM d, yyyy",
            .Font = New Font("Segoe UI", 11), .Value = DateTime.Today
        }
        ThemeCalendar(dtOrderRef)
        Dim orderField As Panel = MakeField(dtOrderRef, 170, 48)

        ' --- Expected Date ---
        dtExpectedRef = New DateTimePicker With {
            .Format = DateTimePickerFormat.Custom, .CustomFormat = "MMM d, yyyy",
            .Font = New Font("Segoe UI", 11), .ShowCheckBox = True, .Checked = True,
            .Value = DateTime.Today.AddDays(7)
        }
        ThemeCalendar(dtExpectedRef)
        Dim expField As Panel = MakeField(dtExpectedRef, 210, 48)

        ' --- Add Quote button — matches palette: solid MintDeep, white text, icon from file ---
        Dim btnAddQuote As New Button With {
            .Text = "  Add Quote",
            .Size = New Size(148, 44),
            .FlatStyle = FlatStyle.Flat,
            .BackColor = MintDeep,
            .ForeColor = Color.White,
            .Font = New Font("Segoe UI Semibold", 10),
            .Cursor = Cursors.Hand,
            .TextAlign = ContentAlignment.MiddleLeft,
            .ImageAlign = ContentAlignment.MiddleLeft,
            .TextImageRelation = TextImageRelation.ImageBeforeText,
            .Padding = New Padding(10, 0, 12, 0),
            .Anchor = AnchorStyles.Bottom Or AnchorStyles.Right
        }
        btnAddQuote.FlatAppearance.BorderSize = 0
        RoundControl(btnAddQuote, 10)
        AddHover(btnAddQuote, MintDeep, DeepHover)
        ' Override hover so it stays dark while expanded
        AddHandler btnAddQuote.MouseLeave,
            Sub()
                btnAddQuote.BackColor = If(quoteExpanded, DeepHover, MintDeep)
            End Sub

        ' Load and tint down-arrow icon (white, 18×18)
        Dim iconDown As Image = Nothing
        Dim iconUp As Image = Nothing
        Dim downPath As String = IO.Path.Combine(Application.StartupPath, "down-arrow.png")
        Dim upPath As String = IO.Path.Combine(Application.StartupPath, "up-arrow.png")

        Dim TintWhite As Func(Of String, Image) =
            Function(path As String) As Image
                If Not IO.File.Exists(path) Then Return Nothing
                Try
                    Dim raw As Image = Image.FromFile(path)
                    Dim bmp As New Bitmap(18, 18)
                    Using g As Graphics = Graphics.FromImage(bmp)
                        g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic
                        Dim cm As New Imaging.ColorMatrix(New Single()() {
                            New Single() {0, 0, 0, 0, 0},
                            New Single() {0, 0, 0, 0, 0},
                            New Single() {0, 0, 0, 0, 0},
                            New Single() {0, 0, 0, 1, 0},
                            New Single() {1, 1, 1, 0, 1}
                        })
                        Dim ia As New Imaging.ImageAttributes()
                        ia.SetColorMatrix(cm)
                        g.DrawImage(raw, New Rectangle(0, 0, 18, 18),
                                    0, 0, raw.Width, raw.Height, GraphicsUnit.Pixel, ia)
                    End Using
                    raw.Dispose()
                    Return bmp
                Catch
                    Return Nothing
                End Try
            End Function

        iconDown = TintWhite(downPath)
        iconUp = TintWhite(upPath)
        btnAddQuote.Image = iconDown

        card.Controls.AddRange({lblSection, lblHint, lblSup, supField, lblOrder, orderField, lblExp, expField, btnAddQuote})

        ' force native controls to repaint at their final width (kills the ghost arrow)
        Dim repaintFields As Action =
            Sub()
                For Each fld As Control In New Control() {supField, orderField, expField}
                    fld.Invalidate()
                    If fld.Controls.Count > 0 Then fld.Controls(0).Refresh()
                Next
            End Sub

        Dim doLayout As Action =
            Sub()
                If card.Width < 120 Then Return
                Const padL As Integer = 22
                Const padR As Integer = 22
                Const gap As Integer = 16
                Const labY As Integer = 74
                Const fldY As Integer = 98
                Const fldH As Integer = 48

                Dim avail As Integer = card.Width - padL - padR - gap * 2
                Dim supW As Integer = CInt(avail * 0.4)
                Dim orderW As Integer = CInt(avail * 0.28)
                Dim expW As Integer = avail - supW - orderW

                If orderW < 150 Then orderW = 150
                If expW < 190 Then expW = 190
                supW = avail - orderW - expW
                If supW < 170 Then supW = 170

                Dim x As Integer = padL
                lblSup.Location = New Point(x, labY)
                supField.SetBounds(x, fldY, supW, fldH)

                x = supField.Right + gap
                lblOrder.Location = New Point(x, labY)
                orderField.SetBounds(x, fldY, orderW, fldH)

                x = orderField.Right + gap
                lblExp.Location = New Point(x, labY)
                expField.SetBounds(x, fldY, expW, fldH)

                ' Quote button — sits below the field row, right-aligned
                btnAddQuote.Left = card.Width - padR - btnAddQuote.Width
                btnAddQuote.Top = fldY + fldH + 12

                repaintFields()
            End Sub

        AddHandler card.Resize, Sub() doLayout()
        AddHandler card.HandleCreated,
            Sub()
                card.BeginInvoke(Sub()
                                     doLayout()
                                     card.BeginInvoke(Sub()
                                                          doLayout()
                                                          repaintFields()
                                                      End Sub)
                                 End Sub)
            End Sub
        doLayout()

        ' ── Inline Quote Panel (expands below the card, with margins) ─────────────
        quoteHostPanelRef = BuildQuotePanel()
        quoteHostPanelRef.Visible = False

        ' Margin wrapper so the quote card floats with space on all sides
        Dim quoteMargin As New Panel With {
            .BackColor = Color.Transparent,
            .Padding = New Padding(0, 15, 0, 10),
            .Visible = False
        }
        quoteMargin.Controls.Add(quoteHostPanelRef)
        quoteHostPanelRef.Dock = DockStyle.Fill

        AddHandler btnAddQuote.Click,
            Sub()
                quoteExpanded = Not quoteExpanded
                quoteMargin.Visible = quoteExpanded
                quoteHostPanelRef.Visible = quoteExpanded
                If quoteExpanded Then
                    btnAddQuote.Text = "  Hide Quote"
                    btnAddQuote.Image = iconUp
                    btnAddQuote.BackColor = DeepHover
                Else
                    btnAddQuote.Text = "  Add Quote"
                    btnAddQuote.Image = iconDown
                    btnAddQuote.BackColor = MintDeep
                End If
                Dim quoteH As Integer = If(quoteExpanded, quoteHostPanelRef.Height + 28, 0)
                quoteMargin.Height = quoteH
                wrapper.Height = 225 + quoteH
            End Sub

        AddHandler quoteHostPanelRef.Resize,
            Sub()
                If quoteExpanded Then
                    Dim quoteH As Integer = quoteHostPanelRef.Height + 28
                    quoteMargin.Height = quoteH
                    wrapper.Height = 225 + quoteH
                End If
            End Sub

        wrapper.Controls.Add(quoteMargin)
        wrapper.Controls.Add(card)
        quoteMargin.Top = 230
        quoteMargin.Left = 0
        quoteMargin.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        AddHandler wrapper.Resize,
            Sub()
                quoteMargin.Width = wrapper.Width
            End Sub

        Return wrapper
    End Function

    ' ── Inline Quote Panel — appears below Order Details card ───────────────────
    Private Shared Function BuildQuotePanel() As Panel
        Dim approved As List(Of ApprovedReqLite) = LoadApprovedRequestsLite()

        Dim quoteCard As New BufferedPanel With {.Height = 300, .BackColor = CardBg}
        AddHandler quoteCard.Paint, Sub(s, e) PaintCard(CType(s, Panel), e)

        ' Header — generous top padding, bigger title, clearly separated subtitle
        Dim lblHead As New Label With {
            .Text = "Quote Details",
            .Font = New Font("Segoe UI", 15, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True,
            .Location = New Point(24, 20), .BackColor = Color.Transparent
        }
        Dim lblSubHead As New Label With {
            .Text = "Record the supplier's quoted price before placing the order.",
            .Font = New Font("Segoe UI", 10.5F), .ForeColor = SubText, .AutoSize = True,
            .Location = New Point(24, 46), .BackColor = Color.Transparent
        }
        Dim divider As New Panel With {
            .Height = 1, .BackColor = RowDivider,
            .Location = New Point(20, 74), .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        }

        ' Row 1 — Request picker + Valid Until
        Dim lblReq As Label = MakeFieldLabel("Approved Request")
        Dim lblValid As Label = MakeFieldLabel("Valid Until")

        Dim cmbReq As New ComboBox With {
            .DropDownStyle = ComboBoxStyle.DropDownList, .FlatStyle = FlatStyle.Flat,
            .Font = New Font("Segoe UI", 10.5F), .BackColor = CardBg
        }
        If approved.Count = 0 Then
            cmbReq.Items.Add("— No approved requests —")
            cmbReq.SelectedIndex = 0
            cmbReq.Enabled = False
        Else
            cmbReq.Items.Add("— Select a request —")
            For Each r In approved : cmbReq.Items.Add(r) : Next
            cmbReq.SelectedIndex = 0
        End If
        Dim reqField As Panel = MakeField(cmbReq, 340, 44)

        Dim dtValid As New DateTimePicker With {
            .Format = DateTimePickerFormat.Custom, .CustomFormat = "MMM d, yyyy",
            .Font = New Font("Segoe UI", 10.5F),
            .Value = DateTime.Today.AddDays(14)
        }
        ThemeCalendar(dtValid)
        Dim validField As Panel = MakeField(dtValid, 170, 44)

        ' Row 2 — Unit Price + running Total label
        Dim lblPrice As Label = MakeFieldLabel("Quoted Unit Price (" & Peso & ")")
        Dim lblTotal As New Label With {
            .Text = "Total:  " & Peso & "0.00",
            .Font = New Font("Segoe UI Semibold", 11),
            .ForeColor = MintDark, .AutoSize = True, .BackColor = Color.Transparent
        }

        Dim txtPrice As New TextBox With {
            .Font = New Font("Segoe UI", 12), .ForeColor = MintDark,
            .BackColor = CardBg, .BorderStyle = BorderStyle.None,
            .Text = "0.00"
        }
        Dim priceField As Panel = MakeField(txtPrice, 160, 44)

        ' Notes
        Dim lblNotes As Label = MakeFieldLabel("Notes (optional)")
        Dim txtNotes As New TextBox With {
            .Font = New Font("Segoe UI", 10.5F), .ForeColor = MintDark,
            .BackColor = CardBg, .BorderStyle = BorderStyle.None,
            .Text = ""
        }
        Dim notesField As Panel = MakeField(txtNotes, 340, 44)

        ' Save Quote button
        Dim btnSaveQuote As New Button With {
            .Text = "Save Quote", .Size = New Size(148, 44),
            .FlatStyle = FlatStyle.Flat, .BackColor = MintDeep, .ForeColor = Color.White,
            .Font = New Font("Segoe UI Semibold", 11), .Cursor = Cursors.Hand,
            .TextAlign = ContentAlignment.MiddleCenter
        }
        btnSaveQuote.FlatAppearance.BorderSize = 0
        RoundControl(btnSaveQuote, 10)
        AddHover(btnSaveQuote, MintDeep, DeepHover)

        ' Live total update
        Dim updateTotal As Action =
            Sub()
                Dim qty As Integer = 0
                If TypeOf cmbReq.SelectedItem Is ApprovedReqLite Then
                    qty = CType(cmbReq.SelectedItem, ApprovedReqLite).QtyRequested
                End If
                Dim price As Decimal = 0D
                Decimal.TryParse(txtPrice.Text, price)
                Dim tot As Decimal = Math.Round(price * qty, 2)
                lblTotal.Text = "Total:  " & Peso & tot.ToString("N2")
                If qty > 0 Then lblTotal.Text &= "   (" & qty & " " & ChrW(215) & " " & Peso & price.ToString("N2") & ")"
            End Sub

        AddHandler cmbReq.SelectedIndexChanged, Sub() updateTotal()
        AddHandler txtPrice.TextChanged, Sub() updateTotal()

        ' Save handler — writes quotations + quotation_items, flips request to Quoted
        AddHandler btnSaveQuote.Click,
            Sub()
                If Not TypeOf cmbReq.SelectedItem Is ApprovedReqLite Then
                    AppModal.Show("Please select an approved request.", "Validation",
                                  MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    Return
                End If
                Dim price As Decimal = 0D
                If Not Decimal.TryParse(txtPrice.Text, price) OrElse price < 0 Then
                    AppModal.Show("Please enter a valid unit price.", "Validation",
                                  MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    Return
                End If

                Dim req As ApprovedReqLite = CType(cmbReq.SelectedItem, ApprovedReqLite)

                ' Supplier is whatever is currently selected in the PO form
                Dim sup As SupItem = If(cmbSupplierRef IsNot Nothing,
                    TryCast(cmbSupplierRef.SelectedItem, SupItem), Nothing)
                If sup Is Nothing Then
                    AppModal.Show("Please select a supplier in Order Details first.", "No Supplier",
                                  MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    Return
                End If

                Dim total As Decimal = Math.Round(price * req.QtyRequested, 2)
                Dim notes As String = txtNotes.Text.Trim()
                Dim quotNo As String = "QT-" & DateTime.Now.ToString("yyMMddHHmmss")

                Try
                    Using conn = Db.GetConnection()
                        conn.Open()
                        Using tx = conn.BeginTransaction()
                            Dim quotId As Integer
                            Using cmd As New MySqlCommand(
                                "INSERT INTO quotations (quotation_no, request_id, supplier_id, created_by, " &
                                "valid_until, status, notes, total_amount) " &
                                "VALUES (@qno, @rid, @sid, @uid, @vd, 'Draft', @notes, @total)", conn, tx)
                                cmd.Parameters.AddWithValue("@qno", quotNo)
                                cmd.Parameters.AddWithValue("@rid", req.RequestId)
                                cmd.Parameters.AddWithValue("@sid", sup.Id)
                                cmd.Parameters.AddWithValue("@uid", Session.CurrentUserId)
                                cmd.Parameters.AddWithValue("@vd", dtValid.Value.Date)
                                cmd.Parameters.AddWithValue("@notes",
                                    If(notes = "", CType(DBNull.Value, Object), CObj(notes)))
                                cmd.Parameters.AddWithValue("@total", total)
                                cmd.ExecuteNonQuery()
                                quotId = Convert.ToInt32(cmd.LastInsertedId)
                            End Using

                            Using cmd As New MySqlCommand(
                                "INSERT INTO quotation_items (quotation_id, product_id, qty_requested, unit_price) " &
                                "VALUES (@qid, @pid, @qty, @price)", conn, tx)
                                cmd.Parameters.AddWithValue("@qid", quotId)
                                cmd.Parameters.AddWithValue("@pid", req.ProductId)
                                cmd.Parameters.AddWithValue("@qty", req.QtyRequested)
                                cmd.Parameters.AddWithValue("@price", price)
                                cmd.ExecuteNonQuery()
                            End Using

                            Using cmd As New MySqlCommand(
                                "UPDATE purchase_requests SET status = 'Quoted' WHERE request_id = @rid", conn, tx)
                                cmd.Parameters.AddWithValue("@rid", req.RequestId)
                                cmd.ExecuteNonQuery()
                            End Using

                            tx.Commit()
                        End Using
                    End Using

                    ' Auto-add this product as a PO line at the quoted price
                    Dim already As Boolean = False
                    For Each ln In newLines
                        If ln.ProductId = req.ProductId Then
                            ln.UnitCost = price   ' update the existing line's price
                            already = True
                            Exit For
                        End If
                    Next
                    If Not already Then
                        newLines.Add(New POLine With {
                            .ProductId = req.ProductId, .ProductName = req.ProductName,
                            .Sku = req.Sku, .Qty = req.QtyRequested,
                            .UnitCost = price, .RequestId = req.RequestId
                        })
                    End If
                    RebuildLines()
                    RecomputeGrand()

                    AppModal.Show("Quote " & quotNo & " saved as Draft." & Environment.NewLine &
                                  "The item has been added to the line items at the quoted price.",
                                  "Quote Saved", MessageBoxButtons.OK, MessageBoxIcon.Information)

                    ' Reset quote form for another entry
                    cmbReq.SelectedIndex = 0
                    txtPrice.Text = "0.00"
                    txtNotes.Clear()
                    updateTotal()

                Catch ex As Exception
                    AppModal.Show("Could not save quote:" & Environment.NewLine & ex.Message,
                                  "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
                End Try
            End Sub

        quoteCard.Controls.AddRange({lblHead, lblSubHead, divider,
                                     lblReq, reqField, lblValid, validField,
                                     lblPrice, priceField, lblTotal,
                                     lblNotes, notesField, btnSaveQuote})

        ' Layout inside quote card
        Dim doQLayout As Action =
            Sub()
                If quoteCard.Width < 120 Then Return
                Const padL As Integer = 22
                Const padR As Integer = 22
                Const gap As Integer = 14
                Dim w As Integer = quoteCard.Width

                ' Divider
                divider.Width = w - 40

                ' Row 1 — label at 90, field at 112 (18px gap after divider at 74)
                Const r1LabelY As Integer = 90
                Const r1FieldY As Integer = 110
                Dim reqW As Integer = CInt((w - padL - padR - gap) * 0.6)
                Dim valW As Integer = w - padL - padR - gap - reqW
                If valW < 160 Then valW = 160 : reqW = w - padL - padR - gap - valW
                lblReq.Location = New Point(padL, r1LabelY)
                reqField.SetBounds(padL, r1FieldY, reqW, 44)
                lblValid.Location = New Point(reqField.Right + gap, r1LabelY)
                validField.SetBounds(reqField.Right + gap, r1FieldY, valW, 44)

                ' Row 2 — 20px breathing gap after row 1 fields
                Const r2LabelY As Integer = 172
                Const r2FieldY As Integer = 192
                Dim priceW As Integer = 180
                Dim notesW As Integer = w - padL - padR - gap - priceW - gap - 140
                If notesW < 120 Then notesW = 120
                lblPrice.Location = New Point(padL, r2LabelY)
                priceField.SetBounds(padL, r2FieldY, priceW, 44)
                lblNotes.Location = New Point(priceField.Right + gap, r2LabelY)
                notesField.SetBounds(priceField.Right + gap, r2FieldY, notesW, 44)

                ' Total label — vertically centred beside notes
                lblTotal.Location = New Point(notesField.Right + gap,
                                              r2FieldY + (44 - lblTotal.Height) \ 2)

                ' Save button — 18px below row 2 fields
                btnSaveQuote.Left = w - padR - btnSaveQuote.Width
                btnSaveQuote.Top = r2FieldY + 44 + 18

                quoteCard.Height = btnSaveQuote.Bottom + 22
            End Sub

        AddHandler quoteCard.Resize, Sub() doQLayout()
        AddHandler quoteCard.HandleCreated,
            Sub()
                quoteCard.BeginInvoke(Sub() doQLayout())
            End Sub
        doQLayout()

        Return quoteCard
    End Function

    ' Loads approved purchase requests for the inline quote picker.
    Private Shared Function LoadApprovedRequestsLite() As List(Of ApprovedReqLite)
        Dim result As New List(Of ApprovedReqLite)
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT pr.request_id, pr.product_id, p.name AS pname, p.sku, " &
                    "pr.qty_requested, p.unit_price " &
                    "FROM purchase_requests pr " &
                    "JOIN products p ON pr.product_id = p.product_id " &
                    "WHERE pr.status = 'Approved' " &
                    "ORDER BY pr.created_at DESC", conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            result.Add(New ApprovedReqLite With {
                                .RequestId = SafeInt(rdr("request_id")),
                                .ProductId = SafeInt(rdr("product_id")),
                                .ProductName = rdr("pname").ToString(),
                                .Sku = rdr("sku").ToString(),
                                .QtyRequested = SafeInt(rdr("qty_requested")),
                                .UnitPrice = If(IsDBNull(rdr("unit_price")), 0D,
                                               Convert.ToDecimal(rdr("unit_price")))
                            })
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load approved requests:" & Environment.NewLine & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
        Return result
    End Function

    ' Rounded, bordered field wrapper. Inner control is resized to fill the
    ' wrapper AND invalidated on every resize so its dropdown button never ghosts.
    Private Shared Function MakeField(inner As Control, w As Integer, Optional h As Integer = 46) As BufferedPanel
        Dim host As New BufferedPanel With {.Size = New Size(w, h), .BackColor = CardBg}
        Dim isFocused As Boolean = False

        AddHandler host.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
                Dim rect As New Rectangle(0, 0, host.Width - 1, host.Height - 1)
                Using path As Drawing2D.GraphicsPath = RoundRect(rect, 10)
                    Using b As New SolidBrush(CardBg)
                        e.Graphics.FillPath(b, path)
                    End Using
                    Using pen As New Pen(If(isFocused, InputFocus, InputBorder), If(isFocused, 1.6F, 1.0F))
                        e.Graphics.DrawPath(pen, path)
                    End Using
                End Using
            End Sub

        inner.BackColor = CardBg
        inner.Width = w - 22
        inner.Location = New Point(11, (h - inner.Height) \ 2)
        host.Controls.Add(inner)

        ' Resize the inner control explicitly, then repaint it (no ghost arrow).
        AddHandler host.Resize,
            Sub()
                inner.Left = 11
                inner.Top = (host.Height - inner.Height) \ 2
                inner.Width = Math.Max(20, host.Width - 22)
                inner.Refresh()      ' synchronous repaint clears the stale dropdown button
                host.Invalidate()
            End Sub

        AddHandler inner.Enter, Sub()
                                    isFocused = True
                                    host.Invalidate()
                                End Sub
        AddHandler inner.Leave, Sub()
                                    isFocused = False
                                    host.Invalidate()
                                End Sub
        Return host
    End Function





    ' ---- Line rows ----
    Private Shared Sub RebuildLines()
        If linesHostRef Is Nothing Then Return
        linesHostRef.SuspendLayout()
        linesHostRef.Controls.Clear()

        ' Empty state — dashed placeholder card
        If newLines.Count = 0 Then
            Dim ph As New BufferedPanel With {.BackColor = Color.Transparent}
            AddHandler ph.Paint,
                Sub(s, e)
                    e.Graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
                    Dim r As New Rectangle(2, 2, ph.Width - 6, ph.Height - 6)
                    Using pen As New Pen(InputBorder, 1.4F)
                        pen.DashStyle = Drawing2D.DashStyle.Dash
                        Using path As Drawing2D.GraphicsPath = RoundRect(r, 12)
                            e.Graphics.DrawPath(pen, path)
                        End Using
                    End Using
                End Sub
            ph.Location = New Point(0, 8)
            ph.Size = New Size(linesHostRef.ClientSize.Width - 4, 130)
            ph.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
            Dim lbl As New Label With {
                .Text = "No line items yet" & Environment.NewLine &
                        "Add from a pending request or search a product above.",
                .Font = New Font("Segoe UI", 12), .ForeColor = SubText, .AutoSize = False,
                .TextAlign = ContentAlignment.MiddleCenter, .Dock = DockStyle.Fill,
                .BackColor = Color.Transparent
            }
            ph.Controls.Add(lbl)
            linesHostRef.Controls.Add(ph)
            linesHostRef.ResumeLayout()
            Return
        End If

        ' --- render one row per line item ---
        Dim y As Integer = 8
        For Each ln In newLines
            Dim row As Panel = BuildLineRow(ln, 66)
            row.Location = New Point(14, y)
            row.Width = linesHostRef.ClientSize.Width - 28
            row.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
            linesHostRef.Controls.Add(row)
            y += 70
        Next

        linesHostRef.ResumeLayout()

        ' Re-apply widths and re-run each row's layout NOW and again after the
        ' message pump settles. Double-deferred so the second pass runs after
        ' the docking/anchoring engine has fully resolved all panel widths.
        Dim hostLocal As Panel = linesHostRef
        Dim applyWidths As Action =
            Sub()
                If hostLocal Is Nothing OrElse hostLocal.IsDisposed Then Return
                Dim finalW As Integer = hostLocal.ClientSize.Width - 28
                For Each c As Control In hostLocal.Controls
                    c.Left = 14
                    c.Width = finalW
                    Dim relayout = TryCast(c.Tag, Action)
                    If relayout IsNot Nothing Then relayout()
                Next
            End Sub

        applyWidths()
        If hostLocal.IsHandleCreated Then
            hostLocal.BeginInvoke(Sub()
                                      applyWidths()
                                      hostLocal.BeginInvoke(Sub() applyWidths())
                                  End Sub)
        End If
    End Sub




    Private Shared Function BuildLineRow(ln As POLine, height As Integer) As Panel
        Dim row As New BufferedPanel With {.Height = height, .BackColor = Color.Transparent}
        AddHandler row.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect As New Rectangle(0, 0, row.Width - 1, row.Height - 1)
                Using path As GraphicsPath = RoundRect(rect, 10)
                    Using b As New SolidBrush(TileBg)
                        e.Graphics.FillPath(b, path)
                    End Using
                    Using pen As New Pen(Color.FromArgb(159, 225, 203), 1.2F)
                        e.Graphics.DrawPath(pen, path)
                    End Using
                End Using
            End Sub

        Dim capLine As POLine = ln

        ' --- left: name + locked "unit x qty = total" calc line ---
        Dim lblName As New Label With {
            .Text = ln.ProductName, .Font = New Font("Segoe UI", 11.5F, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = False, .BackColor = Color.Transparent,
            .TextAlign = ContentAlignment.MiddleLeft,
            .Location = New Point(16, 11), .Size = New Size(240, 22)
        }
        Dim lblCalc As New Label With {
            .Text = "", .Font = New Font("Segoe UI", 9.5F), .ForeColor = SubText,
            .AutoSize = False, .BackColor = Color.Transparent,
            .TextAlign = ContentAlignment.MiddleLeft,
            .Location = New Point(16, 37), .Size = New Size(260, 18)
        }
        row.Controls.AddRange({lblName, lblCalc})

        Dim refreshCalc As Action =
            Sub()
                Dim src As String = If(capLine.RequestId > 0, "    " & ChrW(8226) & " from request", "")
                lblCalc.Text = Peso & capLine.UnitCost.ToString("N2") & "   " & ChrW(215) & "   " &
                               capLine.Qty & "   =   " & Peso & capLine.LineTotal.ToString("N2") & src
            End Sub

        ' --- qty stepper (editable: - value + , or type in the middle) ---
        Dim stepPanel As New BufferedPanel With {.Size = New Size(108, 32), .BackColor = CardBg}
        AddHandler stepPanel.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim r As New Rectangle(0, 0, stepPanel.Width - 1, stepPanel.Height - 1)
                Using path As GraphicsPath = RoundRect(r, 8)
                    Using b As New SolidBrush(CardBg) : e.Graphics.FillPath(b, path) : End Using
                    Using pen As New Pen(InputBorder, 1.2F) : e.Graphics.DrawPath(pen, path) : End Using
                End Using
            End Sub

        Dim btnMinus As New Button With {
            .Text = ChrW(8722), .Size = New Size(30, 30), .Location = New Point(1, 1),
            .FlatStyle = FlatStyle.Flat, .Font = New Font("Segoe UI", 13),
            .ForeColor = SubText, .BackColor = Color.Transparent, .Cursor = Cursors.Hand, .TabStop = False
        }
        btnMinus.FlatAppearance.BorderSize = 0

        Dim txtQty As New TextBox With {
            .Text = ln.Qty.ToString(), .Width = 44, .Location = New Point(32, 5),
            .TextAlign = HorizontalAlignment.Center, .BorderStyle = BorderStyle.None,
            .Font = New Font("Segoe UI Semibold", 11), .ForeColor = MintDark, .BackColor = CardBg
        }

        Dim btnPlus As New Button With {
            .Text = "+", .Size = New Size(30, 30), .Location = New Point(77, 1),
            .FlatStyle = FlatStyle.Flat, .Font = New Font("Segoe UI", 13),
            .ForeColor = MintPrimary, .BackColor = Color.Transparent, .Cursor = Cursors.Hand, .TabStop = False
        }
        btnPlus.FlatAppearance.BorderSize = 0
        stepPanel.Controls.AddRange({btnMinus, txtQty, btnPlus})

        ' guard prevents the programmatic SetText from re-firing TextChanged work
        Dim guard As Boolean = False
        Dim commitQty As Action(Of Integer) =
            Sub(q As Integer)
                If q < 1 Then q = 1
                capLine.Qty = q
                guard = True
                txtQty.Text = q.ToString()
                guard = False
                refreshCalc()
                RecomputeGrand()
            End Sub

        AddHandler btnMinus.Click, Sub() commitQty(capLine.Qty - 1)
        AddHandler btnPlus.Click, Sub() commitQty(capLine.Qty + 1)
        AddHandler txtQty.KeyPress,
            Sub(s, e)
                If Not Char.IsControl(e.KeyChar) AndAlso Not Char.IsDigit(e.KeyChar) Then e.Handled = True
            End Sub
        AddHandler txtQty.Enter, Sub() txtQty.SelectAll()
        AddHandler txtQty.TextChanged,
            Sub()
                If guard Then Return
                capLine.Qty = SafeIntStr(txtQty.Text)   ' may be 0 mid-typing; normalised on Leave
                refreshCalc()
                RecomputeGrand()
            End Sub
        AddHandler txtQty.Leave, Sub() commitQty(SafeIntStr(txtQty.Text))

        ' --- remove (circular x) ---
        Dim btnRemove As New Button With {
            .Text = ChrW(10005), .Size = New Size(28, 28),
            .FlatStyle = FlatStyle.Flat, .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold),
            .ForeColor = NegativeClr, .BackColor = Color.FromArgb(252, 235, 235),
            .Cursor = Cursors.Hand, .TabStop = False
        }
        btnRemove.FlatAppearance.BorderSize = 0
        AddHandler btnRemove.Paint,
            Sub(s, e)
                Dim bb = CType(s, Button)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Using br As New SolidBrush(bb.BackColor)
                    e.Graphics.FillEllipse(br, 0, 0, bb.Width - 1, bb.Height - 1)
                End Using
                TextRenderer.DrawText(e.Graphics, bb.Text, bb.Font, bb.ClientRectangle, bb.ForeColor,
                    TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub
        AddHandler btnRemove.MouseEnter, Sub()
                                             btnRemove.BackColor = NegativeClr
                                             btnRemove.ForeColor = Color.White
                                             btnRemove.Invalidate()
                                         End Sub
        AddHandler btnRemove.MouseLeave, Sub()
                                             btnRemove.BackColor = Color.FromArgb(252, 235, 235)
                                             btnRemove.ForeColor = NegativeClr
                                             btnRemove.Invalidate()
                                         End Sub
        AddHandler btnRemove.Click,
            Sub()
                newLines.Remove(capLine)
                RebuildLines()
                RecomputeGrand()
            End Sub

        row.Controls.AddRange({stepPanel, btnRemove})

        ' --- layout (right-aligned stepper + delete; text fills the rest) ---
        Dim layoutRow As Action =
            Sub()
                If row.Width < 60 Then Return
                Dim rightPad As Integer = 16
                btnRemove.Left = row.Width - rightPad - btnRemove.Width
                btnRemove.Top = (height - btnRemove.Height) \ 2

                stepPanel.Left = btnRemove.Left - 14 - stepPanel.Width
                stepPanel.Top = (height - stepPanel.Height) \ 2

                Dim textRight As Integer = stepPanel.Left - 12
                lblName.Width = Math.Max(60, textRight - lblName.Left)
                lblCalc.Width = Math.Max(60, textRight - lblCalc.Left)
            End Sub
        AddHandler row.Resize, Sub() layoutRow()
        AddHandler row.SizeChanged, Sub() layoutRow()
        row.Tag = layoutRow
        AddHandler row.HandleCreated,
            Sub()
                row.BeginInvoke(Sub()
                                    layoutRow()
                                    row.Invalidate()
                                End Sub)
            End Sub

        refreshCalc()
        Return row
    End Function

    Private Shared Sub RecomputeGrand()
        If lblGrandRef Is Nothing Then Return
        Dim total As Decimal = 0D
        For Each ln In newLines
            total += ln.LineTotal
        Next
        lblGrandRef.Text = Peso & total.ToString("N2")

        ' The label width changes with the total; re-run the grand bar's own
        ' layout so the gold value stays centered and pinned inside the card.
        Dim parent As Control = lblGrandRef.Parent
        If parent IsNot Nothing Then
            Dim relayout = TryCast(parent.Tag, Action)
            If relayout IsNot Nothing Then
                relayout()
            Else
                lblGrandRef.Left = parent.Width - lblGrandRef.Width - 34
                lblGrandRef.Top = (parent.Height - lblGrandRef.Height) \ 2
            End If
        End If
    End Sub

    ' ---- Adding lines ----
    Private Shared Sub AddLineFromRequest(req As PendingReqLite)
        For Each ln In newLines
            If ln.ProductId = req.ProductId Then
                AppModal.Show("That product is already on this purchase order.",
                              "Already Added", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If
        Next
        newLines.Add(New POLine With {
            .ProductId = req.ProductId, .ProductName = req.ProductName, .Sku = req.Sku,
            .Qty = Math.Max(1, req.QtyRequested), .UnitCost = req.UnitPrice, .RequestId = req.RequestId
        })
        RebuildLines()
        RecomputeGrand()
    End Sub

    Private Shared Sub AddLineFromCombo()
        If cmbProductRef Is Nothing OrElse cmbProductRef.SelectedItem Is Nothing Then
            AppModal.Show("Pick a product from the list first.", "Select a Product",
                          MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        Dim p As ProdItem = TryCast(cmbProductRef.SelectedItem, ProdItem)
        If p Is Nothing Then Return
        For Each ln In newLines
            If ln.ProductId = p.Id Then
                AppModal.Show("That product is already on this purchase order.",
                              "Already Added", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If
        Next
        newLines.Add(New POLine With {
            .ProductId = p.Id, .ProductName = p.Name, .Sku = p.Sku,
            .Qty = 1, .UnitCost = p.Price, .RequestId = 0
        })
        RebuildLines()
        RecomputeGrand()
    End Sub

    Private Shared Sub AddLineFromRequestId(requestId As Integer)
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT pr.request_id, pr.product_id, p.name AS pname, p.sku, p.unit_price, " &
                    "p.color_hex, p.image_path, pr.qty_requested, u.full_name AS reqby " &
                    "FROM purchase_requests pr " &
                    "JOIN products p ON pr.product_id = p.product_id " &
                    "LEFT JOIN users u ON pr.requested_by = u.user_id " &
                    "WHERE pr.request_id = @rid LIMIT 1", conn)
                    cmd.Parameters.AddWithValue("@rid", requestId)
                    Using rdr = cmd.ExecuteReader()
                        If rdr.Read() Then
                            AddLineFromRequest(New PendingReqLite With {
                                .RequestId = SafeInt(rdr("request_id")),
                                .ProductId = SafeInt(rdr("product_id")),
                                .ProductName = rdr("pname").ToString(),
                                .Sku = rdr("sku").ToString(),
                                .UnitPrice = If(IsDBNull(rdr("unit_price")), 0D, Convert.ToDecimal(rdr("unit_price"))),
                                .QtyRequested = SafeInt(rdr("qty_requested")),
                                .RequestedBy = If(IsDBNull(rdr("reqby")), "Unknown", rdr("reqby").ToString()),
                                .ImagePath = If(IsDBNull(rdr("image_path")), "", rdr("image_path").ToString()),
                                .ColorHex = If(IsDBNull(rdr("color_hex")), "", rdr("color_hex").ToString())
                            })
                        End If
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not reach the database:" & Environment.NewLine & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    ' ---- Save (single transaction) ----
    Private Shared Sub CommitNewPO()
        If btnSaveRef IsNot Nothing AndAlso CStr(If(btnSaveRef.Tag, "")) = "done" Then Return

        ' Validate
        If cmbSupplierRef Is Nothing OrElse cmbSupplierRef.SelectedItem Is Nothing Then
            AppModal.Show("Please choose a supplier.", "Missing Supplier",
                          MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If
        If newLines.Count = 0 Then
            AppModal.Show("Add at least one line item before saving.", "No Items",
                          MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If
        For Each ln In newLines
            If ln.Qty <= 0 Then
                AppModal.Show("Every line needs a quantity greater than zero." & Environment.NewLine &
                              "Check: " & ln.ProductName, "Invalid Quantity",
                              MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End If
            If ln.UnitCost < 0 Then
                AppModal.Show("Unit cost cannot be negative." & Environment.NewLine &
                              "Check: " & ln.ProductName, "Invalid Cost",
                              MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End If
        Next

        Dim sup As SupItem = TryCast(cmbSupplierRef.SelectedItem, SupItem)
        If sup Is Nothing Then Return

        Dim grand As Decimal = 0D
        For Each ln In newLines
            grand += ln.LineTotal
        Next

        Dim poNo As String = "PO-" & DateTime.Now.ToString("yyMMddHHmmss")
        Dim orderDate As DateTime = dtOrderRef.Value.Date
        Dim expObj As Object = If(dtExpectedRef.Checked,
                                  CType(dtExpectedRef.Value.Date, Object),
                                  CType(DBNull.Value, Object))

        btnSaveRef.Tag = "done"
        Try
            Using conn As MySqlConnection = Db.GetConnection()
                conn.Open()
                Using txn As MySqlTransaction = conn.BeginTransaction()
                    Try
                        Dim poId As Integer = 0
                        Using cmd As New MySqlCommand(
                            "INSERT INTO purchase_orders " &
                            "(po_no, supplier_id, created_by, order_date, expected_date, status, total_cost) " &
                            "VALUES (@no, @sid, @uid, @od, @ed, 'Draft', @total)", conn, txn)
                            cmd.Parameters.AddWithValue("@no", poNo)
                            cmd.Parameters.AddWithValue("@sid", sup.Id)
                            cmd.Parameters.AddWithValue("@uid", Session.CurrentUserId)
                            cmd.Parameters.AddWithValue("@od", orderDate)
                            cmd.Parameters.AddWithValue("@ed", expObj)
                            cmd.Parameters.AddWithValue("@total", grand)
                            cmd.ExecuteNonQuery()
                            poId = CInt(cmd.LastInsertedId)
                        End Using

                        For Each ln In newLines
                            Using cmd As New MySqlCommand(
                                "INSERT INTO po_items (po_id, product_id, qty, unit_cost, line_cost, request_id) " &
                                "VALUES (@po, @pid, @qty, @uc, @lc, @rid)", conn, txn)
                                cmd.Parameters.AddWithValue("@po", poId)
                                cmd.Parameters.AddWithValue("@pid", ln.ProductId)
                                cmd.Parameters.AddWithValue("@qty", ln.Qty)
                                cmd.Parameters.AddWithValue("@uc", Math.Round(ln.UnitCost, 2))
                                cmd.Parameters.AddWithValue("@lc", ln.LineTotal)
                                cmd.Parameters.AddWithValue("@rid",
                                    If(ln.RequestId > 0, CType(ln.RequestId, Object), CType(DBNull.Value, Object)))
                                cmd.ExecuteNonQuery()
                            End Using

                            ' Mark sourced request as Converted
                            If ln.RequestId > 0 Then
                                Using cmd As New MySqlCommand(
                                    "UPDATE purchase_requests SET status = 'Converted', " &
                                    "reviewed_by = @uid, reviewed_at = NOW() " &
                                    "WHERE request_id = @rid AND status = 'Pending'", conn, txn)
                                    cmd.Parameters.AddWithValue("@uid", Session.CurrentUserId)
                                    cmd.Parameters.AddWithValue("@rid", ln.RequestId)
                                    cmd.ExecuteNonQuery()
                                End Using
                            End If
                        Next

                        txn.Commit()
                    Catch exInner As Exception
                        txn.Rollback()
                        If btnSaveRef IsNot Nothing Then btnSaveRef.Tag = ""
                        AppModal.Show("Purchase order was not saved:" & Environment.NewLine & exInner.Message,
                                      "Could Not Save", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                        Return
                    End Try
                End Using
            End Using
        Catch ex As Exception
            If btnSaveRef IsNot Nothing Then btnSaveRef.Tag = ""
            AppModal.Show("Could not reach the database:" & Environment.NewLine & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try

        AppModal.Show("Purchase order " & poNo & " created as Draft." & Environment.NewLine &
                      "Open it to mark it as Ordered.", "Saved",
                      MessageBoxButtons.OK, MessageBoxIcon.Information)
        currentFilter = "All"
        RenderList()
    End Sub

    ' =========================================================================
    '  VIEW 3 — PO DETAIL
    ' =========================================================================
    Private Shared Sub LoadDetail(poId As Integer)
        Dim container As Panel = containerRef
        If container Is Nothing Then Return
        isOnNewPOForm = False

        Dim po As PORow = Nothing
        Dim lines As New List(Of POLine)

        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                "SELECT po.po_no, s.name AS sname, po.order_date, po.expected_date, " &
                "po.status, po.total_cost " &
                "FROM purchase_orders po JOIN suppliers s ON po.supplier_id = s.supplier_id " &
                "WHERE po.po_id = @id LIMIT 1", conn)
                    cmd.Parameters.AddWithValue("@id", poId)
                    Using rdr = cmd.ExecuteReader()
                        If rdr.Read() Then
                            Dim hasExp As Boolean = Not IsDBNull(rdr("expected_date"))
                            po = New PORow With {
                            .PoId = poId,
                            .PoNo = rdr("po_no").ToString(),
                            .SupplierName = rdr("sname").ToString(),
                            .OrderDate = If(IsDBNull(rdr("order_date")), DateTime.Today, Convert.ToDateTime(rdr("order_date"))),
                            .HasExpected = hasExp,
                            .ExpectedDate = If(hasExp, Convert.ToDateTime(rdr("expected_date")), DateTime.MinValue),
                            .Status = rdr("status").ToString(),
                            .TotalCost = If(IsDBNull(rdr("total_cost")), 0D, Convert.ToDecimal(rdr("total_cost")))
                        }
                        End If
                    End Using
                End Using

                If po Is Nothing Then
                    AppModal.Show("That purchase order could not be found.", "Not Found",
                              MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    Return
                End If

                Using cmd As New MySqlCommand(
                                "SELECT pi.product_id, p.name AS pname, p.sku, p.color_hex, p.image_path, pi.qty, pi.unit_cost, pi.line_cost " &
                                "FROM po_items pi JOIN products p ON pi.product_id = p.product_id " &
                                "WHERE pi.po_id = @id ORDER BY p.name ASC", conn)
                    cmd.Parameters.AddWithValue("@id", poId)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            lines.Add(New POLine With {
                            .ProductId = SafeInt(rdr("product_id")),
                            .ProductName = rdr("pname").ToString(),
                            .Sku = rdr("sku").ToString(),
                            .Qty = SafeInt(rdr("qty")),
                            .UnitCost = If(IsDBNull(rdr("unit_cost")), 0D, Convert.ToDecimal(rdr("unit_cost"))),
                            .RequestId = 0,
                            .ColorHex = If(IsDBNull(rdr("color_hex")), "", rdr("color_hex").ToString()),
                            .ImagePath = If(IsDBNull(rdr("image_path")), "", rdr("image_path").ToString())
                        })
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load the purchase order:" & Environment.NewLine & ex.Message,
                      "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try

        container.SuspendLayout()
        container.Controls.Clear()
        container.BackColor = MintBg

        ' ── HEADER BAR  (< Purchase Order  |  Print  |  Action btn) ─────────────
        Dim header As New Panel With {
        .Height = 80, .BackColor = MintBg,
        .Padding = New Padding(0, 0, 24, 0), .Dock = DockStyle.Top
    }

        Dim iconPath As String = IO.Path.Combine(Application.StartupPath, "previous.png")
        Dim picBack As New PictureBox With {
        .Size = New Size(22, 22), .Location = New Point(24, 30),
        .BackColor = Color.Transparent, .SizeMode = PictureBoxSizeMode.Zoom, .Cursor = Cursors.Hand
    }
        If IO.File.Exists(iconPath) Then
            Try
                Dim raw As Image = Image.FromFile(iconPath)
                Dim bmp As New Bitmap(raw.Width, raw.Height)
                Using g As Graphics = Graphics.FromImage(bmp)
                    Dim cm As New Imaging.ColorMatrix(New Single()() {
                    New Single() {0, 0, 0, 0, 0},
                    New Single() {0, 0, 0, 0, 0},
                    New Single() {0, 0, 0, 0, 0},
                    New Single() {0, 0, 0, 1, 0},
                    New Single() {CSng(MintDeep.R / 255.0F), CSng(MintDeep.G / 255.0F), CSng(MintDeep.B / 255.0F), 0, 1}
                })
                    Dim ia As New Imaging.ImageAttributes()
                    ia.SetColorMatrix(cm)
                    g.DrawImage(raw, New Rectangle(0, 0, bmp.Width, bmp.Height),
                            0, 0, raw.Width, raw.Height, GraphicsUnit.Pixel, ia)
                End Using
                picBack.Image = bmp
                raw.Dispose()
            Catch
                picBack.Image = Nothing
            End Try
        End If
        AddHandler picBack.Click, Sub() RenderList()
        AddHandler picBack.MouseEnter, Sub()
                                           picBack.BackColor = Color.FromArgb(30, 0, 130, 109)
                                           picBack.Padding = New Padding(2)
                                       End Sub
        AddHandler picBack.MouseLeave, Sub()
                                           picBack.BackColor = Color.Transparent
                                           picBack.Padding = New Padding(0)
                                       End Sub

        Dim lblTitle As New Label With {
        .Text = "Purchase Order",
        .Font = New Font("Segoe UI", 22, FontStyle.Bold),
        .ForeColor = MintDark, .AutoSize = True,
        .Location = New Point(picBack.Right + 10, 18),
        .BackColor = Color.Transparent
    }
        header.Controls.AddRange({picBack, lblTitle})



        ' ── Action button (Mark as Ordered / Receive Goods) ──────────────────────
        If po.Status = "Draft" Then
            Dim btnOrder As New Button With {
            .Text = "Mark as Ordered", .Size = New Size(180, 46),
            .FlatStyle = FlatStyle.Flat, .BackColor = MintPrimary, .ForeColor = Color.White,
            .Font = New Font("Segoe UI Semibold", 11), .Cursor = Cursors.Hand,
            .TextAlign = ContentAlignment.MiddleCenter,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right, .Tag = ""
        }
            btnOrder.FlatAppearance.BorderSize = 0
            RoundControl(btnOrder, 12)
            AddHover(btnOrder, MintPrimary, MintHover)
            Dim capId As Integer = po.PoId
            AddHandler btnOrder.Click, Sub()
                                           If CStr(If(btnOrder.Tag, "")) = "done" Then Return
                                           MarkOrdered(capId, btnOrder)
                                       End Sub
            header.Controls.Add(btnOrder)
            AddHandler header.Resize, Sub()
                                          btnOrder.Top = 17
                                          btnOrder.Left = header.Width - 24 - btnOrder.Width
                                      End Sub
            btnOrder.Top = 17 : btnOrder.Left = 700

        ElseIf po.Status = "Ordered" OrElse po.Status = "Partially Received" Then
            Dim btnReceive As New Button With {
            .Text = "Receive Goods", .Size = New Size(160, 46),
            .FlatStyle = FlatStyle.Flat, .BackColor = MintDeep, .ForeColor = Color.White,
            .Font = New Font("Segoe UI Semibold", 11), .Cursor = Cursors.Hand,
            .TextAlign = ContentAlignment.MiddleCenter,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
            btnReceive.FlatAppearance.BorderSize = 0
            RoundControl(btnReceive, 12)
            AddHover(btnReceive, MintDeep, DeepHover)
            Dim capId As Integer = po.PoId
            AddHandler btnReceive.Click, Sub() GoToReceive(capId)
            header.Controls.Add(btnReceive)
            AddHandler header.Resize, Sub()
                                          btnReceive.Top = 17
                                          btnReceive.Left = header.Width - 24 - btnReceive.Width
                                      End Sub
            btnReceive.Top = 17 : btnReceive.Left = 700

        End If

        ' ── SCROLLABLE BODY ──────────────────────────────────────────────────────
        Dim body As New Panel With {
            .Dock = DockStyle.Fill, .BackColor = MintBg,
            .Padding = New Padding(24, 12, 24, 24), .AutoScroll = True
        }

        Const cPadTop As Integer = 18        ' card top padding before eyebrow
        Const cIdentH As Integer = 78        ' eyebrow + PO# + supplier
        Const cDivH As Integer = 1
        Const cMetaH As Integer = 62        ' ORDERED/EXPECTED/ITEMS strip
        Const cSecH As Integer = 38        ' "LINE ITEMS" label + its divider
        Const cLineH As Integer = 68        ' height of each line row
        Const cLineGap As Integer = 8        ' gap between line rows
        Const cTotalH As Integer = 64        ' total footer bar
        Const cPadBot As Integer = 18        ' card bottom padding after total bar

        Dim lineBlockH As Integer = If(lines.Count = 0,
            50,  ' empty-state message height
            lines.Count * (cLineH + cLineGap) - cLineGap + 8)

        Dim poCardH As Integer = cPadTop + cIdentH + cDivH + cMetaH + cSecH + lineBlockH + cTotalH + cPadBot

        Dim poCard As New BufferedPanel With {
            .Height = poCardH,
            .BackColor = CardBg,
            .Margin = New Padding(0)
        }
        AddHandler poCard.Paint, Sub(s, e) PaintCard(CType(s, Panel), e)

        ' ── 1. IDENTITY ROW ──────────────────────────────────────────────────────
        Dim lblEyebrow As New Label With {
            .Text = "Purchase Order",
            .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold),
            .ForeColor = FaintText, .AutoSize = True,
            .Location = New Point(22, cPadTop), .BackColor = Color.Transparent
        }
        Dim lblPoNum As New Label With {
            .Text = po.PoNo,
            .Font = New Font("Segoe UI", 19, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True,
            .Location = New Point(20, cPadTop + 16), .BackColor = Color.Transparent
        }
        Dim lblSupName As New Label With {
            .Text = po.SupplierName,
            .Font = New Font("Segoe UI", 11),
            .ForeColor = SubText, .AutoSize = True,
            .Location = New Point(22, cPadTop + 50), .BackColor = Color.Transparent
        }

        ' Status badge — top-right of card
        Dim stI = StatusInfo(po.Status)
        Dim detailBadgeFore As Color = Color.White
        Dim detailBadgeBack As Color
        Select Case po.Status
            Case "Ordered" : detailBadgeBack = MintDeep
            Case "Draft" : detailBadgeBack = Color.FromArgb(115, 130, 126)
            Case "Partially Received" : detailBadgeBack = WarningClr
            Case "Received" : detailBadgeBack = PositiveClr
            Case "Cancelled" : detailBadgeBack = NegativeClr
            Case Else : detailBadgeBack = SubText
        End Select

        Dim cardBadge As New Label With {
            .Text = stI.text,
            .Font = New Font("Segoe UI Semibold", 12.0F, FontStyle.Bold),
            .ForeColor = detailBadgeFore,
            .BackColor = detailBadgeBack,
            .AutoSize = True,
            .Padding = New Padding(16, 8, 16, 8),
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        RoundLabel(cardBadge, 14)
        poCard.Controls.AddRange({lblEyebrow, lblPoNum, lblSupName, cardBadge})

        ' ── 2. IDENTITY / META DIVIDER ───────────────────────────────────────────
        Dim divIdentMeta As New Panel With {
            .Height = 1, .BackColor = RowDivider,
            .Location = New Point(20, cPadTop + cIdentH + 2),
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        }
        poCard.Controls.Add(divIdentMeta)

        ' ── 3. META STRIP  (ORDERED · EXPECTED · ITEMS) ──────────────────────────
        Dim metaTopY As Integer = cPadTop + cIdentH + cDivH + 10
        Dim metaDefs() As (cap As String, val As String) = {
            ("ORDERED", po.OrderDate.ToString("MMM d, yyyy")),
            ("EXPECTED", If(po.HasExpected, po.ExpectedDate.ToString("MMM d, yyyy"), "—")),
            ("ITEMS", lines.Count & If(lines.Count = 1, " item", " items"))
        }
        For mi As Integer = 0 To metaDefs.Length - 1
            Dim mx As Integer = 22 + mi * 148
            Dim mCap As New Label With {
                .Text = metaDefs(mi).cap,
                .Font = New Font("Segoe UI", 8.5F, FontStyle.Bold),
                .ForeColor = FaintText, .AutoSize = True,
                .Location = New Point(mx, metaTopY), .BackColor = Color.Transparent
            }
            Dim mVal As New Label With {
                .Text = metaDefs(mi).val,
                .Font = New Font("Segoe UI", 11, FontStyle.Bold),
                .ForeColor = MintDark, .AutoSize = True,
                .Location = New Point(mx, metaTopY + 16), .BackColor = Color.Transparent
            }
            poCard.Controls.AddRange({mCap, mVal})
        Next

        ' ── 4. SECTION LABEL "LINE ITEMS" + DIVIDER ──────────────────────────────
        Dim lineSecTopY As Integer = metaTopY + cMetaH
        Dim lblLinesSec As New Label With {
            .Text = "LINE ITEMS",
            .Font = New Font("Segoe UI", 8.5F, FontStyle.Bold),
            .ForeColor = FaintText, .AutoSize = True,
            .Location = New Point(22, lineSecTopY), .BackColor = Color.Transparent
        }
        Dim divLineSec As New Panel With {
            .Height = 1, .BackColor = RowDivider,
            .Location = New Point(20, lineSecTopY + 18),
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        }
        poCard.Controls.AddRange({lblLinesSec, divLineSec})

        ' ── 5. LINE ROWS ──────────────────────────────────────────────────────────
        Dim lineStartY As Integer = lineSecTopY + cSecH
        If lines.Count = 0 Then
            Dim lblNoItems As New Label With {
                .Text = "No line items on this purchase order.",
                .Font = New Font("Segoe UI", 10.5F), .ForeColor = SubText,
                .AutoSize = True, .Location = New Point(22, lineStartY + 8),
                .BackColor = Color.Transparent
            }
            poCard.Controls.Add(lblNoItems)
        Else
            Dim yLn As Integer = lineStartY
            For Each ln In lines
                Dim lRow As Panel = BuildUnifiedLineRow(ln, cLineH)
                lRow.Location = New Point(14, yLn)
                lRow.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
                poCard.Controls.Add(lRow)
                Dim rl = TryCast(lRow.Tag, Action)
                If rl IsNot Nothing Then rl()
                yLn += cLineH + cLineGap
            Next
        End If

        ' ── 6. TOTAL FOOTER BAR  (dark-mint gradient inside the card) ─────────────
        Dim totalBarTopY As Integer = poCardH - cTotalH - cPadBot
        Dim totalBar As New BufferedPanel With {
            .Height = cTotalH,
            .Location = New Point(14, totalBarTopY),
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Bottom,
            .BackColor = Color.Transparent
        }
        AddHandler totalBar.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim r As New Rectangle(0, 0, totalBar.Width - 1, totalBar.Height - 1)
                Using path As GraphicsPath = RoundRect(r, 14)
                    Using br As New LinearGradientBrush(r, MintDark, TotalDeep, 90.0F)
                        e.Graphics.FillPath(br, path)
                    End Using
                End Using
            End Sub

        Dim lblTotalCap As New Label With {
            .Text = "TOTAL",
            .Font = New Font("Segoe UI", 13, FontStyle.Bold),
            .ForeColor = Color.FromArgb(190, 235, 218),
            .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblTotalVal As New Label With {
            .Text = Peso & po.TotalCost.ToString("N2"),
            .Font = New Font("Segoe UI", 20, FontStyle.Bold),
            .ForeColor = GoldValue,
            .AutoSize = True, .BackColor = Color.Transparent
        }
        totalBar.Controls.AddRange({lblTotalCap, lblTotalVal})

        Dim layoutTotalBar As Action =
            Sub()
                lblTotalCap.Left = 20
                lblTotalCap.Top = (totalBar.Height - lblTotalCap.Height) \ 2
                Dim vx As Integer = totalBar.Width - lblTotalVal.Width - 20
                If vx < lblTotalCap.Right + 12 Then vx = lblTotalCap.Right + 12
                lblTotalVal.Left = vx
                lblTotalVal.Top = (totalBar.Height - lblTotalVal.Height) \ 2
            End Sub
        AddHandler totalBar.Resize, Sub() layoutTotalBar()
        poCard.Controls.Add(totalBar)

        ' ── CARD RESIZE — stretch dividers, reflow line rows, repin total bar ─────
        AddHandler poCard.Resize,
            Sub()
                ' Badge — top-right
                cardBadge.Left = poCard.Width - cardBadge.Width - 20
                cardBadge.Top = cPadTop + 4

                ' Dividers — full card width minus side padding
                divIdentMeta.Width = poCard.Width - 40
                divLineSec.Width = poCard.Width - 40

                ' Line rows — fill card minus 14px each side
                Dim lrw As Integer = poCard.Width - 28
                For Each c As Control In poCard.Controls
                    If TypeOf c Is Panel AndAlso c.Tag IsNot Nothing AndAlso TypeOf c.Tag Is Action Then
                        c.Left = 14
                        c.Width = lrw
                        Dim relayout = TryCast(c.Tag, Action)
                        If relayout IsNot Nothing Then relayout()
                    End If
                Next

                ' Total bar — fill card minus 14px each side
                totalBar.Left = 14
                totalBar.Width = poCard.Width - 28
                layoutTotalBar()
            End Sub

        ' ── BODY LAYOUT ──────────────────────────────────────────────────────────
        Dim doBodyLayout As Action =
            Sub()
                If body.IsDisposed Then Return
                Dim cw As Integer = body.ClientSize.Width - body.Padding.Horizontal
                poCard.SetBounds(body.Padding.Left, body.Padding.Top, cw, poCardH)
            End Sub

        body.Controls.Add(poCard)
        AddHandler body.Resize, Sub() If Not body.IsDisposed Then doBodyLayout()

        container.Controls.Add(body)
        container.Controls.Add(header)
        container.ResumeLayout()

        body.BeginInvoke(Sub() doBodyLayout())
    End Sub

    Private Shared Function BuildUnifiedLineRow(ln As POLine, height As Integer) As Panel
        Dim row As New BufferedPanel With {.Height = height, .BackColor = Color.Transparent}

        AddHandler row.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect As New Rectangle(0, 0, row.Width - 1, row.Height - 1)
                Using path As GraphicsPath = RoundRect(rect, 10)
                    Using b As New SolidBrush(TileBg) : e.Graphics.FillPath(b, path) : End Using
                    Using pen As New Pen(Color.FromArgb(200, 228, 218), 1.0F) : e.Graphics.DrawPath(pen, path) : End Using
                End Using
                PaintLineThumb(e.Graphics, New Rectangle(12, (row.Height - 48) \ 2, 48, 48), ln.ColorHex, ln.ImagePath, ln.ProductName)
            End Sub

        Dim lblName As New Label With {
            .Text = ln.ProductName,
            .Font = New Font("Segoe UI", 11.5F, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = False,
            .TextAlign = ContentAlignment.MiddleLeft,
            .BackColor = Color.Transparent,
            .Location = New Point(62, 6), .Size = New Size(220, 22)
        }
        Dim lblSku As New Label With {
            .Text = ln.Sku,
            .Font = New Font("Segoe UI", 9),
            .ForeColor = FaintText, .AutoSize = True,
            .BackColor = Color.Transparent,
            .Location = New Point(62, 30)
        }

        ' Right top — "qty × ₱unit"
        Dim lblQtyUnit As New Label With {
            .Text = ln.Qty & " " & ChrW(215) & " " & Peso & ln.UnitCost.ToString("N2"),
            .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = SubText, .AutoSize = True,
            .BackColor = Color.Transparent,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }

        ' Right bottom — line total in gold (mirrors the TOTAL bar)

        Dim lblLineTotal As New Label With {
            .Text = Peso & ln.LineTotal.ToString("N2"),
            .Font = New Font("Segoe UI", 13, FontStyle.Bold),
            .ForeColor = MintDeep, .AutoSize = True,
            .BackColor = Color.Transparent,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }

        row.Controls.AddRange({lblName, lblSku, lblQtyUnit, lblLineTotal})

        Dim layoutR As Action =
            Sub()
                If row.Width < 80 Then Return
                Const rPad As Integer = 14
                ' Line total — lower half, far right
                lblLineTotal.Left = row.Width - lblLineTotal.Width - rPad
                lblLineTotal.Top = (height \ 2) + 2
                ' Qty × unit — upper half, far right, aligned with the total
                lblQtyUnit.Left = row.Width - lblQtyUnit.Width - rPad
                lblQtyUnit.Top = (height \ 2) - lblQtyUnit.Height - 1
                ' Name width — stops before the right column
                Dim rightStop As Integer = Math.Min(lblQtyUnit.Left, lblLineTotal.Left) - 12
                lblName.Width = Math.Max(60, rightStop - 62)
            End Sub

        AddHandler row.Resize, Sub() layoutR()
        AddHandler row.SizeChanged, Sub() layoutR()
        row.Tag = layoutR
        AddHandler row.HandleCreated,
            Sub()
                row.BeginInvoke(Sub()
                                    layoutR()
                                    row.Invalidate()
                                End Sub)
            End Sub

        layoutR()
        Return row
    End Function


    ' =========================================================================
    '  PRINT — builds a PrintDocument from the PO data and shows the dialog
    ' =========================================================================
    Private Shared Sub PrintPO(po As PORow, lines As List(Of POLine))
        Dim pd As New Printing.PrintDocument()
        pd.DocumentName = po.PoNo

        AddHandler pd.PrintPage,
        Sub(sender As Object, e As Printing.PrintPageEventArgs)
            Dim g As Graphics = e.Graphics
            Dim x As Integer = e.MarginBounds.Left
            Dim y As Integer = e.MarginBounds.Top
            Dim pageW As Integer = e.MarginBounds.Width

            ' ── Fonts ────────────────────────────────────────────────────────
            Dim fTitle As New Font("Segoe UI", 18, FontStyle.Bold)
            Dim fSub As New Font("Segoe UI", 10)
            Dim fCaption As New Font("Segoe UI", 8, FontStyle.Bold)
            Dim fMeta As New Font("Segoe UI", 10, FontStyle.Bold)
            Dim fSectionHead As New Font("Segoe UI", 11, FontStyle.Bold)
            Dim fRowNormal As New Font("Segoe UI", 10)
            Dim fRowBold As New Font("Segoe UI", 10, FontStyle.Bold)
            Dim fTotal As New Font("Segoe UI", 13, FontStyle.Bold)

            ' ── Brushes ──────────────────────────────────────────────────────
            Dim brDark As New SolidBrush(Color.FromArgb(10, 79, 66))
            Dim brSub As New SolidBrush(Color.FromArgb(90, 114, 107))
            Dim brFaint As New SolidBrush(Color.FromArgb(150, 165, 160))
            Dim brMint As New SolidBrush(Color.FromArgb(0, 168, 142))
            Dim brDeep As New SolidBrush(Color.FromArgb(0, 130, 109))
            Dim brWhite As New SolidBrush(Color.White)
            Dim brTile As New SolidBrush(Color.FromArgb(236, 248, 244))
            Dim brTotalBg As New SolidBrush(Color.FromArgb(0, 60, 50))

            ' ── HEADER block ─────────────────────────────────────────────────
            g.DrawString("Paint POS", fSub, brSub, x, y)
            y += 18
            g.DrawString(po.PoNo, fTitle, brDark, x, y)
            y += 32
            g.DrawString(po.SupplierName, fSub, brSub, x, y)
            y += 20

            ' Status pill (text only for print)
            Dim stInfo = StatusInfo(po.Status)
            g.DrawString("Status: " & stInfo.text, fSub, brSub, x, y)
            y += 24

            ' Divider line
            g.DrawLine(New Pen(Color.FromArgb(228, 232, 230), 1), x, y, x + pageW, y)
            y += 12

            ' ── META row ─────────────────────────────────────────────────────
            Dim colW As Integer = pageW \ 3
            Dim metaItems() As (cap As String, val As String) = {
                ("ORDERED", po.OrderDate.ToString("MMM d, yyyy")),
                ("EXPECTED", If(po.HasExpected, po.ExpectedDate.ToString("MMM d, yyyy"), "—")),
                ("ITEMS", lines.Count & If(lines.Count = 1, " item", " items"))
            }
            For i As Integer = 0 To metaItems.Length - 1
                Dim mx As Integer = x + i * colW
                g.DrawString(metaItems(i).cap, fCaption, brFaint, mx, y)
                g.DrawString(metaItems(i).val, fMeta, brDark, mx, y + 13)
            Next
            y += 38

            ' Divider
            g.DrawLine(New Pen(Color.FromArgb(228, 232, 230), 1), x, y, x + pageW, y)
            y += 14

            ' ── LINE ITEMS table ─────────────────────────────────────────────
            g.DrawString("Line Items", fSectionHead, brDark, x, y)
            y += 22

            ' Table header row
            Dim colProduct As Integer = CInt(pageW * 0.45)
            Dim colSku As Integer = CInt(pageW * 0.18)
            Dim colQtyUnit As Integer = CInt(pageW * 0.22)
            Dim colTotal As Integer = pageW - colProduct - colSku - colQtyUnit

            g.FillRectangle(New SolidBrush(Color.FromArgb(236, 248, 244)), x, y, pageW, 20)
            g.DrawString("Product", fCaption, brSub, x + 4, y + 4)
            g.DrawString("SKU", fCaption, brSub, x + colProduct + 4, y + 4)
            g.DrawString("Qty × Unit", fCaption, brSub, x + colProduct + colSku + 4, y + 4)
            Dim totalStr As String = "Total"
            Dim totalSz As SizeF = g.MeasureString(totalStr, fCaption)
            g.DrawString(totalStr, fCaption, brSub,
                         x + pageW - totalSz.Width - 4, y + 4)
            y += 22

            ' Line rows — alternating tile background
            For i As Integer = 0 To lines.Count - 1
                Dim ln = lines(i)
                Dim rowH As Integer = 24
                If i Mod 2 = 0 Then
                    g.FillRectangle(New SolidBrush(Color.FromArgb(248, 252, 250)), x, y, pageW, rowH)
                End If

                g.DrawString(ln.ProductName, fRowBold, brDark, x + 4, y + 4)
                g.DrawString(ln.Sku, fRowNormal, brFaint, x + colProduct + 4, y + 4)
                g.DrawString(ln.Qty & " " & ChrW(215) & " " & Peso & ln.UnitCost.ToString("N2"),
                             fRowNormal, brSub, x + colProduct + colSku + 4, y + 4)

                Dim lineStr As String = Peso & ln.LineTotal.ToString("N2")
                Dim lineSz As SizeF = g.MeasureString(lineStr, fRowBold)
                g.DrawString(lineStr, fRowBold, brDeep,
                             x + pageW - lineSz.Width - 4, y + 4)
                y += rowH
            Next

            ' Bottom line
            g.DrawLine(New Pen(Color.FromArgb(228, 232, 230), 1), x, y, x + pageW, y)
            y += 12

            ' ── TOTAL block ──────────────────────────────────────────────────
            Dim totalBoxH As Integer = 36
            g.FillRectangle(brTotalBg, x, y, pageW, totalBoxH)
            g.DrawString("TOTAL", fSectionHead, New SolidBrush(Color.FromArgb(190, 235, 218)), x + 16, y + 8)
            Dim grandStr As String = Peso & po.TotalCost.ToString("N2")
            Dim grandSz As SizeF = g.MeasureString(grandStr, fTotal)
            g.DrawString(grandStr, fTotal, New SolidBrush(Color.FromArgb(255, 196, 120)),
                         x + pageW - grandSz.Width - 16, y + 6)
            y += totalBoxH + 16

            ' ── Footer ───────────────────────────────────────────────────────
            g.DrawString("Printed " & DateTime.Now.ToString("MMM d, yyyy  h:mm tt") &
                         "   |   Paint POS",
                         fSub, brFaint, x, y)

            ' Dispose fonts
            For Each f As Font In {fTitle, fSub, fCaption, fMeta, fSectionHead, fRowNormal, fRowBold, fTotal}
                f.Dispose()
            Next
            For Each br As SolidBrush In {brDark, brSub, brFaint, brMint, brDeep, brWhite, brTile, brTotalBg}
                br.Dispose()
            Next

            e.HasMorePages = False
        End Sub

        Dim dlg As New PrintDialog With {.Document = pd, .UseEXDialog = True}
        If dlg.ShowDialog() = DialogResult.OK Then
            pd.Print()
        End If
    End Sub

    ' =========================================================================
    '  ACTIONS
    ' =========================================================================
    ' Returns True if the New PO form has work the user would lose on leaving.
    Private Shared Function NewPODirty() As Boolean
        Return isOnNewPOForm AndAlso newLines IsNot Nothing AndAlso newLines.Count > 0
    End Function

    ' Confirms before leaving the New PO form. Returns True if it's OK to leave.
    ' Public so the parent form (sidebar nav) can call it too.
    Public Shared Function ConfirmLeaveNewPO() As Boolean
        If Not NewPODirty() Then
            isOnNewPOForm = False
            Return True
        End If
        If AppModal.Show(
            "You have unsaved items on this purchase order." & Environment.NewLine &
            "Leave without saving? Your changes will be lost.",
            "Discard Purchase Order?",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) = DialogResult.Yes Then
            isOnNewPOForm = False
            Return True
        End If
        Return False
    End Function

    ' Back-chevron handler: guard, then return to the list.
    Private Shared Sub TryLeaveNewPO()
        If ConfirmLeaveNewPO() Then RenderList()
    End Sub

    Private Shared Sub MarkOrdered(poId As Integer, btn As Button)
        If AppModal.Show("Mark this purchase order as Ordered?" & Environment.NewLine &
                         "It will then be available to receive.", "Mark as Ordered",
                         MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then
            Return
        End If
        btn.Tag = "done"
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "UPDATE purchase_orders SET status = 'Ordered' " &
                    "WHERE po_id = @id AND status = 'Draft'", conn)
                    cmd.Parameters.AddWithValue("@id", poId)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        Catch ex As Exception
            btn.Tag = ""
            AppModal.Show("Could not update the purchase order:" & Environment.NewLine & ex.Message,
                          "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try
        LoadDetail(poId)
    End Sub

    Private Shared Sub GoToReceive(poId As Integer)
        Try
            Dim host As Control = If(containerRef, CType(Nothing, Control))
            If host Is Nothing Then Return
            Dim frm = TryCast(host.FindForm(), PROCUREMENT_STAFF)
            If frm Is Nothing Then Return
            frm.OpenReceiveForPO(poId)
        Catch ex As Exception
            AppModal.Show("Could not open Receive Goods:" & Environment.NewLine & ex.Message,
                          "Navigation Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    ' =========================================================================
    '  DATA LOADERS
    ' =========================================================================
    Private Shared Function FilterClause(filter As String) As String
        Select Case filter
            Case "Draft" : Return "po.status = 'Draft'"
            Case "Ordered" : Return "po.status = 'Ordered'"
            Case "Received" : Return "po.status = 'Received'"
            Case "Cancelled" : Return "po.status = 'Cancelled'"
            Case Else : Return "po.status NOT IN ('Cancelled','Received')"   ' All = active
        End Select
    End Function

    Private Shared Function LoadList(filter As String) As List(Of PORow)
        Dim result As New List(Of PORow)
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT po.po_id, po.po_no, s.name AS sname, po.order_date, po.expected_date, " &
                    "po.status, po.total_cost, " &
                    "(SELECT COUNT(*) FROM po_items pi WHERE pi.po_id = po.po_id) AS item_count " &
                    "FROM purchase_orders po JOIN suppliers s ON po.supplier_id = s.supplier_id " &
                    "WHERE " & FilterClause(filter) & " " &
                    "ORDER BY po.order_date DESC, po.po_id DESC", conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            Dim hasExp As Boolean = Not IsDBNull(rdr("expected_date"))
                            result.Add(New PORow With {
                                .PoId = SafeInt(rdr("po_id")),
                                .PoNo = rdr("po_no").ToString(),
                                .SupplierName = rdr("sname").ToString(),
                                .OrderDate = If(IsDBNull(rdr("order_date")), DateTime.Today, Convert.ToDateTime(rdr("order_date"))),
                                .HasExpected = hasExp,
                                .ExpectedDate = If(hasExp, Convert.ToDateTime(rdr("expected_date")), DateTime.MinValue),
                                .Status = rdr("status").ToString(),
                                .TotalCost = If(IsDBNull(rdr("total_cost")), 0D, Convert.ToDecimal(rdr("total_cost"))),
                                .ItemCount = SafeInt(rdr("item_count"))
                            })
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load purchase orders:" & Environment.NewLine & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
        Return result
    End Function

    Private Shared Function LoadSuppliers() As List(Of SupItem)
        Dim result As New List(Of SupItem)
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand("SELECT supplier_id, name FROM suppliers ORDER BY name ASC", conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            result.Add(New SupItem With {.Id = SafeInt(rdr("supplier_id")), .Name = rdr("name").ToString()})
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load suppliers:" & Environment.NewLine & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
        Return result
    End Function

    Private Shared Function LoadProducts() As List(Of ProdItem)
        Dim result As New List(Of ProdItem)
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT product_id, sku, name, unit_price FROM products " &
                    "WHERE is_active = 1 ORDER BY name ASC", conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            result.Add(New ProdItem With {
                                .Id = SafeInt(rdr("product_id")),
                                .Sku = rdr("sku").ToString(),
                                .Name = rdr("name").ToString(),
                                .Price = If(IsDBNull(rdr("unit_price")), 0D, Convert.ToDecimal(rdr("unit_price")))
                            })
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load products:" & Environment.NewLine & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
        Return result
    End Function

    Private Shared Function LoadPendingLite() As List(Of PendingReqLite)
        Dim result As New List(Of PendingReqLite)
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                         "SELECT pr.request_id, pr.product_id, p.name AS pname, p.sku, p.unit_price, " &
                    "p.image_path, p.color_hex, pr.qty_requested, u.full_name AS reqby " &
                    "FROM purchase_requests pr " &
                    "JOIN products p ON pr.product_id = p.product_id " &
                    "LEFT JOIN users u ON pr.requested_by = u.user_id " &
                    "WHERE pr.status = 'Pending' " &
                    "ORDER BY pr.created_at DESC", conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            result.Add(New PendingReqLite With {
                                .RequestId = SafeInt(rdr("request_id")),
                                .ProductId = SafeInt(rdr("product_id")),
                                .ProductName = rdr("pname").ToString(),
                                .Sku = rdr("sku").ToString(),
                                .UnitPrice = If(IsDBNull(rdr("unit_price")), 0D, Convert.ToDecimal(rdr("unit_price"))),
                                .QtyRequested = SafeInt(rdr("qty_requested")),
                                .RequestedBy = If(IsDBNull(rdr("reqby")), "Unknown", rdr("reqby").ToString()),
                              .ImagePath = If(IsDBNull(rdr("image_path")), "", rdr("image_path").ToString()),
                                .ColorHex = If(IsDBNull(rdr("color_hex")), "", rdr("color_hex").ToString())
                            })
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load pending requests:" & Environment.NewLine & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
        Return result
    End Function

    ' =========================================================================
    '  SHARED UI HELPERS
    ' =========================================================================
    Private Shared Function BuildCardHeader(title As String, subtitle As String) As Panel
        Dim header As New Panel With {.Height = 64, .BackColor = Color.Transparent}
        Dim lblTitle As New Label With {
            .Text = title, .Font = New Font("Segoe UI", 13.5F, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(16, 12), .BackColor = Color.Transparent
        }
        Dim lblSub As New Label With {
            .Text = subtitle, .Font = New Font("Segoe UI", 10),
            .ForeColor = FaintText, .AutoSize = True, .Location = New Point(16, 38), .BackColor = Color.Transparent
        }
        header.Controls.AddRange({lblTitle, lblSub})
        Dim divider As New Panel With {.Height = 1, .BackColor = RowDivider, .Location = New Point(16, 62)}
        divider.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        header.Controls.Add(divider)
        AddHandler header.Resize, Sub() divider.Width = header.Width - 32
        Return header
    End Function

    Private Shared Function BuildEmptyCard(title As String, message As String) As Panel
        Dim card As New BufferedPanel With {.Height = 150, .BackColor = CardBg}
        AddHandler card.Paint, Sub(s, e) PaintCard(CType(s, Panel), e)
        Dim lbl As New Label With {
            .Text = title, .Font = New Font("Segoe UI", 14, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(28, 44), .BackColor = Color.Transparent
        }
        Dim sub_ As New Label With {
            .Text = message, .Font = New Font("Segoe UI", 10.5F), .ForeColor = SubText, .AutoSize = True,
            .Location = New Point(30, 76), .BackColor = Color.Transparent
        }
        card.Controls.AddRange({lbl, sub_})
        Return card
    End Function

    ' Status pill colours — spec mapping (covers PO + request statuses).
    Private Shared Function StatusInfo(status As String) As (text As String, fore As Color, back As Color)
        Select Case status
            Case "Draft" : Return ("Draft", FaintText, Color.FromArgb(238, 240, 239))
            Case "Ordered" : Return ("Ordered", MintPrimary, TileBg)
            Case "Partially Received" : Return ("Partial", WarningClr, Color.FromArgb(252, 240, 228))
            Case "Received" : Return ("Received", PositiveClr, Color.FromArgb(232, 245, 233))
            Case "Cancelled" : Return ("Cancelled", NegativeClr, Color.FromArgb(251, 234, 234))
            Case "Pending" : Return ("Pending", WarningClr, Color.FromArgb(252, 240, 228))
            Case "Approved" : Return ("Approved", PositiveClr, Color.FromArgb(232, 245, 233))
            Case "Rejected" : Return ("Rejected", NegativeClr, Color.FromArgb(251, 234, 234))
            Case "Converted" : Return ("Converted", FaintText, Color.FromArgb(238, 240, 239))
            Case Else : Return (status, SubText, TileBg)
        End Select
    End Function

    ' =========================================================================
    '  SHARED PAINT / CONTROL HELPERS  (verbatim from InventoryLowStockPanelBuilder)
    ' =========================================================================
    Private Shared Sub PaintCard(panel As Panel, e As PaintEventArgs)
        PanelRenderer.Draw(panel, e, True)
    End Sub


    Private Shared Function MakeLink(text As String, clr As Color) As Label
        Dim lbl As New Label With {
            .Text = text, .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold),
            .ForeColor = clr, .AutoSize = True, .Cursor = Cursors.Hand, .BackColor = Color.Transparent
        }
        AddHandler lbl.MouseEnter, Sub() lbl.Font = New Font("Segoe UI", 9.5F, FontStyle.Bold Or FontStyle.Underline)
        AddHandler lbl.MouseLeave, Sub() lbl.Font = New Font("Segoe UI", 9.5F, FontStyle.Bold)
        Return lbl
    End Function

    Private Shared Function SafeInt(o As Object) As Integer
        If o Is Nothing OrElse IsDBNull(o) Then Return 0
        Dim n As Integer
        If Integer.TryParse(o.ToString(), n) Then Return n
        Return 0
    End Function

    ' String overloads for editable text boxes.
    Private Shared Function SafeIntStr(s As String) As Integer
        Dim n As Integer
        If Integer.TryParse(s, n) Then Return Math.Max(0, n)
        Return 0
    End Function



    Private Shared Function HexToColor(hex As String, fallback As Color) As Color
        Try
            If Not String.IsNullOrEmpty(hex) AndAlso hex.StartsWith("#") AndAlso hex.Length = 7 Then
                Return ColorTranslator.FromHtml(hex)
            End If
        Catch
        End Try
        Return fallback
    End Function

    Private Shared Function RoundRect(rect As Rectangle, radius As Integer) As GraphicsPath
        Dim r As Integer = radius
        Dim path As New GraphicsPath()
        If r <= 0 Then
            path.AddRectangle(rect)
            path.CloseFigure()
            Return path
        End If
        path.AddArc(rect.X, rect.Y, r, r, 180, 90)
        path.AddArc(rect.Right - r, rect.Y, r, r, 270, 90)
        path.AddArc(rect.Right - r, rect.Bottom - r, r, r, 0, 90)
        path.AddArc(rect.X, rect.Bottom - r, r, r, 90, 90)
        path.CloseFigure()
        Return path
    End Function

    Private Shared Sub RoundControl(ctrl As Control, radius As Integer)
        Dim path As New GraphicsPath()
        Dim r As Integer = radius
        path.AddArc(0, 0, r, r, 180, 90)
        path.AddArc(ctrl.Width - r, 0, r, r, 270, 90)
        path.AddArc(ctrl.Width - r, ctrl.Height - r, r, r, 0, 90)
        path.AddArc(0, ctrl.Height - r, r, r, 90, 90)
        path.CloseFigure()
        ctrl.Region = New Region(path)
    End Sub

    Private Shared Sub RoundLabel(lbl As Label, radius As Integer)
        AddHandler lbl.Resize,
            Sub()
                Dim path As New GraphicsPath()
                Dim r As Integer = radius
                path.AddArc(0, 0, r, r, 180, 90)
                path.AddArc(lbl.Width - r, 0, r, r, 270, 90)
                path.AddArc(lbl.Width - r, lbl.Height - r, r, r, 0, 90)
                path.AddArc(0, lbl.Height - r, r, r, 90, 90)
                path.CloseFigure()
                lbl.Region = New Region(path)
            End Sub
    End Sub



    ' Small uppercase field caption.
    Private Shared Function MakeFieldLabel(text As String) As Label
        Return New Label With {
            .Text = text.ToUpperInvariant(), .Font = New Font("Segoe UI Semibold", 10),
            .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent
        }
    End Function

    ' Themes the dropdown calendar to the mint palette.
    Private Shared Sub ThemeCalendar(dt As DateTimePicker)
        dt.CalendarForeColor = MintDark
        dt.CalendarMonthBackground = CardBg
        dt.CalendarTitleBackColor = MintPrimary
        dt.CalendarTitleForeColor = Color.White
        dt.CalendarTrailingForeColor = FaintText
    End Sub

    ' Hover swap for flat buttons.
    Private Shared Sub AddHover(btn As Button, normal As Color, hover As Color)
        AddHandler btn.MouseEnter, Sub() btn.BackColor = hover
        AddHandler btn.MouseLeave, Sub() btn.BackColor = normal
    End Sub

End Class