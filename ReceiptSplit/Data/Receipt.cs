namespace ReceiptSplit.Data;

public class Receipt
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public required string OriginalFileName { get; set; }

    /// <summary>File name of the original upload inside the uploads directory.</summary>
    public required string StoredFileName { get; set; }

    /// <summary>MIME type detected from the file contents, not the one the client sent.</summary>
    public required string ContentType { get; set; }

    public ReceiptStatus Status { get; set; } = ReceiptStatus.Queued;

    public string? Error { get; set; }

    public string? StoreName { get; set; }

    public DateOnly? PurchaseDate { get; set; }

    public decimal? Subtotal { get; set; }

    public decimal? Tax { get; set; }

    public decimal? Total { get; set; }

    public List<ReceiptLine> Lines { get; set; } = [];

    // Extraction diagnostics from the most recent model run.
    public string? ModelOutput { get; set; }

    public string? Model { get; set; }

    public int? PromptTokens { get; set; }

    public int? CompletionTokens { get; set; }

    public long? DurationMs { get; set; }

    public int? SentImageWidth { get; set; }

    public int? SentImageHeight { get; set; }

    public DateTime? ExtractedAt { get; set; }

    /// <summary>Drops all extraction results and puts the receipt back in the queue state.</summary>
    public void ResetExtraction()
    {
        Status = ReceiptStatus.Queued;
        Error = null;
        StoreName = null;
        PurchaseDate = null;
        Subtotal = null;
        Tax = null;
        Total = null;
        Lines.Clear();
        ModelOutput = null;
        Model = null;
        PromptTokens = null;
        CompletionTokens = null;
        DurationMs = null;
        SentImageWidth = null;
        SentImageHeight = null;
        ExtractedAt = null;
    }
}
