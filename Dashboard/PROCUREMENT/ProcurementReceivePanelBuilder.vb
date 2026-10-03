Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports MySql.Data.MySqlClient

' =============================================================================
'  ProcurementReceivePanelBuilder
'  Screen 4 — Receive Goods.
'
'  INLINE VIEWS:
'    1. List    — every PO with status 'Ordered' or 'Partially Received',
'                 soonest expected first. Each has a "Receive" link.
'    2. Receive — one PO's lines: product, ordered qty, already-received,
'                 and a "qty received now" input. Partial receipt allowed.
'
'  SAVE — Phase 1 of the two-phase goods-receipt flow:
'    * everything in ONE MySqlTransaction
'    * INSERT stock_movements (movement_type='Purchase', reference_type='receive',
'      note='Received via PO ' & poNo)   — records goods arriving at the dock
'    * products.stock_qty is NOT updated here — that happens in Phase 2
'      (Inventory → Put Away) when staff physically places goods on the shelf
'    * rollback on any error
'  After the writes, the PO becomes 'Received' (all lines fully in) or
'  'Partially Received' (some still outstanding). reference_id = po_id lets
'  InventoryPutAwayPanelBuilder compute pending = received − shelved_qty.
'
'  Shared-only, no instance state. Verbatim helpers from
'  InventoryLowStockPanelBuilder.
' =============================================================================
Public Class ProcurementReceivePanelBuilder

    ' ── PALETTE (exact spec values) ──────────────────────────────────────────────
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
    Private Class ReceivePORow
        Public PoId As Integer
        Public PoNo As String
        Public SupplierName As String
        Public HasExpected As Boolean
        Public ExpectedDate As DateTime
        Public Status As String
        Public ItemCount As Integer
    End Class

    Private Class ReceiveLine
        Public PoItemId As Integer
        Public ProductId As Integer
        Public ProductName As String
        Public Sku As String
        Public ImagePath As String
        Public OrderedQty As Integer
        Public AlreadyReceived As Integer
        Public ReceiveNow As Integer
        Public ReadOnly Property Remaining As Integer
            Get
                Return Math.Max(0, OrderedQty - AlreadyReceived)
            End Get
        End Property

        ' Condition of this line's delivery. IssueType "" = arrived complete
        ' and good; otherwise DeliveryIssues.TYPE_DEFECTIVE / TYPE_MISSING.
        Public IssueType As String = ""
        Public IssueQty As Integer
        Public IssueDetails As String = ""
        Public IssueAction As String = ""

        Public ReadOnly Property HasIssue As Boolean
            Get
                Return IssueType <> ""
            End Get
        End Property

        ' Units booked in now: everything still owed, less what is going back
        ' or never came. Accepted-as-is defects are still booked in.
        Public Sub Recompute()
            Dim notStocked As Integer =
                If(HasIssue, DeliveryIssues.UnitsNotStocked(IssueType, IssueAction, IssueQty), 0)
            ReceiveNow = Math.Max(0, Remaining - notStocked)
        End Sub
    End Class

    ' ── STATE ────────────────────────────────────────────────────────────────────
    Private Shared containerRef As Panel
    Private Shared receiveLines As New List(Of ReceiveLine)
    Private Shared currentPoId As Integer
    Private Shared currentPoNo As String
    Private Shared currentSupplierName As String
    Private Shared btnSaveRef As Button

    ' =========================================================================
    '  ENTRY POINTS
    ' =========================================================================
    Public Shared Sub LoadReceive(container As Panel)
        containerRef = container
        RenderList()
    End Sub

    ' Opens the Receive form for one PO directly (from PO detail / Delivery screen).
    Public Shared Sub LoadReceiveForPO(container As Panel, poId As Integer)
        containerRef = container
        LoadReceiveForm(poId)
    End Sub

    ' =========================================================================
    '  VIEW 1 — LIST
    ' =========================================================================
    Private Shared Sub RenderList()
        Dim container As Panel = containerRef
        If container Is Nothing Then Return

        Dim rows As List(Of ReceivePORow) = LoadReceivableList()

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
            Dim empty As Panel = BuildEmptyCard("Nothing to receive",
                "No purchase orders are currently Ordered or awaiting more stock.")
            empty.Location = New Point(0, 16)
            empty.Width = rowsHost.ClientSize.Width - 4
            empty.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
            rowsHost.Controls.Add(empty)
        Else
            Dim y As Integer = 8
            For Each po In rows
                Dim card As Panel = BuildReceiveCard(po)
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
            .Text = "Receive Goods", .Font = New Font("Segoe UI", 24, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(24, 18)
        }
        Dim lblSub As New Label With {
            .Text = count & If(count = 1, " order awaiting receipt", " orders awaiting receipt"),
            .Font = New Font("Segoe UI", 11), .ForeColor = SubText, .AutoSize = True, .Location = New Point(30, 58)
        }

        region.Controls.AddRange({lblTitle, lblSub})

        AddHandler region.Resize,
    Sub()
    End Sub

        Return region
    End Function

    Private Shared Function BuildReceiveCard(po As ReceivePORow) As Panel
        Dim cardH As Integer = 138
        Dim card As New BufferedPanel With {.Height = cardH, .BackColor = CardBg, .Cursor = Cursors.Hand}

        AddHandler card.Paint,
            Sub(s, e)
                PaintCard(CType(s, Panel), e)
            End Sub

        ' ── Text ─────────────────────────────────────────────────────────────────
        Dim lblPo As New Label With {
            .Text = po.PoNo,
            .Font = New Font("Segoe UI", 16, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True,
            .Location = New Point(22, 18), .BackColor = Color.Transparent
        }
        Dim lblSupplier As New Label With {
            .Text = po.SupplierName,
            .Font = New Font("Segoe UI", 11), .ForeColor = SubText, .AutoSize = True,
            .Location = New Point(24, 52), .BackColor = Color.Transparent
        }
        Dim lblMeta As New Label With {
            .Text = If(po.HasExpected, "Expected " & po.ExpectedDate.ToString("MMM d, yyyy"), "No expected date") &
                    "   " & ChrW(8226) & "   " & po.ItemCount & If(po.ItemCount = 1, " item", " items"),
            .Font = New Font("Segoe UI", 10),
            .ForeColor = Color.FromArgb(72, 92, 87), .AutoSize = True,
            .Location = New Point(24, 76), .BackColor = Color.Transparent
        }
        card.Controls.AddRange({lblPo, lblSupplier, lblMeta})

        ' ── Status badge — solid fill, TOP-RIGHT ─────────────────────────────────
        Dim badgeBack As Color
        Select Case po.Status
            Case "Ordered" : badgeBack = MintDeep
            Case "Partially Received" : badgeBack = WarningClr
            Case Else : badgeBack = SubText
        End Select
        Dim badge As Label = OutlineBadge(StatusInfo(po.Status).text, badgeBack)
        card.Controls.Add(badge)
        ' ── "Receive" button — solid mint, button-shaped corners, BOTTOM-RIGHT ──
        Dim vpH As Integer = 32
        Dim vpW As Integer = 104
        Dim receivePill As New BufferedPanel With {
            .Size = New Size(vpW, vpH),
            .BackColor = MintDeep,
            .Cursor = Cursors.Hand,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }

        ' Button-shaped region — modest 8px corners, NOT a pill
        Dim receivePillRadius As Integer = 6
        Using rgnPath As GraphicsPath = RoundRect(New Rectangle(0, 0, vpW, vpH), receivePillRadius * 2)
            receivePill.Region = New Region(rgnPath)
        End Using

        ' Hover / press state drives the fill colour
        Dim receivePillHot As Boolean = False
        Dim receivePillDown As Boolean = False

        AddHandler receivePill.Paint,
            Sub(s, e)
                Dim b As BufferedPanel = CType(s, BufferedPanel)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim fill As Color = MintDeep
                If receivePillDown Then
                    fill = Color.FromArgb(0, 104, 87)
                ElseIf receivePillHot Then
                    fill = MintPrimary
                End If
                Using path As GraphicsPath = RoundRect(New Rectangle(0, 0, b.Width - 1, b.Height - 1), receivePillRadius * 2)
                    Using br As New SolidBrush(fill)
                        e.Graphics.FillPath(br, path)
                    End Using
                End Using
                ' Label + chevron, centred as a unit
                Using f As New Font("Segoe UI Semibold", 9.5F, FontStyle.Regular),
                      br As New SolidBrush(Color.White)
                    Dim txtSz As SizeF = e.Graphics.MeasureString("Receive", f)
                    Dim gap As Single = 8
                    Dim chW As Single = 6 * 0.7F
                    Dim totalW As Single = txtSz.Width + gap + chW
                    Dim startX As Single = (b.Width - totalW) / 2.0F
                    Dim sf As New StringFormat With {
                        .Alignment = StringAlignment.Near,
                        .LineAlignment = StringAlignment.Center
                    }
                    e.Graphics.DrawString("Receive", f, br,
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

        AddHandler receivePill.MouseEnter,
            Sub()
                receivePillHot = True
                receivePill.Invalidate()
            End Sub
        AddHandler receivePill.MouseLeave,
            Sub()
                receivePillHot = False
                receivePillDown = False
                receivePill.Invalidate()
            End Sub
        AddHandler receivePill.MouseDown,
            Sub()
                receivePillDown = True
                receivePill.Invalidate()
            End Sub
        AddHandler receivePill.MouseUp,
            Sub()
                receivePillDown = False
                receivePill.Invalidate()
            End Sub
        card.Controls.Add(receivePill)

        ' ── Click handlers ───────────────────────────────────────────────────────
        Dim capId As Integer = po.PoId
        AddHandler receivePill.Click, Sub() LoadReceiveForm(capId)
        AddHandler card.Click, Sub() LoadReceiveForm(capId)
        AddHandler lblPo.Click, Sub() LoadReceiveForm(capId)
        AddHandler lblSupplier.Click, Sub() LoadReceiveForm(capId)
        AddHandler lblMeta.Click, Sub() LoadReceiveForm(capId)

        ' ── Hover ────────────────────────────────────────────────────────────────
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

        ' ── Resize ───────────────────────────────────────────────────────────────
        AddHandler card.Resize,
            Sub()
                Const rPad As Integer = 24
                badge.Left = card.Width - badge.Width - rPad
                badge.Top = 22
                receivePill.Left = card.Width - receivePill.Width - rPad
                receivePill.Top = cardH - receivePill.Height - 22
                Dim rightStop As Integer = badge.Left - 20
                lblPo.MaximumSize = New Size(Math.Max(80, rightStop - 22), 30)
                lblSupplier.MaximumSize = New Size(Math.Max(80, rightStop - 24), 26)
                lblMeta.MaximumSize = New Size(Math.Max(80, rightStop - 24), 26)
            End Sub

        Return card
    End Function

    ' =========================================================================
    '  VIEW 2 — RECEIVE FORM
    ' =========================================================================
    Private Shared Sub LoadReceiveForm(poId As Integer)
        Dim container As Panel = containerRef
        If container Is Nothing Then Return

        ' Load header + status guard
        Dim poNo As String = ""
        Dim supplierName As String = ""
        Dim status As String = ""
        Dim hasExp As Boolean = False
        Dim expDate As DateTime = DateTime.MinValue

        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT po.po_no, s.name AS sname, po.expected_date, po.status " &
                    "FROM purchase_orders po JOIN suppliers s ON po.supplier_id = s.supplier_id " &
                    "WHERE po.po_id = @id LIMIT 1", conn)
                    cmd.Parameters.AddWithValue("@id", poId)
                    Using rdr = cmd.ExecuteReader()
                        If rdr.Read() Then
                            poNo = rdr("po_no").ToString()
                            supplierName = rdr("sname").ToString()
                            status = rdr("status").ToString()
                            hasExp = Not IsDBNull(rdr("expected_date"))
                            If hasExp Then expDate = Convert.ToDateTime(rdr("expected_date"))
                        End If
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load the purchase order:" & Environment.NewLine & ErrorLog.ForUser(ex),
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try

        If poNo = "" Then
            AppModal.Show("That purchase order could not be found.", "Not Found",
                          MessageBoxButtons.OK, MessageBoxIcon.Warning)
            RenderList()
            Return
        End If
        If status <> "Ordered" AndAlso status <> "Partially Received" Then
            AppModal.Show("This purchase order is " & status & " and cannot be received.",
                          "Not Receivable", MessageBoxButtons.OK, MessageBoxIcon.Information)
            RenderList()
            Return
        End If

        currentPoId = poId
        currentPoNo = poNo
        currentSupplierName = supplierName
        receiveLines = LoadReceiveLines(poId)

        container.SuspendLayout()
        container.Controls.Clear()
        container.BackColor = MintBg

        Dim body As New Panel With {
            .Dock = DockStyle.Fill, .BackColor = MintBg, .Padding = New Padding(0, 4, 0, 20), .AutoScroll = True
        }
        ' ---- Lines card ----
        Dim linesHeight As Integer = 80 + Math.Max(1, receiveLines.Count) * (ROW_BASE_H + 8) + 14
        Dim linesCard As New BufferedPanel With {.Height = linesHeight, .BackColor = CardBg, .Dock = DockStyle.Top}
        AddHandler linesCard.Paint,
              Sub(s, e)
                  PaintCard(CType(s, Panel), e)
              End Sub

        Dim lblHead As New Label With {
            .Text = "Line Items",
            .Font = New Font("Segoe UI", 14, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True,
            .Location = New Point(20, 16), .BackColor = Color.Transparent
        }
        Dim itemCountPill As New Label With {
            .Text = receiveLines.Count & If(receiveLines.Count = 1, " item", " items"),
            .Font = New Font("Segoe UI Semibold", 9.5F, FontStyle.Bold),
            .ForeColor = MintDeep, .BackColor = TileBg,
            .AutoSize = True, .Padding = New Padding(10, 4, 10, 4),
            .Location = New Point(130, 20)
        }
        PillLabel(itemCountPill)
        itemCountPill.Top = lblHead.Top + (lblHead.PreferredHeight - itemCountPill.PreferredHeight) \ 2
        Dim lblHint As New Label With {
            .Text = "Everything still owed is received on save. If an item arrived damaged or short, " &
                    "change its Condition.",
            .Font = New Font("Segoe UI", 9.0F), .ForeColor = FaintText, .AutoSize = True,
            .Location = New Point(22, 48), .BackColor = Color.Transparent
        }
        linesCard.Controls.AddRange({lblHead, itemCountPill, lblHint})

        If receiveLines.Count = 0 Then
            linesCard.Controls.Add(New Label With {
                .Text = "This purchase order has no line items.",
                .Font = New Font("Segoe UI", 10), .ForeColor = SubText,
                .AutoSize = True, .Location = New Point(20, 72), .BackColor = Color.Transparent
            })
        Else
            ' Rows grow when a Condition other than Good is picked (the problem
            ' details open under the row), so positions are recomputed then.
            Dim rowPanels As New List(Of Panel)
            Dim relayout As Action =
                Sub()
                    Dim ry As Integer = 72
                    For Each rp In rowPanels
                        rp.Top = ry
                        ry += rp.Height + 8
                    Next
                    linesCard.Height = ry + 14
                End Sub
            For Each ln In receiveLines
                Dim r As Panel = BuildReceiveLineRow(ln, relayout)
                r.Location = New Point(12, 72)
                r.Width = linesCard.ClientSize.Width - 24
                r.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
                linesCard.Controls.Add(r)
                rowPanels.Add(r)
            Next
            relayout()
            AddHandler linesCard.Resize,
                Sub()
                    Dim w As Integer = linesCard.ClientSize.Width - 24
                    For Each c As Control In linesCard.Controls
                        If TypeOf c Is Panel Then c.Width = w
                    Next
                End Sub
        End If

        ' ---- Header bar ----
        Dim header As New Panel With {
            .Height = 86, .BackColor = MintBg,
            .Padding = New Padding(0, 0, 24, 0), .Dock = DockStyle.Top
        }

        Dim iconPath As String = IO.Path.Combine(Application.StartupPath, "previous.png")
        Dim picBack As New PictureBox With {
            .Size = New Size(22, 22), .Location = New Point(24, 30),
            .BackColor = Color.Transparent, .SizeMode = PictureBoxSizeMode.Zoom, .Cursor = Cursors.Hand
        }
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
                picBack.Image = bmp
                raw.Dispose()
            Catch
            End Try
        End If
        AddHandler picBack.Click, Sub() RenderList()
        AddHandler picBack.MouseEnter, Sub() picBack.BackColor = Color.FromArgb(30, 0, 130, 109)
        AddHandler picBack.MouseLeave, Sub() picBack.BackColor = Color.Transparent

        Dim textLeft As Integer = picBack.Left
        Dim lblTitle As New Label With {
            .Text = "Receive " & poNo,
            .Font = New Font("Segoe UI", 22, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True,
            .Location = New Point(textLeft + 32, 14), .BackColor = Color.Transparent
        }
        Dim lblMeta As New Label With {
            .Text = supplierName & "   " & ChrW(8226) & "   " &
                    If(hasExp, "Expected " & expDate.ToString("MMM d, yyyy"), "No expected date"),
            .Font = New Font("Segoe UI", 10), .ForeColor = SubText, .AutoSize = True,
            .Location = New Point(textLeft + 34, 50), .BackColor = Color.Transparent
        }

        btnSaveRef = New Button With {
            .Text = "Save Receipt", .Size = New Size(150, 42),
            .FlatStyle = FlatStyle.Flat, .BackColor = MintPrimary, .ForeColor = Color.White,
            .Font = New Font("Segoe UI Semibold", 10), .Cursor = Cursors.Hand,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right, .Tag = ""
        }
        btnSaveRef.FlatAppearance.BorderSize = 0
        RoundControl(btnSaveRef, 10)
        AddHandler btnSaveRef.Click, Sub() CommitReceive()

        header.Controls.AddRange({picBack, lblTitle, lblMeta, btnSaveRef})
        AddHandler header.Resize,
            Sub()
                btnSaveRef.Top = 22
                btnSaveRef.Left = header.Width - 24 - btnSaveRef.Width
            End Sub
        btnSaveRef.Top = 22
        btnSaveRef.Left = 700
        linesCard.Dock = DockStyle.None
        linesCard.Width = body.ClientSize.Width - 48
        linesCard.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        linesCard.Location = New Point(32, 12)

        body.Controls.Add(linesCard)

        AddHandler body.Resize,
            Sub()
                linesCard.Width = Math.Max(200, body.ClientSize.Width - 64)
            End Sub

        container.Controls.Add(body)
        container.Controls.Add(header)
        container.ResumeLayout()

        ' Force resize to settle widths
        body.PerformLayout()
    End Sub

    ' Height of a line row with nothing wrong, and of the problem panel that
    ' opens under it when the Condition is Defective or Missing.
    Private Const ROW_BASE_H As Integer = 84
    Private Const ROW_ISSUE_H As Integer = 74

    Private Shared ReadOnly ConditionChoices() As String = {"Good", "Defective", "Missing (short)"}

    Private Shared Function BuildReceiveLineRow(ln As ReceiveLine, relayout As Action) As Panel
        Dim height As Integer = ROW_BASE_H
        Dim row As New BufferedPanel With {.Height = height, .BackColor = Color.Transparent}

        AddHandler row.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect As New Rectangle(0, 3, row.Width - 1, row.Height - 6)
                ' A line with a problem is tinted, so it stands out in a long list.
                Dim fill As Color = If(ln.HasIssue, Color.FromArgb(254, 247, 240), Color.FromArgb(246, 251, 249))
                Dim edge As Color = If(ln.HasIssue, Color.FromArgb(240, 200, 160), Color.FromArgb(200, 228, 218))
                Using path As GraphicsPath = RoundRect(rect, 10)
                    Using br As New SolidBrush(fill)
                        e.Graphics.FillPath(br, path)
                    End Using
                    Using pen As New Pen(edge, 1.0F)
                        e.Graphics.DrawPath(pen, path)
                    End Using
                End Using
            End Sub

        ' ── Product image or initial fallback ────────────────────────────────
        Dim avSize As Integer = 52
        Dim avLeft As Integer = 14
        Dim avTop As Integer = (height - avSize) \ 2

        Dim imgLoaded As Boolean = False
        If Not String.IsNullOrWhiteSpace(ln.ImagePath) AndAlso IO.File.Exists(ln.ImagePath) Then
            Try
                Dim pic As New PictureBox With {
                    .Size = New Size(avSize, avSize),
                    .Location = New Point(avLeft, avTop),
                    .SizeMode = PictureBoxSizeMode.Zoom,
                    .BackColor = Color.Transparent
                }
                pic.Image = Image.FromFile(ln.ImagePath)
                ' clip to rounded square
                Dim clipPath As New GraphicsPath()
                clipPath.AddArc(0, 0, 12, 12, 180, 90)
                clipPath.AddArc(avSize - 12, 0, 12, 12, 270, 90)
                clipPath.AddArc(avSize - 12, avSize - 12, 12, 12, 0, 90)
                clipPath.AddArc(0, avSize - 12, 12, 12, 90, 90)
                clipPath.CloseFigure()
                pic.Region = New Region(clipPath)
                row.Controls.Add(pic)
                imgLoaded = True
            Catch
            End Try
        End If

        If Not imgLoaded Then
            Dim initial As String = If(String.IsNullOrWhiteSpace(ln.ProductName), "?",
                                       ln.ProductName.Trim().Substring(0, 1).ToUpperInvariant())
            Dim avatar As New BufferedPanel With {
                .Size = New Size(avSize, avSize),
                .Location = New Point(avLeft, avTop),
                .BackColor = Color.Transparent
            }
            AddHandler avatar.Paint,
                Sub(s, e)
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                    Dim av As New Rectangle(0, 0, avSize - 1, avSize - 1)
                    Using path As GraphicsPath = RoundRect(av, 12)
                        Using br As New LinearGradientBrush(av, MintPrimary, MintDeep, 135.0F)
                            e.Graphics.FillPath(br, path)
                        End Using
                    End Using
                    Using f As New Font("Segoe UI", 20, FontStyle.Bold),
                          br As New SolidBrush(Color.White)
                        Dim sz As SizeF = e.Graphics.MeasureString(initial, f)
                        e.Graphics.DrawString(initial, f, br,
                            (avSize - sz.Width) / 2.0F - 1,
                            (avSize - sz.Height) / 2.0F)
                    End Using
                End Sub
            row.Controls.Add(avatar)
        End If

        ' ── Text ─────────────────────────────────────────────────────────────
        Dim textX As Integer = avLeft + avSize + 16
        Dim lblName As New Label With {
            .Text = ln.ProductName,
            .Font = New Font("Segoe UI", 14, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True,
            .Location = New Point(textX, 10), .BackColor = Color.Transparent
        }
        Dim lblSku As New Label With {
            .Text = ln.Sku,
            .Font = New Font("Segoe UI", 9.5F), .ForeColor = FaintText, .AutoSize = True,
            .Location = New Point(textX, 36), .BackColor = Color.Transparent
        }
        Dim lblOrderInfo As New Label With {
            .Text = "Ordered: " & ln.OrderedQty &
                    If(ln.AlreadyReceived > 0, "   " & ChrW(8226) & "   Already received: " & ln.AlreadyReceived, ""),
            .Font = New Font("Segoe UI", 10.5F), .ForeColor = Color.FromArgb(72, 92, 87),
            .AutoSize = True,
            .Location = New Point(textX, 56), .BackColor = Color.Transparent
        }
        row.Controls.AddRange({lblName, lblSku, lblOrderInfo})

        ' ── Qty tile — units booked in now ───────────────────────────────────
        Dim tileW As Integer = 78
        Dim tileH As Integer = 60
        Dim qtyTile As New BufferedPanel With {
            .Size = New Size(tileW, tileH),
            .BackColor = Color.Transparent,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        AddHandler qtyTile.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim r As New Rectangle(0, 0, tileW - 1, tileH - 1)
                Using path As GraphicsPath = RoundRect(r, 10)
                    Using br As New SolidBrush(Color.White)
                        e.Graphics.FillPath(br, path)
                    End Using
                    Using pen As New Pen(Color.FromArgb(180, 210, 198), 1.0F)
                        e.Graphics.DrawPath(pen, path)
                    End Using
                End Using
                ' Fewer than still owed: shown in the warning colour.
                Dim numClr As Color = If(ln.ReceiveNow < ln.Remaining, WarningClr, MintDark)
                Using f As New Font("Segoe UI", 22, FontStyle.Bold),
                      br As New SolidBrush(numClr)
                    Dim sf As New StringFormat With {
                        .Alignment = StringAlignment.Center,
                        .LineAlignment = StringAlignment.Near
                    }
                    e.Graphics.DrawString(ln.ReceiveNow.ToString(), f, br,
                        New RectangleF(0, 6, tileW, 36), sf)
                End Using
                Using f As New Font("Segoe UI", 7.5F),
                      br As New SolidBrush(SubText)
                    Dim sf As New StringFormat With {
                        .Alignment = StringAlignment.Center,
                        .LineAlignment = StringAlignment.Near
                    }
                    e.Graphics.DrawString("to receive", f, br,
                        New RectangleF(0, 41, tileW, 14), sf)
                End Using
            End Sub
        row.Controls.Add(qtyTile)

        ' ── Condition picker ─────────────────────────────────────────────────
        Dim lblCond As New Label With {
            .Text = "CONDITION", .Font = New Font("Segoe UI Semibold", 8.0F),
            .ForeColor = FaintText, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim cmbCond As New ComboBox With {
            .DropDownStyle = ComboBoxStyle.DropDownList, .FlatStyle = FlatStyle.Flat,
            .Font = New Font("Segoe UI", 10.5F), .Width = 170, .BackColor = CardBg,
            .Cursor = Cursors.Hand
        }
        cmbCond.Items.AddRange(ConditionChoices)
        cmbCond.SelectedIndex = 0
        row.Controls.AddRange({lblCond, cmbCond})

        ' ── Problem panel (opens under the row) ─────────────────────────────
        Dim issuePanel As New Panel With {
            .Location = New Point(textX, ROW_BASE_H - 4), .Height = ROW_ISSUE_H - 6,
            .BackColor = Color.Transparent, .Visible = False
        }
        Dim mkCap = Function(text As String, x As Integer) As Label
                        Return New Label With {
                            .Text = text, .Font = New Font("Segoe UI Semibold", 8.0F),
                            .ForeColor = WarningClr, .AutoSize = True,
                            .Location = New Point(x, 0), .BackColor = Color.Transparent
                        }
                    End Function
        Dim numQty As New NumericUpDown With {
            .Minimum = 1, .Maximum = Math.Max(1, ln.Remaining), .Value = 1,
            .Font = New Font("Segoe UI", 10.5F), .Width = 80,
            .Location = New Point(0, 20), .TextAlign = HorizontalAlignment.Center
        }
        Dim txtWhat As New TextBox With {
            .Font = New Font("Segoe UI", 10.5F), .Location = New Point(96, 20),
            .MaxLength = 300, .BorderStyle = BorderStyle.FixedSingle
        }
        Dim cmbAction As New ComboBox With {
            .DropDownStyle = ComboBoxStyle.DropDownList, .FlatStyle = FlatStyle.Flat,
            .Font = New Font("Segoe UI", 10.5F), .Width = 190, .BackColor = CardBg,
            .Location = New Point(0, 20)
        }
        Dim capQty As Label = mkCap("QTY AFFECTED", 0)
        Dim capWhat As Label = mkCap("WHAT IS WRONG", 96)
        Dim capAction As Label = mkCap("WHAT HAPPENS NEXT", 0)
        issuePanel.Controls.AddRange({capQty, numQty, capWhat, txtWhat, capAction, cmbAction})
        row.Controls.Add(issuePanel)

        Dim layoutIssue As Action =
            Sub()
                Dim w As Integer = issuePanel.Width
                cmbAction.Left = w - cmbAction.Width
                capAction.Left = cmbAction.Left
                txtWhat.Width = Math.Max(120, cmbAction.Left - 16 - txtWhat.Left)
            End Sub
        AddHandler issuePanel.Resize, Sub() layoutIssue()

        Dim applyState As Action =
            Sub()
                ln.IssueQty = CInt(numQty.Value)
                ln.IssueDetails = txtWhat.Text.Trim()
                ln.IssueAction = If(cmbAction.SelectedItem Is Nothing, "", cmbAction.SelectedItem.ToString())
                ln.Recompute()
                qtyTile.Invalidate()
            End Sub

        AddHandler cmbCond.SelectedIndexChanged,
            Sub()
                Select Case cmbCond.SelectedIndex
                    Case 1 : ln.IssueType = DeliveryIssues.TYPE_DEFECTIVE
                    Case 2 : ln.IssueType = DeliveryIssues.TYPE_MISSING
                    Case Else : ln.IssueType = ""
                End Select

                cmbAction.Items.Clear()
                If ln.IssueType = DeliveryIssues.TYPE_DEFECTIVE Then
                    cmbAction.Items.AddRange({DeliveryIssues.ACTION_RETURN, DeliveryIssues.ACTION_ACCEPT})
                    cmbAction.Enabled = True
                    capWhat.Text = "WHAT IS WRONG  *"
                ElseIf ln.IssueType = DeliveryIssues.TYPE_MISSING Then
                    cmbAction.Items.Add(DeliveryIssues.ACTION_AWAITING)
                    cmbAction.Enabled = False
                    capWhat.Text = "NOTE  (optional)"
                End If
                If cmbAction.Items.Count > 0 Then cmbAction.SelectedIndex = 0

                issuePanel.Visible = ln.HasIssue
                row.Height = If(ln.HasIssue, ROW_BASE_H + ROW_ISSUE_H, ROW_BASE_H)
                applyState()
                row.Invalidate()
                relayout()
                If ln.HasIssue Then txtWhat.Focus()
            End Sub
        AddHandler numQty.ValueChanged, Sub() applyState()
        AddHandler txtWhat.TextChanged, Sub() applyState()
        AddHandler cmbAction.SelectedIndexChanged, Sub() applyState()

        ' A line with nothing left to receive has no condition to set.
        If ln.Remaining = 0 Then
            cmbCond.Enabled = False
        End If

        AddHandler row.Resize,
            Sub()
                qtyTile.Left = row.Width - qtyTile.Width - 16
                qtyTile.Top = (ROW_BASE_H - qtyTile.Height) \ 2
                cmbCond.Left = qtyTile.Left - 18 - cmbCond.Width
                cmbCond.Top = 36
                lblCond.Left = cmbCond.Left
                lblCond.Top = 17
                issuePanel.Width = Math.Max(300, qtyTile.Right - textX)
                Dim rightStop As Integer = cmbCond.Left - 14
                lblName.MaximumSize = New Size(Math.Max(80, rightStop - textX), 28)
                lblSku.MaximumSize = New Size(Math.Max(80, rightStop - textX), 22)
                lblOrderInfo.MaximumSize = New Size(Math.Max(80, rightStop - textX), 24)
            End Sub

        Return row
    End Function

    ' =========================================================================
    '  SAVE — single transaction, mirrors CommitAdjustment()
    ' =========================================================================
    Private Shared Sub CommitReceive()
        If Not Permissions.Require(Permissions.ReceiveGoods, "receive deliveries") Then Return
        If btnSaveRef IsNot Nothing AndAlso CStr(If(btnSaveRef.Tag, "")) = "done" Then Return

        ' Collect lines actually being received, and lines with a problem.
        Dim toReceive As New List(Of ReceiveLine)
        Dim issueLines As New List(Of ReceiveLine)
        For Each ln In receiveLines
            ln.Recompute()
            If ln.ReceiveNow < 0 Then
                AppModal.Show("Received quantity cannot be negative." & Environment.NewLine &
                              "Check: " & ln.ProductName, "Invalid Quantity",
                              MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End If
            If ln.HasIssue Then
                If ln.IssueQty < 1 OrElse ln.IssueQty > ln.Remaining Then
                    AppModal.Show("The quantity affected must be between 1 and " & ln.Remaining & "." &
                                  Environment.NewLine & "Check: " & ln.ProductName, "Check the Quantity",
                                  MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    Return
                End If
                If ln.IssueType = DeliveryIssues.TYPE_DEFECTIVE AndAlso ln.IssueDetails = "" Then
                    AppModal.Show("Say what is wrong with the defective " & ln.ProductName & "." &
                                  Environment.NewLine & "For example: dented cans, wrong colour, leaking.",
                                  "Describe the Defect", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    Return
                End If
                If ln.IssueAction = "" Then
                    AppModal.Show("Choose what happens next for " & ln.ProductName & ".",
                                  "Choose an Action", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    Return
                End If
                issueLines.Add(ln)
            End If
            If ln.ReceiveNow > 0 Then toReceive.Add(ln)
        Next

        If toReceive.Count = 0 AndAlso issueLines.Count = 0 Then
            AppModal.Show("Enter a quantity on at least one line to receive.", "Nothing to Receive",
                          MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        ' Problems are stored in goods_receipt_issues (migrations.sql section 22).
        ' Without it they could not be recorded, so stop rather than lose them.
        If issueLines.Count > 0 AndAlso Not DeliveryIssues.HasTable() Then
            AppModal.Show("Recording defective or missing items needs the latest database update" &
                          Environment.NewLine & "(migrations.sql, section 22). Ask the admin to run it first.",
                          "Database Update Needed", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        ' Guard: cannot receive before the expected date
        Dim expectedDate As DateTime = DateTime.MinValue
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT expected_date FROM purchase_orders WHERE po_id = @id LIMIT 1", conn)
                    cmd.Parameters.AddWithValue("@id", currentPoId)
                    Using rdr = cmd.ExecuteReader()
                        If rdr.Read() AndAlso Not IsDBNull(rdr("expected_date")) Then
                            expectedDate = Convert.ToDateTime(rdr("expected_date"))
                        End If
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not verify the expected date:" & Environment.NewLine & ErrorLog.ForUser(ex),
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try

        If expectedDate <> DateTime.MinValue AndAlso DateTime.Today < expectedDate.Date Then
            AppModal.Show("This purchase order cannot be received before its expected date." &
                          Environment.NewLine & Environment.NewLine &
                          "Expected date: " & expectedDate.ToString("MMM d, yyyy") &
                          Environment.NewLine &
                          "Today: " & DateTime.Today.ToString("MMM d, yyyy"),
                          "Too Early to Receive",
                          MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        ' Decide the resulting PO status from cumulative receipts
        Dim fullyReceived As Boolean = True
        For Each ln In receiveLines
            If (ln.AlreadyReceived + ln.ReceiveNow) < ln.OrderedQty Then
                fullyReceived = False
                Exit For
            End If
        Next
        Dim newStatus As String = If(fullyReceived, "Received", "Partially Received")
        Dim note As String = "Received via PO " & currentPoNo
        Dim unitsNow As Integer = toReceive.Sum(Function(l) l.ReceiveNow)
        ' Nothing booked in (the whole delivery went back, or never came): the
        ' problems are recorded but the PO keeps the status it had.
        Dim changeStatus As Boolean = unitsNow > 0

        ' One readable line per problem, for the attestation remarks, the log
        ' and the printed receipt.
        Dim issueText As New List(Of String)
        For Each ln In issueLines
            issueText.Add(ln.ProductName & ": " & ln.IssueType & " " & ln.IssueQty &
                          If(ln.IssueDetails = "", "", " (" & ln.IssueDetails & ")") &
                          " - " & ln.IssueAction)
        Next

        ' ── ATTESTATION GATE ─────────────────────────────────────────────────
        ' Nothing is written until a named person confirms they inspected this
        ' delivery AND re-enters their own password. Receiving is what makes
        ' the payable real, so it must not be possible from an unattended
        ' terminal, and "who received it" must be a recorded claim rather than
        ' an inference from whoever was signed in.
        '
        ' Placed after every validation above so the receiver is only asked to
        ' certify a receipt that would actually save.
        Dim att As ReceiveConfirmModal.Attestation =
            ReceiveConfirmModal.Prompt(currentPoNo, currentSupplierName,
                                       toReceive.Count, unitsNow,
                                       presetRemarks:=If(issueText.Count = 0, Nothing,
                                                         String.Join("; ", issueText)))
        If att Is Nothing Then Return   ' backed out — nothing written

        ' The attestation row only lands where the table exists; on an
        ' un-migrated database the receipt still saves, it just carries no
        ' stored attestation, which is the pre-existing behaviour.
        Dim canStoreAttestation As Boolean = ReceiveConfirmModal.HasTable()

        btnSaveRef.Tag = "done"
        Try
            Using conn As MySqlConnection = Db.GetConnection()
                conn.Open()
                Using txn As MySqlTransaction = conn.BeginTransaction()
                    Try
                        For Each ln In toReceive
                            ' INSERT stock movement (Purchase / receive).
                            ' Records goods confirmed arrived from supplier.
                            ' products.stock_qty is NOT updated here — inventory
                            ' updates it in Phase 2 via the Put Away screen.
                            Using cmd As New MySqlCommand(
                                "INSERT INTO stock_movements " &
                                "(product_id, user_id, movement_type, qty_change, reference_type, reference_id, note, created_at) " &
                                "VALUES (@pid, @uid, 'Purchase', @qc, 'receive', @poid, @note, NOW())", conn, txn)
                                cmd.Parameters.AddWithValue("@pid", ln.ProductId)
                                cmd.Parameters.AddWithValue("@uid", Session.CurrentUserId)
                                cmd.Parameters.AddWithValue("@qc", ln.ReceiveNow)
                                cmd.Parameters.AddWithValue("@poid", currentPoId)
                                cmd.Parameters.AddWithValue("@note", note)
                                cmd.ExecuteNonQuery()
                            End Using
                        Next

                        ' Update PO status (+ received_at when fully received)
                        If Not changeStatus Then
                            ' Nothing booked in: keep the PO's status.
                        ElseIf fullyReceived Then
                            Using cmd As New MySqlCommand(
                                "UPDATE purchase_orders SET status = 'Received', received_at = NOW() " &
                                "WHERE po_id = @poid", conn, txn)
                                cmd.Parameters.AddWithValue("@poid", currentPoId)
                                cmd.ExecuteNonQuery()
                            End Using
                        Else
                            Using cmd As New MySqlCommand(
                                "UPDATE purchase_orders SET status = 'Partially Received' " &
                                "WHERE po_id = @poid", conn, txn)
                                cmd.Parameters.AddWithValue("@poid", currentPoId)
                                cmd.ExecuteNonQuery()
                            End Using
                        End If

                        ' The attestation commits with the movements it
                        ' describes: an attestation for goods never booked in,
                        ' or movements with no attestation, are both worse than
                        ' the old behaviour, so they share one transaction.
                        Dim receiptId As Integer = 0
                        If canStoreAttestation Then
                            receiptId = ReceiveConfirmModal.Save(conn, txn, currentPoId, att, unitsNow)
                        End If

                        ' Problem lines commit with the receipt they belong to.
                        For Each ln In issueLines
                            DeliveryIssues.Save(conn, txn, receiptId, currentPoId, ln.PoItemId,
                                                ln.ProductId, ln.IssueType, ln.IssueQty,
                                                ln.IssueDetails, ln.IssueAction)
                        Next

                        txn.Commit()

                        ' Receiving is where goods are accepted from a supplier and
                        ' the liability becomes real, so it belongs in the audit log.
                        ' The receiver's name is recorded here too, so the log
                        ' answers "who accepted this" without a second lookup.
                        ActivityLog.Record("PO_RECEIVED", "purchase_order", currentPoId,
                            If(Not changeStatus, "Nothing booked in",
                               If(fullyReceived, "Fully received", "Partially received")) &
                            ", " & unitsNow & " unit(s)" &
                            If(issueLines.Count = 0, "",
                               ", " & issueLines.Count & " problem(s): " & String.Join("; ", issueText)) &
                            ", received by " & att.ReceiverName &
                            If(att.ConditionNote = "Discrepancy",
                               " (DISCREPANCY: " & att.Remarks & ")", " (good condition)"))
                    Catch exInner As Exception
                        txn.Rollback()
                        If btnSaveRef IsNot Nothing Then btnSaveRef.Tag = ""
                        AppModal.Show("Receipt was not saved:" & Environment.NewLine & exInner.Message,
                                      "Could Not Save", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                        Return
                    End Try
                End Using
            End Using
        Catch ex As Exception
            If btnSaveRef IsNot Nothing Then btnSaveRef.Tag = ""
            AppModal.Show("Could not reach the database:" & Environment.NewLine & ErrorLog.ForUser(ex),
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try

        ShowReceiveReceipt(currentPoNo, currentSupplierName, toReceive,
                           If(changeStatus, newStatus, "Unchanged (nothing booked in)"), att, issueLines)
        RenderList()
    End Sub

    ' =========================================================================
    '  DATA LOADERS
    ' =========================================================================
    Private Shared Function LoadReceivableList() As List(Of ReceivePORow)
        Dim result As New List(Of ReceivePORow)
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT po.po_id, po.po_no, s.name AS sname, po.expected_date, po.status, " &
                    "(SELECT COUNT(*) FROM po_items pi WHERE pi.po_id = po.po_id) AS item_count " &
                    "FROM purchase_orders po JOIN suppliers s ON po.supplier_id = s.supplier_id " &
                    "WHERE po.status IN ('Ordered','Partially Received') " &
                    "ORDER BY (po.expected_date IS NULL), po.expected_date ASC, po.po_id ASC", conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            Dim hasExp As Boolean = Not IsDBNull(rdr("expected_date"))
                            result.Add(New ReceivePORow With {
                                .PoId = SafeInt(rdr("po_id")),
                                .PoNo = rdr("po_no").ToString(),
                                .SupplierName = rdr("sname").ToString(),
                                .HasExpected = hasExp,
                                .ExpectedDate = If(hasExp, Convert.ToDateTime(rdr("expected_date")), DateTime.MinValue),
                                .Status = rdr("status").ToString(),
                                .ItemCount = SafeInt(rdr("item_count"))
                            })
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load receivable purchase orders:" & Environment.NewLine & ErrorLog.ForUser(ex),
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
        Return result
    End Function

    ' =========================================================================
    '  RECEIPT
    ' =========================================================================
    Private Shared Sub ShowReceiveReceipt(poNo As String, supplierName As String,
                                          receivedLines As List(Of ReceiveLine),
                                          newStatus As String,
                                          att As ReceiveConfirmModal.Attestation,
                                          issueLines As List(Of ReceiveLine))
        Dim lines As New List(Of RLine)

        lines.Add(New RLine("Purchase Order", poNo, LineStyle.Bold))
        lines.Add(New RLine("Supplier", supplierName))
        lines.Add(New RLine("Date Received", DateTime.Now.ToString("MMM d, yyyy  h:mm tt")))
        lines.Add(New RLine("Status", newStatus))
        ' Who accepted the delivery, on the document itself — a receipt that
        ' does not name its receiver cannot be used to settle a dispute later.
        If att IsNot Nothing Then
            lines.Add(New RLine("Received By", att.ReceiverName, LineStyle.Bold))
            lines.Add(New RLine("Condition",
                                If(att.ConditionNote = "Discrepancy",
                                   "With discrepancy", "Good condition")))
        End If
        lines.Add(Divider())

        lines.Add(Section("Items Received"))
        For Each ln In receivedLines
            If ln.ReceiveNow > 0 Then
                lines.Add(New RLine(ln.ProductName, "x" & ln.ReceiveNow, LineStyle.Bold))
                lines.Add(New RLine("SKU: " & ln.Sku, "", LineStyle.Normal))
                lines.Add(Spacer())
            End If
        Next
        lines.Add(Divider())

        Dim totalUnits As Integer = receivedLines.Where(Function(l) l.ReceiveNow > 0).Sum(Function(l) l.ReceiveNow)
        Dim totalLines As Integer = receivedLines.Where(Function(l) l.ReceiveNow > 0).Count()
        lines.Add(New RLine("Total Products", totalLines & If(totalLines = 1, " product", " products")))
        lines.Add(New RLine("Total Units", totalUnits & If(totalUnits = 1, " unit", " units"), LineStyle.Bold))

        ' Defective / missing items, with what happens to each.
        If issueLines IsNot Nothing AndAlso issueLines.Count > 0 Then
            lines.Add(Divider())
            lines.Add(Section("Problems Found"))
            For Each ln In issueLines
                lines.Add(New RLine(ln.ProductName, ln.IssueType & " x" & ln.IssueQty, LineStyle.Bold))
                If ln.IssueDetails <> "" Then lines.Add(New RLine(ln.IssueDetails, "", LineStyle.Normal))
                lines.Add(New RLine("Action: " & ln.IssueAction, "", LineStyle.Muted))
                lines.Add(Spacer())
            Next
        End If

        ' The attestation printed on the document. A discrepancy is called out
        ' rather than tucked away: it is the part someone will need to act on.
        If att IsNot Nothing Then
            lines.Add(Spacer())
            If att.ConditionNote = "Discrepancy" AndAlso att.Remarks <> "" Then
                lines.Add(Section("Discrepancy Noted"))
                lines.Add(New RLine(att.Remarks, "", LineStyle.Normal))
            ElseIf att.Remarks <> "" Then
                lines.Add(Section("Remarks"))
                lines.Add(New RLine(att.Remarks, "", LineStyle.Normal))
            End If
            lines.Add(Spacer())
            ' The wording follows the condition: claiming the goods were
            ' "correct" under a discrepancy note would contradict the line
            ' printed directly above it.
            lines.Add(New RLine(
                If(att.ConditionNote = "Discrepancy",
                   "Inspected by " & att.ReceiverName & ", received with the discrepancy noted above.",
                   "Certified by " & att.ReceiverName & " as inspected and correct."),
                "", LineStyle.Muted))
        End If

        lines.Add(Spacer())
        lines.Add(New RLine("Goods are pending Put Away by Inventory staff.", "", LineStyle.Muted))

        ReceiptPrinter.Preview(
            "Goods Receipt — " & poNo,
            lines,
            receiptNo:=poNo,
            storeName:="PAINT STORE",
            storeTagline:="Goods Receipt Confirmation")
    End Sub

    Private Shared Function LoadReceiveLines(poId As Integer) As List(Of ReceiveLine)
        Dim result As New List(Of ReceiveLine)
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT pi.po_item_id, pi.product_id, p.name AS pname, p.sku, p.image_path, pi.qty, " &
                    "COALESCE((SELECT SUM(sm.qty_change) FROM stock_movements sm " &
                    " WHERE sm.reference_id = @poid AND sm.reference_type = 'receive' " &
                    " AND sm.product_id = pi.product_id), 0) AS already " &
                    "FROM po_items pi JOIN products p ON pi.product_id = p.product_id " &
                    "WHERE pi.po_id = @poid ORDER BY p.name ASC", conn)
                    cmd.Parameters.AddWithValue("@poid", poId)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            Dim ordered As Integer = SafeInt(rdr("qty"))
                            Dim already As Integer = SafeInt(rdr("already"))
                            result.Add(New ReceiveLine With {
                                .PoItemId = SafeInt(rdr("po_item_id")),
                                .ProductId = SafeInt(rdr("product_id")),
                                .ProductName = rdr("pname").ToString(),
                                .Sku = rdr("sku").ToString(),
                                .ImagePath = If(IsDBNull(rdr("image_path")), "", rdr("image_path").ToString()),
                                .OrderedQty = ordered,
                                .AlreadyReceived = already,
                                .ReceiveNow = Math.Max(0, ordered - already)
                            })
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load the purchase order lines:" & Environment.NewLine & ErrorLog.ForUser(ex),
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
    '  SHARED PAINT / CONTROL HELPERS  (verbatim from InventoryLowStockPanelBuilder)
    ' =========================================================================
    Private Shared Sub PaintCard(panel As Panel, e As PaintEventArgs)
        PanelRenderer.Draw(panel, e, True)
    End Sub

    Private Shared Function MakeLink(text As String, clr As Color) As Label
        Dim lbl As New Label With {
            .Text = text, .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold),
            .ForeColor = clr, .AutoSize = True, .Cursor = Cursors.Hand, .BackColor = Color.Transparent
        }
        AddHandler lbl.MouseEnter, Sub() lbl.Font = New Font("Segoe UI", 9.5F, FontStyle.Bold Or FontStyle.Underline)
        AddHandler lbl.MouseLeave, Sub() lbl.Font = New Font("Segoe UI", 9.5F, FontStyle.Bold)
        Return lbl
    End Function

    Private Shared Function SafeInt(o As Object) As Integer
        If o Is Nothing OrElse IsDBNull(o) Then Return 0
        Dim n As Integer
        If Integer.TryParse(o.ToString(), n) Then Return n
        Return 0
    End Function

    Private Shared Function SafeIntStr(s As String) As Integer
        Dim n As Integer
        If Integer.TryParse(s, n) Then Return Math.Max(0, n)
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

    Private Sub ProcurementReceivePanelBuilder_Load(sender As Object, e As EventArgs) Handles MyBase.Load

    End Sub
End Class