using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using ReceiptSplit.Extraction;
using ReceiptSplit.Options;
using ReceiptSplit.Testing;
using ReceiptSplit.Tests.Support;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace ReceiptSplit.Tests;

public class ModelServerCheckTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Passes_when_the_server_offers_the_configured_model()
    {
        var llama = new FakeLlamaClient { Models = { "qwen3.8-27b-q4-mtp" } };

        Assert.True(await Check(llama, "qwen3.8-27b-q4-mtp").RunAsync(Ct));
    }

    [Fact]
    public async Task Fails_when_the_server_doesnt_offer_the_configured_model()
    {
        var llama = new FakeLlamaClient { Models = { "qwen3.8-27b" } };

        Assert.False(await Check(llama, "qwen3.8-27b-q4-mtp").RunAsync(Ct));
    }

    [Fact]
    public async Task Fails_when_the_server_rejects_the_request()
    {
        var llama = new FakeLlamaClient
        {
            ListFailure = new HttpRequestException(
                "llama-server returned 401: Invalid API Key", null, HttpStatusCode.Unauthorized),
        };

        Assert.False(await Check(llama, FakeLlamaClient.ModelName).RunAsync(Ct));
    }

    [Fact]
    public async Task The_app_doesnt_start_when_the_check_fails()
    {
        await using var factory = new ReceiptApiFactory();
        factory.Llm.Models.Clear();

        Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
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

    private static ModelServerCheck Check(FakeLlamaClient llama, string model) => new(
        llama,
        MsOptions.Create(new LlmOptions { Model = model }),
        NullLogger<ModelServerCheck>.Instance);

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("/v1/models", request.RequestUri?.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
