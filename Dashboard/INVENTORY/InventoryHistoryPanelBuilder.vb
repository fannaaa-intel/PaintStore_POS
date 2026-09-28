Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms

' =============================================================================
'  InventoryHistoryPanelBuilder  —  Inventory ▸ Transaction History (READ-ONLY)
'
'  Every stock change in one list: sales, voids, customer returns, deliveries
'  received and stocked in, corrections, damage, and opening stock. Before
'  this screen the only view was the last eight changes of ONE product, inside
'  Stock Adjustment.
'
'  LAYOUT (same frame as Admin ▸ Inventory Overview):
'    TopRegion (Dock = Top, 128px):
'      ├─ TitleBar  (72px) : title + product search
'      └─ FilterBar (56px) : From / To, Type, Category
'    SummaryStrip (Dock = Top, 152px) : Movements, Units In, Units Out, Net
'    Table card  (Dock = Fill) : column header, rows, pager
'
'  Labels, filters and the dock-receipt rule live in InventoryMovements, so
'  this screen and Inventory Reports always agree.
' =============================================================================
Public Class InventoryHistoryPanelBuilder

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
    Private Shared ReadOnly PositiveClr As Color = Theme.Success

    Private Const SEARCH_PLACEHOLDER As String = "Search product or SKU…"
    Private Const SIDE_PAD As Integer = 24
    Private Const ROW_H As Integer = 62
    Private Const SUMMARY_H As Integer = 140
    Private Const MAX_PAGE_SIZE As Integer = 50
    ' topRegion 128 + summary (140+12) + spacers 16+16 + card padding 2
    ' + colHeader 44 + pagerBar 52 + bottomPad 12
    Private Const CHROME_H As Integer = 422

    ' Column x positions as a share of the row width (header and rows alike).
    Private Const X_PRODUCT As Single = 0.16F
    Private Const X_TYPE As Single = 0.43F
    Private Const X_QTY As Single = 0.58F
    Private Const X_BY As Single = 0.66F
    Private Const X_NOTE As Single = 0.79F

    ' ── STATE ─────────────────────────────────────────────────────────────────
    Private Shared _container As Panel
    Private Shared _scrollHost As Panel
    Private Shared _filter As New InventoryMovements.Filter()
    Private Shared _categories As New List(Of (id As Integer, name As String))
    Private Shared _rows As New List(Of InventoryMovements.MovementRow)
    Private Shared _pageSize As Integer = MAX_PAGE_SIZE
    Private Shared _currentPage As Integer = 0
    Private Shared _totalRows As Integer = 0
    Private Shared _unitsIn As Integer = 0
    Private Shared _unitsOut As Integer = 0
    Private Shared _reflowAttached As Boolean = False
    Private Shared _loadFailed As Boolean = False

    Private Shared _lblCount, _lblIn, _lblOut, _lblNet As Label
    Private Shared _lblPageInfo As Label
    Private Shared _btnPrev, _btnNext As Button
    Private Shared _searchTimer As Timer

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Shared Sub LoadHistory(container As Panel)
        _container = container
        _filter = New InventoryMovements.Filter()    ' last 30 days, no filters
        _currentPage = 0
        _categories = InventoryMovements.LoadCategories()

        ComputePageSize()
        LoadData()
        Render()
    End Sub

    Private Shared Sub ComputePageSize()
        _pageSize = MAX_PAGE_SIZE
        If _container Is Nothing Then Return
        Dim viewport As Integer = _container.ClientSize.Height - CHROME_H
        If viewport < 120 Then Return
        _pageSize = Math.Max(4, Math.Min(MAX_PAGE_SIZE, (viewport - 4) \ ROW_H))
    End Sub

    Private Shared Sub ReQuery(Optional resetPage As Boolean = True)
        If resetPage Then _currentPage = 0
        LoadData()
        PopulateRows()
        UpdateSummaryLabels()
        UpdatePager()
    End Sub

    ' =========================================================================
    '  DATA
    ' =========================================================================
    Private Shared Sub LoadData()
        _rows.Clear()
        Try
            InventoryMovements.LoadSummary(_filter, _totalRows, _unitsIn, _unitsOut)
            Dim totalPages As Integer = Math.Max(1, CInt(Math.Ceiling(_totalRows / _pageSize)))
            If _currentPage > totalPages - 1 Then _currentPage = totalPages - 1
            If _currentPage < 0 Then _currentPage = 0
            _rows = InventoryMovements.Load(_filter, _pageSize, _currentPage * _pageSize)
            _loadFailed = False
        Catch ex As Exception
            _totalRows = 0 : _unitsIn = 0 : _unitsOut = 0
            ' One message per failure, not one per keystroke in the search box.
            If Not _loadFailed Then
                AppModal.Show("Could not load the transaction history:" & Environment.NewLine & ex.Message,
                              "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End If
            _loadFailed = True
        End Try
    End Sub

    Private Shared Sub UpdateSummaryLabels()
        If _lblCount IsNot Nothing Then _lblCount.Text = _totalRows.ToString("N0")
        If _lblIn IsNot Nothing Then _lblIn.Text = InventoryMovements.Signed(_unitsIn)
        If _lblOut IsNot Nothing Then _lblOut.Text = InventoryMovements.Signed(_unitsOut)
        If _lblNet IsNot Nothing Then
            Dim net As Integer = _unitsIn + _unitsOut
            _lblNet.Text = InventoryMovements.Signed(net)
            _lblNet.ForeColor = If(net < 0, AccentRed, MintDeep)
        End If
    End Sub

    ' =========================================================================
    '  RENDER
    ' =========================================================================
    Private Shared Sub Render()
        _container.SuspendLayout()
        _container.Controls.Clear()
        _reflowAttached = False
        _container.BackColor = MintBg

        Dim tableWrapper As New Panel With {.Dock = DockStyle.Fill, .BackColor = MintBg}

        Dim listArea As New BufferedPanel With {
            .Dock = DockStyle.Fill, .BackColor = CardBg, .Padding = New Padding(1)
        }
        AddHandler listArea.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)

        _scrollHost = New Panel With {
            .Dock = DockStyle.Fill, .BackColor = Color.Transparent, .AutoScroll = True
        }

        Dim colHdr As Panel = BuildColHeader()
        colHdr.Dock = DockStyle.Top
        Dim pagerBar As Panel = BuildPagerBar()
        pagerBar.Dock = DockStyle.Bottom
        Dim bottomPad As New Panel With {.Dock = DockStyle.Bottom, .Height = 12, .BackColor = Color.Transparent}

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
    '  TOP REGION — title, search, filters
    ' =========================================================================
    Private Shared Function BuildTopRegion() As Panel
        Dim region As New Panel With {.BackColor = MintBg, .Height = 128}

        Dim titleBar As New Panel With {.Height = 72, .Dock = DockStyle.Top, .BackColor = Color.Transparent}
        titleBar.Controls.Add(New Label With {
            .Text = "Transaction History",
            .Font = New Font("Segoe UI", 28, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True,
            .Location = New Point(SIDE_PAD, 16)
        })

        Dim searchWrap As Panel = BuildSearchBox()
        searchWrap.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        AddHandler titleBar.Resize,
            Sub()
                searchWrap.Left = titleBar.Width - searchWrap.Width - SIDE_PAD
                searchWrap.Top = (titleBar.Height - searchWrap.Height) \ 2
            End Sub
        titleBar.Controls.Add(searchWrap)

        ' ── Filter bar ──────────────────────────────────────────────────────
        Dim filterBar As New Panel With {.Height = 56, .Dock = DockStyle.Top, .BackColor = Color.Transparent}
        Dim x As Integer = SIDE_PAD
        Const TOP_Y As Integer = 11

        Dim addLabel As Action(Of String) =
            Sub(text As String)
                Dim l As New Label With {
                    .Text = text, .Font = New Font("Segoe UI", 9.5F), .ForeColor = SubText,
                    .AutoSize = True, .BackColor = Color.Transparent
                }
                l.Location = New Point(x, TOP_Y + (FilterFields.FIELD_H - l.PreferredHeight) \ 2)
                filterBar.Controls.Add(l)
                x += l.PreferredWidth + 8
            End Sub

        addLabel("From")
        Dim setFrom As Action(Of Date) = Nothing, setTo As Action(Of Date) = Nothing
        Dim boxFrom As Panel = FilterFields.MakeDateField(140, _filter.FromDate,
            Sub(d As Date)
                _filter.FromDate = d.Date
                If _filter.FromDate > _filter.ToDate Then
                    _filter.ToDate = _filter.FromDate
                    setTo(_filter.ToDate)
                End If
                ReQuery()
            End Sub, setFrom)
        boxFrom.Location = New Point(x, TOP_Y)
        filterBar.Controls.Add(boxFrom)
        x += boxFrom.Width + 16

        addLabel("To")
        Dim boxTo As Panel = FilterFields.MakeDateField(140, _filter.ToDate,
            Sub(d As Date)
                _filter.ToDate = d.Date
                If _filter.ToDate < _filter.FromDate Then
                    _filter.FromDate = _filter.ToDate
                    setFrom(_filter.FromDate)
                End If
                ReQuery()
            End Sub, setTo)
        boxTo.Location = New Point(x, TOP_Y)
        filterBar.Controls.Add(boxTo)
        x += boxTo.Width + 24

        addLabel("Type")
        Dim boxType As Panel = FilterFields.MakeSelectField(
            New List(Of String)(InventoryMovements.TypeFilters), 170, 0,
            Sub(i As Integer)
                _filter.TypeIndex = i
                ReQuery()
            End Sub)
        boxType.Location = New Point(x, TOP_Y)
        filterBar.Controls.Add(boxType)
        x += boxType.Width + 24

        addLabel("Category")
        Dim boxCat As Panel = FilterFields.MakeSelectField(
            _categories.Select(Function(c) c.name).ToList(), 170, 0,
            Sub(i As Integer)
                _filter.CategoryId = If(i >= 0 AndAlso i < _categories.Count, _categories(i).id, 0)
                ReQuery()
            End Sub)
        boxCat.Location = New Point(x, TOP_Y)
        filterBar.Controls.Add(boxCat)

        region.Controls.Add(filterBar)
        region.Controls.Add(titleBar)
        Return region
    End Function

    Private Shared Function BuildSearchBox() As Panel
        Dim searchWrap As New Panel With {.Size = New Size(260, 34), .BackColor = CardBg, .Cursor = Cursors.IBeam}
        Dim focused As Boolean = False
        AddHandler searchWrap.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim rc = searchWrap.ClientRectangle : rc.Inflate(-1, -1)
                Using b As New SolidBrush(CardBg)
                    e.Graphics.FillRoundedRectangle(b, rc, 8)
                End Using
                Using p As New Pen(If(focused, MintPrimary, Theme.Border), If(focused, 1.8F, 1.0F))
                    e.Graphics.DrawRoundedRectangle(p, rc, 8)
                End Using
                Using p As New Pen(Color.FromArgb(160, 180, 178), 1.4F)
                    e.Graphics.DrawEllipse(p, 10, 10, 12, 12)
                    e.Graphics.DrawLine(p, 20, 20, 25, 25)
                End Using
            End Sub

        Dim txt As New TextBox With {
            .Font = New Font("Segoe UI", 10), .BorderStyle = BorderStyle.None,
            .BackColor = CardBg, .ForeColor = SubText, .Text = SEARCH_PLACEHOLDER,
            .Location = New Point(34, 9), .Width = 214
        }

        ' Debounced: every keystroke would otherwise run two queries.
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

        AddHandler txt.Enter,
            Sub()
                focused = True
                If txt.Text = SEARCH_PLACEHOLDER Then
                    txt.Text = ""
                    txt.ForeColor = Theme.TextDark
                End If
                searchWrap.Invalidate()
            End Sub
        AddHandler txt.Leave,
            Sub()
                focused = False
                If String.IsNullOrWhiteSpace(txt.Text) Then
                    txt.Text = SEARCH_PLACEHOLDER
                    txt.ForeColor = SubText
                End If
                searchWrap.Invalidate()
            End Sub
        AddHandler txt.TextChanged,
            Sub()
                If txt.Text = SEARCH_PLACEHOLDER Then Return
                _filter.Search = txt.Text.Trim()
                _searchTimer.Stop()
                _searchTimer.Start()
            End Sub
        AddHandler searchWrap.Click, Sub() txt.Focus()
        searchWrap.Controls.Add(txt)
        Return searchWrap
    End Function

    ' =========================================================================
    '  SUMMARY STRIP
    ' =========================================================================
    Private Shared Function BuildSummaryStrip() As Panel
        Dim bar As New Panel With {
            .Height = SUMMARY_H + 12, .BackColor = MintBg,
            .Padding = New Padding(SIDE_PAD, 0, SIDE_PAD, 12)
        }
        Dim grid As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 4, .RowCount = 1,
            .BackColor = Color.Transparent
        }
        For i As Integer = 0 To 3
            grid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 25.0F))
        Next
        grid.RowStyles.Add(New RowStyle(SizeType.Percent, 100))

        Dim c1 = MakeSummaryCard("Movements", MintDark, "stock changes in this period", _lblCount)
        Dim c2 = MakeSummaryCard("Units In", PositiveClr, "stocked in, returns, + corrections", _lblIn)
        Dim c3 = MakeSummaryCard("Units Out", AccentRed, "sales, damage, - corrections", _lblOut)
        Dim c4 = MakeSummaryCard("Net Change", MintDeep, "change in shelf stock", _lblNet)
        c1.Margin = New Padding(0, 0, 12, 0)
        c2.Margin = New Padding(0, 0, 12, 0)
        c3.Margin = New Padding(0, 0, 12, 0)
        c4.Margin = New Padding(0)
        grid.Controls.Add(c1, 0, 0)
        grid.Controls.Add(c2, 1, 0)
        grid.Controls.Add(c3, 2, 0)
        grid.Controls.Add(c4, 3, 0)
        bar.Controls.Add(grid)
        Return bar
    End Function

    Private Shared Function MakeSummaryCard(caption As String, valueClr As Color, subLabel As String,
                                            ByRef valueLabel As Label) As Panel
        Dim card As New BufferedPanel With {.Dock = DockStyle.Fill, .BackColor = CardBg}
        AddHandler card.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)
        card.Controls.Add(New Label With {
            .Text = caption, .Font = New Font("Segoe UI", 11), .ForeColor = SubText,
            .AutoSize = True, .Location = New Point(22, 14), .BackColor = Color.Transparent
        })
        valueLabel = New Label With {
            .Text = "0", .Font = New Font("Segoe UI", 26, FontStyle.Bold), .ForeColor = valueClr,
            .AutoSize = True, .Location = New Point(20, 36), .BackColor = Color.Transparent
        }
        card.Controls.Add(valueLabel)
        Dim lblSub As New Label With {
            .Text = subLabel, .Font = New Font("Segoe UI", 10), .ForeColor = SubText,
            .AutoSize = False, .AutoEllipsis = True, .Height = 22,
            .Location = New Point(22, 100), .BackColor = Color.Transparent
        }
        card.Controls.Add(lblSub)
        AddHandler card.Resize, Sub() lblSub.Width = Math.Max(40, card.Width - 36)
        Return card
    End Function

    ' =========================================================================
    '  COLUMN HEADER — 44px, same x positions as BuildDataRow
    ' =========================================================================
    Private Shared Function BuildColHeader() As Panel
        Dim hdr As New Panel With {.Height = 44, .BackColor = Color.Transparent}
        Dim labels As Label() = {
            MakeColLabel("DATE"), MakeColLabel("PRODUCT"), MakeColLabel("TYPE"),
            MakeColLabel("QTY"), MakeColLabel("BY"), MakeColLabel("NOTE")
        }
        hdr.Controls.AddRange(labels)
        AddHandler hdr.Resize,
            Sub()
                Dim w = hdr.Width
                labels(0).Left = 24
                labels(1).Left = CInt(w * X_PRODUCT)
                labels(2).Left = CInt(w * X_TYPE)
                labels(3).Left = CInt(w * X_QTY)
                labels(4).Left = CInt(w * X_BY)
                labels(5).Left = CInt(w * X_NOTE)
            End Sub
        AddHandler hdr.Paint,
            Sub(s, e)
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 20, hdr.Height - 1, hdr.Width - 20, hdr.Height - 1)
                End Using
            End Sub
        Return hdr
    End Function

    Private Shared Function MakeColLabel(text As String) As Label
        Return New Label With {
            .Text = text, .Font = New Font("Segoe UI", 10, FontStyle.Bold), .ForeColor = SubText,
            .AutoSize = True, .Location = New Point(0, 13), .BackColor = Color.Transparent
        }
    End Function

    ' =========================================================================
    '  ROWS
    ' =========================================================================
    Private Shared Sub PopulateRows()
        If _scrollHost Is Nothing Then Return
        _scrollHost.SuspendLayout()
        For Each c As Control In _scrollHost.Controls.Cast(Of Control).ToList()
            c.Dispose()
        Next
        _scrollHost.Controls.Clear()

        If _reflowAttached Then
            RemoveHandler _scrollHost.Resize, AddressOf ReflowScrollHost
            _reflowAttached = False
        End If

        If _rows.Count = 0 Then
            _scrollHost.Controls.Add(BuildEmptyRow(
                If(_loadFailed, "The history could not be loaded.",
                   "No stock changes match these filters. Try a wider date range.")))
        Else
            Dim y As Integer = 4
            For Each r In _rows
                Dim row As Panel = BuildDataRow(r)
                row.Location = New Point(0, y)
                row.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
                _scrollHost.Controls.Add(row)
                row.Width = _scrollHost.ClientSize.Width - 4
                y += ROW_H
            Next
            _reflowAttached = True
            AddHandler _scrollHost.Resize, AddressOf ReflowScrollHost
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

    Private Shared Function BuildDataRow(r As InventoryMovements.MovementRow) As Panel
        Dim p As New BufferedPanel With {.Height = ROW_H, .BackColor = Color.Transparent}
        AddHandler p.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                If TypeOf p.Tag Is Boolean AndAlso CBool(p.Tag) Then
                    Using br As New SolidBrush(RowHover)
                        e.Graphics.FillRoundedRectangle(br, New Rectangle(8, 3, p.Width - 16, ROW_H - 6), 10)
                    End Using
                End If
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 20, p.Height - 1, p.Width - 20, p.Height - 1)
                End Using
            End Sub

        Dim mk = Function(text As String, fnt As Font, clr As Color) As Label
                     Return New Label With {
                         .Text = text, .Font = fnt, .ForeColor = clr, .AutoSize = True,
                         .BackColor = Color.Transparent, .UseMnemonic = False
                     }
                 End Function

        Dim lblDate As Label = mk(r.CreatedAt.ToString("MMM d, yyyy"), New Font("Segoe UI", 10.5F), Theme.TextDark)
        Dim lblTime As Label = mk(r.CreatedAt.ToString("h:mm tt"), New Font("Segoe UI", 9), FaintText)

        Dim lblName As Label = mk(If(r.ProductName = "", "—", r.ProductName),
                                  New Font("Segoe UI", 10.5F, FontStyle.Bold), MintDark)
        lblName.AutoSize = False : lblName.AutoEllipsis = True : lblName.Height = 20
        Dim lblSku As Label = mk(r.Sku & If(r.Category = "", "", "   " & ChrW(8226) & "   " & r.Category),
                                 New Font("Segoe UI", 9), FaintText)
        lblSku.AutoSize = False : lblSku.AutoEllipsis = True : lblSku.Height = 18

        Dim typeText As String = r.TypeLabel
        Dim bc = InventoryMovements.BadgeColors(typeText)
        Dim badge As Label = mk(typeText, New Font("Segoe UI", 9, FontStyle.Bold), bc.fore)
        badge.BackColor = bc.back
        badge.Padding = New Padding(10, 4, 10, 4)
        RoundLabel(badge, 10)

        Dim qtyClr As Color = If(r.IsDock, FaintText,
                                 If(r.QtyChange < 0, AccentRed, If(r.QtyChange > 0, PositiveClr, SubText)))
        Dim lblQty As Label = mk(InventoryMovements.Signed(r.QtyChange),
                                 New Font("Segoe UI", 12.5F, FontStyle.Bold), qtyClr)

        Dim lblBy As Label = mk(If(r.UserName = "", "—", r.UserName), New Font("Segoe UI", 10), SubText)
        lblBy.AutoSize = False : lblBy.AutoEllipsis = True : lblBy.Height = 20

        Dim lblNote As Label = mk(If(r.Note = "", "—", r.Note), New Font("Segoe UI", 9.5F),
                                  If(r.Note = "", FaintText, SubText))
        lblNote.AutoSize = False : lblNote.AutoEllipsis = True : lblNote.Height = 20

        p.Controls.AddRange({lblDate, lblTime, lblName, lblSku, badge, lblQty, lblBy, lblNote})

        ' The full note can be longer than its column; hovering shows all of it.
        If r.Note <> "" Then
            Dim tip As New ToolTip()
            tip.SetToolTip(lblNote, r.Note)
            AddHandler p.Disposed, Sub() tip.Dispose()
        End If

        AddHandler p.Resize,
            Sub()
                Dim w = p.Width
                Dim top1 As Integer = (ROW_H \ 2) - 19
                Dim top2 As Integer = (ROW_H \ 2) + 1
                lblDate.Location = New Point(24, top1)
                lblTime.Location = New Point(24, top2)

                Dim xProd As Integer = CInt(w * X_PRODUCT)
                Dim xType As Integer = CInt(w * X_TYPE)
                lblName.Location = New Point(xProd, top1)
                lblName.Width = Math.Max(40, xType - xProd - 16)
                lblSku.Location = New Point(xProd, top2)
                lblSku.Width = lblName.Width

                badge.Location = New Point(xType, (ROW_H - badge.Height) \ 2)
                lblQty.Location = New Point(CInt(w * X_QTY), (ROW_H - lblQty.Height) \ 2)

                Dim xBy As Integer = CInt(w * X_BY)
                Dim xNote As Integer = CInt(w * X_NOTE)
                lblBy.Location = New Point(xBy, (ROW_H - lblBy.Height) \ 2)
                lblBy.Width = Math.Max(30, xNote - xBy - 12)
                lblNote.Location = New Point(xNote, (ROW_H - lblNote.Height) \ 2)
                lblNote.Width = Math.Max(40, w - xNote - 24)
            End Sub

        PropagateHover(p)
        Return p
    End Function

    Private Shared Function BuildEmptyRow(message As String) As Panel
        Dim p As New Panel With {.Height = 90, .Dock = DockStyle.Top, .BackColor = Color.Transparent}
        p.Controls.Add(New Label With {
            .Text = message, .Font = New Font("Segoe UI", 10.5F), .ForeColor = FaintText,
            .AutoSize = True, .Location = New Point(22, 28), .BackColor = Color.Transparent
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

        _btnPrev = MakePagerButton("‹")
        _btnNext = MakePagerButton("›")
        _lblPageInfo = New Label With {
            .Font = New Font("Segoe UI", 9.5F), .ForeColor = SubText,
            .AutoSize = True, .BackColor = Color.Transparent
        }

        Dim place As Action =
            Sub()
                Dim cx As Integer = wrapper.Width \ 2
                _btnPrev.Left = cx - _btnPrev.Width - _lblPageInfo.Width \ 2 - 8
                _btnPrev.Top = (wrapper.Height - _btnPrev.Height) \ 2
                _lblPageInfo.Left = cx - _lblPageInfo.Width \ 2
                _lblPageInfo.Top = (wrapper.Height - _lblPageInfo.Height) \ 2
                _btnNext.Left = cx + _lblPageInfo.Width \ 2 + 8
                _btnNext.Top = (wrapper.Height - _btnNext.Height) \ 2
            End Sub
        AddHandler wrapper.Resize, Sub() place()
        ' The caption changes length page to page; keep the arrows beside it.
        AddHandler _lblPageInfo.SizeChanged, Sub() place()

        AddHandler _btnPrev.Click,
            Sub()
                If _currentPage > 0 Then
                    _currentPage -= 1
                    ReQuery(resetPage:=False)
                End If
            End Sub
        AddHandler _btnNext.Click,
            Sub()
                Dim totalPages As Integer = Math.Max(1, CInt(Math.Ceiling(_totalRows / _pageSize)))
                If _currentPage < totalPages - 1 Then
                    _currentPage += 1
                    ReQuery(resetPage:=False)
                End If
            End Sub

        wrapper.Controls.AddRange({_btnPrev, _lblPageInfo, _btnNext})
        Return wrapper
    End Function

    Private Shared Function MakePagerButton(glyph As String) As Button
        Dim b As New Button With {
            .Text = glyph, .FlatStyle = FlatStyle.Flat, .Cursor = Cursors.Hand,
            .Size = New Size(32, 32), .BackColor = Theme.Border,
            .ForeColor = MintDark, .Font = New Font("Segoe UI", 14, FontStyle.Bold),
            .TextAlign = ContentAlignment.MiddleCenter
        }
        b.FlatAppearance.BorderSize = 0
        Return b
    End Function

    Private Shared Sub UpdatePager()
        If _lblPageInfo Is Nothing Then Return
        Dim totalPages As Integer = Math.Max(1, CInt(Math.Ceiling(_totalRows / _pageSize)))
        If _totalRows = 0 Then
            _lblPageInfo.Text = "No stock changes"
        Else
            Dim firstRow As Integer = _currentPage * _pageSize + 1
            Dim lastRow As Integer = Math.Min(_totalRows, firstRow + _rows.Count - 1)
            _lblPageInfo.Text =
                $"Showing {firstRow:N0}–{lastRow:N0} of {_totalRows:N0}   ·   Page {_currentPage + 1} of {totalPages}"
        End If
        _btnPrev.Enabled = (_currentPage > 0)
        _btnNext.Enabled = (_currentPage < totalPages - 1)
    End Sub

    ' =========================================================================
    '  HELPERS
    ' =========================================================================
    Private Shared Sub RoundLabel(lbl As Label, radius As Integer)
        Dim apply As Action =
            Sub()
                If lbl.Width <= radius OrElse lbl.Height <= radius Then Return
                Dim path As New GraphicsPath()
                path.AddArc(0, 0, radius, radius, 180, 90)
                path.AddArc(lbl.Width - radius, 0, radius, radius, 270, 90)
                path.AddArc(lbl.Width - radius, lbl.Height - radius, radius, radius, 0, 90)
                path.AddArc(0, lbl.Height - radius, radius, radius, 90, 90)
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
