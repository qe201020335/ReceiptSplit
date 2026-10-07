using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ReceiptSplit.Data;

/// <summary>
/// Creates the storage directories and applies migrations at startup. Registered as the first hosted
/// service so it runs before the extraction worker, and also under WebApplicationFactory in tests.
/// </summary>
internal sealed class DatabaseInitializer(IServiceScopeFactory scopeFactory, IOptions<StorageOptions> storage) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(storage.Value.UploadsPath);

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
