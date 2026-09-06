// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ActorNet.Cluster;
using ActorNet.Network;
using ActorNet.Serialization;
using Microsoft.Extensions.Logging.Abstractions;

namespace ActorNet.Tests;

/// <summary>
/// A seed that never answers, which is not the same as one that refuses.
/// </summary>
/// <remarks>
/// A closed port is refused in microseconds; a firewall that drops packets instead leaves the
/// connect sitting there until the operating system gives up, a minute or more later. Loopback only
/// ever produces the first kind, which is why this uses a transport that hangs on demand.
/// </remarks>
public sealed class StuckSeedTests
{
    /// <summary>A transport where one address swallows everything sent to it.</summary>
    private sealed class HangingTransport : ITransport
    {
        private readonly string _blackHole;
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<string> _delivered = [];
        private readonly Lock _gate = new();

        public HangingTransport(string blackHole) => _blackHole = blackHole;

        public int BoundPort => 1;

        public IReadOnlyList<string> Delivered
        {
            get { lock (_gate) return _delivered.ToArray(); }
        }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _released.TrySetResult();
            return Task.CompletedTask;
        }

        public ValueTask SendAsync(string nodeId, WireEnvelope frame, CancellationToken cancellationToken) => default;

        public async ValueTask SendToAddressAsync(string host, int port, WireEnvelope frame, CancellationToken cancellationToken)
        {
            if (host == _blackHole)
            {
                await _released.Task.WaitAsync(cancellationToken);
                return;
            }

            // A real dial with an already-cancelled token throws before it reaches the wire, so a
            // seed only counts as contacted if the attempt began while there was still time.
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate) _delivered.Add($"{host}:{port}");
        }

        public ValueTask DisposeAsync()
        {
            _released.TrySetResult();
            return default;
        }
    }

    private static ClusterMembership Membership(ClusterOptions options) =>
        new("self", "127.0.0.1", 1, options, NullLogger.Instance);

    [Fact]
    public async Task AStuckSeedDoesNotStarveTheSeedsListedAfterIt()
    {
        var transport = new HangingTransport("black-hole");
        var options = new ClusterOptions
        {
            Enabled = true,
            Seeds = ["black-hole:9000", "reachable:9001"],
            JoinTimeout = TimeSpan.FromSeconds(2),
        };

        await using var membership = Membership(options);
        await membership.StartAsync(transport, TestContext.Current.CancellationToken);

        // Contacted at once rather than in turn, so the seed after the black hole still gets its
        // join. In sequence it would have waited behind a connect nobody was ever going to answer.
        Assert.Equal(["reachable:9001"], transport.Delivered);
    }

    [Fact]
    public async Task StartingDoesNotWaitForASeedThatNeverAnswers()
    {
        var transport = new HangingTransport("black-hole");
        var options = new ClusterOptions
        {
            Enabled = true,
            Seeds = ["black-hole:9000"],
            JoinTimeout = TimeSpan.FromMilliseconds(500),
        };

        await using var membership = Membership(options);

        var started = System.Diagnostics.Stopwatch.StartNew();
        await membership.StartAsync(transport, TestContext.Current.CancellationToken);
        started.Stop();

        // A node that has not finished starting cannot serve the actors it already owns, so coming
        // up alone beats waiting: the handshake is retried on every beat anyway.
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(5), $"starting took {started.Elapsed}");
        Assert.Single(membership.Members);
    }

    [Fact]
    public void AJoinTimeoutOfZeroIsRefused()
    {
        var options = new ClusterOptions { Enabled = true, JoinTimeout = TimeSpan.Zero };

        var error = Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
        Assert.Equal("JoinTimeout", error.ParamName);
    }
}
