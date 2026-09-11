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

    ' Grand total payable for a net (post-discount) amount.
    '
    ' VAT-inclusive: the shelf price already contains the tax, so the net amount
    ' IS the total. VAT-exclusive: the tax sits on top and must be added, or the
    ' customer is charged the ex-VAT figure and the store absorbs the tax.
    Public Function ComputeTotalDue(netAmount As Decimal) As Decimal
        If VatInclusive Then Return Math.Round(netAmount, 2)
        Return Math.Round(netAmount + ComputeVat(netAmount), 2)
    End Function

    ' ── Senior / PWD VAT-exempt sales ────────────────────────────────────────
    '  Under RA 9994 (Senior Citizens) and RA 10754 (PWD) these sales are VAT
    '  EXEMPT, and the 20% statutory discount is taken off the VAT-LESS base --
    '  not off the shelf price. Discounting the VAT-inclusive amount overcharges
    '  the customer and leaves output VAT recorded on a sale that owes none.
    '
    '  On a P1,000 VAT-inclusive sale at 12%:
    '     wrong :  1000 - 20%            = 800.00, VAT still reported
    '     right :  1000 / 1.12 = 892.86
    '              892.86 - 20%          = 714.29, VAT = 0.00
    Public Function IsVatExemptDiscount(discountType As String) As Boolean
        Return String.Equals(discountType, "Senior", StringComparison.OrdinalIgnoreCase) OrElse
               String.Equals(discountType, "PWD", StringComparison.OrdinalIgnoreCase)
    End Function

    ' The statutory discount rate for Senior/PWD sales.
    Public ReadOnly Property ExemptDiscountRate As Decimal
        Get
            Return 20D
        End Get
    End Property

    ' Strip VAT out of a shelf price to get the VAT-exempt selling base.
    ' With exclusive pricing the shelf price is already VAT-less.
    Public Function VatExemptBase(grossSubtotal As Decimal) As Decimal
        If Not VatInclusive Then Return Math.Round(grossSubtotal, 2)
        Dim rate As Decimal = VatRate
        If rate <= 0D Then Return Math.Round(grossSubtotal, 2)
        Return Math.Round(grossSubtotal * 100D / (100D + rate), 2)
    End Function

    ' ── The one place that turns a cart into money ───────────────────────────
    '  Every screen that shows or charges a total calls this, so the cart
    '  summary, the payment prompt, the committed sale and the receipt can
    '  never disagree about what the customer owes.
    '
    '  Returns the figures a sale and its receipt need:
    '    netSales   - VATable (or VAT-exempt) sales before VAT
    '    vat        - output VAT; always 0 on an exempt sale
    '    total      - what the customer actually pays
    Public Function ComputeSaleTotals(grossSubtotal As Decimal,
                                      discountAmount As Decimal,
                                      discountType As String) As (subtotal As Decimal, discount As Decimal, netSales As Decimal, vat As Decimal, total As Decimal, vatExempt As Boolean)
        Dim subtotal As Decimal = Math.Round(grossSubtotal, 2)
        If subtotal < 0D Then subtotal = 0D

        If IsVatExemptDiscount(discountType) Then
            ' Exempt: drop VAT first, then apply the statutory 20% to the base.
            ' The discount is always recomputed here rather than trusting the
            ' passed-in amount -- the rate is fixed by law, not by the cashier.
            Dim base As Decimal = VatExemptBase(subtotal)
            Dim disc As Decimal = Math.Round(base * ExemptDiscountRate / 100D, 2)
            If disc > base Then disc = base
            Dim net As Decimal = Math.Round(base - disc, 2)
            Return (subtotal, disc, net, 0D, net, True)
        End If

        Dim d As Decimal = Math.Round(discountAmount, 2)
        If d < 0D Then d = 0D
        If d > subtotal Then d = subtotal
        Dim netAmount As Decimal = Math.Round(subtotal - d, 2)
        Dim vatAmt As Decimal = ComputeVat(netAmount)
        Dim due As Decimal = ComputeTotalDue(netAmount)
        ' netSales is the pre-VAT figure. Inclusive pricing hides the VAT inside
        ' the net amount, so it has to come back out for the receipt breakdown.
        Dim netSales As Decimal = If(VatInclusive, Math.Round(netAmount - vatAmt, 2), netAmount)
        Return (subtotal, d, netSales, vatAmt, due, False)
    End Function

    ' VAT contained inside a known VAT-INCLUSIVE amount (e.g. a supplier invoice
    ' total). Unlike ComputeVat this ignores the store's inclusive/exclusive
    ' setting, because the caller already knows the amount includes tax.
    Public Function ExtractInclusiveVat(grossAmount As Decimal) As Decimal
        Dim rate As Decimal = VatRate
        If rate <= 0D Then Return 0D
        Return Math.Round(grossAmount * rate / (100D + rate), 2)
    End Function

    ' Display label for the VAT line, e.g. "VAT (12%)" or "VAT (12% excl)".
    ' The rate is configurable, so it must never be hardcoded in the UI.
    Public ReadOnly Property VatLabel As String
        Get
            Dim r As Decimal = VatRate
            ' Trim a trailing ".00" so 12.00 reads as "12".
            Dim shown As String = If(r = Math.Truncate(r), CInt(r).ToString(), r.ToString("0.##"))
            Return "VAT (" & shown & "%" & If(VatInclusive, "", " excl") & ")"
        End Get
    End Property

    ' ── Test hook ────────────────────────────────────────────────────────────
    '  Sets the VAT configuration directly and marks the module loaded, so the
    '  sale-math tests can pin an exact rate without a database behind them.
    '  Production code never calls this -- EnsureLoaded is the only other way
    '  _loaded becomes True, and it always reads the real settings row.
    Public Sub ConfigureForTest(rate As Decimal, inclusive As Boolean)
        ApplyDefaults()
        _vatRate = rate
        _vatInclusive = inclusive
        _loaded = True
    End Sub

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
