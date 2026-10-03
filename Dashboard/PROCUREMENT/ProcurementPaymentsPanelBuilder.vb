Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Globalization
Imports System.Windows.Forms
Imports MySql.Data.MySqlClient

' =============================================================================
'  ProcurementPaymentsPanelBuilder  —  Procurement ▸ Payment Records
'
'  Every payment made to a supplier, newest first: payment no., the PO it was
'  for, the supplier's invoice, the amount, and the PO's payment status now
'  (Partial while a balance is still owed, Paid once settled). The Accountant
'  sees and records the same payments in Payables -- one shared record.
'
'    + Record Payment   SupplierPaymentModal.ShowNew: pick the PO, enter the
'                       invoice and amount (part payments allowed, never more
'                       than the balance).
'    Row click          the PO with its items and payment status.
'    Save as PDF/Print  the filtered list with the report signature block.
'
'  "Still Owed" is across ALL dates, so an old unpaid order is never out of
'  sight because of the date filter. Same frame as Procurement ▸ Reports.
' =============================================================================
Public Class ProcurementPaymentsPanelBuilder

    ' ── PALETTE ───────────────────────────────────────────────────────────────
    Private Shared ReadOnly MintDark As Color = Theme.Dark
    Private Shared ReadOnly MintPrimary As Color = Theme.Primary
    Private Shared ReadOnly MintDeep As Color = Theme.Deep
    Private Shared ReadOnly MintBg As Color = Theme.PageBg
    Private Shared ReadOnly CardBg As Color = Color.White
    Private Shared ReadOnly SubText As Color = Theme.SubText
    Private Shared ReadOnly FaintText As Color = Theme.FaintText
    Private Shared ReadOnly RowDivider As Color = Theme.RowDivider
    Private Shared ReadOnly RowHover As Color = Theme.RowHover
    Private Shared ReadOnly AccentAmber As Color = Theme.Warning
    Private Shared ReadOnly Peso As String = ChrW(8369)

    Private Const SEARCH_PLACEHOLDER As String = "Search payment no., P.O., supplier, invoice…"
    Private Const SIDE_PAD As Integer = 24
    Private Const ROW_H As Integer = 62
    Private Const SUMMARY_H As Integer = 140
    Private Const MAX_PAGE_SIZE As Integer = 50
    Private Const EXPORT_LIMIT As Integer = 5000
    Private Const CHROME_H As Integer = 422

    Private Const X_PAY As Single = 0.12F
    Private Const X_PO As Single = 0.22F
    Private Const X_SUPPLIER As Single = 0.34F
    Private Const X_INVOICE As Single = 0.54F
    Private Const X_STATUS As Single = 0.7F

    Private Const ALL_STATUSES As String = "All statuses"

    Private Class PayRow
        Public PaymentId As Integer
        Public PoId As Integer
        Public PaidOn As Date
        Public PoNo As String = ""
        Public Supplier As String = ""
        Public InvoiceNo As String = ""
        Public InvoiceDate As Date?
        Public Reference As String = ""
        Public Method As String = ""
        Public Amount As Decimal
        Public PoPaid As Boolean
        Public RecordedBy As String = ""
    End Class

    ' ── STATE ─────────────────────────────────────────────────────────────────
    Private Shared _container As Panel
    Private Shared _scrollHost As Panel
    Private Shared _fromDate As Date
    Private Shared _toDate As Date
    Private Shared _search As String = ""
    Private Shared _status As String = ""
    Private Shared _rows As New List(Of PayRow)
    Private Shared _pageSize As Integer = MAX_PAGE_SIZE
    Private Shared _currentPage As Integer = 0
    Private Shared _totalRows As Integer = 0
    Private Shared _sumPaid As Decimal
    Private Shared _owed As Decimal
    Private Shared _owedCount, _partialCount As Integer
    Private Shared _hasInvoice As Boolean
    Private Shared _reflowAttached As Boolean = False
    Private Shared _loadFailed As Boolean = False

    Private Shared _lblCount, _lblPaid, _lblOwed, _lblPartial As Label
    Private Shared _subOwed As Label
    Private Shared _lblPageInfo As Label
    Private Shared _btnPrev, _btnNext As Button
    Private Shared _searchTimer As Timer

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Shared Sub LoadPayments(container As Panel)
        _container = container
        _toDate = Date.Today
        _fromDate = New Date(Date.Today.Year, Date.Today.Month, 1)   ' this month so far
        _search = ""
        _status = ""
        _currentPage = 0

        ComputePageSize()
        LoadData()
        Render()
    End Sub

    Private Shared Sub ComputePageSize()
        _pageSize = MAX_PAGE_SIZE
        If _container Is Nothing Then Return
        Dim viewport As Integer = _container.ClientSize.Height - CHROME_H
        If viewport < 120 Then Return
        _pageSize = Math.Max(4, Math.Min(MAX_PAGE_SIZE, (viewport - 4) \ ROW_H))
    End Sub

    Private Shared Sub ReQuery(Optional resetPage As Boolean = True)
        If resetPage Then _currentPage = 0
        LoadData()
        PopulateRows()
        UpdateSummaryLabels()
        UpdatePager()
    End Sub

    ' =========================================================================
    '  DATA
    ' =========================================================================
    Private Const FROM_SQL As String =
        "FROM supplier_payments sp " &
        "JOIN purchase_orders po ON po.po_id = sp.po_id " &
        "JOIN suppliers s ON s.supplier_id = po.supplier_id " &
        "LEFT JOIN users u ON u.user_id = sp.recorded_by "

    Private Shared Function InvoiceSql() As String
        Return If(_hasInvoice, "COALESCE(sp.invoice_no, sp.reference_no, '')", "COALESCE(sp.reference_no, '')")
    End Function

    Private Shared Function WhereClause(includeStatus As Boolean) As String
        Dim sb As New System.Text.StringBuilder("WHERE sp.paid_on >= @fromD AND sp.paid_on < @toD ")
        If includeStatus AndAlso _status = SupplierPayments.STATUS_PAID Then sb.Append("AND po.paid_at IS NOT NULL ")
        If includeStatus AndAlso _status = SupplierPayments.STATUS_PARTIAL Then sb.Append("AND po.paid_at IS NULL ")
        If _search <> "" Then
            sb.Append("AND (CONCAT('PAY-', LPAD(sp.payment_id, 4, '0')) LIKE @q OR po.po_no LIKE @q " &
                      "OR s.name LIKE @q OR " & InvoiceSql() & " LIKE @q " &
                      "OR COALESCE(sp.reference_no, '') LIKE @q OR COALESCE(sp.remarks, '') LIKE @q) ")
        End If
        Return sb.ToString()
    End Function

    Private Shared Sub AddParams(cmd As MySqlCommand)
        cmd.Parameters.AddWithValue("@fromD", _fromDate.Date)
        cmd.Parameters.AddWithValue("@toD", _toDate.Date.AddDays(1))
        cmd.Parameters.AddWithValue("@q", "%" & _search & "%")
    End Sub

    Private Shared Function SelectSql() As String
        Return "SELECT sp.payment_id, sp.po_id, sp.paid_on, sp.amount_paid, sp.method, " &
               InvoiceSql() & " AS invoice_no, " &
               If(_hasInvoice, "sp.invoice_date", "NULL") & " AS invoice_date, " &
               If(_hasInvoice, "COALESCE(sp.reference_no, '')", "''") & " AS reference, " &
               "po.po_no, s.name AS sname, (po.paid_at IS NOT NULL) AS po_paid, " &
               "COALESCE(u.full_name, '') AS recby " & FROM_SQL
    End Function

    Private Shared Function ReadRows(cmd As MySqlCommand) As List(Of PayRow)
        Dim result As New List(Of PayRow)
        Using rdr = cmd.ExecuteReader()
            While rdr.Read()
                result.Add(New PayRow With {
                    .PaymentId = SafeInt(rdr("payment_id")),
                    .PoId = SafeInt(rdr("po_id")),
                    .PaidOn = Convert.ToDateTime(rdr("paid_on")),
                    .Amount = SafeDec(rdr("amount_paid")),
                    .Method = rdr("method").ToString(),
                    .InvoiceNo = rdr("invoice_no").ToString(),
                    .InvoiceDate = If(IsDBNull(rdr("invoice_date")), CType(Nothing, Date?), Convert.ToDateTime(rdr("invoice_date"))),
                    .Reference = rdr("reference").ToString(),
                    .PoNo = rdr("po_no").ToString(),
                    .Supplier = rdr("sname").ToString(),
                    .PoPaid = SafeInt(rdr("po_paid")) = 1,
                    .RecordedBy = rdr("recby").ToString()
                })
            End While
        End Using
        Return result
    End Function

    Private Shared Sub LoadData()
        _rows.Clear()
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                _hasInvoice = SupplierPayments.HasInvoiceColumns(conn)

                Using cmd As New MySqlCommand(
                    "SELECT COALESCE(SUM(sp.amount_paid), 0) " & FROM_SQL & WhereClause(False), conn)
                    AddParams(cmd)
                    _sumPaid = SafeDec(cmd.ExecuteScalar())
                End Using

                ' Owed on every placed, not-yet-settled order, whatever the dates.
                Using cmd As New MySqlCommand(
                    "SELECT COUNT(*) AS n, " &
                    "COALESCE(SUM(po.total_cost - COALESCE(p.paid, 0)), 0) AS owed, " &
                    "SUM(COALESCE(p.paid, 0) > 0) AS n_partial " &
                    "FROM purchase_orders po " &
                    "LEFT JOIN (SELECT po_id, SUM(amount_paid) AS paid FROM supplier_payments GROUP BY po_id) p " &
                    "  ON p.po_id = po.po_id " &
                    "WHERE po.status IN ('Ordered','Partially Received','Received') AND po.paid_at IS NULL", conn)
                    Using rdr = cmd.ExecuteReader()
                        If rdr.Read() Then
                            _owedCount = SafeInt(rdr("n"))
                            _owed = SafeDec(rdr("owed"))
                            _partialCount = SafeInt(rdr("n_partial"))
                        End If
                    End Using
                End Using

                Using cmd As New MySqlCommand("SELECT COUNT(*) " & FROM_SQL & WhereClause(True), conn)
                    AddParams(cmd)
                    _totalRows = Convert.ToInt32(cmd.ExecuteScalar())
                End Using

                Dim totalPages As Integer = Math.Max(1, CInt(Math.Ceiling(_totalRows / _pageSize)))
                If _currentPage > totalPages - 1 Then _currentPage = totalPages - 1
                If _currentPage < 0 Then _currentPage = 0

                Using cmd As New MySqlCommand(
                    SelectSql() & WhereClause(True) &
                    "ORDER BY sp.paid_on DESC, sp.payment_id DESC LIMIT @lim OFFSET @off", conn)
                    AddParams(cmd)
                    cmd.Parameters.AddWithValue("@lim", _pageSize)
                    cmd.Parameters.AddWithValue("@off", _currentPage * _pageSize)
                    _rows = ReadRows(cmd)
                End Using
            End Using
            _loadFailed = False
        Catch ex As Exception
            _totalRows = 0 : _sumPaid = 0D : _owed = 0D : _owedCount = 0 : _partialCount = 0
            If Not _loadFailed Then
                AppModal.Show("Could not load payment records:" & Environment.NewLine & ErrorLog.ForUser(ex),
                              "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End If
            _loadFailed = True
        End Try
    End Sub

    Private Shared Function LoadAllForExport() As List(Of PayRow)
        Using conn = Db.GetConnection()
            conn.Open()
            _hasInvoice = SupplierPayments.HasInvoiceColumns(conn)
            Using cmd As New MySqlCommand(
                SelectSql() & WhereClause(True) &
                "ORDER BY sp.paid_on DESC, sp.payment_id DESC LIMIT " & EXPORT_LIMIT, conn)
                AddParams(cmd)
                Return ReadRows(cmd)
            End Using
        End Using
    End Function

    Private Shared Sub UpdateSummaryLabels()
        If _lblCount IsNot Nothing Then _lblCount.Text = _totalRows.ToString("N0")
        If _lblPaid IsNot Nothing Then _lblPaid.Text = Money(_sumPaid)
        If _lblOwed IsNot Nothing Then _lblOwed.Text = Money(_owed)
        If _subOwed IsNot Nothing Then
            _subOwed.Text = _owedCount.ToString("N0") & If(_owedCount = 1, " order", " orders") & " not fully paid"
        End If
        If _lblPartial IsNot Nothing Then _lblPartial.Text = _partialCount.ToString("N0")
    End Sub

    ' =========================================================================
    '  RENDER
    ' =========================================================================
    Private Shared Sub Render()
        _container.SuspendLayout()
        _container.Controls.Clear()
        _reflowAttached = False
        _container.BackColor = MintBg

        Dim tableWrapper As New Panel With {.Dock = DockStyle.Fill, .BackColor = MintBg}
        Dim listArea As New BufferedPanel With {
            .Dock = DockStyle.Fill, .BackColor = CardBg, .Padding = New Padding(1)
        }
        AddHandler listArea.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)

        _scrollHost = New Panel With {
            .Dock = DockStyle.Fill, .BackColor = Color.Transparent, .AutoScroll = True
        }
        Dim colHdr As Panel = BuildColHeader()
        colHdr.Dock = DockStyle.Top
        Dim pagerBar As Panel = BuildPagerBar()
        pagerBar.Dock = DockStyle.Bottom
        Dim bottomPad As New Panel With {.Dock = DockStyle.Bottom, .Height = 12, .BackColor = Color.Transparent}

        listArea.Controls.Add(bottomPad)
        listArea.Controls.Add(pagerBar)
        listArea.Controls.Add(colHdr)
        listArea.Controls.Add(_scrollHost)
        colHdr.BringToFront()
        _scrollHost.BringToFront()
        _scrollHost.AutoScrollMinSize = New Size(0, 1)

        tableWrapper.Controls.Add(listArea)
        tableWrapper.Controls.Add(New Panel With {.Dock = DockStyle.Bottom, .Height = 16, .BackColor = MintBg})
        tableWrapper.Controls.Add(New Panel With {.Dock = DockStyle.Top, .Height = 16, .BackColor = MintBg})
        tableWrapper.Controls.Add(New Panel With {.Dock = DockStyle.Left, .Width = SIDE_PAD, .BackColor = MintBg})
        tableWrapper.Controls.Add(New Panel With {.Dock = DockStyle.Right, .Width = SIDE_PAD, .BackColor = MintBg})

        Dim summary As Panel = BuildSummaryStrip()
        summary.Dock = DockStyle.Top
        Dim topRegion As Panel = BuildTopRegion()
        topRegion.Dock = DockStyle.Top

        _container.Controls.Add(tableWrapper)
        _container.Controls.Add(summary)
        _container.Controls.Add(topRegion)
        _container.ResumeLayout()

        PopulateRows()
        UpdateSummaryLabels()
        UpdatePager()
    End Sub

    Private Shared Function BuildTopRegion() As Panel
        Dim region As New Panel With {.BackColor = MintBg, .Height = 128}

        Dim titleBar As New Panel With {.Height = 72, .Dock = DockStyle.Top, .BackColor = Color.Transparent}
        titleBar.Controls.Add(New Label With {
            .Text = "Payment Records",
            .Font = New Font("Segoe UI", 28, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True,
            .Location = New Point(SIDE_PAD, 16)
        })

        Dim btnPrint As Button = MakeActionButton("Print", 90, MintDark, MintDeep)
        Dim btnPdf As Button = MakeActionButton("Save as PDF", 130, MintDark, MintDeep)
        Dim btnNew As Button = MakeActionButton("+  Record Payment", 170, MintPrimary, MintDeep)
        AddHandler btnPrint.Click, Sub() PrintReport()
        AddHandler btnPdf.Click, Sub() SaveReport(btnPdf)
        AddHandler btnNew.Click,
            Sub()
                If SupplierPaymentModal.ShowNew() Then ReQuery()
            End Sub
        AddHandler titleBar.Resize,
            Sub()
                Dim cy As Integer = (titleBar.Height - btnPrint.Height) \ 2 + 2
                btnPrint.Location = New Point(titleBar.Width - btnPrint.Width - SIDE_PAD, cy)
                btnPdf.Location = New Point(btnPrint.Left - btnPdf.Width - 10, cy)
                btnNew.Location = New Point(btnPdf.Left - btnNew.Width - 10, cy)
            End Sub
        titleBar.Controls.AddRange({btnNew, btnPdf, btnPrint})

        ' ── Filter bar ──────────────────────────────────────────────────────
        Dim filterBar As New Panel With {.Height = 56, .Dock = DockStyle.Top, .BackColor = Color.Transparent}
        Dim x As Integer = SIDE_PAD
        Const TOP_Y As Integer = 11

        Dim searchWrap As Panel = BuildSearchBox()
        searchWrap.Location = New Point(x, TOP_Y)
        filterBar.Controls.Add(searchWrap)
        x += searchWrap.Width + 24

        Dim addLabel As Action(Of String) =
            Sub(text As String)
                Dim l As New Label With {
                    .Text = text, .Font = New Font("Segoe UI", 9.5F), .ForeColor = SubText,
                    .AutoSize = True, .BackColor = Color.Transparent
                }
                l.Location = New Point(x, TOP_Y + (FilterFields.FIELD_H - l.PreferredHeight) \ 2)
                filterBar.Controls.Add(l)
                x += l.PreferredWidth + 8
            End Sub

        addLabel("From")
        Dim setFrom As Action(Of Date) = Nothing, setTo As Action(Of Date) = Nothing
        Dim boxFrom As Panel = FilterFields.MakeDateField(136, _fromDate,
            Sub(d As Date)
                _fromDate = d.Date
                If _fromDate > _toDate Then
                    _toDate = _fromDate
                    setTo(_toDate)
                End If
                ReQuery()
            End Sub, setFrom)
        boxFrom.Location = New Point(x, TOP_Y)
        filterBar.Controls.Add(boxFrom)
        x += boxFrom.Width + 16

        addLabel("To")
        Dim boxTo As Panel = FilterFields.MakeDateField(136, _toDate,
            Sub(d As Date)
                _toDate = d.Date
                If _toDate < _fromDate Then
                    _fromDate = _toDate
                    setFrom(_fromDate)
                End If
                ReQuery()
            End Sub, setTo)
        boxTo.Location = New Point(x, TOP_Y)
        filterBar.Controls.Add(boxTo)
        x += boxTo.Width + 24

        addLabel("PO Status")
        Dim names As New List(Of String) From {ALL_STATUSES, SupplierPayments.STATUS_PARTIAL, SupplierPayments.STATUS_PAID}
        Dim boxStatus As Panel = FilterFields.MakeSelectField(names, 150, 0,
            Sub(i As Integer)
                _status = If(i > 0 AndAlso i < names.Count, names(i), "")
                ReQuery()
            End Sub)
        boxStatus.Location = New Point(x, TOP_Y)
        filterBar.Controls.Add(boxStatus)

        region.Controls.Add(filterBar)
        region.Controls.Add(titleBar)
        Return region
    End Function

    Private Shared Function BuildSearchBox() As Panel
        Dim searchWrap As New Panel With {.Size = New Size(300, FilterFields.FIELD_H), .BackColor = CardBg, .Cursor = Cursors.IBeam}
        Dim focused As Boolean = False
        AddHandler searchWrap.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim rc = searchWrap.ClientRectangle : rc.Inflate(-1, -1)
                Using b As New SolidBrush(CardBg)
                    e.Graphics.FillRoundedRectangle(b, rc, 8)
                End Using
                Using p As New Pen(If(focused, MintPrimary, Theme.Border), If(focused, 1.8F, 1.0F))
                    e.Graphics.DrawRoundedRectangle(p, rc, 8)
                End Using
                Using p As New Pen(Color.FromArgb(160, 180, 178), 1.4F)
                    e.Graphics.DrawEllipse(p, 10, 10, 12, 12)
                    e.Graphics.DrawLine(p, 20, 20, 25, 25)
                End Using
            End Sub

        Dim txt As New TextBox With {
            .Font = New Font("Segoe UI", 10), .BorderStyle = BorderStyle.None,
            .BackColor = CardBg, .ForeColor = SubText, .Text = SEARCH_PLACEHOLDER,
            .Location = New Point(34, 9), .Width = 254
        }

        If _searchTimer IsNot Nothing Then
            _searchTimer.Stop()
            _searchTimer.Dispose()
        End If
        _searchTimer = New Timer With {.Interval = 350}
        AddHandler _searchTimer.Tick,
            Sub()
                _searchTimer.Stop()
                ReQuery()
            End Sub

        AddHandler txt.Enter,
            Sub()
                focused = True
                If txt.Text = SEARCH_PLACEHOLDER Then
                    txt.Text = ""
                    txt.ForeColor = Theme.TextDark
                End If
                searchWrap.Invalidate()
            End Sub
        AddHandler txt.Leave,
            Sub()
                focused = False
                If String.IsNullOrWhiteSpace(txt.Text) Then
                    txt.Text = SEARCH_PLACEHOLDER
                    txt.ForeColor = SubText
                End If
                searchWrap.Invalidate()
            End Sub
        AddHandler txt.TextChanged,
            Sub()
                If txt.Text = SEARCH_PLACEHOLDER Then Return
                _search = txt.Text.Trim()
                _searchTimer.Stop()
                _searchTimer.Start()
            End Sub
        AddHandler searchWrap.Click, Sub() txt.Focus()
        searchWrap.Controls.Add(txt)
        Return searchWrap
    End Function

    ' =========================================================================
    '  SUMMARY STRIP
    ' =========================================================================
    Private Shared Function BuildSummaryStrip() As Panel
        Dim bar As New Panel With {
            .Height = SUMMARY_H + 12, .BackColor = MintBg,
            .Padding = New Padding(SIDE_PAD, 0, SIDE_PAD, 12)
        }
        Dim grid As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 4, .RowCount = 1,
            .BackColor = Color.Transparent
        }
        For i As Integer = 0 To 3
            grid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 25.0F))
        Next
        grid.RowStyles.Add(New RowStyle(SizeType.Percent, 100))

        Dim dummy As Label = Nothing
        Dim c1 = MakeSummaryCard("Payments", MintDark, "in this period", _lblCount, dummy)
        Dim c2 = MakeSummaryCard("Amount Paid", MintDeep, "to suppliers in this period", _lblPaid, dummy)
        Dim c3 = MakeSummaryCard("Still Owed", AccentAmber, "", _lblOwed, _subOwed)
        Dim c4 = MakeSummaryCard("Partly Paid Orders", AccentAmber, "a balance is still owed", _lblPartial, dummy)
        c1.Margin = New Padding(0, 0, 12, 0)
        c2.Margin = New Padding(0, 0, 12, 0)
        c3.Margin = New Padding(0, 0, 12, 0)
        c4.Margin = New Padding(0)
        grid.Controls.Add(c1, 0, 0)
        grid.Controls.Add(c2, 1, 0)
        grid.Controls.Add(c3, 2, 0)
        grid.Controls.Add(c4, 3, 0)
        bar.Controls.Add(grid)
        Return bar
    End Function

    Private Shared Function MakeSummaryCard(caption As String, valueClr As Color, subLabel As String,
                                            ByRef valueLabel As Label, ByRef subLabelCtl As Label) As Panel
        Dim card As New BufferedPanel With {.Dock = DockStyle.Fill, .BackColor = CardBg}
        AddHandler card.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)
        card.Controls.Add(New Label With {
            .Text = caption, .Font = New Font("Segoe UI", 11), .ForeColor = SubText,
            .AutoSize = True, .Location = New Point(22, 14), .BackColor = Color.Transparent
        })
        valueLabel = New Label With {
            .Text = "0", .Font = New Font("Segoe UI", 24, FontStyle.Bold), .ForeColor = valueClr,
            .AutoSize = True, .Location = New Point(20, 38), .BackColor = Color.Transparent
        }
        card.Controls.Add(valueLabel)
        Dim lblSub As New Label With {
            .Text = subLabel, .Font = New Font("Segoe UI", 10), .ForeColor = SubText,
            .AutoSize = False, .AutoEllipsis = True, .Height = 22,
            .Location = New Point(22, 100), .BackColor = Color.Transparent
        }
        card.Controls.Add(lblSub)
        subLabelCtl = lblSub
        AddHandler card.Resize, Sub() lblSub.Width = Math.Max(40, card.Width - 36)
        Return card
    End Function

    ' =========================================================================
    '  COLUMN HEADER + ROWS
    ' =========================================================================
    Private Shared Function BuildColHeader() As Panel
        Dim hdr As New Panel With {.Height = 44, .BackColor = Color.Transparent}
        Dim labels As Label() = {
            MakeColLabel("PAID ON"), MakeColLabel("PAYMENT NO."), MakeColLabel("PO NO."),
            MakeColLabel("SUPPLIER"), MakeColLabel("INVOICE NO."), MakeColLabel("PO STATUS"),
            MakeColLabel("AMOUNT")
        }
        hdr.Controls.AddRange(labels)
        AddHandler hdr.Resize,
            Sub()
                Dim w = hdr.Width
                labels(0).Left = 24
                labels(1).Left = CInt(w * X_PAY)
                labels(2).Left = CInt(w * X_PO)
                labels(3).Left = CInt(w * X_SUPPLIER)
                labels(4).Left = CInt(w * X_INVOICE)
                labels(5).Left = CInt(w * X_STATUS)
                labels(6).Left = w - labels(6).Width - 28
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
            .Text = text, .Font = New Font("Segoe UI", 10, FontStyle.Bold), .ForeColor = SubText,
            .AutoSize = True, .Location = New Point(0, 13), .BackColor = Color.Transparent
        }
    End Function

    Private Shared Sub PopulateRows()
        If _scrollHost Is Nothing Then Return
        _scrollHost.SuspendLayout()
        For Each c As Control In _scrollHost.Controls.Cast(Of Control).ToList()
            c.Dispose()
        Next
        _scrollHost.Controls.Clear()

        If _reflowAttached Then
            RemoveHandler _scrollHost.Resize, AddressOf ReflowScrollHost
            _reflowAttached = False
        End If

        If _rows.Count = 0 Then
            _scrollHost.Controls.Add(BuildEmptyRow(
                If(_loadFailed, "The payment records could not be loaded.",
                   "No payments in this period. Use + Record Payment to add one.")))
        Else
            Dim y As Integer = 4
            For Each r In _rows
                Dim row As Panel = BuildDataRow(r)
                row.Location = New Point(0, y)
                row.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
                _scrollHost.Controls.Add(row)
                row.Width = _scrollHost.ClientSize.Width - 4
                y += ROW_H
            Next
            _reflowAttached = True
            AddHandler _scrollHost.Resize, AddressOf ReflowScrollHost
        End If

        _scrollHost.ResumeLayout()
        _scrollHost.Invalidate(True)
    End Sub

    Private Shared Sub ReflowScrollHost(sender As Object, e As EventArgs)
        If _scrollHost Is Nothing Then Return
        Dim w As Integer = _scrollHost.ClientSize.Width - 4
        For Each c As Control In _scrollHost.Controls
            c.Width = w
        Next
    End Sub

    Private Shared Function BuildDataRow(r As PayRow) As Panel
        Dim p As New BufferedPanel With {.Height = ROW_H, .BackColor = Color.Transparent, .Cursor = Cursors.Hand}
        AddHandler p.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                If TypeOf p.Tag Is Boolean AndAlso CBool(p.Tag) Then
                    Using br As New SolidBrush(RowHover)
                        e.Graphics.FillRoundedRectangle(br, New Rectangle(8, 3, p.Width - 16, ROW_H - 6), 10)
                    End Using
                End If
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 20, p.Height - 1, p.Width - 20, p.Height - 1)
                End Using
            End Sub

        Dim mk = Function(text As String, fnt As Font, clr As Color) As Label
                     Return New Label With {
                         .Text = text, .Font = fnt, .ForeColor = clr, .AutoSize = True,
                         .BackColor = Color.Transparent, .UseMnemonic = False
                     }
                 End Function

        Dim lblDate As Label = mk(r.PaidOn.ToString("MMM d, yyyy"), New Font("Segoe UI", 10.5F), Theme.TextDark)
        Dim lblPay As Label = mk(SupplierPayments.PaymentNo(r.PaymentId), New Font("Segoe UI", 10.5F, FontStyle.Bold), MintDark)
        Dim lblPo As Label = mk(r.PoNo, New Font("Segoe UI", 10.5F, FontStyle.Bold Or FontStyle.Underline), MintPrimary)
        Dim lblSup As Label = mk(r.Supplier, New Font("Segoe UI", 10), SubText)
        lblSup.AutoSize = False : lblSup.AutoEllipsis = True : lblSup.Height = 20

        Dim lblInv As Label = mk(If(r.InvoiceNo = "", ChrW(8212), r.InvoiceNo), New Font("Segoe UI Semibold", 10), MintDark)
        lblInv.AutoSize = False : lblInv.AutoEllipsis = True : lblInv.Height = 20
        Dim invSub As String = If(r.InvoiceDate.HasValue, "dated " & r.InvoiceDate.Value.ToString("MMM d, yyyy"), "") &
                               If(r.Reference <> "", If(r.InvoiceDate.HasValue, "  ·  ", "") & "ref " & r.Reference, "")
        Dim lblInvSub As Label = mk(invSub, New Font("Segoe UI", 9), FaintText)
        lblInvSub.AutoSize = False : lblInvSub.AutoEllipsis = True : lblInvSub.Height = 18

        Dim st As String = If(r.PoPaid, SupplierPayments.STATUS_PAID, SupplierPayments.STATUS_PARTIAL)
        Dim sc = SupplierPayments.StatusColors(st)
        Dim badge As Label = mk(st, New Font("Segoe UI", 8.5F, FontStyle.Bold), sc.fore)
        badge.BackColor = sc.back
        badge.Padding = New Padding(10, 4, 10, 4)
        RoundLabel(badge, 10)

        Dim lblAmt As Label = mk(Money(r.Amount), New Font("Segoe UI", 11, FontStyle.Bold), MintDeep)
        Dim lblMethod As Label = mk(r.Method, New Font("Segoe UI", 9), FaintText)

        p.Controls.AddRange({lblDate, lblPay, lblPo, lblSup, lblInv, lblInvSub, badge, lblAmt, lblMethod})

        Dim tip As New ToolTip()
        tip.SetToolTip(lblPo, "Show the items and payments on " & r.PoNo)
        If r.RecordedBy <> "" Then tip.SetToolTip(lblPay, "Recorded by " & r.RecordedBy)
        AddHandler p.Disposed, Sub() tip.Dispose()

        AddHandler p.Resize,
            Sub()
                Dim w = p.Width
                Dim top1 As Integer = (ROW_H \ 2) - 19
                Dim top2 As Integer = (ROW_H \ 2) + 1
                Dim mid As Integer = (ROW_H - 20) \ 2
                lblDate.Location = New Point(24, (ROW_H - lblDate.Height) \ 2)
                lblPay.Location = New Point(CInt(w * X_PAY), (ROW_H - lblPay.Height) \ 2)
                lblPo.Location = New Point(CInt(w * X_PO), (ROW_H - lblPo.Height) \ 2)
                Dim xSup As Integer = CInt(w * X_SUPPLIER)
                Dim xInv As Integer = CInt(w * X_INVOICE)
                Dim xSt As Integer = CInt(w * X_STATUS)
                lblSup.Location = New Point(xSup, mid)
                lblSup.Width = Math.Max(40, xInv - xSup - 12)
                If invSub = "" Then
                    lblInv.Location = New Point(xInv, mid)
                Else
                    lblInv.Location = New Point(xInv, top1)
                    lblInvSub.Location = New Point(xInv, top2)
                End If
                lblInv.Width = Math.Max(40, xSt - xInv - 12)
                lblInvSub.Width = lblInv.Width
                badge.Location = New Point(xSt, (ROW_H - badge.Height) \ 2)
                lblAmt.Location = New Point(w - lblAmt.Width - 28, top1)
                lblMethod.Location = New Point(w - lblMethod.Width - 28, top2 + 2)
            End Sub

        Dim open As Action =
            Sub()
                PoDetailsModal.Show(r.PoId)
            End Sub
        AddHandler p.Click, Sub() open()
        For Each ctrl As Control In p.Controls
            AddHandler ctrl.Click, Sub() open()
            ctrl.Cursor = Cursors.Hand
        Next

        PropagateHover(p)
        Return p
    End Function

    Private Shared Function BuildEmptyRow(message As String) As Panel
        Dim p As New Panel With {.Height = 90, .Dock = DockStyle.Top, .BackColor = Color.Transparent}
        p.Controls.Add(New Label With {
            .Text = message, .Font = New Font("Segoe UI", 10.5F), .ForeColor = FaintText,
            .AutoSize = True, .Location = New Point(22, 28), .BackColor = Color.Transparent
        })
        Return p
    End Function

    ' =========================================================================
    '  PAGER
    ' =========================================================================
    Private Shared Function BuildPagerBar() As Panel
        Dim wrapper As New Panel With {.Height = 52, .BackColor = Color.Transparent}
        AddHandler wrapper.Paint,
            Sub(s, e)
                Using dp As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(dp, 0, 0, wrapper.Width, 0)
                End Using
            End Sub

        _btnPrev = MakePagerButton("‹")
        _btnNext = MakePagerButton("›")
        _lblPageInfo = New Label With {
            .Font = New Font("Segoe UI", 9.5F), .ForeColor = SubText,
            .AutoSize = True, .BackColor = Color.Transparent
        }

        Dim place As Action =
            Sub()
                Dim cx As Integer = wrapper.Width \ 2
                _btnPrev.Left = cx - _btnPrev.Width - _lblPageInfo.Width \ 2 - 8
                _btnPrev.Top = (wrapper.Height - _btnPrev.Height) \ 2
                _lblPageInfo.Left = cx - _lblPageInfo.Width \ 2
                _lblPageInfo.Top = (wrapper.Height - _lblPageInfo.Height) \ 2
                _btnNext.Left = cx + _lblPageInfo.Width \ 2 + 8
                _btnNext.Top = (wrapper.Height - _btnNext.Height) \ 2
            End Sub
        AddHandler wrapper.Resize, Sub() place()
        AddHandler _lblPageInfo.SizeChanged, Sub() place()

        AddHandler _btnPrev.Click,
            Sub()
                If _currentPage > 0 Then
                    _currentPage -= 1
                    ReQuery(resetPage:=False)
                End If
            End Sub
        AddHandler _btnNext.Click,
            Sub()
                Dim totalPages As Integer = Math.Max(1, CInt(Math.Ceiling(_totalRows / _pageSize)))
                If _currentPage < totalPages - 1 Then
                    _currentPage += 1
                    ReQuery(resetPage:=False)
                End If
            End Sub

        wrapper.Controls.AddRange({_btnPrev, _lblPageInfo, _btnNext})
        Return wrapper
    End Function

    Private Shared Function MakePagerButton(glyph As String) As Button
        Dim b As New Button With {
            .Text = glyph, .FlatStyle = FlatStyle.Flat, .Cursor = Cursors.Hand,
            .Size = New Size(32, 32), .BackColor = Theme.Border,
            .ForeColor = MintDark, .Font = New Font("Segoe UI", 14, FontStyle.Bold),
            .TextAlign = ContentAlignment.MiddleCenter
        }
        b.FlatAppearance.BorderSize = 0
        Return b
    End Function

    Private Shared Sub UpdatePager()
        If _lblPageInfo Is Nothing Then Return
        Dim totalPages As Integer = Math.Max(1, CInt(Math.Ceiling(_totalRows / _pageSize)))
        If _totalRows = 0 Then
            _lblPageInfo.Text = "No payments"
        Else
            Dim firstRow As Integer = _currentPage * _pageSize + 1
            Dim lastRow As Integer = Math.Min(_totalRows, firstRow + _rows.Count - 1)
            _lblPageInfo.Text =
                $"Showing {firstRow:N0}–{lastRow:N0} of {_totalRows:N0}   ·   Page {_currentPage + 1} of {totalPages}"
        End If
        _btnPrev.Enabled = (_currentPage > 0)
        _btnNext.Enabled = (_currentPage < totalPages - 1)
    End Sub

    ' =========================================================================
    '  PRINT / PDF — the same filtered list, every page of it
    ' =========================================================================
    Private Shared Function BuildReportDoc() As ReportPrinter.ReportDoc
        Dim rows As List(Of PayRow)
        Try
            rows = LoadAllForExport()
        Catch ex As Exception
            AppModal.Show("Could not build the report:" & Environment.NewLine & ErrorLog.ForUser(ex),
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return Nothing
        End Try

        Dim period As String = _fromDate.ToString("MMM d, yyyy") & " - " & _toDate.ToString("MMM d, yyyy")
        If _status <> "" Then period &= "   (PO status: " & _status & ")"
        If _search <> "" Then period &= "   (search: " & _search & ")"

        Dim doc As New ReportPrinter.ReportDoc With {
            .Title = "Supplier Payment Records", .PeriodLabel = "Period", .PeriodValue = period,
            .Columns = New List(Of ReportPrinter.Column) From {
                ReportPrinter.Col("Paid On", 1.0F),
                ReportPrinter.Col("Payment No.", 1.0F),
                ReportPrinter.Col("PO No.", 1.2F),
                ReportPrinter.Col("Supplier", 1.8F),
                ReportPrinter.Col("Invoice No.", 1.2F),
                ReportPrinter.Col("PO Status", 0.9F),
                ReportPrinter.Col("Amount", 1.1F, ReportPrinter.ColumnKind.Money)
            }
        }
        doc.Rows.Add(ReportPrinter.TableHeader())
        Dim total As Decimal = 0D
        If rows.Count = 0 Then
            doc.Rows.Add(ReportPrinter.TableRow("No payments match these filters.", "", "", "", "", "", ""))
        Else
            For Each r In rows
                total += r.Amount
                doc.Rows.Add(ReportPrinter.TableRow(
                    r.PaidOn.ToString("MMM d, yyyy"), SupplierPayments.PaymentNo(r.PaymentId), r.PoNo,
                    r.Supplier, r.InvoiceNo,
                    If(r.PoPaid, SupplierPayments.STATUS_PAID, SupplierPayments.STATUS_PARTIAL),
                    Money(r.Amount)))
            Next
        End If
        doc.Rows.Add(ReportPrinter.TableTotal("TOTAL",
            rows.Count.ToString("N0") & If(rows.Count = 1, " payment", " payments"),
            "", "", "", "", Money(total)))
        Return doc
    End Function

    Private Shared Sub PrintReport()
        Dim doc As ReportPrinter.ReportDoc = BuildReportDoc()
        If doc Is Nothing Then Return
        ActivityLog.Record("REPORT_EXPORT", "report", Nothing,
                           Left(doc.Title & " (Print) - " & doc.PeriodValue, 250))
        ReportPrinter.Preview(doc, companyName:=ReportPrinter.CompanyName(),
                              preparedBy:=Session.CurrentFullName)
    End Sub

    Private Shared Sub SaveReport(owner As Control)
        Dim doc As ReportPrinter.ReportDoc = BuildReportDoc()
        If doc Is Nothing Then Return
        ReportExport.SaveWithDialog(doc, ReportExport.ExportFormat.Pdf, ReportPrinter.CompanyName(),
                                    Session.CurrentFullName, owner.FindForm())
    End Sub

    ' =========================================================================
    '  HELPERS
    ' =========================================================================
    Private Shared Function Money(v As Decimal) As String
        Return Peso & v.ToString("N2", CultureInfo.InvariantCulture)
    End Function

    Private Shared Function SafeInt(o As Object) As Integer
        If o Is Nothing OrElse IsDBNull(o) Then Return 0
        Return Convert.ToInt32(o)
    End Function

    Private Shared Function SafeDec(o As Object) As Decimal
        If o Is Nothing OrElse IsDBNull(o) Then Return 0D
        Return Convert.ToDecimal(o)
    End Function

    Private Shared Function MakeActionButton(text As String, width As Integer,
                                             rest As Color, hover As Color) As Button
        Dim b As New Button With {
            .Text = text, .FlatStyle = FlatStyle.Flat, .Font = New Font("Segoe UI Semibold", 10),
            .ForeColor = Color.White, .BackColor = rest, .Size = New Size(width, 40),
            .Cursor = Cursors.Hand, .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        b.FlatAppearance.BorderSize = 0
        AddHandler b.Paint,
            Sub(s, e)
                Dim bb = CType(s, Button)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Using bg As New SolidBrush(bb.Parent.BackColor)
                    e.Graphics.FillRectangle(bg, bb.ClientRectangle)
                End Using
                Dim rect = bb.ClientRectangle : rect.Inflate(-1, -1)
                Using br As New SolidBrush(bb.BackColor)
                    e.Graphics.FillRoundedRectangle(br, rect, 8)
                End Using
                TextRenderer.DrawText(e.Graphics, bb.Text, bb.Font, bb.ClientRectangle,
                    bb.ForeColor, TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub
        AddHandler b.MouseEnter, Sub()
                                     b.BackColor = hover
                                     b.Invalidate()
                                 End Sub
        AddHandler b.MouseLeave, Sub()
                                     b.BackColor = rest
                                     b.Invalidate()
                                 End Sub
        Return b
    End Function

    Private Shared Sub RoundLabel(lbl As Label, radius As Integer)
        Dim apply As Action =
            Sub()
                If lbl.Width <= radius OrElse lbl.Height <= radius Then Return
                Dim path As New GraphicsPath()
                path.AddArc(0, 0, radius, radius, 180, 90)
                path.AddArc(lbl.Width - radius, 0, radius, radius, 270, 90)
                path.AddArc(lbl.Width - radius, lbl.Height - radius, radius, radius, 0, 90)
                path.AddArc(0, lbl.Height - radius, radius, radius, 90, 90)
                path.CloseFigure()
                lbl.Region = New Region(path)
            End Sub
        AddHandler lbl.Resize, Sub() apply()
        apply()
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
