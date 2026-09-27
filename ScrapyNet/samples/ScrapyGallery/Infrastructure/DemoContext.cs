using ScrapyNet;
using ScrapyNet.Sandbox;

namespace ScrapyGallery.Infrastructure;

/// <summary>What a running demo gets: the sandbox site, its inputs, and helpers that wire crawlers to the visualizations.</summary>
public sealed class DemoContext(SandboxSite site, CrawlObserver observer, IReadOnlyDictionary<string, string> inputs, CancellationToken cancellationToken)
{
    public SandboxSite Site { get; } = site;

    public CrawlObserver Observer { get; } = observer;

    public CancellationToken CancellationToken { get; } = cancellationToken;

    public string Input(string key) => inputs.TryGetValue(key, out var v) ? v : "";

    /// <summary>Settings suited to the gallery: no periodic stats lines, no stats dump.</summary>
    public Settings NewSettings()
    {
        var s = new Settings();
        s.Set(SettingKeys.LogStatsInterval, 0);
        s.Set(SettingKeys.StatsDump, false);
        return s;
    }

    /// <summary>A crawler whose signals and log feed the web, charts, items and log tabs.</summary>
    public Crawler CreateCrawler(Spider spider, Settings settings)
    {
        var crawler = new Crawler(spider, settings, Observer.LoggerFactory);
        Observer.Attach(crawler);
        return crawler;
    }

    public Task<CrawlResult> CrawlAsync(Spider spider, Settings settings) =>
        CreateCrawler(spider, settings).CrawlAsync(cancellationToken: CancellationToken);

    public void Print(string text) => Observer.AppendOutput(text);
}
