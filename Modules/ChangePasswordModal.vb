Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms

' =============================================================================
'  ChangePasswordModal - every password change in the app goes through here:
'  the forced one at sign-in and the voluntary one in Settings on all five
'  dashboards. Since the email update it is two steps: prove it is you
'  (password + account email), then type the code emailed to you.
'
'  (Original notes on the forced change follow.)
'
'  WHY THIS EXISTS
'  ---------------
'  An admin handling a reset request issues a temporary password and reads it
'  out over the counter (AdminUserPanelBuilder.IssueResetPassword). That
'  password has been spoken aloud, so it must not stay valid: the same action
'  sets users.must_change_password = 1, and this dialog is what clears it.
'
'  The login screen shows this once the password has been verified but BEFORE
'  the session is established and a dashboard opens, so a temporary password
'  cannot be used to reach any part of the app.
'
'  NOT DISMISSABLE
'  ---------------
'  Escape does not close it and there is no quiet way out. The only two exits
'  are setting a new password or abandoning the sign-in, because "later" would
'  leave the spoken-aloud password working - exactly what the flag exists to
'  prevent.
'
'  SCHEMA TOLERANCE
'  ----------------
'  Like the rest of the approval feature this is inert on an un-migrated
'  database: AccountRequests.MustChangePassword returns False when the column
'  is absent, so LogIn never reaches this dialog and behaviour is unchanged.
' =============================================================================
Public Module ChangePasswordModal

    Private ReadOnly CardBg As Color = Color.White
    Private ReadOnly TitleClr As Color = Color.FromArgb(25, 40, 35)
    Private ReadOnly BodyClr As Color = Color.FromArgb(80, 100, 94)
    Private ReadOnly BorderClr As Color = Color.FromArgb(160, 195, 185)
    Private ReadOnly DividerClr As Color = Color.FromArgb(230, 238, 235)
    Private ReadOnly MintPrimary As Color = Theme.Primary
    Private ReadOnly MintDark As Color = Theme.Dark
    Private ReadOnly SubText As Color = Theme.SubText
    Private ReadOnly FieldBg As Color = Theme.PageBg
    Private ReadOnly FieldBorder As Color = Theme.BorderMuted
    Private ReadOnly ErrClr As Color = Color.FromArgb(200, 65, 65)

    Private Const RADIUS As Integer = 10
    Private Const CARD_W As Integer = 430
    Private Const PAD As Integer = 24
    Private Const FIELD_H As Integer = 40

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

    ' =========================================================================
    '  FORCED - shown by LogIn when must_change_password is set.
    '
    '  Returns True when a new password was set and stored; False when the user
    '  gave up, in which case the caller must abandon the sign-in.
    ' =========================================================================
    Public Function Prompt(userId As Integer, fullName As String) As Boolean
        Return Run(userId, fullName, forced:=True)
    End Function

    ' =========================================================================
    '  VOLUNTARY - Settings > My Account > Change Password, on every dashboard.
    '
    '  Returns True when the password was changed.
    ' =========================================================================
    Public Function PromptVoluntary(userId As Integer, fullName As String) As Boolean
        Return Run(userId, fullName, forced:=False)
    End Function

    ' =========================================================================
    '  TWO-STEP FLOW
    '
    '  Step 1  Prove who you are: current password (voluntary only - the forced
    '          dialog follows a sign-in that just checked it) AND the email on
    '          the account. A code is emailed there.
    '  Step 2  Type the code, then the new password twice.
    '
    '  Someone who knows the password but not the mailbox cannot get past step
    '  2, so a leaked password is no longer enough to take an account over.
    '
    '  FALLBACKS
    '  - Database without the email column / code table: the original one-step
    '    change, so an un-migrated database keeps working.
    '  - Forced change on an account with no email on file: one-step as well.
    '    The admin just handed over a temporary password in person, and
    '    refusing here would lock the person out of their own first sign-in.
    '  - Voluntary change with no email on file: refused, with the fix spelled
    '    out, because skipping the code there would defeat the whole point.
    ' =========================================================================
    Private Function Run(userId As Integer, fullName As String, forced As Boolean) As Boolean
        Dim twoStep As Boolean = AccountRequests.HasEmailColumn() AndAlso PasswordOtp.HasOtpTable()
        If Not twoStep Then Return ShowSingleStep(userId, fullName, forced)

        Dim email As String = AccountRequests.GetEmail(userId)
        If email = "" Then
            If forced Then Return ShowSingleStep(userId, fullName, forced)
            AppModal.Show("Your account has no email address yet, so a verification " &
                          "code cannot be sent." & vbCrLf & vbCrLf &
                          "Ask the administrator to add your email in User Management " &
                          "(Edit user), then try again.",
                          "Change Password", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return False
        End If

        If Not ShowVerifyStep(userId, fullName, email, forced) Then Return False
        Return ShowCodeStep(userId, fullName, email, forced)
    End Function

    ' ── STEP 1: current password + email ─────────────────────────────────────
    Private Function ShowVerifyStep(userId As Integer, fullName As String,
                                    email As String, forced As Boolean) As Boolean
        Dim passed As Boolean = False
        Dim f As Form = NewCard()

        Dim intro As String = If(forced,
            NameFor(fullName) & " was issued by an administrator, so it has to be replaced. " &
            "Type the email on your account and we will send you a 6-digit code.",
            "Step 1 of 2. Confirm it is you: type your current password and the email " &
            "on your account. We will send a 6-digit code to that email.")
        Dim y As Integer = AddHeader(f, If(forced, "Choose a new password", "Change your password"), intro)

        Dim txtCurrent As TextBox = Nothing
        If Not forced Then
            y = AddLabel(f, "Current password", y + 14)
            txtCurrent = MakeField(PAD, y + 4, CARD_W - PAD * 2, f)
            y = txtCurrent.Parent.Bottom
        End If

        y = AddLabel(f, "Email on your account", y + 12)
        Dim txtEmail As TextBox = MakeField(PAD, y + 4, CARD_W - PAD * 2, f, masked:=False)
        y = txtEmail.Parent.Bottom

        Dim lblErr As Label = AddError(f, y + 8)
        Dim btnSend As Button = Nothing, btnCancel As Button = Nothing
        FinishCard(f, lblErr.Bottom + 10, "Send code", If(forced, "Sign out", "Cancel"), btnSend, btnCancel)

        AddHandler btnCancel.Click, Sub() f.Close()
        AddHandler btnSend.Click,
            Sub()
                lblErr.Text = ""
                If Not forced AndAlso
                   Not AccountRequests.VerifyCurrentPassword(userId, txtCurrent.Text) Then
                    lblErr.Text = "That is not your current password."
                    txtCurrent.Focus() : txtCurrent.SelectAll()
                    Return
                End If
                ' The typed address must match the one on file. Saying which
                ' address IS on file would hand it to whoever is at the till.
                If Not String.Equals(txtEmail.Text.Trim(), email, StringComparison.OrdinalIgnoreCase) Then
                    lblErr.Text = "That is not the email on this account."
                    txtEmail.Focus() : txtEmail.SelectAll()
                    Return
                End If

                Dim err As String = SendCode(f, userId, fullName, email)
                If err <> "" Then
                    lblErr.Text = err
                    Return
                End If
                passed = True
                f.Close()
            End Sub

        f.AcceptButton = btnSend
        AddHandler f.Shown, Sub() If forced Then txtEmail.Focus() Else txtCurrent.Focus()
        ShowCard(f, dismissable:=Not forced)
        Return passed
    End Function

    ' ── STEP 2: code + new password ──────────────────────────────────────────
    Private Function ShowCodeStep(userId As Integer, fullName As String,
                                  email As String, forced As Boolean) As Boolean
        Dim succeeded As Boolean = False
        Dim f As Form = NewCard()

        Dim y As Integer = AddHeader(f, "Enter the code",
            "Step 2 of 2. We sent a 6-digit code to " & PasswordOtp.MaskEmail(email) &
            ". It expires in " & PasswordOtp.CodeMinutes.ToString() & " minutes. " &
            "Check Spam if it is not in the inbox.")

        y = AddLabel(f, "6-digit code", y + 14)
        Dim txtCode As TextBox = MakeField(PAD, y + 4, CARD_W - PAD * 2, f, masked:=False)
        txtCode.MaxLength = 6

        ' Resend sits beside the code label, where the eye already is when the
        ' email does not arrive. Disabled for a minute after each send so an
        ' impatient double-click does not fire off a pile of emails.
        Dim lnkResend As New LinkLabel With {
            .Text = "Resend code",
            .Font = New Font("Segoe UI", 9.0F),
            .AutoSize = True,
            .LinkColor = MintPrimary,
            .ActiveLinkColor = MintDark,
            .DisabledLinkColor = Color.FromArgb(160, 175, 170),
            .BackColor = Color.Transparent
        }
        f.Controls.Add(lnkResend)
        lnkResend.Location = New Point(CARD_W - PAD - lnkResend.PreferredWidth, y)
        y = txtCode.Parent.Bottom

        y = AddLabel(f, "New password", y + 12)
        Dim txtNew As TextBox = MakeField(PAD, y + 4, CARD_W - PAD * 2, f)
        y = AddLabel(f, "Confirm new password", txtNew.Parent.Bottom + 12)
        Dim txtConfirm As TextBox = MakeField(PAD, y + 4, CARD_W - PAD * 2, f)

        Dim lblErr As Label = AddError(f, txtConfirm.Parent.Bottom + 8)
        Dim btnSave As Button = Nothing, btnCancel As Button = Nothing
        FinishCard(f, lblErr.Bottom + 10, "Set password", If(forced, "Sign out", "Cancel"), btnSave, btnCancel)

        Dim cooldown As New Timer With {.Interval = 1000}
        Dim secondsLeft As Integer = PasswordOtp.ResendSeconds
        Dim startCooldown As Action =
            Sub()
                secondsLeft = PasswordOtp.ResendSeconds
                lnkResend.Enabled = False
                lnkResend.Text = "Resend in " & secondsLeft.ToString() & "s"
                cooldown.Start()
            End Sub
        AddHandler cooldown.Tick,
            Sub()
                secondsLeft -= 1
                If secondsLeft <= 0 Then
                    cooldown.Stop()
                    lnkResend.Enabled = True
                    lnkResend.Text = "Resend code"
                Else
                    lnkResend.Text = "Resend in " & secondsLeft.ToString() & "s"
                End If
                lnkResend.Left = CARD_W - PAD - lnkResend.PreferredWidth
            End Sub
        startCooldown()

        AddHandler lnkResend.LinkClicked,
            Sub()
                Dim err As String = SendCode(f, userId, fullName, email)
                lblErr.ForeColor = If(err = "", MintDark, ErrClr)
                lblErr.Text = If(err = "", "A new code was sent. Earlier codes no longer work.", err)
                If err = "" Then startCooldown()
                txtCode.Focus()
            End Sub

        AddHandler btnCancel.Click, Sub() f.Close()
        AddHandler btnSave.Click,
            Sub()
                lblErr.ForeColor = ErrClr
                lblErr.Text = ""

                ' Check the password rules before spending a code attempt on it.
                Dim pw As String = txtNew.Text
                Dim err As String = AccountRequests.ValidatePassword(pw)
                If err = "" AndAlso pw <> txtConfirm.Text Then err = "Passwords do not match."
                If err = "" AndAlso AccountRequests.VerifyCurrentPassword(userId, pw) Then
                    err = "The new password must be different from the current one."
                End If
                If err <> "" Then
                    lblErr.Text = err
                    txtNew.Focus() : txtNew.SelectAll()
                    Return
                End If

                Dim codeErr As String = PasswordOtp.Verify(userId, txtCode.Text)
                If codeErr <> "" Then
                    lblErr.Text = codeErr
                    txtCode.Focus() : txtCode.SelectAll()
                    Return
                End If

                Dim dbErr As String = AccountRequests.SetPassword(userId, pw)
                If dbErr <> "" Then
                    lblErr.Text = dbErr
                    Return
                End If
                succeeded = True
                f.Close()
            End Sub

        f.AcceptButton = btnSave
        AddHandler f.Shown, Sub() txtCode.Focus()
        AddHandler f.FormClosed, Sub() cooldown.Dispose()
        ShowCard(f, dismissable:=Not forced)

        If succeeded AndAlso Not forced Then
            ActivityLog.Record("PASSWORD_CHANGE", "user", userId, "Changed own password (email verified)")
        End If
        Return succeeded
    End Function

    ' Sends a code with a wait cursor - the SMTP round trip takes a second or two.
    Private Function SendCode(f As Form, userId As Integer, fullName As String, email As String) As String
        Dim old As Cursor = f.Cursor
        f.Cursor = Cursors.WaitCursor
        f.UseWaitCursor = True
        Application.DoEvents()
        Try
            Return PasswordOtp.Issue(userId, fullName, email)
        Finally
            f.UseWaitCursor = False
            f.Cursor = old
        End Try
    End Function

    ' ── ONE-STEP (the original dialog, used by the fallbacks above) ──────────
    Private Function ShowSingleStep(userId As Integer, fullName As String, forced As Boolean) As Boolean
        Dim succeeded As Boolean = False
        Dim f As Form = NewCard()

        Dim y As Integer = AddHeader(f,
            If(forced, "Choose a new password", "Change your password"),
            If(forced,
               NameFor(fullName) & " was issued by an administrator, so it has to be " &
               "replaced before you can continue.",
               "Enter your current password, then choose a new one. You stay " &
               "signed in afterwards."))

        Dim txtCurrent As TextBox = Nothing
        If Not forced Then
            y = AddLabel(f, "Current password", y + 14)
            txtCurrent = MakeField(PAD, y + 4, CARD_W - PAD * 2, f)
            y = txtCurrent.Parent.Bottom + 2
        End If
        y = AddLabel(f, "New password", y + 14)
        Dim txtNew As TextBox = MakeField(PAD, y + 4, CARD_W - PAD * 2, f)
        y = AddLabel(f, "Confirm new password", txtNew.Parent.Bottom + 12)
        Dim txtConfirm As TextBox = MakeField(PAD, y + 4, CARD_W - PAD * 2, f)

        Dim lblErr As Label = AddError(f, txtConfirm.Parent.Bottom + 8)
        Dim btnSave As Button = Nothing, btnCancel As Button = Nothing
        FinishCard(f, lblErr.Bottom + 10, "Set password", If(forced, "Sign out", "Cancel"), btnSave, btnCancel)

        AddHandler btnCancel.Click, Sub() f.Close()
        AddHandler btnSave.Click,
            Sub()
                Dim pw As String = txtNew.Text
                If Not forced Then
                    If Not AccountRequests.VerifyCurrentPassword(userId, txtCurrent.Text) Then
                        lblErr.Text = "That is not your current password."
                        txtCurrent.Focus() : txtCurrent.SelectAll()
                        Return
                    End If
                    If pw = txtCurrent.Text Then
                        lblErr.Text = "The new password must be different from the current one."
                        txtNew.Focus() : txtNew.SelectAll()
                        Return
                    End If
                End If
                Dim err As String = AccountRequests.ValidatePassword(pw)
                If err = "" AndAlso pw <> txtConfirm.Text Then err = "Passwords do not match."
                If err <> "" Then
                    lblErr.Text = err
                    txtNew.Focus() : txtNew.SelectAll()
                    Return
                End If
                Dim dbErr As String = AccountRequests.SetPassword(userId, pw)
                If dbErr <> "" Then
                    lblErr.Text = dbErr
                    Return
                End If
                succeeded = True
                f.Close()
            End Sub

        f.AcceptButton = btnSave
        AddHandler f.Shown, Sub() If forced Then txtNew.Focus() Else txtCurrent.Focus()
        ShowCard(f, dismissable:=Not forced)

        If succeeded AndAlso Not forced Then
            ActivityLog.Record("PASSWORD_CHANGE", "user", userId, "Changed own password")
        End If
        Return succeeded
    End Function

    ' ── CARD BUILDING BLOCKS (shared by all three dialogs) ───────────────────
    Private Function NameFor(fullName As String) As String
        ' Names the account: a shared till may have had someone else's name in
        ' the username box a moment ago.
        Return If(String.IsNullOrWhiteSpace(fullName),
                  "Your password", fullName.Trim() & "’s password")
    End Function

    Private Function NewCard() As Form
        Return New Form With {
            .FormBorderStyle = FormBorderStyle.None,
            .StartPosition = FormStartPosition.CenterParent,
            .BackColor = CardBg,
            .ShowInTaskbar = False,
            .Width = CARD_W,
            .KeyPreview = True
        }
    End Function

    ' Title + wrapped intro. Returns the y below the intro.
    Private Function AddHeader(f As Form, title As String, intro As String) As Integer
        Dim lblTitle As New Label With {
            .Text = title,
            .Font = New Font("Segoe UI Semibold", 12.0F),
            .ForeColor = TitleClr,
            .AutoSize = False,
            .Location = New Point(PAD, PAD),
            .Size = New Size(CARD_W - PAD * 2, 22),
            .BackColor = Color.Transparent
        }
        Dim bodyFont As New Font("Segoe UI", 9.5F)
        Dim measured As Size = TextRenderer.MeasureText(
            intro, bodyFont, New Size(CARD_W - PAD * 2, 0),
            TextFormatFlags.WordBreak Or TextFormatFlags.TextBoxControl)
        Dim lblBody As New Label With {
            .Text = intro,
            .Font = bodyFont,
            .ForeColor = BodyClr,
            .AutoSize = False,
            .Location = New Point(PAD, lblTitle.Bottom + 8),
            .Size = New Size(CARD_W - PAD * 2, Math.Max(20, measured.Height + 4)),
            .BackColor = Color.Transparent
        }
        f.Controls.AddRange({lblTitle, lblBody})
        Return lblBody.Bottom
    End Function

    ' A field caption at y. Returns its bottom.
    Private Function AddLabel(f As Form, text As String, y As Integer) As Integer
        Dim lbl As New Label With {
            .Text = text,
            .Font = New Font("Segoe UI Semibold", 9.0F),
            .ForeColor = SubText,
            .AutoSize = True,
            .Location = New Point(PAD, y),
            .BackColor = Color.Transparent
        }
        f.Controls.Add(lbl)
        Return lbl.Bottom
    End Function

    ' Fixed two-line height so the card never resizes as the message changes.
    Private Function AddError(f As Form, y As Integer) As Label
        Dim lblErr As New Label With {
            .Text = "",
            .Font = New Font("Segoe UI", 9.0F),
            .ForeColor = ErrClr,
            .AutoSize = False,
            .Location = New Point(PAD, y),
            .Size = New Size(CARD_W - PAD * 2, 32),
            .BackColor = Color.Transparent
        }
        f.Controls.Add(lblErr)
        Return lblErr
    End Function

    ' Divider, button row, rounded chrome and final height.
    Private Sub FinishCard(f As Form, dividerY As Integer, okText As String, cancelText As String,
                           ByRef btnOk As Button, ByRef btnCancel As Button)
        Dim btnY As Integer = dividerY + 16
        Dim cardH As Integer = btnY + 38 + PAD

        btnOk = MakeButton(okText, True, New Point(CARD_W - PAD - 130, btnY), New Size(130, 38))
        btnCancel = MakeButton(cancelText, False, New Point(CARD_W - PAD - 130 - 8 - 96, btnY), New Size(96, 38))
        f.Controls.AddRange({btnOk, btnCancel})
        f.Height = cardH

        AddHandler f.Paint,
            Sub(s As Object, e As PaintEventArgs)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim card As New Rectangle(0, 0, f.Width - 1, f.Height - 1)
                Using pen As New Pen(DividerClr, 1.0F)
                    g.DrawLine(pen, PAD, dividerY, f.Width - PAD, dividerY)
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
    End Sub

    ' Forced dialogs ignore Escape - "later" would leave a spoken-aloud
    ' temporary password working, which is exactly what the flag prevents.
    Private Sub ShowCard(f As Form, dismissable As Boolean)
        AddHandler f.KeyDown,
            Sub(s As Object, e As KeyEventArgs)
                If e.KeyCode <> Keys.Escape Then Return
                e.Handled = True
                If dismissable Then f.Close()
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
    End Sub

    ' A masked field on the rounded panel the rest of the app uses. The panel is
    ' reachable as the textbox's Parent, so callers stack by .Parent.Bottom.
    ' masked:=False gives a plain field (email, code) with no SHOW toggle.
    Private Function MakeField(x As Integer, y As Integer, w As Integer, host As Form,
                               Optional masked As Boolean = True) As TextBox
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

        ' Show/hide toggle, the same affordance the login screen offers.
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
        If masked Then pnl.Controls.Add(btnEye)
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
        Dim hoverClr As Color = If(isPrimary, MintDark, Color.FromArgb(231, 234, 232))

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

End Module
