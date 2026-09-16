using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ImageMagick;
using ReceiptSplit.Contracts;
using ReceiptSplit.Data;
using ReceiptSplit.Tests.Support;

namespace ReceiptSplit.Tests;

public class ReceiptsApiTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Upload_extract_rerun_and_delete()
    {
        await using var factory = new ReceiptApiFactory();
        using var client = factory.CreateClient();
        var photo = TestImages.Create(64, 48, MagickFormat.Jpeg);

        factory.Llm.Block();
        using var upload = await client.PostAsync("/api/receipts", PhotoForm(photo, "lunch.jpg"), Ct);
        Assert.Equal(HttpStatusCode.Accepted, upload.StatusCode);
        var queued = (await upload.Content.ReadFromJsonAsync<ReceiptQueuedDto>(Json, Ct))!;
        Assert.Equal(ReceiptStatus.Queued, queued.Status);
        Assert.Equal($"/api/receipts/{queued.Id}", upload.Headers.Location?.AbsolutePath);

        using (var busy = await client.PostAsync($"/api/receipts/{queued.Id}/extract", null, Ct))
        {
            Assert.Equal(HttpStatusCode.Conflict, busy.StatusCode);
        }

        factory.Llm.Release();
        var receipt = await WaitForResultAsync(client, queued.Id);
        Assert.Equal(ReceiptStatus.Completed, receipt.Status);
        Assert.Null(receipt.Error);
        Assert.Equal("lunch.jpg", receipt.OriginalFileName);
        Assert.Equal("Corner Market", receipt.StoreName);
        Assert.Equal(new DateOnly(2026, 9, 14), receipt.PurchaseDate);
        Assert.Equal(13m, receipt.TaxRatePercent);
        Assert.Equal((7.48m, 0.71m, 8.19m), (receipt.Subtotal!.Value, receipt.Tax!.Value, receipt.Total!.Value));
        Assert.Equal(
            new[]
            {
                new ReceiptLineDto(0, "BANANAS", null, 1.25m, 1.99m, null, IsTaxed: false),
                new ReceiptLineDto(1, "MILK 2L", "4011", 1m, 5.49m, "H", IsTaxed: true),
            },
            receipt.Lines);
        Assert.Equal(
            new ReceiptChecksDto(7.48m, LinesMatchSubtotal: true, TotalMatches: true, TaxedSum: 5.49m, ExpectedTax: 0.71m, TaxMatches: true),
            receipt.Checks);
        Assert.Equal(("fake-model", 64, 48), (receipt.Extraction?.Model, receipt.Extraction?.SentImageWidth, receipt.Extraction?.SentImageHeight));
        Assert.Equal(photo, Assert.Single(factory.Llm.Requests).Data);

        Assert.Equal(photo, await client.GetByteArrayAsync($"/api/receipts/{queued.Id}/image", Ct));
        var summaries = await client.GetFromJsonAsync<List<ReceiptSummaryDto>>("/api/receipts", Json, Ct);
        Assert.Equal((queued.Id, 8.19m), (Assert.Single(summaries!).Id, summaries![0].Total!.Value));

        using (var rerun = await client.PostAsync($"/api/receipts/{queued.Id}/extract", null, Ct))
        {
            Assert.Equal(HttpStatusCode.Accepted, rerun.StatusCode);
        }

        receipt = await WaitForResultAsync(client, queued.Id);
        Assert.Equal(ReceiptStatus.Completed, receipt.Status);
        Assert.Equal(2, receipt.Lines.Count);
        Assert.Equal(2, factory.Llm.Requests.Count);

        using (var delete = await client.DeleteAsync($"/api/receipts/{queued.Id}", Ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        }

        using var gone = await client.GetAsync($"/api/receipts/{queued.Id}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        Assert.Empty(Directory.GetFiles(Path.Combine(factory.StorageRoot, "uploads")));
    }

    [Fact]
    public async Task Totals_that_disagree_with_the_lines_need_review()
    {
        await using var factory = new ReceiptApiFactory();
        factory.Llm.Content = FakeLlamaClient.ValidOutput.Replace("\"s\": \"7.48\"", "\"s\": \"7.84\"");
        using var client = factory.CreateClient();

        var receipt = await UploadAndWaitAsync(client, TestImages.Create(64, 48, MagickFormat.Jpeg));

        Assert.Equal(ReceiptStatus.NeedsReview, receipt.Status);
        Assert.False(receipt.Checks!.LinesMatchSubtotal);
        Assert.Equal(2, receipt.Lines.Count);
    }

    [Fact]
    public async Task Tax_that_does_not_match_the_taxed_lines_needs_review()
    {
        await using var factory = new ReceiptApiFactory();
        using var client = factory.CreateClient();

        // The fake receipt's 5.49 taxed line carries 0.71 of tax, which is 13%, not Alberta's 5%.
        var receipt = await UploadAndWaitAsync(client, TestImages.Create(64, 48, MagickFormat.Jpeg), taxRatePercent: 5m);

        Assert.Equal(ReceiptStatus.NeedsReview, receipt.Status);
        Assert.Equal(5m, receipt.TaxRatePercent);
        Assert.Equal((5.49m, 0.27m, false), (receipt.Checks!.TaxedSum, receipt.Checks.ExpectedTax, receipt.Checks.TaxMatches));
        Assert.True(receipt.Checks.LinesMatchSubtotal);
    }

    [Fact]
    public async Task Changing_the_tax_rate_rechecks_the_receipt_without_the_model()
    {
        await using var factory = new ReceiptApiFactory();
        using var client = factory.CreateClient();
        var receipt = await UploadAndWaitAsync(client, TestImages.Create(64, 48, MagickFormat.Jpeg));
        Assert.Equal(ReceiptStatus.Completed, receipt.Status);

        var wrongRate = await PatchTaxRateAsync(client, receipt.Id, 5m);

        Assert.Equal(ReceiptStatus.NeedsReview, wrongRate.Status);
        Assert.Equal(5m, wrongRate.TaxRatePercent);
        Assert.False(wrongRate.Checks!.TaxMatches);

        var backToOntario = await PatchTaxRateAsync(client, receipt.Id, 13m);

        Assert.Equal(ReceiptStatus.Completed, backToOntario.Status);
        Assert.Equal((13m, 0.71m, true), (backToOntario.TaxRatePercent, backToOntario.Checks!.ExpectedTax, backToOntario.Checks.TaxMatches));
        Assert.Single(factory.Llm.Requests);
    }

    [Fact]
    public async Task Rejects_tax_rates_outside_the_accepted_range()
    {
        await using var factory = new ReceiptApiFactory();
        using var client = factory.CreateClient();
        var photo = TestImages.Create(64, 48, MagickFormat.Jpeg);

        using var upload = await client.PostAsync("/api/receipts", PhotoForm(photo, "receipt.jpg", taxRatePercent: 45m), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, upload.StatusCode);
        Assert.Empty(Directory.GetFiles(Path.Combine(factory.StorageRoot, "uploads")));

        var receipt = await UploadAndWaitAsync(client, photo);
        using var patch = await client.PatchAsJsonAsync($"/api/receipts/{receipt.Id}", new ReceiptTaxRateDto(-1m), Json, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, patch.StatusCode);
        Assert.Equal(13m, (await client.GetFromJsonAsync<ReceiptDetailDto>($"/api/receipts/{receipt.Id}", Json, Ct))!.TaxRatePercent);
    }

    [Fact]
    public async Task Refuses_to_change_the_tax_rate_while_the_model_is_reading()
    {
        await using var factory = new ReceiptApiFactory();
        using var client = factory.CreateClient();
        factory.Llm.Block();
        using var upload = await client.PostAsync("/api/receipts", PhotoForm(TestImages.Create(64, 48, MagickFormat.Jpeg), "receipt.jpg"), Ct);
        var queued = (await upload.Content.ReadFromJsonAsync<ReceiptQueuedDto>(Json, Ct))!;
        await WaitForStatusAsync(client, queued.Id, ReceiptStatus.Processing);

        using var patch = await client.PatchAsJsonAsync($"/api/receipts/{queued.Id}", new ReceiptTaxRateDto(5m), Json, Ct);

        Assert.Equal(HttpStatusCode.Conflict, patch.StatusCode);
        factory.Llm.Release();
    }

    [Fact]
    public async Task Output_without_lines_fails_and_keeps_the_raw_output()
    {
        await using var factory = new ReceiptApiFactory();
        factory.Llm.Content = "I can't read this receipt.";
        using var client = factory.CreateClient();

        var receipt = await UploadAndWaitAsync(client, TestImages.Create(64, 48, MagickFormat.Png));

        Assert.Equal(ReceiptStatus.Failed, receipt.Status);
        Assert.NotNull(receipt.Error);
        Assert.Null(receipt.Checks);
        Assert.Equal("I can't read this receipt.", receipt.Extraction?.ModelOutput);
    }

    [Fact]
    public async Task Rejects_files_that_are_not_images()
    {
        await using var factory = new ReceiptApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/receipts", PhotoForm("not a photo"u8.ToArray(), "notes.txt"), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(Directory.GetFiles(Path.Combine(factory.StorageRoot, "uploads")));
        Assert.Empty((await client.GetFromJsonAsync<List<ReceiptSummaryDto>>("/api/receipts", Json, Ct))!);
    }

    [Fact]
    public async Task Unknown_receipts_return_not_found()
    {
        await using var factory = new ReceiptApiFactory();
        using var client = factory.CreateClient();
        var id = Guid.CreateVersion7();

        using var get = await client.GetAsync($"/api/receipts/{id}", Ct);
        using var image = await client.GetAsync($"/api/receipts/{id}/image", Ct);
        using var extract = await client.PostAsync($"/api/receipts/{id}/extract", null, Ct);
        using var patch = await client.PatchAsJsonAsync($"/api/receipts/{id}", new ReceiptTaxRateDto(13m), Json, Ct);
        using var delete = await client.DeleteAsync($"/api/receipts/{id}", Ct);

        Assert.All([get, image, extract, patch, delete], r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
    }

    private static async Task<ReceiptDetailDto> PatchTaxRateAsync(HttpClient client, Guid id, decimal taxRatePercent)
    {
        using var response = await client.PatchAsJsonAsync($"/api/receipts/{id}", new ReceiptTaxRateDto(taxRatePercent), Json, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ReceiptDetailDto>(Json, Ct))!;
    }

    private static async Task<ReceiptDetailDto> UploadAndWaitAsync(HttpClient client, byte[] photo, decimal? taxRatePercent = null)
    {
        using var upload = await client.PostAsync("/api/receipts", PhotoForm(photo, "receipt.jpg", taxRatePercent), Ct);
        Assert.Equal(HttpStatusCode.Accepted, upload.StatusCode);
        var queued = (await upload.Content.ReadFromJsonAsync<ReceiptQueuedDto>(Json, Ct))!;
        return await WaitForResultAsync(client, queued.Id);
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

    private static MultipartFormDataContent PhotoForm(byte[] data, string fileName, decimal? taxRatePercent = null)
    {
        var file = new ByteArrayContent(data);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var form = new MultipartFormDataContent { { file, "file", fileName } };
        if (taxRatePercent is { } rate)
        {
            form.Add(new StringContent(rate.ToString(CultureInfo.InvariantCulture)), "taxRatePercent");
        }

        return form;
    }
}
