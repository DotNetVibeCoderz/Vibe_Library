// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ActorNet.Persistence;

namespace ActorNet.Tests;

public sealed class PersistenceTests
{
    [Fact]
    public async Task StateSurvivesDeactivationAndReactivation()
    {
        await using var harness = new TestHarness();
        var system = await harness.LocalAsync();

        var id = ActorId.For<WalletActor>($"wallet-{Guid.NewGuid():N}");
        var wallet = system.ActorOf(id);

        await wallet.TellAsync(new Credit(120m));
        await wallet.TellAsync(new Credit(30m));
        Assert.Equal(150m, (await wallet.AskAsync<Balance>(new GetBalance())).Amount);

        // Deactivation flushes; reactivation reloads. This is the whole point of a virtual actor -
        // the address never changed and the caller never knew it went away.
        await system.DeactivateAsync(id);
        await TestHarness.AssertEventuallyAsync(() => !system.LocalActors.Contains(id), "the actor should have deactivated");

        var reloaded = await wallet.AskAsync<Balance>(new GetBalance());
        Assert.Equal(150m, reloaded.Amount);
        Assert.Equal(2, reloaded.Operations);
    }

    [Fact]
    public async Task AFailedActorDoesNotFlushItsHalfUpdatedState()
    {
        await using var harness = new TestHarness();
        var system = await harness.LocalAsync();
        system.RegisterActor<HalfWayActor>(SupervisorStrategy.StopOnFailure);

        var id = ActorId.For<HalfWayActor>($"halfway-{Guid.NewGuid():N}");
        var actor = system.ActorOf(id);

        await actor.TellAsync(new Credit(100m));
        await system.DeactivateAsync(id);
        Assert.Equal(100m, (await actor.AskAsync<Balance>(new GetBalance())).Amount);

        // This message mutates state and then throws. Writing that back would turn a transient
        // bug into permanently wrong data.
        await actor.TellAsync(new Boom("after mutating"));
        await TestHarness.AssertEventuallyAsync(() => !system.LocalActors.Contains(id), "the supervisor should have stopped it");

        Assert.Equal(100m, (await actor.AskAsync<Balance>(new GetBalance())).Amount);
    }

    [Fact]
    public async Task TheStoreRefusesAWriteBuiltOnAStaleRead()
    {
        var store = new InMemoryStateStore();
        var version = await store.WriteAsync("k", new WalletState { Balance = 1 });

        await store.WriteAsync("k", new WalletState { Balance = 2 }, version);

        var ex = await Assert.ThrowsAsync<StateConcurrencyException>(
            async () => await store.WriteAsync("k", new WalletState { Balance = 3 }, version));

        Assert.Equal(version, ex.ExpectedVersion);
        Assert.Equal(version + 1, ex.ActualVersion);
    }

    [Fact]
    public async Task AFileStoreRoundTripsStateAcrossStoreInstances()
    {
        var directory = Path.Combine(Path.GetTempPath(), "actornet-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var written = new WalletState { Balance = 42.5m, Operations = 3 };
            await new FileStateStore(directory).WriteAsync("BankAccountActor/user-1", written);

            // A second instance over the same directory stands in for a process restart.
            var read = await new FileStateStore(directory).ReadAsync<WalletState>("BankAccountActor/user-1");

            Assert.NotNull(read);
            Assert.Equal(42.5m, read!.Value.Value.Balance);
            Assert.Equal(3, read.Value.Value.Operations);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AFileStoreKeepsKeysWithSeparatorsInsideItsRoot()
    {
        var directory = Path.Combine(Path.GetTempPath(), "actornet-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileStateStore(directory);

            // Keys are user data. A key that looks like a relative path must not be able to write
            // outside the store's directory.
            await store.WriteAsync("../../escaped", new WalletState { Balance = 1 });
            await store.WriteAsync("Device/plant-3/line-2", new WalletState { Balance = 2 });

            Assert.Equal(2, Directory.GetFiles(directory).Length);
            Assert.Empty(Directory.GetDirectories(directory));
            Assert.Equal(2m, (await store.ReadAsync<WalletState>("Device/plant-3/line-2"))!.Value.Value.Balance);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}

/// <summary>Mutates its state and then throws, to prove a failed actor does not flush.</summary>
public sealed class HalfWayActor : PersistentActor<WalletState>
{
    protected override Task ReceiveAsync(object message, CancellationToken cancellationToken)
    {
        switch (message)
        {
            case Credit credit:
                State.Balance += credit.Amount;
                return Task.CompletedTask;

            case Boom boom:
                State.Balance += 999m;
                throw new InvalidOperationException(boom.Message);

            case GetBalance:
                return Context.ReplyAsync(new Balance(State.Balance, State.Operations), cancellationToken).AsTask();

            default:
                return Task.CompletedTask;
        }
    }
}
