// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using ActorNet.Metrics;
using Microsoft.Extensions.Logging;

namespace ActorNet.Runtime;

/// <summary>
/// One activated actor: its instance, its mailbox, the loop that drains it, and everything the
/// supervisor needs to restart or stop it.
/// </summary>
/// <remarks>
/// <para>
/// The loop is the single thread of control for an actor. Activation runs on it, every message
/// runs on it, supervision decisions about <em>this</em> actor run on it, and deactivation runs on
/// it. Nothing outside reaches in and mutates the instance - other components ask by posting a
/// <see cref="SystemCommand"/>.
/// </para>
/// <para>
/// That is what closes the activation race a naive implementation has: the loop awaits
/// <see cref="IActor.OnActivateAsync"/> before it reads the first message, so a message can queue
/// during activation but can never be handled by an actor whose state has not finished loading.
/// </para>
/// </remarks>
internal sealed class ActorCell
{
    private const int StateRunning = 1;
    private const int StateStopping = 2;
    private const int StateStopped = 3;

    private readonly ActorSystem _system;
    private readonly ActorRegistration _registration;
    private readonly IMailbox _mailbox;
    private readonly CancellationTokenSource _cts;
    private readonly ConcurrentDictionary<ActorId, byte> _children = new();
    private readonly ActorContext _context;
    private readonly ILogger _logger;
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private IActor _actor;
    private MetricsCollector.ActorMetrics? _metrics;
    private int _state = StateRunning;
    private long _lastActivityTicks = Stopwatch.GetTimestamp();
    private int _restartCount;
    private int _restartsInWindow;

    /// <summary>
    /// Who asked to be told when this actor stops, in <c>Type/Key</c> form.
    /// </summary>
    /// <remarks>
    /// Only ever touched from the mailbox loop, like everything else about an activation, so it
    /// needs no lock. Watches are per-activation on purpose: an actor that idles out and comes back
    /// is a new activation with nothing to report, which is the same reason idling is not a
    /// termination.
    /// </remarks>
    private readonly HashSet<string> _watchers = new(StringComparer.Ordinal);
    private long _windowStartTicks = Stopwatch.GetTimestamp();
    private bool _deactivateAfterCurrentMessage;

    public ActorId Id { get; }
    public ActorId Parent { get; }
    public SupervisorStrategy Strategy { get; }
    public IReadOnlyCollection<ActorId> Children => (IReadOnlyCollection<ActorId>)_children.Keys;
    public int RestartCount => Volatile.Read(ref _restartCount);
    public int MailboxDepth => _mailbox.Count;
    public bool IsAlive => Volatile.Read(ref _state) == StateRunning;

    /// <summary>How long since this actor last handled anything - what the idle sweeper reads.</summary>
    public TimeSpan Idle => Stopwatch.GetElapsedTime(Volatile.Read(ref _lastActivityTicks));

    /// <summary>Completes once the loop has exited and deactivation has run.</summary>
    public Task Stopped => _stopped.Task;

    public ActorCell(ActorSystem system, ActorId id, ActorRegistration registration, ActorId parent, SupervisorStrategy strategy)
    {
        _system = system;
        Id = id;
        Parent = parent;
        Strategy = strategy;
        _registration = registration;
        _mailbox = new ChannelMailbox(system.Options.MailboxCapacity);
        _cts = new CancellationTokenSource();
        _logger = system.LoggerFactory.CreateLogger("ActorNet." + registration.TypeName);
        _actor = system.CreateInstance(registration.ClrType);
        _context = new ActorContext(this);
    }

    /// <summary>Starts the loop. Called once, by the directory, before the cell is published.</summary>
    public void Start() => _ = Task.Run(RunAsync, CancellationToken.None);

