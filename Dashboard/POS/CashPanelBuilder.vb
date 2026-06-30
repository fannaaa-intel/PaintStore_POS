Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports System.Linq
Imports MySql.Data.MySqlClient

Public Class CashPanelBuilder

    Private Shared ReadOnly MintDark As Color = Color.FromArgb(10, 79, 66)
    Private Shared ReadOnly MintPrimary As Color = Color.FromArgb(0, 168, 142)
    Private Shared ReadOnly MintBg As Color = Color.FromArgb(244, 248, 246)
    Private Shared ReadOnly CardBg As Color = Color.White
    Private Shared ReadOnly CardBorder As Color = Color.FromArgb(220, 232, 228)
    Private Shared ReadOnly SubText As Color = Color.FromArgb(90, 114, 107)
    Private Shared ReadOnly TileBg As Color = Color.FromArgb(236, 248, 244)
    Private Shared ReadOnly SoftBorder As Color = Color.FromArgb(210, 225, 220)
    Private Shared ReadOnly TextDark As Color = Color.FromArgb(45, 45, 45)
    Private Shared ReadOnly HeaderBg As Color = Color.FromArgb(0, 100, 84)
    Private Shared ReadOnly DangerBack As Color = Color.FromArgb(252, 235, 235)
    Private Shared ReadOnly DangerFore As Color = Color.FromArgb(163, 45, 45)
    Private Shared ReadOnly OkGreen As Color = Color.FromArgb(120, 230, 180)
    Private Shared ReadOnly OverGold As Color = Color.FromArgb(255, 196, 120)
    Private Shared ReadOnly ShortRed As Color = Color.FromArgb(255, 150, 140)

    ' ── SESSION FIGURES — loaded from DB ──────────────────────────────────────
    Private Shared _shiftId As Integer = 0
    Private Shared _openingFloat As Decimal = 0D
    Private Shared _cashSales As Decimal = 0D
    Private Shared _cashPaidOut As Decimal = 0D
    Private Shared _cashRefunds As Decimal = 0D
    Private Shared _cashPaidIn As Decimal = 0D
    Private Shared _cashDropped As Decimal = 0D
    Private Shared _shiftStarted As DateTime = DateTime.MinValue
    Private Shared _mainContainer As Panel

    Private Shared ReadOnly IconBasePath As String = Application.StartupPath & "\"
    Private Shared ReadOnly IconCache As New Dictionary(Of String, Image)

    Private Shared Function GetIcon(fileName As String) As Image
        Dim path As String = IconBasePath & fileName
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

    Public Class Denom
        Public Property Value As Decimal
        Public Property Count As Integer
        Public Property IsBill As Boolean
    End Class

    Private Shared _denoms As List(Of Denom)
    Private Shared _lblCountFooter As Label
    Private Shared _lblExpected As Label
    Private Shared _lblCounted As Label
    Private Shared _lblVariance As Label

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Shared Sub LoadCash(mainContainer As Panel)
        _mainContainer = mainContainer
        mainContainer.Controls.Clear()
        mainContainer.BackColor = MintBg

        Dim root As New BufferedPanel With {.Dock = DockStyle.Fill, .BackColor = MintBg}
        mainContainer.Controls.Add(root)
        root.BringToFront()

        BuildLayout(root)
        AddHandler root.Resize, Sub() root.Invalidate(True)
    End Sub

    Private Shared Sub BuildLayout(host As Panel)
        ' Load shift data and denomination counts from DB FIRST
        LoadShiftData()
        _denoms = GetDrawer()

        Dim outer As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .BackColor = Color.Transparent,
            .ColumnCount = 1, .RowCount = 4,
            .Padding = New Padding(20, 12, 16, 12), .Margin = New Padding(0)
        }
        outer.RowStyles.Add(New RowStyle(SizeType.Absolute, 64))
        outer.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
        outer.RowStyles.Add(New RowStyle(SizeType.Absolute, 78))
        outer.RowStyles.Add(New RowStyle(SizeType.Absolute, 60))
        outer.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        host.Controls.Add(outer)

        Dim topBar As Control = BuildTopBar()
        Dim content As Control = BuildContentArea()
        Dim opsBar As Control = BuildOpsBar()
        Dim actionBar As Control = BuildActionBar()

        topBar.Dock = DockStyle.Fill
        content.Dock = DockStyle.Fill
        opsBar.Dock = DockStyle.Fill
        actionBar.Dock = DockStyle.Fill

        outer.Controls.Add(topBar, 0, 0)
        outer.Controls.Add(content, 0, 1)
        outer.Controls.Add(opsBar, 0, 2)
        outer.Controls.Add(actionBar, 0, 3)

        Recalc()
    End Sub

    ' =========================================================================
    '  LOAD SHIFT DATA FROM DB
    ' =========================================================================
    Private Shared Sub LoadShiftData()
        _shiftId = 0
        _openingFloat = 0D
        _cashSales = 0D
        _cashPaidOut = 0D
        _cashRefunds = 0D
        _cashPaidIn = 0D
        _cashDropped = 0D
        _shiftStarted = DateTime.MinValue

        Try
            Using conn = Db.GetConnection()
                conn.Open()

                ' Find the user's current active shift
                Using cmd As New MySqlCommand(
                    "SELECT shift_id, opening_float, started_at FROM shifts " &
                    "WHERE user_id=@uid AND ended_at IS NULL ORDER BY started_at DESC LIMIT 1", conn)
                    cmd.Parameters.AddWithValue("@uid", Session.CurrentUserId)
                    Using rdr = cmd.ExecuteReader()
                        If rdr.Read() Then
                            _shiftId = CInt(rdr("shift_id"))
                            _openingFloat = CDec(rdr("opening_float"))
                            _shiftStarted = CDate(rdr("started_at"))
                        End If
                    End Using
                End Using

                If _shiftId <= 0 Then Return

                ' Find the Cash payment method ID
                Dim cashMethodId As Integer = 0
                Using cmd As New MySqlCommand(
                    "SELECT method_id FROM payment_methods WHERE LOWER(name) LIKE '%cash%' LIMIT 1", conn)
                    Dim result = cmd.ExecuteScalar()
                    If result IsNot Nothing Then cashMethodId = CInt(result)
                End Using

                ' Total cash sales for this shift
                If cashMethodId > 0 Then
                    Using cmd As New MySqlCommand(
                        "SELECT COALESCE(SUM(total),0) FROM sales " &
                        "WHERE shift_id=@sid AND method_id=@mid AND status='Completed'", conn)
                        cmd.Parameters.AddWithValue("@sid", _shiftId)
                        cmd.Parameters.AddWithValue("@mid", cashMethodId)
                        _cashSales = CDec(cmd.ExecuteScalar())
                    End Using
                End If

                ' Cash refunds PROCESSED during this shift. These remove cash from
                ' THIS drawer, regardless of which shift the original sale was in,
                ' so we match on who processed it and when — not the sale's shift.
                Using cmd As New MySqlCommand(
                    "SELECT COALESCE(SUM(r.total_refund),0) FROM returns r " &
                    "WHERE LOWER(r.refund_method) LIKE '%cash%' " &
                    "  AND r.processed_by=@uid " &
                    "  AND r.return_datetime >= @start", conn)
                    cmd.Parameters.AddWithValue("@uid", Session.CurrentUserId)
                    cmd.Parameters.AddWithValue("@start", _shiftStarted)
                    _cashRefunds = CDec(cmd.ExecuteScalar())
                End Using

                ' Pay-ins / pay-outs / drops recorded for this shift
                Using cmd As New MySqlCommand(
                    "SELECT " &
                    "  COALESCE(SUM(CASE WHEN movement_type='PayIn'  THEN amount END),0) AS pin, " &
                    "  COALESCE(SUM(CASE WHEN movement_type='PayOut' THEN amount END),0) AS pout, " &
                    "  COALESCE(SUM(CASE WHEN movement_type='Drop'   THEN amount END),0) AS pdrop " &
                    "FROM cash_movements WHERE shift_id=@sid", conn)
                    cmd.Parameters.AddWithValue("@sid", _shiftId)
                    Using rdr = cmd.ExecuteReader()
                        If rdr.Read() Then
                            _cashPaidIn = CDec(rdr("pin"))
                            _cashPaidOut = CDec(rdr("pout"))
                            _cashDropped = CDec(rdr("pdrop"))
                        End If
                    End Using
                End Using
            End Using
        Catch ex As Exception
            System.Diagnostics.Debug.WriteLine("LoadShiftData DB error: " & ex.Message)
        End Try
    End Sub

    ' ── Save a single denomination count to DB ────────────────────────────────
    Private Shared Sub SaveDenomCount(d As Denom)
        If _shiftId <= 0 Then Return
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                ' Try UPDATE first; if no row affected, INSERT
                Using cmd As New MySqlCommand(
                    "UPDATE cash_counts SET qty=@qty WHERE shift_id=@sid AND denomination=@denom AND is_bill=@bill", conn)
                    cmd.Parameters.AddWithValue("@qty", d.Count)
                    cmd.Parameters.AddWithValue("@sid", _shiftId)
                    cmd.Parameters.AddWithValue("@denom", d.Value)
                    cmd.Parameters.AddWithValue("@bill", d.IsBill)
                    If cmd.ExecuteNonQuery() = 0 Then
                        Using cmd2 As New MySqlCommand(
                            "INSERT INTO cash_counts (shift_id, denomination, is_bill, qty) " &
                            "VALUES (@sid, @denom, @bill, @qty)", conn)
                            cmd2.Parameters.AddWithValue("@sid", _shiftId)
                            cmd2.Parameters.AddWithValue("@denom", d.Value)
                            cmd2.Parameters.AddWithValue("@bill", d.IsBill)
                            cmd2.Parameters.AddWithValue("@qty", d.Count)
                            cmd2.ExecuteNonQuery()
                        End Using
                    End If
                End Using
            End Using
        Catch
        End Try
    End Sub

    ' =========================================================================
    '  1. TOP BAR
    ' =========================================================================
    Private Shared Function BuildTopBar() As Control
        Dim bar As New BufferedPanel With {
            .Dock = DockStyle.Fill, .BackColor = MintDark, .Margin = New Padding(0, 0, 0, 10)
        }
        AddHandler bar.Paint,
            Sub(s As Object, e As PaintEventArgs)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect = bar.ClientRectangle : rect.Inflate(-1, -1)
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

        Dim lblTitle As New Label With {
            .Text = "CASH DRAWER",
            .Font = New Font("Segoe UI Semibold", 13, FontStyle.Bold),
            .ForeColor = Color.White, .BackColor = Color.Transparent, .AutoSize = True, .Location = New Point(18, 9)
        }

        ' Build subtitle from real shift data
        Dim shiftNoText As String = If(_shiftId > 0, "SESSION #DR-" & _shiftId.ToString("D4"), "NO ACTIVE SHIFT")
        Dim openedText As String = If(_shiftStarted <> DateTime.MinValue, _shiftStarted.ToString("h:mm tt"), "—")

        Dim lblSub As New Label With {
            .Text = shiftNoText & "   ·   CASHIER: " & Session.CurrentFullName.ToUpper() & "   ·   OPENED " & openedText,
            .Font = New Font("Segoe UI", 9),
            .ForeColor = Color.FromArgb(160, 215, 200), .BackColor = Color.Transparent,
            .AutoSize = True, .Location = New Point(20, 36)
        }

        Dim lblClock As New Label With {
            .Text = DateTime.Now.ToString("MMM dd, yyyy  |  hh:mm tt"),
            .Font = New Font("Segoe UI", 10), .ForeColor = Color.FromArgb(200, 240, 225),
            .BackColor = Color.Transparent, .AutoSize = True
        }
        Dim timer As New Timer With {.Interval = 1000}
        AddHandler timer.Tick,
            Sub()
                If Not lblClock.IsDisposed Then
                    lblClock.Text = DateTime.Now.ToString("MMM dd, yyyy  |  hh:mm tt")
                    lblClock.Location = New Point(bar.Width - lblClock.Width - 18, (bar.Height - lblClock.Height) \ 2)
                Else
                    timer.Stop() : timer.Dispose()
                End If
            End Sub
        timer.Start()

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
        btnRefresh.FlatAppearance.BorderColor = Color.FromArgb(0, 120, 100)
        AddHandler btnRefresh.Paint,
            Sub(s, e)
                Dim bb = CType(s, Button)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Using path As New GraphicsPath()
                    Dim r = 6
                    Dim rect = bb.ClientRectangle
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
                If _mainContainer IsNot Nothing Then LoadCash(_mainContainer)
            End Sub

        bar.Controls.AddRange({lblTitle, lblSub, lblClock, btnRefresh})
        AddHandler bar.Resize,
            Sub()
                lblClock.Location = New Point(bar.Width - lblClock.Width - 18, (bar.Height - lblClock.Height) \ 2)
                btnRefresh.Location = New Point(
                    bar.Width - lblClock.Width - btnRefresh.Width - 30,
                    (bar.Height - btnRefresh.Height) \ 2)
            End Sub
        Return bar
    End Function

    ' =========================================================================
    '  2. CONTENT
    ' =========================================================================
    Private Shared Function BuildContentArea() As Control
        Dim split As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 1,
            .BackColor = Color.Transparent, .Margin = New Padding(0), .Padding = New Padding(0)
        }
        split.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 60.0F))
        split.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 40.0F))
        split.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))

        Dim left As Control = BuildCountCard()
        Dim right As Control = BuildReconcileCard()
        left.Dock = DockStyle.Fill
        right.Dock = DockStyle.Fill

        split.Controls.Add(left, 0, 0)
        split.Controls.Add(right, 1, 0)
        Return split
    End Function

    ' =========================================================================
    '  LEFT — CASH COUNT CARD
    ' =========================================================================
    Private Shared Function BuildCountCard() As Control
        Dim card As New BufferedPanel With {
            .Dock = DockStyle.Fill, .BackColor = CardBg, .Margin = New Padding(0, 0, 8, 0)
        }
        AddHandler card.Paint,
            Sub(s As Object, e As PaintEventArgs) PanelRenderer.Draw(CType(s, Panel), e, True)

        Dim header = MakeCardHeader("Cash Count", "cash.png", "₱")
        card.Controls.Add(header)
        card.Controls.SetChildIndex(header, 0)

        Dim colHeader As New Panel With {
            .Top = 60, .Left = 0, .Height = 34, .BackColor = Color.FromArgb(247, 250, 249),
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        }
        AddHandler card.Resize, Sub() colHeader.Width = card.Width
        AddHandler colHeader.Paint,
            Sub(s, e)
                Using p As New Pen(SoftBorder, 1)
                    e.Graphics.DrawLine(p, 0, colHeader.Height - 1, colHeader.Width, colHeader.Height - 1)
                    e.Graphics.DrawLine(p, 0, 0, colHeader.Width, 0)
                End Using
            End Sub

        Dim colGrid As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 3, .RowCount = 1,
            .BackColor = Color.Transparent, .Padding = New Padding(18, 0, 18, 0)
        }
        colGrid.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
        colGrid.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 130))
        colGrid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        colGrid.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 140))

        Dim hdrTxt() As String = {"Denomination", "Count", "Subtotal"}
        Dim hdrAl() As ContentAlignment = {ContentAlignment.MiddleLeft, ContentAlignment.MiddleCenter, ContentAlignment.MiddleRight}
        For i = 0 To 2
            colGrid.Controls.Add(New Label With {
                .Text = hdrTxt(i), .Dock = DockStyle.Fill, .TextAlign = hdrAl(i),
                .Font = New Font("Segoe UI Semibold", 10),
                .ForeColor = Color.FromArgb(122, 152, 144), .BackColor = Color.Transparent
            }, i, 0)
        Next
        colHeader.Controls.Add(colGrid)
        card.Controls.Add(colHeader)

        Dim rowList As New Panel With {
            .Top = 94, .Left = 0, .AutoScroll = True, .BackColor = Color.White,
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top Or AnchorStyles.Bottom
        }
        AddHandler card.Resize,
            Sub()
                rowList.Width = card.Width
                rowList.Height = card.Height - 94 - 52
            End Sub

        Dim SyncGutter As Action =
            Sub()
                Dim sbw As Integer = If(rowList.VerticalScroll.Visible, SystemInformation.VerticalScrollBarWidth, 0)
                colGrid.Padding = New Padding(18, 0, 18 + sbw, 0)
            End Sub
        AddHandler rowList.Layout, Sub() SyncGutter()
        AddHandler card.Resize, Sub() SyncGutter()

        rowList.Controls.Add(MakeSectionHeader("BILLS"))
        For Each d In _denoms.Where(Function(x) x.IsBill)
            rowList.Controls.Add(BuildDenomRow(d))
        Next
        rowList.Controls.Add(MakeSectionHeader("COINS"))
        For Each d In _denoms.Where(Function(x) Not x.IsBill)
            rowList.Controls.Add(BuildDenomRow(d))
        Next
        For i = rowList.Controls.Count - 1 To 0 Step -1
            rowList.Controls.SetChildIndex(rowList.Controls(i), rowList.Controls.Count - 1 - i)
        Next
        card.Controls.Add(rowList)

        Dim footer As New Panel With {
            .Height = 52, .Left = 0, .BackColor = Color.FromArgb(247, 250, 249),
            .Padding = New Padding(18, 0, 18, 0),
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Bottom
        }
        AddHandler card.Resize,
            Sub()
                footer.Width = card.Width
                footer.Top = card.Height - footer.Height
            End Sub
        AddHandler footer.Paint,
            Sub(s, e)
                Using p As New Pen(SoftBorder, 1) : e.Graphics.DrawLine(p, 0, 0, footer.Width, 0) : End Using
            End Sub

        Dim lblCaption As New Label With {
            .Text = "CASH COUNTED", .Font = New Font("Segoe UI Semibold", 10),
            .ForeColor = SubText, .AutoSize = True, .Location = New Point(0, 18)
        }
        _lblCountFooter = New Label With {
            .Text = "₱ 0.00", .Font = New Font("Segoe UI", 16, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Anchor = AnchorStyles.Right Or AnchorStyles.Top
        }
        AddHandler footer.Resize,
            Sub() _lblCountFooter.Location = New Point(footer.Width - _lblCountFooter.Width - 18, 13)
        footer.Controls.Add(lblCaption)
        footer.Controls.Add(_lblCountFooter)
        card.Controls.Add(footer)

        Return card
    End Function

    Private Shared Function MakeSectionHeader(text As String) As Panel
        Dim p As New Panel With {.Height = 30, .Dock = DockStyle.Top, .BackColor = Color.FromArgb(250, 252, 251), .Padding = New Padding(18, 0, 18, 0)}
        AddHandler p.Paint,
            Sub(s, e)
                Using pen As New Pen(Color.FromArgb(237, 243, 242), 1)
                    e.Graphics.DrawLine(pen, 0, p.Height - 1, p.Width, p.Height - 1)
                End Using
            End Sub
        p.Controls.Add(New Label With {
            .Text = text, .Font = New Font("Segoe UI Semibold", 9, FontStyle.Bold),
            .ForeColor = MintPrimary, .AutoSize = True, .Location = New Point(0, 8)
        })
        Return p
    End Function

    Private Shared Function BuildDenomRow(d As Denom) As Panel
        Dim row As New Panel With {
            .Height = 48, .Dock = DockStyle.Top, .BackColor = Color.White,
            .Margin = New Padding(0), .Padding = New Padding(18, 0, 18, 0)
        }
        AddHandler row.Paint,
            Sub(s, e)
                Using p As New Pen(Color.FromArgb(237, 243, 242), 1)
                    e.Graphics.DrawLine(p, 0, row.Height - 1, row.Width, row.Height - 1)
                End Using
            End Sub

        Dim grid As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .BackColor = Color.Transparent, .ColumnCount = 3, .RowCount = 1, .Margin = New Padding(0)
        }
        grid.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 130))
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 140))

        ' Denomination chip
        Dim chipWrap As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.Transparent}
        Dim chipText As String = "₱" & d.Value.ToString("#,0.##")
        Dim chip As New Panel With {.Size = New Size(96, 28), .BackColor = Color.Transparent}
        AddHandler chip.Paint,
            Sub(s, e)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim r As New Rectangle(0, 0, chip.Width - 1, chip.Height - 1)
                Using b As New SolidBrush(TileBg) : g.FillRoundedRectangle(b, r, 8) : End Using
                Using pn As New Pen(SoftBorder, 1) : g.DrawRoundedRectangle(pn, r, 8) : End Using
                TextRenderer.DrawText(g, chipText, New Font("Segoe UI Semibold", 10, FontStyle.Bold),
                    r, MintDark, TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub
        AddHandler chipWrap.Resize, Sub() chip.Location = New Point(0, (chipWrap.Height - chip.Height) \ 2)
        chipWrap.Controls.Add(chip)
        grid.Controls.Add(chipWrap, 0, 0)

        ' Stepper
        Dim stepWrap As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.Transparent}
        Dim lblQty As New Label With {
            .Text = d.Count.ToString(), .Size = New Size(40, 26), .TextAlign = ContentAlignment.MiddleCenter,
            .Font = New Font("Segoe UI Semibold", 11), .ForeColor = MintDark, .BackColor = Color.Transparent
        }
        Dim btnMinus As New Button With {.Text = "−", .Size = New Size(28, 26), .FlatStyle = FlatStyle.Flat,
            .Font = New Font("Segoe UI", 11, FontStyle.Bold), .ForeColor = SubText, .BackColor = TileBg, .Cursor = Cursors.Hand}
        Dim btnPlus As New Button With {.Text = "+", .Size = New Size(28, 26), .FlatStyle = FlatStyle.Flat,
            .Font = New Font("Segoe UI", 11, FontStyle.Bold), .ForeColor = MintPrimary, .BackColor = TileBg, .Cursor = Cursors.Hand}
        btnMinus.FlatAppearance.BorderColor = SoftBorder
        btnPlus.FlatAppearance.BorderColor = SoftBorder

        ' Line subtotal
        Dim lblLine As New Label With {
            .Text = "₱ " & (d.Value * d.Count).ToString("N2"), .Dock = DockStyle.Fill,
            .TextAlign = ContentAlignment.MiddleRight,
            .Font = New Font("Segoe UI Semibold", 10), .ForeColor = MintDark, .BackColor = Color.Transparent
        }

        Dim SyncRow As Action =
            Sub()
                lblQty.Text = d.Count.ToString()
                lblLine.Text = "₱ " & (d.Value * d.Count).ToString("N2")
                lblLine.ForeColor = If(d.Count > 0, MintDark, Color.FromArgb(170, 178, 175))
            End Sub

        AddHandler btnMinus.Click,
            Sub()
                If d.Count > 0 Then
                    d.Count -= 1
                    SyncRow()
                    Recalc()
                    SaveDenomCount(d)
                End If
            End Sub
        AddHandler btnPlus.Click,
            Sub()
                d.Count += 1
                SyncRow()
                Recalc()
                SaveDenomCount(d)
            End Sub

        Dim layoutStepper As Action =
            Sub()
                Dim total As Integer = btnMinus.Width + lblQty.Width + btnPlus.Width
                Dim sx As Integer = (stepWrap.Width - total) \ 2
                Dim cy As Integer = (stepWrap.Height - 26) \ 2
                btnMinus.Location = New Point(sx, cy)
                lblQty.Location = New Point(sx + btnMinus.Width, cy)
                btnPlus.Location = New Point(sx + btnMinus.Width + lblQty.Width, cy)
            End Sub
        AddHandler stepWrap.Resize, Sub() layoutStepper()
        stepWrap.Controls.AddRange({btnMinus, lblQty, btnPlus})
        grid.Controls.Add(stepWrap, 1, 0)
        grid.Controls.Add(lblLine, 2, 0)

        row.Controls.Add(grid)
        SyncRow()
        Return row
    End Function

    ' =========================================================================
    '  RIGHT — RECONCILIATION CARD
    ' =========================================================================
    Private Shared Function BuildReconcileCard() As Control
        Dim card As New BufferedPanel With {.Dock = DockStyle.Fill, .BackColor = CardBg, .Margin = New Padding(0)}
        AddHandler card.Paint,
            Sub(s As Object, e As PaintEventArgs)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect = card.ClientRectangle : rect.Inflate(-1, -1)
                Using br As New SolidBrush(CardBg) : g.FillRoundedRectangle(br, rect, 12) : End Using
                Using p As New Pen(CardBorder, 1.2F) : g.DrawRoundedRectangle(p, rect, 12) : End Using
            End Sub

        Dim outer As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 3,
            .BackColor = Color.Transparent, .Padding = New Padding(0), .Margin = New Padding(0)
        }
        outer.RowStyles.Add(New RowStyle(SizeType.Absolute, 52))
        outer.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
        outer.RowStyles.Add(New RowStyle(SizeType.Absolute, 100))

        ' Header
        Dim head As New BufferedPanel With {.Dock = DockStyle.Fill, .BackColor = Color.Transparent}
        AddHandler head.Paint,
            Sub(s As Object, e As PaintEventArgs)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect = head.ClientRectangle
                Using path As New GraphicsPath()
                    Dim r = 12
                    path.AddArc(rect.X, rect.Y, r, r, 180, 90)
                    path.AddArc(rect.Right - r, rect.Y, r, r, 270, 90)
                    path.AddLine(rect.Right, rect.Bottom, rect.X, rect.Bottom)
                    path.CloseFigure()
                    Using br As New SolidBrush(MintDark) : g.FillPath(br, path) : End Using
                End Using
            End Sub

        Dim iconCircle As New Panel With {.Size = New Size(28, 28), .BackColor = Color.Transparent}
        AddHandler iconCircle.Paint,
            Sub(s As Object, e As PaintEventArgs)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Using br As New SolidBrush(Color.FromArgb(45, 255, 255, 255)) : e.Graphics.FillEllipse(br, 0, 0, 27, 27) : End Using
                Using p As New Pen(Color.White, 1.8F) With {.StartCap = LineCap.Round, .EndCap = LineCap.Round}
                    e.Graphics.DrawLine(p, 7, 10, 20, 10)
                    e.Graphics.DrawLine(p, 7, 15, 20, 15)
                    e.Graphics.DrawLine(p, 7, 20, 15, 20)
                End Using
            End Sub

        Dim lblHead As New Label With {
            .Text = "RECONCILIATION", .Font = New Font("Segoe UI Semibold", 11, FontStyle.Bold),
            .ForeColor = Color.White, .BackColor = Color.Transparent, .AutoSize = True
        }
        head.Controls.AddRange({iconCircle, lblHead})
        AddHandler head.Resize,
            Sub()
                iconCircle.Location = New Point(16, (head.Height - iconCircle.Height) \ 2)
                lblHead.Location = New Point(52, (head.Height - lblHead.Height) \ 2)
            End Sub

        ' Stats
        Dim stats As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 7,
            .BackColor = Color.Transparent, .Padding = New Padding(16, 12, 16, 8)
        }
        stats.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 58.0F))
        stats.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 42.0F))
        For i = 0 To 3
            stats.RowStyles.Add(New RowStyle(SizeType.Percent, 16.0F))
        Next
        stats.RowStyles.Add(New RowStyle(SizeType.Absolute, 14))
        stats.RowStyles.Add(New RowStyle(SizeType.Percent, 16.0F))
        stats.RowStyles.Add(New RowStyle(SizeType.Percent, 16.0F))

        Dim AddStat As Action(Of Integer, String, String, Boolean, Color) =
            Sub(rowIdx As Integer, labelText As String, valueText As String, bold As Boolean, valColor As Color)
                Dim l As New Label With {
                    .Text = labelText, .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleLeft,
                    .Font = If(bold, New Font("Segoe UI Semibold", 10.5F), New Font("Segoe UI", 10)),
                    .ForeColor = If(bold, MintDark, SubText), .BackColor = Color.Transparent
                }
                Dim v As New Label With {
                    .Text = valueText, .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleRight,
                    .Font = If(bold, New Font("Segoe UI Semibold", 11, FontStyle.Bold), New Font("Segoe UI Semibold", 11)),
                    .ForeColor = valColor, .BackColor = Color.Transparent
                }
                stats.Controls.Add(l, 0, rowIdx)
                stats.Controls.Add(v, 1, rowIdx)
            End Sub

        ' Use real DB values
        AddStat(0, "OPENING FLOAT", "₱ " & _openingFloat.ToString("N2"), False, Color.FromArgb(50, 70, 65))
        AddStat(1, "+ CASH SALES", "₱ " & _cashSales.ToString("N2"), False, Color.FromArgb(0, 150, 122))
        AddStat(2, "− CASH PAID OUT", "₱ " & _cashPaidOut.ToString("N2"), False, DangerFore)
        AddStat(3, "− REFUNDS", "₱ " & _cashRefunds.ToString("N2"), False, DangerFore)

        Dim div As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.Transparent}
        AddHandler div.Paint,
            Sub(s, e)
                Using pen As New Pen(CardBorder, 1.5F) : e.Graphics.DrawLine(pen, 0, div.Height \ 2, div.Width, div.Height \ 2) : End Using
            End Sub
        stats.SetColumnSpan(div, 2)
        stats.Controls.Add(div, 0, 4)

        Dim lblExpL As New Label With {
            .Text = "EXPECTED IN DRAWER", .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleLeft,
            .Font = New Font("Segoe UI Semibold", 10.5F), .ForeColor = MintDark, .BackColor = Color.Transparent
        }
        _lblExpected = New Label With {
            .Text = "₱ 0.00", .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleRight,
            .Font = New Font("Segoe UI Semibold", 11, FontStyle.Bold), .ForeColor = MintDark, .BackColor = Color.Transparent
        }
        stats.Controls.Add(lblExpL, 0, 5)
        stats.Controls.Add(_lblExpected, 1, 5)

        Dim lblCntL As New Label With {
            .Text = "CASH COUNTED", .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleLeft,
            .Font = New Font("Segoe UI Semibold", 10.5F), .ForeColor = MintDark, .BackColor = Color.Transparent
        }
        _lblCounted = New Label With {
            .Text = "₱ 0.00", .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleRight,
            .Font = New Font("Segoe UI Semibold", 11, FontStyle.Bold), .ForeColor = MintDark, .BackColor = Color.Transparent
        }
        stats.Controls.Add(lblCntL, 0, 6)
        stats.Controls.Add(_lblCounted, 1, 6)

        ' Variance block
        Dim block As New BufferedPanel With {
            .Dock = DockStyle.Fill, .BackColor = Color.Transparent, .Padding = New Padding(12, 6, 12, 6)
        }
        Dim varCard As New BufferedPanel With {.Dock = DockStyle.Fill, .BackColor = MintDark}
        AddHandler varCard.Paint,
            Sub(s As Object, e As PaintEventArgs)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect = varCard.ClientRectangle : rect.Inflate(-1, -1)
                Using br As New LinearGradientBrush(rect, MintDark, Color.FromArgb(0, 60, 50), 90.0F)
                    g.FillRoundedRectangle(br, rect, 10)
                End Using
            End Sub
        Dim tl As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 1, .BackColor = Color.Transparent
        }
        tl.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 42.0F))
        tl.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 58.0F))
        tl.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
        Dim lblVL As New Label With {
            .Text = "OVER / SHORT", .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleLeft,
            .Font = New Font("Segoe UI", 12, FontStyle.Bold), .ForeColor = Color.FromArgb(200, 240, 225),
            .BackColor = Color.Transparent, .Padding = New Padding(14, 0, 0, 0)
        }
        _lblVariance = New Label With {
            .Text = "₱ 0.00", .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleRight,
            .Font = New Font("Segoe UI", 18, FontStyle.Bold), .ForeColor = OkGreen,
            .BackColor = Color.Transparent, .Padding = New Padding(0, 0, 14, 0)
        }
        tl.Controls.Add(lblVL, 0, 0)
        tl.Controls.Add(_lblVariance, 1, 0)
        varCard.Controls.Add(tl)
        block.Controls.Add(varCard)

        outer.Controls.Add(head, 0, 0)
        outer.Controls.Add(stats, 0, 1)
        outer.Controls.Add(block, 0, 2)
        card.Controls.Add(outer)
        Return card
    End Function

    ' =========================================================================
    '  RECALC
    ' =========================================================================
    Private Shared Sub Recalc()
        If _denoms Is Nothing Then Return
        Dim counted As Decimal = _denoms.Sum(Function(d) d.Value * d.Count)
        ' Expected drawer cash = float + cash sales + pay-ins − pay-outs − cash refunds − safe drops
        Dim expected As Decimal = _openingFloat + _cashSales + _cashPaidIn _
                                  - _cashPaidOut - _cashRefunds - _cashDropped
        Dim variance As Decimal = counted - expected

        If _lblCountFooter IsNot Nothing Then _lblCountFooter.Text = "₱ " & counted.ToString("N2")
        If _lblExpected IsNot Nothing Then _lblExpected.Text = "₱ " & expected.ToString("N2")
        If _lblCounted IsNot Nothing Then _lblCounted.Text = "₱ " & counted.ToString("N2")

        If _lblVariance IsNot Nothing Then
            If variance = 0D Then
                _lblVariance.Text = "✓  ₱ 0.00"
                _lblVariance.ForeColor = OkGreen
            ElseIf variance > 0D Then
                _lblVariance.Text = "▲  +₱ " & variance.ToString("N2")
                _lblVariance.ForeColor = OverGold
            Else
                _lblVariance.Text = "▼  −₱ " & Math.Abs(variance).ToString("N2")
                _lblVariance.ForeColor = ShortRed
            End If
        End If
    End Sub

    ' =========================================================================
    '  CASH DRAWER ACTIONS  (Cash In / Out / Drop · No Sale · Close Drawer)
    ' =========================================================================

    ''' <summary>Record a PayIn / PayOut / Drop against the current shift.</summary>
    Private Shared Sub CashMovement(movementType As String, title As String, notePrompt As String)
        If _shiftId <= 0 Then
            AppModal.Show("There is no open shift. Open a shift before recording cash movements.",
                            title, MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        Dim res = PromptAmount(title, notePrompt)
        If Not res.ok Then Return
        If res.amount <= 0 Then
            AppModal.Show("Enter an amount greater than zero.", title,
                            MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "INSERT INTO cash_movements (shift_id, movement_type, amount, reason, created_by, created_at) " &
                    "VALUES (@sid, @type, @amt, @reason, @uid, NOW())", conn)
                    cmd.Parameters.AddWithValue("@sid", _shiftId)
                    cmd.Parameters.AddWithValue("@type", movementType)
                    cmd.Parameters.AddWithValue("@amt", res.amount)
                    cmd.Parameters.AddWithValue("@reason", If(String.IsNullOrWhiteSpace(res.note), CObj(DBNull.Value), res.note))
                    cmd.Parameters.AddWithValue("@uid", Session.CurrentUserId)
                    cmd.ExecuteNonQuery()
                End Using
            End Using

            AppModal.Show(title & " of ₱" & res.amount.ToString("N2") & " recorded.",
                            title, MessageBoxButtons.OK, MessageBoxIcon.Information)
            If _mainContainer IsNot Nothing Then LoadCash(_mainContainer)   ' refresh expected cash
        Catch ex As Exception
            AppModal.Show("Could not record the cash movement:" & Environment.NewLine &
                            ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    ''' <summary>No-sale drawer open. No financial effect; just acknowledges the open.</summary>
    Private Shared Sub NoSaleOpen()
        ' No cash event table exists; this simply signals/acknowledges a drawer open.
        ' If a physical drawer kick is wired later, trigger it here.
        AppModal.Show("Drawer opened (No Sale).", "No Sale",
                        MessageBoxButtons.OK, MessageBoxIcon.Information)
    End Sub

    ''' <summary>Close the active shift: store counted/expected/variance and stamp ended_at.</summary>
    Private Shared Sub CloseDrawer()
        If _shiftId <= 0 Then
            AppModal.Show("There is no open shift to close.", "Close Drawer",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        Dim counted As Decimal = If(_denoms Is Nothing, 0D, _denoms.Sum(Function(d) d.Value * d.Count))
        Dim expected As Decimal = _openingFloat + _cashSales + _cashPaidIn _
                                  - _cashPaidOut - _cashRefunds - _cashDropped
        Dim variance As Decimal = counted - expected

        Dim msg As String =
            "Close this shift?" & vbCrLf & vbCrLf &
            "  Expected:  ₱ " & expected.ToString("N2") & vbCrLf &
            "  Counted:   ₱ " & counted.ToString("N2") & vbCrLf &
            "  Variance:  ₱ " & variance.ToString("N2") & vbCrLf & vbCrLf &
            "Once closed, this shift can no longer record cash."
        If AppModal.Show(msg, "Close Drawer", MessageBoxButtons.YesNo,
                           MessageBoxIcon.Question) <> DialogResult.Yes Then Return

        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "UPDATE shifts SET ended_at=NOW(), expected_cash=@exp, " &
                    "counted_cash=@cnt, variance=@var, status='Closed' " &
                    "WHERE shift_id=@sid", conn)
                    cmd.Parameters.AddWithValue("@exp", expected)
                    cmd.Parameters.AddWithValue("@cnt", counted)
                    cmd.Parameters.AddWithValue("@var", variance)
                    cmd.Parameters.AddWithValue("@sid", _shiftId)
                    cmd.ExecuteNonQuery()
                End Using
            End Using

            AppModal.Show("Shift closed." & vbCrLf & "Variance: ₱ " & variance.ToString("N2"),
                            "Drawer Closed", MessageBoxButtons.OK, MessageBoxIcon.Information)
            If _mainContainer IsNot Nothing Then LoadCash(_mainContainer)
        Catch ex As Exception
            AppModal.Show("Could not close the drawer:" & Environment.NewLine &
                            ex.Message, "Close Drawer", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    ''' <summary>Print the denomination breakdown and reconciliation.</summary>
    Private Shared Sub PrintCount()
        Dim counted As Decimal = If(_denoms Is Nothing, 0D, _denoms.Sum(Function(d) d.Value * d.Count))
        Dim expected As Decimal = _openingFloat + _cashSales + _cashPaidIn _
                                  - _cashPaidOut - _cashRefunds - _cashDropped
        Dim variance As Decimal = counted - expected

        Dim lines As New List(Of ReceiptPrinter.RLine)
        lines.Add(New ReceiptPrinter.RLine("PAINT STORE POS", , ReceiptPrinter.LineStyle.Title))
        lines.Add(New ReceiptPrinter.RLine("Cash Drawer Count", , ReceiptPrinter.LineStyle.Muted))
        lines.Add(ReceiptPrinter.Divider())
        lines.Add(New ReceiptPrinter.RLine("Session:", If(_shiftId > 0, "DR-" & _shiftId.ToString("D4"), "—")))
        lines.Add(New ReceiptPrinter.RLine("Cashier:", Session.CurrentFullName))
        lines.Add(New ReceiptPrinter.RLine("Printed:", DateTime.Now.ToString("MMM d, yyyy h:mm tt")))
        lines.Add(ReceiptPrinter.Divider())

        If _denoms IsNot Nothing Then
            For Each d In _denoms.Where(Function(x) x.Count > 0).OrderByDescending(Function(x) x.Value)
                lines.Add(New ReceiptPrinter.RLine(
                    "₱ " & d.Value.ToString("N2") & " × " & d.Count.ToString(),
                    "₱ " & (d.Value * d.Count).ToString("N2")))
            Next
        End If

        lines.Add(ReceiptPrinter.Divider())
        lines.Add(New ReceiptPrinter.RLine("Opening float", "₱ " & _openingFloat.ToString("N2")))
        lines.Add(New ReceiptPrinter.RLine("Cash sales", "₱ " & _cashSales.ToString("N2")))
        If _cashPaidIn > 0 Then lines.Add(New ReceiptPrinter.RLine("Pay-ins", "₱ " & _cashPaidIn.ToString("N2")))
        If _cashPaidOut > 0 Then lines.Add(New ReceiptPrinter.RLine("Pay-outs", "− ₱ " & _cashPaidOut.ToString("N2")))
        If _cashRefunds > 0 Then lines.Add(New ReceiptPrinter.RLine("Cash refunds", "− ₱ " & _cashRefunds.ToString("N2")))
        If _cashDropped > 0 Then lines.Add(New ReceiptPrinter.RLine("Safe drops", "− ₱ " & _cashDropped.ToString("N2")))
        lines.Add(ReceiptPrinter.Divider())
        lines.Add(New ReceiptPrinter.RLine("Expected", "₱ " & expected.ToString("N2"), ReceiptPrinter.LineStyle.Bold))
        lines.Add(New ReceiptPrinter.RLine("Counted", "₱ " & counted.ToString("N2"), ReceiptPrinter.LineStyle.Bold))
        lines.Add(New ReceiptPrinter.RLine("Variance", "₱ " & variance.ToString("N2"), ReceiptPrinter.LineStyle.Bold))

        ReceiptPrinter.Preview("Cash Drawer Count", lines)
    End Sub

    ''' <summary>Modal amount + note prompt. Returns (ok, amount, note).</summary>
    Private Shared Function PromptAmount(title As String, notePrompt As String) _
                                         As (ok As Boolean, amount As Decimal, note As String)
        Using f As New Form With {
                .Text = title,
                .FormBorderStyle = FormBorderStyle.FixedDialog,
                .StartPosition = FormStartPosition.CenterParent,
                .MaximizeBox = False, .MinimizeBox = False,
                .ClientSize = New Size(320, 190),
                .BackColor = Color.White, .Font = New Font("Segoe UI", 9.5F)}

            Dim lblAmt As New Label With {.Text = "Amount (₱)", .Location = New Point(18, 18), .AutoSize = True, .ForeColor = SubText}
            Dim txtAmt As New TextBox With {.Location = New Point(18, 38), .Width = 160, .Text = "0.00"}

            Dim lblNote As New Label With {.Text = notePrompt, .Location = New Point(18, 74), .AutoSize = True, .ForeColor = SubText}
            Dim txtNote As New TextBox With {.Location = New Point(18, 94), .Width = 284}

            Dim btnOk As New Button With {
                .Text = "OK", .DialogResult = DialogResult.OK,
                .Location = New Point(152, 142), .Size = New Size(72, 30),
                .FlatStyle = FlatStyle.Flat, .BackColor = MintPrimary, .ForeColor = Color.White}
            btnOk.FlatAppearance.BorderSize = 0
            Dim btnCancel As New Button With {
                .Text = "Cancel", .DialogResult = DialogResult.Cancel,
                .Location = New Point(230, 142), .Size = New Size(72, 30),
                .FlatStyle = FlatStyle.Flat, .BackColor = Color.FromArgb(232, 238, 237), .ForeColor = SubText}
            btnCancel.FlatAppearance.BorderSize = 0

            f.Controls.AddRange({lblAmt, txtAmt, lblNote, txtNote, btnOk, btnCancel})
            f.AcceptButton = btnOk
            f.CancelButton = btnCancel

            If f.ShowDialog() <> DialogResult.OK Then Return (False, 0D, "")

            Dim amt As Decimal = 0
            Decimal.TryParse(txtAmt.Text.Replace("₱", "").Replace(",", "").Trim(), amt)
            Return (True, Decimal.Round(amt, 2), txtNote.Text.Trim())
        End Using
    End Function

    ' =========================================================================
    '  3. OPERATIONS BAR
    ' =========================================================================
    Private Shared Function BuildOpsBar() As Control
        Dim host As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.Transparent, .Padding = New Padding(0, 10, 0, 4)}
        Dim layout As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 4, .RowCount = 1,
            .BackColor = Color.Transparent, .Margin = New Padding(0), .Padding = New Padding(0)
        }
        For i = 0 To 3
            layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 25.0F))
        Next
        layout.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))

        Dim btnIn = MakePayBtn("⬇  CASH IN", Color.FromArgb(0, 148, 74), Color.FromArgb(0, 100, 55), 0)
        Dim btnOut = MakePayBtn("⬆  CASH OUT", Color.FromArgb(196, 118, 22), Color.FromArgb(150, 88, 12), 4)
        Dim btnNoSale = MakePayBtn("⊟  NO SALE  ·  OPEN", Color.FromArgb(30, 100, 190), Color.FromArgb(20, 70, 140), 4)
        Dim btnDrop = MakePayBtn("🔒  DROP TO SAFE", Color.FromArgb(70, 90, 100), Color.FromArgb(50, 65, 74), 4)

        AddHandler btnIn.Click, Sub() CashMovement("PayIn", "Cash In", "Reason / source of cash")
        AddHandler btnOut.Click, Sub() CashMovement("PayOut", "Cash Out", "Reason for paying out")
        AddHandler btnDrop.Click, Sub() CashMovement("Drop", "Drop to Safe", "Note (e.g. envelope #)")
        AddHandler btnNoSale.Click, Sub() NoSaleOpen()

        layout.Controls.Add(btnIn, 0, 0)
        layout.Controls.Add(btnOut, 1, 0)
        layout.Controls.Add(btnNoSale, 2, 0)
        layout.Controls.Add(btnDrop, 3, 0)

        host.Controls.Add(layout)
        Return host
    End Function

    Private Shared Function MakePayBtn(text As String, normalClr As Color, hoverClr As Color, leftMargin As Integer) As Button
        Dim btn As New Button With {
            .Text = text, .Font = New Font("Segoe UI Semibold", 11), .FlatStyle = FlatStyle.Flat,
            .BackColor = normalClr, .ForeColor = Color.White, .Cursor = Cursors.Hand,
            .Margin = New Padding(leftMargin, 0, 0, 0), .Dock = DockStyle.Fill
        }
        btn.FlatAppearance.BorderSize = 0
        AddHandler btn.Paint,
            Sub(s As Object, e As PaintEventArgs)
                Dim b = CType(s, Button) : Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect = b.ClientRectangle
                Using path As New GraphicsPath()
                    Dim r = 10
                    path.AddArc(0, 0, r, r, 180, 90)
                    path.AddArc(rect.Width - r, 0, r, r, 270, 90)
                    path.AddArc(rect.Width - r, rect.Height - r, r, r, 0, 90)
                    path.AddArc(0, rect.Height - r, r, r, 90, 90)
                    path.CloseFigure()
                    Using br As New SolidBrush(b.BackColor) : g.FillPath(br, path) : End Using
                End Using
                TextRenderer.DrawText(g, b.Text, b.Font, rect, Color.White,
                    TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub
        AddHandler btn.MouseEnter,
            Sub()
                btn.BackColor = hoverClr
                btn.Invalidate()
            End Sub
        AddHandler btn.MouseLeave,
            Sub()
                btn.BackColor = normalClr
                btn.Invalidate()
            End Sub
        Return btn
    End Function

    ' =========================================================================
    '  4. ACTION BAR
    ' =========================================================================
    Private Shared Function BuildActionBar() As Control
        Dim host As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.Transparent, .Padding = New Padding(0, 4, 0, 0)}
        Dim layout As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 3, .RowCount = 1,
            .BackColor = Color.Transparent, .Margin = New Padding(0), .Padding = New Padding(0)
        }
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 26.0F))
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 30.0F))
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 44.0F))
        layout.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))

        ' RESET COUNT — zero all denominations and re-save to DB
        Dim btnReset = MakeActionBtn("⟲  RESET COUNT", DangerBack, DangerFore, 0)
        AddHandler btnReset.Click,
            Sub()
                If AppModal.Show("Reset all denomination counts to zero?", "Reset Count",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) = DialogResult.Yes Then
                    For Each d In _denoms
                        d.Count = 0
                        SaveDenomCount(d)
                    Next
                    Recalc()
                    ' Rebuild the drawer UI by reloading
                    If _mainContainer IsNot Nothing Then LoadCash(_mainContainer)
                End If
            End Sub

        layout.Controls.Add(btnReset, 0, 0)

        Dim btnPrint = MakeActionBtn("🖨  PRINT COUNT", Color.FromArgb(220, 235, 245), Color.FromArgb(30, 80, 160), 6)
        AddHandler btnPrint.Click, Sub() PrintCount()
        layout.Controls.Add(btnPrint, 1, 0)

        Dim btnClose = MakePrimaryBtn("🔐  CLOSE DRAWER", 6)
        AddHandler btnClose.Click, Sub() CloseDrawer()
        layout.Controls.Add(btnClose, 2, 0)

        host.Controls.Add(layout)
        Return host
    End Function

    Private Shared Function MakeActionBtn(text As String, backClr As Color, foreClr As Color, leftMargin As Integer) As Button
        Dim btn As New Button With {
            .Text = text, .Font = New Font("Segoe UI Semibold", 9.5F), .FlatStyle = FlatStyle.Flat,
            .BackColor = backClr, .ForeColor = foreClr, .Cursor = Cursors.Hand,
            .Margin = New Padding(leftMargin, 0, 0, 0), .Dock = DockStyle.Fill
        }
        btn.FlatAppearance.BorderSize = 0
        AddHandler btn.Paint,
            Sub(s As Object, e As PaintEventArgs)
                Dim b = CType(s, Button) : Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect = b.ClientRectangle
                Using path As New GraphicsPath()
                    Dim r = 8
                    path.AddArc(0, 0, r, r, 180, 90)
                    path.AddArc(rect.Width - r, 0, r, r, 270, 90)
                    path.AddArc(rect.Width - r, rect.Height - r, r, r, 0, 90)
                    path.AddArc(0, rect.Height - r, r, r, 90, 90)
                    path.CloseFigure()
                    Using br As New SolidBrush(b.BackColor) : g.FillPath(br, path) : End Using
                    Using p As New Pen(Color.FromArgb(30, foreClr.R, foreClr.G, foreClr.B), 1.0F) : g.DrawPath(p, path) : End Using
                End Using
                TextRenderer.DrawText(g, b.Text, b.Font, rect, b.ForeColor,
                    TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub
        Dim ob = backClr
        Dim hb As Color = Color.FromArgb(Math.Max(0, ob.R - 18), Math.Max(0, ob.G - 18), Math.Max(0, ob.B - 18))
        AddHandler btn.MouseEnter,
            Sub()
                btn.BackColor = hb
                btn.Invalidate()
            End Sub
        AddHandler btn.MouseLeave,
            Sub()
                btn.BackColor = ob
                btn.Invalidate()
            End Sub
        Return btn
    End Function

    Private Shared Function MakePrimaryBtn(text As String, leftMargin As Integer) As Button
        Dim btn As New Button With {
            .Text = text, .Font = New Font("Segoe UI Semibold", 11, FontStyle.Bold), .FlatStyle = FlatStyle.Flat,
            .BackColor = MintPrimary, .ForeColor = Color.White, .Cursor = Cursors.Hand,
            .Margin = New Padding(leftMargin, 0, 0, 0), .Dock = DockStyle.Fill
        }
        btn.FlatAppearance.BorderSize = 0
        AddHandler btn.Paint,
            Sub(s As Object, e As PaintEventArgs)
                Dim b = CType(s, Button) : Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect = b.ClientRectangle : rect.Inflate(-1, -1)
                Using path As New GraphicsPath()
                    Dim r = 8
                    path.AddArc(rect.X, rect.Y, r, r, 180, 90)
                    path.AddArc(rect.Right - r, rect.Y, r, r, 270, 90)
                    path.AddArc(rect.Right - r, rect.Bottom - r, r, r, 0, 90)
                    path.AddArc(rect.X, rect.Bottom - r, r, r, 90, 90)
                    path.CloseFigure()
                    Using br As New LinearGradientBrush(rect, MintPrimary, MintDark, 90.0F) : g.FillPath(br, path) : End Using
                End Using
                TextRenderer.DrawText(g, b.Text, b.Font, rect, Color.White,
                    TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub
        AddHandler btn.MouseEnter,
            Sub()
                btn.BackColor = MintDark
                btn.Invalidate()
            End Sub
        AddHandler btn.MouseLeave,
            Sub()
                btn.BackColor = MintPrimary
                btn.Invalidate()
            End Sub
        Return btn
    End Function

    ' =========================================================================
    '  CARD HEADER FACTORY
    ' =========================================================================
    Private Shared Function MakeCardHeader(title As String, iconFile As String, fallbackIcon As String) As Panel
        Dim header As New BufferedPanel With {.Height = 60, .Dock = DockStyle.Top, .BackColor = Color.Transparent}

        Dim iconBox As New Panel With {
            .Size = New Size(36, 36), .BackColor = Color.FromArgb(232, 246, 241), .Location = New Point(18, 12)
        }
        AddHandler iconBox.Paint,
            Sub(s As Object, e As PaintEventArgs)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Using br As New SolidBrush(CType(s, Panel).BackColor) : e.Graphics.FillEllipse(br, 0, 0, 35, 35) : End Using
            End Sub

        Dim img As Image = GetIcon(iconFile)
        If img IsNot Nothing Then
            Dim pb As New PictureBox With {
                .Image = img, .SizeMode = PictureBoxSizeMode.Zoom, .Size = New Size(22, 22), .BackColor = Color.Transparent
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
    '  DRAWER DENOMINATIONS — structure is fixed; counts loaded from DB
    ' =========================================================================
    Private Shared Function GetDrawer() As List(Of Denom)
        Dim denoms As New List(Of Denom) From {
            New Denom With {.Value = 1000D, .Count = 0, .IsBill = True},
            New Denom With {.Value = 500D, .Count = 0, .IsBill = True},
            New Denom With {.Value = 200D, .Count = 0, .IsBill = True},
            New Denom With {.Value = 100D, .Count = 0, .IsBill = True},
            New Denom With {.Value = 50D, .Count = 0, .IsBill = True},
            New Denom With {.Value = 20D, .Count = 0, .IsBill = True},
            New Denom With {.Value = 20D, .Count = 0, .IsBill = False},
            New Denom With {.Value = 10D, .Count = 0, .IsBill = False},
            New Denom With {.Value = 5D, .Count = 0, .IsBill = False},
            New Denom With {.Value = 1D, .Count = 0, .IsBill = False},
            New Denom With {.Value = 0.25D, .Count = 0, .IsBill = False}
        }

        If _shiftId <= 0 Then Return denoms

        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT denomination, is_bill, qty FROM cash_counts WHERE shift_id=@sid", conn)
                    cmd.Parameters.AddWithValue("@sid", _shiftId)
                    Dim counts As New Dictionary(Of String, Integer)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            Dim key As String = rdr("denomination").ToString() & "_" & rdr("is_bill").ToString()
                            counts(key) = CInt(rdr("qty"))
                        End While
                    End Using
                    For Each d In denoms
                        Dim key As String = d.Value.ToString() & "_" & d.IsBill.ToString()
                        If counts.ContainsKey(key) Then d.Count = counts(key)
                    Next
                End Using
            End Using
        Catch
        End Try

        Return denoms
    End Function

End Class