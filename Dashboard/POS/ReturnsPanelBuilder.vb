Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports System.Linq
Imports MySql.Data.MySqlClient

' =============================================================================
'  ReturnsPanelBuilder — Returns / Refund screen (DB-connected)
' =============================================================================
Public Class ReturnsPanelBuilder

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
    Private Shared ReadOnly RefundGold As Color = Color.FromArgb(255, 196, 120)
    Private Shared ReadOnly RefundAmber As Color = Color.FromArgb(196, 118, 22)

    Private Shared ReadOnly IconBasePath As String = Application.StartupPath & "\"
    Private Shared ReadOnly IconCache As New Dictionary(Of String, Image)

    Private Shared Function GetIcon(fileName As String) As Image
        Dim path As String = IconBasePath & fileName
        If String.IsNullOrEmpty(path) OrElse Not IO.File.Exists(path) Then Return Nothing
        If Not IconCache.ContainsKey(path) Then
            Using fs As New IO.FileStream(path, IO.FileMode.Open, IO.FileAccess.Read)
                IconCache(path) = Image.FromStream(fs)
            End Using
        End If
        Return IconCache(path)
    End Function

    ' ── DATA MODEL ─────────────────────────────────────────────────────────────
    Public Class ReturnItem
        Public Property Sku As String
        Public Property Name As String
        Public Property Swatch As Color
        Public Property UnitPrice As Decimal
        Public Property Qty As Integer       ' originally purchased
        Public Property SelectedQty As Integer
        Public Property SaleItemId As Integer  ' FK → sale_items.sale_item_id
        Public Property ProductId As Integer   ' FK → products.product_id
    End Class

    ' ── LIVE STATE ─────────────────────────────────────────────────────────────
    Private Shared _items As List(Of ReturnItem)
    Private Shared _lblItems As Label
    Private Shared _lblSubtotal As Label
    Private Shared _lblFee As Label
    Private Shared _lblTotal As Label
    Private Shared _restockPct As Decimal = 0D
    ' DB-session refs
    Private Shared _mainContainer As Panel
    Private Shared _saleId As Integer = 0
    Private Shared _saleNo As String = ""
    Private Shared _rowsHost As CardTableBody
    Private Shared _lblLoaded As Label
    Private Shared _selectedRefundMethod As String = "Cash"
    Private Shared _cmbReason As ComboBox
    Private Shared _activeMethodBtn As Button
    ' Snapshot of the most recent processed refund, for PRINT SLIP.
    Private Shared _lastRtnNo As String = ""
    Private Shared _lastRtnSaleNo As String = ""
    Private Shared _lastRtnMethod As String = ""
    Private Shared _lastRtnReason As String = ""
    Private Shared _lastRtnFeePct As Decimal = 0
    Private Shared _lastRtnSubtotal As Decimal = 0
    Private Shared _lastRtnFee As Decimal = 0
    Private Shared _lastRtnTotal As Decimal = 0
    Private Shared _lastRtnAt As DateTime = DateTime.MinValue
    Private Shared _lastRtnLines As New List(Of (name As String, qty As Integer, amt As Decimal))

    ' =========================================================================
    '  SESSION RESET
    ' =========================================================================
    ' The refund snapshot behind PRINT SLIP is Shared and outlives the form, so
    ' without this the next cashier could reprint the previous cashier's refund
    ' slip -- stamped with their own name as the processor.
    Public Shared Sub ResetForNewSession()
        _saleId = 0
        _saleNo = ""
        _selectedRefundMethod = "Cash"
        _items = New List(Of ReturnItem)

        _lastRtnNo = ""
        _lastRtnSaleNo = ""
        _lastRtnMethod = ""
        _lastRtnReason = ""
        _lastRtnFeePct = 0
        _lastRtnSubtotal = 0
        _lastRtnFee = 0
        _lastRtnTotal = 0
        _lastRtnAt = DateTime.MinValue
        _lastRtnLines.Clear()
    End Sub

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Shared Sub LoadReturns(mainContainer As Panel)
        _mainContainer = mainContainer
        _saleId = 0
        _saleNo = ""
        _selectedRefundMethod = "Cash"
        _activeMethodBtn = Nothing

        mainContainer.Controls.Clear()
        mainContainer.BackColor = MintBg

        Dim root As New BufferedPanel With {.Dock = DockStyle.Fill, .BackColor = MintBg}
        mainContainer.Controls.Add(root)
        root.BringToFront()

        BuildLayout(root)
        AddHandler root.Resize, Sub() root.Invalidate(True)
    End Sub

    Private Shared Sub BuildLayout(host As Panel)
        _items = New List(Of ReturnItem)()   ' start empty — no fake data

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
        Dim methodBar As Control = BuildMethodBar()
        Dim actionBar As Control = BuildActionBar()

        topBar.Dock = DockStyle.Fill
        content.Dock = DockStyle.Fill
        methodBar.Dock = DockStyle.Fill
        actionBar.Dock = DockStyle.Fill

        outer.Controls.Add(topBar, 0, 0)
        outer.Controls.Add(content, 0, 1)
        outer.Controls.Add(methodBar, 0, 2)
        outer.Controls.Add(actionBar, 0, 3)

        Recalc()
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
            .Text = "RETURNS / REFUND", .Font = New Font("Segoe UI Semibold", 13, FontStyle.Bold),
            .ForeColor = Color.White, .BackColor = Color.Transparent, .AutoSize = True, .Location = New Point(18, 9)
        }
        Dim lblSub As New Label With {
            .Text = "CASHIER: " & Session.CurrentFullName.ToUpper(),
            .Font = New Font("Segoe UI", 9), .ForeColor = Color.FromArgb(160, 215, 200),
            .BackColor = Color.Transparent, .AutoSize = True, .Location = New Point(20, 36)
        }

        Dim lblDateTime As New Label With {
            .Text = DateTime.Now.ToString("MMM dd, yyyy  |  hh:mm tt"),
            .Font = New Font("Segoe UI", 10), .ForeColor = Color.FromArgb(200, 240, 225),
            .BackColor = Color.Transparent, .AutoSize = True
        }
        Dim timer As New Timer With {.Interval = 1000}
        AddHandler timer.Tick,
            Sub()
                If Not lblDateTime.IsDisposed Then
                    lblDateTime.Text = DateTime.Now.ToString("MMM dd, yyyy  |  hh:mm tt")
                    lblDateTime.Location = New Point(bar.Width - lblDateTime.Width - 18, (bar.Height - lblDateTime.Height) \ 2)
                Else
                    timer.Stop() : timer.Dispose()
                End If
            End Sub
        timer.Start()

        AddHandler bar.Disposed,
            Sub()
                timer.Stop()
                timer.Dispose()
            End Sub

        ' ── Refresh button ──────────────────────────────────────────────────
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
                If _mainContainer IsNot Nothing Then LoadReturns(_mainContainer)
            End Sub

        bar.Controls.AddRange({lblTitle, lblSub, lblDateTime, btnRefresh})


        AddHandler bar.Resize,
               Sub()
                   lblDateTime.Location = New Point(bar.Width - lblDateTime.Width - 18, (bar.Height - lblDateTime.Height) \ 2)
                   btnRefresh.Left = lblDateTime.Left - btnRefresh.Width - 12
                   btnRefresh.Top = (bar.Height - btnRefresh.Height) \ 2
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
        split.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 68.0F))
        split.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 32.0F))
        split.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))

        Dim left As Control = BuildItemsCard()
        Dim right As Control = BuildSummaryCard()
        left.Dock = DockStyle.Fill
        right.Dock = DockStyle.Fill

        split.Controls.Add(left, 0, 0)
        split.Controls.Add(right, 1, 0)
        Return split
    End Function

    ' =========================================================================
    '  LEFT — RETURNABLE ITEMS CARD
    ' =========================================================================
    Private Shared Function BuildItemsCard() As Control
        Dim card As New BufferedPanel With {
            .Dock = DockStyle.Fill, .BackColor = CardBg, .Margin = New Padding(0, 0, 8, 0)
        }
        AddHandler card.Paint,
            Sub(s As Object, e As PaintEventArgs) PanelRenderer.Draw(CType(s, Panel), e, True)

        Dim header = MakeCardHeader("Returnable Items", "recent.png", "↩")
        card.Controls.Add(header)
        card.Controls.SetChildIndex(header, 0)

        ' ── Receipt lookup row ──
        Dim lookupRow As New Panel With {
            .Top = 60, .Left = 0, .Height = 52, .BackColor = Color.Transparent,
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        }
        AddHandler card.Resize, Sub() lookupRow.Width = card.Width

        Dim searchWrap As New Panel With {
            .Size = New Size(280, 34), .Location = New Point(18, 9),
            .BackColor = TileBg, .Cursor = Cursors.IBeam
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

        Dim txtReceipt As New TextBox With {
            .Font = New Font("Segoe UI", 10), .BorderStyle = BorderStyle.None,
            .BackColor = TileBg, .ForeColor = SubText,
            .Text = "Enter receipt #  (e.g. TXN-0471)",
            .Location = New Point(34, 9), .Width = 232
        }
        AddHandler txtReceipt.Enter,
            Sub()
                searchFocused = True
                If txtReceipt.Text.StartsWith("Enter receipt") Then txtReceipt.Text = "" : txtReceipt.ForeColor = TextDark
                searchWrap.Invalidate()
            End Sub
        AddHandler txtReceipt.Leave,
            Sub()
                searchFocused = False
                If String.IsNullOrWhiteSpace(txtReceipt.Text) Then
                    txtReceipt.Text = "Enter receipt #  (e.g. TXN-0471)" : txtReceipt.ForeColor = SubText
                End If
                searchWrap.Invalidate()
            End Sub
        AddHandler txtReceipt.KeyDown,
            Sub(s, e)
                If CType(e, KeyEventArgs).KeyCode = Keys.Enter Then
                    Dim raw = txtReceipt.Text.TrimStart("#"c).Trim()
                    If Not String.IsNullOrWhiteSpace(raw) Then LookupReceipt(raw)
                End If
            End Sub
        AddHandler searchWrap.Click, Sub() txtReceipt.Focus()
        searchWrap.Controls.Add(txtReceipt)

        ' Loaded-receipt badge
        _lblLoaded = New Label With {
            .Text = "Enter a receipt number and click Look up",
            .Font = New Font("Segoe UI", 9.5F), .ForeColor = SubText,
            .AutoSize = True, .Anchor = AnchorStyles.Right Or AnchorStyles.Top
        }
        AddHandler lookupRow.Resize,
            Sub() _lblLoaded.Location = New Point(lookupRow.Width - _lblLoaded.Width - 18, 16)

        Dim btnLookup As New Button With {
            .Text = "Look up", .FlatStyle = FlatStyle.Flat,
            .Font = New Font("Segoe UI Semibold", 9.5F), .ForeColor = Color.White,
            .BackColor = MintPrimary, .Size = New Size(92, 34),
            .Location = New Point(306, 9), .Cursor = Cursors.Hand
        }
        btnLookup.FlatAppearance.BorderSize = 0
        AddHandler btnLookup.MouseEnter, Sub() btnLookup.BackColor = MintDark
        AddHandler btnLookup.MouseLeave, Sub() btnLookup.BackColor = MintPrimary
        AddHandler btnLookup.Click,
            Sub()
                Dim raw = txtReceipt.Text.TrimStart("#"c).Trim()
                If String.IsNullOrWhiteSpace(raw) OrElse raw.StartsWith("Enter receipt") Then
                    AppModal.Show("Please enter a receipt number first.", "No Receipt", MessageBoxButtons.OK, MessageBoxIcon.Information)
                    Return
                End If
                LookupReceipt(raw)
            End Sub

        lookupRow.Controls.AddRange({searchWrap, btnLookup, _lblLoaded})
        card.Controls.Add(lookupRow)

        ' ── Column headers ──
        Dim colHeader As New Panel With {
            .Top = 116, .Left = 2, .Height = 36, .BackColor = Color.FromArgb(247, 250, 249),
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
            .Dock = DockStyle.Fill, .ColumnCount = 5, .RowCount = 1,
            .BackColor = Color.Transparent, .Padding = New Padding(18, 0, 18, 0)
        }
        colGrid.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
        colGrid.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 54))
        colGrid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        colGrid.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 100))
        colGrid.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 132))
        colGrid.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 110))

        Dim hdrTxt() As String = {"", "Product", "Unit Price", "Return Qty", "Refund"}
        Dim hdrAl() As ContentAlignment = {
            ContentAlignment.MiddleLeft, ContentAlignment.MiddleLeft,
            ContentAlignment.MiddleRight, ContentAlignment.MiddleCenter, ContentAlignment.MiddleRight
        }
        For i = 0 To hdrTxt.Length - 1
            colGrid.Controls.Add(New Label With {
                .Text = hdrTxt(i), .Dock = DockStyle.Fill, .TextAlign = hdrAl(i),
                .Font = New Font("Segoe UI Semibold", 10),
                .ForeColor = Color.FromArgb(122, 152, 144), .BackColor = Color.Transparent
            }, i, 0)
        Next
        colHeader.Controls.Add(colGrid)
        card.Controls.Add(colHeader)

        ' ── Scrollable row list ──
        Dim rowList As New CardTableBody With {
            .Top = 152, .Left = 2, .EmptyText = "",
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top Or AnchorStyles.Bottom
        }
        AddHandler card.Resize,
            Sub()
                rowList.Width = card.Width - 4
                rowList.Height = card.Height - 152 - 41
            End Sub

        Dim SyncGutter As Action =
            Sub()
                Dim sbw As Integer = If(rowList.VerticalScroll.Visible, SystemInformation.VerticalScrollBarWidth, 0)
                colGrid.Padding = New Padding(18, 0, 18 + sbw, 0)
            End Sub
        AddHandler rowList.Layout, Sub() SyncGutter()
        AddHandler card.Resize, Sub() SyncGutter()

        _rowsHost = rowList

        ' Show placeholder initially
        rowList.Controls.Add(MakeEmptyPlaceholder("Look up a receipt to see returnable items."))

        card.Controls.Add(rowList)

        ' ── Footer ──
        ' Inset 1px with a rounded bottom so the card's corner + border survive.
        Dim footer As New CardFooterPanel With {
            .Height = 40, .Left = 2, .BackColor = Color.White,
            .Padding = New Padding(18, 0, 18, 0),
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Bottom
        }
        AddHandler card.Resize,
            Sub()
                footer.Width = card.Width - 4
                footer.Top = card.Height - footer.Height - 1
            End Sub
        ' Inset + vertically centred; at x=0 / fixed y the text was clipped by
        ' the footer's rounded corner.
        Dim lblHint As New Label With {
            .Text = "Tick an item and set the quantity to include it in the refund.",
            .Font = New Font("Segoe UI", 9.5F), .ForeColor = SubText,
            .AutoSize = True
        }
        AddHandler footer.Resize,
            Sub() lblHint.Location = New Point(18, (footer.Height - lblHint.Height) \ 2)
        footer.Controls.Add(lblHint)
        card.Controls.Add(footer)

        Return card
    End Function

    ' ── Empty-state placeholder ───────────────────────────────────────────────
    Private Shared Function MakeEmptyPlaceholder(msg As String) As Panel
        Dim ph As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.White}
        ph.Controls.Add(New Label With {
            .Text = msg, .Font = New Font("Segoe UI", 10), .ForeColor = SubText,
            .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleCenter,
            .BackColor = Color.White
        })
        Return ph
    End Function

    ' ── Single returnable row ─────────────────────────────────────────────────
    Private Shared Function BuildReturnRow(it As ReturnItem) As Panel
        Dim row As New Panel With {
            .Height = 52, .Dock = DockStyle.Top, .BackColor = Color.White,
            .Margin = New Padding(0), .Padding = New Padding(18, 0, 18, 0)
        }
        AddHandler row.Paint,
            Sub(s, e)
                Using p As New Pen(Color.FromArgb(237, 243, 242), 1)
                    e.Graphics.DrawLine(p, 0, row.Height - 1, row.Width, row.Height - 1)
                End Using
            End Sub

        Dim grid As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .BackColor = Color.Transparent,
            .ColumnCount = 5, .RowCount = 1, .Margin = New Padding(0)
        }
        grid.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 54))
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 100))
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 132))
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 110))

        ' (0) Checkbox
        Dim selWrap As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.Transparent}
        Dim chk As New Panel With {.Size = New Size(20, 20), .BackColor = Color.Transparent, .Cursor = Cursors.Hand}
        Dim isChecked As Boolean = False
        AddHandler chk.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim r As New Rectangle(0, 0, 19, 19)
                If isChecked Then
                    Using b As New SolidBrush(MintPrimary) : e.Graphics.FillRoundedRectangle(b, r, 5) : End Using
                    Using p As New Pen(Color.White, 2) With {.StartCap = LineCap.Round, .EndCap = LineCap.Round}
                        e.Graphics.DrawLines(p, {New Point(5, 10), New Point(8, 13), New Point(14, 6)})
                    End Using
                Else
                    Using b As New SolidBrush(TileBg) : e.Graphics.FillRoundedRectangle(b, r, 5) : End Using
                    Using p As New Pen(SoftBorder, 1.3F) : e.Graphics.DrawRoundedRectangle(p, r, 5) : End Using
                End If
            End Sub
        AddHandler selWrap.Resize, Sub() chk.Location = New Point(2, (selWrap.Height - chk.Height) \ 2)
        selWrap.Controls.Add(chk)
        grid.Controls.Add(selWrap, 0, 0)

        ' (1) Product + swatch
        Dim prodCell As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.Transparent}
        Dim dot As New Panel With {.Size = New Size(11, 11), .BackColor = it.Swatch}
        AddHandler dot.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Using br As New SolidBrush(it.Swatch) : e.Graphics.FillEllipse(br, dot.ClientRectangle) : End Using
                Using pn As New Pen(Color.FromArgb(180, 200, 196), 0.8F) : e.Graphics.DrawEllipse(pn, dot.ClientRectangle) : End Using
            End Sub
        AddHandler prodCell.Resize, Sub() dot.Location = New Point(0, (prodCell.Height - 11) \ 2)
        Dim lblProd As New Label With {
            .Text = it.Name & "  ·  returnable " & it.Qty, .Dock = DockStyle.Fill,
            .TextAlign = ContentAlignment.MiddleLeft, .Font = New Font("Segoe UI", 10),
            .ForeColor = TextDark, .BackColor = Color.Transparent, .Padding = New Padding(18, 0, 0, 0)
        }
        prodCell.Controls.Add(dot)
        prodCell.Controls.Add(lblProd)
        grid.Controls.Add(prodCell, 1, 0)

        ' (2) Unit price
        grid.Controls.Add(New Label With {
            .Text = "₱ " & it.UnitPrice.ToString("N2"), .Dock = DockStyle.Fill,
            .TextAlign = ContentAlignment.MiddleRight, .Font = New Font("Segoe UI", 10),
            .ForeColor = SubText, .BackColor = Color.Transparent
        }, 2, 0)

        ' (3) Qty stepper
        Dim stepWrap As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.Transparent}
        Dim lblQty As New Label With {
            .Text = "0", .Size = New Size(34, 24), .TextAlign = ContentAlignment.MiddleCenter,
            .Font = New Font("Segoe UI Semibold", 10), .ForeColor = MintDark, .BackColor = Color.Transparent
        }
        Dim btnMinus As New Button With {.Text = "−", .Size = New Size(26, 24), .FlatStyle = FlatStyle.Flat,
            .Font = New Font("Segoe UI", 11, FontStyle.Bold), .ForeColor = SubText, .BackColor = TileBg, .Cursor = Cursors.Hand}
        Dim btnPlus As New Button With {.Text = "+", .Size = New Size(26, 24), .FlatStyle = FlatStyle.Flat,
            .Font = New Font("Segoe UI", 11, FontStyle.Bold), .ForeColor = MintPrimary, .BackColor = TileBg, .Cursor = Cursors.Hand}
        btnMinus.FlatAppearance.BorderColor = SoftBorder
        btnPlus.FlatAppearance.BorderColor = SoftBorder

        ' (4) Line refund
        Dim lblRefund As New Label With {
            .Text = "₱ 0", .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleRight,
            .Font = New Font("Segoe UI Semibold", 10), .ForeColor = RefundAmber, .BackColor = Color.Transparent
        }

        Dim SyncRow As Action =
            Sub()
                lblQty.Text = it.SelectedQty.ToString()
                lblRefund.Text = "₱ " & (it.SelectedQty * it.UnitPrice).ToString("N2")
                lblRefund.ForeColor = If(it.SelectedQty > 0, RefundAmber, Color.FromArgb(170, 178, 175))
                isChecked = (it.SelectedQty > 0)
                chk.Invalidate()
            End Sub

        AddHandler chk.Click,
            Sub()
                it.SelectedQty = If(it.SelectedQty > 0, 0, it.Qty)
                SyncRow() : Recalc()
            End Sub
        AddHandler btnMinus.Click,
            Sub()
                If it.SelectedQty > 0 Then it.SelectedQty -= 1
                SyncRow() : Recalc()
            End Sub
        AddHandler btnPlus.Click,
            Sub()
                If it.SelectedQty < it.Qty Then it.SelectedQty += 1
                SyncRow() : Recalc()
            End Sub

        Dim layoutStepper As Action =
            Sub()
                Dim total As Integer = btnMinus.Width + lblQty.Width + btnPlus.Width
                Dim sx As Integer = (stepWrap.Width - total) \ 2
                Dim cy As Integer = (stepWrap.Height - 24) \ 2
                btnMinus.Location = New Point(sx, cy)
                lblQty.Location = New Point(sx + btnMinus.Width, cy)
                btnPlus.Location = New Point(sx + btnMinus.Width + lblQty.Width, cy)
            End Sub
        AddHandler stepWrap.Resize, Sub() layoutStepper()
        stepWrap.Controls.AddRange({btnMinus, lblQty, btnPlus})
        grid.Controls.Add(stepWrap, 3, 0)
        grid.Controls.Add(lblRefund, 4, 0)

        row.Controls.Add(grid)
        SyncRow()
        Return row
    End Function

    ' =========================================================================
    '  RIGHT — REFUND SUMMARY CARD
    ' =========================================================================
    Private Shared Function BuildSummaryCard() As Control
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
            End Sub

        Dim retIcon As Image = GetIcon("return.png")
        If retIcon IsNot Nothing Then
            Dim tinted As New Bitmap(retIcon.Width, retIcon.Height)
            Using gI As Graphics = Graphics.FromImage(tinted)
                Dim cm As New Imaging.ColorMatrix(New Single()() {
                    New Single() {0, 0, 0, 0, 0}, New Single() {0, 0, 0, 0, 0},
                    New Single() {0, 0, 0, 0, 0}, New Single() {0, 0, 0, 1, 0},
                    New Single() {1, 1, 1, 0, 1}
                })
                Dim ia As New Imaging.ImageAttributes()
                ia.SetColorMatrix(cm)
                gI.DrawImage(retIcon, New Rectangle(0, 0, tinted.Width, tinted.Height),
                    0, 0, retIcon.Width, retIcon.Height, GraphicsUnit.Pixel, ia)
            End Using
            Dim pbRet As New PictureBox With {
                .Image = tinted, .SizeMode = PictureBoxSizeMode.Zoom,
                .Size = New Size(18, 18), .BackColor = Color.Transparent
            }
            iconCircle.Controls.Add(pbRet)
            pbRet.Location = New Point((iconCircle.Width - pbRet.Width) \ 2, (iconCircle.Height - pbRet.Height) \ 2)
        End If

        Dim lblHead As New Label With {
            .Text = "REFUND SUMMARY", .Font = New Font("Segoe UI Semibold", 11, FontStyle.Bold),
            .ForeColor = Color.White, .BackColor = Color.Transparent, .AutoSize = True
        }
        head.Controls.AddRange({iconCircle, lblHead})
        AddHandler head.Resize,
            Sub()
                iconCircle.Location = New Point(16, (head.Height - iconCircle.Height) \ 2)
                lblHead.Location = New Point(52, (head.Height - lblHead.Height) \ 2)
            End Sub

        ' Stats panel
        Dim stats As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 6,
            .BackColor = Color.Transparent, .Padding = New Padding(16, 12, 16, 8)
        }
        stats.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 52.0F))
        stats.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 48.0F))
        stats.RowStyles.Add(New RowStyle(SizeType.Absolute, 30))
        stats.RowStyles.Add(New RowStyle(SizeType.Absolute, 38))
        stats.RowStyles.Add(New RowStyle(SizeType.Percent, 33.0F))
        stats.RowStyles.Add(New RowStyle(SizeType.Percent, 33.0F))
        stats.RowStyles.Add(New RowStyle(SizeType.Percent, 33.0F))
        stats.RowStyles.Add(New RowStyle(SizeType.Absolute, 16))

        ' Reason dropdown — stored in shared field so ProcessRefund can read it
        Dim lblReason As New Label With {
            .Text = "REASON FOR RETURN", .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleLeft,
            .Font = New Font("Segoe UI Semibold", 9), .ForeColor = SubText, .BackColor = Color.Transparent
        }
        stats.SetColumnSpan(lblReason, 2)
        stats.Controls.Add(lblReason, 0, 0)

        _cmbReason = New ComboBox With {
            .Dock = DockStyle.Fill, .DropDownStyle = ComboBoxStyle.DropDownList,
            .FlatStyle = FlatStyle.Flat, .Font = New Font("Segoe UI", 10),
            .BackColor = TileBg, .ForeColor = TextDark, .Margin = New Padding(0, 2, 0, 6)
        }
        _cmbReason.Items.AddRange(New Object() {
            "Defective / damaged", "Wrong colour mixed", "Customer changed mind",
            "Wrong item given", "Expired stock", "Other"
        })
        _cmbReason.SelectedIndex = 0
        stats.SetColumnSpan(_cmbReason, 2)
        stats.Controls.Add(_cmbReason, 0, 1)

        Dim AddStat As Func(Of Integer, String, Label) =
            Function(rowIdx As Integer, labelText As String) As Label
                Dim l As New Label With {
                    .Text = labelText, .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleLeft,
                    .Font = New Font("Segoe UI", 10), .ForeColor = SubText, .BackColor = Color.Transparent
                }
                Dim v As New Label With {
                    .Text = "—", .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleRight,
                    .Font = New Font("Segoe UI Semibold", 11), .ForeColor = Color.FromArgb(50, 70, 65),
                    .BackColor = Color.Transparent
                }
                stats.Controls.Add(l, 0, rowIdx)
                stats.Controls.Add(v, 1, rowIdx)
                Return v
            End Function

        ' ── Restocking fee % spinner ──────────────────────────────────────
        Dim lblFeePct As New Label With {
            .Text = "RESTOCKING FEE %", .Dock = DockStyle.Fill,
            .TextAlign = ContentAlignment.MiddleLeft,
            .Font = New Font("Segoe UI Semibold", 9), .ForeColor = SubText, .BackColor = Color.Transparent
        }
        Dim nudFee As New NumericUpDown With {
            .Minimum = 0, .Maximum = 50, .Value = 0, .DecimalPlaces = 0,
            .Font = New Font("Segoe UI", 10), .BackColor = TileBg, .ForeColor = TextDark,
            .Dock = DockStyle.Fill, .Margin = New Padding(0, 2, 0, 4),
            .TextAlign = HorizontalAlignment.Center
        }
        AddHandler nudFee.ValueChanged,
            Sub()
                _restockPct = nudFee.Value
                Recalc()
            End Sub

        ' Insert fee spinner into stats grid before the stat rows.
        ' We need to shift row indices — replace AddStat calls below.
        stats.RowCount = 8
        stats.RowStyles.Clear()
        stats.RowStyles.Add(New RowStyle(SizeType.Absolute, 30))   ' row 0 — reason label
        stats.RowStyles.Add(New RowStyle(SizeType.Absolute, 38))   ' row 1 — reason combo
        stats.RowStyles.Add(New RowStyle(SizeType.Absolute, 26))   ' row 2 — fee % label
        stats.RowStyles.Add(New RowStyle(SizeType.Absolute, 34))   ' row 3 — fee % spinner
        stats.RowStyles.Add(New RowStyle(SizeType.Percent, 33.0F)) ' row 4 — items
        stats.RowStyles.Add(New RowStyle(SizeType.Percent, 33.0F)) ' row 5 — subtotal
        stats.RowStyles.Add(New RowStyle(SizeType.Percent, 33.0F)) ' row 6 — fee amount
        stats.RowStyles.Add(New RowStyle(SizeType.Absolute, 16))   ' row 7 — divider

        stats.SetColumnSpan(lblFeePct, 2)
        stats.Controls.Add(lblFeePct, 0, 2)
        stats.SetColumnSpan(nudFee, 2)
        stats.Controls.Add(nudFee, 0, 3)

        _lblItems = AddStat(4, "ITEMS TO RETURN")
        _lblSubtotal = AddStat(5, "REFUND SUBTOTAL")
        _lblFee = AddStat(6, "RESTOCKING FEE")

        Dim div As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.Transparent}
        AddHandler div.Paint,
            Sub(s, e)
                Using pen As New Pen(CardBorder, 1.5F)
                    e.Graphics.DrawLine(pen, 0, div.Height \ 2, div.Width, div.Height \ 2)
                End Using
            End Sub
        stats.SetColumnSpan(div, 2)
        stats.Controls.Add(div, 0, 7)

        ' Total block
        Dim totalBlock As New BufferedPanel With {
            .Dock = DockStyle.Fill, .BackColor = Color.Transparent, .Padding = New Padding(12, 6, 12, 6)
        }
        Dim totalCard As New BufferedPanel With {.Dock = DockStyle.Fill, .BackColor = MintDark}
        AddHandler totalCard.Paint,
            Sub(s As Object, e As PaintEventArgs)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect = totalCard.ClientRectangle : rect.Inflate(-1, -1)
                Using br As New LinearGradientBrush(rect, MintDark, Color.FromArgb(0, 60, 50), 90.0F)
                    g.FillRoundedRectangle(br, rect, 10)
                End Using
            End Sub
        Dim tl As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 1, .BackColor = Color.Transparent
        }
        tl.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 46.0F))
        tl.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 54.0F))
        tl.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
        Dim lblTL As New Label With {
            .Text = "TOTAL REFUND", .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleLeft,
            .Font = New Font("Segoe UI", 12, FontStyle.Bold), .ForeColor = Color.FromArgb(200, 240, 225),
            .BackColor = Color.Transparent, .Padding = New Padding(14, 0, 0, 0)
        }
        _lblTotal = New Label With {
            .Text = "₱ 0.00", .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleRight,
            .Font = New Font("Segoe UI", 22, FontStyle.Bold), .ForeColor = RefundGold,
            .BackColor = Color.Transparent, .Padding = New Padding(0, 0, 14, 0)
        }
        tl.Controls.Add(lblTL, 0, 0)
        tl.Controls.Add(_lblTotal, 1, 0)
        totalCard.Controls.Add(tl)
        totalBlock.Controls.Add(totalCard)

        outer.Controls.Add(head, 0, 0)
        outer.Controls.Add(stats, 0, 1)
        outer.Controls.Add(totalBlock, 0, 2)
        card.Controls.Add(outer)
        Return card
    End Function

    ' =========================================================================
    '  REFUND MATH  (single source of truth)
    ' =========================================================================
    '  The restocking fee is quoted to the customer in whole pesos, but each
    '  return_items row stores centavos. Computing the two independently made
    '  returns.total_refund disagree with SUM(return_items.refund_amount) - the
    '  refund handed over did not match its own itemised record.
    '
    '  So: the fee is rounded ONCE here, and the per-line refunds are derived
    '  from that same figure, with the rounding remainder folded into the last
    '  line. SUM(lines) = total_refund exactly, by construction.

    Private Shared Function RestockFee(subtotal As Decimal) As Decimal
        Return Decimal.Round(subtotal * (_restockPct / 100D), 0)
    End Function

    ' Per-line refund amounts, in the order of the list passed in, guaranteed to
    ' sum to (subtotal - RestockFee(subtotal)).
    Private Shared Function LineRefunds(sel As List(Of ReturnItem)) As List(Of Decimal)
        Dim result As New List(Of Decimal)
        If sel Is Nothing OrElse sel.Count = 0 Then Return result

        Dim subtotal As Decimal = sel.Sum(Function(i) i.SelectedQty * i.UnitPrice)
        Dim target As Decimal = subtotal - RestockFee(subtotal)

        If subtotal <= 0D Then
            For Each it In sel : result.Add(0D) : Next
            Return result
        End If

        Dim running As Decimal = 0D
        For idx = 0 To sel.Count - 1
            Dim gross As Decimal = sel(idx).SelectedQty * sel(idx).UnitPrice
            Dim amt As Decimal
            If idx = sel.Count - 1 Then
                ' Last line absorbs the remainder so the parts match the total.
                amt = target - running
            Else
                amt = Decimal.Round(target * gross / subtotal, 2)
                running += amt
            End If
            result.Add(amt)
        Next
        Return result
    End Function

    ' =========================================================================
    '  RECALC
    ' =========================================================================
    Private Shared Sub Recalc()
        If _items Is Nothing Then Return
        Dim qty As Integer = _items.Sum(Function(i) i.SelectedQty)
        Dim subtotal As Decimal = _items.Sum(Function(i) i.SelectedQty * i.UnitPrice)
        Dim fee As Decimal = RestockFee(subtotal)
        Dim total As Decimal = subtotal - fee

        If _lblItems IsNot Nothing Then _lblItems.Text = qty.ToString()
        If _lblSubtotal IsNot Nothing Then _lblSubtotal.Text = "₱ " & subtotal.ToString("N2")
        If _lblFee IsNot Nothing Then _lblFee.Text = "₱ " & fee.ToString("N2")
        If _lblTotal IsNot Nothing Then _lblTotal.Text = "₱ " & total.ToString("N2")
    End Sub

    ' =========================================================================
    '  3. REFUND METHOD BAR  (wired — click tracks _selectedRefundMethod)
    ' =========================================================================
    ''' <summary>
    ''' Readable spelling of a stored refund_method enum value.
    ''' </summary>
    ''' <remarks>
    ''' The column stores 'CardReversal'; a person reading a receipt or an
    ''' activity-log entry should see "Card Reversal". Keeping the two apart is
    ''' what lets the stored value match the enum exactly.
    ''' </remarks>
    Private Shared Function MethodDisplay(stored As String) As String
        Select Case stored
            Case "CardReversal" : Return "Card Reversal"
            Case "EWallet" : Return "E-Wallet"
            Case "StoreCredit" : Return "Store Credit"
            Case Else : Return stored
        End Select
    End Function

    Private Shared Function BuildMethodBar() As Control
        Dim host As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.Transparent, .Padding = New Padding(0, 10, 0, 4)}
        Dim layout As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 4, .RowCount = 1,
            .BackColor = Color.Transparent, .Margin = New Padding(0), .Padding = New Padding(0)
        }
        For i = 0 To 3
            layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 25.0F))
        Next
        layout.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))

        ' The method name passed here is written straight into returns.
        ' refund_method, which is enum('Cash','CardReversal','EWallet',
        ' 'StoreCredit') -- no spaces, no hyphen. This used to pass the display
        ' spelling ("Card Reversal", "E-Wallet", "Store Credit"); MySQL is not
        ' running in STRICT mode here, so rather than failing it silently stored
        ' the empty string. Every non-cash refund lost its method, and the
        ' accountant's reports (which match on the method text) could not
        ' classify them. Only "Cash" ever round-tripped, because it happens to
        ' be spelled the same either way.
        Dim btnCash = MakePayBtn("💵  CASH", Color.FromArgb(0, 148, 74), Color.FromArgb(0, 100, 55), 0, "Cash")
        Dim btnCard = MakePayBtn("💳  CARD REVERSAL", Color.FromArgb(30, 100, 190), Color.FromArgb(20, 70, 140), 4, "CardReversal")
        Dim btnWallet = MakePayBtn("📱  E-WALLET", Color.FromArgb(100, 50, 180), Color.FromArgb(70, 30, 140), 4, "EWallet")
        Dim btnStore = MakePayBtn("🎟  STORE CREDIT", MintPrimary, MintDark, 4, "StoreCredit")

        ' Default highlight CASH
        _activeMethodBtn = btnCash
        btnCash.FlatAppearance.BorderSize = 3
        btnCash.FlatAppearance.BorderColor = Color.White

        layout.Controls.Add(btnCash, 0, 0)
        layout.Controls.Add(btnCard, 1, 0)
        layout.Controls.Add(btnWallet, 2, 0)
        layout.Controls.Add(btnStore, 3, 0)

        host.Controls.Add(layout)
        Return host
    End Function

    Private Shared Function MakePayBtn(text As String, normalClr As Color, hoverClr As Color,
                                        leftMargin As Integer, methodName As String) As Button
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
                    If b.FlatAppearance.BorderSize > 0 Then
                        Using p As New Pen(b.FlatAppearance.BorderColor, 2.5F) : g.DrawPath(p, path) : End Using
                    End If
                End Using
                TextRenderer.DrawText(g, b.Text, b.Font, rect, Color.White,
                    TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub

        AddHandler btn.MouseEnter,
            Sub()
                If _activeMethodBtn IsNot btn Then btn.BackColor = hoverClr
                btn.Invalidate()
            End Sub
        AddHandler btn.MouseLeave,
            Sub()
                If _activeMethodBtn IsNot btn Then btn.BackColor = normalClr
                btn.Invalidate()
            End Sub

        AddHandler btn.Click,
            Sub()
                _selectedRefundMethod = methodName
                ' De-select old
                If _activeMethodBtn IsNot Nothing Then
                    _activeMethodBtn.FlatAppearance.BorderSize = 0
                    _activeMethodBtn.Invalidate()
                End If
                ' Highlight new
                btn.FlatAppearance.BorderSize = 3
                btn.FlatAppearance.BorderColor = Color.White
                btn.Invalidate()
                _activeMethodBtn = btn
            End Sub

        Return btn
    End Function

    ' =========================================================================
    '  4. ACTION BAR  (all buttons wired)
    ' =========================================================================
    Private Shared Function BuildActionBar() As Control
        Dim host As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.Transparent, .Padding = New Padding(0, 4, 0, 0)}
        Dim layout As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 4, .RowCount = 1,
            .BackColor = Color.Transparent, .Margin = New Padding(0), .Padding = New Padding(0)
        }
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 18.0F))
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 18.0F))
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 26.0F))
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 38.0F))
        layout.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))

        ' CANCEL
        Dim btnCancel = MakeActionBtn("✖  CANCEL", DangerBack, DangerFore, 0)
        AddHandler btnCancel.Click, Sub() LoadReturns(_mainContainer)
        layout.Controls.Add(btnCancel, 0, 0)

        ' CLEAR — reset all selections, keep the loaded receipt
        Dim btnClear = MakeActionBtn("⟲  CLEAR", Color.FromArgb(232, 238, 237), SubText, 6)
        AddHandler btnClear.Click,
            Sub()
                If _items Is Nothing OrElse _items.Count = 0 Then Return
                For Each it In _items : it.SelectedQty = 0 : Next
                RebuildRows()
                Recalc()
            End Sub
        layout.Controls.Add(btnClear, 1, 0)

        ' PRINT SLIP
        Dim btnPrint = MakeActionBtn("🖨  PRINT SLIP", Color.FromArgb(220, 235, 245), Color.FromArgb(30, 80, 160), 6)
        AddHandler btnPrint.Click, Sub() PrintSlip()
        layout.Controls.Add(btnPrint, 2, 0)

        ' PROCESS REFUND
        Dim btnProcess = MakePrimaryBtn("↩  PROCESS REFUND", 6)
        AddHandler btnProcess.Click, Sub() ProcessRefund()
        layout.Controls.Add(btnProcess, 3, 0)

        host.Controls.Add(layout)
        Return host
    End Function

    ' =========================================================================
    '  DB — LOOKUP RECEIPT
    ' =========================================================================
    Private Shared Sub LookupReceipt(saleNo As String)
        If _rowsHost Is Nothing Then Return

        ' Strip common prefixes customers might type
        saleNo = saleNo.TrimStart("#"c).Trim()

        _rowsHost.SuspendLayout()
        _rowsHost.Controls.Clear()
        _rowsHost.Controls.Add(MakeEmptyPlaceholder("Searching…"))
        _rowsHost.ResumeLayout()
        _rowsHost.Refresh()

        Dim newItems As New List(Of ReturnItem)
        Dim foundSaleId As Integer = 0
        Dim foundSaleNo As String = ""

        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT s.sale_id, s.sale_no, s.sale_datetime, " &
                    "si.sale_item_id, si.product_id, si.qty, si.unit_price, " &
                    "(si.qty - COALESCE(r.returned_qty, 0)) AS returnable_qty, " &
                    "p.name AS product_name, p.sku, p.color_hex " &
                    "FROM sales s " &
                    "JOIN sale_items si ON s.sale_id=si.sale_id " &
                    "JOIN products p ON si.product_id=p.product_id " &
                    "LEFT JOIN (SELECT sale_item_id, SUM(qty_returned) AS returned_qty " &
                    "           FROM return_items GROUP BY sale_item_id) r " &
                    "       ON r.sale_item_id = si.sale_item_id " &
                    "WHERE s.sale_no=@sno AND s.status IN ('Completed','Partially Refunded') " &
                    "  AND (si.qty - COALESCE(r.returned_qty, 0)) > 0 " &
                    "ORDER BY si.sale_item_id", conn)
                    cmd.Parameters.AddWithValue("@sno", saleNo)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            If foundSaleId = 0 Then
                                foundSaleId = CInt(rdr("sale_id"))
                                foundSaleNo = rdr("sale_no").ToString()
                            End If
                            Dim colorHex As String = If(IsDBNull(rdr("color_hex")), "", rdr("color_hex").ToString().Trim())
                            Dim swatch As Color = Color.FromArgb(180, 180, 185)
                            Try
                                If colorHex.StartsWith("#") Then swatch = ColorTranslator.FromHtml(colorHex)
                            Catch : End Try

                            newItems.Add(New ReturnItem With {
                                .SaleItemId = CInt(rdr("sale_item_id")),
                                .ProductId = CInt(rdr("product_id")),
                                .Sku = rdr("sku").ToString(),
                                .Name = rdr("product_name").ToString(),
                                .Swatch = swatch,
                                .UnitPrice = CDec(rdr("unit_price")),
                                .Qty = CInt(rdr("returnable_qty")),
                                .SelectedQty = 0
                            })
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Database error: " & ex.Message, "Lookup Failed", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try

        _rowsHost.SuspendLayout()
        _rowsHost.Controls.Clear()

        If foundSaleId = 0 Then
            _saleId = 0
            _saleNo = ""
            _items = New List(Of ReturnItem)()
            _rowsHost.Controls.Add(MakeEmptyPlaceholder($"No completed sale found for  #{ saleNo }."))
            If _lblLoaded IsNot Nothing Then _lblLoaded.Text = "Receipt not found"
        Else
            _saleId = foundSaleId
            _saleNo = foundSaleNo
            _items = newItems

            For Each it In _items
                _rowsHost.Controls.Add(BuildReturnRow(it))
            Next
            ' Reverse z-order so first item shows on top
            For i = _rowsHost.Controls.Count - 1 To 0 Step -1
                _rowsHost.Controls.SetChildIndex(_rowsHost.Controls(i), _rowsHost.Controls.Count - 1 - i)
            Next

            If _lblLoaded IsNot Nothing Then
                _lblLoaded.Text = "Loaded:  #" & _saleNo & "  ·  " & newItems.Count & " item(s)"
            End If
        End If

        _rowsHost.ResumeLayout()
        _rowsHost.Invalidate(True)
        Recalc()
    End Sub

    ' ── Rebuild rows after CLEAR (keep loaded receipt, reset qtys) ────────────
    Private Shared Sub RebuildRows()
        If _rowsHost Is Nothing OrElse _items Is Nothing Then Return
        _rowsHost.SuspendLayout()
        _rowsHost.Controls.Clear()
        If _items.Count = 0 Then
            _rowsHost.Controls.Add(MakeEmptyPlaceholder("Look up a receipt to see returnable items."))
        Else
            For Each it In _items
                _rowsHost.Controls.Add(BuildReturnRow(it))
            Next
            For i = _rowsHost.Controls.Count - 1 To 0 Step -1
                _rowsHost.Controls.SetChildIndex(_rowsHost.Controls(i), _rowsHost.Controls.Count - 1 - i)
            Next
        End If
        _rowsHost.ResumeLayout()
        _rowsHost.Invalidate(True)
    End Sub

    ' =========================================================================
    '  DB — PROCESS REFUND  (full InnoDB transaction)
    ' =========================================================================
    Private Shared Sub ProcessRefund()
        ' Guards
        If _saleId <= 0 Then
            AppModal.Show("Please look up a receipt first.", "No Receipt Loaded",
                MessageBoxButtons.OK, MessageBoxIcon.Warning) : Return
        End If

        Dim selectedItems = _items.Where(Function(i) i.SelectedQty > 0).ToList()
        If selectedItems.Count = 0 Then
            AppModal.Show("Select at least one item to return.", "Nothing Selected",
                MessageBoxButtons.OK, MessageBoxIcon.Warning) : Return
        End If

        Dim subtotal As Decimal = selectedItems.Sum(Function(i) i.SelectedQty * i.UnitPrice)
        Dim fee As Decimal = RestockFee(subtotal)
        Dim totalRefund As Decimal = subtotal - fee
        Dim reason As String = If(_cmbReason IsNot Nothing AndAlso _cmbReason.SelectedIndex >= 0,
                                   _cmbReason.SelectedItem.ToString(), "Other")

        ' Confirm
        Dim msg As String =
            $"Refund Summary:{vbCrLf}" &
            $"  Receipt:   #{_saleNo}{vbCrLf}" &
            $"  Items:      {selectedItems.Count}{vbCrLf}" &
            $"  Subtotal:  ₱{subtotal:N2}{vbCrLf}" &
            $"  Fee:        ₱{fee:N2}{vbCrLf}" &
            $"  Method:    {MethodDisplay(_selectedRefundMethod)}{vbCrLf}" &
            $"  TOTAL:     ₱{totalRefund:N2}{vbCrLf}{vbCrLf}" &
            "Process this refund?"

        If AppModal.Show(msg, "Confirm Refund", MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then
            Return
        End If

        Dim returnNo As String = DocNumber.Generate("RTN-")
        Dim committed As Boolean = False

        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Dim tx = conn.BeginTransaction()
                Try
                    ' 0. Re-check what is still returnable, with the sale row locked.
                    '    The list on screen was read before the confirmation dialog,
                    '    so a refund raised on another till in between would let the
                    '    same units be refunded twice and restocked twice.
                    Using cmd As New MySqlCommand(
                        "SELECT status FROM sales WHERE sale_id = @sid FOR UPDATE", conn, tx)
                        cmd.Parameters.AddWithValue("@sid", _saleId)
                        Dim st = cmd.ExecuteScalar()
                        If st Is Nothing OrElse IsDBNull(st) Then
                            Throw New Exception("That sale no longer exists.")
                        End If
                        Dim liveStatus As String = st.ToString()
                        If liveStatus <> "Completed" AndAlso liveStatus <> "Partially Refunded" Then
                            Throw New Exception("Sale #" & _saleNo & " is now marked '" & liveStatus &
                                                "' and can no longer be refunded.")
                        End If
                    End Using

                    For Each chk In selectedItems
                        Dim stillReturnable As Integer
                        Using cmd As New MySqlCommand(
                            "SELECT si.qty - COALESCE(SUM(ri.qty_returned), 0) " &
                            "FROM sale_items si " &
                            "LEFT JOIN return_items ri ON ri.sale_item_id = si.sale_item_id " &
                            "WHERE si.sale_item_id = @siid GROUP BY si.sale_item_id, si.qty", conn, tx)
                            cmd.Parameters.AddWithValue("@siid", chk.SaleItemId)
                            Dim o = cmd.ExecuteScalar()
                            stillReturnable = If(o Is Nothing OrElse IsDBNull(o), 0, Convert.ToInt32(o))
                        End Using
                        If chk.SelectedQty > stillReturnable Then
                            Throw New Exception(
                                chk.Name & " has already been refunded elsewhere." & vbCrLf &
                                "Requested " & chk.SelectedQty & ", only " & stillReturnable &
                                " still returnable. Re-scan the receipt and try again.")
                        End If
                    Next
                    ' 1. INSERT into returns
                    Dim returnId As Integer
                    Using cmd As New MySqlCommand(
                        "INSERT INTO returns (return_no, sale_id, processed_by, return_datetime, " &
                        "reason, refund_method, restocking_fee_pct, total_refund) " &
                        "VALUES (@rno, @sid, @uid, NOW(), @reason, @method, @feePct, @total)", conn, tx)
                        cmd.Parameters.AddWithValue("@rno", returnNo)
                        cmd.Parameters.AddWithValue("@sid", _saleId)
                        cmd.Parameters.AddWithValue("@uid", Session.CurrentUserId)
                        cmd.Parameters.AddWithValue("@reason", reason)
                        cmd.Parameters.AddWithValue("@method", _selectedRefundMethod)
                        cmd.Parameters.AddWithValue("@feePct", _restockPct)
                        cmd.Parameters.AddWithValue("@total", totalRefund)
                        cmd.ExecuteNonQuery()
                    End Using

                    ' Get the new return_id
                    Using cmd As New MySqlCommand("SELECT LAST_INSERT_ID()", conn, tx)
                        returnId = CInt(cmd.ExecuteScalar())
                    End Using

                    ' 2. For each selected item: INSERT return_items + restore stock
                    Dim lineAmounts = LineRefunds(selectedItems)
                    For itemIdx = 0 To selectedItems.Count - 1
                        Dim it = selectedItems(itemIdx)
                        Dim refundAmt As Decimal = lineAmounts(itemIdx)

                        Using cmd As New MySqlCommand(
                            "INSERT INTO return_items (return_id, sale_item_id, qty_returned, refund_amount) " &
                            "VALUES (@rid, @siid, @qty, @amt)", conn, tx)
                            cmd.Parameters.AddWithValue("@rid", returnId)
                            cmd.Parameters.AddWithValue("@siid", it.SaleItemId)
                            cmd.Parameters.AddWithValue("@qty", it.SelectedQty)
                            cmd.Parameters.AddWithValue("@amt", refundAmt)
                            cmd.ExecuteNonQuery()
                        End Using

                        ' Restore stock
                        Using cmd As New MySqlCommand(
                            "UPDATE products SET stock_qty=stock_qty+@qty WHERE product_id=@pid", conn, tx)
                            cmd.Parameters.AddWithValue("@qty", it.SelectedQty)
                            cmd.Parameters.AddWithValue("@pid", it.ProductId)
                            cmd.ExecuteNonQuery()
                        End Using

                        ' Log the stock movement for the audit trail (mirrors Sale / Void)
                        Using cmd As New MySqlCommand(
                            "INSERT INTO stock_movements (product_id, user_id, movement_type, qty_change, reference_type, reference_id, note, created_at) " &
                            "VALUES (@pid, @uid, 'Adjustment', @qc, 'return', @rid, 'Return of sale', NOW())", conn, tx)
                            cmd.Parameters.AddWithValue("@pid", it.ProductId)
                            cmd.Parameters.AddWithValue("@uid", Session.CurrentUserId)
                            cmd.Parameters.AddWithValue("@qc", it.SelectedQty)
                            cmd.Parameters.AddWithValue("@rid", returnId)
                            cmd.ExecuteNonQuery()
                        End Using
                    Next

                    ' Mark the sale Refunded (all units returned) or Partially Refunded.
                    ' The just-inserted return_items rows are visible in this same transaction.
                    Dim soldQty As Integer = 0
                    Using cmd As New MySqlCommand(
                        "SELECT COALESCE(SUM(qty),0) FROM sale_items WHERE sale_id=@sid", conn, tx)
                        cmd.Parameters.AddWithValue("@sid", _saleId)
                        soldQty = CInt(cmd.ExecuteScalar())
                    End Using
                    Dim returnedQty As Integer = 0
                    Using cmd As New MySqlCommand(
                        "SELECT COALESCE(SUM(ri.qty_returned),0) FROM return_items ri " &
                        "JOIN sale_items si ON ri.sale_item_id=si.sale_item_id " &
                        "WHERE si.sale_id=@sid", conn, tx)
                        cmd.Parameters.AddWithValue("@sid", _saleId)
                        returnedQty = CInt(cmd.ExecuteScalar())
                    End Using
                    Dim newStatus As String = If(returnedQty >= soldQty, "Refunded", "Partially Refunded")
                    Using cmd As New MySqlCommand(
                        "UPDATE sales SET status=@st WHERE sale_id=@sid", conn, tx)
                        cmd.Parameters.AddWithValue("@st", newStatus)
                        cmd.Parameters.AddWithValue("@sid", _saleId)
                        cmd.ExecuteNonQuery()
                    End Using

                    tx.Commit()
                    committed = True

                    ' Refunds move cash out of the drawer; without this the log
                    ' showed the sale but never the money going back.
                    ActivityLog.Record("SALE_REFUND", "sale", CInt(_saleId),
                        returnNo & " against " & _saleNo & " — " &
                        ChrW(8369) & totalRefund.ToString("N2") &
                        " via " & MethodDisplay(_selectedRefundMethod) &
                        If(reason = "", "", " (" & reason & ")"))

                    ' Snapshot for PRINT SLIP before the screen resets.
                    _lastRtnNo = returnNo
                    _lastRtnSaleNo = _saleNo
                    _lastRtnMethod = _selectedRefundMethod
                    _lastRtnReason = reason
                    _lastRtnFeePct = _restockPct
                    _lastRtnSubtotal = subtotal
                    _lastRtnFee = fee
                    _lastRtnTotal = totalRefund
                    _lastRtnAt = DateTime.Now
                    _lastRtnLines.Clear()
                    For snapIdx = 0 To selectedItems.Count - 1
                        _lastRtnLines.Add((selectedItems(snapIdx).Name,
                                           selectedItems(snapIdx).SelectedQty,
                                           lineAmounts(snapIdx)))
                    Next

                    AppModal.Show(
                        $"Refund #{returnNo} processed successfully!{vbCrLf}Total refunded: ₱{totalRefund:N2}",
                        "Refund Complete", MessageBoxButtons.OK, MessageBoxIcon.Information)

                    ' Reset the screen
                    LoadReturns(_mainContainer)

                Catch exInner As Exception
                    ' Only roll back work that is still open. Once the commit has
                    ' landed the refund is final, and rolling back here would throw
                    ' over it and report a successful refund as a failure.
                    If Not committed Then tx.Rollback()
                    Throw
                End Try
            End Using

        Catch ex As Exception
            If committed Then
                ' The money moved and stock was restored; only the follow-up work
                ' (receipt snapshot, dialog, screen reset) failed. Saying "failed"
                ' here would invite a second refund for the same items.
                AppModal.Show(
                    "Refund " & returnNo & " was processed, but the screen could not be refreshed:" &
                    Environment.NewLine & ex.Message & Environment.NewLine & Environment.NewLine &
                    "Do NOT process this refund again - reopen Returns to continue.",
                    "Refund Processed", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Else
                AppModal.Show("Failed to process refund: " & ex.Message, "Refund Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error)
            End If
        End Try
    End Sub

    ' =========================================================================
    '  PRINT SLIP — reprint the last processed refund, or preview the current
    '  selection if no refund has been processed yet this session.
    ' =========================================================================
    Private Shared Sub PrintSlip()
        Dim lines As New List(Of ReceiptPrinter.RLine)
        ' Store identity comes from settings, exactly as the sales receipt does;
        ' a hardcoded name meant a renamed store still printed the old one.
        lines.Add(New ReceiptPrinter.RLine(StoreSettings.StoreName, , ReceiptPrinter.LineStyle.Title))
        lines.Add(New ReceiptPrinter.RLine("Return / Refund Slip", , ReceiptPrinter.LineStyle.Muted))
        lines.Add(ReceiptPrinter.Divider())

        If Not String.IsNullOrEmpty(_lastRtnNo) Then
            lines.Add(New ReceiptPrinter.RLine("Return No:", _lastRtnNo))
            lines.Add(New ReceiptPrinter.RLine("Orig. Receipt:", _lastRtnSaleNo))
            lines.Add(New ReceiptPrinter.RLine("Date:", _lastRtnAt.ToString("MMM d, yyyy h:mm tt")))
            lines.Add(New ReceiptPrinter.RLine("Processed by:", Session.CurrentFullName))
            lines.Add(New ReceiptPrinter.RLine("Reason:", _lastRtnReason))
            lines.Add(New ReceiptPrinter.RLine("Method:", MethodDisplay(_lastRtnMethod)))
            lines.Add(ReceiptPrinter.Divider())
            For Each it In _lastRtnLines
                lines.Add(New ReceiptPrinter.RLine(it.name, "", ReceiptPrinter.LineStyle.Bold))
                lines.Add(New ReceiptPrinter.RLine("  qty " & it.qty.ToString(), "₱ " & it.amt.ToString("N2")))
            Next
            lines.Add(ReceiptPrinter.Divider())
            lines.Add(New ReceiptPrinter.RLine("Subtotal", "₱ " & _lastRtnSubtotal.ToString("N2")))
            If _lastRtnFee > 0 Then _
                lines.Add(New ReceiptPrinter.RLine("Restocking fee (" & _lastRtnFeePct.ToString("0.##") & "%)",
                                                   "− ₱ " & _lastRtnFee.ToString("N2")))
            lines.Add(New ReceiptPrinter.RLine("REFUND", "₱ " & _lastRtnTotal.ToString("N2"), ReceiptPrinter.LineStyle.Bold))
        ElseIf _saleId > 0 AndAlso _items IsNot Nothing AndAlso _items.Any(Function(i) i.SelectedQty > 0) Then
            ' Draft preview of the current selection (not yet processed).
            Dim sel = _items.Where(Function(i) i.SelectedQty > 0).ToList()
            Dim subtotal As Decimal = sel.Sum(Function(i) i.SelectedQty * i.UnitPrice)
            Dim fee As Decimal = RestockFee(subtotal)
            lines.Add(New ReceiptPrinter.RLine("** DRAFT — NOT PROCESSED **", , ReceiptPrinter.LineStyle.Muted))
            lines.Add(New ReceiptPrinter.RLine("Orig. Receipt:", _saleNo))
            lines.Add(New ReceiptPrinter.RLine("Method:", MethodDisplay(_selectedRefundMethod)))
            lines.Add(ReceiptPrinter.Divider())
            For Each it In sel
                lines.Add(New ReceiptPrinter.RLine(it.Name, "", ReceiptPrinter.LineStyle.Bold))
                lines.Add(New ReceiptPrinter.RLine("  qty " & it.SelectedQty.ToString(),
                                                   "₱ " & (it.SelectedQty * it.UnitPrice).ToString("N2")))
            Next
            lines.Add(ReceiptPrinter.Divider())
            lines.Add(New ReceiptPrinter.RLine("Subtotal", "₱ " & subtotal.ToString("N2")))
            If fee > 0 Then lines.Add(New ReceiptPrinter.RLine("Restocking fee", "− ₱ " & fee.ToString("N2")))
            lines.Add(New ReceiptPrinter.RLine("REFUND", "₱ " & (subtotal - fee).ToString("N2"), ReceiptPrinter.LineStyle.Bold))
        Else
            AppModal.Show("Process a refund or select items first.",
                            "Nothing to Print", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If

        lines.Add(ReceiptPrinter.Spacer())
        lines.Add(New ReceiptPrinter.RLine("Keep this slip for your records.", , ReceiptPrinter.LineStyle.Muted))
        ReceiptPrinter.Preview("Return Slip", lines)
    End Sub

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
    '  BUTTON FACTORIES (identical to other builders)
    ' =========================================================================
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


End Class