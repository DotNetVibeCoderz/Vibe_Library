using System.Collections;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace ScrapyNet.Loaders;

/// <summary>What processors can see besides the values: the loader's selector, response and extra data.</summary>
public sealed class LoaderContext
{
    public Response? Response { get; init; }

    public SelectorList? Selector { get; init; }

    public object? Item { get; init; }

    /// <summary>Arbitrary values processors may read (Scrapy's loader context kwargs).</summary>
    public Dictionary<string, object?> Values { get; } = new(StringComparer.Ordinal);
}

/// <summary>
/// An input or output processor, as in Scrapy's <c>itemloaders.processors</c>. Input processors get the
/// list of freshly extracted values and return a list; output processors get every collected value for
/// a field and return the final field value.
/// </summary>
public abstract class Processor
{
    public abstract object? Process(object? values, LoaderContext context);

    /// <summary>Scrapy's <c>arg_to_iter</c>: null → empty, list → itself, scalar → one-element list.</summary>
    public static List<object?> ToList(object? value) => value switch
    {
        null => [],
        string s => [s],
        List<object?> list => list,
        IEnumerable e => e.Cast<object?>().ToList(),
        _ => [value],
    };

    /// <summary>Wraps a function applied to the whole value (Scrapy lets a bare callable be a processor).</summary>
    public static Processor From(Func<object?, object?> function) => new FunctionProcessor(function);

    private sealed class FunctionProcessor(Func<object?, object?> function) : Processor
    {
        public override object? Process(object? values, LoaderContext context) => function(values);
    }
}

/// <summary>Returns the values unchanged.</summary>
public sealed class Identity : Processor
{
    public static readonly Identity Instance = new();

    public override object? Process(object? values, LoaderContext context) => values;
}

/// <summary>The first value that is neither null nor an empty string.</summary>
public sealed class TakeFirst : Processor
{
    public static readonly TakeFirst Instance = new();

    public override object? Process(object? values, LoaderContext context)
    {
        foreach (var v in ToList(values))
            if (v is not null && v is not "") return v;
        return null;
    }
}

/// <summary>Joins the values with a separator (default a space).</summary>
public sealed class Join(string separator = " ") : Processor
{
    public override object? Process(object? values, LoaderContext context) =>
        string.Join(separator, ToList(values).Where(v => v is not null).Select(v => Convert.ToString(v, CultureInfo.InvariantCulture)));
}

/// <summary>
/// Applies each function to every value in turn, flattening lists and dropping nulls — the workhorse
/// input processor. <c>new MapCompose(Transforms.Strip, Transforms.Price)</c>.
/// </summary>
public sealed class MapCompose : Processor
{
    private readonly Func<object?, LoaderContext, object?>[] _functions;

    public MapCompose(params Func<object?, object?>[] functions) =>
        _functions = [.. functions.Select(f => (Func<object?, LoaderContext, object?>)((v, _) => f(v)))];

    /// <summary>Functions that also receive the loader context (e.g. to resolve relative URLs).</summary>
    public MapCompose(params Func<object?, LoaderContext, object?>[] functions) => _functions = functions;

    public override object? Process(object? values, LoaderContext context)
    {
        var current = ToList(values);
        foreach (var function in _functions)
        {
            var next = new List<object?>(current.Count);
            foreach (var v in current)
            {
                var result = function(v, context);
                if (result is null) continue;
                if (result is IEnumerable e and not string) next.AddRange(e.Cast<object?>().Where(x => x is not null));
                else next.Add(result);
            }
            current = next;
        }
        return current;
    }
}

/// <summary>Chains functions over the whole value; stops at the first null unless told otherwise.</summary>
public sealed class Compose(params Func<object?, object?>[] functions) : Processor
{
    public bool StopOnNone { get; init; } = true;

    public override object? Process(object? values, LoaderContext context)
    {
        var current = values;
        foreach (var f in functions)
        {
            if (current is null && StopOnNone) break;
            current = f(current);
        }
        return current;
    }
}

/// <summary>Resolves relative URLs against the loader's response.</summary>
public sealed class AbsoluteUrl : Processor
{
    public static readonly AbsoluteUrl Instance = new();

    public override object? Process(object? values, LoaderContext context)
    {
        var list = ToList(values);
        if (context.Response is null) return list;
        return list.Where(v => v is not null).Select(v => (object?)context.Response.UrlJoin(v!.ToString()!)).ToList();
    }
}

/// <summary>
/// Ready-made value transforms for <see cref="MapCompose"/> and <see cref="Compose"/>. Each takes one
/// value and returns the transformed value, or <c>null</c> to drop it.
/// </summary>
public static class Transforms
{
    /// <summary>Adapts a string function; non-string values are converted with ToString first.</summary>
    public static Func<object?, object?> Str(Func<string, object?> function) =>
        v => v switch
        {
            null => null,
            string s => function(s),
            _ => function(Convert.ToString(v, CultureInfo.InvariantCulture) ?? ""),
        };

