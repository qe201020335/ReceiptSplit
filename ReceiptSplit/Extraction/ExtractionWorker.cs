using Microsoft.EntityFrameworkCore;
using ReceiptSplit.Data;

namespace ReceiptSplit.Extraction;

/// <summary>
/// Processes queued receipts one at a time, matching the model server's single slot.
/// </summary>
public sealed class ExtractionWorker(
    IServiceScopeFactory scopeFactory,
    ExtractionQueue queue,
    ILogger<ExtractionWorker> logger) : BackgroundService
{
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        // Runs before the server accepts requests, so recovered ids can't race with new uploads.
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Receipts
                .Where(r => r.Status == ReceiptStatus.Processing)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, ReceiptStatus.Queued), cancellationToken);

            var unfinished = await db.Receipts
                .Where(r => r.Status == ReceiptStatus.Queued)
                .OrderBy(r => r.CreatedAt)
                .Select(r => r.Id)
                .ToListAsync(cancellationToken);
            foreach (var id in unfinished)
            {
                queue.Enqueue(id);
            }

            if (unfinished.Count > 0)
            {
                logger.LogInformation("Re-queued {Count} unfinished receipts", unfinished.Count);
            }
        }

        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var receiptId in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ReceiptExtractor>().RunAsync(receiptId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutting down; the receipt stays Processing and is re-queued on the next start.
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Extraction crashed for receipt {ReceiptId}", receiptId);
            }
        }
    }
}
