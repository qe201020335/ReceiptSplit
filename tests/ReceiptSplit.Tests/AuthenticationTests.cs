using System.Net;
using System.Net.Http.Headers;
using ImageMagick;
using Microsoft.AspNetCore.Hosting;
using ReceiptSplit.Testing;
using ReceiptSplit.Tests.Support;

namespace ReceiptSplit.Tests;

public class AuthenticationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_valid_access_token_signs_the_request_in()
    {
        await using var factory = new ReceiptApiFactory();
        using var client = factory.CreateClientFor("someone@example.com");

        using var response = await client.GetAsync("/api/receipts", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Requests_without_a_token_are_refused_and_change_nothing()
    {
        await using var factory = new ReceiptApiFactory();
        using var anonymous = factory.CreateClientWithToken(null);

        using var list = await anonymous.GetAsync("/api/receipts", Ct);
        using var upload = await anonymous.PostAsync("/api/receipts", Photo(), Ct);

        Assert.All([list, upload], r => Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode));
        Assert.Empty(Directory.GetFiles(Path.Combine(factory.StorageRoot, "uploads")));
    }

    [Fact]
    public async Task The_plain_email_header_is_not_trusted()
    {
        await using var factory = new ReceiptApiFactory();
        using var forged = factory.CreateClientWithToken(null);
        forged.DefaultRequestHeaders.Add("Cf-Access-Authenticated-User-Email", ReceiptApiFactory.AdminEmail);

        using var response = await forged.GetAsync("/api/receipts", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Only_the_access_header_carries_the_token()
    {
        await using var factory = new ReceiptApiFactory();
        using var client = factory.CreateClientWithToken(null);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", AccessTokens.Create(ReceiptApiFactory.MemberEmail));

        using var response = await client.GetAsync("/api/receipts", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private const string Email = ReceiptApiFactory.MemberEmail;

    public static TheoryData<string, string> FailingTokens => new()
    {
        { "another key with the same id", AccessTokens.Create(Email, key: AccessTokens.NewKey()) },
        { "an unknown key", AccessTokens.Create(Email, key: AccessTokens.NewKey("other-key")) },
        { "another audience", AccessTokens.Create(Email, audience: "another-application") },
        { "another issuer", AccessTokens.Create(Email, issuer: "https://other.cloudflareaccess.com") },
        { "expired", AccessTokens.Create(Email, expires: DateTime.UtcNow.AddMinutes(-10)) },
        { "not a token", "not.a.token" },
    };

    [Theory]
    [MemberData(nameof(FailingTokens))]
    public async Task Tokens_that_fail_a_check_are_refused(string reason, string token)
    {
        await using var factory = new ReceiptApiFactory();
        using var client = factory.CreateClientWithToken(token);

        using var response = await client.GetAsync("/api/receipts", Ct);

        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"A token with {reason} was accepted.");
    }

    [Fact]
    public async Task The_development_user_is_signed_in_without_a_token()
    {
        await using var factory = new ReceiptApiFactory();
        await using var development = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Auth:DevUser", "dev@example.com"));
        using var client = development.CreateClient();
        client.DefaultRequestHeaders.Remove(AccessTokens.Header);

        using var response = await client.GetAsync("/api/receipts", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_page_loads_without_a_token()
    {
        // The client isn't built for tests, so the page comes from a web root holding only index.html.
        await using var factory = new ReceiptApiFactory();
        var webRoot = Directory.CreateDirectory(Path.Combine(factory.StorageRoot, "wwwroot")).FullName;
        await File.WriteAllTextAsync(
            Path.Combine(webRoot, "index.html"), "<!doctype html><title>ReceiptSplit</title>", Ct);
        await using var withPage = factory.WithWebHostBuilder(builder => builder.UseWebRoot(webRoot));
        using var anonymous = withPage.CreateClient();
        anonymous.DefaultRequestHeaders.Remove(AccessTokens.Header);

        foreach (var path in new[] { "/", $"/receipts/{Guid.CreateVersion7()}" })
        {
            using var response = await anonymous.GetAsync(path, Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("<title>ReceiptSplit</title>", await response.Content.ReadAsStringAsync(Ct));
        }
    }

    private static MultipartFormDataContent Photo()
    {
        var file = new ByteArrayContent(TestImages.Create(64, 48, MagickFormat.Jpeg));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return new MultipartFormDataContent { { file, "file", "receipt.jpg" } };
    }
}
