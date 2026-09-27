using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScrapyNet.Platform;

[JsonConverter(typeof(JsonStringEnumConverter<JobStatus>))]
public enum JobStatus
{
    Pending,
    Running,
    Paused,
    Completed,
    Failed,
    Stopped,
    Cancelled,
}

/// <summary>One run of a spider.</summary>
public sealed class JobRecord
{
    public required string Id { get; init; }
    public required string SpiderId { get; init; }
    public required string SpiderName { get; init; }
    public int SpiderVersion { get; init; }

    /// <summary><c>manual</c>, <c>schedule:ID</c>, <c>retry:JOBID</c>, <c>api</c>.</summary>
    public string Trigger { get; init; } = "manual";

    public int Attempt { get; init; } = 1;
    public Dictionary<string, string> Arguments { get; init; } = [];
    public Dictionary<string, object?> SettingsOverrides { get; init; } = [];
    public JobStatus Status { get; set; } = JobStatus.Pending;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string? FinishReason { get; set; }
    public string? Error { get; set; }
    public long ItemCount { get; set; }
    public long RequestCount { get; set; }
    public long ResponseCount { get; set; }
    public long ErrorCount { get; set; }

    /// <summary>Where this job's items were written (when the manager has a data directory).</summary>
    public string? OutputPath { get; set; }

    public Dictionary<string, object?> Stats { get; set; } = [];

    [JsonIgnore]
    public TimeSpan? Duration => StartedAt is null ? null : (FinishedAt ?? DateTimeOffset.UtcNow) - StartedAt.Value;

    [JsonIgnore]
    public bool IsFinished => Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Stopped or JobStatus.Cancelled;

    public override string ToString() => $"{Id} {SpiderName} {Status} ({ItemCount} items)";
}

public sealed record JobManagerOptions
{
    /// <summary>Jobs running at once; others wait in the queue.</summary>
    public int MaxConcurrentJobs { get; init; } = 4;

    /// <summary>Base settings for every job (defaults are Scrapy.Net's).</summary>
    public Func<Settings>? BaseSettings { get; init; }

    /// <summary>JSON Lines file where finished jobs are appended (job history).</summary>
    public string? HistoryPath { get; init; }

    /// <summary>When set, each job's items are exported to <c>{DataDirectory}/{spider}/{jobId}.jsonl</c>.</summary>
    public string? DataDirectory { get; init; }

    /// <summary>Resolves <c>${secret:...}</c> placeholders in spider settings.</summary>
    public SecretStore? Secrets { get; init; }
}

