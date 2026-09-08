// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Collections.Concurrent;
using System.Net.Sockets;
using ActorNet.Cluster;
using ActorNet.Network;
using ActorNet.Serialization;

namespace ActorNet.Client;

/// <summary>
/// Talks to an ActorNet node from a process that is not itself a node.
/// </summary>
/// <remarks>
/// <para>
/// One persistent connection, not one per message. Dialling per message costs a handshake every
/// time and exhausts the ephemeral port range under load - and it makes ask impossible, because
/// the reply has nowhere to arrive.
/// </para>
/// <para>
/// The node addresses this client by the id in <see cref="ClientId"/>: it is not a cluster member,
/// so the node has no address to dial back and instead answers on this connection. That is why
/// asks work here at all, and why the client must keep reading even when it is only telling.
/// </para>
/// <para>
/// A client connects to <em>one</em> node. If the actor it addresses lives elsewhere in the
/// cluster, that node forwards it - so any node is a valid entry point, but this client does not
/// itself track membership.
/// </para>
/// </remarks>
public sealed class ActorNetClient : IAsyncDisposable
{
    private readonly IReadOnlyList<(string Host, int Port)> _endpoints;

    /// <summary>Which endpoint to try first. Sticky: it only moves when one fails.</summary>
    /// <remarks>
    /// Rotating on every connect would spread clients evenly and reconnect somewhere new after
    /// every blip, which is churn rather than balance - any node forwards by the ring anyway, so
    /// there is nothing to gain by moving and a connection to re-establish by moving.
    /// </remarks>
    private int _cursor;
    private readonly IMessageSerializer _serializer;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<WireEnvelope>> _pending = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();

    /// <summary>
    /// Whether this client believes the connection is usable.
    /// </summary>
    /// <remarks>
    /// <see cref="TcpClient.Connected"/> reports the result of the last I/O rather than the state
    /// of the socket, so it stays true after the peer has gone until something tries to use it.
    /// Trusting it meant a client whose node had stopped kept writing into a dead socket and waited
    /// out its own ask timeout instead of reconnecting. The reader knows first, so the reader
    /// decides.
    /// </remarks>
    private volatile bool _alive;

    private TcpClient? _client;
    private NetworkStream? _stream;
    private Task? _readLoop;

    // One extra connection per node the ring sends this client to. The first connection stays what
    // it was - somewhere to ask, and somewhere to fall back to - and these are opened on demand.
    private readonly ConcurrentDictionary<string, NodeLink> _direct = new(StringComparer.Ordinal);

    private volatile ClusterRoutes? _routes;
    private DateTimeOffset _routesTakenAt = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _routesGate = new(1, 1);

    /// <summary>How this client identifies itself to the node. Must be unique among its clients.</summary>
    public string ClientId { get; }

    /// <summary>The type allow-list. Register every message and reply type before using them.</summary>
    public MessageTypeRegistry Types => _serializer.Types;

    /// <summary>Default timeout for <see cref="AskAsync{TResponse}"/>.</summary>
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>True while the connection is up.</summary>
    public bool IsConnected => _alive && _client?.Connected == true;

    /// <summary>The endpoint currently in use, in <c>host:port</c> form, or null when not connected.</summary>
    public string? ConnectedTo { get; private set; }

    /// <summary>
    /// Whether to work out which node owns a key and send straight to it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Off by default. With it off a client sends everything to the node it is connected to, which
    /// forwards - correct, and one extra hop for every message whose actor lives elsewhere. With it
    /// on the client asks a node for the member table, builds the same ring the cluster uses, and
    /// opens a connection per node it actually addresses.
    /// </para>
    /// <para>
    /// It is an optimisation and never a requirement: every node accepts a message for any actor.
    /// A client whose view is stale sends to the wrong node and the message still arrives, which is
    /// what makes it safe to route by a view that is seconds behind.
    /// </para>
    /// </remarks>
    public bool ClusterAware { get; set; }