    /// <summary>
    /// Enqueues a message. False means this cell is on its way out and the caller should look the
    /// address up again, which will activate a fresh one.
    /// </summary>
    public ValueTask<bool> PostAsync(Envelope envelope, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _state) != StateRunning) return ValueTask.FromResult(false);
        Volatile.Write(ref _lastActivityTicks, Stopwatch.GetTimestamp());
        return _mailbox.PostAsync(envelope, cancellationToken);
    }

    /// <summary>
    /// Enqueues without ever suspending. Null means "could not answer synchronously" - the mailbox
    /// is bounded and full - and the caller should fall back to <see cref="PostAsync"/>.
    /// </summary>
    /// <remarks>
    /// This is the hot path. With the default unbounded mailbox a post always succeeds
    /// immediately, so taking it here lets a tell complete without allocating an async state
    /// machine at all.
    /// <para>
    /// Do not expect this to show up in the throughput benchmark. When senders outrun handlers,
    /// the run's allocation is dominated by the channel's own segment storage for the millions of
    /// buffered envelopes - measured at 160-180 B/msg either way, with more run-to-run variance
    /// than the state machine costs. The saving is real but it is visible in a steady-state
    /// workload, not in a queue-filling one.
    /// </para>
    /// </remarks>
    public bool? TryPostFast(in Envelope envelope)
    {
        if (Volatile.Read(ref _state) != StateRunning) return false;
        Volatile.Write(ref _lastActivityTicks, Stopwatch.GetTimestamp());
        return _mailbox.TryPost(envelope) ? true : null;
    }

    /// <summary>
    /// Posts a lifecycle instruction, bypassing the state check that <see cref="PostAsync"/> does.
    /// A stop has to be deliverable to a cell that is already stopping.
    /// </summary>
    public void PostSystem(SystemCommand command) => _mailbox.TryPost(Envelope.Create(Id, command));

    /// <summary>
    /// Closes the mailbox and asks the loop to wind down after draining. Returns false when the
    /// cell was already stopping, so the caller knows not to count the deactivation twice.
    /// </summary>
    public bool RequestStop(DeactivationReason reason)
    {
        if (Interlocked.CompareExchange(ref _state, StateStopping, StateRunning) != StateRunning) return false;
        PostSystem(new StopCommand(reason));
        _mailbox.Complete();
        return true;
    }

    /// <summary>
    /// Idle-stops the cell, but only if it is genuinely idle: a non-empty mailbox means work
    /// arrived between the sweeper's check and this call.
    /// </summary>
    public bool TryStopIfIdle(TimeSpan idleTimeout)
    {
        if (!IsAlive || _mailbox.Count > 0 || Idle < idleTimeout) return false;
        return RequestStop(DeactivationReason.Idle);
    }

    /// <summary>Asks the supervising strategy's restart budget whether another restart is allowed.</summary>
    private bool RestartBudgetAllows()
    {
        if (Stopwatch.GetElapsedTime(_windowStartTicks) > Strategy.Window)
        {
            _windowStartTicks = Stopwatch.GetTimestamp();
            _restartsInWindow = 0;
        }

        return ++_restartsInWindow <= Strategy.MaxRestarts;
    }

    private async Task RunAsync()
    {
        var token = _cts.Token;
        var reason = DeactivationReason.Requested;

        try
        {
            if (!await ActivateAsync(token).ConfigureAwait(false)) return;

            while (await _mailbox.WaitToReadAsync(token).ConfigureAwait(false))
            {
                while (_mailbox.TryRead(out var envelope))
                {
                    if (envelope.Message is StopCommand stop)
                    {
                        reason = stop.Reason;
                        return;
                    }

                    if (envelope.Message is RestartCommand restart)
                    {
                        if (!await RestartAsync(restart.Cause, token).ConfigureAwait(false))
                        {
                            reason = DeactivationReason.Supervision;
                            return;
                        }

                        continue;
                    }

                    await HandleAsync(envelope, token).ConfigureAwait(false);

                    if (_deactivateAfterCurrentMessage)
                    {
                        reason = DeactivationReason.Requested;
                        return;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            reason = DeactivationReason.Shutdown;
        }
        catch (Exception ex)
        {
            // The loop itself failed, not a message handler. There is nothing left to supervise.
            _logger.LogError(ex, "Mailbox loop for {ActorId} terminated unexpectedly.", Id);
            reason = DeactivationReason.Supervision;
        }
        finally
        {
            await ShutdownAsync(reason).ConfigureAwait(false);
        }
    }

    private async Task<bool> ActivateAsync(CancellationToken token)
    {
        _metrics = _system.MetricsCollector.RegisterActor(Id, _registration.TypeName, () => _mailbox.Count);

        try
        {
            await _actor.OnActivateAsync(_context, token).ConfigureAwait(false);

            ActorNetDiagnostics.Activations.Add(1,
                new KeyValuePair<string, object?>("actor.type", _registration.TypeName));

            _logger.LogDebug("Activated {ActorId}.", Id);
            return true;
        }
        catch (Exception ex)
        {
            // Activation failures skip the message-level supervision path: there is no message to
            // resume past, and restarting an actor that cannot load its state is exactly the
            // busy-loop the restart budget exists to prevent.
            _logger.LogError(ex, "Activation of {ActorId} failed; the actor will not be started.", Id);
            _system.MetricsCollector.RecordFailed(_metrics);
            _system.ReportActivationFailure(Id, ex);
            return false;
        }
    }

    private async Task HandleAsync(Envelope envelope, CancellationToken token)
    {
        var queueLatency = Stopwatch.GetTimestamp() - envelope.EnqueuedTimestamp;
        var started = Stopwatch.GetTimestamp();

        var messageType = envelope.Message.GetType().Name;

        // Null unless something is listening, which is what makes a span affordable per message.
        using var activity = ActorNetDiagnostics.StartReceive(Id, messageType, envelope.TraceParent, envelope.TraceState);

        // Watch bookkeeping is the runtime's, not the actor's: handled here so an actor cannot be
        // written to forget it, and so watching works on an actor that has no handler for anything
        // but its own protocol.
        if (envelope.Message is Watch watch)
        {
            _watchers.Add(watch.Watcher);
            return;
        }

        if (envelope.Message is Unwatch unwatch)
        {
            _watchers.Remove(unwatch.Watcher);
            return;
        }

        // Getting here is the whole message. Reaching this method means the cell exists and its
        // actor has been activated, which is what the sender wanted; passing it on would only make
        // every actor write a handler to ignore it.
        if (envelope.Message is Warm) return;

        if (envelope.Message is Inspect)
        {
            await AnswerInspectAsync(envelope, token).ConfigureAwait(false);
            return;
        }

        _context.BeginMessage(envelope);
        try
        {
            await _actor.ReceiveAsync(_context, envelope.Message, token).ConfigureAwait(false);

            var elapsed = Stopwatch.GetTimestamp() - started;
            _system.MetricsCollector.RecordProcessed(_metrics!, queueLatency, elapsed);

            ActorNetDiagnostics.MessagesProcessed.Add(1,
                new KeyValuePair<string, object?>("actor.type", _registration.TypeName));
            ActorNetDiagnostics.ProcessingDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                new KeyValuePair<string, object?>("actor.type", _registration.TypeName));
            ActorNetDiagnostics.QueueLatency.Record(
                Stopwatch.GetElapsedTime(envelope.EnqueuedTimestamp, started).TotalMilliseconds,
                new KeyValuePair<string, object?>("actor.type", _registration.TypeName));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _system.MetricsCollector.RecordFailed(_metrics);

            ActorNetDiagnostics.MessagesFailed.Add(1,
                new KeyValuePair<string, object?>("actor.type", _registration.TypeName),
                new KeyValuePair<string, object?>("exception.type", ex.GetType().Name));
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);

            _logger.LogError(ex, "{ActorId} failed handling {MessageType}.", Id, messageType);

            // An ask that will never be answered is worse than one answered with the failure: the
            // caller would sit on a timeout it has no way to diagnose.
            if (envelope.CorrelationId is not null)
                await _system.FailAskAsync(envelope, ex).ConfigureAwait(false);

            await ApplySupervisionAsync(ex, token).ConfigureAwait(false);
        }
        finally
        {
            _context.EndMessage();
            Volatile.Write(ref _lastActivityTicks, Stopwatch.GetTimestamp());
        }
    }

    /// <summary>Runs the supervising strategy's decision about a failure in this actor.</summary>
    private async Task ApplySupervisionAsync(Exception cause, CancellationToken token)
    {
        var directive = Strategy.Decide(cause);

        // AllForOne only means anything when there are siblings, which requires a parent. At the
        // root every actor would count as a sibling, and restarting the whole node because one
        // actor threw is never what was meant.
        if (directive is Directive.Restart or Directive.Stop &&
            Strategy.Scope == SupervisionScope.AllForOne && !Parent.IsEmpty)
        {
            _system.ApplyToSiblings(this, directive, cause);
        }

        switch (directive)
        {
            case Directive.Resume:
                _logger.LogWarning("Resuming {ActorId} after {Exception}; the message was dropped.", Id, cause.GetType().Name);
                break;

            case Directive.Restart when RestartBudgetAllows():
                if (!await RestartAsync(cause, token).ConfigureAwait(false)) RequestStop(DeactivationReason.Supervision);
                break;

            case Directive.Restart:
                _logger.LogError(cause,
                    "{ActorId} exceeded its restart budget ({MaxRestarts} in {Window}); stopping instead of restarting.",
                    Id, Strategy.MaxRestarts, Strategy.Window);
                RequestStop(DeactivationReason.Supervision);
                break;

            case Directive.Stop:
                RequestStop(DeactivationReason.Supervision);
                break;

            case Directive.Escalate:
                await _system.EscalateAsync(this, cause).ConfigureAwait(false);
                break;
        }
    }

    /// <summary>
    /// Answers <see cref="Inspect"/> with whatever this actor is holding.
    /// </summary>
    /// <remarks>
    /// Runs on the mailbox loop like every other message, so it never reads state a handler is
    /// halfway through writing. An actor that is wedged does not answer, which is a true thing to
    /// learn about it.
    /// </remarks>
    private async Task AnswerInspectAsync(Envelope envelope, CancellationToken token)
    {
        string? state = null;

        try
        {
            var subject = _actor is IInspectable inspectable ? inspectable.Inspect() : StateOf(_actor);
            if (subject is not null) state = JsonSerializer.Serialize(subject, InspectionJson);
        }
        catch (Exception ex)
        {
            // A state that will not serialize is a fact about the actor, not a reason to fail the
            // request: an inspector that throws teaches nothing about what it was inspecting.
            state = JsonSerializer.Serialize($"<not serializable: {ex.GetType().Name}: {ex.Message}>", InspectionJson);
        }

        var snapshot = _metrics?.ToSnapshot(Id);
        var answer = new Inspected(
            Id.ToString(),
            _actor.GetType().Name,
            state,
            snapshot?.MessagesProcessed ?? 0,
            snapshot?.ActivatedAt ?? DateTimeOffset.UtcNow);

        _context.BeginMessage(envelope);
        try { await _context.ReplyAsync(answer, token).ConfigureAwait(false); }
        finally { _context.EndMessage(); }
    }

    /// <summary>
    /// The <c>State</c> of a persistent or event-sourced actor, or the fields the actor declares.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Those two base classes keep the interesting part behind a protected <c>State</c>, and
    /// serializing the actor around it would report the plumbing rather than the data.
    /// </para>
    /// <para>
    /// An actor without one keeps everything in private fields, which no serializer will touch on
    /// its own - so they are read here by reflection. Only the fields the actor's own class
    /// declares: walking into the framework's base classes would report a mailbox and a handler
    /// table to somebody asking what an account is holding.
    /// </para>
    /// </remarks>
    private static object? StateOf(IActor actor)
    {
        const System.Reflection.BindingFlags Members =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.FlattenHierarchy;

        var property = actor.GetType().GetProperty("State", Members);
        if (property is not null && property.GetIndexParameters().Length == 0) return property.GetValue(actor);

        var described = new Dictionary<string, object?>(StringComparer.Ordinal);
        var framework = typeof(IActor).Assembly;

        for (var type = actor.GetType(); type is not null && type.Assembly != framework; type = type.BaseType)
        {
            foreach (var field in type.GetFields(System.Reflection.BindingFlags.Instance |
                                                 System.Reflection.BindingFlags.NonPublic |
                                                 System.Reflection.BindingFlags.Public |
                                                 System.Reflection.BindingFlags.DeclaredOnly))
            {
                // A property's backing field is reported under the property's name, because that is
                // the name whoever wrote the actor would recognise.
                var name = field.Name;
                if (name.StartsWith('<') && name.Contains(">k__BackingField", StringComparison.Ordinal))
                    name = name[1..name.IndexOf('>', StringComparison.Ordinal)];

                described.TryAdd(name, field.GetValue(actor));
            }
        }

        return described.Count == 0 ? null : described;
    }

    /// <summary>
    /// Lenient on purpose: this serializes types nobody wrote for a serializer.
    /// </summary>
    private static readonly JsonSerializerOptions InspectionJson = new()
    {
        WriteIndented = false,
        IncludeFields = true,
        ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Replaces the actor instance in place. The address, mailbox and children survive.</summary>
    private async Task<bool> RestartAsync(Exception cause, CancellationToken token)
    {
        // Waiting happens on this actor's own loop, so its mailbox keeps filling and nothing else
        // on the node is held up. That is the intended shape: the actor is unavailable while it is
        // restarting, and it was unavailable anyway.
        var backoff = Strategy.BackoffFor(Volatile.Read(ref _restartsInWindow));
        if (backoff > TimeSpan.Zero)
        {
            _logger.LogWarning("Waiting {Backoff} before restarting {ActorId}; it has failed {Count} times in this window.",
                backoff, Id, Volatile.Read(ref _restartsInWindow));

            try { await Task.Delay(backoff, token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return false; }
        }

        try
        {
            await _actor.OnRestartAsync(_context, cause, token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OnRestartAsync for {ActorId} threw; continuing with the restart anyway.", Id);
        }

        Interlocked.Increment(ref _restartCount);
        _system.MetricsCollector.RecordRestart(_metrics);

        ActorNetDiagnostics.Restarts.Add(1,
            new KeyValuePair<string, object?>("actor.type", _registration.TypeName),
            new KeyValuePair<string, object?>("exception.type", cause.GetType().Name));

        try
        {
            _actor = _system.CreateInstance(_registration.ClrType);
            await _actor.OnActivateAsync(_context, token).ConfigureAwait(false);
            _logger.LogWarning("Restarted {ActorId} after {Exception} (restart #{Count}).", Id, cause.GetType().Name, RestartCount);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{ActorId} could not be restarted; stopping it.", Id);
            return false;
        }
    }

    private async Task ShutdownAsync(DeactivationReason reason)
    {
        Interlocked.Exchange(ref _state, StateStopping);
        _mailbox.Complete();

        // Children go first, so that a parent's OnDeactivateAsync can still reach them if it
        // needs to drain something through them.
        foreach (var child in _children.Keys)
            await _system.StopCellAsync(child, reason).ConfigureAwait(false);

        try
        {
            using var deactivation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await _actor.OnDeactivateAsync(_context, reason, deactivation.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OnDeactivateAsync for {ActorId} threw; state may not have been flushed.", Id);
        }

        _system.MetricsCollector.UnregisterActor(Id);
        _system.RemoveCell(Id, this);

        if (!Parent.IsEmpty) _system.DetachChild(Parent, Id);

        ActorNetDiagnostics.Deactivations.Add(1,
            new KeyValuePair<string, object?>("actor.type", _registration.TypeName),
            new KeyValuePair<string, object?>("reason", reason.ToString()));

        Interlocked.Exchange(ref _state, StateStopped);
        _cts.Dispose();
        _logger.LogDebug("Deactivated {ActorId} ({Reason}).", Id, reason);

        await NotifyWatchersAsync(reason).ConfigureAwait(false);
        _stopped.TrySetResult();
    }

    /// <summary>
    /// Tells anyone watching that this actor has stopped, when the reason is one worth reporting.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="DeactivationReason.Idle"/>, <see cref="DeactivationReason.Rebalanced"/> and
    /// <see cref="DeactivationReason.Shutdown"/> are routine for a virtual actor: the address stays
    /// valid and the next message brings it back somewhere. Reporting those as terminations would
    /// train every watcher to ignore them, which would make the two that matter useless too.
    /// </para>
    /// <para>
    /// Sent after the cell is unregistered, so a watcher that responds by messaging the address
    /// gets a fresh activation rather than a queue on a mailbox that is already closed.
    /// </para>
    /// </remarks>
    private async Task NotifyWatchersAsync(DeactivationReason reason)
    {
        if (_watchers.Count == 0) return;
        if (reason is not (DeactivationReason.Supervision or DeactivationReason.Requested)) return;

        var notice = new Terminated(Id.ToString(), reason);
        foreach (var watcher in _watchers)
        {
            if (!ActorId.TryParse(watcher, out var target)) continue;

            try
            {
                await _system.TellAsync(target, notice).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // A watcher that has itself gone away is the ordinary case, not a fault worth
                // failing a deactivation over.
                _logger.LogDebug(ex, "Could not tell {Watcher} that {ActorId} stopped.", watcher, Id);
            }
        }

        _watchers.Clear();
    }

    internal void AttachChild(ActorId child) => _children[child] = 0;

    internal void DetachChild(ActorId child) => _children.TryRemove(child, out _);

    /// <summary>Forces the loop to unwind without draining. Used only when a graceful stop timed out.</summary>
    internal void Abort()
    {
        try { _cts.Cancel(); }
        catch (ObjectDisposedException) { /* already finished shutting down */ }
    }

    /// <summary>
    /// The per-actor context. One instance per cell, re-stamped with the current envelope before
    /// each message - safe because only the cell's own loop ever touches it.
    /// </summary>
    private sealed class ActorContext(ActorCell cell) : IActorContext
    {
        private Envelope _current;

        public ActorId Self => cell.Id;
        public ActorId Sender => _current.Sender;
        public ActorId Parent => cell.Parent;
        public IActorSystem System => cell._system;
        public ILogger Logger => cell._logger;
        public int RestartCount => cell.RestartCount;
        public IReadOnlyCollection<ActorId> Children => cell.Children;

        internal void BeginMessage(in Envelope envelope) => _current = envelope;

        internal void EndMessage() => _current = default;

        public ValueTask TellAsync(ActorId target, object message, CancellationToken cancellationToken = default) =>
            cell._system.TellAsync(target, message, cell.Id, cancellationToken);

        public ValueTask<bool> ReplyAsync(object message, CancellationToken cancellationToken = default) =>
            cell._system.ReplyAsync(_current, message, cancellationToken);

        public IActorRef SpawnChild<TActor>(string key, SupervisorStrategy? strategy = null) where TActor : IActor
        {
            var child = ActorId.For<TActor>(key);
            cell._system.SpawnChild(cell, child, strategy);
            return cell._system.ActorOf(child);
        }

        public IDisposable ScheduleTell(TimeSpan delay, object message, TimeSpan? repeatEvery = null)
        {
            var self = cell.Id;
            var system = cell._system;
            var period = repeatEvery ?? Timeout.InfiniteTimeSpan;

            // Fire-and-forget on purpose: a timer callback has nobody to report to, and a delivery
            // failure is already visible as a message that never arrived.
            return new Timer(_ => _ = system.TellAsync(self, message, self).AsTask(), null, delay, period);
        }

        public ValueTask WatchAsync(ActorId target, CancellationToken cancellationToken = default) =>
            cell._system.TellAsync(target, new Watch(cell.Id.ToString()), cancellationToken: cancellationToken);

        public ValueTask UnwatchAsync(ActorId target, CancellationToken cancellationToken = default) =>
            cell._system.TellAsync(target, new Unwatch(cell.Id.ToString()), cancellationToken: cancellationToken);

        public void DeactivateOnIdle() => cell._deactivateAfterCurrentMessage = true;
    }
}

/// <summary>What the runtime knows about an actor type.</summary>
internal sealed record ActorRegistration(string TypeName, Type ClrType, SupervisorStrategy Strategy);
