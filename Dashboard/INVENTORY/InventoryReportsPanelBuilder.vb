Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Globalization
Imports System.Windows.Forms
Imports MySql.Data.MySqlClient

' =============================================================================
'  InventoryReportsPanelBuilder  —  Inventory ▸ Reports
'
'  Four tabular reports, each generated on screen and saved as Excel (numbers
'  stay editable), PDF, or printed -- with the Prepared / Checked / Approved
'  signature block, exactly like the Accountant's reports.
'
'     Stock on Hand          every active item, qty, status and value   (as of now)
'     Items to Restock       the Low Stock Alerts list, as a purchase plan (as of now)
'     Stock Movements        every stock change in a period          (From / To, Type)
'     Damage & Adjustments   hand corrections and damage in a period  (From / To)
'  All four filter by Category.
'
'  The on-screen table is drawn from the SAME ReportDoc that is printed and
'  exported, so what is seen is exactly what is saved.
'
'  Rules shared with other screens, so the figures always agree:
'     Status tiers / "to restock"  = Stock Levels and Low Stock Alerts
'                                    (Critical: qty <= restock level,
'                                     Low: qty within 125% of it)
'     Suggested order / est. value = Low Stock Alerts (value at selling price)
'     Movement labels and totals   = InventoryMovements (dock receipts are
'                                    listed but not counted twice)
' =============================================================================
Public Class InventoryReportsPanelBuilder

    ' ── PALETTE ───────────────────────────────────────────────────────────────
    Private Shared ReadOnly MintDark As Color = Theme.Dark
    Private Shared ReadOnly MintPrimary As Color = Theme.Primary
    Private Shared ReadOnly MintDeep As Color = Theme.Deep
    Private Shared ReadOnly MintBg As Color = Theme.PageBg
    Private Shared ReadOnly CardBg As Color = Color.White
    Private Shared ReadOnly SubText As Color = Theme.SubText
    Private Shared ReadOnly FaintText As Color = Theme.FaintText
    Private Shared ReadOnly RowDivider As Color = Theme.RowDivider
    Private Shared ReadOnly TotalBg As Color = Color.FromArgb(240, 248, 245)
    Private Shared ReadOnly TabIdle As Color = Color.FromArgb(220, 235, 232)
    Private Shared ReadOnly Peso As String = ChrW(8369)

    Private Const SIDE_PAD As Integer = 24
    Private Const PREVIEW_ROW_H As Integer = 34

    Private Const TAB_STOCK As String = "stock"
    Private Const TAB_RESTOCK As String = "restock"
    Private Const TAB_MOVES As String = "moves"
    Private Const TAB_ADJUST As String = "adjust"

    Private Shared ReadOnly Tabs As (key As String, label As String)() = {
        (TAB_STOCK, "Stock on Hand"),
        (TAB_RESTOCK, "Items to Restock"),
        (TAB_MOVES, "Stock Movements"),
        (TAB_ADJUST, "Damage & Adjustments")
    }

    ' ── STATE ─────────────────────────────────────────────────────────────────
    Private Shared _container As Panel
    Private Shared _activeTab As String = TAB_STOCK
    Private Shared _from As Date
    Private Shared _to As Date
    Private Shared _typeIdx As Integer = 0
    Private Shared _catIdx As Integer = 0
    Private Shared _categories As New List(Of (id As Integer, name As String))
    Private Shared _doc As ReportPrinter.ReportDoc
    Private Shared _loadFailed As Boolean = False

    Private Shared _tabHost As FlowLayoutPanel
    Private Shared _filterBar As Panel
    Private Shared _contentHost As Panel

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Shared Sub LoadReports(container As Panel)
        _container = container
        _activeTab = TAB_STOCK
        _from = Date.Today.AddDays(-29)
        _to = Date.Today
        _typeIdx = 0
        _catIdx = 0
        _categories = InventoryMovements.LoadCategories()
        Render()
    End Sub

    Private Shared ReadOnly Property CategoryId As Integer
        Get
            If _catIdx < 0 OrElse _catIdx >= _categories.Count Then Return 0
            Return _categories(_catIdx).id
        End Get
    End Property

    Private Shared ReadOnly Property IsPeriodReport As Boolean
        Get
            Return _activeTab = TAB_MOVES OrElse _activeTab = TAB_ADJUST
        End Get
    End Property

    ' =========================================================================
    '  FRAME
    ' =========================================================================
    Private Shared Sub Render()
        _container.SuspendLayout()
        _container.Controls.Clear()
        _container.BackColor = MintBg

        _contentHost = New Panel With {
            .Dock = DockStyle.Fill, .BackColor = MintBg,
            .Padding = New Padding(SIDE_PAD, 4, SIDE_PAD, 20)
        }
        _filterBar = New Panel With {.Dock = DockStyle.Top, .Height = 56, .BackColor = MintBg}
        Dim tabRow As Panel = BuildTabRow()
        tabRow.Dock = DockStyle.Top
        Dim titleBar As Panel = BuildTitleBar()
        titleBar.Dock = DockStyle.Top

        _container.Controls.Add(_contentHost)
        _container.Controls.Add(_filterBar)
        _container.Controls.Add(tabRow)
        _container.Controls.Add(titleBar)
        _container.ResumeLayout()

        RebuildFilters()
        Regenerate()
    End Sub

    Private Shared Function BuildTitleBar() As Panel
        Dim bar As New Panel With {.BackColor = MintBg, .Height = 76}
        bar.Controls.Add(New Label With {
            .Text = "Reports", .Font = New Font("Segoe UI", 28, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .BackColor = Color.Transparent,
            .Location = New Point(SIDE_PAD - 4, 14)
        })

        Dim btnPrint As Button = MakeActionButton("Print", 110, MintDark, MintDeep)
        Dim btnPdf As Button = MakeActionButton("Save as PDF", 140, MintDark, MintDeep)
        Dim btnExcel As Button = MakeActionButton("Save as Excel", 150, MintPrimary, MintDeep)
        AddHandler btnPrint.Click, Sub() PrintReport()
        AddHandler btnPdf.Click, Sub() SaveReport(ReportExport.ExportFormat.Pdf, btnPdf)
        AddHandler btnExcel.Click, Sub() SaveReport(ReportExport.ExportFormat.Excel, btnExcel)

        AddHandler bar.Resize,
            Sub()
                Dim cy As Integer = (bar.Height - btnPrint.Height) \ 2
                btnPrint.Location = New Point(bar.Width - btnPrint.Width - SIDE_PAD, cy)
                btnPdf.Location = New Point(btnPrint.Left - btnPdf.Width - 10, cy)
                btnExcel.Location = New Point(btnPdf.Left - btnExcel.Width - 10, cy)
            End Sub
        bar.Controls.AddRange({btnExcel, btnPdf, btnPrint})
        Return bar
    End Function

    Private Shared Function BuildTabRow() As Panel
        Dim bar As New Panel With {.BackColor = MintBg, .Height = 52}
        _tabHost = New FlowLayoutPanel With {
            .FlowDirection = FlowDirection.LeftToRight, .AutoSize = True, .WrapContents = False,
            .BackColor = Color.Transparent, .Location = New Point(SIDE_PAD, 6)
        }
        For Each t In Tabs
            Dim key As String = t.key
            Dim b As New Button With {
                .Text = t.label, .FlatStyle = FlatStyle.Flat, .Cursor = Cursors.Hand,
                .Height = 38, .Margin = New Padding(0, 0, 8, 0), .Tag = key,
                .Font = New Font("Segoe UI Semibold", 10)
            }
            ' NoPrefix: without it "Damage & Adjustments" draws as "Damage _Adjustments".
            b.Width = TextRenderer.MeasureText(b.Text, b.Font, Size.Empty, TextFormatFlags.NoPrefix).Width + 40
            b.FlatAppearance.BorderSize = 0
            AddHandler b.Paint,
                Sub(s, e)
                    Dim bb = CType(s, Button)
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                    Using bg As New SolidBrush(MintBg)
                        e.Graphics.FillRectangle(bg, bb.ClientRectangle)
                    End Using
                    Dim rect = bb.ClientRectangle : rect.Inflate(-1, -1)
                    Using br As New SolidBrush(bb.BackColor)
                        e.Graphics.FillRoundedRectangle(br, rect, 8)
                    End Using
                    TextRenderer.DrawText(e.Graphics, bb.Text, bb.Font, bb.ClientRectangle, bb.ForeColor,
                        TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPrefix)
                End Sub
            AddHandler b.MouseEnter,
                Sub()
                    If key <> _activeTab Then
                        b.BackColor = Theme.Wash
                        b.Invalidate()
                    End If
                End Sub
            AddHandler b.MouseLeave, Sub() StyleTabs()
            AddHandler b.Click,
                Sub()
                    If _activeTab = key Then Return
                    _activeTab = key
                    StyleTabs()
                    RebuildFilters()
                    Regenerate()
                End Sub
            _tabHost.Controls.Add(b)
        Next
        StyleTabs()
        bar.Controls.Add(_tabHost)
        Return bar
    End Function

    Private Shared Sub StyleTabs()
        If _tabHost Is Nothing Then Return
        For Each c As Control In _tabHost.Controls
            Dim active As Boolean = CStr(c.Tag) = _activeTab
            c.BackColor = If(active, MintPrimary, TabIdle)
            c.ForeColor = If(active, Color.White, SubText)
            c.Invalidate()
        Next
    End Sub

    ' Only the filters that apply to the report on screen are shown: the
    ' "as of now" reports have no period, and only Stock Movements has a Type.
    Private Shared Sub RebuildFilters()
        FilterFields.ClosePopup()
        For Each c As Control In _filterBar.Controls.Cast(Of Control).ToList()
            c.Dispose()
        Next
        _filterBar.Controls.Clear()

        Dim x As Integer = SIDE_PAD
        Const TOP_Y As Integer = 10
        Dim addLabel As Action(Of String) =
            Sub(text As String)
                Dim l As New Label With {
                    .Text = text, .Font = New Font("Segoe UI Semibold", 10), .ForeColor = SubText,
                    .AutoSize = True, .BackColor = Color.Transparent
                }
                l.Location = New Point(x, TOP_Y + (FilterFields.FIELD_H - l.PreferredHeight) \ 2)
                _filterBar.Controls.Add(l)
                x += l.PreferredWidth + 8
            End Sub

        If IsPeriodReport Then
            addLabel("From")
            Dim setFrom As Action(Of Date) = Nothing, setTo As Action(Of Date) = Nothing
            Dim boxFrom As Panel = FilterFields.MakeDateField(140, _from,
                Sub(d As Date)
                    _from = d.Date
                    If _from > _to Then
                        _to = _from
                        setTo(_to)
                    End If
                    Regenerate()
                End Sub, setFrom)
            boxFrom.Location = New Point(x, TOP_Y)
            _filterBar.Controls.Add(boxFrom)
            x += boxFrom.Width + 16

            addLabel("To")
            Dim boxTo As Panel = FilterFields.MakeDateField(140, _to,
                Sub(d As Date)
                    _to = d.Date
                    If _to < _from Then
                        _from = _to
                        setFrom(_from)
                    End If
                    Regenerate()
                End Sub, setTo)
            boxTo.Location = New Point(x, TOP_Y)
            _filterBar.Controls.Add(boxTo)
            x += boxTo.Width + 24
        End If

        If _activeTab = TAB_MOVES Then
            addLabel("Type")
            Dim boxType As Panel = FilterFields.MakeSelectField(
                New List(Of String)(InventoryMovements.TypeFilters), 170, _typeIdx,
                Sub(i As Integer)
                    _typeIdx = i
                    Regenerate()
                End Sub)
            boxType.Location = New Point(x, TOP_Y)
            _filterBar.Controls.Add(boxType)
            x += boxType.Width + 24
        End If

        addLabel("Category")
        Dim boxCat As Panel = FilterFields.MakeSelectField(
            _categories.Select(Function(c) c.name).ToList(), 170, _catIdx,
            Sub(i As Integer)
                _catIdx = i
                Regenerate()
            End Sub)
        boxCat.Location = New Point(x, TOP_Y)
        _filterBar.Controls.Add(boxCat)
        x += boxCat.Width + 24

        If Not IsPeriodReport Then
            Dim lblAsOf As New Label With {
                .Text = "Current stock, as of when the report is generated",
                .Font = New Font("Segoe UI", 9.5F, FontStyle.Italic), .ForeColor = FaintText,
                .AutoSize = True, .BackColor = Color.Transparent
            }
            lblAsOf.Location = New Point(x, TOP_Y + (FilterFields.FIELD_H - lblAsOf.PreferredHeight) \ 2)
            _filterBar.Controls.Add(lblAsOf)
        End If
    End Sub

    ' =========================================================================
    '  GENERATE  (build the ReportDoc, then show it)
    ' =========================================================================
    Private Shared Sub Regenerate()
        _doc = BuildReportDoc()
        ShowPreview()
    End Sub

    Private Shared Function BuildReportDoc() As ReportPrinter.ReportDoc
        Dim doc As ReportPrinter.ReportDoc
        Try
            Select Case _activeTab
                Case TAB_RESTOCK : doc = BuildRestock()
                Case TAB_MOVES : doc = BuildMovements()
                Case TAB_ADJUST : doc = BuildAdjustments()
                Case Else : doc = BuildStockOnHand()
            End Select
            _loadFailed = False
        Catch ex As Exception
            If Not _loadFailed Then
                AppModal.Show("Could not generate the report:" & Environment.NewLine & ErrorLog.ForUser(ex),
                              "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End If
            _loadFailed = True
            doc = New ReportPrinter.ReportDoc With {
                .Title = TabLabel(_activeTab),
                .Columns = New List(Of ReportPrinter.Column) From {ReportPrinter.Col("", 1)}
            }
            doc.Rows.Add(ReportPrinter.TableRow("The report could not be loaded."))
        End Try
        Return doc
    End Function

    Private Shared Function TabLabel(key As String) As String
        For Each t In Tabs
            If t.key = key Then Return t.label
        Next
        Return "Report"
    End Function

    ' "As of Sep 28, 2026 3:05 PM", plus the category when one is picked.
    Private Shared Function AsOfText() As String
        Return DateTime.Now.ToString("MMM d, yyyy h:mm tt", CultureInfo.InvariantCulture) & CategorySuffix()
    End Function

    Private Shared Function CategorySuffix() As String
        If CategoryId = 0 Then Return ""
        Return "  -  " & _categories(_catIdx).name
    End Function

    Private Shared Function Money(v As Decimal) As String
        If v < 0 Then Return "(" & Peso & (-v).ToString("N2", CultureInfo.InvariantCulture) & ")"
        Return Peso & v.ToString("N2", CultureInfo.InvariantCulture)
    End Function

    Private Shared Function Num(n As Integer) As String
        Return n.ToString("N0", CultureInfo.InvariantCulture)
    End Function

    Private Class ProductLine
        Public Sku As String
        Public Name As String
        Public Category As String
        Public Qty As Integer
        Public Restock As Integer
        Public UnitPrice As Decimal
    End Class

    Private Shared Function LoadProducts(onlyToRestock As Boolean) As List(Of ProductLine)
        Dim list As New List(Of ProductLine)
        Using conn = Db.GetConnection()
            conn.Open()
            ' The restock condition is the Low Stock Alerts one, word for word.
            Using cmd As New MySqlCommand(
                "SELECT p.sku, p.name, COALESCE(c.name, '') AS cname, p.stock_qty, p.reorder_level, p.unit_price " &
                "FROM products p LEFT JOIN categories c ON c.category_id = p.category_id " &
                "WHERE p.is_active = 1 " &
                If(CategoryId > 0, "AND p.category_id = @cat ", "") &
                If(onlyToRestock,
                   "AND p.stock_qty * 100 <= p.reorder_level * 125 " &
                   "ORDER BY p.stock_qty - p.reorder_level ASC, p.name ASC",
                   "ORDER BY cname ASC, p.name ASC"), conn)
                If CategoryId > 0 Then cmd.Parameters.AddWithValue("@cat", CategoryId)
                Using rdr = cmd.ExecuteReader()
                    While rdr.Read()
                        list.Add(New ProductLine With {
                            .Sku = If(IsDBNull(rdr("sku")), "", rdr("sku").ToString()),
                            .Name = rdr("name").ToString(),
                            .Category = rdr("cname").ToString(),
                            .Qty = Convert.ToInt32(rdr("stock_qty")),
                            .Restock = Convert.ToInt32(rdr("reorder_level")),
                            .UnitPrice = If(IsDBNull(rdr("unit_price")), 0D, Convert.ToDecimal(rdr("unit_price")))
                        })
                    End While
                End Using
            End Using
        End Using
        Return list
    End Function

    ' Same tiers as the Stock Levels screen.
    Private Shared Function StatusOf(p As ProductLine) As String
        If p.Qty <= 0 Then Return "Out"
        If p.Qty <= p.Restock Then Return "Critical"
        If p.Qty * 100 <= p.Restock * 125 Then Return "Low"
        Return "OK"
    End Function

    ' Same as Low Stock Alerts: enough to reach twice the restock level.
    Private Shared Function SuggestedQty(p As ProductLine) As Integer
        Dim target As Integer = Math.Max(p.Restock * 2, p.Restock + 1)
        Return Math.Max(1, target - p.Qty)
    End Function

    Private Shared Sub AddEmptyRow(doc As ReportPrinter.ReportDoc, message As String)
        Dim cells(doc.Columns.Count - 1) As String
        cells(Math.Min(1, cells.Length - 1)) = message
        doc.Rows.Add(ReportPrinter.TableRow(cells))
    End Sub

    ' ── 1. STOCK ON HAND ──────────────────────────────────────────────────────
    Private Shared Function BuildStockOnHand() As ReportPrinter.ReportDoc
        Dim doc As New ReportPrinter.ReportDoc With {
            .Title = "Stock on Hand", .PeriodLabel = "As of", .PeriodValue = AsOfText(),
            .Columns = New List(Of ReportPrinter.Column) From {
                ReportPrinter.Col("SKU", 1.0F),
                ReportPrinter.Col("Product", 2.3F),
                ReportPrinter.Col("Category", 1.1F),
                ReportPrinter.Col("Qty", 0.7F, ReportPrinter.ColumnKind.Number),
                ReportPrinter.Col("Restock Lvl", 0.9F, ReportPrinter.ColumnKind.Number),
                ReportPrinter.Col("Status", 0.8F),
                ReportPrinter.Col("Unit Price", 1.1F, ReportPrinter.ColumnKind.Money),
                ReportPrinter.Col("Stock Value", 1.3F, ReportPrinter.ColumnKind.Money)
            }
        }
        doc.Rows.Add(ReportPrinter.TableHeader())
        Dim items = LoadProducts(onlyToRestock:=False)
        Dim totQty As Integer = 0
        Dim totValue As Decimal = 0D
        For Each p In items
            Dim value As Decimal = p.Qty * p.UnitPrice
            doc.Rows.Add(ReportPrinter.TableRow(p.Sku, p.Name, p.Category, Num(p.Qty), Num(p.Restock),
                                                StatusOf(p), Money(p.UnitPrice), Money(value)))
            totQty += p.Qty
            totValue += value
        Next
        If items.Count = 0 Then AddEmptyRow(doc, "No active items in this category.")
        doc.Rows.Add(ReportPrinter.TableTotal("TOTAL", Num(items.Count) & If(items.Count = 1, " item", " items"),
                                              "", Num(totQty), "", "", "", Money(totValue)))
        Return doc
    End Function

    ' ── 2. ITEMS TO RESTOCK ───────────────────────────────────────────────────
    Private Shared Function BuildRestock() As ReportPrinter.ReportDoc
        Dim doc As New ReportPrinter.ReportDoc With {
            .Title = "Items to Restock", .PeriodLabel = "As of", .PeriodValue = AsOfText(),
            .Columns = New List(Of ReportPrinter.Column) From {
                ReportPrinter.Col("SKU", 1.0F),
                ReportPrinter.Col("Product", 2.3F),
                ReportPrinter.Col("Category", 1.1F),
                ReportPrinter.Col("Qty", 0.7F, ReportPrinter.ColumnKind.Number),
                ReportPrinter.Col("Restock Lvl", 0.9F, ReportPrinter.ColumnKind.Number),
                ReportPrinter.Col("Short", 0.7F, ReportPrinter.ColumnKind.Number),
                ReportPrinter.Col("Order Qty", 0.9F, ReportPrinter.ColumnKind.Number),
                ReportPrinter.Col("Est. Value", 1.3F, ReportPrinter.ColumnKind.Money)
            }
        }
        doc.Rows.Add(ReportPrinter.TableHeader())
        Dim items = LoadProducts(onlyToRestock:=True)
        Dim totShort As Integer = 0, totOrder As Integer = 0
        Dim totValue As Decimal = 0D
        For Each p In items
            Dim shortBy As Integer = Math.Max(0, p.Restock - p.Qty)
            Dim order As Integer = SuggestedQty(p)
            Dim value As Decimal = order * p.UnitPrice
            doc.Rows.Add(ReportPrinter.TableRow(p.Sku, p.Name, p.Category, Num(p.Qty), Num(p.Restock),
                                                Num(shortBy), Num(order), Money(value)))
            totShort += shortBy : totOrder += order : totValue += value
        Next
        If items.Count = 0 Then AddEmptyRow(doc, "Nothing needs restocking right now.")
        doc.Rows.Add(ReportPrinter.TableTotal("TOTAL", Num(items.Count) & If(items.Count = 1, " item", " items"),
                                              "", "", "", Num(totShort), Num(totOrder), Money(totValue)))
        Return doc
    End Function

    ' ── 3. STOCK MOVEMENTS ────────────────────────────────────────────────────
    Private Shared Function MovementFilter(adjustmentsOnly As Boolean) As InventoryMovements.Filter
        Return New InventoryMovements.Filter With {
            .FromDate = _from, .ToDate = _to, .CategoryId = CategoryId,
            .TypeIndex = If(adjustmentsOnly, 0, _typeIdx), .AdjustmentsOnly = adjustmentsOnly
        }
    End Function

    Private Shared Function BuildMovements() As ReportPrinter.ReportDoc
        Dim f = MovementFilter(adjustmentsOnly:=False)
        Dim period As String = f.PeriodText() &
            If(_typeIdx > 0, "  -  " & InventoryMovements.TypeFilters(_typeIdx), "") & CategorySuffix()
        Dim doc As New ReportPrinter.ReportDoc With {
            .Title = "Stock Movements", .PeriodLabel = "Period", .PeriodValue = period,
            .Columns = New List(Of ReportPrinter.Column) From {
                ReportPrinter.Col("Date", 1.8F),
                ReportPrinter.Col("Product", 2.1F),
                ReportPrinter.Col("Type", 1.3F),
                ReportPrinter.Col("Qty", 0.6F, ReportPrinter.ColumnKind.SignedNumber),
                ReportPrinter.Col("By", 1.1F),
                ReportPrinter.Col("Note", 1.7F)
            }
        }
        doc.Rows.Add(ReportPrinter.TableHeader())
        Dim rows = InventoryMovements.Load(f)
        Dim unitsIn As Integer = 0, unitsOut As Integer = 0
        For Each m In rows
            doc.Rows.Add(ReportPrinter.TableRow(
                m.CreatedAt.ToString("MM/dd/yyyy h:mm tt", CultureInfo.InvariantCulture),
                m.ProductName, m.TypeLabel, InventoryMovements.Signed(m.QtyChange), m.UserName, m.Note))
            If Not m.IsDock Then
                If m.QtyChange > 0 Then unitsIn += m.QtyChange Else unitsOut += m.QtyChange
            End If
        Next
        If rows.Count = 0 Then AddEmptyRow(doc, "No stock changes in this period.")
        doc.Rows.Add(ReportPrinter.TableTotal("NET CHANGE",
            "In " & InventoryMovements.Signed(unitsIn) & "  /  Out " & InventoryMovements.Signed(unitsOut),
            "", InventoryMovements.Signed(unitsIn + unitsOut), "",
            If(rows.Any(Function(m) m.IsDock), "Excl. dock rows", "")))
        Return doc
    End Function

    ' ── 4. DAMAGE & ADJUSTMENTS ───────────────────────────────────────────────
    Private Shared Function BuildAdjustments() As ReportPrinter.ReportDoc
        Dim f = MovementFilter(adjustmentsOnly:=True)
        Dim doc As New ReportPrinter.ReportDoc With {
            .Title = "Damage & Adjustments", .PeriodLabel = "Period",
            .PeriodValue = f.PeriodText() & CategorySuffix(),
            .Columns = New List(Of ReportPrinter.Column) From {
                ReportPrinter.Col("Date", 1.8F),
                ReportPrinter.Col("Product", 2.0F),
                ReportPrinter.Col("Reason", 1.2F),
                ReportPrinter.Col("Qty", 0.6F, ReportPrinter.ColumnKind.SignedNumber),
                ReportPrinter.Col("Est. Value", 1.2F, ReportPrinter.ColumnKind.Money),
                ReportPrinter.Col("By", 1.0F),
                ReportPrinter.Col("Note", 1.5F)
            }
        }
        doc.Rows.Add(ReportPrinter.TableHeader())
        Dim rows = InventoryMovements.Load(f)
        Dim netQty As Integer = 0
        Dim netValue As Decimal = 0D
        For Each m In rows
            Dim value As Decimal = m.QtyChange * m.UnitValue
            doc.Rows.Add(ReportPrinter.TableRow(
                m.CreatedAt.ToString("MM/dd/yyyy h:mm tt", CultureInfo.InvariantCulture),
                m.ProductName, m.TypeLabel, InventoryMovements.Signed(m.QtyChange), Money(value),
                m.UserName, m.Note))
            netQty += m.QtyChange
            netValue += value
        Next
        If rows.Count = 0 Then AddEmptyRow(doc, "No corrections or damage in this period.")
        doc.Rows.Add(ReportPrinter.TableTotal("TOTAL",
            Num(rows.Count) & If(rows.Count = 1, " adjustment", " adjustments"),
            "", InventoryMovements.Signed(netQty), Money(netValue), "", "Value at cost"))
        Return doc
    End Function

    ' =========================================================================
    '  ON-SCREEN PREVIEW  (card: heading, column captions, painted rows)
    ' =========================================================================
    Private Shared Sub ShowPreview()
        If _contentHost Is Nothing OrElse _doc Is Nothing Then Return
        _contentHost.SuspendLayout()
        For Each c As Control In _contentHost.Controls.Cast(Of Control).ToList()
            c.Dispose()
        Next
        _contentHost.Controls.Clear()

        Dim doc As ReportPrinter.ReportDoc = _doc
        Dim cols = doc.Columns
        Dim lines = doc.Rows.Where(Function(r) r.Kind = ReportPrinter.RowKind.TableRow OrElse
                                               r.Kind = ReportPrinter.RowKind.TableTotal).ToList()

        Dim card As New BufferedPanel With {.Dock = DockStyle.Fill, .BackColor = CardBg, .Padding = New Padding(2)}
        AddHandler card.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)

        ' Heading: report title left, period / as-of right.
        Dim heading As New Panel With {.Dock = DockStyle.Top, .Height = 64, .BackColor = Color.Transparent}
        Dim lblTitle As New Label With {
            .Text = doc.Title, .Font = New Font("Segoe UI Semibold", 16, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .BackColor = Color.Transparent,
            .Location = New Point(22, 18), .UseMnemonic = False
        }
        Dim lblPeriod As New Label With {
            .Text = If(doc.PeriodValue = "", "", doc.PeriodLabel & ":  " & doc.PeriodValue),
            .Font = New Font("Segoe UI", 10.5F), .ForeColor = FaintText,
            .AutoSize = True, .BackColor = Color.Transparent, .UseMnemonic = False
        }
        heading.Controls.AddRange({lblTitle, lblPeriod})
        AddHandler heading.Resize,
            Sub() lblPeriod.Location = New Point(heading.Width - lblPeriod.Width - 22,
                                                 (heading.Height - lblPeriod.Height) \ 2)

        ' Column captions: painted with the same column maths as the rows.
        Dim capStrip As New BufferedPanel With {.Dock = DockStyle.Top, .Height = 36, .BackColor = TotalBg}
        AddHandler capStrip.Paint,
            Sub(s, e)
                Dim caps As String() = cols.Select(Function(c) c.Header.ToUpperInvariant()).ToArray()
                Using f As New Font("Segoe UI", 9, FontStyle.Bold)
                    DrawCells(e.Graphics, cols, caps, f, SubText, capStrip.Width, 0, capStrip.Height)
                End Using
            End Sub

        ' Rows: one tall canvas inside a scrolling host; only the rows in view
        ' are painted, so a long movement report stays quick to scroll.
        Dim scrollHost As New Panel With {.Dock = DockStyle.Fill, .AutoScroll = True, .BackColor = Color.Transparent}
        Dim canvas As New BufferedPanel With {
            .Location = New Point(0, 0), .Height = Math.Max(1, lines.Count * PREVIEW_ROW_H + 8),
            .BackColor = CardBg
        }
        AddHandler canvas.Paint,
            Sub(s, e)
                Dim g = e.Graphics
                Dim first As Integer = Math.Max(0, e.ClipRectangle.Top \ PREVIEW_ROW_H)
                Dim last As Integer = Math.Min(lines.Count - 1, e.ClipRectangle.Bottom \ PREVIEW_ROW_H)
                Using fRow As New Font("Segoe UI", 10), fTot As New Font("Segoe UI", 10, FontStyle.Bold),
                      pen As New Pen(RowDivider, 1), totBg As New SolidBrush(TotalBg)
                    For i As Integer = first To last
                        Dim y As Integer = i * PREVIEW_ROW_H
                        Dim isTotal As Boolean = lines(i).Kind = ReportPrinter.RowKind.TableTotal
                        If isTotal Then
                            g.FillRectangle(totBg, 0, y, canvas.Width, PREVIEW_ROW_H)
                        Else
                            g.DrawLine(pen, 14, y + PREVIEW_ROW_H - 1, canvas.Width - 14, y + PREVIEW_ROW_H - 1)
                        End If
                        DrawCells(g, cols, lines(i).Cells, If(isTotal, fTot, fRow),
                                  If(isTotal, MintDark, Theme.TextDark), canvas.Width, y, PREVIEW_ROW_H)
                    Next
                End Using
            End Sub
        scrollHost.Controls.Add(canvas)
        Dim fitWidth As Action =
            Sub()
                Dim w As Integer = scrollHost.ClientSize.Width
                If w > 0 AndAlso canvas.Width <> w Then canvas.Width = w
                capStrip.Invalidate()
            End Sub
        AddHandler scrollHost.Resize, Sub() fitWidth()
        AddHandler scrollHost.Layout, Sub() fitWidth()

        ' Row count under the table.
        Dim dataRows As Integer = lines.Where(Function(r) r.Kind = ReportPrinter.RowKind.TableRow).Count()
        Dim footer As New Panel With {.Dock = DockStyle.Bottom, .Height = 40, .BackColor = Color.Transparent}
        footer.Controls.Add(New Label With {
            .Text = If(_loadFailed, "Nothing to show.",
                       Num(Math.Max(0, dataRows - If(IsEmptyReport(doc), 1, 0))) & " row(s)   " & ChrW(8226) &
                       "   Save as Excel, Save as PDF or Print for the full report with signatures"),
            .Font = New Font("Segoe UI", 9.5F), .ForeColor = FaintText, .AutoSize = True,
            .Location = New Point(22, 11), .BackColor = Color.Transparent
        })

        card.Controls.Add(scrollHost)
        card.Controls.Add(footer)
        card.Controls.Add(capStrip)
        card.Controls.Add(heading)
        capStrip.BringToFront()
        scrollHost.BringToFront()
        _contentHost.Controls.Add(card)
        _contentHost.ResumeLayout()
        fitWidth()
    End Sub

    ' True when the only data line is the "nothing matched" message.
    Private Shared Function IsEmptyReport(doc As ReportPrinter.ReportDoc) As Boolean
        Dim data = doc.Rows.Where(Function(r) r.Kind = ReportPrinter.RowKind.TableRow).ToList()
        Return data.Count = 1 AndAlso data(0).Cells.Length > 0 AndAlso String.IsNullOrEmpty(data(0).Cells(0))
    End Function

    ' Screen twin of ReportPrinter's cell layout: weighted columns, text left,
    ' figures right, "..." when a value is too long for its column.
    Private Shared Sub DrawCells(g As Graphics, cols As List(Of ReportPrinter.Column), cells As String(),
                                 f As Font, clr As Color, width As Integer, y As Integer, h As Integer)
        If cols Is Nothing OrElse cols.Count = 0 OrElse cells Is Nothing Then Return
        Dim totalW As Single = cols.Sum(Function(c) c.Weight)
        If totalW <= 0 Then Return
        Dim usable As Integer = width - 28
        Dim x As Single = 14
        For i As Integer = 0 To cols.Count - 1
            Dim colW As Single = usable * cols(i).Weight / totalW
            Dim text As String = If(i < cells.Length, If(cells(i), ""), "")
            If text <> "" Then
                Dim flags As TextFormatFlags = TextFormatFlags.VerticalCenter Or TextFormatFlags.EndEllipsis Or
                                               TextFormatFlags.SingleLine Or TextFormatFlags.NoPrefix Or
                    If(cols(i).Kind = ReportPrinter.ColumnKind.Text, TextFormatFlags.Left, TextFormatFlags.Right)
                Dim cellClr As Color = clr
                ' Movement quantities read at a glance: in green, out red.
                If cols(i).Kind = ReportPrinter.ColumnKind.SignedNumber Then
                    If text.StartsWith("+") Then cellClr = Theme.Success
                    If text.StartsWith("-") Then cellClr = Theme.Danger
                End If
                TextRenderer.DrawText(g, text, f, New Rectangle(CInt(x) + 6, y, CInt(colW) - 12, h), cellClr, flags)
            End If
            x += colW
        Next
    End Sub

    ' =========================================================================
    '  OUTPUT — built fresh each time, so it never prints stale figures
    ' =========================================================================
    Private Shared Sub PrintReport()
        Dim doc As ReportPrinter.ReportDoc = BuildReportDoc()
        If _loadFailed Then Return
        ActivityLog.Record("REPORT_EXPORT", "report", Nothing,
                           Left(doc.Title & " (Print) - " & doc.PeriodValue, 250))
        ReportPrinter.Preview(doc, companyName:=ReportPrinter.CompanyName(),
                              preparedBy:=Session.CurrentFullName)
    End Sub

    Private Shared Sub SaveReport(format As ReportExport.ExportFormat, owner As Control)
        Dim doc As ReportPrinter.ReportDoc = BuildReportDoc()
        If _loadFailed Then Return
        ReportExport.SaveWithDialog(doc, format, ReportPrinter.CompanyName(), Session.CurrentFullName,
                                    owner.FindForm())
    End Sub

    Private Shared Function MakeActionButton(text As String, width As Integer,
                                             rest As Color, hover As Color) As Button
        Dim b As New Button With {
            .Text = text, .FlatStyle = FlatStyle.Flat, .Font = New Font("Segoe UI Semibold", 10),
            .ForeColor = Color.White, .BackColor = rest, .Size = New Size(width, 40),
            .Cursor = Cursors.Hand, .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        b.FlatAppearance.BorderSize = 0
        AddHandler b.Paint,
            Sub(s, e)
                Dim bb = CType(s, Button)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Using bg As New SolidBrush(bb.Parent.BackColor)
                    e.Graphics.FillRectangle(bg, bb.ClientRectangle)
                End Using
                Dim rect = bb.ClientRectangle : rect.Inflate(-1, -1)
                Using br As New SolidBrush(bb.BackColor)
                    e.Graphics.FillRoundedRectangle(br, rect, 8)
                End Using
                TextRenderer.DrawText(e.Graphics, bb.Text, bb.Font, bb.ClientRectangle,
                    bb.ForeColor, TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub
        AddHandler b.MouseEnter, Sub()
                                     b.BackColor = hover
                                     b.Invalidate()
                                 End Sub
        AddHandler b.MouseLeave, Sub()
                                     b.BackColor = rest
                                     b.Invalidate()
                                 End Sub
        Return b
    End Function

End Class
