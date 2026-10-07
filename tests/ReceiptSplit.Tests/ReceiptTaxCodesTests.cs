using ReceiptSplit.Data;
using ReceiptSplit.Extraction;

namespace ReceiptSplit.Tests;

public class ReceiptTaxCodesTests
{
    [Theory]
    // Costco prints H, T&T prints G P.
    [InlineData("H", true)]
    [InlineData("G P", true)]
    [InlineData("h", true)]
    [InlineData("G,P", true)]
    [InlineData("HST", true)]
    // Costco US marks taxable lines A and exempt ones E.
    [InlineData("A", true)]
    [InlineData("E", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    // Non-taxable markers other chains print.
    [InlineData("N", false)]
    [InlineData("NT", false)]
    [InlineData("F", false)]
    [InlineData("Z", false)]
    public void Reads_printed_tax_codes(string? code, bool expected) =>
        Assert.Equal(expected, ReceiptTaxCodes.IsTaxed(code));

    [Fact]
    public void A_receipt_that_charges_tax_but_marks_nothing_taxes_every_line()
    {
        var lines = Lines(null, null);

        ReceiptTaxCodes.AssumeAllTaxedWhenNoneAre(lines, 1.50m);

        Assert.All(lines, line => Assert.True(line.IsTaxed));
    }

    [Fact]
    public void Lines_read_as_exempt_stay_exempt_when_some_line_is_taxed()
    {
        var lines = Lines("H", "E");

        ReceiptTaxCodes.AssumeAllTaxedWhenNoneAre(lines, 1.50m);

        Assert.Equal([true, false], lines.Select(line => line.IsTaxed));
    }

    [Fact]
    public void A_receipt_without_tax_keeps_every_line_untaxed()
    {
        foreach (var tax in new decimal?[] { null, 0m })
        {
            var lines = Lines(null, null);

            ReceiptTaxCodes.AssumeAllTaxedWhenNoneAre(lines, tax);

            Assert.All(lines, line => Assert.False(line.IsTaxed));
        }
    }

    private static List<ReceiptLine> Lines(params string?[] codes) =>
        [.. codes.Select((code, position) => new ReceiptLine
        {
            Position = position,
            Name = $"Item {position}",
            TaxCode = code,
            Amount = 10m,
            IsTaxed = ReceiptTaxCodes.IsTaxed(code),
        })];
}
