using Microsoft.AspNetCore.Http;
using ReceiptSplit.Data;

namespace ReceiptSplit.Accounts;

/// <summary>
/// Finds the full identity behind a request the proxy signed in: which provider the person signed in with, that
/// provider's stable account id and their name. Implemented by the proxy's library, since only it knows how.
/// </summary>
public interface IIdentityLookup
{
    /// <summary>Where the proxy signs people out, for the account menu.</summary>
    string? SignOutUrl { get; }

    /// <summary>Called once per proxy session; the result is cached by the account middleware.</summary>
    Task<IdentityLookupResult> LookupAsync(HttpContext context, CancellationToken cancellationToken);
}

/// <summary>A provider's account, as the proxy reports it.</summary>
/// <param name="Subject">The provider's stable account id.</param>
public sealed record ProviderIdentity(IdentityProvider Provider, string Subject, string? Name);

public enum IdentityLookupStatus
{
    Found,

    /// <summary>The person signed in with a method the app doesn't accept.</summary>
    Unsupported,

    /// <summary>The identity couldn't be found out right now; the next request tries again.</summary>
    Unavailable,
}

public sealed record IdentityLookupResult(IdentityLookupStatus Status, ProviderIdentity? Identity)
{
    public static IdentityLookupResult Unsupported { get; } = new(IdentityLookupStatus.Unsupported, null);

    public static IdentityLookupResult Unavailable { get; } = new(IdentityLookupStatus.Unavailable, null);

    public static IdentityLookupResult Found(ProviderIdentity identity) => new(IdentityLookupStatus.Found, identity);
}
