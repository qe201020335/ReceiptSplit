using ReceiptSplit.Data;

namespace ReceiptSplit.Receipts;

/// <summary>A receipt as lists show it, without its lines or extraction details.</summary>
/// <param name="OwnerId">Null for a receipt without an owner; clients name owners from the user list.</param>
public sealed record ReceiptSummary(
    Guid Id,
    DateTime CreatedAt,
    ReceiptStatus Status,
    string? StoreName,
    DateOnly? PurchaseDate,
    decimal? Total,
    Guid? OwnerId);
