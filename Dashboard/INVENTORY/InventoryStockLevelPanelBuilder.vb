Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.IO
Imports System.Windows.Forms
Imports System.Linq
Imports MySql.Data.MySqlClient

' =============================================================================
'  InventoryStockLevelPanelBuilder
'  Stock Levels screen for Inventory Staff.
'
'  PURPOSE
'  -------
'  A READ-ONLY stock-health monitor. It does NOT add / edit / delete product
'  details (that lives in Item List) and it does NOT change quantities (that
'  lives in Stock Adjustment). Its only job is to surface inventory condition
'  at a glance:
'     1. Summary band  — total units, total inventory value, per-tier counts.
'     2. Health bar    — proportional share of Healthy / Low / Critical / Out.
'     3. Main table    — thumbnail + name/SKU, category, qty, reorder, a visual
'                        stock-level bar, status badge, line value. Sorted most
'                        urgent first (Out -> Critical -> Low -> Healthy, then
'                        lowest qty).
'     4. Filters       — category chips, health-tier KPI-card filters, search.
'
'  DESIGN
'  ------
'  Reuses the exact mint design language, palette, rounded BufferedPanel cards,
'  PanelRenderer.Draw, fonts, spacing and media-chip/thumbnail logic from
'  InventoryItemListPanelBuilder. Honors is_active = 1 so deleted products stay
'  hidden, consistent with the rest of the app.
'
'  4-TIER HEALTH MODEL
'  -------------------
'     Out      : stock_qty <= 0
'     Critical : stock_qty <= reorder_level   (and > 0)
'     Low      : stock_qty > reorder_level but <= reorder_level * 1.25
'     Healthy  : above that
'
'  Each row carries an "Adjust" link that will later open Stock Adjustment
'  pre-loaded with that product. For now it is stubbed to a "coming soon"
'  modal so the wiring is ready but never breaks.
' =============================================================================
Public Class InventoryStockLevelPanelBuilder

    ' ── PALETTE (identical to Item List) ─────────────────────────────────────────
    Private Shared ReadOnly MintDark As Color = Color.FromArgb(10, 79, 66)
    Private Shared ReadOnly MintPrimary As Color = Color.FromArgb(0, 168, 142)
    Private Shared ReadOnly MintDeep As Color = Color.FromArgb(0, 130, 109)
    Private Shared ReadOnly MintBg As Color = Color.FromArgb(244, 248, 246)
    Private Shared ReadOnly CardBg As Color = Color.White
    ' Tint for the table's column-header band — a touch off white so the header
    ' reads as part of the card rather than as a separate strip above it.
    Private Shared ReadOnly HeaderBandBg As Color = Color.FromArgb(248, 251, 249)
    ' Must match the radius PanelRenderer.Draw uses for the card border, so the
    ' header band's top corners sit exactly on that curve.
    Private Const CARD_RADIUS As Integer = 14

    ' Padding on the card that keeps its children clear of the border stroke.
    Private Const CARD_INNER_INSET As Integer = 2

    ' Rules inside the table. Lighter than the frame so rows do not compete with
    ' the card edge, but the same hue family and clearly visible.
    Private Shared ReadOnly TableRule As Color = Color.FromArgb(214, 224, 219)

    ' Side inset shared by every horizontal rule in the table — the header
    ' underline, each row divider, the blank filler rules and the footer rule.
    ' They must all use this or the lines visibly disagree at the card edges.
    Private Const RULE_INSET As Integer = 16
    Private Shared ReadOnly CardBorder As Color = Color.FromArgb(228, 232, 230)
    Private Shared ReadOnly SubText As Color = Color.FromArgb(90, 114, 107)
    Private Shared ReadOnly FaintText As Color = Color.FromArgb(150, 165, 160)
    Private Shared ReadOnly TileBg As Color = Color.FromArgb(236, 248, 244)
    Private Shared ReadOnly MintGreen As Color = Color.FromArgb(0, 160, 106)
    Private Shared ReadOnly AccentYellow As Color = Color.FromArgb(220, 170, 0)
    Private Shared ReadOnly AccentRed As Color = Color.FromArgb(210, 60, 60)
    Private Shared ReadOnly RowDivider As Color = Color.FromArgb(238, 242, 240)

    ' Tier-specific accents used by the health bar, badges and KPI values.
    Private Shared ReadOnly HealthyClr As Color = Color.FromArgb(0, 160, 106)
    Private Shared ReadOnly LowClr As Color = Color.FromArgb(220, 170, 0)
    Private Shared ReadOnly CriticalClr As Color = Color.FromArgb(232, 120, 30)
    Private Shared ReadOnly OutClr As Color = Color.FromArgb(210, 60, 60)
    Private Shared ReadOnly TrackBg As Color = Color.FromArgb(233, 239, 236)

    Private Shared ReadOnly Peso As String = ChrW(8369)
    Private Const PaintCategory As String = "Paint"

    ' Tier keys (kept lowercase so they never collide with the Color constants).
    Private Const TierHealthy As String = "healthy"
    Private Const TierLow As String = "low"
    Private Const TierCritical As String = "critical"
    Private Const TierOut As String = "out"

    ' ── PRODUCT MODEL ────────────────────────────────────────────────────────────
    Private Class StockItem
        Public Id As Integer
        Public Sku As String
        Public Name As String
        Public CategoryId As Integer
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

        Public ReadOnly Property LineValue As Decimal
            Get
                Return UnitPrice * StockQty
            End Get
        End Property
    End Class

    ' ── STATE ────────────────────────────────────────────────────────────────────
    Private Shared items As New List(Of StockItem)
    Private Shared categories As New List(Of (id As Integer, name As String))

    Private Shared activeCategory As String = ""
    Private Shared activeTier As String = ""
    Private Shared searchTerm As String = ""

    ' ── CONTROL REFERENCES ────────────────────────────────────────────────────────
    Private Shared containerRef As Panel
    ' Fixed row pitch — shared by real rows and the blank filler rows painted
    ' beneath them, so the ruling stays continuous down the whole table.
    Private Const ROW_HEIGHT As Integer = 64

    Private Shared rowsHost As Panel
    Private Shared lblSubtitle As Label
    Private Shared lblShowing As Label
    Private Shared searchBox As TextBox
    Private Shared healthBar As Panel
    Private Shared kpiCards As New List(Of (panel As Panel, tier As String, valueLbl As Label))
    Private Shared chipMap As New Dictionary(Of String, Panel)
    Private Shared activeRows As New List(Of Panel)
    Private Shared lblTotalUnits As Label
    Private Shared lblTotalValue As Label

    ' Thumbnail cache so we don't re-decode the same file on every render.
    Private Shared ReadOnly thumbCache As New Dictionary(Of String, Image)

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Shared Sub LoadStockLevels(container As Panel,
                                      Optional categoryFilter As String = "",
                                      Optional tierFilter As String = "")

        containerRef = container
        activeCategory = If(categoryFilter, "")
        activeTier = If(tierFilter, "")
        searchTerm = ""
        kpiCards.Clear()
        chipMap.Clear()
        activeRows.Clear()
        thumbCache.Clear()

        LoadData()

        container.SuspendLayout()
        container.Controls.Clear()
        container.BackColor = MintBg

        ' ---- Main list card (pinned header + scrolling rows) ----
        ' PanelRenderer strokes the rounded border 1px inside the client rect, and
        ' Dock=Fill children cover the whole client rect. Padding of 1 put the
        ' header band on exactly the same pixel as that stroke — and since the
        ' band paints after the card, it covered the frame and left it looking
        ' pale above and beside the column header. 2 clears the stroke entirely.
        Dim listCard As New BufferedPanel With {
            .Dock = DockStyle.Fill, .BackColor = CardBg,
            .Padding = New Padding(CARD_INNER_INSET), .Margin = New Padding(0)
        }
        ' Same renderer the KPI/stat cards use, so the table card's border, corner
        ' radius and drop shadow are identical to every other container.
        AddHandler listCard.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)

        Dim colHead As Panel = BuildColumnHeader()
        colHead.Dock = DockStyle.Top

        ' Zero horizontal padding: rows must line up with the docked column
        ' header, which spans the full card width. AutoScroll stays off so no
        ' scrollbar is ever painted — HookSilentScroll drives it by wheel instead.
        rowsHost = New Panel With {
            .Dock = DockStyle.Fill, .BackColor = Color.Transparent, .AutoScroll = False,
            .Padding = New Padding(0)
        }
        AddHandler rowsHost.Resize, Sub() ReflowRowWidths()
        HookSilentScroll(rowsHost)

        Dim footer As New Panel With {.Height = 34, .Dock = DockStyle.Bottom, .BackColor = Color.Transparent}
        lblShowing = New Label With {
            .Text = "", .Font = New Font("Segoe UI", 9), .ForeColor = FaintText,
            .AutoSize = True, .Location = New Point(20, 9), .BackColor = Color.Transparent
        }
        footer.Controls.Add(lblShowing)
        AddHandler footer.Paint,
            Sub(s, e)
                Using pen As New Pen(TableRule, 1)
                    e.Graphics.DrawLine(pen, RULE_INSET, 0, footer.Width - RULE_INSET, 0)
                End Using
            End Sub

        listCard.Controls.Add(rowsHost)
        listCard.Controls.Add(footer)
        listCard.Controls.Add(colHead)

        Dim listHost As New Panel With {
            .Dock = DockStyle.Fill, .BackColor = MintBg, .Padding = New Padding(24, 8, 24, 20)
        }
        listHost.Controls.Add(listCard)

        Dim topRegion As Panel = BuildTopRegion()
        topRegion.Dock = DockStyle.Top

        container.Controls.Add(listHost)
        container.Controls.Add(topRegion)
        container.ResumeLayout()

        RefreshKpiVisuals()
        RefreshChipVisuals()
        RefreshSummary()
        RenderRows()
    End Sub

    ' =========================================================================
    '  DATA
    ' =========================================================================
    Private Shared Sub LoadData()
        items.Clear()
        categories.Clear()
        Try
            Using conn = Db.GetConnection()
                conn.Open()

                Using cmd As New MySqlCommand("SELECT category_id, name FROM categories ORDER BY name ASC", conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            categories.Add((SafeInt(rdr("category_id")), rdr("name").ToString()))
                        End While
                    End Using
                End Using

                Using cmd As New MySqlCommand(
                    "SELECT p.product_id, p.sku, p.name, p.category_id, c.name AS cname, " &
                    " p.color_hex, p.image_path, p.unit_price, p.stock_qty, p.reorder_level " &
                    "FROM products p JOIN categories c ON p.category_id = c.category_id " &
                    "WHERE p.is_active = 1 ORDER BY p.name ASC", conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            items.Add(New StockItem With {
                                .Id = SafeInt(rdr("product_id")),
                                .Sku = rdr("sku").ToString(),
                                .Name = rdr("name").ToString(),
                                .CategoryId = SafeInt(rdr("category_id")),
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
            AppModal.Show("Could not load stock levels:" & Environment.NewLine & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    ' =========================================================================
    '  HEALTH MODEL
    ' =========================================================================
    ' Returns the tier key for an item using the 4-tier model.
    Private Shared Function TierOf(it As StockItem) As String
        If it.StockQty <= 0 Then Return TierOut
        If it.StockQty <= it.ReorderLevel Then Return TierCritical
        ' "within 25% above" reorder. Compare on integers scaled by 100 so the
        ' 1.25 boundary is exact and we avoid floating-point edge wobble.
        If it.StockQty * 100 <= it.ReorderLevel * 125 Then Return TierLow
        Return TierHealthy
    End Function

    ' Sort weight — most urgent first (Out -> Critical -> Low -> Healthy).
    Private Shared Function TierRank(tier As String) As Integer
        Select Case tier
            Case TierOut : Return 0
            Case TierCritical : Return 1
            Case TierLow : Return 2
            Case Else : Return 3
        End Select
    End Function

    Private Shared Function TierColor(tier As String) As Color
        Select Case tier
            Case TierOut : Return OutClr
            Case TierCritical : Return CriticalClr
            Case TierLow : Return LowClr
            Case Else : Return HealthyClr
        End Select
    End Function

    Private Shared Function TierLabel(tier As String) As String
        Select Case tier
            Case TierOut : Return "Out"
            Case TierCritical : Return "Critical"
            Case TierLow : Return "Low"
            Case Else : Return "Healthy"
        End Select
    End Function

    Private Shared Function CountTier(tier As String) As Integer
        Return items.Where(Function(p) TierOf(p) = tier).Count()
    End Function

    ' =========================================================================
    '  TOP REGION
    ' =========================================================================
    Private Shared Function BuildTopRegion() As Panel
        Dim region As New Panel With {.Height = 430, .BackColor = MintBg, .Padding = New Padding(24, 22, 24, 0)}

        Dim lblTitle As New Label With {
            .Text = "Stock Levels", .Font = New Font("Segoe UI", 24, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(24, 18)
        }
        lblSubtitle = New Label With {
            .Text = SubtitleText(), .Font = New Font("Segoe UI", 11),
            .ForeColor = SubText, .AutoSize = True, .Location = New Point(26, 58)
        }
        region.Controls.AddRange({lblTitle, lblSubtitle})

        ' ---- Search box ----
        Dim searchWrap As New BufferedPanel With {.Size = New Size(230, 38), .BackColor = CardBg}
        AddHandler searchWrap.Paint,
            Sub(s, e)
                Dim p = CType(s, Panel)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Using path = RoundRect(New Rectangle(0, 0, p.Width - 1, p.Height - 1), 10)
                    Using br As New SolidBrush(CardBg)
                        e.Graphics.FillPath(br, path)
                    End Using
                    Using pen As New Pen(CardBorder, 1)
                        e.Graphics.DrawPath(pen, path)
                    End Using
                End Using
            End Sub
        Dim searchIcon As New Panel With {.Size = New Size(14, 14), .Location = New Point(12, 12), .BackColor = Color.Transparent}
        AddHandler searchIcon.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Using pen As New Pen(SubText, 1.6F)
                    e.Graphics.DrawEllipse(pen, 0, 0, 9, 9)
                    e.Graphics.DrawLine(pen, 9, 9, 13, 13)
                End Using
            End Sub
        searchBox = New TextBox With {
            .BorderStyle = BorderStyle.None, .Font = New Font("Segoe UI", 11),
            .ForeColor = MintDark, .BackColor = CardBg, .Location = New Point(32, 9), .Width = 186
        }
        SetCue(searchBox, "Search name or SKU…")
        AddHandler searchBox.TextChanged,
            Sub()
                Dim cue As String = CStr(If(searchBox.Tag, ""))
                searchTerm = If(searchBox.Text = cue, "", searchBox.Text.Trim())
                RenderRows()
            End Sub
        searchWrap.Controls.AddRange({searchIcon, searchBox})
        region.Controls.Add(searchWrap)

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
                    LoadStockLevels(containerRef)
                End If
            End Sub
        region.Controls.Add(btnRefresh)

        Dim PositionTopRight As Action =
            Sub()
                searchWrap.Top = 22
                searchWrap.Left = region.Width - 24 - searchWrap.Width
                btnRefresh.Top = 22
                btnRefresh.Left = searchWrap.Left - btnRefresh.Width - 10
            End Sub
        AddHandler region.Resize, Sub() PositionTopRight()
        PositionTopRight()

        ' ---- SUMMARY BAND (units + value + health bar) ----
        Dim summaryRow As Panel = BuildSummaryBand()
        summaryRow.Left = 24
        summaryRow.Top = 92
        summaryRow.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        AddHandler region.Resize, Sub() summaryRow.Width = region.Width - 48
        summaryRow.Width = region.Width - 48
        region.Controls.Add(summaryRow)

        ' ---- KPI / HEALTH-TIER FILTER ROW ----
        Dim kpiRow As TableLayoutPanel = BuildKpiRow()
        kpiRow.Left = 24
        kpiRow.Top = 250
        AddHandler region.Resize, Sub() kpiRow.Width = region.Width - 48
        kpiRow.Width = region.Width - 48
        region.Controls.Add(kpiRow)

        ' ---- CATEGORY CHIPS ----
        Dim chipFlow As New FlowLayoutPanel With {
            .Left = 22, .Top = 392, .Height = 44, .AutoSize = False, .WrapContents = False,
            .BackColor = Color.Transparent, .Padding = New Padding(0),
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        }
        AddHandler region.Resize, Sub() chipFlow.Width = region.Width - 44
        chipFlow.Width = region.Width - 44

        chipFlow.Controls.Add(MakeChip("All", ""))
        For Each c In categories
            chipFlow.Controls.Add(MakeChip(c.name, c.name))
        Next
        region.Controls.Add(chipFlow)

        Return region
    End Function

    Private Shared Function SubtitleText() As String
        Return items.Count.ToString() & If(items.Count = 1, " active product", " active products") &
               "   " & ChrW(8226) & "   Stock-health monitor"
    End Function

    ' =========================================================================
    '  SUMMARY BAND  (total units | total value | health distribution bar)
    ' =========================================================================
    Private Shared Function BuildSummaryBand() As Panel
        Dim band As New TableLayoutPanel With {
            .Height = 142, .ColumnCount = 3, .RowCount = 1,
            .BackColor = Color.Transparent, .CellBorderStyle = TableLayoutPanelCellBorderStyle.None,
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        }
        band.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 26))
        band.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 26))
        band.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 48))
        band.RowStyles.Add(New RowStyle(SizeType.Percent, 100))

        ' --- Card 1: Total stock units ---
        Dim unitsCard As New BufferedPanel With {
            .Dock = DockStyle.Fill, .BackColor = CardBg, .Margin = New Padding(0, 0, 6, 0)
        }
        AddHandler unitsCard.Paint, Sub(s, e) PaintStatCard(CType(s, Panel), e)
        Dim lblUnitsCap As New Label With {
            .Text = "Total Stock Units", .Font = New Font("Segoe UI", 11),
            .ForeColor = SubText, .AutoSize = True, .Location = New Point(22, 14), .BackColor = Color.Transparent
        }
        lblTotalUnits = New Label With {
            .Text = "0", .Font = New Font("Segoe UI", 26, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(20, 40), .BackColor = Color.Transparent
        }
        Dim lblUnitsSub As New Label With {
            .Text = "On hand across catalog", .Font = New Font("Segoe UI", 10),
            .ForeColor = SubText, .AutoSize = True, .Location = New Point(22, 100), .BackColor = Color.Transparent
        }
        unitsCard.Controls.AddRange({lblUnitsCap, lblTotalUnits, lblUnitsSub})

        ' --- Card 2: Total inventory value ---
        Dim valueCard As New BufferedPanel With {
            .Dock = DockStyle.Fill, .BackColor = CardBg, .Margin = New Padding(6, 0, 6, 0)
        }
        AddHandler valueCard.Paint, Sub(s, e) PaintStatCard(CType(s, Panel), e)
        Dim lblValueCap As New Label With {
            .Text = "Inventory Value", .Font = New Font("Segoe UI", 11),
            .ForeColor = SubText, .AutoSize = True, .Location = New Point(22, 14), .BackColor = Color.Transparent
        }
        lblTotalValue = New Label With {
            .Text = Peso & "0.00", .Font = New Font("Segoe UI", 22, FontStyle.Bold),
            .ForeColor = MintPrimary, .AutoSize = True, .Location = New Point(20, 44), .BackColor = Color.Transparent
        }
        Dim lblValueSub As New Label With {
            .Text = "Qty " & ChrW(215) & " unit price", .Font = New Font("Segoe UI", 10),
            .ForeColor = SubText, .AutoSize = True, .Location = New Point(22, 100), .BackColor = Color.Transparent
        }
        valueCard.Controls.AddRange({lblValueCap, lblTotalValue, lblValueSub})

        ' --- Card 3: Health distribution share bar ---
        Dim distCard As New BufferedPanel With {
            .Dock = DockStyle.Fill, .BackColor = CardBg, .Margin = New Padding(6, 0, 0, 0)
        }
        AddHandler distCard.Paint, Sub(s, e) PaintStatCard(CType(s, Panel), e)
        Dim lblDistCap As New Label With {
            .Text = "Health Distribution", .Font = New Font("Segoe UI", 11),
            .ForeColor = SubText, .AutoSize = True, .Location = New Point(22, 14), .BackColor = Color.Transparent
        }
        distCard.Controls.Add(lblDistCap)

        healthBar = New Panel With {
            .Location = New Point(22, 46), .Height = 22, .BackColor = Color.Transparent,
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        }
        AddHandler healthBar.Paint, Sub(s, e) PaintHealthBar(CType(s, Panel), e)
        AddHandler distCard.Resize, Sub() healthBar.Width = distCard.Width - 44
        distCard.Controls.Add(healthBar)
        healthBar.Width = distCard.Width - 44

        ' Legend under the bar (four tiers).
        Dim legend As Panel = BuildHealthLegend()
        legend.Location = New Point(22, 84)
        legend.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        AddHandler distCard.Resize, Sub() legend.Width = distCard.Width - 44
        distCard.Controls.Add(legend)
        legend.Width = distCard.Width - 44

        band.Controls.Add(unitsCard, 0, 0)
        band.Controls.Add(valueCard, 1, 0)
        band.Controls.Add(distCard, 2, 0)
        Return band
    End Function

    Private Shared Function BuildHealthLegend() As Panel
        Dim legend As New FlowLayoutPanel With {
            .Height = 44, .AutoSize = False, .WrapContents = True, .FlowDirection = FlowDirection.LeftToRight,
            .BackColor = Color.Transparent, .Padding = New Padding(0)
        }
        Dim tierKeys() As String = {TierHealthy, TierLow, TierCritical, TierOut}
        For Each tk In tierKeys
            Dim swatchColor As Color = TierColor(tk)
            Dim caption As String = TierLabel(tk)
            Dim item As New Panel With {
                .Size = New Size(96, 20), .Margin = New Padding(0, 0, 6, 4), .BackColor = Color.Transparent
            }
            Dim dot As New Panel With {
                .Size = New Size(10, 10), .Location = New Point(0, 5), .BackColor = Color.Transparent
            }
            AddHandler dot.Paint,
                Sub(s, e)
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                    Using br As New SolidBrush(swatchColor)
                        e.Graphics.FillEllipse(br, 0, 0, 9, 9)
                    End Using
                End Sub
            Dim lbl As New Label With {
                .Text = caption, .Font = New Font("Segoe UI", 8.5F), .ForeColor = SubText,
                .AutoSize = True, .Location = New Point(14, 2), .BackColor = Color.Transparent
            }
            item.Controls.Add(dot)
            item.Controls.Add(lbl)
            legend.Controls.Add(item)
        Next
        Return legend
    End Function

    Private Shared Sub PaintStatCard(panel As Panel, e As PaintEventArgs)
        PanelRenderer.Draw(panel, e, True)
    End Sub

    ' Horizontal proportional share bar across the four tiers.
    Private Shared Sub PaintHealthBar(panel As Panel, e As PaintEventArgs)
        Dim g = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim w As Integer = panel.Width
        Dim h As Integer = panel.Height
        If w <= 2 OrElse h <= 2 Then Return

        Dim radius As Integer = h \ 2
        Dim full As New Rectangle(0, 0, w - 1, h - 1)

        ' Track background.
        Using path = RoundRect(full, radius)
            Using br As New SolidBrush(TrackBg)
                g.FillPath(br, path)
            End Using
        End Using

        Dim total As Integer = items.Count
        If total = 0 Then
            Using pen As New Pen(CardBorder, 1)
                Using path = RoundRect(full, radius)
                    g.DrawPath(pen, path)
                End Using
            End Using
            Return
        End If

        ' Segments ordered Healthy -> Low -> Critical -> Out for a natural read.
        Dim order() As String = {TierHealthy, TierLow, TierCritical, TierOut}
        Dim counts(order.Length - 1) As Integer
        For i = 0 To order.Length - 1
            counts(i) = CountTier(order(i))
        Next

        ' Clip to a rounded rect so each segment shares the pill silhouette.
        Dim oldClip = g.Clip
        Using clipPath = RoundRect(full, radius)
            g.SetClip(clipPath, CombineMode.Replace)
            Dim x As Single = 0
            For i = 0 To order.Length - 1
                If counts(i) <= 0 Then Continue For
                Dim segW As Single = (counts(i) / CSng(total)) * w
                Using br As New SolidBrush(TierColor(order(i)))
                    g.FillRectangle(br, x, 0, segW + 0.5F, h)
                End Using
                x += segW
            Next
            g.Clip = oldClip
        End Using

        Using pen As New Pen(Color.FromArgb(225, 232, 229), 1)
            Using path = RoundRect(full, radius)
                g.DrawPath(pen, path)
            End Using
        End Using
    End Sub

    ' =========================================================================
    '  KPI ROW  (health-tier cards that double as filters)
    ' =========================================================================
    Private Shared Function BuildKpiRow() As TableLayoutPanel
        Dim kpiRow As New TableLayoutPanel With {
            .Height = 132, .ColumnCount = 4, .RowCount = 1,
            .BackColor = Color.Transparent, .CellBorderStyle = TableLayoutPanelCellBorderStyle.None,
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        }
        For i = 0 To 3
            kpiRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 25))
        Next
        kpiRow.RowStyles.Add(New RowStyle(SizeType.Percent, 100))

        Dim kpiDef() As (title As String, tier As String, clr As Color, sub1 As String, badge As String) = {
            ("Healthy", TierHealthy, HealthyClr, "Comfortable stock", "OK"),
            ("Low", TierLow, LowClr, "Near reorder point", "Low"),
            ("Critical", TierCritical, CriticalClr, "At or below reorder", "Crit"),
            ("Out of Stock", TierOut, OutClr, "Zero on hand", "Out")
        }

        For i As Integer = 0 To 3
            Dim kd = kpiDef(i)
            Dim card As New BufferedPanel With {
                .Dock = DockStyle.Fill, .BackColor = CardBg, .Cursor = Cursors.Hand,
                .Margin = New Padding(If(i = 0, 0, 6), 0, If(i = 3, 0, 6), 0)
            }
            Dim capTier As String = kd.tier
            AddHandler card.Paint, Sub(s, e) PaintKpiCard(CType(s, Panel), e, capTier = activeTier)

            Dim lblK As New Label With {
                .Text = kd.title, .Font = New Font("Segoe UI", 11),
                .ForeColor = SubText, .AutoSize = True, .Location = New Point(22, 14), .BackColor = Color.Transparent
            }
            Dim lblV As New Label With {
                .Text = "0", .Font = New Font("Segoe UI", 26, FontStyle.Bold),
                .ForeColor = kd.clr, .AutoSize = True, .Location = New Point(20, 36), .BackColor = Color.Transparent
            }
            Dim lblS As New Label With {
                .Text = kd.sub1, .Font = New Font("Segoe UI", 10),
                .ForeColor = SubText, .AutoSize = True, .Location = New Point(22, 92), .BackColor = Color.Transparent
            }
            card.Controls.AddRange({lblK, lblV, lblS})

            If kd.badge <> "" Then
                Dim badge As New Label With {
                    .Text = kd.badge, .Font = New Font("Segoe UI", 9, FontStyle.Bold),
                    .ForeColor = kd.clr, .BackColor = TileBg, .AutoSize = True,
                    .Padding = New Padding(6, 3, 6, 3), .Anchor = AnchorStyles.Right Or AnchorStyles.Top
                }
                card.Controls.Add(badge)
                AddHandler card.Resize,
                    Sub()
                        badge.Left = card.Width - badge.Width - 14
                        badge.Top = 14
                    End Sub
            End If

            Dim onClick As EventHandler =
                Sub(s, e)
                    activeTier = If(activeTier = capTier, "", capTier)
                    RefreshKpiVisuals()
                    RenderRows()
                End Sub
            AddHandler card.Click, onClick
            For Each child As Control In card.Controls
                child.Cursor = Cursors.Hand
                AddHandler child.Click, onClick
            Next

            kpiCards.Add((card, kd.tier, lblV))
            kpiRow.Controls.Add(card, i, 0)
        Next

        Return kpiRow
    End Function

    Private Shared Sub PaintKpiCard(panel As Panel, e As PaintEventArgs, active As Boolean)
        Dim g = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim rect As New Rectangle(1, 1, panel.Width - 3, panel.Height - 3)
        ' Soft shadow to match the Home cards (PanelRenderer.Draw).
        Dim shadowRect As New Rectangle(rect.X + 1, rect.Y + 2, rect.Width - 2, rect.Height - 2)
        Using shadowBrush As New SolidBrush(Color.FromArgb(14, 0, 0, 0))
            Using sp = RoundRect(shadowRect, 14)
                g.FillPath(shadowBrush, sp)
            End Using
        End Using
        Using path = RoundRect(rect, 14)
            Using br As New SolidBrush(If(active, TileBg, CardBg))
                g.FillPath(br, path)
            End Using
            Using pen As New Pen(If(active, MintPrimary, Color.FromArgb(210, 220, 216)), If(active, 2.0F, 1.0F))
                g.DrawPath(pen, path)
            End Using
        End Using
    End Sub

    Private Shared Sub RefreshKpiVisuals()
        Dim vals() As Integer = {CountTier(TierHealthy), CountTier(TierLow), CountTier(TierCritical), CountTier(TierOut)}
        For i = 0 To kpiCards.Count - 1
            kpiCards(i).valueLbl.Text = vals(i).ToString()
            kpiCards(i).panel.Invalidate()
        Next
        If lblSubtitle IsNot Nothing Then lblSubtitle.Text = SubtitleText()
        If healthBar IsNot Nothing Then healthBar.Invalidate()
    End Sub

    Private Shared Sub RefreshSummary()
        Dim totalUnits As Integer = items.Sum(Function(p) p.StockQty)
        Dim totalValue As Decimal = items.Sum(Function(p) p.LineValue)
        If lblTotalUnits IsNot Nothing Then lblTotalUnits.Text = totalUnits.ToString("N0")
        If lblTotalValue IsNot Nothing Then lblTotalValue.Text = Peso & totalValue.ToString("N2")
        If healthBar IsNot Nothing Then healthBar.Invalidate()
    End Sub

    ' =========================================================================
    '  CATEGORY CHIPS
    ' =========================================================================
    Private Shared Function MakeChip(text As String, catKey As String) As Panel
        Dim font As New Font("Segoe UI", 10, FontStyle.Bold)
        Dim w As Integer = TextRenderer.MeasureText(text, font).Width + 28
        Dim chip As New BufferedPanel With {
            .Size = New Size(w, 30), .Margin = New Padding(0, 0, 8, 0), .Cursor = Cursors.Hand,
            .BackColor = Color.Transparent, .Tag = catKey
        }
        Dim lbl As New Label With {
            .Text = text, .Font = font, .AutoSize = False, .Dock = DockStyle.Fill,
            .TextAlign = ContentAlignment.MiddleCenter, .BackColor = Color.Transparent, .ForeColor = MintDark
        }
        AddHandler chip.Paint,
            Sub(s, e)
                Dim p = CType(s, Panel)
                Dim isActive As Boolean = (catKey = activeCategory)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Using path = RoundRect(New Rectangle(0, 0, p.Width - 1, p.Height - 1), 15)
                    Using br As New SolidBrush(If(isActive, MintDark, TileBg))
                        e.Graphics.FillPath(br, path)
                    End Using
                End Using
            End Sub
        chip.Controls.Add(lbl)

        Dim onClick As EventHandler =
            Sub(s, e)
                activeCategory = catKey
                RefreshChipVisuals()
                RenderRows()
            End Sub
        AddHandler chip.Click, onClick
        AddHandler lbl.Click, onClick
        chipMap(catKey) = chip
        Return chip
    End Function

    Private Shared Sub RefreshChipVisuals()
        For Each kv In chipMap
            Dim chip = kv.Value
            chip.Invalidate()
            For Each c As Control In chip.Controls
                If TypeOf c Is Label Then c.ForeColor = If(kv.Key = activeCategory, Color.White, MintDark)
            Next
        Next
    End Sub

    ' =========================================================================
    '  PRODUCT TABLE
    ' =========================================================================

    Private Shared Function BuildColumnHeader() As Panel
        ' Transparent so the card's own rounded white fill shows through: painting
        ' this strip in the page tone made the card look open at the top, as if the
        ' header sat outside the box. The tint is drawn in Paint below, clipped to
        ' the card's top corner radius so the border stays closed.
        Dim head As New BufferedPanel With {.Height = 40, .BackColor = Color.Transparent, .Padding = New Padding(0)}
        Dim inner As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 7, .RowCount = 1,
            .BackColor = Color.Transparent, .Padding = New Padding(16, 0, 16, 0)
        }
        AddColumnStyles(inner)
        Dim titles() As (t As String, align As ContentAlignment) = {
            ("PRODUCT", ContentAlignment.MiddleLeft),
            ("CATEGORY", ContentAlignment.MiddleLeft),
            ("QTY / REORDER", ContentAlignment.MiddleCenter),
            ("STOCK LEVEL", ContentAlignment.MiddleLeft),
            ("STATUS", ContentAlignment.MiddleCenter),
            ("LINE VALUE", ContentAlignment.MiddleRight),
            ("ACTION", ContentAlignment.MiddleCenter)
        }
        ' Margin must be zero on both this grid and the row grid, otherwise the
        ' default 3px cell margin shifts header text off its column. Col 0 is
        ' padded to clear the row thumbnail so PRODUCT sits over the item name.
        For i = 0 To 6
            inner.Controls.Add(New Label With {
                .Text = titles(i).t, .Font = New Font("Segoe UI", 8.5F, FontStyle.Bold),
                .ForeColor = SubText, .Dock = DockStyle.Fill, .TextAlign = titles(i).align,
                .BackColor = Color.Transparent,
                .Margin = New Padding(0),
                .Padding = New Padding(If(i = 0, 52, 0), 0, 0, 0)
            }, i, 0)
        Next
        head.Controls.Add(inner)
        AddHandler head.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias

                ' Fill the strip with the header tint, rounding only the two top
                ' corners so it follows the card's border exactly.
                '
                ' AddArc takes a bounding BOX, not a radius — PanelRenderer passes
                ' radius*2 for that box, so this must too or the band curves at
                ' half the card's radius and exposes white along the corner.
                '
                ' The band must stop INSIDE the card's border, never paint over
                ' it: the card paints first and this header is a child drawn on
                ' top, so any bleed would erase the frame along the top edge.
                Dim w As Integer = head.Width
                Dim h As Integer = head.Height
                ' The band sits CARD_INNER_INSET px inside the card, so its corner
                ' curve is the card's radius less that inset — otherwise the band
                ' curves wider than the frame it sits in.
                Dim d As Integer = (CARD_RADIUS - CARD_INNER_INSET) * 2
                Using gp As New GraphicsPath()
                    gp.AddArc(0, 0, d, d, 180, 90)
                    gp.AddArc(w - d, 0, d, d, 270, 90)
                    gp.AddLine(w, h, 0, h)
                    gp.CloseFigure()
                    Using br As New SolidBrush(HeaderBandBg)
                        e.Graphics.FillPath(br, gp)
                    End Using
                End Using

                ' Divider between the header band and the first row. Uses
                ' TableRule, the same grey as every other rule in the table, so
                ' the header line never reads heavier than the rows beneath it.
                '
                ' Smoothing is switched off first: the rounded corners above need
                ' AntiAlias, but an anti-aliased 1px horizontal line is spread
                ' over two pixel rows at partial alpha, which made this rule look
                ' washed out next to the crisp, unsmoothed row dividers.
                e.Graphics.SmoothingMode = SmoothingMode.None
                Using pen As New Pen(TableRule, 1)
                    e.Graphics.DrawLine(pen, RULE_INSET, h - 1, w - RULE_INSET, h - 1)
                End Using
            End Sub
        Return head
    End Function

    Private Shared Sub AddColumnStyles(tlp As TableLayoutPanel)
        Dim pct() As Single = {28, 13, 13, 16, 11, 12, 7}
        For Each p In pct
            tlp.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, p))
        Next
        tlp.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
    End Sub

    Private Shared Function FilteredItems() As List(Of StockItem)
        Dim q = items.AsEnumerable()
        If activeCategory <> "" Then q = q.Where(Function(p) p.CategoryName = activeCategory)
        If activeTier <> "" Then q = q.Where(Function(p) TierOf(p) = activeTier)
        If searchTerm <> "" Then
            Dim term = searchTerm.ToLowerInvariant()
            q = q.Where(Function(p) p.Name.ToLowerInvariant().Contains(term) OrElse
                                    p.Sku.ToLowerInvariant().Contains(term))
        End If
        ' Default sort: most urgent first (Out -> Critical -> Low -> Healthy),
        ' then lowest qty within a tier.
        Return q.OrderBy(Function(p) TierRank(TierOf(p))).
                 ThenBy(Function(p) p.StockQty).
                 ThenBy(Function(p) p.Name).
                 ToList()
    End Function

    Private Shared Sub RenderRows()
        If rowsHost Is Nothing Then Return
        rowsHost.SuspendLayout()
        rowsHost.Controls.Clear()
        activeRows.Clear()

        Dim list = FilteredItems()

        If list.Count = 0 Then
            Dim empty As New Label With {
                .Text = "No products match your filters.", .Font = New Font("Segoe UI", 11),
                .ForeColor = SubText, .AutoSize = True, .Location = New Point(20, 24), .BackColor = Color.Transparent
            }
            rowsHost.Controls.Add(empty)
        Else
            Dim top As Integer = 0
            Dim width As Integer = rowsHost.ClientSize.Width
            For Each it In list
                Dim row As Panel = BuildProductRow(it)
                row.Top = top
                row.Left = 0
                row.Width = width
                rowsHost.Controls.Add(row)
                activeRows.Add(row)
                top += row.Height
            Next
            rowContentHeight = top
        End If

        rowsHost.ResumeLayout()
        scrollOffset = 0
        ApplyScrollOffset()

        If lblShowing IsNot Nothing Then
            lblShowing.Text = "Showing " & list.Count & " of " & items.Count
        End If
    End Sub

    Private Shared Sub ReflowRowWidths()
        Dim width As Integer = rowsHost.ClientSize.Width
        For Each r In activeRows
            r.Width = width
        Next
        ApplyScrollOffset()
        rowsHost.Invalidate()
    End Sub

    ' ── SILENT SCROLLING ─────────────────────────────────────────────────────
    ' The rows panel scrolls with the mouse wheel but never shows a scrollbar,
    ' so the table keeps a clean edge and the rows stay aligned with the column
    ' header (a visible scrollbar would shrink the client area and shift them).
    Private Shared scrollOffset As Integer = 0
    Private Shared rowContentHeight As Integer = 0

    Private Shared Function MaxScroll() As Integer
        If rowsHost Is Nothing Then Return 0
        Return Math.Max(rowContentHeight - rowsHost.ClientSize.Height, 0)
    End Function

    Private Shared Sub ApplyScrollOffset()
        If rowsHost Is Nothing Then Return
        scrollOffset = Math.Max(0, Math.Min(scrollOffset, MaxScroll()))
        Dim top As Integer = -scrollOffset
        For Each r In activeRows
            r.Top = top
            top += r.Height
        Next
    End Sub

    Private Shared Sub HookSilentScroll(host As Panel)
        Dim onWheel As MouseEventHandler =
            Sub(s, e)
                If MaxScroll() <= 0 Then Return
                scrollOffset -= CInt(e.Delta / 120.0 * 48)
                host.SuspendLayout()
                ApplyScrollOffset()
                host.ResumeLayout()
                host.Invalidate()
            End Sub
        AddHandler host.MouseWheel, onWheel

        ' A Panel only gets wheel messages while focused, so take focus when the
        ' pointer enters the table. TabStop stays off — this is not a tab target.
        host.TabStop = False
        AddHandler host.MouseEnter,
            Sub()
                If Not host.Focused Then host.Focus()
            End Sub

        ' Child controls swallow the wheel event, so relay it up to the host.
        Dim relay As MouseEventHandler = Sub(s, e) onWheel(host, e)
        AddHandler host.ControlAdded,
            Sub(s, e)
                AttachWheelRelay(e.Control, relay)
            End Sub

        ' Empty space below the final row is painted as blank striped rows so the
        ' table body always reaches the footer instead of ending in a grey void.
        AddHandler host.Paint,
            Sub(s, e)
                ' The last real row already drew its own divider at
                ' rowContentHeight - 1, so start a full row below that rather than
                ' at rowContentHeight — otherwise the first filler rule doubles up
                ' on the last row's line and the spacing below it reads uneven.
                Dim y As Integer = (rowContentHeight - scrollOffset) + ROW_HEIGHT - 1
                Dim h As Integer = host.ClientSize.Height
                Using pen As New Pen(TableRule, 1)
                    While y < h
                        If y >= 0 Then
                            e.Graphics.DrawLine(pen, RULE_INSET, y, host.ClientSize.Width - RULE_INSET, y)
                        End If
                        y += ROW_HEIGHT
                    End While
                End Using
            End Sub
    End Sub

    Private Shared Sub AttachWheelRelay(c As Control, relay As MouseEventHandler)
        AddHandler c.MouseWheel, relay
        For Each child As Control In c.Controls
            AttachWheelRelay(child, relay)
        Next
        AddHandler c.ControlAdded, Sub(s, e) AttachWheelRelay(e.Control, relay)
    End Sub

    Private Shared Function BuildProductRow(it As StockItem) As Panel
        Dim row As New Panel With {
            .Height = ROW_HEIGHT, .BackColor = Color.Transparent,
            .Anchor = AnchorStyles.Left Or AnchorStyles.Top
        }
        AddHandler row.Paint,
            Sub(s, e)
                Using pen As New Pen(TableRule, 1)
                    e.Graphics.DrawLine(pen, RULE_INSET, row.Height - 1, row.Width - RULE_INSET, row.Height - 1)
                End Using
            End Sub
        AddHandler row.MouseEnter, Sub() row.BackColor = Color.FromArgb(247, 251, 249)
        AddHandler row.MouseLeave, Sub() row.BackColor = Color.Transparent

        Dim grid As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 7, .RowCount = 1,
            .BackColor = Color.Transparent, .Padding = New Padding(16, 0, 16, 0)
        }
        AddColumnStyles(grid)

        ' Col 0 — media thumbnail + name + sku
        Dim cProduct As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.Transparent}
        Dim thumb As Panel = BuildRowThumb(it)
        thumb.Location = New Point(2, 13)
        Dim lblName As New Label With {
            .Text = it.Name, .Font = New Font("Segoe UI", 10.5F, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(52, 13), .BackColor = Color.Transparent
        }
        Dim lblSku As New Label With {
            .Text = it.Sku, .Font = New Font("Segoe UI", 8.5F),
            .ForeColor = SubText, .AutoSize = True, .Location = New Point(52, 34), .BackColor = Color.Transparent
        }
        cProduct.Controls.AddRange({thumb, lblName, lblSku})

        ' Col 1 — category
        Dim lblCat As New Label With {
            .Text = it.CategoryName, .Font = New Font("Segoe UI", 10),
            .ForeColor = MintDark, .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleLeft,
            .BackColor = Color.Transparent
        }

        ' Col 2 — qty / reorder (center)
        Dim cStock As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.Transparent}
        Dim lblQty As New Label With {
            .Text = it.StockQty.ToString(), .Font = New Font("Segoe UI", 11, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = False, .Dock = DockStyle.Top, .Height = 28,
            .TextAlign = ContentAlignment.BottomCenter, .BackColor = Color.Transparent
        }
        Dim lblRe As New Label With {
            .Text = "reorder " & it.ReorderLevel, .Font = New Font("Segoe UI", 8),
            .ForeColor = FaintText, .AutoSize = False, .Dock = DockStyle.Top, .Height = 16,
            .TextAlign = ContentAlignment.TopCenter, .BackColor = Color.Transparent
        }
        cStock.Controls.Add(lblRe)
        cStock.Controls.Add(lblQty)

        ' Col 3 — visual stock-level bar (qty relative to reorder*3, clamped)
        Dim cLevel As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.Transparent}
        Dim levelBar As Panel = BuildLevelBar(it)
        cLevel.Controls.Add(levelBar)
        AddHandler cLevel.Resize,
            Sub()
                levelBar.Width = Math.Max(40, cLevel.Width - 16)
                levelBar.Left = 0
                levelBar.Top = (cLevel.Height - levelBar.Height) \ 2
            End Sub

        ' Col 4 — status badge (center)
        Dim cStatus As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.Transparent}
        Dim st = StatusOf(it)
        Dim badge As New Label With {
            .Text = st.text, .Font = New Font("Segoe UI", 8.5F, FontStyle.Bold),
            .ForeColor = st.fore, .BackColor = st.back, .AutoSize = True,
            .Padding = New Padding(10, 4, 10, 4), .TextAlign = ContentAlignment.MiddleCenter
        }
        RoundLabel(badge, 12)
        cStatus.Controls.Add(badge)
        AddHandler cStatus.Resize,
            Sub()
                badge.Left = (cStatus.Width - badge.Width) \ 2
                badge.Top = (cStatus.Height - badge.Height) \ 2
            End Sub

        ' Col 5 — line value (right)
        Dim lblValue As New Label With {
            .Text = Peso & it.LineValue.ToString("N2"), .Font = New Font("Segoe UI", 10.5F, FontStyle.Bold),
            .ForeColor = MintDark, .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleRight,
            .BackColor = Color.Transparent
        }

        ' Col 6 — action (Adjust link -> Stock Adjustment, stubbed for now)
        Dim cAct As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.Transparent}
        Dim btnAdjust As Label = MakeLink("Adjust", MintPrimary)
        cAct.Controls.Add(btnAdjust)
        AddHandler cAct.Resize,
            Sub()
                btnAdjust.Left = (cAct.Width - btnAdjust.Width) \ 2
                btnAdjust.Top = (cAct.Height - btnAdjust.Height) \ 2
            End Sub
        Dim capId As Integer = it.Id
        Dim capName As String = it.Name
        AddHandler btnAdjust.Click, Sub() OpenStockAdjustment(capId, capName)

        For Each cell As Control In New Control() {cProduct, lblCat, cStock, cLevel, cStatus, lblValue, cAct}
            cell.Margin = New Padding(0)
        Next

        grid.Controls.Add(cProduct, 0, 0)
        grid.Controls.Add(lblCat, 1, 0)
        grid.Controls.Add(cStock, 2, 0)
        grid.Controls.Add(cLevel, 3, 0)
        grid.Controls.Add(cStatus, 4, 0)
        grid.Controls.Add(lblValue, 5, 0)
        grid.Controls.Add(cAct, 6, 0)
        row.Controls.Add(grid)
        Return row
    End Function

    ' Per-row stock-level bar: fill = qty relative to a sensible max
    ' (reorder_level * 3), clamped to [0, 1]. When reorder_level is 0 we fall
    ' back to a small nominal max so the bar still reads sensibly.
    Private Shared Function BuildLevelBar(it As StockItem) As Panel
        Dim bar As New Panel With {
            .Height = 10, .Width = 120, .BackColor = Color.Transparent
        }
        Dim tier As String = TierOf(it)
        Dim fillColor As Color = TierColor(tier)

        Dim maxRef As Integer = it.ReorderLevel * 3
        If maxRef <= 0 Then maxRef = Math.Max(3, it.StockQty)
        Dim frac As Double = 0
        If maxRef > 0 Then frac = it.StockQty / CDbl(maxRef)
        If frac < 0 Then frac = 0
        If frac > 1 Then frac = 1
        Dim capFrac As Double = frac

        AddHandler bar.Paint,
            Sub(s, e)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim w As Integer = bar.Width
                Dim h As Integer = bar.Height
                If w <= 2 OrElse h <= 2 Then Return
                Dim radius As Integer = h \ 2
                Dim track As New Rectangle(0, 0, w - 1, h - 1)
                Using path = RoundRect(track, radius)
                    Using br As New SolidBrush(TrackBg)
                        g.FillPath(br, path)
                    End Using
                End Using
                Dim fillW As Integer = CInt((w - 1) * capFrac)
                If fillW >= 2 Then
                    Dim fillRect As New Rectangle(0, 0, fillW, h - 1)
                    Using path = RoundRect(fillRect, radius)
                        Using br As New SolidBrush(fillColor)
                            g.FillPath(br, path)
                        End Using
                    End Using
                End If
            End Sub
        Return bar
    End Function

    ' Rounded 38x38 thumbnail: photo for non-paint, colour chip for paint,
    ' tinted initial tile as the universal fallback. (Identical to Item List.)
    Private Shared Function BuildRowThumb(it As StockItem) As Panel
        Dim box As New Panel With {.Size = New Size(38, 38), .BackColor = Color.Transparent}
        Dim img As Image = If(it.IsPaint, Nothing, GetThumb(it.ImagePath, 36))
        Dim chip As Color = HexToColor(it.ColorHex, MintPrimary)
        Dim initial As String = If(String.IsNullOrEmpty(it.Name), "?", it.Name.Substring(0, 1).ToUpperInvariant())

        AddHandler box.Paint,
            Sub(s, e)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim r As New Rectangle(0, 0, 37, 37)
                Using path = RoundRect(r, 10)
                    If it.IsPaint Then
                        ' Solid colour chip
                        Using br As New SolidBrush(chip)
                            g.FillPath(br, path)
                        End Using
                    ElseIf img IsNot Nothing Then
                        ' Clipped rounded photo
                        Dim oldClip = g.Clip
                        g.SetClip(path, CombineMode.Replace)
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic
                        g.DrawImage(img, r)
                        g.Clip = oldClip
                    Else
                        ' Tinted initial tile
                        Using br As New SolidBrush(TileBg)
                            g.FillPath(br, path)
                        End Using
                        Using f As New Font("Segoe UI", 12, FontStyle.Bold)
                            Using br As New SolidBrush(MintPrimary)
                                Dim sz = g.MeasureString(initial, f)
                                g.DrawString(initial, f, br, (38 - sz.Width) / 2, (38 - sz.Height) / 2)
                            End Using
                        End Using
                    End If
                    Using pen As New Pen(Color.FromArgb(213, 221, 217), 1)
                        g.DrawPath(pen, path)
                    End Using
                End Using
            End Sub
        Return box
    End Function

    ' Status badge colors keyed to the 4-tier model.
    Private Shared Function StatusOf(it As StockItem) As (text As String, fore As Color, back As Color)
        Select Case TierOf(it)
            Case TierOut
                Return ("Out", OutClr, Color.FromArgb(251, 234, 234))
            Case TierCritical
                Return ("Critical", Color.FromArgb(190, 95, 20), Color.FromArgb(252, 240, 228))
            Case TierLow
                Return ("Low", Color.FromArgb(170, 130, 0), Color.FromArgb(251, 244, 222))
            Case Else
                Return ("Healthy", Color.FromArgb(0, 130, 90), Color.FromArgb(227, 247, 238))
        End Select
    End Function

    Private Shared Function MakeLink(text As String, clr As Color) As Label
        Dim lbl As New Label With {
            .Text = text, .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold),
            .ForeColor = clr, .AutoSize = True, .Cursor = Cursors.Hand, .BackColor = Color.Transparent
        }
        AddHandler lbl.MouseEnter, Sub() lbl.Font = New Font("Segoe UI", 9.5F, FontStyle.Bold Or FontStyle.Underline)
        AddHandler lbl.MouseLeave, Sub() lbl.Font = New Font("Segoe UI", 9.5F, FontStyle.Bold)
        Return lbl
    End Function

    ' =========================================================================
    '  ADJUST  (wiring stub for the future Stock Adjustment screen)
    ' =========================================================================
    ' Wired and ready: when Stock Adjustment exists, replace the body with a call
    ' that opens it pre-loaded with this product. Until then, a clean "coming
    ' soon" modal keeps the flow from breaking.
    Private Shared Sub OpenStockAdjustment(productId As Integer, productName As String)
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
    '  MEDIA / THUMBNAIL  (identical to Item List)
    ' =========================================================================
    Private Shared Function GetThumb(path As String, maxSize As Integer) As Image
        If String.IsNullOrEmpty(path) Then Return Nothing
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
    '  SMALL HELPERS  (mirrors Item List)
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

    Private Shared Sub SetCue(tb As TextBox, cue As String)
        tb.Tag = cue
        tb.ForeColor = FaintText
        tb.Text = cue
        AddHandler tb.GotFocus,
            Sub()
                If tb.Text = cue Then
                    tb.Text = ""
                    tb.ForeColor = MintDark
                End If
            End Sub
        AddHandler tb.LostFocus,
            Sub()
                If tb.Text.Trim() = "" Then
                    tb.Text = cue
                    tb.ForeColor = FaintText
                End If
            End Sub
    End Sub

    Private Sub InventoryStockLevelPanelBuilder_Load(sender As Object, e As EventArgs) Handles MyBase.Load

    End Sub
End Class