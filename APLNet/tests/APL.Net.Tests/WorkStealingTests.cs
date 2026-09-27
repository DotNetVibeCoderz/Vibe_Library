// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using AplNet.Core.Partitioners;

namespace AplNet.Tests;

// Serial: the stealing test needs pool threads to be free to steal. When other test classes
// occupy the pool, the caller legitimately runs everything itself (that is what keeps loops from
// deadlocking), and nothing is stolen.
[Collection(nameof(AllocationCollection))]
public class WorkStealingTests
{
    [Fact]
    public void SkewedWorkIsSharedRatherThanLeftToOneWorker()
    {
        // All the cost sits in the first static partition. Without stealing, the worker that owns it
        // runs every expensive item; with stealing, idle workers take slices of it.
        const int n = 400;
        var owners = new int[n];
        var options = new AplOptions { MaxDegreeOfParallelism = 4, Partitioner = new WorkStealingPartitioner(1) };
        Apl.For(0, n, i =>
        {
            owners[i] = Environment.CurrentManagedThreadId;
            if (i < n / 4)
                Thread.Sleep(3);
        }, options);

        int threadsOnExpensiveItems = owners.Take(n / 4).Distinct().Count();
        Assert.True(threadsOnExpensiveItems > 1, "the expensive quarter was run by a single thread: nothing was stolen");
    }

    [Fact]
    public void StaticPartitioningKeepsTheExpensiveQuarterOnOneWorker()
    {
        // The contrast, pinned so the trade-off stays visible: static means static.
        const int n = 400;
        var owners = new int[n];
        Apl.For(0, n, i =>
        {
            owners[i] = Environment.CurrentManagedThreadId;
            if (i < n / 4)
                Thread.SpinWait(1000);
        }, TestSupport.Options(4));

        Assert.Single(owners.Take(n / 4).Distinct());
    }

    [Fact]
    public void EveryIndexRunsOnceUnderHeavyStealing()
    {
        for (int round = 0; round < 20; round++)
        {
            var hits = new int[10_007];
            var random = new Random(round);
            int[] cost = Enumerable.Range(0, hits.Length).Select(_ => random.Next(8) == 0 ? 200 : 0).ToArray();
            var options = new AplOptions { MaxDegreeOfParallelism = 8, Partitioner = new WorkStealingPartitioner(random.Next(1, 20)) };
            Apl.For(0, hits.Length, i =>
            {
                Thread.SpinWait(cost[i]);
                Interlocked.Increment(ref hits[i]);
            }, options);
            Assert.All(hits, h => Assert.Equal(1, h));
        }
    }

    [Fact]
    public void ReduceUnderStealingIsExact()
    {
        long[] values = Enumerable.Range(0, 250_000).Select(i => (long)i * 31 % 1_000_003).ToArray();
        var options = new AplOptions { MaxDegreeOfParallelism = 8, Partitioner = new WorkStealingPartitioner(3) };
        for (int round = 0; round < 10; round++)
            Assert.Equal(values.Sum(), Apl.Reduce(0, values.Length, 0L, new LongSumBody(values), options));
    }
}
