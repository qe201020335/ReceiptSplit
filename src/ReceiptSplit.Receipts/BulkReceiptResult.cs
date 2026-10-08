namespace ReceiptSplit.Receipts;

/// <summary>
/// How an operation on several receipts ended. Bulk operations are all or nothing, so anything but
/// <see cref="ReceiptActionResult.Done"/> changed nothing, and <see cref="Blocking"/> lists the receipts that
/// stopped it.
/// </summary>
public sealed record BulkReceiptResult(ReceiptActionResult Result, IReadOnlyList<Guid> Blocking)
{
    public static BulkReceiptResult Done { get; } = new(ReceiptActionResult.Done, []);
}
