Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms

' =============================================================================
'  OpeningCashModal - the drawer count at the START of a shift.
'
'  The opening cash used to be one number typed into a prompt, checked against
'  nothing. A cashier could take ₱500 out of a drawer the last shift left at
'  ₱1,000 and simply declare ₱500 -- the shortage vanished before the shift
'  began, and the previous cashier's clean close hid it.
'
'  Now the cashier counts bills and coins (the same denominations as the Cash
'  Drawer) and the window shows what the LAST shift left:
'
'    less than that   -> the shift cannot start (recount; cash that leaves the
'                        drawer must go through Drop to Safe before closing)
'    the same or more -> it starts; cash ADDED to the drawer is allowed and
'                        recorded on the shift by the caller.
' =============================================================================
Public Module OpeningCashModal

    Public Class Result
        Public Counted As Decimal
    End Class

    Private ReadOnly CardBg As Color = Color.White
    Private ReadOnly TitleClr As Color = Color.FromArgb(25, 40, 35)
    Private ReadOnly SubText As Color = Theme.SubText
    Private ReadOnly BorderClr As Color = Color.FromArgb(160, 195, 185)
    Private ReadOnly DividerClr As Color = Color.FromArgb(230, 238, 235)
    Private ReadOnly OkClr As Color = Theme.Deep
    Private ReadOnly ShortClr As Color = Color.FromArgb(200, 65, 65)
    Private ReadOnly OverClr As Color = Theme.Warning
    Private ReadOnly Peso As String = ChrW(8369)

    Private Const CARD_W As Integer = 560
    Private Const PAD As Integer = 24
    Private Const ROW_H As Integer = 34

    ' expected: what the last shift on this register left (Nothing = no earlier
    ' count to compare with). lastInfo: who left it and when, for the header.
    Public Function Show(expected As Decimal?, lastInfo As String) As Result
        Dim getResult As Func(Of Result) = Nothing
        Dim f As Form = Build(expected, lastInfo, getResult)
        Try
            Dim owner As Form = Form.ActiveForm
            If owner Is Nothing OrElse owner Is f Then
                f.StartPosition = FormStartPosition.CenterScreen
                f.ShowDialog()
            Else
                f.ShowDialog(owner)
            End If
            ' Nothing only if Windows closed the window (e.g. shutting down):
            ' the caller then opens no shift rather than one with no count.
            Return getResult()
        Finally
            f.Dispose()
        End Try
    End Function

    ' Builds the window without showing it. getResult returns Nothing until
    ' Start Shift has been accepted. Separate from Show so the check can be
    ' exercised without putting a window on screen.
    Public Function Build(expected As Decimal?, lastInfo As String,
                          ByRef getResult As Func(Of Result)) As Form
        Dim bills() As Decimal = {1000D, 500D, 200D, 100D, 50D, 20D}
        Dim coins() As Decimal = {20D, 10D, 5D, 1D, 0.25D}
        Dim boxes As New List(Of (value As Decimal, box As TextBox))

        Dim f As New Form With {
            .FormBorderStyle = FormBorderStyle.None,
            .StartPosition = FormStartPosition.CenterParent,
            .BackColor = CardBg, .ShowInTaskbar = False, .Width = CARD_W, .KeyPreview = True
        }

        Dim lblTitle As New Label With {
            .Text = "Start of Shift - Count the Drawer",
            .Font = New Font("Segoe UI Semibold", 13.0F), .ForeColor = TitleClr,
            .AutoSize = True, .Location = New Point(PAD, PAD), .BackColor = Color.Transparent
        }
        Dim lblLast As New Label With {
            .Text = If(expected.HasValue,
                       "The last shift left " & Peso & expected.Value.ToString("N2") & " in the drawer" &
                       If(lastInfo <> "", " (" & lastInfo & ")", "") & ".",
                       "No earlier shift count to compare with." & If(lastInfo <> "", " " & lastInfo, "")),
            .Font = New Font("Segoe UI Semibold", 10.0F), .ForeColor = TitleClr,
            .AutoSize = False, .Size = New Size(CARD_W - PAD * 2, 40),
            .Location = New Point(PAD, lblTitle.Bottom + 8), .BackColor = Color.Transparent
        }
        Dim lblHow As New Label With {
            .Text = "Count every bill and coin now in the drawer and type how many of each.",
            .Font = New Font("Segoe UI", 9.5F), .ForeColor = SubText, .AutoSize = True,
            .Location = New Point(PAD, lblLast.Bottom + 2), .BackColor = Color.Transparent
        }
        f.Controls.AddRange({lblTitle, lblLast, lblHow})

        ' Two columns: bills left, coins right.
        Dim gridTop As Integer = lblHow.Bottom + 14
        Dim colW As Integer = (CARD_W - PAD * 2 - 20) \ 2
        Dim addColumn =
            Sub(caption As String, values() As Decimal, x As Integer)
                f.Controls.Add(New Label With {
                    .Text = caption, .Font = New Font("Segoe UI Semibold", 9.0F), .ForeColor = SubText,
                    .AutoSize = True, .Location = New Point(x, gridTop), .BackColor = Color.Transparent})
                Dim y As Integer = gridTop + 22
                For Each v In values
                    f.Controls.Add(New Label With {
                        .Text = Peso & If(v < 1D, v.ToString("0.00"), v.ToString("N0")),
                        .Font = New Font("Segoe UI", 10.5F), .ForeColor = TitleClr,
                        .AutoSize = False, .Size = New Size(80, 26), .TextAlign = ContentAlignment.MiddleLeft,
                        .Location = New Point(x, y), .BackColor = Color.Transparent})
                    f.Controls.Add(New Label With {
                        .Text = "x", .Font = New Font("Segoe UI", 9.5F), .ForeColor = SubText,
                        .AutoSize = True, .Location = New Point(x + 84, y + 5), .BackColor = Color.Transparent})
                    Dim tb As New TextBox With {
                        .Font = New Font("Segoe UI", 10.5F), .Width = 70, .Text = "0",
                        .TextAlign = HorizontalAlignment.Right, .MaxLength = 5,
                        .Location = New Point(x + 102, y), .BorderStyle = BorderStyle.FixedSingle}
                    ' Whole numbers only: a count of notes cannot be fractional.
                    AddHandler tb.KeyPress,
                        Sub(s As Object, e As KeyPressEventArgs)
                            If Not Char.IsDigit(e.KeyChar) AndAlso Not Char.IsControl(e.KeyChar) Then e.Handled = True
                        End Sub
                    AddHandler tb.Enter, Sub() tb.SelectAll()
                    f.Controls.Add(tb)
                    boxes.Add((v, tb))
                    y += ROW_H
                Next
            End Sub
        addColumn("BILLS", bills, PAD)
        addColumn("COINS", coins, PAD + colW + 20)

        Dim totalTop As Integer = gridTop + 22 + bills.Length * ROW_H + 8
        Dim lblTotal As New Label With {
            .Font = New Font("Segoe UI Semibold", 14.0F), .ForeColor = TitleClr,
            .AutoSize = True, .Location = New Point(PAD, totalTop), .BackColor = Color.Transparent}
        Dim lblDiff As New Label With {
            .Font = New Font("Segoe UI Semibold", 10.0F), .AutoSize = False,
            .Size = New Size(CARD_W - PAD * 2, 22), .Location = New Point(PAD, totalTop + 32),
            .BackColor = Color.Transparent}
        f.Controls.AddRange({lblTotal, lblDiff})

        Dim lblErr As New Label With {
            .Text = "", .Font = New Font("Segoe UI Semibold", 9.5F), .ForeColor = ShortClr,
            .AutoSize = False, .Size = New Size(CARD_W - PAD * 2, 40),
            .Location = New Point(PAD, lblDiff.Bottom + 8), .BackColor = Color.Transparent}
        f.Controls.Add(lblErr)

        Dim counted = Function() As Decimal
                          Dim sum As Decimal = 0D
                          For Each b In boxes
                              Dim n As Integer
                              If Integer.TryParse(b.box.Text, n) Then sum += b.value * n
                          Next
                          Return sum
                      End Function

        Dim difference = Function() As Decimal
                             Return If(expected.HasValue, counted() - expected.Value, 0D)
                         End Function

        Dim refresh As Action =
            Sub()
                lblTotal.Text = "Counted:  " & Peso & counted().ToString("N2")
                Dim d As Decimal = difference()
                If Not expected.HasValue Then
                    lblDiff.Text = "Nothing to compare with - this count becomes the starting cash."
                    lblDiff.ForeColor = SubText
                ElseIf d = 0D Then
                    lblDiff.Text = "Matches what the last shift left."
                    lblDiff.ForeColor = OkClr
                ElseIf d < 0D Then
                    lblDiff.Text = Peso & Math.Abs(d).ToString("N2") & " SHORT compared with what the last shift left."
                    lblDiff.ForeColor = ShortClr
                Else
                    lblDiff.Text = Peso & d.ToString("N2") & " added on top of what the last shift left."
                    lblDiff.ForeColor = OverClr
                End If
                lblErr.Text = ""
            End Sub
        For Each b In boxes
            AddHandler b.box.TextChanged, Sub() refresh()
        Next

        Dim dividerY As Integer = lblErr.Bottom + 8
        Dim btnY As Integer = dividerY + 16
        Dim cardH As Integer = btnY + 40 + PAD
        Dim btnOk As Button = PaymentMethodModal.MakeButton("Start Shift", True,
            New Point(CARD_W - PAD - 140, btnY), New Size(140, 40))
        f.Controls.Add(btnOk)

        Dim out As Result = Nothing
        AddHandler btnOk.Click,
            Sub()
                Dim total As Decimal = counted()
                ' Never less than the last shift left. Cash may be added to the
                ' drawer, but none may leave it between shifts.
                If difference() < 0D Then
                    lblErr.Text = "You cannot start with less than " & Peso & expected.Value.ToString("N2") &
                                  ". Recount the drawer. If cash is really missing, call the admin."
                    boxes(0).box.Focus()
                    Return
                End If
                If total = 0D AndAlso AppModal.Show(
                        "You counted " & Peso & "0.00. Start the shift with an EMPTY drawer?",
                        "Empty Drawer", MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then
                    Return
                End If
                out = New Result With {.Counted = total}
                f.Close()
            End Sub

        f.Height = cardH
        AddHandler f.Paint,
            Sub(s As Object, e As PaintEventArgs)
                Dim g = e.Graphics
                g.SmoothingMode = SmoothingMode.AntiAlias
                Using pen As New Pen(DividerClr, 1.0F)
                    g.DrawLine(pen, PAD, dividerY, f.Width - PAD, dividerY)
                End Using
                Using path As GraphicsPath = PaymentMethodModal.RoundedPath(New Rectangle(0, 0, f.Width - 1, f.Height - 1), 10)
                    Using pen As New Pen(BorderClr, 1.2F)
                        g.DrawPath(pen, path)
                    End Using
                End Using
            End Sub
        Using gp As GraphicsPath = PaymentMethodModal.RoundedPath(New Rectangle(0, 0, CARD_W, cardH), 10)
            f.Region = New Region(gp)
        End Using

        ' No Cancel: a shift cannot start without its opening count, and
        ' closing this window by any other route must not skip the check.
        AddHandler f.FormClosing,
            Sub(s As Object, e As FormClosingEventArgs)
                If out Is Nothing AndAlso e.CloseReason = CloseReason.UserClosing Then e.Cancel = True
            End Sub
        AddHandler f.Shown, Sub() boxes(0).box.Focus()
        refresh()

        getResult = Function() out
        Return f
    End Function

End Module
