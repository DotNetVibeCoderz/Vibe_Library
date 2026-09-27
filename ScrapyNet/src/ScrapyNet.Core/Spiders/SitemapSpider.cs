using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml;

namespace ScrapyNet;

/// <summary>One <c>&lt;url&gt;</c> (or <c>&lt;sitemap&gt;</c>) entry of a sitemap.</summary>
public sealed record SitemapEntry(string Loc, DateTimeOffset? LastMod = null, string? ChangeFreq = null, double? Priority = null)
{
    /// <summary><c>xhtml:link rel="alternate"</c> URLs (other languages of the same page).</summary>
    public IReadOnlyList<string> Alternates { get; init; } = [];
}

/// <summary>A parsed sitemap: either a URL set or an index of other sitemaps.</summary>
public sealed record Sitemap(bool IsIndex, IReadOnlyList<SitemapEntry> Entries)
{
    /// <summary>
    /// Parses sitemap XML with a streaming reader. DTDs are prohibited and external entities are never
    /// resolved, so a hostile sitemap cannot trigger XXE or entity expansion attacks.
    /// </summary>
    public static Sitemap Parse(ReadOnlySpan<byte> body)
    {
        var entries = new List<SitemapEntry>();
        var isIndex = false;
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreWhitespace = true,
            IgnoreProcessingInstructions = true,
        };
        using var stream = new MemoryStream(body.ToArray());
        using var reader = XmlReader.Create(stream, settings);
        try
        {
            string? loc = null, changefreq = null;
            DateTimeOffset? lastmod = null;
            double? priority = null;
            var alternates = new List<string>();
            var depthOfEntry = -1;

            // ReadElementContentAsString leaves the reader on the node after the element; "advanced" makes
            // the loop look at that node instead of skipping it with another Read().
            var advanced = false;
            while (advanced || reader.Read())
            {
                advanced = false;
                if (reader.NodeType == XmlNodeType.Element)
                {
                    var name = reader.LocalName;
                    if (reader.Depth == 0)
                    {
                        isIndex = name == "sitemapindex";
                        continue;
                    }
                    if (name is "url" or "sitemap" && depthOfEntry < 0)
                    {
                        depthOfEntry = reader.Depth;
                        loc = changefreq = null;
                        lastmod = null;
                        priority = null;
                        alternates = [];
                        if (reader.IsEmptyElement) depthOfEntry = -1;
                        continue;
                    }
                    if (depthOfEntry < 0) continue;
                    switch (name)
                    {
                        case "loc":
                            loc = reader.ReadElementContentAsString().Trim();
                            advanced = true;
                            break;
                        case "lastmod":
                            var text = reader.ReadElementContentAsString().Trim();
                            advanced = true;
                            if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)) lastmod = d;
                            break;
                        case "changefreq":
                            changefreq = reader.ReadElementContentAsString().Trim();
                            advanced = true;
                            break;
                        case "priority":
                            if (double.TryParse(reader.ReadElementContentAsString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var p)) priority = p;
                            advanced = true;
                            break;
                        case "link":
                            if (reader.GetAttribute("rel") == "alternate" && reader.GetAttribute("href") is { } href) alternates.Add(href.Trim());
                            break;
                    }
                }
                else if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depthOfEntry)
                {
                    if (!string.IsNullOrEmpty(loc))
                        entries.Add(new SitemapEntry(loc, lastmod, changefreq, priority) { Alternates = alternates });
                    depthOfEntry = -1;
                }
            }
        }
        catch (XmlException)
        {
            // Truncated or malformed sitemaps: keep what was parsed so far, as Scrapy does with recover=True.
        }
        return new Sitemap(isIndex, entries);
    }

    /// <summary>Decompresses gzip bodies (sitemap.xml.gz) — detected by magic bytes, not by the URL.</summary>
    public static byte[] GetBody(Response response, long maxSize = 50L * 1024 * 1024)
    {
        var body = response.Body;
        if (body.Length > 2 && body[0] == 0x1F && body[1] == 0x8B)
        {
            using var input = new GZipStream(new MemoryStream(body), CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                output.Write(buffer, 0, read);
                // Guard against decompression bombs.
                if (output.Length > maxSize) throw new InvalidDataException($"Sitemap {response.Url} exceeds {maxSize} bytes when decompressed.");
            }
            return output.ToArray();
        }
        return body;
    }

    /// <summary>Sitemap URLs declared in a robots.txt body.</summary>
    public static IEnumerable<string> UrlsFromRobots(string robotsText, string baseUrl)
    {
        foreach (var raw in robotsText.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("sitemap:", StringComparison.OrdinalIgnoreCase))
                yield return UrlUtils.Join(baseUrl, line[8..].Trim());
        }
    }
}