    /// <summary>How long a member table is used before it is asked for again.</summary>
    public TimeSpan RoutesRefreshAfter { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>The nodes this client currently holds a connection to.</summary>
    public IReadOnlyList<string> ConnectedNodes =>
        [.. (ConnectedTo is { } primary ? new[] { primary } : []), .. _direct.Keys.Order(StringComparer.Ordinal)];

    /// <summary>The endpoints this client may use, in the order they were given.</summary>
    public IReadOnlyList<string> Endpoints => _endpoints.Select(e => $"{e.Host}:{e.Port}").ToArray();

    /// <summary>Connects to one node.</summary>
    public ActorNetClient(string host, int port, string? clientId = null, IMessageSerializer? serializer = null)
        : this([$"{host}:{port}"], clientId, serializer)
    {
    }

    /// <summary>
    /// Connects to whichever of these nodes answers.
    /// </summary>
    /// <param name="endpoints">Nodes as <c>host:port</c>. Any of them will do.</param>
    /// <remarks>
    /// <para>
    /// A client bound to one node goes down with it, which is a strange property for a client of a
    /// cluster. It does not matter which node it reaches: a node that does not own the target key
    /// forwards by the ring, so every node is an equally correct entrance.
    /// </para>
    /// <para>
    /// Anything in flight when a connection drops still fails. Delivery is at-most-once, and
    /// re-sending a request whose reply was lost would quietly turn it into at-least-once - the
    /// caller knows whether its operation is safe to repeat and this does not.
    /// </para>
    /// </remarks>
    public ActorNetClient(IEnumerable<string> endpoints, string? clientId = null, IMessageSerializer? serializer = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        _endpoints = endpoints.Select(Parse).ToArray();
        if (_endpoints.Count == 0) throw new ArgumentException("At least one endpoint is required.", nameof(endpoints));

        ClientId = clientId ?? $"client-{Guid.NewGuid():N}"[..19];
        _serializer = serializer ?? new JsonMessageSerializer();

        // The client's own protocol types, registered here rather than left to the caller. The
        // allow-list is there to stop a peer choosing which type this process constructs, and these
        // are types this process already knows about - a caller having to name them would only mean
        // routing quietly not working for anyone who forgot.
        _serializer.Types.Register<ClusterView>();
        _serializer.Types.Register<ClusterViewMember>();

        static (string Host, int Port) Parse(string endpoint)
        {
            var colon = endpoint.LastIndexOf(':');
            if (colon <= 0 || !int.TryParse(endpoint.AsSpan(colon + 1), out var port))
                throw new FormatException($"Endpoint '{endpoint}' is not in 'host:port' form.");

            return (endpoint[..colon], port);
        }
    }

    /// <summary>
    /// Opens a connection to whichever endpoint answers. Called automatically on first use.
    /// </summary>
    /// <remarks>
    /// Tried in rotation from the last one that worked, so an ordinary reconnect goes back where it
    /// was and only a node that is actually gone costs a move.
    /// </remarks>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnected) return;

        await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsConnected) return;

            Exception? last = null;

            for (var attempt = 0; attempt < _endpoints.Count; attempt++)
            {
                var index = (_cursor + attempt) % _endpoints.Count;
                var (host, port) = _endpoints[index];

                try
                {
                    _client?.Dispose();
                    _client = new TcpClient { NoDelay = true };
                    await _client.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
                    _stream = _client.GetStream();
                    _readLoop = Task.Run(() => ReadLoopAsync(_shutdown.Token), CancellationToken.None);

                    _alive = true;
                    _cursor = index;
                    ConnectedTo = $"{host}:{port}";
                    return;
                }
                catch (Exception ex) when (ex is SocketException or IOException)
                {
                    last = ex;
                }
            }

