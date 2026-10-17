using Microsoft.Extensions.Logging;
using ReceiptSplit.Testing;

namespace ReceiptSplit.Extraction.Tests;

public class ModelServerStateTests
{
    private static readonly ModelServerCheckResult Available = new(ModelServerStatus.Available, "offers the model");

    private static readonly ModelServerCheckResult Down = new(ModelServerStatus.Unavailable, "Connection refused");

    private static readonly ModelServerCheckResult KeyRejected = new(ModelServerStatus.Misconfigured, "returned 401");

    private readonly ListLogger<ModelServerState> _logger = new();

    [Fact]
    public void The_startup_result_is_the_first_state_and_isnt_logged_again()
    {
        var state = new ModelServerState(_logger);

        state.Seed(Down);

        Assert.Equal(Down, state.Current);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public void An_outage_logs_one_warning_however_many_checks_see_it()
    {
        var state = Seeded(Available);

        state.Update(Down);
        state.Update(Down);
        state.Update(Down);

        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("Connection refused", entry.Message);
        Assert.Equal(ModelServerStatus.Unavailable, state.Current.Status);
    }

    [Fact]
    public void Coming_back_logs_one_information_line()
    {
        var state = Seeded(Down);

        state.Update(Available);
        state.Update(Available);

        Assert.Equal(LogLevel.Information, Assert.Single(_logger.Entries).Level);
    }

    [Fact]
    public void Settings_that_stop_working_log_an_error()
    {
        var state = Seeded(Available);

        state.Update(KeyRejected);

        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains("returned 401", entry.Message);
    }

    [Fact]
    public void A_new_reason_for_the_same_status_isnt_logged_but_is_kept()
    {
        var state = Seeded(Down);
        var timedOut = new ModelServerCheckResult(ModelServerStatus.Unavailable, "no answer within 30 s");

        state.Update(timedOut);

        Assert.Empty(_logger.Entries);
        Assert.Equal(timedOut, state.Current);
    }

    private ModelServerState Seeded(ModelServerCheckResult result)
    {
        var state = new ModelServerState(_logger);
        state.Seed(result);
        return state;
    }
}
