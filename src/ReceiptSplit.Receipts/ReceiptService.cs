using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ReceiptSplit.Accounts;
using ReceiptSplit.Data;

namespace ReceiptSplit.Receipts;

/// <summary>
/// Receipt operations shared by the HTTP API and future entry points such as a Discord bot. Each one is done for an
/// <see cref="Actor"/>: members only reach receipts they own and admins reach all of them, and a receipt the actor
/// can't reach is treated as missing.
/// </summary>
public sealed class ReceiptService(AppDbContext db, ExtractionQueue queue, IOptions<StorageOptions> storage)
{
    public const long MaxUploadBytes = 30 * 1024 * 1024;

    /// <summary>Ontario's HST, the rate most of these receipts are printed at.</summary>
    public const decimal DefaultTaxRatePercent = 13m;

    public const decimal MinTaxRatePercent = 0m;

    public const decimal MaxTaxRatePercent = 30m;

    public static bool IsValidTaxRate(decimal percent) => percent is >= MinTaxRatePercent and <= MaxTaxRatePercent;

    /// <summary>The receipts the actor can see, newest first.</summary>
    public async Task<IReadOnlyList<ReceiptSummary>> ListAsync(Actor actor, CancellationToken cancellationToken) =>
        await Visible(actor)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new ReceiptSummary(r.Id, r.CreatedAt, r.Status, r.StoreName, r.PurchaseDate, r.Total))
            .ToListAsync(cancellationToken);

    /// <summary>The receipt with its lines, or null when it doesn't exist or the actor can't see it.</summary>
    public async Task<Receipt?> GetAsync(Actor actor, Guid id, CancellationToken cancellationToken) =>
        await Visible(actor)
            .AsNoTracking()
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    /// <summary>Stores the upload, creates a queued receipt owned by the actor, and queues it for extraction.</summary>
    /// <exception cref="InvalidImageException">The content is not a supported image.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The tax rate is outside the accepted range.</exception>
    public async Task<Receipt> CreateAsync(
        Actor actor,
        Stream content,
        string fileName,
        decimal? taxRatePercent,
        bool taxIncluded,
        CancellationToken cancellationToken)
    {
        var taxRate = Precision.Rate(taxRatePercent ?? DefaultTaxRatePercent);
        ValidateTaxRate(taxRate);

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
            OwnerId = actor.UserId,
            OriginalFileName = Path.GetFileName(fileName) is { Length: > 0 } name ? name : $"receipt{image.Extension}",
            StoredFileName = $"{id}{image.Extension}",
            ContentType = image.MimeType,
            TaxRatePercent = taxRate,
            TaxIncluded = taxIncluded,
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
    public async Task<ReceiptActionResult> RequeueAsync(Actor actor, Guid id, CancellationToken cancellationToken)
    {
        var receipt = await Visible(actor)
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
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

    /// <summary>
    /// Replaces the extracted store, date, totals and lines with hand corrected ones, and re-checks the receipt.
    /// Refused while the model is reading the photo, because that would overwrite the correction moments later.
    /// </summary>
    public async Task<ReceiptActionResult> UpdateAsync(
        Actor actor, Guid id, ReceiptEdit edit, CancellationToken cancellationToken)
    {
        var receipt = await Visible(actor)
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (receipt is null)
        {
            return ReceiptActionResult.NotFound;
        }

        if (receipt.Status is ReceiptStatus.Queued or ReceiptStatus.Processing)
        {
            return ReceiptActionResult.Busy;
        }

        receipt.StoreName = Trimmed(edit.StoreName);
        receipt.PurchaseDate = edit.PurchaseDate;
        // Rounded to what is stored before checking, or a stray third decimal would decide the status.
        receipt.Subtotal = Precision.Cents(edit.Subtotal);
        receipt.DiscountPercent = Precision.Rate(edit.DiscountPercent);
        receipt.Tax = Precision.Cents(edit.Tax);
        receipt.Total = Precision.Cents(edit.Total);

        receipt.Lines.Clear();
        receipt.Lines.AddRange(edit.Lines.Select((line, position) => new ReceiptLine
        {
            Position = position,
            Name = line.Name.Trim(),
            Code = Trimmed(line.Code),
            Quantity = line.Quantity,
            Amount = Precision.Cents(line.Amount),
            Discount = Precision.Cents(line.Discount),
            TaxCode = Trimmed(line.TaxCode),
            IsTaxed = line.IsTaxed,
        }));

        // Whatever went wrong during extraction has just been corrected by hand.
        receipt.Error = null;
        receipt.EditedAt = DateTime.UtcNow;
        receipt.Status = ReceiptChecks.StatusFor(ReceiptChecks.Evaluate(
            receipt.Lines.Select(l => (l.Amount, l.IsTaxed)),
            receipt.Subtotal,
            receipt.Tax,
            receipt.Total,
            receipt.TaxRatePercent,
            receipt.DiscountPercent,
            receipt.TaxIncluded));

        await db.SaveChangesAsync(cancellationToken);
        return ReceiptActionResult.Done;
    }

    /// <summary>
    /// Changes the tax rate the receipt is checked against, and whether its prices include the tax when given.
    /// The checks are computed from the stored lines,
    /// so an already extracted receipt is re-checked here instead of being sent to the model again.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The tax rate is outside the accepted range.</exception>
    public async Task<ReceiptActionResult> UpdateTaxRateAsync(
        Actor actor,
        Guid id,
        decimal taxRatePercent,
        bool? taxIncluded,
        CancellationToken cancellationToken)
    {
        taxRatePercent = Precision.Rate(taxRatePercent);
        ValidateTaxRate(taxRatePercent);

        var receipt = await Visible(actor)
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (receipt is null)
        {
            return ReceiptActionResult.NotFound;
        }

        if (receipt.Status == ReceiptStatus.Processing)
        {
            return ReceiptActionResult.Busy;
        }

        receipt.TaxRatePercent = taxRatePercent;
        receipt.TaxIncluded = taxIncluded ?? receipt.TaxIncluded;

        // A queued receipt is checked when it is extracted, and a failed one has no lines worth checking.
        if (receipt.Error is null && receipt.Status is ReceiptStatus.Completed or ReceiptStatus.NeedsReview)
        {
            var checks = ReceiptChecks.Evaluate(
                receipt.Lines.Select(l => (l.Amount, l.IsTaxed)),
                receipt.Subtotal,
                receipt.Tax,
                receipt.Total,
                taxRatePercent,
                receipt.DiscountPercent,
                receipt.TaxIncluded);
            receipt.Status = ReceiptChecks.StatusFor(checks);
        }

        await db.SaveChangesAsync(cancellationToken);
        return ReceiptActionResult.Done;
    }

    /// <summary>
    /// The stored photo as a file a browser can display: the original, or for HEIC and TIFF uploads a JPEG copy,
    /// converted on first request and kept next to the original. Null when the receipt or its photo is missing.
    /// <see cref="ReceiptImage.FileName"/> is what saving the photo should name it.
    /// </summary>
    public async Task<ReceiptImage?> GetImageAsync(Actor actor, Guid id, CancellationToken cancellationToken)
    {
        var receipt = await Visible(actor).AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        var original = receipt is null ? null : storage.Value.GetUploadPath(receipt.StoredFileName);
        if (receipt is null || !File.Exists(original))
        {
            return null;
        }

        if (ImagePreparer.BrowsersCanShow(receipt.ContentType))
        {
            return new ReceiptImage(original, receipt.ContentType, receipt.OriginalFileName);
        }

        var copy = storage.Value.GetDisplayCopyPath(receipt.StoredFileName);
        if (!File.Exists(copy))
        {
            var jpeg = ImagePreparer.ToDisplayJpeg(await File.ReadAllBytesAsync(original, cancellationToken));
            // Written under a temporary name and moved into place, so a concurrent request never serves half a file.
            var partial = $"{copy}.{Guid.NewGuid():N}.tmp";
            await File.WriteAllBytesAsync(partial, jpeg, cancellationToken);
            File.Move(partial, copy, overwrite: true);
        }

        // The copy is a JPEG whatever the upload was, so the saved file gets the extension of what is served.
        return new ReceiptImage(copy, "image/jpeg", Path.ChangeExtension(receipt.OriginalFileName, ".jpg"));
    }

    /// <summary>Deletes the receipt, its lines, and the stored photo. Refused while the model is reading it.</summary>
    public async Task<ReceiptActionResult> DeleteAsync(Actor actor, Guid id, CancellationToken cancellationToken)
    {
        var receipt = await Visible(actor).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
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
        DeletePhotos(receipt);
        return ReceiptActionResult.Done;
    }

    /// <summary>
    /// Deletes several receipts and their photos, or none of them: ids that don't exist or the actor can't see make
    /// it not found, and receipts the model is reading make it busy, as they would for <see cref="DeleteAsync"/>.
    /// </summary>
    public async Task<BulkReceiptResult> DeleteManyAsync(
        Actor actor, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        // SQLite transactions from Microsoft.Data.Sqlite start IMMEDIATE, taking the write lock now, so the worker
        // can't mark one of these receipts Processing between the check and the delete. It may already have read one
        // that is still Queued; its own save then finds the receipt gone, and it skips it.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var receipts = await Visible(actor).Where(r => ids.Contains(r.Id)).ToListAsync(cancellationToken);
        var missing = ids.Except(receipts.Select(r => r.Id)).ToList();
        if (missing.Count > 0)
        {
            return new BulkReceiptResult(ReceiptActionResult.NotFound, missing);
        }

        var busy = receipts.Where(r => r.Status == ReceiptStatus.Processing).Select(r => r.Id).ToList();
        if (busy.Count > 0)
        {
            return new BulkReceiptResult(ReceiptActionResult.Busy, busy);
        }

        db.Receipts.RemoveRange(receipts);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        foreach (var receipt in receipts)
        {
            DeletePhotos(receipt);
        }

        return BulkReceiptResult.Done;
    }

    /// <summary>
    /// Every lookup goes through here, so a receipt someone else owns, or one without an owner, looks the same to a
    /// member as one that doesn't exist.
    /// </summary>
    private IQueryable<Receipt> Visible(Actor actor) =>
        actor.IsAdmin ? db.Receipts : db.Receipts.Where(r => r.OwnerId == actor.UserId);

    private void DeletePhotos(Receipt receipt)
    {
        File.Delete(storage.Value.GetUploadPath(receipt.StoredFileName));
        File.Delete(storage.Value.GetDisplayCopyPath(receipt.StoredFileName));
    }

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void ValidateTaxRate(decimal percent)
    {
        if (!IsValidTaxRate(percent))
        {
            throw new ArgumentOutOfRangeException(
                nameof(percent), percent, $"The tax rate must be between {MinTaxRatePercent} and {MaxTaxRatePercent} percent.");
        }
    }
}
