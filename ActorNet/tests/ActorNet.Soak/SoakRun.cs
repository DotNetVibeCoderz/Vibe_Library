// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ActorNet.Persistence;

namespace ActorNet.Soak;

/// <summary>A message that adds to a running total.</summary>
public sealed record Bump(int By);

/// <summary>Asks an actor what it is holding, which is also the barrier that proves it kept up.</summary>
public sealed record WhatTotal;

/// <summary>The answer.</summary>
public sealed record TotalIs(long Value);

/// <summary>Makes the actor throw, so supervision is exercised rather than assumed.</summary>
public sealed record Trip;

/// <summary>
/// A persistent actor that is cheap to run and expensive to get wrong.
/// </summary>
/// <remarks>
/// Persistent rather than plain, because the interesting leaks are in the paths that touch a store:
/// an activation that loads, a deactivation that flushes, and the recovery in between.
/// </remarks>
public sealed class SoakActor : PersistentActor<SoakState>
{
    protected override async Task ReceiveAsync(object message, CancellationToken cancellationToken)
    {
        switch (message)
        {
            case Bump bump:
                State.Total += bump.By;
                State.Handled++;
                break;

            case WhatTotal:
                await Context.ReplyAsync(new TotalIs(State.Total), cancellationToken);
                break;

            case Trip:
                throw new InvalidOperationException("tripped on purpose");
        }
    }
}

/// <summary>What a soak actor is holding.</summary>
public sealed class SoakState
{
    public long Total { get; set; }

    public long Handled { get; set; }
}

/// <summary>One measurement of a running soak.</summary>
public sealed record Sample(
    TimeSpan Elapsed,
    long Sent,
    long Handled,
    int LiveActors,
    long ManagedBytes,
    long DeadLetters,
    long Failures)
{
    /// <summary>Messages a second since the previous sample.</summary>
    /// <remarks>
    /// The rate over the interval rather than since the start. A cumulative average hides a slowdown
    /// behind however fast the run began, which is exactly the trend a soak exists to notice.
    /// </remarks>
    public double Rate { get; init; }

    public override string ToString() =>
        $"{Elapsed:hh\\:mm\\:ss}  sent {Sent,12:N0}  handled {Handled,12:N0}  " +
        $"actors {LiveActors,6:N0}  heap {ManagedBytes / 1024 / 1024,5:N0} MiB  " +
        $"dead {DeadLetters,4:N0}  failures {Failures,4:N0}";
}

/// <summary>
/// Two nodes, a working set of actors, and traffic that never stops.
/// </summary>
/// <remarks>
/// <para>
/// Two nodes rather than one, because the remote path is where the connections, the pending-ask
/// table and the per-peer queues live - the three things most likely to grow without bound.
/// </para>
/// <para>
/// The working set is deliberately larger than what an idle sweep will keep, so activations and
/// deactivations churn for the whole run. A soak over a fixed set of always-hot actors would never
/// touch recovery, which is where a leak is most likely and least visible.
/// </para>
/// </remarks>
public sealed class SoakRun : IAsyncDisposable
{
    private readonly ActorSystem _first;
    private readonly ActorSystem _second;
    private readonly ActorId[] _actors;
    private readonly Random _random = new(20260907);

    private long _sent;
    private long _failures;

    private SoakRun(ActorSystem first, ActorSystem second, ActorId[] actors)
    {
        _first = first;
        _second = second;
        _actors = actors;
    }

    public static async Task<SoakRun> StartAsync(int actorCount)
    {
        // One shared store, because an actor that moves between nodes has to find its state - and
        // the whole point of churning activations is that they do move.
        var store = new InMemoryStateStore();

        var first = Build("soak-a", store, seeds: []);
        await first.StartAsync();

        var second = Build("soak-b", store, seeds: [$"127.0.0.1:{first.BoundPort}"]);
        await second.StartAsync();

        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);
        while (first.Cluster.Members.Count < 2 || second.Cluster.Members.Count < 2)
        {
            if (DateTimeOffset.UtcNow > deadline) throw new ActorNetException("The soak cluster did not converge.");
            await Task.Delay(100);
        }

