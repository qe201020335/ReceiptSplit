using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using ReceiptSplit.Accounts;
using ReceiptSplit.Data;

namespace ReceiptSplit.Testing;

/// <summary>
/// Stands in for the proxy's identity lookup. By default everyone has a Google account of their own, derived from
/// the email; <see cref="Answers"/> and <see cref="Unavailable"/> change that.
/// </summary>
public sealed class FakeIdentityLookup : IIdentityLookup
{
    public const string SignOutPath = "/cdn-cgi/access/logout";

    public string? SignOutUrl => SignOutPath;

    /// <summary>The email of every lookup, in order.</summary>
    public ConcurrentQueue<string> Lookups { get; } = new();

    /// <summary>What the lookup returns for an email, in place of <see cref="GoogleAccount"/>.</summary>
    public ConcurrentDictionary<string, IdentityLookupResult> Answers { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>When set, every lookup fails as if the proxy couldn't be reached.</summary>
    public bool Unavailable { get; set; }

    /// <summary>The Google account a person has unless <see cref="Answers"/> says otherwise.</summary>
    public static ProviderIdentity GoogleAccount(string email, string? name = null) =>
        new(IdentityProvider.Google, $"google-{email.ToLowerInvariant()}", name ?? $"Name of {email}");

    public Task<IdentityLookupResult> LookupAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var email = context.User.FindFirst(AccountClaims.Email)?.Value ?? "";
        Lookups.Enqueue(email);
        if (Unavailable)
        {
            return Task.FromResult(IdentityLookupResult.Unavailable);
        }

        return Task.FromResult(Answers.TryGetValue(email, out var answer)
            ? answer
            : IdentityLookupResult.Found(GoogleAccount(email)));
    }
}
