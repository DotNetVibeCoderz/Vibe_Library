// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Diagnostics.Tracing;
using AplNet.Core.Partitioners;
using AplNet.Diagnostics;

namespace AplNet.Tests;

[Collection(nameof(AllocationCollection))]
public class DiagnosticsTests
{
    [Fact]
    public void CountersMoveOnlyWhileTheSourceIsEnabled()
    {
        long loops = AplEventSource.Log.LoopsExecuted;
        Apl.For(0, 10_000, _ => { }, TestSupport.Options(4));
        Assert.False(AplEventSource.Log.IsEnabled());
        Assert.Equal(loops, AplEventSource.Log.LoopsExecuted);

        using var listener = new CounterListener();
        Assert.True(AplEventSource.Log.IsEnabled());

        long before = AplEventSource.Log.LoopsExecuted;
        long beforeInline = AplEventSource.Log.InlineLoopsExecuted;
        long beforeChunks = AplEventSource.Log.ChunksExecuted;
        long beforeSteals = AplEventSource.Log.StealCount;

        Apl.For(0, 10_000, _ => { }, TestSupport.Options(4));
        Apl.For(0, 10, _ => { }, TestSupport.Options(1));

        var options = new AplOptions { MaxDegreeOfParallelism = 4, Partitioner = new WorkStealingPartitioner(1) };
        for (int round = 0; round < 20; round++)
            Apl.For(0, 400, i => { if (i < 100) Thread.SpinWait(2000); }, options);

        Assert.True(AplEventSource.Log.LoopsExecuted >= before + 21);
        Assert.True(AplEventSource.Log.InlineLoopsExecuted >= beforeInline + 1);
        Assert.True(AplEventSource.Log.ChunksExecuted >= beforeChunks + 4);
        Assert.True(AplEventSource.Log.StealCount > beforeSteals, "a skewed work-stealing loop should record steals");
    }

    private sealed class CounterListener : EventListener
    {
        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name == "AplNet")
                EnableEvents(eventSource, EventLevel.Informational, EventKeywords.All, new Dictionary<string, string?> { ["EventCounterIntervalSec"] = "1" });
        }
    }
}
