using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ImageMagick;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ReceiptSplit.Accounts;
using ReceiptSplit.Contracts;
using ReceiptSplit.Data;
using ReceiptSplit.Testing;
using ReceiptSplit.Tests.Support;

namespace ReceiptSplit.Tests;

public class AccountsApiTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_first_sign_in_creates_a_user_linked_to_the_external_identity()
    {
        await using var factory = new ReceiptApiFactory();
        using var client = factory.CreateClientFor("New.Person@Example.com");

        var me = await MeAsync(client);

        Assert.Equal(("new.person@example.com", "Name of New.Person@Example.com"), (me.Email, me.Name));
        var identity = await QueryAsync(factory, db => db.ExternalIdentities.SingleAsync(Ct));
        Assert.Equal(
            (me.Id, IdentityProvider.Google, "google-new.person@example.com"),
            (identity.UserId, identity.Provider, identity.Subject));
    }

    [Fact]
    public async Task The_identity_is_looked_up_once_per_access_session()
    {
        await using var factory = new ReceiptApiFactory();
        using var client = factory.CreateClient();
        using var sameSession = factory.CreateClient();

        foreach (var c in new[] { client, client, sameSession })
        {
            using var response = await c.GetAsync("/api/receipts", Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Single(factory.Identities.Lookups);

        using var newSession = factory.CreateClientFor(ReceiptApiFactory.MemberEmail);
        Assert.Equal((await MeAsync(client)).Id, (await MeAsync(newSession)).Id);
        Assert.Equal(2, factory.Identities.Lookups.Count);
    }

    [Fact]
    public async Task Concurrent_first_requests_of_a_session_share_one_lookup()
    {
        await using var factory = new ReceiptApiFactory();
        factory.Identities.Delay = TimeSpan.FromMilliseconds(200);
        using var client = factory.CreateClient();

        // As the page does on load: several API calls before the session has been identified.
        var accounts = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => MeAsync(client)));

        Assert.Single(accounts.Select(a => a.Id).Distinct());
        Assert.Single(factory.Identities.Lookups);
        Assert.Equal(1, await QueryAsync(factory, db => db.Users.CountAsync(Ct)));
    }

    [Fact]
    public async Task Concurrent_first_requests_of_the_development_user_create_it_once()
    {
        await using var factory = new ReceiptApiFactory();
        await using var development = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Auth:DevUser", "dev@example.com"));
        using var client = development.CreateClient();
        client.DefaultRequestHeaders.Remove(AccessTokens.Header);

        var accounts = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => MeAsync(client)));

        Assert.Single(accounts.Select(a => a.Id).Distinct());
    }

    [Fact]
    public async Task A_token_just_past_its_expiry_is_still_resolved()
    {
        // The token handler allows five minutes of clock skew, so this token is still accepted.
        await using var factory = new ReceiptApiFactory();
        using var client = factory.CreateClientWithToken(
            AccessTokens.Create(ReceiptApiFactory.MemberEmail, expires: DateTime.UtcNow.AddMinutes(-2)));

        Assert.Equal(ReceiptApiFactory.MemberEmail, (await MeAsync(client)).Email);
    }

    [Fact]
    public async Task A_returning_user_picks_up_a_changed_email_and_name()
    {
        await using var factory = new ReceiptApiFactory();
        using var before = factory.CreateClient();
        var original = await MeAsync(before);
        factory.Identities.Answers["renamed@example.com"] = IdentityLookupResult.Found(
            FakeIdentityLookup.GoogleAccount(ReceiptApiFactory.MemberEmail, "Renamed Member"));

        using var after = factory.CreateClientFor("renamed@example.com");
        var renamed = await MeAsync(after);

        Assert.Equal(original.Id, renamed.Id);
        Assert.Equal(("renamed@example.com", "Renamed Member"), (renamed.Email, renamed.Name));
    }

    [Fact]
    public async Task A_new_email_that_another_user_holds_is_not_taken()
    {
        await using var factory = new ReceiptApiFactory();
        using var first = factory.CreateClientFor("first@example.com");
        using var second = factory.CreateClientFor("second@example.com");
        var (firstUser, secondUser) = (await MeAsync(first), await MeAsync(second));
        // The first person's account now reports the second person's email.
        factory.Identities.Answers["second@example.com"] =
            IdentityLookupResult.Found(FakeIdentityLookup.GoogleAccount("first@example.com"));

        using var moved = factory.CreateClientFor("second@example.com");
        var resolved = await MeAsync(moved);

        Assert.Equal((firstUser.Id, "first@example.com"), (resolved.Id, resolved.Email));
        var secondEmail = await QueryAsync(
            factory, db => db.Users.Where(u => u.Id == secondUser.Id).Select(u => u.Email).SingleAsync(Ct));
        Assert.Equal("second@example.com", secondEmail);
    }

    [Fact]
    public async Task An_unsupported_sign_in_method_is_refused_without_creating_a_user()
    {
        await using var factory = new ReceiptApiFactory();
        factory.Identities.Answers[ReceiptApiFactory.MemberEmail] = IdentityLookupResult.Unsupported;
        using var client = factory.CreateClient();

        await AssertSignInProblemAsync(client, "/api/receipts", HttpStatusCode.Forbidden, "unsupported-sign-in");
        await AssertSignInProblemAsync(client, "/api/me", HttpStatusCode.Forbidden, "unsupported-sign-in");
        Assert.Equal(0, await QueryAsync(factory, db => db.Users.CountAsync(Ct)));
    }

    [Fact]
    public async Task An_email_that_belongs_to_another_account_is_refused()
    {
        await using var factory = new ReceiptApiFactory();
        using var holder = factory.CreateClient();
        await MeAsync(holder);
        // Someone else's Google account with the same email, in a session of their own.
        factory.Identities.Answers[ReceiptApiFactory.MemberEmail] =
            IdentityLookupResult.Found(new ProviderIdentity(IdentityProvider.Google, "another-google-id", "Someone"));
        using var other = factory.CreateClientFor(ReceiptApiFactory.MemberEmail);

        await AssertSignInProblemAsync(other, "/api/me", HttpStatusCode.Forbidden, "account-conflict");
        var counts = await QueryAsync(
            factory, async db => (await db.Users.CountAsync(Ct), await db.ExternalIdentities.CountAsync(Ct)));
        Assert.Equal((1, 1), counts);
    }

    [Fact]
    public async Task An_unavailable_identity_is_retried_on_the_next_request()
    {
        await using var factory = new ReceiptApiFactory();
        factory.Identities.Unavailable = true;
        using var client = factory.CreateClient();

        await AssertSignInProblemAsync(
            client, "/api/receipts", HttpStatusCode.ServiceUnavailable, "identity-unavailable");

        factory.Identities.Unavailable = false;
        using var retried = await client.GetAsync("/api/receipts", Ct);
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        Assert.Equal(2, factory.Identities.Lookups.Count);
    }

    [Fact]
    public async Task Sessions_already_identified_keep_working_while_lookups_fail()
    {
        await using var factory = new ReceiptApiFactory();
        using var identified = factory.CreateClient();
        await MeAsync(identified);
        factory.Identities.Unavailable = true;

        using var response = await identified.GetAsync("/api/receipts", Ct);
        using var newSession = factory.CreateClientFor(ReceiptApiFactory.MemberEmail);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertSignInProblemAsync(
            newSession, "/api/receipts", HttpStatusCode.ServiceUnavailable, "identity-unavailable");
    }

    [Fact]
    public async Task A_refused_sign_in_still_loads_the_page()
    {
        await using var factory = new ReceiptApiFactory();
        factory.Identities.Answers[ReceiptApiFactory.MemberEmail] = IdentityLookupResult.Unsupported;
        var webRoot = Directory.CreateDirectory(Path.Combine(factory.StorageRoot, "wwwroot")).FullName;
        await File.WriteAllTextAsync(
            Path.Combine(webRoot, "index.html"), "<!doctype html><title>ReceiptSplit</title>", Ct);
        await using var withPage = factory.WithWebHostBuilder(builder => builder.UseWebRoot(webRoot));
        using var client = withPage.CreateClient();

        using var page = await client.GetAsync("/", Ct);

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        await AssertSignInProblemAsync(client, "/api/me", HttpStatusCode.Forbidden, "unsupported-sign-in");
    }

    [Fact]
    public async Task Me_describes_a_member_and_an_admin()
    {
        await using var factory = new ReceiptApiFactory();
        using var member = factory.CreateClient();
        // The admin list is matched ignoring case.
        using var admin = factory.CreateClientFor("Admin@Example.COM");

        var memberAccount = await MeAsync(member);
        var adminAccount = await MeAsync(admin);

        Assert.Equal(
            (ReceiptApiFactory.MemberEmail, "Name of member@example.com", false, FakeIdentityLookup.SignOutPath),
            (memberAccount.Email, memberAccount.Name, memberAccount.IsAdmin, memberAccount.SignOutUrl));
        Assert.Equal((ReceiptApiFactory.AdminEmail, true), (adminAccount.Email, adminAccount.IsAdmin));
        Assert.NotEqual(memberAccount.Id, adminAccount.Id);
    }

    [Fact]
    public async Task An_admin_email_held_by_another_user_doesnt_make_someone_an_admin()
    {
        await using var factory = new ReceiptApiFactory();
        using var admin = factory.CreateClientFor(ReceiptApiFactory.AdminEmail);
        using var member = factory.CreateClient();
        await MeAsync(admin);
        var memberAccount = await MeAsync(member);
        // The member's account now reports the admin's email, which the admin's user still holds.
        factory.Identities.Answers[ReceiptApiFactory.AdminEmail] =
            IdentityLookupResult.Found(FakeIdentityLookup.GoogleAccount(ReceiptApiFactory.MemberEmail));

        using var moved = factory.CreateClientFor(ReceiptApiFactory.AdminEmail);
        var resolved = await MeAsync(moved);

        Assert.Equal(
            (memberAccount.Id, ReceiptApiFactory.MemberEmail, false), (resolved.Id, resolved.Email, resolved.IsAdmin));
        using var release = await moved.PostAsJsonAsync(
            "/api/users/release-email", new ReleaseEmailDto(ReceiptApiFactory.AdminEmail), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, release.StatusCode);
    }

    [Fact]
    public async Task The_development_user_has_nothing_to_sign_out_of()
    {
        await using var factory = new ReceiptApiFactory();
        await using var development = factory.WithWebHostBuilder(builder => builder
            .UseSetting("Auth:DevUser", "Dev@Example.com")
            .UseSetting("Auth:Admins:0", "dev@example.com"));
        using var client = development.CreateClient();
        client.DefaultRequestHeaders.Remove(AccessTokens.Header);

        var me = await MeAsync(client);

        Assert.Equal(("dev@example.com", true, null), (me.Email, me.IsAdmin, me.SignOutUrl));
        Assert.Empty(factory.Identities.Lookups);
    }

    [Fact]
    public async Task Admins_list_every_user()
    {
        await using var factory = new ReceiptApiFactory();
        // Ordinal order would be Bob, Zoe, amy: the list ignores case.
        Name(factory, ReceiptApiFactory.AdminEmail, "Zoe");
        Name(factory, ReceiptApiFactory.MemberEmail, "amy");
        Name(factory, "other@example.com", "Bob");
        using var admin = factory.CreateClientFor(ReceiptApiFactory.AdminEmail);
        using var member = factory.CreateClient();
        using var other = factory.CreateClientFor("other@example.com");
        var (zoe, amy, bob) = (await MeAsync(admin), await MeAsync(member), await MeAsync(other));

        var users = await admin.GetFromJsonAsync<List<UserSummaryDto>>("/api/users", Json, Ct);

        Assert.Equal(
            [
                new UserSummaryDto(amy.Id, ReceiptApiFactory.MemberEmail, "amy"),
                new UserSummaryDto(bob.Id, "other@example.com", "Bob"),
                new UserSummaryDto(zoe.Id, ReceiptApiFactory.AdminEmail, "Zoe"),
            ],
            users!);

        static void Name(ReceiptApiFactory factory, string email, string name) =>
            factory.Identities.Answers[email] =
                IdentityLookupResult.Found(FakeIdentityLookup.GoogleAccount(email, name));
    }

    [Fact]
    public async Task Members_cant_list_users()
    {
        await using var factory = new ReceiptApiFactory();
        using var member = factory.CreateClient();

        using var response = await member.GetAsync("/api/users", Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Members_cant_release_an_email()
    {
        await using var factory = new ReceiptApiFactory();
        using var member = factory.CreateClient();
        await MeAsync(member);

        using var response = await member.PostAsJsonAsync(
            "/api/users/release-email", new ReleaseEmailDto(ReceiptApiFactory.MemberEmail), Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ReceiptApiFactory.MemberEmail, (await MeAsync(member)).Email);
    }

    [Fact]
    public async Task Releasing_an_email_no_user_holds_is_not_found()
    {
        await using var factory = new ReceiptApiFactory();
        using var admin = factory.CreateClientFor(ReceiptApiFactory.AdminEmail);

        using var unknown = await admin.PostAsJsonAsync(
            "/api/users/release-email", new ReleaseEmailDto("nobody@example.com"), Ct);
        using var notAnEmail = await admin.PostAsJsonAsync(
            "/api/users/release-email", new ReleaseEmailDto("nobody"), Ct);

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, notAnEmail.StatusCode);
    }

    [Fact]
    public async Task Releasing_an_email_lets_the_refused_person_in_as_a_new_user()
    {
        await using var factory = new ReceiptApiFactory();
        using var holder = factory.CreateClient();
        var holderAccount = await MeAsync(holder);
        var receipt = await UploadAsync(holder);
        factory.Identities.Answers[ReceiptApiFactory.MemberEmail] =
            IdentityLookupResult.Found(new ProviderIdentity(IdentityProvider.Google, "another-google-id", "Someone"));
        using var refused = factory.CreateClientFor(ReceiptApiFactory.MemberEmail);
        await AssertSignInProblemAsync(refused, "/api/me", HttpStatusCode.Forbidden, "account-conflict");

        using var admin = factory.CreateClientFor(ReceiptApiFactory.AdminEmail);
        using var release = await admin.PostAsJsonAsync(
            "/api/users/release-email", new ReleaseEmailDto("Member@Example.com"), Ct);
        Assert.Equal(HttpStatusCode.NoContent, release.StatusCode);

        // The refusal is remembered for a minute.
        await AssertSignInProblemAsync(refused, "/api/me", HttpStatusCode.Forbidden, "account-conflict");
        factory.Clock.Advance(TimeSpan.FromSeconds(61));
        var newUser = await MeAsync(refused);
        Assert.NotEqual(holderAccount.Id, newUser.Id);
        Assert.Equal(("member@example.com", "Someone"), (newUser.Email, newUser.Name));

        // The previous holder keeps their sign-in and receipts, without an email.
        var kept = await MeAsync(holder);
        Assert.Equal((holderAccount.Id, null), (kept.Id, kept.Email));
        var summaries = await holder.GetFromJsonAsync<List<ReceiptSummaryDto>>("/api/receipts", Json, Ct);
        Assert.Contains(summaries!, s => s.Id == receipt);
    }

    private static async Task<AccountDto> MeAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/me", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AccountDto>(Json, Ct))!;
    }

    private static async Task AssertSignInProblemAsync(
        HttpClient client, string path, HttpStatusCode status, string code)
    {
        using var response = await client.GetAsync(path, Ct);

        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>(Json, Ct))!;
        Assert.Equal(code, problem.Extensions["code"]?.ToString());
        Assert.Equal(FakeIdentityLookup.SignOutPath, problem.Extensions["signOutUrl"]?.ToString());
        Assert.False(string.IsNullOrEmpty(problem.Detail));
        Assert.DoesNotContain("Google", problem.Detail);
        Assert.DoesNotContain("Google", problem.Title);
    }

    private static async Task<Guid> UploadAsync(HttpClient client)
    {
        var file = new ByteArrayContent(TestImages.Create(64, 48, MagickFormat.Jpeg));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var response = await client.PostAsync(
            "/api/receipts", new MultipartFormDataContent { { file, "file", "receipt.jpg" } }, Ct);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ReceiptQueuedDto>(Json, Ct))!.Id;
    }

    private static async Task<T> QueryAsync<T>(ReceiptApiFactory factory, Func<AppDbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}
