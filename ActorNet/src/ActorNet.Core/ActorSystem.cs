// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Collections.Concurrent;
using ActorNet.Cluster;
using ActorNet.Metrics;
using ActorNet.Network;
using ActorNet.Runtime;
using ActorNet.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ActorNet;

/// <summary>
/// A node. Owns the actor directory, the mailbox scheduler, the supervisor, the transport and the
/// cluster view.
/// </summary>
/// <remarks>
/// <para>
/// <b>Location transparency.</b> <see cref="TellAsync"/> asks the hash ring who owns the key and
/// then either enqueues locally or hands the message to the transport. Callers never branch on
/// where an actor lives, and an actor that moves because the cluster changed shape keeps the same
/// address.
/// </para>
/// <para>
/// <b>The local path does not serialize.</b> An in-process send puts the message object itself
/// into the target's mailbox. Serialization exists for the wire and nowhere else, which is what
/// makes an in-process tell cost a channel write rather than a JSON round trip.
/// </para>
/// <para>
/// <b>Deactivation has a small overlap window.</b> When a cell stops, it closes its mailbox and
/// drains what it already accepted while a new send creates a fresh cell. Every message is still
/// handled exactly once by exactly one instance, but a message accepted just before the stop can
/// be handled after one that was sent later. Actors that care should persist through
/// <see cref="Persistence.PersistentActor{TState}"/>, which reloads on the new instance.
/// </para>
/// </remarks>
public sealed class ActorSystem : IActorSystem
{
    private readonly ConcurrentDictionary<ActorId, Lazy<ActorCell>> _cells = new();
    private readonly ConcurrentDictionary<string, ActorRegistration> _registrations = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, PendingAsk> _pendingAsks = new(StringComparer.Ordinal);
    private readonly IServiceProvider? _services;
    private readonly ILogger<ActorSystem> _logger;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ClusterMembership _cluster;

    private ITransport? _transport;
    private Task? _sweeper;
    private int _started;

    /// <summary>Options this node was built with. Mutating them after start has no effect.</summary>
    public ActorSystemOptions Options { get; }

    /// <summary>Serializer used for anything crossing a node boundary.</summary>
    public IMessageSerializer Serializer { get; }

    /// <inheritdoc />
    public string NodeId => Options.NodeId;

    /// <inheritdoc />
    public IMetricsCollector Metrics => MetricsCollector;

    /// <inheritdoc />
    public IClusterView Cluster => _cluster;

    /// <inheritdoc />
    public IReadOnlyCollection<ActorId> LocalActors => _cells.Keys.ToArray();

    /// <summary>The port the transport bound. Meaningful only after <see cref="StartAsync"/>.</summary>
    public int BoundPort => _transport?.BoundPort ?? Options.Port;

    internal MetricsCollector MetricsCollector { get; }

    internal ILoggerFactory LoggerFactory { get; }

    /// <summary>Builds a node.</summary>
    /// <param name="options">Node configuration. Validated here, so a bad setting fails at construction.</param>
    /// <param name="loggerFactory">Where the runtime logs. Defaults to no logging.</param>
    /// <param name="services">
    /// Used to construct actors, so they can take dependencies through their constructors. Without
    /// one, actors must have a parameterless constructor.
    /// </param>
    /// <param name="serializer">Overrides the default JSON serializer and its type allow-list.</param>
    public ActorSystem(
        ActorSystemOptions? options = null,
        ILoggerFactory? loggerFactory = null,
        IServiceProvider? services = null,
        IMessageSerializer? serializer = null)
    {
        Options = options ?? new ActorSystemOptions();
        Options.Validate();

        LoggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = LoggerFactory.CreateLogger<ActorSystem>();
        _services = services;
        Serializer = serializer ?? new JsonMessageSerializer();
        MetricsCollector = new MetricsCollector(Options.NodeId);
        _cluster = new ClusterMembership(
            Options.NodeId,
            Options.EffectiveAdvertisedHost,
            Options.AdvertisedPort ?? Options.Port,
            Options.Cluster,
            LoggerFactory.CreateLogger<ClusterMembership>());
        _cluster.MembershipChanged += OnMembershipChanged;
    }

