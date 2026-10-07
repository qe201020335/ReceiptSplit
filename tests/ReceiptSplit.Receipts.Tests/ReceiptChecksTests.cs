using System.Globalization;
using ReceiptSplit.Data;

namespace ReceiptSplit.Receipts.Tests;

public class ReceiptChecksTests
{
    private const decimal Ontario = 13m;

    /// <summary>4.99 taxed less a 1.00 discount, plus 2.00 untaxed: 3.99 taxable, 0.52 tax at 13%.</summary>
    private static readonly (decimal Amount, bool IsTaxed)[] Lines = [(4.99m, true), (-1.00m, true), (2.00m, false)];

    [Fact]
    public void Passes_when_lines_totals_and_tax_agree()
    {
        var result = ReceiptChecks.Evaluate(Lines, subtotal: 5.99m, tax: 0.52m, total: 6.51m, Ontario);

        Assert.Equal(
            new ReceiptCheckResult(5.99m, true, Discount: 0m, true, TaxedSum: 3.99m, ExpectedTax: 0.52m, TaxMatches: true),
            result);
        Assert.True(result.Passed);
        Assert.Equal(ReceiptStatus.Completed, ReceiptChecks.StatusFor(result));
    }

    [Fact]
    public void Flags_lines_that_do_not_add_up_to_the_subtotal()
    {
        var result = ReceiptChecks.Evaluate(Lines, subtotal: 6.99m, tax: 0.52m, total: 7.51m, Ontario);

        Assert.False(result.LinesMatchSubtotal);
        Assert.True(result.TotalMatches);
        Assert.True(result.TaxMatches);
        Assert.False(result.Passed);
        Assert.Equal(ReceiptStatus.NeedsReview, ReceiptChecks.StatusFor(result));
    }

    [Fact]
    public void Flags_a_total_that_is_not_subtotal_plus_tax()
    {
        var result = ReceiptChecks.Evaluate(Lines, subtotal: 5.99m, tax: 0.52m, total: 6.61m, Ontario);

        Assert.True(result.LinesMatchSubtotal);
        Assert.False(result.TotalMatches);
    }

    [Fact]
    public void Flags_tax_that_does_not_match_the_taxed_lines()
    {
        // The whole subtotal taxed at 13% would be 0.78, so the receipt's 0.52 only fits the taxed lines.
        var result = ReceiptChecks.Evaluate(Lines, subtotal: 5.99m, tax: 0.78m, total: 6.77m, Ontario);

        Assert.True(result.LinesMatchSubtotal);
        Assert.True(result.TotalMatches);
        Assert.False(result.TaxMatches);
        Assert.Equal(0.52m, result.ExpectedTax);
        Assert.False(result.Passed);
    }

    [Theory]
    // Stores that round tax per line can land a cent either side of the rate on the taxed subtotal.
    [InlineData("0.53", true)]
    [InlineData("0.51", true)]
    [InlineData("0.54", false)]
    [InlineData("0.50", false)]
    public void Allows_a_cent_of_rounding(string printedTax, bool matches)
    {
        var tax = decimal.Parse(printedTax, CultureInfo.InvariantCulture);

        Assert.Equal(matches, ReceiptChecks.Evaluate(Lines, subtotal: 5.99m, tax: tax, total: 5.99m + tax, Ontario).TaxMatches);
    }

    [Fact]
    public void Treats_missing_tax_as_zero_on_an_untaxed_receipt()
    {
        (decimal, bool)[] untaxed = [(4.99m, false), (1.00m, false)];

        Assert.True(ReceiptChecks.Evaluate(untaxed, subtotal: 5.99m, tax: null, total: 5.99m, Ontario).Passed);
    }

    [Fact]
    public void Flags_a_receipt_with_tax_but_no_taxed_lines()
    {
        (decimal, bool)[] untaxed = [(4.99m, false), (1.00m, false)];

        var result = ReceiptChecks.Evaluate(untaxed, subtotal: 5.99m, tax: 0.78m, total: 6.77m, Ontario);

        Assert.Equal(0m, result.ExpectedTax);
        Assert.False(result.TaxMatches);
    }

    [Fact]
    public void Uses_the_receipts_own_rate()
    {
        // Alberta charges 5% GST: 3.99 taxable is 0.20.
        var result = ReceiptChecks.Evaluate(Lines, subtotal: 5.99m, tax: 0.20m, total: 6.19m, taxRatePercent: 5m);

        Assert.Equal(0.20m, result.ExpectedTax);
        Assert.True(result.Passed);
    }

    /// <summary>A Target receipt in Texas: "10%off Storewide" under the subtotal, and tax "8.25000 on $361.21".</summary>
    private static readonly (decimal Amount, bool IsTaxed)[] TargetLines =
        [(13.99m, false), (349.99m, true), (25.99m, true), (10.99m, true), (12.99m, true), (1.39m, true)];

    [Fact]
    public void Takes_a_storewide_discount_off_the_total_and_the_taxed_amount()
    {
        var result = ReceiptChecks.Evaluate(
            TargetLines, subtotal: 415.34m, tax: 29.80m, total: 403.60m, taxRatePercent: 8.25m, discountPercent: 10m);

        // Rounded per line as the receipt does; 10% of the whole 415.34 would be 41.53.
        Assert.Equal(-41.54m, result.Discount);
        Assert.Equal(361.21m, result.TaxedSum);
        Assert.Equal(29.80m, result.ExpectedTax);
        Assert.True(result.Passed);
    }

    [Fact]
    public void Flags_a_storewide_discount_the_totals_do_not_show()
    {
        var result = ReceiptChecks.Evaluate(
            TargetLines, subtotal: 415.34m, tax: 29.80m, total: 403.60m, taxRatePercent: 8.25m, discountPercent: 0m);

        Assert.True(result.LinesMatchSubtotal);
        Assert.False(result.TotalMatches);
        Assert.False(result.TaxMatches);
    }

    /// <summary>A MIXUE receipt from Japan: 合計 1,110 with (内消費税 8%) 83 already inside the prices.</summary>
    private static readonly (decimal Amount, bool IsTaxed)[] MixueLines = [(130m, true), (500m, true), (480m, true)];

    [Theory]
    [InlineData("83")]
    [InlineData("82")]
    [InlineData("0")]
    public void Does_not_add_or_check_tax_that_is_included_in_the_prices(string printedTax)
    {
        var tax = decimal.Parse(printedTax, CultureInfo.InvariantCulture);

        var result = ReceiptChecks.Evaluate(
            MixueLines, subtotal: 1110m, tax: tax, total: 1110m, taxRatePercent: 8m, taxIncluded: true);

        Assert.True(result.Passed);
    }

    [Fact]
    public void Still_checks_the_lines_and_total_when_tax_is_included()
    {
        var misread = ReceiptChecks.Evaluate(
            MixueLines, subtotal: 1110m, tax: 83m, total: 1193m, taxRatePercent: 8m, taxIncluded: true);
        var taxOnTop = ReceiptChecks.Evaluate(MixueLines, subtotal: 1110m, tax: 83m, total: 1110m, taxRatePercent: 8m);

        Assert.True(misread.LinesMatchSubtotal);
        Assert.False(misread.TotalMatches);
        Assert.False(taxOnTop.TotalMatches);
        Assert.False(taxOnTop.TaxMatches);
    }

    [Fact]
    public void Fails_without_printed_totals()
    {
        var result = ReceiptChecks.Evaluate(Lines, subtotal: null, tax: null, total: null, Ontario);

        Assert.False(result.LinesMatchSubtotal);
        Assert.False(result.TotalMatches);
        Assert.False(result.TaxMatches);
    }
}
