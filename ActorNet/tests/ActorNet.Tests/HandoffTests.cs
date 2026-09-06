// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Diagnostics;
using ActorNet.Cluster;
using ActorNet.Persistence;

namespace ActorNet.Tests;

/// <summary>
/// Handing a node's actors over when it stops on purpose.
/// </summary>
/// <remarks>
/// A node that leaves gracefully has one thing an unplanned failure does not: the chance to put its
/// actors' state where the next owner will look before telling anyone it is going. Getting that
/// order wrong is invisible on a fast store and corrupting on a slow one, which is why these tests
/// use a slow one.
/// </remarks>
public sealed class HandoffTests
{
    /// <summary>A store that takes its time, and notes when each write actually landed.</summary>
    private sealed class SlowStore : IStateStore
    {
        private readonly InMemoryStateStore _inner = new();
        private readonly TimeSpan _delay;
        private readonly Stopwatch _clock;

        public SlowStore(TimeSpan delay, Stopwatch clock)
        {
            _delay = delay;
            _clock = clock;
        }

        public TimeSpan? LastWriteAt { get; private set; }

        public Task<StoredState<T>?> ReadAsync<T>(string key, CancellationToken cancellationToken = default) =>
            _inner.ReadAsync<T>(key, cancellationToken);

        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) =>
            _inner.DeleteAsync(key, cancellationToken);

        public async Task<long> WriteAsync<T>(string key, T state, long expectedVersion = IStateStore.AnyVersion, CancellationToken cancellationToken = default)
        {
            // The delay stands in for a database on the other side of a network. Without it, the
            // wrong order still produces the right answer nearly every time, which is exactly what
            // makes this class of bug survive a test suite.
            await Task.Delay(_delay, cancellationToken);
            var version = await _inner.WriteAsync(key, state, expectedVersion, cancellationToken);
            LastWriteAt = _clock.Elapsed;
            return version;
        }
    }

    [Fact]
    public async Task StateIsFlushedBeforeThePeersAreToldTheNodeIsLeaving()
    {
        var clock = Stopwatch.StartNew();
        var store = new SlowStore(TimeSpan.FromMilliseconds(400), clock);

        await using var harness = new TestHarness();

        var leaving = await harness.NetworkedAsync("hand-a", seeds: [], configure: o => o.StateStore = store);
        var staying = await harness.NetworkedAsync("hand-b", seeds: [$"127.0.0.1:{leaving.BoundPort}"], configure: o => o.StateStore = store);

        await TestHarness.AssertEventuallyAsync(
            () => leaving.Cluster.Members.Count == 2 && staying.Cluster.Members.Count == 2,
            "the cluster should converge", TimeSpan.FromSeconds(15));

        var mine = Enumerable.Range(0, 2000)
            .Select(i => ActorId.For<WalletActor>($"hand-{i}"))
            .First(id => leaving.Cluster.OwnerOf(id) == "hand-a");

        await leaving.TellAsync(mine, new Credit(120m));
        await leaving.AskAsync<Balance>(mine, new GetBalance(), TimeSpan.FromSeconds(10));

        // When the survivor learns the owner has gone, it rebuilds its ring and the very next
        // message to that key activates the actor here instead. That instant is the deadline the
        // flush has to beat.
        TimeSpan? departureSeenAt = null;
        staying.Cluster.MembershipChanged += members =>
        {
            if (departureSeenAt is null && members.Any(m => m.NodeId == "hand-a" && m.Status != MemberStatus.Up))
                departureSeenAt = clock.Elapsed;
        };

        await leaving.StopAsync();

        await TestHarness.AssertEventuallyAsync(
            () => departureSeenAt is not null,
            "the survivor should notice the departure", TimeSpan.FromSeconds(15));

        Assert.NotNull(store.LastWriteAt);
        Assert.True(store.LastWriteAt < departureSeenAt,
            $"state landed at {store.LastWriteAt}, departure was announced at {departureSeenAt}");
    }

    [Fact]
    public async Task TheNextOwnerPicksUpTheStateTheDepartedNodeLeft()
    {
        var store = new SlowStore(TimeSpan.FromMilliseconds(200), Stopwatch.StartNew());

        await using var harness = new TestHarness();

        var leaving = await harness.NetworkedAsync("go-a", seeds: [], configure: o => o.StateStore = store);
        var staying = await harness.NetworkedAsync("go-b", seeds: [$"127.0.0.1:{leaving.BoundPort}"], configure: o => o.StateStore = store);

        await TestHarness.AssertEventuallyAsync(
            () => leaving.Cluster.Members.Count == 2 && staying.Cluster.Members.Count == 2,
            "the cluster should converge", TimeSpan.FromSeconds(15));

        var mine = Enumerable.Range(0, 2000)
            .Select(i => ActorId.For<WalletActor>($"go-{i}"))
            .First(id => leaving.Cluster.OwnerOf(id) == "go-a");

        await leaving.TellAsync(mine, new Credit(75m));
        await leaving.AskAsync<Balance>(mine, new GetBalance(), TimeSpan.FromSeconds(10));

        await leaving.StopAsync();

        await TestHarness.AssertEventuallyAsync(
            () => staying.Cluster.IsSingleNode,
            "the survivor should end up owning the whole ring", TimeSpan.FromSeconds(15));

        // The actor moved, and what moved with it is the balance - through the store, which is what
        // makes an actor's state durable rather than merely resident.
        var balance = await staying.AskAsync<Balance>(mine, new GetBalance(), TimeSpan.FromSeconds(10));
        Assert.Equal(75m, balance.Amount);
    }
}
