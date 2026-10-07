namespace ReceiptSplit.Accounts;

/// <summary>The claims accounts are built from, and those the app adds once it knows the user.</summary>
public static class AccountClaims
{
    /// <summary>The verified email, under its standard JWT name.</summary>
    public const string Email = "email";
}
