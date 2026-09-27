using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace ScrapyNet.Engine;

/// <summary>
/// Coordinates the scheduler, downloader and scraper for one spider — Scrapy's
/// <c>ExecutionEngine</c>.
/// </summary>
/// <remarks>
/// <para>
/// The engine loop pulls start requests lazily, moves requests from the scheduler into the downloader
/// while neither the downloader (<c>CONCURRENT_REQUESTS</c>) nor the scraper (response bytes in
/// flight) asks it to back off, and sleeps on a wake-up signal otherwise. Every completed download,
/// scheduled request or unpause wakes it, so there is no polling interval to tune; a one-second
/// heartbeat only covers the idle check.
/// </para>
/// <para>
/// A spider closes as <c>finished</c> when start requests are exhausted, nothing is scheduled,
/// nothing is downloading or being scraped, and no <c>spider_idle</c> handler objects.
/// </para>
/// </remarks>
public sealed class ExecutionEngine
{
    private readonly Crawler _crawler;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly ConcurrentDictionary<Task, byte> _inFlight = new();
    private readonly CancellationTokenSource _killSource = new();
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IAsyncEnumerator<Request>? _start;
    private bool _startExhausted;
    private int _processing;
    private string? _closeReason;
    private volatile bool _paused;
    private Spider _spider = null!;

    internal ExecutionEngine(Crawler crawler)
    {
        _crawler = crawler;
        _logger = crawler.LoggerFactory.CreateLogger("scrapynet.core.engine");
        var schedulerType = crawler.Settings.GetTypeSetting(SettingKeys.Scheduler);
        Scheduler = schedulerType is null ? new Scheduler(crawler) : (IScheduler)ComponentFactory.Create(schedulerType, crawler)!;
        Downloader = new Downloader(crawler);
        Scraper = new Scraper(crawler, this);
    }

    public IScheduler Scheduler { get; }

    public Downloader Downloader { get; }

    public Scraper Scraper { get; }

    public bool IsPaused => _paused;

    public bool IsClosing => _closeReason is not null;

    /// <summary>Requests taken from the scheduler whose download or scraping has not finished.</summary>
    public int InProgress => Volatile.Read(ref _processing);

    /// <summary>Completes once the spider is open and the loop is running.</summary>
    public Task Started => _started.Task;

    internal async Task<string> RunAsync(CancellationToken cancellationToken)
    {
        _spider = _crawler.Spider!;
        using var registration = cancellationToken.Register(() => _ = CloseSpiderAsync("shutdown"));
        var stopwatch = Stopwatch.StartNew();
        _crawler.Stats.SetValue("start_time", DateTimeOffset.UtcNow);
        await _crawler.Signals.SendAsync(Signals.EngineStarted, new EngineEventArgs(_crawler)).ConfigureAwait(false);

        await OpenSpiderAsync().ConfigureAwait(false);
        _started.TrySetResult();
        try
        {
            await LoopAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Engine loop failed");
            _closeReason ??= "engine_error";
        }

        var reason = _closeReason ?? "finished";
        await CloseSpiderCoreAsync(reason, stopwatch).ConfigureAwait(false);
        await _crawler.Signals.SendAsync(Signals.EngineStopped, new EngineEventArgs(_crawler)).ConfigureAwait(false);
        return reason;
    }

    private async Task OpenSpiderAsync()
    {
        _logger.LogInformation("Spider opened");
        _crawler.Stats.OpenSpider(_spider);
        await Scheduler.OpenAsync(_spider).ConfigureAwait(false);
        await Downloader.Middleware.OpenSpiderAsync(_spider).ConfigureAwait(false);
        await Scraper.OpenSpiderAsync(_spider).ConfigureAwait(false);
        await _crawler.Signals.SendAsync(Signals.SpiderOpened, new SpiderEventArgs(_spider)).ConfigureAwait(false);
        await _spider.OnOpenedAsync().ConfigureAwait(false);
        var start = Scraper.SpiderMiddleware.ProcessStartRequests(_spider.StartAsync(_killSource.Token), _spider);
        _start = start.GetAsyncEnumerator(_killSource.Token);
    }

    private async Task LoopAsync()
    {
        var startBuffer = Math.Max(1, Downloader.TotalConcurrency);
        while (_closeReason is null)
        {
            if (!_paused)
            {
                // Start requests are pulled only while the queue is short, so a spider with millions of
                // start URLs never materialises them all.
                while (!_startExhausted && _closeReason is null && !NeedsBackout && Scheduler.Count < startBuffer)
                {
                    Request request;
                    try
                    {
                        if (!await _start!.MoveNextAsync().ConfigureAwait(false))
                        {
                            _startExhausted = true;
                            break;
                        }
                        request = _start.Current;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogError(ex, "Error while obtaining start requests");
                        _startExhausted = true;
                        break;
                    }
                    await CrawlAsync(request).ConfigureAwait(false);
                }

                while (!NeedsBackout && _closeReason is null && !_paused)
                {
                    var request = Scheduler.Dequeue();
                    if (request is null) break;
                    Launch(request);
                }
            }

            if (!_paused && _closeReason is null && IsIdle)
            {
                var keepOpen = await _crawler.Signals.SendAsync(Signals.SpiderIdle, new SpiderEventArgs(_spider)).ConfigureAwait(false);
                if (!keepOpen && IsIdle && _closeReason is null)
                {
                    _closeReason = "finished";
                    break;
                }
            }

            try
            {
                await _wake.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                break;
            }
        }
    }

    private bool NeedsBackout => _closeReason is not null || Downloader.NeedsBackout || Scraper.NeedsBackout;

