using System.Collections.Concurrent;

namespace ScrapyNet;

internal enum CssPseudo
{
    None,
    Text,
    Attr,
}

/// <summary>One comma-separated part of a CSS query with its Scrapy pseudo-element split off.</summary>
internal sealed record CssQueryPart(string Base, CssPseudo Pseudo, string? AttrName, bool Descendant);

/// <summary>
/// Splits Scrapy-flavoured CSS (<c>a::attr(href), h1::text</c>) into standard CSS that AngleSharp
/// understands plus a pseudo-element to apply to its matches. Parsed queries are cached; spiders run
/// the same handful of queries on every page.
/// </summary>
internal static class CssQuery
{
    private static readonly ConcurrentDictionary<string, IReadOnlyList<CssQueryPart>> Cache = new(StringComparer.Ordinal);

    public static IReadOnlyList<CssQueryPart> Parse(string query)
    {
        if (Cache.TryGetValue(query, out var cached)) return cached;
        var parsed = SplitTopLevel(query).Select(ParsePart).ToArray();
        if (Cache.Count < 4096) Cache[query] = parsed;
        return parsed;
    }

    private static CssQueryPart ParsePart(string part)
    {
        var idx = LastTopLevelIndexOf(part, "::");
        if (idx < 0) return new CssQueryPart(part.Trim(), CssPseudo.None, null, false);

        var pseudo = part[(idx + 2)..].Trim();
        var before = part[..idx];
        var descendant = before.Length > 0 && char.IsWhiteSpace(before[^1]);
        var baseSelector = before.Trim();
        if (baseSelector == "*") baseSelector = "";

        if (pseudo.Equals("text", StringComparison.OrdinalIgnoreCase))
            return new CssQueryPart(baseSelector, CssPseudo.Text, null, descendant);

        if (pseudo.StartsWith("attr(", StringComparison.OrdinalIgnoreCase) && pseudo.EndsWith(')'))
        {
            var name = pseudo[5..^1].Trim().Trim('"', '\'');
            return new CssQueryPart(baseSelector, CssPseudo.Attr, name, descendant);
        }

        throw new ArgumentException($"Unsupported pseudo-element '::{pseudo}' in '{part}'. Use ::text or ::attr(name).");
    }

    private static IEnumerable<string> SplitTopLevel(string query)
    {
        var depth = 0;
        char quote = '\0';
        var start = 0;
        for (var i = 0; i < query.Length; i++)
        {
            var c = query[i];
            if (quote != '\0')
            {
                if (c == '\\') i++;
                else if (c == quote) quote = '\0';
                continue;
            }
            switch (c)
            {
                case '"' or '\'': quote = c; break;
                case '(' or '[': depth++; break;
                case ')' or ']': depth--; break;
                case ',' when depth == 0:
                    yield return query[start..i];
                    start = i + 1;
                    break;
            }
        }
        yield return query[start..];
    }

    private static int LastTopLevelIndexOf(string s, string token)
    {
        var depth = 0;
        char quote = '\0';
        var found = -1;
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (quote != '\0')
            {
                if (c == '\\') i++;
                else if (c == quote) quote = '\0';
                continue;
            }
            if (c is '"' or '\'') quote = c;
            else if (c is '(' or '[') depth++;
            else if (c is ')' or ']') depth--;
            else if (depth == 0 && string.CompareOrdinal(s, i, token, 0, token.Length) == 0) found = i;
        }
        return found;
    }
}
