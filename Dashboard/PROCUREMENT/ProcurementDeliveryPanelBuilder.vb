Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports MySql.Data.MySqlClient

' =============================================================================
'  ProcurementDeliveryPanelBuilder
'  Screen 5 — Delivery Tracking (read only).
'
'  A timeline of every non-cancelled PO, bucketed by expected date into
'  Overdue / Due Today / Due This Week / Upcoming. Clicking a card expands its
'  po_items inline (product, ordered qty, per-line received status). The only
'  action is "Go to Receive", which hands off to the Receive screen via
'  PROCUREMENT_STAFF.OpenReceiveForPO(poId). No edits happen here.
'
'  Shared-only, no instance state. Verbatim helpers from
'  InventoryLowStockPanelBuilder.
' =============================================================================
Public Class ProcurementDeliveryPanelBuilder

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
    Private Class DeliveryPO
        Public PoId As Integer
        Public PoNo As String
        Public SupplierName As String
        Public OrderDate As DateTime
        Public HasExpected As Boolean
        Public ExpectedDate As DateTime
        Public Status As String
        Public ItemCount As Integer
    End Class

    Private Class DeliveryItem
        Public ProductName As String
        Public Sku As String
        Public OrderedQty As Integer
        Public Received As Integer
    End Class

    ' ── STATE ────────────────────────────────────────────────────────────────────
    Private Shared containerRef As Panel
    Private Shared rowsHostRef As Panel
    Private Shared allPos As New List(Of DeliveryPO)
    Private Shared expandedIds As New HashSet(Of Integer)
    Private Shared itemsCache As New Dictionary(Of Integer, List(Of DeliveryItem))

    Private Shared ReadOnly GroupOrder() As String = {"Overdue", "Due Today", "Due This Week", "Upcoming"}

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Shared Sub LoadDelivery(container As Panel)
        containerRef = container
        expandedIds = New HashSet(Of Integer)()
        itemsCache = New Dictionary(Of Integer, List(Of DeliveryItem))()
        allPos = LoadAll()

        container.SuspendLayout()
        container.Controls.Clear()
        container.BackColor = MintBg

        Dim listHost As New Panel With {
            .Dock = DockStyle.Fill, .BackColor = MintBg, .Padding = New Padding(24, 4, 24, 20)
        }
        rowsHostRef = New Panel With {
            .Dock = DockStyle.Fill, .BackColor = Color.Transparent, .AutoScroll = True
        }
        listHost.Controls.Add(rowsHostRef)

        Dim topRegion As Panel = BuildTop()
        topRegion.Dock = DockStyle.Top

        container.Controls.Add(listHost)
        container.Controls.Add(topRegion)
        container.ResumeLayout()

        AddHandler rowsHostRef.Resize,
            Sub()
                Dim w As Integer = rowsHostRef.ClientSize.Width - 4
                For Each c As Control In rowsHostRef.Controls
                    c.Width = w
                Next
            End Sub

        RenderBody()
    End Sub

    Private Shared Function BuildTop() As Panel
        Dim region As New Panel With {.Height = 96, .BackColor = MintBg, .Padding = New Padding(24, 22, 24, 0)}
        Dim lblTitle As New Label With {
            .Text = "Delivery Tracking", .Font = New Font("Segoe UI", 24, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(24, 18)
        }
        Dim overdue As Integer = 0
        For Each po In allPos
            If GroupOf(po) = "Overdue" Then overdue += 1
        Next
        Dim lblSub As New Label With {
            .Text = allPos.Count & If(allPos.Count = 1, " active order", " active orders") &
                    If(overdue > 0, "   " & ChrW(8226) & "   " & overdue & " overdue", ""),
            .Font = New Font("Segoe UI", 11), .ForeColor = If(overdue > 0, NegativeClr, SubText),
            .AutoSize = True, .Location = New Point(30, 58)
        }
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
        AddHandler btnRefresh.Click, Sub() LoadDelivery(containerRef)
        AddHandler region.Resize,
            Sub()
                btnRefresh.Left = region.Width - btnRefresh.Width - 24
                btnRefresh.Top = (region.Height - btnRefresh.Height) \ 2
            End Sub

        region.Controls.AddRange({lblTitle, lblSub, btnRefresh})
        Return region
    End Function

    ' =========================================================================
    '  BODY (re-rendered on every expand/collapse)
    ' =========================================================================
    Private Shared Sub RenderBody()
        Dim host As Panel = rowsHostRef
        If host Is Nothing Then Return
        host.SuspendLayout()
        host.Controls.Clear()

        If allPos.Count = 0 Then
            Dim empty As Panel = BuildEmptyCard("No deliveries to track",
                "There are no active (non-cancelled) purchase orders right now.")
            empty.Location = New Point(0, 16)
            empty.Width = host.ClientSize.Width - 4
            empty.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
            host.Controls.Add(empty)
            host.ResumeLayout()
            Return
        End If

        Dim w As Integer = host.ClientSize.Width - 4
        Dim y As Integer = 4

        For Each grp In GroupOrder
            Dim members As New List(Of DeliveryPO)
            For Each po In allPos
                If GroupOf(po) = grp Then members.Add(po)
            Next
            If members.Count = 0 Then Continue For

            ' Group header
            Dim head As Panel = BuildGroupHeader(grp, members.Count)
            head.Location = New Point(0, y)
            head.Width = w
            head.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
            host.Controls.Add(head)
            y += head.Height + 2

            For Each po In members
                Dim card As Panel = BuildDeliveryCard(po)
                card.Location = New Point(0, y)
                card.Width = w
                card.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
                host.Controls.Add(card)
                y += card.Height + 8

                If expandedIds.Contains(po.PoId) Then
                    Dim items As Panel = BuildItemsPanel(po)
                    items.Location = New Point(0, y)
                    items.Width = w
                    items.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
                    host.Controls.Add(items)
                    y += items.Height + 8
                End If
            Next
            y += 10
        Next

        host.ResumeLayout()
    End Sub

    Private Shared Function BuildGroupHeader(name As String, count As Integer) As Panel
        Dim p As New Panel With {.Height = 38, .BackColor = Color.Transparent}
        Dim accent As Color = If(name = "Overdue", NegativeClr,
                              If(name = "Due Today", WarningClr,
                              If(name = "Due This Week", MintPrimary, MintDeep)))

        Dim dotSize As Integer = 10
        Dim dotTop As Integer = 14
        Dim dot As New BufferedPanel With {
            .Size = New Size(dotSize, dotSize),
            .Location = New Point(2, dotTop),
            .BackColor = Color.Transparent
        }
        AddHandler dot.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Using br As New SolidBrush(accent)
                    e.Graphics.FillEllipse(br, 0, 0, dotSize - 1, dotSize - 1)
                End Using
            End Sub

        Dim lbl As New Label With {
            .Text = name & "  (" & count & ")",
            .Font = New Font("Segoe UI", 12, FontStyle.Bold),
            .ForeColor = accent, .AutoSize = True,
            .Location = New Point(18, dotTop - 4),
            .BackColor = Color.Transparent
        }

        ' vertically center dot to label
        AddHandler lbl.Resize,
            Sub()
                dot.Top = lbl.Top + (lbl.Height - dotSize) \ 2
            End Sub

        p.Controls.AddRange({dot, lbl})
        Return p
    End Function

    Private Shared Function BuildDeliveryCard(po As DeliveryPO) As Panel
        Dim cardH As Integer = 110
        Dim card As New BufferedPanel With {.Height = cardH, .BackColor = CardBg, .Cursor = Cursors.Hand}
        AddHandler card.Paint,
            Sub(s, e)
                PaintCard(CType(s, Panel), e)
            End Sub

        Dim lblPo As New Label With {
            .Text = po.PoNo, .Font = New Font("Segoe UI", 16, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(22, 16), .BackColor = Color.Transparent
        }
        Dim lblMeta As New Label With {
            .Text = po.SupplierName & "   " & ChrW(8226) & "   Ordered " & po.OrderDate.ToString("MMM d, yyyy") &
                    "   " & ChrW(8226) & "   " & po.ItemCount & If(po.ItemCount = 1, " item", " items"),
            .Font = New Font("Segoe UI", 10.5F), .ForeColor = SubText, .AutoSize = True,
            .Location = New Point(24, 50), .BackColor = Color.Transparent
        }
        card.Controls.AddRange({lblPo, lblMeta})

        ' Expected date chip
        Dim expText As String = If(po.HasExpected, "Expected " & po.ExpectedDate.ToString("MMM d"), "No date")
        ' Expected date reads as quiet supporting text, not a filled chip.
        Dim expChip As New Label With {
            .Text = expText, .Font = New Font("Segoe UI", 9.5F, FontStyle.Regular),
            .ForeColor = If(po.HasExpected, SubText, FaintText),
            .BackColor = Color.Transparent,
            .AutoSize = True, .Padding = New Padding(0, 7, 4, 7),
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        card.Controls.Add(expChip)

        Dim st = StatusInfo(po.Status)
        Dim badgeAccent As Color = If(po.Status = "Received", PositiveClr,
                                      If(po.Status = "Partially Received", WarningClr, MintDeep))
        Dim badge As Label = OutlineBadge(st.text, badgeAccent)
        card.Controls.Add(badge)

        Dim caret As New Label With {
            .Text = If(expandedIds.Contains(po.PoId), ChrW(9650), ChrW(9660)),
            .Font = New Font("Segoe UI", 11), .ForeColor = SubText, .AutoSize = True,
            .BackColor = Color.Transparent, .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        card.Controls.Add(caret)

        ' Go to Receive link (only when receivable)
        Dim lnkReceive As Label = Nothing
        If po.Status = "Ordered" OrElse po.Status = "Partially Received" Then
            lnkReceive = MakeLink("Go to Receive " & ChrW(8594), MintPrimary)
            lnkReceive.Anchor = AnchorStyles.Top Or AnchorStyles.Right
            card.Controls.Add(lnkReceive)
            Dim capId As Integer = po.PoId
            AddHandler lnkReceive.Click, Sub() GoToReceive(capId)
        End If

        Dim capPoId As Integer = po.PoId
        Dim toggle As Action = Sub() ToggleExpand(capPoId)
        AddHandler card.Click, Sub() toggle()
        AddHandler lblPo.Click, Sub() toggle()
        AddHandler lblMeta.Click, Sub() toggle()
        AddHandler caret.Click, Sub() toggle()

        AddHandler card.Resize,
            Sub()
                Const rPad As Integer = 24
                badge.Left = card.Width - badge.Width - rPad
                badge.Top = 20
                expChip.Left = badge.Left - expChip.Width - 10
                expChip.Top = 20
                caret.Left = card.Width - caret.Width - rPad
                caret.Top = cardH - caret.Height - 18
                If lnkReceive IsNot Nothing Then
                    lnkReceive.Left = caret.Left - lnkReceive.Width - 16
                    lnkReceive.Top = cardH - lnkReceive.Height - 18
                End If
                Dim rightStop As Integer = expChip.Left - 20
                lblPo.MaximumSize = New Size(Math.Max(80, rightStop - 22), 30)
                lblMeta.MaximumSize = New Size(Math.Max(80, rightStop - 24), 26)
            End Sub

        Return card
    End Function

    Private Shared Function BuildItemsPanel(po As DeliveryPO) As Panel
        Dim items As List(Of DeliveryItem) = GetItems(po.PoId)
        Dim rowH As Integer = 76
        Dim h As Integer = 14 + Math.Max(1, items.Count) * rowH + 8
        Dim panel As New BufferedPanel With {
        .Height = h,
        .BackColor = Color.White
    }

        ' Border only — no background fill so rows aren't clipped
        AddHandler panel.Paint,
        Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, False)

        If items.Count = 0 Then
            panel.Controls.Add(New Label With {
            .Text = "No line items on this purchase order.",
            .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = SubText,
            .AutoSize = True,
            .Location = New Point(24, 16),
            .BackColor = Color.Transparent
        })
            Return panel
        End If

        Dim y As Integer = 8
        For Each it In items
            Dim r As Panel = BuildItemRow(it, rowH)
            r.Location = New Point(0, y)
            r.Width = panel.ClientSize.Width - 4
            r.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
            panel.Controls.Add(r)
            y += rowH
        Next

        AddHandler panel.Resize,
            Sub()
                Dim w As Integer = panel.ClientSize.Width - 4
                For Each c As Control In panel.Controls
                    If TypeOf c Is Panel Then c.Width = w
                Next
            End Sub
        AddHandler panel.VisibleChanged,
            Sub()
                If panel.Visible Then
                    Dim w As Integer = panel.ClientSize.Width - 4
                    For Each c As Control In panel.Controls
                        If TypeOf c Is Panel Then c.Width = w
                    Next
                End If
            End Sub
        Return panel
    End Function

    Private Shared Function BuildItemRow(it As DeliveryItem, height As Integer) As Panel
        Dim rowH As Integer = height
        Dim row As New BufferedPanel With {.Height = rowH, .BackColor = Color.Transparent}

        AddHandler row.Paint,
        Sub(s, e)
            If row.Parent Is Nothing Then Return
            Dim isLast As Boolean = (row.Bottom >= row.Parent.Height - 16)
            If Not isLast Then
                Using pen As New Pen(Color.FromArgb(228, 232, 230), 1)
                    e.Graphics.DrawLine(pen, 24, rowH - 1, row.Width - 24, rowH - 1)
                End Using
            End If
        End Sub

        Dim lblName As New Label With {
        .Text = it.ProductName,
        .Font = New Font("Segoe UI", 14, FontStyle.Bold),
        .ForeColor = Color.FromArgb(10, 79, 66),
        .AutoSize = True,
        .Location = New Point(24, 12),
        .BackColor = Color.Transparent
    }

        Dim lblSku As New Label With {
        .Text = it.Sku,
        .Font = New Font("Segoe UI", 9, FontStyle.Regular),
        .ForeColor = Color.FromArgb(150, 165, 160),
        .AutoSize = True,
        .BackColor = Color.Transparent
    }

        Dim qtyText As String = "Ordered " & it.OrderedQty
        If it.Received > 0 Then
            qtyText &= "   " & ChrW(183) & "   Received " & it.Received
        End If
        Dim lblQty As New Label With {
        .Text = qtyText,
        .Font = New Font("Segoe UI", 11, FontStyle.Regular),
        .ForeColor = Color.FromArgb(90, 114, 107),
        .AutoSize = True,
        .Location = New Point(24, 42),
        .BackColor = Color.Transparent
    }

        row.Controls.AddRange({lblName, lblSku, lblQty})

        Dim ls = LineStatus(it)
        Dim badge As Label = OutlineBadge(ls.text, ls.fore)
        row.Controls.Add(badge)

        AddHandler row.Resize,
        Sub()
            badge.Left = row.Width - badge.Width - 24
            badge.Top = (rowH - badge.Height) \ 2
            lblSku.Left = lblName.Right + 10
            lblSku.Top = lblName.Top + (lblName.Height - lblSku.Height) \ 2
            lblName.MaximumSize = New Size(Math.Max(80, badge.Left - 40), 30)
        End Sub

        Return row
    End Function

    ' =========================================================================
    '  EXPAND / NAVIGATION
    ' =========================================================================
    Private Shared Sub ToggleExpand(poId As Integer)
        If expandedIds.Contains(poId) Then
            expandedIds.Remove(poId)
        Else
            expandedIds.Add(poId)
            GetItems(poId)   ' warm the cache before re-render
        End If
        RenderBody()
    End Sub

    Private Shared Sub GoToReceive(poId As Integer)
        Try
            Dim host As Control = If(containerRef, CType(Nothing, Control))
            If host Is Nothing Then Return
            Dim frm = TryCast(host.FindForm(), PROCUREMENT_STAFF)
            If frm Is Nothing Then Return
            frm.OpenReceiveForPO(poId)
        Catch ex As Exception
            AppModal.Show("Could not open Receive Goods:" & Environment.NewLine & ex.Message,
                          "Navigation Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    ' =========================================================================
    '  GROUPING
    ' =========================================================================
    Private Shared Function GroupOf(po As DeliveryPO) As String
        If po.Status = "Received" Then Return "Upcoming"
        If Not po.HasExpected Then Return "Upcoming"
        Dim days As Integer = CInt((po.ExpectedDate.Date - DateTime.Today).TotalDays)
        If days < 0 Then Return "Overdue"
        If days = 0 Then Return "Due Today"
        If days <= 7 Then Return "Due This Week"
        Return "Upcoming"
    End Function



    Private Shared Function LineStatus(it As DeliveryItem) As (text As String, fore As Color, back As Color)
        If it.Received >= it.OrderedQty AndAlso it.OrderedQty > 0 Then
            Return ("Received", PositiveClr, Color.FromArgb(232, 245, 233))
        ElseIf it.Received > 0 Then
            Return ("Partial  " & it.Received & "/" & it.OrderedQty, WarningClr, Color.FromArgb(252, 240, 228))
        Else
            Return ("Pending", FaintText, Color.FromArgb(238, 240, 239))
        End If
    End Function

    ' =========================================================================
    '  DATA LOADERS
    ' =========================================================================
    Private Shared Function LoadAll() As List(Of DeliveryPO)
        Dim result As New List(Of DeliveryPO)
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT po.po_id, po.po_no, s.name AS sname, po.order_date, po.expected_date, " &
                    "po.status, " &
                    "(SELECT COUNT(*) FROM po_items pi WHERE pi.po_id = po.po_id) AS item_count " &
                    "FROM purchase_orders po JOIN suppliers s ON po.supplier_id = s.supplier_id " &
                    "WHERE po.status <> 'Cancelled' " &
                    "ORDER BY (po.expected_date IS NULL), po.expected_date ASC, po.po_id ASC", conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            Dim hasExp As Boolean = Not IsDBNull(rdr("expected_date"))
                            result.Add(New DeliveryPO With {
                                .PoId = SafeInt(rdr("po_id")),
                                .PoNo = rdr("po_no").ToString(),
                                .SupplierName = rdr("sname").ToString(),
                                .OrderDate = If(IsDBNull(rdr("order_date")), DateTime.Today, Convert.ToDateTime(rdr("order_date"))),
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
            AppModal.Show("Could not load deliveries:" & Environment.NewLine & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
        Return result
    End Function

    Private Shared Function GetItems(poId As Integer) As List(Of DeliveryItem)
        If itemsCache.ContainsKey(poId) Then Return itemsCache(poId)
        Dim result As New List(Of DeliveryItem)
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT p.name AS pname, p.sku, pi.qty, " &
                    "COALESCE((SELECT SUM(sm.qty_change) FROM stock_movements sm " &
                    " WHERE sm.reference_id = @poid AND sm.reference_type = 'receive' " &
                    " AND sm.product_id = pi.product_id), 0) AS received " &
                    "FROM po_items pi JOIN products p ON pi.product_id = p.product_id " &
                    "WHERE pi.po_id = @poid ORDER BY p.name ASC", conn)
                    cmd.Parameters.AddWithValue("@poid", poId)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            result.Add(New DeliveryItem With {
                                .ProductName = rdr("pname").ToString(),
                                .Sku = rdr("sku").ToString(),
                                .OrderedQty = SafeInt(rdr("qty")),
                                .Received = SafeInt(rdr("received"))
                            })
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            Debug.WriteLine("ProcurementDelivery GetItems error (PO " & poId & "): " & ex.Message)
        End Try
        itemsCache(poId) = result
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



    ' True pill: corner radius always equals the label's full height, so both
    ' ends are perfect semicircles no matter how the text/padding sizes it.
    ' AutoSize labels have usually already sized themselves by the time this is
    ' called, so apply the region NOW as well as on every later resize.
    ' Minimalist status badge: no fill, just the status colour as text plus a
    ' hairline border at a soft 6px radius. Owner-drawn because a WinForms Label
    ' cannot paint its own border without going square/3-D.
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


End Class