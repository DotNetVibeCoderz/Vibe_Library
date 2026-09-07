// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Text.Json;

namespace ActorNet.Tests;

/// <summary>
/// Asking an actor what it is holding, without having written a message for it first.
/// </summary>
/// <remarks>
/// Answering a question about an actor otherwise means writing a message, handling it, and
/// registering both - fine for a question you knew you would ask, useless at three in the morning
/// for one you did not.
/// </remarks>
public sealed class InspectionTests
{
    [Fact]
    public async Task APersistentActorReportsItsState()
    {
        await using var harness = new TestHarness();
        var system = await harness.LocalAsync();

        var id = ActorId.For<WalletActor>("inspect-wallet");
        await system.TellAsync(id, new Credit(250m));
        await system.AskAsync<Balance>(id, new GetBalance(), TimeSpan.FromSeconds(10));

        var inspected = await system.InspectAsync(id, TimeSpan.FromSeconds(10));

        Assert.Equal(id.ToString(), inspected.Actor);
        Assert.Equal("WalletActor", inspected.ActorType);

        // The State, not the actor wrapped around it: the base class holds the plumbing and the
        // state holds the answer.
        var state = JsonSerializer.Deserialize<JsonElement>(inspected.State!);
        Assert.Equal(250m, state.GetProperty("Balance").GetDecimal());
        Assert.Equal(1, state.GetProperty("Operations").GetInt32());
    }

    [Fact]
    public async Task APlainActorReportsItself()
    {
        await using var harness = new TestHarness();
        var system = await harness.LocalAsync();

        var id = ActorId.For<CounterActor>("inspect-counter");
        await system.TellAsync(id, new Add(4));
        await system.AskAsync<Total>(id, new GetTotal(), TimeSpan.FromSeconds(10));

        var inspected = await system.InspectAsync(id, TimeSpan.FromSeconds(10));

        // No State property, so the instance is reported - private fields included, because that is
        // where an actor without a persistence base class keeps everything.
        var state = JsonSerializer.Deserialize<JsonElement>(inspected.State!);
        Assert.Equal(4, state.GetProperty("_total").GetInt32());
        Assert.True(inspected.MessagesHandled >= 2);
    }

    [Fact]
    public async Task AnActorDecidesWhatItShows()
    {
        await using var harness = new TestHarness();
        var system = await harness.LocalAsync();
        system.RegisterActor<SecretiveActor>();

        var id = ActorId.For<SecretiveActor>("inspect-secret");
        await system.TellAsync(id, new Add(1));
        await system.AskAsync<Total>(id, new GetTotal(), TimeSpan.FromSeconds(10));

        var inspected = await system.InspectAsync(id, TimeSpan.FromSeconds(10));

        // Reflection over the instance would have reported the token. An actor holding a secret
        // says what it wants seen instead.
        Assert.DoesNotContain("hunter2", inspected.State, StringComparison.Ordinal);
        Assert.Contains("visible", inspected.State!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnActorCanShowNothing()
    {
        await using var harness = new TestHarness();
        var system = await harness.LocalAsync();
        system.RegisterActor<PrivateActor>();

        var inspected = await system.InspectAsync(ActorId.For<PrivateActor>("hidden"), TimeSpan.FromSeconds(10));

        Assert.Null(inspected.State);
        Assert.Equal("PrivateActor", inspected.ActorType);
    }

    [Fact]
    public async Task StateThatWillNotSerializeIsReportedRatherThanThrown()
    {
        await using var harness = new TestHarness();
        var system = await harness.LocalAsync();
        system.RegisterActor<AwkwardActor>();

        var inspected = await system.InspectAsync(ActorId.For<AwkwardActor>("awkward"), TimeSpan.FromSeconds(10));

        // An inspector that throws teaches nothing about what it was inspecting.
        Assert.Contains("not serializable", inspected.State!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnActorOnAnotherNodeAnswersToo()
    {
        await using var harness = new TestHarness();

        var here = await harness.NetworkedAsync("look-a", seeds: []);
        var there = await harness.NetworkedAsync("look-b", seeds: [$"127.0.0.1:{here.BoundPort}"]);

        await TestHarness.AssertEventuallyAsync(
            () => here.Cluster.Members.Count == 2 && there.Cluster.Members.Count == 2,
            "the cluster should converge", TimeSpan.FromSeconds(15));

        await TestHarness.AssertRingsAgreeAsync(here, there);

        var remote = Enumerable.Range(0, 2000)
            .Select(i => ActorId.For<CounterActor>($"look-{i}"))
            .First(id => here.Cluster.OwnerOf(id) == "look-b");

        await here.TellAsync(remote, new Add(11));

        var inspected = await here.InspectAsync(remote, TimeSpan.FromSeconds(15));

        // Routed by the ring like any other message, which is the point of it being one.
        var state = JsonSerializer.Deserialize<JsonElement>(inspected.State!);
        Assert.Equal(11, state.GetProperty("_total").GetInt32());
    }
}

/// <summary>Holds something it does not want reported, and says so.</summary>
public sealed class SecretiveActor : ReceiveActor, IInspectable
{
    private readonly string _token = "hunter2";
    private int _total;

    public SecretiveActor()
    {
        On<Add>(m => _total += m.By);
        On<GetTotal>(async (_, ct) => await Context.ReplyAsync(new Total(_total), ct));
    }

    public object? Inspect() => new { visible = _total, secret = _token.Length };
}

/// <summary>Shows nothing at all.</summary>
public sealed class PrivateActor : ReceiveActor, IInspectable
{
    public PrivateActor() => On<Add>(_ => { });

    public object? Inspect() => null;
}

/// <summary>Holds state the serializer cannot deal with.</summary>
public sealed class AwkwardActor : ReceiveActor, IInspectable
{
    public AwkwardActor() => On<Add>(_ => { });

    public object? Inspect() => new NotSerializable();

    private sealed class NotSerializable
    {
        public string Boom => throw new InvalidOperationException("no");
    }
}
