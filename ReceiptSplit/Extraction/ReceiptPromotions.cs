using System.Text.RegularExpressions;

namespace ReceiptSplit.Extraction;

/// <summary>One extracted line after promotions have been folded in: the price paid, and what was taken off.</summary>
public sealed record BakedLine(ParsedLine Line, decimal Discount);

/// <summary>
/// Costco prints a promotion as its own negative line under the item it discounts: "TPD/339054" naming the
/// item number in Canada, "/1218574" in the US, and "TPD/CANDY" naming only what kind of deal it was. Those
/// lines are folded into their item, so a receipt holds one line per item at the price paid, and the tax
/// check sees the discounted price.
/// </summary>
public static partial class ReceiptPromotions
{
    [GeneratedRegex(@"^\s*(?:TPD)?\s*/\s*(\S.*?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex PromotionName();

    [GeneratedRegex(@"^\d+$")]
    private static partial Regex ItemNumber();

    /// <summary>Whether a line is a promotion rather than something bought.</summary>
    public static bool IsPromotion(string name, decimal amount) =>
        amount < 0 && PromotionName().IsMatch(name);

    /// <summary>
    /// The item number a promotion line names, or null when it is not a promotion or names a kind of deal
    /// ("TPD/CANDY") instead. The number a promotion is printed with is its own, not the discounted item's.
    /// </summary>
    public static string? DiscountedCode(string name, decimal amount) =>
        IsPromotion(name, amount) && PromotionName().Match(name).Groups[1].Value is { } target && ItemNumber().IsMatch(target)
            ? target
            : null;

    /// <summary>
    /// Deducts each promotion from its item: the closest line above carrying the item number it names, or
    /// simply the closest item above when it names none. A promotion with no item to deduct from, because the
    /// number was misread or the item is off the photo, is kept as an ordinary discount line.
    /// </summary>
    public static IReadOnlyList<BakedLine> Bake(IEnumerable<ParsedLine> lines)
    {
        var baked = new List<BakedLine>();
        foreach (var line in lines)
        {
            var code = DiscountedCode(line.Name, line.Amount);
            var item = !IsPromotion(line.Name, line.Amount)
                ? -1
                : baked.FindLastIndex(candidate =>
                    candidate.Line.Amount >= 0 && (code is null || candidate.Line.Code == code));

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
