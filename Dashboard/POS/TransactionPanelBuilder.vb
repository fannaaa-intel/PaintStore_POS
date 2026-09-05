Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Windows.Forms
Imports System.Linq
Imports System.Collections.Generic
Imports MySql.Data.MySqlClient

' =============================================================================
'  TransactionPanelBuilder  (PRODUCTION add-to-cart model)
'
'  LEFT  = product catalog: live search + category tabs + scrollable list.
'          Click a product (or its "Add") to put it in the cart. The catalog is
'          read-only; it never mutates the products table.
'  RIGHT = the cart (starts empty) + order summary. Steppers cap at stock, the x
'          button removes a line (memory only). Payment buttons complete the sale
'          (one DB transaction) and then clear the cart for the next customer.
'
'  Out of stock -> shown greyed with an "Out of stock" tag and cannot be added.
'
'  MEDIA (matches InventoryItemListPanelBuilder):
'    - "Paint" category   -> the product is shown as a COLOUR chip (color_hex).
'    - Any other category -> the product is shown as its uploaded PHOTO
'                            (image_path), with a tinted initial as fallback.
'  Both the catalog rows and the cart rows use the SAME media chip, so the POS
'  always reflects whatever was set on the Item List screen.
' =============================================================================
Public Class TransactionPanelBuilder

    ' -- Palette --------------------------------------------------------------
    Private Shared ReadOnly MintDark As Color = Color.FromArgb(10, 79, 66)
    Private Shared ReadOnly MintPrimary As Color = Color.FromArgb(0, 168, 142)
    Private Shared ReadOnly MintBg As Color = Color.FromArgb(244, 248, 246)
    Private Shared ReadOnly CardBg As Color = Color.White
    Private Shared ReadOnly CardBorder As Color = Color.FromArgb(220, 232, 228)
    Private Shared ReadOnly SubText As Color = Color.FromArgb(90, 114, 107)
    Private Shared ReadOnly TextDark As Color = Color.FromArgb(45, 45, 45)
    Private Shared ReadOnly TileBg As Color = Color.FromArgb(236, 248, 244)
    Private Shared ReadOnly SoftBorder As Color = Color.FromArgb(210, 225, 220)
    Private Shared ReadOnly HeaderBg As Color = Color.FromArgb(0, 100, 84)
    Private Shared ReadOnly RowAlt As Color = Color.FromArgb(248, 252, 250)
    Private Shared ReadOnly DangerBack As Color = Color.FromArgb(252, 235, 235)
    Private Shared ReadOnly DangerFore As Color = Color.FromArgb(163, 45, 45)
    Private Shared ReadOnly GoldValue As Color = Color.FromArgb(255, 196, 120)
    Private Shared ReadOnly MutedBg As Color = Color.FromArgb(238, 240, 239)
    Private Shared ReadOnly MutedFore As Color = Color.FromArgb(150, 160, 156)

    ' Thumbnail cache so the same file isn't decoded on every render.
    Private Shared ReadOnly _thumbCache As New Dictionary(Of String, Image)

    ' -- Catalog + cart model -------------------------------------------------
    Private Class ProductInfo
        Public Id As Integer
        Public Name As String
        Public Sku As String
        Public Subtitle As String
        Public Category As String      ' mapped tab category
        Public UnitPrice As Decimal
        Public Stock As Integer
        Public Swatch As Color
        Public ImagePath As String     ' for non-paint items (photo)
        Public ReadOnly Property IsPaint As Boolean
            Get
                Return String.Equals(Category, "Paint", StringComparison.OrdinalIgnoreCase)
            End Get
        End Property
    End Class

    Private Class CartLine
        Public ProductId As Integer
        Public Name As String
        Public UnitPrice As Decimal
        Public Qty As Integer
        Public Stock As Integer
    End Class

    ' -- State (persists across navigation; cleared on complete/hold/recall) ---
    Private Shared _mainContainer As Panel
    Private Shared _allProducts As New List(Of ProductInfo)
    Private Shared _cart As New Dictionary(Of Integer, CartLine)
    Private Shared _searchText As String = ""
    ' Rebuilding the whole catalog on every keystroke made typing stutter on a
    ' large product list; the timer coalesces a burst of keys into one render.
    Private Shared _searchDebounce As Timer
    Private Shared _activeCategory As String = "All"

    Private Shared _catalogHost As FlowLayoutPanel
    Private Shared _cartHost As FlowLayoutPanel
    Private Shared _lblCatalogCount As Label

    ' Payment
    Private Shared _payMethods As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)
    ' Active methods in method_id order, carrying their type/note so the till
    ' reads behaviour from the record instead of guessing from the name.
    Private Shared _payMethodList As New List(Of PaymentMethods.Method)
    Private Shared _payMethodId As Integer = 0
    Private Shared _payMethodName As String = ""
    Private Shared _activePayBtn As Button = Nothing
    Private Shared _activePayNormalClr As Color = Color.Transparent

    ' Discount
    Private Shared _discountAmount As Decimal = 0
    Private Shared _discountType As String = "None"


    ' Quota badge
    Private Shared _lblQuotaBadge As Label
    ' Summary labels
    Private Shared _lblItems As Label
    Private Shared _lblSubtotal As Label
    ' Cart-dependent actions, disabled while the cart is empty.
    Private Shared _btnDiscount As Button
    Private Shared _btnHold As Button
    Private Shared ReadOnly _payButtons As New List(Of Button)
    Private Shared _lblDiscount As Label
    Private Shared _lblVat As Label
    Private Shared _lblGrandTotal As Label

    ' Last completed sale snapshot (for PRINT RECEIPT)
    Private Shared _lastSaleNo As String = ""
    Private Shared _lastSaleAt As DateTime = DateTime.MinValue
    Private Shared _lastSalePay As String = ""
    Private Shared _lastSaleSubtotal As Decimal = 0
    Private Shared _lastSaleDiscount As Decimal = 0
    Private Shared _lastSaleVat As Decimal = 0
    Private Shared _lastSaleTotal As Decimal = 0
    Private Shared _lastSaleTendered As Decimal = 0
    Private Shared _lastSaleChange As Decimal = 0
    Private Shared _lastSaleRef As String = ""
    Private Shared _lastSaleLines As New List(Of (name As String, qty As Integer, price As Decimal, line As Decimal))

    ' =========================================================================
    '  SESSION RESET
    ' =========================================================================
    ' The cart, the discount and the last-sale receipt snapshot are Shared, so
    ' they outlive the form and survive a logout. Without this the next cashier
    ' inherited the previous one's cart, and REPRINT re-issued the previous
    ' cashier's receipt with the NEW cashier's name printed on it.
    Public Shared Sub ResetForNewSession()
        _cart = New Dictionary(Of Integer, CartLine)
        _discountAmount = 0
        _discountType = "None"
        _searchText = ""
        _activeCategory = "All"

        _lastSaleNo = ""
        _lastSaleAt = DateTime.MinValue
        _lastSalePay = ""
        _lastSaleSubtotal = 0
        _lastSaleDiscount = 0
        _lastSaleVat = 0
        _lastSaleTotal = 0
        _lastSaleTendered = 0
        _lastSaleChange = 0
        _lastSaleRef = ""
        _lastSaleLines.Clear()

        ImageCache.ClearAndDispose(_thumbCache, Nothing)
    End Sub

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Shared Sub LoadTransaction(mainContainer As Panel)
        _mainContainer = mainContainer

        ' Per-load UI refs reset (the cart + discount persist across navigation)
        _payMethodId = 0
        _payMethodName = ""
        _activePayBtn = Nothing
        _activePayNormalClr = Color.Transparent
        _lblItems = Nothing : _lblSubtotal = Nothing
        _btnDiscount = Nothing : _btnHold = Nothing
        _payButtons.Clear()
        _lblDiscount = Nothing : _lblVat = Nothing : _lblGrandTotal = Nothing
        _catalogHost = Nothing : _cartHost = Nothing : _lblCatalogCount = Nothing

        ' The search box is rebuilt showing its placeholder, so the filter must
        ' start empty too - otherwise the catalog renders against a stale term.
        _searchText = ""

        ' Drop cached thumbnails so freshly-set images show immediately. The
        ' bitmaps hold GDI+ handles, so they are disposed once this rebuild has
        ' replaced the controls that were still painting them.
        ImageCache.ClearAndDispose(_thumbCache, mainContainer)

        LoadPaymentMethods()
        LoadProducts()
        SyncCartWithCatalog()

        mainContainer.Controls.Clear()
        mainContainer.BackColor = MintBg

        Dim root As New Panel With {.Dock = DockStyle.Fill, .BackColor = MintBg}
        mainContainer.Controls.Add(root)
        root.BringToFront()

        BuildLayout(root)
        RenderCatalog()
        RenderCart()
    End Sub

    Private Shared Sub LoadPaymentMethods()
        ' Only active methods reach the till. Behaviour (cash vs reference) and
        ' the note come from the record via PaymentMethods, which falls back to
        ' name-guessing on an un-migrated database.
        _payMethodList = PaymentMethods.LoadAll(activeOnly:=True)
        _payMethods.Clear()
        For Each m In _payMethodList
            _payMethods(m.Name) = m.MethodId
        Next
    End Sub

    Private Shared Sub LoadProducts()
        _allProducts = New List(Of ProductInfo)
        Try
            Using conn As MySqlConnection = Db.GetConnection()
                conn.Open()
                Dim sql As String =
                    "SELECT p.product_id, p.name, p.sku, p.paint_type, p.size_label, p.color_hex, p.image_path, " &
                    "       p.unit_price, p.stock_qty, c.name AS category_name " &
                    "FROM products p JOIN categories c ON c.category_id = p.category_id " &
                    "WHERE p.is_active = 1 ORDER BY c.name, p.name"
                Using cmd As New MySqlCommand(sql, conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            Dim paintType = If(rdr.IsDBNull(rdr.GetOrdinal("paint_type")), "", rdr.GetString("paint_type"))
                            Dim sizeLabel = If(rdr.IsDBNull(rdr.GetOrdinal("size_label")), "", rdr.GetString("size_label"))
                            Dim colorHex = If(rdr.IsDBNull(rdr.GetOrdinal("color_hex")), "", rdr.GetString("color_hex"))
                            Dim imagePath = If(rdr.IsDBNull(rdr.GetOrdinal("image_path")), "", rdr.GetString("image_path"))
                            Dim catName = rdr.GetString("category_name")
                            Dim sub_ As String = catName
                            If paintType <> "" AndAlso sizeLabel <> "" Then
                                sub_ = paintType & " . " & sizeLabel
                            ElseIf sizeLabel <> "" Then
                                sub_ = sizeLabel
                            ElseIf paintType <> "" Then
                                sub_ = paintType
                            End If
                            _allProducts.Add(New ProductInfo With {
                                .Id = rdr.GetInt32("product_id"),
                                .Name = rdr.GetString("name"),
                                .Sku = If(rdr.IsDBNull(rdr.GetOrdinal("sku")), "", rdr.GetString("sku")),
                                .Subtitle = sub_,
                                .Category = MapToTabCategory(catName),
                                .UnitPrice = rdr.GetDecimal("unit_price"),
                                .Stock = rdr.GetInt32("stock_qty"),
                                .ImagePath = imagePath,
                                .Swatch = ParseSwatchColor(colorHex, catName)})
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            ' image_path may be missing on older databases.
            AppModal.Show("Could not load products:" & Environment.NewLine & ex.Message &
                          Environment.NewLine & Environment.NewLine &
                          "If the error mentions 'image_path', run:" & Environment.NewLine &
                          "ALTER TABLE products ADD COLUMN image_path VARCHAR(255) DEFAULT NULL AFTER color_hex;",
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    ' Keep cart lines consistent with the freshly loaded catalog (price/stock can
    ' change, products can be deactivated). Drop cart lines no longer sellable.
    Private Shared Sub SyncCartWithCatalog()
        If _cart Is Nothing Then _cart = New Dictionary(Of Integer, CartLine)
        Dim byId = _allProducts.ToDictionary(Function(p) p.Id)
        For Each pid In _cart.Keys.ToList()
            If byId.ContainsKey(pid) Then
                Dim p = byId(pid)
                _cart(pid).UnitPrice = p.UnitPrice
                _cart(pid).Stock = p.Stock
                _cart(pid).Name = p.Name
                If _cart(pid).Qty > p.Stock Then _cart(pid).Qty = Math.Max(0, p.Stock)
                If _cart(pid).Qty <= 0 Then _cart.Remove(pid)
            Else
                _cart.Remove(pid)   ' product deactivated/removed
            End If
        Next
    End Sub

    ' =========================================================================
    '  LAYOUT
    ' =========================================================================
    Private Shared Sub BuildLayout(host As Panel)
        Dim outer As New TableLayoutPanel With {
        .Dock = DockStyle.Fill, .BackColor = Color.Transparent,
        .ColumnCount = 1, .RowCount = 4,
        .Padding = New Padding(16, 10, 16, 10),   ' uniform padding all sides
        .Margin = New Padding(0)}
        outer.RowStyles.Add(New RowStyle(SizeType.Absolute, 60))   ' header
        outer.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F)) ' catalog + cart
        outer.RowStyles.Add(New RowStyle(SizeType.Absolute, 72))    ' payment bar
        outer.RowStyles.Add(New RowStyle(SizeType.Absolute, 54))    ' action bar
        outer.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        host.Controls.Add(outer)

        Dim topBar = BuildTopBar() : topBar.Dock = DockStyle.Fill : topBar.Margin = New Padding(0, 0, 0, 8)
        Dim content = BuildContentArea() : content.Dock = DockStyle.Fill : content.Margin = New Padding(0)
        Dim payBar = BuildPaymentBar() : payBar.Dock = DockStyle.Fill : payBar.Margin = New Padding(0, 8, 0, 4)
        Dim actBar = BuildActionBar() : actBar.Dock = DockStyle.Fill : actBar.Margin = New Padding(0, 0, 0, 0)

        outer.Controls.Add(topBar, 0, 0)
        outer.Controls.Add(content, 0, 1)
        outer.Controls.Add(payBar, 0, 2)
        outer.Controls.Add(actBar, 0, 3)
    End Sub

    Private Shared Function BuildTopBar() As Control
        Dim bar As New Panel With {.Dock = DockStyle.Fill, .BackColor = MintDark, .Margin = New Padding(0, 0, 0, 10)}
        AddHandler bar.Paint,
            Sub(s As Object, e As PaintEventArgs)
                Dim g = e.Graphics : g.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect = bar.ClientRectangle : rect.Inflate(-1, -1)
                Using path = Rounded(rect, 10)
                    Using br As New LinearGradientBrush(rect, MintDark, HeaderBg, 0.0F)
                        g.FillPath(br, path)
                    End Using
                End Using
            End Sub
        Dim lblCashier As New Label With {
            .Text = "CASHIER: " & Session.CurrentFullName.ToUpper(),
            .Font = New Font("Segoe UI Semibold", 10), .ForeColor = Color.FromArgb(200, 240, 225),
            .BackColor = Color.Transparent, .AutoSize = True, .Location = New Point(16, 12)}
        Dim lblRegister As New Label With {
            .Text = "REGISTER: 01", .Font = New Font("Segoe UI", 9),
            .ForeColor = Color.FromArgb(140, 210, 195), .BackColor = Color.Transparent,
            .AutoSize = True, .Location = New Point(16, 32)}
        Dim lblDateTime As New Label With {
            .Text = DateTime.Now.ToString("MMM dd, yyyy  |  hh:mm tt"),
            .Font = New Font("Segoe UI", 10), .ForeColor = Color.FromArgb(200, 240, 225),
            .BackColor = Color.Transparent, .AutoSize = True}
        Dim tmr As New Timer With {.Interval = 1000}
        AddHandler tmr.Tick,
            Sub()
                If Not lblDateTime.IsDisposed Then
                    lblDateTime.Text = DateTime.Now.ToString("MMM dd, yyyy  |  hh:mm tt")
                    lblDateTime.Location = New Point(bar.Width - lblDateTime.Width - 18, (bar.Height - lblDateTime.Height) \ 2)
                Else
                    tmr.Stop() : tmr.Dispose()
                End If
            End Sub
        tmr.Start()
        AddHandler bar.Disposed,
            Sub()
                tmr.Stop()
                tmr.Dispose()
            End Sub
        ' Quota badge — only shown when quota is set
        _lblQuotaBadge = New Label With {
            .AutoSize = True, .BackColor = Color.Transparent,
            .Font = New Font("Segoe UI Semibold", 9),
            .ForeColor = Color.White, .Visible = False}
        ' ── Refresh button ────────────────────────────────────────────────
        Dim btnRefresh As New Button With {
            .Text = "↻  Refresh",
            .Size = New Size(100, 34),
            .FlatStyle = FlatStyle.Flat,
            .BackColor = Color.FromArgb(0, 110, 92),
            .ForeColor = Color.FromArgb(200, 240, 225),
            .Font = New Font("Segoe UI", 9.5F),
            .Cursor = Cursors.Hand
        }
        btnRefresh.FlatAppearance.BorderSize = 0
        AddHandler btnRefresh.Paint,
            Sub(s As Object, e As PaintEventArgs)
                Dim b = CType(s, Button)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect As Rectangle = b.ClientRectangle
                rect.Inflate(-1, -1)
                Using path As New GraphicsPath()
                    Dim r = 8
                    path.AddArc(rect.X, rect.Y, r, r, 180, 90)
                    path.AddArc(rect.Right - r, rect.Y, r, r, 270, 90)
                    path.AddArc(rect.Right - r, rect.Bottom - r, r, r, 0, 90)
                    path.AddArc(rect.X, rect.Bottom - r, r, r, 90, 90)
                    path.CloseFigure()
                    Using br As New SolidBrush(b.BackColor)
                        e.Graphics.FillPath(br, path)
                    End Using
                End Using
                TextRenderer.DrawText(e.Graphics, b.Text, b.Font, b.ClientRectangle, b.ForeColor,
                    TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub
        AddHandler btnRefresh.MouseEnter,
            Sub()
                btnRefresh.BackColor = Color.FromArgb(0, 90, 75)
                btnRefresh.ForeColor = Color.White
                btnRefresh.Invalidate()
            End Sub
        AddHandler btnRefresh.MouseLeave,
            Sub()
                btnRefresh.BackColor = Color.FromArgb(0, 110, 92)
                btnRefresh.ForeColor = Color.FromArgb(200, 240, 225)
                btnRefresh.Invalidate()
            End Sub
        AddHandler btnRefresh.Click,
            Sub()
                If _mainContainer IsNot Nothing Then LoadTransaction(_mainContainer)
            End Sub

        bar.Controls.AddRange({lblCashier, lblRegister, lblDateTime, _lblQuotaBadge, btnRefresh})

        Dim PositionBadge As Action =
            Sub()
                _lblQuotaBadge.Location = New Point(
                    bar.Width - lblDateTime.Width - _lblQuotaBadge.Width - 28,
                    (bar.Height - _lblQuotaBadge.Height) \ 2)
            End Sub

        AddHandler bar.Resize,
            Sub()
                lblCashier.Location = New Point(16, 12)
                lblRegister.Location = New Point(16, 32)
                lblDateTime.Location = New Point(bar.Width - lblDateTime.Width - 18, (bar.Height - lblDateTime.Height) \ 2)
                btnRefresh.Left = lblDateTime.Left - btnRefresh.Width - 12
                btnRefresh.Top = (bar.Height - btnRefresh.Height) \ 2
                PositionBadge()
            End Sub
        UpdateQuotaBadge()
        Return bar
    End Function

    ' ── Quota badge updater ───────────────────────────────────────────────────
    Friend Shared Sub UpdateQuotaBadge()
        If _lblQuotaBadge Is Nothing OrElse _lblQuotaBadge.IsDisposed Then Return
        If Session.UserQuota <= 0 Then
            _lblQuotaBadge.Visible = False
            Return
        End If

        ' Get current shift total from DB
        Dim shiftTotal As Decimal = 0
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT COALESCE(SUM(total),0) FROM sales " &
                    "WHERE shift_id=@sid AND status IN ('Completed','Partially Refunded')", conn)
                    cmd.Parameters.AddWithValue("@sid", Session.ShiftId)
                    shiftTotal = CDec(cmd.ExecuteScalar())
                End Using
            End Using
        Catch ex As Exception
            System.Diagnostics.Debug.WriteLine("UpdateQuotaBadge error: " & ex.Message)
            Return
        End Try

        Dim isOvertime As Boolean = shiftTotal >= Session.UserQuota
        _lblQuotaBadge.Text = If(isOvertime, " OVERTIME ", " QUOTA MET ")
        _lblQuotaBadge.BackColor = If(isOvertime,
            Color.FromArgb(220, 80, 50),
            Color.FromArgb(0, 168, 100))
        _lblQuotaBadge.Visible = shiftTotal >= Session.UserQuota
        _lblQuotaBadge.Invalidate()
    End Sub

    Private Shared Function BuildContentArea() As Control
        Dim split As New TableLayoutPanel With {
        .Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 1,
        .BackColor = Color.Transparent,
        .Margin = New Padding(0), .Padding = New Padding(0)}
        split.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 60.0F))
        split.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 40.0F))
        split.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))

        Dim left = BuildCatalogCard() : left.Dock = DockStyle.Fill : left.Margin = New Padding(0, 0, 6, 0)
        Dim right = BuildCartCard() : right.Dock = DockStyle.Fill : right.Margin = New Padding(6, 0, 0, 0)

        split.Controls.Add(left, 0, 0)
        split.Controls.Add(right, 1, 0)
        Return split
    End Function

    ' =========================================================================
    '  LEFT: CATALOG CARD
    ' =========================================================================
    Private Shared Function BuildCatalogCard() As Control
        Dim wrapper As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.Transparent, .Padding = New Padding(0)}

        Dim card As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.Transparent}

        ' AFTER
        AddHandler card.Paint,
    Sub(s As Object, e As PaintEventArgs)
        Dim g = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim rect = card.ClientRectangle
        rect.Inflate(-1, -1)
        Dim radius = 14
        Dim d = radius * 2

        ' Shadow
        Dim shadowRect As New Rectangle(rect.X + 1, rect.Y + 2, rect.Width - 2, rect.Height - 2)
        Using shadowBrush As New SolidBrush(Color.FromArgb(14, 0, 0, 0))
            g.FillRoundedRectangle(shadowBrush, shadowRect, radius)
        End Using

        ' Step 1: fill entire card white (rounded)
        Using fillBrush As New SolidBrush(Color.White)
            g.FillRoundedRectangle(fillBrush, rect, radius)
        End Using

        ' Step 2: overpaint the top header zone with MintDark gradient,
        '         using a rounded-top clipping path that matches the card exactly

        Dim headerHeight = 52
        Dim fullRect = card.ClientRectangle
        fullRect.Inflate(1, 1)   ' bleed 1px under the border so no white gap peeks through
        Using path As New GraphicsPath()
            path.AddArc(fullRect.X, fullRect.Y, d, d, 180, 90)
            path.AddArc(fullRect.Right - d, fullRect.Y, d, d, 270, 90)
            path.AddLine(fullRect.Right, fullRect.Y + headerHeight, fullRect.X, fullRect.Y + headerHeight)
            path.CloseFigure()
            Using br As New LinearGradientBrush(
                New Rectangle(fullRect.X, fullRect.Y, fullRect.Width, headerHeight),
                MintDark, HeaderBg, 0.0F)
                g.FillPath(br, path)
            End Using
        End Using


        Dim bd = radius * 2

        Using pen As New Pen(Color.FromArgb(170, 185, 178), 1.0F)
            Dim bp As New GraphicsPath()
            bp.StartFigure()
            ' Left side — start from top of header zone, no gap
            bp.AddLine(rect.X, rect.Y + headerHeight - 1, rect.X, rect.Bottom - radius)
            ' Bottom-left corner
            bp.AddArc(rect.X, rect.Bottom - bd, bd, bd, 90, 90)
            ' Bottom edge
            bp.AddLine(rect.X + radius, rect.Bottom, rect.Right - radius, rect.Bottom)
            ' Bottom-right corner
            bp.AddArc(rect.Right - bd, rect.Bottom - bd, bd, bd, 0, 90)

            ' Right side — overlap 1px into header zone to close the seam
            bp.AddLine(rect.Right, rect.Bottom - radius, rect.Right, rect.Y + headerHeight - 1)
            g.DrawPath(pen, bp)
        End Using
    End Sub

        Dim layout As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 4,
            .BackColor = Color.Transparent, .Margin = New Padding(0), .Padding = New Padding(0),
            .CellBorderStyle = TableLayoutPanelCellBorderStyle.None}
        layout.RowStyles.Add(New RowStyle(SizeType.Absolute, 52))   ' title
        layout.RowStyles.Add(New RowStyle(SizeType.Absolute, 50))   ' search
        layout.RowStyles.Add(New RowStyle(SizeType.Absolute, 44))   ' tabs
        layout.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F)) ' list

        ' Title bar
        Dim titleRow As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.Transparent}



        AddHandler titleRow.Paint,
    Sub(s, e)
        ' intentionally empty — card.Paint owns the rounded-top gradient header
    End Sub
        Dim lblSection As New Label With {
            .Text = "PRODUCT CATALOG", .Font = New Font("Segoe UI", 12, FontStyle.Bold),
            .ForeColor = Color.White, .BackColor = Color.Transparent, .AutoSize = True}
        _lblCatalogCount = New Label With {
            .Text = "", .Font = New Font("Segoe UI", 9), .ForeColor = Color.FromArgb(170, 220, 205),
            .BackColor = Color.Transparent, .AutoSize = True}
        titleRow.Controls.AddRange({lblSection, _lblCatalogCount})
        AddHandler titleRow.Resize,
                  Sub()
                      lblSection.Location = New Point(16, (titleRow.Height - lblSection.Height) \ 2)
                      _lblCatalogCount.Location = New Point(titleRow.Width - _lblCatalogCount.Width - 16, (titleRow.Height - _lblCatalogCount.Height) \ 2)
                      lblSection.Invalidate()
                      _lblCatalogCount.Invalidate()
                  End Sub
        ' Force initial layout
        titleRow.PerformLayout()

        ' Search row
        Dim searchRow As New Panel With {.Dock = DockStyle.Fill, .BackColor = CardBg, .Padding = New Padding(16, 8, 16, 6)}
        Dim searchBox As New Panel With {.Dock = DockStyle.Fill, .BackColor = TileBg, .Cursor = Cursors.IBeam}
        Dim focused As Boolean = False
        AddHandler searchBox.Paint,
            Sub(s, e)
                Dim g = e.Graphics : g.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect = searchBox.ClientRectangle : rect.Inflate(-1, -1)
                Using br As New SolidBrush(TileBg) : g.FillRoundedRectangle(br, rect, 8) : End Using
                Using p As New Pen(If(focused, MintPrimary, SoftBorder), If(focused, 1.8F, 1.0F)) : g.DrawRoundedRectangle(p, rect, 8) : End Using
                Using pen As New Pen(Color.FromArgb(150, 170, 165), 1.6F)
                    g.DrawEllipse(pen, 12, rect.Height \ 2 - 6, 11, 11)
                    g.DrawLine(pen, 21, rect.Height \ 2 + 4, 26, rect.Height \ 2 + 9)
                End Using
            End Sub
        Dim showingPlaceholder As Boolean = True
        Dim txtSearch As New TextBox With {
            .Font = New Font("Segoe UI", 10.5F), .BorderStyle = BorderStyle.None,
            .BackColor = TileBg, .ForeColor = SubText, .Text = "Search products by name or SKU...",
            .Location = New Point(38, 9), .Width = 300}
        ' The placeholder is drawn by writing it into the TextBox itself, so every
        ' handler below must keep _searchText in sync with the REAL query only -
        ' otherwise the placeholder string leaks into the filter and hides every
        ' product (and a stale term survives after the box is cleared).
        AddHandler txtSearch.Enter,
            Sub()
                focused = True
                If showingPlaceholder Then
                    showingPlaceholder = False
                    txtSearch.Text = "" : txtSearch.ForeColor = TextDark
                End If
                searchBox.Invalidate()
            End Sub
        AddHandler txtSearch.Leave,
            Sub()
                focused = False
                If String.IsNullOrWhiteSpace(txtSearch.Text) Then
                    ' Clear the filter first, then restore the placeholder text.
                    ' Any keystroke still sitting in the debounce window is dropped,
                    ' otherwise it would re-render over this cleared state.
                    If _searchDebounce IsNot Nothing Then _searchDebounce.Stop()
                    _searchText = ""
                    showingPlaceholder = True
                    txtSearch.Text = "Search products by name or SKU..." : txtSearch.ForeColor = SubText
                    RenderCatalog()
                End If
                searchBox.Invalidate()
            End Sub
        If _searchDebounce Is Nothing Then
            _searchDebounce = New Timer With {.Interval = 180}
            AddHandler _searchDebounce.Tick,
                Sub()
                    _searchDebounce.Stop()
                    RenderCatalog()
                End Sub
        End If
        AddHandler txtSearch.TextChanged,
            Sub()
                If showingPlaceholder Then Return ' placeholder showing, not a real query
                _searchText = txtSearch.Text.Trim()
                ' Restart the window on each key: render once the typing pauses.
                _searchDebounce.Stop()
                _searchDebounce.Start()
            End Sub
        ' Enter renders immediately -- a barcode scanner sends its terminator at
        ' speed and should not wait out the debounce window.
        AddHandler txtSearch.KeyDown,
            Sub(s2 As Object, ke As KeyEventArgs)
                If ke.KeyCode = Keys.Enter Then
                    ke.SuppressKeyPress = True
                    _searchDebounce.Stop()
                    _searchText = txtSearch.Text.Trim()
                    RenderCatalog()
                End If
            End Sub
        AddHandler searchBox.Resize, Sub() txtSearch.Location = New Point(38, (searchBox.Height - txtSearch.Height) \ 2)
        AddHandler searchBox.Click, Sub() txtSearch.Focus()
        searchBox.Controls.Add(txtSearch)
        searchRow.Controls.Add(searchBox)

        ' Tabs
        Dim tabHost As New Panel With {.Dock = DockStyle.Fill, .BackColor = TileBg, .Padding = New Padding(10, 6, 10, 4)}
        Dim cats() As String = {"All", "Paint", "Tools", "Thinner", "Accessories"}
        Dim tabBtns(cats.Length - 1) As Button
        Dim selIdx As Integer = Array.IndexOf(cats, _activeCategory) : If selIdx < 0 Then selIdx = 0
        For i = 0 To cats.Length - 1
            Dim idx = i
            Dim b As New Button With {.Text = cats(i), .FlatStyle = FlatStyle.Flat, .Cursor = Cursors.Hand,
                .Margin = New Padding(0), .Height = 30, .Font = New Font("Segoe UI", 9.5F)}
            b.FlatAppearance.BorderSize = 0
            Dim style As Action(Of Boolean, Boolean) =
                Sub(active As Boolean, hover As Boolean)
                    If active Then
                        b.BackColor = MintPrimary : b.ForeColor = Color.White : b.Font = New Font("Segoe UI Semibold", 9.5F)
                    ElseIf hover Then
                        b.BackColor = Color.FromArgb(210, 240, 235) : b.ForeColor = MintDark : b.Font = New Font("Segoe UI", 9.5F)
                    Else
                        b.BackColor = Color.FromArgb(220, 235, 232) : b.ForeColor = SubText : b.Font = New Font("Segoe UI", 9.5F)
                    End If
                    b.Invalidate()
                End Sub
            AddHandler b.Paint,
                Sub(s, e)
                    Dim bb = CType(s, Button) : Dim g = e.Graphics : g.SmoothingMode = SmoothingMode.AntiAlias
                    Dim rect = bb.ClientRectangle : rect.Inflate(-1, -1)
                    Using br As New SolidBrush(bb.BackColor) : g.FillRoundedRectangle(br, rect, 8) : End Using
                    TextRenderer.DrawText(g, bb.Text, bb.Font, bb.ClientRectangle, bb.ForeColor,
                        TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
                End Sub
            AddHandler b.MouseEnter, Sub() If idx <> selIdx Then style(False, True)
            AddHandler b.MouseLeave, Sub() If idx <> selIdx Then style(False, False)
            AddHandler b.Click,
                Sub()
                    Dim prev = selIdx : selIdx = idx
                    If tabBtns(prev) IsNot Nothing Then
                        tabBtns(prev).BackColor = Color.FromArgb(220, 235, 232)
                        tabBtns(prev).ForeColor = SubText
                        tabBtns(prev).Font = New Font("Segoe UI", 9.5F)
                        tabBtns(prev).Invalidate()
                    End If
                    style(True, False)
                    _activeCategory = cats(idx)
                    RenderCatalog()
                End Sub
            style(i = selIdx, False)
            tabBtns(i) = b
            tabHost.Controls.Add(b)
        Next
        AddHandler tabHost.Resize,
            Sub()
                Dim gap = 6
                Dim total = tabHost.Width - tabHost.Padding.Horizontal
                Dim bw = (total - gap * (tabBtns.Length - 1)) \ tabBtns.Length
                Dim xx = tabHost.Padding.Left
                For Each tb In tabBtns
                    tb.SetBounds(xx, tabHost.Padding.Top, bw, tabHost.Height - tabHost.Padding.Vertical)
                    xx += bw + gap
                Next
            End Sub

        ' List
        Dim listHost As New FlowLayoutPanel With {
    .Dock = DockStyle.Fill, .FlowDirection = FlowDirection.TopDown, .WrapContents = False,
    .AutoScroll = True, .BackColor = Color.Transparent, .Padding = New Padding(10, 10, 8, 10)}
        AddHandler listHost.Resize,
            Sub()
                For Each c As Control In listHost.Controls
                    c.Width = listHost.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4
                Next
            End Sub
        _catalogHost = listHost

        AddHandler titleRow.HandleCreated,
            Sub()
                lblSection.Location = New Point(16, (titleRow.Height - lblSection.Height) \ 2)
                _lblCatalogCount.Location = New Point(titleRow.Width - _lblCatalogCount.Width - 16, (titleRow.Height - _lblCatalogCount.Height) \ 2)
            End Sub
        layout.Controls.Add(titleRow, 0, 0)
        layout.Controls.Add(searchRow, 0, 1)
        layout.Controls.Add(tabHost, 0, 2)
        layout.Controls.Add(listHost, 0, 3)
        card.Controls.Add(layout)
        wrapper.Controls.Add(card)
        Return wrapper
    End Function

    ' =========================================================================
    '  CATALOG RENDER + ROWS
    ' =========================================================================
    Private Shared Sub RenderCatalog()
        If _catalogHost Is Nothing Then Return
        _catalogHost.SuspendLayout()
        _catalogHost.Controls.Clear()

        Dim q As String = If(_searchText, "").ToLower()
        Dim shown As Integer = 0
        Dim n As Integer = 1
        For Each p In _allProducts
            If _activeCategory <> "All" AndAlso p.Category <> _activeCategory Then Continue For
            ' SKU is matched as well as the name so a scanned or typed barcode
            ' finds the item -- searching name-only made the scanner useless.
            If q <> "" AndAlso Not (p.Name.ToLower().Contains(q) OrElse
                                    p.Subtitle.ToLower().Contains(q) OrElse
                                    If(p.Sku, "").ToLower().Contains(q)) Then Continue For
            _catalogHost.Controls.Add(CreateCatalogRow(p, n))
            n += 1 : shown += 1
        Next

        If shown = 0 Then
            _catalogHost.Controls.Add(New Label With {
                .Text = "No products match your search.", .ForeColor = SubText,
                .Font = New Font("Segoe UI", 10), .AutoSize = False,
                .Height = 40, .Width = _catalogHost.ClientSize.Width - 20,
                .TextAlign = ContentAlignment.MiddleCenter})
        End If

        ' width fix
        For Each c As Control In _catalogHost.Controls
            c.Width = _catalogHost.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4
        Next
        If _lblCatalogCount IsNot Nothing Then
            _lblCatalogCount.Text = shown & " item(s)"
            If _lblCatalogCount.Parent IsNot Nothing Then
                _lblCatalogCount.Location = New Point(_lblCatalogCount.Parent.Width - _lblCatalogCount.Width - 16,
                                                      (_lblCatalogCount.Parent.Height - _lblCatalogCount.Height) \ 2)
            End If
        End If
        _catalogHost.ResumeLayout()
    End Sub

    Private Shared Function CreateCatalogRow(p As ProductInfo, rowNum As Integer) As Panel
        Dim outOfStock As Boolean = (p.Stock <= 0)
        Dim alt As Boolean = (rowNum Mod 2 = 0)
        Dim row As New Panel With {
            .Height = 72, .Width = 700, .Margin = New Padding(0, 0, 0, 8),
            .BackColor = If(alt, RowAlt, CardBg), .Cursor = If(outOfStock, Cursors.Default, Cursors.Hand)}
        AddHandler row.Paint,
            Sub(s, e)
                Dim g = e.Graphics : g.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect = row.ClientRectangle : rect.Inflate(-1, -1)
                Using br As New SolidBrush(row.BackColor) : g.FillRoundedRectangle(br, rect, 9) : End Using
                Using pen As New Pen(CardBorder, 1.0F) : g.DrawRoundedRectangle(pen, rect, 9) : End Using
            End Sub

        ' Media chip: colour for paint, photo for everything else.
        Dim media As Panel = BuildMediaChip(p, 38, outOfStock)
        media.Location = New Point(12, 17)

        Dim lblName As New Label With {
            .Text = p.Name, .Font = New Font("Segoe UI Semibold", 10.5F),
            .ForeColor = If(outOfStock, MutedFore, MintDark), .AutoSize = False,
            .BackColor = Color.Transparent, .Location = New Point(60, 16), .Size = New Size(260, 22)}
        ' SKU is searchable, so it is shown on the row too -- same "   .   "
        ' separator and 8.5pt SubText treatment as the rest of the subtitle,
        ' and simply omitted when the product has no SKU.
        Dim subTxt As String = p.Subtitle
        If Not String.IsNullOrEmpty(p.Sku) Then subTxt &= "   .   " & p.Sku
        subTxt &= "   .   Stock: " & p.Stock
        Dim lblSub As New Label With {
            .Text = subTxt, .Font = New Font("Segoe UI", 8.5F),
            .ForeColor = If(outOfStock, MutedFore, SubText), .AutoSize = False,
            .BackColor = Color.Transparent, .Location = New Point(60, 40), .Size = New Size(330, 16)}

        Dim lblPrice As New Label With {
            .Text = "P " & p.UnitPrice.ToString("N2"), .Font = New Font("Segoe UI Semibold", 10.5F),
            .ForeColor = If(outOfStock, MutedFore, Color.FromArgb(50, 70, 65)), .AutoSize = False,
            .TextAlign = ContentAlignment.MiddleRight, .BackColor = Color.Transparent}

        ' Add button / out-of-stock tag
        Dim addBtn As New Button With {
            .Text = If(outOfStock, "OUT OF STOCK", "ADD  +"),
            .FlatStyle = FlatStyle.Flat, .Font = New Font("Segoe UI Semibold", 9),
            .ForeColor = If(outOfStock, MutedFore, Color.White),
            .BackColor = If(outOfStock, MutedBg, MintPrimary),
            .Cursor = If(outOfStock, Cursors.Default, Cursors.Hand), .Enabled = Not outOfStock}
        addBtn.FlatAppearance.BorderSize = 0
        AddHandler addBtn.Paint,
            Sub(s, e)
                Dim bb = CType(s, Button) : Dim g = e.Graphics : g.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect = bb.ClientRectangle : rect.Inflate(-1, -1)
                Using br As New SolidBrush(bb.BackColor) : g.FillRoundedRectangle(br, rect, 7) : End Using
                TextRenderer.DrawText(g, bb.Text, bb.Font, bb.ClientRectangle, bb.ForeColor,
                    TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub
        If Not outOfStock Then
            AddHandler addBtn.MouseEnter,
                Sub()
                    addBtn.BackColor = MintDark
                    addBtn.Invalidate()
                End Sub
            AddHandler addBtn.MouseLeave,
                Sub()
                    addBtn.BackColor = MintPrimary
                    addBtn.Invalidate()
                End Sub
            Dim addNow As Action = Sub() AddToCart(p.Id)
            AddHandler addBtn.Click, Sub() addNow()
            AddHandler row.Click, Sub() addNow()
            AddHandler lblName.Click, Sub() addNow()
            AddHandler lblSub.Click, Sub() addNow()
            AddHandler media.Click, Sub() addNow()
        End If

        row.Controls.AddRange({media, lblName, lblSub, lblPrice, addBtn})
        AddHandler row.Resize,
    Sub()
        lblPrice.SetBounds(row.Width - 230, 0, 110, row.Height)
        addBtn.SetBounds(row.Width - 116, (row.Height - 36) \ 2, 100, 36)
    End Sub
        Return row
    End Function

    Private Shared Sub AddToCart(pid As Integer)
        Dim p = _allProducts.FirstOrDefault(Function(x) x.Id = pid)
        If p Is Nothing Then Return
        If p.Stock <= 0 Then Return   ' blocked (UI already shows it greyed)

        If _cart.ContainsKey(pid) Then
            If _cart(pid).Qty < p.Stock Then
                _cart(pid).Qty += 1
            Else
                Return ' at stock cap — silently ignore
            End If
        Else
            _cart(pid) = New CartLine With {.ProductId = pid, .Name = p.Name,
                .UnitPrice = p.UnitPrice, .Qty = 1, .Stock = p.Stock}
        End If
        RenderCart()
    End Sub

    ' =========================================================================
    '  RIGHT: CART CARD
    ' =========================================================================
    Private Shared Function BuildCartCard() As Control
        Dim card As New Panel With {.Dock = DockStyle.Fill, .BackColor = CardBg, .Margin = New Padding(0)}



        AddHandler card.Paint,
    Sub(s As Object, e As PaintEventArgs)
        Dim g = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim rect = card.ClientRectangle
        rect.Inflate(-1, -1)
        Dim radius = 14
        Dim d = radius * 2

        ' Shadow
        Dim shadowRect As New Rectangle(rect.X + 1, rect.Y + 2, rect.Width - 2, rect.Height - 2)
        Using shadowBrush As New SolidBrush(Color.FromArgb(14, 0, 0, 0))
            g.FillRoundedRectangle(shadowBrush, shadowRect, radius)
        End Using

        ' Fill entire card white (rounded)
        Using fillBrush As New SolidBrush(Color.White)
            g.FillRoundedRectangle(fillBrush, rect, radius)
        End Using

        ' Rounded-top header zone
        Dim headerHeight = 52
        Dim fullRect = card.ClientRectangle
        fullRect.Inflate(1, 1)
        Using path As New GraphicsPath()
            path.AddArc(fullRect.X, fullRect.Y, d, d, 180, 90)
            path.AddArc(fullRect.Right - d, fullRect.Y, d, d, 270, 90)
            path.AddLine(fullRect.Right, fullRect.Y + headerHeight, fullRect.X, fullRect.Y + headerHeight)
            path.CloseFigure()
            Using br As New LinearGradientBrush(
                New Rectangle(fullRect.X, fullRect.Y, fullRect.Width, headerHeight),
                MintDark, HeaderBg, 0.0F)
                g.FillPath(br, path)
            End Using
        End Using

        ' Border: left + bottom + right only (skip top — covered by rounded header)
        Using pen As New Pen(Color.FromArgb(170, 185, 178), 1.0F)
            Dim bp As New GraphicsPath()
            bp.StartFigure()
            bp.AddLine(rect.X, rect.Y + headerHeight - 1, rect.X, rect.Bottom - radius)
            bp.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90)
            bp.AddLine(rect.X + radius, rect.Bottom, rect.Right - radius, rect.Bottom)
            bp.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90)
            bp.AddLine(rect.Right, rect.Bottom - radius, rect.Right, rect.Y + headerHeight - 1)
            g.DrawPath(pen, bp)
        End Using
    End Sub

        Dim layout As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 3,
            .BackColor = Color.Transparent, .Margin = New Padding(0), .Padding = New Padding(0),
            .CellBorderStyle = TableLayoutPanelCellBorderStyle.None}
        layout.RowStyles.Add(New RowStyle(SizeType.Absolute, 52))
        layout.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
        layout.RowStyles.Add(New RowStyle(SizeType.Absolute, 200))

        ' Header
        Dim header As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.Transparent}


        AddHandler header.Paint,
    Sub(s, e)
        ' intentionally empty — card.Paint owns the rounded-top gradient header
    End Sub
        Dim lblTitle As New Label With {
            .Text = "CART", .Font = New Font("Segoe UI Semibold", 11, FontStyle.Bold),
            .ForeColor = Color.White, .BackColor = Color.Transparent, .AutoSize = True}
        Dim lblCount As New Label With {
            .Text = "0 items", .Font = New Font("Segoe UI", 9), .ForeColor = Color.FromArgb(170, 220, 205),
            .BackColor = Color.Transparent, .AutoSize = True, .Name = "cartCount"}
        header.Controls.AddRange({lblTitle, lblCount})
        AddHandler header.Resize,
            Sub()
                lblTitle.Location = New Point(16, (header.Height - lblTitle.Height) \ 2)
                lblCount.Location = New Point(header.Width - lblCount.Width - 16, (header.Height - lblCount.Height) \ 2)
            End Sub

        ' Cart list
        Dim listHost As New FlowLayoutPanel With {
            .Dock = DockStyle.Fill, .FlowDirection = FlowDirection.TopDown, .WrapContents = False,
            .AutoScroll = True, .BackColor = Color.Transparent, .Padding = New Padding(10, 10, 8, 10)}
        AddHandler listHost.Resize,
    Sub()
        Dim availW = listHost.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 6
        For Each c As Control In listHost.Controls
            c.Width = availW
        Next
    End Sub
        _cartHost = listHost

        ' Summary
        Dim summary As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.Transparent, .Padding = New Padding(16, 6, 16, 10)}
        Dim sgrid As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 5, .BackColor = Color.Transparent}
        sgrid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 55.0F))
        sgrid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 45.0F))
        sgrid.RowStyles.Add(New RowStyle(SizeType.Absolute, 26))
        sgrid.RowStyles.Add(New RowStyle(SizeType.Absolute, 26))
        sgrid.RowStyles.Add(New RowStyle(SizeType.Absolute, 26))
        sgrid.RowStyles.Add(New RowStyle(SizeType.Absolute, 26))
        sgrid.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))

        _lblItems = MakeStat(sgrid, 0, "TOTAL ITEMS", "0")
        _lblSubtotal = MakeStat(sgrid, 1, "SUBTOTAL", "P 0.00")
        _lblDiscount = MakeStat(sgrid, 2, "DISCOUNT", "P 0.00")
        _lblVat = MakeStat(sgrid, 3, StoreSettings.VatLabel, "P 0.00")

        Dim totalBlock As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.Transparent, .Padding = New Padding(0, 8, 0, 0)}
        Dim totalCard As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.Transparent}
        AddHandler totalCard.Paint,
            Sub(s, e)
                Dim g = e.Graphics : g.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect As New Rectangle(1, 1, totalCard.Width - 2, totalCard.Height - 2)
                Using br As New LinearGradientBrush(rect, MintDark, Color.FromArgb(0, 60, 50), 90.0F) : g.FillRoundedRectangle(br, rect, 13) : End Using
            End Sub
        Dim lblTotL As New Label With {.Text = "TOTAL", .Dock = DockStyle.Left, .Width = 90,
            .TextAlign = ContentAlignment.MiddleLeft, .Font = New Font("Segoe UI", 13, FontStyle.Bold),
            .ForeColor = Color.FromArgb(200, 240, 225), .BackColor = Color.Transparent, .Padding = New Padding(14, 0, 0, 0)}
        _lblGrandTotal = New Label With {.Text = "P 0.00", .Dock = DockStyle.Fill,
            .TextAlign = ContentAlignment.MiddleRight, .Font = New Font("Segoe UI", 20, FontStyle.Bold),
            .ForeColor = GoldValue, .BackColor = Color.Transparent, .Padding = New Padding(0, 0, 14, 0)}
        totalCard.Controls.Add(_lblGrandTotal)
        totalCard.Controls.Add(lblTotL)
        totalBlock.Controls.Add(totalCard)
        sgrid.SetColumnSpan(totalBlock, 2)
        sgrid.Controls.Add(totalBlock, 0, 4)
        summary.Controls.Add(sgrid)

        layout.Controls.Add(header, 0, 0)
        layout.Controls.Add(listHost, 0, 1)
        layout.Controls.Add(summary, 0, 2)
        card.Controls.Add(layout)
        Return card
    End Function

    Private Shared Function MakeStat(grid As TableLayoutPanel, row As Integer, label As String, value As String) As Label
        Dim l As New Label With {.Text = label, .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleLeft,
            .Font = New Font("Segoe UI", 10), .ForeColor = SubText, .BackColor = Color.Transparent}
        Dim v As New Label With {.Text = value, .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleRight,
            .Font = New Font("Segoe UI Semibold", 11), .ForeColor = Color.FromArgb(50, 70, 65), .BackColor = Color.Transparent}
        grid.Controls.Add(l, 0, row)
        grid.Controls.Add(v, 1, row)
        Return v
    End Function

    ' =========================================================================
    '  CART RENDER + ROWS
    ' =========================================================================
    Private Shared Sub RenderCart()
        If _cartHost IsNot Nothing Then
            _cartHost.SuspendLayout()
            _cartHost.Controls.Clear()
            If _cart.Count = 0 Then
                _cartHost.Controls.Add(New Label With {
                    .Text = "Cart is empty." & Environment.NewLine & "Add products from the catalog.",
                    .ForeColor = SubText, .Font = New Font("Segoe UI", 10), .AutoSize = False,
                    .Height = 60, .Width = _cartHost.ClientSize.Width - 20, .TextAlign = ContentAlignment.MiddleCenter})
            Else
                For Each kv In _cart
                    _cartHost.Controls.Add(CreateCartRow(kv.Value))
                Next
            End If
            For Each c As Control In _cartHost.Controls
                c.Width = _cartHost.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4
            Next
            _cartHost.ResumeLayout()

            ' cart count label
            Dim hdr = _cartHost.Parent
            If hdr IsNot Nothing Then
                For Each c As Control In hdr.Controls
                    For Each cc As Control In c.Controls
                        If cc.Name = "cartCount" Then cc.Text = _cart.Count & " item(s)"
                    Next
                Next
            End If
        End If

        ' Summary
        Dim items As Integer = 0
        Dim subtotal As Decimal = 0
        For Each kv In _cart
            items += kv.Value.Qty
            subtotal += kv.Value.Qty * kv.Value.UnitPrice
        Next
        Dim disc As Decimal = _discountAmount
        If disc < 0 Then disc = 0
        If disc > subtotal Then disc = subtotal
        ' VAT-exclusive pricing adds the tax on top of the net amount; inclusive
        ' pricing already contains it. ComputeTotalDue keeps the displayed total,
        ' the amount charged and the receipt in agreement under both settings.
        Dim net As Decimal = subtotal - disc
        Dim vat As Decimal = StoreSettings.ComputeVat(net)
        Dim grand As Decimal = StoreSettings.ComputeTotalDue(net)
        If _lblItems IsNot Nothing Then _lblItems.Text = items.ToString()
        If _lblSubtotal IsNot Nothing Then _lblSubtotal.Text = "P " & subtotal.ToString("N2")
        If _lblDiscount IsNot Nothing Then _lblDiscount.Text = "- P " & disc.ToString("N2")
        If _lblVat IsNot Nothing Then _lblVat.Text = "P " & vat.ToString("N2")
        If _lblGrandTotal IsNot Nothing Then _lblGrandTotal.Text = "P " & grand.ToString("N2")

        ' HOLD and DISCOUNT only mean something with a cart behind them. Greying
        ' them out shows that up front rather than answering a click with a
        ' warning dialog.
        Dim hasCart As Boolean = (items > 0)
        If _btnDiscount IsNot Nothing Then _btnDiscount.Enabled = hasCart
        If _btnHold IsNot Nothing Then _btnHold.Enabled = hasCart
        For Each pb In _payButtons
            pb.Enabled = hasCart
        Next
    End Sub

    Private Shared Function CreateCartRow(line As CartLine) As Panel
        Dim row As New Panel With {.Height = 72, .Width = 360, .Margin = New Padding(0, 0, 0, 8), .BackColor = CardBg}
        AddHandler row.Paint,
        Sub(s, e)
            Dim g = e.Graphics : g.SmoothingMode = SmoothingMode.AntiAlias
            Dim rect = row.ClientRectangle : rect.Inflate(-1, -1)
            Using br As New SolidBrush(TileBg) : g.FillRoundedRectangle(br, rect, 9) : End Using
            Using pen As New Pen(Color.FromArgb(159, 225, 203), 1.2F) : g.DrawRoundedRectangle(pen, rect, 9) : End Using
        End Sub

        ' Media chip (same source as the catalog/Item List).
        Dim p = _allProducts.FirstOrDefault(Function(x) x.Id = line.ProductId)
        Dim media As Panel = Nothing
        If p IsNot Nothing Then
            media = BuildMediaChip(p, 34, False)
            media.Location = New Point(12, 19)
        End If
        Dim textX As Integer = If(media IsNot Nothing, 54, 12)

        ' LEFT: name + unit price + line total stacked
        Dim lblName As New Label With {
        .Text = line.Name,
        .Font = New Font("Segoe UI Semibold", 10),
        .ForeColor = MintDark, .AutoSize = False,
        .BackColor = Color.Transparent,
        .TextAlign = ContentAlignment.MiddleLeft,
         .Location = New Point(textX, 10), .Size = New Size(220, 22)}

        Dim lblUnit As New Label With {
    .Text = "P " & line.UnitPrice.ToString("N2") & "  ×  " & line.Qty & "  =  P " & (line.UnitPrice * line.Qty).ToString("N2"),
        .Font = New Font("Segoe UI", 8.5F),
        .ForeColor = SubText, .AutoSize = False,
        .BackColor = Color.Transparent,
        .TextAlign = ContentAlignment.MiddleLeft,
           .Location = New Point(textX, 36), .Size = New Size(220, 18)}

        ' Stepper
        Dim stepPanel As New Panel With {.Size = New Size(88, 26), .BackColor = CardBg}
        AddHandler stepPanel.Paint,
        Sub(s, e)
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
            Dim rect = stepPanel.ClientRectangle : rect.Inflate(-1, -1)
            Using br As New SolidBrush(CardBg) : e.Graphics.FillRoundedRectangle(br, rect, 7) : End Using
            Using p2 As New Pen(SoftBorder, 1.2F) : e.Graphics.DrawRoundedRectangle(p2, rect, 7) : End Using
        End Sub

        Dim btnMinus As New Button With {
        .Text = "−", .Size = New Size(26, 26), .Location = New Point(0, 0),
        .FlatStyle = FlatStyle.Flat,
        .Font = New Font("Segoe UI", 11), .ForeColor = SubText,
        .BackColor = Color.Transparent, .Cursor = Cursors.Hand}
        btnMinus.FlatAppearance.BorderSize = 0

        Dim lblQty As New Label With {
        .Text = line.Qty.ToString(),
        .Size = New Size(34, 26), .Location = New Point(27, 0),
        .TextAlign = ContentAlignment.MiddleCenter,
        .Font = New Font("Segoe UI Semibold", 10.5F),
        .ForeColor = MintDark, .BackColor = Color.Transparent}

        Dim btnPlus As New Button With {
        .Text = "+", .Size = New Size(26, 26), .Location = New Point(62, 0),
        .FlatStyle = FlatStyle.Flat,
        .Font = New Font("Segoe UI", 11), .ForeColor = MintPrimary,
        .BackColor = Color.Transparent, .Cursor = Cursors.Hand}
        btnPlus.FlatAppearance.BorderSize = 0

        AddHandler btnMinus.Click, Sub() SetQty(line.ProductId, line.Qty - 1)
        AddHandler btnPlus.Click, Sub() SetQty(line.ProductId, line.Qty + 1)
        stepPanel.Controls.AddRange({btnMinus, lblQty, btnPlus})

        ' Delete button
        Dim btnDel As New Button With {
        .Text = "×", .Size = New Size(24, 24),
        .FlatStyle = FlatStyle.Flat,
        .Font = New Font("Segoe UI", 10, FontStyle.Bold),
        .ForeColor = DangerFore, .BackColor = DangerBack,
        .Cursor = Cursors.Hand}
        btnDel.FlatAppearance.BorderSize = 0
        AddHandler btnDel.Paint,
        Sub(s, e)
            Dim bb = CType(s, Button) : Dim g = e.Graphics : g.SmoothingMode = SmoothingMode.AntiAlias
            Using br As New SolidBrush(bb.BackColor) : g.FillEllipse(br, 0, 0, bb.Width - 1, bb.Height - 1) : End Using
            TextRenderer.DrawText(g, bb.Text, bb.Font, bb.ClientRectangle, bb.ForeColor,
                TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
        End Sub
        AddHandler btnDel.MouseEnter,
        Sub()
            btnDel.BackColor = DangerFore
            btnDel.ForeColor = Color.White
            btnDel.Invalidate()
        End Sub
        AddHandler btnDel.MouseLeave,
        Sub()
            btnDel.BackColor = DangerBack
            btnDel.ForeColor = DangerFore
            btnDel.Invalidate()
        End Sub
        AddHandler btnDel.Click, Sub() RemoveFromCart(line.ProductId)

        If media IsNot Nothing Then row.Controls.Add(media)
        row.Controls.AddRange({lblName, lblUnit, stepPanel, btnDel})

        Dim DoLayout As Action =
    Sub()
        Dim rightPad = 12
        Dim delX = row.Width - rightPad - btnDel.Width
        Dim stepX = delX - 10 - stepPanel.Width

        lblName.SetBounds(textX, 10, stepX - textX - 6, 22)
        lblUnit.SetBounds(textX, 36, stepX - textX - 6, 18)
        stepPanel.Location = New Point(stepX, (row.Height - stepPanel.Height) \ 2)
        btnDel.Location = New Point(delX, (row.Height - btnDel.Height) \ 2)
    End Sub

        AddHandler row.Resize, Sub() DoLayout()
        AddHandler row.HandleCreated, Sub() DoLayout()

        Return row
    End Function

    Private Shared Sub SetQty(pid As Integer, newQty As Integer)
        If Not _cart.ContainsKey(pid) Then Return
        Dim line = _cart(pid)
        If newQty <= 0 Then
            _cart.Remove(pid)
        Else
            If newQty > line.Stock Then newQty = line.Stock   ' cap at available
            line.Qty = newQty
        End If
        RenderCart()
    End Sub

    Private Shared Sub RemoveFromCart(pid As Integer)
        If _cart.ContainsKey(pid) Then _cart.Remove(pid)
        RenderCart()
    End Sub

    Private Shared Function CartSubtotal() As Decimal
        Dim s As Decimal = 0
        For Each kv In _cart
            s += kv.Value.Qty * kv.Value.UnitPrice
        Next
        Return s
    End Function

    ' =========================================================================
    '  MEDIA CHIP + THUMBNAIL HELPERS  (shared by catalog + cart)
    ' =========================================================================
    ' Rounded chip: solid colour for paint, clipped photo for everything else,
    ' tinted initial tile as the universal fallback. Mirrors the Item List.
    Private Shared Function BuildMediaChip(p As ProductInfo, size As Integer, outOfStock As Boolean) As Panel
        Dim box As New Panel With {.Size = New Size(size, size), .BackColor = Color.Transparent,
                                   .Cursor = If(outOfStock, Cursors.Default, Cursors.Hand)}
        Dim img As Image = If(p.IsPaint, Nothing, GetThumb(p.ImagePath, size))
        Dim chipColor As Color = If(outOfStock AndAlso p.IsPaint, Color.FromArgb(200, 205, 203), p.Swatch)
        Dim initial As String = If(String.IsNullOrEmpty(p.Name), "?", p.Name.Substring(0, 1).ToUpperInvariant())

        AddHandler box.Paint,
            Sub(s, e)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim r As New Rectangle(0, 0, size - 1, size - 1)
                Using path = Rounded(r, 9)
                    If p.IsPaint Then
                        Using br As New SolidBrush(chipColor) : g.FillPath(br, path) : End Using
                    ElseIf img IsNot Nothing Then
                        Dim oldClip = g.Clip
                        g.SetClip(path, CombineMode.Replace)
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic
                        g.DrawImage(img, r)
                        g.Clip = oldClip
                    Else
                        Using br As New SolidBrush(TileBg) : g.FillPath(br, path) : End Using
                        Using f As New Font("Segoe UI", CSng(size * 0.4), FontStyle.Bold), br As New SolidBrush(MintPrimary)
                            Dim sz = g.MeasureString(initial, f)
                            g.DrawString(initial, f, br, (size - sz.Width) / 2, (size - sz.Height) / 2)
                        End Using
                    End If
                    Using pen As New Pen(Color.FromArgb(60, 0, 0, 0), 1.0F) : g.DrawPath(pen, path) : End Using
                End Using
            End Sub
        Return box
    End Function

    ' Decode an image without locking the file, scaled and cached.
    Private Shared Function GetThumb(path As String, maxSize As Integer) As Image
        If String.IsNullOrEmpty(path) Then Return Nothing
        ' image_path may be relative (current) or an absolute path baked to
        ' another machine (older rows); resolve both to something openable.
        path = ImageCache.ToFullPath(path)
        Dim key = path & "|" & maxSize
        If _thumbCache.ContainsKey(key) Then Return _thumbCache(key)
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
                    _thumbCache(key) = bmp
                    Return bmp
                End Using
            End Using
        Catch
            Return Nothing
        End Try
    End Function

    ' =========================================================================
    '  PAYMENT BAR + ACTION BAR
    ' =========================================================================
    Private Shared Function BuildPaymentBar() As Control
        Dim host As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.Transparent, .Padding = New Padding(0)}

        ' One button per payment method, built from the payment_methods table
        ' (in method_id order) rather than a fixed three. A method the admin adds
        ' in Store Settings therefore shows up here for the cashier, and a removed
        ' one disappears - the row is no longer hardcoded to Cash / Card / E-Wallet.
        Dim names As New List(Of String)
        For Each m In _payMethodList
            names.Add(m.Name)
        Next
        If names.Count = 0 Then
            ' No active methods configured: keep the till usable with a single
            ' Cash button, matching the old default, rather than an empty bar.
            names = New List(Of String) From {"Cash"}
        End If

        Dim count As Integer = names.Count
        Dim layout As New TableLayoutPanel With {.Dock = DockStyle.Fill, .RowCount = 1,
        .ColumnCount = count, .BackColor = Color.Transparent, .Margin = New Padding(0), .Padding = New Padding(0)}
        For i = 0 To count - 1
            layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F / count))
        Next
        layout.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))

        For i = 0 To count - 1
            Dim nm As String = names(i)
            Dim clr = PayColorFor(nm, i)
            Dim leftMargin As Integer = If(i = 0, 0, 4)
            layout.Controls.Add(MakePayBtn(nm.ToUpper(), nm, clr.normal, clr.hover, leftMargin), i, 0)
        Next

        host.Controls.Add(layout)
        Return host
    End Function

    ' Colour for a payment button. Cash / Card / E-Wallet keep the exact greens,
    ' blues and purples the till has always used, matched on content so a rename
    ' like "Cash on Hand" still reads as cash. Anything else cycles a small
    ' neutral-but-distinct palette by position, so newly added methods look
    ' deliberate rather than all identical.
    Private Shared Function PayColorFor(name As String, index As Integer) As (normal As Color, hover As Color)
        Dim n As String = If(name, "").ToLower()
        If n.IndexOf("cash") >= 0 Then Return (Color.FromArgb(0, 148, 74), Color.FromArgb(0, 100, 55))
        If n.IndexOf("card") >= 0 Then Return (Color.FromArgb(30, 100, 190), Color.FromArgb(20, 70, 140))
        If n.IndexOf("wallet") >= 0 OrElse n.IndexOf("gcash") >= 0 OrElse n.IndexOf("maya") >= 0 Then
            Return (Color.FromArgb(100, 50, 180), Color.FromArgb(70, 30, 140))
        End If
        ' Fallback palette for any other method, indexed so two new methods do
        ' not collide on the same colour.
        Dim palette = {
            (Color.FromArgb(196, 120, 20), Color.FromArgb(150, 90, 12)),
            (Color.FromArgb(30, 140, 150), Color.FromArgb(20, 100, 110)),
            (Color.FromArgb(150, 60, 90), Color.FromArgb(110, 40, 66)),
            (Color.FromArgb(90, 110, 40), Color.FromArgb(64, 80, 26))
        }
        Dim p = palette(index Mod palette.Length)
        Return (p.Item1, p.Item2)
    End Function

    Private Shared Function MakePayBtn(text As String, dbName As String, normalClr As Color, hoverClr As Color, leftMargin As Integer) As Button
        Dim btn As New Button With {.Text = text, .Font = New Font("Segoe UI Semibold", 11), .FlatStyle = FlatStyle.Flat,
            .BackColor = normalClr, .ForeColor = Color.White, .Cursor = Cursors.Hand, .Margin = New Padding(leftMargin, 0, 0, 0), .Dock = DockStyle.Fill}
        btn.FlatAppearance.BorderSize = 0
        AddHandler btn.Paint,
            Sub(s, e)
                Dim b = CType(s, Button) : Dim g = e.Graphics : g.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect = b.ClientRectangle : rect.Inflate(-1, -1)
                ' Same Muted treatment the action bar uses, so an empty cart reads
                ' the same way across the whole till.
                Dim fill As Color = If(b.Enabled, b.BackColor, MutedBg)
                Using br As New SolidBrush(fill) : g.FillRoundedRectangle(br, rect, 10) : End Using
                TextRenderer.DrawText(g, b.Text, b.Font, b.ClientRectangle,
                    If(b.Enabled, Color.White, MutedFore),
                    TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub
        AddHandler btn.MouseEnter,
            Sub()
                If btn.Enabled AndAlso btn IsNot _activePayBtn Then
                    btn.BackColor = hoverClr
                    btn.Invalidate()
                End If
            End Sub
        AddHandler btn.EnabledChanged,
            Sub()
                btn.Cursor = If(btn.Enabled, Cursors.Hand, Cursors.Default)
                If btn IsNot _activePayBtn Then btn.BackColor = normalClr
                btn.Invalidate()
            End Sub
        AddHandler btn.MouseLeave,
            Sub()
                If btn IsNot _activePayBtn Then
                    btn.BackColor = normalClr
                    btn.Invalidate()
                End If
            End Sub
        _payButtons.Add(btn)
        AddHandler btn.Click,
            Sub()
                If _cart.Count = 0 Then
                    AppModal.Show("Add at least one item to the cart before taking payment.",
                        "Empty Cart", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    Return
                End If
                Dim mid As Integer = 0
                If Not _payMethods.TryGetValue(dbName, mid) OrElse mid = 0 Then
                    AppModal.Show("Payment method """ & dbName & """ was not found in the database.",
                        "Configuration Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
                    Return
                End If
                If _activePayBtn IsNot Nothing Then _activePayBtn.BackColor = _activePayNormalClr : _activePayBtn.Invalidate()
                _activePayBtn = btn : _activePayNormalClr = normalClr
                btn.BackColor = hoverClr : btn.Invalidate()
                _payMethodId = mid : _payMethodName = dbName

                Dim subtotal As Decimal = CartSubtotal()
                Dim disc As Decimal = _discountAmount
                If disc < 0 Then disc = 0
                If disc > subtotal Then disc = subtotal
                ' Charge the VAT-adjusted total, not the net -- see ComputeTotalDue.
                Dim grand As Decimal = StoreSettings.ComputeTotalDue(subtotal - disc)

                ' Per-method capture: cash needs the amount tendered (-> change);
                ' everything else needs a reference number (no change given).
                Dim tendered As Decimal = grand
                Dim changeDue As Decimal = 0
                Dim refNo As String = ""

                ' Cash vs reference now comes from the method's TYPE, not its
                ' name: a method typed 'Cash' asks for the amount tendered and
                ' gives change, everything else asks for a reference number. This
                ' fixes 'GCash' - whose name contains 'cash' - being wrongly
                ' treated as physical cash. On an un-migrated database the type
                ' falls back to the (gcash-aware) name guess inside PaymentMethods.
                Dim payIsCash As Boolean = False
                For Each pm In _payMethodList
                    If String.Equals(pm.Name, dbName, StringComparison.OrdinalIgnoreCase) Then
                        payIsCash = pm.IsCash
                        Exit For
                    End If
                Next
                If payIsCash Then
                    Do
                        Dim entry As String = PaymentPromptModal.Show("Cash", grand, PaymentPromptModal.FieldKind.AmountTendered, grand.ToString("0.00"))
                        If entry Is Nothing Then Return   ' cashier cancelled
                        Dim parsed As Decimal
                        If Not Decimal.TryParse(entry.Trim(), parsed) Then
                            AppModal.Show("Please enter a valid amount (numbers only).",
                                "Invalid Amount", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                        ElseIf parsed < grand Then
                            AppModal.Show(String.Format("Amount tendered (P {0:N2}) is less than the total (P {1:N2}).",
                                parsed, grand), "Insufficient Amount", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                        Else
                            tendered = parsed
                            changeDue = parsed - grand
                            Exit Do
                        End If
                    Loop
                Else
                    Do
                        Dim entry As String = PaymentPromptModal.Show(dbName, grand, PaymentPromptModal.FieldKind.ReferenceNumber, "")
                        If entry Is Nothing Then Return   ' cashier cancelled
                        If entry.Trim().Length = 0 Then
                            AppModal.Show("A reference number is required for " & dbName & " payments.",
                                "Reference Required", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                        Else
                            refNo = entry.Trim()
                            Exit Do
                        End If
                    Loop
                End If

                If ConfirmSaleModal.Show(dbName, grand, tendered, changeDue, refNo) Then
                    CompleteSale(grand, tendered, changeDue, refNo)
                End If
            End Sub
        Return btn
    End Function

    Private Shared Function BuildActionBar() As Control
        Dim host As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.Transparent, .Padding = New Padding(0)}
        Dim layout As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 5, .RowCount = 1,
            .BackColor = Color.Transparent, .Margin = New Padding(0), .Padding = New Padding(0)}
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 16.0F))
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 16.0F))
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 18.0F))
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 18.0F))
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 32.0F))
        layout.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))

        Dim btnVoid = MakeActionBtn("VOID", DangerBack, DangerFore, 0)
        Dim btnDiscount = MakeActionBtn("DISCOUNT", Color.FromArgb(232, 238, 237), SubText, 6)
        Dim btnHold = MakeActionBtn("HOLD", Color.FromArgb(232, 238, 237), SubText, 6)
        Dim btnRetrieve = MakeActionBtn("RETRIEVE", Color.FromArgb(232, 238, 237), SubText, 6)
        Dim btnPrint = MakePrimaryBtn("REPRINT", 6)
        _btnDiscount = btnDiscount
        _btnHold = btnHold
        AddHandler btnVoid.Click, Sub() VoidSale()
        AddHandler btnDiscount.Click, Sub() ApplyDiscount()
        AddHandler btnHold.Click, Sub() HoldSale()
        AddHandler btnRetrieve.Click, Sub() RetrieveSale()
        AddHandler btnPrint.Click, Sub() PrintReceipt()
        layout.Controls.Add(btnVoid, 0, 0)
        layout.Controls.Add(btnDiscount, 1, 0)
        layout.Controls.Add(btnHold, 2, 0)
        layout.Controls.Add(btnRetrieve, 3, 0)
        layout.Controls.Add(btnPrint, 4, 0)
        host.Controls.Add(layout)
        Return host
    End Function

    Private Shared Function MakeActionBtn(text As String, backClr As Color, foreClr As Color, leftMargin As Integer) As Button
        Dim btn As New Button With {.Text = text, .Font = New Font("Segoe UI Semibold", 9.5F), .FlatStyle = FlatStyle.Flat,
            .BackColor = backClr, .ForeColor = foreClr, .Cursor = Cursors.Hand, .Margin = New Padding(leftMargin, 0, 0, 0), .Dock = DockStyle.Fill}
        btn.FlatAppearance.BorderSize = 0
        AddHandler btn.Paint,
            Sub(s, e)
                Dim b = CType(s, Button) : Dim g = e.Graphics : g.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect = b.ClientRectangle : rect.Inflate(-1, -1)
                ' Disabled actions use the existing Muted tokens, so an unavailable
                ' button is legible at a glance instead of only failing on click.
                Dim fill As Color = If(b.Enabled, b.BackColor, MutedBg)
                Dim fore As Color = If(b.Enabled, b.ForeColor, MutedFore)
                Using br As New SolidBrush(fill) : g.FillRoundedRectangle(br, rect, 8) : End Using
                Using p As New Pen(Color.FromArgb(40, fore.R, fore.G, fore.B), 1.0F) : g.DrawRoundedRectangle(p, rect, 8) : End Using
                TextRenderer.DrawText(g, b.Text, b.Font, b.ClientRectangle, fore,
                    TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub
        Dim orig = backClr
        Dim hover = Color.FromArgb(Math.Max(0, orig.R - 18), Math.Max(0, orig.G - 18), Math.Max(0, orig.B - 18))
        AddHandler btn.MouseEnter,
            Sub()
                If Not btn.Enabled Then Return
                btn.BackColor = hover
                btn.Invalidate()
            End Sub
        AddHandler btn.MouseLeave,
            Sub()
                If Not btn.Enabled Then Return
                btn.BackColor = orig
                btn.Invalidate()
            End Sub
        ' Repaint on enable/disable and switch the cursor with it.
        AddHandler btn.EnabledChanged,
            Sub()
                btn.Cursor = If(btn.Enabled, Cursors.Hand, Cursors.Default)
                btn.BackColor = orig
                btn.Invalidate()
            End Sub
        Return btn
    End Function

    Private Shared Function MakePrimaryBtn(text As String, leftMargin As Integer) As Button
        Dim btn As New Button With {.Text = text, .Font = New Font("Segoe UI Semibold", 11, FontStyle.Bold), .FlatStyle = FlatStyle.Flat,
            .BackColor = MintPrimary, .ForeColor = Color.White, .Cursor = Cursors.Hand, .Margin = New Padding(leftMargin, 0, 0, 0), .Dock = DockStyle.Fill}
        btn.FlatAppearance.BorderSize = 0
        AddHandler btn.Paint,
            Sub(s, e)
                Dim b = CType(s, Button) : Dim g = e.Graphics : g.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect = b.ClientRectangle : rect.Inflate(-1, -1)
                If b.Enabled Then
                    Using br As New LinearGradientBrush(rect, MintPrimary, MintDark, 90.0F) : g.FillRoundedRectangle(br, rect, 8) : End Using
                Else
                    Using br As New SolidBrush(MutedBg) : g.FillRoundedRectangle(br, rect, 8) : End Using
                End If
                TextRenderer.DrawText(g, b.Text, b.Font, b.ClientRectangle,
                    If(b.Enabled, Color.White, MutedFore),
                    TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub
        AddHandler btn.MouseEnter,
            Sub()
                If Not btn.Enabled Then Return
                btn.BackColor = MintDark
                btn.Invalidate()
            End Sub
        AddHandler btn.MouseLeave,
            Sub()
                If Not btn.Enabled Then Return
                btn.BackColor = MintPrimary
                btn.Invalidate()
            End Sub
        AddHandler btn.EnabledChanged,
            Sub()
                btn.Cursor = If(btn.Enabled, Cursors.Hand, Cursors.Default)
                btn.BackColor = MintPrimary
                btn.Invalidate()
            End Sub
        Return btn
    End Function

    ' =========================================================================
    '  PAINT + COLOUR HELPERS
    ' =========================================================================
    Private Shared Sub PaintCard(card As Control, e As PaintEventArgs)
        PanelRenderer.Draw(CType(card, Panel), e, True)
    End Sub

    Private Shared Function Rounded(r As Rectangle, radius As Integer) As GraphicsPath
        Dim d = radius * 2
        Dim p As New GraphicsPath()
        p.AddArc(r.X, r.Y, d, d, 180, 90)
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90)
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90)
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90)
        p.CloseFigure()
        Return p
    End Function

    Private Shared Function ParseSwatchColor(hex As String, category As String) As Color
        If Not String.IsNullOrWhiteSpace(hex) Then
            Try
                Dim clean = hex.Trim().TrimStart("#"c)
                If clean.Length = 6 Then
                    Return Color.FromArgb(
                        Convert.ToInt32(clean.Substring(0, 2), 16),
                        Convert.ToInt32(clean.Substring(2, 2), 16),
                        Convert.ToInt32(clean.Substring(4, 2), 16))
                End If
            Catch
            End Try
        End If
        Select Case category.Trim().ToLower()
            Case "paint", "paints" : Return Color.FromArgb(150, 205, 225)
            Case "tools", "tool" : Return Color.FromArgb(160, 165, 175)
            Case "thinner", "thinners", "solvent", "solvents" : Return Color.FromArgb(235, 220, 140)
            Case "accessories", "accessory" : Return Color.FromArgb(145, 190, 170)
            Case Else : Return Color.FromArgb(200, 210, 205)
        End Select
    End Function

    Private Shared Function MapToTabCategory(dbCategory As String) As String
        Select Case dbCategory.Trim().ToLower()
            Case "paint", "paints" : Return "Paint"
            Case "tools", "tool" : Return "Tools"
            Case "thinner", "thinners", "solvent", "solvents" : Return "Thinner"
            Case "accessories", "accessory" : Return "Accessories"
            Case Else : Return dbCategory
        End Select
    End Function

    ' =========================================================================
    '  COMPLETE SALE (one DB transaction) -> then clears the cart
    ' =========================================================================
    Private Shared Sub CompleteSale(grandTotal As Decimal,
                                    amountTendered As Decimal,
                                    changeDue As Decimal,
                                    referenceNo As String)
        Dim items As New List(Of (pid As Integer, pname As String, qty As Integer, price As Decimal, lineTotal As Decimal))
        For Each kv In _cart
            If kv.Value.Qty > 0 Then
                items.Add((kv.Value.ProductId, kv.Value.Name, kv.Value.Qty, kv.Value.UnitPrice, kv.Value.Qty * kv.Value.UnitPrice))
            End If
        Next
        If items.Count = 0 Then
            AppModal.Show("No valid items in the cart.", "Cannot Complete Sale", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        Dim saleNo As String = DocNumber.Generate("TXN-")
        Dim subtotal As Decimal = 0
        For Each it In items : subtotal += it.lineTotal : Next
        Dim discount As Decimal = _discountAmount
        If discount < 0 Then discount = 0
        If discount > subtotal Then discount = subtotal
        ' The customer agreed to grandTotal at the confirmation dialog and, for
        ' cash, the change was computed from it. That figure is therefore the
        ' authoritative one to store -- recomputing here instead would let the
        ' recorded sale drift from what was actually collected if the cart or
        ' the VAT settings changed between confirming and committing.
        Dim netAmount As Decimal = subtotal - discount
        Dim total As Decimal = grandTotal

        ' A mismatch means the cart moved under the cashier after they confirmed;
        ' abandon rather than charge one amount and record another.
        If Math.Abs(total - StoreSettings.ComputeTotalDue(netAmount)) > 0.01D Then
            AppModal.Show("The cart changed while the payment was being confirmed." & Environment.NewLine &
                          "The sale was not completed. Please review the cart and try again.",
                          "Sale Not Completed", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        Try
            Using conn As MySqlConnection = Db.GetConnection()
                conn.Open()
                Using txn As MySqlTransaction = conn.BeginTransaction()
                    Try
                        For Each item In items
                            Using cmd As New MySqlCommand("SELECT stock_qty FROM products WHERE product_id = @pid FOR UPDATE", conn, txn)
                                cmd.Parameters.AddWithValue("@pid", item.pid)
                                Dim avail As Integer = Convert.ToInt32(cmd.ExecuteScalar())
                                If avail < item.qty Then
                                    Throw New Exception(String.Format("Insufficient stock for '{0}'.  Available: {1}, Requested: {2}.", item.pname, avail, item.qty))
                                End If
                            End Using
                        Next

                        Using cmd As New MySqlCommand(
                            "INSERT INTO sales (sale_no, user_id, shift_id, method_id, sale_datetime, status, discount_type, " &
                            "subtotal, discount_amount, vat_amount, total, amount_tendered, change_due, reference_no) " &
                        "VALUES (@sno, @uid, @sid, @mid, NOW(), 'Completed', @dtype, @sub, @disc, @vat, @total, @tendered, @change, @ref)", conn, txn)
                            cmd.Parameters.AddWithValue("@sno", saleNo)
                            cmd.Parameters.AddWithValue("@uid", Session.CurrentUserId)
                            cmd.Parameters.AddWithValue("@sid", If(Session.ShiftId > 0, CType(Session.ShiftId, Object), CType(DBNull.Value, Object)))
                            cmd.Parameters.AddWithValue("@mid", _payMethodId)
                            cmd.Parameters.AddWithValue("@dtype", _discountType)
                            cmd.Parameters.AddWithValue("@sub", subtotal)
                            cmd.Parameters.AddWithValue("@disc", discount)
                            ' VAT is always derived from the NET amount. With exclusive
                            ' pricing `total` already carries the tax, so computing from
                            ' it would report VAT on top of VAT.
                            cmd.Parameters.AddWithValue("@vat", StoreSettings.ComputeVat(netAmount))
                            cmd.Parameters.AddWithValue("@total", total)
                            cmd.Parameters.AddWithValue("@tendered", amountTendered)
                            cmd.Parameters.AddWithValue("@change", changeDue)
                            cmd.Parameters.AddWithValue("@ref",
                                If(String.IsNullOrEmpty(referenceNo), CType(DBNull.Value, Object), CType(referenceNo, Object)))
                            cmd.ExecuteNonQuery()
                        End Using

                        Dim saleId As Long
                        Using cmd As New MySqlCommand("SELECT LAST_INSERT_ID()", conn, txn)
                            saleId = Convert.ToInt64(cmd.ExecuteScalar())
                        End Using

                        For Each item In items
                            Using cmd As New MySqlCommand(
                                "INSERT INTO sale_items (sale_id, product_id, formula_id, description, qty, unit_price, line_total) " &
                                "VALUES (@sid, @pid, NULL, @desc, @qty, @price, @ltotal)", conn, txn)
                                cmd.Parameters.AddWithValue("@sid", saleId)
                                cmd.Parameters.AddWithValue("@pid", item.pid)
                                cmd.Parameters.AddWithValue("@desc", item.pname)
                                cmd.Parameters.AddWithValue("@qty", item.qty)
                                cmd.Parameters.AddWithValue("@price", item.price)
                                cmd.Parameters.AddWithValue("@ltotal", item.lineTotal)
                                cmd.ExecuteNonQuery()
                            End Using
                            Using cmd As New MySqlCommand(
                                "INSERT INTO stock_movements (product_id, user_id, movement_type, qty_change, created_at) " &
                                "VALUES (@pid, @uid, 'Sale', @qc, NOW())", conn, txn)
                                cmd.Parameters.AddWithValue("@pid", item.pid)
                                cmd.Parameters.AddWithValue("@uid", Session.CurrentUserId)
                                cmd.Parameters.AddWithValue("@qc", -item.qty)
                                cmd.ExecuteNonQuery()
                            End Using
                            Using cmd As New MySqlCommand("UPDATE products SET stock_qty = stock_qty - @qty WHERE product_id = @pid", conn, txn)
                                cmd.Parameters.AddWithValue("@qty", item.qty)
                                cmd.Parameters.AddWithValue("@pid", item.pid)
                                cmd.ExecuteNonQuery()
                            End Using
                        Next

                        txn.Commit()
                        UpdateQuotaBadge()

                    Catch ex As Exception
                        txn.Rollback()
                        Throw
                    End Try
                End Using
            End Using

            ' ── Post-commit: snapshot, UI, print ──────────────────────────
            ' These run OUTSIDE the transaction so a UI/print error can
            ' never trigger a Rollback on an already-committed transaction.
            _lastSaleNo = saleNo
            _lastSaleAt = DateTime.Now
            _lastSalePay = _payMethodName
            _lastSaleSubtotal = subtotal
            _lastSaleDiscount = discount
            _lastSaleVat = StoreSettings.ComputeVat(netAmount)
            _lastSaleTotal = total
            _lastSaleTendered = amountTendered
            _lastSaleChange = changeDue
            _lastSaleRef = referenceNo
            _lastSaleLines = items.Select(Function(it) (it.pname, it.qty, it.price, it.lineTotal)).ToList()

            SaleCompleteModal.Show(_payMethodName, saleNo, items.Count, total)
            PrintReceipt()

            ' Fresh cart for the next customer
            _cart = New Dictionary(Of Integer, CartLine)
            _discountAmount = 0
            _discountType = "None"
            LoadTransaction(_mainContainer)

        Catch ex As Exception
            AppModal.Show("The sale could not be completed:" & Environment.NewLine & Environment.NewLine & ex.Message,
                "Sale Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    ' =========================================================================
    '  DISCOUNT
    ' =========================================================================
    Private Shared Sub ApplyDiscount()
        Dim subtotal As Decimal = CartSubtotal()
        If subtotal <= 0 Then
            AppModal.Show("Add items to the cart before applying a discount.", "Nothing to Discount", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        Dim r = PromptDiscount(subtotal, _discountType, _discountAmount)
        If Not r.ok Then Return
        _discountType = r.dtype
        _discountAmount = r.amount
        RenderCart()
    End Sub

    Private Shared Function PromptDiscount(subtotal As Decimal, currentType As String, currentAmount As Decimal) As (ok As Boolean, dtype As String, amount As Decimal)
        Using f As New Form With {.Text = "Apply Discount", .FormBorderStyle = FormBorderStyle.FixedDialog,
                .StartPosition = FormStartPosition.CenterParent, .MaximizeBox = False, .MinimizeBox = False,
                .ClientSize = New Size(340, 250), .BackColor = CardBg, .Font = New Font("Segoe UI", 9.5F)}
            Dim lblSub As New Label With {.Text = "Subtotal:  P " & subtotal.ToString("N2"), .Location = New Point(18, 16), .AutoSize = True, .Font = New Font("Segoe UI Semibold", 10), .ForeColor = MintDark}
            Dim lblType As New Label With {.Text = "Type", .Location = New Point(18, 52), .AutoSize = True, .ForeColor = SubText}
            Dim cboType As New ComboBox With {.Location = New Point(18, 72), .Width = 140, .DropDownStyle = ComboBoxStyle.DropDownList}
            cboType.Items.AddRange({"None", "Senior", "PWD", "Promo"})
            cboType.SelectedItem = If(cboType.Items.Contains(currentType), currentType, "None")
            Dim rdoPct As New RadioButton With {.Text = "Percent (%)", .Location = New Point(180, 54), .AutoSize = True, .Checked = True}
            Dim rdoFlat As New RadioButton With {.Text = "Flat (P)", .Location = New Point(180, 78), .AutoSize = True}
            Dim lblVal As New Label With {.Text = "Value", .Location = New Point(18, 108), .AutoSize = True, .ForeColor = SubText}
            Dim txtVal As New TextBox With {.Location = New Point(18, 128), .Width = 140, .Text = "0"}
            Dim lblPreview As New Label With {.Location = New Point(18, 162), .AutoSize = True, .Font = New Font("Segoe UI Semibold", 10), .ForeColor = MintPrimary, .Text = "Discount:  P 0.00"}
            Dim resolve As Func(Of Decimal) =
                Function() As Decimal
                    Dim v As Decimal = 0
                    Decimal.TryParse(txtVal.Text.Replace("P", "").Replace(",", "").Trim(), v)
                    If v < 0 Then v = 0
                    Dim amt As Decimal = If(rdoPct.Checked, subtotal * v / 100D, v)
                    If amt > subtotal Then amt = subtotal
                    Return Decimal.Round(amt, 2)
                End Function
            Dim upd As Action = Sub() lblPreview.Text = "Discount:  P " & resolve().ToString("N2")
            AddHandler txtVal.TextChanged, Sub() upd()
            AddHandler rdoPct.CheckedChanged, Sub() upd()
            AddHandler rdoFlat.CheckedChanged, Sub() upd()
            AddHandler cboType.SelectedIndexChanged,
                Sub()
                    Dim t = CStr(cboType.SelectedItem)
                    If t = "Senior" OrElse t = "PWD" Then
                        rdoPct.Checked = True : txtVal.Text = "20"
                    ElseIf t = "None" Then
                        txtVal.Text = "0"
                    End If
                    upd()
                End Sub
            Dim btnOk As New Button With {.Text = "Apply", .DialogResult = DialogResult.OK, .Location = New Point(176, 196),
                .Size = New Size(72, 30), .FlatStyle = FlatStyle.Flat, .BackColor = MintPrimary, .ForeColor = Color.White}
            btnOk.FlatAppearance.BorderSize = 0
            Dim btnCancel As New Button With {.Text = "Cancel", .DialogResult = DialogResult.Cancel, .Location = New Point(254, 196),
                .Size = New Size(72, 30), .FlatStyle = FlatStyle.Flat, .BackColor = Color.FromArgb(232, 238, 237), .ForeColor = SubText}
            btnCancel.FlatAppearance.BorderSize = 0
            f.Controls.AddRange({lblSub, lblType, cboType, rdoPct, rdoFlat, lblVal, txtVal, lblPreview, btnOk, btnCancel})
            f.AcceptButton = btnOk : f.CancelButton = btnCancel
            If currentAmount > 0 Then rdoFlat.Checked = True : txtVal.Text = currentAmount.ToString("0.##")
            upd()
            If f.ShowDialog() <> DialogResult.OK Then Return (False, currentType, currentAmount)
            Dim finalAmt As Decimal = resolve()
            Dim finalType As String = CStr(cboType.SelectedItem)
            If finalAmt > 0 AndAlso finalType = "None" Then finalType = "Promo"
            If finalAmt = 0 Then finalType = "None"
            Return (True, finalType, finalAmt)
        End Using
    End Function

    ' =========================================================================
    '  HOLD / RETRIEVE / VOID
    ' =========================================================================
    Private Shared Sub HoldSale()
        Dim items As New List(Of (pid As Integer, pname As String, qty As Integer, price As Decimal, lineTotal As Decimal))
        For Each kv In _cart
            If kv.Value.Qty > 0 Then items.Add((kv.Value.ProductId, kv.Value.Name, kv.Value.Qty, kv.Value.UnitPrice, kv.Value.Qty * kv.Value.UnitPrice))
        Next
        If items.Count = 0 Then
            AppModal.Show("There is nothing in the cart to hold.", "Cannot Hold", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If
        Dim subtotal As Decimal = 0
        For Each it In items : subtotal += it.lineTotal : Next
        Dim discount As Decimal = _discountAmount
        If discount < 0 Then discount = 0
        If discount > subtotal Then discount = subtotal
        ' Mirror the checkout maths so the parked figures match what the sale will
        ' actually charge when it is retrieved.
        Dim netAmount As Decimal = subtotal - discount
        Dim total As Decimal = StoreSettings.ComputeTotalDue(netAmount)
        Dim saleNo As String = DocNumber.Generate("HOLD-")
        Try
            Using conn As MySqlConnection = Db.GetConnection()
                conn.Open()
                Using txn As MySqlTransaction = conn.BeginTransaction()
                    Try
                        Using cmd As New MySqlCommand(
                            "INSERT INTO sales (sale_no, user_id, shift_id, method_id, sale_datetime, status, discount_type, " &
                            "subtotal, discount_amount, vat_amount, total, amount_tendered, change_due) " &
                            "VALUES (@sno, @uid, @sid, NULL, NOW(), 'OnHold', @dtype, @sub, @disc, @vat, @total, NULL, NULL)", conn, txn)
                            cmd.Parameters.AddWithValue("@sno", saleNo)
                            cmd.Parameters.AddWithValue("@uid", Session.CurrentUserId)
                            cmd.Parameters.AddWithValue("@sid", If(Session.ShiftId > 0, CType(Session.ShiftId, Object), CType(DBNull.Value, Object)))
                            cmd.Parameters.AddWithValue("@dtype", _discountType)
                            cmd.Parameters.AddWithValue("@sub", subtotal)
                            cmd.Parameters.AddWithValue("@disc", discount)
                            cmd.Parameters.AddWithValue("@vat", StoreSettings.ComputeVat(netAmount))
                            cmd.Parameters.AddWithValue("@total", total)
                            cmd.ExecuteNonQuery()
                        End Using
                        Dim saleId As Long
                        Using cmd As New MySqlCommand("SELECT LAST_INSERT_ID()", conn, txn)
                            saleId = Convert.ToInt64(cmd.ExecuteScalar())
                        End Using
                        For Each item In items
                            Using cmd As New MySqlCommand(
                                "INSERT INTO sale_items (sale_id, product_id, formula_id, description, qty, unit_price, line_total) " &
                                "VALUES (@sid, @pid, NULL, @desc, @qty, @price, @ltotal)", conn, txn)
                                cmd.Parameters.AddWithValue("@sid", saleId)
                                cmd.Parameters.AddWithValue("@pid", item.pid)
                                cmd.Parameters.AddWithValue("@desc", item.pname)
                                cmd.Parameters.AddWithValue("@qty", item.qty)
                                cmd.Parameters.AddWithValue("@price", item.price)
                                cmd.Parameters.AddWithValue("@ltotal", item.lineTotal)
                                cmd.ExecuteNonQuery()
                            End Using
                        Next
                        txn.Commit()
                    Catch
                        txn.Rollback()
                        Throw
                    End Try
                End Using
            End Using
            AppModal.Show("Sale held as " & saleNo & "." & Environment.NewLine & "Use RETRIEVE to bring it back.",
                "Sale Held", MessageBoxButtons.OK, MessageBoxIcon.Information)
            _cart = New Dictionary(Of Integer, CartLine)
            _discountAmount = 0
            _discountType = "None"
            LoadTransaction(_mainContainer)
        Catch ex As Exception
            AppModal.Show("Could not hold the sale:" & Environment.NewLine & Environment.NewLine & ex.Message,
                "Hold Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    Private Shared Sub RetrieveSale()
        Dim held As New List(Of (id As Long, no As String, total As Decimal, dt As DateTime))
        Try
            Using conn As MySqlConnection = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand("SELECT sale_id, sale_no, total, sale_datetime FROM sales WHERE status = 'OnHold' AND user_id = @uid ORDER BY sale_datetime DESC", conn)
                    cmd.Parameters.AddWithValue("@uid", Session.CurrentUserId)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            held.Add((rdr.GetInt64("sale_id"), rdr.GetString("sale_no"), rdr.GetDecimal("total"), rdr.GetDateTime("sale_datetime")))
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load held sales:" & Environment.NewLine & ex.Message, "Retrieve Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try
        If held.Count = 0 Then
            AppModal.Show("There are no held sales to retrieve.", "Nothing on Hold", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        Dim chosenId As Long = PromptPickSale("Retrieve Held Sale",
            held.Select(Function(h) (h.id, h.no & "   P " & h.total.ToString("N2") & "   " & h.dt.ToString("MMM d, h:mm tt"))).ToList())
        If chosenId <= 0 Then Return

        ' The held row carries the discount that was in force when it was parked;
        ' dropping it here silently re-prices the sale at full price.
        Dim qtyByPid As New Dictionary(Of Integer, Integer)
        Dim heldDiscount As Decimal = 0
        Dim heldDiscountType As String = "None"
        Try
            Using conn As MySqlConnection = Db.GetConnection()
                conn.Open()
                ' One transaction: the row is only deleted once its lines have been
                ' read back. Reading and deleting on separate statements meant a
                ' failure between the two destroyed the held sale with nothing to
                ' restore it from.
                Using txn As MySqlTransaction = conn.BeginTransaction()
                    Try
                        ' Lock the row and confirm it is still on hold — the picker
                        ' list was built before the dialog, so another till may have
                        ' already retrieved or voided this sale.
                        Using cmd As New MySqlCommand(
                            "SELECT status, discount_amount, discount_type FROM sales " &
                            "WHERE sale_id = @sid FOR UPDATE", conn, txn)
                            cmd.Parameters.AddWithValue("@sid", chosenId)
                            Using rdr = cmd.ExecuteReader()
                                If Not rdr.Read() Then
                                    Throw New Exception("That held sale no longer exists.")
                                End If
                                Dim liveStatus As String = rdr.GetString("status")
                                If liveStatus <> "OnHold" Then
                                    Throw New Exception(
                                        "That sale is now marked '" & liveStatus &
                                        "' and can no longer be retrieved.")
                                End If
                                If Not rdr.IsDBNull(rdr.GetOrdinal("discount_amount")) Then
                                    heldDiscount = rdr.GetDecimal("discount_amount")
                                End If
                                If Not rdr.IsDBNull(rdr.GetOrdinal("discount_type")) Then
                                    heldDiscountType = rdr.GetString("discount_type")
                                End If
                            End Using
                        End Using

                        Using cmd As New MySqlCommand("SELECT product_id, qty FROM sale_items WHERE sale_id = @sid AND product_id IS NOT NULL", conn, txn)
                            cmd.Parameters.AddWithValue("@sid", chosenId)
                            Using rdr = cmd.ExecuteReader()
                                While rdr.Read()
                                    qtyByPid(rdr.GetInt32("product_id")) = rdr.GetInt32("qty")
                                End While
                            End Using
                        End Using

                        Using cmd As New MySqlCommand("DELETE FROM sales WHERE sale_id = @sid", conn, txn)
                            cmd.Parameters.AddWithValue("@sid", chosenId)
                            cmd.ExecuteNonQuery()
                        End Using
                        txn.Commit()
                    Catch
                        txn.Rollback()
                        Throw
                    End Try
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load the held sale:" & Environment.NewLine & ex.Message, "Retrieve Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try

        ' Rebuild the cart from the held quantities (clamped to current stock).
        ' Stock can have been sold down while the sale sat on hold, so anything
        ' that no longer fits is reported rather than silently dropped.
        _cart = New Dictionary(Of Integer, CartLine)
        Dim shortfalls As New List(Of String)
        For Each kv In qtyByPid
            Dim p = _allProducts.FirstOrDefault(Function(x) x.Id = kv.Key)
            If p Is Nothing Then
                shortfalls.Add("Product #" & kv.Key & " is no longer available (" & kv.Value & " dropped)")
                Continue For
            End If
            Dim qn As Integer = kv.Value
            If qn > p.Stock Then
                shortfalls.Add(p.Name & ": held " & kv.Value & ", only " & p.Stock & " in stock")
                qn = p.Stock
            End If
            If qn > 0 Then
                _cart(kv.Key) = New CartLine With {.ProductId = kv.Key, .Name = p.Name, .UnitPrice = p.UnitPrice, .Qty = qn, .Stock = p.Stock}
            End If
        Next

        ' Restore the parked discount, re-clamped to the cart it now applies to.
        Dim newSubtotal As Decimal = 0
        For Each kv In _cart : newSubtotal += kv.Value.Qty * kv.Value.UnitPrice : Next
        If heldDiscount < 0 Then heldDiscount = 0
        If heldDiscount > newSubtotal Then heldDiscount = newSubtotal
        _discountAmount = heldDiscount
        _discountType = If(heldDiscount > 0, heldDiscountType, "None")

        LoadTransaction(_mainContainer)

        If shortfalls.Count > 0 Then
            AppModal.Show("Held sale loaded, but stock changed while it was on hold:" &
                Environment.NewLine & Environment.NewLine & String.Join(Environment.NewLine, shortfalls),
                "Retrieved with Changes", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        Else
            AppModal.Show("Held sale loaded into the cart.", "Retrieved", MessageBoxButtons.OK, MessageBoxIcon.Information)
        End If
    End Sub

    Private Shared Sub VoidSale()
        Dim candidates As New List(Of (id As Long, no As String, total As Decimal, status As String, dt As DateTime))
        Try
            Using conn As MySqlConnection = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand("SELECT sale_id, sale_no, total, status, sale_datetime FROM sales WHERE status IN ('Completed','OnHold') AND user_id = @uid ORDER BY sale_datetime DESC LIMIT 50", conn)
                    cmd.Parameters.AddWithValue("@uid", Session.CurrentUserId)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            candidates.Add((rdr.GetInt64("sale_id"), rdr.GetString("sale_no"), rdr.GetDecimal("total"), rdr.GetString("status"), rdr.GetDateTime("sale_datetime")))
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load sales:" & Environment.NewLine & ex.Message, "Void Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try
        If candidates.Count = 0 Then
            AppModal.Show("There are no completed or held sales to void.", "Nothing to Void", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        Dim chosenId As Long = PromptPickSale("Void Sale",
            candidates.Select(Function(c) (c.id, c.no & "   [" & c.status & "]   P " & c.total.ToString("N2") & "   " & c.dt.ToString("MMM d, h:mm tt"))).ToList())
        If chosenId <= 0 Then Return
        Dim chosen = candidates.First(Function(c) c.id = chosenId)
        If AppModal.Show("Void sale " & chosen.no & " (" & chosen.status & ")?" & Environment.NewLine &
                If(chosen.status = "Completed", "Stock from this sale will be restored.", "") & Environment.NewLine & "This cannot be undone.",
                "Confirm Void", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then Return
        Try
            Using conn As MySqlConnection = Db.GetConnection()
                conn.Open()
                Using txn As MySqlTransaction = conn.BeginTransaction()
                    Try
                        ' Re-read the status from the LOCKED row. chosen.status was
                        ' captured before the confirmation prompt; if the sale was
                        ' refunded or voided in the meantime, restoring stock again
                        ' here would inflate inventory for the same sale twice.
                        Dim liveStatus As String = ""
                        Using cmd As New MySqlCommand(
                            "SELECT status FROM sales WHERE sale_id = @sid FOR UPDATE", conn, txn)
                            cmd.Parameters.AddWithValue("@sid", chosenId)
                            Dim o = cmd.ExecuteScalar()
                            If o Is Nothing OrElse IsDBNull(o) Then
                                Throw New Exception("That sale no longer exists.")
                            End If
                            liveStatus = o.ToString()
                        End Using
                        If liveStatus <> "Completed" AndAlso liveStatus <> "OnHold" Then
                            Throw New Exception(
                                "Sale " & chosen.no & " is now marked '" & liveStatus &
                                "' and can no longer be voided.")
                        End If

                        If liveStatus = "Completed" Then
                            Dim lines As New List(Of (pid As Integer, qty As Integer))
                            Using cmd As New MySqlCommand("SELECT product_id, qty FROM sale_items WHERE sale_id = @sid AND product_id IS NOT NULL", conn, txn)
                                cmd.Parameters.AddWithValue("@sid", chosenId)
                                Using rdr = cmd.ExecuteReader()
                                    While rdr.Read()
                                        lines.Add((rdr.GetInt32("product_id"), rdr.GetInt32("qty")))
                                    End While
                                End Using
                            End Using
                            For Each ln In lines
                                Using cmd As New MySqlCommand("UPDATE products SET stock_qty = stock_qty + @qty WHERE product_id = @pid", conn, txn)
                                    cmd.Parameters.AddWithValue("@qty", ln.qty)
                                    cmd.Parameters.AddWithValue("@pid", ln.pid)
                                    cmd.ExecuteNonQuery()
                                End Using
                                Using cmd As New MySqlCommand(
                                    "INSERT INTO stock_movements (product_id, user_id, movement_type, qty_change, reference_type, reference_id, note, created_at) " &
                                    "VALUES (@pid, @uid, 'Adjustment', @qc, 'void', @sid, 'Void of sale', NOW())", conn, txn)
                                    cmd.Parameters.AddWithValue("@pid", ln.pid)
                                    cmd.Parameters.AddWithValue("@uid", Session.CurrentUserId)
                                    cmd.Parameters.AddWithValue("@qc", ln.qty)
                                    cmd.Parameters.AddWithValue("@sid", chosenId)
                                    cmd.ExecuteNonQuery()
                                End Using
                            Next
                        End If
                        Using cmd As New MySqlCommand("UPDATE sales SET status = 'Voided' WHERE sale_id = @sid", conn, txn)
                            cmd.Parameters.AddWithValue("@sid", chosenId)
                            cmd.ExecuteNonQuery()
                        End Using
                        txn.Commit()
                    Catch
                        txn.Rollback()
                        Throw
                    End Try
                End Using
            End Using
            AppModal.Show("Sale " & chosen.no & " was voided.", "Voided", MessageBoxButtons.OK, MessageBoxIcon.Information)
            LoadTransaction(_mainContainer)
        Catch ex As Exception
            AppModal.Show("Could not void the sale:" & Environment.NewLine & Environment.NewLine & ex.Message, "Void Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    Private Shared Function PromptPickSale(title As String, items As List(Of (id As Long, text As String))) As Long
        Using f As New Form With {.Text = title, .FormBorderStyle = FormBorderStyle.FixedDialog,
                .StartPosition = FormStartPosition.CenterParent, .MaximizeBox = False, .MinimizeBox = False,
                .ClientSize = New Size(420, 320), .BackColor = CardBg, .Font = New Font("Segoe UI", 9.5F)}
            Dim lst As New ListBox With {.Location = New Point(16, 16), .Size = New Size(388, 248), .Font = New Font("Consolas", 9.5F)}
            For Each it In items : lst.Items.Add(it.text) : Next
            If lst.Items.Count > 0 Then lst.SelectedIndex = 0
            Dim btnOk As New Button With {.Text = "OK", .DialogResult = DialogResult.OK, .Location = New Point(252, 276),
                .Size = New Size(72, 30), .FlatStyle = FlatStyle.Flat, .BackColor = MintPrimary, .ForeColor = Color.White}
            btnOk.FlatAppearance.BorderSize = 0
            Dim btnCancel As New Button With {.Text = "Cancel", .DialogResult = DialogResult.Cancel, .Location = New Point(332, 276),
                .Size = New Size(72, 30), .FlatStyle = FlatStyle.Flat, .BackColor = Color.FromArgb(232, 238, 237), .ForeColor = SubText}
            btnCancel.FlatAppearance.BorderSize = 0
            f.Controls.AddRange({lst, btnOk, btnCancel})
            f.AcceptButton = btnOk : f.CancelButton = btnCancel
            AddHandler lst.DoubleClick, Sub() f.DialogResult = DialogResult.OK
            If f.ShowDialog() <> DialogResult.OK OrElse lst.SelectedIndex < 0 Then Return 0
            Return items(lst.SelectedIndex).id
        End Using
    End Function

    ' =========================================================================
    '  PRINT RECEIPT
    ' =========================================================================
    Private Shared Sub PrintReceipt()
        Dim lines As New List(Of ReceiptPrinter.RLine)
        lines.Add(New ReceiptPrinter.RLine(StoreSettings.StoreName, , ReceiptPrinter.LineStyle.Title))
        lines.Add(New ReceiptPrinter.RLine(StoreSettings.StoreTagline, , ReceiptPrinter.LineStyle.Muted))
        ' Optional header lines — only emitted when the setting is filled in.
        If StoreSettings.StoreAddress <> "" Then
            lines.Add(New ReceiptPrinter.RLine(StoreSettings.StoreAddress, , ReceiptPrinter.LineStyle.Muted))
        End If
        If StoreSettings.StoreContact <> "" Then
            lines.Add(New ReceiptPrinter.RLine(StoreSettings.StoreContact, , ReceiptPrinter.LineStyle.Muted))
        End If
        If StoreSettings.StoreTin <> "" Then
            lines.Add(New ReceiptPrinter.RLine("TIN: " & StoreSettings.StoreTin, , ReceiptPrinter.LineStyle.Muted))
        End If
        lines.Add(ReceiptPrinter.Divider())
        If Not String.IsNullOrEmpty(_lastSaleNo) Then
            lines.Add(New ReceiptPrinter.RLine("Receipt:", _lastSaleNo))
            lines.Add(New ReceiptPrinter.RLine("Date:", _lastSaleAt.ToString("MMM d, yyyy h:mm tt")))
            lines.Add(New ReceiptPrinter.RLine("Cashier:", Session.CurrentFullName))
            lines.Add(New ReceiptPrinter.RLine("Payment:", _lastSalePay))
            lines.Add(ReceiptPrinter.Divider())
            For Each it In _lastSaleLines
                lines.Add(New ReceiptPrinter.RLine(it.name, "", ReceiptPrinter.LineStyle.Bold))
                lines.Add(New ReceiptPrinter.RLine("  " & it.qty.ToString() & " x P " & it.price.ToString("N2"), "P " & it.line.ToString("N2")))
            Next
            lines.Add(ReceiptPrinter.Divider())
            lines.Add(New ReceiptPrinter.RLine("Subtotal", "P " & _lastSaleSubtotal.ToString("N2")))
            If _lastSaleDiscount > 0 Then lines.Add(New ReceiptPrinter.RLine("Discount", "- P " & _lastSaleDiscount.ToString("N2")))
            lines.Add(New ReceiptPrinter.RLine(StoreSettings.VatLabel, "P " & _lastSaleVat.ToString("N2")))
            lines.Add(New ReceiptPrinter.RLine("TOTAL", "P " & _lastSaleTotal.ToString("N2"), ReceiptPrinter.LineStyle.Bold))
            ' Same content match the payment prompt uses, so a method named
            ' "CASH" or "Cash on Hand" still prints Tendered and Change.
            If _lastSalePay IsNot Nothing AndAlso
               _lastSalePay.IndexOf("cash", StringComparison.OrdinalIgnoreCase) >= 0 Then
                lines.Add(New ReceiptPrinter.RLine("Tendered", "P " & _lastSaleTendered.ToString("N2")))
                lines.Add(New ReceiptPrinter.RLine("Change", "P " & _lastSaleChange.ToString("N2")))
            End If
            If Not String.IsNullOrEmpty(_lastSaleRef) Then
                lines.Add(New ReceiptPrinter.RLine("Ref No:", _lastSaleRef))
            End If
        Else
            AppModal.Show("No completed sale to reprint yet. Take a payment first and the receipt will print automatically.",
                "Reprint Receipt", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        lines.Add(ReceiptPrinter.Spacer())
        lines.Add(New ReceiptPrinter.RLine("Thank you for your purchase!", , ReceiptPrinter.LineStyle.Muted))
        ReceiptPrinter.Preview("Sales Receipt", lines)
    End Sub

End Class