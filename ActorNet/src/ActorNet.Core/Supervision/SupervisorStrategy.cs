// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace ActorNet;

/// <summary>What a supervisor decides to do about a child that threw.</summary>
public enum Directive
{
    /// <summary>Drop the offending message and carry on with the same instance and the same state.</summary>
    Resume,

    /// <summary>Throw the instance away and build a fresh one. The mailbox and the address survive.</summary>
    Restart,

    /// <summary>Deactivate the actor. The next message to that address activates a new instance.</summary>
    Stop,

    /// <summary>Treat it as the parent's failure and let the grandparent decide.</summary>
    Escalate,
}

/// <summary>How widely a directive is applied.</summary>
public enum SupervisionScope
{
    /// <summary>Only the actor that failed is affected. The Akka default, and ours.</summary>
    OneForOne,

    /// <summary>
    /// Every sibling under the same supervisor is affected too. Right when siblings share
    /// invariants - a set of shard workers that must be restarted as a group, for instance.
    /// </summary>
    AllForOne,
}

/// <summary>
/// A supervisor's policy: what to do about a failure, how widely, and how many times before
/// giving up.
/// </summary>
/// <remarks>
/// <para>
/// Strategies are attached per actor <em>type</em> at registration, and inherited by children
/// spawned through <see cref="IActorContext.SpawnChild{TActor}"/> unless the child overrides it.
/// Root actors - anything addressed directly rather than spawned - are supervised by the system's
/// <see cref="ActorSystemOptions.DefaultSupervisorStrategy"/>.
/// </para>
/// <para>
/// The restart budget is what stops a poison message from spinning forever: exceed
/// <see cref="MaxRestarts"/> within <see cref="Window"/> and the directive is downgraded to
/// <see cref="Directive.Stop"/>, so the actor goes away instead of burning a core.
/// </para>
/// </remarks>
public abstract class SupervisorStrategy
{
    /// <summary>How widely the directive applies.</summary>
    public abstract SupervisionScope Scope { get; }

    /// <summary>How many restarts are tolerated inside <see cref="Window"/> before stopping instead.</summary>
    public int MaxRestarts { get; init; } = 10;

    /// <summary>The sliding window the restart budget is counted over.</summary>
    public TimeSpan Window { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How long the second restart in a window waits, doubling for each one after it.
    /// </summary>
    /// <remarks>
    /// The first restart is immediate, because the common failure is a one-off - a timeout, a bad
    /// payload - and making every actor pay for the rare crash loop would be the wrong trade. It is
    /// the second failure in quick succession that says the cause has not gone away, and restarting
    /// into it as fast as the mailbox allows just moves the busy-loop from the actor to whatever it
    /// depends on.
    /// </remarks>
    public TimeSpan MinBackoff { get; init; } = TimeSpan.FromMilliseconds(100);

    /// <summary>A ceiling on the doubling.</summary>
    /// <remarks>
    /// Deliberately well short of <see cref="Window"/>: if the waits added up to more than the
    /// window, it would keep resetting and a permanently broken actor would restart forever instead
    /// of hitting <see cref="MaxRestarts"/> and stopping.
    /// </remarks>
    public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How much to spread the wait, as a fraction either side of it. Zero is exact.
    /// </summary>
    /// <remarks>
    /// A database going down fails every actor that touches it within the same millisecond. Without
    /// jitter they would all come back in lockstep and hit it together, which is how a brief outage
    /// becomes a long one.
    /// </remarks>
    public double BackoffJitter { get; init; } = 0.2;

    /// <summary>
    /// How long to wait before the <paramref name="restartsInWindow"/>-th restart of this window.
    /// </summary>
    public TimeSpan BackoffFor(int restartsInWindow)
    {
        if (restartsInWindow <= 1 || MinBackoff <= TimeSpan.Zero) return TimeSpan.Zero;

        // Doubling from the second restart, computed in doubles so an exponent that runs away
        // saturates rather than overflowing a long of ticks.
        var doublings = Math.Min(restartsInWindow - 2, 32);
        var millis = Math.Min(MinBackoff.TotalMilliseconds * Math.Pow(2, doublings), MaxBackoff.TotalMilliseconds);

        if (BackoffJitter > 0)
        {
            var spread = millis * BackoffJitter;
            millis += Random.Shared.NextDouble() * spread * 2 - spread;
        }

        return TimeSpan.FromMilliseconds(Math.Max(0, millis));
    }

    /// <summary>Chooses what to do about <paramref name="exception"/>.</summary>
    public abstract Directive Decide(Exception exception);

    /// <summary>
    /// Restart on anything unexpected, but stop on a wiring bug.
    /// </summary>
    /// <remarks>
    /// The split is deliberate: a missing registration or a bad address will fail identically
    /// every time, so restarting is a busy-loop. Everything else - a transient database error, a
    /// bad payload - gets a fresh instance, which is the whole point of an actor supervisor.
    /// </remarks>
    public static SupervisorStrategy Default { get; } = new OneForOneStrategy(static ex => ex switch
    {
        ActorTypeNotRegisteredException => Directive.Stop,
        UnknownMessageTypeException => Directive.Stop,
        FormatException => Directive.Stop,
        _ => Directive.Restart,
    });

    /// <summary>Never restarts; a failure just stops the actor.</summary>
    public static SupervisorStrategy StopOnFailure { get; } = new OneForOneStrategy(static _ => Directive.Stop);

    /// <summary>Logs and skips the bad message, keeping state. Suitable for idempotent read models.</summary>
    public static SupervisorStrategy ResumeOnFailure { get; } = new OneForOneStrategy(static _ => Directive.Resume);
}

/// <summary>Applies its decision to the failing actor alone.</summary>
public sealed class OneForOneStrategy(Func<Exception, Directive> decider) : SupervisorStrategy
{
    private readonly Func<Exception, Directive> _decider = decider ?? throw new ArgumentNullException(nameof(decider));

    /// <inheritdoc />
    public override SupervisionScope Scope => SupervisionScope.OneForOne;

    /// <inheritdoc />
    public override Directive Decide(Exception exception) => _decider(exception);
}

/// <summary>Applies its decision to the failing actor and every one of its siblings.</summary>
public sealed class AllForOneStrategy(Func<Exception, Directive> decider) : SupervisorStrategy
{
    private readonly Func<Exception, Directive> _decider = decider ?? throw new ArgumentNullException(nameof(decider));

    /// <inheritdoc />
    public override SupervisionScope Scope => SupervisionScope.AllForOne;

    /// <inheritdoc />
    public override Directive Decide(Exception exception) => _decider(exception);
}
