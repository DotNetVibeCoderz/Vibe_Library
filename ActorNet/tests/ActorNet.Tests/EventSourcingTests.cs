// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ActorNet.Persistence;

namespace ActorNet.Tests;

public sealed class EventSourcingTests
{
    [Fact]
    public async Task StateIsRebuiltByReplayingTheStream()
    {
        await using var harness = new TestHarness();
        var system = await harness.LocalAsync();

        var id = ActorId.For<LedgerActor>($"ledger-{Guid.NewGuid():N}");
        var ledger = system.ActorOf(id);

        await ledger.TellAsync(new Deposit(100m));
        await ledger.TellAsync(new Deposit(50m));
        await ledger.TellAsync(new Withdraw(30m));
        Assert.Equal(120m, (await ledger.AskAsync<Balance>(new GetBalance())).Amount);

        await system.DeactivateAsync(id);
        await TestHarness.AssertEventuallyAsync(() => !system.LocalActors.Contains(id), "the actor should have deactivated");

        // Nothing was saved as state - only three events. The balance below is a fold over them.
        var replayed = await ledger.AskAsync<Balance>(new GetBalance());
        Assert.Equal(120m, replayed.Amount);
        Assert.Equal(3, replayed.Operations);
    }

    [Fact]
    public async Task ARejectedCommandWritesNoEvent()
    {
        await using var harness = new TestHarness();
        var system = await harness.LocalAsync();

        var id = ActorId.For<LedgerActor>($"reject-{Guid.NewGuid():N}");
        var ledger = system.ActorOf(id);

        await ledger.TellAsync(new Deposit(10m));
        var rejected = await ledger.AskAsync<Rejected>(new Withdraw(999m));

        Assert.Equal("insufficient funds", rejected.Reason);

        // A refused withdrawal is not something that happened, so the journal must not record it.
        var journal = (InMemoryEventJournal)system.Options.EventJournal;
        Assert.Equal(1, await journal.HighestSequenceAsync(id.ToString()));
    }

    [Fact]
    public async Task TheJournalKeepsTheWholeHistoryNotJustTheCurrentValue()
    {
        await using var harness = new TestHarness();
        var system = await harness.LocalAsync();

        var id = ActorId.For<LedgerActor>($"history-{Guid.NewGuid():N}");
        var ledger = system.ActorOf(id);

        await ledger.TellAsync(new Deposit(20m));
        await ledger.TellAsync(new Withdraw(5m));
        await ledger.AskAsync<Balance>(new GetBalance());

        var journal = (InMemoryEventJournal)system.Options.EventJournal;
        var entries = new List<JournalEntry>();
        await foreach (var entry in journal.ReadAsync(id.ToString())) entries.Add(entry);

        Assert.Equal(2, entries.Count);
        Assert.Equal([1L, 2L], entries.Select(e => e.Sequence));
        Assert.IsType<Deposited>(entries[0].Event);
        Assert.IsType<Withdrawn>(entries[1].Event);
    }

    [Fact]
    public async Task ASnapshotShortensRecoveryWithoutChangingIt()
    {
        await using var harness = new TestHarness();
        var system = await harness.LocalAsync();
        system.RegisterActor<SnapshottingLedgerActor>();

        var id = ActorId.For<SnapshottingLedgerActor>($"snap-{Guid.NewGuid():N}");
        var ledger = system.ActorOf(id);

        for (var i = 0; i < 25; i++) await ledger.TellAsync(new Deposit(2m));
        Assert.Equal(50m, (await ledger.AskAsync<Balance>(new GetBalance())).Amount);

        var snapshots = (InMemorySnapshotStore)system.Options.SnapshotStore;
        var stored = await snapshots.LoadAsync<LedgerState>(id.ToString());
        Assert.NotNull(stored);
        Assert.Equal(20, stored!.Sequence);

        await system.DeactivateAsync(id);
        await TestHarness.AssertEventuallyAsync(() => !system.LocalActors.Contains(id), "the actor should have deactivated");

        // Recovery loads the snapshot at sequence 20 and replays only events 21-25. The answer is
        // identical to the full replay, which is what makes a snapshot an optimization and not a
        // second source of truth.
        Assert.Equal(50m, (await ledger.AskAsync<Balance>(new GetBalance())).Amount);
    }

