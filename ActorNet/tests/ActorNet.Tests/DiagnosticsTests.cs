// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using ActorNet.Metrics;
using ActorNet.Runtime;

namespace ActorNet.Tests;

/// <summary>
/// Undeliverable messages were logged and dropped, which makes them invisible to everything but a
/// human reading logs. These check they are recorded instead.
/// </summary>
public sealed class DeadLetterTests
{
    [Fact]
    public async Task SendingToAnUnregisteredTypeIsRecorded()
    {
        await using var harness = new TestHarness();
        var system = await harness.LocalAsync();

        await Assert.ThrowsAsync<ActorTypeNotRegisteredException>(
            async () => await system.TellAsync(new ActorId("GhostActor", "x"), new Add(1)));

        // Thrown *and* recorded. A local caller learns immediately, but an inbound remote frame has
        // no caller to tell, and both paths land here.
        var letter = Assert.Single(system.DeadLetters.Recent());
        Assert.Equal(DeadLetterReason.UnregisteredActorType, letter.Reason);
        Assert.Equal("GhostActor", letter.Target.Type);
        Assert.Contains("not registered", letter.Detail);
    }

    [Fact]
    public async Task TheCounterAndTheQueueAgree()
    {
        await using var harness = new TestHarness();
        var system = await harness.LocalAsync();

        for (var i = 0; i < 5; i++)
        {
            try { await system.TellAsync(new ActorId("GhostActor", $"x{i}"), new Add(1)); }
            catch (ActorTypeNotRegisteredException) { }
        }

        Assert.Equal(5, system.DeadLetters.Count);
        Assert.Equal(5, system.Metrics.Snapshot(false).DeadLetters);
    }

    [Fact]
    public void TheBufferIsBoundedAndKeepsTheNewest()
    {
        var queue = new DeadLetterQueue(capacity: 3);

        for (var i = 0; i < 10; i++)
        {
            queue.Record(new DeadLetter(new ActorId("A", $"k{i}"), ActorId.None, null, "Msg",
                DeadLetterReason.NodeUnreachable, "detail", DateTimeOffset.UtcNow));
        }

        // A node that is failing to deliver usually fails a lot. Retaining all of it would be a
        // second outage on top of the first - but the lifetime count is still exact.
        var retained = queue.Recent();
        Assert.Equal(3, retained.Count);
        Assert.Equal(10, queue.Count);
        Assert.Equal("k9", retained[0].Target.Key);
        Assert.Equal("k7", retained[2].Target.Key);
    }

    [Fact]
    public void SubscribersAreNotifiedAndCannotBreakTheSender()
    {
        var queue = new DeadLetterQueue();
        var seen = new List<DeadLetter>();

        queue.LetterRecorded += _ => throw new InvalidOperationException("a badly behaved subscriber");
        queue.LetterRecorded += seen.Add;

        queue.Record(new DeadLetter(new ActorId("A", "k"), ActorId.None, null, "Msg",
            DeadLetterReason.Shutdown, "detail", DateTimeOffset.UtcNow));

        // A throwing subscriber must not turn an undeliverable message into a failed send. The
        // letter is still recorded either way.
        Assert.Equal(1, queue.Count);
        Assert.Single(seen);
    }

