using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json.Serialization;

namespace ScrapyNet;

/// <summary>
/// Uniform field access over every kind of item: <see cref="Item"/>, dictionaries, and plain C#
/// classes/records (public properties). Port of the <c>itemadapter</c> package Scrapy uses.
/// </summary>
/// <remarks>
/// Property access on classes goes through delegates compiled once per type and cached, so exporting
/// a million items costs a million delegate calls rather than a million reflection lookups.
/// Field names are matched loosely when setting — <c>file_urls</c>, <c>FileUrls</c> and <c>fileUrls</c>
/// all find the same property — so pipelines written against Scrapy's snake_case names work on
/// idiomatic C# classes.
/// </remarks>
public abstract class ItemAdapter
{
    /// <summary>Wraps any item.</summary>
    public static ItemAdapter For(object item) => item switch
    {
        null => throw new ArgumentNullException(nameof(item)),
        Item i => new ItemItemAdapter(i),
        IDictionary<string, object?> d => new DictionaryAdapter(d),
        IDictionary d => new LegacyDictionaryAdapter(d),
        _ => new PocoAdapter(item, TypeAccessor.Get(item.GetType())),
    };

    /// <summary>True for objects the engine treats as items (anything but requests, strings and primitives).</summary>
    public static bool IsItem(object? value) =>
        value is not null and not Request and not string and not ValueType;

    public abstract object Item { get; }

    /// <summary>Field names in declaration order: declared fields for <see cref="ScrapyNet.Item"/>s and classes, keys for dictionaries.</summary>
    public abstract IReadOnlyList<string> FieldNames { get; }

    public abstract bool TryGet(string field, out object? value);

    public abstract void Set(string field, object? value);

    /// <summary>True when the item can hold the field (declared, or a free-form dictionary).</summary>
    public abstract bool HasField(string field);

    /// <summary>Declared metadata for a field, when the item type provides any.</summary>
    public virtual Field? GetFieldMeta(string field) => null;

    /// <summary>CLR type of the field for classes; <c>null</c> for dictionaries.</summary>
    public virtual Type? GetFieldType(string field) => null;

    public object? Get(string field) => TryGet(field, out var v) ? v : null;

    public T? Get<T>(string field)
    {
        var value = Get(field);
        return value is T t ? t : value is null ? default : (T?)ConvertTo(value, typeof(T));
    }

