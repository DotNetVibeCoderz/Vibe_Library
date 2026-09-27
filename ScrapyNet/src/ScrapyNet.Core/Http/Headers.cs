using System.Collections;

namespace ScrapyNet;

/// <summary>
/// Case-insensitive, multi-valued HTTP header collection used by <see cref="Request"/> and
/// <see cref="Response"/>. The indexer reads and writes the first value, which is what almost every
/// caller wants; <see cref="GetValues"/> exposes the rest (e.g. several <c>Set-Cookie</c> lines).
/// </summary>
public sealed class Headers : IEnumerable<KeyValuePair<string, IReadOnlyList<string>>>
{
    private readonly Dictionary<string, List<string>> _values;

    public Headers() => _values = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

    public Headers(IEnumerable<KeyValuePair<string, string>> values) : this()
    {
        foreach (var (key, value) in values) Add(key, value);
    }

    public int Count => _values.Count;

    /// <summary>First value of the header, or <c>null</c>. Setting replaces every existing value.</summary>
    public string? this[string name]
    {
        get => Get(name);
        set
        {
            if (value is null) Remove(name);
            else Set(name, value);
        }
    }

    public string? Get(string name) => _values.TryGetValue(name, out var list) && list.Count > 0 ? list[0] : null;

    public IReadOnlyList<string> GetValues(string name) =>
        _values.TryGetValue(name, out var list) ? list : Array.Empty<string>();

    public bool Contains(string name) => _values.ContainsKey(name);

    public void Set(string name, string value) => _values[name] = [value];

    public void Set(string name, IEnumerable<string> values) => _values[name] = [.. values];

    /// <summary>Sets the header only if it is not present yet (Scrapy's <c>setdefault</c>).</summary>
    public bool SetDefault(string name, string value)
    {
        if (_values.ContainsKey(name)) return false;
        _values[name] = [value];
        return true;
    }

    public void Add(string name, string value)
    {
        if (!_values.TryGetValue(name, out var list)) _values[name] = list = new List<string>(1);
        list.Add(value);
    }

    public bool Remove(string name) => _values.Remove(name);

    public void Clear() => _values.Clear();

    public Headers Copy()
    {
        var copy = new Headers();
        foreach (var (key, list) in _values) copy._values[key] = [.. list];
        return copy;
    }

    public IEnumerable<string> Names => _values.Keys;

    public IEnumerator<KeyValuePair<string, IReadOnlyList<string>>> GetEnumerator()
    {
        foreach (var (key, list) in _values) yield return new KeyValuePair<string, IReadOnlyList<string>>(key, list);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Flattens to one entry per header name, joining repeated values with a comma.</summary>
    public Dictionary<string, string> ToDictionary()
    {
        var result = new Dictionary<string, string>(_values.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, list) in _values) result[key] = string.Join(", ", list);
        return result;
    }

    public override string ToString() => string.Join("\n", _values.SelectMany(kv => kv.Value.Select(v => $"{kv.Key}: {v}")));
}
