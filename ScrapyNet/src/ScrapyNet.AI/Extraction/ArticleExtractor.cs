using System.Globalization;
using System.Text;
using System.Text.Json;
using AngleSharp.Dom;

namespace ScrapyNet.AI;

/// <summary>Page metadata gathered from standard tags: title, description, OpenGraph, Twitter cards, JSON-LD.</summary>
public sealed record PageMetadata
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? Author { get; init; }
    public DateTimeOffset? Published { get; init; }
    public string? Language { get; init; }
    public string? CanonicalUrl { get; init; }
    public string? SiteName { get; init; }
    public string? Image { get; init; }
    public string? Type { get; init; }
    public IReadOnlyList<string> Keywords { get; init; } = [];

    /// <summary>Every <c>application/ld+json</c> block, parsed.</summary>
    public IReadOnlyList<JsonElement> JsonLd { get; init; } = [];

    /// <summary>All <c>meta</c> name/property → content pairs.</summary>
    public IReadOnlyDictionary<string, string> Meta { get; init; } = new Dictionary<string, string>();
}

/// <summary>The main content of a page with boilerplate (navigation, sidebars, footers, ads) removed.</summary>
public sealed record ExtractedArticle
{
    public required string Url { get; init; }
    public required string Title { get; init; }
    public required string Text { get; init; }
    public required PageMetadata Metadata { get; init; }
    public IReadOnlyList<string> Paragraphs { get; init; } = [];
    public IReadOnlyList<string> Headings { get; init; } = [];
    public IReadOnlyList<string> Images { get; init; } = [];
    public int WordCount { get; init; }

    /// <summary>0..1 — how confident the extractor is that it found real article content.</summary>
    public double Confidence { get; init; }
}

/// <summary>Reads page metadata from any HTML response.</summary>
public static class MetadataExtractor
{
    public static PageMetadata Extract(Response response) => Extract(response.Selector, response.Url);

    public static PageMetadata Extract(Selector root, string url)
    {
        var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in root.Css("meta"))
        {
            var key = m.Attrib.GetValueOrDefault("property") ?? m.Attrib.GetValueOrDefault("name") ?? m.Attrib.GetValueOrDefault("itemprop");
            var content = m.Attrib.GetValueOrDefault("content");
            if (key is not null && content is not null) meta.TryAdd(key, content.Trim());
        }

        var jsonLd = new List<JsonElement>();
        foreach (var script in root.Css("script[type='application/ld+json']::text").GetAll())
        {
            try
            {
                using var doc = JsonDocument.Parse(script);
                jsonLd.Add(doc.RootElement.Clone());
            }
            catch (JsonException)
            {
                // Broken JSON-LD is common; skip it.
            }
        }

        string? FromLd(string property) => jsonLd.Select(e => LdValue(e, property)).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

