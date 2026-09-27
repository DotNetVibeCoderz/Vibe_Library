using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;

namespace ScrapyNet;

/// <summary>
/// Scrapy's regex extraction rules (<c>parsel.utils.extract_regex</c>): a group named <c>extract</c>
/// wins; otherwise every capture group of every match; otherwise every whole match.
/// </summary>
public static class RegexExtractor
{
    private static readonly ConcurrentDictionary<string, Regex> Cache = new(StringComparer.Ordinal);

    public static Regex GetRegex(string pattern, RegexOptions options = RegexOptions.None)
    {
        var key = options == RegexOptions.None ? pattern : $"{(int)options}:{pattern}";
        if (Cache.TryGetValue(key, out var regex)) return regex;
        regex = new Regex(pattern, options | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));
        if (Cache.Count < 2048) Cache[key] = regex;
        return regex;
    }

    public static IReadOnlyList<string> Extract(string pattern, string text, bool replaceEntities = true) =>
        Extract(GetRegex(pattern), text, replaceEntities);

    public static IReadOnlyList<string> Extract(Regex regex, string text, bool replaceEntities = true)
    {
        var results = new List<string>();
        var extractGroup = regex.GroupNumberFromName("extract");
        var groupCount = regex.GetGroupNumbers().Length - 1;

        foreach (Match match in regex.Matches(text))
        {
            if (extractGroup >= 0)
            {
                if (match.Groups[extractGroup].Success) results.Add(match.Groups[extractGroup].Value);
            }
            else if (groupCount > 0)
            {
                for (var g = 1; g <= groupCount; g++)
                    if (match.Groups[g].Success) results.Add(match.Groups[g].Value);
            }
            else
            {
                results.Add(match.Value);
            }
        }

        if (replaceEntities)
            for (var i = 0; i < results.Count; i++)
                if (results[i].Contains('&')) results[i] = WebUtility.HtmlDecode(results[i]);
        return results;
    }
}
