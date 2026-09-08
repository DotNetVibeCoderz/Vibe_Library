# Streams

*Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.*
*[Bahasa Indonesia](../id/07-streams.md) · [Docs index](README.md)*

## Scope, stated honestly

`ActorStream` is `IAsyncEnumerable<T>` with a few operators and an actor sink. It is **not** a port
of Akka Streams: there is no graph DSL, no materialisation, no dynamic fan-in.

That is the scope that earns its place. It covers "pull from a source, shape it, route each item to
the actor that owns it", which is what event-driven pipelines actually need here, and it composes
with every other async enumerable in .NET instead of inventing a parallel world.

## The shape

```csharp
await ActorStream.From(readings)
    .Where(r => r.Celsius > -50)
    .Select(r => r with { Celsius = r.Celsius + calibration })
    .Batch(200, within: TimeSpan.FromSeconds(1))
    .ToActorsAsync(system, batch => ActorId.For<DeviceActor>(batch[0].DeviceId));
```

## Operators

| | |
| --- | --- |
| `Where(predicate)` | Keep matching items |
| `Select(selector)` | Project |
| `SelectAsync(selector)` | Project asynchronously, one at a time, order preserved |
| `Take(count)` | Stop after N |
| `Batch(size, within)` | Group into batches, flushing early on a deadline |
| `Buffer(capacity)` | Decouple producer and consumer with a bounded queue |
| `Tap(effect)` | Side effect per item, passing it through |

### Why `Batch` takes a time bound

A size-only batcher leaves a partly-filled batch sitting forever on a quiet stream. `within` is
what stops the last 37 readings of the day from never being delivered. The check runs per item
rather than on a timer — a timer would need a second thread and a lock over the list, and this is
enough for a stream that is producing at all.

## Sinks

```csharp
// Route by key: each item lands on the activation that owns it.
await stream.ToActorsAsync(system, item => ActorId.For<DeviceActor>(item.DeviceId));

// Everything to one actor.
await stream.ToActorAsync(system, ActorId.For<AlarmDeskActor>("main"));

// Just run it.
var count = await stream.RunAsync(async (item, ct) => await Handle(item, ct));
```

`ToActorsAsync` is where a stream meets the actor model. Routing by key means every item for a key
lands on that key's single activation, so **per-key ordering and single-writer state come for
free** — no partitioning scheme, no locks, no coordinating consumer group.

## Backpressure

There is no separate backpressure protocol. The stream pulls, so the consumer's pace is the
producer's pace.

Where that matters:

- **Unbounded mailboxes (the default) give no backpressure.** `ToActorsAsync` will happily fill
  memory if the actors cannot keep up.
- **A bounded mailbox propagates.** Set `options.MailboxCapacity` and a send stops completing
  synchronously once the actor falls behind, which pushes the slowdown back through the stream to
  the source.
- **`Buffer(n)` absorbs a burst**, not a sustained mismatch. It is a shock absorber, not a fix.

If a stream is feeding actors from an external firehose, bound the mailbox. That is the one setting
that turns "slow consumer" from an out-of-memory into a slowdown.

## Failures

A producer failure propagates to the consumer, including through `Buffer`:

```csharp
await Assert.ThrowsAsync<InvalidOperationException>(async () =>
    await ActorStream.From(Failing()).Buffer(4).RunAsync());
```

A buffered stream that swallowed the producer's exception would look like a stream that simply
ended, and the caller would never learn it lost data.

The sink side is different: `ToActorsAsync` uses `TellAsync`, so a failure *inside* an actor is the
actor's supervisor's problem, not the stream's. The stream sees a successful send. If you need the
stream to know, use an ask in `RunAsync` instead.

## Interop

```csharp
IAsyncEnumerable<T> raw = stream.AsAsyncEnumerable();   // out
ActorStream.From(channel.Reader.ReadAllAsync());        // in
ActorStream.From(File.ReadLinesAsync(path));
ActorStream.Interval(TimeSpan.FromSeconds(1), tick => new Heartbeat(tick));
```

Anything producing an `IAsyncEnumerable` is a source — a channel, a file, an EF Core query, a Kafka
consumer wrapper.

## A worked example

The telemetry sample, in full:

```csharp
await ActorStream
    .Interval(TimeSpan.FromMilliseconds(30), tick => tick)
    .Select(tick =>
    {
        var device = (int)(tick % deviceCount);
        return new SensorReading($"sensor-{device:D3}", ReadTemperature(device), DateTimeOffset.UtcNow);
    })
    .ToActorsAsync(system, reading => ActorId.For<DeviceActor>(reading.DeviceId), cancellationToken);
```

Each device actor keeps a rolling aggregate with no locking, raises an alarm to a single desk actor
when it goes over temperature, and is swept when it stops reporting.

![Telemetry: a stream into one actor per device](../images/samples-telemetry.png)

## Changing a pipeline's shape

Everything on `ActorStream<T>` itself is one-in, one-out, which is what an async enumerable is.
`StreamShapes` has the two that are not:

```csharp
// Several sources read at once, in whatever order they arrive.
var merged = StreamShapes.Merge(fromKafka, fromTimer, fromHttp);

// One source into named branches.
var byKind = StreamShapes.Split(events, e => e.Kind, ["order", "payment", "refund"]);
await Task.WhenAll(byKind.Select(b => b.Value.ToActorsAsync(system, Route)));

// The common reason to merge: several sources into one actor.
await StreamShapes.FanInAsync(system, auditor, [fromKafka, fromHttp]);
```

A merge is interleaved, not concatenated: every source is read concurrently, so a source with
nothing to say does not hold up the others — and the order of the result is not the order of the
arguments. A source that fails fails the merge, because a merge that swallowed it would report a
short stream as a complete one.

A split's branches must be named in advance. A split that invented a branch on first sight would
have to buffer everything for a branch nobody is reading yet, and the buffer would be unbounded
because nothing can say how long "yet" is. An item whose key names no branch is dropped — failing a
whole pipeline over one unroutable item is the worse of the two answers for what this is usually
doing. **Every branch must be consumed, concurrently**: they share one reader of the source, so a
branch nobody reads fills its buffer and stops the source, which stops the others.

Both buffer, and both bound the buffer, for the same reason the mailboxes do: a fast producer is
made to wait for a slow reader instead of filling the heap.

## Resuming where a run left off

```csharp
var positions = new StoredStreamPositions(system.Options.StateStore);

await journalEntries
    .Resume(positions, "audit-view", offsetOf: entry => entry.Sequence)
    .ToActorAsync(system, auditor);
```

The position lives in the ordinary state store, so it lasts exactly as long as everything else does
and needs no second thing to configure or back up.

Two things to be clear about:

**Only for a source that replays** — the same items, in the same order, on the next run. A journal
read does that. A socket, a timer and a queue that acknowledges do not, and skipping the first *n*
items of one of those skips whatever happened to arrive first.

**Delivery is at least once.** The position is written after an item has been handled, so an
interruption replays that item rather than losing it, and a handler behind this has to be
idempotent. Writing the position first would make it at most once and lose the item instead.

`checkpointEvery` trades writes against replay: the position lags by up to that many items while the
run is going, and a run that ends — cleanly, early, or by throwing — flushes what it got to. An item
delivered to a consumer that then stopped the enumeration is *not* counted as handled, because the
producer never got control back to learn that it was; it replays, which is the safe direction.

See the [roadmap](../../Plan.md).

## Next

- [Actors and lifecycle](03-actors.md)
- [Performance](10-performance.md)
