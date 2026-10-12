using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using ScrapyNet.Engine;
using StackExchange.Redis;

namespace ScrapyNet.Redis;

/// <summary>Setting keys of the Redis integration (the names scrapy-redis uses).</summary>
public static class RedisSettingKeys
{
    /// <summary>Connection: <c>localhost:6379</c>, a StackExchange.Redis configuration string, or <c>redis://[:password@]host:port[/db]</c>.</summary>
    public const string RedisUrl = "REDIS_URL";
    /// <summary>Queue key; <c>{spider}</c> is the spider name. Default <c>{spider}:requests</c>.</summary>
    public const string QueueKey = "SCHEDULER_QUEUE_KEY";
    /// <summary><c>priority</c> (sorted set, default), <c>fifo</c> or <c>lifo</c> (lists).</summary>
    public const string QueueClass = "SCHEDULER_QUEUE_CLASS";
    /// <summary>Duplicate filter key. Default <c>{spider}:dupefilter</c>.</summary>
    public const string DupeFilterKey = "SCHEDULER_DUPEFILTER_KEY";
    /// <summary>Keep the queue and the filter when the spider closes (default false: they are cleared).</summary>
    public const string Persist = "SCHEDULER_PERSIST";
    /// <summary>Clear the queue and the filter when the spider opens (default false).</summary>
    public const string FlushOnStart = "SCHEDULER_FLUSH_ON_START";
    /// <summary>Start URL key of a <see cref="RedisSpider"/>. Default <c>{spider}:start_urls</c>.</summary>
    public const string StartUrlsKey = "REDIS_START_URLS_KEY";
    /// <summary>Read start URLs from a set instead of a list (default false).</summary>
    public const string StartUrlsAsSet = "REDIS_START_URLS_AS_SET";
    /// <summary>Start URLs fetched per idle tick (default <c>CONCURRENT_REQUESTS</c>).</summary>
    public const string StartUrlsBatchSize = "REDIS_START_URLS_BATCH_SIZE";
    /// <summary>Seconds a <see cref="RedisSpider"/> may stay idle before closing; 0 (default) waits forever.</summary>
    public const string MaxIdleTimeBeforeClose = "MAX_IDLE_TIME_BEFORE_CLOSE";
    /// <summary>List the <see cref="RedisPipeline"/> pushes items to. Default <c>{spider}:items</c>.</summary>
    public const string ItemsKey = "REDIS_ITEMS_KEY";
}

/// <summary>Shared multiplexers, one per connection string (StackExchange.Redis connections are meant to be shared).</summary>
public static class RedisConnections
{
    private static readonly ConcurrentDictionary<string, Lazy<ConnectionMultiplexer>> Multiplexers = new(StringComparer.Ordinal);

    public static IConnectionMultiplexer Get(Settings settings) => Get(settings.GetString(RedisSettingKeys.RedisUrl) ?? "localhost:6379");

    public static IConnectionMultiplexer Get(string url) =>
        Multiplexers.GetOrAdd(url, u => new Lazy<ConnectionMultiplexer>(() => ConnectionMultiplexer.Connect(ToConfiguration(u)))).Value;