/// <summary>
/// Runs spiders as jobs: queue with a concurrency limit, run now, stop (graceful), pause/resume,
/// cancel (immediate), retry, and a persistent job history. Events let monitoring and alerting hook in.
/// </summary>
public sealed class JobManager : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = ScrapyJson.Web;

    private readonly SpiderCatalog _catalog;
    private readonly JobManagerOptions _options;
    private readonly SemaphoreSlim _slots;
    private readonly ConcurrentDictionary<string, JobRecord> _jobs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Crawler> _crawlers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JobRecord>> _completions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pendingCancel = new(StringComparer.Ordinal);
    private readonly object _historyGate = new();
    private int _sequence;

    public JobManager(SpiderCatalog catalog, JobManagerOptions? options = null)
    {
        _catalog = catalog;
        _options = options ?? new JobManagerOptions();
        _slots = new SemaphoreSlim(Math.Max(1, _options.MaxConcurrentJobs));
        if (_options.HistoryPath is not null && File.Exists(_options.HistoryPath))
            foreach (var line in File.ReadLines(_options.HistoryPath))
                if (!string.IsNullOrWhiteSpace(line) && JsonSerializer.Deserialize<JobRecord>(line, Json) is { } job)
                    _jobs[job.Id] = job;
    }

    public SpiderCatalog Catalog => _catalog;

    public event Action<JobRecord>? JobQueued;
    public event Action<JobRecord>? JobStarted;
    public event Action<JobRecord>? JobFinished;

    /// <summary>Raised when a job's crawler exists but has not started, so observers can connect to its signals.</summary>
    public event Action<JobRecord, Crawler>? CrawlerCreated;

    /// <summary>Queues a run of a spider. It starts as soon as a slot is free.</summary>
    public JobRecord RunNow(string spiderId, IReadOnlyDictionary<string, string>? arguments = null,
        IReadOnlyDictionary<string, object?>? settings = null, string trigger = "manual", int attempt = 1)
    {
        var definition = _catalog.Get(spiderId);
        if (!definition.Enabled) throw new InvalidOperationException($"Spider '{spiderId}' is disabled.");
        var job = new JobRecord
        {
            Id = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Interlocked.Increment(ref _sequence):D4}",
            SpiderId = spiderId,
            SpiderName = definition.Name,
            SpiderVersion = definition.Version,
            Trigger = trigger,
            Attempt = attempt,
            Arguments = arguments is null ? [] : new Dictionary<string, string>(arguments),
            SettingsOverrides = settings is null ? [] : new Dictionary<string, object?>(settings),
        };
        _jobs[job.Id] = job;
        _completions[job.Id] = new TaskCompletionSource<JobRecord>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancel = new CancellationTokenSource();
        _pendingCancel[job.Id] = cancel;
        JobQueued?.Invoke(job);
        _ = RunJobAsync(job, definition, cancel.Token);
        return job;
    }

    private async Task RunJobAsync(JobRecord job, SpiderDefinition definition, CancellationToken cancel)
    {
        try
        {
            await _slots.WaitAsync(cancel).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            job.Status = JobStatus.Cancelled;
            job.FinishReason = "cancelled";
            job.FinishedAt = DateTimeOffset.UtcNow;
            Complete(job);
            return;
        }

        try
        {
            var settings = BuildSettings(definition, job);
            var spider = _catalog.CreateSpider(definition);
            var crawler = new Crawler(spider, settings);
            _crawlers[job.Id] = crawler;
            CrawlerCreated?.Invoke(job, crawler);
            job.Status = JobStatus.Running;
            job.StartedAt = DateTimeOffset.UtcNow;
            JobStarted?.Invoke(job);

            var result = await crawler.CrawlAsync(job.Arguments).ConfigureAwait(false);
            job.FinishReason = result.FinishReason;
            job.Status = result.FinishReason switch
            {
                "shutdown" or "stopped" => JobStatus.Stopped,
                "killed" => JobStatus.Cancelled,
                "engine_error" => JobStatus.Failed,
                _ => JobStatus.Completed,
            };
            Capture(job, result.Stats);
        }
        catch (Exception ex)
        {
            job.Status = JobStatus.Failed;
            job.Error = ex.Message;
            job.FinishReason ??= "error";
        }
        finally
        {
            job.FinishedAt = DateTimeOffset.UtcNow;
            _crawlers.TryRemove(job.Id, out _);
            _slots.Release();
            Complete(job);
        }
    }

    private void Complete(JobRecord job)
    {
        if (_pendingCancel.TryRemove(job.Id, out var cts)) cts.Dispose();
        AppendHistory(job);
        JobFinished?.Invoke(job);
        if (_completions.TryRemove(job.Id, out var tcs)) tcs.TrySetResult(job);
    }

    private static void Capture(JobRecord job, IReadOnlyDictionary<string, object?> stats)
    {
        long Get(string key) => stats.TryGetValue(key, out var v) && v is not null ? Convert.ToInt64(v, CultureInfo.InvariantCulture) : 0;
        job.ItemCount = Get("item_scraped_count");
        job.RequestCount = Get("downloader/request_count");
        job.ResponseCount = Get("response_received_count");
        job.ErrorCount = Get("log_count/ERROR");
        job.Stats = stats.ToDictionary(kv => kv.Key, kv => kv.Value is DateTimeOffset d ? d.ToString("O", CultureInfo.InvariantCulture) : kv.Value);
    }

    private Settings BuildSettings(SpiderDefinition definition, JobRecord job)
    {
        var settings = _options.BaseSettings?.Invoke() ?? new Settings();
        foreach (var (k, v) in definition.Settings) settings.Set(k, Resolve(v), SettingPriority.Project);
        foreach (var (k, v) in job.SettingsOverrides) settings.Set(k, Resolve(v), SettingPriority.Cmdline);
        settings.Extensions.Add<LoginRedirectDetector>(0);
        if (_options.DataDirectory is not null)
        {
            job.OutputPath = Path.Combine(_options.DataDirectory, definition.Name, job.Id + ".jsonl");
            settings.Feeds.Add(job.OutputPath, new FeedOptions { Format = "jsonlines", Overwrite = true });
        }
        return settings;
    }

    /// <summary>Resolves secret placeholders anywhere in a setting value, including nested dictionaries and lists.</summary>
    private object? Resolve(object? value)
    {
        if (_options.Secrets is not { } secrets) return value;
        return value switch
        {
            string s => secrets.ResolvePlaceholders(s),
            JsonElement je => Resolve(ScrapyNet.Settings.FromJson(je)),
            IDictionary<string, object?> d => d.ToDictionary(kv => kv.Key, kv => Resolve(kv.Value), StringComparer.OrdinalIgnoreCase),
            IDictionary<string, string> ds => ds.ToDictionary(kv => kv.Key, kv => (object?)secrets.ResolvePlaceholders(kv.Value), StringComparer.OrdinalIgnoreCase),
            List<object?> list => list.Select(Resolve).ToList(),
            List<string> strings => strings.Select(secrets.ResolvePlaceholders).ToList(),
            _ => value,
        };
    }

    // ---- control ----------------------------------------------------------------------------------

    /// <summary>Graceful stop: in-flight requests finish, pending ones are saved if the spider uses JOBDIR.</summary>
    public async Task StopAsync(string jobId)
    {
        if (_crawlers.TryGetValue(jobId, out var crawler)) await crawler.StopAsync("stopped").ConfigureAwait(false);
        else CancelPending(jobId);
    }

    /// <summary>Immediate cancellation: in-flight downloads are aborted.</summary>
    public void Cancel(string jobId)
    {
        if (_crawlers.TryGetValue(jobId, out var crawler)) crawler.Kill();
        else CancelPending(jobId);
    }

    public void Pause(string jobId)
    {
        if (!_crawlers.TryGetValue(jobId, out var crawler)) throw new InvalidOperationException($"Job {jobId} is not running.");
        crawler.Pause();
        _jobs[jobId].Status = JobStatus.Paused;
    }

    public void Resume(string jobId)
    {
        if (!_crawlers.TryGetValue(jobId, out var crawler)) throw new InvalidOperationException($"Job {jobId} is not running.");
        crawler.Resume();
        _jobs[jobId].Status = JobStatus.Running;
    }

    /// <summary>Runs a finished job again with the same arguments.</summary>
    public JobRecord Retry(string jobId)
    {
        var old = Get(jobId);
        return RunNow(old.SpiderId, old.Arguments, old.SettingsOverrides, $"retry:{jobId}", old.Attempt + 1);
    }

    private void CancelPending(string jobId)
    {
        if (_pendingCancel.TryGetValue(jobId, out var cts)) cts.Cancel();
    }

    // ---- queries ----------------------------------------------------------------------------------

    public JobRecord Get(string jobId) => _jobs.TryGetValue(jobId, out var j) ? j : throw new KeyNotFoundException($"Job {jobId} not found.");

    /// <summary>Live crawler of a running job (for dashboards).</summary>
    public Crawler? GetCrawler(string jobId) => _crawlers.GetValueOrDefault(jobId);

    public IReadOnlyList<JobRecord> Running => [.. _jobs.Values.Where(j => j.Status is JobStatus.Running or JobStatus.Paused or JobStatus.Pending).OrderBy(j => j.CreatedAt)];

    /// <summary>Jobs newest first, optionally for one spider.</summary>
    public IReadOnlyList<JobRecord> History(string? spiderId = null, int limit = 100) =>
        [.. _jobs.Values.Where(j => spiderId is null || j.SpiderId == spiderId).OrderByDescending(j => j.CreatedAt).Take(limit)];

    /// <summary>Waits for a job to finish.</summary>
    public Task<JobRecord> WaitAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var job = Get(jobId);
        if (job.IsFinished) return Task.FromResult(job);
        return _completions.TryGetValue(jobId, out var tcs) ? tcs.Task.WaitAsync(cancellationToken) : Task.FromResult(job);
    }

    private void AppendHistory(JobRecord job)
    {
        if (_options.HistoryPath is null) return;
        lock (_historyGate)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(_options.HistoryPath));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.AppendAllText(_options.HistoryPath, JsonSerializer.Serialize(job, Json) + Environment.NewLine);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var id in _crawlers.Keys) await StopAsync(id).ConfigureAwait(false);
        foreach (var tcs in _completions.Values) await tcs.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
    }
}
