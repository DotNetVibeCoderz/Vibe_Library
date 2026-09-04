// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ActorNet.Persistence;
using ActorNet.Persistence.MySql;
using ActorNet.Persistence.PostgreSql;
using ActorNet.Persistence.Redis;
using ActorNet.Persistence.Relational;
using ActorNet.Persistence.Sqlite;
using ActorNet.Serialization;
using StackExchange.Redis;

namespace ActorNet.Tests;

/// <summary>
/// One conformance suite, run against every provider.
/// </summary>
/// <remarks>
/// <para>
/// The three store interfaces are a contract, and a provider that satisfies it is
/// interchangeable with any other. Writing the tests once and running them against each provider is
/// the only way to hold that line - a per-provider test file drifts, and the drift is invisible
/// until an actor moves between nodes and finds state that does not load.
/// </para>
/// <para>
/// SQLite always runs, because it is a real database with real SQL and real constraint enforcement
/// that needs no server. The server-backed providers self-skip unless their connection string is in
/// the environment, and CI supplies those through service containers - so "PostgreSQL works" is a
/// claim something actually checks rather than one this file merely asserts.
/// </para>
/// </remarks>
public abstract class PersistenceProviderConformance : IAsyncLifetime
{
    private string _prefix = null!;

    /// <summary>Null when this provider has nothing to connect to, which skips every test.</summary>
    protected abstract ProviderFixture? Fixture { get; }

    /// <summary>How to reach this provider, named in the skip message so a run says what it wants.</summary>
    protected virtual string RequiredEnvironmentVariable => "(always available)";

    /// <summary>
    /// The fixture, or a genuine skip.
    /// </summary>
    /// <remarks>
    /// A skip rather than an early return. A test that quietly returns is reported as passing,
    /// which would let "PostgreSQL conforms" appear green on a machine that has never seen a
    /// PostgreSQL server - the exact claim this suite exists to make honestly.
    /// </remarks>
    protected ProviderFixture Require()
    {
        if (Fixture is { } fixture) return fixture;

        Assert.Skip($"Set {RequiredEnvironmentVariable} to a connection string to run the {GetType().Name} suite.");
        throw new InvalidOperationException("unreachable");
    }

    /// <summary>A key unique to this test run, so a shared database does not leak between runs.</summary>
    protected string Key(string name) => $"{_prefix}{name}";

    public ValueTask InitializeAsync()
    {
        _prefix = $"t{Guid.NewGuid():N}"[..12] + "/";
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => Fixture?.DisposeAsync() ?? ValueTask.CompletedTask;

    [Fact]
    public async Task StateRoundTripsThroughTheStore()
    {
        var f = Require();

        var key = Key("wallet-1");
        Assert.Null(await f.State.ReadAsync<WalletState>(key));

        var version = await f.State.WriteAsync(key, new WalletState { Balance = 42.5m, Operations = 3 });
        Assert.Equal(1, version);

        var stored = await f.State.ReadAsync<WalletState>(key);
        Assert.NotNull(stored);
        Assert.Equal(42.5m, stored!.Value.Value.Balance);
        Assert.Equal(3, stored.Value.Value.Operations);
        Assert.Equal(1, stored.Value.Version);
    }

    [Fact]
    public async Task WritingAgainAdvancesTheVersion()
    {
        var f = Require();

        var key = Key("wallet-2");
        var first = await f.State.WriteAsync(key, new WalletState { Balance = 1 });
        var second = await f.State.WriteAsync(key, new WalletState { Balance = 2 }, first);

        Assert.Equal(first + 1, second);
        Assert.Equal(2m, (await f.State.ReadAsync<WalletState>(key))!.Value.Value.Balance);
    }

    [Fact]
    public async Task AWriteBuiltOnAStaleReadIsRefused()
    {
        var f = Require();

        var key = Key("wallet-3");
        var version = await f.State.WriteAsync(key, new WalletState { Balance = 1 });
        await f.State.WriteAsync(key, new WalletState { Balance = 2 }, version);

        // This is the split-activation case: two writers both read version 1. Whichever arrives
        // second must be told rather than allowed to overwrite.
        var ex = await Assert.ThrowsAsync<StateConcurrencyException>(
            async () => await f.State.WriteAsync(key, new WalletState { Balance = 3 }, version));

        Assert.Equal(version, ex.ExpectedVersion);
        Assert.Equal(version + 1, ex.ActualVersion);
        Assert.Equal(2m, (await f.State.ReadAsync<WalletState>(key))!.Value.Value.Balance);
    }

    [Fact]
    public async Task ConcurrentWritersCannotBothWinTheSameVersion()
    {
        var f = Require();

        var key = Key("wallet-4");
        var version = await f.State.WriteAsync(key, new WalletState { Balance = 0 });

        // Sixteen writers, all working from the same version. Exactly one may succeed; the rest
        // must be refused. Anything else means the check and the write are separable.
        var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(async i =>
        {
            try
            {
                await f.State.WriteAsync(key, new WalletState { Balance = i }, version);
                return true;
            }
            catch (StateConcurrencyException)
            {
                return false;
            }
        }));

        Assert.Equal(1, results.Count(won => won));
    }

