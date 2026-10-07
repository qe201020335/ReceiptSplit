namespace ReceiptSplit.Receipts;

/// <summary>A stored photo ready to be served, with the name a browser should save it under.</summary>
public sealed record ReceiptImage(string Path, string ContentType, string FileName);
