Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports MySql.Data.MySqlClient

' =============================================================================
'  LowStockAlert
'
'  The low-stock warning shown outside the Low Stock Alerts page itself:
'    - a red count badge painted on a nav button (Inventory: Low Stock Alerts,
'      Procurement: Stock Level)
'    - a one-time notice when an Inventory user logs in
'
'  The count uses the SAME rule as the Low Stock Alerts page and the Admin
'  dashboard's Low Stock tile (Low + Critical + Out), so the badge number always
'  matches the number of rows that page lists.
' =============================================================================
Public Module LowStockAlert

    Private ReadOnly BadgeRed As Color = Color.FromArgb(211, 47, 47)

    ' Products that need restocking. Returns -1 when the database cannot be
    ' read, so callers can hide the badge instead of showing a wrong 0.
    Public Function CountLowStock() As Integer
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT COUNT(*) FROM products " &
                    "WHERE is_active = 1 AND stock_qty * 100 <= reorder_level * 125", conn)
                    Return Convert.ToInt32(cmd.ExecuteScalar())
                End Using
            End Using
        Catch ex As Exception
            ErrorLog.Write("LowStockAlert.CountLowStock", ex)
            Return -1
        End Try
    End Function

    ' Paints the badge on top of a nav button. Wide rail: a pill at the right
    ' end of the caption row. Collapsed icon rail: a small dot-count on the
    ' icon's top-right corner. Nothing is drawn for 0 or an unreadable count.
    Public Sub PaintBadge(btn As Button, e As PaintEventArgs, count As Integer, collapsed As Boolean)
        If count <= 0 Then Return
        Dim text As String = If(count > 99, "99+", count.ToString())
        Dim g = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias

        Using f As New Font("Segoe UI", If(collapsed, 7.5F, 9.0F), FontStyle.Bold)
            Dim sz As Size = TextRenderer.MeasureText(text, f, Size.Empty, TextFormatFlags.NoPadding)
            Dim h As Integer = If(collapsed, 16, 22)
            Dim w As Integer = Math.Max(h, sz.Width + If(collapsed, 8, 14))
            Dim rect As Rectangle
            If collapsed Then
                rect = New Rectangle(btn.Width \ 2 + 2, 3, w, h)
            Else
                rect = New Rectangle(btn.Width - w - 8, (btn.Height - h) \ 2, w, h)
            End If
            Using path As New GraphicsPath()
                Dim d As Integer = h
                path.AddArc(rect.X, rect.Y, d, d, 90, 180)
                path.AddArc(rect.Right - d, rect.Y, d, d, 270, 180)
                path.CloseFigure()
                Using br As New SolidBrush(BadgeRed)
                    g.FillPath(br, path)
                End Using
            End Using
            TextRenderer.DrawText(g, text, f, rect, Color.White,
                TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPadding)
        End Using
    End Sub

    ' Login notice. Returns True when the user chose to open the list now.
    Public Function ShowLoginNotice(count As Integer) As Boolean
        If count <= 0 Then Return False
        Dim what As String = If(count = 1, "1 item needs", count & " items need")
        Return AppModal.Show(what & " restocking (low, critical or out of stock)." &
                             Environment.NewLine & Environment.NewLine &
                             "Open Low Stock Alerts now?",
                             "Low Stock Alert", MessageBoxButtons.YesNo,
                             MessageBoxIcon.Warning) = DialogResult.Yes
    End Function

End Module
