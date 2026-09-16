using ReceiptSplit.Extraction;

namespace ReceiptSplit.Tests;

public class ReceiptPromotionsTests
{
    private static ParsedLine Line(string name, string? code, decimal amount, string? taxCode = null) =>
        new(name, code, Quantity: 1m, amount, taxCode);

    [Theory]
    // Costco Canada prints TPD/<item number>, Costco US just the number.
    [InlineData("TPD/339054", -3.00, "339054")]
    [InlineData("tpd/339054", -3.00, "339054")]
    [InlineData("/1218574", -3.20, "1218574")]
    [InlineData("/ 195864", -2.00, "195864")]
    // Not promotions.
    [InlineData("TPD/339054", 3.00, null)]
    [InlineData("(SALE) CHERRY", -1.00, null)]
    [InlineData("KS ORG OAT", -1.00, null)]
    [InlineData("N/A ITEM", -1.00, null)]
    public void Reads_the_item_number_a_promotion_discounts(string name, double amount, string? expected) =>
        Assert.Equal(expected, ReceiptPromotions.DiscountedCode(name, (decimal)amount));

    [Fact]
    public void Deducts_each_promotion_from_the_item_it_names()
    {
        var baked = ReceiptPromotions.Bake([
            Line("PF GOLDFISH", "339054", 13.99m),
            Line("TPD/339054", "2108345", -3.00m),
            Line("TONKOTSU RAM", "1407434", 15.99m),
            Line("TPD/1407434", "2099496", -4.00m),
        ]);

        Assert.Equal(["PF GOLDFISH", "TONKOTSU RAM"], baked.Select(b => b.Line.Name));
        Assert.Equal([10.99m, 11.99m], baked.Select(b => b.Line.Amount));
        Assert.Equal([-3.00m, -4.00m], baked.Select(b => b.Discount));
    }

    [Fact]
    public void Keeps_a_promotion_whose_item_is_missing_as_its_own_line()
    {
        var baked = ReceiptPromotions.Bake([Line("PF GOLDFISH", "339054", 13.99m), Line("TPD/999999", "2108345", -3.00m)]);

        Assert.Equal(["PF GOLDFISH", "TPD/999999"], baked.Select(b => b.Line.Name));
        Assert.Equal([13.99m, -3.00m], baked.Select(b => b.Line.Amount));
        Assert.All(baked, line => Assert.Equal(0m, line.Discount));
    }

    [Fact]
    public void Deducts_from_the_closest_item_above_when_an_item_number_repeats()
    {
        var baked = ReceiptPromotions.Bake([
            Line("VIENNA SAUSG", "704329", 7.99m),
            Line("VIENNA SAUSG", "704329", 7.99m),
            Line("/704329", null, -1.00m),
        ]);

        Assert.Equal([7.99m, 6.99m], baked.Select(b => b.Line.Amount));
        Assert.Equal([0m, -1.00m], baked.Select(b => b.Discount));
    }

    [Fact]
    public void Adds_up_several_promotions_on_one_item()
    {
        var baked = ReceiptPromotions.Bake([
            Line("CREST SCOPE", "1216715", 12.99m),
            Line("/1216715", null, -4.00m),
            Line("/1216715", null, -1.00m),
        ]);

        Assert.Equal(7.99m, Assert.Single(baked).Line.Amount);
        Assert.Equal(-5.00m, baked[0].Discount);
    }

    [Fact]
    public void Leaves_a_receipt_without_promotions_untouched()
    {
        ParsedLine[] lines = [Line("CILANTRO", null, 1.98m), Line("GARLIC", null, 0.98m, "G P")];

        var baked = ReceiptPromotions.Bake(lines);

        Assert.Equal(lines, baked.Select(b => b.Line));
        Assert.All(baked, line => Assert.Equal(0m, line.Discount));
    }
}
