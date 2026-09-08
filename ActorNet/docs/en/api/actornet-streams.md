# ActorNet.Streams

Generated - edit the `///` comments in the source, not this file.

## ActorStream

Entry points for building a stream.

### method `From<T0>(IAsyncEnumerable<T0>)`

Wraps an async sequence.

### method `From<T0>(IEnumerable<T0>)`

Wraps a synchronous sequence.

### method `Interval<T0>(TimeSpan, Func<Int64, T0>)`

Ticks on an interval.

## ActorStream&lt;T0&gt;

A reactive pipeline that ends by delivering to actors.

Deliberately small: this is `IAsyncEnumerable` with a few operators and an actor sink, not a port of Akka Streams. That is the honest scope - it covers "pull from a source, shape it, route each item to the actor that owns it", which is what the event-driven pipelines in the requirements actually need, and it composes with every other async-enumerable in .NET instead of inventing a parallel world.

Backpressure is whatever the consumer's pace makes it: nothing is buffered unless `Buffer` asks for it, and a bounded mailbox on the sink propagates the slowdown all the way back to the source.

### method `ActorStream<T0>(IAsyncEnumerable<T0>)`

A reactive pipeline that ends by delivering to actors.

Deliberately small: this is `IAsyncEnumerable` with a few operators and an actor sink, not a port of Akka Streams. That is the honest scope - it covers "pull from a source, shape it, route each item to the actor that owns it", which is what the event-driven pipelines in the requirements actually need, and it composes with every other async-enumerable in .NET instead of inventing a parallel world.

Backpressure is whatever the consumer's pace makes it: nothing is buffered unless `Buffer` asks for it, and a bounded mailbox on the sink propagates the slowdown all the way back to the source.

### method `AsAsyncEnumerable`

The underlying sequence, for interop with anything that takes one.

### method `Batch(Int32, Nullable<TimeSpan>)`

Groups items into batches of at most `size`, flushing early when `within` elapses.

The time bound is what keeps a partly-filled batch from sitting forever on a quiet stream - the failure mode of a size-only batcher.

### method `Buffer(Int32)`

Decouples producer and consumer with a bounded buffer.

### method `From(IAsyncEnumerable<T0>)`

Wraps any async sequence.

### method `From(IEnumerable<T0>)`

Wraps a synchronous sequence.

### method `Interval(TimeSpan, Func<Int64, T0>)`

Ticks `value` on an interval, forever.

### method `RunAsync(Func<T0, CancellationToken, ValueTask>, CancellationToken)`

Runs the pipeline for its side effects.

### method `SelectAsync<T0>(Func<T0, CancellationToken, ValueTask<T0>>)`

Projects each item asynchronously, one at a time, preserving order.

### method `Select<T0>(Func<T0, T0>)`

Projects each item.

### method `Take(Int32)`

Stops after `count` items.

### method `Tap(Action<T0>)`

Runs a side effect per item, passing it through. For logging and metrics.

### method `ToActorAsync(IActorSystem, ActorId, CancellationToken)`

Sends every item to one actor.

### method `ToActorsAsync(IActorSystem, Func<T0, ActorId>, CancellationToken)`

Sends each item to the actor chosen by `route` and returns how many were sent.

This is where a stream meets the actor model: routing by key means each item lands on the single activation that owns that key, so per-key ordering and single-writer state come for free.

### method `Where(Func<T0, Boolean>)`

Keeps only the items that satisfy `predicate`.

## IStreamPositions

Where a stream's progress is remembered between runs.

### method `LoadAsync(String, CancellationToken)`

The last offset handled for `stream`, or zero.

### method `SaveAsync(String, Int64, CancellationToken)`

Records that `stream` has been handled up to `offset`.

## ResumableStreams

Resuming a stream where the last run left off.

Only for a source that replays: the same items, in the same order, on the next run. A journal read does that. A socket, a timer and a queue that acknowledges do not, and skipping the first *n* items of one of those skips whatever happened to arrive first.

Delivery is at least once. The position is written after an item has been handled, so a crash between the two replays that item on the next run - which is the safe direction, and the reason a handler behind this has to be idempotent. Writing the position first would make it at most once, and lose the item instead.

