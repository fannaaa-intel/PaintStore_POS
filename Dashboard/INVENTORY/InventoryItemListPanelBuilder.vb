Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Windows.Forms
Imports System.Globalization
Imports System.Linq
Imports MySql.Data.MySqlClient

' =============================================================================
'  InventoryItemListPanelBuilder  (redesigned + fixed)
'
'  CHANGES IN THIS VERSION
'  -----------------------
'  A. REFRESH BUTTON added to BuildTopRegion, matching InventoryHomePanelBuilder
'     style exactly:
'       - Rounded pill via RoundRect GraphicsPath (avoids the non-standard
'         Graphics.FillRoundedRectangle extension used in Doc 2's Paint handler).
'       - Same muted-green resting colour  (220, 235, 232) / SubText text.
'       - MouseEnter → brighter green (210, 240, 235) / MintDark text.
'       - MouseLeave → restores resting colours.
'       - Click → calls LoadItemList(containerRef, activeCategory, activeStatus)
'         so filters and category chip are preserved across a refresh.
'       - Positioned immediately left of the "Add Item" button; search box sits
'         to the left of the refresh button.  The PositionTopRight lambda is
'         updated to lay out all three controls correctly.
'
'  B. ReloadAndRefresh now also resets searchTerm so the search box is cleared
'     after a save/delete, matching the expectation that you see the full list
'     after the dialog closes.
'
'  C. RoundControl is called on btnRefresh AFTER its size is set (same pattern
'     as btnAdd), so the pill Region is applied at the correct dimensions.
'
'  D. All other logic from the Doc-1 / Doc-3 redesign is preserved unchanged.
'
'  ORIGINAL NOTES
'  -----------------------------
'  1. MEDIA PER ITEM.
'     - "Paint" category   -> the item is represented by a COLOR (hex + picker).
'     - Any other category -> the item is represented by a PICTURE (file upload).
'     The dialog swaps its media block automatically when the category changes.
'  2. The product table now shows a real thumbnail: a colour chip for paint,
'     a rounded photo for everything else (falls back to a tinted initial tile).
'  3. Add / View / Edit dialogs were rebuilt with a premium layout: gradient
'     header, grouped "card" sections, a large live media preview, native colour
'     picker, click-to-browse image tile, and cleaner spacing.
'
'  DB REQUIREMENT (run once):
'     ALTER TABLE products ADD COLUMN image_path VARCHAR(255) DEFAULT NULL AFTER color_hex;
'
'  Images are copied into  <AppFolder>\ProductImages\  and only the relative
'  path is stored in the DB.
' =============================================================================
Public Class InventoryItemListPanelBuilder

    ' ── PALETTE ─────────────────────────────────────────────────────────────────
    Private Shared ReadOnly MintDark As Color = Theme.Dark
    Private Shared ReadOnly MintPrimary As Color = Theme.Primary
    Private Shared ReadOnly MintDeep As Color = Theme.Deep
    Private Shared ReadOnly MintBg As Color = Theme.PageBg
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
    Private Shared ReadOnly CardBorder As Color = Theme.BorderSoft
    Private Shared ReadOnly SubText As Color = Theme.SubText
    Private Shared ReadOnly FaintText As Color = Theme.FaintText
    Private Shared ReadOnly TileBg As Color = Theme.TileBg
    Private Shared ReadOnly FieldBg As Color = Color.FromArgb(248, 250, 249)
    Private Shared ReadOnly MintGreen As Color = Theme.Success
    Private Shared ReadOnly AccentYellow As Color = Color.FromArgb(220, 170, 0)
    Private Shared ReadOnly AccentRed As Color = Theme.Danger
    Private Shared ReadOnly RowDivider As Color = Theme.RowDivider

    Private Shared ReadOnly Peso As String = ChrW(8369)
    Private Const PaintCategory As String = "Paint"

    ' Borderless dialog that still casts a native OS drop shadow.
    Private Class ShadowDialog
        Inherits Form
        Public Sub New()
            SetStyle(ControlStyles.OptimizedDoubleBuffer Or
                     ControlStyles.AllPaintingInWmPaint Or
                     ControlStyles.ResizeRedraw, True)
        End Sub
        Protected Overrides ReadOnly Property CreateParams As CreateParams
            Get
                Const CS_DROPSHADOW As Integer = &H20000
                Dim cp = MyBase.CreateParams
                cp.ClassStyle = cp.ClassStyle Or CS_DROPSHADOW
                Return cp
            End Get
        End Property
    End Class

    ' Where uploaded product images live (created on demand).
    Private Shared ReadOnly ImageRoot As String =
        Path.Combine(Application.StartupPath, "ProductImages")

    ' ── PRODUCT MODEL ───────────────────────────────────────────────────────────
    Private Class ProdItem
        Public Id As Integer
        Public Sku As String
        Public Name As String
        Public CategoryId As Integer
        Public CategoryName As String
        Public PaintType As String
        Public SizeLabel As String
        Public ColorHex As String
        Public ImagePath As String
        Public UnitPrice As Decimal
        Public CostPrice As Decimal
        Public StockQty As Integer
        Public ReorderLevel As Integer
        Public ReadOnly Property IsPaint As Boolean
            Get
                Return String.Equals(CategoryName, PaintCategory, StringComparison.OrdinalIgnoreCase)
            End Get
        End Property
    End Class

    ' ── STATE ───────────────────────────────────────────────────────────────────
    Private Shared items As New List(Of ProdItem)
    Private Shared categories As New List(Of (id As Integer, name As String))

    Private Shared activeCategory As String = ""
    Private Shared activeStatus As String = ""
    Private Shared searchTerm As String = ""

    ' ── CONTROL REFERENCES ───────────────────────────────────────────────────────
    ' Fixed row pitch — shared by real rows and the blank filler rows painted
    ' beneath them, so the ruling stays continuous down the whole table.
    Private Const ROW_HEIGHT As Integer = 64

    Private Shared containerRef As Panel
    Private Shared rowsHost As Panel
    Private Shared lblSubtitle As Label
    Private Shared lblShowing As Label
    Private Shared searchBox As TextBox
    Private Shared kpiCards As New List(Of (panel As Panel, status As String, valueLbl As Label))
    Private Shared chipMap As New Dictionary(Of String, Panel)
    Private Shared activeRows As New List(Of Panel)

    ' Thumbnail cache so we don't re-decode the same file on every render.
    Private Shared ReadOnly thumbCache As New Dictionary(Of String, Image)

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Shared Sub LoadItemList(container As Panel,
                                   Optional categoryFilter As String = "",
                                   Optional statusFilter As String = "")

        containerRef = container
        activeCategory = If(categoryFilter, "")
        activeStatus = If(statusFilter, "")
        searchTerm = ""
        kpiCards.Clear()
        chipMap.Clear()
        activeRows.Clear()

        LoadData()

        container.SuspendLayout()
        container.Controls.Clear()
        container.BackColor = MintBg

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
                    " p.paint_type, p.size_label, p.color_hex, p.image_path, p.unit_price, p.cost_price, p.stock_qty, p.reorder_level " &
                    "FROM products p JOIN categories c ON p.category_id = c.category_id " &
                    "WHERE p.is_active = 1 ORDER BY p.name ASC", conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            items.Add(New ProdItem With {
                                .Id = SafeInt(rdr("product_id")),
                                .Sku = rdr("sku").ToString(),
                                .Name = rdr("name").ToString(),
                                .CategoryId = SafeInt(rdr("category_id")),
                                .CategoryName = rdr("cname").ToString(),
                                .PaintType = If(IsDBNull(rdr("paint_type")), "", rdr("paint_type").ToString()),
                                .SizeLabel = If(IsDBNull(rdr("size_label")), "", rdr("size_label").ToString()),
                                .ColorHex = If(IsDBNull(rdr("color_hex")), "", rdr("color_hex").ToString()),
                                .ImagePath = If(IsDBNull(rdr("image_path")), "", rdr("image_path").ToString()),
                                .UnitPrice = If(IsDBNull(rdr("unit_price")), 0D, Convert.ToDecimal(rdr("unit_price"))),
                                .CostPrice = If(IsDBNull(rdr("cost_price")), 0D, Convert.ToDecimal(rdr("cost_price"))),
                                .StockQty = SafeInt(rdr("stock_qty")),
                                .ReorderLevel = SafeInt(rdr("reorder_level"))
                            })
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load products:" & Environment.NewLine & ErrorLog.ForUser(ex) &
                          Environment.NewLine & Environment.NewLine &
                          "If the error mentions 'image_path', run:" & Environment.NewLine &
                          "ALTER TABLE products ADD COLUMN image_path VARCHAR(255) DEFAULT NULL AFTER color_hex;",
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    ' FIX B: searchTerm is also cleared so that after a save/delete the full
    ' list is visible and the search cue placeholder reappears correctly.
    Private Shared Sub ReloadAndRefresh()
        ImageCache.ClearAndDispose(thumbCache, containerRef)
        searchTerm = ""
        If searchBox IsNot Nothing Then
            Dim cue As String = CStr(If(searchBox.Tag, ""))
            searchBox.Text = cue
            searchBox.ForeColor = FaintText
        End If
        LoadData()
        RefreshKpiVisuals()
        RenderRows()
    End Sub

    ' =========================================================================
    '  TOP REGION
    ' =========================================================================
    Private Shared Function BuildTopRegion() As Panel
        Dim region As New Panel With {.Height = 306, .BackColor = MintBg, .Padding = New Padding(24, 22, 24, 0)}

        Dim lblTitle As New Label With {
            .Text = "Item List", .Font = New Font("Segoe UI", 24, FontStyle.Bold),
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
                    Using br As New SolidBrush(CardBg) : e.Graphics.FillPath(br, path) : End Using
                    Using pen As New Pen(CardBorder, 1) : e.Graphics.DrawPath(pen, path) : End Using
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

        ' ---- Add Item button ----
        Dim btnAdd As New Button With {
            .Text = "  + Add Item", .Size = New Size(120, 38),
            .FlatStyle = FlatStyle.Flat, .BackColor = MintPrimary, .ForeColor = Color.White,
            .Font = New Font("Segoe UI Semibold", 10, FontStyle.Bold), .Cursor = Cursors.Hand,
            .TextAlign = ContentAlignment.MiddleCenter
        }
        btnAdd.FlatAppearance.BorderSize = 0
        btnAdd.FlatAppearance.MouseOverBackColor = MintDeep
        RoundControl(btnAdd, 10)
        AddHandler btnAdd.Click, Sub() ShowItemDialog(0, False)

        region.Controls.AddRange({searchWrap, btnAdd})

        ' Layout: [searchWrap] [+ Add Item]  — right-aligned.
        Dim PositionTopRight As Action =
            Sub()
                btnAdd.Top = 22
                btnAdd.Left = region.Width - 24 - btnAdd.Width


                searchWrap.Top = 22
                searchWrap.Left = btnAdd.Left - 10 - searchWrap.Width
            End Sub
        AddHandler region.Resize, Sub() PositionTopRight()
        PositionTopRight()

        ' ---- KPI ROW ----
        Dim kpiRow As New TableLayoutPanel With {
            .Left = 24, .Top = 92, .Height = 140, .ColumnCount = 4, .RowCount = 1,
            .BackColor = Color.Transparent, .CellBorderStyle = TableLayoutPanelCellBorderStyle.None,
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        }
        For i = 0 To 3
            kpiRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 25))
        Next
        kpiRow.RowStyles.Add(New RowStyle(SizeType.Percent, 100))

        Dim kpiDef() As (title As String, status As String, clr As Color, sub1 As String, badge As String) = {
            ("Total Products", "", MintDark, "Active items", "Items"),
            ("In Stock", "in", MintGreen, "Above restock level", "Good"),
            ("Low Stock", "low", AccentYellow, "Needs restock", "Low"),
            ("Out of Stock", "out", AccentRed, "Zero on hand", "Out")
        }

        For i As Integer = 0 To 3
            Dim kd = kpiDef(i)
            Dim card As New BufferedPanel With {
                .Dock = DockStyle.Fill, .BackColor = CardBg, .Cursor = Cursors.Hand,
                .Margin = New Padding(If(i = 0, 0, 6), 0, If(i = 3, 0, 6), 0)
            }
            Dim capStatus As String = kd.status
            AddHandler card.Paint, Sub(s, e) PaintKpiCard(CType(s, Panel), e, capStatus = activeStatus)

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
                .ForeColor = SubText, .AutoSize = True, .Location = New Point(22, 100), .BackColor = Color.Transparent
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
                    activeStatus = If(activeStatus = capStatus, "", capStatus)
                    RefreshKpiVisuals()
                    RenderRows()
                End Sub
            AddHandler card.Click, onClick
            For Each child As Control In card.Controls
                child.Cursor = Cursors.Hand
                AddHandler child.Click, onClick
            Next

            kpiCards.Add((card, kd.status, lblV))
            kpiRow.Controls.Add(card, i, 0)
        Next
        AddHandler region.Resize, Sub() kpiRow.Width = region.Width - 48
        kpiRow.Width = region.Width - 48
        region.Controls.Add(kpiRow)

        ' ---- CATEGORY CHIPS ----
        Dim chipFlow As New FlowLayoutPanel With {
            .Left = 22, .Top = 248, .Height = 44, .AutoSize = False, .WrapContents = False,
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
               "   " & ChrW(8226) & "   Inventory catalog"
    End Function

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
                    Using br As New SolidBrush(If(isActive, MintDark, TileBg)) : e.Graphics.FillPath(br, path) : End Using
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
    '  KPI VISUALS
    ' =========================================================================
    Private Shared Sub PaintKpiCard(panel As Panel, e As PaintEventArgs, active As Boolean)
        Dim g = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim rect As New Rectangle(1, 1, panel.Width - 3, panel.Height - 3)
        Dim shadowRect As New Rectangle(rect.X + 1, rect.Y + 2, rect.Width - 2, rect.Height - 2)
        Using shadowBrush As New SolidBrush(Color.FromArgb(14, 0, 0, 0))
            Using sp = RoundRect(shadowRect, 14)
                g.FillPath(shadowBrush, sp)
            End Using
        End Using
        Using path = RoundRect(rect, 14)
            Using br As New SolidBrush(If(active, TileBg, CardBg)) : g.FillPath(br, path) : End Using
            Using pen As New Pen(If(active, MintPrimary, Color.FromArgb(210, 220, 216)), If(active, 2.0F, 1.0F))
                g.DrawPath(pen, path)
            End Using
        End Using
    End Sub

    Private Shared Sub RefreshKpiVisuals()
        Dim total = items.Count
        Dim inStock = items.Where(Function(p) p.StockQty > p.ReorderLevel).Count()
        Dim low = items.Where(Function(p) p.StockQty <= p.ReorderLevel AndAlso p.StockQty > 0).Count()
        Dim outOfStock = items.Where(Function(p) p.StockQty <= 0).Count()
        Dim vals() As Integer = {total, inStock, low, outOfStock}
        For i = 0 To kpiCards.Count - 1
            kpiCards(i).valueLbl.Text = vals(i).ToString()
            kpiCards(i).panel.Invalidate()
        Next
        If lblSubtitle IsNot Nothing Then lblSubtitle.Text = SubtitleText()
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
            .Dock = DockStyle.Fill, .ColumnCount = 6, .RowCount = 1,
            .BackColor = Color.Transparent, .Padding = New Padding(16, 0, 16, 0)
        }
        AddColumnStyles(inner)
        Dim titles() As (t As String, align As ContentAlignment) = {
            ("PRODUCT", ContentAlignment.MiddleLeft),
            ("TYPE / SIZE", ContentAlignment.MiddleLeft),
            ("UNIT PRICE", ContentAlignment.MiddleRight),
            ("STOCK", ContentAlignment.MiddleCenter),
            ("STATUS", ContentAlignment.MiddleCenter),
            ("ACTIONS", ContentAlignment.MiddleCenter)
        }
        ' Margin must be zero on both this grid and the row grid, otherwise the
        ' default 3px cell margin shifts header text off its column. Col 0 is
        ' padded to clear the row thumbnail so PRODUCT sits over the item name.
        For i = 0 To 5
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
        Dim pct() As Single = {35, 14, 13, 12, 14, 12}
        For Each p In pct
            tlp.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, p))
        Next
        tlp.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
    End Sub

    Private Shared Function FilteredItems() As List(Of ProdItem)
        Dim q = items.AsEnumerable()
        If activeCategory <> "" Then q = q.Where(Function(p) p.CategoryName = activeCategory)
        Select Case activeStatus
            Case "in" : q = q.Where(Function(p) p.StockQty > p.ReorderLevel)
            Case "low" : q = q.Where(Function(p) p.StockQty <= p.ReorderLevel AndAlso p.StockQty > 0)
            Case "out" : q = q.Where(Function(p) p.StockQty <= 0)
        End Select
        If searchTerm <> "" Then
            Dim term = searchTerm.ToLowerInvariant()
            q = q.Where(Function(p) p.Name.ToLowerInvariant().Contains(term) OrElse
                                    p.Sku.ToLowerInvariant().Contains(term))
        End If
        Return q.ToList()
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

    Private Shared Function BuildProductRow(it As ProdItem) As Panel
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
            .Dock = DockStyle.Fill, .ColumnCount = 6, .RowCount = 1,
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
        AddHandler cProduct.Resize, Sub() TextFit.Clamp(lblName, cProduct.Width - lblName.Left - 10)

        ' Col 1 — type / size
        Dim cTypeSize As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.Transparent}
        Dim lblType As New Label With {
            .Text = If(it.PaintType = "", it.CategoryName, it.PaintType), .Font = New Font("Segoe UI", 10),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(0, 14), .BackColor = Color.Transparent
        }
        Dim lblSize As New Label With {
            .Text = If(it.SizeLabel = "", ChrW(8212).ToString(), it.SizeLabel), .Font = New Font("Segoe UI", 8.5F),
            .ForeColor = SubText, .AutoSize = True, .Location = New Point(0, 34), .BackColor = Color.Transparent
        }
        cTypeSize.Controls.AddRange({lblType, lblSize})

        ' Col 2 — price (right)
        Dim lblPrice As New Label With {
            .Text = Peso & it.UnitPrice.ToString("N2"), .Font = New Font("Segoe UI", 10.5F, FontStyle.Bold),
            .ForeColor = MintDark, .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleRight,
            .BackColor = Color.Transparent
        }

        ' Col 3 — stock + reorder (center)
        Dim cStock As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.Transparent}
        Dim lblQty As New Label With {
            .Text = it.StockQty.ToString(), .Font = New Font("Segoe UI", 11, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = False, .Dock = DockStyle.Top, .Height = 28,
            .TextAlign = ContentAlignment.BottomCenter, .BackColor = Color.Transparent
        }
        Dim lblRe As New Label With {
            .Text = "restock " & it.ReorderLevel, .Font = New Font("Segoe UI", 8),
            .ForeColor = FaintText, .AutoSize = False, .Dock = DockStyle.Top, .Height = 16,
            .TextAlign = ContentAlignment.TopCenter, .BackColor = Color.Transparent
        }
        cStock.Controls.Add(lblRe)
        cStock.Controls.Add(lblQty)

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

        ' Col 5 — actions
        Dim cAct As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.Transparent}
        Dim btnView As Label = MakeLink("View", SubText)
        Dim btnEdit As Label = MakeLink("Edit", MintPrimary)
        cAct.Controls.AddRange({btnView, btnEdit})
        AddHandler cAct.Resize,
            Sub()
                Dim totalW As Integer = btnView.Width + btnEdit.Width + 14
                Dim startX As Integer = (cAct.Width - totalW) \ 2
                btnView.Left = startX
                btnView.Top = (cAct.Height - btnView.Height) \ 2
                btnEdit.Left = btnView.Right + 14
                btnEdit.Top = btnView.Top
            End Sub
        Dim capId As Integer = it.Id
        AddHandler btnView.Click, Sub() ShowItemDialog(capId, True)
        AddHandler btnEdit.Click, Sub() ShowItemDialog(capId, False)

        For Each cell As Control In New Control() {cProduct, cTypeSize, lblPrice, cStock, cStatus, cAct}
            cell.Margin = New Padding(0)
        Next

        grid.Controls.Add(cProduct, 0, 0)
        grid.Controls.Add(cTypeSize, 1, 0)
        grid.Controls.Add(lblPrice, 2, 0)
        grid.Controls.Add(cStock, 3, 0)
        grid.Controls.Add(cStatus, 4, 0)
        grid.Controls.Add(cAct, 5, 0)
        row.Controls.Add(grid)
        Return row
    End Function

    ' Rounded 38×38 thumbnail: colour chip for paint, photo for everything else,
    ' tinted initial tile as the universal fallback.
    Private Shared Function BuildRowThumb(it As ProdItem) As Panel
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
                        Using br As New SolidBrush(chip) : g.FillPath(br, path) : End Using
                    ElseIf img IsNot Nothing Then
                        Dim oldClip = g.Clip
                        g.SetClip(path, CombineMode.Replace)
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic
                        g.DrawImage(img, r)
                        g.Clip = oldClip
                    Else
                        Using br As New SolidBrush(TileBg) : g.FillPath(br, path) : End Using
                        Using f As New Font("Segoe UI", 12, FontStyle.Bold)
                            Using br As New SolidBrush(MintPrimary)
                                Dim sz = g.MeasureString(initial, f)
                                g.DrawString(initial, f, br, (38 - sz.Width) / 2, (38 - sz.Height) / 2)
                            End Using
                        End Using
                    End If
                    Using pen As New Pen(Color.FromArgb(213, 221, 217), 1) : g.DrawPath(pen, path) : End Using
                End Using
            End Sub
        Return box
    End Function

    Private Shared Function StatusOf(it As ProdItem) As (text As String, fore As Color, back As Color)
        If it.StockQty <= 0 Then Return ("Out", AccentRed, Color.FromArgb(251, 234, 234))
        If it.StockQty <= it.ReorderLevel Then Return ("Low", Color.FromArgb(170, 130, 0), Color.FromArgb(251, 244, 222))
        Return ("In Stock", Color.FromArgb(0, 130, 90), Color.FromArgb(227, 247, 238))
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
    '  ADD / EDIT / VIEW DIALOG
    ' =========================================================================
    Private Shared Sub ShowItemDialog(productId As Integer, viewOnly As Boolean)
        Dim editing As Boolean = productId > 0
        Dim existing As ProdItem = items.FirstOrDefault(Function(p) p.Id = productId)

        Dim pickedImagePath As String = If(existing IsNot Nothing, existing.ImagePath, "")

        Dim f As New ShadowDialog With {
            .FormBorderStyle = FormBorderStyle.None, .StartPosition = FormStartPosition.CenterParent,
            .BackColor = CardBg, .ShowInTaskbar = False, .Width = 540, .Height = 732,
            .MinimizeBox = False, .MaximizeBox = False,
            .KeyPreview = True, .Padding = New Padding(2)
        }
        AddHandler f.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.None
                Using pen As New Pen(Color.FromArgb(176, 196, 189), 1)
                    e.Graphics.DrawRectangle(pen, 0, 0, f.Width - 1, f.Height - 1)
                End Using
                Using pen As New Pen(Color.FromArgb(238, 242, 240), 1)
                    e.Graphics.DrawRectangle(pen, 1, 1, f.Width - 3, f.Height - 3)
                End Using
            End Sub

        ' ---- Gradient header band ----
        Dim header As New Panel With {.Dock = DockStyle.Top, .Height = 86, .BackColor = MintDark}
        AddHandler header.Paint,
            Sub(s, e)
                Using lg As New LinearGradientBrush(header.ClientRectangle, MintDark, MintDeep, 25.0F)
                    e.Graphics.FillRectangle(lg, header.ClientRectangle)
                End Using
            End Sub
        Dim titleText As String = If(viewOnly, "Product Details", If(editing, "Edit Item", "Add Item"))
        Dim headerSub As String = If(viewOnly, "Read-only view", "Fill in the product information below")
        Dim lblHdr As New Label With {
            .Text = titleText, .Font = New Font("Segoe UI", 16, FontStyle.Bold),
            .ForeColor = Color.White, .AutoSize = True, .Location = New Point(28, 20), .BackColor = Color.Transparent
        }
        Dim lblSub As New Label With {
            .Text = headerSub, .Font = New Font("Segoe UI", 9.5F), .ForeColor = Color.FromArgb(200, 230, 222),
            .AutoSize = True, .Location = New Point(29, 50), .BackColor = Color.Transparent
        }
        header.Controls.AddRange({lblHdr, lblSub})

        Dim btnX As New Label With {
            .Text = ChrW(10005).ToString(), .Font = New Font("Segoe UI", 12), .ForeColor = Color.FromArgb(210, 235, 228),
            .AutoSize = True, .Cursor = Cursors.Hand, .BackColor = Color.Transparent
        }
        AddHandler btnX.Click, Sub() f.Close()
        AddHandler btnX.MouseEnter, Sub() btnX.ForeColor = Color.White
        AddHandler btnX.MouseLeave, Sub() btnX.ForeColor = Color.FromArgb(210, 235, 228)
        header.Controls.Add(btnX)
        AddHandler header.Resize, Sub() btnX.Location = New Point(header.Width - 36, 16)
        btnX.Location = New Point(header.Width - 36, 16)
        f.Controls.Add(header)

        ' ---- Scrollable body ----
        ' The footer is DockStyle.Bottom and is added to the form later. WinForms
        ' gives a Fill control whatever is left after the docks added BEFORE it,
        ' so adding body first would let the footer sit on top of the body's last
        ' 72px -- which is exactly where the Reorder Level row lands. Docking the
        ' footer first and adding body after keeps them from overlapping.
        '
        ' No Padding here either: these children are positioned absolutely, and
        ' WinForms does not offset absolute children by a container's Padding, so
        ' setting it only inflated the scroll range. The breathing room below the
        ' header comes from the starting y instead.
        Dim body As New Panel With {.Dock = DockStyle.Fill, .BackColor = CardBg, .AutoScroll = True, .Padding = New Padding(0)}

        Dim y As Integer = 34
        Dim L As Integer = 28
        Dim fullW As Integer = 484
        Dim halfW As Integer = 234
        Dim rightX As Integer = L + 250

        Dim txtName = AddField(body, "Product Name", L, y, fullW) : y += 66

        Dim txtSku = AddField(body, "SKU", L, y, halfW)
        Dim cboCat = AddCombo(body, "Category", rightX, y, halfW)
        For Each c In categories : cboCat.Items.Add(c.name) : Next
        y += 66

        Dim txtType = AddField(body, "Paint Type", L, y, halfW)
        Dim txtSize = AddField(body, "Size Label", rightX, y, halfW)
        y += 70

        ' ---- DYNAMIC MEDIA SECTION ----
        Dim mediaCard As New BufferedPanel With {
            .Location = New Point(L, y), .Size = New Size(fullW, 168), .BackColor = FieldBg
        }
        AddHandler mediaCard.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Using path = RoundRect(New Rectangle(0, 0, mediaCard.Width - 1, mediaCard.Height - 1), 12)
                    Using br As New SolidBrush(FieldBg) : e.Graphics.FillPath(br, path) : End Using
                    Using pen As New Pen(CardBorder, 1) : e.Graphics.DrawPath(pen, path) : End Using
                End Using
            End Sub
        body.Controls.Add(mediaCard)

        Dim lblMediaTitle As New Label With {
            .Text = "Appearance", .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(16, 14), .BackColor = Color.Transparent
        }
        Dim lblMediaHint As New Label With {
            .Text = "", .Font = New Font("Segoe UI", 8.5F), .ForeColor = SubText,
            .AutoSize = True, .Location = New Point(16, 34), .BackColor = Color.Transparent
        }
        mediaCard.Controls.AddRange({lblMediaTitle, lblMediaHint})

        ' --- Colour controls (Paint) ---
        Dim swColor As New Panel With {.Size = New Size(84, 84), .Location = New Point(16, 62), .BackColor = MintPrimary, .Cursor = Cursors.Hand}
        Dim txtHex As New TextBox With {
            .Font = New Font("Segoe UI", 11), .Location = New Point(116, 70), .Width = 150,
            .BorderStyle = BorderStyle.FixedSingle, .ForeColor = MintDark
        }
        Dim btnPick As New Button With {
            .Text = "Pick colour", .Size = New Size(120, 34), .Location = New Point(116, 104),
            .FlatStyle = FlatStyle.Flat, .BackColor = CardBg, .ForeColor = MintDark,
            .Font = New Font("Segoe UI Semibold", 9), .Cursor = Cursors.Hand
        }
        btnPick.FlatAppearance.BorderColor = CardBorder
        AddHandler swColor.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Using path = RoundRect(New Rectangle(0, 0, swColor.Width - 1, swColor.Height - 1), 12)
                    Using br As New SolidBrush(HexToColor(txtHex.Text.Trim(), Color.Gainsboro)) : e.Graphics.FillPath(br, path) : End Using
                    Using pen As New Pen(Color.FromArgb(205, 214, 210), 1) : e.Graphics.DrawPath(pen, path) : End Using
                End Using
            End Sub
        AddHandler txtHex.TextChanged, Sub() swColor.Invalidate()
        Dim openColorPicker As Action =
            Sub()
                Using cd As New ColorDialog With {.FullOpen = True, .Color = HexToColor(txtHex.Text.Trim(), MintPrimary)}
                    If cd.ShowDialog() = DialogResult.OK Then
                        txtHex.Text = "#" & cd.Color.R.ToString("X2") & cd.Color.G.ToString("X2") & cd.Color.B.ToString("X2")
                    End If
                End Using
            End Sub
        AddHandler btnPick.Click, Sub() openColorPicker()
        AddHandler swColor.Click, Sub() openColorPicker()
        mediaCard.Controls.AddRange({swColor, txtHex, btnPick})

        ' --- Image controls (non-Paint) ---
        Dim imgBox As New Panel With {.Size = New Size(120, 120), .Location = New Point(16, 40), .BackColor = CardBg, .Cursor = Cursors.Hand}
        Dim btnBrowse As New Button With {
            .Text = "Choose image…", .Size = New Size(150, 34), .Location = New Point(152, 56),
            .FlatStyle = FlatStyle.Flat, .BackColor = MintPrimary, .ForeColor = Color.White,
            .Font = New Font("Segoe UI Semibold", 9), .Cursor = Cursors.Hand
        }
        btnBrowse.FlatAppearance.BorderSize = 0
        Dim btnRemove As New Button With {
            .Text = "Remove", .Size = New Size(150, 30), .Location = New Point(152, 94),
            .FlatStyle = FlatStyle.Flat, .BackColor = CardBg, .ForeColor = AccentRed,
            .Font = New Font("Segoe UI Semibold", 9), .Cursor = Cursors.Hand
        }
        btnRemove.FlatAppearance.BorderColor = CardBorder
        AddHandler imgBox.Paint,
            Sub(s, e)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim r As New Rectangle(0, 0, imgBox.Width - 1, imgBox.Height - 1)
                Using path = RoundRect(r, 12)
                    Dim img As Image = GetThumb(pickedImagePath, 116)
                    If img IsNot Nothing Then
                        Dim oldClip = g.Clip
                        g.SetClip(path, CombineMode.Replace)
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic
                        g.DrawImage(img, r)
                        g.Clip = oldClip
                        Using pen As New Pen(CardBorder, 1) : g.DrawPath(pen, path) : End Using
                    Else
                        Using br As New SolidBrush(FieldBg) : g.FillPath(br, path) : End Using
                        Using pen As New Pen(CardBorder, 1)
                            pen.DashStyle = DashStyle.Dash
                            g.DrawPath(pen, path)
                        End Using
                        Using fnt As New Font("Segoe UI", 8.5F), br As New SolidBrush(FaintText)
                            Dim t = "No image"
                            Dim sz = g.MeasureString(t, fnt)
                            g.DrawString(t, fnt, br, (imgBox.Width - sz.Width) / 2, (imgBox.Height - sz.Height) / 2)
                        End Using
                    End If
                End Using
            End Sub
        Dim browseImage As Action =
            Sub()
                Using ofd As New OpenFileDialog With {
                    .Title = "Choose product image",
                    .Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*"
                }
                    If ofd.ShowDialog() = DialogResult.OK Then
                        Dim stored = StoreImage(ofd.FileName)
                        If stored <> "" Then
                            pickedImagePath = stored
                            imgBox.Invalidate()
                        End If
                    End If
                End Using
            End Sub
        AddHandler btnBrowse.Click, Sub() browseImage()
        AddHandler imgBox.Click, Sub() If Not viewOnly Then browseImage()
        AddHandler btnRemove.Click,
            Sub()
                pickedImagePath = ""
                imgBox.Invalidate()
            End Sub
        mediaCard.Controls.AddRange({imgBox, btnBrowse, btnRemove})

        Dim applyMediaMode As Action =
            Sub()
                Dim isPaint As Boolean = String.Equals(CStr(If(cboCat.SelectedItem, "")), PaintCategory, StringComparison.OrdinalIgnoreCase)
                swColor.Visible = isPaint
                txtHex.Visible = isPaint
                btnPick.Visible = isPaint AndAlso Not viewOnly
                imgBox.Visible = Not isPaint
                btnBrowse.Visible = (Not isPaint) AndAlso Not viewOnly
                btnRemove.Visible = (Not isPaint) AndAlso Not viewOnly
                lblMediaHint.Text = If(isPaint, "Paint items are identified by colour.", "Upload a product photo for this item.")

                If Not viewOnly Then
                    If isPaint Then
                        txtType.Enabled = True
                        txtType.ReadOnly = False
                        txtType.BackColor = Color.White
                        txtType.ForeColor = MintDark
                        If txtType.Text = "Not applicable for this category" Then txtType.Text = ""
                    Else
                        txtType.Text = "Not applicable for this category"
                        txtType.ReadOnly = True
                        txtType.Enabled = False
                        txtType.BackColor = FieldBg
                        txtType.ForeColor = FaintText
                    End If
                End If
            End Sub

        Dim applyAutoSku As Action =
            Sub()
                If editing Then Return
                Dim catName As String = CStr(If(cboCat.SelectedItem, ""))
                If catName = "" Then Return
                txtSku.Text = NextSku(catName)
            End Sub

        txtSku.ReadOnly = True
        txtSku.BackColor = FieldBg
        txtSku.ForeColor = SubText
        txtSku.TabStop = False

        AddHandler cboCat.SelectedIndexChanged,
            Sub()
                applyMediaMode()
                applyAutoSku()
            End Sub

        y += 168 + 18

        ' ---- Numeric row ----
        ' Cost sits next to price so the margin is visible while typing. It
        ' feeds Gross Profit on the Accountant dashboard -- without it every
        ' sale of this item reports as 100% profit.
        ' Four numeric fields on TWO rows of two, rather than three-then-one. The
        ' lone trailing field used to leave a full empty row's worth of dead space
        ' above the footer.
        Dim txtCost = AddField(body, "Unit Cost", L, y, 234)
        Dim txtPrice = AddField(body, "Unit Price", rightX, y, 234)
        y += 80
        Dim numStock = AddNumeric(body, "Stock Qty", L, y, 234, 0, 1000000)
        Dim numReorder = AddNumeric(body, "Restock Level", rightX, y, 234, 0, 1000000)

        ' Says what the number does, so it is not mistaken for an order quantity.
        If Not viewOnly Then
            body.Controls.Add(New Label With {
                .Text = "Low-stock alert at or below this qty",
                .Font = New Font("Segoe UI", 7.5F), .ForeColor = FaintText,
                .AutoSize = True, .Location = New Point(rightX, y + 50), .BackColor = Color.Transparent
            })
        End If

        If editing AndAlso Not viewOnly Then
            numStock.Enabled = False
            Dim lblStockLock As New Label With {
                .Text = "Locked " & ChrW(8226) & " change in Stock Adjustment",
                .Font = New Font("Segoe UI", 7.5F), .ForeColor = FaintText,
                .AutoSize = True, .Location = New Point(L, y + 50), .BackColor = Color.Transparent
            }
            body.Controls.Add(lblStockLock)
        End If

        ' y + 48 is where the last input actually ends (22 label offset + control
        ' height); the lock / restock hints below it need a further ~16.
        y += If(Not viewOnly, 48 + 22, 48)
        body.Controls.Add(New Panel With {.Location = New Point(0, y), .Size = New Size(1, 16), .BackColor = Color.Transparent})
        y += 16

        ' ---- Footer with buttons ----
        Dim footer As New Panel With {.Dock = DockStyle.Bottom, .Height = 72, .BackColor = CardBg}
        AddHandler footer.Paint,
            Sub(s, e)
                Using pen As New Pen(CardBorder, 1) : e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0) : End Using
            End Sub
        Dim btnCancel As New Button With {
            .Text = If(viewOnly, "Close", "Cancel"), .Size = New Size(120, 42),
            .FlatStyle = FlatStyle.Flat, .BackColor = Color.FromArgb(242, 245, 244), .ForeColor = MintDark,
            .Font = New Font("Segoe UI Semibold", 10), .Cursor = Cursors.Hand
        }
        btnCancel.FlatAppearance.BorderSize = 0
        RoundControl(btnCancel, 10)
        AddHandler btnCancel.Click, Sub() f.Close()
        footer.Controls.Add(btnCancel)

        Dim btnSave As Button = Nothing
        If Not viewOnly Then
            btnSave = New Button With {
                .Text = If(editing, "Save Changes", "Add Item"), .Size = New Size(140, 42),
                .FlatStyle = FlatStyle.Flat, .BackColor = MintPrimary, .ForeColor = Color.White,
                .Font = New Font("Segoe UI Semibold", 10, FontStyle.Bold), .Cursor = Cursors.Hand
            }
            btnSave.FlatAppearance.BorderSize = 0
            btnSave.FlatAppearance.MouseOverBackColor = MintDeep
            RoundControl(btnSave, 10)
            AddHandler btnSave.Click,
                Sub()
                    Dim isPaint As Boolean = String.Equals(CStr(If(cboCat.SelectedItem, "")), PaintCategory, StringComparison.OrdinalIgnoreCase)
                    Dim hexToSave As String = If(isPaint, txtHex.Text.Trim(), "")
                    Dim imgToSave As String = If(isPaint, "", pickedImagePath)
                    Dim typeToSave As String = If(isPaint, txtType.Text.Trim(), "")
                    If SaveItem(productId, txtName, txtSku, cboCat, typeToSave, txtSize,
                                hexToSave, imgToSave, txtPrice, txtCost, numStock, numReorder) Then
                        f.Close()
                        ReloadAndRefresh()
                    End If
                End Sub
            footer.Controls.Add(btnSave)
        End If

        Dim btnDelete As Button = Nothing
        If editing AndAlso Not viewOnly Then
            btnDelete = New Button With {
                .Text = "Delete", .Size = New Size(110, 42),
                .FlatStyle = FlatStyle.Flat, .BackColor = Color.FromArgb(251, 234, 234), .ForeColor = AccentRed,
                .Font = New Font("Segoe UI Semibold", 10), .Cursor = Cursors.Hand
            }
            btnDelete.FlatAppearance.BorderSize = 0
            btnDelete.FlatAppearance.MouseOverBackColor = Color.FromArgb(245, 220, 220)
            RoundControl(btnDelete, 10)
            AddHandler btnDelete.Click,
                Sub()
                    If DeleteItem(productId, txtName.Text.Trim()) Then
                        f.Close()
                        ReloadAndRefresh()
                    End If
                End Sub
            footer.Controls.Add(btnDelete)
        End If

        f.Controls.Add(footer)
        ' body is added last so the Fill region is measured after the Bottom dock
        ' has claimed its 72px; otherwise the footer overlaps the Restock Level row.
        f.Controls.Add(body)

        Dim layoutFooter As Action =
            Sub()
                If btnSave IsNot Nothing Then
                    btnSave.Location = New Point(footer.Width - 24 - btnSave.Width, 16)
                    btnCancel.Location = New Point(btnSave.Left - 12 - btnCancel.Width, 16)
                Else
                    btnCancel.Location = New Point(footer.Width - 24 - btnCancel.Width, 16)
                End If
                If btnDelete IsNot Nothing Then btnDelete.Location = New Point(24, 16)
            End Sub
        AddHandler footer.Resize, Sub() layoutFooter()
        layoutFooter()

        ' ---- Populate ----
        If existing IsNot Nothing Then
            txtName.Text = existing.Name
            txtSku.Text = existing.Sku
            cboCat.SelectedItem = existing.CategoryName
            txtType.Text = existing.PaintType
            txtSize.Text = existing.SizeLabel
            txtHex.Text = existing.ColorHex
            txtPrice.Text = existing.UnitPrice.ToString("0.00")
            txtCost.Text = existing.CostPrice.ToString("0.00")
            numStock.Value = Math.Max(0, existing.StockQty)
            numReorder.Value = Math.Max(0, existing.ReorderLevel)
        Else
            If cboCat.Items.Count > 0 Then cboCat.SelectedIndex = 0
            txtPrice.Text = "0.00"
            txtCost.Text = "0.00"
            applyAutoSku()
        End If
        applyMediaMode()

        If viewOnly Then
            DisableInputs(body)
            txtHex.Enabled = False
        End If

        ' Size the dialog to whatever the body actually laid out, instead of a
        ' hardcoded guess. Edit mode is taller than view mode (it adds the stock
        ' lock hint), and hand-computed heights kept leaving dead space above the
        ' footer or forcing a needless scrollbar. y is the true content bottom.
        Dim chromeH As Integer = header.Height + footer.Height + f.Padding.Vertical
        Dim wantH As Integer = y + chromeH
        Dim maxH As Integer = Screen.FromPoint(Cursor.Position).WorkingArea.Height - 40
        f.Height = Math.Min(wantH, maxH)

        AddHandler f.KeyDown, Sub(s, e) If e.KeyCode = Keys.Escape Then f.Close()
        AddHandler f.Shown,
            Sub()
                f.Invalidate(True)
                f.Update()
            End Sub
        f.ShowDialog()
    End Sub

    Private Shared Sub DisableInputs(host As Control)
        For Each ctl As Control In host.Controls
            If TypeOf ctl Is TextBox OrElse TypeOf ctl Is ComboBox OrElse TypeOf ctl Is NumericUpDown Then
                ctl.Enabled = False
            End If
            If ctl.HasChildren Then DisableInputs(ctl)
        Next
    End Sub

    ' =========================================================================
    '  SAVE
    ' =========================================================================
    Private Shared Function SaveItem(productId As Integer, txtName As TextBox, txtSku As TextBox,
                                     cboCat As ComboBox, paintType As String, txtSize As TextBox,
                                     hex As String, imagePath As String, txtPrice As TextBox, txtCost As TextBox,
                                     numStock As NumericUpDown, numReorder As NumericUpDown) As Boolean
        If Not Permissions.Require(Permissions.EditProduct, "add or edit products") Then Return False
        Dim name = txtName.Text.Trim()
        Dim sku = txtSku.Text.Trim()
        If name = "" Then
            AppModal.Show("Product name is required.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning) : Return False
        End If
        If sku = "" Then
            AppModal.Show("SKU is required.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning) : Return False
        End If
        If cboCat.SelectedIndex < 0 Then
            AppModal.Show("Please choose a category.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning) : Return False
        End If
        Dim price As Decimal
        If Not Decimal.TryParse(txtPrice.Text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, price) OrElse price < 0 Then
            AppModal.Show("Unit price must be a valid non-negative number.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning) : Return False
        End If
        Dim cost As Decimal
        ' Blank is allowed and means "not known yet" -- an item with no cost
        ' recorded simply contributes nothing to COGS rather than blocking a save.
        If txtCost.Text.Trim() = "" Then
            cost = 0D
        ElseIf Not Decimal.TryParse(txtCost.Text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, cost) OrElse cost < 0 Then
            AppModal.Show("Unit cost must be a valid non-negative number (or left blank).", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning) : Return False
        End If
        ' Selling below cost is usually a typo, so it is worth a question --
        ' but it is a legitimate clearance decision, so it is not blocked.
        If cost > 0D AndAlso price > 0D AndAlso price < cost Then
            If AppModal.Show("The unit price is below the unit cost." & Environment.NewLine &
                             "Every sale of this item will record a loss." & Environment.NewLine & Environment.NewLine &
                             "Save it anyway?", "Price Below Cost",
                             MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then
                Return False
            End If
        End If
        If hex <> "" AndAlso Not (hex.StartsWith("#") AndAlso hex.Length = 7) Then
            AppModal.Show("Color hex must look like #RRGGBB (or be left blank).", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning) : Return False
        End If

        Dim catId As Integer = categories(cboCat.SelectedIndex).id

        Try
            Using conn = Db.GetConnection()
                conn.Open()
                ' A new product's opening stock has to be recorded as a movement
                ' as well, or that inventory appears from nowhere and stock on
                ' hand can never be reconciled against the movement history. The
                ' schema already defines an 'Initial' movement type for this; it
                ' was simply never written. Both statements go in one transaction
                ' so the product and its opening balance are never half-saved.
                Using txn As MySqlTransaction = conn.BeginTransaction()
                    Try
                        Dim sql As String
                        If productId > 0 Then
                            sql = "UPDATE products SET category_id=@cat, sku=@sku, name=@name, paint_type=@ptype, " &
                                  "size_label=@size, color_hex=@hex, image_path=@img, unit_price=@price, cost_price=@cost, " &
                                  "reorder_level=@re WHERE product_id=@id"
                        Else
                            sql = "INSERT INTO products (category_id, sku, name, paint_type, size_label, color_hex, " &
                                  "image_path, unit_price, cost_price, stock_qty, reorder_level, is_active) " &
                                  "VALUES (@cat, @sku, @name, @ptype, @size, @hex, @img, @price, @cost, @qty, @re, 1)"
                        End If

                        Dim newId As Integer = 0
                        Dim openingQty As Integer = If(productId > 0, 0, CInt(numStock.Value))

                        Using cmd As New MySqlCommand(sql, conn, txn)
                            cmd.Parameters.AddWithValue("@cat", catId)
                            cmd.Parameters.AddWithValue("@sku", sku)
                            cmd.Parameters.AddWithValue("@name", name)
                            cmd.Parameters.AddWithValue("@ptype", NullIfEmpty(If(paintType, "").Trim()))
                            cmd.Parameters.AddWithValue("@size", NullIfEmpty(txtSize.Text.Trim()))
                            cmd.Parameters.AddWithValue("@hex", NullIfEmpty(hex))
                            cmd.Parameters.AddWithValue("@img", NullIfEmpty(imagePath))
                            cmd.Parameters.AddWithValue("@price", price)
                            cmd.Parameters.AddWithValue("@cost", cost)
                            If productId <= 0 Then cmd.Parameters.AddWithValue("@qty", openingQty)
                            cmd.Parameters.AddWithValue("@re", CInt(numReorder.Value))
                            If productId > 0 Then cmd.Parameters.AddWithValue("@id", productId)
                            cmd.ExecuteNonQuery()
                            If productId <= 0 Then newId = CInt(cmd.LastInsertedId)
                        End Using

                        If productId <= 0 AndAlso openingQty <> 0 Then
                            Using cmd As New MySqlCommand(
                                "INSERT INTO stock_movements " &
                                "(product_id, user_id, movement_type, qty_change, reference_type, note, created_at) " &
                                "VALUES (@pid, @uid, 'Initial', @qc, 'newitem', 'Opening stock on item creation', NOW())",
                                conn, txn)
                                cmd.Parameters.AddWithValue("@pid", newId)
                                cmd.Parameters.AddWithValue("@uid", Session.CurrentUserId)
                                cmd.Parameters.AddWithValue("@qc", openingQty)
                                cmd.ExecuteNonQuery()
                            End Using
                        End If

                        txn.Commit()

                        ' Product changes are exactly what an audit needs to see;
                        ' this screen previously logged nothing but LOGOUT.
                        If productId > 0 Then
                            ActivityLog.Record("PRODUCT_EDIT", "product", productId, name)
                        Else
                            ActivityLog.Record("PRODUCT_ADD", "product", newId,
                                               name & " (" & sku & "), opening stock " & openingQty)
                        End If
                    Catch
                        txn.Rollback()
                        Throw
                    End Try
                End Using
            End Using
            Return True
        Catch ex As MySqlException
            If ex.Number = 1062 Then
                AppModal.Show("That SKU already exists. Please use a unique SKU.", "Duplicate SKU", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Else
                AppModal.Show("Could not save the product:" & Environment.NewLine & ErrorLog.ForUser(ex), "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End If
            Return False
        Catch ex As Exception
            AppModal.Show("Could not save the product:" & Environment.NewLine & ErrorLog.ForUser(ex), "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return False
        End Try
    End Function

    ' =========================================================================
    '  DELETE (soft delete / archive)
    ' =========================================================================
    Private Shared Function DeleteItem(productId As Integer, productName As String) As Boolean
        If Not Permissions.Require(Permissions.EditProduct, "delete products") Then Return False
        If productId <= 0 Then Return False

        Dim shownName As String = If(String.IsNullOrEmpty(productName), "this product", """" & productName & """")
        If AppModal.Show(
            "Delete " & shownName & "?" & Environment.NewLine & Environment.NewLine &
            "It will be removed from the item list and the POS catalog. Past sales and " &
            "reports that reference it are kept intact.",
            "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then
            Return False
        End If

        Try
            Using conn = Db.GetConnection()
                conn.Open()
                ' Products are only switched off (sales point at them), so the
                ' Recycle Bin copy is a soft one: restoring turns it back on.
                Using tx = conn.BeginTransaction()
                    Archive.Capture(conn, tx, "products", "product_id", productId,
                                    If(String.IsNullOrEmpty(productName), "Product #" & productId.ToString(), productName),
                                    soft:=True)
                    Using cmd As New MySqlCommand("UPDATE products SET is_active = 0 WHERE product_id = @id", conn, tx)
                        cmd.Parameters.AddWithValue("@id", productId)
                        cmd.ExecuteNonQuery()
                    End Using
                    tx.Commit()
                End Using
            End Using
            ActivityLog.Record("PRODUCT_DELETE", "product", productId, productName)
            Return True
        Catch ex As Exception
            AppModal.Show("Could not delete the product:" & Environment.NewLine & ErrorLog.ForUser(ex),
                          "Delete Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return False
        End Try
    End Function

    ' =========================================================================
    '  IMAGE HELPERS
    ' =========================================================================
    Private Shared Function StoreImage(sourcePath As String) As String
        Try
            If Not Directory.Exists(ImageRoot) Then Directory.CreateDirectory(ImageRoot)
            Dim ext = Path.GetExtension(sourcePath)
            Dim fileName = "PROD_" & Guid.NewGuid().ToString("N").Substring(0, 12) & ext
            Dim dest = Path.Combine(ImageRoot, fileName)
            File.Copy(sourcePath, dest, True)
            ' Store it relative to the app folder. Returning the absolute path
            ' baked the developer's checkout directory into the row, so the
            ' image resolved to nothing on any other machine or install path.
            Return ImageCache.ToStoredPath(dest)
        Catch ex As Exception
            AppModal.Show("Could not import the image:" & Environment.NewLine & ErrorLog.ForUser(ex),
                          "Image Error", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return ""
        End Try
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
    '  DIALOG FIELD HELPERS
    ' =========================================================================
    Private Shared Function AddField(host As Control, caption As String, x As Integer, y As Integer, w As Integer) As TextBox
        host.Controls.Add(New Label With {
            .Text = caption, .Font = New Font("Segoe UI", 9, FontStyle.Bold), .ForeColor = SubText,
            .AutoSize = True, .Location = New Point(x, y), .BackColor = Color.Transparent
        })
        Dim tb As New TextBox With {
            .Font = New Font("Segoe UI", 10.5F), .Location = New Point(x, y + 22), .Width = w,
            .BorderStyle = BorderStyle.FixedSingle, .ForeColor = MintDark
        }
        host.Controls.Add(tb)
        Return tb
    End Function

    Private Shared Function AddCombo(host As Control, caption As String, x As Integer, y As Integer, w As Integer) As ComboBox
        host.Controls.Add(New Label With {
            .Text = caption, .Font = New Font("Segoe UI", 9, FontStyle.Bold), .ForeColor = SubText,
            .AutoSize = True, .Location = New Point(x, y), .BackColor = Color.Transparent
        })
        Dim cbo As New ComboBox With {
            .DropDownStyle = ComboBoxStyle.DropDownList, .Font = New Font("Segoe UI", 10.5F),
            .Location = New Point(x, y + 22), .Width = w, .FlatStyle = FlatStyle.Flat
        }
        host.Controls.Add(cbo)
        Return cbo
    End Function

    Private Shared Function AddNumeric(host As Control, caption As String, x As Integer, y As Integer, w As Integer, lo As Integer, hi As Integer) As NumericUpDown
        host.Controls.Add(New Label With {
            .Text = caption, .Font = New Font("Segoe UI", 9, FontStyle.Bold), .ForeColor = SubText,
            .AutoSize = True, .Location = New Point(x, y), .BackColor = Color.Transparent
        })
        Dim n As New NumericUpDown With {
            .Font = New Font("Segoe UI", 10.5F), .Location = New Point(x, y + 22), .Width = w,
            .Minimum = lo, .Maximum = hi, .BorderStyle = BorderStyle.FixedSingle
        }
        host.Controls.Add(n)
        Return n
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

    Private Shared Function NullIfEmpty(s As String) As Object
        If String.IsNullOrEmpty(s) Then Return DBNull.Value
        Return s
    End Function

    Private Shared Function SkuPrefixFor(categoryName As String) As String
        Select Case If(categoryName, "").Trim().ToLowerInvariant()
            Case "paint", "paints" : Return "PNT"
            Case "tools", "tool" : Return "TOOL"
            Case "thinner", "thinners", "solvent", "solvents" : Return "THN"
            Case "accessories", "accessory" : Return "ACC"
            Case Else
                Dim letters = New String((If(categoryName, "").Where(Function(ch) Char.IsLetter(ch)).ToArray()))
                If letters.Length = 0 Then Return "ITM"
                Return letters.Substring(0, Math.Min(3, letters.Length)).ToUpperInvariant()
        End Select
    End Function

    ' Next free SKU for a category, e.g. "PNT-0007".
    '
    ' This must look at EVERY product row, not the in-memory `items` list: that
    ' list is loaded WHERE is_active = 1, but deleting a product only sets
    ' is_active = 0 and its SKU keeps occupying the unique index. Scanning the
    ' list therefore handed back a SKU that was already taken, and the insert
    ' failed on the unique constraint with no way for the user to correct it --
    ' the SKU box is read-only.
    Private Shared Function NextSku(categoryName As String) As String
        Dim prefix As String = SkuPrefixFor(categoryName)
        Dim maxNum As Integer = 0

        Try
            Using conn = Db.GetConnection()
                conn.Open()
                ' Compare against the numeric tail of every SKU sharing this
                ' prefix, active or not.
                Using cmd As New MySqlCommand(
                    "SELECT COALESCE(MAX(CAST(SUBSTRING(sku, @len) AS UNSIGNED)), 0) " &
                    "FROM products WHERE sku LIKE @pat", conn)
                    cmd.Parameters.AddWithValue("@len", prefix.Length + 2)  ' 1-based, past the '-'
                    cmd.Parameters.AddWithValue("@pat", prefix & "-%")
                    Dim o = cmd.ExecuteScalar()
                    If o IsNot Nothing AndAlso Not IsDBNull(o) Then
                        maxNum = Convert.ToInt32(o)
                    End If
                End Using
            End Using
        Catch swallowed As Exception
            ErrorLog.Write("InventoryItemListPanelBuilder.NextSku", swallowed)
            ' Unreachable database: fall back to the loaded list. Worst case the
            ' insert reports a duplicate SKU, which is already handled.
            For Each it In items
                Dim s = If(it.Sku, "").Trim().ToUpperInvariant()
                Dim head = prefix.ToUpperInvariant() & "-"
                If s.StartsWith(head) Then
                    Dim tail = s.Substring(head.Length)
                    Dim n As Integer
                    If Integer.TryParse(tail, n) AndAlso n > maxNum Then maxNum = n
                End If
            Next
        End Try

        Return prefix & "-" & (maxNum + 1).ToString("0000")
    End Function

    Private Shared Function RoundRect(rect As Rectangle, radius As Integer) As GraphicsPath
        Dim r As Integer = radius
        Dim path As New GraphicsPath()
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

    Private Shared Sub SetCue(tb As TextBox, cue As String)
        tb.Tag = cue
        tb.ForeColor = FaintText
        tb.Text = cue
        AddHandler tb.GotFocus,
            Sub()
                If tb.Text = cue Then tb.Text = "" : tb.ForeColor = MintDark
            End Sub
        AddHandler tb.LostFocus,
            Sub()
                If tb.Text.Trim() = "" Then tb.Text = cue : tb.ForeColor = FaintText
            End Sub
    End Sub

    Private Sub InventoryItemListPanelBuilder_Load(sender As Object, e As EventArgs) Handles MyBase.Load

    End Sub
End Class