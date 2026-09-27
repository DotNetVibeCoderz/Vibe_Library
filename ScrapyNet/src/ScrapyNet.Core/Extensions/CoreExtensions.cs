using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ScrapyNet.Engine;

namespace ScrapyNet.Extensions;

/// <summary>Records the basic crawl stats: item counts and response counts (Scrapy's <c>CoreStats</c>).</summary>
public sealed class CoreStats
{
    public CoreStats(Crawler crawler)
    {
        var stats = crawler.Stats;
        crawler.Signals.Connect(Signals.SpiderOpened, _ => stats.SetValue("start_time", DateTimeOffset.UtcNow));
        crawler.Signals.Connect(Signals.ItemScraped, _ => { });
        crawler.Signals.Connect(Signals.ResponseReceived, _ => { });
    }
}

/// <summary>
/// Logs progress every <c>LOGSTATS_INTERVAL</c> seconds: pages crawled and items scraped, with rates
/// (Scrapy's <c>LogStats</c>). Also keeps a rolling history that dashboards can chart.
/// </summary>
public sealed class LogStats : IAsyncDisposable
{
    private readonly Crawler _crawler;
    private readonly ILogger _logger;
    private readonly TimeSpan _interval;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;
    private long _pagesPrev;
    private long _itemsPrev;

    public LogStats(Crawler crawler, ILogger logger)
    {
        var seconds = crawler.Settings.GetDouble(SettingKeys.LogStatsInterval, 60);
        if (seconds <= 0) throw new NotConfiguredException();
        _crawler = crawler;
        _logger = logger;
        _interval = TimeSpan.FromSeconds(seconds);
        crawler.Signals.Connect(Signals.SpiderOpened, _ => _loop = RunAsync());
        crawler.Signals.Connect(Signals.SpiderClosed, e => Stop(e.Reason));
    }

