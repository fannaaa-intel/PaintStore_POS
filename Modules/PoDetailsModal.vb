Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports MySql.Data.MySqlClient

' =============================================================================
'  PoDetailsModal — one purchase order at a glance, with the items bought.
'
'  Opened by clicking a P.O. number in Accountant > Payables (the payment list)
'  and in Procurement > Reports. Before this, Payables showed a plain text box
'  with the supplier and total only; nobody could see WHAT the money was for.
'
'  Shows:
'    * header facts   supplier, dates, delivery status, payment status
'    * items table    product, qty ordered, qty received, unit cost, line total
'    * problems       any defective / missing units found at receiving, under
'                     the product they belong to (see DeliveryIssues)
'
'  An optional primary button (e.g. "Record Payment") lets the caller put its
'  own action on the window. Show returns True only when that button was used.
' =============================================================================
Public Module PoDetailsModal

    Private ReadOnly CardBg As Color = Color.White
    Private ReadOnly TitleClr As Color = Color.FromArgb(25, 40, 35)
    Private ReadOnly BorderClr As Color = Color.FromArgb(160, 195, 185)
    Private ReadOnly DividerClr As Color = Color.FromArgb(230, 238, 235)
    Private ReadOnly Peso As String = ChrW(8369)

    Private Const RADIUS As Integer = 10
    Private Const CARD_W As Integer = 820
    Private Const PAD As Integer = 26
    Private Const MAX_TABLE_H As Integer = 300

    Private Class Header
        Public PoNo As String = ""
        Public Supplier As String = ""
        Public OrderDate As DateTime
        Public ExpectedDate As DateTime?
        Public Status As String = ""
        Public TotalCost As Decimal
        Public PaidAt As DateTime?
        Public AmountPaid As Decimal?
        Public PayMethod As String = ""
        Public CreatedBy As String = ""
    End Class

    Private Class Line
        Public ProductId As Integer
        Public Name As String = ""
        Public Sku As String = ""
        Public Qty As Integer
        Public Received As Integer
        Public UnitCost As Decimal
        Public LineCost As Decimal
    End Class

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Function Show(poId As Integer,
                         Optional actionText As String = Nothing,
                         Optional footNote As String = Nothing) As Boolean
        Dim hdr As Header = Nothing
        Dim lines As New List(Of Line)
        Try
            hdr = LoadHeader(poId)
            If hdr IsNot Nothing Then lines = LoadLines(poId)
        Catch ex As Exception
            AppModal.Show("Could not load the purchase order:" & Environment.NewLine & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return False
        End Try
        If hdr Is Nothing Then
            AppModal.Show("That purchase order could not be found.", "Not Found",
                          MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return False
        End If

        Dim issues As List(Of DeliveryIssues.Issue) = DeliveryIssues.ForPo(poId)
        Dim innerW As Integer = CARD_W - PAD * 2
        Dim acted As Boolean = False

        Dim f As New Form With {
            .FormBorderStyle = FormBorderStyle.None,
            .StartPosition = FormStartPosition.CenterParent,
            .BackColor = CardBg, .ShowInTaskbar = False,
            .Width = CARD_W, .KeyPreview = True
        }

        ' ── Title ───────────────────────────────────────────────────────────
        Dim lblTitle As New Label With {
            .Text = hdr.PoNo, .Font = New Font("Segoe UI Semibold", 16.0F),
            .ForeColor = Theme.Dark, .AutoSize = True,
            .Location = New Point(PAD, PAD - 4), .BackColor = Color.Transparent
        }
        Dim lblSub As New Label With {
            .Text = hdr.Supplier & If(hdr.CreatedBy = "", "", "   " & ChrW(8226) & "   prepared by " & hdr.CreatedBy),
            .Font = New Font("Segoe UI", 10.0F), .ForeColor = Theme.SubText,
            .AutoSize = True, .Location = New Point(PAD + 2, lblTitle.Bottom + 2),
            .BackColor = Color.Transparent
        }
        f.Controls.AddRange({lblTitle, lblSub})

        Dim stBadge As Label = MakeBadge(hdr.Status, StatusColors(hdr.Status))
        stBadge.Location = New Point(CARD_W - PAD - stBadge.PreferredSize.Width, PAD)
        f.Controls.Add(stBadge)

        ' ── Facts strip ─────────────────────────────────────────────────────
        Dim factsTop As Integer = lblSub.Bottom + 16
        Dim facts As New Panel With {
            .Location = New Point(PAD, factsTop), .Size = New Size(innerW, 64),
            .BackColor = Theme.TileBg
        }
        AddHandler facts.Paint,
            Sub(s As Object, e As PaintEventArgs)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Using path As GraphicsPath = RoundedPath(New Rectangle(0, 0, facts.Width - 1, facts.Height - 1), 8)
                    Using br As New SolidBrush(Theme.TileBg)
                        e.Graphics.FillPath(br, path)
                    End Using
                End Using
            End Sub

        Dim payText As String
        Dim payClr As Color
        Dim paidSum As Decimal = If(hdr.AmountPaid, 0D)
        Dim payStatus As String = SupplierPayments.StatusOf(hdr.PaidAt.HasValue, paidSum)
        payClr = SupplierPayments.StatusColors(payStatus).fore
        If hdr.PaidAt.HasValue Then
            payText = "Paid " & hdr.PaidAt.Value.ToString("MMM d, yyyy")
            If hdr.AmountPaid.HasValue Then payText &= "  (" & Peso & paidSum.ToString("N2") & ")"
        ElseIf payStatus = SupplierPayments.STATUS_PARTIAL Then
            payText = "Partial: " & Peso & paidSum.ToString("N2") & " paid"
        Else
            payText = "Unpaid"
        End If

        Dim factList As New List(Of (cap As String, val As String, clr As Color)) From {
            ("ORDERED", hdr.OrderDate.ToString("MMM d, yyyy"), TitleClr),
            ("EXPECTED", If(hdr.ExpectedDate.HasValue, hdr.ExpectedDate.Value.ToString("MMM d, yyyy"), ChrW(8212)), TitleClr),
            ("PAYMENT", payText, payClr),
            ("TOTAL COST", Peso & hdr.TotalCost.ToString("N2"), Theme.Dark)
        }
        Dim colW As Integer = innerW \ factList.Count
        For i As Integer = 0 To factList.Count - 1
            facts.Controls.Add(New Label With {
                .Text = factList(i).cap, .Font = New Font("Segoe UI Semibold", 8.0F),
                .ForeColor = Theme.FaintText, .AutoSize = True,
                .Location = New Point(16 + i * colW, 12), .BackColor = Color.Transparent
            })
            facts.Controls.Add(New Label With {
                .Text = factList(i).val, .Font = New Font("Segoe UI Semibold", 10.5F),
                .ForeColor = factList(i).clr, .AutoSize = False, .AutoEllipsis = True,
                .Size = New Size(colW - 20, 22),
                .Location = New Point(16 + i * colW, 30), .BackColor = Color.Transparent
            })
        Next
        f.Controls.Add(facts)

        ' ── Items table ─────────────────────────────────────────────────────
        Dim capItems As New Label With {
            .Text = "ITEMS BOUGHT  (" & lines.Count & ")",
            .Font = New Font("Segoe UI Semibold", 9.0F), .ForeColor = Theme.SubText,
            .AutoSize = True, .Location = New Point(PAD, facts.Bottom + 18),
            .BackColor = Color.Transparent
        }
        f.Controls.Add(capItems)

        ' Column x positions inside the table (relative to innerW).
        Dim xQty As Integer = CInt(innerW * 0.5)
        Dim xRcv As Integer = CInt(innerW * 0.61)
        Dim xUnit As Integer = CInt(innerW * 0.72)
        Dim xTotal As Integer = innerW - 12
        Const NUM_W As Integer = 90

        Dim head As New Panel With {
            .Location = New Point(PAD, capItems.Bottom + 6), .Size = New Size(innerW, 30),
            .BackColor = Color.Transparent
        }
        AddHandler head.Paint,
            Sub(s As Object, e As PaintEventArgs)
                Using pen As New Pen(Theme.Border, 1)
                    e.Graphics.DrawLine(pen, 0, head.Height - 1, head.Width, head.Height - 1)
                End Using
            End Sub
        Dim addHead As Action(Of String, Integer, Boolean) =
            Sub(text As String, x As Integer, rightAlign As Boolean)
                Dim l As New Label With {
                    .Text = text, .Font = New Font("Segoe UI Semibold", 8.5F),
                    .ForeColor = Theme.FaintText, .AutoSize = False,
                    .Size = New Size(If(rightAlign, NUM_W, 300), 18),
                    .TextAlign = If(rightAlign, ContentAlignment.MiddleRight, ContentAlignment.MiddleLeft),
                    .Location = New Point(If(rightAlign, x - NUM_W, x), 6), .BackColor = Color.Transparent
                }
                head.Controls.Add(l)
            End Sub
        addHead("PRODUCT", 12, False)
        addHead("ORDERED", xQty + NUM_W \ 2, True)
        addHead("RECEIVED", xRcv + NUM_W \ 2, True)
        addHead("UNIT COST", xUnit + NUM_W \ 2 + 10, True)
        addHead("LINE TOTAL", xTotal, True)
        f.Controls.Add(head)

        Dim body As New Panel With {
            .Location = New Point(PAD, head.Bottom), .Width = innerW,
            .BackColor = CardBg, .AutoScroll = True
        }
        Dim y As Integer = 0
        If lines.Count = 0 Then
            body.Controls.Add(New Label With {
                .Text = "This purchase order has no items.", .Font = New Font("Segoe UI", 10.0F),
                .ForeColor = Theme.FaintText, .AutoSize = True, .Location = New Point(12, 14),
                .BackColor = Color.Transparent
            })
            y = 48
        Else
            For Each ln In lines
                Dim lnIssues = issues.Where(Function(i) i.ProductId = ln.ProductId).ToList()
                Dim rowH As Integer = 50 + lnIssues.Count * 20
                Dim row As New Panel With {
                    .Location = New Point(0, y), .Size = New Size(innerW - 18, rowH),
                    .BackColor = CardBg
                }
                AddHandler row.Paint,
                    Sub(s As Object, e As PaintEventArgs)
                        Dim p = CType(s, Panel)
                        Using pen As New Pen(Theme.RowDivider, 1)
                            e.Graphics.DrawLine(pen, 0, p.Height - 1, p.Width, p.Height - 1)
                        End Using
                    End Sub
                row.Controls.Add(New Label With {
                    .Text = ln.Name, .Font = New Font("Segoe UI Semibold", 10.5F),
                    .ForeColor = Theme.Dark, .AutoSize = False, .AutoEllipsis = True,
                    .Size = New Size(xQty - NUM_W \ 2 - 20, 20), .Location = New Point(12, 7),
                    .BackColor = Color.Transparent, .UseMnemonic = False
                })
                row.Controls.Add(New Label With {
                    .Text = ln.Sku, .Font = New Font("Segoe UI", 8.5F),
                    .ForeColor = Theme.FaintText, .AutoSize = True, .Location = New Point(12, 27),
                    .BackColor = Color.Transparent
                })
                Dim addNum As Action(Of String, Integer, Color, Boolean) =
                    Sub(text As String, rightEdge As Integer, clr As Color, bold As Boolean)
                        row.Controls.Add(New Label With {
                            .Text = text,
                            .Font = New Font(If(bold, "Segoe UI Semibold", "Segoe UI"), 10.0F),
                            .ForeColor = clr, .AutoSize = False, .Size = New Size(NUM_W, 20),
                            .TextAlign = ContentAlignment.MiddleRight,
                            .Location = New Point(rightEdge - NUM_W, 14), .BackColor = Color.Transparent
                        })
                    End Sub
                addNum(ln.Qty.ToString("N0"), xQty + NUM_W \ 2, TitleClr, False)
                addNum(ln.Received.ToString("N0"), xRcv + NUM_W \ 2,
                       If(ln.Received >= ln.Qty, Theme.Deep, If(ln.Received > 0, Theme.Warning, Theme.FaintText)), False)
                addNum(Peso & ln.UnitCost.ToString("N2"), xUnit + NUM_W \ 2 + 10, Theme.SubText, False)
                addNum(Peso & ln.LineCost.ToString("N2"), xTotal - 18, Theme.Dark, True)

                Dim iy As Integer = 46
                For Each iss In lnIssues
                    row.Controls.Add(New Label With {
                        .Text = ChrW(9888) & "  " & iss.Summary,
                        .Font = New Font("Segoe UI", 9.0F),
                        .ForeColor = If(iss.Action = DeliveryIssues.ACTION_ACCEPT, Theme.Warning, Theme.Danger),
                        .AutoSize = False, .AutoEllipsis = True,
                        .Size = New Size(innerW - 60, 18), .Location = New Point(12, iy - 2),
                        .BackColor = Color.Transparent, .UseMnemonic = False
                    })
                    iy += 20
                Next
                body.Controls.Add(row)
                y += rowH
            Next
        End If
        body.Height = Math.Min(MAX_TABLE_H, Math.Max(48, y + 2))
        f.Controls.Add(body)

        ' ── Total line ──────────────────────────────────────────────────────
        Dim lblTotal As New Label With {
            .Text = "TOTAL   " & Peso & hdr.TotalCost.ToString("N2"),
            .Font = New Font("Segoe UI", 12.5F, FontStyle.Bold), .ForeColor = Theme.Dark,
            .AutoSize = False, .Size = New Size(innerW, 26),
            .TextAlign = ContentAlignment.MiddleRight,
            .Location = New Point(PAD, body.Bottom + 10), .BackColor = Color.Transparent
        }
        f.Controls.Add(lblTotal)

        Dim nextY As Integer = lblTotal.Bottom + 6
        If Not String.IsNullOrWhiteSpace(footNote) Then
            Dim lblNote As New Label With {
                .Text = footNote, .Font = New Font("Segoe UI", 9.5F), .ForeColor = Theme.SubText,
                .AutoSize = False, .Size = New Size(innerW, 38),
                .Location = New Point(PAD, nextY), .BackColor = Color.Transparent
            }
            f.Controls.Add(lblNote)
            nextY = lblNote.Bottom
        End If

        ' ── Buttons ─────────────────────────────────────────────────────────
        Dim dividerY As Integer = nextY + 10
        Dim btnY As Integer = dividerY + 14
        Const BTN_H As Integer = 38
        Dim hasAction As Boolean = Not String.IsNullOrWhiteSpace(actionText)
        Dim btnClose As Button = MakeButton("Close", False,
            New Point(CARD_W - PAD - 110 - If(hasAction, 170 + 8, 0), btnY), New Size(110, BTN_H))
        AddHandler btnClose.Click, Sub() f.Close()
        f.Controls.Add(btnClose)
        If hasAction Then
            Dim btnAct As Button = MakeButton(actionText, True,
                New Point(CARD_W - PAD - 170, btnY), New Size(170, BTN_H))
            AddHandler btnAct.Click,
                Sub()
                    acted = True
                    f.Close()
                End Sub
            f.Controls.Add(btnAct)
        End If

        Dim cardH As Integer = btnY + BTN_H + PAD
        f.Height = cardH
        Dim dividerYLocal As Integer = dividerY
        AddHandler f.Paint,
            Sub(s As Object, e As PaintEventArgs)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Using pen As New Pen(DividerClr, 1.0F)
                    e.Graphics.DrawLine(pen, PAD, dividerYLocal, f.Width - PAD, dividerYLocal)
                End Using
                Using path As GraphicsPath = RoundedPath(New Rectangle(0, 0, f.Width - 1, f.Height - 1), RADIUS)
                    Using pen As New Pen(BorderClr, 1.2F)
                        e.Graphics.DrawPath(pen, path)
                    End Using
                End Using
            End Sub
        Using gp As GraphicsPath = RoundedPath(New Rectangle(0, 0, CARD_W, cardH), RADIUS)
            f.Region = New Region(gp)
        End Using

        f.CancelButton = btnClose
        AddHandler f.KeyDown,
            Sub(s As Object, e As KeyEventArgs)
                If e.KeyCode = Keys.Escape Then f.Close()
            End Sub

        Try
            Dim owner As Form = Form.ActiveForm
            If owner Is Nothing OrElse owner Is f Then
                f.StartPosition = FormStartPosition.CenterScreen
                f.ShowDialog()
            Else
                f.ShowDialog(owner)
            End If
        Finally
            f.Dispose()
        End Try
        Return acted
    End Function

    ' =========================================================================
    '  DATA
    ' =========================================================================
    Private Function LoadHeader(poId As Integer) As Header
        Using conn = Db.GetConnection()
            conn.Open()
            Dim hasPaidAt As Boolean = ColumnExists(conn, "purchase_orders", "paid_at")
            Dim hasPayments As Boolean = TableExists(conn, "supplier_payments")

            Dim hdr As Header = Nothing
            Using cmd As New MySqlCommand(
                "SELECT po.po_no, s.name AS sname, po.order_date, po.expected_date, po.status, " &
                "po.total_cost, " & If(hasPaidAt, "po.paid_at", "NULL AS paid_at") & ", " &
                "COALESCE(u.full_name, '') AS uname " &
                "FROM purchase_orders po JOIN suppliers s ON s.supplier_id = po.supplier_id " &
                "LEFT JOIN users u ON u.user_id = po.created_by WHERE po.po_id = @id", conn)
                cmd.Parameters.AddWithValue("@id", poId)
                Using rdr = cmd.ExecuteReader()
                    If rdr.Read() Then
                        hdr = New Header With {
                            .PoNo = rdr("po_no").ToString(),
                            .Supplier = rdr("sname").ToString(),
                            .OrderDate = Convert.ToDateTime(rdr("order_date")),
                            .ExpectedDate = If(IsDBNull(rdr("expected_date")), CType(Nothing, DateTime?),
                                               Convert.ToDateTime(rdr("expected_date"))),
                            .Status = rdr("status").ToString(),
                            .TotalCost = Convert.ToDecimal(rdr("total_cost")),
                            .PaidAt = If(IsDBNull(rdr("paid_at")), CType(Nothing, DateTime?),
                                         Convert.ToDateTime(rdr("paid_at"))),
                            .CreatedBy = rdr("uname").ToString()
                        }
                    End If
                End Using
            End Using
            If hdr Is Nothing Then Return Nothing

            If hasPayments Then
                Using cmd As New MySqlCommand(
                    "SELECT SUM(amount_paid) AS amount_paid, " &
                    "(SELECT method FROM supplier_payments WHERE po_id = @id ORDER BY payment_id DESC LIMIT 1) AS method " &
                    "FROM supplier_payments WHERE po_id = @id", conn)
                    cmd.Parameters.AddWithValue("@id", poId)
                    Using rdr = cmd.ExecuteReader()
                        If rdr.Read() AndAlso Not IsDBNull(rdr("amount_paid")) Then
                            hdr.AmountPaid = Convert.ToDecimal(rdr("amount_paid"))
                            hdr.PayMethod = rdr("method").ToString()
                        End If
                    End Using
                End Using
            End If
            Return hdr
        End Using
    End Function

    Private Function LoadLines(poId As Integer) As List(Of Line)
        Dim result As New List(Of Line)
        Using conn = Db.GetConnection()
            conn.Open()
            Using cmd As New MySqlCommand(
                "SELECT pi.product_id, p.name AS pname, p.sku, pi.qty, pi.unit_cost, pi.line_cost, " &
                "COALESCE((SELECT SUM(sm.qty_change) FROM stock_movements sm " &
                " WHERE sm.reference_type = 'receive' AND sm.reference_id = pi.po_id " &
                "   AND sm.product_id = pi.product_id), 0) AS received " &
                "FROM po_items pi JOIN products p ON p.product_id = pi.product_id " &
                "WHERE pi.po_id = @id ORDER BY p.name", conn)
                cmd.Parameters.AddWithValue("@id", poId)
                Using rdr = cmd.ExecuteReader()
                    While rdr.Read()
                        result.Add(New Line With {
                            .ProductId = Convert.ToInt32(rdr("product_id")),
                            .Name = rdr("pname").ToString(),
                            .Sku = rdr("sku").ToString(),
                            .Qty = Convert.ToInt32(rdr("qty")),
                            .Received = Convert.ToInt32(rdr("received")),
                            .UnitCost = Convert.ToDecimal(rdr("unit_cost")),
                            .LineCost = Convert.ToDecimal(rdr("line_cost"))
                        })
                    End While
                End Using
            End Using
        End Using
        Return result
    End Function

    Private Function ColumnExists(conn As MySqlConnection, table As String, column As String) As Boolean
        Try
            Using cmd As New MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() " &
                "AND table_name = @t AND column_name = @c", conn)
                cmd.Parameters.AddWithValue("@t", table)
                cmd.Parameters.AddWithValue("@c", column)
                Return Convert.ToInt32(cmd.ExecuteScalar()) > 0
            End Using
        Catch
            Return False
        End Try
    End Function

    Private Function TableExists(conn As MySqlConnection, table As String) As Boolean
        Try
            Using cmd As New MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() " &
                "AND table_name = @t", conn)
                cmd.Parameters.AddWithValue("@t", table)
                Return Convert.ToInt32(cmd.ExecuteScalar()) > 0
            End Using
        Catch
            Return False
        End Try
    End Function

    ' =========================================================================
    '  CHROME
    ' =========================================================================
    Private Function StatusColors(status As String) As (fore As Color, back As Color)
        Select Case status
            Case "Received" : Return (Theme.Forest, Color.FromArgb(220, 248, 240))
            Case "Partially Received" : Return (Color.FromArgb(165, 110, 20), Color.FromArgb(255, 244, 224))
            Case "Ordered" : Return (Theme.Accent, Color.FromArgb(232, 238, 250))
            Case "Cancelled" : Return (Color.FromArgb(180, 40, 40), Color.FromArgb(255, 233, 233))
            Case Else : Return (Theme.SubText, Color.FromArgb(238, 242, 240))
        End Select
    End Function

    Private Function MakeBadge(text As String, clr As (fore As Color, back As Color)) As Label
        Dim b As New Label With {
            .Text = text, .Font = New Font("Segoe UI", 9.0F, FontStyle.Bold),
            .ForeColor = clr.fore, .BackColor = clr.back,
            .AutoSize = True, .Padding = New Padding(12, 5, 12, 5)
        }
        AddHandler b.Resize,
            Sub()
                If b.Width < 12 OrElse b.Height < 12 Then Return
                Using gp As GraphicsPath = RoundedPath(New Rectangle(0, 0, b.Width, b.Height), 10)
                    b.Region = New Region(gp)
                End Using
            End Sub
        Return b
    End Function

    Private Function MakeButton(caption As String, isPrimary As Boolean,
                                loc As Point, size As Size) As Button
        Dim b As New Button With {
            .Text = caption, .Size = size, .Location = loc,
            .FlatStyle = FlatStyle.Flat, .Font = New Font("Segoe UI Semibold", 9.5F),
            .Cursor = Cursors.Hand,
            .BackColor = If(isPrimary, Theme.Primary, Color.FromArgb(243, 246, 244)),
            .ForeColor = If(isPrimary, Color.White, Color.FromArgb(70, 86, 80))
        }
        b.FlatAppearance.BorderSize = 0
        Dim normalClr As Color = b.BackColor
        Dim hoverClr As Color = If(isPrimary, Theme.Deep, Color.FromArgb(231, 234, 232))
        AddHandler b.Paint,
            Sub(s As Object, e As PaintEventArgs)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Using bg As New SolidBrush(CardBg)
                    g.FillRectangle(bg, b.ClientRectangle)
                End Using
                Dim rect = b.ClientRectangle
                rect.Width -= 1 : rect.Height -= 1
                Using path As GraphicsPath = RoundedPath(rect, 7)
                    Using br As New SolidBrush(b.BackColor)
                        g.FillPath(br, path)
                    End Using
                    If Not isPrimary Then
                        Using pen As New Pen(Color.FromArgb(200, 215, 210), 1.0F)
                            g.DrawPath(pen, path)
                        End Using
                    End If
                End Using
                TextRenderer.DrawText(g, b.Text, b.Font, b.ClientRectangle, b.ForeColor,
                    TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub
        AddHandler b.MouseEnter, Sub()
                                     b.BackColor = hoverClr
                                     b.Invalidate()
                                 End Sub
        AddHandler b.MouseLeave, Sub()
                                     b.BackColor = normalClr
                                     b.Invalidate()
                                 End Sub
        Return b
    End Function

    Private Function RoundedPath(rect As Rectangle, radius As Integer) As GraphicsPath
        Dim path As New GraphicsPath()
        Dim d As Integer = radius * 2
        path.AddArc(rect.X, rect.Y, d, d, 180, 90)
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90)
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90)
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90)
        path.CloseFigure()
        Return path
    End Function

End Module
