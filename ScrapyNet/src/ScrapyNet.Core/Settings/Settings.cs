using System.Collections;
using System.Globalization;
using System.Text.Json;

namespace ScrapyNet;

/// <summary>Where a setting value came from; a value only overrides one of equal or lower priority.</summary>
public enum SettingPriority
{
    Default = 0,
    Command = 10,
    Addon = 15,
    Project = 20,
    Spider = 30,
    Cmdline = 40,
}

/// <summary>
/// Crawler configuration: string keys with Scrapy's names (<c>CONCURRENT_REQUESTS</c>, <c>DOWNLOAD_DELAY</c>,
/// ...), each value tagged with the priority it was set at. Port of <c>scrapy.settings.Settings</c>.
/// </summary>
/// <remarks>
/// <para>
/// Priorities make layering predictable: defaults &lt; project file &lt; spider <see cref="Spider.ConfigureSettings"/>
/// &lt; command line <c>-s KEY=VALUE</c>. Setting a key at a lower priority than it already has is a no-op.
/// </para>
/// <para>
/// Component registries (<see cref="DownloaderMiddlewares"/>, <see cref="SpiderMiddlewares"/>,
/// <see cref="ItemPipelines"/>, <see cref="Extensions"/>) and <see cref="Feeds"/> are typed objects rather
/// than raw dictionaries, so components are referenced by <see cref="Type"/> with compile-time checking.
/// </para>
/// <para>
/// A crawler freezes its settings when it starts; changing them afterwards throws, because components
/// have already read them.
/// </para>
/// </remarks>
public sealed class Settings : IEnumerable<KeyValuePair<string, object?>>
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    private readonly record struct Entry(object? Value, int Priority);

    /// <summary>Creates settings pre-populated with Scrapy.Net's defaults (priority <see cref="SettingPriority.Default"/>).</summary>
    public Settings() : this(withDefaults: true) { }

    public Settings(bool withDefaults)
    {
        if (withDefaults) DefaultSettings.ApplyTo(this);
    }

    /// <summary>Creates settings from key/value pairs at project priority, on top of the defaults.</summary>
    public Settings(IEnumerable<KeyValuePair<string, object?>> values, SettingPriority priority = SettingPriority.Project) : this()
    {
        Update(values, priority);
    }

    public bool IsFrozen { get; private set; }

    public object? this[string key]
    {
        get => _entries.TryGetValue(key, out var e) ? e.Value : null;
        set => Set(key, value);
    }

    /// <summary>Sets a value if <paramref name="priority"/> is at least the current one.</summary>
    public Settings Set(string key, object? value, SettingPriority priority = SettingPriority.Project) => Set(key, value, (int)priority);

    public Settings Set(string key, object? value, int priority)
    {
        EnsureMutable();
        if (_entries.TryGetValue(key, out var existing))
        {
            if (priority < existing.Priority) return this;
            // Component registries and feeds merge rather than replace, as Scrapy merges the _BASE dicts.
            if (existing.Value is ComponentDictionary components && value is not ComponentDictionary && value is not null)
            {
                components.Merge(value);
                _entries[key] = existing with { Priority = priority };
                return this;
            }
            if (existing.Value is FeedCollection feeds && value is not FeedCollection && value is not null)
            {
                feeds.Merge(value);
                _entries[key] = existing with { Priority = priority };
                return this;
            }
        }
        _entries[key] = new Entry(value, priority);
        return this;
    }

    /// <summary>Sets a value only if the key has none yet.</summary>
    public Settings SetDefault(string key, object? value, SettingPriority priority = SettingPriority.Project)
    {
        if (!_entries.ContainsKey(key)) Set(key, value, priority);
        return this;
    }

    public Settings Update(IEnumerable<KeyValuePair<string, object?>> values, SettingPriority priority = SettingPriority.Project)
    {
        foreach (var (k, v) in values) Set(k, v, priority);
        return this;
    }

    public bool Contains(string key) => _entries.ContainsKey(key);

    public bool Remove(string key)
    {
        EnsureMutable();
        return _entries.Remove(key);
    }

    /// <summary>Priority of the current value, or <c>null</c> when unset.</summary>
    public int? GetPriority(string key) => _entries.TryGetValue(key, out var e) ? e.Priority : null;

    /// <summary>Highest priority of any setting (Scrapy's <c>maxpriority</c>).</summary>
    public int MaxPriority => _entries.Count == 0 ? 0 : _entries.Values.Max(e => e.Priority);

    // ---- typed getters --------------------------------------------------------------------------

    public string? GetString(string key, string? defaultValue = null) =>
        this[key] switch
        {
            null => defaultValue,
            string s => s,
            JsonElement je => je.ValueKind == JsonValueKind.String ? je.GetString() : je.ToString(),
            var v => Convert.ToString(v, CultureInfo.InvariantCulture),
        };

    public bool GetBool(string key, bool defaultValue = false) =>
        this[key] switch
        {
            null => defaultValue,
            bool b => b,
            string s => s.Trim().ToLowerInvariant() is "true" or "1" or "yes" or "on",
            JsonElement { ValueKind: JsonValueKind.True } => true,
            JsonElement { ValueKind: JsonValueKind.False } => false,
            var v => Convert.ToDouble(v, CultureInfo.InvariantCulture) != 0,
        };

    public int GetInt(string key, int defaultValue = 0) =>
        this[key] switch
        {
            null => defaultValue,
            int i => i,
            string s when string.IsNullOrWhiteSpace(s) => defaultValue,
            string s => (int)double.Parse(s, CultureInfo.InvariantCulture),
            JsonElement je => je.ValueKind == JsonValueKind.String ? int.Parse(je.GetString()!, CultureInfo.InvariantCulture) : (int)je.GetDouble(),
            var v => Convert.ToInt32(v, CultureInfo.InvariantCulture),
        };

    public long GetLong(string key, long defaultValue = 0) =>
        this[key] switch
        {
            null => defaultValue,
            long l => l,
            string s when string.IsNullOrWhiteSpace(s) => defaultValue,
            string s => (long)double.Parse(s, CultureInfo.InvariantCulture),
            JsonElement je => je.ValueKind == JsonValueKind.String ? long.Parse(je.GetString()!, CultureInfo.InvariantCulture) : (long)je.GetDouble(),
            var v => Convert.ToInt64(v, CultureInfo.InvariantCulture),
        };

    public double GetDouble(string key, double defaultValue = 0) =>
        this[key] switch
        {
            null => defaultValue,
            double d => d,
            string s when string.IsNullOrWhiteSpace(s) => defaultValue,
            string s => double.Parse(s, CultureInfo.InvariantCulture),
            JsonElement je => je.ValueKind == JsonValueKind.String ? double.Parse(je.GetString()!, CultureInfo.InvariantCulture) : je.GetDouble(),
            TimeSpan t => t.TotalSeconds,
            var v => Convert.ToDouble(v, CultureInfo.InvariantCulture),
        };

    /// <summary>A duration stored as seconds (number or <see cref="TimeSpan"/>).</summary>
    public TimeSpan GetTimeSpan(string key, TimeSpan defaultValue = default) =>
        this[key] is TimeSpan t ? t : Contains(key) && this[key] is not null ? TimeSpan.FromSeconds(GetDouble(key)) : defaultValue;

    /// <summary>A list from a list value, a JSON array, or a comma-separated string.</summary>
    public IReadOnlyList<string> GetList(string key, IReadOnlyList<string>? defaultValue = null)
    {
        var value = this[key];
        switch (value)
        {
            case null:
                return defaultValue ?? [];
            case string s when s.TrimStart().StartsWith('['):
                return JsonSerializer.Deserialize<List<JsonElement>>(s, ScrapyJson.Default)!.Select(e => e.ToString()).ToList();
            case string s:
                return s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            case JsonElement { ValueKind: JsonValueKind.Array } je:
                return je.EnumerateArray().Select(e => e.ToString()).ToList();
            case JsonElement je:
                return GetListFromString(je.ToString());
            case IEnumerable e:
                return e.Cast<object?>().Select(x => Convert.ToString(x, CultureInfo.InvariantCulture) ?? "").ToList();
            default:
                return [Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""];
        }

        static IReadOnlyList<string> GetListFromString(string s) => s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public IReadOnlyList<int> GetIntList(string key, IReadOnlyList<int>? defaultValue = null) =>
        Contains(key) && this[key] is not null
            ? GetList(key).Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToList()
            : defaultValue ?? [];

    /// <summary>A dictionary from a dictionary value or a JSON object (string or element).</summary>
    public IReadOnlyDictionary<string, object?> GetDict(string key)
    {
        var value = this[key];
        switch (value)
        {
            case null:
                return new Dictionary<string, object?>();
            case IDictionary<string, object?> d:
                return new Dictionary<string, object?>(d, StringComparer.OrdinalIgnoreCase);
            case IDictionary<string, string> ds:
                return ds.ToDictionary(kv => kv.Key, kv => (object?)kv.Value, StringComparer.OrdinalIgnoreCase);
            case IDictionary nd:
                var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (DictionaryEntry de in nd) result[de.Key.ToString()!] = de.Value;
                return result;
            case string s when !string.IsNullOrWhiteSpace(s):
                return JsonToDict(JsonDocument.Parse(s).RootElement);
            case JsonElement { ValueKind: JsonValueKind.Object } je:
                return JsonToDict(je);
            default:
                throw new InvalidCastException($"Setting {key} is not a dictionary.");
        }
    }

    /// <summary>Generic typed getter with conversion.</summary>
    public T? Get<T>(string key, T? defaultValue = default)
    {
        var value = this[key];
        if (value is null) return defaultValue;
        if (value is T t) return t;
        if (value is JsonElement je) return je.Deserialize<T>();
        return (T?)ItemAdapter.ConvertTo(value, typeof(T));
    }

    /// <summary>A <see cref="Type"/> setting: a Type value or an assembly-qualified / full type name.</summary>
    public Type? GetTypeSetting(string key) => this[key] switch
    {
        null => null,
        Type t => t,
        var v => TypeResolver.Resolve(Convert.ToString(v, CultureInfo.InvariantCulture)!),
    };

    // ---- structured settings --------------------------------------------------------------------

    public ComponentDictionary DownloaderMiddlewares => GetComponents(SettingKeys.DownloaderMiddlewares);

    public ComponentDictionary SpiderMiddlewares => GetComponents(SettingKeys.SpiderMiddlewares);

    public ComponentDictionary ItemPipelines => GetComponents(SettingKeys.ItemPipelines);

    public ComponentDictionary Extensions => GetComponents(SettingKeys.Extensions);

    /// <summary>Registers the download handler for a URL scheme (e.g. <c>"https"</c> → a browser-rendering handler). <c>null</c> disables the scheme.</summary>
    public Settings SetDownloadHandler(string scheme, Type? handlerType, SettingPriority priority = SettingPriority.Project)
    {
        var handlers = new Dictionary<string, object?>(GetDict(SettingKeys.DownloadHandlers), StringComparer.OrdinalIgnoreCase) { [scheme] = handlerType };
        return Set(SettingKeys.DownloadHandlers, handlers, priority);
    }

    /// <summary>Feed exports: output URI → options. Same as Scrapy's <c>FEEDS</c>.</summary>
    public FeedCollection Feeds
    {
        get
        {
            if (this[SettingKeys.Feeds] is FeedCollection feeds) return feeds;
            var created = new FeedCollection(this);
            if (this[SettingKeys.Feeds] is { } raw) created.Merge(raw);
            _entries[SettingKeys.Feeds] = new Entry(created, GetPriority(SettingKeys.Feeds) ?? (int)SettingPriority.Default);
            return created;
        }
    }

    private ComponentDictionary GetComponents(string key)
    {
        if (this[key] is ComponentDictionary c) return c;
        var created = new ComponentDictionary(this);
        if (this[key] is { } raw) created.Merge(raw);
        _entries[key] = new Entry(created, GetPriority(key) ?? (int)SettingPriority.Default);
        return created;
    }

    // ---- lifecycle ------------------------------------------------------------------------------

    /// <summary>Makes the settings read-only.</summary>
    public void Freeze() => IsFrozen = true;

    /// <summary>A mutable deep copy (component registries and feeds are cloned, other values shared).</summary>
    public Settings Copy()
    {
        var copy = new Settings(withDefaults: false);
        foreach (var (key, entry) in _entries)
        {
            var value = entry.Value switch
            {
                ComponentDictionary c => c.Clone(copy),
                FeedCollection f => f.Clone(copy),
                Dictionary<string, object?> d => new Dictionary<string, object?>(d, StringComparer.OrdinalIgnoreCase),
                List<string> l => new List<string>(l),
                _ => entry.Value,
            };
            copy._entries[key] = new Entry(value, entry.Priority);
        }
        return copy;
    }

    public Settings FrozenCopy()
    {
        var copy = Copy();
        copy.Freeze();
        return copy;
    }

    internal void EnsureMutable()
    {
        if (IsFrozen) throw new InvalidOperationException("Trying to modify frozen settings; configure before the crawler starts.");
    }

    // ---- loading --------------------------------------------------------------------------------

    /// <summary>
    /// Loads a JSON settings file (<c>scrapy.json</c>): a flat object of setting keys. Component registries
    /// map type names to orders (<c>null</c> disables); <c>FEEDS</c> maps URIs to feed options.
    /// </summary>
    public Settings LoadJsonFile(string path, SettingPriority priority = SettingPriority.Project)
    {
        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        return LoadJson(doc.RootElement, priority);
    }

    public Settings LoadJson(JsonElement root, SettingPriority priority = SettingPriority.Project)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new FormatException("Settings JSON must be an object.");
        foreach (var prop in root.EnumerateObject()) Set(prop.Name, FromJson(prop.Value), priority);
        return this;
    }

    /// <summary>
    /// Loads <c>scrapy.json</c> and then <c>scrapy.{environment}.json</c> from a directory, so dev, test and
    /// prod can differ (Scrapy's <c>settings_dev.py</c> pattern). The environment defaults to the
    /// <c>SCRAPYNET_ENV</c> variable.
    /// </summary>
    public Settings LoadProjectFiles(string directory, string? environment = null, SettingPriority priority = SettingPriority.Project)
    {
        var baseFile = Path.Combine(directory, "scrapy.json");
        if (File.Exists(baseFile)) LoadJsonFile(baseFile, priority);
        environment ??= Environment.GetEnvironmentVariable("SCRAPYNET_ENV");
        if (!string.IsNullOrWhiteSpace(environment))
        {
            var envFile = Path.Combine(directory, $"scrapy.{environment}.json");
            if (File.Exists(envFile)) LoadJsonFile(envFile, priority);
        }
        return this;
    }

    /// <summary>Applies environment variables named <c>{prefix}KEY</c> (default <c>SCRAPYNET_SETTINGS_</c>).</summary>
    public Settings LoadEnvironmentVariables(string prefix = "SCRAPYNET_SETTINGS_", SettingPriority priority = SettingPriority.Project)
    {
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var name = entry.Key.ToString()!;
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                Set(name[prefix.Length..], entry.Value?.ToString(), priority);
        }
        return this;
    }

    /// <summary>Converts a JSON value to plain .NET values (string, int, long, double, bool, List, Dictionary).</summary>
    public static object? FromJson(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.TryGetInt32(out var i) ? i : e.TryGetInt64(out var l) ? l : e.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.Array => e.EnumerateArray().Select(FromJson).ToList(),
        JsonValueKind.Object => JsonToDict(e),
        _ => e.ToString(),
    };

    private static Dictionary<string, object?> JsonToDict(JsonElement e)
    {
        var d = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in e.EnumerateObject()) d[p.Name] = FromJson(p.Value);
        return d;
    }

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() =>
        _entries.OrderBy(e => e.Key, StringComparer.Ordinal).Select(e => new KeyValuePair<string, object?>(e.Key, e.Value.Value)).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
