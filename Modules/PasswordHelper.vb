Imports System.Security.Cryptography

Public Module PasswordHelper

    Private Const Iterations As Integer = 100000
    Private Const SaltSize As Integer = 16   ' bytes
    Private Const KeySize As Integer = 32    ' bytes

    ' Create a new hash to store in the database (used later for Sign Up).
    ' Returns: "iterations:saltBase64:hashBase64"
    Public Function HashPassword(password As String) As String
        Dim salt(SaltSize - 1) As Byte
        Using rng As New RNGCryptoServiceProvider()
            rng.GetBytes(salt)
        End Using

        Dim hash As Byte()
        Using pbkdf2 As New Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256)
            hash = pbkdf2.GetBytes(KeySize)
        End Using

        Return Iterations & ":" &
               Convert.ToBase64String(salt) & ":" &
               Convert.ToBase64String(hash)
    End Function

    ' Check a typed password against a stored hash.
    Public Function VerifyPassword(password As String, stored As String) As Boolean
        If String.IsNullOrEmpty(stored) Then Return False

        Dim parts = stored.Split(":"c)
        If parts.Length <> 3 Then Return False

        Dim iters As Integer
        If Not Integer.TryParse(parts(0), iters) Then Return False

        Dim salt As Byte()
        Dim expected As Byte()
        Try
            salt = Convert.FromBase64String(parts(1))
            expected = Convert.FromBase64String(parts(2))
        Catch
            Return False
        End Try

        Dim actual As Byte()
        Using pbkdf2 As New Rfc2898DeriveBytes(password, salt, iters, HashAlgorithmName.SHA256)
            actual = pbkdf2.GetBytes(expected.Length)
        End Using

        ' constant-time compare
        Dim diff As Integer = expected.Length Xor actual.Length
        For i As Integer = 0 To Math.Min(expected.Length, actual.Length) - 1
            diff = diff Or (expected(i) Xor actual(i))
        Next
        Return diff = 0
    End Function

End Module