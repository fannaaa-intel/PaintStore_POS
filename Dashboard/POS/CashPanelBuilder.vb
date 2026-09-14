Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports System.Linq
Imports MySql.Data.MySqlClient

Public Class CashPanelBuilder

    Private Shared ReadOnly MintDark As Color = Theme.Dark
    Private Shared ReadOnly MintPrimary As Color = Theme.Primary
    Private Shared ReadOnly MintBg As Color = Theme.PageBg
    Private Shared ReadOnly CardBg As Color = Color.White
    Private Shared ReadOnly CardBorder As Color = Color.FromArgb(220, 232, 228)
    Private Shared ReadOnly SubText As Color = Theme.SubText
    Private Shared ReadOnly TileBg As Color = Theme.TileBg
    Private Shared ReadOnly SoftBorder As Color = Theme.BorderMuted
    Private Shared ReadOnly TextDark As Color = Theme.TextDark
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
    ' Reset on every panel load so the "no shift" warning appears once per visit
    ' rather than on every single +/- tap.
    Private Shared _warnedNoShift As Boolean = False

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
    Private Shared _lblVarianceHint As Label

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Shared Sub LoadCash(mainContainer As Panel)
        _mainContainer = mainContainer
        _warnedNoShift = False
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

        ' With no open shift there is nothing to save counts against: every
        ' SaveDenomCount returns at its first line. The screen still let the
        ' cashier tap counts up and showed a running total, so the drawer looked
        ' counted right up until a refresh wiped it -- the work was never
        ' anywhere but on screen. Say so, loudly, instead of failing silently.
        If _shiftId <= 0 Then
            Dim warn As New Panel With {
                .Dock = DockStyle.Top, .Height = 46, .BackColor = Color.FromArgb(255, 244, 222)
            }
            Dim lblWarn As New Label With {
                .Text = "  ⚠   No open shift — counts on this screen are NOT being saved." &
                        "  Go to POS and press Continue on the Start Shift screen first.",
                .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleLeft,
                .Font = New Font("Segoe UI Semibold", 10),
                .ForeColor = Color.FromArgb(150, 90, 10), .BackColor = Color.Transparent
            }
            AddHandler warn.Paint,
                Sub(s, e)
                    Using p As New Pen(Color.FromArgb(226, 190, 120), 1)
                        e.Graphics.DrawRectangle(p, 0, 0, warn.Width - 1, warn.Height - 1)
                    End Using
                End Sub
            warn.Controls.Add(lblWarn)
            host.Controls.Add(warn)
            warn.BringToFront()
        End If

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

                ' Find the Cash payment method ID. Prefer the real type column
                ' (added by migrations.sql section 8) so 'GCash' is never counted
                ' as drawer cash; fall back to the old name match when the column
                ' is absent, keeping an un-migrated database working.
                Dim cashMethodId As Integer = 0
                Dim cashSql As String = If(PaymentMethods.HasBehaviourColumns(),
                    "SELECT method_id FROM payment_methods WHERE type = 'Cash' ORDER BY method_id LIMIT 1",
                    "SELECT method_id FROM payment_methods WHERE LOWER(name) LIKE '%cash%' AND LOWER(name) NOT LIKE '%gcash%' LIMIT 1")
                Using cmd As New MySqlCommand(cashSql, conn)
                    Dim result = cmd.ExecuteScalar()
                    If result IsNot Nothing Then cashMethodId = CInt(result)
                End Using

                ' Total cash sales for this shift.
                ' 'Partially Refunded' is included: that cash really did enter the
                ' drawer, and the refund is subtracted separately below. Counting
                ' only 'Completed' removed the takings but kept the refund, showing
                ' the cashier a shortfall that was not real.
                If cashMethodId > 0 Then
                    Using cmd As New MySqlCommand(
                        "SELECT COALESCE(SUM(total),0) FROM sales " &
                        "WHERE shift_id=@sid AND method_id=@mid " &
                        "AND status IN ('Completed','Partially Refunded')", conn)
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
        ' No shift means nothing to attach the count to. This used to return in
        ' silence, so the cashier counted a whole drawer that was only ever on
        ' screen and lost it at the next refresh. Warn once per panel load --
        ' every tap would be unusable, but saying nothing is worse.
        If _shiftId <= 0 Then
            If Not _warnedNoShift Then
                _warnedNoShift = True
                AppModal.Show("There is no open shift, so this count cannot be saved." & Environment.NewLine & Environment.NewLine &
                              "Open the POS screen and press Continue on the Start Shift screen, " &
                              "then come back and count the drawer.",
                              "No Open Shift", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            End If
            Return
        End If
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                ' Single statement instead of UPDATE-then-INSERT: the uq_count
                ' unique key (shift_id, denomination, is_bill) already exists, so
                ' the database resolves the conflict atomically. The old two-step
                ' form could have two saves both see "no row" and race to insert.
                Using cmd As New MySqlCommand(
                    "INSERT INTO cash_counts (shift_id, denomination, is_bill, qty) " &
                    "VALUES (@sid, @denom, @bill, @qty) " &
                    "ON DUPLICATE KEY UPDATE qty = VALUES(qty)", conn)
                    cmd.Parameters.AddWithValue("@sid", _shiftId)
                    cmd.Parameters.AddWithValue("@denom", d.Value)
                    cmd.Parameters.AddWithValue("@bill", d.IsBill)
                    cmd.Parameters.AddWithValue("@qty", d.Count)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        Catch ex As Exception
            ' This is a WRITE on the cash-reconciliation path. Swallowing it left
            ' the counted amount on screen while the database never received it,
            ' so the drawer silently reconciled against stale numbers. Tell the
            ' cashier instead -- a failed count they know about can be retyped.
            System.Diagnostics.Debug.WriteLine("SaveDenomCount error: " & ex.Message)
            AppModal.Show("That denomination count could not be saved:" & Environment.NewLine & Environment.NewLine &
                          ex.Message & Environment.NewLine & Environment.NewLine &
                          "Re-enter the count before closing the drawer.",
                          "Count Not Saved", MessageBoxButtons.OK, MessageBoxIcon.Warning)
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
            .Top = 60, .Left = 2, .Height = 34, .BackColor = Color.FromArgb(247, 250, 249),
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

        Dim rowList As New CardTableBody With {
            .Top = 94, .Left = 2, .EmptyText = "",
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top Or AnchorStyles.Bottom
        }
        AddHandler card.Resize,
            Sub()
                rowList.Width = card.Width - 4
                rowList.Height = card.Height - 94 - 53
            End Sub

        Dim SyncGutter As Action =
            Sub()
                Dim sbw As Integer = If(rowList.VerticalScroll.Visible, SystemInformation.VerticalScrollBarWidth, 0)
                colGrid.Padding = New Padding(18, 0, 18 + sbw, 0)
            End Sub
        AddHandler rowList.Layout, Sub() SyncGutter()
        AddHandler card.Resize, Sub() SyncGutter()

        ' Build the strip in the order it should read top-to-bottom, then add in
        ' reverse: with Dock=Top the control added last sits highest, so adding
        ' the reversed sequence yields the intended order. The previous version
        ' added forwards and then reversed the whole child list, which detached
        ' each header from its group -- COINS ended up labelling the bills.
        Dim ordered As New List(Of Control)
        ordered.Add(MakeSectionHeader("BILLS"))
        For Each d In _denoms.Where(Function(x) x.IsBill)
            ordered.Add(BuildDenomRow(d))
        Next
        ordered.Add(MakeSectionHeader("COINS"))
        For Each d In _denoms.Where(Function(x) Not x.IsBill)
            ordered.Add(BuildDenomRow(d))
        Next
        For i = ordered.Count - 1 To 0 Step -1
            rowList.Controls.Add(ordered(i))
        Next
        card.Controls.Add(rowList)

        ' Inset 1px with a rounded bottom so the card's corner + border survive.
        Dim footer As New CardFooterPanel With {
            .Height = 52, .Left = 2, .BackColor = Color.FromArgb(247, 250, 249),
            .Padding = New Padding(18, 0, 18, 0),
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Bottom
        }
        AddHandler card.Resize,
            Sub()
                footer.Width = card.Width - 4
                footer.Top = card.Height - footer.Height - 1
            End Sub

        Dim lblCaption As New Label With {
            .Text = "CASH COUNTED", .Font = New Font("Segoe UI Semibold", 10),
            .ForeColor = SubText, .AutoSize = False,
            .Dock = DockStyle.Left, .Width = 140,
            .TextAlign = ContentAlignment.MiddleLeft, .BackColor = Color.Transparent
        }
        ' Docked right and right-aligned rather than positioned by hand.
        '
        ' This label was AutoSize with its Location computed in footer.Resize as
        ' (footer.Width - label.Width - 18). Recalc() then sets the text, and an
        ' AutoSize label grows RIGHTWARD from its fixed Location -- so once the
        ' total reached a wider string like "P 1,341.25" it ran past the footer's
        ' rounded clipping region and the last digits were cut off. Nothing
        ' repositioned it, because a text change is not a resize of the footer.
        '
        ' Docking makes the width track the footer instead of the text, and
        ' MiddleRight keeps the value hard against the 18px padding at any
        ' length, so no amount can overflow.
        _lblCountFooter = New Label With {
            .Text = "₱ 0.00", .Font = New Font("Segoe UI", 16, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = False,
            .Dock = DockStyle.Fill,
            .TextAlign = ContentAlignment.MiddleRight, .BackColor = Color.Transparent,
            .AutoEllipsis = True
        }
        ' Fill must be added before Left so the docked Left strip is not
        ' overlapped by it -- with Dock, later-added controls sit closest to
        ' the centre of the remaining space.
        footer.Controls.Add(_lblCountFooter)
        footer.Controls.Add(lblCaption)
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
        '
        ' The count is TYPED, not tapped. Entering 47 one-peso coins through a
        ' +/- stepper is 47 clicks; the cashier has already counted them and
        ' just needs to write the number down. The buttons stay for small
        ' corrections (one more bill found in the till), which is what a
        ' stepper is actually good for.
        Dim stepWrap As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.Transparent}
        Dim txtQty As New TextBox With {
            .Text = d.Count.ToString(), .Size = New Size(52, 26),
            .TextAlign = HorizontalAlignment.Center,
            .Font = New Font("Segoe UI Semibold", 11), .ForeColor = MintDark,
            .BorderStyle = BorderStyle.FixedSingle, .BackColor = Color.White,
            .MaxLength = 4
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

        ' True while code is writing to the box, so the TextChanged handler can
        ' tell its own updates apart from the cashier's typing.
        Dim updating As Boolean = False

        Dim SyncRow As Action =
            Sub()
                updating = True
                txtQty.Text = d.Count.ToString()
                updating = False
                lblLine.Text = "₱ " & (d.Value * d.Count).ToString("N2")
                lblLine.ForeColor = If(d.Count > 0, MintDark, Color.FromArgb(170, 178, 175))
            End Sub

        ' Only the subtotal is refreshed while typing -- rewriting txtQty here
        ' would fight the cashier for the caret (typing "12" would land as "21"
        ' or reset to "1" mid-keystroke).
        Dim SyncLineOnly As Action =
            Sub()
                lblLine.Text = "₱ " & (d.Value * d.Count).ToString("N2")
                lblLine.ForeColor = If(d.Count > 0, MintDark, Color.FromArgb(170, 178, 175))
            End Sub

        ' Digits only. Silently refusing a letter is better than a modal on a
        ' screen where the cashier is typing eleven numbers in a row.
        AddHandler txtQty.KeyPress,
            Sub(s As Object, e As KeyPressEventArgs)
                If Not Char.IsControl(e.KeyChar) AndAlso Not Char.IsDigit(e.KeyChar) Then e.Handled = True
            End Sub

        ' Live total as they type: the running CASH COUNTED updates on each
        ' keystroke, but nothing is written to the database until they leave
        ' the field -- a save per keystroke would be a query per keystroke.
        AddHandler txtQty.TextChanged,
            Sub()
                If updating Then Return
                Dim n As Integer
                If Integer.TryParse(txtQty.Text.Trim(), n) AndAlso n >= 0 Then
                    d.Count = n
                ElseIf txtQty.Text.Trim() = "" Then
                    d.Count = 0
                End If
                SyncLineOnly()
                Recalc()
            End Sub

        ' Commit on leaving the field: normalise what is shown ("007" -> "7",
        ' empty -> "0") and persist once.
        AddHandler txtQty.Leave,
            Sub()
                Dim n As Integer
                If Not Integer.TryParse(txtQty.Text.Trim(), n) OrElse n < 0 Then n = 0
                d.Count = n
                SyncRow()
                Recalc()
                SaveDenomCount(d)
            End Sub

        ' Enter commits and moves on, so the whole drawer is entered from the
        ' keyboard without reaching for the mouse.
        AddHandler txtQty.KeyDown,
            Sub(s As Object, e As KeyEventArgs)
                If e.KeyCode = Keys.Enter Then
                    e.SuppressKeyPress = True
                    Dim frm = row.FindForm()
                    If frm IsNot Nothing Then frm.SelectNextControl(txtQty, True, True, True, True)
                End If
            End Sub

        ' Select-all on focus: typing then replaces the count instead of
        ' appending to it ("0" + "12" = "012").
        AddHandler txtQty.Enter, Sub() txtQty.SelectAll()
        AddHandler txtQty.Click, Sub() If txtQty.SelectionLength = 0 Then txtQty.SelectAll()

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
                Dim total As Integer = btnMinus.Width + txtQty.Width + btnPlus.Width
                Dim sx As Integer = (stepWrap.Width - total) \ 2
                Dim cy As Integer = (stepWrap.Height - 26) \ 2
                btnMinus.Location = New Point(sx, cy)
                txtQty.Location = New Point(sx + btnMinus.Width, cy)
                btnPlus.Location = New Point(sx + btnMinus.Width + txtQty.Width, cy)
            End Sub
        AddHandler stepWrap.Resize, Sub() layoutStepper()
        stepWrap.Controls.AddRange({btnMinus, txtQty, btnPlus})
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
        '
        ' Nine rows, not seven: PAY-INS and SAFE DROPS were part of the EXPECTED
        ' formula in Recalc but were never shown, so the figures on screen did
        ' not add up to the total printed beneath them. A cashier checking the
        ' arithmetic by hand would find money unaccounted for and no way to see
        ' where it went. Every term in the formula now has a visible row.
        Dim stats As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 9,
            .BackColor = Color.Transparent, .Padding = New Padding(16, 10, 16, 6)
        }
        stats.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 58.0F))
        stats.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 42.0F))
        For i = 0 To 5
            stats.RowStyles.Add(New RowStyle(SizeType.Percent, 12.5F))
        Next
        stats.RowStyles.Add(New RowStyle(SizeType.Absolute, 12))
        stats.RowStyles.Add(New RowStyle(SizeType.Percent, 12.5F))
        stats.RowStyles.Add(New RowStyle(SizeType.Percent, 12.5F))

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

        ' Use real DB values. Order follows the money: what we started with,
        ' what came in, then what went out -- matching the EXPECTED formula in
        ' Recalc term for term so the column can be checked by hand.
        AddStat(0, "OPENING FLOAT", "₱ " & _openingFloat.ToString("N2"), False, Color.FromArgb(50, 70, 65))
        AddStat(1, "+ CASH SALES", "₱ " & _cashSales.ToString("N2"), False, Color.FromArgb(0, 150, 122))
        AddStat(2, "+ PAY-INS", "₱ " & _cashPaidIn.ToString("N2"), False, Color.FromArgb(0, 150, 122))
        AddStat(3, "− CASH PAID OUT", "₱ " & _cashPaidOut.ToString("N2"), False, DangerFore)
        AddStat(4, "− REFUNDS", "₱ " & _cashRefunds.ToString("N2"), False, DangerFore)
        AddStat(5, "− SAFE DROPS", "₱ " & _cashDropped.ToString("N2"), False, DangerFore)

        Dim div As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.Transparent}
        AddHandler div.Paint,
            Sub(s, e)
                Using pen As New Pen(CardBorder, 1.5F) : e.Graphics.DrawLine(pen, 0, div.Height \ 2, div.Width, div.Height \ 2) : End Using
            End Sub
        stats.SetColumnSpan(div, 2)
        stats.Controls.Add(div, 0, 6)

        Dim lblExpL As New Label With {
            .Text = "EXPECTED IN DRAWER", .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleLeft,
            .Font = New Font("Segoe UI Semibold", 10.5F), .ForeColor = MintDark, .BackColor = Color.Transparent
        }
        _lblExpected = New Label With {
            .Text = "₱ 0.00", .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleRight,
            .Font = New Font("Segoe UI Semibold", 11, FontStyle.Bold), .ForeColor = MintDark, .BackColor = Color.Transparent
        }
        stats.Controls.Add(lblExpL, 0, 7)
        stats.Controls.Add(_lblExpected, 1, 7)

        Dim lblCntL As New Label With {
            .Text = "CASH COUNTED", .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleLeft,
            .Font = New Font("Segoe UI Semibold", 10.5F), .ForeColor = MintDark, .BackColor = Color.Transparent
        }
        _lblCounted = New Label With {
            .Text = "₱ 0.00", .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleRight,
            .Font = New Font("Segoe UI Semibold", 11, FontStyle.Bold), .ForeColor = MintDark, .BackColor = Color.Transparent
        }
        stats.Controls.Add(lblCntL, 0, 8)
        stats.Controls.Add(_lblCounted, 1, 8)

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
        ' Two rows: the figure, then a plain-language reading of it underneath.
        ' "OVER / SHORT: -50.00" tells a trained cashier plenty and a new one
        ' nothing, so the second row says what it means in words.
        Dim tl As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 2, .BackColor = Color.Transparent
        }
        tl.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 42.0F))
        tl.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 58.0F))
        tl.RowStyles.Add(New RowStyle(SizeType.Percent, 62.0F))
        tl.RowStyles.Add(New RowStyle(SizeType.Percent, 38.0F))
        Dim lblVL As New Label With {
            .Text = "OVER / SHORT", .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleLeft,
            .Font = New Font("Segoe UI", 12, FontStyle.Bold), .ForeColor = Color.FromArgb(200, 240, 225),
            .BackColor = Color.Transparent, .Padding = New Padding(14, 0, 0, 0)
        }
        _lblVariance = New Label With {
            .Text = "₱ 0.00", .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleRight,
            .Font = New Font("Segoe UI", 18, FontStyle.Bold), .ForeColor = OkGreen,
            .BackColor = Color.Transparent, .Padding = New Padding(0, 0, 14, 0),
            .AutoEllipsis = True
        }
        _lblVarianceHint = New Label With {
            .Text = "", .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleRight,
            .Font = New Font("Segoe UI", 8.5F), .ForeColor = Color.FromArgb(170, 205, 195),
            .BackColor = Color.Transparent, .Padding = New Padding(14, 0, 14, 0),
            .AutoEllipsis = True
        }
        tl.Controls.Add(lblVL, 0, 0)
        tl.Controls.Add(_lblVariance, 1, 0)
        tl.SetColumnSpan(_lblVarianceHint, 2)
        tl.Controls.Add(_lblVarianceHint, 0, 1)
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

        Dim hint As String
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

        ' Say what the number means, in the words a cashier would use.
        If counted = 0D Then
            hint = "Count the drawer to see if it balances"
        ElseIf variance = 0D Then
            hint = "Drawer balances — safe to close"
        ElseIf variance > 0D Then
            hint = "₱ " & variance.ToString("N2") & " MORE in the drawer than expected"
        Else
            hint = "₱ " & Math.Abs(variance).ToString("N2") & " MISSING from the drawer"
        End If
        If _lblVarianceHint IsNot Nothing Then _lblVarianceHint.Text = hint
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

            ' Pay-ins, pay-outs and drops all move cash without a sale behind
            ' them, which is exactly the kind of movement an audit checks.
            ActivityLog.Record("CASH_" & movementType.ToUpper(), "shift", _shiftId,
                ChrW(8369) & res.amount.ToString("N2") &
                If(String.IsNullOrWhiteSpace(res.note), "", " — " & res.note))

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

        ' Commit whatever is in the focused count box first.
        '
        ' Counts persist on Leave, but clicking Close Drawer straight from a box
        ' the cashier just typed into does not always raise Leave before the
        ' click is handled. The typed value is in _denoms either way (TextChanged
        ' keeps it live), so the close itself is correct -- but cash_counts would
        ' miss that last denomination, and the saved breakdown would then not add
        ' up to the counted_cash recorded beside it.
        If _mainContainer IsNot Nothing Then
            Dim frm = _mainContainer.FindForm()
            If frm IsNot Nothing AndAlso TypeOf frm.ActiveControl Is TextBox Then
                frm.ActiveControl = Nothing
            End If
        End If

        ' Re-read the shift figures before computing what gets written.
        '
        ' The panel loads these once and only refreshes on navigation or the
        ' Refresh button, so a drawer screen left open while sales continued
        ' elsewhere held a stale _cashSales. Closing from that screen wrote the
        ' stale expected -- and its variance -- into the shift row permanently,
        ' inventing a shortage the cashier could not account for. The figures
        ' are only trustworthy as of this moment, so read them at this moment.
        LoadShiftData()
        If _shiftId <= 0 Then
            AppModal.Show("This shift is no longer open. It may have been closed elsewhere.",
                            "Close Drawer", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            If _mainContainer IsNot Nothing Then LoadCash(_mainContainer)
            Return
        End If

        Dim counted As Decimal = If(_denoms Is Nothing, 0D, _denoms.Sum(Function(d) d.Value * d.Count))
        Dim expected As Decimal = _openingFloat + _cashSales + _cashPaidIn _
                                  - _cashPaidOut - _cashRefunds - _cashDropped
        Dim variance As Decimal = counted - expected

        ' Closing on an uncounted drawer is almost always a forgotten count
        ' rather than a genuinely empty till, and the resulting variance is
        ' recorded permanently. Make them say so deliberately.
        If counted = 0D AndAlso expected <> 0D Then
            If AppModal.Show(
                "No cash has been counted, but ₱ " & expected.ToString("N2") & " is expected in the drawer." & vbCrLf & vbCrLf &
                "Closing now records a shortage of ₱ " & Math.Abs(variance).ToString("N2") & " against your name." & vbCrLf & vbCrLf &
                "Count the drawer first?",
                "Drawer Not Counted", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) = DialogResult.Yes Then Return
        End If

        Dim varianceLine As String
        If variance = 0D Then
            varianceLine = "  Variance:  ₱ 0.00  (balanced)"
        ElseIf variance > 0D Then
            varianceLine = "  Variance:  ₱ " & variance.ToString("N2") & "  (OVER — more cash than expected)"
        Else
            varianceLine = "  Variance:  ₱ " & variance.ToString("N2") & "  (SHORT — cash missing)"
        End If

        Dim msg As String =
            "Close this shift?" & vbCrLf & vbCrLf &
            "  Expected:  ₱ " & expected.ToString("N2") & vbCrLf &
            "  Counted:   ₱ " & counted.ToString("N2") & vbCrLf &
            varianceLine & vbCrLf & vbCrLf &
            "Once closed, this shift can no longer record cash."
        If AppModal.Show(msg, "Close Drawer", MessageBoxButtons.YesNo,
                           MessageBoxIcon.Question) <> DialogResult.Yes Then Return

        Try
            Using conn = Db.GetConnection()
                conn.Open()
                ' AND status='Open' makes the close idempotent. Without it a
                ' second close (another window, or a stale screen) overwrote
                ' ended_at and the recorded variance of a shift already closed.
                Dim affected As Integer
                Using cmd As New MySqlCommand(
                    "UPDATE shifts SET ended_at=NOW(), expected_cash=@exp, " &
                    "counted_cash=@cnt, variance=@var, status='Closed' " &
                    "WHERE shift_id=@sid AND status='Open'", conn)
                    cmd.Parameters.AddWithValue("@exp", expected)
                    cmd.Parameters.AddWithValue("@cnt", counted)
                    cmd.Parameters.AddWithValue("@var", variance)
                    cmd.Parameters.AddWithValue("@sid", _shiftId)
                    affected = cmd.ExecuteNonQuery()
                End Using
                ' A counted drawer that disagrees with expected cash is the
                ' headline number of any cash audit, so the variance is recorded
                ' with the close rather than left only on the shift row.
                If affected > 0 Then
                    ActivityLog.Record("SHIFT_CLOSE", "shift", _shiftId,
                        "Expected " & ChrW(8369) & expected.ToString("N2") &
                        ", counted " & ChrW(8369) & counted.ToString("N2") &
                        ", variance " & ChrW(8369) & variance.ToString("N2"))
                End If

                If affected = 0 Then
                    AppModal.Show("This shift has already been closed." & vbCrLf &
                                  "Its recorded count was left untouched.",
                                  "Already Closed", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    If _mainContainer IsNot Nothing Then LoadCash(_mainContainer)
                    Return
                End If
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
        lines.Add(New ReceiptPrinter.RLine(StoreSettings.StoreName, , ReceiptPrinter.LineStyle.Title))
        lines.Add(New ReceiptPrinter.RLine("Cash Drawer Count", , ReceiptPrinter.LineStyle.Muted))
        lines.Add(ReceiptPrinter.Divider())
        lines.Add(New ReceiptPrinter.RLine("Session:", If(_shiftId > 0, "DR-" & _shiftId.ToString("D4"), "—")))
        lines.Add(New ReceiptPrinter.RLine("Cashier:", Session.CurrentFullName))
        lines.Add(New ReceiptPrinter.RLine("Printed:", DateTime.Now.ToString("MMM d, yyyy h:mm tt")))
        lines.Add(ReceiptPrinter.Divider())

        If _denoms IsNot Nothing Then
            ' Bills before coins, each group high to low -- and each line says
            ' which it is. There is both a 20-peso bill and a 20-peso coin, so
            ' without the suffix the printout showed two identical "P 20.00"
            ' rows and the manager checking it could not tell them apart.
            For Each d In _denoms.Where(Function(x) x.Count > 0) _
                                 .OrderByDescending(Function(x) x.IsBill) _
                                 .ThenByDescending(Function(x) x.Value)
                lines.Add(New ReceiptPrinter.RLine(
                    "₱ " & d.Value.ToString("N2") & If(d.IsBill, " bill", " coin") & " × " & d.Count.ToString(),
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

            ' The TryParse result was discarded, so unparseable text silently
            ' became 0 and was returned as a success. "abc" was caught later by
            ' the caller's <= 0 check, but a typo like "50x" or a stray keystroke
            ' produced a confidently wrong amount with no complaint. Reject text
            ' that is not a number rather than guessing what it meant.
            Dim amt As Decimal = 0
            Dim raw As String = txtAmt.Text.Replace("₱", "").Replace(",", "").Trim()
            If Not Decimal.TryParse(raw, amt) Then
                AppModal.Show("'" & txtAmt.Text.Trim() & "' is not a valid amount." & Environment.NewLine &
                              "Enter digits only, for example 500.00",
                              title, MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return (False, 0D, "")
            End If
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

                    ' Keyed on the TYPED pair, not on formatted strings.
                    '
                    ' This previously built the key with .ToString() on both
                    ' sides, which never matched: denomination is decimal(7,2),
                    ' so MySQL returns "1000.00" while the in-memory literal
                    ' 1000D formats as "1000". Every whole-peso denomination
                    ' failed to load, so a saved count silently came back as
                    ' zero on every refresh -- and CloseDrawer would then record
                    ' counted=0 against a real expected figure, writing a fake
                    ' shortage into the shift's permanent audit record.
                    ' is_bill was equally fragile: "True" only if the driver
                    ' hands back a Boolean, "1" if it hands back the raw tinyint.
                    ' A Decimal/Boolean tuple compares by value and sidesteps
                    ' both formatting questions entirely.
                    Dim counts As New Dictionary(Of Tuple(Of Decimal, Boolean), Integer)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            Dim denom As Decimal = Convert.ToDecimal(rdr("denomination"))
                            Dim isBill As Boolean = Convert.ToBoolean(rdr("is_bill"))
                            counts(Tuple.Create(denom, isBill)) = Convert.ToInt32(rdr("qty"))
                        End While
                    End Using
                    For Each d In denoms
                        Dim qty As Integer
                        If counts.TryGetValue(Tuple.Create(d.Value, d.IsBill), qty) Then d.Count = qty
                    Next
                End Using
            End Using
        Catch ex As Exception
            ' A failure here means the on-screen counts silently disagree with
            ' what the drawer actually holds, which is the one thing this screen
            ' exists to get right. Surface it rather than showing a zeroed drawer
            ' that looks like a legitimate count.
            System.Diagnostics.Debug.WriteLine("GetDrawer error: " & ex.Message)
            AppModal.Show("The saved denomination counts could not be loaded:" & Environment.NewLine & Environment.NewLine &
                          ex.Message & Environment.NewLine & Environment.NewLine &
                          "The drawer is showing zero for every denomination. Re-count before closing the drawer.",
                          "Counts Not Loaded", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End Try

        Return denoms
    End Function

End Class