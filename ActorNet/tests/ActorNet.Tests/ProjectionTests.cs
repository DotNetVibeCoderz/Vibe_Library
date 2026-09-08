// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ActorNet.Persistence;
using ActorNet.Streams;

namespace ActorNet.Tests;

/// <summary>
/// Read models folded out of a journal, and the positions that let them resume.
/// </summary>
/// <remarks>
/// The events are the record and a projection is a view of them, so it can always be thrown away
/// and rebuilt. That property is what the rewind test is about; the rest are about not rebuilding
/// it every time, which is what the stored position is for.
/// </remarks>
public sealed class ProjectionTests
{
    private sealed record Credited(decimal Amount);

    /// <summary>Counts what it has been shown, and remembers each event so replays are visible.</summary>
    private sealed class RunningTotal(string name = "running-total") : IProjection
    {
        public string Name { get; } = name;

        public List<long> Applied { get; } = [];

        public decimal Total { get; private set; }

        public Task ApplyAsync(JournalEntry entry, CancellationToken cancellationToken)
        {
            Applied.Add(entry.Sequence);
            if (entry.Event is Credited credited) Total += credited.Amount;
            return Task.CompletedTask;
        }
    }

    private static async Task<(InMemoryEventJournal Journal, ProjectionRunner Runner, IStreamPositions Positions)> WithEventsAsync(
        string id, int count)
    {
        var journal = new InMemoryEventJournal();
        var positions = new StoredStreamPositions(new InMemoryStateStore());

        for (var i = 1; i <= count; i++)
            await journal.AppendAsync(id, [new Credited(10m)], cancellationToken: TestContext.Current.CancellationToken);

        return (journal, new ProjectionRunner(journal, positions), positions);
    }

