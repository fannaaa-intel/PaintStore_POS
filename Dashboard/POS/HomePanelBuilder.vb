Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports MySql.Data.MySqlClient

Public Class HomePanelBuilder

    Private Shared ReadOnly MintDark As Color = Theme.Dark
    Private Shared ReadOnly MintPrimary As Color = Theme.Primary
    Private Shared ReadOnly MintBg As Color = Theme.PageBg
    Private Shared ReadOnly CardBg As Color = Color.White
    Private Shared ReadOnly CardBorder As Color = Theme.BorderSoft
    Private Shared ReadOnly SubText As Color = Theme.SubText
    Private Shared ReadOnly TileBg As Color = Theme.TileBg
    Private Shared ReadOnly MintGreen As Color = Color.FromArgb(0, 195, 115)
    Private Shared ReadOnly AccentYellow As Color = Color.FromArgb(220, 170, 0)
    Private Shared ReadOnly AccentOrange As Color = Theme.Warning
    Private Shared ReadOnly AccentBlue As Color = Color.FromArgb(80, 160, 255)
    Private Shared ReadOnly AccentRed As Color = Theme.Danger

    ' ── LIVE CONTROLS ──────────────────────────────────────────────────────────
    Private Shared clockTimer As Timer
    Private Shared notifTimer As Timer
    Private Shared lblClock As Label
    Private Shared lblShiftElapsed As Label
    Private Shared shiftStartTime As DateTime
    Private Shared progFill As Panel
    Private Shared progPctLbl As Label
    Private Shared progBarBg As Panel

    ' ── STAT LABELS (captured from loop, updated by LoadDashboardStats) ────────
    Private Shared _lblSalesValue As Label
    Private Shared _lblTxnsValue As Label
    Private Shared _lblItemsValue As Label

    Private Shared ReadOnly IconQuick As String = "quick.png"
    Private Shared ReadOnly IconNewSale As String = "sales.png"
    Private Shared ReadOnly IconInventory As String = "inventory.png"
    Private Shared ReadOnly IconSales As String = "product-sales.png"
    Private Shared ReadOnly IconReports As String = "coins.png"
    Private Shared ReadOnly IconShiftInfo As String = "transfer.png"
    Private Shared ReadOnly IconActivity As String = "recent.png"
    Private Shared ReadOnly IconNotif As String = "notification.png"
    Private Shared ReadOnly IconPOS As String = "setting.png"
    Private Shared ReadOnly IconClock As String = "clock.png"

    Private Shared ReadOnly IconCache As New Dictionary(Of String, Image)

    Private Shared Function GetIcon(path As String) As Image
        If String.IsNullOrEmpty(path) OrElse Not IO.File.Exists(path) Then Return Nothing
        If Not IconCache.ContainsKey(path) Then
            Try
                Dim bytes() As Byte = IO.File.ReadAllBytes(path)
                Using ms As New IO.MemoryStream(bytes)
                    IconCache(path) = Image.FromStream(ms)
                End Using
            Catch
                Return Nothing
            End Try
        End If
        Return IconCache(path)
    End Function

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Shared Sub LoadHome(container As Panel, shiftStart As DateTime)

        shiftStartTime = shiftStart

        If clockTimer IsNot Nothing Then
            clockTimer.Stop()
            clockTimer.Dispose()
            clockTimer = Nothing
        End If

        If notifTimer IsNot Nothing Then
            notifTimer.Stop()
            notifTimer.Dispose()
            notifTimer = Nothing
        End If

        container.SuspendLayout()
        container.Controls.Clear()
        container.BackColor = MintBg

        Dim scroll As New Panel With {
            .Dock = DockStyle.Fill,
            .AutoScroll = True,
            .BackColor = MintBg,
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

        ' ── PRE-LOAD DB DATA ─────────────────────────────────────────────────
        Dim lowStockCount As Integer = 0
        Dim todaySalesTotal As Decimal = 0D
        Dim recentTxns As New List(Of (no As String, timeStr As String, total As Decimal))

        Try
            Using conn = Db.GetConnection()
                conn.Open()
                ' Low stock items
                Using cmd As New MySqlCommand(
                    "SELECT COUNT(*) FROM products WHERE stock_qty <= reorder_level AND stock_qty >= 0", conn)
                    lowStockCount = CInt(cmd.ExecuteScalar())
                End Using
                ' Today's sales total
                Using cmd As New MySqlCommand(
                    "SELECT COALESCE(SUM(total),0) FROM sales WHERE DATE(sale_datetime)=CURDATE() AND status IN ('Completed','Partially Refunded') AND user_id=@uid", conn)
                    cmd.Parameters.AddWithValue("@uid", Session.CurrentUserId)
                    todaySalesTotal = CDec(cmd.ExecuteScalar())
                End Using
                ' Recent 2 transactions today (completed sales only, so the feed
                ' matches the Today's Sales figure and never shows held/voided rows)
                Using cmd As New MySqlCommand(
                    "SELECT sale_no, sale_datetime, total FROM sales " &
                    "WHERE DATE(sale_datetime)=CURDATE() AND status IN ('Completed','Partially Refunded') AND user_id=@uid " &
                    "ORDER BY sale_datetime DESC LIMIT 2", conn)
                    cmd.Parameters.AddWithValue("@uid", Session.CurrentUserId)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            recentTxns.Add((
                                rdr("sale_no").ToString(),
                                CDate(rdr("sale_datetime")).ToString("hh:mm tt"),
                                CDec(rdr("total"))))
                        End While
                    End Using
                End Using
            End Using
        Catch
            ' Show zeros on DB error
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

        Dim firstName As String = Session.CurrentFullName
        If firstName.Contains(" ") Then firstName = firstName.Split(" "c)(0)

        Dim lblGreet As New Label With {
            .Text = GetGreeting() & ", " & firstName & "!",
            .Font = New Font("Segoe UI", 28, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(0, 4)
        }

        Dim lblDate As New Label With {
            .Text = DateTime.Now.ToString("MMMM dd, yyyy — dddd"),
            .Font = New Font("Segoe UI", 12), .ForeColor = SubText,
            .AutoSize = True, .Location = New Point(2, 60)
        }

        Dim clockChip As New BufferedPanel With {
            .Size = New Size(210, 76), .Top = 10, .BackColor = MintBg,
            .Anchor = AnchorStyles.Right Or AnchorStyles.Top
        }

        AddHandler clockChip.Paint,
            Sub(s As Object, e As PaintEventArgs)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect = clockChip.ClientRectangle
                rect.Inflate(-1, -1)
                Dim r As Integer = 14
                Dim path As New GraphicsPath()
                path.AddArc(rect.X, rect.Y, r, r, 180, 90)
                path.AddArc(rect.Right - r, rect.Y, r, r, 270, 90)
                path.AddArc(rect.Right - r, rect.Bottom - r, r, r, 0, 90)
                path.AddArc(rect.X, rect.Bottom - r, r, r, 90, 90)
                path.CloseFigure()
                Using brush As New SolidBrush(Color.White) : g.FillPath(brush, path) : End Using
                Using pen As New Pen(Color.FromArgb(220, 232, 227), 1.0F) : g.DrawPath(pen, path) : End Using
                Using cb As New SolidBrush(Color.FromArgb(225, 245, 238)) : g.FillEllipse(cb, 16, 19, 38, 38) : End Using
            End Sub

        Dim lblClockTag As New Label With {
            .Text = "CURRENT TIME", .Font = New Font("Segoe UI", 8, FontStyle.Bold),
            .ForeColor = Color.FromArgb(126, 147, 141), .BackColor = Color.Transparent,
            .AutoSize = True, .Location = New Point(68, 18)
        }

        lblClock = New Label With {
            .Text = DateTime.Now.ToString("hh:mm tt"), .Font = New Font("Segoe UI", 19, FontStyle.Bold),
            .ForeColor = MintDark, .BackColor = Color.Transparent,
            .AutoSize = True, .Location = New Point(66, 34)
        }

        Dim clockIcon As Control
        Dim clockImg As Image = GetIcon(IconClock)
        If clockImg IsNot Nothing Then
            clockIcon = New PictureBox With {
                .Image = clockImg, .SizeMode = PictureBoxSizeMode.Zoom, .Size = New Size(20, 20),
                .BackColor = Color.Transparent, .Location = New Point(25, 28)
            }
        Else
            clockIcon = New Label With {
                .Text = "🕐", .Font = New Font("Segoe UI", 15), .ForeColor = MintDark,
                .BackColor = Color.Transparent, .AutoSize = False,
                .Size = New Size(38, 38), .TextAlign = ContentAlignment.MiddleCenter, .Location = New Point(16, 19)
            }
        End If

        clockChip.Controls.AddRange({lblClockTag, lblClock, clockIcon})
        Dim btnRefresh As New Button With {
            .Text = "↻  Refresh",
            .Size = New Size(100, 34),
            .FlatStyle = FlatStyle.Flat,
            .BackColor = Color.FromArgb(220, 235, 232),
            .ForeColor = SubText,
            .Font = New Font("Segoe UI", 9.5F),
            .Cursor = Cursors.Hand,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        btnRefresh.FlatAppearance.BorderSize = 0
        AddHandler btnRefresh.Paint,
            Sub(s, e)
                Dim bb = CType(s, Button)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect As Rectangle = bb.ClientRectangle
                rect.Inflate(-1, -1)
                Using path As New GraphicsPath()
                    Dim r = 8
                    path.AddArc(rect.X, rect.Y, r, r, 180, 90)
                    path.AddArc(rect.Right - r, rect.Y, r, r, 270, 90)
                    path.AddArc(rect.Right - r, rect.Bottom - r, r, r, 0, 90)
                    path.AddArc(rect.X, rect.Bottom - r, r, r, 90, 90)
                    path.CloseFigure()
                    Using br As New SolidBrush(bb.BackColor)
                        e.Graphics.FillPath(br, path)
                    End Using
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
                StopTimer()
                LoadHome(container, shiftStartTime)
            End Sub

        topBar.Controls.AddRange({lblGreet, lblDate, clockChip, btnRefresh})

        AddHandler wrap.Resize,
    Sub()
        topBar.Width = wrap.Width
        clockChip.Left = topBar.Width - clockChip.Width - 4
        btnRefresh.Top = clockChip.Top + (clockChip.Height - btnRefresh.Height) \ 2
        btnRefresh.Left = clockChip.Left - btnRefresh.Width - 12
    End Sub

        wrap.Controls.Add(topBar)
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
        AddHandler wrap.Resize, Sub() statRow.Width = wrap.Width

        Dim statData() As (title As String, value As String, subtitle As String, badge As String) = {
            ("Sales Today", "₱ 0.00", "Loading...", "Today"),
            ("Transactions", "—", "This shift", "Shift"),
            ("Items Sold", "—", "Units today", "Units"),
            ("Shift Hours", "0:00", "Since clock-in", "")
        }

        For i As Integer = 0 To 3
            Dim sd = statData(i)
            Dim capI = i

            Dim card As New BufferedPanel With {
                .Dock = DockStyle.Fill, .BackColor = CardBg,
                .Margin = New Padding(0, 0, If(i < 3, 12, 0), 0)
            }
            AddHandler card.Paint,
                Sub(s As Object, e As PaintEventArgs)
                    PanelRenderer.Draw(CType(s, Panel), e, True)
                End Sub

            Dim lTitle As New Label With {
                .Text = sd.title, .Font = New Font("Segoe UI", 11),
                .ForeColor = SubText, .AutoSize = True, .Location = New Point(22, 14)
            }
            Dim lValue As New Label With {
                .Text = sd.value, .Font = New Font("Segoe UI", 26, FontStyle.Bold),
                .ForeColor = {MintPrimary, MintDark, AccentOrange, AccentBlue}(capI),
                .AutoSize = True, .Location = New Point(20, 36)
            }
            Dim lSub As New Label With {
                .Text = sd.subtitle, .Font = New Font("Segoe UI", 10),
                .ForeColor = SubText, .AutoSize = True, .Location = New Point(22, 100)
            }
            card.Controls.AddRange({lTitle, lValue, lSub})

            ' Capture stat labels for later update
            Select Case capI
                Case 0 : _lblSalesValue = lValue
                Case 1 : _lblTxnsValue = lValue
                Case 2 : _lblItemsValue = lValue
                Case 3 : lblShiftElapsed = lValue
            End Select

            If sd.badge <> "" Then
                Dim badge As New Label With {
                    .Text = sd.badge, .Font = New Font("Segoe UI", 9, FontStyle.Bold),
                    .ForeColor = MintPrimary, .BackColor = TileBg,
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

            statRow.Controls.Add(card, i, 0)
        Next

        wrap.Controls.Add(statRow)
        curTop = statRow.Bottom + GAP

        ' ===========================
        ' 3. MIDDLE ROW — 3 CARDS
        ' ===========================
        Dim midRow As New TableLayoutPanel With {
            .Top = curTop, .Left = 0, .Width = wrap.Width, .Height = 320,
            .ColumnCount = 3, .RowCount = 1, .BackColor = Color.Transparent,
            .CellBorderStyle = TableLayoutPanelCellBorderStyle.None,
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        }
        midRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 34))
        midRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 33))
        midRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 33))
        AddHandler wrap.Resize, Sub() midRow.Width = wrap.Width

        ' ---- QUICK ACTIONS CARD ----
        Dim qaCard As New BufferedPanel With {
            .Dock = DockStyle.Fill, .BackColor = CardBg, .Margin = New Padding(0, 0, 12, 0)
        }
        AddHandler qaCard.Paint,
            Sub(s As Object, e As PaintEventArgs) PanelRenderer.Draw(CType(s, Panel), e, True)
        qaCard.Controls.Add(MakeCardHeader("Quick Actions", IconQuick, "≡"))

        Dim qaGrid As New TableLayoutPanel With {
            .ColumnCount = 2, .RowCount = 2, .Dock = DockStyle.Fill,
            .BackColor = Color.Transparent, .CellBorderStyle = TableLayoutPanelCellBorderStyle.None,
            .Padding = New Padding(14, 70, 14, 14)
        }
        qaGrid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
        qaGrid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
        qaGrid.RowStyles.Add(New RowStyle(SizeType.Percent, 50))
        qaGrid.RowStyles.Add(New RowStyle(SizeType.Percent, 50))

        Dim qaTiles() As (title As String, sub1 As String, iconPath As String, fallback As String, iconBg As Color) = {
    ("New Transaction", "Start transaction", "transaction.png", "+", Color.Transparent),
    ("Inventory", "View stock", "inventory.png", "▦", Color.Transparent),
    ("Sales History", "Units today", "recent.png", "◑", Color.Transparent),
    ("Cash Drawer", DateTime.Now.ToString("MMM dd, yyyy"), "coins.png", "≡", Color.Transparent)
}

        For Each qt In qaTiles
            Dim capQt = qt
            Dim tile As New BufferedPanel With {
                .BackColor = TileBg, .Dock = DockStyle.Fill,
                .Margin = New Padding(5), .Cursor = Cursors.Hand
            }
            AddHandler tile.Paint, Sub(s As Object, e As PaintEventArgs) PaintFlatTile(CType(s, Panel), e)

            Dim tileIconBox As New Panel With {
      .Size = New Size(42, 42), .BackColor = Color.Transparent, .Location = New Point(14, 14)
  }
            ' No Paint handler — no colored circle, just plain transparent background

            If capQt.iconPath <> "" AndAlso IO.File.Exists(capQt.iconPath) Then
                Dim pb As New PictureBox With {
    .Image = GetIcon(capQt.iconPath), .SizeMode = PictureBoxSizeMode.Zoom,
    .Size = New Size(24, 24), .BackColor = Color.Transparent
}
                pb.Location = New Point((tileIconBox.Width - pb.Width) \ 2, (tileIconBox.Height - pb.Height) \ 2)
                AddHandler tileIconBox.Resize,
                    Sub() pb.Location = New Point((tileIconBox.Width - pb.Width) \ 2, (tileIconBox.Height - pb.Height) \ 2)
                tileIconBox.Controls.Add(pb)
            Else
                tileIconBox.Controls.Add(New Label With {
                    .Text = capQt.fallback, .Font = New Font("Segoe UI", 14, FontStyle.Bold),
                    .ForeColor = Color.White, .AutoSize = False, .Size = New Size(38, 38),
                    .TextAlign = ContentAlignment.MiddleCenter, .BackColor = Color.Transparent, .Location = New Point(0, 0)
                })
            End If

            Dim tileName As New Label With {
                .Text = capQt.title, .Font = New Font("Segoe UI", 13, FontStyle.Bold),
                .ForeColor = MintDark, .AutoSize = True, .Location = New Point(14, 60)
            }
            Dim tileSub As New Label With {
                .Text = capQt.sub1, .Font = New Font("Segoe UI", 10),
                .ForeColor = SubText, .AutoSize = True, .Location = New Point(14, 82)
            }
            Dim chev As New Label With {
                .Text = "›", .Font = New Font("Segoe UI", 16), .ForeColor = SubText, .AutoSize = True
            }

            tile.Controls.AddRange({tileIconBox, tileName, tileSub, chev})
            AddHandler tile.Resize,
                Sub()
                    chev.Left = tile.Width - chev.Width - 10
                    chev.Top = tile.Height - chev.Height - 8
                End Sub
            AddHandler tile.MouseEnter, Sub() tile.BackColor = Color.FromArgb(220, 242, 235)
            AddHandler tile.MouseLeave, Sub() tile.BackColor = TileBg

            ' Wire the tile (and its children, since child clicks don't bubble) to navigate.
            Dim navTitle As String = capQt.title
            Dim onClick As EventHandler = Sub(s As Object, e As EventArgs) NavigateFromHome(tile, navTitle)
            AddHandler tile.Click, onClick
            For Each child As Control In tile.Controls
                AddHandler child.Click, onClick
                For Each grandChild As Control In child.Controls
                    AddHandler grandChild.Click, onClick
                Next
            Next

            qaGrid.Controls.Add(tile)
        Next

        qaCard.Controls.Add(qaGrid)
        midRow.Controls.Add(qaCard, 0, 0)

        ' ---- SHIFT INFO CARD ----
        Dim siCard As New BufferedPanel With {
            .Dock = DockStyle.Fill, .BackColor = CardBg, .Margin = New Padding(0, 0, 12, 0)
        }
        AddHandler siCard.Paint, Sub(s As Object, e As PaintEventArgs) PanelRenderer.Draw(CType(s, Panel), e, True)
        siCard.Controls.Add(MakeCardHeader("Shift Info", IconShiftInfo, "≡"))

        Dim siRows() As (key As String, val As String, isStatus As Boolean) = {
            ("Staff", Session.CurrentFullName, False),
            ("Shift Start", shiftStart.ToString("hh:mm tt"), False),
            ("Status", "● Active", True),
            ("Date", DateTime.Now.ToString("MMMM dd, yyyy"), False),
            ("Role", Session.CurrentRole, False)
        }

        Dim siBody As New Panel With {
            .Dock = DockStyle.Fill, .BackColor = Color.Transparent, .Padding = New Padding(0, 4, 0, 0)
        }

        Dim siY As Integer = 6
        For Each sr In siRows
            Dim capSR = sr
            Dim capY As Integer = siY

            Dim row As New Panel With {
                .Height = 46, .Left = 0, .Top = capY, .BackColor = Color.Transparent,
                .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
            }

            Dim kLbl As New Label With {
                .Text = capSR.key, .Font = New Font("Segoe UI", 11),
                .ForeColor = SubText, .AutoSize = True, .Location = New Point(22, 14)
            }
            Dim vLbl As New Label With {
                .Text = capSR.val, .Font = New Font("Segoe UI", 11, FontStyle.Bold),
                .ForeColor = If(capSR.isStatus, MintPrimary, MintDark),
                .AutoSize = True, .Anchor = AnchorStyles.Right Or AnchorStyles.Top
            }

            row.Controls.AddRange({kLbl, vLbl})
            AddHandler row.Resize,
                Sub()
                    vLbl.Left = row.Width - vLbl.Width - 22
                    vLbl.Top = 14
                End Sub
            AddHandler row.Paint,
                Sub(s As Object, e As PaintEventArgs)
                    Dim c = CType(s, Panel)
                    Using pen As New Pen(CardBorder, 1)
                        e.Graphics.DrawLine(pen, 18, c.Height - 1, c.Width - 18, c.Height - 1)
                    End Using
                End Sub

            siBody.Controls.Add(row)
            AddHandler siBody.Resize, Sub() row.Width = siBody.Width
            siY += 46
        Next

        Dim progPnl As New Panel With {
            .Height = 50, .Left = 0, .BackColor = Color.Transparent,
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Bottom
        }
        AddHandler siBody.Resize,
            Sub()
                progPnl.Width = siBody.Width
                progPnl.Top = siBody.Height - 54
            End Sub

        Dim progTitleLbl As New Label With {
            .Text = "Shift progress", .Font = New Font("Segoe UI", 10),
            .ForeColor = SubText, .AutoSize = True, .Location = New Point(22, 6)
        }
        progPctLbl = New Label With {
    .Text = "0%", .Font = New Font("Segoe UI", 10),
    .ForeColor = SubText, .AutoSize = True, .Anchor = AnchorStyles.Right Or AnchorStyles.Top
}
        progBarBg = New Panel With {
    .Height = 8, .Left = 22, .Top = 28, .BackColor = CardBorder,
    .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
}
        PaintRound(progBarBg, 4)

        progFill = New Panel With {.Height = 8, .Width = 0, .BackColor = MintPrimary}
        PaintRound(progFill, 4)
        progBarBg.Controls.Add(progFill)
        progPnl.Controls.AddRange({progTitleLbl, progPctLbl, progBarBg})
        AddHandler progPnl.Resize,
            Sub()
                progPctLbl.Left = progPnl.Width - progPctLbl.Width - 22
                progPctLbl.Top = 6
                progBarBg.Width = progPnl.Width - 44
            End Sub

        siBody.Controls.Add(progPnl)
        siCard.Controls.Add(siBody)
        midRow.Controls.Add(siCard, 1, 0)

        ' ---- RECENT ACTIVITY CARD ----
        Dim actCard As New BufferedPanel With {
            .Dock = DockStyle.Fill, .BackColor = CardBg, .Margin = New Padding(0, 0, 0, 0)
        }
        AddHandler actCard.Paint, Sub(s As Object, e As PaintEventArgs) PanelRenderer.Draw(CType(s, Panel), e, True)
        actCard.Controls.Add(MakeCardHeader("Recent Activity", IconActivity, "⚡"))

        ' Build activity list — 2 fixed entries + up to 2 from DB
        Dim actList As New List(Of (icon As String, iconBg As Color, text As String, time As String, dim1 As Boolean))
        actList.Add(("✔", Color.FromArgb(215, 248, 235), "Shift started successfully", shiftStart.ToString("hh:mm tt"), False))
        actList.Add(("→", Color.FromArgb(215, 248, 235), "Logged in as " & Session.CurrentFullName, shiftStart.ToString("hh:mm tt"), False))
        For Each t In recentTxns
            actList.Add(("₱", Color.FromArgb(215, 248, 235), "Sale #" & t.no & "  —  ₱" & t.total.ToString("N2"), t.timeStr, False))
        Next
        While actList.Count < 4
            actList.Add(("—", Color.FromArgb(235, 242, 240), If(actList.Count = 2, "No transactions yet today", "Waiting for more…"), "", True))
        End While

        Dim actBody As New Panel With {
            .Dock = DockStyle.Fill, .BackColor = Color.Transparent, .Padding = New Padding(16, 10, 16, 10)
        }

        Dim actY As Integer = 10
        For Each ai In actList
            Dim capAi = ai
            Dim capAy As Integer = actY

            Dim aRow As New Panel With {
                .Height = 58, .Left = 0, .Top = capAy, .BackColor = Color.Transparent,
                .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
            }

            Dim aCircle As New Panel With {
                .Size = New Size(36, 36), .BackColor = capAi.iconBg, .Location = New Point(14, 12)
            }
            PaintRound(aCircle, 18)

            Dim aIconLbl As New Label With {
                .Text = capAi.icon, .Font = New Font("Segoe UI", 12),
                .ForeColor = If(capAi.dim1, SubText, MintPrimary),
                .AutoSize = False, .Size = New Size(36, 36),
                .TextAlign = ContentAlignment.MiddleCenter, .BackColor = Color.Transparent, .Location = New Point(0, 0)
            }
            aCircle.Controls.Add(aIconLbl)

            Dim aText As New Label With {
                .Text = capAi.text,
                .Font = New Font("Segoe UI", 11, If(capAi.dim1, FontStyle.Regular, FontStyle.Bold)),
                .ForeColor = If(capAi.dim1, SubText, MintDark), .AutoSize = True, .Location = New Point(60, 12)
            }
            Dim aTime As New Label With {
                .Text = capAi.time, .Font = New Font("Segoe UI", 10),
                .ForeColor = SubText, .AutoSize = True, .Location = New Point(60, 34)
            }

            aRow.Controls.AddRange({aCircle, aText, aTime})

            If Not capAi.dim1 Then
                AddHandler aRow.Paint,
                    Sub(s As Object, e As PaintEventArgs)
                        Dim c = CType(s, Panel)
                        Using pen As New Pen(CardBorder, 1)
                            e.Graphics.DrawLine(pen, 0, c.Height - 1, c.Width, c.Height - 1)
                        End Using
                    End Sub
            End If

            actBody.Controls.Add(aRow)
            AddHandler actBody.Resize, Sub() aRow.Width = actBody.Width - 32
            actY += 60
        Next

        actCard.Controls.Add(actBody)
        midRow.Controls.Add(actCard, 2, 0)
        wrap.Controls.Add(midRow)
        curTop = midRow.Bottom + GAP

        ' ===========================
        ' 4. BOTTOM ROW — 2 CARDS
        ' ===========================
        Dim botRow As New TableLayoutPanel With {
            .Top = curTop, .Left = 0, .Width = wrap.Width, .Height = 380,
            .ColumnCount = 2, .RowCount = 1, .BackColor = Color.Transparent,
            .CellBorderStyle = TableLayoutPanelCellBorderStyle.None,
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        }
        botRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 60))
        botRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 40))
        AddHandler wrap.Resize, Sub() botRow.Width = wrap.Width

        ' ---- NOTIFICATIONS CARD ----
        Dim notifCard As New BufferedPanel With {
            .Dock = DockStyle.Fill, .BackColor = CardBg, .Margin = New Padding(0, 0, 12, 0)
        }
        AddHandler notifCard.Paint, Sub(s As Object, e As PaintEventArgs) PanelRenderer.Draw(CType(s, Panel), e, True)
        notifCard.Controls.Add(MakeCardHeader("Notifications", IconNotif, "🔔"))

        ' ── Smart notifications ───────────────────────────────────────────────────
        ' Build a dynamic list based on actual state rather than 3 hardcoded rows.
        Dim smartNotifs As New List(Of (icon As String, iconBg As Color, iconColor As Color,
                                bold As String, normal As String, navTarget As String, refreshKey As String))

        ' 1. Shift started — only show for first 10 minutes, then swap to shift duration
        Dim shiftAge As TimeSpan = DateTime.Now - shiftStart
        If shiftAge.TotalMinutes <= 10 Then
            smartNotifs.Add(("✔", Color.FromArgb(215, 248, 235), MintGreen,
        "Shift started", " successfully. Have a great day!", "", "shift"))
        Else
            Dim hrs = CInt(Math.Floor(shiftAge.TotalHours))
            Dim mins = shiftAge.Minutes
            smartNotifs.Add(("🕐", Color.FromArgb(215, 248, 235), MintPrimary,
        "Shift active:", $" {hrs}h {mins}m elapsed.", "", "shift"))
        End If

        ' 2. Stock alert — smarter messaging
        If lowStockCount = 0 Then
            smartNotifs.Add(("✔", Color.FromArgb(215, 248, 235), MintGreen,
        "Stock OK:", " All items are well-stocked.", "inventory", "stock"))
        ElseIf lowStockCount <= 3 Then
            smartNotifs.Add(("⚠", Color.FromArgb(255, 248, 215), AccentYellow,
        "Stock alert:", $" {lowStockCount} item(s) need reordering.", "inventory", "stock"))
        Else
            smartNotifs.Add(("⚠", Color.FromArgb(252, 230, 230), AccentRed,
        "Urgent:", $" {lowStockCount} items below reorder level!", "inventory", "stock"))
        End If

        ' 3. Sales — smarter messaging
        If todaySalesTotal = 0 Then
            smartNotifs.Add(("◎", Color.FromArgb(245, 245, 245), SubText,
        "No sales yet", " today. Ready to start selling!", "sales", "sales"))
        ElseIf todaySalesTotal < 1000 Then
            smartNotifs.Add(("◎", Color.FromArgb(215, 248, 235), MintGreen,
        "Today's sales:", $" ₱{todaySalesTotal:N2} so far.", "sales", "sales"))
        Else
            smartNotifs.Add(("◎", Color.FromArgb(215, 248, 235), MintGreen,
        "Great day!", $" ₱{todaySalesTotal:N2} in completed sales.", "sales", "sales"))
        End If

        ' 4. Bonus — held transactions warning
        Dim heldCount As Integer = 0
        Try
            Using conn2 = Db.GetConnection()
                conn2.Open()
                Using cmd As New MySqlCommand(
            "SELECT COUNT(*) FROM sales WHERE status='OnHold' AND DATE(sale_datetime)=CURDATE() AND user_id=@uid", conn2)
                    cmd.Parameters.AddWithValue("@uid", Session.CurrentUserId)
                    heldCount = CInt(cmd.ExecuteScalar())
                End Using
            End Using
        Catch
        End Try

        If heldCount > 0 Then
            smartNotifs.Add(("⏸", Color.FromArgb(255, 248, 215), AccentYellow,
        "Held orders:", $" {heldCount} transaction(s) on hold.", "transaction", "held"))
        End If

        Dim nBody As New Panel With {
    .Dock = DockStyle.Fill, .BackColor = Color.Transparent,
    .Padding = New Padding(14, 70, 14, 14)
}

        ' Auto-refresh every 60 seconds
        ' Auto-refresh every 60 seconds
        notifTimer = New Timer With {.Interval = 60000}
        Dim thisNotifTimer As Timer = notifTimer
        AddHandler thisNotifTimer.Tick,
    Sub()
        Try
            If notifCard IsNot Nothing AndAlso Not notifCard.IsDisposed Then
                ' Rebuild just the notification body
                Dim newLowStock As Integer = 0
                Dim newSalesTotal As Decimal = 0D
                Dim newHeld As Integer = 0
                Using conn2 = Db.GetConnection()
                    conn2.Open()
                    Using cmd As New MySqlCommand(
                        "SELECT COUNT(*) FROM products WHERE stock_qty <= reorder_level AND stock_qty >= 0", conn2)
                        newLowStock = CInt(cmd.ExecuteScalar())
                    End Using
                    Using cmd As New MySqlCommand(
                        "SELECT COALESCE(SUM(total),0) FROM sales WHERE DATE(sale_datetime)=CURDATE() AND status IN ('Completed','Partially Refunded') AND user_id=@uid", conn2)
                        cmd.Parameters.AddWithValue("@uid", Session.CurrentUserId)
                        newSalesTotal = CDec(cmd.ExecuteScalar())
                    End Using
                    Using cmd As New MySqlCommand(
                        "SELECT COUNT(*) FROM sales WHERE status='OnHold' AND DATE(sale_datetime)=CURDATE() AND user_id=@uid", conn2)
                        cmd.Parameters.AddWithValue("@uid", Session.CurrentUserId)
                        newHeld = CInt(cmd.ExecuteScalar())
                    End Using
                End Using

                Dim shiftAgeNow As TimeSpan = DateTime.Now - shiftStartTime

                For Each ctrl As Control In nBody.Controls
                    Dim bp = TryCast(ctrl, BufferedPanel)
                    If bp Is Nothing Then Continue For
                    Dim tag = If(bp.Tag?.ToString(), "")

                    If tag = "shift" Then
                        For Each lbl As Control In bp.Controls
                            If TypeOf lbl Is Label AndAlso lbl.Font.Bold Then
                                Dim boldL = CType(lbl, Label)
                                boldL.Text = If(shiftAgeNow.TotalMinutes <= 10, "Shift started", "Shift active:")
                            End If
                            If TypeOf lbl Is Label AndAlso lbl.Tag?.ToString() = "normal" Then
                                Dim normL = CType(lbl, Label)
                                If shiftAgeNow.TotalMinutes <= 10 Then
                                    normL.Text = " successfully. Have a great day!"
                                Else
                                    Dim hrs = CInt(Math.Floor(shiftAgeNow.TotalHours))
                                    Dim mins = shiftAgeNow.Minutes
                                    normL.Text = $" {hrs}h {mins}m elapsed."
                                End If
                            End If
                        Next
                    End If

                    If tag = "stock" Then
                        For Each lbl As Control In bp.Controls
                            If TypeOf lbl Is Label AndAlso lbl.Font.Bold Then
                                Dim boldL = CType(lbl, Label)
                                If newLowStock = 0 Then
                                    boldL.Text = "Stock OK:"
                                ElseIf newLowStock <= 3 Then
                                    boldL.Text = "Stock alert:"
                                Else
                                    boldL.Text = "Urgent:"
                                End If
                            End If
                            If TypeOf lbl Is Label AndAlso Not lbl.Font.Bold AndAlso
                               lbl.Tag?.ToString() = "normal" Then
                                Dim normL = CType(lbl, Label)
                                If newLowStock = 0 Then
                                    normL.Text = " All items are well-stocked."
                                ElseIf newLowStock <= 3 Then
                                    normL.Text = $" {newLowStock} item(s) need reordering."
                                Else
                                    normL.Text = $" {newLowStock} items below reorder level!"
                                End If
                            End If
                        Next
                    End If

                    If tag = "sales" Then
                        For Each lbl As Control In bp.Controls
                            If TypeOf lbl Is Label AndAlso lbl.Font.Bold Then
                                Dim boldL = CType(lbl, Label)
                                If newSalesTotal = 0 Then
                                    boldL.Text = "No sales yet"
                                ElseIf newSalesTotal < 1000 Then
                                    boldL.Text = "Today's sales:"
                                Else
                                    boldL.Text = "Great day!"
                                End If
                            End If
                            If TypeOf lbl Is Label AndAlso lbl.Tag?.ToString() = "normal" Then
                                Dim normL = CType(lbl, Label)
                                If newSalesTotal = 0 Then
                                    normL.Text = " today. Ready to start selling!"
                                Else
                                    normL.Text = $" ₱{newSalesTotal:N2} in completed sales."
                                End If
                            End If
                        Next
                    End If

                    If tag = "held" AndAlso newHeld > 0 Then
                        For Each lbl As Control In bp.Controls
                            If TypeOf lbl Is Label AndAlso lbl.Tag?.ToString() = "normal" Then
                                CType(lbl, Label).Text = $" {newHeld} transaction(s) on hold."
                            End If
                        Next
                    End If
                    ' Note: a held-orders row only exists if heldCount > 0 at the time the
                    ' Home tab was loaded. If it changes between 0 and >0 while the tab is
                    ' open, the row itself won't appear/disappear until the tab is reloaded —
                    ' this loop can only update text on rows that already exist.
                Next
            Else
                thisNotifTimer.Stop()
                thisNotifTimer.Dispose()
            End If
        Catch
        End Try
    End Sub
        thisNotifTimer.Start()

        ' Stop notif timer when container is disposed
        ' Stop notif timer when container is disposed
        Dim capturedNotifTimer As Timer = thisNotifTimer
        AddHandler container.Disposed,
            Sub()
                Try
                    capturedNotifTimer.Stop()
                    capturedNotifTimer.Dispose()
                Catch
                End Try
            End Sub

        Dim nY As Integer = 70
        For Each n In smartNotifs
            Dim capN = n
            Dim capNY As Integer = nY

            Dim nCard As New BufferedPanel With {
        .Height = 58, .Left = 0, .Top = capNY, .BackColor = TileBg,
        .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top,
        .Cursor = If(capN.navTarget <> "", Cursors.Hand, Cursors.Default),
        .Tag = capN.refreshKey  ' used by refresh logic
    }
            AddHandler nCard.Paint, Sub(s As Object, e As PaintEventArgs) PaintFlatTile(CType(s, Panel), e)

            Dim iconCircle As New Panel With {
        .Size = New Size(36, 36), .BackColor = capN.iconBg, .Location = New Point(14, 11)
    }
            PaintRound(iconCircle, 18)

            Dim iconLbl As New Label With {
        .Text = capN.icon, .Font = New Font("Segoe UI", 12), .ForeColor = capN.iconColor,
        .AutoSize = False, .Size = New Size(36, 36),
        .TextAlign = ContentAlignment.MiddleCenter, .BackColor = Color.Transparent, .Location = New Point(0, 0)
    }
            iconCircle.Controls.Add(iconLbl)

            Dim boldLbl As New Label With {
        .Text = capN.bold, .Font = New Font("Segoe UI", 11, FontStyle.Bold),
        .ForeColor = MintDark, .AutoSize = True, .Location = New Point(66, 18)
    }
            Dim normalLbl As New Label With {
        .Text = capN.normal, .Font = New Font("Segoe UI", 11),
        .ForeColor = SubText, .AutoSize = True, .Location = New Point(0, 0),
        .Tag = "normal"  ' used by refresh logic
    }
            Dim chev As New Label With {
        .Text = If(capN.navTarget <> "", "›", ""),
        .Font = New Font("Segoe UI", 16), .ForeColor = SubText, .AutoSize = True
    }

            nCard.Controls.AddRange({iconCircle, boldLbl, normalLbl, chev})
            AddHandler nCard.Resize,
        Sub()
            boldLbl.Left = 66 : boldLbl.Top = 18
            normalLbl.Left = boldLbl.Right + 6 : normalLbl.Top = 18
            chev.Left = nCard.Width - chev.Width - 12
            chev.Top = (nCard.Height - chev.Height) \ 2
        End Sub

            ' Hover only if it navigates somewhere
            If capN.navTarget <> "" Then
                AddHandler nCard.MouseEnter, Sub() nCard.BackColor = Color.FromArgb(212, 240, 232)
                AddHandler nCard.MouseLeave, Sub() nCard.BackColor = TileBg

                ' Navigate on click
                Dim navDest As String = capN.navTarget
                Dim onClick As EventHandler =
            Sub(s, e)
                Try
                    Dim frm = TryCast(nCard.FindForm(), POS_STAFF)
                    If frm Is Nothing Then Return
                    Select Case navDest
                        Case "inventory" : frm.transaction.PerformClick()
                        Case "sales" : frm.sales.PerformClick()
                        Case "transaction" : frm.transaction.PerformClick()
                    End Select
                Catch
                End Try
            End Sub
                AddHandler nCard.Click, onClick
                For Each child As Control In nCard.Controls
                    AddHandler child.Click, onClick
                Next
            End If

            nBody.Controls.Add(nCard)
            AddHandler nBody.Resize,
        Sub()
            nCard.Width = nBody.ClientSize.Width - 28
            nCard.Left = 14
        End Sub

            nY += 68
        Next

        notifCard.Controls.Add(nBody)
        notifCard.Controls.SetChildIndex(nBody, 1)
        botRow.Controls.Add(notifCard, 0, 0)

        ' ---- SESSION INFO CARD ----
        Dim posCard As New BufferedPanel With {
    .Dock = DockStyle.Fill, .BackColor = CardBg, .Margin = New Padding(0, 0, 0, 0)
}
        AddHandler posCard.Paint, Sub(s As Object, e As PaintEventArgs) PanelRenderer.Draw(CType(s, Panel), e, True)

        Dim hdr = MakeCardHeader("Session Info", IconPOS, "⚙")
        posCard.Controls.Add(hdr)
        posCard.Controls.SetChildIndex(hdr, 0)

        Dim posBody As New Panel With {
    .Dock = DockStyle.None, .Top = 70, .Left = 0,
    .Width = posCard.Width, .Height = posCard.Height - 70,
    .BackColor = Color.Transparent, .Padding = New Padding(20, 10, 20, 10)
}
        AddHandler posCard.Resize,
    Sub()
        posBody.Width = posCard.Width
        posBody.Height = posCard.Height - 70
    End Sub
        posCard.Controls.Add(posBody)

        ' ── DB ping on load ──────────────────────────────────────────────────
        Dim dbOnline As Boolean = False
        Dim dbLatencyMs As Long = -1
        Try
            Dim sw As New System.Diagnostics.Stopwatch()
            sw.Start()
            Using conn2 = Db.GetConnection()
                conn2.Open()
                Using cmd As New MySqlCommand("SELECT 1", conn2)
                    cmd.ExecuteScalar()
                End Using
            End Using
            sw.Stop()
            dbOnline = True
            dbLatencyMs = sw.ElapsedMilliseconds
        Catch
            dbOnline = False
        End Try

        ' ── Info rows ────────────────────────────────────────────────────────
        Dim sessionRows() As (key As String, val As String) = {
    ("Cashier", Session.CurrentFullName),
    ("Role", Session.CurrentRole),
    ("Shift Start", shiftStart.ToString("hh:mm tt")),
    ("Terminal", Environment.MachineName),
    ("Database", If(dbOnline, $"● Online  ({dbLatencyMs} ms)", "● Offline"))
}

        Dim rowY As Integer = 8
        For Each sr In sessionRows
            Dim capSR = sr
            Dim capY = rowY

            Dim row As New Panel With {
        .Height = 40, .Left = 0, .Top = capY,
        .BackColor = Color.Transparent,
        .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
    }

            Dim kLbl As New Label With {
        .Text = capSR.key,
        .Font = New Font("Segoe UI", 10),
        .ForeColor = SubText,
        .AutoSize = True,
        .Location = New Point(22, 12)
    }
            Dim vLbl As New Label With {
        .Text = capSR.val,
        .Font = New Font("Segoe UI", 10, FontStyle.Bold),
        .ForeColor = If(capSR.key = "Database",
                        If(dbOnline, MintGreen, AccentRed),
                        MintDark),
        .AutoSize = True,
        .Anchor = AnchorStyles.Right Or AnchorStyles.Top
    }

            row.Controls.AddRange({kLbl, vLbl})
            AddHandler row.Resize,
        Sub()
            vLbl.Left = row.Width - vLbl.Width - 22
            vLbl.Top = 12
        End Sub
            AddHandler row.Paint,
        Sub(s As Object, e As PaintEventArgs)
            Dim c = CType(s, Panel)
            Using pen As New Pen(CardBorder, 1)
                e.Graphics.DrawLine(pen, 18, c.Height - 1, c.Width - 18, c.Height - 1)
            End Using
        End Sub

            posBody.Controls.Add(row)
            AddHandler posBody.Resize, Sub() row.Width = posBody.Width
            rowY += 40
        Next

        ' ── Re-ping tile ─────────────────────────────────────────────────────
        Dim pingTile As New BufferedPanel With {
    .Height = 44, .Left = 15, .Top = rowY + 8,
    .BackColor = TileBg, .Cursor = Cursors.Hand,
    .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
}
        AddHandler pingTile.Paint, Sub(s As Object, e As PaintEventArgs) PaintFlatTile(CType(s, Panel), e)

        Dim pingDot As New Panel With {
    .Size = New Size(10, 10),
    .BackColor = If(dbOnline, MintGreen, AccentRed),
    .Location = New Point(16, 17)
}
        PaintRound(pingDot, 5)

        Dim pingLbl As New Label With {
    .Text = If(dbOnline, $"DB connected  ·  {dbLatencyMs} ms", "DB unreachable"),
    .Font = New Font("Segoe UI", 10, FontStyle.Bold),
    .ForeColor = If(dbOnline, MintDark, AccentRed),
    .AutoSize = True,
    .Location = New Point(36, 14)
}
        Dim pingChev As New Label With {
    .Text = "↻", .Font = New Font("Segoe UI", 13),
    .ForeColor = SubText, .AutoSize = True
}

        pingTile.Controls.AddRange({pingDot, pingLbl, pingChev})
        AddHandler pingTile.Resize,
    Sub()
        pingChev.Left = pingTile.Width - pingChev.Width - 14
        pingChev.Top = (pingTile.Height - pingChev.Height) \ 2
    End Sub

        AddHandler pingTile.Click,
    Sub()
        pingLbl.Text = "Pinging…"
        pingLbl.ForeColor = SubText
        Application.DoEvents()
        Try
            Dim sw2 As New System.Diagnostics.Stopwatch()
            sw2.Start()
            Using conn3 = Db.GetConnection()
                conn3.Open()
                Using cmd As New MySqlCommand("SELECT 1", conn3)
                    cmd.ExecuteScalar()
                End Using
            End Using
            sw2.Stop()
            pingDot.BackColor = MintGreen
            pingLbl.Text = $"DB connected  ·  {sw2.ElapsedMilliseconds} ms"
            pingLbl.ForeColor = MintDark
        Catch
            pingDot.BackColor = AccentRed
            pingLbl.Text = "DB unreachable"
            pingLbl.ForeColor = AccentRed
        End Try
    End Sub

        AddHandler pingTile.MouseEnter, Sub() pingTile.BackColor = Color.FromArgb(212, 240, 232)
        AddHandler pingTile.MouseLeave, Sub() pingTile.BackColor = TileBg

        posBody.Controls.Add(pingTile)
        AddHandler posBody.Resize, Sub() pingTile.Width = posBody.Width - 32

        botRow.Controls.Add(posCard, 1, 0)

        wrap.Controls.Add(botRow)
        curTop = botRow.Bottom + 28
        wrap.Height = curTop

        ' ===========================
        ' 5. LIVE TIMER
        ' ===========================
        clockTimer = New Timer With {.Interval = 1000}
        AddHandler clockTimer.Tick,
    Sub()
        Try
            If lblClock IsNot Nothing AndAlso Not lblClock.IsDisposed Then
                lblClock.Text = DateTime.Now.ToString("hh:mm tt")
            End If
            If lblShiftElapsed IsNot Nothing AndAlso Not lblShiftElapsed.IsDisposed Then
                Dim elapsed As TimeSpan = DateTime.Now - shiftStartTime
                lblShiftElapsed.Text = $"{CInt(Math.Floor(elapsed.TotalHours))}:{elapsed.Minutes:D2}"

                ' ── Shift progress bar (8-hour shift) ──────────────────
                Const SHIFT_HOURS As Double = 4.0
                Dim pct As Double = Math.Min(1.0, elapsed.TotalHours / SHIFT_HOURS)
                Dim pctInt As Integer = CInt(pct * 100)

                If progPctLbl IsNot Nothing AndAlso Not progPctLbl.IsDisposed Then
                    progPctLbl.Text = pctInt & "%"
                End If
                If progFill IsNot Nothing AndAlso Not progFill.IsDisposed AndAlso
                   progBarBg IsNot Nothing AndAlso Not progBarBg.IsDisposed Then
                    progFill.Width = CInt(progBarBg.Width * pct)
                    ' Color shifts: green → yellow → red
                    progFill.BackColor = If(pct < 0.75, MintPrimary,
                                         If(pct < 1.0, AccentYellow, AccentRed))
                End If
            End If
        Catch
        End Try
    End Sub
        clockTimer.Start()

        container.ResumeLayout()
        container.Refresh()

        ' Load real stat values from DB after UI is shown
        LoadDashboardStats()
    End Sub

    ' =========================================================================
    '  LOAD DASHBOARD STATS FROM DB
    ' =========================================================================
    Private Shared Sub LoadDashboardStats()
        Try
            Using conn = Db.GetConnection()
                conn.Open()

                ' Sales today and transaction count
                Using cmd As New MySqlCommand(
                    "SELECT COALESCE(SUM(total),0) AS rev, COUNT(*) AS txns " &
                    "FROM sales WHERE DATE(sale_datetime)=CURDATE() AND status IN ('Completed','Partially Refunded') AND user_id=@uid", conn)
                    cmd.Parameters.AddWithValue("@uid", Session.CurrentUserId)
                    Using rdr = cmd.ExecuteReader()
                        If rdr.Read() Then
                            Dim rev = CDec(rdr("rev"))
                            Dim txns = CInt(rdr("txns"))
                            SafeSetText(_lblSalesValue, "₱ " & rev.ToString("N2"))
                            SafeSetText(_lblTxnsValue, txns.ToString())
                        End If
                    End Using
                End Using

                ' Items sold today
                Using cmd As New MySqlCommand(
                    "SELECT COALESCE(SUM(si.qty),0) FROM sale_items si " &
                    "JOIN sales s ON si.sale_id=s.sale_id " &
                    "WHERE DATE(s.sale_datetime)=CURDATE() AND s.status IN ('Completed','Partially Refunded') AND s.user_id=@uid", conn)
                    cmd.Parameters.AddWithValue("@uid", Session.CurrentUserId)
                    SafeSetText(_lblItemsValue, CInt(cmd.ExecuteScalar()).ToString())
                End Using
            End Using
        Catch ex As Exception
            ' Keep graceful defaults on the cards, and report via the themed modal.
            AppModal.Show("Could not load dashboard figures:" & Environment.NewLine &
                          ex.Message, "Dashboard", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End Try
    End Sub

    ''' <summary>
    ''' Quick-action navigation. Reuses the existing nav buttons on POS_STAFF so the
    ''' active-highlight / load logic stays in one place. Errors surface on the modal.
    ''' </summary>
    Private Shared Sub NavigateFromHome(host As Control, section As String)
        Try
            Dim frm = TryCast(host.FindForm(), POS_STAFF)
            If frm Is Nothing Then Return
            Select Case section
                Case "New Transaction" : frm.transaction.PerformClick()
                Case "Inventory" : frm.transaction.PerformClick()
                Case "Sales History" : frm.sales.PerformClick()
                Case "Cash Drawer" : frm.Reports.PerformClick()
                Case Else
                    AppModal.Show(section & " isn't available from the cashier dashboard yet.",
                                  section, MessageBoxButtons.OK, MessageBoxIcon.Information)
            End Select
        Catch ex As Exception
            AppModal.Show("Could not open " & section & ":" & Environment.NewLine & ex.Message,
                          "Navigation Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    Private Shared Sub SafeSetText(lbl As Label, text As String)
        If lbl Is Nothing OrElse lbl.IsDisposed Then Return
        If lbl.InvokeRequired Then
            lbl.Invoke(Sub() lbl.Text = text)
        Else
            lbl.Text = text
        End If
    End Sub

    ' =========================================================================
    '  CARD HEADER FACTORY
    ' =========================================================================
    Private Shared Function MakeCardHeader(title As String, iconPath As String, fallbackIcon As String) As Panel
        Dim header As New BufferedPanel With {
            .Height = 60, .Dock = DockStyle.Top, .BackColor = Color.Transparent
        }

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

    ' =========================================================================
    '  PAINT HELPERS
    ' =========================================================================
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

            Using fill As New SolidBrush(ctrl.BackColor) : g.FillPath(fill, path) : End Using
            Using pen As New Pen(Color.FromArgb(228, 232, 230), 1.0F) : g.DrawPath(pen, path) : End Using
        End Using
    End Sub

    Private Shared Sub PaintRound(ctrl As Control, radius As Integer)
        AddHandler ctrl.Paint,
            Sub(s As Object, e As PaintEventArgs)
                Dim c = CType(s, Control)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Using path As New GraphicsPath()
                    path.AddArc(0, 0, radius, radius, 180, 90)
                    path.AddArc(c.Width - radius, 0, radius, radius, 270, 90)
                    path.AddArc(c.Width - radius, c.Height - radius, radius, radius, 0, 90)
                    path.AddArc(0, c.Height - radius, radius, radius, 90, 90)
                    path.CloseFigure()
                    e.Graphics.FillPath(New SolidBrush(c.BackColor), path)
                End Using
            End Sub
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
        If notifTimer IsNot Nothing Then
            notifTimer.Stop()
            notifTimer.Dispose()
            notifTimer = Nothing
        End If
    End Sub

End Class

Public Class BufferedPanel
    Inherits Panel
    Public Sub New()
        Me.DoubleBuffered = True
        Me.SetStyle(ControlStyles.OptimizedDoubleBuffer Or
                    ControlStyles.AllPaintingInWmPaint Or
                    ControlStyles.UserPaint, True)
    End Sub
End Class