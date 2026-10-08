using System.Security.Claims;

namespace ReceiptSplit.Accounts;

/// <summary>Who a receipt operation is done for: members act on their own receipts, admins on all of them.</summary>
public sealed record Actor(Guid UserId, bool IsAdmin)
{
    /// <summary>The signed-in user, from the claims the account middleware adds once it has resolved them.</summary>
    /// <exception cref="InvalidOperationException">The request has no resolved user.</exception>
    public static Actor From(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirst(AccountClaims.UserId)?.Value, out var id)
            ? new Actor(id, user.HasClaim(AccountClaims.Admin, "true"))
            : throw new InvalidOperationException("The request has no signed-in user.");
}
