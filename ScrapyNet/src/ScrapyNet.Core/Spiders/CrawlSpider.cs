namespace ScrapyNet;

/// <summary>
/// A crawling rule for <see cref="CrawlSpider"/>: which links to extract, which callback parses the
/// pages they lead to, and whether to keep following links from those pages.
/// </summary>
public sealed class Rule
{
    private readonly bool? _follow;

    /// <summary>A rule that only follows links (no callback).</summary>
    public Rule(LinkExtractor? linkExtractor = null)
    {
        LinkExtractor = linkExtractor ?? new LinkExtractor();
    }

    public Rule(LinkExtractor linkExtractor, Func<Response, IAsyncEnumerable<object>> callback) : this(linkExtractor) => Callback = callback;

    public Rule(LinkExtractor linkExtractor, Func<Response, IEnumerable<object>> callback) : this(linkExtractor) => Callback = Callbacks.FromSync(callback);

    public LinkExtractor LinkExtractor { get; }

    public Func<Response, IAsyncEnumerable<object>>? Callback { get; }

    /// <summary>Follow links from matched pages. Defaults to true without a callback and false with one, as in Scrapy.</summary>
    public bool Follow
    {
        get => _follow ?? Callback is null;
        init => _follow = value;
    }

    /// <summary>Filter or reorder the extracted links.</summary>
    public Func<IReadOnlyList<Link>, IEnumerable<Link>>? ProcessLinks { get; init; }

    /// <summary>Modify or drop (return <c>null</c>) each generated request.</summary>
    public Func<Request, Response, Request?>? ProcessRequest { get; init; }

    public Func<Failure, IAsyncEnumerable<object>>? Errback { get; init; }

    public Dictionary<string, object?>? CbKwargs { get; init; }
}

/// <summary>
/// A spider driven by <see cref="Rule"/>s: it follows links matching the rules and hands pages to
/// the rule callbacks. Port of <c>scrapy.spiders.CrawlSpider</c>.
/// </summary>
/// <example>
/// <code>
/// public sealed class BooksSpider : CrawlSpider
/// {
///     public override IReadOnlyList&lt;string&gt; StartUrls => ["https://books.toscrape.com/"];
///     protected override IReadOnlyList&lt;Rule&gt; Rules =>
///     [
///         new(new LinkExtractor { Allow = ["/catalogue/page-"] }),                  // pagination: follow
///         new(new LinkExtractor { RestrictCss = ["article.product_pod h3"] }, ParseBook),
///     ];
///     private IEnumerable&lt;object&gt; ParseBook(Response r) { yield return new { Title = r.Css("h1::text").Get() }; }
/// }
/// </code>
/// </example>
public abstract class CrawlSpider : Spider
{
    private IReadOnlyList<Rule>? _rules;

    /// <summary>The crawling rules, evaluated once. The first rule matching a link wins.</summary>
    protected virtual IReadOnlyList<Rule> Rules => [];

    /// <summary>Set false to stop following links from any page (Scrapy's <c>CRAWLSPIDER_FOLLOW_LINKS</c>).</summary>
    public virtual bool FollowLinks => true;

    internal IReadOnlyList<Rule> CompiledRules => _rules ??= Rules;

    public override IAsyncEnumerable<object> Parse(Response response) => ParseWithRules(response, ParseStartUrl, cbKwargs: null, follow: true);

    /// <summary>Hook for the start URLs' responses; yields nothing by default.</summary>
    public virtual IAsyncEnumerable<object> ParseStartUrl(Response response) => AsyncEnumerable.Empty<object>();

    /// <summary>Hook applied to each callback's output.</summary>
    protected virtual IAsyncEnumerable<object> ProcessResults(Response response, IAsyncEnumerable<object> results) => results;

    /// <summary>Callback for pages reached through a rule. Named, so rule requests survive pause/resume.</summary>
    protected IAsyncEnumerable<object> ParseRuleResponse(Response response)
    {
        var index = response.Request.GetMeta<int>(MetaKeys.Rule);
        var rule = CompiledRules[index];
        return ParseWithRules(response, rule.Callback, rule.CbKwargs, rule.Follow);
    }

    /// <summary>Errback for rule requests; forwards to the rule's errback.</summary>
    protected async IAsyncEnumerable<object> ErrbackRuleRequest(Failure failure)
    {
        var index = failure.Request.GetMeta<int>(MetaKeys.Rule);
        var rule = CompiledRules[index];
        if (rule.Errback is null) yield break;
        await foreach (var r in rule.Errback(failure).ConfigureAwait(false)) yield return r;
    }

    private async IAsyncEnumerable<object> ParseWithRules(Response response, Func<Response, IAsyncEnumerable<object>>? callback,
        Dictionary<string, object?>? cbKwargs, bool follow)
    {
        if (callback is not null)
        {
            if (cbKwargs is not null)
                foreach (var (k, v) in cbKwargs) response.Request.CbKwargs.TryAdd(k, v);
            await foreach (var result in ProcessResults(response, callback(response)).ConfigureAwait(false))
                yield return result;
        }

        if (follow && FollowLinks)
            foreach (var request in RequestsToFollow(response))
                yield return request;
    }

    private IEnumerable<Request> RequestsToFollow(Response response)
    {
        if (response is not HtmlResponse) yield break;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rules = CompiledRules;
        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            IEnumerable<Link> links = rule.LinkExtractor.ExtractLinks(response).Where(l => seen.Add(l.Url)).ToList();
            if (rule.ProcessLinks is not null) links = rule.ProcessLinks(links.ToList());
            foreach (var link in links)
            {
                var request = new Request(link.Url, ParseRuleResponse)
                {
                    Errback = rule.Errback is null ? null : ErrbackRuleRequest,
                    Meta = { [MetaKeys.Rule] = i, ["link_text"] = link.Text },
                };
                var processed = rule.ProcessRequest is null ? request : rule.ProcessRequest(request, response);
                if (processed is not null) yield return processed;
            }
        }
    }
}
