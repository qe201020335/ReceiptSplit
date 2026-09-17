using System.Globalization;
using System.Text.Json;
using ReceiptSplit.Extraction;
using ReceiptSplit.Tests.Support;

namespace ReceiptSplit.Tests;

public class ReceiptOutputParserTests
{
    [Fact]
    public void Parses_rows_totals_store_and_date()
    {
        var parsed = ReceiptOutputParser.Parse(FakeLlamaClient.ValidOutput);

        Assert.Equal(
            new[]
            {
                new ParsedLine("BANANAS", null, 1.25m, 1.99m, null),
                new ParsedLine("MILK 2L", "4011", 1m, 5.49m, "H"),
            },
            parsed.Lines);
        Assert.Equal(7.48m, parsed.Subtotal);
        Assert.Equal(0.71m, parsed.Tax);
        Assert.Equal(8.19m, parsed.Total);
        Assert.Equal("Corner Market", parsed.StoreName);
        Assert.Equal(new DateOnly(2026, 9, 14), parsed.PurchaseDate);
    }

    [Fact]
    public void Handles_code_fences_and_one_multiline_array()
    {
        const string output = """
            ```json
            [
              ["A", null, 1, "1.00", null],
              ["B", null, 1, "2.00", "G P"]
            ]
            ```
            ```json
            {"s": "3.00", "t": "0.00", "T": "3.00"}
            ```
            """;

        var parsed = ReceiptOutputParser.Parse(output);

        Assert.Equal(["A", "B"], parsed.Lines.Select(l => l.Name));
        Assert.Equal("G P", parsed.Lines[1].TaxCode);
        Assert.Equal(3.00m, parsed.Total);
    }

    [Fact]
    public void Handles_rows_wrapped_in_double_brackets()
    {
        const string output = """
            [["A","1",1,"1.00",null]]
            [["B","2",1,"2.00",null]]
            {"s":"3.00","t":"0.00","T":"3.00"}
            """;

        Assert.Equal([1.00m, 2.00m], ReceiptOutputParser.Parse(output).Lines.Select(l => l.Amount));
    }

    [Fact]
    public void Handles_a_dropped_opening_bracket()
    {
        const string output = """[["A",null,1,"1.00",null],"B",null,1,"2.00",null],["C",null,1,"3.00",null]]""";

        Assert.Equal(["A", "B", "C"], ReceiptOutputParser.Parse(output).Lines.Select(l => l.Name));
    }

    [Fact]
    public void Reads_negative_discount_lines()
    {
        const string output = """
            ["PF GOLDFISH", "339054", 1, "13.99", null]
            ["TPD/339054", "2108345", -1, "3.00-", null]
            """;

        var discount = ReceiptOutputParser.Parse(output).Lines[1];

        Assert.Equal(-3.00m, discount.Amount);
        Assert.Equal(-1m, discount.Quantity);
    }

    [Fact]
    public void Drops_zero_amount_info_rows()
    {
        const string output = """
            [["GROCERY", null, 1, "0.00", null], ["CILANTRO", "W", 1, "1.98", null], ["Points 40", null, 1, "0.00", null]]
            """;

        Assert.Equal("CILANTRO", Assert.Single(ReceiptOutputParser.Parse(output).Lines).Name);
    }

    [Fact]
    public void Reads_escaped_names_bare_codes_and_quoted_quantities()
    {
        const string output = """["12\" PIZZA", 4011, "2", "$19.98", null]""";

        Assert.Equal(new ParsedLine("12\" PIZZA", "4011", 2m, 19.98m, null), Assert.Single(ReceiptOutputParser.Parse(output).Lines));
    }

    /// <summary>From a real T&amp;T photo, where all but the first row came back in these shapes.</summary>
    [Fact]
    public void Reads_names_given_with_their_printed_translation_and_rows_without_a_code()
    {
        const string output = """
            [["TTL DA HONG PAO OOLONG","040663446",1,"26.99","U"],[["MM NO ADDED SUGAR APPLE SODA","美粒果0加糖苹果苏打"],null,1,"2.49","G P"],[["CARROT","紅蘿蔔"],0.375,"1.23","U"],[["STRAWBERRY","草莓"],null,1,"6.99","U"],["ONION",2,"3.49",null]]
            """;

        Assert.Equal(
            new[]
            {
                new ParsedLine("TTL DA HONG PAO OOLONG", "040663446", 1m, 26.99m, "U"),
                new ParsedLine("MM NO ADDED SUGAR APPLE SODA", null, 1m, 2.49m, "G P"),
                new ParsedLine("CARROT", null, 0.375m, 1.23m, "U"),
                new ParsedLine("STRAWBERRY", null, 1m, 6.99m, "U"),
                new ParsedLine("ONION", null, 2m, 3.49m, null),
            },
            ReceiptOutputParser.Parse(output).Lines);
    }

    [Theory]
    [InlineData("4.99", "4.99")]
    [InlineData("$1,234.50", "1234.50")]
    [InlineData("-3.00", "-3.00")]
    [InlineData("3.00-", "-3.00")]
    [InlineData("(3.00)", "-3.00")]
    [InlineData("-$3.00", "-3.00")]
    [InlineData("$-3.00", "-3.00")]
    [InlineData("", null)]
    [InlineData("n/a", null)]
    public void Parses_money_formats(string text, string? expected)
    {
        Assert.Equal(
            expected is null ? null : decimal.Parse(expected, CultureInfo.InvariantCulture),
            ReceiptOutputParser.ParseMoney(text));
    }

    [Theory]
    [InlineData("\"2026-09-14\"", "2026-09-14")]
    [InlineData("\"2026-09-14 16:51:13\"", "2026-09-14")]
    [InlineData("\"Sept 14\"", null)]
    [InlineData("null", null)]
    public void Reads_purchase_date(string dateJson, string? expected)
    {
        var parsed = ReceiptOutputParser.Parse($$"""{"s":"1.00","t":"0.00","T":"1.00","store":null,"date":{{dateJson}}}""");

        Assert.Equal(expected is null ? null : DateOnly.Parse(expected, CultureInfo.InvariantCulture), parsed.PurchaseDate);
        Assert.Null(parsed.StoreName);
    }

    [Fact]
    public void Reads_numeric_totals()
    {
        var parsed = ReceiptOutputParser.Parse("""{"s": 58.05, "t": 2.59, "T": 60.64}""");

        Assert.Equal((58.05m, 2.59m, 60.64m), (parsed.Subtotal!.Value, parsed.Tax!.Value, parsed.Total!.Value));
    }

    [Fact]
    public void Leaves_totals_empty_without_a_totals_line()
    {
        var parsed = ReceiptOutputParser.Parse("""["A", null, 1, "1.00", null]""");

        Assert.Single(parsed.Lines);
        Assert.Null(parsed.Subtotal);
        Assert.Null(parsed.Total);
    }

    [Fact]
    public void Finds_nothing_in_prose()
    {
        var parsed = ReceiptOutputParser.Parse("I can't read this receipt, the photo is too blurry.");

        Assert.Empty(parsed.Lines);
        Assert.Null(parsed.Total);
    }

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
            taxRatePercent: 13m).Passed);
    }
}
