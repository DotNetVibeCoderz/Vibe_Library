// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using AplNet.Core;
using AplNet.Core.Partitioners;
using AplNet.Simd;

namespace AplNet.Tests;

public class SpinningWorkersTests
{
    private static readonly int s_threads = Math.Clamp(Environment.ProcessorCount - 1, 1, 3);

    [Fact]
    public void LoopsOnSpinningWorkersAreCorrect()
    {
        using var workers = new SpinningWorkers(s_threads);
        foreach (IPartitioner partitioner in TestSupport.Partitioners)
        {
            foreach (int n in new[] { 1, 7, 1000, 100_003 })
            {
                var options = new AplOptions { MaxDegreeOfParallelism = s_threads + 1, Partitioner = partitioner, Workers = workers };
                var hits = new int[n];
                Apl.For(0, n, new CountingBody(hits, 0), options);
                Assert.All(hits, h => Assert.Equal(1, h));

                long[] values = Enumerable.Range(0, n).Select(i => (long)i).ToArray();
                Assert.Equal(values.Sum(), Apl.Reduce(0, n, 0L, new LongSumBody(values), options));
            }
        }
    }

    [Fact]
    public void ManySmallLoopsInARow()
    {
        // The case the workers exist for: a stream of tiny loops, each one waking nobody.
        using var workers = new SpinningWorkers(s_threads);
        var options = new AplOptions { MaxDegreeOfParallelism = s_threads + 1, Workers = workers };
        var data = new int[256];
        for (int round = 0; round < 20_000; round++)
            Apl.For(0, data.Length, i => data[i]++, options);
        Assert.All(data, d => Assert.Equal(20_000, d));
    }

    [Fact]
    public void WorkersThatParkImmediatelyAreWokenEveryTime()
    {
        // A spin duration of zero sends every worker to sleep between loops, which exercises the
        // wake-up handshake on every call.
        using var workers = new SpinningWorkers(s_threads, spinDuration: TimeSpan.Zero);
        var options = new AplOptions { MaxDegreeOfParallelism = s_threads + 1, Workers = workers };
        long total = 0;
        for (int round = 0; round < 2000; round++)
            total += Apl.Reduce(0, 1000, 0L, (a, b, acc) => acc + (b - a), (x, y) => x + y, options);
        Assert.Equal(2000L * 1000, total);
    }

    [Fact]
    public void NestedLoopsOnTheSameWorkersDoNotDeadlock()
    {
        using var workers = new SpinningWorkers(s_threads);
        var options = new AplOptions { MaxDegreeOfParallelism = s_threads + 1, Workers = workers };
        int total = 0;
        Apl.For(0, 16, _ => Apl.For(0, 100, _ => Interlocked.Increment(ref total), options), options);
        Assert.Equal(1600, total);
    }

    [Fact]
    public void ConcurrentCallersShareTheWorkers()
    {
        using var workers = new SpinningWorkers(s_threads);
        var options = new AplOptions { MaxDegreeOfParallelism = s_threads + 1, Workers = workers };
        long total = 0;
        Parallel.For(0, 4, _ =>
        {
            for (int round = 0; round < 500; round++)
                Interlocked.Add(ref total, (long)SimdOps.ParallelSum(Enumerable.Repeat(1, 4096).ToArray(), new AplOptions { MinChunkSize = 512, Workers = workers }));
        });
        Assert.Equal(4L * 500 * 4096, total);
    }

    [Fact]
    public void ADisposedGroupFallsBackToThePool()
    {
        var workers = new SpinningWorkers(s_threads);
        workers.Dispose();
        workers.Dispose();
        var options = new AplOptions { MaxDegreeOfParallelism = 4, Workers = workers };
        var hits = new int[10_000];
        Apl.For(0, hits.Length, new CountingBody(hits, 0), options);
        Assert.All(hits, h => Assert.Equal(1, h));
    }

