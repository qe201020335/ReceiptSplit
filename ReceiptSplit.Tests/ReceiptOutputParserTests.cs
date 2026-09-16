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
        Assert.True(ReceiptChecks.Evaluate(parsed.Lines.Select(l => l.Amount), parsed.Subtotal, parsed.Tax, parsed.Total).Passed);
    }
}
