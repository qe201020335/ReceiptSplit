using System.Net;
using Microsoft.AspNetCore.Hosting;
using ReceiptSplit.Tests.Support;

namespace ReceiptSplit.Tests;

public class StartupTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_app_doesnt_start_when_the_model_isnt_offered()
    {
        await using var factory = new ReceiptApiFactory();
        factory.Llm.Models.Clear();

        Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
    }

    [Fact]
    public async Task The_app_doesnt_start_when_the_model_server_rejects_the_api_key()
    {
        await using var factory = new ReceiptApiFactory();
        factory.Llm.ListFailure = new HttpRequestException(
            "llama-server returned 401: Invalid API Key", null, HttpStatusCode.Unauthorized);

        Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
    }

    [Fact]
    public async Task The_app_doesnt_start_with_a_malformed_model_server_url()
    {
        await using var factory = new ReceiptApiFactory();
        await using var malformed = factory.WithWebHostBuilder(b => b.UseSetting("Llm:BaseUrl", "not a url"));

        Assert.Throws<InvalidOperationException>(() => malformed.CreateClient());
    }

    [Fact]
    public async Task The_app_starts_while_the_model_server_is_down()
    {
        await using var factory = new ReceiptApiFactory();
        factory.Llm.ListFailure = new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/receipts", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_app_starts_while_the_model_server_loads_a_model()
    {
        await using var factory = new ReceiptApiFactory();
        factory.Llm.ListFailure = new HttpRequestException(
            "llama-server returned 503: Loading model", null, HttpStatusCode.ServiceUnavailable);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/receipts", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
