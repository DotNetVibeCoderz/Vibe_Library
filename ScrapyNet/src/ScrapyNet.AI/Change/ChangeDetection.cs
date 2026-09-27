using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ScrapyNet.AI;

/// <summary>What was last seen at a URL.</summary>
public sealed record ContentState(string Url, string ContentHash, string? ETag, string? LastModified, DateTimeOffset LastSeen, DateTimeOffset LastChanged);

/// <summary>Result of checking a page against its previous state.</summary>
public enum ChangeKind
{
    New,
    Changed,
    Unchanged,
}

/// <summary>
/// Remembers a content hash, ETag and Last-Modified per URL across runs (a JSON file), so re-crawls can
/// ask servers "has this changed?" and skip pages that haven't.
/// </summary>
public sealed class ContentChangeStore
{
    private readonly ConcurrentDictionary<string, ContentState> _states = new(StringComparer.Ordinal);
    private readonly string? _path;

    public ContentChangeStore(string? path = null)
    {
        _path = path;
        if (path is not null && File.Exists(path))
            foreach (var s in JsonSerializer.Deserialize<List<ContentState>>(File.ReadAllText(path), ScrapyJson.Default) ?? []) _states[s.Url] = s;
    }

    public int Count => _states.Count;

    public ContentState? Get(string url) => _states.GetValueOrDefault(url);

    /// <summary>Records the page and reports whether it is new, changed or unchanged.</summary>
    public ChangeKind Record(string url, string text, string? etag = null, string? lastModified = null)
    {
        var hash = ContentHasher.Hash(text);
        var now = DateTimeOffset.UtcNow;
        var kind = ChangeKind.New;
        _states.AddOrUpdate(url,
            _ => new ContentState(url, hash, etag, lastModified, now, now),
            (_, old) =>
            {
                kind = old.ContentHash == hash ? ChangeKind.Unchanged : ChangeKind.Changed;
                return old with { ContentHash = hash, ETag = etag ?? old.ETag, LastModified = lastModified ?? old.LastModified, LastSeen = now, LastChanged = kind == ChangeKind.Changed ? now : old.LastChanged };
            });
        return kind;
    }

    public void Save()
    {
        if (_path is null) return;
        var dir = Path.GetDirectoryName(Path.GetFullPath(_path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(_path, JsonSerializer.Serialize(_states.Values.OrderBy(s => s.Url, StringComparer.Ordinal).ToList(), ScrapyJson.Default));
    }
}

/// <summary>
/// Incremental re-crawling: sends <c>If-None-Match</c>/<c>If-Modified-Since</c> from the previous run, marks
/// 304 responses and pages whose content hash is unchanged with <c>meta["content_change"]</c>, and (with
/// <see cref="SkipUnchanged"/>) drops them before they reach callbacks. Configure the state file with
/// <c>CHANGE_STORE_PATH</c>.
/// </summary>
/// <remarks>Register with <c>settings.DownloaderMiddlewares.Add&lt;IncrementalRecrawlMiddleware&gt;(950)</c>.</remarks>
public sealed class IncrementalRecrawlMiddleware : DownloaderMiddleware
{
    public const string MetaKey = "content_change";

    private readonly IStatsCollector _stats;
    private readonly ILogger _logger;

    public IncrementalRecrawlMiddleware(Settings settings, IStatsCollector stats, ILogger logger)
    {
        Store = new ContentChangeStore(settings.GetString("CHANGE_STORE_PATH") ?? ".scrapynet/changes.json");
        SkipUnchanged = settings.GetBool("CHANGE_SKIP_UNCHANGED", true);
        _stats = stats;
        _logger = logger;
    }

    public ContentChangeStore Store { get; }

    public bool SkipUnchanged { get; }

    public override ValueTask<DownloadResult> ProcessRequestAsync(Request request, Spider spider)
    {
        if (request.Method != "GET" || Store.Get(request.Url) is not { } state) return DownloadResult.Continue;
        if (state.ETag is not null) request.Headers.SetDefault("If-None-Match", state.ETag);
        if (state.LastModified is not null) request.Headers.SetDefault("If-Modified-Since", state.LastModified);
        return DownloadResult.Continue;
    }

    public override ValueTask<DownloadResult> ProcessResponseAsync(Request request, Response response, Spider spider)
    {
        ChangeKind kind;
        if (response.Status == 304)
        {
            kind = ChangeKind.Unchanged;
        }
        else if (response is TextResponse text && response.IsSuccess)
        {
            kind = Store.Record(request.Url, text.Text, response.Headers.Get("ETag"), response.Headers.Get("Last-Modified"));
        }
        else
        {
            return (DownloadResult)response;
        }

        request.Meta[MetaKey] = kind.ToString().ToLowerInvariant();
        _stats.Inc($"change_detection/{kind.ToString().ToLowerInvariant()}");
        if (kind == ChangeKind.Unchanged && SkipUnchanged)
        {
            _logger.LogDebug("Skipping unchanged page {Url}", request.Url);
            throw new IgnoreRequestException($"Content unchanged: {request.Url}");
        }
        return (DownloadResult)response;
    }

    public override ValueTask CloseSpiderAsync(Spider spider, string reason)
    {
        Store.Save();
        return ValueTask.CompletedTask;
    }
}
