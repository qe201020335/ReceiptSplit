using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using ReceiptSplit.Data;
using ReceiptSplit.Testing;

namespace ReceiptSplit.Accounts.Tests;

/// <summary>The resolution rules, against the real schema in an in-memory SQLite database.</summary>
public sealed class AccountServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    private readonly ListLogger<AccountService> _logger = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _connection.OpenAsync(Ct);
        await using var db = NewDb();
        await db.Database.MigrateAsync(Ct);
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task The_first_sign_in_creates_a_user_linked_to_the_identity()
    {
        var resolution = await ResolveAsync(" New.Person@Example.com ", Google("g-1", "New Person"));

        await using var db = NewDb();
        var user = await db.Users.Include(u => u.ExternalIdentities).SingleAsync(Ct);
        Assert.Equal(user.Id, resolution.UserId);
        Assert.Equal(("new.person@example.com", "New Person"), (user.Email, user.Name));
        var identity = Assert.Single(user.ExternalIdentities);
        Assert.Equal((IdentityProvider.Google, "g-1"), (identity.Provider, identity.Subject));
    }

    [Fact]
    public async Task A_returning_user_is_found_by_identity_and_picks_up_a_new_email_and_name()
    {
        var first = await ResolveAsync("old@example.com", Google("g-1", "Old Name"));

        var again = await ResolveAsync("new@example.com", Google("g-1", "New Name"));

        Assert.Equal(first.UserId, again.UserId);
        await using var db = NewDb();
        var user = await db.Users.SingleAsync(Ct);
        Assert.Equal(("new@example.com", "New Name"), (user.Email, user.Name));
    }

    [Fact]
    public async Task A_name_the_identity_doesnt_give_is_kept()
    {
        await ResolveAsync("person@example.com", Google("g-1", "Person"));

        await ResolveAsync("person@example.com", Google("g-1", null));

        await using var db = NewDb();
        Assert.Equal("Person", (await db.Users.SingleAsync(Ct)).Name);
    }

    [Fact]
    public async Task A_new_email_held_by_another_user_is_not_taken_and_is_logged()
    {
        var first = await ResolveAsync("first@example.com", Google("g-1"));
        var second = await ResolveAsync("second@example.com", Google("g-2"));

        var moved = await ResolveAsync("second@example.com", Google("g-1"));

        Assert.Equal(first.UserId, moved.UserId);
        await using var db = NewDb();
        Assert.Equal(
            ["first@example.com", "second@example.com"],
            await db.Users.OrderBy(u => u.Email).Select(u => u.Email).ToListAsync(Ct));
        var warning = Assert.Single(_logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains(first.UserId.ToString()!, warning.Message);
        Assert.Contains(second.UserId.ToString()!, warning.Message);
    }

    [Fact]
    public async Task An_email_held_by_another_account_is_refused_without_creating_anything()
    {
        await ResolveAsync("person@example.com", Google("g-1"));

        var conflict = await ResolveAsync("Person@example.com", Google("g-2"));

        Assert.Null(conflict.UserId);
        await using var db = NewDb();
        Assert.Equal((1, 1), (await db.Users.CountAsync(Ct), await db.ExternalIdentities.CountAsync(Ct)));
    }

    [Fact]
    public async Task A_first_sign_in_that_loses_a_race_resolves_to_the_winner()
    {
        Guid winner = default;
        // Just before this sign-in saves, a request for the same person creates them.
        var race = new BeforeFirstSave(async () =>
        {
            await using var other = NewDb();
            winner = (await new AccountService(other, _logger)
                .ResolveAsync("person@example.com", Google("g-1"), Ct)).UserId!.Value;
        });

        await using var db = NewDb(race);
        var resolution = await new AccountService(db, _logger).ResolveAsync("person@example.com", Google("g-1"), Ct);

        Assert.Equal(winner, resolution.UserId);
        await using var check = NewDb();
        Assert.Equal(1, await check.Users.CountAsync(Ct));
    }

    [Fact]
    public async Task Development_sign_in_finds_or_creates_the_user_by_email()
    {
        await using var db = NewDb();
        var accounts = new AccountService(db, _logger);

        var created = await accounts.ResolveDevelopmentAsync("Dev@Example.com", Ct);
        var found = await accounts.ResolveDevelopmentAsync("dev@example.com", Ct);

        Assert.Equal(created.Id, found.Id);
        Assert.Equal("dev@example.com", created.Email);
        Assert.Empty(await db.ExternalIdentities.ToListAsync(Ct));
    }

    [Fact]
    public async Task Releasing_an_email_keeps_the_users_identity()
    {
        var holder = await ResolveAsync("person@example.com", Google("g-1"));
        await using var db = NewDb();
        var accounts = new AccountService(db, _logger);

        Assert.True(await accounts.ReleaseEmailAsync(" Person@Example.com", Ct));
        Assert.False(await accounts.ReleaseEmailAsync("person@example.com", Ct));

        await using var check = NewDb();
        var user = await check.Users.Include(u => u.ExternalIdentities).SingleAsync(Ct);
        Assert.Equal(holder.UserId, user.Id);
        Assert.Null(user.Email);
        Assert.Single(user.ExternalIdentities);
        // The same account still finds its user, and takes the email back now that nobody holds it.
        Assert.Equal(holder.UserId, (await ResolveAsync("person@example.com", Google("g-1"))).UserId);
    }

    [Fact]
    public async Task Users_are_listed_by_name_or_else_email_ignoring_case()
    {
        await ResolveAsync("zed@example.com", Google("g-1", "zed"));
        await ResolveAsync("amy@example.com", Google("g-2", "Amy"));
        await ResolveAsync("bea@example.com", Google("g-3", null));
        await using var db = NewDb();
        db.Users.Add(new User());
        await db.SaveChangesAsync(Ct);

        var users = await new AccountService(db, _logger).ListUsersAsync(Ct);

        Assert.Equal(["amy@example.com", "bea@example.com", "zed@example.com", null], users.Select(u => u.Email));
        Assert.Equal(["Amy", null, "zed", null], users.Select(u => u.Name));
    }

    [Fact]
    public async Task A_user_whose_email_was_released_is_still_listed()
    {
        var holder = await ResolveAsync("person@example.com", Google("g-1", "Person"));
        await using var db = NewDb();
        var accounts = new AccountService(db, _logger);
        await accounts.ReleaseEmailAsync("person@example.com", Ct);

        var user = Assert.Single(await accounts.ListUsersAsync(Ct));

        Assert.Equal(new UserSummary(holder.UserId!.Value, null, "Person"), user);
    }

    private async Task<AccountResolution> ResolveAsync(string email, ProviderIdentity identity)
    {
        await using var db = NewDb();
        return await new AccountService(db, _logger).ResolveAsync(email, identity, Ct);
    }

    private static ProviderIdentity Google(string subject, string? name = "Someone") =>
        new(IdentityProvider.Google, subject, name);

    private AppDbContext NewDb(params IInterceptor[] interceptors) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).AddInterceptors(interceptors).Options);

    private sealed class BeforeFirstSave(Func<Task> action) : SaveChangesInterceptor
    {
        private bool _done;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!_done)
            {
                _done = true;
                await action();
            }

            return result;
        }
    }
}
