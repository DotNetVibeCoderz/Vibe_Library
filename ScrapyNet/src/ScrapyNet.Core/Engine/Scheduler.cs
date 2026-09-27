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
/// With <c>JOBDIR</c>, pending requests are written to <c>requests.queue</c> when the spider closes for
/// any reason other than <c>finished</c>, and read back on the next start.
/// </remarks>
public class Scheduler : IScheduler
{
    private readonly PriorityQueue<Request, (int Priority, long Sequence)> _queue = new();
    private readonly object _gate = new();
    private readonly Crawler _crawler;
    private readonly ILogger _logger;
    private readonly bool _lifo;
    private readonly bool _debug;
    private readonly string? _queuePath;
    private long _sequence;
    private Spider? _spider;

    public Scheduler(Crawler crawler, IDupeFilter? dupeFilter = null)
    {
        _crawler = crawler;
        _logger = crawler.LoggerFactory.CreateLogger("scrapynet.core.scheduler");
        DupeFilter = dupeFilter ?? CreateDupeFilter(crawler);
        _lifo = !string.Equals(crawler.Settings.GetString(SettingKeys.SchedulerOrder), "fifo", StringComparison.OrdinalIgnoreCase);
        _debug = crawler.Settings.GetBool(SettingKeys.SchedulerDebug);
        var jobDir = crawler.Settings.GetString(SettingKeys.JobDir);
        if (!string.IsNullOrEmpty(jobDir)) _queuePath = Path.Combine(jobDir, "requests.queue");
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
        if (_queuePath is not null && File.Exists(_queuePath))
        {
            var restored = 0;
            foreach (var line in File.ReadLines(_queuePath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var request = RequestSerializer.FromJson(JsonNode.Parse(line)!.AsObject(), spider);
                    Push(request);
                    restored++;
                }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException)
                {
                    _logger.LogWarning("Could not restore a request from {Path}: {Error}", _queuePath, ex.Message);
                }
            }
            File.Delete(_queuePath);
            _logger.LogInformation("Resuming crawl ({Count} requests scheduled)", restored);
            _crawler.Stats.Inc("scheduler/restored", restored);
        }
    }

    public async ValueTask CloseAsync(string reason)
    {
        if (_queuePath is not null)
        {
            List<Request> pending;
            lock (_gate)
            {
                pending = [];
                while (_queue.TryDequeue(out var r, out _)) pending.Add(r);
            }
            if (pending.Count > 0)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_queuePath)!);
                var written = 0;
                await using (var writer = new StreamWriter(_queuePath, append: false))
                {
                    foreach (var request in pending)
                    {
                        var json = RequestSerializer.ToJson(request);
                        if (json is null)
                        {
                            _crawler.Stats.Inc("scheduler/unserializable");
                            continue;
                        }
                        await writer.WriteLineAsync(json.ToJsonString()).ConfigureAwait(false);
                        written++;
                    }
                }
                _logger.LogInformation("Saved {Count} pending requests to {Path} (reason: {Reason})", written, _queuePath, reason);
            }
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
        Push(request);
        if (_debug) _logger.LogDebug("Enqueued {Request} (priority {Priority})", request, request.Priority);
        _crawler.Stats.Inc("scheduler/enqueued");
        _crawler.Stats.Inc("scheduler/enqueued/memory");
        return true;
    }

    public Request? Dequeue()
    {
        Request? request;
        lock (_gate)
        {
            if (!_queue.TryDequeue(out request, out _)) return null;
        }
        _crawler.Stats.Inc("scheduler/dequeued");
        _crawler.Stats.Inc("scheduler/dequeued/memory");
        return request;
    }

    private void Push(Request request)
    {
        lock (_gate)
        {
            var seq = ++_sequence;
            // PriorityQueue is a min-heap: negate priority so higher runs first; negate the sequence for LIFO.
            _queue.Enqueue(request, (-request.Priority, _lifo ? -seq : seq));
        }
    }

    private static IDupeFilter CreateDupeFilter(Crawler crawler)
    {
        var type = crawler.Settings.GetTypeSetting(SettingKeys.DupeFilterClass);
        if (type is null) return new RFPDupeFilter(crawler);
        return (IDupeFilter)(ComponentFactory.Create(type, crawler) ?? new NoDupeFilter());
    }
}
