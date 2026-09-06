// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using ActorNet.Network;
using ActorNet.Runtime;
using ActorNet.Serialization;
using Microsoft.Extensions.Logging.Abstractions;

namespace ActorNet.Tests;

public sealed record Block;

/// <summary>Holds its mailbox loop until released, so a bounded mailbox can be filled on purpose.</summary>
public sealed class BlockingActor : ReceiveActor
{
    /// <summary>One gate per actor key, opened by the test when it is done.</summary>
    public static readonly ConcurrentDictionary<string, TaskCompletionSource> Gates = new();

    public static TaskCompletionSource GateFor(string key) =>
        Gates.GetOrAdd(key, static _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

    public BlockingActor()
    {
        On<Block>(async (_, ct) => await GateFor(Context.Self.Key).Task.WaitAsync(TimeSpan.FromSeconds(30), ct));
        On<Add>(static _ => { });
        On<GetTotal>(async (_, ct) => await Context.ReplyAsync(new Total(0), ct));
    }
}

/// <summary>Registered on the sending node only, so the receiver refuses it.</summary>
public sealed class SenderOnlyActor : ReceiveActor
{
    public SenderOnlyActor() => On<Add>(static _ => { });
}

/// <summary>
/// Flow control across a node boundary.
/// </summary>
/// <remarks>
/// A bounded mailbox gave the local sender backpressure and gave a remote one a stall it could not
/// see. These check the two halves of that: the stall is now bounded and diagnosable, and the
/// failures a caller gets say which side was at fault.
/// </remarks>
public sealed class BackpressureTests
{
    /// <summary>A key the sending node does not own, so the send genuinely crosses the network.</summary>
    private static ActorId RemoteKey<TActor>(ActorSystem from, string owner, string prefix) where TActor : IActor =>
        Enumerable.Range(0, 2000)
            .Select(i => ActorId.For<TActor>($"{prefix}-{i}"))
            .First(id => from.Cluster.OwnerOf(id) == owner);

    private static async Task<(ActorSystem Sender, ActorSystem Receiver)> PairAsync(
        TestHarness harness, Action<ActorSystemOptions>? receiver = null)
    {
        var first = await harness.NetworkedAsync("bp-a", seeds: []);
        var second = await harness.NetworkedAsync("bp-b", seeds: [$"127.0.0.1:{first.BoundPort}"], configure: receiver);

        foreach (var system in new[] { first, second })
        {
            system.RegisterActor<BlockingActor>();
            system.RegisterMessage<Block>();
        }

        await TestHarness.AssertEventuallyAsync(
            () => first.Cluster.Members.Count == 2 && second.Cluster.Members.Count == 2,
            "the cluster should have converged", TimeSpan.FromSeconds(15));

        await TestHarness.AssertRingsAgreeAsync(first, second);

        return (first, second);
    }

    [Fact]
    public async Task AFullMailboxDoesNotStallOtherActorsOnTheSameConnection()
    {
        await using var harness = new TestHarness();
        var (sender, receiver) = await PairAsync(harness, o =>
        {
            o.MailboxCapacity = 1;
            o.RemoteDeliveryTimeout = TimeSpan.FromMilliseconds(400);
        });

        var blocker = RemoteKey<BlockingActor>(sender, "bp-b", "stall");
        var bystander = RemoteKey<CounterActor>(sender, "bp-b", "bystander");
        var gate = BlockingActor.GateFor(blocker.Key);

        try
        {
            // Wedge the blocker: one message in its handler, one in its capacity-1 mailbox, and
            // several more that the receiver's reader loop cannot place.
            for (var i = 0; i < 5; i++) await sender.TellAsync(blocker, new Block());

            // This is the whole point. Before the inbound path had a deadline, the reader loop
            // waited on the full mailbox forever and every other actor's traffic on that
            // connection - including this one - was stuck behind it.
            await sender.TellAsync(bystander, new Add(7));

            await TestHarness.AssertEventuallyAsync(
                () => receiver.LocalActors.Contains(bystander),
                "an unrelated actor should still receive its message while another mailbox is full",
                TimeSpan.FromSeconds(15));

            Assert.Equal(7, (await sender.AskAsync<Total>(bystander, new GetTotal(), TimeSpan.FromSeconds(20))).Value);
        }
        finally
        {
            gate.TrySetResult();
        }
    }

