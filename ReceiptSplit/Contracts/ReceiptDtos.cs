using ReceiptSplit.Data;
using ReceiptSplit.Extraction;

namespace ReceiptSplit.Contracts;

public sealed record ReceiptQueuedDto(Guid Id, ReceiptStatus Status);

public sealed record ReceiptSummaryDto(
    Guid Id,
    DateTime CreatedAt,
    ReceiptStatus Status,
    string? StoreName,
    DateOnly? PurchaseDate,
    decimal? Total);

public sealed record ReceiptDetailDto(
    Guid Id,
    DateTime CreatedAt,
    string OriginalFileName,
    ReceiptStatus Status,
    string? Error,
    string? StoreName,
    DateOnly? PurchaseDate,
    decimal TaxRatePercent,
    decimal? Subtotal,
    decimal? Tax,
    decimal? Total,
    ReceiptChecksDto? Checks,
    IReadOnlyList<ReceiptLineDto> Lines,
    ExtractionDto? Extraction);

public sealed record ReceiptLineDto(
    int Position,
    string Name,
    string? Code,
    decimal Quantity,
    decimal Amount,
    string? TaxCode,
    bool IsTaxed);

public sealed record ReceiptChecksDto(
    decimal LinesSum,
    bool LinesMatchSubtotal,
    bool TotalMatches,
    decimal TaxedSum,
    decimal ExpectedTax,
    bool TaxMatches);

public sealed record ExtractionDto(
    DateTime ExtractedAt,
    string? Model,
    int? PromptTokens,
    int? CompletionTokens,
    long? DurationMs,
    int? SentImageWidth,
    int? SentImageHeight,
    string? ModelOutput);

public static class ReceiptMappings
{
    public static ReceiptDetailDto ToDetailDto(this Receipt receipt)
    {
        var lines = receipt.Lines
            .OrderBy(l => l.Position)
            .Select(l => new ReceiptLineDto(l.Position, l.Name, l.Code, l.Quantity, l.Amount, l.TaxCode, l.IsTaxed))
            .ToList();

        ReceiptChecksDto? checks = null;
        if (lines.Count > 0)
        {
            var result = ReceiptChecks.Evaluate(
                lines.Select(l => (l.Amount, l.IsTaxed)),
                receipt.Subtotal,
                receipt.Tax,
                receipt.Total,
                receipt.TaxRatePercent);
            checks = new ReceiptChecksDto(
                result.LinesSum,
                result.LinesMatchSubtotal,
                result.TotalMatches,
                result.TaxedSum,
                result.ExpectedTax,
                result.TaxMatches);
        }

        var extraction = receipt.ExtractedAt is { } extractedAt
            ? new ExtractionDto(
                extractedAt,
                receipt.Model,
                receipt.PromptTokens,
                receipt.CompletionTokens,
                receipt.DurationMs,
                receipt.SentImageWidth,
                receipt.SentImageHeight,
                receipt.ModelOutput)
            : null;

        return new ReceiptDetailDto(
            receipt.Id,
            receipt.CreatedAt,
            receipt.OriginalFileName,
            receipt.Status,
            receipt.Error,
            receipt.StoreName,
            receipt.PurchaseDate,
            receipt.TaxRatePercent,
            receipt.Subtotal,
            receipt.Tax,
            receipt.Total,
            checks,
            lines,
            extraction);
    }
}
