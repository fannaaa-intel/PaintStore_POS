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
'  SAVE — the heart of this screen — replicates
'  InventoryStockAdjustmentPanelBuilder.CommitAdjustment() EXACTLY:
'    * everything in ONE MySqlTransaction
'    * FOR UPDATE re-read of products.stock_qty before each write
'    * INSERT stock_movements (movement_type='Purchase', reference_type='receive',
'      note='Received via PO ' & poNo)  then  UPDATE products.stock_qty += received
'    * rollback on any error
'  After the writes, the PO becomes 'Received' (all lines fully in) or
'  'Partially Received' (some still outstanding). reference_id = po_id lets us
'  track cumulative receipts so a PO can be received over several sessions.
'
'  Shared-only, no instance state. Verbatim helpers from
'  InventoryLowStockPanelBuilder.
' =============================================================================
Public Class ProcurementReceivePanelBuilder

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
        Public OrderedQty As Integer
        Public AlreadyReceived As Integer
        Public ReceiveNow As Integer
        Public ReadOnly Property Remaining As Integer
            Get
                Return Math.Max(0, OrderedQty - AlreadyReceived)
            End Get
        End Property
    End Class

    ' ── STATE ────────────────────────────────────────────────────────────────────
    Private Shared containerRef As Panel
    Private Shared receiveLines As New List(Of ReceiveLine)
    Private Shared currentPoId As Integer
    Private Shared currentPoNo As String
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
        Return region
    End Function

    Private Shared Function BuildReceiveCard(po As ReceivePORow) As Panel
        Dim card As New BufferedPanel With {.Height = 84, .BackColor = CardBg, .Cursor = Cursors.Hand}
        AddHandler card.Paint, Sub(s, e) PaintCard(CType(s, Panel), e)

        Dim lblPo As New Label With {
            .Text = po.PoNo, .Font = New Font("Segoe UI", 12, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(20, 14), .BackColor = Color.Transparent
        }
        Dim lblMeta As New Label With {
            .Text = po.SupplierName & "   " & ChrW(8226) & "   " &
                    If(po.HasExpected, "Expected " & po.ExpectedDate.ToString("MMM d, yyyy"), "No expected date") &
                    "   " & ChrW(8226) & "   " & po.ItemCount & If(po.ItemCount = 1, " item", " items"),
            .Font = New Font("Segoe UI", 9.5F), .ForeColor = SubText, .AutoSize = True,
            .Location = New Point(20, 44), .BackColor = Color.Transparent
        }
        card.Controls.AddRange({lblPo, lblMeta})

        Dim st = StatusInfo(po.Status)
        Dim badge As New Label With {
            .Text = st.text, .Font = New Font("Segoe UI", 8.5F, FontStyle.Bold),
            .ForeColor = st.fore, .BackColor = st.back, .AutoSize = True,
            .Padding = New Padding(9, 4, 9, 4), .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        RoundLabel(badge, 11)
        card.Controls.Add(badge)

        Dim lnkReceive As Label = MakeLink("Receive " & ChrW(8594), MintPrimary)
        lnkReceive.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        card.Controls.Add(lnkReceive)

        Dim capId As Integer = po.PoId
        AddHandler lnkReceive.Click, Sub() LoadReceiveForm(capId)
        AddHandler card.Click, Sub() LoadReceiveForm(capId)
        AddHandler lblPo.Click, Sub() LoadReceiveForm(capId)
        AddHandler lblMeta.Click, Sub() LoadReceiveForm(capId)

        AddHandler card.Resize,
            Sub()
                lnkReceive.Left = card.Width - lnkReceive.Width - 22
                lnkReceive.Top = 46
                badge.Left = card.Width - badge.Width - 22
                badge.Top = 16
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
            AppModal.Show("Could not load the purchase order:" & Environment.NewLine & ex.Message,
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
        receiveLines = LoadReceiveLines(poId)

        container.SuspendLayout()
        container.Controls.Clear()
        container.BackColor = MintBg

        Dim body As New Panel With {
            .Dock = DockStyle.Fill, .BackColor = MintBg, .Padding = New Padding(24, 4, 24, 20), .AutoScroll = True
        }

        ' ---- Lines card ----
        Dim linesHeight As Integer = 64 + Math.Max(1, receiveLines.Count) * 64 + 14
        Dim linesCard As New BufferedPanel With {.Height = linesHeight, .BackColor = CardBg, .Dock = DockStyle.Top}
        AddHandler linesCard.Paint, Sub(s, e) PaintCard(CType(s, Panel), e)

        Dim lblHead As New Label With {
            .Text = "Line Items", .Font = New Font("Segoe UI", 13, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(20, 14), .BackColor = Color.Transparent
        }
        Dim lblHint As New Label With {
            .Text = "Enter how many of each you're receiving now. Partial receipts are fine.",
            .Font = New Font("Segoe UI", 8.5F), .ForeColor = FaintText, .AutoSize = True,
            .Location = New Point(22, 40), .BackColor = Color.Transparent
        }
        linesCard.Controls.AddRange({lblHead, lblHint})

        If receiveLines.Count = 0 Then
            Dim lbl As New Label With {
                .Text = "This purchase order has no line items.", .Font = New Font("Segoe UI", 10),
                .ForeColor = SubText, .AutoSize = True, .Location = New Point(20, 64), .BackColor = Color.Transparent
            }
            linesCard.Controls.Add(lbl)
        Else
            Dim y As Integer = 64
            For Each ln In receiveLines
                Dim r As Panel = BuildReceiveLineRow(ln, 64)
                r.Location = New Point(0, y)
                r.Width = linesCard.ClientSize.Width - 4
                r.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
                linesCard.Controls.Add(r)
                y += 64
            Next
            AddHandler linesCard.Resize,
                Sub()
                    Dim w As Integer = linesCard.ClientSize.Width - 4
                    For Each c As Control In linesCard.Controls
                        If TypeOf c Is Panel Then c.Width = w
                    Next
                End Sub
        End If

        body.Controls.Add(linesCard)

        ' ---- Header bar ----
        Dim header As New Panel With {.Height = 110, .BackColor = MintBg, .Padding = New Padding(24, 18, 24, 0), .Dock = DockStyle.Top}
        Dim lblTitle As New Label With {
            .Text = "Receive " & poNo, .Font = New Font("Segoe UI", 22, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(24, 14)
        }
        Dim lblMeta As New Label With {
            .Text = supplierName & "   " & ChrW(8226) & "   " &
                    If(hasExp, "Expected " & expDate.ToString("MMM d, yyyy"), "No expected date"),
            .Font = New Font("Segoe UI", 10), .ForeColor = SubText, .AutoSize = True, .Location = New Point(26, 54)
        }
        Dim lnkBack As Label = MakeLink(ChrW(8592) & " Back to list", SubText)
        lnkBack.Location = New Point(26, 78)
        AddHandler lnkBack.Click, Sub() RenderList()
        header.Controls.AddRange({lblTitle, lblMeta, lnkBack})

        btnSaveRef = New Button With {
            .Text = "Save Receipt", .Size = New Size(150, 42),
            .FlatStyle = FlatStyle.Flat, .BackColor = MintPrimary, .ForeColor = Color.White,
            .Font = New Font("Segoe UI Semibold", 10), .Cursor = Cursors.Hand,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right, .Tag = ""
        }
        btnSaveRef.FlatAppearance.BorderSize = 0
        RoundControl(btnSaveRef, 10)
        AddHandler btnSaveRef.Click, Sub() CommitReceive()
        header.Controls.Add(btnSaveRef)
        AddHandler header.Resize, Sub()
                                      btnSaveRef.Top = 26
                                      btnSaveRef.Left = header.Width - 24 - btnSaveRef.Width
                                  End Sub
        btnSaveRef.Top = 26
        btnSaveRef.Left = 700

        container.Controls.Add(body)
        container.Controls.Add(header)
        container.ResumeLayout()
    End Sub

    Private Shared Function BuildReceiveLineRow(ln As ReceiveLine, height As Integer) As Panel
        Dim row As New Panel With {.Height = height, .BackColor = Color.Transparent}
        AddHandler row.Paint,
            Sub(s, e)
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 20, row.Height - 1, row.Width - 20, row.Height - 1)
                End Using
            End Sub

        Dim lblName As New Label With {
            .Text = ln.ProductName, .Font = New Font("Segoe UI", 10.5F, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(20, 12), .BackColor = Color.Transparent
        }
        Dim lblMeta As New Label With {
            .Text = ln.Sku & "   " & ChrW(8226) & "   Ordered " & ln.OrderedQty &
                    "   " & ChrW(8226) & "   Already received " & ln.AlreadyReceived,
            .Font = New Font("Segoe UI", 8.5F), .ForeColor = SubText, .AutoSize = True,
            .Location = New Point(20, 35), .BackColor = Color.Transparent
        }
        row.Controls.AddRange({lblName, lblMeta})

        ' Remaining badge
        Dim remBadge As New Label With {
            .Text = ln.Remaining & " left", .Font = New Font("Segoe UI", 8, FontStyle.Bold),
            .ForeColor = If(ln.Remaining > 0, WarningClr, PositiveClr),
            .BackColor = If(ln.Remaining > 0, Color.FromArgb(252, 240, 228), Color.FromArgb(232, 245, 233)),
            .AutoSize = True, .Padding = New Padding(8, 3, 8, 3),
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        RoundLabel(remBadge, 10)
        row.Controls.Add(remBadge)

        ' Receive-now input
        Dim lblCap As New Label With {
            .Text = "Receive now", .Font = New Font("Segoe UI", 7.5F, FontStyle.Bold), .ForeColor = FaintText,
            .AutoSize = True, .BackColor = Color.Transparent, .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        Dim txtQty As New TextBox With {
            .Text = ln.ReceiveNow.ToString(), .Font = New Font("Segoe UI", 11), .Width = 70,
            .TextAlign = HorizontalAlignment.Center, .BorderStyle = BorderStyle.FixedSingle,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        row.Controls.AddRange({lblCap, txtQty})

        Dim capLine As ReceiveLine = ln
        AddHandler txtQty.TextChanged,
            Sub()
                capLine.ReceiveNow = SafeIntStr(txtQty.Text)
            End Sub

        AddHandler row.Resize,
            Sub()
                txtQty.Left = row.Width - txtQty.Width - 24
                txtQty.Top = (height - txtQty.Height) \ 2 + 4
                lblCap.Left = txtQty.Left
                lblCap.Top = txtQty.Top - 15
                remBadge.Left = txtQty.Left - remBadge.Width - 18
                remBadge.Top = (height - remBadge.Height) \ 2
            End Sub

        Return row
    End Function

    ' =========================================================================
    '  SAVE — single transaction, mirrors CommitAdjustment()
    ' =========================================================================
    Private Shared Sub CommitReceive()
        If btnSaveRef IsNot Nothing AndAlso CStr(If(btnSaveRef.Tag, "")) = "done" Then Return

        ' Collect lines actually being received
        Dim toReceive As New List(Of ReceiveLine)
        For Each ln In receiveLines
            If ln.ReceiveNow < 0 Then
                AppModal.Show("Received quantity cannot be negative." & Environment.NewLine &
                              "Check: " & ln.ProductName, "Invalid Quantity",
                              MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End If
            If ln.ReceiveNow > 0 Then toReceive.Add(ln)
        Next

        If toReceive.Count = 0 Then
            AppModal.Show("Enter a quantity on at least one line to receive.", "Nothing to Receive",
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

        btnSaveRef.Tag = "done"
        Try
            Using conn As MySqlConnection = Db.GetConnection()
                conn.Open()
                Using txn As MySqlTransaction = conn.BeginTransaction()
                    Try
                        For Each ln In toReceive
                            ' FOR UPDATE re-read of current stock (mirrors CommitAdjustment)
                            Dim avail As Integer = 0
                            Using cmd As New MySqlCommand(
                                "SELECT stock_qty FROM products WHERE product_id = @pid FOR UPDATE", conn, txn)
                                cmd.Parameters.AddWithValue("@pid", ln.ProductId)
                                avail = Convert.ToInt32(cmd.ExecuteScalar())
                            End Using

                            ' INSERT stock movement (Purchase / receive)
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

                            ' UPDATE stock_qty += received
                            Using cmd As New MySqlCommand(
                                "UPDATE products SET stock_qty = stock_qty + @qc WHERE product_id = @pid", conn, txn)
                                cmd.Parameters.AddWithValue("@qc", ln.ReceiveNow)
                                cmd.Parameters.AddWithValue("@pid", ln.ProductId)
                                cmd.ExecuteNonQuery()
                            End Using
                        Next

                        ' Update PO status (+ received_at when fully received)
                        If fullyReceived Then
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

                        txn.Commit()
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
            AppModal.Show("Could not reach the database:" & Environment.NewLine & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try

        Dim totalUnits As Integer = 0
        For Each ln In toReceive
            totalUnits += ln.ReceiveNow
        Next
        AppModal.Show(totalUnits & If(totalUnits = 1, " unit", " units") & " received into stock." & Environment.NewLine &
                      currentPoNo & " is now " & newStatus & ".", "Goods Received",
                      MessageBoxButtons.OK, MessageBoxIcon.Information)
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
            AppModal.Show("Could not load receivable purchase orders:" & Environment.NewLine & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
        Return result
    End Function

    Private Shared Function LoadReceiveLines(poId As Integer) As List(Of ReceiveLine)
        Dim result As New List(Of ReceiveLine)
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT pi.po_item_id, pi.product_id, p.name AS pname, p.sku, pi.qty, " &
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
                                .OrderedQty = ordered,
                                .AlreadyReceived = already,
                                .ReceiveNow = Math.Max(0, ordered - already)   ' default to remaining
                            })
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load the purchase order lines:" & Environment.NewLine & ex.Message,
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

End Class