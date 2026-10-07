using System.Threading.Channels;

namespace ReceiptSplit.Extraction;

/// <summary>
/// In-memory queue of receipt ids waiting for extraction. The receipt's status in the database is the source
/// of truth; the worker re-queues unfinished receipts on startup.
/// </summary>
public sealed class ExtractionQueue
{
    private readonly Channel<Guid> _channel =
        Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(Guid receiptId) => _channel.Writer.TryWrite(receiptId);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
