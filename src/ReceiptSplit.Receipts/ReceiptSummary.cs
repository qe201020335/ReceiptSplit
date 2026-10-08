using ReceiptSplit.Data;

namespace ReceiptSplit.Receipts;

/// <summary>A receipt as lists show it, without its lines or extraction details.</summary>
public sealed record ReceiptSummary(
    Guid Id,
    DateTime CreatedAt,
    ReceiptStatus Status,
    string? StoreName,
    DateOnly? PurchaseDate,
    decimal? Total);
