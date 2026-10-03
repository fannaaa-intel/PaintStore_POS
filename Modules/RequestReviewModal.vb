Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports MySql.Data.MySqlClient

' =============================================================================
'  RequestReviewModal — the admin approves or rejects a purchase request.
'
'  THE FLOW (owner's decision, Sept 2026)
'    Inventory sends a request         -> 'Pending'
'    Admin approves                    -> 'Approved'  (Procurement can now
'                                                      quote and order it)
'    Admin rejects, with a reason      -> 'Rejected'  (the reason is shown to
'                                                      the requester)
'    While 'Pending', the requester may edit or remove it (MyRequestsModal).
'
'  Opened from Admin > Procurement Overview > Purchase Requests. Every write is
'  guarded with "AND status = 'Pending'", so a request the requester edited or
'  removed a moment ago cannot be approved in its old form.
'
'  Also used read-only (no buttons but Close) for requests already decided.
' =============================================================================
Public Module RequestReviewModal

    Private ReadOnly CardBg As Color = Color.White
    Private ReadOnly TitleClr As Color = Color.FromArgb(25, 40, 35)
    Private ReadOnly BorderClr As Color = Color.FromArgb(160, 195, 185)
    Private ReadOnly DividerClr As Color = Color.FromArgb(230, 238, 235)
    Private ReadOnly Peso As String = ChrW(8369)

    Private Const RADIUS As Integer = 10
    Private Const CARD_W As Integer = 760
    Private Const PAD As Integer = 26

    Public Class RequestInfo
        Public RequestId As Integer
        Public RequestedBy As String = ""
        Public RequestedById As Integer
        Public CreatedAt As DateTime
        Public Status As String = ""
        Public Note As String = ""
        Public Purpose As String = ""
        Public ReviewNote As String = ""
        Public ReviewedBy As String = ""
        Public ReviewedAt As DateTime?
        Public Lines As New List(Of RequestLine)

        Public ReadOnly Property TotalUnits As Integer
            Get
                Return Lines.Sum(Function(l) l.Qty)
            End Get
        End Property

        Public ReadOnly Property EstValue As Decimal
            Get
                Return Lines.Sum(Function(l) l.UnitPrice * l.Qty)
            End Get
        End Property
    End Class

    Public Class RequestLine
        Public ProductId As Integer
        Public Name As String = ""
        Public Sku As String = ""
        Public Qty As Integer
        Public StockQty As Integer
        Public ReorderLevel As Integer
        Public UnitPrice As Decimal
    End Class

    ''' <summary>"REQ-0007" — the one way a request number is written.</summary>
    Public Function RequestNo(requestId As Integer) As String
        Return "REQ-" & requestId.ToString("0000")
    End Function

    ''' <summary>True when migration 22 added purchase_requests.review_note.</summary>
    Public Function HasReviewNote(conn As MySqlConnection, Optional tx As MySqlTransaction = Nothing) As Boolean
        Try
            Using cmd As New MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() " &
                "AND table_name = 'purchase_requests' AND column_name = 'review_note'", conn, tx)
                Return Convert.ToInt32(cmd.ExecuteScalar()) > 0
            End Using
        Catch swallowed As Exception
            ErrorLog.Write("RequestReviewModal.HasReviewNote", swallowed)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' SQL for a request line's estimated unit cost (aliases pri, p): what the
    ''' requester entered, else the product's cost price, else its selling price.
    ''' The same rule the request form uses to suggest a price.
    ''' </summary>
    Public Function EstPriceSql(hasLinePrice As Boolean) As String
        Dim fallback As String = "IF(COALESCE(p.cost_price,0) > 0, p.cost_price, COALESCE(p.unit_price,0))"
        Return If(hasLinePrice, "COALESCE(pri.unit_price, " & fallback & ")", fallback)
    End Function

    ''' <summary>The reason shown for a request: its purpose, or the typed part of the older note.</summary>
    Public Function PurposeText(info As RequestInfo) As String
        Return If(info.Purpose <> "", info.Purpose, TypedNote(info.Note))
    End Function

    ''' <summary>Loads a request and its product lines. Nothing when not found.</summary>
    Public Function LoadRequest(requestId As Integer) As RequestInfo
        Using conn = Db.GetConnection()
            conn.Open()
            Dim withNote As Boolean = HasReviewNote(conn)
            Dim withPurpose As Boolean = SupplierPayments.ColumnExists(conn, "purchase_requests", "purpose")
            Dim withPrice As Boolean = SupplierPayments.ColumnExists(conn, "purchase_request_items", "unit_price")
            Dim info As RequestInfo = Nothing
            Using cmd As New MySqlCommand(
                "SELECT pr.request_id, pr.requested_by, COALESCE(u.full_name,'Unknown') AS reqby, " &
                "pr.created_at, pr.status, COALESCE(pr.note,'') AS note, pr.reviewed_at, " &
                If(withPurpose, "COALESCE(pr.purpose,'')", "''") & " AS purpose, " &
                "COALESCE(v.full_name,'') AS revby, " &
                If(withNote, "COALESCE(pr.review_note,'')", "''") & " AS review_note " &
                "FROM purchase_requests pr " &
                "LEFT JOIN users u ON u.user_id = pr.requested_by " &
                "LEFT JOIN users v ON v.user_id = pr.reviewed_by " &
                "WHERE pr.request_id = @id", conn)
                cmd.Parameters.AddWithValue("@id", requestId)
                Using rdr = cmd.ExecuteReader()
                    If rdr.Read() Then
                        info = New RequestInfo With {
                            .RequestId = Convert.ToInt32(rdr("request_id")),
                            .RequestedById = Convert.ToInt32(rdr("requested_by")),
                            .RequestedBy = rdr("reqby").ToString(),
                            .CreatedAt = Convert.ToDateTime(rdr("created_at")),
                            .Status = rdr("status").ToString(),
                            .Note = rdr("note").ToString(),
                            .Purpose = rdr("purpose").ToString(),
                            .ReviewedBy = rdr("revby").ToString(),
                            .ReviewedAt = If(IsDBNull(rdr("reviewed_at")), CType(Nothing, DateTime?),
                                             Convert.ToDateTime(rdr("reviewed_at"))),
                            .ReviewNote = rdr("review_note").ToString()
                        }
                    End If
                End Using
            End Using
            If info Is Nothing Then Return Nothing

            Using cmd As New MySqlCommand(
                "SELECT pri.product_id, p.name, p.sku, pri.qty_requested, p.stock_qty, " &
                "COALESCE(p.reorder_level,0) AS reorder_level, " & EstPriceSql(withPrice) & " AS unit_price " &
                "FROM purchase_request_items pri JOIN products p ON p.product_id = pri.product_id " &
                "WHERE pri.request_id = @id ORDER BY pri.pr_item_id", conn)
                cmd.Parameters.AddWithValue("@id", requestId)
                Using rdr = cmd.ExecuteReader()
                    While rdr.Read()
                        info.Lines.Add(New RequestLine With {
                            .ProductId = Convert.ToInt32(rdr("product_id")),
                            .Name = rdr("name").ToString(),
                            .Sku = rdr("sku").ToString(),
                            .Qty = Convert.ToInt32(rdr("qty_requested")),
                            .StockQty = Convert.ToInt32(rdr("stock_qty")),
                            .ReorderLevel = Convert.ToInt32(rdr("reorder_level")),
                            .UnitPrice = Convert.ToDecimal(rdr("unit_price"))
                        })
                    End While
                End Using
            End Using
            Return info
        End Using
    End Function

    ' The note stored on a multi-product request starts with a generated
    ' "Request for N products (...)" prefix; the part the requester typed
    ' follows " — ". Shown without the prefix, since the table already says it.
    Public Function TypedNote(note As String) As String
        If String.IsNullOrEmpty(note) Then Return ""
        If note.StartsWith("Request for ", StringComparison.Ordinal) Then
            Dim i As Integer = note.IndexOf(" " & ChrW(8212) & " ", StringComparison.Ordinal)
            Return If(i >= 0, note.Substring(i + 3).Trim(), "")
        End If
        Return note
    End Function

    ' =========================================================================
    '  ENTRY POINT — returns "Approved", "Rejected" or Nothing (no change).
    ' =========================================================================
    Public Function Show(requestId As Integer) As String
        Dim info As RequestInfo
        Try
            info = LoadRequest(requestId)
        Catch ex As Exception
            AppModal.Show("Could not load the request:" & Environment.NewLine & ErrorLog.ForUser(ex),
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return Nothing
        End Try
        If info Is Nothing Then
            AppModal.Show("This request no longer exists. The requester may have removed it.",
                          "Not Found", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return Nothing
        End If

        ' Only the admin decides; anyone else opening a request just reads it.
        Dim canDecide As Boolean = (info.Status = "Pending") AndAlso
            String.Equals(Session.CurrentRole, "Admin", StringComparison.OrdinalIgnoreCase)
        Dim innerW As Integer = CARD_W - PAD * 2
        Dim outcome As String = Nothing

        Dim f As New Form With {
            .FormBorderStyle = FormBorderStyle.None,
            .StartPosition = FormStartPosition.CenterParent,
            .BackColor = CardBg, .ShowInTaskbar = False,
            .Width = CARD_W, .KeyPreview = True
        }

        Dim lblTitle As New Label With {
            .Text = If(canDecide, "Review Purchase Request", "Purchase Request") & "  " & RequestNo(info.RequestId),
            .Font = New Font("Segoe UI Semibold", 14.0F), .ForeColor = Theme.Dark,
            .AutoSize = True, .Location = New Point(PAD, PAD - 4), .BackColor = Color.Transparent
        }
        Dim lblSub As New Label With {
            .Text = "Requested by " & info.RequestedBy & "   " & ChrW(8226) & "   " &
                    info.CreatedAt.ToString("MMM d, yyyy  h:mm tt"),
            .Font = New Font("Segoe UI", 10.0F), .ForeColor = Theme.SubText,
            .AutoSize = True, .Location = New Point(PAD + 2, lblTitle.Bottom + 2),
            .BackColor = Color.Transparent
        }
        f.Controls.AddRange({lblTitle, lblSub})

        Dim badge As Label = MakeBadge(info.Status, BadgeColors(info.Status))
        badge.Location = New Point(CARD_W - PAD - badge.PreferredSize.Width, PAD)
        f.Controls.Add(badge)

        Dim y As Integer = lblSub.Bottom + 16

        ' Why it is needed: the purpose (or, on older requests, the note).
        Dim typed As String = PurposeText(info)
        If typed <> "" Then
            Dim capPurpose As New Label With {
                .Text = "PURPOSE", .Font = New Font("Segoe UI Semibold", 8.5F), .ForeColor = Theme.FaintText,
                .AutoSize = True, .Location = New Point(PAD, y), .BackColor = Color.Transparent
            }
            f.Controls.Add(capPurpose)
            Dim noteBox As New Label With {
                .Text = typed, .Font = New Font("Segoe UI", 10.0F),
                .ForeColor = Theme.TextDark, .BackColor = Theme.TileBg, .AutoSize = False,
                .Size = New Size(innerW, 46), .Padding = New Padding(12, 4, 12, 4),
                .TextAlign = ContentAlignment.MiddleLeft, .AutoEllipsis = True,
                .Location = New Point(PAD, capPurpose.Bottom + 4), .UseMnemonic = False
            }
            f.Controls.Add(noteBox)
            y = noteBox.Bottom + 12
        End If

        ' ── Items table ─────────────────────────────────────────────────────
        Dim xStock As Integer = CInt(innerW * 0.5)
        Dim xQty As Integer = CInt(innerW * 0.68)
        Dim xVal As Integer = innerW - 12
        Const NUM_W As Integer = 110

        Dim head As New Panel With {.Location = New Point(PAD, y), .Size = New Size(innerW, 30), .BackColor = Color.Transparent}
        AddHandler head.Paint,
            Sub(s As Object, e As PaintEventArgs)
                Using pen As New Pen(Theme.Border, 1)
                    e.Graphics.DrawLine(pen, 0, head.Height - 1, head.Width, head.Height - 1)
                End Using
            End Sub
        Dim addHead As Action(Of String, Integer, Boolean) =
            Sub(text As String, x As Integer, rightAlign As Boolean)
                head.Controls.Add(New Label With {
                    .Text = text, .Font = New Font("Segoe UI Semibold", 8.5F), .ForeColor = Theme.FaintText,
                    .AutoSize = False, .Size = New Size(If(rightAlign, NUM_W + 40, 300), 18),
                    .TextAlign = If(rightAlign, ContentAlignment.MiddleRight, ContentAlignment.MiddleLeft),
                    .Location = New Point(If(rightAlign, x - NUM_W - 40, x), 6), .BackColor = Color.Transparent
                })
            End Sub
        addHead("PRODUCT  (" & info.Lines.Count & ")", 12, False)
        addHead("STOCK / RESTOCK", xStock + NUM_W \ 2, True)
        addHead("QTY REQUESTED", xQty + NUM_W \ 2, True)
        addHead("EST. TOTAL", xVal, True)
        f.Controls.Add(head)

        Dim body As New Panel With {.Location = New Point(PAD, head.Bottom), .Width = innerW, .BackColor = CardBg, .AutoScroll = True}
        Dim ry As Integer = 0
        For Each ln In info.Lines
            Dim row As New Panel With {.Location = New Point(0, ry), .Size = New Size(innerW - 18, 48), .BackColor = CardBg}
            AddHandler row.Paint,
                Sub(s As Object, e As PaintEventArgs)
                    Dim p = CType(s, Panel)
                    Using pen As New Pen(Theme.RowDivider, 1)
                        e.Graphics.DrawLine(pen, 0, p.Height - 1, p.Width, p.Height - 1)
                    End Using
                End Sub
            row.Controls.Add(New Label With {
                .Text = ln.Name, .Font = New Font("Segoe UI Semibold", 10.5F), .ForeColor = Theme.Dark,
                .AutoSize = False, .AutoEllipsis = True, .Size = New Size(xStock - NUM_W \ 2 - 20, 20),
                .Location = New Point(12, 6), .BackColor = Color.Transparent, .UseMnemonic = False
            })
            row.Controls.Add(New Label With {
                .Text = ln.Sku, .Font = New Font("Segoe UI", 8.5F), .ForeColor = Theme.FaintText,
                .AutoSize = True, .Location = New Point(12, 26), .BackColor = Color.Transparent
            })
            Dim low As Boolean = ln.ReorderLevel > 0 AndAlso ln.StockQty <= ln.ReorderLevel
            Dim addNum As Action(Of String, Integer, Color, Boolean) =
                Sub(text As String, rightEdge As Integer, clr As Color, bold As Boolean)
                    row.Controls.Add(New Label With {
                        .Text = text, .Font = New Font(If(bold, "Segoe UI Semibold", "Segoe UI"), 10.0F),
                        .ForeColor = clr, .AutoSize = False, .Size = New Size(NUM_W, 20),
                        .TextAlign = ContentAlignment.MiddleRight,
                        .Location = New Point(rightEdge - NUM_W, 14), .BackColor = Color.Transparent
                    })
                End Sub
            addNum(ln.StockQty.ToString("N0") & " / " & ln.ReorderLevel.ToString("N0"), xStock + NUM_W \ 2,
                   If(low, Theme.Warning, Theme.SubText), low)
            addNum(ln.Qty.ToString("N0"), xQty + NUM_W \ 2, Theme.Dark, True)
            addNum(Peso & (ln.UnitPrice * ln.Qty).ToString("N2"), xVal - 18, Theme.SubText, False)
            body.Controls.Add(row)
            ry += 48
        Next
        body.Height = Math.Min(260, Math.Max(48, ry + 2))
        f.Controls.Add(body)

        Dim lblTotals As New Label With {
            .Text = info.Lines.Count & If(info.Lines.Count = 1, " product", " products") & "   " & ChrW(8226) & "   " &
                    info.TotalUnits.ToString("N0") & If(info.TotalUnits = 1, " unit   ", " units   ") & ChrW(8226) & "   est. total " & Peso & info.EstValue.ToString("N2"),
            .Font = New Font("Segoe UI Semibold", 10.0F), .ForeColor = Theme.Dark,
            .AutoSize = False, .Size = New Size(innerW, 22), .TextAlign = ContentAlignment.MiddleRight,
            .Location = New Point(PAD, body.Bottom + 8), .BackColor = Color.Transparent
        }
        f.Controls.Add(lblTotals)
        y = lblTotals.Bottom + 12

        ' ── Decision area ───────────────────────────────────────────────────
        Dim txtReason As TextBox = Nothing
        Dim lblErr As Label = Nothing
        If canDecide Then
            Dim capReason As New Label With {
                .Text = "REASON  (required to reject, optional to approve)",
                .Font = New Font("Segoe UI Semibold", 8.5F), .ForeColor = Theme.FaintText,
                .AutoSize = True, .Location = New Point(PAD, y), .BackColor = Color.Transparent
            }
            f.Controls.Add(capReason)
            txtReason = New TextBox With {
                .Font = New Font("Segoe UI", 10.0F), .BorderStyle = BorderStyle.None,
                .BackColor = Theme.PageBg, .ForeColor = TitleClr,
                .Location = New Point(12, 10), .Width = innerW - 24, .MaxLength = 255
            }
            Dim fld As Panel = MakeField(txtReason, PAD, capReason.Bottom + 4, innerW, 40)
            f.Controls.Add(fld)
            lblErr = New Label With {
                .Text = "", .Font = New Font("Segoe UI", 9.0F), .ForeColor = Theme.Danger,
                .AutoSize = False, .Size = New Size(innerW, 20),
                .Location = New Point(PAD, fld.Bottom + 4), .BackColor = Color.Transparent
            }
            f.Controls.Add(lblErr)
            y = lblErr.Bottom + 4
        ElseIf info.Status = "Rejected" OrElse info.ReviewNote <> "" Then
            Dim decided As String = info.Status &
                If(info.ReviewedBy = "", "", " by " & info.ReviewedBy) &
                If(info.ReviewedAt.HasValue, " on " & info.ReviewedAt.Value.ToString("MMM d, yyyy"), "") &
                If(info.ReviewNote = "", "", ":  " & info.ReviewNote)
            Dim lblDecided As New Label With {
                .Text = decided, .Font = New Font("Segoe UI", 10.0F),
                .ForeColor = If(info.Status = "Rejected", Theme.Danger, Theme.SubText),
                .AutoSize = False, .Size = New Size(innerW, 40),
                .Location = New Point(PAD, y), .BackColor = Color.Transparent, .UseMnemonic = False
            }
            f.Controls.Add(lblDecided)
            y = lblDecided.Bottom + 4
        End If

        ' ── Buttons ─────────────────────────────────────────────────────────
        Dim dividerY As Integer = y + 8
        Dim btnY As Integer = dividerY + 14
        Const BTN_H As Integer = 38
        Dim btnClose As Button = MakeButton(If(canDecide, "Cancel", "Close"), "plain",
            New Point(If(canDecide, PAD, CARD_W - PAD - 110), btnY), New Size(110, BTN_H))
        AddHandler btnClose.Click, Sub() f.Close()
        f.Controls.Add(btnClose)

        If canDecide Then
            Dim btnApprove As Button = MakeButton("Approve", "primary",
                New Point(CARD_W - PAD - 150, btnY), New Size(150, BTN_H))
            Dim btnReject As Button = MakeButton("Reject", "danger",
                New Point(btnApprove.Left - 10 - 130, btnY), New Size(130, BTN_H))
            AddHandler btnApprove.Click,
                Sub()
                    If AppModal.Show("Approve " & RequestNo(info.RequestId) & "?" & Environment.NewLine &
                                     "Procurement will then be able to quote and order it.",
                                     "Approve Request", MessageBoxButtons.YesNo,
                                     MessageBoxIcon.Question) <> DialogResult.Yes Then Return
                    If Decide(info, "Approved", txtReason.Text.Trim(), lblErr) Then
                        outcome = "Approved"
                        f.Close()
                    End If
                End Sub
            AddHandler btnReject.Click,
                Sub()
                    If txtReason.Text.Trim() = "" Then
                        lblErr.Text = "Say why it is rejected, so the requester knows what to change."
                        txtReason.Focus()
                        Return
                    End If
                    If AppModal.Show("Reject " & RequestNo(info.RequestId) & "?", "Reject Request",
                                     MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then Return
                    If Decide(info, "Rejected", txtReason.Text.Trim(), lblErr) Then
                        outcome = "Rejected"
                        f.Close()
                    End If
                End Sub
            f.Controls.AddRange({btnReject, btnApprove})
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

        AddHandler f.KeyDown,
            Sub(s As Object, e As KeyEventArgs)
                If e.KeyCode = Keys.Escape Then f.Close()
            End Sub
        If txtReason IsNot Nothing Then
            Dim capReasonBox As TextBox = txtReason
            AddHandler f.Shown, Sub() capReasonBox.Focus()
        End If

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
        Return outcome
    End Function

    ' Writes the decision. Guarded on 'Pending' so an edit or removal by the
    ' requester in the meantime is never overwritten by a stale review.
    Private Function Decide(info As RequestInfo, newStatus As String, reason As String, lblErr As Label) As Boolean
        If Not Permissions.Require(Permissions.ApproveRequests, "approve or reject purchase requests") Then Return False
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Dim withNote As Boolean = HasReviewNote(conn)
                Dim n As Integer
                Using cmd As New MySqlCommand(
                    "UPDATE purchase_requests SET status = @st, reviewed_by = @uid, reviewed_at = NOW()" &
                    If(withNote, ", review_note = @note", "") & " " &
                    "WHERE request_id = @id AND status = 'Pending'", conn)
                    cmd.Parameters.AddWithValue("@st", newStatus)
                    cmd.Parameters.AddWithValue("@uid", If(Session.CurrentUserId = 0, CObj(DBNull.Value), CObj(Session.CurrentUserId)))
                    cmd.Parameters.AddWithValue("@id", info.RequestId)
                    If withNote Then cmd.Parameters.AddWithValue("@note", If(reason = "", CObj(DBNull.Value), CObj(reason)))
                    n = cmd.ExecuteNonQuery()
                End Using
                If n = 0 Then
                    lblErr.Text = "This request was changed or removed a moment ago. Close and open it again."
                    Return False
                End If
            End Using
            ActivityLog.Record(If(newStatus = "Approved", "REQUEST_APPROVE", "REQUEST_REJECT"),
                               "purchase_request", info.RequestId,
                               RequestNo(info.RequestId) & ", " & info.Lines.Count &
                               If(info.Lines.Count = 1, " product", " products") &
                               If(reason = "", "", " - " & reason))
            Return True
        Catch ex As Exception
            lblErr.Text = "Could not save: " & ErrorLog.ForUser(ex)
            Return False
        End Try
    End Function

    ' =========================================================================
    '  CHROME
    ' =========================================================================
    Public Function BadgeColors(status As String) As (fore As Color, back As Color)
        Select Case status
            Case "Approved", "Converted" : Return (Theme.Forest, Color.FromArgb(220, 248, 240))
            Case "Pending" : Return (Color.FromArgb(165, 110, 20), Color.FromArgb(255, 244, 224))
            Case "Quoted" : Return (Color.FromArgb(30, 100, 160), Color.FromArgb(226, 240, 252))
            Case "Rejected" : Return (Color.FromArgb(180, 40, 40), Color.FromArgb(255, 233, 233))
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

    Private Function MakeField(inner As Control, x As Integer, y As Integer, w As Integer, h As Integer) As Panel
        Dim host As New Panel With {.Location = New Point(x, y), .Size = New Size(w, h), .BackColor = Theme.PageBg}
        AddHandler host.Paint,
            Sub(sender As Object, ev As PaintEventArgs)
                ev.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Using path As GraphicsPath = RoundedPath(New Rectangle(0, 0, host.Width - 1, host.Height - 1), 8)
                    Using b As New SolidBrush(Theme.PageBg)
                        ev.Graphics.FillPath(b, path)
                    End Using
                    Using pen As New Pen(If(inner.Focused, Theme.Primary, Theme.BorderMuted), 1.2F)
                        ev.Graphics.DrawPath(pen, path)
                    End Using
                End Using
            End Sub
        AddHandler inner.GotFocus, Sub() host.Invalidate()
        AddHandler inner.LostFocus, Sub() host.Invalidate()
        host.Controls.Add(inner)
        Return host
    End Function

    ' kind: "primary" (mint), "danger" (red outline), "plain" (light).
    Private Function MakeButton(caption As String, kind As String, loc As Point, size As Size) As Button
        Dim isPrimary As Boolean = (kind = "primary")
        Dim isDanger As Boolean = (kind = "danger")
        Dim b As New Button With {
            .Text = caption, .Size = size, .Location = loc,
            .FlatStyle = FlatStyle.Flat, .Font = New Font("Segoe UI Semibold", 9.5F),
            .Cursor = Cursors.Hand,
            .BackColor = If(isPrimary, Theme.Primary, If(isDanger, Color.FromArgb(253, 238, 238), Color.FromArgb(243, 246, 244))),
            .ForeColor = If(isPrimary, Color.White, If(isDanger, Theme.Danger, Color.FromArgb(70, 86, 80)))
        }
        b.FlatAppearance.BorderSize = 0
        Dim normalClr As Color = b.BackColor
        Dim hoverClr As Color = If(isPrimary, Theme.Deep, If(isDanger, Color.FromArgb(248, 220, 220), Color.FromArgb(231, 234, 232)))
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
                        Using pen As New Pen(If(isDanger, Color.FromArgb(235, 190, 190), Color.FromArgb(200, 215, 210)), 1.0F)
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