    [Fact]
    public async Task DeletingRemovesTheState()
    {
        var f = Require();

        var key = Key("wallet-5");
        await f.State.WriteAsync(key, new WalletState { Balance = 9 });
        await f.State.DeleteAsync(key);

        Assert.Null(await f.State.ReadAsync<WalletState>(key));
    }

    [Fact]
    public async Task EventsReplayInTheOrderTheyWereAppended()
    {
        var f = Require();

        var id = Key("ledger-1");
        await f.Journal.AppendAsync(id, [new Deposited(10m), new Withdrawn(4m), new Deposited(2m)]);

        var entries = new List<JournalEntry>();
        await foreach (var entry in f.Journal.ReadAsync(id)) entries.Add(entry);

        Assert.Equal(3, entries.Count);
        Assert.Equal([1L, 2L, 3L], entries.Select(e => e.Sequence));
        Assert.Equal(10m, Assert.IsType<Deposited>(entries[0].Event).Amount);
        Assert.Equal(4m, Assert.IsType<Withdrawn>(entries[1].Event).Amount);
        Assert.Equal(2m, Assert.IsType<Deposited>(entries[2].Event).Amount);
    }

    [Fact]
    public async Task IdenticalEventsAreBothKept()
    {
        var f = Require();

        var id = Key("ledger-2");

        // Two deposits of the same amount are two events, not one. A journal that de-duplicates
        // them - which a naive Redis set would - silently halves an account's balance.
        await f.Journal.AppendAsync(id, [new Deposited(5m), new Deposited(5m)]);

        var entries = new List<JournalEntry>();
        await foreach (var entry in f.Journal.ReadAsync(id)) entries.Add(entry);

        Assert.Equal(2, entries.Count);
        Assert.Equal(2, await f.Journal.HighestSequenceAsync(id));
    }

    [Fact]
    public async Task ReadingForwardSkipsWhatWasAlreadySeen()
    {
        var f = Require();

        var id = Key("ledger-3");
        await f.Journal.AppendAsync(id, [new Deposited(1m), new Deposited(2m), new Deposited(3m)]);

        var entries = new List<JournalEntry>();
        await foreach (var entry in f.Journal.ReadAsync(id, fromSequence: 1)) entries.Add(entry);

        // fromSequence is exclusive: this is how recovery replays only what a snapshot does not
        // already cover.
        Assert.Equal([2L, 3L], entries.Select(e => e.Sequence));
    }

    [Fact]
    public async Task AnAppendBuiltOnAStaleTipIsRefused()
    {
        var f = Require();

        var id = Key("ledger-4");
        var sequence = await f.Journal.AppendAsync(id, [new Deposited(1m)]);
        await f.Journal.AppendAsync(id, [new Deposited(2m)], sequence);

        var ex = await Assert.ThrowsAsync<StateConcurrencyException>(
            async () => await f.Journal.AppendAsync(id, [new Deposited(3m)], sequence));

        Assert.Equal(sequence, ex.ExpectedVersion);
        Assert.Equal(2, await f.Journal.HighestSequenceAsync(id));
    }

    [Fact]
    public async Task ConcurrentAppendersCannotInterleaveOneStream()
    {
        var f = Require();

        var id = Key("ledger-5");
        var tip = await f.Journal.AppendAsync(id, [new Deposited(1m)]);

        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(async i =>
        {
            try
            {
                await f.Journal.AppendAsync(id, [new Deposited(i)], tip);
                return true;
            }
            catch (StateConcurrencyException)
            {
                return false;
            }
        }));

