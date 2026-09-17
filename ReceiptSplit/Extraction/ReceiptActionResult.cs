namespace ReceiptSplit.Extraction;

/// <summary>How a receipt operation ended, mapped to a status code by the controller.</summary>
public enum ReceiptActionResult
{
    Done,
    NotFound,
    Busy,
}
