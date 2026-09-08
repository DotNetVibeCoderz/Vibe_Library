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

    /// <summary>
    /// This node's transport, for tests that need to pull the network out from under it.
    /// </summary>
    /// <remarks>
    /// Internal on purpose. An abrupt node loss is the case failure detection exists for and the
    /// one a graceful <see cref="StopAsync"/> cannot produce - it announces a departure, which
    /// peers act on immediately and which therefore proves nothing about detection. Closing the
    /// transport is the closest an in-process test can get to unplugging a cable.
    /// </remarks>
    internal ITransport? Transport => _transport;
    private Task? _sweeper;
    private Task? _digests;

    // What each peer last said it was holding that this node would inherit. Advice only: nothing
    // here is consulted for routing, and an entry is dropped the moment its node is gone.
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _inheritable = new(StringComparer.Ordinal);

    // Asks this node forwarded on behalf of a client, so the reply can be sent back to it. The
    // owner replies here because this node named itself as the reply address; without this map
    // there would be nothing to say who the reply was really for.
    private readonly ConcurrentDictionary<string, (string Client, DateTimeOffset At)> _proxiedAsks = new(StringComparer.Ordinal);
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
    public IDeadLetterQueue DeadLetters => Options.DeadLetters;

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

        // The runtime's own protocol. Registered here rather than left to the application, because
        // a watch that works locally and fails to serialize across a node boundary would be a worse
        // outcome than no watch at all.
        Serializer.Types.Register<Watch>();
        Serializer.Types.Register<Unwatch>();
        Serializer.Types.Register<Terminated>();
        Serializer.Types.Register<NodeStatus>();
        Serializer.Types.Register<Warm>();
        Serializer.Types.Register<KeyDigest>();
        Serializer.Types.Register<ClusterView>();
        Serializer.Types.Register<ClusterViewMember>();
        Serializer.Types.Register<LeaveRequest>();
        Serializer.Types.Register<LeaveDecision>();
        Serializer.Types.Register<Inspect>();
        Serializer.Types.Register<Inspected>();
        MetricsCollector = new MetricsCollector(Options.NodeId);
        _cluster = new ClusterMembership(
            Options.NodeId,
            Options.EffectiveAdvertisedHost,
            Options.AdvertisedPort ?? Options.Port,
            Options.Cluster,
            LoggerFactory.CreateLogger<ClusterMembership>());
        _cluster.MembershipChanged += OnMembershipChanged;
        _cluster.SelfDowned += OnSelfDowned;
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
            _transport = new TcpTransport(
                Options.Host, Options.Port, OnFrameAsync, _cluster.Resolve,
                LoggerFactory.CreateLogger<TcpTransport>(), Options.Security, Options.SendTimeout,
                Options.OutboundQueueCapacity, Options.WireFormat, Serializer);
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

        if (Options.InheritanceDigestLimit > 0 && Options.Cluster.Enabled)
            _digests = Task.Run(() => DigestLoopAsync(_shutdown.Token), CancellationToken.None);
        _logger.LogInformation("Node {NodeId} started on {Host}:{Port}.", NodeId, Options.Host, BoundPort);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _started, 0) == 0) return;

        _logger.LogInformation("Node {NodeId} stopping; deactivating {Count} actor(s).", NodeId, _cells.Count);

        // Noted before the drain empties the directory. These are the keys that are about to move.
        var moving = _cells.Keys.ToArray();

        // Before the drain, not after: the point is that the keys of two nodes do not move at the
        // same time, and they start moving the moment a departure is announced.
        await AcquireLeaveTokenAsync(cancellationToken).ConfigureAwait(false);

        // Flush before announcing, not after. A peer that hears the leave rebuilds its ring at once
        // and the first message to a key that moved reactivates that actor from the store - so if
        // this node's state has not landed yet, the new activation starts from a stale version and
        // the flush still to come overwrites whatever it went on to do.
        await DrainAsync().ConfigureAwait(false);

        await _cluster.LeaveAsync(cancellationToken).ConfigureAwait(false);

        // Traffic that arrived during the first drain was handled here, because this node still
        // owned those keys, and can have activated an actor the first pass had already been past.
        // One more pass: after the announce nothing new is routed here.
        await DrainAsync().ConfigureAwait(false);

        // After the announce, because the successors only own these keys once they have heard.
        await WarmSuccessorsAsync(moving).ConfigureAwait(false);

        // Handed back once the departure is announced and the handover is done, which is the point
        // at which the next node can start without the two overlapping.
        await ReleaseLeaveTokenAsync(cancellationToken).ConfigureAwait(false);

        await _shutdown.CancelAsync().ConfigureAwait(false);

        foreach (var pending in _pendingAsks.Values) pending.Fail(new ActorNetException("The node stopped before a reply arrived."));
        _pendingAsks.Clear();

        await _cluster.DisposeAsync().ConfigureAwait(false);
        if (_transport is not null) await _transport.DisposeAsync().ConfigureAwait(false);

        _logger.LogInformation("Node {NodeId} stopped.", NodeId);
    }

    /// <summary>
    /// Tells whoever inherits these keys to activate them now rather than on the first message.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what <see cref="HashRing.PreferenceList"/> is for: the entry after this node is the
    /// one that takes the key when this node goes. Best effort throughout - a successor that does
    /// not answer simply activates on demand later, which is what would have happened anyway, and
    /// a node on its way out is the wrong place to insist on anything.
    /// </para>
    /// <para>
    /// Runs after the leave has been announced, because until then the successors do not own these
    /// keys and would forward the message straight back here.
    /// </para>
    /// </remarks>
    private async Task WarmSuccessorsAsync(IReadOnlyCollection<ActorId> moving)
    {
        if (Options.WarmHandoffLimit <= 0 || moving.Count == 0 || _transport is null) return;
        if (!Options.Cluster.Enabled || _cluster.Ring.Nodes.Count <= 1) return;

        var ring = _cluster.Ring;
        var warmed = 0;

        foreach (var id in moving)
        {
            if (warmed >= Options.WarmHandoffLimit) break;

            var successor = ring.PreferenceList(id.ToString(), 2)
                .FirstOrDefault(node => !string.Equals(node, NodeId, StringComparison.Ordinal));

            if (successor is null) continue;

            if (_cluster.Resolve(successor) is not { } address) continue;

            try
            {
                var (alias, payload) = Serializer.Serialize(new Warm());
                var frame = new WireEnvelope
                {
                    Kind = WireKind.Message,
                    Target = id.ToString(),
                    MessageAlias = alias,
                    Payload = payload,
                    FromNode = NodeId,
                };

                // Written straight to a socket rather than queued on the peer connection. The
                // queue is drained by a writer loop, and this node closes its transport moments
                // later - so a queued frame is a frame that may never leave. This path returns
                // once the bytes are out, which is the only thing that makes the handover
                // reliable rather than usually reliable.
                await _transport.SendToAddressAsync(address.Host, address.Port, frame, CancellationToken.None)
                    .ConfigureAwait(false);

                warmed++;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not warm {ActorId} on {NodeId}.", id, successor);
            }
        }

        if (warmed > 0)
            _logger.LogInformation("Asked successors to activate {Count} of {Total} actor(s) before stopping.", warmed, moving.Count);
    }

    /// <summary>Deactivates every live actor and waits for them to finish.</summary>
    /// <remarks>
    /// Deactivation runs on each actor's own loop, so they are all asked to stop and then waited on
    /// once, rather than serializing a node with thousands of actors through one shutdown at a
    /// time. Only cells that were actually built: touching a <see cref="Lazy{T}"/> that has not run
    /// yet would activate an actor purely in order to deactivate it.
    /// </remarks>
    private async Task DrainAsync()
    {
        var cells = _cells.Values.Where(l => l.IsValueCreated).Select(l => l.Value).ToArray();
        if (cells.Length == 0) return;

        foreach (var cell in cells) cell.RequestStop(DeactivationReason.Shutdown);

        var drained = Task.WhenAll(cells.Select(c => c.Stopped));
        if (await Task.WhenAny(drained, Task.Delay(TimeSpan.FromSeconds(15), CancellationToken.None)).ConfigureAwait(false) == drained) return;

        _logger.LogWarning("Some actors did not deactivate within 15s; aborting their loops.");
        foreach (var cell in cells) cell.Abort();
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

    /// <summary>
    /// Asks every reachable member what its counters say, and returns them together.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The ring and the member table were already cluster-wide; the counters were not, so a console
    /// could report five members and only what one of them was doing. A node buried under work
    /// looks exactly like an idle one from three nodes away.
    /// </para>
    /// <para>
    /// Peers that do not answer inside <paramref name="timeout"/> are named in
    /// <see cref="ClusterStatus.Silent"/> rather than dropped. Summing four nodes and presenting it
    /// as five would be worse than showing which one is missing - and a peer going quiet is itself
    /// the thing somebody looking at this page wants to know.
    /// </para>
    /// </remarks>
    public async Task<ClusterStatus> GetClusterStatusAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var window = timeout ?? TimeSpan.FromSeconds(2);
        var here = NodeStatus.From(MetricsCollector.Snapshot());

        var peers = _cluster.Members
            .Where(m => m.NodeId != NodeId && m.Status == MemberStatus.Up)
            .Select(m => m.NodeId)
            .ToArray();

        if (peers.Length == 0 || _transport is null)
            return new ClusterStatus(DateTimeOffset.UtcNow, [here], []);

        // Asked in parallel: one slow peer should cost the whole refresh its own latency, not the
        // sum of everyone's.
        var answers = await Task.WhenAll(peers.Select(peer => AskNodeStatusAsync(peer, window, cancellationToken)))
            .ConfigureAwait(false);

        var nodes = new List<NodeStatus> { here };
        var silent = new List<string>();

        for (var i = 0; i < peers.Length; i++)
        {
            if (answers[i] is { } status) nodes.Add(status);
            else silent.Add(peers[i]);
        }

        return new ClusterStatus(
            DateTimeOffset.UtcNow,
            nodes.OrderBy(n => n.NodeId, StringComparer.Ordinal).ToArray(),
            silent);
    }

    /// <summary>
    /// Waits for permission to leave, so a rolling restart takes the nodes one at a time.
    /// </summary>
    /// <remarks>
    /// Returns false when the budget runs out, and the caller leaves anyway - a shutdown that
    /// blocks forever is worse than an uncoordinated one, because whatever asked this node to stop
    /// will kill it instead, and a killed node announces nothing at all.
    /// </remarks>
    private async Task<bool> AcquireLeaveTokenAsync(CancellationToken cancellationToken)
    {
        if (!Options.Cluster.CoordinatedLeave || !Options.Cluster.Enabled || _cluster.IsSingleNode) return true;

        var deadline = DateTimeOffset.UtcNow + Options.Cluster.LeaveTokenWait;

        while (true)
        {
            var decision = await AskToLeaveAsync(releasing: false, cancellationToken).ConfigureAwait(false);

            if (decision is { Granted: true })
            {
                _logger.LogInformation("Node {NodeId} holds the leave token.", NodeId);
                return true;
            }

            var left = deadline - DateTimeOffset.UtcNow;
            if (left <= TimeSpan.Zero)
            {
                _logger.LogWarning(
                    "Node {NodeId} waited {Wait} for the leave token (held by {Holder}) and is leaving without it.",
                    NodeId, Options.Cluster.LeaveTokenWait, decision?.HeldBy ?? "nobody that answered");
                return false;
            }

            // A coordinator that does not answer at all is retried on the cadence a refusal asks
            // for rather than hammered: it is most often one that is itself shutting down.
            var retry = decision?.RetryAfter ?? TimeSpan.FromMilliseconds(500);
            if (retry > left) retry = left;
            if (retry < TimeSpan.FromMilliseconds(50)) retry = TimeSpan.FromMilliseconds(50);

            try { await Task.Delay(retry, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { return false; }
        }
    }

    /// <summary>Hands the leave token back, so the next node does not wait out the lease.</summary>
    private async Task ReleaseLeaveTokenAsync(CancellationToken cancellationToken)
    {
        if (!Options.Cluster.CoordinatedLeave || !Options.Cluster.Enabled) return;

        try { await AskToLeaveAsync(releasing: true, cancellationToken).ConfigureAwait(false); }
        catch (Exception ex)
        {
            // Best effort by design: the lease expiring is the backstop, and failing to hand a
            // token back is not a reason to fail a shutdown that has otherwise finished.
            _logger.LogDebug(ex, "Could not hand the leave token back.");
        }
    }

    /// <summary>Puts one leave request to the coordinator, or answers it here when this node is it.</summary>
    private async Task<LeaveDecision?> AskToLeaveAsync(bool releasing, CancellationToken cancellationToken)
    {
        if (_cluster.Coordinator is not { } coordinator) return null;

        // No round trip to ask yourself. This is also the case where the coordinator is the node
        // that is leaving, which is the one a protocol would have had to handle specially.
        if (string.Equals(coordinator, NodeId, StringComparison.Ordinal))
            return _cluster.DecideLeave(new LeaveRequest(NodeId, releasing));

        var correlationId = Guid.NewGuid().ToString("N");
        var pending = new PendingAsk();
        _pendingAsks[correlationId] = pending;

        using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        await using var registration = linked.Token.Register(static state =>
        {
            var (asks, id) = ((ConcurrentDictionary<string, PendingAsk>, string))state!;
            if (asks.TryRemove(id, out var waiting)) waiting.Cancel();
        }, (_pendingAsks, correlationId)).ConfigureAwait(false);

        try
        {
            var (alias, payload) = Serializer.Serialize(new LeaveRequest(NodeId, releasing));
            var frame = new WireEnvelope
            {
                Kind = WireKind.LeaveTokenRequest,
                CorrelationId = correlationId,
                ReplyToNode = NodeId,
                FromNode = NodeId,
                MessageAlias = alias,
                Payload = payload,
            };

            await SendRemoteFrameAsync(coordinator, frame, null, cancellationToken).ConfigureAwait(false);
            return await pending.Task.ConfigureAwait(false) as LeaveDecision;
        }
        catch (Exception ex)
        {
            // Silence from the coordinator is an ordinary outcome - it may be leaving itself - and
            // is reported as no answer, so the caller can retry within its budget.
            _logger.LogDebug(ex, "No leave decision from {NodeId}.", coordinator);
            return null;
        }
        finally
        {
            _pendingAsks.TryRemove(correlationId, out _);
        }
    }

    /// <summary>One peer's counters, or null when it did not answer in time.</summary>
    private async Task<NodeStatus?> AskNodeStatusAsync(string nodeId, TimeSpan window, CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        var pending = new PendingAsk();
        _pendingAsks[correlationId] = pending;

        using var timeoutSource = new CancellationTokenSource(window);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        await using var registration = linked.Token.Register(static state =>
        {
            var (asks, id) = ((ConcurrentDictionary<string, PendingAsk>, string))state!;
            if (asks.TryRemove(id, out var waiting)) waiting.Cancel();
        }, (_pendingAsks, correlationId)).ConfigureAwait(false);

        try
        {
            var frame = new WireEnvelope
            {
                Kind = WireKind.NodeStatusRequest,
                CorrelationId = correlationId,
                ReplyToNode = NodeId,
                FromNode = NodeId,
            };

            await SendRemoteFrameAsync(nodeId, frame, null, cancellationToken).ConfigureAwait(false);
            return await pending.Task.ConfigureAwait(false) as NodeStatus;
        }
        catch (Exception ex)
        {
            // A peer that is gone, congested or simply slow is an ordinary outcome here, not a
            // failure of the query: it is reported as silence rather than thrown.
            _logger.LogDebug(ex, "No status from {NodeId} within {Window}.", nodeId, window);
            return null;
        }
        finally
        {
            _pendingAsks.TryRemove(correlationId, out _);
        }
    }

    /// <summary>
    /// Asks an actor what it is holding, wherever in the cluster it is.
    /// </summary>
    /// <remarks>
    /// Answering a question about an actor otherwise means writing a message for the purpose,
    /// handling it and registering both - fine for a question you knew you would ask, useless for
    /// one you did not. This activates the actor if it is not already running, which is the same
    /// thing any other message would do.
    /// </remarks>
    public Task<Inspected> InspectAsync(ActorId id, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        AskAsync<Inspected>(id, new Inspect(), timeout, cancellationToken);

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

        RecordDeadLetter(envelope.Target, envelope.Sender, envelope.Message, envelope.Message.GetType().Name,
            DeadLetterReason.UndeliverableToActor, "The actor kept deactivating between lookup and delivery.");

        throw new ActorNetException($"Could not deliver to '{envelope.Target}': the actor kept deactivating between lookup and delivery.");
    }

    private ActorCell GetOrCreateCell(ActorId id, ActorId parent, SupervisorStrategy? strategy)
    {
        // Resolved before the Lazy so that an unregistered type throws at the call site instead of
        // being cached inside a Lazy that would then keep throwing after the type is registered.
        if (!_registrations.TryGetValue(id.Type, out var registration))
        {
            // Recorded as well as thrown. The caller of a local send learns immediately, but an
            // inbound remote frame has no caller to tell - and both end up here.
            RecordDeadLetter(id, ActorId.None, null, id.Type, DeadLetterReason.UnregisteredActorType,
                $"Actor type '{id.Type}' is not registered on node {NodeId}.");
            throw new ActorTypeNotRegisteredException(id.Type);
        }

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
            // The alias only. The body itself is encoded by whichever format the connection turns
            // out to write, so serializing it here would produce JSON that a binary frame throws
            // away - which is exactly the cost the binary format exists to remove.
            frame.MessageAlias = Serializer.Types.AliasOf(payload.GetType());
            frame.Body = payload;
        }

        // Stamped here rather than at each call site, so every frame that leaves this node carries
        // the trace - including the replies and failures, which is the half that shows how long the
        // caller actually waited. Null when nothing is tracing, which costs one null check.
        if (System.Diagnostics.Activity.Current is { } current)
        {
            frame.TraceParent ??= current.Id;
            frame.TraceState ??= current.TraceStateString;
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

            case WireKind.NodeStatusRequest:
            {
                if (frame.CorrelationId is not { } correlation || frame.FromNode is not { Length: > 0 } asker || _transport is null)
                    return;

                var (alias, payload) = Serializer.Serialize(NodeStatus.From(MetricsCollector.Snapshot()));
                var reply = new WireEnvelope
                {
                    Kind = WireKind.AskReply,
                    CorrelationId = correlation,
                    FromNode = NodeId,
                    MessageAlias = alias,
                    Payload = payload,
                };

                try { await _transport.SendAsync(asker, reply, _shutdown.Token).ConfigureAwait(false); }
                catch (Exception ex) { _logger.LogDebug(ex, "Could not answer a status request from {NodeId}.", asker); }

                return;
            }

            case WireKind.KeyDigest:
            {
                var digest = frame.Body as KeyDigest
                    ?? (frame.MessageAlias is { } digestAlias && frame.Payload is { } digestPayload
                        ? Serializer.Deserialize(digestAlias, digestPayload) as KeyDigest
                        : null);

                // Kept under the name the sender gave rather than the frame's, because the digest
                // is about that node's keys and nothing else makes it addressable later.
                if (digest is { Node.Length: > 0 }) _inheritable[digest.Node] = digest.Keys;
                return;
            }

            case WireKind.ClusterViewRequest:
            {
                if (frame.CorrelationId is not { } viewCorrelation || frame.FromNode is not { Length: > 0 } viewAsker || _transport is null)
                    return;

                // Only what a client needs to compute ownership. Incarnations, statuses and
                // last-seen times would give it an idea of membership that could drift from the
                // cluster's own and still be believed.
                var view = new ClusterView(
                    [.. _cluster.Members.Select(m => new ClusterViewMember(m.NodeId, m.Host, m.Port))],
                    Options.Cluster.VirtualNodesPerMember);

                var (viewAlias, viewPayload) = Serializer.Serialize(view);
                var viewReply = new WireEnvelope
                {
                    Kind = WireKind.AskReply,
                    CorrelationId = viewCorrelation,
                    FromNode = NodeId,
                    MessageAlias = viewAlias,
                    Payload = viewPayload,
                };

                try { await _transport.SendAsync(viewAsker, viewReply, _shutdown.Token).ConfigureAwait(false); }
                catch (Exception ex) { _logger.LogDebug(ex, "Could not answer a cluster view request from {NodeId}.", viewAsker); }

                return;
            }

            case WireKind.LeaveTokenRequest:
            {
                if (frame.CorrelationId is not { } correlation || frame.FromNode is not { Length: > 0 } asker || _transport is null)
                    return;

                var request = frame.Body as LeaveRequest
                    ?? (frame.MessageAlias is { } requestAlias && frame.Payload is { } requestPayload
                        ? Serializer.Deserialize(requestAlias, requestPayload) as LeaveRequest
                        : null);

                if (request is null) return;

                // Answered even when this node no longer believes it is the coordinator. The asker
                // decided who to ask from its own member table; disagreeing by staying silent would
                // make it wait out its whole budget to learn nothing.
                var (alias, payload) = Serializer.Serialize(_cluster.DecideLeave(request));
                var reply = new WireEnvelope
                {
                    Kind = WireKind.AskReply,
                    CorrelationId = correlation,
                    FromNode = NodeId,
                    MessageAlias = alias,
                    Payload = payload,
                };

                try { await _transport.SendAsync(asker, reply, _shutdown.Token).ConfigureAwait(false); }
                catch (Exception ex) { _logger.LogDebug(ex, "Could not answer a leave request from {NodeId}.", asker); }

                return;
            }

            case WireKind.AskReply:
            {
                if (frame.CorrelationId is not { } id) return;

                // Somebody else's reply, travelling back through this node because this node put
                // the question on their behalf.
                if (await TryReturnToClientAsync(frame, id).ConfigureAwait(false)) return;

                if (frame.Body is { } decoded)
                {
                    CompleteAsk(id, decoded);
                    return;
                }

                if (frame.MessageAlias is { } alias && frame.Payload is { } payload)
                    CompleteAsk(id, Serializer.Deserialize(alias, payload));

                return;
            }

            case WireKind.AskFailure:
            {
                if (frame.CorrelationId is { } failed && await TryReturnToClientAsync(frame, failed).ConfigureAwait(false)) return;

                if (frame.CorrelationId is { } id && _pendingAsks.TryRemove(id, out var pending))
                    pending.Fail(new ActorNetException(frame.Error ?? "The remote actor failed while handling the request."));
                return;
            }

            case WireKind.Message:
            case WireKind.AskRequest:
            {
                if (!ActorId.TryParse(frame.Target, out var target))
                {
                    RecordDeadLetter(ActorId.None, ActorId.None, null, frame.MessageAlias ?? "<unknown>",
                        DeadLetterReason.UnroutableFrame, $"Target '{frame.Target}' is not a routable actor address.");
                    return;
                }

                // A binary frame carries a decoded body and no JSON payload; a JSON one carries the
                // payload and no body. Requiring the payload rejected every binary message here,
                // before the line below could use what the frame had actually brought.
                if (frame.MessageAlias is not { } alias || (frame.Body is null && frame.Payload is null))
                {
                    RecordDeadLetter(target, ActorId.None, null, frame.MessageAlias ?? "<unknown>",
                        DeadLetterReason.UnroutableFrame, "The frame carried no message alias and no body.");
                    return;
                }

                object message;
                try
                {
                    // A binary frame decoded its body while it was being read, next to the type
                    // allow-list that says which type the alias means. A JSON one still parses here.
                    message = frame.Body ?? Serializer.Deserialize(alias, frame.Payload!.Value);
                }
                catch (UnknownMessageTypeException ex)
                {
                    // The allow-list did its job. The body is deliberately not kept: materializing
                    // a payload this node just refused to construct would undo the refusal.
                    RecordDeadLetter(target, ActorId.None, null, alias, DeadLetterReason.UnknownMessageType, ex.Message);
                    return;
                }

                ActorId.TryParse(frame.Sender, out var sender);

                // A client's frame, for a key this node does not own, is forwarded once. A client
                // is not a member and cannot route - it sends to whichever node it reached - so
                // delivering here would activate the actor on the wrong node and leave the cluster
                // with two activations of one address. The loop this guards against is between
                // peers, and a peer is exactly what the sender is not here.
                if (await TryForwardForClientAsync(frame, target).ConfigureAwait(false)) return;

                // Delivered locally even if the ring has since moved this key elsewhere. The
                // sender routed with the view it had, and bouncing the message onward risks a
                // loop between two nodes that disagree during a rebalance.
                await DeliverInboundAsync(
                    Envelope.Create(target, message, sender, frame.CorrelationId, frame.ReplyToNode, frame.TraceParent, frame.TraceState))
                    .ConfigureAwait(false);
                return;
            }
        }
    }

    /// <summary>
    /// Sends a client's misrouted frame to the node that owns the key, once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only for a sender that is not a cluster member. A peer routed with its own view of the ring,
    /// and bouncing its message onward is what risks a loop between two nodes mid-rebalance; a
    /// client has no view at all and sends to whichever node it happens to hold a connection to.
    /// Delivering that here would activate the actor on a node that does not own its key, and the
    /// cluster would hold two activations of one address.
    /// </para>
    /// <para>
    /// One hop, by construction: the forwarded frame is stamped with this node's id, so the node
    /// that receives it sees a member and delivers locally.
    /// </para>
    /// </remarks>
    private async Task<bool> TryForwardForClientAsync(WireEnvelope frame, ActorId target)
    {
        if (_transport is null || !Options.Cluster.Enabled) return false;
        if (frame.FromNode is not { Length: > 0 } from || _cluster.IsMember(from)) return false;

        var owner = _cluster.OwnerOf(target);
        if (string.Equals(owner, NodeId, StringComparison.Ordinal)) return false;

        // An owner nobody can reach is no better than this node. Handling it here at least gets the
        // message to an actor, which is what the client asked for.
        if (!_cluster.OwnerIsReachable(target)) return false;

        var forwarded = new WireEnvelope
        {
            Kind = frame.Kind,
            Target = frame.Target,
            Sender = frame.Sender,
            MessageAlias = frame.MessageAlias,

            // Cloned, because a JsonElement is a window onto the document it was parsed from and
            // that document belongs to the frame this node is about to finish with. Passing the
            // window on and writing through it later reads whatever the buffer has become.
            Payload = frame.Payload?.Clone(),
            Body = frame.Body,
            CorrelationId = frame.CorrelationId,
            TraceParent = frame.TraceParent,
            TraceState = frame.TraceState,

            // This node answers for the client from here on, in both fields: the owner replies to
            // whoever asked, and it has no connection to a client it has never heard from.
            FromNode = NodeId,
            ReplyToNode = frame.Kind == WireKind.AskRequest ? NodeId : null,
        };

        // Recorded before the send, not after. The owner can answer before SendAsync has returned
        // here - it does, often, on a loopback cluster - and a reply that arrives with nothing to
        // say who it was for is dropped, leaving the client to wait out its whole timeout.
        var proxied = frame.Kind == WireKind.AskRequest && frame.CorrelationId is { Length: > 0 };
        if (proxied) _proxiedAsks[frame.CorrelationId!] = (from, DateTimeOffset.UtcNow);

        try
        {
            await _transport.SendAsync(owner, forwarded, _shutdown.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            // The owner could not be reached after all. Falling through to local delivery is the
            // lesser wrong: the actor runs in the wrong place rather than the message being lost.
            if (proxied) _proxiedAsks.TryRemove(frame.CorrelationId!, out _);

            _logger.LogDebug(ex, "Could not forward a client frame to {NodeId}; handling it here.", owner);
            return false;
        }
    }

    /// <summary>Sends a reply back to the client this node asked on behalf of.</summary>
    private async Task<bool> TryReturnToClientAsync(WireEnvelope frame, string correlationId)
    {
        if (!_proxiedAsks.TryRemove(correlationId, out var proxied) || _transport is null) return false;

        try
        {
            await _transport.SendAsync(proxied.Client, frame, _shutdown.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The client hung up while its answer was in flight. Nothing to do and nobody to tell.
            _logger.LogDebug(ex, "Could not return a reply to client {ClientId}.", proxied.Client);
        }

        return true;
    }

    /// <summary>
    /// Delivers an inbound remote message, bounding how long it may wait and telling the sender
    /// when it cannot be delivered at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This runs on the connection's reader loop, which is why it cannot simply await mailbox room
    /// the way a local send does: every other actor's traffic on that connection is queued behind
    /// it, so one busy actor with a bounded mailbox would stall the whole node.
    /// </para>
    /// <para>
    /// It stays sequential rather than dispatching concurrently, because concurrent dispatch would
    /// break the ordering guarantee that messages from one sender to one actor arrive in order.
    /// The honest trade-off is therefore a <em>bounded</em> stall: other traffic on this connection
    /// waits at most <see cref="ActorSystemOptions.RemoteDeliveryTimeout"/> behind a full mailbox,
    /// and then the message is refused rather than waited on forever.
    /// </para>
    /// <para>
    /// Every failure path answers a waiting ask. Without that, a caller on another node sees only
    /// a timeout and cannot tell a refused delivery from a slow handler.
    /// </para>
    /// </remarks>
    private async Task DeliverInboundAsync(Envelope envelope)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        deadline.CancelAfter(Options.RemoteDeliveryTimeout);

        try
        {
            await DispatchLocalAsync(envelope, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !_shutdown.IsCancellationRequested)
        {
            var refusal = new MailboxFullException(envelope.Target, Options.RemoteDeliveryTimeout);

            RecordDeadLetter(envelope.Target, envelope.Sender, envelope.Message,
                envelope.Message.GetType().Name, DeadLetterReason.MailboxFull, refusal.Message);

            await FailAskAsync(envelope, refusal).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Already recorded as a dead letter wherever it was detected - an unregistered actor
            // type, say. What is still missing is telling the remote caller, who would otherwise
            // wait out the ask timeout for a message this node refused immediately.
            await FailAskAsync(envelope, ex).ConfigureAwait(false);
        }
    }

    /// <summary>Records an undeliverable message and logs it once.</summary>
    internal void RecordDeadLetter(ActorId target, ActorId sender, object? message, string messageType, DeadLetterReason reason, string detail)
    {
        Options.DeadLetters.Record(new DeadLetter(target, sender, message, messageType, reason, detail, DateTimeOffset.UtcNow));
        MetricsCollector.RecordDeadLetter();
        ActorNetDiagnostics.DeadLetters.Add(1,
            new KeyValuePair<string, object?>("reason", reason.ToString()),
            new KeyValuePair<string, object?>("message.type", messageType));
        _logger.LogWarning("Dead letter for {Target} ({MessageType}): {Reason} - {Detail}", target, messageType, reason, detail);
    }

    /// <summary>
    /// Tells each successor which of this node's keys it would inherit.
    /// </summary>
    /// <remarks>
    /// Sent per successor rather than broadcast, so a peer hears only about the keys it would
    /// actually take. In a cluster of many nodes that is most of the saving: the whole directory
    /// broadcast to everybody would be the same information N times over, and useful once.
    /// </remarks>
    private async Task DigestLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(Options.InheritanceDigestInterval);
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

            try { await PublishKeyDigestsAsync(cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogDebug(ex, "Publishing the key digest failed."); }
        }
    }

    private async Task PublishKeyDigestsAsync(CancellationToken cancellationToken)
    {
        if (_transport is null || _cluster.Ring.Nodes.Count <= 1) return;

        var ring = _cluster.Ring;
        var limit = Options.InheritanceDigestLimit;
        var bySuccessor = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var (id, lazy) in _cells)
        {
            if (!lazy.IsValueCreated) continue;

            var successor = ring.PreferenceList(id.ToString(), 2)
                .FirstOrDefault(node => !string.Equals(node, NodeId, StringComparison.Ordinal));

            if (successor is null) continue;

            var keys = bySuccessor.TryGetValue(successor, out var existing) ? existing : bySuccessor[successor] = [];

            // Capped per successor rather than in total, so one busy successor cannot crowd the
            // others out of the digest entirely.
            if (keys.Count < limit) keys.Add(id.ToString());
        }

        foreach (var (successor, keys) in bySuccessor)
        {
            var (alias, payload) = Serializer.Serialize(new KeyDigest(NodeId, keys));
            var frame = new WireEnvelope
            {
                Kind = WireKind.KeyDigest,
                FromNode = NodeId,
                MessageAlias = alias,
                Payload = payload,
            };

            try { await _transport.SendAsync(successor, frame, cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogDebug(ex, "Could not send a key digest to {NodeId}.", successor); }
        }
    }

    /// <summary>
    /// Activates the keys a node that has gone was holding, without waiting for traffic.
    /// </summary>
    /// <remarks>
    /// Only the keys that now belong here, and only ones not already running. A key that has moved
    /// somewhere else is that node's to warm, and it heard the same digest.
    /// </remarks>
    private void InheritFrom(string node)
    {
        if (!_inheritable.TryRemove(node, out var keys)) return;

        var warmed = 0;
        foreach (var key in keys)
        {
            if (!ActorId.TryParse(key, out var id) || !_cluster.IsLocal(id) || _cells.ContainsKey(id)) continue;

            // Through the ordinary send path, so an unregistered type fails the way it always does
            // rather than being swallowed by a background loop. Fire and forget: this is warming,
            // and a failure costs a cold activation later rather than anything worse.
            _ = TellAsync(id, new Warm(), cancellationToken: CancellationToken.None).AsTask().ContinueWith(
                static (task, state) =>
                {
                    var (logger, actor) = ((ILogger, ActorId))state!;
                    logger.LogDebug(task.Exception, "Could not warm {ActorId} inherited from a lost node.", actor);
                },
                (_logger, id), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

            warmed++;
        }

        if (warmed > 0)
            _logger.LogInformation("Activating {Count} actor(s) inherited from {NodeId}, which was lost.", warmed, node);
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

            // A forwarded ask whose reply never came. Without this the map grows for the life of
            // the process, one entry per client ask that timed out anywhere in the cluster.
            var stale = DateTimeOffset.UtcNow - Options.DefaultAskTimeout - TimeSpan.FromMinutes(1);
            foreach (var (correlation, proxied) in _proxiedAsks)
                if (proxied.At < stale) _proxiedAsks.TryRemove(correlation, out _);
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
    /// <summary>
    /// Stops the node once the cluster has decided it is on the losing side of a partition.
    /// </summary>
    /// <remarks>
    /// Continuing would mean serving actors this node no longer owns, while the winning side serves
    /// its own copies - which is the divergence the strategy exists to prevent. Stopping is not a
    /// failure to handle here; it is the handling.
    /// </remarks>
    private void OnSelfDowned(string because)
    {
        _logger.LogCritical("Node {NodeId} is stopping: {Because}.", NodeId, because);

        _ = Task.Run(async () =>
        {
            try { await StopAsync().ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogError(ex, "Stopping after a split brain threw."); }
        });
    }

    private void OnMembershipChanged(IReadOnlyList<ClusterMember> members)
    {
        if (_shutdown.IsCancellationRequested) return;

        // A node this one holds a digest for that is no longer on the ring has been lost, or has
        // left. Either way its keys are somebody's now, and this node knows which of them are its
        // own - which is the whole point of having been told in advance.
        if (Options.InheritanceDigestLimit > 0)
        {
            var ring = _cluster.Ring.Nodes;
            foreach (var node in _inheritable.Keys)
                if (!ring.Contains(node, StringComparer.Ordinal)) InheritFrom(node);
        }

        if (!Options.Cluster.RebalanceOnMembershipChange) return;

        var moved = 0;
        foreach (var (id, lazy) in _cells)
        {
            if (!lazy.IsValueCreated || _cluster.IsLocal(id)) continue;

            // A handoff needs somewhere to hand to. An unreachable owner is still on the ring -
            // deliberately - so without this the actor is deactivated here and unreachable there,
            // and nobody holds it until the ring moves again. Keeping it costs nothing and the next
            // membership change settles it either way.
            if (!_cluster.OwnerIsReachable(id)) continue;

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