    [Fact]
    public async Task AProjectionFoldsTheWholeStreamTheFirstTime()
    {
        var (_, runner, _) = await WithEventsAsync("proj-a", 5);
        var view = new RunningTotal();

        var applied = await runner.CatchUpAsync(view, "proj-a", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(5, applied);
        Assert.Equal(50m, view.Total);
        Assert.Equal(new long[] { 1, 2, 3, 4, 5 }, view.Applied);
    }

    [Fact]
    public async Task ASecondRunOnlyFoldsWhatIsNew()
    {
        var (journal, runner, _) = await WithEventsAsync("proj-b", 3);
        var view = new RunningTotal();

        await runner.CatchUpAsync(view, "proj-b", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(30m, view.Total);

        await journal.AppendAsync("proj-b", [new Credited(10m), new Credited(10m)],
            cancellationToken: TestContext.Current.CancellationToken);

        view.Applied.Clear();
        var applied = await runner.CatchUpAsync(view, "proj-b", cancellationToken: TestContext.Current.CancellationToken);

        // The whole point of a stored position: the first three are not folded in twice.
        Assert.Equal(2, applied);
        Assert.Equal(new long[] { 4, 5 }, view.Applied);
        Assert.Equal(50m, view.Total);
    }

    [Fact]
    public async Task RewindingReplaysFromTheBeginning()
    {
        var (_, runner, _) = await WithEventsAsync("proj-c", 4);
        var view = new RunningTotal();

        await runner.CatchUpAsync(view, "proj-c", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(4, await runner.PositionAsync(view, "proj-c", TestContext.Current.CancellationToken));

        await runner.RewindAsync(view, "proj-c", TestContext.Current.CancellationToken);

        // The events are the record, so a view can always be thrown away and rebuilt. The caller
        // clears its own read model; this only moves the position.
        var rebuilt = new RunningTotal();
        var applied = await runner.CatchUpAsync(rebuilt, "proj-c", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(4, applied);
        Assert.Equal(40m, rebuilt.Total);
    }

    [Fact]
    public async Task TwoProjectionsOverOneStreamKeepSeparatePositions()
    {
        var (_, runner, _) = await WithEventsAsync("proj-d", 3);

        var first = new RunningTotal("first");
        var second = new RunningTotal("second");

        await runner.CatchUpAsync(first, "proj-d", cancellationToken: TestContext.Current.CancellationToken);

        // The second has seen nothing, and must not inherit the first one's progress.
        var applied = await runner.CatchUpAsync(second, "proj-d", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(3, applied);
        Assert.Equal(30m, second.Total);
    }

    [Fact]
    public async Task OneProjectionOverTwoStreamsKeepsAPositionForEach()
    {
        var journal = new InMemoryEventJournal();
        var runner = new ProjectionRunner(journal, new StoredStreamPositions(new InMemoryStateStore()));

        await journal.AppendAsync("left", [new Credited(1m), new Credited(1m)],
            cancellationToken: TestContext.Current.CancellationToken);
        await journal.AppendAsync("right", [new Credited(1m)],
            cancellationToken: TestContext.Current.CancellationToken);

        var view = new RunningTotal();
        await runner.CatchUpAsync(view, "left", cancellationToken: TestContext.Current.CancellationToken);
        await runner.CatchUpAsync(view, "right", cancellationToken: TestContext.Current.CancellationToken);

        // One position per stream. A single shared one would be wrong for every stream but the last
        // to run, and would silently skip the others.
        Assert.Equal(2, await runner.PositionAsync(view, "left", TestContext.Current.CancellationToken));
        Assert.Equal(1, await runner.PositionAsync(view, "right", TestContext.Current.CancellationToken));
        Assert.Equal(3m, view.Total);
    }

    [Fact]
    public async Task AProjectionOverARealActorsHistory()
    {
        await using var harness = new TestHarness();
        var system = await harness.LocalAsync();

        var id = ActorId.For<LedgerActor>("projected");
        await system.TellAsync(id, new Deposit(40m));
        await system.TellAsync(id, new Withdraw(15m));
        await system.AskAsync<Balance>(id, new GetBalance(), TimeSpan.FromSeconds(10));

        var runner = new ProjectionRunner(
            system.Options.EventJournal,
            new StoredStreamPositions(system.Options.StateStore));

        var audit = new EventNames();
        await runner.CatchUpAsync(audit, id.ToString(), cancellationToken: TestContext.Current.CancellationToken);

        // The events an actor wrote, read back by something that is not that actor. That is the
        // whole reason a journal is a journal rather than a private detail of recovery.
        Assert.Equal(["Deposited", "Withdrawn"], audit.Names);
    }

    private sealed class EventNames : IProjection
    {
        public string Name => "event-names";

        public List<string> Names { get; } = [];

        public Task ApplyAsync(JournalEntry entry, CancellationToken cancellationToken)
        {
            Names.Add(entry.Event.GetType().Name);
            return Task.CompletedTask;
        }
    }
}

/// <summary>
/// Resuming a stream where the last run left off.
/// </summary>
public sealed class StreamPositionTests
{
    [Fact]
    public async Task ARestartedStreamSkipsWhatItAlreadyHandled()
    {
        var positions = new StoredStreamPositions(new InMemoryStateStore());

        var first = new List<int>();
        await ActorStream<int>.From(Enumerable.Range(1, 5))
            .Resume(positions, "counter")
            .RunAsync((i, _) => { first.Add(i); return ValueTask.CompletedTask; }, TestContext.Current.CancellationToken);

        Assert.Equal([1, 2, 3, 4, 5], first);

        // The same source again - which is the case this is for, a source that replays.
        var second = new List<int>();
        await ActorStream<int>.From(Enumerable.Range(1, 8))
            .Resume(positions, "counter")
            .RunAsync((i, _) => { second.Add(i); return ValueTask.CompletedTask; }, TestContext.Current.CancellationToken);

        Assert.Equal([6, 7, 8], second);
    }

    [Fact]
    public async Task ACheckpointIntervalLetsThePositionLagWhileItRuns()
    {
        var positions = new StoredStreamPositions(new InMemoryStateStore());
        var duringRun = new List<long>();

        // Observed from inside the run: with a checkpoint every three, the position is written on
        // the third item and not the fourth. That lag is exactly what the interval buys - fewer
        // writes while running, and more replayed after a crash that stops the process outright.
        await ActorStream<int>.From(Enumerable.Range(1, 5))
            .Resume(positions, "chunked", checkpointEvery: 3)
            .RunAsync(async (_, token) => duringRun.Add(await positions.LoadAsync("chunked", token)),
                TestContext.Current.CancellationToken);

        Assert.Equal(new long[] { 0, 0, 0, 3, 3 }, duringRun);

        // And a run that ends - cleanly, early, or by throwing - flushes what it got to, so the
        // next run does not replay the tail for no reason.
        Assert.Equal(5, await positions.LoadAsync("chunked", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AConsumerThatStopsEarlyRecordsOnlyWhatItCanProveItHandled()
    {
        var positions = new StoredStreamPositions(new InMemoryStateStore());

        await ActorStream<int>.From(Enumerable.Range(1, 10))
            .Resume(positions, "stopped", checkpointEvery: 100)
            .Take(4)
            .RunAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Three, not four. The fourth item reached the consumer, and then the Take ended the
        // enumeration while the producer was still suspended on that yield - so it never learned
        // the item had been handled. Recording three replays the fourth on the next run, which is
        // the safe direction and the whole meaning of at-least-once.
        Assert.Equal(3, await positions.LoadAsync("stopped", TestContext.Current.CancellationToken));

        // Without the flush on the way out it would have been zero, and the next run would have
        // started from the beginning: a checkpoint interval larger than the run wrote nothing.
        Assert.NotEqual(0, await positions.LoadAsync("stopped", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnItemThatCarriesItsOwnOffsetIsUsedRatherThanACount()
    {
        var positions = new StoredStreamPositions(new InMemoryStateStore());
        await positions.SaveAsync("sparse", 20, TestContext.Current.CancellationToken);

        var seen = new List<long>();

        // The offsets are the items' own, and they have gaps - a compacted journal looks like this.
        // Counting items instead would have resumed in the wrong place entirely.
        await ActorStream<long>.From(new long[] { 5, 10, 20, 30, 40 })
            .Resume(positions, "sparse", offsetOf: static value => value)
            .RunAsync((value, _) => { seen.Add(value); return ValueTask.CompletedTask; }, TestContext.Current.CancellationToken);

        Assert.Equal([30, 40], seen);
        Assert.Equal(40, await positions.LoadAsync("sparse", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task APositionSurvivesTheStoreItIsKeptIn()
    {
        // The same store, a new reader - which is what a restart looks like from here.
        var store = new InMemoryStateStore();

        await new StoredStreamPositions(store).SaveAsync("restarted", 7, TestContext.Current.CancellationToken);
        Assert.Equal(7, await new StoredStreamPositions(store).LoadAsync("restarted", TestContext.Current.CancellationToken));
    }
}
