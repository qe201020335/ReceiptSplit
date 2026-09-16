using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ReceiptSplit.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Receipt> Receipts => Set<Receipt>();

    public DbSet<ReceiptLine> ReceiptLines => Set<ReceiptLine>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite has no date type and returns DateTime with an unspecified kind; everything is stored as UTC.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // SQLite stores decimal as TEXT, which breaks SQL sums and ordering, so money is stored as integer cents.
        var cents = new ValueConverter<decimal, long>(v => (long)Math.Round(v * 100m), v => v / 100m);
        // Tax rates need three decimals of a percent for Quebec's combined 14.975%.
        var rate = new ValueConverter<decimal, long>(v => (long)Math.Round(v * 1000m), v => v / 1000m);

        modelBuilder.Entity<Receipt>(receipt =>
        {
            receipt.HasIndex(r => r.CreatedAt);
            receipt.Property(r => r.Status).HasConversion<string>().HasMaxLength(16);
            receipt.Property(r => r.TaxRatePercent).HasConversion(rate);
            receipt.Property(r => r.Subtotal).HasConversion(cents);
            receipt.Property(r => r.Tax).HasConversion(cents);
            receipt.Property(r => r.Total).HasConversion(cents);
            receipt.HasMany(r => r.Lines)
                .WithOne()
                .HasForeignKey(l => l.ReceiptId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReceiptLine>(line =>
        {
            line.HasIndex(l => new { l.ReceiptId, l.Position }).IsUnique();
            line.Property(l => l.Amount).HasConversion(cents);
            line.Property(l => l.Discount).HasConversion(cents);
        });
    }

}

internal sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : v,
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
