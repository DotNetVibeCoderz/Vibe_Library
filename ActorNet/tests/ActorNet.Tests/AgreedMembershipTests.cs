// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ActorNet.Cluster;
using ActorNet.Network;
using ActorNet.Serialization;
using Microsoft.Extensions.Logging.Abstractions;

namespace ActorNet.Tests;

/// <summary>
/// Making both halves of a partition measure themselves against the same denominator.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ClusterMembership.SurvivesMajority"/> cannot let two sides both win - a strict
/// majority is a strict majority, and an exact half is broken by the lowest node id. What it can do
/// is be asked two different questions: each node used to freeze its own snapshot of "everyone was
/// healthy when I last looked", and two nodes take that snapshot at different moments.
/// </para>
/// <para>
/// So the agreement itself is gossiped, with an epoch, and only a node that can see every member it
/// knows of raises it. That is the entire protocol between the halves, and all of it happens before
/// the partition: once the cut is made, neither side can see a whole cluster, neither raises the
/// epoch, and both keep the last set they held in common.
/// </para>
/// </remarks>
public sealed class AgreedMembershipTests
{
    private sealed class SilentTransport : ITransport
    {
        public int BoundPort => 1;
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask SendAsync(string nodeId, WireEnvelope frame, CancellationToken cancellationToken) => default;
        public ValueTask SendToAddressAsync(string host, int port, WireEnvelope frame, CancellationToken cancellationToken) => default;
        public ValueTask DisposeAsync() => default;
    }

    private static ClusterOptions Options() => new()
    {
        Enabled = true,
        SplitBrainStrategy = SplitBrainStrategy.KeepMajority,
        FailureDetection = FailureDetection.Deadline,
        HeartbeatInterval = TimeSpan.FromMilliseconds(100),
        UnreachableAfter = TimeSpan.FromMilliseconds(300),
        DownAfter = TimeSpan.FromMilliseconds(600),
        SplitBrainStabilityWindow = TimeSpan.FromMilliseconds(400),
    };

    private static WireEnvelope GossipFrom(string nodeId, long epoch = 0, string[]? agreed = null) => new()
    {
        Kind = WireKind.Gossip,
        FromNode = nodeId,
        Members = [new WireMember { NodeId = nodeId, Host = "127.0.0.1", Port = 9, Status = (int)MemberStatus.Up, Incarnation = 1 }],
        AgreedEpoch = epoch,
        AgreedMembers = agreed?.ToList(),
    };

    private static async Task<ClusterMembership> NodeAsync(string self, params string[] peers)
    {
        var membership = new ClusterMembership(self, "127.0.0.1", 1, Options(), NullLogger.Instance);
        await membership.StartAsync(new SilentTransport(), TestContext.Current.CancellationToken);
        foreach (var peer in peers) membership.HandleFrame(GossipFrom(peer));
        return membership;
    }

    [Fact]
    public async Task ANewerAgreementWins()
    {
        await using var node = await NodeAsync("agree-a", "agree-b");

        node.HandleFrame(GossipFrom("agree-b", epoch: 4, agreed: ["agree-a", "agree-b", "agree-c"]));
        Assert.Equal(4, node.AgreedMembership.Epoch);
        Assert.Equal(["agree-a", "agree-b", "agree-c"], node.AgreedMembership.Members);

        // A longer list does not win, and neither does arriving later. Only a higher epoch does:
        // the epoch is only ever raised by a node that could see every member it knew of, so it is
        // the one piece of evidence that says whose view was complete more recently.
        node.HandleFrame(GossipFrom("agree-b", epoch: 3, agreed: ["agree-a", "agree-b", "agree-c", "agree-d", "agree-e"]));
        Assert.Equal(4, node.AgreedMembership.Epoch);
        Assert.Equal(["agree-a", "agree-b", "agree-c"], node.AgreedMembership.Members);
    }

