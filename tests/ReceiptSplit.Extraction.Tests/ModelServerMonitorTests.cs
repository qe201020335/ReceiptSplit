using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ReceiptSplit.Testing;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace ReceiptSplit.Extraction.Tests;

public class ModelServerMonitorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakeLlamaClient _llama = new();

    private readonly ModelServerState _state = new(NullLogger<ModelServerState>.Instance);

    [Fact]
    public async Task The_state_follows_the_server_going_down_and_coming_back()
    {
        _state.Seed(new(ModelServerStatus.Available, "offers the model"));
        using var monitor = Monitor(TimeSpan.FromMilliseconds(50));
        await monitor.StartAsync(Ct);

        _llama.ListFailure = new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused");
        await WaitForStatusAsync(ModelServerStatus.Unavailable);
        _llama.ListFailure = null;
        await WaitForStatusAsync(ModelServerStatus.Available);

        await monitor.StopAsync(Ct);
    }

    [Fact]
    public async Task The_first_check_waits_one_interval_after_the_startup_check()
    {
        _state.Seed(new(ModelServerStatus.Unavailable, "Connection refused"));
        using var monitor = Monitor(TimeSpan.FromHours(1));

        await monitor.StartAsync(Ct);
        await Task.Delay(200, Ct);
        await monitor.StopAsync(Ct);

        Assert.Equal(0, _llama.ListCalls);
        Assert.Equal(ModelServerStatus.Unavailable, _state.Current.Status);
    }

    [Fact]
    public async Task An_unexpected_failure_doesnt_stop_the_monitor()
    {
        _state.Seed(new(ModelServerStatus.Available, "offers the model"));
        _llama.ListFailure = new InvalidOperationException("something unexpected");
        using var monitor = Monitor(TimeSpan.FromMilliseconds(50));
        await monitor.StartAsync(Ct);

        for (var attempt = 0; _llama.ListCalls < 2 && attempt < 200; attempt++)
        {
            await Task.Delay(50, Ct);
        }

        _llama.ListFailure = new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused");
        await WaitForStatusAsync(ModelServerStatus.Unavailable);
        await monitor.StopAsync(Ct);
    }

    private ModelServerMonitor Monitor(TimeSpan interval)
    {
        var options = MsOptions.Create(new LlmOptions
        {
            Model = FakeLlamaClient.ModelName,
            BaseUrl = "http://llama.test",
            MonitorInterval = interval,
        });
        var scopes = new ServiceCollection().AddSingleton<ILlamaClient>(_llama).BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();
        return new ModelServerMonitor(
            new ModelServerCheck(scopes, options), _state, options, TimeProvider.System,
            NullLogger<ModelServerMonitor>.Instance);
    }

    private async Task WaitForStatusAsync(ModelServerStatus status)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (_state.Current.Status == status)
            {
                return;
            }

            await Task.Delay(50, Ct);
        }

        throw new TimeoutException($"The model server state didn't become {status} within 10 seconds.");
    }
}
