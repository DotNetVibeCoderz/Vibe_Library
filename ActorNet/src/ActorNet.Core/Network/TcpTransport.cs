// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading.Channels;
using ActorNet.Serialization;
using Microsoft.Extensions.Logging;

namespace ActorNet.Network;

/// <summary>How a node reaches another node.</summary>
public interface ITransport : IAsyncDisposable
{
    /// <summary>The port actually bound, which differs from the requested one when port 0 was asked for.</summary>
    int BoundPort { get; }

    /// <summary>Begins listening.</summary>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>Stops listening and closes every connection.</summary>
    Task StopAsync(CancellationToken cancellationToken);

    /// <summary>Sends to a node, resolving its address through the membership table.</summary>
    ValueTask SendAsync(string nodeId, WireEnvelope frame, CancellationToken cancellationToken);

    /// <summary>Sends to a raw address. Used for the join handshake, before a node id is known.</summary>
    ValueTask SendToAddressAsync(string host, int port, WireEnvelope frame, CancellationToken cancellationToken);
}

/// <summary>
/// The default transport: one TCP listener, plus one persistent outbound connection per peer.
/// </summary>
/// <remarks>
/// <para>
/// Connections are long-lived and per-peer, not per-message. A connection per message costs a
/// three-way handshake on every send, and on Windows it burns through the ephemeral port range
/// under load - the failure mode is a node that works in a demo and dies in a benchmark.
/// </para>
/// <para>
/// Sends go through a per-connection channel drained by a single writer loop. Two threads writing
/// to the same socket would interleave their bytes and produce frames neither of them sent, so
/// serializing writes is a correctness requirement, not a throughput choice.
/// </para>
/// <para>
/// Every node listens and every node dials, so replies travel on the sender's own outbound
/// connection to the peer rather than back down the inbound one. That keeps connection identity
/// out of the protocol at the cost of assuming peers are mutually dialable, which holds for the
/// cluster deployments this targets.
/// </para>
/// </remarks>
public sealed class TcpTransport : ITransport
{
    private readonly string _host;
    private readonly int _requestedPort;
    private readonly ClusterSecurityOptions _security;
    private readonly Func<WireEnvelope, Task> _onFrame;
    private readonly Func<string, (string Host, int Port)?> _resolveNode;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, PeerConnection> _peers = new(StringComparer.Ordinal);

    /// <summary>
    /// Inbound connections, keyed by the id the peer stamps on its frames.
    /// </summary>
    /// <remarks>
    /// Cluster peers are mutually dialable, so a reply to one normally travels on this node's own
    /// outbound connection. An external client is not: it dialled in, it is not in the membership
    /// table, and there is no address to dial back. Keeping its inbound connection addressable is
    /// what lets an SDK client use ask at all.
    /// </remarks>
    private readonly ConcurrentDictionary<string, InboundConnection> _inbound = new(StringComparer.Ordinal);

    private readonly CancellationTokenSource _shutdown = new();

    private TcpListener? _listener;
    private Task? _acceptLoop;

    /// <inheritdoc />
    public int BoundPort { get; private set; }

