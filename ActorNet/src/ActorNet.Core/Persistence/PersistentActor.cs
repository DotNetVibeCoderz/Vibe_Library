// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace ActorNet.Persistence;

/// <summary>
/// A virtual actor whose state is loaded on activation and written back on deactivation.
/// </summary>
/// <remarks>
/// <para>
/// This is the Orleans-style grain state model. The actor works against
/// <see cref="State"/> as a plain field; the runtime handles the reload, and because deactivation
/// flushes, an actor that is swept for being idle - or handed to another node during a rebalance -
/// comes back with what it had.
/// </para>
/// <para>
/// Write-on-deactivate is the default because it turns N updates into one store write. It also
/// means a hard process kill loses everything since the last flush. Call
/// <see cref="SaveStateAsync"/> after any change you are not willing to lose, or override
/// <see cref="SaveEvery"/> to have the base class do it for you every N messages.
/// </para>
/// </remarks>
/// <typeparam name="TState">
/// The state shape. Must be JSON round-trippable and must have a parameterless constructor, since
/// the first activation has nothing to load.
/// </typeparam>
public abstract class PersistentActor<TState> : VirtualActor where TState : class, new()
{
    private IStateStore _store = null!;
    private long _version;
    private int _messagesSinceSave;

    /// <summary>The actor's state. Never null after activation.</summary>
    protected TState State { get; private set; } = new();

    /// <summary>True when nothing was stored under this key and <see cref="State"/> is a fresh instance.</summary>
    protected bool IsNew { get; private set; }

    /// <summary>
    /// Flush every this many messages, in addition to on deactivation. Zero - the default - flushes
    /// only on deactivation and on explicit <see cref="SaveStateAsync"/> calls.
    /// </summary>
    protected virtual int SaveEvery => 0;

    /// <summary>
    /// The store key. Defaults to the actor's own address, which gives every actor its own row and
    /// is what makes reactivation on another node find the same state.
    /// </summary>
    protected virtual string PersistenceKey => Context.Self.ToString();

    /// <summary>Reads the state before the first message. Override and call base first.</summary>
    protected override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _store = ResolveStore();

        var stored = await _store.ReadAsync<TState>(PersistenceKey, cancellationToken).ConfigureAwait(false);
        if (stored is { } found)
        {
            State = found.Value;
            _version = found.Version;
            IsNew = false;
        }
        else
        {
            State = new TState();
            _version = 0;
            IsNew = true;
        }

        await OnStateLoadedAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs after state is available, on every activation. The place to rebuild derived data.</summary>
    protected virtual Task OnStateLoadedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Writes the state now.</summary>
    protected async Task SaveStateAsync(CancellationToken cancellationToken = default)
    {
        _version = await _store.WriteAsync(PersistenceKey, State, _version, cancellationToken).ConfigureAwait(false);
        _messagesSinceSave = 0;
        IsNew = false;
    }

    /// <summary>Removes the stored state and resets <see cref="State"/> to a fresh instance.</summary>
    protected async Task ClearStateAsync(CancellationToken cancellationToken = default)
    {
        await _store.DeleteAsync(PersistenceKey, cancellationToken).ConfigureAwait(false);
        State = new TState();
        _version = 0;
        IsNew = true;
    }

    /// <summary>Runs the message, then applies the <see cref="SaveEvery"/> checkpoint policy.</summary>
    /// <remarks>
    /// The checkpoint deliberately only runs when the handler returned normally. A message that
    /// threw is about to reach the supervisor, and its half-applied changes are not something to
    /// write down.
    /// </remarks>
    protected override async Task ReceiveCoreAsync(object message, CancellationToken cancellationToken)
    {
        await base.ReceiveCoreAsync(message, cancellationToken).ConfigureAwait(false);

        if (SaveEvery <= 0) return;
        if (++_messagesSinceSave < SaveEvery) return;
        await SaveStateAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Flushes on the way out, unless the actor was stopped by a supervisor.</summary>
    protected override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        // A supervision stop means the actor failed. Writing back state that a failed message may
        // have left half-updated is how a transient bug becomes a permanent one.
        if (reason == DeactivationReason.Supervision)
        {
            Context.Logger.LogSupervisionSkippedFlush(PersistenceKey);
            return;
        }

        await SaveStateAsync(cancellationToken).ConfigureAwait(false);
    }

    private IStateStore ResolveStore() => Context.System is ActorSystem system
        ? system.Options.StateStore
        : throw new ActorNetException("PersistentActor requires the built-in ActorSystem, which owns the state store.");
}

/// <summary>Log messages the persistence base classes emit.</summary>
internal static partial class PersistenceLog
{
    [Microsoft.Extensions.Logging.LoggerMessage(
        EventId = 1001,
        Level = Microsoft.Extensions.Logging.LogLevel.Warning,
        Message = "Not flushing state for {Key}: the actor was stopped by its supervisor after a failure, and its state may be half-updated.")]
    public static partial void LogSupervisionSkippedFlush(this Microsoft.Extensions.Logging.ILogger logger, string key);
}
