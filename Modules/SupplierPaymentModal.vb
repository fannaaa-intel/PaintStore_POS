Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports MySql.Data.MySqlClient

' =============================================================================
'  SupplierPaymentModal
'
'  Records what was actually paid to a supplier against a purchase order.
'
'  WHAT THIS REPLACES
'    Settling a PO used to be one statement:
'
'        UPDATE purchase_orders SET paid_at = NOW()
'
'    which says a PO was settled and nothing more. How much, against which
'    supplier document, by whom, on what date, by what method -- none of it was
'    captured, so the only figure reporting could reach was the amount ORDERED.
'
'  THE STORE'S ACTUAL ARRANGEMENT
'    Payment upon delivery: the goods arrive, the supplier hands over a receipt,
'    and payment is made against THAT document. The receipt is the authority,
'    not the purchase order, so this dialog asks for the receipt's amount and
'    records it as given.
'
'  VARIANCE IS EVIDENCE, NOT AN ERROR
'    The receipt can legitimately differ from the PO total -- a short delivery,
'    a price change since quoting, a discount at the door. When it differs the
'    gap is the most useful number on the record, so it is shown live as the
'    amount is typed, and a differing amount requires a written reason before
'    it can be saved. It is never silently reconciled to the ordered figure.
'
'  Returns True when a payment was recorded, so the caller can refresh.
' =============================================================================
Public Module SupplierPaymentModal

    ' ── PALETTE (matches AppModal) ───────────────────────────────────────────
    Private ReadOnly CardBg As Color = Color.White
    Private ReadOnly TitleClr As Color = Color.FromArgb(25, 40, 35)
    Private ReadOnly BodyClr As Color = Color.FromArgb(80, 100, 94)
    Private ReadOnly BorderClr As Color = Color.FromArgb(160, 195, 185)
    Private ReadOnly DividerClr As Color = Color.FromArgb(230, 238, 235)
    Private ReadOnly MintPrimary As Color = Theme.Primary
    Private ReadOnly MintDeep As Color = Theme.Deep
    Private ReadOnly FieldBg As Color = Theme.PageBg
    Private ReadOnly FieldBorder As Color = Theme.BorderMuted
    Private ReadOnly FaintClr As Color = Theme.FaintText
    Private ReadOnly WarnClr As Color = Theme.Warning
    Private ReadOnly DangerClr As Color = Theme.Danger
    Private ReadOnly OkClr As Color = Color.FromArgb(0, 120, 92)
    Private ReadOnly Peso As String = ChrW(8369)

    Private Const RADIUS As Integer = 10

    ' The store pays its suppliers in cash only (owner's decision, Sept 2026),
    ' so the method is fixed rather than picked. Older payments recorded under
    ' other methods keep their stored value; only new payments are Cash.
    Private Const PAY_METHOD As String = "Cash"

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

    ' A rounded input that matches AppModal.Prompt's field exactly.
    Private Function MakeField(inner As Control, x As Integer, y As Integer,
                               w As Integer, Optional h As Integer = 40) As Panel
        Dim host As New Panel With {
            .Location = New Point(x, y), .Size = New Size(w, h), .BackColor = FieldBg
        }
        AddHandler host.Paint,
            Sub(sender As Object, ev As PaintEventArgs)
                ev.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Dim rc As New Rectangle(0, 0, host.Width - 1, host.Height - 1)
                Using path As GraphicsPath = RoundedPath(rc, 8)
                    Using b As New SolidBrush(FieldBg)
                        ev.Graphics.FillPath(b, path)
                    End Using
                    Using pen As New Pen(If(inner.Focused, MintPrimary, FieldBorder), 1.2F)
                        ev.Graphics.DrawPath(pen, path)
                    End Using
                End Using
            End Sub
        AddHandler inner.GotFocus, Sub() host.Invalidate()
        AddHandler inner.LostFocus, Sub() host.Invalidate()
        host.Controls.Add(inner)
        Return host
    End Function

    Private Function MakeCaption(text As String, x As Integer, y As Integer, w As Integer) As Label
        Return New Label With {
            .Text = text, .Font = New Font("Segoe UI Semibold", 8.5F),
            .ForeColor = FaintClr, .AutoSize = False,
            .Location = New Point(x, y), .Size = New Size(w, 16),
            .TextAlign = ContentAlignment.MiddleLeft, .BackColor = Color.Transparent
        }
    End Function

    Private Function MakeButton(text As String, primary As Boolean,
                                loc As Point, sz As Size) As Button
        Dim b As New Button With {
            .Text = text, .Location = loc, .Size = sz,
            .FlatStyle = FlatStyle.Flat, .Cursor = Cursors.Hand,
            .Font = New Font("Segoe UI Semibold", 9.5F),
            .BackColor = If(primary, MintDeep, Theme.FieldBg),
            .ForeColor = If(primary, Color.White, Color.FromArgb(70, 86, 80))
        }
        b.FlatAppearance.BorderSize = 0
        Using gp As GraphicsPath = RoundedPath(New Rectangle(0, 0, sz.Width, sz.Height), 7)
            b.Region = New Region(gp)
        End Using
        Return b
    End Function

    ' =========================================================================
    '  ENTRY POINT
    '
    '  poId / poNo / supplier / poTotal describe the order being settled.
    '  Returns True only when a payment row was actually written.
    ' =========================================================================
    Public Function Show(poId As Integer, poNo As String, supplier As String,
                         poTotal As Decimal) As Boolean

        Const CARD_W As Integer = 480
        Const PAD As Integer = 24
        Dim innerW As Integer = CARD_W - PAD * 2

        Dim f As New Form With {
            .FormBorderStyle = FormBorderStyle.None,
            .StartPosition = FormStartPosition.CenterParent,
            .BackColor = CardBg, .ShowInTaskbar = False,
            .Width = CARD_W, .KeyPreview = True
        }

        Dim lblTitle As New Label With {
            .Text = "Record Payment", .Font = New Font("Segoe UI Semibold", 12.0F),
            .ForeColor = TitleClr, .AutoSize = False,
            .Location = New Point(PAD, PAD), .Size = New Size(innerW, 22),
            .BackColor = Color.Transparent
        }

        Dim lblBody As New Label With {
            .Text = poNo & "   " & ChrW(8226) & "   " & supplier & Environment.NewLine &
                    "Ordered: " & Peso & poTotal.ToString("N2"),
            .Font = New Font("Segoe UI", 9.5F), .ForeColor = BodyClr,
            .AutoSize = False, .Location = New Point(PAD, lblTitle.Bottom + 8),
            .Size = New Size(innerW, 36), .BackColor = Color.Transparent
        }

        Dim y As Integer = lblBody.Bottom + 14

        ' ── Amount on the supplier's receipt ─────────────────────────────────
        Dim capAmount As Label = MakeCaption("AMOUNT ON SUPPLIER'S RECEIPT", PAD, y, innerW)
        y += 18

        Dim txtAmount As New TextBox With {
            .Font = New Font("Segoe UI", 12.0F), .BorderStyle = BorderStyle.None,
            .BackColor = FieldBg, .ForeColor = TitleClr,
            .Location = New Point(12, 10), .Width = innerW - 24,
            .Text = poTotal.ToString("0.00")
        }
        Dim fldAmount As Panel = MakeField(txtAmount, PAD, y, innerW, 42)
        y += 46

        ' Live variance readout. This is the whole point of the dialog: the gap
        ' between what was ordered and what the receipt says, stated as it is
        ' typed rather than discovered later in a report.
        Dim lblVariance As New Label With {
            .Text = "", .Font = New Font("Segoe UI", 9.0F),
            .ForeColor = FaintClr, .AutoSize = False,
            .Location = New Point(PAD + 2, y), .Size = New Size(innerW - 4, 32),
            .TextAlign = ContentAlignment.TopLeft, .BackColor = Color.Transparent
        }
        y += 36

        ' ── Supplier's reference number ──────────────────────────────────────
        Dim capRef As Label = MakeCaption("SUPPLIER'S RECEIPT / INVOICE NO.", PAD, y, innerW)
        y += 18
        Dim txtRef As New TextBox With {
            .Font = New Font("Segoe UI", 11.0F), .BorderStyle = BorderStyle.None,
            .BackColor = FieldBg, .ForeColor = TitleClr,
            .Location = New Point(12, 10), .Width = innerW - 24
        }
        Dim fldRef As Panel = MakeField(txtRef, PAD, y, innerW, 40)
        y += 50

        ' ── Method + date, side by side ──────────────────────────────────────
        Dim halfW As Integer = (innerW - 12) \ 2

        Dim capMethod As Label = MakeCaption("METHOD", PAD, y, halfW)
        Dim capDate As Label = MakeCaption("DATE PAID", PAD + halfW + 12, y, halfW)
        y += 18

        ' Fixed, not a dropdown: there is nothing to choose.
        Dim lblMethod As New Label With {
            .Text = PAY_METHOD & "   " & ChrW(8226) & "   cash payments only",
            .Font = New Font("Segoe UI Semibold", 10.5F),
            .BackColor = FieldBg, .ForeColor = TitleClr,
            .AutoSize = False, .Location = New Point(12, 10),
            .Size = New Size(halfW - 24, 20),
            .TextAlign = ContentAlignment.MiddleLeft
        }
        Dim fldMethod As Panel = MakeField(lblMethod, PAD, y, halfW, 40)

        ' Money can be handed over on Friday and typed in on Monday, so the date
        ' is editable rather than pinned to NOW(). Future dates are refused at
        ' save -- a payment that has not happened yet is not a payment.
        Dim dtPaid As New DateTimePicker With {
            .Font = New Font("Segoe UI", 10.5F), .Format = DateTimePickerFormat.Custom,
            .CustomFormat = "MMM d, yyyy", .Location = New Point(10, 9),
            .Width = halfW - 20, .Value = DateTime.Today,
            .MaxDate = DateTime.Today
        }
        Dim fldDate As Panel = MakeField(dtPaid, PAD + halfW + 12, y, halfW, 40)
        y += 50

        ' ── Remarks ──────────────────────────────────────────────────────────
        Dim capRemarks As Label = MakeCaption("REMARKS", PAD, y, innerW)
        y += 18
        Dim txtRemarks As New TextBox With {
            .Font = New Font("Segoe UI", 10.0F), .BorderStyle = BorderStyle.None,
            .BackColor = FieldBg, .ForeColor = TitleClr,
            .Location = New Point(12, 10), .Width = innerW - 24,
            .Multiline = True, .Height = 44
        }
        Dim fldRemarks As Panel = MakeField(txtRemarks, PAD, y, innerW, 64)
        y += 74

        ' ── Variance recompute ───────────────────────────────────────────────
        Dim parsedAmount As Decimal = poTotal
        Dim amountValid As Boolean = True

        Dim recompute As Action =
            Sub()
                Dim raw As String = txtAmount.Text.Trim().Replace(Peso, "").Replace(",", "")
                Dim val As Decimal
                If Not Decimal.TryParse(raw, val) Then
                    amountValid = False
                    lblVariance.ForeColor = DangerClr
                    lblVariance.Text = "Enter the amount printed on the receipt."
                    Return
                End If
                If val < 0D Then
                    amountValid = False
                    lblVariance.ForeColor = DangerClr
                    lblVariance.Text = "The amount cannot be negative."
                    Return
                End If

                amountValid = True
                parsedAmount = Math.Round(val, 2)
                Dim diff As Decimal = parsedAmount - poTotal

                If diff = 0D Then
                    lblVariance.ForeColor = OkClr
                    lblVariance.Text = ChrW(10003) & " Matches the purchase order exactly."
                ElseIf diff < 0D Then
                    lblVariance.ForeColor = WarnClr
                    lblVariance.Text = Peso & Math.Abs(diff).ToString("N2") &
                                       " LESS than ordered." & Environment.NewLine &
                                       "Give the reason in remarks before saving."
                Else
                    lblVariance.ForeColor = WarnClr
                    lblVariance.Text = Peso & diff.ToString("N2") &
                                       " MORE than ordered." & Environment.NewLine &
                                       "Give the reason in remarks before saving."
                End If
            End Sub

        AddHandler txtAmount.TextChanged, Sub() recompute()
        recompute()

        ' ── Buttons ──────────────────────────────────────────────────────────
        Dim dividerY As Integer = y + 4
        Dim btnRowTop As Integer = dividerY + 16
        Const BTN_H As Integer = 36
        Const BTN_W As Integer = 110
        Const GAP As Integer = 8

        Dim saved As Boolean = False

        Dim btnCancel As Button = MakeButton("Cancel", False,
            New Point(CARD_W - PAD - BTN_W * 2 - GAP, btnRowTop), New Size(BTN_W, BTN_H))
        Dim btnSave As Button = MakeButton("Record Payment", True,
            New Point(CARD_W - PAD - BTN_W, btnRowTop), New Size(BTN_W, BTN_H))

        AddHandler btnCancel.Click,
            Sub()
                f.DialogResult = DialogResult.Cancel
                f.Close()
            End Sub

        AddHandler btnSave.Click,
            Sub()
                recompute()
                If Not amountValid Then
                    AppModal.Show("Enter a valid amount from the supplier's receipt.",
                                  "Invalid Amount", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    txtAmount.Focus()
                    Return
                End If

                If parsedAmount = 0D Then
                    If AppModal.Show(
                        "Record a payment of " & Peso & "0.00?" & Environment.NewLine &
                        "This clears " & poNo & " without any money changing hands.",
                        "Zero Payment", MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning) <> DialogResult.Yes Then
                        Return
                    End If
                End If

                ' A gap between ordered and paid is exactly what an audit asks
                ' about, so it cannot be saved unexplained. The dialog does not
                ' block the payment -- it blocks the SILENCE around it.
                Dim diff As Decimal = parsedAmount - poTotal
                If diff <> 0D AndAlso txtRemarks.Text.Trim().Length = 0 Then
                    AppModal.Show(
                        "This payment differs from the purchase order by " &
                        Peso & Math.Abs(diff).ToString("N2") & "." & Environment.NewLine &
                        Environment.NewLine &
                        "Say why in remarks -- short delivery, price change, " &
                        "discount -- so the difference is explained on the record.",
                        "Reason Required", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    txtRemarks.Focus()
                    Return
                End If

                If SavePayment(poId, poNo, supplier, parsedAmount, poTotal,
                               txtRef.Text.Trim(),
                               PAY_METHOD,
                               dtPaid.Value.Date,
                               txtRemarks.Text.Trim()) Then
                    saved = True
                    f.DialogResult = DialogResult.OK
                    f.Close()
                End If
            End Sub

        f.Controls.AddRange({lblTitle, lblBody,
                             capAmount, fldAmount, lblVariance,
                             capRef, fldRef,
                             capMethod, capDate, fldMethod, fldDate,
                             capRemarks, fldRemarks,
                             btnCancel, btnSave})

        Dim cardH As Integer = btnRowTop + BTN_H + PAD
        f.Height = cardH

        Dim dividerYLocal As Integer = dividerY
        AddHandler f.Paint,
            Sub(s As Object, e As PaintEventArgs)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim card As New Rectangle(0, 0, f.Width - 1, f.Height - 1)
                Using br As New SolidBrush(CardBg)
                    g.FillRectangle(br, card)
                End Using
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
            f.Region = New Region(gp)
        End Using

        f.AcceptButton = btnSave
        AddHandler f.KeyDown,
            Sub(s As Object, e As KeyEventArgs)
                If e.KeyCode = Keys.Escape Then
                    f.DialogResult = DialogResult.Cancel
                    f.Close()
                End If
            End Sub
        AddHandler f.Shown,
            Sub()
                txtAmount.Focus()
                txtAmount.SelectAll()
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

        Return saved
    End Function

    ' =========================================================================
    '  SAVE
    '
    '  The payment row and the PO's paid_at stamp are written in one
    '  transaction: a recorded payment that left the PO sitting in payables,
    '  or a cleared PO with no payment behind it, would both be wrong.
    ' =========================================================================
    Private Function SavePayment(poId As Integer, poNo As String, supplier As String,
                                 amount As Decimal, poTotal As Decimal,
                                 referenceNo As String, method As String,
                                 paidOn As Date, remarks As String) As Boolean
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using tx = conn.BeginTransaction()

                    Using cmd As New MySqlCommand(
                        "INSERT INTO supplier_payments " &
                        "(po_id, amount_paid, po_total, reference_no, method, " &
                        " paid_on, remarks, recorded_by, recorded_at) " &
                        "VALUES (@po, @amt, @tot, @ref, @mth, @on, @rem, @by, NOW())",
                        conn, tx)
                        cmd.Parameters.AddWithValue("@po", poId)
                        cmd.Parameters.AddWithValue("@amt", amount)
                        cmd.Parameters.AddWithValue("@tot", poTotal)
                        cmd.Parameters.AddWithValue("@ref",
                            If(referenceNo.Length = 0, CObj(DBNull.Value), referenceNo))
                        cmd.Parameters.AddWithValue("@mth", method)
                        cmd.Parameters.AddWithValue("@on", paidOn)
                        cmd.Parameters.AddWithValue("@rem",
                            If(remarks.Length = 0, CObj(DBNull.Value), remarks))
                        cmd.Parameters.AddWithValue("@by", Session.CurrentUserId)
                        cmd.ExecuteNonQuery()
                    End Using

                    ' Guarded on paid_at IS NULL so two accountants settling the
                    ' same PO cannot overwrite the first settlement date.
                    Dim affected As Integer
                    Using cmd As New MySqlCommand(
                        "UPDATE purchase_orders SET paid_at = @on " &
                        "WHERE po_id = @po AND paid_at IS NULL", conn, tx)
                        cmd.Parameters.AddWithValue("@on", paidOn)
                        cmd.Parameters.AddWithValue("@po", poId)
                        affected = cmd.ExecuteNonQuery()
                    End Using

                    tx.Commit()

                    ' Money leaving the business is the most audited event this
                    ' system records, so the amount, the supplier document and
                    ' any variance all go in the trail -- not just the PO number.
                    Dim diff As Decimal = amount - poTotal
                    Dim trail As String =
                        poNo & " to " & supplier & ", " & Peso & amount.ToString("N2") &
                        " via " & method &
                        If(referenceNo.Length > 0, " (ref " & referenceNo & ")", "") &
                        If(diff <> 0D,
                           " — " & Peso & Math.Abs(diff).ToString("N2") &
                           If(diff < 0D, " under", " over") & " ordered",
                           "")
                    ActivityLog.Record("PO_PAID", "purchase_order", poId, trail)

                    If affected = 0 Then
                        AppModal.Show(
                            "The payment was recorded, but " & poNo &
                            " had already been settled." & Environment.NewLine &
                            "Its original settlement date was left unchanged.",
                            "Already Settled", MessageBoxButtons.OK,
                            MessageBoxIcon.Information)
                    Else
                        AppModal.Show(
                            Peso & amount.ToString("N2") & " recorded against " & poNo & "." &
                            Environment.NewLine &
                            If(diff = 0D,
                               "Cleared from payables.",
                               "Cleared from payables with a " &
                               Peso & Math.Abs(diff).ToString("N2") &
                               If(diff < 0D, " shortfall", " overpayment") & " on record."),
                            "Payment Recorded", MessageBoxButtons.OK,
                            MessageBoxIcon.Information)
                    End If

                    Return True
                End Using
            End Using

        Catch ex As Exception
            AppModal.Show("Could not record the payment:" & Environment.NewLine & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return False
        End Try
    End Function

End Module
