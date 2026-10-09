namespace ReceiptSplit.Receipts;

/// <summary>How a receipt operation ended, mapped to a status code by the controller.</summary>
public enum ReceiptActionResult
{
    Done,
    NotFound,
    Busy,

    /// <summary>The user the receipts would be given to doesn't exist.</summary>
    UnknownUser,
}
