// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Diagnostics;
using ActorNet.Metrics;

namespace ActorNet.Tests;

/// <summary>
/// One operation that crosses a node boundary staying one trace.
/// </summary>
/// <remarks>
/// Spans were already emitted per message, but a message that crossed the wire started a trace of
/// its own: an ask answered on another node read as two unrelated traces, and the interesting part
/// - which hop was slow - was exactly what got lost.
/// </remarks>
public sealed class TracingTests : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly List<Activity> _spans = [];
    private readonly Lock _gate = new();

    public TracingTests()
    {
        // Without a listener StartActivity returns null, by design - that is what makes a span per
        // message affordable. Which also means a test has to listen to see anything at all.
        _listener = new ActivityListener
        {
            // The test's own source too: a caller span has to exist before there is a trace for the
            // receiving side to join.
            ShouldListenTo = source => source.Name is ActorNetDiagnostics.ActivitySourceName or "test",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (_gate) _spans.Add(activity);
            },
        };

        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose() => _listener.Dispose();

    /// <summary>Receive spans for one actor key.</summary>
    /// <remarks>
    /// An <see cref="ActivityListener"/> is process-wide and test classes run in parallel, so the
    /// raw list holds spans from whatever else is running. Filtering by the key under test is what
    /// keeps these assertions about this test.
    /// </remarks>
    private Activity[] ReceivesFor(string actorKey)
    {
        lock (_gate)
        {
            return _spans
                .Where(a => a.OperationName.EndsWith("receive", StringComparison.Ordinal))
                .Where(a => a.GetTagItem("actornet.actor.key") as string == actorKey)
                .ToArray();
        }
    }

    [Fact]
    public async Task AnAskAcrossNodesStaysOneTrace()
    {
        await using var harness = new TestHarness();

        var here = await harness.NetworkedAsync("trace-a", seeds: []);
        var there = await harness.NetworkedAsync("trace-b", seeds: [$"127.0.0.1:{here.BoundPort}"]);

        await TestHarness.AssertEventuallyAsync(
            () => here.Cluster.Members.Count == 2 && there.Cluster.Members.Count == 2,
            "the cluster should converge", TimeSpan.FromSeconds(15));

        await TestHarness.AssertRingsAgreeAsync(here, there);

        var remote = Enumerable.Range(0, 2000)
            .Select(i => ActorId.For<CounterActor>($"trace-{i}"))
            .First(id => here.Cluster.OwnerOf(id) == "trace-b");

        using var caller = new ActivitySource("test").StartActivity("caller")
            ?? throw new InvalidOperationException("the test source needs its own listener");

        await here.TellAsync(remote, new Add(3));
        var total = await here.AskAsync<Total>(remote, new GetTotal(), TimeSpan.FromSeconds(15));

        Assert.Equal(3, total.Value);

        // The receiving span runs on the other node, and it has to sit under the caller's trace or
        // a viewer draws two unrelated ones.
        var received = ReceivesFor(remote.Key);
        Assert.NotEmpty(received);
        Assert.All(received, span => Assert.Equal(caller.TraceId, span.TraceId));
    }

    [Fact]
    public async Task ALocalSendIsStillUnderTheCallersSpan()
    {
        await using var harness = new TestHarness();
        var system = await harness.LocalAsync();

        using var caller = new ActivitySource("test").StartActivity("caller")
            ?? throw new InvalidOperationException("the test source needs its own listener");

        await system.AskAsync<Total>(ActorId.For<CounterActor>("local-trace"), new GetTotal(), TimeSpan.FromSeconds(10));

        // Nothing was serialized here, so this works through Activity.Current rather than the wire
        // field. Worth pinning: it would be easy to make the remote path work and quietly break the
        // path almost every message takes.
        var received = Assert.Single(ReceivesFor("local-trace"));
        Assert.Equal(caller.TraceId, received.TraceId);
    }

    [Fact]
    public void AReceiveWithNoParentStartsItsOwnTrace()
    {
        using var orphan = ActorNetDiagnostics.StartReceive(ActorId.For<CounterActor>("orphan"), "Add");

        // A message with no trace on it is not an error - a timer tick, a client that does not
        // trace - and it should still be observable rather than dropped.
        Assert.NotNull(orphan);
        Assert.NotEqual(default, orphan.TraceId);
    }

    [Fact]
    public void AnIncomingTraceParentIsAdopted()
    {
        const string parent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";

        using var span = ActorNetDiagnostics.StartReceive(
            ActorId.For<CounterActor>("adopted"), "Add", parent, traceState: "vendor=1");

        Assert.NotNull(span);
        Assert.Equal("0af7651916cd43dd8448eb211c80319c", span.TraceId.ToHexString());
        Assert.Equal("b7ad6b7169203331", span.ParentSpanId.ToHexString());
        Assert.Equal("vendor=1", span.TraceStateString);
    }
}
