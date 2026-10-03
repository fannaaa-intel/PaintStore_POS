Imports System.Collections.Generic
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Windows.Forms
Imports MySql.Data.MySqlClient

' =============================================================================
'  SupplierLogo
'
'  Supplier profile photos, shared by every screen that shows a supplier:
'  Procurement ▸ Suppliers (list / detail / add-edit form) and
'  Admin ▸ Suppliers (read-only table).
'
'  STORAGE
'    The file is copied into SupplierImages\ and suppliers.image_path holds the
'    path RELATIVE to the app folder, the same convention products.image_path
'    uses, so a row survives the app being installed somewhere else.
'
'  OPTIONAL COLUMNS
'    image_path, tin, payment_terms and notes only exist once migrations.sql
'    has been run. Probe() is called once per screen load and every caller
'    checks the matching Has* property before selecting or writing, so an
'    un-migrated database keeps working: the avatar falls back to the
'    supplier's initial and the extra fields are simply not offered.
'
'  WHY A MODULE
'    Two panels draw the same avatar from the same files. Kept per-panel, each
'    would carry its own probe and its own bitmap cache, so the same logo was
'    decoded twice and one screen could not invalidate the other's copy.
' =============================================================================
Public Module SupplierLogo

    ' ── PALETTE  (the mint the avatar gradient falls back to) ────────────────
    Private ReadOnly MintPrimary As Color = Theme.Primary
    Private ReadOnly MintDeep As Color = Theme.Deep

    ' Scaled avatars keyed by "full path|size". Rebuilding a view re-reads every
    ' logo otherwise, and each decode is a fresh GDI+ handle.
    Private ReadOnly _cache As New Dictionary(Of String, Image)

    ' Which optional columns this database actually has. All False until
    ' Probe() runs, which is the safe default: callers then behave exactly as
    ' they did before these columns existed.
    Private _present As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

    ''' <summary>Every optional supplier column, in the order a SELECT lists them.</summary>
    Private ReadOnly OptionalColumns As String() =
        {"image_path", "tin", "payment_terms", "notes",
         "company_name", "head_name", "dti_no", "bir_no"}

    ''' <summary>True when suppliers.image_path exists.</summary>
    Public ReadOnly Property HasColumn As Boolean
        Get
            Return Has("image_path")
        End Get
    End Property

    ''' <summary>True when the commercial fields (tin / terms / notes) exist.</summary>
    Public ReadOnly Property HasCommercial As Boolean
        Get
            Return Has("tin") AndAlso Has("payment_terms") AndAlso Has("notes")
        End Get
    End Property

    ''' <summary>
    ''' True when the business-registration fields exist — registered company
    ''' name, head of company, DTI and BIR numbers.
    '''
    ''' Checked as a group because the form offers them as one "Business
    ''' Registration" section, and half a section is worse than none: a form
    ''' that asks for DTI but has nowhere to put BIR invites the number being
    ''' typed into whichever box is showing.
    ''' </summary>
    Public ReadOnly Property HasRegistration As Boolean
        Get
            Return Has("company_name") AndAlso Has("head_name") AndAlso
                   Has("dti_no") AndAlso Has("bir_no")
        End Get
    End Property

    ''' <summary>True when a specific optional column is present.</summary>
    Public Function Has(column As String) As Boolean
        Return _present.Contains(column)
    End Function

    ''' <summary>
    ''' Detect the optional columns. Called once when a supplier screen loads
    ''' rather than per query, so information_schema is not hit per row. One
    ''' round-trip covers every column via IN (...).
    ''' </summary>
    Public Sub Probe()
        _present.Clear()
        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT column_name FROM information_schema.columns " &
                    "WHERE table_schema = DATABASE() " &
                    "  AND table_name = 'suppliers' " &
                    "  AND column_name IN ('image_path','tin','payment_terms','notes'," &
                    "                     'company_name','head_name','dti_no','bir_no')", conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            _present.Add(rdr.GetString(0))
                        End While
                    End Using
                End Using
            End Using
        Catch swallowed As Exception
            ErrorLog.Write("SupplierLogo.Probe", swallowed)
            ' No access to information_schema, or no database at all: stay on
            ' the pre-migration behaviour.
            _present.Clear()
        End Try
    End Sub

    ''' <summary>
    ''' The optional columns that exist, as "s.col, s.col, " ready to splice
    ''' into a SELECT list. Empty when none do, so one query string stays valid
    ''' on a migrated and an un-migrated database alike.
    ''' </summary>
    Public Function SelectFragment() As String
        Dim sb As New System.Text.StringBuilder()
        For Each col In OptionalColumns
            If Has(col) Then sb.Append("s.").Append(col).Append(", ")
        Next
        Return sb.ToString()
    End Function

    ''' <summary>
    ''' The same columns as a GROUP BY tail (", s.col, s.col"), for the grouped
    ''' query the Admin grid runs. Empty when none exist.
    ''' </summary>
    Public Function GroupByFragment() As String
        Dim sb As New System.Text.StringBuilder()
        For Each col In OptionalColumns
            If Has(col) Then sb.Append(", s.").Append(col)
        Next
        Return sb.ToString()
    End Function

    ''' <summary>Read an optional column, or "" when it was not selected.</summary>
    Public Function ReadColumn(rdr As MySqlDataReader, column As String) As String
        If Not Has(column) Then Return ""
        Try
            If IsDBNull(rdr(column)) Then Return ""
            Return rdr(column).ToString()
        Catch
            ' The column was not part of this particular SELECT.
            Return ""
        End Try
    End Function

    ''' <summary>Read image_path from a reader, or "" when it was not selected.</summary>
    Public Function ReadPath(rdr As MySqlDataReader) As String
        Return ReadColumn(rdr, "image_path")
    End Function

    ''' <summary>
    ''' The payment terms a supplier can be given. Free-typed values from an
    ''' older row still display; this is what the dropdown offers.
    ''' </summary>
    Public ReadOnly Property PaymentTermOptions As String()
        Get
            Return New String() {"COD", "7 days", "15 days", "30 days", "45 days", "60 days"}
        End Get
    End Property

    ''' <summary>Folder supplier logos are copied into.</summary>
    Public ReadOnly Property ImageRoot As String
        Get
            Return Path.Combine(Application.StartupPath, "SupplierImages")
        End Get
    End Property

    ''' <summary>
    ''' Copy a chosen file into the app's own folder and return the path to
    ''' store. Empty string when the import failed, which callers treat as
    ''' "no photo picked".
    ''' </summary>
    Public Function Store(sourcePath As String) As String
        Try
            If Not Directory.Exists(ImageRoot) Then Directory.CreateDirectory(ImageRoot)
            Dim ext As String = Path.GetExtension(sourcePath)
            Dim fileName As String = "SUP_" & Guid.NewGuid().ToString("N").Substring(0, 12) & ext
            Dim dest As String = Path.Combine(ImageRoot, fileName)
            File.Copy(sourcePath, dest, True)
            Return ImageCache.ToStoredPath(dest)
        Catch ex As Exception
            AppModal.Show("Could not import the image:" & Environment.NewLine & ErrorLog.ForUser(ex),
                          "Image Error", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' A square thumbnail, centre-cropped so a wide logo is not squashed into
    ''' the avatar tile. Nothing when the path is empty or the file cannot be
    ''' read — every caller treats that as "draw the initial instead".
    ''' </summary>
    Public Function GetThumb(storedPath As String, size As Integer) As Image
        If String.IsNullOrWhiteSpace(storedPath) OrElse size <= 0 Then Return Nothing

        Dim full As String = ImageCache.ToFullPath(storedPath)
        Dim key As String = full & "|" & size
        If _cache.ContainsKey(key) Then Return _cache(key)

        Try
            If Not File.Exists(full) Then Return Nothing
            ' Read through a MemoryStream so the file is not left locked, which
            ' would stop the user replacing the logo they just picked.
            Dim bytes As Byte() = File.ReadAllBytes(full)
            Using ms As New MemoryStream(bytes)
                Using src As Image = Image.FromStream(ms)
                    Dim side As Integer = Math.Min(src.Width, src.Height)
                    Dim srcRect As New Rectangle((src.Width - side) \ 2,
                                                 (src.Height - side) \ 2, side, side)
                    Dim bmp As New Bitmap(size, size)
                    Using g As Graphics = Graphics.FromImage(bmp)
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality
                        g.DrawImage(src, New Rectangle(0, 0, size, size),
                                    srcRect.X, srcRect.Y, srcRect.Width, srcRect.Height,
                                    GraphicsUnit.Pixel)
                    End Using
                    _cache(key) = bmp
                    Return bmp
                End Using
            End Using
        Catch
            ' A corrupt or unreadable file must not take the screen down.
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' Drop the scaled avatars and dispose them once the UI thread has finished
    ''' the rebuild that replaced them. Call when a supplier screen re-renders,
    ''' so a logo replaced since the last render is re-read rather than served
    ''' from a stale bitmap.
    ''' </summary>
    Public Sub ClearCache(host As Control)
        ImageCache.ClearAndDispose(_cache, host)
    End Sub

    ''' <summary>
    ''' One avatar tile: the logo when there is one, the gradient initial when
    ''' there is not.
    ''' </summary>
    Public Sub Paint(g As Graphics, box As Rectangle, radius As Integer,
                     imagePath As String, initial As String, fontSize As Single)
        g.SmoothingMode = SmoothingMode.AntiAlias
        Using path As GraphicsPath = RoundedPath(box, radius)
            Dim img As Image = GetThumb(imagePath, Math.Max(1, box.Width))
            If img IsNot Nothing Then
                Dim oldClip As Region = g.Clip
                g.SetClip(path, CombineMode.Replace)
                g.InterpolationMode = InterpolationMode.HighQualityBicubic
                g.DrawImage(img, box)
                g.Clip = oldClip
                ' A hairline keeps a white-cornered logo from bleeding into a
                ' white card.
                Using pen As New Pen(Color.FromArgb(30, 0, 0, 0), 1.0F)
                    g.DrawPath(pen, path)
                End Using
            Else
                Using br As New LinearGradientBrush(box, MintPrimary, MintDeep, 135.0F)
                    g.FillPath(br, path)
                End Using
                Using f As New Font("Segoe UI", fontSize, FontStyle.Bold),
                      br As New SolidBrush(Color.White)
                    Dim sz As SizeF = g.MeasureString(initial, f)
                    g.DrawString(initial, f, br,
                                 box.X + (box.Width - sz.Width) / 2.0F,
                                 box.Y + (box.Height - sz.Height) / 2.0F)
                End Using
            End If
        End Using
    End Sub

    ''' <summary>First letter of a supplier name, or "?" when it is blank.</summary>
    Public Function InitialOf(name As String) As String
        If String.IsNullOrWhiteSpace(name) Then Return "?"
        Return name.Trim().Substring(0, 1).ToUpperInvariant()
    End Function

    ' Local copy of the rounded-rect path: this module is drawn from by panels
    ' that each keep their own private RoundRect, and reaching into one of them
    ' would couple the module to that panel.
    Private Function RoundedPath(rect As Rectangle, radius As Integer) As GraphicsPath
        Dim path As New GraphicsPath()
        If radius <= 0 Then
            path.AddRectangle(rect)
            path.CloseFigure()
            Return path
        End If
        Dim r As Integer = radius
        path.AddArc(rect.X, rect.Y, r, r, 180, 90)
        path.AddArc(rect.Right - r, rect.Y, r, r, 270, 90)
        path.AddArc(rect.Right - r, rect.Bottom - r, r, r, 0, 90)
        path.AddArc(rect.X, rect.Bottom - r, r, r, 90, 90)
        path.CloseFigure()
        Return path
    End Function

End Module
