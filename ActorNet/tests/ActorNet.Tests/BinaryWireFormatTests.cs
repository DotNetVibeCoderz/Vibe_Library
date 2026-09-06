// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Text.Json;
using ActorNet.Network;
using ActorNet.Serialization;

namespace ActorNet.Tests;

/// <summary>
/// The binary envelope encoding, and the mixed traffic it has to survive.
/// </summary>
/// <remarks>
/// The risk here is not the encoding, which is small; it is that four language clients speak JSON
/// to the same listener. A node that only knew one encoding would either break them or be unable
/// to use the other one at all, so the interesting tests are the ones with both on the wire.
/// </remarks>
public sealed class BinaryWireFormatTests
{
    private static WireEnvelope Sample() => new()
    {
        Kind = WireKind.AskRequest,
        Target = "BankAccountActor/acct-001",
        Sender = "LedgerActor/ledger",
        MessageAlias = "bank.deposit",
        Payload = JsonSerializer.Deserialize<JsonElement>("""{"Amount":125.5,"Reference":"opening"}"""),
        CorrelationId = "a2f4c0d8-8f5e-4a1a-9f1e-1c2b3d4e5f60",
        ReplyToNode = "node-1",
        FromNode = "node-1",
        TraceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01",
    };

    [Fact]
    public void AnEnvelopeSurvivesTheRoundTrip()
    {
        var original = Sample();

        var restored = BinaryWireFormat.Read(BinaryWireFormat.Write(original));

        Assert.Equal(original.Kind, restored.Kind);
        Assert.Equal(original.Target, restored.Target);
        Assert.Equal(original.Sender, restored.Sender);
        Assert.Equal(original.MessageAlias, restored.MessageAlias);
        Assert.Equal(original.CorrelationId, restored.CorrelationId);
        Assert.Equal(original.ReplyToNode, restored.ReplyToNode);
        Assert.Equal(original.FromNode, restored.FromNode);
        Assert.Equal(original.TraceParent, restored.TraceParent);
        Assert.Equal(original.Payload!.Value.GetRawText(), restored.Payload!.Value.GetRawText());
    }

    [Fact]
    public void AMemberTableSurvivesTheRoundTrip()
    {
        var original = new WireEnvelope
        {
            Kind = WireKind.Gossip,
            FromNode = "node-1",
            Members =
            [
                new WireMember { NodeId = "node-1", Host = "10.0.1.5", Port = 9000, Status = 1, Incarnation = 3 },
                new WireMember { NodeId = "node-2", Host = "10.0.1.6", Port = 9000, Status = 2, Incarnation = 41 },
            ],
        };

        var restored = BinaryWireFormat.Read(BinaryWireFormat.Write(original));

        Assert.Equal(2, restored.Members!.Count);
        Assert.Equal("node-2", restored.Members[1].NodeId);
        Assert.Equal(9000, restored.Members[1].Port);
        Assert.Equal(41, restored.Members[1].Incarnation);
    }

    [Fact]
    public void NullFieldsStayNull()
    {
        // Absent rather than empty. Writing a tag for every field would cost more than it saves and
        // would turn "no sender" into "a sender whose name is the empty string".
        var restored = BinaryWireFormat.Read(BinaryWireFormat.Write(new WireEnvelope { Kind = WireKind.Message }));

        Assert.Null(restored.Target);
        Assert.Null(restored.Sender);
        Assert.Null(restored.Payload);
        Assert.Null(restored.Members);
    }

    [Fact]
    public void ABinaryEnvelopeIsSmallerThanTheJsonOne()
    {
        var envelope = Sample();

        var json = JsonSerializer.SerializeToUtf8Bytes(envelope, typeof(WireEnvelope), WireJsonContext.Default).Length;
        var binary = BinaryWireFormat.Write(envelope).Length;

        // The saving is on the framing, not the payload - the payload is still the same JSON. So
        // this asserts a real reduction without pretending it is a whole binary protocol.
        Assert.True(binary < json, $"binary was {binary} bytes against JSON's {json}");
    }

    [Fact]
    public void AFrameFromANewerVersionIsRefusedRatherThanMisread()
    {
        var frame = BinaryWireFormat.Write(Sample());
        frame[1] = 99;

        var error = Assert.Throws<ActorNetException>(() => BinaryWireFormat.Read(frame));
        Assert.Contains("version 99", error.Message);
    }

    [Fact]
    public void ATruncatedFrameIsRefusedRatherThanRead()
    {
        var frame = BinaryWireFormat.Write(Sample());

        // Hostile input, not just a bug: a length that runs past the end of the frame must not read
        // whatever happens to be next in the buffer.
        Assert.Throws<ActorNetException>(() => BinaryWireFormat.Read(frame.AsSpan(0, frame.Length / 2)));
    }

    [Fact]
    public async Task ABinaryClusterCarriesRealTraffic()
    {
        await using var harness = new TestHarness();

        void Binary(ActorSystemOptions o) => o.WireFormat = WireFormat.Binary;

        var here = await harness.NetworkedAsync("bin-a", seeds: [], configure: Binary);
        var there = await harness.NetworkedAsync("bin-b", seeds: [$"127.0.0.1:{here.BoundPort}"], configure: Binary);

        await TestHarness.AssertEventuallyAsync(
            () => here.Cluster.Members.Count == 2 && there.Cluster.Members.Count == 2,
            "a binary cluster should converge", TimeSpan.FromSeconds(15));

        var remote = Enumerable.Range(0, 2000)
            .Select(i => ActorId.For<CounterActor>($"bin-{i}"))
            .First(id => here.Cluster.OwnerOf(id) == "bin-b");

        await here.TellAsync(remote, new Add(7));

        Assert.Equal(7, (await here.AskAsync<Total>(remote, new GetTotal(), TimeSpan.FromSeconds(15))).Value);
    }

    [Fact]
    public async Task ABinaryNodeAndAJsonNodeStillTalk()
    {
        await using var harness = new TestHarness();

        // The rolling-upgrade case, and the reason a frame says which encoding it is in: half a
        // cluster on each setting has to keep working, or the option could only ever be turned on
        // during a full stop.
        var json = await harness.NetworkedAsync("mix-json", seeds: []);
        var binary = await harness.NetworkedAsync("mix-bin", seeds: [$"127.0.0.1:{json.BoundPort}"],
            configure: o => o.WireFormat = WireFormat.Binary);

        await TestHarness.AssertEventuallyAsync(
            () => json.Cluster.Members.Count == 2 && binary.Cluster.Members.Count == 2,
            "a mixed cluster should converge", TimeSpan.FromSeconds(15));

        var onBinary = Enumerable.Range(0, 2000)
            .Select(i => ActorId.For<CounterActor>($"mix-{i}"))
            .First(id => json.Cluster.OwnerOf(id) == "mix-bin");

        var onJson = Enumerable.Range(0, 2000)
            .Select(i => ActorId.For<CounterActor>($"mix-{i}"))
            .First(id => json.Cluster.OwnerOf(id) == "mix-json");

        await json.TellAsync(onBinary, new Add(3));
        await binary.TellAsync(onJson, new Add(5));

        Assert.Equal(3, (await json.AskAsync<Total>(onBinary, new GetTotal(), TimeSpan.FromSeconds(15))).Value);
        Assert.Equal(5, (await binary.AskAsync<Total>(onJson, new GetTotal(), TimeSpan.FromSeconds(15))).Value);
    }
}
