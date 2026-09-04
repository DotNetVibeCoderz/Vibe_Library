// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Threading.Channels;

namespace ActorNet.Runtime;

/// <summary>A single actor's message queue.</summary>
public interface IMailbox
{
    /// <summary>Messages accepted but not yet handed to the actor.</summary>
    int Count { get; }

    /// <summary>
    /// Enqueues a message, waiting when the mailbox is bounded and full. Returns false once the
    /// mailbox has been closed, which is how a sender learns the actor is going away and that it
    /// should look the address up again.
    /// </summary>
    ValueTask<bool> PostAsync(Envelope envelope, CancellationToken cancellationToken);

    /// <summary>Non-blocking enqueue. False when the mailbox is closed or full.</summary>
    bool TryPost(in Envelope envelope);

    /// <summary>Completes when a message is available; false once the mailbox is closed and drained.</summary>
    ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken);

    /// <summary>Takes the next message if one is queued.</summary>
    bool TryRead(out Envelope envelope);

    /// <summary>
    /// Closes the mailbox to new messages. Anything already queued is still delivered, which is
    /// what makes a graceful stop graceful.
    /// </summary>
    void Complete();
}

/// <summary>
/// The default mailbox: a <see cref="Channel{T}"/>, single-reader (the actor's loop),
/// multi-writer.
/// </summary>
/// <remarks>
/// Unbounded is the default because it makes a tell complete synchronously, which is what keeps
/// fan-out cheap. A bounded mailbox trades that for backpressure - the sender's
/// <see cref="PostAsync"/> stops completing synchronously once the actor falls behind, which
/// pushes the slowdown back to whoever is producing rather than into this process's heap.
/// </remarks>
public sealed class ChannelMailbox : IMailbox
{
    private readonly Channel<Envelope> _channel;

    /// <summary>Creates a mailbox. <paramref name="capacity"/> of zero or less means unbounded.</summary>
    public ChannelMailbox(int capacity = 0)
    {
        _channel = capacity > 0
            ? Channel.CreateBounded<Envelope>(new BoundedChannelOptions(capacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
            })
            : Channel.CreateUnbounded<Envelope>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
            });
    }

    /// <inheritdoc />
    public int Count => _channel.Reader.CanCount ? _channel.Reader.Count : 0;

    /// <inheritdoc />
    public async ValueTask<bool> PostAsync(Envelope envelope, CancellationToken cancellationToken)
    {
        // The fast path: an unbounded mailbox, or a bounded one with room. No await, no state machine.
        if (_channel.Writer.TryWrite(envelope)) return true;

        try
        {
            while (await _channel.Writer.WaitToWriteAsync(cancellationToken).ConfigureAwait(false))
            {
                if (_channel.Writer.TryWrite(envelope)) return true;
            }
        }
        catch (ChannelClosedException)
        {
            // Raced with Complete(). Same answer as a clean close.
        }

        return false;
    }

    /// <inheritdoc />
    public bool TryPost(in Envelope envelope) => _channel.Writer.TryWrite(envelope);

    /// <inheritdoc />
    public async ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public bool TryRead(out Envelope envelope) => _channel.Reader.TryRead(out envelope);

    /// <inheritdoc />
    public void Complete() => _channel.Writer.TryComplete();
}