            ConnectedTo = null;
            var message = $"None of the {_endpoints.Count} configured node(s) accepted a connection: {string.Join(", ", Endpoints)}.";
            throw last is null ? new ActorNetException(message) : new ActorNetException(message, last);
        }
        finally
        {
            _connectGate.Release();
        }
    }

    /// <summary>Sends a message and returns once the node has accepted it.</summary>
    public async Task TellAsync(ActorId target, object message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        await ConnectAsync(cancellationToken).ConfigureAwait(false);

        var (alias, payload) = _serializer.Serialize(message);
        await SendAsync(new WireEnvelope
        {
            Kind = WireKind.Message,
            Target = target.ToString(),
            MessageAlias = alias,
            Payload = payload,
            FromNode = ClientId,
        }, target, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sends a message and waits for the actor's reply.</summary>
    public async Task<TResponse> AskAsync<TResponse>(ActorId target, object message, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        await ConnectAsync(cancellationToken).ConfigureAwait(false);

        var window = timeout ?? DefaultTimeout;
        var correlationId = Guid.NewGuid().ToString("N");
        var pending = new TaskCompletionSource<WireEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[correlationId] = pending;

        try
        {
            var (alias, payload) = _serializer.Serialize(message);
            await SendAsync(new WireEnvelope
            {
                Kind = WireKind.AskRequest,
                Target = target.ToString(),
                MessageAlias = alias,
                Payload = payload,
                CorrelationId = correlationId,

                // Both fields carry this client's id: ReplyToNode is what the actor's reply is
                // routed by, and FromNode is what the node keys this connection under.
                ReplyToNode = ClientId,
                FromNode = ClientId,
            }, target, cancellationToken).ConfigureAwait(false);

            using var timeoutSource = new CancellationTokenSource(window);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

            WireEnvelope reply;
            try
            {
                reply = await pending.Task.WaitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
            {
                throw new AskTimeoutException(target, window);
            }

            if (reply.Kind == WireKind.AskFailure)
                throw new ActorNetException(reply.Error ?? $"Actor '{target}' failed while handling the request.");

            if (reply.MessageAlias is not { } replyAlias || reply.Payload is not { } replyPayload)
                throw new ActorNetException($"Actor '{target}' replied with an empty payload.");

            var materialized = _serializer.Deserialize(replyAlias, replyPayload);
            return materialized is TResponse typed
                ? typed
                : throw new AskReplyTypeMismatchException(target, typeof(TResponse), materialized.GetType());
        }
        finally
        {
            _pending.TryRemove(correlationId, out _);
        }
    }

    /// <summary>Registers a message type under an alias. Both ends must agree on the alias.</summary>
    public ActorNetClient RegisterMessage<T>(string? alias = null)
    {
        Types.Register<T>(alias);
        return this;
    }

    /// <summary>Registers every attributed message type in an assembly.</summary>
    public ActorNetClient RegisterMessagesFromAssembly(System.Reflection.Assembly assembly)
    {
        Types.RegisterFromAssembly(assembly);
        return this;
    }

    /// <summary>Sends a frame, to the owner of <paramref name="target"/> when routing is on.</summary>
    private async Task SendAsync(WireEnvelope frame, ActorId target, CancellationToken cancellationToken)
    {
        if (!ClusterAware)
        {
            await SendAsync(frame, cancellationToken).ConfigureAwait(false);
            return;
        }

        var owner = await OwnerLinkAsync(target, cancellationToken).ConfigureAwait(false);
        if (owner is null)
        {
            // No view, or the owner is the node already connected to. Either way this is the
            // ordinary path, and a client with no view is a client that forwards - not one that
            // fails.
            await SendAsync(frame, cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            await owner.SendAsync(frame, cancellationToken).ConfigureAwait(false);
        }
        catch (ActorNetException)
        {
            // The owner went away between being named and being written to. Falling back to the
            // first connection is not a workaround - forwarding is what a node does for any actor
            // it does not own, so the message still arrives.
            _direct.TryRemove(owner.Endpoint, out _);
            await owner.DisposeAsync().ConfigureAwait(false);
            Invalidate();

            await SendAsync(frame, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The connection to whoever owns this key, or null to use the first connection.</summary>
    private async Task<NodeLink?> OwnerLinkAsync(ActorId target, CancellationToken cancellationToken)
    {
        var routes = await RoutesAsync(cancellationToken).ConfigureAwait(false);
        if (routes is null || routes.Ring.IsEmpty) return null;

        var owner = routes.Ring.OwnerOf(target.ToString());
        if (!routes.Endpoints.TryGetValue(owner, out var endpoint)) return null;

        // Already connected to it. A second connection to the same node would double the sockets
        // for nothing.
        if (string.Equals(endpoint, ConnectedTo, StringComparison.Ordinal)) return null;

        if (_direct.TryGetValue(endpoint, out var existing) && existing.IsConnected) return existing;

        var separator = endpoint.LastIndexOf(':');
        var host = endpoint[..separator];
        var port = int.Parse(endpoint[(separator + 1)..], System.Globalization.CultureInfo.InvariantCulture);

        var link = new NodeLink(endpoint, _pending, _shutdown.Token);
        try
        {
            await link.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A node named by the view that will not accept a connection. Forwarding still works,
            // so this is a slower message rather than a failed one.
            await link.DisposeAsync().ConfigureAwait(false);
            return null;
        }

        var winner = _direct.GetOrAdd(endpoint, link);
        if (!ReferenceEquals(winner, link)) await link.DisposeAsync().ConfigureAwait(false);

        return winner;
    }

    /// <summary>The member table, asked for again once it is old enough.</summary>
    private async Task<ClusterRoutes?> RoutesAsync(CancellationToken cancellationToken)
    {
        var current = _routes;
        if (current is not null && DateTimeOffset.UtcNow - _routesTakenAt < RoutesRefreshAfter) return current;

        if (!await _routesGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            // Somebody else is asking. Routing by a view a moment out of date is the whole premise,
            // so waiting for theirs would cost more than it saves.
            return current;
        }

        try
        {
            if (_routes is { } fresh && DateTimeOffset.UtcNow - _routesTakenAt < RoutesRefreshAfter) return fresh;

            var view = await AskClusterViewAsync(cancellationToken).ConfigureAwait(false);
            if (view is null) return current;

            _routes = new ClusterRoutes(
                new Cluster.HashRing(view.Members.Select(m => m.NodeId), view.VirtualNodes),
                view.Members.ToDictionary(m => m.NodeId, m => $"{m.Host}:{m.Port}", StringComparer.Ordinal));

            _routesTakenAt = DateTimeOffset.UtcNow;
            return _routes;
        }
        finally
        {
            _routesGate.Release();
        }
    }

    private void Invalidate() => _routesTakenAt = DateTimeOffset.MinValue;

    /// <summary>Asks the node this client is connected to for the member table.</summary>
    private async Task<ClusterView?> AskClusterViewAsync(CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        var pending = new TaskCompletionSource<WireEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[correlationId] = pending;

        try
        {
            await SendAsync(new WireEnvelope
            {
                Kind = WireKind.ClusterViewRequest,
                CorrelationId = correlationId,
                ReplyToNode = ClientId,
                FromNode = ClientId,
            }, cancellationToken).ConfigureAwait(false);

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

            var reply = await pending.Task.WaitAsync(linked.Token).ConfigureAwait(false);
            if (reply.MessageAlias is not { } alias || reply.Payload is not { } payload) return null;

            return _serializer.Deserialize(alias, payload) as ClusterView;
        }
        catch (Exception)
        {
            // A node that will not answer leaves the client forwarding, which is what it did before
            // routing existed. Nothing here is worth failing a send over.
            return null;
        }
        finally
        {
            _pending.TryRemove(correlationId, out _);
        }
    }

    private async Task SendAsync(WireEnvelope frame, CancellationToken cancellationToken)
    {
        // Serialized writes: several callers may be telling and asking at once, and interleaved
        // bytes would produce frames nobody sent.
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await FrameCodec.WriteAsync(_stream!, frame, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            // A write that fails has told us more than TcpClient.Connected ever will. Marking the
            // connection dead here is what makes the next call reconnect rather than write into the
            // same socket again.
            _alive = false;

            // Wrapped, because a caller should not have to catch three socket types to handle "the
            // node went away" - which is an ordinary event for a client of a cluster, not an
            // exceptional one. The cause is kept.
            throw new ActorNetException($"The connection to {ConnectedTo ?? "the node"} failed while sending.", ex);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var frame = await FrameCodec.ReadAsync(_stream!, cancellationToken).ConfigureAwait(false);
                if (frame is null) break;

                if (frame.CorrelationId is { } id && _pending.TryRemove(id, out var pending))
                    pending.TrySetResult(frame);
            }
        }
        catch (OperationCanceledException)
        {
            // Disposing.
        }
        catch (Exception ex)
        {
            _alive = false;
            ConnectedTo = null;

            // Same reason as the write path: one exception type for a connection that went away,
            // with whatever the socket said attached to it.
            FailPending(ex as ActorNetException
                ?? new ActorNetException("The connection to the node failed while waiting for a reply.", ex));
            return;
        }

        _alive = false;
        ConnectedTo = null;
        FailPending(new ActorNetException("The connection to the node closed before a reply arrived."));
    }

    /// <summary>A ring and the addresses behind it, taken together because neither is any use alone.</summary>
    private sealed record ClusterRoutes(Cluster.HashRing Ring, IReadOnlyDictionary<string, string> Endpoints);

    /// <summary>
    /// One extra connection, to a node the ring sent this client to.
    /// </summary>
    /// <remarks>
    /// Replies come back on whichever connection the request went out on, so every link completes
    /// the same pending-ask table. That is what lets these be opened and dropped freely: a link is
    /// a route, not a session.
    /// </remarks>
    private sealed class NodeLink(
        string endpoint,
        ConcurrentDictionary<string, TaskCompletionSource<WireEnvelope>> pending,
        CancellationToken shutdown)
    {
        private readonly SemaphoreSlim _gate = new(1, 1);

        private TcpClient? _client;
        private NetworkStream? _stream;
        private Task? _readLoop;
        private volatile bool _alive;

        public string Endpoint { get; } = endpoint;

        public bool IsConnected => _alive && _client?.Connected == true;

        public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken)
        {
            _client = new TcpClient { NoDelay = true };
            await _client.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);

            _stream = _client.GetStream();
            _alive = true;
            _readLoop = Task.Run(() => ReadLoopAsync(shutdown), CancellationToken.None);
        }

        public async Task SendAsync(WireEnvelope frame, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await FrameCodec.WriteAsync(_stream!, frame, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
                _alive = false;
                throw new ActorNetException($"The connection to {Endpoint} failed while sending.", ex);
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task ReadLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var frame = await FrameCodec.ReadAsync(_stream!, cancellationToken).ConfigureAwait(false);
                    if (frame is null) break;

                    if (frame.CorrelationId is { } id && pending.TryRemove(id, out var waiting))
                        waiting.TrySetResult(frame);
                }
            }
            catch (Exception)
            {
                // Reported to whoever was waiting, below.
            }

            _alive = false;

            // Deliberately not failing the pending asks. They are shared with every other link, and
            // there is nothing here that says which of them went out on this one - failing them all
            // would take down asks travelling on connections that are perfectly healthy. An ask
            // whose reply was lost with this link times out instead, which is slower and true.
        }

        public async ValueTask DisposeAsync()
        {
            _alive = false;
            _stream?.Dispose();
            _client?.Dispose();

            if (_readLoop is not null)
            {
                try { await _readLoop.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
                catch (Exception ex) when (ex is TimeoutException or OperationCanceledException) { /* forced */ }
            }

            _gate.Dispose();
        }
    }

    private void FailPending(Exception cause)
    {
        foreach (var (id, pending) in _pending)
        {
            if (_pending.TryRemove(id, out _)) pending.TrySetException(cause);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _shutdown.CancelAsync().ConfigureAwait(false);
        _stream?.Dispose();
        _client?.Dispose();

        if (_readLoop is not null)
        {
            try { await _readLoop.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
            catch (Exception ex) when (ex is TimeoutException or OperationCanceledException) { /* forced */ }
        }

        foreach (var link in _direct.Values) await link.DisposeAsync().ConfigureAwait(false);
        _direct.Clear();

        FailPending(new ActorNetException("The client was disposed before a reply arrived."));
        _shutdown.Dispose();
        _writeGate.Dispose();
        _connectGate.Dispose();
        _routesGate.Dispose();
    }
}
