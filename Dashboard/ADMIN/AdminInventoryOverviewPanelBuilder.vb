Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports MySql.Data.MySqlClient

' =============================================================================
'  AdminInventoryOverviewPanelBuilder  —  Admin ▸ Inventory Overview (READ-ONLY)
'
'  Store-wide stock oversight. Operational screens (stock adjustment, put-away,
'  item CRUD) stay in the Inventory role; nothing here writes.
'
'  This is where the admin dashboard's Low Stock tile lands.
'
'  LAYOUT:
'    TopRegion (Dock = Top, 128px):
'      ├─ TitleBar  (72px) : title + search + "Read-only" pill + Refresh
'      └─ FilterBar (56px) : Category + stock status
'    SummaryStrip (Dock = Top, 110px) : 4 cards over ALL active products
'    TableWrapper (Dock = Fill):
'      └─ listArea (BufferedPanel, PanelRenderer card):
'           ├─ ColHeader   (Dock = Top,    44px)
'           ├─ _scrollHost (Dock = Fill,   AutoScroll)
'           ├─ bottomPad   (Dock = Bottom, 12px)
'           └─ pagerBar    (Dock = Bottom, 52px)
'
'  LOW-STOCK THRESHOLD — one definition, shared with
'  InventoryLowStockPanelBuilder and the admin dashboard tile:
'      stock_qty * 100 <= reorder_level * 125      (within 125% of reorder)
'  It is held in LOW_STOCK_SQL below so the three screens cannot drift apart.
'  Note this predicate is also true at stock_qty = 0, so the Low Stock count
'  deliberately INCLUDES out-of-stock items — that is what makes it match the
'  dashboard tile. The per-row badge resolves the overlap by checking Out first.
' =============================================================================
Public Class AdminInventoryOverviewPanelBuilder

    ' ── PALETTE ───────────────────────────────────────────────────────────────
    Private Shared ReadOnly MintDark As Color = Theme.Dark
    Private Shared ReadOnly MintPrimary As Color = Theme.Primary
    Private Shared ReadOnly MintDeep As Color = Theme.Deep
    Private Shared ReadOnly MintBg As Color = Theme.PageBg
    Private Shared ReadOnly CardBg As Color = Color.White
    Private Shared ReadOnly SubText As Color = Theme.SubText
    Private Shared ReadOnly FaintText As Color = Theme.FaintText
    Private Shared ReadOnly RowDivider As Color = Theme.RowDivider
    Private Shared ReadOnly RowHover As Color = Theme.RowHover
    Private Shared ReadOnly AccentRed As Color = Theme.Danger
    Private Shared ReadOnly AccentAmber As Color = Color.FromArgb(210, 140, 40)
    Private Shared ReadOnly Peso As String = ChrW(8369)

    Private Shared ReadOnly BadgeIn As (fore As Color, back As Color) =
        (Color.FromArgb(0, 120, 92), Color.FromArgb(220, 248, 240))
    Private Shared ReadOnly BadgeLow As (fore As Color, back As Color) =
        (Color.FromArgb(165, 110, 20), Color.FromArgb(255, 244, 224))
    Private Shared ReadOnly BadgeOut As (fore As Color, back As Color) =
        (Color.FromArgb(180, 40, 40), Color.FromArgb(255, 233, 233))

    ' The single source of truth for "low stock" across the app.
    Private Const LOW_STOCK_SQL As String = "(p.stock_qty * 100 <= p.reorder_level * 125)"

    ' ── DATA MODEL ────────────────────────────────────────────────────────────
    Private Class ProductRow
        Public Sku As String
        Public Name As String
        Public Category As String
        Public Supplier As String
        Public UnitPrice As Decimal
        Public StockQty As Integer
        Public ReorderLevel As Integer
    End Class

    Private Class CategoryOption
        Public CategoryId As Integer
        Public Name As String
        Public Overrides Function ToString() As String
            Return Name
        End Function
    End Class

    ' ── STATE ─────────────────────────────────────────────────────────────────
    Private Const ALL_CATEGORIES As String = "All categories"
    Private Const STOCK_ALL As String = "All stock"
    Private Const STOCK_IN As String = "In Stock"
    Private Const STOCK_LOW As String = "Low Stock"
    Private Const STOCK_OUT As String = "Out of Stock"
    Private Const SEARCH_PLACEHOLDER As String = "Search by name or SKU…"

    Private Const SIDE_PAD As Integer = 24
    Private Const ROW_H As Integer = 72
    ' Matches the house KPI band used by Users / Suppliers / Categories and
    ' Sales Overview: 140px of card, caption 11pt at (22,14), value 26pt bold
    ' at (20,36), sub-label 10pt at (22,100), 12px between cards. A card that
    ' is nearly the same size as the rest of the system reads as a mistake.
    Private Const SUMMARY_H As Integer = 140
    Private Const MAX_PAGE_SIZE As Integer = 50
    ' topRegion 128 + summary (140+12) + spacers 16+16 + card padding 2
    ' + colHeader 44 + pagerBar 52 + bottomPad 12
    ' Keep in step with SUMMARY_H or the table misjudges its viewport.
    Private Const CHROME_H As Integer = 422
    Private Shared _pageSize As Integer = MAX_PAGE_SIZE

    Private Shared _container As Panel
    Private Shared _scrollHost As Panel

    Private Shared _searchQuery As String = ""
    Private Shared _categoryId As Integer = 0        ' 0 = all
    Private Shared _stockFilter As String = STOCK_ALL

    Private Shared _rows As New List(Of ProductRow)
    Private Shared _categories As New List(Of CategoryOption)
    Private Shared _totalRows As Integer = 0
    Private Shared _currentPage As Integer = 0
    Private Shared _reflowAttached As Boolean = False

    ' Summary (store-wide over active products, independent of the filters)
    Private Shared _totProducts, _lowCount, _outCount As Integer
    Private Shared _stockValue As Decimal = 0D

    Private Shared _lblTotProducts, _lblLow, _lblOut, _lblValue As Label
    Private Shared _lblPageInfo As Label
    Private Shared _btnPrev, _btnNext As Button
    Private Shared _searchTimer As Timer

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Shared Sub LoadInventoryOverview(container As Panel)
        _container = container
        _searchQuery = ""
        _categoryId = 0
        _stockFilter = STOCK_ALL
        _currentPage = 0

        ComputePageSize()
        LoadCategories()
        LoadSummary()
        LoadPage()
        Render()
    End Sub

    Private Shared Sub ComputePageSize()
        _pageSize = MAX_PAGE_SIZE
        If _container Is Nothing Then Return
        Dim viewport As Integer = _container.ClientSize.Height - CHROME_H
        If viewport < 120 Then Return          ' not laid out yet — keep the ceiling
        Dim fits As Integer = (viewport - 4) \ ROW_H
        _pageSize = Math.Max(4, Math.Min(MAX_PAGE_SIZE, fits))
    End Sub

    Private Shared Sub ReQuery(Optional resetPage As Boolean = True)
        If resetPage Then _currentPage = 0
        LoadSummary()
        LoadPage()
        PopulateRows()
        UpdateSummaryLabels()
        UpdatePager()
    End Sub

    ' Filters change the figures, so the labels are updated in place rather
    ' than by rebuilding the strip — that would rebuild the filter bar too and
    ' drop the dropdown selection mid-interaction.
    Private Shared Sub UpdateSummaryLabels()
        If _lblTotProducts IsNot Nothing Then _lblTotProducts.Text = _totProducts.ToString("N0")
        If _lblLow IsNot Nothing Then _lblLow.Text = _lowCount.ToString("N0")
        If _lblOut IsNot Nothing Then _lblOut.Text = _outCount.ToString("N0")
        If _lblValue IsNot Nothing Then _lblValue.Text = Money(_stockValue)
    End Sub

    ' =========================================================================
    '  DATA
    ' =========================================================================
    Private Shared Sub LoadCategories()
        _categories.Clear()
        _categories.Add(New CategoryOption With {.CategoryId = 0, .Name = ALL_CATEGORIES})
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT category_id, name FROM categories ORDER BY name ASC", conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            _categories.Add(New CategoryOption With {
                                .CategoryId = SafeInt(rdr("category_id")),
                                .Name = SafeStr(rdr("name"))
                            })
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load categories: " & ErrorLog.ForUser(ex), "Database Error",
                          MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    ' Both the summary and the list build their FROM clause from this, so the
    ' two queries cannot drift apart.
    Private Shared Function BaseJoins() As String
        Return "FROM products p " &
               "JOIN categories c ON c.category_id = p.category_id " &
               "LEFT JOIN suppliers s ON s.supplier_id = p.supplier_id "
    End Function

    ' Figures over the SAME filtered set the list shows — same joins, same
    ' WHERE clause, same parameters. BuildWhere() already scopes to
    ' p.is_active = 1, so the unfiltered case is unchanged.
    Private Shared Sub LoadSummary()
        _totProducts = 0 : _lowCount = 0 : _outCount = 0 : _stockValue = 0D
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT COUNT(*) AS n, " &
                    "SUM(CASE WHEN " & LOW_STOCK_SQL & " THEN 1 ELSE 0 END) AS low_n, " &
                    "SUM(CASE WHEN p.stock_qty <= 0 THEN 1 ELSE 0 END) AS out_n, " &
                    "COALESCE(SUM(p.stock_qty * p.unit_price),0) AS stock_value " &
                    BaseJoins() & BuildWhere(), conn)
                    AddWhereParams(cmd)
                    Using rdr = cmd.ExecuteReader()
                        If rdr.Read() Then
                            _totProducts = SafeInt(rdr("n"))
                            _lowCount = SafeInt(rdr("low_n"))
                            _outCount = SafeInt(rdr("out_n"))
                            _stockValue = SafeDec(rdr("stock_value"))
                        End If
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load the inventory summary: " & ErrorLog.ForUser(ex), "Database Error",
                          MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    Private Shared Function BuildWhere() As String
        Dim sb As New System.Text.StringBuilder("WHERE p.is_active = 1 ")
        If _categoryId > 0 Then sb.Append("AND p.category_id = @cat ")
        Select Case _stockFilter
            Case STOCK_IN
                sb.Append("AND p.stock_qty > 0 AND NOT " & LOW_STOCK_SQL & " ")
            Case STOCK_LOW
                sb.Append("AND " & LOW_STOCK_SQL & " ")
            Case STOCK_OUT
                sb.Append("AND p.stock_qty <= 0 ")
        End Select
        If _searchQuery <> "" Then
            sb.Append("AND (p.name LIKE @q OR COALESCE(p.sku,'') LIKE @q) ")
        End If
        Return sb.ToString()
    End Function

    Private Shared Sub AddWhereParams(cmd As MySqlCommand)
        If _categoryId > 0 Then cmd.Parameters.AddWithValue("@cat", _categoryId)
        If _searchQuery <> "" Then cmd.Parameters.AddWithValue("@q", "%" & _searchQuery & "%")
    End Sub

    Private Shared Sub LoadPage()
        _rows.Clear()
        _totalRows = 0

        Dim joins As String = BaseJoins()

        Try
            Using conn = Db.GetConnection()
                conn.Open()

                Using cmd As New MySqlCommand(
                    "SELECT COUNT(*) " & joins & BuildWhere(), conn)
                    AddWhereParams(cmd)
                    _totalRows = Convert.ToInt32(cmd.ExecuteScalar())
                End Using

                Dim totalPages As Integer =
                    Math.Max(1, CInt(Math.Ceiling(_totalRows / _pageSize)))
                If _currentPage > totalPages - 1 Then _currentPage = totalPages - 1
                If _currentPage < 0 Then _currentPage = 0

                Using cmd As New MySqlCommand(
                    "SELECT p.sku, p.name, c.name AS cname, " &
                    "COALESCE(s.name,'—') AS sname, " &
                    "p.unit_price, p.stock_qty, p.reorder_level " &
                    joins & BuildWhere() &
                    "ORDER BY p.name ASC " &
                    "LIMIT @lim OFFSET @off", conn)
                    AddWhereParams(cmd)
                    cmd.Parameters.AddWithValue("@lim", _pageSize)
                    cmd.Parameters.AddWithValue("@off", _currentPage * _pageSize)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            _rows.Add(New ProductRow With {
                                .Sku = SafeStr(rdr("sku")),
                                .Name = SafeStr(rdr("name")),
                                .Category = SafeStr(rdr("cname")),
                                .Supplier = SafeStr(rdr("sname")),
                                .UnitPrice = SafeDec(rdr("unit_price")),
                                .StockQty = SafeInt(rdr("stock_qty")),
                                .ReorderLevel = SafeInt(rdr("reorder_level"))
                            })
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load products: " & ErrorLog.ForUser(ex), "Database Error",
                          MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    Private Shared Function SafeInt(o As Object) As Integer
        If o Is Nothing OrElse IsDBNull(o) Then Return 0
        Return Convert.ToInt32(o)
    End Function

    Private Shared Function SafeDec(o As Object) As Decimal
        If o Is Nothing OrElse IsDBNull(o) Then Return 0D
        Return Convert.ToDecimal(o)
    End Function

    Private Shared Function SafeStr(o As Object) As String
        If o Is Nothing OrElse IsDBNull(o) Then Return ""
        Return o.ToString()
    End Function

    Private Shared Function Money(v As Decimal) As String
        Return Peso & v.ToString("N2")
    End Function

    ' Same three-way resolution the badges use: Out wins over Low.
    Private Shared Function StockStateOf(r As ProductRow) As String
        If r.StockQty <= 0 Then Return STOCK_OUT
        If r.StockQty * 100 <= r.ReorderLevel * 125 Then Return STOCK_LOW
        Return STOCK_IN
    End Function

    ' =========================================================================
    '  RENDER
    ' =========================================================================
    Private Shared Sub Render()
        _container.SuspendLayout()
        _container.Controls.Clear()
        _reflowAttached = False
        _container.BackColor = MintBg

        Dim tableWrapper As New Panel With {
            .Dock = DockStyle.Fill, .BackColor = MintBg, .Padding = New Padding(0)
        }

        Dim listArea As New BufferedPanel With {
            .Dock = DockStyle.Fill, .BackColor = CardBg, .Padding = New Padding(1)
        }
        AddHandler listArea.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)

        _scrollHost = New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = Color.Transparent,
            .AutoScroll = True,
            .Padding = New Padding(0)
        }

        Dim colHdr As Panel = BuildColHeader()
        colHdr.Dock = DockStyle.Top

        Dim pagerBar As Panel = BuildPagerBar()
        pagerBar.Dock = DockStyle.Bottom

        Dim bottomPad As New Panel With {
            .Dock = DockStyle.Bottom, .Height = 12, .BackColor = Color.Transparent
        }

        ' bottomPad before pagerBar so the gap sits between the last row and
        ' the pager rule, not underneath the pager.
        listArea.Controls.Add(bottomPad)
        listArea.Controls.Add(pagerBar)
        listArea.Controls.Add(colHdr)
        listArea.Controls.Add(_scrollHost)
        colHdr.BringToFront()
        _scrollHost.BringToFront()
        _scrollHost.AutoScrollMinSize = New Size(0, 1)

        tableWrapper.Controls.Add(listArea)
        tableWrapper.Controls.Add(New Panel With {.Dock = DockStyle.Bottom, .Height = 16, .BackColor = MintBg})
        tableWrapper.Controls.Add(New Panel With {.Dock = DockStyle.Top, .Height = 16, .BackColor = MintBg})
        tableWrapper.Controls.Add(New Panel With {.Dock = DockStyle.Left, .Width = SIDE_PAD, .BackColor = MintBg})
        tableWrapper.Controls.Add(New Panel With {.Dock = DockStyle.Right, .Width = SIDE_PAD, .BackColor = MintBg})

        Dim summary As Panel = BuildSummaryStrip()
        summary.Dock = DockStyle.Top

        Dim topRegion As Panel = BuildTopRegion()
        topRegion.Dock = DockStyle.Top

        _container.Controls.Add(tableWrapper)
        _container.Controls.Add(summary)
        _container.Controls.Add(topRegion)

        _container.ResumeLayout()

        PopulateRows()
        UpdateSummaryLabels()
        UpdatePager()
    End Sub

    ' =========================================================================
    '  TOP REGION
    ' =========================================================================
    Private Shared Function BuildTopRegion() As Panel
        Dim region As New Panel With {.BackColor = MintBg, .AutoSize = False, .Height = 128}

        Dim titleBar As New Panel With {
            .Height = 72, .Dock = DockStyle.Top, .BackColor = Color.Transparent
        }

        titleBar.Controls.Add(New Label With {
            .Text = "Inventory Overview",
            .Font = New Font("Segoe UI", 28, FontStyle.Bold),
            .ForeColor = MintDark,
            .AutoSize = True,
            .Location = New Point(SIDE_PAD, 16)
        })

        Dim pill As New Label With {
            .Text = "Read-only",
            .Font = New Font("Segoe UI Semibold", 9.0F),
            .ForeColor = SubText,
            .BackColor = Color.FromArgb(232, 238, 236),
            .AutoSize = True,
            .Padding = New Padding(10, 5, 10, 5),
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        RoundLabel(pill, 12)

        Dim searchWrap As New Panel With {
            .Size = New Size(250, 34),
            .BackColor = CardBg,
            .Cursor = Cursors.IBeam,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        Dim searchFocused As Boolean = False
        AddHandler searchWrap.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim rc = searchWrap.ClientRectangle : rc.Inflate(-1, -1)
                Using b As New SolidBrush(CardBg)
                    e.Graphics.FillRoundedRectangle(b, rc, 8)
                End Using
                Using p As New Pen(If(searchFocused, MintPrimary, RowDivider),
                                   If(searchFocused, 1.8F, 1.0F))
                    e.Graphics.DrawRoundedRectangle(p, rc, 8)
                End Using
                Using p As New Pen(Color.FromArgb(160, 180, 178), 1.4F)
                    e.Graphics.DrawEllipse(p, 10, 10, 12, 12)
                    e.Graphics.DrawLine(p, 20, 20, 25, 25)
                End Using
            End Sub

        Dim txtSearch As New TextBox With {
            .Font = New Font("Segoe UI", 10),
            .BorderStyle = BorderStyle.None,
            .BackColor = CardBg,
            .ForeColor = SubText,
            .Text = SEARCH_PLACEHOLDER,
            .Location = New Point(34, 9),
            .Width = 204
        }

        ' Debounced — the search hits the DB.
        If _searchTimer IsNot Nothing Then
            _searchTimer.Stop()
            _searchTimer.Dispose()
        End If
        _searchTimer = New Timer With {.Interval = 350}
        AddHandler _searchTimer.Tick,
            Sub()
                _searchTimer.Stop()
                ReQuery()
            End Sub

        AddHandler txtSearch.Enter,
            Sub()
                searchFocused = True
                If txtSearch.Text = SEARCH_PLACEHOLDER Then
                    txtSearch.Text = ""
                    txtSearch.ForeColor = Color.FromArgb(45, 45, 45)
                End If
                searchWrap.Invalidate()
            End Sub
        AddHandler txtSearch.Leave,
            Sub()
                searchFocused = False
                If String.IsNullOrWhiteSpace(txtSearch.Text) Then
                    txtSearch.Text = SEARCH_PLACEHOLDER
                    txtSearch.ForeColor = SubText
                End If
                searchWrap.Invalidate()
            End Sub
        AddHandler txtSearch.TextChanged,
            Sub()
                If txtSearch.Text = SEARCH_PLACEHOLDER Then Return
                _searchQuery = txtSearch.Text.Trim()
                _searchTimer.Stop()
                _searchTimer.Start()
            End Sub
        AddHandler searchWrap.Click, Sub() txtSearch.Focus()
        searchWrap.Controls.Add(txtSearch)

        AddHandler titleBar.Resize,
            Sub()
                pill.Left = titleBar.Width - pill.Width - SIDE_PAD
                pill.Top = (titleBar.Height - pill.Height) \ 2
                searchWrap.Left = pill.Left - searchWrap.Width - 10
                searchWrap.Top = (titleBar.Height - searchWrap.Height) \ 2
            End Sub

        titleBar.Controls.Add(searchWrap)
        titleBar.Controls.Add(pill)

        ' ── Filter bar ───────────────────────────────────────────────────────
        Dim filterBar As New Panel With {
            .Height = 56, .Dock = DockStyle.Top, .BackColor = Color.Transparent
        }

        Dim lblCat As New Label With {
            .Text = "Category", .Font = New Font("Segoe UI", 9.5F), .ForeColor = SubText,
            .AutoSize = True, .Location = New Point(SIDE_PAD, 19), .BackColor = Color.Transparent
        }
        ' _categories and this name list stay in the same order, so the picked
        ' index maps straight back to the CategoryOption behind it.
        Dim catNames As New List(Of String)
        For Each c In _categories
            catNames.Add(c.ToString())
        Next
        Dim boxCat As Panel = FilterFields.MakeSelectField(catNames, 196, 0,
            Sub(i As Integer)
                _categoryId = If(i >= 0 AndAlso i < _categories.Count,
                                 _categories(i).CategoryId, 0)
                ReQuery()
            End Sub)
        boxCat.Location = New Point(SIDE_PAD + 70, 11)

        Dim lblStock As New Label With {
            .Text = "Stock", .Font = New Font("Segoe UI", 9.5F), .ForeColor = SubText,
            .AutoSize = True, .Location = New Point(boxCat.Right + 24, 19),
            .BackColor = Color.Transparent
        }
        Dim stockNames As New List(Of String) From {
            STOCK_ALL, STOCK_IN, STOCK_LOW, STOCK_OUT
        }
        Dim boxStock As Panel = FilterFields.MakeSelectField(stockNames, 166, 0,
            Sub(i As Integer)
                _stockFilter = If(i >= 0 AndAlso i < stockNames.Count,
                                  stockNames(i), STOCK_ALL)
                ReQuery()
            End Sub)
        boxStock.Location = New Point(boxCat.Right + 76, 11)

        filterBar.Controls.AddRange({lblCat, boxCat, lblStock, boxStock})

        region.Controls.Add(filterBar)
        region.Controls.Add(titleBar)
        Return region
    End Function

    ' =========================================================================
    '  SUMMARY STRIP
    ' =========================================================================
    Private Shared Function BuildSummaryStrip() As Panel
        ' +12 so the 12px bottom padding does not eat into the card: the cards
        ' must be a full SUMMARY_H tall or the sub-label at y=100 clips.
        Dim bar As New Panel With {
            .Height = SUMMARY_H + 12,
            .BackColor = MintBg,
            .Padding = New Padding(SIDE_PAD, 0, SIDE_PAD, 12)
        }

        Dim grid As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .ColumnCount = 4,
            .RowCount = 1,
            .BackColor = Color.Transparent,
            .CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        }
        For i As Integer = 0 To 3
            grid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 25.0F))
        Next
        grid.RowStyles.Add(New RowStyle(SizeType.Percent, 100))

        Dim c1 = MakeSummaryCard("Total Products", MintDark,
                                 _totProducts.ToString("N0"), "active items", _lblTotProducts)
        Dim c2 = MakeSummaryCard("Low Stock", AccentAmber,
                                 _lowCount.ToString("N0"), "at or near restock level", _lblLow)
        Dim c3 = MakeSummaryCard("Out of Stock", AccentRed,
                                 _outCount.ToString("N0"), "nothing on hand", _lblOut)
        Dim c4 = MakeSummaryCard("Stock Value", MintDeep,
                                 Money(_stockValue), "qty x unit price", _lblValue)

        c1.Margin = New Padding(0, 0, 12, 0)
        c2.Margin = New Padding(0, 0, 12, 0)
        c3.Margin = New Padding(0, 0, 12, 0)
        c4.Margin = New Padding(0, 0, 0, 0)

        grid.Controls.Add(c1, 0, 0)
        grid.Controls.Add(c2, 1, 0)
        grid.Controls.Add(c3, 2, 0)
        grid.Controls.Add(c4, 3, 0)

        bar.Controls.Add(grid)
        Return bar
    End Function

    Private Shared Function MakeSummaryCard(caption As String, valueClr As Color,
                                            value As String, subLabel As String,
                                            ByRef valueLabel As Label) As Panel
        Dim card As New BufferedPanel With {.Dock = DockStyle.Fill, .BackColor = CardBg}
        AddHandler card.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)

        card.Controls.Add(New Label With {
            .Text = caption,
            .Font = New Font("Segoe UI", 11),
            .ForeColor = SubText,
            .AutoSize = True,
            .Location = New Point(22, 14),
            .BackColor = Color.Transparent
        })

        valueLabel = New Label With {
            .Text = value,
            .Font = New Font("Segoe UI", 26, FontStyle.Bold),
            .ForeColor = valueClr,
            .AutoSize = True,
            .Location = New Point(20, 36),
            .BackColor = Color.Transparent
        }
        card.Controls.Add(valueLabel)

        card.Controls.Add(New Label With {
            .Text = subLabel,
            .Font = New Font("Segoe UI", 10),
            .ForeColor = SubText,
            .AutoSize = True,
            .Location = New Point(22, 100),
            .BackColor = Color.Transparent
        })
        Return card
    End Function

    ' =========================================================================
    '  COLUMN HEADER  —  44px (must match BuildDataRow)
    ' =========================================================================
    Private Shared Function BuildColHeader() As Panel
        Dim hdr As New Panel With {.Height = 44, .BackColor = Color.Transparent}

        Dim labels As Label() = {
            MakeColLabel("PRODUCT", New Point(24, 13)),
            MakeColLabel("CATEGORY / SUPPLIER", New Point(0, 13)),
            MakeColLabel("PRICE", New Point(0, 13)),
            MakeColLabel("STOCK", New Point(0, 13)),
            MakeColLabel("RESTOCK", New Point(0, 13)),
            MakeColLabel("STATUS", New Point(0, 13))
        }
        hdr.Controls.AddRange(labels)

        AddHandler hdr.Resize,
            Sub()
                Dim w = hdr.Width
                labels(0).Left = 24
                labels(1).Left = CInt(w * 0.28)
                labels(2).Left = CInt(w * 0.5)
                labels(3).Left = CInt(w * 0.6)
                labels(4).Left = CInt(w * 0.69)
                labels(5).Left = CInt(w * 0.82)
            End Sub

        AddHandler hdr.Paint,
            Sub(s, e)
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 20, hdr.Height - 1, hdr.Width - 20, hdr.Height - 1)
                End Using
            End Sub

        Return hdr
    End Function

    Private Shared Function MakeColLabel(text As String, loc As Point) As Label
        Return New Label With {
            .Text = text,
            .Font = New Font("Segoe UI", 10, FontStyle.Bold),
            .ForeColor = SubText,
            .AutoSize = True,
            .Location = loc,
            .BackColor = Color.Transparent
        }
    End Function

    ' =========================================================================
    '  ROWS
    ' =========================================================================
    Private Shared Sub PopulateRows()
        If _scrollHost Is Nothing Then Return

        _scrollHost.SuspendLayout()
        _scrollHost.Controls.Clear()

        If _reflowAttached Then
            RemoveHandler _scrollHost.Resize, AddressOf ReflowScrollHost
            _reflowAttached = False
        End If

        If _rows.Count = 0 Then
            _scrollHost.Controls.Add(BuildEmptyRow("No products match the current filters."))
        Else
            Dim y As Integer = 4
            For Each r As ProductRow In _rows
                Dim row As Panel = BuildDataRow(r, ROW_H)
                row.Location = New Point(0, y)
                row.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
                _scrollHost.Controls.Add(row)
                row.Width = _scrollHost.ClientSize.Width - 4
                y += ROW_H
            Next
            If Not _reflowAttached Then
                _reflowAttached = True
                AddHandler _scrollHost.Resize, AddressOf ReflowScrollHost
            End If
        End If

        _scrollHost.ResumeLayout()
        _scrollHost.Invalidate(True)
    End Sub

    Private Shared Sub ReflowScrollHost(sender As Object, e As EventArgs)
        If _scrollHost Is Nothing Then Return
        Dim w As Integer = _scrollHost.ClientSize.Width - 4
        For Each c As Control In _scrollHost.Controls
            c.Width = w
        Next
    End Sub

    Private Shared Function BuildDataRow(r As ProductRow, height As Integer) As Panel
        Dim p As New BufferedPanel With {.Height = height, .BackColor = Color.Transparent}

        Dim state As String = StockStateOf(r)
        Dim accent As Color
        Dim badgeClr As (fore As Color, back As Color)
        Select Case state
            Case STOCK_OUT
                accent = AccentRed : badgeClr = BadgeOut
            Case STOCK_LOW
                accent = AccentAmber : badgeClr = BadgeLow
            Case Else
                accent = MintPrimary : badgeClr = BadgeIn
        End Select

        AddHandler p.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                If TypeOf p.Tag Is Boolean AndAlso CBool(p.Tag) Then
                    Dim rect As New Rectangle(8, 3, p.Width - 16, height - 6)
                    Using br As New SolidBrush(RowHover)
                        e.Graphics.FillRoundedRectangle(br, rect, 10)
                    End Using
                End If
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 20, p.Height - 1, p.Width - 20, p.Height - 1)
                End Using
            End Sub

        Dim lblName As New Label With {
            .Text = If(r.Name = "", "—", r.Name),
            .Font = New Font("Segoe UI", 11, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblSku As New Label With {
            .Text = If(r.Sku = "", "no SKU", r.Sku),
            .Font = New Font("Segoe UI", 9),
            .ForeColor = FaintText, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblCat As New Label With {
            .Text = If(r.Category = "", "—", r.Category),
            .Font = New Font("Segoe UI", 10.5F),
            .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblSup As New Label With {
            .Text = If(r.Supplier = "", "—", r.Supplier),
            .Font = New Font("Segoe UI", 9),
            .ForeColor = FaintText, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblPrice As New Label With {
            .Text = Money(r.UnitPrice),
            .Font = New Font("Segoe UI", 10.5F),
            .ForeColor = Color.FromArgb(45, 60, 56),
            .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblStock As New Label With {
            .Text = r.StockQty.ToString("N0"),
            .Font = New Font("Segoe UI", 13, FontStyle.Bold),
            .ForeColor = If(state = STOCK_IN, MintPrimary, accent),
            .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblReorder As New Label With {
            .Text = r.ReorderLevel.ToString("N0"),
            .Font = New Font("Segoe UI", 10.5F),
            .ForeColor = FaintText, .AutoSize = True, .BackColor = Color.Transparent
        }

        Dim badge As New Label With {
            .Text = If(state = STOCK_IN, "In Stock", If(state = STOCK_LOW, "Low", "Out")),
            .Font = New Font("Segoe UI", 9, FontStyle.Bold),
            .ForeColor = badgeClr.fore,
            .BackColor = badgeClr.back,
            .AutoSize = True,
            .Padding = New Padding(10, 4, 10, 4)
        }
        RoundLabel(badge, 10)

        p.Controls.AddRange({lblName, lblSku, lblCat, lblSup, lblPrice,
                             lblStock, lblReorder, badge})

        AddHandler p.Resize,
            Sub()
                Dim w = p.Width
                Dim top1 As Integer = (height \ 2) - 17
                Dim top2 As Integer = (height \ 2) + 2

                lblName.Left = 24 : lblName.Top = top1
                lblSku.Left = 24 : lblSku.Top = top2

                Dim cx As Integer = CInt(w * 0.28)
                TextFit.Clamp(lblName, cx - 24 - 12)
                lblCat.Left = cx : lblCat.Top = top1
                lblSup.Left = cx : lblSup.Top = top2
                TextFit.Clamp(lblSup, CInt(w * 0.5) - cx - 12)

                lblPrice.Left = CInt(w * 0.5)
                lblPrice.Top = (height - lblPrice.Height) \ 2
                lblStock.Left = CInt(w * 0.6)
                lblStock.Top = (height - lblStock.Height) \ 2
                lblReorder.Left = CInt(w * 0.69)
                lblReorder.Top = (height - lblReorder.Height) \ 2
                badge.Left = CInt(w * 0.82)
                badge.Top = (height - badge.Height) \ 2
            End Sub

        PropagateHover(p)
        Return p
    End Function

    Private Shared Function BuildEmptyRow(message As String) As Panel
        Dim p As New Panel With {.Height = 90, .Dock = DockStyle.Top, .BackColor = Color.Transparent}
        p.Controls.Add(New Label With {
            .Text = message,
            .Font = New Font("Segoe UI", 10.5F),
            .ForeColor = FaintText,
            .AutoSize = True,
            .Location = New Point(22, 28),
            .BackColor = Color.Transparent
        })
        Return p
    End Function

    ' =========================================================================
    '  PAGER
    ' =========================================================================
    Private Shared Function BuildPagerBar() As Panel
        Dim wrapper As New Panel With {.Height = 52, .BackColor = Color.Transparent}

        AddHandler wrapper.Paint,
            Sub(s, e)
                Using dp As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(dp, 0, 0, wrapper.Width, 0)
                End Using
            End Sub

        _btnPrev = New Button With {
            .Text = "‹", .FlatStyle = FlatStyle.Flat, .Cursor = Cursors.Hand,
            .Size = New Size(32, 32), .BackColor = Color.FromArgb(220, 235, 232),
            .ForeColor = MintDark, .Font = New Font("Segoe UI", 14, FontStyle.Bold),
            .TextAlign = ContentAlignment.MiddleCenter
        }
        _btnPrev.FlatAppearance.BorderSize = 0

        _btnNext = New Button With {
            .Text = "›", .FlatStyle = FlatStyle.Flat, .Cursor = Cursors.Hand,
            .Size = New Size(32, 32), .BackColor = Color.FromArgb(220, 235, 232),
            .ForeColor = MintDark, .Font = New Font("Segoe UI", 14, FontStyle.Bold),
            .TextAlign = ContentAlignment.MiddleCenter
        }
        _btnNext.FlatAppearance.BorderSize = 0

        _lblPageInfo = New Label With {
            .Font = New Font("Segoe UI", 9.5F), .ForeColor = SubText,
            .AutoSize = True, .BackColor = Color.Transparent
        }

        AddHandler wrapper.Resize,
            Sub()
                Dim cx As Integer = wrapper.Width \ 2
                _btnPrev.Left = cx - _btnPrev.Width - _lblPageInfo.Width \ 2 - 8
                _btnPrev.Top = (wrapper.Height - _btnPrev.Height) \ 2
                _lblPageInfo.Left = cx - _lblPageInfo.Width \ 2
                _lblPageInfo.Top = (wrapper.Height - _lblPageInfo.Height) \ 2
                _btnNext.Left = cx + _lblPageInfo.Width \ 2 + 8
                _btnNext.Top = (wrapper.Height - _btnNext.Height) \ 2
            End Sub

        AddHandler _btnPrev.Click,
            Sub()
                If _currentPage > 0 Then
                    _currentPage -= 1
                    ReQuery(resetPage:=False)
                End If
            End Sub
        AddHandler _btnNext.Click,
            Sub()
                Dim totalPages As Integer =
                    Math.Max(1, CInt(Math.Ceiling(_totalRows / _pageSize)))
                If _currentPage < totalPages - 1 Then
                    _currentPage += 1
                    ReQuery(resetPage:=False)
                End If
            End Sub

        wrapper.Controls.AddRange({_btnPrev, _lblPageInfo, _btnNext})
        Return wrapper
    End Function

    Private Shared Sub UpdatePager()
        If _lblPageInfo Is Nothing Then Return
        Dim totalPages As Integer = Math.Max(1, CInt(Math.Ceiling(_totalRows / _pageSize)))
        If _totalRows = 0 Then
            _lblPageInfo.Text = "No products"
        Else
            Dim firstRow As Integer = _currentPage * _pageSize + 1
            Dim lastRow As Integer = Math.Min(_totalRows, firstRow + _rows.Count - 1)
            _lblPageInfo.Text =
                $"Showing {firstRow:N0}–{lastRow:N0} of {_totalRows:N0}   ·   Page {_currentPage + 1} of {totalPages}"
        End If
        _btnPrev.Enabled = (_currentPage > 0)
        _btnNext.Enabled = (_currentPage < totalPages - 1)
        If _lblPageInfo.Parent IsNot Nothing Then _lblPageInfo.Parent.PerformLayout()
    End Sub

    ' =========================================================================
    '  SHARED HELPERS
    ' =========================================================================
    Private Shared Function MakePillButton(text As String, width As Integer,
                                           backClr As Color, foreClr As Color) As Button
        Dim b As New Button With {
            .Text = text,
            .Size = New Size(width, 34),
            .FlatStyle = FlatStyle.Flat,
            .Font = New Font("Segoe UI", 9.5F),
            .Cursor = Cursors.Hand,
            .BackColor = backClr,
            .ForeColor = foreClr,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        b.FlatAppearance.BorderSize = 0
        Dim normalClr As Color = backClr
        Dim hoverClr As Color = Color.FromArgb(Math.Max(0, backClr.R - 12),
                                               Math.Max(0, backClr.G - 12),
                                               Math.Max(0, backClr.B - 12))
        AddHandler b.Paint,
            Sub(s, e)
                Dim bb = CType(s, Button)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect = bb.ClientRectangle : rect.Inflate(-1, -1)
                Using br As New SolidBrush(bb.BackColor)
                    g.FillRoundedRectangle(br, rect, 8)
                End Using
                TextRenderer.DrawText(g, bb.Text, bb.Font, bb.ClientRectangle, bb.ForeColor,
                    TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub
        AddHandler b.MouseEnter, Sub()
                                     b.BackColor = hoverClr
                                     b.Invalidate()
                                 End Sub
        AddHandler b.MouseLeave, Sub()
                                     b.BackColor = normalClr
                                     b.Invalidate()
                                 End Sub
        Return b
    End Function

    Private Shared Sub RoundLabel(lbl As Label, radius As Integer)
        Dim apply As Action =
            Sub()
                If lbl.Width <= radius OrElse lbl.Height <= radius Then Return
                Dim path As New GraphicsPath()
                Dim r As Integer = radius
                path.AddArc(0, 0, r, r, 180, 90)
                path.AddArc(lbl.Width - r, 0, r, r, 270, 90)
                path.AddArc(lbl.Width - r, lbl.Height - r, r, r, 0, 90)
                path.AddArc(0, lbl.Height - r, r, r, 90, 90)
                path.CloseFigure()
                lbl.Region = New Region(path)
            End Sub
        AddHandler lbl.Resize, Sub() apply()
        apply()
    End Sub

    Private Shared Sub PropagateHover(row As Panel)
        AddHandler row.MouseEnter, Sub() SetRowHover(row, True)
        AddHandler row.MouseLeave, Sub() SetRowHover(row, False)
        For Each ctrl As Control In row.Controls
            AddHandler ctrl.MouseEnter, Sub() SetRowHover(row, True)
            AddHandler ctrl.MouseLeave,
                Sub()
                    Dim pos = row.PointToClient(Cursor.Position)
                    SetRowHover(row, row.ClientRectangle.Contains(pos))
                End Sub
        Next
    End Sub

    Private Shared Sub SetRowHover(row As Panel, on_ As Boolean)
        row.Tag = on_
        row.Invalidate()
    End Sub

End Class
