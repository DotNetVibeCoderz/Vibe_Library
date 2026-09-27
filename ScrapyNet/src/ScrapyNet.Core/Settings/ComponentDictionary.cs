using System.Collections;
using System.Globalization;
using System.Reflection;

namespace ScrapyNet;

/// <summary>
/// An ordered registry of components — middlewares, pipelines, extensions — as in Scrapy's
/// <c>DOWNLOADER_MIDDLEWARES = {"path.Class": 543}</c>. Lower orders sit closer to the engine.
/// </summary>
/// <remarks>
/// A component can be registered by <see cref="Type"/> (constructed by the crawler, which injects
/// <see cref="Crawler"/>, <see cref="Settings"/>, and so on into its constructor), by a ready-made
/// instance, or by type name (for JSON settings files). Registering <c>null</c> as the order disables a
/// component, including a built-in default.
/// </remarks>
public sealed class ComponentDictionary : IEnumerable<ComponentDictionary.Entry>
{
    private readonly List<Entry> _entries = [];
    private Settings _owner;

    internal ComponentDictionary(Settings owner) => _owner = owner;

    /// <summary>A registration. <see cref="Component"/> is a <see cref="Type"/>, an instance, or an unresolved type name.</summary>
    public sealed record Entry(object Component, int? Order)
    {
        /// <summary>The component's type, resolving a type name if needed.</summary>
        public Type? ComponentType => Component switch
        {
            Type t => t,
            string s => TypeResolver.Resolve(s),
            _ => Component.GetType(),
        };

        public bool IsEnabled => Order.HasValue;
    }

    public int Count => _entries.Count;

    public ComponentDictionary Add<T>(int order) => Set(typeof(T), order);

    public ComponentDictionary Add(Type type, int order) => Set(type, order);

    /// <summary>Registers a ready-made instance (it will not be constructed by the crawler).</summary>
    public ComponentDictionary Add(object instance, int order)
    {
        ArgumentNullException.ThrowIfNull(instance);
        return instance is Type t ? Set(t, order) : Set(instance, order);
    }

    /// <summary>Registers by type name, e.g. from a JSON settings file.</summary>
    public ComponentDictionary Add(string typeName, int? order) => Set((object?)TypeResolver.Resolve(typeName) ?? typeName, order);

    /// <summary>Disables a component (including a default one).</summary>
    public ComponentDictionary Disable<T>() => Set(typeof(T), null);

    public ComponentDictionary Disable(Type type) => Set(type, null);

    public ComponentDictionary Disable(string typeName) => Set((object?)TypeResolver.Resolve(typeName) ?? typeName, null);

    public bool Contains<T>() => IsEnabled(typeof(T));

    /// <summary>True when a component of this type is registered and enabled.</summary>
    public bool IsEnabled(Type type) => _entries.Any(e => e.IsEnabled && e.ComponentType == type);

    /// <summary>Order of a registered type, or <c>null</c>.</summary>
    public int? GetOrder(Type type) => _entries.FirstOrDefault(e => e.ComponentType == type)?.Order;

    /// <summary>Enabled components sorted by order (stable for equal orders).</summary>
    public IReadOnlyList<Entry> GetEnabled() =>
        _entries.Select((e, i) => (e, i)).Where(x => x.e.IsEnabled).OrderBy(x => x.e.Order).ThenBy(x => x.i).Select(x => x.e).ToList();

    private ComponentDictionary Set(object component, int? order)
    {
        _owner.EnsureMutable();
        var type = component as Type ?? (component is string s ? TypeResolver.Resolve(s) : null);
        var index = _entries.FindIndex(e => ReferenceEquals(e.Component, component) || (type is not null && e.Component is Type or string && e.ComponentType == type)
            || (component is string name && e.Component is string other && other == name));
        var entry = new Entry(component, order);
        if (index >= 0) _entries[index] = entry;
        else _entries.Add(entry);
        return this;
    }

    /// <summary>Merges a raw dictionary (type or type-name → order) from JSON or <see cref="Settings.Set(string, object?, SettingPriority)"/>.</summary>
    internal void Merge(object raw)
    {
        switch (raw)
        {
            case ComponentDictionary other:
                foreach (var e in other._entries) Set(e.Component, e.Order);
                break;
            case IDictionary<Type, int?> typed:
                foreach (var (t, o) in typed) Set(t, o);
                break;
            case IDictionary<Type, int> typed2:
                foreach (var (t, o) in typed2) Set(t, o);
                break;
            case IDictionary dict:
                foreach (DictionaryEntry de in dict)
                {
                    int? order = de.Value is null ? null : Convert.ToInt32(de.Value, CultureInfo.InvariantCulture);
                    if (de.Key is Type t) Set(t, order);
                    else Add(de.Key.ToString()!, order);
                }
                break;
            default:
                throw new ArgumentException($"Cannot merge {raw.GetType().Name} into a component registry.");
        }
    }

    internal ComponentDictionary Clone(Settings owner)
    {
        var copy = new ComponentDictionary(owner);
        copy._entries.AddRange(_entries);
        return copy;
    }

    public IEnumerator<Entry> GetEnumerator() => _entries.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() =>
        "{" + string.Join(", ", GetEnabled().Select(e => $"{(e.Component is Type t ? t.Name : e.Component is string s ? s : e.Component.GetType().Name)}: {e.Order}")) + "}";
}

/// <summary>Resolves type names from settings files against every loaded assembly.</summary>
public static class TypeResolver
{
    public static Type? Resolve(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var type = Type.GetType(name, throwOnError: false);
        if (type is not null) return type;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                type = asm.GetType(name, throwOnError: false);
                if (type is not null) return type;
            }
            catch (Exception ex) when (ex is ReflectionTypeLoadException or FileNotFoundException or BadImageFormatException)
            {
                // Dynamic or broken assemblies: skip.
            }
        }
        // Allow short names for built-ins: "RetryMiddleware".
        foreach (var candidate in BuiltInNamespaces)
        {
            type = typeof(TypeResolver).Assembly.GetType($"{candidate}.{name}", throwOnError: false);
            if (type is not null) return type;
        }
        return null;
    }

    private static readonly string[] BuiltInNamespaces =
    [
        "ScrapyNet", "ScrapyNet.DownloaderMiddlewares", "ScrapyNet.SpiderMiddlewares", "ScrapyNet.Pipelines",
        "ScrapyNet.Extensions", "ScrapyNet.Exporters", "ScrapyNet.Engine", "ScrapyNet.Handlers",
    ];
}
