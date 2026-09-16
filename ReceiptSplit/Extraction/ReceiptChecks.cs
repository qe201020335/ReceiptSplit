using ReceiptSplit.Data;

namespace ReceiptSplit.Extraction;

public sealed record ReceiptCheckResult(
    decimal LinesSum,
    bool LinesMatchSubtotal,
    bool TotalMatches,
    decimal TaxedSum,
    decimal ExpectedTax,
    bool TaxMatches)
{
    public bool Passed => LinesMatchSubtotal && TotalMatches && TaxMatches;
}

/// <summary>
/// Uses the receipt's own printed totals to catch misread digits or skipped lines.
/// </summary>
public static class ReceiptChecks
{
    /// <summary>Stores round tax on the taxed subtotal, but allow a cent for those that round per line.</summary>
    private const decimal TaxTolerance = 0.01m;

    public static ReceiptCheckResult Evaluate(
        IEnumerable<(decimal Amount, bool IsTaxed)> lines,
        decimal? subtotal,
        decimal? tax,
        decimal? total,
        decimal taxRatePercent)
    {
        var sum = 0m;
        var taxedSum = 0m;
        foreach (var (amount, isTaxed) in lines)
        {
            sum += amount;
            if (isTaxed)
            {
                taxedSum += amount;
            }
        }

        var expectedTax = Math.Round(taxedSum * taxRatePercent / 100m, 2, MidpointRounding.AwayFromZero);
        return new ReceiptCheckResult(
            sum,
            LinesMatchSubtotal: subtotal == sum,
            // Receipts without any tax may come back with a null tax.
            TotalMatches: subtotal is not null && total is not null && subtotal + (tax ?? 0m) == total,
            taxedSum,
            expectedTax,
            TaxMatches: Math.Abs(expectedTax - (tax ?? 0m)) <= TaxTolerance);
    }

    /// <summary>The status an extracted receipt gets from its checks; shared with the tax rate editor.</summary>
    public static ReceiptStatus StatusFor(ReceiptCheckResult checks) =>
        checks.Passed ? ReceiptStatus.Completed : ReceiptStatus.NeedsReview;
}