    /// <summary>Accepts <c>redis://</c> and <c>rediss://</c> URLs as well as native configuration strings.</summary>
    public static string ToConfiguration(string url)
    {
        if (!url.StartsWith("redis://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("rediss://", StringComparison.OrdinalIgnoreCase)) return url;
        var uri = new Uri(url);
        var parts = new List<string> { $"{uri.Host}:{(uri.Port > 0 ? uri.Port : 6379)}" };
        if (uri.UserInfo is { Length: > 0 } info)
        {
            var colon = info.IndexOf(':');
            var user = colon < 0 ? "" : Uri.UnescapeDataString(info[..colon]);
            var password = Uri.UnescapeDataString(colon < 0 ? info : info[(colon + 1)..]);
            if (user.Length > 0) parts.Add("user=" + user);
            parts.Add("password=" + password);
        }
        if (uri.AbsolutePath.Trim('/') is { Length: > 0 } db) parts.Add("defaultDatabase=" + db);
        if (uri.Scheme.Equals("rediss", StringComparison.OrdinalIgnoreCase)) parts.Add("ssl=true");
        parts.Add("abortConnect=false");
        return string.Join(",", parts);
    }

    /// <summary>Closes every cached connection.</summary>
    public static void CloseAll()
    {
        foreach (var lazy in Multiplexers.Values)
            if (lazy.IsValueCreated) lazy.Value.Dispose();
        Multiplexers.Clear();
    }

    internal static string Key(string? template, string fallback, Spider spider) =>
        (template ?? fallback).Replace("{spider}", spider.Name, StringComparison.Ordinal).Replace("%(spider)s", spider.Name, StringComparison.Ordinal)
            .Replace("%(name)s", spider.Name, StringComparison.Ordinal);
}

/// <summary>
/// Duplicate filter shared by every worker: fingerprints in a Redis set (scrapy-redis' <c>RFPDupeFilter</c>).
/// </summary>
public sealed class RedisDupeFilter(Crawler crawler) : IDupeFilter
{
    private readonly ILogger _logger = crawler.LoggerFactory.CreateLogger("scrapynet.redis.dupefilter");
    private IDatabase? _db;
    private RedisKey _key;
    private bool _logged;

    /// <summary>The set holding fingerprints.</summary>
    public string Key => _key.ToString();

    public ValueTask OpenAsync(Spider spider)
    {
        _db = RedisConnections.Get(crawler.Settings).GetDatabase();
        _key = RedisConnections.Key(crawler.Settings.GetString(RedisSettingKeys.DupeFilterKey), "{spider}:dupefilter", spider);
        return ValueTask.CompletedTask;
    }

    public bool RequestSeen(Request request) => !_db!.SetAdd(_key, crawler.Fingerprinter.Fingerprint(request).ToHex());

    public void Log(Request request, Spider spider)
    {
        if (!_logged)
        {
            _logger.LogDebug("Filtered duplicate request {Request} - no more duplicates will be shown (see DUPEFILTER_DEBUG to show all duplicates)", request);
            _logged = true;
        }
        crawler.Stats.Inc("dupefilter/filtered");
    }

    internal void Clear() => _db?.KeyDelete(_key);

    public ValueTask CloseAsync(string reason) => ValueTask.CompletedTask;
}

/// <summary>
/// A scheduler whose queue lives in Redis, shared by every process crawling the same spider
/// (scrapy-redis' <c>Scheduler</c>). Requests travel as JSON (<see cref="RequestSerializer"/>), so
/// callbacks must be named spider methods; requests with lambda callbacks stay in a local queue.
/// </summary>
/// <remarks>
/// Enable it with <see cref="RedisSettingsExtensions.UseRedis"/>. Unless <c>SCHEDULER_PERSIST</c> is true,
/// the queue and the duplicate filter are deleted when the spider closes; for several workers set it,
/// so the first worker to finish does not wipe the others' queue.
/// </remarks>
public sealed class RedisScheduler : IScheduler
{
    private readonly Crawler _crawler;
    private readonly ILogger _logger;
    private readonly PriorityQueue<Request, (int, long)> _local = new();
    private readonly bool _persist;
    private readonly bool _flushOnStart;
    private readonly string _queueClass;
    private IDatabase? _db;
    private RedisKey _key;
    private Spider? _spider;
    private long _sequence;

    public RedisScheduler(Crawler crawler)
    {
        _crawler = crawler;
        _logger = crawler.LoggerFactory.CreateLogger("scrapynet.redis.scheduler");
        _persist = crawler.Settings.GetBool(RedisSettingKeys.Persist);
        _flushOnStart = crawler.Settings.GetBool(RedisSettingKeys.FlushOnStart);
        _queueClass = (crawler.Settings.GetString(RedisSettingKeys.QueueClass) ?? "priority").ToLowerInvariant();
        if (_queueClass is not ("priority" or "fifo" or "lifo"))
            throw new ArgumentException($"SCHEDULER_QUEUE_CLASS must be priority, fifo or lifo, not '{_queueClass}'.");
        DupeFilter = crawler.Settings.GetTypeSetting(SettingKeys.DupeFilterClass) is { } type && type != typeof(RFPDupeFilter)
            ? (IDupeFilter)ComponentFactory.Create(type, crawler)!
            : new RedisDupeFilter(crawler);
    }

    public IDupeFilter DupeFilter { get; }

    /// <summary>The Redis key holding the queue.</summary>
    public string QueueKey => _key.ToString();

    public async ValueTask OpenAsync(Spider spider)
    {
        _spider = spider;
        _db = RedisConnections.Get(_crawler.Settings).GetDatabase();
        _key = RedisConnections.Key(_crawler.Settings.GetString(RedisSettingKeys.QueueKey), "{spider}:requests", spider);
        await DupeFilter.OpenAsync(spider).ConfigureAwait(false);
        if (_flushOnStart) Flush();
        var pending = RemoteCount();
        if (pending > 0) _logger.LogInformation("Resuming crawl ({Count} requests scheduled in {Key})", pending, QueueKey);
    }

    public async ValueTask CloseAsync(string reason)
    {
        if (!_persist) Flush();
        await DupeFilter.CloseAsync(reason).ConfigureAwait(false);
    }

    private void Flush()
    {
        _db!.KeyDelete(_key);
        (DupeFilter as RedisDupeFilter)?.Clear();
    }

    private long RemoteCount() => _queueClass == "priority" ? _db!.SortedSetLength(_key) : _db!.ListLength(_key);

    public int Count
    {
        get
        {
            int local;
            lock (_local) local = _local.Count;
            return (int)Math.Min(int.MaxValue, RemoteCount() + local);
        }
    }

    public bool HasPendingRequests => Count > 0;

    public bool Enqueue(Request request)
    {
        if (!request.DontFilter && DupeFilter.RequestSeen(request))
        {
            DupeFilter.Log(request, _spider!);
            return false;
        }
        _crawler.Stats.Inc("scheduler/enqueued");
        if (RequestSerializer.ToJson(request) is not { } json)
        {
            lock (_local) _local.Enqueue(request, (-request.Priority, ++_sequence));
            _crawler.Stats.Inc("scheduler/enqueued/memory");
            _crawler.Stats.Inc("scheduler/unserializable");
            return true;
        }
        // A unique id keeps identical requests (dont_filter) distinct members of the sorted set.
        json["_rid"] = Guid.NewGuid().ToString("N");
        var payload = json.ToJsonString();
        switch (_queueClass)
        {
            case "priority": _db!.SortedSetAdd(_key, payload, -request.Priority); break;
            default: _db!.ListLeftPush(_key, payload); break;
        }
        _crawler.Stats.Inc("scheduler/enqueued/redis");
        return true;
    }

    public Request? Dequeue()
    {
        lock (_local)
        {
            if (_local.TryDequeue(out var local, out _))
            {
                _crawler.Stats.Inc("scheduler/dequeued");
                _crawler.Stats.Inc("scheduler/dequeued/memory");
                return local;
            }
        }
        RedisValue value = _queueClass switch
        {
            "priority" => _db!.SortedSetPop(_key) is { } entry ? entry.Element : RedisValue.Null,
            "fifo" => _db!.ListRightPop(_key),
            _ => _db!.ListLeftPop(_key),
        };
        if (value.IsNull) return null;
        _crawler.Stats.Inc("scheduler/dequeued");
        _crawler.Stats.Inc("scheduler/dequeued/redis");
        return RequestSerializer.FromJson(JsonNode.Parse(value.ToString())!.AsObject(), _spider!);
    }
}

/// <summary>
/// A spider fed from Redis (scrapy-redis' <c>RedisSpider</c>): start URLs are popped from
/// <c>{spider}:start_urls</c> in batches, and when the spider runs out of work it stays open, polling the key
/// every second, until <c>MAX_IDLE_TIME_BEFORE_CLOSE</c> seconds pass without new URLs (0: forever).
/// </summary>
/// <remarks>
/// Entries are plain URLs or JSON objects: <c>{"url": "...", "meta": {...}, "priority": 5}</c>.
/// Override <see cref="MakeRequestFromData"/> for other formats.
/// </remarks>
public abstract class RedisSpider : Spider
{
    private RedisStartFeeder? _feeder;

    /// <summary>The key start URLs are read from.</summary>
    public string RedisKey => _feeder?.Key ?? "";

    public override ValueTask OnOpenedAsync()
    {
        _feeder = new RedisStartFeeder(this, MakeRequestFromData);
        return base.OnOpenedAsync();
    }

    public override ValueTask OnClosedAsync(string reason)
    {
        _feeder?.Dispose();
        return base.OnClosedAsync(reason);
    }

    public override async IAsyncEnumerable<Request> StartAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var request in _feeder!.NextBatch()) yield return request;
        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>Builds the request for one entry of the start key; <c>null</c> skips it.</summary>
    protected virtual Request? MakeRequestFromData(string data) => RedisStartFeeder.DefaultRequest(data, null);
}

/// <summary>A <see cref="RedisSpider"/> with crawl rules (scrapy-redis' <c>RedisCrawlSpider</c>): start URLs go through the rules.</summary>
public abstract class RedisCrawlSpider : CrawlSpider
{
    private RedisStartFeeder? _feeder;