    /// <summary>Field values as an ordered dictionary (set fields only for dictionary-like items).</summary>
    public Dictionary<string, object?> AsDictionary()
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var name in FieldNames)
            if (TryGet(name, out var value)) result[name] = value;
        return result;
    }

    /// <summary>Resolves a loosely-spelled field name to the item's own spelling, or <c>null</c>.</summary>
    public virtual string? ResolveFieldName(string field)
    {
        if (HasField(field)) return field;
        var key = NormalizeName(field);
        foreach (var name in FieldNames)
            if (NormalizeName(name) == key) return name;
        return null;
    }

    internal static string NormalizeName(string name)
    {
        Span<char> buffer = stackalloc char[name.Length];
        var n = 0;
        foreach (var c in name)
            if (c != '_' && c != '-') buffer[n++] = char.ToLowerInvariant(c);
        return new string(buffer[..n]);
    }

    /// <summary>Best-effort conversion used when loaders and pipelines assign into typed properties.</summary>
    public static object? ConvertTo(object? value, Type target)
    {
        if (value is null) return target.IsValueType && Nullable.GetUnderlyingType(target) is null ? Activator.CreateInstance(target) : null;
        var type = Nullable.GetUnderlyingType(target) ?? target;
        if (type.IsInstanceOfType(value)) return value;
        if (type == typeof(object)) return value;

        if (type == typeof(string))
            return value is IEnumerable e and not string ? string.Join(" ", e.Cast<object?>().Select(x => Convert.ToString(x, CultureInfo.InvariantCulture))) : Convert.ToString(value, CultureInfo.InvariantCulture);

        // Collections: List<T>, T[], IList<T>, IEnumerable<T>, ...
        var elementType = GetElementType(type);
        if (elementType is not null)
        {
            var source = value is IEnumerable enumerable and not string ? enumerable.Cast<object?>() : [value];
            var converted = source.Select(v => ConvertTo(v, elementType)).ToList();
            if (type.IsArray)
            {
                var array = Array.CreateInstance(elementType, converted.Count);
                for (var i = 0; i < converted.Count; i++) array.SetValue(converted[i], i);
                return array;
            }
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType))!;
            foreach (var v in converted) list.Add(v);
            if (type.IsInstanceOfType(list)) return list;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(HashSet<>))
                return Activator.CreateInstance(type, list);
            return list;
        }

        // A list assigned to a scalar: take its first non-null element.
        if (value is IEnumerable seq and not string)
        {
            var first = seq.Cast<object?>().FirstOrDefault(x => x is not null);
            return first is null ? ConvertTo(null, target) : ConvertTo(first, target);
        }

        var text = value as string;
        try
        {
            if (type.IsEnum) return text is not null ? Enum.Parse(type, text, ignoreCase: true) : Enum.ToObject(type, value);
            if (type == typeof(Guid)) return Guid.Parse(text ?? value.ToString()!);
            if (type == typeof(Uri)) return new Uri(text ?? value.ToString()!, UriKind.RelativeOrAbsolute);
            if (type == typeof(DateTimeOffset)) return text is not null ? DateTimeOffset.Parse(text, CultureInfo.InvariantCulture) : new DateTimeOffset(Convert.ToDateTime(value, CultureInfo.InvariantCulture));
            if (type == typeof(DateOnly)) return DateOnly.Parse(text ?? value.ToString()!, CultureInfo.InvariantCulture);
            if (type == typeof(TimeSpan)) return TimeSpan.Parse(text ?? value.ToString()!, CultureInfo.InvariantCulture);
            if (type == typeof(bool) && text is not null)
                return text.Trim().ToLowerInvariant() is "true" or "1" or "yes" or "y" or "on";
            if (text is not null && IsNumeric(type))
                text = text.Trim().Replace(",", "");
            return Convert.ChangeType(text ?? value, type, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException or ArgumentException)
        {
            throw new InvalidCastException($"Cannot convert '{value}' ({value.GetType().Name}) to {type.Name}.", ex);
        }
    }

    private static bool IsNumeric(Type t) =>
        t == typeof(int) || t == typeof(long) || t == typeof(decimal) || t == typeof(double) || t == typeof(float) ||
        t == typeof(short) || t == typeof(byte) || t == typeof(uint) || t == typeof(ulong);

    internal static Type? GetElementType(Type type)
    {
        if (type == typeof(string)) return null;
        if (type.IsArray) return type.GetElementType();
        if (type.IsGenericType)
        {
            var def = type.GetGenericTypeDefinition();
            if (def == typeof(List<>) || def == typeof(IList<>) || def == typeof(IEnumerable<>) || def == typeof(ICollection<>) ||
                def == typeof(IReadOnlyList<>) || def == typeof(IReadOnlyCollection<>) || def == typeof(HashSet<>) || def == typeof(ISet<>))
                return type.GetGenericArguments()[0];
        }
        return null;
    }

    internal static string FormatValue(object? value) => value switch
    {
        null => "null",
        string s => $"'{s}'",
        IEnumerable e => "[" + string.Join(", ", e.Cast<object?>().Select(FormatValue)) + "]",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    /// <summary>Human-readable rendering used in "Scraped from" log lines.</summary>
    public override string ToString() =>
        "{" + string.Join(", ", AsDictionary().Select(kv => $"'{kv.Key}': {FormatValue(kv.Value)}")) + "}";

    // ---- implementations ------------------------------------------------------------------------

    private sealed class ItemItemAdapter(Item item) : ItemAdapter
    {
        public override object Item => item;

        public override IReadOnlyList<string> FieldNames =>
            item.Fields.Count > 0 ? [.. item.Fields.Keys] : [.. item.Keys];

        public override bool TryGet(string field, out object? value) => item.TryGetValue(field, out value);

        public override void Set(string field, object? value) => item[ResolveFieldName(field) ?? field] = value;

        public override bool HasField(string field) => item.IsDeclared(field);

        public override Field? GetFieldMeta(string field) => item.Fields.GetValueOrDefault(field);
    }

    private sealed class DictionaryAdapter(IDictionary<string, object?> dict) : ItemAdapter
    {
        public override object Item => dict;

        public override IReadOnlyList<string> FieldNames => [.. dict.Keys];

        public override bool TryGet(string field, out object? value) => dict.TryGetValue(field, out value);

        public override void Set(string field, object? value) => dict[field] = value;

        public override bool HasField(string field) => true;

        public override string? ResolveFieldName(string field) => field;
    }

    private sealed class LegacyDictionaryAdapter(IDictionary dict) : ItemAdapter
    {
        public override object Item => dict;

        public override IReadOnlyList<string> FieldNames => [.. dict.Keys.Cast<object>().Select(k => k.ToString()!)];

        public override bool TryGet(string field, out object? value)
        {
            value = dict[field];
            return dict.Contains(field);
        }

        public override void Set(string field, object? value) => dict[field] = value;

        public override bool HasField(string field) => true;

        public override string? ResolveFieldName(string field) => field;
    }

    private sealed class PocoAdapter(object item, TypeAccessor accessor) : ItemAdapter
    {
        public override object Item => item;

        public override IReadOnlyList<string> FieldNames => accessor.Names;

        public override bool TryGet(string field, out object? value)
        {
            var member = accessor.Find(field);
            if (member is null)
            {
                value = null;
                return false;
            }
            value = member.Getter(item);
            return true;
        }

        public override void Set(string field, object? value)
        {
            var member = accessor.Find(field) ?? throw new KeyNotFoundException($"{item.GetType().Name} has no property '{field}'.");
            if (member.Setter is null) throw new InvalidOperationException($"Property {item.GetType().Name}.{member.Name} is read-only.");
            member.Setter(item, ConvertTo(value, member.Type));
        }

        public override bool HasField(string field) => accessor.Find(field) is not null;

        public override string? ResolveFieldName(string field) => accessor.Find(field)?.Name;

        public override Type? GetFieldType(string field) => accessor.Find(field)?.Type;
    }

    internal sealed class MemberAccessor(string name, Type type, Func<object, object?> getter, Action<object, object?>? setter)
    {
        public string Name { get; } = name;
        public Type Type { get; } = type;
        public Func<object, object?> Getter { get; } = getter;
        public Action<object, object?>? Setter { get; } = setter;
    }

    /// <summary>Compiled property accessors for one CLR type.</summary>
    internal sealed class TypeAccessor
    {
        private static readonly ConcurrentDictionary<Type, TypeAccessor> Cache = new();

        private readonly Dictionary<string, MemberAccessor> _exact;
        private readonly Dictionary<string, MemberAccessor> _loose;

        private TypeAccessor(Type type)
        {
            var members = new List<MemberAccessor>();
            foreach (var p in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (p.GetIndexParameters().Length > 0 || p.GetMethod is null) continue;
                if (p.GetCustomAttribute<JsonIgnoreAttribute>() is { Condition: JsonIgnoreCondition.Always }) continue;
                if (p.Name == "EqualityContract") continue;
                members.Add(new MemberAccessor(p.Name, p.PropertyType, BuildGetter(type, p), p.SetMethod is { IsPublic: true } ? BuildSetter(type, p) : null));
            }
            foreach (var f in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
                members.Add(new MemberAccessor(f.Name, f.FieldType, o => f.GetValue(o), f.IsInitOnly ? null : (o, v) => f.SetValue(o, v)));

            Names = [.. members.Select(m => m.Name)];
            _exact = members.ToDictionary(m => m.Name, StringComparer.Ordinal);
            _loose = new Dictionary<string, MemberAccessor>(StringComparer.Ordinal);
            foreach (var m in members) _loose.TryAdd(NormalizeName(m.Name), m);
        }

        public IReadOnlyList<string> Names { get; }

        public static TypeAccessor Get(Type type) => Cache.GetOrAdd(type, static t => new TypeAccessor(t));

        public MemberAccessor? Find(string name) =>
            _exact.TryGetValue(name, out var m) ? m : _loose.GetValueOrDefault(NormalizeName(name));

        private static Func<object, object?> BuildGetter(Type type, PropertyInfo property)
        {
            var obj = Expression.Parameter(typeof(object), "o");
            var body = Expression.Convert(Expression.Property(Expression.Convert(obj, type), property), typeof(object));
            return Expression.Lambda<Func<object, object?>>(body, obj).Compile();
        }

        private static Action<object, object?> BuildSetter(Type type, PropertyInfo property)
        {
            // init-only setters are still public set methods; calling them after construction is fine
            // at the IL level, which is exactly what loaders need for records with init properties.
            var obj = Expression.Parameter(typeof(object), "o");
            var value = Expression.Parameter(typeof(object), "v");
            var assign = Expression.Call(Expression.Convert(obj, type), property.SetMethod!, Expression.Convert(value, property.PropertyType));
            return Expression.Lambda<Action<object, object?>>(assign, obj, value).Compile();
        }
    }
}
