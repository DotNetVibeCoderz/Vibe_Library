using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ScrapyNet.DownloaderMiddlewares;

/// <summary>Decides what the HTTP cache stores and when a stored response is still usable.</summary>
public interface IHttpCachePolicy
{
    bool ShouldCacheRequest(Request request);

    bool ShouldCacheResponse(Response response, Request request);

    bool IsCachedResponseFresh(Response cached, Request request);
}

/// <summary>Caches everything and never revalidates — ideal for replaying a crawl while developing selectors.</summary>
public sealed class DummyCachePolicy(Settings settings) : IHttpCachePolicy
{
    private readonly HashSet<string> _ignoreSchemes = [.. settings.GetList(SettingKeys.HttpCacheIgnoreSchemes, ["file"])];
    private readonly HashSet<int> _ignoreCodes = [.. settings.GetIntList(SettingKeys.HttpCacheIgnoreHttpCodes)];

    public bool ShouldCacheRequest(Request request) => !_ignoreSchemes.Contains(request.Uri.Scheme);

    public bool ShouldCacheResponse(Response response, Request request) => !_ignoreCodes.Contains(response.Status);

    public bool IsCachedResponseFresh(Response cached, Request request) => true;
}

/// <summary>A pragmatic subset of RFC 9111: respects <c>no-store</c>, <c>max-age</c> and <c>Expires</c>.</summary>
public sealed class Rfc2616CachePolicy(Settings settings) : IHttpCachePolicy
{
    private readonly HashSet<int> _ignoreCodes = [.. settings.GetIntList(SettingKeys.HttpCacheIgnoreHttpCodes)];

    public bool ShouldCacheRequest(Request request) =>
        request.Uri.Scheme is "http" or "https" && !(request.Headers.Get("Cache-Control")?.Contains("no-store", StringComparison.OrdinalIgnoreCase) ?? false);

    public bool ShouldCacheResponse(Response response, Request request)
    {
        if (_ignoreCodes.Contains(response.Status)) return false;
        var cc = response.Headers.Get("Cache-Control") ?? "";
        return !cc.Contains("no-store", StringComparison.OrdinalIgnoreCase);
    }