        var actors = Enumerable.Range(0, actorCount)
            .Select(i => ActorId.For<SoakActor>($"soak-{i}"))
            .ToArray();

        return new SoakRun(first, second, actors);
    }

    private static ActorSystem Build(string nodeId, IStateStore store, string[] seeds)
    {
        var options = new ActorSystemOptions
        {
            NodeId = nodeId,
            Host = "127.0.0.1",
            Port = 0,
            StateStore = store,

            // Short enough that the working set churns rather than staying hot for the whole run.
            IdleTimeout = TimeSpan.FromSeconds(5),
            SweepInterval = TimeSpan.FromSeconds(2),
        };

        options.Cluster.Enabled = true;
        options.Cluster.Seeds = [.. seeds];
        options.Cluster.HeartbeatInterval = TimeSpan.FromMilliseconds(500);

        var system = new ActorSystem(options);
        system.RegisterActor<SoakActor>();
        system.RegisterMessage<Bump>();
        system.RegisterMessage<WhatTotal>();
        system.RegisterMessage<TotalIs>();
        system.RegisterMessage<Trip>();
        return system;
    }

    /// <summary>Sends traffic for a while, from both nodes, to actors on both nodes.</summary>
    public async Task WorkAsync(TimeSpan slice)
    {
        var until = DateTimeOffset.UtcNow + slice;

        while (DateTimeOffset.UtcNow < until)
        {
            var from = _random.Next(2) == 0 ? _first : _second;
            var target = _actors[_random.Next(_actors.Length)];

            try
            {
                // Mostly tells, because that is what a mailbox is for, with an occasional ask as a
                // barrier - a loop of tells alone measures how fast a channel fills.
                if (_random.Next(50) == 0)
                {
                    await from.AskAsync<TotalIs>(target, new WhatTotal(), TimeSpan.FromSeconds(10));
                }
                else if (_random.Next(2000) == 0)
                {
                    // A failure now and then, so supervision and restart are part of the soak
                    // rather than something only the unit tests ever see.
                    await from.TellAsync(target, new Trip());
                }
                else
                {
                    await from.TellAsync(target, new Bump(1));
                }

                Interlocked.Increment(ref _sent);
            }
            catch (Exception)
            {
                // Counted rather than thrown. A soak that stops at the first hiccup measures the
                // first hiccup; the question here is whether they accumulate.
                Interlocked.Increment(ref _failures);
            }
        }
    }

    private Sample? _previous;

    /// <summary>One measurement, taken from both nodes.</summary>
    public Sample Sample(TimeSpan elapsed)
    {
        // Without the per-actor list: a soak takes this every few seconds, and building a row per
        // activation each time would make the measurement the biggest thing in the run.
        var here = _first.Metrics.Snapshot(includeActors: false);
        var there = _second.Metrics.Snapshot(includeActors: false);

        var sent = Interlocked.Read(ref _sent);
        var since = elapsed - (_previous?.Elapsed ?? TimeSpan.Zero);

        var sample = new Sample(
            elapsed,
            sent,
            here.MessagesProcessed + there.MessagesProcessed,
            _first.LocalActors.Count + _second.LocalActors.Count,
            GC.GetTotalMemory(forceFullCollection: false),
            here.DeadLetters + there.DeadLetters,
            Interlocked.Read(ref _failures))
        {
            Rate = since <= TimeSpan.Zero ? 0 : (sent - (_previous?.Sent ?? 0)) / since.TotalSeconds,
        };

        _previous = sample;
        return sample;
    }

    public async ValueTask DisposeAsync()
    {
        await _second.DisposeAsync();
        await _first.DisposeAsync();
    }
}
