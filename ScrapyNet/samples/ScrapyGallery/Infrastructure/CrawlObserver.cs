using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using ScrapyNet;

namespace ScrapyGallery.Infrastructure;

/// <summary>A page in the crawl web.</summary>
public sealed class WebNode(string url, string? parent, int depth, long arrivedTicks)
{
    public string Url { get; } = url;
    public string? Parent { get; } = parent;
    public int Depth { get; } = depth;

    /// <summary>0 = scheduled, -1 = failed, otherwise the HTTP status.</summary>
    public int Status { get; set; }

    /// <summary>When the node appeared (Stopwatch ticks), for the grow-in animation.</summary>
    public long ArrivedTicks { get; set; } = arrivedTicks;

    public bool Cached { get; set; }
}

/// <summary>A point in a named series for the Charts tab.</summary>
public readonly record struct SeriesPoint(double X, double Y);

/// <summary>A log line with its level, for colouring.</summary>
public sealed record LogLine(string Text, LogLevel Level);

/// <summary>
/// Collects everything the gallery visualizes about a run: the crawl web, counters, a timeline,
/// scraped items, log lines, extra series and output. Crawler threads write; the UI polls a snapshot
/// ten times a second, which keeps all cross-thread traffic in one place.
/// </summary>
public sealed class CrawlObserver
{
    private readonly object _gate = new();
    private readonly Dictionary<string, WebNode> _nodes = new(StringComparer.Ordinal);
    private readonly List<WebNode> _order = [];
    private readonly List<Dictionary<string, string>> _items = [];
    private readonly List<string> _columns = [];
    private readonly List<LogLine> _log = [];
    private readonly Dictionary<string, List<SeriesPoint>> _series = new(StringComparer.Ordinal);
    private readonly Dictionary<int, long> _statuses = [];
    private readonly List<(double T, long Responses, long Items)> _timeline = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _requests;
    private long _responses;
    private long _items2;
    private long _dropped;
    private long _errors;
    private long _bytes;
    private long _retries;
    private long _redirects;
    private long _cacheHits;

    public CrawlObserver()
    {
        LoggerFactory = new ScrapyLoggerFactory(LogLevel.Information, console: false);
        LoggerFactory.LogWritten += entry =>
        {
            lock (_gate)
            {
                _log.Add(new LogLine($"{entry.Timestamp.LocalDateTime:HH:mm:ss.fff} {entry.LevelName,-7} {ShortCategory(entry.Category)}: {entry.Message}", entry.Level));
                if (_log.Count > 1500) _log.RemoveRange(0, 300);
            }
        };
    }

    /// <summary>Logger factory to hand to crawlers so their log lines reach the Log tab.</summary>
    public ScrapyLoggerFactory LoggerFactory { get; }

    public int Version { get; private set; }

    public TimeSpan Elapsed => _clock.Elapsed;

    public bool Finished { get; set; }

    /// <summary>Show crawled items in the Items tab (off when a demo shows its own results there).</summary>
    public bool CaptureCrawlItems { get; set; } = true;

    public string? Output { get; private set; }

    public List<string> Images { get; } = [];

    /// <summary>Subscribes to a crawler's signals. Call before it starts.</summary>
    public void Attach(Crawler crawler)
    {
        crawler.Signals.Connect(Signals.RequestScheduled, e => OnScheduled(e.Request));
        crawler.Signals.Connect(Signals.RequestReachedDownloader, _ => Interlocked.Increment(ref _requests));
        // The raw status is known as soon as the download completes (before middlewares retry or redirect).
        crawler.Signals.Connect(Signals.ResponseDownloaded, e =>
        {
            lock (_gate)
            {
                if (_nodes.TryGetValue(UrlUtils.StripFragment(e.Request.Url), out var node) && node.Status <= 0) node.Status = e.Response.Status;
            }
        });
        // Left the downloader without a response: a real download failure (not merely never fetched).
        crawler.Signals.Connect(Signals.RequestLeftDownloader, e =>
        {
            lock (_gate)
            {
                if (_nodes.TryGetValue(UrlUtils.StripFragment(e.Request.Url), out var node) && node.Status == 0)
                {
                    node.Status = -1;
                    Version++;
                }
            }
        });
        crawler.Signals.Connect(Signals.ResponseReceived, e => OnResponse(e.Response, e.Request));
        crawler.Signals.Connect(Signals.ItemScraped, e =>
        {
            if (CaptureCrawlItems) AddItem(e.Item);
            else Interlocked.Increment(ref _items2);
        });
        crawler.Signals.Connect(Signals.ItemDropped, _ => Interlocked.Increment(ref _dropped));
        crawler.Signals.Connect(Signals.SpiderError, _ => Interlocked.Increment(ref _errors));
        crawler.Signals.Connect(Signals.SpiderClosed, e =>
        {
            lock (_gate)
            {
                _retries += Convert.ToInt64(e.Spider.Crawler.Stats.GetCount("retry/count"));
                _redirects += Convert.ToInt64(e.Spider.Crawler.Stats.GetCount("downloader/redirect_count"));
                Version++;
            }
        });
    }