    /// <inheritdoc />
    public IActorSystem RegisterActor<TActor>(SupervisorStrategy? strategy = null) where TActor : IActor
    {
        var type = typeof(TActor);
        if (type.IsAbstract || type.IsInterface)
            throw new ArgumentException($"{type.Name} is abstract and cannot be activated.", nameof(TActor));

        _registrations[type.Name] = new ActorRegistration(type.Name, type, strategy ?? Options.DefaultSupervisorStrategy);
        _logger.LogDebug("Registered actor type {ActorType}.", type.Name);
        return this;
    }

    /// <inheritdoc />
    public IActorSystem RegisterMessage<TMessage>(string? alias = null)
    {
        Serializer.Types.Register<TMessage>(alias);
        return this;
    }

    /// <summary>Registers every type in an assembly carrying <see cref="ActorMessageAttribute"/>.</summary>
    public IActorSystem RegisterMessagesFromAssembly(System.Reflection.Assembly assembly)
    {
        var count = Serializer.Types.RegisterFromAssembly(assembly);
        _logger.LogDebug("Registered {Count} message type(s) from {Assembly}.", count, assembly.GetName().Name);
        return this;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _started, 1) == 1) return;

        if (Options.EnableNetworking)
        {
            _transport = new TcpTransport(Options.Host, Options.Port, OnFrameAsync, _cluster.Resolve, LoggerFactory.CreateLogger<TcpTransport>());
            await _transport.StartAsync(cancellationToken).ConfigureAwait(false);
            // The bound port unless one was pinned - a published container port is not the port
            // the listener actually opened.
            _cluster.SetAdvertisedPort(Options.AdvertisedPort ?? _transport.BoundPort);
            await _cluster.StartAsync(_transport, cancellationToken).ConfigureAwait(false);
        }
        else if (Options.Cluster.Enabled)
        {
            throw new ActorNetException("Clustering requires networking; set EnableNetworking to true or turn clustering off.");
        }

        _sweeper = Task.Run(() => SweepLoopAsync(_shutdown.Token), CancellationToken.None);
        _logger.LogInformation("Node {NodeId} started on {Host}:{Port}.", NodeId, Options.Host, BoundPort);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _started, 0) == 0) return;

        _logger.LogInformation("Node {NodeId} stopping; deactivating {Count} actor(s).", NodeId, _cells.Count);

        await _cluster.LeaveAsync(cancellationToken).ConfigureAwait(false);
        await _shutdown.CancelAsync().ConfigureAwait(false);

        // Deactivation runs on each actor's own loop, so stop them all and then wait once, rather
        // than serializing a node with thousands of actors through one shutdown at a time.
        // Only cells that were actually built: touching a Lazy that has not run yet would
        // activate an actor purely in order to deactivate it.
        var cells = _cells.Values.Where(l => l.IsValueCreated).Select(l => l.Value).ToArray();
        foreach (var cell in cells) cell.RequestStop(DeactivationReason.Shutdown);

        var drained = Task.WhenAll(cells.Select(c => c.Stopped));
        if (await Task.WhenAny(drained, Task.Delay(TimeSpan.FromSeconds(15), CancellationToken.None)).ConfigureAwait(false) != drained)
        {
            _logger.LogWarning("Some actors did not deactivate within 15s; aborting their loops.");
            foreach (var cell in cells) cell.Abort();
        }

        foreach (var pending in _pendingAsks.Values) pending.Fail(new ActorNetException("The node stopped before a reply arrived."));
        _pendingAsks.Clear();

        await _cluster.DisposeAsync().ConfigureAwait(false);
        if (_transport is not null) await _transport.DisposeAsync().ConfigureAwait(false);

        _logger.LogInformation("Node {NodeId} stopped.", NodeId);
    }

    /// <inheritdoc />
    public IActorRef ActorOf<TActor>(string key) where TActor : IActor => ActorOf(ActorId.For<TActor>(key));

    /// <inheritdoc />
    public IActorRef ActorOf(ActorId id) => new ActorRef(this, id);

    /// <inheritdoc />
    public ValueTask TellAsync(ActorId target, object message, ActorId sender = default, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (target.IsEmpty) throw new ArgumentException("Target actor id is empty.", nameof(target));

        return _cluster.IsLocal(target)
            ? DispatchLocalAsync(Envelope.Create(target, message, sender), cancellationToken)
            : SendRemoteAsync(_cluster.OwnerOf(target), WireKind.Message, target, message, sender, null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TResponse> AskAsync<TResponse>(ActorId target, object message, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var window = timeout ?? Options.DefaultAskTimeout;
        var correlationId = Guid.NewGuid().ToString("N");
        var pending = new PendingAsk();
        _pendingAsks[correlationId] = pending;

        MetricsCollector.RecordAskIssued();

        using var timeoutSource = new CancellationTokenSource(window);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        await using var registration = linked.Token.Register(static state =>
        {
            var (asks, id) = ((ConcurrentDictionary<string, PendingAsk>, string))state!;
            if (asks.TryRemove(id, out var waiting)) waiting.Cancel();
        }, (_pendingAsks, correlationId)).ConfigureAwait(false);

        try
        {
            if (_cluster.IsLocal(target))
                await DispatchLocalAsync(Envelope.Create(target, message, default, correlationId), cancellationToken).ConfigureAwait(false);
            else
                await SendRemoteAsync(_cluster.OwnerOf(target), WireKind.AskRequest, target, message, default, correlationId, cancellationToken).ConfigureAwait(false);

            var reply = await pending.Task.ConfigureAwait(false);
            if (reply is TResponse typed) return typed;

            throw new AskReplyTypeMismatchException(target, typeof(TResponse), reply.GetType());
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            MetricsCollector.RecordAskTimedOut();
            throw new AskTimeoutException(target, window);
        }
        finally
        {
            _pendingAsks.TryRemove(correlationId, out _);
        }
    }

    /// <inheritdoc />
    public async Task DeactivateAsync(ActorId id, CancellationToken cancellationToken = default) =>
        await StopCellAsync(id, DeactivationReason.Requested).ConfigureAwait(false);

    /// <summary>
    /// Puts an envelope into a local actor's mailbox, activating it if needed.
    /// </summary>
    /// <remarks>
    /// The retry loop is the deactivation race: a cell can start stopping between the directory
    /// lookup and the post. Removing that exact instance before retrying guarantees the next
    /// iteration builds a fresh one, so the loop terminates rather than spinning on a corpse.
    /// </remarks>
    internal ValueTask DispatchLocalAsync(Envelope envelope, CancellationToken cancellationToken)
    {
        // Counted here rather than at the send, so that "dispatched" means "accepted for handling
        // on this node". Counting every send would include messages this node forwarded to another
        // owner, which it will never process - and InFlight, being dispatched minus processed,
        // would then climb forever on any node that routes remotely. Inbound remote frames land
        // here too, which is correct: they are in flight locally.
        MetricsCollector.RecordDispatched();

        // Fast path: an unbounded mailbox on a live actor always accepts immediately, so the
        // common case returns without ever building an async state machine.
        var cell = GetOrCreateCell(envelope.Target, ActorId.None, null);
        switch (cell.TryPostFast(envelope))
        {
            case true:
                return ValueTask.CompletedTask;
            case null:
                return DispatchWithBackpressureAsync(cell, envelope, cancellationToken);
            default:
                RemoveCell(envelope.Target, cell);
                return DispatchAfterDeactivationAsync(envelope, cancellationToken);
        }
    }

    /// <summary>The bounded-mailbox case: the target is behind, so the sender waits for room.</summary>
    private async ValueTask DispatchWithBackpressureAsync(ActorCell cell, Envelope envelope, CancellationToken cancellationToken)
    {
        if (await cell.PostAsync(envelope, cancellationToken).ConfigureAwait(false)) return;
        RemoveCell(envelope.Target, cell);
        await DispatchAfterDeactivationAsync(envelope, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The target deactivated between the directory lookup and the post, so try again against a
    /// fresh activation.
    /// </summary>
    /// <remarks>
    /// Removing the exact instance before retrying is what makes this terminate: the next
    /// iteration is guaranteed to build a new cell rather than spin on a corpse.
    /// </remarks>
    private async ValueTask DispatchAfterDeactivationAsync(Envelope envelope, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 64; attempt++)
        {
            var cell = GetOrCreateCell(envelope.Target, ActorId.None, null);
            if (await cell.PostAsync(envelope, cancellationToken).ConfigureAwait(false)) return;
            RemoveCell(envelope.Target, cell);
        }

        throw new ActorNetException($"Could not deliver to '{envelope.Target}': the actor kept deactivating between lookup and delivery.");
    }

    private ActorCell GetOrCreateCell(ActorId id, ActorId parent, SupervisorStrategy? strategy)
    {
        // Resolved before the Lazy so that an unregistered type throws at the call site instead of
        // being cached inside a Lazy that would then keep throwing after the type is registered.
        var registration = _registrations.TryGetValue(id.Type, out var found)
            ? found
            : throw new ActorTypeNotRegisteredException(id.Type);

        while (true)
        {
            var lazy = _cells.GetOrAdd(id, key => new Lazy<ActorCell>(() =>
            {
                var supervising = strategy
                    ?? (parent.IsEmpty ? registration.Strategy : TryGetCell(parent)?.Strategy ?? registration.Strategy);
                var cell = new ActorCell(this, key, registration, parent, supervising);
                cell.Start();
                return cell;
            }, LazyThreadSafetyMode.ExecutionAndPublication));

            var cell = lazy.Value;
            if (cell.IsAlive) return cell;

            // Exact-instance removal: another thread may already have replaced this entry, and
            // evicting its cell would restart an actor that is perfectly healthy.
            _cells.TryRemove(new KeyValuePair<ActorId, Lazy<ActorCell>>(id, lazy));
        }
    }

    private ActorCell? TryGetCell(ActorId id) => _cells.TryGetValue(id, out var lazy) && lazy.IsValueCreated ? lazy.Value : null;

    internal void RemoveCell(ActorId id, ActorCell cell)
    {
        if (_cells.TryGetValue(id, out var lazy) && lazy.IsValueCreated && ReferenceEquals(lazy.Value, cell))
            _cells.TryRemove(new KeyValuePair<ActorId, Lazy<ActorCell>>(id, lazy));
    }

    internal async Task StopCellAsync(ActorId id, DeactivationReason reason)
    {
        var cell = TryGetCell(id);
        if (cell is null) return;

        cell.RequestStop(reason);
        try { await cell.Stopped.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false); }
        catch (TimeoutException) { cell.Abort(); }
    }

    internal void DetachChild(ActorId parent, ActorId child) => TryGetCell(parent)?.DetachChild(child);

    internal void SpawnChild(ActorCell parent, ActorId child, SupervisorStrategy? strategy)
    {
        GetOrCreateCell(child, parent.Id, strategy ?? parent.Strategy);
        parent.AttachChild(child);
    }

    /// <summary>Applies an all-for-one directive to the failing actor's siblings.</summary>
    internal void ApplyToSiblings(ActorCell failing, Directive directive, Exception cause)
    {
        foreach (var lazy in _cells.Values)
        {
            if (!lazy.IsValueCreated) continue;
            var sibling = lazy.Value;
            if (sibling.Parent != failing.Parent || sibling.Id == failing.Id || !sibling.IsAlive) continue;

            if (directive == Directive.Restart) sibling.PostSystem(new RestartCommand(cause));
            else sibling.RequestStop(DeactivationReason.Supervision);
        }
    }

    /// <summary>Hands a failure up the supervision tree.</summary>
    internal async Task EscalateAsync(ActorCell failing, Exception cause)
    {
        // The child that escalated is not fit to continue - it declined to handle its own failure.
        failing.RequestStop(DeactivationReason.Supervision);

        var parent = failing.Parent.IsEmpty ? null : TryGetCell(failing.Parent);
        if (parent is null)
        {
            _logger.LogError(new ActorFailureEscalatedException(failing.Id, cause),
                "Failure in {ActorId} reached the root guardian; the actor was stopped.", failing.Id);
            return;
        }

        switch (parent.Strategy.Decide(cause))
        {
            case Directive.Restart:
                parent.PostSystem(new RestartCommand(cause));
                break;
            case Directive.Stop:
                parent.RequestStop(DeactivationReason.Supervision);
                break;
            case Directive.Escalate:
                await EscalateAsync(parent, cause).ConfigureAwait(false);
                break;
            case Directive.Resume:
                _logger.LogWarning(cause, "{Parent} resumed after {Child} escalated; the child stays stopped.", parent.Id, failing.Id);
                break;
        }
    }

    internal void ReportActivationFailure(ActorId id, Exception cause) =>
        _logger.LogError(new ActorActivationException(id, cause), "Activation failed for {ActorId}.", id);

    /// <summary>Routes a reply to whoever is waiting for it: a pending ask here, one on another node, or a sender.</summary>
    internal async ValueTask<bool> ReplyAsync(Envelope original, object reply, CancellationToken cancellationToken)
    {
        if (original.CorrelationId is { } correlationId)
        {
            if (original.ReplyToNode is { } node && node != NodeId)
            {
                await SendRemoteFrameAsync(node, new WireEnvelope
                {
                    Kind = WireKind.AskReply,
                    CorrelationId = correlationId,
                    FromNode = NodeId,
                }, reply, cancellationToken).ConfigureAwait(false);
                return true;
            }

            return CompleteAsk(correlationId, reply);
        }

        if (original.Sender.IsEmpty) return false;

        await TellAsync(original.Sender, reply, original.Target, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Answers a pending ask with the failure that happened instead of a reply.</summary>
    internal async ValueTask FailAskAsync(Envelope original, Exception cause)
    {
        if (original.CorrelationId is not { } correlationId) return;

        if (original.ReplyToNode is { } node && node != NodeId)
        {
            try
            {
                await SendRemoteFrameAsync(node, new WireEnvelope
                {
                    Kind = WireKind.AskFailure,
                    CorrelationId = correlationId,
                    FromNode = NodeId,
                    Error = $"{cause.GetType().Name}: {cause.Message}",
                }, null, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not report an ask failure back to {NodeId}.", node);
            }

            return;
        }

        if (_pendingAsks.TryRemove(correlationId, out var pending))
            pending.Fail(new ActorNetException($"Actor '{original.Target}' failed while handling the request.", cause));
    }

    private bool CompleteAsk(string correlationId, object reply) =>
        _pendingAsks.TryRemove(correlationId, out var pending) && pending.Complete(reply);

    /// <summary>
    /// Builds an actor instance, through the container when there is one.
    /// </summary>
    /// <remarks>
    /// Going through <see cref="ActivatorUtilities"/> is what lets an actor take a repository or an
    /// <c>HttpClient</c> as a constructor parameter instead of reaching for a static - which is the
    /// difference between an actor that can be unit tested and one that cannot.
    /// </remarks>
    internal IActor CreateInstance(Type actorType)
    {
        var instance = _services is not null
            ? ActivatorUtilities.CreateInstance(_services, actorType)
            : Activator.CreateInstance(actorType)
              ?? throw new ActorNetException(
                  $"{actorType.Name} could not be constructed. Give it a parameterless constructor, or pass an IServiceProvider to the ActorSystem.");

        return instance as IActor
               ?? throw new ActorNetException($"{actorType.Name} does not implement IActor.");
    }

    private ValueTask SendRemoteAsync(string nodeId, WireKind kind, ActorId target, object message, ActorId sender, string? correlationId, CancellationToken cancellationToken)
    {
        var frame = new WireEnvelope
        {
            Kind = kind,
            Target = target.ToString(),
            Sender = sender.IsEmpty ? null : sender.ToString(),
            CorrelationId = correlationId,
            ReplyToNode = correlationId is null ? null : NodeId,
            FromNode = NodeId,
        };

        return SendRemoteFrameAsync(nodeId, frame, message, cancellationToken);
    }

    private async ValueTask SendRemoteFrameAsync(string nodeId, WireEnvelope frame, object? payload, CancellationToken cancellationToken)
    {
        if (_transport is null) throw new ActorNetException("Networking is disabled on this node, so it cannot reach another one.");

        if (payload is not null)
        {
            var (alias, element) = Serializer.Serialize(payload);
            frame.MessageAlias = alias;
            frame.Payload = element;
        }

        await _transport.SendAsync(nodeId, frame, cancellationToken).ConfigureAwait(false);
        MetricsCollector.RecordRemoteSent();
    }

    /// <summary>Handles one inbound frame, whatever connection it arrived on.</summary>
    private async Task OnFrameAsync(WireEnvelope frame)
    {
        MetricsCollector.RecordRemoteReceived();

        switch (frame.Kind)
        {
            case WireKind.Join:
            case WireKind.JoinAck:
            case WireKind.Gossip:
            case WireKind.Leave:
            {
                var reply = _cluster.HandleFrame(frame);
                if (reply is not null && frame.FromNode is { Length: > 0 } from && _transport is not null)
                {
                    try { await _transport.SendAsync(from, reply, _shutdown.Token).ConfigureAwait(false); }
                    catch (Exception ex) { _logger.LogDebug(ex, "Could not answer a {Kind} from {NodeId}.", frame.Kind, from); }
                }

                return;
            }

            case WireKind.AskReply:
            {
                if (frame.CorrelationId is { } id && frame.MessageAlias is { } alias && frame.Payload is { } payload)
                    CompleteAsk(id, Serializer.Deserialize(alias, payload));
                return;
            }

            case WireKind.AskFailure:
            {
                if (frame.CorrelationId is { } id && _pendingAsks.TryRemove(id, out var pending))
                    pending.Fail(new ActorNetException(frame.Error ?? "The remote actor failed while handling the request."));
                return;
            }

            case WireKind.Message:
            case WireKind.AskRequest:
            {
                if (!ActorId.TryParse(frame.Target, out var target))
                {
                    _logger.LogWarning("Dropping an inbound frame with an unusable target '{Target}'.", frame.Target);
                    return;
                }

                if (frame.MessageAlias is not { } alias || frame.Payload is not { } payload)
                {
                    _logger.LogWarning("Dropping an inbound frame for {Target} with no payload.", target);
                    return;
                }

                var message = Serializer.Deserialize(alias, payload);
                ActorId.TryParse(frame.Sender, out var sender);

                // Delivered locally even if the ring has since moved this key elsewhere. The
                // sender routed with the view it had, and bouncing the message onward risks a
                // loop between two nodes that disagree during a rebalance.
                await DispatchLocalAsync(
                    Envelope.Create(target, message, sender, frame.CorrelationId, frame.ReplyToNode),
                    _shutdown.Token).ConfigureAwait(false);
                return;
            }
        }
    }

    private async Task SweepLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(Options.SweepInterval);
        while (true)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false)) return;
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var swept = 0;
            foreach (var lazy in _cells.Values)
            {
                if (!lazy.IsValueCreated) continue;
                if (lazy.Value.TryStopIfIdle(Options.IdleTimeout)) swept++;
            }

            if (swept > 0) _logger.LogDebug("Idle sweep deactivated {Count} actor(s).", swept);
        }
    }

    /// <summary>
    /// Hands off actors whose keys now belong to another node.
    /// </summary>
    /// <remarks>
    /// This is the elastic half of elastic scaling. Deactivation flushes state through
    /// <see cref="IActor.OnDeactivateAsync"/>, and the next message re-activates the actor on its
    /// new owner from the store - so scaling out migrates roughly 1/N of the actors and nothing
    /// else moves.
    /// </remarks>
    private void OnMembershipChanged(IReadOnlyList<ClusterMember> members)
    {
        if (!Options.Cluster.RebalanceOnMembershipChange || _shutdown.IsCancellationRequested) return;

        var moved = 0;
        foreach (var (id, lazy) in _cells)
        {
            if (!lazy.IsValueCreated || _cluster.IsLocal(id)) continue;
            if (lazy.Value.RequestStop(DeactivationReason.Rebalanced)) moved++;
        }

        if (moved > 0)
            _logger.LogInformation("Membership changed to {Count} member(s); handing off {Moved} actor(s).", members.Count, moved);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _shutdown.Dispose();
    }

    /// <summary>One caller blocked in an ask.</summary>
    private sealed class PendingAsk
    {
        private readonly TaskCompletionSource<object> _source = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<object> Task => _source.Task;

        public bool Complete(object reply) => _source.TrySetResult(reply);

        public void Fail(Exception cause) => _source.TrySetException(cause);

        public void Cancel() => _source.TrySetCanceled();
    }
}
