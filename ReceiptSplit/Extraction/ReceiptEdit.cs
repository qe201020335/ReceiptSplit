namespace ReceiptSplit.Extraction;

/// <summary>A hand correction of one receipt, replacing what the model read.</summary>
public sealed record ReceiptEdit(
    string? StoreName,
    DateOnly? PurchaseDate,
    decimal? Subtotal,
    decimal DiscountPercent,
    decimal? Tax,
    bool TaxIncluded,
    decimal? Total,
    IReadOnlyList<ReceiptLineEdit> Lines);

/// <summary>One corrected line, in printed order; <see cref="IsTaxed"/> is set by hand rather than read from the code.</summary>
/// <param name="Amount">The price paid, promotion already deducted.</param>
/// <param name="Discount">The promotion deducted from <paramref name="Amount"/>, negative, or zero.</param>
public sealed record ReceiptLineEdit(
    string Name,
    string? Code,
    decimal Quantity,
    decimal Amount,
    decimal Discount,
    string? TaxCode,
    bool IsTaxed);
