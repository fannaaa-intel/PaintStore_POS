Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Runtime.InteropServices
Imports System.Windows.Forms

' =============================================================================
'  IdleLock - covers the dashboard after a few minutes with no mouse or keys.
'
'  A till left signed in could be used by anyone walking past: ring up sales,
'  refund, open settings, all under the absent person's name. After IdleMinutes
'  without input the screen is covered and asks for that person's password.
'
'  If they are gone for good, an Admin can sign them out from the lock screen
'  with the Admin's own username and password. Signing out is NOT offered
'  without a password: on the POS that would close someone's shift uncounted.
'
'  Each dashboard calls Attach(Me, <its own no-questions logout>) on Load.
' =============================================================================
Public Module IdleLock

    ' Minutes without keyboard or mouse input before the screen locks.
    Public Const IdleMinutes As Integer = 5

    <StructLayout(LayoutKind.Sequential)>
    Private Structure LASTINPUTINFO
        Public cbSize As UInteger
        Public dwTime As UInteger
    End Structure

    <DllImport("user32.dll")>
    Private Function GetLastInputInfo(ByRef plii As LASTINPUTINFO) As Boolean
    End Function

    Private _timer As Timer
    Private _owner As Form
    Private _logout As Action
    Private _locked As Boolean = False

    Public ReadOnly Property IsLocked As Boolean
        Get
            Return _locked
        End Get
    End Property

    ' Start watching for idle time on this dashboard. logout must sign out
    ' WITHOUT asking "are you sure" - the Admin already confirmed it.
    Public Sub Attach(owner As Form, logout As Action)
        Detach()
        _owner = owner
        _logout = logout
        _timer = New Timer With {.Interval = 5000}
        AddHandler _timer.Tick, AddressOf OnTick
        AddHandler owner.FormClosed, Sub() If _owner Is owner Then Detach()
        _timer.Start()
    End Sub

    Public Sub Detach()
        If _timer IsNot Nothing Then
            _timer.Stop()
            _timer.Dispose()
            _timer = Nothing
        End If
        _owner = Nothing
        _logout = Nothing
    End Sub

    Private Function IdleTime() As TimeSpan
        Dim info As New LASTINPUTINFO With {.cbSize = CUInt(Marshal.SizeOf(GetType(LASTINPUTINFO)))}
        If Not GetLastInputInfo(info) Then Return TimeSpan.Zero
        ' Both values wrap at ~49.7 days; unsigned subtraction handles that.
        Dim ms As UInteger = CUInt((CLng(Environment.TickCount) And &HFFFFFFFFL) - info.dwTime And &HFFFFFFFFL)
        Return TimeSpan.FromMilliseconds(ms)
    End Function

    Private Sub OnTick(sender As Object, e As EventArgs)
        If _locked OrElse _owner Is Nothing OrElse _owner.IsDisposed Then Return
        If Session.CurrentUserId = 0 Then Return
        If IdleTime() < TimeSpan.FromMinutes(IdleMinutes) Then Return
        LockNow()
    End Sub

    ' Shows the lock screen and waits until it is unlocked or the Admin signs
    ' the user out.
    Public Sub LockNow()
        If _locked OrElse _owner Is Nothing OrElse _owner.IsDisposed Then Return
        _locked = True
        _timer?.Stop()
        Dim signOut As Boolean = False
        Try
            ActivityLog.Record("SCREEN_LOCKED", "user", Session.CurrentUserId,
                               "No activity for " & IdleMinutes & " minutes")
            signOut = ShowLockScreen()
        Catch ex As Exception
            ErrorLog.Write("IdleLock.LockNow", ex)
        Finally
            _locked = False
        End Try

        If signOut Then
            ' The lock may have opened on top of another dialog (a payment
            ' prompt, a confirmation). Close those first, then sign out once
            ' the stack has unwound back to the dashboard.
            Dim owner As Form = _owner
            Dim logout As Action = _logout
            For Each f As Form In Application.OpenForms.Cast(Of Form)().ToList()
                If f IsNot owner AndAlso f.Modal Then
                    Try : f.DialogResult = DialogResult.Cancel : f.Close() : Catch : End Try
                End If
            Next
            If owner IsNot Nothing AndAlso Not owner.IsDisposed AndAlso logout IsNot Nothing Then
                owner.BeginInvoke(logout)
            End If
        Else
            _timer?.Start()
        End If
    End Sub

    ' Returns True when an Admin chose to sign the user out.
    Private Function ShowLockScreen() As Boolean
        Dim signOut As Boolean = False
        Dim allowClose As Boolean = False
        Dim adminMode As Boolean = False

        Dim cover As New Form With {
            .FormBorderStyle = FormBorderStyle.None,
            .StartPosition = FormStartPosition.Manual,
            .Bounds = Screen.FromControl(_owner).Bounds,
            .BackColor = Color.FromArgb(22, 38, 34),
            .ShowInTaskbar = False, .TopMost = True, .KeyPreview = True
        }

        Const CARD_W As Integer = 420
        Const PAD As Integer = 28
        Dim card As New Panel With {.BackColor = Color.White, .Width = CARD_W}
        cover.Controls.Add(card)

        ' Positions are worked out in relayout from each label's measured
        ' height. Placing them from .Bottom at creation time made the second
        ' line overlap the title (AutoSize had not measured the big font yet).
        Dim lblTitle As New Label With {
            .Text = "Screen locked", .Font = New Font("Segoe UI Semibold", 16.0F),
            .ForeColor = Color.FromArgb(25, 40, 35), .AutoSize = True}
        Dim lblWho As New Label With {
            .Text = "Signed in as " & Session.CurrentFullName & "  (" & Session.CurrentRole & ")",
            .Font = New Font("Segoe UI", 10.0F), .ForeColor = Theme.SubText, .AutoSize = True}
        Dim lblHint As New Label With {
            .Font = New Font("Segoe UI", 9.5F), .ForeColor = Theme.SubText,
            .AutoSize = False, .Width = CARD_W - PAD * 2}
        card.Controls.AddRange({lblTitle, lblWho, lblHint})

        Dim mkLabel = Function(text As String) As Label
                          Dim l As New Label With {
                              .Text = text, .Font = New Font("Segoe UI Semibold", 9.0F),
                              .ForeColor = Theme.SubText, .AutoSize = True}
                          card.Controls.Add(l)
                          Return l
                      End Function
        Dim mkBox = Function(isPass As Boolean) As TextBox
                        Dim t As New TextBox With {
                            .Font = New Font("Segoe UI", 11.0F),
                            .Width = CARD_W - PAD * 2, .UseSystemPasswordChar = isPass}
                        card.Controls.Add(t)
                        Return t
                    End Function

        ' Unlock mode shows the signed-in username (read-only) so it is plain
        ' whose password is wanted; Admin mode makes it an empty, typeable box.
        Dim lblUser = mkLabel("Username")
        Dim txtUser = mkBox(False)
        Dim lblPass = mkLabel("Password")
        Dim txtPass = mkBox(True)

        Dim lblErr As New Label With {
            .Text = "", .Font = New Font("Segoe UI", 9.0F), .ForeColor = Color.FromArgb(200, 65, 65),
            .AutoSize = False, .Size = New Size(CARD_W - PAD * 2, 36)}
        card.Controls.Add(lblErr)

        Dim btnOk As Button = PaymentMethodModal.MakeButton("Unlock", True, Point.Empty, New Size(130, 40))
        Dim lnkMode As New LinkLabel With {
            .Text = "Not you? Admin sign-out", .Font = New Font("Segoe UI", 9.0F),
            .AutoSize = True, .LinkColor = Theme.SubText, .ActiveLinkColor = Theme.SubText}
        card.Controls.AddRange({btnOk, lnkMode})

        ' Positions only - safe to run on every resize.
        Dim place As Action =
            Sub()
                Dim y As Integer = PAD
                lblTitle.Location = New Point(PAD - 2, y) : y += lblTitle.PreferredHeight + 2
                lblWho.Location = New Point(PAD, y) : y += lblWho.PreferredHeight + 12
                lblHint.Height = TextRenderer.MeasureText(lblHint.Text, lblHint.Font, New Size(lblHint.Width, 0),
                                                          TextFormatFlags.WordBreak).Height + 2
                lblHint.Location = New Point(PAD, y) : y += lblHint.Height + 12
                lblUser.Location = New Point(PAD, y) : y += lblUser.PreferredHeight + 4
                txtUser.Location = New Point(PAD, y) : y += txtUser.Height + 12
                lblPass.Location = New Point(PAD, y) : y += lblPass.PreferredHeight + 4
                txtPass.Location = New Point(PAD, y) : y += txtPass.Height + 8
                lblErr.Location = New Point(PAD, y) : y += lblErr.Height + 4
                btnOk.Location = New Point(CARD_W - PAD - btnOk.Width, y)
                lnkMode.Location = New Point(PAD, y + (btnOk.Height - lnkMode.PreferredHeight) \ 2)
                card.Height = btnOk.Bottom + PAD
                card.Location = New Point((cover.ClientSize.Width - card.Width) \ 2,
                                          (cover.ClientSize.Height - card.Height) \ 2)
            End Sub

        ' Switches between "unlock" and "Admin sign-out" and clears the boxes.
        Dim relayout As Action =
            Sub()
                If adminMode Then
                    lblHint.Text = "An Admin can sign " & Session.CurrentFullName & " out here. " &
                                   "Type the ADMIN's own username and password. Anything unsaved on this screen will be lost."
                    lblUser.Text = "Admin username"
                    txtUser.ReadOnly = False
                    txtUser.TabStop = True
                    txtUser.Text = ""
                    lblPass.Text = "Admin password"
                    btnOk.Text = "Sign out"
                    lnkMode.Text = "Back to unlock"
                Else
                    lblHint.Text = "No activity for " & IdleMinutes & " minutes. Enter the password of the " &
                                   "account below to continue. Forgot it or not you? Use Admin sign-out."
                    lblUser.Text = "Username"
                    txtUser.Text = Session.CurrentUsername
                    txtUser.ReadOnly = True
                    txtUser.TabStop = False
                    lblPass.Text = "Password for " & Session.CurrentUsername
                    btnOk.Text = "Unlock"
                    lnkMode.Text = "Not you? Admin sign-out"
                End If
                lblErr.Text = ""
                txtPass.Clear()
                place()
                If adminMode Then txtUser.Focus() Else txtPass.Focus()
            End Sub

        AddHandler lnkMode.LinkClicked,
            Sub()
                adminMode = Not adminMode
                relayout()
            End Sub

        AddHandler btnOk.Click,
            Sub()
                If txtPass.Text = "" Then
                    lblErr.Text = "Enter the password."
                    txtPass.Focus()
                    Return
                End If
                cover.Cursor = Cursors.WaitCursor
                Dim chk As Credentials.Result
                If adminMode Then
                    chk = Credentials.Check(txtUser.Text, txtPass.Text, "Admin")
                Else
                    chk = Credentials.Check(Session.CurrentUsername, txtPass.Text)
                    ' The account behind the session must be the one unlocking.
                    If chk.Ok AndAlso chk.UserId <> Session.CurrentUserId Then
                        chk.Ok = False
                        chk.Message = "Wrong password for " & Session.CurrentUsername & "."
                    End If
                End If
                cover.Cursor = Cursors.Default
                If Not chk.Ok Then
                    lblErr.Text = If(adminMode, chk.Message,
                        chk.Message.Replace("Wrong username or password.",
                                            "Wrong password for " & Session.CurrentUsername & "."))
                    txtPass.Clear()
                    txtPass.Focus()
                    Return
                End If
                If adminMode Then
                    ActivityLog.Record("ADMIN_SIGN_OUT", "user", Session.CurrentUserId,
                                       "Signed out from the lock screen by " & chk.FullName)
                    signOut = True
                Else
                    ActivityLog.Record("SCREEN_UNLOCKED", "user", Session.CurrentUserId)
                End If
                allowClose = True
                cover.Close()
            End Sub

        ' Alt+F4 must not get past the lock.
        AddHandler cover.FormClosing,
            Sub(s As Object, e As FormClosingEventArgs)
                If Not allowClose AndAlso e.CloseReason = CloseReason.UserClosing Then e.Cancel = True
            End Sub
        AddHandler cover.Shown, Sub() relayout()
        AddHandler cover.Resize, Sub() place()
        cover.AcceptButton = btnOk

        AddHandler card.Paint,
            Sub(s As Object, e As PaintEventArgs)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
                Using pen As New Pen(Color.FromArgb(160, 195, 185), 1.2F)
                    e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1)
                End Using
            End Sub

        Try
            cover.ShowDialog(_owner)
        Finally
            cover.Dispose()
        End Try
        Return signOut
    End Function

End Module
