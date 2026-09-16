using System.Text.RegularExpressions;

namespace ReceiptSplit.Extraction;

/// <summary>One extracted line after promotions have been folded in: the price paid, and what was taken off.</summary>
public sealed record BakedLine(ParsedLine Line, decimal Discount);

/// <summary>
/// Costco prints a promotion as its own negative line naming the item it discounts: "TPD/339054" under
/// item 339054 in Canada, "/1218574" in the US. Those lines are folded into the item they name, so a
/// receipt holds one line per item at the price paid, and the tax check sees the discounted price.
/// </summary>
public static partial class ReceiptPromotions
{
    [GeneratedRegex(@"^\s*(?:TPD)?\s*/\s*(\d+)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex PromotionName();

    /// <summary>The item number a promotion line discounts, or null when the line is not a promotion.</summary>
    public static string? DiscountedCode(string name, decimal amount) =>
        amount < 0 && PromotionName().Match(name) is { Success: true } match ? match.Groups[1].Value : null;

    /// <summary>
    /// Deducts each promotion from the item it names, which is the closest line above it carrying that item
    /// number, and drops the promotion line. A promotion whose item is missing, because the number was misread
    /// or the item is off the photo, is kept as an ordinary discount line.
    /// </summary>
    public static IReadOnlyList<BakedLine> Bake(IEnumerable<ParsedLine> lines)
    {
        var baked = new List<BakedLine>();
        foreach (var line in lines)
        {
            var code = DiscountedCode(line.Name, line.Amount);
            var item = code is null
                ? -1
                : baked.FindLastIndex(candidate => candidate.Line.Code == code && candidate.Line.Amount >= 0);

            if (item < 0)
            {
                baked.Add(new BakedLine(line, 0m));
                continue;
            }

            var discounted = baked[item];
            baked[item] = discounted with
            {
                Line = discounted.Line with { Amount = discounted.Line.Amount + line.Amount },
                Discount = discounted.Discount + line.Amount,
            };
        }

        return baked;
    }
}
