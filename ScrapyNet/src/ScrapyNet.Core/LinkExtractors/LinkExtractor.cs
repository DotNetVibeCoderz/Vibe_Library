using System.Text.RegularExpressions;
using AngleSharp.Dom;

namespace ScrapyNet;

/// <summary>A link found in a page.</summary>
/// <param name="Url">Absolute URL (fragment removed unless the extractor keeps it).</param>
/// <param name="Text">Anchor text, whitespace-normalized.</param>
/// <param name="Fragment">The <c>#fragment</c> part, without the hash.</param>
/// <param name="NoFollow">True when the element has <c>rel="nofollow"</c>.</param>
public sealed record Link(string Url, string Text = "", string Fragment = "", bool NoFollow = false);

/// <summary>
/// Extracts links from responses, with Scrapy's filtering rules: allow/deny regexes, allowed and
/// denied domains, restricting to page regions by CSS or XPath, tags/attributes to scan, and
/// ignored file extensions. Port of <c>scrapy.linkextractors.LinkExtractor</c>.
/// </summary>
/// <example>
/// <code>
/// var extractor = new LinkExtractor
/// {
///     Allow = [@"/catalogue/"],
///     Deny = [@"/category/"],
///     RestrictCss = ["article.product_pod"],
/// };
/// foreach (var link in extractor.ExtractLinks(response)) ...
/// </code>
/// </example>
public sealed class LinkExtractor
{
    private Regex[]? _allow;
    private Regex[]? _deny;
    private Regex[]? _restrictText;

    /// <summary>Regexes a URL must match (any of). Empty means everything is allowed.</summary>
    public IReadOnlyList<string> Allow { get; init; } = [];

    /// <summary>Regexes that exclude a URL (any of). Takes precedence over <see cref="Allow"/>.</summary>
    public IReadOnlyList<string> Deny { get; init; } = [];

    /// <summary>Only links to these domains (or their subdomains).</summary>
    public IReadOnlyList<string> AllowDomains { get; init; } = [];

    /// <summary>Never links to these domains (or their subdomains).</summary>
    public IReadOnlyList<string> DenyDomains { get; init; } = [];

    /// <summary>Only scan inside elements matching these CSS selectors.</summary>
    public IReadOnlyList<string> RestrictCss { get; init; } = [];

    /// <summary>Only scan inside elements matching these XPath expressions.</summary>
    public IReadOnlyList<string> RestrictXpaths { get; init; } = [];

    /// <summary>Regexes the link text must match (any of).</summary>
    public IReadOnlyList<string> RestrictText { get; init; } = [];

    /// <summary>Tags to consider. Default <c>a</c> and <c>area</c>.</summary>
    public IReadOnlyList<string> Tags { get; init; } = ["a", "area"];

    /// <summary>Attributes holding URLs. Default <c>href</c>.</summary>
    public IReadOnlyList<string> Attrs { get; init; } = ["href"];

    /// <summary>Canonicalize URLs (sorted query, no fragment). Off by default, as in Scrapy.</summary>
    public bool Canonicalize { get; init; }

    /// <summary>Remove duplicate URLs. On by default.</summary>
    public bool Unique { get; init; } = true;

    /// <summary>Extensions to ignore (without dot). Defaults to <see cref="UrlUtils.IgnoredExtensions"/>.</summary>
    public IReadOnlySet<string>? DenyExtensions { get; init; }

    /// <summary>Hook to rewrite or drop (return <c>null</c>) each raw attribute value, e.g. to unwrap <c>javascript:</c> links.</summary>
    public Func<string, string?>? ProcessValue { get; init; }

    /// <summary>Keep the <c>#fragment</c> in extracted URLs.</summary>
    public bool KeepFragments { get; init; }

    /// <summary>Extracts links from an HTML response. Non-HTML responses yield nothing.</summary>
    public List<Link> ExtractLinks(Response response)
    {
        if (response is not TextResponse text || response is XmlResponse or JsonResponse) return [];
        return ExtractLinks(text.Selector, text.BaseUrl);
    }

