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
/// <para>
/// Unbounded is the default because it makes a tell complete synchronously, which is what keeps
/// fan-out cheap. A bounded mailbox trades that for backpressure - the sender's
/// <see cref="PostAsync"/> stops completing synchronously once the actor falls behind, which
/// pushes the slowdown back to whoever is producing rather than into this process's heap.
/// </para>
/// <para>
/// A second lane appears the first time a message marked <see cref="UrgentAttribute"/> is posted,
/// and not before: an actor that never sees one is a single channel, exactly as it was. That is why
/// the lane is created on demand rather than configured - there is nothing to switch on, and no way
/// to mark a message urgent and then find the mailbox was not listening.
/// </para>
/// </remarks>
public sealed class ChannelMailbox : IMailbox
{
    /// <summary>
    /// An ordinary message is taken after this many urgent ones, even with urgent messages queued.
    /// </summary>
    /// <remarks>
    /// Strict priority is simpler to describe, and under it a sender producing urgent messages
    /// faster than the actor handles them starves the ordinary lane for as long as it keeps going -
    /// a queue that never drains and never errors. Eight is high enough that an urgent message
    /// still overtakes any backlog worth the name; being finite is the part that matters.
    /// </remarks>
    private const int UrgentBurst = 8;

    private readonly int _capacity;
    private readonly Channel<Envelope> _ordinary;

    // Signalled once, when the urgent lane is created. The reader can be parked on the ordinary
    // lane at that moment, with nothing about to be written there - this is what wakes it.
    private readonly TaskCompletionSource _urgentAppeared = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private Channel<Envelope>? _urgent;
    private bool _completed;

    // Touched only by the actor's own loop, which is the single reader.
    private int _urgentRun;

    /// <summary>Creates a mailbox. <paramref name="capacity"/> of zero or less means unbounded.</summary>
    public ChannelMailbox(int capacity = 0)
    {
        _capacity = capacity;
        _ordinary = Create(capacity);
    }

    private static Channel<Envelope> Create(int capacity) =>
        capacity > 0
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

    /// <inheritdoc />
    public int Count
    {
        get
        {
            var count = _ordinary.Reader.CanCount ? _ordinary.Reader.Count : 0;
            if (Volatile.Read(ref _urgent) is { Reader.CanCount: true } urgent) count += urgent.Reader.Count;
            return count;
        }
    }

    /// <summary>The lane a message belongs in, creating the urgent one the first time it is needed.</summary>
    private Channel<Envelope> LaneFor(in Envelope envelope) =>
        envelope.Message is { } message && UrgentMessages.Is(message.GetType()) ? UrgentLane() : _ordinary;

    private Channel<Envelope> UrgentLane()
    {
        if (Volatile.Read(ref _urgent) is { } existing) return existing;

        var created = Create(_capacity);
        var winner = Interlocked.CompareExchange(ref _urgent, created, null) ?? created;
        if (!ReferenceEquals(winner, created)) return winner;

        // A lane created after the mailbox closed must be closed too, or a message posted during a
        // stop would be accepted into a queue nothing will ever read.
        if (Volatile.Read(ref _completed)) created.Writer.TryComplete();

        _urgentAppeared.TrySetResult();
        return created;
    }

    /// <inheritdoc />
    public async ValueTask<bool> PostAsync(Envelope envelope, CancellationToken cancellationToken)
    {
        var lane = LaneFor(envelope);

        // The fast path: an unbounded mailbox, or a bounded one with room. No await, no state machine.
        if (lane.Writer.TryWrite(envelope)) return true;

        try
        {
            while (await lane.Writer.WaitToWriteAsync(cancellationToken).ConfigureAwait(false))
            {
                if (lane.Writer.TryWrite(envelope)) return true;
            }
        }
        catch (ChannelClosedException)
        {
            // Raced with Complete(). Same answer as a clean close.
        }

        return false;
    }

    /// <inheritdoc />
    public bool TryPost(in Envelope envelope) => LaneFor(envelope).Writer.TryWrite(envelope);

    /// <inheritdoc />
    public async ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            if (Volatile.Read(ref _urgent) is not { } urgent)
            {
                // Nothing here has ever been urgent, so this is the single-channel wait it always
                // was, racing only the signal that says a second lane now exists.
                var only = Wait(_ordinary.Reader, cancellationToken);
                var appeared = _urgentAppeared.Task;

                if (!only.IsCompleted && !appeared.IsCompleted)
                    await Task.WhenAny(only, appeared).ConfigureAwait(false);

                if (only.IsCompleted && await only.ConfigureAwait(false)) return true;
                if (Volatile.Read(ref _urgent) is not null) continue;   // ask again, with both lanes
                return false;
            }

            Task<bool>? ordinary = Wait(_ordinary.Reader, cancellationToken);
            Task<bool>? pressing = Wait(urgent.Reader, cancellationToken);

            // A lane is dropped from the wait once it reports closed and drained, rather than left
            // in it: a completed task in a WhenAny returns instantly, every time, forever.
            while (ordinary is not null || pressing is not null)
            {
                if (ordinary is { IsCompleted: true })
                {
                    if (await ordinary.ConfigureAwait(false)) return true;
                    ordinary = null;
                    continue;
                }

                if (pressing is { IsCompleted: true })
                {
                    if (await pressing.ConfigureAwait(false)) return true;
                    pressing = null;
                    continue;
                }

                if (ordinary is null) await pressing!.ConfigureAwait(false);
                else if (pressing is null) await ordinary.ConfigureAwait(false);
                else await Task.WhenAny(ordinary, pressing).ConfigureAwait(false);
            }

            return false;
        }
    }

    private static async Task<bool> Wait(ChannelReader<Envelope> reader, CancellationToken cancellationToken)
    {
        try
        {
            return await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public bool TryRead(out Envelope envelope)
    {
        if (Volatile.Read(ref _urgent) is not { } urgent) return _ordinary.Reader.TryRead(out envelope);

        if (_urgentRun < UrgentBurst && urgent.Reader.TryRead(out envelope))
        {
            _urgentRun++;
            return true;
        }

        // Either the burst is spent or there is nothing urgent left. An ordinary message resets the
        // run, so the next urgent one overtakes again.
        if (_ordinary.Reader.TryRead(out envelope))
        {
            _urgentRun = 0;
            return true;
        }

        // The burst was spent and there was nothing to be fair to.
        if (urgent.Reader.TryRead(out envelope))
        {
            _urgentRun = 1;
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public void Complete()
    {
        Volatile.Write(ref _completed, true);
        _ordinary.Writer.TryComplete();
        Volatile.Read(ref _urgent)?.Writer.TryComplete();
    }
}
