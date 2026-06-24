Imports System.Configuration
Imports MySql.Data.MySqlClient

Public Module Db
    ' One place that knows how to open a connection.
    Public Function GetConnection() As MySqlConnection
        Dim cs As String = ConfigurationManager.ConnectionStrings("PosDb").ConnectionString
        Return New MySqlConnection(cs)
    End Function
End Module