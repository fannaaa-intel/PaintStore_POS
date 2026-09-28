Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.IO
Imports System.Windows.Forms
Imports MySql.Data.MySqlClient

' =============================================================================
'  SettingsPanelBuilder - the Settings page, shared by all five dashboards.
'
'  Replaces the old "My Account" sidebar button. Which tabs appear depends on
'  who is signed in:
'
'    Everyone      My Account    name, username, role, email + Change Password
'                  My Activity   this user's own history - never anyone else's
'    Cashier       Payment Methods - view, and REQUEST an add/removal
'    Admin         Store & Payments, Payment Requests (approve / reject),
'                  Database Backup, Recycle Bin
'
'  Nothing here shows another account's data: every query is filtered by
'  Session.CurrentUserId, and the admin-only tabs are not built for anyone
'  else even if this panel were opened from another dashboard.
' =============================================================================
Public Class SettingsPanelBuilder

    ' ── PALETTE (matches the admin panels) ───────────────────────────────────
    Private Shared ReadOnly MintDark As Color = Theme.Dark
    Private Shared ReadOnly MintPrimary As Color = Theme.Primary
    Private Shared ReadOnly MintBg As Color = Theme.PageBg
    Private Shared ReadOnly CardBg As Color = Color.White
    Private Shared ReadOnly SubText As Color = Theme.SubText
    Private Shared ReadOnly FaintText As Color = Theme.FaintText
    Private Shared ReadOnly RowDivider As Color = Theme.RowDivider
    Private Shared ReadOnly AccentRed As Color = Theme.Danger

    Private Const SIDE_PAD As Integer = 24

    Private Enum SettingsTab
        Account
        Activity
        Payments
        Store
        PayRequests
        Backup
        RecycleBin
    End Enum

    Private Shared _container As Panel
    Private Shared _host As Panel
    Private Shared _tab As SettingsTab = SettingsTab.Account
    Private Shared _tabButtons As New Dictionary(Of SettingsTab, Button)
    Private Shared _activityRange As Integer = 1   ' index into ActivityRanges
    Private Shared _backupFolder As String = ""

    Private Shared ReadOnly ActivityRanges As String() = {"Today", "Last 7 days", "Last 30 days", "All time"}

    ' =========================================================================
    '  ENTRY POINT
    ' =========================================================================
    Public Shared Sub LoadSettings(container As Panel)
        _container = container
        _tab = SettingsTab.Account
        If _backupFolder = "" Then _backupFolder = Backup.DefaultFolder
        Render()
    End Sub

    Private Shared ReadOnly Property IsAdmin As Boolean
        Get
            Return String.Equals(Session.CurrentRole, "Admin", StringComparison.OrdinalIgnoreCase)
        End Get
    End Property

    Private Shared ReadOnly Property IsCashier As Boolean
        Get
            Return String.Equals(Session.CurrentRole, "Cashier", StringComparison.OrdinalIgnoreCase)
        End Get
    End Property

    Private Shared Function TabsForRole() As List(Of (tab As SettingsTab, caption As String))
        Dim tabs As New List(Of (SettingsTab, String)) From {
            (SettingsTab.Account, "My Account"),
            (SettingsTab.Activity, "My Activity")
        }
        If IsCashier Then tabs.Add((SettingsTab.Payments, "Payment Methods"))
        If IsAdmin Then
            tabs.Add((SettingsTab.Store, "Store & Payments"))
            If PaymentRequests.HasTable() Then
                Dim n As Integer = PaymentRequests.PendingCount()
                tabs.Add((SettingsTab.PayRequests, "Payment Requests" & If(n > 0, "  (" & n.ToString() & ")", "")))
            End If
            tabs.Add((SettingsTab.Backup, "Database Backup"))
            tabs.Add((SettingsTab.RecycleBin, "Recycle Bin"))
        End If
        Return tabs
    End Function

    ' =========================================================================
    '  FRAME: title, tab strip, content host
    ' =========================================================================
    Private Shared Sub Render()
        _container.SuspendLayout()
        _container.Controls.Clear()
        _container.BackColor = MintBg

        _host = New Panel With {.Dock = DockStyle.Fill, .BackColor = MintBg}

        Dim titleBar As New Panel With {.Dock = DockStyle.Top, .Height = 104, .BackColor = MintBg}
        titleBar.Controls.Add(New Label With {
            .Text = "Settings",
            .Font = New Font("Segoe UI", 28, FontStyle.Bold),
            .ForeColor = MintDark, .AutoSize = True,
            .Location = New Point(SIDE_PAD, 12)
        })
        titleBar.Controls.Add(New Label With {
            .Text = "Signed in as " & Session.CurrentFullName & "  -  " & Session.CurrentRole,
            .Font = New Font("Segoe UI", 10),
            .ForeColor = SubText, .AutoSize = True,
            .Location = New Point(SIDE_PAD + 3, 74)
        })

        Dim strip As New FlowLayoutPanel With {
            .Dock = DockStyle.Top, .Height = 50, .BackColor = MintBg,
            .Padding = New Padding(SIDE_PAD - 3, 4, SIDE_PAD, 6), .WrapContents = False
        }
        _tabButtons.Clear()
        For Each t In TabsForRole()
            Dim tabId As SettingsTab = t.tab
            Dim b As New Button With {
                .Text = t.caption, .FlatStyle = FlatStyle.Flat, .Cursor = Cursors.Hand,
                .Font = New Font("Segoe UI Semibold", 10), .Height = 38,
                .Margin = New Padding(3, 0, 5, 0)
            }
            b.Width = TextRenderer.MeasureText(b.Text, b.Font).Width + 36
            b.FlatAppearance.BorderSize = 0
            AddHandler b.Paint, AddressOf PaintPill
            AddHandler b.Click, Sub() ShowTab(tabId)
            _tabButtons(tabId) = b
            strip.Controls.Add(b)
        Next

        _container.Controls.Add(_host)
        _container.Controls.Add(strip)
        _container.Controls.Add(titleBar)
        _container.ResumeLayout()
        ShowTab(_tab)
    End Sub

    Private Shared Sub ShowTab(t As SettingsTab)
        _tab = t
        For Each kv In _tabButtons
            Dim active As Boolean = kv.Key = t
            kv.Value.BackColor = If(active, MintPrimary, Color.FromArgb(220, 235, 232))
            kv.Value.ForeColor = If(active, Color.White, MintDark)
            kv.Value.Invalidate()
        Next

        _host.SuspendLayout()
        For Each c As Control In _host.Controls.Cast(Of Control).ToList()
            c.Dispose()
        Next
        _host.Controls.Clear()
        Select Case t
            Case SettingsTab.Account : BuildAccountTab()
            Case SettingsTab.Activity : BuildActivityTab()
            Case SettingsTab.Payments : BuildPaymentsTab()
            Case SettingsTab.Store
                If IsAdmin Then
                    ' The existing Store Settings screen, unchanged, inside the tab.
                    Dim inner As New Panel With {.Dock = DockStyle.Fill, .BackColor = MintBg}
                    _host.Controls.Add(inner)
                    AdminStoreSettingsPanelBuilder.LoadStoreSettings(inner)
                End If
            Case SettingsTab.PayRequests : If IsAdmin Then BuildPayRequestsTab()
            Case SettingsTab.Backup : If IsAdmin Then BuildBackupTab()
            Case SettingsTab.RecycleBin : If IsAdmin Then BuildRecycleTab()
        End Select
        _host.ResumeLayout()
    End Sub

    ' =========================================================================
    '  MY ACCOUNT
    ' =========================================================================
    Private Shared Sub BuildAccountTab()
        Dim fullName As String = Session.CurrentFullName
        Dim username As String = Session.CurrentUsername
        Dim created As String = "-"
        Dim email As String = ""
        Dim hasEmail As Boolean = AccountRequests.HasEmailColumn()
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT full_name, username, created_at" & If(hasEmail, ", email", "") &
                    " FROM users WHERE user_id = @id", conn)
                    cmd.Parameters.AddWithValue("@id", Session.CurrentUserId)
                    Using rdr = cmd.ExecuteReader()
                        If rdr.Read() Then
                            fullName = rdr("full_name").ToString()
                            username = rdr("username").ToString()
                            If Not IsDBNull(rdr("created_at")) Then
                                created = Convert.ToDateTime(rdr("created_at")).ToString("MMMM d, yyyy")
                            End If
                            If hasEmail AndAlso Not IsDBNull(rdr("email")) Then email = rdr("email").ToString()
                        End If
                    End Using
                End Using
            End Using
        Catch ex As Exception
            ' The session already has name/username; show those rather than nothing.
        End Try

        Dim scroll As Panel = NewScrollHost()

        ' Security card (added first: Dock=Top stacks in reverse).
        Dim sec As Panel = NewCard("Password", 150)
        Dim secText As String = If(hasEmail AndAlso PasswordOtp.HasOtpTable(),
            "Changing your password takes two steps: your current password and email, " &
            "then a 6-digit code we send to that email.",
            "Enter your current password, then choose a new one.")
        sec.Controls.Add(New Label With {
            .Text = secText, .Font = New Font("Segoe UI", 10), .ForeColor = SubText,
            .AutoSize = False, .Size = New Size(760, 40), .Location = New Point(24, 54),
            .BackColor = Color.Transparent
        })
        Dim btnPw As Button = MakeButton("Change Password", True, 170)
        btnPw.Location = New Point(24, 98)
        AddHandler btnPw.Click,
            Sub() ChangePasswordModal.PromptVoluntary(Session.CurrentUserId, Session.CurrentFullName)
        sec.Controls.Add(btnPw)

        ' Profile card.
        Dim prof As Panel = NewCard("My Account", 268)
        Dim y As Integer = 58
        Dim addRow As Action(Of String, String, Color) =
            Sub(label As String, value As String, clr As Color)
                prof.Controls.Add(New Label With {
                    .Text = label, .Font = New Font("Segoe UI", 10), .ForeColor = FaintText,
                    .AutoSize = True, .Location = New Point(24, y), .BackColor = Color.Transparent})
                prof.Controls.Add(New Label With {
                    .Text = value, .Font = New Font("Segoe UI Semibold", 11), .ForeColor = clr,
                    .AutoSize = True, .Location = New Point(180, y - 1), .BackColor = Color.Transparent})
                y += 38
            End Sub
        addRow("Full name", fullName, MintDark)
        addRow("Username", username, MintDark)
        addRow("Role", Session.CurrentRole, MintDark)
        If hasEmail Then
            If email = "" Then
                addRow("Email", "Not set - ask the administrator to add it", Theme.Warning)
            Else
                addRow("Email", email, MintDark)
            End If
        End If
        addRow("Member since", created, MintDark)

        scroll.Controls.Add(Spacer())
        scroll.Controls.Add(sec)
        scroll.Controls.Add(Spacer())
        scroll.Controls.Add(prof)
        _host.Controls.Add(scroll)
    End Sub

    ' =========================================================================
    '  MY ACTIVITY - this user's own trail only
    ' =========================================================================
    Private Shared Sub BuildActivityTab()
        Dim rows As New List(Of (at As DateTime, action As String, details As String))
        Dim loadErr As String = ""
        Try
            Dim fromDate As DateTime? = Nothing
            Select Case _activityRange
                Case 0 : fromDate = DateTime.Today
                Case 1 : fromDate = DateTime.Today.AddDays(-6)
                Case 2 : fromDate = DateTime.Today.AddDays(-29)
            End Select
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT created_at, action, details FROM activity_logs " &
                    "WHERE user_id = @uid" & If(fromDate.HasValue, " AND created_at >= @from", "") &
                    " ORDER BY created_at DESC, log_id DESC LIMIT 500", conn)
                    cmd.Parameters.AddWithValue("@uid", Session.CurrentUserId)
                    If fromDate.HasValue Then cmd.Parameters.AddWithValue("@from", fromDate.Value)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            rows.Add((Convert.ToDateTime(rdr("created_at")),
                                      rdr("action").ToString(),
                                      If(IsDBNull(rdr("details")), "", rdr("details").ToString())))
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            loadErr = "Could not load your activity: " & ex.Message
        End Try

        Dim card As Panel = NewCard("My Activity", 0)
        Dim wrap As Panel = FillCardWrap(card)

        Dim cbo As New ComboBox With {
            .DropDownStyle = ComboBoxStyle.DropDownList, .Font = New Font("Segoe UI", 10),
            .Width = 150, .FlatStyle = FlatStyle.Flat
        }
        cbo.Items.AddRange(ActivityRanges)
        cbo.SelectedIndex = _activityRange
        AddHandler cbo.SelectedIndexChanged,
            Sub()
                _activityRange = cbo.SelectedIndex
                ShowTab(SettingsTab.Activity)
            End Sub
        card.Controls.Add(cbo)
        Dim lblCount As New Label With {
            .Text = rows.Count.ToString("N0") & If(rows.Count = 1, " entry", " entries") &
                    If(rows.Count >= 500, " (latest 500)", ""),
            .Font = New Font("Segoe UI", 9.5F), .ForeColor = FaintText, .AutoSize = True,
            .BackColor = Color.Transparent
        }
        card.Controls.Add(lblCount)
        AddHandler card.Resize,
            Sub()
                cbo.Location = New Point(card.Width - cbo.Width - 24, 16)
                lblCount.Location = New Point(cbo.Left - lblCount.Width - 12, 22)
            End Sub

        Dim list As FlowLayoutPanel = NewRowList(card)
        If loadErr <> "" Then
            list.Controls.Add(EmptyRow(loadErr))
        ElseIf rows.Count = 0 Then
            list.Controls.Add(EmptyRow("Nothing recorded for you in this period."))
        Else
            Dim lastDay As Date = Date.MinValue
            For Each r In rows
                If r.at.Date <> lastDay Then
                    lastDay = r.at.Date
                    list.Controls.Add(DayHeader(lastDay))
                End If
                list.Controls.Add(ActivityRow(r.at, AdminActivityLogPanelBuilder.FriendlyAction(r.action), r.details))
            Next
        End If
        _host.Controls.Add(wrap)
    End Sub

    Private Shared Function DayHeader(d As Date) As Control
        Dim caption As String =
            If(d = DateTime.Today, "Today",
               If(d = DateTime.Today.AddDays(-1), "Yesterday", d.ToString("dddd, MMMM d, yyyy")))
        Return New Label With {
            .Text = caption.ToUpperInvariant(), .Font = New Font("Segoe UI", 8.5F, FontStyle.Bold),
            .ForeColor = FaintText, .AutoSize = False, .Height = 30,
            .TextAlign = ContentAlignment.BottomLeft, .Padding = New Padding(14, 0, 0, 4),
            .Margin = New Padding(0), .BackColor = Color.Transparent
        }
    End Function

    Private Shared Function ActivityRow(at As DateTime, action As String, details As String) As Control
        Dim p As New BufferedPanel With {.Height = 44, .Margin = New Padding(0), .BackColor = Color.Transparent}
        AddHandler p.Paint,
            Sub(s, e)
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 12, p.Height - 1, p.Width - 12, p.Height - 1)
                End Using
            End Sub
        Dim lblTime As New Label With {
            .Text = at.ToString("h:mm tt"), .Font = New Font("Segoe UI", 10), .ForeColor = FaintText,
            .AutoSize = True, .Location = New Point(14, 12), .BackColor = Color.Transparent}
        Dim lblAction As New Label With {
            .Text = action, .Font = New Font("Segoe UI Semibold", 10.5F), .ForeColor = MintDark,
            .AutoSize = True, .Location = New Point(110, 11), .BackColor = Color.Transparent}
        Dim lblDetails As New Label With {
            .Text = details, .Font = New Font("Segoe UI", 10), .ForeColor = SubText,
            .AutoSize = False, .AutoEllipsis = True, .Location = New Point(330, 12),
            .Height = 22, .BackColor = Color.Transparent}
        p.Controls.AddRange({lblTime, lblAction, lblDetails})
        AddHandler p.Resize, Sub() lblDetails.Width = Math.Max(60, p.Width - lblDetails.Left - 16)
        Return p
    End Function

    ' =========================================================================
    '  PAYMENT METHODS (cashier) - view, and request an add or a removal.
    '  The admin approves or rejects in Settings > Payment Requests.
    ' =========================================================================
    Private Shared Sub BuildPaymentsTab()
        Dim canRequest As Boolean = PaymentRequests.HasTable()
        Dim scroll As Panel = NewScrollHost()

        ' ── My Requests (added first: Dock=Top stacks in reverse) ──
        Dim mine As List(Of PaymentRequests.Request) = If(canRequest,
            PaymentRequests.ListMine(Session.CurrentUserId), New List(Of PaymentRequests.Request))
        Dim reqRows As New List(Of Control)
        If mine.Count = 0 Then
            reqRows.Add(EmptyRow("You have not made any requests."))
        End If
        For Each r In mine
            Dim status As String = r.Status.ToUpperInvariant()
            Dim third As String
            Select Case r.Status
                Case "Approved" : third = "Approved by " & r.ReviewedBy & ":  " & r.Outcome
                Case "Rejected" : third = "Rejected by " & r.ReviewedBy & ":  " & r.RejectReason
                Case Else : third = "Waiting for the administrator."
            End Select
            reqRows.Add(ThreeLineRow(r.Headline,
                "Your reason: " & r.Reason & "   -   " & r.RequestedAt.ToString("MMM d, yyyy  h:mm tt"),
                third, StatusBadge(r.Status), Nothing))
        Next
        Dim reqCard As Panel = ListCard("My Requests",
            "What you asked for, and the administrator's answer.", reqRows, Nothing)

        ' ── Methods ──
        Dim pendingRemoval As HashSet(Of Integer) = PaymentRequests.PendingRemovalIds()
        Dim methodRows As New List(Of Control)
        Dim methods = PaymentMethods.LoadAll(activeOnly:=True)
        If methods.Count = 0 Then methodRows.Add(EmptyRow("No payment methods are set up."))
        For Each m In methods
            Dim meth As PaymentMethods.Method = m
            Dim sub_ As String = PaymentMethods.TypeDescription(m.Type)
            If m.Note.Trim() <> "" Then sub_ &= "   -   " & m.Note.Trim()
            Dim btns As Button() = Nothing
            If canRequest Then
                If pendingRemoval.Contains(m.MethodId) Then
                    Dim waiting As Button = MakeButton("Removal requested", False, 160)
                    waiting.Enabled = False
                    btns = {waiting}
                Else
                    Dim b As Button = MakeButton("Request Removal", False, 150)
                    b.ForeColor = AccentRed
                    AddHandler b.Click, Sub() RequestRemoval(meth)
                    btns = {b}
                End If
            End If
            methodRows.Add(TwoLineRow(m.Name, sub_, btns))
        Next
        Dim btnNew As Button = Nothing
        If canRequest Then
            btnNew = MakeButton("+  Request New Method", True, 190)
            AddHandler btnNew.Click, Sub() RequestNewMethod()
        End If
        Dim methodCard As Panel = ListCard("Payment Methods",
            If(canRequest, "What the till accepts. To add or remove one, send a request to the administrator.",
                           "What the till accepts. Only the administrator can add or change these."),
            methodRows, btnNew)

        scroll.Controls.Add(Spacer())
        If canRequest Then
            scroll.Controls.Add(reqCard)
            scroll.Controls.Add(Spacer())
        End If
        scroll.Controls.Add(methodCard)
        _host.Controls.Add(scroll)
    End Sub

    Private Shared Sub RequestNewMethod()
        Dim r As PaymentMethodModal.Result = PaymentMethodModal.Prompt(
            isEdit:=False, showTypeActive:=PaymentMethods.HasBehaviourColumns(), seed:=Nothing, forRequest:=True)
        If r Is Nothing Then Return
        Dim err As String = PaymentRequests.SubmitAdd(r.Name, r.Type, r.Note, r.Reason)
        If err <> "" Then
            AppModal.Show(err, "Request New Method", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        Else
            AppModal.Show("Your request for """ & r.Name & """ was sent to the administrator." & vbCrLf &
                          "You will see the answer under My Requests.", "Request Sent",
                          MessageBoxButtons.OK, MessageBoxIcon.Information)
        End If
        ShowTab(SettingsTab.Payments)
    End Sub

    Private Shared Sub RequestRemoval(m As PaymentMethods.Method)
        Dim reason As String = AppModal.Prompt(
            "Why should """ & m.Name & """ be removed from the till?" & vbCrLf &
            "The administrator reads this before deciding.", "Request Removal", "")
        If reason Is Nothing Then Return
        If reason.Trim() = "" Then
            AppModal.Show("A reason is required.", "Request Removal", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If
        Dim err As String = PaymentRequests.SubmitRemove(m.MethodId, m.Name, reason)
        If err <> "" Then
            AppModal.Show(err, "Request Removal", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        Else
            AppModal.Show("Your request to remove """ & m.Name & """ was sent to the administrator.",
                          "Request Sent", MessageBoxButtons.OK, MessageBoxIcon.Information)
        End If
        ShowTab(SettingsTab.Payments)
    End Sub

    ' =========================================================================
    '  PAYMENT REQUESTS (admin) - approve, or reject with a reason
    ' =========================================================================
    Private Shared Sub BuildPayRequestsTab()
        Dim scroll As Panel = NewScrollHost()
        Dim pending As List(Of PaymentRequests.Request)
        Dim decided As List(Of PaymentRequests.Request)
        Try
            pending = PaymentRequests.ListPending()
            decided = PaymentRequests.ListDecided()
        Catch ex As Exception
            AppModal.Show("Could not load payment requests: " & ex.Message, "Payment Requests",
                          MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try

        Dim histRows As New List(Of Control)
        If decided.Count = 0 Then histRows.Add(EmptyRow("No decisions yet."))
        For Each r In decided
            histRows.Add(ThreeLineRow(r.Headline,
                "Asked by " & r.RequestedBy & ":  " & r.Reason,
                r.Status & " by " & r.ReviewedBy & "  " & r.ReviewedAt.ToString("MMM d, h:mm tt") & "  -  " &
                If(r.Status = "Rejected", r.RejectReason, r.Outcome),
                StatusBadge(r.Status), Nothing))
        Next
        Dim histCard As Panel = ListCard("Decided", "The last 50 requests you or another admin answered.", histRows, Nothing)

        Dim pendRows As New List(Of Control)
        If pending.Count = 0 Then pendRows.Add(EmptyRow("Nothing waiting. Cashiers' requests will appear here."))
        For Each r In pending
            Dim req As PaymentRequests.Request = r
            Dim btnOk As Button = MakeButton("Approve", True, 100)
            Dim btnNo As Button = MakeButton("Reject", False, 90)
            btnNo.ForeColor = AccentRed
            AddHandler btnOk.Click, Sub() ApproveRequest(req)
            AddHandler btnNo.Click, Sub() RejectRequest(req)
            Dim detail As String = "Asked by " & req.RequestedBy & "  -  " & req.RequestedAt.ToString("MMM d, yyyy  h:mm tt")
            If req.Action = "Add" Then
                detail &= "  -  " & PaymentMethods.TypeDescription(req.Type)
                If req.Note <> "" Then detail &= "  -  note: " & req.Note
            End If
            pendRows.Add(ThreeLineRow(req.Headline, detail, "Why: " & req.Reason,
                                      StatusBadge("Pending"), {btnOk, btnNo}))
        Next
        Dim pendCard As Panel = ListCard("Waiting for you",
            "Approve makes the change straight away. Reject needs a reason - the cashier sees it.", pendRows, Nothing)

        scroll.Controls.Add(Spacer())
        scroll.Controls.Add(histCard)
        scroll.Controls.Add(Spacer())
        scroll.Controls.Add(pendCard)
        _host.Controls.Add(scroll)
    End Sub

    Private Shared Sub ApproveRequest(req As PaymentRequests.Request)
        If AppModal.Show("Approve:  " & req.Headline.Replace("  ", " ") & "?" & vbCrLf & vbCrLf &
                         "Requested by " & req.RequestedBy & vbCrLf & "Why: " & req.Reason,
                         "Approve Request", MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then
            Return
        End If
        Dim outcome As String = ""
        Dim err As String = PaymentRequests.Approve(req, outcome)
        If err <> "" Then
            AppModal.Show(err, "Approve Request", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        Else
            AppModal.Show("Approved. " & outcome, "Request Approved", MessageBoxButtons.OK, MessageBoxIcon.Information)
        End If
        Render()
        ShowTab(SettingsTab.PayRequests)
    End Sub

    Private Shared Sub RejectRequest(req As PaymentRequests.Request)
        Dim reason As String = AppModal.Prompt(
            "Why is """ & req.Headline.Replace("  ", " ") & """ rejected?" & vbCrLf &
            req.RequestedBy & " will see this reason.", "Reject Request", "")
        If reason Is Nothing Then Return
        Dim err As String = PaymentRequests.Reject(req, reason)
        If err <> "" Then
            AppModal.Show(err, "Reject Request", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If
        Render()
        ShowTab(SettingsTab.PayRequests)
    End Sub

    Private Shared Function StatusBadge(status As String) As Label
        Dim fore As Color, back As Color
        Select Case status
            Case "Approved" : fore = Color.FromArgb(0, 120, 92) : back = Color.FromArgb(222, 246, 238)
            Case "Rejected" : fore = Color.FromArgb(180, 40, 40) : back = Color.FromArgb(255, 233, 233)
            Case Else : fore = Color.FromArgb(170, 105, 20) : back = Color.FromArgb(255, 243, 224)
        End Select
        Return New Label With {
            .Text = status, .Font = New Font("Segoe UI", 8.5F, FontStyle.Bold), .ForeColor = fore,
            .BackColor = back, .AutoSize = True, .Padding = New Padding(8, 3, 8, 3)}
    End Function

    ' =========================================================================
    '  DATABASE BACKUP (admin)
    ' =========================================================================
    Private Shared Sub BuildBackupTab()
        Dim card As Panel = NewCard("Database Backup", 0)
        Dim wrap As Panel = FillCardWrap(card, topBand:=150)

        Dim lblInfo As New Label With {
            .Text = "A backup is a full copy of the database (.sql file). Keep copies on a USB " &
                    "drive or another computer, not only on this PC." & vbCrLf &
                    "Restoring replaces ALL current data with the backup. A safety copy of the " &
                    "current data is made automatically first.",
            .Font = New Font("Segoe UI", 9.5F), .ForeColor = SubText, .AutoSize = False,
            .Location = New Point(24, 52), .Size = New Size(800, 44), .BackColor = Color.Transparent}
        Dim lblFolder As New Label With {
            .Text = "Folder:  " & _backupFolder, .Font = New Font("Segoe UI Semibold", 9.5F),
            .ForeColor = MintDark, .AutoSize = False, .AutoEllipsis = True,
            .Location = New Point(24, 110), .Size = New Size(500, 22), .BackColor = Color.Transparent}
        Dim btnNow As Button = MakeButton("Back Up Now", True, 140)
        Dim btnFolder As Button = MakeButton("Change Folder", False, 130)
        Dim btnOpen As Button = MakeButton("Open Folder", False, 120)
        Dim btnFile As Button = MakeButton("Restore from File...", False, 170)
        card.Controls.AddRange({lblInfo, lblFolder, btnNow, btnFolder, btnOpen, btnFile})
        AddHandler card.Resize,
            Sub()
                lblInfo.Width = Math.Max(200, card.Width - 48)
                btnNow.Location = New Point(card.Width - btnNow.Width - 24, 104)
                btnFolder.Location = New Point(btnNow.Left - btnFolder.Width - 8, 104)
                btnOpen.Location = New Point(btnFolder.Left - btnOpen.Width - 8, 104)
                btnFile.Location = New Point(btnOpen.Left - btnFile.Width - 8, 104)
                lblFolder.Width = Math.Max(120, btnFile.Left - 36)
            End Sub

        AddHandler btnNow.Click,
            Sub()
                Dim err As String = ""
                Dim path As String = WithWait(Function() Backup.Create(_backupFolder, err))
                If err <> "" Then
                    AppModal.Show(err, "Backup", MessageBoxButtons.OK, MessageBoxIcon.Error)
                Else
                    AppModal.Show("Backup saved:" & vbCrLf & path, "Backup Complete",
                                  MessageBoxButtons.OK, MessageBoxIcon.Information)
                End If
                ShowTab(SettingsTab.Backup)
            End Sub
        AddHandler btnFolder.Click,
            Sub()
                Using dlg As New FolderBrowserDialog With {
                        .Description = "Choose where backups are saved", .SelectedPath = _backupFolder}
                    If dlg.ShowDialog() = DialogResult.OK Then
                        _backupFolder = dlg.SelectedPath
                        ShowTab(SettingsTab.Backup)
                    End If
                End Using
            End Sub
        AddHandler btnOpen.Click,
            Sub()
                Try
                    Directory.CreateDirectory(_backupFolder)
                    Process.Start("explorer.exe", """" & _backupFolder & """")
                Catch ex As Exception
                    AppModal.Show("Could not open the folder: " & ex.Message, "Backup",
                                  MessageBoxButtons.OK, MessageBoxIcon.Error)
                End Try
            End Sub
        AddHandler btnFile.Click,
            Sub()
                Using dlg As New OpenFileDialog With {
                        .Filter = "SQL backup (*.sql)|*.sql", .InitialDirectory = _backupFolder,
                        .Title = "Choose a backup to restore"}
                    If dlg.ShowDialog() = DialogResult.OK Then DoRestore(dlg.FileName)
                End Using
            End Sub

        Dim list As FlowLayoutPanel = NewRowList(card)
        Dim files As List(Of FileInfo) = Backup.ListBackups(_backupFolder)
        If files.Count = 0 Then
            list.Controls.Add(EmptyRow("No backups in this folder yet. Press Back Up Now."))
        End If
        For Each fi In files
            Dim f As FileInfo = fi
            Dim btn As Button = MakeButton("Restore", False, 90)
            AddHandler btn.Click, Sub() DoRestore(f.FullName)
            list.Controls.Add(TwoLineRow(f.Name,
                f.LastWriteTime.ToString("MMM d, yyyy  h:mm tt") & "   -   " & SizeText(f.Length),
                {btn}))
        Next
        _host.Controls.Add(wrap)
    End Sub

    Private Shared Sub DoRestore(file As String)
        If AppModal.Show("Restore the database from:" & vbCrLf & Path.GetFileName(file) & vbCrLf & vbCrLf &
                         "ALL current data (sales, stock, users...) will be replaced by this backup. " &
                         "Anything recorded after it was made will be lost." & vbCrLf & vbCrLf &
                         "A safety copy of the current data is made first. Continue?",
                         "Restore Database", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then
            Return
        End If
        Dim typed As String = AppModal.Prompt("Type RESTORE to confirm.", "Restore Database", "")
        If typed Is Nothing OrElse typed.Trim().ToUpperInvariant() <> "RESTORE" Then
            AppModal.Show("Restore cancelled. Nothing was changed.", "Restore Database",
                          MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If

        Dim safety As String = ""
        Dim err As String = WithWait(Function() Backup.RestoreFrom(file, safety))
        If err <> "" Then
            AppModal.Show(err & If(safety <> "", vbCrLf & vbCrLf & "Safety copy: " & safety, ""),
                          "Restore Failed", MessageBoxButtons.OK, MessageBoxIcon.Error)
        Else
            AppModal.Show("The database was restored." & vbCrLf & vbCrLf &
                          "Safety copy of the data before the restore:" & vbCrLf & safety & vbCrLf & vbCrLf &
                          "Sign out and back in on every computer so they reload the data.",
                          "Restore Complete", MessageBoxButtons.OK, MessageBoxIcon.Information)
        End If
        ShowTab(SettingsTab.Backup)
    End Sub

    Private Shared Function SizeText(bytes As Long) As String
        If bytes >= 1048576 Then Return (bytes / 1048576.0).ToString("0.0") & " MB"
        Return Math.Max(1, bytes \ 1024).ToString("N0") & " KB"
    End Function

    ' =========================================================================
    '  RECYCLE BIN (admin)
    ' =========================================================================
    Private Shared Sub BuildRecycleTab()
        Dim card As Panel = NewCard("Recycle Bin", 0)
        Dim wrap As Panel = FillCardWrap(card)
        Dim hint As New Label With {
            .Text = "Everything deleted in the system lands here first. Restore puts it back.",
            .Font = New Font("Segoe UI", 9.5F), .ForeColor = FaintText, .AutoSize = True,
            .BackColor = Color.Transparent}
        card.Controls.Add(hint)
        AddHandler card.Resize, Sub() hint.Location = New Point(Math.Max(200, card.Width - hint.Width - 24), 22)

        Dim list As FlowLayoutPanel = NewRowList(card)
        If Not Archive.HasArchiveTable() Then
            list.Controls.Add(EmptyRow("The Recycle Bin is not set up yet. Run migrations.sql section 18."))
            _host.Controls.Add(wrap)
            Return
        End If

        Dim items As List(Of Archive.Entry)
        Try
            items = Archive.ListArchived()
        Catch ex As Exception
            list.Controls.Add(EmptyRow("Could not load the Recycle Bin: " & ex.Message))
            _host.Controls.Add(wrap)
            Return
        End Try

        If items.Count = 0 Then list.Controls.Add(EmptyRow("The Recycle Bin is empty."))
        For Each it In items
            Dim entry As Archive.Entry = it
            Dim btnRestore As Button = MakeButton("Restore", True, 90)
            Dim btnPurge As Button = MakeButton("Delete Forever", False, 120)
            btnPurge.ForeColor = AccentRed
            AddHandler btnRestore.Click,
                Sub()
                    Dim err As String = Archive.Restore(entry.ArchiveId)
                    If err <> "" Then
                        AppModal.Show(err, "Restore", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    Else
                        AppModal.Show(Archive.KindName(entry.SourceTable) & " """ & entry.Label &
                                      """ was restored.", "Restored",
                                      MessageBoxButtons.OK, MessageBoxIcon.Information)
                    End If
                    ShowTab(SettingsTab.RecycleBin)
                End Sub
            AddHandler btnPurge.Click,
                Sub()
                    If AppModal.Show("Delete """ & entry.Label & """ forever?" & vbCrLf & vbCrLf &
                                     "It can no longer be restored after this.",
                                     "Delete Forever", MessageBoxButtons.YesNo,
                                     MessageBoxIcon.Warning) <> DialogResult.Yes Then Return
                    Dim err As String = Archive.Purge(entry.ArchiveId)
                    If err <> "" Then AppModal.Show(err, "Delete Forever", MessageBoxButtons.OK, MessageBoxIcon.Error)
                    ShowTab(SettingsTab.RecycleBin)
                End Sub
            list.Controls.Add(TwoLineRow(
                Archive.KindName(entry.SourceTable) & ":  " & entry.Label,
                "Deleted by " & entry.DeletedBy & "  -  " & entry.DeletedAt.ToString("MMM d, yyyy  h:mm tt"),
                {btnRestore, btnPurge}))
        Next
        _host.Controls.Add(wrap)
    End Sub

    ' =========================================================================
    '  BUILDING BLOCKS
    ' =========================================================================
    Private Shared Function NewScrollHost() As Panel
        Return New Panel With {
            .Dock = DockStyle.Fill, .BackColor = MintBg, .AutoScroll = True,
            .Padding = New Padding(SIDE_PAD, 4, SIDE_PAD, SIDE_PAD)}
    End Function

    Private Shared Function Spacer() As Panel
        Return New Panel With {.Dock = DockStyle.Top, .Height = 16, .BackColor = MintBg}
    End Function

    ' A white card with a title. height = 0 means the caller docks it to fill.
    Private Shared Function NewCard(title As String, height As Integer) As Panel
        Dim card As New BufferedPanel With {.BackColor = CardBg}
        If height > 0 Then
            card.Dock = DockStyle.Top
            card.Height = height
        End If
        AddHandler card.Paint, Sub(s, e) PanelRenderer.Draw(CType(s, Panel), e, True)
        card.Controls.Add(New Label With {
            .Text = title, .Font = New Font("Segoe UI Semibold", 13), .ForeColor = MintDark,
            .AutoSize = True, .Location = New Point(24, 18), .BackColor = Color.Transparent})
        Return card
    End Function

    ' Places a fill-height card inside the page padding. topBand is how much of
    ' the card sits above its row list.
    Private Shared Function FillCardWrap(card As Panel, Optional topBand As Integer = 60) As Panel
        Dim wrap As New Panel With {
            .Dock = DockStyle.Fill, .BackColor = MintBg,
            .Padding = New Padding(SIDE_PAD, 4, SIDE_PAD, SIDE_PAD)}
        ' Changing Padding shifts the controls already inside the card (its
        ' title) by the same amount, which pushed the title down behind the
        ' list. Put it back where NewCard placed it.
        Dim title As Label = card.Controls.OfType(Of Label)().FirstOrDefault()
        card.Dock = DockStyle.Fill
        card.Padding = New Padding(12, topBand, 12, 12)
        If title IsNot Nothing Then title.Location = New Point(24, 18)
        wrap.Controls.Add(card)
        Return wrap
    End Function

    ' A scrolling top-down list filling the card below its header band. Rows
    ' are kept at the list's width as it resizes.
    Private Shared Function NewRowList(card As Panel) As FlowLayoutPanel
        Dim list As New FlowLayoutPanel With {
            .Dock = DockStyle.Fill, .FlowDirection = FlowDirection.TopDown, .WrapContents = False,
            .AutoScroll = True, .BackColor = Color.Transparent, .Margin = New Padding(0)}
        AddHandler list.ControlAdded, Sub(s, e) e.Control.Width = RowWidth(list)
        AddHandler list.Resize,
            Sub()
                Dim w As Integer = RowWidth(list)
                For Each c As Control In list.Controls
                    c.Width = w
                Next
            End Sub
        card.Controls.Add(list)
        list.BringToFront()
        Return list
    End Function

    Private Shared Function RowWidth(list As FlowLayoutPanel) As Integer
        Return Math.Max(200, list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 2)
    End Function

    Private Shared Function EmptyRow(message As String) As Control
        Return New Label With {
            .Text = message, .Font = New Font("Segoe UI", 10.5F), .ForeColor = FaintText,
            .AutoSize = False, .Height = 60, .TextAlign = ContentAlignment.MiddleLeft,
            .Padding = New Padding(14, 0, 0, 0), .BackColor = Color.Transparent}
    End Function

    ' A Dock=Top card sized to its rows (no inner scrolling - the page scrolls).
    Private Shared Function ListCard(title As String, hint As String, rows As List(Of Control),
                                     headerButton As Button) As Panel
        Dim rowsH As Integer = rows.Sum(Function(r) r.Height)
        Dim card As Panel = NewCard(title, 62 + rowsH + 16)
        Dim lblHint As New Label With {
            .Text = hint, .Font = New Font("Segoe UI", 9.5F), .ForeColor = FaintText,
            .AutoSize = True, .Location = New Point(26, 44), .BackColor = Color.Transparent}
        card.Height += 18
        card.Controls.Add(lblHint)
        If headerButton IsNot Nothing Then card.Controls.Add(headerButton)
        Dim y As Integer = 76
        For Each r In rows
            r.Location = New Point(12, y)
            card.Controls.Add(r)
            y += r.Height
        Next
        AddHandler card.Resize,
            Sub()
                For Each r In rows
                    r.Width = card.Width - 24
                Next
                If headerButton IsNot Nothing Then headerButton.Location = New Point(card.Width - headerButton.Width - 24, 18)
            End Sub
        Return card
    End Function

    ' Title (+ badge), grey detail line, a third line for the reason/answer,
    ' optional buttons on the right.
    Private Shared Function ThreeLineRow(title As String, line2 As String, line3 As String,
                                         badge As Label, buttons As Button()) As Control
        Dim p As New BufferedPanel With {.Height = 80, .Margin = New Padding(0), .BackColor = Color.Transparent}
        AddHandler p.Paint,
            Sub(s, e)
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 12, p.Height - 1, p.Width - 12, p.Height - 1)
                End Using
            End Sub
        Dim lblTitle As New Label With {
            .Text = title, .Font = New Font("Segoe UI", 11, FontStyle.Bold), .ForeColor = MintDark,
            .AutoSize = True, .Location = New Point(14, 8), .BackColor = Color.Transparent, .UseMnemonic = False}
        Dim l2 As New Label With {
            .Text = line2, .Font = New Font("Segoe UI", 9.5F), .ForeColor = FaintText,
            .AutoSize = False, .AutoEllipsis = True, .Height = 20, .Location = New Point(14, 33),
            .BackColor = Color.Transparent, .UseMnemonic = False}
        Dim l3 As New Label With {
            .Text = line3, .Font = New Font("Segoe UI", 9.5F), .ForeColor = SubText,
            .AutoSize = False, .AutoEllipsis = True, .Height = 20, .Location = New Point(14, 54),
            .BackColor = Color.Transparent, .UseMnemonic = False}
        p.Controls.AddRange({lblTitle, l2, l3})
        If badge IsNot Nothing Then p.Controls.Add(badge)
        If buttons IsNot Nothing Then p.Controls.AddRange(buttons)
        Dim tips As New ToolTip()
        tips.SetToolTip(l2, line2)
        tips.SetToolTip(l3, line3)
        AddHandler p.Resize,
            Sub()
                Dim x As Integer = p.Width - 14
                If buttons IsNot Nothing Then
                    For i As Integer = buttons.Length - 1 To 0 Step -1
                        x -= buttons(i).Width
                        buttons(i).Location = New Point(x, (p.Height - buttons(i).Height) \ 2)
                        x -= 8
                    Next
                End If
                If badge IsNot Nothing Then badge.Location = New Point(lblTitle.Right + 10, 10)
                l2.Width = Math.Max(60, x - 22)
                l3.Width = l2.Width
            End Sub
        Return p
    End Function

    ' Bold title, grey sub-line, optional buttons on the right.
    Private Shared Function TwoLineRow(title As String, subLine As String, buttons As Button()) As Control
        Dim p As New BufferedPanel With {.Height = 60, .Margin = New Padding(0), .BackColor = Color.Transparent}
        AddHandler p.Paint,
            Sub(s, e)
                Using pen As New Pen(RowDivider, 1)
                    e.Graphics.DrawLine(pen, 12, p.Height - 1, p.Width - 12, p.Height - 1)
                End Using
            End Sub
        Dim lblTitle As New Label With {
            .Text = title, .Font = New Font("Segoe UI", 11, FontStyle.Bold), .ForeColor = MintDark,
            .AutoSize = False, .AutoEllipsis = True, .Height = 22, .Location = New Point(14, 9),
            .BackColor = Color.Transparent, .UseMnemonic = False}
        Dim lblSub As New Label With {
            .Text = subLine, .Font = New Font("Segoe UI", 9.5F), .ForeColor = FaintText,
            .AutoSize = False, .AutoEllipsis = True, .Height = 20, .Location = New Point(14, 32),
            .BackColor = Color.Transparent, .UseMnemonic = False}
        p.Controls.AddRange({lblTitle, lblSub})
        If buttons IsNot Nothing Then p.Controls.AddRange(buttons)
        AddHandler p.Resize,
            Sub()
                Dim x As Integer = p.Width - 14
                If buttons IsNot Nothing Then
                    For i As Integer = buttons.Length - 1 To 0 Step -1
                        x -= buttons(i).Width
                        buttons(i).Location = New Point(x, (p.Height - buttons(i).Height) \ 2)
                        x -= 8
                    Next
                End If
                lblTitle.Width = Math.Max(60, x - 22)
                lblSub.Width = lblTitle.Width
            End Sub
        Return p
    End Function

    Private Shared Function MakeButton(text As String, primary As Boolean, width As Integer) As Button
        Dim b As New Button With {
            .Text = text, .Size = New Size(width, 36), .FlatStyle = FlatStyle.Flat,
            .Font = New Font("Segoe UI Semibold", 9.5F), .Cursor = Cursors.Hand,
            .BackColor = If(primary, MintPrimary, Color.FromArgb(220, 235, 232)),
            .ForeColor = If(primary, Color.White, MintDark)}
        b.FlatAppearance.BorderSize = 0
        Dim normal As Color = b.BackColor
        Dim hover As Color = If(primary, MintDark, Color.FromArgb(205, 228, 223))
        AddHandler b.Paint, AddressOf PaintPill
        AddHandler b.MouseEnter, Sub()
                                     b.BackColor = hover
                                     If primary Then b.ForeColor = Color.White
                                     b.Invalidate()
                                 End Sub
        AddHandler b.MouseLeave, Sub()
                                     b.BackColor = normal
                                     b.Invalidate()
                                 End Sub
        Return b
    End Function

    Private Shared Sub PaintPill(s As Object, e As PaintEventArgs)
        Dim b = CType(s, Button)
        Dim g = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias
        Using bg As New SolidBrush(If(b.Parent IsNot Nothing AndAlso TypeOf b.Parent Is FlowLayoutPanel,
                                      MintBg, CardBg))
            g.FillRectangle(bg, b.ClientRectangle)
        End Using
        Dim rect = b.ClientRectangle : rect.Inflate(-1, -1)
        Using br As New SolidBrush(b.BackColor)
            g.FillRoundedRectangle(br, rect, 8)
        End Using
        ' NoPrefix: "Store & Payments" must not turn the & into an underline.
        TextRenderer.DrawText(g, b.Text, b.Font, b.ClientRectangle, b.ForeColor,
            TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPrefix)
    End Sub

    ' Runs slow work (backup/restore) under a wait cursor.
    Private Shared Function WithWait(Of T)(work As Func(Of T)) As T
        Dim f As Form = If(_container IsNot Nothing, _container.FindForm(), Nothing)
        If f IsNot Nothing Then f.UseWaitCursor = True
        Cursor.Current = Cursors.WaitCursor
        Application.DoEvents()
        Try
            Return work()
        Finally
            If f IsNot Nothing Then f.UseWaitCursor = False
            Cursor.Current = Cursors.Default
        End Try
    End Function

End Class
