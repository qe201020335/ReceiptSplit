using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace ReceiptSplit.Testing;

/// <summary>
/// Makes tokens shaped like the ones Cloudflare Access adds to requests, signed with a key generated for the test
/// run, so the app's real token checks run in tests.
/// </summary>
public static class AccessTokens
{
    public const string Header = "Cf-Access-Jwt-Assertion";

    public const string TeamDomain = "test.cloudflareaccess.com";

    public const string Issuer = $"https://{TeamDomain}";

    public const string Audience = "test-audience-tag";

    public static RsaSecurityKey Key { get; } = NewKey();

    /// <summary>What the app would read from Access's key set: only <see cref="Key"/>.</summary>
    public static OpenIdConnectConfiguration Configuration => new() { SigningKeys = { Key } };

    /// <summary>A key nobody trusts, by default under the same id as <see cref="Key"/>.</summary>
    public static RsaSecurityKey NewKey(string keyId = "test-key") => new(RSA.Create(2048)) { KeyId = keyId };

    /// <param name="nonce">The Access session; a new one per token unless given.</param>
    /// <param name="subject">Access's own user id; derived from the email unless given.</param>
    public static string Create(
        string email,
        string? nonce = null,
        string? subject = null,
        DateTime? expires = null,
        string issuer = Issuer,
        string audience = Audience,
        SecurityKey? key = null)
    {
        var expiry = expires ?? DateTime.UtcNow.AddHours(1);
        var issued = expiry.AddHours(-24);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            IssuedAt = issued,
            NotBefore = issued,
            Expires = expiry,
            Claims = new Dictionary<string, object>
            {
                // Access sends the audience as a list.
                ["aud"] = new[] { audience },
                ["email"] = email,
                ["identity_nonce"] = nonce ?? Guid.NewGuid().ToString("N"),
                ["sub"] = subject ?? $"access-{email}",
                ["type"] = "app",
                ["country"] = "CA",
            },
            SigningCredentials = new SigningCredentials(key ?? Key, SecurityAlgorithms.RsaSha256),
        });
    }
}