### method `Resume<T0>(ActorStream<T0>, IStreamPositions, String, Func<T0, Int64>, Int32)`

Skips what has already been handled, and records progress as it goes.

- `source` — The stream to resume. Must replay the same items in the same order.
- `positions` — Where progress is kept.
- `name` — The name this stream's position is kept under.
- `offsetOf` — The offset an item represents. Defaults to counting items, which is right for a source whose items carry no position of their own - and wrong for one whose items do, because a count cannot survive the source itself being compacted.
- `checkpointEvery` — Items handled between writes of the position. One is the safest and the most expensive; a larger number replays more after a crash and writes less during normal running.

## StoredStreamPositions

Stream positions kept in the ordinary state store, so they last as long as anything else does.

A position is a tiny piece of durable state keyed by a name, which is exactly what `IStateStore` is. Giving positions a store of their own would mean a second thing to configure, a second thing to back up, and a second provider per database.

### method `StoredStreamPositions(IStateStore, String)`

Stream positions kept in the ordinary state store, so they last as long as anything else does.

A position is a tiny piece of durable state keyed by a name, which is exactly what `IStateStore` is. Giving positions a store of their own would mean a second thing to configure, a second thing to back up, and a second provider per database.

### method `LoadAsync(String, CancellationToken)`

### method `SaveAsync(String, Int64, CancellationToken)`

## StreamPosition

How far a named stream has been read.

- `Stream` — The name the position is kept under.
- `Offset` — The offset of the last item handled.
- `UpdatedAt` — When it was last written.

### method `StreamPosition(String, Int64, DateTimeOffset)`

How far a named stream has been read.

- `Stream` — The name the position is kept under.
- `Offset` — The offset of the last item handled.
- `UpdatedAt` — When it was last written.

### property `Offset`

The offset of the last item handled.

### property `Stream`

The name the position is kept under.

### property `UpdatedAt`

When it was last written.

## StreamShapes

Operators that change a pipeline's shape rather than its contents: several sources into one, one source into several.

Everything on `ActorStream` itself is one-in, one-out, which is what an async enumerable is. These are the two shapes that are not, and they are here rather than on the class because neither has a single obvious receiver - a merge belongs to no one source, and a split returns something that is not a stream at all.

Both are built on a channel, for the same reason: the sources run at their own pace and a reader pulls at its own, so something has to sit between them. The channel is bounded, so a fast source waits for a slow reader instead of filling the heap - the same trade the mailboxes make.

### field `DefaultBuffer`

How much a shape buffers between its producers and its consumer.

Small on purpose. The point of a bounded buffer is that a fast producer is made to wait, and a large one only delays finding out whether the consumer can keep up.

### method `FanInAsync<T0>(IActorSystem, ActorId, IEnumerable<ActorStream<T0>>, CancellationToken)`

Runs several sources into one actor, which is the common reason to merge.

### method `Merge<T0>(ActorStream<T0>[])`

Reads several sources at once, yielding items in whatever order they arrive.

Interleaved, not concatenated: every source is read concurrently, and a source that has nothing to say does not hold up the others. That is the whole difference from chaining, and it is why the order of the result is not the order of the arguments.

### method `Merge<T0>(IEnumerable<IAsyncEnumerable<T0>>, Int32)`

The same, for sequences that were never `ActorStream` to begin with.

### method `Merge<T0>(Int32, ActorStream<T0>[])`

### method `Split<T0, T1>(ActorStream<T0>, Func<T0, T1>, IReadOnlyCollection<T1>, Int32)`

Splits one source into several, by giving each item a key.

The keys have to be known in advance, and that is deliberate. A split that invented a branch on first sight would have to buffer everything for a branch nobody is reading yet, and the buffer would be unbounded because nothing can say how long "yet" is. Naming the branches up front makes the memory bounded and the failure mode obvious.

An item whose key is not one of them is dropped. There is nowhere to put it, and the alternative - failing the whole pipeline over one item - is worse for the thing this is usually doing, which is fanning traffic out by type or by tenant.

Every branch must be consumed, and concurrently. They share one reader of the source, so a branch nobody reads fills its buffer and then stops the source, which stops the others.

---

[Back to the index](README.md)
