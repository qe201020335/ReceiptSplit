using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using ReceiptSplit.Tests.Support;

namespace ReceiptSplit.Tests;

public class HealthTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_available_model_server_is_healthy()
    {
        await using var factory = new ReceiptApiFactory();
        using var client = factory.CreateClient();

        await AssertHealthAsync(client, HttpStatusCode.OK, "Healthy");
    }

    [Fact]
    public async Task A_model_server_that_was_down_at_startup_is_degraded_from_the_first_request()
    {
        await using var factory = new ReceiptApiFactory();
        factory.Llm.ListFailure = new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused");
        using var client = factory.CreateClient();

        await AssertHealthAsync(client, HttpStatusCode.ServiceUnavailable, "Degraded");
    }

    [Fact]
    public async Task Settings_that_stop_working_after_a_good_start_are_unhealthy()
    {
        await using var factory = new ReceiptApiFactory();
        await using var monitored = WithMonitorInterval(factory, TimeSpan.FromMilliseconds(50));
        using var client = monitored.CreateClient();
        factory.Llm.ListFailure = new HttpRequestException(
            "llama-server returned 401: Invalid API Key", null, HttpStatusCode.Unauthorized);

        await WaitForHealthAsync(client, "Unhealthy");

        await AssertHealthAsync(client, HttpStatusCode.ServiceUnavailable, "Unhealthy");
        using var receipts = await client.GetAsync("/api/receipts", Ct);
        Assert.Equal(HttpStatusCode.OK, receipts.StatusCode);
    }

    [Fact]
    public async Task Health_follows_the_model_server_coming_back()
    {
        await using var factory = new ReceiptApiFactory();
        factory.Llm.ListFailure = new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused");
        await using var monitored = WithMonitorInterval(factory, TimeSpan.FromMilliseconds(50));
        using var client = monitored.CreateClient();
        await AssertHealthAsync(client, HttpStatusCode.ServiceUnavailable, "Degraded");

        factory.Llm.ListFailure = null;

        await WaitForHealthAsync(client, "Healthy");
    }

    [Fact]
    public async Task Health_needs_no_access_token()
    {
        await using var factory = new ReceiptApiFactory();
        using var anonymous = factory.CreateClientWithToken(null);

        await AssertHealthAsync(anonymous, HttpStatusCode.OK, "Healthy");
    }

    [Fact]
    public async Task Health_requests_dont_reach_the_model_server()
    {
        await using var factory = new ReceiptApiFactory();
        using var client = factory.CreateClient();
        var before = factory.Llm.ListCalls;

        for (var i = 0; i < 10; i++)
        {
            using var response = await client.GetAsync("/health", Ct);
        }

        Assert.Equal(before, factory.Llm.ListCalls);
    }

    private static WebApplicationFactory<Program> WithMonitorInterval(ReceiptApiFactory factory, TimeSpan interval) =>
        factory.WithWebHostBuilder(b => b.UseSetting("Llm:MonitorInterval", interval.ToString()));

    /// <summary>The body is the status word alone: no URL, model name or error text.</summary>
    private static async Task AssertHealthAsync(HttpClient client, HttpStatusCode status, string body)
    {
        using var response = await client.GetAsync("/health", Ct);

        Assert.Equal(status, response.StatusCode);
        Assert.Equal(body, await response.Content.ReadAsStringAsync(Ct));
    }

    private static async Task WaitForHealthAsync(HttpClient client, string body)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            using var response = await client.GetAsync("/health", Ct);
            if (await response.Content.ReadAsStringAsync(Ct) == body)
            {
                return;
            }

            await Task.Delay(50, Ct);
        }

        throw new TimeoutException($"/health didn't answer {body} within 10 seconds.");
    }
}
