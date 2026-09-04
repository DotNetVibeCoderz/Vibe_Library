// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Diagnostics;
using ActorNet.Demo.Banking;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ActorNet.Cli.Commands;

public sealed class BenchSettings : CommandSettings
{
    [CommandOption("-n|--messages <COUNT>")]
    [System.ComponentModel.Description("Messages to send. Default 1000000.")]
    public int Messages { get; init; } = 1_000_000;

    [CommandOption("-a|--actors <COUNT>")]
    [System.ComponentModel.Description("How many actors to spread the messages over. Default 8.")]
    public int Actors { get; init; } = 8;

    [CommandOption("-s|--senders <COUNT>")]
    [System.ComponentModel.Description("Concurrent sending tasks. Default: one per core.")]
    public int Senders { get; init; } = Environment.ProcessorCount;

    [CommandOption("--warmup <COUNT>")]
    [System.ComponentModel.Description("Messages to send before timing, to let the JIT settle. Default 50000.")]
    public int Warmup { get; init; } = 50_000;
}

/// <summary>
/// Measures local message throughput.
/// </summary>
/// <remarks>
/// <para>
/// The number reported is <em>drained</em> throughput, not dispatch throughput. A tell completes
/// as soon as the message is accepted into a mailbox, so timing a loop of tells measures how fast
/// this process can fill a queue - a number that is large, meaningless, and the one a naive
/// benchmark reports. Here the run is not finished until every actor has actually handled every
/// message, which is checked by asking each one afterwards.
/// </para>
/// <para>
/// It is still a micro-benchmark of one machine's in-process path. It says nothing about network
/// hops, persistence or contention with real work in the handlers.
/// </para>
/// </remarks>
public sealed class BenchCommand : AsyncCommand<BenchSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, BenchSettings settings, CancellationToken cancellationToken)
    {
        Theme.Banner();
        Theme.Rule("Throughput benchmark");

        var messages = Math.Max(1, settings.Messages);
        var actorCount = Math.Max(1, settings.Actors);
        var senders = Math.Max(1, settings.Senders);

        await using var system = await NodeFactory.StartAsync(
            new NodeSettings { Port = 0, IdleTimeoutSeconds = 3600 },
            networking: false);

        var actors = Enumerable.Range(0, actorCount)
            .Select(i => ActorId.For<CountingActor>($"bench-{i}"))
            .ToArray();

        system.RegisterActor<CountingActor>();

        AnsiConsole.Write(Theme.Facts("Configuration")
            .Fact("Messages", $"{messages:N0}")
            .Fact("Actors", $"{actorCount:N0}")
            .Fact("Sender tasks", $"{senders:N0}")
            .Fact("Cores", $"{Environment.ProcessorCount}")
            .Fact("Runtime", Environment.Version.ToString()));
        AnsiConsole.WriteLine();

        // Warm-up is not decoration: the first thousand messages pay for JIT, for the first
        // activation of each actor, and for the channel's initial allocations.
        await SendAsync(system, actors, Math.Max(0, settings.Warmup), senders);
        await DrainAsync(system, actors);

        var before = GC.GetTotalAllocatedBytes(precise: false);
        var collections = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));

        // One run, two readings off the same clock: where the sending loop finished, and where
        // the last actor finished handling. The gap between them is the point of the exercise.
        var watch = Stopwatch.StartNew();
        await SendAsync(system, actors, messages, senders);
        var dispatch = watch.Elapsed;
        await DrainAsync(system, actors);
        var elapsed = watch.Elapsed;
        watch.Stop();

        var allocated = GC.GetTotalAllocatedBytes(precise: false) - before;

        var table = Theme.Facts("Results")
            .Fact("Dispatch only", $"{messages / dispatch.TotalSeconds:N0} msg/s  [{Theme.Muted}](what a naive benchmark reports)[/]")
            .Fact("Drained", $"[{Theme.Accent}]{messages / elapsed.TotalSeconds:N0} msg/s[/]  [{Theme.Muted}](every message handled)[/]")
            .Fact("Wall clock", $"{elapsed.TotalSeconds:N2}s")
            .Fact("Per message", $"{elapsed.TotalMilliseconds * 1_000_000 / messages:N0} ns")
            .Fact("Allocated", $"{allocated / (1024.0 * 1024.0):N1} MiB  [{Theme.Muted}]({(double)allocated / messages:N0} B/msg)[/]")
            .Fact("GC", $"gen0 {GC.CollectionCount(0) - collections.Item1}, gen1 {GC.CollectionCount(1) - collections.Item2}, gen2 {GC.CollectionCount(2) - collections.Item3}");

        AnsiConsole.WriteLine();
        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        var snapshot = system.Metrics.Snapshot(includeActors: false);
        Theme.Info($"Runtime counters agree: {snapshot.MessagesProcessed:N0} processed, {snapshot.MessagesFailed:N0} failed.");
        AnsiConsole.MarkupLine(
            $"[{Theme.Muted}]In-process only: no network hop, no persistence, and the handler does nothing but increment.[/]");

        return 0;
    }

    private static async Task SendAsync(ActorSystem system, ActorId[] actors, int messages, int senders)
    {
        if (messages == 0) return;

        var perSender = messages / senders;
        var remainder = messages % senders;
        var message = new Tick();

        await Task.WhenAll(Enumerable.Range(0, senders).Select(sender => Task.Run(async () =>
        {
            var count = perSender + (sender < remainder ? 1 : 0);

            // Each sender sticks to one actor where possible, which is the realistic shape: a
            // partitioned workload, not every thread contending on one mailbox.
            var target = actors[sender % actors.Length];
            for (var i = 0; i < count; i++) await system.TellAsync(target, message);
        })));
    }

    /// <summary>
    /// Blocks until every actor has handled everything sent to it.
    /// </summary>
    /// <remarks>
    /// The barrier is an ask per actor. An ask is ordered behind the tells already in that
    /// mailbox, so its reply arriving proves the queue ahead of it has been drained - which is
    /// the guarantee a timer or a sleep cannot give.
    /// </remarks>
    private static async Task DrainAsync(ActorSystem system, ActorId[] actors)
    {
        foreach (var actor in actors)
            await system.AskAsync<Counted>(actor, new GetCount(), TimeSpan.FromMinutes(5));
    }
}

/// <summary>The cheapest possible handler, so the benchmark measures the runtime and not the actor.</summary>
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
