using Microsoft.Extensions.Hosting;

namespace ReceiptSplit.Accounts;

/// <summary>Account settings that don't depend on the identity-aware proxy in front of the app.</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>Emails of the admins, matched against the signed-in email ignoring case.</summary>
    public List<string> Admins { get; set; } = [];

    /// <summary>
    /// In Development, every request is signed in as this email without the proxy. The app refuses to start with it
    /// set in any other environment.
    /// </summary>
    public string? DevUser { get; set; }

    /// <summary>Whether requests are signed in as <see cref="DevUser"/> instead of through the proxy.</summary>
    public bool SignsInDevUser(IHostEnvironment environment) =>
        environment.IsDevelopment() && !string.IsNullOrWhiteSpace(DevUser);
}
