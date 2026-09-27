using System.Threading.Channels;

namespace Infrastructure.Push;

/// <summary>
/// Wakes the push worker when a push is queued, so it goes out now instead of at the worker's next poll. Only a
/// shortcut: the outbox table is the queue, and the worker polls it anyway (retries, other app instances).
/// </summary>
public sealed class PushOutboxSignal
{
    // One pending wake-up is enough: the worker drains everything that is due each time it wakes.
    private readonly Channel<bool> channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true
    });

    public void Notify() => channel.Writer.TryWrite(true);

    /// <summary>Returns when <see cref="Notify"/> is called or after <paramref name="timeout"/>.</summary>
    public async Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await channel.Reader.ReadAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timed out: poll anyway.
        }
    }
}