    /// <summary>Nothing left to do: start requests consumed, queue empty, nothing downloading or scraping.</summary>
    public bool IsIdle => _startExhausted && Volatile.Read(ref _processing) == 0 && !Scheduler.HasPendingRequests;

    private void Launch(Request request)
    {
        Interlocked.Increment(ref _processing);
        var task = ProcessAsync(request);
        _inFlight.TryAdd(task, 0);
        _ = task.ContinueWith(t => _inFlight.TryRemove(t, out _), TaskScheduler.Default);
    }

    private async Task ProcessAsync(Request request)
    {
        try
        {
            object result;
            try
            {
                result = await Downloader.FetchAsync(request, _spider, _killSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_killSource.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                await Scraper.HandleDownloadErrorAsync(new Failure(ex, request), _spider).ConfigureAwait(false);
                return;
            }

            if (result is Request next)
            {
                await CrawlAsync(next).ConfigureAwait(false);
                return;
            }

            var response = (Response)result;
            if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("{Message}", LogFormatter.Crawled(request, response));
            _crawler.Stats.Inc("response_received_count");
            await _crawler.Signals.SendAsync(Signals.ResponseReceived, new ResponseEventArgs(response, request, _spider)).ConfigureAwait(false);
            await Scraper.ScrapeAsync(response, response.HasRequest ? response.Request : request, _spider).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error processing {Request}", request);
        }
        finally
        {
            Interlocked.Decrement(ref _processing);
            Wake();
        }
    }

    /// <summary>Schedules a request (Scrapy's <c>engine.crawl()</c>). Duplicates are dropped by the dupe filter.</summary>
    public async ValueTask CrawlAsync(Request request)
    {
        await _crawler.Signals.SendAsync(Signals.RequestScheduled, new RequestEventArgs(request, _spider)).ConfigureAwait(false);
        if (!Scheduler.Enqueue(request))
            await _crawler.Signals.SendAsync(Signals.RequestDropped, new RequestEventArgs(request, _spider)).ConfigureAwait(false);
        Wake();
    }

    /// <summary>
    /// Downloads a request right away through the downloader middlewares, bypassing the scheduler, and
    /// follows redirects (Scrapy's <c>engine.download()</c>). Used by media pipelines and robots.txt.
    /// </summary>
    public async Task<Response> DownloadAsync(Request request, CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _killSource.Token);
        for (var hops = 0; hops < 20; hops++)
        {
            var result = await Downloader.FetchAsync(request, _spider, linked.Token).ConfigureAwait(false);
            if (result is Response response)
            {
                _crawler.Stats.Inc("response_received_count");
                await _crawler.Signals.SendAsync(Signals.ResponseReceived, new ResponseEventArgs(response, request, _spider)).ConfigureAwait(false);
                return response;
            }
            request = (Request)result;
        }
        throw new InvalidOperationException($"Too many redirects or retries for {request}");
    }

    /// <summary>Requests a close with the given reason. Downloads in flight finish first.</summary>
    public Task CloseSpiderAsync(string reason = "cancelled")
    {
        if (_closeReason is null)
        {
            _closeReason = reason;
            _logger.LogInformation("Closing spider ({Reason})", reason);
        }
        Wake();
        return Task.CompletedTask;
    }

    /// <summary>Stops immediately, cancelling in-flight downloads.</summary>
    public void Kill()
    {
        _closeReason ??= "killed";
        _killSource.Cancel();
        Wake();
    }

    public void Pause()
    {
        _paused = true;
        _logger.LogInformation("Crawl paused");
    }

    public void Unpause()
    {
        _paused = false;
        _logger.LogInformation("Crawl resumed");
        Wake();
    }

    private void Wake()
    {
        try
        {
            if (_wake.CurrentCount == 0) _wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already signalled.
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task CloseSpiderCoreAsync(string reason, Stopwatch stopwatch)
    {
        // Let in-flight work finish (it may still schedule requests, which JOBDIR then persists).
        while (!_inFlight.IsEmpty)
        {
            try
            {
                await Task.WhenAll(_inFlight.Keys.ToArray()).ConfigureAwait(false);
            }
            catch
            {
                // Individual failures were already logged.
            }
        }

        if (_start is not null)
        {
            try
            {
                await _start.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or OperationCanceledException)
            {
            }
        }

        await Scheduler.CloseAsync(reason).ConfigureAwait(false);
        await Downloader.Middleware.CloseSpiderAsync(_spider, reason).ConfigureAwait(false);
        Downloader.Dispose();
        await Scraper.CloseSpiderAsync(_spider, reason).ConfigureAwait(false);

        var finish = DateTimeOffset.UtcNow;
        _crawler.Stats.SetValue("finish_time", finish);
        _crawler.Stats.SetValue("finish_reason", reason);
        _crawler.Stats.SetValue("elapsed_time_seconds", Math.Round(stopwatch.Elapsed.TotalSeconds, 6));

        await _crawler.Signals.SendAsync(Signals.SpiderClosed, new SpiderClosedEventArgs(_spider, reason)).ConfigureAwait(false);
        try
        {
            await _spider.OnClosedAsync(reason).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in {Spider}.OnClosedAsync", _spider.GetType().Name);
        }

        if (_crawler.Settings.GetBool(SettingKeys.StatsDump, true))
            _logger.LogInformation("Dumping Scrapy.Net stats:\n{Stats}", MemoryStatsCollector.Format(_crawler.Stats.GetStats()));
        _crawler.Stats.CloseSpider(_spider, reason);
        _logger.LogInformation("Spider closed ({Reason})", reason);
    }
}
