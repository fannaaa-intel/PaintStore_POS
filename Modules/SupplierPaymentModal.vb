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
'  PART PAYMENTS
'    An order can be paid in parts. The form shows the order total, what is
'    already paid and the balance, and says live what this payment will do:
'    pay the balance (order becomes Paid) or leave some owed (Partial). It can
'    never be more than the balance. When the supplier accepted less (discount,
'    short delivery), the payment can be marked final with a written reason,
'    which closes the order. See SupplierPayments for the status rules.
'
'  Opened for one order from Accountant > Payables (Show), or from Procurement
'  > Payment Records where the order is picked in the form (ShowNew).
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
    '  ENTRY POINTS
    '
    '  Show     -- for one known order (Accountant > Payables).
    '  ShowNew  -- pick the order in the form (Procurement > Payment Records).
    '  Both return True only when a payment row was actually written.
    ' =========================================================================
    Public Function Show(poId As Integer, poNo As String, supplier As String,
                         poTotal As Decimal) As Boolean
        Return ShowForm(poId)
    End Function

    Public Function ShowNew() As Boolean
        Return ShowForm(0)
    End Function

    Private Class ItemLine
        Public Name As String = ""
        Public Qty As Integer
        Public UnitCost As Decimal
        Public LineCost As Decimal
    End Class

    Private Function ShowForm(presetPoId As Integer) As Boolean
        ' ── Which orders can be paid ─────────────────────────────────────────
        Dim choices As New List(Of SupplierPayments.PayablePo)
        Try
            If presetPoId > 0 Then
                Dim one As SupplierPayments.PayablePo = SupplierPayments.LoadPo(presetPoId)
                If one Is Nothing Then
                    AppModal.Show("That purchase order could not be found.", "Not Found",
                                  MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    Return False
                End If
                If one.IsPaid Then
                    AppModal.Show(one.PoNo & " is already fully paid.", "Already Paid",
                                  MessageBoxButtons.OK, MessageBoxIcon.Information)
                    Return False
                End If
                choices.Add(one)
            Else
                choices = SupplierPayments.LoadOpenPos()
            End If
        Catch ex As Exception
            AppModal.Show("Could not load purchase orders:" & Environment.NewLine & ErrorLog.ForUser(ex),
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return False
        End Try
        If choices.Count = 0 Then
            AppModal.Show("No purchase orders are waiting for payment." & Environment.NewLine &
                          "Orders appear here once they are marked as Ordered.",
                          "Nothing to Pay", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return False
        End If

        Const CARD_W As Integer = 780
        Const PAD As Integer = 24
        Dim innerW As Integer = CARD_W - PAD * 2
        Dim colW As Integer = (innerW - 16) \ 2
        Dim col2X As Integer = PAD + colW + 16

        Dim f As New Form With {
            .FormBorderStyle = FormBorderStyle.None,
            .StartPosition = FormStartPosition.CenterParent,
            .BackColor = CardBg, .ShowInTaskbar = False,
            .Width = CARD_W, .KeyPreview = True
        }

        Dim lblTitle As New Label With {
            .Text = "Record Payment", .Font = New Font("Segoe UI Semibold", 13.0F),
            .ForeColor = TitleClr, .AutoSize = False,
            .Location = New Point(PAD, PAD), .Size = New Size(innerW, 24),
            .BackColor = Color.Transparent
        }
        Dim lblBody As New Label With {
            .Text = "Money paid to a supplier for a purchase order. " & PAY_METHOD &
                    " only; the payment number is given when you save.",
            .Font = New Font("Segoe UI", 9.5F), .ForeColor = BodyClr,
            .AutoSize = False, .Location = New Point(PAD, lblTitle.Bottom + 4),
            .Size = New Size(innerW, 20), .BackColor = Color.Transparent
        }
        Dim y As Integer = lblBody.Bottom + 14

        ' ── Purchase order ───────────────────────────────────────────────────
        Dim capPo As Label = MakeCaption("PURCHASE ORDER  *", PAD, y, innerW)
        y += 18
        Dim cboPo As New ComboBox With {
            .DropDownStyle = ComboBoxStyle.DropDownList, .FlatStyle = FlatStyle.Flat,
            .Font = New Font("Segoe UI", 10.5F), .BackColor = FieldBg, .ForeColor = TitleClr,
            .Location = New Point(8, 7), .Width = innerW - 16, .IntegralHeight = False,
            .MaxDropDownItems = 12, .Enabled = (presetPoId = 0)
        }
        For Each po In choices
            cboPo.Items.Add(po.PoNo & "   " & ChrW(8212) & "   " & po.Supplier &
                            "   (balance " & Peso & po.Balance.ToString("N2") & ")")
        Next
        ' Drawn by hand: the stock list paints the chosen order as a solid blue
        ' bar, and shows nothing at all before one is picked.
        cboPo.DrawMode = DrawMode.OwnerDrawFixed
        cboPo.ItemHeight = 22
        AddHandler cboPo.DrawItem,
            Sub(sender As Object, ev As DrawItemEventArgs)
                Dim isEdit As Boolean = (ev.State And DrawItemState.ComboBoxEdit) = DrawItemState.ComboBoxEdit
                Dim hot As Boolean = Not isEdit AndAlso (ev.State And DrawItemState.Selected) = DrawItemState.Selected
                Using bg As New SolidBrush(If(hot, Theme.RowHover, FieldBg))
                    ev.Graphics.FillRectangle(bg, ev.Bounds)
                End Using
                Dim text As String = If(ev.Index >= 0, cboPo.Items(ev.Index).ToString(), "Choose a purchase order" & ChrW(8230))
                TextRenderer.DrawText(ev.Graphics, text, cboPo.Font, ev.Bounds,
                    If(ev.Index >= 0, TitleClr, FaintClr),
                    TextFormatFlags.VerticalCenter Or TextFormatFlags.Left Or TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPrefix)
            End Sub
        Dim fldPo As Panel = MakeField(cboPo, PAD, y, innerW, 40)
        y += 40 + 10

        ' Order facts: total, paid so far, balance, status.
        Dim facts As New Panel With {
            .Location = New Point(PAD, y), .Size = New Size(innerW, 52), .BackColor = Theme.TileBg
        }
        AddHandler facts.Paint,
            Sub(sender As Object, ev As PaintEventArgs)
                ev.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Using path As GraphicsPath = RoundedPath(New Rectangle(0, 0, facts.Width - 1, facts.Height - 1), 8)
                    Using b As New SolidBrush(Theme.TileBg)
                        ev.Graphics.FillPath(b, path)
                    End Using
                End Using
            End Sub
        Dim factCaps = {"PO TOTAL", "PAID SO FAR", "BALANCE", "STATUS NOW"}
        Dim factVals(3) As Label
        Dim factW As Integer = innerW \ 4
        For i As Integer = 0 To 3
            facts.Controls.Add(New Label With {
                .Text = factCaps(i), .Font = New Font("Segoe UI Semibold", 8.0F),
                .ForeColor = FaintClr, .AutoSize = True,
                .Location = New Point(14 + i * factW, 8), .BackColor = Color.Transparent
            })
            factVals(i) = New Label With {
                .Font = New Font("Segoe UI Semibold", 10.5F), .ForeColor = TitleClr,
                .AutoSize = False, .AutoEllipsis = True, .Size = New Size(factW - 20, 20),
                .Location = New Point(14 + i * factW, 25), .BackColor = Color.Transparent
            }
            facts.Controls.Add(factVals(i))
        Next
        y = facts.Bottom + 10

        ' Items on the order, so the payer can check what the money is for.
        Dim capItems As Label = MakeCaption("ITEMS ON THIS ORDER", PAD, y, innerW)
        y += 18
        Dim itemsHost As New Panel With {
            .Location = New Point(PAD, y), .Size = New Size(innerW, 76),
            .BackColor = CardBg, .AutoScroll = True
        }
        AddHandler itemsHost.Paint,
            Sub(sender As Object, ev As PaintEventArgs)
                Using pen As New Pen(DividerClr, 1.0F)
                    ev.Graphics.DrawRectangle(pen, 0, 0, itemsHost.Width - 1, itemsHost.Height - 1)
                End Using
            End Sub
        y = itemsHost.Bottom + 12

        ' ── Invoice ──────────────────────────────────────────────────────────
        Dim capInv As Label = MakeCaption("SUPPLIER'S INVOICE / RECEIPT NO.  *", PAD, y, colW)
        Dim capInvDate As Label = MakeCaption("INVOICE DATE  *", col2X, y, colW)
        y += 18
        Dim txtInvoice As New TextBox With {
            .Font = New Font("Segoe UI", 10.5F), .BorderStyle = BorderStyle.None,
            .BackColor = FieldBg, .ForeColor = TitleClr, .MaxLength = 60,
            .Location = New Point(12, 11), .Width = colW - 24
        }
        Dim fldInvoice As Panel = MakeField(txtInvoice, PAD, y, colW, 40)
        Dim dtInvoice As New DateTimePicker With {
            .Font = New Font("Segoe UI", 10.5F), .Format = DateTimePickerFormat.Custom,
            .CustomFormat = "MMM d, yyyy", .Location = New Point(10, 9),
            .Width = colW - 20, .Value = DateTime.Today, .MaxDate = DateTime.Today
        }
        Dim fldInvDate As Panel = MakeField(dtInvoice, col2X, y, colW, 40)
        y += 40 + 10

        ' ── Amount + date paid ───────────────────────────────────────────────
        Dim capAmount As Label = MakeCaption("AMOUNT PAID  *", PAD, y, colW)
        Dim capDate As Label = MakeCaption("PAYMENT DATE  *", col2X, y, colW)
        y += 18
        Dim txtAmount As New TextBox With {
            .Font = New Font("Segoe UI", 11.0F), .BorderStyle = BorderStyle.None,
            .BackColor = FieldBg, .ForeColor = TitleClr,
            .Location = New Point(30, 10), .Width = colW - 42
        }
        Dim fldAmount As Panel = MakeField(txtAmount, PAD, y, colW, 40)
        fldAmount.Controls.Add(New Label With {
            .Text = Peso, .Font = New Font("Segoe UI", 11.0F), .ForeColor = FaintClr,
            .AutoSize = True, .Location = New Point(12, 10), .BackColor = Color.Transparent
        })
        ' Money can be handed over on Friday and typed in on Monday, so the date
        ' is editable rather than pinned to today. Future dates are refused.
        Dim dtPaid As New DateTimePicker With {
            .Font = New Font("Segoe UI", 10.5F), .Format = DateTimePickerFormat.Custom,
            .CustomFormat = "MMM d, yyyy", .Location = New Point(10, 9),
            .Width = colW - 20, .Value = DateTime.Today, .MaxDate = DateTime.Today
        }
        Dim fldDate As Panel = MakeField(dtPaid, col2X, y, colW, 40)
        y += 40 + 10

        ' ── Reference + remarks ──────────────────────────────────────────────
        Dim capRef As Label = MakeCaption("REFERENCE NO.  (OPTIONAL)", PAD, y, colW)
        Dim capRemarks As Label = MakeCaption("REMARKS", col2X, y, colW - 60)
        Dim lblRemCount As New Label With {
            .Text = "0/200", .Font = New Font("Segoe UI", 8.5F), .ForeColor = FaintClr,
            .AutoSize = False, .Size = New Size(60, 16), .TextAlign = ContentAlignment.MiddleRight,
            .Location = New Point(col2X + colW - 60, y), .BackColor = Color.Transparent
        }
        y += 18
        Dim txtRef As New TextBox With {
            .Font = New Font("Segoe UI", 10.5F), .BorderStyle = BorderStyle.None,
            .BackColor = FieldBg, .ForeColor = TitleClr, .MaxLength = 60,
            .Location = New Point(12, 11), .Width = colW - 24
        }
        Dim fldRef As Panel = MakeField(txtRef, PAD, y, colW, 40)
        Dim txtRemarks As New TextBox With {
            .Font = New Font("Segoe UI", 10.0F), .BorderStyle = BorderStyle.None,
            .BackColor = FieldBg, .ForeColor = TitleClr, .MaxLength = 200,
            .Location = New Point(12, 8), .Size = New Size(colW - 24, 24)
        }
        Dim fldRemarks As Panel = MakeField(txtRemarks, col2X, y, colW, 40)
        AddHandler txtRemarks.TextChanged, Sub() lblRemCount.Text = txtRemarks.TextLength & "/200"
        y += 40 + 8

        ' Live result of this payment: what the order's status will become.
        Dim lblResult As New Label With {
            .Text = "", .Font = New Font("Segoe UI Semibold", 9.5F),
            .ForeColor = FaintClr, .AutoSize = False,
            .Location = New Point(PAD + 2, y), .Size = New Size(innerW - 4, 20),
            .TextAlign = ContentAlignment.MiddleLeft, .BackColor = Color.Transparent
        }
        y += 22
        ' A short payment is normally Partial. When the supplier has agreed to
        ' take less (discount, short delivery) this closes the order instead.
        Dim chkFinal As New CheckBox With {
            .Text = "Final payment: the supplier accepted less, so mark this order as Paid",
            .Font = New Font("Segoe UI", 9.5F), .ForeColor = BodyClr, .AutoSize = True,
            .Location = New Point(PAD, y), .BackColor = Color.Transparent, .Visible = False,
            .Cursor = Cursors.Hand
        }
        y += 26

        ' ── State + recompute ────────────────────────────────────────────────
        Dim current As SupplierPayments.PayablePo = Nothing
        Dim parsedAmount As Decimal = 0D
        Dim amountValid As Boolean = False

        Dim recompute As Action =
            Sub()
                chkFinal.Visible = False
                If current Is Nothing Then
                    amountValid = False
                    lblResult.ForeColor = FaintClr
                    lblResult.Text = "Choose the purchase order being paid."
                    Return
                End If
                Dim raw As String = txtAmount.Text.Trim().Replace(Peso, "").Replace(",", "")
                Dim val As Decimal
                If Not Decimal.TryParse(raw, val) OrElse val <= 0D Then
                    amountValid = False
                    lblResult.ForeColor = DangerClr
                    lblResult.Text = "Enter the amount paid (more than " & Peso & "0.00)."
                    Return
                End If
                parsedAmount = Math.Round(val, 2)
                If parsedAmount > current.Balance Then
                    amountValid = False
                    lblResult.ForeColor = DangerClr
                    lblResult.Text = "More than the balance of " & Peso & current.Balance.ToString("N2") &
                                     ". An order cannot be overpaid."
                    Return
                End If
                amountValid = True
                If parsedAmount = current.Balance Then
                    lblResult.ForeColor = OkClr
                    lblResult.Text = ChrW(10003) & "  Pays the balance in full. The order will be Paid."
                Else
                    chkFinal.Visible = True
                    Dim left As Decimal = current.Balance - parsedAmount
                    If chkFinal.Checked Then
                        lblResult.ForeColor = WarnClr
                        lblResult.Text = "The order will be Paid; " & Peso & left.ToString("N2") &
                                         " is not paid. Say why in remarks."
                    Else
                        lblResult.ForeColor = WarnClr
                        lblResult.Text = "Part payment. " & Peso & left.ToString("N2") &
                                         " will still be owed; the order will be Partial."
                    End If
                End If
            End Sub

        Dim showItems As Action(Of Integer) =
            Sub(poId As Integer)
                itemsHost.SuspendLayout()
                For Each c As Control In itemsHost.Controls.Cast(Of Control).ToList()
                    c.Dispose()
                Next
                Dim items As List(Of ItemLine)
                Try
                    items = LoadItems(poId)
                Catch
                    items = New List(Of ItemLine)
                End Try
                If items.Count = 0 Then
                    itemsHost.Controls.Add(New Label With {
                        .Text = "No items on this order.", .Font = New Font("Segoe UI", 9.5F),
                        .ForeColor = FaintClr, .AutoSize = True, .Location = New Point(12, 10),
                        .BackColor = Color.Transparent
                    })
                End If
                Dim rowW As Integer = innerW - 22
                For i As Integer = 0 To items.Count - 1
                    Dim it As ItemLine = items(i)
                    Dim top As Integer = 4 + i * 24
                    itemsHost.Controls.Add(New Label With {
                        .Text = it.Name, .Font = New Font("Segoe UI Semibold", 9.5F), .ForeColor = TitleClr,
                        .AutoSize = False, .AutoEllipsis = True, .Size = New Size(rowW - 300, 20),
                        .Location = New Point(10, top), .BackColor = Color.Transparent
                    })
                    itemsHost.Controls.Add(New Label With {
                        .Text = it.Qty.ToString("N0") & " " & ChrW(215) & " " & Peso & it.UnitCost.ToString("N2"),
                        .Font = New Font("Segoe UI", 9.5F), .ForeColor = BodyClr,
                        .AutoSize = False, .Size = New Size(170, 20), .TextAlign = ContentAlignment.MiddleRight,
                        .Location = New Point(rowW - 290, top), .BackColor = Color.Transparent
                    })
                    itemsHost.Controls.Add(New Label With {
                        .Text = Peso & it.LineCost.ToString("N2"),
                        .Font = New Font("Segoe UI Semibold", 9.5F), .ForeColor = TitleClr,
                        .AutoSize = False, .Size = New Size(110, 20), .TextAlign = ContentAlignment.MiddleRight,
                        .Location = New Point(rowW - 112, top), .BackColor = Color.Transparent
                    })
                Next
                itemsHost.ResumeLayout()
            End Sub

        AddHandler cboPo.SelectedIndexChanged,
            Sub()
                If cboPo.SelectedIndex < 0 Then Return
                current = choices(cboPo.SelectedIndex)
                factVals(0).Text = Peso & current.Total.ToString("N2")
                factVals(1).Text = Peso & current.Paid.ToString("N2")
                factVals(2).Text = Peso & current.Balance.ToString("N2")
                factVals(3).Text = current.Status
                factVals(3).ForeColor = SupplierPayments.StatusColors(current.Status).fore
                showItems(current.PoId)
                txtAmount.Text = current.Balance.ToString("0.00")
                chkFinal.Checked = False
                recompute()
            End Sub
        AddHandler txtAmount.TextChanged, Sub() recompute()
        AddHandler chkFinal.CheckedChanged, Sub() recompute()

        ' ── Buttons ──────────────────────────────────────────────────────────
        Dim dividerY As Integer = y + 4
        Dim btnRowTop As Integer = dividerY + 14
        Const BTN_H As Integer = 38
        Const BTN_W As Integer = 130
        Const GAP As Integer = 8

        Dim saved As Boolean = False
        Dim btnCancel As Button = MakeButton("Cancel", False,
            New Point(CARD_W - PAD - BTN_W * 2 - GAP, btnRowTop), New Size(BTN_W, BTN_H))
        Dim btnSave As Button = MakeButton("Save Payment", True,
            New Point(CARD_W - PAD - BTN_W, btnRowTop), New Size(BTN_W, BTN_H))

        AddHandler btnCancel.Click,
            Sub()
                f.DialogResult = DialogResult.Cancel
                f.Close()
            End Sub

        AddHandler btnSave.Click,
            Sub()
                recompute()
                If current Is Nothing Then
                    AppModal.Show("Choose the purchase order being paid.", "Purchase Order Required",
                                  MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    cboPo.Focus()
                    Return
                End If
                If txtInvoice.Text.Trim().Length = 0 Then
                    AppModal.Show("Enter the supplier's invoice or receipt number.", "Invoice Required",
                                  MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    txtInvoice.Focus()
                    Return
                End If
                If Not amountValid Then
                    AppModal.Show(lblResult.Text, "Check the Amount",
                                  MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    txtAmount.Focus()
                    Return
                End If
                Dim isFinal As Boolean = chkFinal.Visible AndAlso chkFinal.Checked
                ' Closing an order on less than its balance is exactly what an
                ' audit asks about, so it cannot be saved unexplained.
                If isFinal AndAlso txtRemarks.Text.Trim().Length = 0 Then
                    AppModal.Show("Say in remarks why the supplier accepted less " &
                                  "(discount, short delivery, price change).",
                                  "Reason Required", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    txtRemarks.Focus()
                    Return
                End If

                Dim result = SavePayment(current, parsedAmount, isFinal,
                                         txtInvoice.Text.Trim(), dtInvoice.Value.Date,
                                         txtRef.Text.Trim(), dtPaid.Value.Date, txtRemarks.Text.Trim())
                If result.paymentId > 0 Then
                    saved = True
                    Dim msg As String =
                        "Payment " & SupplierPayments.PaymentNo(result.paymentId) & " saved: " &
                        Peso & parsedAmount.ToString("N2") & " for " & current.PoNo & "." &
                        Environment.NewLine & Environment.NewLine &
                        If(result.nowPaid,
                           current.PoNo & " is now fully Paid.",
                           current.PoNo & " is Partial: " & Peso &
                           (current.Balance - parsedAmount).ToString("N2") & " still owed.")
                    AppModal.Show(msg, "Payment Saved", MessageBoxButtons.OK, MessageBoxIcon.Information)
                    f.DialogResult = DialogResult.OK
                    f.Close()
                End If
            End Sub

        f.Controls.AddRange({lblTitle, lblBody, capPo, fldPo, facts,
                             capItems, itemsHost,
                             capInv, capInvDate, fldInvoice, fldInvDate,
                             capAmount, capDate, fldAmount, fldDate,
                             capRef, capRemarks, lblRemCount, fldRef, fldRemarks,
                             lblResult, chkFinal,
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

        AddHandler f.KeyDown,
            Sub(s As Object, e As KeyEventArgs)
                If e.KeyCode = Keys.Escape Then
                    f.DialogResult = DialogResult.Cancel
                    f.Close()
                End If
            End Sub

        If choices.Count = 1 Then
            cboPo.SelectedIndex = 0
        Else
            recompute()
        End If
        AddHandler f.Shown,
            Sub()
                If current Is Nothing Then cboPo.Focus() Else txtInvoice.Focus()
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

    Private Function LoadItems(poId As Integer) As List(Of ItemLine)
        Dim result As New List(Of ItemLine)
        Using conn = Db.GetConnection()
            conn.Open()
            Using cmd As New MySqlCommand(
                "SELECT p.name, pi.qty, pi.unit_cost, pi.line_cost FROM po_items pi " &
                "JOIN products p ON p.product_id = pi.product_id WHERE pi.po_id = @id " &
                "ORDER BY pi.po_item_id", conn)
                cmd.Parameters.AddWithValue("@id", poId)
                Using rdr = cmd.ExecuteReader()
                    While rdr.Read()
                        result.Add(New ItemLine With {
                            .Name = rdr("name").ToString(),
                            .Qty = Convert.ToInt32(rdr("qty")),
                            .UnitCost = Convert.ToDecimal(rdr("unit_cost")),
                            .LineCost = Convert.ToDecimal(rdr("line_cost"))
                        })
                    End While
                End Using
            End Using
        End Using
        Return result
    End Function

    ' =========================================================================
    '  SAVE
    '
    '  One transaction: the order is locked, the balance re-read (someone may
    '  have paid it while this form was open), the payment written, and the
    '  order stamped Paid (paid_at) once nothing is owed or the payment was
    '  marked final. Returns the new payment id (0 when nothing was saved).
    ' =========================================================================
    Private Function SavePayment(po As SupplierPayments.PayablePo, amount As Decimal, isFinal As Boolean,
                                 invoiceNo As String, invoiceDate As Date,
                                 referenceNo As String, paidOn As Date,
                                 remarks As String) As (paymentId As Integer, nowPaid As Boolean)
        If Not Permissions.Require(Permissions.ManagePayables, "record supplier payments") Then Return (0, False)
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using tx = conn.BeginTransaction()
                    Dim total As Decimal = 0D
                    Dim alreadyPaid As Boolean = False
                    Using cmd As New MySqlCommand(
                        "SELECT total_cost, (paid_at IS NOT NULL) AS is_paid FROM purchase_orders " &
                        "WHERE po_id = @po FOR UPDATE", conn, tx)
                        cmd.Parameters.AddWithValue("@po", po.PoId)
                        Using rdr = cmd.ExecuteReader()
                            If rdr.Read() Then
                                total = Convert.ToDecimal(rdr("total_cost"))
                                alreadyPaid = Convert.ToInt32(rdr("is_paid")) = 1
                            End If
                        End Using
                    End Using
                    Dim paidBefore As Decimal
                    Using cmd As New MySqlCommand(
                        "SELECT COALESCE(SUM(amount_paid), 0) FROM supplier_payments WHERE po_id = @po", conn, tx)
                        cmd.Parameters.AddWithValue("@po", po.PoId)
                        paidBefore = Convert.ToDecimal(cmd.ExecuteScalar())
                    End Using
                    If alreadyPaid OrElse amount > total - paidBefore Then
                        tx.Rollback()
                        AppModal.Show(po.PoNo & " was paid by someone else while this form was open." &
                                      Environment.NewLine & "Nothing was saved. Open it again to see the new balance.",
                                      "Not Saved", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                        Return (0, False)
                    End If

                    ' Without migration 23 the invoice number goes in reference_no,
                    ' as payments were stored before.
                    Dim hasInvoice As Boolean = SupplierPayments.HasInvoiceColumns(conn, tx)
                    Dim refValue As String = If(hasInvoice, referenceNo,
                        invoiceNo & If(referenceNo.Length > 0, " / " & referenceNo, ""))
                    Dim paymentId As Integer
                    Using cmd As New MySqlCommand(
                        "INSERT INTO supplier_payments " &
                        "(po_id, amount_paid, po_total, reference_no, method, paid_on, remarks, recorded_by, recorded_at" &
                        If(hasInvoice, ", invoice_no, invoice_date", "") & ") " &
                        "VALUES (@po, @amt, @tot, @ref, @mth, @on, @rem, @by, NOW()" &
                        If(hasInvoice, ", @inv, @invd", "") & ")", conn, tx)
                        cmd.Parameters.AddWithValue("@po", po.PoId)
                        cmd.Parameters.AddWithValue("@amt", amount)
                        cmd.Parameters.AddWithValue("@tot", total)
                        cmd.Parameters.AddWithValue("@ref", If(refValue.Length = 0, CObj(DBNull.Value), Left(refValue, 60)))
                        cmd.Parameters.AddWithValue("@mth", PAY_METHOD)
                        cmd.Parameters.AddWithValue("@on", paidOn)
                        cmd.Parameters.AddWithValue("@rem", If(remarks.Length = 0, CObj(DBNull.Value), remarks))
                        cmd.Parameters.AddWithValue("@by", Session.CurrentUserId)
                        If hasInvoice Then
                            cmd.Parameters.AddWithValue("@inv", invoiceNo)
                            cmd.Parameters.AddWithValue("@invd", invoiceDate)
                        End If
                        cmd.ExecuteNonQuery()
                        paymentId = CInt(cmd.LastInsertedId)
                    End Using

                    Dim nowPaid As Boolean = isFinal OrElse paidBefore + amount >= total
                    If nowPaid Then
                        Using cmd As New MySqlCommand(
                            "UPDATE purchase_orders SET paid_at = @on WHERE po_id = @po AND paid_at IS NULL", conn, tx)
                            cmd.Parameters.AddWithValue("@on", paidOn)
                            cmd.Parameters.AddWithValue("@po", po.PoId)
                            cmd.ExecuteNonQuery()
                        End Using
                    End If
                    tx.Commit()

                    ' Money leaving the business is the most audited event this
                    ' system records, so the amount, invoice and result all go in
                    ' the trail -- not just the PO number.
                    Dim owed As Decimal = total - paidBefore - amount
                    ActivityLog.Record("PO_PAID", "purchase_order", po.PoId,
                        SupplierPayments.PaymentNo(paymentId) & ": " & po.PoNo & " to " & po.Supplier & ", " &
                        Peso & amount.ToString("N2") & " " & PAY_METHOD & ", invoice " & invoiceNo &
                        If(nowPaid,
                           If(owed > 0D, " - final, " & Peso & owed.ToString("N2") & " not paid", " - fully paid"),
                           " - partial, " & Peso & owed.ToString("N2") & " still owed"))
                    Return (paymentId, nowPaid)
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not record the payment:" & Environment.NewLine & ErrorLog.ForUser(ex),
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return (0, False)
        End Try
    End Function

    Private Function Left(s As String, n As Integer) As String
        Return If(s.Length > n, s.Substring(0, n), s)
    End Function

End Module
