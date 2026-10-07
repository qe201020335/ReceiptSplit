using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ReceiptSplit.Data;

public static class DatabaseExtensions
{
    /// <summary>
    /// Registers the storage settings, the SQLite database and the startup migrations. Libraries that need the
    /// database call this too, so a second call does nothing.
    /// </summary>
    public static IHostApplicationBuilder AddDatabase(this IHostApplicationBuilder builder)
    {
        if (builder.Services.Any(s => s.ServiceType == typeof(DatabaseMarker)))
        {
            return builder;
        }

        builder.Services.AddSingleton<DatabaseMarker>();
        builder.Services.AddOptions<StorageOptions>()
            .Bind(builder.Configuration.GetSection(StorageOptions.SectionName))
            .PostConfigure(o => o.Root = Path.GetFullPath(o.Root, builder.Environment.ContentRootPath));
        builder.Services.AddDbContext<AppDbContext>((sp, options) =>
            options.UseSqlite($"Data Source={sp.GetRequiredService<IOptions<StorageOptions>>().Value.DatabasePath}"));
        // Hosted services start in registration order, so anything registered after this finds the database migrated.
        builder.Services.AddHostedService<DatabaseInitializer>();
        return builder;
    }

    private sealed class DatabaseMarker;
}
