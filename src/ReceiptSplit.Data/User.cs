namespace ReceiptSplit.Data;

/// <summary>
/// Someone who signs in to the app. The id is the app's own, so an account survives a changed email or a second
/// sign-in provider; providers are linked through <see cref="ExternalIdentities"/>.
/// </summary>
public class User
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Lowercased, and unique among users. Null once an admin has released it to someone else.</summary>
    public string? Email { get; set; }

    public string? Name { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<ExternalIdentity> ExternalIdentities { get; set; } = [];
}
