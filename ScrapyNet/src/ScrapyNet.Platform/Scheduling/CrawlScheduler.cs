using System.Text.Json;

namespace ScrapyNet.Platform;

/// <summary>A recurring run of a spider.</summary>
public sealed record ScheduleEntry
{
    public required string Id { get; init; }
    public required string SpiderId { get; init; }

    /// <summary>Cron expression (see <see cref="CronExpression"/> and <see cref="Schedules"/>).</summary>
    public required string Cron { get; init; }

    public bool Enabled { get; init; } = true;

    /// <summary>IANA or Windows time zone id; UTC when null.</summary>
    public string? TimeZone { get; init; }

    public Dictionary<string, string> Arguments { get; init; } = [];

    /// <summary>Start a run even if the previous one of this spider is still going.</summary>
    public bool AllowOverlap { get; init; }

    public DateTimeOffset? NextRun { get; set; }
    public DateTimeOffset? LastRun { get; set; }
    public string? LastJobId { get; set; }
}

/// <summary>
/// Triggers jobs from cron schedules. Call <see cref="Start"/> to run it in the background or
/// <see cref="TickAsync"/> from your own loop. Schedules can be persisted to a JSON file.
/// </summary>
public sealed class CrawlScheduler : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(ScrapyJson.Web) { WriteIndented = true };

    private readonly JobManager _jobs;
    private readonly Dictionary<string, ScheduleEntry> _entries = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly string? _path;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public CrawlScheduler(JobManager jobs, string? path = null)
    {
        _jobs = jobs;
        _path = path;
        if (path is not null && File.Exists(path))
            foreach (var e in JsonSerializer.Deserialize<List<ScheduleEntry>>(File.ReadAllText(path), Json) ?? []) _entries[e.Id] = e;
    }

    public event Action<ScheduleEntry, JobRecord>? Triggered;

    public ScheduleEntry Add(ScheduleEntry entry)
    {
        var cron = new CronExpression(entry.Cron);
        entry.NextRun = cron.GetNextOccurrence(DateTimeOffset.UtcNow, Zone(entry));
        lock (_gate) _entries[entry.Id] = entry;
        Persist();
        return entry;
    }

    public bool Remove(string id)
    {
        bool removed;
        lock (_gate) removed = _entries.Remove(id);
        Persist();
        return removed;
    }

    public ScheduleEntry SetEnabled(string id, bool enabled)
    {
        lock (_gate)
        {
            var updated = _entries[id] with { Enabled = enabled };
            _entries[id] = updated;
            Persist();
            return updated;
        }
    }

    public IReadOnlyList<ScheduleEntry> List()
    {
        lock (_gate) return [.. _entries.Values.OrderBy(e => e.NextRun)];
    }

    /// <summary>Starts every due schedule. Returns the jobs started.</summary>
    public Task<IReadOnlyList<JobRecord>> TickAsync(DateTimeOffset? now = null)
    {
        var at = now ?? DateTimeOffset.UtcNow;
        var started = new List<JobRecord>();
        List<ScheduleEntry> due;
        lock (_gate) due = [.. _entries.Values.Where(e => e.Enabled && e.NextRun is { } n && n <= at)];
        foreach (var entry in due)
        {
            entry.NextRun = new CronExpression(entry.Cron).GetNextOccurrence(at, Zone(entry));
            if (!entry.AllowOverlap && _jobs.Running.Any(j => j.SpiderId == entry.SpiderId)) continue;
            var job = _jobs.RunNow(entry.SpiderId, entry.Arguments, trigger: $"schedule:{entry.Id}");
            entry.LastRun = at;
            entry.LastJobId = job.Id;
            started.Add(job);
            Triggered?.Invoke(entry, job);
        }
        if (due.Count > 0) Persist();
        return Task.FromResult<IReadOnlyList<JobRecord>>(started);
    }

    /// <summary>Runs <see cref="TickAsync"/> every <paramref name="interval"/> (default 15 s) until disposed.</summary>
    public void Start(TimeSpan? interval = null)
    {
        if (_loop is not null) return;
        _cts = new CancellationTokenSource();
        var period = interval ?? TimeSpan.FromSeconds(15);
        _loop = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(period);
            try
            {
                while (await timer.WaitForNextTickAsync(_cts.Token).ConfigureAwait(false)) await TickAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    private static TimeZoneInfo Zone(ScheduleEntry e) => e.TimeZone is null ? TimeZoneInfo.Utc : TimeZoneInfo.FindSystemTimeZoneById(e.TimeZone);

    private void Persist()
    {
        if (_path is null) return;
        List<ScheduleEntry> snapshot;
        lock (_gate) snapshot = [.. _entries.Values];
        var dir = Path.GetDirectoryName(Path.GetFullPath(_path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(_path, JsonSerializer.Serialize(snapshot, Json));
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is not null) await _cts.CancelAsync().ConfigureAwait(false);
        if (_loop is not null) await _loop.ConfigureAwait(false);
        _cts?.Dispose();
    }
}