    /// <summary>Trims whitespace; empty results are dropped.</summary>
    public static readonly Func<object?, object?> Strip = Str(s => s.Trim() is { Length: > 0 } t ? t : null);

    /// <summary>Collapses whitespace runs and trims; empty results are dropped.</summary>
    public static readonly Func<object?, object?> Clean = Str(s => TextUtils.NormalizeWhitespace(s) is { Length: > 0 } t ? t : null);

    /// <summary>Removes HTML tags and decodes entities.</summary>
    public static readonly Func<object?, object?> RemoveTags = Str(TextUtils.RemoveTags);

    /// <summary>Decodes HTML entities.</summary>
    public static readonly Func<object?, object?> DecodeEntities = Str(WebUtility.HtmlDecode);

    public static readonly Func<object?, object?> Lower = Str(s => s.ToLowerInvariant());

    public static readonly Func<object?, object?> Upper = Str(s => s.ToUpperInvariant());

    /// <summary>Parses an integer, ignoring thousands separators and surrounding text ("1,234 reviews" → 1234).</summary>
    public static readonly Func<object?, object?> ToInt = Str(s =>
    {
        var m = RegexExtractor.GetRegex(@"-?\d[\d,\.]*").Match(s);
        if (!m.Success) return null;
        var digits = m.Value.Replace(",", "").Replace(".", "");
        return long.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? (object)(n is >= int.MinValue and <= int.MaxValue ? (int)n : n) : null;
    });

    /// <summary>Parses a decimal number with '.' as decimal separator ("12.50", "1,299.00").</summary>
    public static readonly Func<object?, object?> ToDecimal = Str(s =>
    {
        var m = RegexExtractor.GetRegex(@"-?\d[\d,]*(\.\d+)?").Match(s);
        return m.Success && decimal.TryParse(m.Value.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
    });

    public static readonly Func<object?, object?> ToDouble = v => ToDecimal(v) is decimal d ? (double)d : null;

    /// <summary>
    /// Parses prices in either convention — "£51.77", "$1,299.00", "Rp 1.250.000", "12,50 €" — into a
    /// decimal. A separator followed by exactly two digits at the end is the decimal separator; any
    /// other separators are grouping.
    /// </summary>
    public static readonly Func<object?, object?> Price = Str(s => ParsePrice(s));

    /// <summary>Extracts regex matches (Scrapy's <c>re=</c> argument); returns a list.</summary>
    public static Func<object?, object?> Regex(string pattern) =>
        Str(s => RegexExtractor.Extract(pattern, s).ToList<object?>());

    /// <summary>Replaces text (plain, not regex).</summary>
    public static Func<object?, object?> Replace(string oldValue, string newValue) => Str(s => s.Replace(oldValue, newValue, StringComparison.Ordinal));

    /// <summary>Returns the value only if it matches the regex (filters the rest out).</summary>
    public static Func<object?, object?> Where(string pattern) => Str(s => RegexExtractor.GetRegex(pattern).IsMatch(s) ? s : null);

    /// <summary>Resolves a relative URL against the loader's response (context-aware; use with <see cref="MapCompose"/>).</summary>
    public static readonly Func<object?, LoaderContext, object?> UrlJoin =
        (v, ctx) => v is null ? null : ctx.Response?.UrlJoin(v.ToString()!) ?? v;

    /// <summary>Parses a date/time in invariant culture, returning <see cref="DateTimeOffset"/>.</summary>
    public static readonly Func<object?, object?> ToDateTime = Str(s =>
        DateTimeOffset.TryParse(s.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : null);

    internal static decimal? ParsePrice(string s)
    {
        var m = RegexExtractor.GetRegex(@"\d[\d.,\s]*\d|\d").Match(s);
        if (!m.Success) return null;
        var raw = m.Value.Replace(" ", "").Replace(" ", "");
        var lastSep = raw.LastIndexOfAny(['.', ',']);
        string normalized;
        if (lastSep >= 0 && raw.Length - lastSep - 1 == 2)
            normalized = raw[..lastSep].Replace(".", "").Replace(",", "") + "." + raw[(lastSep + 1)..];
        else if (lastSep >= 0 && raw.Length - lastSep - 1 != 3)
            normalized = raw[..lastSep].Replace(".", "").Replace(",", "") + "." + raw[(lastSep + 1)..];
        else
            normalized = raw.Replace(".", "").Replace(",", "");
        var negative = s.TrimStart().StartsWith('-');
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? (negative ? -d : d) : null;
    }
}
