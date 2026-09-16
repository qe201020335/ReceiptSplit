using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ReceiptSplit.Data;
using ReceiptSplit.Options;

namespace ReceiptSplit.Extraction;

/// <summary>Runs one extraction: prepare the stored photo, ask the model, parse, check totals, save.</summary>
public sealed class ReceiptExtractor(
    AppDbContext db,
    ImagePreparer imagePreparer,
    ILlamaClient llm,
    IOptions<StorageOptions> storage,
    ILogger<ReceiptExtractor> logger)
{
    public async Task RunAsync(Guid receiptId, CancellationToken cancellationToken)
    {
        var receipt = await db.Receipts
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == receiptId, cancellationToken);

        // Deleted since it was queued, or a duplicate queue entry for a receipt that was already handled.
        if (receipt is not { Status: ReceiptStatus.Queued })
        {
            return;
        }

        receipt.Status = ReceiptStatus.Processing;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var original = await File.ReadAllBytesAsync(storage.Value.GetUploadPath(receipt.StoredFileName), cancellationToken);
            var image = imagePreparer.Prepare(original);
            receipt.SentImageWidth = image.Width;
            receipt.SentImageHeight = image.Height;

            var completion = await llm.ExtractReceiptAsync(image, cancellationToken);
            receipt.ModelOutput = completion.Content;
            receipt.Model = completion.Model;
            receipt.PromptTokens = completion.PromptTokens;
            receipt.CompletionTokens = completion.CompletionTokens;
            receipt.DurationMs = (long)completion.Elapsed.TotalMilliseconds;

            var parsed = ReceiptOutputParser.Parse(completion.Content);
            receipt.StoreName = parsed.StoreName;
            receipt.PurchaseDate = parsed.PurchaseDate;
            receipt.Subtotal = parsed.Subtotal;
            receipt.Tax = parsed.Tax;
            receipt.Total = parsed.Total;
            receipt.Lines.Clear();
            receipt.Lines.AddRange(parsed.Lines.Select((line, position) => new ReceiptLine
            {
                Position = position,
                Name = line.Name,
                Code = line.Code,
                Quantity = line.Quantity,
                Amount = line.Amount,
                TaxCode = line.TaxCode,
                IsTaxed = ReceiptTaxCodes.IsTaxed(line.TaxCode),
            }));

            var checks = ReceiptChecks.Evaluate(
                receipt.Lines.Select(l => (l.Amount, l.IsTaxed)),
                parsed.Subtotal,
                parsed.Tax,
                parsed.Total,
                receipt.TaxRatePercent);
            if (parsed.Lines.Count == 0)
            {
                receipt.Status = ReceiptStatus.Failed;
                receipt.Error = "The model output contained no receipt lines.";
            }
            else if (completion.FinishReason == "length")
            {
                receipt.Status = ReceiptStatus.NeedsReview;
                receipt.Error = "The model output was cut off at the output token limit.";
            }
            else
            {
                receipt.Status = ReceiptChecks.StatusFor(checks);
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Extraction failed for receipt {ReceiptId}", receipt.Id);
            receipt.Status = ReceiptStatus.Failed;
            receipt.Error = ex.Message;
        }

        receipt.ExtractedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);
        logger.LogInformation(
            "Receipt {ReceiptId} extracted as {Status}: {LineCount} lines in {DurationMs} ms",
            receipt.Id, receipt.Status, receipt.Lines.Count, receipt.DurationMs);
    }
}
