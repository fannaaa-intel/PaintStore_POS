Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

' =============================================================================
'  ImageCache
'
'  The panel builders each keep a Dictionary(Of String, Image) of scaled
'  thumbnails and call .Clear() when the screen is rebuilt. Clear() only drops
'  the references -- a Bitmap holds an unmanaged GDI+ handle that is released
'  when the finalizer eventually runs, so revisiting an image-heavy screen leaked
'  handles until drawing began to fail.
'
'  Disposing at the point of Clear() is not safe on its own: the bitmaps are
'  still assigned to Control.BackgroundImage / PictureBox.Image on controls that
'  have not been torn down yet, and painting a disposed Bitmap throws. The
'  disposal is therefore posted back to the message loop with BeginInvoke, so it
'  runs after the current rebuild has finished and the old controls are gone.
' =============================================================================
Public Module ImageCache

    ''' <summary>
    ''' Empty an image cache and dispose what it held, once the UI thread has
    ''' finished the rebuild that replaced those images.
    ''' </summary>
    ''' <param name="cache">The builder's thumbnail dictionary.</param>
    ''' <param name="host">
    ''' Any live control on the UI thread, used to defer the disposal. When it is
    ''' Nothing or its handle is gone the images are released immediately, which
    ''' is correct because there is no surviving control left to paint them.
    ''' </param>
    Public Sub ClearAndDispose(cache As Dictionary(Of String, Image), host As Control)
        If cache Is Nothing OrElse cache.Count = 0 Then Return

        ' Snapshot then clear, so callers repopulate an empty cache immediately
        ' while the old bitmaps are still pending disposal.
        Dim stale As New List(Of Image)(cache.Values)
        cache.Clear()

        If host Is Nothing OrElse host.IsDisposed OrElse Not host.IsHandleCreated Then
            DisposeAll(stale)
            Return
        End If

        Try
            host.BeginInvoke(New MethodInvoker(Sub() DisposeAll(stale)))
        Catch
            ' BeginInvoke can fail if the handle died between the check and here.
            ' Falling back to an immediate dispose is safe for the same reason.
            DisposeAll(stale)
        End Try
    End Sub

    ''' <summary>Folder product images are copied into.</summary>
    Public ReadOnly Property ImageRoot As String
        Get
            Return IO.Path.Combine(Application.StartupPath, "ProductImages")
        End Get
    End Property

    ''' <summary>
    ''' Store a product image path in the DB relative to the app folder, e.g.
    ''' "ProductImages\PROD_x.png", so the row survives the app being installed
    ''' somewhere else.
    ''' </summary>
    Public Function ToStoredPath(fullPath As String) As String
        If String.IsNullOrWhiteSpace(fullPath) Then Return ""
        Try
            Dim root As String = Application.StartupPath
            If fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) Then
                Return fullPath.Substring(root.Length).TrimStart(
                    IO.Path.DirectorySeparatorChar, IO.Path.AltDirectorySeparatorChar)
            End If
        Catch
        End Try
        Return fullPath
    End Function

    ''' <summary>
    ''' Turn a stored image_path back into a path that can actually be opened.
    '''
    ''' Rows written before this existed hold an absolute path baked to one
    ''' machine and one checkout folder. Anywhere else that resolves to nothing
    ''' and the picture silently disappeared. Such rows are recovered by looking
    ''' the file name up inside this installation's ProductImages folder.
    ''' </summary>
    Public Function ToFullPath(storedPath As String) As String
        If String.IsNullOrWhiteSpace(storedPath) Then Return ""

        Try
            ' Relative: anchor it to this installation.
            If Not IO.Path.IsPathRooted(storedPath) Then
                Return IO.Path.Combine(Application.StartupPath, storedPath)
            End If

            ' Absolute and present -- this machine wrote it.
            If IO.File.Exists(storedPath) Then Return storedPath

            ' Absolute but missing: an old row from another machine or install
            ' path. The file name is still valid inside our own image folder.
            Dim name As String = IO.Path.GetFileName(storedPath)
            If name <> "" Then
                Dim local As String = IO.Path.Combine(ImageRoot, name)
                If IO.File.Exists(local) Then Return local
            End If
        Catch
            ' A malformed path must not take the screen down; callers treat an
            ' unreadable path as "no image".
        End Try

        Return storedPath
    End Function

    Private Sub DisposeAll(images As List(Of Image))
        For Each img In images
            Try
                If img IsNot Nothing Then img.Dispose()
            Catch
                ' A bitmap already disposed elsewhere must not break the rest.
            End Try
        Next
    End Sub

End Module
