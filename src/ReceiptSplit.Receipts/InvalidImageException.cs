namespace ReceiptSplit.Receipts;

/// <summary>The uploaded file is not an image in a supported format.</summary>
public sealed class InvalidImageException(string message) : Exception(message);
