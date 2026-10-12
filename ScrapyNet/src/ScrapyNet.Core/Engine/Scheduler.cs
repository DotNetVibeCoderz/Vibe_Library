using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace ScrapyNet.Engine;

/// <summary>Stores requests until the engine asks for the next one (Scrapy's <c>SCHEDULER</c>).</summary>
public interface IScheduler
{
    ValueTask OpenAsync(Spider spider);

    ValueTask CloseAsync(string reason);

    bool HasPendingRequests { get; }

    int Count { get; }

    /// <summary>Queues a request. Returns false when it was rejected (e.g. as a duplicate).</summary>
    bool Enqueue(Request request);

    /// <summary>Next request, or <c>null</c> when empty.</summary>
    Request? Dequeue();
}

/// <summary>
/// Default scheduler: a priority queue (higher <see cref="Request.Priority"/> first) with LIFO or FIFO
/// ordering within a priority (<c>SCHEDULER_ORDER</c>), a duplicate filter, and pause/resume through
/// <c>JOBDIR</c>.
/// </summary>
/// <remarks>
/// LIFO (Scrapy's default) crawls depth-first, which keeps the pending queue short. FIFO crawls
/// breadth-first; combine it with a positive <c>DEPTH_PRIORITY</c> for a strict BFS.
/// <para>
/// With <c>JOBDIR</c>, requests live on disk from the moment they are scheduled (<c>requests.queue.log</c>,
/// see <see cref="DiskRequestJournal"/>), like Scrapy's disk queues: memory holds only their position,
/// and a crash or a kill keeps the queue. Requests whose callback cannot be named (lambdas) stay in
/// memory and are counted as <c>scheduler/unserializable</c>; they are lost if the crawl stops early.
/// </para>
/// </remarks>
public class Scheduler : IScheduler
{
    private readonly PriorityQueue<Entry, (int Priority, long Sequence)> _queue = new();
    private readonly object _gate = new();
    private readonly Crawler _crawler;
    private readonly ILogger _logger;
    private readonly bool _lifo;
    private readonly bool _debug;
    private readonly string? _jobDir;
    private DiskRequestJournal? _journal;
    private long _sequence;
    private Spider? _spider;

    /// <summary>A queued request: in memory, or at an offset in the journal.</summary>
    private readonly record struct Entry(Request? Request, int Priority, long Offset, int Length);

    public Scheduler(Crawler crawler, IDupeFilter? dupeFilter = null)
    {
        _crawler = crawler;
        _logger = crawler.LoggerFactory.CreateLogger("scrapynet.core.scheduler");
        DupeFilter = dupeFilter ?? CreateDupeFilter(crawler);
        _lifo = !string.Equals(crawler.Settings.GetString(SettingKeys.SchedulerOrder), "fifo", StringComparison.OrdinalIgnoreCase);
        _debug = crawler.Settings.GetBool(SettingKeys.SchedulerDebug);
        var jobDir = crawler.Settings.GetString(SettingKeys.JobDir);
        if (!string.IsNullOrEmpty(jobDir)) _jobDir = jobDir;
    }

    public IDupeFilter DupeFilter { get; }

    public int Count
    {
        get
        {
            lock (_gate) return _queue.Count;
        }
    }

    public bool HasPendingRequests => Count > 0;

    public async ValueTask OpenAsync(Spider spider)
    {
        _spider = spider;
        await DupeFilter.OpenAsync(spider).ConfigureAwait(false);
        if (_jobDir is null) return;

        _journal = DiskRequestJournal.Open(Path.Combine(_jobDir, "requests.queue.log"), out var pending);
        lock (_gate)
            foreach (var (priority, offset, length) in pending)
                PushEntry(new Entry(null, priority, offset, length));
        var restored = pending.Count + RestoreLegacyQueue(spider);
        if (restored > 0)
        {
            _logger.LogInformation("Resuming crawl ({Count} requests scheduled)", restored);
            _crawler.Stats.Inc("scheduler/restored", restored);
        }
    }

