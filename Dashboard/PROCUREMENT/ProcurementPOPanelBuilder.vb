Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports MySql.Data.MySqlClient

' =============================================================================
'  ProcurementPOPanelBuilder
'  Screen 2 — Purchase Orders.
'
'  THREE INLINE VIEWS (swapped inside the same container):
'    1. List    — filterable list of POs (All-active / Draft / Ordered /
'                 Received / Cancelled). "New PO" opens the builder.
'    2. New PO  — supplier + dates, a Pending-Requests picker on the left and a
'                 live line-items editor on the right. Save writes
'                 purchase_orders + po_items in ONE transaction and flips any
'                 sourced requests to 'Converted'.
'    3. Detail  — read of one PO's lines, with "Mark as Ordered" (Draft) or
'                 "Receive Goods" (Ordered → navigates to the Receive screen).
'
'  Shared-only, no instance state. Helpers (RoundRect / RoundControl /
'  RoundLabel / MakeLink / SafeInt / HexToColor) copied verbatim from
'  InventoryLowStockPanelBuilder. The save transaction mirrors
'  InventoryStockAdjustmentPanelBuilder.CommitAdjustment().
'
'  NEW PO FORM — UI/UX pass:
'    • Larger, more readable type across the whole form (labels, fields,
'      combos, date pickers, line rows, totals).
'    • Inputs wrapped in rounded, bordered "field" cards with a focus glow.
'    • Date-picker calendars themed to the mint palette.
'    • "Back to list" is a tappable pill; buttons have hover states.
'    • RESTORED: the line-row rendering loop in RebuildLines (was accidentally
'      dropped when the empty-state placeholder was pasted in).
' =============================================================================
Public Class ProcurementPOPanelBuilder

    ' ── PALETTE (exact spec values) ──────────────────────────────────────────────
    Private Shared ReadOnly MintDark As Color = Color.FromArgb(10, 79, 66)
    Private Shared ReadOnly MintPrimary As Color = Color.FromArgb(0, 168, 142)
    Private Shared ReadOnly MintDeep As Color = Color.FromArgb(0, 130, 109)
    Private Shared ReadOnly MintBg As Color = Color.FromArgb(244, 248, 246)
    Private Shared ReadOnly CardBg As Color = Color.White
    Private Shared ReadOnly CardBorder As Color = Color.FromArgb(228, 232, 230)
    Private Shared ReadOnly SubText As Color = Color.FromArgb(90, 114, 107)
    Private Shared ReadOnly FaintText As Color = Color.FromArgb(150, 165, 160)
    Private Shared ReadOnly TileBg As Color = Color.FromArgb(236, 248, 244)
    Private Shared ReadOnly RowDivider As Color = Color.FromArgb(238, 242, 240)
    Private Shared ReadOnly PositiveClr As Color = Color.FromArgb(0, 160, 106)
    Private Shared ReadOnly NegativeClr As Color = Color.FromArgb(210, 60, 60)
    Private Shared ReadOnly WarningClr As Color = Color.FromArgb(232, 120, 30)
    Private Shared ReadOnly GoldValue As Color = Color.FromArgb(255, 196, 120)
    Private Shared ReadOnly TotalDeep As Color = Color.FromArgb(0, 60, 50)
    Private Shared ReadOnly Peso As String = ChrW(8369)

    Private Shared ReadOnly InputBorder As Color = Color.FromArgb(214, 224, 220)
    Private Shared ReadOnly InputFocus As Color = Color.FromArgb(0, 168, 142)
    Private Shared ReadOnly MintHover As Color = Color.FromArgb(0, 150, 127)
    Private Shared ReadOnly DeepHover As Color = Color.FromArgb(0, 112, 94)

    ' ── DATA MODELS ──────────────────────────────────────────────────────────────
    Private Class PORow
        Public PoId As Integer
        Public PoNo As String
        Public SupplierName As String
        Public OrderDate As DateTime
        Public HasExpected As Boolean
        Public ExpectedDate As DateTime
        Public Status As String
        Public ItemCount As Integer
        Public TotalCost As Decimal
    End Class

    Private Class POLine
        Public ProductId As Integer
        Public ProductName As String
        Public Sku As String
        Public Qty As Integer
        Public UnitCost As Decimal
        Public RequestId As Integer   ' 0 = manual (no source request)
        Public ReadOnly Property LineTotal As Decimal
            Get
                Return Math.Round(UnitCost * Qty, 2)
            End Get
        End Property
    End Class

    Private Class ProdItem
        Public Id As Integer
        Public Name As String
        Public Sku As String
        Public Price As Decimal
        Public Overrides Function ToString() As String
            Return Name & "  (" & Sku & ")"
        End Function
    End Class

    Private Class SupItem
        Public Id As Integer
        Public Name As String
        Public Overrides Function ToString() As String
            Return Name
        End Function
    End Class

    Private Class PendingReqLite
        Public RequestId As Integer
        Public ProductId As Integer
        Public ProductName As String
        Public Sku As String
        Public UnitPrice As Decimal
        Public QtyRequested As Integer
        Public RequestedBy As String
        Public ImagePath As String
    End Class

    ' ── STATE ────────────────────────────────────────────────────────────────────
    Private Shared containerRef As Panel
    Private Shared currentFilter As String = "All"   ' All = active (not Cancelled/Received)

    ' New-PO form state
    Private Shared newLines As New List(Of POLine)
    Private Shared linesHostRef As Panel
    Private Shared lblGrandRef As Label
    Private Shared cmbSupplierRef As ComboBox
    Private Shared dtOrderRef As DateTimePicker
    Private Shared dtExpectedRef As DateTimePicker
    Private Shared cmbProductRef As ComboBox
    Private Shared btnSaveRef As Button

    ' =========================================================================
    '  ENTRY POINTS
    ' =========================================================================
    Public Shared Sub LoadPO(container As Panel)
        containerRef = container
        currentFilter = "All"
        RenderList()
    End Sub

    ' Pre-loads the New PO form with one pending request already added as a line.
    Public Shared Sub LoadPOWithRequest(container As Panel, requestId As Integer)
        containerRef = container
        LoadNewPOForm(requestId)
    End Sub

    ' =========================================================================
    '  VIEW 1 — PO LIST
    ' =========================================================================
    Private Shared Sub RenderList()
        Dim container As Panel = containerRef
        If container Is Nothing Then Return

        Dim rows As List(Of PORow) = LoadList(currentFilter)

        container.SuspendLayout()
        container.Controls.Clear()
        container.BackColor = MintBg

        ' ---- Scrolling list ----
        Dim listHost As New Panel With {
            .Dock = DockStyle.Fill, .BackColor = MintBg, .Padding = New Padding(24, 4, 24, 20)
        }
        Dim rowsHost As New Panel With {
            .Dock = DockStyle.Fill, .BackColor = Color.Transparent, .AutoScroll = True
        }
        listHost.Controls.Add(rowsHost)

        ' ---- Top region ----
        Dim topRegion As Panel = BuildListTop()
        topRegion.Dock = DockStyle.Top

        container.Controls.Add(listHost)
        container.Controls.Add(topRegion)
        container.ResumeLayout()

        ' ---- Fill rows ----
        If rows.Count = 0 Then
            Dim empty As Panel = BuildEmptyCard("No purchase orders",
                "Nothing matches this filter. Use " & ChrW(8220) & "New PO" & ChrW(8221) & " to create one.")
            empty.Location = New Point(0, 16)
            empty.Width = rowsHost.ClientSize.Width - 4
            empty.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
            rowsHost.Controls.Add(empty)
        Else
            Dim y As Integer = 8
            For Each po In rows
                Dim card As Panel = BuildPOCard(po)
                card.Location = New Point(0, y)
                card.Width = rowsHost.ClientSize.Width - 4
                card.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
                rowsHost.Controls.Add(card)
                y += card.Height + 12
            Next
            AddHandler rowsHost.Resize,
                Sub()
                    Dim w As Integer = rowsHost.ClientSize.Width - 4
                    For Each c As Control In rowsHost.Controls
                        c.Width = w
                    Next
                End Sub
        End If
    End Sub

    Private Shared Function BuildListTop() As Panel
        Dim region As New Panel With {.Height = 150, .BackColor = MintBg, .Padding = New Padding(24, 22, 24, 0)}

        Dim lblTitle As New Label With {
            .Text = "Purchase Orders", .Font = New Font("Segoe UI", 24, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(24, 18)
        }
        region.Controls.Add(lblTitle)

        ' ---- New PO button (top-right) ----
        Dim btnNew As New Button With {
            .Text = "+ New PO", .Size = New Size(130, 40),
            .FlatStyle = FlatStyle.Flat, .BackColor = MintPrimary, .ForeColor = Color.White,
            .Font = New Font("Segoe UI Semibold", 10), .Cursor = Cursors.Hand,
            .TextAlign = ContentAlignment.MiddleCenter,
            .Padding = New Padding(0),
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        btnNew.FlatAppearance.BorderSize = 0
        RoundControl(btnNew, 10)
        AddHover(btnNew, MintPrimary, MintHover)
        AddHandler btnNew.Click, Sub() LoadNewPOForm(0)
        region.Controls.Add(btnNew)
        AddHandler region.Resize, Sub()
                                      btnNew.Top = 22
                                      btnNew.Left = region.Width - 24 - btnNew.Width
                                  End Sub
        btnNew.Top = 22
        btnNew.Left = 700

        ' ---- Filter tabs ----
        Dim tabs() As String = {"All", "Draft", "Ordered", "Received", "Cancelled"}
        Dim tabRow As New FlowLayoutPanel With {
            .Left = 24, .Top = 84, .Height = 44, .AutoSize = False, .Width = 700,
            .BackColor = Color.Transparent, .WrapContents = False,
            .Anchor = AnchorStyles.Left Or AnchorStyles.Top
        }
        For Each t In tabs
            tabRow.Controls.Add(BuildFilterTab(t))
        Next
        region.Controls.Add(tabRow)

        Return region
    End Function

    Private Shared Function BuildFilterTab(name As String) As Label
        Dim isActive As Boolean = String.Equals(name, currentFilter, StringComparison.OrdinalIgnoreCase)
        Dim tab As New Label With {
            .Text = If(name = "All", "All Active", name),
            .Font = New Font(If(isActive, "Segoe UI Semibold", "Segoe UI"), 9.5F),
            .ForeColor = If(isActive, Color.White, SubText),
            .BackColor = If(isActive, MintPrimary, Color.FromArgb(220, 235, 232)),
            .AutoSize = False, .Size = New Size(108, 34),
            .TextAlign = ContentAlignment.MiddleCenter, .Cursor = Cursors.Hand,
            .Margin = New Padding(0, 0, 8, 0)
        }
        RoundLabel(tab, 8)

        AddHandler tab.MouseEnter,
            Sub()
                If Not String.Equals(name, currentFilter, StringComparison.OrdinalIgnoreCase) Then
                    tab.BackColor = Color.FromArgb(210, 240, 235)
                    tab.ForeColor = MintDark
                    tab.Invalidate()
                End If
            End Sub
        AddHandler tab.MouseLeave,
            Sub()
                If Not String.Equals(name, currentFilter, StringComparison.OrdinalIgnoreCase) Then
                    tab.BackColor = Color.FromArgb(220, 235, 232)
                    tab.ForeColor = SubText
                    tab.Invalidate()
                End If
            End Sub
        AddHandler tab.Click,
            Sub()
                currentFilter = name
                RenderList()
            End Sub
        Return tab
    End Function

    Private Shared Function BuildPOCard(po As PORow) As Panel
        Dim card As New BufferedPanel With {.Height = 76, .BackColor = CardBg, .Cursor = Cursors.Hand}
        AddHandler card.Paint, Sub(s, e) PaintCard(CType(s, Panel), e)

        Dim lblPo As New Label With {
            .Text = po.PoNo, .Font = New Font("Segoe UI", 11.5F, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(20, 14), .BackColor = Color.Transparent
        }
        Dim lblSupplier As New Label With {
            .Text = po.SupplierName, .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = SubText, .AutoSize = True, .Location = New Point(20, 42), .BackColor = Color.Transparent
        }
        Dim lblDates As New Label With {
            .Text = "Ordered " & po.OrderDate.ToString("MMM d, yyyy") & "   " & ChrW(8226) & "   " &
                    If(po.HasExpected, "Expected " & po.ExpectedDate.ToString("MMM d, yyyy"), "No expected date") &
                    "   " & ChrW(8226) & "   " & po.ItemCount & If(po.ItemCount = 1, " item", " items"),
            .Font = New Font("Segoe UI", 9), .ForeColor = FaintText, .AutoSize = True,
            .Location = New Point(300, 28), .BackColor = Color.Transparent,
            .Anchor = AnchorStyles.Left Or AnchorStyles.Top
        }
        card.Controls.AddRange({lblPo, lblSupplier, lblDates})

        ' Status badge
        Dim st = StatusInfo(po.Status)
        Dim badge As New Label With {
            .Text = st.text, .Font = New Font("Segoe UI", 8.5F, FontStyle.Bold),
            .ForeColor = st.fore, .BackColor = st.back, .AutoSize = True,
            .Padding = New Padding(9, 4, 9, 4), .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        RoundLabel(badge, 11)
        card.Controls.Add(badge)

        ' Total cost
        Dim lblTotal As New Label With {
            .Text = Peso & po.TotalCost.ToString("N2"), .Font = New Font("Segoe UI", 13, FontStyle.Bold),
            .ForeColor = MintPrimary, .AutoSize = True, .BackColor = Color.Transparent,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        card.Controls.Add(lblTotal)

        ' View link
        Dim lnkView As Label = MakeLink("View", MintPrimary)
        lnkView.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        card.Controls.Add(lnkView)

        Dim capId As Integer = po.PoId
        AddHandler lnkView.Click, Sub() LoadDetail(capId)
        AddHandler card.Click, Sub() LoadDetail(capId)
        AddHandler lblPo.Click, Sub() LoadDetail(capId)
        AddHandler lblSupplier.Click, Sub() LoadDetail(capId)
        AddHandler lblDates.Click, Sub() LoadDetail(capId)

        AddHandler card.Resize,
            Sub()
                badge.Left = card.Width - badge.Width - 22
                badge.Top = 16
                lblTotal.Left = card.Width - lblTotal.Width - 22
                lblTotal.Top = 40
                lnkView.Left = card.Width - lnkView.Width - 22
                lnkView.Top = 16
                ' keep total/badge from colliding with the link on the same right edge
                badge.Left = lnkView.Left - badge.Width - 14
            End Sub

        Return card
    End Function

    ' =========================================================================
    '  VIEW 2 — NEW PO FORM
    ' =========================================================================
    Private Shared Sub LoadNewPOForm(preloadRequestId As Integer)
        Dim container As Panel = containerRef
        If container Is Nothing Then Return

        newLines = New List(Of POLine)()

        container.SuspendLayout()
        container.Controls.Clear()
        container.BackColor = MintBg

        ' ---- Main 2-column area ----
        Dim mainGrid As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 1,
            .BackColor = MintBg, .Padding = New Padding(24, 4, 24, 20),
            .CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        }
        mainGrid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 34.0F))
        mainGrid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 66.0F))
        mainGrid.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))

        Dim leftCard As Panel = BuildPendingPicker()
        leftCard.Margin = New Padding(0, 0, 8, 0)
        Dim rightHost As Panel = BuildLineEditor()
        rightHost.Margin = New Padding(8, 0, 0, 0)
        mainGrid.Controls.Add(leftCard, 0, 0)
        mainGrid.Controls.Add(rightHost, 1, 0)

        ' ---- Header bar (title + back + save) ----
        Dim header As Panel = BuildFormHeader()
        header.Dock = DockStyle.Top

        container.Controls.Add(mainGrid)
        container.Controls.Add(header)
        container.ResumeLayout()

        ' ---- Optional preload ----
        If preloadRequestId > 0 Then
            AddLineFromRequestId(preloadRequestId)
        End If
        RebuildLines()
        RecomputeGrand()
    End Sub

    Private Shared Function BuildFormHeader() As Panel
        Dim region As New Panel With {.Height = 80, .BackColor = MintBg, .Padding = New Padding(0, 0, 24, 0)}

        ' ← chevron icon (previous.png)
        Dim iconPath As String = IO.Path.Combine(Application.StartupPath, "previous.png")
        Dim picBack As New PictureBox With {
            .Size = New Size(22, 22),
            .Location = New Point(24, 34),
            .BackColor = Color.Transparent,
            .SizeMode = PictureBoxSizeMode.Zoom,
            .Cursor = Cursors.Hand
        }
        If IO.File.Exists(iconPath) Then
            Try
                Dim raw As Image = Image.FromFile(iconPath)
                ' Tint to MintDeep by re-drawing with a colour matrix
                Dim bmp As New Bitmap(raw.Width, raw.Height)
                Using g As Graphics = Graphics.FromImage(bmp)
                    Dim cm As New Imaging.ColorMatrix(New Single()() {
                        New Single() {0, 0, 0, 0, 0},
                        New Single() {0, 0, 0, 0, 0},
                        New Single() {0, 0, 0, 0, 0},
                        New Single() {0, 0, 0, 1, 0},
                        New Single() {CSng(0 / 255.0F), CSng(130 / 255.0F), CSng(109 / 255.0F), 0, 1}
                    })
                    Dim ia As New Imaging.ImageAttributes()
                    ia.SetColorMatrix(cm)
                    g.DrawImage(raw, New Rectangle(0, 0, bmp.Width, bmp.Height),
                                0, 0, raw.Width, raw.Height, GraphicsUnit.Pixel, ia)
                End Using
                picBack.Image = bmp
                raw.Dispose()
            Catch
                picBack.Image = Nothing
            End Try
        End If
        AddHandler picBack.Click, Sub() RenderList()
        AddHandler picBack.MouseEnter,
            Sub()
                picBack.BackColor = Color.FromArgb(30, 0, 130, 109)
                picBack.Padding = New Padding(2)
            End Sub
        AddHandler picBack.MouseLeave,
            Sub()
                picBack.BackColor = Color.Transparent
                picBack.Padding = New Padding(0)
            End Sub

        Dim lblTitle As New Label With {
            .Text = "New Purchase Order", .Font = New Font("Segoe UI", 22, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True,
            .Location = New Point(picBack.Right + 10, 22),
            .BackColor = Color.Transparent
        }


        btnSaveRef = New Button With {
            .Text = "Save Purchase Order", .Size = New Size(210, 44),
            .FlatStyle = FlatStyle.Flat, .BackColor = MintPrimary, .ForeColor = Color.White,
            .Font = New Font("Segoe UI Semibold", 11), .Cursor = Cursors.Hand,
            .TextAlign = ContentAlignment.MiddleCenter,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        btnSaveRef.FlatAppearance.BorderSize = 0
        RoundControl(btnSaveRef, 12)
        AddHover(btnSaveRef, MintPrimary, MintHover)
        AddHandler btnSaveRef.Click, Sub() CommitNewPO()

        region.Controls.AddRange({picBack, lblTitle, btnSaveRef})
        AddHandler region.Resize, Sub()
                                      btnSaveRef.Top = 18
                                      btnSaveRef.Left = region.Width - 24 - btnSaveRef.Width
                                  End Sub
        btnSaveRef.Top = 18
        btnSaveRef.Left = 700
        Return region
    End Function

    ' ---- Left: pending-requests picker ----
    Private Shared Function BuildPendingPicker() As Panel
        Dim card As New BufferedPanel With {.Dock = DockStyle.Fill, .BackColor = CardBg}
        AddHandler card.Paint, Sub(s, e) PaintCard(CType(s, Panel), e)

        Dim pendings As List(Of PendingReqLite) = LoadPendingLite()

        Dim header As Panel = BuildCardHeader("Pending Requests",
            If(pendings.Count = 0, "Nothing waiting",
               pendings.Count & If(pendings.Count = 1, " request waiting", " requests waiting") &
               "  " & ChrW(8226) & "  tap to add"))
        header.Dock = DockStyle.Top

        Dim rowsHost As New Panel With {
            .Dock = DockStyle.Fill, .BackColor = Color.Transparent, .AutoScroll = True,
            .Padding = New Padding(14, 12, 14, 12)
        }
        card.Controls.Add(rowsHost)
        card.Controls.Add(header)
        rowsHost.BringToFront()

        If pendings.Count = 0 Then
            Dim lbl As New Label With {
                .Text = "No pending requests right now." & Environment.NewLine &
                        "Use the product search to add items manually.",
                .Font = New Font("Segoe UI", 11), .ForeColor = SubText, .AutoSize = False,
                .TextAlign = ContentAlignment.TopCenter, .Dock = DockStyle.Top, .Height = 60,
                .BackColor = Color.Transparent
            }
            rowsHost.Controls.Add(lbl)
        Else
            Dim y As Integer = 4
            For Each req In pendings
                Dim row As Panel = BuildPendingPickRow(req, 78)
                row.Location = New Point(0, y)
                row.Width = rowsHost.ClientSize.Width - 4
                row.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
                rowsHost.Controls.Add(row)
                y += 88
            Next
            AddHandler rowsHost.Resize,
                Sub()
                    Dim w As Integer = rowsHost.ClientSize.Width - 4
                    For Each c As Control In rowsHost.Controls
                        c.Width = w
                    Next
                End Sub
        End If

        Return card
    End Function

    Private Shared Function BuildPendingPickRow(req As PendingReqLite, height As Integer) As Panel
        Dim row As New BufferedPanel With {.Height = height, .BackColor = Color.Transparent, .Cursor = Cursors.Hand}
        Dim hovered As Boolean = False
        Dim initial As String = If(String.IsNullOrWhiteSpace(req.ProductName), "?",
                                   req.ProductName.Trim().Substring(0, 1).ToUpperInvariant())

        AddHandler row.Paint,
            Sub(s, e)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias

                ' card tile
                Dim rect As New Rectangle(2, 2, row.Width - 6, row.Height - 6)
                Using path As GraphicsPath = RoundRect(rect, 12)
                    Using b As New SolidBrush(If(hovered, Color.FromArgb(228, 246, 239), TileBg))
                        g.FillPath(b, path)
                    End Using
                    Using pen As New Pen(If(hovered, Color.FromArgb(140, 212, 192), Color.FromArgb(223, 238, 233)), 1.2F)
                        g.DrawPath(pen, path)
                    End Using
                End Using

                ' avatar (rounded square, mint, white initial)
                Dim av As New Rectangle(16, (row.Height - 44) \ 2, 44, 44)
                Using path As GraphicsPath = RoundRect(av, 13)
                    e.Graphics.SetClip(path)
                    Dim imgLoaded As Boolean = False
                    If Not String.IsNullOrWhiteSpace(req.ImagePath) Then
                        Try
                            Dim fullPath As String = req.ImagePath
                            If Not IO.Path.IsPathRooted(fullPath) Then
                                fullPath = IO.Path.Combine(Application.StartupPath, fullPath)
                            End If
                            If IO.File.Exists(fullPath) Then
                                Using img As Image = Image.FromFile(fullPath)
                                    g.DrawImage(img, av)
                                End Using
                                imgLoaded = True
                            End If
                        Catch
                        End Try
                    End If
                    e.Graphics.ResetClip()
                    If Not imgLoaded Then
                        Using br As New LinearGradientBrush(av, MintPrimary, MintDeep, 90.0F)
                            g.FillPath(br, path)
                        End Using
                        Using f As New Font("Segoe UI Semibold", 16, FontStyle.Bold), br As New SolidBrush(Color.White)
                            Dim sz = g.MeasureString(initial, f)
                            g.DrawString(initial, f, br, av.X + (av.Width - sz.Width) / 2.0F, av.Y + (av.Height - sz.Height) / 2.0F)
                        End Using
                    End If
                End Using
            End Sub
        AddHandler row.MouseEnter, Sub()
                                       hovered = True
                                       row.Invalidate()
                                   End Sub
        AddHandler row.MouseLeave, Sub()
                                       hovered = False
                                       row.Invalidate()
                                   End Sub

        Const textX As Integer = 74

        Dim lblName As New Label With {
            .Text = req.ProductName, .Font = New Font("Segoe UI", 12, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(textX, 16),
            .BackColor = Color.Transparent
        }

        ' Qty chip
        Dim pillQty As New Label With {
            .Text = "Qty " & req.QtyRequested, .Font = New Font("Segoe UI Semibold", 8.5F),
            .ForeColor = MintDeep, .BackColor = Color.FromArgb(210, 238, 229),
            .AutoSize = True, .Padding = New Padding(8, 3, 8, 3),
            .Location = New Point(textX, 43)
        }
        RoundLabel(pillQty, 9)

        Dim lblBy As New Label With {
            .Text = ChrW(8226) & "  " & req.RequestedBy, .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent
        }

        ' Add pill
        Dim lblAdd As New Label With {
            .Text = "+  Add", .Font = New Font("Segoe UI Semibold", 10),
            .ForeColor = Color.White, .BackColor = MintPrimary,
            .AutoSize = False, .TextAlign = ContentAlignment.MiddleCenter,
            .Cursor = Cursors.Hand, .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        RoundLabel(lblAdd, 9)
        lblAdd.Size = New Size(74, 34)   ' set AFTER RoundLabel so region applies
        AddHandler lblAdd.MouseEnter, Sub()
                                          lblAdd.BackColor = MintHover
                                          hovered = True : row.Invalidate()
                                      End Sub
        AddHandler lblAdd.MouseLeave, Sub()
                                          lblAdd.BackColor = MintPrimary
                                          hovered = False : row.Invalidate()
                                      End Sub

        row.Controls.AddRange({lblName, pillQty, lblBy, lblAdd})

        Dim capReq As PendingReqLite = req
        Dim doAdd As Action = Sub() AddLineFromRequest(capReq)
        AddHandler row.Click, Sub() doAdd()
        AddHandler lblName.Click, Sub() doAdd()
        AddHandler pillQty.Click, Sub() doAdd()
        AddHandler lblBy.Click, Sub() doAdd()
        AddHandler lblAdd.Click, Sub() doAdd()

        AddHandler row.Resize,
            Sub()
                lblAdd.Left = row.Width - lblAdd.Width - 18
                lblAdd.Top = (height - lblAdd.Height) \ 2
                lblBy.Left = pillQty.Right + 8
                lblBy.Top = pillQty.Top + (pillQty.Height - lblBy.Height) \ 2
            End Sub
        ' initial placement
        lblBy.Left = pillQty.Right + 8
        lblBy.Top = pillQty.Top + (pillQty.Height - lblBy.Height) \ 2

        Return row
    End Function


    ' ---- Right: supplier/date settings + add bar + lines + grand total ----
    ' ---- Right: settings card on top + one unified Items card (add + rows + total) ----
    Private Shared Function BuildLineEditor() As Panel
        Dim host As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.Transparent}

        ' Items card fills the rest — contains header(+add), rows, and grand-total footer
        Dim itemsCard As New BufferedPanel With {.Dock = DockStyle.Fill, .BackColor = CardBg}
        AddHandler itemsCard.Paint, Sub(s, e) PaintCard(CType(s, Panel), e)

        ' --- grand-total footer (bottom, inside the card): dark mint gradient
        '     "TOTAL" card, mirroring the POS Transaction screen ---
        ' Inset the gradient pill well inside the card so it never overlaps the
        ' card's rounded bottom border, and keep the footer short enough that it
        ' doesn't clip the scrolling line rows above it.
        Dim grandBar As New BufferedPanel With {
            .Height = 84, .Dock = DockStyle.Bottom, .BackColor = Color.Transparent,
            .Padding = New Padding(20, 10, 22, 16)
        }
        AddHandler grandBar.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim r As New Rectangle(
                    grandBar.Padding.Left, grandBar.Padding.Top,
                    Math.Max(0, grandBar.Width - grandBar.Padding.Horizontal - 1),
                    Math.Max(0, grandBar.Height - grandBar.Padding.Vertical - 1))
                If r.Width <= 4 OrElse r.Height <= 4 Then Return
                Using path As GraphicsPath = RoundRect(r, 14)
                    Using br As New LinearGradientBrush(r, MintDark, TotalDeep, 90.0F)
                        e.Graphics.FillPath(br, path)
                    End Using
                End Using
            End Sub

        Dim lblCap As New Label With {
            .Text = "TOTAL", .Font = New Font("Segoe UI", 14, FontStyle.Bold),
            .ForeColor = Color.FromArgb(200, 240, 225), .AutoSize = True, .BackColor = Color.Transparent
        }
        lblGrandRef = New Label With {
            .Text = Peso & "0.00", .Font = New Font("Segoe UI", 22, FontStyle.Bold),
            .ForeColor = GoldValue, .AutoSize = True, .BackColor = Color.Transparent
        }
        grandBar.Controls.AddRange({lblCap, lblGrandRef})

        ' Center both labels inside the gradient card (not the whole footer panel)
        Dim layoutGrand As Action =
            Sub()
                Dim padL As Integer = grandBar.Padding.Left
                Dim padR As Integer = grandBar.Padding.Right
                Dim padT As Integer = grandBar.Padding.Top
                Dim cardH As Integer = grandBar.Height - grandBar.Padding.Vertical
                Dim pillRight As Integer = grandBar.Width - padR

                lblCap.Left = padL + 18
                lblCap.Top = padT + (cardH - lblCap.Height) \ 2

                Dim valX As Integer = pillRight - 18 - lblGrandRef.Width
                ' never let the value cross into the caption
                Dim minX As Integer = lblCap.Right + 16
                If valX < minX Then valX = minX
                lblGrandRef.Left = valX
                lblGrandRef.Top = padT + (cardH - lblGrandRef.Height) \ 2
            End Sub
        grandBar.Tag = layoutGrand          ' RecomputeGrand re-runs this on text change
        AddHandler grandBar.Resize, Sub() layoutGrand()

        ' --- header with title + inline add control (top, inside the card) ---
        Dim headerPanel As New Panel With {.Height = 92, .Dock = DockStyle.Top, .BackColor = Color.Transparent}
        Dim lblItems As New Label With {
        .Text = "Line Items", .Font = New Font("Segoe UI", 15, FontStyle.Bold),
        .ForeColor = MintDark, .AutoSize = True, .Location = New Point(20, 14), .BackColor = Color.Transparent
    }
        cmbProductRef = New ComboBox With {
        .DropDownStyle = ComboBoxStyle.DropDown, .FlatStyle = FlatStyle.Flat,
        .Font = New Font("Segoe UI", 11), .BackColor = CardBg,
        .AutoCompleteMode = AutoCompleteMode.SuggestAppend,
        .AutoCompleteSource = AutoCompleteSource.ListItems
    }
        For Each p In LoadProducts() : cmbProductRef.Items.Add(p) : Next
        Dim prodField As Panel = MakeField(cmbProductRef, 360)
        prodField.Location = New Point(20, 46)

        Dim btnAdd As New Button With {
        .Text = "+  Add Product", .Size = New Size(150, 46),
        .FlatStyle = FlatStyle.Flat, .BackColor = MintDeep, .ForeColor = Color.White,
        .Font = New Font("Segoe UI Semibold", 11), .Cursor = Cursors.Hand,
        .TextAlign = ContentAlignment.MiddleCenter, .Location = New Point(388, 46),
        .Anchor = AnchorStyles.Top Or AnchorStyles.Left
    }
        btnAdd.FlatAppearance.BorderSize = 0
        RoundControl(btnAdd, 10)
        AddHover(btnAdd, MintDeep, DeepHover)
        AddHandler btnAdd.Click, Sub() AddLineFromCombo()

        Dim headDiv As New Panel With {.Height = 1, .BackColor = RowDivider, .Dock = DockStyle.Bottom}
        headerPanel.Controls.AddRange({lblItems, prodField, btnAdd, headDiv})

        ' --- rows region (fill, inside the card) ---
        linesHostRef = New Panel With {
        .Dock = DockStyle.Fill, .BackColor = Color.Transparent, .AutoScroll = True,
        .Padding = New Padding(0)
    }
        AddHandler linesHostRef.Resize,
        Sub()
            Dim w As Integer = linesHostRef.ClientSize.Width - 28
            For Each c As Control In linesHostRef.Controls
                c.Width = w
                c.Left = 14
            Next
        End Sub

        itemsCard.Controls.Add(linesHostRef)   ' fill first
        itemsCard.Controls.Add(headerPanel)    ' top
        itemsCard.Controls.Add(grandBar)       ' bottom
        linesHostRef.BringToFront()

        ' Settings card on top of the whole right column
        Dim settings As Panel = BuildSettingsCard()
        settings.Dock = DockStyle.Top

        Dim spacer As New Panel With {.Height = 14, .Dock = DockStyle.Top, .BackColor = Color.Transparent}

        host.Controls.Add(itemsCard)
        host.Controls.Add(spacer)
        host.Controls.Add(settings)
        Return host
    End Function

    Private Shared Function BuildSettingsCard() As Panel
        Dim card As New BufferedPanel With {.Height = 166, .BackColor = CardBg}
        AddHandler card.Paint, Sub(s, e) PaintCard(CType(s, Panel), e)

        Dim lblSection As New Label With {
            .Text = "Order Details", .Font = New Font("Segoe UI", 15, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(22, 16),
            .BackColor = Color.Transparent
        }
        Dim lblHint As New Label With {
            .Text = "Who you're ordering from, and when.",
            .Font = New Font("Segoe UI", 9.5F), .ForeColor = FaintText, .AutoSize = True,
            .Location = New Point(22, 42), .BackColor = Color.Transparent
        }

        Dim lblSup As Label = MakeFieldLabel("Supplier")
        Dim lblOrder As Label = MakeFieldLabel("Order Date")
        Dim lblExp As Label = MakeFieldLabel("Expected Date")

        ' --- Supplier ---
        cmbSupplierRef = New ComboBox With {
            .DropDownStyle = ComboBoxStyle.DropDownList, .FlatStyle = FlatStyle.Flat,
            .Font = New Font("Segoe UI", 11), .BackColor = CardBg
        }
        For Each s In LoadSuppliers() : cmbSupplierRef.Items.Add(s) : Next
        If cmbSupplierRef.Items.Count > 0 Then cmbSupplierRef.SelectedIndex = 0
        Dim supField As Panel = MakeField(cmbSupplierRef, 320, 48)

        ' --- Order Date ---
        dtOrderRef = New DateTimePicker With {
            .Format = DateTimePickerFormat.Custom, .CustomFormat = "MMM d, yyyy",
            .Font = New Font("Segoe UI", 11), .Value = DateTime.Today
        }
        ThemeCalendar(dtOrderRef)
        Dim orderField As Panel = MakeField(dtOrderRef, 170, 48)

        ' --- Expected Date ---
        dtExpectedRef = New DateTimePicker With {
            .Format = DateTimePickerFormat.Custom, .CustomFormat = "MMM d, yyyy",
            .Font = New Font("Segoe UI", 11), .ShowCheckBox = True, .Checked = True,
            .Value = DateTime.Today.AddDays(7)
        }
        ThemeCalendar(dtExpectedRef)
        Dim expField As Panel = MakeField(dtExpectedRef, 210, 48)

        card.Controls.AddRange({lblSection, lblHint, lblSup, supField, lblOrder, orderField, lblExp, expField})

        ' force native controls to repaint at their final width (kills the ghost arrow)
        Dim repaintFields As Action =
            Sub()
                For Each fld As Control In New Control() {supField, orderField, expField}
                    fld.Invalidate()
                    If fld.Controls.Count > 0 Then fld.Controls(0).Refresh()
                Next
            End Sub

        Dim doLayout As Action =
            Sub()
                If card.Width < 120 Then Return
                Const padL As Integer = 22
                Const padR As Integer = 22
                Const gap As Integer = 16
                Const labY As Integer = 74
                Const fldY As Integer = 98
                Const fldH As Integer = 48

                Dim avail As Integer = card.Width - padL - padR - gap * 2
                Dim supW As Integer = CInt(avail * 0.4)
                Dim orderW As Integer = CInt(avail * 0.28)
                Dim expW As Integer = avail - supW - orderW

                If orderW < 150 Then orderW = 150
                If expW < 190 Then expW = 190
                supW = avail - orderW - expW
                If supW < 170 Then supW = 170

                Dim x As Integer = padL
                lblSup.Location = New Point(x, labY)
                supField.SetBounds(x, fldY, supW, fldH)

                x = supField.Right + gap
                lblOrder.Location = New Point(x, labY)
                orderField.SetBounds(x, fldY, orderW, fldH)

                x = orderField.Right + gap
                lblExp.Location = New Point(x, labY)
                expField.SetBounds(x, fldY, expW, fldH)

                repaintFields()
            End Sub

        AddHandler card.Resize, Sub() doLayout()
        AddHandler card.HandleCreated,
            Sub()
                ' run after the message pump settles, then once more, repainting each time
                card.BeginInvoke(Sub()
                                     doLayout()
                                     card.BeginInvoke(Sub()
                                                          doLayout()
                                                          repaintFields()
                                                      End Sub)
                                 End Sub)
            End Sub
        doLayout()
        Return card
    End Function

    ' Rounded, bordered field wrapper. Inner control is resized to fill the
    ' wrapper AND invalidated on every resize so its dropdown button never ghosts.
    Private Shared Function MakeField(inner As Control, w As Integer, Optional h As Integer = 46) As BufferedPanel
        Dim host As New BufferedPanel With {.Size = New Size(w, h), .BackColor = CardBg}
        Dim isFocused As Boolean = False

        AddHandler host.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
                Dim rect As New Rectangle(0, 0, host.Width - 1, host.Height - 1)
                Using path As Drawing2D.GraphicsPath = RoundRect(rect, 10)
                    Using b As New SolidBrush(CardBg)
                        e.Graphics.FillPath(b, path)
                    End Using
                    Using pen As New Pen(If(isFocused, InputFocus, InputBorder), If(isFocused, 1.6F, 1.0F))
                        e.Graphics.DrawPath(pen, path)
                    End Using
                End Using
            End Sub

        inner.BackColor = CardBg
        inner.Width = w - 22
        inner.Location = New Point(11, (h - inner.Height) \ 2)
        host.Controls.Add(inner)

        ' Resize the inner control explicitly, then repaint it (no ghost arrow).
        AddHandler host.Resize,
            Sub()
                inner.Left = 11
                inner.Top = (host.Height - inner.Height) \ 2
                inner.Width = Math.Max(20, host.Width - 22)
                inner.Refresh()      ' synchronous repaint clears the stale dropdown button
                host.Invalidate()
            End Sub

        AddHandler inner.Enter, Sub()
                                    isFocused = True
                                    host.Invalidate()
                                End Sub
        AddHandler inner.Leave, Sub()
                                    isFocused = False
                                    host.Invalidate()
                                End Sub
        Return host
    End Function


    ' Wrapper now keeps the inner control sized to the wrapper on every resize,
    ' so fields built with MakeField can flex when their bounds change.

    Private Shared Function BuildAddBar() As Panel
        Dim bar As New Panel With {.Height = 80, .BackColor = Color.Transparent}

        Dim lbl As Label = MakeFieldLabel("Add Item")
        lbl.Location = New Point(2, 4)

        cmbProductRef = New ComboBox With {
            .DropDownStyle = ComboBoxStyle.DropDown, .FlatStyle = FlatStyle.Flat,
            .Font = New Font("Segoe UI", 11), .BackColor = CardBg,
            .AutoCompleteMode = AutoCompleteMode.SuggestAppend,
            .AutoCompleteSource = AutoCompleteSource.ListItems
        }
        For Each p In LoadProducts() : cmbProductRef.Items.Add(p) : Next
        Dim prodField As Panel = MakeField(cmbProductRef, 380)
        prodField.Location = New Point(0, 28)

        Dim btnAdd As New Button With {
            .Text = "+  Add Product", .Size = New Size(150, 46),
            .FlatStyle = FlatStyle.Flat, .BackColor = MintDeep, .ForeColor = Color.White,
            .Font = New Font("Segoe UI Semibold", 11), .Cursor = Cursors.Hand,
            .TextAlign = ContentAlignment.MiddleCenter,
            .Location = New Point(392, 28)
        }
        btnAdd.FlatAppearance.BorderSize = 0
        RoundControl(btnAdd, 10)
        AddHover(btnAdd, MintDeep, DeepHover)
        AddHandler btnAdd.Click, Sub() AddLineFromCombo()

        bar.Controls.AddRange({lbl, prodField, btnAdd})
        Return bar
    End Function

    Private Shared Function BuildGrandBar() As Panel
        Dim bar As New BufferedPanel With {.Height = 68, .BackColor = CardBg}
        AddHandler bar.Paint, Sub(s, e) PaintCard(CType(s, Panel), e)

        Dim lblCap As New Label With {
            .Text = "Grand Total", .Font = New Font("Segoe UI", 13, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(20, 22), .BackColor = Color.Transparent
        }
        lblGrandRef = New Label With {
            .Text = Peso & "0.00", .Font = New Font("Segoe UI", 19, FontStyle.Bold),
            .ForeColor = MintPrimary, .AutoSize = True, .BackColor = Color.Transparent,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        bar.Controls.AddRange({lblCap, lblGrandRef})
        AddHandler bar.Resize,
            Sub()
                lblGrandRef.Left = bar.Width - lblGrandRef.Width - 22
                lblGrandRef.Top = 18
            End Sub
        Return bar
    End Function

    ' ---- Line rows ----
    Private Shared Sub RebuildLines()
        If linesHostRef Is Nothing Then Return
        linesHostRef.SuspendLayout()
        linesHostRef.Controls.Clear()

        ' Empty state — dashed placeholder card
        If newLines.Count = 0 Then
            Dim ph As New BufferedPanel With {.BackColor = Color.Transparent}
            AddHandler ph.Paint,
                Sub(s, e)
                    e.Graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
                    Dim r As New Rectangle(2, 2, ph.Width - 6, ph.Height - 6)
                    Using pen As New Pen(InputBorder, 1.4F)
                        pen.DashStyle = Drawing2D.DashStyle.Dash
                        Using path As Drawing2D.GraphicsPath = RoundRect(r, 12)
                            e.Graphics.DrawPath(pen, path)
                        End Using
                    End Using
                End Sub
            ph.Location = New Point(0, 8)
            ph.Size = New Size(linesHostRef.ClientSize.Width - 4, 130)
            ph.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
            Dim lbl As New Label With {
                .Text = "No line items yet" & Environment.NewLine &
                        "Add from a pending request or search a product above.",
                .Font = New Font("Segoe UI", 12), .ForeColor = SubText, .AutoSize = False,
                .TextAlign = ContentAlignment.MiddleCenter, .Dock = DockStyle.Fill,
                .BackColor = Color.Transparent
            }
            ph.Controls.Add(lbl)
            linesHostRef.Controls.Add(ph)
            linesHostRef.ResumeLayout()
            Return
        End If

        ' --- render one row per line item ---
        Dim y As Integer = 8
        For Each ln In newLines
            Dim row As Panel = BuildLineRow(ln, 66)
            row.Location = New Point(14, y)
            row.Width = linesHostRef.ClientSize.Width - 28
            row.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
            linesHostRef.Controls.Add(row)
            y += 70
        Next

        linesHostRef.ResumeLayout()

        ' Re-apply widths and re-run each row's layout NOW and again after the
        ' message pump settles. Double-deferred so the second pass runs after
        ' the docking/anchoring engine has fully resolved all panel widths.
        Dim hostLocal As Panel = linesHostRef
        Dim applyWidths As Action =
            Sub()
                If hostLocal Is Nothing OrElse hostLocal.IsDisposed Then Return
                Dim finalW As Integer = hostLocal.ClientSize.Width - 28
                For Each c As Control In hostLocal.Controls
                    c.Left = 14
                    c.Width = finalW
                    Dim relayout = TryCast(c.Tag, Action)
                    If relayout IsNot Nothing Then relayout()
                Next
            End Sub

        applyWidths()
        If hostLocal.IsHandleCreated Then
            hostLocal.BeginInvoke(Sub()
                                      applyWidths()
                                      hostLocal.BeginInvoke(Sub() applyWidths())
                                  End Sub)
        End If
    End Sub




    Private Shared Function BuildLineRow(ln As POLine, height As Integer) As Panel
        Dim row As New BufferedPanel With {.Height = height, .BackColor = Color.Transparent}
        AddHandler row.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect As New Rectangle(0, 0, row.Width - 1, row.Height - 1)
                Using path As GraphicsPath = RoundRect(rect, 10)
                    Using b As New SolidBrush(TileBg)
                        e.Graphics.FillPath(b, path)
                    End Using
                    Using pen As New Pen(Color.FromArgb(159, 225, 203), 1.2F)
                        e.Graphics.DrawPath(pen, path)
                    End Using
                End Using
            End Sub

        Dim capLine As POLine = ln

        ' --- left: name + locked "unit x qty = total" calc line ---
        Dim lblName As New Label With {
            .Text = ln.ProductName, .Font = New Font("Segoe UI", 11.5F, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = False, .BackColor = Color.Transparent,
            .TextAlign = ContentAlignment.MiddleLeft,
            .Location = New Point(16, 11), .Size = New Size(240, 22)
        }
        Dim lblCalc As New Label With {
            .Text = "", .Font = New Font("Segoe UI", 9.5F), .ForeColor = SubText,
            .AutoSize = False, .BackColor = Color.Transparent,
            .TextAlign = ContentAlignment.MiddleLeft,
            .Location = New Point(16, 37), .Size = New Size(260, 18)
        }
        row.Controls.AddRange({lblName, lblCalc})

        Dim refreshCalc As Action =
            Sub()
                Dim src As String = If(capLine.RequestId > 0, "    " & ChrW(8226) & " from request", "")
                lblCalc.Text = Peso & capLine.UnitCost.ToString("N2") & "   " & ChrW(215) & "   " &
                               capLine.Qty & "   =   " & Peso & capLine.LineTotal.ToString("N2") & src
            End Sub

        ' --- qty stepper (editable: - value + , or type in the middle) ---
        Dim stepPanel As New BufferedPanel With {.Size = New Size(108, 32), .BackColor = CardBg}
        AddHandler stepPanel.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim r As New Rectangle(0, 0, stepPanel.Width - 1, stepPanel.Height - 1)
                Using path As GraphicsPath = RoundRect(r, 8)
                    Using b As New SolidBrush(CardBg) : e.Graphics.FillPath(b, path) : End Using
                    Using pen As New Pen(InputBorder, 1.2F) : e.Graphics.DrawPath(pen, path) : End Using
                End Using
            End Sub

        Dim btnMinus As New Button With {
            .Text = ChrW(8722), .Size = New Size(30, 30), .Location = New Point(1, 1),
            .FlatStyle = FlatStyle.Flat, .Font = New Font("Segoe UI", 13),
            .ForeColor = SubText, .BackColor = Color.Transparent, .Cursor = Cursors.Hand, .TabStop = False
        }
        btnMinus.FlatAppearance.BorderSize = 0

        Dim txtQty As New TextBox With {
            .Text = ln.Qty.ToString(), .Width = 44, .Location = New Point(32, 5),
            .TextAlign = HorizontalAlignment.Center, .BorderStyle = BorderStyle.None,
            .Font = New Font("Segoe UI Semibold", 11), .ForeColor = MintDark, .BackColor = CardBg
        }

        Dim btnPlus As New Button With {
            .Text = "+", .Size = New Size(30, 30), .Location = New Point(77, 1),
            .FlatStyle = FlatStyle.Flat, .Font = New Font("Segoe UI", 13),
            .ForeColor = MintPrimary, .BackColor = Color.Transparent, .Cursor = Cursors.Hand, .TabStop = False
        }
        btnPlus.FlatAppearance.BorderSize = 0
        stepPanel.Controls.AddRange({btnMinus, txtQty, btnPlus})

        ' guard prevents the programmatic SetText from re-firing TextChanged work
        Dim guard As Boolean = False
        Dim commitQty As Action(Of Integer) =
            Sub(q As Integer)
                If q < 1 Then q = 1
                capLine.Qty = q
                guard = True
                txtQty.Text = q.ToString()
                guard = False
                refreshCalc()
                RecomputeGrand()
            End Sub

        AddHandler btnMinus.Click, Sub() commitQty(capLine.Qty - 1)
        AddHandler btnPlus.Click, Sub() commitQty(capLine.Qty + 1)
        AddHandler txtQty.KeyPress,
            Sub(s, e)
                If Not Char.IsControl(e.KeyChar) AndAlso Not Char.IsDigit(e.KeyChar) Then e.Handled = True
            End Sub
        AddHandler txtQty.Enter, Sub() txtQty.SelectAll()
        AddHandler txtQty.TextChanged,
            Sub()
                If guard Then Return
                capLine.Qty = SafeIntStr(txtQty.Text)   ' may be 0 mid-typing; normalised on Leave
                refreshCalc()
                RecomputeGrand()
            End Sub
        AddHandler txtQty.Leave, Sub() commitQty(SafeIntStr(txtQty.Text))

        ' --- remove (circular x) ---
        Dim btnRemove As New Button With {
            .Text = ChrW(10005), .Size = New Size(28, 28),
            .FlatStyle = FlatStyle.Flat, .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold),
            .ForeColor = NegativeClr, .BackColor = Color.FromArgb(252, 235, 235),
            .Cursor = Cursors.Hand, .TabStop = False
        }
        btnRemove.FlatAppearance.BorderSize = 0
        AddHandler btnRemove.Paint,
            Sub(s, e)
                Dim bb = CType(s, Button)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Using br As New SolidBrush(bb.BackColor)
                    e.Graphics.FillEllipse(br, 0, 0, bb.Width - 1, bb.Height - 1)
                End Using
                TextRenderer.DrawText(e.Graphics, bb.Text, bb.Font, bb.ClientRectangle, bb.ForeColor,
                    TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub
        AddHandler btnRemove.MouseEnter, Sub()
                                             btnRemove.BackColor = NegativeClr
                                             btnRemove.ForeColor = Color.White
                                             btnRemove.Invalidate()
                                         End Sub
        AddHandler btnRemove.MouseLeave, Sub()
                                             btnRemove.BackColor = Color.FromArgb(252, 235, 235)
                                             btnRemove.ForeColor = NegativeClr
                                             btnRemove.Invalidate()
                                         End Sub
        AddHandler btnRemove.Click,
            Sub()
                newLines.Remove(capLine)
                RebuildLines()
                RecomputeGrand()
            End Sub

        row.Controls.AddRange({stepPanel, btnRemove})

        ' --- layout (right-aligned stepper + delete; text fills the rest) ---
        Dim layoutRow As Action =
            Sub()
                If row.Width < 60 Then Return
                Dim rightPad As Integer = 16
                btnRemove.Left = row.Width - rightPad - btnRemove.Width
                btnRemove.Top = (height - btnRemove.Height) \ 2

                stepPanel.Left = btnRemove.Left - 14 - stepPanel.Width
                stepPanel.Top = (height - stepPanel.Height) \ 2

                Dim textRight As Integer = stepPanel.Left - 12
                lblName.Width = Math.Max(60, textRight - lblName.Left)
                lblCalc.Width = Math.Max(60, textRight - lblCalc.Left)
            End Sub
        AddHandler row.Resize, Sub() layoutRow()
        AddHandler row.SizeChanged, Sub() layoutRow()
        row.Tag = layoutRow
        AddHandler row.HandleCreated,
            Sub()
                row.BeginInvoke(Sub()
                                    layoutRow()
                                    row.Invalidate()
                                End Sub)
            End Sub

        refreshCalc()
        Return row
    End Function

    Private Shared Sub RecomputeGrand()
        If lblGrandRef Is Nothing Then Return
        Dim total As Decimal = 0D
        For Each ln In newLines
            total += ln.LineTotal
        Next
        lblGrandRef.Text = Peso & total.ToString("N2")

        ' The label width changes with the total; re-run the grand bar's own
        ' layout so the gold value stays centered and pinned inside the card.
        Dim parent As Control = lblGrandRef.Parent
        If parent IsNot Nothing Then
            Dim relayout = TryCast(parent.Tag, Action)
            If relayout IsNot Nothing Then
                relayout()
            Else
                lblGrandRef.Left = parent.Width - lblGrandRef.Width - 34
                lblGrandRef.Top = (parent.Height - lblGrandRef.Height) \ 2
            End If
        End If
    End Sub

    ' ---- Adding lines ----
    Private Shared Sub AddLineFromRequest(req As PendingReqLite)
        For Each ln In newLines
            If ln.ProductId = req.ProductId Then
                AppModal.Show("That product is already on this purchase order.",
                              "Already Added", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If
        Next
        newLines.Add(New POLine With {
            .ProductId = req.ProductId, .ProductName = req.ProductName, .Sku = req.Sku,
            .Qty = Math.Max(1, req.QtyRequested), .UnitCost = req.UnitPrice, .RequestId = req.RequestId
        })
        RebuildLines()
        RecomputeGrand()
    End Sub

    Private Shared Sub AddLineFromCombo()
        If cmbProductRef Is Nothing OrElse cmbProductRef.SelectedItem Is Nothing Then
            AppModal.Show("Pick a product from the list first.", "Select a Product",
                          MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        Dim p As ProdItem = TryCast(cmbProductRef.SelectedItem, ProdItem)
        If p Is Nothing Then Return
        For Each ln In newLines
            If ln.ProductId = p.Id Then
                AppModal.Show("That product is already on this purchase order.",
                              "Already Added", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If
        Next
        newLines.Add(New POLine With {
            .ProductId = p.Id, .ProductName = p.Name, .Sku = p.Sku,
            .Qty = 1, .UnitCost = p.Price, .RequestId = 0
        })
        RebuildLines()
        RecomputeGrand()
    End Sub

    Private Shared Sub AddLineFromRequestId(requestId As Integer)
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                                    "SELECT pr.request_id, pr.product_id, p.name AS pname, p.sku, p.unit_price, " &
                    "p.image_path, pr.qty_requested, u.full_name AS reqby " &
                    "FROM purchase_requests pr " &
                    "JOIN products p ON pr.product_id = p.product_id " &
                    "LEFT JOIN users u ON pr.requested_by = u.user_id " &
                    "WHERE pr.request_id = @rid LIMIT 1", conn)
                    cmd.Parameters.AddWithValue("@rid", requestId)
                    Using rdr = cmd.ExecuteReader()
                        If rdr.Read() Then
                            AddLineFromRequest(New PendingReqLite With {
                                .RequestId = SafeInt(rdr("request_id")),
                                .ProductId = SafeInt(rdr("product_id")),
                                .ProductName = rdr("pname").ToString(),
                                .Sku = rdr("sku").ToString(),
                                .UnitPrice = If(IsDBNull(rdr("unit_price")), 0D, Convert.ToDecimal(rdr("unit_price"))),
                                .QtyRequested = SafeInt(rdr("qty_requested")),
                               .RequestedBy = If(IsDBNull(rdr("reqby")), "Unknown", rdr("reqby").ToString()),
                                .ImagePath = If(IsDBNull(rdr("image_path")), "", rdr("image_path").ToString())
                            })
                        End If
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not reach the database:" & Environment.NewLine & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    ' ---- Save (single transaction) ----
    Private Shared Sub CommitNewPO()
        If btnSaveRef IsNot Nothing AndAlso CStr(If(btnSaveRef.Tag, "")) = "done" Then Return

        ' Validate
        If cmbSupplierRef Is Nothing OrElse cmbSupplierRef.SelectedItem Is Nothing Then
            AppModal.Show("Please choose a supplier.", "Missing Supplier",
                          MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If
        If newLines.Count = 0 Then
            AppModal.Show("Add at least one line item before saving.", "No Items",
                          MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If
        For Each ln In newLines
            If ln.Qty <= 0 Then
                AppModal.Show("Every line needs a quantity greater than zero." & Environment.NewLine &
                              "Check: " & ln.ProductName, "Invalid Quantity",
                              MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End If
            If ln.UnitCost < 0 Then
                AppModal.Show("Unit cost cannot be negative." & Environment.NewLine &
                              "Check: " & ln.ProductName, "Invalid Cost",
                              MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End If
        Next

        Dim sup As SupItem = TryCast(cmbSupplierRef.SelectedItem, SupItem)
        If sup Is Nothing Then Return

        Dim grand As Decimal = 0D
        For Each ln In newLines
            grand += ln.LineTotal
        Next

        Dim poNo As String = "PO-" & DateTime.Now.ToString("yyMMddHHmmss")
        Dim orderDate As DateTime = dtOrderRef.Value.Date
        Dim expObj As Object = If(dtExpectedRef.Checked,
                                  CType(dtExpectedRef.Value.Date, Object),
                                  CType(DBNull.Value, Object))

        btnSaveRef.Tag = "done"
        Try
            Using conn As MySqlConnection = Db.GetConnection()
                conn.Open()
                Using txn As MySqlTransaction = conn.BeginTransaction()
                    Try
                        Dim poId As Integer = 0
                        Using cmd As New MySqlCommand(
                            "INSERT INTO purchase_orders " &
                            "(po_no, supplier_id, created_by, order_date, expected_date, status, total_cost) " &
                            "VALUES (@no, @sid, @uid, @od, @ed, 'Draft', @total)", conn, txn)
                            cmd.Parameters.AddWithValue("@no", poNo)
                            cmd.Parameters.AddWithValue("@sid", sup.Id)
                            cmd.Parameters.AddWithValue("@uid", Session.CurrentUserId)
                            cmd.Parameters.AddWithValue("@od", orderDate)
                            cmd.Parameters.AddWithValue("@ed", expObj)
                            cmd.Parameters.AddWithValue("@total", grand)
                            cmd.ExecuteNonQuery()
                            poId = CInt(cmd.LastInsertedId)
                        End Using

                        For Each ln In newLines
                            Using cmd As New MySqlCommand(
                                "INSERT INTO po_items (po_id, product_id, qty, unit_cost, line_cost, request_id) " &
                                "VALUES (@po, @pid, @qty, @uc, @lc, @rid)", conn, txn)
                                cmd.Parameters.AddWithValue("@po", poId)
                                cmd.Parameters.AddWithValue("@pid", ln.ProductId)
                                cmd.Parameters.AddWithValue("@qty", ln.Qty)
                                cmd.Parameters.AddWithValue("@uc", Math.Round(ln.UnitCost, 2))
                                cmd.Parameters.AddWithValue("@lc", ln.LineTotal)
                                cmd.Parameters.AddWithValue("@rid",
                                    If(ln.RequestId > 0, CType(ln.RequestId, Object), CType(DBNull.Value, Object)))
                                cmd.ExecuteNonQuery()
                            End Using

                            ' Mark sourced request as Converted
                            If ln.RequestId > 0 Then
                                Using cmd As New MySqlCommand(
                                    "UPDATE purchase_requests SET status = 'Converted', " &
                                    "reviewed_by = @uid, reviewed_at = NOW() " &
                                    "WHERE request_id = @rid AND status = 'Pending'", conn, txn)
                                    cmd.Parameters.AddWithValue("@uid", Session.CurrentUserId)
                                    cmd.Parameters.AddWithValue("@rid", ln.RequestId)
                                    cmd.ExecuteNonQuery()
                                End Using
                            End If
                        Next

                        txn.Commit()
                    Catch exInner As Exception
                        txn.Rollback()
                        If btnSaveRef IsNot Nothing Then btnSaveRef.Tag = ""
                        AppModal.Show("Purchase order was not saved:" & Environment.NewLine & exInner.Message,
                                      "Could Not Save", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                        Return
                    End Try
                End Using
            End Using
        Catch ex As Exception
            If btnSaveRef IsNot Nothing Then btnSaveRef.Tag = ""
            AppModal.Show("Could not reach the database:" & Environment.NewLine & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try

        AppModal.Show("Purchase order " & poNo & " created as Draft." & Environment.NewLine &
                      "Open it to mark it as Ordered.", "Saved",
                      MessageBoxButtons.OK, MessageBoxIcon.Information)
        currentFilter = "All"
        RenderList()
    End Sub

    ' =========================================================================
    '  VIEW 3 — PO DETAIL
    ' =========================================================================
    Private Shared Sub LoadDetail(poId As Integer)
        Dim container As Panel = containerRef
        If container Is Nothing Then Return

        Dim po As PORow = Nothing
        Dim supplierName As String = ""
        Dim lines As New List(Of POLine)

        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT po.po_no, s.name AS sname, po.order_date, po.expected_date, " &
                    "po.status, po.total_cost " &
                    "FROM purchase_orders po JOIN suppliers s ON po.supplier_id = s.supplier_id " &
                    "WHERE po.po_id = @id LIMIT 1", conn)
                    cmd.Parameters.AddWithValue("@id", poId)
                    Using rdr = cmd.ExecuteReader()
                        If rdr.Read() Then
                            Dim hasExp As Boolean = Not IsDBNull(rdr("expected_date"))
                            po = New PORow With {
                                .PoId = poId,
                                .PoNo = rdr("po_no").ToString(),
                                .SupplierName = rdr("sname").ToString(),
                                .OrderDate = If(IsDBNull(rdr("order_date")), DateTime.Today, Convert.ToDateTime(rdr("order_date"))),
                                .HasExpected = hasExp,
                                .ExpectedDate = If(hasExp, Convert.ToDateTime(rdr("expected_date")), DateTime.MinValue),
                                .Status = rdr("status").ToString(),
                                .TotalCost = If(IsDBNull(rdr("total_cost")), 0D, Convert.ToDecimal(rdr("total_cost")))
                            }
                        End If
                    End Using
                End Using

                If po Is Nothing Then
                    AppModal.Show("That purchase order could not be found.", "Not Found",
                                  MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    Return
                End If

                Using cmd As New MySqlCommand(
                    "SELECT pi.product_id, p.name AS pname, p.sku, pi.qty, pi.unit_cost, pi.line_cost " &
                    "FROM po_items pi JOIN products p ON pi.product_id = p.product_id " &
                    "WHERE pi.po_id = @id ORDER BY p.name ASC", conn)
                    cmd.Parameters.AddWithValue("@id", poId)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            lines.Add(New POLine With {
                                .ProductId = SafeInt(rdr("product_id")),
                                .ProductName = rdr("pname").ToString(),
                                .Sku = rdr("sku").ToString(),
                                .Qty = SafeInt(rdr("qty")),
                                .UnitCost = If(IsDBNull(rdr("unit_cost")), 0D, Convert.ToDecimal(rdr("unit_cost"))),
                                .RequestId = 0
                            })
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load the purchase order:" & Environment.NewLine & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try

        container.SuspendLayout()
        container.Controls.Clear()
        container.BackColor = MintBg

        ' Scrolling body
        Dim body As New Panel With {
            .Dock = DockStyle.Fill, .BackColor = MintBg, .Padding = New Padding(24, 4, 24, 20), .AutoScroll = True
        }

        ' Header card
        Dim headCard As New BufferedPanel With {.Height = 130, .BackColor = CardBg, .Dock = DockStyle.Top}
        AddHandler headCard.Paint, Sub(s, e) PaintCard(CType(s, Panel), e)

        Dim lblPo As New Label With {
            .Text = po.PoNo, .Font = New Font("Segoe UI", 16, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(20, 16), .BackColor = Color.Transparent
        }
        Dim lblSup As New Label With {
            .Text = po.SupplierName, .Font = New Font("Segoe UI", 11),
            .ForeColor = SubText, .AutoSize = True, .Location = New Point(22, 50), .BackColor = Color.Transparent
        }
        Dim lblMeta As New Label With {
            .Text = "Ordered " & po.OrderDate.ToString("MMM d, yyyy") & "   " & ChrW(8226) & "   " &
                    If(po.HasExpected, "Expected " & po.ExpectedDate.ToString("MMM d, yyyy"), "No expected date") &
                    "   " & ChrW(8226) & "   " & lines.Count & If(lines.Count = 1, " item", " items"),
            .Font = New Font("Segoe UI", 9.5F), .ForeColor = FaintText, .AutoSize = True,
            .Location = New Point(22, 80), .BackColor = Color.Transparent
        }
        headCard.Controls.AddRange({lblPo, lblSup, lblMeta})

        Dim st = StatusInfo(po.Status)
        Dim badge As New Label With {
            .Text = st.text, .Font = New Font("Segoe UI", 9, FontStyle.Bold),
            .ForeColor = st.fore, .BackColor = st.back, .AutoSize = True,
            .Padding = New Padding(10, 4, 10, 4), .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        RoundLabel(badge, 11)
        headCard.Controls.Add(badge)

        Dim lblTotal As New Label With {
            .Text = Peso & po.TotalCost.ToString("N2"), .Font = New Font("Segoe UI", 18, FontStyle.Bold),
            .ForeColor = MintPrimary, .AutoSize = True, .BackColor = Color.Transparent,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        headCard.Controls.Add(lblTotal)

        AddHandler headCard.Resize,
            Sub()
                badge.Left = headCard.Width - badge.Width - 22
                badge.Top = 20
                lblTotal.Left = headCard.Width - lblTotal.Width - 22
                lblTotal.Top = 70
            End Sub

        ' Lines card
        Dim linesCard As New BufferedPanel With {
            .Height = 60 + Math.Max(1, lines.Count) * 46 + 10, .BackColor = CardBg, .Dock = DockStyle.Top,
            .Padding = New Padding(0, 12, 0, 0)
        }
        AddHandler linesCard.Paint, Sub(s, e) PaintCard(CType(s, Panel), e)

        Dim lblLinesHead As New Label With {
            .Text = "Line Items", .Font = New Font("Segoe UI", 13, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(20, 14), .BackColor = Color.Transparent
        }
        linesCard.Controls.Add(lblLinesHead)

        Dim yLine As Integer = 52
        If lines.Count = 0 Then
            Dim lbl As New Label With {
                .Text = "This purchase order has no line items.", .Font = New Font("Segoe UI", 10),
                .ForeColor = SubText, .AutoSize = True, .Location = New Point(20, yLine), .BackColor = Color.Transparent
            }
            linesCard.Controls.Add(lbl)
        Else
            For Each ln In lines
                Dim r As Panel = BuildDetailLineRow(ln, 46)
                r.Location = New Point(0, yLine)
                r.Width = linesCard.ClientSize.Width - 4
                r.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
                linesCard.Controls.Add(r)
                yLine += 46
            Next
            AddHandler linesCard.Resize,
                Sub()
                    Dim w As Integer = linesCard.ClientSize.Width - 4
                    For Each c As Control In linesCard.Controls
                        If TypeOf c Is Panel Then c.Width = w
                    Next
                End Sub
        End If

        ' Spacer so the Top-docked cards stack with a gap
        Dim spacer1 As New Panel With {.Height = 14, .Dock = DockStyle.Top, .BackColor = Color.Transparent}

        body.Controls.Add(linesCard)
        body.Controls.Add(spacer1)
        body.Controls.Add(headCard)

        ' Header bar (title + back + action)
        Dim header As New Panel With {.Height = 80, .BackColor = MintBg, .Padding = New Padding(0, 0, 24, 0), .Dock = DockStyle.Top}
        Dim btnBackD As New Button With {
            .Text = ChrW(8592), .Size = New Size(44, 44),
            .FlatStyle = FlatStyle.Flat, .BackColor = Color.Transparent, .ForeColor = MintDeep,
            .Font = New Font("Segoe UI", 16), .Cursor = Cursors.Hand,
            .TextAlign = ContentAlignment.MiddleCenter, .TabStop = False,
            .Location = New Point(16, 18)
        }
        btnBackD.FlatAppearance.BorderSize = 0
        btnBackD.FlatAppearance.MouseOverBackColor = Color.Transparent
        btnBackD.FlatAppearance.MouseDownBackColor = Color.Transparent
        AddHandler btnBackD.Click, Sub() RenderList()
        Dim lblTitle As New Label With {
            .Text = "Purchase Order", .Font = New Font("Segoe UI", 22, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True,
            .Location = New Point(btnBackD.Right + 6, 24),
            .BackColor = Color.Transparent
        }
        header.Controls.AddRange({btnBackD, lblTitle})

        ' Action button: Mark as Ordered (Draft) or Receive Goods (Ordered)
        If po.Status = "Draft" Then
            Dim btnOrder As New Button With {
                .Text = "Mark as Ordered", .Size = New Size(170, 46),
                .FlatStyle = FlatStyle.Flat, .BackColor = MintPrimary, .ForeColor = Color.White,
                .Font = New Font("Segoe UI Semibold", 11), .Cursor = Cursors.Hand,
                .TextAlign = ContentAlignment.MiddleCenter,
                .Anchor = AnchorStyles.Top Or AnchorStyles.Right, .Tag = ""
            }
            btnOrder.FlatAppearance.BorderSize = 0
            RoundControl(btnOrder, 12)
            AddHover(btnOrder, MintPrimary, MintHover)
            Dim capId As Integer = po.PoId
            AddHandler btnOrder.Click,
                Sub()
                    If CStr(If(btnOrder.Tag, "")) = "done" Then Return
                    MarkOrdered(capId, btnOrder)
                End Sub
            header.Controls.Add(btnOrder)
            AddHandler header.Resize, Sub()
                                          btnOrder.Top = 22
                                          btnOrder.Left = header.Width - 24 - btnOrder.Width
                                      End Sub
            btnOrder.Top = 22
            btnOrder.Left = 700
        ElseIf po.Status = "Ordered" OrElse po.Status = "Partially Received" Then
            Dim btnReceive As New Button With {
                .Text = "Receive Goods", .Size = New Size(160, 46),
                .FlatStyle = FlatStyle.Flat, .BackColor = MintDeep, .ForeColor = Color.White,
                .Font = New Font("Segoe UI Semibold", 11), .Cursor = Cursors.Hand,
                .TextAlign = ContentAlignment.MiddleCenter,
                .Anchor = AnchorStyles.Top Or AnchorStyles.Right
            }
            btnReceive.FlatAppearance.BorderSize = 0
            RoundControl(btnReceive, 12)
            AddHover(btnReceive, MintDeep, DeepHover)
            Dim capId As Integer = po.PoId
            AddHandler btnReceive.Click, Sub() GoToReceive(capId)
            header.Controls.Add(btnReceive)
            AddHandler header.Resize, Sub()
                                          btnReceive.Top = 22
                                          btnReceive.Left = header.Width - 24 - btnReceive.Width
                                      End Sub
            btnReceive.Top = 22
            btnReceive.Left = 700
        End If

        container.Controls.Add(body)
        container.Controls.Add(header)
        container.ResumeLayout()
    End Sub

    Private Shared Function BuildDetailLineRow(ln As POLine, height As Integer) As Panel
        Dim row As New Panel With {.Height = height, .BackColor = Color.Transparent}

        Dim lblName As New Label With {
            .Text = ln.ProductName, .Font = New Font("Segoe UI", 10, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(20, 6), .BackColor = Color.Transparent
        }
        Dim lblSku As New Label With {
            .Text = ln.Sku, .Font = New Font("Segoe UI", 8.5F), .ForeColor = SubText, .AutoSize = True,
            .Location = New Point(20, 26), .BackColor = Color.Transparent
        }
        row.Controls.AddRange({lblName, lblSku})

        Dim lblQty As New Label With {
            .Text = ln.Qty & " " & ChrW(215) & " " & Peso & ln.UnitCost.ToString("N2"),
            .Font = New Font("Segoe UI", 9.5F), .ForeColor = SubText, .AutoSize = True,
            .BackColor = Color.Transparent, .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        Dim lblLine As New Label With {
            .Text = Peso & ln.LineTotal.ToString("N2"), .Font = New Font("Segoe UI", 11, FontStyle.Bold),
            .ForeColor = MintDeep, .AutoSize = True, .BackColor = Color.Transparent,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        row.Controls.AddRange({lblQty, lblLine})

        AddHandler row.Resize,
            Sub()
                lblLine.Left = row.Width - lblLine.Width - 24
                lblLine.Top = (height - lblLine.Height) \ 2
                lblQty.Left = lblLine.Left - lblQty.Width - 28
                lblQty.Top = (height - lblQty.Height) \ 2
            End Sub

        Return row
    End Function

    ' =========================================================================
    '  ACTIONS
    ' =========================================================================
    Private Shared Sub MarkOrdered(poId As Integer, btn As Button)
        If AppModal.Show("Mark this purchase order as Ordered?" & Environment.NewLine &
                         "It will then be available to receive.", "Mark as Ordered",
                         MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then
            Return
        End If
        btn.Tag = "done"
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "UPDATE purchase_orders SET status = 'Ordered' " &
                    "WHERE po_id = @id AND status = 'Draft'", conn)
                    cmd.Parameters.AddWithValue("@id", poId)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        Catch ex As Exception
            btn.Tag = ""
            AppModal.Show("Could not update the purchase order:" & Environment.NewLine & ex.Message,
                          "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try
        LoadDetail(poId)
    End Sub

    Private Shared Sub GoToReceive(poId As Integer)
        Try
            Dim host As Control = If(containerRef, CType(Nothing, Control))
            If host Is Nothing Then Return
            Dim frm = TryCast(host.FindForm(), PROCUREMENT_STAFF)
            If frm Is Nothing Then Return
            frm.OpenReceiveForPO(poId)
        Catch ex As Exception
            AppModal.Show("Could not open Receive Goods:" & Environment.NewLine & ex.Message,
                          "Navigation Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    ' =========================================================================
    '  DATA LOADERS
    ' =========================================================================
    Private Shared Function FilterClause(filter As String) As String
        Select Case filter
            Case "Draft" : Return "po.status = 'Draft'"
            Case "Ordered" : Return "po.status = 'Ordered'"
            Case "Received" : Return "po.status = 'Received'"
            Case "Cancelled" : Return "po.status = 'Cancelled'"
            Case Else : Return "po.status NOT IN ('Cancelled','Received')"   ' All = active
        End Select
    End Function

    Private Shared Function LoadList(filter As String) As List(Of PORow)
        Dim result As New List(Of PORow)
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT po.po_id, po.po_no, s.name AS sname, po.order_date, po.expected_date, " &
                    "po.status, po.total_cost, " &
                    "(SELECT COUNT(*) FROM po_items pi WHERE pi.po_id = po.po_id) AS item_count " &
                    "FROM purchase_orders po JOIN suppliers s ON po.supplier_id = s.supplier_id " &
                    "WHERE " & FilterClause(filter) & " " &
                    "ORDER BY po.order_date DESC, po.po_id DESC", conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            Dim hasExp As Boolean = Not IsDBNull(rdr("expected_date"))
                            result.Add(New PORow With {
                                .PoId = SafeInt(rdr("po_id")),
                                .PoNo = rdr("po_no").ToString(),
                                .SupplierName = rdr("sname").ToString(),
                                .OrderDate = If(IsDBNull(rdr("order_date")), DateTime.Today, Convert.ToDateTime(rdr("order_date"))),
                                .HasExpected = hasExp,
                                .ExpectedDate = If(hasExp, Convert.ToDateTime(rdr("expected_date")), DateTime.MinValue),
                                .Status = rdr("status").ToString(),
                                .TotalCost = If(IsDBNull(rdr("total_cost")), 0D, Convert.ToDecimal(rdr("total_cost"))),
                                .ItemCount = SafeInt(rdr("item_count"))
                            })
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load purchase orders:" & Environment.NewLine & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
        Return result
    End Function

    Private Shared Function LoadSuppliers() As List(Of SupItem)
        Dim result As New List(Of SupItem)
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand("SELECT supplier_id, name FROM suppliers ORDER BY name ASC", conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            result.Add(New SupItem With {.Id = SafeInt(rdr("supplier_id")), .Name = rdr("name").ToString()})
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load suppliers:" & Environment.NewLine & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
        Return result
    End Function

    Private Shared Function LoadProducts() As List(Of ProdItem)
        Dim result As New List(Of ProdItem)
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT product_id, sku, name, unit_price FROM products " &
                    "WHERE is_active = 1 ORDER BY name ASC", conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            result.Add(New ProdItem With {
                                .Id = SafeInt(rdr("product_id")),
                                .Sku = rdr("sku").ToString(),
                                .Name = rdr("name").ToString(),
                                .Price = If(IsDBNull(rdr("unit_price")), 0D, Convert.ToDecimal(rdr("unit_price")))
                            })
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load products:" & Environment.NewLine & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
        Return result
    End Function

    Private Shared Function LoadPendingLite() As List(Of PendingReqLite)
        Dim result As New List(Of PendingReqLite)
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                             "SELECT pr.request_id, pr.product_id, p.name AS pname, p.sku, p.unit_price, " &
                    "p.image_path, pr.qty_requested, u.full_name AS reqby " &
                    "FROM purchase_requests pr " &
                    "JOIN products p ON pr.product_id = p.product_id " &
                    "LEFT JOIN users u ON pr.requested_by = u.user_id " &
                    "WHERE pr.status = 'Pending' " &
                    "ORDER BY pr.created_at DESC", conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            result.Add(New PendingReqLite With {
                                .RequestId = SafeInt(rdr("request_id")),
                                .ProductId = SafeInt(rdr("product_id")),
                                .ProductName = rdr("pname").ToString(),
                                .Sku = rdr("sku").ToString(),
                                .UnitPrice = If(IsDBNull(rdr("unit_price")), 0D, Convert.ToDecimal(rdr("unit_price"))),
                                .QtyRequested = SafeInt(rdr("qty_requested")),
                                .RequestedBy = If(IsDBNull(rdr("reqby")), "Unknown", rdr("reqby").ToString()),
                                .ImagePath = If(IsDBNull(rdr("image_path")), "", rdr("image_path").ToString())
                            })
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load pending requests:" & Environment.NewLine & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
        Return result
    End Function

    ' =========================================================================
    '  SHARED UI HELPERS
    ' =========================================================================
    Private Shared Function BuildCardHeader(title As String, subtitle As String) As Panel
        Dim header As New Panel With {.Height = 64, .BackColor = Color.Transparent}
        Dim lblTitle As New Label With {
            .Text = title, .Font = New Font("Segoe UI", 13.5F, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(16, 12), .BackColor = Color.Transparent
        }
        Dim lblSub As New Label With {
            .Text = subtitle, .Font = New Font("Segoe UI", 10),
            .ForeColor = FaintText, .AutoSize = True, .Location = New Point(16, 38), .BackColor = Color.Transparent
        }
        header.Controls.AddRange({lblTitle, lblSub})
        Dim divider As New Panel With {.Height = 1, .BackColor = RowDivider, .Location = New Point(16, 62)}
        divider.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        header.Controls.Add(divider)
        AddHandler header.Resize, Sub() divider.Width = header.Width - 32
        Return header
    End Function

    Private Shared Function BuildEmptyCard(title As String, message As String) As Panel
        Dim card As New BufferedPanel With {.Height = 150, .BackColor = CardBg}
        AddHandler card.Paint, Sub(s, e) PaintCard(CType(s, Panel), e)
        Dim lbl As New Label With {
            .Text = title, .Font = New Font("Segoe UI", 14, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .Location = New Point(28, 44), .BackColor = Color.Transparent
        }
        Dim sub_ As New Label With {
            .Text = message, .Font = New Font("Segoe UI", 10.5F), .ForeColor = SubText, .AutoSize = True,
            .Location = New Point(30, 76), .BackColor = Color.Transparent
        }
        card.Controls.AddRange({lbl, sub_})
        Return card
    End Function

    ' Status pill colours — spec mapping (covers PO + request statuses).
    Private Shared Function StatusInfo(status As String) As (text As String, fore As Color, back As Color)
        Select Case status
            Case "Draft" : Return ("Draft", FaintText, Color.FromArgb(238, 240, 239))
            Case "Ordered" : Return ("Ordered", MintPrimary, TileBg)
            Case "Partially Received" : Return ("Partial", WarningClr, Color.FromArgb(252, 240, 228))
            Case "Received" : Return ("Received", PositiveClr, Color.FromArgb(232, 245, 233))
            Case "Cancelled" : Return ("Cancelled", NegativeClr, Color.FromArgb(251, 234, 234))
            Case "Pending" : Return ("Pending", WarningClr, Color.FromArgb(252, 240, 228))
            Case "Approved" : Return ("Approved", PositiveClr, Color.FromArgb(232, 245, 233))
            Case "Rejected" : Return ("Rejected", NegativeClr, Color.FromArgb(251, 234, 234))
            Case "Converted" : Return ("Converted", FaintText, Color.FromArgb(238, 240, 239))
            Case Else : Return (status, SubText, TileBg)
        End Select
    End Function

    ' =========================================================================
    '  SHARED PAINT / CONTROL HELPERS  (verbatim from InventoryLowStockPanelBuilder)
    ' =========================================================================
    Private Shared Sub PaintCard(panel As Panel, e As PaintEventArgs)
        PanelRenderer.Draw(panel, e, True)
    End Sub

    ' Fills a panel with a rounded solid colour (used for line tiles).
    Private Shared Sub RoundFill(panel As Panel, e As PaintEventArgs, fill As Color, radius As Integer)
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
        Dim rect As New Rectangle(0, 0, panel.Width - 1, panel.Height - 1)
        Using path As GraphicsPath = RoundRect(rect, radius)
            Using b As New SolidBrush(fill)
                e.Graphics.FillPath(b, path)
            End Using
        End Using
    End Sub

    Private Shared Function MakeLink(text As String, clr As Color) As Label
        Dim lbl As New Label With {
            .Text = text, .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold),
            .ForeColor = clr, .AutoSize = True, .Cursor = Cursors.Hand, .BackColor = Color.Transparent
        }
        AddHandler lbl.MouseEnter, Sub() lbl.Font = New Font("Segoe UI", 9.5F, FontStyle.Bold Or FontStyle.Underline)
        AddHandler lbl.MouseLeave, Sub() lbl.Font = New Font("Segoe UI", 9.5F, FontStyle.Bold)
        Return lbl
    End Function

    Private Shared Function SafeInt(o As Object) As Integer
        If o Is Nothing OrElse IsDBNull(o) Then Return 0
        Dim n As Integer
        If Integer.TryParse(o.ToString(), n) Then Return n
        Return 0
    End Function

    ' String overloads for editable text boxes.
    Private Shared Function SafeIntStr(s As String) As Integer
        Dim n As Integer
        If Integer.TryParse(s, n) Then Return Math.Max(0, n)
        Return 0
    End Function

    Private Shared Function SafeDecStr(s As String) As Decimal
        Dim d As Decimal
        If Decimal.TryParse(s, d) Then Return Math.Max(0D, d)
        Return 0D
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

    Private Shared Function RoundRect(rect As Rectangle, radius As Integer) As GraphicsPath
        Dim r As Integer = radius
        Dim path As New GraphicsPath()
        If r <= 0 Then
            path.AddRectangle(rect)
            path.CloseFigure()
            Return path
        End If
        path.AddArc(rect.X, rect.Y, r, r, 180, 90)
        path.AddArc(rect.Right - r, rect.Y, r, r, 270, 90)
        path.AddArc(rect.Right - r, rect.Bottom - r, r, r, 0, 90)
        path.AddArc(rect.X, rect.Bottom - r, r, r, 90, 90)
        path.CloseFigure()
        Return path
    End Function

    Private Shared Sub RoundControl(ctrl As Control, radius As Integer)
        Dim path As New GraphicsPath()
        Dim r As Integer = radius
        path.AddArc(0, 0, r, r, 180, 90)
        path.AddArc(ctrl.Width - r, 0, r, r, 270, 90)
        path.AddArc(ctrl.Width - r, ctrl.Height - r, r, r, 0, 90)
        path.AddArc(0, ctrl.Height - r, r, r, 90, 90)
        path.CloseFigure()
        ctrl.Region = New Region(path)
    End Sub

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



    ' Small uppercase field caption.
    Private Shared Function MakeFieldLabel(text As String) As Label
        Return New Label With {
            .Text = text.ToUpperInvariant(), .Font = New Font("Segoe UI Semibold", 10),
            .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent
        }
    End Function

    ' Themes the dropdown calendar to the mint palette.
    Private Shared Sub ThemeCalendar(dt As DateTimePicker)
        dt.CalendarForeColor = MintDark
        dt.CalendarMonthBackground = CardBg
        dt.CalendarTitleBackColor = MintPrimary
        dt.CalendarTitleForeColor = Color.White
        dt.CalendarTrailingForeColor = FaintText
    End Sub

    ' Hover swap for flat buttons.
    Private Shared Sub AddHover(btn As Button, normal As Color, hover As Color)
        AddHandler btn.MouseEnter, Sub() btn.BackColor = hover
        AddHandler btn.MouseLeave, Sub() btn.BackColor = normal
    End Sub

    ' "Back to list" as a tappable pill with hover.
    Private Shared Function MakeBackPill() As Label
        Dim pill As New Label With {
            .Text = ChrW(8592) & "   Back to list",
            .Font = New Font("Segoe UI Semibold", 11),
            .ForeColor = MintDeep, .BackColor = TileBg,
            .AutoSize = False,
            .TextAlign = ContentAlignment.MiddleCenter, .Cursor = Cursors.Hand
        }
        RoundLabel(pill, 8)
        pill.Size = New Size(150, 34)   ' set AFTER RoundLabel so the rounded region applies
        AddHandler pill.MouseEnter, Sub() pill.BackColor = Color.FromArgb(210, 240, 235)
        AddHandler pill.MouseLeave, Sub() pill.BackColor = TileBg
        Return pill
    End Function

    Private Sub ProcurementPOPanelBuilder_Load(sender As Object, e As EventArgs) Handles MyBase.Load

    End Sub
End Class