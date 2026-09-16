namespace ReceiptSplit.Extraction;

/// <summary>A hand correction of one receipt, replacing what the model read.</summary>
public sealed record ReceiptEdit(
    string? StoreName,
    DateOnly? PurchaseDate,
    decimal? Subtotal,
    decimal? Tax,
    decimal? Total,
    IReadOnlyList<ReceiptLineEdit> Lines);

/// <summary>One corrected line, in printed order; <see cref="IsTaxed"/> is set by hand rather than read from the code.</summary>
public sealed record ReceiptLineEdit(string Name, string? Code, decimal Quantity, decimal Amount, string? TaxCode, bool IsTaxed);
