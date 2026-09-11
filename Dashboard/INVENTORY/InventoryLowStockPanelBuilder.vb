Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.IO
Imports System.Text
Imports System.Windows.Forms
Imports MySql.Data.MySqlClient

' =============================================================================
'  InventoryLowStockPanelBuilder  (fixed)
'
'  CHANGES IN THIS VERSION
'  -----------------------
'  A. REFRESH BUTTON added to BuildTopRegion — matches InventoryHomePanelBuilder
'     and InventoryItemListPanelBuilder exactly:
'       - Muted-green resting state RGB(220,235,232) / SubText label.
'       - MouseEnter → RGB(210,240,235) / MintDark label + Invalidate().
'       - MouseLeave → restores resting colours + Invalidate().
'       - Rounded pill via RoundRect GraphicsPath (no non-standard extension).
'       - Click calls LoadLowStock(containerRef) to reload from the DB.
'       - Positioned: [↻ Refresh]  [Copy order list] — right-aligned.
'       - positionRight lambda updated to lay out both buttons correctly.
'
'  B. MakeKpiCard ByRef parameter removed — it was declared ByRef but never
'     assigned inside the function; the caller immediately overwrote its own
'     variable from card.Tag anyway. The signature now simply omits the unused
'     ByRef argument. All three call sites updated accordingly.
'
'  C. OpenReceive() dead-code fixed — the function existed but was never
'     called from any UI element. A "Receive" link is now added alongside the
'     "Request" pill so inventory staff can jump straight to Stock Adjustment
'     for the flagged item without hunting through the nav.
'
'  ORIGINAL NOTES (unchanged)
'  --------------------------
'  PURPOSE  (deliberately NOT a second Stock Levels)
'  The three inventory screens each own ONE verb:
'     Stock Levels      = MONITOR  (health of everything, colour-coded)
'     Low Stock Alerts  = PLAN     (what to buy, how much, what it's worth)
'     Stock Adjustment  = RECORD   (write down what actually arrived)
'
'  GROUPING SEAM (forward-compatible with a future Supplier module)
'  Change ONE function — GroupKeyFor() — to return supplier name and the whole
'  screen regroups. Nothing else needs to change.
'
'  COST NOTE
'  `products` has no wholesale/cost column. Money figure is "estimated value at
'  selling price" — labelled honestly as VALUE, not COST.
' =============================================================================
Public Class InventoryLowStockPanelBuilder

    ' ── PALETTE (identical to the other inventory panels) ─────────────────────────
    Private Shared ReadOnly MintDark As Color = Theme.Dark
    Private Shared ReadOnly MintPrimary As Color = Theme.Primary
    Private Shared ReadOnly MintDeep As Color = Theme.Deep
    Private Shared ReadOnly MintBg As Color = Theme.PageBg
    Private Shared ReadOnly CardBg As Color = Color.White
    Private Shared ReadOnly CardBorder As Color = Theme.BorderSoft
    Private Shared ReadOnly SubText As Color = Theme.SubText
    Private Shared ReadOnly FaintText As Color = Theme.FaintText
    Private Shared ReadOnly TileBg As Color = Theme.TileBg
    Private Shared ReadOnly RowDivider As Color = Theme.RowDivider
    Private Shared ReadOnly CriticalClr As Color = Theme.Warning
    Private Shared ReadOnly OutClr As Color = Theme.Danger
    Private Shared ReadOnly LowClr As Color = Color.FromArgb(220, 170, 0)

    Private Shared ReadOnly Peso As String = ChrW(8369)
    Private Const PaintCategory As String = "Paint"

    ' Tier keys (lowercase so they never case-collide with the Color constants).
    Private Const TierOut As String = "out"
    Private Const TierCritical As String = "critical"
    Private Const TierLow As String = "low"

    ' ── Table columns — product left, rest right-anchored ──────────────────────
    Private Const ColProductX As Integer = 20
    Private Const ColThumbW As Integer = 50

    Private Const RColActionW As Integer = 180   ' widened: holds "Request" + "Receive" side-by-side
    Private Const RColValueW As Integer = 110
    Private Const RColStatusW As Integer = 90
    Private Const RColQtyW As Integer = 120
    Private Const RColCatW As Integer = 130
    Private Const RColGap As Integer = 20

    Private Shared Function ColX(width As Integer) As (cat As Integer, qty As Integer, status As Integer, value As Integer, action As Integer)
        Dim actionX As Integer = width - RColActionW - 12
        Dim valueX As Integer = actionX - RColValueW - RColGap
        Dim statusX As Integer = valueX - RColStatusW - RColGap
        Dim qtyX As Integer = statusX - RColQtyW - RColGap
        Dim catX As Integer = qtyX - RColCatW - RColGap
        Return (catX, qtyX, statusX, valueX, actionX)
    End Function

    ' Small uppercase column caption.
    Private Shared Function ColCap(text As String) As Label
        Return New Label With {
            .Text = text, .Font = New Font("Segoe UI Semibold", 8.5F),
            .ForeColor = FaintText, .AutoSize = True, .BackColor = Color.Transparent
        }
    End Function

    ' ── PRODUCT MODEL ─────────────────────────────────────────────────────────────
    Private Class AlertItem
        Public Id As Integer
        Public Sku As String
        Public Name As String
        Public CategoryName As String
        Public ColorHex As String
        Public ImagePath As String
        Public UnitPrice As Decimal
        Public StockQty As Integer
        Public ReorderLevel As Integer

        Public ReadOnly Property IsPaint As Boolean
            Get
                Return String.Equals(CategoryName, PaintCategory, StringComparison.OrdinalIgnoreCase)
            End Get
        End Property

        ' Suggested order qty: enough to reach 2× reorder level, never less than 1.
        Public ReadOnly Property SuggestedQty As Integer
            Get
                Dim target As Integer = Math.Max(ReorderLevel * 2, ReorderLevel + 1)
                Dim need As Integer = target - StockQty
                If need < 1 Then need = 1
                Return need
            End Get
        End Property

        ' Units below the reorder level (0 if at/above it but still flagged Low).
        Public ReadOnly Property UnitsShort As Integer
            Get
                Dim s As Integer = ReorderLevel - StockQty
                If s < 0 Then Return 0
                Return s
            End Get
        End Property

        Public ReadOnly Property RestockValue As Decimal
            Get
                Return UnitPrice * SuggestedQty
            End Get
        End Property
    End Class

    ' ── STATE ─────────────────────────────────────────────────────────────────────
    Private Shared containerRef As Panel
    Private Shared items As New List(Of AlertItem)

    ' ── CONTROL REFERENCES ────────────────────────────────────────────────────────
    Private Shared rowsHost As Panel
    Private Shared lblSubtitle As Label
    Private Shared lblKpiItems As Label
    Private Shared lblKpiShort As Label
    Private Shared lblKpiValue As Label

    Private Shared ReadOnly thumbCache As New Dictionary(Of String, Image)

    ' =========================================================================
    '  GROUPING SEAM
    ' =========================================================================
    ' Returns the key items are grouped under. TODAY: category name.
    ' LATER (Supplier module): change this single line to return the supplier
    ' name and the whole screen regroups with no other edits.
    Private Shared Function GroupKeyFor(it As AlertItem) As String
        Return If(String.IsNullOrEmpty(it.CategoryName), "Uncategorized", it.CategoryName)
    End Function

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Shared Sub LoadLowStock(container As Panel)
        containerRef = container
        ImageCache.ClearAndDispose(thumbCache, containerRef)
        LoadData()

        container.SuspendLayout()
        container.Controls.Clear()
        container.BackColor = MintBg

        ' ---- Scrolling list of grouped sections ----
        Dim listHost As New Panel With {
            .Dock = DockStyle.Fill, .BackColor = MintBg, .Padding = New Padding(24, 4, 24, 20)
        }
        rowsHost = New Panel With {
            .Dock = DockStyle.Fill, .BackColor = Color.Transparent, .AutoScroll = True, .Padding = New Padding(0)
        }
        AddHandler rowsHost.Resize, Sub() ReflowWidths()
        listHost.Controls.Add(rowsHost)

        Dim topRegion As Panel = BuildTopRegion()
        topRegion.Dock = DockStyle.Top

        container.Controls.Add(listHost)
        container.Controls.Add(topRegion)
        container.ResumeLayout()

        RenderGroups()
    End Sub

    ' =========================================================================
    '  DATA
    ' =========================================================================
    Private Shared Sub LoadData()
        items.Clear()
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                ' Only items that actually need attention: at or below reorder, or
                ' within 25% above it (the "Low" cushion).
                Using cmd As New MySqlCommand(
                    "SELECT p.product_id, p.sku, p.name, c.name AS cname, p.color_hex, p.image_path, " &
                    " p.unit_price, p.stock_qty, p.reorder_level " &
                    "FROM products p JOIN categories c ON p.category_id = c.category_id " &
                    "WHERE p.is_active = 1 AND p.stock_qty * 100 <= p.reorder_level * 125 " &
                    "ORDER BY c.name ASC, p.stock_qty ASC, p.name ASC", conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            items.Add(New AlertItem With {
                                .Id = SafeInt(rdr("product_id")),
                                .Sku = rdr("sku").ToString(),
                                .Name = rdr("name").ToString(),
                                .CategoryName = rdr("cname").ToString(),
                                .ColorHex = If(IsDBNull(rdr("color_hex")), "", rdr("color_hex").ToString()),
                                .ImagePath = If(IsDBNull(rdr("image_path")), "", rdr("image_path").ToString()),
                                .UnitPrice = If(IsDBNull(rdr("unit_price")), 0D, Convert.ToDecimal(rdr("unit_price"))),
                                .StockQty = SafeInt(rdr("stock_qty")),
                                .ReorderLevel = SafeInt(rdr("reorder_level"))
                            })
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load low-stock items:" & Environment.NewLine & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    ' =========================================================================
    '  HEALTH TIER
    ' =========================================================================
    Private Shared Function TierOf(it As AlertItem) As String
        If it.StockQty <= 0 Then Return TierOut
        If it.StockQty <= it.ReorderLevel Then Return TierCritical
        Return TierLow
    End Function

    Private Shared Function TierRank(tier As String) As Integer
        Select Case tier
            Case TierOut : Return 0
            Case TierCritical : Return 1
            Case Else : Return 2
        End Select
    End Function

    Private Shared Function TierColor(tier As String) As Color
        Select Case tier
            Case TierOut : Return OutClr
            Case TierCritical : Return CriticalClr
            Case Else : Return LowClr
        End Select
    End Function

    Private Shared Function StatusOf(it As AlertItem) As (text As String, fore As Color, back As Color)
        Select Case TierOf(it)
            Case TierOut
                Return ("Out", OutClr, Color.FromArgb(251, 234, 234))
            Case TierCritical
                Return ("Critical", Color.FromArgb(190, 95, 20), Color.FromArgb(252, 240, 228))
            Case Else
                Return ("Low", Color.FromArgb(170, 130, 0), Color.FromArgb(251, 244, 222))
        End Select
    End Function

    ' =========================================================================
    '  TOP REGION  (title + KPIs + Refresh + Copy order list)
    ' =========================================================================
    Private Shared Function BuildTopRegion() As Panel
        Dim region As New Panel With {.Height = 240, .BackColor = MintBg, .Padding = New Padding(24, 22, 24, 0)}

        Dim lblTitle As New Label With {
            .Text = "Low Stock Alerts", .Font = New Font("Segoe UI", 24, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(24, 18)
        }
        lblSubtitle = New Label With {
            .Text = SubtitleText(), .Font = New Font("Segoe UI", 11),
            .ForeColor = SubText, .AutoSize = True, .Location = New Point(26, 58)
        }
        region.Controls.AddRange({lblTitle, lblSubtitle})

        ' ---- Copy order summary button ----
        Dim btnCopy As New Button With {
            .Text = "  Copy order list", .Size = New Size(150, 38),
            .FlatStyle = FlatStyle.Flat, .BackColor = CardBg, .ForeColor = MintDark,
            .Font = New Font("Segoe UI Semibold", 10), .Cursor = Cursors.Hand,
            .TextAlign = ContentAlignment.MiddleCenter,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        btnCopy.FlatAppearance.BorderColor = CardBorder
        RoundControl(btnCopy, 10)
        AddHandler btnCopy.Click, Sub() CopyOrderSummary()
        region.Controls.Add(btnCopy)

        ' ── FIX A: REFRESH BUTTON ────────────────────────────────────────────
        ' Matches InventoryHomePanelBuilder / InventoryItemListPanelBuilder exactly:
        '   • Muted-green resting background  RGB(220,235,232) / SubText label
        '   • MouseEnter  → RGB(210,240,235) / MintDark label
        '   • MouseLeave  → restores resting state
        '   • Rounded pill via RoundRect GraphicsPath (no extension-method dependency)
        '   • Click reloads the screen from the DB
        Dim btnRefresh As New Button With {
            .Text = "↻  Refresh",
            .Size = New Size(100, 38),
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

        AddHandler btnRefresh.Click,
            Sub()
                If containerRef IsNot Nothing Then
                    LoadLowStock(containerRef)
                End If
            End Sub

        ' Apply pill region after size is set.
        RoundControl(btnRefresh, 8)
        region.Controls.Add(btnRefresh)
        ' ─────────────────────────────────────────────────────────────────────

        ' Layout right-to-left: [Copy order list]  [↻ Refresh]
        Dim positionRight As Action =
            Sub()
                btnCopy.Top = 22
                btnCopy.Left = region.Width - 24 - btnCopy.Width

                btnRefresh.Top = 22
                btnRefresh.Left = btnCopy.Left - 10 - btnRefresh.Width
            End Sub
        AddHandler region.Resize, Sub() positionRight()
        positionRight()

        ' ---- KPI row: items to reorder / units short / estimated value ----
        Dim kpiRow As New TableLayoutPanel With {
            .Left = 24, .Top = 96, .Height = 124, .ColumnCount = 3, .RowCount = 1,
            .BackColor = Color.Transparent, .CellBorderStyle = TableLayoutPanelCellBorderStyle.None,
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        }
        For i = 0 To 2
            kpiRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 33.33F))
        Next
        kpiRow.RowStyles.Add(New RowStyle(SizeType.Percent, 100))

        ' FIX B: MakeKpiCard no longer takes a ByRef parameter that was never
        ' assigned internally. The label reference is returned via card.Tag and
        ' extracted by the caller — same mechanism, cleaner signature.
        Dim card1 As Panel = MakeKpiCard("Items To Reorder", "0", MintDark, "Flagged below reorder")
        lblKpiItems = CType(card1.Tag, Label)

        Dim card2 As Panel = MakeKpiCard("Units Short", "0", CriticalClr, "Below reorder level")
        lblKpiShort = CType(card2.Tag, Label)

        Dim card3 As Panel = MakeKpiCard("Estimated Value", Peso & "0.00", MintPrimary, "Suggested order " & ChrW(215) & " price")
        lblKpiValue = CType(card3.Tag, Label)

        card1.Margin = New Padding(0, 0, 6, 0)
        card2.Margin = New Padding(6, 0, 6, 0)
        card3.Margin = New Padding(6, 0, 0, 0)
        kpiRow.Controls.Add(card1, 0, 0)
        kpiRow.Controls.Add(card2, 1, 0)
        kpiRow.Controls.Add(card3, 2, 0)

        AddHandler region.Resize, Sub() kpiRow.Width = region.Width - 48
        kpiRow.Width = region.Width - 48
        region.Controls.Add(kpiRow)

        Return region
    End Function

    ' FIX B: ByRef parameter removed — it was declared but never assigned inside
    ' the function. The caller reads the label back from card.Tag, which works
    ' correctly. Removing ByRef eliminates the misleading signature without
    ' changing any observable behaviour.
    Private Shared Function MakeKpiCard(caption As String, value As String,
                                        valueClr As Color, sub1 As String) As Panel
        Dim card As New BufferedPanel With {.Dock = DockStyle.Fill, .BackColor = CardBg}
        AddHandler card.Paint, Sub(s, e) PaintCard(CType(s, Panel), e)

        Dim lblCap As New Label With {
            .Text = caption, .Font = New Font("Segoe UI", 11),
            .ForeColor = SubText, .AutoSize = True, .Location = New Point(22, 14), .BackColor = Color.Transparent
        }
        Dim lblVal As New Label With {
            .Text = value, .Font = New Font("Segoe UI", 24, FontStyle.Bold),
            .ForeColor = valueClr, .AutoSize = True, .Location = New Point(20, 40), .BackColor = Color.Transparent
        }
        Dim lblSub As New Label With {
            .Text = sub1, .Font = New Font("Segoe UI", 10),
            .ForeColor = SubText, .AutoSize = True, .Location = New Point(22, 88), .BackColor = Color.Transparent
        }
        card.Controls.AddRange({lblCap, lblVal, lblSub})
        card.Tag = lblVal   ' caller reads this to get the live-update reference
        Return card
    End Function

    Private Shared Function SubtitleText() As String
        If items.Count = 0 Then Return "Everything is stocked " & ChrW(8226) & "   Purchase plan"
        Return items.Count.ToString() & If(items.Count = 1, " item needs", " items need") &
               " reordering   " & ChrW(8226) & "   Purchase plan"
    End Function

    Private Shared Sub RefreshKpis()
        Dim count As Integer = items.Count
        Dim short_ As Integer = 0
        Dim value As Decimal = 0D
        For Each it In items
            short_ += it.UnitsShort
            value += it.RestockValue
        Next
        If lblKpiItems IsNot Nothing Then lblKpiItems.Text = count.ToString("N0")
        If lblKpiShort IsNot Nothing Then lblKpiShort.Text = short_.ToString("N0")
        If lblKpiValue IsNot Nothing Then lblKpiValue.Text = Peso & value.ToString("N2")
        If lblSubtitle IsNot Nothing Then lblSubtitle.Text = SubtitleText()
    End Sub

    ' =========================================================================
    '  GROUPED LIST
    ' =========================================================================

    ' The single column-header strip above the table.
    Private Shared Function BuildTableHeader() As Panel
        Dim strip As New Panel With {.Height = 34, .BackColor = Color.Transparent}

        Dim hProduct As Label = ColCap("PRODUCT")
        hProduct.Location = New Point(ColProductX + ColThumbW, 10)
        strip.Controls.Add(hProduct)

        Dim PlaceRightCols As Action =
            Sub()
                Dim toRemove As New List(Of Control)
                For Each ctl As Control In strip.Controls
                    If ctl IsNot hProduct Then toRemove.Add(ctl)
                Next
                For Each ctl As Control In toRemove
                    strip.Controls.Remove(ctl)
                    ctl.Dispose()
                Next

                Dim c = ColX(strip.Width)
                Dim hCat As Label = ColCap("CATEGORY")
                Dim hQty As Label = ColCap("QTY / REORDER")
                Dim hStatus As Label = ColCap("STATUS")
                Dim hValue As Label = ColCap("LINE VALUE")
                Dim hAction As Label = ColCap("ACTIONS")   ' was "REQUEST" — now holds both Request + Receive

                hCat.Location = New Point(c.cat, 10)
                hQty.Location = New Point(c.qty, 10)
                hStatus.Location = New Point(c.status, 10)
                hValue.Location = New Point(c.value, 10)
                hAction.Location = New Point(c.action, 10)
                strip.Controls.AddRange({hCat, hQty, hStatus, hValue, hAction})
            End Sub

        AddHandler strip.Resize, Sub() PlaceRightCols()
        Return strip
    End Function

    Private Shared Sub RenderGroups()
        If rowsHost Is Nothing Then Return
        rowsHost.SuspendLayout()
        rowsHost.Controls.Clear()

        RefreshKpis()

        If items.Count = 0 Then
            Dim ok As Panel = BuildEmptyState()
            ok.Location = New Point(0, 20)
            ok.Width = rowsHost.ClientSize.Width - 4
            ok.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
            rowsHost.Controls.Add(ok)
            rowsHost.ResumeLayout()
            Return
        End If

        ' Flat sort: most urgent first, then category, then lowest stock.
        Dim sorted As New List(Of AlertItem)(items)
        sorted.Sort(
            Function(a, b)
                Dim ra As Integer = TierRank(TierOf(a))
                Dim rb As Integer = TierRank(TierOf(b))
                If ra <> rb Then Return ra.CompareTo(rb)
                Dim cc As Integer = String.Compare(GroupKeyFor(a), GroupKeyFor(b), StringComparison.OrdinalIgnoreCase)
                If cc <> 0 Then Return cc
                Return a.StockQty.CompareTo(b.StockQty)
            End Function)

        Dim width As Integer = rowsHost.ClientSize.Width - 4

        ' One header strip for the whole table.
        Dim headerStrip As Panel = BuildTableHeader()
        headerStrip.Location = New Point(0, 4)
        headerStrip.Width = width
        headerStrip.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        rowsHost.Controls.Add(headerStrip)

        ' Card that holds all the rows.
        ' 78px: at 64 the 42px thumbnail plus the two-line name/SKU stack left
        ' almost no breathing room top or bottom and the row read as cramped.
        Dim rowH As Integer = 78
        Dim card As New BufferedPanel With {
            .Height = items.Count * rowH + 12, .BackColor = CardBg
        }
        AddHandler card.Paint, Sub(s, e) PaintCard(CType(s, Panel), e)
        card.Location = New Point(0, 4 + headerStrip.Height + 6)
        card.Width = width
        card.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top

        Dim y As Integer = 6
        For Each it In sorted
            Dim row As Panel = BuildItemRow(it, rowH)
            row.Location = New Point(0, y)
            row.Width = card.Width
            row.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
            card.Controls.Add(row)
            y += rowH
        Next
        rowsHost.Controls.Add(card)

        rowsHost.ResumeLayout()
    End Sub

    Private Shared Sub ReflowWidths()
        If rowsHost Is Nothing Then Return
        Dim width As Integer = rowsHost.ClientSize.Width - 4
        For Each c As Control In rowsHost.Controls
            c.Width = width
        Next
    End Sub

    Private Shared Function BuildEmptyState() As Panel
        Dim card As New BufferedPanel With {.Height = 160, .BackColor = CardBg}
        AddHandler card.Paint, Sub(s, e) PaintCard(CType(s, Panel), e)
        Dim lbl As New Label With {
            .Text = "All stocked up", .Font = New Font("Segoe UI", 15, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(28, 44), .BackColor = Color.Transparent
        }
        Dim sub_ As New Label With {
            .Text = "No products are at or below their reorder level. Nothing to order right now.",
            .Font = New Font("Segoe UI", 10.5F), .ForeColor = SubText, .AutoSize = True,
            .Location = New Point(30, 76), .BackColor = Color.Transparent
        }
        card.Controls.AddRange({lbl, sub_})
        Return card
    End Function

    Private Shared Function BuildItemRow(it As AlertItem, height As Integer) As Panel
        Dim row As New Panel With {.Height = height, .BackColor = Color.Transparent}
        AddHandler row.Paint,
            Sub(s, e)
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 20, row.Height - 1, row.Width - 20, row.Height - 1)
                End Using
            End Sub
        AddHandler row.MouseEnter, Sub() row.BackColor = Color.FromArgb(247, 251, 249)
        AddHandler row.MouseLeave, Sub() row.BackColor = Color.Transparent

        ' PRODUCT thumbnail + name + SKU
        Const ThumbSize As Integer = 50
        Dim thumb As Panel = BuildThumb(it, ThumbSize)
        thumb.Location = New Point(ColProductX, (height - ThumbSize) \ 2)
        row.Controls.Add(thumb)
        Dim textX As Integer = ColProductX + ThumbSize + 14
        Dim lblName As New Label With {
            .Text = it.Name, .Font = New Font("Segoe UI", 11, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblSku As New Label With {
            .Text = it.Sku, .Font = New Font("Segoe UI", 9),
            .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent
        }
        ' Positioned in SnapCols so the pair centres as one block in the row.
        row.Controls.AddRange({lblName, lblSku})

        ' CATEGORY
        Dim lblCat As New Label With {
            .Text = GroupKeyFor(it), .Font = New Font("Segoe UI", 10),
            .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent
        }
        row.Controls.Add(lblCat)

        ' QTY / REORDER
        Dim lblQty As New Label With {
            .Text = it.StockQty.ToString(), .Font = New Font("Segoe UI", 13, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblReorder As New Label With {
            .Text = "reorder " & it.ReorderLevel, .Font = New Font("Segoe UI", 8.5F),
            .ForeColor = FaintText, .AutoSize = True, .BackColor = Color.Transparent
        }
        row.Controls.AddRange({lblQty, lblReorder})

        ' STATUS badge
        Dim st = StatusOf(it)
        Dim badge As New Label With {
            .Text = st.text, .Font = New Font("Segoe UI", 8, FontStyle.Bold),
            .ForeColor = st.fore, .BackColor = st.back, .AutoSize = True,
            .Padding = New Padding(9, 4, 9, 4)
        }
        RoundLabel(badge, 11)
        row.Controls.Add(badge)

        ' LINE VALUE + suggested order qty
        Dim lblVal As New Label With {
            .Text = Peso & it.RestockValue.ToString("N2"), .Font = New Font("Segoe UI", 11, FontStyle.Bold),
            .ForeColor = MintPrimary, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblOrder As New Label With {
            .Text = "order +" & it.SuggestedQty, .Font = New Font("Segoe UI", 8.5F),
            .ForeColor = FaintText, .AutoSize = True, .BackColor = Color.Transparent
        }
        row.Controls.AddRange({lblVal, lblOrder})

        ' ── FIX C: ACTIONS — "Request" pill + "Receive" link ─────────────────
        ' Previously only the Request pill existed, and OpenReceive() was dead code
        ' (defined but never called from any UI element). Now both actions appear
        ' side-by-side in the ACTIONS column so staff can either raise a purchase
        ' request OR jump directly to Stock Adjustment to log a receipt.

        ' Request pill
        Dim btnRequest As New Label With {
            .Font = New Font("Segoe UI", 9, FontStyle.Bold),
            .AutoSize = False,
            .Size = New Size(80, 26),
            .TextAlign = ContentAlignment.MiddleCenter,
            .Cursor = Cursors.Hand
        }
        If HasPendingRequest(it.Id) Then
            btnRequest.Text = "Requested " & ChrW(10003)
            btnRequest.ForeColor = FaintText
            btnRequest.BackColor = Color.FromArgb(238, 240, 239)
            btnRequest.Cursor = Cursors.Default
        Else
            btnRequest.Text = "Request"
            btnRequest.ForeColor = CriticalClr
            btnRequest.BackColor = Color.FromArgb(252, 235, 235)
            Dim capItem As AlertItem = it
            Dim capBtn As Label = btnRequest
            AddHandler capBtn.Click,
                Sub()
                    If CStr(If(capBtn.Tag, "")) = "done" Then Return
                    RaiseRequest(capItem, capBtn)
                End Sub
            AddHandler capBtn.MouseEnter,
                Sub()
                    If CStr(If(capBtn.Tag, "")) = "done" Then Return
                    capBtn.BackColor = Color.FromArgb(245, 210, 210)
                End Sub
            AddHandler capBtn.MouseLeave,
                Sub()
                    If CStr(If(capBtn.Tag, "")) = "done" Then Return
                    capBtn.BackColor = Color.FromArgb(252, 235, 235)
                End Sub
        End If
        RoundLabel(btnRequest, 10)
        row.Controls.Add(btnRequest)

        ' Receive link — navigates to Stock Adjustment pre-loaded for this product.
        ' This makes OpenReceive() reachable from the UI for the first time.
        Dim capId As Integer = it.Id
        ' Only clickable when units are actually awaiting receipt for this
        ' product; otherwise it stays visible but greyed out, so staff can see
        ' the action exists without being sent to an empty Stock Adjustment.
        Dim pendingIn As Integer = PendingReceiptQty(it.Id)
        Dim btnReceive As Label
        If pendingIn > 0 Then
            btnReceive = MakeLink("Receive", MintPrimary)
            AddHandler btnReceive.Click, Sub() OpenReceive(capId)
        Else
            btnReceive = New Label With {
                .Text = "Receive",
                .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold),
                .ForeColor = FaintText,
                .AutoSize = True,
                .Cursor = Cursors.Default,
                .BackColor = Color.Transparent
            }
        End If
        row.Controls.Add(btnReceive)
        ' ─────────────────────────────────────────────────────────────────────

        ' Column layout — fires on first paint and every resize.
        Dim SnapCols As Action =
            Sub()
                Dim c = ColX(row.Width)

                ' Vertically centre the name/SKU pair as one block.
                Dim nameBlockH As Integer = lblName.Height + 2 + lblSku.Height
                Dim nameTop As Integer = (row.Height - nameBlockH) \ 2
                lblName.Location = New Point(textX, nameTop)
                lblSku.Location = New Point(textX, nameTop + lblName.Height + 2)

                ' Vertically centre two-line qty stack as a unit.
                Dim qtyBlockH As Integer = lblQty.Height + 2 + lblReorder.Height
                Dim qtyTop As Integer = (row.Height - qtyBlockH) \ 2
                lblCat.Location = New Point(c.cat, (row.Height - lblCat.Height) \ 2)
                lblQty.Location = New Point(c.qty, qtyTop)
                lblReorder.Location = New Point(c.qty, qtyTop + lblQty.Height + 2)

                badge.Location = New Point(c.status, (row.Height - badge.Height) \ 2)

                ' Vertically centre two-line value stack as a unit.
                Dim valBlockH As Integer = lblVal.Height + 2 + lblOrder.Height
                Dim valTop As Integer = (row.Height - valBlockH) \ 2
                lblVal.Location = New Point(c.value, valTop)
                lblOrder.Location = New Point(c.value, valTop + lblVal.Height + 2)

                ' Request pill left, Receive link to its right — both vertically centred.
                btnRequest.Location = New Point(c.action, (row.Height - btnRequest.Height) \ 2)
                btnReceive.Location = New Point(btnRequest.Right + 10, (row.Height - btnReceive.Height) \ 2)
            End Sub

        SnapCols()
        AddHandler row.Resize, Sub() SnapCols()

        Return row
    End Function

    ' =========================================================================
    '  ACTIONS
    ' =========================================================================

    ' FIX C: Now called from the "Receive" link in BuildItemRow.
    ' Previously this was dead code — no UI element invoked it.
    Private Shared Sub OpenReceive(productId As Integer)
        Try
            Dim host As Control = If(containerRef, CType(Nothing, Control))
            If host Is Nothing Then Return
            Dim frm = TryCast(host.FindForm(), INVENTORY_STAFF)
            If frm Is Nothing Then Return
            frm.OpenStockAdjustmentFor(productId)
        Catch ex As Exception
            AppModal.Show("Could not open Stock Adjustment:" & Environment.NewLine & ex.Message,
                          "Navigation Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    ' =========================================================================
    '  PURCHASE REQUEST HELPERS
    ' =========================================================================

    ' Units for this product sitting on an ordered PO that have not been
    ' shelved yet. Mirrors the pending calculation in InventoryPutAwayPanelBuilder
    ' so "Receive" is only offered when there is genuinely something to receive.
    Private Shared Function PendingReceiptQty(productId As Integer) As Integer
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT COALESCE(SUM(COALESCE(sm_agg.recv, 0) - poi.shelved_qty), 0) " &
                    "FROM purchase_orders po " &
                    "JOIN po_items poi ON poi.po_id = po.po_id " &
                    "LEFT JOIN ( " &
                    "    SELECT reference_id AS po_id, product_id, SUM(qty_change) AS recv " &
                    "    FROM stock_movements " &
                    "    WHERE reference_type = 'receive' AND movement_type = 'Purchase' " &
                    "    GROUP BY reference_id, product_id " &
                    ") sm_agg ON sm_agg.po_id = po.po_id AND sm_agg.product_id = poi.product_id " &
                    "WHERE po.status IN ('Ordered','Partially Received','Received') " &
                    "  AND poi.product_id = @pid", conn)
                    cmd.Parameters.AddWithValue("@pid", productId)
                    Return Math.Max(0, Convert.ToInt32(cmd.ExecuteScalar()))
                End Using
            End Using
        Catch
            Return 0
        End Try
    End Function

    ' Returns True if a Pending request already exists for this product.
    Private Shared Function HasPendingRequest(productId As Integer) As Boolean
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT COUNT(*) FROM purchase_requests " &
                    "WHERE product_id = @pid AND status = 'Pending'", conn)
                    cmd.Parameters.AddWithValue("@pid", productId)
                    Return Convert.ToInt32(cmd.ExecuteScalar()) > 0
                End Using
            End Using
        Catch
            Return False
        End Try
    End Function

    ' Inserts a purchase_request row and updates the button in place.
    ' The suggested qty is offered as the pre-filled default, but staff can type
    ' any quantity they judge correct — whatever they confirm is what reaches
    ' Procurement in purchase_requests.qty_requested.
    Private Shared Sub RaiseRequest(it As AlertItem, btnRequest As Label)
        Try
            Dim q As String = ChrW(34)
            Dim msg As String =
                "Raise a restock request for " & q & it.Name & q & "?" &
                Environment.NewLine & Environment.NewLine &
                "Recommended: " & it.SuggestedQty & " units" &
                "   (stock " & it.StockQty & ", reorder level " & it.ReorderLevel & ")" &
                Environment.NewLine & Environment.NewLine &
                "Enter the quantity to request. Procurement will review it and " &
                "create a Purchase Order."

            ' Loop until we get a valid number or the user cancels, so a typo
            ' re-opens the prompt with what they typed instead of silently
            ' falling back to the recommendation.
            Dim qty As Integer = 0
            Dim seed As String = it.SuggestedQty.ToString()
            Do
                Dim answer As String = AppModal.Prompt(msg, "Raise Request", seed)
                If answer Is Nothing Then Return   ' cancelled

                answer = answer.Trim()
                If Integer.TryParse(answer, qty) AndAlso qty > 0 Then Exit Do

                seed = answer
                AppModal.Show(
                    "Enter a whole number of units greater than 0.",
                    "Invalid Quantity", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Loop

            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "INSERT INTO purchase_requests " &
                    "(product_id, requested_by, qty_requested, status, note, created_at) " &
                    "VALUES (@pid, @uid, @qty, 'Pending', @note, NOW())", conn)
                    cmd.Parameters.AddWithValue("@pid", it.Id)
                    cmd.Parameters.AddWithValue("@uid", Session.CurrentUserId)
                    cmd.Parameters.AddWithValue("@qty", qty)
                    cmd.Parameters.AddWithValue("@note",
                        "Raised from Low Stock Alerts. Stock: " &
                        it.StockQty & ", Reorder level: " & it.ReorderLevel &
                        ", Recommended: " & it.SuggestedQty &
                        If(qty = it.SuggestedQty, "", " (staff adjusted to " & qty & ")"))
                    cmd.ExecuteNonQuery()
                End Using
            End Using

            ' A purchase request starts the spend approval chain, so its origin
            ' belongs in the trail alongside the approval that follows it.
            ActivityLog.Record("REQUEST_RAISE", "purchase_request", it.Id,
                               it.Name & " — " & qty.ToString("N0") & " units")

            ' Update the pill in place — tag it so click handlers bail early.
            btnRequest.Text = "Requested " & ChrW(10003)
            btnRequest.ForeColor = FaintText
            btnRequest.BackColor = Color.FromArgb(238, 240, 239)
            btnRequest.Cursor = Cursors.Default
            btnRequest.Tag = "done"

            AppModal.Show(
                "Request raised for " & qty & " units." & Environment.NewLine &
                "Procurement has been notified and will review it.",
                "Request Sent", MessageBoxButtons.OK, MessageBoxIcon.Information)

        Catch ex As Exception
            AppModal.Show("Could not raise request:" & Environment.NewLine & ex.Message,
                          "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    ' Builds a plain-text purchase list and copies it to the clipboard.
    Private Shared Sub CopyOrderSummary()
        If items.Count = 0 Then
            AppModal.Show("There's nothing to reorder right now.", "Nothing To Copy",
                          MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If

        Dim groups As New Dictionary(Of String, List(Of AlertItem))
        For Each it In items
            Dim key As String = GroupKeyFor(it)
            If Not groups.ContainsKey(key) Then groups(key) = New List(Of AlertItem)
            groups(key).Add(it)
        Next
        Dim orderedKeys As New List(Of String)(groups.Keys)
        orderedKeys.Sort(Function(a, b) String.Compare(a, b, StringComparison.OrdinalIgnoreCase))

        Dim sb As New StringBuilder()
        sb.AppendLine("PURCHASE PLAN — " & DateTime.Now.ToString("MMM d, yyyy"))
        sb.AppendLine("====================================")
        sb.AppendLine()

        Dim grandUnits As Integer = 0
        Dim grandValue As Decimal = 0D
        For Each key In orderedKeys
            sb.AppendLine(key & ":")
            For Each it In groups(key)
                sb.AppendLine("  - " & it.Name & " (" & it.Sku & ")  x" & it.SuggestedQty &
                              "   " & Peso & it.RestockValue.ToString("N2"))
                grandUnits += it.SuggestedQty
                grandValue += it.RestockValue
            Next
            sb.AppendLine()
        Next
        sb.AppendLine("------------------------------------")
        sb.AppendLine("Total units to order: " & grandUnits)
        sb.AppendLine("Estimated value: " & Peso & grandValue.ToString("N2"))
        sb.AppendLine("(Value at selling price — not supplier cost.)")

        Try
            Clipboard.SetText(sb.ToString())
            AppModal.Show("Order list copied to the clipboard." & Environment.NewLine &
                          "Paste it into an email or message to your supplier.",
                          "Copied", MessageBoxButtons.OK, MessageBoxIcon.Information)
        Catch ex As Exception
            AppModal.Show("Could not access the clipboard:" & Environment.NewLine & ex.Message,
                          "Copy Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End Try
    End Sub

    ' =========================================================================
    '  SHARED PAINT / CONTROL HELPERS
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

    ' Rounded thumbnail: paint = colour chip, other = cached photo, fallback = initial tile.
    Private Shared Function BuildThumb(it As AlertItem, size As Integer) As Panel
        Dim box As New Panel With {.Size = New Size(size, size), .BackColor = Color.Transparent}
        Dim img As Image = If(it.IsPaint, Nothing, GetThumb(it.ImagePath, size - 2))
        Dim chip As Color = HexToColor(it.ColorHex, MintPrimary)
        Dim initial As String = If(String.IsNullOrEmpty(it.Name), "?", it.Name.Substring(0, 1).ToUpperInvariant())
        Dim capSize As Integer = size

        AddHandler box.Paint,
            Sub(s, e)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim r As New Rectangle(0, 0, capSize - 1, capSize - 1)
                Using path = RoundRect(r, 10)
                    If it.IsPaint Then
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
                                g.DrawString(initial, f, br, (capSize - sz.Width) / 2, (capSize - sz.Height) / 2)
                            End Using
                        End Using
                    End If
                    Using pen As New Pen(Color.FromArgb(213, 221, 217), 1) : g.DrawPath(pen, path) : End Using
                End Using
            End Sub
        Return box
    End Function

    ' =========================================================================
    '  MEDIA / THUMBNAIL
    ' =========================================================================
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
    '  SMALL HELPERS
    ' =========================================================================
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

    Private Sub InventoryLowStockPanelBuilder_Load(sender As Object, e As EventArgs) Handles MyBase.Load

    End Sub
End Class