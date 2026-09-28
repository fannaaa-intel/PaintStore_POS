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
        Signatures
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
        ' Printed as a Prepared / Checked / Approved block after the last row.
        ' Left Nothing, the configured signatories are filled in on output.
        Public Signers As List(Of ReportSignatories.Signer)
    End Class

    ' Appends the signature block once, as the final row, so it paginates like
    ' any other row and never lands in the page footer.
    Public Shared Sub EnsureSignatures(doc As ReportDoc)
        If doc.Signers Is Nothing Then doc.Signers = ReportSignatories.ForReport()
        If doc.Signers.Count = 0 Then Return
        If doc.Rows.Any(Function(r) r.Kind = RowKind.Signatures) Then Return
        doc.Rows.Add(New Row With {.Kind = RowKind.Signatures})
    End Sub

    ' The store name from Store Settings, for the report letterhead.
    Public Shared Function CompanyName() As String
        Try
            Dim n As String = StoreSettings.StoreName
            If Not String.IsNullOrWhiteSpace(n) Then Return n
        Catch
        End Try
        Return "PAINT STORE"
    End Function

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

    ' rowHeader captions the left-hand (label) column. It defaults to "ACCOUNT"
    ' because the trial balance was the only caller when this was written, and
    ' the caption used to be hardcoded in DrawRow.
    Public Shared Function TwoColHeader(c1Header As String, c2Header As String,
                                        Optional rowHeader As String = "ACCOUNT") As Row
        Return New Row With {.Kind = RowKind.TwoColHeader, .Label = rowHeader,
                             .Col1 = c1Header, .Col2 = c2Header}
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
    Private Shared ReadOnly BrandDark As Color = Theme.Dark  ' title bar / button (rest state)
    Private Shared ReadOnly BrandDeep As Color = Theme.Deep  ' button hover
    Private Shared ReadOnly BrandPrimary As Color = Theme.Primary  ' button pressed

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
        EnsureSignatures(doc)
        Dim job As New PrintJob(doc, companyName, preparedBy)
        Dim frm As New PreviewForm(job.PrintDoc, doc, companyName, preparedBy)
        frm.ShowDialog()
    End Sub

    ' Draws every page of the report onto bitmaps at the given resolution,
    ' with the very same routine that prints it -- so a PDF made from these
    ' pages matches the printout exactly. Each page is Letter size.
    Public Shared Function RenderPages(doc As ReportDoc, companyName As String,
                                       preparedBy As String, dpi As Integer) As List(Of Bitmap)
        EnsureSignatures(doc)
        Dim job As New PrintJob(doc, companyName, preparedBy)
        job.Reset()
        Dim pageBounds As New Rectangle(0, 0, PAGE_W, PAGE_H)
        Dim marginBounds As New Rectangle(MARGIN_X, MARGIN_Y, PAGE_W - MARGIN_X * 2, PAGE_H - MARGIN_Y * 2)
        Dim pages As New List(Of Bitmap)
        Dim more As Boolean = True
        While more
            Dim bmp As New Bitmap(PAGE_W * dpi \ 100, PAGE_H * dpi \ 100)
            bmp.SetResolution(dpi, dpi)
            Using g As Graphics = Graphics.FromImage(bmp)
                g.Clear(Color.White)
                ' World units of 1/100 inch, the same units a printer page uses,
                ' so every coordinate in OnPrintPage lands where it prints.
                g.PageUnit = GraphicsUnit.Inch
                g.PageScale = 0.01F
                g.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAliasGridFit
                Dim e As New PrintPageEventArgs(g, marginBounds, pageBounds, Nothing)
                job.DrawPage(e)
                more = e.HasMorePages
            End Using
            pages.Add(bmp)
            If pages.Count > 200 Then Exit While   ' runaway guard
        End While
        Return pages
    End Function

    ' Letter paper and margins, in 1/100 inch.
    Private Const PAGE_W As Integer = 850
    Private Const PAGE_H As Integer = 1100
    Private Const MARGIN_X As Integer = 70
    Private Const MARGIN_Y As Integer = 60

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
            ' Page settings come from the default printer; on a PC with none
            ' installed they throw. Exporting to PDF/Excel must still work there.
            Try
                PrintDoc.DefaultPageSettings.PaperSize = New PaperSize("Letter", PAGE_W, PAGE_H)
                PrintDoc.DefaultPageSettings.Margins = New Margins(MARGIN_X, MARGIN_X, MARGIN_Y, MARGIN_Y)
            Catch
            End Try
            AddHandler PrintDoc.PrintPage, AddressOf OnPrintPage
            AddHandler PrintDoc.BeginPrint, Sub() Reset()
        End Sub

        Public Sub Reset()
            _rowIdx = 0
            _pageNo = 0
        End Sub

        Private Sub OnPrintPage(sender As Object, e As PrintPageEventArgs)
            DrawPage(e)
        End Sub

        Public Sub DrawPage(e As PrintPageEventArgs)
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
            ' Its own line: sharing one with a long store name made them overlap.
            y += 18

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
                Case RowKind.Signatures : Return 150
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
                    ' Label / Col1 / Col2 must sit on the same three positions as
                    ' TwoColRow and TwoColTotalRow below, or the captions do not
                    ' line up with the figures they head.
                    Using f As New Font(ReportFont, 9.5F, FontStyle.Bold)
                        g.DrawString(row.Label, f, New SolidBrush(InkMuted), left, y)
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

                Case RowKind.Signatures
                    DrawSignatures(g, left, y + 36, w)
            End Select
        End Sub

        ' Prepared by / Checked by / Approved by, side by side: caption, room
        ' to sign, a rule, then the name and position centred under the rule.
        ' A blank name leaves the rule to be filled in by hand.
        Private Sub DrawSignatures(g As Graphics, left As Integer, y As Integer, w As Integer)
            Dim signers = _doc.Signers
            If signers Is Nothing OrElse signers.Count = 0 Then Return
            Dim colW As Single = w / CSng(signers.Count)
            Using fCap As New Font(ReportFont, 9.5F, FontStyle.Italic),
                  fName As New Font(ReportFont, 10.5F, FontStyle.Bold),
                  fPos As New Font(ReportFont, 9.5F),
                  inkBr As New SolidBrush(Ink), mutedBr As New SolidBrush(InkMuted),
                  rule As New Pen(Ink, 0.75F)
                Dim center As New StringFormat With {.Alignment = StringAlignment.Center}
                For i As Integer = 0 To signers.Count - 1
                    Dim s = signers(i)
                    Dim x As Single = left + i * colW
                    Dim lineL As Single = x + 8
                    Dim lineR As Single = x + colW - 16
                    g.DrawString(s.Caption & ":", fCap, mutedBr, x, y)
                    g.DrawLine(rule, lineL, y + 60, lineR, y + 60)
                    Dim box As New RectangleF(lineL, y + 64, lineR - lineL, 20)
                    If s.Name <> "" Then g.DrawString(s.Name, fName, inkBr, box, center)
                    If s.Position <> "" Then
                        g.DrawString(s.Position, fPos, mutedBr,
                                     New RectangleF(lineL, y + 84, lineR - lineL, 20), center)
                    End If
                Next
            End Using
        End Sub
    End Class

    ' =========================================================================
    '  PREVIEW FORM
    ' =========================================================================
    Private Class PreviewForm
        Inherits Form

        Public Sub New(doc As PrintDocument, report As ReportDoc,
                       companyName As String, preparedBy As String)
            Dim title As String = report.Title
            Me.Text = "Print Preview — " & title
            ' Most of the screen, so the page is read at a comfortable size
            ' instead of squinting at a small window.
            Dim scr As Rectangle = Screen.PrimaryScreen.WorkingArea
            Me.Size = New Size(Math.Min(1200, CInt(scr.Width * 0.9)), CInt(scr.Height * 0.92))
            Me.MinimumSize = New Size(760, 560)
            Me.StartPosition = FormStartPosition.CenterScreen
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
            ' Fit the page to the window's width; the page scrolls vertically.
            AddHandler ppc.Resize,
                Sub()
                    Dim avail As Integer = ppc.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 40
                    If avail > 200 Then ppc.Zoom = Math.Min(1.6, avail / CDbl(PAGE_W))
                End Sub

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

            Dim makeExport =
                Function(caption As String, format As ReportExport.ExportFormat) As Button
                    Dim b As New Button With {
                        .Text = caption, .Size = New Size(140, 36), .FlatStyle = FlatStyle.Flat,
                        .BackColor = Color.FromArgb(220, 235, 232), .ForeColor = BrandDark,
                        .Font = New Font("Segoe UI Semibold", 9.5F), .Cursor = Cursors.Hand
                    }
                    b.FlatAppearance.BorderSize = 0
                    b.FlatAppearance.MouseOverBackColor = Color.FromArgb(205, 228, 223)
                    AddHandler b.Click,
                        Sub() ReportExport.SaveWithDialog(report, format, companyName, preparedBy, Me)
                    Return b
                End Function
            Dim btnExcel As Button = makeExport("Save as Excel", ReportExport.ExportFormat.Excel)
            Dim btnPdf As Button = makeExport("Save as PDF", ReportExport.ExportFormat.Pdf)

            AddHandler btnBar.Resize,
                Sub()
                    Dim cy As Integer = (btnBar.Height - btnPrint.Height) \ 2
                    btnPrint.Location = New Point(btnBar.Width - btnPrint.Width - 20, cy)
                    btnPdf.Location = New Point(btnPrint.Left - btnPdf.Width - 10, cy)
                    btnExcel.Location = New Point(btnPdf.Left - btnExcel.Width - 10, cy)
                    btnClose.Location = New Point(20, cy)
                End Sub

            btnBar.Controls.AddRange({btnClose, btnExcel, btnPdf, btnPrint})

            Me.Controls.Add(ppc)
            Me.Controls.Add(btnBar)
            Me.Controls.Add(titleBar)
        End Sub
    End Class

End Class