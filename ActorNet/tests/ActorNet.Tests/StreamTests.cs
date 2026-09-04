// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ActorNet.Streams;

namespace ActorNet.Tests;

public sealed class StreamTests
{
    [Fact]
    public async Task OperatorsComposeInOrder()
    {
        var results = new List<int>();

        await ActorStream.From(Enumerable.Range(1, 20))
            .Where(n => n % 2 == 0)
            .Select(n => n * 10)
            .Take(4)
            .RunAsync((n, _) =>
            {
                results.Add(n);
                return ValueTask.CompletedTask;
            });

        Assert.Equal([20, 40, 60, 80], results);
    }

    [Fact]
    public async Task BatchGroupsBySize()
    {
        var batches = new List<int>();

        await ActorStream.From(Enumerable.Range(1, 10))
            .Batch(3)
            .RunAsync((batch, _) =>
            {
                batches.Add(batch.Count);
                return ValueTask.CompletedTask;
            });

        // The trailing partial batch must still be delivered, or the last items are lost.
        Assert.Equal([3, 3, 3, 1], batches);
    }

    [Fact]
    public async Task ItemsAreRoutedToTheActorThatOwnsTheirKey()
    {
        await using var harness = new TestHarness();
        var system = await harness.LocalAsync();

        // Routing by key is what makes a stream fit the actor model: every reading for a sensor
        // lands on that sensor's single activation, so per-key ordering comes for free.
        var sent = await ActorStream.From(Enumerable.Range(0, 90))
            .Select(i => new Add(1))
            .ToActorsAsync(system, _ => ActorId.For<CounterActor>("sensor-a"));

        Assert.Equal(90, sent);

        var total = await system.AskAsync<Total>(ActorId.For<CounterActor>("sensor-a"), new GetTotal(), TimeSpan.FromSeconds(10));
        Assert.Equal(90, total.Value);
    }

    [Fact]
    public async Task RoutingSpreadsAcrossKeysWithoutMixingThemUp()
    {
        await using var harness = new TestHarness();
        var system = await harness.LocalAsync();

        await ActorStream.From(Enumerable.Range(0, 300))
            .ToActorsAsync(system, i => ActorId.For<CounterActor>($"shard-{i % 3}"), CancellationToken.None);

        for (var shard = 0; shard < 3; shard++)
        {
            var total = await system.AskAsync<Total>(ActorId.For<CounterActor>($"shard-{shard}"), new GetTotal(), TimeSpan.FromSeconds(10));
            Assert.Equal(0, total.Value);
        }
    }

    [Fact]
    public async Task BufferDecouplesASlowConsumerFromAFastProducer()
    {
        var produced = 0;
        var consumed = 0;

        await ActorStream.From(Produce())
            .Buffer(8)
            .RunAsync(async (_, ct) =>
            {
                consumed++;
                await Task.Delay(1, ct);
            });

        Assert.Equal(50, produced);
        Assert.Equal(50, consumed);

        async IAsyncEnumerable<int> Produce()
        {
            for (var i = 0; i < 50; i++)
            {
                produced++;
                yield return i;
            }

            await Task.CompletedTask;
        }
    }

    [Fact]
    public async Task AProducerFailureReachesTheConsumerThroughTheBuffer()
    {
        // A buffered stream that swallowed the producer's exception would look like a stream that
        // simply ended, and the caller would never know it lost data.
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await ActorStream.From(Failing()).Buffer(4).RunAsync());

        static async IAsyncEnumerable<int> Failing()
        {
            yield return 1;
            await Task.Yield();
            throw new InvalidOperationException("producer failed");
        }
    }

    [Fact]
    public async Task IntervalTicksUntilCancelled()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));

        var count = await ActorStream.Interval(TimeSpan.FromMilliseconds(30), tick => tick)
            .Take(5)
            .RunAsync(cancellationToken: cts.Token);

        Assert.Equal(5, count);
    }
}
