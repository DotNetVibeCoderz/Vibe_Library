using System.Collections;
using System.Diagnostics.CodeAnalysis;
using ScrapyNet.Loaders;

namespace ScrapyNet;

/// <summary>
/// Declared field of an <see cref="Item"/>, with optional metadata used by loaders and exporters
/// (Scrapy's <c>scrapy.Field()</c>).
/// </summary>
public sealed record Field(string Name)
{
    /// <summary>Converts the value when exporting (e.g. formats a price).</summary>
    public Func<object?, object?>? Serializer { get; init; }

    /// <summary>Default input processor for <see cref="ItemLoader"/>s filling this field.</summary>
    public Processor? Input { get; init; }

    /// <summary>Default output processor for <see cref="ItemLoader"/>s filling this field.</summary>
    public Processor? Output { get; init; }

    public static implicit operator Field(string name) => new(name);
}

/// <summary>
/// A dictionary-backed item with a declared set of fields, like Scrapy's <c>scrapy.Item</c>: assigning
/// an undeclared field throws, which catches typos in field names early.
/// </summary>
/// <remarks>
/// Plain C# classes and records work as items too and are usually the better choice in C#; the item
/// adapter reads their public properties. <see cref="Item"/> is for when the field set is dynamic or
/// when per-field metadata (serializers, loader processors) should live with the item.
/// </remarks>
/// <example>
/// <code>
/// public sealed class ProductItem() : Item("name", "price", new Field("url") { Serializer = v => v?.ToString()?.ToLowerInvariant() });
/// var item = new ProductItem { ["name"] = "Book", ["price"] = 12.5m };
/// </code>
/// </example>
public class Item : IDictionary<string, object?>
{
    private Dictionary<string, object?> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Field>? _fields;

    /// <summary>A free-form item that accepts any field.</summary>
    public Item() { }

    /// <summary>An item restricted to the declared fields.</summary>
    public Item(params Field[] fields)
    {
        if (fields.Length == 0) return;
        _fields = new Dictionary<string, Field>(fields.Length, StringComparer.Ordinal);
        foreach (var f in fields) _fields[f.Name] = f;
    }

    /// <summary>Declared fields, or an empty dictionary for free-form items.</summary>
    public IReadOnlyDictionary<string, Field> Fields => (IReadOnlyDictionary<string, Field>?)_fields ?? EmptyFields;

    public bool IsDeclared(string name) => _fields is null || _fields.ContainsKey(name);

    public object? this[string key]
    {
        get => _values.TryGetValue(key, out var v) ? v : throw new KeyNotFoundException($"Field '{key}' is not set on {GetType().Name}.");
        set
        {
            if (!IsDeclared(key)) throw new KeyNotFoundException($"{GetType().Name} does not support field: {key}");
            _values[key] = value;
        }
    }

    public T? Get<T>(string key, T? defaultValue = default) =>
        _values.TryGetValue(key, out var v) && v is T t ? t : defaultValue;

    public ICollection<string> Keys => _values.Keys;

    public ICollection<object?> Values => _values.Values;

    public int Count => _values.Count;

    public bool IsReadOnly => false;

    public void Add(string key, object? value) => this[key] = value;

    public void Add(KeyValuePair<string, object?> item) => this[item.Key] = item.Value;

    public void Clear() => _values.Clear();

    public bool Contains(KeyValuePair<string, object?> item) => ((ICollection<KeyValuePair<string, object?>>)_values).Contains(item);

    public bool ContainsKey(string key) => _values.ContainsKey(key);

    public void CopyTo(KeyValuePair<string, object?>[] array, int arrayIndex) => ((ICollection<KeyValuePair<string, object?>>)_values).CopyTo(array, arrayIndex);

    public bool Remove(string key) => _values.Remove(key);

    public bool Remove(KeyValuePair<string, object?> item) => ((ICollection<KeyValuePair<string, object?>>)_values).Remove(item);

    public bool TryGetValue(string key, [MaybeNullWhen(false)] out object? value) => _values.TryGetValue(key, out value);

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => _values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Shallow copy with the same declared fields.</summary>
    public Item Copy()
    {
        var copy = (Item)MemberwiseClone();
        copy._values = new Dictionary<string, object?>(_values, StringComparer.Ordinal);
        return copy;
    }

    public override string ToString() =>
        "{" + string.Join(", ", _values.Select(kv => $"'{kv.Key}': {ItemAdapter.FormatValue(kv.Value)}")) + "}";

    private static readonly IReadOnlyDictionary<string, Field> EmptyFields = new Dictionary<string, Field>();
}