    [Fact]
    public void FailuresAndCancellationBehaveAsOnThePool()
    {
        using var workers = new SpinningWorkers(s_threads);
        var options = new AplOptions { MaxDegreeOfParallelism = s_threads + 1, Workers = workers };
        var ex = Assert.Throws<AggregateException>(() => Apl.For(0, 1000, i => { if (i == 999) throw new InvalidOperationException(); }, options));
        Assert.IsType<InvalidOperationException>(Assert.Single(ex.InnerExceptions));

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => Apl.For(0, 1000, _ => { }, new AplOptions { Workers = workers, CancellationToken = cts.Token }));
    }

    [Fact]
    public void AffinityIsAppliedWhereThePlatformSupportsIt()
    {
        using var workers = new SpinningWorkers(s_threads, affinity: WorkerAffinity.Core);
        Assert.Equal(WorkerAffinity.Core, workers.Affinity);
        Assert.Equal(AplTopology.IsAffinitySupported, workers.IsAffinityApplied);

        using var numa = new SpinningWorkers(s_threads, affinity: WorkerAffinity.NumaNode);
        Assert.Equal(AplTopology.IsAffinitySupported, numa.IsAffinityApplied);

        var hits = new int[50_000];
        Apl.For(0, hits.Length, new CountingBody(hits, 0), new AplOptions { MaxDegreeOfParallelism = s_threads + 1, Workers = numa });
        Assert.All(hits, h => Assert.Equal(1, h));

        using var none = new SpinningWorkers(1);
        Assert.False(none.IsAffinityApplied);
    }

    [Fact]
    public void ConstructorValidatesItsArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpinningWorkers(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpinningWorkers(1, TimeSpan.FromSeconds(-1)));
        using var infinite = new SpinningWorkers(1, Timeout.InfiniteTimeSpan);
        Assert.Equal(Timeout.InfiniteTimeSpan, infinite.SpinDuration);
        using var defaults = new SpinningWorkers();
        Assert.Equal(Math.Max(1, Environment.ProcessorCount - 1), defaults.ThreadCount);
    }
}

[CollectionDefinition(nameof(QuietMachineCollection), DisableParallelization = true)]
public sealed class QuietMachineCollection;

/// <summary>
/// Placement is a statement about which thread runs what when the workers get the cores they spin
/// on. Next to other tests saturating the machine they get descheduled, and the caller rightly takes
/// over their partitions, so this runs alone.
/// </summary>
[Collection(nameof(QuietMachineCollection))]
public class SpinningWorkerPlacementTests
{
    private static readonly int s_threads = Math.Clamp(Environment.ProcessorCount - 1, 1, 3);

    [Fact]
    public void PartitionsStayWithTheirHomeWorker()
    {
        // Stable placement: partition k + 1 is claimed by worker k first. Each partition takes long
        // enough for every worker to arrive, so the caller does not need to help, and the mapping from
        // partition to thread should hold on nearly every call.
        if (Environment.ProcessorCount < 2)
            return;

        int threads = s_threads;
        using var workers = new SpinningWorkers(threads, spinDuration: TimeSpan.FromMilliseconds(50));
        var options = new AplOptions { MaxDegreeOfParallelism = threads + 1, Workers = workers };
        int partitions = threads + 1;
        var owners = new int[partitions];
        int stable = 0;
        const int rounds = 200;
        for (int round = 0; round < rounds; round++)
        {
            var current = new int[partitions];
            Apl.ForRange(0, partitions, (from, _) =>
            {
                current[from] = Environment.CurrentManagedThreadId;
                Thread.SpinWait(20_000);
            }, options);

            if (round > 0 && current.AsSpan(1).SequenceEqual(owners.AsSpan(1)))
                stable++;
            owners = current;
        }

        Assert.True(stable >= rounds * 3 / 4, $"partitions kept their worker on {stable} of {rounds - 1} calls");
    }
}

public class TopologyTests
{
    [Theory]
    [InlineData("0", new[] { 0 })]
    [InlineData("0-3", new[] { 0, 1, 2, 3 })]
    [InlineData("0-3,8,10-11\n", new[] { 0, 1, 2, 3, 8, 10, 11 })]
    [InlineData(" 4-5 , 1 ", new[] { 1, 4, 5 })]
    public void CpuListsParseAndFormat(string text, int[] expected)
    {
        int[] parsed = AplTopology.ParseCpuList(text);
        Assert.Equal(expected, parsed);
        Assert.Equal(parsed, AplTopology.ParseCpuList(AplTopology.FormatCpuList(parsed)));
    }

    [Fact]
    public void WorkersArePlacedNodeByNodeAfterTheCallersProcessor()
    {
        int[][] nodes = [[0, 1, 2, 3], [4, 5, 6, 7]];
        Assert.Equal([1, 2, 3, 4, 5, 6, 7], AplTopology.GetWorkerPlacement(nodes, 7));
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 0, 1], AplTopology.GetWorkerPlacement(nodes, 9));
        Assert.Equal(1, AplTopology.NodeOf(nodes, 5));
        Assert.Equal(-1, AplTopology.NodeOf(nodes, 8));
    }

    [Fact]
    public void TheMachineHasAtLeastOneNodeWithProcessors()
    {
        Assert.True(AplTopology.NumaNodeCount >= 1);
        int processors = Enumerable.Range(0, AplTopology.NumaNodeCount).Sum(n => AplTopology.GetNodeProcessors(n).Count);
        Assert.True(processors >= 1);
        Assert.Contains("NUMA node", AplTopology.Describe());
        Assert.Throws<ArgumentOutOfRangeException>(() => AplTopology.GetNodeProcessors(AplTopology.NumaNodeCount));
    }
}
