// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using AplNet.Core.Partitioners;

namespace AplNet.Tests;

/// <summary>Partitioning is pure arithmetic, testable without threads or a particular core count.</summary>
public class PartitionerTests
{
    public static TheoryData<int, int> Shapes => new()
    {
        { 1, 1 }, { 1, 8 }, { 7, 8 }, { 8, 8 }, { 9, 8 }, { 100, 3 }, { 1000, 7 }, { 1_000_003, 16 }, { 5, 64 },
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void StaticCoversEveryIndexExactlyOnceInBalancedSlices(int length, int workers)
    {
        var partitioner = StaticRangePartitioner.Instance;
        int count = partitioner.GetPartitionCount(length, workers);
        Assert.Equal(Math.Min(length, workers), count);

        var sizes = new List<int>();
        int expectedStart = 0;
        for (int p = 0; p < count; p++)
        {
            Assert.True(partitioner.TryGetRange(length, count, p, 0, out int from, out int to));
            Assert.Equal(expectedStart, from);
            sizes.Add(to - from);
            expectedStart = to;
            Assert.False(partitioner.TryGetRange(length, count, p, 1, out _, out _));
        }

        Assert.Equal(length, expectedStart);
        Assert.True(sizes.Max() - sizes.Min() <= 1, "static partitions must differ in size by at most one");
    }

    [Fact]
    public void BalancedSplitDoesNotLeaveWorkersIdleWhereCeilingWould()
    {
        // ceil(9 / 8) = 2 would give five workers two items and three none.
        var partitioner = StaticRangePartitioner.Instance;
        int busy = Enumerable.Range(0, 8).Count(p => partitioner.TryGetRange(9, 8, p, 0, out _, out _));
        Assert.Equal(8, busy);
    }

    [Theory]
    [InlineData(10, 3, 1)]
    [InlineData(10, 3, 4)]
    [InlineData(100_000, 8, 4096)]
    [InlineData(5, 8, 2)]
    [InlineData(4096, 4, 4096)]
    public void StripedDealsStripesRoundRobinAndCoversEverything(int length, int workers, int stripe)
    {
        var partitioner = new StripedPartitioner(stripe);
        int count = partitioner.GetPartitionCount(length, workers);
        Assert.Equal((int)Math.Min(workers, (length + (long)stripe - 1) / stripe), count);

        var seen = new int[length];
        for (int p = 0; p < count; p++)
        {
            for (int index = 0; partitioner.TryGetRange(length, count, p, index, out int from, out int to); index++)
            {
                Assert.Equal((p + (long)index * count) * stripe, from);
                Assert.True(to - from <= stripe);
                for (int i = from; i < to; i++)
                    seen[i]++;
            }
        }

        Assert.All(seen, hits => Assert.Equal(1, hits));
    }

    [Fact]
    public void StripedDoesNotOverflowNearInt32MaxValue()
    {
        var partitioner = new StripedPartitioner(1 << 30);
        int count = partitioner.GetPartitionCount(int.MaxValue, 8);
        Assert.Equal(2, count);
        Assert.True(partitioner.TryGetRange(int.MaxValue, count, 1, 0, out int from, out int to));
        Assert.Equal(1 << 30, from);
        Assert.Equal(int.MaxValue, to);
        Assert.False(partitioner.TryGetRange(int.MaxValue, count, 0, 2, out _, out _));
    }

    [Fact]
    public void WorkStealingStartsFromTheStaticSplitAndChoosesAGrain()
    {
        var auto = WorkStealingPartitioner.Instance;
        Assert.Equal(1, auto.GetGrainSize(10_000, 8));
        Assert.Equal(1_000_000 / 8192, auto.GetGrainSize(1_000_000, 8));
        Assert.Equal(64, new WorkStealingPartitioner(64).GetGrainSize(1_000_000, 8));

        Assert.True(auto.TryGetRange(10, 3, 0, 0, out int from, out int to));
        Assert.Equal((0, 4), (from, to));
    }

    [Fact]
    public void InvalidArgumentsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StripedPartitioner(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkStealingPartitioner(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Core.FixedParallelismProvider(0));
    }
}
