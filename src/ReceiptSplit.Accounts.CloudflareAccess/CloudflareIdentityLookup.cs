using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ReceiptSplit.Data;

namespace ReceiptSplit.Accounts.CloudflareAccess;

/// <summary>
/// Asks Access for the full identity behind a request's token. The token carries only the email; the identity adds
/// the sign-in method and the provider's stable account id, which accounts are linked by. Its response also holds
/// the person's IP address, location and devices, so it is never logged.
/// </summary>
internal sealed class CloudflareIdentityLookup(HttpClient http, ILogger<CloudflareIdentityLookup> logger)
    : IIdentityLookup
{
    public const string Path = "cdn-cgi/access/get-identity";

    /// <summary>Access answers this path on the application's own hostname.</summary>
    public string? SignOutUrl => "/cdn-cgi/access/logout";

    public async Task<IdentityLookupResult> LookupAsync(HttpContext context, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Path);
        // Access reads the token from its cookie here, as it does for a browser.
        var token = context.Request.Headers[CloudflareAccessExtensions.TokenHeader].ToString();
        request.Headers.Add("Cookie", $"CF_Authorization={token}");
        AccessIdentity? identity;
        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Cloudflare Access answered {Status} to the identity lookup", (int)response.StatusCode);
                return IdentityLookupResult.Unavailable;
            }

            identity = await response.Content.ReadFromJsonAsync<AccessIdentity>(cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("Couldn't look up the identity at Cloudflare Access: {Error}", ex.Message);
            return IdentityLookupResult.Unavailable;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Cloudflare Access didn't answer the identity lookup in time");
            return IdentityLookupResult.Unavailable;
        }
        catch (JsonException)
        {
            // The exception's message can quote the response, so only the fact is logged.
            logger.LogWarning("Cloudflare Access answered the identity lookup with something other than an identity");
            return IdentityLookupResult.Unavailable;
        }

        // Google personal accounts sign in as "google" and Workspace ones as "google-apps", both with Google's id.
        if (identity?.Idp?.Type is not ("google" or "google-apps"))
        {
            return IdentityLookupResult.Unsupported;
        }

        if (string.IsNullOrWhiteSpace(identity.Id))
        {
            logger.LogWarning("Cloudflare Access returned an identity without an account id");
            return IdentityLookupResult.Unavailable;
        }

        var name = string.IsNullOrWhiteSpace(identity.Name) ? null : identity.Name.Trim();
        return IdentityLookupResult.Found(new ProviderIdentity(IdentityProvider.Google, identity.Id, name));
    }

    /// <summary>The fields used from Access's identity; the rest is never read.</summary>
    private sealed record AccessIdentity(string? Id, string? Name, AccessIdentityProvider? Idp);

    private sealed record AccessIdentityProvider(string? Type);
}
