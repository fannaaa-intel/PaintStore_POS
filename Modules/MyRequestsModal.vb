Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports MySql.Data.MySqlClient

' =============================================================================
'  MyRequestsModal — the requester's own purchase requests.
'
'  Opened from Inventory > Low Stock Alerts > My Requests. Lists every request
'  the signed-in user sent, newest first, with its status:
'
'    Pending    waiting for the admin. Edit (PurchaseRequestModal.Edit) and
'               Remove are offered, and ONLY here: once the admin approves or
'               rejects a request it is locked.
'    Approved   cleared for Procurement to quote and order.
'    Rejected   the admin's reason is shown under the row.
'    Quoted / Converted   Procurement is working on it / it is on a PO.
'
'  Remove goes through the Recycle Bin (Archive.Capture) like every delete.
'  Returns True when anything was changed, so the caller can reload.
' =============================================================================
Public Module MyRequestsModal

    Private ReadOnly CardBg As Color = Color.White
    Private ReadOnly BorderClr As Color = Color.FromArgb(160, 195, 185)
    Private ReadOnly DividerClr As Color = Color.FromArgb(230, 238, 235)

    Private Const RADIUS As Integer = 10
    Private Const CARD_W As Integer = 860
    Private Const CARD_H As Integer = 620
    Private Const PAD As Integer = 26
    Private Const ROW_H As Integer = 74
    Private Const MAX_ROWS As Integer = 100

    Private Class MyRow
        Public RequestId As Integer
        Public CreatedAt As DateTime
        Public Status As String = ""
        Public ReviewNote As String = ""
        Public ItemCount As Integer
        Public Units As Integer
        Public Summary As String = ""
    End Class

    Public Function Show() As Boolean
        Dim changed As Boolean = False
        Dim innerW As Integer = CARD_W - PAD * 2

        Dim f As New Form With {
            .FormBorderStyle = FormBorderStyle.None,
            .StartPosition = FormStartPosition.CenterParent,
            .BackColor = CardBg, .ShowInTaskbar = False,
            .Width = CARD_W, .Height = CARD_H, .KeyPreview = True
        }

        Dim lblTitle As New Label With {
            .Text = "My Purchase Requests", .Font = New Font("Segoe UI Semibold", 14.0F),
            .ForeColor = Theme.Dark, .AutoSize = True, .Location = New Point(PAD, PAD - 4),
            .BackColor = Color.Transparent
        }
        Dim lblSub As New Label With {
            .Text = "Requests you sent. While a request is waiting for approval you can edit it or remove it.",
            .Font = New Font("Segoe UI", 10.0F), .ForeColor = Theme.SubText,
            .AutoSize = True, .Location = New Point(PAD + 2, lblTitle.Bottom + 2),
            .BackColor = Color.Transparent
        }
        f.Controls.AddRange({lblTitle, lblSub})

        Dim listTop As Integer = lblSub.Bottom + 14
        Dim btnH As Integer = 38
        Dim btnY As Integer = CARD_H - PAD - btnH
        Dim dividerY As Integer = btnY - 14

        Dim host As New Panel With {
            .Location = New Point(PAD, listTop),
            .Size = New Size(innerW, dividerY - 10 - listTop),
            .BackColor = CardBg, .AutoScroll = True
        }
        f.Controls.Add(host)

        Dim render As Action = Nothing
        render =
            Sub()
                host.SuspendLayout()
                For Each c As Control In host.Controls.Cast(Of Control).ToList()
                    c.Dispose()
                Next
                host.Controls.Clear()

                Dim rows As List(Of MyRow)
                Try
                    rows = LoadMine()
                Catch ex As Exception
                    host.Controls.Add(New Label With {
                        .Text = "Could not load your requests: " & ex.Message,
                        .Font = New Font("Segoe UI", 10.0F), .ForeColor = Theme.Danger,
                        .AutoSize = True, .Location = New Point(8, 16)
                    })
                    host.ResumeLayout()
                    Return
                End Try

                If rows.Count = 0 Then
                    host.Controls.Add(New Label With {
                        .Text = "You have not sent any purchase requests yet." & Environment.NewLine &
                                "Use + New Request on Low Stock Alerts to send one.",
                        .Font = New Font("Segoe UI", 10.5F), .ForeColor = Theme.FaintText,
                        .AutoSize = False, .Size = New Size(innerW - 20, 60),
                        .TextAlign = ContentAlignment.MiddleCenter, .Location = New Point(0, 60)
                    })
                    host.ResumeLayout()
                    Return
                End If

                Dim y As Integer = 0
                For Each r In rows
                    Dim rowH As Integer = If(r.Status = "Rejected" AndAlso r.ReviewNote <> "", ROW_H + 18, ROW_H)
                    Dim row As Panel = BuildRow(r, innerW - 20, rowH,
                        Sub()
                            If PurchaseRequestModal.Edit(r.RequestId) Then
                                changed = True
                                render()
                            End If
                        End Sub,
                        Sub()
                            If RemoveRequest(r) Then
                                changed = True
                                render()
                            End If
                        End Sub)
                    row.Location = New Point(0, y)
                    host.Controls.Add(row)
                    y += rowH
                Next
                host.ResumeLayout()
            End Sub

        Dim btnClose As Button = MakeButton("Close", New Point(CARD_W - PAD - 110, btnY), New Size(110, btnH))
        AddHandler btnClose.Click, Sub() f.Close()
        f.Controls.Add(btnClose)

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
        Using gp As GraphicsPath = RoundedPath(New Rectangle(0, 0, CARD_W, CARD_H), RADIUS)
            f.Region = New Region(gp)
        End Using
        AddHandler f.KeyDown,
            Sub(s As Object, e As KeyEventArgs)
                If e.KeyCode = Keys.Escape Then f.Close()
            End Sub

        render()

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
        Return changed
    End Function

    ' ── One request row ──────────────────────────────────────────────────────
    Private Function BuildRow(r As MyRow, w As Integer, h As Integer,
                              onEdit As Action, onRemove As Action) As Panel
        Dim row As New Panel With {.Size = New Size(w, h), .BackColor = CardBg}
        AddHandler row.Paint,
            Sub(s As Object, e As PaintEventArgs)
                Using pen As New Pen(Theme.RowDivider, 1)
                    e.Graphics.DrawLine(pen, 0, row.Height - 1, row.Width, row.Height - 1)
                End Using
            End Sub

        row.Controls.Add(New Label With {
            .Text = RequestReviewModal.RequestNo(r.RequestId), .Font = New Font("Segoe UI Semibold", 11.0F),
            .ForeColor = Theme.Dark, .AutoSize = True, .Location = New Point(8, 14), .BackColor = Color.Transparent
        })
        row.Controls.Add(New Label With {
            .Text = r.CreatedAt.ToString("MMM d, yyyy  h:mm tt"), .Font = New Font("Segoe UI", 9.0F),
            .ForeColor = Theme.FaintText, .AutoSize = True, .Location = New Point(8, 38), .BackColor = Color.Transparent
        })

        Dim midX As Integer = 170
        Dim actionsW As Integer = If(r.Status = "Pending", 150, 0)
        Dim badgeX As Integer = w - actionsW - 120
        row.Controls.Add(New Label With {
            .Text = r.ItemCount & If(r.ItemCount = 1, " product", " products") & "   " & ChrW(8226) & "   " &
                    r.Units.ToString("N0") & If(r.Units = 1, " unit", " units"),
            .Font = New Font("Segoe UI Semibold", 10.0F), .ForeColor = Theme.TextDark,
            .AutoSize = True, .Location = New Point(midX, 14), .BackColor = Color.Transparent
        })
        row.Controls.Add(New Label With {
            .Text = r.Summary, .Font = New Font("Segoe UI", 9.0F), .ForeColor = Theme.SubText,
            .AutoSize = False, .AutoEllipsis = True, .Size = New Size(badgeX - midX - 16, 18),
            .Location = New Point(midX, 38), .BackColor = Color.Transparent, .UseMnemonic = False
        })
        If r.Status = "Rejected" AndAlso r.ReviewNote <> "" Then
            row.Controls.Add(New Label With {
                .Text = "Reason: " & r.ReviewNote, .Font = New Font("Segoe UI", 9.0F, FontStyle.Italic),
                .ForeColor = Theme.Danger, .AutoSize = False, .AutoEllipsis = True,
                .Size = New Size(w - midX - 16, 18), .Location = New Point(midX, 58),
                .BackColor = Color.Transparent, .UseMnemonic = False
            })
        End If

        Dim clr = RequestReviewModal.BadgeColors(r.Status)
        Dim badge As New Label With {
            .Text = If(r.Status = "Pending", "Waiting for approval", r.Status),
            .Font = New Font("Segoe UI", 8.5F, FontStyle.Bold), .ForeColor = clr.fore, .BackColor = clr.back,
            .AutoSize = True, .Padding = New Padding(10, 4, 10, 4)
        }
        row.Controls.Add(badge)
        badge.Location = New Point(If(r.Status = "Pending", badgeX - 40, badgeX), 22)
        AddHandler badge.Resize,
            Sub()
                If badge.Width < 12 Then Return
                Using gp As GraphicsPath = RoundedPath(New Rectangle(0, 0, badge.Width, badge.Height), 9)
                    badge.Region = New Region(gp)
                End Using
            End Sub

        If r.Status = "Pending" Then
            Dim lnkEdit As Label = MakeLink("Edit", Theme.Primary)
            Dim lnkRemove As Label = MakeLink("Remove", Theme.Danger)
            lnkEdit.Location = New Point(w - 140, 24)
            lnkRemove.Location = New Point(w - 80, 24)
            AddHandler lnkEdit.Click, Sub() onEdit()
            AddHandler lnkRemove.Click, Sub() onRemove()
            row.Controls.AddRange({lnkEdit, lnkRemove})
        End If
        Return row
    End Function

    Private Function MakeLink(text As String, clr As Color) As Label
        Dim lbl As New Label With {
            .Text = text, .Font = New Font("Segoe UI Semibold", 10.0F),
            .ForeColor = clr, .AutoSize = True, .Cursor = Cursors.Hand, .BackColor = Color.Transparent
        }
        AddHandler lbl.MouseEnter, Sub() lbl.Font = New Font("Segoe UI Semibold", 10.0F, FontStyle.Underline)
        AddHandler lbl.MouseLeave, Sub() lbl.Font = New Font("Segoe UI Semibold", 10.0F)
        Return lbl
    End Function

    ' ── Data ─────────────────────────────────────────────────────────────────
    Private Function LoadMine() As List(Of MyRow)
        Dim result As New List(Of MyRow)
        Using conn = Db.GetConnection()
            conn.Open()
            Dim withNote As Boolean = RequestReviewModal.HasReviewNote(conn)
            Using cmd As New MySqlCommand(
                "SELECT pr.request_id, pr.created_at, pr.status, " &
                If(withNote, "COALESCE(pr.review_note,'')", "''") & " AS review_note, " &
                "(SELECT COUNT(*) FROM purchase_request_items pri WHERE pri.request_id = pr.request_id) AS n_items, " &
                "(SELECT COALESCE(SUM(pri.qty_requested),0) FROM purchase_request_items pri " &
                " WHERE pri.request_id = pr.request_id) AS n_units, " &
                "(SELECT GROUP_CONCAT(CONCAT(p.name, ' x', pri.qty_requested) " &
                "        ORDER BY pri.pr_item_id SEPARATOR ',  ') " &
                " FROM purchase_request_items pri JOIN products p ON p.product_id = pri.product_id " &
                " WHERE pri.request_id = pr.request_id) AS summary " &
                "FROM purchase_requests pr WHERE pr.requested_by = @uid " &
                "ORDER BY pr.created_at DESC, pr.request_id DESC LIMIT " & MAX_ROWS, conn)
                cmd.Parameters.AddWithValue("@uid", Session.CurrentUserId)
                Using rdr = cmd.ExecuteReader()
                    While rdr.Read()
                        result.Add(New MyRow With {
                            .RequestId = Convert.ToInt32(rdr("request_id")),
                            .CreatedAt = Convert.ToDateTime(rdr("created_at")),
                            .Status = rdr("status").ToString(),
                            .ReviewNote = rdr("review_note").ToString(),
                            .ItemCount = Convert.ToInt32(rdr("n_items")),
                            .Units = Convert.ToInt32(rdr("n_units")),
                            .Summary = If(IsDBNull(rdr("summary")), "", rdr("summary").ToString())
                        })
                    End While
                End Using
            End Using
        End Using
        Return result
    End Function

    ' Removes a still-Pending request. Re-checked under a row lock, and copied
    ' to the Recycle Bin inside the same transaction as the delete.
    Private Function RemoveRequest(r As MyRow) As Boolean
        Return RemoveRequest(r.RequestId, r.ItemCount)
    End Function

    ' Also used by Procurement > Purchase Requests for the user's own requests.
    Public Function RemoveRequest(requestId As Integer, itemCount As Integer) As Boolean
        If AppModal.Show("Remove " & RequestReviewModal.RequestNo(requestId) & "?" & Environment.NewLine &
                         itemCount & If(itemCount = 1, " product", " products") &
                         " will no longer be requested." & Environment.NewLine &
                         "The admin can still restore it from the Recycle Bin.",
                         "Remove Request", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then
            Return False
        End If
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using tx = conn.BeginTransaction()
                    Dim status As String = ""
                    Dim owner As Integer = 0
                    Using cmd As New MySqlCommand(
                        "SELECT status, requested_by FROM purchase_requests WHERE request_id = @id FOR UPDATE", conn, tx)
                        cmd.Parameters.AddWithValue("@id", requestId)
                        Using rdr = cmd.ExecuteReader()
                            If rdr.Read() Then
                                status = rdr("status").ToString()
                                owner = Convert.ToInt32(rdr("requested_by"))
                            End If
                        End Using
                    End Using
                    If status <> "Pending" OrElse owner <> Session.CurrentUserId Then
                        tx.Rollback()
                        AppModal.Show(If(status = "", "This request was already removed.",
                                         "This request was " & status.ToLower() & " by the admin and can no longer be removed."),
                                      "Not Removed", MessageBoxButtons.OK, MessageBoxIcon.Information)
                        Return True    ' the list is stale either way; reload it
                    End If

                    Archive.Capture(conn, tx, "purchase_requests", "request_id", requestId,
                                    RequestReviewModal.RequestNo(requestId) & " (" & itemCount &
                                    If(itemCount = 1, " product)", " products)"),
                                    children:={("purchase_request_items", "request_id")})
                    Using cmd As New MySqlCommand(
                        "DELETE FROM purchase_requests WHERE request_id = @id AND status = 'Pending'", conn, tx)
                        cmd.Parameters.AddWithValue("@id", requestId)
                        cmd.ExecuteNonQuery()
                    End Using
                    tx.Commit()
                End Using
            End Using
            ActivityLog.Record("REQUEST_REMOVE", "purchase_request", requestId,
                               RequestReviewModal.RequestNo(requestId) & ", " & itemCount &
                               If(itemCount = 1, " product", " products"))
            Return True
        Catch ex As Exception
            AppModal.Show("Could not remove the request:" & Environment.NewLine & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return False
        End Try
    End Function

    ' ── Chrome ───────────────────────────────────────────────────────────────
    Private Function MakeButton(caption As String, loc As Point, size As Size) As Button
        Dim b As New Button With {
            .Text = caption, .Size = size, .Location = loc,
            .FlatStyle = FlatStyle.Flat, .Font = New Font("Segoe UI Semibold", 9.5F),
            .Cursor = Cursors.Hand, .BackColor = Color.FromArgb(243, 246, 244),
            .ForeColor = Color.FromArgb(70, 86, 80)
        }
        b.FlatAppearance.BorderSize = 0
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
                    Using pen As New Pen(Color.FromArgb(200, 215, 210), 1.0F)
                        g.DrawPath(pen, path)
                    End Using
                End Using
                TextRenderer.DrawText(g, b.Text, b.Font, b.ClientRectangle, b.ForeColor,
                    TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub
        AddHandler b.MouseEnter, Sub()
                                     b.BackColor = Color.FromArgb(231, 234, 232)
                                     b.Invalidate()
                                 End Sub
        AddHandler b.MouseLeave, Sub()
                                     b.BackColor = Color.FromArgb(243, 246, 244)
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
