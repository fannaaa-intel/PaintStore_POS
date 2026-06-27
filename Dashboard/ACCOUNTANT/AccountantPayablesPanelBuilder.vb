Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports MySql.Data.MySqlClient

' =============================================================================
'  AccountantPayablesPanelBuilder  —  Screen 3: Payables
'
'  LAYOUT
'  ------
'    Top bar    (Dock = Top, 64px)  : dark gradient — title + date
'    Filter bar (Dock = Top, 48px)  : status tab pills — All | Draft | Ordered | Partially Received
'    List card  (Dock = Fill)       : scrollable PO rows with overdue highlight
'    Totals bar (Dock = Bottom, 64px): Total Payable + order count
' =============================================================================
Public Class AccountantPayablesPanelBuilder

    ' ── PALETTE ──────────────────────────────────────────────────────────────
    Private Shared ReadOnly MintDark As Color = Color.FromArgb(0, 80, 72)
    Private Shared ReadOnly MintPrimary As Color = Color.FromArgb(0, 200, 170)
    Private Shared ReadOnly MintDeep As Color = Color.FromArgb(0, 130, 109)
    Private Shared ReadOnly MintBg As Color = Color.FromArgb(244, 248, 246)
    Private Shared ReadOnly HeaderBg As Color = Color.FromArgb(0, 60, 54)
    Private Shared ReadOnly CardBg As Color = Color.White
    Private Shared ReadOnly SubText As Color = Color.FromArgb(90, 114, 107)
    Private Shared ReadOnly FaintText As Color = Color.FromArgb(150, 165, 160)
    Private Shared ReadOnly RowDivider As Color = Color.FromArgb(238, 242, 240)
    Private Shared ReadOnly RowHover As Color = Color.FromArgb(224, 242, 235)
    Private Shared ReadOnly AccentOrange As Color = Color.FromArgb(255, 160, 50)
    Private Shared ReadOnly AccentBlue As Color = Color.FromArgb(80, 160, 255)
    Private Shared ReadOnly AccentRed As Color = Color.FromArgb(220, 80, 80)
    Private Shared ReadOnly Peso As String = ChrW(8369)

    ' ── DATA MODEL ───────────────────────────────────────────────────────────
    Private Class PayableRow
        Public PoNo As String
        Public Supplier As String
        Public OrderDate As DateTime
        Public HasExpected As Boolean
        Public ExpectedDate As DateTime
        Public Amount As Decimal
        Public Status As String
    End Class

    ' ── STATE ────────────────────────────────────────────────────────────────
    Private Shared _container As Panel
    Private Shared _filter As String = "All"
    Private Shared _rows As New List(Of PayableRow)
    Private Shared _totalPayable As Decimal
    Private Shared _lblTotal As Label
    Private Shared _lblCount As Label

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Shared Sub LoadPayables(container As Panel, Optional filter As String = "All")
        _container = container
        _filter = filter
        LoadData()
        Render()
    End Sub

    ' =========================================================================
    '  DATA
    ' =========================================================================
    Private Shared Sub LoadData()
        _rows.Clear()
        _totalPayable = 0

        Dim statusWhere As String = BuildStatusWhere()

        Try
            Using conn = Db.GetConnection()
                conn.Open()

                Using cmd As New MySqlCommand(
                    "SELECT po.po_no, s.name AS sname, po.order_date, " &
                    "po.expected_date, po.total_cost, po.status " &
                    "FROM purchase_orders po " &
                    "JOIN suppliers s ON po.supplier_id = s.supplier_id " &
                    "WHERE po.status NOT IN ('Received','Cancelled')" & statusWhere &
                    " ORDER BY po.order_date ASC", conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            Dim hasExp As Boolean = Not IsDBNull(rdr("expected_date"))
                            _rows.Add(New PayableRow With {
                                .PoNo = rdr("po_no").ToString(),
                                .Supplier = rdr("sname").ToString(),
                                .OrderDate = Convert.ToDateTime(rdr("order_date")),
                                .HasExpected = hasExp,
                                .ExpectedDate = If(hasExp, Convert.ToDateTime(rdr("expected_date")), DateTime.MinValue),
                                .Amount = Convert.ToDecimal(rdr("total_cost")),
                                .Status = rdr("status").ToString()
                            })
                        End While
                    End Using
                End Using

                Using cmd As New MySqlCommand(
                    "SELECT COALESCE(SUM(total_cost),0) FROM purchase_orders " &
                    "WHERE status NOT IN ('Received','Cancelled')" & statusWhere, conn)
                    _totalPayable = Convert.ToDecimal(cmd.ExecuteScalar())
                End Using
            End Using
        Catch ex As Exception
            Debug.WriteLine("AccountantPayables LoadData error: " & ex.Message)
        End Try
    End Sub

    Private Shared Function BuildStatusWhere() As String
        Select Case _filter
            Case "Draft" : Return " AND po.status = 'Draft'"
            Case "Ordered" : Return " AND po.status = 'Ordered'"
            Case "Partially Received" : Return " AND po.status = 'Partially Received'"
            Case Else : Return ""
        End Select
    End Function

    ' =========================================================================
    '  RENDER
    ' =========================================================================
    Private Shared Sub Render()
        _container.SuspendLayout()
        _container.Controls.Clear()
        _container.BackColor = MintBg

        ' Totals bar — bottom, add first
        Dim totalsBar As Panel = BuildTotalsBar()
        totalsBar.Dock = DockStyle.Bottom
        totalsBar.Height = 64

        ' List card — fill
        Dim listCard As New BufferedPanel With {
            .Dock = DockStyle.Fill,
            .BackColor = CardBg,
            .Margin = New Padding(24, 0, 24, 0)
        }
        AddHandler listCard.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)

        Dim colHdr As Panel = BuildColHeader()
        colHdr.Dock = DockStyle.Top
        listCard.Controls.Add(colHdr)

        Dim rowsHost As New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = Color.Transparent,
            .AutoScroll = True,
            .Padding = New Padding(0, 4, 0, 8)
        }
        listCard.Controls.Add(rowsHost)
        rowsHost.BringToFront()

        If _rows.Count = 0 Then
            rowsHost.Controls.Add(BuildEmptyRow("No outstanding payables found."))
        Else
            Dim y As Integer = 4
            For Each row In _rows
                Dim r As Panel = BuildPayableRow(row, 56)
                r.Location = New Point(0, y)
                r.Width = rowsHost.ClientSize.Width - 4
                r.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
                rowsHost.Controls.Add(r)
                y += 56
            Next
            AddHandler rowsHost.Resize, Sub() ReflowRows(rowsHost)
        End If

        ' Wrapper with padding around the list card
        Dim listWrapper As New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = MintBg,
            .Padding = New Padding(24, 8, 24, 12)
        }
        listWrapper.Controls.Add(listCard)

        ' Filter bar
        Dim filterBar As Panel = BuildFilterBar()
        filterBar.Dock = DockStyle.Top
        filterBar.Height = 52

        ' Top bar
        Dim topBar As Panel = BuildTopBar()
        topBar.Dock = DockStyle.Top
        topBar.Height = 64

        _container.Controls.Add(totalsBar)
        _container.Controls.Add(listWrapper)
        _container.Controls.Add(filterBar)
        _container.Controls.Add(topBar)
        _container.ResumeLayout()
    End Sub

    ' =========================================================================
    '  TOP BAR
    ' =========================================================================
    Private Shared Function BuildTopBar() As Panel
        Dim bar As New BufferedPanel With {.BackColor = MintDark}
        AddHandler bar.Paint,
            Sub(s, e)
                Using br As New LinearGradientBrush(bar.ClientRectangle, MintDark, HeaderBg, 0.0F)
                    e.Graphics.FillRectangle(br, bar.ClientRectangle)
                End Using
            End Sub

        Dim lblTitle As New Label With {
            .Text = "Payables",
            .Font = New Font("Segoe UI Semibold", 13, FontStyle.Bold),
            .ForeColor = Color.White, .BackColor = Color.Transparent,
            .AutoSize = True, .Location = New Point(22, 10)
        }
        Dim lblSub As New Label With {
            .Text = "Outstanding purchase orders owed to suppliers",
            .Font = New Font("Segoe UI", 9),
            .ForeColor = Color.FromArgb(160, 215, 200),
            .BackColor = Color.Transparent,
            .AutoSize = True, .Location = New Point(24, 36)
        }
        Dim lblDate As New Label With {
            .Text = DateTime.Now.ToString("MMMM dd, yyyy").ToUpper(),
            .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = Color.FromArgb(200, 240, 225),
            .BackColor = Color.Transparent,
            .AutoSize = True,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }
        bar.Controls.AddRange({lblTitle, lblSub, lblDate})
        AddHandler bar.Resize,
            Sub()
                lblDate.Left = bar.Width - lblDate.Width - 22
                lblDate.Top = (bar.Height - lblDate.Height) \ 2
            End Sub
        Return bar
    End Function

    ' =========================================================================
    '  FILTER BAR
    ' =========================================================================
    Private Shared Function BuildFilterBar() As Panel
        Dim bar As New Panel With {
            .BackColor = Color.FromArgb(250, 252, 251),
            .Padding = New Padding(24, 10, 24, 10)
        }

        Dim tabs() As String = {"All", "Draft", "Ordered", "Partially Received"}
        Dim pillX As Integer = 24

        For Each TAB As String In tabs
            Dim capTab As String = TAB
            Dim isActive As Boolean = (_filter = TAB)

            Dim pill As New Label With {
        .Text = TAB,
                .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold),
                .AutoSize = True,
                .Padding = New Padding(14, 4, 14, 4),
                .Cursor = Cursors.Hand,
                .Location = New Point(pillX, 10),
                .ForeColor = If(isActive, Color.White, SubText),
                .BackColor = If(isActive, MintPrimary, Color.FromArgb(230, 238, 235))
            }
            RoundLabel(pill, 12)
            AddHandler pill.Click, Sub() LoadPayables(_container, capTab)
            bar.Controls.Add(pill)
            pillX += pill.PreferredWidth + 28 + 8
        Next

        AddHandler bar.Paint,
            Sub(s, e)
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 0, bar.Height - 1, bar.Width, bar.Height - 1)
                End Using
            End Sub

        Return bar
    End Function

    ' =========================================================================
    '  COLUMN HEADER
    ' =========================================================================
    Private Shared Function BuildColHeader() As Panel
        Dim hdr As New Panel With {.Height = 34, .BackColor = Color.FromArgb(248, 250, 249)}

        Dim chPo As New Label With {.Text = "PO NO", .Font = New Font("Segoe UI", 9, FontStyle.Bold), .ForeColor = SubText, .AutoSize = True, .Location = New Point(18, 9), .BackColor = Color.Transparent}
        Dim chSupplier As New Label With {.Text = "SUPPLIER", .Font = New Font("Segoe UI", 9, FontStyle.Bold), .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent}
        Dim chOrder As New Label With {.Text = "ORDER DATE", .Font = New Font("Segoe UI", 9, FontStyle.Bold), .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent}
        Dim chExpected As New Label With {.Text = "EXPECTED", .Font = New Font("Segoe UI", 9, FontStyle.Bold), .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent}
        Dim chStatus As New Label With {.Text = "STATUS", .Font = New Font("Segoe UI", 9, FontStyle.Bold), .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent}
        Dim chAmount As New Label With {.Text = "AMOUNT", .Font = New Font("Segoe UI", 9, FontStyle.Bold), .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent, .Anchor = AnchorStyles.Top Or AnchorStyles.Right}

        hdr.Controls.AddRange({chPo, chSupplier, chOrder, chExpected, chStatus, chAmount})

        AddHandler hdr.Resize,
            Sub()
                Dim w = hdr.Width
                chPo.Left = 18
                chSupplier.Left = CInt(w * 0.18) : chSupplier.Top = 9
                chOrder.Left = CInt(w * 0.38) : chOrder.Top = 9
                chExpected.Left = CInt(w * 0.52) : chExpected.Top = 9
                chStatus.Left = CInt(w * 0.66) : chStatus.Top = 9
                chAmount.Left = w - chAmount.Width - 18 : chAmount.Top = 9
            End Sub

        AddHandler hdr.Paint,
            Sub(s, e)
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 16, hdr.Height - 1, hdr.Width - 16, hdr.Height - 1)
                End Using
            End Sub

        Return hdr
    End Function

    ' =========================================================================
    '  PAYABLE ROW
    ' =========================================================================
    Private Shared Function BuildPayableRow(row As PayableRow, height As Integer) As Panel
        Dim isOverdue As Boolean = row.HasExpected AndAlso
                                   row.ExpectedDate.Date < DateTime.Today AndAlso
                                   row.Status <> "Received"

        Dim accentColor As Color = If(isOverdue, AccentRed, AccentOrange)

        Dim p As New BufferedPanel With {.Height = height, .BackColor = Color.Transparent}
        AddHandler p.Paint,
            Sub(s, e)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                If TypeOf p.Tag Is Boolean AndAlso CBool(p.Tag) Then
                    Dim rect As New Rectangle(8, 3, p.Width - 16, p.Height - 6)
                    Using path As GraphicsPath = RoundRect(rect, 10)
                        Using b As New SolidBrush(RowHover)
                            e.Graphics.FillPath(b, path)
                        End Using
                    End Using
                End If
                Using br As New SolidBrush(accentColor)
                    e.Graphics.FillRectangle(br, New Rectangle(0, 10, 3, height - 20))
                End Using
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 16, p.Height - 1, p.Width - 16, p.Height - 1)
                End Using
            End Sub

        Dim lblPo As New Label With {
            .Text = row.PoNo,
            .Font = New Font("Segoe UI", 10, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblSupplier As New Label With {
            .Text = row.Supplier,
            .Font = New Font("Segoe UI", 10),
            .ForeColor = SubText, .AutoSize = True, .BackColor = Color.Transparent
        }
        Dim lblOrderDate As New Label With {
            .Text = row.OrderDate.ToString("MMM dd, yyyy"),
            .Font = New Font("Segoe UI", 10),
            .ForeColor = FaintText, .AutoSize = True, .BackColor = Color.Transparent
        }

        ' Expected date — red if overdue
        Dim expectedText As String = If(row.HasExpected, row.ExpectedDate.ToString("MMM dd, yyyy"), "—")
        Dim lblExpected As New Label With {
            .Text = expectedText,
            .Font = New Font("Segoe UI", 10, If(isOverdue, FontStyle.Bold, FontStyle.Regular)),
            .ForeColor = If(isOverdue, AccentRed, FaintText),
            .AutoSize = True, .BackColor = Color.Transparent
        }

        ' Status badge
        Dim badgeInfo = GetStatusBadge(row.Status)
        Dim badge As New Label With {
            .Text = row.Status,
            .Font = New Font("Segoe UI", 8.25F, FontStyle.Bold),
            .ForeColor = badgeInfo.fore,
            .BackColor = badgeInfo.back,
            .AutoSize = True,
            .Padding = New Padding(8, 3, 8, 3)
        }
        RoundLabel(badge, 10)

        Dim lblAmount As New Label With {
            .Text = Peso & row.Amount.ToString("N2"),
            .Font = New Font("Segoe UI", 10.5F, FontStyle.Bold),
            .ForeColor = AccentOrange,
            .AutoSize = True, .BackColor = Color.Transparent,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Right
        }

        ' Overdue tag
        If isOverdue Then
            Dim overdueTag As New Label With {
                .Text = "OVERDUE",
                .Font = New Font("Segoe UI", 7.5F, FontStyle.Bold),
                .ForeColor = Color.White,
                .BackColor = AccentRed,
                .AutoSize = True,
                .Padding = New Padding(5, 2, 5, 2)
            }
            RoundLabel(overdueTag, 8)
            p.Controls.Add(overdueTag)
            AddHandler p.Resize,
                Sub()
                    overdueTag.Left = CInt(p.Width * 0.66)
                    overdueTag.Top = (height - overdueTag.Height) \ 2 - 10
                End Sub
        End If

        p.Controls.AddRange({lblPo, lblSupplier, lblOrderDate, lblExpected, badge, lblAmount})

        AddHandler p.Resize,
            Sub()
                Dim w = p.Width
                lblPo.Left = 14 : lblPo.Top = (height - lblPo.Height) \ 2
                lblSupplier.Left = CInt(w * 0.18) : lblSupplier.Top = (height - lblSupplier.Height) \ 2
                lblOrderDate.Left = CInt(w * 0.38) : lblOrderDate.Top = (height - lblOrderDate.Height) \ 2
                lblExpected.Left = CInt(w * 0.52) : lblExpected.Top = (height - lblExpected.Height) \ 2
                badge.Left = CInt(w * 0.66) : badge.Top = (height - badge.Height) \ 2
                lblAmount.Left = w - lblAmount.Width - 18 : lblAmount.Top = (height - lblAmount.Height) \ 2
            End Sub

        PropagateHover(p)
        Return p
    End Function

    ' =========================================================================
    '  TOTALS BAR
    ' =========================================================================
    Private Shared Function BuildTotalsBar() As Panel
        Dim bar As New BufferedPanel With {.BackColor = CardBg}
        AddHandler bar.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)

        Dim lblCapTotal As New Label With {
            .Text = "Total Payable",
            .Font = New Font("Segoe UI", 9),
            .ForeColor = FaintText, .AutoSize = True,
            .BackColor = Color.Transparent
        }
        _lblTotal = New Label With {
            .Text = Peso & _totalPayable.ToString("N2"),
            .Font = New Font("Segoe UI", 16, FontStyle.Bold),
            .ForeColor = AccentOrange, .AutoSize = True,
            .BackColor = Color.Transparent
        }
        Dim lblCapCount As New Label With {
            .Text = "Outstanding Orders",
            .Font = New Font("Segoe UI", 9),
            .ForeColor = FaintText, .AutoSize = True,
            .BackColor = Color.Transparent
        }
        _lblCount = New Label With {
            .Text = _rows.Count & If(_rows.Count = 1, " order", " orders"),
            .Font = New Font("Segoe UI", 16, FontStyle.Bold),
            .ForeColor = MintDeep, .AutoSize = True,
            .BackColor = Color.Transparent
        }

        bar.Controls.AddRange({lblCapTotal, _lblTotal, lblCapCount, _lblCount})

        AddHandler bar.Resize,
            Sub()
                Dim w = bar.Width
                lblCapTotal.Left = 28 : lblCapTotal.Top = 10
                _lblTotal.Left = 28 : _lblTotal.Top = 28

                lblCapCount.Left = CInt(w * 0.35) : lblCapCount.Top = 10
                _lblCount.Left = CInt(w * 0.35) : _lblCount.Top = 28
            End Sub

        AddHandler bar.Paint,
            Sub(s, e)
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 0, 0, bar.Width, 0)
                End Using
            End Sub

        Return bar
    End Function

    ' =========================================================================
    '  BADGE + HELPERS
    ' =========================================================================
    Private Shared Function GetStatusBadge(status As String) As (fore As Color, back As Color)
        Select Case status
            Case "Draft" : Return (Color.FromArgb(120, 120, 120), Color.FromArgb(238, 238, 238))
            Case "Ordered" : Return (AccentOrange, Color.FromArgb(255, 240, 220))
            Case "Partially Received" : Return (AccentBlue, Color.FromArgb(220, 235, 255))
            Case Else : Return (SubText, Color.FromArgb(238, 238, 238))
        End Select
    End Function

    Private Shared Function BuildEmptyRow(message As String) As Panel
        Dim p As New Panel With {.Height = 90, .Dock = DockStyle.Top, .BackColor = Color.Transparent}
        Dim lbl As New Label With {
            .Text = message, .Font = New Font("Segoe UI", 10.5F),
            .ForeColor = SubText, .AutoSize = True,
            .Location = New Point(20, 28), .BackColor = Color.Transparent
        }
        p.Controls.Add(lbl)
        Return p
    End Function

    Private Shared Sub ReflowRows(host As Panel)
        If host Is Nothing Then Return
        Dim w As Integer = host.ClientSize.Width - 4
        For Each c As Control In host.Controls
            c.Width = w
        Next
    End Sub

    Private Shared Function RoundRect(rect As Rectangle, radius As Integer) As GraphicsPath
        Dim path As New GraphicsPath()
        Dim r As Integer = radius
        path.AddArc(rect.X, rect.Y, r, r, 180, 90)
        path.AddArc(rect.Right - r, rect.Y, r, r, 270, 90)
        path.AddArc(rect.Right - r, rect.Bottom - r, r, r, 0, 90)
        path.AddArc(rect.X, rect.Bottom - r, r, r, 90, 90)
        path.CloseFigure()
        Return path
    End Function

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