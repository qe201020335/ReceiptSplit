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
}
