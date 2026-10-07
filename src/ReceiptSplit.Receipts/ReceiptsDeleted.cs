namespace ReceiptSplit.Receipts;

/// <summary>What a bulk delete removed, and the receipts it left because the model was reading them.</summary>
public sealed record ReceiptsDeleted(IReadOnlyList<Guid> Deleted, IReadOnlyList<Guid> Busy);
