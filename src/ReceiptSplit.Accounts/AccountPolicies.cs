namespace ReceiptSplit.Accounts;

/// <summary>Authorization policies for endpoints that need more than a signed-in user.</summary>
public static class AccountPolicies
{
    /// <summary>Only users whose email is in <see cref="AuthOptions.Admins"/>.</summary>
    public const string Admin = "Admin";
}
