namespace ReceiptSplit.Extraction;

public sealed record ReceiptCheckResult(decimal LinesSum, bool LinesMatchSubtotal, bool TotalMatches)
{
    public bool Passed => LinesMatchSubtotal && TotalMatches;
}

/// <summary>
/// Uses the receipt's own printed totals to catch misread digits or skipped lines.
/// </summary>
public static class ReceiptChecks
{
    public static ReceiptCheckResult Evaluate(IEnumerable<decimal> lineAmounts, decimal? subtotal, decimal? tax, decimal? total)
    {
        var sum = lineAmounts.Sum();
        return new ReceiptCheckResult(
            sum,
            LinesMatchSubtotal: subtotal == sum,
            // Receipts without any tax may come back with a null tax.
            TotalMatches: subtotal is not null && total is not null && subtotal + (tax ?? 0m) == total);
    }
}
