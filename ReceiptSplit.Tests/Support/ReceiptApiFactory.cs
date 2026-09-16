using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ReceiptSplit.Extraction;

namespace ReceiptSplit.Tests.Support;

/// <summary>Runs the app against a throwaway storage directory, with a fake model unless told otherwise.</summary>
public sealed class ReceiptApiFactory(bool useFakeLlm = true) : WebApplicationFactory<Program>
{
    public string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), "receiptsplit-tests", Guid.NewGuid().ToString("N"));

    public FakeLlamaClient Llm { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Storage:Root", StorageRoot);
        if (useFakeLlm)
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ILlamaClient>();
                services.AddSingleton<ILlamaClient>(Llm);
            });
        }
    }

    public override async ValueTask DisposeAsync()
    {
        Llm.Release();
        await base.DisposeAsync();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(StorageRoot))
        {
            Directory.Delete(StorageRoot, recursive: true);
        }
    }
}
