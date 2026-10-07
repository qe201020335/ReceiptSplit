using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ReceiptSplit.Extraction;

public static class ExtractionExtensions
{
    /// <summary>
    /// Registers the model server settings, the model client and its startup check. Libraries that read receipts
    /// call this too, so a second call does nothing.
    /// </summary>
    public static IHostApplicationBuilder AddExtraction(this IHostApplicationBuilder builder)
    {
        if (builder.Services.Any(s => s.ServiceType == typeof(ExtractionMarker)))
        {
            return builder;
        }

        builder.Services.AddSingleton<ExtractionMarker>();
        builder.Services.AddOptions<LlmOptions>()
            .Bind(builder.Configuration.GetSection(LlmOptions.SectionName));
        builder.Services.AddHttpClient<ILlamaClient, LlamaClient>((sp, http) =>
        {
            var llm = sp.GetRequiredService<IOptions<LlmOptions>>().Value;
            http.BaseAddress = new Uri(llm.BaseUrl.TrimEnd('/') + "/");
            http.Timeout = llm.Timeout;
            if (!string.IsNullOrEmpty(llm.ApiKey))
            {
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", llm.ApiKey);
            }
        });
        builder.Services.AddScoped<ModelServerCheck>();
        return builder;
    }

    /// <summary>
    /// Checks that the model server can be reached and offers the configured model, logging why when it can't.
    /// Run before the app starts, so a model server that can't be used stops a deploy instead of failing every
    /// receipt.
    /// </summary>
    public static async Task<bool> CheckModelServerAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ModelServerCheck>().RunAsync(cancellationToken);
    }

    private sealed class ExtractionMarker;
}
