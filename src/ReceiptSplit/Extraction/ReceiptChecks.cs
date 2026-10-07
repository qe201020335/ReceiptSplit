using ReceiptSplit.Data;

namespace ReceiptSplit.Extraction;

/// <param name="Discount">The storewide discount taken off after the subtotal, negative or zero.</param>
/// <param name="TaxedSum">The taxed lines less their part of the discount, which is what tax is charged on.</param>
public sealed record ReceiptCheckResult(
    decimal LinesSum,
    bool LinesMatchSubtotal,
    decimal Discount,
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
        decimal taxRatePercent,
        decimal discountPercent = 0m,
        bool taxIncluded = false)
    {
        var all = lines.ToList();
        var sum = all.Sum(line => line.Amount);
        var taxed = all.Where(line => line.IsTaxed).Select(line => line.Amount).ToList();
        var discount = ReceiptDiscount.Amount(all.Select(line => line.Amount), discountPercent);
        var taxedSum = taxed.Sum() + ReceiptDiscount.Amount(taxed, discountPercent);

        var expectedTax = Math.Round(taxedSum * taxRatePercent / 100m, 2, MidpointRounding.AwayFromZero);
        return new ReceiptCheckResult(
            sum,
            LinesMatchSubtotal: subtotal == sum,
            discount,
            // Receipts without any tax may come back with a null tax. Tax already inside the prices adds nothing.
            TotalMatches: subtotal is not null && total is not null &&
                subtotal + discount + (taxIncluded ? 0m : tax ?? 0m) == total,
            taxedSum,
            expectedTax,
            // How much of an included tax is tax doesn't change what anyone pays, so it isn't checked.
            TaxMatches: taxIncluded || Math.Abs(expectedTax - (tax ?? 0m)) <= TaxTolerance);
    }

    /// <summary>The status an extracted receipt gets from its checks; shared with the tax rate editor.</summary>
    public static ReceiptStatus StatusFor(ReceiptCheckResult checks) =>
        checks.Passed ? ReceiptStatus.Completed : ReceiptStatus.NeedsReview;
}
