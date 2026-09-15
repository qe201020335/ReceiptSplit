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

    [Fact]
    public async Task Upload_extract_rerun_and_delete()
    {
        await using var factory = new ReceiptApiFactory();
        using var client = factory.CreateClient();
        var photo = TestImages.Create(64, 48, MagickFormat.Jpeg);

        factory.Llm.Block();
        using var upload = await client.PostAsync("/api/receipts", PhotoForm(photo, "lunch.jpg"));
        Assert.Equal(HttpStatusCode.Accepted, upload.StatusCode);
        var queued = (await upload.Content.ReadFromJsonAsync<ReceiptQueuedDto>(Json))!;
        Assert.Equal(ReceiptStatus.Queued, queued.Status);
        Assert.Equal($"/api/receipts/{queued.Id}", upload.Headers.Location?.AbsolutePath);

        using (var busy = await client.PostAsync($"/api/receipts/{queued.Id}/extract", null))
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
        Assert.Equal((7.48m, 0.71m, 8.19m), (receipt.Subtotal!.Value, receipt.Tax!.Value, receipt.Total!.Value));
        Assert.Equal(
            new[]
            {
                new ReceiptLineDto(0, "BANANAS", null, 1.25m, 1.99m, null),
                new ReceiptLineDto(1, "MILK 2L", "4011", 1m, 5.49m, "H"),
            },
            receipt.Lines);
        Assert.Equal(new ReceiptChecksDto(7.48m, LinesMatchSubtotal: true, TotalMatches: true), receipt.Checks);
        Assert.Equal(("fake-model", 64, 48), (receipt.Extraction?.Model, receipt.Extraction?.SentImageWidth, receipt.Extraction?.SentImageHeight));
        Assert.Equal(photo, Assert.Single(factory.Llm.Requests).Data);

        Assert.Equal(photo, await client.GetByteArrayAsync($"/api/receipts/{queued.Id}/image"));
        var summaries = await client.GetFromJsonAsync<List<ReceiptSummaryDto>>("/api/receipts", Json);
        Assert.Equal((queued.Id, 8.19m), (Assert.Single(summaries!).Id, summaries![0].Total!.Value));

        using (var rerun = await client.PostAsync($"/api/receipts/{queued.Id}/extract", null))
        {
            Assert.Equal(HttpStatusCode.Accepted, rerun.StatusCode);
        }

        receipt = await WaitForResultAsync(client, queued.Id);
        Assert.Equal(ReceiptStatus.Completed, receipt.Status);
        Assert.Equal(2, receipt.Lines.Count);
        Assert.Equal(2, factory.Llm.Requests.Count);

        using (var delete = await client.DeleteAsync($"/api/receipts/{queued.Id}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        }

        using var gone = await client.GetAsync($"/api/receipts/{queued.Id}");
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

        using var response = await client.PostAsync("/api/receipts", PhotoForm("not a photo"u8.ToArray(), "notes.txt"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(Directory.GetFiles(Path.Combine(factory.StorageRoot, "uploads")));
        Assert.Empty((await client.GetFromJsonAsync<List<ReceiptSummaryDto>>("/api/receipts", Json))!);
    }

    [Fact]
    public async Task Unknown_receipts_return_not_found()
    {
        await using var factory = new ReceiptApiFactory();
        using var client = factory.CreateClient();
        var id = Guid.CreateVersion7();

        using var get = await client.GetAsync($"/api/receipts/{id}");
        using var image = await client.GetAsync($"/api/receipts/{id}/image");
        using var extract = await client.PostAsync($"/api/receipts/{id}/extract", null);
        using var delete = await client.DeleteAsync($"/api/receipts/{id}");

        Assert.All([get, image, extract, delete], r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
    }

    private static async Task<ReceiptDetailDto> UploadAndWaitAsync(HttpClient client, byte[] photo)
    {
        using var upload = await client.PostAsync("/api/receipts", PhotoForm(photo, "receipt.jpg"));
        Assert.Equal(HttpStatusCode.Accepted, upload.StatusCode);
        var queued = (await upload.Content.ReadFromJsonAsync<ReceiptQueuedDto>(Json))!;
        return await WaitForResultAsync(client, queued.Id);
    }

    private static async Task<ReceiptDetailDto> WaitForResultAsync(HttpClient client, Guid id)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var receipt = (await client.GetFromJsonAsync<ReceiptDetailDto>($"/api/receipts/{id}", Json))!;
            if (receipt.Status is not (ReceiptStatus.Queued or ReceiptStatus.Processing))
            {
                return receipt;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"Receipt {id} was not extracted within 10 seconds.");
    }

    private static MultipartFormDataContent PhotoForm(byte[] data, string fileName)
    {
        var file = new ByteArrayContent(data);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return new MultipartFormDataContent { { file, "file", fileName } };
    }
}