    /// <summary>Reads a <c>requests.queue</c> snapshot written by Scrapy.Net 0.1 into the journal.</summary>
    private int RestoreLegacyQueue(Spider spider)
    {
        var legacy = Path.Combine(_jobDir!, "requests.queue");
        if (!File.Exists(legacy)) return 0;
        var restored = 0;
        foreach (var line in File.ReadLines(legacy))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                Push(RequestSerializer.FromJson(JsonNode.Parse(line)!.AsObject(), spider));
                restored++;
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException)
            {
                _logger.LogWarning("Could not restore a request from {Path}: {Error}", legacy, ex.Message);
            }
        }
        File.Delete(legacy);
        return restored;
    }

    public async ValueTask CloseAsync(string reason)
    {
        if (_journal is not null)
        {
            int onDisk, inMemory;
            lock (_gate)
            {
                inMemory = _queue.UnorderedItems.Count(e => e.Element.Request is not null);
                onDisk = _queue.Count - inMemory;
                if (_queue.Count == 0)
                {
                    _journal.Delete();
                }
                else
                {
                    if (_journal.ShouldCompact) Compact();
                    _journal.Flush();
                    _journal.Dispose();
                }
                _journal = null;
            }
            if (onDisk > 0)
                _logger.LogInformation("Kept {Count} pending requests in {Path} (reason: {Reason})", onDisk, Path.Combine(_jobDir!, "requests.queue.log"), reason);
            if (inMemory > 0)
                _logger.LogWarning("{Count} pending requests could not be persisted (lambda callbacks); they are lost", inMemory);
        }
        await DupeFilter.CloseAsync(reason).ConfigureAwait(false);
    }

    public bool Enqueue(Request request)
    {
        if (!request.DontFilter && DupeFilter.RequestSeen(request))
        {
            DupeFilter.Log(request, _spider!);
            return false;
        }
        var onDisk = Push(request);
        if (_debug) _logger.LogDebug("Enqueued {Request} (priority {Priority})", request, request.Priority);
        _crawler.Stats.Inc("scheduler/enqueued");
        _crawler.Stats.Inc(onDisk ? "scheduler/enqueued/disk" : "scheduler/enqueued/memory");
        return true;
    }

    public Request? Dequeue()
    {
        Entry entry;
        string? json = null;
        lock (_gate)
        {
            if (!_queue.TryDequeue(out entry, out _)) return null;
            if (entry.Request is null)
            {
                json = _journal!.Pop(entry.Offset, entry.Length);
                if (_journal.ShouldCompact) Compact();
            }
        }
        _crawler.Stats.Inc("scheduler/dequeued");
        if (json is null)
        {
            _crawler.Stats.Inc("scheduler/dequeued/memory");
            return entry.Request;
        }
        _crawler.Stats.Inc("scheduler/dequeued/disk");
        return RequestSerializer.FromJson(JsonNode.Parse(json)!.AsObject(), _spider!);
    }

    /// <summary>Queues a request on disk when a journal is open and the request can be serialized.</summary>
    private bool Push(Request request)
    {
        if (_journal is not null)
        {
            if (RequestSerializer.ToJson(request) is { } json)
            {
                var text = json.ToJsonString();
                lock (_gate)
                {
                    var (offset, length) = _journal.Push(request.Priority, text);
                    PushEntry(new Entry(null, request.Priority, offset, length));
                }
                return true;
            }
            _crawler.Stats.Inc("scheduler/unserializable");
        }
        lock (_gate) PushEntry(new Entry(request, request.Priority, 0, 0));
        return false;
    }

    private void PushEntry(Entry entry)
    {
        var seq = ++_sequence;
        // PriorityQueue is a min-heap: negate priority so higher runs first; negate the sequence for LIFO.
        _queue.Enqueue(entry, (-entry.Priority, _lifo ? -seq : seq));
    }

    /// <summary>Rewrites the journal with live entries only, keeping queue order. Caller holds the lock.</summary>
    private void Compact()
    {
        var items = _queue.UnorderedItems.ToList();
        var onDisk = items.Where(i => i.Element.Request is null).ToList();
        var moved = _journal!.Compact([.. onDisk.Select(i => (i.Element.Priority, i.Element.Offset, i.Element.Length))]);
        _queue.Clear();
        var next = 0;
        foreach (var (element, priority) in items)
        {
            var updated = element.Request is null
                ? element with { Offset = moved[next].Offset, Length = moved[next++].Length }
                : element;
            _queue.Enqueue(updated, priority);
        }
        _crawler.Stats.Inc("scheduler/compactions");
    }

    private static IDupeFilter CreateDupeFilter(Crawler crawler)
    {
        var type = crawler.Settings.GetTypeSetting(SettingKeys.DupeFilterClass);
        if (type is null) return new RFPDupeFilter(crawler);
        return (IDupeFilter)(ComponentFactory.Create(type, crawler) ?? new NoDupeFilter());
    }
}
