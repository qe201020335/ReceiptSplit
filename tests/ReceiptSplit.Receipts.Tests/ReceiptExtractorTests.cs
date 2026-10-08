using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using ReceiptSplit.Data;
using ReceiptSplit.Extraction;
using ReceiptSplit.Testing;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace ReceiptSplit.Receipts.Tests;

public sealed class ReceiptExtractorTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _connection.OpenAsync(Ct);
        await using var db = NewDb();
        await db.Database.MigrateAsync(Ct);
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task A_receipt_deleted_just_before_it_is_read_is_skipped()
    {
        var receipt = new Receipt { OriginalFileName = "a.jpg", StoredFileName = "a.jpg", ContentType = "image/jpeg" };
        await using (var setup = NewDb())
        {
            setup.Receipts.Add(receipt);
            await setup.SaveChangesAsync(Ct);
        }

        // A delete commits after the extractor has read the queued receipt, but before it marks it as being read.
        var deleteFirst = new BeforeFirstSave(async () =>
        {
            await using var other = NewDb();
            await other.Receipts.Where(r => r.Id == receipt.Id).ExecuteDeleteAsync(Ct);
        });
        await using var db = NewDb(deleteFirst);
        var llm = new FakeLlamaClient();
        var llmOptions = MsOptions.Create(new LlmOptions());

        await new ReceiptExtractor(
                db,
                new ImagePreparer(llmOptions),
                llm,
                MsOptions.Create(new StorageOptions { Root = Path.GetTempPath() }),
                llmOptions,
                NullLogger<ReceiptExtractor>.Instance)
            .RunAsync(receipt.Id, Ct);

        Assert.Empty(llm.Requests);
    }

    private AppDbContext NewDb(params IInterceptor[] interceptors) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).AddInterceptors(interceptors).Options);

    private sealed class BeforeFirstSave(Func<Task> action) : SaveChangesInterceptor
    {
        private bool _done;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!_done)
            {
                _done = true;
                await action();
            }

            return result;
        }
    }
}
