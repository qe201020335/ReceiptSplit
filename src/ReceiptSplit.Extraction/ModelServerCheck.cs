using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ReceiptSplit.Extraction;

internal enum ModelServerStatus
{
    Available,

    /// <summary>Down, restarting, loading a model or not answering: waiting may fix it.</summary>
    Unavailable,

    /// <summary>The settings are wrong (API key, base URL, model), so waiting won't fix it.</summary>
    Misconfigured,
}

/// <summary>A check's outcome, with the reason logged for it.</summary>
internal sealed record ModelServerCheckResult(ModelServerStatus Status, string Reason);

/// <summary>
/// Checks that the model server can be reached, accepts the API key and offers the configured model, and tells a
/// server that is only unavailable from settings that are wrong. Without it those mistakes only show up as failed
/// receipts after someone uploads a photo.
/// </summary>
internal sealed class ModelServerCheck(IServiceScopeFactory scopes, IOptions<LlmOptions> options)
{
    /// <summary>Listing models doesn't load one, so it answers quickly even when the server is busy.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public async Task<ModelServerCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        var llm = options.Value;
        // Checked before the client is resolved, since configuring it parses the URL and would throw. A value like
        // localhost:8000 parses as a URL whose scheme is localhost, so the scheme is checked too.
        if (!Uri.TryCreate(llm.BaseUrl, UriKind.Absolute, out var baseUrl)
            || baseUrl.Scheme is not ("http" or "https"))
        {
            return new(ModelServerStatus.Misconfigured,
                $"Llm:BaseUrl \"{llm.BaseUrl}\" isn't an absolute http or https URL");
        }

        IReadOnlyList<string> models;
        await using (var scope = scopes.CreateAsyncScope())
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeout.CancelAfter(Timeout);
            try
            {
                models = await scope.ServiceProvider.GetRequiredService<ILlamaClient>().ListModelsAsync(timeout.Token);
            }
            catch (HttpRequestException ex)
            {
                return new(Classify(ex), CouldntList(llm, ex.Message));
            }
            catch (JsonException)
            {
                return new(ModelServerStatus.Misconfigured,
                    CouldntList(llm, "the answer isn't a model list, so it may not be llama-server"));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new(ModelServerStatus.Unavailable,
                    CouldntList(llm, $"no answer within {Timeout.TotalSeconds} s"));
            }
        }

        return models.Contains(llm.Model)
            ? new(ModelServerStatus.Available, $"The model server at {llm.BaseUrl} offers the model {llm.Model}")
            : new(ModelServerStatus.Misconfigured,
                $"The model server at {llm.BaseUrl} doesn't offer the model {llm.Model}. "
                + $"It offers: {string.Join(", ", models)}");
    }

    /// <summary>
    /// Answers that come and go are Unavailable: 5xx (llama-server answers 503 while it loads a model), 408 and 429.
    /// Any other status means the URL or the API key is wrong. Without a status, a TLS failure or something that
    /// isn't HTTP means the URL is wrong; every other network error, including DNS, counts as an outage, so a
    /// hiccup doesn't stop a deploy.
    /// </summary>
    private static ModelServerStatus Classify(HttpRequestException exception) => exception switch
    {
        { StatusCode: { } status } => (int)status >= 500 || status is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            ? ModelServerStatus.Unavailable
            : ModelServerStatus.Misconfigured,
        { HttpRequestError: HttpRequestError.SecureConnectionError or HttpRequestError.HttpProtocolError
            or HttpRequestError.InvalidResponse } => ModelServerStatus.Misconfigured,
        _ => ModelServerStatus.Unavailable,
    };

    private static string CouldntList(LlmOptions llm, string error) =>
        $"Couldn't list the models on the model server at {llm.BaseUrl}: {error}";
}
