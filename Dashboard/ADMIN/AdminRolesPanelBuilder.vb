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
    Private Shared ReadOnly MintDark As Color = Color.FromArgb(10, 79, 66)
    Private Shared ReadOnly MintPrimary As Color = Color.FromArgb(0, 168, 142)
    Private Shared ReadOnly MintBg As Color = Color.FromArgb(244, 248, 246)
    Private Shared ReadOnly CardBg As Color = Color.White
    Private Shared ReadOnly SubText As Color = Color.FromArgb(90, 114, 107)
    Private Shared ReadOnly FaintText As Color = Color.FromArgb(150, 165, 160)
    Private Shared ReadOnly RowDivider As Color = Color.FromArgb(238, 242, 240)
    Private Shared ReadOnly RowHover As Color = Color.FromArgb(224, 242, 235)

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

        Dim btnRefresh As New Button With {
            .Text = "⟳  Refresh",
            .FlatStyle = FlatStyle.Flat,
            .Cursor = Cursors.Hand,
            .Size = New Size(100, 34),
            .Font = New Font("Segoe UI", 9.5F),
            .BackColor = Color.FromArgb(220, 235, 232),
            .ForeColor = MintDark,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        btnRefresh.FlatAppearance.BorderSize = 0
        AddHandler btnRefresh.Paint,
            Sub(s, e)
                Dim bb = CType(s, Button)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect = bb.ClientRectangle : rect.Inflate(-1, -1)
                Using br As New SolidBrush(bb.BackColor)
                    g.FillRoundedRectangle(br, rect, 8)
                End Using
                TextRenderer.DrawText(g, bb.Text, bb.Font, bb.ClientRectangle, bb.ForeColor,
                    TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub
        AddHandler btnRefresh.MouseEnter, Sub()
                                              btnRefresh.BackColor = Color.FromArgb(210, 240, 235)
                                              btnRefresh.Invalidate()
                                          End Sub
        AddHandler btnRefresh.MouseLeave, Sub()
                                              btnRefresh.BackColor = Color.FromArgb(220, 235, 232)
                                              btnRefresh.Invalidate()
                                          End Sub
        AddHandler btnRefresh.Click, Sub() LoadRoles(_container)

        AddHandler titleBar.Resize,
            Sub()
                btnRefresh.Left = titleBar.Width - btnRefresh.Width - SIDE_PAD
                btnRefresh.Top = (titleBar.Height - btnRefresh.Height) \ 2
                pill.Left = btnRefresh.Left - pill.Width - 10
                pill.Top = (titleBar.Height - pill.Height) \ 2
            End Sub

        titleBar.Controls.Add(lblTitle)
        titleBar.Controls.Add(pill)
        titleBar.Controls.Add(btnRefresh)

        region.Controls.Add(titleBar)
        Return region
    End Function

    ' =========================================================================
    '  COLUMN HEADER  —  44px, proportional (must match BuildDataRow)
    ' =========================================================================
    Private Shared Function BuildColHeader() As Panel
        Dim hdr As New Panel With {.Height = HDR_H, .BackColor = Color.Transparent}

        Dim labels As Label() = {
            MakeColLabel("ROLE", New Point(72, 13)),
            MakeColLabel("DESCRIPTION", New Point(0, 13)),
            MakeColLabel("USERS", New Point(0, 13))
        }
        hdr.Controls.AddRange(labels)

        AddHandler hdr.Resize,
            Sub()
                Dim w = hdr.Width
                labels(0).Left = 72
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

        Dim tint = TintFor(r.RoleId)

        AddHandler p.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                If TypeOf p.Tag Is Boolean AndAlso CBool(p.Tag) Then
                    Dim rect As New Rectangle(8, 3, p.Width - 16, height - 6)
                    Using br As New SolidBrush(RowHover)
                        e.Graphics.FillRoundedRectangle(br, rect, 10)
                    End Using
                End If
                Using br As New SolidBrush(tint.fore)
                    e.Graphics.FillRectangle(br, New Rectangle(0, 12, 3, height - 24))
                End Using
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 20, p.Height - 1, p.Width - 20, p.Height - 1)
                End Using
            End Sub

        ' Initial-letter badge, tinted per role
        Dim badge As New Label With {
            .Text = If(r.RoleName = "", "?", r.RoleName.Substring(0, 1).ToUpper()),
            .Font = New Font("Segoe UI", 12, FontStyle.Bold),
            .ForeColor = tint.fore,
            .BackColor = tint.back,
            .Size = New Size(34, 34),
            .TextAlign = ContentAlignment.MiddleCenter,
            .Location = New Point(24, (height - 34) \ 2)
        }
        RoundLabel(badge, 17)

        Dim lblName As New Label With {
            .Text = If(r.RoleName = "", "—", r.RoleName),
            .Font = New Font("Segoe UI", 11, FontStyle.Bold),
            .ForeColor = MintDark,
            .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblDesc As New Label With {
            .Text = If(r.Description = "", "—", r.Description),
            .Font = New Font("Segoe UI", 10.5F),
            .ForeColor = If(r.Description = "", FaintText, SubText),
            .AutoSize = False,
            .AutoEllipsis = True,
            .Height = 20,
            .BackColor = Color.Transparent
        }

        ' "X active" pill + "of Y total"
        Dim activePill As New Label With {
            .Text = r.ActiveCount.ToString("N0") & " active",
            .Font = New Font("Segoe UI Semibold", 9.0F),
            .ForeColor = If(r.ActiveCount > 0, TintMint.fore, FaintText),
            .BackColor = If(r.ActiveCount > 0, TintMint.back, Color.FromArgb(238, 242, 240)),
            .AutoSize = True,
            .Padding = New Padding(10, 4, 10, 4)
        }
        RoundLabel(activePill, 10)

        Dim lblTotal As New Label With {
            .Text = "of " & r.TotalCount.ToString("N0") & " total",
            .Font = New Font("Segoe UI", 10),
            .ForeColor = FaintText,
            .AutoSize = True, .BackColor = Color.Transparent
        }

        p.Controls.AddRange({badge, lblName, lblDesc, activePill, lblTotal})

        AddHandler p.Resize,
            Sub()
                Dim w = p.Width
                badge.Left = 24 : badge.Top = (height - badge.Height) \ 2
                lblName.Left = 72 : lblName.Top = (height - lblName.Height) \ 2

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

    Private Shared Function TintFor(roleId As Integer) As (fore As Color, back As Color)
        Select Case roleId
            Case 1 : Return TintMint      ' Admin
            Case 2 : Return TintBlue      ' Cashier
            Case 3 : Return TintAmber     ' Inventory
            Case 4 : Return TintPlum      ' Accountant
            Case 5 : Return TintGrey      ' Procurement
            Case Else : Return TintGrey
        End Select
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