    private async Task RunAsync()
    {
        using var timer = new PeriodicTimer(_interval);
        Log();
        try
        {
            while (await timer.WaitForNextTickAsync(_cts.Token).ConfigureAwait(false)) Log();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Log()
    {
        var pages = _crawler.Stats.GetCount("response_received_count");
        var items = _crawler.Stats.GetCount("item_scraped_count");
        var multiplier = 60.0 / _interval.TotalSeconds;
        var pageRate = (pages - _pagesPrev) * multiplier;
        var itemRate = (items - _itemsPrev) * multiplier;
        _pagesPrev = pages;
        _itemsPrev = items;
        _logger.LogInformation("Crawled {Pages} pages (at {PageRate:0} pages/min), scraped {Items} items (at {ItemRate:0} items/min)",
            pages, pageRate, items, itemRate);
    }

    private void Stop(string reason)
    {
        _cts.Cancel();
        var elapsed = _crawler.Stats.GetValue("elapsed_time_seconds");
        if (elapsed is double seconds && seconds > 0)
        {
            var pages = _crawler.Stats.GetCount("response_received_count");
            var items = _crawler.Stats.GetCount("item_scraped_count");
            _crawler.Stats.SetValue("responses_per_minute", Math.Round(pages / seconds * 60, 2));
            _crawler.Stats.SetValue("items_per_minute", Math.Round(items / seconds * 60, 2));
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        if (_loop is not null) await _loop.ConfigureAwait(false);
        _cts.Dispose();
    }
}

/// <summary>
/// Watches memory and closes the spider (<c>memusage_exceeded</c>) above <c>MEMUSAGE_LIMIT_MB</c>, warning
/// at <c>MEMUSAGE_WARNING_MB</c>. Enable with <c>MEMUSAGE_ENABLED</c>. Port of Scrapy's <c>MemoryUsage</c>.
/// </summary>
public sealed class MemoryUsage : IAsyncDisposable
{
    private readonly Crawler _crawler;
    private readonly ILogger _logger;
    private readonly long _limit;
    private readonly long _warning;
    private readonly TimeSpan _interval;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;
    private bool _warned;

    public MemoryUsage(Crawler crawler, ILogger logger)
    {
        var s = crawler.Settings;
        if (!s.GetBool(SettingKeys.MemUsageEnabled)) throw new NotConfiguredException();
        _crawler = crawler;
        _logger = logger;
        _limit = s.GetLong(SettingKeys.MemUsageLimitMb) * 1024 * 1024;
        _warning = s.GetLong(SettingKeys.MemUsageWarningMb) * 1024 * 1024;
        _interval = TimeSpan.FromSeconds(Math.Max(0.1, s.GetDouble(SettingKeys.MemUsageCheckIntervalSeconds, 60)));
        crawler.Signals.Connect(Signals.EngineStarted, _ =>
        {
            crawler.Stats.SetValue("memusage/startup", CurrentBytes());
            _loop = RunAsync();
        });
        crawler.Signals.Connect(Signals.SpiderClosed, _ => _cts.Cancel());
    }

    private static long CurrentBytes()
    {
        using var process = Process.GetCurrentProcess();
        return process.WorkingSet64;
    }

    private async Task RunAsync()
    {
        using var timer = new PeriodicTimer(_interval);
        try
        {
            while (await timer.WaitForNextTickAsync(_cts.Token).ConfigureAwait(false))
            {
                var bytes = CurrentBytes();
                _crawler.Stats.Max("memusage/max", bytes);
                if (_limit > 0 && bytes > _limit)
                {
                    _crawler.Stats.SetValue("memusage/limit_reached", 1);
                    _logger.LogError("Memory usage exceeded {Limit} MiB. Shutting down Scrapy.Net...", _limit / 1024 / 1024);
                    await _crawler.StopAsync("memusage_exceeded").ConfigureAwait(false);
                    return;
                }
                if (_warning > 0 && bytes > _warning && !_warned)
                {
                    _warned = true;
                    _crawler.Stats.SetValue("memusage/warning_reached", 1);
                    _logger.LogWarning("Memory usage reached {Warning} MiB", _warning / 1024 / 1024);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        if (_loop is not null) await _loop.ConfigureAwait(false);
        _cts.Dispose();
    }
}

/// <summary>
/// Closes the spider when a limit is reached: <c>CLOSESPIDER_TIMEOUT</c> (seconds), <c>CLOSESPIDER_ITEMCOUNT</c>,
/// <c>CLOSESPIDER_PAGECOUNT</c>, <c>CLOSESPIDER_ERRORCOUNT</c>, <c>CLOSESPIDER_TIMEOUT_NO_ITEM</c>.
/// Port of Scrapy's <c>CloseSpider</c> extension.
/// </summary>
public sealed class CloseSpiderExtension : IAsyncDisposable
{
    private readonly Crawler _crawler;
    private readonly double _timeout;
    private readonly int _itemCount;
    private readonly int _pageCount;
    private readonly int _errorCount;
    private readonly double _timeoutNoItem;
    private readonly CancellationTokenSource _cts = new();
    private int _items;
    private int _pages;
    private int _errors;
    private long _lastItemTicks = Stopwatch.GetTimestamp();

    public CloseSpiderExtension(Crawler crawler)
    {
        var s = crawler.Settings;
        _timeout = s.GetDouble(SettingKeys.CloseSpiderTimeout);
        _itemCount = s.GetInt(SettingKeys.CloseSpiderItemCount);
        _pageCount = s.GetInt(SettingKeys.CloseSpiderPageCount);
        _errorCount = s.GetInt(SettingKeys.CloseSpiderErrorCount);
        _timeoutNoItem = s.GetDouble(SettingKeys.CloseSpiderTimeoutNoItem);
        if (_timeout <= 0 && _itemCount <= 0 && _pageCount <= 0 && _errorCount <= 0 && _timeoutNoItem <= 0) throw new NotConfiguredException();
        _crawler = crawler;

        if (_itemCount > 0)
            crawler.Signals.Connect(Signals.ItemScraped, e =>
            {
                if (Interlocked.Increment(ref _items) == _itemCount) _ = crawler.StopAsync("closespider_itemcount");
            });
        if (_pageCount > 0)
            crawler.Signals.Connect(Signals.ResponseReceived, e =>
            {
                if (Interlocked.Increment(ref _pages) == _pageCount) _ = crawler.StopAsync("closespider_pagecount");
            });
        if (_errorCount > 0)
            crawler.Signals.Connect(Signals.SpiderError, e =>
            {
                if (Interlocked.Increment(ref _errors) == _errorCount) _ = crawler.StopAsync("closespider_errorcount");
            });
        if (_timeoutNoItem > 0)
            crawler.Signals.Connect(Signals.ItemScraped, _ => Interlocked.Exchange(ref _lastItemTicks, Stopwatch.GetTimestamp()));

        crawler.Signals.Connect(Signals.SpiderOpened, opened =>
        {
            if (_timeout > 0) _ = CloseAfterAsync(TimeSpan.FromSeconds(_timeout), "closespider_timeout");
            if (_timeoutNoItem > 0) _ = WatchNoItemAsync();
        });
        crawler.Signals.Connect(Signals.SpiderClosed, _ => _cts.Cancel());
    }

    private async Task CloseAfterAsync(TimeSpan delay, string reason)
    {
        try
        {
            await Task.Delay(delay, _cts.Token).ConfigureAwait(false);
            await _crawler.StopAsync(reason).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task WatchNoItemAsync()
    {
        var limit = TimeSpan.FromSeconds(_timeoutNoItem);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Min(_timeoutNoItem, 1)));
        try
        {
            while (await timer.WaitForNextTickAsync(_cts.Token).ConfigureAwait(false))
            {
                if (Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastItemTicks)) >= limit)
                {
                    await _crawler.StopAsync("closespider_timeout_no_item").ConfigureAwait(false);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _cts.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>Loads and saves <see cref="Spider.State"/> in <c>JOBDIR/spider.state</c> (Scrapy's <c>SpiderState</c>).</summary>
public sealed class SpiderStateExtension
{
    private readonly string _path;

    public SpiderStateExtension(Crawler crawler)
    {
        var jobDir = crawler.Settings.GetString(SettingKeys.JobDir);
        if (string.IsNullOrEmpty(jobDir)) throw new NotConfiguredException();
        _path = Path.Combine(jobDir, "spider.state");
        crawler.Signals.Connect(Signals.SpiderOpened, e =>
        {
            if (!File.Exists(_path)) return;
            var state = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(_path), ScrapyJson.Default) ?? [];
            e.Spider.State = state.ToDictionary(kv => kv.Key, kv => Settings.FromJson(kv.Value), StringComparer.Ordinal);
        });
        crawler.Signals.Connect(Signals.SpiderClosed, e =>
        {
            Directory.CreateDirectory(jobDir);
            File.WriteAllText(_path, JsonSerializer.Serialize(e.Spider.State, ScrapyJson.Indented));
        });
    }
}

/// <summary>One AutoThrottle adjustment, for charts.</summary>
public sealed record ThrottleSample(DateTimeOffset Time, string Slot, double Latency, double Delay, int Status);

/// <summary>
/// Adapts download delays to server latency (Scrapy's <c>AutoThrottle</c>): each slot's delay moves
/// towards <c>latency / AUTOTHROTTLE_TARGET_CONCURRENCY</c>, bounded by <c>DOWNLOAD_DELAY</c> below and
/// <c>AUTOTHROTTLE_MAX_DELAY</c> above. Error responses never shrink the delay, so a struggling server
/// is given more room rather than less.
/// </summary>
public sealed class AutoThrottle
{
    private readonly Crawler _crawler;
    private readonly ILogger _logger;
    private readonly double _minDelay;
    private readonly double _maxDelay;
    private readonly double _startDelay;
    private readonly double _target;
    private readonly bool _debug;
    private readonly List<ThrottleSample> _history = [];

    public AutoThrottle(Crawler crawler, ILogger logger)
    {
        var s = crawler.Settings;
        if (!s.GetBool(SettingKeys.AutoThrottleEnabled)) throw new NotConfiguredException();
        _crawler = crawler;
        _logger = logger;
        _minDelay = s.GetDouble(SettingKeys.DownloadDelay);
        _maxDelay = s.GetDouble(SettingKeys.AutoThrottleMaxDelay, 60);
        _startDelay = Math.Max(_minDelay, s.GetDouble(SettingKeys.AutoThrottleStartDelay, 5));
        _target = s.GetDouble(SettingKeys.AutoThrottleTargetConcurrency, 1);
        if (_target <= 0) throw new ArgumentException("AUTOTHROTTLE_TARGET_CONCURRENCY must be positive.");
        _debug = s.GetBool(SettingKeys.AutoThrottleDebug);
        crawler.Signals.Connect(Signals.ResponseDownloaded, OnResponse);
        crawler.Signals.Connect(Signals.RequestReachedDownloader, OnRequest);
    }

    /// <summary>Recent adjustments (last 2000).</summary>
    public IReadOnlyList<ThrottleSample> History
    {
        get
        {
            lock (_history) return [.. _history];
        }
    }

    private readonly HashSet<string> _initialized = new(StringComparer.Ordinal);

    private void OnRequest(RequestEventArgs e)
    {
        var key = e.Request.Meta.GetValueOrDefault(MetaKeys.DownloadSlot) as string;
        if (key is null) return;
        lock (_initialized)
        {
            if (!_initialized.Add(key)) return;
        }
        var slot = _crawler.Engine.Downloader.GetOrCreateSlot(key);
        slot.Delay = Math.Max(slot.Delay, _startDelay);
    }

    private void OnResponse(ResponseEventArgs e)
    {
        var request = e.Request;
        if (request.GetMeta(MetaKeys.AutoThrottleDontAdjustDelay, false)) return;
        if (request.Meta.GetValueOrDefault(MetaKeys.DownloadSlot) is not string key) return;
        if (request.Meta.GetValueOrDefault(MetaKeys.DownloadLatency) is not double latency) return;
        var slot = _crawler.Engine.Downloader.GetOrCreateSlot(key);

        var old = slot.Delay;
        var targetDelay = latency / _target;
        var newDelay = (old + targetDelay) / 2.0;
        newDelay = Math.Max(targetDelay, newDelay);
        newDelay = Math.Min(Math.Max(_minDelay, newDelay), _maxDelay);
        // Faster responses to errors are not a sign the server can take more load.
        if (e.Response.Status != 200 && newDelay <= old) newDelay = old;
        slot.Delay = newDelay;

        lock (_history)
        {
            _history.Add(new ThrottleSample(DateTimeOffset.UtcNow, key, latency, newDelay, e.Response.Status));
            if (_history.Count > 2000) _history.RemoveRange(0, _history.Count - 2000);
        }
        if (_debug)
            _logger.LogInformation("slot: {Slot} | conc:{Conc,2} | delay:{Delay,5:0} ms ({Diff:+0;-0}) | latency:{Latency,5:0} ms | size:{Size,6} bytes",
                key, slot.Transferring, newDelay * 1000, (newDelay - old) * 1000, latency * 1000, e.Response.Body.Length);
    }
}
