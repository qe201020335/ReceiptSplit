using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace ReceiptSplit.Accounts.CloudflareAccess;

/// <summary>
/// Reads Access's key set into the configuration the JWT bearer handler validates with. Access publishes the keys
/// but no OpenID discovery document for self-hosted applications, so the handler can't find them by itself; its
/// configuration manager still caches them and refreshes when a token names a key it hasn't seen.
/// </summary>
internal sealed class AccessKeySetRetriever : IConfigurationRetriever<OpenIdConnectConfiguration>
{
    public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(
        string address, IDocumentRetriever retriever, CancellationToken cancel)
    {
        var keys = new JsonWebKeySet(await retriever.GetDocumentAsync(address, cancel));
        var configuration = new OpenIdConnectConfiguration();
        foreach (var key in keys.GetSigningKeys())
        {
            configuration.SigningKeys.Add(key);
        }

        return configuration;
    }
}
