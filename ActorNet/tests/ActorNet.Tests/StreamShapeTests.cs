// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ActorNet.Streams;

namespace ActorNet.Tests;

/// <summary>
/// Several sources into one, and one source into several.
/// </summary>
/// <remarks>
/// Everything else on a stream is one-in one-out, which is what an async enumerable is. These two
/// shapes are not, and both need a buffer between producers running at their own pace and a consumer
/// pulling at its own - so both are also about what happens when one side cannot keep up.
/// </remarks>
public sealed class StreamShapeTests
{
    private static ActorStream<int> Of(params int[] items) => ActorStream<int>.From(items);

    private static async IAsyncEnumerable<int> SlowlyAsync(int from, int count, TimeSpan gap)
    {
        for (var i = 0; i < count; i++)
        {
            await Task.Delay(gap);
            yield return from + i;
        }
    }

    [Fact]
    public async Task AMergeYieldsEverythingFromEverySource()
    {
        var merged = StreamShapes.Merge(Of(1, 2, 3), Of(4, 5), Of(6));

        var seen = new List<int>();
        await merged.RunAsync((item, _) => { seen.Add(item); return ValueTask.CompletedTask; },
            TestContext.Current.CancellationToken);

        Assert.Equal([1, 2, 3, 4, 5, 6], seen.Order());
    }

    [Fact]
    public async Task AMergeDoesNotWaitForOneSourceToFinishBeforeReadingAnother()
    {
        // The first source is slow and the second is not. Concatenation would give 1, 2, 3 and then
        // 10, 11, 12; interleaving is the whole difference, and it is why the order of a merge is
        // not the order of its arguments.
        var merged = StreamShapes.Merge(
            ActorStream<int>.From(SlowlyAsync(1, 3, TimeSpan.FromMilliseconds(120))),
            ActorStream<int>.From(SlowlyAsync(10, 3, TimeSpan.FromMilliseconds(10))));

        var seen = new List<int>();
        await merged.RunAsync((item, _) => { seen.Add(item); return ValueTask.CompletedTask; },
            TestContext.Current.CancellationToken);

        Assert.Equal(6, seen.Count);
        Assert.True(seen.IndexOf(12) < seen.IndexOf(3),
            $"the fast source should have finished before the slow one, but the order was {string.Join(", ", seen)}");
    }

    [Fact]
    public async Task AFailingSourceFailsTheMerge()
    {
        static async IAsyncEnumerable<int> BreaksAsync()
        {
            yield return 1;
            await Task.Yield();
            throw new InvalidOperationException("the source gave up");
        }

        var merged = StreamShapes.Merge(Of(9), ActorStream<int>.From(BreaksAsync()));

        // A merge that swallowed a source's failure would report a short stream as a complete one,
        // which is the kind of wrong that looks right in a log.
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await merged.RunAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnEmptyMergeIsAnEmptyStream()
    {
        var count = await StreamShapes.Merge<int>().RunAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task ASplitSendsEachItemToItsBranch()
    {
        var source = ActorStream<int>.From(Enumerable.Range(1, 9));
        var branches = StreamShapes.Split(source, i => i % 3, [0, 1, 2]);

        // Consumed concurrently, because they share one reader of the source: a branch nobody reads
        // fills its buffer and then stops the source, which stops the others.
        var collected = await Task.WhenAll(branches.Select(async pair =>
        {
            var items = new List<int>();
            await pair.Value.RunAsync((item, _) => { items.Add(item); return ValueTask.CompletedTask; });
            return (pair.Key, Items: items);
        }));

        var byKey = collected.ToDictionary(c => c.Key, c => c.Items);

        Assert.Equal([3, 6, 9], byKey[0]);
        Assert.Equal([1, 4, 7], byKey[1]);
        Assert.Equal([2, 5, 8], byKey[2]);
    }

    [Fact]
    public async Task AnItemWithNoBranchIsDropped()
    {
        var source = ActorStream<int>.From(Enumerable.Range(1, 9));

        // Only the multiples of three have somewhere to go. Failing the whole pipeline over the
        // others would be the worse of the two answers for what this is usually doing, which is
        // fanning traffic out by type or by tenant.
        var branches = StreamShapes.Split(source, i => i % 3, [0]);

        var items = new List<int>();
        await branches[0].RunAsync((item, _) => { items.Add(item); return ValueTask.CompletedTask; },
            TestContext.Current.CancellationToken);

        Assert.Equal([3, 6, 9], items);
    }

    [Fact]
    public void ASplitWithNoBranchesIsRefused()
    {
        Assert.Throws<ArgumentException>(() => StreamShapes.Split(Of(1), i => i, Array.Empty<int>()));
    }

    [Fact]
    public async Task AFanInDeliversEverySourceToOneActor()
    {
        await using var harness = new TestHarness();
        var system = await harness.LocalAsync();

        var target = ActorId.For<CounterActor>("fan-in");

        var delivered = await StreamShapes.FanInAsync(
            system,
            target,
            [ActorStream<object>.From(new object[] { new Add(1), new Add(2) }),
             ActorStream<object>.From(new object[] { new Add(3), new Add(4) })],
            TestContext.Current.CancellationToken);

        Assert.Equal(4, delivered);

        var total = await system.AskAsync<Total>(target, new GetTotal(), TimeSpan.FromSeconds(10));
        Assert.Equal(10, total.Value);
    }
}
