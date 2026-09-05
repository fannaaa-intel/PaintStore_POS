Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.IO
Imports System.Windows.Forms
Imports MySql.Data.MySqlClient

' =============================================================================
'  InventoryStockAdjustmentPanelBuilder
'  Stock Adjustment screen for Inventory Staff.
'
'  PURPOSE
'  -------
'  The ONE place a human manually reconciles a product's quantity to physical
'  reality, and every change is logged. Mirrors the write pattern POS already
'  uses: a row in stock_movements (the audit trail) PLUS an UPDATE to
'  products.stock_qty, both inside a single transaction so they can never
'  drift apart.
'
'  NOTE: New purchased goods must ONLY enter stock via Procurement → Receive PO.
'  This screen can never create a 'Purchase' movement — it is for reconciliation
'  only, not receiving.
'
'  This is the destination for the "Adjust" link on the Stock Levels screen: that
'  link calls INVENTORY_STAFF.OpenStockAdjustmentFor(productId), which loads this
'  panel with the product pre-selected.
'
'  WHAT YOU SEE
'  ------------
'     1. Target product card — pre-filled when arriving from Stock Levels
'        (thumbnail, name, SKU, current qty shown large), or a search box to
'        find a product when opened fresh from the nav.
'     2. Adjustment form — reason picker (Correction or Damage/Loss), a signed
'        quantity input (+n to add, -n to remove), a required note, and a live
'        Current -> New preview, then Save / Cancel.
'     3. Recent history — the last few stock_movements for the product, read-only.
'
'  REASON -> MOVEMENT MODEL
'  ------------------------
'     Correction -> movement_type 'Adjustment', signed (recount fix: + or -)
'     Damage     -> movement_type 'Adjustment', qty stored negative (loss)
'  reference_type carries the human reason ("correction" / "damage")
'  so history reads clearly.  Historical "receive" rows written by Procurement
'  are still displayed correctly in Recent Movements.
'
'  Honors is_active = 1 so archived products never appear.
' =============================================================================
Public Class InventoryStockAdjustmentPanelBuilder

    ' ── PALETTE (identical to the other inventory panels) ─────────────────────────
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

    Private Const PaintCategory As String = "Paint"

    ' Reason keys (lowercase so they can never case-collide with Color constants).
    Private Const ReasonReceive As String = "receive"
    Private Const ReasonCorrection As String = "correction"
    Private Const ReasonDamage As String = "damage"

    ' Quantity entry mode (only Change remains; Set Total was removed — stock can only
    ' be reconciled to physical counts, never set to an arbitrary number).
    Private Const ModeChange As String = "change"

    ' ── PRODUCT MODEL ─────────────────────────────────────────────────────────────
    Private Class AdjItem
        Public Id As Integer
        Public Sku As String
        Public Name As String
        Public CategoryName As String
        Public ColorHex As String
        Public ImagePath As String
        Public UnitPrice As Decimal
        Public StockQty As Integer
        Public ReorderLevel As Integer

        Public ReadOnly Property IsPaint As Boolean
            Get
                Return String.Equals(CategoryName, PaintCategory, StringComparison.OrdinalIgnoreCase)
            End Get
        End Property
    End Class

    Private Class MovementRow
        Public MovementType As String
        Public QtyChange As Integer
        Public RefType As String
        Public Note As String
        Public CreatedAt As DateTime
        Public UserName As String
    End Class

    ' ── STATE ─────────────────────────────────────────────────────────────────────
    Private Shared containerRef As Panel
    Private Shared selected As AdjItem
    Private Shared activeReason As String = ReasonCorrection
    Private Shared entryMode As String = ModeChange

    ' ── CONTROL REFERENCES ──────────────────────────────────────────────────────────
    Private Shared formHost As Panel
    Private Shared targetHost As Panel
    Private Shared historyHost As Panel
    Private Shared searchResultsHost As Panel
    Private Shared qtyBox As TextBox
    Private Shared noteBox As TextBox
    Private Shared lblPreview As Label
    Private Shared lblPreviewSub As Label
    Private Shared lblModeHint As Label
    Private Shared lblReasonInfo As Label    ' contextual sentence below the reason picker
    Private Shared btnSave As Panel
    Private Shared reasonCards As New List(Of (panel As Panel, reason As String))
    Private Shared modeTabs As New List(Of (panel As Panel, mode As String))

    Private Shared ReadOnly thumbCache As New Dictionary(Of String, Image)

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Shared Sub LoadStockAdjustment(container As Panel, Optional productId As Integer = 0)
        containerRef = container
        activeReason = ReasonCorrection
        entryMode = ModeChange
        reasonCards.Clear()
        modeTabs.Clear()
        ImageCache.ClearAndDispose(thumbCache, containerRef)

        selected = Nothing
        If productId > 0 Then selected = LoadProduct(productId)

        container.SuspendLayout()
        container.Controls.Clear()
        container.BackColor = MintBg

        ' ---- Scrollable body so the form + history fit any window ----
        Dim scroller As New Panel With {
            .Dock = DockStyle.Fill, .BackColor = MintBg, .AutoScroll = True, .Padding = New Padding(0)
        }
        Dim body As New Panel With {.Location = New Point(0, 0), .BackColor = Color.Transparent, .AutoSize = False, .AutoScroll = False}
        body.Width = Math.Max(720, container.ClientSize.Width - 48)
        body.Location = New Point(24, 16)
        AddHandler scroller.Resize, Sub() body.Width = Math.Max(720, scroller.ClientSize.Width - 48)
        ' ---- Left column: target + form. Right column: history. ----
        Dim columns As New TableLayoutPanel With {
            .Location = New Point(0, 8), .ColumnCount = 2, .RowCount = 1,
            .BackColor = Color.Transparent, .AutoSize = True, .Width = body.Width,
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        }
        columns.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 58))
        columns.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 42))
        columns.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        AddHandler body.Resize, Sub() columns.Width = body.Width

        Dim leftCol As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.Transparent, .AutoSize = True, .Margin = New Padding(0, 0, 12, 0)}
        Dim rightCol As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.Transparent, .AutoSize = True, .Margin = New Padding(12, 0, 0, 0)}

        ' AFTER
        targetHost = New Panel With {.Dock = DockStyle.Top, .BackColor = Color.Transparent, .Height = 140}

        Dim spacer As New Panel With {.Dock = DockStyle.Top, .BackColor = Color.Transparent, .Height = 12}

        formHost = New Panel With {.Dock = DockStyle.Top, .BackColor = Color.Transparent, .Height = 500}

        ' Controls added in REVERSE order for DockStyle.Top stacking
        leftCol.Controls.Add(formHost)
        leftCol.Controls.Add(spacer)
        leftCol.Controls.Add(targetHost)

        historyHost = New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.Transparent}
        rightCol.Controls.Add(historyHost)

        columns.Controls.Add(leftCol, 0, 0)
        columns.Controls.Add(rightCol, 1, 0)
        body.Controls.Add(columns)
        body.Height = Math.Max(660, 140 + 12 + 500 + 24)  ' targetHost + spacer + formHost + padding
        scroller.Controls.Add(body)

        ' Grow body height whenever columns auto-size taller than the fixed estimate
        AddHandler columns.Resize,
            Sub()
                Dim needed As Integer = columns.Top + columns.Height + 24
                If needed > body.Height Then body.Height = needed
            End Sub

        Dim header As Panel = BuildHeader()
        header.Dock = DockStyle.Top

        container.Controls.Add(scroller)
        container.Controls.Add(header)
        container.ResumeLayout()

        BuildTargetCard()
        BuildForm()
        BuildHistory()
        RefreshFormEnabled()
        UpdatePreview()
    End Sub

    Private Shared Function BuildHeader() As Panel
        Dim region As New Panel With {.Height = 104, .BackColor = MintBg, .Padding = New Padding(24, 0, 24, 0)}
        Dim lblTitle As New Label With {
            .Text = "Stock Adjustment", .Font = New Font("Segoe UI", 26, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(24, 18)
        }
        Dim lblSub As New Label With {
            .Text = "Reconcile stock counts and record losses " & ChrW(8226) & " every change is logged",
            .Font = New Font("Segoe UI", 12), .ForeColor = SubText, .AutoSize = True, .Location = New Point(26, 62)
        }

        Dim btnRefresh As New Button With {
            .Text = "↻  Refresh",
            .Size = New Size(100, 34),
            .FlatStyle = FlatStyle.Flat,
            .BackColor = Color.FromArgb(220, 235, 232),
            .ForeColor = SubText,
            .Font = New Font("Segoe UI", 9.5F),
            .Cursor = Cursors.Hand,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        btnRefresh.FlatAppearance.BorderSize = 0
        AddHandler btnRefresh.Paint,
            Sub(s, e)
                Dim bb = CType(s, Button)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect As Rectangle = bb.ClientRectangle
                rect.Inflate(-1, -1)
                Using path = RoundRect(rect, 8)
                    Using br As New SolidBrush(bb.BackColor)
                        e.Graphics.FillPath(br, path)
                    End Using
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
        AddHandler btnRefresh.Click,
            Sub()
                If containerRef IsNot Nothing Then
                    Dim capId As Integer = If(selected IsNot Nothing, selected.Id, 0)
                    LoadStockAdjustment(containerRef, capId)
                End If
            End Sub

        region.Controls.AddRange({lblTitle, lblSub, btnRefresh})

        Dim PositionRefresh As Action =
            Sub()
                btnRefresh.Top = (region.Height - btnRefresh.Height) \ 2
                btnRefresh.Left = region.Width - btnRefresh.Width - 24
            End Sub
        AddHandler region.Resize, Sub() PositionRefresh()
        PositionRefresh()

        Return region
    End Function

    ' =========================================================================
    '  DATA
    ' =========================================================================
    Private Shared Function LoadProduct(productId As Integer) As AdjItem
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT p.product_id, p.sku, p.name, c.name AS cname, p.color_hex, p.image_path, " &
                    " p.unit_price, p.stock_qty, p.reorder_level " &
                    "FROM products p JOIN categories c ON p.category_id = c.category_id " &
                    "WHERE p.product_id = @id AND p.is_active = 1", conn)
                    cmd.Parameters.AddWithValue("@id", productId)
                    Using rdr = cmd.ExecuteReader()
                        If rdr.Read() Then
                            Return New AdjItem With {
                                .Id = SafeInt(rdr("product_id")),
                                .Sku = rdr("sku").ToString(),
                                .Name = rdr("name").ToString(),
                                .CategoryName = rdr("cname").ToString(),
                                .ColorHex = If(IsDBNull(rdr("color_hex")), "", rdr("color_hex").ToString()),
                                .ImagePath = If(IsDBNull(rdr("image_path")), "", rdr("image_path").ToString()),
                                .UnitPrice = If(IsDBNull(rdr("unit_price")), 0D, Convert.ToDecimal(rdr("unit_price"))),
                                .StockQty = SafeInt(rdr("stock_qty")),
                                .ReorderLevel = SafeInt(rdr("reorder_level"))
                            }
                        End If
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load the product:" & Environment.NewLine & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
        Return Nothing
    End Function

    Private Shared Function SearchProducts(term As String) As List(Of AdjItem)
        Dim list As New List(Of AdjItem)
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT p.product_id, p.sku, p.name, c.name AS cname, p.color_hex, p.image_path, " &
                    " p.unit_price, p.stock_qty, p.reorder_level " &
                    "FROM products p JOIN categories c ON p.category_id = c.category_id " &
                    "WHERE p.is_active = 1 AND (p.name LIKE @t OR p.sku LIKE @t) " &
                    "ORDER BY p.name ASC LIMIT 12", conn)
                    cmd.Parameters.AddWithValue("@t", "%" & term & "%")
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            list.Add(New AdjItem With {
                                .Id = SafeInt(rdr("product_id")),
                                .Sku = rdr("sku").ToString(),
                                .Name = rdr("name").ToString(),
                                .CategoryName = rdr("cname").ToString(),
                                .ColorHex = If(IsDBNull(rdr("color_hex")), "", rdr("color_hex").ToString()),
                                .ImagePath = If(IsDBNull(rdr("image_path")), "", rdr("image_path").ToString()),
                                .UnitPrice = If(IsDBNull(rdr("unit_price")), 0D, Convert.ToDecimal(rdr("unit_price"))),
                                .StockQty = SafeInt(rdr("stock_qty")),
                                .ReorderLevel = SafeInt(rdr("reorder_level"))
                            })
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Search failed:" & Environment.NewLine & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
        Return list
    End Function

    Private Shared Function LoadHistory(productId As Integer) As List(Of MovementRow)
        Dim list As New List(Of MovementRow)
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT m.movement_type, m.qty_change, m.reference_type, m.note, m.created_at, " &
                    " COALESCE(u.username, '') AS uname " &
                    "FROM stock_movements m LEFT JOIN users u ON m.user_id = u.user_id " &
                    "WHERE m.product_id = @id ORDER BY m.created_at DESC, m.movement_id DESC LIMIT 8", conn)
                    cmd.Parameters.AddWithValue("@id", productId)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            list.Add(New MovementRow With {
                                .MovementType = rdr("movement_type").ToString(),
                                .QtyChange = SafeInt(rdr("qty_change")),
                                .RefType = If(IsDBNull(rdr("reference_type")), "", rdr("reference_type").ToString()),
                                .Note = If(IsDBNull(rdr("note")), "", rdr("note").ToString()),
                                .CreatedAt = If(IsDBNull(rdr("created_at")), DateTime.MinValue, Convert.ToDateTime(rdr("created_at"))),
                                .UserName = rdr("uname").ToString()
                            })
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            ' History is non-critical; show nothing rather than blocking the screen.
        End Try
        Return list
    End Function

    ' =========================================================================
    '  SAVE  (one transaction: stock_movements row + products.stock_qty update)
    ' =========================================================================
    ' Mirrors the canonical POS write exactly: insert the movement, then update
    ' the product, both inside a single MySqlTransaction. A FOR UPDATE re-read
    ' guards against a quantity going negative under concurrent edits.
    Private Shared Function CommitAdjustment(productId As Integer, qtyChange As Integer,
                                             movementType As String, refType As String,
                                             note As String) As Boolean
        If qtyChange = 0 Then Return False
        Try
            Using conn As MySqlConnection = Db.GetConnection()
                conn.Open()
                Using txn As MySqlTransaction = conn.BeginTransaction()
                    Try
                        Dim avail As Integer = 0
                        Using cmd As New MySqlCommand("SELECT stock_qty FROM products WHERE product_id = @pid FOR UPDATE", conn, txn)
                            cmd.Parameters.AddWithValue("@pid", productId)
                            avail = Convert.ToInt32(cmd.ExecuteScalar())
                        End Using

                        If avail + qtyChange < 0 Then
                            Throw New Exception("This change would drop stock below zero." & Environment.NewLine &
                                                "Current: " & avail & ", Change: " & qtyChange & ".")
                        End If

                        Dim noteParam As Object = If(String.IsNullOrEmpty(note), CType(DBNull.Value, Object), CType(note, Object))
                        Using cmd As New MySqlCommand(
                            "INSERT INTO stock_movements (product_id, user_id, movement_type, qty_change, reference_type, note, created_at) " &
                            "VALUES (@pid, @uid, @mtype, @qc, @rtype, @note, NOW())", conn, txn)
                            cmd.Parameters.AddWithValue("@pid", productId)
                            cmd.Parameters.AddWithValue("@uid", Session.CurrentUserId)
                            cmd.Parameters.AddWithValue("@mtype", movementType)
                            cmd.Parameters.AddWithValue("@qc", qtyChange)
                            cmd.Parameters.AddWithValue("@rtype", refType)
                            cmd.Parameters.AddWithValue("@note", noteParam)
                            cmd.ExecuteNonQuery()
                        End Using

                        Using cmd As New MySqlCommand("UPDATE products SET stock_qty = stock_qty + @qc WHERE product_id = @pid", conn, txn)
                            cmd.Parameters.AddWithValue("@qc", qtyChange)
                            cmd.Parameters.AddWithValue("@pid", productId)
                            cmd.ExecuteNonQuery()
                        End Using

                        txn.Commit()

                        ' Manual stock changes are the highest-risk inventory
                        ' action there is -- they move quantity with no sale or
                        ' delivery behind them -- so they belong in the audit log.
                        ActivityLog.Record("STOCK_ADJUST", "product", productId,
                            movementType & " " & If(qtyChange > 0, "+", "") & qtyChange &
                            If(String.IsNullOrEmpty(note), "", " — " & note))

                        Return True
                    Catch exInner As Exception
                        txn.Rollback()
                        AppModal.Show("Adjustment was not saved:" & Environment.NewLine & exInner.Message,
                                      "Could Not Save", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                        Return False
                    End Try
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Adjustment failed:" & Environment.NewLine & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return False
        End Try
    End Function

    Private Shared Sub OnSaveClicked()
        If selected Is Nothing Then Return

        Dim delta As Integer = ResolveDelta()
        If delta = 0 Then
            AppModal.Show("Enter a quantity that actually changes the stock.",
                          "Nothing To Save", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If

        Dim movementType As String = MovementTypeForReason(activeReason)
        Dim refType As String = activeReason
        Dim note As String = NoteText()

        ' Notes are required on every adjustment — no silent unexplained stock changes.
        If String.IsNullOrEmpty(note) Then
            AppModal.Show("A note is required for every adjustment." & Environment.NewLine &
                          "Please describe the reason (e.g. recount correction, broken in transit, expired stock…).",
                          "Note Required", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        Dim newQty As Integer = selected.StockQty + delta
        Dim confirmText As String =
            "Apply this adjustment to """ & selected.Name & """?" & Environment.NewLine & Environment.NewLine &
            ReasonLabel(activeReason) & "   " & SignedText(delta) & Environment.NewLine &
            "Stock: " & selected.StockQty & "  " & ChrW(8594) & "  " & newQty
        If AppModal.Show(confirmText, "Confirm Adjustment",
                         MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then
            Return
        End If

        If CommitAdjustment(selected.Id, delta, movementType, refType, note) Then
            ' Re-pull the product so the UI shows the committed number, then reset.
            selected = LoadProduct(selected.Id)
            If qtyBox IsNot Nothing Then qtyBox.Text = ""
            If noteBox IsNot Nothing Then ClearNote()
            BuildTargetCard()
            BuildHistory()
            UpdatePreview()
            AppModal.Show("Stock updated. New quantity: " & newQty & ".",
                          "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information)
        End If
    End Sub

    ' Resolve the signed qty_change from the current reason + input.
    '   Correction: raw value is signed — "+3" adds stock, "-2" removes it.
    '   Damage/Loss: always negative — removes stock regardless of sign entered.
    Private Shared Function ResolveDelta() As Integer
        If selected Is Nothing Then Return 0
        Dim raw As Integer = ParsedQtyValue()
        If raw = 0 Then Return 0

        ' Damage / Loss: always removes stock (force negative magnitude).
        If activeReason = ReasonDamage Then Return -Math.Abs(raw)
        ' Correction: accept the signed value as-is so a recount can go up or down.
        Return raw
    End Function

    Private Shared Function ParsedQtyValue() As Integer
        If qtyBox Is Nothing Then Return 0
        Dim cue As String = CStr(If(qtyBox.Tag, ""))
        Dim txt As String = qtyBox.Text.Trim()
        If txt = "" OrElse txt = cue Then Return 0
        Dim n As Integer
        If Integer.TryParse(txt, n) Then Return n
        Return 0
    End Function

    Private Shared Function MovementTypeForReason(reason As String) As String
        ' Inventory adjustments are always Adjustments — new purchased stock enters
        ' only through the Procurement → Receive PO flow, never from here.
        Return "Adjustment"
    End Function

    Private Shared Function ReasonLabel(reason As String) As String
        Select Case reason
            Case ReasonCorrection : Return "Correction"
            Case ReasonDamage : Return "Damage / Loss"
            Case Else : Return "Adjustment"
        End Select
    End Function

    Private Shared Function SignedText(delta As Integer) As String
        If delta > 0 Then Return "+" & delta
        Return delta.ToString()
    End Function

    ' =========================================================================
    '  TARGET PRODUCT CARD  (pre-filled, or a search box when none selected)
    ' =========================================================================
    Private Shared Sub BuildTargetCard()
        targetHost.Controls.Clear()

        Dim card As New BufferedPanel With {.Dock = DockStyle.Fill, .BackColor = CardBg}
        AddHandler card.Paint, Sub(s, e) PaintCard(CType(s, Panel), e)

        If selected Is Nothing Then
            ' ---- Search mode ----
            Dim lblPrompt As New Label With {
                .Text = "Find a product to adjust", .Font = New Font("Segoe UI", 12, FontStyle.Bold),
                .ForeColor = MintDark, .AutoSize = True, .Location = New Point(20, 16), .BackColor = Color.Transparent
            }
            card.Controls.Add(lblPrompt)

            Dim searchWrap As New BufferedPanel With {.Location = New Point(20, 48), .Size = New Size(380, 42), .BackColor = CardBg}
            AddHandler searchWrap.Paint, Sub(s, e) PaintPill(CType(s, Panel), e)
            Dim box As New TextBox With {
                .BorderStyle = BorderStyle.None, .Font = New Font("Segoe UI", 12),
                .ForeColor = MintDark, .BackColor = CardBg, .Location = New Point(16, 11), .Width = 348
            }
            SetCue(box, "Search name or SKU…")
            AddHandler box.TextChanged,
                Sub()
                    Dim cue As String = CStr(If(box.Tag, ""))
                    Dim term As String = If(box.Text = cue, "", box.Text.Trim())
                    RenderSearchResults(term)
                End Sub
            searchWrap.Controls.Add(box)
            card.Controls.Add(searchWrap)

            searchResultsHost = New Panel With {
                .Location = New Point(20, 98), .Size = New Size(380, 28), .BackColor = Color.Transparent, .AutoScroll = False
            }
            card.Controls.Add(searchResultsHost)
            AddHandler card.Resize,
                Sub()
                    searchWrap.Width = card.Width - 40
                    box.Width = searchWrap.Width - 32
                    searchResultsHost.Width = card.Width - 40
                End Sub
            targetHost.Height = 140
        Else
            ' ---- Selected mode ----
            Dim thumb As Panel = BuildThumb(selected, 60)
            thumb.Location = New Point(20, 22)
            card.Controls.Add(thumb)

            Dim lblName As New Label With {
                .Text = selected.Name, .Font = New Font("Segoe UI", 15, FontStyle.Bold),
                .ForeColor = MintDark, .AutoSize = True, .Location = New Point(94, 20), .BackColor = Color.Transparent
            }
            Dim lblMeta As New Label With {
                .Text = selected.Sku & "   " & ChrW(8226) & "   " & selected.CategoryName,
                .Font = New Font("Segoe UI", 10), .ForeColor = SubText, .AutoSize = True,
                .Location = New Point(96, 50), .BackColor = Color.Transparent
            }
            card.Controls.AddRange({lblName, lblMeta})

            ' Current-quantity block on the right.
            Dim lblQtyCap As New Label With {
                .Text = "CURRENT STOCK", .Font = New Font("Segoe UI", 8.5F, FontStyle.Bold),
                .ForeColor = FaintText, .AutoSize = True, .BackColor = Color.Transparent,
                .Anchor = AnchorStyles.Top Or AnchorStyles.Right
            }
            Dim lblQtyVal As New Label With {
                .Text = selected.StockQty.ToString(), .Font = New Font("Segoe UI", 28, FontStyle.Bold),
                .ForeColor = MintPrimary, .AutoSize = True, .BackColor = Color.Transparent,
                .Anchor = AnchorStyles.Top Or AnchorStyles.Right
            }
            Dim lblReorder As New Label With {
                .Text = "reorder at " & selected.ReorderLevel, .Font = New Font("Segoe UI", 9),
                .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent,
                .Anchor = AnchorStyles.Top Or AnchorStyles.Right
            }
            card.Controls.AddRange({lblQtyCap, lblQtyVal, lblReorder})

            Dim btnChange As Label = MakeLink("Change product", MintPrimary)
            btnChange.Anchor = AnchorStyles.Top Or AnchorStyles.Right
            AddHandler btnChange.Click,
                Sub()
                    selected = Nothing
                    BuildTargetCard()
                    BuildForm()
                    BuildHistory()
                    RefreshFormEnabled()
                    UpdatePreview()
                End Sub
            card.Controls.Add(btnChange)

            AddHandler card.Resize,
                Sub()
                    lblQtyVal.Left = card.Width - lblQtyVal.Width - 24
                    lblQtyVal.Top = 28
                    lblQtyCap.Left = card.Width - lblQtyCap.Width - 24
                    lblQtyCap.Top = 14
                    lblReorder.Left = card.Width - lblReorder.Width - 24
                    lblReorder.Top = 76
                    btnChange.Left = card.Width - btnChange.Width - 24
                    btnChange.Top = 100
                End Sub
            targetHost.Height = 140
        End If

        targetHost.Controls.Add(card)
        card.PerformLayout()
    End Sub

    Private Shared Sub RenderSearchResults(term As String)
        If searchResultsHost Is Nothing Then Return
        searchResultsHost.Controls.Clear()

        If term.Length < 1 Then
            searchResultsHost.Height = 1
            ResizeTargetForResults()
            Return
        End If

        Dim results = SearchProducts(term)
        If results.Count = 0 Then
            Dim none As New Label With {
                .Text = "No matching products.", .Font = New Font("Segoe UI", 10),
                .ForeColor = FaintText, .AutoSize = True, .Location = New Point(2, 4), .BackColor = Color.Transparent
            }
            searchResultsHost.Controls.Add(none)
            searchResultsHost.Height = 28
            ResizeTargetForResults()
            Return
        End If

        Dim top As Integer = 0
        For Each r In results
            Dim rowItem As AdjItem = r
            Dim row As New Panel With {
                .Location = New Point(0, top), .Height = 44, .Width = searchResultsHost.Width,
                .BackColor = Color.Transparent, .Cursor = Cursors.Hand,
                .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
            }
            AddHandler row.MouseEnter, Sub() row.BackColor = TileBg
            AddHandler row.MouseLeave, Sub() row.BackColor = Color.Transparent

            Dim chip As Panel = BuildThumb(rowItem, 30)
            chip.Location = New Point(4, 7)
            Dim lbl As New Label With {
                .Text = rowItem.Name, .Font = New Font("Segoe UI", 10.5F, FontStyle.Bold),
                .ForeColor = MintDark, .AutoSize = True, .Location = New Point(46, 4), .BackColor = Color.Transparent
            }
            Dim lblSub As New Label With {
                .Text = rowItem.Sku & "  " & ChrW(8226) & "  qty " & rowItem.StockQty,
                .Font = New Font("Segoe UI", 9), .ForeColor = SubText, .AutoSize = True,
                .Location = New Point(46, 24), .BackColor = Color.Transparent
            }
            row.Controls.Add(chip)
            row.Controls.Add(lbl)
            row.Controls.Add(lblSub)

            Dim pick As EventHandler =
                Sub(s, e)
                    selected = rowItem
                    BuildTargetCard()
                    BuildForm()
                    BuildHistory()
                    RefreshFormEnabled()
                    UpdatePreview()
                End Sub
            AddHandler row.Click, pick
            AddHandler chip.Click, pick
            AddHandler lbl.Click, pick
            AddHandler lblSub.Click, pick

            searchResultsHost.Controls.Add(row)
            top += 46
        Next
        searchResultsHost.Height = top
        ResizeTargetForResults()
    End Sub

    Private Shared Sub ResizeTargetForResults()
        Dim needed As Integer = 100 + If(searchResultsHost Is Nothing, 0, searchResultsHost.Height) + 4
        targetHost.Height = Math.Max(110, needed)
    End Sub

    ' =========================================================================
    '  ADJUSTMENT FORM
    ' =========================================================================
    Private Shared Sub BuildForm()
        formHost.Controls.Clear()

        Dim card As New BufferedPanel With {.Dock = DockStyle.Fill, .BackColor = CardBg}
        AddHandler card.Paint, Sub(s, e) PaintCard(CType(s, Panel), e)

        Dim lblHeading As New Label With {
            .Text = "New adjustment", .Font = New Font("Segoe UI", 13, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(20, 16), .BackColor = Color.Transparent
        }
        card.Controls.Add(lblHeading)

        ' ---- Reason picker ----
        Dim lblReasonCap As New Label With {
            .Text = "REASON", .Font = New Font("Segoe UI", 9, FontStyle.Bold),
            .ForeColor = FaintText, .AutoSize = True, .Location = New Point(20, 54), .BackColor = Color.Transparent
        }
        card.Controls.Add(lblReasonCap)

        Dim reasonRow As New FlowLayoutPanel With {
            .Location = New Point(18, 76), .Height = 50, .WrapContents = False, .AutoSize = False,
            .BackColor = Color.Transparent, .Padding = New Padding(0),
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        }
        AddHandler card.Resize, Sub() reasonRow.Width = card.Width - 36
        reasonCards.Clear()
        reasonRow.Controls.Add(MakeReasonCard("Correction", "Recount: + or −", ReasonCorrection, MintPrimary))
        reasonRow.Controls.Add(MakeReasonCard("Damage / Loss", "Stock written off", ReasonDamage, NegativeClr))
        card.Controls.Add(reasonRow)

        ' ---- Contextual reason explanation (updates when reason changes) ----
        lblReasonInfo = New Label With {
            .Font = New Font("Segoe UI", 9.5F), .ForeColor = SubText,
            .AutoSize = True, .Location = New Point(20, 132), .BackColor = Color.Transparent
        }
        card.Controls.Add(lblReasonInfo)

        ' ---- Quantity section ----
        Dim lblQtyCap As New Label With {
            .Text = "QUANTITY", .Font = New Font("Segoe UI", 9, FontStyle.Bold),
            .ForeColor = FaintText, .AutoSize = True, .Location = New Point(20, 162), .BackColor = Color.Transparent
        }
        card.Controls.Add(lblQtyCap)

        ' Mode toggle hidden — only "Change (+/-)" remains, so no toggle is needed.
        modeTabs.Clear()
        entryMode = ModeChange

        ' ---- Quantity input ----
        Dim qtyWrap As New BufferedPanel With {.Location = New Point(20, 184), .Size = New Size(196, 48), .BackColor = CardBg}
        AddHandler qtyWrap.Paint, Sub(s, e) PaintPill(CType(s, Panel), e)
        qtyBox = New TextBox With {
            .BorderStyle = BorderStyle.None, .Font = New Font("Segoe UI", 16, FontStyle.Bold),
            .ForeColor = MintDark, .BackColor = CardBg, .Location = New Point(16, 12), .Width = 164
        }
        SetCue(qtyBox, "0")
        AddHandler qtyBox.TextChanged, Sub() UpdatePreview()
        qtyWrap.Controls.Add(qtyBox)
        card.Controls.Add(qtyWrap)

        lblModeHint = New Label With {
            .Text = "", .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold), .ForeColor = SubText,
            .AutoSize = True, .Location = New Point(228, 200), .BackColor = Color.Transparent
        }
        card.Controls.Add(lblModeHint)

        ' ---- Note ----
        Dim lblNoteCap As New Label With {
            .Text = "NOTE (REQUIRED)", .Font = New Font("Segoe UI", 9, FontStyle.Bold),
            .ForeColor = FaintText, .AutoSize = True, .Location = New Point(20, 244), .BackColor = Color.Transparent
        }
        card.Controls.Add(lblNoteCap)

        Dim noteWrap As New BufferedPanel With {
            .Location = New Point(20, 266), .Size = New Size(400, 44), .BackColor = CardBg,
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        }
        AddHandler noteWrap.Paint, Sub(s, e) PaintPill(CType(s, Panel), e)
        noteBox = New TextBox With {
            .BorderStyle = BorderStyle.None, .Font = New Font("Segoe UI", 11),
            .ForeColor = MintDark, .BackColor = CardBg, .Location = New Point(16, 13), .Width = 368,
            .MaxLength = 150, .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        }
        SetCue(noteBox, "e.g. recount correction, broken in transit, expired stock…")
        noteWrap.Controls.Add(noteBox)
        AddHandler card.Resize,
            Sub()
                noteWrap.Width = card.Width - 40
                noteBox.Width = noteWrap.Width - 32
            End Sub
        card.Controls.Add(noteWrap)

        ' ---- Live preview ----
        Dim previewWrap As New BufferedPanel With {
            .Location = New Point(20, 326), .Size = New Size(400, 62), .BackColor = TileBg,
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        }
        AddHandler previewWrap.Paint,
            Sub(s, e)
                Dim p = CType(s, Panel)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Using path = RoundRect(New Rectangle(0, 0, p.Width - 1, p.Height - 1), 12)
                    Using br As New SolidBrush(TileBg)
                        e.Graphics.FillPath(br, path)
                    End Using
                End Using
            End Sub
        lblPreview = New Label With {
            .Text = "—", .Font = New Font("Segoe UI", 16, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(18, 10), .BackColor = Color.Transparent
        }
        lblPreviewSub = New Label With {
            .Text = "Select a product to begin", .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = SubText, .AutoSize = True, .Location = New Point(20, 38), .BackColor = Color.Transparent
        }
        previewWrap.Controls.AddRange({lblPreview, lblPreviewSub})
        AddHandler card.Resize, Sub() previewWrap.Width = card.Width - 40
        card.Controls.Add(previewWrap)

        ' ---- Actions ----
        btnSave = MakePrimaryButton("Save Adjustment")
        btnSave.Location = New Point(20, 406)
        btnSave.Anchor = AnchorStyles.Left Or AnchorStyles.Top
        AddHandler btnSave.Click, Sub() OnSaveClicked()
        card.Controls.Add(btnSave)

        Dim btnCancel As Panel = MakeSecondaryButton("Clear")
        btnCancel.Location = New Point(196, 406)
        AddHandler btnCancel.Click,
            Sub()
                If qtyBox IsNot Nothing Then qtyBox.Text = ""
                ClearNote()
                UpdatePreview()
            End Sub
        card.Controls.Add(btnCancel)

        formHost.Controls.Add(card)
        card.PerformLayout()

        RefreshReasonVisuals()
        RefreshModeVisuals()
    End Sub

    Private Shared Function MakeReasonCard(title As String, subtitle As String, reason As String, accent As Color) As Panel
        Dim capReason As String = reason
        Dim capAccent As Color = accent
        Dim card As New BufferedPanel With {
            .Size = New Size(148, 48), .Margin = New Padding(0, 0, 8, 0), .Cursor = Cursors.Hand, .BackColor = CardBg
        }
        AddHandler card.Paint,
            Sub(s, e)
                Dim p = CType(s, Panel)
                Dim isOn As Boolean = (capReason = activeReason)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Using path = RoundRect(New Rectangle(1, 1, p.Width - 3, p.Height - 3), 10)
                    Using br As New SolidBrush(If(isOn, TileBg, CardBg))
                        e.Graphics.FillPath(br, path)
                    End Using
                    Using pen As New Pen(If(isOn, capAccent, CardBorder), If(isOn, 2.0F, 1.0F))
                        e.Graphics.DrawPath(pen, path)
                    End Using
                End Using
                Using dotBr As New SolidBrush(capAccent)
                    e.Graphics.FillEllipse(dotBr, 13, 18, 10, 10)
                End Using
            End Sub
        Dim lblT As New Label With {
            .Text = title, .Font = New Font("Segoe UI", 10, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(30, 7), .BackColor = Color.Transparent
        }
        Dim lblS As New Label With {
            .Text = subtitle, .Font = New Font("Segoe UI", 8),
            .ForeColor = SubText, .AutoSize = True, .Location = New Point(30, 27), .BackColor = Color.Transparent
        }
        card.Controls.Add(lblT)
        card.Controls.Add(lblS)

        Dim onClick As EventHandler =
            Sub(s, e)
                activeReason = capReason
                RefreshReasonVisuals()
                UpdatePreview()
            End Sub
        AddHandler card.Click, onClick
        AddHandler lblT.Click, onClick
        AddHandler lblS.Click, onClick
        lblT.Cursor = Cursors.Hand
        lblS.Cursor = Cursors.Hand

        reasonCards.Add((card, reason))
        Return card
    End Function

    Private Shared Sub RefreshReasonVisuals()
        For Each rc In reasonCards
            rc.panel.Invalidate()
        Next
        UpdateModeHint()
    End Sub

    Private Shared Function MakeModeTab(text As String, mode As String) As Panel
        Dim capMode As String = mode
        Dim tab As New BufferedPanel With {
            .Size = New Size(130, 36), .Cursor = Cursors.Hand, .BackColor = CardBg
        }
        AddHandler tab.Paint,
            Sub(s, e)
                Dim p = CType(s, Panel)
                Dim isOn As Boolean = (capMode = entryMode)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Using path = RoundRect(New Rectangle(0, 0, p.Width - 1, p.Height - 1), 8)
                    Using br As New SolidBrush(If(isOn, MintPrimary, TileBg))
                        e.Graphics.FillPath(br, path)
                    End Using
                End Using
                Dim fg As Color = If(isOn, Color.White, SubText)
                TextRenderer.DrawText(e.Graphics, text, New Font("Segoe UI", 9.5F, FontStyle.Bold),
                    p.ClientRectangle, fg, TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub
        Dim onClick As EventHandler =
            Sub(s, e)
                entryMode = capMode
                RefreshModeVisuals()
                UpdatePreview()
            End Sub
        AddHandler tab.Click, onClick
        modeTabs.Add((tab, mode))
        Return tab
    End Function

    Private Shared Sub RefreshModeVisuals()
        For Each mt In modeTabs
            mt.panel.Invalidate()
        Next
        UpdateModeHint()
    End Sub

    Private Shared Sub UpdateModeHint()
        If lblModeHint Is Nothing Then Return
        If activeReason = ReasonDamage Then
            lblModeHint.Text = "Enter how many to remove"
        Else
            lblModeHint.Text = "Signed: +3 adds, -2 removes"
        End If

        If lblReasonInfo IsNot Nothing Then
            If activeReason = ReasonDamage Then
                lblReasonInfo.Text = "Always removes stock — enter the quantity to write off."
            Else
                lblReasonInfo.Text = "Reconcile to your physical count — positive adds, negative removes."
            End If
        End If
    End Sub

    Private Shared Sub RefreshFormEnabled()
        Dim hasProduct As Boolean = (selected IsNot Nothing)
        If qtyBox IsNot Nothing Then qtyBox.Enabled = hasProduct
        If noteBox IsNot Nothing Then noteBox.Enabled = hasProduct
        If btnSave IsNot Nothing Then
            btnSave.Enabled = hasProduct
            btnSave.Tag = If(hasProduct, "on", "off")
            btnSave.Invalidate()
        End If
    End Sub

    Private Shared Sub UpdatePreview()
        UpdateModeHint()
        If lblPreview Is Nothing OrElse lblPreviewSub Is Nothing Then Return

        If selected Is Nothing Then
            lblPreview.Text = "—"
            lblPreview.ForeColor = MintDark
            lblPreviewSub.Text = "Select a product to begin"
            RefreshFormEnabled()
            Return
        End If

        RefreshFormEnabled()
        Dim delta As Integer = ResolveDelta()
        Dim newQty As Integer = selected.StockQty + delta

        If delta = 0 Then
            lblPreview.Text = "Current: " & selected.StockQty
            lblPreview.ForeColor = MintDark
            lblPreviewSub.Text = "Enter a quantity to see the result"
            Return
        End If

        lblPreview.Text = selected.StockQty & "   " & ChrW(8594) & "   " & newQty
        lblPreview.ForeColor = If(newQty < 0, NegativeClr, MintDark)
        Dim arrowWord As String = If(delta > 0, "Adding ", "Removing ")
        lblPreviewSub.Text = ReasonLabel(activeReason) & "   " & ChrW(8226) & "   " & arrowWord & Math.Abs(delta) &
                             If(newQty < 0, "  (cannot go below zero)", "")
    End Sub

    ' =========================================================================
    '  RECENT HISTORY
    ' =========================================================================
    Private Shared Sub BuildHistory()
        historyHost.Controls.Clear()

        Dim card As New BufferedPanel With {.Dock = DockStyle.Fill, .BackColor = CardBg}
        AddHandler card.Paint, Sub(s, e) PaintCard(CType(s, Panel), e)

        Dim lblH As New Label With {
            .Text = "Recent movements", .Font = New Font("Segoe UI", 13, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(20, 16), .BackColor = Color.Transparent
        }
        card.Controls.Add(lblH)

        Dim listWrap As New Panel With {
            .Location = New Point(12, 54), .BackColor = Color.Transparent, .AutoScroll = True,
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top Or AnchorStyles.Bottom
        }
        AddHandler card.Resize,
            Sub()
                listWrap.Width = card.Width - 24
                listWrap.Height = card.Height - 66
            End Sub
        card.Controls.Add(listWrap)

        If selected Is Nothing Then
            Dim none As New Label With {
                .Text = "Select a product to see its history.", .Font = New Font("Segoe UI", 10),
                .ForeColor = FaintText, .AutoSize = True, .Location = New Point(8, 8), .BackColor = Color.Transparent
            }
            listWrap.Controls.Add(none)
        Else
            Dim rows = LoadHistory(selected.Id)
            If rows.Count = 0 Then
                Dim none As New Label With {
                    .Text = "No movements recorded yet.", .Font = New Font("Segoe UI", 10),
                    .ForeColor = FaintText, .AutoSize = True, .Location = New Point(8, 8), .BackColor = Color.Transparent
                }
                listWrap.Controls.Add(none)
            Else
                Dim top As Integer = 0
                For Each m In rows
                    Dim rowPanel As Panel = BuildHistoryRow(m)
                    rowPanel.Location = New Point(0, top)
                    rowPanel.Width = listWrap.ClientSize.Width - 4
                    rowPanel.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
                    listWrap.Controls.Add(rowPanel)
                    top += rowPanel.Height
                Next
            End If
        End If

        historyHost.Controls.Add(card)
        card.PerformLayout()
    End Sub

    Private Shared Function BuildHistoryRow(m As MovementRow) As Panel
        Dim row As New Panel With {.Height = 56, .BackColor = Color.Transparent}
        AddHandler row.Paint,
            Sub(s, e)
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 8, row.Height - 1, row.Width - 8, row.Height - 1)
                End Using
            End Sub

        Dim signed As String = SignedText(m.QtyChange)
        Dim clr As Color = If(m.QtyChange >= 0, PositiveClr, NegativeClr)

        Dim lblQty As New Label With {
            .Text = signed, .Font = New Font("Segoe UI", 14, FontStyle.Bold),
            .ForeColor = clr, .AutoSize = True, .Location = New Point(8, 9), .BackColor = Color.Transparent
        }
        Dim lblWhat As New Label With {
            .Text = HistoryCaption(m), .Font = New Font("Segoe UI", 10, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(68, 8), .BackColor = Color.Transparent
        }
        Dim metaText As String = m.CreatedAt.ToString("MMM d, yyyy h:mm tt")
        If m.UserName <> "" Then metaText &= "  " & ChrW(8226) & "  " & m.UserName
        Dim lblMeta As New Label With {
            .Text = metaText, .Font = New Font("Segoe UI", 8.5F), .ForeColor = FaintText,
            .AutoSize = True, .Location = New Point(68, 29), .BackColor = Color.Transparent
        }
        row.Controls.AddRange({lblQty, lblWhat, lblMeta})

        If m.Note <> "" Then
            Dim lblNote As New Label With {
                .Text = ChrW(8220) & m.Note & ChrW(8221), .Font = New Font("Segoe UI", 8.5F, FontStyle.Italic),
                .ForeColor = SubText, .AutoSize = True, .Location = New Point(224, 10), .BackColor = Color.Transparent,
                .Anchor = AnchorStyles.Top Or AnchorStyles.Right
            }
            row.Controls.Add(lblNote)
            AddHandler row.Resize, Sub() lblNote.Left = Math.Max(224, row.Width - lblNote.Width - 8)
        End If

        Return row
    End Function

    ' Human label for a movement row, preferring the reference_type reason.
    Private Shared Function HistoryCaption(m As MovementRow) As String
        Select Case m.RefType.ToLowerInvariant()
            Case ReasonReceive : Return "Received stock"
            Case ReasonCorrection : Return "Correction"
            Case ReasonDamage : Return "Damage / loss"
            Case "void" : Return "Void of sale"
            Case "return" : Return "Return"
        End Select
        Select Case m.MovementType
            Case "Sale" : Return "Sale"
            Case "Purchase" : Return "Received stock"
            Case "Return" : Return "Return"
            Case "Initial" : Return "Opening stock"
            Case Else : Return "Adjustment"
        End Select
    End Function

    ' =========================================================================
    '  SHARED PAINT HELPERS
    ' =========================================================================
    Private Shared Sub PaintCard(panel As Panel, e As PaintEventArgs)
        PanelRenderer.Draw(panel, e, True)
    End Sub

    Private Shared Sub PaintPill(panel As Panel, e As PaintEventArgs)
        Dim g = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias
        Using path = RoundRect(New Rectangle(0, 0, panel.Width - 1, panel.Height - 1), 10)
            Using br As New SolidBrush(CardBg)
                g.FillPath(br, path)
            End Using
            Using pen As New Pen(CardBorder, 1)
                g.DrawPath(pen, path)
            End Using
        End Using
    End Sub

    Private Shared Function MakePrimaryButton(text As String) As Panel
        Dim btn As New BufferedPanel With {
            .Size = New Size(164, 48), .Cursor = Cursors.Hand, .BackColor = CardBg, .Tag = "on"
        }
        AddHandler btn.Paint,
            Sub(s, e)
                Dim p = CType(s, Panel)
                Dim isOn As Boolean = (CStr(If(p.Tag, "on")) <> "off")
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Using path = RoundRect(New Rectangle(0, 0, p.Width - 1, p.Height - 1), 12)
                    Using br As New SolidBrush(If(isOn, MintPrimary, Color.FromArgb(200, 214, 209)))
                        e.Graphics.FillPath(br, path)
                    End Using
                End Using
                TextRenderer.DrawText(e.Graphics, text, New Font("Segoe UI", 12, FontStyle.Bold),
                    p.ClientRectangle, Color.White, TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub
        AddHandler btn.MouseEnter, Sub() btn.Invalidate()
        Return btn
    End Function

    Private Shared Function MakeSecondaryButton(text As String) As Panel
        Dim btn As New BufferedPanel With {
            .Size = New Size(110, 48), .Cursor = Cursors.Hand, .BackColor = CardBg
        }
        AddHandler btn.Paint,
            Sub(s, e)
                Dim p = CType(s, Panel)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Using path = RoundRect(New Rectangle(0, 0, p.Width - 1, p.Height - 1), 12)
                    Using br As New SolidBrush(CardBg)
                        e.Graphics.FillPath(br, path)
                    End Using
                    Using pen As New Pen(CardBorder, 1.4F)
                        e.Graphics.DrawPath(pen, path)
                    End Using
                End Using
                TextRenderer.DrawText(e.Graphics, text, New Font("Segoe UI", 11, FontStyle.Bold),
                    p.ClientRectangle, MintDark, TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub
        Return btn
    End Function

    Private Shared Function MakeLink(text As String, clr As Color) As Label
        Dim lbl As New Label With {
            .Text = text, .Font = New Font("Segoe UI", 10, FontStyle.Bold),
            .ForeColor = clr, .AutoSize = True, .Cursor = Cursors.Hand, .BackColor = Color.Transparent
        }
        AddHandler lbl.MouseEnter, Sub() lbl.Font = New Font("Segoe UI", 10, FontStyle.Bold Or FontStyle.Underline)
        AddHandler lbl.MouseLeave, Sub() lbl.Font = New Font("Segoe UI", 10, FontStyle.Bold)
        Return lbl
    End Function

    ' Rounded thumbnail: paint = colour chip, other = cached photo, fallback = tile.
    Private Shared Function BuildThumb(it As AdjItem, size As Integer) As Panel
        Dim box As New Panel With {.Size = New Size(size, size), .BackColor = Color.Transparent}
        Dim img As Image = If(it.IsPaint, Nothing, GetThumb(it.ImagePath, size - 2))
        Dim chip As Color = HexToColor(it.ColorHex, MintPrimary)
        Dim initial As String = If(String.IsNullOrEmpty(it.Name), "?", it.Name.Substring(0, 1).ToUpperInvariant())
        Dim capSize As Integer = size

        AddHandler box.Paint,
            Sub(s, e)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim r As New Rectangle(0, 0, capSize - 1, capSize - 1)
                Using path = RoundRect(r, 10)
                    If it.IsPaint Then
                        Using br As New SolidBrush(chip)
                            g.FillPath(br, path)
                        End Using
                    ElseIf img IsNot Nothing Then
                        Dim oldClip = g.Clip
                        g.SetClip(path, CombineMode.Replace)
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic
                        g.DrawImage(img, r)
                        g.Clip = oldClip
                    Else
                        Using br As New SolidBrush(TileBg)
                            g.FillPath(br, path)
                        End Using
                        Using f As New Font("Segoe UI", capSize \ 3, FontStyle.Bold)
                            Using br As New SolidBrush(MintPrimary)
                                Dim sz = g.MeasureString(initial, f)
                                g.DrawString(initial, f, br, (capSize - sz.Width) / 2, (capSize - sz.Height) / 2)
                            End Using
                        End Using
                    End If
                    Using pen As New Pen(Color.FromArgb(213, 221, 217), 1)
                        g.DrawPath(pen, path)
                    End Using
                End Using
            End Sub
        Return box
    End Function

    ' =========================================================================
    '  NOTE-BOX CUE HELPERS  (treat the cue text as empty)
    ' =========================================================================
    Private Shared Function NoteText() As String
        If noteBox Is Nothing Then Return ""
        Dim cue As String = CStr(If(noteBox.Tag, ""))
        Dim txt As String = noteBox.Text.Trim()
        If txt = cue Then Return ""
        Return txt
    End Function

    Private Shared Sub ClearNote()
        If noteBox Is Nothing Then Return
        Dim cue As String = CStr(If(noteBox.Tag, ""))
        noteBox.Text = cue
        noteBox.ForeColor = FaintText
    End Sub

    ' =========================================================================
    '  MEDIA / THUMBNAIL  (identical to the other inventory panels)
    ' =========================================================================
    Private Shared Function GetThumb(path As String, maxSize As Integer) As Image
        If String.IsNullOrEmpty(path) Then Return Nothing
        ' image_path may be relative (current) or an absolute path baked to
        ' another machine (older rows); resolve both to something openable.
        path = ImageCache.ToFullPath(path)
        Dim key = path & "|" & maxSize
        If thumbCache.ContainsKey(key) Then Return thumbCache(key)
        Try
            If Not File.Exists(path) Then Return Nothing
            Dim bytes = File.ReadAllBytes(path)
            Using ms As New MemoryStream(bytes)
                Using src As Image = Image.FromStream(ms)
                    Dim scale = Math.Min(maxSize / CSng(src.Width), maxSize / CSng(src.Height))
                    Dim w = Math.Max(1, CInt(src.Width * scale))
                    Dim h = Math.Max(1, CInt(src.Height * scale))
                    Dim bmp As New Bitmap(w, h)
                    Using g = Graphics.FromImage(bmp)
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic
                        g.DrawImage(src, 0, 0, w, h)
                    End Using
                    thumbCache(key) = bmp
                    Return bmp
                End Using
            End Using
        Catch
            Return Nothing
        End Try
    End Function

    ' =========================================================================
    '  SMALL HELPERS  (mirror the other inventory panels)
    ' =========================================================================
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

    Private Shared Sub SetCue(tb As TextBox, cue As String)
        tb.Tag = cue
        tb.ForeColor = FaintText
        tb.Text = cue
        AddHandler tb.GotFocus,
            Sub()
                If tb.Text = cue Then
                    tb.Text = ""
                    tb.ForeColor = MintDark
                End If
            End Sub
        AddHandler tb.LostFocus,
            Sub()
                If tb.Text.Trim() = "" Then
                    tb.Text = cue
                    tb.ForeColor = FaintText
                End If
            End Sub
    End Sub

End Class