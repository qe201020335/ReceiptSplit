using System.Collections.Concurrent;
using ReceiptSplit.Extraction;

namespace ReceiptSplit.Tests.Support;

public sealed class FakeLlamaClient : ILlamaClient
{
    public const string ValidOutput = """
        ["BANANAS", null, 1.25, "1.99", null]
        ["MILK 2L", "4011", 1, "5.49", "H"]
        {"s": "7.48", "t": "0.71", "T": "8.19", "store": "Corner Market", "date": "2026-09-14"}
        """;

    /// <summary>A Costco style receipt: a promotion under a taxed item, priced at Ontario's 13% on the net 10.99.</summary>
    public const string PromotionOutput = """
        ["WAGON", "1872234", 1, "13.99", "H"]
        ["TPD/1872234", "2108345", 1, "-3.00", null]
        ["KS ORG OAT", "1272413", 1, "12.99", null]
        {"s": "23.98", "t": "1.43", "T": "25.41", "store": "Costco Wholesale", "date": "2026-09-13"}
        """;

    private volatile TaskCompletionSource _gate = CompletedGate();

    public string Content { get; set; } = ValidOutput;

    /// <summary>When set, extraction requests throw this instead of answering.</summary>
    public Exception? Failure { get; set; }

    public ConcurrentQueue<PreparedImage> Requests { get; } = new();

    /// <summary>Holds extraction requests until <see cref="Release"/> is called.</summary>
    public void Block() => _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Release() => _gate.TrySetResult();

    public async Task<LlmCompletion> ExtractReceiptAsync(PreparedImage image, CancellationToken cancellationToken)
    {
        await _gate.Task.WaitAsync(cancellationToken);
        Requests.Enqueue(image);
        if (Failure is not null)
        {
            throw Failure;
        }

        return new LlmCompletion(Content, "fake-model", "stop", 100, 50, TimeSpan.FromMilliseconds(5));
    }

    private static TaskCompletionSource CompletedGate()
    {
        var gate = new TaskCompletionSource();
        gate.SetResult();
        return gate;
    }
}
