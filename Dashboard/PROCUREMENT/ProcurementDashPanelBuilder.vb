Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports MySql.Data.MySqlClient

' =============================================================================
'  ProcurementDashPanelBuilder
'  Screen 1 — Procurement Dashboard.
'
'  LAYOUT
'  ------
'    Top region (Dock = Top)  : title + subtitle + 4 KPI cards
'    Body  (Dock = Fill)      : two side-by-side list cards
'                                 left  = Pending Requests (newest first, max 8)
'                                 right = POs Due Soon (Ordered, expected within 7d)
'
'  Each pending-request row shows: product name, requested qty, who requested it,
'  and how long ago. Its "Review" link jumps to the Purchase Orders screen with
'  that request pre-loaded via PROCUREMENT_STAFF.OpenPOWithRequest(requestId).
'
'  Same Shared-only, no-instance-state pattern as the inventory builders.
'  Helpers (RoundRect / RoundControl / RoundLabel / MakeLink / SafeInt /
'  HexToColor / PaintCard) are copied verbatim from InventoryLowStockPanelBuilder.
' =============================================================================
Public Class ProcurementDashPanelBuilder

    ' ── PALETTE (exact values from the project spec) ─────────────────────────────
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
    Private Class PendingReq
        Public RequestId As Integer
        Public ProductName As String
        Public QtyRequested As Integer
        Public RequestedBy As String
        Public CreatedAt As DateTime
    End Class

    Private Class DuePO
        Public PoId As Integer
        Public PoNo As String
        Public SupplierName As String
        Public ExpectedDate As DateTime
        Public HasExpected As Boolean
        Public ItemCount As Integer
        Public TotalCost As Decimal
        Public Status As String
    End Class

    ' ── STATE ────────────────────────────────────────────────────────────────────
    Private Shared containerRef As Panel
    Private Shared pending As New List(Of PendingReq)
    Private Shared dueSoon As New List(Of DuePO)

    Private Shared kpiPending As Integer
    Private Shared kpiActivePOs As Integer
    Private Shared kpiExpectedWeek As Integer
    Private Shared kpiTotalValue As Decimal

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Shared Sub LoadDash(container As Panel)
        containerRef = container
        LoadData()

        container.SuspendLayout()
        container.Controls.Clear()
        container.BackColor = MintBg

        ' ---- Body: two side-by-side list cards ----
        Dim body As New Panel With {
            .Dock = DockStyle.Fill, .BackColor = MintBg, .Padding = New Padding(24, 4, 24, 20)
        }

        Dim grid As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 1,
            .BackColor = Color.Transparent, .CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        }
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50.0F))
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50.0F))
        grid.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))

        Dim leftCard As Panel = BuildPendingCard()
        leftCard.Margin = New Padding(0, 0, 8, 0)
        Dim rightCard As Panel = BuildDueSoonCard()
        rightCard.Margin = New Padding(8, 0, 0, 0)

        grid.Controls.Add(leftCard, 0, 0)
        grid.Controls.Add(rightCard, 1, 0)
        body.Controls.Add(grid)

        ' ---- Top region: title + KPIs ----
        Dim topRegion As Panel = BuildTopRegion()
        topRegion.Dock = DockStyle.Top

        container.Controls.Add(body)
        container.Controls.Add(topRegion)
        container.ResumeLayout()
    End Sub

    ' =========================================================================
    '  DATA
    ' =========================================================================
    Private Shared Sub LoadData()
        pending.Clear()
        dueSoon.Clear()
        kpiPending = 0
        kpiActivePOs = 0
        kpiExpectedWeek = 0
        kpiTotalValue = 0D

        Try
            Using conn = Db.GetConnection()
                conn.Open()

                ' ---- KPI 1: Approved requests ----
                ' Requests reach Procurement only once the admin approves them;
                ' 'Pending' ones are still waiting for that decision.
                Using cmd As New MySqlCommand(
                    "SELECT COUNT(*) FROM purchase_requests WHERE status = 'Approved'", conn)
                    kpiPending = Convert.ToInt32(cmd.ExecuteScalar())
                End Using

                ' ---- KPI 2: Active POs (not Received / Cancelled) ----
                Using cmd As New MySqlCommand(
                    "SELECT COUNT(*) FROM purchase_orders " &
                    "WHERE status NOT IN ('Received','Cancelled')", conn)
                    kpiActivePOs = Convert.ToInt32(cmd.ExecuteScalar())
                End Using

                ' ---- KPI 3: Expected deliveries this week ----
                Using cmd As New MySqlCommand(
                    "SELECT COUNT(*) FROM purchase_orders " &
                    "WHERE status NOT IN ('Received','Cancelled') " &
                    "AND expected_date IS NOT NULL " &
                    "AND expected_date BETWEEN CURDATE() AND CURDATE() + INTERVAL 7 DAY", conn)
                    kpiExpectedWeek = Convert.ToInt32(cmd.ExecuteScalar())
                End Using

                ' ---- KPI 4: Total value of active POs ----
                Using cmd As New MySqlCommand(
                    "SELECT COALESCE(SUM(total_cost), 0) FROM purchase_orders " &
                    "WHERE status NOT IN ('Received','Cancelled')", conn)
                    kpiTotalValue = Convert.ToDecimal(cmd.ExecuteScalar())
                End Using

                ' ---- Pending requests list (newest first, max 8) ----
                ' One row per REQUEST, which is what a dashboard summary wants.
                ' A request is a basket of products though (migrations.sql
                ' section 14), so naming only its first product would understate
                ' it -- the line counts come along and a multi-product request
                ' is labelled by its size instead.
                Using cmd As New MySqlCommand(
                    "SELECT pr.request_id, p.name AS pname, pr.qty_requested, " &
                    "u.full_name AS reqby, pr.created_at, " &
                    "(SELECT COUNT(*) FROM purchase_request_items pri " &
                    " WHERE pri.request_id = pr.request_id) AS n_items, " &
                    "(SELECT COALESCE(SUM(pri.qty_requested),0) FROM purchase_request_items pri " &
                    " WHERE pri.request_id = pr.request_id) AS n_units " &
                    "FROM purchase_requests pr " &
                    "JOIN products p ON pr.product_id = p.product_id " &
                    "LEFT JOIN users u ON pr.requested_by = u.user_id " &
                    "WHERE pr.status = 'Approved' " &
                    "ORDER BY pr.created_at DESC " &
                    "LIMIT 8", conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            Dim nItems As Integer = SafeInt(rdr("n_items"))
                            Dim nUnits As Integer = SafeInt(rdr("n_units"))
                            Dim legacyQty As Integer = SafeInt(rdr("qty_requested"))
                            pending.Add(New PendingReq With {
                                .RequestId = SafeInt(rdr("request_id")),
                                .ProductName = If(nItems > 1,
                                                  nItems & " products",
                                                  rdr("pname").ToString()),
                                .QtyRequested = If(nUnits > 0, nUnits, legacyQty),
                                .RequestedBy = If(IsDBNull(rdr("reqby")), "Unknown", rdr("reqby").ToString()),
                                .CreatedAt = If(IsDBNull(rdr("created_at")), DateTime.Now, Convert.ToDateTime(rdr("created_at")))
                            })
                        End While
                    End Using
                End Using

                ' ---- POs due soon (Ordered, expected within 7 days) ----
                Using cmd As New MySqlCommand(
                    "SELECT po.po_id, po.po_no, s.name AS sname, po.expected_date, " &
                    "po.total_cost, po.status, " &
                    "(SELECT COUNT(*) FROM po_items pi WHERE pi.po_id = po.po_id) AS item_count " &
                    "FROM purchase_orders po " &
                    "JOIN suppliers s ON po.supplier_id = s.supplier_id " &
                    "WHERE po.status = 'Ordered' AND po.expected_date IS NOT NULL " &
                    "AND po.expected_date <= CURDATE() + INTERVAL 7 DAY " &
                    "ORDER BY po.expected_date ASC " &
                    "LIMIT 8", conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            Dim hasExp As Boolean = Not IsDBNull(rdr("expected_date"))
                            dueSoon.Add(New DuePO With {
                                .PoId = SafeInt(rdr("po_id")),
                                .PoNo = rdr("po_no").ToString(),
                                .SupplierName = rdr("sname").ToString(),
                                .HasExpected = hasExp,
                                .ExpectedDate = If(hasExp, Convert.ToDateTime(rdr("expected_date")), DateTime.MinValue),
                                .TotalCost = If(IsDBNull(rdr("total_cost")), 0D, Convert.ToDecimal(rdr("total_cost"))),
                                .Status = rdr("status").ToString(),
                                .ItemCount = SafeInt(rdr("item_count"))
                            })
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load the procurement dashboard:" & Environment.NewLine & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    ' =========================================================================
    '  TOP REGION  (title + 4 KPI cards)
    ' =========================================================================
    Private Shared Function BuildTopRegion() As Panel
        Dim region As New Panel With {.Height = 256, .BackColor = MintBg, .Padding = New Padding(24, 22, 24, 0)}

        Dim lblTitle As New Label With {
            .Text = "Dashboard", .Font = New Font("Segoe UI", 24, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(24, 18)
        }
        Dim lblSubtitle As New Label With {
            .Text = SubtitleText(), .Font = New Font("Segoe UI", 11),
            .ForeColor = SubText, .AutoSize = True, .Location = New Point(30, 58)
        }
        AddHandler region.Resize,
            Sub()
            End Sub

        region.Controls.AddRange({lblTitle, lblSubtitle})

        ' ---- KPI row: 4 cards ----
        Dim kpiRow As New TableLayoutPanel With {
            .Left = 24, .Top = 96, .Height = 140, .ColumnCount = 4, .RowCount = 1,
            .BackColor = Color.Transparent, .CellBorderStyle = TableLayoutPanelCellBorderStyle.None,
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        }
        For i = 0 To 3
            kpiRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 25.0F))
        Next
        kpiRow.RowStyles.Add(New RowStyle(SizeType.Percent, 100))

        Dim card1 As Panel = MakeKpiCard("Approved Requests", kpiPending.ToString("N0"), WarningClr, "Approved by admin, ready to order")
        Dim card2 As Panel = MakeKpiCard("Active POs", kpiActivePOs.ToString("N0"), MintDark, "Not received or cancelled")
        Dim card3 As Panel = MakeKpiCard("Expected This Week", kpiExpectedWeek.ToString("N0"), MintPrimary, "Due within 7 days")
        Dim card4 As Panel = MakeKpiCard("Total PO Value", Peso & kpiTotalValue.ToString("N2"), MintDeep, "Active purchase orders")

        card1.Margin = New Padding(0, 0, 6, 0)
        card2.Margin = New Padding(6, 0, 6, 0)
        card3.Margin = New Padding(6, 0, 6, 0)
        card4.Margin = New Padding(6, 0, 0, 0)
        kpiRow.Controls.Add(card1, 0, 0)
        kpiRow.Controls.Add(card2, 1, 0)
        kpiRow.Controls.Add(card3, 2, 0)
        kpiRow.Controls.Add(card4, 3, 0)

        AddHandler region.Resize, Sub() kpiRow.Width = region.Width - 48
        kpiRow.Width = region.Width - 48
        region.Controls.Add(kpiRow)

        Return region
    End Function

    Private Shared Function SubtitleText() As String
        Dim parts As String = kpiPending.ToString() & If(kpiPending = 1, " approved request", " approved requests") & " to order"
        Return parts & "   " & ChrW(8226) & "   " & kpiActivePOs & " active " & If(kpiActivePOs = 1, "PO", "POs")
    End Function

    Private Shared Function MakeKpiCard(caption As String, value As String, valueClr As Color, sub1 As String) As Panel
        Dim card As New BufferedPanel With {.Dock = DockStyle.Fill, .BackColor = CardBg}
        AddHandler card.Paint, Sub(s, e) PaintCard(CType(s, Panel), e)

        Dim lblCap As New Label With {
            .Text = caption, .Font = New Font("Segoe UI", 11),
            .ForeColor = SubText, .AutoSize = True, .Location = New Point(22, 14), .BackColor = Color.Transparent
        }
        Dim lblVal As New Label With {
            .Text = value, .Font = New Font("Segoe UI", 26, FontStyle.Bold),
            .ForeColor = valueClr, .AutoSize = True, .Location = New Point(20, 36), .BackColor = Color.Transparent
        }
        Dim lblSub As New Label With {
            .Text = sub1, .Font = New Font("Segoe UI", 10),
            .ForeColor = SubText, .AutoSize = True, .Location = New Point(22, 100), .BackColor = Color.Transparent
        }
        card.Controls.AddRange({lblCap, lblVal, lblSub})
        Return card
    End Function

    ' =========================================================================
    '  LEFT CARD — PENDING REQUESTS
    ' =========================================================================
    Private Shared Function BuildPendingCard() As Panel
        Dim card As New BufferedPanel With {.Dock = DockStyle.Fill, .BackColor = CardBg}
        AddHandler card.Paint, Sub(s, e) PaintCard(CType(s, Panel), e)

        Dim header As Panel = BuildListHeader("Approved Requests",
        If(pending.Count = 0, "Nothing waiting", pending.Count & If(pending.Count = 1, " request", " requests")))
        header.Dock = DockStyle.Top
        card.Controls.Add(header)

        ' ── Column header row (transparent, inset divider — no gray strip) ──
        Dim colHeader As New Panel With {.Height = 34, .Dock = DockStyle.Top, .BackColor = Color.Transparent}
        Dim chProduct As New Label With {
    .Text = "PRODUCT", .Font = New Font("Segoe UI", 10.0F, FontStyle.Bold),
    .ForeColor = SubText, .AutoSize = True, .Location = New Point(18, 9), .BackColor = Color.Transparent}
        Dim chQty As New Label With {
    .Text = "QTY", .Font = New Font("Segoe UI", 10.0F, FontStyle.Bold),
    .ForeColor = SubText, .AutoSize = True, .Location = New Point(0, 9), .BackColor = Color.Transparent}
        Dim chBy As New Label With {
    .Text = "REQUESTED BY", .Font = New Font("Segoe UI", 10.0F, FontStyle.Bold),
    .ForeColor = SubText, .AutoSize = True, .Location = New Point(0, 9), .BackColor = Color.Transparent}
        Dim chWhen As New Label With {
    .Text = "WHEN", .Font = New Font("Segoe UI", 10.0F, FontStyle.Bold),
    .ForeColor = SubText, .AutoSize = True, .Location = New Point(0, 9), .BackColor = Color.Transparent}
        Dim chAction As New Label With {
    .Text = "ACTION", .Font = New Font("Segoe UI", 10.0F, FontStyle.Bold),
    .ForeColor = SubText, .AutoSize = True, .Location = New Point(0, 9), .BackColor = Color.Transparent,
    .Anchor = AnchorStyles.Top Or AnchorStyles.Right}
        colHeader.Controls.AddRange({chProduct, chQty, chBy, chWhen, chAction})

        ' inset bottom divider, painted (so it never touches the card edge)
        AddHandler colHeader.Paint,
    Sub(s, e)
        Using pen As New Pen(RowDivider, 1)
            e.Graphics.DrawLine(pen, 16, colHeader.Height - 1, colHeader.Width - 16, colHeader.Height - 1)
        End Using
    End Sub

        AddHandler colHeader.Resize,
    Sub()
        Dim w As Integer = colHeader.Width
        chProduct.Left = 18
        chQty.Left = 220
        chBy.Left = 310
        chWhen.Left = 490
        chWhen.Visible = (w >= PENDING_WHEN_MIN_W)
        chAction.Left = w - chAction.Width - 18
    End Sub

        card.Controls.Add(colHeader)
        colHeader.BringToFront()

        Dim rowsHost As New Panel With {
        .Dock = DockStyle.Fill, .BackColor = Color.Transparent, .AutoScroll = True,
        .Padding = New Padding(0, 2, 0, 8)
    }
        card.Controls.Add(rowsHost)
        rowsHost.BringToFront()

        If pending.Count = 0 Then
            rowsHost.Controls.Add(BuildEmptyRow("All caught up — no approved requests waiting."))
        Else
            Dim y As Integer = 4
            For Each req In pending
                Dim row As Panel = BuildPendingRow(req, 52)
                row.Location = New Point(0, y)
                row.Width = rowsHost.ClientSize.Width - 4
                row.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
                rowsHost.Controls.Add(row)
                y += 52
            Next
            Dim onPendingResize As EventHandler = Sub(s, e) ReflowRows(rowsHost)
            AddHandler rowsHost.Resize, onPendingResize
            AddHandler rowsHost.VisibleChanged,
                Sub()
                    If rowsHost.Visible Then ReflowRows(rowsHost)
                End Sub
        End If

        Return card
    End Function

    Private Shared Function BuildPendingRow(req As PendingReq, height As Integer) As Panel
        Dim row As New BufferedPanel With {.Height = height, .BackColor = Color.Transparent}
        AddHandler row.Paint,
    Sub(s, e)
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
        If TypeOf row.Tag Is Boolean AndAlso CBool(row.Tag) Then
            Dim rect As New Rectangle(8, 3, row.Width - 16, row.Height - 6)
            Using path As GraphicsPath = RoundRect(rect, 10)
                Using b As New SolidBrush(RowHover)
                    e.Graphics.FillPath(b, path)
                End Using
            End Using
        End If
        Using pen As New Pen(RowDivider, 1)
            e.Graphics.DrawLine(pen, 16, row.Height - 1, row.Width - 16, row.Height - 1)
        End Using
    End Sub


        ' Col 1 — Product name
        Dim lblName As New Label With {
    .Text = req.ProductName, .Font = New Font("Segoe UI", 13.0F, FontStyle.Bold),
    .ForeColor = MintDark, .AutoSize = True, .BackColor = Color.Transparent,
    .Location = New Point(18, (height - 16) \ 2)}

        ' Col 2 — Qty pill
        Dim qtyPill As New Label With {
    .Text = "x" & req.QtyRequested, .Font = New Font("Segoe UI", 10.5F, FontStyle.Bold),
    .ForeColor = MintDeep, .BackColor = Color.Transparent, .AutoSize = True,
    .Padding = New Padding(7, 2, 7, 2)
}
        PillLabel(qtyPill)

        ' Col 3 — Requested by
        Dim lblBy As New Label With {
    .Text = req.RequestedBy, .Font = New Font("Segoe UI", 11.0F),
    .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent
}
        Dim lblWhen As New Label With {
    .Text = TimeAgo(req.CreatedAt), .Font = New Font("Segoe UI", 11.0F),
    .ForeColor = FaintText, .AutoSize = True, .BackColor = Color.Transparent
}

        ' Col 5 — Review link
        Dim btnReview As Label = MakeLink("Review", MintPrimary)
        btnReview.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        Dim capId As Integer = req.RequestId
        AddHandler btnReview.Click, Sub() OpenReview(capId)

        row.Controls.AddRange({lblName, qtyPill, lblBy, lblWhen, btnReview})

        AddHandler row.Resize,
    Sub()
        Dim w As Integer = row.Width
        Dim showWhen As Boolean = (w >= PENDING_WHEN_MIN_W)
        btnReview.Left = w - btnReview.Width - 18
        btnReview.Top = (height - btnReview.Height) \ 2
        TextFit.Clamp(lblName, 220 - 18 - 12)
        lblName.Left = 18 : lblName.Top = (height - lblName.Height) \ 2
        qtyPill.Left = 220 : qtyPill.Top = (height - qtyPill.Height) \ 2
        TextFit.Clamp(lblBy, If(showWhen, 490, btnReview.Left) - 310 - 12)
        lblBy.Left = 310 : lblBy.Top = (height - lblBy.Height) \ 2
        lblWhen.Left = 490 : lblWhen.Top = (height - lblWhen.Height) \ 2
        lblWhen.Visible = showWhen
    End Sub
        PropagateHover(row)
        Return row
    End Function

    ' =========================================================================
    '  RIGHT CARD — POs DUE SOON
    ' =========================================================================
    ' On a narrow card (smaller screens) the least important column is dropped
    ' so the names keep enough room; they end in "..." when still too long.
    Private Const PENDING_WHEN_MIN_W As Integer = 640
    Private Const DUE_ITEMS_MIN_W As Integer = 620

    Private Shared Function DueSupplierLeft(w As Integer) As Integer
        Return If(w >= DUE_ITEMS_MIN_W, 200, 160)
    End Function

    Private Shared Function BuildDueSoonCard() As Panel
        Dim card As New BufferedPanel With {.Dock = DockStyle.Fill, .BackColor = CardBg}
        AddHandler card.Paint, Sub(s, e) PaintCard(CType(s, Panel), e)

        Dim header As Panel = BuildListHeader("POs Due Soon",
        If(dueSoon.Count = 0, "Nothing due", dueSoon.Count & If(dueSoon.Count = 1, " order", " orders")))
        header.Dock = DockStyle.Top
        card.Controls.Add(header)

        ' ── Column header row ──
        ' ── Column header row (transparent, inset divider) ──
        Dim colHeader As New Panel With {.Height = 34, .Dock = DockStyle.Top, .BackColor = Color.Transparent}
        Dim chPo As New Label With {
    .Text = "PO NO.", .Font = New Font("Segoe UI", 10.0F, FontStyle.Bold),
    .ForeColor = SubText, .AutoSize = True, .Location = New Point(18, 9), .BackColor = Color.Transparent}
        Dim chSupplier As New Label With {
    .Text = "SUPPLIER", .Font = New Font("Segoe UI", 10.0F, FontStyle.Bold),
    .ForeColor = SubText, .AutoSize = True, .Location = New Point(0, 9), .BackColor = Color.Transparent}
        Dim chItems As New Label With {
    .Text = "ITEMS", .Font = New Font("Segoe UI", 10.0F, FontStyle.Bold),
    .ForeColor = SubText, .AutoSize = True, .Location = New Point(0, 9), .BackColor = Color.Transparent}
        Dim chCost As New Label With {
    .Text = "TOTAL", .Font = New Font("Segoe UI", 10.0F, FontStyle.Bold),
    .ForeColor = SubText, .AutoSize = True, .Location = New Point(0, 9), .BackColor = Color.Transparent}
        Dim chStatus As New Label With {
    .Text = "STATUS", .Font = New Font("Segoe UI", 10.0F, FontStyle.Bold),
    .ForeColor = SubText, .AutoSize = True, .Location = New Point(0, 9), .BackColor = Color.Transparent,
    .Anchor = AnchorStyles.Top Or AnchorStyles.Right}
        colHeader.Controls.AddRange({chPo, chSupplier, chItems, chCost, chStatus})

        AddHandler colHeader.Paint,
    Sub(s, e)
        Using pen As New Pen(RowDivider, 1)
            e.Graphics.DrawLine(pen, 16, colHeader.Height - 1, colHeader.Width - 16, colHeader.Height - 1)
        End Using
    End Sub

        AddHandler colHeader.Resize,
    Sub()
        Dim w As Integer = colHeader.Width
        chPo.Left = 18
        chSupplier.Left = DueSupplierLeft(w)
        chItems.Left = w - 290
        chItems.Visible = (w >= DUE_ITEMS_MIN_W)
        chCost.Left = w - 190
        chStatus.Left = w - 95
    End Sub
        card.Controls.Add(colHeader)
        colHeader.BringToFront()

        Dim rowsHost As New Panel With {
        .Dock = DockStyle.Fill, .BackColor = Color.Transparent, .AutoScroll = True,
        .Padding = New Padding(0, 2, 0, 8)
    }
        card.Controls.Add(rowsHost)
        rowsHost.BringToFront()

        If dueSoon.Count = 0 Then
            rowsHost.Controls.Add(BuildEmptyRow("No ordered POs are due in the next 7 days."))
        Else
            Dim y As Integer = 4
            For Each po In dueSoon
                Dim row As Panel = BuildDueRow(po, 52)
                row.Location = New Point(0, y)
                row.Width = rowsHost.ClientSize.Width - 4
                row.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
                rowsHost.Controls.Add(row)
                y += 52
            Next
            Dim onDueSoonResize As EventHandler = Sub(s, e) ReflowRows(rowsHost)
            AddHandler rowsHost.Resize, onDueSoonResize
            AddHandler rowsHost.VisibleChanged,
                Sub()
                    If rowsHost.Visible Then ReflowRows(rowsHost)
                End Sub
        End If

        Return card
    End Function

    Private Shared Function BuildDueRow(po As DuePO, height As Integer) As Panel
        Dim row As New Panel With {.Height = height, .BackColor = Color.Transparent}
        AddHandler row.Paint,
    Sub(s, e)
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
        If TypeOf row.Tag Is Boolean AndAlso CBool(row.Tag) Then
            Dim rect As New Rectangle(8, 3, row.Width - 16, row.Height - 6)
            Using path As GraphicsPath = RoundRect(rect, 10)
                Using b As New SolidBrush(RowHover)
                    e.Graphics.FillPath(b, path)
                End Using
            End Using
        End If
        Using pen As New Pen(RowDivider, 1)
            e.Graphics.DrawLine(pen, 16, row.Height - 1, row.Width - 16, row.Height - 1)
        End Using
    End Sub


        ' Col 1 — PO number
        Dim lblPo As New Label With {
    .Text = po.PoNo, .Font = New Font("Segoe UI", 13.0F, FontStyle.Bold),
    .ForeColor = MintDark, .AutoSize = True, .BackColor = Color.Transparent,
    .Location = New Point(18, (height - 16) \ 2)}

        ' Col 2 — Supplier
        Dim lblSupplier As New Label With {
    .Text = po.SupplierName, .Font = New Font("Segoe UI", 11.5F),
    .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent
}
        Dim lblItems As New Label With {
    .Text = po.ItemCount & If(po.ItemCount = 1, " item", " items"),
    .Font = New Font("Segoe UI", 11.0F),
    .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent
}
        Dim lblCost As New Label With {
    .Text = Peso & po.TotalCost.ToString("N2"), .Font = New Font("Segoe UI", 12.0F, FontStyle.Bold),
    .ForeColor = MintPrimary, .AutoSize = True, .BackColor = Color.Transparent
}

        ' Col 5 — Badge (right-aligned)
        Dim st = DueBadge(po)
        Dim badge As New Label With {
        .Text = st.text, .Font = New Font("Segoe UI", 10.0F, FontStyle.Bold),
        .ForeColor = st.fore, .BackColor = st.back, .AutoSize = True,
        .Padding = New Padding(8, 3, 8, 3), .Anchor = AnchorStyles.Top Or AnchorStyles.Right
    }
        PillLabel(badge)

        row.Controls.AddRange({lblPo, lblSupplier, lblItems, lblCost, badge})

        AddHandler row.Resize,
    Sub()
        Dim w As Integer = row.Width
        lblPo.Left = 18 : lblPo.Top = (height - lblPo.Height) \ 2
        Dim showItems As Boolean = (w >= DUE_ITEMS_MIN_W)
        Dim supLeft As Integer = DueSupplierLeft(w)
        TextFit.Clamp(lblSupplier, If(showItems, w - 290, w - 200) - supLeft - 12)
        lblSupplier.Left = supLeft : lblSupplier.Top = (height - lblSupplier.Height) \ 2
        lblItems.Left = w - 290 : lblItems.Top = (height - lblItems.Height) \ 2
        lblItems.Visible = showItems
        lblCost.Left = w - 200 : lblCost.Top = (height - lblCost.Height) \ 2
        badge.Left = w - 95 : badge.Top = (height - badge.Height) \ 2
    End Sub
        PropagateHover(row)
        Return row
    End Function

    ' =========================================================================
    '  SHARED LIST-CARD HELPERS
    ' =========================================================================
    Private Shared Function BuildListHeader(title As String, countText As String) As Panel
        Dim header As New Panel With {.Height = 56, .BackColor = Color.Transparent}

        Dim lblTitle As New Label With {
            .Text = title, .Font = New Font("Segoe UI", 15, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(18, 16), .BackColor = Color.Transparent
        }
        Dim lblCount As New Label With {
            .Text = countText, .Font = New Font("Segoe UI", 10.5F),
            .ForeColor = FaintText, .AutoSize = True, .BackColor = Color.Transparent,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        header.Controls.AddRange({lblTitle, lblCount})

        Dim divider As New Panel With {.Height = 1, .BackColor = RowDivider, .Location = New Point(16, 54)}
        divider.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        header.Controls.Add(divider)

        AddHandler header.Resize,
            Sub()
                lblCount.Left = header.Width - lblCount.Width - 18
                lblCount.Top = 20
                divider.Width = header.Width - 32
            End Sub

        Return header
    End Function

    Private Shared Function BuildEmptyRow(message As String) As Panel
        Dim p As New Panel With {.Height = 90, .Dock = DockStyle.Top, .BackColor = Color.Transparent}
        Dim lbl As New Label With {
    .Text = message, .Font = New Font("Segoe UI", 10.5F), .ForeColor = SubText,
    .AutoSize = True, .Location = New Point(20, 28), .BackColor = Color.Transparent
}
        p.Controls.Add(lbl)
        Return p
    End Function

    Private Shared Sub ReflowRows(host As Panel)
        If host Is Nothing Then Return
        Dim w As Integer = host.ClientSize.Width - 4
        For Each c As Control In host.Controls
            c.Width = w
        Next
    End Sub

    ' =========================================================================
    '  RELATIVE TIME + DUE TEXT
    ' =========================================================================
    Private Shared Function TimeAgo(dt As DateTime) As String
        Dim span As TimeSpan = DateTime.Now - dt
        If span.TotalSeconds < 60 Then Return "just now"
        If span.TotalMinutes < 60 Then
            Dim m As Integer = CInt(Math.Floor(span.TotalMinutes))
            Return m & If(m = 1, " min ago", " mins ago")
        End If
        If span.TotalHours < 24 Then
            Dim h As Integer = CInt(Math.Floor(span.TotalHours))
            Return h & If(h = 1, " hr ago", " hrs ago")
        End If
        Dim d As Integer = CInt(Math.Floor(span.TotalDays))
        If d < 30 Then Return d & If(d = 1, " day ago", " days ago")
        Return dt.ToString("MMM d, yyyy")
    End Function



    Private Shared Function DueBadge(po As DuePO) As (text As String, fore As Color, back As Color)
        If Not po.HasExpected Then
            Return ("No date", FaintText, Color.FromArgb(238, 240, 239))
        End If
        Dim days As Integer = CInt(Math.Floor((po.ExpectedDate.Date - DateTime.Today).TotalDays))
        If days < 0 Then
            Return ("Overdue", NegativeClr, Color.FromArgb(251, 234, 234))
        ElseIf days <= 2 Then
            Return ("Due soon", WarningClr, Color.FromArgb(252, 240, 228))
        Else
            Return ("Ordered", MintPrimary, TileBg)
        End If
    End Function

    ' =========================================================================
    '  NAVIGATION
    ' =========================================================================
    Private Shared Sub OpenReview(requestId As Integer)
        Try
            Dim host As Control = If(containerRef, CType(Nothing, Control))
            If host Is Nothing Then Return
            Dim frm = TryCast(host.FindForm(), PROCUREMENT_STAFF)
            If frm Is Nothing Then Return
            frm.OpenPOWithRequest(requestId)
        Catch ex As Exception
            AppModal.Show("Could not open Purchase Orders:" & Environment.NewLine & ex.Message,
                          "Navigation Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    ' =========================================================================
    '  SHARED PAINT / CONTROL HELPERS  (verbatim from InventoryLowStockPanelBuilder)
    ' =========================================================================
    Private Shared Sub PaintCard(panel As Panel, e As PaintEventArgs)
        PanelRenderer.Draw(panel, e, True)
    End Sub

    Private Shared Function MakeLink(text As String, clr As Color) As Label
        Dim lbl As New Label With {
            .Text = text, .Font = New Font("Segoe UI", 10.0F, FontStyle.Bold),
            .ForeColor = clr, .AutoSize = True, .Cursor = Cursors.Hand, .BackColor = Color.Transparent
        }
        AddHandler lbl.MouseEnter, Sub() lbl.Font = New Font("Segoe UI", 10F, FontStyle.Bold Or FontStyle.Underline)
        AddHandler lbl.MouseLeave, Sub() lbl.Font = New Font("Segoe UI", 10.0F, FontStyle.Bold)
        Return lbl
    End Function

    Private Shared Function SafeInt(o As Object) As Integer
        If o Is Nothing OrElse IsDBNull(o) Then Return 0
        Dim n As Integer
        If Integer.TryParse(o.ToString(), n) Then Return n
        Return 0
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
    Private Shared ReadOnly RowHover As Color = Theme.RowHover

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
        row.Tag = on_      ' row.Paint reads this
        row.Invalidate()
    End Sub


    ' True pill: corner radius always equals the label's full height, so both
    ' ends are perfect semicircles no matter how the text/padding sizes it.
    ' AutoSize labels have usually already sized themselves by the time this is
    ' called, so apply the region NOW as well as on every later resize.
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

    Private Sub ProcurementDashPanelBuilder_Load(sender As Object, e As EventArgs) Handles MyBase.Load

    End Sub
End Class