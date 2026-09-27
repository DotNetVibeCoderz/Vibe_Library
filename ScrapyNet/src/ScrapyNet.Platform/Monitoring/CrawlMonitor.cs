using System.Collections.Concurrent;
using System.Diagnostics;

namespace ScrapyNet.Platform;

/// <summary>A point in a job's time series.</summary>
public sealed record MetricSample(DateTimeOffset Time, long Requests, long Responses, long Items, long Errors);

/// <summary>Live counters of one running job, fed by the crawler's signals.</summary>
public sealed class LiveMetrics
{
    private readonly ConcurrentQueue<long> _recentResponses = new();
    private readonly List<MetricSample> _series = [];
    private readonly ConcurrentDictionary<int, long> _statuses = new();
    private long _requests;
    private long _responses;
    private long _items;
    private long _dropped;
    private long _errors;
    private long _bytes;

    public required string JobId { get; init; }
    public required string SpiderName { get; init; }
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;

    public long Requests => Interlocked.Read(ref _requests);
    public long Responses => Interlocked.Read(ref _responses);
    public long Items => Interlocked.Read(ref _items);
    public long DroppedItems => Interlocked.Read(ref _dropped);
    public long Errors => Interlocked.Read(ref _errors);
    public long BytesDownloaded => Interlocked.Read(ref _bytes);
    public IReadOnlyDictionary<int, long> StatusCounts => _statuses;

    /// <summary>Responses in the last 60 seconds.</summary>
    public int ResponsesPerMinute
    {
        get
        {
            var cutoff = Stopwatch.GetTimestamp() - Stopwatch.Frequency * 60;
            while (_recentResponses.TryPeek(out var t) && t < cutoff) _recentResponses.TryDequeue(out _);
            return _recentResponses.Count;
        }
    }

    /// <summary>Share of responses that were errors (non-2xx) or download failures.</summary>
    public double ErrorRate
    {
        get
        {
            var total = Responses + Errors;
            if (total == 0) return 0;
            var bad = Errors + _statuses.Where(s => s.Key >= 400).Sum(s => s.Value);
            return Math.Round((double)bad / total, 4);
        }
    }

    public IReadOnlyList<MetricSample> Series
    {
        get
        {
            lock (_series) return [.. _series];
        }
    }

    internal void OnRequest() => Interlocked.Increment(ref _requests);

    internal void OnResponse(Response r)
    {
        Interlocked.Increment(ref _responses);
        Interlocked.Add(ref _bytes, r.Body.Length);
        _statuses.AddOrUpdate(r.Status, 1, (_, v) => v + 1);
        _recentResponses.Enqueue(Stopwatch.GetTimestamp());
    }

    internal void OnItem() => Interlocked.Increment(ref _items);

    internal void OnDropped() => Interlocked.Increment(ref _dropped);

    internal void OnError() => Interlocked.Increment(ref _errors);

    internal void Sample()
    {
        lock (_series)
        {
            _series.Add(new MetricSample(DateTimeOffset.UtcNow, Requests, Responses, Items, Errors));
            if (_series.Count > 3600) _series.RemoveAt(0);
        }
    }
}

/// <summary>Per-spider summary for dashboards.</summary>
public sealed record SpiderStatus(string SpiderId, string Name, bool Enabled, JobStatus? LastStatus, DateTimeOffset? LastRun, long LastItems, int Runs, int Failures);

/// <summary>Everything a monitoring dashboard shows at one moment.</summary>
public sealed record DashboardSnapshot(
    DateTimeOffset Time,
    IReadOnlyList<(JobRecord Job, LiveMetrics Metrics)> Running,
    int CompletedJobs,
    int FailedJobs,
    long ItemsLast24Hours,
    int RequestRatePerMinute,
    double ErrorRate,
    IReadOnlyList<SpiderStatus> Spiders);

/// <summary>
/// Collects live metrics for every job a <see cref="JobManager"/> runs (request rate, items, errors,
/// status codes, time series sampled each second) and builds dashboard snapshots.
/// </summary>
public sealed class CrawlMonitor : IDisposable
{
    private readonly JobManager _jobs;
    private readonly ConcurrentDictionary<string, LiveMetrics> _live = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, LiveMetrics> _finished = new(StringComparer.Ordinal);
    private readonly Timer _sampler;

    public CrawlMonitor(JobManager jobs, TimeSpan? sampleInterval = null)
    {
        _jobs = jobs;
        jobs.CrawlerCreated += Attach;
        jobs.JobFinished += job =>
        {
            if (_live.TryRemove(job.Id, out var m))
            {
                m.Sample();
                _finished[job.Id] = m;
            }
        };
        var interval = sampleInterval ?? TimeSpan.FromSeconds(1);
        _sampler = new Timer(_ => { foreach (var m in _live.Values) m.Sample(); }, null, interval, interval);
    }

    public LiveMetrics? Get(string jobId) => _live.GetValueOrDefault(jobId) ?? _finished.GetValueOrDefault(jobId);

    private void Attach(JobRecord job, Crawler crawler)
    {
        var metrics = new LiveMetrics { JobId = job.Id, SpiderName = job.SpiderName };
        _live[job.Id] = metrics;
        crawler.Signals.Connect(Signals.RequestReachedDownloader, _ => metrics.OnRequest());
        crawler.Signals.Connect(Signals.ResponseReceived, e => metrics.OnResponse(e.Response));
        crawler.Signals.Connect(Signals.ItemScraped, _ => metrics.OnItem());
        crawler.Signals.Connect(Signals.ItemDropped, _ => metrics.OnDropped());
        crawler.Signals.Connect(Signals.SpiderError, _ => metrics.OnError());
    }

    public DashboardSnapshot Snapshot()
    {
        var now = DateTimeOffset.UtcNow;
        var history = _jobs.History(limit: 10_000);
        var running = _jobs.Running.Select(j => (j, _live.GetValueOrDefault(j.Id))).Where(x => x.Item2 is not null).Select(x => (x.j, x.Item2!)).ToList();
        var recent = history.Where(j => j.FinishedAt >= now.AddHours(-24)).ToList();
        var spiders = _jobs.Catalog.List().Select(d =>
        {
            var runs = history.Where(j => j.SpiderId == d.Id).ToList();
            var last = runs.FirstOrDefault();
            return new SpiderStatus(d.Id, d.Name, d.Enabled, last?.Status, last?.StartedAt, last?.ItemCount ?? 0, runs.Count, runs.Count(r => r.Status == JobStatus.Failed));
        }).ToList();
        var liveTotal = running.Sum(r => r.Item2.Responses + r.Item2.Errors);
        var liveBad = running.Sum(r => r.Item2.ErrorRate * (r.Item2.Responses + r.Item2.Errors));
        return new DashboardSnapshot(
            now,
            running,
            history.Count(j => j.Status == JobStatus.Completed),
            history.Count(j => j.Status == JobStatus.Failed),
            recent.Sum(j => j.ItemCount) + running.Sum(r => r.Item2.Items),
            running.Sum(r => r.Item2.ResponsesPerMinute),
            liveTotal == 0 ? 0 : Math.Round(liveBad / liveTotal, 4),
            spiders);
    }

    public void Dispose() => _sampler.Dispose();
}
