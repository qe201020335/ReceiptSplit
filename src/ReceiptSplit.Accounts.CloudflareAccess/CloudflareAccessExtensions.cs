using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace ReceiptSplit.Accounts.CloudflareAccess;

public static class CloudflareAccessExtensions
{
    internal const string AuthenticationScheme = "CloudflareAccess";

    /// <summary>The header Access adds to every request it lets through, holding its signed token.</summary>
    internal const string TokenHeader = "Cf-Access-Jwt-Assertion";

    internal const string KeysClient = "CloudflareAccessKeys";

    /// <summary>
    /// Signs requests in from the token Cloudflare Access adds to each one, after checking its signature, issuer,
    /// audience and expiry, and looks up the full identity behind it. Registers the accounts first. A second call
    /// does nothing.
    /// </summary>
    public static IHostApplicationBuilder AddCloudflareAccess(this IHostApplicationBuilder builder)
    {
        if (builder.Services.Any(s => s.ServiceType == typeof(CloudflareAccessMarker)))
        {
            return builder;
        }

        builder.AddAccounts();

        builder.Services.AddSingleton<CloudflareAccessMarker>();
        builder.Services.AddOptions<CloudflareAccessOptions>()
            .Bind(builder.Configuration.GetSection(CloudflareAccessOptions.SectionName));
        builder.Services.AddHttpClient(KeysClient);

        builder.Services.AddAuthentication().AddJwtBearer(AuthenticationScheme);
        builder.Services.AddOptions<JwtBearerOptions>(AuthenticationScheme)
            .Configure<IOptions<CloudflareAccessOptions>, IHttpClientFactory>((jwt, options, http) =>
            {
                var access = options.Value;
                // Claims keep their JWT names (email, identity_nonce) instead of being mapped to .NET's.
                jwt.MapInboundClaims = false;
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = access.Issuer,
                    ValidAudience = access.Audience,
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                    NameClaimType = AccountClaims.Email,
                };
                jwt.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                    access.CertsUrl,
                    new AccessKeySetRetriever(),
                    new HttpDocumentRetriever(http.CreateClient(KeysClient)) { RequireHttps = true });
                jwt.Events = new JwtBearerEvents
                {
                    // Only Access's header counts; without it the request isn't signed in, whatever else it carries.
                    OnMessageReceived = context =>
                    {
                        if (context.Request.Headers[TokenHeader].ToString() is { Length: > 0 } token)
                        {
                            context.Token = token;
                        }
                        else
                        {
                            context.NoResult();
                        }

                        return Task.CompletedTask;
                    },
                    // Access changes identity_nonce when someone signs in again, and documents it as the key for
                    // caching the identity.
                    OnTokenValidated = context =>
                    {
                        if (context.Principal?.Identity is ClaimsIdentity identity
                            && identity.FindFirst("identity_nonce")?.Value is { Length: > 0 } nonce)
                        {
                            identity.AddClaim(new Claim(AccountClaims.Session, nonce));
                        }

                        return Task.CompletedTask;
                    },
                };
            });
        builder.Services.AddHttpClient<IIdentityLookup, CloudflareIdentityLookup>((services, http) =>
            {
                var access = services.GetRequiredService<IOptions<CloudflareAccessOptions>>().Value;
                // Left unset without a team domain, as in development, where the lookup is never used.
                if (!string.IsNullOrWhiteSpace(access.TeamDomain))
                {
                    http.BaseAddress = new Uri($"{access.Issuer}/");
                }

                http.Timeout = TimeSpan.FromSeconds(10);
            })
            // The token goes in a Cookie header of its own, which a cookie container would replace.
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseCookies = false });
        // Development sign-in, when it's on, has already claimed the default.
        builder.Services.AddOptions<AuthenticationOptions>()
            .Configure(options => options.DefaultScheme ??= AuthenticationScheme);

        builder.Services.AddSingleton<CloudflareAccessCheck>();
        return builder;
    }

    /// <summary>
    /// Checks that the Access settings are present and its keys can be fetched, logging why when they can't. Run
    /// before the app starts, so a mistyped team domain stops a deploy instead of refusing every request.
    /// </summary>
    public static async Task<bool> CheckCloudflareAccessAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default) =>
        await services.GetRequiredService<CloudflareAccessCheck>().RunAsync(cancellationToken);

    private sealed class CloudflareAccessMarker;
}