/// <summary>A sitemap rule: URLs matching <see cref="Pattern"/> go to <see cref="Callback"/>.</summary>
public sealed class SitemapRule
{
    public SitemapRule(string pattern, Func<Response, IAsyncEnumerable<object>> callback)
    {
        Pattern = RegexExtractor.GetRegex(pattern);
        Callback = callback;
    }

    public SitemapRule(string pattern, Func<Response, IEnumerable<object>> callback) : this(pattern, Callbacks.FromSync(callback)) { }

    public Regex Pattern { get; }

    public Func<Response, IAsyncEnumerable<object>> Callback { get; }
}

/// <summary>
/// Crawls a site from its sitemaps (including sitemap indexes, gzipped sitemaps and sitemaps listed in
/// robots.txt). Port of <c>scrapy.spiders.SitemapSpider</c>.
/// </summary>
public abstract class SitemapSpider : Spider
{
    private IReadOnlyList<SitemapRule>? _rules;
    private Regex[]? _follow;

    /// <summary>Sitemap URLs, or robots.txt URLs to read sitemap locations from.</summary>
    public virtual IReadOnlyList<string> SitemapUrls => [];

    /// <summary>URL pattern → callback. The first matching rule handles a URL. Defaults to everything → <see cref="Spider.Parse"/>.</summary>
    protected virtual IReadOnlyList<SitemapRule> SitemapRules => [new SitemapRule("", Parse)];

    /// <summary>Patterns of nested sitemaps (in sitemap indexes) to follow. Defaults to all.</summary>
    public virtual IReadOnlyList<string> SitemapFollow => [""];

    /// <summary>Also crawl <c>hreflang</c> alternate URLs.</summary>
    public virtual bool SitemapAlternateLinks => false;

    /// <summary>Filter hook, e.g. only entries modified after a date.</summary>
    public virtual IEnumerable<SitemapEntry> SitemapFilter(IEnumerable<SitemapEntry> entries) => entries;

    public override async IAsyncEnumerable<Request> StartAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var url in SitemapUrls)
            yield return new Request(url, ParseSitemap);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>Callback for sitemap and robots.txt responses.</summary>
    protected IEnumerable<object> ParseSitemap(Response response)
    {
        _rules ??= SitemapRules;
        _follow ??= [.. SitemapFollow.Select(p => RegexExtractor.GetRegex(p))];

        if (response.Uri.AbsolutePath.EndsWith("/robots.txt", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var url in Sitemap.UrlsFromRobots(response.Text, response.Url))
                yield return new Request(url, ParseSitemap);
            yield break;
        }

        byte[] body;
        try
        {
            body = Sitemap.GetBody(response);
        }
        catch (InvalidDataException ex)
        {
            Logger.LogWarningMessage($"Ignoring invalid sitemap: {ex.Message}");
            yield break;
        }

        var sitemap = Sitemap.Parse(body);
        if (sitemap.Entries.Count == 0)
        {
            Logger.LogWarningMessage($"Ignoring invalid sitemap: {response}");
            yield break;
        }

        if (sitemap.IsIndex)
        {
            foreach (var entry in SitemapFilter(sitemap.Entries))
                if (_follow.Any(r => r.IsMatch(entry.Loc)))
                    yield return new Request(entry.Loc, ParseSitemap);
            yield break;
        }

        foreach (var entry in SitemapFilter(sitemap.Entries))
        {
            IEnumerable<string> urls = SitemapAlternateLinks ? [entry.Loc, .. entry.Alternates] : [entry.Loc];
            foreach (var url in urls)
            {
                var rule = _rules.FirstOrDefault(r => r.Pattern.IsMatch(url));
                if (rule is null) continue;
                yield return new Request(url, rule.Callback) { Meta = { ["sitemap_entry"] = entry.LastMod?.ToString("O") } };
            }
        }
    }
}

internal static class LoggerMessageExtensions
{
    public static void LogWarningMessage(this Microsoft.Extensions.Logging.ILogger logger, string message) =>
        Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(logger, "{Message}", message);
}