    public string RedisKey => _feeder?.Key ?? "";

    public override ValueTask OnOpenedAsync()
    {
        _feeder = new RedisStartFeeder(this, MakeRequestFromData);
        return base.OnOpenedAsync();
    }

    public override ValueTask OnClosedAsync(string reason)
    {
        _feeder?.Dispose();
        return base.OnClosedAsync(reason);
    }

    public override async IAsyncEnumerable<Request> StartAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var request in _feeder!.NextBatch()) yield return request;
        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>Builds the request for one entry; the default sends it through <see cref="CrawlSpider"/>'s rules.</summary>
    protected virtual Request? MakeRequestFromData(string data) => RedisStartFeeder.DefaultRequest(data, Parse);
}

/// <summary>Pops start entries for a Redis-fed spider and keeps the spider open while waiting for more.</summary>
internal sealed class RedisStartFeeder : IDisposable
{
    private readonly Spider _spider;
    private readonly Func<string, Request?> _make;
    private readonly IDatabase _db;
    private readonly RedisKey _key;
    private readonly bool _asSet;
    private readonly int _batch;
    private readonly double _maxIdle;
    private readonly IDisposable _subscription;
    private DateTime _idleSince = DateTime.MaxValue;

    public RedisStartFeeder(Spider spider, Func<string, Request?> make)
    {
        _spider = spider;
        _make = make;
        var settings = spider.Crawler.Settings;
        _db = RedisConnections.Get(settings).GetDatabase();
        _key = RedisConnections.Key(settings.GetString(RedisSettingKeys.StartUrlsKey), "{spider}:start_urls", spider);
        _asSet = settings.GetBool(RedisSettingKeys.StartUrlsAsSet);
        _batch = settings.GetInt(RedisSettingKeys.StartUrlsBatchSize, settings.GetInt(SettingKeys.ConcurrentRequests, 16));
        _maxIdle = settings.GetDouble(RedisSettingKeys.MaxIdleTimeBeforeClose);
        _subscription = spider.Crawler.Signals.Connect(Signals.SpiderIdle, (SpiderEventArgs _) => OnIdleAsync());
    }

