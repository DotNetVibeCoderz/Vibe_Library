// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Collections.Concurrent;
using ActorNet.Cluster;
using ActorNet.Network;
using ActorNet.Serialization;
using Microsoft.Extensions.Logging.Abstractions;

namespace ActorNet.Tests;

/// <summary>
/// One seed name standing for however many nodes are behind it.
/// </summary>
/// <remarks>
/// A Kubernetes headless service resolves to one address per pod. Connecting to the name reaches
/// whichever record came back first, so a single seed entry used to mean a single pod - and if that
/// was the pod still starting, the join failed and the next beat tried the same coin flip again.
/// </remarks>
public sealed class SeedDiscoveryTests
{
    /// <summary>Records the addresses a join was actually sent to.</summary>
    private sealed class DialledTransport : ITransport
    {
        public ConcurrentQueue<string> Dialled { get; } = new();

        public int BoundPort => 1;
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask SendAsync(string nodeId, WireEnvelope frame, CancellationToken cancellationToken) => default;
        public ValueTask DisposeAsync() => default;

        public ValueTask SendToAddressAsync(string host, int port, WireEnvelope frame, CancellationToken cancellationToken)
        {
            Dialled.Enqueue($"{host}:{port}");
            return default;
        }
    }

    private static async Task<DialledTransport> JoinAsync(bool resolve, params string[] seeds)
    {
        var options = new ClusterOptions
        {
            Enabled = true,
            Seeds = seeds,
            ResolveSeedHostnames = resolve,
            HeartbeatInterval = TimeSpan.FromMinutes(5),
        };

        await using var membership = new ClusterMembership("self", "10.0.0.1", 9999, options, NullLogger.Instance);
        var transport = new DialledTransport();
        await membership.StartAsync(transport, TestContext.Current.CancellationToken);
        return transport;
    }

    [Fact]
    public async Task ANameIsExpandedToEveryAddressBehindIt()
    {
        // localhost is the one name every machine agrees on, and on a dual-stack host it stands for
        // both 127.0.0.1 and ::1 - which is the shape a headless service has, at a smaller scale.
        var expected = (await System.Net.Dns.GetHostAddressesAsync("localhost", TestContext.Current.CancellationToken))
            .Select(a => $"{a}:9000")
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var transport = await JoinAsync(resolve: true, "localhost:9000");

        Assert.Equal(expected.Order(StringComparer.Ordinal), transport.Dialled.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task AnAddressLiteralIsNotLookedUp()
    {
        var transport = await JoinAsync(resolve: true, "192.0.2.7:9000");

        // Asking a resolver about an address can only return what it was given, and the syscall is
        // on the path a node takes on every retry while it is alone.
        Assert.Equal(["192.0.2.7:9000"], transport.Dialled);
    }

    [Fact]
    public async Task ANameThatDoesNotResolveIsStillTried()
    {
        var transport = await JoinAsync(resolve: true, "no-such-host.invalid:9000");

        // Dropping it would turn a typo into a seed that silently stopped being contacted. Trying
        // it produces a connect error naming the seed, which is what somebody debugging needs.
        Assert.Equal(["no-such-host.invalid:9000"], transport.Dialled);
    }

    [Fact]
    public async Task ResolutionCanBeTurnedOff()
    {
        var transport = await JoinAsync(resolve: false, "localhost:9000");

        Assert.Equal(["localhost:9000"], transport.Dialled);
    }
}
