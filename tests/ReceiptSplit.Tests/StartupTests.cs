using ReceiptSplit.Tests.Support;

namespace ReceiptSplit.Tests;

public class StartupTests
{
    [Fact]
    public async Task The_app_doesnt_start_when_the_check_fails()
    {
        await using var factory = new ReceiptApiFactory();
        factory.Llm.Models.Clear();

        Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
    }
}
