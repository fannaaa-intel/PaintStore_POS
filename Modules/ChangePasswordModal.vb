Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms

' =============================================================================
'  ChangePasswordModal - forced password change at sign-in.
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
        Return Show(userId, fullName, forced:=True)
    End Function

    ' =========================================================================
    '  VOLUNTARY - shown from the Account button on every dashboard.
    '
    '  Asks for the current password first. Without that, anyone who found a
    '  till left signed in could change the password and lock out its owner.
    '
    '  Returns True when the password was changed.
    ' =========================================================================
    Public Function PromptVoluntary(userId As Integer, fullName As String) As Boolean
        Return Show(userId, fullName, forced:=False)
    End Function

    ' Both entry points render the same card; `forced` decides whether the
    ' current-password field appears and whether the dialog can be dismissed.
    Private Function Show(userId As Integer, fullName As String,
                          forced As Boolean) As Boolean

        Dim succeeded As Boolean = False

        Dim f As New Form With {
            .FormBorderStyle = FormBorderStyle.None,
            .StartPosition = FormStartPosition.CenterParent,
            .BackColor = CardBg,
            .ShowInTaskbar = False,
            .Width = CARD_W,
            .KeyPreview = True
        }

        Dim lblTitle As New Label With {
            .Text = If(forced, "Choose a new password", "Change your password"),
            .Font = New Font("Segoe UI Semibold", 12.0F),
            .ForeColor = TitleClr,
            .AutoSize = False,
            .Location = New Point(PAD, PAD),
            .Size = New Size(CARD_W - PAD * 2, 22),
            .BackColor = Color.Transparent
        }

        ' Names the account. The forced dialog appears straight after sign-in
        ' with no other context on screen, and a shared till may well have had
        ' someone else's name in the username box a moment ago.
        Dim who As String = If(String.IsNullOrWhiteSpace(fullName),
                              "Your password", fullName.Trim() & "’s password")
        Dim intro As String
        If forced Then
            intro = who & " was issued by an administrator, so it has to be " &
                    "replaced before you can continue."
        Else
            intro = "Enter your current password, then choose a new one. You stay " &
                    "signed in afterwards."
        End If
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

        ' Current password - voluntary mode only. Nothing above it moves, so
        ' the two layouts share every other measurement.
        Dim lblCurrent As Label = Nothing
        Dim txtCurrent As TextBox = Nothing
        Dim stackTop As Integer = lblBody.Bottom
        If Not forced Then
            lblCurrent = New Label With {
                .Text = "Current password",
                .Font = New Font("Segoe UI Semibold", 9.0F),
                .ForeColor = SubText,
                .AutoSize = True,
                .Location = New Point(PAD, lblBody.Bottom + 14),
                .BackColor = Color.Transparent
            }
            txtCurrent = MakeField(PAD, lblCurrent.Bottom + 4, CARD_W - PAD * 2, f)
            stackTop = txtCurrent.Parent.Bottom + 2
        End If

        Dim lblNew As New Label With {
            .Text = "New password",
            .Font = New Font("Segoe UI Semibold", 9.0F),
            .ForeColor = SubText,
            .AutoSize = True,
            .Location = New Point(PAD, stackTop + 14),
            .BackColor = Color.Transparent
        }
        Dim txtNew As TextBox = MakeField(PAD, lblNew.Bottom + 4, CARD_W - PAD * 2, f)

        Dim lblConfirm As New Label With {
            .Text = "Confirm new password",
            .Font = New Font("Segoe UI Semibold", 9.0F),
            .ForeColor = SubText,
            .AutoSize = True,
            .Location = New Point(PAD, txtNew.Parent.Bottom + 12),
            .BackColor = Color.Transparent
        }
        Dim txtConfirm As TextBox = MakeField(PAD, lblConfirm.Bottom + 4, CARD_W - PAD * 2, f)

        ' Fixed two-line height so the card never resizes as the message changes.
        Dim lblErr As New Label With {
            .Text = "",
            .Font = New Font("Segoe UI", 9.0F),
            .ForeColor = ErrClr,
            .AutoSize = False,
            .Location = New Point(PAD, txtConfirm.Parent.Bottom + 8),
            .Size = New Size(CARD_W - PAD * 2, 32),
            .BackColor = Color.Transparent
        }

        Dim dividerY As Integer = lblErr.Bottom + 10
        Dim btnY As Integer = dividerY + 16
        Dim cardH As Integer = btnY + 38 + PAD

        ' Forced: the secondary action abandons the sign-in, and says so, because
        ' there is no dismissing the requirement. Voluntary: it is an ordinary
        ' Cancel that leaves the password alone.
        Dim btnSave As Button = MakeButton("Set password", True,
            New Point(CARD_W - PAD - 130, btnY), New Size(130, 38))
        Dim btnCancel As Button = MakeButton(If(forced, "Sign out", "Cancel"), False,
            New Point(CARD_W - PAD - 130 - 8 - 96, btnY), New Size(96, 38))

        AddHandler btnCancel.Click,
            Sub()
                succeeded = False
                f.Close()
            End Sub

        AddHandler btnSave.Click,
            Sub()
                Dim pw As String = txtNew.Text
                Dim cf As String = txtConfirm.Text

                ' Prove it is really this user before changing anything.
                If Not forced Then
                    If Not AccountRequests.VerifyCurrentPassword(userId, txtCurrent.Text) Then
                        lblErr.Text = "That is not your current password."
                        txtCurrent.Focus()
                        txtCurrent.SelectAll()
                        Return
                    End If
                    If pw = txtCurrent.Text Then
                        lblErr.Text = "The new password must be different from the current one."
                        txtNew.Focus()
                        txtNew.SelectAll()
                        Return
                    End If
                End If

                ' The same rule the sign-up card enforces, so the two screens
                ' can never disagree about what an acceptable password is.
                Dim err As String = AccountRequests.ValidatePassword(pw)
                If err = "" AndAlso pw <> cf Then err = "Passwords do not match."
                If err <> "" Then
                    lblErr.Text = err
                    txtNew.Focus()
                    txtNew.SelectAll()
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

        f.Height = cardH
        f.Controls.AddRange({lblTitle, lblBody, lblNew, lblConfirm, lblErr, btnSave, btnCancel})
        If lblCurrent IsNot Nothing Then f.Controls.Add(lblCurrent)

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

        f.AcceptButton = btnSave

        ' Forced: Escape must not dismiss this - the point is that the temporary
        ' password stops working here. Voluntary: Escape is an ordinary cancel.
        AddHandler f.KeyDown,
            Sub(s As Object, e As KeyEventArgs)
                If e.KeyCode <> Keys.Escape Then Return
                e.Handled = True
                If Not forced Then
                    succeeded = False
                    f.Close()
                End If
            End Sub

        ' Start on the first field the user actually has to fill in.
        AddHandler f.Shown, Sub() If forced Then txtNew.Focus() Else txtCurrent.Focus()

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

        Return succeeded
    End Function

    ' A masked field on the rounded panel the rest of the app uses. The panel is
    ' reachable as the textbox's Parent, so callers stack by .Parent.Bottom.
    Private Function MakeField(x As Integer, y As Integer, w As Integer, host As Form) As TextBox
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
            .Width = w - 70,
            .UseSystemPasswordChar = True
        }

        ' Show/hide toggle, the same affordance the sign-up card offers.
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
        pnl.Controls.Add(btnEye)
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
