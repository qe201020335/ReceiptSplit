using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ReceiptSplit.Contracts;
using ReceiptSplit.Data;
using ReceiptSplit.Testing;
using ReceiptSplit.Tests.Support;

namespace ReceiptSplit.Tests;

public class ReceiptAccessTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_upload_is_owned_by_the_uploader()
    {
        await using var factory = new ReceiptApiFactory();
        using var member = factory.CreateClient();

        var id = await UploadAsync(member);

        var me = (await member.GetFromJsonAsync<AccountDto>("/api/me", Json, Ct))!;
        var owner = await QueryAsync(
            factory, db => db.Receipts.Where(r => r.Id == id).Select(r => r.OwnerId).SingleAsync(Ct));
        Assert.Equal(me.Id, owner);
    }

    [Fact]
    public async Task A_listed_receipt_names_its_owner()
    {
        await using var factory = new ReceiptApiFactory();
        using var member = factory.CreateClient();
        await UploadAsync(member);

        var me = (await member.GetFromJsonAsync<AccountDto>("/api/me", Json, Ct))!;
        var summary = Assert.Single(await SummariesAsync(member));
        Assert.Equal(me.Id, summary.OwnerId);
    }

    [Fact]
    public async Task A_listed_receipt_without_an_owner_has_a_null_owner()
    {
        await using var factory = new ReceiptApiFactory();
        using var admin = factory.CreateClientFor(ReceiptApiFactory.AdminEmail);
        var id = await AddUnownedReceiptAsync(factory);

        var summary = Assert.Single(await SummariesAsync(admin));
        Assert.Equal((id, null), (summary.Id, summary.OwnerId));
    }

    [Fact]
    public async Task Members_list_only_their_own_receipts()
    {
        await using var factory = new ReceiptApiFactory();
        using var member = factory.CreateClient();
        using var other = factory.CreateClientFor("other@example.com");
        var own = await UploadAsync(member);
        await UploadAsync(other);
        await AddUnownedReceiptAsync(factory);

        Assert.Equal([own], await ListAsync(member));
    }

    [Fact]
    public async Task Someone_elses_receipt_is_not_found_and_stays_unchanged()
    {
        await using var factory = new ReceiptApiFactory();
        using var owner = factory.CreateClientFor("owner@example.com");
        using var member = factory.CreateClient();
        var id = await UploadAsync(owner);
        var before = await WaitForResultAsync(owner, id);

        var responses = await TryEverythingAsync(member, id);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
        var after = (await owner.GetFromJsonAsync<ReceiptDetailDto>($"/api/receipts/{id}", Json, Ct))!;
        Assert.Equal(
            (before.Status, before.TaxRatePercent, before.EditedAt),
            (after.Status, after.TaxRatePercent, after.EditedAt));
        Assert.Single(factory.Llm.Requests);
    }

    [Fact]
    public async Task A_receipt_without_an_owner_is_not_found_for_members()
    {
        await using var factory = new ReceiptApiFactory();
        using var member = factory.CreateClient();
        var id = await AddUnownedReceiptAsync(factory);

        using var response = await member.GetAsync($"/api/receipts/{id}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Admins_list_every_receipt_including_those_without_an_owner()
    {
        await using var factory = new ReceiptApiFactory();
        using var member = factory.CreateClient();
        using var other = factory.CreateClientFor("other@example.com");
        using var admin = factory.CreateClientFor(ReceiptApiFactory.AdminEmail);
        Guid[] expected = [await UploadAsync(member), await UploadAsync(other), await AddUnownedReceiptAsync(factory)];

        Assert.Equal(expected.Order(), (await ListAsync(admin)).Order());
        using var unowned = await admin.GetAsync($"/api/receipts/{expected[2]}", Ct);
        Assert.Equal(HttpStatusCode.OK, unowned.StatusCode);
    }

    [Fact]
    public async Task Admins_can_edit_and_delete_another_users_receipt()
    {
        await using var factory = new ReceiptApiFactory();
        using var member = factory.CreateClient();
        using var admin = factory.CreateClientFor(ReceiptApiFactory.AdminEmail);
        var id = await UploadAsync(member);
        await WaitForResultAsync(member, id);

        using var edit = await admin.PutAsJsonAsync($"/api/receipts/{id}", OneLineEdit, Json, Ct);
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var edited = await member.GetFromJsonAsync<ReceiptDetailDto>($"/api/receipts/{id}", Json, Ct);
        Assert.Equal("By an admin", edited!.StoreName);

        using var delete = await admin.DeleteAsync($"/api/receipts/{id}", Ct);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Empty(await ListAsync(member));
    }

    [Fact]
    public async Task Admins_reassign_a_receipt_to_another_user()
    {
        await using var factory = new ReceiptApiFactory();
        using var member = factory.CreateClient();
        using var other = factory.CreateClientFor("other@example.com");
        using var admin = factory.CreateClientFor(ReceiptApiFactory.AdminEmail);
        var id = await UploadAsync(member);
        await WaitForResultAsync(member, id);
        var otherId = (await other.GetFromJsonAsync<AccountDto>("/api/me", Json, Ct))!.Id;

        using var response = await ReassignAsync(admin, otherId, id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal([id], await ListAsync(other));
        using var opened = await other.GetAsync($"/api/receipts/{id}", Ct);
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        Assert.Empty(await ListAsync(member));
        using var gone = await member.GetAsync($"/api/receipts/{id}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task Admins_give_a_receipt_without_an_owner_to_a_member()
    {
        await using var factory = new ReceiptApiFactory();
        using var member = factory.CreateClient();
        using var admin = factory.CreateClientFor(ReceiptApiFactory.AdminEmail);
        var id = await AddUnownedReceiptAsync(factory);
        var memberId = (await member.GetFromJsonAsync<AccountDto>("/api/me", Json, Ct))!.Id;

        using var response = await ReassignAsync(admin, memberId, id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal([id], await ListAsync(member));
        using var opened = await member.GetAsync($"/api/receipts/{id}", Ct);
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
    }

    [Fact]
    public async Task Admins_set_a_receipt_back_to_no_owner()
    {
        await using var factory = new ReceiptApiFactory();
        using var member = factory.CreateClient();
        using var admin = factory.CreateClientFor(ReceiptApiFactory.AdminEmail);
        var id = await UploadAsync(member);
        await WaitForResultAsync(member, id);

        using var response = await ReassignAsync(admin, null, id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await ListAsync(member));
        using var gone = await member.GetAsync($"/api/receipts/{id}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        var summary = Assert.Single(await SummariesAsync(admin));
        Assert.Equal((id, null), (summary.Id, summary.OwnerId));
    }

    [Fact]
    public async Task Admins_reassign_receipts_with_different_owners_together()
    {
        await using var factory = new ReceiptApiFactory();
        using var member = factory.CreateClient();
        using var other = factory.CreateClientFor("other@example.com");
        using var admin = factory.CreateClientFor(ReceiptApiFactory.AdminEmail);
        Guid[] ids = [await UploadAsync(member), await UploadAsync(other), await AddUnownedReceiptAsync(factory)];
        var adminId = (await admin.GetFromJsonAsync<AccountDto>("/api/me", Json, Ct))!.Id;

        using var response = await ReassignAsync(admin, adminId, ids);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.All(await SummariesAsync(admin), s => Assert.Equal(adminId, s.OwnerId));
        Assert.Empty(await ListAsync(member));
        Assert.Empty(await ListAsync(other));
    }

    [Fact]
    public async Task A_receipt_being_read_keeps_the_owner_it_was_given()
    {
        await using var factory = new ReceiptApiFactory();
        using var member = factory.CreateClient();
        using var other = factory.CreateClientFor("other@example.com");
        using var admin = factory.CreateClientFor(ReceiptApiFactory.AdminEmail);
        var otherId = (await other.GetFromJsonAsync<AccountDto>("/api/me", Json, Ct))!.Id;
        factory.Llm.Block();
        var id = await UploadAsync(member);
        await WaitForStatusAsync(member, id, ReceiptStatus.Processing);

        using var response = await ReassignAsync(admin, otherId, id);
        factory.Llm.Release();

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var result = await WaitForResultAsync(other, id);
        Assert.Equal(ReceiptStatus.Completed, result.Status);
        Assert.NotEmpty(result.Lines);
        Assert.Equal(otherId, await OwnerOfAsync(factory, id));
    }

    [Fact]
    public async Task A_reassign_with_an_unknown_id_changes_nothing()
    {
        await using var factory = new ReceiptApiFactory();
        using var member = factory.CreateClient();
        using var admin = factory.CreateClientFor(ReceiptApiFactory.AdminEmail);
        var id = await UploadAsync(member);
        var memberId = (await member.GetFromJsonAsync<AccountDto>("/api/me", Json, Ct))!.Id;
        var unknown = Guid.NewGuid();

        using var response = await ReassignAsync(admin, null, id, unknown);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        Assert.Equal("1 receipt couldn't be found; nothing was reassigned.", problem.GetProperty("detail").GetString());
        Assert.Equal([unknown], problem.GetProperty("ids").EnumerateArray().Select(i => i.GetGuid()));
        Assert.Equal(memberId, await OwnerOfAsync(factory, id));
    }

    [Fact]
    public async Task A_reassign_to_an_unknown_user_changes_nothing()
    {
        await using var factory = new ReceiptApiFactory();
        using var member = factory.CreateClient();
        using var admin = factory.CreateClientFor(ReceiptApiFactory.AdminEmail);
        var id = await UploadAsync(member);
        var memberId = (await member.GetFromJsonAsync<AccountDto>("/api/me", Json, Ct))!.Id;

        using var response = await ReassignAsync(admin, Guid.NewGuid(), id);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        Assert.Equal("User not found", problem.GetProperty("title").GetString());
        Assert.Equal(memberId, await OwnerOfAsync(factory, id));
    }

    [Fact]
    public async Task Members_cant_reassign_receipts()
    {
        await using var factory = new ReceiptApiFactory();
        using var member = factory.CreateClient();
        using var other = factory.CreateClientFor("other@example.com");
        var own = await UploadAsync(member);
        var theirs = await UploadAsync(other);
        var memberId = (await member.GetFromJsonAsync<AccountDto>("/api/me", Json, Ct))!.Id;
        var otherId = (await other.GetFromJsonAsync<AccountDto>("/api/me", Json, Ct))!.Id;

        using var giving = await ReassignAsync(member, otherId, own);
        using var taking = await ReassignAsync(member, memberId, theirs);

        Assert.Equal(HttpStatusCode.Forbidden, giving.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, taking.StatusCode);
        Assert.Equal((memberId, otherId), (await OwnerOfAsync(factory, own), await OwnerOfAsync(factory, theirs)));
    }

    [Fact]
    public async Task A_reassign_needs_at_least_one_receipt()
    {
        await using var factory = new ReceiptApiFactory();
        using var admin = factory.CreateClientFor(ReceiptApiFactory.AdminEmail);

        using var response = await ReassignAsync(admin, null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_reassign_that_leaves_out_the_owner_changes_nothing()
    {
        await using var factory = new ReceiptApiFactory();
        using var member = factory.CreateClient();
        using var admin = factory.CreateClientFor(ReceiptApiFactory.AdminEmail);
        var id = await UploadAsync(member);
        var memberId = (await member.GetFromJsonAsync<AccountDto>("/api/me", Json, Ct))!.Id;

        using var response = await admin.PostAsync(
            "/api/receipts/reassign", JsonContent.Create(new { ids = new[] { id } }), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(memberId, await OwnerOfAsync(factory, id));
    }

    private static readonly ReceiptEditDto OneLineEdit = new(
        "By an admin",
        null,
        1.00m,
        0m,
        null,
        1.00m,
        [new ReceiptLineEditDto("TYPED", null, 1m, 1.00m, 0m, null, false)]);

    /// <summary>Every per-receipt action, each of which would change or reveal the receipt if allowed.</summary>
    private static async Task<HttpResponseMessage[]> TryEverythingAsync(HttpClient client, Guid id) =>
    [
        await client.GetAsync($"/api/receipts/{id}", Ct),
        await client.GetAsync($"/api/receipts/{id}/image", Ct),
        await client.PutAsJsonAsync($"/api/receipts/{id}", OneLineEdit, Json, Ct),
        await client.PatchAsJsonAsync($"/api/receipts/{id}", new ReceiptTaxRateDto(5m), Json, Ct),
        await client.PostAsync($"/api/receipts/{id}/extract", null, Ct),
        await client.DeleteAsync($"/api/receipts/{id}", Ct),
    ];

    private static async Task<List<Guid>> ListAsync(HttpClient client) =>
        [.. (await SummariesAsync(client)).Select(s => s.Id)];

    private static async Task<List<ReceiptSummaryDto>> SummariesAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<ReceiptSummaryDto>>("/api/receipts", Json, Ct))!;

    private static async Task<Guid> UploadAsync(HttpClient client)
    {
        var file = new ByteArrayContent(TestImages.Create(64, 48, MagickFormat.Jpeg));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var response = await client.PostAsync(
            "/api/receipts", new MultipartFormDataContent { { file, "file", "receipt.jpg" } }, Ct);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ReceiptQueuedDto>(Json, Ct))!.Id;
    }

    private static Task<HttpResponseMessage> ReassignAsync(HttpClient client, Guid? ownerId, params Guid[] ids) =>
        client.PostAsJsonAsync("/api/receipts/reassign", new ReceiptReassignDto(ids, ownerId), Json, Ct);

    private static Task<Guid?> OwnerOfAsync(ReceiptApiFactory factory, Guid id) =>
        QueryAsync(factory, db => db.Receipts.Where(r => r.Id == id).Select(r => r.OwnerId).SingleAsync(Ct));

    private static async Task WaitForStatusAsync(HttpClient client, Guid id, ReceiptStatus status)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var receipt = (await client.GetFromJsonAsync<ReceiptDetailDto>($"/api/receipts/{id}", Json, Ct))!;
            if (receipt.Status == status)
            {
                return;
            }

            await Task.Delay(50, Ct);
        }

        throw new TimeoutException($"Receipt {id} did not reach {status} within 10 seconds.");
    }

    private static async Task<ReceiptDetailDto> WaitForResultAsync(HttpClient client, Guid id)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var receipt = (await client.GetFromJsonAsync<ReceiptDetailDto>($"/api/receipts/{id}", Json, Ct))!;
            if (receipt.Status is not (ReceiptStatus.Queued or ReceiptStatus.Processing))
            {
                return receipt;
            }

            await Task.Delay(50, Ct);
        }

        throw new TimeoutException($"Receipt {id} was not extracted within 10 seconds.");
    }

    /// <summary>A receipt from before there were users.</summary>
    private static Task<Guid> AddUnownedReceiptAsync(ReceiptApiFactory factory) => QueryAsync(factory, async db =>
    {
        var receipt = new Receipt
        {
            OriginalFileName = "old.jpg",
            StoredFileName = "old.jpg",
            ContentType = "image/jpeg",
            Status = ReceiptStatus.Completed,
        };
        db.Receipts.Add(receipt);
        await db.SaveChangesAsync(Ct);
        return receipt.Id;
    });

    private static async Task<T> QueryAsync<T>(ReceiptApiFactory factory, Func<AppDbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}
