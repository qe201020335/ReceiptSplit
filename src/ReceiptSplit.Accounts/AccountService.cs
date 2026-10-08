using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ReceiptSplit.Data;

namespace ReceiptSplit.Accounts;

/// <summary>
/// Turns a signed-in person into the app's user, and the account operations around it. Knows nothing about the
/// proxy: it gets a verified email and, through <see cref="IIdentityLookup"/>, the provider's stable account id.
/// </summary>
public sealed class AccountService(
    AppDbContext db,
    ILogger<AccountService> logger,
    IIdentityLookup? lookup = null)
{
    /// <summary>
    /// The user linked to <paramref name="identity"/>, created on the first sign-in. Refused when another user holds
    /// the email, so nobody reaches another account's receipts by email alone.
    /// </summary>
    internal async Task<AccountResolution> ResolveAsync(
        string email, ProviderIdentity identity, CancellationToken cancellationToken)
    {
        email = NormalizeEmail(email);
        try
        {
            return await TryResolveAsync(email, identity, cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two first requests for the same person raced to create or update a user; the winner's row is now there.
            db.ChangeTracker.Clear();
            return await TryResolveAsync(email, identity, cancellationToken);
        }
    }

    /// <summary>
    /// The development user, found or created by email. There's no provider behind it, so this can only be
    /// reached through development sign-in.
    /// </summary>
    internal async Task<User> ResolveDevelopmentAsync(string email, CancellationToken cancellationToken)
    {
        email = NormalizeEmail(email);
        if (await db.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken) is { } existing)
        {
            return existing;
        }

        var user = new User { Email = email };
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return user;
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return await db.Users.FirstAsync(u => u.Email == email, cancellationToken);
        }
    }

    /// <summary>The signed-in user, for the account menu; null when the request has no resolved user.</summary>
    public async Task<CurrentAccount?> GetCurrentAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(principal.FindFirst(AccountClaims.UserId)?.Value, out var id)
            || await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, cancellationToken) is not { } user)
        {
            return null;
        }

        // Development sign-in has nothing to sign out of.
        var signOutUrl = principal.Identity?.AuthenticationType == DevelopmentAuthenticationHandler.SchemeName
            ? null
            : lookup?.SignOutUrl;
        return new CurrentAccount(
            user.Id, user.Email, user.Name, principal.HasClaim(AccountClaims.Admin, "true"), signOutUrl);
    }

    /// <summary>
    /// Clears <paramref name="email"/> from the user holding it, who keeps their external identity and receipts.
    /// Someone refused because that user held the email gets a user of their own when their refusal expires.
    /// </summary>
    /// <returns>False when no user holds the email.</returns>
    public async Task<bool> ReleaseEmailAsync(string email, CancellationToken cancellationToken)
    {
        email = NormalizeEmail(email);
        if (await db.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken) is not { } user)
        {
            return false;
        }

        user.Email = null;
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Released the email of user {UserId}", user.Id);
        return true;
    }

    private async Task<AccountResolution> TryResolveAsync(
        string email, ProviderIdentity identity, CancellationToken cancellationToken)
    {
        var linked = await db.ExternalIdentities
            .Where(i => i.Provider == identity.Provider && i.Subject == identity.Subject)
            .Select(i => i.UserId)
            .ToListAsync(cancellationToken);
        if (linked is [var userId])
        {
            var user = await db.Users.FirstAsync(u => u.Id == userId, cancellationToken);
            await UpdateAsync(user, email, identity.Name, cancellationToken);
            return AccountResolution.Resolved(user.Id);
        }

        if (await db.Users.AnyAsync(u => u.Email == email, cancellationToken))
        {
            return AccountResolution.Conflict;
        }

        var created = new User { Email = email, Name = identity.Name };
        created.ExternalIdentities.Add(
            new ExternalIdentity { Provider = identity.Provider, Subject = identity.Subject });
        db.Users.Add(created);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Created user {UserId} on their first sign-in", created.Id);
        return AccountResolution.Resolved(created.Id);
    }

    /// <summary>Picks up a name or email changed at the provider, unless the new email is another user's.</summary>
    private async Task UpdateAsync(User user, string email, string? name, CancellationToken cancellationToken)
    {
        if (name is not null)
        {
            user.Name = name;
        }

        if (user.Email != email)
        {
            if (await db.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken) is { } holder)
            {
                logger.LogWarning(
                    "User {UserId} signed in with an email that user {HolderId} holds; their stored email is kept",
                    user.Id, holder.Id);
            }
            else
            {
                user.Email = email;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}

/// <summary>The signed-in user, as the account menu shows them.</summary>
/// <param name="SignOutUrl">Null when there is nothing to sign out of, as with development sign-in.</param>
public sealed record CurrentAccount(Guid Id, string? Email, string? Name, bool IsAdmin, string? SignOutUrl);

/// <summary>The user a sign-in resolved to, or a conflict when the email belongs to another account.</summary>
internal sealed record AccountResolution(Guid? UserId)
{
    public static AccountResolution Conflict { get; } = new((Guid?)null);

    public static AccountResolution Resolved(Guid userId) => new(userId);
}
