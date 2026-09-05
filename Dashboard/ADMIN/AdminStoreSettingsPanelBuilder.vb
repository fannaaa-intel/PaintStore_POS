Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports MySql.Data.MySqlClient

' =============================================================================
'  AdminStoreSettingsPanelBuilder  —  Admin ▸ Store Settings
'
'  Two stacked cards inside an AutoScroll host:
'    A) STORE INFO + TAX  : the single store_settings row, editable + Save
'    B) PAYMENT METHODS   : payment_methods CRUD (add / rename / remove)
'
'  Saving settings calls StoreSettings.Reload() so the POS picks up the new
'  VAT rate and receipt header without an app restart.
'
'  Deleting a payment method is blocked while any sale still references it —
'  the row is a foreign key target, and orphaning sales would corrupt history.
' =============================================================================
Public Class AdminStoreSettingsPanelBuilder

    ' ── PALETTE  (matches the other ADMIN panels) ────────────────────────────
    Private Shared ReadOnly MintDark As Color = Color.FromArgb(10, 79, 66)
    Private Shared ReadOnly MintPrimary As Color = Color.FromArgb(0, 168, 142)
    Private Shared ReadOnly MintBg As Color = Color.FromArgb(244, 248, 246)
    Private Shared ReadOnly CardBg As Color = Color.White
    Private Shared ReadOnly SubText As Color = Color.FromArgb(90, 114, 107)
    Private Shared ReadOnly FaintText As Color = Color.FromArgb(150, 165, 160)
    Private Shared ReadOnly RowDivider As Color = Color.FromArgb(238, 242, 240)
    Private Shared ReadOnly RowHover As Color = Color.FromArgb(224, 242, 235)
    Private Shared ReadOnly AccentRed As Color = Color.FromArgb(210, 60, 60)
    Private Shared ReadOnly FieldBg As Color = Color.FromArgb(244, 248, 246)
    Private Shared ReadOnly FieldBorder As Color = Color.FromArgb(210, 225, 220)

    Private Const SIDE_PAD As Integer = 24
    Private Const FIELD_H As Integer = 44
    Private Const BLOCK_H As Integer = 20 + FIELD_H + 14
    Private Const PM_ROW_H As Integer = 56
    Private Const PAD_TOP As Integer = 4
    Private Const PAD_BOTTOM As Integer = 12

    ' ── DATA MODEL ────────────────────────────────────────────────────────────
    Private Class PayMethod
        Public MethodId As Integer
        Public Name As String
        Public Type As String = "Reference"
        Public IsActive As Boolean = True
        Public Note As String = ""
    End Class

    ' ── STATE ─────────────────────────────────────────────────────────────────
    Private Shared _container As Panel

    Private Shared _txtName As TextBox
    Private Shared _txtTagline As TextBox
    Private Shared _txtAddress As TextBox
    Private Shared _txtContact As TextBox
    Private Shared _txtTin As TextBox
    Private Shared _txtVat As TextBox
    Private Shared _chkVatIncl As CheckBox

    Private Shared _methods As New List(Of PayMethod)
    Private Shared _settingsFailed As Boolean = False

    ' Loaded values for the form
    Private Shared _sName, _sTagline, _sAddress, _sContact, _sTin As String
    Private Shared _sVatRate As Decimal
    Private Shared _sVatIncl As Boolean

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Shared Sub LoadStoreSettings(container As Panel)
        _container = container
        LoadSettingsRow()
        LoadMethods()
        Render()
    End Sub

    ' =========================================================================
    '  DATA
    ' =========================================================================
    Private Shared Sub LoadSettingsRow()
        _settingsFailed = False
        ' Defaults mirror StoreSettings' fallbacks.
        _sName = "PAINT STORE POS"
        _sTagline = "Sales Receipt"
        _sAddress = "" : _sContact = "" : _sTin = ""
        _sVatRate = 12D
        _sVatIncl = True

        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT store_name, store_tagline, store_address, store_contact, " &
                    "store_tin, vat_rate, vat_inclusive " &
                    "FROM store_settings WHERE setting_id = @id LIMIT 1", conn)
                    cmd.Parameters.AddWithValue("@id", 1)
                    Using rdr = cmd.ExecuteReader()
                        If rdr.Read() Then
                            _sName = SafeStr(rdr("store_name"))
                            _sTagline = SafeStr(rdr("store_tagline"))
                            _sAddress = SafeStr(rdr("store_address"))
                            _sContact = SafeStr(rdr("store_contact"))
                            _sTin = SafeStr(rdr("store_tin"))
                            If Not IsDBNull(rdr("vat_rate")) Then _sVatRate = Convert.ToDecimal(rdr("vat_rate"))
                            If Not IsDBNull(rdr("vat_inclusive")) Then _sVatIncl = Convert.ToBoolean(rdr("vat_inclusive"))
                        End If
                    End Using
                End Using
            End Using
        Catch ex As Exception
            _settingsFailed = True
            AppModal.Show("Could not load store settings: " & ex.Message &
                          vbCrLf & vbCrLf &
                          "The POS is still running on its built-in defaults " &
                          "(12% VAT-inclusive), so sales are unaffected.",
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    Private Shared Sub LoadMethods()
        ' Delegates to the shared PaymentMethods module (all methods, including
        ' inactive ones, since this screen manages them). Type/active/note come
        ' from the record, or a safe fallback on an un-migrated database.
        _methods.Clear()
        For Each m In PaymentMethods.LoadAll(activeOnly:=False)
            _methods.Add(New PayMethod With {
                .MethodId = m.MethodId, .Name = m.Name,
                .Type = m.Type, .IsActive = m.IsActive, .Note = m.Note
            })
        Next
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
        _container.BackColor = MintBg

        Dim host As New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = MintBg,
            .AutoScroll = True,
            .Padding = New Padding(SIDE_PAD, 12, SIDE_PAD, SIDE_PAD)
        }

        ' Docked Top children stack in REVERSE add order, so the trailing
        ' spacer is added first to land below the last card.
        host.Controls.Add(Spacer())
        host.Controls.Add(BuildPaymentCard())
        host.Controls.Add(Spacer())
        host.Controls.Add(BuildInfoCard())

        Dim topRegion As Panel = BuildTopRegion()
        topRegion.Dock = DockStyle.Top

        _container.Controls.Add(host)
        _container.Controls.Add(topRegion)
        _container.ResumeLayout()
    End Sub

    Private Shared Function Spacer() As Panel
        Return New Panel With {.Dock = DockStyle.Top, .Height = 16, .BackColor = MintBg}
    End Function

    ' =========================================================================
    '  TITLE BAR  —  72px
    ' =========================================================================
    Private Shared Function BuildTopRegion() As Panel
        Dim region As New Panel With {.BackColor = MintBg, .AutoSize = False, .Height = 72}

        Dim titleBar As New Panel With {
            .Height = 72, .Dock = DockStyle.Top, .BackColor = Color.Transparent
        }

        Dim lblTitle As New Label With {
            .Text = "Store Settings",
            .Font = New Font("Segoe UI", 28, FontStyle.Bold),
            .ForeColor = MintDark,
            .AutoSize = True,
            .Location = New Point(SIDE_PAD, 16)
        }

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
        AddHandler btnRefresh.Paint, AddressOf PaintPillButton
        AddHandler btnRefresh.MouseEnter, Sub()
                                              btnRefresh.BackColor = Color.FromArgb(210, 240, 235)
                                              btnRefresh.Invalidate()
                                          End Sub
        AddHandler btnRefresh.MouseLeave, Sub()
                                              btnRefresh.BackColor = Color.FromArgb(220, 235, 232)
                                              btnRefresh.Invalidate()
                                          End Sub
        AddHandler btnRefresh.Click, Sub() LoadStoreSettings(_container)

        AddHandler titleBar.Resize,
            Sub()
                btnRefresh.Left = titleBar.Width - btnRefresh.Width - SIDE_PAD
                btnRefresh.Top = (titleBar.Height - btnRefresh.Height) \ 2
            End Sub

        titleBar.Controls.Add(lblTitle)
        titleBar.Controls.Add(btnRefresh)
        region.Controls.Add(titleBar)
        Return region
    End Function

    Private Shared Sub PaintPillButton(s As Object, e As PaintEventArgs)
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

    ' =========================================================================
    '  SECTION A — STORE INFO + TAX
    ' =========================================================================
    Private Shared Function BuildInfoCard() As Panel
        ' header 56 + 4 field rows + save row 60 + bottom pad
        Dim cardH As Integer = 56 + BLOCK_H * 4 + 60 + 8

        Dim wrap As New Panel With {
            .Dock = DockStyle.Top, .Height = cardH, .BackColor = MintBg
        }

        Dim card As New BufferedPanel With {.Dock = DockStyle.Fill, .BackColor = CardBg}
        AddHandler card.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)

        ' "&&" renders as one ampersand. WinForms treats a lone & in a Label as a
        ' mnemonic prefix: it swallows the & and underlines the next character,
        ' so this title read "Store Information Tax" on screen.
        card.Controls.Add(New Label With {
            .Text = "Store Information && Tax",
            .Font = New Font("Segoe UI Semibold", 13),
            .ForeColor = MintDark,
            .AutoSize = True,
            .Location = New Point(24, 18),
            .BackColor = Color.Transparent
        })

        _txtName = MakeTextBox(_sName)
        _txtTagline = MakeTextBox(_sTagline)
        _txtAddress = MakeTextBox(_sAddress)
        _txtContact = MakeTextBox(_sContact)
        _txtTin = MakeTextBox(_sTin)
        _txtVat = MakeTextBox(_sVatRate.ToString("0.##"))

        Dim fName = AddField(card, "Store name", _txtName)
        Dim fTagline = AddField(card, "Receipt tagline", _txtTagline)
        Dim fAddress = AddField(card, "Address", _txtAddress)
        Dim fContact = AddField(card, "Contact", _txtContact)
        Dim fTin = AddField(card, "TIN", _txtTin)
        Dim fVat = AddField(card, "VAT rate (%)", _txtVat)

        _chkVatIncl = New CheckBox With {
            .Text = "Prices already include VAT",
            .Font = New Font("Segoe UI", 10),
            .ForeColor = SubText,
            .AutoSize = True,
            .Checked = _sVatIncl,
            .BackColor = Color.Transparent,
            .Cursor = Cursors.Hand
        }
        card.Controls.Add(_chkVatIncl)

        Dim lblVatHint As New Label With {
            .Text = "",
            .Font = New Font("Segoe UI", 9),
            .ForeColor = FaintText,
            .AutoSize = True,
            .BackColor = Color.Transparent
        }
        card.Controls.Add(lblVatHint)

        ' Live preview of the formula so the effect on sales is never a guess.
        Dim refreshHint As Action =
            Sub()
                Dim r As Decimal
                If Not Decimal.TryParse(_txtVat.Text.Trim(), r) Then
                    lblVatHint.Text = "Enter a number between 0 and 100."
                    Return
                End If
                If _chkVatIncl.Checked Then
                    lblVatHint.Text = $"VAT = total × {r:0.##} ÷ {(100D + r):0.##}  (extracted from the price)"
                Else
                    lblVatHint.Text = $"VAT = total × {r:0.##} ÷ 100  (added on top)"
                End If
            End Sub
        AddHandler _txtVat.TextChanged, Sub() refreshHint()
        AddHandler _chkVatIncl.CheckedChanged, Sub() refreshHint()
        refreshHint()

        Dim btnSave As Button = MakePrimaryButton("Save Settings", 140)
        AddHandler btnSave.Click, Sub() SaveSettings()
        card.Controls.Add(btnSave)

        AddHandler card.Resize,
            Sub()
                Dim pad As Integer = 24
                Dim gap As Integer = 16
                Dim fullW As Integer = Math.Max(120, card.Width - pad * 2)
                Dim colW As Integer = Math.Max(100, (fullW - gap) \ 2)
                Dim rightX As Integer = pad + colW + gap

                Dim y As Integer = 56
                PlaceField(fName, pad, y, colW)
                PlaceField(fTagline, rightX, y, colW)
                y += BLOCK_H
                PlaceField(fAddress, pad, y, fullW)
                y += BLOCK_H
                PlaceField(fContact, pad, y, colW)
                PlaceField(fTin, rightX, y, colW)
                y += BLOCK_H
                PlaceField(fVat, pad, y, colW)
                _chkVatIncl.Left = rightX
                _chkVatIncl.Top = y + 20 + (FIELD_H - _chkVatIncl.Height) \ 2
                lblVatHint.Left = rightX
                lblVatHint.Top = _chkVatIncl.Bottom + 4
                y += BLOCK_H

                btnSave.Left = card.Width - pad - btnSave.Width
                btnSave.Top = y + 4
            End Sub

        wrap.Controls.Add(card)
        Return wrap
    End Function

    Private Shared Sub SaveSettings()
        Dim storeName As String = _txtName.Text.Trim()
        Dim tagline As String = _txtTagline.Text.Trim()

        If storeName = "" Then
            AppModal.Show("Store name is required.", "Missing Information",
                          MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If
        If tagline = "" Then
            AppModal.Show("Receipt tagline is required.", "Missing Information",
                          MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        Dim rate As Decimal
        If Not Decimal.TryParse(_txtVat.Text.Trim(), rate) Then
            AppModal.Show("VAT rate must be a number.", "Invalid VAT Rate",
                          MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If
        If rate < 0D OrElse rate > 100D Then
            AppModal.Show("VAT rate must be between 0 and 100.", "Invalid VAT Rate",
                          MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        Try
            Using conn = Db.GetConnection()
                conn.Open()
                ' store_settings is a single-row table (CHECK setting_id = 1) but
                ' nothing creates that row, so on a fresh database a plain UPDATE
                ' matched nothing and the save silently did nothing while still
                ' reporting success. INSERT .. ON DUPLICATE KEY UPDATE creates the
                ' row the first time and updates it every time after.
                Using cmd As New MySqlCommand(
                    "INSERT INTO store_settings " &
                    "(setting_id, store_name, store_tagline, store_address, " &
                    " store_contact, store_tin, vat_rate, vat_inclusive) " &
                    "VALUES (@id, @name, @tag, @addr, @contact, @tin, @rate, @incl) " &
                    "ON DUPLICATE KEY UPDATE " &
                    "store_name=@name, store_tagline=@tag, store_address=@addr, " &
                    "store_contact=@contact, store_tin=@tin, " &
                    "vat_rate=@rate, vat_inclusive=@incl", conn)
                    cmd.Parameters.AddWithValue("@name", storeName)
                    cmd.Parameters.AddWithValue("@tag", tagline)
                    cmd.Parameters.AddWithValue("@addr", NullIfBlank(_txtAddress.Text.Trim()))
                    cmd.Parameters.AddWithValue("@contact", NullIfBlank(_txtContact.Text.Trim()))
                    cmd.Parameters.AddWithValue("@tin", NullIfBlank(_txtTin.Text.Trim()))
                    cmd.Parameters.AddWithValue("@rate", rate)
                    cmd.Parameters.AddWithValue("@incl", If(_chkVatIncl.Checked, 1, 0))
                    cmd.Parameters.AddWithValue("@id", 1)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not save settings: " & ex.Message, "Database Error",
                          MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try

        ' Push the new values into the live POS cache.
        StoreSettings.Reload()
        ActivityLog.Record("SETTINGS_UPDATE", "settings", 1, "Updated store settings")

        AppModal.Show("Store settings saved. New sales will use a " &
                      rate.ToString("0.##") & "% " &
                      If(_chkVatIncl.Checked, "VAT-inclusive", "VAT-exclusive") & " rate.",
                      "Settings Saved", MessageBoxButtons.OK, MessageBoxIcon.Information)

        LoadStoreSettings(_container)
    End Sub

    Private Shared Function NullIfBlank(s As String) As Object
        If String.IsNullOrEmpty(s) Then Return DBNull.Value
        Return s
    End Function

    ' =========================================================================
    '  SECTION B — PAYMENT METHODS
    ' =========================================================================
    Private Shared Function BuildPaymentCard() As Panel
        Dim contentH As Integer = If(_methods.Count = 0, 80, _methods.Count * PM_ROW_H)
        Dim cardH As Integer = 64 + PAD_TOP + contentH + PAD_BOTTOM + 8

        Dim wrap As New Panel With {
            .Dock = DockStyle.Top, .Height = cardH, .BackColor = MintBg
        }

        Dim card As New BufferedPanel With {.Dock = DockStyle.Fill, .BackColor = CardBg}
        AddHandler card.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)

        Dim header As New Panel With {
            .Dock = DockStyle.Top, .Height = 64, .BackColor = Color.Transparent
        }
        header.Controls.Add(New Label With {
            .Text = "Payment Methods",
            .Font = New Font("Segoe UI Semibold", 13),
            .ForeColor = MintDark,
            .AutoSize = True,
            .Location = New Point(24, 20),
            .BackColor = Color.Transparent
        })
        AddHandler header.Paint,
            Sub(s, e)
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 22, header.Height - 1, header.Width - 22, header.Height - 1)
                End Using
            End Sub

        Dim btnAdd As Button = MakePrimaryButton("+  Add Method", 130)
        btnAdd.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        AddHandler btnAdd.Click, Sub() AddMethod()
        header.Controls.Add(btnAdd)
        AddHandler header.Resize,
            Sub()
                btnAdd.Left = header.Width - btnAdd.Width - 24
                btnAdd.Top = (header.Height - btnAdd.Height) \ 2
            End Sub

        Dim bottomPad As New Panel With {
            .Dock = DockStyle.Bottom, .Height = PAD_BOTTOM, .BackColor = Color.Transparent
        }

        Dim list As New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = Color.Transparent,
            .AutoScroll = True,
            .Padding = New Padding(0)
        }

        If _methods.Count = 0 Then
            list.Controls.Add(New Label With {
                .Text = "No payment methods defined.",
                .Font = New Font("Segoe UI", 10.5F),
                .ForeColor = FaintText,
                .AutoSize = True,
                .Location = New Point(24, 26),
                .BackColor = Color.Transparent
            })
        Else
            Dim y As Integer = PAD_TOP
            For Each m As PayMethod In _methods
                Dim row As Panel = BuildMethodRow(m, PM_ROW_H)
                row.Location = New Point(12, y)
                row.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
                list.Controls.Add(row)
                row.Width = list.ClientSize.Width - 24
                y += PM_ROW_H
            Next
            AddHandler list.Resize,
                Sub()
                    For Each c As Control In list.Controls
                        c.Width = list.ClientSize.Width - 24
                    Next
                End Sub
        End If

        card.Controls.Add(bottomPad)
        card.Controls.Add(header)
        card.Controls.Add(list)
        header.BringToFront()
        list.BringToFront()
        list.AutoScrollMinSize = New Size(0, 1)

        wrap.Controls.Add(card)
        Return wrap
    End Function

    Private Shared Function BuildMethodRow(m As PayMethod, height As Integer) As Panel
        Dim p As New BufferedPanel With {.Height = height, .BackColor = Color.Transparent}

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
                    e.Graphics.DrawLine(pen, 10, p.Height - 1, p.Width - 10, p.Height - 1)
                End Using
            End Sub

        Dim lblName As New Label With {
            .Text = If(m.Name = "", "—", m.Name),
            .Font = New Font("Segoe UI", 11, FontStyle.Bold),
            .ForeColor = MintDark,
            .AutoSize = True, .BackColor = Color.Transparent
        }
        ' A sub-line under the name showing what the method IS: its type, and
        ' whether it is hidden from the till. Turns the row from a bare name
        ' into something an admin can read at a glance.
        Dim typeLabel As String = If(String.Equals(m.Type, "Cash", StringComparison.OrdinalIgnoreCase),
                                     "Cash - gives change", "Reference - needs a ref. no.")
        Dim subLine As String = typeLabel
        If Not m.IsActive Then subLine &= "   -   HIDDEN"
        If Not String.IsNullOrWhiteSpace(m.Note) Then subLine &= "   -   " & m.Note.Trim()
        Dim lblId As New Label With {
            .Text = subLine,
            .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = If(m.IsActive, FaintText, AccentRed),
            .AutoSize = True, .BackColor = Color.Transparent
        }

        Dim btnRename As Button = MakeActionButton("Edit", Color.FromArgb(220, 235, 232), MintDark)
        Dim btnRemove As Button = MakeActionButton("Remove", Color.FromArgb(255, 232, 232), AccentRed)
        AddHandler btnRename.Click, Sub() RenameMethod(m)
        AddHandler btnRemove.Click, Sub() RemoveMethod(m)

        p.Controls.AddRange({lblName, lblId, btnRename, btnRemove})

        AddHandler p.Resize,
            Sub()
                ' Name on top, the type/status sub-line beneath it - two stacked
                ' lines, vertically centred as a pair.
                Dim blockH As Integer = lblName.Height + 2 + lblId.Height
                Dim top0 As Integer = (height - blockH) \ 2
                lblName.Left = 22 : lblName.Top = top0
                lblId.Left = 22 : lblId.Top = lblName.Bottom + 2
                btnRemove.Left = p.Width - btnRemove.Width - 16
                btnRemove.Top = (height - btnRemove.Height) \ 2
                btnRename.Left = btnRemove.Left - btnRename.Width - 6
                btnRename.Top = btnRemove.Top
            End Sub

        PropagateHover(p)
        Return p
    End Function

    ' ── CRUD ─────────────────────────────────────────────────────────────────
    Private Shared Sub AddMethod()
        ' Full form: name, type, active, note. On an un-migrated database only
        ' the name is asked (showTypeActive:=False) and the extra columns are
        ' not written, so the old behaviour is preserved.
        Dim hasCols As Boolean = PaymentMethods.HasBehaviourColumns()
        Dim r As PaymentMethodModal.Result =
            PaymentMethodModal.Prompt(isEdit:=False, showTypeActive:=hasCols, seed:=Nothing)
        If r Is Nothing Then Return

        Dim name As String = r.Name.Trim()
        Dim nameErr As String = ValidateMethodName(name)
        If nameErr <> "" Then
            AppModal.Show(nameErr, "Add Payment Method", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        Dim newId As Integer = 0
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT COUNT(*) FROM payment_methods WHERE LOWER(name) = LOWER(@n)", conn)
                    cmd.Parameters.AddWithValue("@n", name)
                    If Convert.ToInt32(cmd.ExecuteScalar()) > 0 Then
                        AppModal.Show("A payment method named """ & name & """ already exists.",
                                      "Duplicate Name", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                        Return
                    End If
                End Using

                Dim sql As String = If(hasCols,
                    "INSERT INTO payment_methods (name, type, is_active, note) VALUES (@n, @t, @a, @note)",
                    "INSERT INTO payment_methods (name) VALUES (@n)")
                Using cmd As New MySqlCommand(sql, conn)
                    cmd.Parameters.AddWithValue("@n", name)
                    If hasCols Then
                        cmd.Parameters.AddWithValue("@t", r.Type)
                        cmd.Parameters.AddWithValue("@a", If(r.IsActive, 1, 0))
                        cmd.Parameters.AddWithValue("@note",
                            If(String.IsNullOrWhiteSpace(r.Note), CObj(DBNull.Value), CObj(r.Note.Trim())))
                    End If
                    cmd.ExecuteNonQuery()
                End Using
                Using cmd As New MySqlCommand("SELECT LAST_INSERT_ID()", conn)
                    newId = Convert.ToInt32(cmd.ExecuteScalar())
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not add the payment method: " & ex.Message, "Database Error",
                          MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try

        PaymentMethods.ResetSchemaCache()
        ActivityLog.Record("PAYMETHOD_ADD", "payment_method", newId, name)
        LoadStoreSettings(_container)
    End Sub

    ' Kept the name RenameMethod so the row wiring is unchanged, but it now opens
    ' the full editor rather than a name-only prompt.
    Private Shared Sub RenameMethod(m As PayMethod)
        Dim hasCols As Boolean = PaymentMethods.HasBehaviourColumns()
        Dim seed As New PaymentMethodModal.Result With {
            .Name = m.Name, .Type = m.Type, .IsActive = m.IsActive, .Note = m.Note
        }
        Dim r As PaymentMethodModal.Result =
            PaymentMethodModal.Prompt(isEdit:=True, showTypeActive:=hasCols, seed:=seed)
        If r Is Nothing Then Return

        Dim name As String = r.Name.Trim()
        Dim nameErr As String = ValidateMethodName(name)
        If nameErr <> "" Then
            AppModal.Show(nameErr, "Edit Payment Method", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT COUNT(*) FROM payment_methods " &
                    "WHERE LOWER(name) = LOWER(@n) AND method_id <> @id", conn)
                    cmd.Parameters.AddWithValue("@n", name)
                    cmd.Parameters.AddWithValue("@id", m.MethodId)
                    If Convert.ToInt32(cmd.ExecuteScalar()) > 0 Then
                        AppModal.Show("A payment method named """ & name & """ already exists.",
                                      "Duplicate Name", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                        Return
                    End If
                End Using

                Dim sql As String = If(hasCols,
                    "UPDATE payment_methods SET name=@n, type=@t, is_active=@a, note=@note WHERE method_id=@id",
                    "UPDATE payment_methods SET name=@n WHERE method_id=@id")
                Using cmd As New MySqlCommand(sql, conn)
                    cmd.Parameters.AddWithValue("@n", name)
                    If hasCols Then
                        cmd.Parameters.AddWithValue("@t", r.Type)
                        cmd.Parameters.AddWithValue("@a", If(r.IsActive, 1, 0))
                        cmd.Parameters.AddWithValue("@note",
                            If(String.IsNullOrWhiteSpace(r.Note), CObj(DBNull.Value), CObj(r.Note.Trim())))
                    End If
                    cmd.Parameters.AddWithValue("@id", m.MethodId)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not save the payment method: " & ex.Message, "Database Error",
                          MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try

        PaymentMethods.ResetSchemaCache()
        ActivityLog.Record("PAYMETHOD_EDIT", "payment_method", m.MethodId, name)
        LoadStoreSettings(_container)
    End Sub

    ' payment_methods.name is VARCHAR(30) UNIQUE. Checking the length here turns
    ' a raw MySQL truncation error — surfaced as an unactionable "Database
    ' Error" — into something the admin can actually fix.
    Private Shared Function ValidateMethodName(name As String) As String
        If String.IsNullOrWhiteSpace(name) Then Return "The name cannot be empty."
        If name.Length > 30 Then
            Return "Use 30 characters or fewer (that name is " &
                   name.Length.ToString() & ")."
        End If
        Return ""
    End Function

    Private Shared Sub RemoveMethod(m As PayMethod)
        ' A method referenced by sales cannot be deleted — removing it would
        ' orphan historical rows and break every sales report that joins on it.
        Dim inUse As Integer = 0
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT COUNT(*) FROM sales WHERE method_id = @id", conn)
                    cmd.Parameters.AddWithValue("@id", m.MethodId)
                    inUse = Convert.ToInt32(cmd.ExecuteScalar())
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not check whether the method is in use: " & ex.Message,
                          "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try

        If inUse > 0 Then
            AppModal.Show("""" & m.Name & """ is used by " & inUse.ToString("N0") &
                          If(inUse = 1, " sale", " sales") & " and cannot be removed." &
                          vbCrLf & vbCrLf &
                          "Deleting it would orphan those records.",
                          "Payment Method In Use",
                          MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        ' The POS tender list IS this table. Emptying it leaves the till with
        ' nothing to take payment with, and nothing else in the system warns
        ' about that.
        If _methods.Count <= 1 Then
            AppModal.Show("At least one payment method is required." & vbCrLf & vbCrLf &
                          "The POS builds its tender list from this table, so removing " &
                          "the last one would leave the till unable to complete a sale.",
                          "Cannot Remove", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        ' CashPanelBuilder finds the drawer's method with LIKE '%cash%'. With no
        ' such row the shift's cash total silently reads zero, so losing the last
        ' cash-like method deserves a deliberate confirmation rather than the
        ' generic one.
        Dim isCashLike As Boolean = m.Name.ToLowerInvariant().Contains("cash")
        Dim otherCashLike As Boolean = False
        For Each other In _methods
            If other.MethodId <> m.MethodId AndAlso
               other.Name.ToLowerInvariant().Contains("cash") Then
                otherCashLike = True
                Exit For
            End If
        Next

        Dim warn As String = "Remove the payment method """ & m.Name & """?"
        If isCashLike AndAlso Not otherCashLike Then
            warn &= vbCrLf & vbCrLf &
                    "This is the only method the cash drawer recognises. Without it " &
                    "the shift's cash total will read zero."
        End If

        If AppModal.Show(warn, "Confirm",
                         MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then
            Return
        End If

        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "DELETE FROM payment_methods WHERE method_id = @id", conn)
                    cmd.Parameters.AddWithValue("@id", m.MethodId)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        Catch ex As Exception
            AppModal.Show("Could not remove the payment method: " & ex.Message, "Database Error",
                          MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try

        ActivityLog.Record("PAYMETHOD_DELETE", "payment_method", m.MethodId, m.Name)
        LoadStoreSettings(_container)
    End Sub

    ' =========================================================================
    '  FIELD / BUTTON HELPERS
    ' =========================================================================
    Private Class FieldUI
        Public Lbl As Label
        Public Box As Panel
        Public Input As Control
    End Class

    Private Shared Function MakeTextBox(value As String) As TextBox
        Return New TextBox With {
            .Font = New Font("Segoe UI", 11.0F),
            .BorderStyle = BorderStyle.None,
            .BackColor = FieldBg,
            .ForeColor = Color.FromArgb(35, 50, 46),
            .Text = value
        }
    End Function

    Private Shared Function AddField(host As Control, labelText As String, input As Control) As FieldUI
        Dim lbl As New Label With {
            .Text = labelText,
            .Font = New Font("Segoe UI", 9.5F),
            .ForeColor = SubText,
            .AutoSize = True,
            .BackColor = Color.Transparent
        }
        Dim box As New Panel With {.Size = New Size(100, FIELD_H), .BackColor = Color.Transparent}

        Dim focused As Boolean = False
        AddHandler box.Paint,
            Sub(s, e)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect = box.ClientRectangle : rect.Inflate(-1, -1)
                Using br As New SolidBrush(FieldBg)
                    g.FillRoundedRectangle(br, rect, 9)
                End Using
                Using p As New Pen(If(focused, MintPrimary, FieldBorder), If(focused, 1.8F, 1.0F))
                    g.DrawRoundedRectangle(p, rect, 9)
                End Using
            End Sub
        AddHandler input.Enter, Sub()
                                    focused = True
                                    box.Invalidate()
                                End Sub
        AddHandler input.Leave, Sub()
                                    focused = False
                                    box.Invalidate()
                                End Sub

        box.Controls.Add(input)
        host.Controls.Add(lbl)
        host.Controls.Add(box)
        Return New FieldUI With {.Lbl = lbl, .Box = box, .Input = input}
    End Function

    Private Shared Sub PlaceField(f As FieldUI, x As Integer, y As Integer, w As Integer)
        f.Lbl.Location = New Point(x, y)
        f.Box.Location = New Point(x, y + 20)
        f.Box.Size = New Size(w, FIELD_H)
        f.Input.Location = New Point(14, (FIELD_H - f.Input.PreferredSize.Height) \ 2)
        f.Input.Width = Math.Max(40, w - 28)
    End Sub

    Private Shared Function MakePrimaryButton(text As String, width As Integer) As Button
        Dim b As New Button With {
            .Text = text,
            .Size = New Size(width, 38),
            .FlatStyle = FlatStyle.Flat,
            .Font = New Font("Segoe UI Semibold", 9.5F),
            .Cursor = Cursors.Hand,
            .BackColor = MintPrimary,
            .ForeColor = Color.White
        }
        b.FlatAppearance.BorderSize = 0
        AddHandler b.Paint,
            Sub(s, e)
                Dim bb = CType(s, Button)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Using bg As New SolidBrush(CardBg)
                    g.FillRectangle(bg, bb.ClientRectangle)
                End Using
                Dim rect = bb.ClientRectangle : rect.Inflate(-1, -1)
                Using br As New SolidBrush(bb.BackColor)
                    g.FillRoundedRectangle(br, rect, 8)
                End Using
                TextRenderer.DrawText(g, bb.Text, bb.Font, bb.ClientRectangle, bb.ForeColor,
                    TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub
        AddHandler b.MouseEnter, Sub()
                                     b.BackColor = MintDark
                                     b.Invalidate()
                                 End Sub
        AddHandler b.MouseLeave, Sub()
                                     b.BackColor = MintPrimary
                                     b.Invalidate()
                                 End Sub
        Return b
    End Function

    Private Shared Function MakeActionButton(text As String, backClr As Color, foreClr As Color) As Button
        Dim b As New Button With {
            .Text = text,
            .FlatStyle = FlatStyle.Flat,
            .Cursor = Cursors.Hand,
            .Size = New Size(84, 30),
            .Font = New Font("Segoe UI Semibold", 9.0F),
            .BackColor = backClr,
            .ForeColor = foreClr
        }
        b.FlatAppearance.BorderSize = 0
        Dim normalClr As Color = backClr
        Dim hoverClr As Color = Color.FromArgb(Math.Max(0, backClr.R - 14),
                                               Math.Max(0, backClr.G - 14),
                                               Math.Max(0, backClr.B - 14))
        AddHandler b.Paint,
            Sub(s, e)
                Dim bb = CType(s, Button)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim rect = bb.ClientRectangle : rect.Inflate(-1, -1)
                Using br As New SolidBrush(bb.BackColor)
                    g.FillRoundedRectangle(br, rect, 7)
                End Using
                TextRenderer.DrawText(g, bb.Text, bb.Font, bb.ClientRectangle, bb.ForeColor,
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
