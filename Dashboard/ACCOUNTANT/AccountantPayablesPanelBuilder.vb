Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports System.Linq
Imports MySql.Data.MySqlClient

' =============================================================================
'  AccountantPayablesPanelBuilder  —  Screen 3: Payables
'
'  FIXES APPLIED:
'    1. Row click → opens PO detail modal (stub: shows MessageBox, wire up your own form)
'    2. Refresh button added to title bar
'    3. Overdue tag positioning fixed (uses Load event instead of Resize)
'    4. Virtual/paged scrolling: renders only PAGE_SIZE rows at a time
'    5. DB failure shows visible error label instead of silent Debug.WriteLine
'
'  DB REQUIREMENT (optional, run once):
'     ALTER TABLE purchase_orders ADD COLUMN paid_at DATETIME DEFAULT NULL AFTER received_at;
'
'  Without it this screen behaves exactly as before. With it, received-but-
'  unpaid orders appear as the real liabilities they are and can be settled
'  from the row detail. See PayableWhere().
' =============================================================================
Public Class AccountantPayablesPanelBuilder

    ' ── PALETTE ──────────────────────────────────────────────────────────────
    Private Shared ReadOnly MintDark As Color = Color.FromArgb(10, 79, 66)
    Private Shared ReadOnly MintPrimary As Color = Color.FromArgb(0, 168, 142)
    Private Shared ReadOnly MintDeep As Color = Color.FromArgb(0, 130, 109)
    Private Shared ReadOnly MintBg As Color = Color.FromArgb(244, 248, 246)
    Private Shared ReadOnly CardBg As Color = Color.White
    Private Shared ReadOnly SubText As Color = Color.FromArgb(90, 114, 107)
    Private Shared ReadOnly FaintText As Color = Color.FromArgb(150, 165, 160)
    Private Shared ReadOnly RowDivider As Color = Color.FromArgb(238, 242, 240)
    Private Shared ReadOnly RowHover As Color = Color.FromArgb(224, 242, 235)
    Private Shared ReadOnly AccentOrange As Color = Color.FromArgb(232, 120, 30)
    Private Shared ReadOnly AccentBlue As Color = Color.FromArgb(80, 160, 255)
    Private Shared ReadOnly AccentRed As Color = Color.FromArgb(210, 60, 60)
    Private Shared ReadOnly Peso As String = ChrW(8369)

    ' ── PAGING ───────────────────────────────────────────────────────────────
    Private Const PAGE_SIZE As Integer = 50   ' rows rendered per page
    Private Shared _currentPage As Integer = 0
    Private Shared _lblPageInfo As Label
    Private Shared _btnPrev As Button
    Private Shared _btnNext As Button

    ' ── DATA MODEL ───────────────────────────────────────────────────────────
    Private Class PayableRow
        Public PoNo As String
        Public Supplier As String
        Public OrderDate As DateTime
        Public HasExpected As Boolean
        Public ExpectedDate As DateTime
        Public Amount As Decimal
        Public Status As String
    End Class

    ' ── STATE ────────────────────────────────────────────────────────────────
    Private Shared _container As Panel
    Private Shared _filter As String = "All"
    Private Shared _rows As New List(Of PayableRow)
    Private Shared _totalPayable As Decimal
    Private Shared _loadError As String = ""   ' FIX 5: track DB error message

    ' Filter tabs
    ' "Received" appears only once paid_at exists -- see PayableWhere(). On an
    ' un-migrated database the tab would always be empty, so it is filtered out
    ' of this list at build time rather than shown as a dead end.
    Private Shared ReadOnly AllFilterTabs() As String =
        {"All", "Draft", "Ordered", "Partially Received", "Received"}
    Private Shared FilterTabs() As String = {"All", "Draft", "Ordered", "Partially Received"}
    Private Shared _filterBtns() As Button
    Private Shared _activeFilterIdx As Integer = 0

    Private Shared _rowsHost As Panel
    Private Shared _reflowAttached As Boolean = False

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Shared Sub LoadPayables(container As Panel, Optional filter As String = "All")
        _container = container
        _filter = filter
        _currentPage = 0
        Dim idx As Integer = Array.IndexOf(FilterTabs, filter)
        _activeFilterIdx = If(idx >= 0, idx, 0)
        LoadData()
        Render()
    End Sub

    ' ── SETTLEMENT SUPPORT ───────────────────────────────────────────────────
    '  DB REQUIREMENT (run once, optional):
    '     ALTER TABLE purchase_orders ADD COLUMN paid_at DATETIME DEFAULT NULL AFTER received_at;
    '
    '  Receiving goods is what creates the liability -- the ledger credits
    '  Accounts Payable at that moment. This screen used to EXCLUDE 'Received'
    '  POs, so it showed the opposite of what was owed: real debts vanished the
    '  instant they became real, while un-received orders (not yet liabilities)
    '  were listed as payable.
    '
    '  Received POs can only be shown once "settled" is expressible, hence the
    '  column. Where it does not exist the screen keeps its old behaviour
    '  exactly, so an un-migrated database is never shown wrong figures.
    Private Shared _hasPaidAt As Boolean = False

    Private Shared Sub DetectPaidAtColumn(conn As MySqlConnection)
        _hasPaidAt = False
        Try
            Using cmd As New MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.columns " &
                "WHERE table_schema = DATABASE() " &
                "  AND table_name = 'purchase_orders' " &
                "  AND column_name = 'paid_at'", conn)
                _hasPaidAt = Convert.ToInt32(cmd.ExecuteScalar()) > 0
            End Using
        Catch
            ' No access to information_schema: stay on the old behaviour.
            _hasPaidAt = False
        End Try
    End Sub

    ' Show the Received tab only when settlement can actually be tracked.
    Private Shared Sub RefreshVisibleTabs()
        If _hasPaidAt Then
            FilterTabs = AllFilterTabs
        Else
            FilterTabs = AllFilterTabs.Take(AllFilterTabs.Length - 1).ToArray()
        End If
        ' A filter that just disappeared must not leave the screen on a dead tab.
        If Array.IndexOf(FilterTabs, _filter) < 0 Then _filter = "All"
    End Sub

    ' Settle a received purchase order: stamps paid_at, which is what removes it
    ' from payables (see PayableWhere). Guarded on paid_at IS NULL so a second
    ' click, or another accountant settling the same PO, cannot overwrite the
    ' original settlement date.
    Private Shared Sub MarkPaid(row As PayableRow)
        Try
            Dim affected As Integer
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "UPDATE purchase_orders SET paid_at = NOW() " &
                    "WHERE po_no = @no AND status = 'Received' AND paid_at IS NULL", conn)
                    cmd.Parameters.AddWithValue("@no", row.PoNo)
                    affected = cmd.ExecuteNonQuery()
                End Using
            End Using

            If affected = 0 Then
                AppModal.Show("This purchase order has already been settled." & Environment.NewLine &
                              "Its original payment date was left unchanged.",
                              "Already Paid", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Else
                ActivityLog.Record("PO_PAID", "purchase_order", Nothing,
                                   row.PoNo & " to " & row.Supplier & ", " &
                                   Peso & row.Amount.ToString("N2"))
                AppModal.Show(row.PoNo & " marked as paid." & Environment.NewLine &
                              Peso & row.Amount.ToString("N2") & " cleared from payables.",
                              "Payment Recorded", MessageBoxButtons.OK, MessageBoxIcon.Information)
            End If

            LoadPayables(_container, _filter)

        Catch ex As Exception
            AppModal.Show("Could not record the payment:" & Environment.NewLine & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    ' Statuses that represent money owed to a supplier.
    '
    ' With paid_at present, a Received PO is payable until it is settled, which
    ' is the accounting-correct view. Without it, "Received" cannot be
    ' distinguished from "Received and paid", so those are left out rather than
    ' shown as owed forever.
    Private Shared Function PayableWhere() As String
        If _hasPaidAt Then
            Return "WHERE po.status IN ('Draft','Ordered','Partially Received','Received') " &
                   "AND po.paid_at IS NULL"
        End If
        Return "WHERE po.status NOT IN ('Received','Cancelled')"
    End Function

    ' =========================================================================
    '  DATA
    ' =========================================================================
    Private Shared Sub LoadData()
        _rows.Clear()
        _totalPayable = 0
        _loadError = ""   ' FIX 5: reset before each load

        Dim statusWhere As String = BuildStatusWhere()

        Try
            Using conn = Db.GetConnection()
                conn.Open()
                DetectPaidAtColumn(conn)
                RefreshVisibleTabs()

                Using cmd As New MySqlCommand(
                    "SELECT po.po_no, s.name AS sname, po.order_date, " &
                    "po.expected_date, po.total_cost, po.status " &
                    "FROM purchase_orders po " &
                    "JOIN suppliers s ON po.supplier_id = s.supplier_id " &
                    PayableWhere() & statusWhere &
                    " ORDER BY po.order_date ASC", conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            Dim hasExp As Boolean = Not IsDBNull(rdr("expected_date"))
                            _rows.Add(New PayableRow With {
                                .PoNo = rdr("po_no").ToString(),
                                .Supplier = rdr("sname").ToString(),
                                .OrderDate = Convert.ToDateTime(rdr("order_date")),
                                .HasExpected = hasExp,
                                .ExpectedDate = If(hasExp, Convert.ToDateTime(rdr("expected_date")), DateTime.MinValue),
                                .Amount = Convert.ToDecimal(rdr("total_cost")),
                                .Status = rdr("status").ToString()
                            })
                        End While
                    End Using
                End Using

                Using cmd As New MySqlCommand(
                    "SELECT COALESCE(SUM(po.total_cost),0) FROM purchase_orders po " &
                    PayableWhere() & statusWhere, conn)
                    _totalPayable = Convert.ToDecimal(cmd.ExecuteScalar())
                End Using
            End Using

        Catch ex As Exception
            ' FIX 5: store error so Render() can show it visibly
            _loadError = "Could not load payables: " & ex.Message
            Debug.WriteLine("AccountantPayables LoadData error: " & ex.Message)
        End Try
    End Sub

    Private Shared Function BuildStatusWhere() As String
        Select Case _filter
            Case "Draft" : Return " AND po.status = 'Draft'"
            Case "Ordered" : Return " AND po.status = 'Ordered'"
            Case "Partially Received" : Return " AND po.status = 'Partially Received'"
            Case "Received" : Return " AND po.status = 'Received'"
            Case Else : Return ""
        End Select
    End Function

    ' =========================================================================
    '  RENDER
    ' =========================================================================
    Private Shared Sub Render()
        _container.SuspendLayout()
        _container.Controls.Clear()
        _reflowAttached = False
        _container.BackColor = MintBg

        ' ── Table wrapper — white card with side gutters ─────────────────────
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

        ' FIX 4: pager bar docked at bottom of listArea
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

        ' ── Top region ───────────────────────────────────────────────────────
        Dim topRegion As Panel = BuildTopRegion()
        topRegion.Dock = DockStyle.Top

        _container.Controls.Add(tableWrapper)
        _container.Controls.Add(topRegion)

        PopulateRows()
        _container.ResumeLayout()
    End Sub

    ' =========================================================================
    '  TOP REGION  — title bar (72px) + filter pill bar (52px) + KPI bar (165px)
    ' =========================================================================
    Private Shared Function BuildTopRegion() As Panel
        Const SIDE_PAD As Integer = 24

        Dim region As New Panel With {
            .BackColor = MintBg,
            .AutoSize = False
        }

        ' ── Row 1: Title bar (72px) — includes FIX 2 Refresh button ─────────
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

        ' FIX 2: Refresh button ───────────────────────────────────────────────
        Dim btnRefresh As New Button With {
            .Text = "⟳  Refresh",
            .FlatStyle = FlatStyle.Flat,
            .Cursor = Cursors.Hand,
            .Size = New Size(100, 34),
            .Font = New Font("Segoe UI", 9.5F),
            .BackColor = Color.FromArgb(220, 235, 232),
            .ForeColor = MintDark,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        btnRefresh.FlatAppearance.BorderSize = 0

        AddHandler titleBar.Resize,
            Sub()
                btnRefresh.Left = titleBar.Width - btnRefresh.Width - SIDE_PAD
                btnRefresh.Top = (titleBar.Height - btnRefresh.Height) \ 2
            End Sub

        AddHandler btnRefresh.Paint,
            Sub(s, e)
                Dim bb = CType(s, Button)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect = bb.ClientRectangle : rect.Inflate(-1, -1)
                Using br As New SolidBrush(bb.BackColor)
                    g.FillRoundedRectangle(br, rect, 8)
                End Using
                TextRenderer.DrawText(g, bb.Text, bb.Font, bb.ClientRectangle, bb.ForeColor,
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
                LoadPayables(_container, _filter)
            End Sub

        titleBar.Controls.Add(lblTitle)
        titleBar.Controls.Add(btnRefresh)

        ' ── Row 2: Filter pills (52px) ───────────────────────────────────────
        Dim pillBar As New Panel With {
            .Height = 52,
            .Dock = DockStyle.Top,
            .BackColor = Color.Transparent
        }

        ReDim _filterBtns(FilterTabs.Length - 1)
        Dim filterHost As New FlowLayoutPanel With {
            .FlowDirection = FlowDirection.LeftToRight,
            .AutoSize = True,
            .BackColor = Color.Transparent,
            .WrapContents = False,
            .Location = New Point(SIDE_PAD, 10)
        }

        For i As Integer = 0 To FilterTabs.Length - 1
            Dim idx As Integer = i
            Dim capTab As String = FilterTabs(i)
            Dim btnWidth As Integer = If(capTab = "Partially Received", 146, 96)
            If capTab = "Received" Then btnWidth = 110

            Dim b As New Button With {
                .Text = capTab,
                .FlatStyle = FlatStyle.Flat,
                .Cursor = Cursors.Hand,
                .Size = New Size(btnWidth, 34),
                .Font = New Font("Segoe UI", 9.5F),
                .Margin = New Padding(0, 0, 6, 0)
            }
            b.FlatAppearance.BorderSize = 0

            Dim styleBtn As Action(Of Boolean, Boolean) =
                Sub(active As Boolean, hover As Boolean)
                    If active Then
                        b.BackColor = MintPrimary
                        b.ForeColor = Color.White
                        b.Font = New Font("Segoe UI Semibold", 9.5F)
                    ElseIf hover Then
                        b.BackColor = Color.FromArgb(210, 240, 235)
                        b.ForeColor = MintDark
                        b.Font = New Font("Segoe UI", 9.5F)
                    Else
                        b.BackColor = Color.FromArgb(220, 235, 232)
                        b.ForeColor = SubText
                        b.Font = New Font("Segoe UI", 9.5F)
                    End If
                    b.Invalidate()
                End Sub

            AddHandler b.Paint,
                Sub(s, e)
                    Dim bb = CType(s, Button)
                    Dim g = e.Graphics
                    g.SmoothingMode = SmoothingMode.AntiAlias
                    Dim rect = bb.ClientRectangle : rect.Inflate(-1, -1)
                    Using br As New SolidBrush(bb.BackColor)
                        g.FillRoundedRectangle(br, rect, 8)
                    End Using
                    TextRenderer.DrawText(g, bb.Text, bb.Font, bb.ClientRectangle, bb.ForeColor,
                        TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
                End Sub

            AddHandler b.MouseEnter, Sub() If idx <> _activeFilterIdx Then styleBtn(False, True)
            AddHandler b.MouseLeave, Sub() If idx <> _activeFilterIdx Then styleBtn(False, False)
            AddHandler b.Click,
                Sub()
                    If _filterBtns(_activeFilterIdx) IsNot Nothing Then
                        _filterBtns(_activeFilterIdx).BackColor = Color.FromArgb(220, 235, 232)
                        _filterBtns(_activeFilterIdx).ForeColor = SubText
                        _filterBtns(_activeFilterIdx).Font = New Font("Segoe UI", 9.5F)
                        _filterBtns(_activeFilterIdx).Invalidate()
                    End If
                    LoadPayables(_container, capTab)
                End Sub

            styleBtn(i = _activeFilterIdx, False)
            _filterBtns(i) = b
            filterHost.Controls.Add(b)
        Next

        pillBar.Controls.Add(filterHost)

        ' ── Row 3: KPI cards (165px) ─────────────────────────────────────────
        Dim kpiBar As Panel = BuildKpiBar()
        kpiBar.Dock = DockStyle.Top

        region.Controls.Add(kpiBar)
        region.Controls.Add(pillBar)
        region.Controls.Add(titleBar)

        region.Height = 72 + 52 + 165
        Return region
    End Function

    ' =========================================================================
    '  KPI BAR
    ' =========================================================================
    Private Shared Function BuildKpiBar() As Panel
        Const SIDE_PAD As Integer = 24

        ' FIX (original BC32016 bug): use .Where().Count() not .Count(lambda)
        Dim overdueCount As Integer = _rows.Where(
            Function(r) r.HasExpected AndAlso
                        r.ExpectedDate.Date < DateTime.Today AndAlso
                        r.Status <> "Received").Count()

        Dim orderSub As String = If(overdueCount > 0,
            overdueCount.ToString() & " overdue",
            "All within expected delivery")

        Dim bar As New Panel With {
            .Height = 165,
            .BackColor = Color.Transparent,
            .Padding = New Padding(SIDE_PAD, 16, SIDE_PAD, 0)
        }

        Dim kpiRow As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .ColumnCount = 2,
            .RowCount = 1,
            .BackColor = Color.Transparent,
            .CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        }
        kpiRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50.0F))
        kpiRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50.0F))
        kpiRow.RowStyles.Add(New RowStyle(SizeType.Percent, 100))

        Dim c1 = MakeKpiCard("Total Payable",
                              Peso & _totalPayable.ToString("N2"),
                              AccentOrange, "Outstanding purchase order value")
        Dim c2 = MakeKpiCard("Outstanding Orders",
                              _rows.Count.ToString() & If(_rows.Count = 1, " order", " orders"),
                              MintDeep, orderSub)

        c1.Margin = New Padding(0, 0, 6, 0)
        c2.Margin = New Padding(6, 0, 0, 0)

        kpiRow.Controls.Add(c1, 0, 0)
        kpiRow.Controls.Add(c2, 1, 0)

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
    '  COLUMN HEADER
    ' =========================================================================
    Private Shared Function BuildColHeader() As Panel
        Dim hdr As New Panel With {.Height = 44, .BackColor = Color.Transparent}

        Dim labels As Label() = {
            MakeColLabel("PO NO", New Point(24, 13)),
            MakeColLabel("SUPPLIER", New Point(0, 13)),
            MakeColLabel("ORDER DATE", New Point(0, 13)),
            MakeColLabel("EXPECTED", New Point(0, 13)),
            MakeColLabel("STATUS", New Point(0, 13)),
            MakeColLabel("AMOUNT", New Point(0, 13), AnchorStyles.Top Or AnchorStyles.Right)
        }
        hdr.Controls.AddRange(labels)

        AddHandler hdr.Resize,
            Sub()
                Dim w = hdr.Width
                labels(0).Left = 24
                labels(1).Left = CInt(w * 0.18)
                labels(2).Left = CInt(w * 0.38)
                labels(3).Left = CInt(w * 0.52)
                labels(4).Left = CInt(w * 0.66)
                labels(5).Left = w - labels(5).Width - 24
            End Sub

        AddHandler hdr.Paint,
            Sub(s, e)
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 20, hdr.Height - 1, hdr.Width - 20, hdr.Height - 1)
                End Using
            End Sub

        Return hdr
    End Function

    Private Shared Function MakeColLabel(text As String, loc As Point,
                                          Optional anchor As AnchorStyles = AnchorStyles.Top Or AnchorStyles.Left) As Label
        Return New Label With {
            .Text = text,
            .Font = New Font("Segoe UI", 10, FontStyle.Bold),
            .ForeColor = SubText,
            .AutoSize = True,
            .Location = loc,
            .BackColor = Color.Transparent,
            .Anchor = anchor
        }
    End Function

    ' =========================================================================
    '  FIX 4: PAGER BAR
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
        ' Re-layout so button positions recalculate around new label width
        If _lblPageInfo.Parent IsNot Nothing Then
            _lblPageInfo.Parent.PerformLayout()
        End If
    End Sub

    ' =========================================================================
    '  POPULATE ROWS  (FIX 4: only renders current page slice)
    ' =========================================================================
    Private Shared Sub PopulateRows()
        _rowsHost.Controls.Clear()

        ' FIX 5: show DB error visibly if load failed
        If _loadError <> "" Then
            Dim errPanel As New Panel With {
                .Height = 80, .Dock = DockStyle.Top, .BackColor = Color.FromArgb(255, 240, 240)
            }
            Dim errIcon As New Label With {
                .Text = "⚠",
                .Font = New Font("Segoe UI", 14),
                .ForeColor = AccentRed,
                .AutoSize = True,
                .Location = New Point(24, 22),
                .BackColor = Color.Transparent
            }
            Dim errLbl As New Label With {
                .Text = _loadError,
                .Font = New Font("Segoe UI", 10),
                .ForeColor = AccentRed,
                .AutoSize = True,
                .Location = New Point(50, 26),
                .BackColor = Color.Transparent
            }
            errPanel.Controls.AddRange({errIcon, errLbl})
            _rowsHost.Controls.Add(errPanel)
            UpdatePager()
            Return
        End If

        If _rows.Count = 0 Then
            _rowsHost.Controls.Add(BuildEmptyRow("No outstanding payables found."))
            UpdatePager()
            Return
        End If

        ' FIX 4: slice current page
        Dim startIdx As Integer = _currentPage * PAGE_SIZE
        Dim pageRows = _rows.Skip(startIdx).Take(PAGE_SIZE).ToList()

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
    '  PAYABLE ROW  (FIX 1: row click, FIX 3: overdue tag positioning)
    ' =========================================================================
    Private Shared Function BuildPayableRow(row As PayableRow, height As Integer) As Panel
        Dim isOverdue As Boolean = row.HasExpected AndAlso
                                   row.ExpectedDate.Date < DateTime.Today AndAlso
                                   row.Status <> "Received"

        Dim accentColor As Color = If(isOverdue, AccentRed, AccentOrange)

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
                Using br As New SolidBrush(accentColor)
                    e.Graphics.FillRectangle(br, New Rectangle(0, 12, 3, height - 24))
                End Using
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 20, p.Height - 1, p.Width - 20, p.Height - 1)
                End Using
            End Sub

        Dim lblPo As New Label With {
    .Text = row.PoNo,
    .Font = New Font("Segoe UI", 11, FontStyle.Bold),
    .ForeColor = MintDark, .AutoSize = True, .BackColor = Color.Transparent
}
        Dim lblSupplier As New Label With {
            .Text = row.Supplier,
            .Font = New Font("Segoe UI", 11),
            .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblOrderDate As New Label With {
    .Text = row.OrderDate.ToString("MMM dd, yyyy"),
    .Font = New Font("Segoe UI", 11),
    .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent
}

        Dim expectedText As String = If(row.HasExpected, row.ExpectedDate.ToString("MMM dd, yyyy"), "—")
        Dim lblExpected As New Label With {
            .Text = expectedText,
            .Font = New Font("Segoe UI", 11, If(isOverdue, FontStyle.Bold, FontStyle.Regular)),
            .ForeColor = If(isOverdue, AccentRed, FaintText),
            .AutoSize = True, .BackColor = Color.Transparent
        }

        Dim badgeInfo = GetStatusBadge(row.Status)
        Dim badge As New Label With {
            .Text = row.Status,
            .Font = New Font("Segoe UI", 9, FontStyle.Bold),
            .ForeColor = badgeInfo.fore,
            .BackColor = badgeInfo.back,
            .AutoSize = True,
            .Padding = New Padding(10, 4, 10, 4)
        }
        RoundLabel(badge, 10)

        Dim lblAmount As New Label With {
            .Text = Peso & row.Amount.ToString("N2"),
            .Font = New Font("Segoe UI", 12, FontStyle.Bold),
            .ForeColor = AccentOrange,
            .AutoSize = True, .BackColor = Color.Transparent,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }

        p.Controls.AddRange({lblPo, lblSupplier, lblOrderDate, lblExpected, badge, lblAmount})

        ' FIX 3: overdue tag — position AFTER badge is laid out via HandleCreated
        Dim overdueTag As Label = Nothing
        If isOverdue Then
            overdueTag = New Label With {
                .Text = "OVERDUE",
                .Font = New Font("Segoe UI", 7.5F, FontStyle.Bold),
                .ForeColor = Color.White,
                .BackColor = AccentRed,
                .AutoSize = True,
                .Padding = New Padding(5, 2, 5, 2),
                .Visible = False   ' hidden until positioned
            }
            RoundLabel(overdueTag, 8)
            p.Controls.Add(overdueTag)
        End If

        AddHandler p.Resize,
            Sub()
                Dim w = p.Width
                lblPo.Left = 24 : lblPo.Top = (height - lblPo.Height) \ 2
                lblSupplier.Left = CInt(w * 0.18) : lblSupplier.Top = (height - lblSupplier.Height) \ 2
                lblOrderDate.Left = CInt(w * 0.38) : lblOrderDate.Top = (height - lblOrderDate.Height) \ 2
                lblExpected.Left = CInt(w * 0.52) : lblExpected.Top = (height - lblExpected.Height) \ 2
                badge.Left = CInt(w * 0.66) : badge.Top = (height \ 2) - badge.Height - 2

                lblAmount.Left = w - lblAmount.Width - 24 : lblAmount.Top = (height - lblAmount.Height) \ 2

                ' FIX 3: position overdue tag below badge now that badge.Height is known
                If overdueTag IsNot Nothing Then
                    overdueTag.Left = CInt(w * 0.66)
                    overdueTag.Top = (height \ 2) + 2
                    overdueTag.Visible = True
                End If
            End Sub

        ' FIX 1: row click — opens PO detail
        ' A Received PO is a debt that can be settled, so its detail offers that
        ' as a Yes/No instead of adding another button to the row. Everything
        ' else keeps the plain OK it always had.
        Dim openDetail As Action =
    Sub()
        Dim sb As New System.Text.StringBuilder()
        sb.AppendLine($"Supplier  : {row.Supplier}")
        sb.AppendLine($"Order Date: {row.OrderDate:MMM dd, yyyy}")
        sb.AppendLine($"Expected  : {If(row.HasExpected, row.ExpectedDate.ToString("MMM dd, yyyy"), "N/A")}")
        sb.AppendLine($"Status    : {row.Status}")
        sb.AppendLine($"Amount    : {Peso}{row.Amount:N2}")
        If isOverdue Then sb.AppendLine(vbCrLf & "⚠ This order is OVERDUE.")

        Dim canSettle As Boolean = _hasPaidAt AndAlso row.Status = "Received"
        If canSettle Then
            sb.AppendLine()
            sb.AppendLine("This order has been received in full, so the amount is owed")
            sb.AppendLine("to the supplier. Mark it as paid and clear it from payables?")

            If AppModal.Show(sb.ToString(),
                             caption:=$"PO Details — {row.PoNo}",
                             buttons:=MessageBoxButtons.YesNo,
                             icon:=MessageBoxIcon.Question) = DialogResult.Yes Then
                MarkPaid(row)
            End If
        Else
            AppModal.Show(sb.ToString(),
                          caption:=$"PO Details — {row.PoNo}",
                          buttons:=MessageBoxButtons.OK,
                          icon:=If(isOverdue, MessageBoxIcon.Warning, MessageBoxIcon.Information))
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
    Private Shared Function GetStatusBadge(status As String) As (fore As Color, back As Color)
        Select Case status
            Case "Draft" : Return (Color.FromArgb(120, 120, 120), Color.FromArgb(238, 238, 238))
            Case "Ordered" : Return (AccentOrange, Color.FromArgb(255, 240, 220))
            Case "Partially Received" : Return (AccentBlue, Color.FromArgb(220, 235, 255))
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