    public TcpTransport(
        string host,
        int port,
        Func<WireEnvelope, Task> onFrame,
        Func<string, (string Host, int Port)?> resolveNode,
        ILogger logger,
        ClusterSecurityOptions? security = null)
    {
        _host = host;
        _requestedPort = port;
        _security = security ?? new ClusterSecurityOptions();
        _onFrame = onFrame;
        _resolveNode = resolveNode;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var address = IPAddress.TryParse(_host, out var parsed) ? parsed : IPAddress.Any;
        _listener = new TcpListener(address, _requestedPort);
        _listener.Start();
        BoundPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_shutdown.Token), CancellationToken.None);
        _logger.LogInformation("Transport listening on {Address}:{Port}.", address, BoundPort);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _shutdown.CancelAsync().ConfigureAwait(false);
        _listener?.Stop();

        foreach (var peer in _peers.Values) await peer.DisposeAsync().ConfigureAwait(false);
        _peers.Clear();

        if (_acceptLoop is not null)
        {
            try { await _acceptLoop.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) when (ex is TimeoutException or OperationCanceledException) { /* forced */ }
        }
    }

    /// <inheritdoc />
    public ValueTask SendAsync(string nodeId, WireEnvelope frame, CancellationToken cancellationToken)
    {
        var address = _resolveNode(nodeId);
        if (address is null)
        {
            // Not a cluster member. If it dialled in and is still connected, answer down that
            // connection - this is the path every external SDK client's ask reply takes.
            if (_inbound.TryGetValue(nodeId, out var client) && client.IsConnected)
                return client.SendAsync(frame, cancellationToken);

            throw new NodeUnreachableException(nodeId);
        }

        var peer = _peers.GetOrAdd(nodeId, static (id, state) =>
            new PeerConnection(id, state.Address.Host, state.Address.Port, state.Logger, state.OnFrame, state.Security, state.Token),
            (Address: address.Value, Logger: _logger, OnFrame: _onFrame, Security: _security, Token: _shutdown.Token));

        return peer.SendAsync(frame, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask SendToAddressAsync(string host, int port, WireEnvelope frame, CancellationToken cancellationToken)
    {
        // The join handshake happens before the peer has a known id, so this path is deliberately
        // connectionless: dial, send, hang up. Everything after the handshake uses SendAsync.
        using var client = new TcpClient();
        await client.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
        await using var stream = await SecureChannel.ConnectAsync(client, host, _security, cancellationToken).ConfigureAwait(false);
        await FrameCodec.WriteAsync(stream, frame, cancellationToken).ConfigureAwait(false);
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener!.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            _ = Task.Run(() => ReadInboundAsync(client, cancellationToken), CancellationToken.None);
        }
    }

    private async Task ReadInboundAsync(TcpClient client, CancellationToken cancellationToken)
    {
        InboundConnection? registered = null;

        try
        {
            client.NoDelay = true;

            // Security first: a peer that cannot prove it belongs never gets a frame parsed.
            await using var stream = await SecureChannel.AcceptAsync(client, _security, cancellationToken).ConfigureAwait(false);
            var connection = new InboundConnection(stream, client);

            while (!cancellationToken.IsCancellationRequested)
            {
                var frame = await FrameCodec.ReadAsync(stream, cancellationToken).ConfigureAwait(false);
                if (frame is null) return;

                // Learned from the first frame. A peer that never names itself simply cannot be
                // replied to, which is the correct outcome rather than an error.
                if (registered is null && frame.FromNode is { Length: > 0 } from)
                {
                    registered = connection;
                    _inbound[from] = connection;
                }

                try
                {
                    await _onFrame(frame).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // One bad frame must not take down a connection carrying good ones.
                    _logger.LogError(ex, "Handling an inbound {Kind} frame from {FromNode} failed.", frame.Kind, frame.FromNode);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (ClusterAuthenticationException ex)
        {
            // A warning rather than an error: a refused peer is the feature working. It is also
            // the one line an operator needs when a rollout has mismatched settings.
            _logger.LogWarning("Refused an inbound connection: {Reason}", ex.Message);
        }
        catch (AuthenticationException ex)
        {
            _logger.LogWarning(ex, "TLS handshake failed on an inbound connection. Check that every node agrees on TLS.");
        }
        catch (Exception ex) when (ex is IOException or SocketException or EndOfStreamException or ObjectDisposedException)
        {
            _logger.LogDebug(ex, "Inbound connection closed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Inbound connection failed.");
        }
        finally
        {
            if (registered is not null)
            {
                registered.MarkClosed();

                // Remove only this exact connection: the peer may already have reconnected, and
                // evicting its live entry would break the reply path it just established.
                foreach (var (id, existing) in _inbound)
                    if (ReferenceEquals(existing, registered)) _inbound.TryRemove(new KeyValuePair<string, InboundConnection>(id, existing));
            }

            client.Dispose();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _shutdown.Dispose();
    }

    /// <summary>
    /// A connection someone else opened to this node, kept addressable so replies can go back
    /// down it.
    /// </summary>
    /// <remarks>
    /// The write gate is not optional. The reader loop and any number of reply-producing actor
    /// threads can all want this socket at once, and two concurrent writes would interleave their
    /// bytes into frames neither of them sent.
    /// </remarks>
    private sealed class InboundConnection(Stream stream, TcpClient client)
    {
        private readonly SemaphoreSlim _writeGate = new(1, 1);
        private volatile bool _closed;

        public bool IsConnected => !_closed && client.Connected;

        public void MarkClosed() => _closed = true;

        public async ValueTask SendAsync(WireEnvelope frame, CancellationToken cancellationToken)
        {
            await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await FrameCodec.WriteAsync(stream, frame, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _writeGate.Release();
            }
        }
    }

    /// <summary>
    /// One persistent outbound connection, with a writer loop and reconnect-on-failure.
    /// </summary>
    private sealed class PeerConnection : IAsyncDisposable
    {
        private const int MaxQueuedFrames = 8192;

        private readonly string _nodeId;
        private readonly string _host;
        private readonly int _port;
        private readonly ILogger _logger;
        private readonly Func<WireEnvelope, Task> _onFrame;
        private readonly ClusterSecurityOptions _security;
        private readonly Channel<WireEnvelope> _outbound;
        private readonly CancellationTokenSource _cts;
        private readonly Task _writerLoop;

        public PeerConnection(
            string nodeId, string host, int port, ILogger logger,
            Func<WireEnvelope, Task> onFrame, ClusterSecurityOptions security, CancellationToken shutdown)
        {
            _nodeId = nodeId;
            _security = security;
            _host = host;
            _port = port;
            _logger = logger;
            _onFrame = onFrame;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(shutdown);

            // Bounded: an unreachable peer must not let this node queue frames until it runs out
            // of memory. Once full, sends wait, and the caller feels the backpressure.
            _outbound = Channel.CreateBounded<WireEnvelope>(new BoundedChannelOptions(MaxQueuedFrames)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
            });

            _writerLoop = Task.Run(() => RunAsync(_cts.Token), CancellationToken.None);
        }

        public async ValueTask SendAsync(WireEnvelope frame, CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
            try
            {
                await _outbound.Writer.WriteAsync(frame, linked.Token).ConfigureAwait(false);
            }
            catch (ChannelClosedException ex)
            {
                throw new NodeUnreachableException(_nodeId, ex);
            }
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            var backoff = TimeSpan.FromMilliseconds(100);

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    using var client = new TcpClient { NoDelay = true };
                    await client.ConnectAsync(_host, _port, cancellationToken).ConfigureAwait(false);
                    await using var stream = await SecureChannel.ConnectAsync(client, _host, _security, cancellationToken).ConfigureAwait(false);

                    backoff = TimeSpan.FromMilliseconds(100);
                    _logger.LogDebug("Connected to {NodeId} at {Host}:{Port}.", _nodeId, _host, _port);

                    // The peer answers on this same socket for anything it chooses to send back,
                    // so drain it too rather than leaving bytes unread and stalling its writer.
                    var reader = Task.Run(() => ReadAsync(stream, cancellationToken), CancellationToken.None);

                    while (await _outbound.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        while (_outbound.Reader.TryRead(out var frame))
                            await FrameCodec.WriteAsync(stream, frame, cancellationToken).ConfigureAwait(false);
                    }

                    await reader.ConfigureAwait(false);
                    return;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Connection to {NodeId} at {Host}:{Port} failed; retrying in {Backoff}.", _nodeId, _host, _port, backoff);
                    try { await Task.Delay(backoff, cancellationToken).ConfigureAwait(false); }
                    catch (OperationCanceledException) { return; }

                    // Capped exponential backoff: a node that is down for an hour should not be
                    // dialed thousands of times a second, and one that is down for 200 ms should
                    // not wait a minute.
                    backoff = TimeSpan.FromMilliseconds(Math.Min(backoff.TotalMilliseconds * 2, 5_000));
                }
            }
        }

        private async Task ReadAsync(Stream stream, CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var frame = await FrameCodec.ReadAsync(stream, cancellationToken).ConfigureAwait(false);
                    if (frame is null) return;
                    await _onFrame(frame).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Shutting down.
            }
            catch (Exception ex) when (ex is IOException or SocketException or EndOfStreamException or ObjectDisposedException)
            {
                _logger.LogDebug(ex, "Outbound connection to {NodeId} closed by the peer.", _nodeId);
            }
        }

        public async ValueTask DisposeAsync()
        {
            _outbound.Writer.TryComplete();
            await _cts.CancelAsync().ConfigureAwait(false);
            try { await _writerLoop.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
            catch (Exception ex) when (ex is TimeoutException or OperationCanceledException) { /* forced */ }
            _cts.Dispose();
        }
    }
}