    [Fact]
    public async Task PersistingDuringRecoveryIsRefused()
    {
        await using var harness = new TestHarness();
        var system = await harness.LocalAsync();
        system.RegisterActor<BadRecoveryActor>();

        var id = ActorId.For<BadRecoveryActor>($"bad-{Guid.NewGuid():N}");

        // Appending to history while replaying it would grow the stream on every activation. The
        // base class refuses, activation fails, and the actor never handles a message.
        await system.TellAsync(id, new Deposit(1m));

        await TestHarness.AssertEventuallyAsync(() => BadRecoveryActor.Refusal is not null,
            "persisting during recovery should have been refused");
        Assert.DoesNotContain(id, system.LocalActors);

        var journal = (InMemoryEventJournal)system.Options.EventJournal;
        Assert.Equal(0, await journal.HighestSequenceAsync(id.ToString()));
    }

    [Fact]
    public async Task TheJournalRefusesInterleavedAppendsFromTwoWriters()
    {
        var journal = new InMemoryEventJournal();
        var sequence = await journal.AppendAsync("stream", [new Deposited(1m)]);

        await journal.AppendAsync("stream", [new Deposited(2m)], sequence);

        // The second writer is working from a sequence that is no longer the tip - which is what
        // a split activation looks like from the journal's side.
        await Assert.ThrowsAsync<StateConcurrencyException>(
            async () => await journal.AppendAsync("stream", [new Deposited(3m)], sequence));
    }

    [Fact]
    public async Task AFileJournalReplaysAcrossJournalInstances()
    {
        var directory = Path.Combine(Path.GetTempPath(), "actornet-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var types = new Serialization.MessageTypeRegistry();
            types.Register<Deposited>();
            types.Register<Withdrawn>();

            await new FileEventJournal(directory, types).AppendAsync("acct-1", [new Deposited(10m), new Withdrawn(4m)]);

            var reread = new FileEventJournal(directory, types);
            var events = new List<object>();
            await foreach (var entry in reread.ReadAsync("acct-1")) events.Add(entry.Event);

            Assert.Equal(2, events.Count);
            Assert.Equal(10m, Assert.IsType<Deposited>(events[0]).Amount);
            Assert.Equal(4m, Assert.IsType<Withdrawn>(events[1]).Amount);
            Assert.Equal(2, await reread.HighestSequenceAsync("acct-1"));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}

/// <summary>Snapshots every 20 events.</summary>
public sealed class SnapshottingLedgerActor : EventSourcedActor<LedgerState>
{
    protected override long SnapshotEvery => 20;

    protected override void Apply(object domainEvent)
    {
        if (domainEvent is Deposited d)
        {
            State.Balance += d.Amount;
            State.EventCount++;
        }
    }

    protected override async Task ReceiveAsync(object message, CancellationToken cancellationToken)
    {
        switch (message)
        {
            case Deposit d:
                await PersistAsync(new Deposited(d.Amount), cancellationToken);
                break;
            case GetBalance:
                await Context.ReplyAsync(new Balance(State.Balance, State.EventCount), cancellationToken);
                break;
        }
    }
}

/// <summary>
/// Appends an event from inside the recovery hook - the mistake the guard exists to catch.
/// </summary>
/// <remarks>
/// Writing a "recovered" marker event here looks harmless and is not: recovery runs on every
/// activation, so the stream would grow by one event each time the actor woke up, and each of
/// those would be replayed on the next activation.
/// </remarks>
public sealed class BadRecoveryActor : EventSourcedActor<LedgerState>
{
    /// <summary>The exception the base class raised, captured so the test can assert on it.</summary>
    public static Exception? Refusal { get; private set; }

    protected override void Apply(object domainEvent) { }

    protected override async Task OnRecoveryCompletedAsync(int eventsReplayed, CancellationToken cancellationToken)
    {
        Assert.True(IsRecovering);

        try
        {
            await PersistAsync(new Deposited(1m), cancellationToken);
        }
        catch (Exception ex)
        {
            Refusal = ex;
            throw;
        }
    }

    protected override Task ReceiveAsync(object message, CancellationToken cancellationToken) => Task.CompletedTask;
}
