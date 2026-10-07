using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ReceiptSplit.Data;

namespace ReceiptSplit.Accounts;

public static class AccountsExtensions
{
    /// <summary>
    /// Registers the account settings, development sign-in and the authorization policies, along with the database.
    /// The identity-aware proxy's library calls this and adds its own scheme. A second call does nothing.
    /// </summary>
    public static IHostApplicationBuilder AddAccounts(this IHostApplicationBuilder builder)
    {
        if (builder.Services.Any(s => s.ServiceType == typeof(AccountsMarker)))
        {
            return builder;
        }

        builder.AddDatabase();

        builder.Services.AddSingleton<AccountsMarker>();
        builder.Services.AddOptions<AuthOptions>()
            .Bind(builder.Configuration.GetSection(AuthOptions.SectionName));

        builder.Services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, DevelopmentAuthenticationHandler>(
                DevelopmentAuthenticationHandler.SchemeName, null);
        // Decided when the options are first read, after tests have changed the settings. Registered before the
        // proxy's library sets its own scheme, which only fills in a default left empty here.
        builder.Services.AddOptions<AuthenticationOptions>()
            .Configure<IOptions<AuthOptions>, IHostEnvironment>((options, auth, environment) =>
            {
                if (auth.Value.SignsInDevUser(environment))
                {
                    options.DefaultScheme = DevelopmentAuthenticationHandler.SchemeName;
                }
            });

        // Every endpoint needs a signed-in user unless it says otherwise, as the page and its files do.
        builder.Services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        builder.Services.AddSingleton<AccountSettingsCheck>();
        return builder;
    }

    /// <summary>
    /// Checks that the account settings are safe to run with, logging why when they aren't. Run before the app
    /// starts, so a development sign-in can't reach production.
    /// </summary>
    public static Task<bool> CheckAccountSettingsAsync(this IServiceProvider services) =>
        Task.FromResult(services.GetRequiredService<AccountSettingsCheck>().Run());

    private sealed class AccountsMarker;
}
