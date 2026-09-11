Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports MySql.Data.MySqlClient

' =============================================================================
'  ReceiveConfirmModal — the attestation step in front of a goods receipt.
'
'  WHY THIS EXISTS
'  ---------------
'  Receiving goods is the moment the store takes ownership: stock movements are
'  written, the PO flips to Received, and the payable becomes real money owed.
'  Before this dialog, pressing "Save Receipt" did all of that immediately, and
'  the only trace of who did it was stock_movements.user_id — whoever happened
'  to be signed in.
'
'  That is not an attestation. It does not record that a person actually looked
'  at the delivery, it does not say the goods were sound, and a terminal left
'  signed in at the dock could accept a delivery under someone else's name.
'
'  WHAT IT ASKS FOR
'  ----------------
'    * Receiver name   — who physically checked the goods. Defaults to the
'                        signed-in user's name but is editable, because the
'                        person at the dock is often a warehouse hand working
'                        on the account of whoever opened the till.
'    * Condition       — Good, or Discrepancy. Choosing Discrepancy makes
'                        remarks mandatory: "something was wrong" with no
'                        detail is not a usable record.
'    * Certification   — an explicit tick. It is the sentence the receiver is
'                        put on record as asserting, so it is worded as a
'                        statement rather than a label.
'    * Password        — the signed-in user's own password, re-entered.
'
'  WHY THE PASSWORD
'  ----------------
'  The name field alone can be typed by anyone. Re-entering the password proves
'  the person committing the receipt is really the account holder, so the
'  attestation is bound to an authenticated identity rather than an ambient
'  session. It is verified with AccountRequests.VerifyCurrentPassword, which
'  fails CLOSED — a database error is a refusal, never a way past the check.
'
'  Modelled on ChangePasswordModal: same card, same field chrome, same button
'  treatment, so it reads as part of the same application.
' =============================================================================
Public Module ReceiveConfirmModal

    Private ReadOnly CardBg As Color = Color.White
    Private ReadOnly TitleClr As Color = Color.FromArgb(25, 40, 35)
    Private ReadOnly BodyClr As Color = Color.FromArgb(80, 100, 94)
    Private ReadOnly BorderClr As Color = Color.FromArgb(160, 195, 185)
    Private ReadOnly DividerClr As Color = Color.FromArgb(230, 238, 235)
    Private ReadOnly MintPrimary As Color = Theme.Primary
    Private ReadOnly MintDark As Color = Theme.Dark
    Private ReadOnly MintDeep As Color = Theme.Deep
    Private ReadOnly SubText As Color = Theme.SubText
    Private ReadOnly FieldBg As Color = Theme.PageBg
    Private ReadOnly FieldBorder As Color = Theme.BorderMuted
    Private ReadOnly TileBg As Color = Theme.TileBg
    Private ReadOnly ErrClr As Color = Color.FromArgb(200, 65, 65)
    Private ReadOnly WarnClr As Color = Theme.Warning

    Private Const RADIUS As Integer = 10
    Private Const CARD_W As Integer = 470
    Private Const PAD As Integer = 24
    Private Const FIELD_H As Integer = 40

    ''' <summary>
    ''' What the receiver attested to. Returned by Prompt so the caller can
    ''' store it alongside the stock movements it writes.
    ''' </summary>
    Public Class Attestation
        Public ReceiverName As String
        Public ConditionNote As String   ' "Good" | "Discrepancy"
        Public Remarks As String
    End Class

    ' =========================================================================
    '  Ask the receiver to confirm. Returns Nothing when they backed out, in
    '  which case the caller must not write anything.
    ' =========================================================================
    Public Function Prompt(poNo As String, supplierName As String,
                           lineCount As Integer, totalUnits As Integer) As Attestation

        Dim result As Attestation = Nothing

        Dim f As New Form With {
            .FormBorderStyle = FormBorderStyle.None,
            .StartPosition = FormStartPosition.CenterParent,
            .BackColor = CardBg,
            .ShowInTaskbar = False,
            .Width = CARD_W,
            .KeyPreview = True
        }

        Dim lblTitle As New Label With {
            .Text = "Confirm goods receipt",
            .Font = New Font("Segoe UI Semibold", 12.0F),
            .ForeColor = TitleClr,
            .AutoSize = False,
            .Location = New Point(PAD, PAD),
            .Size = New Size(CARD_W - PAD * 2, 22),
            .BackColor = Color.Transparent
        }

        ' A summary tile rather than a sentence: the receiver is checking these
        ' three facts against the delivery in front of them, so they need to be
        ' scannable, not buried in prose.
        Dim summary As New Panel With {
            .Location = New Point(PAD, lblTitle.Bottom + 10),
            .Size = New Size(CARD_W - PAD * 2, 58),
            .BackColor = TileBg
        }
        AddHandler summary.Paint,
            Sub(s As Object, e As PaintEventArgs)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim rc As New Rectangle(0, 0, summary.Width - 1, summary.Height - 1)
                Using path As GraphicsPath = RoundedPath(rc, 8)
                    Using br As New SolidBrush(TileBg)
                        e.Graphics.FillPath(br, path)
                    End Using
                End Using
            End Sub

        Dim lblPo As New Label With {
            .Text = poNo,
            .Font = New Font("Segoe UI Semibold", 11.0F),
            .ForeColor = MintDark, .AutoSize = True,
            .Location = New Point(14, 9), .BackColor = Color.Transparent
        }
        Dim lblPoSub As New Label With {
            .Text = supplierName & "   " & ChrW(8226) & "   " &
                    lineCount & If(lineCount = 1, " item", " items") & "   " & ChrW(8226) & "   " &
                    totalUnits & If(totalUnits = 1, " unit", " units"),
            .Font = New Font("Segoe UI", 9.0F),
            .ForeColor = SubText, .AutoSize = True,
            .Location = New Point(14, 32), .BackColor = Color.Transparent
        }
        summary.Controls.AddRange({lblPo, lblPoSub})

        ' ── Receiver name ─────────────────────────────────────────────────────
        Dim lblWho As New Label With {
            .Text = "Received by",
            .Font = New Font("Segoe UI Semibold", 9.0F),
            .ForeColor = SubText, .AutoSize = True,
            .Location = New Point(PAD, summary.Bottom + 14),
            .BackColor = Color.Transparent
        }
        Dim txtWho As TextBox = MakeField(PAD, lblWho.Bottom + 4, CARD_W - PAD * 2, f, False)
        txtWho.Text = If(String.IsNullOrWhiteSpace(Session.CurrentFullName),
                         "", Session.CurrentFullName.Trim())

        Dim lblWhoHint As New Label With {
            .Text = "The person who physically checked this delivery.",
            .Font = New Font("Segoe UI", 8.5F),
            .ForeColor = Color.FromArgb(150, 165, 160), .AutoSize = True,
            .Location = New Point(PAD, txtWho.Parent.Bottom + 3),
            .BackColor = Color.Transparent
        }

        ' ── Condition ─────────────────────────────────────────────────────────
        Dim lblCond As New Label With {
            .Text = "Condition of goods",
            .Font = New Font("Segoe UI Semibold", 9.0F),
            .ForeColor = SubText, .AutoSize = True,
            .Location = New Point(PAD, lblWhoHint.Bottom + 12),
            .BackColor = Color.Transparent
        }
        Dim rbGood As New RadioButton With {
            .Text = "Complete and in good condition",
            .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = TitleClr, .AutoSize = True,
            .Location = New Point(PAD + 2, lblCond.Bottom + 6),
            .BackColor = Color.Transparent,
            .Cursor = Cursors.Hand,
            .Checked = True
        }
        Dim rbBad As New RadioButton With {
            .Text = "Received with a discrepancy",
            .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = TitleClr, .AutoSize = True,
            .Location = New Point(PAD + 2, rbGood.Bottom + 4),
            .BackColor = Color.Transparent,
            .Cursor = Cursors.Hand
        }

        ' ── Remarks ───────────────────────────────────────────────────────────
        Dim lblRem As New Label With {
            .Text = "Remarks",
            .Font = New Font("Segoe UI Semibold", 9.0F),
            .ForeColor = SubText, .AutoSize = True,
            .Location = New Point(PAD, rbBad.Bottom + 10),
            .BackColor = Color.Transparent
        }
        Dim txtRem As TextBox = MakeField(PAD, lblRem.Bottom + 4, CARD_W - PAD * 2, f, False)
        Dim lblRemHint As New Label With {
            .Text = "Optional.",
            .Font = New Font("Segoe UI", 8.5F),
            .ForeColor = Color.FromArgb(150, 165, 160), .AutoSize = True,
            .Location = New Point(PAD, txtRem.Parent.Bottom + 3),
            .BackColor = Color.Transparent
        }

        ' Picking "discrepancy" turns remarks into the required field it has to
        ' be — the whole value of the flag is the detail that follows it.
        Dim SyncRemarks As Action =
            Sub()
                If rbBad.Checked Then
                    lblRem.Text = "Remarks *"
                    lblRemHint.Text = "Required — say what was short, damaged or wrong."
                    lblRemHint.ForeColor = WarnClr
                Else
                    lblRem.Text = "Remarks"
                    lblRemHint.Text = "Optional."
                    lblRemHint.ForeColor = Color.FromArgb(150, 165, 160)
                End If
            End Sub
        AddHandler rbGood.CheckedChanged, Sub() SyncRemarks()
        AddHandler rbBad.CheckedChanged, Sub() SyncRemarks()

        ' ── Certification ─────────────────────────────────────────────────────
        Dim chkCert As New CheckBox With {
            .Text = "I certify that I inspected these goods and that the quantities" &
                    Environment.NewLine & "above are what was actually delivered.",
            .Font = New Font("Segoe UI", 9.0F),
            .ForeColor = TitleClr,
            .AutoSize = False,
            .Size = New Size(CARD_W - PAD * 2, 38),
            .Location = New Point(PAD + 2, lblRemHint.Bottom + 12),
            .BackColor = Color.Transparent,
            .Cursor = Cursors.Hand
        }

        ' ── Password ──────────────────────────────────────────────────────────
        Dim lblPw As New Label With {
            .Text = "Your password",
            .Font = New Font("Segoe UI Semibold", 9.0F),
            .ForeColor = SubText, .AutoSize = True,
            .Location = New Point(PAD, chkCert.Bottom + 8),
            .BackColor = Color.Transparent
        }
        Dim txtPw As TextBox = MakeField(PAD, lblPw.Bottom + 4, CARD_W - PAD * 2, f, True)
        Dim lblPwHint As New Label With {
            .Text = "Confirms this receipt is being accepted by you, " &
                    If(String.IsNullOrWhiteSpace(Session.CurrentUsername),
                       "the signed-in user", Session.CurrentUsername) & ".",
            .Font = New Font("Segoe UI", 8.5F),
            .ForeColor = Color.FromArgb(150, 165, 160), .AutoSize = True,
            .Location = New Point(PAD, txtPw.Parent.Bottom + 3),
            .BackColor = Color.Transparent
        }

        Dim lblErr As New Label With {
            .Text = "",
            .Font = New Font("Segoe UI", 9.0F),
            .ForeColor = ErrClr,
            .AutoSize = False,
            .Location = New Point(PAD, lblPwHint.Bottom + 6),
            .Size = New Size(CARD_W - PAD * 2, 32),
            .BackColor = Color.Transparent
        }

        Dim dividerY As Integer = lblErr.Bottom + 8
        Dim btnY As Integer = dividerY + 14
        Dim cardH As Integer = btnY + 38 + PAD

        Dim btnOk As Button = MakeButton("Confirm receipt", True,
            New Point(CARD_W - PAD - 150, btnY), New Size(150, 38))
        Dim btnCancel As Button = MakeButton("Cancel", False,
            New Point(CARD_W - PAD - 150 - 8 - 96, btnY), New Size(96, 38))

        AddHandler btnCancel.Click,
            Sub()
                result = Nothing
                f.Close()
            End Sub

        AddHandler btnOk.Click,
            Sub()
                Dim who As String = txtWho.Text.Trim()
                Dim rem_ As String = txtRem.Text.Trim()

                If who = "" Then
                    lblErr.Text = "Enter the name of the person who received the goods."
                    txtWho.Focus()
                    Return
                End If
                If who.Length > 120 Then
                    lblErr.Text = "That name is too long."
                    txtWho.Focus()
                    Return
                End If
                If rbBad.Checked AndAlso rem_ = "" Then
                    lblErr.Text = "Describe the discrepancy in the remarks."
                    txtRem.Focus()
                    Return
                End If
                If rem_.Length > 500 Then
                    lblErr.Text = "Remarks are too long (500 characters maximum)."
                    txtRem.Focus()
                    Return
                End If
                If Not chkCert.Checked Then
                    lblErr.Text = "Tick the certification to confirm you inspected the goods."
                    Return
                End If
                If txtPw.Text = "" Then
                    lblErr.Text = "Enter your password to confirm."
                    txtPw.Focus()
                    Return
                End If

                ' Fails closed: a database error here is a refusal, not a pass.
                If Not AccountRequests.VerifyCurrentPassword(Session.CurrentUserId, txtPw.Text) Then
                    lblErr.Text = "That password is not correct."
                    txtPw.Focus()
                    txtPw.SelectAll()
                    Return
                End If

                result = New Attestation With {
                    .ReceiverName = who,
                    .ConditionNote = If(rbBad.Checked, "Discrepancy", "Good"),
                    .Remarks = rem_
                }
                f.Close()
            End Sub

        f.Height = cardH
        f.Controls.AddRange({lblTitle, summary, lblWho, lblWhoHint, lblCond,
                             rbGood, rbBad, lblRem, lblRemHint, chkCert,
                             lblPw, lblPwHint, lblErr, btnOk, btnCancel})

        Dim dividerYLocal As Integer = dividerY
        AddHandler f.Paint,
            Sub(s As Object, e As PaintEventArgs)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim card As New Rectangle(0, 0, f.Width - 1, f.Height - 1)
                Using pen As New Pen(DividerClr, 1.0F)
                    g.DrawLine(pen, PAD, dividerYLocal, f.Width - PAD, dividerYLocal)
                End Using
                Using path As GraphicsPath = RoundedPath(card, RADIUS)
                    Using pen As New Pen(BorderClr, 1.2F)
                        g.DrawPath(pen, path)
                    End Using
                End Using
            End Sub

        Using gp As GraphicsPath = RoundedPath(New Rectangle(0, 0, CARD_W, cardH), RADIUS)
            f.Region = New System.Drawing.Region(gp)
        End Using

        ' Enter confirms, Escape backs out. Escape IS allowed here, unlike the
        ' forced password change: abandoning a receipt is a legitimate choice —
        ' the goods simply stay un-received until someone confirms.
        f.AcceptButton = btnOk
        AddHandler f.KeyDown,
            Sub(s As Object, e As KeyEventArgs)
                If e.KeyCode <> Keys.Escape Then Return
                e.Handled = True
                result = Nothing
                f.Close()
            End Sub

        AddHandler f.Shown, Sub() txtWho.Focus()

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

        Return result
    End Function

    ' =========================================================================
    '  PERSISTENCE
    '
    '  Writes the attestation row. Takes the caller's open connection and
    '  transaction so the receipt record commits or rolls back together with
    '  the stock movements it describes — an attestation for goods that were
    '  never booked in, or movements with no attestation, would both be worse
    '  than the previous behaviour.
    ' =========================================================================
    Public Sub Save(conn As MySqlConnection, txn As MySqlTransaction,
                    poId As Integer, att As Attestation, totalUnits As Integer)
        If att Is Nothing Then Return

        Using cmd As New MySqlCommand(
            "INSERT INTO goods_receipts " &
            "(po_id, received_by, receiver_name, condition_note, remarks, attested, total_units, received_at) " &
            "VALUES (@po, @by, @who, @cond, @rem, 1, @units, NOW())", conn, txn)
            cmd.Parameters.AddWithValue("@po", poId)
            cmd.Parameters.AddWithValue("@by",
                If(Session.CurrentUserId = 0, CObj(DBNull.Value), CObj(Session.CurrentUserId)))
            cmd.Parameters.AddWithValue("@who", att.ReceiverName)
            cmd.Parameters.AddWithValue("@cond", att.ConditionNote)
            cmd.Parameters.AddWithValue("@rem",
                If(String.IsNullOrWhiteSpace(att.Remarks), CObj(DBNull.Value), CObj(att.Remarks)))
            cmd.Parameters.AddWithValue("@units", totalUnits)
            cmd.ExecuteNonQuery()
        End Using
    End Sub

    ''' <summary>
    ''' True when the goods_receipts table exists. Probed per commit rather
    ''' than cached, because it is one cheap round-trip against a screen the
    ''' user reaches a handful of times a day, and a cached False would
    ''' outlive the migration being applied mid-session.
    ''' </summary>
    Public Function HasTable() As Boolean
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT COUNT(*) FROM information_schema.tables " &
                    "WHERE table_schema = DATABASE() AND table_name = 'goods_receipts'", conn)
                    Return Convert.ToInt32(cmd.ExecuteScalar()) > 0
                End Using
            End Using
        Catch
            ' No access, no table as far as the caller is concerned: it falls
            ' back to receiving without a stored attestation, exactly as before.
            Return False
        End Try
    End Function

    ' =========================================================================
    '  CHROME  (matched to ChangePasswordModal so the two read as one app)
    ' =========================================================================
    Private Function MakeField(x As Integer, y As Integer, w As Integer,
                               host As Form, masked As Boolean) As TextBox
        Dim pnl As New Panel With {
            .Location = New Point(x, y),
            .Size = New Size(w, FIELD_H),
            .BackColor = FieldBg
        }

        Dim txt As New TextBox With {
            .BorderStyle = BorderStyle.None,
            .Font = New Font("Segoe UI", 10.5F),
            .BackColor = FieldBg,
            .ForeColor = TitleClr,
            .Location = New Point(12, (FIELD_H - 20) \ 2),
            .Width = If(masked, w - 70, w - 24),
            .UseSystemPasswordChar = masked
        }

        If masked Then
            Dim btnEye As New Label With {
                .Text = "SHOW",
                .Font = New Font("Segoe UI", 7.5F, FontStyle.Bold),
                .ForeColor = SubText,
                .AutoSize = True,
                .BackColor = Color.Transparent,
                .Cursor = Cursors.Hand
            }
            btnEye.Location = New Point(w - btnEye.PreferredWidth - 12,
                                        (FIELD_H - btnEye.PreferredHeight) \ 2)
            AddHandler btnEye.Click,
                Sub()
                    txt.UseSystemPasswordChar = Not txt.UseSystemPasswordChar
                    btnEye.Text = If(txt.UseSystemPasswordChar, "SHOW", "HIDE")
                    btnEye.ForeColor = If(txt.UseSystemPasswordChar, SubText, MintPrimary)
                End Sub
            pnl.Controls.Add(btnEye)
        End If

        AddHandler pnl.Paint,
            Sub(s As Object, e As PaintEventArgs)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim rc As New Rectangle(0, 0, pnl.Width - 1, pnl.Height - 1)
                Using path As GraphicsPath = RoundedPath(rc, 8)
                    Using b As New SolidBrush(FieldBg)
                        e.Graphics.FillPath(b, path)
                    End Using
                    Using pen As New Pen(If(txt.Focused, MintPrimary, FieldBorder), 1.2F)
                        e.Graphics.DrawPath(pen, path)
                    End Using
                End Using
            End Sub

        AddHandler txt.GotFocus, Sub() pnl.Invalidate()
        AddHandler txt.LostFocus, Sub() pnl.Invalidate()

        pnl.Controls.Add(txt)
        host.Controls.Add(pnl)
        Return txt
    End Function

    Private Function MakeButton(caption As String, isPrimary As Boolean,
                                loc As Point, size As Size) As Button
        Dim b As New Button With {
            .Text = caption,
            .Size = size,
            .Location = loc,
            .FlatStyle = FlatStyle.Flat,
            .Font = New Font("Segoe UI Semibold", 9.5F),
            .Cursor = Cursors.Hand,
            .BackColor = If(isPrimary, MintPrimary, Color.FromArgb(243, 246, 244)),
            .ForeColor = If(isPrimary, Color.White, Color.FromArgb(70, 86, 80))
        }
        b.FlatAppearance.BorderSize = 0

        Dim normalClr As Color = b.BackColor
        Dim hoverClr As Color = If(isPrimary, MintDeep, Color.FromArgb(231, 234, 232))

        AddHandler b.Paint,
            Sub(s As Object, e As PaintEventArgs)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                g.PixelOffsetMode = PixelOffsetMode.HighQuality
                Using bgBrush As New SolidBrush(CardBg)
                    g.FillRectangle(bgBrush, b.ClientRectangle)
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
