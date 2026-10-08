using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ReceiptSplit.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Receipt> Receipts => Set<Receipt>();

    public DbSet<ReceiptLine> ReceiptLines => Set<ReceiptLine>();

    public DbSet<User> Users => Set<User>();

    public DbSet<ExternalIdentity> ExternalIdentities => Set<ExternalIdentity>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite has no date type and returns DateTime with an unspecified kind; everything is stored as UTC.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // SQLite stores decimal as TEXT, which breaks SQL sums and ordering, so money is stored as integer cents.
        var cents = new ValueConverter<decimal, long>(v => (long)(Precision.Cents(v) * 100m), v => v / 100m);
        // Tax rates need three decimals of a percent for Quebec's combined 14.975%.
        var rate = new ValueConverter<decimal, long>(v => (long)(Precision.Rate(v) * 1000m), v => v / 1000m);

        modelBuilder.Entity<Receipt>(receipt =>
        {
            receipt.HasIndex(r => r.CreatedAt);
            // A member's receipts, newest first.
            receipt.HasIndex(r => new { r.OwnerId, r.CreatedAt });
            // Users aren't deleted yet; when they are, their receipts need somewhere to go first.
            receipt.HasOne<User>()
                .WithMany()
                .HasForeignKey(r => r.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);
            receipt.Property(r => r.Status).HasConversion<string>().HasMaxLength(16);
            receipt.Property(r => r.TaxRatePercent).HasConversion(rate);
            receipt.Property(r => r.DiscountPercent).HasConversion(rate);
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

        modelBuilder.Entity<User>(user =>
        {
            // SQLite's unique indexes allow any number of nulls, so released emails don't collide.
            user.HasIndex(u => u.Email).IsUnique();
            user.Property(u => u.Email).HasMaxLength(320);
            user.Property(u => u.Name).HasMaxLength(200);
            user.HasMany(u => u.ExternalIdentities)
                .WithOne()
                .HasForeignKey(i => i.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ExternalIdentity>(identity =>
        {
            identity.HasIndex(i => new { i.Provider, i.Subject }).IsUnique();
            identity.HasIndex(i => new { i.UserId, i.Provider }).IsUnique();
            identity.Property(i => i.Provider).HasConversion<string>().HasMaxLength(16);
            identity.Property(i => i.Subject).HasMaxLength(255);
        });
    }

}

internal sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : v,
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
