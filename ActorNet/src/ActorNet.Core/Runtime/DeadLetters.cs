// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Collections.Concurrent;

namespace ActorNet.Runtime;

/// <summary>Why a message could not be delivered.</summary>
public enum DeadLetterReason
{
    /// <summary>The actor id's type half was never registered on this node.</summary>
    UnregisteredActorType,

    /// <summary>The actor kept deactivating between the directory lookup and the post.</summary>
    UndeliverableToActor,

    /// <summary>The node that owns the key could not be reached.</summary>
    NodeUnreachable,

    /// <summary>A frame arrived naming a message alias this node does not allow.</summary>
    UnknownMessageType,

    /// <summary>A frame arrived that could not be routed - a malformed address, or no payload.</summary>
    UnroutableFrame,

    /// <summary>The node was stopping when the message arrived.</summary>
    Shutdown,
}

/// <summary>One message that never reached an actor.</summary>
/// <param name="Target">Where it was headed. May be empty when the address itself was unusable.</param>
/// <param name="Sender">Who sent it, when that was known.</param>
/// <param name="Message">
/// The message, when it had been materialized. Null for a frame refused before deserialization -
/// which is deliberate: materializing a refused payload would undo the allow-list that refused it.
/// </param>
/// <param name="MessageType">The message's type or wire alias, which is known even when the body is not.</param>
/// <param name="Reason">Why delivery failed.</param>
/// <param name="Detail">The exception message or diagnostic behind the reason.</param>
/// <param name="At">When it was recorded.</param>
public sealed record DeadLetter(
    ActorId Target,
    ActorId Sender,
    object? Message,
    string MessageType,
    DeadLetterReason Reason,
    string Detail,
    DateTimeOffset At);

/// <summary>
/// Where undeliverable messages go.
/// </summary>
/// <remarks>
/// <para>
/// Before this, an undeliverable message was logged and dropped, which makes it invisible to
/// anything but a human reading logs. A dead letter is a record an operator can look at, count, and
/// - because the message object is kept where it was materialized - re-drive once whatever was
/// broken is fixed.
/// </para>
/// <para>
/// The buffer is bounded and drops the oldest. A node that is failing to deliver is usually failing
/// a lot, and an unbounded record of that is a second outage on top of the first.
/// </para>
/// </remarks>
public interface IDeadLetterQueue
{
    /// <summary>How many messages have been recorded since the node started, including dropped ones.</summary>
    long Count { get; }

    /// <summary>Records an undeliverable message.</summary>
    void Record(DeadLetter letter);

    /// <summary>The retained letters, newest first.</summary>
    IReadOnlyList<DeadLetter> Recent(int limit = 100);

    /// <summary>Forgets everything retained. The lifetime count is not reset.</summary>
    void Clear();

    /// <summary>Raised for each letter, on the thread that recorded it.</summary>
    /// <remarks>
    /// Subscribers should be quick and must not throw - this runs on the sending path, and a slow
    /// handler here slows down the thing that is already going wrong.
    /// </remarks>
    event Action<DeadLetter>? LetterRecorded;
}

/// <summary>The default queue: a bounded ring in memory.</summary>
public sealed class DeadLetterQueue(int capacity = 256) : IDeadLetterQueue
{
    private readonly ConcurrentQueue<DeadLetter> _letters = new();
    private readonly int _capacity = capacity > 0
        ? capacity
        : throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be positive.");

    private long _count;

    /// <inheritdoc />
    public long Count => Interlocked.Read(ref _count);

    /// <inheritdoc />
    public event Action<DeadLetter>? LetterRecorded;

    /// <inheritdoc />
    public void Record(DeadLetter letter)
    {
        ArgumentNullException.ThrowIfNull(letter);

        Interlocked.Increment(ref _count);
        _letters.Enqueue(letter);

        // Trim after enqueueing rather than before, so a burst from several threads settles at the
        // capacity instead of racing below it.
        while (_letters.Count > _capacity && _letters.TryDequeue(out _)) { }

        // Each subscriber is invoked separately. A multicast delegate stops at the first handler
        // that throws, so one badly-behaved subscriber would otherwise silence every subscriber
        // registered after it - a worse failure than the one being reported.
        if (LetterRecorded is not { } handlers) return;

        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action<DeadLetter>)handler)(letter);
            }
            catch
            {
                // A subscriber that throws must not turn an undeliverable message into a failed
                // send, and must not stop the ones after it.
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<DeadLetter> Recent(int limit = 100) =>
        _letters.Reverse().Take(Math.Max(1, limit)).ToArray();

    /// <inheritdoc />
    public void Clear()
    {
        while (_letters.TryDequeue(out _)) { }
    }
}
