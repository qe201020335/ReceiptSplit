using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ReceiptSplit.Extraction;

/// <summary>
/// Checks the model server every <see cref="LlmOptions.MonitorInterval"/> while the app runs, so its state follows
/// the server. The first check comes one interval after the startup check, whose result is the first state. A
/// result never stops the app, whatever it is.
/// </summary>
internal sealed class ModelServerMonitor(
    ModelServerCheck check,
    ModelServerState state,
    IOptions<LlmOptions> options,
    TimeProvider time,
    ILogger<ModelServerMonitor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.MonitorInterval, time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                state.Update(await check.RunAsync(stoppingToken));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // An exception escaping a background service stops the host, which the monitor must never do.
                logger.LogError(ex, "Checking the model server failed unexpectedly");
            }
        }
    }
}
