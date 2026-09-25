// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace AplNet.Tests;

public class AsyncTests
{
    [Fact]
    public async Task ForEachAsyncVisitsEveryElementWithinTheDegreeOfParallelism()
    {
        var probe = new TestSupport.ConcurrencyProbe();
        int[] items = Enumerable.Range(0, 40).ToArray();
        var seen = new System.Collections.Concurrent.ConcurrentBag<int>();

        await Apl.ForEachAsync(items, async (item, ct) =>
        {
            probe.Enter();
            await Task.Delay(5, ct);
            seen.Add(item);
            probe.Exit();
        }, TestSupport.Options(3));

        Assert.Equal(items, seen.Order());
        Assert.InRange(probe.Peak, 1, 3);
    }

    [Fact]
    public async Task ForEachAsyncOverAnAsyncStream()
    {
        long sum = 0;
        await Apl.ForEachAsync(Numbers(100), (n, _) =>
        {
            Interlocked.Add(ref sum, n);
            return ValueTask.CompletedTask;
        });
        Assert.Equal(4950, sum);

        static async IAsyncEnumerable<int> Numbers(int count)
        {
            for (int i = 0; i < count; i++)
            {
                await Task.Yield();
                yield return i;
            }
        }
    }

    [Fact]
    public async Task ForAsyncHonoursCancellation()
    {
        using var cts = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Apl.ForAsync(0, 1000, async (i, ct) =>
        {
            if (i == 5)
                await cts.CancelAsync();
            await Task.Delay(1, ct);
        }, new AplOptions { MaxDegreeOfParallelism = 2, CancellationToken = cts.Token }));
    }
}