    [Fact]
    public void ClearingKeepsTheLifetimeCount()
    {
        var queue = new DeadLetterQueue();
        queue.Record(new DeadLetter(ActorId.None, ActorId.None, null, "Msg",
            DeadLetterReason.Shutdown, "d", DateTimeOffset.UtcNow));

        queue.Clear();

        Assert.Empty(queue.Recent());
        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public async Task ARefusedWireTypeIsRecordedWithoutMaterializingIt()
    {
        await using var harness = new TestHarness();
        var system = await harness.NetworkedAsync("dl-node");

        await using var client = new Client.ActorNetClient("127.0.0.1", system.BoundPort);
        client.RegisterMessage<UnknownToTheNode>("tests.refused-alias");

        await client.TellAsync(ActorId.For<CounterActor>("victim"), new UnknownToTheNode("payload"));

        await TestHarness.AssertEventuallyAsync(() => system.DeadLetters.Count > 0,
            "the refused alias should have been recorded");

        var letter = system.DeadLetters.Recent()[0];
        Assert.Equal(DeadLetterReason.UnknownMessageType, letter.Reason);
        Assert.Equal("tests.refused-alias", letter.MessageType);

        // The body is deliberately absent. Materializing a payload the allow-list just refused to
        // construct would undo the refusal.
        Assert.Null(letter.Message);
        Assert.DoesNotContain(ActorId.For<CounterActor>("victim"), system.LocalActors);
    }
}

/// <summary>
/// The OpenTelemetry surface. The console reads the runtime's own counters, which is fine for one
/// node and useless for anything that aggregates.
/// </summary>
public sealed class OpenTelemetryTests
{
    [Fact]
    public async Task HandlingAMessageEmitsASpan()
    {
        var spans = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ActorNetDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Add,
        };
        ActivitySource.AddActivityListener(listener);

        await using var harness = new TestHarness();
        var system = await harness.LocalAsync();

        await system.AskAsync<Total>(ActorId.For<CounterActor>("traced"), new GetTotal());

        Assert.NotEmpty(spans);
        var span = spans.First(a => a.GetTagItem("actornet.actor.key") as string == "traced");
        Assert.Equal("CounterActor receive", span.OperationName);
        Assert.Equal("CounterActor", span.GetTagItem("actornet.actor.type"));
        Assert.Equal(ActivityKind.Consumer, span.Kind);
    }

    [Fact]
    public async Task AFailingHandlerMarksItsSpanAsAnError()
    {
        var spans = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ActorNetDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Add,
        };
        ActivitySource.AddActivityListener(listener);

        await using var harness = new TestHarness();
        var system = await harness.LocalAsync();
        system.RegisterActor<AskingFlakyActor>();

        await Assert.ThrowsAsync<ActorNetException>(
            async () => await system.AskAsync<Total>(ActorId.For<AskingFlakyActor>("boom"), new Boom("failed"), TimeSpan.FromSeconds(5)));

        // The ask is completed with the failure from inside the catch block, so it returns before
        // the span is disposed. Wait for the span rather than assuming it has already stopped.
        await TestHarness.AssertEventuallyAsync(
            () => spans.Any(a => a.GetTagItem("actornet.actor.key") as string == "boom"),
            "the failing handler should have produced a span");

        // A trace that showed a failing handler as a successful span would be worse than no trace.
        var span = spans.First(a => a.GetTagItem("actornet.actor.key") as string == "boom");
        Assert.Equal(ActivityStatusCode.Error, span.Status);
    }

    [Fact]
    public async Task CountersAndHistogramsAreRecorded()
    {
        var measurements = new List<(string Instrument, long Value)>();
        var durations = new List<string>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == ActorNetDiagnostics.MeterName) l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        {
            lock (measurements) measurements.Add((instrument.Name, value));
        });
        listener.SetMeasurementEventCallback<double>((instrument, _, _, _) =>
        {
            lock (durations) durations.Add(instrument.Name);
        });
        listener.Start();

        await using var harness = new TestHarness();
        var system = await harness.LocalAsync();

        var counter = system.ActorOf<CounterActor>("metered");
        await counter.TellAsync(new Add(1));
        await counter.AskAsync<Total>(new GetTotal());

        listener.RecordObservableInstruments();

        lock (measurements)
        {
            Assert.Contains(measurements, m => m.Instrument == "actornet.messages.processed");
            Assert.Contains(measurements, m => m.Instrument == "actornet.actors.activated");
        }

        lock (durations)
        {
            Assert.Contains("actornet.message.duration", durations);
            Assert.Contains("actornet.message.queue_time", durations);
        }
    }

    [Fact]
    public void TheSourceAndMeterAreNamedForAProvider()
    {
        // These strings go into an application's tracer and meter provider configuration, so
        // changing them silently breaks every deployment that already exports.
        Assert.Equal("ActorNet", ActorNetDiagnostics.ActivitySourceName);
        Assert.Equal("ActorNet", ActorNetDiagnostics.MeterName);
    }
}
