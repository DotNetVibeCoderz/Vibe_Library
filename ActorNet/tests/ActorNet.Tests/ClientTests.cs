// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ActorNet.Client;

namespace ActorNet.Tests;

public sealed class ClientTests
{
    private static ActorNetClient ClientFor(ActorSystem system)
    {
        var client = new ActorNetClient("127.0.0.1", system.BoundPort);
        client.RegisterMessage<Add>()
              .RegisterMessage<GetTotal>()
              .RegisterMessage<Total>()
              .RegisterMessage<Ping>()
              .RegisterMessage<Pong>();
        return client;
    }

    [Fact]
    public async Task AnExternalClientCanTellAnActor()
    {
        await using var harness = new TestHarness();
        var system = await harness.NetworkedAsync("node-tell");
        await using var client = ClientFor(system);

        var id = ActorId.For<CounterActor>("from-client");
        await client.TellAsync(id, new Add(6));

        await TestHarness.AssertEventuallyAsync(() => system.LocalActors.Contains(id),
            "the client's message should have activated the actor");
    }

    [Fact]
    public async Task AnExternalClientGetsRepliesToItsAsks()
    {
        await using var harness = new TestHarness();
        var system = await harness.NetworkedAsync("node-ask");
        await using var client = ClientFor(system);

        var id = ActorId.For<CounterActor>("client-ask");
        await client.TellAsync(id, new Add(21));

        // The client is not a cluster member, so the node has no address to dial back. The reply
        // comes down the connection the client opened, which is the whole reason an SDK can use
        // ask rather than only fire-and-forget.
        var total = await client.AskAsync<Total>(id, new GetTotal(), TimeSpan.FromSeconds(10));

        Assert.Equal(21, total.Value);
    }

    [Fact]
    public async Task ManyAsksInFlightAtOnceAreMatchedToTheRightCaller()
    {
        await using var harness = new TestHarness();
        var system = await harness.NetworkedAsync("node-concurrent");
        await using var client = ClientFor(system);

        // Replies arrive interleaved on one socket, so a client that matched them by arrival
        // order rather than by correlation id would hand callers each other's answers.
        var asks = Enumerable.Range(1, 40)
            .Select(i => client.AskAsync<Pong>(ActorId.For<CounterActor>($"echo-{i}"), new Ping(i), TimeSpan.FromSeconds(20)))
            .ToArray();

        var replies = await Task.WhenAll(asks);

        Assert.Equal(Enumerable.Range(1, 40), replies.Select(r => r.Value).Order());
    }

    [Fact]
    public async Task AnAskAgainstAFailingActorSurfacesTheFailure()
    {
        await using var harness = new TestHarness();
        var system = await harness.NetworkedAsync("node-failure");
        system.RegisterActor<AskingFlakyActor>();

        await using var client = ClientFor(system);
        client.RegisterMessage<Boom>();

        var ex = await Assert.ThrowsAsync<ActorNetException>(
            async () => await client.AskAsync<Total>(ActorId.For<AskingFlakyActor>("x"), new Boom("handler failed"), TimeSpan.FromSeconds(10)));

        Assert.Contains("handler failed", ex.Message);
    }

    [Fact]
    public async Task AnAskToASilentActorTimesOutOnTheClientSide()
    {
        await using var harness = new TestHarness();
        var system = await harness.NetworkedAsync("node-silent");
        await using var client = ClientFor(system);

        await Assert.ThrowsAsync<AskTimeoutException>(
            async () => await client.AskAsync<Pong>(ActorId.For<SilentActor>("quiet"), new Ping(1), TimeSpan.FromMilliseconds(400)));
    }

    [Fact]
    public async Task AMessageTypeTheNodeDoesNotKnowIsRefusedRatherThanConstructed()
    {
        await using var harness = new TestHarness();
        var system = await harness.NetworkedAsync("node-allowlist");

        await using var client = new ActorNetClient("127.0.0.1", system.BoundPort);
        client.RegisterMessage<UnknownToTheNode>("tests.not-on-the-node");

        await client.TellAsync(ActorId.For<CounterActor>("victim"), new UnknownToTheNode("payload"));
        await Task.Delay(300);

        // The node resolves aliases through its own allow-list, so a client cannot name a type for
        // it to construct. The frame is refused and the actor is never activated.
        Assert.DoesNotContain(ActorId.For<CounterActor>("victim"), system.LocalActors);
    }
}

/// <summary>A type registered on the client but deliberately not on the node.</summary>
public sealed record UnknownToTheNode(string Value);
