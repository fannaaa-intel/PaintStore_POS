Imports System.Drawing.Printing
Imports System.Drawing
Imports System.Windows.Forms

Public Class ReportPrinter

    ' ── ROW MODEL ────────────────────────────────────────────────────────
    Public Enum RowKind
        DataRow
        TwoColRow
        TwoColHeader
        SectionHeader
        TotalRow
        TwoColTotalRow
        HighlightRow
        StatusRow
        Spacer
    End Enum

    Public Class Row
        Public Kind As RowKind = RowKind.DataRow
        Public Label As String = ""
        Public Col1 As String = ""
        Public Col2 As String = ""
        Public IsNegative As Boolean = False
        Public IsGood As Boolean = True
    End Class

    Public Class ReportDoc
        Public Title As String = ""
        Public PeriodLabel As String = ""
        Public PeriodValue As String = ""
        Public Rows As New List(Of Row)
    End Class

    ' ── ROW BUILDERS ─────────────────────────────────────────────────────
    Public Shared Function Data(label As String, value As String, Optional negative As Boolean = False) As Row
        Return New Row With {.Kind = RowKind.DataRow, .Label = label, .Col1 = value, .IsNegative = negative}
    End Function

    Public Shared Function Total(label As String, value As String) As Row
        Return New Row With {.Kind = RowKind.TotalRow, .Label = label, .Col1 = value}
    End Function

    Public Shared Function Highlight(label As String, value As String, Optional negative As Boolean = False) As Row
        Return New Row With {.Kind = RowKind.HighlightRow, .Label = label, .Col1 = value, .IsNegative = negative}
    End Function

    Public Shared Function Section(label As String) As Row
        Return New Row With {.Kind = RowKind.SectionHeader, .Label = label}
    End Function

    Public Shared Function Spacer() As Row
        Return New Row With {.Kind = RowKind.Spacer}
    End Function

    Public Shared Function Status(label As String, isGood As Boolean) As Row
        Return New Row With {.Kind = RowKind.StatusRow, .Label = label, .IsGood = isGood}
    End Function

    Public Shared Function TwoColHeader(c1Header As String, c2Header As String) As Row
        Return New Row With {.Kind = RowKind.TwoColHeader, .Col1 = c1Header, .Col2 = c2Header}
    End Function

    Public Shared Function TwoCol(label As String, col1 As String, col2 As String) As Row
        Return New Row With {.Kind = RowKind.TwoColRow, .Label = label, .Col1 = col1, .Col2 = col2}
    End Function

    Public Shared Function TwoColTotal(label As String, col1 As String, col2 As String) As Row
        Return New Row With {.Kind = RowKind.TwoColTotalRow, .Label = label, .Col1 = col1, .Col2 = col2}
    End Function

    ' ── PALETTE — formal, grayscale only; no color accents ─────────────────
    ' (This grayscale palette is used for the printed/drawn report page itself —
    '  it stays grayscale intentionally for a formal printed-document look.)
    Private Shared ReadOnly Ink As Color = Color.FromArgb(20, 20, 20)            ' headings, rules, totals
    Private Shared ReadOnly InkBody As Color = Color.FromArgb(40, 40, 40)        ' standard figures
    Private Shared ReadOnly InkMuted As Color = Color.FromArgb(95, 95, 95)       ' line-item labels
    Private Shared ReadOnly InkFaint As Color = Color.FromArgb(150, 150, 150)    ' meta text (generated, page no.)
    Private Shared ReadOnly RuleMedium As Color = Color.FromArgb(60, 60, 60)     ' subtotal / section rules
    Private Shared ReadOnly RuleLight As Color = Color.FromArgb(195, 195, 195)   ' minor separators
    Private Shared ReadOnly ReportFont As String = "Times New Roman"

    ' ── BRAND PALETTE — used only for the Preview window chrome (title bar,
    '    Print button), so it matches the Paint Store app's mint theme. ─────
    Private Shared ReadOnly BrandDark As Color = Color.FromArgb(10, 79, 66)      ' title bar / button (rest state)
    Private Shared ReadOnly BrandDeep As Color = Color.FromArgb(0, 130, 109)     ' button hover
    Private Shared ReadOnly BrandPrimary As Color = Color.FromArgb(0, 168, 142)  ' button pressed

    ' ── ENTRY POINT ──────────────────────────────────────────────────────
    ' NOTE on the "Print" button: it reuses this same PrintDocument/OnPrintPage
    ' drawing routine that renders the on-screen preview, so the header and
    ' the grayscale palette are guaranteed to be identical between preview and
    ' the physical printout. The dark title bar you see in the preview window
    ' ("Income Statement") is WinForms chrome on the PreviewForm itself — it
    ' is never part of the drawn page and is not sent to the printer.
    Public Shared Sub Preview(doc As ReportDoc,
                               Optional companyName As String = "PAINT STORE",
                               Optional preparedBy As String = "")
        Dim job As New PrintJob(doc, companyName, preparedBy)
        Dim frm As New PreviewForm(job.PrintDoc, doc.Title)
        frm.ShowDialog()
    End Sub

    ' =========================================================================
    '  PRINT JOB — owns pagination state and draws every page
    ' =========================================================================
    Private Class PrintJob
        Private ReadOnly _doc As ReportDoc
        Private ReadOnly _company As String
        Private ReadOnly _preparedBy As String
        Private _rowIdx As Integer = 0
        Private _pageNo As Integer = 0

        Public ReadOnly PrintDoc As New PrintDocument()

        Public Sub New(doc As ReportDoc, company As String, preparedBy As String)
            _doc = doc
            _company = company
            _preparedBy = preparedBy
            PrintDoc.DocumentName = doc.Title
            PrintDoc.DefaultPageSettings.PaperSize = New PaperSize("Letter", 850, 1100)
            PrintDoc.DefaultPageSettings.Margins = New Margins(70, 70, 60, 60)
            AddHandler PrintDoc.PrintPage, AddressOf OnPrintPage
            AddHandler PrintDoc.BeginPrint,
                Sub()
                    _rowIdx = 0
                    _pageNo = 0
                End Sub
        End Sub

        Private Sub OnPrintPage(sender As Object, e As PrintPageEventArgs)
            _pageNo += 1
            Dim g = e.Graphics
            g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
            Dim bounds = e.MarginBounds
            Dim contentW As Integer = bounds.Width
            Dim y As Integer = bounds.Top

            If _pageNo = 1 Then
                y = DrawFullHeader(g, bounds, y)
            Else
                y = DrawContinuationHeader(g, bounds, y)
            End If

            Using rule As New Pen(Ink, 1.25F)
                g.DrawLine(rule, bounds.Left, y, bounds.Right, y)
            End Using
            y += 20

            While _rowIdx < _doc.Rows.Count
                Dim row = _doc.Rows(_rowIdx)
                Dim rowH As Integer = MeasureRowHeight(row)

                If y + rowH > bounds.Bottom - 34 Then
                    e.HasMorePages = True
                    DrawFooter(g, bounds, _pageNo)
                    Return
                End If

                DrawRow(g, row, bounds.Left, y, contentW)
                y += rowH
                _rowIdx += 1
            End While

            DrawFooter(g, bounds, _pageNo)
            e.HasMorePages = False
        End Sub

        Private Function DrawFullHeader(g As Graphics, bounds As Rectangle, startY As Integer) As Integer
            Dim y As Integer = startY
            Dim centerX As Single = bounds.Left + bounds.Width / 2.0F

            Dim genText As String = "Generated " & DateTime.Now.ToString("MMM d, yyyy h:mm tt")
            If _preparedBy <> "" Then genText &= " by " & _preparedBy
            Using f As New Font(ReportFont, 8.5F, FontStyle.Italic)
                Dim sz = g.MeasureString(genText, f)
                g.DrawString(genText, f, New SolidBrush(InkFaint), bounds.Right - sz.Width, y)
            End Using

            ' Company name — centered, bold, formal letterhead style
            Using f As New Font(ReportFont, 18, FontStyle.Bold)
                Dim sz = g.MeasureString(_company, f)
                g.DrawString(_company, f, New SolidBrush(Ink), centerX - sz.Width / 2, y)
            End Using
            y += 36

            ' Report title — centered
            Using f As New Font(ReportFont, 13, FontStyle.Bold)
                Dim titleText As String = _doc.Title.ToUpper()
                Dim sz = g.MeasureString(titleText, f)
                g.DrawString(titleText, f, New SolidBrush(Ink), centerX - sz.Width / 2, y)
            End Using
            y += 26

            ' Period — centered, italic
            If _doc.PeriodValue <> "" Then
                Dim periodText As String = _doc.PeriodLabel & ":  " & _doc.PeriodValue
                Using f As New Font(ReportFont, 10, FontStyle.Italic)
                    Dim sz = g.MeasureString(periodText, f)
                    g.DrawString(periodText, f, New SolidBrush(InkMuted), centerX - sz.Width / 2, y)
                End Using
                y += 26
            End If

            Return y + 16
        End Function

        Private Function DrawContinuationHeader(g As Graphics, bounds As Rectangle, startY As Integer) As Integer
            Dim y As Integer = startY
            Using f As New Font(ReportFont, 11, FontStyle.Bold Or FontStyle.Italic)
                g.DrawString(_doc.Title & "  (continued)", f, New SolidBrush(Ink), bounds.Left, y)
            End Using
            y += 28
            Return y
        End Function

        Private Sub DrawFooter(g As Graphics, bounds As Rectangle, pageNo As Integer)
            Using rule As New Pen(RuleLight, 0.75F)
                g.DrawLine(rule, bounds.Left, bounds.Bottom + 4, bounds.Right, bounds.Bottom + 4)
            End Using
            Dim footerText As String = "Page " & pageNo.ToString()
            Using f As New Font(ReportFont, 8.5F, FontStyle.Italic)
                Dim sz = g.MeasureString(footerText, f)
                g.DrawString(footerText, f, New SolidBrush(InkFaint),
                    bounds.Left + (bounds.Width - sz.Width) / 2, bounds.Bottom + 12)
            End Using
        End Sub

        Private Function MeasureRowHeight(row As Row) As Integer
            Select Case row.Kind
                Case RowKind.Spacer : Return 18
                Case RowKind.SectionHeader : Return 34
                Case RowKind.TwoColHeader : Return 30
                Case RowKind.TotalRow, RowKind.TwoColTotalRow : Return 40
                Case RowKind.HighlightRow : Return 56
                Case RowKind.StatusRow : Return 40
                Case Else : Return 30
            End Select
        End Function

        Private Sub DrawRow(g As Graphics, row As Row, left As Integer, y As Integer, w As Integer)
            Dim right As Integer = left + w
            Dim col2X As Integer = left + CInt(w * 0.55)

            Select Case row.Kind

                Case RowKind.Spacer
                    ' nothing to draw

                Case RowKind.SectionHeader
                    Using f As New Font(ReportFont, 10.5F, FontStyle.Bold)
                        g.DrawString(row.Label.ToUpper(), f, New SolidBrush(Ink), left, y)
                    End Using
                    Using rule As New Pen(RuleMedium, 0.75F)
                        g.DrawLine(rule, left, y + 22, right, y + 22)
                    End Using

                Case RowKind.TwoColHeader
                    Using f As New Font(ReportFont, 9.5F, FontStyle.Bold)
                        g.DrawString("ACCOUNT", f, New SolidBrush(InkMuted), left, y)
                        g.DrawString(row.Col1, f, New SolidBrush(InkMuted), col2X, y)
                        Dim sz = g.MeasureString(row.Col2, f)
                        g.DrawString(row.Col2, f, New SolidBrush(InkMuted), right - sz.Width, y)
                    End Using
                    Using rule As New Pen(RuleMedium, 0.75F)
                        g.DrawLine(rule, left, y + 24, right, y + 24)
                    End Using

                Case RowKind.DataRow
                    Dim valStyle As FontStyle = If(row.IsNegative, FontStyle.Italic, FontStyle.Regular)
                    Using lf As New Font(ReportFont, 10.5F)
                        g.DrawString(row.Label, lf, New SolidBrush(InkMuted), left, y)
                    End Using
                    Using vf As New Font(ReportFont, 10.5F, valStyle)
                        Dim sz = g.MeasureString(row.Col1, vf)
                        g.DrawString(row.Col1, vf, New SolidBrush(InkBody), right - sz.Width, y)
                    End Using

                Case RowKind.TwoColRow
                    Using lf As New Font(ReportFont, 10.5F)
                        g.DrawString(row.Label, lf, New SolidBrush(InkBody), left, y)
                        g.DrawString(row.Col1, lf, New SolidBrush(InkBody), col2X, y)
                        Dim sz = g.MeasureString(row.Col2, lf)
                        g.DrawString(row.Col2, lf, New SolidBrush(InkBody), right - sz.Width, y)
                    End Using

                Case RowKind.TotalRow
                    Using rule As New Pen(RuleMedium, 0.75F)
                        g.DrawLine(rule, left, y - 8, right, y - 8)
                    End Using
                    Using f As New Font(ReportFont, 10.5F, FontStyle.Bold)
                        g.DrawString(row.Label, f, New SolidBrush(Ink), left, y)
                        Dim sz = g.MeasureString(row.Col1, f)
                        g.DrawString(row.Col1, f, New SolidBrush(Ink), right - sz.Width, y)
                    End Using

                Case RowKind.TwoColTotalRow
                    Using rule As New Pen(RuleMedium, 0.75F)
                        g.DrawLine(rule, left, y - 8, right, y - 8)
                    End Using
                    Using f As New Font(ReportFont, 10.5F, FontStyle.Bold)
                        g.DrawString(row.Label, f, New SolidBrush(Ink), left, y)
                        g.DrawString(row.Col1, f, New SolidBrush(Ink), col2X, y)
                        Dim sz = g.MeasureString(row.Col2, f)
                        g.DrawString(row.Col2, f, New SolidBrush(Ink), right - sz.Width, y)
                    End Using

                Case RowKind.HighlightRow
                    Dim valStyle As FontStyle = If(row.IsNegative, FontStyle.Bold Or FontStyle.Italic, FontStyle.Bold)
                    Using rule As New Pen(Ink, 1.0F)
                        g.DrawLine(rule, left, y - 10, right, y - 10)
                    End Using
                    Using f As New Font(ReportFont, 12.5F, FontStyle.Bold)
                        g.DrawString(row.Label, f, New SolidBrush(Ink), left, y)
                    End Using
                    Using f As New Font(ReportFont, 13.5F, valStyle)
                        Dim sz = g.MeasureString(row.Col1, f)
                        g.DrawString(row.Col1, f, New SolidBrush(Ink), right - sz.Width, y - 2)
                    End Using
                    ' classic accounting double-rule beneath a grand total
                    Using rule As New Pen(Ink, 1.0F)
                        g.DrawLine(rule, left, y + 28, right, y + 28)
                        g.DrawLine(rule, left, y + 31, right, y + 31)
                    End Using

                Case RowKind.StatusRow
                    Dim labelText As String = row.Label.ToUpper()
                    Using f As New Font(ReportFont, 9.5F, FontStyle.Bold)
                        Dim sz = g.MeasureString(labelText, f)
                        Dim boxW As Single = sz.Width + 28
                        Dim bx As Single = left + (w - boxW) / 2
                        Using pen As New Pen(Ink, 1.0F)
                            If Not row.IsGood Then pen.DashStyle = Drawing2D.DashStyle.Dash
                            g.DrawRectangle(pen, bx, y, boxW, 28)
                        End Using
                        g.DrawString(labelText, f, New SolidBrush(Ink), bx + 14, y + 6)
                    End Using
            End Select
        End Sub
    End Class

    ' =========================================================================
    '  PREVIEW FORM
    ' =========================================================================
    Private Class PreviewForm
        Inherits Form

        Public Sub New(doc As PrintDocument, title As String)
            Me.Text = "Print Preview — " & title
            Me.Size = New Size(900, 760)
            Me.StartPosition = FormStartPosition.CenterParent
            Me.MinimizeBox = False
            Me.MaximizeBox = True
            Me.BackColor = Color.FromArgb(245, 245, 245)
            Me.ShowIcon = False  ' suppress the default multi-color WinForms icon in the OS title bar

            Dim titleBar As New Panel With {
                .Dock = DockStyle.Top, .Height = 56, .BackColor = BrandDark
            }
            Dim lblTitle As New Label With {
                .Text = title, .Font = New Font("Segoe UI Semibold", 13, FontStyle.Bold),
                .ForeColor = Color.White, .AutoSize = True,
                .Location = New Point(20, 16), .BackColor = Color.Transparent
            }
            titleBar.Controls.Add(lblTitle)

            Dim ppc As New PrintPreviewControl With {
                .Dock = DockStyle.Fill,
                .Document = doc,
                .Zoom = 1.0,
                .Columns = 1,
                .BackColor = Color.FromArgb(225, 225, 225)
            }

            Dim btnBar As New Panel With {
                .Dock = DockStyle.Bottom, .Height = 64, .BackColor = Color.White
            }
            Dim btnClose As New Button With {
                .Text = "Close", .Size = New Size(100, 36), .FlatStyle = FlatStyle.Flat,
                .BackColor = Color.FromArgb(235, 235, 235), .ForeColor = InkMuted,
                .Font = New Font("Segoe UI", 9.5F)
            }
            btnClose.FlatAppearance.BorderSize = 0
            btnClose.FlatAppearance.MouseOverBackColor = Color.FromArgb(220, 220, 220)
            btnClose.FlatAppearance.MouseDownBackColor = Color.FromArgb(205, 205, 205)
            AddHandler btnClose.Click, Sub() Me.Close()

            Dim btnPrint As New Button With {
                .Text = "Print", .Size = New Size(120, 36), .FlatStyle = FlatStyle.Flat,
                .BackColor = BrandDark, .ForeColor = Color.White,
                .Font = New Font("Segoe UI Semibold", 9.5F)
            }
            btnPrint.FlatAppearance.BorderSize = 0
            btnPrint.FlatAppearance.MouseOverBackColor = BrandDeep
            btnPrint.FlatAppearance.MouseDownBackColor = BrandPrimary
            AddHandler btnPrint.Click,
                Sub()
                    Using pd As New PrintDialog()
                        pd.Document = doc
                        pd.AllowSomePages = False
                        pd.AllowSelection = False
                        If pd.ShowDialog(Me) = DialogResult.OK Then
                            Try
                                doc.Print()
                            Catch ex As Exception
                                MessageBox.Show("Failed to print: " & ex.Message, "Print Error",
                                                 MessageBoxButtons.OK, MessageBoxIcon.Error)
                            End Try
                        End If
                    End Using
                End Sub

            AddHandler btnBar.Resize,
                Sub()
                    btnPrint.Location = New Point(btnBar.Width - btnPrint.Width - 20, (btnBar.Height - btnPrint.Height) \ 2)
                    btnClose.Location = New Point(btnPrint.Left - btnClose.Width - 10, (btnBar.Height - btnClose.Height) \ 2)
                End Sub

            btnBar.Controls.AddRange({btnClose, btnPrint})

            Me.Controls.Add(ppc)
            Me.Controls.Add(btnBar)
            Me.Controls.Add(titleBar)
        End Sub
    End Class

End Class