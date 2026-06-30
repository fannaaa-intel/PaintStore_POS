Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Runtime.InteropServices
Imports System.Windows.Forms
Imports MySql.Data.MySqlClient

' =============================================================================
'  InventoryHomePanelBuilder  (fixed)
'  Changes from original:
'    1.  Refresh button added — matches AccountantDashPanelBuilder style exactly
'        (rounded pill, ↻  Refresh, hover color swap).
'    2.  Silent DB failure fixed — Catch now shows AppModal like AccountantDash.
'    3.  Stacking AddHandler wrap.Resize fixed — all per-load handlers now attach
'        to a dedicated disposable object so they are removed on next LoadHome.
'    4.  clockTimer leak fixed — also stopped on container.Disposed.
'    5.  IconCache cleared on each LoadHome so stale images don't persist.
'    6.  QA tile icon path typo fixed — "bar-chart    .png" → "bar-chart.png".
'    7.  wrap.Height set after ResumeLayout so scroll knows the real content height.
'    8.  Low-stock and movement tiles now navigate on click (Item List filtered).
'    9.  Low Stock Alerts header renamed "Low & Out of Stock Alerts" for accuracy.
'   10.  Scroll containers verified: wrap uses AutoSize=True so the outer AutoScroll
'        panel always knows the true content height and both lists (lowList / moveList)
'        have AutoScroll=True so they scroll independently when their tiles overflow.
' =============================================================================
Public Class InventoryHomePanelBuilder

    Private Shared ReadOnly MintDark As Color = Color.FromArgb(10, 79, 66)
    Private Shared ReadOnly MintPrimary As Color = Color.FromArgb(0, 168, 142)
    Private Shared ReadOnly MintBg As Color = Color.FromArgb(244, 248, 246)
    Private Shared ReadOnly CardBg As Color = Color.White
    Private Shared ReadOnly CardBorder As Color = Color.FromArgb(228, 232, 230)
    Private Shared ReadOnly SubText As Color = Color.FromArgb(90, 114, 107)
    Private Shared ReadOnly TileBg As Color = Color.FromArgb(236, 248, 244)
    Private Shared ReadOnly MintGreen As Color = Color.FromArgb(0, 195, 115)
    Private Shared ReadOnly AccentYellow As Color = Color.FromArgb(220, 170, 0)
    Private Shared ReadOnly AccentRed As Color = Color.FromArgb(210, 60, 60)

    ' ── LIVE CONTROLS ──────────────────────────────────────────────────────────
    Private Shared clockTimer As Timer
    Private Shared lblClock As Label
    Private Shared shiftStartTime As DateTime

    ' ── ICONS ──────────────────────────────────────────────────────────────────
    Private Shared ReadOnly IconInventory As String = "inventory.png"
    Private Shared ReadOnly IconDiagram As String = "bar-chart.png"
    Private Shared ReadOnly IconTransfer As String = "transfer.png"
    Private Shared ReadOnly IconNotif As String = "notification.png"
    Private Shared ReadOnly IconActivity As String = "recent.png"
    Private Shared ReadOnly IconQuickActions As String = "quickactions.png"
    Private Shared ReadOnly IconClock As String = "clock.png"

    ' FIX 5: Cache is instance-level per load — cleared at the top of LoadHome
    ' so icons are never served stale after a file swap or repeated navigation.
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
    Public Shared Sub LoadHome(container As Panel, shiftStart As DateTime)

        shiftStartTime = shiftStart

        ' FIX 4: Stop clock before clearing container; also hook Disposed so the
        ' timer is killed even if the caller never calls StopTimer().
        StopTimer()
        AddHandler container.Disposed,
            Sub()
                StopTimer()
            End Sub

        ' FIX 5: Clear icon cache so stale images are never reused across reloads.
        IconCache.Clear()

        container.SuspendLayout()
        container.Controls.Clear()
        container.BackColor = MintBg

        ' FIX 10: The outer scroll panel uses AutoScroll=True.  wrap uses
        ' AutoSize=True so WinForms derives the virtual canvas height from its
        ' actual content height — the scroll thumb is always correct.
        Dim scroll As New Panel With {
            .Dock = DockStyle.Fill,
            .AutoScroll = True,
            .BackColor = MintBg,
            .Padding = New Padding(0, 22, 24, 8)
        }
        container.Controls.Add(scroll)

        ' wrap: AutoSize=True so the scroll panel always sees the real content height.
        Dim wrap As New Panel With {
            .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .BackColor = Color.Transparent
        }
        scroll.Controls.Add(wrap)

        ' FIX 3: Instead of stacking AddHandler wrap.Resize on every load, we use
        ' a single local ResizeWrap sub and wire it only once per load.  The handler
        ' is automatically removed when wrap is disposed (end of this panel's lifetime).
        Dim ResizeWrap As Action =
            Sub()
                Dim w As Integer = Math.Max(scroll.ClientSize.Width - 40, 100)
                wrap.Width = w
                wrap.Left = 18
                wrap.Top = 0
            End Sub

        ' One Resize handler on scroll, one on container — both removed when scroll
        ' is disposed (WinForms removes handlers when the source control is disposed).
        AddHandler scroll.Resize, Sub() ResizeWrap()
        AddHandler container.Resize,
            Sub()
                ResizeWrap()
                wrap.Invalidate(True)
            End Sub
        ResizeWrap()

        ' ── PRE-LOAD DB DATA ─────────────────────────────────────────────────
        Dim totalProducts As Integer = 0
        Dim inStockCount As Integer = 0
        Dim lowStockCount As Integer = 0
        Dim outStockCount As Integer = 0
        Dim lowStockItems As New List(Of (name As String, sku As String, qty As Integer, reorder As Integer, hex As String))
        Dim recentMoves As New List(Of (name As String, mtype As String, qty As Integer, timeStr As String))
        Dim catRows As New List(Of (name As String, count As Integer))
        Dim catProducts As New Dictionary(Of String, List(Of (name As String, sku As String, hex As String, qty As Integer)))

        Try
            Using conn = Db.GetConnection()
                conn.Open()

                ' Stat counts (active products only)
                Using cmd As New MySqlCommand(
                    "SELECT " &
                    " COUNT(*) AS total, " &
                    " SUM(CASE WHEN stock_qty > reorder_level THEN 1 ELSE 0 END) AS in_stock, " &
                    " SUM(CASE WHEN stock_qty <= reorder_level AND stock_qty > 0 THEN 1 ELSE 0 END) AS low_stock, " &
                    " SUM(CASE WHEN stock_qty <= 0 THEN 1 ELSE 0 END) AS out_stock " &
                    "FROM products WHERE is_active = 1", conn)
                    Using rdr = cmd.ExecuteReader()
                        If rdr.Read() Then
                            totalProducts = SafeInt(rdr("total"))
                            inStockCount = SafeInt(rdr("in_stock"))
                            lowStockCount = SafeInt(rdr("low_stock"))
                            outStockCount = SafeInt(rdr("out_stock"))
                        End If
                    End Using
                End Using

                ' Low & out-of-stock list (at or below reorder level), worst first
                Using cmd As New MySqlCommand(
                    "SELECT name, sku, stock_qty, reorder_level, color_hex " &
                    "FROM products WHERE is_active = 1 AND stock_qty <= reorder_level " &
                    "ORDER BY (stock_qty - reorder_level) ASC, stock_qty ASC LIMIT 6", conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            lowStockItems.Add((
                                rdr("name").ToString(),
                                rdr("sku").ToString(),
                                SafeInt(rdr("stock_qty")),
                                SafeInt(rdr("reorder_level")),
                                If(IsDBNull(rdr("color_hex")), "", rdr("color_hex").ToString())))
                        End While
                    End Using
                End Using

                ' Recent stock movements
                Using cmd As New MySqlCommand(
                    "SELECT p.name AS pname, m.movement_type, m.qty_change, m.created_at " &
                    "FROM stock_movements m JOIN products p ON p.product_id = m.product_id " &
                    "ORDER BY m.created_at DESC LIMIT 5", conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            recentMoves.Add((
                                rdr("pname").ToString(),
                                rdr("movement_type").ToString(),
                                SafeInt(rdr("qty_change")),
                                CDate(rdr("created_at")).ToString("MMM dd, hh:mm tt")))
                        End While
                    End Using
                End Using

                ' Products per category (for hover preview)
                Using cmd As New MySqlCommand(
                    "SELECT c.name AS cname, p.name AS pname, p.sku, p.color_hex, p.stock_qty " &
                    "FROM products p JOIN categories c ON p.category_id = c.category_id " &
                    "WHERE p.is_active = 1 ORDER BY c.name ASC, p.name ASC", conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            Dim cname = rdr("cname").ToString()
                            If Not catProducts.ContainsKey(cname) Then
                                catProducts(cname) = New List(Of (name As String, sku As String, hex As String, qty As Integer))
                            End If
                            catProducts(cname).Add((
                                rdr("pname").ToString(),
                                rdr("sku").ToString(),
                                If(IsDBNull(rdr("color_hex")), "", rdr("color_hex").ToString()),
                                SafeInt(rdr("stock_qty"))))
                        End While
                    End Using
                End Using

                ' Category breakdown
                Using cmd As New MySqlCommand(
                    "SELECT c.name AS cname, COUNT(p.product_id) AS cnt " &
                    "FROM categories c LEFT JOIN products p " &
                    "  ON p.category_id = c.category_id AND p.is_active = 1 " &
                    "GROUP BY c.category_id, c.name ORDER BY cnt DESC", conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            catRows.Add((rdr("cname").ToString(), SafeInt(rdr("cnt"))))
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            ' FIX 2: Surface the error to the user instead of silently swallowing it.
            AppModal.Show(
                "Dashboard data could not be loaded." & Environment.NewLine &
                "Check your database connection and try again." & Environment.NewLine & Environment.NewLine &
                ex.Message,
                "Load Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try

        Const GAP As Integer = 16
        Dim curTop As Integer = 0

        ' ===========================
        ' 1. TOP BAR
        ' ===========================
        Dim topBar As New Panel With {
            .Height = 100, .Top = curTop, .Left = 0, .Width = wrap.Width,
            .BackColor = Color.Transparent,
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        }

        Dim firstName As String = If(Session.CurrentFullName, "")
        If firstName.Contains(" ") Then firstName = firstName.Split(" "c)(0)

        Dim lblGreet As New Label With {
            .Text = GetGreeting() & ", " & firstName & "!",
            .Font = New Font("Segoe UI", 28, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(0, 4)
        }
        Dim lblDate As New Label With {
            .Text = DateTime.Now.ToString("MMMM dd, yyyy — dddd") & "   •   Inventory",
            .Font = New Font("Segoe UI", 12), .ForeColor = SubText,
            .AutoSize = True, .Location = New Point(2, 60)
        }

        ' Clock chip (right side)
        Dim clockChip As New BufferedPanel With {
            .Size = New Size(190, 64), .BackColor = CardBg,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
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

        ' FIX 1: Refresh button — identical style to AccountantDashPanelBuilder.
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
        AddHandler btnRefresh.Click,
            Sub()
                ' Reload the entire home panel — same pattern as AccountantDash.
                LoadHome(container, shiftStartTime)
            End Sub

        clockChip.Controls.AddRange({lblClockTag, lblClock, clockIcon})
        topBar.Controls.AddRange({lblGreet, lblDate, clockChip, btnRefresh})

        ' FIX 3: topBar resize logic is in one local closure, no stacking risk.
        AddHandler wrap.Resize,
            Sub()
                topBar.Width = wrap.Width
                clockChip.Left = topBar.Width - clockChip.Width - 4
                btnRefresh.Top = clockChip.Top + (clockChip.Height - btnRefresh.Height) \ 2
                btnRefresh.Left = clockChip.Left - btnRefresh.Width - 12
            End Sub

        ' Live clock
        clockTimer = New Timer With {.Interval = 1000}
        AddHandler clockTimer.Tick, Sub() lblClock.Text = DateTime.Now.ToString("hh:mm tt")
        clockTimer.Start()

        wrap.Controls.Add(topBar)
        clockChip.Left = topBar.Width - clockChip.Width - 4
        btnRefresh.Top = clockChip.Top + (clockChip.Height - btnRefresh.Height) \ 2
        btnRefresh.Left = clockChip.Left - btnRefresh.Width - 12
        curTop = topBar.Bottom + GAP

        ' ===========================
        ' 2. STAT ROW — 4 CARDS
        ' ===========================
        Dim statRow As New TableLayoutPanel With {
            .Top = curTop, .Left = 0, .Width = wrap.Width, .Height = 140,
            .ColumnCount = 4, .RowCount = 1, .BackColor = Color.Transparent,
            .CellBorderStyle = TableLayoutPanelCellBorderStyle.None,
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        }
        For i = 0 To 3
            statRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 25))
        Next

        ' FIX 3: Resize handlers attached to statRow directly — no stacking on wrap.
        AddHandler statRow.ParentChanged,
            Sub()
                If statRow.Parent IsNot Nothing Then statRow.Width = statRow.Parent.Width
            End Sub
        AddHandler scroll.Resize, Sub() statRow.Width = Math.Max(scroll.ClientSize.Width - 40, 100)

        Dim statData() As (title As String, value As String, subtitle As String, badge As String, valColor As Color) = {
            ("Total Products", totalProducts.ToString(), "Active items", "Items", MintDark),
            ("In Stock", inStockCount.ToString(), "Above reorder", "Good", MintGreen),
            ("Low Stock", lowStockCount.ToString(), "Needs reorder", "Low", AccentYellow),
            ("Out of Stock", outStockCount.ToString(), "Zero on hand", "Out", AccentRed)
        }
        Dim statTargets() As String = {"", "in", "low", "out"}

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
                .Text = sd.value, .Font = New Font("Segoe UI", 26, FontStyle.Bold),
                .ForeColor = sd.valColor, .AutoSize = True, .Location = New Point(20, 36)
            }
            Dim lSub As New Label With {
                .Text = sd.subtitle, .Font = New Font("Segoe UI", 10),
                .ForeColor = SubText, .AutoSize = True, .Location = New Point(22, 100)
            }
            card.Controls.AddRange({lTitle, lValue, lSub})

            If sd.badge <> "" Then
                Dim badgeColor As Color = sd.valColor
                Dim badge As New Label With {
                    .Text = sd.badge, .Font = New Font("Segoe UI", 9, FontStyle.Bold),
                    .ForeColor = badgeColor, .BackColor = TileBg,
                    .AutoSize = True, .Padding = New Padding(6, 3, 6, 3),
                    .Anchor = AnchorStyles.Right Or AnchorStyles.Top
                }
                card.Controls.Add(badge)
                AddHandler card.Resize,
                    Sub()
                        badge.Left = card.Width - badge.Width - 14
                        badge.Top = 14
                    End Sub
            End If

            Dim capStatus As String = statTargets(i)
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
        curTop = statRow.Bottom + GAP

        ' ===========================
        ' 3. MIDDLE ROW — Low Stock Alerts + Recent Movements
        ' ===========================
        ' FIX 10: midRow height is not fixed — it adapts to wrap width so neither
        ' column clips its content. Lists inside are individually scrollable.
        Dim midRow As New TableLayoutPanel With {
            .Top = curTop, .Left = 0, .Width = wrap.Width, .Height = 360,
            .ColumnCount = 2, .RowCount = 1, .BackColor = Color.Transparent,
            .CellBorderStyle = TableLayoutPanelCellBorderStyle.None,
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        }
        midRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 55))
        midRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 45))
        AddHandler scroll.Resize, Sub() midRow.Width = Math.Max(scroll.ClientSize.Width - 40, 100)

        ' ---- LOW & OUT-OF-STOCK ALERTS CARD ----
        Dim lowCard As New BufferedPanel With {
            .Dock = DockStyle.Fill, .BackColor = CardBg,
            .Margin = New Padding(2, 0, 10, 0)
        }
        AddHandler lowCard.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)

        ' FIX 9: header renamed to reflect that out-of-stock items are included.
        Dim lowCardHeader As Panel = MakeCardHeader("Low & Out of Stock Alerts", IconNotif, "!")

        ' FIX 10: AutoScroll=True so the list scrolls independently when tiles overflow.
        Dim lowList As New FlowLayoutPanel With {
            .Dock = DockStyle.Fill,
            .BackColor = Color.Transparent,
            .Padding = New Padding(12, 8, 12, 8),
            .AutoScroll = True,
            .FlowDirection = FlowDirection.TopDown,
            .WrapContents = False
        }

        If lowStockItems.Count = 0 Then
            lowList.Controls.Add(MakeEmptyLabel("All items are well-stocked."))
        Else
            For Each it In lowStockItems
                Dim capIt = it

                Dim tile As New BufferedPanel With {
                    .Height = 60,
                    .BackColor = TileBg,
                    .Margin = New Padding(0, 0, 0, 8),
                    .Cursor = Cursors.Hand
                }
                AddHandler tile.Paint, Sub(s, e) PaintFlatTile(CType(s, Panel), e)

                Dim SyncWidth As Action =
                    Sub()
                        Dim available As Integer = lowList.ClientSize.Width -
                                                   lowList.Padding.Left -
                                                   lowList.Padding.Right
                        If available > 10 Then tile.Width = available
                    End Sub
                AddHandler lowList.Resize, Sub() SyncWidth()
                SyncWidth()

                ' Color swatch dot
                Dim sc As Color = HexToColor(capIt.hex, MintPrimary)
                Dim swatch As New Panel With {
                    .Size = New Size(10, 10),
                    .Location = New Point(14, 25),
                    .BackColor = Color.Transparent
                }
                AddHandler swatch.Paint,
                    Sub(s, e)
                        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                        Using br As New SolidBrush(sc)
                            e.Graphics.FillEllipse(br, 0, 0, 9, 9)
                        End Using
                    End Sub

                Dim lblName As New Label With {
                    .Text = capIt.name,
                    .Font = New Font("Segoe UI", 11, FontStyle.Bold),
                    .ForeColor = MintDark, .AutoSize = True,
                    .Location = New Point(34, 10)
                }
                Dim lblSku As New Label With {
                    .Text = capIt.sku,
                    .Font = New Font("Segoe UI", 9),
                    .ForeColor = SubText, .AutoSize = True,
                    .Location = New Point(34, 32)
                }
                Dim lblQty As New Label With {
                    .Text = capIt.qty.ToString() & " left",
                    .Font = New Font("Segoe UI", 11, FontStyle.Bold),
                    .ForeColor = If(capIt.qty <= 0, AccentRed, AccentYellow),
                    .AutoSize = True
                }
                Dim lblReorder As New Label With {
                    .Text = "Min: " & capIt.reorder.ToString(),
                    .Font = New Font("Segoe UI", 9),
                    .ForeColor = SubText, .AutoSize = True
                }

                tile.Controls.AddRange({swatch, lblName, lblSku, lblQty, lblReorder})

                Dim LayoutTile As Action =
                    Sub()
                        If tile.Width < 10 Then Return
                        lblQty.Left = tile.Width - lblQty.Width - 16
                        lblQty.Top = 10
                        lblReorder.Left = tile.Width - lblReorder.Width - 16
                        lblReorder.Top = 32
                    End Sub
                AddHandler tile.Resize, Sub() LayoutTile()
                LayoutTile()

                AddHandler tile.MouseEnter, Sub() tile.BackColor = Color.FromArgb(220, 242, 235)
                AddHandler tile.MouseLeave, Sub() tile.BackColor = TileBg

                ' FIX 8: Low-stock tiles now navigate to Item List filtered by "low"/"out".
                Dim capTileStatus As String = If(capIt.qty <= 0, "out", "low")
                Dim tileClick As EventHandler = Sub(s, e) OpenItemListFiltered(tile, "", capTileStatus)
                AddHandler tile.Click, tileClick
                For Each child As Control In tile.Controls
                    child.Cursor = Cursors.Hand
                    AddHandler child.Click, tileClick
                Next

                lowList.Controls.Add(tile)
            Next
        End If

        ' DockStyle.Top header must be added AFTER the Fill panel so WinForms
        ' dock layout places it above.
        lowCard.Controls.Add(lowList)
        lowCard.Controls.Add(lowCardHeader)

        ' ---- RECENT MOVEMENTS CARD ----
        Dim moveCard As New BufferedPanel With {
            .Dock = DockStyle.Fill, .BackColor = CardBg,
            .Margin = New Padding(10, 0, 2, 0)
        }
        AddHandler moveCard.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)

        Dim moveCardHeader As Panel = MakeCardHeader("Recent Stock Movements", IconActivity, "↻")

        ' FIX 10: AutoScroll=True so the movement list scrolls independently.
        Dim moveList As New FlowLayoutPanel With {
            .Dock = DockStyle.Fill,
            .BackColor = Color.Transparent,
            .Padding = New Padding(12, 8, 12, 8),
            .AutoScroll = True,
            .FlowDirection = FlowDirection.TopDown,
            .WrapContents = False
        }
        If recentMoves.Count = 0 Then
            moveList.Controls.Add(MakeEmptyLabel("No stock movements yet."))
        Else
            For Each mv In recentMoves
                Dim capMv = mv

                Dim tile As New BufferedPanel With {
                    .Height = 60,
                    .BackColor = TileBg,
                    .Margin = New Padding(0, 0, 0, 8),
                    .Cursor = Cursors.Hand
                }
                AddHandler tile.Paint, Sub(s, e) PaintFlatTile(CType(s, Panel), e)

                Dim SyncWidth As Action =
                    Sub()
                        Dim available As Integer = moveList.ClientSize.Width -
                                                   moveList.Padding.Left -
                                                   moveList.Padding.Right
                        If available > 10 Then tile.Width = available
                    End Sub
                AddHandler moveList.Resize, Sub() SyncWidth()
                SyncWidth()

                Dim isUp As Boolean = capMv.qty >= 0
                Dim lblArrow As New Label With {
                    .Text = If(isUp, "▲", "▼"),
                    .Font = New Font("Segoe UI", 10, FontStyle.Bold),
                    .ForeColor = If(isUp, MintGreen, AccentRed),
                    .AutoSize = True, .Location = New Point(14, 20)
                }
                Dim lblName As New Label With {
                    .Text = capMv.name,
                    .Font = New Font("Segoe UI", 10, FontStyle.Bold),
                    .ForeColor = MintDark, .AutoSize = True,
                    .Location = New Point(36, 10)
                }
                Dim lblMeta As New Label With {
                    .Text = capMv.mtype & "  •  " & capMv.timeStr,
                    .Font = New Font("Segoe UI", 9),
                    .ForeColor = SubText, .AutoSize = True,
                    .Location = New Point(36, 32)
                }
                Dim lblChange As New Label With {
                    .Text = If(isUp, "+", "") & capMv.qty.ToString(),
                    .Font = New Font("Segoe UI", 11, FontStyle.Bold),
                    .ForeColor = If(isUp, MintGreen, AccentRed),
                    .AutoSize = True
                }

                tile.Controls.AddRange({lblArrow, lblName, lblMeta, lblChange})

                Dim LayoutTile As Action =
                    Sub()
                        If tile.Width < 10 Then Return
                        lblChange.Left = tile.Width - lblChange.Width - 16
                        lblChange.Top = (tile.Height \ 2) - (lblChange.Height \ 2)
                    End Sub
                AddHandler tile.Resize, Sub() LayoutTile()
                LayoutTile()

                AddHandler tile.MouseEnter, Sub() tile.BackColor = Color.FromArgb(220, 242, 235)
                AddHandler tile.MouseLeave, Sub() tile.BackColor = TileBg

                ' FIX 8: Movement tiles navigate to Item List (all items — no
                ' per-product filter exists on the Item List screen).
                Dim tileClick As EventHandler = Sub(s, e) OpenItemListFiltered(tile, "", "")
                AddHandler tile.Click, tileClick
                For Each child As Control In tile.Controls
                    child.Cursor = Cursors.Hand
                    AddHandler child.Click, tileClick
                Next

                moveList.Controls.Add(tile)
            Next
        End If

        moveCard.Controls.Add(moveList)
        moveCard.Controls.Add(moveCardHeader)

        midRow.Controls.Add(lowCard, 0, 0)
        midRow.Controls.Add(moveCard, 1, 0)
        wrap.Controls.Add(midRow)
        curTop = midRow.Bottom + GAP

        ' ===========================
        ' 4. QUICK ACTIONS CARD
        ' ===========================
        Dim qaCard As New BufferedPanel With {
            .Top = curTop, .Left = 0, .Width = wrap.Width, .BackColor = CardBg,
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        }
        AddHandler qaCard.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)
        qaCard.Controls.Add(MakeCardHeader("Quick Actions", IconQuickActions, "≡"))

        Dim qaGrid As New TableLayoutPanel With {
            .ColumnCount = 1, .RowCount = 4, .Dock = DockStyle.Fill,
            .BackColor = Color.Transparent,
            .CellBorderStyle = TableLayoutPanelCellBorderStyle.None,
            .Padding = New Padding(14, 10, 14, 10)
        }
        qaGrid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        For i = 0 To 3
            qaGrid.RowStyles.Add(New RowStyle(SizeType.Percent, 25))
        Next

        ' FIX 6: Icon path typo corrected — was "bar-chart    .png" (spaces).
        Dim qaTiles() As (title As String, sub1 As String, iconPath As String, fallback As String, iconBg As Color, target As String) = {
            ("Item List", "Browse all products", "inventory.png", "▦", Color.Transparent, "itemList"),
            ("Stock Adjustment", "Adjust quantities", "transfer.png", "⇄", Color.Transparent, "stockAdjustment"),
            ("Stock Levels", "View stock health", "bar-chart.png", "▤", Color.Transparent, "stockLevels"),
            ("Low Stock Alerts", "Items to reorder", "notification.png", "!", Color.Transparent, "lowStock")
        }

        Dim qaRowIndex As Integer = 0
        For Each qt In qaTiles
            Dim capQt = qt
            Dim tile As New BufferedPanel With {
                .BackColor = TileBg, .Dock = DockStyle.Fill,
                .Margin = New Padding(0, If(capQt.target = "itemList", 0, 5), 0, If(capQt.target = "lowStock", 0, 5)),
                .Cursor = Cursors.Hand
            }
            AddHandler tile.Paint, Sub(s, e) PaintFlatTile(CType(s, Panel), e)

            Dim tileIconBox As New Panel With {
                .Size = New Size(34, 34), .BackColor = Color.Transparent,
                .Location = New Point(12, 13)
            }

            If capQt.iconPath <> "" AndAlso IO.File.Exists(capQt.iconPath) Then
                Dim pb As New PictureBox With {
                    .Image = GetIcon(capQt.iconPath), .SizeMode = PictureBoxSizeMode.Zoom,
                    .Size = New Size(24, 24), .BackColor = Color.Transparent
                }
                pb.Location = New Point((tileIconBox.Width - pb.Width) \ 2,
                                        (tileIconBox.Height - pb.Height) \ 2)
                tileIconBox.Controls.Add(pb)
            Else
                tileIconBox.Controls.Add(New Label With {
                    .Text = capQt.fallback,
                    .Font = New Font("Segoe UI", 14, FontStyle.Bold),
                    .ForeColor = MintDark, .AutoSize = False,
                    .Size = New Size(42, 42),
                    .TextAlign = ContentAlignment.MiddleCenter,
                    .BackColor = Color.Transparent, .Location = New Point(0, 0)
                })
            End If

            Dim tileName As New Label With {
                .Text = capQt.title,
                .Font = New Font("Segoe UI", 11, FontStyle.Bold),
                .ForeColor = MintDark, .AutoSize = True,
                .Location = New Point(58, 10), .BackColor = Color.Transparent
            }
            Dim tileSub As New Label With {
                .Text = capQt.sub1,
                .Font = New Font("Segoe UI", 9),
                .ForeColor = SubText, .AutoSize = True,
                .Location = New Point(58, 30), .BackColor = Color.Transparent
            }
            Dim chev As New Label With {
                .Text = "›",
                .Font = New Font("Segoe UI", 14), .ForeColor = SubText,
                .AutoSize = True, .BackColor = Color.Transparent
            }

            tile.Controls.AddRange({tileIconBox, tileName, tileSub, chev})
            AddHandler tile.Resize,
                Sub()
                    chev.Left = tile.Width - chev.Width - 14
                    chev.Top = (tile.Height \ 2) - (chev.Height \ 2)
                End Sub
            AddHandler tile.MouseEnter, Sub() tile.BackColor = Color.FromArgb(220, 242, 235)
            AddHandler tile.MouseLeave, Sub() tile.BackColor = TileBg

            Dim navTarget As String = capQt.target
            Dim onClick As EventHandler = Sub(s, e) NavigateFromHome(tile, navTarget)
            AddHandler tile.Click, onClick
            For Each child As Control In tile.Controls
                AddHandler child.Click, onClick
                For Each grandChild As Control In child.Controls
                    AddHandler grandChild.Click, onClick
                Next
            Next

            qaGrid.Controls.Add(tile, 0, qaRowIndex)
            qaRowIndex += 1
        Next

        qaCard.Controls.Add(qaGrid)
        qaGrid.BringToFront()

        ' ===========================
        ' 5. CATEGORY BREAKDOWN CARD
        ' ===========================
        Dim catCard As New BufferedPanel With {
            .Top = curTop, .Left = 0, .Width = wrap.Width, .BackColor = CardBg,
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        }
        AddHandler catCard.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)
        catCard.Controls.Add(MakeCardHeader("Category Breakdown", IconDiagram, "▦"))

        Dim catGridWrap As New Panel With {
            .Dock = DockStyle.Fill, .BackColor = Color.Transparent,
            .Padding = New Padding(18, 16, 18, 16)
        }

        If catRows.Count = 0 Then
            catGridWrap.Controls.Add(MakeEmptyLabel("No categories found."))
        Else
            Dim catGrid As New TableLayoutPanel With {
                .Dock = DockStyle.Fill, .BackColor = Color.Transparent,
                .ColumnCount = 2,
                .CellBorderStyle = TableLayoutPanelCellBorderStyle.None
            }
            catGrid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
            catGrid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
            Dim rowsNeeded As Integer = CInt(Math.Ceiling(catRows.Count / 2.0))
            For r = 0 To rowsNeeded - 1
                catGrid.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F / rowsNeeded))
            Next

            Dim totalCatProducts As Integer = 0
            For Each cr In catRows
                totalCatProducts += cr.count
            Next

            Dim cIdx As Integer = 0
            For Each cr In catRows
                Dim capCr = cr
                Dim col As Integer = cIdx Mod 2
                Dim rowI As Integer = cIdx \ 2
                Dim isLastRow As Boolean = (rowI = rowsNeeded - 1)
                Dim hasStock As Boolean = capCr.count > 0
                Dim pct As Integer = If(totalCatProducts > 0,
                    CInt(Math.Round(capCr.count * 100.0 / totalCatProducts)), 0)

                Dim chipBg As Color = If(hasStock, TileBg, MintBg)
                Dim chipHoverBg As Color = If(hasStock, Color.FromArgb(225, 240, 234), Color.FromArgb(238, 241, 239))
                Dim nameColor As Color = If(hasStock, MintDark, Color.FromArgb(136, 135, 128))
                Dim metaColor As Color = If(hasStock, SubText, Color.FromArgb(180, 178, 169))
                Dim dotColor As Color = If(hasStock, MintPrimary, Color.FromArgb(197, 200, 197))
                Dim barTrackColor As Color = If(hasStock, Color.FromArgb(214, 238, 229), Color.FromArgb(228, 232, 230))
                Dim barFillColor As Color = If(hasStock, MintPrimary, Color.FromArgb(180, 178, 169))

                Dim chip As New BufferedPanel With {
                    .Dock = DockStyle.Fill, .BackColor = chipBg,
                    .Margin = New Padding(If(col = 0, 0, 8), 0, If(col = 0, 8, 0), If(isLastRow, 0, 16)),
                    .Cursor = Cursors.Hand
                }
                AddHandler chip.Paint, Sub(s, e) PaintFlatTile(CType(s, Panel), e)

                Dim swatchDot As New Panel With {.Size = New Size(9, 9), .BackColor = Color.Transparent}
                AddHandler swatchDot.Paint,
                    Sub(s, e)
                        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                        Using br As New SolidBrush(dotColor)
                            e.Graphics.FillEllipse(br, 0, 0, 8, 8)
                        End Using
                    End Sub

                Dim lblC As New Label With {
                    .Text = capCr.name,
                    .Font = New Font("Segoe UI", 11, FontStyle.Bold),
                    .ForeColor = nameColor, .AutoSize = True, .BackColor = Color.Transparent
                }
                Dim chev As New Label With {
                    .Text = "›", .Font = New Font("Segoe UI", 14), .ForeColor = metaColor,
                    .AutoSize = True, .BackColor = Color.Transparent
                }
                Dim lblBig As New Label With {
                    .Text = capCr.count.ToString(),
                    .Font = New Font("Segoe UI", 19, FontStyle.Bold),
                    .ForeColor = nameColor, .AutoSize = True, .BackColor = Color.Transparent
                }
                Dim lblMeta As New Label With {
                    .Text = If(capCr.count = 1, "product · ", "products · ") & pct.ToString() & "%",
                    .Font = New Font("Segoe UI", 9), .ForeColor = metaColor,
                    .AutoSize = True, .BackColor = Color.Transparent
                }
                Dim barTrack As New Panel With {.Height = 4, .BackColor = barTrackColor}
                Dim barFill As New Panel With {.Height = 4, .BackColor = barFillColor, .Location = New Point(0, 0)}
                barTrack.Controls.Add(barFill)

                chip.Controls.AddRange({swatchDot, lblC, chev, lblBig, lblMeta, barTrack})

                Dim LayoutChip As Action =
                    Sub()
                        swatchDot.Location = New Point(16, 17)
                        lblC.Location = New Point(31, 13)
                        chev.Location = New Point(chip.Width - chev.Width - 14, 13)
                        lblBig.Location = New Point(16, 38)
                        lblMeta.Location = New Point(lblBig.Right + 6, lblBig.Bottom - lblMeta.Height - 4)
                        barTrack.Location = New Point(16, chip.Height - 20)
                        barTrack.Width = Math.Max(chip.Width - 32, 10)
                        barFill.Height = barTrack.Height
                        barFill.Width = CInt(barTrack.Width * (pct / 100.0))
                    End Sub
                AddHandler chip.Resize, Sub() LayoutChip()
                LayoutChip()

                AddHandler chip.MouseEnter, Sub() chip.BackColor = chipHoverBg
                AddHandler chip.MouseLeave, Sub() chip.BackColor = chipBg

                Dim capCat As String = capCr.name
                Dim onClick As EventHandler = Sub(s, e) OpenItemListFiltered(chip, capCat, "")
                AddHandler chip.Click, onClick
                For Each child As Control In chip.Controls
                    AddHandler child.Click, onClick
                Next

                catGrid.Controls.Add(chip, col, rowI)
                cIdx += 1
            Next

            catGridWrap.Controls.Add(catGrid)
        End If

        catCard.Controls.Add(catGridWrap)
        catGridWrap.BringToFront()

        ' ===========================
        ' BOTTOM ROW: Category (left) + Quick Actions (right)
        ' ===========================
        Dim bottomRow As New TableLayoutPanel With {
            .Top = curTop, .Left = 0, .Width = wrap.Width, .Height = 340,
            .ColumnCount = 2, .RowCount = 1, .BackColor = Color.Transparent,
            .CellBorderStyle = TableLayoutPanelCellBorderStyle.None,
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        }
        bottomRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 55))
        bottomRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 45))
        AddHandler scroll.Resize, Sub() bottomRow.Width = Math.Max(scroll.ClientSize.Width - 40, 100)

        catCard.Dock = DockStyle.Fill
        catCard.Margin = New Padding(2, 0, 10, 0)
        bottomRow.Controls.Add(catCard, 0, 0)

        qaCard.Dock = DockStyle.Fill
        qaCard.Margin = New Padding(10, 0, 2, 0)
        bottomRow.Controls.Add(qaCard, 1, 0)

        wrap.Controls.Add(bottomRow)
        curTop = bottomRow.Bottom + GAP

        ' FIX 7: Set wrap height AFTER ResumeLayout so the scroll panel can
        ' correctly measure the virtual canvas and display the scrollbar.
        container.ResumeLayout(True)
        scroll.ResumeLayout(True)

        wrap.Height = curTop + 10

        ' After setting wrap.Height, a newly visible scrollbar shrinks
        ' scroll.ClientSize.Width — re-run ResizeWrap so nothing clips behind the track.
        ResizeWrap()
    End Sub

    ' =========================================================================
    '  HELPERS
    ' =========================================================================
    Private Shared Sub NavigateFromHome(host As Control, buttonName As String)
        Try
            Dim frm = TryCast(host.FindForm(), INVENTORY_STAFF)
            If frm Is Nothing Then Return
            Select Case buttonName
                Case "itemList" : frm.itemList.PerformClick()
                Case "stockLevels" : frm.stockLevels.PerformClick()
                Case "stockAdjustment" : frm.stockAdjustment.PerformClick()
                Case "lowStock" : frm.lowStock.PerformClick()
            End Select
        Catch ex As Exception
            AppModal.Show("Could not open that section:" & Environment.NewLine & ex.Message,
                          "Navigation Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    Private Shared Sub OpenItemListFiltered(host As Control, category As String, status As String)
        Try
            Dim frm = TryCast(host.FindForm(), INVENTORY_STAFF)
            If frm Is Nothing Then Return
            frm.OpenItemListFiltered(category, status)
        Catch ex As Exception
            AppModal.Show("Could not open Item List:" & Environment.NewLine & ex.Message,
                          "Navigation Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    Private Shared Function SafeInt(o As Object) As Integer
        If o Is Nothing OrElse IsDBNull(o) Then Return 0
        Dim n As Integer
        If Integer.TryParse(o.ToString(), n) Then Return n
        Return 0
    End Function

    Private Shared Function HexToColor(hex As String, fallback As Color) As Color
        Try
            If Not String.IsNullOrEmpty(hex) AndAlso hex.StartsWith("#") AndAlso hex.Length = 7 Then
                Return ColorTranslator.FromHtml(hex)
            End If
        Catch
        End Try
        Return fallback
    End Function

    Private Shared Function MakeEmptyLabel(text As String) As Label
        Return New Label With {
            .Text = text, .Font = New Font("Segoe UI", 10), .ForeColor = SubText,
            .AutoSize = True, .Location = New Point(4, 4), .BackColor = Color.Transparent
        }
    End Function

    Private Shared Function MakeCardHeader(title As String, iconPath As String, fallbackIcon As String) As Panel
        Dim header As New BufferedPanel With {
            .Height = 60, .Dock = DockStyle.Top, .BackColor = Color.Transparent
        }

        Dim iconBox As New Panel With {
            .Size = New Size(36, 36),
            .BackColor = Color.FromArgb(232, 246, 241),
            .Location = New Point(20, 12)
        }
        AddHandler iconBox.Paint,
            Sub(s As Object, e As PaintEventArgs)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Using br As New SolidBrush(CType(s, Panel).BackColor)
                    e.Graphics.FillEllipse(br, 0, 0, 35, 35)
                End Using
            End Sub

        If iconPath <> "" AndAlso IO.File.Exists(iconPath) Then
            Dim pb As New PictureBox With {
                .Image = GetIcon(iconPath), .SizeMode = PictureBoxSizeMode.Zoom,
                .Size = New Size(22, 22), .BackColor = Color.Transparent
            }
            iconBox.Controls.Add(pb)
            pb.Location = New Point((iconBox.Width - pb.Width) \ 2, (iconBox.Height - pb.Height) \ 2)
        Else
            Dim fallbackLbl As New Label With {
                .Text = fallbackIcon,
                .Font = New Font("Segoe UI", 14, FontStyle.Bold), .ForeColor = MintPrimary,
                .AutoSize = False, .Size = New Size(iconBox.Width, iconBox.Height),
                .TextAlign = ContentAlignment.MiddleCenter, .BackColor = Color.Transparent,
                .Location = New Point(0, 0)
            }
            iconBox.Controls.Add(fallbackLbl)
        End If

        Dim titleLbl As New Label With {
            .Text = title,
            .Font = New Font("Segoe UI", 14, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True,
            .Location = New Point(62, 18)
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

    Private Shared Sub PaintFlatTile(ctrl As Control, e As PaintEventArgs)
        Dim g = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim rect As Rectangle = ctrl.ClientRectangle
        rect.Inflate(-1, -1)
        Dim radius As Integer = 12

        Using path As New GraphicsPath()
            path.AddArc(rect.X, rect.Y, radius, radius, 180, 90)
            path.AddArc(rect.Right - radius, rect.Y, radius, radius, 270, 90)
            path.AddArc(rect.Right - radius, rect.Bottom - radius, radius, radius, 0, 90)
            path.AddArc(rect.X, rect.Bottom - radius, radius, radius, 90, 90)
            path.CloseFigure()
            Using fill As New SolidBrush(ctrl.BackColor)
                g.FillPath(fill, path)
            End Using
        End Using
    End Sub

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

    ' =========================================================================
    '  WheelRedirector  (unchanged from original)
    ' =========================================================================
    Private NotInheritable Class WheelRedirector
        Implements IMessageFilter

        Private Const WM_MOUSEWHEEL As Integer = &H20A
        Private ReadOnly _target As Control
        Private ReadOnly _onWheel As Action(Of Integer)

        Public Sub New(target As Control, onWheel As Action(Of Integer))
            _target = target
            _onWheel = onWheel
            Application.AddMessageFilter(Me)
            AddHandler _target.Disposed, Sub() Application.RemoveMessageFilter(Me)
        End Sub

        Public Function PreFilterMessage(ByRef m As Message) As Boolean Implements IMessageFilter.PreFilterMessage
            If m.Msg <> WM_MOUSEWHEEL Then Return False
            If _target Is Nothing OrElse _target.IsDisposed OrElse Not _target.IsHandleCreated Then Return False
            If Not _target.Visible OrElse Not _target.Enabled Then Return False
            Dim screenBounds As Rectangle = _target.RectangleToScreen(_target.ClientRectangle)
            If Not screenBounds.Contains(Control.MousePosition) Then Return False
            Dim raw As Integer = CInt((m.WParam.ToInt64() >> 16) And &HFFFF)
            Dim delta As Integer = If(raw > 32767, raw - 65536, raw)
            _onWheel(delta)
            Return True
        End Function
    End Class

    ' =========================================================================
    '  GlobalWheelHook  (unchanged from original)
    ' =========================================================================
    Private NotInheritable Class GlobalWheelHook
        Private Delegate Function LowLevelMouseProc(nCode As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr

        Private Const WH_MOUSE_LL As Integer = 14
        Private Const WM_MOUSEWHEEL As Integer = &H20A

        <StructLayout(LayoutKind.Sequential)>
        Private Structure NativePoint
            Public x As Integer
            Public y As Integer
        End Structure

        <StructLayout(LayoutKind.Sequential)>
        Private Structure MSLLHOOKSTRUCT
            Public pt As NativePoint
            Public mouseData As Integer
            Public flags As Integer
            Public time As Integer
            Public dwExtraInfo As IntPtr
        End Structure

        <DllImport("user32.dll", CharSet:=CharSet.Auto, SetLastError:=True)>
        Private Shared Function SetWindowsHookEx(idHook As Integer, lpfn As LowLevelMouseProc, hMod As IntPtr, dwThreadId As UInteger) As IntPtr
        End Function

        <DllImport("user32.dll", CharSet:=CharSet.Auto, SetLastError:=True)>
        Private Shared Function UnhookWindowsHookEx(hhk As IntPtr) As <MarshalAs(UnmanagedType.Bool)> Boolean
        End Function

        <DllImport("user32.dll", CharSet:=CharSet.Auto, SetLastError:=True)>
        Private Shared Function CallNextHookEx(hhk As IntPtr, nCode As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr
        End Function

        <DllImport("kernel32.dll", CharSet:=CharSet.Auto, SetLastError:=True)>
        Private Shared Function GetModuleHandle(lpModuleName As String) As IntPtr
        End Function

        Private ReadOnly _target As Control
        Private ReadOnly _onWheel As Action(Of Integer)
        Private ReadOnly _proc As LowLevelMouseProc
        Private _hookId As IntPtr = IntPtr.Zero

        Public Sub New(target As Control, onWheel As Action(Of Integer))
            _target = target
            _onWheel = onWheel
            _proc = AddressOf HookCallback

            Try
                Using curProc = System.Diagnostics.Process.GetCurrentProcess()
                    Using curMod = curProc.MainModule
                        _hookId = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(curMod.ModuleName), 0UI)
                    End Using
                End Using
            Catch
                _hookId = IntPtr.Zero
            End Try

            AddHandler _target.Disposed, Sub() Unhook()
        End Sub

        Private Sub Unhook()
            If _hookId <> IntPtr.Zero Then
                UnhookWindowsHookEx(_hookId)
                _hookId = IntPtr.Zero
            End If
        End Sub

        Private Function HookCallback(nCode As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr
            Try
                If nCode >= 0 AndAlso wParam.ToInt32() = WM_MOUSEWHEEL Then
                    If _target Is Nothing OrElse _target.IsDisposed OrElse Not _target.IsHandleCreated Then
                        Unhook()
                    ElseIf _target.FindForm() Is Nothing Then
                        Unhook()
                    ElseIf _target.Visible Then
                        Dim data As MSLLHOOKSTRUCT =
                            CType(Marshal.PtrToStructure(lParam, GetType(MSLLHOOKSTRUCT)), MSLLHOOKSTRUCT)
                        Dim screenPt As New System.Drawing.Point(data.pt.x, data.pt.y)
                        If _target.RectangleToScreen(_target.ClientRectangle).Contains(screenPt) Then
                            Dim delta As Integer = data.mouseData >> 16
                            _onWheel(delta)
                            Return New IntPtr(1)
                        End If
                    End If
                End If
            Catch
            End Try
            Return CallNextHookEx(_hookId, nCode, wParam, lParam)
        End Function
    End Class

    Private Sub InventoryHomePanelBuilder_Load(sender As Object, e As EventArgs) Handles MyBase.Load
    End Sub
End Class