namespace ReceiptSplit.Data;

/// <summary>A sign-in provider's account linked to a user, at most one per provider.</summary>
public class ExternalIdentity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    public IdentityProvider Provider { get; set; }

    /// <summary>The provider's stable account id, which unlike the email never changes for an account.</summary>
    public required string Subject { get; set; }

    public DateTime LinkedAt { get; set; } = DateTime.UtcNow;
}
