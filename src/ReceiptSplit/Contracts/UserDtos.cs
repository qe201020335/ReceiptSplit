using System.ComponentModel.DataAnnotations;

namespace ReceiptSplit.Contracts;

/// <summary>The signed-in user, for the account menu.</summary>
/// <param name="SignOutUrl">Where to sign out; null when there is nothing to sign out of, as in development.</param>
public sealed record AccountDto(Guid Id, string? Email, string? Name, bool IsAdmin, string? SignOutUrl);

/// <summary>Body of the request that clears an email from the user holding it.</summary>
public sealed record ReleaseEmailDto([Required, EmailAddress, StringLength(320)] string Email);
