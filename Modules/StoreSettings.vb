Imports MySql.Data.MySqlClient

' =============================================================================
'  StoreSettings  —  cached accessor for the single store_settings row.
'
'  The POS sale path reads VAT configuration from here on every recalculation,
'  so the row is loaded once and cached. Call Reload() after saving settings.
'
'  IMPORTANT: the fallback defaults below are exactly the values the POS had
'  hardcoded before this module existed (name "PAINT STORE POS", tagline
'  "Sales Receipt", 12% VAT-inclusive). If the table is missing or unreadable,
'  every figure the POS computes is identical to the pre-settings behaviour —
'  a broken settings read can never change sale math.
' =============================================================================
Public Module StoreSettings

    Private Const DEF_NAME As String = "PAINT STORE POS"
    Private Const DEF_TAGLINE As String = "Sales Receipt"
    Private Const DEF_VAT_INCLUSIVE As Boolean = True
    Private ReadOnly DEF_VAT_RATE As Decimal = 12D

    Private _loaded As Boolean = False

    Private _storeName As String = DEF_NAME
    Private _storeTagline As String = DEF_TAGLINE
    Private _storeAddress As String = ""
    Private _storeContact As String = ""
    Private _storeTin As String = ""
    Private _vatRate As Decimal = DEF_VAT_RATE
    Private _vatInclusive As Boolean = DEF_VAT_INCLUSIVE

    ' ── Typed properties ─────────────────────────────────────────────────────
    Public ReadOnly Property StoreName As String
        Get
            EnsureLoaded()
            Return _storeName
        End Get
    End Property

    Public ReadOnly Property StoreTagline As String
        Get
            EnsureLoaded()
            Return _storeTagline
        End Get
    End Property

    Public ReadOnly Property StoreAddress As String
        Get
            EnsureLoaded()
            Return _storeAddress
        End Get
    End Property

    Public ReadOnly Property StoreContact As String
        Get
            EnsureLoaded()
            Return _storeContact
        End Get
    End Property

    Public ReadOnly Property StoreTin As String
        Get
            EnsureLoaded()
            Return _storeTin
        End Get
    End Property

    ' VAT rate as a percent, e.g. 12.00 means 12%.
    Public ReadOnly Property VatRate As Decimal
        Get
            EnsureLoaded()
            Return _vatRate
        End Get
    End Property

    ' True when displayed prices already contain VAT.
    Public ReadOnly Property VatInclusive As Boolean
        Get
            EnsureLoaded()
            Return _vatInclusive
        End Get
    End Property

    ' ── VAT helper ───────────────────────────────────────────────────────────
    '  inclusive : the amount already contains VAT -> amount * rate / (100 + rate)
    '  exclusive : VAT sits on top of the amount   -> amount * rate / 100
    '
    '  At rate = 12 with VatInclusive = True this is (amount * 12D) / 112D —
    '  the same operands, in the same order, as the expression the POS used
    '  before settings existed. Decimal arithmetic makes that bit-identical,
    '  not merely close.
    Public Function ComputeVat(amount As Decimal) As Decimal
        Dim rate As Decimal = VatRate
        If rate < 0D Then rate = 0D
        If VatInclusive Then
            Return Math.Round(amount * rate / (100D + rate), 2)
        End If
        Return Math.Round(amount * rate / 100D, 2)
    End Function

    ' ── Loading ──────────────────────────────────────────────────────────────
    Public Sub Reload()
        _loaded = False
        EnsureLoaded()
    End Sub

    Private Sub EnsureLoaded()
        If _loaded Then Return

        ' Marked loaded up front: these properties are read from paint/recalc
        ' paths, and a missing table must not mean a DB round trip per read.
        ' Reload() is the way back once the table exists.
        _loaded = True
        ApplyDefaults()

        Try
            Using conn = Db.GetConnection()
                conn.Open()
                Using cmd As New MySqlCommand(
                    "SELECT store_name, store_tagline, store_address, store_contact, " &
                    "store_tin, vat_rate, vat_inclusive " &
                    "FROM store_settings WHERE setting_id = @id LIMIT 1", conn)
                    cmd.Parameters.AddWithValue("@id", 1)
                    Using rdr = cmd.ExecuteReader()
                        If rdr.Read() Then
                            _storeName = TextOr(rdr("store_name"), DEF_NAME)
                            _storeTagline = TextOr(rdr("store_tagline"), DEF_TAGLINE)
                            _storeAddress = TextOr(rdr("store_address"), "")
                            _storeContact = TextOr(rdr("store_contact"), "")
                            _storeTin = TextOr(rdr("store_tin"), "")
                            If Not IsDBNull(rdr("vat_rate")) Then
                                _vatRate = Convert.ToDecimal(rdr("vat_rate"))
                            End If
                            If Not IsDBNull(rdr("vat_inclusive")) Then
                                _vatInclusive = Convert.ToBoolean(rdr("vat_inclusive"))
                            End If
                        End If
                    End Using
                End Using
            End Using
        Catch ex As Exception
            ' Never surface a dialog here — this runs inside the sale path.
            System.Diagnostics.Debug.WriteLine("StoreSettings load error: " & ex.Message)
            ApplyDefaults()
        End Try
    End Sub

    Private Sub ApplyDefaults()
        _storeName = DEF_NAME
        _storeTagline = DEF_TAGLINE
        _storeAddress = ""
        _storeContact = ""
        _storeTin = ""
        _vatRate = DEF_VAT_RATE
        _vatInclusive = DEF_VAT_INCLUSIVE
    End Sub

    Private Function TextOr(o As Object, fallback As String) As String
        If o Is Nothing OrElse IsDBNull(o) Then Return fallback
        Dim s As String = o.ToString()
        If s = "" Then Return fallback
        Return s
    End Function

End Module
