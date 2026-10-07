using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReceiptSplit.Data;
using ReceiptSplit.Extraction;

namespace ReceiptSplit.Receipts;

/// <summary>Runs one extraction: prepare the stored photo, ask the model, parse, check totals, save.</summary>
internal sealed class ReceiptExtractor(
    AppDbContext db,
    ImagePreparer imagePreparer,
    ILlamaClient llm,
    IOptions<StorageOptions> storage,
    IOptions<LlmOptions> llmOptions,
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
            // Rounded to what is stored, so the status below agrees with the totals read back later.
            receipt.Subtotal = Precision.Cents(parsed.Subtotal);
            receipt.DiscountPercent = Precision.Rate(parsed.DiscountPercent);
            receipt.Tax = Precision.Cents(parsed.Tax);
            receipt.Total = Precision.Cents(parsed.Total);
            // Costco's promotion lines belong to the item above them, not on a line of their own.
            var baked = ReceiptPromotions.Bake(parsed.Lines);
            receipt.Lines.Clear();
            receipt.Lines.AddRange(baked.Select((baked, position) => new ReceiptLine
            {
                Position = position,
                Name = baked.Line.Name,
                Code = baked.Line.Code,
                Quantity = baked.Line.Quantity,
                Amount = Precision.Cents(baked.Line.Amount),
                Discount = Precision.Cents(baked.Discount),
                TaxCode = baked.Line.TaxCode,
                IsTaxed = ReceiptTaxCodes.IsTaxed(baked.Line.TaxCode),
            }));
            ReceiptTaxCodes.AssumeAllTaxedWhenNoneAre(receipt.Lines, receipt.Tax);

            var checks = ReceiptChecks.Evaluate(
                receipt.Lines.Select(l => (l.Amount, l.IsTaxed)),
                receipt.Subtotal,
                receipt.Tax,
                receipt.Total,
                receipt.TaxRatePercent,
                receipt.DiscountPercent,
                receipt.TaxIncluded);
            if (receipt.Lines.Count == 0)
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
            receipt.Error = ExtractionErrors.Describe(ex, llmOptions.Value.Timeout);
        }

        receipt.ExtractedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);
        logger.LogInformation(
            "Receipt {ReceiptId} extracted as {Status}: {LineCount} lines in {DurationMs} ms",
            receipt.Id, receipt.Status, receipt.Lines.Count, receipt.DurationMs);
    }
}
