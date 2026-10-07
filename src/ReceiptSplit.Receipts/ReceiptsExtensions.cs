using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ReceiptSplit.Data;
using ReceiptSplit.Extraction;

namespace ReceiptSplit.Receipts;

public static class ReceiptsExtensions
{
    /// <summary>
    /// Registers receipt handling and the background extraction, along with the database and the model client it
    /// needs. A second call does nothing.
    /// </summary>
    public static IHostApplicationBuilder AddReceipts(this IHostApplicationBuilder builder)
    {
        if (builder.Services.Any(s => s.ServiceType == typeof(ReceiptsMarker)))
        {
            return builder;
        }

        // First, so the database is migrated before the worker re-queues unfinished receipts.
        builder.AddDatabase();
        builder.AddExtraction();

        builder.Services.AddSingleton<ReceiptsMarker>();
        builder.Services.AddSingleton<ImagePreparer>();
        builder.Services.AddSingleton<ExtractionQueue>();
        builder.Services.AddScoped<ReceiptExtractor>();
        builder.Services.AddScoped<ReceiptService>();
        builder.Services.AddHostedService<ExtractionWorker>();
        return builder;
    }

    private sealed class ReceiptsMarker;
}
