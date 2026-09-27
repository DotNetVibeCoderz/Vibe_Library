using System.Text.Json.Serialization;
using ScrapyNet.Loaders;

namespace ScrapyNet.Platform;

/// <summary>What to crawl: where to start and which URLs are in scope.</summary>
public sealed record TargetConfig
{
    public string? BaseUrl { get; init; }
    public List<string> StartUrls { get; init; } = [];
    public List<string> AllowedDomains { get; init; } = [];

    /// <summary>Maximum link depth from the start URLs (0 = unlimited).</summary>
    public int MaxDepth { get; init; }

    /// <summary>Regexes a followed URL must match (any). Empty = all.</summary>
    public List<string> IncludePatterns { get; init; } = [];

    /// <summary>Regexes that exclude a URL from following.</summary>
    public List<string> ExcludePatterns { get; init; } = [];
}

/// <summary>How to extract one field.</summary>
public sealed record FieldRule
{
    /// <summary>CSS query (Scrapy syntax: <c>::text</c>, <c>::attr(name)</c>).</summary>
    public string? Css { get; init; }

    public string? Xpath { get; init; }

    /// <summary>Optional regex applied to the extracted values (first group or whole match).</summary>
    public string? Regex { get; init; }

    /// <summary>Keep every match as a list instead of the first one.</summary>
    public bool Multiple { get; init; }

    /// <summary>
    /// Transforms, in order: <c>strip</c>, <c>clean</c>, <c>lower</c>, <c>upper</c>, <c>remove_tags</c>,
    /// <c>price</c>, <c>int</c>, <c>decimal</c>, <c>absolute_url</c>, <c>date</c>.
    /// </summary>
    public List<string> Processors { get; init; } = [];

    public string? Default { get; init; }
}

/// <summary>
/// Extraction rules that describe a scraper without code: an optional item container selector, field
/// rules, links to follow (pagination) and links to detail pages.
/// </summary>
public sealed record ScrapingRules
{
    /// <summary>CSS selector of repeated item containers on a page; <c>null</c> = one item per page.</summary>
    public string? ItemSelector { get; init; }

    public Dictionary<string, FieldRule> Fields { get; init; } = [];

    /// <summary>CSS selectors of links to follow and parse with these same rules (pagination, categories).</summary>
    public List<string> FollowLinks { get; init; } = [];

    /// <summary>CSS selectors of links to detail pages; fields are then extracted from the detail pages.</summary>
    public List<string> DetailLinks { get; init; } = [];

    /// <summary>Items missing any of these fields are dropped.</summary>
    public List<string> RequiredFields { get; init; } = [];
}

/// <summary>Applies <see cref="ScrapingRules"/> to a page — shared by <see cref="DeclarativeSpider"/> and selector previews.</summary>
public static class RuleEvaluator
{
    /// <summary>Items found in a page by the rules (fields as a dictionary, plus <c>_url</c>).</summary>
    public static List<Dictionary<string, object?>> Extract(ScrapingRules rules, Selector root, string url)
    {
        var containers = string.IsNullOrWhiteSpace(rules.ItemSelector) ? [root] : root.Css(rules.ItemSelector).ToList();
        var items = new List<Dictionary<string, object?>>();
        foreach (var container in containers)
        {
            var item = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var (name, rule) in rules.Fields)
            {
                var value = ExtractField(rule, container, url);
                if (value is not null) item[name] = value;
            }
            if (rules.RequiredFields.Any(f => !item.TryGetValue(f, out var v) || v is null or "")) continue;
            if (item.Count == 0) continue;
            item["_url"] = url;
            items.Add(item);
        }
        return items;
    }

    public static object? ExtractField(FieldRule rule, Selector context, string url)
    {
        var selected = rule.Css is not null ? context.Css(rule.Css) : rule.Xpath is not null ? context.Xpath(rule.Xpath) : SelectorList.Empty;
        var values = rule.Regex is not null ? [.. selected.Re(rule.Regex)] : selected.GetAll();
        var processed = values.Select(v => (object?)v).ToList();
        foreach (var p in rule.Processors) processed = [.. processed.Select(v => Apply(p, v, url)).Where(v => v is not null)];
        if (processed.Count == 0) return rule.Default;
        return rule.Multiple ? processed : processed[0];
    }

    private static object? Apply(string processor, object? value, string url) => processor.ToLowerInvariant() switch
    {
        "strip" => Transforms.Strip(value),
        "clean" => Transforms.Clean(value),
        "lower" => Transforms.Lower(value),
        "upper" => Transforms.Upper(value),
        "remove_tags" => Transforms.RemoveTags(value),
        "price" => Transforms.Price(value),
        "int" => Transforms.ToInt(value),
        "decimal" => Transforms.ToDecimal(value),
        "date" => Transforms.ToDateTime(value),
        "absolute_url" => value is null ? null : UrlUtils.Join(url, value.ToString()!),
        _ => throw new ArgumentException($"Unknown processor '{processor}'."),
    };
}

