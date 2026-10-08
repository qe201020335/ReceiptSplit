using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ReceiptSplit.Accounts;

/// <summary>
/// Turns the person the proxy signed in into the app's user, adding the user id and admin claims. A sign-in that
/// can't be used is answered with a problem on API requests; other paths go on, so the page can load and explain.
/// Results are cached per proxy session, so only a session's first request looks the identity up; requests that
/// arrive while it does, as the page's first calls do, wait for it instead of looking it up again.
/// </summary>
internal sealed class AccountMiddleware(
    RequestDelegate next,
    IMemoryCache cache,
    TimeProvider time,
    IOptions<AuthOptions> auth,
    ILogger<AccountMiddleware> logger)
{
    /// <summary>Short, so a released email takes effect soon without anything having to clear the cache.</summary>
    private static readonly TimeSpan RefusalLifetime = TimeSpan.FromMinutes(1);

    /// <summary>For tokens without an expiry, and for development sign-in, which has none.</summary>
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(5);

    /// <summary>Resolutions under way, by cache key. Lazy, so a lost race to add one never starts a second.</summary>
    private readonly ConcurrentDictionary<string, Lazy<Task<Outcome>>> _pending = new();

    public async Task InvokeAsync(HttpContext context, AccountService accounts)
    {
        var principal = context.User;
        if (principal.Identity?.IsAuthenticated != true
            || principal.FindFirst(AccountClaims.Email)?.Value is not { Length: > 0 } email)
        {
            await next(context);
            return;
        }

        var outcome = await ResolveAsync(context, accounts, email);
        if (outcome.UserId is { } userId)
        {
            List<Claim> claims = [new(AccountClaims.UserId, userId.ToString())];
            // The account's email rather than the token's: an email held by another user doesn't make this one an
            // admin. Compared on every request, so a change to the admin list applies at once.
            if (outcome.Email is { } accountEmail && auth.Value.IsAdmin(accountEmail))
            {
                claims.Add(new Claim(AccountClaims.Admin, "true"));
            }

            principal.AddIdentity(new ClaimsIdentity(claims));
        }
        else if (context.Request.Path.StartsWithSegments("/api"))
        {
            var signOutUrl = context.RequestServices.GetService<IIdentityLookup>()?.SignOutUrl;
            await outcome.Problem!.WriteAsync(context, signOutUrl);
            return;
        }

        await next(context);
    }

    private async Task<Outcome> ResolveAsync(HttpContext context, AccountService accounts, string email)
    {
        var principal = context.User;
        var now = time.GetUtcNow();
        var development = principal.Identity?.AuthenticationType == DevelopmentAuthenticationHandler.SchemeName;
        var session = development ? $"development:{email}" : principal.FindFirst(AccountClaims.Session)?.Value;
        var key = session is null ? null : $"account:{session}";
        if (key is null)
        {
            // A proxy that names no session (Access always does) gets no caching, and so nothing to share either:
            // every request resolves on its own, and overlapping first requests race to create the user. The
            // unique indexes and AccountService's retry keep that correct; EF just logs the losing insert as an error.
            return await ResolveUncachedAsync(context, accounts, email, development, null);
        }

        if (cache.TryGetValue(key, out Cached? cached) && cached!.Until > now)
        {
            return cached.Outcome;
        }

        // Only saves the work, it doesn't guarantee a single insert: this is one process, and two sessions of the
        // same new person still resolve separately. The unique indexes and AccountService's retry are what make a
        // race end in one user.
        var pending = _pending.GetOrAdd(key, _ => new Lazy<Task<Outcome>>(
            () => ResolveUncachedAsync(context, accounts, email, development, key)));
        try
        {
            return await pending.Value;
        }
        finally
        {
            _pending.TryRemove(KeyValuePair.Create(key, pending));
        }
    }

    /// <summary>
    /// Looks the account up and caches the outcome under <paramref name="key"/>. Other requests may be waiting on it,
    /// so it isn't cancelled with the request that started it.
    /// </summary>
    private async Task<Outcome> ResolveUncachedAsync(
        HttpContext context, AccountService accounts, string email, bool development, string? key)
    {
        var principal = context.User;
        var now = time.GetUtcNow();
        Outcome outcome;
        DateTimeOffset until;
        if (development)
        {
            var user = await accounts.ResolveDevelopmentAsync(email, CancellationToken.None);
            outcome = new Outcome(user.Id, user.Email, null);
            until = now + DefaultLifetime;
        }
        else
        {
            var lookup = context.RequestServices.GetService<IIdentityLookup>();
            var result = lookup is null
                ? IdentityLookupResult.Unavailable
                : await lookup.LookupAsync(context, CancellationToken.None);
            switch (result.Status)
            {
                case IdentityLookupStatus.Found:
                    var resolution = await accounts.ResolveAsync(email, result.Identity!, CancellationToken.None);
                    if (resolution.UserId is { } userId)
                    {
                        outcome = new Outcome(userId, resolution.Email, null);
                        until = SessionEnd(principal) ?? now + DefaultLifetime;
                    }
                    else
                    {
                        logger.LogWarning(
                            "Refused the sign-in of {Email}: another user holds the email; an admin can release it",
                            email);
                        outcome = new Outcome(null, null, SignInProblem.AccountConflict);
                        until = now + RefusalLifetime;
                    }

                    break;
                case IdentityLookupStatus.Unsupported:
                    logger.LogWarning("Refused the sign-in of {Email}: the sign-in method isn't supported", email);
                    outcome = new Outcome(null, null, SignInProblem.UnsupportedSignIn);
                    until = now + RefusalLifetime;
                    break;
                default:
                    // Not cached: the next request tries again.
                    return new Outcome(null, null, SignInProblem.IdentityUnavailable);
            }
        }

        // A token the handler accepted within its clock skew can already be past its expiry, and so past the end of
        // what it could be cached for; Access replaces it soon, so it just isn't cached.
        if (key is not null && until > now)
        {
            // The cache's own expiry only frees memory; the time provider decides, so tests can move the clock.
            cache.Set(key, new Cached(outcome, until), until - now);
        }

        return outcome;
    }

    private static DateTimeOffset? SessionEnd(ClaimsPrincipal principal) =>
        long.TryParse(principal.FindFirst(AccountClaims.Expires)?.Value, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;

    /// <param name="Email">The account's stored email, which decides whether the user is an admin.</param>
    private sealed record Outcome(Guid? UserId, string? Email, SignInProblem? Problem);

    private sealed record Cached(Outcome Outcome, DateTimeOffset Until);
}
