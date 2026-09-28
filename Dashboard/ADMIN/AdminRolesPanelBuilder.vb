Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports MySql.Data.MySqlClient

' =============================================================================
'  AdminRolesPanelBuilder  —  Admin ▸ Roles  (READ-ONLY reference view)
'
'  Roles are fixed by the system and are not editable from this screen, so the
'  panel is a plain reference list: no create / edit / delete, no pager.
'
'  LAYOUT  mirrors AdminUserPanelBuilder:
'    TopRegion (Dock = Top, 72px):
'      └─ TitleBar : "Roles" + "Read-only" pill + Refresh
'    TableWrapper (Dock = Fill, 24px side spacers + 16px top/bottom):
'      └─ listArea (BufferedPanel, PanelRenderer card, Dock = Top):
'           ├─ ColHeader   (Dock = Top,    44px)
'           ├─ _scrollHost (Dock = Fill)
'           └─ bottomPad   (Dock = Bottom, 12px)
'
'  The card is sized to its content rather than Dock=Fill — with only five
'  roles a full-height card would be mostly empty white. It is capped at the
'  viewport height, and _scrollHost keeps AutoScroll as a safety net in case
'  the roles table ever grows past what fits.
'
'  SQL (single round trip — counts come from a LEFT JOIN, not 5 extra queries):
'    SELECT r.role_id, r.role_name, r.description,
'           SUM(CASE WHEN u.is_active = 1 THEN 1 ELSE 0 END) AS active_cnt,
'           COUNT(u.user_id) AS total_cnt
'    FROM roles r LEFT JOIN users u ON u.role_id = r.role_id
'    GROUP BY r.role_id, r.role_name, r.description
'    ORDER BY r.role_id
' =============================================================================
Public Class AdminRolesPanelBuilder

    ' ── PALETTE  (matches AdminUserPanelBuilder / the Accountant panels) ──────
    Private Shared ReadOnly MintDark As Color = Theme.Dark
    Private Shared ReadOnly MintPrimary As Color = Theme.Primary
    Private Shared ReadOnly MintBg As Color = Theme.PageBg
    Private Shared ReadOnly CardBg As Color = Color.White
    Private Shared ReadOnly SubText As Color = Theme.SubText
    Private Shared ReadOnly FaintText As Color = Theme.FaintText
    Private Shared ReadOnly RowDivider As Color = Theme.RowDivider
    Private Shared ReadOnly RowHover As Color = Theme.RowHover

    ' Per-role badge tints, keyed by role_id (1 Admin … 5 Procurement).
    Private Shared ReadOnly TintMint As (fore As Color, back As Color) =
        (Color.FromArgb(0, 120, 92), Color.FromArgb(222, 246, 238))
    Private Shared ReadOnly TintBlue As (fore As Color, back As Color) =
        (Color.FromArgb(45, 80, 150), Color.FromArgb(232, 238, 250))
    Private Shared ReadOnly TintAmber As (fore As Color, back As Color) =
        (Color.FromArgb(165, 110, 20), Color.FromArgb(255, 244, 224))
    Private Shared ReadOnly TintPlum As (fore As Color, back As Color) =
        (Color.FromArgb(110, 60, 140), Color.FromArgb(243, 236, 250))
    Private Shared ReadOnly TintGrey As (fore As Color, back As Color) =
        (Color.FromArgb(90, 114, 107), Color.FromArgb(238, 242, 240))

    ' ── DATA MODEL ────────────────────────────────────────────────────────────
    ' Set by ADMIN_STAFF so a role row can hand the admin straight to the
    ' matching filter in User Management.
    Public Shared NavigateToUsers As Action(Of Integer)

    ' The roles the login screen can actually route. RouteByRole in LogIn.vb is
    ' a Select Case over these exact names, so a role NOT in this set exists in
    ' the database but can never sign in - the login lands on "Unknown role".
    ' Listing them here lets the panel say so instead of showing the row as if
    ' it were usable.
    Private Shared ReadOnly RoutableRoles As String() =
        {"Admin", "Cashier", "Inventory", "Accountant", "Procurement"}

    ' What each role reaches, derived from the dashboard each one is routed to.
    ' Kept beside RoutableRoles so the two are read - and updated - together.
    Private Shared Function ModulesFor(roleName As String) As String
        Select Case roleName
            Case "Admin"
                Return "Users, Roles, Store Settings, Categories, Suppliers, " &
                       "Color Formulas, all Oversight reports, Activity Log"
            Case "Cashier"
                Return "POS terminal, Returns, Cash drawer, Shift reports"
            Case "Inventory"
                Return "Item list, Stock levels, Adjustments, Put-away, Low stock"
            Case "Accountant"
                Return "Income, Ledger, Payables, Sales history, Financial reports"
            Case "Procurement"
                Return "Suppliers, Purchase orders, Quotations, Receiving, Deliveries"
            Case Else
                Return ""
        End Select
    End Function

    Private Shared Function IsRoutable(roleName As String) As Boolean
        For Each n In RoutableRoles
            If String.Equals(n, roleName, StringComparison.OrdinalIgnoreCase) Then Return True
        Next
        Return False
    End Function

    Private Class RoleRecord
        Public RoleId As Integer
        Public RoleName As String
        Public Description As String
        Public ActiveCount As Integer
        Public TotalCount As Integer
    End Class

    ' ── STATE ─────────────────────────────────────────────────────────────────
    Private Const SIDE_PAD As Integer = 24
    Private Const ROW_H As Integer = 64
    Private Const HDR_H As Integer = 44
    Private Const PAD_TOP As Integer = 4
    Private Const PAD_BOTTOM As Integer = 12

    Private Shared _container As Panel
    Private Shared _scrollHost As Panel
    Private Shared _rows As New List(Of RoleRecord)
    Private Shared _reflowAttached As Boolean = False

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Shared Sub LoadRoles(container As Panel)
        _container = container
        LoadData()
        Render()
    End Sub

    ' =========================================================================
    '  DATA
    ' =========================================================================
    Private Shared Sub LoadData()
        _rows.Clear()

        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Dim sql As String =
                    "SELECT r.role_id, r.role_name, r.description, " &
                    "COALESCE(SUM(CASE WHEN u.is_active = 1 THEN 1 ELSE 0 END), 0) AS active_cnt, " &
                    "COUNT(u.user_id) AS total_cnt " &
                    "FROM roles r LEFT JOIN users u ON u.role_id = r.role_id " &
                    "GROUP BY r.role_id, r.role_name, r.description " &
                    "ORDER BY r.role_id"

                Using cmd As New MySqlCommand(sql, conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            _rows.Add(New RoleRecord With {
                                .RoleId = SafeInt(rdr("role_id")),
                                .RoleName = SafeStr(rdr("role_name")),
                                .Description = SafeStr(rdr("description")),
                                .ActiveCount = SafeInt(rdr("active_cnt")),
                                .TotalCount = SafeInt(rdr("total_cnt"))
                            })
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not load roles: " & ex.Message, "Database Error",
                          MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    ' One ToolTip for the whole panel; a per-row instance would leak.
    Private Shared ReadOnly _tips As New ToolTip()

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
        _reflowAttached = False
        _container.BackColor = MintBg

        Dim tableWrapper As New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = MintBg,
            .Padding = New Padding(0)
        }

        ' Card height follows the row count, capped at the viewport.
        Dim contentH As Integer = If(_rows.Count = 0, 90, _rows.Count * ROW_H)
        Dim cardH As Integer = HDR_H + PAD_TOP + contentH + PAD_BOTTOM + 2
        Dim maxH As Integer = _container.ClientSize.Height - 72 - 32
        If maxH > 160 AndAlso cardH > maxH Then cardH = maxH

        Dim listArea As New BufferedPanel With {
            .Dock = DockStyle.Top,
            .Height = cardH,
            .BackColor = CardBg,
            .Padding = New Padding(1)
        }
        AddHandler listArea.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)

        _scrollHost = New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = Color.Transparent,
            .AutoScroll = True,
            .Padding = New Padding(0)
        }

        Dim colHdr As Panel = BuildColHeader()
        colHdr.Dock = DockStyle.Top

        ' Keeps the last row off the card's rounded bottom edge.
        Dim bottomPad As New Panel With {
            .Dock = DockStyle.Bottom,
            .Height = PAD_BOTTOM,
            .BackColor = Color.Transparent
        }

        listArea.Controls.Add(bottomPad)
        listArea.Controls.Add(colHdr)
        listArea.Controls.Add(_scrollHost)
        colHdr.BringToFront()
        _scrollHost.BringToFront()
        _scrollHost.AutoScrollMinSize = New Size(0, 1)

        Dim spacerBottom As New Panel With {.Dock = DockStyle.Bottom, .Height = 16, .BackColor = MintBg}
        Dim spacerTop As New Panel With {.Dock = DockStyle.Top, .Height = 16, .BackColor = MintBg}
        Dim spacerLeft As New Panel With {.Dock = DockStyle.Left, .Width = SIDE_PAD, .BackColor = MintBg}
        Dim spacerRight As New Panel With {.Dock = DockStyle.Right, .Width = SIDE_PAD, .BackColor = MintBg}

        tableWrapper.Controls.Add(listArea)
        tableWrapper.Controls.Add(spacerBottom)
        tableWrapper.Controls.Add(spacerTop)
        tableWrapper.Controls.Add(spacerLeft)
        tableWrapper.Controls.Add(spacerRight)

        Dim topRegion As Panel = BuildTopRegion()
        topRegion.Dock = DockStyle.Top

        _container.Controls.Add(tableWrapper)
        _container.Controls.Add(topRegion)

        _container.ResumeLayout()

        PopulateRows()
    End Sub

    ' =========================================================================
    '  TOP REGION  —  72px title bar
    ' =========================================================================
    Private Shared Function BuildTopRegion() As Panel
        Dim region As New Panel With {
            .BackColor = MintBg, .AutoSize = False, .Height = 72
        }

        Dim titleBar As New Panel With {
            .Height = 72, .Dock = DockStyle.Top, .BackColor = Color.Transparent
        }

        Dim lblTitle As New Label With {
            .Text = "Roles",
            .Font = New Font("Segoe UI", 28, FontStyle.Bold),
            .ForeColor = MintDark,
            .AutoSize = True,
            .Location = New Point(SIDE_PAD, 16)
        }

        ' Makes the read-only nature of this screen obvious.
        Dim pill As New Label With {
            .Text = "Read-only",
            .Font = New Font("Segoe UI Semibold", 9.0F),
            .ForeColor = SubText,
            .BackColor = Color.FromArgb(232, 238, 236),
            .AutoSize = True,
            .Padding = New Padding(10, 5, 10, 5),
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        RoundLabel(pill, 12)

        AddHandler titleBar.Resize,
            Sub()
                pill.Left = titleBar.Width - pill.Width - SIDE_PAD
                pill.Top = (titleBar.Height - pill.Height) \ 2
            End Sub

        titleBar.Controls.Add(lblTitle)
        titleBar.Controls.Add(pill)

        region.Controls.Add(titleBar)
        Return region
    End Function

    ' =========================================================================
    '  COLUMN HEADER  —  44px, proportional (must match BuildDataRow)
    ' =========================================================================
    Private Shared Function BuildColHeader() As Panel
        Dim hdr As New Panel With {.Height = HDR_H, .BackColor = Color.Transparent}

        Dim labels As Label() = {
            MakeColLabel("ROLE", New Point(28, 13)),
            MakeColLabel("WHAT IT REACHES", New Point(0, 13)),
            MakeColLabel("USERS", New Point(0, 13))
        }
        hdr.Controls.AddRange(labels)

        AddHandler hdr.Resize,
            Sub()
                Dim w = hdr.Width
                labels(0).Left = 28
                labels(1).Left = CInt(w * 0.26)
                labels(2).Left = CInt(w * 0.72)
            End Sub

        AddHandler hdr.Paint,
            Sub(s, e)
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 20, hdr.Height - 1, hdr.Width - 20, hdr.Height - 1)
                End Using
            End Sub

        Return hdr
    End Function

    Private Shared Function MakeColLabel(text As String, loc As Point) As Label
        Return New Label With {
            .Text = text,
            .Font = New Font("Segoe UI", 10, FontStyle.Bold),
            .ForeColor = SubText,
            .AutoSize = True,
            .Location = loc,
            .BackColor = Color.Transparent
        }
    End Function

    ' =========================================================================
    '  ROWS
    ' =========================================================================
    Private Shared Sub PopulateRows()
        If _scrollHost Is Nothing Then Return

        _scrollHost.SuspendLayout()
        _scrollHost.Controls.Clear()

        If _reflowAttached Then
            RemoveHandler _scrollHost.Resize, AddressOf ReflowScrollHost
            _reflowAttached = False
        End If

        If _rows.Count = 0 Then
            _scrollHost.Controls.Add(BuildEmptyRow("No roles are defined."))
        Else
            Dim y As Integer = PAD_TOP
            For Each r As RoleRecord In _rows
                Dim row As Panel = BuildDataRow(r, ROW_H)
                row.Location = New Point(0, y)
                row.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
                _scrollHost.Controls.Add(row)
                row.Width = _scrollHost.ClientSize.Width - 4
                y += ROW_H
            Next

            If Not _reflowAttached Then
                _reflowAttached = True
                AddHandler _scrollHost.Resize, AddressOf ReflowScrollHost
            End If
        End If

        _scrollHost.ResumeLayout()
        _scrollHost.Invalidate(True)
    End Sub

    Private Shared Sub ReflowScrollHost(sender As Object, e As EventArgs)
        If _scrollHost Is Nothing Then Return
        Dim w As Integer = _scrollHost.ClientSize.Width - 4
        For Each c As Control In _scrollHost.Controls
            c.Width = w
        Next
    End Sub

    Private Shared Function BuildDataRow(r As RoleRecord, height As Integer) As Panel
        Dim p As New BufferedPanel With {
            .Height = height, .BackColor = Color.Transparent
        }

        AddHandler p.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                If TypeOf p.Tag Is Boolean AndAlso CBool(p.Tag) Then
                    Dim rect As New Rectangle(8, 3, p.Width - 16, height - 6)
                    Using br As New SolidBrush(RowHover)
                        e.Graphics.FillRoundedRectangle(br, rect, 10)
                    End Using
                End If
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 20, p.Height - 1, p.Width - 20, p.Height - 1)
                End Using
            End Sub

        ' No initial-letter avatar. There are five fixed roles with distinct
        ' names, so a coloured "A" adds a second thing to decode and invents a
        ' colour language the rest of the screen does not use. The name is the
        ' identifier.
        Dim lblName As New Label With {
            .Text = If(r.RoleName = "", "—", r.RoleName),
            .Font = New Font("Segoe UI", 12, FontStyle.Bold),
            .ForeColor = MintDark,
            .AutoSize = True, .BackColor = Color.Transparent
        }
        ' The modules this role actually reaches, derived from the routing rather
        ' than from roles.description - a free-text column that can drift out of
        ' step with what the code really allows. Falls back to the stored
        ' description for any role the routing does not know.
        Dim routable As Boolean = IsRoutable(r.RoleName)
        Dim reachText As String = ModulesFor(r.RoleName)
        If reachText = "" Then reachText = If(r.Description = "", "—", r.Description)

        Dim lblDesc As New Label With {
            .Text = reachText,
            .Font = New Font("Segoe UI", 10.5F),
            .ForeColor = If(routable, SubText, FaintText),
            .AutoSize = False,
            .AutoEllipsis = True,
            .Height = 20,
            .BackColor = Color.Transparent
        }
        _tips.SetToolTip(lblDesc, reachText)

        ' "3 users" when every holder is enabled; "3 of 5 active" only when the
        ' two numbers actually differ. Printing "1 active of 1 total" on every
        ' row was two numbers saying one thing.
        Dim pillText As String
        If r.TotalCount = 0 Then
            pillText = "None"
        ElseIf r.ActiveCount = r.TotalCount Then
            pillText = r.TotalCount.ToString("N0") & If(r.TotalCount = 1, " user", " users")
        Else
            pillText = r.ActiveCount.ToString("N0") & " of " & r.TotalCount.ToString("N0") & " active"
        End If

        Dim activePill As New Label With {
            .Text = pillText,
            .Font = New Font("Segoe UI Semibold", 9.0F),
            .ForeColor = If(r.ActiveCount > 0, TintMint.fore, FaintText),
            .BackColor = If(r.ActiveCount > 0, TintMint.back, Color.FromArgb(238, 242, 240)),
            .AutoSize = True,
            .Padding = New Padding(10, 4, 10, 4),
            .Cursor = If(r.TotalCount > 0, Cursors.Hand, Cursors.Default)
        }
        RoundLabel(activePill, 10)

        If r.TotalCount > 0 Then
            _tips.SetToolTip(activePill, "Show these users in User Management")
            AddHandler activePill.Click,
                Sub()
                    If NavigateToUsers IsNot Nothing Then NavigateToUsers(r.RoleId)
                End Sub
        End If

        ' A role the login screen cannot route is a real defect, not a style
        ' point: someone holding it would be refused at sign-in with
        ' "Unknown role". Say so on the row rather than leaving it to be found
        ' by a member of staff who cannot get in.
        Dim lblTotal As New Label With {
            .Text = If(routable, "", "⚠ cannot sign in"),
            .Font = New Font("Segoe UI Semibold", 9),
            .ForeColor = Color.FromArgb(198, 40, 40),
            .AutoSize = True, .BackColor = Color.Transparent
        }
        If Not routable Then
            _tips.SetToolTip(lblTotal,
                "No login route exists for this role name, so anyone assigned it " &
                "would be refused at sign-in.")
        End If

        p.Controls.AddRange({lblName, lblDesc, activePill, lblTotal})

        AddHandler p.Resize,
            Sub()
                Dim w = p.Width
                lblName.Left = 28 : lblName.Top = (height - lblName.Height) \ 2

                Dim descX As Integer = CInt(w * 0.26)
                Dim usersX As Integer = CInt(w * 0.72)
                lblDesc.Left = descX
                lblDesc.Top = (height - lblDesc.Height) \ 2
                lblDesc.Width = Math.Max(40, usersX - descX - 20)

                activePill.Left = usersX
                activePill.Top = (height - activePill.Height) \ 2
                lblTotal.Left = usersX + activePill.Width + 8
                lblTotal.Top = (height - lblTotal.Height) \ 2
            End Sub

        PropagateHover(p)
        Return p
    End Function


    Private Shared Function BuildEmptyRow(message As String) As Panel
        Dim p As New Panel With {
            .Height = 90, .Dock = DockStyle.Top, .BackColor = Color.Transparent
        }
        p.Controls.Add(New Label With {
            .Text = message,
            .Font = New Font("Segoe UI", 10.5F),
            .ForeColor = FaintText,
            .AutoSize = True,
            .Location = New Point(22, 28),
            .BackColor = Color.Transparent
        })
        Return p
    End Function

    ' =========================================================================
    '  SHARED HELPERS  (same implementations as the other panels)
    ' =========================================================================
    Private Shared Sub RoundLabel(lbl As Label, radius As Integer)
        Dim apply As Action =
            Sub()
                If lbl.Width <= radius OrElse lbl.Height <= radius Then Return
                Dim path As New GraphicsPath()
                Dim r As Integer = radius
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
