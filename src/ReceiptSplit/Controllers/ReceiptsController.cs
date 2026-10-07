using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using ReceiptSplit.Contracts;
using ReceiptSplit.Data;
using ReceiptSplit.Receipts;

namespace ReceiptSplit.Controllers;

[ApiController]
[Route("api/receipts")]
public class ReceiptsController(AppDbContext db, ReceiptService receipts) : ControllerBase
{
    /// <summary>Uploads a receipt photo and queues it for extraction; poll the returned location for the result.</summary>
    /// <param name="taxRatePercent">Sales tax rate the receipt was charged at; defaults to Ontario's 13%.</param>
    /// <param name="taxIncluded">Whether the printed prices already include the tax, which is then ignored.</param>
    [HttpPost]
    [RequestSizeLimit(ReceiptService.MaxUploadBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = ReceiptService.MaxUploadBytes + 1024 * 1024)]
    [ProducesResponseType<ReceiptQueuedDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Upload(
        IFormFile file,
        [FromForm] decimal? taxRatePercent,
        [FromForm] bool taxIncluded,
        CancellationToken cancellationToken)
    {
        if (taxRatePercent is { } rate && !ReceiptService.IsValidTaxRate(rate))
        {
            return InvalidTaxRate();
        }

        await using var stream = file.OpenReadStream();
        try
        {
            var receipt = await receipts.CreateAsync(stream, file.FileName, taxRatePercent, taxIncluded, cancellationToken);
            return AcceptedAtAction(nameof(Get), new { id = receipt.Id }, new ReceiptQueuedDto(receipt.Id, receipt.Status));
        }
        catch (InvalidImageException ex)
        {
            return Problem(title: "Unsupported file", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    /// <summary>Lists receipts, newest first.</summary>
    [HttpGet]
    public async Task<IReadOnlyList<ReceiptSummaryDto>> List(CancellationToken cancellationToken) =>
        await db.Receipts
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new ReceiptSummaryDto(r.Id, r.CreatedAt, r.Status, r.StoreName, r.PurchaseDate, r.Total))
            .ToListAsync(cancellationToken);

    /// <summary>Gets a receipt's status, extracted lines, totals and totals checks.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<ReceiptDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReceiptDetailDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var receipt = await db.Receipts
            .AsNoTracking()
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        return receipt is null ? NotFound() : receipt.ToDetailDto();
    }

    /// <summary>Returns the uploaded photo, as a JPEG copy when browsers can't display the original format.</summary>
    [HttpGet("{id:guid}/image")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Image(Guid id, CancellationToken cancellationToken)
    {
        if (await receipts.GetImageAsync(id, cancellationToken) is not { } image)
        {
            return NotFound();
        }

        // Inline, or the page would download the photo instead of showing it; the name is what saving it offers,
        // in place of the "image" every browser falls back to.
        var disposition = new ContentDispositionHeaderValue("inline");
        disposition.SetHttpFileName(image.FileName);
        Response.Headers.ContentDisposition = disposition.ToString();
        return PhysicalFile(image.Path, image.ContentType);
    }

    /// <summary>Replaces the extracted lines and totals with hand corrected ones and re-checks the receipt.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<ReceiptDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReceiptDetailDto>> Update(Guid id, ReceiptEditDto request, CancellationToken cancellationToken) =>
        await receipts.UpdateAsync(id, request.ToEdit(), cancellationToken) switch
        {
            ReceiptActionResult.Done => await Get(id, cancellationToken),
            ReceiptActionResult.Busy => Problem(
                title: "Receipt is busy",
                detail: "The receipt is queued or being extracted; edit it once extraction finishes.",
                statusCode: StatusCodes.Status409Conflict),
            _ => NotFound(),
        };

    /// <summary>
    /// Changes the tax rate the receipt is checked against, or whether its prices include the tax, and re-checks it
    /// without calling the model.
    /// </summary>
    [HttpPatch("{id:guid}")]
    [ProducesResponseType<ReceiptDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReceiptDetailDto>> UpdateTaxRate(
        Guid id,
        ReceiptTaxRateDto request,
        CancellationToken cancellationToken)
    {
        if (!ReceiptService.IsValidTaxRate(request.TaxRatePercent))
        {
            return InvalidTaxRate();
        }

        return await receipts.UpdateTaxRateAsync(id, request.TaxRatePercent, request.TaxIncluded, cancellationToken) switch
        {
            ReceiptActionResult.Done => await Get(id, cancellationToken),
            ReceiptActionResult.Busy => Problem(
                title: "Receipt is busy",
                detail: "The receipt is being extracted; change the tax rate once extraction finishes.",
                statusCode: StatusCodes.Status409Conflict),
            _ => NotFound(),
        };
    }

    /// <summary>Discards the previous result and extracts the stored photo again.</summary>
    [HttpPost("{id:guid}/extract")]
    [ProducesResponseType<ReceiptQueuedDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Extract(Guid id, CancellationToken cancellationToken) =>
        await receipts.RequeueAsync(id, cancellationToken) switch
        {
            ReceiptActionResult.Done => AcceptedAtAction(nameof(Get), new { id }, new ReceiptQueuedDto(id, ReceiptStatus.Queued)),
            ReceiptActionResult.Busy => Problem(
                title: "Receipt is busy",
                detail: "The receipt is already queued or being extracted.",
                statusCode: StatusCodes.Status409Conflict),
            _ => NotFound(),
        };

    /// <summary>Deletes the receipt and its stored photo.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        await receipts.DeleteAsync(id, cancellationToken) switch
        {
            ReceiptActionResult.Done => NoContent(),
            ReceiptActionResult.Busy => Problem(
                title: "Receipt is busy",
                detail: "The receipt is being extracted; delete it once extraction finishes.",
                statusCode: StatusCodes.Status409Conflict),
            _ => NotFound(),
        };

    /// <summary>
    /// Deletes several receipts and their photos. Receipts being extracted are left and listed as busy; unknown ids
    /// are ignored.
    /// </summary>
    [HttpPost("delete")]
    [ProducesResponseType<ReceiptsDeletedDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ReceiptsDeletedDto> DeleteMany(ReceiptDeleteDto request, CancellationToken cancellationToken)
    {
        var result = await receipts.DeleteManyAsync([.. request.Ids.Distinct()], cancellationToken);
        return new ReceiptsDeletedDto(result.Deleted, result.Busy);
    }

    private ObjectResult InvalidTaxRate() => Problem(
        title: "Invalid tax rate",
        detail: $"The tax rate must be between {ReceiptService.MinTaxRatePercent} and {ReceiptService.MaxTaxRatePercent} percent.",
        statusCode: StatusCodes.Status400BadRequest);
}
