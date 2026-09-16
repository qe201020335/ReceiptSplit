namespace ReceiptSplit.Data;

public enum ReceiptStatus
{
    /// <summary>Waiting for the extraction worker.</summary>
    Queued,

    /// <summary>The model is reading the photo.</summary>
    Processing,

    /// <summary>Lines add up to the subtotal and subtotal + tax equals the total.</summary>
    Completed,

    /// <summary>Lines were extracted but the receipt's own totals don't agree with them.</summary>
    NeedsReview,

    /// <summary>The model call failed or its output contained no lines; see <see cref="Receipt.Error"/>.</summary>
    Failed,
}
