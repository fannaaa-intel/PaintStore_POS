Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports MySql.Data.MySqlClient

' =============================================================================
'  AdminSuppliersPanelBuilder  —  Admin ▸ Suppliers  (READ-ONLY oversight)
'
'  Supplier CRUD belongs to ProcurementSuppliersPanelBuilder. This screen is a
'  window onto the same data for oversight only: no add, edit, delete, or
'  activate/deactivate anywhere in this file.
'
'  Unlike the Procurement screen this lists INACTIVE suppliers too — that is
'  the point of an oversight view — with an "Inactive" badge to distinguish
'  them.
'
'  LAYOUT  mirrors AdminRolesPanelBuilder:
'    TopRegion (Dock = Top, 72px):
'      └─ TitleBar : "Suppliers" + search + "Read-only" pill + Refresh
'    TableWrapper (Dock = Fill, 24px side spacers + 16px top/bottom):
'      └─ listArea (BufferedPanel, PanelRenderer card, Dock = Top):
'           ├─ ColHeader   (Dock = Top,    44px)
'           ├─ _scrollHost (Dock = Fill,   AutoScroll)
'           └─ bottomPad   (Dock = Bottom, 12px)
'
'  Counts come from one grouped query, not a per-row lookup:
'    COUNT(DISTINCT p.product_id) / COUNT(DISTINCT po.po_id) over two LEFT
'    JOINs. DISTINCT is required — joining both tables at once multiplies rows.
'
'  The supplier logo is drawn by the shared SupplierLogo module, the same one
'  Procurement uploads through, so both screens show one photo from one cache.
'  suppliers.image_path is optional (see migrations.sql); without it the tile
'  falls back to the supplier's initial.
' =============================================================================
Public Class AdminSuppliersPanelBuilder

    ' ── PALETTE  (matches the other ADMIN panels) ────────────────────────────
    Private Shared ReadOnly MintDark As Color = Color.FromArgb(10, 79, 66)
    Private Shared ReadOnly MintPrimary As Color = Color.FromArgb(0, 168, 142)
    Private Shared ReadOnly MintBg As Color = Color.FromArgb(244, 248, 246)
    Private Shared ReadOnly CardBg As Color = Color.White
    Private Shared ReadOnly SubText As Color = Color.FromArgb(90, 114, 107)
    Private Shared ReadOnly FaintText As Color = Color.FromArgb(150, 165, 160)
    Private Shared ReadOnly RowDivider As Color = Color.FromArgb(238, 242, 240)
    Private Shared ReadOnly RowHover As Color = Color.FromArgb(224, 242, 235)

    Private Shared ReadOnly TintMint As (fore As Color, back As Color) =
        (Color.FromArgb(0, 120, 92), Color.FromArgb(222, 246, 238))
    Private Shared ReadOnly TintBlue As (fore As Color, back As Color) =
        (Color.FromArgb(45, 80, 150), Color.FromArgb(232, 238, 250))
    Private Shared ReadOnly TintGrey As (fore As Color, back As Color) =
        (Color.FromArgb(90, 114, 107), Color.FromArgb(238, 242, 240))

    ' ── DATA MODEL ────────────────────────────────────────────────────────────
    Private Class SupplierRecord
        Public SupplierId As Integer
        Public Name As String
        Public ContactPerson As String
        Public Phone As String
        Public Email As String
        Public ImagePath As String
        Public IsActive As Boolean
        Public ProductCount As Integer
        Public PoCount As Integer
    End Class

    ' ── STATE ─────────────────────────────────────────────────────────────────
    Private Const SIDE_PAD As Integer = 24
    Private Const ROW_H As Integer = 76
    Private Const HDR_H As Integer = 44
    Private Const PAD_TOP As Integer = 4
    Private Const PAD_BOTTOM As Integer = 12
    Private Const SEARCH_PLACEHOLDER As String = "Search by name or contact…"

    Private Shared _container As Panel
    Private Shared _scrollHost As Panel
    Private Shared _listArea As Panel
    Private Shared _allRows As New List(Of SupplierRecord)
    Private Shared _visibleRows As New List(Of SupplierRecord)
    Private Shared _searchQuery As String = ""
    Private Shared _reflowAttached As Boolean = False

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Shared Sub LoadSuppliers(container As Panel)
        _container = container
        _searchQuery = ""
        SupplierLogo.Probe()
        ' A logo replaced in Procurement since this screen last rendered must be
        ' re-read rather than served from the bitmap cached at that size.
        SupplierLogo.ClearCache(container)
        LoadData()
        Render()
    End Sub

    ' =========================================================================
    '  DATA
    ' =========================================================================
    Private Shared Sub LoadData()
        _allRows.Clear()

        Try
            Using conn = Db.GetConnection()
                conn.Open()
                ' The optional columns join both the projection and the
                ' GROUP BY, and only the ones that actually exist.
                Dim sql As String =
                    "SELECT s.supplier_id, s.name, s.contact_person, s.phone, s.email, s.is_active, " &
                    SupplierLogo.SelectFragment() &
                    "COUNT(DISTINCT p.product_id) AS product_count, " &
                    "COUNT(DISTINCT po.po_id) AS po_count " &
                    "FROM suppliers s " &
                    "LEFT JOIN products p ON p.supplier_id = s.supplier_id " &
                    "LEFT JOIN purchase_orders po ON po.supplier_id = s.supplier_id " &
                    "GROUP BY s.supplier_id, s.name, s.contact_person, s.phone, s.email, s.is_active" &
                    SupplierLogo.GroupByFragment() &
                    " ORDER BY s.name ASC"

                Using cmd As New MySqlCommand(sql, conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            _allRows.Add(New SupplierRecord With {
                                .SupplierId = SafeInt(rdr("supplier_id")),
                                .Name = SafeStr(rdr("name")),
                                .ContactPerson = SafeStr(rdr("contact_person")),
                                .Phone = SafeStr(rdr("phone")),
                                .Email = SafeStr(rdr("email")),
                                .ImagePath = SupplierLogo.ReadPath(rdr),
                                .IsActive = (Not IsDBNull(rdr("is_active"))) AndAlso
                                            Convert.ToBoolean(rdr("is_active")),
                                .ProductCount = SafeInt(rdr("product_count")),
                                .PoCount = SafeInt(rdr("po_count"))
                            })
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load suppliers: " & ex.Message, "Database Error",
                          MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try

        ApplySearch()
    End Sub

    ' Filtered in memory: the supplier list is small and already loaded, so a
    ' round trip per keystroke would buy nothing.
    Private Shared Sub ApplySearch()
        _visibleRows.Clear()
        If String.IsNullOrWhiteSpace(_searchQuery) Then
            _visibleRows.AddRange(_allRows)
        Else
            Dim q As String = _searchQuery.ToLower()
            For Each r In _allRows
                If r.Name.ToLower().Contains(q) OrElse r.ContactPerson.ToLower().Contains(q) Then
                    _visibleRows.Add(r)
                End If
            Next
        End If
    End Sub

    Private Shared Function SafeInt(o As Object) As Integer
        If o Is Nothing OrElse IsDBNull(o) Then Return 0
        Return Convert.ToInt32(o)
    End Function

    Private Shared Function SafeStr(o As Object) As String
        If o Is Nothing OrElse IsDBNull(o) Then Return ""
        Return o.ToString()
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
            .Dock = DockStyle.Bottom,
            .Height = PAD_BOTTOM,
            .BackColor = Color.Transparent
        }

        _listArea.Controls.Add(bottomPad)
        _listArea.Controls.Add(colHdr)
        _listArea.Controls.Add(_scrollHost)
        colHdr.BringToFront()
        _scrollHost.BringToFront()
        _scrollHost.AutoScrollMinSize = New Size(0, 1)

        Dim spacerBottom As New Panel With {.Dock = DockStyle.Bottom, .Height = 16, .BackColor = MintBg}
        Dim spacerTop As New Panel With {.Dock = DockStyle.Top, .Height = 16, .BackColor = MintBg}
        Dim spacerLeft As New Panel With {.Dock = DockStyle.Left, .Width = SIDE_PAD, .BackColor = MintBg}
        Dim spacerRight As New Panel With {.Dock = DockStyle.Right, .Width = SIDE_PAD, .BackColor = MintBg}

        tableWrapper.Controls.Add(_listArea)
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

    ' Card follows the row count, capped at the viewport so it can never
    ' overflow the container; _scrollHost picks up anything past the cap.
    Private Shared Function CardHeightFor(rowCount As Integer) As Integer
        Dim contentH As Integer = If(rowCount = 0, 90, rowCount * ROW_H)
        Dim cardH As Integer = HDR_H + PAD_TOP + contentH + PAD_BOTTOM + 2
        Dim maxH As Integer = _container.ClientSize.Height - 72 - 32
        If maxH > 160 AndAlso cardH > maxH Then cardH = maxH
        Return cardH
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
            .Text = "Suppliers",
            .Font = New Font("Segoe UI", 28, FontStyle.Bold),
            .ForeColor = MintDark,
            .AutoSize = True,
            .Location = New Point(SIDE_PAD, 16)
        }

        ' Makes it obvious nothing here writes — management is in Procurement.
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
        AddHandler btnRefresh.Paint,
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
        AddHandler btnRefresh.MouseEnter, Sub()
                                              btnRefresh.BackColor = Color.FromArgb(210, 240, 235)
                                              btnRefresh.Invalidate()
                                          End Sub
        AddHandler btnRefresh.MouseLeave, Sub()
                                              btnRefresh.BackColor = Color.FromArgb(220, 235, 232)
                                              btnRefresh.Invalidate()
                                          End Sub
        AddHandler btnRefresh.Click, Sub() LoadSuppliers(_container)

        ' ── Search box ───────────────────────────────────────────────────────
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
                ApplySearch()
                PopulateRows()
            End Sub
        AddHandler searchWrap.Click, Sub() txtSearch.Focus()
        searchWrap.Controls.Add(txtSearch)

        AddHandler titleBar.Resize,
            Sub()
                btnRefresh.Left = titleBar.Width - btnRefresh.Width - SIDE_PAD
                btnRefresh.Top = (titleBar.Height - btnRefresh.Height) \ 2
                pill.Left = btnRefresh.Left - pill.Width - 10
                pill.Top = (titleBar.Height - pill.Height) \ 2
                searchWrap.Left = pill.Left - searchWrap.Width - 10
                searchWrap.Top = (titleBar.Height - searchWrap.Height) \ 2
            End Sub

        titleBar.Controls.Add(lblTitle)
        titleBar.Controls.Add(searchWrap)
        titleBar.Controls.Add(pill)
        titleBar.Controls.Add(btnRefresh)

        region.Controls.Add(titleBar)
        Return region
    End Function

    ' =========================================================================
    '  COLUMN HEADER  —  44px (must match BuildDataRow)
    ' =========================================================================
    Private Shared Function BuildColHeader() As Panel
        Dim hdr As New Panel With {.Height = HDR_H, .BackColor = Color.Transparent}

        Dim labels As Label() = {
            MakeColLabel("SUPPLIER", New Point(24 + 38 + 14, 13)),
            MakeColLabel("CONTACT", New Point(0, 13)),
            MakeColLabel("PRODUCTS", New Point(0, 13)),
            MakeColLabel("PURCHASE ORDERS", New Point(0, 13))
        }
        hdr.Controls.AddRange(labels)

        AddHandler hdr.Resize,
            Sub()
                Dim w = hdr.Width
                ' Aligned with the name text, which now starts past the logo.
                labels(0).Left = 24 + 38 + 14
                labels(1).Left = CInt(w * 0.3)
                labels(2).Left = CInt(w * 0.64)
                labels(3).Left = CInt(w * 0.79)
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

        ' Filtering changes the row count, so the card is resized here rather
        ' than by a full Render() — that would drop focus out of the search box.
        If _listArea IsNot Nothing Then
            _listArea.Height = CardHeightFor(_visibleRows.Count)
        End If

        _scrollHost.SuspendLayout()
        _scrollHost.Controls.Clear()

        If _reflowAttached Then
            RemoveHandler _scrollHost.Resize, AddressOf ReflowScrollHost
            _reflowAttached = False
        End If

        If _visibleRows.Count = 0 Then
            _scrollHost.Controls.Add(BuildEmptyRow(
                If(String.IsNullOrWhiteSpace(_searchQuery),
                   "No suppliers found.",
                   $"No suppliers match ""{_searchQuery}"".")))
        Else
            Dim y As Integer = PAD_TOP
            For Each r As SupplierRecord In _visibleRows
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

    Private Shared Function BuildDataRow(r As SupplierRecord, height As Integer) As Panel
        Dim p As New BufferedPanel With {
            .Height = height, .BackColor = Color.Transparent
        }

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

        ' Logo tile — the same avatar Procurement uploads, so a supplier is
        ' recognisable here at a glance rather than by name alone.
        Dim avSize As Integer = 38
        Dim initial As String = SupplierLogo.InitialOf(r.Name)
        Dim capImage As String = r.ImagePath
        Dim isActive As Boolean = r.IsActive
        Dim avatar As New BufferedPanel With {
            .Size = New Size(avSize, avSize),
            .BackColor = Color.Transparent
        }
        AddHandler avatar.Paint,
            Sub(s2, e2)
                Dim box As New Rectangle(0, 0, avSize - 1, avSize - 1)
                SupplierLogo.Paint(e2.Graphics, box, 11, capImage, initial, 15.0F)
                ' Inactive suppliers read as muted everywhere else on the row;
                ' the logo is greyed to match rather than staying full colour.
                If Not isActive Then
                    e2.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                    Using br As New SolidBrush(Color.FromArgb(150, 244, 248, 246))
                        e2.Graphics.FillRoundedRectangle(br, box, 11)
                    End Using
                End If
            End Sub

        Dim lblName As New Label With {
            .Text = If(r.Name = "", "—", r.Name),
            .Font = New Font("Segoe UI", 11, FontStyle.Bold),
            .ForeColor = If(r.IsActive, MintDark, FaintText),
            .AutoSize = True, .BackColor = Color.Transparent
        }

        ' Only inactive suppliers carry a badge — active is the norm and would
        ' just add noise on every row.
        Dim badge As Label = Nothing
        If Not r.IsActive Then
            badge = New Label With {
                .Text = "Inactive",
                .Font = New Font("Segoe UI", 8.5F, FontStyle.Bold),
                .ForeColor = TintGrey.fore,
                .BackColor = TintGrey.back,
                .AutoSize = True,
                .Padding = New Padding(8, 3, 8, 3)
            }
            RoundLabel(badge, 9)
        End If

        Dim lblContact As New Label With {
            .Text = If(r.ContactPerson = "", "—", r.ContactPerson),
            .Font = New Font("Segoe UI", 10.5F),
            .ForeColor = If(r.ContactPerson = "", FaintText, SubText),
            .AutoSize = True, .BackColor = Color.Transparent
        }

        ' Phone and email share the second line of the contact column.
        Dim parts As New List(Of String)
        If r.Phone <> "" Then parts.Add(r.Phone)
        If r.Email <> "" Then parts.Add(r.Email)
        Dim lblDetail As New Label With {
            .Text = If(parts.Count = 0, "no contact details", String.Join("  ·  ", parts)),
            .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = FaintText,
            .AutoSize = False,
            .AutoEllipsis = True,
            .Height = 18,
            .BackColor = Color.Transparent
        }

        Dim prodTint = If(r.ProductCount > 0, TintMint, TintGrey)
        Dim prodPill As New Label With {
            .Text = r.ProductCount.ToString("N0"),
            .Font = New Font("Segoe UI Semibold", 9.5F),
            .ForeColor = prodTint.fore,
            .BackColor = prodTint.back,
            .AutoSize = True,
            .Padding = New Padding(12, 5, 12, 5)
        }
        RoundLabel(prodPill, 10)

        Dim poTint = If(r.PoCount > 0, TintBlue, TintGrey)
        Dim poPill As New Label With {
            .Text = r.PoCount.ToString("N0"),
            .Font = New Font("Segoe UI Semibold", 9.5F),
            .ForeColor = poTint.fore,
            .BackColor = poTint.back,
            .AutoSize = True,
            .Padding = New Padding(12, 5, 12, 5)
        }
        RoundLabel(poPill, 10)

        p.Controls.Add(avatar)
        p.Controls.Add(lblName)
        If badge IsNot Nothing Then p.Controls.Add(badge)
        p.Controls.AddRange({lblContact, lblDetail, prodPill, poPill})

        AddHandler p.Resize,
            Sub()
                Dim w = p.Width
                Dim contactX As Integer = CInt(w * 0.3)
                Dim prodX As Integer = CInt(w * 0.64)
                Dim poX As Integer = CInt(w * 0.79)

                avatar.Left = 24
                avatar.Top = (height - avSize) \ 2

                Dim nameX As Integer = 24 + avSize + 14
                lblName.Left = nameX
                lblName.Top = (height - lblName.Height) \ 2
                If badge IsNot Nothing Then
                    badge.Left = nameX + lblName.PreferredWidth + 8
                    badge.Top = (height - badge.Height) \ 2
                End If

                ' Two-line contact block, centred as a unit
                lblContact.Left = contactX
                lblContact.Top = (height \ 2) - lblContact.Height - 1
                lblDetail.Left = contactX
                lblDetail.Top = (height \ 2) + 2
                lblDetail.Width = Math.Max(60, prodX - contactX - 20)

                prodPill.Left = prodX
                prodPill.Top = (height - prodPill.Height) \ 2
                poPill.Left = poX
                poPill.Top = (height - poPill.Height) \ 2
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
    '  SHARED HELPERS  (same implementations as the other panels)
    ' =========================================================================
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
