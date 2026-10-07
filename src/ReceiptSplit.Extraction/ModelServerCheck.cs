using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ReceiptSplit.Extraction;

/// <summary>
/// Checks at startup that the model server can be reached, accepts the API key and offers the configured model.
/// Without it those mistakes only show up as failed receipts after someone uploads a photo.
/// </summary>
public sealed class ModelServerCheck(ILlamaClient llama, IOptions<LlmOptions> options, ILogger<ModelServerCheck> logger)
{
    /// <summary>Listing models doesn't load one, so it answers quickly even when the server is busy.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>Logs why the model server can't be used and returns false, or returns true when it can.</summary>
    public async Task<bool> RunAsync(CancellationToken cancellationToken)
    {
        var llm = options.Value;
        IReadOnlyList<string> models;
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeout.CancelAfter(Timeout);
            try
            {
                models = await llama.ListModelsAsync(timeout.Token);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException
                || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                var error = ex is OperationCanceledException
                    ? $"no answer within {Timeout.TotalSeconds} s"
                    : ex.Message;
                logger.LogCritical(
                    "Couldn't list the models on the model server at {BaseUrl}: {Error}", llm.BaseUrl, error);
                return false;
            }
        }

        if (!models.Contains(llm.Model))
        {
            logger.LogCritical(
                "The model server at {BaseUrl} doesn't offer the model {Model}. It offers: {Models}",
                llm.BaseUrl, llm.Model, models);
            return false;
        }

        logger.LogInformation("The model server at {BaseUrl} offers the model {Model}", llm.BaseUrl, llm.Model);
        return true;
    }
}
