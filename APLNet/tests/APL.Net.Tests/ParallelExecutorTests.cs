// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using AplNet.Core;
using AplNet.Core.Partitioners;

namespace AplNet.Tests;

public class ParallelExecutorTests
{
    // ---------------------------------------------------------------- edge cases

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 5)]
    [InlineData(10, 3)]
    [InlineData(int.MaxValue, int.MinValue)]
    public void EmptyOrReversedRangeRunsNothing(int from, int to)
    {
        int calls = 0;
        Apl.For(from, to, _ => Interlocked.Increment(ref calls));
        Apl.ForRange(from, to, (_, _) => Interlocked.Increment(ref calls));
        Assert.Equal(0, calls);
        Assert.Equal(42L, Apl.Reduce(from, to, 42L, (_, _, acc) => acc + 1, (a, b) => a + b));
    }

    [Theory]
    [InlineData(1, 8)]
    [InlineData(3, 8)]
    [InlineData(7, 8)]
    [InlineData(8, 8)]
    [InlineData(9, 8)]
    [InlineData(1000, 1)]
    [InlineData(1000, 64)]
    public void EveryIndexRunsExactlyOnce(int n, int dop)
    {
        foreach (IPartitioner partitioner in TestSupport.Partitioners)
        {
            var hits = new int[n];
            Apl.For(0, n, new CountingBody(hits, 0), TestSupport.Options(dop, partitioner));
            Assert.All(hits, h => Assert.Equal(1, h));

            var rangeHits = new int[n];
            Apl.ForRange(0, n, new CountingRangeBody(rangeHits, 0), TestSupport.Options(dop, partitioner));
            Assert.All(rangeHits, h => Assert.Equal(1, h));
        }
    }

    [Fact]
    public void NegativeAndOffsetRangesAreHonoured()
    {
        var hits = new int[200];
        Apl.For(-100, 100, new CountingBody(hits, -100), TestSupport.Options(4));
        Assert.All(hits, h => Assert.Equal(1, h));
    }

    [Fact]
    public void RangeLongerThanInt32IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Apl.For(int.MinValue, int.MaxValue, _ => { }));
    }

    [Fact]
    public void NullBodiesAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => Apl.For(0, 1, (Action<int>)null!));
        Assert.Throws<ArgumentNullException>(() => Apl.ForRange(0, 1, (Action<int, int>)null!));
        Assert.Throws<ArgumentNullException>(() => Apl.ForEach((int[])null!, _ => { }));
        Assert.Throws<ArgumentNullException>(() => Apl.ForEach(new int[1], (Action<int>)null!));
    }

    // ---------------------------------------------------------------- degree of parallelism

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void NeverRunsMoreBodiesAtOnceThanTheDegreeOfParallelism(int dop)
    {
        var probe = new TestSupport.ConcurrencyProbe();
        Apl.For(0, 64, _ =>
        {
            probe.Enter();
            Thread.Sleep(2);
            probe.Exit();
        }, TestSupport.Options(dop));

        Assert.InRange(probe.Peak, 1, dop);
    }

    [Fact]
    public void AnInjectedProviderSetsTheDefaultDegreeOfParallelism()
    {
        var probe = new TestSupport.ConcurrencyProbe();
        var options = new AplOptions { ParallelismProvider = new FixedParallelismProvider(2) };
        Assert.Equal(2, options.GetEffectiveDegreeOfParallelism());

        Apl.For(0, 32, _ =>
        {
            probe.Enter();
            Thread.Sleep(2);
            probe.Exit();
        }, options);

        Assert.InRange(probe.Peak, 1, 2);
    }

    [Fact]
    public void ExplicitMaxDegreeOverridesTheProvider()
    {
        var options = new AplOptions { ParallelismProvider = new FixedParallelismProvider(2), MaxDegreeOfParallelism = 5 };
        Assert.Equal(5, options.GetEffectiveDegreeOfParallelism());
        Assert.Equal(Environment.ProcessorCount, AplOptions.Default.GetEffectiveDegreeOfParallelism());
        Assert.Null(new AplOptions { MaxDegreeOfParallelism = -1 }.MaxDegreeOfParallelism);
    }

    [Fact]
    public void InvalidOptionsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AplOptions { MaxDegreeOfParallelism = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AplOptions { MaxDegreeOfParallelism = -2 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AplOptions { MinChunkSize = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AplOptions { CancellationCheckInterval = 0 });
    }

    [Fact]
    public void MinChunkSizeKeepsSmallLoopsOnOneThread()
    {
        int callerThread = Environment.CurrentManagedThreadId;
        var threads = new HashSet<int>();
        Apl.For(0, 100, _ =>
        {
            lock (threads)
                threads.Add(Environment.CurrentManagedThreadId);
        }, new AplOptions { MaxDegreeOfParallelism = 8, MinChunkSize = 1000 });

        Assert.Equal([callerThread], threads);
    }

    [Fact]
    public void ACustomPartitionerReturningNoPartitionsIsReported()
    {
        var options = new AplOptions { Partitioner = new BrokenPartitioner(), MaxDegreeOfParallelism = 4 };
        Assert.Throws<InvalidOperationException>(() => Apl.For(0, 100, _ => { }, options));
    }

    private sealed class BrokenPartitioner : IPartitioner
    {
        public int GetPartitionCount(int length, int maxWorkers) => 0;

        public bool TryGetRange(int length, int partitionCount, int partition, int index, out int fromInclusive, out int toExclusive)
        {
            fromInclusive = toExclusive = 0;
            return false;
        }
    }

    // ---------------------------------------------------------------- exceptions

    [Fact]
    public void AnExceptionFromOneChunkIsAggregated()
    {
        foreach (IPartitioner partitioner in TestSupport.Partitioners)
        {
            var ex = Assert.Throws<AggregateException>(() =>
                Apl.For(0, 10_000, i =>
                {
                    if (i == 5_000)
                        throw new InvalidOperationException("boom");
                }, TestSupport.Options(4, partitioner)));

            var inner = Assert.Single(ex.InnerExceptions);
            Assert.IsType<InvalidOperationException>(inner);
            Assert.Equal("boom", inner.Message);
        }
    }

    [Fact]
    public void ExceptionsFromSeveralChunksAreAllCollected()
    {
        using var gate = new Barrier(4);
        var ex = Assert.Throws<AggregateException>(() =>
            Apl.For(0, 4, i =>
            {
                // Every worker gets as far as its throw before any of them does, so none is skipped.
                gate.SignalAndWait(TimeSpan.FromSeconds(10));
                throw new ArgumentException($"chunk {i}");
            }, TestSupport.Options(4)));

        Assert.Equal(4, ex.InnerExceptions.Count);
        Assert.All(ex.InnerExceptions, e => Assert.IsType<ArgumentException>(e));
    }

    [Fact]
    public void AnInlineLoopAlsoWrapsItsException()
    {
        var ex = Assert.Throws<AggregateException>(() => Apl.For(0, 1, _ => throw new FormatException()));
        Assert.IsType<FormatException>(Assert.Single(ex.InnerExceptions));
    }

    [Fact]
    public void AFailureStopsTheRestOfTheLoopEarly()
    {
        int ran = 0;
        var options = new AplOptions { MaxDegreeOfParallelism = 2, Partitioner = new WorkStealingPartitioner(1) };
        Assert.Throws<AggregateException>(() => Apl.For(0, 1_000_000, i =>
        {
            Interlocked.Increment(ref ran);
            if (i == 0)
                throw new InvalidOperationException();
            Thread.SpinWait(50);
        }, options));

        Assert.True(ran < 1_000_000, $"ran {ran} iterations after the first failed");
    }

    [Fact]
    public void TheLoopIsReusableAfterAFailure()
    {
        Assert.Throws<AggregateException>(() => Apl.For(0, 100, new ThrowingBody(), TestSupport.Options(4)));

        var hits = new int[100];
        Apl.For(0, 100, new CountingBody(hits, 0), TestSupport.Options(4));
        Assert.All(hits, h => Assert.Equal(1, h));
    }

    private readonly struct ThrowingBody : IWorkBody
    {
        public void Invoke(int index)
        {
            if (index == 50)
                throw new InvalidOperationException();
        }
    }

    // ---------------------------------------------------------------- cancellation

    [Fact]
    public void AnAlreadyCanceledTokenThrowsBeforeAnyIteration()
    {
        int calls = 0;
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            Apl.For(0, 100, _ => Interlocked.Increment(ref calls), new AplOptions { CancellationToken = cts.Token }));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void CancellationMidRunStopsTheLoop()
    {
        foreach (IPartitioner partitioner in TestSupport.Partitioners)
        {
            using var cts = new CancellationTokenSource();
            int ran = 0;
            var options = new AplOptions
            {
                MaxDegreeOfParallelism = 4,
                Partitioner = partitioner,
                CancellationToken = cts.Token,
                CancellationCheckInterval = 16,
            };

            var ex = Assert.ThrowsAny<OperationCanceledException>(() => Apl.For(0, 10_000_000, i =>
            {
                if (Interlocked.Increment(ref ran) == 1000)
                    cts.Cancel();
            }, options));

            Assert.Equal(cts.Token, ex.CancellationToken);
            Assert.True(ran < 10_000_000, $"{partitioner.GetType().Name}: all iterations ran despite cancellation");
        }
    }

    [Fact]
    public void CancellationStopsAnInlineLoopToo()
    {
        using var cts = new CancellationTokenSource();
        int ran = 0;
        var options = new AplOptions { MaxDegreeOfParallelism = 1, CancellationToken = cts.Token, CancellationCheckInterval = 10 };
        Assert.ThrowsAny<OperationCanceledException>(() => Apl.For(0, 1000, i =>
        {
            if (Interlocked.Increment(ref ran) == 25)
                cts.Cancel();
        }, options));
        Assert.Equal(30, ran);
    }

    [Fact]
    public void ABodyObservingTheLoopTokenIsCancellationNotFailure()
    {
        using var cts = new CancellationTokenSource();
        var options = new AplOptions { MaxDegreeOfParallelism = 4, CancellationToken = cts.Token };
        Assert.ThrowsAny<OperationCanceledException>(() => Apl.For(0, 1000, i =>
        {
            if (i == 10)
                cts.Cancel();
            cts.Token.ThrowIfCancellationRequested();
        }, options));
    }

    [Fact]
    public void AnUnrelatedOperationCanceledExceptionIsAFailure()
    {
        var ex = Assert.Throws<AggregateException>(() =>
            Apl.For(0, 100, i => { if (i == 3) throw new OperationCanceledException(); }, TestSupport.Options(4)));
        Assert.IsType<OperationCanceledException>(Assert.Single(ex.InnerExceptions));
    }

    [Fact]
    public void BlocksNeverExceedTheCancellationCheckInterval()
    {
        using var cts = new CancellationTokenSource();
        int largest = 0;
        var options = new AplOptions { MaxDegreeOfParallelism = 3, CancellationToken = cts.Token, CancellationCheckInterval = 100 };
        Apl.ForRange(0, 10_000, (from, to) =>
        {
            int size = to - from;
            int seen;
            while (size > (seen = Volatile.Read(ref largest)) && Interlocked.CompareExchange(ref largest, size, seen) != seen)
            {
            }
        }, options);
        Assert.Equal(100, largest);
    }

    [Fact]
    public void WithoutATokenARangeBodySeesWholePartitions()
    {
        var sizes = new List<int>();
        Apl.ForRange(0, 10_000, (from, to) =>
        {
            lock (sizes)
                sizes.Add(to - from);
        }, TestSupport.Options(4));
        Assert.Equal([2500, 2500, 2500, 2500], sizes.Order());
    }

    // ---------------------------------------------------------------- scheduling behaviour

    [Fact]
    public void NestedLoopsCompleteEvenWhenTheyOutnumberThePool()
    {
        long total = 0;
        Apl.For(0, Environment.ProcessorCount * 4, _ =>
        {
            Apl.For(0, 1000, _ => Interlocked.Increment(ref total));
        });
        Assert.Equal(Environment.ProcessorCount * 4 * 1000L, total);
    }

    [Fact]
    public async Task ConcurrentLoopsOnTheSameBodyTypeDoNotInterfere()
    {
        var tasks = Enumerable.Range(0, 16).Select(t => Task.Run(() =>
        {
            for (int round = 0; round < 50; round++)
            {
                var hits = new int[997];
                Apl.For(0, hits.Length, new CountingBody(hits, 0), TestSupport.Options(4));
                Assert.All(hits, h => Assert.Equal(1, h));
            }
        }));
        await Task.WhenAll(tasks);
    }

    [Fact]
    public void ExecutionContextDoesNotFlowToPoolWorkers()
    {
        // Pinned deliberately: this is the documented difference from Parallel.For. If it ever starts
        // flowing, the docs and the "why UnsafeQueueUserWorkItem" rationale are wrong.
        var local = new AsyncLocal<string?> { Value = "caller" };
        var seen = new string?[8];
        Apl.For(0, seen.Length, i =>
        {
            seen[i] = local.Value;
            Thread.Sleep(30);
        }, TestSupport.Options(seen.Length));

        Assert.Contains("caller", seen);
        Assert.Contains(null, seen);

        // And TPL, for contrast, flows it everywhere.
        var tplSeen = new string?[8];
        Parallel.For(0, tplSeen.Length, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i => tplSeen[i] = local.Value);
        Assert.All(tplSeen, s => Assert.Equal("caller", s));
    }

    [Fact]
    public void AMutableStructBodyGetsAPrivateCopyPerWorker()
    {
        var totals = new System.Collections.Concurrent.ConcurrentBag<int>();
        Apl.ForRange(0, 1000, new MutableRangeBody(totals), TestSupport.Options(4));
        Assert.Equal(1000, totals.Sum());
    }

    private struct MutableRangeBody(System.Collections.Concurrent.ConcurrentBag<int> totals) : IRangeWorkBody
    {
        private int _count;

        public void Invoke(int fromInclusive, int toExclusive)
        {
            for (int i = fromInclusive; i < toExclusive; i++)
                _count++;
            totals.Add(_count);
        }
    }
}