    public bool IsCachedResponseFresh(Response cached, Request request)
    {
        var cc = cached.Headers.Get("Cache-Control") ?? "";
        if (cc.Contains("no-cache", StringComparison.OrdinalIgnoreCase)) return false;
        var date = DateTimeOffset.TryParse(cached.Headers.Get("Date"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : (DateTimeOffset?)null;
        var maxAge = ParseMaxAge(cc);
        if (maxAge is not null && date is not null) return DateTimeOffset.UtcNow - date.Value < TimeSpan.FromSeconds(maxAge.Value);
        if (DateTimeOffset.TryParse(cached.Headers.Get("Expires"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var expires))
            return DateTimeOffset.UtcNow < expires;
        // Heuristic freshness: 10% of the time since Last-Modified, as browsers do.
        if (date is not null && DateTimeOffset.TryParse(cached.Headers.Get("Last-Modified"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var lm))
            return DateTimeOffset.UtcNow - date.Value < (date.Value - lm) / 10;
        return false;
    }

    private static int? ParseMaxAge(string cacheControl)
    {
        foreach (var part in cacheControl.Split(','))
        {
            var p = part.Trim();
            if (p.StartsWith("max-age=", StringComparison.OrdinalIgnoreCase) && int.TryParse(p[8..], out var v)) return v;
        }
        return null;
    }
}

/// <summary>
/// Stores responses on disk under <c>HTTPCACHE_DIR/spider/fp[0:2]/fp/</c> (a metadata JSON file and the raw
/// body), expiring them after <c>HTTPCACHE_EXPIRATION_SECS</c> (0 = never).
/// </summary>
public sealed class FilesystemCacheStorage
{
    private readonly string _root;
    private readonly double _expiration;
    private readonly RequestFingerprinter _fingerprinter;

    public FilesystemCacheStorage(Settings settings, RequestFingerprinter fingerprinter)
    {
        var dir = settings.GetString(SettingKeys.HttpCacheDir) ?? "httpcache";
        _root = Path.IsPathRooted(dir) ? dir : Path.Combine(settings.GetString(SettingKeys.JobDir) ?? ".scrapynet", dir);
        _expiration = settings.GetDouble(SettingKeys.HttpCacheExpirationSecs);
        _fingerprinter = fingerprinter;
    }

    public string Root => _root;

    public Response? Retrieve(Spider spider, Request request)
    {
        var dir = GetPath(spider, request);
        var metaPath = Path.Combine(dir, "meta.json");
        var bodyPath = Path.Combine(dir, "response_body");
        if (!File.Exists(metaPath) || !File.Exists(bodyPath)) return null;
        if (_expiration > 0 && DateTime.UtcNow - File.GetLastWriteTimeUtc(metaPath) > TimeSpan.FromSeconds(_expiration)) return null;

        var meta = JsonSerializer.Deserialize<CacheMeta>(File.ReadAllText(metaPath), ScrapyJson.Default);
        if (meta is null) return null;
        var headers = new Headers();
        foreach (var (name, values) in meta.Headers) headers.Set(name, values);
        return ResponseTypes.Create(meta.ResponseUrl, meta.Status, headers, File.ReadAllBytes(bodyPath), request);
    }

    public void Store(Spider spider, Request request, Response response)
    {
        var dir = GetPath(spider, request);
        Directory.CreateDirectory(dir);
        var meta = new CacheMeta(request.Url, response.Url, response.Status, request.Method,
            response.Headers.ToDictionary(h => h.Key, h => h.Value.ToList()), DateTimeOffset.UtcNow);
        File.WriteAllBytes(Path.Combine(dir, "response_body"), response.Body);
        File.WriteAllText(Path.Combine(dir, "meta.json"), JsonSerializer.Serialize(meta, ScrapyJson.Default));
    }

    private string GetPath(Spider spider, Request request)
    {
        var fp = _fingerprinter.Fingerprint(request).ToHex();
        return Path.Combine(_root, spider.Name, fp[..2], fp);
    }

    private sealed record CacheMeta(string Url, string ResponseUrl, int Status, string Method, Dictionary<string, List<string>> Headers, DateTimeOffset Timestamp);
}

/// <summary>
/// Caches every response to disk and replays it on the next run (Scrapy's <c>HttpCacheMiddleware</c>).
/// Turn on with <c>HTTPCACHE_ENABLED</c> while iterating on parsing code, so the site is hit only once.
/// Replayed responses carry the <c>cached</c> flag.
/// </summary>
public sealed class HttpCacheMiddleware : DownloaderMiddleware
{
    private readonly IHttpCachePolicy _policy;
    private readonly FilesystemCacheStorage _storage;
    private readonly bool _ignoreMissing;
    private readonly IStatsCollector _stats;

    public HttpCacheMiddleware(Crawler crawler, ILogger logger)
    {
        var settings = crawler.Settings;
        if (!settings.GetBool(SettingKeys.HttpCacheEnabled)) throw new NotConfiguredException();
        _policy = settings.GetString(SettingKeys.HttpCachePolicy)?.ToLowerInvariant() switch
        {
            "rfc2616" => new Rfc2616CachePolicy(settings),
            _ => new DummyCachePolicy(settings),
        };
        _storage = new FilesystemCacheStorage(settings, crawler.Fingerprinter);
        _ignoreMissing = settings.GetBool(SettingKeys.HttpCacheIgnoreMissing);
        _stats = crawler.Stats;
        logger.LogInformation("Using filesystem cache storage in {Dir}", Path.GetFullPath(_storage.Root));
    }

    public override ValueTask<DownloadResult> ProcessRequestAsync(Request request, Spider spider)
    {
        if (request.GetMeta(MetaKeys.DontCache, false) || !_policy.ShouldCacheRequest(request))
        {
            request.Meta["_dont_cache"] = true;
            return DownloadResult.Continue;
        }

        var cached = _storage.Retrieve(spider, request);
        if (cached is null)
        {
            _stats.Inc("httpcache/miss");
            if (_ignoreMissing)
            {
                _stats.Inc("httpcache/ignore");
                throw new IgnoreRequestException($"Ignored request not in cache: {request}");
            }
            return DownloadResult.Continue;
        }

        if (_policy.IsCachedResponseFresh(cached, request))
        {
            _stats.Inc("httpcache/hit");
            cached.Flags.Add("cached");
            return (DownloadResult)cached;
        }
        _stats.Inc("httpcache/stale");
        return DownloadResult.Continue;
    }

    public override ValueTask<DownloadResult> ProcessResponseAsync(Request request, Response response, Spider spider)
    {
        if (request.Meta.ContainsKey("_dont_cache"))
        {
            request.Meta.Remove("_dont_cache");
            return (DownloadResult)response;
        }
        if (response.Flags.Contains("cached")) return (DownloadResult)response;
        if (_policy.ShouldCacheResponse(response, request))
        {
            _stats.Inc("httpcache/store");
            _storage.Store(spider, request, response);
        }
        else
        {
            _stats.Inc("httpcache/uncacheable");
        }
        return (DownloadResult)response;
    }
}
