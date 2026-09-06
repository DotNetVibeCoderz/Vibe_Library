// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace ActorNet.Tests;

/// <summary>
/// Joining a cluster whose seed is not up yet.
/// </summary>
/// <remarks>
/// The seed handshake used to run exactly once, at startup, so a node that could not reach its
/// seeds in that instant stayed alone forever. Every test before this one started the seed first
/// on loopback, where a connect does not transiently fail - which is why the suite could not catch
/// it and two machines on a wireless link could.
/// </remarks>
public sealed class SeedRetryTests
{
    /// <summary>
    /// Reserves a port and releases it, so it is free but almost certainly still unused.
    /// </summary>
    /// <remarks>
    /// The joiner needs a seed address that refuses connections now and accepts them later, which
    /// means knowing the port before anything listens on it.
    /// </remarks>
    private static int ReservePort()
    {
        var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        probe.Start();
        var port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    [Fact]
    public async Task ANodeThatStartsBeforeItsSeedStillJoins()
    {
        await using var harness = new TestHarness();
        var seedPort = ReservePort();

        // Nothing is listening yet, so the startup handshake is guaranteed to fail - which is the
        // ordinary case whenever nodes start in an arbitrary order.
        var joiner = await harness.NetworkedAsync("late-joiner", seeds: [$"127.0.0.1:{seedPort}"]);

        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.Single(joiner.Cluster.Members);

        // The seed arrives afterwards.
        var seed = await harness.NetworkedAsync("early-seed", seeds: [], configure: o => o.Port = seedPort);

        await TestHarness.AssertEventuallyAsync(
            () => joiner.Cluster.Members.Count == 2 && seed.Cluster.Members.Count == 2,
            "a joiner whose seed was down at startup should join once the seed comes up",
            TimeSpan.FromSeconds(20));

        await TestHarness.AssertRingsAgreeAsync(joiner, seed);
    }

    [Fact]
    public async Task RetryingStopsOnceThereIsAPeer()
    {
        await using var harness = new TestHarness();

        var seed = await harness.NetworkedAsync("stop-seed", seeds: []);
        var joiner = await harness.NetworkedAsync("stop-joiner", seeds: [$"127.0.0.1:{seed.BoundPort}"]);

        await TestHarness.AssertEventuallyAsync(
            () => joiner.Cluster.Members.Count == 2,
            "the cluster should converge", TimeSpan.FromSeconds(15));

        await TestHarness.AssertRingsAgreeAsync(seed, joiner);

        // Several heartbeats later the table must still hold exactly the two of them. A retry that
        // kept firing after the join succeeded would be harmless but wasteful, and a re-seed that
        // duplicated members would not be harmless at all.
        await Task.Delay(TimeSpan.FromSeconds(2));

        Assert.Equal(2, joiner.Cluster.Members.Count);
        Assert.Equal(2, seed.Cluster.Members.Count);
    }

    [Fact]
    public async Task MessagesFlowAfterALateJoin()
    {
        await using var harness = new TestHarness();
        var seedPort = ReservePort();

        var joiner = await harness.NetworkedAsync("late-a", seeds: [$"127.0.0.1:{seedPort}"]);
        var seed = await harness.NetworkedAsync("late-b", seeds: [], configure: o => o.Port = seedPort);

        await TestHarness.AssertEventuallyAsync(
            () => joiner.Cluster.Members.Count == 2 && seed.Cluster.Members.Count == 2,
            "the cluster should converge after the late join", TimeSpan.FromSeconds(20));

        await TestHarness.AssertRingsAgreeAsync(joiner, seed);

        // Converging is not the same as working: the ring has to route and the transport has to
        // carry a reply back.
        var remote = Enumerable.Range(0, 2000)
            .Select(i => ActorId.For<CounterActor>($"late-{i}"))
            .First(id => joiner.Cluster.OwnerOf(id) == "late-b");

        await joiner.TellAsync(remote, new Add(9));
        Assert.Equal(9, (await joiner.AskAsync<Total>(remote, new GetTotal(), TimeSpan.FromSeconds(20))).Value);
    }
}
