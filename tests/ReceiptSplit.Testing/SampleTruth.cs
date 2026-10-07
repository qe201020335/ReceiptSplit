using System.Globalization;
using System.Text.Json;

namespace ReceiptSplit.Testing;

public sealed record TruthReceipt(IReadOnlyList<decimal> Amounts, decimal Subtotal, decimal Tax, decimal Total, DateOnly Date);

/// <summary>Known-correct answers for the sample photos in samples/.</summary>
public static class SampleTruth
{
    public static TruthReceipt For(string photoOrOutputPath) =>
        photoOrOutputPath.Contains("receipt2") || photoOrOutputPath.StartsWith("tnt") ? TnT() : Costco();

    /// <summary>samples/receipt.json: Costco's own receipt data for samples/receipt.jpg.</summary>
    public static TruthReceipt Costco()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(TestPaths.Samples, "receipt.json")));
        var receipt = document.RootElement
            .GetProperty("data").GetProperty("receiptsWithCounts").GetProperty("receipts")[0];
        return new TruthReceipt(
            receipt.GetProperty("itemArray").EnumerateArray().Select(i => i.GetProperty("amount").GetDecimal()).ToList(),
            receipt.GetProperty("subTotal").GetDecimal(),
            receipt.GetProperty("taxes").GetDecimal(),
            receipt.GetProperty("total").GetDecimal(),
            DateOnly.Parse(receipt.GetProperty("transactionDate").GetString()!, CultureInfo.InvariantCulture));
    }

    /// <summary>samples/receipt2_truth.json: hand transcription of samples/receipt2.jpg, confirmed against the paper.</summary>
    public static TruthReceipt TnT()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(TestPaths.Samples, "receipt2_truth.json")));
        var root = document.RootElement;
        return new TruthReceipt(
            root.GetProperty("items").EnumerateArray().Select(i => Money(i.GetProperty("amount"))).ToList(),
            Money(root.GetProperty("subtotal")),
            Money(root.GetProperty("tax")),
            Money(root.GetProperty("total")),
            DateOnly.Parse(root.GetProperty("date").GetString()![..10], CultureInfo.InvariantCulture));
    }

    private static decimal Money(JsonElement value) => decimal.Parse(value.GetString()!, CultureInfo.InvariantCulture);
}
