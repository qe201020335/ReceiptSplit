using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ReceiptSplit.Data;
using ReceiptSplit.Options;

namespace ReceiptSplit.Extraction;

public enum ReceiptActionResult
{
    Done,
    NotFound,
    Busy,
}

/// <summary>
/// Receipt operations shared by the HTTP API and future entry points such as a Discord bot.
/// </summary>
public sealed class ReceiptService(AppDbContext db, ExtractionQueue queue, IOptions<StorageOptions> storage)
{
    public const long MaxUploadBytes = 30 * 1024 * 1024;

    /// <summary>Stores the original upload, creates a queued receipt, and queues it for extraction.</summary>
    /// <exception cref="InvalidImageException">The content is not a supported image.</exception>
    public async Task<Receipt> CreateAsync(Stream content, string fileName, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length > MaxUploadBytes)
        {
            throw new InvalidImageException("The file is larger than 30 MB.");
        }

        var data = buffer.ToArray();
        var image = ImagePreparer.Identify(data);

        var id = Guid.CreateVersion7();
        var receipt = new Receipt
        {
            Id = id,
            OriginalFileName = Path.GetFileName(fileName) is { Length: > 0 } name ? name : $"receipt{image.Extension}",
            StoredFileName = $"{id}{image.Extension}",
            ContentType = image.MimeType,
        };

        var path = storage.Value.GetUploadPath(receipt.StoredFileName);
        await File.WriteAllBytesAsync(path, data, cancellationToken);
        try
        {
            db.Receipts.Add(receipt);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            File.Delete(path);
            throw;
        }

        queue.Enqueue(receipt.Id);
        return receipt;
    }

    /// <summary>Clears previous results and queues the stored photo for another extraction.</summary>
    public async Task<ReceiptActionResult> RequeueAsync(Guid id, CancellationToken cancellationToken)
    {
        var receipt = await db.Receipts.Include(r => r.Lines).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (receipt is null)
        {
            return ReceiptActionResult.NotFound;
        }

        if (receipt.Status is ReceiptStatus.Queued or ReceiptStatus.Processing)
        {
            return ReceiptActionResult.Busy;
        }

        receipt.ResetExtraction();
        await db.SaveChangesAsync(cancellationToken);
        queue.Enqueue(receipt.Id);
        return ReceiptActionResult.Done;
    }

    /// <summary>Deletes the receipt, its lines, and the stored photo. Refused while the model is reading it.</summary>
    public async Task<ReceiptActionResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var receipt = await db.Receipts.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (receipt is null)
        {
            return ReceiptActionResult.NotFound;
        }

        if (receipt.Status == ReceiptStatus.Processing)
        {
            return ReceiptActionResult.Busy;
        }

        db.Receipts.Remove(receipt);
        await db.SaveChangesAsync(cancellationToken);
        File.Delete(storage.Value.GetUploadPath(receipt.StoredFileName));
        return ReceiptActionResult.Done;
    }
}
