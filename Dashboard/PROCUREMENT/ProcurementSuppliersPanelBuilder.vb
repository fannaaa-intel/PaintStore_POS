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
' =============================================================================
Public Class ProcurementSuppliersPanelBuilder

    ' ── PALETTE ──────────────────────────────────────────────────────────────────
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

    Private Shared editingId As Integer = 0
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

        region.Controls.AddRange({lblTitle, lblSub, btnAdd})

        AddHandler region.Resize,
            Sub()
                btnAdd.Top = 22
                btnAdd.Left = region.Width - 24 - btnAdd.Width
            End Sub
        btnAdd.Top = 22
        btnAdd.Left = 700

        Return region
    End Function

    ' ── SUPPLIER CARD (REDESIGNED) ────────────────────────────────────────────────
    '   • PanelRenderer for card shadow/border
    '   • Gradient-initial avatar on the left
    '   • Mint accent bar along the left edge (clipped)
    '   • Name / contact line / address line (darker, clearly readable)
    '   • PO pill top-right, chevron icon circle pill bottom-right
    '   • Subtle hover tint
    Private Shared Function BuildSupplierCard(sup As SupplierRow) As Panel
        Dim cardH As Integer = 148
        Dim card As New BufferedPanel With {
            .Height = cardH, .BackColor = CardBg, .Cursor = Cursors.Hand
        }

        ' ── Paint: PanelRenderer + clipped left accent bar ───────────────────────
        AddHandler card.Paint,
            Sub(s, e)
                PaintCard(CType(s, Panel), e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                ' Clip to card rounded rect so accent doesn't bleed into corners
                Dim clipRect As New Rectangle(1, 1, card.Width - 2, card.Height - 2)
                Using clipPath As GraphicsPath = RoundRect(clipRect, 14)
                    e.Graphics.SetClip(clipPath)
                    Dim aY As Integer = 14
                    Dim aH As Integer = cardH - 28
                    Using br As New LinearGradientBrush(
                        New Point(1, aY), New Point(1, aY + aH),
                        MintPrimary, MintDeep)
                        e.Graphics.FillRectangle(br, New Rectangle(1, aY, 5, aH))
                    End Using
                    e.Graphics.ResetClip()
                End Using
            End Sub

        ' ── Avatar bubble ────────────────────────────────────────────────────────
        Dim initial As String = If(String.IsNullOrWhiteSpace(sup.Name), "?",
                                   sup.Name.Trim().Substring(0, 1).ToUpperInvariant())
        Dim avSize As Integer = 52
        Dim avatar As New BufferedPanel With {
            .Size = New Size(avSize, avSize),
            .Location = New Point(22, (cardH - avSize) \ 2),
            .BackColor = Color.Transparent
        }
        AddHandler avatar.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim av As New Rectangle(0, 0, avSize - 1, avSize - 1)
                Using path As GraphicsPath = RoundRect(av, 14)
                    Using br As New LinearGradientBrush(av, MintPrimary, MintDeep, 135.0F)
                        e.Graphics.FillPath(br, path)
                    End Using
                End Using
                Using f As New Font("Segoe UI", 20, FontStyle.Bold),
                      br As New SolidBrush(Color.White)
                    Dim sz As SizeF = e.Graphics.MeasureString(initial, f)
                    e.Graphics.DrawString(initial, f, br,
                        (avSize - sz.Width) / 2.0F - 0.5F,
                        (avSize - sz.Height) / 2.0F)
                End Using
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
            .ForeColor = If(sup.ActivePoCount > 0, Color.White, Color.FromArgb(75, 92, 87)),
            .BackColor = If(sup.ActivePoCount > 0, MintDeep, Color.FromArgb(208, 218, 214)),
            .AutoSize = True, .Padding = New Padding(13, 6, 13, 6),
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        RoundLabel(poPill, 12)
        card.Controls.Add(poPill)

        ' ── "View >" pill — thick drawn chevron (bottom-right) ───────────────────
        Dim vpH As Integer = 44
        Dim vpW As Integer = 110
        Dim viewPill As New BufferedPanel With {
            .Size = New Size(vpW, vpH),
            .BackColor = TileBg,
            .Cursor = Cursors.Hand,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }

        ' Pill-shaped region (fully rounded ends)
        Dim vpRgn As New GraphicsPath()
        vpRgn.AddArc(0, 0, vpH, vpH, 180, 90)
        vpRgn.AddArc(vpW - vpH, 0, vpH, vpH, 270, 90)
        vpRgn.AddArc(vpW - vpH, 0, vpH, vpH, 0, 90)
        vpRgn.AddArc(0, 0, vpH, vpH, 90, 90)
        vpRgn.CloseFigure()
        viewPill.Region = New Region(vpRgn)

        AddHandler viewPill.Paint,
            Sub(s, e)
                Dim b As BufferedPanel = CType(s, BufferedPanel)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                ' Pill fill
                Using path As GraphicsPath = RoundRect(New Rectangle(0, 0, b.Width - 1, b.Height - 1), vpH \ 2)
                    Using br As New SolidBrush(b.BackColor)
                        e.Graphics.FillPath(br, path)
                    End Using
                End Using
                ' "View" text
                Using f As New Font("Segoe UI Semibold", 13.0F, FontStyle.Bold),
                      br As New SolidBrush(MintDeep)
                    Dim sf As New StringFormat With {
                        .Alignment = StringAlignment.Near,
                        .LineAlignment = StringAlignment.Center
                    }
                    e.Graphics.DrawString("View", f, br,
                        New RectangleF(16, 0, 56, b.Height), sf)
                End Using
                ' Thick ">" chevron — rounded pen so it matches icon style
                Dim chX As Single = b.Width - 24
                Dim midY As Single = b.Height / 2.0F
                Dim arm As Single = 6
                Using pen As New Pen(MintDeep, 3.0F)
                    pen.LineJoin = LineJoin.Round
                    pen.StartCap = LineCap.Round
                    pen.EndCap = LineCap.Round
                    e.Graphics.DrawLines(pen, New PointF() {
                        New PointF(chX, midY - arm),
                        New PointF(chX + arm * 0.7F, midY),
                        New PointF(chX, midY + arm)
                    })
                End Using
            End Sub

        AddHandler viewPill.MouseEnter,
            Sub()
                viewPill.BackColor = Color.FromArgb(180, 230, 218)
                viewPill.Invalidate()
            End Sub
        AddHandler viewPill.MouseLeave,
            Sub()
                viewPill.BackColor = TileBg
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
        Dim header As New Panel With {
            .Height = 80, .BackColor = MintBg,
            .Padding = New Padding(0, 0, 24, 0), .Dock = DockStyle.Top
        }
        Dim iconPathF As String = IO.Path.Combine(Application.StartupPath, "previous.png")
        Dim picBackF As New PictureBox With {
            .Size = New Size(22, 22), .Location = New Point(24, 36),
            .BackColor = Color.Transparent, .SizeMode = PictureBoxSizeMode.Zoom, .Cursor = Cursors.Hand
        }
        If IO.File.Exists(iconPathF) Then
            Try
                Dim raw As Image = Image.FromFile(iconPathF)
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
                picBackF.Image = bmp
                raw.Dispose()
            Catch
            End Try
        End If
        AddHandler picBackF.Click, Sub() If editingId > 0 Then LoadDetail(editingId) Else RenderList()
        AddHandler picBackF.MouseEnter, Sub() picBackF.BackColor = Color.FromArgb(30, 0, 130, 109)
        AddHandler picBackF.MouseLeave, Sub() picBackF.BackColor = Color.Transparent
        Dim lblTitle As New Label With {
            .Text = If(supplierId > 0, "Edit Supplier", "Add Supplier"),
            .Font = New Font("Segoe UI", 22, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True,
            .Location = New Point(picBackF.Right + 10, 24), .BackColor = Color.Transparent
        }
        header.Controls.AddRange({picBackF, lblTitle})

        ' ── Body fills remaining space ─────────────────────────────────────────
        Dim body As New Panel With {
            .Dock = DockStyle.Fill, .BackColor = MintBg, .AutoScroll = False
        }

        Dim cardWidth As Integer = 560
        Dim card As New BufferedPanel With {.Width = cardWidth, .BackColor = CardBg}
        AddHandler card.Paint, Sub(s, e) PaintCard(CType(s, Panel), e)

        Dim yy As Integer = 28
        txtName = AddFormField(card, "Supplier Name *", yy, If(existing IsNot Nothing, existing.Name, ""), False, "e.g. ABC Paint Supplies") : yy += 80
        txtContact = AddFormField(card, "Contact Person", yy, If(existing IsNot Nothing, existing.Contact, ""), False, "e.g. Juan dela Cruz") : yy += 80
        txtPhone = AddFormField(card, "Phone", yy, If(existing IsNot Nothing, existing.Phone, ""), False, "e.g. 09171234567") : yy += 80
        txtEmail = AddFormField(card, "Email", yy, If(existing IsNot Nothing, existing.Email, ""), False, "e.g. supplier@gmail.com") : yy += 80
        txtAddress = AddFormField(card, "Address", yy, If(existing IsNot Nothing, existing.Address, ""), True, "e.g. 123 Rizal St., Aparri, Cagayan") : yy += 80

        Dim btnBarH As Integer = 64
        Dim btnBarY As Integer = yy + 8

        Dim btnBar As New Panel With {
            .BackColor = Color.Transparent, .Location = New Point(0, btnBarY),
            .Height = btnBarH, .Width = cardWidth
        }
        AddHandler btnBar.Paint,
            Sub(s, e)
                Using pen As New Pen(CardBorder, 1)
                    e.Graphics.DrawLine(pen, 20, 0, CType(s, Panel).Width - 20, 0)
                End Using
            End Sub

        Dim btnCancel As New Button With {
            .Text = "Cancel", .Size = New Size(110, 38),
            .FlatStyle = FlatStyle.Flat, .BackColor = Color.White,
            .ForeColor = SubText, .Font = New Font("Segoe UI Semibold", 10), .Cursor = Cursors.Hand
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
                    .Alignment = StringAlignment.Center, .LineAlignment = StringAlignment.Center
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
        AddHandler btnCancel.Click, Sub() If editingId > 0 Then LoadDetail(editingId) Else RenderList()

        btnSaveRef = New Button With {
            .Text = If(supplierId > 0, "Save Changes", "Add Supplier"),
            .Size = New Size(148, 38), .FlatStyle = FlatStyle.Flat,
            .BackColor = MintPrimary, .ForeColor = Color.White,
            .Font = New Font("Segoe UI Semibold", 10), .Cursor = Cursors.Hand, .Tag = ""
        }
        btnSaveRef.FlatAppearance.BorderSize = 0
        AddHandler btnSaveRef.Paint,
            Sub(s, e)
                Dim b = CType(s, Button)
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

        Dim tInit As Integer = (btnBarH - btnSaveRef.Height) \ 2
        btnSaveRef.Top = tInit
        btnSaveRef.Left = btnBar.Width - 20 - btnSaveRef.Width
        btnCancel.Top = tInit
        btnCancel.Left = btnSaveRef.Left - btnCancel.Width - 10

        AddHandler btnBar.Resize,
            Sub()
                Dim t As Integer = (btnBar.Height - btnSaveRef.Height) \ 2
                btnSaveRef.Top = t
                btnSaveRef.Left = btnBar.Width - 20 - btnSaveRef.Width
                btnCancel.Top = t
                btnCancel.Left = btnSaveRef.Left - btnCancel.Width - 10
            End Sub

        card.Height = btnBarY + btnBarH + 20
        card.Controls.Add(btnBar)

        Dim verticalOffset As Integer = -100

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

    Private Shared Function AddFormField(parent As Panel, label As String, y As Integer,
                                         value As String,
                                         Optional multiline As Boolean = False,
                                         Optional placeholder As String = "") As TextBox
        Dim lbl As New Label With {
            .Text = label, .Font = New Font("Segoe UI Semibold", 10),
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
            .ForeColor = MintDark, .Font = New Font("Segoe UI", 11),
            .Text = If(value, ""), .Location = New Point(12, (tileH - 22) \ 2),
            .Multiline = multiline,
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        }
        tile.Controls.Add(txt)

        If placeholder <> "" Then
            Dim phLabel As New Label With {
                .Text = placeholder, .Font = New Font("Segoe UI", 10, FontStyle.Italic),
                .ForeColor = Color.FromArgb(180, 180, 180), .BackColor = Color.Transparent,
                .AutoSize = False, .Height = tileH, .Location = New Point(13, 0),
                .Cursor = Cursors.IBeam, .TextAlign = ContentAlignment.MiddleLeft
            }
            tile.Controls.Add(phLabel)
            phLabel.BringToFront()

            Dim UpdatePh As Action = Sub() phLabel.Visible = (txt.Text = "" AndAlso Not txt.Focused)

            AddHandler txt.TextChanged, Sub() UpdatePh()
            AddHandler txt.Enter, Sub() UpdatePh()
            AddHandler txt.Leave, Sub() UpdatePh()
            AddHandler phLabel.Click, Sub() txt.Focus()
            UpdatePh()

            AddHandler tile.Resize, Sub() phLabel.Width = tile.Width - 24
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
        AddHandler parent.Resize, Sub() tile.Width = parent.Width - 48

        tile.Width = Math.Max(200, parent.Width - 48)
        txt.Width = tile.Width - 24

        parent.Controls.AddRange({lbl, tile})
        Return txt
    End Function

    Private Shared Sub CommitSupplier()
        If btnSaveRef IsNot Nothing AndAlso CStr(If(btnSaveRef.Tag, "")) = "done" Then Return

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

        Dim body As New Panel With {
            .Dock = DockStyle.Fill, .BackColor = MintBg,
            .Padding = New Padding(24, 4, 24, 20), .AutoScroll = True
        }

        ' ── PO history card ───────────────────────────────────────────────────

        Dim histHeight As Integer = 68 + Math.Max(1, history.Count) * 58 + 12
        Dim histCard As New BufferedPanel With {
            .Height = histHeight, .BackColor = CardBg, .Dock = DockStyle.Top
        }
        AddHandler histCard.Paint, Sub(s, e) PaintCard(CType(s, Panel), e)

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
            .ForeColor = If(history.Count > 0, MintDeep, FaintText),
            .BackColor = If(history.Count > 0, TileBg, Color.FromArgb(236, 238, 237)),
            .AutoSize = True, .Padding = New Padding(10, 4, 10, 4)
        }
        RoundLabel(histCountPill, 10)
        histCard.Controls.AddRange({lblHist, histCountPill})

        ' Status badge of most recent order — pinned TOP-RIGHT of the card
        Dim histBadge As Label = Nothing
        If history.Count > 0 Then
            Dim badgeFore As Color = Color.White
            Dim badgeBack As Color
            Select Case history(0).Status
                Case "Ordered" : badgeBack = MintDeep
                Case "Draft" : badgeBack = Color.FromArgb(115, 130, 126)
                Case "Partially Received" : badgeBack = WarningClr
                Case "Received" : badgeBack = PositiveClr
                Case "Cancelled" : badgeBack = NegativeClr
                Case Else : badgeBack = SubText
            End Select
            histBadge = New Label With {
                .Text = StatusInfo(history(0).Status).text,
                .Font = New Font("Segoe UI Semibold", 11.0F, FontStyle.Bold),
                .ForeColor = badgeFore,
                .BackColor = badgeBack,
                .AutoSize = True, .Padding = New Padding(14, 6, 14, 6),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Right
            }
            RoundLabel(histBadge, 12)
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

        Dim spacer2 As New Panel With {.Height = 14, .Dock = DockStyle.Top, .BackColor = Color.Transparent}

        ' ── Products chips card ──────────────────────────────────────────────────
        Dim chipsCard As New BufferedPanel With {
            .Height = 130, .BackColor = CardBg, .Dock = DockStyle.Top
        }
        AddHandler chipsCard.Paint, Sub(s, e) PaintCard(CType(s, Panel), e)

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
            .ForeColor = If(products.Count > 0, MintDeep, FaintText),
            .BackColor = If(products.Count > 0, TileBg, Color.FromArgb(236, 238, 237)),
            .AutoSize = True, .Padding = New Padding(10, 4, 10, 4)
        }
        RoundLabel(chipsCountPill, 10)
        chipsCard.Controls.AddRange({lblChipsTitle, chipsCountPill})
        AddHandler chipsCard.Resize,
    Sub()
        chipsCountPill.Left = lblChipsTitle.Right + 10
        chipsCountPill.Top = lblChipsTitle.Top + (lblChipsTitle.PreferredHeight - chipsCountPill.PreferredHeight) \ 2 + 2
    End Sub

        Dim chipFlow As New FlowLayoutPanel With {
            .Location = New Point(18, 50), .AutoSize = False, .Height = 64,
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

        Dim initial As String = If(String.IsNullOrWhiteSpace(sup.Name), "?",
                                   sup.Name.Trim().Substring(0, 1).ToUpperInvariant())
        Dim avatarSize As Integer = 56
        Dim avatar As New BufferedPanel With {
            .Size = New Size(avatarSize, avatarSize),
            .Location = New Point(22, 20), .BackColor = Color.Transparent
        }
        AddHandler avatar.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim av As New Rectangle(0, 0, avatarSize - 1, avatarSize - 1)
                Using path As GraphicsPath = RoundRect(av, 16)
                    Using br As New LinearGradientBrush(av, MintPrimary, MintDeep, 90.0F)
                        e.Graphics.FillPath(br, path)
                    End Using
                End Using
                Using f As New Font("Segoe UI", 22, FontStyle.Bold),
                      br As New SolidBrush(Color.White)
                    Dim sz = e.Graphics.MeasureString(initial, f)
                    e.Graphics.DrawString(initial, f, br,
                        (avatarSize - sz.Width) / 2.0F,
                        (avatarSize - sz.Height) / 2.0F)
                End Using
            End Sub
        infoCard.Controls.Add(avatar)

        Dim lblSupName As New Label With {
            .Text = sup.Name,
            .Font = New Font("Segoe UI", 18, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True,
            .Location = New Point(avatarSize + 34, 22), .BackColor = Color.Transparent
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
                pill.Top = 26
            End Sub

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

        Dim fieldDefs As New List(Of (img As Image, lbl As String, val As String)) From {
            (TintIcon("user.png"), "Contact Person", If(String.IsNullOrWhiteSpace(sup.Contact), ChrW(8212), sup.Contact)),
            (TintIcon("phone.png"), "Phone", If(String.IsNullOrWhiteSpace(sup.Phone), ChrW(8212), sup.Phone)),
            (TintIcon("mail.png"), "Email", If(String.IsNullOrWhiteSpace(sup.Email), ChrW(8212), sup.Email)),
            (TintIcon("location.png"), "Address", If(String.IsNullOrWhiteSpace(sup.Address), ChrW(8212), sup.Address))
        }

        Dim yf As Integer = fieldStartY
        For Each fd In fieldDefs
            Dim iconTile As New BufferedPanel With {
                .Size = New Size(34, 34), .Location = New Point(22, yf), .BackColor = TileBg
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
                .Location = New Point(66, yf + 2), .BackColor = Color.Transparent
            }
            Dim lblFieldVal As New Label With {
                .Text = fd.val, .Font = New Font("Segoe UI", 11, FontStyle.Bold),
                .ForeColor = MintDark, .AutoSize = True,
                .Location = New Point(66, yf + 18), .BackColor = Color.Transparent
            }
            infoCard.Controls.AddRange({iconTile, lblFieldCap, lblFieldVal})
            yf += fieldGap
        Next

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
                Dim confirm = AppModal.Show(
                    "Are you sure you want to delete """ & sup.Name & """?" &
                    Environment.NewLine & "This cannot be undone.",
                    "Delete Supplier", MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
                If confirm = DialogResult.Yes Then
                    Try
                        Using conn = Db.GetConnection()
                            conn.Open()
                            Using cmd As New MySqlCommand("DELETE FROM suppliers WHERE supplier_id = @id", conn)
                                cmd.Parameters.AddWithValue("@id", supplierId)
                                cmd.ExecuteNonQuery()
                            End Using
                        End Using
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
                ' Left accent bar — full tile height
                Using br As New SolidBrush(accentClr)
                    Using path As GraphicsPath = RoundRect(New Rectangle(0, 3, 5, row.Height - 6), 3)
                        e.Graphics.FillPath(br, path)
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