    private void OnScheduled(Request request)
    {
        var url = UrlUtils.StripFragment(request.Url);
        var referer = request.Headers.Get("Referer");
        lock (_gate)
        {
            if (_nodes.ContainsKey(url)) return;
            string? parent = referer is not null && _nodes.ContainsKey(referer) ? referer : null;
            var depth = parent is null ? 0 : _nodes[parent].Depth + 1;
            var node = new WebNode(url, parent, depth, _clock.ElapsedTicks);
            _nodes[url] = node;
            _order.Add(node);
            Version++;
        }
    }

    private void OnResponse(Response response, Request request)
    {
        Interlocked.Increment(ref _responses);
        Interlocked.Add(ref _bytes, response.Body.Length);
        lock (_gate)
        {
            _statuses[response.Status] = _statuses.GetValueOrDefault(response.Status) + 1;
            if (response.Flags.Contains("cached")) _cacheHits++;
            // Redirects land on a new URL: attach it to the node that started the chain.
            var key = UrlUtils.StripFragment(request.Url);
            var original = request.Meta.GetValueOrDefault(MetaKeys.RedirectUrls) is List<string> chain && chain.Count > 0 ? chain[0] : key;
            if (!_nodes.TryGetValue(original, out var node) && !_nodes.TryGetValue(key, out node))
            {
                // Downloaded outside the scheduler (robots.txt, media): hang it off the first page.
                var root = _order.FirstOrDefault()?.Url;
                node = new WebNode(key, root, root is null ? 0 : 1, _clock.ElapsedTicks);
                _nodes[key] = node;
                _order.Add(node);
            }
            node.Status = response.Status;
            node.Cached = response.Flags.Contains("cached");
            node.ArrivedTicks = _clock.ElapsedTicks;
            Version++;
        }
    }

    /// <summary>Adds an item to the Items tab (crawl items arrive here automatically).</summary>
    public void AddItem(object item)
    {
        Interlocked.Increment(ref _items2);
        var row = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (k, v) in ItemAdapter.For(item).AsDictionary()) row[k] = Format(v);
        lock (_gate)
        {
            foreach (var k in row.Keys) if (!_columns.Contains(k)) _columns.Add(k);
            if (_items.Count < 1000) _items.Add(row);
            Version++;
        }
    }

    public void AddPoint(string series, double x, double y)
    {
        lock (_gate)
        {
            if (!_series.TryGetValue(series, out var list)) _series[series] = list = [];
            list.Add(new SeriesPoint(x, y));
            Version++;
        }
    }

    public void AppendOutput(string text)
    {
        lock (_gate)
        {
            Output = Output is null ? text : Output + Environment.NewLine + text;
            Version++;
        }
    }

    public void AddImage(string path)
    {
        lock (_gate)
        {
            Images.Add(path);
            Version++;
        }
    }

    public void Log(string message) =>
        LoggerFactory.CreateLogger("gallery").LogInformation("{Message}", message);

    /// <summary>Samples the timeline (called by the UI timer while running).</summary>
    public void Tick()
    {
        lock (_gate)
        {
            _timeline.Add((_clock.Elapsed.TotalSeconds, Interlocked.Read(ref _responses), Interlocked.Read(ref _items2)));
            if (_timeline.Count > 2000) _timeline.RemoveAt(0);
        }
    }

    public ObserverSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new ObserverSnapshot(
                [.. _order],
                [.. _items],
                [.. _columns],
                [.. _log],
                _series.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<SeriesPoint>)[.. kv.Value]),
                new Dictionary<int, long>(_statuses),
                [.. _timeline],
                Interlocked.Read(ref _requests),
                Interlocked.Read(ref _responses),
                Interlocked.Read(ref _items2),
                Interlocked.Read(ref _dropped),
                Interlocked.Read(ref _errors),
                Interlocked.Read(ref _bytes),
                _retries,
                _redirects,
                _cacheHits,
                _clock.Elapsed,
                Output,
                [.. Images],
                Finished);
        }
    }

    private static string ShortCategory(string category) => category.Contains('.') ? category[(category.LastIndexOf('.') + 1)..] : category;

    private static string Format(object? value) => value switch
    {
        null => "",
        string s => s,
        decimal d => d.ToString("0.00", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        System.Collections.IEnumerable e => string.Join(", ", e.Cast<object?>().Select(Format)),
        _ => value.ToString() ?? "",
    };
}

public sealed record ObserverSnapshot(
    IReadOnlyList<WebNode> Nodes,
    IReadOnlyList<Dictionary<string, string>> Items,
    IReadOnlyList<string> Columns,
    IReadOnlyList<LogLine> Log,
    IReadOnlyDictionary<string, IReadOnlyList<SeriesPoint>> Series,
    IReadOnlyDictionary<int, long> Statuses,
    IReadOnlyList<(double T, long Responses, long Items)> Timeline,
    long Requests,
    long Responses,
    long ItemCount,
    long Dropped,
    long Errors,
    long Bytes,
    long Retries,
    long Redirects,
    long CacheHits,
    TimeSpan Elapsed,
    string? Output,
    IReadOnlyList<string> Images,
    bool Finished);
