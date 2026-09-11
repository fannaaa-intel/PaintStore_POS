Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Text.RegularExpressions
Imports System.Windows.Forms
Imports MySql.Data.MySqlClient

' =============================================================================
'  AdminColorFormulasPanelBuilder  —  Admin ▸ Color Formulas  (full CRUD)
'
'  Parent/child: color_formulas 1—* formula_components. Nothing else in the app
'  touches either table.
'
'  TWO VIEWS, swapped into the same container (the ProcurementSuppliers pattern):
'    RenderList()          — searchable list of formulas + swatch + counts
'    RenderForm(formulaId) — parent fields and the component recipe together
'                            (formulaId = 0 means "new")
'
'  Every multi-step write runs inside one transaction, so a failure part-way
'  through can never leave a formula without its components, or components
'  pointing at a formula that was rolled back.
' =============================================================================
Public Class AdminColorFormulasPanelBuilder

    ' ── PALETTE  (matches the other ADMIN panels) ────────────────────────────
    Private Shared ReadOnly MintDark As Color = Theme.Dark
    Private Shared ReadOnly MintPrimary As Color = Theme.Primary
    Private Shared ReadOnly MintBg As Color = Theme.PageBg
    Private Shared ReadOnly CardBg As Color = Color.White
    Private Shared ReadOnly SubText As Color = Theme.SubText
    Private Shared ReadOnly FaintText As Color = Theme.FaintText
    Private Shared ReadOnly RowDivider As Color = Theme.RowDivider
    Private Shared ReadOnly RowHover As Color = Theme.RowHover
    Private Shared ReadOnly AccentRed As Color = Theme.Danger
    Private Shared ReadOnly FieldBg As Color = Theme.PageBg
    Private Shared ReadOnly FieldBorder As Color = Theme.BorderMuted

    Private Shared ReadOnly TintMint As (fore As Color, back As Color) =
        (Color.FromArgb(0, 120, 92), Color.FromArgb(222, 246, 238))
    Private Shared ReadOnly TintGrey As (fore As Color, back As Color) =
        (Color.FromArgb(90, 114, 107), Color.FromArgb(238, 242, 240))

    Private Shared ReadOnly HexPattern As New Regex("^#[0-9A-Fa-f]{6}$")

    ' ── DATA MODEL ────────────────────────────────────────────────────────────
    Private Class FormulaRow
        Public FormulaId As Integer
        Public Name As String
        Public PaintType As String
        Public SizeLabel As String
        Public ColorHex As String
        Public ComponentCount As Integer
    End Class

    Private Class FormulaDetail
        Public FormulaId As Integer
        Public Name As String = ""
        Public PaintType As String = ""
        Public BaseProductId As Integer = 0
        Public SizeLabel As String = ""
        Public ColorHex As String = ""
        Public Notes As String = ""
    End Class

    Private Class ComponentRow
        Public ColorantName As String
        Public AmountMl As Decimal
    End Class

    Private Class ProductOption
        Public ProductId As Integer
        Public Name As String
        Public Overrides Function ToString() As String
            Return Name
        End Function
    End Class

    ' One editable component line in the form.
    Private Class ComponentEditor
        Public Row As Panel
        Public NameBox As TextBox
        Public AmtBox As TextBox
    End Class

    ' ── STATE ─────────────────────────────────────────────────────────────────
    Private Const SIDE_PAD As Integer = 24
    Private Const ROW_H As Integer = 68
    Private Const HDR_H As Integer = 44
    Private Const PAD_TOP As Integer = 4
    Private Const PAD_BOTTOM As Integer = 12
    Private Const FIELD_H As Integer = 44
    Private Const BLOCK_H As Integer = 20 + FIELD_H + 14
    Private Const COMP_ROW_H As Integer = 52
    Private Const SEARCH_PLACEHOLDER As String = "Search by name or paint type…"

    Private Shared _container As Panel
    Private Shared _scrollHost As Panel
    Private Shared _listArea As Panel
    Private Shared _reflowAttached As Boolean = False

    Private Shared _allRows As New List(Of FormulaRow)
    Private Shared _visibleRows As New List(Of FormulaRow)
    Private Shared _searchQuery As String = ""

    ' Form state
    Private Shared _editingId As Integer = 0
    Private Shared _txtName As TextBox
    Private Shared _cboPaintType As ComboBox
    Private Shared _cboBaseProduct As ComboBox
    Private Shared _txtSize As TextBox
    Private Shared _txtHex As TextBox
    Private Shared _txtNotes As TextBox
    Private Shared _swatch As Panel
    Private Shared _compHost As Panel
    Private Shared _compCard As Panel
    Private Shared _compEditors As New List(Of ComponentEditor)

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Shared Sub LoadColorFormulas(container As Panel)
        _container = container
        _searchQuery = ""
        LoadList()
        RenderList()
    End Sub

    ' =========================================================================
    '  DATA — LIST
    ' =========================================================================
    Private Shared Sub LoadList()
        _allRows.Clear()
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Dim sql As String =
                    "SELECT f.formula_id, f.name, f.paint_type, f.size_label, f.color_hex, " &
                    "COUNT(c.component_id) AS component_count " &
                    "FROM color_formulas f " &
                    "LEFT JOIN formula_components c ON c.formula_id = f.formula_id " &
                    "GROUP BY f.formula_id, f.name, f.paint_type, f.size_label, f.color_hex " &
                    "ORDER BY f.name ASC"
                Using cmd As New MySqlCommand(sql, conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            _allRows.Add(New FormulaRow With {
                                .FormulaId = SafeInt(rdr("formula_id")),
                                .Name = SafeStr(rdr("name")),
                                .PaintType = SafeStr(rdr("paint_type")),
                                .SizeLabel = SafeStr(rdr("size_label")),
                                .ColorHex = SafeStr(rdr("color_hex")),
                                .ComponentCount = SafeInt(rdr("component_count"))
                            })
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load color formulas: " & ex.Message, "Database Error",
                          MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
        ApplySearch()
    End Sub

    Private Shared Sub ApplySearch()
        _visibleRows.Clear()
        If String.IsNullOrWhiteSpace(_searchQuery) Then
            _visibleRows.AddRange(_allRows)
        Else
            Dim q As String = _searchQuery.ToLower()
            For Each r In _allRows
                If r.Name.ToLower().Contains(q) OrElse r.PaintType.ToLower().Contains(q) Then
                    _visibleRows.Add(r)
                End If
            Next
        End If
    End Sub

    Private Shared Function LoadDetail(formulaId As Integer) As FormulaDetail
        Dim d As New FormulaDetail With {.FormulaId = formulaId}
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT formula_id, name, paint_type, base_product_id, size_label, " &
                    "color_hex, notes FROM color_formulas WHERE formula_id = @id LIMIT 1", conn)
                    cmd.Parameters.AddWithValue("@id", formulaId)
                    Using rdr = cmd.ExecuteReader()
                        If rdr.Read() Then
                            d.Name = SafeStr(rdr("name"))
                            d.PaintType = SafeStr(rdr("paint_type"))
                            d.BaseProductId = SafeInt(rdr("base_product_id"))
                            d.SizeLabel = SafeStr(rdr("size_label"))
                            d.ColorHex = SafeStr(rdr("color_hex"))
                            d.Notes = SafeStr(rdr("notes"))
                        End If
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load the formula: " & ex.Message, "Database Error",
                          MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
        Return d
    End Function

    Private Shared Function LoadComponents(formulaId As Integer) As List(Of ComponentRow)
        Dim list As New List(Of ComponentRow)
        If formulaId <= 0 Then Return list
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT colorant_name, amount_ml FROM formula_components " &
                    "WHERE formula_id = @id ORDER BY component_id", conn)
                    cmd.Parameters.AddWithValue("@id", formulaId)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            list.Add(New ComponentRow With {
                                .ColorantName = SafeStr(rdr("colorant_name")),
                                .AmountMl = If(IsDBNull(rdr("amount_ml")), 0D,
                                               Convert.ToDecimal(rdr("amount_ml")))
                            })
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load the formula components: " & ex.Message, "Database Error",
                          MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
        Return list
    End Function

    Private Shared Function LoadProducts() As List(Of ProductOption)
        Dim list As New List(Of ProductOption)
        list.Add(New ProductOption With {.ProductId = 0, .Name = "None"})
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT product_id, name FROM products WHERE is_active = 1 ORDER BY name", conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            list.Add(New ProductOption With {
                                .ProductId = SafeInt(rdr("product_id")),
                                .Name = SafeStr(rdr("name"))
                            })
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load base products: " & ex.Message, "Database Error",
                          MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
        Return list
    End Function

    ' Paint types are reused from products so formulas stay consistent with
    ' the catalogue; the combo is editable so a new type can still be typed.
    Private Shared Function LoadPaintTypes() As List(Of String)
        Dim list As New List(Of String)
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT DISTINCT paint_type FROM products " &
                    "WHERE paint_type IS NOT NULL AND paint_type <> '' ORDER BY paint_type", conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            list.Add(SafeStr(rdr("paint_type")))
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            System.Diagnostics.Debug.WriteLine("LoadPaintTypes: " & ex.Message)
        End Try
        Return list
    End Function

    Private Shared Function SafeInt(o As Object) As Integer
        If o Is Nothing OrElse IsDBNull(o) Then Return 0
        Return Convert.ToInt32(o)
    End Function

    Private Shared Function SafeStr(o As Object) As String
        If o Is Nothing OrElse IsDBNull(o) Then Return ""
        Return o.ToString()
    End Function

    ' =========================================================================
    '  VIEW 1 — LIST
    ' =========================================================================
    Private Shared Sub RenderList()
        _container.SuspendLayout()
        _container.Controls.Clear()
        _reflowAttached = False
        _container.BackColor = MintBg

        Dim tableWrapper As New Panel With {
            .Dock = DockStyle.Fill, .BackColor = MintBg, .Padding = New Padding(0)
        }

        _listArea = New BufferedPanel With {
            .Dock = DockStyle.Top,
            .Height = CardHeightFor(_visibleRows.Count),
            .BackColor = CardBg,
            .Padding = New Padding(1)
        }
        AddHandler _listArea.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)

        _scrollHost = New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = Color.Transparent,
            .AutoScroll = True,
            .Padding = New Padding(0)
        }

        Dim colHdr As Panel = BuildColHeader()
        colHdr.Dock = DockStyle.Top

        Dim bottomPad As New Panel With {
            .Dock = DockStyle.Bottom, .Height = PAD_BOTTOM, .BackColor = Color.Transparent
        }

        _listArea.Controls.Add(bottomPad)
        _listArea.Controls.Add(colHdr)
        _listArea.Controls.Add(_scrollHost)
        colHdr.BringToFront()
        _scrollHost.BringToFront()
        _scrollHost.AutoScrollMinSize = New Size(0, 1)

        tableWrapper.Controls.Add(_listArea)
        tableWrapper.Controls.Add(New Panel With {.Dock = DockStyle.Bottom, .Height = 16, .BackColor = MintBg})
        tableWrapper.Controls.Add(New Panel With {.Dock = DockStyle.Top, .Height = 16, .BackColor = MintBg})
        tableWrapper.Controls.Add(New Panel With {.Dock = DockStyle.Left, .Width = SIDE_PAD, .BackColor = MintBg})
        tableWrapper.Controls.Add(New Panel With {.Dock = DockStyle.Right, .Width = SIDE_PAD, .BackColor = MintBg})

        Dim topRegion As Panel = BuildListTop()
        topRegion.Dock = DockStyle.Top

        Dim tileStrip As Panel = BuildTileStrip()
        tileStrip.Dock = DockStyle.Top

        _container.Controls.Add(tableWrapper)
        _container.Controls.Add(tileStrip)
        _container.Controls.Add(topRegion)
        _container.ResumeLayout()

        PopulateRows()
    End Sub

    Private Shared Function CardHeightFor(rowCount As Integer) As Integer
        Dim contentH As Integer = If(rowCount = 0, 90, rowCount * ROW_H)
        Dim cardH As Integer = HDR_H + PAD_TOP + contentH + PAD_BOTTOM + 2
        Dim maxH As Integer = _container.ClientSize.Height - 72 - (SUMMARY_H + 12) - 32
        If maxH > 160 AndAlso cardH > maxH Then cardH = maxH
        Return cardH
    End Function

    ' =========================================================================
    '  SUMMARY TILES
    ' =========================================================================
    Private Const SUMMARY_H As Integer = 140

    Private Shared Function BuildTileStrip() As Panel
        Dim bar As New Panel With {
            .Height = SUMMARY_H + 12, .BackColor = MintBg,
            .Padding = New Padding(SIDE_PAD, 0, SIDE_PAD, 12)
        }
        Dim grid As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 3, .RowCount = 1,
            .BackColor = Color.Transparent,
            .CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        }
        For i As Integer = 0 To 2
            grid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 33.33F))
        Next
        grid.RowStyles.Add(New RowStyle(SizeType.Percent, 100))

        Dim total As Integer = _allRows.Count
        Dim colorants As Integer = 0
        Dim types As New List(Of String)
        For Each r In _allRows
            colorants += r.ComponentCount
            Dim t As String = If(r.PaintType, "").Trim()
            If t <> "" AndAlso Not types.Contains(t, StringComparer.OrdinalIgnoreCase) Then types.Add(t)
        Next

        Dim defs = {
            ("Formulas", total.ToString("N0"), "on file", MintPrimary),
            ("Paint Types", types.Count.ToString("N0"), "distinct types", MintDark),
            ("Colorants", colorants.ToString("N0"), "across all recipes", Color.FromArgb(45, 80, 150))
        }
        For i As Integer = 0 To 2
            Dim d = defs(i)
            Dim card = MakeSummaryCard(d.Item1, d.Item4, d.Item2, d.Item3)
            card.Margin = New Padding(0, 0, If(i < 2, 12, 0), 0)
            grid.Controls.Add(card, i, 0)
        Next
        bar.Controls.Add(grid)
        Return bar
    End Function

    Private Shared Function MakeSummaryCard(caption As String, valueClr As Color,
                                            value As String, subLabel As String) As Panel
        Dim card As New BufferedPanel With {.Dock = DockStyle.Fill, .BackColor = CardBg}
        AddHandler card.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)
        card.Controls.Add(New Label With {
            .Text = caption, .Font = New Font("Segoe UI", 11), .ForeColor = SubText,
            .AutoSize = True, .Location = New Point(22, 14), .BackColor = Color.Transparent})
        card.Controls.Add(New Label With {
            .Text = value, .Font = New Font("Segoe UI", 26, FontStyle.Bold), .ForeColor = valueClr,
            .AutoSize = True, .Location = New Point(20, 36), .BackColor = Color.Transparent})
        card.Controls.Add(New Label With {
            .Text = subLabel, .Font = New Font("Segoe UI", 10), .ForeColor = SubText,
            .AutoSize = True, .Location = New Point(22, 100), .BackColor = Color.Transparent})
        Return card
    End Function

    Private Shared Function BuildListTop() As Panel
        Dim region As New Panel With {.BackColor = MintBg, .AutoSize = False, .Height = 72}
        Dim titleBar As New Panel With {
            .Height = 72, .Dock = DockStyle.Top, .BackColor = Color.Transparent
        }

        titleBar.Controls.Add(New Label With {
            .Text = "Color Formulas",
            .Font = New Font("Segoe UI", 28, FontStyle.Bold),
            .ForeColor = MintDark,
            .AutoSize = True,
            .Location = New Point(SIDE_PAD, 16)
        })

        Dim btnNew As Button = MakePillButton("+  New Formula", 150, MintPrimary, Color.White, True)
        AddHandler btnNew.Click, Sub() RenderForm(0)

        Dim btnRefresh As Button = MakePillButton("⟳  Refresh", 100,
                                                  Color.FromArgb(220, 235, 232), MintDark, False)
        AddHandler btnRefresh.Click, Sub() LoadColorFormulas(_container)

        Dim searchWrap As Panel = Nothing
        Dim txtSearch As TextBox = Nothing
        BuildSearchBox(searchWrap, txtSearch)

        AddHandler titleBar.Resize,
            Sub()
                btnNew.Left = titleBar.Width - btnNew.Width - SIDE_PAD
                btnNew.Top = (titleBar.Height - btnNew.Height) \ 2
                btnRefresh.Left = btnNew.Left - btnRefresh.Width - 10
                btnRefresh.Top = (titleBar.Height - btnRefresh.Height) \ 2
                searchWrap.Left = btnRefresh.Left - searchWrap.Width - 10
                searchWrap.Top = (titleBar.Height - searchWrap.Height) \ 2
            End Sub

        titleBar.Controls.Add(searchWrap)
        titleBar.Controls.Add(btnRefresh)
        titleBar.Controls.Add(btnNew)
        region.Controls.Add(titleBar)
        Return region
    End Function

    Private Shared Sub BuildSearchBox(ByRef wrap As Panel, ByRef txt As TextBox)
        wrap = New Panel With {
            .Size = New Size(250, 34),
            .BackColor = CardBg,
            .Cursor = Cursors.IBeam,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        Dim focusedFlag As Boolean = False
        Dim wrapLocal As Panel = wrap

        AddHandler wrap.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim rc = wrapLocal.ClientRectangle : rc.Inflate(-1, -1)
                Using b As New SolidBrush(CardBg)
                    e.Graphics.FillRoundedRectangle(b, rc, 8)
                End Using
                Using p As New Pen(If(focusedFlag, MintPrimary, RowDivider),
                                   If(focusedFlag, 1.8F, 1.0F))
                    e.Graphics.DrawRoundedRectangle(p, rc, 8)
                End Using
                Using p As New Pen(Color.FromArgb(160, 180, 178), 1.4F)
                    e.Graphics.DrawEllipse(p, 10, 10, 12, 12)
                    e.Graphics.DrawLine(p, 20, 20, 25, 25)
                End Using
            End Sub

        txt = New TextBox With {
            .Font = New Font("Segoe UI", 10),
            .BorderStyle = BorderStyle.None,
            .BackColor = CardBg,
            .ForeColor = SubText,
            .Text = SEARCH_PLACEHOLDER,
            .Location = New Point(34, 9),
            .Width = 204
        }
        Dim txtLocal As TextBox = txt

        AddHandler txt.Enter,
            Sub()
                focusedFlag = True
                If txtLocal.Text = SEARCH_PLACEHOLDER Then
                    txtLocal.Text = ""
                    txtLocal.ForeColor = Color.FromArgb(45, 45, 45)
                End If
                wrapLocal.Invalidate()
            End Sub
        AddHandler txt.Leave,
            Sub()
                focusedFlag = False
                If String.IsNullOrWhiteSpace(txtLocal.Text) Then
                    txtLocal.Text = SEARCH_PLACEHOLDER
                    txtLocal.ForeColor = SubText
                End If
                wrapLocal.Invalidate()
            End Sub
        AddHandler txt.TextChanged,
            Sub()
                If txtLocal.Text = SEARCH_PLACEHOLDER Then Return
                _searchQuery = txtLocal.Text.Trim()
                ApplySearch()
                PopulateRows()
            End Sub
        AddHandler wrap.Click, Sub() txtLocal.Focus()
        wrap.Controls.Add(txt)
    End Sub

    Private Shared Function BuildColHeader() As Panel
        Dim hdr As New Panel With {.Height = HDR_H, .BackColor = Color.Transparent}
        Dim labels As Label() = {
            MakeColLabel("FORMULA", New Point(76, 13)),
            MakeColLabel("PAINT TYPE", New Point(0, 13)),
            MakeColLabel("SIZE", New Point(0, 13)),
            MakeColLabel("COMPONENTS", New Point(0, 13)),
            MakeColLabel("ACTIONS", New Point(0, 13))
        }
        hdr.Controls.AddRange(labels)

        AddHandler hdr.Resize,
            Sub()
                Dim w = hdr.Width
                labels(0).Left = 76
                labels(1).Left = CInt(w * 0.3)
                labels(2).Left = CInt(w * 0.44)
                labels(3).Left = CInt(w * 0.55)
                labels(4).Left = CInt(w * 0.72)
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

    Private Shared Sub PopulateRows()
        If _scrollHost Is Nothing Then Return
        If _listArea IsNot Nothing Then _listArea.Height = CardHeightFor(_visibleRows.Count)

        _scrollHost.SuspendLayout()
        _scrollHost.Controls.Clear()

        If _reflowAttached Then
            RemoveHandler _scrollHost.Resize, AddressOf ReflowScrollHost
            _reflowAttached = False
        End If

        If _visibleRows.Count = 0 Then
            _scrollHost.Controls.Add(BuildEmptyRow(
                If(String.IsNullOrWhiteSpace(_searchQuery),
                   "No color formulas yet. Use the New Formula button to add one.",
                   $"No formulas match ""{_searchQuery}"".")))
        Else
            Dim y As Integer = PAD_TOP
            For Each r As FormulaRow In _visibleRows
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

    Private Shared Function BuildDataRow(r As FormulaRow, height As Integer) As Panel
        Dim p As New BufferedPanel With {.Height = height, .BackColor = Color.Transparent}
        Dim swatchClr As Color = HexToColor(r.ColorHex, Color.FromArgb(226, 232, 230))

        AddHandler p.Paint,
            Sub(s, e)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                If TypeOf p.Tag Is Boolean AndAlso CBool(p.Tag) Then
                    Dim rect As New Rectangle(8, 3, p.Width - 16, height - 6)
                    Using br As New SolidBrush(RowHover)
                        g.FillRoundedRectangle(br, rect, 10)
                    End Using
                End If
                ' Colour swatch, filled from color_hex
                Dim sw As New Rectangle(24, (height - 34) \ 2, 34, 34)
                Using br As New SolidBrush(swatchClr)
                    g.FillRoundedRectangle(br, sw, 8)
                End Using
                Using pen As New Pen(Color.FromArgb(60, 0, 0, 0), 1.0F)
                    g.DrawRoundedRectangle(pen, sw, 8)
                End Using
                Using pen As New Pen(RowDivider, 1)
                    g.DrawLine(pen, 20, p.Height - 1, p.Width - 20, p.Height - 1)
                End Using
            End Sub

        Dim lblName As New Label With {
            .Text = If(r.Name = "", "—", r.Name),
            .Font = New Font("Segoe UI", 11, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblHex As New Label With {
            .Text = If(r.ColorHex = "", "no hex", r.ColorHex.ToUpper()),
            .Font = New Font("Segoe UI", 9),
            .ForeColor = FaintText, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblType As New Label With {
            .Text = If(r.PaintType = "", "—", r.PaintType),
            .Font = New Font("Segoe UI", 10.5F),
            .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblSize As New Label With {
            .Text = If(r.SizeLabel = "", "—", r.SizeLabel),
            .Font = New Font("Segoe UI", 10.5F),
            .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent
        }

        Dim tint = If(r.ComponentCount > 0, TintMint, TintGrey)
        Dim compPill As New Label With {
            .Text = r.ComponentCount.ToString("N0") &
                    If(r.ComponentCount = 1, " colorant", " colorants"),
            .Font = New Font("Segoe UI Semibold", 9.0F),
            .ForeColor = tint.fore, .BackColor = tint.back,
            .AutoSize = True, .Padding = New Padding(10, 4, 10, 4)
        }
        RoundLabel(compPill, 10)

        Dim btnEdit As Button = MakeActionButton("View / Edit", Color.FromArgb(220, 235, 232), MintDark, 96)
        Dim btnDelete As Button = MakeActionButton("Delete", Color.FromArgb(255, 232, 232), AccentRed, 84)
        AddHandler btnEdit.Click, Sub() RenderForm(r.FormulaId)
        AddHandler btnDelete.Click, Sub() DeleteFormula(r)

        p.Controls.AddRange({lblName, lblHex, lblType, lblSize, compPill, btnEdit, btnDelete})

        AddHandler p.Resize,
            Sub()
                Dim w = p.Width
                lblName.Left = 76
                lblName.Top = (height \ 2) - lblName.Height - 1
                lblHex.Left = 76
                lblHex.Top = (height \ 2) + 2
                lblType.Left = CInt(w * 0.3) : lblType.Top = (height - lblType.Height) \ 2
                lblSize.Left = CInt(w * 0.44) : lblSize.Top = (height - lblSize.Height) \ 2
                compPill.Left = CInt(w * 0.55) : compPill.Top = (height - compPill.Height) \ 2
                Dim ax As Integer = CInt(w * 0.72)
                Dim ay As Integer = (height - btnEdit.Height) \ 2
                btnEdit.Left = ax : btnEdit.Top = ay
                btnDelete.Left = btnEdit.Right + 6 : btnDelete.Top = ay
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
    '  VIEW 2 — ADD / EDIT FORM
    ' =========================================================================
    Private Shared Sub RenderForm(formulaId As Integer)
        _editingId = formulaId
        _compEditors.Clear()

        Dim detail As FormulaDetail = If(formulaId > 0, LoadDetail(formulaId), New FormulaDetail())
        Dim comps As List(Of ComponentRow) = LoadComponents(formulaId)
        Dim products As List(Of ProductOption) = LoadProducts()
        Dim paintTypes As List(Of String) = LoadPaintTypes()

        _container.SuspendLayout()
        _container.Controls.Clear()
        _container.BackColor = MintBg

        Dim host As New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = MintBg,
            .AutoScroll = True,
            .Padding = New Padding(SIDE_PAD, 12, SIDE_PAD, SIDE_PAD)
        }

        ' Reverse add order: trailing spacer first so it lands at the bottom.
        host.Controls.Add(Spacer())
        host.Controls.Add(BuildActionRow())
        host.Controls.Add(Spacer())
        _compCard = BuildComponentsCard(comps)
        host.Controls.Add(_compCard)
        host.Controls.Add(Spacer())
        host.Controls.Add(BuildDetailsCard(detail, products, paintTypes))

        Dim topRegion As Panel = BuildFormTop(formulaId)
        topRegion.Dock = DockStyle.Top

        _container.Controls.Add(host)
        _container.Controls.Add(topRegion)
        _container.ResumeLayout()
    End Sub

    Private Shared Function Spacer() As Panel
        Return New Panel With {.Dock = DockStyle.Top, .Height = 16, .BackColor = MintBg}
    End Function

    Private Shared Function BuildFormTop(formulaId As Integer) As Panel
        Dim region As New Panel With {.BackColor = MintBg, .AutoSize = False, .Height = 72}
        Dim titleBar As New Panel With {
            .Height = 72, .Dock = DockStyle.Top, .BackColor = Color.Transparent
        }

        titleBar.Controls.Add(New Label With {
            .Text = If(formulaId > 0, "Edit Formula", "New Formula"),
            .Font = New Font("Segoe UI", 28, FontStyle.Bold),
            .ForeColor = MintDark,
            .AutoSize = True,
            .Location = New Point(SIDE_PAD, 16)
        })

        Dim btnBack As Button = MakePillButton("‹  Back to list", 130,
                                               Color.FromArgb(220, 235, 232), MintDark, False)
        AddHandler btnBack.Click, Sub() LoadColorFormulas(_container)

        AddHandler titleBar.Resize,
            Sub()
                btnBack.Left = titleBar.Width - btnBack.Width - SIDE_PAD
                btnBack.Top = (titleBar.Height - btnBack.Height) \ 2
            End Sub

        titleBar.Controls.Add(btnBack)
        region.Controls.Add(titleBar)
        Return region
    End Function

    ' ── Card: parent fields ──────────────────────────────────────────────────
    Private Shared Function BuildDetailsCard(d As FormulaDetail,
                                             products As List(Of ProductOption),
                                             paintTypes As List(Of String)) As Panel
        Dim notesH As Integer = 88
        Dim cardH As Integer = 56 + BLOCK_H * 3 + (20 + notesH) + 24

        Dim wrap As New Panel With {.Dock = DockStyle.Top, .Height = cardH, .BackColor = MintBg}
        Dim card As New BufferedPanel With {.Dock = DockStyle.Fill, .BackColor = CardBg}
        AddHandler card.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)

        card.Controls.Add(New Label With {
            .Text = "Formula Details",
            .Font = New Font("Segoe UI Semibold", 13),
            .ForeColor = MintDark,
            .AutoSize = True,
            .Location = New Point(24, 18),
            .BackColor = Color.Transparent
        })

        _txtName = MakeTextBox(d.Name)
        _txtSize = MakeTextBox(d.SizeLabel)
        _txtHex = MakeTextBox(d.ColorHex)

        _cboPaintType = New ComboBox With {
            .Font = New Font("Segoe UI", 11.0F),
            .DropDownStyle = ComboBoxStyle.DropDown,   ' editable — new types allowed
            .FlatStyle = FlatStyle.Flat,
            .BackColor = FieldBg,
            .ForeColor = Color.FromArgb(35, 50, 46)
        }
        For Each t In paintTypes
            _cboPaintType.Items.Add(t)
        Next
        _cboPaintType.Text = d.PaintType
        ' An editable combo cannot be OwnerDrawn on its edit box, but its
        ' dropdown list can - drop-down items get the mint highlight.
        StyleComboList(_cboPaintType)

        _cboBaseProduct = New ComboBox With {
            .Font = New Font("Segoe UI", 11.0F),
            .DropDownStyle = ComboBoxStyle.DropDownList,
            .FlatStyle = FlatStyle.Flat,
            .BackColor = FieldBg,
            .ForeColor = Color.FromArgb(35, 50, 46)
        }
        StyleComboList(_cboBaseProduct)
        For Each po In products
            _cboBaseProduct.Items.Add(po)
        Next
        Dim baseIdx As Integer = 0
        For i As Integer = 0 To products.Count - 1
            If products(i).ProductId = d.BaseProductId Then
                baseIdx = i
                Exit For
            End If
        Next
        _cboBaseProduct.SelectedIndex = baseIdx

        _txtNotes = New TextBox With {
            .Font = New Font("Segoe UI", 10.5F),
            .BorderStyle = BorderStyle.None,
            .BackColor = FieldBg,
            .ForeColor = Color.FromArgb(35, 50, 46),
            .Multiline = True,
            .ScrollBars = ScrollBars.Vertical,
            .Text = d.Notes
        }

        Dim fName = AddField(card, "Name  (required)", _txtName)
        Dim fType = AddField(card, "Paint type", _cboPaintType)
        Dim fBase = AddField(card, "Base product (optional)", _cboBaseProduct)
        Dim fSize = AddField(card, "Size label", _txtSize)
        Dim fHex = AddField(card, "Color hex  (#RRGGBB, optional)", _txtHex)
        Dim fNotes = AddField(card, "Notes", _txtNotes)

        ' Live swatch beside the hex field.
        _swatch = New Panel With {.Size = New Size(FIELD_H, FIELD_H), .BackColor = Color.Transparent, .Cursor = Cursors.Hand}
        AddHandler _swatch.Paint,
            Sub(s, e)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim rc = _swatch.ClientRectangle : rc.Inflate(-1, -1)
                Dim txt As String = _txtHex.Text.Trim()
                Dim valid As Boolean = (txt = "") OrElse HexPattern.IsMatch(txt)
                Using br As New SolidBrush(HexToColor(txt, Color.FromArgb(238, 242, 240)))
                    g.FillRoundedRectangle(br, rc, 9)
                End Using
                Using pen As New Pen(If(valid, FieldBorder, AccentRed), If(valid, 1.0F, 1.8F))
                    g.DrawRoundedRectangle(pen, rc, 9)
                End Using
            End Sub
        card.Controls.Add(_swatch)
        AddHandler _txtHex.TextChanged, Sub() _swatch.Invalidate()
        ' Clicking the swatch opens the system colour picker and writes the
        ' chosen colour back as #RRGGBB - the same behaviour the Inventory Item
        ' List uses. Before this the swatch looked clickable but did nothing.
        Dim openColorPicker As Action =
            Sub()
                Using cd As New ColorDialog With {.FullOpen = True,
                    .Color = HexToColor(_txtHex.Text.Trim(), MintPrimary)}
                    If cd.ShowDialog() = DialogResult.OK Then
                        _txtHex.Text = "#" & cd.Color.R.ToString("X2") &
                            cd.Color.G.ToString("X2") & cd.Color.B.ToString("X2")
                    End If
                End Using
            End Sub
        AddHandler _swatch.Click, Sub() openColorPicker()

        AddHandler card.Resize,
            Sub()
                Dim pad As Integer = 24
                Dim gap As Integer = 16
                Dim fullW As Integer = Math.Max(140, card.Width - pad * 2)
                Dim colW As Integer = Math.Max(120, (fullW - gap) \ 2)
                Dim rightX As Integer = pad + colW + gap

                Dim y As Integer = 56
                PlaceField(fName, pad, y, colW)
                PlaceField(fType, rightX, y, colW)
                y += BLOCK_H
                PlaceField(fBase, pad, y, colW)
                PlaceField(fSize, rightX, y, colW)
                y += BLOCK_H
                ' Hex field leaves room for the swatch on its right
                PlaceField(fHex, pad, y, colW - FIELD_H - 10)
                _swatch.Left = pad + colW - FIELD_H
                _swatch.Top = y + 20
                y += BLOCK_H

                fNotes.Lbl.Location = New Point(pad, y)
                fNotes.Box.Location = New Point(pad, y + 20)
                fNotes.Box.Size = New Size(fullW, notesH)
                _txtNotes.Location = New Point(14, 12)
                _txtNotes.Size = New Size(Math.Max(60, fullW - 28), notesH - 24)
            End Sub

        wrap.Controls.Add(card)
        Return wrap
    End Function

    ' ── Card: component recipe ───────────────────────────────────────────────
    Private Shared Function BuildComponentsCard(comps As List(Of ComponentRow)) As Panel
        Dim wrap As New Panel With {.Dock = DockStyle.Top, .Height = 200, .BackColor = MintBg}
        Dim card As New BufferedPanel With {.Dock = DockStyle.Fill, .BackColor = CardBg}
        AddHandler card.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)

        Dim header As New Panel With {
            .Dock = DockStyle.Top, .Height = 64, .BackColor = Color.Transparent
        }
        header.Controls.Add(New Label With {
            .Text = "Mixing Recipe",
            .Font = New Font("Segoe UI Semibold", 13),
            .ForeColor = MintDark,
            .AutoSize = True,
            .Location = New Point(24, 12),
            .BackColor = Color.Transparent
        })
        header.Controls.Add(New Label With {
            .Text = "Colorant name and amount in millilitres",
            .Font = New Font("Segoe UI", 9),
            .ForeColor = FaintText,
            .AutoSize = True,
            .Location = New Point(24, 36),
            .BackColor = Color.Transparent
        })
        AddHandler header.Paint,
            Sub(s, e)
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 22, header.Height - 1, header.Width - 22, header.Height - 1)
                End Using
            End Sub

        Dim btnAdd As Button = MakePillButton("+  Add Colorant", 140, MintPrimary, Color.White, True)
        btnAdd.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        AddHandler btnAdd.Click, Sub() AddComponentEditor("", "")
        header.Controls.Add(btnAdd)
        AddHandler header.Resize,
            Sub()
                btnAdd.Left = header.Width - btnAdd.Width - 24
                btnAdd.Top = (header.Height - btnAdd.Height) \ 2
            End Sub

        Dim bottomPad As New Panel With {
            .Dock = DockStyle.Bottom, .Height = PAD_BOTTOM, .BackColor = Color.Transparent
        }

        _compHost = New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = Color.Transparent,
            .AutoScroll = True,
            .Padding = New Padding(0)
        }

        card.Controls.Add(bottomPad)
        card.Controls.Add(header)
        card.Controls.Add(_compHost)
        header.BringToFront()
        _compHost.BringToFront()
        _compHost.AutoScrollMinSize = New Size(0, 1)

        wrap.Controls.Add(card)

        For Each c In comps
            AddComponentEditor(c.ColorantName, c.AmountMl.ToString("0.##"))
        Next
        RelayoutComponents()

        Return wrap
    End Function

    Private Shared Sub AddComponentEditor(colorant As String, amount As String)
        If _compHost Is Nothing Then Return

        Dim row As New Panel With {.Height = COMP_ROW_H, .BackColor = Color.Transparent}
        AddHandler row.Paint,
            Sub(s, e)
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 10, row.Height - 1, row.Width - 10, row.Height - 1)
                End Using
            End Sub

        Dim nameBox As TextBox = MakeTextBox(colorant)
        Dim amtBox As TextBox = MakeTextBox(amount)
        Dim nameWrap As Panel = MakeFieldBox(nameBox, "Colorant name (e.g. Raw Umber)")
        Dim amtWrap As Panel = MakeFieldBox(amtBox, "0")

        Dim lblMl As New Label With {
            .Text = "ml",
            .Font = New Font("Segoe UI", 10),
            .ForeColor = FaintText,
            .AutoSize = True,
            .BackColor = Color.Transparent
        }

        Dim btnRemove As Button = MakeActionButton("Remove", Color.FromArgb(255, 232, 232), AccentRed, 84)

        Dim ed As New ComponentEditor With {
            .Row = row, .NameBox = nameBox, .AmtBox = amtBox
        }
        AddHandler btnRemove.Click,
            Sub()
                _compEditors.Remove(ed)
                _compHost.Controls.Remove(row)
                row.Dispose()
                RelayoutComponents()
            End Sub

        row.Controls.AddRange({nameWrap, amtWrap, lblMl, btnRemove})

        AddHandler row.Resize,
            Sub()
                Dim w = row.Width
                Dim amtW As Integer = 110
                Dim removeW As Integer = btnRemove.Width
                Dim nameW As Integer = Math.Max(120, w - 24 - amtW - 34 - removeW - 40)

                nameWrap.Location = New Point(14, (COMP_ROW_H - 38) \ 2)
                nameWrap.Size = New Size(nameW, 38)
                nameBox.Location = New Point(12, (38 - nameBox.PreferredSize.Height) \ 2)
                nameBox.Width = Math.Max(40, nameW - 24)

                amtWrap.Location = New Point(nameWrap.Right + 12, nameWrap.Top)
                amtWrap.Size = New Size(amtW, 38)
                amtBox.Location = New Point(12, (38 - amtBox.PreferredSize.Height) \ 2)
                amtBox.Width = Math.Max(30, amtW - 24)

                lblMl.Left = amtWrap.Right + 8
                lblMl.Top = (COMP_ROW_H - lblMl.Height) \ 2

                btnRemove.Left = w - removeW - 16
                btnRemove.Top = (COMP_ROW_H - btnRemove.Height) \ 2
            End Sub

        _compEditors.Add(ed)
        _compHost.Controls.Add(row)
        RelayoutComponents()
    End Sub

    ' Repositions component rows and resizes the card to fit them.
    Private Shared Sub RelayoutComponents()
        If _compHost Is Nothing Then Return

        Dim y As Integer = 4
        For Each ed In _compEditors
            ed.Row.Location = New Point(12, y)
            ed.Row.Width = _compHost.ClientSize.Width - 24
            ed.Row.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
            y += COMP_ROW_H
        Next

        If _compCard IsNot Nothing Then
            Dim contentH As Integer = If(_compEditors.Count = 0, 70, _compEditors.Count * COMP_ROW_H + 8)
            ' Cap the recipe card so a long formula scrolls instead of pushing
            ' the Save button off the bottom of the page.
            Dim cardH As Integer = 64 + contentH + PAD_BOTTOM + 8
            Dim maxH As Integer = 420
            If cardH > maxH Then cardH = maxH
            _compCard.Height = cardH
        End If

        If _compEditors.Count = 0 Then
            If _compHost.Controls.Count = 0 Then
                _compHost.Controls.Add(New Label With {
                    .Text = "No colorants yet. Use Add Colorant to build the recipe.",
                    .Font = New Font("Segoe UI", 10),
                    .ForeColor = FaintText,
                    .AutoSize = True,
                    .Location = New Point(22, 20),
                    .BackColor = Color.Transparent
                })
            End If
        Else
            ' Drop the placeholder label once real rows exist.
            For i As Integer = _compHost.Controls.Count - 1 To 0 Step -1
                If TypeOf _compHost.Controls(i) Is Label Then
                    _compHost.Controls.RemoveAt(i)
                End If
            Next
        End If
    End Sub

    ' ── Save / Cancel row ────────────────────────────────────────────────────
    Private Shared Function BuildActionRow() As Panel
        Dim row As New Panel With {.Dock = DockStyle.Top, .Height = 52, .BackColor = MintBg}

        Dim btnSave As Button = MakePillButton("Save Formula", 140, MintPrimary, Color.White, True)
        btnSave.Height = 40
        AddHandler btnSave.Click, Sub() SaveFormula()

        Dim btnCancel As Button = MakePillButton("Cancel", 100,
                                                 Color.FromArgb(243, 246, 244),
                                                 Color.FromArgb(70, 86, 80), False)
        btnCancel.Height = 40
        AddHandler btnCancel.Click, Sub() LoadColorFormulas(_container)

        row.Controls.Add(btnCancel)
        row.Controls.Add(btnSave)
        AddHandler row.Resize,
            Sub()
                btnSave.Left = row.Width - btnSave.Width
                btnSave.Top = 6
                btnCancel.Left = btnSave.Left - btnCancel.Width - 8
                btnCancel.Top = 6
            End Sub
        Return row
    End Function

    ' =========================================================================
    '  SAVE  —  parent + children in one transaction
    ' =========================================================================
    Private Shared Sub SaveFormula()
        Dim name As String = _txtName.Text.Trim()
        If name = "" Then
            AppModal.Show("The formula name is required.", "Missing Information",
                          MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        ' Length guards matching the column widths, so an over-long value gives
        ' a clear message here instead of a raw MySQL truncation error on save.
        Dim lenErr As String = FirstLengthError({
            ("Name", name, 100),
            ("Paint type", _cboPaintType.Text.Trim(), 30),
            ("Size label", _txtSize.Text.Trim(), 20),
            ("Notes", _txtNotes.Text.Trim(), 255)
        })
        If lenErr <> "" Then
            AppModal.Show(lenErr, "Too Long", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        Dim hex As String = _txtHex.Text.Trim()
        If hex <> "" AndAlso Not HexPattern.IsMatch(hex) Then
            AppModal.Show("Color hex must look like #RRGGBB (for example #1A2B3C), or be left empty.",
                          "Invalid Color", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        ' Gather components; a completely blank row is ignored rather than
        ' treated as an error, so an accidental Add doesn't block saving.
        Dim comps As New List(Of ComponentRow)
        For Each ed In _compEditors
            Dim cName As String = ed.NameBox.Text.Trim()
            Dim cAmtText As String = ed.AmtBox.Text.Trim()
            If cName = "" AndAlso cAmtText = "" Then Continue For

            If cName = "" Then
                AppModal.Show("A colorant amount was entered without a name.",
                              "Incomplete Colorant", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End If
            Dim amt As Decimal
            If Not Decimal.TryParse(cAmtText, amt) OrElse amt <= 0D Then
                AppModal.Show("The amount for """ & cName & """ must be a number greater than zero.",
                              "Invalid Amount", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End If
            comps.Add(New ComponentRow With {.ColorantName = cName, .AmountMl = amt})
        Next

        ' Warn, don't block.
        If comps.Count = 0 Then
            If AppModal.Show("This formula has no colorants. Save it anyway?",
                             "No Components", MessageBoxButtons.YesNo,
                             MessageBoxIcon.Warning) <> DialogResult.Yes Then
                Return
            End If
        End If

        Dim paintType As String = _cboPaintType.Text.Trim()
        Dim sizeLabel As String = _txtSize.Text.Trim()
        Dim notes As String = _txtNotes.Text.Trim()
        Dim baseOpt = TryCast(_cboBaseProduct.SelectedItem, ProductOption)
        Dim baseId As Integer = If(baseOpt Is Nothing, 0, baseOpt.ProductId)

        Dim savedId As Integer = _editingId
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using txn = conn.BeginTransaction()
                    Try
                        If _editingId > 0 Then
                            Using cmd As New MySqlCommand(
                                "UPDATE color_formulas SET name=@name, paint_type=@ptype, " &
                                "base_product_id=@base, size_label=@size, color_hex=@hex, notes=@notes " &
                                "WHERE formula_id=@id", conn, txn)
                                AddParentParams(cmd, name, paintType, baseId, sizeLabel, hex, notes)
                                cmd.Parameters.AddWithValue("@id", _editingId)
                                cmd.ExecuteNonQuery()
                            End Using

                            ' Replace the whole recipe — simplest correct way to
                            ' reconcile adds, edits and removals in one shot.
                            Using cmd As New MySqlCommand(
                                "DELETE FROM formula_components WHERE formula_id=@id", conn, txn)
                                cmd.Parameters.AddWithValue("@id", _editingId)
                                cmd.ExecuteNonQuery()
                            End Using
                        Else
                            Using cmd As New MySqlCommand(
                                "INSERT INTO color_formulas " &
                                "(name, paint_type, base_product_id, size_label, color_hex, notes, created_by) " &
                                "VALUES (@name, @ptype, @base, @size, @hex, @notes, @by)", conn, txn)
                                AddParentParams(cmd, name, paintType, baseId, sizeLabel, hex, notes)
                                cmd.Parameters.AddWithValue("@by",
                                    If(Session.CurrentUserId = 0,
                                       CObj(DBNull.Value), CObj(Session.CurrentUserId)))
                                cmd.ExecuteNonQuery()
                            End Using
                            Using cmd As New MySqlCommand("SELECT LAST_INSERT_ID()", conn, txn)
                                savedId = Convert.ToInt32(cmd.ExecuteScalar())
                            End Using
                        End If

                        For Each c In comps
                            Using cmd As New MySqlCommand(
                                "INSERT INTO formula_components (formula_id, colorant_name, amount_ml) " &
                                "VALUES (@fid, @cname, @amt)", conn, txn)
                                cmd.Parameters.AddWithValue("@fid", savedId)
                                cmd.Parameters.AddWithValue("@cname", c.ColorantName)
                                cmd.Parameters.AddWithValue("@amt", c.AmountMl)
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
        Catch ex As Exception
            AppModal.Show("Could not save the formula: " & ex.Message &
                          vbCrLf & vbCrLf & "No changes were made.",
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try

        ActivityLog.Record(If(_editingId > 0, "FORMULA_EDIT", "FORMULA_ADD"),
                           "formula", savedId, name)

        AppModal.Show("Formula """ & name & """ saved with " &
                      comps.Count.ToString("N0") &
                      If(comps.Count = 1, " colorant.", " colorants."),
                      "Formula Saved", MessageBoxButtons.OK, MessageBoxIcon.Information)

        LoadColorFormulas(_container)
    End Sub

    Private Shared Sub AddParentParams(cmd As MySqlCommand, name As String, paintType As String,
                                       baseId As Integer, sizeLabel As String,
                                       hex As String, notes As String)
        cmd.Parameters.AddWithValue("@name", name)
        cmd.Parameters.AddWithValue("@ptype", NullIfBlank(paintType))
        cmd.Parameters.AddWithValue("@base", If(baseId = 0, CObj(DBNull.Value), CObj(baseId)))
        cmd.Parameters.AddWithValue("@size", NullIfBlank(sizeLabel))
        cmd.Parameters.AddWithValue("@hex", NullIfBlank(hex))
        cmd.Parameters.AddWithValue("@notes", NullIfBlank(notes))
    End Sub

    ' Returns the first field that exceeds its column width, or "" if all fit.
    Private Shared Function FirstLengthError(fields As (label As String, value As String, max As Integer)()) As String
        For Each f In fields
            If f.value IsNot Nothing AndAlso f.value.Length > f.max Then
                Return f.label & " must be " & f.max.ToString() & " characters or fewer " &
                       "(it is " & f.value.Length.ToString() & ")."
            End If
        Next
        Return ""
    End Function

    Private Shared Function NullIfBlank(s As String) As Object
        If String.IsNullOrEmpty(s) Then Return DBNull.Value
        Return s
    End Function

    ' =========================================================================
    '  DELETE  —  children then parent, one transaction
    ' =========================================================================
    Private Shared Sub DeleteFormula(r As FormulaRow)
        ' sale_items.formula_id can reference a formula. Deleting one that a
        ' sale points at would either fail on the FK or orphan sale history,
        ' so it is blocked the same way categories and payment methods are.
        Dim usedBySales As Integer = 0
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT COUNT(*) FROM sale_items WHERE formula_id = @id", conn)
                    cmd.Parameters.AddWithValue("@id", r.FormulaId)
                    usedBySales = Convert.ToInt32(cmd.ExecuteScalar())
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not check whether the formula is in use: " & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try

        If usedBySales > 0 Then
            AppModal.Show("""" & r.Name & """ is referenced by " & usedBySales.ToString("N0") &
                          If(usedBySales = 1, " sale line", " sale lines") &
                          " and cannot be deleted." & vbCrLf & vbCrLf &
                          "Removing it would break that sales history.",
                          "Formula In Use", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        If AppModal.Show("Delete the formula """ & r.Name & """ and its " &
                         r.ComponentCount.ToString("N0") &
                         If(r.ComponentCount = 1, " colorant?", " colorants?"),
                         "Confirm Delete", MessageBoxButtons.YesNo,
                         MessageBoxIcon.Warning) <> DialogResult.Yes Then
            Return
        End If

        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using txn = conn.BeginTransaction()
                    Try
                        ' Children first — this works whether or not the FK
                        ' has ON DELETE CASCADE.
                        Using cmd As New MySqlCommand(
                            "DELETE FROM formula_components WHERE formula_id=@id", conn, txn)
                            cmd.Parameters.AddWithValue("@id", r.FormulaId)
                            cmd.ExecuteNonQuery()
                        End Using
                        Using cmd As New MySqlCommand(
                            "DELETE FROM color_formulas WHERE formula_id=@id", conn, txn)
                            cmd.Parameters.AddWithValue("@id", r.FormulaId)
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
            AppModal.Show("Could not delete the formula: " & ex.Message &
                          vbCrLf & vbCrLf & "No changes were made.",
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try

        ActivityLog.Record("FORMULA_DELETE", "formula", r.FormulaId, r.Name)
        LoadColorFormulas(_container)
    End Sub

    ' =========================================================================
    '  FIELD / BUTTON HELPERS
    ' =========================================================================
    Private Class FieldUI
        Public Lbl As Label
        Public Box As Panel
        Public Input As Control
    End Class

    ' Owner-draws a combo's dropdown list so the selected row uses the app's
    ' mint highlight instead of the harsh Windows-blue bar. Works for both the
    ' editable and the list-only combos on this screen.
    Private Shared Sub StyleComboList(cbo As ComboBox)
        cbo.DrawMode = DrawMode.OwnerDrawFixed
        cbo.ItemHeight = 26
        AddHandler cbo.DrawItem,
            Sub(s As Object, e As DrawItemEventArgs)
                e.DrawBackground()
                If e.Index < 0 Then Return
                Dim selected As Boolean =
                    (e.State And DrawItemState.Selected) = DrawItemState.Selected
                Using bg As New SolidBrush(If(selected, Color.FromArgb(224, 242, 235), CardBg))
                    e.Graphics.FillRectangle(bg, e.Bounds)
                End Using
                Dim txt As String = cbo.GetItemText(cbo.Items(e.Index))
                TextRenderer.DrawText(e.Graphics, txt, cbo.Font,
                    New Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height),
                    Color.FromArgb(35, 50, 46), TextFormatFlags.Left Or TextFormatFlags.VerticalCenter)
            End Sub
    End Sub

    Private Shared Function MakeTextBox(value As String) As TextBox
        Return New TextBox With {
            .Font = New Font("Segoe UI", 11.0F),
            .BorderStyle = BorderStyle.None,
            .BackColor = FieldBg,
            .ForeColor = Color.FromArgb(35, 50, 46),
            .Text = value
        }
    End Function

    ' A rounded field box wrapping an arbitrary input control.
    Private Shared Function MakeFieldBox(input As Control, Optional placeholder As String = "") As Panel
        Dim box As New Panel With {.Size = New Size(100, FIELD_H), .BackColor = Color.Transparent}
        Dim focusedFlag As Boolean = False
        Dim tb As TextBox = TryCast(input, TextBox)
        AddHandler box.Paint,
            Sub(s, e)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect = box.ClientRectangle : rect.Inflate(-1, -1)
                Using br As New SolidBrush(FieldBg)
                    g.FillRoundedRectangle(br, rect, 9)
                End Using
                Using p As New Pen(If(focusedFlag, MintPrimary, FieldBorder), If(focusedFlag, 1.8F, 1.0F))
                    g.DrawRoundedRectangle(p, rect, 9)
                End Using
                ' Placeholder painted behind an empty box, so the input's .Text
                ' stays truly empty (callers read .Text as the real value).
                If placeholder <> "" AndAlso tb IsNot Nothing AndAlso tb.Text = "" AndAlso Not tb.Focused Then
                    TextRenderer.DrawText(g, placeholder, tb.Font,
                        New Rectangle(tb.Left, tb.Top - 1, box.Width - tb.Left - 6, tb.Height + 2),
                        FaintText, TextFormatFlags.Left Or TextFormatFlags.VerticalCenter)
                End If
            End Sub
        AddHandler input.Enter, Sub()
                                    focusedFlag = True
                                    box.Invalidate()
                                End Sub
        AddHandler input.Leave, Sub()
                                    focusedFlag = False
                                    box.Invalidate()
                                End Sub
        If tb IsNot Nothing Then AddHandler tb.TextChanged, Sub() box.Invalidate()
        box.Controls.Add(input)
        Return box
    End Function

    Private Shared Function AddField(host As Control, labelText As String, input As Control) As FieldUI
        Dim lbl As New Label With {
            .Text = labelText,
            .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = SubText,
            .AutoSize = True,
            .BackColor = Color.Transparent
        }
        Dim box As Panel = MakeFieldBox(input)
        host.Controls.Add(lbl)
        host.Controls.Add(box)
        Return New FieldUI With {.Lbl = lbl, .Box = box, .Input = input}
    End Function

    Private Shared Sub PlaceField(f As FieldUI, x As Integer, y As Integer, w As Integer)
        f.Lbl.Location = New Point(x, y)
        f.Box.Location = New Point(x, y + 20)
        f.Box.Size = New Size(w, FIELD_H)
        f.Input.Location = New Point(14, (FIELD_H - f.Input.PreferredSize.Height) \ 2)
        f.Input.Width = Math.Max(40, w - 28)
    End Sub

    Private Shared Function MakePillButton(text As String, width As Integer,
                                           backClr As Color, foreClr As Color,
                                           isPrimary As Boolean) As Button
        Dim b As New Button With {
            .Text = text,
            .Size = New Size(width, 34),
            .FlatStyle = FlatStyle.Flat,
            .Font = New Font("Segoe UI Semibold", 9.5F),
            .Cursor = Cursors.Hand,
            .BackColor = backClr,
            .ForeColor = foreClr,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        b.FlatAppearance.BorderSize = 0

        Dim normalClr As Color = backClr
        Dim hoverClr As Color = If(isPrimary, MintDark,
            Color.FromArgb(Math.Max(0, backClr.R - 12),
                           Math.Max(0, backClr.G - 12),
                           Math.Max(0, backClr.B - 12)))

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

    Private Shared Function MakeActionButton(text As String, backClr As Color,
                                             foreClr As Color, width As Integer) As Button
        Dim b As New Button With {
            .Text = text,
            .FlatStyle = FlatStyle.Flat,
            .Cursor = Cursors.Hand,
            .Size = New Size(width, 30),
            .Font = New Font("Segoe UI Semibold", 9.0F),
            .BackColor = backClr,
            .ForeColor = foreClr
        }
        b.FlatAppearance.BorderSize = 0
        Dim normalClr As Color = backClr
        Dim hoverClr As Color = Color.FromArgb(Math.Max(0, backClr.R - 14),
                                               Math.Max(0, backClr.G - 14),
                                               Math.Max(0, backClr.B - 14))
        AddHandler b.Paint,
            Sub(s, e)
                Dim bb = CType(s, Button)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect = bb.ClientRectangle : rect.Inflate(-1, -1)
                Using br As New SolidBrush(bb.BackColor)
                    g.FillRoundedRectangle(br, rect, 7)
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

    ' =========================================================================
    '  SHARED HELPERS
    ' =========================================================================
    Private Shared Function HexToColor(hex As String, fallback As Color) As Color
        If String.IsNullOrWhiteSpace(hex) Then Return fallback
        Dim h As String = hex.Trim()
        If Not HexPattern.IsMatch(h) Then Return fallback
        Try
            Return ColorTranslator.FromHtml(h)
        Catch
            Return fallback
        End Try
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
