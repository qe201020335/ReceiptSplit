using System.Text.Json;
using ReceiptSplit.Extraction;
using ReceiptSplit.Testing;

namespace ReceiptSplit.Receipts.Tests;

/// <summary>
/// Runs the real model outputs in samples/ through the parser, the promotions and the checks, as an extraction
/// does, so it sits with the receipt rules rather than with the parser's own tests.
/// </summary>
public class SampleOutputTests
{
    public static TheoryData<string> SampleOutputs()
    {
        var directory = Path.Combine(TestPaths.Samples, "outputs");
        if (!Directory.Exists(directory))
        {
            return [""];
        }

        return new TheoryData<string>(Directory
            .EnumerateFiles(directory, "*.json", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(directory, path))
            .Order());
    }

    /// <summary>Real outputs from the 2026-09-14 benchmark runs, including the structurally broken ones.</summary>
    [Theory]
    [MemberData(nameof(SampleOutputs))]
    public void Real_model_outputs_match_the_printed_receipt(string outputFile)
    {
        Assert.SkipWhen(outputFile == "", "samples/outputs is not available.");

        using var response = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(TestPaths.Samples, "outputs", outputFile)));
        var content = response.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()!;
        var truth = SampleTruth.For(outputFile);

        var parsed = ReceiptOutputParser.Parse(content);

        Assert.Equal(truth.Amounts.Order(), parsed.Lines.Select(l => l.Amount).Order());
        Assert.Equal(truth.Subtotal, parsed.Subtotal);
        Assert.Equal(truth.Tax, parsed.Tax);
        Assert.Equal(truth.Total, parsed.Total);
        // Folding Costco's promotions into their item keeps the receipt's total intact.
        var baked = ReceiptPromotions.Bake(parsed.Lines);
        Assert.Equal(parsed.Lines.Sum(l => l.Amount), baked.Sum(b => b.Line.Amount));
        Assert.All(baked, line => Assert.True(line.Discount <= 0m));

        // Ontario 13%: the printed tax on both sample stores is exactly 13% of their taxed lines.
        Assert.True(ReceiptChecks.Evaluate(
            baked.Select(b => (b.Line.Amount, ReceiptTaxCodes.IsTaxed(b.Line.TaxCode))),
            parsed.Subtotal,
            parsed.Tax,
            parsed.Total,
            taxRatePercent: 13m,
            parsed.DiscountPercent).Passed);
    }
}
