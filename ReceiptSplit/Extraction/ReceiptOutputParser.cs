using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ReceiptSplit.Extraction;

public sealed record ParsedLine(string Name, string? Code, decimal Quantity, decimal Amount, string? TaxCode);

public sealed record ParsedReceipt(
    IReadOnlyList<ParsedLine> Lines,
    decimal? Subtotal,
    decimal DiscountPercent,
    decimal? Tax,
    decimal? Total,
    string? StoreName,
    DateOnly? PurchaseDate);

/// <summary>
/// Reads the model's row-per-line output. The model gets values right but slips on structure (code fences,
/// a dropped '[', rows wrapped as [[row]], everything in one array), so each row is matched on its own instead
/// of parsing the whole output as JSON. The totals check catches genuine misreads.
/// </summary>
public static partial class ReceiptOutputParser
{
    private const string JsonString = """
        "(?:[^"\\]|\\.)*"
        """;

    /// <summary>
    /// A name, or a list of the name's printed lines (T&amp;T prints a Chinese line under each English one). The code
    /// may be left out entirely; the amount is always quoted, so a quantity is never taken for a code.
    /// </summary>
    private const string RowPattern =
        $$"""
        (?<name>{{JsonString}}|\[\s*{{JsonString}}(?:\s*,\s*{{JsonString}})*\s*\])\s*,\s*(?:(?<code>{{JsonString}}|null|-?\d+)\s*,\s*)?(?<qty>"?-?\d+(?:\.\d+)?"?|null)\s*,\s*"(?<amount>\(?-?\$?-?[\d,]*\.\d{2}-?\)?)"\s*,\s*(?<tax>{{JsonString}}|null)
        """;

    [GeneratedRegex(RowPattern)]
    private static partial Regex RowRegex();

    [GeneratedRegex("""\{[^{}]*"s"\s*:[^{}]*\}""")]
    private static partial Regex TotalsRegex();

    public static ParsedReceipt Parse(string content)
    {
        var lines = new List<ParsedLine>();
        foreach (Match row in RowRegex().Matches(content))
        {
            var amount = ParseMoney(row.Groups["amount"].Value);
            if (amount is null or 0m)
            {
                continue;
            }

            lines.Add(new ParsedLine(
                Name: ReadName(row.Groups["name"].Value),
                Code: row.Groups["code"].Success ? NullIfBlank(ReadToken(row.Groups["code"].Value)) : null,
                Quantity: ParseQuantity(row.Groups["qty"].Value),
                Amount: amount.Value,
                TaxCode: NullIfBlank(ReadToken(row.Groups["tax"].Value))));
        }

        var totals = TotalsRegex().Matches(content).LastOrDefault();
        if (totals is null)
        {
            return new ParsedReceipt(lines, null, 0m, null, null, null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(totals.Value);
            var root = document.RootElement;
            return new ParsedReceipt(
                lines,
                Subtotal: ParseMoney(ReadProperty(root, "s")),
                DiscountPercent: ParsePercent(ReadProperty(root, "d")),
                Tax: ParseMoney(ReadProperty(root, "t")),
                Total: ParseMoney(ReadProperty(root, "T")),
                StoreName: NullIfBlank(ReadProperty(root, "store")),
                PurchaseDate: ParseDate(ReadProperty(root, "date")));
        }
        catch (JsonException)
        {
            return new ParsedReceipt(lines, null, 0m, null, null, null, null);
        }
    }

    /// <summary>Parses "4.99", "$1,234.50", "-3.00", "3.00-" or "(3.00)".</summary>
    public static decimal? ParseMoney(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var value = text.Trim();
        var negative = false;
        if (value.StartsWith('(') && value.EndsWith(')'))
        {
            negative = true;
            value = value[1..^1];
        }

        if (value.EndsWith('-'))
        {
            negative = true;
            value = value[..^1];
        }

        value = value.Replace("$", "").Replace(",", "").Trim();
        if (value.StartsWith('-'))
        {
            negative = true;
            value = value[1..];
        }

        return decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
            ? negative ? -amount : amount
            : null;
    }

    /// <summary>Parses 10, "10" or "10%"; anything else, null included, means no discount.</summary>
    public static decimal ParsePercent(string? text) =>
        decimal.TryParse(text?.Trim().TrimEnd('%').Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture,
            out var percent) && percent < 100m
            ? percent
            : 0m;

    private static decimal ParseQuantity(string token)
    {
        var value = token.Trim('"');
        return decimal.TryParse(value, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out var quantity)
            ? quantity
            : 1m;
    }

    private static DateOnly? ParseDate(string? text) =>
        text is { Length: >= 10 } &&
        DateOnly.TryParseExact(text.Trim()[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    /// <summary>Keeps the first printed line of a name given as a list, which is the one other rows are read as.</summary>
    private static string ReadName(string token)
    {
        if (token.StartsWith('['))
        {
            token = JsonStringRegex().Match(token).Value;
        }

        return ReadToken(token)?.Trim() ?? "";
    }

    [GeneratedRegex(JsonString)]
    private static partial Regex JsonStringRegex();

    /// <summary>Reads a matched JSON string, <c>null</c>, or bare number token.</summary>
    private static string? ReadToken(string token)
    {
        if (token == "null")
        {
            return null;
        }

        if (!token.StartsWith('"'))
        {
            return token;
        }

        try
        {
            return JsonSerializer.Deserialize<string>(token);
        }
        catch (JsonException)
        {
            return token[1..^1];
        }
    }

    private static string? ReadProperty(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null,
            }
            : null;

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
