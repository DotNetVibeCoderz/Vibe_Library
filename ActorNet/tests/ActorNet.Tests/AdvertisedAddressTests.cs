// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace ActorNet.Tests;

/// <summary>
/// Binding and advertising are different questions, and conflating them is what made
/// <c>--host 0.0.0.0</c> fail in a way that looked like a network problem.
/// </summary>
public sealed class AdvertisedAddressTests
{
    private static ActorSystemOptions Clustered(Action<ActorSystemOptions> configure)
    {
        var options = new ActorSystemOptions { NodeId = "n1", Host = "127.0.0.1", Port = 9000 };
        options.Cluster.Enabled = true;
        configure(options);
        return options;
    }

    [Fact]
    public void TheBindHostIsAdvertisedWhenNothingElseIsSet()
    {
        var options = new ActorSystemOptions { Host = "10.0.1.5" };
        Assert.Equal("10.0.1.5", options.EffectiveAdvertisedHost);
    }

    [Fact]
    public void AnExplicitAdvertisedHostWins()
    {
        var options = new ActorSystemOptions { Host = "0.0.0.0", AdvertisedHost = "node-a" };
        Assert.Equal("node-a", options.EffectiveAdvertisedHost);
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    [InlineData("[::]")]
    [InlineData("*")]
    [InlineData("")]
    public void AdvertisingABindAddressIsRefused(string host)
    {
        // These all mean "every interface" to a listener and nothing at all to a dialler. Before
        // this check, a node bound to 0.0.0.0 advertised 0.0.0.0, peers dialled nothing, and the
        // node was marked unreachable while being perfectly healthy - a failure that reads as a
        // firewall problem and is not one.
        var options = Clustered(o => o.Host = host);

        var ex = Assert.Throws<ArgumentException>(options.Validate);
        Assert.Contains("peer can dial", ex.Message);
    }

    [Fact]
    public void BindingEveryInterfaceIsFineOnceAnAdvertisedHostIsGiven()
    {
        var options = Clustered(o =>
        {
            o.Host = "0.0.0.0";
            o.AdvertisedHost = "10.0.1.5";
        });

        options.Validate();
        Assert.Equal("10.0.1.5", options.EffectiveAdvertisedHost);
    }

    [Fact]
    public void ABindAddressIsOnlyRefusedForAClusteredNode()
    {
        // A standalone node advertises to nobody, so binding every interface is unremarkable.
        // Refusing it there would break a perfectly reasonable single-node deployment.
        var options = new ActorSystemOptions { Host = "0.0.0.0", Port = 9000 };
        options.Validate();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(70000)]
    public void AnOutOfRangeAdvertisedPortIsRefused(int port)
    {
        var options = Clustered(o => o.AdvertisedPort = port);
        Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
    }

    [Fact]
    public async Task ANodeAdvertisesItsBoundPortWhenItAskedForAnyPort()
    {
        await using var harness = new TestHarness();
        var system = await harness.NetworkedAsync("ephemeral", seeds: []);

        // Port 0 means "pick one", so the advertised port cannot be known until the listener is
        // up. Advertising the configured 0 would tell peers to dial port zero.
        var self = system.Cluster.Members.Single(m => m.NodeId == "ephemeral");
        Assert.Equal(system.BoundPort, self.Port);
        Assert.NotEqual(0, self.Port);
    }

    [Fact]
    public async Task APinnedAdvertisedPortSurvivesTheListenerBinding()
    {
        await using var harness = new TestHarness();

        // The published-container-port case: bind whatever, tell peers something else. Nothing
        // dials it in this test - the point is that the transport's bound port does not overwrite
        // what the operator pinned.
        var system = await harness.NetworkedAsync("published", seeds: [], configure: o => o.AdvertisedPort = 19000);

        var self = system.Cluster.Members.Single(m => m.NodeId == "published");
        Assert.Equal(19000, self.Port);
        Assert.NotEqual(19000, system.BoundPort);
    }

    [Fact]
    public async Task PeersDialTheAdvertisedAddressRatherThanTheBindAddress()
    {
        await using var harness = new TestHarness();

        // The seed binds every interface and advertises loopback. If the advertised address were
        // ignored, the joiner would receive 0.0.0.0 in the member table and never reach it.
        var seed = await harness.NetworkedAsync("seed-any", seeds: [], configure: o =>
        {
            o.Host = "0.0.0.0";
            o.AdvertisedHost = "127.0.0.1";
        });

        var joiner = await harness.NetworkedAsync("joiner", seeds: [$"127.0.0.1:{seed.BoundPort}"]);

        await TestHarness.AssertEventuallyAsync(
            () => seed.Cluster.Members.Count == 2 && joiner.Cluster.Members.Count == 2,
            "the cluster should converge when the seed advertises a routable address",
            TimeSpan.FromSeconds(15));

        Assert.Equal("127.0.0.1", joiner.Cluster.Members.Single(m => m.NodeId == "seed-any").Host);

        // And the traffic genuinely flows both ways over that address.
        var remote = Enumerable.Range(0, 500)
            .Select(i => ActorId.For<CounterActor>($"adv-{i}"))
            .First(id => joiner.Cluster.OwnerOf(id) == "seed-any");

        await joiner.TellAsync(remote, new Add(3));
        var total = await joiner.AskAsync<Total>(remote, new GetTotal(), TimeSpan.FromSeconds(20));
        Assert.Equal(3, total.Value);
    }
}
