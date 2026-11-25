Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.Globalization
Imports System.IO
Imports System.IO.Compression
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Windows.Forms

' =============================================================================
'  ReportExport  —  saves a ReportPrinter.ReportDoc as a PDF.
'
'  Reports are PDF and Print only (owner's revision, Nov 2026: the Excel
'  export was removed). Every save starts from the preview window, so the
'  user has seen exactly what goes into the file -- see ReportPrinter.Preview.
'
'  PDF    Each page is drawn by ReportPrinter's own print routine at 200 dpi
'         and embedded as a greyscale image. The PDF therefore matches the
'         printout exactly -- letterhead, peso signs and signature block
'         included -- and needs no PDF printer installed.
' =============================================================================
Public Module ReportExport

    Private Const PDF_DPI As Integer = 200

    ' Asks where to save, writes the PDF, logs it, and offers to open it.
    ' Returns True when a file was written.
    Public Function SavePdfWithDialog(doc As ReportPrinter.ReportDoc,
                                      companyName As String, preparedBy As String,
                                      Optional owner As IWin32Window = Nothing) As Boolean
        Dim path As String
        Using dlg As New SaveFileDialog With {
                .Title = "Save report as PDF",
                .RestoreDirectory = True,
                .Filter = "PDF document (*.pdf)|*.pdf",
                .FileName = SafeFileName(doc.Title & " " & doc.PeriodValue) & ".pdf",
                .InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                .OverwritePrompt = True}
            If dlg.ShowDialog(owner) <> DialogResult.OK Then Return False
            path = dlg.FileName
        End Using

        Dim oldCursor As Cursor = Cursor.Current
        Cursor.Current = Cursors.WaitCursor
        Try
            SavePdf(doc, path, companyName, preparedBy)
        Catch ex As IOException
            Cursor.Current = oldCursor
            AppModal.Show("The file could not be saved. If it is open in a PDF viewer, " &
                          "close it and try again." & vbCrLf & vbCrLf & ErrorLog.ForUser(ex),
                          "Save Failed", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return False
        Catch ex As Exception
            Cursor.Current = oldCursor
            AppModal.Show("The report could not be saved:" & vbCrLf & ErrorLog.ForUser(ex),
                          "Save Failed", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return False
        Finally
            Cursor.Current = oldCursor
        End Try

        ActivityLog.Record("REPORT_EXPORT", "report", Nothing,
                           Left(doc.Title & " (PDF) - " & doc.PeriodValue, 250))

        If AppModal.Show("Saved:" & vbCrLf & path & vbCrLf & vbCrLf & "Open it now?",
                         "Report Saved", MessageBoxButtons.YesNo,
                         MessageBoxIcon.Information) = DialogResult.Yes Then
            Try
                Process.Start(New ProcessStartInfo(path) With {.UseShellExecute = True})
            Catch ex As Exception
                AppModal.Show("The file was saved but could not be opened: " & ErrorLog.ForUser(ex),
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
        Dim name As String = sb.ToString().Replace(",", "").Trim()
        ' Windows refuses paths past 260 characters; a long period text could
        ' push the suggested name there.
        If name.Length > 120 Then name = name.Substring(0, 120).Trim()
        Return If(name = "", "Report", name)
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
