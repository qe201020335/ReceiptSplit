using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ReceiptSplit.Contracts;
using ReceiptSplit.Data;
using ReceiptSplit.Extraction;
using ReceiptSplit.Options;

namespace ReceiptSplit.Controllers;

[ApiController]
[Route("api/receipts")]
public class ReceiptsController(AppDbContext db, ReceiptService receipts, IOptions<StorageOptions> storage) : ControllerBase
{
    /// <summary>Uploads a receipt photo and queues it for extraction; poll the returned location for the result.</summary>
    [HttpPost]
    [RequestSizeLimit(ReceiptService.MaxUploadBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = ReceiptService.MaxUploadBytes + 1024 * 1024)]
    [ProducesResponseType<ReceiptQueuedDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        try
        {
            var receipt = await receipts.CreateAsync(stream, file.FileName, cancellationToken);
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

    /// <summary>Returns the original uploaded photo.</summary>
    [HttpGet("{id:guid}/image")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Image(Guid id, CancellationToken cancellationToken)
    {
        var receipt = await db.Receipts.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (receipt is null)
        {
            return NotFound();
        }

        var path = storage.Value.GetUploadPath(receipt.StoredFileName);
        return System.IO.File.Exists(path) ? PhysicalFile(path, receipt.ContentType) : NotFound();
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
}
