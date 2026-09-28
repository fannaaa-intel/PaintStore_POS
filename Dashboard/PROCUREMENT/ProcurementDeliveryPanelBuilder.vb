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
        ' Defective / missing lines recorded at receiving (DeliveryIssues).
        Public IssueCount As Integer
    End Class

    Private Class DeliveryItem
        Public ProductId As Integer
        Public ProductName As String
        Public Sku As String
        Public OrderedQty As Integer
        Public Received As Integer
        Public Issues As New List(Of DeliveryIssues.Issue)
    End Class

    ' One attested handling event for a PO: who took responsibility, when, for
    ' how many units, and in which phase. Read from goods_receipts — the whole
    ' point of the table is that this screen can answer "who accepted this"
    ' without anyone opening the database.
    Private Class ReceiptEvent
        Public Phase As String          ' "receive" | "putaway"
        Public OfficerName As String
        Public ConditionNote As String  ' "Good" | "Discrepancy"
        Public Remarks As String
        Public TotalUnits As Integer
        Public HappenedAt As DateTime
    End Class

    ' ── STATE ────────────────────────────────────────────────────────────────────
    Private Shared containerRef As Panel
    Private Shared rowsHostRef As Panel
    Private Shared allPos As New List(Of DeliveryPO)
    Private Shared expandedIds As New HashSet(Of Integer)
    Private Shared itemsCache As New Dictionary(Of Integer, List(Of DeliveryItem))
    Private Shared receiptsCache As New Dictionary(Of Integer, List(Of ReceiptEvent))

    Private Shared ReadOnly GroupOrder() As String = {"Overdue", "Due Today", "Due This Week", "Upcoming"}

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Shared Sub LoadDelivery(container As Panel)
        containerRef = container
        expandedIds = New HashSet(Of Integer)()
        itemsCache = New Dictionary(Of Integer, List(Of DeliveryItem))()
        receiptsCache = New Dictionary(Of Integer, List(Of ReceiptEvent))()
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
        AddHandler region.Resize,
            Sub()
            End Sub

        region.Controls.AddRange({lblTitle, lblSub})
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

        ' Problems found at receiving: flagged on the card itself, so a
        ' delivery with defects or short items is visible without opening it.
        Dim defectBadge As Label = Nothing
        If po.IssueCount > 0 Then
            defectBadge = OutlineBadge(ChrW(9888) & "  Has defect / short", NegativeClr)
            defectBadge.Font = New Font("Segoe UI Semibold", 9.0F)
            card.Controls.Add(defectBadge)
        End If

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
                If defectBadge IsNot Nothing Then
                    defectBadge.Left = expChip.Left - defectBadge.Width - 10
                    defectBadge.Top = 20
                End If
                caret.Left = card.Width - caret.Width - rPad
                caret.Top = cardH - caret.Height - 18
                If lnkReceive IsNot Nothing Then
                    lnkReceive.Left = caret.Left - lnkReceive.Width - 16
                    lnkReceive.Top = cardH - lnkReceive.Height - 18
                End If
                Dim rightStop As Integer = If(defectBadge IsNot Nothing, defectBadge.Left, expChip.Left) - 20
                lblPo.MaximumSize = New Size(Math.Max(80, rightStop - 22), 30)
                lblMeta.MaximumSize = New Size(Math.Max(80, rightStop - 24), 26)
            End Sub

        Return card
    End Function

    Private Shared Function BuildItemsPanel(po As DeliveryPO) As Panel
        Dim items As List(Of DeliveryItem) = GetItems(po.PoId)
        Dim receipts As List(Of ReceiptEvent) = GetReceipts(po.PoId)

        ' The accountability strip sits ABOVE the line items: "who handled this"
        ' is the question the expansion is being opened to answer once goods
        ' have moved, and burying it under the product rows would hide it on
        ' any PO with more than a few lines.
        Dim custodyH As Integer = 0
        If receipts.Count > 0 Then custodyH = 34 + receipts.Count * 44 + 10

        Dim rowH As Integer = 76
        ' Each recorded problem adds a line under its product.
        Dim itemsH As Integer = 0
        For Each it In items
            itemsH += ItemRowHeight(it, rowH)
        Next
        Dim h As Integer = custodyH + 14 + Math.Max(rowH, itemsH) + 8
        Dim panel As New BufferedPanel With {
        .Height = h,
        .BackColor = Color.White
    }

        ' Border only — no background fill so rows aren't clipped
        AddHandler panel.Paint,
        Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, False)

        Dim custodyRows As List(Of Panel) = BuildCustodyRows(receipts)
        For Each cr In custodyRows
            panel.Controls.Add(cr)
        Next

        If items.Count = 0 Then
            panel.Controls.Add(New Label With {
            .Text = "No line items on this purchase order.",
            .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = SubText,
            .AutoSize = True,
            .Location = New Point(24, custodyH + 16),
            .BackColor = Color.Transparent
        })
            Return panel
        End If

        Dim y As Integer = custodyH + 8
        For Each it In items
            Dim itH As Integer = ItemRowHeight(it, rowH)
            Dim r As Panel = BuildItemRow(it, itH)
            r.Location = New Point(0, y)
            r.Width = panel.ClientSize.Width - 4
            r.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
            panel.Controls.Add(r)
            y += itH
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

    ' =========================================================================
    '  CUSTODY STRIP — who handled these goods
    '
    '  Renders the goods_receipts trail: a section heading, then one row per
    '  attested event. Returns positioned controls rather than a container so
    '  they share the items panel's resize handling and full-bleed dividers
    '  instead of introducing a second nested scroll region.
    ' =========================================================================
    Private Shared Function BuildCustodyRows(receipts As List(Of ReceiptEvent)) As List(Of Panel)
        Dim rows As New List(Of Panel)
        If receipts Is Nothing OrElse receipts.Count = 0 Then Return rows

        Dim header As New BufferedPanel With {
            .Height = 34,
            .Location = New Point(0, 0),
            .BackColor = Color.Transparent,
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        }
        header.Controls.Add(New Label With {
            .Text = "HANDLED BY",
            .Font = New Font("Segoe UI Semibold", 8.5F),
            .ForeColor = FaintText,
            .AutoSize = True,
            .Location = New Point(24, 14),
            .BackColor = Color.Transparent
        })
        rows.Add(header)

        Dim y As Integer = 34
        For Each ev In receipts
            rows.Add(BuildCustodyRow(ev, y, 44))
            y += 44
        Next

        Return rows
    End Function

    Private Shared Function BuildCustodyRow(ev As ReceiptEvent, top As Integer,
                                            height As Integer) As Panel
        Dim isPutAway As Boolean =
            String.Equals(ev.Phase, "putaway", StringComparison.OrdinalIgnoreCase)
        Dim hasIssue As Boolean =
            String.Equals(ev.ConditionNote, "Discrepancy", StringComparison.OrdinalIgnoreCase)

        Dim row As New BufferedPanel With {
            .Height = height,
            .Location = New Point(0, top),
            .BackColor = Color.Transparent,
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        }

        ' A hairline under every custody row, including the last: it separates
        ' the strip from the line items that follow it.
        AddHandler row.Paint,
            Sub(s2, e2)
                Using pen As New Pen(Color.FromArgb(228, 232, 230), 1)
                    e2.Graphics.DrawLine(pen, 24, height - 1, row.Width - 24, height - 1)
                End Using
            End Sub

        ' Phase badge, reusing the outline treatment the line-status badges use
        ' so the strip reads as part of the same list rather than a new widget.
        Dim phaseBadge As Label = OutlineBadge(
            If(isPutAway, "Put Away", "Received"),
            If(isPutAway, MintPrimary, PositiveClr))
        phaseBadge.Anchor = AnchorStyles.Top Or AnchorStyles.Left
        phaseBadge.Location = New Point(24, (height - phaseBadge.PreferredHeight) \ 2)
        row.Controls.Add(phaseBadge)

        Dim lblWho As New Label With {
            .Text = ev.OfficerName,
            .Font = New Font("Segoe UI Semibold", 10.5F),
            .ForeColor = Color.FromArgb(10, 79, 66),
            .AutoSize = True,
            .BackColor = Color.Transparent
        }
        row.Controls.Add(lblWho)

        Dim meta As String = ev.HappenedAt.ToString("dd MMM yyyy, h:mm tt")
        If ev.TotalUnits > 0 Then
            meta &= "   " & ChrW(183) & "   " & ev.TotalUnits &
                    If(ev.TotalUnits = 1, " unit", " units")
        End If
        Dim lblMeta As New Label With {
            .Text = meta,
            .Font = New Font("Segoe UI", 9.0F),
            .ForeColor = SubText,
            .AutoSize = True,
            .BackColor = Color.Transparent
        }
        row.Controls.Add(lblMeta)

        ' A discrepancy is the reason anyone comes looking at this strip, so it
        ' gets the right-hand slot the line rows use for status, with the
        ' remarks as the tooltip rather than a truncated second line.
        Dim issueBadge As Label = Nothing
        If hasIssue Then
            issueBadge = OutlineBadge("Discrepancy", WarningClr)
            If Not String.IsNullOrWhiteSpace(ev.Remarks) Then
                Dim tip As New ToolTip()
                tip.SetToolTip(issueBadge, ev.Remarks)
            End If
            row.Controls.Add(issueBadge)
        End If

        AddHandler row.Resize,
            Sub()
                lblWho.Left = phaseBadge.Right + 12
                lblWho.Top = (height - lblWho.Height) \ 2

                Dim rightStop As Integer = row.Width - 24
                If issueBadge IsNot Nothing Then
                    issueBadge.Left = row.Width - issueBadge.Width - 24
                    issueBadge.Top = (height - issueBadge.Height) \ 2
                    rightStop = issueBadge.Left - 12
                End If

                lblMeta.Left = Math.Max(lblWho.Right + 14, rightStop - lblMeta.Width)
                lblMeta.Top = (height - lblMeta.Height) \ 2
                lblWho.MaximumSize = New Size(Math.Max(80, lblMeta.Left - lblWho.Left - 14), 24)
            End Sub

        Return row
    End Function

    Private Shared Function ItemRowHeight(it As DeliveryItem, baseH As Integer) As Integer
        Return baseH + it.Issues.Count * 24
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

        ' One line per problem: what was wrong and what happens next.
        Dim iy As Integer = 70
        For Each iss In it.Issues
            Dim isAccepted As Boolean = (iss.Action = DeliveryIssues.ACTION_ACCEPT)
            Dim lblIssue As New Label With {
                .Text = ChrW(9888) & "  " & iss.Summary & "   " & ChrW(183) & "   " &
                        iss.RecordedAt.ToString("MMM d, yyyy"),
                .Font = New Font("Segoe UI Semibold", 9.5F),
                .ForeColor = If(isAccepted, WarningClr, NegativeClr),
                .AutoSize = True, .Location = New Point(24, iy),
                .BackColor = Color.Transparent, .UseMnemonic = False
            }
            row.Controls.Add(lblIssue)
            iy += 24
        Next

        Dim ls = LineStatus(it)
        Dim badge As Label = OutlineBadge(ls.text, ls.fore)
        row.Controls.Add(badge)

        AddHandler row.Resize,
        Sub()
            badge.Left = row.Width - badge.Width - 24
            badge.Top = (76 - badge.Height) \ 2
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
        ' Still short because units went back or never came: say so, rather
        ' than a plain "Partial" that hides why.
        If it.Received < it.OrderedQty Then
            If it.Issues.Any(Function(i) i.Action = DeliveryIssues.ACTION_RETURN) Then
                Return ("Return to Supplier", NegativeClr, Color.FromArgb(251, 234, 234))
            End If
            If it.Issues.Any(Function(i) i.IssueType = DeliveryIssues.TYPE_MISSING) Then
                Return ("Short " & (it.OrderedQty - it.Received) & " " & ChrW(8212) & " awaiting supplier",
                        WarningClr, Color.FromArgb(252, 240, 228))
            End If
        End If
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

        Dim counts As Dictionary(Of Integer, Integer) = DeliveryIssues.CountsByPo()
        For Each po In result
            Dim n As Integer = 0
            If counts.TryGetValue(po.PoId, n) Then po.IssueCount = n
        Next
        Return result
    End Function

    Private Shared Function GetItems(poId As Integer) As List(Of DeliveryItem)
        If itemsCache.ContainsKey(poId) Then Return itemsCache(poId)
        Dim result As New List(Of DeliveryItem)
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT pi.product_id, p.name AS pname, p.sku, pi.qty, " &
                    "COALESCE((SELECT SUM(sm.qty_change) FROM stock_movements sm " &
                    " WHERE sm.reference_id = @poid AND sm.reference_type = 'receive' " &
                    " AND sm.product_id = pi.product_id), 0) AS received " &
                    "FROM po_items pi JOIN products p ON pi.product_id = p.product_id " &
                    "WHERE pi.po_id = @poid ORDER BY p.name ASC", conn)
                    cmd.Parameters.AddWithValue("@poid", poId)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            result.Add(New DeliveryItem With {
                                .ProductId = SafeInt(rdr("product_id")),
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
        If result.Count > 0 Then
            Dim issues As List(Of DeliveryIssues.Issue) = DeliveryIssues.ForPo(poId)
            For Each it In result
                it.Issues = issues.Where(Function(i) i.ProductId = it.ProductId).ToList()
            Next
        End If
        itemsCache(poId) = result
        Return result
    End Function

    ' Attested handling events for a PO, oldest first so the strip reads as a
    ' chronology: accepted at the dock, then shelved (possibly in several trips).
    '
    ' goods_receipts and its phase column arrived in separate migrations, and
    ' both are optional — this screen predates them. Any failure here (missing
    ' table, missing column, no rights) yields an empty list and the strip
    ' simply does not render, leaving the item list exactly as it was before.
    Private Shared Function GetReceipts(poId As Integer) As List(Of ReceiptEvent)
        If receiptsCache.ContainsKey(poId) Then Return receiptsCache(poId)
        Dim result As New List(Of ReceiptEvent)
        Try
            Using conn = Db.GetConnection()
                conn.Open()

                ' Probed rather than assumed: selecting phase outright would
                ' throw on a database with the table but not the column, and
                ' turn a missing badge into a blank expansion.
                Dim hasPhase As Boolean = False
                Using cmd As New MySqlCommand(
                    "SELECT COUNT(*) FROM information_schema.columns " &
                    "WHERE table_schema = DATABASE() AND table_name = 'goods_receipts' " &
                    "  AND column_name = 'phase'", conn)
                    hasPhase = (Convert.ToInt32(cmd.ExecuteScalar()) > 0)
                End Using

                Using cmd As New MySqlCommand(
                    "SELECT " & If(hasPhase, "gr.phase", "'receive' AS phase") & ", " &
                    "gr.receiver_name, gr.condition_note, gr.remarks, " &
                    "gr.total_units, gr.received_at " &
                    "FROM goods_receipts gr WHERE gr.po_id = @poid " &
                    "ORDER BY gr.received_at ASC, gr.receipt_id ASC", conn)
                    cmd.Parameters.AddWithValue("@poid", poId)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            result.Add(New ReceiptEvent With {
                                .Phase = rdr("phase").ToString(),
                                .OfficerName = rdr("receiver_name").ToString(),
                                .ConditionNote = rdr("condition_note").ToString(),
                                .Remarks = If(IsDBNull(rdr("remarks")), "", rdr("remarks").ToString()),
                                .TotalUnits = SafeInt(rdr("total_units")),
                                .HappenedAt = Convert.ToDateTime(rdr("received_at"))
                            })
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            Debug.WriteLine("ProcurementDelivery GetReceipts error (PO " & poId & "): " & ex.Message)
            result.Clear()
        End Try
        receiptsCache(poId) = result
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