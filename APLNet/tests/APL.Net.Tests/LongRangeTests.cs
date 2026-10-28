// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Collections.Concurrent;
using AplNet.Core;
using AplNet.Core.Partitioners;

namespace AplNet.Tests;

public class LongRangeTests
{
    [Theory]
    [InlineData(0L, 0L)]
    [InlineData(10L, 3L)]
    [InlineData(long.MaxValue, long.MinValue)]
    public void EmptyOrReversedRangeRunsNothing(long from, long to)
    {
        int calls = 0;
        Apl.For(from, to, (long _) => Interlocked.Increment(ref calls));
        Apl.ForRange(from, to, (long _, long _) => Interlocked.Increment(ref calls));
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(1, 8)]
    [InlineData(9, 8)]
    [InlineData(1000, 3)]
    [InlineData(100_000, 64)]
    public void EveryIndexRunsExactlyOnce(int n, int dop)
    {
        const long offset = 5_000_000_000L;
        foreach (IPartitioner partitioner in TestSupport.Partitioners)
        {
            var hits = new int[n];
            Apl.For(offset, offset + n, new LongCountingBody(hits, offset), TestSupport.Options(dop, partitioner));
            Assert.All(hits, h => Assert.Equal(1, h));

            var delegateHits = new int[n];
            Apl.For(offset, offset + n, i => Interlocked.Increment(ref delegateHits[i - offset]), TestSupport.Options(dop, partitioner));
            Assert.All(delegateHits, h => Assert.Equal(1, h));
        }
    }

    [Fact]
    public void IntArgumentsStillPickTheIntOverloads()
    {
        // Overload resolution must not drift to the new long overloads for existing code.
        int sum = 0;
        Apl.For(0, 10, i => Interlocked.Add(ref sum, i));
        Assert.Equal(45, sum);

        var hits = new int[10];
        Apl.For(0, 10, new CountingBody(hits, 0));
        Assert.All(hits, h => Assert.Equal(1, h));
    }

    [Theory]
    [InlineData(0L, 5_000_000_000L)]
    [InlineData(-3_000_000_000L, 3_000_000_000L)]
    [InlineData(long.MinValue, long.MaxValue)]
    [InlineData(long.MaxValue - 4_294_967_296L, long.MaxValue)]
    public void RangesBeyondInt32AreCoveredExactlyOnce(long from, long to)
    {
        foreach (IPartitioner partitioner in TestSupport.Partitioners)
        {
            if (partitioner is StripedPartitioner { StripeSize: < 4096 } or WorkStealingPartitioner { MinGrainSize: > 0 })
                continue; // billions of tiny pieces: correct, but far too slow for a unit test

            var ranges = new ConcurrentBag<(long From, long To)>();
            Apl.ForRange(from, to, (a, b) => ranges.Add((a, b)), TestSupport.Options(8, partitioner));
            AssertTiles(ranges, from, to);
        }
    }

    [Fact]
    public void StructRangeBodyCoversAHugeRange()
    {
        var ranges = new ConcurrentBag<(long From, long To)>();
        Apl.ForRange(0L, 10_000_000_000L, new RecordingLongRangeBody(ranges), TestSupport.Options(4));
        AssertTiles(ranges, 0, 10_000_000_000L);
    }

    [Fact]
    public void HugeRangeHonoursCancellation()
    {
        using var cts = new CancellationTokenSource();
        var options = new AplOptions { MaxDegreeOfParallelism = 4, CancellationToken = cts.Token, CancellationCheckInterval = 1_000_000 };
        long seen = 0;
        Assert.Throws<OperationCanceledException>(() => Apl.ForRange(0L, 1L << 40, (a, b) =>
        {
            if (Interlocked.Add(ref seen, b - a) > 100_000_000)
                cts.Cancel();
        }, options));
        Assert.True(Volatile.Read(ref seen) < 1L << 40);
    }

    [Fact]
    public void ExceptionsAreAggregated()
    {
        var ex = Assert.Throws<AggregateException>(() =>
            Apl.For(0L, 1000L, i => { if (i == 500) throw new InvalidOperationException("boom"); }, TestSupport.Options(4)));
        Assert.IsType<InvalidOperationException>(Assert.Single(ex.InnerExceptions));
    }

    /// <summary>The ranges are disjoint, contiguous once sorted, and span exactly <c>[from, to)</c>.</summary>
    private static void AssertTiles(IEnumerable<(long From, long To)> ranges, long from, long to)
    {
        var sorted = ranges.OrderBy(r => r.From).ToArray();
        Assert.NotEmpty(sorted);
        Assert.Equal(from, sorted[0].From);
        for (int i = 0; i < sorted.Length; i++)
        {
            Assert.True(sorted[i].From < sorted[i].To, $"empty or reversed range {sorted[i]}");
            if (i > 0)
                Assert.Equal(sorted[i - 1].To, sorted[i].From);
        }

        Assert.Equal(to, sorted[^1].To);
    }

    private readonly struct LongCountingBody(int[] hits, long offset) : ILongWorkBody
    {
        public void Invoke(long index) => Interlocked.Increment(ref hits[index - offset]);
    }

    private readonly struct RecordingLongRangeBody(ConcurrentBag<(long, long)> ranges) : ILongRangeWorkBody
    {
        public void Invoke(long fromInclusive, long toExclusive) => ranges.Add((fromInclusive, toExclusive));
    }
}
