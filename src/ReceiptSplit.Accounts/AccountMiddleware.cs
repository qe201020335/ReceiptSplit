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
/// Results are cached per proxy session, so only a session's first request looks the identity up.
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
            // Decided on every request from the current settings, so it isn't cached with the user.
            if (auth.Value.IsAdmin(email))
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
        if (key is not null && cache.TryGetValue(key, out Cached? cached) && cached!.Until > now)
        {
            return cached.Outcome;
        }

        Outcome outcome;
        DateTimeOffset until;
        if (development)
        {
            outcome = new Outcome((await accounts.ResolveDevelopmentAsync(email, context.RequestAborted)).Id, null);
            until = now + DefaultLifetime;
        }
        else
        {
            var lookup = context.RequestServices.GetService<IIdentityLookup>();
            var result = lookup is null
                ? IdentityLookupResult.Unavailable
                : await lookup.LookupAsync(context, context.RequestAborted);
            switch (result.Status)
            {
                case IdentityLookupStatus.Found:
                    var resolution = await accounts.ResolveAsync(email, result.Identity!, context.RequestAborted);
                    if (resolution.UserId is { } userId)
                    {
                        outcome = new Outcome(userId, null);
                        until = SessionEnd(principal) ?? now + DefaultLifetime;
                    }
                    else
                    {
                        logger.LogWarning(
                            "Refused the sign-in of {Email}: another user holds the email; an admin can release it",
                            email);
                        outcome = new Outcome(null, SignInProblem.AccountConflict);
                        until = now + RefusalLifetime;
                    }

                    break;
                case IdentityLookupStatus.Unsupported:
                    logger.LogWarning("Refused the sign-in of {Email}: the sign-in method isn't supported", email);
                    outcome = new Outcome(null, SignInProblem.UnsupportedSignIn);
                    until = now + RefusalLifetime;
                    break;
                default:
                    // Not cached: the next request tries again.
                    return new Outcome(null, SignInProblem.IdentityUnavailable);
            }
        }

        if (key is not null)
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

    private sealed record Outcome(Guid? UserId, SignInProblem? Problem);

    private sealed record Cached(Outcome Outcome, DateTimeOffset Until);
}
