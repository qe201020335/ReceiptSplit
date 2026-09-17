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
                new ReceiptLineDto(0, "BANANAS", null, 1.25m, 1.99m, Discount: 0m, null, IsTaxed: false),
                new ReceiptLineDto(1, "MILK 2L", "4011", 1m, 5.49m, Discount: 0m, "H", IsTaxed: true),
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
    public async Task Promotions_are_linked_and_taxed_with_the_item_they_discount()
    {
        await using var factory = new ReceiptApiFactory();
        factory.Llm.Content = FakeLlamaClient.PromotionOutput;
        using var client = factory.CreateClient();

        var receipt = await UploadAndWaitAsync(client, TestImages.Create(64, 48, MagickFormat.Jpeg));

        Assert.Equal(ReceiptStatus.Completed, receipt.Status);
        // The promotion is folded into the taxed item above it, so it is taxed with it: 10.99 at 13% is 1.43.
        Assert.Equal(["WAGON", "KS ORG OAT"], receipt.Lines.Select(l => l.Name));
        Assert.Equal((10.99m, -3.00m, true), (receipt.Lines[0].Amount, receipt.Lines[0].Discount, receipt.Lines[0].IsTaxed));
        Assert.Equal((0m, false), (receipt.Lines[1].Discount, receipt.Lines[1].IsTaxed));
        Assert.Equal((10.99m, 1.43m, true), (receipt.Checks!.TaxedSum, receipt.Checks.ExpectedTax, receipt.Checks.TaxMatches));
    }

    [Fact]
    public async Task Hand_corrections_keep_the_discount_and_reject_a_positive_one()
    {
        await using var factory = new ReceiptApiFactory();
        factory.Llm.Content = FakeLlamaClient.PromotionOutput;
        using var client = factory.CreateClient();
        var receipt = await UploadAndWaitAsync(client, TestImages.Create(64, 48, MagickFormat.Jpeg));

        var lines = receipt.Lines
            .Select(l => new ReceiptLineEditDto(l.Name, l.Code, l.Quantity, l.Amount, l.Discount, l.TaxCode, l.IsTaxed))
            .ToList();
        var edit = new ReceiptEditDto(receipt.StoreName, receipt.PurchaseDate, 23.98m, 1.43m, 25.41m, lines);

        var corrected = await PutEditAsync(client, receipt.Id, edit);
        Assert.Equal([-3.00m, 0m], corrected.Lines.Select(l => l.Discount));
        Assert.Equal(ReceiptStatus.Completed, corrected.Status);

        using var positiveDiscount = await client.PutAsJsonAsync(
            $"/api/receipts/{receipt.Id}",
            edit with { Lines = [.. lines.Select((line, index) => index == 0 ? line with { Discount = 3.00m } : line)] },
            Json,
            Ct);

        Assert.Equal(HttpStatusCode.BadRequest, positiveDiscount.StatusCode);
    }

    [Fact]
    public async Task Hand_corrections_replace_the_lines_and_re_check_the_receipt()
    {
        await using var factory = new ReceiptApiFactory();
        // A misread amount: the lines no longer add up to the printed subtotal.
        factory.Llm.Content = FakeLlamaClient.ValidOutput.Replace("\"5.49\"", "\"5.09\"");
        using var client = factory.CreateClient();
        var receipt = await UploadAndWaitAsync(client, TestImages.Create(64, 48, MagickFormat.Jpeg));
        Assert.Equal(ReceiptStatus.NeedsReview, receipt.Status);
        Assert.Null(receipt.EditedAt);

        var edit = new ReceiptEditDto(
            "Corner Market",
            new DateOnly(2026, 9, 14),
            Subtotal: 7.48m,
            Tax: 0.71m,
            Total: 8.19m,
            [
                new ReceiptLineEditDto("BANANAS", null, 1.25m, 1.99m, Discount: 0m, null, IsTaxed: false),
                new ReceiptLineEditDto("MILK 2L", "4011", 1m, 5.49m, Discount: 0m, "H", IsTaxed: true),
            ]);
        var corrected = await PutEditAsync(client, receipt.Id, edit);

        Assert.Equal(ReceiptStatus.Completed, corrected.Status);
        Assert.NotNull(corrected.EditedAt);
        Assert.Equal(5.49m, corrected.Lines[1].Amount);
        Assert.True(corrected.Checks!.LinesMatchSubtotal);
        Assert.True(corrected.Checks.TaxMatches);
        // The photo is untouched, so the raw model output stays available next to the correction.
        Assert.Equal(factory.Llm.Content, corrected.Extraction?.ModelOutput);
        Assert.Single(factory.Llm.Requests);
    }

    [Fact]
    public async Task Hand_corrections_are_checked_at_the_precision_they_are_stored()
    {
        await using var factory = new ReceiptApiFactory();
        using var client = factory.CreateClient();
        var receipt = await UploadAndWaitAsync(client, TestImages.Create(64, 48, MagickFormat.Jpeg));

        // 1.994 is stored as 1.99, which matches the 7.48 subtotal; the status must not be decided on the extra digit.
        var corrected = await PutEditAsync(client, receipt.Id, new ReceiptEditDto(
            receipt.StoreName,
            receipt.PurchaseDate,
            Subtotal: 7.48m,
            Tax: 0.714m,
            Total: 8.19m,
            [
                new ReceiptLineEditDto("BANANAS", null, 1.25m, 1.994m, Discount: 0m, null, IsTaxed: false),
                new ReceiptLineEditDto("MILK 2L", "4011", 1m, 5.49m, Discount: 0m, "H", IsTaxed: true),
            ]));

        Assert.Equal(ReceiptStatus.Completed, corrected.Status);
        Assert.Equal((1.99m, 0.71m), (corrected.Lines[0].Amount, corrected.Tax!.Value));
        Assert.True(corrected.Checks!.LinesMatchSubtotal);

        var rated = await PatchTaxRateAsync(client, receipt.Id, 13.0004m);
        Assert.Equal((13m, ReceiptStatus.Completed), (rated.TaxRatePercent, rated.Status));
    }

    [Fact]
    public async Task Extracted_totals_are_checked_at_the_precision_they_are_stored()
    {
        await using var factory = new ReceiptApiFactory();
        factory.Llm.Content = FakeLlamaClient.ValidOutput.Replace("\"7.48\"", "\"7.481\"");
        using var client = factory.CreateClient();

        var receipt = await UploadAndWaitAsync(client, TestImages.Create(64, 48, MagickFormat.Jpeg));

        Assert.Equal(7.48m, receipt.Subtotal);
        Assert.Equal(ReceiptStatus.Completed, receipt.Status);
    }

    [Fact]
    public async Task Hand_corrections_renumber_lines_and_clear_the_extraction_error()
    {
        await using var factory = new ReceiptApiFactory();
        factory.Llm.Content = "I can't read this receipt.";
        using var client = factory.CreateClient();
        var receipt = await UploadAndWaitAsync(client, TestImages.Create(64, 48, MagickFormat.Jpeg));
        Assert.Equal(ReceiptStatus.Failed, receipt.Status);
        Assert.NotNull(receipt.Error);

        var typedByHand = await PutEditAsync(client, receipt.Id, new ReceiptEditDto(
            "  Corner Market  ",
            new DateOnly(2026, 9, 14),
            Subtotal: 3.00m,
            Tax: null,
            Total: 3.00m,
            [
                new ReceiptLineEditDto("  APPLES  ", "  ", 1m, 1.00m, Discount: 0m, "  ", IsTaxed: false),
                new ReceiptLineEditDto("PEARS", null, 1m, 2.00m, Discount: 0m, null, IsTaxed: false),
            ]));

        Assert.Equal(ReceiptStatus.Completed, typedByHand.Status);
        Assert.Null(typedByHand.Error);
        Assert.Equal("Corner Market", typedByHand.StoreName);
        Assert.Equal([0, 1], typedByHand.Lines.Select(l => l.Position));
        Assert.Equal(("APPLES", null, null), (typedByHand.Lines[0].Name, typedByHand.Lines[0].Code, typedByHand.Lines[0].TaxCode));
    }

    [Fact]
    public async Task Re_running_extraction_discards_hand_corrections()
    {
        await using var factory = new ReceiptApiFactory();
        using var client = factory.CreateClient();
        var receipt = await UploadAndWaitAsync(client, TestImages.Create(64, 48, MagickFormat.Jpeg));
        await PutEditAsync(client, receipt.Id, new ReceiptEditDto(
            "By hand", null, 1.00m, null, 1.00m, [new ReceiptLineEditDto("TYPED", null, 1m, 1.00m, Discount: 0m, null, IsTaxed: false)]));

        using (var rerun = await client.PostAsync($"/api/receipts/{receipt.Id}/extract", null, Ct))
        {
            Assert.Equal(HttpStatusCode.Accepted, rerun.StatusCode);
        }

        var extracted = await WaitForResultAsync(client, receipt.Id);

        Assert.Null(extracted.EditedAt);
        Assert.Equal("Corner Market", extracted.StoreName);
        Assert.Equal(2, extracted.Lines.Count);
    }

    [Fact]
    public async Task Refuses_edits_while_the_model_is_reading_and_rejects_empty_ones()
    {
        await using var factory = new ReceiptApiFactory();
        using var client = factory.CreateClient();
        factory.Llm.Block();
        using var upload = await client.PostAsync("/api/receipts", PhotoForm(TestImages.Create(64, 48, MagickFormat.Jpeg), "receipt.jpg"), Ct);
        var queued = (await upload.Content.ReadFromJsonAsync<ReceiptQueuedDto>(Json, Ct))!;
        var oneLine = new ReceiptEditDto("Corner Market", null, 1.00m, null, 1.00m,
            [new ReceiptLineEditDto("TYPED", null, 1m, 1.00m, Discount: 0m, null, IsTaxed: false)]);

        using (var busy = await client.PutAsJsonAsync($"/api/receipts/{queued.Id}", oneLine, Json, Ct))
        {
            Assert.Equal(HttpStatusCode.Conflict, busy.StatusCode);
        }

        factory.Llm.Release();
        await WaitForResultAsync(client, queued.Id);

        using var noLines = await client.PutAsJsonAsync($"/api/receipts/{queued.Id}", oneLine with { Lines = [] }, Json, Ct);
        using var noName = await client.PutAsJsonAsync(
            $"/api/receipts/{queued.Id}",
            oneLine with { Lines = [new ReceiptLineEditDto(" ", null, 1m, 1.00m, Discount: 0m, null, IsTaxed: false)] },
            Json,
            Ct);

        Assert.All([noLines, noName], r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
        Assert.Null((await client.GetFromJsonAsync<ReceiptDetailDto>($"/api/receipts/{queued.Id}", Json, Ct))!.EditedAt);
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
    public async Task A_model_server_error_fails_with_a_plain_message()
    {
        await using var factory = new ReceiptApiFactory();
        factory.Llm.Failure = new HttpRequestException(
            "llama-server returned 500: {\"error\":\"out of memory at /models/qwen.gguf\"}", null, HttpStatusCode.InternalServerError);
        using var client = factory.CreateClient();

        var receipt = await UploadAndWaitAsync(client, TestImages.Create(64, 48, MagickFormat.Jpeg));

        Assert.Equal(ReceiptStatus.Failed, receipt.Status);
        Assert.Equal("The model server returned an error (500). Try running extraction again.", receipt.Error);
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
    public async Task Serves_a_jpeg_copy_of_photos_browsers_cannot_display()
    {
        await using var factory = new ReceiptApiFactory();
        using var client = factory.CreateClient();
        var receipt = await UploadAndWaitAsync(client, TestImages.Create(64, 48, MagickFormat.Tiff));

        foreach (var _ in new[] { "converted", "cached" })
        {
            using var image = await client.GetAsync($"/api/receipts/{receipt.Id}/image", Ct);
            Assert.Equal("image/jpeg", image.Content.Headers.ContentType?.MediaType);
            var info = new MagickImageInfo(await image.Content.ReadAsByteArrayAsync(Ct));
            Assert.Equal((MagickFormat.Jpeg, 64u, 48u), (info.Format, info.Width, info.Height));
        }

        var uploads = Path.Combine(factory.StorageRoot, "uploads");
        Assert.Equal(2, Directory.GetFiles(uploads).Length);

        using var delete = await client.DeleteAsync($"/api/receipts/{receipt.Id}", Ct);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Empty(Directory.GetFiles(uploads));
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
        using var put = await client.PutAsJsonAsync(
            $"/api/receipts/{id}",
            new ReceiptEditDto("Corner Market", null, 1.00m, null, 1.00m, [new ReceiptLineEditDto("TYPED", null, 1m, 1.00m, 0m, null, false)]),
            Json,
            Ct);
        using var delete = await client.DeleteAsync($"/api/receipts/{id}", Ct);

        Assert.All([get, image, extract, patch, put, delete], r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
    }

    private static async Task<ReceiptDetailDto> PutEditAsync(HttpClient client, Guid id, ReceiptEditDto edit)
    {
        using var response = await client.PutAsJsonAsync($"/api/receipts/{id}", edit, Json, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ReceiptDetailDto>(Json, Ct))!;
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
