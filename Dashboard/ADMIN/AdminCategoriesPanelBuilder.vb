Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports MySql.Data.MySqlClient

' =============================================================================
'  AdminCategoriesPanelBuilder  —  Admin ▸ Categories
'
'  Product CRUD belongs to InventoryItemListPanelBuilder; this screen owns the
'  one thing Inventory does not — the category list itself. Inventory only ever
'  reads categories (to assign and filter), so add / rename / remove live here.
'
'  LAYOUT  mirrors AdminRolesPanelBuilder / AdminUserPanelBuilder:
'    TopRegion (Dock = Top, 72px):
'      └─ TitleBar : "Categories" + Refresh + "+ New Category"
'    TableWrapper (Dock = Fill, 24px side spacers + 16px top/bottom):
'      └─ listArea (BufferedPanel, PanelRenderer card, Dock = Top):
'           ├─ ColHeader   (Dock = Top,    44px)
'           ├─ _scrollHost (Dock = Fill,   AutoScroll)
'           └─ bottomPad   (Dock = Bottom, 12px)
'
'  The card is sized to its content and capped at the viewport, so a short list
'  is not a mostly-empty full-height card and a long one cannot overflow.
'
'  A category in use by any product cannot be deleted — products.category_id
'  references it, and removing it would orphan those rows.
' =============================================================================
Public Class AdminCategoriesPanelBuilder

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

    Private Shared ReadOnly TintMint As (fore As Color, back As Color) =
        (Color.FromArgb(0, 120, 92), Color.FromArgb(222, 246, 238))
    Private Shared ReadOnly TintGrey As (fore As Color, back As Color) =
        (Color.FromArgb(90, 114, 107), Color.FromArgb(238, 242, 240))

    ' ── DATA MODEL ────────────────────────────────────────────────────────────
    Private Class CategoryRecord
        Public CategoryId As Integer
        Public Name As String
        Public ProductCount As Integer
    End Class

    ' ── STATE ─────────────────────────────────────────────────────────────────
    Private Const SIDE_PAD As Integer = 24
    Private Const ROW_H As Integer = 64
    Private Const HDR_H As Integer = 44
    Private Const PAD_TOP As Integer = 4
    Private Const PAD_BOTTOM As Integer = 12

    Private Shared _container As Panel
    Private Shared _scrollHost As Panel
    Private Shared _rows As New List(Of CategoryRecord)
    Private Shared _reflowAttached As Boolean = False

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Shared Sub LoadCategories(container As Panel)
        _container = container
        LoadData()
        Render()
    End Sub

    ' =========================================================================
    '  DATA
    ' =========================================================================
    Private Shared Sub LoadData()
        _rows.Clear()

        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Dim sql As String =
                    "SELECT c.category_id, c.name, COUNT(p.product_id) AS product_count " &
                    "FROM categories c LEFT JOIN products p ON p.category_id = c.category_id " &
                    "GROUP BY c.category_id, c.name ORDER BY c.name ASC"

                Using cmd As New MySqlCommand(sql, conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            _rows.Add(New CategoryRecord With {
                                .CategoryId = SafeInt(rdr("category_id")),
                                .Name = SafeStr(rdr("name")),
                                .ProductCount = SafeInt(rdr("product_count"))
                            })
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load categories: " & ex.Message, "Database Error",
                          MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    Private Shared Function SafeInt(o As Object) As Integer
        If o Is Nothing OrElse IsDBNull(o) Then Return 0
        Return Convert.ToInt32(o)
    End Function

    Private Shared Function SafeStr(o As Object) As String
        If o Is Nothing OrElse IsDBNull(o) Then Return ""
        Return o.ToString()
    End Function

    ' Case-insensitive duplicate check. excludeId lets a rename keep its own name.
    ' True when the name is one the POS tab strip / SKU prefix logic recognises.
    Private Shared Function IsPosMappedName(categoryName As String) As Boolean
        Select Case If(categoryName, "").Trim().ToLowerInvariant()
            Case "paint", "paints", "tools", "tool",
                 "thinner", "thinners", "solvent", "solvents",
                 "accessories", "accessory"
                Return True
            Case Else
                Return False
        End Select
    End Function

    ' categories.name is VARCHAR(50) UNIQUE. Checking the length here turns a
    ' raw MySQL truncation error - surfaced as an unactionable 'Database Error'
    ' - into something the admin can act on, matching how payment methods and
    ' other master data validate their names.
    Private Shared Function ValidateCategoryName(name As String) As String
        If String.IsNullOrWhiteSpace(name) Then Return "The category name cannot be empty."
        If name.Length > 50 Then
            Return "Use 50 characters or fewer (that name is " & name.Length.ToString() & ")."
        End If
        Return ""
    End Function

    Private Shared Function NameTaken(name As String, Optional excludeId As Integer = 0) As Boolean
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Dim sql As String = "SELECT COUNT(*) FROM categories WHERE LOWER(name) = LOWER(@n)"
                If excludeId > 0 Then sql &= " AND category_id <> @id"
                Using cmd As New MySqlCommand(sql, conn)
                    cmd.Parameters.AddWithValue("@n", name)
                    If excludeId > 0 Then cmd.Parameters.AddWithValue("@id", excludeId)
                    Return Convert.ToInt32(cmd.ExecuteScalar()) > 0
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not verify the category name: " & ex.Message, "Database Error",
                          MessageBoxButtons.OK, MessageBoxIcon.Error)
            ' Fail closed — never insert a possible duplicate blind.
            Return True
        End Try
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
            .Dock = DockStyle.Fill,
            .BackColor = MintBg,
            .Padding = New Padding(0)
        }

        ' Height follows the row count, capped at the viewport.
        Dim contentH As Integer = If(_rows.Count = 0, 90, _rows.Count * ROW_H)
        Dim cardH As Integer = HDR_H + PAD_TOP + contentH + PAD_BOTTOM + 2
        Dim maxH As Integer = _container.ClientSize.Height - 72 - (SUMMARY_H + 12) - 32
        If maxH > 160 AndAlso cardH > maxH Then cardH = maxH

        Dim listArea As New BufferedPanel With {
            .Dock = DockStyle.Top,
            .Height = cardH,
            .BackColor = CardBg,
            .Padding = New Padding(1)
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

        Dim bottomPad As New Panel With {
            .Dock = DockStyle.Bottom,
            .Height = PAD_BOTTOM,
            .BackColor = Color.Transparent
        }

        listArea.Controls.Add(bottomPad)
        listArea.Controls.Add(colHdr)
        listArea.Controls.Add(_scrollHost)
        colHdr.BringToFront()
        _scrollHost.BringToFront()
        _scrollHost.AutoScrollMinSize = New Size(0, 1)

        Dim spacerBottom As New Panel With {.Dock = DockStyle.Bottom, .Height = 16, .BackColor = MintBg}
        Dim spacerTop As New Panel With {.Dock = DockStyle.Top, .Height = 16, .BackColor = MintBg}
        Dim spacerLeft As New Panel With {.Dock = DockStyle.Left, .Width = SIDE_PAD, .BackColor = MintBg}
        Dim spacerRight As New Panel With {.Dock = DockStyle.Right, .Width = SIDE_PAD, .BackColor = MintBg}

        tableWrapper.Controls.Add(listArea)
        tableWrapper.Controls.Add(spacerBottom)
        tableWrapper.Controls.Add(spacerTop)
        tableWrapper.Controls.Add(spacerLeft)
        tableWrapper.Controls.Add(spacerRight)

        Dim topRegion As Panel = BuildTopRegion()
        topRegion.Dock = DockStyle.Top

        Dim tileStrip As Panel = BuildTileStrip()
        tileStrip.Dock = DockStyle.Top

        ' Docked-Top children stack in reverse add order, so the last added
        ' sits nearest the top: table, then tiles, then the title bar.
        _container.Controls.Add(tableWrapper)
        _container.Controls.Add(tileStrip)
        _container.Controls.Add(topRegion)

        _container.ResumeLayout()

        PopulateRows()
    End Sub

    ' =========================================================================
    '  SUMMARY TILES  —  the shape of the catalogue at a glance
    ' =========================================================================
    Private Const SUMMARY_H As Integer = 140

    Private Shared Function BuildTileStrip() As Panel
        Dim bar As New Panel With {
            .Height = SUMMARY_H + 12,
            .BackColor = MintBg,
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

        Dim total As Integer = _rows.Count
        Dim withProducts As Integer = 0
        Dim totalProducts As Integer = 0
        For Each r In _rows
            If r.ProductCount > 0 Then withProducts += 1
            totalProducts += r.ProductCount
        Next
        Dim empties As Integer = total - withProducts

        Dim defs = {
            ("Categories", total.ToString("N0"), "in the catalogue", MintPrimary, ""),
            ("Products Filed", totalProducts.ToString("N0"), "across all categories", MintDark, ""),
            ("Empty", empties.ToString("N0"), "with no products yet", SubText,
             If(empties > 0, "Review", ""))
        }
        For i As Integer = 0 To 2
            Dim d = defs(i)
            Dim card = MakeSummaryCard(d.Item1, d.Item4, d.Item2, d.Item3, d.Item5)
            card.Margin = New Padding(0, 0, If(i < 2, 12, 0), 0)
            grid.Controls.Add(card, i, 0)
        Next
        bar.Controls.Add(grid)
        Return bar
    End Function

    Private Shared Function MakeSummaryCard(caption As String, valueClr As Color,
                                            value As String, subLabel As String,
                                            badgeText As String) As Panel
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
        If badgeText <> "" Then
            Dim badge As New Label With {
                .Text = badgeText, .Font = New Font("Segoe UI", 9, FontStyle.Bold),
                .ForeColor = MintPrimary, .BackColor = Color.FromArgb(236, 248, 244),
                .AutoSize = True, .Padding = New Padding(6, 3, 6, 3),
                .Anchor = AnchorStyles.Right Or AnchorStyles.Top}
            card.Controls.Add(badge)
            AddHandler card.Resize, Sub()
                                        badge.Left = card.Width - badge.Width - 14
                                        badge.Top = 14
                                    End Sub
        End If
        Return card
    End Function

    ' =========================================================================
    '  TOP REGION  —  72px title bar
    ' =========================================================================
    Private Shared Function BuildTopRegion() As Panel
        Dim region As New Panel With {
            .BackColor = MintBg, .AutoSize = False, .Height = 72
        }

        Dim titleBar As New Panel With {
            .Height = 72, .Dock = DockStyle.Top, .BackColor = Color.Transparent
        }

        Dim lblTitle As New Label With {
            .Text = "Categories",
            .Font = New Font("Segoe UI", 28, FontStyle.Bold),
            .ForeColor = MintDark,
            .AutoSize = True,
            .Location = New Point(SIDE_PAD, 16)
        }

        Dim btnNew As New Button With {
            .Text = "+  New Category",
            .FlatStyle = FlatStyle.Flat,
            .Cursor = Cursors.Hand,
            .Size = New Size(150, 34),
            .Font = New Font("Segoe UI Semibold", 9.5F),
            .BackColor = MintPrimary,
            .ForeColor = Color.White,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        btnNew.FlatAppearance.BorderSize = 0
        AddHandler btnNew.Paint, AddressOf PaintPillButton
        AddHandler btnNew.MouseEnter, Sub()
                                          btnNew.BackColor = MintDark
                                          btnNew.Invalidate()
                                      End Sub
        AddHandler btnNew.MouseLeave, Sub()
                                          btnNew.BackColor = MintPrimary
                                          btnNew.Invalidate()
                                      End Sub
        AddHandler btnNew.Click, Sub() AddCategory()

        AddHandler titleBar.Resize,
            Sub()
                btnNew.Left = titleBar.Width - btnNew.Width - SIDE_PAD
                btnNew.Top = (titleBar.Height - btnNew.Height) \ 2
            End Sub

        titleBar.Controls.Add(lblTitle)
        titleBar.Controls.Add(btnNew)

        region.Controls.Add(titleBar)
        Return region
    End Function

    Private Shared Sub PaintPillButton(s As Object, e As PaintEventArgs)
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

    ' =========================================================================
    '  COLUMN HEADER  —  44px (must match BuildDataRow)
    ' =========================================================================
    Private Shared Function BuildColHeader() As Panel
        Dim hdr As New Panel With {.Height = HDR_H, .BackColor = Color.Transparent}

        Dim labels As Label() = {
            MakeColLabel("CATEGORY", New Point(24, 13)),
            MakeColLabel("PRODUCTS", New Point(0, 13)),
            MakeColLabel("ACTIONS", New Point(0, 13))
        }
        hdr.Controls.AddRange(labels)

        AddHandler hdr.Resize,
            Sub()
                Dim w = hdr.Width
                labels(0).Left = 24
                labels(1).Left = CInt(w * 0.42)
                labels(2).Left = CInt(w * 0.7)
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
            _scrollHost.Controls.Add(BuildEmptyRow(
                "No categories yet. Use the New Category button to add one."))
        Else
            Dim y As Integer = PAD_TOP
            For Each r As CategoryRecord In _rows
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

    Private Shared Function BuildDataRow(r As CategoryRecord, height As Integer) As Panel
        Dim p As New BufferedPanel With {
            .Height = height, .BackColor = Color.Transparent
        }

        Dim inUse As Boolean = (r.ProductCount > 0)

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
            .ForeColor = MintDark,
            .AutoSize = True, .BackColor = Color.Transparent
        }

        Dim tint = If(inUse, TintMint, TintGrey)
        Dim countPill As New Label With {
            .Text = r.ProductCount.ToString("N0") &
                    If(r.ProductCount = 1, " product", " products"),
            .Font = New Font("Segoe UI Semibold", 9.0F),
            .ForeColor = tint.fore,
            .BackColor = tint.back,
            .AutoSize = True,
            .Padding = New Padding(10, 4, 10, 4)
        }
        RoundLabel(countPill, 10)

        ' Merge folds this category's products into another, then deletes it -
        ' the way to clear an in-use category that Remove refuses to touch.
        Dim canMerge As Boolean = (_rows.Count > 1)
        Dim btnMerge As Button
        If canMerge Then
            btnMerge = MakeActionButton("Merge", Color.FromArgb(232, 238, 250), Color.FromArgb(45, 80, 150))
        Else
            btnMerge = MakeActionButton("Merge", Color.FromArgb(240, 242, 241), FaintText)
        End If
        AddHandler btnMerge.Click, Sub() If canMerge Then MergeCategory(r)

        Dim btnRename As Button = MakeActionButton("Rename", Color.FromArgb(220, 235, 232), MintDark)
        AddHandler btnRename.Click, Sub() RenameCategory(r)

        ' Deleting an in-use category would orphan products, so the button is
        ' visibly inert there and explains itself on click.
        Dim btnRemove As Button
        If inUse Then
            btnRemove = MakeActionButton("Remove", Color.FromArgb(240, 242, 241), FaintText)
        Else
            btnRemove = MakeActionButton("Remove", Color.FromArgb(255, 232, 232), AccentRed)
        End If
        AddHandler btnRemove.Click, Sub() RemoveCategory(r)

        p.Controls.AddRange({lblName, countPill, btnMerge, btnRename, btnRemove})

        AddHandler p.Resize,
            Sub()
                Dim w = p.Width
                lblName.Left = 24 : lblName.Top = (height - lblName.Height) \ 2
                countPill.Left = CInt(w * 0.42)
                countPill.Top = (height - countPill.Height) \ 2

                Dim ax As Integer = CInt(w * 0.7)
                Dim ay As Integer = (height - btnRename.Height) \ 2
                btnMerge.Left = ax : btnMerge.Top = ay
                btnRename.Left = btnMerge.Right + 6 : btnRename.Top = ay
                btnRemove.Left = btnRename.Right + 6 : btnRemove.Top = ay
            End Sub

        PropagateHover(p)
        Return p
    End Function

    Private Shared Function BuildEmptyRow(message As String) As Panel
        Dim p As New Panel With {
            .Height = 90, .Dock = DockStyle.Top, .BackColor = Color.Transparent
        }
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
    '  CRUD
    ' =========================================================================
    Private Shared Sub AddCategory()
        Dim name As String = AppModal.Prompt("Name of the new category:", "New Category")
        If name Is Nothing Then Return
        name = name.Trim()

        Dim vErr As String = ValidateCategoryName(name)
        If vErr <> "" Then
            AppModal.Show(vErr, "New Category",
                          MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If
        If NameTaken(name) Then
            AppModal.Show("A category named """ & name & """ already exists.",
                          "Duplicate Category", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        Dim newId As Integer = 0
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "INSERT INTO categories (name) VALUES (@n)", conn)
                    cmd.Parameters.AddWithValue("@n", name)
                    cmd.ExecuteNonQuery()
                End Using
                Using cmd As New MySqlCommand("SELECT LAST_INSERT_ID()", conn)
                    newId = Convert.ToInt32(cmd.ExecuteScalar())
                End Using
            End Using
        Catch ex As MySqlException When ex.Number = 1062
            ' Unique index on name - someone added it between the NameTaken
            ' check and this insert.
            AppModal.Show("A category named """ & name & """ was just added. Please refresh.",
                          "Duplicate Category", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        Catch ex As Exception
            AppModal.Show("Could not add the category: " & ex.Message, "Database Error",
                          MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try

        ActivityLog.Record("CATEGORY_ADD", "category", newId, name)
        LoadCategories(_container)
    End Sub

    Private Shared Sub RenameCategory(r As CategoryRecord)
        Dim name As String = AppModal.Prompt("New name for """ & r.Name & """:",
                                             "Rename Category", r.Name)
        If name Is Nothing Then Return
        name = name.Trim()

        Dim vErr As String = ValidateCategoryName(name)
        If vErr <> "" Then
            AppModal.Show(vErr, "Rename Category",
                          MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If
        If name = r.Name Then Return
        If NameTaken(name, r.CategoryId) Then
            AppModal.Show("A category named """ & name & """ already exists.",
                          "Duplicate Category", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        ' The POS tab strip and the SKU prefix generator both match the category
        ' NAME as a string ("paint", "tools", "thinner", "accessories"). Renaming
        ' away from one of those does not lose any product, but they stop landing
        ' under the fixed POS tab and new SKUs change prefix. Say so first.
        If IsPosMappedName(r.Name) AndAlso Not IsPosMappedName(name) Then
            If AppModal.Show(
                "The POS product tabs and SKU prefixes are matched on the category name." &
                Environment.NewLine & Environment.NewLine &
                "Renaming """ & r.Name & """ to """ & name & """ means its products no longer " &
                "appear under the " & r.Name & " tab in the POS, and new items in it get a " &
                "different SKU prefix. Existing SKUs are unchanged." &
                Environment.NewLine & Environment.NewLine & "Rename anyway?",
                "Rename Category", MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) <> DialogResult.Yes Then
                Return
            End If
        End If

        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "UPDATE categories SET name=@n WHERE category_id=@id", conn)
                    cmd.Parameters.AddWithValue("@n", name)
                    cmd.Parameters.AddWithValue("@id", r.CategoryId)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        Catch ex As MySqlException When ex.Number = 1062
            AppModal.Show("A category named """ & name & """ was just added. Please refresh.",
                          "Duplicate Category", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        Catch ex As Exception
            AppModal.Show("Could not rename the category: " & ex.Message, "Database Error",
                          MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try

        ActivityLog.Record("CATEGORY_EDIT", "category", r.CategoryId, name)
        LoadCategories(_container)
    End Sub

    ' =========================================================================
    '  MERGE  -  fold a category products into another, then delete it.
    '
    '  The way out of the dead-end where an in-use category cannot be removed:
    '  instead of the admin hand-reassigning every product in Inventory first,
    '  this moves them all in one step and removes the now-empty source. Both
    '  writes run in a transaction so a failure leaves nothing half-done.
    ' =========================================================================
    Private Shared Sub MergeCategory(source As CategoryRecord)
        Dim targets As New List(Of CategoryRecord)
        For Each r In _rows
            If r.CategoryId <> source.CategoryId Then targets.Add(r)
        Next
        If targets.Count = 0 Then Return

        Dim targetId As Integer = PickTarget(source, targets)
        If targetId <= 0 Then Return

        Dim targetName As String = ""
        For Each r In targets
            If r.CategoryId = targetId Then targetName = r.Name
        Next

        Dim moved As Integer = source.ProductCount
        If AppModal.Show(
            "Move " & moved.ToString("N0") & If(moved = 1, " product", " products") &
            " from """ & source.Name & """ into """ & targetName & """, then delete """ &
            source.Name & """?" & vbCrLf & vbCrLf &
            If(Archive.HasArchiveTable(), "The empty category goes to the Recycle Bin.", "This cannot be undone."),
            "Confirm Merge", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then
            Return
        End If

        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using tx = conn.BeginTransaction()
                    Using cmd As New MySqlCommand(
                        "UPDATE products SET category_id=@to WHERE category_id=@from", conn, tx)
                        cmd.Parameters.AddWithValue("@to", targetId)
                        cmd.Parameters.AddWithValue("@from", source.CategoryId)
                        cmd.ExecuteNonQuery()
                    End Using
                    Archive.Capture(conn, tx, "categories", "category_id", source.CategoryId,
                                    source.Name & " (merged into " & targetName & ")")
                    Using cmd As New MySqlCommand(
                        "DELETE FROM categories WHERE category_id=@id", conn, tx)
                        cmd.Parameters.AddWithValue("@id", source.CategoryId)
                        cmd.ExecuteNonQuery()
                    End Using
                    tx.Commit()
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not merge the category: " & ex.Message, "Database Error",
                          MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try

        ActivityLog.Record("CATEGORY_MERGE", "category", source.CategoryId,
                           source.Name & " merged into " & targetName &
                           " (" & moved.ToString() & " products)")
        LoadCategories(_container)
    End Sub

    ' Small themed picker: a titled card with a drop-down of the target
    ' categories. Returns the chosen category_id, or 0 if cancelled.
    Private Shared Function PickTarget(source As CategoryRecord,
                                       targets As List(Of CategoryRecord)) As Integer
        Const CARD_W As Integer = 420
        Const PAD As Integer = 24
        Dim chosen As Integer = 0

        Dim f As New Form With {
            .FormBorderStyle = FormBorderStyle.None,
            .StartPosition = FormStartPosition.CenterParent,
            .BackColor = CardBg, .ShowInTaskbar = False, .Width = CARD_W, .KeyPreview = True
        }

        Dim lblTitle As New Label With {
            .Text = "Merge " & source.Name,
            .Font = New Font("Segoe UI Semibold", 12.0F), .ForeColor = MintDark,
            .AutoSize = False, .Location = New Point(PAD, PAD),
            .Size = New Size(CARD_W - PAD * 2, 22), .BackColor = Color.Transparent
        }
        Dim lblBody As New Label With {
            .Text = "Its products will move into the category you pick, and """ &
                    source.Name & """ will be deleted.",
            .Font = New Font("Segoe UI", 9.5F), .ForeColor = SubText,
            .AutoSize = False, .Location = New Point(PAD, lblTitle.Bottom + 8),
            .Size = New Size(CARD_W - PAD * 2, 40), .BackColor = Color.Transparent
        }
        Dim lblPick As New Label With {
            .Text = "Merge into", .Font = New Font("Segoe UI Semibold", 9.0F), .ForeColor = SubText,
            .AutoSize = True, .Location = New Point(PAD, lblBody.Bottom + 10), .BackColor = Color.Transparent
        }
        ' Owner-drawn so the dropdown list uses the app's mint highlight instead
        ' of the harsh Windows-blue selection bar, and each row gets comfortable
        ' padding. The 34px item height matches the field's own height.
        Dim cbo As New ComboBox With {
            .DropDownStyle = ComboBoxStyle.DropDownList,
            .Font = New Font("Segoe UI", 10.5F),
            .Location = New Point(PAD, lblPick.Bottom + 4),
            .Width = CARD_W - PAD * 2,
            .FlatStyle = FlatStyle.Flat,
            .BackColor = CardBg,
            .ForeColor = MintDark,
            .DrawMode = DrawMode.OwnerDrawFixed,
            .ItemHeight = 30
        }
        For Each r In targets
            cbo.Items.Add(r.Name)
        Next
        cbo.SelectedIndex = 0
        AddHandler cbo.DrawItem,
            Sub(s As Object, e As DrawItemEventArgs)
                e.DrawBackground()
                If e.Index < 0 Then Return
                Dim selected As Boolean =
                    (e.State And DrawItemState.Selected) = DrawItemState.Selected
                Using bg As New SolidBrush(If(selected, RowHover, CardBg))
                    e.Graphics.FillRectangle(bg, e.Bounds)
                End Using
                Dim txt As String = cbo.Items(e.Index).ToString()
                TextRenderer.DrawText(e.Graphics, txt, cbo.Font,
                    New Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height),
                    MintDark, TextFormatFlags.Left Or TextFormatFlags.VerticalCenter)
            End Sub

        Dim btnY As Integer = cbo.Bottom + 22
        Dim btnOk As New Button With {
            .Text = "Merge", .Size = New Size(110, 36),
            .Location = New Point(CARD_W - PAD - 110, btnY),
            .FlatStyle = FlatStyle.Flat, .BackColor = MintPrimary, .ForeColor = Color.White,
            .Font = New Font("Segoe UI Semibold", 9.5F), .Cursor = Cursors.Hand
        }
        btnOk.FlatAppearance.BorderSize = 0
        Dim btnCancel As New Button With {
            .Text = "Cancel", .Size = New Size(96, 36),
            .Location = New Point(CARD_W - PAD - 110 - 8 - 96, btnY),
            .FlatStyle = FlatStyle.Flat, .BackColor = Color.FromArgb(243, 246, 244),
            .ForeColor = Color.FromArgb(70, 86, 80), .Font = New Font("Segoe UI Semibold", 9.5F),
            .Cursor = Cursors.Hand
        }
        btnCancel.FlatAppearance.BorderSize = 0

        AddHandler btnCancel.Click, Sub() f.Close()
        AddHandler btnOk.Click,
            Sub()
                Dim idx As Integer = cbo.SelectedIndex
                If idx >= 0 AndAlso idx < targets.Count Then chosen = targets(idx).CategoryId
                f.Close()
            End Sub

        Dim cardH As Integer = btnY + 36 + PAD
        f.Height = cardH
        f.Controls.AddRange({lblTitle, lblBody, lblPick, cbo, btnOk, btnCancel})

        AddHandler f.Paint,
            Sub(s As Object, e As PaintEventArgs)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim rc As New Rectangle(0, 0, f.Width - 1, f.Height - 1)
                Using pen As New Pen(Color.FromArgb(160, 195, 185), 1.2F)
                    Using path As New Drawing2D.GraphicsPath()
                        Dim d As Integer = 20
                        path.AddArc(rc.X, rc.Y, d, d, 180, 90)
                        path.AddArc(rc.Right - d, rc.Y, d, d, 270, 90)
                        path.AddArc(rc.Right - d, rc.Bottom - d, d, d, 0, 90)
                        path.AddArc(rc.X, rc.Bottom - d, d, d, 90, 90)
                        path.CloseFigure()
                        e.Graphics.DrawPath(pen, path)
                    End Using
                End Using
            End Sub
        AddHandler f.KeyDown, Sub(s As Object, e As KeyEventArgs)
                                  If e.KeyCode = Keys.Escape Then f.Close()
                              End Sub
        f.AcceptButton = btnOk

        Try
            Dim owner As Form = Form.ActiveForm
            If owner Is Nothing OrElse owner Is f Then
                f.StartPosition = FormStartPosition.CenterScreen
                f.ShowDialog()
            Else
                f.ShowDialog(owner)
            End If
        Finally
            f.Dispose()
        End Try

        Return chosen
    End Function

    Private Shared Sub RemoveCategory(r As CategoryRecord)
        ' Re-check against the DB rather than trusting the cached row count —
        ' a product could have been assigned since this list was drawn.
        Dim inUse As Integer = 0
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT COUNT(*) FROM products WHERE category_id = @id", conn)
                    cmd.Parameters.AddWithValue("@id", r.CategoryId)
                    inUse = Convert.ToInt32(cmd.ExecuteScalar())
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not check whether the category is in use: " & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try

        If inUse > 0 Then
            AppModal.Show("""" & r.Name & """ is used by " & inUse.ToString("N0") &
                          If(inUse = 1, " product", " products") & " and cannot be removed." &
                          vbCrLf & vbCrLf &
                          "Reassign those products to another category in Inventory ▸ Item List " &
                          "first, then remove this one.",
                          "Category In Use", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        If AppModal.Show("Remove the category """ & r.Name & """?", "Confirm",
                         MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then
            Return
        End If

        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using tx = conn.BeginTransaction()
                    Archive.Capture(conn, tx, "categories", "category_id", r.CategoryId, r.Name)
                    Using cmd As New MySqlCommand(
                        "DELETE FROM categories WHERE category_id = @id", conn, tx)
                        cmd.Parameters.AddWithValue("@id", r.CategoryId)
                        cmd.ExecuteNonQuery()
                    End Using
                    tx.Commit()
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not remove the category: " & ex.Message, "Database Error",
                          MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try

        ActivityLog.Record("CATEGORY_DELETE", "category", r.CategoryId, r.Name)
        LoadCategories(_container)
    End Sub

    ' =========================================================================
    '  SHARED HELPERS  (same implementations as the other panels)
    ' =========================================================================
    Private Shared Function MakeActionButton(text As String, backClr As Color, foreClr As Color) As Button
        Dim b As New Button With {
            .Text = text,
            .FlatStyle = FlatStyle.Flat,
            .Cursor = Cursors.Hand,
            .Size = New Size(84, 30),
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
