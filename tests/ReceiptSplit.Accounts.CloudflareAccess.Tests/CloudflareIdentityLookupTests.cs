using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using ReceiptSplit.Data;
using ReceiptSplit.Testing;

namespace ReceiptSplit.Accounts.CloudflareAccess.Tests;

public class CloudflareIdentityLookupTests
{
    /// <summary>Shaped like get-identity's answer, with made-up values and the session data it also returns.</summary>
    private const string Identity = """
        {
          "id": "104738572036598370000",
          "name": "Pat Example",
          "email": "pat@example.com",
          "amr": ["pwd"],
          "idp": {"id": "5e6f7a8b-0000-4000-8000-000000000000", "type": "google"},
          "geo": {"country": "CA"},
          "ip": "203.0.113.7",
          "user_uuid": "1a2b3c4d-0000-4000-8000-000000000000",
          "device_sessions": {}
        }
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("google")]
    [InlineData("google-apps")]
    public async Task Google_accounts_are_found_by_their_account_id(string type)
    {
        var (result, handler, _) = await LookupAsync(HttpStatusCode.OK, Identity.Replace("\"google\"", $"\"{type}\""));

        Assert.Equal(IdentityLookupStatus.Found, result.Status);
        Assert.Equal(
            new ProviderIdentity(IdentityProvider.Google, "104738572036598370000", "Pat Example"), result.Identity);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://test.cloudflareaccess.com/cdn-cgi/access/get-identity", request.Uri.ToString());
        Assert.Equal("CF_Authorization=the.access.token", request.Cookie);
    }

    [Fact]
    public async Task Other_sign_in_methods_are_unsupported()
    {
        var (result, _, _) = await LookupAsync(HttpStatusCode.OK, Identity.Replace("\"google\"", "\"github\""));

        Assert.Equal(IdentityLookupResult.Unsupported, result);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, Identity)]
    [InlineData(HttpStatusCode.OK, "{\"ip\": \"203.0.113.7\", ")]
    [InlineData(HttpStatusCode.OK, "{\"idp\": {\"type\": \"google\"}, \"ip\": \"203.0.113.7\"}")]
    [InlineData(HttpStatusCode.OK, "null")]
    [InlineData(HttpStatusCode.OK, "{\"id\": \"104738572036598370000\", \"ip\": \"203.0.113.7\"}")]
    public async Task Failures_are_unavailable_and_dont_log_the_response(HttpStatusCode status, string body)
    {
        var (result, _, logger) = await LookupAsync(status, body);

        Assert.Equal(IdentityLookupResult.Unavailable, result);
        Assert.NotEmpty(logger.Entries);
        Assert.DoesNotContain(logger.Entries, e => e.Message.Contains("203.0.113.7"));
    }

    [Fact]
    public async Task An_unreachable_access_is_unavailable()
    {
        var (result, _, _) = await LookupAsync(new HttpRequestException("Connection refused"));

        Assert.Equal(IdentityLookupResult.Unavailable, result);
    }

    private static async Task<(IdentityLookupResult, StubHandler, ListLogger<CloudflareIdentityLookup>)> LookupAsync(
        HttpStatusCode status, string body) => await LookupAsync(new StubHandler(status, body, null));

    private static async Task<(IdentityLookupResult, StubHandler, ListLogger<CloudflareIdentityLookup>)> LookupAsync(
        HttpRequestException failure) => await LookupAsync(new StubHandler(HttpStatusCode.OK, "", failure));

    private static async Task<(IdentityLookupResult, StubHandler, ListLogger<CloudflareIdentityLookup>)> LookupAsync(
        StubHandler handler)
    {
        using var http = new HttpClient(handler) { BaseAddress = new Uri($"{AccessTokens.Issuer}/") };
        var logger = new ListLogger<CloudflareIdentityLookup>();
        var context = new DefaultHttpContext();
        context.Request.Headers[AccessTokens.Header] = "the.access.token";

        var result = await new CloudflareIdentityLookup(http, logger).LookupAsync(context, Ct);
        return (result, handler, logger);
    }

    private sealed class StubHandler(HttpStatusCode status, string body, Exception? failure) : HttpMessageHandler
    {
        public List<(Uri Uri, string Cookie)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!, string.Join("; ", request.Headers.GetValues("Cookie"))));
            return failure is not null
                ? Task.FromException<HttpResponseMessage>(failure)
                : Task.FromResult(new HttpResponseMessage(status)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                });
        }
    }
}
