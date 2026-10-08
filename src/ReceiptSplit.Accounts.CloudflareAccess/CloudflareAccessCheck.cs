using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ReceiptSplit.Accounts.CloudflareAccess;

/// <summary>
/// Checks at startup that the Access settings are present and that Access's keys can be fetched, which catches a
/// mistyped team domain at deploy time instead of on every request. Skipped while the development user signs
/// requests in, since then no token is checked.
/// </summary>
internal sealed class CloudflareAccessCheck(
    IOptions<CloudflareAccessOptions> options,
    IOptions<AuthOptions> auth,
    IHostEnvironment environment,
    IOptionsMonitor<JwtBearerOptions> jwtBearer,
    ILogger<CloudflareAccessCheck> logger)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>Logs why Access tokens can't be checked and returns false, or returns true when they can.</summary>
    public async Task<bool> RunAsync(CancellationToken cancellationToken)
    {
        if (auth.Value.SignsInDevUser(environment))
        {
            logger.LogWarning("Requests are signed in as the development user {Email}", auth.Value.DevUser);
            return true;
        }

        var access = options.Value;
        if (string.IsNullOrWhiteSpace(access.TeamDomain) || string.IsNullOrWhiteSpace(access.Audience))
        {
            logger.LogCritical(
                "Auth:CloudflareAccess:TeamDomain and Auth:CloudflareAccess:Audience must both be set; without them " +
                "no request can be signed in");
            return false;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            var manager = jwtBearer.Get(CloudflareAccessExtensions.AuthenticationScheme).ConfigurationManager!;
            var configuration = await manager.GetConfigurationAsync(timeout.Token);
            if (configuration.SigningKeys.Count == 0)
            {
                logger.LogCritical("Cloudflare Access at {Url} lists no signing keys", access.CertsUrl);
                return false;
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // The configuration manager wraps the cause; its message names what went wrong.
            var error = ex is OperationCanceledException ? $"no answer within {Timeout.TotalSeconds} s" : ex.Message;
            logger.LogCritical("Couldn't fetch the Cloudflare Access keys from {Url}: {Error}", access.CertsUrl, error);
            return false;
        }

        logger.LogInformation("Checking Cloudflare Access tokens from {Issuer}", access.Issuer);
        return true;
    }
}
