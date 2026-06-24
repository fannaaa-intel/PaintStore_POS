Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports MySql.Data.MySqlClient

' =============================================================================
'  ProcurementSuppliersPanelBuilder
'  Screen 3 — Suppliers.
'
'  INLINE VIEWS (swapped in the same container):
'    1. List    — every supplier with contact details + active-PO count.
'                 "Add Supplier" opens the form.
'    2. Form    — add OR edit a supplier (name / contact / phone / email /
'                 address). Save inserts or updates the suppliers table.
'    3. Detail  — one supplier: contact card, product chips
'                 (products WHERE supplier_id = this), last-10 PO history,
'                 and an Edit button.
'
'  Shared-only, no instance state. Verbatim helpers from
'  InventoryLowStockPanelBuilder (RoundRect / RoundControl / RoundLabel /
'  MakeLink / SafeInt / HexToColor).
' =============================================================================
Public Class ProcurementSuppliersPanelBuilder

    ' ── PALETTE (exact spec values) ──────────────────────────────────────────────
    Private Shared ReadOnly MintDark As Color = Color.FromArgb(10, 79, 66)
    Private Shared ReadOnly MintPrimary As Color = Color.FromArgb(0, 168, 142)
    Private Shared ReadOnly MintDeep As Color = Color.FromArgb(0, 130, 109)
    Private Shared ReadOnly MintBg As Color = Color.FromArgb(244, 248, 246)
    Private Shared ReadOnly CardBg As Color = Color.White
    Private Shared ReadOnly CardBorder As Color = Color.FromArgb(228, 232, 230)
    Private Shared ReadOnly SubText As Color = Color.FromArgb(90, 114, 107)
    Private Shared ReadOnly FaintText As Color = Color.FromArgb(150, 165, 160)
    Private Shared ReadOnly TileBg As Color = Color.FromArgb(236, 248, 244)
    Private Shared ReadOnly RowDivider As Color = Color.FromArgb(238, 242, 240)
    Private Shared ReadOnly PositiveClr As Color = Color.FromArgb(0, 160, 106)
    Private Shared ReadOnly NegativeClr As Color = Color.FromArgb(210, 60, 60)
    Private Shared ReadOnly WarningClr As Color = Color.FromArgb(232, 120, 30)
    Private Shared ReadOnly Peso As String = ChrW(8369)

    ' ── DATA MODELS ──────────────────────────────────────────────────────────────
    Private Class SupplierRow
        Public Id As Integer
        Public Name As String
        Public Contact As String
        Public Phone As String
        Public Email As String
        Public Address As String
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

    ' Form field references
    Private Shared editingId As Integer = 0   ' 0 = add new
    Private Shared txtName As TextBox
    Private Shared txtContact As TextBox
    Private Shared txtPhone As TextBox
    Private Shared txtEmail As TextBox
    Private Shared txtAddress As TextBox
    Private Shared btnSaveRef As Button

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Shared Sub LoadSuppliers(container As Panel)
        containerRef = container
        RenderList()
    End Sub

    ' =========================================================================
    '  VIEW 1 — LIST
    ' =========================================================================
    Private Shared Sub RenderList()
        Dim container As Panel = containerRef
        If container Is Nothing Then Return

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
                y += card.Height + 12
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

    Private Shared Function BuildListTop(count As Integer) As Panel
        Dim region As New Panel With {.Height = 96, .BackColor = MintBg, .Padding = New Padding(24, 22, 24, 0)}

        Dim lblTitle As New Label With {
            .Text = "Suppliers", .Font = New Font("Segoe UI", 24, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(24, 18)
        }
        Dim lblSub As New Label With {
            .Text = count & If(count = 1, " supplier", " suppliers"),
            .Font = New Font("Segoe UI", 11), .ForeColor = SubText, .AutoSize = True, .Location = New Point(30, 58)
        }
        region.Controls.AddRange({lblTitle, lblSub})

        Dim btnAdd As New Button With {
            .Text = "  + Add Supplier", .Size = New Size(150, 40),
            .FlatStyle = FlatStyle.Flat, .BackColor = MintPrimary, .ForeColor = Color.White,
            .Font = New Font("Segoe UI Semibold", 10), .Cursor = Cursors.Hand,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        btnAdd.FlatAppearance.BorderSize = 0
        RoundControl(btnAdd, 10)
        AddHandler btnAdd.Click, Sub() LoadForm(0)
        region.Controls.Add(btnAdd)
        AddHandler region.Resize, Sub()
                                      btnAdd.Top = 22
                                      btnAdd.Left = region.Width - 24 - btnAdd.Width
                                  End Sub
        btnAdd.Top = 22
        btnAdd.Left = 700

        Return region
    End Function

    Private Shared Function BuildSupplierCard(sup As SupplierRow) As Panel
        Dim card As New BufferedPanel With {.Height = 100, .BackColor = CardBg, .Cursor = Cursors.Hand}
        AddHandler card.Paint, Sub(s, e) PaintCard(CType(s, Panel), e)
        AddHandler card.Resize,
            Sub()
                Dim path As New GraphicsPath()
                Dim r As Integer = 12
                Dim rect As New Rectangle(0, 0, card.Width, card.Height)
                path.AddArc(rect.X, rect.Y, r, r, 180, 90)
                path.AddArc(rect.Right - r, rect.Y, r, r, 270, 90)
                path.AddArc(rect.Right - r, rect.Bottom - r, r, r, 0, 90)
                path.AddArc(rect.X, rect.Bottom - r, r, r, 90, 90)
                path.CloseFigure()
                card.Region = New Region(path)
            End Sub

        Dim lblName As New Label With {
    .Text = sup.Name, .Font = New Font("Segoe UI", 14, FontStyle.Bold),
    .ForeColor = MintDark, .AutoSize = True, .Location = New Point(20, 16), .BackColor = Color.Transparent
}
        Dim contactBits As New List(Of String)
        If Not String.IsNullOrWhiteSpace(sup.Contact) Then contactBits.Add(sup.Contact)
        If Not String.IsNullOrWhiteSpace(sup.Phone) Then contactBits.Add(sup.Phone)
        If Not String.IsNullOrWhiteSpace(sup.Email) Then contactBits.Add(sup.Email)
        Dim lblContact As New Label With {
    .Text = If(contactBits.Count = 0, "No contact details", String.Join("   " & ChrW(8226) & "   ", contactBits)),
    .Font = New Font("Segoe UI", 11), .ForeColor = SubText, .AutoSize = True,
    .Location = New Point(20, 50), .BackColor = Color.Transparent
}
        card.Controls.AddRange({lblName, lblContact})

        ' Active PO pill
        Dim pill As New Label With {
            .Text = sup.ActivePoCount & If(sup.ActivePoCount = 1, " active PO", " active POs"),
          .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold),
            .ForeColor = If(sup.ActivePoCount > 0, MintDeep, FaintText),
            .BackColor = If(sup.ActivePoCount > 0, TileBg, Color.FromArgb(238, 240, 239)),
            .AutoSize = True, .Padding = New Padding(9, 4, 9, 4),
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        RoundLabel(pill, 11)
        card.Controls.Add(pill)

        Dim lnkView As Label = MakeLink("View", MintPrimary)
        lnkView.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        card.Controls.Add(lnkView)

        Dim capId As Integer = sup.Id
        AddHandler lnkView.Click, Sub() LoadDetail(capId)
        AddHandler card.Click, Sub() LoadDetail(capId)
        AddHandler lblName.Click, Sub() LoadDetail(capId)
        AddHandler lblContact.Click, Sub() LoadDetail(capId)

        AddHandler card.Resize,
    Sub()
        lnkView.Left = card.Width - lnkView.Width - 22
        lnkView.Top = 20
        pill.Left = card.Width - pill.Width - 22
        pill.Top = 56.0F
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
        Dim header As New Panel With {
        .Height = 88, .BackColor = MintBg,
        .Padding = New Padding(24, 18, 24, 0), .Dock = DockStyle.Top
    }
        Dim lblTitle As New Label With {
        .Text = If(supplierId > 0, "Edit Supplier", "Add Supplier"),
        .Font = New Font("Segoe UI", 22, FontStyle.Bold),
        .ForeColor = MintDark, .AutoSize = True, .Location = New Point(24, 12)
    }
        Dim backPill As New Label With {
        .Text = ChrW(8592) & "   Back to list",
        .Font = New Font("Segoe UI Semibold", 10),
        .ForeColor = MintDeep, .BackColor = TileBg,
        .AutoSize = False, .Size = New Size(148, 32),
        .TextAlign = ContentAlignment.MiddleCenter,
        .Cursor = Cursors.Hand, .Location = New Point(26, 52)
    }
        RoundLabel(backPill, 8)
        AddHandler backPill.MouseEnter, Sub() backPill.BackColor = Color.FromArgb(210, 240, 235)
        AddHandler backPill.MouseLeave, Sub() backPill.BackColor = TileBg
        AddHandler backPill.Click, Sub() If editingId > 0 Then LoadDetail(editingId) Else RenderList()
        header.Controls.AddRange({lblTitle, backPill})

        ' ── Body fills remaining space ─────────────────────────────────────
        Dim body As New Panel With {
        .Dock = DockStyle.Fill,
        .BackColor = MintBg,
        .AutoScroll = False
    }

        ' ── Card ──────────────────────────────────────────────────────────
        Dim cardWidth As Integer = 560
        Dim card As New BufferedPanel With {
        .Width = cardWidth,
        .BackColor = CardBg
    }
        AddHandler card.Paint, Sub(s, e) PaintCard(CType(s, Panel), e)

        ' ── Fields ────────────────────────────────────────────────────────
        Dim yy As Integer = 28
       txtName = AddFormField(card, "Supplier Name *", yy, If(existing IsNot Nothing, existing.Name, ""), False, "e.g. ABC Paint Supplies") : yy += 80
txtContact = AddFormField(card, "Contact Person", yy, If(existing IsNot Nothing, existing.Contact, ""), False, "e.g. Juan dela Cruz") : yy += 80
txtPhone = AddFormField(card, "Phone", yy, If(existing IsNot Nothing, existing.Phone, ""), False, "e.g. 09171234567") : yy += 80
txtEmail = AddFormField(card, "Email", yy, If(existing IsNot Nothing, existing.Email, ""), False, "e.g. supplier@gmail.com") : yy += 80
        txtAddress = AddFormField(card, "Address", yy, If(existing IsNot Nothing, existing.Address, ""), True, "e.g. 123 Rizal St., Aparri, Cagayan") : yy += 80

        ' ── Button bar INSIDE card ─────────────────────────────────────────
        Dim btnBarH As Integer = 64
        Dim btnBarY As Integer = yy + 8

        Dim btnBar As New Panel With {
    .BackColor = Color.Transparent,
    .Location = New Point(0, btnBarY),
    .Height = btnBarH,
    .Width = cardWidth
}
        AddHandler btnBar.Paint,
    Sub(s, e)
        Using pen As New Pen(CardBorder, 1)
            e.Graphics.DrawLine(pen, 20, 0, CType(s, Panel).Width - 20, 0)
        End Using
    End Sub

        ' ── Cancel button ──────────────────────────────────────────────────
        Dim btnCancel As New Button With {
            .Text = "Cancel",
            .Size = New Size(110, 38),
            .FlatStyle = FlatStyle.Flat,
            .BackColor = Color.White,
            .ForeColor = SubText,
            .Font = New Font("Segoe UI Semibold", 10),
            .Cursor = Cursors.Hand
        }
        btnCancel.FlatAppearance.BorderSize = 1
        btnCancel.FlatAppearance.BorderColor = CardBorder
        AddHandler btnCancel.Paint,
            Sub(s, e)
                Dim b = CType(s, Button)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Using path As GraphicsPath = RoundRect(New Rectangle(1, 1, b.Width - 2, b.Height - 2), 8)
                    Using fill As New SolidBrush(b.BackColor)
                        e.Graphics.FillPath(fill, path)
                    End Using
                    Using pen As New Pen(CardBorder, 1.2F)
                        e.Graphics.DrawPath(pen, path)
                    End Using
                End Using
                Dim sf As New StringFormat With {
                    .Alignment = StringAlignment.Center,
                    .LineAlignment = StringAlignment.Center
                }
                Using brush As New SolidBrush(b.ForeColor)
                    e.Graphics.DrawString(b.Text, b.Font, brush,
                        New RectangleF(0, 0, b.Width, b.Height), sf)
                End Using
            End Sub
        AddHandler btnCancel.MouseEnter,
            Sub()
                btnCancel.BackColor = Color.FromArgb(244, 248, 246)
                btnCancel.Invalidate()
            End Sub
        AddHandler btnCancel.MouseLeave,
            Sub()
                btnCancel.BackColor = Color.White
                btnCancel.Invalidate()
            End Sub
        AddHandler btnCancel.Click,
            Sub() If editingId > 0 Then LoadDetail(editingId) Else RenderList()

        ' ── Save button ────────────────────────────────────────────────────
        btnSaveRef = New Button With {
            .Text = If(supplierId > 0, "Save Changes", "Add Supplier"),
            .Size = New Size(148, 38),
            .FlatStyle = FlatStyle.Flat,
            .BackColor = MintPrimary,
            .ForeColor = Color.White,
            .Font = New Font("Segoe UI Semibold", 10),
            .Cursor = Cursors.Hand,
            .Tag = ""
        }
        btnSaveRef.FlatAppearance.BorderSize = 0
        AddHandler btnSaveRef.Paint,
            Sub(s, e)
                Dim b = CType(s, Button)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                ' Fill full rectangle — no -1 offset that caused clipping
                Using path As GraphicsPath = RoundRect(New Rectangle(0, 0, b.Width, b.Height), 8)
                    Using fill As New SolidBrush(b.BackColor)
                        e.Graphics.FillPath(fill, path)
                    End Using
                End Using
                Dim sf As New StringFormat With {
                    .Alignment = StringAlignment.Center,
                    .LineAlignment = StringAlignment.Center
                }
                Using brush As New SolidBrush(b.ForeColor)
                    e.Graphics.DrawString(b.Text, b.Font, brush,
                        New RectangleF(0, 0, b.Width, b.Height), sf)
                End Using
            End Sub
        AddHandler btnSaveRef.MouseEnter,
            Sub()
                btnSaveRef.BackColor = Color.FromArgb(0, 150, 127)
                btnSaveRef.Invalidate()
            End Sub
        AddHandler btnSaveRef.MouseLeave,
            Sub()
                btnSaveRef.BackColor = MintPrimary
                btnSaveRef.Invalidate()
            End Sub
        AddHandler btnSaveRef.Click, Sub() CommitSupplier()

        btnBar.Controls.AddRange({btnCancel, btnSaveRef})

        ' Position buttons immediately (don't wait for first Resize event)
        Dim tInit As Integer = (btnBarH - btnSaveRef.Height) \ 2
        btnSaveRef.Top = tInit
        btnSaveRef.Left = btnBar.Width - 20 - btnSaveRef.Width
        btnCancel.Top = tInit
        btnCancel.Left = btnSaveRef.Left - btnCancel.Width - 10

        ' Keep right-aligned on resize
        AddHandler btnBar.Resize,
            Sub()
                Dim t As Integer = (btnBar.Height - btnSaveRef.Height) \ 2
                btnSaveRef.Top = t
                btnSaveRef.Left = btnBar.Width - 20 - btnSaveRef.Width
                btnCancel.Top = t
                btnCancel.Left = btnSaveRef.Left - btnCancel.Width - 10
            End Sub

        ' Card height: fields + gap + btnBar + extra for rounded bottom corners
        card.Height = btnBarY + btnBarH + 20   ' +20 instead of +12
        card.Controls.Add(btnBar)



        ' ── Centre card horizontally and vertically inside body ───────────
        '    TUNING TIP:
        '      Move UP  → decrease the offset, e.g. change `\ 2` to `\ 2 - 30`
        '      Move DOWN → increase the offset, e.g. change `\ 2` to `\ 2 + 30`
        Dim verticalOffset As Integer = -100   ' negative = up, positive = down

        Dim PositionCard As Action =
        Sub()
            Dim bw As Integer = body.ClientSize.Width
            Dim bh As Integer = body.ClientSize.Height
            card.Left = Math.Max(24, (bw - cardWidth) \ 2)
            card.Top = Math.Max(16, (bh - card.Height) \ 2 + verticalOffset)
        End Sub

        AddHandler body.Resize, Sub() PositionCard()
        body.Controls.Add(card)

        container.Controls.Add(body)
        container.Controls.Add(header)
        container.ResumeLayout()

        body.BeginInvoke(New Action(Sub() PositionCard()))
    End Sub

    ' Builds a labelled text box inside the card; returns the TextBox.
    Private Shared Function AddFormField(parent As Panel, label As String, y As Integer,
                                     value As String,
                                     Optional multiline As Boolean = False,
                                     Optional placeholder As String = "") As TextBox
        Dim lbl As New Label With {
        .Text = label,
        .Font = New Font("Segoe UI Semibold", 10),
        .ForeColor = SubText, .AutoSize = True,
        .Location = New Point(24, y), .BackColor = Color.Transparent
    }

        Dim tileH As Integer = If(multiline, 52, 46)
        Dim tile As New Panel With {
        .Location = New Point(24, y + 24), .Height = tileH,
        .BackColor = CardBg,
        .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
    }

        Dim txt As New TextBox With {
        .BorderStyle = BorderStyle.None, .BackColor = CardBg,
        .ForeColor = MintDark,
        .Font = New Font("Segoe UI", 11),
        .Text = If(value, ""),
        .Location = New Point(12, (tileH - 22) \ 2),
        .Multiline = multiline,
        .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
    }
        tile.Controls.Add(txt)

        ' ── Overlay placeholder label for ALL fields (single + multiline) ─────
        If placeholder <> "" Then
            Dim phLabel As New Label With {
            .Text = placeholder,
            .Font = New Font("Segoe UI", 10, FontStyle.Italic),
            .ForeColor = Color.FromArgb(180, 180, 180),
            .BackColor = Color.Transparent,
            .AutoSize = False,
            .Height = tileH,
            .Location = New Point(13, 0),
            .Cursor = Cursors.IBeam,
            .TextAlign = ContentAlignment.MiddleLeft
        }
            tile.Controls.Add(phLabel)
            phLabel.BringToFront()

            Dim UpdatePh As Action =
            Sub()
                phLabel.Visible = (txt.Text = "" AndAlso Not txt.Focused)
            End Sub

            AddHandler txt.TextChanged, Sub() UpdatePh()
            AddHandler txt.Enter, Sub() UpdatePh()
            AddHandler txt.Leave, Sub() UpdatePh()
            AddHandler phLabel.Click, Sub() txt.Focus()

            UpdatePh()

            AddHandler tile.Resize,
            Sub()
                phLabel.Width = tile.Width - 24
            End Sub
        End If

        ' ── Border painting ───────────────────────────────────────────────────
        AddHandler tile.Paint,
        Sub(s, e)
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
            Dim rect = tile.ClientRectangle
            rect.Inflate(-1, -1)
            Using path As GraphicsPath = RoundRect(rect, 10)
                Using fill As New SolidBrush(CardBg)
                    e.Graphics.FillPath(fill, path)
                End Using
                Using pen As New Pen(If(txt.Focused,
                                        Color.FromArgb(0, 168, 142),
                                        Color.FromArgb(214, 224, 220)),
                                     If(txt.Focused, 1.8F, 1.2F))
                    e.Graphics.DrawPath(pen, path)
                End Using
            End Using
        End Sub

        AddHandler txt.Enter, Sub() tile.Invalidate()
        AddHandler txt.Leave, Sub() tile.Invalidate()
        AddHandler txt.TextChanged, Sub() tile.Invalidate()

        AddHandler tile.Resize,
        Sub()
            txt.Width = tile.Width - 24
            If multiline Then txt.Height = tile.Height - 16
        End Sub
        AddHandler parent.Resize,
        Sub()
            tile.Width = parent.Width - 48
        End Sub

        tile.Width = Math.Max(200, parent.Width - 48)
        txt.Width = tile.Width - 24

        parent.Controls.AddRange({lbl, tile})
        Return txt
    End Function

    Private Shared Sub CommitSupplier()
        If btnSaveRef IsNot Nothing AndAlso CStr(If(btnSaveRef.Tag, "")) = "done" Then Return

        ' ── Validation: all fields required ──────────────────────────────────
        Dim name As String = txtName.Text.Trim()
        Dim contact As String = txtContact.Text.Trim()
        Dim phone As String = txtPhone.Text.Trim()
        Dim email As String = txtEmail.Text.Trim()
        Dim address As String = txtAddress.Text.Trim()

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
        If Not System.Text.RegularExpressions.Regex.IsMatch(email,
            "^[^@\s]+@[^@\s]+\.[^@\s]+$") Then
            AppModal.Show("Email must be a valid address (e.g. supplier@gmail.com).", "Invalid Email",
                      MessageBoxButtons.OK, MessageBoxIcon.Warning)
            txtEmail.Focus() : Return
        End If
        If address = "" Then
            AppModal.Show("Address is required.", "Missing Field",
                      MessageBoxButtons.OK, MessageBoxIcon.Warning)
            txtAddress.Focus() : Return
        End If

        ' ── Persist ──────────────────────────────────────────────────────────
        btnSaveRef.Tag = "done"
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                If editingId > 0 Then
                    Using cmd As New MySqlCommand(
                    "UPDATE suppliers SET name = @n, contact_person = @c, phone = @p, " &
                    "email = @e, address = @a WHERE supplier_id = @id", conn)
                        cmd.Parameters.AddWithValue("@n", name)
                        cmd.Parameters.AddWithValue("@c", contact)
                        cmd.Parameters.AddWithValue("@p", phone)
                        cmd.Parameters.AddWithValue("@e", email)
                        cmd.Parameters.AddWithValue("@a", address)
                        cmd.Parameters.AddWithValue("@id", editingId)
                        cmd.ExecuteNonQuery()
                    End Using
                Else
                    Using cmd As New MySqlCommand(
                    "INSERT INTO suppliers (name, contact_person, phone, email, address, is_active) " &
                    "VALUES (@n, @c, @p, @e, @a, 1)", conn)
                        cmd.Parameters.AddWithValue("@n", name)
                        cmd.Parameters.AddWithValue("@c", contact)
                        cmd.Parameters.AddWithValue("@p", phone)
                        cmd.Parameters.AddWithValue("@e", email)
                        cmd.Parameters.AddWithValue("@a", address)
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

        ' ── Scrollable body ───────────────────────────────────────────────────
        Dim body As New Panel With {
        .Dock = DockStyle.Fill, .BackColor = MintBg,
        .Padding = New Padding(24, 4, 24, 20), .AutoScroll = True
    }

        ' ── PO history card ───────────────────────────────────────────────────
        Dim histHeight As Integer = 60 + Math.Max(1, history.Count) * 54 + 12
        Dim histCard As New BufferedPanel With {
        .Height = histHeight, .BackColor = CardBg, .Dock = DockStyle.Top
    }
        AddHandler histCard.Paint, Sub(s, e) PaintCard(CType(s, Panel), e)
        Dim lblHist As New Label With {
        .Text = "Recent Purchase Orders",
        .Font = New Font("Segoe UI", 13, FontStyle.Bold),
        .ForeColor = MintDark, .AutoSize = True,
        .Location = New Point(20, 14), .BackColor = Color.Transparent
    }
        histCard.Controls.Add(lblHist)
        If history.Count = 0 Then
            histCard.Controls.Add(New Label With {
            .Text = "No purchase orders for this supplier yet.",
            .Font = New Font("Segoe UI", 10), .ForeColor = SubText,
            .AutoSize = True, .Location = New Point(20, 52),
            .BackColor = Color.Transparent
        })
        Else
            Dim yh As Integer = 52
            For Each h In history
                Dim r As Panel = BuildHistoryRow(h, 54)
                r.Location = New Point(0, yh)
                r.Width = histCard.ClientSize.Width - 4
                r.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
                histCard.Controls.Add(r)
                yh += 54
            Next
            AddHandler histCard.Resize,
            Sub()
                Dim w As Integer = histCard.ClientSize.Width - 4
                For Each c As Control In histCard.Controls
                    If TypeOf c Is Panel Then c.Width = w
                Next
            End Sub
        End If

        Dim spacer2 As New Panel With {
        .Height = 14, .Dock = DockStyle.Top, .BackColor = Color.Transparent
    }

        ' ── Products chips card ───────────────────────────────────────────────
        Dim chipsCard As New BufferedPanel With {
        .Height = 120, .BackColor = CardBg, .Dock = DockStyle.Top
    }
        AddHandler chipsCard.Paint, Sub(s, e) PaintCard(CType(s, Panel), e)
        chipsCard.Controls.Add(New Label With {
        .Text = "Products Supplied",
        .Font = New Font("Segoe UI", 13, FontStyle.Bold),
        .ForeColor = MintDark, .AutoSize = True,
        .Location = New Point(20, 14), .BackColor = Color.Transparent
    })
        Dim chipFlow As New FlowLayoutPanel With {
        .Location = New Point(18, 46), .AutoSize = False, .Height = 60,
        .BackColor = Color.Transparent, .WrapContents = True,
        .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top,
        .AutoScroll = True
    }
        If products.Count = 0 Then
            chipFlow.Controls.Add(New Label With {
            .Text = "No products assigned to this supplier.",
            .Font = New Font("Segoe UI", 10), .ForeColor = SubText,
            .AutoSize = True, .Margin = New Padding(2, 6, 0, 0)
        })
        Else
            For Each p In products
                chipFlow.Controls.Add(MakeChip(p))
            Next
        End If
        chipsCard.Controls.Add(chipFlow)
        AddHandler chipsCard.Resize, Sub() chipFlow.Width = chipsCard.Width - 36

        Dim spacer1 As New Panel With {
        .Height = 14, .Dock = DockStyle.Top, .BackColor = Color.Transparent
    }

        ' ── Contact info card ─────────────────────────────────────────────────
        '    Height: name(40) + 4 rows×28 + pill row + action bar(56) + padding
        Dim infoCard As New BufferedPanel With {
        .Height = 260, .BackColor = CardBg, .Dock = DockStyle.Top
    }
        AddHandler infoCard.Paint, Sub(s, e) PaintCard(CType(s, Panel), e)

        ' Supplier name + active PO pill (same row)
        Dim lblSupName As New Label With {
        .Text = sup.Name,
      .Font = New Font("Segoe UI", 20, FontStyle.Bold),
        .ForeColor = MintDark, .AutoSize = True,
        .Location = New Point(22, 18), .BackColor = Color.Transparent
    }
        infoCard.Controls.Add(lblSupName)

        Dim pill As New Label With {
        .Text = sup.ActivePoCount & If(sup.ActivePoCount = 1, " active PO", " active POs"),
        .Font = New Font("Segoe UI", 9, FontStyle.Bold),
        .ForeColor = If(sup.ActivePoCount > 0, MintDeep, FaintText),
        .BackColor = If(sup.ActivePoCount > 0, TileBg, Color.FromArgb(238, 240, 239)),
        .AutoSize = True, .Padding = New Padding(10, 4, 10, 4),
        .Anchor = AnchorStyles.Top Or AnchorStyles.Right
    }
        RoundLabel(pill, 11)
        infoCard.Controls.Add(pill)
        AddHandler infoCard.Resize,
        Sub()
            pill.Left = infoCard.Width - pill.Width - 22
            pill.Top = 22
        End Sub

        ' Detail rows
        Dim detailRowY As Integer = 56
        Dim detailRows As New List(Of (lbl As String, val As String)) From {
        ("Contact Person", If(String.IsNullOrWhiteSpace(sup.Contact), ChrW(8212), sup.Contact)),
        ("Phone", If(String.IsNullOrWhiteSpace(sup.Phone), ChrW(8212), sup.Phone)),
        ("Email", If(String.IsNullOrWhiteSpace(sup.Email), ChrW(8212), sup.Email)),
        ("Address", If(String.IsNullOrWhiteSpace(sup.Address), ChrW(8212), sup.Address))
    }
        For Each dr In detailRows
            AddDetailRow(infoCard, dr.lbl, dr.val, detailRowY)
            detailRowY += 34
        Next

        ' ── Divider line ──────────────────────────────────────────────────────
        Dim actionBarY As Integer = detailRowY + 12
        Dim actionBar As New Panel With {
        .BackColor = Color.Transparent,
        .Location = New Point(0, actionBarY),
        .Height = 56,
        .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
    }
        AddHandler actionBar.Paint,
        Sub(s, e)
            Using pen As New Pen(CardBorder, 1)
                e.Graphics.DrawLine(pen, 20, 0, CType(s, Panel).Width - 20, 0)
            End Using
        End Sub
        AddHandler infoCard.Resize, Sub() actionBar.Width = infoCard.Width

        ' ── Edit button ───────────────────────────────────────────────────────
        Dim btnEdit As New Button With {
        .Text = "✎  Edit",
        .Size = New Size(100, 36),
        .FlatStyle = FlatStyle.Flat,
        .BackColor = MintPrimary,
        .ForeColor = Color.White,
        .Font = New Font("Segoe UI Semibold", 10),
        .Cursor = Cursors.Hand
    }
        btnEdit.FlatAppearance.BorderSize = 0
        RoundControl(btnEdit, 8)
        Dim capId As Integer = supplierId
        AddHandler btnEdit.Click, Sub() LoadForm(capId)

        ' ── Delete button ─────────────────────────────────────────────────────
        Dim btnDelete As New Button With {
        .Text = "🗑  Delete",
        .Size = New Size(110, 36),
        .FlatStyle = FlatStyle.Flat,
        .BackColor = Color.White,
        .ForeColor = NegativeClr,
        .Font = New Font("Segoe UI Semibold", 10),
        .Cursor = Cursors.Hand
    }
        btnDelete.FlatAppearance.BorderSize = 1
        btnDelete.FlatAppearance.BorderColor = Color.FromArgb(220, 180, 180)
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
            Dim sf As New StringFormat With {
                .Alignment = StringAlignment.Center,
                .LineAlignment = StringAlignment.Center
            }
            Using brush As New SolidBrush(b.ForeColor)
                e.Graphics.DrawString(b.Text, b.Font, brush,
                    New RectangleF(0, 0, b.Width, b.Height), sf)
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
            Dim confirm = AppModal.Show(
                "Are you sure you want to delete """ & sup.Name & """?" &
                Environment.NewLine & "This cannot be undone.",
                "Delete Supplier",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
            If confirm = DialogResult.Yes Then
                Try
                    Using conn = Db.GetConnection()
                        conn.Open()
                        Using cmd As New MySqlCommand(
                            "DELETE FROM suppliers WHERE supplier_id = @id", conn)
                            cmd.Parameters.AddWithValue("@id", supplierId)
                            cmd.ExecuteNonQuery()
                        End Using
                    End Using
                    AppModal.Show("Supplier deleted.", "Deleted",
                        MessageBoxButtons.OK, MessageBoxIcon.Information)
                    RenderList()
                Catch ex As Exception
                    AppModal.Show("Could not delete supplier:" &
                        Environment.NewLine & ex.Message,
                        "Database Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error)
                End Try
            End If
        End Sub

        actionBar.Controls.AddRange({btnEdit, btnDelete})

        ' Position action buttons right-aligned, vertically centred
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

        infoCard.Height = actionBarY + actionBar.Height + 24
        infoCard.Controls.Add(actionBar)

        body.Controls.Add(histCard)
        body.Controls.Add(spacer2)
        body.Controls.Add(chipsCard)
        body.Controls.Add(spacer1)
        body.Controls.Add(infoCard)

        ' ── Header: back pill + title (NO Edit Supplier button here) ─────────
        Dim header As New Panel With {
        .Height = 88, .BackColor = MintBg,
        .Padding = New Padding(24, 18, 24, 0), .Dock = DockStyle.Top
    }
        Dim lblTitle As New Label With {
        .Text = "Supplier",
        .Font = New Font("Segoe UI", 22, FontStyle.Bold),
        .ForeColor = MintDark, .AutoSize = True,
        .Location = New Point(24, 12)
    }
        Dim backPill As New Label With {
        .Text = ChrW(8592) & "   Back to list",
        .Font = New Font("Segoe UI Semibold", 10),
        .ForeColor = MintDeep, .BackColor = TileBg,
        .AutoSize = False, .Size = New Size(148, 32),
        .TextAlign = ContentAlignment.MiddleCenter,
        .Cursor = Cursors.Hand, .Location = New Point(26, 52)
    }
        RoundLabel(backPill, 8)
        AddHandler backPill.MouseEnter, Sub() backPill.BackColor = Color.FromArgb(210, 240, 235)
        AddHandler backPill.MouseLeave, Sub() backPill.BackColor = TileBg
        AddHandler backPill.Click, Sub() RenderList()
        header.Controls.AddRange({lblTitle, backPill})

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
        Dim row As New Panel With {.Height = height, .BackColor = Color.Transparent}
        AddHandler row.Paint,
            Sub(s, e)
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 20, row.Height - 1, row.Width - 20, row.Height - 1)
                End Using
            End Sub

        Dim lblPo As New Label With {
    .Text = h.PoNo, .Font = New Font("Segoe UI", 11, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(20, 6), .BackColor = Color.Transparent
        }
        Dim lblMeta As New Label With {
    .Text = h.OrderDate.ToString("MMM d, yyyy") & "   " & ChrW(8226) & "   " &
            h.ItemCount & If(h.ItemCount = 1, " item", " items"),
    .Font = New Font("Segoe UI", 10), .ForeColor = SubText, .AutoSize = True,
            .Location = New Point(20, 26), .BackColor = Color.Transparent
        }
        row.Controls.AddRange({lblPo, lblMeta})

        Dim st = StatusInfo(h.Status)
        Dim badge As New Label With {
            .Text = st.text, .Font = New Font("Segoe UI", 8, FontStyle.Bold),
            .ForeColor = st.fore, .BackColor = st.back, .AutoSize = True,
            .Padding = New Padding(8, 3, 8, 3), .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        RoundLabel(badge, 10)
        Dim lblTotal As New Label With {
            .Text = Peso & h.TotalCost.ToString("N2"), .Font = New Font("Segoe UI", 10.5F, FontStyle.Bold),
            .ForeColor = MintDeep, .AutoSize = True, .BackColor = Color.Transparent,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        row.Controls.AddRange({badge, lblTotal})

        AddHandler row.Resize,
            Sub()
                lblTotal.Left = row.Width - lblTotal.Width - 24
                lblTotal.Top = (height - lblTotal.Height) \ 2
                badge.Left = lblTotal.Left - badge.Width - 16
                badge.Top = (height - badge.Height) \ 2
            End Sub

        Return row
    End Function

    Private Shared Function MakeChip(text As String) As Label
        Dim chip As New Label With {
            .Text = text, .Font = New Font("Segoe UI", 9),
            .ForeColor = MintDeep, .BackColor = TileBg, .AutoSize = True,
            .Padding = New Padding(10, 5, 10, 5), .Margin = New Padding(0, 0, 8, 8)
        }
        RoundLabel(chip, 12)
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
            ' Non-fatal for the detail view
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
            ' Non-fatal for the detail view
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
    '  SHARED PAINT / CONTROL HELPERS  (verbatim from InventoryLowStockPanelBuilder)
    ' =========================================================================
    Private Shared Sub PaintCard(panel As Panel, e As PaintEventArgs)
        PanelRenderer.Draw(panel, e, True)
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

    <System.Runtime.InteropServices.DllImport("user32.dll", CharSet:=System.Runtime.InteropServices.CharSet.Unicode)>
    Private Shared Function SendMessage(hWnd As IntPtr, msg As Integer, wParam As IntPtr, lParam As String) As IntPtr
    End Function
    Private Const EM_SETCUEBANNER As Integer = &H1501

    Private Sub ProcurementSuppliersPanelBuilder_Load(sender As Object, e As EventArgs) Handles MyBase.Load

    End Sub
End Class