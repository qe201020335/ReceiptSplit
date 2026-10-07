using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using ReceiptSplit.Testing;

namespace ReceiptSplit.Accounts.CloudflareAccess.Tests;

public class CloudflareAccessCheckTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Passes_when_access_lists_its_keys()
    {
        var keys = new StubHandler(HttpStatusCode.OK, KeySet());

        Assert.True(await RunAsync(keys));
        Assert.Equal($"{AccessTokens.Issuer}/cdn-cgi/access/certs", Assert.Single(keys.Requests).ToString());
    }

    [Fact]
    public async Task Fails_when_the_keys_cant_be_fetched()
    {
        Assert.False(await RunAsync(new StubHandler(HttpStatusCode.NotFound, "Not found")));
    }

    [Fact]
    public async Task Fails_when_access_lists_no_keys()
    {
        Assert.False(await RunAsync(new StubHandler(HttpStatusCode.OK, """{"keys": []}""")));
    }

    [Fact]
    public async Task Fails_without_the_team_domain()
    {
        var keys = new StubHandler(HttpStatusCode.OK, KeySet());

        Assert.False(await RunAsync(keys, teamDomain: null));
        Assert.Empty(keys.Requests);
    }

    [Fact]
    public async Task Fails_without_the_audience()
    {
        Assert.False(await RunAsync(new StubHandler(HttpStatusCode.OK, KeySet()), audience: null));
    }

    [Fact]
    public async Task Is_skipped_while_the_development_user_signs_requests_in()
    {
        var keys = new StubHandler(HttpStatusCode.NotFound, "Not found");

        Assert.True(await RunAsync(
            keys, teamDomain: null, audience: null, environment: Environments.Development, devUser: "dev@example.com"));
        Assert.Empty(keys.Requests);
    }

    [Theory]
    [InlineData("myteam.cloudflareaccess.com")]
    [InlineData("https://myteam.cloudflareaccess.com")]
    [InlineData(" https://myteam.cloudflareaccess.com/ ")]
    public void The_team_domain_can_be_given_with_or_without_the_scheme(string teamDomain)
    {
        var options = new CloudflareAccessOptions { TeamDomain = teamDomain };

        Assert.Equal("https://myteam.cloudflareaccess.com", options.Issuer);
        Assert.Equal("https://myteam.cloudflareaccess.com/cdn-cgi/access/certs", options.CertsUrl);
    }

    /// <summary>Runs the check as the app registers it, fetching the key set through <paramref name="keys"/>.</summary>
    private static async Task<bool> RunAsync(
        StubHandler keys,
        string? teamDomain = AccessTokens.TeamDomain,
        string? audience = AccessTokens.Audience,
        string environment = "Production",
        string? devUser = null)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = environment,
            DisableDefaults = true,
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:CloudflareAccess:TeamDomain"] = teamDomain,
            ["Auth:CloudflareAccess:Audience"] = audience,
            ["Auth:DevUser"] = devUser,
        });
        builder.Logging.ClearProviders();
        builder.AddCloudflareAccess();
        builder.Services.AddHttpClient(CloudflareAccessExtensions.KeysClient)
            .ConfigurePrimaryHttpMessageHandler(() => keys);
        await using var services = builder.Services.BuildServiceProvider();

        return await services.CheckCloudflareAccessAsync(Ct);
    }

    /// <summary>A key set shaped like Access's, which lists the keys again as certificates.</summary>
    private static string KeySet()
    {
        var rsa = AccessTokens.Key.Rsa.ExportParameters(includePrivateParameters: false);
        var key = new
        {
            kid = AccessTokens.Key.KeyId,
            kty = "RSA",
            alg = "RS256",
            use = "sig",
            e = Base64UrlEncoder.Encode(rsa.Exponent),
            n = Base64UrlEncoder.Encode(rsa.Modulus),
        };
        var cert = new { kid = AccessTokens.Key.KeyId, cert = "-----BEGIN CERTIFICATE-----\n..." };
        return JsonSerializer.Serialize(
            new { keys = new[] { key }, public_cert = cert, public_certs = new[] { cert } });
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