        // Exactly one append may extend the stream from a given tip. The rest have to be refused,
        // or an actor's history contains events from two activations that never saw each other.
        Assert.Equal(1, results.Count(won => won));
        Assert.Equal(2, await f.Journal.HighestSequenceAsync(id));
    }

    [Fact]
    public async Task TruncationDropsOnlyWhatItWasAskedTo()
    {
        var f = Require();

        var id = Key("ledger-6");
        await f.Journal.AppendAsync(id, [new Deposited(1m), new Deposited(2m), new Deposited(3m), new Deposited(4m)]);

        await f.Journal.DeleteToAsync(id, 2);

        var entries = new List<JournalEntry>();
        await foreach (var entry in f.Journal.ReadAsync(id)) entries.Add(entry);

        Assert.Equal([3L, 4L], entries.Select(e => e.Sequence));

        // The tip does not move backwards. Sequence numbers are the stream's identity, so a
        // truncated stream continues from where it was, not from where it now starts.
        Assert.Equal(4, await f.Journal.HighestSequenceAsync(id));
    }

    [Fact]
    public async Task AnEmptyStreamHasNoEventsAndSequenceZero()
    {
        var f = Require();

        var id = Key("ledger-empty");
        Assert.Equal(0, await f.Journal.HighestSequenceAsync(id));

        var count = 0;
        await foreach (var _ in f.Journal.ReadAsync(id)) count++;
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task SnapshotsRoundTripAndOverwrite()
    {
        var f = Require();

        var id = Key("snap-1");
        Assert.Null(await f.Snapshots.LoadAsync<LedgerState>(id));

        await f.Snapshots.SaveAsync(id, new LedgerState { Balance = 100m, EventCount = 20 }, 20);
        var first = await f.Snapshots.LoadAsync<LedgerState>(id);
        Assert.Equal(100m, first!.State.Balance);
        Assert.Equal(20, first.Sequence);

        // Last write wins: a snapshot is an optimization over replay, so there is nothing to
        // reconcile between two of them.
        await f.Snapshots.SaveAsync(id, new LedgerState { Balance = 250m, EventCount = 40 }, 40);
        var second = await f.Snapshots.LoadAsync<LedgerState>(id);
        Assert.Equal(250m, second!.State.Balance);
        Assert.Equal(40, second.Sequence);

        await f.Snapshots.DeleteAsync(id);
        Assert.Null(await f.Snapshots.LoadAsync<LedgerState>(id));
    }

    [Fact]
    public async Task StoredStateIsACopyNotAReference()
    {
        var f = Require();

        var key = Key("wallet-copy");
        var state = new WalletState { Balance = 10m };
        await f.State.WriteAsync(key, state);

        // The actor keeps mutating its own instance after the write. A store that held a reference
        // would keep changing after it was written - the bug that made a snapshot taken at one
        // sequence read back as the state at a later one.
        state.Balance = 999m;

        Assert.Equal(10m, (await f.State.ReadAsync<WalletState>(key))!.Value.Value.Balance);
    }

    [Fact]
    public async Task AnActorReloadsItsStateThroughTheProvider()
    {
        var f = Require();

        await using var harness = new TestHarness();
        var system = await harness.LocalAsync(o =>
        {
            o.StateStore = f.State;
            o.EventJournal = f.Journal;
            o.SnapshotStore = f.Snapshots;
        });

        var id = ActorId.For<WalletActor>(Key("live"));
        var wallet = system.ActorOf(id);

        await wallet.TellAsync(new Credit(120m));
        await wallet.TellAsync(new Credit(30m));
        Assert.Equal(150m, (await wallet.AskAsync<Balance>(new GetBalance())).Amount);

        await system.DeactivateAsync(id);
        await TestHarness.AssertEventuallyAsync(() => !system.LocalActors.Contains(id), "the actor should have deactivated");

        // The whole point of a shared provider: the actor went away and came back with its state,
        // through the same path a rebalance onto another node would take.
        Assert.Equal(150m, (await wallet.AskAsync<Balance>(new GetBalance())).Amount);
    }
}

/// <summary>The three stores under test, plus whatever needs disposing afterwards.</summary>
public sealed record ProviderFixture(
    IStateStore State,
    IEventJournal Journal,
    ISnapshotStore Snapshots,
    Func<ValueTask>? Cleanup = null)
{
    public ValueTask DisposeAsync() => Cleanup?.Invoke() ?? ValueTask.CompletedTask;

    /// <summary>The event types the conformance suite writes.</summary>
    public static MessageTypeRegistry Types()
    {
        var types = new MessageTypeRegistry();
        types.Register<Deposited>("tests.deposited");
        types.Register<Withdrawn>("tests.withdrawn");
        return types;
    }
}

/// <summary>The in-memory stores, which are the default and the dev/test provider.</summary>
public sealed class InMemoryProviderTests : PersistenceProviderConformance
{
    protected override ProviderFixture? Fixture { get; } = new(
        new InMemoryStateStore(), new InMemoryEventJournal(), new InMemorySnapshotStore());
}

/// <summary>The file stores, which survive a process restart but are not shared between nodes.</summary>
public sealed class FileProviderTests : PersistenceProviderConformance
{
    private static readonly string Directory =
        Path.Combine(Path.GetTempPath(), "actornet-tests", Guid.NewGuid().ToString("N"));

    protected override ProviderFixture? Fixture { get; } = new(
        new FileStateStore(Path.Combine(Directory, "state")),
        new FileEventJournal(Path.Combine(Directory, "journal"), ProviderFixture.Types()),
        new FileSnapshotStore(Path.Combine(Directory, "snapshots")),
        Cleanup: () =>
        {
            if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, recursive: true);
            return ValueTask.CompletedTask;
        });
}

