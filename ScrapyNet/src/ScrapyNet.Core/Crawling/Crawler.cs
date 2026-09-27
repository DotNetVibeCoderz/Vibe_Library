using System.Globalization;
using Microsoft.Extensions.Logging;
using ScrapyNet.Engine;

namespace ScrapyNet;

/// <summary>Outcome of a finished crawl.</summary>
public sealed record CrawlResult(string SpiderName, string FinishReason, IReadOnlyDictionary<string, object?> Stats)
{
    public long ItemCount => GetLong("item_scraped_count");

    public long RequestCount => GetLong("downloader/request_count");

    public long ResponseCount => GetLong("response_received_count");

    public long ErrorCount => GetLong("log_count/ERROR");

    public TimeSpan Elapsed => TimeSpan.FromSeconds(Stats.TryGetValue("elapsed_time_seconds", out var v) && v is not null ? Convert.ToDouble(v, CultureInfo.InvariantCulture) : 0);

    private long GetLong(string key) => Stats.TryGetValue(key, out var v) && v is not null ? Convert.ToInt64(v, CultureInfo.InvariantCulture) : 0;

    public override string ToString() =>
        $"{SpiderName}: {FinishReason} — {ItemCount} items, {ResponseCount} responses in {Elapsed.TotalSeconds:0.00}s";
}

/// <summary>
/// Runs one spider: owns its settings, stats, signals, logging, extensions and engine. Port of
/// <c>scrapy.crawler.Crawler</c>.
/// </summary>
public sealed class Crawler
{
    private readonly Spider? _spiderInstance;
    private readonly ScrapyLoggerFactory? _providedLoggerFactory;
    private readonly List<object> _extensions = [];
    private ExecutionEngine? _engine;
    private int _started;

    /// <summary>A crawler that instantiates <paramref name="spiderType"/> when it starts.</summary>
    public Crawler(Type spiderType, Settings? settings = null, ScrapyLoggerFactory? loggerFactory = null)
    {
        if (!typeof(Spider).IsAssignableFrom(spiderType) || spiderType.IsAbstract)
            throw new ArgumentException($"{spiderType.FullName} is not a concrete Spider.", nameof(spiderType));
        SpiderType = spiderType;
        Settings = (settings ?? new Settings()).Copy();
        _providedLoggerFactory = loggerFactory;
        LoggerFactory = loggerFactory ?? ScrapyLoggerFactory.FromSettings(Settings);
        Stats = new MemoryStatsCollector();
        Signals = new SignalManager(LoggerFactory.CreateLogger("scrapynet.signalmanager"));
    }

    /// <summary>A crawler for an already-constructed spider (useful for spiders with constructor arguments).</summary>
    public Crawler(Spider spider, Settings? settings = null, ScrapyLoggerFactory? loggerFactory = null)
        : this(spider.GetType(), settings, loggerFactory)
    {
        _spiderInstance = spider;
    }

    public Type SpiderType { get; }

    public Spider? Spider { get; private set; }

    public Settings Settings { get; }

    public ScrapyLoggerFactory LoggerFactory { get; private set; }

    public IStatsCollector Stats { get; private set; }

    public SignalManager Signals { get; private set; }

    public RequestFingerprinter Fingerprinter { get; private set; } = new();

    public ExecutionEngine Engine => _engine ?? throw new InvalidOperationException("The crawler has not started.");

    /// <summary>Enabled extensions, in order.</summary>
    public IReadOnlyList<object> Extensions => _extensions;

    public bool IsCrawling => _engine is not null && Volatile.Read(ref _started) == 1 && Result is null;

    /// <summary>Set when the crawl finishes.</summary>
    public CrawlResult? Result { get; private set; }

    /// <summary>
    /// Runs the crawl to completion. <paramref name="arguments"/> are the spider arguments (<c>-a key=value</c>).
    /// Cancelling <paramref name="cancellationToken"/> stops the crawl gracefully (reason <c>shutdown</c>).
    /// </summary>
    public async Task<CrawlResult> CrawlAsync(IReadOnlyDictionary<string, string>? arguments = null, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _started, 1) == 1) throw new InvalidOperationException("Crawling already taking place.");

        var spider = _spiderInstance ?? (Spider)(Activator.CreateInstance(SpiderType)
            ?? throw new InvalidOperationException($"Cannot create {SpiderType.Name}."));
        spider.ApplyArguments(arguments);

        // Per-spider settings at spider priority, then freeze.
        var custom = new Settings(withDefaults: false);
        spider.ConfigureSettings(custom);
        foreach (var (key, value) in custom) Settings.Set(key, value, SettingPriority.Spider);
        Settings.Freeze();

        if (_providedLoggerFactory is null)
        {
            // Rebuild the logger from the final settings: the spider may have changed LOG_LEVEL.
            LoggerFactory.Dispose();
            LoggerFactory = ScrapyLoggerFactory.FromSettings(Settings);
            Signals.Logger = LoggerFactory.CreateLogger("scrapynet.signalmanager");
        }

        var statsType = Settings.GetTypeSetting(SettingKeys.StatsClass);
        if (statsType is not null) Stats = (IStatsCollector)(ComponentFactory.Create(statsType, this) ?? new MemoryStatsCollector());
        LoggerFactory.Stats = Stats;
        Fingerprinter = new RequestFingerprinter(Settings.GetList(SettingKeys.RequestFingerprinterIncludeHeaders));

        Spider = spider;
        spider.Bind(this);

        var logger = LoggerFactory.CreateLogger("scrapynet.crawler");
        logger.LogInformation("Scrapy.Net {Version} started (bot: {Bot})", typeof(Crawler).Assembly.GetName().Version?.ToString(3), Settings.GetString(SettingKeys.BotName));
        var overridden = Settings.Where(kv => Settings.GetPriority(kv.Key) > (int)SettingPriority.Default && kv.Value is not ComponentDictionary and not FeedCollection)
            .Select(kv => $"'{kv.Key}': {ItemAdapter.FormatValue(kv.Value)}").ToList();
        if (overridden.Count > 0) logger.LogInformation("Overridden settings:\n{{{Settings}}}", string.Join(",\n ", overridden));

        foreach (var entry in Settings.Extensions.GetEnabled())
        {
            var extension = ComponentFactory.Create<object>(entry, this);
            if (extension is not null) _extensions.Add(extension);
        }
        if (_extensions.Count > 0)
            logger.LogInformation("Enabled extensions:\n[{Extensions}]", string.Join(",\n ", _extensions.Select(e => $"'{e.GetType().FullName}'")));

        _engine = new ExecutionEngine(this);
        string reason;
        try
        {
            reason = await _engine.RunAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            foreach (var ext in _extensions)
            {
                if (ext is IAsyncDisposable ad) await ad.DisposeAsync().ConfigureAwait(false);
                else if (ext is IDisposable d) d.Dispose();
            }
        }

        Result = new CrawlResult(spider.Name, reason, Stats.GetStats());
        if (_providedLoggerFactory is null) LoggerFactory.Dispose();
        return Result;
    }

    /// <summary>Stops gracefully: no new requests, in-flight ones finish, pending ones are persisted with <c>JOBDIR</c>.</summary>
    public Task StopAsync(string reason = "shutdown") => _engine is null ? Task.CompletedTask : _engine.CloseSpiderAsync(reason);

    /// <summary>Stops immediately, cancelling in-flight downloads.</summary>
    public void Kill() => _engine?.Kill();

    /// <summary>Pauses scheduling of new downloads (in-flight ones finish).</summary>
    public void Pause() => _engine?.Pause();

    public void Resume() => _engine?.Unpause();
}
