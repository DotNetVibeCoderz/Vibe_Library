// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using BenchmarkDotNet.Attributes;

namespace ActorNet.Benchmarks;

/// <summary>
/// The in-process message path, measured end to end.
/// </summary>
/// <remarks>
/// <para>
/// Every benchmark here ends with a drain barrier - an ask per actor, which is ordered behind the
/// tells already in that mailbox, so its reply proves the queue ahead of it was handled. Without
/// that, a tell benchmark measures how fast this process can fill a channel: a large number that
/// says nothing about the runtime.
/// </para>
/// <para>
/// The barrier is inside the measured region on purpose. It costs one round trip per actor, which
/// at these batch sizes is a rounding error next to the work it is waiting for, and leaving it out
/// would be measuring the wrong thing to save it.
/// </para>
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 2, iterationCount: 5)]
public class MessagingBenchmarks
{
    private ActorSystem _system = null!;
    private ActorId[] _actors = null!;
    private readonly Tick _message = new();

    /// <summary>How many actors the messages are spread over.</summary>
    [Params(1, 8)]
    public int Actors { get; set; }

    /// <summary>Messages per invocation.</summary>
    [Params(10_000, 100_000)]
    public int Messages { get; set; }

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _system = new ActorSystem(new ActorSystemOptions
        {
            NodeId = "bench",
            EnableNetworking = false,
            IdleTimeout = TimeSpan.FromHours(1),
            SweepInterval = TimeSpan.FromMinutes(10),
        });

        _system.RegisterActor<CountingActor>();
        await _system.StartAsync();

        _actors = Enumerable.Range(0, Actors).Select(i => ActorId.For<CountingActor>($"bench-{i}")).ToArray();

        // Activate everything before measuring: the first message to an address pays for
        // construction and for the actor's activation hook.
        await DrainAsync();
    }

    [GlobalCleanup]
    public async Task CleanupAsync() => await _system.DisposeAsync();

    /// <summary>One sender per actor - the partitioned shape a real workload has.</summary>
    [Benchmark(Baseline = true, Description = "Tell, one sender per actor")]
    public async Task PartitionedTell()
    {
        var perActor = Messages / _actors.Length;

        await Task.WhenAll(_actors.Select(actor => Task.Run(async () =>
        {
            for (var i = 0; i < perActor; i++) await _system.TellAsync(actor, _message);
        })));

        await DrainAsync();
    }

    /// <summary>
    /// Every core sending to every actor. Measures the multi-writer path, where the mailbox's
    /// channel is genuinely contended rather than effectively single-writer.
    /// </summary>
    [Benchmark(Description = "Tell, all senders to all actors")]
    public async Task ContendedTell()
    {
        var senders = Environment.ProcessorCount;
        var perSender = Messages / senders;

        await Task.WhenAll(Enumerable.Range(0, senders).Select(sender => Task.Run(async () =>
        {
            for (var i = 0; i < perSender; i++)
                await _system.TellAsync(_actors[i % _actors.Length], _message);
        })));

        await DrainAsync();
    }

    /// <summary>
    /// Ask instead of tell, at a much lower count.
    /// </summary>
    /// <remarks>
    /// Deliberately 1/100th the message count: an ask is a round trip, so it is two to three
    /// orders of magnitude slower than a tell and running it at the same count would dominate the
    /// whole suite's runtime. Compare it against itself across changes, not against the tells.
    /// </remarks>
    [Benchmark(Description = "Ask, sequential round trips")]
    public async Task SequentialAsk()
    {
        var count = Math.Max(1, Messages / 100);
        for (var i = 0; i < count; i++)
            await _system.AskAsync<Counted>(_actors[i % _actors.Length], new GetCount(), TimeSpan.FromSeconds(30));
    }

    private async Task DrainAsync()
    {
        foreach (var actor in _actors)
            await _system.AskAsync<Counted>(actor, new GetCount(), TimeSpan.FromMinutes(2));
    }
}

/// <summary>The cheapest possible handler, so the numbers describe the runtime and not the actor.</summary>
public sealed record Tick;

public sealed record GetCount;

public sealed record Counted(long Value);

public sealed class CountingActor : ReceiveActor
{
    private long _count;

    public CountingActor()
    {
        On<Tick>(_ => _count++);
        On<GetCount>(async (_, ct) => await Context.ReplyAsync(new Counted(_count), ct));
    }
}