/// <summary>
/// SQLite, which always runs.
/// </summary>
/// <remarks>
/// A file rather than <c>:memory:</c>, because an in-memory SQLite database is scoped to its
/// connection and these stores open one per operation. The temporary file is what makes the
/// concurrency tests exercise real constraint enforcement rather than three empty databases.
/// </remarks>
public sealed class SqliteProviderTests : PersistenceProviderConformance
{
    private static readonly string Path_ =
        Path.Combine(Path.GetTempPath(), $"actornet-{Guid.NewGuid():N}.db");

    private static readonly string ConnectionString = $"Data Source={Path_}";

    protected override ProviderFixture? Fixture { get; } = new(
        SqlitePersistence.StateStore(ConnectionString),
        SqlitePersistence.EventJournal(ConnectionString, ProviderFixture.Types()),
        SqlitePersistence.SnapshotStore(ConnectionString),
        Cleanup: () =>
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(Path_)) File.Delete(Path_);
            return ValueTask.CompletedTask;
        });
}

/// <summary>PostgreSQL. Set ACTORNET_TEST_POSTGRES to a connection string to run it.</summary>
public sealed class PostgreSqlProviderTests : PersistenceProviderConformance
{
    protected override string RequiredEnvironmentVariable => "ACTORNET_TEST_POSTGRES";

    protected override ProviderFixture? Fixture { get; } =
        Environment.GetEnvironmentVariable("ACTORNET_TEST_POSTGRES") is { Length: > 0 } connectionString
            ? new ProviderFixture(
                PostgreSqlPersistence.StateStore(connectionString),
                PostgreSqlPersistence.EventJournal(connectionString, ProviderFixture.Types()),
                PostgreSqlPersistence.SnapshotStore(connectionString))
            : null;
}

/// <summary>SQL Server. Set ACTORNET_TEST_SQLSERVER to a connection string to run it.</summary>
public sealed class SqlServerProviderTests : PersistenceProviderConformance
{
    protected override string RequiredEnvironmentVariable => "ACTORNET_TEST_SQLSERVER";

    protected override ProviderFixture? Fixture { get; } =
        Environment.GetEnvironmentVariable("ACTORNET_TEST_SQLSERVER") is { Length: > 0 } connectionString
            ? new ProviderFixture(
                Persistence.SqlServer.SqlServerPersistence.StateStore(connectionString),
                Persistence.SqlServer.SqlServerPersistence.EventJournal(connectionString, ProviderFixture.Types()),
                Persistence.SqlServer.SqlServerPersistence.SnapshotStore(connectionString))
            : null;
}

/// <summary>MySQL. Set ACTORNET_TEST_MYSQL to a connection string to run it.</summary>
public sealed class MySqlProviderTests : PersistenceProviderConformance
{
    protected override string RequiredEnvironmentVariable => "ACTORNET_TEST_MYSQL";

    protected override ProviderFixture? Fixture { get; } =
        Environment.GetEnvironmentVariable("ACTORNET_TEST_MYSQL") is { Length: > 0 } connectionString
            ? new ProviderFixture(
                MySqlPersistence.StateStore(connectionString),
                MySqlPersistence.EventJournal(connectionString, ProviderFixture.Types()),
                MySqlPersistence.SnapshotStore(connectionString))
            : null;
}

/// <summary>Redis. Set ACTORNET_TEST_REDIS to a connection string to run it.</summary>
public sealed class RedisProviderTests : PersistenceProviderConformance
{
    protected override string RequiredEnvironmentVariable => "ACTORNET_TEST_REDIS";

    protected override ProviderFixture? Fixture { get; } = Build();

    private static ProviderFixture? Build()
    {
        if (Environment.GetEnvironmentVariable("ACTORNET_TEST_REDIS") is not { Length: > 0 } connectionString)
            return null;

        var connection = ConnectionMultiplexer.Connect(connectionString);
        var options = new RedisStoreOptions { KeyPrefix = $"actornet-test-{Guid.NewGuid():N}:" };

        return new ProviderFixture(
            RedisPersistence.StateStore(connection, options),
            RedisPersistence.EventJournal(connection, ProviderFixture.Types(), options),
            RedisPersistence.SnapshotStore(connection, options),
            Cleanup: async () =>
            {
                // Each run uses its own key prefix, so cleanup is a scan-and-delete of that prefix
                // rather than a FLUSHDB, which would take out whatever else shares the server.
                foreach (var endpoint in connection.GetEndPoints())
                {
                    var server = connection.GetServer(endpoint);
                    if (server.IsReplica) continue;

                    foreach (var key in server.Keys(pattern: $"{options.KeyPrefix}*"))
                        await connection.GetDatabase(options.Database).KeyDeleteAsync(key);
                }

                await connection.DisposeAsync();
            });
    }
}
