namespace ReceiptSplit.Accounts.CloudflareAccess;

/// <summary>The Cloudflare Access application in front of the app, from Zero Trust's Access settings.</summary>
internal sealed class CloudflareAccessOptions
{
    public const string SectionName = "Auth:CloudflareAccess";

    /// <summary>The team domain, such as <c>myteam.cloudflareaccess.com</c>, with or without https.</summary>
    public string? TeamDomain { get; set; }

    /// <summary>The application's Audience (AUD) tag.</summary>
    public string? Audience { get; set; }

    /// <summary>The team domain as Access writes it in a token's issuer, which is also where its endpoints are.</summary>
    public string Issuer
    {
        get
        {
            var domain = (TeamDomain ?? "").Trim().TrimEnd('/');
            return domain.Contains("://", StringComparison.Ordinal) ? domain : $"https://{domain}";
        }
    }

    /// <summary>The public keys Access signs its tokens with.</summary>
    public string CertsUrl => $"{Issuer}/cdn-cgi/access/certs";
}
