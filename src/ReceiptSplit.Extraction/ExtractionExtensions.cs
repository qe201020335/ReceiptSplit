using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ReceiptSplit.Extraction;

public static class ExtractionExtensions
{
    /// <summary>
    /// Registers the model server settings, the model client, its startup check and the monitor that keeps checking
    /// it. Libraries that read receipts call this too, so a second call does nothing.
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
        builder.Services.AddSingleton<ModelServerCheck>();
        builder.Services.AddSingleton<ModelServerState>();
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddHostedService<ModelServerMonitor>();
        return builder;
    }

    /// <summary>
    /// Checks the model server, logs the result and makes it the monitor's first state. Returns false only when its
    /// settings are wrong, so a deploy with a wrong API key, URL or model stops instead of failing every receipt,
    /// while a server that is only down doesn't take the rest of the app down with it.
    /// </summary>
    public static async Task<bool> CheckModelServerAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        var result = await services.GetRequiredService<ModelServerCheck>().RunAsync(cancellationToken);
        services.GetRequiredService<ModelServerState>().Seed(result);
        var logger = services.GetRequiredService<ILogger<ModelServerCheck>>();
        switch (result.Status)
        {
            case ModelServerStatus.Misconfigured:
                logger.LogCritical("{Reason}", result.Reason);
                return false;
            case ModelServerStatus.Unavailable:
                logger.LogWarning(
                    "Starting without the model server, so receipts can't be read until it answers. {Reason}",
                    result.Reason);
                return true;
            default:
                logger.LogInformation("{Reason}", result.Reason);
                return true;
        }
    }

    private sealed class ExtractionMarker;
}
