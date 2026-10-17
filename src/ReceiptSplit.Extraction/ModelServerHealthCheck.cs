using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ReceiptSplit.Extraction;

/// <summary>
/// Reports the monitor's last result without contacting the model server, so requests to the health endpoint,
/// which needs no sign-in, never add load to the shared server.
/// </summary>
internal sealed class ModelServerHealthCheck(ModelServerState state) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var current = state.Current;
        return Task.FromResult(current.Status switch
        {
            ModelServerStatus.Available => HealthCheckResult.Healthy(current.Reason),
            ModelServerStatus.Unavailable => HealthCheckResult.Degraded(current.Reason),
            _ => HealthCheckResult.Unhealthy(current.Reason),
        });
    }
}
