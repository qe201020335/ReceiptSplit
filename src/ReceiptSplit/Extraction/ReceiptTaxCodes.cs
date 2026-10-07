using System.Text.RegularExpressions;
using ReceiptSplit.Data;

namespace ReceiptSplit.Extraction;

/// <summary>
/// Reads the tax code letters printed next to an amount. Costco Canada prints "H" on taxed lines and nothing
/// on the rest, T&amp;T prints "G P", and Costco US prints "A" on taxable lines and "E" on exempt ones.
/// Codes are matched whole, so an exempt marker such as "E", "N" or "NT" is not mistaken for a tax code.
/// </summary>
public static partial class ReceiptTaxCodes
{
    private static readonly HashSet<string> Taxed = new(StringComparer.OrdinalIgnoreCase)
    {
        "A", "G", "H", "P", "Q", "S", "T", "GST", "HST", "PST", "QST", "TPS", "TVQ",
    };

    [GeneratedRegex("[A-Za-z]+")]
    private static partial Regex LetterGroups();

    /// <summary>Whether a printed tax code means sales tax was charged on the line.</summary>
    public static bool IsTaxed(string? code) =>
        code is not null && LetterGroups().Matches(code).Any(group => Taxed.Contains(group.Value));

    /// <summary>
    /// Some receipts charge tax but print no code on any line, either because every item is taxed or because
    /// the codes were not read. Taxing everything then matches the printed tax; leaving it as read would make
    /// the expected tax zero and send every such receipt to review.
    /// </summary>
    public static void AssumeAllTaxedWhenNoneAre(IReadOnlyCollection<ReceiptLine> lines, decimal? tax)
    {
        if (tax is not > 0m || lines.Count == 0 || lines.Any(line => line.IsTaxed))
        {
            return;
        }

        foreach (var line in lines)
        {
            line.IsTaxed = true;
        }
    }
}
