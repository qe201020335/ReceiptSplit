using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using ReceiptSplit.Testing;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace ReceiptSplit.Accounts.Tests;

public class AccountSettingsCheckTests
{
    [Fact]
    public void Fails_when_a_development_user_is_set_outside_development()
    {
        var logger = new ListLogger<AccountSettingsCheck>();

        Assert.False(Check(Environments.Production, devUser: "dev@example.com", logger: logger).Run());
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Critical && e.Message.Contains("Auth:DevUser"));
    }

    [Fact]
    public void Passes_with_a_development_user_in_development()
    {
        Assert.True(Check(Environments.Development, devUser: "dev@example.com").Run());
    }

    [Fact]
    public void Passes_with_admins_and_no_development_user()
    {
        var logger = new ListLogger<AccountSettingsCheck>();

        Assert.True(Check(Environments.Production, logger: logger).Run());
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void Warns_but_passes_without_admins()
    {
        var logger = new ListLogger<AccountSettingsCheck>();

        Assert.True(Check(Environments.Production, admins: [], logger: logger).Run());
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("Auth:Admins"));
    }

    private static AccountSettingsCheck Check(
        string environment,
        string? devUser = null,
        List<string>? admins = null,
        ListLogger<AccountSettingsCheck>? logger = null) => new(
        MsOptions.Create(new AuthOptions { DevUser = devUser, Admins = admins ?? ["admin@example.com"] }),
        new HostingEnvironment { EnvironmentName = environment },
        logger ?? new ListLogger<AccountSettingsCheck>());
}
