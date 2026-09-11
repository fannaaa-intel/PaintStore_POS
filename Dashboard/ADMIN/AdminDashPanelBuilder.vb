Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports MySql.Data.MySqlClient

' =============================================================================
'  AdminDashPanelBuilder  —  Admin ▸ Dashboard (landing page)
'
'  LAYOUT (top to bottom, all inside one AutoScroll host):
'    1. HeaderRow   (84px)  : greeting + date, "N staff on shift" pill, Refresh
'    2. KpiRow     (150px)  : Sales Today / This Week / This Month / Transactions
'    3. TrendCard  (300px)  : "Sales · Last 30 days" area+line chart
'    4. HealthStrip(120px)  : Low Stock / Pending POs / Open Shifts (clickable)
'    5. RecentCard (420px)  : latest 10 activity_logs rows
'
'  Each section loads its data in its own Try/Catch and sets its own failure
'  flag, so a broken query degrades that card to a "Couldn't load" line instead
'  of taking down the whole page.
'
'  Chart drawing follows the approach in AnalyticsPanelBuilder.DrawSalesGraph
'  (gridlines with "k" labels, polyline, point dots, x labels) — that routine
'  itself is hardcoded demo data with a blue-grey palette and takes no
'  parameters, so it is reproduced here against real data in the house palette.
' =============================================================================
Public Class AdminDashPanelBuilder

    ' ── PALETTE  (matches AccountantSalesHistoryPanelBuilder exactly) ─────────
    Private Shared ReadOnly MintDark As Color = Theme.Dark
    Private Shared ReadOnly MintPrimary As Color = Theme.Primary
    Private Shared ReadOnly MintDeep As Color = Theme.Deep
    Private Shared ReadOnly MintBg As Color = Theme.PageBg
    Private Shared ReadOnly CardBg As Color = Color.White
    Private Shared ReadOnly SubText As Color = Theme.SubText
    Private Shared ReadOnly FaintText As Color = Theme.FaintText
    Private Shared ReadOnly RowDivider As Color = Theme.RowDivider
    Private Shared ReadOnly RowHover As Color = Theme.RowHover
    Private Shared ReadOnly AccentRed As Color = Theme.Danger
    Private Shared ReadOnly AccentAmber As Color = Color.FromArgb(210, 140, 40)
    Private Shared ReadOnly GridLine As Color = Color.FromArgb(228, 236, 233)
    Private Shared ReadOnly Peso As String = ChrW(8369)

    ' Health tile tints
    Private Shared ReadOnly TintRed As (fore As Color, back As Color) =
        (Color.FromArgb(180, 40, 40), Color.FromArgb(255, 233, 233))
    Private Shared ReadOnly TintAmber As (fore As Color, back As Color) =
        (Color.FromArgb(165, 110, 20), Color.FromArgb(255, 244, 224))
    Private Shared ReadOnly TintMint As (fore As Color, back As Color) =
        (Color.FromArgb(0, 120, 92), Color.FromArgb(222, 246, 238))

    ' Navigation hook — ADMIN_STAFF assigns this. Nothing => tiles are inert.
    Public Shared NavigateTo As Action(Of String)

    ' ── DATA ──────────────────────────────────────────────────────────────────
    Private Class ActivityEntry
        Public Action As String
        Public EntityType As String
        Public EntityId As Integer
        Public Details As String
        Public CreatedAt As DateTime
        Public FullName As String
    End Class

    Private Structure TrendPoint
        Public Day As DateTime
        Public Value As Decimal
    End Structure

    Private Shared _container As Panel

    ' KPI figures
    Private Shared _today, _yesterday, _week, _lastWeek, _month, _lastMonth As Decimal
    Private Shared _txToday, _cashiersToday As Integer
    Private Shared _kpiFailed As Boolean

    ' Trend
    Private Shared _trend As New List(Of TrendPoint)
    Private Shared _trendFailed As Boolean

    ' Health
    Private Shared _lowStock, _pendingPos, _openShifts As Integer
    Private Shared _healthFailed As Boolean

    ' Activity
    Private Shared _activity As New List(Of ActivityEntry)
    Private Shared _activityFailed As Boolean

    Private Const SIDE_PAD As Integer = 24
    Private Const TREND_DAYS As Integer = 30
    Private Const ACTIVITY_LIMIT As Integer = 10

    ' Recent Activity metrics — the card is sized from these so every row fits
    ' inside the rounded border with a visible gap at the bottom.
    Private Const ACT_ROW_H As Integer = 52
    Private Const ACT_HEADER_H As Integer = 56
    ' Rows visible before the list scrolls. The card stays this tall no matter
    ' how many entries come back, instead of growing with the row count.
    Private Const ACT_MAX_ROWS As Integer = 6
    Private Const ACT_PAD_X As Integer = 12
    Private Const ACT_PAD_TOP As Integer = 8
    ' 18 matches the header's own top inset (title sits at y=18), so the card
    ' reads as symmetrically inset top and bottom.
    Private Const ACT_PAD_BOTTOM As Integer = 18

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Shared Sub LoadDashboard(container As Panel)
        _container = container
        LoadAll()
        Render()
    End Sub

    Private Shared Sub LoadAll()
        LoadKpis()
        LoadTrend()
        LoadHealth()
        LoadActivity()
    End Sub

    ' =========================================================================
    '  DATA — each block fails independently
    ' =========================================================================
    Private Shared Sub LoadKpis()
        _kpiFailed = False
        _today = 0D : _yesterday = 0D : _week = 0D
        _lastWeek = 0D : _month = 0D : _lastMonth = 0D
        _txToday = 0 : _cashiersToday = 0

        Try
            Using conn = Db.GetConnection()
                conn.Open()
                ' One pass with conditional aggregation. The 70-day window
                ' comfortably covers the start of last month (max ~62 days back).
                Dim sql As String =
                    "SELECT " &
                    " COALESCE(SUM(CASE WHEN DATE(sale_datetime)=CURDATE() THEN total END),0) AS d_today, " &
                    " COALESCE(SUM(CASE WHEN DATE(sale_datetime)=CURDATE() - INTERVAL 1 DAY THEN total END),0) AS d_yest, " &
                    " COALESCE(SUM(CASE WHEN YEARWEEK(sale_datetime,1)=YEARWEEK(CURDATE(),1) THEN total END),0) AS d_week, " &
                    " COALESCE(SUM(CASE WHEN YEARWEEK(sale_datetime,1)=YEARWEEK(CURDATE() - INTERVAL 7 DAY,1) THEN total END),0) AS d_lastweek, " &
                    " COALESCE(SUM(CASE WHEN YEAR(sale_datetime)=YEAR(CURDATE()) " &
                    "   AND MONTH(sale_datetime)=MONTH(CURDATE()) THEN total END),0) AS d_month, " &
                    " COALESCE(SUM(CASE WHEN YEAR(sale_datetime)=YEAR(CURDATE() - INTERVAL 1 MONTH) " &
                    "   AND MONTH(sale_datetime)=MONTH(CURDATE() - INTERVAL 1 MONTH) THEN total END),0) AS d_lastmonth, " &
                    " COALESCE(SUM(CASE WHEN DATE(sale_datetime)=CURDATE() THEN 1 ELSE 0 END),0) AS d_tx, " &
                    " COUNT(DISTINCT CASE WHEN DATE(sale_datetime)=CURDATE() THEN user_id END) AS d_cashiers " &
                    "FROM sales " &
                    "WHERE status IN ('Completed','Partially Refunded') AND sale_datetime >= CURDATE() - INTERVAL @win DAY"

                Using cmd As New MySqlCommand(sql, conn)
                    cmd.Parameters.AddWithValue("@win", 70)
                    Using rdr = cmd.ExecuteReader()
                        If rdr.Read() Then
                            _today = SafeDec(rdr("d_today"))
                            _yesterday = SafeDec(rdr("d_yest"))
                            _week = SafeDec(rdr("d_week"))
                            _lastWeek = SafeDec(rdr("d_lastweek"))
                            _month = SafeDec(rdr("d_month"))
                            _lastMonth = SafeDec(rdr("d_lastmonth"))
                            _txToday = SafeInt(rdr("d_tx"))
                            _cashiersToday = SafeInt(rdr("d_cashiers"))
                        End If
                    End Using
                End Using
            End Using
        Catch ex As Exception
            _kpiFailed = True
            Debug.WriteLine("AdminDash LoadKpis error: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub LoadTrend()
        _trendFailed = False
        _trend.Clear()

        ' Seed every day in the window at zero, then overlay what the DB returns
        ' so gaps render as 0 rather than collapsing the series.
        Dim byDay As New Dictionary(Of DateTime, Decimal)
        Dim startDay As DateTime = DateTime.Today.AddDays(-(TREND_DAYS - 1))
        For i As Integer = 0 To TREND_DAYS - 1
            byDay(startDay.AddDays(i)) = 0D
        Next

        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT DATE(sale_datetime) AS d, COALESCE(SUM(total),0) AS t " &
                    "FROM sales " &
                    "WHERE status IN ('Completed','Partially Refunded') AND sale_datetime >= CURDATE() - INTERVAL @days DAY " &
                    "GROUP BY DATE(sale_datetime) ORDER BY d", conn)
                    cmd.Parameters.AddWithValue("@days", TREND_DAYS - 1)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            If IsDBNull(rdr("d")) Then Continue While
                            Dim d As DateTime = Convert.ToDateTime(rdr("d")).Date
                            If byDay.ContainsKey(d) Then byDay(d) = SafeDec(rdr("t"))
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            _trendFailed = True
            Debug.WriteLine("AdminDash LoadTrend error: " & ex.Message)
        End Try

        For i As Integer = 0 To TREND_DAYS - 1
            Dim d As DateTime = startDay.AddDays(i)
            _trend.Add(New TrendPoint With {.Day = d, .Value = byDay(d)})
        Next
    End Sub

    Private Shared Sub LoadHealth()
        _healthFailed = False
        _lowStock = 0 : _pendingPos = 0 : _openShifts = 0

        Try
            Using conn = Db.GetConnection()
                conn.Open()

                Using cmd As New MySqlCommand(
                    "SELECT COUNT(*) FROM products " &
                    "WHERE is_active = 1 AND stock_qty * 100 <= reorder_level * 125", conn)
                    _lowStock = SafeInt(cmd.ExecuteScalar())
                End Using

                Using cmd As New MySqlCommand(
                    "SELECT COUNT(*) FROM purchase_orders " &
                    "WHERE status IN ('Draft','Ordered','Partially Received')", conn)
                    _pendingPos = SafeInt(cmd.ExecuteScalar())
                End Using

                Using cmd As New MySqlCommand(
                    "SELECT COUNT(*) FROM shifts WHERE status = 'Open'", conn)
                    _openShifts = SafeInt(cmd.ExecuteScalar())
                End Using
            End Using
        Catch ex As Exception
            _healthFailed = True
            Debug.WriteLine("AdminDash LoadHealth error: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub LoadActivity()
        _activityFailed = False
        _activity.Clear()

        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT a.action, a.entity_type, a.entity_id, a.details, a.created_at, " &
                    "u.full_name " &
                    "FROM activity_logs a LEFT JOIN users u ON a.user_id = u.user_id " &
                    "ORDER BY a.log_id DESC LIMIT " & ACTIVITY_LIMIT, conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            _activity.Add(New ActivityEntry With {
                                .Action = SafeStr(rdr("action")),
                                .EntityType = SafeStr(rdr("entity_type")),
                                .EntityId = SafeInt(rdr("entity_id")),
                                .Details = SafeStr(rdr("details")),
                                .CreatedAt = If(IsDBNull(rdr("created_at")),
                                                DateTime.MinValue,
                                                Convert.ToDateTime(rdr("created_at"))),
                                .FullName = SafeStr(rdr("full_name"))
                            })
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            _activityFailed = True
            Debug.WriteLine("AdminDash LoadActivity error: " & ex.Message)
        End Try
    End Sub

    ' ── Reader helpers ────────────────────────────────────────────────────────
    Private Shared Function SafeDec(o As Object) As Decimal
        If o Is Nothing OrElse IsDBNull(o) Then Return 0D
        Return Convert.ToDecimal(o)
    End Function

    Private Shared Function SafeInt(o As Object) As Integer
        If o Is Nothing OrElse IsDBNull(o) Then Return 0
        Return Convert.ToInt32(o)
    End Function

    Private Shared Function SafeStr(o As Object) As String
        If o Is Nothing OrElse IsDBNull(o) Then Return ""
        Return o.ToString()
    End Function

    ' =========================================================================
    '  RENDER
    ' =========================================================================
    Private Shared Sub Render()
        _container.SuspendLayout()
        _container.Controls.Clear()
        _container.BackColor = MintBg

        Dim host As New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = MintBg,
            .AutoScroll = True,
            .Padding = New Padding(SIDE_PAD, 12, SIDE_PAD, SIDE_PAD)
        }

        ' Docked Top children stack in REVERSE add order (the convention the
        ' Accountant panels use), so add bottom section first.
        ' This leading spacer therefore lands BELOW the recent card, keeping it
        ' off the bottom edge of the scroll region. The host's bottom Padding
        ' can't do that on its own — padding only reserves space against a Fill
        ' child, and every section here is docked Top.
        host.Controls.Add(Spacer())
        host.Controls.Add(BuildRecentCard())
        host.Controls.Add(Spacer())
        host.Controls.Add(BuildHealthStrip())
        host.Controls.Add(Spacer())
        host.Controls.Add(BuildTrendCard())
        host.Controls.Add(Spacer())
        host.Controls.Add(BuildKpiRow())
        host.Controls.Add(Spacer())
        host.Controls.Add(BuildHeaderRow())

        _container.Controls.Add(host)
        _container.ResumeLayout()
    End Sub

    Private Shared Function Spacer() As Panel
        Return New Panel With {
            .Dock = DockStyle.Top, .Height = 16, .BackColor = MintBg
        }
    End Function

    ' =========================================================================
    '  1 — HEADER ROW
    ' =========================================================================
    Private Shared Function BuildHeaderRow() As Panel
        Dim row As New Panel With {
            .Dock = DockStyle.Top, .Height = 84, .BackColor = MintBg
        }

        Dim hour As Integer = DateTime.Now.Hour
        Dim partOfDay As String =
            If(hour < 12, "morning", If(hour < 18, "afternoon", "evening"))
        Dim who As String = If(String.IsNullOrWhiteSpace(Session.CurrentFullName),
                               "there", Session.CurrentFullName)

        Dim lblGreet As New Label With {
            .Text = "Good " & partOfDay & ", " & who,
            .Font = New Font("Segoe UI", 24, FontStyle.Bold),
            .ForeColor = MintDark,
            .AutoSize = True,
            .Location = New Point(0, 8),
            .BackColor = Color.Transparent
        }
        Dim lblDate As New Label With {
            .Text = DateTime.Now.ToString("dddd, MMMM d, yyyy"),
            .Font = New Font("Segoe UI", 10.5F),
            .ForeColor = SubText,
            .AutoSize = True,
            .Location = New Point(2, 50),
            .BackColor = Color.Transparent
        }

        ' "N staff on shift" pill
        Dim pillText As String =
            If(_healthFailed, "shift count unavailable",
               _openShifts.ToString() & If(_openShifts = 1, " staff on shift", " staff on shift"))
        Dim pill As New Label With {
            .Text = pillText,
            .Font = New Font("Segoe UI Semibold", 9.5F),
            .ForeColor = If(_healthFailed, FaintText, TintMint.fore),
            .BackColor = If(_healthFailed, Color.FromArgb(240, 242, 241), TintMint.back),
            .AutoSize = True,
            .Padding = New Padding(12, 6, 12, 6),
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        RoundLabel(pill, 12)

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
        AddHandler btnRefresh.Paint, AddressOf PaintPillButton
        AddHandler btnRefresh.MouseEnter, Sub()
                                              btnRefresh.BackColor = Color.FromArgb(210, 240, 235)
                                              btnRefresh.Invalidate()
                                          End Sub
        AddHandler btnRefresh.MouseLeave, Sub()
                                              btnRefresh.BackColor = Color.FromArgb(220, 235, 232)
                                              btnRefresh.Invalidate()
                                          End Sub
        AddHandler btnRefresh.Click, Sub() LoadDashboard(_container)

        AddHandler row.Resize,
            Sub()
                btnRefresh.Left = row.Width - btnRefresh.Width
                btnRefresh.Top = 16
                pill.Left = btnRefresh.Left - pill.Width - 10
                pill.Top = 16 + (btnRefresh.Height - pill.Height) \ 2
            End Sub

        row.Controls.Add(lblGreet)
        row.Controls.Add(lblDate)
        row.Controls.Add(pill)
        row.Controls.Add(btnRefresh)
        Return row
    End Function

    Private Shared Sub PaintPillButton(s As Object, e As PaintEventArgs)
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

    ' =========================================================================
    '  2 — KPI ROW
    ' =========================================================================
    Private Shared Function BuildKpiRow() As Panel
        Dim bar As New Panel With {
            .Dock = DockStyle.Top, .Height = 150, .BackColor = MintBg
        }

        Dim grid As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .ColumnCount = 4,
            .RowCount = 1,
            .BackColor = Color.Transparent,
            .CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        }
        For i As Integer = 0 To 3
            grid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 25.0F))
        Next
        grid.RowStyles.Add(New RowStyle(SizeType.Percent, 100))

        Dim todayDelta = DeltaOf(_today, _yesterday)
        Dim weekDelta = DeltaOf(_week, _lastWeek)
        Dim monthDelta = DeltaOf(_month, _lastMonth)

        Dim c1 = MakeKpiCard("Sales Today", Money(_today), MintPrimary,
                             todayDelta.text, todayDelta.clr, "vs yesterday")
        Dim c2 = MakeKpiCard("This Week", Money(_week), MintDeep,
                             weekDelta.text, weekDelta.clr, "vs last week")
        Dim c3 = MakeKpiCard("This Month", Money(_month), MintDeep,
                             monthDelta.text, monthDelta.clr, "vs last month")
        Dim c4 = MakeKpiCard("Transactions", _txToday.ToString("N0"), MintDark,
                             "", SubText,
                             "across " & _cashiersToday.ToString() &
                             If(_cashiersToday = 1, " cashier", " cashiers"))

        c1.Margin = New Padding(0, 0, 6, 0)
        c2.Margin = New Padding(6, 0, 6, 0)
        c3.Margin = New Padding(6, 0, 6, 0)
        c4.Margin = New Padding(6, 0, 0, 0)

        grid.Controls.Add(c1, 0, 0)
        grid.Controls.Add(c2, 1, 0)
        grid.Controls.Add(c3, 2, 0)
        grid.Controls.Add(c4, 3, 0)

        bar.Controls.Add(grid)
        Return bar
    End Function

    Private Shared Function MakeKpiCard(caption As String, value As String,
                                        valueClr As Color,
                                        deltaText As String, deltaClr As Color,
                                        subLabel As String) As Panel
        Dim card As New BufferedPanel With {.Dock = DockStyle.Fill, .BackColor = CardBg}
        AddHandler card.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)

        Dim lblCap As New Label With {
            .Text = caption,
            .Font = New Font("Segoe UI", 11),
            .ForeColor = SubText,
            .AutoSize = True,
            .Location = New Point(22, 16),
            .BackColor = Color.Transparent
        }
        card.Controls.Add(lblCap)

        If _kpiFailed Then
            card.Controls.Add(New Label With {
                .Text = "Couldn't load",
                .Font = New Font("Segoe UI", 12),
                .ForeColor = FaintText,
                .AutoSize = True,
                .Location = New Point(22, 52),
                .BackColor = Color.Transparent
            })
            Return card
        End If

        Dim lblVal As New Label With {
            .Text = value,
            .Font = New Font("Segoe UI", 24, FontStyle.Bold),
            .ForeColor = valueClr,
            .AutoSize = True,
            .Location = New Point(20, 42),
            .BackColor = Color.Transparent
        }
        card.Controls.Add(lblVal)

        Dim y As Integer = 92
        If deltaText <> "" Then
            Dim lblDelta As New Label With {
                .Text = deltaText,
                .Font = New Font("Segoe UI Semibold", 10),
                .ForeColor = deltaClr,
                .AutoSize = True,
                .Location = New Point(22, y),
                .BackColor = Color.Transparent
            }
            card.Controls.Add(lblDelta)

            Dim lblSub As New Label With {
                .Text = subLabel,
                .Font = New Font("Segoe UI", 9.5F),
                .ForeColor = FaintText,
                .AutoSize = True,
                .BackColor = Color.Transparent
            }
            card.Controls.Add(lblSub)
            lblSub.Location = New Point(22 + lblDelta.PreferredWidth + 6, y + 1)
        Else
            card.Controls.Add(New Label With {
                .Text = subLabel,
                .Font = New Font("Segoe UI", 9.5F),
                .ForeColor = FaintText,
                .AutoSize = True,
                .Location = New Point(22, y),
                .BackColor = Color.Transparent
            })
        End If

        Return card
    End Function

    ' Percentage change, rounded to whole %. Green up / amber down.
    Private Shared Function DeltaOf(current As Decimal, prior As Decimal) As (text As String, clr As Color)
        If prior = 0D Then
            If current = 0D Then Return ("— 0%", FaintText)
            Return ("▲ new", MintPrimary)
        End If
        Dim pct As Decimal = (current - prior) / Math.Abs(prior) * 100D
        Dim whole As Integer = CInt(Math.Round(pct, 0, MidpointRounding.AwayFromZero))
        If whole > 0 Then Return ("▲ " & whole.ToString() & "%", MintPrimary)
        If whole < 0 Then Return ("▼ " & Math.Abs(whole).ToString() & "%", AccentAmber)
        Return ("— 0%", FaintText)
    End Function

    Private Shared Function Money(v As Decimal) As String
        Return Peso & v.ToString("N2")
    End Function

    ' =========================================================================
    '  3 — SALES TREND CARD
    ' =========================================================================
    Private Shared Function BuildTrendCard() As Panel
        Dim wrap As New Panel With {
            .Dock = DockStyle.Top, .Height = 300, .BackColor = MintBg
        }

        Dim card As New BufferedPanel With {.Dock = DockStyle.Fill, .BackColor = CardBg}

        Dim lblTitle As New Label With {
            .Text = "Sales · Last 30 days",
            .Font = New Font("Segoe UI Semibold", 13),
            .ForeColor = MintDark,
            .AutoSize = True,
            .Location = New Point(22, 18),
            .BackColor = Color.Transparent
        }
        card.Controls.Add(lblTitle)

        If _trendFailed Then
            card.Controls.Add(New Label With {
                .Text = "Couldn't load the sales trend.",
                .Font = New Font("Segoe UI", 10.5F),
                .ForeColor = FaintText,
                .AutoSize = True,
                .Location = New Point(22, 60),
                .BackColor = Color.Transparent
            })
            AddHandler card.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)
        Else
            AddHandler card.Paint,
                Sub(s, e)
                    PanelRenderer.Draw(CType(s, Panel), e, True)
                    DrawTrend(CType(s, Panel), e)
                    PanelRenderer.Draw(CType(s, Panel), e, False)
                End Sub
            AddHandler card.Resize, Sub() card.Invalidate()
        End If

        wrap.Controls.Add(card)
        Return wrap
    End Function

    ' Follows AnalyticsPanelBuilder.DrawSalesGraph's approach, against real data.
    Private Shared Sub DrawTrend(panel As Panel, e As PaintEventArgs)
        If _trend.Count = 0 Then Return

        Dim g = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias

        Const leftMargin As Integer = 74
        Const rightMargin As Integer = 28
        Const topMargin As Integer = 62
        Const bottomMargin As Integer = 44

        Dim rect As Rectangle = panel.ClientRectangle
        Dim gw As Integer = rect.Width - leftMargin - rightMargin
        Dim gh As Integer = rect.Height - topMargin - bottomMargin
        If gw <= 10 OrElse gh <= 10 Then Return

        ' Scale
        Dim peak As Decimal = 0D
        For Each p In _trend
            If p.Value > peak Then peak = p.Value
        Next
        Dim maxVal As Decimal = NiceMax(peak)

        ' Gridlines + y labels (4 steps)
        Using gridPen As New Pen(GridLine, 1)
            Using lblBrush As New SolidBrush(FaintText)
                Using lblFont As New Font("Segoe UI", 9)
                    For i As Integer = 4 To 1 Step -1
                        Dim stepVal As Decimal = maxVal * i / 4D
                        Dim y As Single = topMargin + gh - CSng(stepVal / maxVal) * gh
                        g.DrawLine(gridPen, CSng(leftMargin), y, CSng(leftMargin + gw), y)
                        g.DrawString(ShortMoney(stepVal), lblFont, lblBrush, 16, y - 8)
                    Next
                    ' Baseline
                    g.DrawLine(gridPen, CSng(leftMargin), CSng(topMargin + gh),
                               CSng(leftMargin + gw), CSng(topMargin + gh))
                End Using
            End Using
        End Using

        ' Points
        Dim pts(_trend.Count - 1) As PointF
        For i As Integer = 0 To _trend.Count - 1
            Dim x As Single = leftMargin + CSng(i / (_trend.Count - 1)) * gw
            Dim y As Single = topMargin + gh - CSng(_trend(i).Value / maxVal) * gh
            pts(i) = New PointF(x, y)
        Next

        ' Area fill under the line
        Dim area As New List(Of PointF)(pts)
        area.Add(New PointF(pts(pts.Length - 1).X, topMargin + gh))
        area.Add(New PointF(pts(0).X, topMargin + gh))
        Using areaBrush As New SolidBrush(Color.FromArgb(38, MintPrimary))
            g.FillPolygon(areaBrush, area.ToArray())
        End Using

        ' Line
        Using linePen As New Pen(MintPrimary, 2.5F)
            linePen.LineJoin = LineJoin.Round
            g.DrawLines(linePen, pts)
        End Using

        ' Dots + x labels every 5th day (30 points is too dense for every one)
        Using dotBrush As New SolidBrush(MintPrimary)
            Using lblBrush As New SolidBrush(FaintText)
                Using lblFont As New Font("Segoe UI", 8.5F)
                    For i As Integer = 0 To _trend.Count - 1
                        If i Mod 5 <> 0 AndAlso i <> _trend.Count - 1 Then Continue For
                        g.FillEllipse(dotBrush, pts(i).X - 3.5F, pts(i).Y - 3.5F, 7, 7)
                        Dim lbl As String = _trend(i).Day.ToString("M/d")
                        Dim tw As Single = g.MeasureString(lbl, lblFont).Width
                        g.DrawString(lbl, lblFont, lblBrush,
                                     pts(i).X - tw / 2, topMargin + gh + 10)
                    Next
                End Using
            End Using
        End Using
    End Sub

    ' Rounds a peak up to a readable axis maximum.
    Private Shared Function NiceMax(v As Decimal) As Decimal
        If v <= 0D Then Return 1000D
        Dim d As Double = CDbl(v)
        Dim mag As Double = Math.Pow(10, Math.Floor(Math.Log10(d)))
        Dim n As Double = Math.Ceiling(d / mag * 2.0R) / 2.0R * mag
        If n <= 0 Then Return 1000D
        Return CDec(n)
    End Function

    Private Shared Function ShortMoney(v As Decimal) As String
        If v >= 1000000D Then Return Peso & (v / 1000000D).ToString("0.#") & "M"
        If v >= 1000D Then Return Peso & (v / 1000D).ToString("0.#") & "k"
        Return Peso & v.ToString("0")
    End Function

    ' =========================================================================
    '  4 — STORE HEALTH STRIP
    ' =========================================================================
    Private Shared Function BuildHealthStrip() As Panel
        Dim bar As New Panel With {
            .Dock = DockStyle.Top, .Height = 120, .BackColor = MintBg
        }

        Dim grid As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .ColumnCount = 3,
            .RowCount = 1,
            .BackColor = Color.Transparent,
            .CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        }
        For i As Integer = 0 To 2
            grid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 33.33F))
        Next
        grid.RowStyles.Add(New RowStyle(SizeType.Percent, 100))

        Dim t1 = MakeHealthTile("Low Stock", _lowStock,
                                "items at or near reorder level", TintRed, "inventory")
        Dim t2 = MakeHealthTile("Pending POs", _pendingPos,
                                "purchase orders awaiting receipt", TintAmber, "procurement")
        Dim t3 = MakeHealthTile("Open Shifts", _openShifts,
                                "registers currently open", TintMint, "")

        t1.Margin = New Padding(0, 0, 6, 0)
        t2.Margin = New Padding(6, 0, 6, 0)
        t3.Margin = New Padding(6, 0, 0, 0)

        grid.Controls.Add(t1, 0, 0)
        grid.Controls.Add(t2, 1, 0)
        grid.Controls.Add(t3, 2, 0)

        bar.Controls.Add(grid)
        Return bar
    End Function

    Private Shared Function MakeHealthTile(caption As String, count As Integer,
                                           subLabel As String,
                                           tint As (fore As Color, back As Color),
                                           navKey As String) As Panel
        Dim tile As New BufferedPanel With {
            .Dock = DockStyle.Fill,
            .BackColor = tint.back,
            .Cursor = If(navKey = "", Cursors.Default, Cursors.Hand)
        }

        Dim hovering As Boolean = False
        AddHandler tile.Paint,
            Sub(s, e)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim rc = tile.ClientRectangle : rc.Inflate(-1, -1)
                Using br As New SolidBrush(tile.BackColor)
                    g.FillRoundedRectangle(br, rc, 14)
                End Using
                Using pen As New Pen(Color.FromArgb(60, tint.fore), If(hovering, 1.8F, 1.0F))
                    g.DrawRoundedRectangle(pen, rc, 14)
                End Using
            End Sub

        Dim lblCap As New Label With {
            .Text = caption,
            .Font = New Font("Segoe UI Semibold", 11),
            .ForeColor = tint.fore,
            .AutoSize = True,
            .Location = New Point(22, 16),
            .BackColor = Color.Transparent
        }
        tile.Controls.Add(lblCap)

        If _healthFailed Then
            tile.Controls.Add(New Label With {
                .Text = "Couldn't load",
                .Font = New Font("Segoe UI", 11),
                .ForeColor = FaintText,
                .AutoSize = True,
                .Location = New Point(22, 48),
                .BackColor = Color.Transparent
            })
            Return tile
        End If

        Dim lblCount As New Label With {
            .Text = count.ToString("N0"),
            .Font = New Font("Segoe UI", 26, FontStyle.Bold),
            .ForeColor = tint.fore,
            .AutoSize = True,
            .Location = New Point(20, 40),
            .BackColor = Color.Transparent
        }
        Dim lblSub As New Label With {
            .Text = subLabel,
            .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = Color.FromArgb(180, tint.fore),
            .AutoSize = True,
            .Location = New Point(22, 86),
            .BackColor = Color.Transparent
        }
        tile.Controls.AddRange({lblCount, lblSub})

        If navKey <> "" Then
            Dim go As Action =
                Sub()
                    ' No-op when ADMIN_STAFF hasn't wired navigation.
                    Dim nav = NavigateTo
                    If nav Is Nothing Then Return
                    Try
                        nav(navKey)
                    Catch ex As Exception
                        Debug.WriteLine("AdminDash navigate error: " & ex.Message)
                    End Try
                End Sub
            AddHandler tile.Click, Sub() go()
            For Each c As Control In tile.Controls
                AddHandler c.Click, Sub() go()
                AddHandler c.MouseEnter, Sub()
                                             hovering = True
                                             tile.Invalidate()
                                         End Sub
            Next
            AddHandler tile.MouseEnter, Sub()
                                            hovering = True
                                            tile.Invalidate()
                                        End Sub
            AddHandler tile.MouseLeave, Sub()
                                            hovering = False
                                            tile.Invalidate()
                                        End Sub
        End If

        Return tile
    End Function

    ' =========================================================================
    '  5 — RECENT ACTIVITY
    ' =========================================================================
    Private Shared Function BuildRecentCard() As Panel
        ' Height follows the row count instead of a fixed 420, which clipped rows
        ' and let the last one bleed over the card's bottom edge.
        Dim visibleRows As Integer = Math.Min(_activity.Count, ACT_MAX_ROWS)
        Dim contentH As Integer =
            If(_activityFailed OrElse _activity.Count = 0, 80, visibleRows * ACT_ROW_H)

        Dim wrap As New Panel With {
            .Dock = DockStyle.Top,
            .Height = ACT_HEADER_H + ACT_PAD_TOP + contentH + ACT_PAD_BOTTOM + 2,
            .BackColor = MintBg
        }

        Dim card As New BufferedPanel With {
            .Dock = DockStyle.Fill,
            .BackColor = CardBg,
            .Padding = New Padding(1)
        }
        AddHandler card.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)

        Dim header As New Panel With {
            .Dock = DockStyle.Top, .Height = ACT_HEADER_H, .BackColor = Color.Transparent
        }
        header.Controls.Add(New Label With {
            .Text = "Recent Activity",
            .Font = New Font("Segoe UI Semibold", 13),
            .ForeColor = MintDark,
            .AutoSize = True,
            .Location = New Point(22, 18),
            .BackColor = Color.Transparent
        })
        AddHandler header.Paint,
            Sub(s, e)
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 20, header.Height - 1, header.Width - 20, header.Height - 1)
                End Using
            End Sub

        ' Keeps the last row clear of the card's rounded bottom edge so the
        ' outline reads as a closed container.
        Dim bottomPad As New Panel With {
            .Dock = DockStyle.Bottom,
            .Height = ACT_PAD_BOTTOM,
            .BackColor = Color.Transparent
        }

        Dim list As New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = Color.Transparent,
            .AutoScroll = True,
            .Padding = New Padding(0)
        }

        If _activityFailed Then
            list.Controls.Add(MessageRow("Couldn't load recent activity."))
        ElseIf _activity.Count = 0 Then
            list.Controls.Add(MessageRow("No activity recorded yet."))
        Else
            Dim y As Integer = ACT_PAD_TOP
            For Each entry As ActivityEntry In _activity
                Dim row As Panel = BuildActivityRow(entry, ACT_ROW_H)
                row.Location = New Point(ACT_PAD_X, y)
                row.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
                list.Controls.Add(row)
                row.Width = list.ClientSize.Width - ACT_PAD_X * 2
                y += ACT_ROW_H
            Next
            AddHandler list.Resize,
                Sub()
                    For Each c As Control In list.Controls
                        c.Width = list.ClientSize.Width - ACT_PAD_X * 2
                    Next
                End Sub
        End If

        card.Controls.Add(bottomPad)
        card.Controls.Add(header)
        card.Controls.Add(list)
        header.BringToFront()
        list.BringToFront()
        list.AutoScrollMinSize = New Size(0, 1)

        wrap.Controls.Add(card)
        Return wrap
    End Function

    Private Shared Function MessageRow(text As String) As Panel
        Dim p As New Panel With {
            .Height = 80, .Dock = DockStyle.Top, .BackColor = Color.Transparent
        }
        p.Controls.Add(New Label With {
            .Text = text,
            .Font = New Font("Segoe UI", 10.5F),
            .ForeColor = FaintText,
            .AutoSize = True,
            .Location = New Point(22, 26),
            .BackColor = Color.Transparent
        })
        Return p
    End Function

    Private Shared Function BuildActivityRow(entry As ActivityEntry, height As Integer) As Panel
        Dim p As New BufferedPanel With {
            .Height = height, .BackColor = Color.Transparent
        }

        AddHandler p.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                If TypeOf p.Tag Is Boolean AndAlso CBool(p.Tag) Then
                    Dim rc As New Rectangle(8, 3, p.Width - 16, height - 6)
                    Using br As New SolidBrush(RowHover)
                        e.Graphics.FillRoundedRectangle(br, rc, 10)
                    End Using
                End If
                ' Inset to the padded content width — the row itself is already
                ' inset by ACT_PAD_X, so this lands at 22px from the card edge,
                ' flush with the header rule above.
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 10, p.Height - 1, p.Width - 10, p.Height - 1)
                End Using
            End Sub

        Dim gl = GlyphFor(entry.Action)
        Dim glyph As New Label With {
            .Text = gl.ch,
            .Font = New Font("Segoe UI", 11, FontStyle.Bold),
            .ForeColor = gl.tint.fore,
            .BackColor = gl.tint.back,
            .Size = New Size(30, 30),
            .TextAlign = ContentAlignment.MiddleCenter,
            .Location = New Point(10, (height - 30) \ 2)
        }
        RoundLabel(glyph, 15)

        Dim lblText As New Label With {
            .Text = SentenceFor(entry),
            .Font = New Font("Segoe UI", 10.5F),
            .ForeColor = Color.FromArgb(45, 60, 56),
            .AutoSize = True,
            .BackColor = Color.Transparent
        }
        Dim lblTime As New Label With {
            .Text = RelativeTime(entry.CreatedAt),
            .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = FaintText,
            .AutoSize = True,
            .BackColor = Color.Transparent,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }

        p.Controls.AddRange({glyph, lblText, lblTime})

        AddHandler p.Resize,
            Sub()
                lblText.Left = 52
                lblText.Top = (height - lblText.Height) \ 2
                lblTime.Left = p.Width - lblTime.PreferredWidth - 12
                lblTime.Top = (height - lblTime.Height) \ 2
            End Sub

        PropagateHover(p)
        Return p
    End Function

    ' Glyph + tint per action family.
    Private Shared Function GlyphFor(action As String) As (ch As String, tint As (fore As Color, back As Color))
        Select Case action.ToUpper()
            Case "LOGIN" : Return ("→", TintMint)
            Case "LOGOUT" : Return ("←", (SubText, Color.FromArgb(238, 242, 240)))
            Case "USER_CREATE" : Return ("+", TintMint)
            Case "USER_EDIT" : Return ("✎", (Color.FromArgb(45, 80, 150), Color.FromArgb(232, 238, 250)))
            Case "USER_ENABLE" : Return ("✓", TintMint)
            Case "USER_DISABLE" : Return ("✕", TintRed)
            Case "USER_RESET_PW" : Return ("⟳", (Color.FromArgb(45, 80, 150), Color.FromArgb(232, 238, 250)))
            Case Else
                If action.ToUpper().StartsWith("SALE") Then Return (Peso, TintMint)
                Return ("•", (SubText, Color.FromArgb(238, 242, 240)))
        End Select
    End Function

    ' Turns action + details into something readable.
    Private Shared Function SentenceFor(entry As ActivityEntry) As String
        Dim who As String = If(String.IsNullOrWhiteSpace(entry.FullName), "Someone", entry.FullName)
        Dim d As String = entry.Details

        Select Case entry.Action.ToUpper()
            Case "LOGIN" : Return who & " logged in"
            Case "LOGOUT" : Return who & " logged out"
            Case "USER_CREATE"
                ' details already reads "Created <username> (<role>)"
                Return If(d <> "", who & " · " & d, who & " created a user")
            Case "USER_EDIT"
                Return who & " updated user" & If(d <> "", " " & d, "")
            Case "USER_ENABLE"
                Return who & " enabled" & If(d <> "", " " & d, " a user")
            Case "USER_DISABLE"
                Return who & " disabled" & If(d <> "", " " & d, " a user")
            Case "USER_RESET_PW"
                Return who & " reset the password for" & If(d <> "", " " & d, " a user")
            Case Else
                Dim pretty As String = entry.Action.Replace("_", " ").ToLower()
                If d <> "" Then Return who & " · " & pretty & " — " & d
                Return who & " · " & pretty
        End Select
    End Function

    Private Shared Function RelativeTime(t As DateTime) As String
        If t = DateTime.MinValue Then Return "—"
        Dim span As TimeSpan = DateTime.Now - t
        If span.TotalMinutes < 1 Then Return "just now"
        If span.TotalMinutes < 60 Then Return CInt(span.TotalMinutes).ToString() & "m ago"
        If span.TotalHours < 24 Then Return CInt(span.TotalHours).ToString() & "h ago"
        If span.TotalDays < 7 Then Return CInt(span.TotalDays).ToString() & "d ago"
        Return t.ToString("MMM d, h:mm tt")
    End Function

    ' =========================================================================
    '  SHARED HELPERS  (same implementations as the Accountant panels)
    ' =========================================================================
    Private Shared Sub RoundLabel(lbl As Label, radius As Integer)
        Dim apply As Action =
            Sub()
                Dim path As New GraphicsPath()
                Dim r As Integer = radius
                If lbl.Width <= r OrElse lbl.Height <= r Then Return
                path.AddArc(0, 0, r, r, 180, 90)
                path.AddArc(lbl.Width - r, 0, r, r, 270, 90)
                path.AddArc(lbl.Width - r, lbl.Height - r, r, r, 0, 90)
                path.AddArc(0, lbl.Height - r, r, r, 90, 90)
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