    [Fact]
    public async Task AJoiningNodeIsToldWhatWasAgreed()
    {
        await using var seed = await NodeAsync("join-a");

        // The seed has agreed something with the rest of the cluster.
        seed.HandleFrame(GossipFrom("join-b", epoch: 9, agreed: ["join-a", "join-b", "join-c"]));

        var ack = seed.HandleFrame(new WireEnvelope { Kind = WireKind.Join, FromNode = "join-d", Members = [] });

        // Without this a node that joined moments before a partition would have agreed nothing, and
        // an empty agreement survives everything.
        Assert.NotNull(ack);
        Assert.Equal(9, ack.AgreedEpoch);
        Assert.Equal(["join-a", "join-b", "join-c"], ack.AgreedMembers);
    }

    [Fact]
    public async Task TwoSidesThatDisagreedOnTheMembershipCannotBothBeAMajority()
    {
        // The cluster grew to five, and then split three against two. The hole this closes is that
        // the smaller side had only ever agreed on four members, so two of four looked to it like
        // an exact half - which the lowest node id then awarded to it, while the other side was
        // winning three of five.
        string[] whole = ["p-a", "p-b", "p-c", "p-d", "p-e"];

        await using var larger = await NodeAsync("p-c", "p-a", "p-b", "p-d", "p-e");
        await using var smaller = await NodeAsync("p-a", "p-b", "p-c", "p-d", "p-e");

        // Both are told the same agreement, because both heard it before the cut.
        larger.HandleFrame(GossipFrom("p-e", epoch: 7, agreed: whole));
        smaller.HandleFrame(GossipFrom("p-e", epoch: 7, agreed: whole));

        Assert.Equal(larger.AgreedMembership.Members, smaller.AgreedMembership.Members);

        var agreed = larger.AgreedMembership.Members;
        Assert.True(ClusterMembership.SurvivesMajority(agreed, ["p-c", "p-d", "p-e"]));
        Assert.False(ClusterMembership.SurvivesMajority(agreed, ["p-a", "p-b"]));
    }

    [Fact]
    public void TheOldHoleIsRealWhenTheDenominatorsDiffer()
    {
        // Kept as a statement of what the gossiped agreement is for. Ask the same function two
        // different questions and both sides win: the side of two measures itself against the four
        // members it knew about and takes the exact-half tiebreak with the lowest id, while the
        // side of three measures itself against five.
        string[] four = ["p-a", "p-b", "p-c", "p-d"];
        string[] five = ["p-a", "p-b", "p-c", "p-d", "p-e"];

        Assert.True(ClusterMembership.SurvivesMajority(four, ["p-a", "p-b"]));
        Assert.True(ClusterMembership.SurvivesMajority(five, ["p-c", "p-d", "p-e"]));
    }

    [Fact]
    public async Task TheAgreementIsCarriedOnGossip()
    {
        await using var node = await NodeAsync("carry-a", "carry-b");
        node.HandleFrame(GossipFrom("carry-b", epoch: 2, agreed: ["carry-a", "carry-b"]));

        // Round-tripped through the binary format, because a field the writer emits and the reader
        // cannot parse is a field that only works between two nodes speaking JSON.
        var frame = new WireEnvelope
        {
            Kind = WireKind.Gossip,
            FromNode = "carry-a",
            AgreedEpoch = node.AgreedMembership.Epoch,
            AgreedMembers = [.. node.AgreedMembership.Members],
        };

        var read = BinaryWireFormat.Read(BinaryWireFormat.Write(frame), null);

        Assert.Equal(2, read.AgreedEpoch);
        Assert.Equal(["carry-a", "carry-b"], read.AgreedMembers);
    }

    [Fact]
    public void AReaderThatPredatesTheFieldSkipsIt()
    {
        // The block carries its own length, which is the only shape the reader's default case can
        // skip. Everything around it must still come back intact.
        var frame = new WireEnvelope
        {
            Kind = WireKind.Gossip,
            FromNode = "skip-a",
            TraceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01",
            AgreedEpoch = 5,
            AgreedMembers = ["skip-a", "skip-b"],
        };

        var read = BinaryWireFormat.Read(BinaryWireFormat.Write(frame), null);

        Assert.Equal("skip-a", read.FromNode);
        Assert.Equal(frame.TraceParent, read.TraceParent);
        Assert.Equal(5, read.AgreedEpoch);
    }
}
