// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Collections.Concurrent;
using System.Net.Sockets;
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
    private readonly string _host;
    private readonly int _port;
    private readonly IMessageSerializer _serializer;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<WireEnvelope>> _pending = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();

    private TcpClient? _client;
    private NetworkStream? _stream;
    private Task? _readLoop;

    /// <summary>How this client identifies itself to the node. Must be unique among its clients.</summary>
    public string ClientId { get; }

    /// <summary>The type allow-list. Register every message and reply type before using them.</summary>
    public MessageTypeRegistry Types => _serializer.Types;

    /// <summary>Default timeout for <see cref="AskAsync{TResponse}"/>.</summary>
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>True while the connection is up.</summary>
    public bool IsConnected => _client?.Connected == true;

    public ActorNetClient(string host, int port, string? clientId = null, IMessageSerializer? serializer = null)
    {
        _host = host;
        _port = port;
        ClientId = clientId ?? $"client-{Guid.NewGuid():N}"[..19];
        _serializer = serializer ?? new JsonMessageSerializer();
    }

    /// <summary>Opens the connection. Called automatically on first use.</summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnected) return;

        await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsConnected) return;

            _client?.Dispose();
            _client = new TcpClient { NoDelay = true };
            await _client.ConnectAsync(_host, _port, cancellationToken).ConfigureAwait(false);
            _stream = _client.GetStream();
            _readLoop = Task.Run(() => ReadLoopAsync(_shutdown.Token), CancellationToken.None);
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
        }, cancellationToken).ConfigureAwait(false);
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
            }, cancellationToken).ConfigureAwait(false);

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

    private async Task SendAsync(WireEnvelope frame, CancellationToken cancellationToken)
    {
        // Serialized writes: several callers may be telling and asking at once, and interleaved
        // bytes would produce frames nobody sent.
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await FrameCodec.WriteAsync(_stream!, frame, cancellationToken).ConfigureAwait(false);
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
            FailPending(ex);
            return;
        }

        FailPending(new ActorNetException("The connection to the node closed before a reply arrived."));
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

        FailPending(new ActorNetException("The client was disposed before a reply arrived."));
        _shutdown.Dispose();
        _writeGate.Dispose();
        _connectGate.Dispose();
    }
}
