using Microsoft.Extensions.DependencyInjection;
using ReceiptSplit.Extraction;
using ReceiptSplit.Tests.Support;

namespace ReceiptSplit.Tests;

/// <summary>
/// Sends the sample photos to the real llama-server configured for the app (appsettings + user secrets).
/// Opt-in because it loads a model on a shared server:
/// RECEIPTSPLIT_LIVE_TESTS=1 dotnet test --filter-class ReceiptSplit.Tests.LiveExtractionTests
/// </summary>
public class LiveExtractionTests
{
    [Theory]
    [InlineData("receipt.jpg")]
    [InlineData("receipt2.jpg")]
    public async Task Extracts_sample_receipt_photo(string photo)
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("RECEIPTSPLIT_LIVE_TESTS") == "1",
            "Set RECEIPTSPLIT_LIVE_TESTS=1 to call the real llama-server.");
        Assert.SkipUnless(TestPaths.SamplesAvailable, "samples/ is not available.");

        await using var factory = new ReceiptApiFactory(useFakeLlm: false);
        using var scope = factory.Services.CreateScope();
        var preparer = scope.ServiceProvider.GetRequiredService<ImagePreparer>();
        var llm = scope.ServiceProvider.GetRequiredService<ILlamaClient>();

        var image = preparer.Prepare(await File.ReadAllBytesAsync(Path.Combine(TestPaths.Samples, photo)));
        var completion = await llm.ExtractReceiptAsync(image, TestContext.Current.CancellationToken);
        var parsed = ReceiptOutputParser.Parse(completion.Content);

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{photo}: sent {image.Width}×{image.Height} (converted: {image.Converted}), " +
            $"{completion.PromptTokens} prompt + {completion.CompletionTokens} completion tokens in {completion.Elapsed.TotalSeconds:F1} s\n" +
            completion.Content);

        var truth = SampleTruth.For(photo);
        Assert.Equal(truth.Amounts.Order(), parsed.Lines.Select(l => l.Amount).Order());
        Assert.Equal(truth.Subtotal, parsed.Subtotal);
        Assert.Equal(truth.Tax, parsed.Tax);
        Assert.Equal(truth.Total, parsed.Total);
        Assert.Equal(truth.Date, parsed.PurchaseDate);
        Assert.NotNull(parsed.StoreName);
    }
}
