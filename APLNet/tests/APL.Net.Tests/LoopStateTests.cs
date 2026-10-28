// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using AplNet.Core;
using AplNet.Core.Partitioners;

namespace AplNet.Tests;

public class LoopStateTests
{
    [Fact]
    public void ALoopThatNeverStopsIsCompleted()
    {
        int calls = 0;
        AplLoopResult result = Apl.For(0, 10_000, (_, _) => Interlocked.Increment(ref calls), TestSupport.Options(4));
        Assert.True(result.IsCompleted);
        Assert.Null(result.LowestBreakIteration);
        Assert.Equal(10_000, calls);
    }

    [Fact]
    public void StopEndsTheLoopEarly()
    {
        foreach (IPartitioner partitioner in TestSupport.Partitioners)
        {
            const int n = 4_000_000;
            long calls = 0;
            AplLoopResult result = Apl.For(0, n, (i, state) =>
            {
                Interlocked.Increment(ref calls);
                if (i == 1000)
                    state.Stop();
            }, TestSupport.Options(4, partitioner));

            Assert.False(result.IsCompleted);
            Assert.Null(result.LowestBreakIteration);
            Assert.True(calls < n, $"{partitioner.GetType().Name}: {calls} of {n} iterations ran after Stop");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4999)]
    [InlineData(77_777)]
    public void BreakRunsEveryIterationBelowIt(int breakAt)
    {
        foreach (IPartitioner partitioner in TestSupport.Partitioners)
        {
            const int n = 100_000;
            var ran = new int[n];
            AplLoopResult result = Apl.For(0, n, (i, state) =>
            {
                ran[i] = 1;
                if (i >= breakAt)
                    state.Break();
            }, TestSupport.Options(4, partitioner));

            Assert.False(result.IsCompleted);
            Assert.Equal(breakAt, result.LowestBreakIteration);
            for (int i = 0; i < breakAt; i++)
                Assert.True(ran[i] == 1, $"{partitioner.GetType().Name}: iteration {i} below the break at {breakAt} did not run");
        }
    }

    [Fact]
    public void StateReportsTheCurrentIterationAndWhetherToExit()
    {
        bool sawExit = false;
        Apl.For(0, 100, (i, state) =>
        {
            Assert.Equal(i, state.CurrentIteration);
            if (i == 10)
            {
                state.Stop();
                Assert.True(state.IsStopped);
                Assert.True(state.ShouldExitCurrentIteration);
                sawExit = true;
            }
        }, TestSupport.Options(1));
        Assert.True(sawExit);
    }

    [Fact]
    public void MixingStopAndBreakThrows()
    {
        var ex = Assert.Throws<AggregateException>(() => Apl.For(0, 10, (i, state) =>
        {
            state.Break();
            state.Stop();
        }, TestSupport.Options(1)));
        Assert.IsType<InvalidOperationException>(Assert.Single(ex.InnerExceptions));

        ex = Assert.Throws<AggregateException>(() => Apl.For(0, 10, (i, state) =>
        {
            state.Stop();
            state.Break();
        }, TestSupport.Options(1)));
        Assert.IsType<InvalidOperationException>(Assert.Single(ex.InnerExceptions));
    }

    [Fact]
    public void StructBodyFindsTheFirstMatch()
    {
        int[] data = Enumerable.Range(0, 1_000_000).Select(i => i % 9973).ToArray();
        data[123_456] = -1;
        data[654_321] = -1;
        var options = TestSupport.Options(8);

        AplLoopResult result = Apl.ForWithState(0, data.Length, new FindNegative(data), options);
        Assert.Equal(123_456, result.LowestBreakIteration);
    }

    [Fact]
    public void NestedStatefulLoopsKeepTheirOwnState()
    {
        AplLoopResult outer = Apl.For(0, 8, (i, outerState) =>
        {
            AplLoopResult inner = Apl.For(0, 1000, (j, innerState) =>
            {
                if (j == 10)
                    innerState.Stop();
            }, TestSupport.Options(2));
            Assert.False(inner.IsCompleted);
            Assert.False(outerState.IsStopped);
        }, TestSupport.Options(2));
        Assert.True(outer.IsCompleted);
    }

    internal readonly struct FindNegative(int[] data) : IStatefulWorkBody
    {
        public void Invoke(int index, AplLoopState state)
        {
            if (data[index] < 0)
                state.Break();
        }
    }
}
