using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace ScrapyNet.Engine;

/// <summary>Chains spider middlewares (Scrapy's <c>SpiderMiddlewareManager</c>).</summary>
public sealed class SpiderMiddlewareManager
{
    private readonly SpiderMiddleware[] _middlewares;

    public SpiderMiddlewareManager(Crawler crawler)
    {
        var list = new List<SpiderMiddleware>();
        foreach (var entry in crawler.Settings.SpiderMiddlewares.GetEnabled())
        {
            var mw = ComponentFactory.Create<SpiderMiddleware>(entry, crawler);
            if (mw is not null) list.Add(mw);
        }
        _middlewares = [.. list];
        crawler.LoggerFactory.CreateLogger("scrapynet.middleware").LogInformation("Enabled spider middlewares:\n[{Middlewares}]",
            string.Join(",\n ", _middlewares.Select(m => $"'{m.GetType().FullName}'")));
    }

    public IReadOnlyList<SpiderMiddleware> Middlewares => _middlewares;

    public async ValueTask OpenSpiderAsync(Spider spider)
    {
        foreach (var mw in _middlewares) await mw.OpenSpiderAsync(spider).ConfigureAwait(false);
    }

    public async ValueTask CloseSpiderAsync(Spider spider, string reason)
    {
        foreach (var mw in _middlewares) await mw.CloseSpiderAsync(spider, reason).ConfigureAwait(false);
    }

    public async ValueTask ProcessInputAsync(Response response, Spider spider)
    {
        foreach (var mw in _middlewares) await mw.ProcessSpiderInputAsync(response, spider).ConfigureAwait(false);
    }

    /// <summary>Wraps callback output; the middleware closest to the spider (highest order) runs first.</summary>
    public IAsyncEnumerable<object> ProcessOutput(Response response, IAsyncEnumerable<object> output, Spider spider)
    {
        for (var i = _middlewares.Length - 1; i >= 0; i--) output = _middlewares[i].ProcessSpiderOutput(response, output, spider);
        return output;
    }

    public IAsyncEnumerable<object>? ProcessException(Response response, Exception exception, Spider spider)
    {
        for (var i = _middlewares.Length - 1; i >= 0; i--)
        {
            var result = _middlewares[i].ProcessSpiderException(response, exception, spider);
            if (result is not null) return result;
        }
        return null;
    }

    public IAsyncEnumerable<Request> ProcessStartRequests(IAsyncEnumerable<Request> start, Spider spider)
    {
        foreach (var mw in _middlewares) start = mw.ProcessStartRequests(start, spider);
        return start;
    }
}

/// <summary>Runs items through the item pipelines in order (Scrapy's <c>ItemPipelineManager</c>).</summary>
public sealed class ItemPipelineManager
{
    private readonly ItemPipeline[] _pipelines;

    public ItemPipelineManager(Crawler crawler)
    {
        var list = new List<ItemPipeline>();
        foreach (var entry in crawler.Settings.ItemPipelines.GetEnabled())
        {
            var p = ComponentFactory.Create<ItemPipeline>(entry, crawler);
            if (p is not null) list.Add(p);
        }
        _pipelines = [.. list];
        crawler.LoggerFactory.CreateLogger("scrapynet.middleware").LogInformation("Enabled item pipelines:\n[{Pipelines}]",
            string.Join(",\n ", _pipelines.Select(m => $"'{m.GetType().FullName}'")));
    }

    public IReadOnlyList<ItemPipeline> Pipelines => _pipelines;

    public async ValueTask OpenSpiderAsync(Spider spider)
    {
        foreach (var p in _pipelines) await p.OpenSpiderAsync(spider).ConfigureAwait(false);
    }

    public async ValueTask CloseSpiderAsync(Spider spider)
    {
        foreach (var p in _pipelines) await p.CloseSpiderAsync(spider).ConfigureAwait(false);
    }

    public async ValueTask<object> ProcessItemAsync(object item, Spider spider)
    {
        foreach (var p in _pipelines)
        {
            item = await p.ProcessItemAsync(item, spider).ConfigureAwait(false)
                ?? throw new DropItemException($"{p.GetType().Name} returned null");
        }
        return item;
    }
}

