namespace ReceiptSplit.Data;

public class ReceiptLine
{
    public int Id { get; set; }

    public Guid ReceiptId { get; set; }

    /// <summary>Zero-based position in printed order.</summary>
    public int Position { get; set; }

    public required string Name { get; set; }

    /// <summary>Item number as printed, if any.</summary>
    public string? Code { get; set; }

    /// <summary>Count or weight; the least reliable field for weighed items, so splits should use <see cref="Amount"/>.</summary>
    public decimal Quantity { get; set; } = 1;

    /// <summary>Line total actually paid, with any promotion already deducted; negative for a discount line.</summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// Promotion deducted from this line, negative, or zero. Costco prints these as their own lines
    /// ("TPD/339054"), which are folded into the item they name, so the printed price is Amount - Discount.
    /// </summary>
    public decimal Discount { get; set; }

    /// <summary>Tax code letters as printed (e.g. "H", "G P"); null means nothing was printed.</summary>
    public string? TaxCode { get; set; }

    /// <summary>Whether sales tax was charged on this line, read from <see cref="TaxCode"/> when the receipt is extracted.</summary>
    public bool IsTaxed { get; set; }
}
