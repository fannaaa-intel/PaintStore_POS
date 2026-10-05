' =============================================================================
'  SaleMathTests  —  unit tests for the sale money math.
'
'  These cover the arithmetic that decides what a customer is charged and what
'  is reported to BIR: VAT extraction, the Senior/PWD exemption, discounts and
'  change due. That math had no test at all, and the pre-flight script can only
'  check that a query parses -- it cannot catch a total that is merely wrong.
'
'  Run with:  Tests\run-tests.cmd
'
'  The tests compile StoreSettings.vb itself rather than reimplementing the
'  formulas, so a test passing means the shipped code is correct. StoreSettings
'  reads its VAT configuration from the database on first use; these tests set
'  it directly through the test hook so they need no database and always run
'  against known rates.
' =============================================================================
Imports System
Imports System.Linq

Module SaleMathTests

    Private _pass As Integer = 0
    Private _fail As Integer = 0

    Sub Main()
        Console.WriteLine("==============================================")
        Console.WriteLine(" PaintStore POS - sale math tests")
        Console.WriteLine("==============================================")

        VatInclusiveTests()
        VatExclusiveTests()
        SeniorPwdExemptionTests()
        OrdinaryDiscountTests()
        EdgeCaseTests()
        ChangeDueTests()
        RefundPaidPriceTests()
        PromoApprovalTests()

        Console.WriteLine("")
        Console.WriteLine("----------------------------------------------")
        Console.WriteLine(String.Format(" {0} passed, {1} failed", _pass, _fail))
        Console.WriteLine("----------------------------------------------")
        If _fail > 0 Then Environment.Exit(1)
        Environment.Exit(0)
    End Sub

    ' ── assertions ───────────────────────────────────────────────────────────
    Private Sub Eq(actual As Decimal, expected As Decimal, label As String)
        If actual = expected Then
            _pass += 1
            Console.WriteLine(String.Format("  PASS  {0}", label))
        Else
            _fail += 1
            Console.WriteLine(String.Format("  FAIL  {0}", label))
            Console.WriteLine(String.Format("          expected {0}, got {1}", expected, actual))
        End If
    End Sub

    Private Sub IsTrue(actual As Boolean, label As String)
        If actual Then
            _pass += 1
            Console.WriteLine(String.Format("  PASS  {0}", label))
        Else
            _fail += 1
            Console.WriteLine(String.Format("  FAIL  {0}  (expected True)", label))
        End If
    End Sub

    ' ── 12% VAT-inclusive: the shop's normal configuration ───────────────────
    Private Sub VatInclusiveTests()
        Console.WriteLine("")
        Console.WriteLine("[1] VAT inclusive (12%)")
        StoreSettings.ConfigureForTest(12D, True)

        ' P1,120 gross contains P120 of VAT: 1120 * 12 / 112.
        Eq(StoreSettings.ComputeVat(1120D), 120D, "VAT inside 1120 is 120.00")
        ' Inclusive pricing: the shelf price IS the total.
        Eq(StoreSettings.ComputeTotalDue(1120D), 1120D, "total due equals the net amount")
        Eq(StoreSettings.VatExemptBase(1120D), 1000D, "VAT-less base of 1120 is 1000.00")

        Dim t = StoreSettings.ComputeSaleTotals(1120D, 0D, "None")
        Eq(t.total, 1120D, "no discount: total is 1120.00")
        Eq(t.vat, 120D, "no discount: VAT is 120.00")
        Eq(t.netSales, 1000D, "no discount: VATable sales is 1000.00")
        IsTrue(Not t.vatExempt, "no discount: sale is not VAT exempt")
    End Sub

    ' ── VAT-exclusive: tax added on top ──────────────────────────────────────
    Private Sub VatExclusiveTests()
        Console.WriteLine("")
        Console.WriteLine("[2] VAT exclusive (12%)")
        StoreSettings.ConfigureForTest(12D, False)

        Eq(StoreSettings.ComputeVat(1000D), 120D, "VAT on top of 1000 is 120.00")
        Eq(StoreSettings.ComputeTotalDue(1000D), 1120D, "total due is 1120.00")

        Dim t = StoreSettings.ComputeSaleTotals(1000D, 0D, "None")
        Eq(t.total, 1120D, "exclusive: customer pays 1120.00")
        Eq(t.vat, 120D, "exclusive: VAT is 120.00")
        Eq(t.netSales, 1000D, "exclusive: VATable sales is 1000.00")

        ' Restore the normal configuration for the tests that follow.
        StoreSettings.ConfigureForTest(12D, True)
    End Sub

    ' ── Senior / PWD: the bug this suite exists for ──────────────────────────
    Private Sub SeniorPwdExemptionTests()
        Console.WriteLine("")
        Console.WriteLine("[3] Senior / PWD VAT exemption")
        StoreSettings.ConfigureForTest(12D, True)

        IsTrue(StoreSettings.IsVatExemptDiscount("Senior"), "Senior is a VAT-exempt type")
        IsTrue(StoreSettings.IsVatExemptDiscount("PWD"), "PWD is a VAT-exempt type")
        IsTrue(Not StoreSettings.IsVatExemptDiscount("Promo"), "Promo is NOT VAT exempt")
        IsTrue(Not StoreSettings.IsVatExemptDiscount("None"), "None is NOT VAT exempt")

        ' The worked example from the law:
        '   1000 gross -> 892.86 VAT-less base -> less 20% -> 714.29 payable.
        ' The old code charged 800.00 and still booked VAT on an exempt sale.
        Dim t = StoreSettings.ComputeSaleTotals(1000D, 0D, "Senior")
        Eq(t.discount, 178.57D, "Senior: 20% of the VAT-less base is 178.57")
        Eq(t.total, 714.29D, "Senior: customer pays 714.29 (NOT 800.00)")
        Eq(t.vat, 0D, "Senior: VAT is zero on an exempt sale")
        Eq(t.netSales, 714.29D, "Senior: VAT-exempt sales is 714.29")
        IsTrue(t.vatExempt, "Senior: flagged VAT exempt")

        Dim p = StoreSettings.ComputeSaleTotals(1000D, 0D, "PWD")
        Eq(p.total, 714.29D, "PWD: same treatment as Senior")
        Eq(p.vat, 0D, "PWD: VAT is zero")

        ' The statutory rate is fixed by law: a cashier-entered amount must be
        ' ignored rather than allowed to override it in either direction.
        Dim forced = StoreSettings.ComputeSaleTotals(1000D, 999D, "Senior")
        Eq(forced.discount, 178.57D, "Senior: a typed discount cannot override 20%")
        Eq(forced.total, 714.29D, "Senior: total holds despite a typed discount")

        ' A clean 1,120 case where the base divides exactly.
        Dim c = StoreSettings.ComputeSaleTotals(1120D, 0D, "Senior")
        Eq(c.discount, 200D, "Senior on 1120: discount is exactly 200.00")
        Eq(c.total, 800D, "Senior on 1120: customer pays 800.00")
        Eq(c.vat, 0D, "Senior on 1120: VAT is zero")
    End Sub

    ' ── Ordinary (Promo) discounts still behave ──────────────────────────────
    Private Sub OrdinaryDiscountTests()
        Console.WriteLine("")
        Console.WriteLine("[4] Ordinary discounts")
        StoreSettings.ConfigureForTest(12D, True)

        Dim t = StoreSettings.ComputeSaleTotals(1120D, 120D, "Promo")
        Eq(t.discount, 120D, "Promo: the typed discount is honoured")
        Eq(t.total, 1000D, "Promo: customer pays 1000.00")
        ' VAT still applies -- a promo is not a tax exemption.
        Eq(t.vat, 107.14D, "Promo: VAT recomputed on the discounted amount")
        IsTrue(Not t.vatExempt, "Promo: not flagged VAT exempt")

        ' Discount larger than the cart must clamp, never go negative.
        Dim over = StoreSettings.ComputeSaleTotals(500D, 900D, "Promo")
        Eq(over.discount, 500D, "discount clamps to the subtotal")
        Eq(over.total, 0D, "over-discounted sale totals 0.00, never negative")

        Dim neg = StoreSettings.ComputeSaleTotals(500D, -50D, "Promo")
        Eq(neg.discount, 0D, "a negative discount is treated as zero")
        Eq(neg.total, 500D, "a negative discount cannot inflate the total")
    End Sub

    ' ── Edge cases ───────────────────────────────────────────────────────────
    Private Sub EdgeCaseTests()
        Console.WriteLine("")
        Console.WriteLine("[5] Edge cases")

        StoreSettings.ConfigureForTest(12D, True)
        Dim z = StoreSettings.ComputeSaleTotals(0D, 0D, "None")
        Eq(z.total, 0D, "empty cart totals 0.00")
        Eq(z.vat, 0D, "empty cart has no VAT")

        Dim n = StoreSettings.ComputeSaleTotals(-100D, 0D, "None")
        Eq(n.subtotal, 0D, "a negative subtotal is floored at zero")

        ' A zero VAT rate must not divide by zero or report phantom tax.
        StoreSettings.ConfigureForTest(0D, True)
        Eq(StoreSettings.ComputeVat(1000D), 0D, "0% rate reports no VAT")
        Eq(StoreSettings.VatExemptBase(1000D), 1000D, "0% rate leaves the base unchanged")
        Dim zr = StoreSettings.ComputeSaleTotals(1000D, 0D, "Senior")
        Eq(zr.total, 800D, "0% rate: Senior is a plain 20% off")

        ' Rounding: a third of a peso must not leak fractions into the total.
        StoreSettings.ConfigureForTest(12D, True)
        Dim r = StoreSettings.ComputeSaleTotals(33.33D, 0D, "Senior")
        Eq(Decimal.Round(r.total, 2), r.total, "exempt total is rounded to 2 places")
        Eq(Decimal.Round(r.discount, 2), r.discount, "exempt discount is rounded to 2 places")

        StoreSettings.ConfigureForTest(12D, True)
    End Sub

    ' ── Change due ───────────────────────────────────────────────────────────
    Private Sub ChangeDueTests()
        Console.WriteLine("")
        Console.WriteLine("[6] Change due")
        StoreSettings.ConfigureForTest(12D, True)

        Dim t = StoreSettings.ComputeSaleTotals(1120D, 0D, "None")
        Eq(1500D - t.total, 380D, "1500 tendered on 1120 gives 380.00 change")
        Eq(1120D - t.total, 0D, "exact payment gives no change")

        ' Change on an exempt sale comes off the reduced total, not the shelf price.
        Dim sr = StoreSettings.ComputeSaleTotals(1000D, 0D, "Senior")
        Eq(1000D - sr.total, 285.71D, "1000 tendered on a Senior sale gives 285.71 change")
    End Sub

    ' ── Refunds pay back what was PAID, not the shelf price ──────────────────
    '  Mirrors the Returns screen: per-unit paid price from PaidUnitPrice, the
    '  selection summed and rounded once.
    Private Function RefundOf(lines As (shelf As Decimal, qty As Integer)(),
                              saleTotal As Decimal) As Decimal
        Dim linesTotal As Decimal = lines.Sum(Function(l) l.shelf * l.qty)
        Return Decimal.Round(lines.Sum(Function(l) l.qty *
            StoreSettings.PaidUnitPrice(l.shelf, saleTotal, linesTotal)), 2)
    End Function

    Private Sub RefundPaidPriceTests()
        Console.WriteLine("")
        Console.WriteLine("[7] Refund paid price")
        StoreSettings.ConfigureForTest(12D, True)

        ' The reported bug: P1,000 Senior sale collected 714.29 but refunded 1,000.
        Dim sr = StoreSettings.ComputeSaleTotals(1000D, 0D, "Senior")
        Eq(RefundOf({(1000D, 1)}, sr.total), 714.29D, "Senior sale refunds 714.29, not 1000.00")

        ' No discount: refund equals the shelf price, exactly as before.
        Eq(StoreSettings.PaidUnitPrice(550D, 550D, 550D), 550D, "undiscounted: paid price is the shelf price")

        ' Promo P100 off a P1,000 cart of two lines: each line gives back its share.
        Eq(RefundOf({(600D, 1)}, 900D), 900D, "single line gets the whole paid total")
        Dim twoLines = {(600D, 1), (400D, 1)}
        Dim lt As Decimal = 1000D
        Eq(Decimal.Round(StoreSettings.PaidUnitPrice(600D, 900D, lt), 2), 540D, "promo: 600 line refunds 540.00")
        Eq(Decimal.Round(StoreSettings.PaidUnitPrice(400D, 900D, lt), 2), 360D, "promo: 400 line refunds 360.00")
        Eq(RefundOf(twoLines, 900D), 900D, "promo: whole sale refunds exactly what was paid")

        ' Awkward thirds: three lines refunded one at a time never exceed the total.
        Dim thirdsTotal As Decimal = StoreSettings.ComputeSaleTotals(99.99D, 0D, "Senior").total
        Dim oneThird As Decimal = Decimal.Round(StoreSettings.PaidUnitPrice(33.33D, thirdsTotal, 99.99D), 2)
        IsTrue(oneThird * 3D - thirdsTotal <= 0.05D, "thirds: three partial refunds stay within 5 centavos of the total")
        Eq(RefundOf({(33.33D, 3)}, thirdsTotal), thirdsTotal, "thirds: one full refund is exact")

        ' VAT-exclusive: the customer paid VAT on top, so it comes back too.
        StoreSettings.ConfigureForTest(12D, False)
        Dim ex = StoreSettings.ComputeSaleTotals(1000D, 0D, "None")
        Eq(RefundOf({(1000D, 1)}, ex.total), 1120D, "exclusive: refund includes the VAT paid (1120.00)")

        ' Zero-value sale cannot divide by zero.
        Eq(StoreSettings.PaidUnitPrice(0D, 0D, 0D), 0D, "zero sale: paid price is 0")

        StoreSettings.ConfigureForTest(12D, True)
    End Sub

    ' ── Promo discounts over 10% need an Admin ───────────────────────────────
    Private Sub PromoApprovalTests()
        Console.WriteLine("")
        Console.WriteLine("[8] Promo approval rule")

        IsTrue(Not StoreSettings.PromoNeedsApproval("Promo", 100D, 1000D), "exactly 10% needs no Admin")
        IsTrue(StoreSettings.PromoNeedsApproval("Promo", 100.01D, 1000D), "just over 10% needs an Admin")
        IsTrue(StoreSettings.PromoNeedsApproval("Promo", 1000D, 1000D), "100% needs an Admin")
        IsTrue(Not StoreSettings.PromoNeedsApproval("Promo", 0D, 1000D), "no discount needs no Admin")
        IsTrue(Not StoreSettings.PromoNeedsApproval("Senior", 178.57D, 1000D), "Senior (fixed by law) needs no Admin")
        IsTrue(Not StoreSettings.PromoNeedsApproval("PWD", 178.57D, 1000D), "PWD (fixed by law) needs no Admin")
        ' Cart shrank after a 10% discount: same P100 is now 50% of P200.
        IsTrue(StoreSettings.PromoNeedsApproval("Promo", 100D, 200D), "10% that became 50% after removing items needs an Admin")
    End Sub

End Module
