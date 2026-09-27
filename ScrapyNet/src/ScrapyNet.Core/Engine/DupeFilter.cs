using Microsoft.Extensions.Logging;

namespace ScrapyNet.Engine;

/// <summary>Decides whether a request was already seen (Scrapy's <c>DUPEFILTER_CLASS</c>).</summary>
public interface IDupeFilter
{
    ValueTask OpenAsync(Spider spider);

    /// <summary>Returns true if the request is a duplicate (and records it otherwise).</summary>
    bool RequestSeen(Request request);

    void Log(Request request, Spider spider);

    ValueTask CloseAsync(string reason);
}

/// <summary>
/// Fingerprint-based duplicate filter. With <c>JOBDIR</c> set, seen fingerprints are appended to
/// <c>requests.seen</c> and reloaded on the next run, so a resumed crawl skips what it already fetched
/// (Scrapy's persistent visited URLs).
/// </summary>
public class RFPDupeFilter : IDupeFilter
{
    private readonly HashSet<RequestFingerprint> _seen = [];
    private readonly object _gate = new();
    private readonly Crawler _crawler;
    private readonly ILogger _logger;
    private readonly bool _debug;
    private readonly string? _path;
    private StreamWriter? _file;
    private bool _loggedDupes;

    public RFPDupeFilter(Crawler crawler)
    {
        _crawler = crawler;
        _logger = crawler.LoggerFactory.CreateLogger("scrapynet.dupefilters");
        _debug = crawler.Settings.GetBool(SettingKeys.DupeFilterDebug);
        var jobDir = crawler.Settings.GetString(SettingKeys.JobDir);
        if (!string.IsNullOrEmpty(jobDir)) _path = Path.Combine(jobDir, "requests.seen");
    }

    /// <summary>Number of fingerprints recorded.</summary>
    public int Count
    {
        get
        {
            lock (_gate) return _seen.Count;
        }
    }

    public ValueTask OpenAsync(Spider spider)
    {
        if (_path is null) return ValueTask.CompletedTask;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        if (File.Exists(_path))
        {
            foreach (var line in File.ReadLines(_path))
                if (RequestFingerprint.TryParse(line.AsSpan().Trim(), out var fp)) _seen.Add(fp);
            _logger.LogInformation("Loaded {Count} seen request fingerprints from {Path}", _seen.Count, _path);
        }
        _file = new StreamWriter(new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read, 65536));
        return ValueTask.CompletedTask;
    }

    public bool RequestSeen(Request request)
    {
        var fp = _crawler.Fingerprinter.Fingerprint(request);
        lock (_gate)
        {
            if (!_seen.Add(fp)) return true;
            _file?.WriteLine(fp.ToHex());
            return false;
        }
    }

    public void Log(Request request, Spider spider)
    {
        if (_debug)
        {
            _logger.LogDebug("Filtered duplicate request: {Request} (referer: {Referer})", request, request.Headers.Get("Referer") ?? "None");
        }
        else if (!_loggedDupes)
        {
            _logger.LogDebug("Filtered duplicate request: {Request} - no more duplicates will be shown (see DUPEFILTER_DEBUG to show all duplicates)", request);
            _loggedDupes = true;
        }
        _crawler.Stats.Inc("dupefilter/filtered");
    }

    public ValueTask CloseAsync(string reason)
    {
        lock (_gate)
        {
            _file?.Dispose();
            _file = null;
        }
        return ValueTask.CompletedTask;
    }
}

/// <summary>A dupe filter that lets everything through.</summary>
public sealed class NoDupeFilter : IDupeFilter
{
    public ValueTask OpenAsync(Spider spider) => ValueTask.CompletedTask;
    public bool RequestSeen(Request request) => false;
    public void Log(Request request, Spider spider) { }
    public ValueTask CloseAsync(string reason) => ValueTask.CompletedTask;
}