    public string Key => _key.ToString();

    public List<Request> NextBatch()
    {
        var values = (_asSet ? _db.SetPop(_key, _batch) : _db.ListLeftPop(_key, _batch)) ?? [];
        var requests = new List<Request>(values.Length);
        foreach (var value in values)
        {
            try
            {
                if (_make(value.ToString()) is { } request) requests.Add(request);
            }
            catch (Exception ex) when (ex is ArgumentException or JsonException or UriFormatException or KeyNotFoundException or InvalidOperationException)
            {
                _spider.Logger.LogWarning("Ignoring start entry {Entry} from {Key}: {Error}", value.ToString(), Key, ex.Message);
            }
        }
        if (requests.Count > 0) _spider.Crawler.Stats.Inc("redis/start_urls", requests.Count);
        return requests;
    }

    /// <summary>Feeds the next batch, or keeps waiting; closing is allowed once the idle limit passes.</summary>
    private async ValueTask OnIdleAsync()
    {
        var batch = NextBatch();
        if (batch.Count > 0)
        {
            _idleSince = DateTime.MaxValue;
            foreach (var request in batch) await _spider.Crawler.Engine.CrawlAsync(request).ConfigureAwait(false);
            throw new DontCloseSpiderException();
        }
        if (_idleSince == DateTime.MaxValue) _idleSince = DateTime.UtcNow;
        if (_maxIdle <= 0 || (DateTime.UtcNow - _idleSince).TotalSeconds < _maxIdle) throw new DontCloseSpiderException();
    }

