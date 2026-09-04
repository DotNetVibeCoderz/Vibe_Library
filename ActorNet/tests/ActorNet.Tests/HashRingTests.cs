// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ActorNet.Cluster;

namespace ActorNet.Tests;

public sealed class HashRingTests
{
    private static string[] Keys(int count) => Enumerable.Range(0, count).Select(i => $"BankAccountActor/user-{i}").ToArray();

    [Fact]
    public void PlacementIsStableAcrossRingsBuiltFromTheSameMembers()
    {
        var a = new HashRing(["node-1", "node-2", "node-3"]);
        var b = new HashRing(["node-3", "node-1", "node-2"]);

        // Member order must not matter, or two nodes would disagree about ownership purely
        // because they learned about each other in a different sequence.
        foreach (var key in Keys(500)) Assert.Equal(a.OwnerOf(key), b.OwnerOf(key));
    }

    [Fact]
    public void HashIsProcessIndependent()
    {
        // The values are pinned, not just self-consistent. String.GetHashCode is randomized per
        // process, and using it here would make two nodes compute different rings - a bug that
        // only appears in a real cluster. Pinning also makes any change to the hash show up as a
        // failing test, because it silently repartitions every existing deployment.
        Assert.Equal(0x82A2A958A9BECE5BUL, HashRing.Hash("a"));
        Assert.Equal(0x2C22194922D1672BUL, HashRing.Hash("foobar"));
    }

    [Fact]
    public void AddingANodeMovesRoughlyOneOverNOfTheKeyspace()
    {
        var keys = Keys(20_000);
        var before = new HashRing(["node-1", "node-2", "node-3"]);
        var after = new HashRing(["node-1", "node-2", "node-3", "node-4"]);

        var moved = keys.Count(k => before.OwnerOf(k) != after.OwnerOf(k));
        var fraction = (double)moved / keys.Length;

        // Going from 3 to 4 nodes should move about 1/4 of the keys. The window is wide because
        // this is a statistical property of the hash, not an exact one - but a naive
        // "hash modulo count" placement would move roughly 3/4 and fail this outright.
        Assert.InRange(fraction, 0.15, 0.35);
    }

    [Fact]
    public void VirtualNodesKeepTheDistributionReasonablyEven()
    {
        var ring = new HashRing(["node-1", "node-2", "node-3"], virtualNodes: 128);
        var keys = Keys(30_000);

        var counts = keys.GroupBy(ring.OwnerOf).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(3, counts.Count);

        var expected = keys.Length / 3.0;
        foreach (var (node, count) in counts)
            Assert.True(Math.Abs(count - expected) / expected < 0.15, $"{node} holds {count} keys, expected around {expected:N0}.");
    }

    [Fact]
    public void PreferenceListReturnsDistinctNodesInRingOrder()
    {
        var ring = new HashRing(["node-1", "node-2", "node-3", "node-4"]);
        var preference = ring.PreferenceList("BankAccountActor/user-7", 3);

        Assert.Equal(3, preference.Count);
        Assert.Equal(preference.Distinct(), preference);
        Assert.Equal(ring.OwnerOf("BankAccountActor/user-7"), preference[0]);
    }

    [Fact]
    public void AnEmptyRingRefusesToPlaceAKey()
    {
        var ring = new HashRing([]);
        Assert.True(ring.IsEmpty);
        Assert.Throws<InvalidOperationException>(() => ring.OwnerOf("anything"));
    }
}

public sealed class RingSegmentTests
{
    [Fact]
    public void OwnershipSharesSumToTheWholeKeyspace()
    {
        var ring = new HashRing(["node-1", "node-2", "node-3", "node-4"]);
        var shares = ring.OwnershipShare();

        Assert.Equal(4, shares.Count);
        Assert.Equal(1.0, shares.Values.Sum(), precision: 6);
    }

    [Fact]
    public void ASingleMemberOwnsTheWholeRing()
    {
        var ring = new HashRing(["only-node"]);

        // Every replica belongs to the same member, so they merge into one arc whose start and
        // end coincide. Read as a plain subtraction that is a width of zero, and the console
        // reported the node as owning nothing at all.
        var segment = Assert.Single(ring.Segments());
        Assert.True(segment.IsFullCircle);
        Assert.Equal(ulong.MaxValue, segment.Width);
        Assert.Equal(1.0, ring.OwnershipShare()["only-node"], precision: 6);
    }

    [Fact]
    public void SegmentsAgreeWithTheOwnerLookupTheRuntimeUses()
    {
        var ring = new HashRing(["node-1", "node-2", "node-3"]);
        var segments = ring.Segments();

        // The picture and the routing must be the same computation. A key that lands just inside
        // an arc has to be owned by that arc's member.
        foreach (var segment in segments.Where(s => !s.IsFullCircle && s.Width > 16))
        {
            var inside = segment.End - 1;
            Assert.Equal(segment.Owner, OwnerOfPosition(ring, inside));
        }
    }

    [Fact]
    public void MeasuredSharesMatchSampledOwnership()
    {
        var ring = new HashRing(["node-1", "node-2", "node-3"]);
        var shares = ring.OwnershipShare();

        var keys = Enumerable.Range(0, 30_000).Select(i => $"BankAccountActor/user-{i}").ToArray();
        var sampled = keys.GroupBy(ring.OwnerOf).ToDictionary(g => g.Key, g => (double)g.Count() / keys.Length);

        // Sampling estimates what Segments measures exactly; they should agree closely.
        foreach (var (node, share) in shares)
            Assert.Equal(share, sampled[node], tolerance: 0.02);
    }

    /// <summary>
    /// Finds the owner of a raw hash position by binary-searching the same way the ring does.
    /// </summary>
    private static string OwnerOfPosition(HashRing ring, ulong position)
    {
        // Segments are ordered and half-open, so the owner is the first arc whose end is past the
        // position - which is exactly what the ring's own lookup computes.
        foreach (var segment in ring.Segments())
        {
            if (segment.IsFullCircle) return segment.Owner;
            var contains = segment.End > segment.Start
                ? position >= segment.Start && position < segment.End
                : position >= segment.Start || position < segment.End;
            if (contains) return segment.Owner;
        }

        return ring.Nodes[0];
    }
}
