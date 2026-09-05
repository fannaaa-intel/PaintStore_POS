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
    Private Shared ReadOnly MintDark As Color = Color.FromArgb(10, 79, 66)
    Private Shared ReadOnly MintPrimary As Color = Color.FromArgb(0, 168, 142)
    Private Shared ReadOnly MintBg As Color = Color.FromArgb(244, 248, 246)
    Private Shared ReadOnly CardBg As Color = Color.White
    Private Shared ReadOnly SubText As Color = Color.FromArgb(90, 114, 107)
    Private Shared ReadOnly FaintText As Color = Color.FromArgb(150, 165, 160)
    Private Shared ReadOnly RowDivider As Color = Color.FromArgb(238, 242, 240)
    Private Shared ReadOnly RowHover As Color = Color.FromArgb(224, 242, 235)
    Private Shared ReadOnly AccentRed As Color = Color.FromArgb(210, 60, 60)

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
        Dim maxH As Integer = _container.ClientSize.Height - 72 - 32
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

        _container.Controls.Add(tableWrapper)
        _container.Controls.Add(topRegion)

        _container.ResumeLayout()

        PopulateRows()
    End Sub

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

        Dim btnRefresh As New Button With {
            .Text = "⟳  Refresh",
            .FlatStyle = FlatStyle.Flat,
            .Cursor = Cursors.Hand,
            .Size = New Size(100, 34),
            .Font = New Font("Segoe UI", 9.5F),
            .BackColor = Color.FromArgb(220, 235, 232),
            .ForeColor = MintDark,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        btnRefresh.FlatAppearance.BorderSize = 0
        AddHandler btnRefresh.Paint, AddressOf PaintPillButton
        AddHandler btnRefresh.MouseEnter, Sub()
                                              btnRefresh.BackColor = Color.FromArgb(210, 240, 235)
                                              btnRefresh.Invalidate()
                                          End Sub
        AddHandler btnRefresh.MouseLeave, Sub()
                                              btnRefresh.BackColor = Color.FromArgb(220, 235, 232)
                                              btnRefresh.Invalidate()
                                          End Sub
        AddHandler btnRefresh.Click, Sub() LoadCategories(_container)

        AddHandler titleBar.Resize,
            Sub()
                btnNew.Left = titleBar.Width - btnNew.Width - SIDE_PAD
                btnNew.Top = (titleBar.Height - btnNew.Height) \ 2
                btnRefresh.Left = btnNew.Left - btnRefresh.Width - 10
                btnRefresh.Top = (titleBar.Height - btnRefresh.Height) \ 2
            End Sub

        titleBar.Controls.Add(lblTitle)
        titleBar.Controls.Add(btnRefresh)
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
                Using br As New SolidBrush(If(inUse, MintPrimary, Color.FromArgb(200, 208, 205)))
                    e.Graphics.FillRectangle(br, New Rectangle(0, 12, 3, height - 24))
                End Using
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

        p.Controls.AddRange({lblName, countPill, btnRename, btnRemove})

        AddHandler p.Resize,
            Sub()
                Dim w = p.Width
                lblName.Left = 24 : lblName.Top = (height - lblName.Height) \ 2
                countPill.Left = CInt(w * 0.42)
                countPill.Top = (height - countPill.Height) \ 2

                Dim ax As Integer = CInt(w * 0.7)
                Dim ay As Integer = (height - btnRename.Height) \ 2
                btnRename.Left = ax : btnRename.Top = ay
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

        If name = "" Then
            AppModal.Show("The category name cannot be empty.", "New Category",
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

        If name = "" Then
            AppModal.Show("The category name cannot be empty.", "Rename Category",
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
        Catch ex As Exception
            AppModal.Show("Could not rename the category: " & ex.Message, "Database Error",
                          MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try

        ActivityLog.Record("CATEGORY_EDIT", "category", r.CategoryId, name)
        LoadCategories(_container)
    End Sub

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
                Using cmd As New MySqlCommand(
                    "DELETE FROM categories WHERE category_id = @id", conn)
                    cmd.Parameters.AddWithValue("@id", r.CategoryId)
                    cmd.ExecuteNonQuery()
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
