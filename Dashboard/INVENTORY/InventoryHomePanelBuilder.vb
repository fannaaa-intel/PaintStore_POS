Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports MySql.Data.MySqlClient

' =============================================================================
'  InventoryHomePanelBuilder  —  the Inventory dashboard
'
'    1. Greeting + live clock
'    2. Four stat cards (Total / In Stock / Low / Out) -> Item List, filtered
'    3. Two charts (owner's revision, Nov 2026 -- they replace the Quick
'       Actions bar and the Low & Out of Stock / Recent Stock Movements lists):
'         Low & Out of Stock      items at or below restock level, worst first;
'                                 click a bar -> Stock Adjustment for that item
'         Stock Movements         stock in / out per day, last 14 days, from the
'                                 stock_movements rows the old list showed;
'                                 click -> Transaction History
'
'  The low / out rule is LowStockAlert.NeedsRestock (at, or within 25%
'  above, the restock level) -- the same rule as the Low Stock Alerts badge,
'  Item List, Stock Levels and Admin -- so the chart lists exactly the items
'  the Low + Out cards count, and the badge number agrees with them. Movement totals leave dock receipts out, like every other
'  movement total (see InventoryMovements).
'
'  Everything sizes to the screen: the chart row takes whatever height is
'  left below the stat cards (never less than CHART_MIN_H), so on a laptop
'  the whole dashboard fits without scrolling.
' =============================================================================
Public Class InventoryHomePanelBuilder

    Private Shared ReadOnly MintDark As Color = Theme.Dark
    Private Shared ReadOnly MintPrimary As Color = Theme.Primary
    Private Shared ReadOnly MintBg As Color = Theme.PageBg
    Private Shared ReadOnly CardBg As Color = Color.White
    Private Shared ReadOnly CardBorder As Color = Theme.BorderSoft
    Private Shared ReadOnly SubText As Color = Theme.SubText
    Private Shared ReadOnly TileBg As Color = Theme.TileBg
    Private Shared ReadOnly MintGreen As Color = Color.FromArgb(0, 160, 106)
    Private Shared ReadOnly AccentYellow As Color = Color.FromArgb(200, 140, 0)
    Private Shared ReadOnly AccentRed As Color = Theme.Danger

    Private Const MOVE_DAYS As Integer = 14
    Private Const LOW_ROWS As Integer = 8
    Private Const CHART_MIN_H As Integer = 360

    ' ── LIVE CONTROLS ──────────────────────────────────────────────────────────
    Private Shared clockTimer As Timer
    Private Shared lblClock As Label
    Private Shared shiftStartTime As DateTime

    ' ── ICONS ──────────────────────────────────────────────────────────────────
    Private Shared ReadOnly IconNotif As String = "notification.png"
    Private Shared ReadOnly IconActivity As String = "recent.png"
    Private Shared ReadOnly IconClock As String = "clock.png"

    ' Cleared (and disposed) at the top of every LoadHome.
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
    '  DATA
    ' =========================================================================
    Private Class DashData
        Public Total As Integer
        Public InStock As Integer
        Public Low As Integer
        Public Out As Integer
        Public LowItems As New List(Of LowStockChart.Item)
        Public Days As New List(Of MovementChart.DayPoint)
        Public Failed As Boolean
    End Class

    Private Shared Function LoadData() As DashData
        Dim d As New DashData()
        ' Every day of the window, so a quiet day still gets its slot.
        Dim first As Date = Date.Today.AddDays(-(MOVE_DAYS - 1))
        For i As Integer = 0 To MOVE_DAYS - 1
            d.Days.Add(New MovementChart.DayPoint With {.Day = first.AddDays(i)})
        Next

        Try
            Using conn = Db.GetConnection()
                conn.Open()

                Dim needs As String = LowStockAlert.NeedsRestockSql()
                Using cmd As New MySqlCommand(
                    "SELECT COUNT(*) AS total, " &
                    " COALESCE(SUM(CASE WHEN NOT " & needs & " THEN 1 ELSE 0 END), 0) AS in_stock, " &
                    " COALESCE(SUM(CASE WHEN " & needs & " AND stock_qty > 0 THEN 1 ELSE 0 END), 0) AS low_stock, " &
                    " COALESCE(SUM(CASE WHEN stock_qty <= 0 THEN 1 ELSE 0 END), 0) AS out_stock " &
                    "FROM products WHERE is_active = 1", conn)
                    Using rdr = cmd.ExecuteReader()
                        If rdr.Read() Then
                            d.Total = SafeInt(rdr("total"))
                            d.InStock = SafeInt(rdr("in_stock"))
                            d.Low = SafeInt(rdr("low_stock"))
                            d.Out = SafeInt(rdr("out_stock"))
                        End If
                    End Using
                End Using

                ' Worst first: furthest below restock level, then fewest on hand.
                Using cmd As New MySqlCommand(
                    "SELECT product_id, name, sku, stock_qty, reorder_level " &
                    "FROM products WHERE is_active = 1 AND " & needs & " " &
                    "ORDER BY (stock_qty <= 0) DESC, stock_qty / GREATEST(reorder_level, 1) ASC, name ASC " &
                    "LIMIT @lim", conn)
                    cmd.Parameters.AddWithValue("@lim", LOW_ROWS)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            d.LowItems.Add(New LowStockChart.Item With {
                                .ProductId = SafeInt(rdr("product_id")),
                                .Name = rdr("name").ToString(),
                                .Sku = If(IsDBNull(rdr("sku")), "", rdr("sku").ToString()),
                                .Qty = SafeInt(rdr("stock_qty")),
                                .Restock = SafeInt(rdr("reorder_level"))
                            })
                        End While
                    End Using
                End Using

                Using cmd As New MySqlCommand(
                    "SELECT DATE(m.created_at) AS d, " &
                    " COALESCE(SUM(CASE WHEN m.qty_change > 0 THEN m.qty_change END), 0) AS ins, " &
                    " COALESCE(SUM(CASE WHEN m.qty_change < 0 THEN m.qty_change END), 0) AS outs " &
                    "FROM stock_movements m " &
                    "WHERE m.created_at >= @from AND NOT " & InventoryMovements.DOCK_SQL & " " &
                    "GROUP BY DATE(m.created_at)", conn)
                    cmd.Parameters.AddWithValue("@from", first)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            Dim day As Date = Convert.ToDateTime(rdr("d")).Date
                            Dim slot = d.Days.FirstOrDefault(Function(p) p.Day = day)
                            If slot IsNot Nothing Then
                                slot.UnitsIn = SafeInt(rdr("ins"))
                                slot.UnitsOut = SafeInt(rdr("outs"))
                            End If
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            d.Failed = True
            AppModal.Show(
                "Dashboard data could not be loaded." & Environment.NewLine &
                "Check your database connection and try again." & Environment.NewLine & Environment.NewLine &
                ErrorLog.ForUser(ex),
                "Load Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
        Return d
    End Function

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Shared Sub LoadHome(container As Panel, shiftStart As DateTime)
        shiftStartTime = shiftStart

        StopTimer()
        AddHandler container.Disposed, Sub() StopTimer()

        ImageCache.ClearAndDispose(IconCache, container)

        container.SuspendLayout()
        container.Controls.Clear()
        container.BackColor = MintBg

        Dim scroll As New Panel With {
            .Dock = DockStyle.Fill, .AutoScroll = True, .BackColor = MintBg,
            .Padding = New Padding(0, 22, 24, 8)
        }
        container.Controls.Add(scroll)

        Dim wrap As New Panel With {.BackColor = Color.Transparent, .Left = 18, .Top = 0}
        scroll.Controls.Add(wrap)

        Dim data As DashData = LoadData()

        Const GAP As Integer = 16
        Const CARD_INSET As Integer = 3

        ' ── 1. TOP BAR ───────────────────────────────────────────────────────
        Dim topBar As New Panel With {.Height = 100, .Top = 0, .Left = 0, .BackColor = Color.Transparent}

        Dim firstName As String = If(Session.CurrentFullName, "")
        If firstName.Contains(" ") Then firstName = firstName.Split(" "c)(0)

        Dim lblGreet As New Label With {
            .Text = GetGreeting() & If(firstName <> "", ", " & firstName, "") & "!",
            .Font = New Font("Segoe UI", 28, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(0, 4), .UseMnemonic = False
        }
        Dim lblDate As New Label With {
            .Text = DateTime.Now.ToString("MMMM dd, yyyy — dddd") & "   •   Inventory",
            .Font = New Font("Segoe UI", 12), .ForeColor = SubText,
            .AutoSize = True, .Location = New Point(2, 60)
        }

        Dim clockChip As New BufferedPanel With {.Size = New Size(190, 64), .BackColor = CardBg}
        AddHandler clockChip.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)
        Dim lblClockTag As New Label With {
            .Text = "CURRENT TIME", .Font = New Font("Segoe UI", 8, FontStyle.Bold),
            .ForeColor = SubText, .AutoSize = True, .Location = New Point(58, 12)
        }
        lblClock = New Label With {
            .Text = DateTime.Now.ToString("hh:mm tt"), .Font = New Font("Segoe UI", 16, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(58, 30)
        }
        Dim clockImg As Image = GetIcon(IconClock)
        Dim clockIcon As Control
        If clockImg IsNot Nothing Then
            clockIcon = New PictureBox With {
                .Image = clockImg, .SizeMode = PictureBoxSizeMode.Zoom, .Size = New Size(20, 20),
                .BackColor = Color.Transparent, .Location = New Point(25, 22)
            }
        Else
            clockIcon = New Label With {
                .Text = "🕐", .Font = New Font("Segoe UI", 15), .ForeColor = MintDark,
                .BackColor = Color.Transparent, .AutoSize = False,
                .Size = New Size(38, 38), .TextAlign = ContentAlignment.MiddleCenter,
                .Location = New Point(14, 14)
            }
        End If
        clockChip.Controls.AddRange({lblClockTag, lblClock, clockIcon})
        topBar.Controls.AddRange({lblGreet, lblDate, clockChip})
        wrap.Controls.Add(topBar)

        clockTimer = New Timer With {.Interval = 1000}
        AddHandler clockTimer.Tick,
            Sub()
                If lblClock IsNot Nothing AndAlso Not lblClock.IsDisposed Then
                    lblClock.Text = DateTime.Now.ToString("hh:mm tt")
                End If
            End Sub
        clockTimer.Start()

        ' ── 2. STAT ROW ──────────────────────────────────────────────────────
        Dim statRow As New TableLayoutPanel With {
            .Top = topBar.Bottom + GAP, .Left = 0, .Height = 140,
            .ColumnCount = 4, .RowCount = 1, .BackColor = Color.Transparent,
            .CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        }
        For i = 0 To 3
            statRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 25))
        Next

        Dim statData() As (title As String, value As Integer, subtitle As String, badge As String, valColor As Color, target As String) = {
            ("Total Products", data.Total, "Active items", "Items", MintDark, ""),
            ("In Stock", data.InStock, "Comfortably above restock", "Good", MintGreen, "in"),
            ("Low Stock", data.Low, "At or near restock level", "Low", AccentYellow, "low"),
            ("Out of Stock", data.Out, "Zero on hand", "Out", AccentRed, "out")
        }

        For i As Integer = 0 To 3
            Dim sd = statData(i)
            Dim card As New BufferedPanel With {
                .Dock = DockStyle.Fill, .BackColor = CardBg,
                .Margin = New Padding(0, 0, If(i < 3, 12, 0), 0),
                .Cursor = Cursors.Hand
            }
            AddHandler card.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)

            Dim lTitle As New Label With {
                .Text = sd.title, .Font = New Font("Segoe UI", 11),
                .ForeColor = SubText, .AutoSize = True, .Location = New Point(22, 14)
            }
            Dim lValue As New Label With {
                .Text = If(data.Failed, "—", sd.value.ToString("N0")),
                .Font = New Font("Segoe UI", 26, FontStyle.Bold),
                .ForeColor = sd.valColor, .AutoSize = True, .Location = New Point(20, 36)
            }
            Dim lSub As New Label With {
                .Text = sd.subtitle, .Font = New Font("Segoe UI", 10),
                .ForeColor = SubText, .AutoSize = True, .Location = New Point(22, 100)
            }
            Dim badge As New Label With {
                .Text = sd.badge, .Font = New Font("Segoe UI", 9, FontStyle.Bold),
                .ForeColor = sd.valColor, .BackColor = TileBg,
                .AutoSize = True, .Padding = New Padding(6, 3, 6, 3)
            }
            card.Controls.AddRange({lTitle, lValue, lSub, badge})
            AddHandler card.Resize,
                Sub()
                    badge.Left = card.Width - badge.Width - 14
                    badge.Top = 14
                End Sub

            Dim capStatus As String = sd.target
            Dim capCard As BufferedPanel = card
            Dim openFiltered As EventHandler = Sub(s, e) OpenItemListFiltered(capCard, "", capStatus)
            AddHandler card.Click, openFiltered
            For Each child As Control In card.Controls
                child.Cursor = Cursors.Hand
                AddHandler child.Click, openFiltered
            Next
            statRow.Controls.Add(card, i, 0)
        Next
        wrap.Controls.Add(statRow)

        ' ── 3. CHART ROW ─────────────────────────────────────────────────────
        Dim chartRow As New TableLayoutPanel With {
            .Top = statRow.Bottom + GAP, .Left = 0, .Height = CHART_MIN_H,
            .ColumnCount = 2, .RowCount = 1, .BackColor = Color.Transparent,
            .CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        }
        chartRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
        chartRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
        chartRow.RowStyles.Add(New RowStyle(SizeType.Percent, 100))

        ' Low & Out of Stock
        Dim lowChart As New LowStockChart With {.Dock = DockStyle.Fill}
        lowChart.SetItems(data.LowItems)
        AddHandler lowChart.ItemClicked, Sub(pid As Integer) OpenStockAdjustment(lowChart, pid)
        Dim lowCount As Integer = data.Low + data.Out
        Dim lowSub As String
        If lowCount = 0 Then
            lowSub = "Nothing at or near its restock level"
        ElseIf lowCount > data.LowItems.Count Then
            lowSub = "Worst " & data.LowItems.Count & " of " & lowCount & "  ·  bar = on hand, line = restock level"
        Else
            lowSub = lowCount & If(lowCount = 1, " item", " items") & "  ·  bar = on hand, line = restock level"
        End If
        Dim lowCard As Panel = MakeChartCard("Low & Out of Stock", lowSub, IconNotif, "!", lowChart,
                                             New Padding(2, 0, 8, 0), CARD_INSET,
                                             "View all", Sub() NavigateFromHome(lowChart, "lowStock"))

        ' Stock Movements
        Dim moveChart As New MovementChart With {.Dock = DockStyle.Fill}
        moveChart.SetDays(data.Days)
        AddHandler moveChart.ChartClicked, Sub() NavigateFromHome(moveChart, "history")
        Dim moveCard As Panel = MakeChartCard("Stock Movements",
                                              "Units in and out per day, last " & MOVE_DAYS & " days",
                                              IconActivity, "↻", moveChart,
                                              New Padding(8, 0, 2, 0), CARD_INSET,
                                              "History", Sub() NavigateFromHome(moveChart, "history"))

        chartRow.Controls.Add(lowCard, 0, 0)
        chartRow.Controls.Add(moveCard, 1, 0)
        wrap.Controls.Add(chartRow)

        ' ── LAYOUT ───────────────────────────────────────────────────────────
        ' wrap is 40px narrower than the scroll viewport and sits at Left=18.
        ' The chart row fills the height left on screen, down to CHART_MIN_H.
        Dim layoutAll As Action =
            Sub()
                Dim w As Integer = Math.Max(scroll.ClientSize.Width - 40, 100)
                wrap.Width = w
                topBar.Width = w
                clockChip.Left = topBar.Width - clockChip.Width - 4
                statRow.Width = w
                chartRow.Width = w
                Dim avail As Integer = scroll.ClientSize.Height - scroll.Padding.Top - scroll.Padding.Bottom -
                                       chartRow.Top - 6
                chartRow.Height = Math.Max(CHART_MIN_H, avail)
                wrap.Height = chartRow.Bottom + 6
            End Sub
        AddHandler scroll.Resize, Sub() layoutAll()
        layoutAll()

        container.ResumeLayout(True)
        ' A scrollbar that appears after the first pass narrows the viewport.
        layoutAll()
    End Sub

    ' A white card: icon + title + subtitle, an optional link at the right,
    ' then the chart filling the rest.
    Private Shared Function MakeChartCard(title As String, subtitle As String, iconPath As String,
                                          fallbackIcon As String, chart As Control, margin As Padding,
                                          inset As Integer, linkText As String, onLink As Action) As Panel
        Dim card As New BufferedPanel With {
            .Dock = DockStyle.Fill, .BackColor = CardBg, .Margin = margin,
            .Padding = New Padding(inset)
        }
        AddHandler card.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)

        Dim header As Panel = MakeCardHeader(title, iconPath, fallbackIcon, subtitle)
        If linkText <> "" Then
            Dim lnk As New Label With {
                .Text = linkText & "  ›", .Font = New Font("Segoe UI Semibold", 10),
                .ForeColor = MintPrimary, .AutoSize = True, .Cursor = Cursors.Hand,
                .BackColor = Color.Transparent
            }
            AddHandler lnk.Click, Sub() onLink()
            AddHandler lnk.MouseEnter, Sub() lnk.ForeColor = Theme.Deep
            AddHandler lnk.MouseLeave, Sub() lnk.ForeColor = MintPrimary
            header.Controls.Add(lnk)
            AddHandler header.Resize, Sub() lnk.Location = New Point(header.Width - lnk.Width - 18, 22)
        End If

        Dim body As New Panel With {
            .Dock = DockStyle.Fill, .BackColor = CardBg, .Padding = New Padding(18, 12, 18, 14)
        }
        body.Controls.Add(chart)
        card.Controls.Add(body)
        card.Controls.Add(header)
        Return card
    End Function

    ' =========================================================================
    '  HELPERS
    ' =========================================================================
    Private Shared Sub NavigateFromHome(host As Control, target As String)
        Try
            Dim frm = TryCast(host.FindForm(), INVENTORY_STAFF)
            If frm Is Nothing Then Return
            Select Case target
                Case "lowStock" : frm.lowStock.PerformClick()
                Case "history" : frm.OpenHistory()
            End Select
        Catch ex As Exception
            AppModal.Show("Could not open that section:" & Environment.NewLine & ErrorLog.ForUser(ex),
                          "Navigation Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    Private Shared Sub OpenStockAdjustment(host As Control, productId As Integer)
        Try
            Dim frm = TryCast(host.FindForm(), INVENTORY_STAFF)
            If frm IsNot Nothing Then frm.OpenStockAdjustmentFor(productId)
        Catch ex As Exception
            AppModal.Show("Could not open Stock Adjustment:" & Environment.NewLine & ErrorLog.ForUser(ex),
                          "Navigation Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    Private Shared Sub OpenItemListFiltered(host As Control, category As String, status As String)
        Try
            Dim frm = TryCast(host.FindForm(), INVENTORY_STAFF)
            If frm Is Nothing Then Return
            frm.OpenItemListFiltered(category, status)
        Catch ex As Exception
            AppModal.Show("Could not open Item List:" & Environment.NewLine & ErrorLog.ForUser(ex),
                          "Navigation Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    Private Shared Function SafeInt(o As Object) As Integer
        If o Is Nothing OrElse IsDBNull(o) Then Return 0
        Dim n As Integer
        If Integer.TryParse(o.ToString(), n) Then Return n
        Dim dbl As Double
        If Double.TryParse(o.ToString(), dbl) Then Return CInt(dbl)
        Return 0
    End Function

    Private Shared Function MakeCardHeader(title As String, iconPath As String, fallbackIcon As String,
                                           Optional subtitle As String = "") As Panel
        Dim header As New BufferedPanel With {
            .Height = If(subtitle = "", 60, 70), .Dock = DockStyle.Top, .BackColor = Color.Transparent
        }

        Dim iconBox As New Panel With {
            .Size = New Size(36, 36), .BackColor = Color.FromArgb(232, 246, 241),
            .Location = New Point(20, 14)
        }
        AddHandler iconBox.Paint,
            Sub(s As Object, e As PaintEventArgs)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Using br As New SolidBrush(CType(s, Panel).BackColor)
                    e.Graphics.FillEllipse(br, 0, 0, 35, 35)
                End Using
            End Sub
        Dim img As Image = GetIcon(iconPath)
        If img IsNot Nothing Then
            Dim pb As New PictureBox With {
                .Image = img, .SizeMode = PictureBoxSizeMode.Zoom,
                .Size = New Size(22, 22), .BackColor = Color.Transparent
            }
            iconBox.Controls.Add(pb)
            pb.Location = New Point((iconBox.Width - pb.Width) \ 2, (iconBox.Height - pb.Height) \ 2)
        Else
            iconBox.Controls.Add(New Label With {
                .Text = fallbackIcon, .Font = New Font("Segoe UI", 14, FontStyle.Bold), .ForeColor = MintPrimary,
                .AutoSize = False, .Size = iconBox.Size, .TextAlign = ContentAlignment.MiddleCenter,
                .BackColor = Color.Transparent, .Location = New Point(0, 0)
            })
        End If

        Dim titleLbl As New Label With {
            .Text = title, .Font = New Font("Segoe UI", 14, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .UseMnemonic = False,
            .Location = New Point(64, If(subtitle = "", 18, 10))
        }
        header.Controls.AddRange({iconBox, titleLbl})
        If subtitle <> "" Then
            header.Controls.Add(New Label With {
                .Text = subtitle, .Font = New Font("Segoe UI", 9.5F), .ForeColor = SubText,
                .AutoSize = True, .UseMnemonic = False, .Location = New Point(65, 40),
                .BackColor = Color.Transparent
            })
        End If

        AddHandler header.Paint,
            Sub(s As Object, e As PaintEventArgs)
                Using pen As New Pen(CardBorder, 1)
                    e.Graphics.DrawLine(pen, 0, header.Height - 1, CType(s, Panel).Width, header.Height - 1)
                End Using
            End Sub
        Return header
    End Function

    Private Shared Function GetGreeting() As String
        Dim h As Integer = DateTime.Now.Hour
        If h < 12 Then Return "Good Morning"
        If h < 17 Then Return "Good Afternoon"
        Return "Good Evening"
    End Function

    Public Shared Sub StopTimer()
        If clockTimer IsNot Nothing Then
            clockTimer.Stop()
            clockTimer.Dispose()
            clockTimer = Nothing
        End If
    End Sub

    Private Sub InventoryHomePanelBuilder_Load(sender As Object, e As EventArgs) Handles MyBase.Load
    End Sub
End Class
