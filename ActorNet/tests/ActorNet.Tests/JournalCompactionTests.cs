// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ActorNet.Persistence;

namespace ActorNet.Tests;

/// <summary>
/// Truncating a journal without losing the state it was holding.
/// </summary>
/// <remarks>
/// <c>DeleteToAsync</c> deletes what it is told to. It has no way to know whether a snapshot exists
/// at that sequence, so getting the number wrong loses an actor's state silently - and the loss only
/// surfaces the next time that actor recovers, which can be weeks later on a node nobody is
/// watching. Every test here is about the check that makes that impossible.
/// </remarks>
public sealed class JournalCompactionTests
{
    private sealed record Counted(int Total);

    private static async Task<(InMemoryEventJournal Journal, InMemorySnapshotStore Snapshots, JournalCompactor Compactor)> WithEventsAsync(
        string id, int count)
    {
        var journal = new InMemoryEventJournal();
        var snapshots = new InMemorySnapshotStore();

        for (var i = 1; i <= count; i++)
            await journal.AppendAsync(id, [new Counted(i)], cancellationToken: TestContext.Current.CancellationToken);

        return (journal, snapshots, new JournalCompactor(journal, snapshots));
    }

    private static async Task<long[]> SequencesAsync(IEventJournal journal, string id)
    {
        var sequences = new List<long>();
        await foreach (var entry in journal.ReadAsync(id, 0, TestContext.Current.CancellationToken))
            sequences.Add(entry.Sequence);

        return [.. sequences];
    }

    [Fact]
    public async Task NothingIsDeletedWithoutASnapshot()
    {
        var (journal, _, compactor) = await WithEventsAsync("no-snap", 10);

        var result = await compactor.CompactAsync<Counted>("no-snap", cancellationToken: TestContext.Current.CancellationToken);

        // This is the case that loses an actor. Without a snapshot every event is still needed to
        // recover, so there is nothing safe to delete, and saying why beats deleting nothing quietly.
        Assert.False(result.Compacted);
        Assert.Contains("no snapshot", result.Reason!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(10, (await SequencesAsync(journal, "no-snap")).Length);
    }

    [Fact]
    public async Task NothingIsDeletedPastWhatTheSnapshotCovers()
    {
        var (journal, snapshots, compactor) = await WithEventsAsync("partial", 10);

        // The snapshot is old: it covers six events, and four have happened since.
        await snapshots.SaveAsync("partial", new Counted(6), 6, TestContext.Current.CancellationToken);

        var result = await compactor.CompactAsync<Counted>("partial", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(6, result.DeletedTo);
        Assert.Equal(new long[] { 7, 8, 9, 10 }, await SequencesAsync(journal, "partial"));
    }

    [Fact]
    public async Task APolicyCanKeepATailOfHistory()
    {
        var (journal, snapshots, compactor) = await WithEventsAsync("tail", 10);
        await snapshots.SaveAsync("tail", new Counted(10), 10, TestContext.Current.CancellationToken);

        var result = await compactor.CompactAsync<Counted>("tail", new CompactionPolicy(KeepEvents: 4),
            TestContext.Current.CancellationToken);

        // A snapshot makes the events before it unnecessary for recovery, which is not the same as
        // making them unwanted: they are the audit trail and the input to a projection that has not
        // caught up yet.
        Assert.Equal(6, result.DeletedTo);
        Assert.Equal(new long[] { 7, 8, 9, 10 }, await SequencesAsync(journal, "tail"));
    }

    [Fact]
    public async Task ATailLongerThanTheStreamKeepsEverything()
    {
        var (journal, snapshots, compactor) = await WithEventsAsync("short", 3);
        await snapshots.SaveAsync("short", new Counted(3), 3, TestContext.Current.CancellationToken);

        var result = await compactor.CompactAsync<Counted>("short", new CompactionPolicy(KeepEvents: 100),
            TestContext.Current.CancellationToken);

        Assert.False(result.Compacted);
        Assert.Equal(new long[] { 1, 2, 3 }, await SequencesAsync(journal, "short"));
    }

    [Fact]
    public async Task ATimeBasedPolicyKeepsWhatIsRecent()
    {
        var (journal, snapshots, compactor) = await WithEventsAsync("aged", 5);
        await snapshots.SaveAsync("aged", new Counted(5), 5, TestContext.Current.CancellationToken);

        // Everything was written moments ago, so a window of an hour covers all of it.
        var result = await compactor.CompactAsync<Counted>("aged", new CompactionPolicy(KeepFor: TimeSpan.FromHours(1)),
            TestContext.Current.CancellationToken);

        Assert.False(result.Compacted);
        Assert.Equal(5, (await SequencesAsync(journal, "aged")).Length);

        // A window of nothing keeps nothing, which is the same as the aggressive policy.
        var everything = await compactor.CompactAsync<Counted>("aged", new CompactionPolicy(KeepFor: TimeSpan.Zero),
            TestContext.Current.CancellationToken);

        Assert.Equal(5, everything.DeletedTo);
        Assert.Empty(await SequencesAsync(journal, "aged"));
    }

    [Fact]
    public async Task TheSnapshotIsWrittenBeforeAnythingIsDeleted()
    {
        var (journal, snapshots, compactor) = await WithEventsAsync("pair", 8);

        var result = await compactor.SnapshotAndCompactAsync("pair", new Counted(8), 8,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(8, result.DeletedTo);
        Assert.Empty(await SequencesAsync(journal, "pair"));

        // In this order, always. Truncating before the snapshot is stored is the one sequence that
        // loses an actor's state outright, and it is an easy thing to write by accident.
        var stored = await snapshots.LoadAsync<Counted>("pair", TestContext.Current.CancellationToken);
        Assert.NotNull(stored);
        Assert.Equal(8, stored.Sequence);
    }

    [Fact]
    public async Task AnActorStillRecoversFromACompactedJournal()
    {
        await using var harness = new TestHarness();
        var system = await harness.LocalAsync();

        var journal = system.Options.EventJournal;
        var snapshots = system.Options.SnapshotStore;

        var id = ActorId.For<LedgerActor>("compacted");
        for (var i = 0; i < 12; i++)
            await system.TellAsync(id, new Deposit(10m));

        var before = await system.AskAsync<Balance>(id, new GetBalance(), TimeSpan.FromSeconds(10));
        Assert.Equal(120m, before.Amount);

        var compactor = new JournalCompactor(journal, snapshots);
        var persistenceId = id.ToString();
        var highest = await journal.HighestSequenceAsync(persistenceId, TestContext.Current.CancellationToken);

        var result = await compactor.SnapshotAndCompactAsync(
            persistenceId,
            new LedgerState { Balance = before.Amount, EventCount = before.Operations },
            highest,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Compacted);

        // Deactivated after the compaction, so the next ask has to recover from the snapshot alone
        // - an actor still holding its state in memory would mask a recovery that had lost it.
        await system.DeactivateAsync(id);

        // The point of all of it: the events are gone and the balance is not.
        var after = await system.AskAsync<Balance>(id, new GetBalance(), TimeSpan.FromSeconds(10));
        Assert.Equal(120m, after.Amount);
    }
}
