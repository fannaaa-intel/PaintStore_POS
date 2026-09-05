Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports System.Linq
Imports MySql.Data.MySqlClient

Public Class SalesPanelBuilder

    Private Shared ReadOnly MintDark As Color = Color.FromArgb(10, 79, 66)
    Private Shared ReadOnly MintPrimary As Color = Color.FromArgb(0, 168, 142)
    Private Shared ReadOnly MintBg As Color = Color.FromArgb(244, 248, 246)
    Private Shared ReadOnly CardBg As Color = Color.White
    Private Shared ReadOnly CardBorder As Color = Color.FromArgb(228, 232, 230)
    Private Shared ReadOnly SubText As Color = Color.FromArgb(90, 114, 107)
    Private Shared ReadOnly TileBg As Color = Color.FromArgb(236, 248, 244)
    Private Shared ReadOnly SoftBorder As Color = Color.FromArgb(210, 225, 220)
    Private Shared ReadOnly TextDark As Color = Color.FromArgb(45, 45, 45)
    Private Shared ReadOnly HeaderBg As Color = Color.FromArgb(0, 100, 84)

    Public Structure SalesRecord
        Public TxnNo As String
        Public Products As String
        Public SwatchColor As Color
        Public Cashier As String
        Public Time As String
        Public Status As String
        Public Amount As Decimal
    End Structure

    ' ── SHARED STATE (live filtering / chart data) ─────────────────────────────
    Private Shared _txnRowList As CardTableBody
    Private Shared _txnColGrid As TableLayoutPanel
    Private Shared _activeFilter As String = "All"
    Private Shared _container As Panel
    Private Shared _lblPaging As Label
    Private Shared _lblRevenue As Label
    Private Shared _lblTxnCount As Label
    Private Shared _lblAvgOrder As Label
    Private Shared _lblItemsSold As Label
    Private Shared _barAreaRef As Panel
    Private Shared _pieAreaRef As Panel

    ' ── ICON PATHS ─────────────────────────────────────────────────────────────
    Private Shared ReadOnly IconReports As String = "report.png"
    Private Shared ReadOnly IconActivity As String = "recent.png"
    Private Shared ReadOnly IconSales As String = "product-sales.png"

    Private Shared ReadOnly IconCache As New Dictionary(Of String, Image)
    Private Shared Function GetIcon(path As String) As Image
        If String.IsNullOrEmpty(path) OrElse Not IO.File.Exists(path) Then Return Nothing
        If Not IconCache.ContainsKey(path) Then
            Using fs As New IO.FileStream(path, IO.FileMode.Open, IO.FileAccess.Read)
                IconCache(path) = Image.FromStream(fs)
            End Using
        End If
        Return IconCache(path)
    End Function

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Shared Sub LoadSales(container As Panel)

        _activeFilter = "All"
        _container = container

        container.SuspendLayout()
        container.Controls.Clear()
        container.BackColor = MintBg

        Dim scroll As New Panel With {
            .Dock = DockStyle.Fill, .AutoScroll = True, .BackColor = MintBg,
            .Padding = New Padding(0, 22, 24, 8)
        }
        container.Controls.Add(scroll)

        Dim wrap As New Panel With {.AutoSize = False, .BackColor = Color.Transparent}
        scroll.Controls.Add(wrap)

        Dim ResizeWrap As Action =
            Sub()
                Dim w As Integer = Math.Max(scroll.ClientSize.Width - 40, 100)
                wrap.Width = w
                wrap.Left = 18
                wrap.Top = 0
            End Sub

        AddHandler scroll.Resize, Sub() ResizeWrap()
        AddHandler container.Resize,
            Sub()
                ResizeWrap()
                wrap.Invalidate(True)
            End Sub
        ResizeWrap()

        Const GAP As Integer = 16
        Dim curTop As Integer = 0

        ' ── Pre-load today's stats from DB ────────────────────────────────────
        Dim dbRevenue As Decimal = 0
        Dim dbTxns As Integer = 0
        Dim dbItems As Integer = 0
        Dim dbAvg As Decimal = 0

        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT COALESCE(SUM(total),0) AS rev, COUNT(*) AS txns " &
                    "FROM sales WHERE DATE(sale_datetime)=CURDATE() AND status IN ('Completed','Partially Refunded') AND user_id=@uid", conn)
                    cmd.Parameters.AddWithValue("@uid", Session.CurrentUserId)
                    Using rdr = cmd.ExecuteReader()
                        If rdr.Read() Then
                            dbRevenue = CDec(rdr("rev"))
                            dbTxns = CInt(rdr("txns"))
                            dbAvg = If(dbTxns > 0, dbRevenue / dbTxns, 0D)
                        End If
                    End Using
                End Using
                Using cmd As New MySqlCommand(
                    "SELECT COALESCE(SUM(si.qty),0) FROM sale_items si " &
                    "JOIN sales s ON si.sale_id=s.sale_id " &
                    "WHERE DATE(s.sale_datetime)=CURDATE() AND s.status IN ('Completed','Partially Refunded') AND s.user_id=@uid", conn)
                    cmd.Parameters.AddWithValue("@uid", Session.CurrentUserId)
                    dbItems = CInt(cmd.ExecuteScalar())
                End Using
            End Using
        Catch
        End Try

        ' ===========================
        ' 1. TOP BAR
        ' ===========================
        Dim topBar As New BufferedPanel With {
            .Height = 64, .Top = curTop, .Left = 0, .Width = wrap.Width,
            .BackColor = MintDark,
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        }
        AddHandler topBar.Paint,
            Sub(s As Object, e As PaintEventArgs)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect = topBar.ClientRectangle
                rect.Inflate(-1, -1)
                Using path As New GraphicsPath()
                    Dim r = 10
                    path.AddArc(rect.X, rect.Y, r, r, 180, 90)
                    path.AddArc(rect.Right - r, rect.Y, r, r, 270, 90)
                    path.AddArc(rect.Right - r, rect.Bottom - r, r, r, 0, 90)
                    path.AddArc(rect.X, rect.Bottom - r, r, r, 90, 90)
                    path.CloseFigure()
                    Using br As New LinearGradientBrush(rect, MintDark, HeaderBg, 0.0F)
                        g.FillPath(br, path)
                    End Using
                End Using
            End Sub

        Dim lblGreet As New Label With {
            .Text = "SALES HISTORY", .Font = New Font("Segoe UI Semibold", 13, FontStyle.Bold),
            .ForeColor = Color.White, .BackColor = Color.Transparent, .AutoSize = True, .Location = New Point(18, 9)
        }
        Dim lblDate As New Label With {
            .Text = "ALL TRANSACTIONS   ·   " & DateTime.Now.ToString("MMMM dd, yyyy").ToUpper(),
            .Font = New Font("Segoe UI", 9), .ForeColor = Color.FromArgb(160, 215, 200),
            .BackColor = Color.Transparent, .AutoSize = True, .Location = New Point(20, 36)
        }
        Dim lblClock As New Label With {
            .Text = DateTime.Now.ToString("MMM dd, yyyy  |  hh:mm tt"),
            .Font = New Font("Segoe UI", 10), .ForeColor = Color.FromArgb(200, 240, 225),
            .BackColor = Color.Transparent, .AutoSize = True
        }
        Dim clockTimer As New Timer With {.Interval = 1000}
        AddHandler clockTimer.Tick,
            Sub()
                If Not lblClock.IsDisposed Then
                    lblClock.Text = DateTime.Now.ToString("MMM dd, yyyy  |  hh:mm tt")
                    lblClock.Location = New Point(topBar.Width - lblClock.Width - 18, (topBar.Height - lblClock.Height) \ 2)
                Else
                    clockTimer.Stop() : clockTimer.Dispose()
                End If
            End Sub
        clockTimer.Start()
        AddHandler topBar.Disposed,
            Sub()
                clockTimer.Stop()
                clockTimer.Dispose()
            End Sub

        ' ── Refresh button ────────────────────────────────────────────────
        Dim btnRefresh As New Button With {
            .Text = "↻  Refresh",
            .Size = New Size(100, 34),
            .FlatStyle = FlatStyle.Flat,
            .BackColor = Color.FromArgb(0, 110, 92),
            .ForeColor = Color.FromArgb(200, 240, 225),
            .Font = New Font("Segoe UI", 9.5F),
            .Cursor = Cursors.Hand
        }
        btnRefresh.FlatAppearance.BorderSize = 0
        AddHandler btnRefresh.Paint,
            Sub(s As Object, e As PaintEventArgs)
                Dim b = CType(s, Button)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect As Rectangle = b.ClientRectangle
                rect.Inflate(-1, -1)
                Using path As New GraphicsPath()
                    Dim r = 8
                    path.AddArc(rect.X, rect.Y, r, r, 180, 90)
                    path.AddArc(rect.Right - r, rect.Y, r, r, 270, 90)
                    path.AddArc(rect.Right - r, rect.Bottom - r, r, r, 0, 90)
                    path.AddArc(rect.X, rect.Bottom - r, r, r, 90, 90)
                    path.CloseFigure()
                    Using br As New SolidBrush(b.BackColor)
                        e.Graphics.FillPath(br, path)
                    End Using
                End Using
                TextRenderer.DrawText(e.Graphics, b.Text, b.Font, b.ClientRectangle, b.ForeColor,
                    TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub
        AddHandler btnRefresh.MouseEnter,
            Sub()
                btnRefresh.BackColor = Color.FromArgb(0, 90, 75)
                btnRefresh.ForeColor = Color.White
                btnRefresh.Invalidate()
            End Sub
        AddHandler btnRefresh.MouseLeave,
            Sub()
                btnRefresh.BackColor = Color.FromArgb(0, 110, 92)
                btnRefresh.ForeColor = Color.FromArgb(200, 240, 225)
                btnRefresh.Invalidate()
            End Sub
        AddHandler btnRefresh.Click,
            Sub()
                If _container IsNot Nothing Then LoadSales(_container)
            End Sub

        topBar.Controls.AddRange({lblGreet, lblDate, lblClock, btnRefresh})
        AddHandler topBar.Resize,
            Sub()
                lblClock.Location = New Point(topBar.Width - lblClock.Width - 18, (topBar.Height - lblClock.Height) \ 2)
                btnRefresh.Left = lblClock.Left - btnRefresh.Width - 12
                btnRefresh.Top = (topBar.Height - btnRefresh.Height) \ 2
            End Sub
        AddHandler wrap.Resize, Sub() topBar.Width = wrap.Width

        wrap.Controls.Add(topBar)
        curTop = topBar.Bottom + GAP

        ' ── ACTION ROW ────────────────────────────────────────────────────────
        Dim actionRow As New Panel With {
            .Height = 40, .Top = curTop, .Left = 0, .Width = wrap.Width,
            .BackColor = Color.Transparent,
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        }
        Dim btnWrap As New FlowLayoutPanel With {
            .FlowDirection = FlowDirection.LeftToRight, .AutoSize = True,
            .BackColor = Color.Transparent, .WrapContents = False,
            .Anchor = AnchorStyles.Right Or AnchorStyles.Top
        }
        Dim btnExport As New Button With {
            .Text = "Export", .FlatStyle = FlatStyle.Flat, .Font = New Font("Segoe UI", 9),
            .ForeColor = Color.FromArgb(74, 120, 112), .BackColor = Color.White,
            .Size = New Size(90, 34), .Cursor = Cursors.Hand, .Margin = New Padding(0, 0, 8, 0)
        }
        btnExport.FlatAppearance.BorderColor = SoftBorder
        btnExport.FlatAppearance.BorderSize = 1
        Dim btnNew As New Button With {
            .Text = "+ New Transaction", .FlatStyle = FlatStyle.Flat,
            .Font = New Font("Segoe UI Semibold", 10), .ForeColor = Color.White,
            .BackColor = MintPrimary, .Size = New Size(160, 34), .Cursor = Cursors.Hand, .Margin = New Padding(0)
        }
        btnNew.FlatAppearance.BorderSize = 0
        AddHandler btnNew.MouseEnter, Sub() btnNew.BackColor = MintDark
        AddHandler btnNew.MouseLeave, Sub() btnNew.BackColor = MintPrimary

        ' Export the currently shown (filtered) transactions to a CSV file.
        AddHandler btnExport.Click, Sub() ExportTransactions()
        ' Start a new transaction — swap the main panel to the POS builder.
        AddHandler btnNew.Click, Sub() StartNewTransaction()

        btnWrap.Controls.Add(btnExport)
        btnWrap.Controls.Add(btnNew)
        actionRow.Controls.Add(btnWrap)
        AddHandler actionRow.Resize,
            Sub()
                btnWrap.Left = actionRow.Width - btnWrap.Width
                btnWrap.Top = 0
            End Sub
        AddHandler wrap.Resize, Sub() actionRow.Width = wrap.Width

        wrap.Controls.Add(actionRow)
        curTop = actionRow.Bottom + GAP

        ' ===========================
        ' 2. STAT ROW
        ' ===========================
        Dim statRow As New TableLayoutPanel With {
            .Top = curTop, .Left = 0, .Width = wrap.Width, .Height = 130,
            .ColumnCount = 4, .RowCount = 1, .BackColor = Color.Transparent,
            .CellBorderStyle = TableLayoutPanelCellBorderStyle.None,
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        }
        For i = 0 To 3
            statRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 25))
        Next
        AddHandler wrap.Resize, Sub() statRow.Width = wrap.Width

        Dim statData() As (title As String, value As String, subtitle As String, isUp As Boolean) = {
            ("Total Revenue", "₱ " & dbRevenue.ToString("N2"), "Today completed sales", True),
            ("Transactions", dbTxns.ToString(), "Today", True),
            ("Avg. Order Value", "₱ " & dbAvg.ToString("N2"), "Today average", True),
            ("Items Sold", dbItems.ToString(), "Units today", True)
        }

        For i As Integer = 0 To 3
            Dim sd = statData(i)
            Dim capI = i

            Dim card As New BufferedPanel With {
                .Dock = DockStyle.Fill, .BackColor = CardBg,
                .Margin = New Padding(0, 0, If(i < 3, 12, 0), 0)
            }
            AddHandler card.Paint,
                Sub(s As Object, e As PaintEventArgs) PanelRenderer.Draw(CType(s, Panel), e, True)

            Dim lTitle As New Label With {
                .Text = sd.title, .Font = New Font("Segoe UI", 11),
                .ForeColor = SubText, .AutoSize = True, .Location = New Point(22, 14)
            }
            Dim lValue As New Label With {
                .Text = sd.value, .Font = New Font("Segoe UI", 26, FontStyle.Bold),
                .ForeColor = MintDark, .AutoSize = True, .Location = New Point(20, 36)
            }
            Dim lSub As New Label With {
                .Text = sd.subtitle, .Font = New Font("Segoe UI", 10),
                .ForeColor = SubText, .AutoSize = True, .Location = New Point(22, 92)
            }

            ' Capture labels for refresh
            Select Case capI
                Case 0 : _lblRevenue = lValue
                Case 1 : _lblTxnCount = lValue
                Case 2 : _lblAvgOrder = lValue
                Case 3 : _lblItemsSold = lValue
            End Select

            card.Controls.AddRange({lTitle, lValue, lSub})
            statRow.Controls.Add(card, i, 0)
        Next

        wrap.Controls.Add(statRow)
        curTop = statRow.Bottom + GAP

        ' ===========================
        ' 3. TRANSACTION TABLE CARD
        ' ===========================
        Dim tableCard As New BufferedPanel With {
            .Top = curTop, .Left = 0, .Width = wrap.Width, .Height = 480, .BackColor = CardBg,
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        }
        AddHandler tableCard.Paint,
            Sub(s As Object, e As PaintEventArgs) PanelRenderer.Draw(CType(s, Panel), e, True)
        AddHandler wrap.Resize, Sub() tableCard.Width = wrap.Width

        BuildTransactionTable(tableCard)

        wrap.Controls.Add(tableCard)
        curTop = tableCard.Bottom + GAP

        ' ===========================
        ' 4. BOTTOM ROW — CHARTS
        ' ===========================
        Dim botRow As New TableLayoutPanel With {
            .Top = curTop, .Left = 0, .Width = wrap.Width, .Height = 300,
            .ColumnCount = 2, .RowCount = 1, .BackColor = Color.Transparent,
            .CellBorderStyle = TableLayoutPanelCellBorderStyle.None,
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        }
        botRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 58))
        botRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 42))
        AddHandler wrap.Resize, Sub() botRow.Width = wrap.Width

        botRow.Controls.Add(BuildTopProductsCard(), 0, 0)
        botRow.Controls.Add(BuildCategoryCard(), 1, 0)

        wrap.Controls.Add(botRow)
        curTop = botRow.Bottom + 28
        wrap.Height = curTop

        container.ResumeLayout()
        container.Refresh()
    End Sub

    ' =========================================================================
    '  TRANSACTION TABLE
    ' =========================================================================
    Private Shared Sub BuildTransactionTable(card As BufferedPanel)

        Dim header = MakeCardHeader("Recent Transactions", IconActivity, "≡")
        card.Controls.Add(header)
        card.Controls.SetChildIndex(header, 0)

        ' ── Filter tabs ──
        Dim toolRow As New Panel With {
            .Top = 60, .Left = 0, .Height = 44, .BackColor = Color.Transparent,
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        }
        AddHandler card.Resize, Sub() toolRow.Width = card.Width

        Dim filters() As String = {"All", "Completed", "On Hold", "Refunded", "Voided"}
        Dim activeFilterIdx As Integer = 0
        Dim tabBtns(filters.Length - 1) As Button

        ' Tabs sit left — fixed-size flow, original position
        Dim filterHost As New FlowLayoutPanel With {
            .FlowDirection = FlowDirection.LeftToRight, .AutoSize = True,
            .BackColor = Color.Transparent, .WrapContents = False,
            .Location = New Point(18, 8)
        }

        For i = 0 To filters.Length - 1
            Dim idx = i
            Dim capFilter = filters(i)
            Dim b As New Button With {
                .Text = filters(i), .FlatStyle = FlatStyle.Flat, .Cursor = Cursors.Hand,
                .Size = New Size(86, 28), .Font = New Font("Segoe UI", 9.5F),
                .Margin = New Padding(0, 0, 6, 0)
            }
            b.FlatAppearance.BorderSize = 0

            Dim style As Action(Of Boolean, Boolean) =
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

            AddHandler b.MouseEnter, Sub() If idx <> activeFilterIdx Then style(False, True)
            AddHandler b.MouseLeave, Sub() If idx <> activeFilterIdx Then style(False, False)
            AddHandler b.Click,
                Sub()
                    Dim prev = activeFilterIdx : activeFilterIdx = idx
                    If tabBtns(prev) IsNot Nothing Then
                        tabBtns(prev).BackColor = Color.FromArgb(220, 235, 232)
                        tabBtns(prev).ForeColor = SubText
                        tabBtns(prev).Font = New Font("Segoe UI", 9.5F)
                        tabBtns(prev).Invalidate()
                    End If
                    style(True, False)
                    _activeFilter = capFilter
                    RefreshTransactionRows()
                End Sub

            style(i = 0, False)
            tabBtns(i) = b
            filterHost.Controls.Add(b)
        Next

        ' ── Search box ──
        Dim searchWrap As New Panel With {
            .Size = New Size(220, 32), .BackColor = TileBg, .Cursor = Cursors.IBeam,
            .Anchor = AnchorStyles.Right Or AnchorStyles.Top
        }
        Dim searchFocused As Boolean = False
        AddHandler searchWrap.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim r = searchWrap.ClientRectangle : r.Inflate(-1, -1)
                Using b As New SolidBrush(TileBg) : e.Graphics.FillRoundedRectangle(b, r, 8) : End Using
                Using p As New Pen(If(searchFocused, MintPrimary, SoftBorder), If(searchFocused, 1.8F, 1.0F))
                    e.Graphics.DrawRoundedRectangle(p, r, 8)
                End Using
                Using p As New Pen(Color.FromArgb(160, 180, 178), 1.5F)
                    e.Graphics.DrawEllipse(p, 11, 11, 11, 11)
                    e.Graphics.DrawLine(p, 20, 20, 25, 25)
                End Using
            End Sub

        Dim txtSearch As New TextBox With {
            .Font = New Font("Segoe UI", 10), .BorderStyle = BorderStyle.None,
            .BackColor = TileBg, .ForeColor = SubText, .Text = "Search receipt...",
            .Location = New Point(32, 8), .Width = 180
        }
        AddHandler txtSearch.Enter,
            Sub()
                searchFocused = True
                If txtSearch.Text = "Search receipt..." Then txtSearch.Text = "" : txtSearch.ForeColor = TextDark
                searchWrap.Invalidate()
            End Sub
        AddHandler txtSearch.Leave,
            Sub()
                searchFocused = False
                If String.IsNullOrWhiteSpace(txtSearch.Text) Then txtSearch.Text = "Search receipt..." : txtSearch.ForeColor = SubText
                searchWrap.Invalidate()
            End Sub
        AddHandler searchWrap.Click, Sub() txtSearch.Focus()
        AddHandler txtSearch.TextChanged,
            Sub()
                If txtSearch.Text = "Search receipt..." Then Return
                RefreshTransactionRowsWithSearch(txtSearch.Text.Trim())
            End Sub
        AddHandler txtSearch.KeyDown,
            Sub(s, e)
                If CType(e, KeyEventArgs).KeyCode = Keys.Escape Then
                    txtSearch.Text = "Search receipt..."
                    txtSearch.ForeColor = SubText
                    RefreshTransactionRows()
                End If
            End Sub
        searchWrap.Controls.Add(txtSearch)

        toolRow.Controls.Add(filterHost)
        toolRow.Controls.Add(searchWrap)
        AddHandler toolRow.Resize,
            Sub()
                searchWrap.Location = New Point(toolRow.Width - searchWrap.Width - 18, 6)
            End Sub

        card.Controls.Add(toolRow)

        ' ── Column headers ──
        Dim colHeader As New Panel With {
            .Top = 104, .Left = 2, .Height = 36, .BackColor = Color.FromArgb(247, 250, 249),
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        }
        AddHandler card.Resize, Sub() colHeader.Width = card.Width - 4
        AddHandler colHeader.Paint,
            Sub(s, e)
                Using p As New Pen(SoftBorder, 1)
                    e.Graphics.DrawLine(p, 0, colHeader.Height - 1, colHeader.Width, colHeader.Height - 1)
                    e.Graphics.DrawLine(p, 0, 0, colHeader.Width, 0)
                End Using
                ' The strip is inset 2px inside the card, so the card's own
                ' hairline already shows to either side of it -- nothing more to
                ' draw here. (Previously this strip spanned the full card width
                ' and painted over that border, which made the edge look ragged.)
            End Sub

        _txnColGrid = New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 5, .RowCount = 1,
            .BackColor = Color.Transparent, .Padding = New Padding(22, 0, 16, 0)
        }
        _txnColGrid.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
        _txnColGrid.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 110))
        _txnColGrid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        _txnColGrid.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 120))
        _txnColGrid.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 130))
        _txnColGrid.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 110))

        Dim hdrTxts() As String = {"Receipt #", "Products", "Time", "Status", "Amount"}
        Dim hdrAligns() As ContentAlignment = {
            ContentAlignment.MiddleLeft, ContentAlignment.MiddleLeft,
            ContentAlignment.MiddleLeft, ContentAlignment.MiddleCenter, ContentAlignment.MiddleRight
        }
        For idx = 0 To hdrTxts.Length - 1
            _txnColGrid.Controls.Add(New Label With {
                .Text = hdrTxts(idx), .Dock = DockStyle.Fill, .TextAlign = hdrAligns(idx),
                .Font = New Font("Segoe UI Semibold", 10),
                .ForeColor = Color.FromArgb(122, 152, 144), .BackColor = Color.Transparent
            }, idx, 0)
        Next
        colHeader.Controls.Add(_txnColGrid)
        card.Controls.Add(colHeader)

        ' ── Data rows ──
        _txnRowList = New CardTableBody With {
            .Top = 140, .Left = 2,
            .EmptyText = "No transactions yet today.",
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top Or AnchorStyles.Bottom
        }
        AddHandler card.Resize,
            Sub()
                _txnRowList.Width = card.Width - 4
                _txnRowList.Height = card.Height - 140 - 45
            End Sub

        Dim SyncHeaderGutter As Action =
            Sub()
                Dim sbw As Integer = If(_txnRowList.VerticalScroll.Visible, SystemInformation.VerticalScrollBarWidth, 0)
                If _txnColGrid IsNot Nothing Then _txnColGrid.Padding = New Padding(22, 0, 16 + sbw, 0)
            End Sub
        AddHandler _txnRowList.Layout, Sub() SyncHeaderGutter()
        AddHandler card.Resize, Sub() SyncHeaderGutter()

        card.Controls.Add(_txnRowList)

        ' ── Footer / pagination ──
        ' Footer is inset by 1px and given a rounded bottom so the card's own
        ' rounded corner + hairline border stay visible underneath it.
        Dim footer As New CardFooterPanel With {
            .Height = 44, .Left = 2, .BackColor = Color.White, .Padding = New Padding(18, 0, 18, 0),
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Bottom
        }
        AddHandler card.Resize,
            Sub()
                footer.Width = card.Width - 4
                footer.Top = card.Height - footer.Height - 1
            End Sub

        ' Padding does not offset absolutely-positioned children, so inset the
        ' label explicitly and centre it in the strip -- at x=0 it collided with
        ' the footer's clipped corner and the text was cut off at the bottom.
        _lblPaging = New Label With {
            .Text = "Loading transactions...", .Font = New Font("Segoe UI", 10),
            .ForeColor = SubText, .AutoSize = True
        }
        AddHandler footer.Resize,
            Sub() _lblPaging.Location = New Point(18, (footer.Height - _lblPaging.Height) \ 2)
        footer.Controls.Add(_lblPaging)
        card.Controls.Add(footer)

        ' Load first batch of rows
        RefreshTransactionRows()
    End Sub

    Private Shared Sub RefreshTransactionRowsWithSearch(searchTerm As String)
        If _txnRowList Is Nothing Then Return
        If String.IsNullOrWhiteSpace(searchTerm) Then
            RefreshTransactionRows() : Return
        End If

        _txnRowList.SuspendLayout()
        _txnRowList.Controls.Clear()

        Dim data = LoadSalesFromDb(_activeFilter)
        Dim filtered = data.Where(Function(r)
                                      Return r.TxnNo.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                   r.Products.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0
                                  End Function).ToArray()

        For Each rd In filtered
            _txnRowList.Controls.Add(BuildSalesRow(rd))
        Next
        For i = _txnRowList.Controls.Count - 1 To 0 Step -1
            _txnRowList.Controls.SetChildIndex(_txnRowList.Controls(i), _txnRowList.Controls.Count - 1 - i)
        Next

        _txnRowList.ResumeLayout()
        _txnRowList.Invalidate(True)

        If _lblPaging IsNot Nothing Then
            _lblPaging.Text = $"Showing {filtered.Length} of {data.Length} transaction(s) matching ""{searchTerm}"""
        End If
    End Sub

    ' ── Refresh rows from DB based on current filter ──────────────────────────
    Private Shared Sub RefreshTransactionRows()
        If _txnRowList Is Nothing Then Return

        _txnRowList.SuspendLayout()
        _txnRowList.Controls.Clear()

        Dim data = LoadSalesFromDb(_activeFilter)

        For Each rd In data
            _txnRowList.Controls.Add(BuildSalesRow(rd))
        Next
        ' Reverse z-order so first item paints on top
        For i = _txnRowList.Controls.Count - 1 To 0 Step -1
            _txnRowList.Controls.SetChildIndex(_txnRowList.Controls(i), _txnRowList.Controls.Count - 1 - i)
        Next

        _txnRowList.ResumeLayout()
        _txnRowList.Invalidate(True)

        If _lblPaging IsNot Nothing Then
            Dim filterSuffix As String = If(_activeFilter = "All", "", " (" & _activeFilter & ")")
            _lblPaging.Text = $"Showing {data.Length} transaction(s){filterSuffix}"
        End If
    End Sub

    ' ── Query sales from DB ───────────────────────────────────────────────────
    Private Shared Function LoadSalesFromDb(Optional statusFilter As String = "All") As SalesRecord()
        Dim list As New List(Of SalesRecord)
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Dim whereClause As String = "WHERE DATE(s.sale_datetime)=CURDATE() AND s.user_id=@uid"
                If statusFilter <> "All" Then whereClause &= " AND s.status=@status"

                Dim sql As String =
                    "SELECT s.sale_id, s.sale_no, s.sale_datetime, s.status, s.total, " &
                    "GROUP_CONCAT(p.name ORDER BY si.line_total DESC SEPARATOR ', ') AS products, " &
                    "MIN(p.color_hex) AS color_hex " &
                    "FROM sales s " &
                    "JOIN sale_items si ON s.sale_id=si.sale_id " &
                    "JOIN products p ON si.product_id=p.product_id " &
                    whereClause & " GROUP BY s.sale_id ORDER BY s.sale_datetime DESC LIMIT 50"
                Using cmd As New MySqlCommand(sql, conn)
                    cmd.Parameters.AddWithValue("@uid", Session.CurrentUserId)
                    If statusFilter <> "All" Then cmd.Parameters.AddWithValue("@status", FilterToDbStatus(statusFilter))
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            Dim colorHex As String = If(IsDBNull(rdr("color_hex")), "", rdr("color_hex").ToString().Trim())
                            Dim swatch As Color = Color.FromArgb(150, 205, 225)
                            Try
                                If colorHex.StartsWith("#") Then swatch = ColorTranslator.FromHtml(colorHex)
                            Catch : End Try

                            Dim productNames As String = If(IsDBNull(rdr("products")), "—", rdr("products").ToString())
                            If productNames.Length > 40 Then productNames = productNames.Substring(0, 37) & "..."

                            list.Add(New SalesRecord With {
                                .TxnNo = "#" & rdr("sale_no").ToString(),
                                .Products = productNames,
                                .SwatchColor = swatch,
                                .Cashier = "",
                                .Time = CDate(rdr("sale_datetime")).ToString("hh:mm tt"),
                                .Status = DbStatusToDisplay(rdr("status").ToString()),
                                .Amount = CDec(rdr("total"))
                            })
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            ' Return empty list on DB error
        End Try
        Return list.ToArray()
    End Function

    ' ── Status name mapping ───────────────────────────────────────────────────
    ' The UI uses friendly labels ("On Hold") while the database stores compact
    ' codes ("OnHold"). These two helpers keep the two worlds in sync so the
    ' filters actually match rows and the status pill shows the right text.
    Private Shared Function FilterToDbStatus(label As String) As String
        Select Case label
            Case "On Hold" : Return "OnHold"
            Case Else : Return label
        End Select
    End Function

    Private Shared Function DbStatusToDisplay(dbStatus As String) As String
        Select Case dbStatus
            Case "OnHold" : Return "On Hold"
            Case "Partially Refunded" : Return "Partial"
            Case Else : Return dbStatus
        End Select
    End Function

    ' ── Export current (filtered) transactions to CSV ─────────────────────────
    Private Shared Sub ExportTransactions()
        Dim data = LoadSalesFromDb(_activeFilter)
        If data.Length = 0 Then
            AppModal.Show("There are no transactions to export for this filter.",
                          "Export", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If

        ' Confirm before exporting.
        If AppModal.Show($"Export {data.Length} transaction(s) shown in this view to a CSV file?",
                         "Export Transactions", MessageBoxButtons.YesNo,
                         MessageBoxIcon.Information) <> DialogResult.Yes Then
            Return
        End If

        Using sfd As New SaveFileDialog()
            sfd.Title = "Export Transactions"
            sfd.Filter = "CSV file (*.csv)|*.csv"
            sfd.FileName = "transactions_" & DateTime.Now.ToString("yyyyMMdd_HHmm") & ".csv"
            If sfd.ShowDialog() <> DialogResult.OK Then Return

            Try
                Dim sb As New System.Text.StringBuilder()
                sb.AppendLine("Receipt #,Products,Time,Status,Amount")
                For Each rd In data
                    sb.Append(CsvCell(rd.TxnNo)).Append(","c)
                    sb.Append(CsvCell(rd.Products)).Append(","c)
                    sb.Append(CsvCell(rd.Time)).Append(","c)
                    sb.Append(CsvCell(rd.Status)).Append(","c)
                    sb.AppendLine(CsvCell(rd.Amount.ToString("F2")))
                Next
                IO.File.WriteAllText(sfd.FileName, sb.ToString(), System.Text.Encoding.UTF8)

                If AppModal.Show($"Exported {data.Length} transaction(s) to:" &
                                   Environment.NewLine & sfd.FileName & Environment.NewLine &
                                   Environment.NewLine & "Open the file now?",
                                   "Export complete", MessageBoxButtons.YesNo,
                                   MessageBoxIcon.Information) = DialogResult.Yes Then
                    System.Diagnostics.Process.Start(New System.Diagnostics.ProcessStartInfo(sfd.FileName) With {.UseShellExecute = True})
                End If
            Catch ex As Exception
                AppModal.Show("Could not export the file:" & Environment.NewLine &
                                ex.Message, "Export Error",
                                MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Using
    End Sub

    ' Quote a CSV field if it contains a comma, quote or line break.
    Private Shared Function CsvCell(value As String) As String
        If value Is Nothing Then Return ""
        If value.IndexOfAny(New Char() {","c, """"c, ChrW(10), ChrW(13)}) >= 0 Then
            Return """" & value.Replace("""", """""") & """"
        End If
        Return value
    End Function

    ' ── New Transaction → swap the main panel to the POS builder ──────────────
    Private Shared Sub StartNewTransaction()
        If _container Is Nothing Then Return

        ' Route through the sidebar's New Transaction button so the active-highlight
        ' moves off "Sales History" and onto "New Transaction" (SetActive runs there).
        Dim frm = TryCast(_container.FindForm(), POS_STAFF)
        If frm IsNot Nothing Then
            frm.transaction.PerformClick()
            Return
        End If

        ' Fallback if the form can't be resolved — load the panel directly.
        _container.SuspendLayout()
        _container.Controls.Clear()
        TransactionPanelBuilder.LoadTransaction(_container)
        _container.ResumeLayout()
        _container.Refresh()
    End Sub

    ' ── Single transaction row ────────────────────────────────────────────────
    Private Shared Function BuildSalesRow(rd As SalesRecord) As Panel
        Dim rowPanel As New Panel With {
            .Height = 48, .Dock = DockStyle.Top, .BackColor = Color.White,
            .Margin = New Padding(0), .Padding = New Padding(18, 0, 18, 0), .Cursor = Cursors.Hand
        }

        AddHandler rowPanel.Paint,
            Sub(s, e)
                Using p As New Pen(Color.FromArgb(237, 243, 242), 1)
                    e.Graphics.DrawLine(p, 0, rowPanel.Height - 1, rowPanel.Width, rowPanel.Height - 1)
                End Using
            End Sub
        AddHandler rowPanel.MouseEnter, Sub() rowPanel.BackColor = TileBg
        AddHandler rowPanel.MouseLeave, Sub() rowPanel.BackColor = Color.White

        Dim rowGrid As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .BackColor = Color.Transparent, .ColumnCount = 5, .RowCount = 1, .Margin = New Padding(0)
        }
        rowGrid.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
        rowGrid.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 110))
        rowGrid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        rowGrid.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 120))
        rowGrid.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 130))
        rowGrid.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 110))

        rowGrid.Controls.Add(New Label With {
            .Text = rd.TxnNo, .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleLeft,
            .Font = New Font("Segoe UI Semibold", 10), .ForeColor = MintPrimary, .BackColor = Color.Transparent
        }, 0, 0)

        Dim prodCell As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.Transparent}
        Dim dot As New Panel With {.Size = New Size(10, 10), .BackColor = rd.SwatchColor}
        AddHandler dot.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Using br As New SolidBrush(rd.SwatchColor) : e.Graphics.FillEllipse(br, dot.ClientRectangle) : End Using
                Using pn As New Pen(Color.FromArgb(180, 200, 196), 0.8F) : e.Graphics.DrawEllipse(pn, dot.ClientRectangle) : End Using
            End Sub
        AddHandler prodCell.Resize, Sub() dot.Location = New Point(0, (prodCell.Height - 10) \ 2)
        Dim lblProd As New Label With {
            .Text = rd.Products, .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleLeft,
            .Font = New Font("Segoe UI", 10), .ForeColor = TextDark, .BackColor = Color.Transparent, .Padding = New Padding(16, 0, 0, 0)
        }
        prodCell.Controls.Add(dot)
        prodCell.Controls.Add(lblProd)
        rowGrid.Controls.Add(prodCell, 1, 0)

        rowGrid.Controls.Add(New Label With {
            .Text = rd.Time, .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleLeft,
            .Font = New Font("Segoe UI", 10), .ForeColor = SubText, .BackColor = Color.Transparent
        }, 2, 0)

        ' Status pill
        Dim sBack As Color, sFore As Color
        Select Case rd.Status
            Case "Completed" : sBack = Color.FromArgb(223, 245, 238) : sFore = Color.FromArgb(0, 120, 92)
            Case "On Hold" : sBack = Color.FromArgb(254, 243, 220) : sFore = Color.FromArgb(150, 98, 0)
            Case "Refunded", "Partial" : sBack = Color.FromArgb(232, 238, 252) : sFore = Color.FromArgb(60, 86, 168)
            Case Else : sBack = Color.FromArgb(245, 237, 236) : sFore = Color.FromArgb(138, 56, 48)
        End Select

        Dim pillWrap As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.Transparent}
        Dim pill As New Label With {
            .Text = rd.Status, .AutoSize = False, .Size = New Size(90, 22),
            .TextAlign = ContentAlignment.MiddleCenter, .Font = New Font("Segoe UI Semibold", 9),
            .ForeColor = sFore, .BackColor = Color.Transparent
        }

        AddHandler pill.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim pr As New Rectangle(0, 0, pill.Width - 1, pill.Height - 1)
                Using br As New SolidBrush(sBack) : e.Graphics.FillRoundedRectangle(br, pr, 10) : End Using
                pr.Inflate(-1, -1)
                Using pn As New Pen(sFore, 0.7F) : e.Graphics.DrawRoundedRectangle(pn, pr, 10) : End Using
                TextRenderer.DrawText(e.Graphics, pill.Text, pill.Font,
                    New Rectangle(0, 0, pill.Width, pill.Height), sFore,
                    TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub

        Dim CenterPill As Action = Sub() pill.Location = New Point((pillWrap.Width - pill.Width) \ 2 + 5, (pillWrap.Height - pill.Height) \ 2)
        AddHandler pillWrap.Resize, Sub(s, e) CenterPill()
        AddHandler pillWrap.Paint, Sub(s, e) CenterPill()
        pillWrap.Controls.Add(pill)
        rowGrid.Controls.Add(pillWrap, 3, 0)

        Dim isVoided As Boolean = (rd.Status = "Voided")
        rowGrid.Controls.Add(New Label With {
            .Text = "₱ " & rd.Amount.ToString("N2"), .Dock = DockStyle.Fill,
            .TextAlign = ContentAlignment.MiddleRight,
            .Font = If(isVoided, New Font("Segoe UI", 10, FontStyle.Strikeout), New Font("Segoe UI Semibold", 10)),
            .ForeColor = If(isVoided, Color.FromArgb(160, 145, 138), MintDark), .BackColor = Color.Transparent
        }, 4, 0)

        rowPanel.Controls.Add(rowGrid)
        For Each c As Control In rowGrid.Controls
            AddHandler c.MouseEnter, Sub() rowPanel.BackColor = TileBg
            AddHandler c.MouseLeave, Sub() rowPanel.BackColor = Color.White
        Next
        Return rowPanel
    End Function

    ' =========================================================================
    '  TOP PRODUCTS BAR CHART CARD  (DB-backed)
    ' =========================================================================
    Private Shared Function BuildTopProductsCard() As Panel
        Dim card As New BufferedPanel With {
            .Dock = DockStyle.Fill, .BackColor = CardBg, .Margin = New Padding(0, 0, 12, 0)
        }
        AddHandler card.Paint,
            Sub(s As Object, e As PaintEventArgs) PanelRenderer.Draw(CType(s, Panel), e, True)

        card.Controls.Add(MakeCardHeader("Top Products Today", IconSales, "◑"))

        ' Load top products from DB
        Dim bars As New List(Of (name As String, pct As Double, label As String, clr As Color))
        Dim palette() As Color = {
            Color.FromArgb(0, 200, 170), Color.FromArgb(93, 202, 165),
            Color.FromArgb(159, 225, 203), Color.FromArgb(197, 237, 224)
        }

        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT p.name, COALESCE(SUM(si.line_total),0) AS rev " &
                    "FROM sale_items si " &
                    "JOIN products p ON si.product_id=p.product_id " &
                    "JOIN sales s ON si.sale_id=s.sale_id " &
                    "WHERE DATE(s.sale_datetime)=CURDATE() AND s.status IN ('Completed','Partially Refunded') " &
                    "GROUP BY p.product_id ORDER BY rev DESC LIMIT 4", conn)
                    Using rdr = cmd.ExecuteReader()
                        Dim maxRev As Decimal = 1
                        Dim rows As New List(Of (String, Decimal))
                        While rdr.Read()
                            Dim rev = CDec(rdr("rev"))
                            rows.Add((rdr("name").ToString(), rev))
                            If rev > maxRev Then maxRev = rev
                        End While
                        For i = 0 To rows.Count - 1
                            bars.Add((rows(i).Item1, CDbl(rows(i).Item2 / maxRev), "₱" & rows(i).Item2.ToString("N0"), palette(Math.Min(i, palette.Length - 1))))
                        Next
                    End Using
                End Using
            End Using
        Catch
        End Try

        If bars.Count = 0 Then
            bars.Add(("No sales today", 0, "₱0", palette(0)))
        End If

        Dim barArea As New Panel With {
            .BackColor = Color.Transparent, .Top = 70, .Left = 16,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Bottom
        }
        AddHandler card.Resize, Sub() barArea.Size = New Size(card.Width - 32, card.Height - 86)

        Dim capBars = bars
        AddHandler barArea.Paint,
            Sub(s, e)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim rowH As Integer = If(capBars.Count > 0 AndAlso barArea.Height \ capBars.Count > 0,
                                        barArea.Height \ capBars.Count, 26)
                Dim labelW As Integer = 100
                Dim valW As Integer = 70
                Dim trackX As Integer = labelW + 6
                Dim trackW As Integer = Math.Max(10, barArea.Width - labelW - valW - 12)

                For i = 0 To capBars.Count - 1
                    Dim b = capBars(i)
                    Dim midY As Integer = i * rowH + rowH \ 2
                    Using br As New SolidBrush(SubText)
                        g.DrawString(If(b.name.Length > 14, b.name.Substring(0, 13) & "…", b.name),
                            New Font("Segoe UI", 10), br, 0, midY - 10)
                    End Using
                    Dim trackRect As New Rectangle(trackX, midY - 5, trackW, 10)
                    Using br As New SolidBrush(TileBg) : g.FillRoundedRectangle(br, trackRect, 5) : End Using
                    Dim fillW As Integer = CInt(trackW * b.pct)
                    If fillW > 0 Then
                        Using br As New SolidBrush(b.clr)
                            g.FillRoundedRectangle(br, New Rectangle(trackX, midY - 5, fillW, 10), 5)
                            g.FillRoundedRectangle(br, New Rectangle(trackX, midY - 5, fillW, 10), 5)
                        End Using
                    End If
                    Dim sf As New StringFormat With {.Alignment = StringAlignment.Far, .LineAlignment = StringAlignment.Center}
                    Using br As New SolidBrush(MintDark)
                        Using fnt As New Font("Segoe UI Semibold", 10)
                            g.DrawString(b.label, fnt, br,
                                RectangleF.FromLTRB(trackX + trackW + 4, midY - 9, trackX + trackW + 4 + valW, midY + 9), sf)
                        End Using : End Using
                Next
            End Sub

        card.Controls.Add(barArea)
        Return card
    End Function

    ' =========================================================================
    '  SALES BY CATEGORY PIE CARD  (DB-backed)
    ' =========================================================================
    Private Shared Function BuildCategoryCard() As Panel
        Dim card As New BufferedPanel With {
            .Dock = DockStyle.Fill, .BackColor = CardBg, .Margin = New Padding(0)
        }
        AddHandler card.Paint,
            Sub(s As Object, e As PaintEventArgs) PanelRenderer.Draw(CType(s, Panel), e, True)

        card.Controls.Add(MakeCardHeader("Sales by Category", IconReports, "▦"))

        Dim palette() As Color = {
            Color.FromArgb(0, 200, 170), Color.FromArgb(93, 202, 165),
            Color.FromArgb(159, 225, 203), Color.FromArgb(197, 237, 224)
        }

        Dim cats As New List(Of (name As String, pct As Integer, clr As Color))

        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT c.name, COUNT(si.sale_item_id) AS cnt " &
                    "FROM sale_items si " &
                    "JOIN products p ON si.product_id=p.product_id " &
                    "JOIN categories c ON p.category_id=c.category_id " &
                    "JOIN sales s ON si.sale_id=s.sale_id " &
                    "WHERE DATE(s.sale_datetime)=CURDATE() AND s.status IN ('Completed','Partially Refunded') " &
                    "GROUP BY c.category_id ORDER BY cnt DESC LIMIT 4", conn)
                    Using rdr = cmd.ExecuteReader()
                        Dim total As Integer = 0
                        Dim rows As New List(Of (String, Integer))
                        While rdr.Read()
                            Dim cnt = CInt(rdr("cnt"))
                            rows.Add((rdr("name").ToString(), cnt))
                            total += cnt
                        End While
                        For i = 0 To rows.Count - 1
                            Dim pct = If(total > 0, CInt(rows(i).Item2 * 100 / total), 0)
                            cats.Add((rows(i).Item1, pct, palette(Math.Min(i, palette.Length - 1))))
                        Next
                    End Using
                End Using
            End Using
        Catch
        End Try

        If cats.Count = 0 Then
            cats.Add(("No data", 100, palette(0)))
        End If

        Dim pieArea As New Panel With {.Size = New Size(140, 140), .BackColor = Color.Transparent}
        Dim capCats = cats
        AddHandler pieArea.Paint,
            Sub(s, e)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim startAngle As Single = 0
                For Each c In capCats
                    Dim sweep = CSng(c.pct / 100.0 * 360)
                    Using br As New SolidBrush(c.clr) : g.FillPie(br, 0, 0, pieArea.Width - 1, pieArea.Height - 1, startAngle, sweep) : End Using
                    startAngle += sweep
                Next
                Using br As New SolidBrush(card.BackColor) : g.FillEllipse(br, 40, 40, 60, 60) : End Using
            End Sub

        Dim legFlow As New FlowLayoutPanel With {
            .FlowDirection = FlowDirection.TopDown, .AutoSize = True,
            .BackColor = Color.Transparent, .WrapContents = False
        }
        For Each cat In cats
            Dim capCat = cat
            Dim row As New Panel With {.Height = 22, .Width = 150, .BackColor = Color.Transparent, .Margin = New Padding(0, 0, 0, 6)}
            Dim dot As New Panel With {.Size = New Size(10, 10), .BackColor = capCat.clr, .Location = New Point(0, 6)}
            AddHandler dot.Paint,
                Sub(s, e)
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                    Using br As New SolidBrush(capCat.clr) : e.Graphics.FillEllipse(br, dot.ClientRectangle) : End Using
                End Sub
            Dim lbl As New Label With {
                .Text = capCat.name & "  —  " & capCat.pct & "%",
                .Font = New Font("Segoe UI", 10), .ForeColor = Color.FromArgb(74, 104, 98),
                .AutoSize = True, .Location = New Point(16, 2)
            }
            row.Controls.Add(dot)
            row.Controls.Add(lbl)
            legFlow.Controls.Add(row)
        Next

        AddHandler card.Resize,
            Sub()
                Dim spacing As Integer = 16
                Dim legendY As Integer = 78
                Dim totalWidth As Integer = legFlow.Width + spacing + pieArea.Width
                Dim startX As Integer = Math.Max(16, (card.Width - totalWidth) \ 2)
                legFlow.Location = New Point(startX, legendY)
                Dim legendCenterY As Integer = legendY + (legFlow.Height \ 2)
                pieArea.Location = New Point(startX + legFlow.Width + spacing, legendCenterY - (pieArea.Height \ 2))
            End Sub

        card.Controls.Add(legFlow)
        card.Controls.Add(pieArea)
        Return card
    End Function

    ' =========================================================================
    '  CARD HEADER FACTORY
    ' =========================================================================
    Private Shared Function MakeCardHeader(title As String, iconPath As String, fallbackIcon As String) As Panel
        Dim header As New BufferedPanel With {.Height = 60, .Dock = DockStyle.Top, .BackColor = Color.Transparent}

        Dim iconBox As New Panel With {
            .Size = New Size(36, 36), .BackColor = Color.FromArgb(232, 246, 241), .Location = New Point(18, 12)
        }
        AddHandler iconBox.Paint,
            Sub(s As Object, e As PaintEventArgs)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Using br As New SolidBrush(CType(s, Panel).BackColor) : g.FillEllipse(br, 0, 0, 35, 35) : End Using
            End Sub

        If iconPath <> "" AndAlso IO.File.Exists(iconPath) Then
            Dim pb As New PictureBox With {
                .Image = GetIcon(iconPath), .SizeMode = PictureBoxSizeMode.Zoom,
                .Size = New Size(22, 22), .BackColor = Color.Transparent
            }
            iconBox.Controls.Add(pb)
            pb.Location = New Point((iconBox.Width - pb.Width) \ 2, (iconBox.Height - pb.Height) \ 2)
        Else
            iconBox.Controls.Add(New Label With {
                .Text = fallbackIcon, .Font = New Font("Segoe UI", 14, FontStyle.Bold), .ForeColor = MintPrimary,
                .AutoSize = False, .Size = New Size(36, 36),
                .TextAlign = ContentAlignment.MiddleCenter, .BackColor = Color.Transparent, .Location = New Point(0, 0)
            })
        End If

        Dim titleLbl As New Label With {
            .Text = title, .Font = New Font("Segoe UI", 14, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(62, 18)
        }
        header.Controls.AddRange({iconBox, titleLbl})
        AddHandler header.Paint,
            Sub(s As Object, e As PaintEventArgs)
                Using pen As New Pen(CardBorder, 1)
                    e.Graphics.DrawLine(pen, 0, header.Height - 1, CType(s, Panel).Width, header.Height - 1)
                End Using
            End Sub
        Return header
    End Function

End Class