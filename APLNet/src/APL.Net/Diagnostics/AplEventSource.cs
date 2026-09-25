// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Diagnostics.Tracing;

namespace AplNet.Diagnostics;

/// <summary>
/// Lightweight counters for APL.Net, published as EventCounters under the name <c>AplNet</c>.
/// </summary>
/// <remarks>
/// <para>
/// Off by default and free while off: every increment is behind <see cref="EventSource.IsEnabled()"/>,
/// a single field read, and the counters themselves are only created when a listener enables the
/// source. Watch them with <c>dotnet-counters monitor --counters AplNet -p &lt;pid&gt;</c> or an
/// in-process <see cref="EventListener"/>.
/// </para>
/// <para>
/// Under NativeAOT, EventSource support is trimmed away unless the app sets
/// <c>&lt;EventSourceSupport&gt;true&lt;/EventSourceSupport&gt;</c>; the library works the same either
/// way, it just stops counting.
/// </para>
/// </remarks>
[EventSource(Name = "AplNet")]
public sealed class AplEventSource : EventSource
{
    /// <summary>The single instance.</summary>
    public static readonly AplEventSource Log = new();

    private long _loops;
    private long _inlineLoops;
    private long _chunks;
    private long _steals;

    private PollingCounter? _loopsCounter;
    private PollingCounter? _inlineLoopsCounter;
    private PollingCounter? _chunksCounter;
    private PollingCounter? _stealsCounter;

    private AplEventSource()
    {
    }

    /// <summary>Loops that ran on more than one worker since the process started (while enabled).</summary>
    public long LoopsExecuted => Interlocked.Read(ref _loops);

    /// <summary>Loops that ran entirely on the calling thread because they were too small to split.</summary>
    public long InlineLoopsExecuted => Interlocked.Read(ref _inlineLoops);

    /// <summary>Ranges handed to a loop body: partitions, stripes, blocks or work-stealing grains.</summary>
    public long ChunksExecuted => Interlocked.Read(ref _chunks);

    /// <summary>Successful steals by <see cref="Core.Partitioners.WorkStealingPartitioner"/>.</summary>
    public long StealCount => Interlocked.Read(ref _steals);

    /// <inheritdoc />
    protected override void OnEventCommand(EventCommandEventArgs command)
    {
        if (command.Command != EventCommand.Enable)
            return;

        _loopsCounter ??= new PollingCounter("loops-executed", this, () => LoopsExecuted) { DisplayName = "Parallel loops executed" };
        _inlineLoopsCounter ??= new PollingCounter("inline-loops-executed", this, () => InlineLoopsExecuted) { DisplayName = "Loops run inline (too small to split)" };
        _chunksCounter ??= new PollingCounter("chunks-executed", this, () => ChunksExecuted) { DisplayName = "Chunks executed" };
        _stealsCounter ??= new PollingCounter("steals", this, () => StealCount) { DisplayName = "Work-stealing steals" };
    }

    [NonEvent]
    internal void OnLoop(bool inline)
    {
        if (inline)
            Interlocked.Increment(ref _inlineLoops);
        else
            Interlocked.Increment(ref _loops);
    }

    [NonEvent]
    internal void OnChunk() => Interlocked.Increment(ref _chunks);

    [NonEvent]
    internal void OnSteal() => Interlocked.Increment(ref _steals);
}
