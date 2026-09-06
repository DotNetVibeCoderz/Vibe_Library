// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ActorNet.Client;

namespace ActorNet.Tests;

/// <summary>
/// A client that survives the node it dialled.
/// </summary>
/// <remarks>
/// A client bound to one node goes down with it, which is a strange property for a client of a
/// cluster - and it does not matter which node it reaches, because a node that does not own the
/// target key forwards by the ring. Every node is an equally correct entrance.
/// </remarks>
public sealed class ClientFailoverTests
{
    private static ActorNetClient Client(params string[] endpoints)
    {
        var client = new ActorNetClient(endpoints);
        client.RegisterMessage<Add>();
        client.RegisterMessage<GetTotal>();
        client.RegisterMessage<Total>();
        return client;
    }

    [Fact]
    public async Task ADeadFirstEndpointIsSteppedOver()
    {
        await using var harness = new TestHarness();
        var node = await harness.NetworkedAsync("fo-a", seeds: []);

        // TEST-NET-1, which refuses or drops but never answers. The client has to try the next one
        // rather than reporting the whole cluster unreachable.
        await using var client = Client("192.0.2.1:9000", $"127.0.0.1:{node.BoundPort}");

        await client.ConnectAsync(TestContext.Current.CancellationToken);

        Assert.Equal($"127.0.0.1:{node.BoundPort}", client.ConnectedTo);
    }

    [Fact]
    public async Task WorkContinuesOnAnotherNodeAfterTheFirstOneStops()
    {
        await using var harness = new TestHarness();

        var first = await harness.NetworkedAsync("fo-first", seeds: []);
        var second = await harness.NetworkedAsync("fo-second", seeds: [$"127.0.0.1:{first.BoundPort}"]);

        await TestHarness.AssertEventuallyAsync(
            () => first.Cluster.Members.Count == 2 && second.Cluster.Members.Count == 2,
            "the cluster should converge", TimeSpan.FromSeconds(15));

        await using var client = Client($"127.0.0.1:{first.BoundPort}", $"127.0.0.1:{second.BoundPort}");

        await client.TellAsync(ActorId.For<CounterActor>("fo-counter"), new Add(4), TestContext.Current.CancellationToken);
        Assert.Equal($"127.0.0.1:{first.BoundPort}", client.ConnectedTo);

        // The entrance goes away. Its actors move by the usual rebalance, and the client has to
        // find another door rather than reporting the cluster gone.
        await first.StopAsync();

        await TestHarness.AssertEventuallyAsync(
            () => second.Cluster.IsSingleNode,
            "the survivor should own the whole ring", TimeSpan.FromSeconds(20));

        var total = await client.AskAsync<Total>(ActorId.For<CounterActor>("fo-counter"), new GetTotal(),
            TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);

        Assert.Equal($"127.0.0.1:{second.BoundPort}", client.ConnectedTo);

        // The state moved with the actor. The count is 4 or 0 depending on whether the actor had
        // been flushed, and asserting on it would be asserting on persistence rather than failover -
        // what matters here is that the call was answered at all.
        Assert.True(total.Value is 0 or 4, $"unexpected total {total.Value}");
    }

    [Fact]
    public async Task TheEndpointStaysWhereItIsWhileItWorks()
    {
        await using var harness = new TestHarness();

        var a = await harness.NetworkedAsync("stick-a", seeds: []);
        var b = await harness.NetworkedAsync("stick-b", seeds: [$"127.0.0.1:{a.BoundPort}"]);

        await using var client = Client($"127.0.0.1:{a.BoundPort}", $"127.0.0.1:{b.BoundPort}");

        for (var i = 0; i < 5; i++)
        {
            await client.TellAsync(ActorId.For<CounterActor>($"stick-{i}"), new Add(1), TestContext.Current.CancellationToken);
            await client.ConnectAsync(TestContext.Current.CancellationToken);
        }

        // Rotating on every connect would reconnect somewhere new after every blip, which is churn
        // rather than balance: any node forwards by the ring, so moving gains nothing and costs a
        // connection.
        Assert.Equal($"127.0.0.1:{a.BoundPort}", client.ConnectedTo);
    }

    [Fact]
    public async Task WithNoNodeReachableTheErrorNamesEveryEndpointTried()
    {
        await using var client = Client("192.0.2.1:9000", "192.0.2.2:9001");

        var error = await Assert.ThrowsAsync<ActorNetException>(
            () => client.ConnectAsync(TestContext.Current.CancellationToken));

        Assert.Contains("192.0.2.1:9000", error.Message);
        Assert.Contains("192.0.2.2:9001", error.Message);
        Assert.Null(client.ConnectedTo);
    }

    [Fact]
    public void AMalformedEndpointIsRefusedAtConstruction()
    {
        // Rather than at the first call, which could be hours later and somewhere else entirely.
        var error = Assert.Throws<FormatException>(() => new ActorNetClient(["localhost"]));
        Assert.Contains("host:port", error.Message);
    }
}
