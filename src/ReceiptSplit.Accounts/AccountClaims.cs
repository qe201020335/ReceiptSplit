namespace ReceiptSplit.Accounts;

/// <summary>
/// The claims accounts are built from, which the identity-aware proxy's library provides, and those the app adds
/// once it knows the user.
/// </summary>
public static class AccountClaims
{
    /// <summary>The verified email, under its standard JWT name.</summary>
    public const string Email = "email";

    /// <summary>When the proxy's session ends, under its standard JWT name, in seconds since 1970.</summary>
    public const string Expires = "exp";

    /// <summary>
    /// The proxy's sign-in session, which changes when the person signs in again. The full identity is looked up
    /// once per session.
    /// </summary>
    public const string Session = "receiptsplit/session";

    /// <summary>The app's own user id, added once the account is resolved.</summary>
    public const string UserId = "receiptsplit/user_id";

    /// <summary>Present, as "true", when the signed-in email is one of the configured admins.</summary>
    public const string Admin = "receiptsplit/admin";
}
