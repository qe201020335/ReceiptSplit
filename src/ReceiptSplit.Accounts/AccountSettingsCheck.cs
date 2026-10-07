using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ReceiptSplit.Accounts;

/// <summary>Checks at startup that the account settings are safe to run with.</summary>
internal sealed class AccountSettingsCheck(
    IOptions<AuthOptions> options,
    IHostEnvironment environment,
    ILogger<AccountSettingsCheck> logger)
{
    /// <summary>Logs why the settings can't be used and returns false, or returns true when they can.</summary>
    public bool Run()
    {
        var auth = options.Value;
        if (!string.IsNullOrWhiteSpace(auth.DevUser) && !environment.IsDevelopment())
        {
            // Anyone who can reach the app would be signed in as this user.
            logger.LogCritical(
                "Auth:DevUser is set in the {Environment} environment; it is only allowed in Development",
                environment.EnvironmentName);
            return false;
        }

        if (auth.Admins.All(string.IsNullOrWhiteSpace))
        {
            logger.LogWarning("Auth:Admins lists no admins, so nobody can see the receipts that have no owner");
        }

        return true;
    }
}