/// <summary>A spider built entirely from a <see cref="SpiderDefinition"/> — no code, just JSON.</summary>
public sealed class DeclarativeSpider(SpiderDefinition definition) : Spider
{
    private readonly ScrapingRules _rules = definition.Rules ?? throw new ArgumentException("A declarative spider needs Rules.", nameof(definition));
    private readonly System.Text.RegularExpressions.Regex[] _include = [.. definition.Target.IncludePatterns.Select(p => RegexExtractor.GetRegex(p))];
    private readonly System.Text.RegularExpressions.Regex[] _exclude = [.. definition.Target.ExcludePatterns.Select(p => RegexExtractor.GetRegex(p))];

    public SpiderDefinition Definition { get; } = definition;

    public override string Name => Definition.Name;

    public override IReadOnlyList<string> AllowedDomains =>
        Definition.Target.AllowedDomains.Count > 0 ? Definition.Target.AllowedDomains
        : Definition.Target.StartUrls.Select(UrlUtils.GetHost).Where(h => h.Length > 0).Distinct().ToList();

    public override IReadOnlyList<string> StartUrls =>
        [.. Definition.Target.StartUrls.Select(u => Definition.Target.BaseUrl is null ? u : UrlUtils.Join(Definition.Target.BaseUrl, u))];

    public override void ConfigureSettings(Settings settings)
    {
        if (Definition.Target.MaxDepth > 0) settings.Set(SettingKeys.DepthLimit, Definition.Target.MaxDepth);
    }

    public override async IAsyncEnumerable<object> Parse(Response response)
    {
        if (_rules.DetailLinks.Count == 0)
            foreach (var item in RuleEvaluator.Extract(_rules, response.Selector, response.Url)) yield return item;

        foreach (var css in _rules.DetailLinks)
            foreach (var request in Follow(response, css, ParseDetail)) yield return request;
        foreach (var css in _rules.FollowLinks)
            foreach (var request in Follow(response, css, Parse)) yield return request;
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private IEnumerable<object> ParseDetail(Response response) => RuleEvaluator.Extract(_rules with { ItemSelector = null }, response.Selector, response.Url);

    private IEnumerable<Request> Follow(Response response, string css, Func<Response, IAsyncEnumerable<object>> callback)
    {
        foreach (var href in response.Css(css.Contains("::attr") ? css : css + "::attr(href)").GetAll())
        {
            var url = response.UrlJoin(href);
            if (!InScope(url)) continue;
            yield return new Request(url, callback);
        }
    }

    private IEnumerable<Request> Follow(Response response, string css, Func<Response, IEnumerable<object>> callback) =>
        Follow(response, css, Callbacks.FromSync(callback));

    private bool InScope(string url) =>
        (_include.Length == 0 || _include.Any(r => r.IsMatch(url))) && !_exclude.Any(r => r.IsMatch(url));
}

/// <summary>A spider as managed by the platform: identity, version, target, and either rules or a code type.</summary>
public sealed record SpiderDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public int Version { get; init; } = 1;
    public bool Enabled { get; init; } = true;
    public string? Description { get; init; }
    public TargetConfig Target { get; init; } = new();

    /// <summary>Rules for a declarative (no-code) spider.</summary>
    public ScrapingRules? Rules { get; init; }

    /// <summary>Assembly-qualified or full type name of a code spider (instead of <see cref="Rules"/>).</summary>
    public string? SpiderType { get; init; }

    /// <summary>Setting overrides for this spider's jobs. Values may reference secrets: <c>${secret:name}</c>.</summary>
    public Dictionary<string, object?> Settings { get; init; } = [];

    public List<string> Tags { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;

    [JsonIgnore]
    public bool IsDeclarative => Rules is not null && SpiderType is null;
}
