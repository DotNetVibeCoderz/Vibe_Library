using ScrapyGallery.Infrastructure;

namespace ScrapyGallery.Demos;

/// <summary>Every gallery entry, in reading order within each group.</summary>
public static class DemoCatalog
{
    public static readonly IReadOnlyList<Demo> All =
    [
        new SelectorsDemo(),
        new LoadersDemo(),
        new FirstSpiderDemo(),
        new CrawlRulesDemo(),
        new SitemapDemo(),
        new LoginDemo(),
        new ApiDemo(),
        new BrowserDemo(),
        new PipelinesDemo(),
        new FeedExportsDemo(),
        new MediaDemo(),
        new RetriesDemo(),
        new ProxiesDemo(),
        new ThrottleDemo(),
        new LinkGraphDemo(),
        new ResumeDemo(),
        new CacheDemo(),
        new RagDemo(),
        new DedupDemo(),
        new JobsDemo(),
        new RuleBuilderDemo(),
    ];
}
