Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.Globalization
Imports System.IO
Imports System.IO.Compression
Imports System.Runtime.InteropServices
Imports System.Security
Imports System.Text
Imports System.Windows.Forms

' =============================================================================
'  ReportExport  —  saves a ReportPrinter.ReportDoc as Excel (.xlsx) or PDF.
'
'  EXCEL  A real .xlsx, written by hand as Office Open XML inside a zip, so no
'         Excel install or third-party library is needed. Money is stored as
'         NUMBERS with a peso format, not as text, so the accountant can edit a
'         figure and re-total it. The signature block is laid out at the foot.
'
'  PDF    Each page is drawn by ReportPrinter's own print routine at 200 dpi
'         and embedded as a greyscale image. The PDF therefore matches the
'         printout exactly -- letterhead, peso signs and signature block
'         included -- and needs no PDF printer installed.
' =============================================================================
Public Module ReportExport

    Public Enum ExportFormat
        Excel
        Pdf
    End Enum

    Private Const PDF_DPI As Integer = 200

    ' Asks where to save, writes the file, logs it, and offers to open it.
    ' Returns True when a file was written.
    Public Function SaveWithDialog(doc As ReportPrinter.ReportDoc, format As ExportFormat,
                                   companyName As String, preparedBy As String,
                                   Optional owner As IWin32Window = Nothing) As Boolean
        Dim isExcel As Boolean = (format = ExportFormat.Excel)
        Dim ext As String = If(isExcel, ".xlsx", ".pdf")
        Dim path As String
        Using dlg As New SaveFileDialog With {
                .Title = If(isExcel, "Save report as Excel", "Save report as PDF"),
                .Filter = If(isExcel, "Excel workbook (*.xlsx)|*.xlsx", "PDF document (*.pdf)|*.pdf"),
                .FileName = SafeFileName(doc.Title & " " & doc.PeriodValue) & ext,
                .InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                .OverwritePrompt = True}
            If dlg.ShowDialog(owner) <> DialogResult.OK Then Return False
            path = dlg.FileName
        End Using

        Dim oldCursor As Cursor = Cursor.Current
        Cursor.Current = Cursors.WaitCursor
        Try
            If isExcel Then
                SaveExcel(doc, path, companyName, preparedBy)
            Else
                SavePdf(doc, path, companyName, preparedBy)
            End If
        Catch ex As IOException
            Cursor.Current = oldCursor
            AppModal.Show("The file could not be saved. If it is open in Excel or a PDF viewer, " &
                          "close it and try again." & vbCrLf & vbCrLf & ex.Message,
                          "Save Failed", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return False
        Catch ex As Exception
            Cursor.Current = oldCursor
            AppModal.Show("The report could not be saved:" & vbCrLf & ex.Message,
                          "Save Failed", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return False
        Finally
            Cursor.Current = oldCursor
        End Try

        ActivityLog.Record("REPORT_EXPORT", "report", Nothing,
                           Left(doc.Title & " (" & If(isExcel, "Excel", "PDF") & ") - " & doc.PeriodValue, 250))

        If AppModal.Show("Saved:" & vbCrLf & path & vbCrLf & vbCrLf & "Open it now?",
                         "Report Saved", MessageBoxButtons.YesNo,
                         MessageBoxIcon.Information) = DialogResult.Yes Then
            Try
                Process.Start(New ProcessStartInfo(path) With {.UseShellExecute = True})
            Catch ex As Exception
                AppModal.Show("The file was saved but could not be opened: " & ex.Message,
                              "Open File", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            End Try
        End If
        Return True
    End Function

    Private Function SafeFileName(s As String) As String
        Dim sb As New StringBuilder()
        For Each ch As Char In s
            sb.Append(If(Path.GetInvalidFileNameChars().Contains(ch), "-"c, ch))
        Next
        Return sb.ToString().Replace(",", "").Trim()
    End Function

    ' =========================================================================
    '  MONEY PARSING  "₱1,234.50" -> 1234.5 ; "(₱10.00)" or "₱-10.00" -> -10
    ' =========================================================================
    Public Function TryParseMoney(text As String, ByRef value As Decimal) As Boolean
        If String.IsNullOrWhiteSpace(text) Then Return False
        Dim t As String = text.Trim()
        Dim negative As Boolean = False
        If t.StartsWith("(") AndAlso t.EndsWith(")") Then
            negative = True
            t = t.Substring(1, t.Length - 2)
        End If
        t = t.Replace(ChrW(8369), "").Replace(",", "").Replace(" ", "")
        If t.StartsWith("+") Then t = t.Substring(1)
        If t.StartsWith("-") OrElse t.StartsWith(ChrW(8722)) Then
            negative = Not negative
            t = t.Substring(1)
        End If
        If t = "" Then Return False
        If Not Decimal.TryParse(t, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, value) Then Return False
        If negative Then value = -value
        Return True
    End Function

    ' =========================================================================
    '  EXCEL
    ' =========================================================================
    ' cellXfs indexes in BuildStyles()
    Private Const XF_COMPANY As Integer = 1
    Private Const XF_TITLE As Integer = 2
    Private Const XF_META As Integer = 3
    Private Const XF_TEXT As Integer = 4
    Private Const XF_BOLD As Integer = 5
    Private Const XF_MONEY As Integer = 6
    Private Const XF_HEAD As Integer = 7
    Private Const XF_HEAD_R As Integer = 8
    Private Const XF_TOTAL As Integer = 9
    Private Const XF_TOTAL_MONEY As Integer = 10
    Private Const XF_HIGH As Integer = 11
    Private Const XF_HIGH_MONEY As Integer = 12
    Private Const XF_TEXT_R As Integer = 13
    Private Const XF_SIG_CAPTION As Integer = 14
    Private Const XF_SIG_NAME As Integer = 15
    Private Const XF_SIG_POS As Integer = 16
    Private Const XF_NUMBER As Integer = 17
    Private Const XF_TOTAL_NUMBER As Integer = 18
    Private Const XF_SIGNED As Integer = 19
    Private Const XF_TOTAL_SIGNED As Integer = 20

    Private Class SheetWriter
        Public ReadOnly Rows As New StringBuilder()
        Public ReadOnly Merges As New List(Of String)
        Public RowNo As Integer = 0

        Public Function NewRow(Optional height As Double = 0) As StringBuilder
            RowNo += 1
            Dim sb As New StringBuilder()
            sb.Append("<row r=""").Append(RowNo).Append("""")
            If height > 0 Then
                sb.Append(" ht=""").Append(height.ToString(CultureInfo.InvariantCulture)).Append(""" customHeight=""1""")
            End If
            sb.Append(">")
            Return sb
        End Function

        Public Sub EndRow(sb As StringBuilder)
            sb.Append("</row>")
            Rows.Append(sb)
        End Sub

        Public Sub Text(sb As StringBuilder, col As String, value As String, style As Integer)
            sb.Append("<c r=""").Append(col).Append(RowNo).Append(""" s=""").Append(style).Append("""")
            If String.IsNullOrEmpty(value) Then
                sb.Append("/>")
                Return
            End If
            sb.Append(" t=""inlineStr""><is><t xml:space=""preserve"">").
               Append(SecurityElement.Escape(value)).Append("</t></is></c>")
        End Sub

        Public Sub Number(sb As StringBuilder, col As String, value As Decimal, style As Integer)
            sb.Append("<c r=""").Append(col).Append(RowNo).Append(""" s=""").Append(style).
               Append("""><v>").Append(value.ToString(CultureInfo.InvariantCulture)).Append("</v></c>")
        End Sub

        ' A money string becomes a number; anything else ("—", "N/A") stays text.
        Public Sub Money(sb As StringBuilder, col As String, value As String,
                         moneyStyle As Integer, textStyle As Integer)
            Dim d As Decimal
            If TryParseMoney(value, d) Then
                Number(sb, col, d, moneyStyle)
            Else
                Text(sb, col, value, textStyle)
            End If
        End Sub

        Public Sub MergeRow(fromCol As String, toCol As String)
            Merges.Add(fromCol & RowNo & ":" & toCol & RowNo)
        End Sub
    End Class

    ' Column letter for a 0-based index: 0 -> A, 25 -> Z, 26 -> AA.
    Private Function ColLetter(index As Integer) As String
        Dim s As String = ""
        Dim n As Integer = index + 1
        While n > 0
            Dim m As Integer = (n - 1) Mod 26
            s = ChrW(65 + m) & s
            n = (n - 1) \ 26
        End While
        Return s
    End Function

    ' One table line. Figures become real numbers (so they can be summed and
    ' re-totalled in Excel); a cell that is not a figure, such as "—", stays text.
    Private Sub WriteTableCells(w As SheetWriter, r As StringBuilder,
                                cols As List(Of ReportPrinter.Column), cells As String(),
                                isTotal As Boolean)
        For i As Integer = 0 To cols.Count - 1
            Dim c As String = ColLetter(i)
            Dim v As String = If(cells IsNot Nothing AndAlso i < cells.Length, If(cells(i), ""), "")
            Select Case cols(i).Kind
                Case ReportPrinter.ColumnKind.Money
                    w.Money(r, c, v, If(isTotal, XF_TOTAL_MONEY, XF_MONEY), If(isTotal, XF_TOTAL, XF_TEXT_R))
                Case ReportPrinter.ColumnKind.Number, ReportPrinter.ColumnKind.SignedNumber
                    Dim d As Decimal
                    If TryParseMoney(v, d) Then
                        Dim signed As Boolean = cols(i).Kind = ReportPrinter.ColumnKind.SignedNumber
                        w.Number(r, c, d, If(isTotal, If(signed, XF_TOTAL_SIGNED, XF_TOTAL_NUMBER),
                                                      If(signed, XF_SIGNED, XF_NUMBER)))
                    Else
                        w.Text(r, c, v, If(isTotal, XF_TOTAL, XF_TEXT_R))
                    End If
                Case Else
                    w.Text(r, c, v, If(isTotal, XF_TOTAL, XF_TEXT))
            End Select
        Next
    End Sub

    Public Sub SaveExcel(doc As ReportPrinter.ReportDoc, path As String,
                         companyName As String, preparedBy As String)
        ReportPrinter.EnsureSignatures(doc)
        Dim w As New SheetWriter()

        ' A tabular report spreads over as many columns as it has; the
        ' statements use three (label, and up to two figures).
        Dim cols As List(Of ReportPrinter.Column) = doc.Columns
        Dim isTable As Boolean = cols IsNot Nothing AndAlso cols.Count > 0
        Dim lastCol As String = If(isTable, ColLetter(Math.Max(2, cols.Count - 1)), "C")

        ' ── Letterhead ────────────────────────────────────────────────────────
        Dim r As StringBuilder = w.NewRow(24)
        w.Text(r, "A", companyName, XF_COMPANY) : w.Text(r, "B", "", XF_COMPANY) : w.Text(r, "C", "", XF_COMPANY)
        w.EndRow(r) : w.MergeRow("A", lastCol)

        r = w.NewRow(20)
        w.Text(r, "A", doc.Title.ToUpperInvariant(), XF_TITLE) : w.Text(r, "B", "", XF_TITLE) : w.Text(r, "C", "", XF_TITLE)
        w.EndRow(r) : w.MergeRow("A", lastCol)

        If doc.PeriodValue <> "" Then
            r = w.NewRow()
            w.Text(r, "A", doc.PeriodLabel & ":  " & doc.PeriodValue, XF_META)
            w.EndRow(r) : w.MergeRow("A", lastCol)
        End If

        r = w.NewRow()
        Dim gen As String = "Generated " & DateTime.Now.ToString("MMM d, yyyy h:mm tt")
        If preparedBy <> "" Then gen &= " by " & preparedBy
        w.Text(r, "A", gen, XF_META)
        w.EndRow(r) : w.MergeRow("A", lastCol)

        r = w.NewRow() : w.EndRow(r)

        ' ── Body ──────────────────────────────────────────────────────────────
        For Each row In doc.Rows
            Select Case row.Kind
                Case ReportPrinter.RowKind.Spacer
                    r = w.NewRow() : w.EndRow(r)

                Case ReportPrinter.RowKind.SectionHeader
                    r = w.NewRow()
                    w.Text(r, "A", row.Label.ToUpperInvariant(), XF_HEAD)
                    w.Text(r, "B", "", XF_HEAD) : w.Text(r, "C", "", XF_HEAD)
                    w.EndRow(r)

                Case ReportPrinter.RowKind.TwoColHeader
                    r = w.NewRow()
                    w.Text(r, "A", row.Label, XF_HEAD)
                    w.Text(r, "B", row.Col1, XF_HEAD_R)
                    w.Text(r, "C", row.Col2, XF_HEAD_R)
                    w.EndRow(r)

                Case ReportPrinter.RowKind.DataRow
                    r = w.NewRow()
                    w.Text(r, "A", row.Label, XF_TEXT)
                    w.Money(r, lastCol, row.Col1, XF_MONEY, XF_TEXT_R)
                    w.EndRow(r)

                Case ReportPrinter.RowKind.TwoColRow
                    r = w.NewRow()
                    w.Text(r, "A", row.Label, XF_TEXT)
                    w.Money(r, "B", row.Col1, XF_MONEY, XF_TEXT_R)
                    w.Money(r, "C", row.Col2, XF_MONEY, XF_TEXT_R)
                    w.EndRow(r)

                Case ReportPrinter.RowKind.TotalRow
                    r = w.NewRow()
                    w.Text(r, "A", row.Label, XF_TOTAL)
                    w.Text(r, "B", "", XF_TOTAL)
                    w.Money(r, lastCol, row.Col1, XF_TOTAL_MONEY, XF_TOTAL)
                    w.EndRow(r)

                Case ReportPrinter.RowKind.TwoColTotalRow
                    r = w.NewRow()
                    w.Text(r, "A", row.Label, XF_TOTAL)
                    w.Money(r, "B", row.Col1, XF_TOTAL_MONEY, XF_TOTAL)
                    w.Money(r, "C", row.Col2, XF_TOTAL_MONEY, XF_TOTAL)
                    w.EndRow(r)

                Case ReportPrinter.RowKind.HighlightRow
                    r = w.NewRow(22)
                    w.Text(r, "A", row.Label, XF_HIGH)
                    w.Text(r, "B", "", XF_HIGH)
                    w.Money(r, lastCol, row.Col1, XF_HIGH_MONEY, XF_HIGH)
                    w.EndRow(r)

                Case ReportPrinter.RowKind.TableHeader
                    If isTable Then
                        r = w.NewRow()
                        For i As Integer = 0 To cols.Count - 1
                            w.Text(r, ColLetter(i), cols(i).Header.ToUpperInvariant(),
                                   If(cols(i).Kind = ReportPrinter.ColumnKind.Text, XF_HEAD, XF_HEAD_R))
                        Next
                        w.EndRow(r)
                    End If

                Case ReportPrinter.RowKind.TableRow, ReportPrinter.RowKind.TableTotal
                    If isTable Then
                        r = w.NewRow()
                        WriteTableCells(w, r, cols, row.Cells, row.Kind = ReportPrinter.RowKind.TableTotal)
                        w.EndRow(r)
                    End If

                Case ReportPrinter.RowKind.StatusRow
                    r = w.NewRow()
                    w.Text(r, "A", row.Label, XF_BOLD)
                    w.EndRow(r)

                Case ReportPrinter.RowKind.Signatures
                    WriteSignatures(w, doc.Signers)
            End Select
        Next

        ' ── Package ──────────────────────────────────────────────────────────
        Dim sheet As New StringBuilder()
        sheet.Append("<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>")
        sheet.Append("<worksheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"" ")
        sheet.Append("xmlns:r=""http://schemas.openxmlformats.org/officeDocument/2006/relationships"">")
        ' A wide table is printed scaled to one page across.
        If isTable Then sheet.Append("<sheetPr><pageSetUpPr fitToPage=""1""/></sheetPr>")
        sheet.Append("<sheetViews><sheetView workbookViewId=""0""/></sheetViews>")
        sheet.Append("<sheetFormatPr defaultRowHeight=""16""/>")
        sheet.Append("<cols>")
        If isTable Then
            ' Each column's share of about 120 characters, never narrower than 10.
            Dim totalW As Single = Math.Max(0.01F, cols.Sum(Function(c) c.Weight))
            For i As Integer = 0 To cols.Count - 1
                Dim chars As Double = Math.Max(10.0, Math.Round(120.0 * cols(i).Weight / totalW, 1))
                sheet.Append("<col min=""").Append(i + 1).Append(""" max=""").Append(i + 1).
                      Append(""" width=""").Append(chars.ToString(CultureInfo.InvariantCulture)).
                      Append(""" customWidth=""1""/>")
            Next
        Else
            sheet.Append("<col min=""1"" max=""1"" width=""46"" customWidth=""1""/>")
            sheet.Append("<col min=""2"" max=""3"" width=""26"" customWidth=""1""/>")
        End If
        sheet.Append("</cols>")
        sheet.Append("<sheetData>").Append(w.Rows).Append("</sheetData>")
        If w.Merges.Count > 0 Then
            sheet.Append("<mergeCells count=""").Append(w.Merges.Count).Append(""">")
            For Each m In w.Merges
                sheet.Append("<mergeCell ref=""").Append(m).Append("""/>")
            Next
            sheet.Append("</mergeCells>")
        End If
        sheet.Append("<pageMargins left=""0.7"" right=""0.7"" top=""0.75"" bottom=""0.75"" header=""0.3"" footer=""0.3""/>")
        sheet.Append(If(isTable,
                        "<pageSetup paperSize=""1"" orientation=""portrait"" fitToWidth=""1"" fitToHeight=""0""/>",
                        "<pageSetup paperSize=""1"" orientation=""portrait""/>"))
        sheet.Append("</worksheet>")

        Dim safeSheetName As String = SecurityElement.Escape(MakeSheetName(doc.Title))

        If File.Exists(path) Then File.Delete(path)
        Using fs As New FileStream(path, FileMode.CreateNew)
            Using zip As New ZipArchive(fs, ZipArchiveMode.Create)
                AddEntry(zip, "[Content_Types].xml",
                    "<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>" &
                    "<Types xmlns=""http://schemas.openxmlformats.org/package/2006/content-types"">" &
                    "<Default Extension=""rels"" ContentType=""application/vnd.openxmlformats-package.relationships+xml""/>" &
                    "<Default Extension=""xml"" ContentType=""application/xml""/>" &
                    "<Override PartName=""/xl/workbook.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml""/>" &
                    "<Override PartName=""/xl/worksheets/sheet1.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml""/>" &
                    "<Override PartName=""/xl/styles.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml""/>" &
                    "</Types>")
                AddEntry(zip, "_rels/.rels",
                    "<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>" &
                    "<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">" &
                    "<Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"" Target=""xl/workbook.xml""/>" &
                    "</Relationships>")
                AddEntry(zip, "xl/workbook.xml",
                    "<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>" &
                    "<workbook xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"" " &
                    "xmlns:r=""http://schemas.openxmlformats.org/officeDocument/2006/relationships"">" &
                    "<sheets><sheet name=""" & safeSheetName & """ sheetId=""1"" r:id=""rId1""/></sheets>" &
                    "</workbook>")
                AddEntry(zip, "xl/_rels/workbook.xml.rels",
                    "<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>" &
                    "<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">" &
                    "<Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"" Target=""worksheets/sheet1.xml""/>" &
                    "<Relationship Id=""rId2"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"" Target=""styles.xml""/>" &
                    "</Relationships>")
                AddEntry(zip, "xl/styles.xml", BuildStyles())
                AddEntry(zip, "xl/worksheets/sheet1.xml", sheet.ToString())
            End Using
        End Using
    End Sub

    ' Caption row, two tall empty rows to sign in, then name over a rule and
    ' position beneath -- one signer per column (A, B, C).
    Private Sub WriteSignatures(w As SheetWriter, signers As List(Of ReportSignatories.Signer))
        If signers Is Nothing OrElse signers.Count = 0 Then Return
        Dim cols() As String = {"A", "B", "C"}
        Dim n As Integer = Math.Min(3, signers.Count)

        Dim r As StringBuilder = w.NewRow() : w.EndRow(r)
        r = w.NewRow() : w.EndRow(r)

        r = w.NewRow()
        For i As Integer = 0 To n - 1
            w.Text(r, cols(i), signers(i).Caption & ":", XF_SIG_CAPTION)
        Next
        w.EndRow(r)

        r = w.NewRow(30) : w.EndRow(r)

        r = w.NewRow()
        For i As Integer = 0 To n - 1
            w.Text(r, cols(i), signers(i).Name, XF_SIG_NAME)
        Next
        w.EndRow(r)

        r = w.NewRow()
        For i As Integer = 0 To n - 1
            w.Text(r, cols(i), signers(i).Position, XF_SIG_POS)
        Next
        w.EndRow(r)
    End Sub

    Private Function MakeSheetName(title As String) As String
        Dim sb As New StringBuilder()
        For Each ch As Char In If(title, "")
            If "[]:*?/\".IndexOf(ch) < 0 Then sb.Append(ch)
        Next
        Dim s As String = sb.ToString().Trim()
        If s = "" Then s = "Report"
        Return If(s.Length > 31, s.Substring(0, 31), s)
    End Function

    Private Sub AddEntry(zip As ZipArchive, name As String, content As String)
        Dim entry = zip.CreateEntry(name, CompressionLevel.Optimal)
        Using s = entry.Open()
            Dim bytes = New UTF8Encoding(False).GetBytes(content)
            s.Write(bytes, 0, bytes.Length)
        End Using
    End Sub

    Private Function BuildStyles() As String
        Dim peso As String = "&quot;" & ChrW(8369) & "&quot;"
        Dim fmt As String = peso & "#,##0.00;(" & peso & "#,##0.00);" & peso & "0.00"
        Dim signedFmt As String = "+#,##0;-#,##0;0"

        ' xf(font, border, numFmt, align)
        Dim xf = Function(font As Integer, border As Integer, numFmt As Integer, align As String) As String
                     Dim s As String = "<xf numFmtId=""" & numFmt & """ fontId=""" & font &
                                       """ fillId=""0"" borderId=""" & border & """ xfId=""0""" &
                                       If(font > 0, " applyFont=""1""", "") &
                                       If(border > 0, " applyBorder=""1""", "") &
                                       If(numFmt > 0, " applyNumberFormat=""1""", "")
                     If align <> "" Then
                         Return s & " applyAlignment=""1""><alignment horizontal=""" & align & """ vertical=""center""/></xf>"
                     End If
                     Return s & "/>"
                 End Function

        Dim xfs As New List(Of String) From {
            xf(0, 0, 0, ""),              ' 0  default
            xf(2, 0, 0, "center"),        ' 1  company
            xf(4, 0, 0, "center"),        ' 2  title
            xf(3, 0, 0, "center"),        ' 3  meta
            xf(0, 0, 0, ""),              ' 4  text
            xf(1, 0, 0, ""),              ' 5  bold
            xf(0, 0, 164, "right"),       ' 6  money
            xf(1, 1, 0, ""),              ' 7  header / section
            xf(1, 1, 0, "right"),         ' 8  header, right
            xf(1, 2, 0, ""),              ' 9  total label
            xf(1, 2, 164, "right"),       ' 10 total money
            xf(4, 3, 0, ""),              ' 11 highlight label
            xf(4, 3, 164, "right"),       ' 12 highlight money
            xf(0, 0, 0, "right"),         ' 13 text, right
            xf(3, 0, 0, "left"),          ' 14 signature caption
            xf(1, 2, 0, "center"),        ' 15 signature name over a rule
            xf(3, 0, 0, "center"),        ' 16 signature position
            xf(0, 0, 3, "right"),         ' 17 whole number (built-in #,##0)
            xf(1, 2, 3, "right"),         ' 18 total whole number
            xf(0, 0, 165, "right"),       ' 19 signed whole number (+5 / -3)
            xf(1, 2, 165, "right")        ' 20 total signed whole number
        }

        Return "<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>" &
            "<styleSheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"">" &
            "<numFmts count=""2""><numFmt numFmtId=""164"" formatCode=""" & fmt & """/>" &
            "<numFmt numFmtId=""165"" formatCode=""" & signedFmt & """/></numFmts>" &
            "<fonts count=""5"">" &
            "<font><sz val=""11""/><name val=""Calibri""/></font>" &
            "<font><b/><sz val=""11""/><name val=""Calibri""/></font>" &
            "<font><b/><sz val=""16""/><name val=""Calibri""/></font>" &
            "<font><i/><sz val=""10""/><color rgb=""FF666666""/><name val=""Calibri""/></font>" &
            "<font><b/><sz val=""13""/><name val=""Calibri""/></font>" &
            "</fonts>" &
            "<fills count=""2""><fill><patternFill patternType=""none""/></fill>" &
            "<fill><patternFill patternType=""gray125""/></fill></fills>" &
            "<borders count=""4"">" &
            "<border><left/><right/><top/><bottom/><diagonal/></border>" &
            "<border><left/><right/><top/><bottom style=""thin""><color auto=""1""/></bottom><diagonal/></border>" &
            "<border><left/><right/><top style=""thin""><color auto=""1""/></top><bottom/><diagonal/></border>" &
            "<border><left/><right/><top style=""thin""><color auto=""1""/></top>" &
            "<bottom style=""double""><color auto=""1""/></bottom><diagonal/></border>" &
            "</borders>" &
            "<cellStyleXfs count=""1""><xf numFmtId=""0"" fontId=""0"" fillId=""0"" borderId=""0""/></cellStyleXfs>" &
            "<cellXfs count=""" & xfs.Count & """>" & String.Concat(xfs) & "</cellXfs>" &
            "<cellStyles count=""1""><cellStyle name=""Normal"" xfId=""0"" builtinId=""0""/></cellStyles>" &
            "</styleSheet>"
    End Function

    ' =========================================================================
    '  PDF
    ' =========================================================================
    Public Sub SavePdf(doc As ReportPrinter.ReportDoc, path As String,
                       companyName As String, preparedBy As String)
        Dim pages As List(Of Bitmap) = ReportPrinter.RenderPages(doc, companyName, preparedBy, PDF_DPI)
        Try
            Using fs As New FileStream(path, FileMode.Create, FileAccess.Write)
                WritePdf(fs, pages, doc.Title)
            End Using
        Finally
            For Each p In pages
                p.Dispose()
            Next
        End Try
    End Sub

    ' Minimal PDF 1.4: catalog, page tree, and per page a content stream that
    ' paints one full-page greyscale image (FlateDecode).
    Private Sub WritePdf(output As Stream, pages As List(Of Bitmap), title As String)
        Dim offsets As New List(Of Long)
        Dim pos As Long = 0
        Dim write = Sub(bytes As Byte())
                        output.Write(bytes, 0, bytes.Length)
                        pos += bytes.Length
                    End Sub
        Dim writeText = Sub(s As String) write(Encoding.ASCII.GetBytes(s))
        Dim beginObj = Sub(id As Integer)
                           ' offsets(id - 1) is object id's position
                           While offsets.Count < id
                               offsets.Add(0)
                           End While
                           offsets(id - 1) = pos
                           writeText(id & " 0 obj" & vbLf)
                       End Sub

        Const PT_W As Integer = 612   ' Letter, in points
        Const PT_H As Integer = 792

        ' Object ids: 1 catalog, 2 pages, 3 info, then 3 per page.
        Dim pageIds As New List(Of Integer)
        For i As Integer = 0 To pages.Count - 1
            pageIds.Add(4 + i * 3)
        Next

        writeText("%PDF-1.4" & vbLf)
        write(New Byte() {37, &HE2, &HE3, &HCF, &HD3, 10})

        beginObj(1)
        writeText("<< /Type /Catalog /Pages 2 0 R >>" & vbLf & "endobj" & vbLf)

        beginObj(2)
        writeText("<< /Type /Pages /Count " & pages.Count & " /Kids [" &
                  String.Join(" ", pageIds.Select(Function(p) p & " 0 R")) & "] >>" & vbLf & "endobj" & vbLf)

        beginObj(3)
        writeText("<< /Title (" & PdfString(title) & ") /Producer (Paint Store POS) /CreationDate (D:" &
                  DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) & ") >>" & vbLf &
                  "endobj" & vbLf)

        For i As Integer = 0 To pages.Count - 1
            Dim pageId As Integer = pageIds(i)
            Dim contentId As Integer = pageId + 1
            Dim imageId As Integer = pageId + 2
            Dim bmp As Bitmap = pages(i)

            beginObj(pageId)
            writeText("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " & PT_W & " " & PT_H & "] " &
                      "/Resources << /XObject << /Im0 " & imageId & " 0 R >> >> " &
                      "/Contents " & contentId & " 0 R >>" & vbLf & "endobj" & vbLf)

            Dim content As Byte() = Encoding.ASCII.GetBytes("q " & PT_W & " 0 0 " & PT_H & " 0 0 cm /Im0 Do Q")
            beginObj(contentId)
            writeText("<< /Length " & content.Length & " >>" & vbLf & "stream" & vbLf)
            write(content)
            writeText(vbLf & "endstream" & vbLf & "endobj" & vbLf)

            Dim data As Byte() = ZlibCompress(GrayPixels(bmp))
            beginObj(imageId)
            writeText("<< /Type /XObject /Subtype /Image /Width " & bmp.Width & " /Height " & bmp.Height &
                      " /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /FlateDecode /Length " &
                      data.Length & " >>" & vbLf & "stream" & vbLf)
            write(data)
            writeText(vbLf & "endstream" & vbLf & "endobj" & vbLf)
        Next

        Dim xrefPos As Long = pos
        writeText("xref" & vbLf & "0 " & (offsets.Count + 1) & vbLf)
        writeText("0000000000 65535 f " & vbLf)
        For Each o In offsets
            writeText(o.ToString("0000000000") & " 00000 n " & vbLf)
        Next
        writeText("trailer" & vbLf & "<< /Size " & (offsets.Count + 1) & " /Root 1 0 R /Info 3 0 R >>" & vbLf &
                  "startxref" & vbLf & xrefPos & vbLf & "%%EOF" & vbLf)
    End Sub

    Private Function PdfString(s As String) As String
        Dim sb As New StringBuilder()
        For Each ch As Char In If(s, "")
            If AscW(ch) > 126 OrElse AscW(ch) < 32 Then
                sb.Append("?"c)
            ElseIf ch = "("c OrElse ch = ")"c OrElse ch = "\"c Then
                sb.Append("\"c).Append(ch)
            Else
                sb.Append(ch)
            End If
        Next
        Return sb.ToString()
    End Function

    ' One byte per pixel, rows top to bottom -- what /DeviceGray expects.
    Private Function GrayPixels(bmp As Bitmap) As Byte()
        Dim w As Integer = bmp.Width, h As Integer = bmp.Height
        Dim rect As New Rectangle(0, 0, w, h)
        Dim bd As BitmapData = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb)
        Try
            Dim stride As Integer = bd.Stride
            Dim src(stride * h - 1) As Byte
            Marshal.Copy(bd.Scan0, src, 0, src.Length)
            Dim dst(w * h - 1) As Byte
            Dim k As Integer = 0
            For y As Integer = 0 To h - 1
                Dim rowStart As Integer = y * stride
                For x As Integer = 0 To w - 1
                    Dim i As Integer = rowStart + x * 3
                    ' BGR order in memory.
                    dst(k) = CByte((src(i + 2) * 299 + src(i + 1) * 587 + src(i) * 114) \ 1000)
                    k += 1
                Next
            Next
            Return dst
        Finally
            bmp.UnlockBits(bd)
        End Try
    End Function

    ' PDF's FlateDecode wants a zlib stream: 2-byte header, raw deflate data,
    ' then the Adler-32 of the uncompressed bytes. DeflateStream writes only
    ' the middle part.
    Private Function ZlibCompress(data As Byte()) As Byte()
        Using ms As New MemoryStream()
            ms.WriteByte(&H78) : ms.WriteByte(&H9C)
            Using ds As New DeflateStream(ms, CompressionLevel.Optimal, leaveOpen:=True)
                ds.Write(data, 0, data.Length)
            End Using
            Dim a As UInteger = 1, b As UInteger = 0
            For Each v As Byte In data
                a = (a + v) Mod 65521UI
                b = (b + a) Mod 65521UI
            Next
            Dim adler As UInteger = (b << 16) Or a
            ms.WriteByte(CByte((adler >> 24) And &HFF))
            ms.WriteByte(CByte((adler >> 16) And &HFF))
            ms.WriteByte(CByte((adler >> 8) And &HFF))
            ms.WriteByte(CByte(adler And &HFF))
            Return ms.ToArray()
        End Using
    End Function

End Module