        var published = First(meta.GetValueOrDefault("article:published_time"), FromLd("datePublished"), meta.GetValueOrDefault("date"),
            root.Css("time::attr(datetime)").Get());
        var canonical = root.Css("link[rel=canonical]::attr(href)").Get();
        return new PageMetadata
        {
            Title = First(meta.GetValueOrDefault("og:title"), FromLd("headline"), meta.GetValueOrDefault("twitter:title"), root.Css("title::text").Get()?.Trim()),
            Description = First(meta.GetValueOrDefault("og:description"), meta.GetValueOrDefault("description"), FromLd("description")),
            Author = First(meta.GetValueOrDefault("author"), meta.GetValueOrDefault("article:author"), FromLd("author")),
            Published = DateTimeOffset.TryParse(published, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : null,
            Language = root.Css("html::attr(lang)").Get() ?? meta.GetValueOrDefault("og:locale"),
            CanonicalUrl = canonical is null ? null : UrlUtils.Join(url, canonical),
            SiteName = meta.GetValueOrDefault("og:site_name"),
            Image = meta.GetValueOrDefault("og:image") ?? meta.GetValueOrDefault("twitter:image"),
            Type = FromLd("@type") ?? meta.GetValueOrDefault("og:type"),
            Keywords = (meta.GetValueOrDefault("keywords") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            JsonLd = jsonLd,
            Meta = meta,
        };
    }

    private static string? First(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static string? LdValue(JsonElement e, string property)
    {
        if (e.ValueKind == JsonValueKind.Array)
            return e.EnumerateArray().Select(x => LdValue(x, property)).FirstOrDefault(v => v is not null);
        if (e.ValueKind != JsonValueKind.Object) return null;
        if (e.TryGetProperty("@graph", out var graph)) return LdValue(graph, property);
        if (!e.TryGetProperty(property, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Object when v.TryGetProperty("name", out var n) => n.GetString(),
            JsonValueKind.Array => v.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Object && x.TryGetProperty("name", out var nn) ? nn.GetString() : x.ToString()).FirstOrDefault(),
            _ => v.ToString(),
        };
    }
}

/// <summary>
/// Finds the main content of an article page, in the spirit of Mozilla's Readability: candidate blocks
/// are scored by paragraph count, text length, comma density and class/id hints, penalized by link
/// density, and the best block's paragraphs become the article text.
/// </summary>
public static class ArticleExtractor
{
    private static readonly string[] Negative = ["nav", "footer", "sidebar", "aside", "comment", "share", "social", "ad", "promo", "related", "menu", "header", "breadcrumb", "banner", "cookie", "newsletter", "trending"];
    private static readonly string[] Positive = ["article", "content", "story", "body", "post", "entry", "main", "text", "blog"];

    public static ExtractedArticle Extract(Response response) => Extract(response.Selector, response.Url);

    public static ExtractedArticle Extract(Selector root, string url)
    {
        var metadata = MetadataExtractor.Extract(root, url);
        // Work on a private copy: stripping scripts and boilerplate must not alter the response's own DOM.
        var working = Selector.FromHtml(root.Get(), url);
        if (working.Node is not IDocument document || document.Body is null)
            return new ExtractedArticle { Url = url, Title = metadata.Title ?? "", Text = "", Metadata = metadata };

        foreach (var junk in document.Body.QuerySelectorAll("script, style, noscript, template, svg, form, iframe").ToList()) junk.Remove();

        var scores = new Dictionary<IElement, double>(ReferenceEqualityComparer.Instance);
        foreach (var p in document.Body.QuerySelectorAll("p, pre, blockquote, li, td"))
        {
            var paragraphText = TextUtils.NormalizeWhitespace(p.TextContent);
            if (paragraphText.Length < 25) continue;
            var score = 1 + paragraphText.Count(c => c == ',') + Math.Min(paragraphText.Length / 100, 3);
            var parent = p.ParentElement;
            if (parent is null) continue;
            scores[parent] = scores.GetValueOrDefault(parent, InitialScore(parent)) + score;
            if (parent.ParentElement is { } grand)
                scores[grand] = scores.GetValueOrDefault(grand, InitialScore(grand)) + score / 2;
        }

        IElement? best = null;
        var bestScore = double.MinValue;
        foreach (var (el, raw) in scores)
        {
            var adjusted = raw * (1 - LinkDensity(el));
            if (adjusted > bestScore)
            {
                bestScore = adjusted;
                best = el;
            }
        }
        best ??= document.QuerySelector("article") ?? document.QuerySelector("main") ?? document.Body;

        var paragraphs = best.QuerySelectorAll("p, pre, blockquote, li, h2, h3, h4")
            .Where(e => !IsBoilerplate(e))
            .Select(e => TextUtils.NormalizeWhitespace(e.TextContent))
            .Where(t => t.Length > 0)
            .ToList();
        if (paragraphs.Count == 0) paragraphs = [TextUtils.NormalizeWhitespace(best.TextContent)];

        var headings = best.QuerySelectorAll("h1, h2, h3").Select(h => TextUtils.NormalizeWhitespace(h.TextContent)).Where(h => h.Length > 0).ToList();
        var images = best.QuerySelectorAll("img[src]").Select(i => UrlUtils.Join(url, i.GetAttribute("src")!)).Distinct().ToList();
        var title = metadata.Title ?? document.QuerySelector("h1")?.TextContent.Trim() ?? "";
        var text = string.Join("\n\n", paragraphs);
        var words = TextNormalizer.CountWords(text);
        var confidence = Math.Clamp(bestScore / 20.0, 0, 1) * Math.Clamp(words / 150.0, 0, 1);

        return new ExtractedArticle
        {
            Url = url,
            Title = title,
            Text = text,
            Metadata = metadata,
            Paragraphs = paragraphs,
            Headings = headings,
            Images = images,
            WordCount = words,
            Confidence = Math.Round(confidence, 3),
        };
    }

    private static double InitialScore(IElement el)
    {
        var score = el.LocalName switch
        {
            "article" => 10,
            "main" or "section" => 5,
            "div" => 3,
            "pre" or "td" or "blockquote" => 2,
            "ul" or "ol" or "form" or "aside" or "nav" or "footer" or "header" => -5,
            _ => 0,
        };
        var hint = ((el.ClassName ?? "") + " " + el.Id).ToLowerInvariant();
        if (Negative.Any(n => hint.Contains(n, StringComparison.Ordinal))) score -= 25;
        if (Positive.Any(p => hint.Contains(p, StringComparison.Ordinal))) score += 25;
        return score;
    }

    private static bool IsBoilerplate(IElement el)
    {
        for (var e = el; e is not null; e = e.ParentElement)
        {
            if (e.LocalName is "nav" or "aside" or "footer") return true;
            var hint = ((e.ClassName ?? "") + " " + e.Id).ToLowerInvariant();
            if (hint.Length > 1 && Negative.Any(n => hint.Split(' ', '-', '_').Contains(n))) return true;
        }
        return false;
    }

    private static double LinkDensity(IElement el)
    {
        var total = el.TextContent.Length;
        if (total == 0) return 1;
        var links = el.QuerySelectorAll("a").Sum(a => a.TextContent.Length);
        return (double)links / total;
    }
}

/// <summary>Text clean-up for AI ingestion.</summary>
public static class TextNormalizer
{
    /// <summary>Unicode NFKC, unified quotes and dashes, collapsed whitespace, blank-line-separated paragraphs kept.</summary>
    public static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var normalized = text.Normalize(NormalizationForm.FormKC)
            .Replace('‘', '\'').Replace('’', '\'')
            .Replace('“', '"').Replace('”', '"')
            .Replace('–', '-').Replace('—', '-')
            .Replace(" ", " ");
        var paragraphs = normalized.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(TextUtils.NormalizeWhitespace)
            .Where(p => p.Length > 0);
        return string.Join("\n\n", paragraphs);
    }

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "an", "and", "are", "as", "at", "be", "but", "by", "can", "do", "does", "for", "from", "has", "have", "how", "i", "if", "in", "into",
        "is", "it", "its", "of", "on", "or", "so", "than", "that", "the", "their", "then", "there", "these", "they", "this", "to", "was", "we",
        "what", "when", "where", "which", "while", "who", "why", "will", "with", "you", "your",
        "ada", "adalah", "akan", "apa", "atau", "bagaimana", "dalam", "dan", "dari", "dengan", "di", "ini", "itu", "juga", "ke", "untuk", "yang",
    };

