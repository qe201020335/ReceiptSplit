using System.ComponentModel.DataAnnotations;
using ReceiptSplit.Data;
using ReceiptSplit.Receipts;

namespace ReceiptSplit.Contracts;

public sealed record ReceiptQueuedDto(Guid Id, ReceiptStatus Status);

/// <summary>Body of the request that changes the rate a receipt's tax is checked against.</summary>
/// <param name="TaxIncluded">Whether the prices include the tax; left as it is when omitted.</param>
public sealed record ReceiptTaxRateDto(decimal TaxRatePercent, bool? TaxIncluded = null);

/// <summary>Body of the request that replaces a receipt's extracted result with hand corrected values.</summary>
public sealed record ReceiptEditDto(
    [StringLength(200)] string? StoreName,
    DateOnly? PurchaseDate,
    [Range(-1_000_000, 1_000_000)] decimal? Subtotal,
    [Range(0, 100)] decimal DiscountPercent,
    [Range(-1_000_000, 1_000_000)] decimal? Tax,
    [Range(-1_000_000, 1_000_000)] decimal? Total,
    [Required, MinLength(1), MaxLength(500)] IReadOnlyList<ReceiptLineEditDto> Lines);

public sealed record ReceiptLineEditDto(
    [Required, StringLength(200, MinimumLength = 1)] string Name,
    [StringLength(50)] string? Code,
    [Range(-100_000, 100_000)] decimal Quantity,
    [Range(-1_000_000, 1_000_000)] decimal Amount,
    [Range(-1_000_000, 0)] decimal Discount,
    [StringLength(16)] string? TaxCode,
    bool IsTaxed);

/// <summary>Body of the request that deletes several receipts at once.</summary>
public sealed record ReceiptDeleteDto([Required, MinLength(1), MaxLength(1000)] IReadOnlyList<Guid> Ids);

/// <summary>The receipts a bulk delete removed, and those it left because the model is reading them.</summary>
public sealed record ReceiptsDeletedDto(IReadOnlyList<Guid> Deleted, IReadOnlyList<Guid> Busy);

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
    DateTime? EditedAt,
    decimal TaxRatePercent,
    decimal? Subtotal,
    decimal DiscountPercent,
    decimal? Tax,
    bool TaxIncluded,
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
    decimal Discount,
    string? TaxCode,
    bool IsTaxed);

public sealed record ReceiptChecksDto(
    decimal LinesSum,
    bool LinesMatchSubtotal,
    decimal Discount,
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
    public static ReceiptEdit ToEdit(this ReceiptEditDto dto) => new(
        dto.StoreName,
        dto.PurchaseDate,
        dto.Subtotal,
        dto.DiscountPercent,
        dto.Tax,
        dto.Total,
        dto.Lines
            .Select(l => new ReceiptLineEdit(l.Name, l.Code, l.Quantity, l.Amount, l.Discount, l.TaxCode, l.IsTaxed))
            .ToList());

    public static ReceiptDetailDto ToDetailDto(this Receipt receipt)
    {
        var lines = receipt.Lines
            .OrderBy(l => l.Position)
            .Select(l => new ReceiptLineDto(l.Position, l.Name, l.Code, l.Quantity, l.Amount, l.Discount, l.TaxCode, l.IsTaxed))
            .ToList();

        ReceiptChecksDto? checks = null;
        if (lines.Count > 0)
        {
            var result = ReceiptChecks.Evaluate(
                lines.Select(l => (l.Amount, l.IsTaxed)),
                receipt.Subtotal,
                receipt.Tax,
                receipt.Total,
                receipt.TaxRatePercent,
                receipt.DiscountPercent,
                receipt.TaxIncluded);
            checks = new ReceiptChecksDto(
                result.LinesSum,
                result.LinesMatchSubtotal,
                result.Discount,
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
            receipt.EditedAt,
            receipt.TaxRatePercent,
            receipt.Subtotal,
            receipt.DiscountPercent,
            receipt.Tax,
            receipt.TaxIncluded,
            receipt.Total,
            checks,
            lines,
            extraction);
    }
}
