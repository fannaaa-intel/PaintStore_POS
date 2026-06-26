Imports System.Drawing
Imports System.Drawing.Drawing2D

Public Class INVENTORY_STAFF

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

    ' ── Shift reference (kept for HomePanelBuilder parity) ───────────────────────
    Private shiftStartTime As DateTime

    ' NOTE: Nav, home, itemList, stockLevels, stockAdjustment, lowStock, logout
    '       and InitializeComponent live in INVENTORY_STAFF.Designer.vb

    ' ===========================================================================
    ' FORM LOAD
    ' ===========================================================================
    Private Sub INVENTORY_STAFF_Load(sender As Object, e As EventArgs) Handles MyBase.Load

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

        home.Text = "Dashboard"
        itemList.Text = "Item List"
        stockLevels.Text = "Stock Levels"
        stockAdjustment.Text = "Stock Adjustment"
        putAway.Text = "Incoming Stock"
        lowStock.Text = "Low Stock Alerts"
        logout.Text = "Logout"

        Dim lblMenu As New Label With {
            .Text = "MENU",
            .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold),
            .ForeColor = Color.FromArgb(120, 138, 134),
            .AutoSize = True,
            .Location = New Point(22, 96)
        }
        Nav.Controls.Add(lblMenu)

        Dim navButtons() As Button = {home, itemList, stockLevels, stockAdjustment, putAway, lowStock}
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

        SetActive(home)
        TintLogout()

        ' ── LOAD HOME ───────────────────────────────────────────────────────────
        shiftStartTime = DateTime.Now
        MainContainer.SuspendLayout()
        MainContainer.Controls.Clear()
        InventoryHomePanelBuilder.LoadHome(MainContainer, shiftStartTime)
        MainContainer.ResumeLayout()
        btnHamburger.Location = New Point(NAV_OPEN_WIDTH - 46, 18)
        btnHamburger.BringToFront()
    End Sub

    ' ===========================================================================
    ' HAMBURGER TOGGLE
    ' ===========================================================================
    Private Sub ToggleNav(sender As Object, e As EventArgs)
        navIsOpen = Not navIsOpen

        If navIsOpen Then
            Nav.Width = NAV_OPEN_WIDTH
            Nav.Controls.Add(btnHamburger)
            btnHamburger.Location = New Point(NAV_OPEN_WIDTH - 46, 18)
            btnHamburger.BackColor = Color.White
            ' Show all nav contents
            For Each ctrl As Control In Nav.Controls
                If ctrl IsNot btnHamburger Then ctrl.Visible = True
            Next
        Else
            Nav.Width = NAV_CLOSED_WIDTH
            Nav.Controls.Add(btnHamburger)
            btnHamburger.Location = New Point((NAV_CLOSED_WIDTH - btnHamburger.Width) \ 2, 18)
            btnHamburger.BackColor = Color.Transparent
            ' Hide all nav contents except burger
            For Each ctrl As Control In Nav.Controls
                If ctrl IsNot btnHamburger Then ctrl.Visible = False
            Next
        End If

        btnHamburger.BringToFront()
        MainContainer.PerformLayout()
        MainContainer.Refresh()
    End Sub

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
    ' PLACEHOLDER
    ' ===========================================================================
    Private Sub LoadPlaceholder(title As String)
        MainContainer.SuspendLayout()
        MainContainer.Controls.Clear()


        Dim panel As New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = Color.FromArgb(220, 240, 235)
        }
        AddHandler panel.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)
        Dim lblTitle As New Label With {
            .Text = title,
            .Font = New Font("Segoe UI", 18, FontStyle.Bold),
            .ForeColor = MintDarkTop,
            .AutoSize = True,
            .Location = New Point(30, 30)
        }
        Dim lblInfo As New Label With {
            .Text = "Ready for integration...",
            .Font = New Font("Segoe UI", 10),
            .ForeColor = Color.Gray,
            .AutoSize = True,
            .Location = New Point(32, 70)
        }
        panel.Controls.Add(lblTitle)
        panel.Controls.Add(lblInfo)
        MainContainer.Controls.Add(panel)
        MainContainer.ResumeLayout()
    End Sub

    ' ===========================================================================
    ' STYLE HELPERS
    ' ===========================================================================
    Private Sub StyleButton(btn As Button)
        btn.Parent = Nav

        Dim iconPath As String = ""
        Select Case btn.Name.ToLower()
            Case "home" : iconPath = "home.png"
            Case "itemlist" : iconPath = "clipboard.png"
            Case "stocklevels" : iconPath = "bar-chart.png"
            Case "stockadjustment" : iconPath = "transfer.png"
            Case "putaway" : iconPath = "add.png"
            Case "lowstock" : iconPath = "notification.png"
            Case "logout" : iconPath = "logout.png"
        End Select

        If IO.File.Exists(iconPath) Then
            Dim src As New Bitmap(Image.FromFile(iconPath), New Size(24, 24))
            Dim padded As New Bitmap(40, 24)
            Using g As Graphics = Graphics.FromImage(padded)
                g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic
                g.DrawImage(src, 0, 0, 24, 24)
            End Using
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
        Dim redText As Color = Color.FromArgb(200, 50, 50)
        Dim redTintBg As Color = Color.FromArgb(253, 240, 240)
        Dim redHoverBg As Color = Color.FromArgb(248, 220, 220)
        logout.ForeColor = redText
        logout.Font = New Font("Segoe UI Semibold", 12, FontStyle.Bold)
        logout.BackColor = redTintBg
        logout.FlatAppearance.MouseOverBackColor = redHoverBg
        AddHandler logout.MouseEnter, Sub() logout.BackColor = redHoverBg
        AddHandler logout.MouseLeave, Sub() logout.BackColor = redTintBg
    End Sub

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
    Private Sub home_Click(sender As Object, e As EventArgs) Handles home.Click
        SetActive(home)
        InventoryHomePanelBuilder.StopTimer()
        MainContainer.SuspendLayout()
        MainContainer.Controls.Clear()   ' safe — burger is on Form, not MainContainer
        InventoryHomePanelBuilder.LoadHome(MainContainer, shiftStartTime)
        MainContainer.ResumeLayout()
    End Sub

    Private Sub itemList_Click(sender As Object, e As EventArgs) Handles itemList.Click
        SetActive(itemList)
        InventoryHomePanelBuilder.StopTimer()
        MainContainer.SuspendLayout()
        MainContainer.Controls.Clear()
        InventoryItemListPanelBuilder.LoadItemList(MainContainer)
        MainContainer.ResumeLayout()
        MainContainer.Refresh()
    End Sub

    ' Opens the Item List with an incoming filter pre-applied — called from the
    ' dashboard's KPI cards (status) and category chips (category).
    ' category = "" → all categories ; status = "" / "in" / "low" / "out".
    Public Sub OpenItemListFiltered(category As String, status As String)
        SetActive(itemList)
        InventoryHomePanelBuilder.StopTimer()
        MainContainer.SuspendLayout()
        MainContainer.Controls.Clear()
        InventoryItemListPanelBuilder.LoadItemList(MainContainer, category, status)
        MainContainer.ResumeLayout()
        MainContainer.Refresh()
    End Sub

    Private Sub stockLevels_Click(sender As Object, e As EventArgs) Handles stockLevels.Click
        SetActive(stockLevels)
        InventoryHomePanelBuilder.StopTimer()
        MainContainer.SuspendLayout()
        MainContainer.Controls.Clear()
        InventoryStockLevelPanelBuilder.LoadStockLevels(MainContainer)
        MainContainer.ResumeLayout()
        MainContainer.Refresh()
    End Sub

    Private Sub stockAdjustment_Click(sender As Object, e As EventArgs) Handles stockAdjustment.Click
        SetActive(stockAdjustment)
        InventoryHomePanelBuilder.StopTimer()
        MainContainer.SuspendLayout()
        MainContainer.Controls.Clear()
        InventoryStockAdjustmentPanelBuilder.LoadStockAdjustment(MainContainer)
        MainContainer.ResumeLayout()
        MainContainer.Refresh()
    End Sub

    ' Opens Stock Adjustment with a product pre-loaded — called from the
    ' Stock Levels screen's "Adjust" link. productId = 0 means open in search mode.
    Public Sub OpenStockAdjustmentFor(productId As Integer)
        SetActive(stockAdjustment)
        InventoryHomePanelBuilder.StopTimer()
        MainContainer.SuspendLayout()
        MainContainer.Controls.Clear()
        InventoryStockAdjustmentPanelBuilder.LoadStockAdjustment(MainContainer, productId)
        MainContainer.ResumeLayout()
        MainContainer.Refresh()
    End Sub

    Private Sub putAway_Click(sender As Object, e As EventArgs) Handles putAway.Click
        SetActive(putAway)
        InventoryHomePanelBuilder.StopTimer()
        MainContainer.SuspendLayout()
        MainContainer.Controls.Clear()
        InventoryPutAwayPanelBuilder.LoadPutAway(MainContainer)
        MainContainer.ResumeLayout()
        MainContainer.Refresh()
    End Sub

    ' Opens Incoming Stock directly — can be called from other screens.
    Public Sub OpenPutAway()
        SetActive(putAway)
        InventoryHomePanelBuilder.StopTimer()
        MainContainer.SuspendLayout()
        MainContainer.Controls.Clear()
        InventoryPutAwayPanelBuilder.LoadPutAway(MainContainer)
        MainContainer.ResumeLayout()
        MainContainer.Refresh()
    End Sub

    Private Sub lowStock_Click(sender As Object, e As EventArgs) Handles lowStock.Click
        SetActive(lowStock)
        InventoryHomePanelBuilder.StopTimer()
        MainContainer.SuspendLayout()
        MainContainer.Controls.Clear()
        InventoryLowStockPanelBuilder.LoadLowStock(MainContainer)
        MainContainer.ResumeLayout()
        MainContainer.Refresh()
    End Sub

    Private Sub logout_Click(sender As Object, e As EventArgs) Handles logout.Click
        If AppModal.Show("Are you sure you want to log out?", "Logout",
                         MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then
            Return
        End If
        Dim loginForm As New LogIn()
        loginForm.Show()
        Me.Close()
    End Sub

End Class