using Microsoft.Extensions.Logging;

namespace ReceiptSplit.Extraction;

/// <summary>
/// The latest model server check, which the health check reports. The startup check's result is the first one, so
/// it is known before the app serves its first request.
/// </summary>
internal sealed class ModelServerState(ILogger<ModelServerState> logger)
{
    private volatile ModelServerCheckResult _current =
        new(ModelServerStatus.Unavailable, "The model server hasn't been checked yet");

    public ModelServerCheckResult Current => _current;

    /// <summary>Stores the startup check's result, which the startup check logs at its own levels.</summary>
    public void Seed(ModelServerCheckResult result) => _current = result;

    /// <summary>
    /// Stores a later check's result, logging only when the status changes, so an outage logs once rather than on
    /// every check. A different reason within the same status isn't a change.
    /// </summary>
    public void Update(ModelServerCheckResult result)
    {
        var previous = _current;
        _current = result;
        if (result.Status == previous.Status)
        {
            return;
        }

        switch (result.Status)
        {
            case ModelServerStatus.Unavailable:
                logger.LogWarning(
                    "The model server is unavailable, so receipts can't be read until it answers. {Reason}",
                    result.Reason);
                break;
            case ModelServerStatus.Misconfigured:
                logger.LogError(
                    "The model server settings stopped working, so receipts can't be read. {Reason}", result.Reason);
                break;
            default:
                logger.LogInformation("The model server answers again. {Reason}", result.Reason);
                break;
        }
    }
}
