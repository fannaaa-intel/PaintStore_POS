Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Windows.Forms
Imports MySql.Data.MySqlClient

' =============================================================================
'  ProcurementSuppliersPanelBuilder
'  Screen 3 — Suppliers.
'
'  INLINE VIEWS (swapped in the same container):
'    1. List    — every supplier with contact details + active-PO count.
'                 "Add Supplier" opens the form.
'    2. Form    — add OR edit a supplier (logo / name / contact / phone /
'                 email / address). Save inserts or updates the suppliers table.
'    3. Detail  — one supplier: identity card, product chips
'                 (products WHERE supplier_id = this), last-10 PO history,
'                 and an Edit button.
'
'  SUPPLIER LOGO
'    suppliers.image_path is optional (see migrations.sql). The file itself is
'    copied into SupplierImages\ and the row stores the path relative to the app
'    folder, the same convention products.image_path uses. Where the column does
'    not exist the screen falls back to the gradient-initial avatar and hides
'    the upload tile, so an un-migrated database still works.
' =============================================================================
Public Class ProcurementSuppliersPanelBuilder

    ' ── PALETTE ──────────────────────────────────────────────────────────────────
    Private Shared ReadOnly MintDark As Color = Theme.Dark
    Private Shared ReadOnly MintPrimary As Color = Theme.Primary
    Private Shared ReadOnly MintDeep As Color = Theme.Deep
    Private Shared ReadOnly MintBg As Color = Theme.PageBg
    Private Shared ReadOnly CardBg As Color = Color.White
    Private Shared ReadOnly CardBorder As Color = Theme.BorderSoft
    Private Shared ReadOnly SubText As Color = Theme.SubText
    Private Shared ReadOnly FaintText As Color = Theme.FaintText
    Private Shared ReadOnly TileBg As Color = Theme.TileBg
    Private Shared ReadOnly RowDivider As Color = Theme.RowDivider
    Private Shared ReadOnly PositiveClr As Color = Theme.Success
    Private Shared ReadOnly NegativeClr As Color = Theme.Danger
    Private Shared ReadOnly WarningClr As Color = Theme.Warning
    Private Shared ReadOnly Peso As String = ChrW(8369)

    ' ── DATA MODELS ──────────────────────────────────────────────────────────────
    Private Class SupplierRow
        Public Id As Integer
        Public Name As String
        Public Contact As String
        Public Phone As String
        Public Email As String
        Public Address As String
        Public ImagePath As String
        Public Tin As String
        Public PaymentTerms As String
        Public Notes As String
        ' Business registration. CompanyName is the REGISTERED name on the
        ' DTI/SEC papers, which is often not the trading name in Name.
        Public CompanyName As String
        Public HeadName As String
        Public DtiNo As String
        Public BirNo As String
        Public IsActive As Boolean
        Public ActivePoCount As Integer
    End Class

    Private Class POHistRow
        Public PoNo As String
        Public OrderDate As DateTime
        Public Status As String
        Public TotalCost As Decimal
        Public ItemCount As Integer
    End Class

    ' ── STATE ────────────────────────────────────────────────────────────────────
    Private Shared containerRef As Panel

    Private Shared editingId As Integer = 0
    Private Shared txtName As TextBox
    Private Shared txtContact As TextBox
    Private Shared txtPhone As TextBox
    Private Shared txtEmail As TextBox
    Private Shared txtAddress As TextBox
    Private Shared txtTin As TextBox
    Private Shared txtCompany As TextBox
    Private Shared txtHead As TextBox
    Private Shared txtDti As TextBox
    Private Shared txtBir As TextBox
    Private Shared txtNotes As TextBox
    Private Shared cboTerms As ComboBox
    Private Shared cboStatus As ComboBox
    Private Shared btnSaveRef As Button

    ' Logo chosen on the form, as a path relative to the app folder. Empty means
    ' "no photo" — which is also how an existing photo is cleared.
    Private Shared pickedImagePath As String = ""


    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Shared Sub LoadSuppliers(container As Panel)
        containerRef = container
        SupplierLogo.Probe()
        RenderList()
    End Sub

    ' =========================================================================
    '  VIEW 1 — LIST
    ' =========================================================================
    Private Shared Sub RenderList()
        Dim container As Panel = containerRef
        If container Is Nothing Then Return

        ' Drop the scaled avatars: a logo replaced since the last render would
        ' otherwise keep painting from the stale bitmap, and each bitmap holds
        ' a GDI+ handle until it is disposed.
        SupplierLogo.ClearCache(container)

        Dim rows As List(Of SupplierRow) = LoadSupplierList()

        container.SuspendLayout()
        container.Controls.Clear()
        container.BackColor = MintBg

        Dim listHost As New Panel With {
            .Dock = DockStyle.Fill, .BackColor = MintBg, .Padding = New Padding(24, 4, 24, 20)
        }
        Dim rowsHost As New Panel With {
            .Dock = DockStyle.Fill, .BackColor = Color.Transparent, .AutoScroll = True
        }
        listHost.Controls.Add(rowsHost)

        Dim topRegion As Panel = BuildListTop(rows.Count)
        topRegion.Dock = DockStyle.Top

        container.Controls.Add(listHost)
        container.Controls.Add(topRegion)
        container.ResumeLayout()

        If rows.Count = 0 Then
            Dim empty As Panel = BuildEmptyCard("No suppliers yet",
                "Use " & ChrW(8220) & "Add Supplier" & ChrW(8221) & " to create your first one.")
            empty.Location = New Point(0, 16)
            empty.Width = rowsHost.ClientSize.Width - 4
            empty.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
            rowsHost.Controls.Add(empty)
        Else
            Dim y As Integer = 8
            For Each sup In rows
                Dim card As Panel = BuildSupplierCard(sup)
                card.Location = New Point(0, y)
                card.Width = rowsHost.ClientSize.Width - 4
                card.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
                rowsHost.Controls.Add(card)
                y += card.Height + 14
            Next
            AddHandler rowsHost.Resize,
                Sub()
                    Dim w As Integer = rowsHost.ClientSize.Width - 4
                    For Each c As Control In rowsHost.Controls
                        c.Width = w
                    Next
                End Sub
        End If
    End Sub

    ' ── LIST HEADER (REDESIGNED) ──────────────────────────────────────────────────
    Private Shared Function BuildListTop(count As Integer) As Panel
        Dim region As New Panel With {
            .Height = 96, .BackColor = MintBg, .Padding = New Padding(24, 22, 24, 0)
        }

        Dim lblTitle As New Label With {
            .Text = "Suppliers",
            .Font = New Font("Segoe UI", 24, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True,
            .Location = New Point(24, 16), .BackColor = Color.Transparent
        }

        Dim lblSub As New Label With {
            .Text = If(count = 0, "No suppliers yet",
                       If(count = 1, "1 supplier registered", count & " suppliers registered")),
            .Font = New Font("Segoe UI", 11), .ForeColor = SubText,
            .AutoSize = True, .Location = New Point(30, 58), .BackColor = Color.Transparent
        }

        Dim btnAdd As New Button With {
            .Text = "  + Add Supplier",
            .Size = New Size(152, 40),
            .FlatStyle = FlatStyle.Flat,
            .BackColor = MintPrimary, .ForeColor = Color.White,
            .Font = New Font("Segoe UI Semibold", 10),
            .Cursor = Cursors.Hand,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        btnAdd.FlatAppearance.BorderSize = 0
        RoundControl(btnAdd, 10)
        AddHandler btnAdd.Click, Sub() LoadForm(0)
        AddHandler btnAdd.MouseEnter,
            Sub()
                btnAdd.BackColor = MintDeep
                btnAdd.Invalidate()
            End Sub
        AddHandler btnAdd.MouseLeave,
            Sub()
                btnAdd.BackColor = MintPrimary
                btnAdd.Invalidate()
            End Sub

        Dim btnRefresh As New Button With {
    .Text = "↻  Refresh",
    .FlatStyle = FlatStyle.Flat,
    .Cursor = Cursors.Hand,
    .Size = New Size(100, 34),
    .Font = New Font("Segoe UI", 9.5F),
    .BackColor = Color.FromArgb(220, 235, 232),
    .ForeColor = SubText,
    .Anchor = AnchorStyles.Top Or AnchorStyles.Right
}
        btnRefresh.FlatAppearance.BorderSize = 0
        AddHandler btnRefresh.Paint,
    Sub(s, e)
        Dim bb = CType(s, Button)
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
        Dim rect = bb.ClientRectangle : rect.Inflate(-1, -1)
        Using br As New SolidBrush(bb.BackColor)
            e.Graphics.FillRoundedRectangle(br, rect, 8)
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
        AddHandler btnRefresh.Click, Sub() RenderList()

        region.Controls.AddRange({lblTitle, lblSub, btnAdd, btnRefresh})

        AddHandler region.Resize,
    Sub()
        btnAdd.Top = 22
        btnAdd.Left = region.Width - 24 - btnAdd.Width
        btnRefresh.Top = 22
        btnRefresh.Left = btnAdd.Left - 8 - btnRefresh.Width
    End Sub
        btnAdd.Top = 22
        btnAdd.Left = 700
        btnRefresh.Top = 22
        btnRefresh.Left = 584

        Return region
    End Function

    ' ── SUPPLIER CARD (REDESIGNED) ────────────────────────────────────────────────
    '   • PanelRenderer for card shadow/border
    '   • Gradient-initial avatar on the left
    '   • Name / contact line / address line (darker, clearly readable)
    '   • PO pill top-right, chevron icon circle pill bottom-right
    '   • Subtle hover tint
    Private Shared Function BuildSupplierCard(sup As SupplierRow) As Panel
        Dim cardH As Integer = 148
        Dim card As New BufferedPanel With {
            .Height = cardH, .BackColor = CardBg, .Cursor = Cursors.Hand
        }

        AddHandler card.Paint,
            Sub(s, e)
                PaintCard(CType(s, Panel), e)
            End Sub

        ' ── Avatar bubble ────────────────────────────────────────────────────────
        Dim initial As String = SupplierLogo.InitialOf(sup.Name)
        Dim avSize As Integer = 52
        Dim avatar As New BufferedPanel With {
            .Size = New Size(avSize, avSize),
            .Location = New Point(22, (cardH - avSize) \ 2),
            .BackColor = Color.Transparent
        }
        Dim capImage As String = sup.ImagePath
        AddHandler avatar.Paint,
            Sub(s, e)
                SupplierLogo.Paint(e.Graphics, New Rectangle(0, 0, avSize - 1, avSize - 1),
                            14, capImage, initial, 20.0F)
            End Sub
        card.Controls.Add(avatar)

        ' ── Text content ─────────────────────────────────────────────────────────
        Dim textX As Integer = avSize + 36

        ' Line 1 — Supplier name
        Dim lblName As New Label With {
            .Text = sup.Name,
            .Font = New Font("Segoe UI", 13, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True,
            .Location = New Point(textX, 20), .BackColor = Color.Transparent
        }

        ' Line 2 — Contact | Phone | Email  (darker, readable)
        Dim contactBits As New List(Of String)
        If Not String.IsNullOrWhiteSpace(sup.Contact) Then contactBits.Add(sup.Contact)
        If Not String.IsNullOrWhiteSpace(sup.Phone) Then contactBits.Add(sup.Phone)
        If Not String.IsNullOrWhiteSpace(sup.Email) Then contactBits.Add(sup.Email)

        Dim lblContact As New Label With {
            .Text = If(contactBits.Count = 0, "No contact details",
                       String.Join("   " & ChrW(8226) & "   ", contactBits)),
            .Font = New Font("Segoe UI", 10),
            .ForeColor = Color.FromArgb(52, 68, 64),
            .AutoSize = True,
            .Location = New Point(textX, 50), .BackColor = Color.Transparent
        }

        ' Line 3 — Address  (darker, readable)
        Dim hasAddress As Boolean = Not String.IsNullOrWhiteSpace(sup.Address)
        Dim lblAddr As New Label With {
            .Text = If(hasAddress, ChrW(8226) & "  " & sup.Address, ""),
            .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = Color.FromArgb(72, 92, 87),
            .AutoSize = True,
            .Location = New Point(textX, 78), .BackColor = Color.Transparent,
            .Visible = hasAddress
        }

        card.Controls.AddRange({lblName, lblContact, lblAddr})

        ' ── Active PO pill (top-right) ───────────────────────────────────────────
        Dim poPill As New Label With {
            .Text = If(sup.ActivePoCount > 0,
                       sup.ActivePoCount & If(sup.ActivePoCount = 1, " active PO", " active POs"),
                       "No active POs"),
            .Font = New Font("Segoe UI Semibold", 10.0F, FontStyle.Bold),
            .ForeColor = If(sup.ActivePoCount > 0, Color.White, SubText),
            .BackColor = If(sup.ActivePoCount > 0, MintDeep, Color.Transparent),
            .AutoSize = True,
            .Padding = If(sup.ActivePoCount > 0, New Padding(13, 6, 13, 6), New Padding(0, 6, 2, 6)),
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        ' Only the "has active POs" state gets a filled pill; the empty state is
        ' a plain transparent label, so nothing is highlighted that needn't be.
        If sup.ActivePoCount > 0 Then PillLabel(poPill)
        card.Controls.Add(poPill)
        ' ── "View" button — solid mint, button-shaped corners (bottom-right) ────
        Dim vpH As Integer = 32
        Dim vpW As Integer = 92
        Dim viewPill As New BufferedPanel With {
            .Size = New Size(vpW, vpH),
            .BackColor = MintDeep,
            .Cursor = Cursors.Hand,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }

        ' Button-shaped region — modest 8px corners, NOT a pill
        Dim viewPillRadius As Integer = 6
        Using rgnPath As GraphicsPath = RoundRect(New Rectangle(0, 0, vpW, vpH), viewPillRadius * 2)
            viewPill.Region = New Region(rgnPath)
        End Using

        ' Hover / press state drives the fill colour
        Dim viewPillHot As Boolean = False
        Dim viewPillDown As Boolean = False

        AddHandler viewPill.Paint,
            Sub(s, e)
                Dim b As BufferedPanel = CType(s, BufferedPanel)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim fill As Color = MintDeep
                If viewPillDown Then
                    fill = Color.FromArgb(0, 104, 87)
                ElseIf viewPillHot Then
                    fill = MintPrimary
                End If
                Using path As GraphicsPath = RoundRect(New Rectangle(0, 0, b.Width - 1, b.Height - 1), viewPillRadius * 2)
                    Using br As New SolidBrush(fill)
                        e.Graphics.FillPath(br, path)
                    End Using
                End Using
                ' Label + chevron, centred as a unit
                Using f As New Font("Segoe UI Semibold", 9.5F, FontStyle.Regular),
                      br As New SolidBrush(Color.White)
                    Dim txtSz As SizeF = e.Graphics.MeasureString("View", f)
                    Dim gap As Single = 8
                    Dim chW As Single = 6 * 0.7F
                    Dim totalW As Single = txtSz.Width + gap + chW
                    Dim startX As Single = (b.Width - totalW) / 2.0F
                    Dim sf As New StringFormat With {
                        .Alignment = StringAlignment.Near,
                        .LineAlignment = StringAlignment.Center
                    }
                    e.Graphics.DrawString("View", f, br,
                        New RectangleF(startX, 0, txtSz.Width + 2, b.Height), sf)
                    Dim chX As Single = startX + txtSz.Width + gap
                    Dim midY As Single = b.Height / 2.0F
                    Dim arm As Single = 5
                    Using pen As New Pen(Color.White, 2.0F)
                        pen.LineJoin = LineJoin.Round
                        pen.StartCap = LineCap.Round
                        pen.EndCap = LineCap.Round
                        e.Graphics.DrawLines(pen, New PointF() {
                            New PointF(chX, midY - arm),
                            New PointF(chX + arm * 0.7F, midY),
                            New PointF(chX, midY + arm)
                        })
                    End Using
                End Using
            End Sub

        AddHandler viewPill.MouseEnter,
            Sub()
                viewPillHot = True
                viewPill.Invalidate()
            End Sub
        AddHandler viewPill.MouseLeave,
            Sub()
                viewPillHot = False
                viewPillDown = False
                viewPill.Invalidate()
            End Sub
        AddHandler viewPill.MouseDown,
            Sub()
                viewPillDown = True
                viewPill.Invalidate()
            End Sub
        AddHandler viewPill.MouseUp,
            Sub()
                viewPillDown = False
                viewPill.Invalidate()
            End Sub

        card.Controls.Add(viewPill)

        ' ── Click handlers ───────────────────────────────────────────────────────
        Dim capId As Integer = sup.Id
        AddHandler viewPill.Click, Sub() LoadDetail(capId)
        AddHandler card.Click, Sub() LoadDetail(capId)
        AddHandler lblName.Click, Sub() LoadDetail(capId)
        AddHandler lblContact.Click, Sub() LoadDetail(capId)

        ' ── Hover: subtle mint tint on background ────────────────────────────────
        AddHandler card.MouseEnter,
            Sub()
                card.BackColor = Color.FromArgb(248, 252, 250)
                card.Invalidate()
            End Sub
        AddHandler card.MouseLeave,
            Sub()
                card.BackColor = CardBg
                card.Invalidate()
            End Sub

        ' ── Layout: right-align poPill and viewPill on resize ─────────────────────
        AddHandler card.Resize,
            Sub()
                Dim rEdge As Integer = card.Width - 20
                poPill.Left = rEdge - poPill.Width
                poPill.Top = 24
                viewPill.Left = rEdge - viewPill.Width
                viewPill.Top = cardH - viewPill.Height - 24
                avatar.Top = (card.Height - avSize) \ 2
            End Sub

        Return card
    End Function

    ' =========================================================================
    '  VIEW 2 — ADD / EDIT FORM
    ' =========================================================================
    Private Shared Sub LoadForm(supplierId As Integer)
        Dim container As Panel = containerRef
        If container Is Nothing Then Return
        editingId = supplierId

        Dim existing As SupplierRow = Nothing
        If supplierId > 0 Then existing = LoadOneSupplier(supplierId)

        container.SuspendLayout()
        container.Controls.Clear()
        container.BackColor = MintBg

        ' ── Header ────────────────────────────────────────────────────────────
        ' Taller than the list header: this one carries a subtitle under the
        ' title, and the two are stacked by measurement rather than by a guessed
        ' offset, which is what let them overlap.
        Dim header As New Panel With {
            .Height = 96, .BackColor = MintBg,
            .Padding = New Padding(0, 0, 24, 0), .Dock = DockStyle.Top
        }
        Dim picBackF As PictureBox = BuildBackArrow()
        AddHandler picBackF.Click, Sub() If editingId > 0 Then LoadDetail(editingId) Else RenderList()
        Dim lblTitle As New Label With {
            .Text = If(supplierId > 0, "Edit Supplier", "Add Supplier"),
            .Font = New Font("Segoe UI", 22, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True,
            .Location = New Point(picBackF.Right + 10, 20), .BackColor = Color.Transparent
        }
        Dim lblTitleSub As New Label With {
            .Text = If(supplierId > 0,
                       "Update this supplier's details.",
                       "Register a new supplier. Fields marked * are required."),
            .Font = New Font("Segoe UI", 10), .ForeColor = SubText,
            .AutoSize = True, .BackColor = Color.Transparent,
            .Location = New Point(picBackF.Right + 12, 58)
        }
        header.Controls.AddRange({picBackF, lblTitle, lblTitleSub})

        ' PreferredHeight is only meaningful once the label has a parent and a
        ' font applied, so the subtitle is placed from the title's real height
        ' here rather than from a hard-coded y.
        lblTitleSub.Top = lblTitle.Top + lblTitle.PreferredHeight + 2
        picBackF.Top = lblTitle.Top + (lblTitle.PreferredHeight - picBackF.Height) \ 2

        ' ── Body ──────────────────────────────────────────────────────────────
        ' AutoScroll stays on as a safety net for a small or scaled display, but
        ' the layout below is sized so it never actually engages at the normal
        ' window size: a registration form the user has to scroll to finish is
        ' a form where the Save button is easy to miss.
        Dim body As New Panel With {
            .Dock = DockStyle.Fill, .BackColor = MintBg, .AutoScroll = True
        }

        ' THREE columns rather than two. The form carries fifteen inputs once
        ' registration is in, and at two columns that stacked to ~1100px — well
        ' past the ~730px of body a maximised window leaves, so every visit
        ' began with a scroll. Three columns fold the same fields into six rows
        ' and bring the whole form, Save button included, onto one screen.
        ' The card paints itself inset by ShadowPad with a drop shadow around
        ' it, so the panel is that much wider than the content and its
        ' background is the PAGE colour — a white panel would show as a square
        ' behind the shadow instead of letting it fall on the mint.
        Dim contentW As Integer = 1040
        Dim cardWidth As Integer = contentW + PanelRenderer.ShadowPad * 2
        Dim card As New BufferedPanel With {.Width = cardWidth, .BackColor = MintBg}
        AddHandler card.Paint, Sub(s, e) PaintElevatedCard(CType(s, Panel), e)

        ' Seeded from the row being edited, so opening Edit and saving without
        ' touching the photo keeps it rather than clearing it.
        pickedImagePath = If(existing IsNot Nothing, If(existing.ImagePath, ""), "")

        ' Column geometry: three equal fields with a gutter between each.
        ' Every x starts at ShadowPad rather than 0, because the painted card
        ' is inset by that much — content laid out from 0 would sit on the
        ' shadow, outside the white surface.
        Dim inset As Integer = PanelRenderer.ShadowPad
        Dim padX As Integer = inset + 30
        Dim gutter As Integer = 18
        Dim colW As Integer = (contentW - 30 * 2 - gutter * 2) \ 3
        Dim colRX As Integer = padX + colW + gutter
        Dim col3X As Integer = padX + (colW + gutter) * 2

        ' Vertical rhythm. A field is 66px tall (22 label + 44 tile), so ROW is
        ' that plus a 4px gap. Header and divider are tighter than the old
        ' values for the same reason the columns changed: to buy back height.
        Dim ROW As Integer = 70
        Dim yy As Integer = inset + 20

        ' ── Section: Identity ────────────────────────────────────────
        ' The logo sits beside the name rather than above the whole form: the
        ' two together are what identifies the supplier, so they read as one
        ' block instead of the photo looking like a separate step.
        yy = AddSectionHeader(card, "Identity", "Who the supplier is.", yy, padX)

        Dim logoSize As Integer = 96
        AddLogoPicker(card, If(existing IsNot Nothing, existing.Name, ""), padX, yy, logoSize)

        ' The two names share the space beside the logo, stacked, because the
        ' trading name and the registered name are read as a pair.
        Dim idX As Integer = padX + logoSize + 22
        Dim idW As Integer = cardWidth - idX - padX
        txtName = AddFormField(card, "Supplier Name *", idX, yy, idW,
                               If(existing IsNot Nothing, existing.Name, ""),
                               False, "e.g. ABC Paint Supplies")

        If SupplierLogo.HasRegistration Then
            txtCompany = AddFormField(card, "Company Name", idX, yy + ROW, idW,
                                      If(existing IsNot Nothing, existing.CompanyName, ""),
                                      False, "Registered business name on the DTI/SEC papers")
        Else
            txtCompany = Nothing
        End If

        ' Whichever is taller — the logo tile or the stacked names — ends the row.
        yy += Math.Max(logoSize, If(SupplierLogo.HasRegistration, ROW * 2 - 4, 66)) + 16
        yy = AddSectionDivider(card, yy, padX)

        ' ── Section: Contact ─────────────────────────────────────────
        yy = AddSectionHeader(card, "Contact", "How to reach them.", yy, padX)

        ' Head of Company sits directly beside Contact Person so the difference
        ' between "who signs" and "who you ring" is visible rather than inferred.
        txtContact = AddFormField(card, "Contact Person *", padX, yy, colW,
                                  If(existing IsNot Nothing, existing.Contact, ""),
                                  False, "e.g. Juan dela Cruz")
        If SupplierLogo.HasRegistration Then
            txtHead = AddFormField(card, "Head of Company", colRX, yy, colW,
                                   If(existing IsNot Nothing, existing.HeadName, ""),
                                   False, "Owner / President / signatory")
        Else
            txtHead = Nothing
        End If
        txtPhone = AddFormField(card, "Phone *", col3X, yy, colW,
                                If(existing IsNot Nothing, existing.Phone, ""),
                                False, "e.g. 09171234567")
        yy += ROW

        txtEmail = AddFormField(card, "Email *", padX, yy, colW,
                                If(existing IsNot Nothing, existing.Email, ""),
                                False, "e.g. supplier@gmail.com")
        txtAddress = AddFormField(card, "Address *", colRX, yy, colW,
                                  If(existing IsNot Nothing, existing.Address, ""),
                                  False, "e.g. 123 Rizal St., Aparri")
        ' Payment terms rides the third column here rather than opening a
        ' section of its own for a single dropdown.
        If SupplierLogo.HasCommercial Then
            cboTerms = AddFormCombo(card, "Payment Terms", col3X, yy, colW,
                                    SupplierLogo.PaymentTermOptions,
                                    If(existing IsNot Nothing, existing.PaymentTerms, ""),
                                    "Not specified")
        Else
            cboTerms = Nothing
        End If
        yy += ROW

        ' ── Section: Business Registration ───────────────────────────────
        ' TIN, DTI and BIR are three different documents an audit asks for by
        ' name, and at three columns they land on exactly one row together.
        If SupplierLogo.HasRegistration Then
            yy = AddSectionDivider(card, yy, padX)
            yy = AddSectionHeader(card, "Business Registration",
                                  "As printed on the certificates.", yy, padX)

            txtTin = AddFormField(card, "TIN", padX, yy, colW,
                                  If(existing IsNot Nothing, existing.Tin, ""),
                                  False, "e.g. 123-456-789-000")
            txtDti = AddFormField(card, "DTI Registration No.", colRX, yy, colW,
                                  If(existing IsNot Nothing, existing.DtiNo, ""),
                                  False, "DTI / SEC certificate no.")
            txtBir = AddFormField(card, "BIR Registration No.", col3X, yy, colW,
                                  If(existing IsNot Nothing, existing.BirNo, ""),
                                  False, "BIR Form 2303 no.")
            yy += ROW
        ElseIf SupplierLogo.HasCommercial Then
            ' Un-migrated for registration but migrated for TIN: keep TIN
            ' offered rather than losing a field that already works.
            yy = AddSectionDivider(card, yy, padX)
            yy = AddSectionHeader(card, "Business Registration",
                                  "As printed on the certificates.", yy, padX)
            txtTin = AddFormField(card, "TIN", padX, yy, colW,
                                  If(existing IsNot Nothing, existing.Tin, ""),
                                  False, "e.g. 123-456-789-000")
            yy += ROW
            txtDti = Nothing
            txtBir = Nothing
        Else
            txtTin = Nothing
            txtDti = Nothing
            txtBir = Nothing
        End If

        ' ── Notes + Status ──────────────────────────────────────────
        ' Notes spans the first two columns and Status takes the third, so the
        ' free-text box gets the room it needs without costing another row.
        If SupplierLogo.HasCommercial Then
            txtNotes = AddFormField(card, "Notes", padX, yy, colW * 2 + gutter,
                                    If(existing IsNot Nothing, existing.Notes, ""),
                                    False, "Delivery quirks, minimum order, best time to call…")
            cboStatus = AddFormCombo(card, "Status", col3X, yy, colW,
                                     New String() {"Active", "Inactive"},
                                     If(existing Is Nothing OrElse existing.IsActive, "Active", "Inactive"),
                                     "")
            yy += ROW
        Else
            cboStatus = Nothing
            txtNotes = Nothing
        End If

        ' ── Button bar ────────────────────────────────────────────────────────
        Dim btnBarH As Integer = 64
        Dim btnBarY As Integer = yy + 2

        ' Spans the card's white surface, not the whole panel: the outer
        ' ShadowPad ring is page colour, so a transparent bar stretched across
        ' it would pick up mint at both ends.
        Dim btnBar As New Panel With {
            .BackColor = CardBg, .Location = New Point(inset, btnBarY),
            .Height = btnBarH, .Width = contentW
        }
        AddHandler btnBar.Paint,
            Sub(s, e)
                Using pen As New Pen(CardBorder, 1)
                    e.Graphics.DrawLine(pen, 30, 0, CType(s, Panel).Width - 30, 0)
                End Using
            End Sub

        Dim lblRequired As New Label With {
            .Text = "* Required", .Font = New Font("Segoe UI", 9),
            .ForeColor = FaintText, .AutoSize = True, .BackColor = Color.Transparent,
            .Location = New Point(30, (btnBarH - 18) \ 2)
        }

        Dim btnCancel As New Button With {
            .Text = "Cancel", .Size = New Size(112, 40),
            .FlatStyle = FlatStyle.Flat, .BackColor = Color.White,
            .ForeColor = SubText, .Font = New Font("Segoe UI Semibold", 10), .Cursor = Cursors.Hand
        }
        btnCancel.FlatAppearance.BorderSize = 0
        AddHandler btnCancel.Paint, Sub(s, e) PaintOutlineButton(CType(s, Button), e, CardBorder)
        AddHandler btnCancel.MouseEnter,
            Sub()
                btnCancel.BackColor = MintBg
                btnCancel.Invalidate()
            End Sub
        AddHandler btnCancel.MouseLeave,
            Sub()
                btnCancel.BackColor = Color.White
                btnCancel.Invalidate()
            End Sub
        AddHandler btnCancel.Click, Sub() If editingId > 0 Then LoadDetail(editingId) Else RenderList()

        btnSaveRef = New Button With {
            .Text = If(supplierId > 0, "Save Changes", "Add Supplier"),
            .Size = New Size(152, 40), .FlatStyle = FlatStyle.Flat,
            .BackColor = MintPrimary, .ForeColor = Color.White,
            .Font = New Font("Segoe UI Semibold", 10), .Cursor = Cursors.Hand, .Tag = ""
        }
        btnSaveRef.FlatAppearance.BorderSize = 0
        AddHandler btnSaveRef.Paint, Sub(s, e) PaintFilledButton(CType(s, Button), e)
        AddHandler btnSaveRef.MouseEnter,
            Sub()
                btnSaveRef.BackColor = MintDeep
                btnSaveRef.Invalidate()
            End Sub
        AddHandler btnSaveRef.MouseLeave,
            Sub()
                btnSaveRef.BackColor = MintPrimary
                btnSaveRef.Invalidate()
            End Sub
        AddHandler btnSaveRef.Click, Sub() CommitSupplier()

        btnBar.Controls.AddRange({lblRequired, btnCancel, btnSaveRef})

        Dim PositionButtons As Action =
            Sub()
                Dim t As Integer = (btnBar.Height - btnSaveRef.Height) \ 2
                btnSaveRef.Top = t
                btnSaveRef.Left = btnBar.Width - 30 - btnSaveRef.Width
                btnCancel.Top = t
                btnCancel.Left = btnSaveRef.Left - btnCancel.Width - 12
            End Sub
        AddHandler btnBar.Resize, Sub() PositionButtons()
        PositionButtons()

        ' + inset so the bottom shadow has room; the top inset is already
        ' inside btnBarY, which started from yy = inset + 20.
        card.Height = btnBarY + btnBarH + 10 + inset
        card.Controls.Add(btnBar)

        ' Centring has to work on the VISIBLE white surface, not on the panel.
        ' The panel is ShadowPad larger on every side and PanelRenderer lifts
        ' its painted rect 2px, so centring the panel left the white card
        ' sitting ~29px low — which is what read as "too far down".
        Dim visibleTopPad As Integer = inset - 2

        ' The card hangs from the title rather than floating in the middle of
        ' the body. On a tall screen there is far more body than card, and
        ' true centring then strands it low with a large void under the
        ' header — the eye anchors to the title, so that void reads as the
        ' card being pushed down even when it is geometrically centred.
        '
        ' So: centre it, but never let the gap above exceed MaxTopGap.
        '
        ' 90 is tuned, not arbitrary. True centring on a 1080px screen puts the
        ' gap at ~156 and the card reads as sunk; clamping hard to 28 pins it
        ' under the subtitle and reads as top-heavy. 90 sits between the two —
        ' clearly below the header, still visibly settled on the page.
        Dim MaxTopGap As Integer = 90

        Dim PositionCard As Action =
            Sub()
                Dim bw As Integer = body.ClientSize.Width
                Dim bh As Integer = body.ClientSize.Height
                ' Centred while the window is wide enough, pinned to a 20px
                ' margin once it is not. Without the clamp a narrow window put
                ' the card at a negative x, hiding the first column off-screen
                ' behind the nav rather than scrolling to it.
                card.Left = Math.Max(20, (bw - card.Width) \ 2)

                ' Solve for the panel top that places the VISIBLE white
                ' surface, which starts visibleTopPad into the panel and is
                ' inset*2 shorter than it.
                Dim visH As Integer = card.Height - inset * 2
                Dim wantVisTop As Integer = Math.Min((bh - visH) \ 2, MaxTopGap)
                card.Top = Math.Max(-visibleTopPad, wantVisTop - visibleTopPad)
            End Sub

        AddHandler body.Resize, Sub() PositionCard()
        body.Controls.Add(card)

        container.Controls.Add(body)
        container.Controls.Add(header)
        container.ResumeLayout()

        body.BeginInvoke(New Action(Sub() PositionCard()))
    End Sub

    ' ── SECTION HEADER ───────────────────────────────────────────────────────
    ' A titled band that groups the fields under it. Returns the y the first
    ' field of the section starts at.
    Private Shared Function AddSectionHeader(parent As Panel, title As String,
                                             caption As String, y As Integer,
                                             padX As Integer) As Integer
        Dim lblTitle As New Label With {
            .Text = title.ToUpperInvariant(),
            .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold),
            .ForeColor = MintDeep, .AutoSize = True,
            .Location = New Point(padX, y), .BackColor = Color.Transparent
        }
        Dim lblCaption As New Label With {
            .Text = caption, .Font = New Font("Segoe UI", 9),
            .ForeColor = FaintText, .AutoSize = True, .BackColor = Color.Transparent,
            .Location = New Point(padX, y + 17)
        }
        parent.Controls.AddRange({lblTitle, lblCaption})
        Return y + 36
    End Function

    ' A hairline between two sections.
    Private Shared Function AddSectionDivider(parent As Panel, y As Integer,
                                              padX As Integer) As Integer
        Dim line As New Panel With {
            .Height = 1, .BackColor = Color.FromArgb(238, 243, 241),
            .Location = New Point(padX, y),
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        }
        parent.Controls.Add(line)
        ' padX already carries any shadow inset, so mirroring it on the right
        ' keeps the rule inside the card's white surface at any width.
        AddHandler parent.Resize, Sub() line.Width = parent.Width - padX * 2
        line.Width = Math.Max(200, parent.Width - padX * 2)
        Return y + 18
    End Function

    ' ── LOGO PICKER ──────────────────────────────────────────────────────────
    ' A single click-target: the tile IS the button. An "Upload Photo" button
    ' beside it said the same thing twice, so the tile carries the whole
    ' interaction — click to choose, hover to see it offer, and once a photo is
    ' set a small × in the corner clears it.
    Private Shared Sub AddLogoPicker(parent As Panel, supplierName As String,
                                     x As Integer, y As Integer, tileSize As Integer)
        Dim initial As String = SupplierLogo.InitialOf(supplierName)

        Dim lbl As New Label With {
            .Text = "Logo", .Font = New Font("Segoe UI Semibold", 9.5F),
            .ForeColor = SubText, .AutoSize = True,
            .Location = New Point(x, y), .BackColor = Color.Transparent
        }
        parent.Controls.Add(lbl)

        Dim tileY As Integer = y + 22
        Dim tile As New BufferedPanel With {
            .Size = New Size(tileSize, tileSize),
            .Location = New Point(x, tileY),
            .BackColor = CardBg, .Cursor = Cursors.Hand
        }

        Dim hot As Boolean = False
        Dim clearRect As New Rectangle(tileSize - 26, 4, 22, 22)

        AddHandler tile.Paint,
            Sub(s2, e2)
                Dim g As Graphics = e2.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim box As New Rectangle(0, 0, tileSize - 1, tileSize - 1)
                Dim hasPhoto As Boolean = (pickedImagePath <> "") AndAlso
                                          SupplierLogo.GetThumb(pickedImagePath, box.Width) IsNot Nothing

                If hasPhoto Then
                    SupplierLogo.Paint(g, box, 16, pickedImagePath, initial, 34.0F)
                    ' Hovering dims the photo and says what a click does.
                    If hot Then
                        Using path As GraphicsPath = RoundRect(box, 16)
                            Using br As New SolidBrush(Color.FromArgb(140, 10, 79, 66))
                                g.FillPath(br, path)
                            End Using
                        End Using
                        Using f As New Font("Segoe UI Semibold", 9.0F),
                              br As New SolidBrush(Color.White)
                            Dim t As String = "Change"
                            Dim sz As SizeF = g.MeasureString(t, f)
                            g.DrawString(t, f, br, (tileSize - sz.Width) / 2.0F,
                                         (tileSize - sz.Height) / 2.0F)
                        End Using
                    End If
                    ' Clear badge, top-right.
                    Using br As New SolidBrush(Color.FromArgb(215, 255, 255, 255))
                        g.FillEllipse(br, clearRect)
                    End Using
                    Using pen As New Pen(Color.FromArgb(60, 0, 0, 0), 1.0F)
                        g.DrawEllipse(pen, clearRect)
                    End Using
                    Using pen As New Pen(NegativeClr, 1.8F)
                        pen.StartCap = LineCap.Round
                        pen.EndCap = LineCap.Round
                        Dim cx As Single = clearRect.X + clearRect.Width / 2.0F
                        Dim cy As Single = clearRect.Y + clearRect.Height / 2.0F
                        g.DrawLine(pen, cx - 4, cy - 4, cx + 4, cy + 4)
                        g.DrawLine(pen, cx + 4, cy - 4, cx - 4, cy + 4)
                    End Using
                Else
                    ' Empty state: a dashed drop-tile, so it reads as
                    ' "something goes here" rather than as a photo already set.
                    Using path As GraphicsPath = RoundRect(box, 16)
                        Using br As New SolidBrush(If(hot, Color.FromArgb(224, 244, 238), TileBg))
                            g.FillPath(br, path)
                        End Using
                        Using pen As New Pen(If(hot, MintPrimary, Color.FromArgb(190, 210, 204)), 1.4F)
                            pen.DashStyle = DashStyle.Dash
                            g.DrawPath(pen, path)
                        End Using
                    End Using
                    Using pen As New Pen(MintDeep, 2.0F)
                        pen.StartCap = LineCap.Round
                        pen.EndCap = LineCap.Round
                        Dim cx As Single = tileSize / 2.0F
                        Dim cy As Single = tileSize / 2.0F - 11
                        g.DrawLine(pen, cx - 9, cy, cx + 9, cy)
                        g.DrawLine(pen, cx, cy - 9, cx, cy + 9)
                    End Using
                    Using f As New Font("Segoe UI Semibold", 8.5F),
                          br As New SolidBrush(MintDeep)
                        Dim t As String = "Add photo"
                        Dim sz As SizeF = g.MeasureString(t, f)
                        g.DrawString(t, f, br, (tileSize - sz.Width) / 2.0F, tileSize / 2.0F + 10)
                    End Using
                End If
            End Sub

        Dim browse As Action =
            Sub()
                Using ofd As New OpenFileDialog With {
                    .Title = "Choose supplier logo",
                    .Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*"
                }
                    If ofd.ShowDialog() = DialogResult.OK Then
                        Dim stored As String = SupplierLogo.Store(ofd.FileName)
                        If stored <> "" Then
                            pickedImagePath = stored
                            tile.Invalidate()
                        End If
                    End If
                End Using
            End Sub

        AddHandler tile.MouseClick,
            Sub(s2, e2)
                Dim me_ As MouseEventArgs = CType(e2, MouseEventArgs)
                ' The clear badge only exists while a photo is set.
                If pickedImagePath <> "" AndAlso clearRect.Contains(me_.Location) Then
                    ' Clears the row's photo on save. The imported file is left
                    ' in SupplierImages rather than deleted, because another row
                    ' saved earlier may still point at it.
                    pickedImagePath = ""
                    tile.Invalidate()
                Else
                    browse()
                End If
            End Sub
        AddHandler tile.MouseEnter,
            Sub()
                hot = True
                tile.Invalidate()
            End Sub
        AddHandler tile.MouseLeave,
            Sub()
                hot = False
                tile.Invalidate()
            End Sub

        parent.Controls.Add(tile)

        Dim lblHint As New Label With {
            .Text = "PNG or JPG", .Font = New Font("Segoe UI", 8),
            .ForeColor = FaintText, .AutoSize = True, .BackColor = Color.Transparent,
            .Location = New Point(x, tileY + tileSize + 6)
        }
        parent.Controls.Add(lblHint)
    End Sub

    ' ── TEXT FIELD ───────────────────────────────────────────────────────────
    ' Absolutely positioned so two can share a row. Width is fixed by the
    ' caller rather than anchored to the card, which is what lets the form be
    ' two columns.
    Private Shared Function AddFormField(parent As Panel, label As String,
                                         x As Integer, y As Integer, width As Integer,
                                         value As String,
                                         Optional multiline As Boolean = False,
                                         Optional placeholder As String = "") As TextBox
        Dim lbl As New Label With {
            .Text = label, .Font = New Font("Segoe UI Semibold", 9.5F),
            .ForeColor = SubText, .AutoSize = True,
            .Location = New Point(x, y), .BackColor = Color.Transparent
        }

        Dim tileH As Integer = If(multiline, 68, 44)
        Dim tile As New Panel With {
            .Location = New Point(x, y + 22), .Size = New Size(width, tileH),
            .BackColor = CardBg
        }

        Dim txt As New TextBox With {
            .BorderStyle = BorderStyle.None, .BackColor = CardBg,
            .ForeColor = MintDark, .Font = New Font("Segoe UI", 10.5F),
            .Text = If(value, ""),
            .Location = New Point(12, If(multiline, 10, (tileH - 20) \ 2)),
            .Width = width - 24,
            .Multiline = multiline
        }
        If multiline Then txt.Height = tileH - 20
        tile.Controls.Add(txt)

        If placeholder <> "" Then
            Dim phLabel As New Label With {
                .Text = placeholder, .Font = New Font("Segoe UI", 10, FontStyle.Italic),
                .ForeColor = Color.FromArgb(180, 190, 187), .BackColor = Color.Transparent,
                .AutoSize = False, .Size = New Size(width - 26, If(multiline, 22, tileH)),
                .Location = New Point(13, If(multiline, 9, 0)),
                .Cursor = Cursors.IBeam,
                .TextAlign = If(multiline, ContentAlignment.TopLeft, ContentAlignment.MiddleLeft)
            }
            tile.Controls.Add(phLabel)
            phLabel.BringToFront()

            Dim UpdatePh As Action = Sub() phLabel.Visible = (txt.Text = "" AndAlso Not txt.Focused)
            AddHandler txt.TextChanged, Sub() UpdatePh()
            AddHandler txt.Enter, Sub() UpdatePh()
            AddHandler txt.Leave, Sub() UpdatePh()
            AddHandler phLabel.Click, Sub() txt.Focus()
            UpdatePh()
        End If

        AddHandler tile.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect = tile.ClientRectangle
                rect.Inflate(-1, -1)
                Using path As GraphicsPath = RoundRect(rect, 10)
                    Using fill As New SolidBrush(CardBg)
                        e.Graphics.FillPath(fill, path)
                    End Using
                    Using pen As New Pen(If(txt.Focused, MintPrimary, Color.FromArgb(214, 224, 220)),
                                         If(txt.Focused, 1.8F, 1.2F))
                        e.Graphics.DrawPath(pen, path)
                    End Using
                End Using
            End Sub

        AddHandler txt.Enter, Sub() tile.Invalidate()
        AddHandler txt.Leave, Sub() tile.Invalidate()

        parent.Controls.AddRange({lbl, tile})
        Return txt
    End Function

    ' ── DROPDOWN FIELD ───────────────────────────────────────────────────────
    ' Same tile as AddFormField so the two line up on a shared row. The combo is
    ' owner-drawn flat, because a stock ComboBox paints a raised 3D border that
    ' would not match anything else on this form.
    Private Shared Function AddFormCombo(parent As Panel, label As String,
                                         x As Integer, y As Integer, width As Integer,
                                         items As String(), value As String,
                                         emptyText As String) As ComboBox
        Dim lbl As New Label With {
            .Text = label, .Font = New Font("Segoe UI Semibold", 9.5F),
            .ForeColor = SubText, .AutoSize = True,
            .Location = New Point(x, y), .BackColor = Color.Transparent
        }

        Dim tileH As Integer = 44
        Dim tile As New Panel With {
            .Location = New Point(x, y + 22), .Size = New Size(width, tileH),
            .BackColor = CardBg
        }

        Dim cbo As New ComboBox With {
            .DropDownStyle = ComboBoxStyle.DropDownList,
            .FlatStyle = FlatStyle.Flat,
            .Font = New Font("Segoe UI", 10.5F),
            .BackColor = CardBg, .ForeColor = MintDark,
            .Location = New Point(10, (tileH - 24) \ 2),
            .Width = width - 20,
            .DrawMode = DrawMode.OwnerDrawFixed, .ItemHeight = 22
        }

        ' A blank first entry is how "not specified" is chosen; without it a
        ' dropdown-list combo cannot be returned to empty.
        If emptyText <> "" Then cbo.Items.Add(emptyText)
        cbo.Items.AddRange(items)

        ' An older row may hold a value this list does not offer; keep it rather
        ' than silently rewriting the supplier's terms on the next save.
        If value <> "" AndAlso Not cbo.Items.Contains(value) Then cbo.Items.Add(value)
        If value <> "" Then
            cbo.SelectedItem = value
        ElseIf cbo.Items.Count > 0 Then
            cbo.SelectedIndex = 0
        End If

        AddHandler cbo.DrawItem,
            Sub(s, e)
                If e.Index < 0 Then Return
                e.DrawBackground()
                Dim text As String = cbo.Items(e.Index).ToString()
                Dim isEmptyRow As Boolean = (emptyText <> "" AndAlso e.Index = 0)
                Using br As New SolidBrush(If(isEmptyRow, FaintText, MintDark))
                    Dim sf As New StringFormat With {.LineAlignment = StringAlignment.Center}
                    e.Graphics.DrawString(text, cbo.Font, br,
                        New RectangleF(e.Bounds.X + 2, e.Bounds.Y, e.Bounds.Width, e.Bounds.Height), sf)
                End Using
            End Sub

        tile.Controls.Add(cbo)

        AddHandler tile.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect = tile.ClientRectangle
                rect.Inflate(-1, -1)
                Using path As GraphicsPath = RoundRect(rect, 10)
                    Using fill As New SolidBrush(CardBg)
                        e.Graphics.FillPath(fill, path)
                    End Using
                    Using pen As New Pen(If(cbo.Focused, MintPrimary, Color.FromArgb(214, 224, 220)),
                                         If(cbo.Focused, 1.8F, 1.2F))
                        e.Graphics.DrawPath(pen, path)
                    End Using
                End Using
                ' Chevron, drawn over the combo's own flat button area.
                Using pen As New Pen(SubText, 1.6F)
                    pen.StartCap = LineCap.Round
                    pen.EndCap = LineCap.Round
                    pen.LineJoin = LineJoin.Round
                    Dim cx As Single = tile.Width - 20
                    Dim cy As Single = tile.Height / 2.0F - 1
                    e.Graphics.DrawLines(pen, New PointF() {
                        New PointF(cx - 5, cy - 2),
                        New PointF(cx, cy + 3),
                        New PointF(cx + 5, cy - 2)
                    })
                End Using
            End Sub

        AddHandler cbo.Enter, Sub() tile.Invalidate()
        AddHandler cbo.Leave, Sub() tile.Invalidate()

        parent.Controls.AddRange({lbl, tile})
        Return cbo
    End Function

    Private Shared Sub CommitSupplier()
        If btnSaveRef IsNot Nothing AndAlso CStr(If(btnSaveRef.Tag, "")) = "done" Then Return

        Dim name As String = txtName.Text.Trim()
        Dim contact As String = txtContact.Text.Trim()
        Dim phone As String = txtPhone.Text.Trim()
        Dim email As String = txtEmail.Text.Trim()
        Dim address As String = txtAddress.Text.Trim()

        ' The commercial controls only exist once their columns do.
        Dim tin As String = If(txtTin IsNot Nothing, txtTin.Text.Trim(), "")
        Dim notes As String = If(txtNotes IsNot Nothing, txtNotes.Text.Trim(), "")

        ' Registration fields, likewise only present once their columns are.
        Dim companyName As String = If(txtCompany IsNot Nothing, txtCompany.Text.Trim(), "")
        Dim headName As String = If(txtHead IsNot Nothing, txtHead.Text.Trim(), "")
        Dim dtiNo As String = If(txtDti IsNot Nothing, txtDti.Text.Trim(), "")
        Dim birNo As String = If(txtBir IsNot Nothing, txtBir.Text.Trim(), "")
        Dim terms As String = ""
        If cboTerms IsNot Nothing AndAlso cboTerms.SelectedIndex > 0 Then
            terms = CStr(cboTerms.SelectedItem)
        End If
        ' Status defaults to active for a brand-new supplier, which is what the
        ' INSERT did before the field was offered at all.
        Dim isActive As Boolean = True
        If cboStatus IsNot Nothing Then
            isActive = (CStr(If(cboStatus.SelectedItem, "Active")) = "Active")
        End If

        If name = "" Then
            AppModal.Show("Supplier Name is required.", "Missing Field",
                          MessageBoxButtons.OK, MessageBoxIcon.Warning)
            txtName.Focus() : Return
        End If
        If contact = "" Then
            AppModal.Show("Contact Person is required.", "Missing Field",
                          MessageBoxButtons.OK, MessageBoxIcon.Warning)
            txtContact.Focus() : Return
        End If
        If phone = "" Then
            AppModal.Show("Phone is required.", "Missing Field",
                          MessageBoxButtons.OK, MessageBoxIcon.Warning)
            txtPhone.Focus() : Return
        End If
        If Not System.Text.RegularExpressions.Regex.IsMatch(phone, "^[0-9\+\s\-]+$") Then
            AppModal.Show("Phone must contain digits only (e.g. 09171234567).", "Invalid Phone",
                          MessageBoxButtons.OK, MessageBoxIcon.Warning)
            txtPhone.Focus() : Return
        End If
        If email = "" Then
            AppModal.Show("Email is required.", "Missing Field",
                          MessageBoxButtons.OK, MessageBoxIcon.Warning)
            txtEmail.Focus() : Return
        End If
        If Not System.Text.RegularExpressions.Regex.IsMatch(email, "^[^@\s]+@[^@\s]+\.[^@\s]+$") Then
            AppModal.Show("Email must be a valid address (e.g. supplier@gmail.com).", "Invalid Email",
                          MessageBoxButtons.OK, MessageBoxIcon.Warning)
            txtEmail.Focus() : Return
        End If
        If address = "" Then
            AppModal.Show("Address is required.", "Missing Field",
                          MessageBoxButtons.OK, MessageBoxIcon.Warning)
            txtAddress.Focus() : Return
        End If
        ' TIN is optional, but a typed one has to look like a TIN — a wrong
        ' number on an invoice is worse than none at all.
        If tin <> "" AndAlso Not System.Text.RegularExpressions.Regex.IsMatch(tin, "^[0-9\-\s]+$") Then
            AppModal.Show("TIN must contain digits only (e.g. 123-456-789-000).", "Invalid TIN",
                          MessageBoxButtons.OK, MessageBoxIcon.Warning)
            txtTin.Focus() : Return
        End If

        ' DTI and BIR are optional, but a typed one must look like a
        ' certificate number. They are alphanumeric — a DTI number can carry a
        ' branch letter — so this rejects punctuation and stray symbols rather
        ' than demanding digits the way TIN does.
        If dtiNo <> "" AndAlso Not System.Text.RegularExpressions.Regex.IsMatch(dtiNo, "^[A-Za-z0-9\-\s]+$") Then
            AppModal.Show("DTI Registration No. may contain letters, digits and dashes only.",
                          "Invalid DTI Number", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            txtDti.Focus() : Return
        End If
        If birNo <> "" AndAlso Not System.Text.RegularExpressions.Regex.IsMatch(birNo, "^[A-Za-z0-9\-\s]+$") Then
            AppModal.Show("BIR Registration No. may contain letters, digits and dashes only.",
                          "Invalid BIR Number", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            txtBir.Focus() : Return
        End If

        btnSaveRef.Tag = "done"
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                ' The logo column is optional; write it only where it exists,
                ' so an un-migrated database saves everything else as before.
                If editingId > 0 Then
                    Using cmd As New MySqlCommand(
                        "UPDATE suppliers SET name = @n, contact_person = @c, phone = @p, " &
                        "email = @e, address = @a, is_active = @act" &
                        If(SupplierLogo.HasColumn, ", image_path = @img", "") &
                        If(SupplierLogo.HasCommercial,
                           ", tin = @tin, payment_terms = @terms, notes = @notes", "") &
                        If(SupplierLogo.HasRegistration,
                           ", company_name = @company, head_name = @head, " &
                           "dti_no = @dti, bir_no = @bir", "") &
                        " WHERE supplier_id = @id", conn)
                        cmd.Parameters.AddWithValue("@n", name)
                        cmd.Parameters.AddWithValue("@c", contact)
                        cmd.Parameters.AddWithValue("@p", phone)
                        cmd.Parameters.AddWithValue("@e", email)
                        cmd.Parameters.AddWithValue("@a", address)
                        cmd.Parameters.AddWithValue("@act", If(isActive, 1, 0))
                        ' Empty means the user removed the photo — store NULL,
                        ' not "", so the column reads the same as never-set.
                        If SupplierLogo.HasColumn Then
                            cmd.Parameters.AddWithValue("@img",
                                If(pickedImagePath = "", CObj(DBNull.Value), CObj(pickedImagePath)))
                        End If
                        If SupplierLogo.HasCommercial Then
                            cmd.Parameters.AddWithValue("@tin", NullIfBlank(tin))
                            cmd.Parameters.AddWithValue("@terms", NullIfBlank(terms))
                            cmd.Parameters.AddWithValue("@notes", NullIfBlank(notes))
                        End If
                        If SupplierLogo.HasRegistration Then
                            cmd.Parameters.AddWithValue("@company", NullIfBlank(companyName))
                            cmd.Parameters.AddWithValue("@head", NullIfBlank(headName))
                            cmd.Parameters.AddWithValue("@dti", NullIfBlank(dtiNo))
                            cmd.Parameters.AddWithValue("@bir", NullIfBlank(birNo))
                        End If
                        cmd.Parameters.AddWithValue("@id", editingId)
                        cmd.ExecuteNonQuery()
                    End Using
                Else
                    Using cmd As New MySqlCommand(
                        "INSERT INTO suppliers (name, contact_person, phone, email, address" &
                        If(SupplierLogo.HasColumn, ", image_path", "") &
                        If(SupplierLogo.HasCommercial, ", tin, payment_terms, notes", "") &
                        If(SupplierLogo.HasRegistration,
                           ", company_name, head_name, dti_no, bir_no", "") &
                        ", is_active) VALUES (@n, @c, @p, @e, @a" &
                        If(SupplierLogo.HasColumn, ", @img", "") &
                        If(SupplierLogo.HasCommercial, ", @tin, @terms, @notes", "") &
                        If(SupplierLogo.HasRegistration, ", @company, @head, @dti, @bir", "") &
                        ", @act)", conn)
                        cmd.Parameters.AddWithValue("@n", name)
                        cmd.Parameters.AddWithValue("@c", contact)
                        cmd.Parameters.AddWithValue("@p", phone)
                        cmd.Parameters.AddWithValue("@e", email)
                        cmd.Parameters.AddWithValue("@a", address)
                        cmd.Parameters.AddWithValue("@act", If(isActive, 1, 0))
                        If SupplierLogo.HasColumn Then
                            cmd.Parameters.AddWithValue("@img",
                                If(pickedImagePath = "", CObj(DBNull.Value), CObj(pickedImagePath)))
                        End If
                        If SupplierLogo.HasCommercial Then
                            cmd.Parameters.AddWithValue("@tin", NullIfBlank(tin))
                            cmd.Parameters.AddWithValue("@terms", NullIfBlank(terms))
                            cmd.Parameters.AddWithValue("@notes", NullIfBlank(notes))
                        End If
                        If SupplierLogo.HasRegistration Then
                            cmd.Parameters.AddWithValue("@company", NullIfBlank(companyName))
                            cmd.Parameters.AddWithValue("@head", NullIfBlank(headName))
                            cmd.Parameters.AddWithValue("@dti", NullIfBlank(dtiNo))
                            cmd.Parameters.AddWithValue("@bir", NullIfBlank(birNo))
                        End If
                        cmd.ExecuteNonQuery()
                    End Using
                End If
            End Using
        Catch ex As Exception
            If btnSaveRef IsNot Nothing Then btnSaveRef.Tag = ""
            AppModal.Show("Could not save the supplier:" & Environment.NewLine & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try

        Dim savedId As Integer = editingId
        ' Suppliers are master data behind every purchase order, so changing
        ' one is recorded the same way categories and products already are.
        ActivityLog.Record(If(editingId > 0, "SUPPLIER_EDIT", "SUPPLIER_ADD"),
                           "supplier", If(editingId > 0, CType(editingId, Integer?), Nothing),
                           name)

        AppModal.Show(If(editingId > 0, "Supplier updated.", "Supplier added."),
                      "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information)
        If savedId > 0 Then LoadDetail(savedId) Else RenderList()
    End Sub

    ' =========================================================================
    '  VIEW 3 — DETAIL
    ' =========================================================================
    Private Shared Sub LoadDetail(supplierId As Integer)
        Dim container As Panel = containerRef
        If container Is Nothing Then Return

        ' Same reason as RenderList: this is where an edit lands after saving,
        ' so the logo on screen has to be re-read rather than served from the
        ' bitmaps the previous view scaled.
        SupplierLogo.ClearCache(container)

        Dim sup As SupplierRow = LoadOneSupplier(supplierId)
        If sup Is Nothing Then
            AppModal.Show("That supplier could not be found.", "Not Found",
                          MessageBoxButtons.OK, MessageBoxIcon.Warning)
            RenderList()
            Return
        End If
        Dim products As List(Of String) = LoadSupplierProducts(supplierId)
        Dim history As List(Of POHistRow) = LoadSupplierPOs(supplierId)

        container.SuspendLayout()
        container.Controls.Clear()
        container.BackColor = MintBg

        Dim body As New Panel With {
            .Dock = DockStyle.Fill, .BackColor = MintBg,
            .Padding = New Padding(24, 4, 24, 20), .AutoScroll = True
        }

        ' ── PO history card ───────────────────────────────────────────────────
        Dim histHeight As Integer = 68 + Math.Max(1, history.Count) * 62 + 12
        Dim histCard As New BufferedPanel With {
            .Height = histHeight, .BackColor = CardBg, .Dock = DockStyle.Top
        }
        AddHandler histCard.Paint,
             Sub(s, e)
                 PanelRenderer.Draw(CType(s, Panel), e, False)
             End Sub

        ' Header: title + count badge
        Dim lblHist As New Label With {
            .Text = "Recent Purchase Orders",
            .Font = New Font("Segoe UI", 14, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True,
            .Location = New Point(20, 14), .BackColor = Color.Transparent
        }
        Dim histCountPill As New Label With {
            .Text = If(history.Count = 0, "No orders", history.Count & If(history.Count = 1, " order", " orders")),
            .Font = New Font("Segoe UI Semibold", 9.5F, FontStyle.Bold),
            .ForeColor = If(history.Count > 0, MintDeep, SubText),
            .BackColor = If(history.Count > 0, TileBg, Color.Transparent),
            .AutoSize = True,
            .Padding = If(history.Count > 0, New Padding(10, 4, 10, 4), New Padding(0, 4, 2, 4))
        }
        ' Empty state is a plain label — no fill, no pill region.
        If history.Count > 0 Then PillLabel(histCountPill)
        histCard.Controls.AddRange({lblHist, histCountPill})

        ' Status badge of most recent order — pinned TOP-RIGHT of the card
        Dim histBadge As Label = Nothing
        If history.Count > 0 Then
            Dim badgeBack As Color
            Select Case history(0).Status
                Case "Ordered" : badgeBack = MintDeep
                Case "Draft" : badgeBack = Color.FromArgb(115, 130, 126)
                Case "Partially Received" : badgeBack = WarningClr
                Case "Received" : badgeBack = PositiveClr
                Case "Cancelled" : badgeBack = NegativeClr
                Case Else : badgeBack = SubText
            End Select
            ' badgeBack doubles as the accent colour for the outline badge.
            histBadge = OutlineBadge(StatusInfo(history(0).Status).text, badgeBack)
            histCard.Controls.Add(histBadge)
        End If

        AddHandler histCard.Resize,
            Sub()
                histCountPill.Left = lblHist.Right + 10
                histCountPill.Top = lblHist.Top + (lblHist.PreferredHeight - histCountPill.PreferredHeight) \ 2 + 2
                If histBadge IsNot Nothing Then
                    histBadge.Left = histCard.Width - histBadge.Width - 16
                    histBadge.Top = 14
                End If
                Dim w As Integer = histCard.ClientSize.Width - 28
                For Each c As Control In histCard.Controls
                    If TypeOf c Is Panel Then
                        c.Left = 14
                        c.Width = w
                    End If
                Next
            End Sub

        If history.Count = 0 Then
            histCard.Controls.Add(New Label With {
                .Text = "No purchase orders for this supplier yet.",
                .Font = New Font("Segoe UI", 10), .ForeColor = SubText,
                .AutoSize = True, .Location = New Point(20, 52), .BackColor = Color.Transparent
            })
        Else
            Dim yh As Integer = 52
            For Each h In history
                Dim r As Panel = BuildHistoryRow(h, 54)
                r.Location = New Point(14, yh)
                r.Width = histCard.ClientSize.Width - 28
                r.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
                histCard.Controls.Add(r)
                yh += 58
            Next

        End If

        Dim spacer2 As New Panel With {.Height = 14, .Dock = DockStyle.Top, .BackColor = Color.Transparent}

        ' ── Products chips card ──────────────────────────────────────────────────
        Dim chipsCardH As Integer = If(products.Count = 0, 100, 54 + Math.Min(3, CInt(Math.Ceiling(products.Count / 4.0))) * 38 + 16)
        Dim chipsCard As New BufferedPanel With {
            .Height = chipsCardH, .BackColor = CardBg, .Dock = DockStyle.Top
        }
        AddHandler chipsCard.Paint,
             Sub(s, e)
                 PanelRenderer.Draw(CType(s, Panel), e, False)
             End Sub

        ' Header: title + count badge
        Dim lblChipsTitle As New Label With {
            .Text = "Products Supplied",
            .Font = New Font("Segoe UI", 14, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True,
            .Location = New Point(20, 14), .BackColor = Color.Transparent
        }
        Dim chipsCountPill As New Label With {
            .Text = If(products.Count = 0, "None assigned", products.Count & If(products.Count = 1, " product", " products")),
            .Font = New Font("Segoe UI Semibold", 9.5F, FontStyle.Bold),
            .ForeColor = If(products.Count > 0, MintDeep, SubText),
            .BackColor = If(products.Count > 0, TileBg, Color.Transparent),
            .AutoSize = True,
            .Padding = If(products.Count > 0, New Padding(10, 4, 10, 4), New Padding(0, 4, 2, 4))
        }
        ' Empty state is a plain label — no fill, no pill region.
        If products.Count > 0 Then PillLabel(chipsCountPill)
        chipsCard.Controls.AddRange({lblChipsTitle, chipsCountPill})
        AddHandler chipsCard.Resize,
    Sub()
        chipsCountPill.Left = lblChipsTitle.Right + 10
        chipsCountPill.Top = lblChipsTitle.Top + (lblChipsTitle.PreferredHeight - chipsCountPill.PreferredHeight) \ 2 + 2
    End Sub

        Dim chipFlow As New FlowLayoutPanel With {
            .Location = New Point(18, 50), .AutoSize = False, .Height = chipsCardH - 58,
            .BackColor = Color.Transparent, .WrapContents = True,
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top,
            .AutoScroll = True
        }
        If products.Count = 0 Then
            chipFlow.Controls.Add(New Label With {
                .Text = "No products assigned to this supplier yet.",
                .Font = New Font("Segoe UI", 10.5F), .ForeColor = SubText,
                .AutoSize = True, .Margin = New Padding(2, 8, 0, 0)
            })
        Else
            For Each p In products
                chipFlow.Controls.Add(MakeChip(p))
            Next
        End If
        chipsCard.Controls.Add(chipFlow)
        AddHandler chipsCard.Resize, Sub() chipFlow.Width = chipsCard.Width - 36

        Dim spacer1 As New Panel With {.Height = 14, .Dock = DockStyle.Top, .BackColor = Color.Transparent}

        ' ── Contact info card ─────────────────────────────────────────────────
        Dim infoCard As New BufferedPanel With {.Height = 260, .BackColor = CardBg, .Dock = DockStyle.Top}
        AddHandler infoCard.Paint, Sub(s, e) PaintCard(CType(s, Panel), e)

        Dim initial As String = SupplierLogo.InitialOf(sup.Name)
        ' The logo is the identity of the card, so it is given real size here
        ' rather than the list's small tile.
        Dim avatarSize As Integer = 84
        Dim avatar As New BufferedPanel With {
            .Size = New Size(avatarSize, avatarSize),
            .Location = New Point(22, 20), .BackColor = Color.Transparent
        }
        Dim detailImage As String = sup.ImagePath
        AddHandler avatar.Paint,
            Sub(s, e)
                SupplierLogo.Paint(e.Graphics, New Rectangle(0, 0, avatarSize - 1, avatarSize - 1),
                            20, detailImage, initial, 34.0F)
            End Sub
        infoCard.Controls.Add(avatar)

        Dim lblSupName As New Label With {
            .Text = sup.Name,
            .Font = New Font("Segoe UI", 18, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True,
            .Location = New Point(avatarSize + 40, 34), .BackColor = Color.Transparent
        }
        infoCard.Controls.Add(lblSupName)

        ' Second line: where they are. The full address is repeated in the field
        ' list below, but a supplier is recognised by name + place, so the
        ' header states both without making the reader look further down.
        Dim lblSupWhere As New Label With {
            .Text = If(String.IsNullOrWhiteSpace(sup.Address), "No address on file", sup.Address),
            .Font = New Font("Segoe UI", 10.5F),
            .ForeColor = SubText, .AutoSize = True,
            .Location = New Point(avatarSize + 42, 66), .BackColor = Color.Transparent
        }
        infoCard.Controls.Add(lblSupWhere)

        ' Centre the name block against the avatar once the labels have
        ' measured themselves, so a long name does not sit off-axis.
        Dim CentreNameBlock As Action =
            Sub()
                Dim blockH As Integer = lblSupName.PreferredHeight + 4 + lblSupWhere.PreferredHeight
                Dim top As Integer = 20 + (avatarSize - blockH) \ 2
                lblSupName.Top = top
                lblSupWhere.Top = top + lblSupName.PreferredHeight + 4
            End Sub
        CentreNameBlock()

        Dim pill As New Label With {
            .Text = sup.ActivePoCount & If(sup.ActivePoCount = 1, " active PO", " active POs"),
            .Font = New Font("Segoe UI Semibold", 10.0F, FontStyle.Bold),
            .ForeColor = If(sup.ActivePoCount > 0, MintDeep, SubText),
            .BackColor = If(sup.ActivePoCount > 0, TileBg, Color.Transparent),
            .AutoSize = True,
            .Padding = If(sup.ActivePoCount > 0, New Padding(13, 6, 13, 6), New Padding(0, 6, 2, 6)),
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        ' Empty state renders as a plain label — no fill, no pill region.
        If sup.ActivePoCount > 0 Then PillLabel(pill)
        infoCard.Controls.Add(pill)

        ' Inactive is the exception, so only that state is badged; active is the
        ' norm and a badge on every profile would just be noise.
        Dim inactiveBadge As Label = Nothing
        If Not sup.IsActive Then
            inactiveBadge = OutlineBadge("Inactive", SubText)
            infoCard.Controls.Add(inactiveBadge)
        End If
        Dim PositionPills As Action =
            Sub()
                pill.Left = infoCard.Width - pill.Width - 22
                pill.Top = 20 + (avatarSize - pill.Height) \ 2
                If inactiveBadge IsNot Nothing Then
                    ' Stacked under the PO pill, right edges aligned.
                    pill.Top = 20 + (avatarSize - pill.Height) \ 2 - 16
                    inactiveBadge.Left = infoCard.Width - inactiveBadge.Width - 22
                    inactiveBadge.Top = pill.Top + pill.Height + 8
                End If
            End Sub
        AddHandler infoCard.Resize, Sub() PositionPills()
        PositionPills()

        Dim nameDivider As New Panel With {
            .Height = 1, .BackColor = RowDivider,
            .Location = New Point(22, avatarSize + 32),
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        }
        infoCard.Controls.Add(nameDivider)
        AddHandler infoCard.Resize, Sub() nameDivider.Width = infoCard.Width - 44

        Dim fieldStartY As Integer = avatarSize + 46
        Dim fieldGap As Integer = 44

        Dim TintIcon As Func(Of String, Image) =
            Function(filename As String) As Image
                Dim p As String = IO.Path.Combine(Application.StartupPath, filename)
                If Not IO.File.Exists(p) Then Return Nothing
                Try
                    Dim raw As Image = Image.FromFile(p)
                    Dim bmp As New Bitmap(raw.Width, raw.Height)
                    Using g As Graphics = Graphics.FromImage(bmp)
                        Dim cm As New Imaging.ColorMatrix(New Single()() {
                            New Single() {0, 0, 0, 0, 0},
                            New Single() {0, 0, 0, 0, 0},
                            New Single() {0, 0, 0, 0, 0},
                            New Single() {0, 0, 0, 1, 0},
                            New Single() {CSng(0 / 255.0F), CSng(168 / 255.0F), CSng(142 / 255.0F), 0, 1}
                        })
                        Dim ia As New Imaging.ImageAttributes()
                        ia.SetColorMatrix(cm)
                        g.DrawImage(raw, New Rectangle(0, 0, bmp.Width, bmp.Height),
                                    0, 0, raw.Width, raw.Height, GraphicsUnit.Pixel, ia)
                    End Using
                    raw.Dispose()
                    Return bmp
                Catch
                    Return Nothing
                End Try
            End Function

        Dim Dash As String = ChrW(8212)
        Dim fieldDefs As New List(Of (img As Image, lbl As String, val As String)) From {
            (TintIcon("user.png"), "Contact Person", If(String.IsNullOrWhiteSpace(sup.Contact), Dash, sup.Contact)),
            (TintIcon("phone.png"), "Phone", If(String.IsNullOrWhiteSpace(sup.Phone), Dash, sup.Phone)),
            (TintIcon("mail.png"), "Email", If(String.IsNullOrWhiteSpace(sup.Email), Dash, sup.Email)),
            (TintIcon("location.png"), "Address", If(String.IsNullOrWhiteSpace(sup.Address), Dash, sup.Address))
        }
        ' Only offered once the columns exist, so an un-migrated database shows
        ' the same four fields it always did.
        ' Registration detail comes before the commercial terms: it identifies
        ' WHO the business legally is, which is what a reader checking papers
        ' is looking for, and the head of company sits next to the contact
        ' person so the difference between the two is visible at a glance.
        If SupplierLogo.HasRegistration Then
            fieldDefs.Add((TintIcon("group.png"), "Company Name",
                           If(String.IsNullOrWhiteSpace(sup.CompanyName), Dash, sup.CompanyName)))
            fieldDefs.Add((TintIcon("user.png"), "Head of Company",
                           If(String.IsNullOrWhiteSpace(sup.HeadName), Dash, sup.HeadName)))
        End If
        If SupplierLogo.HasCommercial Then
            fieldDefs.Add((TintIcon("clipboard.png"), "TIN",
                           If(String.IsNullOrWhiteSpace(sup.Tin), Dash, sup.Tin)))
        End If
        If SupplierLogo.HasRegistration Then
            fieldDefs.Add((TintIcon("newspaper.png"), "DTI Registration No.",
                           If(String.IsNullOrWhiteSpace(sup.DtiNo), Dash, sup.DtiNo)))
            fieldDefs.Add((TintIcon("task-checklist.png"), "BIR Registration No.",
                           If(String.IsNullOrWhiteSpace(sup.BirNo), Dash, sup.BirNo)))
        End If
        If SupplierLogo.HasCommercial Then
            fieldDefs.Add((TintIcon("wallet-income.png"), "Payment Terms",
                           If(String.IsNullOrWhiteSpace(sup.PaymentTerms), Dash, sup.PaymentTerms)))
        End If

        ' Three columns. The list reaches eight fields once registration is in,
        ' and at two columns that was four rows — taller than the card was
        ' before these fields existed, pushing the history card off the fold.
        ' At three it is three rows, so the view gains the new detail without
        ' costing any height. Two columns is kept for the shorter pre-migration
        ' list, where three would leave a ragged single item on the last row.
        Dim colGap As Integer = 20
        Dim fieldCols As Integer = If(fieldDefs.Count >= 7, 3, 2)

        Dim yf As Integer = fieldStartY
        Dim idx As Integer = -1
        For Each fd In fieldDefs
            idx += 1
            Dim col As Integer = idx Mod fieldCols
            Dim rowY As Integer = fieldStartY + (idx \ fieldCols) * fieldGap
            Dim colX As Integer = 22

            Dim iconTile As New BufferedPanel With {
                .Size = New Size(34, 34), .Location = New Point(colX, rowY), .BackColor = TileBg
            }
            AddHandler iconTile.Paint,
                Sub(s, e)
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                    Using path As GraphicsPath = RoundRect(New Rectangle(0, 0, 33, 33), 10)
                        Using br As New SolidBrush(TileBg)
                            e.Graphics.FillPath(br, path)
                        End Using
                    End Using
                End Sub
            Dim iconPic As New PictureBox With {
                .Size = New Size(18, 18), .Location = New Point(8, 8),
                .BackColor = Color.Transparent, .SizeMode = PictureBoxSizeMode.Zoom
            }
            If fd.img IsNot Nothing Then iconPic.Image = fd.img
            iconTile.Controls.Add(iconPic)
            Dim iconPath2 As New GraphicsPath()
            iconPath2.AddArc(0, 0, 10, 10, 180, 90)
            iconPath2.AddArc(23, 0, 10, 10, 270, 90)
            iconPath2.AddArc(23, 23, 10, 10, 0, 90)
            iconPath2.AddArc(0, 23, 10, 10, 90, 90)
            iconPath2.CloseFigure()
            iconTile.Region = New Region(iconPath2)

            Dim lblFieldCap As New Label With {
                .Text = fd.lbl, .Font = New Font("Segoe UI", 8.5F),
                .ForeColor = FaintText, .AutoSize = True,
                .Location = New Point(colX + 44, rowY + 2), .BackColor = Color.Transparent
            }
            Dim lblFieldVal As New Label With {
                .Text = fd.val, .Font = New Font("Segoe UI", 11, FontStyle.Bold),
                .ForeColor = MintDark, .AutoSize = True, .AutoEllipsis = True,
                .Location = New Point(colX + 44, rowY + 18), .BackColor = Color.Transparent
            }
            infoCard.Controls.AddRange({iconTile, lblFieldCap, lblFieldVal})

            ' Column x depends on the card's width, which is not known until it
            ' is laid out, so the trio is placed again on every resize.
            Dim capCol As Integer = col
            Dim RePlace As Action =
                Sub()
                    Dim usable As Integer = infoCard.Width - 44
                    ' N columns have N-1 gaps between them. Subtracting a single
                    ' gap regardless of the count made every column too wide, so
                    ' at three the rightmost one ran past the edge of the card.
                    Dim cw As Integer = (usable - colGap * (fieldCols - 1)) \ fieldCols
                    Dim x As Integer = 22 + capCol * (cw + colGap)
                    iconTile.Left = x
                    lblFieldCap.Left = x + 44
                    lblFieldVal.Left = x + 44
                    lblFieldVal.MaximumSize = New Size(Math.Max(60, cw - 48), 0)
                End Sub
            AddHandler infoCard.Resize, Sub() RePlace()
            RePlace()
        Next
        ' Where the field grid ends, whatever the row count.
        yf = fieldStartY + CInt(Math.Ceiling(fieldDefs.Count / CDbl(fieldCols))) * fieldGap

        ' Notes span the full width — free text does not belong in a column.
        If SupplierLogo.HasCommercial AndAlso Not String.IsNullOrWhiteSpace(sup.Notes) Then
            Dim lblNotesCap As New Label With {
                .Text = "Notes", .Font = New Font("Segoe UI", 8.5F),
                .ForeColor = FaintText, .AutoSize = True,
                .Location = New Point(22, yf), .BackColor = Color.Transparent
            }
            Dim lblNotesVal As New Label With {
                .Text = sup.Notes, .Font = New Font("Segoe UI", 10.5F),
                .ForeColor = Color.FromArgb(52, 68, 64), .AutoSize = True,
                .Location = New Point(22, yf + 18), .BackColor = Color.Transparent
            }
            infoCard.Controls.AddRange({lblNotesCap, lblNotesVal})

            Dim SizeNotes As Action =
                Sub() lblNotesVal.MaximumSize = New Size(Math.Max(120, infoCard.Width - 44), 0)
            AddHandler infoCard.Resize, Sub() SizeNotes()
            SizeNotes()

            ' AutoSize labels only report a real height once measured, so the
            ' card is grown by what the text actually needs.
            yf += 22 + Math.Max(20, lblNotesVal.PreferredHeight) + 14
        End If

        Dim actionBarY As Integer = yf + 12
        Dim actionBar As New Panel With {
            .BackColor = Color.Transparent, .Location = New Point(0, actionBarY),
            .Height = 56, .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        }
        AddHandler actionBar.Paint,
            Sub(s, e)
                Using pen As New Pen(CardBorder, 1)
                    e.Graphics.DrawLine(pen, 20, 0, CType(s, Panel).Width - 20, 0)
                End Using
            End Sub
        AddHandler infoCard.Resize, Sub() actionBar.Width = infoCard.Width

        Dim btnEdit As New Button With {
            .Text = "✎  Edit", .Size = New Size(100, 36), .FlatStyle = FlatStyle.Flat,
            .BackColor = MintPrimary, .ForeColor = Color.White,
            .Font = New Font("Segoe UI Semibold", 10), .Cursor = Cursors.Hand
        }
        btnEdit.FlatAppearance.BorderSize = 0
        RoundControl(btnEdit, 8)
        Dim capId As Integer = supplierId
        AddHandler btnEdit.Click, Sub() LoadForm(capId)

        Dim deleteIconImg As Image = Nothing
        Dim deleteIconPath As String = IO.Path.Combine(Application.StartupPath, "delete.png")
        If IO.File.Exists(deleteIconPath) Then
            Try
                Dim raw As Image = Image.FromFile(deleteIconPath)
                Dim bmp As New Bitmap(raw.Width, raw.Height)
                Using g As Graphics = Graphics.FromImage(bmp)
                    Dim cm As New Imaging.ColorMatrix(New Single()() {
                        New Single() {0, 0, 0, 0, 0},
                        New Single() {0, 0, 0, 0, 0},
                        New Single() {0, 0, 0, 0, 0},
                        New Single() {0, 0, 0, 1, 0},
                        New Single() {CSng(210 / 255.0F), CSng(60 / 255.0F), CSng(60 / 255.0F), 0, 1}
                    })
                    Dim ia As New Imaging.ImageAttributes()
                    ia.SetColorMatrix(cm)
                    g.DrawImage(raw, New Rectangle(0, 0, bmp.Width, bmp.Height), 0, 0, raw.Width, raw.Height, GraphicsUnit.Pixel, ia)
                End Using
                deleteIconImg = bmp
                raw.Dispose()
            Catch
            End Try
        End If

        Dim btnDelete As New Button With {
            .Text = "Delete", .Size = New Size(110, 36), .FlatStyle = FlatStyle.Flat,
            .BackColor = Color.White, .ForeColor = NegativeClr,
            .Font = New Font("Segoe UI Semibold", 10), .Cursor = Cursors.Hand
        }
        btnDelete.FlatAppearance.BorderSize = 0
        AddHandler btnDelete.Paint,
            Sub(s, e)
                Dim b = CType(s, Button)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Using path As GraphicsPath = RoundRect(New Rectangle(1, 1, b.Width - 2, b.Height - 2), 8)
                    Using fill As New SolidBrush(b.BackColor)
                        e.Graphics.FillPath(fill, path)
                    End Using
                    Using pen As New Pen(Color.FromArgb(220, 180, 180), 1.2F)
                        e.Graphics.DrawPath(pen, path)
                    End Using
                End Using
                Dim iconSize As Integer = 16
                Dim gap As Integer = 6
                Dim sf As New StringFormat With {
                    .Alignment = StringAlignment.Near, .LineAlignment = StringAlignment.Center
                }
                Using brush As New SolidBrush(b.ForeColor)
                    Dim textSz As SizeF = e.Graphics.MeasureString(b.Text, b.Font)
                    Dim totalW As Single = If(deleteIconImg IsNot Nothing, iconSize + gap + textSz.Width, textSz.Width)
                    Dim startX As Single = (b.Width - totalW) / 2.0F
                    Dim midY As Single = b.Height / 2.0F
                    If deleteIconImg IsNot Nothing Then
                        e.Graphics.DrawImage(deleteIconImg,
                            New RectangleF(startX, midY - iconSize / 2.0F, iconSize, iconSize))
                        startX += iconSize + gap
                    End If
                    e.Graphics.DrawString(b.Text, b.Font, brush,
                        New RectangleF(startX, 0, textSz.Width + 2, b.Height), sf)
                End Using
            End Sub
        AddHandler btnDelete.MouseEnter,
            Sub()
                btnDelete.BackColor = Color.FromArgb(251, 234, 234)
                btnDelete.Invalidate()
            End Sub
        AddHandler btnDelete.MouseLeave,
            Sub()
                btnDelete.BackColor = Color.White
                btnDelete.Invalidate()
            End Sub
        AddHandler btnDelete.Click,
            Sub()
                ' Purchase orders and quotations both reference suppliers with a
                ' foreign key, so deleting one that is in use fails at the
                ' database and surfaced a raw constraint error. Check first and
                ' explain it, the way the Categories screen does.
                Dim inUse As Integer = 0
                Try
                    Using conn = Db.GetConnection()
                        conn.Open()
                        Using cmd As New MySqlCommand(
                            "SELECT (SELECT COUNT(*) FROM purchase_orders WHERE supplier_id=@id) + " &
                            "       (SELECT COUNT(*) FROM quotations WHERE supplier_id=@id)", conn)
                            cmd.Parameters.AddWithValue("@id", supplierId)
                            inUse = Convert.ToInt32(cmd.ExecuteScalar())
                        End Using
                    End Using
                Catch ex As Exception
                    AppModal.Show("Could not check whether the supplier is in use:" &
                                  Environment.NewLine & ex.Message,
                                  "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
                    Return
                End Try

                If inUse > 0 Then
                    AppModal.Show(sup.Name & " is referenced by " & inUse.ToString("N0") &
                                  If(inUse = 1, " purchase order or quotation",
                                                " purchase orders / quotations") &
                                  " and cannot be deleted." & Environment.NewLine & Environment.NewLine &
                                  "Its history has to stay for those records to make sense.",
                                  "Supplier In Use", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    Return
                End If

                Dim confirm = AppModal.Show(
                    "Are you sure you want to delete """ & sup.Name & """?" &
                    Environment.NewLine &
                    If(Archive.HasArchiveTable(), "An admin can restore it from the Recycle Bin.",
                       "This cannot be undone."),
                    "Delete Supplier", MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
                If confirm = DialogResult.Yes Then
                    Try
                        Using conn = Db.GetConnection()
                            conn.Open()
                            Using tx = conn.BeginTransaction()
                                Archive.Capture(conn, tx, "suppliers", "supplier_id", supplierId, sup.Name)
                                Using cmd As New MySqlCommand("DELETE FROM suppliers WHERE supplier_id = @id", conn, tx)
                                    cmd.Parameters.AddWithValue("@id", supplierId)
                                    cmd.ExecuteNonQuery()
                                End Using
                                tx.Commit()
                            End Using
                        End Using
                        ' Deleting master data is irreversible, so the name is
                        ' kept in the trail even though the row is gone.
                        ActivityLog.Record("SUPPLIER_DELETE", "supplier", supplierId, sup.Name)

                        AppModal.Show("Supplier deleted.", "Deleted",
                            MessageBoxButtons.OK, MessageBoxIcon.Information)
                        RenderList()
                    Catch ex As Exception
                        AppModal.Show("Could not delete supplier:" & Environment.NewLine & ex.Message,
                            "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
                    End Try
                End If
            End Sub

        actionBar.Controls.AddRange({btnEdit, btnDelete})

        Dim PositionActionBtns As Action =
            Sub()
                Dim t As Integer = (actionBar.Height - btnEdit.Height) \ 2
                btnEdit.Top = t
                btnEdit.Left = actionBar.Width - 20 - btnEdit.Width
                btnDelete.Top = t
                btnDelete.Left = btnEdit.Left - btnDelete.Width - 10
            End Sub
        AddHandler actionBar.Resize, Sub() PositionActionBtns()
        PositionActionBtns()

        infoCard.Height = actionBarY + actionBar.Height + 28
        infoCard.Controls.Add(actionBar)

        body.Controls.Add(histCard)
        body.Controls.Add(spacer2)
        body.Controls.Add(chipsCard)
        body.Controls.Add(spacer1)
        body.Controls.Add(infoCard)

        ' ── Detail header ─────────────────────────────────────────────────────
        Dim header As New Panel With {
            .Height = 80, .BackColor = MintBg,
            .Padding = New Padding(0, 0, 24, 0), .Dock = DockStyle.Top
        }
        Dim iconPathD As String = IO.Path.Combine(Application.StartupPath, "previous.png")
        Dim picBackD As New PictureBox With {
            .Size = New Size(22, 22), .Location = New Point(24, 36),
            .BackColor = Color.Transparent, .SizeMode = PictureBoxSizeMode.Zoom, .Cursor = Cursors.Hand
        }
        If IO.File.Exists(iconPathD) Then
            Try
                Dim raw As Image = Image.FromFile(iconPathD)
                Dim bmp As New Bitmap(raw.Width, raw.Height)
                Using g As Graphics = Graphics.FromImage(bmp)
                    Dim cm As New Imaging.ColorMatrix(New Single()() {
                        New Single() {0, 0, 0, 0, 0},
                        New Single() {0, 0, 0, 0, 0},
                        New Single() {0, 0, 0, 0, 0},
                        New Single() {0, 0, 0, 1, 0},
                        New Single() {CSng(0 / 255.0F), CSng(130 / 255.0F), CSng(109 / 255.0F), 0, 1}
                    })
                    Dim ia As New Imaging.ImageAttributes()
                    ia.SetColorMatrix(cm)
                    g.DrawImage(raw, New Rectangle(0, 0, bmp.Width, bmp.Height), 0, 0, raw.Width, raw.Height, GraphicsUnit.Pixel, ia)
                End Using
                picBackD.Image = bmp
                raw.Dispose()
            Catch
            End Try
        End If
        AddHandler picBackD.Click, Sub() RenderList()
        AddHandler picBackD.MouseEnter, Sub() picBackD.BackColor = Color.FromArgb(30, 0, 130, 109)
        AddHandler picBackD.MouseLeave, Sub() picBackD.BackColor = Color.Transparent
        Dim lblTitleD As New Label With {
            .Text = "Supplier",
            .Font = New Font("Segoe UI", 22, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True,
            .Location = New Point(picBackD.Right + 10, 24), .BackColor = Color.Transparent
        }
        header.Controls.AddRange({picBackD, lblTitleD})

        container.Controls.Add(body)
        container.Controls.Add(header)
        container.ResumeLayout()
    End Sub

    Private Shared Sub AddDetailRow(parent As Panel, label As String, value As String, y As Integer,
                                    Optional anchorBottom As Integer = 0)
        Dim lbl As New Label With {
            .Text = label, .Font = New Font("Segoe UI Semibold", 10),
            .ForeColor = FaintText, .AutoSize = False, .Size = New Size(140, 24),
            .Location = New Point(22, y), .BackColor = Color.Transparent
        }
        Dim val As New Label With {
            .Text = value, .Font = New Font("Segoe UI", 11),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(170, y), .BackColor = Color.Transparent
        }
        parent.Controls.AddRange({lbl, val})
    End Sub

    Private Shared Function BuildHistoryRow(h As POHistRow, height As Integer) As Panel
        Dim row As New BufferedPanel With {.Height = height, .BackColor = Color.Transparent}

        Dim accentClr As Color
        Select Case h.Status
            Case "Ordered" : accentClr = MintDeep
            Case "Draft" : accentClr = FaintText
            Case "Partially Received" : accentClr = WarningClr
            Case "Received" : accentClr = PositiveClr
            Case "Cancelled" : accentClr = NegativeClr
            Case Else : accentClr = SubText
        End Select

        AddHandler row.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                ' Row tile
                Dim rect As New Rectangle(0, 3, row.Width - 1, row.Height - 6)
                Using path As GraphicsPath = RoundRect(rect, 10)
                    Using br As New SolidBrush(Color.FromArgb(246, 251, 249))
                        e.Graphics.FillPath(br, path)
                    End Using
                    Using pen As New Pen(RowDivider, 1.0F)
                        e.Graphics.DrawPath(pen, path)
                    End Using
                End Using

            End Sub

        Dim lblPo As New Label With {
            .Text = h.PoNo,
            .Font = New Font("Segoe UI", 11, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True,
            .Location = New Point(20, 8), .BackColor = Color.Transparent
        }
        Dim lblMeta As New Label With {
            .Text = h.OrderDate.ToString("MMM d, yyyy") & "   " & ChrW(8226) & "   " &
                    h.ItemCount & If(h.ItemCount = 1, " item", " items"),
            .Font = New Font("Segoe UI", 10), .ForeColor = SubText, .AutoSize = True,
            .Location = New Point(20, 28), .BackColor = Color.Transparent
        }
        row.Controls.AddRange({lblPo, lblMeta})

        Return row
    End Function


    Private Shared Function MakeChip(text As String) As Label
        Dim chip As New Label With {
            .Text = text, .Font = New Font("Segoe UI", 9),
            .ForeColor = MintDeep, .BackColor = TileBg, .AutoSize = True,
            .Padding = New Padding(10, 5, 10, 5), .Margin = New Padding(0, 0, 8, 8)
        }
        PillLabel(chip)
        Return chip
    End Function

    ' =========================================================================
    '  DATA LOADERS
    ' =========================================================================
    Private Shared Function LoadSupplierList() As List(Of SupplierRow)
        Dim result As New List(Of SupplierRow)
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT s.supplier_id, s.name, s.contact_person, s.phone, s.email, s.address, s.is_active, " &
                    SupplierLogo.SelectFragment() &
                    "(SELECT COUNT(*) FROM purchase_orders po " &
                    " WHERE po.supplier_id = s.supplier_id AND po.status NOT IN ('Cancelled','Received')) AS active_pos " &
                    "FROM suppliers s ORDER BY s.name ASC", conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            result.Add(ReadSupplier(rdr, True))
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load suppliers:" & Environment.NewLine & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
        Return result
    End Function

    Private Shared Function LoadOneSupplier(id As Integer) As SupplierRow
        Dim sup As SupplierRow = Nothing
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT s.supplier_id, s.name, s.contact_person, s.phone, s.email, s.address, s.is_active, " &
                    SupplierLogo.SelectFragment() &
                    "(SELECT COUNT(*) FROM purchase_orders po " &
                    " WHERE po.supplier_id = s.supplier_id AND po.status NOT IN ('Cancelled','Received')) AS active_pos " &
                    "FROM suppliers s WHERE s.supplier_id = @id LIMIT 1", conn)
                    cmd.Parameters.AddWithValue("@id", id)
                    Using rdr = cmd.ExecuteReader()
                        If rdr.Read() Then sup = ReadSupplier(rdr, True)
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load the supplier:" & Environment.NewLine & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
        Return sup
    End Function

    Private Shared Function ReadSupplier(rdr As MySqlDataReader, withCount As Boolean) As SupplierRow
        Return New SupplierRow With {
            .Id = SafeInt(rdr("supplier_id")),
            .Name = rdr("name").ToString(),
            .Contact = If(IsDBNull(rdr("contact_person")), "", rdr("contact_person").ToString()),
            .Phone = If(IsDBNull(rdr("phone")), "", rdr("phone").ToString()),
            .Email = If(IsDBNull(rdr("email")), "", rdr("email").ToString()),
            .Address = If(IsDBNull(rdr("address")), "", rdr("address").ToString()),
            .ImagePath = SupplierLogo.ReadPath(rdr),
            .Tin = SupplierLogo.ReadColumn(rdr, "tin"),
            .PaymentTerms = SupplierLogo.ReadColumn(rdr, "payment_terms"),
            .Notes = SupplierLogo.ReadColumn(rdr, "notes"),
            .CompanyName = SupplierLogo.ReadColumn(rdr, "company_name"),
            .HeadName = SupplierLogo.ReadColumn(rdr, "head_name"),
            .DtiNo = SupplierLogo.ReadColumn(rdr, "dti_no"),
            .BirNo = SupplierLogo.ReadColumn(rdr, "bir_no"),
            .IsActive = (Not IsDBNull(rdr("is_active"))) AndAlso Convert.ToBoolean(rdr("is_active")),
            .ActivePoCount = If(withCount, SafeInt(rdr("active_pos")), 0)
        }
    End Function

    Private Shared Function LoadSupplierProducts(id As Integer) As List(Of String)
        Dim result As New List(Of String)
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT name FROM products WHERE supplier_id = @id AND is_active = 1 ORDER BY name ASC", conn)
                    cmd.Parameters.AddWithValue("@id", id)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            result.Add(rdr("name").ToString())
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load supplier products:" & Environment.NewLine & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
        Return result
    End Function

    Private Shared Function LoadSupplierPOs(id As Integer) As List(Of POHistRow)
        Dim result As New List(Of POHistRow)
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT po.po_no, po.order_date, po.status, po.total_cost, " &
                    "(SELECT COUNT(*) FROM po_items pi WHERE pi.po_id = po.po_id) AS item_count " &
                    "FROM purchase_orders po WHERE po.supplier_id = @id " &
                    "ORDER BY po.order_date DESC, po.po_id DESC LIMIT 10", conn)
                    cmd.Parameters.AddWithValue("@id", id)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            result.Add(New POHistRow With {
                                .PoNo = rdr("po_no").ToString(),
                                .OrderDate = If(IsDBNull(rdr("order_date")), DateTime.Today, Convert.ToDateTime(rdr("order_date"))),
                                .Status = rdr("status").ToString(),
                                .TotalCost = If(IsDBNull(rdr("total_cost")), 0D, Convert.ToDecimal(rdr("total_cost"))),
                                .ItemCount = SafeInt(rdr("item_count"))
                            })
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load supplier purchase orders:" & Environment.NewLine & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
        Return result
    End Function

    ' =========================================================================
    '  SHARED UI HELPERS
    ' =========================================================================
    Private Shared Function BuildEmptyCard(title As String, message As String) As Panel
        Dim card As New BufferedPanel With {.Height = 150, .BackColor = CardBg}
        AddHandler card.Paint, Sub(s, e) PaintCard(CType(s, Panel), e)
        Dim lbl As New Label With {
            .Text = title, .Font = New Font("Segoe UI", 14, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(28, 44), .BackColor = Color.Transparent
        }
        Dim sub_ As New Label With {
            .Text = message, .Font = New Font("Segoe UI", 10.5F), .ForeColor = SubText, .AutoSize = True,
            .Location = New Point(30, 76), .BackColor = Color.Transparent
        }
        card.Controls.AddRange({lbl, sub_})
        Return card
    End Function

    Private Shared Function StatusInfo(status As String) As (text As String, fore As Color, back As Color)
        Select Case status
            Case "Draft" : Return ("Draft", FaintText, Color.FromArgb(238, 240, 239))
            Case "Ordered" : Return ("Ordered", MintPrimary, TileBg)
            Case "Partially Received" : Return ("Partial", WarningClr, Color.FromArgb(252, 240, 228))
            Case "Received" : Return ("Received", PositiveClr, Color.FromArgb(232, 245, 233))
            Case "Cancelled" : Return ("Cancelled", NegativeClr, Color.FromArgb(251, 234, 234))
            Case Else : Return (status, SubText, TileBg)
        End Select
    End Function

    ' =========================================================================
    '  PAINT / CONTROL HELPERS
    ' =========================================================================
    Private Shared Sub PaintCard(panel As Panel, e As PaintEventArgs)
        PanelRenderer.Draw(panel, e, True)
    End Sub

    ' A card that sits ON the page rather than in it. The white form card was
    ' otherwise near-invisible against the mint background, which is what made
    ' the screen read as one flat surface with fields floating on it.
    Private Shared Sub PaintElevatedCard(panel As Panel, e As PaintEventArgs)
        ' The panel's own background is the page colour, so the shadow has
        ' something to fall on instead of a white square.
        Using bg As New SolidBrush(MintBg)
            e.Graphics.FillRectangle(bg, panel.ClientRectangle)
        End Using
        PanelRenderer.Draw(panel, e, True, True)
    End Sub

    ' Outlined rounded button, centred label. Same reasoning as
    ' PaintFilledButton, with a hairline border instead of a solid fill.
    Private Shared Sub PaintOutlineButton(b As Button, e As PaintEventArgs, border As Color)
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
        Using path As GraphicsPath = RoundRect(New Rectangle(1, 1, b.Width - 2, b.Height - 2), 8)
            Using fill As New SolidBrush(b.BackColor)
                e.Graphics.FillPath(fill, path)
            End Using
            Using pen As New Pen(border, 1.2F)
                e.Graphics.DrawPath(pen, path)
            End Using
        End Using
        Dim sf As New StringFormat With {
            .Alignment = StringAlignment.Center, .LineAlignment = StringAlignment.Center
        }
        Using brush As New SolidBrush(b.ForeColor)
            e.Graphics.DrawString(b.Text, b.Font, brush, New RectangleF(0, 0, b.Width, b.Height), sf)
        End Using
    End Sub

    ' The tinted back chevron both the form and detail headers use. Built once
    ' here rather than copied into each, which is how it drifted before.
    Private Shared Function BuildBackArrow() As PictureBox
        Dim pic As New PictureBox With {
            .Size = New Size(22, 22), .Location = New Point(24, 36),
            .BackColor = Color.Transparent,
            .SizeMode = PictureBoxSizeMode.Zoom, .Cursor = Cursors.Hand
        }
        Dim iconPath As String = IO.Path.Combine(Application.StartupPath, "previous.png")
        If IO.File.Exists(iconPath) Then
            Try
                Dim raw As Image = Image.FromFile(iconPath)
                Dim bmp As New Bitmap(raw.Width, raw.Height)
                Using g As Graphics = Graphics.FromImage(bmp)
                    Dim cm As New Imaging.ColorMatrix(New Single()() {
                        New Single() {0, 0, 0, 0, 0},
                        New Single() {0, 0, 0, 0, 0},
                        New Single() {0, 0, 0, 0, 0},
                        New Single() {0, 0, 0, 1, 0},
                        New Single() {CSng(0 / 255.0F), CSng(130 / 255.0F), CSng(109 / 255.0F), 0, 1}
                    })
                    Dim ia As New Imaging.ImageAttributes()
                    ia.SetColorMatrix(cm)
                    g.DrawImage(raw, New Rectangle(0, 0, bmp.Width, bmp.Height),
                                0, 0, raw.Width, raw.Height, GraphicsUnit.Pixel, ia)
                End Using
                pic.Image = bmp
                raw.Dispose()
            Catch
                ' No icon is better than a crash on a missing or locked file.
            End Try
        End If
        AddHandler pic.MouseEnter, Sub() pic.BackColor = Color.FromArgb(30, 0, 130, 109)
        AddHandler pic.MouseLeave, Sub() pic.BackColor = Color.Transparent
        Return pic
    End Function

    ' Solid rounded button, centred label. FlatStyle alone cannot round a
    ' WinForms button without a Region, and a Region clips to hard pixels, so
    ' the fill is drawn instead and the corners stay smooth.
    Private Shared Sub PaintFilledButton(b As Button, e As PaintEventArgs)
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
        Using path As GraphicsPath = RoundRect(New Rectangle(0, 0, b.Width, b.Height), 8)
            Using fill As New SolidBrush(b.BackColor)
                e.Graphics.FillPath(fill, path)
            End Using
        End Using
        Dim sf As New StringFormat With {
            .Alignment = StringAlignment.Center, .LineAlignment = StringAlignment.Center
        }
        Using brush As New SolidBrush(b.ForeColor)
            e.Graphics.DrawString(b.Text, b.Font, brush, New RectangleF(0, 0, b.Width, b.Height), sf)
        End Using
    End Sub

    Private Shared Function MakeLink(text As String, clr As Color) As Label
        Dim lbl As New Label With {
            .Text = text, .Font = New Font("Segoe UI", 11, FontStyle.Bold),
            .ForeColor = clr, .AutoSize = True, .Cursor = Cursors.Hand, .BackColor = Color.Transparent
        }
        AddHandler lbl.MouseEnter, Sub() lbl.Font = New Font("Segoe UI", 11, FontStyle.Bold Or FontStyle.Underline)
        AddHandler lbl.MouseLeave, Sub() lbl.Font = New Font("Segoe UI", 11, FontStyle.Bold)
        Return lbl
    End Function

    ' An untyped optional field is stored NULL rather than "", so the column
    ' reads the same as never-set and "no terms agreed" cannot be confused with
    ' "terms agreed, recorded as empty".
    Private Shared Function NullIfBlank(value As String) As Object
        If String.IsNullOrWhiteSpace(value) Then Return DBNull.Value
        Return value
    End Function

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

    ' True pill: corner radius always equals the label's full height, so both
    ' ends are perfect semicircles no matter how the text/padding sizes it.
    ' AutoSize labels have usually already sized themselves by the time this is
    ' called, so apply the region NOW as well as on every later resize.
    ' Minimalist status badge: no fill, just the status colour as text plus a
    ' hairline border at a soft radius. Owner-drawn because a WinForms Label
    ' cannot paint its own rounded border.
    Private Shared Function OutlineBadge(text As String, accent As Color) As Label
        Dim lbl As New Label With {
            .Text = text,
            .Font = New Font("Segoe UI", 9.0F, FontStyle.Regular),
            .ForeColor = accent,
            .BackColor = Color.Transparent,
            .AutoSize = True,
            .Padding = New Padding(12, 5, 12, 5),
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        AddHandler lbl.Paint,
            Sub(s2, e2)
                Dim b As Label = CType(s2, Label)
                e2.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim r As New Rectangle(0, 0, b.Width - 1, b.Height - 1)
                Using path As GraphicsPath = RoundRect(r, 12)
                    Using pen As New Pen(Color.FromArgb(85, accent), 1.0F)
                        e2.Graphics.DrawPath(pen, path)
                    End Using
                End Using
                Dim sf As New StringFormat With {
                    .Alignment = StringAlignment.Center,
                    .LineAlignment = StringAlignment.Center
                }
                Using br As New SolidBrush(b.ForeColor)
                    e2.Graphics.DrawString(b.Text, b.Font, br, New RectangleF(0, 0, b.Width, b.Height), sf)
                End Using
            End Sub
        Return lbl
    End Function

    Private Shared Sub PillLabel(lbl As Label)
        Dim apply As Action =
            Sub()
                Dim h As Integer = lbl.Height
                If h <= 0 OrElse lbl.Width < h Then Return
                Dim path As New GraphicsPath()
                path.AddArc(0, 0, h, h, 90, 180)
                path.AddArc(lbl.Width - h, 0, h, h, 270, 180)
                path.CloseFigure()
                lbl.Region = New Region(path)
            End Sub
        AddHandler lbl.Resize, Sub() apply()
        AddHandler lbl.TextChanged, Sub() apply()
        AddHandler lbl.FontChanged, Sub() apply()
        ' AutoSize labels report their final size only once they have a parent /
        ' have been laid out; cover both the immediate and deferred cases.
        AddHandler lbl.HandleCreated, Sub() apply()
        AddHandler lbl.ParentChanged, Sub() apply()
        apply()
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


    Private Sub ProcurementSuppliersPanelBuilder_Load(sender As Object, e As EventArgs) Handles MyBase.Load

    End Sub
End Class