/// <summary>
/// Feeds responses to spider callbacks and routes their output: requests back to the engine, items
/// through the pipelines. Port of <c>scrapy.core.scraper.Scraper</c>.
/// </summary>
public sealed class Scraper
{
    private readonly Crawler _crawler;
    private readonly ExecutionEngine _engine;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _itemSlots;
    private readonly long _maxActiveSize;
    private long _activeSize;
    private int _activeItems;

    public Scraper(Crawler crawler, ExecutionEngine engine)
    {
        _crawler = crawler;
        _engine = engine;
        _logger = crawler.LoggerFactory.CreateLogger("scrapynet.core.scraper");
        _itemSlots = new SemaphoreSlim(Math.Max(1, crawler.Settings.GetInt(SettingKeys.ConcurrentItems, 100)));
        _maxActiveSize = crawler.Settings.GetLong(SettingKeys.ScraperSlotMaxActiveSize, 5_000_000);
        SpiderMiddleware = new SpiderMiddlewareManager(crawler);
        Pipelines = new ItemPipelineManager(crawler);
    }

    public SpiderMiddlewareManager SpiderMiddleware { get; }

    public ItemPipelineManager Pipelines { get; }

    /// <summary>Too many response bytes are being processed; the engine should pause downloading.</summary>
    public bool NeedsBackout => Interlocked.Read(ref _activeSize) > _maxActiveSize;

    public int ActiveItems => Volatile.Read(ref _activeItems);

    public async ValueTask OpenSpiderAsync(Spider spider)
    {
        await SpiderMiddleware.OpenSpiderAsync(spider).ConfigureAwait(false);
        await Pipelines.OpenSpiderAsync(spider).ConfigureAwait(false);
    }

    public async ValueTask CloseSpiderAsync(Spider spider, string reason)
    {
        await Pipelines.CloseSpiderAsync(spider).ConfigureAwait(false);
        await SpiderMiddleware.CloseSpiderAsync(spider, reason).ConfigureAwait(false);
    }

    /// <summary>Runs the callback for a downloaded response and handles everything it yields.</summary>
    public async Task ScrapeAsync(Response response, Request request, Spider spider)
    {
        var size = Math.Max(response.Body.Length, 1024);
        Interlocked.Add(ref _activeSize, size);
        try
        {
            IAsyncEnumerable<object> output;
            try
            {
                await SpiderMiddleware.ProcessInputAsync(response, spider).ConfigureAwait(false);
                var callback = request.Callback ?? spider.Parse;
                output = SpiderMiddleware.ProcessOutput(response, Guard(callback, response), spider);
            }
            catch (Exception ex)
            {
                // Input rejected (typically HttpError): errback if the request has one, else the
                // middlewares' exception hooks.
                if (request.Errback is not null)
                {
                    output = SpiderMiddleware.ProcessOutput(response, request.Errback(new Failure(ex, request)), spider);
                }
                else
                {
                    var replacement = SpiderMiddleware.ProcessException(response, ex, spider);
                    if (replacement is null)
                    {
                        await HandleSpiderErrorAsync(ex, request, response, spider).ConfigureAwait(false);
                        return;
                    }
                    output = replacement;
                }
            }
            await HandleOutputAsync(output, request, response, spider).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Add(ref _activeSize, -size);
        }
    }

    /// <summary>A download failed: call the errback, or log the error.</summary>
    public async Task HandleDownloadErrorAsync(Failure failure, Spider spider)
    {
        var request = failure.Request;
        if (request.Errback is not null)
        {
            var response = failure.Response;
            var output = request.Errback(failure);
            if (response is not null) output = SpiderMiddleware.ProcessOutput(response, output, spider);
            await HandleOutputAsync(output, request, response, spider).ConfigureAwait(false);
            return;
        }
        if (failure.Exception is IgnoreRequestException) return;
        _logger.LogError("Error downloading {Request}: {Error}", request, DescribeError(failure.Exception));
    }

    private static string DescribeError(Exception ex) =>
        ex switch
        {
            HttpRequestException { InnerException: { } inner } => $"{ex.Message} ({inner.Message})",
            _ => ex.Message,
        };