    [Fact]
    public async Task ARefusedDeliveryAnswersTheAskInsteadOfTimingOut()
    {
        await using var harness = new TestHarness();
        var (sender, receiver) = await PairAsync(harness, o =>
        {
            o.MailboxCapacity = 1;
            o.RemoteDeliveryTimeout = TimeSpan.FromMilliseconds(400);
        });

        var blocker = RemoteKey<BlockingActor>(sender, "bp-b", "refuse");
        var gate = BlockingActor.GateFor(blocker.Key);

        try
        {
            for (var i = 0; i < 4; i++) await sender.TellAsync(blocker, new Block());

            var watch = Stopwatch.StartNew();
            var ex = await Assert.ThrowsAsync<ActorNetException>(
                async () => await sender.AskAsync<Total>(blocker, new GetTotal(), TimeSpan.FromSeconds(30)));
            watch.Stop();

            // A caller told "the mailbox is full" can shed load. A caller told "no reply in 30s"
            // cannot tell that from a slow handler, and waits the full timeout to learn nothing.
            Assert.Contains("full", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.IsNotType<AskTimeoutException>(ex);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), $"the refusal took {watch.Elapsed}, which is close to the ask timeout");
        }
        finally
        {
            gate.TrySetResult();
        }
    }

    [Fact]
    public async Task ARefusedDeliveryIsRecordedAsADeadLetter()
    {
        await using var harness = new TestHarness();
        var (sender, receiver) = await PairAsync(harness, o =>
        {
            o.MailboxCapacity = 1;
            o.RemoteDeliveryTimeout = TimeSpan.FromMilliseconds(400);
        });

        var blocker = RemoteKey<BlockingActor>(sender, "bp-b", "record");
        var gate = BlockingActor.GateFor(blocker.Key);

        try
        {
            for (var i = 0; i < 6; i++) await sender.TellAsync(blocker, new Block());

            await TestHarness.AssertEventuallyAsync(
                () => receiver.DeadLetters.Recent().Any(l => l.Reason == DeadLetterReason.MailboxFull),
                "a refused delivery should be recorded on the node that refused it",
                TimeSpan.FromSeconds(15));

            var letter = receiver.DeadLetters.Recent().First(l => l.Reason == DeadLetterReason.MailboxFull);
            Assert.Equal(blocker, letter.Target);

            // Kept, unlike an allow-list refusal: this message was materialized before the mailbox
            // turned it away, so it can be re-driven once the actor catches up.
            Assert.NotNull(letter.Message);
        }
        finally
        {
            gate.TrySetResult();
        }
    }

    [Fact]
    public async Task ALocalSenderStillWaitsRatherThanBeingRefused()
    {
        await using var harness = new TestHarness();
        var system = await harness.LocalAsync(o =>
        {
            o.MailboxCapacity = 1;
            o.RemoteDeliveryTimeout = TimeSpan.FromMilliseconds(200);
        });

        system.RegisterActor<BlockingActor>();

        var blocker = ActorId.For<BlockingActor>("local-wait");
        var gate = BlockingActor.GateFor(blocker.Key);

        await system.TellAsync(blocker, new Block());
        await system.TellAsync(blocker, new Block());

        // The third send has nowhere to go. Locally that must block the caller - the thread being
        // slowed is the one producing the work, which is backpressure doing its job. The inbound
        // deadline must not leak into this path and turn it into a refusal.
        var pending = system.TellAsync(blocker, new Block()).AsTask();
        var settled = await Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(2)));

        Assert.NotSame(pending, settled);
        Assert.False(pending.IsCompleted, "a local send to a full mailbox should wait, not be refused");

        gate.TrySetResult();
        await pending.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.True(pending.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task AnActorTypeTheReceiverDoesNotKnowFailsTheAskQuickly()
    {
        await using var harness = new TestHarness();
        var (sender, _) = await PairAsync(harness);

        // Registered here and deliberately not on the receiver, which is what a half-finished
        // rollout looks like.
        sender.RegisterActor<SenderOnlyActor>();

        var unknown = RemoteKey<SenderOnlyActor>(sender, "bp-b", "unknown");

        var watch = Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<ActorNetException>(
            async () => await sender.AskAsync<Total>(unknown, new GetTotal(), TimeSpan.FromSeconds(30)));
        watch.Stop();

        Assert.Contains("not registered", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), $"the refusal took {watch.Elapsed}, which is close to the ask timeout");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveFlowControlSettingsAreRefused(int seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            new ActorSystemOptions { RemoteDeliveryTimeout = TimeSpan.FromSeconds(seconds) }.Validate);

        Assert.Throws<ArgumentOutOfRangeException>(
            new ActorSystemOptions { SendTimeout = TimeSpan.FromSeconds(seconds) }.Validate);

        Assert.Throws<ArgumentOutOfRangeException>(
            new ActorSystemOptions { OutboundQueueCapacity = seconds }.Validate);
    }
}

