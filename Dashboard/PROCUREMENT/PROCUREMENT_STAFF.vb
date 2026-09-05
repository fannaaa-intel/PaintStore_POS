Imports System.Drawing
Imports System.Drawing.Drawing2D

Public Class PROCUREMENT_STAFF

    Private MintPrimary As Color = Color.FromArgb(0, 200, 170)
    Private MintDarkTop As Color = Color.FromArgb(0, 120, 110)
    Private MintMid As Color = Color.FromArgb(0, 170, 150)
    Private MintLight As Color = Color.FromArgb(220, 255, 245)
    Private MintGlow As Color = Color.FromArgb(0, 255, 200)
    Private NavBg As Color = Color.FromArgb(250, 252, 251)
    Private NavHover As Color = Color.FromArgb(238, 248, 244)

    Private MainContainer As Panel

    ' ── Hamburger toggle state ──────────────────────────────────────────────────
    Private Const NAV_OPEN_WIDTH As Integer = 260
    Private Const NAV_CLOSED_WIDTH As Integer = 56
    Private navIsOpen As Boolean = True
    Private btnHamburger As Button          ' always-visible toggle button

    ' Captions are cleared from the buttons when the rail collapses, so the real
    ' label has to live somewhere else. Icons are cached in both layouts:
    ' "padded" = 40x24 with the glyph left-aligned (caption sits beside it),
    ' "bare"   = 24x24 for the centred icon-only rail.
    Private navCaptions As New Dictionary(Of Button, String)
    Private navIconPadded As New Dictionary(Of Button, Image)
    Private navIconBare As New Dictionary(Of Button, Image)
    Private NavTips As New ToolTip()


    ' NOTE: Nav, dashboard, purchaseOrders, suppliers, receiveGoods,
    '       deliveryTracking, logout and InitializeComponent live in
    '       PROCUREMENT_STAFF.Designer.vb

    ' ===========================================================================
    ' FORM LOAD
    ' ===========================================================================
    Private Sub PROCUREMENT_STAFF_Load(sender As Object, e As EventArgs) Handles MyBase.Load

        Me.SetStyle(ControlStyles.AllPaintingInWmPaint Or
                    ControlStyles.OptimizedDoubleBuffer Or
                    ControlStyles.ResizeRedraw Or
                    ControlStyles.UserPaint, True)
        Me.DoubleBuffered = True

        ' ── NAV PANEL ───────────────────────────────────────────────────────────
        Nav.Dock = DockStyle.Left
        Nav.Width = NAV_OPEN_WIDTH
        Nav.BackColor = NavBg
        AddHandler Nav.Paint,
    Sub(s As Object, pe As PaintEventArgs)
        Using pen As New Pen(Color.FromArgb(220, 230, 226), 1)
            pe.Graphics.DrawLine(pen, Nav.Width - 1, 0, Nav.Width - 1, Nav.Height)
        End Using
    End Sub
        ' Without this the divider keeps the x it was painted at for the previous
        ' width, so after the nav is resized the line is stranded INSIDE the panel
        ' and runs straight through the icon column.
        AddHandler Nav.Resize, Sub() Nav.Invalidate()

        ' ── MAIN CONTAINER ──────────────────────────────────────────────────────
        MainContainer = New Panel With {
    .Dock = DockStyle.Fill,
    .BackColor = Color.White
}
        Me.Controls.Add(MainContainer)
        Me.Controls.Add(Nav)
        MainContainer.Padding = New Padding(0)
        ' ── HAMBURGER BUTTON — lives on the FORM, always visible ────────────────
        btnHamburger = New Button With {
    .Size = New Size(36, 36),
.Location = New Point(NAV_OPEN_WIDTH - 46, 18),
    .FlatStyle = FlatStyle.Flat,
    .BackColor = Color.White,
    .Cursor = Cursors.Hand,
  .Anchor = AnchorStyles.Top Or AnchorStyles.Right,
    .TabStop = False
}
        btnHamburger.FlatAppearance.BorderSize = 0
        btnHamburger.FlatAppearance.MouseOverBackColor = Color.FromArgb(230, 248, 242)
        btnHamburger.FlatAppearance.MouseDownBackColor = Color.FromArgb(210, 240, 232)

        ' Load icon — try full path first, then relative
        Dim burgerFull As String = "burger-menu.png"
        Dim burgerRel As String = "burger-menu.png"
        If IO.File.Exists(burgerFull) Then
            btnHamburger.Image = New Bitmap(Image.FromFile(burgerFull), New Size(22, 22))
        ElseIf IO.File.Exists(burgerRel) Then
            btnHamburger.Image = New Bitmap(Image.FromFile(burgerRel), New Size(22, 22))
        Else
            ' Unicode fallback — three-bar "≡" drawn as text
            btnHamburger.Text = "≡"
            btnHamburger.Font = New Font("Segoe UI", 16, FontStyle.Bold)
            btnHamburger.ForeColor = Color.FromArgb(10, 79, 66)
        End If

        ' Round the hamburger button corners
        AddHandler btnHamburger.Paint,
            Sub(s As Object, pe As PaintEventArgs)
                Dim b = CType(s, Button)
                pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim r As Integer = 10
                Dim rect = b.ClientRectangle
                rect.Inflate(-1, -1)
                Using path As New GraphicsPath()
                    path.AddArc(rect.X, rect.Y, r, r, 180, 90)
                    path.AddArc(rect.Right - r, rect.Y, r, r, 270, 90)
                    path.AddArc(rect.Right - r, rect.Bottom - r, r, r, 0, 90)
                    path.AddArc(rect.X, rect.Bottom - r, r, r, 90, 90)
                    path.CloseFigure()
                    Using br As New SolidBrush(b.BackColor)
                        pe.Graphics.FillPath(br, path)
                    End Using
                End Using
                If b.Image IsNot Nothing Then
                    Dim ix As Integer = (b.Width - b.Image.Width) \ 2
                    Dim iy As Integer = (b.Height - b.Image.Height) \ 2
                    pe.Graphics.DrawImage(b.Image, ix, iy)
                ElseIf b.Text <> "" Then
                    TextRenderer.DrawText(pe.Graphics, b.Text, b.Font,
                        b.ClientRectangle, b.ForeColor,
                        TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
                End If
            End Sub

        AddHandler btnHamburger.Click, AddressOf ToggleNav
        Nav.Controls.Add(btnHamburger)
        btnHamburger.BringToFront()

        ' ── NAV CONTENTS ────────────────────────────────────────────────────────
        Dim logoBox As New PictureBox With {
            .Size = New Size(38, 38),
     .Location = New Point(18, 14),
            .SizeMode = PictureBoxSizeMode.Zoom,
            .BackColor = MintLight
        }
        Dim logoPath2 As New Drawing2D.GraphicsPath()
        logoPath2.AddEllipse(0, 0, logoBox.Width, logoBox.Height)
        logoBox.Region = New Region(logoPath2)

        Dim logoFile As String = "paint.png"
        If IO.File.Exists(logoFile) Then logoBox.Image = Image.FromFile(logoFile)
        Nav.Controls.Add(logoBox)

        Dim lblLogo As New Label With {
            .Text = "Paint POS",
            .Font = New Font("Segoe UI Semibold", 14, FontStyle.Bold),
            .ForeColor = Color.FromArgb(30, 45, 43),
            .AutoSize = True,
           .Location = New Point(64, 22)
        }
        Nav.Controls.Add(lblLogo)

        dashboard.Text = "Dashboard"
        purchaseOrders.Text = "Purchase Orders"
        suppliers.Text = "Suppliers"
        quotations.Text = "Quotations"
        receiveGoods.Text = "Receive Goods"
        deliveryTracking.Text = "Delivery Tracking"
        logout.Text = "Logout"

        Dim lblMenu As New Label With {
            .Text = "MENU",
            .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold),
            .ForeColor = Color.FromArgb(120, 138, 134),
            .AutoSize = True,
            .Location = New Point(22, 96)
        }
        Nav.Controls.Add(lblMenu)

        Dim navButtons() As Button = {dashboard, purchaseOrders, suppliers, quotations, receiveGoods, deliveryTracking}
        Dim topPos As Integer = 128
        For Each btn In navButtons
            StyleButton(btn)
            btn.Left = 14
            btn.Top = topPos
            btn.Width = Nav.Width - 28
            btn.Height = 48
            RoundButton(btn, 12)
            topPos += 58
        Next

        StyleButton(logout)
        logout.Left = 14
        logout.Width = Nav.Width - 28
        logout.Height = 48
        logout.Top = Nav.Height - 110
        logout.Anchor = AnchorStyles.Bottom Or AnchorStyles.Left
        RoundButton(logout, 12)

        SetActive(dashboard)
        TintLogout()

        ' ── LOAD DASHBOARD ──────────────────────────────────────────────────────
        MainContainer.SuspendLayout()
        MainContainer.Controls.Clear()
        ProcurementDashPanelBuilder.LoadDash(MainContainer)
        MainContainer.ResumeLayout()
        btnHamburger.Location = New Point(NAV_OPEN_WIDTH - 46, 18)
        btnHamburger.BringToFront()
    End Sub

    ' ===========================================================================
    ' HAMBURGER TOGGLE
    ' ===========================================================================
    Private Sub ToggleNav(sender As Object, e As EventArgs)
        navIsOpen = Not navIsOpen
        ApplyNavState()
    End Sub

    ' Collapsed nav is an ICON RAIL: every nav button stays visible and clickable,
    ' only its caption is dropped. Text-only chrome (logo wordmark, "MENU") hides.
    Private Sub ApplyNavState()
        Nav.SuspendLayout()

        Dim width As Integer = If(navIsOpen, NAV_OPEN_WIDTH, NAV_CLOSED_WIDTH)
        Nav.Width = width

        For Each ctrl As Control In Nav.Controls
            If ctrl Is btnHamburger Then Continue For
            ' Any button in the rail is navigation and must survive the collapse.
            ctrl.Visible = navIsOpen OrElse TypeOf ctrl Is Button
        Next

        For Each ctrl As Control In Nav.Controls
            If ctrl Is btnHamburger Then Continue For
            Dim btn = TryCast(ctrl, Button)
            If btn IsNot Nothing Then ApplyNavButtonState(btn, width)
        Next

        btnHamburger.BackColor = If(navIsOpen, Color.White, Color.Transparent)
        btnHamburger.Location = If(navIsOpen,
                                   New Point(NAV_OPEN_WIDTH - 46, 18),
                                   New Point((NAV_CLOSED_WIDTH - btnHamburger.Width) \ 2, 18))
        btnHamburger.BringToFront()

        Nav.ResumeLayout()
        Nav.Invalidate()
        MainContainer.PerformLayout()
        MainContainer.Refresh()
    End Sub

    ' Re-lay one nav button for the current rail width. Collapsed: no caption and
    ' the icon is centred; expanded: caption returns and the icon sits left.
    Private Sub ApplyNavButtonState(btn As Button, navWidth As Integer)
        If btn Is Nothing Then Return

        If navIsOpen Then
            btn.Text = NavCaption(btn)
            btn.Left = 14
            btn.Width = navWidth - 28
            btn.TextImageRelation = TextImageRelation.ImageBeforeText
            btn.ImageAlign = ContentAlignment.MiddleLeft
            btn.TextAlign = ContentAlignment.MiddleLeft
            btn.Padding = New Padding(20, 0, 14, 0)
            btn.Image = NavIcon(btn, padded:=True)
        Else
            btn.Text = ""
            btn.Left = 4
            btn.Width = navWidth - 8
            btn.ImageAlign = ContentAlignment.MiddleCenter
            btn.Padding = New Padding(0)
            btn.Image = NavIcon(btn, padded:=False)
        End If

        RoundButton(btn, 12)
        ' Tooltip so a collapsed icon still says what it does.
        NavTips.SetToolTip(btn, NavCaption(btn))
    End Sub

    Private Function NavCaption(btn As Button) As String
        Dim caption As String = ""
        If btn IsNot Nothing AndAlso navCaptions.TryGetValue(btn, caption) Then Return caption
        Return ""
    End Function

    Private Function NavIcon(btn As Button, padded As Boolean) As Image
        Dim img As Image = Nothing
        Dim store = If(padded, navIconPadded, navIconBare)
        If btn IsNot Nothing AndAlso store.TryGetValue(btn, img) Then Return img
        Return Nothing
    End Function

    ' ===========================================================================
    ' HELPER: reload active panel after nav toggle so content re-layouts
    ' ===========================================================================
    Private Sub ReloadCurrentPanel()
        ' Simply refresh — the scroll/wrap panels inside already use
        ' DockStyle.Fill + Resize handlers, so they reflow automatically.
        MainContainer.PerformLayout()
        MainContainer.Invalidate(True)
    End Sub

    ' ===========================================================================
    ' STYLE HELPERS
    ' ===========================================================================
    Private Sub StyleButton(btn As Button)
        ' Remember the caption before anything can clear it, so the collapsed rail
        ' can put it back (and show it as a tooltip meanwhile).
        navCaptions(btn) = btn.Text

        btn.Parent = Nav

        Dim iconPath As String = ""
        Select Case btn.Name.ToLower()
            Case "dashboard" : iconPath = "home.png"
            Case "purchaseorders" : iconPath = "cart.png"
            Case "suppliers" : iconPath = "group.png"
            Case "quotations" : iconPath = "quotation.png"
            Case "receivegoods" : iconPath = "received.png"
            Case "deliverytracking" : iconPath = "express-delivery.png"
            Case "logout" : iconPath = "logout.png"
        End Select

        If IO.File.Exists(iconPath) Then
            Dim src As New Bitmap(Image.FromFile(iconPath), New Size(24, 24))
            Dim padded As New Bitmap(40, 24)
            Using g As Graphics = Graphics.FromImage(padded)
                g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic
                g.DrawImage(src, 0, 0, 24, 24)
            End Using
            navIconPadded(btn) = padded
            navIconBare(btn) = src
            btn.Image = padded
        End If

        btn.Font = New Font("Segoe UI Semibold", 12, FontStyle.Regular)
        btn.ForeColor = Color.FromArgb(90, 105, 102)
        btn.TextImageRelation = TextImageRelation.ImageBeforeText
        btn.ImageAlign = ContentAlignment.MiddleLeft
        btn.TextAlign = ContentAlignment.MiddleLeft
        btn.Padding = New Padding(20, 0, 14, 0)
        btn.FlatStyle = FlatStyle.Flat
        btn.FlatAppearance.BorderSize = 0
        btn.FlatAppearance.MouseOverBackColor = NavBg
        btn.FlatAppearance.MouseDownBackColor = NavHover
        btn.BackColor = NavBg
        btn.Cursor = Cursors.Hand

        AddHandler btn.MouseEnter, Sub() If btn.Tag <> "active" Then btn.BackColor = NavHover
        AddHandler btn.MouseLeave, Sub() If btn.Tag <> "active" Then btn.BackColor = NavBg
    End Sub

    Private Sub SetActive(btn As Button)
        For Each ctrl As Control In Nav.Controls
            If TypeOf ctrl Is Button AndAlso ctrl IsNot logout Then
                ctrl.BackColor = NavBg
                ctrl.ForeColor = Color.FromArgb(90, 105, 102)
                ctrl.Font = New Font("Segoe UI Semibold", 12, FontStyle.Regular)
                ctrl.Tag = ""
            End If
        Next
        btn.BackColor = MintLight
        btn.ForeColor = MintDarkTop
        btn.Font = New Font("Segoe UI Semibold", 12, FontStyle.Bold)
        btn.Tag = "active"
    End Sub

    Private Sub RoundButton(btn As Button, radius As Integer)
        Dim r As Integer = radius
        Dim w As Integer = btn.Width
        Dim h As Integer = btn.Height
        Dim path As New Drawing2D.GraphicsPath()
        path.AddArc(0, 0, r, r, 180, 90)
        path.AddArc(w - r, 0, r, r, 270, 90)
        path.AddArc(w - r, h - r, r, r, 0, 90)
        path.AddArc(0, h - r, r, r, 90, 90)
        path.CloseFigure()
        btn.Region = New Region(path)
    End Sub

    Private Sub TintLogout()
        ' Solid red so Logout reads as a real, deliberate action rather than a
        ' pale tinted strip. White label + white-tinted icon for contrast.
        Dim redBg As Color = Color.FromArgb(198, 40, 40)
        Dim redHoverBg As Color = Color.FromArgb(176, 30, 30)
        Dim redDownBg As Color = Color.FromArgb(154, 24, 24)
        logout.ForeColor = Color.White
        logout.Font = New Font("Segoe UI Semibold", 12, FontStyle.Bold)
        logout.BackColor = redBg
        logout.FlatStyle = FlatStyle.Flat
        logout.FlatAppearance.BorderSize = 0
        logout.FlatAppearance.MouseOverBackColor = redHoverBg
        logout.FlatAppearance.MouseDownBackColor = redDownBg
        logout.Cursor = Cursors.Hand
        If logout.Image IsNot Nothing Then
            logout.Image = TintLogoutIcon(logout.Image, Color.White)
            ' Keep the cached variants in step, or the collapsed rail would fall
            ' back to the untinted (dark-on-red) logout glyph.
            navIconPadded(logout) = logout.Image
            Dim bareLogout As Image = Nothing
            If navIconBare.TryGetValue(logout, bareLogout) AndAlso bareLogout IsNot Nothing Then
                navIconBare(logout) = TintLogoutIcon(bareLogout, Color.White)
            End If
        End If
        AddHandler logout.MouseEnter, Sub() logout.BackColor = redHoverBg
        AddHandler logout.MouseLeave, Sub() logout.BackColor = redBg
    End Sub

    ' Re-colours the logout glyph to a solid target colour, preserving alpha.
    Private Shared Function TintLogoutIcon(src As Image, target As Color) As Image
        Dim bmp As New Bitmap(src.Width, src.Height)
        Using g As Graphics = Graphics.FromImage(bmp)
            Dim cm As New Imaging.ColorMatrix(New Single()() {
                New Single() {0, 0, 0, 0, 0},
                New Single() {0, 0, 0, 0, 0},
                New Single() {0, 0, 0, 0, 0},
                New Single() {0, 0, 0, 1, 0},
                New Single() {target.R / 255.0F, target.G / 255.0F, target.B / 255.0F, 0, 1}})
            Using ia As New Imaging.ImageAttributes()
                ia.SetColorMatrix(cm)
                g.DrawImage(src, New Rectangle(0, 0, src.Width, src.Height),
                            0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia)
            End Using
        End Using
        Return bmp
    End Function

    Private Sub ApplyModernButton(btn As Button)
        btn.FlatStyle = FlatStyle.Flat
        btn.FlatAppearance.BorderSize = 0
        btn.Font = New Font("Segoe UI Semibold", 10, FontStyle.Bold)
        btn.ForeColor = Color.White
        btn.Cursor = Cursors.Hand

        AddHandler btn.Paint,
            Sub(s, ev)
                Dim rect As Rectangle = btn.ClientRectangle
                Dim radius As Integer = 12
                Using path As New Drawing2D.GraphicsPath()
                    path.AddArc(0, 0, radius, radius, 180, 90)
                    path.AddArc(rect.Width - radius, 0, radius, radius, 270, 90)
                    path.AddArc(rect.Width - radius, rect.Height - radius, radius, radius, 0, 90)
                    path.AddArc(0, rect.Height - radius, radius, radius, 90, 90)
                    path.CloseFigure()
                    ev.Graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
                    Using brush As New Drawing2D.LinearGradientBrush(rect, MintMid, MintDarkTop, 90.0F)
                        ev.Graphics.FillPath(brush, path)
                    End Using
                    Using pen As New Pen(MintGlow, 2)
                        ev.Graphics.DrawPath(pen, path)
                    End Using
                End Using
                TextRenderer.DrawText(ev.Graphics, btn.Text, btn.Font, rect, Color.White,
                    TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub
    End Sub

    ' ===========================================================================
    ' BUTTON CLICK HANDLERS
    ' ===========================================================================
    Private Sub dashboard_Click(sender As Object, e As EventArgs) Handles dashboard.Click
        If Not ProcurementPOPanelBuilder.ConfirmLeaveNewPO() Then Return
        SetActive(dashboard)
        MainContainer.SuspendLayout()
        MainContainer.Controls.Clear()   ' safe — burger is on Form, not MainContainer
        ProcurementDashPanelBuilder.LoadDash(MainContainer)
        MainContainer.ResumeLayout()
        MainContainer.Refresh()
    End Sub

    Private Sub purchaseOrders_Click(sender As Object, e As EventArgs) Handles purchaseOrders.Click
        If Not ProcurementPOPanelBuilder.ConfirmLeaveNewPO() Then Return
        SetActive(purchaseOrders)
        MainContainer.SuspendLayout()
        MainContainer.Controls.Clear()
        ProcurementPOPanelBuilder.LoadPO(MainContainer)
        MainContainer.ResumeLayout()
        MainContainer.Refresh()
    End Sub

    ' Opens Purchase Orders with an incoming pending request pre-loaded into the
    ' New PO form — called from the dashboard's "Review" links.
    Public Sub OpenPOWithRequest(requestId As Integer)
        SetActive(purchaseOrders)
        MainContainer.SuspendLayout()
        MainContainer.Controls.Clear()
        ProcurementPOPanelBuilder.LoadPOWithRequest(MainContainer, requestId)
        MainContainer.ResumeLayout()
        MainContainer.Refresh()
    End Sub

    Private Sub suppliers_Click(sender As Object, e As EventArgs) Handles suppliers.Click
        If Not ProcurementPOPanelBuilder.ConfirmLeaveNewPO() Then Return
        SetActive(suppliers)
        MainContainer.SuspendLayout()
        MainContainer.Controls.Clear()
        ProcurementSuppliersPanelBuilder.LoadSuppliers(MainContainer)
        MainContainer.ResumeLayout()
        MainContainer.Refresh()
    End Sub

    Private Sub quotations_Click(sender As Object, e As EventArgs) Handles quotations.Click
        If Not ProcurementPOPanelBuilder.ConfirmLeaveNewPO() Then Return
        SetActive(quotations)
        MainContainer.SuspendLayout()
        MainContainer.Controls.Clear()
        ProcurementQuotationsPanelBuilder.LoadQuotations(MainContainer)
        MainContainer.ResumeLayout()
        MainContainer.Refresh()
    End Sub

    ' Opens Purchase Orders with an accepted quotation converted into a draft PO —
    ' called from the Quotations screen's "Convert to PO" action.
    Public Sub OpenPOFromQuotation(quotationId As Integer)
        SetActive(purchaseOrders)
        MainContainer.SuspendLayout()
        MainContainer.Controls.Clear()
        ProcurementPOPanelBuilder.LoadPOFromQuotation(MainContainer, quotationId)
        MainContainer.ResumeLayout()
        MainContainer.Refresh()
    End Sub

    Private Sub receiveGoods_Click(sender As Object, e As EventArgs) Handles receiveGoods.Click
        If Not ProcurementPOPanelBuilder.ConfirmLeaveNewPO() Then Return
        SetActive(receiveGoods)
        MainContainer.SuspendLayout()
        MainContainer.Controls.Clear()
        ProcurementReceivePanelBuilder.LoadReceive(MainContainer)
        MainContainer.ResumeLayout()
        MainContainer.Refresh()
    End Sub

    ' Opens Receive Goods with a specific PO pre-loaded — called from the
    ' Delivery Tracking screen's "Go to Receive" link.
    Public Sub OpenReceiveForPO(poId As Integer)
        SetActive(receiveGoods)
        MainContainer.SuspendLayout()
        MainContainer.Controls.Clear()
        ProcurementReceivePanelBuilder.LoadReceiveForPO(MainContainer, poId)
        MainContainer.ResumeLayout()
        MainContainer.Refresh()
    End Sub

    Private Sub deliveryTracking_Click(sender As Object, e As EventArgs) Handles deliveryTracking.Click
        If Not ProcurementPOPanelBuilder.ConfirmLeaveNewPO() Then Return
        SetActive(deliveryTracking)
        MainContainer.SuspendLayout()
        MainContainer.Controls.Clear()
        ProcurementDeliveryPanelBuilder.LoadDelivery(MainContainer)
        MainContainer.ResumeLayout()
        MainContainer.Refresh()
    End Sub

    Private Sub logout_Click(sender As Object, e As EventArgs) Handles logout.Click
        If Not ProcurementPOPanelBuilder.ConfirmLeaveNewPO() Then Return
        If AppModal.Show("Are you sure you want to log out?", "Logout",
                         MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then
            Return
        End If
        ActivityLog.Record("LOGOUT")
        Dim loginForm As New LogIn()
        loginForm.Show()
        Me.Close()
    End Sub

End Class