    /// <summary>Extracts links from a selector (document or element), resolving against <paramref name="baseUrl"/>.</summary>
    public List<Link> ExtractLinks(Selector root, string baseUrl)
    {
        _allow ??= Compile(Allow);
        _deny ??= Compile(Deny);
        _restrictText ??= Compile(RestrictText);
        var denyExtensions = DenyExtensions ?? UrlUtils.IgnoredExtensions;

        var regions = new List<Selector>();
        foreach (var css in RestrictCss) regions.AddRange(root.Css(css));
        foreach (var xp in RestrictXpaths) regions.AddRange(root.Xpath(xp));
        if (RestrictCss.Count == 0 && RestrictXpaths.Count == 0) regions.Add(root);

        var tagSelector = string.Join(", ", Tags);
        var links = new List<Link>();
        var seen = Unique ? new HashSet<string>(StringComparer.Ordinal) : null;
        var visited = new HashSet<INode>(ReferenceEqualityComparer.Instance);

        foreach (var region in regions)
        {
            foreach (var element in region.Css(tagSelector))
            {
                var el = (IElement)element.Node!;
                if (!visited.Add(el)) continue;
                foreach (var attr in Attrs)
                {
                    var raw = el.GetAttribute(attr);
                    if (raw is null) continue;
                    var value = ProcessValue is null ? raw : ProcessValue(raw);
                    if (value is null) continue;
                    value = value.Trim();
                    if (value.Length == 0 || value.StartsWith('#')) continue;

                    var absolute = UrlUtils.Join(baseUrl, value);
                    if (!absolute.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                        !absolute.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                        !absolute.StartsWith("file://", StringComparison.OrdinalIgnoreCase)) continue;

                    var fragment = "";
                    var hash = absolute.IndexOf('#');
                    if (hash >= 0)
                    {
                        fragment = absolute[(hash + 1)..];
                        if (!KeepFragments) absolute = absolute[..hash];
                    }
                    var url = Canonicalize ? UrlUtils.Canonicalize(absolute, KeepFragments) : absolute;

                    if (!IsAllowed(url, denyExtensions)) continue;
                    var linkText = TextUtils.NormalizeWhitespace(el.TextContent);
                    if (_restrictText.Length > 0 && !_restrictText.Any(r => r.IsMatch(linkText))) continue;
                    if (seen is not null && !seen.Add(url)) continue;

                    var rel = el.GetAttribute("rel") ?? "";
                    var noFollow = rel.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(r => r.Equals("nofollow", StringComparison.OrdinalIgnoreCase));
                    links.Add(new Link(url, linkText, fragment, noFollow));
                }
            }
        }
        return links;
    }

    /// <summary>True when the URL passes every filter.</summary>
    public bool Matches(string url)
    {
        _allow ??= Compile(Allow);
        _deny ??= Compile(Deny);
        return IsAllowed(url, DenyExtensions ?? UrlUtils.IgnoredExtensions);
    }

    private bool IsAllowed(string url, IReadOnlySet<string> denyExtensions)
    {
        if (_allow!.Length > 0 && !_allow.Any(r => r.IsMatch(url))) return false;
        if (_deny!.Length > 0 && _deny.Any(r => r.IsMatch(url))) return false;
        var host = UrlUtils.GetHost(url);
        if (AllowDomains.Count > 0 && !UrlUtils.HostMatchesAny(host, AllowDomains)) return false;
        if (DenyDomains.Count > 0 && UrlUtils.HostMatchesAny(host, DenyDomains)) return false;
        if (denyExtensions.Count > 0 && UrlUtils.HasAnyExtension(url, denyExtensions)) return false;
        return true;
    }

    private static Regex[] Compile(IReadOnlyList<string> patterns) =>
        [.. patterns.Select(p => RegexExtractor.GetRegex(p))];
}
