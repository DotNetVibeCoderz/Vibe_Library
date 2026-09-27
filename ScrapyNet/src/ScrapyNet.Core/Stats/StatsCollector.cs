using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace ScrapyNet;

/// <summary>Crawl statistics: counters and values keyed like Scrapy's (<c>downloader/request_count</c>, ...).</summary>
public interface IStatsCollector
{
    object? GetValue(string key, object? defaultValue = null);

    long GetCount(string key);

    void SetValue(string key, object? value);

    /// <summary>Atomically adds to a counter.</summary>
    void Inc(string key, long count = 1);

    void Max(string key, double value);

    void Min(string key, double value);

    /// <summary>A sorted snapshot of every stat.</summary>
    IReadOnlyDictionary<string, object?> GetStats();

    void Clear();

    void OpenSpider(Spider spider);

    void CloseSpider(Spider spider, string reason);
}

/// <summary>
/// In-memory stats collector. Counters are <see cref="StrongBox{T}"/> cells updated with
/// <see cref="Interlocked"/>, so the hot per-request increments neither lock nor allocate.
/// </summary>
public class MemoryStatsCollector : IStatsCollector
{
    private readonly ConcurrentDictionary<string, StrongBox<long>> _counters = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, object?> _values = new(StringComparer.Ordinal);
    private readonly object _minMaxGate = new();

    /// <summary>Stats of spiders that have closed, keyed by spider name (Scrapy's <c>spider_stats</c>).</summary>
    public ConcurrentDictionary<string, IReadOnlyDictionary<string, object?>> SpiderStats { get; } = new();

    public object? GetValue(string key, object? defaultValue = null)
    {
        if (_counters.TryGetValue(key, out var box)) return Volatile.Read(ref box.Value);
        return _values.TryGetValue(key, out var v) ? v : defaultValue;
    }

    public long GetCount(string key) => _counters.TryGetValue(key, out var box) ? Volatile.Read(ref box.Value) : 0;

    public void SetValue(string key, object? value)
    {
        if (value is long or int && _counters.TryGetValue(key, out var box))
        {
            Volatile.Write(ref box.Value, Convert.ToInt64(value, CultureInfo.InvariantCulture));
            return;
        }
        _values[key] = value;
    }

    public void Inc(string key, long count = 1)
    {
        var box = _counters.GetOrAdd(key, static _ => new StrongBox<long>());
        Interlocked.Add(ref box.Value, count);
    }

    public void Max(string key, double value)
    {
        lock (_minMaxGate)
        {
            if (!_values.TryGetValue(key, out var current) || current is null || Convert.ToDouble(current, CultureInfo.InvariantCulture) < value)
                _values[key] = value;
        }
    }

    public void Min(string key, double value)
    {
        lock (_minMaxGate)
        {
            if (!_values.TryGetValue(key, out var current) || current is null || Convert.ToDouble(current, CultureInfo.InvariantCulture) > value)
                _values[key] = value;
        }
    }

    public IReadOnlyDictionary<string, object?> GetStats()
    {
        var result = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (k, v) in _values) result[k] = v;
        foreach (var (k, box) in _counters) result[k] = Volatile.Read(ref box.Value);
        return result;
    }

    public void Clear()
    {
        _counters.Clear();
        _values.Clear();
    }

    public virtual void OpenSpider(Spider spider) { }

    public virtual void CloseSpider(Spider spider, string reason) => SpiderStats[spider.Name] = GetStats();

    /// <summary>Scrapy-style multi-line dump used at the end of a crawl.</summary>
    public static string Format(IReadOnlyDictionary<string, object?> stats)
    {
        var sb = new StringBuilder("{");
        var first = true;
        foreach (var (k, v) in stats)
        {
            sb.Append(first ? "" : ",").Append("\n '").Append(k).Append("': ").Append(v switch
            {
                string s => $"'{s}'",
                DateTimeOffset d => d.ToString("O", CultureInfo.InvariantCulture),
                double d => d.ToString("0.######", CultureInfo.InvariantCulture),
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                null => "null",
                _ => v.ToString(),
            });
            first = false;
        }
        return sb.Append('}').ToString();
    }
}

/// <summary>A collector that records nothing, for when stats overhead must be zero.</summary>
public sealed class DummyStatsCollector : IStatsCollector
{
    public object? GetValue(string key, object? defaultValue = null) => defaultValue;
    public long GetCount(string key) => 0;
    public void SetValue(string key, object? value) { }
    public void Inc(string key, long count = 1) { }
    public void Max(string key, double value) { }
    public void Min(string key, double value) { }
    public IReadOnlyDictionary<string, object?> GetStats() => new Dictionary<string, object?>();
    public void Clear() { }
    public void OpenSpider(Spider spider) { }
    public void CloseSpider(Spider spider, string reason) { }
}