/// <summary>
/// The outbound half, exercised against the transport directly.
/// </summary>
/// <remarks>
/// A congested peer needs a socket that accepts and then never reads, which is far easier to
/// arrange around <see cref="TcpTransport"/> than around a whole cluster.
/// </remarks>
public sealed class TransportCongestionTests
{
    private static WireEnvelope BigFrame() => new()
    {
        Kind = WireKind.Message,
        Target = "CounterActor/x",
        MessageAlias = "tests.big",
        // Large enough that a couple of frames fill the socket's send buffer, which is what wedges
        // the writer loop and lets the queue behind it fill.
        Payload = JsonSerializer.SerializeToElement(new { text = new string('x', 32_000) }),
        FromNode = "sender",
    };

    [Fact]
    public async Task APeerThatAcceptsButNeverReadsIsReportedAsCongested()
    {
        var blackHole = new TcpListener(IPAddress.Loopback, 0);
        blackHole.Start();
        var port = ((IPEndPoint)blackHole.LocalEndpoint).Port;

        // Accept the connection and then read nothing at all.
        var held = Task.Run(async () =>
        {
            var client = await blackHole.AcceptTcpClientAsync();
            await Task.Delay(TimeSpan.FromSeconds(30));
            client.Dispose();
        });

        await using var transport = new TcpTransport(
            "127.0.0.1", 0, _ => Task.CompletedTask, _ => ("127.0.0.1", port),
            NullLogger.Instance, security: null,
            sendTimeout: TimeSpan.FromMilliseconds(500), outboundQueueCapacity: 2);

        await transport.StartAsync(CancellationToken.None);

        try
        {
            // One send to establish the connection, so what follows is a peer that is up and
            // behind rather than one that was never there.
            await transport.SendAsync("stuck", BigFrame(), CancellationToken.None);
            await Task.Delay(TimeSpan.FromMilliseconds(500));

            var ex = await Assert.ThrowsAsync<NodeCongestedException>(async () =>
            {
                for (var i = 0; i < 500; i++)
                    await transport.SendAsync("stuck", BigFrame(), CancellationToken.None);
            });

            Assert.Equal("stuck", ex.NodeId);
            Assert.Equal(2, ex.QueueCapacity);
        }
        finally
        {
            blackHole.Stop();
        }
    }

    [Fact]
    public async Task APeerThatIsSimplyDownIsUnreachableRatherThanCongested()
    {
        // A closed port: the writer loop never connects, so the queue fills for a different reason.
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var deadPort = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        await using var transport = new TcpTransport(
            "127.0.0.1", 0, _ => Task.CompletedTask, _ => ("127.0.0.1", deadPort),
            NullLogger.Instance, security: null,
            sendTimeout: TimeSpan.FromMilliseconds(400), outboundQueueCapacity: 2);

        await transport.StartAsync(CancellationToken.None);

        // Telling an operator "congested, send less" about a node that is down would send them
        // after the wrong problem entirely.
        await Assert.ThrowsAsync<NodeUnreachableException>(async () =>
        {
            for (var i = 0; i < 50; i++)
                await transport.SendAsync("gone", BigFrame(), CancellationToken.None);
        });
    }
}
