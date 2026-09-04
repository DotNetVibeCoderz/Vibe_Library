// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ActorNet.Persistence;

namespace ActorNet.Tests;

/// <summary>
/// Builds actor systems for tests and tears them down.
/// </summary>
/// <remarks>
/// Networking is off by default and ports are always zero when it is on, so tests never collide
/// with each other or with a developer's running node.
/// </remarks>
public sealed class TestHarness : IAsyncDisposable
{
    private readonly List<ActorSystem> _systems = [];

    /// <summary>Builds a single-process node with no socket.</summary>
    public async Task<ActorSystem> LocalAsync(Action<ActorSystemOptions>? configure = null)
    {
        var options = new ActorSystemOptions
        {
            NodeId = $"test-{Guid.NewGuid():N}"[..12],
            EnableNetworking = false,
            IdleTimeout = TimeSpan.FromMinutes(10),
            SweepInterval = TimeSpan.FromSeconds(30),
        };

        configure?.Invoke(options);
        var system = new ActorSystem(options);
        RegisterCommonTypes(system);
        await system.StartAsync();
        _systems.Add(system);
        return system;
    }

    /// <summary>Builds a networked node on an ephemeral port.</summary>
    public async Task<ActorSystem> NetworkedAsync(string nodeId, IEnumerable<string>? seeds = null, Action<ActorSystemOptions>? configure = null)
    {
        var options = new ActorSystemOptions
        {
            NodeId = nodeId,
            Host = "127.0.0.1",
            Port = 0,
            EnableNetworking = true,
            IdleTimeout = TimeSpan.FromMinutes(10),
            SweepInterval = TimeSpan.FromSeconds(30),
            StateStore = SharedStateStore,
        };

        if (seeds is not null)
        {
            options.Cluster.Enabled = true;
            options.Cluster.Seeds = seeds.ToList();
            options.Cluster.HeartbeatInterval = TimeSpan.FromMilliseconds(200);
            options.Cluster.UnreachableAfter = TimeSpan.FromSeconds(2);
            options.Cluster.DownAfter = TimeSpan.FromSeconds(5);
        }

        configure?.Invoke(options);
        var system = new ActorSystem(options);
        RegisterCommonTypes(system);
        await system.StartAsync();
        _systems.Add(system);
        return system;
    }

    /// <summary>
    /// One store shared by every node in a multi-node test, standing in for the database a real
    /// cluster would share. Without it, an actor that moves nodes would find nothing.
    /// </summary>
    public InMemoryStateStore SharedStateStore { get; } = new();

    private static void RegisterCommonTypes(ActorSystem system)
    {
        system.RegisterActor<CounterActor>()
              .RegisterActor<SilentActor>()
              .RegisterActor<ParentActor>()
              .RegisterActor<WalletActor>()
              .RegisterActor<LedgerActor>();

        system.RegisterMessage<Ping>().RegisterMessage<Pong>()
              .RegisterMessage<Add>().RegisterMessage<GetTotal>().RegisterMessage<Total>()
              .RegisterMessage<Credit>().RegisterMessage<GetBalance>().RegisterMessage<Balance>()
              .RegisterMessage<Deposit>().RegisterMessage<Withdraw>().RegisterMessage<Rejected>()
              .RegisterMessage<Boom>();
    }

    /// <summary>
    /// Waits for a condition, polling. Used instead of a fixed delay so a slow machine does not
    /// turn into a flaky test - and so a fast one does not pay for the worst case.
    /// </summary>
    public static async Task<bool> WaitForAsync(Func<bool> condition, TimeSpan? timeout = null, string? because = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            await Task.Delay(15);
        }

        return condition();
    }

    /// <summary>Waits for a condition and fails the test with <paramref name="because"/> if it never holds.</summary>
    public static async Task AssertEventuallyAsync(Func<bool> condition, string because, TimeSpan? timeout = null)
    {
        if (!await WaitForAsync(condition, timeout)) Assert.Fail(because);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var system in _systems)
        {
            try { await system.DisposeAsync(); }
            catch (Exception ex) { Console.WriteLine($"Teardown of {system.NodeId} threw: {ex.Message}"); }
        }
    }
}
