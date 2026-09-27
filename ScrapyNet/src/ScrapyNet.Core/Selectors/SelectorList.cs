using System.Collections;

namespace ScrapyNet;

/// <summary>
/// The result of a CSS or XPath query: a list of <see cref="Selector"/>s that can be queried again,
/// like parsel's <c>SelectorList</c>.
/// </summary>
public sealed class SelectorList : IReadOnlyList<Selector>
{
    public static readonly SelectorList Empty = new([]);

    private readonly List<Selector> _items;

    public SelectorList(IEnumerable<Selector> selectors) => _items = selectors as List<Selector> ?? [.. selectors];

    public int Count => _items.Count;

    public Selector this[int index] => _items[index];

    /// <summary>Runs the CSS query on every selector and flattens the results.</summary>
    public SelectorList Css(string query) => _items.Count == 1 ? _items[0].Css(query) : new SelectorList(_items.SelectMany(s => s.Css(query)));

    /// <summary>Runs the XPath query on every selector and flattens the results.</summary>
    public SelectorList Xpath(string query) => _items.Count == 1 ? _items[0].Xpath(query) : new SelectorList(_items.SelectMany(s => s.Xpath(query)));

    public SelectorList Xpath(string query, IReadOnlyDictionary<string, object>? variables) =>
        new(_items.SelectMany(s => s.Xpath(query, variables)));

    /// <summary>First result's <see cref="Selector.Get"/>, or <paramref name="defaultValue"/>. Scrapy's <c>.get()</c>.</summary>
    public string? Get(string? defaultValue = null) => _items.Count > 0 ? _items[0].Get() : defaultValue;

    /// <summary>Every result's <see cref="Selector.Get"/>. Scrapy's <c>.getall()</c>.</summary>
    public List<string> GetAll()
    {
        var list = new List<string>(_items.Count);
        foreach (var s in _items) list.Add(s.Get());
        return list;
    }

    /// <summary>Old-style alias of <see cref="GetAll"/>.</summary>
    public List<string> Extract() => GetAll();

    /// <summary>Old-style alias of <see cref="Get"/>.</summary>
    public string? ExtractFirst(string? defaultValue = null) => Get(defaultValue);

    /// <summary>Whitespace-normalized text of each result, empty strings dropped.</summary>
    public List<string> GetTexts()
    {
        var list = new List<string>(_items.Count);
        foreach (var s in _items)
        {
            var t = s.GetText();
            if (t.Length > 0) list.Add(t);
        }
        return list;
    }

    /// <summary>All results' text joined with a space and whitespace-normalized.</summary>
    public string GetText(string separator = " ") => TextUtils.NormalizeWhitespace(string.Join(separator, _items.Select(s => s.GetText(false))));

    /// <summary>Attributes of the first element (parsel's <c>.attrib</c>).</summary>
    public IReadOnlyDictionary<string, string> Attrib => _items.Count > 0 ? _items[0].Attrib : new Dictionary<string, string>();

    public List<string> Re(string pattern, bool replaceEntities = true)
    {
        var list = new List<string>();
        foreach (var s in _items) list.AddRange(s.Re(pattern, replaceEntities));
        return list;
    }

    public string? ReFirst(string pattern, string? defaultValue = null, bool replaceEntities = true)
    {
        foreach (var s in _items)
        {
            var m = s.Re(pattern, replaceEntities);
            if (m.Count > 0) return m[0];
        }
        return defaultValue;
    }

    /// <summary>Removes every matched node from its document.</summary>
    public void Drop()
    {
        foreach (var s in _items) s.Drop();
    }

    public IEnumerator<Selector> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => $"[{string.Join(", ", _items.Take(5))}{(_items.Count > 5 ? ", ..." : "")}]";
}
