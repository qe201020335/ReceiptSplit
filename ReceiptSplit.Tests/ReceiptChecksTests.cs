using ReceiptSplit.Extraction;

namespace ReceiptSplit.Tests;

public class ReceiptChecksTests
{
    private static readonly decimal[] Lines = [4.99m, -1.00m, 2.00m];

    [Fact]
    public void Passes_when_lines_and_totals_agree()
    {
        var result = ReceiptChecks.Evaluate(Lines, subtotal: 5.99m, tax: 0.78m, total: 6.77m);

        Assert.Equal(new ReceiptCheckResult(5.99m, LinesMatchSubtotal: true, TotalMatches: true), result);
        Assert.True(result.Passed);
    }

    [Fact]
    public void Flags_lines_that_do_not_add_up_to_the_subtotal()
    {
        var result = ReceiptChecks.Evaluate(Lines, subtotal: 6.99m, tax: 0.78m, total: 7.77m);

        Assert.False(result.LinesMatchSubtotal);
        Assert.True(result.TotalMatches);
        Assert.False(result.Passed);
    }

    [Fact]
    public void Flags_a_total_that_is_not_subtotal_plus_tax()
    {
        var result = ReceiptChecks.Evaluate(Lines, subtotal: 5.99m, tax: 0.78m, total: 6.87m);

        Assert.True(result.LinesMatchSubtotal);
        Assert.False(result.TotalMatches);
    }

    [Fact]
    public void Treats_missing_tax_as_zero()
    {
        Assert.True(ReceiptChecks.Evaluate(Lines, subtotal: 5.99m, tax: null, total: 5.99m).Passed);
    }

    [Fact]
    public void Fails_without_printed_totals()
    {
        var result = ReceiptChecks.Evaluate(Lines, subtotal: null, tax: null, total: null);

        Assert.False(result.LinesMatchSubtotal);
        Assert.False(result.TotalMatches);
    }
}
