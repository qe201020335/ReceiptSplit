using ReceiptSplit.Data;

namespace ReceiptSplit.Extraction;

/// <summary>
/// A percentage taken off the whole purchase after the subtotal, such as Target's "10%off Storewide". It is kept
/// on the receipt as a percentage rather than on the lines, and only turned into an amount to check the totals.
/// </summary>
public static class ReceiptDiscount
{
    /// <summary>
    /// The discount on these lines, negative or zero. Each line's share is rounded to the cent before summing,
    /// which is how Target arrives at its printed amount: 10% of 415.34 is printed as 41.54, not 41.53.
    /// </summary>
    public static decimal Amount(IEnumerable<decimal> lineAmounts, decimal percent) =>
        percent == 0m ? 0m : -lineAmounts.Sum(amount => Precision.Cents(amount * percent / 100m));
}
