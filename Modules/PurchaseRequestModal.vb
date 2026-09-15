Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports MySql.Data.MySqlClient

' =============================================================================
'  PurchaseRequestModal
'
'  The purchase request form: search products, add as many as needed, send the
'  whole basket to the approver as ONE request.
'
'  WHAT THIS REPLACES
'    There was no purchase request form. The only way to raise a request was
'    the "Request" pill on each Low Stock Alerts row, which asked for a
'    quantity and wrote one purchase_requests row for that single product.
'    Five products meant five unrelated requests, and the approver reviewed
'    five separate rows with nothing tying them together -- there was no way
'    to see, or approve, a basket.
'
'    Raising requests is the INVENTORY role's job, done from Low Stock Alerts,
'    so this form opens from there and keeps that flow.
'
'  PRODUCT SEARCH
'    The form searches the whole product catalogue by name or SKU, not only
'    the low-stock list. A request is not always triggered by an alert -- a
'    bulk job or a customer order can need stock that is not flagged yet.
'    Each result carries its current stock and reorder level so the requester
'    can see what they are asking for.
'
'  HOW IT IS STORED
'    One purchase_requests row (the request) plus one purchase_request_items
'    row per product (the lines). The first line is mirrored back into the
'    legacy purchase_requests.product_id / .qty_requested columns so the six
'    existing read sites across Procurement, Admin and Inventory keep working
'    unchanged. See migrations.sql section 14.
'
'  Returns True when a request was submitted, so the caller can refresh.
' =============================================================================
Public Module PurchaseRequestModal

    ' ── PALETTE ──────────────────────────────────────────────────────────────
    Private ReadOnly CardBg As Color = Color.White
    Private ReadOnly TitleClr As Color = Color.FromArgb(25, 40, 35)
    Private ReadOnly BodyClr As Color = Color.FromArgb(80, 100, 94)
    Private ReadOnly BorderClr As Color = Color.FromArgb(160, 195, 185)
    Private ReadOnly DividerClr As Color = Color.FromArgb(230, 238, 235)
    Private ReadOnly MintPrimary As Color = Theme.Primary
    Private ReadOnly MintDeep As Color = Theme.Deep
    Private ReadOnly MintDark As Color = Theme.Dark
    Private ReadOnly FieldBg As Color = Theme.PageBg
    Private ReadOnly FieldBorder As Color = Theme.BorderMuted
    Private ReadOnly FaintClr As Color = Theme.FaintText
    Private ReadOnly SubClr As Color = Theme.SubText
    Private ReadOnly RowDivider As Color = Theme.RowDivider
    Private ReadOnly RowHover As Color = Theme.RowHover
    Private ReadOnly DangerClr As Color = Theme.Danger
    Private ReadOnly WarnClr As Color = Theme.Warning
    Private ReadOnly Peso As String = ChrW(8369)

    Private Const RADIUS As Integer = 10
    Private Const MAX_RESULTS As Integer = 40

    ' ── MODELS ───────────────────────────────────────────────────────────────
    Private Class ProductHit
        Public ProductId As Integer
        Public Name As String
        Public Sku As String
        Public StockQty As Integer
        Public ReorderLevel As Integer
        Public UnitPrice As Decimal

        ' Below its reorder level, so the search result can say so without the
        ' requester cross-checking the alerts screen.
        Public ReadOnly Property IsLow As Boolean
            Get
                Return ReorderLevel > 0 AndAlso StockQty <= ReorderLevel
            End Get
        End Property
    End Class

    Private Class ReqLine
        Public ProductId As Integer
        Public Name As String
        Public Sku As String
        Public StockQty As Integer
        Public ReorderLevel As Integer
        Public UnitPrice As Decimal
        Public Qty As Integer

        Public ReadOnly Property LineValue As Decimal
            Get
                Return Math.Round(UnitPrice * Qty, 2)
            End Get
        End Property
    End Class

    ' ── STATE (per dialog; reset on open) ────────────────────────────────────
    Private _lines As New List(Of ReqLine)

    Private Function RoundedPath(rect As Rectangle, radius As Integer) As GraphicsPath
        Dim path As New GraphicsPath()
        Dim d As Integer = radius * 2
        path.AddArc(rect.X, rect.Y, d, d, 180, 90)
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90)
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90)
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90)
        path.CloseFigure()
        Return path
    End Function

    Private Function MakeField(inner As Control, x As Integer, y As Integer,
                               w As Integer, Optional h As Integer = 40) As Panel
        Dim host As New Panel With {
            .Location = New Point(x, y), .Size = New Size(w, h), .BackColor = FieldBg
        }
        AddHandler host.Paint,
            Sub(sender As Object, ev As PaintEventArgs)
                ev.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim rc As New Rectangle(0, 0, host.Width - 1, host.Height - 1)
                Using path As GraphicsPath = RoundedPath(rc, 8)
                    Using b As New SolidBrush(FieldBg)
                        ev.Graphics.FillPath(b, path)
                    End Using
                    Using pen As New Pen(If(inner.Focused, MintPrimary, FieldBorder), 1.2F)
                        ev.Graphics.DrawPath(pen, path)
                    End Using
                End Using
            End Sub
        AddHandler inner.GotFocus, Sub() host.Invalidate()
        AddHandler inner.LostFocus, Sub() host.Invalidate()
        host.Controls.Add(inner)
        Return host
    End Function

    Private Function MakeCaption(text As String, x As Integer, y As Integer, w As Integer) As Label
        Return New Label With {
            .Text = text, .Font = New Font("Segoe UI Semibold", 8.5F),
            .ForeColor = FaintClr, .AutoSize = False,
            .Location = New Point(x, y), .Size = New Size(w, 16),
            .TextAlign = ContentAlignment.MiddleLeft, .BackColor = Color.Transparent
        }
    End Function

    Private Function MakeButton(text As String, primary As Boolean,
                                loc As Point, sz As Size) As Button
        Dim b As New Button With {
            .Text = text, .Location = loc, .Size = sz,
            .FlatStyle = FlatStyle.Flat, .Cursor = Cursors.Hand,
            .Font = New Font("Segoe UI Semibold", 9.5F),
            .BackColor = If(primary, MintDeep, Theme.FieldBg),
            .ForeColor = If(primary, Color.White, Color.FromArgb(70, 86, 80))
        }
        b.FlatAppearance.BorderSize = 0
        Using gp As GraphicsPath = RoundedPath(New Rectangle(0, 0, sz.Width, sz.Height), 7)
            b.Region = New Region(gp)
        End Using
        Return b
    End Function

    ' =========================================================================
    '  ENTRY POINT
    '
    '  seedProductId / seedQty optionally pre-load one line, so the Low Stock
    '  "Request" pill can open the form already carrying the flagged product
    '  instead of making the requester search for what they just clicked.
    ' =========================================================================
    Public Function Show(Optional seedProductId As Integer = 0,
                         Optional seedQty As Integer = 0) As Boolean

        _lines = New List(Of ReqLine)

        Const CARD_W As Integer = 720
        Const CARD_H As Integer = 640
        Const PAD As Integer = 24
        Dim innerW As Integer = CARD_W - PAD * 2

        Dim f As New Form With {
            .FormBorderStyle = FormBorderStyle.None,
            .StartPosition = FormStartPosition.CenterParent,
            .BackColor = CardBg, .ShowInTaskbar = False,
            .Width = CARD_W, .Height = CARD_H, .KeyPreview = True
        }

        Dim lblTitle As New Label With {
            .Text = "New Purchase Request", .Font = New Font("Segoe UI Semibold", 13.0F),
            .ForeColor = TitleClr, .AutoSize = False,
            .Location = New Point(PAD, PAD), .Size = New Size(innerW, 24),
            .BackColor = Color.Transparent
        }
        Dim lblBody As New Label With {
            .Text = "Search products, add what you need, and send the whole list " &
                    "for approval as one request.",
            .Font = New Font("Segoe UI", 9.5F), .ForeColor = BodyClr,
            .AutoSize = False, .Location = New Point(PAD, lblTitle.Bottom + 4),
            .Size = New Size(innerW, 20), .BackColor = Color.Transparent
        }

        ' ── Search field ─────────────────────────────────────────────────────
        Dim searchY As Integer = lblBody.Bottom + 14
        Dim txtSearch As New TextBox With {
            .Font = New Font("Segoe UI", 11.0F), .BorderStyle = BorderStyle.None,
            .BackColor = FieldBg, .ForeColor = TitleClr,
            .Location = New Point(34, 11), .Width = innerW - 48
        }
        Dim fldSearch As Panel = MakeField(txtSearch, PAD, searchY, innerW, 42)

        ' The magnifier that was missing. Drawn rather than loaded so it cannot
        ' go missing with a file, and sized to sit on the field's baseline.
        AddHandler fldSearch.Paint,
            Sub(sender As Object, ev As PaintEventArgs)
                Dim g = ev.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Using pen As New Pen(If(txtSearch.Focused, MintPrimary, FaintClr), 1.8F)
                    g.DrawEllipse(pen, 13, 14, 11, 11)
                    g.DrawLine(pen, 23, 24, 27, 28)
                End Using
            End Sub
        AddHandler txtSearch.GotFocus, Sub() fldSearch.Invalidate()
        AddHandler txtSearch.LostFocus, Sub() fldSearch.Invalidate()

        Dim lblPlaceholder As New Label With {
            .Text = "Search by product name or SKU", .Font = New Font("Segoe UI", 10.5F),
            .ForeColor = FaintClr, .AutoSize = True,
            .Location = New Point(36, 12), .BackColor = Color.Transparent
        }
        fldSearch.Controls.Add(lblPlaceholder)
        lblPlaceholder.BringToFront()

        ' ── Results list ─────────────────────────────────────────────────────
        Dim resultsY As Integer = searchY + 48
        Dim resultsHost As New Panel With {
            .Location = New Point(PAD, resultsY), .Size = New Size(innerW, 150),
            .BackColor = CardBg, .AutoScroll = True, .Visible = False
        }
        AddHandler resultsHost.Paint,
            Sub(sender As Object, ev As PaintEventArgs)
                ev.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim rc As New Rectangle(0, 0, resultsHost.Width - 1, resultsHost.Height - 1)
                Using path As GraphicsPath = RoundedPath(rc, 8)
                    Using pen As New Pen(FieldBorder, 1.2F)
                        ev.Graphics.DrawPath(pen, path)
                    End Using
                End Using
            End Sub

        ' ── Lines list ───────────────────────────────────────────────────────
        Dim capLines As Label = MakeCaption("REQUESTED ITEMS", PAD, resultsY, innerW)
        Dim linesHost As New Panel With {
            .Location = New Point(PAD, resultsY + 20),
            .Size = New Size(innerW, 236),
            .BackColor = CardBg, .AutoScroll = True
        }

        Dim lblTotals As New Label With {
            .Text = "", .Font = New Font("Segoe UI Semibold", 10.0F),
            .ForeColor = MintDark, .AutoSize = False,
            .Location = New Point(PAD + 2, linesHost.Bottom + 8),
            .Size = New Size(innerW - 4, 20),
            .TextAlign = ContentAlignment.MiddleLeft, .BackColor = Color.Transparent
        }

        ' ── Note ─────────────────────────────────────────────────────────────
        Dim noteY As Integer = lblTotals.Bottom + 10
        Dim capNote As Label = MakeCaption("NOTE FOR THE APPROVER (OPTIONAL)", PAD, noteY, innerW)
        Dim txtNote As New TextBox With {
            .Font = New Font("Segoe UI", 10.0F), .BorderStyle = BorderStyle.None,
            .BackColor = FieldBg, .ForeColor = TitleClr,
            .Location = New Point(12, 10), .Width = innerW - 24
        }
        Dim fldNote As Panel = MakeField(txtNote, PAD, noteY + 18, innerW, 40)

        ' ── Declared ahead of the closures that use them ─────────────────────
        Dim renderLines As Action = Nothing
        Dim renderResults As Action(Of List(Of ProductHit)) = Nothing
        Dim addLine As Action(Of ProductHit, Integer) = Nothing

        ' Totals restate the basket in the approver's terms: how many products,
        ' how many units, and roughly what it is worth at current prices.
        Dim refreshTotals As Action =
            Sub()
                If _lines.Count = 0 Then
                    lblTotals.Text = ""
                    Return
                End If
                Dim units As Integer = 0
                Dim value As Decimal = 0D
                For Each ln In _lines
                    units += ln.Qty
                    value += ln.LineValue
                Next
                lblTotals.Text = _lines.Count & If(_lines.Count = 1, " product", " products") &
                                 "   " & ChrW(8226) & "   " & units.ToString("N0") &
                                 If(units = 1, " unit", " units") &
                                 "   " & ChrW(8226) & "   about " & Peso & value.ToString("N2") &
                                 " at current prices"
            End Sub

        ' ── Build one line row ───────────────────────────────────────────────
        Dim buildLineRow As Func(Of ReqLine, Integer, Panel) =
            Function(ln As ReqLine, index As Integer) As Panel
                Dim row As New Panel With {
                    .Height = 56, .Top = index * 56, .Width = linesHost.Width - 20,
                    .BackColor = CardBg
                }

                Dim lblName As New Label With {
                    .Text = ln.Name, .Font = New Font("Segoe UI Semibold", 10.0F),
                    .ForeColor = MintDark, .AutoSize = False,
                    .Location = New Point(8, 8), .Size = New Size(280, 20),
                    .TextAlign = ContentAlignment.MiddleLeft, .BackColor = Color.Transparent
                }

                ' Stock and reorder level travel with the line so the approver
                ' can see WHY the item is on the request without opening another
                ' screen. This is the justification, not decoration.
                Dim stockNote As String = ln.Sku & "   " & ChrW(8226) & "   stock " & ln.StockQty
                If ln.ReorderLevel > 0 Then
                    stockNote &= " / reorder at " & ln.ReorderLevel
                End If
                Dim lblSku As New Label With {
                    .Text = stockNote, .Font = New Font("Segoe UI", 8.5F),
                    .ForeColor = If(ln.ReorderLevel > 0 AndAlso ln.StockQty <= ln.ReorderLevel,
                                    WarnClr, FaintClr),
                    .AutoSize = False, .Location = New Point(8, 28),
                    .Size = New Size(280, 18),
                    .TextAlign = ContentAlignment.MiddleLeft, .BackColor = Color.Transparent
                }

                Dim numQty As New NumericUpDown With {
                    .Minimum = 1, .Maximum = 999999, .Value = Math.Max(ln.Qty, 1),
                    .Font = New Font("Segoe UI", 10.0F), .BorderStyle = BorderStyle.None,
                    .BackColor = FieldBg, .ForeColor = TitleClr,
                    .Location = New Point(8, 8), .Width = 74, .TextAlign = HorizontalAlignment.Center
                }
                Dim fldQty As Panel = MakeField(numQty, row.Width - 230, 9, 90, 38)

                Dim lblValue As New Label With {
                    .Text = Peso & ln.LineValue.ToString("N2"),
                    .Font = New Font("Segoe UI Semibold", 10.0F), .ForeColor = SubClr,
                    .AutoSize = False, .Location = New Point(row.Width - 132, 18),
                    .Size = New Size(96, 20),
                    .TextAlign = ContentAlignment.MiddleRight, .BackColor = Color.Transparent
                }

                AddHandler numQty.ValueChanged,
                    Sub()
                        ln.Qty = CInt(numQty.Value)
                        lblValue.Text = Peso & ln.LineValue.ToString("N2")
                        refreshTotals()
                    End Sub

                Dim lnkRemove As New Label With {
                    .Text = ChrW(10005), .Font = New Font("Segoe UI", 11.0F),
                    .ForeColor = FaintClr, .AutoSize = False,
                    .Location = New Point(row.Width - 28, 18), .Size = New Size(22, 22),
                    .TextAlign = ContentAlignment.MiddleCenter, .Cursor = Cursors.Hand,
                    .BackColor = Color.Transparent
                }
                AddHandler lnkRemove.MouseEnter, Sub() lnkRemove.ForeColor = DangerClr
                AddHandler lnkRemove.MouseLeave, Sub() lnkRemove.ForeColor = FaintClr
                AddHandler lnkRemove.Click,
                    Sub()
                        _lines.Remove(ln)
                        renderLines()
                        refreshTotals()
                    End Sub

                Dim div As New Panel With {
                    .Height = 1, .BackColor = RowDivider, .Dock = DockStyle.Bottom
                }

                row.Controls.AddRange({lblName, lblSku, fldQty, lblValue, lnkRemove, div})
                Return row
            End Function

        ' ── Render the basket ────────────────────────────────────────────────
        renderLines =
            Sub()
                linesHost.SuspendLayout()
                linesHost.Controls.Clear()

                If _lines.Count = 0 Then
                    Dim lblEmpty As New Label With {
                        .Text = "No products added yet." & Environment.NewLine &
                                "Use the search box above to find what you need.",
                        .Font = New Font("Segoe UI", 10.0F), .ForeColor = FaintClr,
                        .AutoSize = False, .Dock = DockStyle.Fill,
                        .TextAlign = ContentAlignment.MiddleCenter,
                        .BackColor = Color.Transparent
                    }
                    linesHost.Controls.Add(lblEmpty)
                Else
                    For i As Integer = 0 To _lines.Count - 1
                        linesHost.Controls.Add(buildLineRow(_lines(i), i))
                    Next
                End If

                linesHost.ResumeLayout()
                linesHost.Refresh()
            End Sub

        ' ── Add a product to the basket ──────────────────────────────────────
        addLine =
            Sub(hit As ProductHit, qty As Integer)
                ' Adding the same product twice is a quantity change, not a
                ' duplicate line -- two lines for one product would be
                ' ambiguous to whoever fills the order.
                For Each existing In _lines
                    If existing.ProductId = hit.ProductId Then
                        existing.Qty += Math.Max(qty, 1)
                        renderLines()
                        refreshTotals()
                        Return
                    End If
                Next

                _lines.Add(New ReqLine With {
                    .ProductId = hit.ProductId, .Name = hit.Name, .Sku = hit.Sku,
                    .StockQty = hit.StockQty, .ReorderLevel = hit.ReorderLevel,
                    .UnitPrice = hit.UnitPrice, .Qty = Math.Max(qty, 1)
                })
                renderLines()
                refreshTotals()
            End Sub

        ' ── Render search results ────────────────────────────────────────────
        renderResults =
            Sub(hits As List(Of ProductHit))
                resultsHost.SuspendLayout()
                resultsHost.Controls.Clear()

                If hits.Count = 0 Then
                    Dim lblNone As New Label With {
                        .Text = "No products match that search.",
                        .Font = New Font("Segoe UI", 10.0F), .ForeColor = FaintClr,
                        .AutoSize = False, .Location = New Point(14, 14),
                        .Size = New Size(resultsHost.Width - 28, 20),
                        .TextAlign = ContentAlignment.MiddleLeft, .BackColor = Color.Transparent
                    }
                    resultsHost.Controls.Add(lblNone)
                Else
                    For i As Integer = 0 To hits.Count - 1
                        Dim hit As ProductHit = hits(i)
                        Dim row As New Panel With {
                            .Height = 46, .Top = i * 46, .Left = 1,
                            .Width = resultsHost.Width - 22, .BackColor = CardBg,
                            .Cursor = Cursors.Hand
                        }

                        Dim lblName As New Label With {
                            .Text = hit.Name, .Font = New Font("Segoe UI Semibold", 10.0F),
                            .ForeColor = MintDark, .AutoSize = False,
                            .Location = New Point(12, 5), .Size = New Size(320, 19),
                            .TextAlign = ContentAlignment.MiddleLeft,
                            .BackColor = Color.Transparent
                        }

                        Dim meta As String = hit.Sku & "   " & ChrW(8226) & "   stock " & hit.StockQty
                        If hit.ReorderLevel > 0 Then meta &= " / reorder at " & hit.ReorderLevel
                        If hit.IsLow Then meta &= "   " & ChrW(8226) & "   LOW"

                        Dim lblMeta As New Label With {
                            .Text = meta, .Font = New Font("Segoe UI", 8.5F),
                            .ForeColor = If(hit.IsLow, WarnClr, FaintClr),
                            .AutoSize = False, .Location = New Point(12, 24),
                            .Size = New Size(360, 17),
                            .TextAlign = ContentAlignment.MiddleLeft,
                            .BackColor = Color.Transparent
                        }

                        ' Already in the basket: say so instead of letting the
                        ' requester add it again wondering if the click landed.
                        Dim already As Boolean = False
                        For Each ln In _lines
                            If ln.ProductId = hit.ProductId Then already = True : Exit For
                        Next

                        Dim lblAdd As New Label With {
                            .Text = If(already, "in list", "+ Add"),
                            .Font = New Font("Segoe UI Semibold", 9.5F),
                            .ForeColor = If(already, FaintClr, MintDeep),
                            .AutoSize = False,
                            .Location = New Point(row.Width - 86, 13),
                            .Size = New Size(74, 20),
                            .TextAlign = ContentAlignment.MiddleRight,
                            .BackColor = Color.Transparent
                        }

                        Dim div As New Panel With {
                            .Height = 1, .BackColor = RowDivider, .Dock = DockStyle.Bottom
                        }

                        row.Controls.AddRange({lblName, lblMeta, lblAdd, div})

                        ' Suggest the shortfall for a low item, 1 otherwise --
                        ' the quantity is editable either way.
                        Dim suggested As Integer = 1
                        If hit.IsLow Then
                            suggested = Math.Max(hit.ReorderLevel - hit.StockQty, 1)
                        End If

                        Dim doAdd As EventHandler =
                            Sub(sn, ev)
                                addLine(hit, suggested)
                                txtSearch.Text = ""
                                txtSearch.Focus()
                            End Sub
                        AddHandler row.Click, doAdd
                        For Each c As Control In row.Controls
                            If c IsNot div Then AddHandler c.Click, doAdd
                        Next

                        Dim setBg As Action(Of Color) = Sub(clr) row.BackColor = clr
                        AddHandler row.MouseEnter, Sub() setBg(RowHover)
                        AddHandler row.MouseLeave, Sub() setBg(CardBg)
                        For Each c As Control In row.Controls
                            If c Is div Then Continue For
                            AddHandler c.MouseEnter, Sub() setBg(RowHover)
                            AddHandler c.MouseLeave, Sub() setBg(CardBg)
                        Next

                        resultsHost.Controls.Add(row)
                    Next
                End If

                resultsHost.ResumeLayout()
                resultsHost.Visible = True
                resultsHost.BringToFront()
            End Sub

        ' ── Search wiring ────────────────────────────────────────────────────
        AddHandler txtSearch.TextChanged,
            Sub()
                Dim term As String = txtSearch.Text.Trim()
                lblPlaceholder.Visible = (term.Length = 0)

                If term.Length < 2 Then
                    resultsHost.Visible = False
                    Return
                End If
                renderResults(SearchProducts(term))
            End Sub

        ' Clicking away from the results puts the basket back in view.
        AddHandler linesHost.Click, Sub() resultsHost.Visible = False
        AddHandler f.Click, Sub() resultsHost.Visible = False

        ' ── Buttons ──────────────────────────────────────────────────────────
        Const BTN_H As Integer = 38
        Const BTN_W As Integer = 130
        Const GAP As Integer = 8
        Dim btnRowTop As Integer = CARD_H - PAD - BTN_H
        Dim dividerY As Integer = btnRowTop - 16

        Dim submitted As Boolean = False

        Dim btnCancel As Button = MakeButton("Cancel", False,
            New Point(CARD_W - PAD - BTN_W * 2 - GAP, btnRowTop), New Size(BTN_W, BTN_H))
        Dim btnSend As Button = MakeButton("Send for Approval", True,
            New Point(CARD_W - PAD - BTN_W, btnRowTop), New Size(BTN_W, BTN_H))

        AddHandler btnCancel.Click,
            Sub()
                ' A basket represents real typing, so losing it is confirmed.
                If _lines.Count > 0 Then
                    If AppModal.Show(
                        "Discard this request?" & Environment.NewLine &
                        _lines.Count & If(_lines.Count = 1, " product", " products") &
                        " will be lost.",
                        "Discard Request", MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning) <> DialogResult.Yes Then
                        Return
                    End If
                End If
                f.DialogResult = DialogResult.Cancel
                f.Close()
            End Sub

        AddHandler btnSend.Click,
            Sub()
                If _lines.Count = 0 Then
                    AppModal.Show("Add at least one product before sending the request.",
                                  "Nothing to Request", MessageBoxButtons.OK,
                                  MessageBoxIcon.Warning)
                    txtSearch.Focus()
                    Return
                End If

                Dim units As Integer = 0
                For Each ln In _lines
                    units += ln.Qty
                Next

                If AppModal.Show(
                    "Send this request for approval?" & Environment.NewLine &
                    Environment.NewLine &
                    _lines.Count & If(_lines.Count = 1, " product", " products") &
                    ", " & units.ToString("N0") & If(units = 1, " unit", " units") & ".",
                    "Send Request", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) <> DialogResult.Yes Then
                    Return
                End If

                If SaveRequest(_lines, txtNote.Text.Trim()) Then
                    submitted = True
                    f.DialogResult = DialogResult.OK
                    f.Close()
                End If
            End Sub

        f.Controls.AddRange({lblTitle, lblBody, fldSearch,
                             capLines, linesHost, lblTotals,
                             capNote, fldNote,
                             btnCancel, btnSend, resultsHost})
        resultsHost.BringToFront()

        Dim dividerYLocal As Integer = dividerY
        AddHandler f.Paint,
            Sub(s As Object, e As PaintEventArgs)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim card As New Rectangle(0, 0, f.Width - 1, f.Height - 1)
                Using br As New SolidBrush(CardBg)
                    g.FillRectangle(br, card)
                End Using
                Using pen As New Pen(DividerClr, 1.0F)
                    g.DrawLine(pen, PAD, dividerYLocal, f.Width - PAD, dividerYLocal)
                End Using
                Using path As GraphicsPath = RoundedPath(card, RADIUS)
                    Using pen As New Pen(BorderClr, 1.2F)
                        g.DrawPath(pen, path)
                    End Using
                End Using
            End Sub

        Using gp As GraphicsPath = RoundedPath(New Rectangle(0, 0, CARD_W, CARD_H), RADIUS)
            f.Region = New Region(gp)
        End Using

        AddHandler f.KeyDown,
            Sub(s As Object, e As KeyEventArgs)
                If e.KeyCode = Keys.Escape Then
                    If resultsHost.Visible Then
                        resultsHost.Visible = False
                    Else
                        f.DialogResult = DialogResult.Cancel
                        f.Close()
                    End If
                End If
            End Sub

        ' Seed the product the requester came in with, if any.
        If seedProductId > 0 Then
            Dim seed As ProductHit = LoadProduct(seedProductId)
            If seed IsNot Nothing Then addLine(seed, Math.Max(seedQty, 1))
        End If

        renderLines()
        refreshTotals()

        AddHandler f.Shown, Sub() txtSearch.Focus()

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

        Return submitted
    End Function

    ' =========================================================================
    '  DATA
    ' =========================================================================

    ' Searches the whole catalogue, not just flagged items: a request can be
    ' raised for stock that is not low yet. Low items sort first because that
    ' is still the most common reason to be here. Discontinued products are
    ' excluded -- they must not be reorderable.
    Private Function SearchProducts(term As String) As List(Of ProductHit)
        Dim result As New List(Of ProductHit)
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT p.product_id, p.name, p.sku, p.stock_qty, " &
                    "COALESCE(p.reorder_level, 0) AS reorder_level, " &
                    "COALESCE(p.unit_price, 0) AS unit_price " &
                    "FROM products p " &
                    "WHERE p.is_active = 1 AND (p.name LIKE @q OR p.sku LIKE @q) " &
                    "ORDER BY " &
                    "  CASE WHEN COALESCE(p.reorder_level,0) > 0 " &
                    "       AND p.stock_qty <= p.reorder_level THEN 0 ELSE 1 END, " &
                    "  p.name " &
                    "LIMIT " & MAX_RESULTS, conn)
                    cmd.Parameters.AddWithValue("@q", "%" & term & "%")
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            result.Add(ReadHit(rdr))
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not search products:" & Environment.NewLine & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
        Return result
    End Function

    Private Function LoadProduct(productId As Integer) As ProductHit
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT p.product_id, p.name, p.sku, p.stock_qty, " &
                    "COALESCE(p.reorder_level, 0) AS reorder_level, " &
                    "COALESCE(p.unit_price, 0) AS unit_price " &
                    "FROM products p WHERE p.product_id = @id LIMIT 1", conn)
                    cmd.Parameters.AddWithValue("@id", productId)
                    Using rdr = cmd.ExecuteReader()
                        If rdr.Read() Then Return ReadHit(rdr)
                    End Using
                End Using
            End Using
        Catch
            ' Seeding is a convenience; the form still opens empty.
        End Try
        Return Nothing
    End Function

    Private Function ReadHit(rdr As MySqlDataReader) As ProductHit
        Return New ProductHit With {
            .ProductId = Convert.ToInt32(rdr("product_id")),
            .Name = rdr("name").ToString(),
            .Sku = rdr("sku").ToString(),
            .StockQty = Convert.ToInt32(rdr("stock_qty")),
            .ReorderLevel = Convert.ToInt32(rdr("reorder_level")),
            .UnitPrice = Convert.ToDecimal(rdr("unit_price"))
        }
    End Function

    ' =========================================================================
    '  SAVE
    '
    '  One request row plus one line per product, in a single transaction: a
    '  request with no lines, or lines with no request, would both be wrong.
    '
    '  The first line is mirrored into purchase_requests.product_id and
    '  .qty_requested because six existing read sites still join on those
    '  columns. A single-product request therefore reads identically under the
    '  old and new queries; a multi-product one shows old queries its first
    '  line and new queries the whole list.
    ' =========================================================================
    Private Function SaveRequest(lines As List(Of ReqLine), note As String) As Boolean
        If lines.Count = 0 Then Return False

        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using tx = conn.BeginTransaction()

                    Dim head As ReqLine = lines(0)

                    ' The note describes the basket. When several products are
                    ' on it, the count goes in too, so an approver reading the
                    ' legacy single-product view can still tell there is more.
                    Dim fullNote As String = note
                    If lines.Count > 1 Then
                        Dim prefix As String =
                            "Request for " & lines.Count & " products (" &
                            head.Name & " + " & (lines.Count - 1) & " more)"
                        fullNote = If(note.Length = 0, prefix, prefix & " — " & note)
                    End If
                    If fullNote.Length > 150 Then fullNote = fullNote.Substring(0, 150)

                    Dim requestId As Integer
                    Using cmd As New MySqlCommand(
                        "INSERT INTO purchase_requests " &
                        "(product_id, requested_by, qty_requested, status, note, created_at) " &
                        "VALUES (@pid, @uid, @qty, 'Pending', @note, NOW())", conn, tx)
                        cmd.Parameters.AddWithValue("@pid", head.ProductId)
                        cmd.Parameters.AddWithValue("@uid", Session.CurrentUserId)
                        cmd.Parameters.AddWithValue("@qty", head.Qty)
                        cmd.Parameters.AddWithValue("@note",
                            If(fullNote.Length = 0, CObj(DBNull.Value), fullNote))
                        cmd.ExecuteNonQuery()
                        requestId = CInt(cmd.LastInsertedId)
                    End Using

                    For Each ln In lines
                        Using cmd As New MySqlCommand(
                            "INSERT INTO purchase_request_items " &
                            "(request_id, product_id, qty_requested, note) " &
                            "VALUES (@rid, @pid, @qty, @note)", conn, tx)
                            cmd.Parameters.AddWithValue("@rid", requestId)
                            cmd.Parameters.AddWithValue("@pid", ln.ProductId)
                            cmd.Parameters.AddWithValue("@qty", ln.Qty)
                            cmd.Parameters.AddWithValue("@note",
                                If(ln.ReorderLevel > 0 AndAlso ln.StockQty <= ln.ReorderLevel,
                                   "Stock " & ln.StockQty & ", reorder level " & ln.ReorderLevel,
                                   CObj(DBNull.Value)))
                            cmd.ExecuteNonQuery()
                        End Using
                    Next

                    tx.Commit()

                    Dim units As Integer = 0
                    For Each ln In lines
                        units += ln.Qty
                    Next

                    ' A purchase request opens the spend approval chain, so its
                    ' size goes in the trail, not just that one was raised.
                    ActivityLog.Record("REQUEST_RAISE", "purchase_request", requestId,
                                       lines.Count & If(lines.Count = 1, " product, ", " products, ") &
                                       units.ToString("N0") & If(units = 1, " unit", " units"))

                    AppModal.Show(
                        "Request sent for approval." & Environment.NewLine &
                        lines.Count & If(lines.Count = 1, " product", " products") &
                        ", " & units.ToString("N0") & If(units = 1, " unit", " units") & ".",
                        "Request Sent", MessageBoxButtons.OK, MessageBoxIcon.Information)

                    Return True
                End Using
            End Using

        Catch ex As Exception
            AppModal.Show("Could not send the request:" & Environment.NewLine & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return False
        End Try
    End Function

End Module
