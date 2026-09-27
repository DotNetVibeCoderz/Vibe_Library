using System.Reflection;

namespace ScrapyNet;

/// <summary>
/// Runs several crawlers in one process, like <c>scrapy.crawler.CrawlerRunner</c>. Use
/// <see cref="CrawlerProcess"/> in console apps to also get Ctrl+C handling.
/// </summary>
public class CrawlerRunner
{
    private readonly List<(Crawler Crawler, IReadOnlyDictionary<string, string>? Args)> _pending = [];
    private readonly List<Crawler> _crawlers = [];

    public CrawlerRunner(Settings? settings = null) => Settings = settings ?? new Settings();

    public Settings Settings { get; }

    public IReadOnlyList<Crawler> Crawlers => _crawlers;

    /// <summary>Optional loader used by <see cref="Crawl(string, IReadOnlyDictionary{string, string}?)"/>.</summary>
    public SpiderLoader? SpiderLoader { get; set; }

    public Crawler Crawl<TSpider>(IReadOnlyDictionary<string, string>? arguments = null) where TSpider : Spider => Crawl(typeof(TSpider), arguments);

    public Crawler Crawl(Type spiderType, IReadOnlyDictionary<string, string>? arguments = null) => Add(new Crawler(spiderType, Settings), arguments);

    public Crawler Crawl(Spider spider) => Add(new Crawler(spider, Settings), null);

    /// <summary>Schedules a spider by name, found with <see cref="SpiderLoader"/> (defaults to the entry assembly).</summary>
    public Crawler Crawl(string spiderName, IReadOnlyDictionary<string, string>? arguments = null)
    {
        SpiderLoader ??= SpiderLoader.FromAssemblies(Assembly.GetEntryAssembly()!);
        return Crawl(SpiderLoader.Load(spiderName), arguments);
    }

    private Crawler Add(Crawler crawler, IReadOnlyDictionary<string, string>? args)
    {
        _pending.Add((crawler, args));
        _crawlers.Add(crawler);
        return crawler;
    }

    /// <summary>Runs every scheduled crawler concurrently and waits for all to finish.</summary>
    public virtual async Task<IReadOnlyList<CrawlResult>> StartAsync(CancellationToken cancellationToken = default)
    {
        var batch = _pending.ToList();
        _pending.Clear();
        var results = await Task.WhenAll(batch.Select(p => p.Crawler.CrawlAsync(p.Args, cancellationToken))).ConfigureAwait(false);
        return results;
    }

    /// <summary>Stops all running crawlers gracefully.</summary>
    public Task StopAsync() => Task.WhenAll(_crawlers.Select(c => c.StopAsync()));
}

/// <summary>
/// A <see cref="CrawlerRunner"/> for console applications: the first Ctrl+C stops crawlers gracefully
/// (pending requests are saved when <c>JOBDIR</c> is set), a second one kills them.
/// Port of <c>scrapy.crawler.CrawlerProcess</c>.
/// </summary>
public sealed class CrawlerProcess(Settings? settings = null, bool installCancelHandler = true) : CrawlerRunner(settings)
{
    private int _cancelCount;

    public override async Task<IReadOnlyList<CrawlResult>> StartAsync(CancellationToken cancellationToken = default)
    {
        if (!installCancelHandler) return await base.StartAsync(cancellationToken).ConfigureAwait(false);

        ConsoleCancelEventHandler handler = (_, e) =>
        {
            e.Cancel = true;
            if (Interlocked.Increment(ref _cancelCount) == 1)
            {
                Console.Error.WriteLine("Received Ctrl+C, shutting down gracefully. Send again to force.");
                _ = StopAsync();
            }
            else
            {
                Console.Error.WriteLine("Received Ctrl+C twice, forcing unclean shutdown.");
                foreach (var c in Crawlers) c.Kill();
            }
        };
        Console.CancelKeyPress += handler;
        try
        {
            return await base.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }
    }
}

/// <summary>One-line entry points for scripts, notebooks and tests.</summary>
public static class Scrapy
{
    /// <summary>Runs one spider and returns its result.</summary>
    public static Task<CrawlResult> CrawlAsync<TSpider>(Action<Settings>? configure = null, IReadOnlyDictionary<string, string>? arguments = null,
        CancellationToken cancellationToken = default) where TSpider : Spider
    {
        var settings = new Settings();
        configure?.Invoke(settings);
        return new Crawler(typeof(TSpider), settings).CrawlAsync(arguments, cancellationToken);
    }

    /// <summary>Runs a spider instance and returns its result.</summary>
    public static Task<CrawlResult> CrawlAsync(Spider spider, Action<Settings>? configure = null, CancellationToken cancellationToken = default)
    {
        var settings = new Settings();
        configure?.Invoke(settings);
        return new Crawler(spider, settings).CrawlAsync(null, cancellationToken);
    }

    /// <summary>Runs a spider and also returns every scraped item, in the order they were scraped.</summary>
    public static async Task<(CrawlResult Result, List<object> Items)> CrawlAndCollectAsync<TSpider>(Action<Settings>? configure = null,
        IReadOnlyDictionary<string, string>? arguments = null, CancellationToken cancellationToken = default) where TSpider : Spider
    {
        var settings = new Settings();
        configure?.Invoke(settings);
        return await CrawlAndCollectAsync(new Crawler(typeof(TSpider), settings), arguments, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<(CrawlResult Result, List<object> Items)> CrawlAndCollectAsync(Spider spider, Action<Settings>? configure = null,
        CancellationToken cancellationToken = default)
    {
        var settings = new Settings();
        configure?.Invoke(settings);
        return await CrawlAndCollectAsync(new Crawler(spider, settings), null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs a spider instance with prepared settings.</summary>
    public static Task<CrawlResult> CrawlAsync(Spider spider, Settings settings, CancellationToken cancellationToken = default) =>
        new Crawler(spider, settings).CrawlAsync(null, cancellationToken);

    /// <summary>Runs a spider instance with prepared settings and returns every scraped item.</summary>
    public static async Task<(CrawlResult Result, List<object> Items)> CrawlAndCollectAsync(Spider spider, Settings settings, CancellationToken cancellationToken = default) =>
        await CrawlAndCollectAsync(new Crawler(spider, settings), null, cancellationToken).ConfigureAwait(false);

    private static async Task<(CrawlResult, List<object>)> CrawlAndCollectAsync(Crawler crawler, IReadOnlyDictionary<string, string>? arguments, CancellationToken ct)
    {
        var items = new List<object>();
        crawler.Signals.Connect(Signals.ItemScraped, e =>
        {
            lock (items) items.Add(e.Item);
        });
        var result = await crawler.CrawlAsync(arguments, ct).ConfigureAwait(false);
        return (result, items);
    }
}