    private async Task HandleOutputAsync(IAsyncEnumerable<object> output, Request request, Response? response, Spider spider)
    {
        var enumerator = output.GetAsyncEnumerator();
        try
        {
            while (true)
            {
                object? result;
                try
                {
                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false)) break;
                    result = enumerator.Current;
                }
                catch (CloseSpiderException cse)
                {
                    await _engine.CloseSpiderAsync(cse.Reason).ConfigureAwait(false);
                    break;
                }
                catch (Exception ex)
                {
                    var replacement = response is null ? null : SpiderMiddleware.ProcessException(response, ex, spider);
                    if (replacement is not null)
                    {
                        await HandleOutputAsync(replacement, request, response, spider).ConfigureAwait(false);
                    }
                    else
                    {
                        await HandleSpiderErrorAsync(ex, request, response, spider).ConfigureAwait(false);
                    }
                    break;
                }
                await HandleResultAsync(result, response, spider).ConfigureAwait(false);
            }
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async ValueTask HandleResultAsync(object? result, Response? response, Spider spider)
    {
        switch (result)
        {
            case null:
                return;
            case Request request:
                await _engine.CrawlAsync(request).ConfigureAwait(false);
                return;
            case IEnumerable<Request> requests:
                foreach (var r in requests) await _engine.CrawlAsync(r).ConfigureAwait(false);
                return;
            default:
                await ProcessItemAsync(result, response, spider).ConfigureAwait(false);
                return;
        }
    }

    /// <summary>Sends one item through the pipelines, honouring <c>CONCURRENT_ITEMS</c>.</summary>
    public async Task ProcessItemAsync(object item, Response? response, Spider spider)
    {
        await _itemSlots.WaitAsync().ConfigureAwait(false);
        Interlocked.Increment(ref _activeItems);
        try
        {
            object output;
            try
            {
                output = await Pipelines.ProcessItemAsync(item, spider).ConfigureAwait(false);
            }
            catch (DropItemException ex)
            {
                _logger.LogWarning("{Message}", LogFormatter.Dropped(item, ex, response));
                _crawler.Stats.Inc("item_dropped_count");
                _crawler.Stats.Inc($"item_dropped_reasons_count/{ex.GetType().Name}");
                await _crawler.Signals.SendAsync(Signals.ItemDropped, new ItemDroppedEventArgs(item, response, ex, spider)).ConfigureAwait(false);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing {Item}", ItemAdapter.For(item));
                _crawler.Stats.Inc("item_error_count");
                await _crawler.Signals.SendAsync(Signals.ItemError, new ItemErrorEventArgs(item, response, ex, spider)).ConfigureAwait(false);
                return;
            }

            if (_logger.IsEnabled(LogLevel.Debug)) _logger.LogDebug("{Message}", LogFormatter.Scraped(output, response));
            _crawler.Stats.Inc("item_scraped_count");
            await _crawler.Signals.SendAsync(Signals.ItemScraped, new ItemEventArgs(output, response, spider)).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref _activeItems);
            _itemSlots.Release();
        }
    }

    private async Task HandleSpiderErrorAsync(Exception exception, Request request, Response? response, Spider spider)
    {
        if (exception is CloseSpiderException cse)
        {
            await _engine.CloseSpiderAsync(cse.Reason).ConfigureAwait(false);
            return;
        }
        var referer = request.Headers.Get("Referer") ?? "None";
        _logger.LogError(exception, "Spider error processing {Request} (referer: {Referer})", request, referer);
        _crawler.Stats.Inc($"spider_exceptions/{exception.GetType().Name}");
        await _crawler.Signals.SendAsync(Signals.SpiderError, new SpiderErrorEventArgs(new Failure(exception, request), response, spider)).ConfigureAwait(false);
    }

    /// <summary>
    /// Calls the callback lazily so that an exception thrown synchronously (before the first yield)
    /// surfaces during enumeration, where the error handling lives.
    /// </summary>
    private static async IAsyncEnumerable<object> Guard(Func<Response, IAsyncEnumerable<object>> callback, Response response,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var item in callback(response).WithCancellation(ct).ConfigureAwait(false)) yield return item;
    }
}