    /// <summary>
    /// A light English suffix stemmer for matching word forms: <c>bans/banned → ban</c>, <c>proxies → proxy</c>,
    /// <c>crawlers/crawling → crawl</c>. Deliberately conservative; it never shortens a word below three letters.
    /// </summary>
    public static string Stem(string token)
    {
        if (token.Length <= 3) return token;
        string[] suffixes = ["ations", "ation", "ingly", "ings", "ing", "ers", "ied", "ies", "ed", "es", "er", "ly", "s"];
        foreach (var suffix in suffixes)
        {
            if (!token.EndsWith(suffix, StringComparison.Ordinal) || token.Length - suffix.Length < 3) continue;
            var stem = token[..^suffix.Length];
            if (suffix is "ies" or "ied") return stem + "y";
            if (suffix == "s" && (stem.EndsWith('s') || stem.EndsWith('u'))) return token;
            // banned → bann → ban
            if (stem.Length > 3 && stem[^1] == stem[^2] && !"aeioulsz".Contains(stem[^1])) stem = stem[..^1];
            return stem;
        }
        return token;
    }

    /// <summary>True for common English and Indonesian function words.</summary>
    public static bool IsStopWord(string token) => StopWords.Contains(token);

    public static int CountWords(string text)
    {
        var count = 0;
        var inWord = false;
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c))
            {
                if (!inWord) count++;
                inWord = true;
            }
            else inWord = false;
        }
        return count;
    }

    /// <summary>Lower-cased word tokens (letters/digits), used by hashing, SimHash and classifiers.</summary>
    public static IEnumerable<string> Tokenize(string text)
    {
        var sb = new StringBuilder();
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            else if (sb.Length > 0)
            {
                yield return sb.ToString();
                sb.Clear();
            }
        }
        if (sb.Length > 0) yield return sb.ToString();
    }

    /// <summary>Splits into sentences on ., ! and ? followed by whitespace and an upper-case letter or digit.</summary>
    public static List<string> SplitSentences(string text)
    {
        var sentences = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('.' or '!' or '?' or '\n')) continue;
            var j = i + 1;
            while (j < text.Length && text[j] is '"' or '\'' or ')') j++;
            if (j < text.Length && !char.IsWhiteSpace(text[j])) continue;
            var k = j;
            while (k < text.Length && char.IsWhiteSpace(text[k])) k++;
            if (text[i] != '\n' && k < text.Length && !(char.IsUpper(text[k]) || char.IsDigit(text[k]) || text[k] is '"' or '\'')) continue;
            var s = text[start..j].Trim();
            if (s.Length > 0) sentences.Add(s);
            start = j;
        }
        var tail = text[start..].Trim();
        if (tail.Length > 0) sentences.Add(tail);
        return sentences;
    }
}
