Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.IO
Imports System.Linq
Imports System.Windows.Forms
Imports MySql.Data.MySqlClient

' =============================================================================
'  InventoryPutAwayPanelBuilder
'  Put Away screen — Phase 2 of the two-phase goods-receipt flow.
'
'  FLOW
'  ----
'  Phase 1  Procurement → Receive PO
'    Confirms goods arrived from the supplier.
'    Writes stock_movements (Purchase / 'receive').
'    Does NOT touch products.stock_qty.
'
'  Phase 2  Inventory → Put Away  (this screen)
'    Staff physically moves goods from dock to shelf.
'    Writes stock_movements (Purchase / 'putaway').
'    Updates products.stock_qty += shelved_now.
'    Updates po_items.shelved_qty += shelved_now.
'    Remainder stays in the queue until fully shelved.
'
'  PENDING FORMULA
'  ---------------
'    pending = SUM(stock_movements.qty_change  WHERE reference_type='receive'
'                                               AND  reference_id=po_id
'                                               AND  product_id=X)
'            − po_items.shelved_qty
'
'  SCREENS
'  -------
'  1. List — one card per PO that has unshelved stock.
'            Shows total pending units and how many products.
'  2. Form  — open one PO: each line shows product, pending qty, and
'             an editable "shelve now" input capped at pending.
'             Confirm Put Away commits the transaction.
'
'  CHANGES
'  -------
'  - Header now shows previous.png chevron on the left for back navigation.
'  - "Back" button removed from the actions card.
'  - Actions card moved BELOW the items card (bottom of the scrolling body).
'  - "Confirm Stock In" button sits on the RIGHT side of the actions card.
'  - Added "↻ Refresh" button (muted-green pill, matches LowStock pattern).
'  - "N pending" badge on each line row renamed to "N remaining".
' =============================================================================
Public Class InventoryPutAwayPanelBuilder

    ' ── PALETTE  (identical to the other inventory panels) ────────────────────
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
    Private Shared ReadOnly WarningClr As Color = Color.FromArgb(232, 120, 30)
    ' Border for the quantity field — dark enough to read as an editable input.
    Private Shared ReadOnly InputBorder As Color = Color.FromArgb(196, 214, 206)
    Private Const PaintCategory As String = "Paint"

    ' ── DATA MODELS ───────────────────────────────────────────────────────────
    Private Class PutAwayPoRow
        Public PoId As Integer
        Public PoNo As String
        Public SupplierName As String
        Public HasReceivedAt As Boolean
        Public ReceivedAt As DateTime
        Public TotalPending As Integer
        Public LineCount As Integer
    End Class

    Private Class PutAwayLine
        Public PoItemId As Integer
        Public ProductId As Integer
        Public ProductName As String
        Public Sku As String
        Public CategoryName As String
        Public ColorHex As String
        Public ImagePath As String
        Public CurrentStock As Integer
        Public PendingQty As Integer
        Public ShelveNow As Integer

        Public ReadOnly Property IsPaint As Boolean
            Get
                Return String.Equals(CategoryName, PaintCategory, StringComparison.OrdinalIgnoreCase)
            End Get
        End Property
    End Class

    ' ── STATE ─────────────────────────────────────────────────────────────────
    Private Shared containerRef As Panel
    Private Shared putAwayLines As New List(Of PutAwayLine)
    Private Shared currentPoId As Integer
    Private Shared currentPoNo As String
    Private Shared ReadOnly thumbCache As New Dictionary(Of String, Image)

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Shared Sub LoadPutAway(container As Panel)
        containerRef = container
        ImageCache.ClearAndDispose(thumbCache, containerRef)
        RenderList()
    End Sub

    ' =========================================================================
    '  VIEW 1 — LIST  (one card per PO with unshelved stock)
    ' =========================================================================
    Private Shared Sub RenderList()
        Dim container As Panel = containerRef
        If container Is Nothing Then Return

        Dim rows As List(Of PutAwayPoRow) = LoadPendingList()

        container.SuspendLayout()
        container.Controls.Clear()
        container.BackColor = MintBg

        Dim listHost As New Panel With {
            .Dock = DockStyle.Fill, .BackColor = MintBg, .Padding = New Padding(24, 4, 24, 20),
            .AutoScroll = False
        }
        Dim rowsHost As New Panel With {
            .Dock = DockStyle.Fill, .BackColor = Color.Transparent, .AutoScroll = True,
            .Padding = New Padding(0)
        }
        listHost.Controls.Add(rowsHost)

        Dim topRegion As Panel = BuildListTop(rows.Count)
        topRegion.Dock = DockStyle.Top

        container.Controls.Add(listHost)
        container.Controls.Add(topRegion)
        container.ResumeLayout()

        If rows.Count = 0 Then
            Dim empty As Panel = BuildEmptyCard(
                "No Incoming Stock",
                "All incoming stock has been processed." &
                "  Check back after Procurement confirms a new delivery.")
            empty.Location = New Point(0, 16)
            empty.Width = rowsHost.ClientSize.Width - 4
            empty.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
            rowsHost.Controls.Add(empty)
        Else
            Dim y As Integer = 8
            For Each po In rows
                Dim card As Panel = BuildPendingCard(po)
                card.Location = New Point(0, y)
                card.Width = rowsHost.ClientSize.Width - 4
                card.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
                rowsHost.Controls.Add(card)
                y += card.Height + 12
            Next
            AddHandler rowsHost.Resize,
                Sub()
                    Dim w As Integer = rowsHost.ClientSize.Width - 4
                    For Each c As Control In rowsHost.Controls
                        c.Width = w
                    Next
                End Sub
        End If
    End Sub

    Private Shared Function BuildListTop(count As Integer) As Panel
        Dim region As New Panel With {
            .Height = 104, .BackColor = MintBg, .Padding = New Padding(24, 22, 24, 0)
        }
        Dim lblTitle As New Label With {
            .Text = "Incoming Stock", .Font = New Font("Segoe UI", 24, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(24, 14)
        }
        Dim lblSub As New Label With {
            .Text = If(count = 0,
                       "All stock is processed — nothing pending",
                       count & If(count = 1, " order has", " orders have") &
                       " stock waiting to be stocked in"),
            .Font = New Font("Segoe UI", 11), .ForeColor = SubText,
            .AutoSize = True, .Location = New Point(30, 56)
        }

        Dim btnRefresh As New Button With {
            .Text = "↻  Refresh",
            .Size = New Size(100, 34),
            .FlatStyle = FlatStyle.Flat,
            .BackColor = Color.FromArgb(220, 235, 232),
            .ForeColor = SubText,
            .Font = New Font("Segoe UI", 9.5F),
            .Cursor = Cursors.Hand,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        btnRefresh.FlatAppearance.BorderSize = 0
        AddHandler btnRefresh.Paint,
            Sub(s, e)
                Dim bb = CType(s, Button)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect As Rectangle = bb.ClientRectangle
                rect.Inflate(-1, -1)
                Using path = RoundRect(rect, 8)
                    Using br As New SolidBrush(bb.BackColor)
                        e.Graphics.FillPath(br, path)
                    End Using
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
        region.Controls.AddRange({lblTitle, lblSub, btnRefresh})

        Dim PositionRefresh As Action =
            Sub()
                btnRefresh.Top = (region.Height - btnRefresh.Height) \ 2
                btnRefresh.Left = region.Width - btnRefresh.Width - 24
            End Sub
        AddHandler region.Resize, Sub() PositionRefresh()
        PositionRefresh()

        Return region
    End Function

    Private Shared Function BuildPendingCard(po As PutAwayPoRow) As Panel
        Dim cardH As Integer = 120
        Dim card As New BufferedPanel With {.Height = cardH, .BackColor = CardBg, .Cursor = Cursors.Hand}

        AddHandler card.Paint,
            Sub(s, e)
                PaintCard(CType(s, Panel), e)
            End Sub

        Dim lblPo As New Label With {
            .Text = po.PoNo, .Font = New Font("Segoe UI", 16, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True,
            .Location = New Point(22, 18), .BackColor = Color.Transparent
        }
        Dim dateStr As String = If(po.HasReceivedAt,
            "Received " & po.ReceivedAt.ToString("MMM d, yyyy"),
            "Partially received")
        Dim lblMeta As New Label With {
            .Text = po.SupplierName & "   " & ChrW(8226) & "   " & dateStr &
                    "   " & ChrW(8226) & "   " &
                    po.LineCount & If(po.LineCount = 1, " product", " products"),
            .Font = New Font("Segoe UI", 10.5F), .ForeColor = SubText,
            .AutoSize = True, .Location = New Point(24, 52), .BackColor = Color.Transparent
        }
        card.Controls.AddRange({lblPo, lblMeta})

        ' ── Badge — top right ─────────────────────────────────────────────────
        ' Soft tinted chip, not a solid fill — this is a status label, so it must
        ' not compete with the Stock In button for "clickable" affordance.
        Dim badge As New Label With {
            .Text = po.TotalPending & If(po.TotalPending = 1, " unit pending", " units pending"),
            .Font = New Font("Segoe UI Semibold", 10F, FontStyle.Bold),
            .ForeColor = MintDeep, .BackColor = TileBg,
            .AutoSize = True, .Padding = New Padding(12, 5, 12, 5),
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        RoundLabel(badge, 12)
        card.Controls.Add(badge)

        ' ── Stock In pill — bottom right with chevron icon ────────────────────
        Dim vpH As Integer = 38
        Dim vpW As Integer = 118
        ' No Region clip here: Region edges are aliased, so they stair-step the
        ' corners while the Paint handler below draws a smooth anti-aliased pill.
        ' The two shapes disagreed, which is why the resting button read as
        ' square-cornered next to its own rounded hover state. The pill shape is
        ' defined purely by the anti-aliased fill in Paint.
        Dim stockPill As New BufferedPanel With {
            .Size = New Size(vpW, vpH),
            .BackColor = MintDeep,
            .Cursor = Cursors.Hand,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }

        ' Load chevron icon
        Dim chevronImg As Image = Nothing
        Dim chevronPath As String = IO.Path.Combine(Application.StartupPath, "right-chevron.png")
        If IO.File.Exists(chevronPath) Then
            Try
                Dim raw As Image = Image.FromFile(chevronPath)
                Dim bmp As New Bitmap(raw.Width, raw.Height)
                Using g As Graphics = Graphics.FromImage(bmp)
                    Dim cm As New Imaging.ColorMatrix(New Single()() {
                        New Single() {0, 0, 0, 0, 0},
                        New Single() {0, 0, 0, 0, 0},
                        New Single() {0, 0, 0, 0, 0},
                        New Single() {0, 0, 0, 1, 0},
                        New Single() {1, 1, 1, 0, 1}
                    })
                    Dim ia As New Imaging.ImageAttributes()
                    ia.SetColorMatrix(cm)
                    g.DrawImage(raw, New Rectangle(0, 0, bmp.Width, bmp.Height),
                                0, 0, raw.Width, raw.Height, GraphicsUnit.Pixel, ia)
                End Using
                chevronImg = bmp
                raw.Dispose()
            Catch
            End Try
        End If

        AddHandler stockPill.Paint,
            Sub(s, e)
                Dim b As BufferedPanel = CType(s, BufferedPanel)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias

                ' Clear to the parent card colour first — with no Region clip the
                ' panel's own rectangle would otherwise show at the corners.
                e.Graphics.Clear(CardBg)

                Using path As GraphicsPath = RoundRect(New Rectangle(0, 0, b.Width - 1, b.Height - 1), vpH \ 2)
                    Using br As New SolidBrush(b.BackColor)
                        e.Graphics.FillPath(br, path)
                    End Using
                End Using
                ' Label and chevron are laid out as one centred group with a
                ' fixed gap, rather than pinning each to an opposite edge.
                Const ICON_SIZE As Integer = 16
                Const ICON_GAP As Integer = 8

                Using f As New Font("Segoe UI Semibold", 11.5F, FontStyle.Bold),
                      br As New SolidBrush(Color.White)
                    Dim textW As Single = e.Graphics.MeasureString("Stock In", f).Width
                    Dim groupW As Single = textW + ICON_GAP + ICON_SIZE
                    Dim startX As Single = (b.Width - groupW) / 2.0F

                    Dim sf As New StringFormat With {
                        .Alignment = StringAlignment.Near,
                        .LineAlignment = StringAlignment.Center
                    }
                    e.Graphics.DrawString("Stock In", f, br,
                                          New RectangleF(startX, 0, textW + 2, b.Height), sf)

                    Dim iconX As Integer = CInt(startX + textW + ICON_GAP)
                    Dim iconY As Integer = (b.Height - ICON_SIZE) \ 2
                    If chevronImg IsNot Nothing Then
                        e.Graphics.DrawImage(chevronImg,
                                             New Rectangle(iconX, iconY, ICON_SIZE, ICON_SIZE))
                    Else
                        DrawChevron(e.Graphics, iconX + ICON_SIZE \ 2, b.Height / 2.0F)
                    End If
                End Using

            End Sub

        AddHandler stockPill.MouseEnter,
            Sub()
                stockPill.BackColor = MintDark
                stockPill.Invalidate()
            End Sub
        AddHandler stockPill.MouseLeave,
            Sub()
                stockPill.BackColor = MintDeep
                stockPill.Invalidate()
            End Sub
        card.Controls.Add(stockPill)

        Dim capId As Integer = po.PoId
        AddHandler stockPill.Click, Sub() LoadPutAwayForm(capId)
        AddHandler card.Click, Sub() LoadPutAwayForm(capId)
        AddHandler lblPo.Click, Sub() LoadPutAwayForm(capId)
        AddHandler lblMeta.Click, Sub() LoadPutAwayForm(capId)

        AddHandler card.MouseEnter,
            Sub()
                card.BackColor = Color.FromArgb(248, 252, 250)
                card.Invalidate()
            End Sub
        AddHandler card.MouseLeave,
            Sub()
                card.BackColor = CardBg
                card.Invalidate()
            End Sub

        AddHandler card.Resize,
            Sub()
                Const rPad As Integer = 24
                badge.Left = card.Width - badge.Width - rPad
                badge.Top = 20
                stockPill.Left = card.Width - stockPill.Width - rPad
                stockPill.Top = cardH - stockPill.Height - 18
                Dim rightStop As Integer = badge.Left - 20
                lblPo.MaximumSize = New Size(Math.Max(80, rightStop - 22), 30)
                lblMeta.MaximumSize = New Size(Math.Max(80, rightStop - 24), 26)
            End Sub

        Return card
    End Function

    ' =========================================================================
    '  VIEW 2 — PUT AWAY FORM  (shelve one PO's pending lines)
    '
    '  LAYOUT (top → bottom, all Dock = Top so they stack naturally):
    '    1. Header panel  — previous.png chevron + PO title + meta
    '    2. Spacer
    '    3. Info banner card
    '    4. Spacer
    '    5. Lines card    — Items to Stock In
    '    6. Spacer
    '    7. Actions card  — Refresh (left) | Confirm Stock In (right)
    ' =========================================================================
    Private Shared Sub LoadPutAwayForm(poId As Integer)
        Dim container As Panel = containerRef
        If container Is Nothing Then Return

        Dim poNo As String = ""
        Dim supplierName As String = ""
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT po.po_no, s.name AS sname " &
                    "FROM purchase_orders po " &
                    "JOIN suppliers s ON po.supplier_id = s.supplier_id " &
                    "WHERE po.po_id = @id LIMIT 1", conn)
                    cmd.Parameters.AddWithValue("@id", poId)
                    Using rdr = cmd.ExecuteReader()
                        If rdr.Read() Then
                            poNo = rdr("po_no").ToString()
                            supplierName = rdr("sname").ToString()
                        End If
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load the purchase order:" & Environment.NewLine & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try

        If poNo = "" Then
            AppModal.Show("That purchase order could not be found.", "Not Found",
                          MessageBoxButtons.OK, MessageBoxIcon.Warning)
            RenderList()
            Return
        End If

        currentPoId = poId
        currentPoNo = poNo
        putAwayLines = LoadPutAwayLines(poId)

        If putAwayLines.Count = 0 Then
            AppModal.Show("All items in this order have already been stocked in.",
                          "Nothing Pending", MessageBoxButtons.OK, MessageBoxIcon.Information)
            RenderList()
            Return
        End If

        container.SuspendLayout()
        container.Controls.Clear()
        container.BackColor = MintBg

        ' ── Scrolling body ────────────────────────────────────────────────────
        Dim body As New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = MintBg,
            .Padding = New Padding(24, 4, 24, 20),
            .AutoScroll = True
        }



        ' Horizontal breathing room between the card's rounded border and the
        ' rows inside it — see the linesCard.Resize handler below.
        Const CARD_INSET As Integer = 12

        Dim linesHeight As Integer = 72 + putAwayLines.Count * 96 + 72 + 16  ' +72 for confirm bar
        Dim linesCard As New BufferedPanel With {
            .Height = linesHeight, .BackColor = CardBg, .Dock = DockStyle.Top
        }
        AddHandler linesCard.Paint, Sub(s, e) PaintCard(CType(s, Panel), e)

        Dim lblHead As New Label With {
            .Text = "Items to Stock In",
            .Font = New Font("Segoe UI", 13, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True,
            .Location = New Point(20, 14), .BackColor = Color.Transparent
        }
        Dim lblHint As New Label With {
            .Text = "Defaults to stocking in everything pending. Reduce a quantity to only partially stock a product.",
            .Font = New Font("Segoe UI", 9F), .ForeColor = SubText,
            .AutoSize = True, .Location = New Point(21, 41), .BackColor = Color.Transparent
        }
        linesCard.Controls.AddRange({lblHead, lblHint})

        Dim y As Integer = 72
        For Each ln In putAwayLines
            Dim row As Panel = BuildPutAwayLineRow(ln, 96)
            row.Location = New Point(CARD_INSET, y)
            row.Width = linesCard.ClientSize.Width - CARD_INSET * 2
            row.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
            linesCard.Controls.Add(row)
            y += 96
        Next

        ' ── Confirm Stock In button — inside the lines card, below all rows ──
        Dim confirmBarY As Integer = 72 + putAwayLines.Count * 96
        Dim confirmBar As New Panel With {
            .BackColor = Color.Transparent,
            .Location = New Point(CARD_INSET, confirmBarY),
            .Height = 64,
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        }
        AddHandler confirmBar.Paint,
            Sub(s, e)
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 8, 0, CType(s, Panel).Width - 8, 0)
                End Using
            End Sub

        Dim btnConfirm As New Button With {
            .Text = "Confirm Stock In",
            .Size = New Size(186, 44),
            .FlatStyle = FlatStyle.Flat,
            .Cursor = Cursors.Hand,
            .Font = New Font("Segoe UI Semibold", 11, FontStyle.Bold),
            .ForeColor = Color.White,
            .BackColor = MintDeep,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        btnConfirm.FlatAppearance.BorderSize = 0
        btnConfirm.FlatAppearance.MouseOverBackColor = MintDark
        btnConfirm.FlatAppearance.MouseDownBackColor = MintDark
        RoundControl(btnConfirm, 12)
        AddHandler btnConfirm.Click, Sub() CommitPutAway()
        confirmBar.Controls.Add(btnConfirm)

        Dim PositionConfirm As Action =
            Sub()
                btnConfirm.Top = (confirmBar.Height - btnConfirm.Height) \ 2
                btnConfirm.Left = confirmBar.Width - btnConfirm.Width - 8
            End Sub
        AddHandler confirmBar.Resize, Sub() PositionConfirm()
        PositionConfirm()

        linesCard.Controls.Add(confirmBar)

        ' Rows and the confirm bar are inset from the card's client edge on both
        ' sides. The card's rounded border is stroked at its own ClientRectangle,
        ' so a full-bleed child that draws its own border (the qty field) had its
        ' stroke land under that line and looked clipped at the card edge.
        AddHandler linesCard.Resize,
            Sub()
                Dim w As Integer = linesCard.ClientSize.Width - CARD_INSET * 2
                For Each c As Control In linesCard.Controls
                    If TypeOf c Is Panel Then
                        c.Left = CARD_INSET
                        c.Width = w
                    End If
                Next
            End Sub
        body.Controls.Add(linesCard)

        ' ── 4. Spacer before lines card ───────────────────────────────────────
        body.Controls.Add(New Panel With {
            .Height = 12, .BackColor = Color.Transparent, .Dock = DockStyle.Top
        })

        ' ── 3. INFO BANNER ────────────────────────────────────────────────────
        Dim bannerCard As New BufferedPanel With {.Height = 60, .BackColor = TileBg, .Dock = DockStyle.Top}
        AddHandler bannerCard.Paint,
            Sub(s, e)
                Dim b As Panel = CType(s, Panel)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias

                Using path = RoundRect(New Rectangle(0, 0, b.Width - 1, b.Height - 1), 10)
                    Using br As New SolidBrush(TileBg)
                        e.Graphics.FillPath(br, path)
                    End Using
                End Using
            End Sub

        Dim lblBanner As New Label With {
            .Text = "Only units already confirmed by Procurement are shown." &
                    "  Adjust quantities if you are partially shelving — the rest stays in the queue.",
            .Font = New Font("Segoe UI", 9.5F), .ForeColor = MintDark,
            .AutoSize = False, .Dock = DockStyle.Fill, .BackColor = Color.Transparent,
            .Padding = New Padding(22, 0, 18, 0), .TextAlign = ContentAlignment.MiddleLeft
        }
        bannerCard.Controls.Add(lblBanner)
        body.Controls.Add(bannerCard)

        ' ── 2. Spacer below header ────────────────────────────────────────────
        body.Controls.Add(New Panel With {
            .Height = 12, .BackColor = Color.Transparent, .Dock = DockStyle.Top
        })

        Dim header As New Panel With {
            .Height = 80,
            .BackColor = MintBg,
            .Padding = New Padding(24, 18, 24, 0),
            .Dock = DockStyle.Top
        }

        ' Load previous.png for the back chevron
        Dim prevImg As Image = Nothing
        Dim prevPath As String = IO.Path.Combine(Application.StartupPath, "previous.png")
        If IO.File.Exists(prevPath) Then
            Try
                Dim raw As Image = Image.FromFile(prevPath)
                Dim bmp As New Bitmap(raw.Width, raw.Height)
                Using g As Graphics = Graphics.FromImage(bmp)
                    Dim cm As New Imaging.ColorMatrix(New Single()() {
                        New Single() {0, 0, 0, 0, 0},
                        New Single() {0, 0, 0, 0, 0},
                        New Single() {0, 0, 0, 0, 0},
                        New Single() {0, 0, 0, 1, 0},
                        New Single() {CSng(10 / 255.0F), CSng(79 / 255.0F), CSng(66 / 255.0F), 0, 1}
                    })
                    Dim ia As New Imaging.ImageAttributes()
                    ia.SetColorMatrix(cm)
                    g.DrawImage(raw, New Rectangle(0, 0, bmp.Width, bmp.Height),
                                0, 0, raw.Width, raw.Height, GraphicsUnit.Pixel, ia)
                End Using
                prevImg = bmp
                raw.Dispose()
            Catch
            End Try
        End If

        Dim picBack As New PictureBox With {
            .Size = New Size(22, 22),
            .Location = New Point(24, 16),
            .BackColor = Color.Transparent,
            .SizeMode = PictureBoxSizeMode.Zoom,
            .Cursor = Cursors.Hand
        }
        If prevImg IsNot Nothing Then picBack.Image = prevImg
        AddHandler picBack.Click, Sub() RenderList()
        AddHandler picBack.MouseEnter, Sub() picBack.BackColor = Color.FromArgb(30, 0, 130, 109)
        AddHandler picBack.MouseLeave, Sub() picBack.BackColor = Color.Transparent
        header.Controls.Add(picBack)



        Dim lblTitle As New Label With {
            .Text = "Incoming Stock: " & poNo,
            .Font = New Font("Segoe UI", 20, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True,
            .Location = New Point(picBack.Right + 10, 10),
            .BackColor = Color.Transparent
        }
        Dim lblMeta As New Label With {
            .Text = supplierName & "   " & ChrW(8226) & "   " &
                    putAwayLines.Count & If(putAwayLines.Count = 1, " product pending", " products pending"),
            .Font = New Font("Segoe UI", 10), .ForeColor = SubText,
            .AutoSize = True, .Location = New Point(picBack.Right + 12, 44),
            .BackColor = Color.Transparent
        }

        ' Position everything once immediately, then again on resize
        Dim LayoutHeader As Action =
            Sub()
                Dim titleH As Integer = 30   ' approx pixel height of font size 20 bold
                Dim metaH As Integer = 20   ' approx pixel height of font size 10
                Dim gap As Integer = 8    ' gap between title and subtitle
                Dim totalH As Integer = titleH + gap + metaH

                ' Center the title+meta block vertically in the header
                Dim blockTop As Integer = (header.Height - totalH) \ 2

                lblTitle.Top = blockTop
                lblMeta.Top = blockTop + titleH + gap

                ' Align chevron to the vertical center of the title line
                picBack.Top = blockTop + (titleH - picBack.Height) \ 2 + 6
            End Sub

        AddHandler header.Resize, Sub() LayoutHeader()
        header.Controls.AddRange({lblTitle, lblMeta})
        LayoutHeader()

        container.Controls.Add(body)
        container.Controls.Add(header)
        container.ResumeLayout()
    End Sub

    ' =========================================================================
    '  LINE ROW  — "N remaining" badge instead of "N pending"
    ' =========================================================================
    Private Shared Function BuildPutAwayLineRow(ln As PutAwayLine, height As Integer) As Panel
        Dim row As New Panel With {.Height = height, .BackColor = Color.Transparent}
        AddHandler row.Paint,
            Sub(s, e)
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 4, height - 1, row.Width - 4, height - 1)
                End Using
            End Sub

        ' Thumbnail and label block sized to the row rather than left at the
        ' dimensions from the shorter row — both read as undersized otherwise.
        Const THUMB_SIZE As Integer = 54
        Dim thumb As Panel = BuildThumb(ln, THUMB_SIZE)
        thumb.Location = New Point(8, (height - THUMB_SIZE) \ 2)
        row.Controls.Add(thumb)

        Dim lblName As New Label With {
            .Text = ln.ProductName,
            .Font = New Font("Segoe UI", 12F, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True,
            .Location = New Point(76, 26), .BackColor = Color.Transparent
        }
        Dim lblMeta As New Label With {
            .Text = ln.Sku & "   " & ChrW(8226) & "   Current stock: " & ln.CurrentStock,
            .Font = New Font("Segoe UI", 9.5F), .ForeColor = SubText,
            .AutoSize = True, .Location = New Point(76, 50), .BackColor = Color.Transparent
        }
        row.Controls.Add(lblName)
        row.Controls.Add(lblMeta)

        ' ── Pending count — plain text, not a badge ───────────────────────────
        ' This is a quiet reference figure, so it reads as a caption beside the
        ' input it qualifies rather than as a status chip competing with it.
        Dim capLn As PutAwayLine = ln
        ' No Anchor on this or the two controls below: the row itself is
        ' anchored Left|Right inside the card, so on every stretch the anchor
        ' engine re-offsets its children by the width delta *on top of* what
        ' LayoutRight() already computed, and the three drift into each other.
        ' LayoutRight() owns all three positions instead.
        Dim pendBadge As New Label With {
            .Text = ln.PendingQty & " pending",
            .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = SubText,
            .BackColor = Color.Transparent,
            .AutoSize = True
        }
        row.Controls.Add(pendBadge)

        ' ── Stock-in input ────────────────────────────────────────────────────
        ' Sentence-case label in the body text colour: the old tiny grey all-caps
        ' caption read as disabled, and floated away from its own field.
        Dim lblCap As New Label With {
            .Text = "Stock in",
            .Font = New Font("Segoe UI", 9F, FontStyle.Bold),
            .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent
        }

        ' The textbox is hosted in a rounded panel — a bare FixedSingle box drew a
        ' hard square rectangle that clashed with every other control on screen.
        Dim qtyHost As New BufferedPanel With {
            .Size = New Size(92, 42),
            .BackColor = CardBg
        }
        AddHandler qtyHost.Paint,
            Sub(s, e)
                ' Clear to the card colour first: the panel's own BackColor fills
                ' the full square before Paint runs, so painting the pill straight
                ' on top left white in the corners and the field looked square.
                e.Graphics.Clear(CardBg)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias

                ' Inset by 1px on every side: a 1.4px pen straddles the path by
                ' 0.7px, so drawing on the panel's outermost pixels clipped the
                ' top and right edges of the stroke and the border looked broken.
                Using path = RoundRect(New Rectangle(1, 1, qtyHost.Width - 3, qtyHost.Height - 3), 10)
                    Using br As New SolidBrush(Color.White)
                        e.Graphics.FillPath(br, path)
                    End Using
                    Using pen As New Pen(InputBorder, 1.4F)
                        e.Graphics.DrawPath(pen, path)
                    End Using
                End Using
            End Sub

        ' BackColor must match the pill's fill: the textbox is opaque and sits on
        ' top of the painted rounded rect, so any other colour shows as a hard
        ' rectangle cutting across the field.
        Dim txtQty As New TextBox With {
            .Width = 66, .Height = 26,
            .BorderStyle = BorderStyle.None,
            .BackColor = Color.White,
            .Font = New Font("Segoe UI", 14, FontStyle.Bold),
            .ForeColor = MintDark,
            .TextAlign = HorizontalAlignment.Center,
            .Text = ln.ShelveNow.ToString()
        }
        qtyHost.Controls.Add(txtQty)

        ' Centre the textbox immediately AND on resize. qtyHost is given a fixed
        ' Size at construction and nothing ever changes it, so a Resize-only
        ' handler never fired: the textbox stayed at its default (0, 0), where
        ' its opaque rectangle painted over the left and top of the rounded
        ' border and the number sat in the corner instead of the centre.
        Dim CentreQty As Action =
            Sub()
                txtQty.Left = (qtyHost.Width - txtQty.Width) \ 2
                txtQty.Top = (qtyHost.Height - txtQty.Height) \ 2
            End Sub
        AddHandler qtyHost.Resize, Sub() CentreQty()
        CentreQty()
        AddHandler txtQty.TextChanged,
            Sub()
                Dim n As Integer = 0
                If Integer.TryParse(txtQty.Text, n) Then
                    capLn.ShelveNow = Math.Max(0, Math.Min(n, capLn.PendingQty))
                Else
                    capLn.ShelveNow = 0
                End If
            End Sub
        AddHandler txtQty.Leave,
            Sub() txtQty.Text = capLn.ShelveNow.ToString()
        row.Controls.Add(lblCap)
        row.Controls.Add(qtyHost)

        Dim LayoutRight As Action =
            Sub()
                ' Field anchored right; its caption sits directly above it and the
                ' pending figure sits on the field's own vertical centre, so the
                ' three read as one group instead of three floating elements.
                '
                ' The caption block is laid out from a fixed CAP_BLOCK height
                ' rather than from lblCap.Height: the label is AutoSize, so during
                ' early layout passes its Height is still stale and the caption
                ' landed on top of the input box.
                Const CAP_BLOCK As Integer = 18
                Const CAP_GAP As Integer = 4
                Const RIGHT_MARGIN As Integer = 8

                Dim groupH As Integer = CAP_BLOCK + CAP_GAP + qtyHost.Height
                Dim groupTop As Integer = (height - groupH) \ 2

                ' 8px matches the Confirm Stock In button's right margin, so
                ' the field and the button share one right edge. The row is
                ' inset CARD_INSET from the card, which keeps the field's own
                ' 1.4px stroke clear of the card's rounded border — full-bleed
                ' put the two strokes on the same pixel and it read as clipped.
                qtyHost.Left = row.Width - qtyHost.Width - RIGHT_MARGIN
                qtyHost.Top = groupTop + CAP_BLOCK + CAP_GAP

                ' Measure both AutoSize labels rather than reading Width/Height:
                ' during the first layout passes those still hold the default
                ' 100x23, which pushed the caption off-centre and dropped the
                ' pending text onto the top edge of the input box.
                Dim capSz As Size = TextRenderer.MeasureText(lblCap.Text, lblCap.Font)
                Dim pendSz As Size = TextRenderer.MeasureText(pendBadge.Text, pendBadge.Font)

                lblCap.Left = qtyHost.Left + (qtyHost.Width - capSz.Width) \ 2
                lblCap.Top = groupTop + (CAP_BLOCK - capSz.Height) \ 2

                ' Right edge of the pending text is pinned a fixed gap left of
                ' the field, so it can never grow rightwards under the box.
                pendBadge.Left = qtyHost.Left - pendSz.Width - 20
                pendBadge.Top = qtyHost.Top + (qtyHost.Height - pendSz.Height) \ 2

                ' Cap the left-hand text block at whatever space is left before
                ' the pending figure. AutoSize labels otherwise keep growing and
                ' a long product name slides under the pending text and field.
                Dim textRight As Integer = pendBadge.Left - 16
                Dim avail As Integer = Math.Max(80, textRight - 76)
                For Each l As Label In {lblName, lblMeta}
                    l.MaximumSize = New Size(avail, 0)
                Next
            End Sub

        AddHandler row.Resize, Sub() LayoutRight()
        ' AutoSize labels settle their width after the text is applied, so re-run
        ' the layout when either caption resizes or they stay mis-centred.
        AddHandler lblCap.Resize, Sub() LayoutRight()
        AddHandler pendBadge.Resize, Sub() LayoutRight()
        LayoutRight()

        Return row
    End Function

    ' =========================================================================
    '  SAVE — single transaction:
    '    INSERT stock_movements (Purchase / putaway)
    '    UPDATE products.stock_qty += shelved_now
    '    UPDATE po_items.shelved_qty += shelved_now
    ' =========================================================================
    Private Shared Sub CommitPutAway()
        Dim toShelve As New List(Of PutAwayLine)
        For Each ln In putAwayLines
            If ln.ShelveNow < 0 Then
                AppModal.Show("Quantity cannot be negative." & Environment.NewLine &
                              "Check: " & ln.ProductName,
                              "Invalid Quantity", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End If
            If ln.ShelveNow > ln.PendingQty Then
                AppModal.Show("You cannot stock in more than the pending amount." & Environment.NewLine &
                              ln.ProductName & ": pending " & ln.PendingQty &
                              ", entered " & ln.ShelveNow & ".",
                              "Exceeds Pending", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End If
            If ln.ShelveNow > 0 Then toShelve.Add(ln)
        Next

        If toShelve.Count = 0 Then
            AppModal.Show("Enter a quantity on at least one line.",
                          "Nothing to Add", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        Dim linesSummary As String = String.Join(
            Environment.NewLine,
            toShelve.Select(Function(l) "  " & l.ProductName & ": +" & l.ShelveNow))
        Dim confirmText As String =
            "Confirm stock-in for " & currentPoNo & "?" &
            Environment.NewLine & Environment.NewLine &
            linesSummary & Environment.NewLine & Environment.NewLine &
            "These units will be added to stock."
        If AppModal.Show(confirmText, "Confirm Stock In",
                         MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then
            Return
        End If

        Dim note As String = "Stocked in from PO " & currentPoNo
        Try
            Using conn As MySqlConnection = Db.GetConnection()
                conn.Open()
                Using txn As MySqlTransaction = conn.BeginTransaction()
                    Try
                        For Each ln In toShelve
                            ' Lock product row before writing
                            Using cmd As New MySqlCommand(
                                "SELECT stock_qty FROM products WHERE product_id = @pid FOR UPDATE",
                                conn, txn)
                                cmd.Parameters.AddWithValue("@pid", ln.ProductId)
                                cmd.ExecuteScalar()
                            End Using

                            ' Re-check the pending amount against the LOCKED po_items row.
                            ' The guard above this transaction used PendingQty as it was when
                            ' the screen loaded; if another station shelved the same PO in the
                            ' meantime that figure is stale, and trusting it would put away
                            ' more units than were ever received.
                            Dim shelvedNow As Integer = 0
                            Using cmd As New MySqlCommand(
                                "SELECT shelved_qty FROM po_items WHERE po_item_id = @piid FOR UPDATE",
                                conn, txn)
                                cmd.Parameters.AddWithValue("@piid", ln.PoItemId)
                                shelvedNow = Convert.ToInt32(cmd.ExecuteScalar())
                            End Using
                            Dim receivedNow As Integer = 0
                            Using cmd As New MySqlCommand(
                                "SELECT COALESCE(SUM(sm.qty_change), 0) FROM stock_movements sm " &
                                "WHERE sm.reference_id = @poid AND sm.reference_type = 'receive' " &
                                "  AND sm.product_id = @pid AND sm.movement_type = 'Purchase'",
                                conn, txn)
                                cmd.Parameters.AddWithValue("@poid", currentPoId)
                                cmd.Parameters.AddWithValue("@pid", ln.ProductId)
                                receivedNow = Convert.ToInt32(cmd.ExecuteScalar())
                            End Using
                            If ln.ShelveNow > (receivedNow - shelvedNow) Then
                                Throw New Exception(
                                    ln.ProductName & ": only " & (receivedNow - shelvedNow).ToString() &
                                    " unit(s) are still pending put-away (someone else may have " &
                                    "stocked this PO in already). Reopen Put Away to see current figures.")
                            End If

                            ' Record the put-away movement in the audit ledger
                            Using cmd As New MySqlCommand(
                                "INSERT INTO stock_movements " &
                                "(product_id, user_id, movement_type, qty_change, " &
                                " reference_type, reference_id, note, created_at) " &
                                "VALUES (@pid, @uid, 'Purchase', @qc, 'putaway', @poid, @note, NOW())",
                                conn, txn)
                                cmd.Parameters.AddWithValue("@pid", ln.ProductId)
                                cmd.Parameters.AddWithValue("@uid", Session.CurrentUserId)
                                cmd.Parameters.AddWithValue("@qc", ln.ShelveNow)
                                cmd.Parameters.AddWithValue("@poid", currentPoId)
                                cmd.Parameters.AddWithValue("@note", note)
                                cmd.ExecuteNonQuery()
                            End Using

                            ' Add to live stock — the ONLY place put-away touches stock_qty
                            Using cmd As New MySqlCommand(
                                "UPDATE products SET stock_qty = stock_qty + @qc " &
                                "WHERE product_id = @pid", conn, txn)
                                cmd.Parameters.AddWithValue("@qc", ln.ShelveNow)
                                cmd.Parameters.AddWithValue("@pid", ln.ProductId)
                                cmd.ExecuteNonQuery()
                            End Using

                            ' Advance the shelved counter on the PO line
                            Using cmd As New MySqlCommand(
                                "UPDATE po_items SET shelved_qty = shelved_qty + @qc " &
                                "WHERE po_item_id = @piid", conn, txn)
                                cmd.Parameters.AddWithValue("@qc", ln.ShelveNow)
                                cmd.Parameters.AddWithValue("@piid", ln.PoItemId)
                                cmd.ExecuteNonQuery()
                            End Using
                        Next

                        txn.Commit()

                        ' Put-away is the point stock actually enters the shelves,
                        ' so record who did it against which PO.
                        ActivityLog.Record("STOCK_PUTAWAY", "purchase_order", currentPoId,
                            toShelve.Count & If(toShelve.Count = 1, " line", " lines") &
                            ", " & toShelve.Sum(Function(l) l.ShelveNow) & " unit(s) shelved")
                    Catch exInner As Exception
                        txn.Rollback()
                        AppModal.Show("Stock-in was not saved:" & Environment.NewLine & exInner.Message,
                                      "Could Not Save", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                        Return
                    End Try
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not reach the database:" & Environment.NewLine & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try

        Dim total As Integer = toShelve.Sum(Function(l) l.ShelveNow)
        AppModal.Show(total & If(total = 1, " unit", " units") &
                      " added to stock from " & currentPoNo & ".",
                      "Stock Added", MessageBoxButtons.OK, MessageBoxIcon.Information)
        RenderList()
    End Sub

    ' =========================================================================
    '  DATA LOADERS
    ' =========================================================================
    ' Returns POs that have at least one product with received_qty > shelved_qty.
    ' Fallback chevron drawn when right-chevron.png is unavailable. Centred on
    ' (cx, cy) so it drops into the same slot as the bitmap icon.
    Private Shared Sub DrawChevron(g As Graphics, cx As Single, cy As Single)
        Const ARM As Single = 4.5F
        Using pen As New Pen(Color.White, 2.2F)
            pen.LineJoin = LineJoin.Round
            pen.StartCap = LineCap.Round
            pen.EndCap = LineCap.Round
            g.DrawLines(pen, New PointF() {
                New PointF(cx - ARM * 0.6F, cy - ARM),
                New PointF(cx + ARM * 0.6F, cy),
                New PointF(cx - ARM * 0.6F, cy + ARM)
            })
        End Using
    End Sub

    Private Shared Function LoadPendingList() As List(Of PutAwayPoRow)
        Dim result As New List(Of PutAwayPoRow)
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT po.po_id, po.po_no, s.name AS sname, po.received_at, " &
                    "    COUNT(DISTINCT poi.po_item_id)                       AS line_count, " &
                    "    SUM(COALESCE(sm_agg.recv, 0) - poi.shelved_qty)      AS total_pending " &
                    "FROM purchase_orders po " &
                    "JOIN suppliers s ON po.supplier_id = s.supplier_id " &
                    "JOIN po_items poi ON poi.po_id = po.po_id " &
                    "LEFT JOIN ( " &
                    "    SELECT reference_id AS po_id, product_id, SUM(qty_change) AS recv " &
                    "    FROM stock_movements " &
                    "    WHERE reference_type = 'receive' AND movement_type = 'Purchase' " &
                    "    GROUP BY reference_id, product_id " &
                    ") sm_agg ON sm_agg.po_id = po.po_id AND sm_agg.product_id = poi.product_id " &
                    "WHERE po.status IN ('Ordered','Partially Received','Received') " &
                    "GROUP BY po.po_id, po.po_no, s.name, po.received_at " &
                    "HAVING total_pending > 0 " &
                    "ORDER BY po.order_date ASC, po.po_id ASC", conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            Dim hasRec As Boolean = Not IsDBNull(rdr("received_at"))
                            result.Add(New PutAwayPoRow With {
                                .PoId = SafeInt(rdr("po_id")),
                                .PoNo = rdr("po_no").ToString(),
                                .SupplierName = rdr("sname").ToString(),
                                .HasReceivedAt = hasRec,
                                .ReceivedAt = If(hasRec, Convert.ToDateTime(rdr("received_at")), DateTime.MinValue),
                                .TotalPending = SafeInt(rdr("total_pending")),
                                .LineCount = SafeInt(rdr("line_count"))
                            })
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load pending put-aways:" & Environment.NewLine & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
        Return result
    End Function

    ' Returns lines for one PO that still have pending stock (received > shelved).
    Private Shared Function LoadPutAwayLines(poId As Integer) As List(Of PutAwayLine)
        Dim result As New List(Of PutAwayLine)
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT poi.po_item_id, poi.product_id, p.name AS pname, p.sku, " &
                    "    c.name AS cname, p.color_hex, p.image_path, p.stock_qty, " &
                    "    poi.shelved_qty, " &
                    "    COALESCE(( " &
                    "        SELECT SUM(sm.qty_change) FROM stock_movements sm " &
                    "        WHERE sm.reference_id = @poid " &
                    "          AND sm.reference_type = 'receive' " &
                    "          AND sm.product_id = poi.product_id " &
                    "          AND sm.movement_type = 'Purchase' " &
                    "    ), 0) AS received_qty " &
                    "FROM po_items poi " &
                    "JOIN products p  ON poi.product_id  = p.product_id " &
                    "JOIN categories c ON p.category_id  = c.category_id " &
                    "WHERE poi.po_id = @poid AND p.is_active = 1 " &
                    "HAVING received_qty > poi.shelved_qty " &
                    "ORDER BY p.name ASC", conn)
                    cmd.Parameters.AddWithValue("@poid", poId)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            Dim recv As Integer = SafeInt(rdr("received_qty"))
                            Dim shelved As Integer = SafeInt(rdr("shelved_qty"))
                            Dim pending As Integer = recv - shelved
                            result.Add(New PutAwayLine With {
                                .PoItemId = SafeInt(rdr("po_item_id")),
                                .ProductId = SafeInt(rdr("product_id")),
                                .ProductName = rdr("pname").ToString(),
                                .Sku = rdr("sku").ToString(),
                                .CategoryName = rdr("cname").ToString(),
                                .ColorHex = If(IsDBNull(rdr("color_hex")), "", rdr("color_hex").ToString()),
                                .ImagePath = If(IsDBNull(rdr("image_path")), "", rdr("image_path").ToString()),
                                .CurrentStock = SafeInt(rdr("stock_qty")),
                                .PendingQty = pending,
                                .ShelveNow = pending   ' default: shelve everything
                            })
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load put-away lines:" & Environment.NewLine & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
        Return result
    End Function

    ' =========================================================================
    '  THUMBNAIL  (paint = colour chip, product = photo, fallback = initial)
    ' =========================================================================
    Private Shared Function BuildThumb(ln As PutAwayLine, size As Integer) As Panel
        Dim box As New Panel With {.Size = New Size(size, size), .BackColor = Color.Transparent}
        Dim img As Image = If(ln.IsPaint, Nothing, GetThumb(ln.ImagePath, size - 2))
        Dim chip As Color = HexToColor(ln.ColorHex, MintPrimary)
        Dim initial As String = If(String.IsNullOrEmpty(ln.ProductName), "?",
                                   ln.ProductName.Substring(0, 1).ToUpperInvariant())
        Dim capSize As Integer = size
        AddHandler box.Paint,
            Sub(s, e)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim r As New Rectangle(0, 0, capSize - 1, capSize - 1)
                Using path = RoundRect(r, 8)
                    If ln.IsPaint Then
                        Using br As New SolidBrush(chip) : g.FillPath(br, path) : End Using
                    ElseIf img IsNot Nothing Then
                        Dim oldClip = g.Clip
                        g.SetClip(path, CombineMode.Replace)
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic
                        g.DrawImage(img, r)
                        g.Clip = oldClip
                    Else
                        Using br As New SolidBrush(TileBg) : g.FillPath(br, path) : End Using
                        Using f As New Font("Segoe UI", capSize \ 3, FontStyle.Bold)
                            Using br As New SolidBrush(MintPrimary)
                                Dim sz = g.MeasureString(initial, f)
                                g.DrawString(initial, f, br,
                                             (capSize - sz.Width) / 2, (capSize - sz.Height) / 2)
                            End Using
                        End Using
                    End If
                    Using pen As New Pen(Color.FromArgb(213, 221, 217), 1) : g.DrawPath(pen, path) : End Using
                End Using
            End Sub
        Return box
    End Function

    Private Shared Function GetThumb(path As String, maxSize As Integer) As Image
        If String.IsNullOrEmpty(path) Then Return Nothing
        ' image_path may be relative (current) or an absolute path baked to
        ' another machine (older rows); resolve both to something openable.
        path = ImageCache.ToFullPath(path)
        Dim key = path & "|" & maxSize
        If thumbCache.ContainsKey(key) Then Return thumbCache(key)
        Try
            If Not File.Exists(path) Then Return Nothing
            Dim bytes = File.ReadAllBytes(path)
            Using ms As New MemoryStream(bytes)
                Using src As Image = Image.FromStream(ms)
                    Dim scale = Math.Min(maxSize / CSng(src.Width), maxSize / CSng(src.Height))
                    Dim w = Math.Max(1, CInt(src.Width * scale))
                    Dim h = Math.Max(1, CInt(src.Height * scale))
                    Dim bmp As New Bitmap(w, h)
                    Using g = Graphics.FromImage(bmp)
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic
                        g.DrawImage(src, 0, 0, w, h)
                    End Using
                    thumbCache(key) = bmp
                    Return bmp
                End Using
            End Using
        Catch
            Return Nothing
        End Try
    End Function

    ' =========================================================================
    '  SHARED PAINT / CONTROL HELPERS  (verbatim pattern from other panels)
    ' =========================================================================
    Private Shared Sub PaintCard(panel As Panel, e As PaintEventArgs)
        PanelRenderer.Draw(panel, e, True)
    End Sub

    Private Shared Function MakeLink(text As String, clr As Color) As Label
        Dim lbl As New Label With {
            .Text = text, .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold),
            .ForeColor = clr, .AutoSize = True, .Cursor = Cursors.Hand, .BackColor = Color.Transparent
        }
        AddHandler lbl.MouseEnter,
            Sub() lbl.Font = New Font("Segoe UI", 9.5F, FontStyle.Bold Or FontStyle.Underline)
        AddHandler lbl.MouseLeave,
            Sub() lbl.Font = New Font("Segoe UI", 9.5F, FontStyle.Bold)
        Return lbl
    End Function

    Private Shared Function BuildEmptyCard(title As String, message As String) As Panel
        Dim card As New BufferedPanel With {.Height = 150, .BackColor = CardBg}
        AddHandler card.Paint, Sub(s, e) PaintCard(CType(s, Panel), e)
        Dim lbl As New Label With {
            .Text = title, .Font = New Font("Segoe UI", 14, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(28, 44),
            .BackColor = Color.Transparent
        }
        Dim sub_ As New Label With {
            .Text = message, .Font = New Font("Segoe UI", 10.5F), .ForeColor = SubText,
            .AutoSize = True, .Location = New Point(30, 76), .BackColor = Color.Transparent
        }
        card.Controls.AddRange({lbl, sub_})
        Return card
    End Function

    Private Shared Function SafeInt(o As Object) As Integer
        If o Is Nothing OrElse IsDBNull(o) Then Return 0
        Dim n As Integer
        If Integer.TryParse(o.ToString(), n) Then Return n
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

    Private Sub InventoryPutAwayPanelBuilder_Load(sender As Object, e As EventArgs) Handles MyBase.Load

    End Sub
End Class