using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ReceiptSplit.Testing;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace ReceiptSplit.Extraction.Tests;

public class ModelServerCheckTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Passes_when_the_server_offers_the_configured_model()
    {
        var llama = new FakeLlamaClient { Models = { "qwen3.8-27b-q4-mtp" } };

        var result = await Check(llama, "qwen3.8-27b-q4-mtp").RunAsync(Ct);

        Assert.Equal(ModelServerStatus.Available, result.Status);
    }

    [Fact]
    public async Task A_model_the_server_doesnt_offer_is_a_wrong_setting()
    {
        var llama = new FakeLlamaClient { Models = { "qwen3.8-27b" } };

        var result = await Check(llama, "qwen3.8-27b-q4-mtp").RunAsync(Ct);

        Assert.Equal(ModelServerStatus.Misconfigured, result.Status);
        Assert.Contains("qwen3.8-27b", result.Reason);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Answers_that_come_and_go_mean_the_server_is_unavailable(HttpStatusCode status)
    {
        var result = await Check(FailingWith(Returned(status))).RunAsync(Ct);

        Assert.Equal(ModelServerStatus.Unavailable, result.Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.MethodNotAllowed)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task Other_failing_answers_are_wrong_settings(HttpStatusCode status)
    {
        var result = await Check(FailingWith(Returned(status))).RunAsync(Ct);

        Assert.Equal(ModelServerStatus.Misconfigured, result.Status);
    }

    [Theory]
    [InlineData(HttpRequestError.ConnectionError)]
    [InlineData(HttpRequestError.NameResolutionError)]
    [InlineData(HttpRequestError.ResponseEnded)]
    [InlineData(HttpRequestError.Unknown)]
    [InlineData(HttpRequestError.ProxyTunnelError)]
    public async Task Network_errors_mean_the_server_is_unavailable(HttpRequestError error)
    {
        var result = await Check(FailingWith(new HttpRequestException(error, "network error"))).RunAsync(Ct);

        Assert.Equal(ModelServerStatus.Unavailable, result.Status);
    }

    [Theory]
    [InlineData(HttpRequestError.SecureConnectionError)]
    [InlineData(HttpRequestError.HttpProtocolError)]
    [InlineData(HttpRequestError.InvalidResponse)]
    public async Task Tls_failures_and_answers_that_arent_http_are_wrong_settings(HttpRequestError error)
    {
        var result = await Check(FailingWith(new HttpRequestException(error, "not http"))).RunAsync(Ct);

        Assert.Equal(ModelServerStatus.Misconfigured, result.Status);
    }

    [Fact]
    public async Task No_answer_in_time_means_the_server_is_unavailable()
    {
        var result = await Check(FailingWith(new TaskCanceledException())).RunAsync(Ct);

        Assert.Equal(ModelServerStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task An_answer_that_isnt_a_model_list_is_a_wrong_setting()
    {
        var result = await Check(FailingWith(new JsonException())).RunAsync(Ct);

        Assert.Equal(ModelServerStatus.Misconfigured, result.Status);
    }

    [Fact]
    public async Task A_web_page_in_place_of_the_model_list_is_a_wrong_setting()
    {
        using var http = new HttpClient(new StubHandler("<html><body>Router login</body></html>", "text/html"))
        {
            BaseAddress = new Uri("http://llama.test/"),
        };

        var result = await Check(new LlamaClient(http, MsOptions.Create(new LlmOptions()))).RunAsync(Ct);

        Assert.Equal(ModelServerStatus.Misconfigured, result.Status);
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("localhost:8000")]
    [InlineData("ftp://llama.test")]
    [InlineData("/v1")]
    public async Task A_base_url_that_isnt_http_is_a_wrong_setting_found_without_calling_the_server(string baseUrl)
    {
        var llama = new FakeLlamaClient();

        var result = await Check(llama, baseUrl: baseUrl).RunAsync(Ct);

        Assert.Equal(ModelServerStatus.Misconfigured, result.Status);
        Assert.Contains("Llm:BaseUrl", result.Reason);
    }

    [Fact]
    public async Task A_malformed_base_url_stops_the_start_without_throwing()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration["Llm:BaseUrl"] = "not a url";
        builder.AddExtraction();
        using var host = builder.Build();

        Assert.False(await host.Services.CheckModelServerAsync(Ct));
    }

    [Fact]
    public async Task Lists_each_models_id_and_aliases_from_a_router_mode_server()
    {
        // Trimmed from llama-server's /v1/models in router mode.
        const string body = """
            {"object": "list", "data": [
              {"id": "unsloth/Qwen3.8-27B-GGUF:Q4_0", "aliases": [], "status": {"value": "unloaded"}},
              {"id": "qwen3.8-27b-q4-mtp", "aliases": ["qwen3.8-27b-q4-mtp", "receipts"]}
            ]}
            """;
        using var http = new HttpClient(new StubHandler(body)) { BaseAddress = new Uri("http://llama.test/") };
        var client = new LlamaClient(http, MsOptions.Create(new LlmOptions()));

        var models = await client.ListModelsAsync(Ct);

        Assert.Equal(["unsloth/Qwen3.8-27B-GGUF:Q4_0", "qwen3.8-27b-q4-mtp", "receipts"], models);
    }

    private static FakeLlamaClient FailingWith(Exception failure) => new() { ListFailure = failure };

    /// <summary>What <see cref="LlamaClient"/> throws for a failing answer.</summary>
    private static HttpRequestException Returned(HttpStatusCode status) =>
        new($"llama-server returned {(int)status}: error", null, status);

    private static ModelServerCheck Check(
        ILlamaClient llama,
        string model = FakeLlamaClient.ModelName,
        string baseUrl = "http://llama.test") => new(
        new ServiceCollection().AddSingleton(llama).BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
        MsOptions.Create(new LlmOptions { Model = model, BaseUrl = baseUrl }));

    private sealed class StubHandler(string body, string mediaType = "application/json") : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("/v1/models", request.RequestUri?.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, mediaType),
            });
        }
    }
}
