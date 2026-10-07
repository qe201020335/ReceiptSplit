using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using ReceiptSplit.Extraction;
using ReceiptSplit.Testing;

namespace ReceiptSplit.Tests.Support;

/// <summary>
/// Runs the app against a throwaway storage directory, with a fake model unless told otherwise. Requests are signed
/// in with Access tokens made by <see cref="AccessTokens"/>, checked by the app's real token handler; clients from
/// <see cref="WebApplicationFactory{TEntryPoint}.CreateClient()"/> carry one for <see cref="MemberEmail"/>.
/// </summary>
public sealed class ReceiptApiFactory(bool useFakeLlm = true) : WebApplicationFactory<Program>
{
    public const string MemberEmail = "member@example.com";

    public const string AdminEmail = "admin@example.com";

    public string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), "receiptsplit-tests", Guid.NewGuid().ToString("N"));

    public FakeLlamaClient Llm { get; } = new();

    /// <summary>The default member's Access session, the same for every client, as in one browser.</summary>
    private readonly string _memberNonce = Guid.NewGuid().ToString("N");

    /// <summary>A client signed in as <paramref name="email"/>, in a new Access session unless one is given.</summary>
    public HttpClient CreateClientFor(string email, string? nonce = null) =>
        CreateClientWithToken(AccessTokens.Create(email, nonce));

    /// <summary>A client sending <paramref name="token"/> as its Access token, or none when null.</summary>
    public HttpClient CreateClientWithToken(string? token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Remove(AccessTokens.Header);
        if (token is not null)
        {
            client.DefaultRequestHeaders.Add(AccessTokens.Header, token);
        }

        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Storage:Root", StorageRoot);
        // appsettings.Development.json signs every request in as a development user, which would skip the tokens.
        builder.UseSetting("Auth:DevUser", "");
        builder.UseSetting("Auth:CloudflareAccess:TeamDomain", AccessTokens.TeamDomain);
        builder.UseSetting("Auth:CloudflareAccess:Audience", AccessTokens.Audience);
        builder.UseSetting("Auth:Admins:0", AdminEmail);
        builder.ConfigureTestServices(services =>
        {
            // The test key in place of the keys Access publishes, which the startup check also reads.
            services.PostConfigureAll<JwtBearerOptions>(options => options.ConfigurationManager =
                new StaticConfigurationManager<OpenIdConnectConfiguration>(AccessTokens.Configuration));
        });

        if (useFakeLlm)
        {
            // The app checks at startup that the model server offers the configured model.
            builder.UseSetting("Llm:Model", FakeLlamaClient.ModelName);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ILlamaClient>();
                services.AddSingleton<ILlamaClient>(Llm);
            });
        }
    }

    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        client.DefaultRequestHeaders.Add(AccessTokens.Header, AccessTokens.Create(MemberEmail, _memberNonce));
    }

    public override async ValueTask DisposeAsync()
    {
        Llm.Release();
        await base.DisposeAsync();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(StorageRoot))
        {
            Directory.Delete(StorageRoot, recursive: true);
        }
    }
}
