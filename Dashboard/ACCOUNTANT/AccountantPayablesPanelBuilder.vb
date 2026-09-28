Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports System.Linq
Imports MySql.Data.MySqlClient

' =============================================================================
'  AccountantPayablesPanelBuilder  —  Screen 3: Payables
'
'  What the store owes its suppliers, in plain words:
'
'    STATUS    Paid / Unpaid        -- has the supplier been paid?
'    DELIVERY  Ordered / Partially Received / Received
'                                    -- where the goods are (the PO stage)
'
'  The PO stage used to sit in the STATUS column, which read as if "Ordered"
'  or "Received" said something about payment. It does not, so payment and
'  delivery are now two separate columns.
'
'  Which orders count: those actually placed with a supplier (Ordered,
'  Partially Received, Received) whose ORDER date falls in the From / To
'  period. Drafts are not a debt yet and Cancelled orders never will be.
'
'  Paid means purchase_orders.paid_at is set (SupplierPaymentModal writes it
'  together with the supplier_payments row). On a database without paid_at,
'  a Received order is taken as settled -- the behaviour this screen always
'  had there -- so an un-migrated database is never shown a false debt.
' =============================================================================
Public Class AccountantPayablesPanelBuilder

    ' ── PALETTE ──────────────────────────────────────────────────────────────
    Private Shared ReadOnly MintDark As Color = Theme.Dark
    Private Shared ReadOnly MintPrimary As Color = Theme.Primary
    Private Shared ReadOnly MintDeep As Color = Theme.Deep
    Private Shared ReadOnly MintBg As Color = Theme.PageBg
    Private Shared ReadOnly CardBg As Color = Color.White
    Private Shared ReadOnly SubText As Color = Theme.SubText
    Private Shared ReadOnly FaintText As Color = Theme.FaintText
    Private Shared ReadOnly RowDivider As Color = Theme.RowDivider
    Private Shared ReadOnly RowHover As Color = Theme.RowHover
    Private Shared ReadOnly AccentOrange As Color = Theme.Warning
    Private Shared ReadOnly AccentBlue As Color = Color.FromArgb(80, 160, 255)
    Private Shared ReadOnly AccentRed As Color = Theme.Danger
    Private Shared ReadOnly Peso As String = ChrW(8369)

    ' ── PAGING ───────────────────────────────────────────────────────────────
    Private Const PAGE_SIZE As Integer = 50
    Private Shared _currentPage As Integer = 0
    Private Shared _lblPageInfo As Label
    Private Shared _btnPrev As Button
    Private Shared _btnNext As Button

    ' ── DATA MODEL ───────────────────────────────────────────────────────────
    Private Class PayableRow
        ' Carried so a payment can be recorded against the order itself rather
        ' than matched back by po_no, which is a display string.
        Public PoId As Integer
        Public PoNo As String
        Public Supplier As String
        Public OrderDate As DateTime
        Public HasExpected As Boolean
        Public ExpectedDate As DateTime
        Public TotalCost As Decimal
        Public Delivery As String          ' purchase_orders.status
        Public IsPaid As Boolean
        Public PaidAt As DateTime?
        Public AmountPaid As Decimal?      ' from supplier_payments, when recorded

        ' Goods late: expected date passed and not all of it has arrived.
        Public ReadOnly Property IsOverdue As Boolean
            Get
                Return HasExpected AndAlso ExpectedDate.Date < DateTime.Today AndAlso Delivery <> "Received"
            End Get
        End Property
    End Class

    ' ── STATE ────────────────────────────────────────────────────────────────
    Private Const TAB_ALL As String = "All"
    Private Const TAB_UNPAID As String = "Unpaid"
    Private Const TAB_PAID As String = "Paid"
    Private Shared ReadOnly FilterTabs() As String = {TAB_ALL, TAB_UNPAID, TAB_PAID}

    Private Shared _container As Panel
    Private Shared _filter As String = TAB_ALL
    Private Shared _allRows As New List(Of PayableRow)     ' every order in the period
    Private Shared _rows As New List(Of PayableRow)        ' after the Paid / Unpaid tab
    Private Shared _unpaidAllDates As Decimal               ' owed regardless of period
    Private Shared _loadError As String = ""

    Private Shared _rowsHost As Panel
    Private Shared _reflowAttached As Boolean = False

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Shared Sub LoadPayables(container As Panel, Optional filter As String = TAB_ALL)
        _container = container
        _filter = If(Array.IndexOf(FilterTabs, filter) >= 0, filter, TAB_ALL)
        _currentPage = 0
        LoadData()
        Render()
    End Sub

    ' ── SETTLEMENT SUPPORT ───────────────────────────────────────────────────
    Private Shared _hasPaidAt As Boolean = False
    Private Shared _hasPayments As Boolean = False

    ' Public: Reports uses the same test so its "Unpaid Purchase Orders"
    ' figure follows the same Paid / Unpaid rule as this screen.
    Public Shared Function HasPaidAtColumn(conn As MySqlConnection) As Boolean
        Return SchemaHas(conn, "SELECT COUNT(*) FROM information_schema.columns " &
                               "WHERE table_schema = DATABASE() AND table_name = 'purchase_orders' " &
                               "AND column_name = 'paid_at'")
    End Function

    Private Shared Function SchemaHas(conn As MySqlConnection, sql As String) As Boolean
        Try
            Using cmd As New MySqlCommand(sql, conn)
                Return Convert.ToInt32(cmd.ExecuteScalar()) > 0
            End Using
        Catch
            ' No access to information_schema: behave as un-migrated.
            Return False
        End Try
    End Function

    ' Record a payment from the supplier's receipt. SupplierPaymentModal stores
    ' what was actually paid and stamps paid_at in one transaction.
    Private Shared Sub MarkPaid(row As PayableRow)
        If SupplierPaymentModal.Show(row.PoId, row.PoNo, row.Supplier, row.TotalCost) Then
            LoadPayables(_container, _filter)
        End If
    End Sub

    ' =========================================================================
    '  DATA
    ' =========================================================================
    Private Shared Sub LoadData()
        _allRows.Clear()
        _rows.Clear()
        _unpaidAllDates = 0D
        _loadError = ""

        Try
            Using conn = Db.GetConnection()
                conn.Open()
                _hasPaidAt = HasPaidAtColumn(conn)
                _hasPayments = SchemaHas(conn,
                    "SELECT COUNT(*) FROM information_schema.tables " &
                    "WHERE table_schema = DATABASE() AND table_name = 'supplier_payments'")

                Dim paidExpr As String = If(_hasPaidAt, "po.paid_at IS NOT NULL", "po.status = 'Received'")

                Using cmd As New MySqlCommand(
                    "SELECT po.po_id, po.po_no, s.name AS sname, po.order_date, po.expected_date, " &
                    "po.total_cost, po.status, (" & paidExpr & ") AS is_paid" &
                    If(_hasPaidAt, ", po.paid_at", ", NULL AS paid_at") &
                    If(_hasPayments, ", sp.amount_paid", ", NULL AS amount_paid") & " " &
                    "FROM purchase_orders po " &
                    "JOIN suppliers s ON po.supplier_id = s.supplier_id " &
                    If(_hasPayments,
                       "LEFT JOIN (SELECT po_id, SUM(amount_paid) AS amount_paid " &
                       "           FROM supplier_payments GROUP BY po_id) sp ON sp.po_id = po.po_id ", "") &
                    "WHERE po.status IN ('Ordered','Partially Received','Received') " &
                    "AND " & AccountantPeriod.SqlRange("po.order_date") & " " &
                    "ORDER BY (" & paidExpr & ") ASC, po.order_date ASC", conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            Dim hasExp As Boolean = Not IsDBNull(rdr("expected_date"))
                            _allRows.Add(New PayableRow With {
                                .PoId = Convert.ToInt32(rdr("po_id")),
                                .PoNo = rdr("po_no").ToString(),
                                .Supplier = rdr("sname").ToString(),
                                .OrderDate = Convert.ToDateTime(rdr("order_date")),
                                .HasExpected = hasExp,
                                .ExpectedDate = If(hasExp, Convert.ToDateTime(rdr("expected_date")), DateTime.MinValue),
                                .TotalCost = Convert.ToDecimal(rdr("total_cost")),
                                .Delivery = rdr("status").ToString(),
                                .IsPaid = Convert.ToInt32(rdr("is_paid")) = 1,
                                .PaidAt = If(IsDBNull(rdr("paid_at")), CType(Nothing, DateTime?), Convert.ToDateTime(rdr("paid_at"))),
                                .AmountPaid = If(IsDBNull(rdr("amount_paid")), CType(Nothing, Decimal?), Convert.ToDecimal(rdr("amount_paid")))
                            })
                        End While
                    End Using
                End Using

                ' Owed in total, whatever the period -- so an old unpaid order
                ' outside the chosen dates is never silently out of sight.
                Using cmd As New MySqlCommand(
                    "SELECT COALESCE(SUM(po.total_cost),0) FROM purchase_orders po " &
                    "WHERE po.status IN ('Ordered','Partially Received','Received') " &
                    "AND NOT (" & paidExpr & ")", conn)
                    _unpaidAllDates = Convert.ToDecimal(cmd.ExecuteScalar())
                End Using
            End Using
        Catch ex As Exception
            _loadError = "Could not load payables: " & ex.Message
            Debug.WriteLine("AccountantPayables LoadData error: " & ex.Message)
        End Try

        Select Case _filter
            Case TAB_UNPAID : _rows.AddRange(_allRows.Where(Function(r) Not r.IsPaid))
            Case TAB_PAID : _rows.AddRange(_allRows.Where(Function(r) r.IsPaid))
            Case Else : _rows.AddRange(_allRows)
        End Select
    End Sub

    ' =========================================================================
    '  RENDER
    ' =========================================================================
    Private Shared Sub Render()
        _container.SuspendLayout()
        _container.Controls.Clear()
        _reflowAttached = False
        _container.BackColor = MintBg

        Dim tableWrapper As New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = MintBg,
            .Padding = New Padding(0)
        }

        Dim listArea As New BufferedPanel With {
            .Dock = DockStyle.Fill,
            .BackColor = CardBg,
            .Padding = New Padding(1)
        }
        AddHandler listArea.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)

        _rowsHost = New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = Color.Transparent,
            .AutoScroll = True,
            .Padding = New Padding(0, 4, 0, 8)
        }

        Dim colHdr As Panel = BuildColHeader()
        colHdr.Dock = DockStyle.Top

        Dim pagerBar As Panel = BuildPagerBar()
        pagerBar.Dock = DockStyle.Bottom

        listArea.Controls.Add(pagerBar)
        listArea.Controls.Add(colHdr)
        listArea.Controls.Add(_rowsHost)
        colHdr.BringToFront()
        _rowsHost.BringToFront()

        Dim spacerBottom As New Panel With {.Dock = DockStyle.Bottom, .Height = 16, .BackColor = MintBg}
        Dim spacerTop As New Panel With {.Dock = DockStyle.Top, .Height = 16, .BackColor = MintBg}
        Dim spacerLeft As New Panel With {.Dock = DockStyle.Left, .Width = 24, .BackColor = MintBg}
        Dim spacerRight As New Panel With {.Dock = DockStyle.Right, .Width = 24, .BackColor = MintBg}

        tableWrapper.Controls.Add(listArea)
        tableWrapper.Controls.Add(spacerBottom)
        tableWrapper.Controls.Add(spacerTop)
        tableWrapper.Controls.Add(spacerLeft)
        tableWrapper.Controls.Add(spacerRight)

        Dim topRegion As Panel = BuildTopRegion()
        topRegion.Dock = DockStyle.Top

        _container.Controls.Add(tableWrapper)
        _container.Controls.Add(topRegion)

        PopulateRows()
        _container.ResumeLayout()
    End Sub

    ' =========================================================================
    '  TOP REGION  — title (72) + From/To and Paid/Unpaid tabs (52) + KPIs (165)
    ' =========================================================================
    Private Shared Function BuildTopRegion() As Panel
        Const SIDE_PAD As Integer = 24

        Dim region As New Panel With {
            .BackColor = MintBg,
            .AutoSize = False
        }

        Dim titleBar As New Panel With {
            .Height = 72,
            .Dock = DockStyle.Top,
            .BackColor = Color.Transparent
        }
        Dim lblTitle As New Label With {
            .Text = "Payables",
            .Font = New Font("Segoe UI", 28, FontStyle.Bold),
            .ForeColor = MintDark,
            .AutoSize = True,
            .Location = New Point(SIDE_PAD, 16)
        }
        titleBar.Controls.Add(lblTitle)

        ' ── Row 2: From / To left, All / Unpaid / Paid right ─────────────────
        Dim pillBar As New Panel With {
            .Height = 52,
            .Dock = DockStyle.Top,
            .BackColor = Color.Transparent
        }

        Dim period As Panel = AccountantPeriod.BuildFields(Sub() LoadPayables(_container, _filter))
        period.Location = New Point(SIDE_PAD, 9)

        ' Sized by hand: an AutoSize flow panel has no height yet when the bar
        ' first lays out, which put the tabs above the bar's top edge.
        Dim filterHost As New FlowLayoutPanel With {
            .FlowDirection = FlowDirection.LeftToRight,
            .AutoSize = False,
            .Size = New Size(FilterTabs.Length * 122, 34),
            .Padding = New Padding(0),
            .BackColor = Color.Transparent,
            .WrapContents = False
        }
        For Each tabName In FilterTabs
            Dim capTab As String = tabName
            Dim count As Integer =
                If(capTab = TAB_UNPAID, _allRows.Where(Function(r) Not r.IsPaid).Count(),
                If(capTab = TAB_PAID, _allRows.Where(Function(r) r.IsPaid).Count(), _allRows.Count))
            Dim b As Button = MakePill(capTab & "  (" & count & ")", 116, capTab = _filter)
            AddHandler b.Click, Sub() If capTab <> _filter Then LoadPayables(_container, capTab)
            filterHost.Controls.Add(b)
        Next

        AddHandler pillBar.Resize,
            Sub()
                filterHost.Left = pillBar.Width - filterHost.Width - SIDE_PAD
                filterHost.Top = (pillBar.Height - filterHost.Height) \ 2
            End Sub

        pillBar.Controls.AddRange({period, filterHost})

        Dim kpiBar As Panel = BuildKpiBar()
        kpiBar.Dock = DockStyle.Top

        region.Controls.Add(kpiBar)
        region.Controls.Add(pillBar)
        region.Controls.Add(titleBar)

        region.Height = 72 + 52 + 165
        Return region
    End Function

    Private Shared Function MakePill(text As String, width As Integer, active As Boolean) As Button
        Dim b As New Button With {
            .Text = text,
            .FlatStyle = FlatStyle.Flat,
            .Cursor = Cursors.Hand,
            .Size = New Size(width, 34),
            .Margin = New Padding(6, 0, 0, 0),
            .Font = New Font(If(active, "Segoe UI Semibold", "Segoe UI"), 9.5F),
            .BackColor = If(active, MintPrimary, Color.FromArgb(220, 235, 232)),
            .ForeColor = If(active, Color.White, SubText)
        }
        b.FlatAppearance.BorderSize = 0
        AddHandler b.Paint,
            Sub(s, e)
                Dim bb = CType(s, Button)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Using bg As New SolidBrush(MintBg)
                    g.FillRectangle(bg, bb.ClientRectangle)
                End Using
                Dim rect = bb.ClientRectangle : rect.Inflate(-1, -1)
                Using br As New SolidBrush(bb.BackColor)
                    g.FillRoundedRectangle(br, rect, 8)
                End Using
                TextRenderer.DrawText(g, bb.Text, bb.Font, bb.ClientRectangle, bb.ForeColor,
                    TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub
        If Not active Then
            AddHandler b.MouseEnter, Sub()
                                         b.BackColor = Color.FromArgb(210, 240, 235)
                                         b.ForeColor = MintDark
                                         b.Invalidate()
                                     End Sub
            AddHandler b.MouseLeave, Sub()
                                         b.BackColor = Color.FromArgb(220, 235, 232)
                                         b.ForeColor = SubText
                                         b.Invalidate()
                                     End Sub
        End If
        Return b
    End Function

    ' =========================================================================
    '  KPI BAR — Unpaid / Paid / Total Purchases, for the chosen period
    ' =========================================================================
    Private Shared Function BuildKpiBar() As Panel
        Const SIDE_PAD As Integer = 24

        Dim unpaid = _allRows.Where(Function(r) Not r.IsPaid).ToList()
        Dim paid = _allRows.Where(Function(r) r.IsPaid).ToList()
        Dim unpaidTotal As Decimal = unpaid.Sum(Function(r) r.TotalCost)
        Dim paidTotal As Decimal = paid.Sum(Function(r) If(r.AmountPaid, r.TotalCost))
        Dim allTotal As Decimal = _allRows.Sum(Function(r) r.TotalCost)
        Dim overdue As Integer = unpaid.Where(Function(r) r.IsOverdue).Count()

        Dim unpaidSub As String = unpaid.Count & If(unpaid.Count = 1, " order", " orders") &
                                  If(overdue > 0, ", " & overdue & " overdue", "")
        ' Owed from outside the period too? Say so, so it is never missed.
        If _unpaidAllDates > unpaidTotal Then
            unpaidSub = Peso & _unpaidAllDates.ToString("N2") & " unpaid across all dates"
        End If

        Dim bar As New Panel With {
            .Height = 165,
            .BackColor = Color.Transparent,
            .Padding = New Padding(SIDE_PAD, 16, SIDE_PAD, 0)
        }

        Dim kpiRow As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .ColumnCount = 3,
            .RowCount = 1,
            .BackColor = Color.Transparent,
            .CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        }
        For i = 0 To 2
            kpiRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 33.33F))
        Next
        kpiRow.RowStyles.Add(New RowStyle(SizeType.Percent, 100))

        Dim c1 = MakeKpiCard("Unpaid", Peso & unpaidTotal.ToString("N2"),
                             If(unpaidTotal > 0, AccentOrange, FaintText), unpaidSub)
        Dim c2 = MakeKpiCard("Paid", Peso & paidTotal.ToString("N2"), MintDeep,
                             paid.Count & If(paid.Count = 1, " order", " orders") & " settled")
        Dim c3 = MakeKpiCard("Total Purchases", Peso & allTotal.ToString("N2"), MintPrimary,
                             "Ordered " & AccountantPeriod.Text())

        c1.Margin = New Padding(0, 0, 6, 0)
        c2.Margin = New Padding(6, 0, 6, 0)
        c3.Margin = New Padding(6, 0, 0, 0)

        kpiRow.Controls.Add(c1, 0, 0)
        kpiRow.Controls.Add(c2, 1, 0)
        kpiRow.Controls.Add(c3, 2, 0)

        bar.Controls.Add(kpiRow)
        Return bar
    End Function

    Private Shared Function MakeKpiCard(caption As String, value As String,
                                         valueClr As Color, subLabel As String) As Panel
        Dim card As New BufferedPanel With {.Dock = DockStyle.Fill, .BackColor = CardBg}
        AddHandler card.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)

        Dim lblCap As New Label With {
            .Text = caption,
            .Font = New Font("Segoe UI", 11),
            .ForeColor = SubText, .AutoSize = True,
            .Location = New Point(22, 14), .BackColor = Color.Transparent
        }
        Dim lblVal As New Label With {
            .Text = value,
            .Font = New Font("Segoe UI", 26, FontStyle.Bold),
            .ForeColor = valueClr, .AutoSize = True,
            .Location = New Point(20, 36), .BackColor = Color.Transparent
        }
        Dim lblSub As New Label With {
            .Text = subLabel,
            .Font = New Font("Segoe UI", 10),
            .ForeColor = SubText, .AutoSize = True,
            .Location = New Point(22, 100), .BackColor = Color.Transparent
        }
        card.Controls.AddRange({lblCap, lblVal, lblSub})
        Return card
    End Function

    ' =========================================================================
    '  COLUMN HEADER — same words as the Procurement screens
    ' =========================================================================
    Private Shared ReadOnly ColX() As Double = {0, 0.15, 0.33, 0.45, 0.57, 0.73}

    Private Shared Function BuildColHeader() As Panel
        Dim hdr As New Panel With {.Height = 44, .BackColor = Color.Transparent}

        Dim labels As Label() = {
            MakeColLabel("PO NO."),
            MakeColLabel("SUPPLIER"),
            MakeColLabel("ORDERED"),
            MakeColLabel("EXPECTED"),
            MakeColLabel("DELIVERY"),
            MakeColLabel("STATUS"),
            MakeColLabel("TOTAL COST")
        }
        hdr.Controls.AddRange(labels)

        AddHandler hdr.Resize,
            Sub()
                Dim w = hdr.Width
                labels(0).Left = 24
                For i As Integer = 1 To 5
                    labels(i).Left = CInt(w * ColX(i))
                Next
                labels(6).Left = w - labels(6).Width - 24
            End Sub

        AddHandler hdr.Paint,
            Sub(s, e)
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 20, hdr.Height - 1, hdr.Width - 20, hdr.Height - 1)
                End Using
            End Sub

        Return hdr
    End Function

    Private Shared Function MakeColLabel(text As String) As Label
        Return New Label With {
            .Text = text,
            .Font = New Font("Segoe UI", 10, FontStyle.Bold),
            .ForeColor = SubText,
            .AutoSize = True,
            .Location = New Point(0, 13),
            .BackColor = Color.Transparent
        }
    End Function

    ' =========================================================================
    '  PAGER BAR
    ' =========================================================================
    Private Shared Function BuildPagerBar() As Panel
        Dim wrapper As New Panel With {
            .Height = 52,
            .BackColor = Color.Transparent
        }

        AddHandler wrapper.Paint,
            Sub(s, e)
                Using dp As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(dp, 0, 0, wrapper.Width, 0)
                End Using
            End Sub

        Dim bar As New Panel With {
            .BackColor = Color.Transparent,
            .Height = 44,
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Bottom
        }

        AddHandler wrapper.Resize,
            Sub()
                bar.Left = 0
                bar.Width = wrapper.Width
                bar.Top = 0
                bar.Height = wrapper.Height
            End Sub

        _btnPrev = New Button With {
            .Text = "‹",
            .FlatStyle = FlatStyle.Flat,
            .Cursor = Cursors.Hand,
            .Size = New Size(32, 32),
            .BackColor = Color.FromArgb(220, 235, 232),
            .ForeColor = MintDark,
            .Font = New Font("Segoe UI", 14, FontStyle.Bold),
            .TextAlign = ContentAlignment.MiddleCenter
        }
        _btnPrev.FlatAppearance.BorderSize = 0

        _btnNext = New Button With {
            .Text = "›",
            .FlatStyle = FlatStyle.Flat,
            .Cursor = Cursors.Hand,
            .Size = New Size(32, 32),
            .BackColor = Color.FromArgb(220, 235, 232),
            .ForeColor = MintDark,
            .Font = New Font("Segoe UI", 14, FontStyle.Bold),
            .TextAlign = ContentAlignment.MiddleCenter
        }
        _btnNext.FlatAppearance.BorderSize = 0

        _lblPageInfo = New Label With {
            .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = SubText,
            .AutoSize = True,
            .BackColor = Color.Transparent
        }

        AddHandler bar.Resize,
            Sub()
                Dim cx As Integer = bar.Width \ 2
                _btnPrev.Left = cx - _btnPrev.Width - _lblPageInfo.Width \ 2 - 8
                _btnPrev.Top = (bar.Height - _btnPrev.Height) \ 2
                _lblPageInfo.Left = cx - _lblPageInfo.Width \ 2
                _lblPageInfo.Top = (bar.Height - _lblPageInfo.Height) \ 2
                _btnNext.Left = cx + _lblPageInfo.Width \ 2 + 8
                _btnNext.Top = (bar.Height - _btnNext.Height) \ 2
                bar.Invalidate()
            End Sub

        AddHandler _btnPrev.Click,
            Sub()
                If _currentPage > 0 Then
                    _currentPage -= 1
                    PopulateRows()
                End If
            End Sub
        AddHandler _btnNext.Click,
            Sub()
                Dim totalPages As Integer = Math.Max(1, CInt(Math.Ceiling(_rows.Count / PAGE_SIZE)))
                If _currentPage < totalPages - 1 Then
                    _currentPage += 1
                    PopulateRows()
                End If
            End Sub

        bar.Controls.AddRange({_btnPrev, _lblPageInfo, _btnNext})
        wrapper.Controls.Add(bar)
        Return wrapper
    End Function

    Private Shared Sub UpdatePager()
        If _lblPageInfo Is Nothing Then Return
        Dim totalPages As Integer = Math.Max(1, CInt(Math.Ceiling(_rows.Count / PAGE_SIZE)))
        _lblPageInfo.Text = $"Page {_currentPage + 1} of {totalPages}  ({_rows.Count} total)"
        _btnPrev.Enabled = (_currentPage > 0)
        _btnNext.Enabled = (_currentPage < totalPages - 1)
        If _lblPageInfo.Parent IsNot Nothing Then
            _lblPageInfo.Parent.PerformLayout()
        End If
    End Sub

    ' =========================================================================
    '  POPULATE ROWS
    ' =========================================================================
    Private Shared Sub PopulateRows()
        _rowsHost.Controls.Clear()

        If _loadError <> "" Then
            Dim errPanel As New Panel With {
                .Height = 80, .Dock = DockStyle.Top, .BackColor = Color.FromArgb(255, 240, 240)
            }
            Dim errIcon As New Label With {
                .Text = "⚠", .Font = New Font("Segoe UI", 14), .ForeColor = AccentRed,
                .AutoSize = True, .Location = New Point(24, 22), .BackColor = Color.Transparent
            }
            Dim errLbl As New Label With {
                .Text = _loadError, .Font = New Font("Segoe UI", 10), .ForeColor = AccentRed,
                .AutoSize = True, .Location = New Point(50, 26), .BackColor = Color.Transparent
            }
            errPanel.Controls.AddRange({errIcon, errLbl})
            _rowsHost.Controls.Add(errPanel)
            UpdatePager()
            Return
        End If

        If _rows.Count = 0 Then
            Dim msg As String
            Select Case _filter
                Case TAB_UNPAID : msg = "No unpaid purchase orders from " & AccountantPeriod.Text() & "."
                Case TAB_PAID : msg = "No paid purchase orders from " & AccountantPeriod.Text() & "."
                Case Else : msg = "No purchase orders from " & AccountantPeriod.Text() & "."
            End Select
            _rowsHost.Controls.Add(BuildEmptyRow(msg))
            UpdatePager()
            Return
        End If

        Dim pageRows = _rows.Skip(_currentPage * PAGE_SIZE).Take(PAGE_SIZE).ToList()
        Dim y As Integer = 4
        For Each row In pageRows
            Dim r As Panel = BuildPayableRow(row, 64)
            r.Location = New Point(0, y)
            r.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
            _rowsHost.Controls.Add(r)
            r.Width = _rowsHost.ClientSize.Width - 4
            y += 64
        Next

        If Not _reflowAttached Then
            _reflowAttached = True
            AddHandler _rowsHost.Resize, Sub() ReflowRows(_rowsHost)
        End If
        UpdatePager()
    End Sub

    ' =========================================================================
    '  PAYABLE ROW
    ' =========================================================================
    Private Shared Function BuildPayableRow(row As PayableRow, height As Integer) As Panel
        Dim p As New BufferedPanel With {.Height = height, .BackColor = Color.Transparent, .Cursor = Cursors.Hand}
        AddHandler p.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                If TypeOf p.Tag Is Boolean AndAlso CBool(p.Tag) Then
                    Dim rect As New Rectangle(8, 3, p.Width - 16, p.Height - 6)
                    Using path As GraphicsPath = RoundRect(rect, 10)
                        Using b As New SolidBrush(RowHover)
                            e.Graphics.FillPath(b, path)
                        End Using
                    End Using
                End If
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 20, p.Height - 1, p.Width - 20, p.Height - 1)
                End Using
            End Sub

        ' Styled as a link: clicking the P.O. number shows the items bought.
        Dim lblPo As New Label With {
            .Text = row.PoNo, .Font = New Font("Segoe UI", 11, FontStyle.Bold Or FontStyle.Underline),
            .ForeColor = MintPrimary, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim poTip As New ToolTip()
        poTip.SetToolTip(lblPo, "Show the items on " & row.PoNo)
        AddHandler p.Disposed, Sub() poTip.Dispose()
        Dim lblSupplier As New Label With {
            .Text = row.Supplier, .Font = New Font("Segoe UI", 11),
            .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblOrdered As New Label With {
            .Text = row.OrderDate.ToString("MMM dd, yyyy"), .Font = New Font("Segoe UI", 11),
            .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblExpected As New Label With {
            .Text = If(row.HasExpected, row.ExpectedDate.ToString("MMM dd, yyyy"), "—"),
            .Font = New Font("Segoe UI", 11, If(row.IsOverdue, FontStyle.Bold, FontStyle.Regular)),
            .ForeColor = If(row.IsOverdue, AccentRed, FaintText),
            .AutoSize = True, .BackColor = Color.Transparent
        }

        Dim delInfo = GetDeliveryBadge(row.Delivery)
        Dim delivery As New Label With {
            .Text = row.Delivery,
            .Font = New Font("Segoe UI", 9, FontStyle.Bold),
            .ForeColor = delInfo.fore, .BackColor = delInfo.back,
            .AutoSize = True, .Padding = New Padding(10, 4, 10, 4)
        }
        RoundLabel(delivery, 10)

        Dim status As New Label With {
            .Text = If(row.IsPaid, TAB_PAID, TAB_UNPAID),
            .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold),
            .ForeColor = If(row.IsPaid, MintDeep, AccentRed),
            .BackColor = If(row.IsPaid, Color.FromArgb(214, 242, 234), Color.FromArgb(253, 232, 232)),
            .AutoSize = True, .Padding = New Padding(12, 4, 12, 4)
        }
        RoundLabel(status, 10)

        Dim lblAmount As New Label With {
            .Text = Peso & row.TotalCost.ToString("N2"),
            .Font = New Font("Segoe UI", 12, FontStyle.Bold),
            .ForeColor = If(row.IsPaid, MintDeep, AccentOrange),
            .AutoSize = True, .BackColor = Color.Transparent
        }

        p.Controls.AddRange({lblPo, lblSupplier, lblOrdered, lblExpected, delivery, status, lblAmount})

        AddHandler p.Resize,
            Sub()
                Dim w = p.Width
                lblPo.Left = 24 : lblPo.Top = (height - lblPo.Height) \ 2
                lblSupplier.Left = CInt(w * ColX(1)) : lblSupplier.Top = (height - lblSupplier.Height) \ 2
                lblOrdered.Left = CInt(w * ColX(2)) : lblOrdered.Top = (height - lblOrdered.Height) \ 2
                lblExpected.Left = CInt(w * ColX(3)) : lblExpected.Top = (height - lblExpected.Height) \ 2
                delivery.Left = CInt(w * ColX(4)) : delivery.Top = (height - delivery.Height) \ 2
                status.Left = CInt(w * ColX(5)) : status.Top = (height - status.Height) \ 2
                lblAmount.Left = w - lblAmount.Width - 24 : lblAmount.Top = (height - lblAmount.Height) \ 2
            End Sub

        ' Row click: the order's details, including every item bought on it.
        ' An unpaid order whose goods are in offers to record the payment from
        ' the supplier's receipt, straight from the same window.
        Dim openDetail As Action =
            Sub()
                Dim canSettle As Boolean = _hasPaidAt AndAlso Not row.IsPaid AndAlso row.Delivery = "Received"
                Dim note As String = Nothing
                If canSettle Then
                    note = "The goods have been received, so this amount is owed to the supplier."
                ElseIf Not row.IsPaid AndAlso _hasPaidAt Then
                    note = "Payment is recorded once the goods are fully received."
                End If
                If row.IsOverdue Then
                    note = ChrW(9888) & " The delivery is OVERDUE." & If(note Is Nothing, "", "  " & note)
                End If
                If PoDetailsModal.Show(row.PoId,
                                       actionText:=If(canSettle, "Record Payment", Nothing),
                                       footNote:=note) Then
                    MarkPaid(row)
                End If
            End Sub

        AddHandler p.Click, Sub() openDetail()
        For Each ctrl As Control In p.Controls
            AddHandler ctrl.Click, Sub() openDetail()
        Next

        PropagateHover(p)
        Return p
    End Function

    ' =========================================================================
    '  BADGE + HELPERS
    ' =========================================================================
    Private Shared Function GetDeliveryBadge(status As String) As (fore As Color, back As Color)
        Select Case status
            Case "Ordered" : Return (AccentOrange, Color.FromArgb(255, 240, 220))
            Case "Partially Received" : Return (AccentBlue, Color.FromArgb(220, 235, 255))
            Case "Received" : Return (MintDeep, Color.FromArgb(214, 242, 234))
            Case Else : Return (SubText, Color.FromArgb(238, 238, 238))
        End Select
    End Function

    Private Shared Function BuildEmptyRow(message As String) As Panel
        Dim p As New Panel With {.Height = 90, .Dock = DockStyle.Top, .BackColor = Color.Transparent}
        Dim lbl As New Label With {
            .Text = message, .Font = New Font("Segoe UI", 10.5F),
            .ForeColor = SubText, .AutoSize = True,
            .Location = New Point(24, 28), .BackColor = Color.Transparent
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

    Private Shared Function RoundRect(rect As Rectangle, radius As Integer) As GraphicsPath
        Dim path As New GraphicsPath()
        Dim r As Integer = radius
        path.AddArc(rect.X, rect.Y, r, r, 180, 90)
        path.AddArc(rect.Right - r, rect.Y, r, r, 270, 90)
        path.AddArc(rect.Right - r, rect.Bottom - r, r, r, 0, 90)
        path.AddArc(rect.X, rect.Bottom - r, r, r, 90, 90)
        path.CloseFigure()
        Return path
    End Function

    ' Rounded pill for a status badge.
    '
    ' Clipping with a Region alone gives hard, aliased corners -- a Region is a
    ' 1-bit mask, so the curve comes out visibly stepped. The pill is painted
    ' here instead, antialiased, with the label drawn transparently over it, and
    ' the Region kept only so the row colour shows outside the curve.
    Private Shared Sub RoundLabel(lbl As Label, radius As Integer)
        Dim fill As Color = lbl.BackColor
        lbl.BackColor = Color.Transparent

        Dim buildPath =
            Function(w As Integer, h As Integer) As GraphicsPath
                Dim r As Integer = Math.Min(radius, Math.Min(w, h) \ 2)
                Dim gp As New GraphicsPath()
                If r <= 0 Then
                    gp.AddRectangle(New Rectangle(0, 0, w, h))
                Else
                    gp.AddArc(0, 0, r * 2, r * 2, 180, 90)
                    gp.AddArc(w - r * 2 - 1, 0, r * 2, r * 2, 270, 90)
                    gp.AddArc(w - r * 2 - 1, h - r * 2 - 1, r * 2, r * 2, 0, 90)
                    gp.AddArc(0, h - r * 2 - 1, r * 2, r * 2, 90, 90)
                End If
                gp.CloseFigure()
                Return gp
            End Function

        AddHandler lbl.Paint,
            Sub(sender As Object, e As PaintEventArgs)
                Dim l = CType(sender, Label)
                If l.Width <= 0 OrElse l.Height <= 0 Then Return
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Using gp As GraphicsPath = buildPath(l.Width, l.Height)
                    Using br As New SolidBrush(fill)
                        e.Graphics.FillPath(br, gp)
                    End Using
                End Using
                TextRenderer.DrawText(e.Graphics, l.Text, l.Font, l.ClientRectangle,
                                      l.ForeColor,
                                      TextFormatFlags.HorizontalCenter Or
                                      TextFormatFlags.VerticalCenter Or
                                      TextFormatFlags.NoPadding)
            End Sub

        AddHandler lbl.Resize, Sub() lbl.Invalidate()
    End Sub

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
        row.Tag = on_
        row.Invalidate()
    End Sub

End Class
