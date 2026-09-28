Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports MySql.Data.MySqlClient

' =============================================================================
'  ProcurementQuotationsPanelBuilder
'  Screen 4 — Quotations.
'
'  Before this screen existed, the only way to record a supplier quote was the
'  collapsible "Add Quote" panel buried inside the New PO form. That panel could
'  only ever create a Draft quote against a single approved request, and nothing
'  in the Procurement role could then list, send, accept, reject or convert it —
'  even though Admin ▸ Procurement Overview has always had a read-only
'  Quotations tab expecting exactly that lifecycle.
'
'  TWO INLINE VIEWS (swapped inside the same container):
'    1. List   — filterable list of quotations
'                (All / Draft / Sent / Accepted / Rejected / Expired).
'    2. Detail — one quotation's lines plus the lifecycle actions:
'                  Draft    → Mark as Sent  | Delete
'                  Sent     → Accept | Reject
'                  Accepted → Convert to PO (hands off to the PO builder)
'
'  STATUS MODEL (matches the live enum on quotations.status):
'    Draft, Sent, Accepted, Rejected, Expired
'  Accepting a quote rejects the sibling quotes on the same request, so a
'  request can only ever have one accepted quote to convert.
'
'  Shared-only, no instance state. Palette and helpers match
'  ProcurementPOPanelBuilder verbatim.
' =============================================================================
Public Class ProcurementQuotationsPanelBuilder

    ' ── PALETTE (exact spec values) ──────────────────────────────────────────────
    Private Shared ReadOnly MintDark As Color = Theme.Dark
    Private Shared ReadOnly MintPrimary As Color = Theme.Primary
    Private Shared ReadOnly MintDeep As Color = Theme.Deep
    Private Shared ReadOnly MintBg As Color = Theme.PageBg
    Private Shared ReadOnly CardBg As Color = Color.White
    Private Shared ReadOnly SubText As Color = Theme.SubText
    Private Shared ReadOnly FaintText As Color = Theme.FaintText
    Private Shared ReadOnly TileBg As Color = Theme.TileBg
    Private Shared ReadOnly RowDivider As Color = Theme.RowDivider
    Private Shared ReadOnly RowHover As Color = Theme.RowHover
    Private Shared ReadOnly NegativeClr As Color = Theme.Danger
    Private Shared ReadOnly WarningClr As Color = Theme.Warning
    Private Shared ReadOnly InputBorder As Color = Color.FromArgb(214, 224, 220)
    Private Shared ReadOnly MintHover As Color = Color.FromArgb(0, 150, 127)
    Private Shared ReadOnly DeepHover As Color = Color.FromArgb(0, 112, 94)
    Private Shared ReadOnly Peso As String = ChrW(8369)

    Private Shared ReadOnly BadgeGood As (fore As Color, back As Color) =
        (Color.FromArgb(0, 120, 92), Color.FromArgb(220, 248, 240))
    Private Shared ReadOnly BadgeWarn As (fore As Color, back As Color) =
        (Color.FromArgb(165, 110, 20), Color.FromArgb(255, 244, 224))
    Private Shared ReadOnly BadgeBad As (fore As Color, back As Color) =
        (Color.FromArgb(180, 40, 40), Color.FromArgb(255, 233, 233))
    Private Shared ReadOnly BadgeInfo As (fore As Color, back As Color) =
        (Color.FromArgb(30, 100, 160), Color.FromArgb(226, 240, 252))
    Private Shared ReadOnly BadgeMuted As (fore As Color, back As Color) =
        (Color.FromArgb(110, 125, 120), Color.FromArgb(238, 242, 240))

    Private Shared ReadOnly Filters() As String =
        {"All", "Draft", "Sent", "Accepted", "Rejected", "Expired"}

    ' ── DATA MODELS ──────────────────────────────────────────────────────────────
    Private Class QuoteRow
        Public QuotationId As Integer
        Public QuotationNo As String
        Public RequestId As Integer
        Public SupplierName As String
        Public CreatedBy As String
        Public CreatedAt As DateTime
        Public HasValidUntil As Boolean
        Public ValidUntil As DateTime
        Public Status As String
        Public Notes As String
        Public TotalAmount As Decimal
        Public ItemCount As Integer

        ' Total quotes on the same request, this one included. Anything above 1
        ' means there is a rival price worth comparing against.
        Public BidCount As Integer

        ' A quote past its valid_until is stale even if nobody has run the
        ' expiry sweep yet — surfaced in the UI so it reads honestly.
        Public ReadOnly Property IsStale As Boolean
            Get
                Return HasValidUntil AndAlso ValidUntil.Date < DateTime.Today AndAlso
                       (Status = "Draft" OrElse Status = "Sent")
            End Get
        End Property
    End Class

    Private Class QuoteLine
        Public ProductId As Integer
        Public ProductName As String
        Public Sku As String
        Public Qty As Integer
        Public UnitPrice As Decimal
        Public ReadOnly Property LineTotal As Decimal
            Get
                Return Math.Round(UnitPrice * Qty, 2)
            End Get
        End Property
    End Class

    ' One supplier's column in the comparison grid.
    Private Class CompareSupplier
        Public QuotationId As Integer
        Public QuotationNo As String
        Public SupplierId As Integer
        Public SupplierName As String
        Public Status As String
        Public TotalAmount As Decimal

        ' Unit price keyed by product. A supplier who did not quote a product
        ' is absent from this map rather than holding a zero, so "did not quote"
        ' never reads as "quoted free".
        Public Prices As New Dictionary(Of Integer, Decimal)

        ' What this supplier would cost for the products they DID quote, at the
        ' quantities the request asked for.
        Public CoveredTotal As Decimal
        Public CoveredCount As Integer
    End Class

    ' One product row in the comparison grid.
    Private Class CompareProduct
        Public ProductId As Integer
        Public ProductName As String
        Public Sku As String
        Public Qty As Integer
    End Class

    ' ── STATE ────────────────────────────────────────────────────────────────────
    Private Shared containerRef As Panel
    Private Shared currentFilter As String = "All"

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Shared Sub LoadQuotations(container As Panel)
        containerRef = container
        currentFilter = "All"
        RenderList()
    End Sub

    ' =========================================================================
    '  VIEW 1 — QUOTATION LIST
    ' =========================================================================
    Private Shared Sub RenderList()
        Dim container As Panel = containerRef
        If container Is Nothing Then Return

        ExpireStaleQuotes()
        Dim rows As List(Of QuoteRow) = LoadList(currentFilter)

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

        Dim topRegion As Panel = BuildTop(rows)
        topRegion.Dock = DockStyle.Top

        container.Controls.Add(listHost)
        container.Controls.Add(topRegion)

        ' Card wrapping header + rows
        Dim card As New BufferedPanel With {.Dock = DockStyle.Fill, .BackColor = CardBg}
        AddHandler card.Paint, Sub(s, e) PaintCard(CType(s, Panel), e)

        Dim header As Panel = BuildHeaderRow()
        header.Dock = DockStyle.Top

        Dim body As New Panel With {
            .Dock = DockStyle.Fill, .BackColor = Color.Transparent, .AutoScroll = True,
            .Padding = New Padding(0, 0, 0, 12)
        }

        If rows.Count = 0 Then
            body.Controls.Add(BuildEmptyState())
        Else
            ' Added bottom-first so DockStyle.Top stacking preserves list order.
            For i As Integer = rows.Count - 1 To 0 Step -1
                Dim r As QuoteRow = rows(i)
                Dim rowPanel As Panel = BuildRow(r)
                rowPanel.Dock = DockStyle.Top
                body.Controls.Add(rowPanel)
            Next
        End If

        card.Controls.Add(body)
        card.Controls.Add(header)
        rowsHost.Controls.Add(card)

        container.ResumeLayout()
        container.Refresh()
    End Sub

    Private Shared Function BuildTop(rows As List(Of QuoteRow)) As Panel
        Dim region As New Panel With {
            .Height = 172, .BackColor = MintBg, .Padding = New Padding(24, 22, 24, 0)
        }

        Const TITLE_Y As Integer = 18

        Dim lblTitle As New Label With {
            .Text = "Quotations", .Font = New Font("Segoe UI", 24, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(24, TITLE_Y),
            .BackColor = Color.Transparent
        }

        Dim pending As Integer = 0
        Dim accepted As Integer = 0
        Dim totalAmt As Decimal = 0D
        For Each r In rows
            If r.Status = "Sent" Then pending += 1
            If r.Status = "Accepted" Then accepted += 1
            totalAmt += r.TotalAmount
        Next

        Dim lblSub As New Label With {
            .Text = rows.Count & If(rows.Count = 1, " quotation", " quotations") &
                    "   " & ChrW(8226) & "   " & pending & " awaiting reply" &
                    "   " & ChrW(8226) & "   " & accepted & " accepted" &
                    "   " & ChrW(8226) & "   " & Peso & totalAmt.ToString("N2") & " quoted",
            .Font = New Font("Segoe UI", 11), .ForeColor = SubText,
            .AutoSize = True, .BackColor = Color.Transparent
        }
        ' A 24pt bold title measures ~44px tall, so the hard-coded y=58 used by
        ' the other procurement screens sits inside its descenders. Measure both
        ' labels and stack them, rather than trusting AutoSize before parenting.
        Dim titleH As Integer = TextRenderer.MeasureText(lblTitle.Text, lblTitle.Font).Height
        Dim subH As Integer = TextRenderer.MeasureText(lblSub.Text, lblSub.Font).Height
        Dim subY As Integer = TITLE_Y + titleH + 8
        lblSub.Location = New Point(26, subY)

        Dim btnRefresh As New Button With {
            .Text = ChrW(8635) & "  Refresh", .FlatStyle = FlatStyle.Flat,
            .Cursor = Cursors.Hand, .Size = New Size(104, 34),
            .Font = New Font("Segoe UI", 9.5F),
            .BackColor = Color.FromArgb(220, 235, 232), .ForeColor = SubText,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        btnRefresh.FlatAppearance.BorderSize = 0
        RoundControl(btnRefresh, 10)
        AddHandler btnRefresh.Click, Sub() RenderList()

        ' Filter pills
        Dim pillHost As New Panel With {
            .Height = 44, .BackColor = Color.Transparent,
            .Location = New Point(24, subY + subH + 16)
        }
        Dim px As Integer = 0
        For Each f In Filters
            Dim fname As String = f
            Dim isOn As Boolean = (currentFilter = fname)
            Dim pill As New Button With {
                .Text = fname, .FlatStyle = FlatStyle.Flat, .AutoSize = False,
                .Height = 34, .Cursor = Cursors.Hand,
                .Font = New Font("Segoe UI Semibold", 9.5F),
                .BackColor = If(isOn, MintDeep, Color.White),
                .ForeColor = If(isOn, Color.White, SubText),
                .Left = px, .Top = 4
            }
            pill.Width = TextRenderer.MeasureText(fname, pill.Font).Width + 34
            pill.FlatAppearance.BorderSize = 0
            RoundControl(pill, 10)
            If Not isOn Then AddHover(pill, Color.White, RowHover)
            AddHandler pill.Click,
                Sub()
                    currentFilter = fname
                    RenderList()
                End Sub
            pillHost.Controls.Add(pill)
            px += pill.Width + 8
        Next
        pillHost.Width = px

        ' Size the whole header to its content so the pills never crowd the card.
        region.Height = pillHost.Top + pillHost.Height + 10

        region.Controls.AddRange({lblTitle, lblSub, btnRefresh, pillHost})

        AddHandler region.Resize,
            Sub()
                btnRefresh.Left = region.Width - btnRefresh.Width - 34
                btnRefresh.Top = 24
            End Sub

        Return region
    End Function

    ' Column layout shared by the header and every row, so they never drift.
    Private Const COL_PAD As Integer = 22
    Private Const COL_SUPPLIER As Integer = 260
    Private Const COL_DATES As Integer = 200
    Private Const COL_STATUS As Integer = 120
    Private Const COL_TOTAL As Integer = 130
    Private Const COL_ACTION As Integer = 110

    Private Shared Function BuildHeaderRow() As Panel
        Dim head As New Panel With {.Height = 46, .BackColor = Color.Transparent}
        Dim texts() As String =
            {"QUOTATION / REQUEST", "SUPPLIER", "CREATED / VALID UNTIL", "STATUS", "TOTAL", ""}

        Dim labels As New List(Of Label)
        For Each t In texts
            Dim l As New Label With {
                .Text = t, .Font = New Font("Segoe UI Semibold", 8.5F),
                .ForeColor = FaintText, .AutoSize = False, .Height = 16,
                .TextAlign = ContentAlignment.MiddleLeft, .BackColor = Color.Transparent
            }
            labels.Add(l)
            head.Controls.Add(l)
        Next

        Dim div As New Panel With {.Height = 1, .BackColor = RowDivider, .Dock = DockStyle.Bottom}
        head.Controls.Add(div)

        AddHandler head.Resize,
            Sub()
                Dim xs() As Integer = ColumnXs(head.Width)
                For i As Integer = 0 To labels.Count - 1
                    labels(i).SetBounds(xs(i), 24, ColumnW(i, head.Width), 16)
                Next
            End Sub

        Return head
    End Function

    ' Left edge of each of the six columns, for a given card width.
    Private Shared Function ColumnXs(w As Integer) As Integer()
        Dim xs(5) As Integer
        xs(0) = COL_PAD
        xs(1) = xs(0) + ColumnW(0, w) + 12
        xs(2) = xs(1) + COL_SUPPLIER + 12
        xs(3) = xs(2) + COL_DATES + 12
        xs(4) = xs(3) + COL_STATUS + 12
        xs(5) = xs(4) + COL_TOTAL + 12
        Return xs
    End Function

    Private Shared Function ColumnW(index As Integer, w As Integer) As Integer
        Select Case index
            Case 0
                ' First column flexes with the card; the rest are fixed.
                Dim fixedW As Integer = COL_SUPPLIER + COL_DATES + COL_STATUS +
                                        COL_TOTAL + COL_ACTION + 12 * 5 + COL_PAD * 2
                Dim flex As Integer = w - fixedW
                If flex < 180 Then flex = 180
                Return flex
            Case 1 : Return COL_SUPPLIER
            Case 2 : Return COL_DATES
            Case 3 : Return COL_STATUS
            Case 4 : Return COL_TOTAL
            Case Else : Return COL_ACTION
        End Select
    End Function

    Private Shared Function BuildRow(r As QuoteRow) As Panel
        Const ROW_H As Integer = 66
        Dim row As New BufferedPanel With {.Height = ROW_H, .BackColor = CardBg}

        Dim lblNo As New Label With {
            .Text = r.QuotationNo, .Font = New Font("Segoe UI Semibold", 10.5F),
            .ForeColor = MintDark, .AutoSize = False, .Height = 20,
            .TextAlign = ContentAlignment.MiddleLeft, .BackColor = Color.Transparent
        }
        ' A request carrying rival bids says so on the row, so a contested price
        ' is visible in the list rather than only after opening the quote.
        Dim reqText As String = "Request #" & r.RequestId & "   " & ChrW(8226) & "   " &
                                r.ItemCount & If(r.ItemCount = 1, " item", " items")
        If r.BidCount > 1 Then
            reqText &= "   " & ChrW(8226) & "   " & r.BidCount & " suppliers bidding"
        End If

        Dim lblReq As New Label With {
            .Text = reqText,
            .Font = New Font("Segoe UI", 9),
            .ForeColor = If(r.BidCount > 1, MintPrimary, FaintText),
            .AutoSize = False, .Height = 18,
            .TextAlign = ContentAlignment.MiddleLeft, .BackColor = Color.Transparent
        }

        Dim lblSup As New Label With {
            .Text = r.SupplierName, .Font = New Font("Segoe UI", 10),
            .ForeColor = MintDark, .AutoSize = False, .Height = 20,
            .TextAlign = ContentAlignment.MiddleLeft, .BackColor = Color.Transparent
        }
        Dim lblBy As New Label With {
            .Text = "by " & r.CreatedBy, .Font = New Font("Segoe UI", 9),
            .ForeColor = FaintText, .AutoSize = False, .Height = 18,
            .TextAlign = ContentAlignment.MiddleLeft, .BackColor = Color.Transparent
        }

        Dim lblCreated As New Label With {
            .Text = r.CreatedAt.ToString("MMM d, yyyy"), .Font = New Font("Segoe UI", 10),
            .ForeColor = MintDark, .AutoSize = False, .Height = 20,
            .TextAlign = ContentAlignment.MiddleLeft, .BackColor = Color.Transparent
        }
        Dim validTxt As String = If(r.HasValidUntil,
                                    "valid until " & r.ValidUntil.ToString("MMM d, yyyy"),
                                    "no expiry")
        Dim lblValid As New Label With {
            .Text = If(r.IsStale, validTxt & "  (lapsed)", validTxt),
            .Font = New Font("Segoe UI", 9),
            .ForeColor = If(r.IsStale, NegativeClr, FaintText),
            .AutoSize = False, .Height = 18,
            .TextAlign = ContentAlignment.MiddleLeft, .BackColor = Color.Transparent
        }

        Dim badge As Panel = MakeBadge(r.Status, StatusBadge(r.Status))

        Dim lblTotal As New Label With {
            .Text = Peso & r.TotalAmount.ToString("N2"),
            .Font = New Font("Segoe UI Semibold", 11), .ForeColor = MintDark,
            .AutoSize = False, .Height = 20,
            .TextAlign = ContentAlignment.MiddleRight, .BackColor = Color.Transparent
        }

        Dim lnkView As Label = MakeLink("View", MintDeep)

        Dim div As New Panel With {.Height = 1, .BackColor = RowDivider, .Dock = DockStyle.Bottom}

        row.Controls.AddRange({lblNo, lblReq, lblSup, lblBy, lblCreated, lblValid,
                               badge, lblTotal, lnkView, div})

        AddHandler row.Resize,
            Sub()
                Dim w As Integer = row.Width
                Dim xs() As Integer = ColumnXs(w)
                Dim w0 As Integer = ColumnW(0, w)

                lblNo.SetBounds(xs(0), 14, w0, 20)
                lblReq.SetBounds(xs(0), 34, w0, 18)
                lblSup.SetBounds(xs(1), 14, COL_SUPPLIER, 20)
                lblBy.SetBounds(xs(1), 34, COL_SUPPLIER, 18)
                lblCreated.SetBounds(xs(2), 14, COL_DATES, 20)
                lblValid.SetBounds(xs(2), 34, COL_DATES, 18)
                badge.Location = New Point(xs(3), (ROW_H - badge.Height) \ 2)
                lblTotal.SetBounds(xs(4), (ROW_H - 20) \ 2, COL_TOTAL, 20)
                lnkView.Location = New Point(xs(5), (ROW_H - lnkView.Height) \ 2)
            End Sub

        ' Whole row is clickable, matching the PO list.
        Dim openDetail As EventHandler = Sub(s, e) RenderDetail(r.QuotationId)
        AddHandler row.Click, openDetail
        AddHandler lnkView.Click, openDetail
        For Each c As Control In row.Controls
            If c IsNot div Then AddHandler c.Click, openDetail
        Next

        ' Hover tint across the row and its children.
        Dim setBg As Action(Of Color) =
            Sub(clr)
                row.BackColor = clr
                badge.Parent.BackColor = clr
            End Sub
        AddHandler row.MouseEnter, Sub() setBg(RowHover)
        AddHandler row.MouseLeave, Sub() setBg(CardBg)
        For Each c As Control In row.Controls
            If c Is div Then Continue For
            AddHandler c.MouseEnter, Sub() setBg(RowHover)
            AddHandler c.MouseLeave, Sub() setBg(CardBg)
        Next

        row.Cursor = Cursors.Hand
        Return row
    End Function

    Private Shared Function BuildEmptyState() As Panel
        Dim host As New Panel With {.Height = 220, .Dock = DockStyle.Top, .BackColor = Color.Transparent}
        Dim lbl As New Label With {
            .Text = If(currentFilter = "All",
                       "No quotations yet." & Environment.NewLine &
                       "Record a supplier's price from Purchase Orders ▸ New PO ▸ Add Quote.",
                       "No " & currentFilter.ToLower() & " quotations."),
            .Font = New Font("Segoe UI", 11), .ForeColor = FaintText,
            .AutoSize = False, .Dock = DockStyle.Fill,
            .TextAlign = ContentAlignment.MiddleCenter, .BackColor = Color.Transparent
        }
        host.Controls.Add(lbl)
        Return host
    End Function

    ' =========================================================================
    '  VIEW 2 — QUOTATION DETAIL
    ' =========================================================================
    Private Shared Sub RenderDetail(quotationId As Integer)
        Dim container As Panel = containerRef
        If container Is Nothing Then Return

        Dim q As QuoteRow = LoadOne(quotationId)
        If q Is Nothing Then
            AppModal.Show("That quotation could no longer be found.", "Not Found",
                          MessageBoxButtons.OK, MessageBoxIcon.Warning)
            RenderList()
            Return
        End If
        Dim lines As List(Of QuoteLine) = LoadLines(quotationId)

        container.SuspendLayout()
        container.Controls.Clear()
        container.BackColor = MintBg

        Dim scrollHost As New Panel With {
            .Dock = DockStyle.Fill, .BackColor = MintBg,
            .AutoScroll = True, .Padding = New Padding(24, 4, 24, 24)
        }

        ' ── Header region ────────────────────────────────────────────────────
        Dim topRegion As New Panel With {
            .Height = 104, .BackColor = MintBg, .Padding = New Padding(24, 18, 24, 0),
            .Dock = DockStyle.Top
        }
        Dim lnkBack As Label = MakeLink(ChrW(8592) & "  Back to quotations", MintDeep)
        lnkBack.Location = New Point(26, 16)
        AddHandler lnkBack.Click, Sub() RenderList()

        Dim lblTitle As New Label With {
            .Text = q.QuotationNo, .Font = New Font("Segoe UI", 22, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(24, 44),
            .BackColor = Color.Transparent
        }
        Dim badge As Panel = MakeBadge(q.Status, StatusBadge(q.Status))
        badge.Location = New Point(0, 0)   ' positioned in Resize

        topRegion.Controls.AddRange({lnkBack, lblTitle, badge})
        AddHandler topRegion.Resize,
            Sub()
                badge.Location = New Point(lblTitle.Right + 14,
                                           lblTitle.Top + (lblTitle.Height - badge.Height) \ 2)
            End Sub

        ' ── Summary card ─────────────────────────────────────────────────────
        Dim sumCard As New BufferedPanel With {.Height = 132, .Dock = DockStyle.Top, .BackColor = CardBg}
        AddHandler sumCard.Paint, Sub(s, e) PaintCard(CType(s, Panel), e)

        Dim facts As New List(Of (label As String, value As String, clr As Color))
        facts.Add(("SUPPLIER", q.SupplierName, MintDark))
        facts.Add(("SOURCE REQUEST", "#" & q.RequestId, MintDark))
        facts.Add(("CREATED", q.CreatedAt.ToString("MMM d, yyyy") & "  by " & q.CreatedBy, MintDark))
        facts.Add(("VALID UNTIL",
                   If(q.HasValidUntil, q.ValidUntil.ToString("MMM d, yyyy"), "no expiry"),
                   If(q.IsStale, NegativeClr, MintDark)))

        Dim factLabels As New List(Of Label)
        Dim factValues As New List(Of Label)
        For Each f In facts
            Dim l As New Label With {
                .Text = f.label, .Font = New Font("Segoe UI Semibold", 8.5F),
                .ForeColor = FaintText, .AutoSize = False, .Height = 16,
                .TextAlign = ContentAlignment.MiddleLeft, .BackColor = Color.Transparent
            }
            Dim v As New Label With {
                .Text = f.value, .Font = New Font("Segoe UI Semibold", 11.5F),
                .ForeColor = f.clr, .AutoSize = False, .Height = 24,
                .TextAlign = ContentAlignment.MiddleLeft, .BackColor = Color.Transparent
            }
            factLabels.Add(l) : factValues.Add(v)
            sumCard.Controls.Add(l) : sumCard.Controls.Add(v)
        Next

        Dim lblNotes As New Label With {
            .Text = If(String.IsNullOrWhiteSpace(q.Notes), "", "Notes:  " & q.Notes),
            .Font = New Font("Segoe UI", 9.5F), .ForeColor = SubText,
            .AutoSize = False, .Height = 20,
            .TextAlign = ContentAlignment.MiddleLeft, .BackColor = Color.Transparent
        }
        sumCard.Controls.Add(lblNotes)

        AddHandler sumCard.Resize,
            Sub()
                Dim pad As Integer = 24
                Dim avail As Integer = sumCard.Width - pad * 2
                Dim colW As Integer = avail \ Math.Max(facts.Count, 1)
                For i As Integer = 0 To facts.Count - 1
                    Dim x As Integer = pad + colW * i
                    factLabels(i).SetBounds(x, 26, colW - 12, 16)
                    factValues(i).SetBounds(x, 46, colW - 12, 24)
                Next
                lblNotes.SetBounds(pad, 88, avail, 20)
            End Sub

        Dim spacer1 As New Panel With {.Height = 14, .Dock = DockStyle.Top, .BackColor = Color.Transparent}

        ' ── Lines card ───────────────────────────────────────────────────────
        Dim linesCard As New BufferedPanel With {
            .Height = 92 + Math.Max(lines.Count, 1) * 56 + 62,
            .Dock = DockStyle.Top, .BackColor = CardBg
        }
        AddHandler linesCard.Paint, Sub(s, e) PaintCard(CType(s, Panel), e)

        Dim lblLines As New Label With {
            .Text = "Quoted Items", .Font = New Font("Segoe UI", 15, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(22, 18),
            .BackColor = Color.Transparent
        }
        linesCard.Controls.Add(lblLines)

        Dim hdrDiv As New Panel With {.Height = 1, .BackColor = RowDivider, .Location = New Point(20, 60)}
        linesCard.Controls.Add(hdrDiv)

        Dim lineRows As New List(Of Panel)
        Dim y As Integer = 70
        For Each ln In lines
            Dim rowP As Panel = BuildLineRow(ln)
            rowP.Top = y
            lineRows.Add(rowP)
            linesCard.Controls.Add(rowP)
            y += 56
        Next

        If lines.Count = 0 Then
            Dim lblNone As New Label With {
                .Text = "This quotation has no line items.",
                .Font = New Font("Segoe UI", 10.5F), .ForeColor = FaintText,
                .AutoSize = False, .Height = 40, .Location = New Point(22, 76),
                .TextAlign = ContentAlignment.MiddleLeft, .BackColor = Color.Transparent
            }
            linesCard.Controls.Add(lblNone)
        End If

        ' Grand total strip
        Dim totalDiv As New Panel With {.Height = 1, .BackColor = RowDivider}
        Dim lblGrandCap As New Label With {
            .Text = "QUOTED TOTAL", .Font = New Font("Segoe UI Semibold", 9),
            .ForeColor = FaintText, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblGrand As New Label With {
            .Text = Peso & q.TotalAmount.ToString("N2"),
            .Font = New Font("Segoe UI", 17, FontStyle.Bold), .ForeColor = MintDark,
            .AutoSize = True, .BackColor = Color.Transparent
        }
        linesCard.Controls.AddRange({totalDiv, lblGrandCap, lblGrand})

        AddHandler linesCard.Resize,
            Sub()
                Dim w As Integer = linesCard.Width
                hdrDiv.Width = w - 40
                For Each rp In lineRows
                    rp.SetBounds(20, rp.Top, w - 40, 56)
                Next
                Dim ty As Integer = 70 + Math.Max(lines.Count, 1) * 56 + 6
                totalDiv.SetBounds(20, ty, w - 40, 1)
                lblGrandCap.Location = New Point(w - 24 - lblGrand.Width - 16 - lblGrandCap.Width,
                                                 ty + 22)
                lblGrand.Location = New Point(w - 24 - lblGrand.Width, ty + 14)
            End Sub

        Dim spacer2 As New Panel With {.Height = 14, .Dock = DockStyle.Top, .BackColor = Color.Transparent}

        ' ── Actions card ─────────────────────────────────────────────────────
        Dim actionCard As Panel = BuildActionCard(q)
        actionCard.Dock = DockStyle.Top

        ' Docked top-first means reverse insertion order.
        scrollHost.Controls.Add(actionCard)
        scrollHost.Controls.Add(spacer2)
        scrollHost.Controls.Add(linesCard)
        scrollHost.Controls.Add(spacer1)
        scrollHost.Controls.Add(sumCard)

        container.Controls.Add(scrollHost)
        container.Controls.Add(topRegion)
        container.ResumeLayout()
        container.Refresh()
    End Sub

    Private Shared Function BuildLineRow(ln As QuoteLine) As Panel
        Dim row As New Panel With {.Height = 56, .BackColor = Color.Transparent}

        Dim lblName As New Label With {
            .Text = ln.ProductName, .Font = New Font("Segoe UI Semibold", 10.5F),
            .ForeColor = MintDark, .AutoSize = False, .Height = 20,
            .TextAlign = ContentAlignment.MiddleLeft, .BackColor = Color.Transparent
        }
        Dim lblSku As New Label With {
            .Text = ln.Sku, .Font = New Font("Segoe UI", 9), .ForeColor = FaintText,
            .AutoSize = False, .Height = 18,
            .TextAlign = ContentAlignment.MiddleLeft, .BackColor = Color.Transparent
        }
        Dim lblQty As New Label With {
            .Text = ln.Qty & " " & ChrW(215) & " " & Peso & ln.UnitPrice.ToString("N2"),
            .Font = New Font("Segoe UI", 10), .ForeColor = SubText,
            .AutoSize = False, .Height = 20,
            .TextAlign = ContentAlignment.MiddleRight, .BackColor = Color.Transparent
        }
        Dim lblTot As New Label With {
            .Text = Peso & ln.LineTotal.ToString("N2"),
            .Font = New Font("Segoe UI Semibold", 11), .ForeColor = MintDark,
            .AutoSize = False, .Height = 20,
            .TextAlign = ContentAlignment.MiddleRight, .BackColor = Color.Transparent
        }
        Dim div As New Panel With {.Height = 1, .BackColor = RowDivider, .Dock = DockStyle.Bottom}

        row.Controls.AddRange({lblName, lblSku, lblQty, lblTot, div})

        AddHandler row.Resize,
            Sub()
                Dim w As Integer = row.Width
                Dim totW As Integer = 130
                Dim qtyW As Integer = 190
                Dim nameW As Integer = w - totW - qtyW - 24
                If nameW < 120 Then nameW = 120
                lblName.SetBounds(4, 8, nameW, 20)
                lblSku.SetBounds(4, 28, nameW, 18)
                lblQty.SetBounds(4 + nameW, 18, qtyW, 20)
                lblTot.SetBounds(w - totW - 4, 18, totW, 20)
            End Sub

        Return row
    End Function

    ' The lifecycle actions available depend entirely on current status.
    Private Shared Function BuildActionCard(q As QuoteRow) As Panel
        Dim card As New BufferedPanel With {.Height = 108, .BackColor = CardBg}
        AddHandler card.Paint, Sub(s, e) PaintCard(CType(s, Panel), e)

        Dim lblHead As New Label With {
            .Text = "Actions", .Font = New Font("Segoe UI", 13, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(22, 16),
            .BackColor = Color.Transparent
        }
        Dim lblHint As New Label With {
            .Text = ActionHint(q), .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = SubText, .AutoSize = True, .Location = New Point(22, 42),
            .BackColor = Color.Transparent
        }
        card.Controls.AddRange({lblHead, lblHint})

        Dim buttons As New List(Of Button)

        ' Comparing is a read-only view, so it stays available at every status —
        ' including Rejected and Expired, where seeing why this bid lost is the
        ' whole point. Offered only when a rival bid exists to compare against.
        If CountQuotesOnRequest(q.RequestId) > 1 Then
            buttons.Add(MakeActionButton("Compare Suppliers", MintPrimary, MintHover,
                Sub() RenderCompare(q.RequestId, q.QuotationId)))
        End If

        Select Case q.Status
            Case "Draft"
                buttons.Add(MakeActionButton("Mark as Sent", MintDeep, DeepHover,
                    Sub() SetStatus(q, "Sent",
                                    "Mark " & q.QuotationNo & " as sent to " & q.SupplierName & "?")))
                buttons.Add(MakeActionButton("Delete", NegativeClr, Color.FromArgb(180, 40, 40),
                    Sub() DeleteQuote(q)))

            Case "Sent"
                buttons.Add(MakeActionButton("Accept", MintDeep, DeepHover,
                    Sub() AcceptQuote(q)))
                buttons.Add(MakeActionButton("Reject", NegativeClr, Color.FromArgb(180, 40, 40),
                    Sub() SetStatus(q, "Rejected",
                                    "Reject " & q.QuotationNo & " from " & q.SupplierName & "?")))

            Case "Accepted"
                buttons.Add(MakeActionButton("Convert to PO", MintDeep, DeepHover,
                    Sub() ConvertToPO(q)))

            Case Else
                ' Rejected / Expired are terminal — nothing to do but go back.
        End Select

        For Each b In buttons : card.Controls.Add(b) : Next

        AddHandler card.Resize,
            Sub()
                Dim x As Integer = card.Width - 22
                For i As Integer = buttons.Count - 1 To 0 Step -1
                    x -= buttons(i).Width
                    buttons(i).Location = New Point(x, 34)
                    x -= 12
                Next
            End Sub

        Return card
    End Function

    Private Shared Function ActionHint(q As QuoteRow) As String
        Select Case q.Status
            Case "Draft" : Return "Send this quotation to the supplier once the price is confirmed."
            Case "Sent" : Return "Waiting on the supplier. Accept to lock in this price, or reject it."
            Case "Accepted" : Return "Turn this accepted quotation into a draft purchase order."
            Case "Rejected" : Return "This quotation was rejected. No further action is available."
            Case "Expired" : Return "This quotation lapsed past its valid-until date."
            Case Else : Return ""
        End Select
    End Function

    Private Shared Function MakeActionButton(text As String, normal As Color, hover As Color,
                                             onClick As Action) As Button
        Dim b As New Button With {
            .Text = text, .Height = 42, .FlatStyle = FlatStyle.Flat,
            .BackColor = normal, .ForeColor = Color.White,
            .Font = New Font("Segoe UI Semibold", 10.5F), .Cursor = Cursors.Hand,
            .TextAlign = ContentAlignment.MiddleCenter
        }
        b.Width = Math.Max(TextRenderer.MeasureText(text, b.Font).Width + 44, 132)
        b.FlatAppearance.BorderSize = 0
        RoundControl(b, 10)
        AddHover(b, normal, hover)
        AddHandler b.Click, Sub() onClick()
        Return b
    End Function

    ' =========================================================================
    '  ACTIONS
    ' =========================================================================

    ' Plain status flip used by Sent / Rejected.
    Private Shared Sub SetStatus(q As QuoteRow, newStatus As String, confirmMsg As String)
        If AppModal.Show(confirmMsg, "Confirm",
                         MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then
            Return
        End If

        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "UPDATE quotations SET status = @st WHERE quotation_id = @id", conn)
                    cmd.Parameters.AddWithValue("@st", newStatus)
                    cmd.Parameters.AddWithValue("@id", q.QuotationId)
                    cmd.ExecuteNonQuery()
                End Using
            End Using

            ActivityLog.Record("QUOTE_" & newStatus.ToUpper(), "quotations", q.QuotationId,
                               q.QuotationNo)
            RenderDetail(q.QuotationId)

        Catch ex As Exception
            AppModal.Show("Could not update the quotation:" & Environment.NewLine & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    ' Accepting one quote rejects every sibling quote on the same request, so a
    ' request can never carry two accepted prices into a PO.
    Private Shared Sub AcceptQuote(q As QuoteRow)
        Dim others As Integer = CountSiblingQuotes(q)
        Dim msg As String = "Accept " & q.QuotationNo & " from " & q.SupplierName &
                            " at " & Peso & q.TotalAmount.ToString("N2") & "?"
        If others > 0 Then
            msg &= Environment.NewLine & Environment.NewLine &
                   others & If(others = 1, " other quotation", " other quotations") &
                   " on request #" & q.RequestId & " will be rejected."
        End If

        If AppModal.Show(msg, "Accept Quotation",
                         MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then
            Return
        End If

        Dim rejected As Integer = 0
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using tx = conn.BeginTransaction()
                    Using cmd As New MySqlCommand(
                        "UPDATE quotations SET status = 'Accepted' WHERE quotation_id = @id", conn, tx)
                        cmd.Parameters.AddWithValue("@id", q.QuotationId)
                        cmd.ExecuteNonQuery()
                    End Using

                    Using cmd As New MySqlCommand(
                        "UPDATE quotations SET status = 'Rejected' " &
                        "WHERE request_id = @rid AND quotation_id <> @id " &
                        "AND status IN ('Draft','Sent')", conn, tx)
                        cmd.Parameters.AddWithValue("@rid", q.RequestId)
                        cmd.Parameters.AddWithValue("@id", q.QuotationId)
                        rejected = cmd.ExecuteNonQuery()
                    End Using

                    tx.Commit()
                End Using
            End Using

            ' Accepting one bid silently rejects the others. Choosing between
            ' suppliers is exactly the decision an audit reviews, so the losing
            ' count is recorded rather than left implicit.
            ActivityLog.Record("QUOTE_ACCEPTED", "quotations", q.QuotationId,
                               q.QuotationNo &
                               If(rejected > 0,
                                  " — " & rejected.ToString() &
                                  If(rejected = 1, " competing quote rejected",
                                                   " competing quotes rejected"),
                                  ""))
            RenderDetail(q.QuotationId)

        Catch ex As Exception
            AppModal.Show("Could not accept the quotation:" & Environment.NewLine & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    Private Shared Sub DeleteQuote(q As QuoteRow)
        If AppModal.Show("Delete draft quotation " & q.QuotationNo & "?" & Environment.NewLine &
                         If(Archive.HasArchiveTable(), "An admin can restore it from the Recycle Bin.",
                            "This cannot be undone."), "Delete Quotation",
                         MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then
            Return
        End If

        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using tx = conn.BeginTransaction()
                    ' quotation_items cascades on delete, but the source request
                    ' must be handed back so it can be quoted again.
                    Archive.Capture(conn, tx, "quotations", "quotation_id", q.QuotationId, q.QuotationNo,
                                    children:={("quotation_items", "quotation_id")})
                    Using cmd As New MySqlCommand(
                        "DELETE FROM quotations WHERE quotation_id = @id AND status = 'Draft'", conn, tx)
                        cmd.Parameters.AddWithValue("@id", q.QuotationId)
                        cmd.ExecuteNonQuery()
                    End Using

                    ' Only hand the request back if this was its last quote.
                    Dim remaining As Integer
                    Using cmd As New MySqlCommand(
                        "SELECT COUNT(*) FROM quotations WHERE request_id = @rid", conn, tx)
                        cmd.Parameters.AddWithValue("@rid", q.RequestId)
                        remaining = Convert.ToInt32(cmd.ExecuteScalar())
                    End Using

                    If remaining = 0 Then
                        Using cmd As New MySqlCommand(
                            "UPDATE purchase_requests SET status = 'Approved' " &
                            "WHERE request_id = @rid AND status = 'Quoted'", conn, tx)
                            cmd.Parameters.AddWithValue("@rid", q.RequestId)
                            cmd.ExecuteNonQuery()
                        End Using
                    End If

                    tx.Commit()
                End Using
            End Using

            ActivityLog.Record("QUOTE_DELETE", "quotations", q.QuotationId, q.QuotationNo)
            RenderList()

        Catch ex As Exception
            AppModal.Show("Could not delete the quotation:" & Environment.NewLine & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    ' Hands the accepted quote to the PO builder, which pre-fills a draft PO.
    Private Shared Sub ConvertToPO(q As QuoteRow)
        Dim frm As PROCUREMENT_STAFF = TryCast(containerRef.FindForm(), PROCUREMENT_STAFF)
        If frm Is Nothing Then
            AppModal.Show("Could not open Purchase Orders from here.", "Navigation",
                          MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If
        frm.OpenPOFromQuotation(q.QuotationId)
    End Sub

    ' =========================================================================
    '  VIEW 3 — SUPPLIER PRICE COMPARISON
    '
    '  Products down the side, suppliers across the top, one unit price per
    '  cell. The cheapest live bid on each row is highlighted, so "who is
    '  cheaper on this item" is answered by looking, not by opening each quote
    '  in turn and remembering the numbers.
    '
    '  Two different questions get two different answers, and the screen says
    '  both rather than pretending they are the same:
    '    • cheapest PER ITEM   — the highlighted cell on each row
    '    • cheapest OVERALL    — the supplier with the lowest covered total
    '  A purchase order goes to ONE supplier, so the recommendation names the
    '  overall winner while the per-item highlights show what that choice costs
    '  on the rows where someone else was cheaper.
    ' =========================================================================
    Private Shared Sub RenderCompare(requestId As Integer, Optional fromQuotationId As Integer = 0)
        Dim container As Panel = containerRef
        If container Is Nothing Then Return

        Dim suppliers As List(Of CompareSupplier) = LoadCompareSuppliers(requestId)
        Dim products As List(Of CompareProduct) = LoadCompareProducts(requestId)
        ComputeCoveredTotals(suppliers, products)

        container.SuspendLayout()
        container.Controls.Clear()
        container.BackColor = MintBg

        Dim scrollHost As New Panel With {
            .Dock = DockStyle.Fill, .BackColor = MintBg,
            .AutoScroll = True, .Padding = New Padding(24, 4, 24, 24)
        }

        ' ── Header ──
        Dim topRegion As New Panel With {
            .Height = 104, .BackColor = MintBg, .Padding = New Padding(24, 18, 24, 0),
            .Dock = DockStyle.Top
        }
        Dim backText As String = If(fromQuotationId > 0,
                                    ChrW(8592) & "  Back to quotation",
                                    ChrW(8592) & "  Back to quotations")
        Dim lnkBack As Label = MakeLink(backText, MintDeep)
        lnkBack.Location = New Point(26, 16)
        AddHandler lnkBack.Click,
            Sub()
                If fromQuotationId > 0 Then
                    RenderDetail(fromQuotationId)
                Else
                    RenderList()
                End If
            End Sub

        Dim lblTitle As New Label With {
            .Text = "Compare Suppliers", .Font = New Font("Segoe UI", 22, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(24, 44),
            .BackColor = Color.Transparent
        }
        Dim lblReq As New Label With {
            .Text = "Request #" & requestId, .Font = New Font("Segoe UI", 11),
            .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent
        }
        topRegion.Controls.AddRange({lnkBack, lblTitle, lblReq})
        AddHandler topRegion.Resize,
            Sub()
                lblReq.Location = New Point(lblTitle.Right + 14,
                                            lblTitle.Top + (lblTitle.Height - lblReq.Height) \ 2)
            End Sub

        container.Controls.Add(scrollHost)
        container.Controls.Add(topRegion)

        ' Nothing to compare against — say so plainly instead of drawing an
        ' empty grid that looks broken.
        If suppliers.Count = 0 OrElse products.Count = 0 Then
            Dim emptyCard As New BufferedPanel With {
                .Height = 200, .Dock = DockStyle.Top, .BackColor = CardBg
            }
            AddHandler emptyCard.Paint, Sub(sn, e) PaintCard(CType(sn, Panel), e)
            Dim lblEmpty As New Label With {
                .Text = "Nothing to compare yet." & Environment.NewLine &
                        "Record a second supplier's quotation on request #" & requestId &
                        " to see prices side by side.",
                .Font = New Font("Segoe UI", 11), .ForeColor = FaintText,
                .AutoSize = False, .Dock = DockStyle.Fill,
                .TextAlign = ContentAlignment.MiddleCenter, .BackColor = Color.Transparent
            }
            emptyCard.Controls.Add(lblEmpty)
            scrollHost.Controls.Add(emptyCard)
            container.ResumeLayout()
            container.Refresh()
            Return
        End If

        Dim recCard As Panel = BuildCompareRecommendation(suppliers, products)
        recCard.Dock = DockStyle.Top

        Dim spacerA As New Panel With {.Height = 14, .Dock = DockStyle.Top, .BackColor = Color.Transparent}

        Dim gridCard As Panel = BuildCompareGrid(suppliers, products)
        gridCard.Dock = DockStyle.Top

        ' Docked top-first renders bottom-up, so add in reverse visual order.
        scrollHost.Controls.Add(gridCard)
        scrollHost.Controls.Add(spacerA)
        scrollHost.Controls.Add(recCard)

        container.ResumeLayout()
        container.Refresh()
    End Sub

    ' The headline answer: which single supplier to send the PO to, and what
    ' choosing them costs on the rows where a rival was cheaper.
    Private Shared Function BuildCompareRecommendation(suppliers As List(Of CompareSupplier),
                                                       products As List(Of CompareProduct)) As Panel
        Dim card As New BufferedPanel With {.Height = 150, .BackColor = CardBg}
        AddHandler card.Paint, Sub(s, e) PaintCard(CType(s, Panel), e)

        ' Only a live bid can be recommended.
        Dim live As New List(Of CompareSupplier)
        For Each sup In suppliers
            If IsLiveBid(sup.Status) Then live.Add(sup)
        Next

        Dim lblHead As New Label With {
            .Text = "Recommendation", .Font = New Font("Segoe UI", 13, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(22, 16),
            .BackColor = Color.Transparent
        }
        card.Controls.Add(lblHead)

        If live.Count = 0 Then
            Dim lblNone As New Label With {
                .Text = "No live quotations left on this request — every bid was rejected or has expired.",
                .Font = New Font("Segoe UI", 10), .ForeColor = SubText,
                .AutoSize = True, .Location = New Point(22, 46), .BackColor = Color.Transparent
            }
            card.Controls.Add(lblNone)
            card.Height = 96
            Return card
        End If

        ' Cheapest overall is only a fair comparison between suppliers who quoted
        ' the same number of products. A supplier missing half the request is
        ' cheaper for the wrong reason, so completeness is ranked before price.
        Dim maxCover As Integer = 0
        For Each sup In live
            If sup.CoveredCount > maxCover Then maxCover = sup.CoveredCount
        Next

        Dim winner As CompareSupplier = Nothing
        For Each sup In live
            If sup.CoveredCount <> maxCover Then Continue For
            If winner Is Nothing OrElse sup.CoveredTotal < winner.CoveredTotal Then winner = sup
        Next
        If winner Is Nothing Then winner = live(0)

        ' Count the rows where the winner is not the cheapest, and what the gap
        ' costs, so the trade-off behind a single-supplier PO is visible.
        Dim beatenRows As Integer = 0
        Dim excess As Decimal = 0D
        For Each prod In products
            Dim best As Decimal = BestPriceFor(prod.ProductId, suppliers)
            If best < 0D Then Continue For
            Dim mine As Decimal
            If Not winner.Prices.TryGetValue(prod.ProductId, mine) Then Continue For
            If mine > best Then
                beatenRows += 1
                excess += Math.Round((mine - best) * prod.Qty, 2)
            End If
        Next

        Dim lblWinner As New Label With {
            .Text = winner.SupplierName & "   " & ChrW(8226) & "   " &
                    Peso & winner.CoveredTotal.ToString("N2"),
            .Font = New Font("Segoe UI", 16, FontStyle.Bold), .ForeColor = MintDeep,
            .AutoSize = True, .Location = New Point(22, 44), .BackColor = Color.Transparent
        }
        card.Controls.Add(lblWinner)

        Dim detail As String =
            "Lowest total across " & winner.CoveredCount &
            If(winner.CoveredCount = 1, " quoted item", " quoted items") &
            "  (" & winner.QuotationNo & ")"
        If winner.CoveredCount < products.Count Then
            detail &= "   " & ChrW(8212) & "   does not cover all " & products.Count &
                      " products on this request"
        End If

        Dim lblDetail As New Label With {
            .Text = detail, .Font = New Font("Segoe UI", 9.5F), .ForeColor = SubText,
            .AutoSize = True, .Location = New Point(24, 76), .BackColor = Color.Transparent
        }
        card.Controls.Add(lblDetail)

        Dim tradeoff As String
        If beatenRows = 0 Then
            tradeoff = "Cheapest on every item quoted " & ChrW(8212) & " no trade-off."
        Else
            tradeoff = "Another supplier is cheaper on " & beatenRows &
                       If(beatenRows = 1, " item", " items") &
                       ", worth " & Peso & excess.ToString("N2") & " " & ChrW(8212) &
                       " a purchase order goes to one supplier, so this is the cost of consolidating."
        End If

        Dim lblTrade As New Label With {
            .Text = tradeoff, .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = If(beatenRows = 0, MintDeep, WarningClr),
            .AutoSize = True, .Location = New Point(24, 98), .BackColor = Color.Transparent
        }
        card.Controls.Add(lblTrade)

        ' Act on the recommendation without hunting for it in the list.
        Dim btnOpen As New Button With {
            .Text = "Open " & winner.QuotationNo, .Height = 40, .Width = 190,
            .FlatStyle = FlatStyle.Flat, .BackColor = MintDeep, .ForeColor = Color.White,
            .Font = New Font("Segoe UI Semibold", 10), .Cursor = Cursors.Hand,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        btnOpen.FlatAppearance.BorderSize = 0
        RoundControl(btnOpen, 10)
        AddHover(btnOpen, MintDeep, DeepHover)
        Dim winnerId As Integer = winner.QuotationId
        AddHandler btnOpen.Click, Sub() RenderDetail(winnerId)
        card.Controls.Add(btnOpen)

        AddHandler card.Resize,
            Sub() btnOpen.Location = New Point(card.Width - btnOpen.Width - 22, 44)

        Return card
    End Function

    ' The grid: one row per product, one column per supplier. Column widths are
    ' computed once from the card width and reused by the header and every row,
    ' so nothing drifts when the panel resizes.
    Private Shared Function BuildCompareGrid(suppliers As List(Of CompareSupplier),
                                             products As List(Of CompareProduct)) As Panel
        Const ROW_H As Integer = 58
        Const HEAD_H As Integer = 64
        Const FOOT_H As Integer = 66
        Const PAD As Integer = 22

        Dim card As New BufferedPanel With {
            .Height = 58 + HEAD_H + products.Count * ROW_H + FOOT_H + 16,
            .BackColor = CardBg
        }
        AddHandler card.Paint, Sub(s, e) PaintCard(CType(s, Panel), e)

        Dim lblHead As New Label With {
            .Text = "Price per item", .Font = New Font("Segoe UI", 13, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(PAD, 18),
            .BackColor = Color.Transparent
        }
        Dim lblHint As New Label With {
            .Text = "Cheapest price on each row is highlighted. Unit price " & ChrW(215) &
                    " quantity requested.",
            .Font = New Font("Segoe UI", 9.5F), .ForeColor = SubText,
            .AutoSize = True, .Location = New Point(PAD + 2, 40), .BackColor = Color.Transparent
        }
        card.Controls.AddRange({lblHead, lblHint})

        ' Column geometry, shared by header, rows and footer.
        Dim colCount As Integer = suppliers.Count
        Dim colXs As Func(Of Integer, Integer()) =
            Function(w As Integer)
                Dim xs(colCount) As Integer
                Dim available As Integer = w - PAD * 2
                Dim supW As Integer = 132
                Dim nameW As Integer = available - supW * colCount
                If nameW < 160 Then nameW = 160
                xs(0) = PAD
                For i As Integer = 1 To colCount
                    xs(i) = PAD + nameW + supW * (i - 1)
                Next
                Return xs
            End Function
        Dim nameWidthOf As Func(Of Integer, Integer) =
            Function(w As Integer)
                Dim available As Integer = w - PAD * 2
                Dim nameW As Integer = available - 132 * colCount
                If nameW < 160 Then nameW = 160
                Return nameW
            End Function

        ' —— Header row: supplier names
        Dim headHost As New Panel With {.Height = HEAD_H, .BackColor = Color.Transparent}
        Dim lblProdHead As New Label With {
            .Text = "PRODUCT", .Font = New Font("Segoe UI Semibold", 8.5F),
            .ForeColor = FaintText, .AutoSize = False, .Height = 16,
            .TextAlign = ContentAlignment.MiddleLeft, .BackColor = Color.Transparent
        }
        headHost.Controls.Add(lblProdHead)

        Dim supNameLabels As New List(Of Label)
        Dim supMetaLabels As New List(Of Label)
        For Each sup In suppliers
            Dim isLive As Boolean = IsLiveBid(sup.Status)
            Dim ln As New Label With {
                .Text = sup.SupplierName,
                .Font = New Font("Segoe UI Semibold", 10),
                .ForeColor = If(isLive, MintDark, FaintText),
                .AutoSize = False, .Height = 20,
                .TextAlign = ContentAlignment.MiddleRight, .BackColor = Color.Transparent
            }
            ' A dead bid must never read as a live price, so the status is on
            ' the column itself rather than only in the quotation list.
            Dim lm As New Label With {
                .Text = If(isLive, sup.QuotationNo, sup.Status.ToLower()),
                .Font = New Font("Segoe UI", 8.5F),
                .ForeColor = If(isLive, FaintText, NegativeClr),
                .AutoSize = False, .Height = 16,
                .TextAlign = ContentAlignment.MiddleRight, .BackColor = Color.Transparent
            }
            supNameLabels.Add(ln)
            supMetaLabels.Add(lm)
            headHost.Controls.Add(ln)
            headHost.Controls.Add(lm)
        Next

        Dim headDiv As New Panel With {
            .Height = 1, .BackColor = RowDivider, .Dock = DockStyle.Bottom
        }
        headHost.Controls.Add(headDiv)

        AddHandler headHost.Resize,
            Sub()
                Dim xs() As Integer = colXs(headHost.Width)
                lblProdHead.SetBounds(xs(0), 40, nameWidthOf(headHost.Width) - 12, 16)
                For i As Integer = 0 To colCount - 1
                    supNameLabels(i).SetBounds(xs(i + 1) - 8, 22, 132, 20)
                    supMetaLabels(i).SetBounds(xs(i + 1) - 8, 42, 132, 16)
                Next
            End Sub

        ' —— Product rows
        Dim bodyHost As New Panel With {
            .Height = products.Count * ROW_H, .BackColor = Color.Transparent
        }
        Dim rowPanels As New List(Of Panel)

        For pi As Integer = 0 To products.Count - 1
            Dim prod As CompareProduct = products(pi)
            Dim best As Decimal = BestPriceFor(prod.ProductId, suppliers)

            Dim row As New Panel With {
                .Height = ROW_H, .Top = pi * ROW_H, .BackColor = Color.Transparent
            }

            Dim lblName As New Label With {
                .Text = prod.ProductName, .Font = New Font("Segoe UI Semibold", 10.5F),
                .ForeColor = MintDark, .AutoSize = False, .Height = 20,
                .TextAlign = ContentAlignment.MiddleLeft, .BackColor = Color.Transparent
            }
            Dim lblSku As New Label With {
                .Text = prod.Sku & "   " & ChrW(8226) & "   qty " & prod.Qty,
                .Font = New Font("Segoe UI", 9), .ForeColor = FaintText,
                .AutoSize = False, .Height = 18,
                .TextAlign = ContentAlignment.MiddleLeft, .BackColor = Color.Transparent
            }
            row.Controls.Add(lblName)
            row.Controls.Add(lblSku)

            Dim cellPanels As New List(Of Panel)
            For Each sup In suppliers
                Dim cell As New BufferedPanel With {
                    .Height = 44, .Width = 124, .BackColor = Color.Transparent
                }

                Dim price As Decimal
                Dim quoted As Boolean = sup.Prices.TryGetValue(prod.ProductId, price)
                Dim isLive As Boolean = IsLiveBid(sup.Status)
                Dim isBest As Boolean = quoted AndAlso isLive AndAlso best >= 0D AndAlso price = best

                ' A supplier who did not bid on this product shows a dash, never
                ' a zero — "no bid" and "free" must not look the same.
                Dim priceText As String = If(quoted, Peso & price.ToString("N2"), ChrW(8212))
                Dim lineText As String = If(quoted,
                                            Peso & Math.Round(price * prod.Qty, 2).ToString("N2"),
                                            "not quoted")

                Dim lblPrice As New Label With {
                    .Text = priceText,
                    .Font = New Font("Segoe UI Semibold", If(isBest, 11.5F, 10.5F)),
                    .ForeColor = If(Not quoted, FaintText,
                                 If(isBest, Color.FromArgb(0, 120, 92),
                                 If(isLive, MintDark, FaintText))),
                    .AutoSize = False, .Height = 20, .Width = 124, .Top = 3, .Left = 0,
                    .TextAlign = ContentAlignment.MiddleRight, .BackColor = Color.Transparent
                }
                Dim lblLine As New Label With {
                    .Text = lineText, .Font = New Font("Segoe UI", 8.5F),
                    .ForeColor = If(isBest, Color.FromArgb(0, 120, 92), FaintText),
                    .AutoSize = False, .Height = 16, .Width = 124, .Top = 23, .Left = 0,
                    .TextAlign = ContentAlignment.MiddleRight, .BackColor = Color.Transparent
                }
                cell.Controls.Add(lblPrice)
                cell.Controls.Add(lblLine)

                If isBest Then
                    ' Tinted pill behind the winning cell. Colour alone would
                    ' fail anyone who cannot distinguish it, so the cheapest
                    ' cell is also the only one in bold at a larger size.
                    AddHandler cell.Paint,
                        Sub(sn, e)
                            Dim g = e.Graphics
                            g.SmoothingMode = SmoothingMode.AntiAlias
                            Dim r As New Rectangle(6, 0, cell.Width - 8, cell.Height - 2)
                            Using path = RoundRect(r, 9)
                                Using br As New SolidBrush(Color.FromArgb(220, 248, 240))
                                    g.FillPath(br, path)
                                End Using
                            End Using
                        End Sub
                End If

                cellPanels.Add(cell)
                row.Controls.Add(cell)
            Next

            Dim rowDiv As New Panel With {
                .Height = 1, .BackColor = RowDivider, .Dock = DockStyle.Bottom
            }
            row.Controls.Add(rowDiv)

            AddHandler row.Resize,
                Sub()
                    Dim xs() As Integer = colXs(row.Width)
                    Dim nw As Integer = nameWidthOf(row.Width) - 12
                    lblName.SetBounds(xs(0), 10, nw, 20)
                    lblSku.SetBounds(xs(0), 30, nw, 18)
                    For i As Integer = 0 To colCount - 1
                        cellPanels(i).Location = New Point(xs(i + 1) - 8, 7)
                    Next
                End Sub

            rowPanels.Add(row)
            bodyHost.Controls.Add(row)
        Next

        ' —— Footer: per-supplier totals
        Dim footHost As New Panel With {.Height = FOOT_H, .BackColor = Color.Transparent}
        Dim lblTotHead As New Label With {
            .Text = "TOTAL", .Font = New Font("Segoe UI Semibold", 9),
            .ForeColor = SubText, .AutoSize = False, .Height = 20,
            .TextAlign = ContentAlignment.MiddleLeft, .BackColor = Color.Transparent
        }
        footHost.Controls.Add(lblTotHead)

        ' The cheapest total among suppliers who quoted the most products, which
        ' is the same rule the recommendation card uses.
        Dim maxCover As Integer = 0
        For Each sup In suppliers
            If IsLiveBid(sup.Status) AndAlso sup.CoveredCount > maxCover Then
                maxCover = sup.CoveredCount
            End If
        Next
        Dim bestTotal As Decimal = -1D
        For Each sup In suppliers
            If Not IsLiveBid(sup.Status) Then Continue For
            If sup.CoveredCount <> maxCover Then Continue For
            If bestTotal < 0D OrElse sup.CoveredTotal < bestTotal Then bestTotal = sup.CoveredTotal
        Next

        Dim totLabels As New List(Of Label)
        Dim subLabels As New List(Of Label)
        For Each sup In suppliers
            Dim isLive As Boolean = IsLiveBid(sup.Status)
            Dim isWin As Boolean = isLive AndAlso sup.CoveredCount = maxCover AndAlso
                                   bestTotal >= 0D AndAlso sup.CoveredTotal = bestTotal
            Dim lt As New Label With {
                .Text = Peso & sup.CoveredTotal.ToString("N2"),
                .Font = New Font("Segoe UI", If(isWin, 12.0F, 11.0F), FontStyle.Bold),
                .ForeColor = If(Not isLive, FaintText,
                             If(isWin, Color.FromArgb(0, 120, 92), MintDark)),
                .AutoSize = False, .Height = 22, .Width = 124,
                .TextAlign = ContentAlignment.MiddleRight, .BackColor = Color.Transparent
            }
            ' Coverage sits under the total because a lower number over fewer
            ' items is not actually a better offer.
            Dim ls As New Label With {
                .Text = If(isWin, ChrW(10003) & " cheapest",
                        sup.CoveredCount & " of " & products.Count & " items"),
                .Font = New Font("Segoe UI", 8.5F),
                .ForeColor = If(isWin, Color.FromArgb(0, 120, 92), FaintText),
                .AutoSize = False, .Height = 16, .Width = 124,
                .TextAlign = ContentAlignment.MiddleRight, .BackColor = Color.Transparent
            }
            totLabels.Add(lt)
            subLabels.Add(ls)
            footHost.Controls.Add(lt)
            footHost.Controls.Add(ls)
        Next

        AddHandler footHost.Resize,
            Sub()
                Dim xs() As Integer = colXs(footHost.Width)
                lblTotHead.SetBounds(xs(0), 16, nameWidthOf(footHost.Width) - 12, 20)
                For i As Integer = 0 To colCount - 1
                    totLabels(i).SetBounds(xs(i + 1) - 8, 12, 124, 22)
                    subLabels(i).SetBounds(xs(i + 1) - 8, 34, 124, 16)
                Next
            End Sub

        ' Lay the three bands out by hand: docking would reverse the row order.
        AddHandler card.Resize,
            Sub()
                Dim w As Integer = card.Width
                headHost.SetBounds(0, 58, w, HEAD_H)
                bodyHost.SetBounds(0, 58 + HEAD_H, w, products.Count * ROW_H)
                For Each rp In rowPanels
                    rp.Width = w
                Next
                footHost.SetBounds(0, 58 + HEAD_H + products.Count * ROW_H, w, FOOT_H)
            End Sub

        card.Controls.Add(headHost)
        card.Controls.Add(bodyHost)
        card.Controls.Add(footHost)

        Return card
    End Function

    ' =========================================================================
    '  DATA ACCESS
    ' =========================================================================

    ' Flips Draft/Sent quotes past their valid_until date to 'Expired'. Runs on
    ' every list render so the screen never shows a stale price as live.
    Private Shared Sub ExpireStaleQuotes()
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "UPDATE quotations SET status = 'Expired' " &
                    "WHERE status IN ('Draft','Sent') " &
                    "AND valid_until IS NOT NULL AND valid_until < CURDATE()", conn)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        Catch
            ' Non-fatal: the list still renders, rows just show as Draft/Sent.
        End Try
    End Sub

    Private Const SELECT_COLS As String =
        "SELECT q.quotation_id, q.quotation_no, q.request_id, q.supplier_id, " &
        "s.name AS sname, COALESCE(u.full_name, '—') AS uname, " &
        "q.created_at, q.valid_until, q.status, q.notes, q.total_amount, " &
        "(SELECT COUNT(*) FROM quotation_items qi WHERE qi.quotation_id = q.quotation_id) AS n_items, " &
        "(SELECT COUNT(*) FROM quotations q2 WHERE q2.request_id = q.request_id) AS n_bids " &
        "FROM quotations q " &
        "JOIN suppliers s ON s.supplier_id = q.supplier_id " &
        "LEFT JOIN users u ON u.user_id = q.created_by "

    Private Shared Function ReadRow(rdr As MySqlDataReader) As QuoteRow
        Dim r As New QuoteRow With {
            .QuotationId = SafeInt(rdr("quotation_id")),
            .QuotationNo = SafeStr(rdr("quotation_no")),
            .RequestId = SafeInt(rdr("request_id")),
            .SupplierName = SafeStr(rdr("sname")),
            .CreatedBy = SafeStr(rdr("uname")),
            .CreatedAt = SafeDate(rdr("created_at")),
            .Status = SafeStr(rdr("status")),
            .Notes = SafeStr(rdr("notes")),
            .TotalAmount = SafeDec(rdr("total_amount")),
            .ItemCount = SafeInt(rdr("n_items")),
            .BidCount = SafeInt(rdr("n_bids"))
        }
        If Not IsDBNull(rdr("valid_until")) Then
            r.HasValidUntil = True
            r.ValidUntil = SafeDate(rdr("valid_until"))
        End If
        Return r
    End Function

    Private Shared Function LoadList(filter As String) As List(Of QuoteRow)
        Dim result As New List(Of QuoteRow)
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Dim where As String = If(filter = "All", "", "WHERE q.status = @st ")
                Using cmd As New MySqlCommand(
                    SELECT_COLS & where &
                    "ORDER BY q.created_at DESC, q.quotation_id DESC", conn)
                    If filter <> "All" Then cmd.Parameters.AddWithValue("@st", filter)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            result.Add(ReadRow(rdr))
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load quotations:" & Environment.NewLine & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
        Return result
    End Function

    Private Shared Function LoadOne(quotationId As Integer) As QuoteRow
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    SELECT_COLS & "WHERE q.quotation_id = @id", conn)
                    cmd.Parameters.AddWithValue("@id", quotationId)
                    Using rdr = cmd.ExecuteReader()
                        If rdr.Read() Then Return ReadRow(rdr)
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load the quotation:" & Environment.NewLine & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
        Return Nothing
    End Function

    Private Shared Function LoadLines(quotationId As Integer) As List(Of QuoteLine)
        Dim result As New List(Of QuoteLine)
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT qi.product_id, p.name AS pname, p.sku, " &
                    "qi.qty_requested, qi.unit_price " &
                    "FROM quotation_items qi " &
                    "JOIN products p ON p.product_id = qi.product_id " &
                    "WHERE qi.quotation_id = @id " &
                    "ORDER BY qi.item_id", conn)
                    cmd.Parameters.AddWithValue("@id", quotationId)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            result.Add(New QuoteLine With {
                                .ProductId = SafeInt(rdr("product_id")),
                                .ProductName = SafeStr(rdr("pname")),
                                .Sku = SafeStr(rdr("sku")),
                                .Qty = SafeInt(rdr("qty_requested")),
                                .UnitPrice = SafeDec(rdr("unit_price"))
                            })
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load quotation items:" & Environment.NewLine & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
        Return result
    End Function

    ' ══════════════════════════════════════════════════════════════════════════
    '  COMPARISON DATA
    ' ══════════════════════════════════════════════════════════════════════════

    ' Every supplier who has quoted a given request, each carrying their unit
    ' price per product. Rejected and expired quotes are kept so the grid shows
    ' the full bidding history rather than silently dropping losing bids.
    Private Shared Function LoadCompareSuppliers(requestId As Integer) As List(Of CompareSupplier)
        Dim result As New List(Of CompareSupplier)
        Try
            Using conn = Db.GetConnection()
                conn.Open()

                Using cmd As New MySqlCommand(
                    "SELECT q.quotation_id, q.quotation_no, q.supplier_id, " &
                    "s.name AS sname, q.status, q.total_amount " &
                    "FROM quotations q " &
                    "JOIN suppliers s ON s.supplier_id = q.supplier_id " &
                    "WHERE q.request_id = @rid " &
                    "ORDER BY q.created_at, q.quotation_id", conn)
                    cmd.Parameters.AddWithValue("@rid", requestId)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            result.Add(New CompareSupplier With {
                                .QuotationId = SafeInt(rdr("quotation_id")),
                                .QuotationNo = SafeStr(rdr("quotation_no")),
                                .SupplierId = SafeInt(rdr("supplier_id")),
                                .SupplierName = SafeStr(rdr("sname")),
                                .Status = SafeStr(rdr("status")),
                                .TotalAmount = SafeDec(rdr("total_amount"))
                            })
                        End While
                    End Using
                End Using

                If result.Count = 0 Then Return result

                ' One pass for every line across every quote on this request,
                ' rather than a query per supplier.
                Using cmd As New MySqlCommand(
                    "SELECT q.quotation_id, qi.product_id, qi.unit_price " &
                    "FROM quotation_items qi " &
                    "JOIN quotations q ON q.quotation_id = qi.quotation_id " &
                    "WHERE q.request_id = @rid", conn)
                    cmd.Parameters.AddWithValue("@rid", requestId)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            Dim qid As Integer = SafeInt(rdr("quotation_id"))
                            Dim pid As Integer = SafeInt(rdr("product_id"))
                            Dim price As Decimal = SafeDec(rdr("unit_price"))
                            For Each sup In result
                                If sup.QuotationId = qid Then
                                    sup.Prices(pid) = price
                                    Exit For
                                End If
                            Next
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load the supplier comparison:" & Environment.NewLine & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
        Return result
    End Function

    ' The products under comparison: the union of everything quoted on this
    ' request, so a product only one supplier bid on still gets a row.
    Private Shared Function LoadCompareProducts(requestId As Integer) As List(Of CompareProduct)
        Dim result As New List(Of CompareProduct)
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT qi.product_id, p.name AS pname, p.sku, " &
                    "MAX(qi.qty_requested) AS qty " &
                    "FROM quotation_items qi " &
                    "JOIN quotations q ON q.quotation_id = qi.quotation_id " &
                    "JOIN products p ON p.product_id = qi.product_id " &
                    "WHERE q.request_id = @rid " &
                    "GROUP BY qi.product_id, p.name, p.sku " &
                    "ORDER BY p.name", conn)
                    cmd.Parameters.AddWithValue("@rid", requestId)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            result.Add(New CompareProduct With {
                                .ProductId = SafeInt(rdr("product_id")),
                                .ProductName = SafeStr(rdr("pname")),
                                .Sku = SafeStr(rdr("sku")),
                                .Qty = Math.Max(SafeInt(rdr("qty")), 1)
                            })
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load the compared products:" & Environment.NewLine & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
        Return result
    End Function

    ' Fills in each supplier's cost for the products they actually quoted.
    ' Computed from unit price x requested qty rather than read from
    ' quotations.total_amount, so every column is measured the same way.
    Private Shared Sub ComputeCoveredTotals(suppliers As List(Of CompareSupplier),
                                            products As List(Of CompareProduct))
        For Each sup In suppliers
            sup.CoveredTotal = 0D
            sup.CoveredCount = 0
            For Each prod In products
                Dim price As Decimal
                If sup.Prices.TryGetValue(prod.ProductId, price) Then
                    sup.CoveredTotal += Math.Round(price * prod.Qty, 2)
                    sup.CoveredCount += 1
                End If
            Next
        Next
    End Sub

    ' The lowest unit price quoted for one product, or -1 when nobody quoted it.
    ' Only live bids (Draft/Sent/Accepted) win the highlight — a rejected or
    ' expired price is shown for history but must not be recommended.
    Private Shared Function BestPriceFor(productId As Integer,
                                         suppliers As List(Of CompareSupplier)) As Decimal
        Dim best As Decimal = -1D
        For Each sup In suppliers
            If Not IsLiveBid(sup.Status) Then Continue For
            Dim price As Decimal
            If sup.Prices.TryGetValue(productId, price) Then
                If best < 0D OrElse price < best Then best = price
            End If
        Next
        Return best
    End Function

    ' A bid still in play. Rejected and Expired quotes stay visible in the grid
    ' as history, but cannot be the cheapest or be acted on.
    Private Shared Function IsLiveBid(status As String) As Boolean
        Return status = "Draft" OrElse status = "Sent" OrElse status = "Accepted"
    End Function

    ' Every quote on a request, whatever its status. Unlike CountSiblingQuotes
    ' this counts rejected and expired bids too, because comparing prices is a
    ' historical question — a losing bid is still worth seeing.
    Private Shared Function CountQuotesOnRequest(requestId As Integer) As Integer
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT COUNT(*) FROM quotations WHERE request_id = @rid", conn)
                    cmd.Parameters.AddWithValue("@rid", requestId)
                    Return SafeInt(cmd.ExecuteScalar())
                End Using
            End Using
        Catch
            Return 0
        End Try
    End Function

    ' How many other still-open quotes sit on the same request.
    Private Shared Function CountSiblingQuotes(q As QuoteRow) As Integer
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT COUNT(*) FROM quotations " &
                    "WHERE request_id = @rid AND quotation_id <> @id " &
                    "AND status IN ('Draft','Sent')", conn)
                    cmd.Parameters.AddWithValue("@rid", q.RequestId)
                    cmd.Parameters.AddWithValue("@id", q.QuotationId)
                    Return SafeInt(cmd.ExecuteScalar())
                End Using
            End Using
        Catch
            Return 0
        End Try
    End Function

    ' =========================================================================
    '  HELPERS (mirrored from ProcurementPOPanelBuilder)
    ' =========================================================================
    Private Shared Function StatusBadge(status As String) As (fore As Color, back As Color)
        Select Case status
            Case "Accepted" : Return BadgeGood
            Case "Sent" : Return BadgeInfo
            Case "Draft" : Return BadgeWarn
            Case "Rejected" : Return BadgeBad
            Case "Expired" : Return BadgeMuted
            Case Else : Return BadgeMuted
        End Select
    End Function

    Private Shared Function MakeBadge(text As String,
                                      clr As (fore As Color, back As Color)) As Panel
        Dim host As New BufferedPanel With {.Height = 26, .BackColor = Color.Transparent}
        Dim lbl As New Label With {
            .Text = text, .Font = New Font("Segoe UI Semibold", 9),
            .ForeColor = clr.fore, .AutoSize = False, .Dock = DockStyle.Fill,
            .TextAlign = ContentAlignment.MiddleCenter, .BackColor = Color.Transparent
        }
        host.Width = TextRenderer.MeasureText(text, lbl.Font).Width + 26
        AddHandler host.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Using path As GraphicsPath = RoundRect(New Rectangle(0, 0, host.Width - 1, host.Height - 1), 12)
                    Using b As New SolidBrush(clr.back)
                        e.Graphics.FillPath(b, path)
                    End Using
                End Using
            End Sub
        host.Controls.Add(lbl)
        Return host
    End Function

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

    Private Shared Function RoundRect(rect As Rectangle, radius As Integer) As GraphicsPath
        Dim path As New GraphicsPath()
        Dim d As Integer = radius * 2
        path.AddArc(rect.X, rect.Y, d, d, 180, 90)
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90)
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90)
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90)
        path.CloseFigure()
        Return path
    End Function

    Private Shared Sub RoundControl(ctrl As Control, radius As Integer)
        AddHandler ctrl.Resize,
            Sub()
                If ctrl.Width <= 0 OrElse ctrl.Height <= 0 Then Return
                ctrl.Region = New Region(RoundRect(New Rectangle(0, 0, ctrl.Width, ctrl.Height), radius))
            End Sub
        If ctrl.Width > 0 AndAlso ctrl.Height > 0 Then
            ctrl.Region = New Region(RoundRect(New Rectangle(0, 0, ctrl.Width, ctrl.Height), radius))
        End If
    End Sub

    Private Shared Sub AddHover(btn As Button, normal As Color, hover As Color)
        AddHandler btn.MouseEnter, Sub() btn.BackColor = hover
        AddHandler btn.MouseLeave, Sub() btn.BackColor = normal
    End Sub

    Private Shared Function SafeInt(o As Object) As Integer
        If o Is Nothing OrElse IsDBNull(o) Then Return 0
        Dim n As Integer
        If Integer.TryParse(o.ToString(), n) Then Return n
        Return 0
    End Function

    Private Shared Function SafeStr(o As Object) As String
        If o Is Nothing OrElse IsDBNull(o) Then Return ""
        Return o.ToString()
    End Function

    Private Shared Function SafeDec(o As Object) As Decimal
        If o Is Nothing OrElse IsDBNull(o) Then Return 0D
        Dim d As Decimal
        If Decimal.TryParse(o.ToString(), d) Then Return d
        Return 0D
    End Function

    Private Shared Function SafeDate(o As Object) As DateTime
        If o Is Nothing OrElse IsDBNull(o) Then Return DateTime.MinValue
        Dim d As DateTime
        If DateTime.TryParse(o.ToString(), d) Then Return d
        Return DateTime.MinValue
    End Function

End Class
