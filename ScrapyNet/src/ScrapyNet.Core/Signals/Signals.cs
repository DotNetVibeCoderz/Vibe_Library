using System.Collections.Immutable;
using Microsoft.Extensions.Logging;

namespace ScrapyNet;

/// <summary>A typed signal. Handlers receive <typeparamref name="TArgs"/>.</summary>
public sealed class Signal<TArgs>(string name)
{
    public string Name { get; } = name;

    public override string ToString() => Name;
}

public sealed record SpiderEventArgs(Spider Spider);

public sealed record SpiderClosedEventArgs(Spider Spider, string Reason);

public sealed record SpiderErrorEventArgs(Failure Failure, Response? Response, Spider Spider);

public sealed record RequestEventArgs(Request Request, Spider Spider);

public sealed record ResponseEventArgs(Response Response, Request Request, Spider Spider);

public sealed record ItemEventArgs(object Item, Response? Response, Spider Spider);

public sealed record ItemDroppedEventArgs(object Item, Response? Response, Exception Exception, Spider Spider);

public sealed record ItemErrorEventArgs(object Item, Response? Response, Exception Exception, Spider Spider);

public sealed record FeedSlotClosedEventArgs(string Uri, int ItemCount, Spider Spider);

public sealed record EngineEventArgs(Crawler Crawler);

/// <summary>
/// The built-in signals (Scrapy's <c>scrapy.signals</c>). Connect handlers with
/// <see cref="SignalManager.Connect{TArgs}(Signal{TArgs}, Func{TArgs, ValueTask})"/>.
/// </summary>
public static class Signals
{
    public static readonly Signal<EngineEventArgs> EngineStarted = new("engine_started");
    public static readonly Signal<EngineEventArgs> EngineStopped = new("engine_stopped");
    public static readonly Signal<SpiderEventArgs> SpiderOpened = new("spider_opened");

    /// <summary>The spider has nothing left to do. Throw <see cref="DontCloseSpiderException"/> from a handler (or schedule requests) to keep it open.</summary>
    public static readonly Signal<SpiderEventArgs> SpiderIdle = new("spider_idle");

    public static readonly Signal<SpiderClosedEventArgs> SpiderClosed = new("spider_closed");
    public static readonly Signal<SpiderErrorEventArgs> SpiderError = new("spider_error");
    public static readonly Signal<RequestEventArgs> RequestScheduled = new("request_scheduled");
    public static readonly Signal<RequestEventArgs> RequestDropped = new("request_dropped");
    public static readonly Signal<RequestEventArgs> RequestReachedDownloader = new("request_reached_downloader");
    public static readonly Signal<RequestEventArgs> RequestLeftDownloader = new("request_left_downloader");
    public static readonly Signal<ResponseEventArgs> ResponseReceived = new("response_received");
    public static readonly Signal<ResponseEventArgs> ResponseDownloaded = new("response_downloaded");
    public static readonly Signal<ItemEventArgs> ItemScraped = new("item_scraped");
    public static readonly Signal<ItemDroppedEventArgs> ItemDropped = new("item_dropped");
    public static readonly Signal<ItemErrorEventArgs> ItemError = new("item_error");
    public static readonly Signal<FeedSlotClosedEventArgs> FeedSlotClosed = new("feed_slot_closed");
    public static readonly Signal<SpiderEventArgs> FeedExporterClosed = new("feed_exporter_closed");
}

/// <summary>
/// Dispatches signals to handlers — the event backbone that extensions, pipelines, exporters and
/// monitoring hook into.
/// </summary>
/// <remarks>
/// Handlers are stored in immutable arrays swapped atomically on connect/disconnect, so sending a
/// signal is a lock-free read. A signal with no handlers costs one dictionary lookup, which matters for
/// the per-request signals.
/// </remarks>
public sealed class SignalManager(ILogger? logger = null)
{
    /// <summary>Logger for handler errors; replaceable after construction.</summary>
    public ILogger? Logger { get; set; } = logger;

    private readonly object _gate = new();
    private ImmutableDictionary<object, ImmutableArray<Func<object, ValueTask>>> _handlers =
        ImmutableDictionary<object, ImmutableArray<Func<object, ValueTask>>>.Empty.WithComparers(ReferenceEqualityComparer.Instance);

    /// <summary>Connects an async handler. Dispose the result to disconnect.</summary>
    public IDisposable Connect<TArgs>(Signal<TArgs> signal, Func<TArgs, ValueTask> handler)
    {
        Func<object, ValueTask> wrapped = args => handler((TArgs)args);
        lock (_gate)
        {
            var list = _handlers.TryGetValue(signal, out var existing) ? existing : ImmutableArray<Func<object, ValueTask>>.Empty;
            _handlers = _handlers.SetItem(signal, list.Add(wrapped));
        }
        return new Subscription(() => Disconnect(signal, wrapped));
    }

    /// <summary>Connects a synchronous handler.</summary>
    public IDisposable Connect<TArgs>(Signal<TArgs> signal, Action<TArgs> handler) =>
        Connect(signal, args =>
        {
            handler(args);
            return ValueTask.CompletedTask;
        });

    public bool HasHandlers<TArgs>(Signal<TArgs> signal) => _handlers.TryGetValue(signal, out var list) && !list.IsEmpty;

    /// <summary>
    /// Sends a signal to every handler in connection order. Handler exceptions are logged and do not
    /// stop the others. Returns true if any handler threw <see cref="DontCloseSpiderException"/>.
    /// </summary>
    public async ValueTask<bool> SendAsync<TArgs>(Signal<TArgs> signal, TArgs args)
    {
        if (!_handlers.TryGetValue(signal, out var list) || list.IsEmpty) return false;
        var dontClose = false;
        foreach (var handler in list)
        {
            try
            {
                await handler(args!).ConfigureAwait(false);
            }
            catch (DontCloseSpiderException)
            {
                dontClose = true;
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Error caught on signal handler for {Signal}", signal.Name);
            }
        }
        return dontClose;
    }

    public void DisconnectAll<TArgs>(Signal<TArgs> signal)
    {
        lock (_gate) _handlers = _handlers.Remove(signal);
    }

    private void Disconnect(object signal, Func<object, ValueTask> handler)
    {
        lock (_gate)
        {
            if (_handlers.TryGetValue(signal, out var list)) _handlers = _handlers.SetItem(signal, list.Remove(handler));
        }
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
