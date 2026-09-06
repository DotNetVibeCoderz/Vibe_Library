// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace ActorNet.Tests;

/// <summary>
/// An actor addressed through an interface the compiler checks.
/// </summary>
/// <remarks>
/// <c>AskAsync&lt;Balance&gt;(id, new GetBalance())</c> is three things that have to agree and
/// nothing that checks they do. These exercise the generated form end to end - including across a
/// node boundary, where the generated records have to serialize like any other message.
/// </remarks>
public sealed class ActorInterfaceTests
{
    [Fact]
    public async Task AProxyCallReachesTheActorAndComesBack()
    {
        await using var harness = new TestHarness();
        var system = await harness.LocalAsync();
        VaultProtocol.Register<VaultActor>(system);

        var vault = VaultProxy.Of<VaultActor>(system, "vault-1");

        await vault.DepositAsync(120m, "opening");
        await vault.DepositAsync(30m, "top-up");

        Assert.Equal(150m, await vault.GetBalanceAsync());
        Assert.Equal(2, await vault.CountEntriesAsync());
    }

    [Fact]
    public async Task AVoidCallIsATellAndDoesNotWaitForTheHandler()
    {
        await using var harness = new TestHarness();
        var system = await harness.LocalAsync();
        VaultProtocol.Register<VaultActor>(system);

        var vault = VaultProxy.Of<VaultActor>(system, "vault-tell");

        // A method returning Task promises the message was accepted, which is what TellAsync
        // completes on. The balance is only guaranteed after something that waits for a reply.
        await vault.DepositAsync(10m, "one");
        Assert.Equal(10m, await vault.GetBalanceAsync());
    }

    [Fact]
    public async Task TheGeneratedMessagesCrossANodeBoundary()
    {
        await using var harness = new TestHarness();

        var here = await harness.NetworkedAsync("proxy-a", seeds: []);
        var there = await harness.NetworkedAsync("proxy-b", seeds: [$"127.0.0.1:{here.BoundPort}"]);

        // The caller registers the messages only; the host registers the actor as well. Getting
        // that split wrong is the failure this arrangement exists to make obvious.
        VaultProtocol.Register(here);
        VaultProtocol.Register<VaultActor>(there);

        await TestHarness.AssertEventuallyAsync(
            () => here.Cluster.Members.Count == 2 && there.Cluster.Members.Count == 2,
            "the cluster should converge", TimeSpan.FromSeconds(15));

        await TestHarness.AssertRingsAgreeAsync(here, there);

        var remote = Enumerable.Range(0, 2000)
            .Select(i => $"vault-{i}")
            .First(key => here.Cluster.OwnerOf(ActorId.For<VaultActor>(key)) == "proxy-b");

        var vault = VaultProxy.Of<VaultActor>(here, remote);

        await vault.DepositAsync(75m, "remote");

        Assert.Equal(75m, await vault.GetBalanceAsync());
    }

    [Fact]
    public async Task ACancellationTokenBoundsTheWaitWithoutTravelling()
    {
        await using var harness = new TestHarness();
        var system = await harness.LocalAsync();
        VaultProtocol.Register<VaultActor>(system);

        var vault = VaultProxy.Of<VaultActor>(system, "vault-token");
        using var cts = new CancellationTokenSource();

        // The token is a call-site concern: it bounds the wait for a reply and is not a field of
        // the message, because it would mean nothing once the request is on another machine.
        Assert.Equal(0m, await vault.GetBalanceAsync(cts.Token));
    }

    [Fact]
    public void TheGeneratedMessagesCarryStableAliases()
    {
        // The alias is the wire contract, and a cross-language client addresses it by name. It is
        // derived from the interface and method names, so a rename is a protocol change - which is
        // what the Alias property on the attribute exists to prevent when that matters.
        var request = typeof(VaultDepositRequest)
            .GetCustomAttributes(typeof(Serialization.ActorMessageAttribute), false)
            .Cast<Serialization.ActorMessageAttribute>()
            .Single();

        Assert.Equal("vault.deposit", request.Alias);

        var reply = typeof(VaultGetBalanceReply)
            .GetCustomAttributes(typeof(Serialization.ActorMessageAttribute), false)
            .Cast<Serialization.ActorMessageAttribute>()
            .Single();

        Assert.Equal("vault.getBalance.reply", reply.Alias);
    }
}

/// <summary>The protocol under test. Everything else in this file is generated from it.</summary>
[ActorInterface]
public interface IVault
{
    Task DepositAsync(decimal amount, string reference);

    Task<decimal> GetBalanceAsync(CancellationToken cancellationToken = default);

    Task<int> CountEntriesAsync();
}

public sealed class VaultActor : VaultActorBase
{
    private decimal _balance;
    private int _entries;

    public override Task DepositAsync(decimal amount, string reference)
    {
        _balance += amount;
        _entries++;
        return Task.CompletedTask;
    }

    public override Task<decimal> GetBalanceAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_balance);

    public override Task<int> CountEntriesAsync() => Task.FromResult(_entries);
}