    /// <summary>A plain URL, or <c>{"url": ..., "meta": {...}, "priority": n, "method": ...}</c>.</summary>
    public static Request DefaultRequest(string data, Func<Response, IAsyncEnumerable<object>>? callback)
    {
        data = data.Trim();
        if (!data.StartsWith('{')) return new Request(data, callback);
        var json = JsonNode.Parse(data)!.AsObject();
        var request = new Request(json["url"]?.GetValue<string>() ?? throw new KeyNotFoundException("JSON start entry has no \"url\"."), callback)
        {
            Priority = json["priority"]?.GetValue<int>() ?? 0,
            Method = json["method"]?.GetValue<string>() ?? "GET",
        };
        if (json["meta"] is JsonObject meta)
            foreach (var (k, v) in meta) request.Meta[k] = v is null ? null : Settings.FromJson(JsonDocument.Parse(v.ToJsonString()).RootElement);
        return request;
    }

    public void Dispose() => _subscription.Dispose();
}


/// <summary>Pushes every item, as JSON, onto <c>{spider}:items</c> (scrapy-redis' <c>RedisPipeline</c>), for other processes to consume.</summary>
public sealed class RedisPipeline(Crawler crawler) : ItemPipeline
{
    private IDatabase? _db;
    private RedisKey _key;

    public override ValueTask OpenSpiderAsync(Spider spider)
    {
        _db = RedisConnections.Get(crawler.Settings).GetDatabase();
        _key = RedisConnections.Key(crawler.Settings.GetString(RedisSettingKeys.ItemsKey), "{spider}:items", spider);
        return ValueTask.CompletedTask;
    }

    public override async ValueTask<object> ProcessItemAsync(object item, Spider spider)
    {
        var json = JsonSerializer.Serialize(ItemAdapter.For(item).AsDictionary(), ScrapyJson.Default);
        await _db!.ListRightPushAsync(_key, json).ConfigureAwait(false);
        return item;
    }
}

public static class RedisSettingsExtensions
{
    /// <summary>
    /// Switches the crawl to the Redis scheduler and duplicate filter (scrapy-redis' <c>SCHEDULER</c> and
    /// <c>DUPEFILTER_CLASS</c>). Set <paramref name="persist"/> when several workers share the queue.
    /// </summary>
    public static Settings UseRedis(this Settings settings, string url = "localhost:6379", bool persist = false, string queue = "priority")
    {
        settings.Set(RedisSettingKeys.RedisUrl, url);
        settings.Set(SettingKeys.Scheduler, typeof(RedisScheduler));
        settings.Set(SettingKeys.DupeFilterClass, typeof(RedisDupeFilter));
        settings.Set(RedisSettingKeys.Persist, persist);
        settings.Set(RedisSettingKeys.QueueClass, queue);
        return settings;
    }

    /// <summary>Adds <see cref="RedisPipeline"/> (default order 300, as in scrapy-redis' examples).</summary>
    public static Settings PushItemsToRedis(this Settings settings, int order = 300)
    {
        settings.ItemPipelines.Add<RedisPipeline>(order);
        return settings;
    }
}
