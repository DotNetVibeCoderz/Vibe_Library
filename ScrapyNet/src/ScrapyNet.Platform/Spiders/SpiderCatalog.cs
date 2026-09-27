using System.Text.Json;

namespace ScrapyNet.Platform;

/// <summary>
/// The platform's spider registry: create, edit (every edit is a new version), delete, enable/disable,
/// clone and roll back spiders, whether code spiders or declarative ones. Optionally persisted to a
/// JSON file.
/// </summary>
public sealed class SpiderCatalog
{
    private static readonly JsonSerializerOptions Json = new(ScrapyJson.Web) { WriteIndented = true };

    private readonly Dictionary<string, List<SpiderDefinition>> _versions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Type> _types = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly string? _path;

    public SpiderCatalog(string? path = null)
    {
        _path = path;
        if (path is not null && File.Exists(path))
        {
            var saved = JsonSerializer.Deserialize<Dictionary<string, List<SpiderDefinition>>>(File.ReadAllText(path), Json) ?? [];
            foreach (var (id, versions) in saved) _versions[id] = versions;
        }
    }

    /// <summary>Raised after any change, with the affected definition.</summary>
    public event Action<SpiderDefinition>? Changed;

    /// <summary>Registers a code spider type.</summary>
    public SpiderDefinition Register<TSpider>(string? id = null, string? description = null) where TSpider : Spider, new() =>
        Register(typeof(TSpider), id, description);

    public SpiderDefinition Register(Type spiderType, string? id = null, string? description = null)
    {
        var name = SpiderLoader.GetName(spiderType);
        var sample = (Spider)Activator.CreateInstance(spiderType)!;
        lock (_gate) _types[spiderType.FullName!] = spiderType;
        return Create(new SpiderDefinition
        {
            Id = id ?? name,
            Name = name,
            SpiderType = spiderType.FullName,
            Description = description,
            Target = new TargetConfig { StartUrls = [.. sample.StartUrls], AllowedDomains = [.. sample.AllowedDomains] },
        });
    }

    public SpiderDefinition Create(SpiderDefinition definition)
    {
        lock (_gate)
        {
            if (_versions.ContainsKey(definition.Id)) throw new InvalidOperationException($"Spider '{definition.Id}' already exists.");
            var created = definition with { Version = 1, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
            _versions[definition.Id] = [created];
            Persist();
            Changed?.Invoke(created);
            return created;
        }
    }

    /// <summary>Edits a spider; the result is saved as a new version and the old one is kept.</summary>
    public SpiderDefinition Update(string id, Func<SpiderDefinition, SpiderDefinition> edit)
    {
        lock (_gate)
        {
            var versions = Versions(id);
            var latest = versions[^1];
            var updated = edit(latest) with { Id = id, Version = latest.Version + 1, CreatedAt = latest.CreatedAt, UpdatedAt = DateTimeOffset.UtcNow };
            _versions[id].Add(updated);
            Persist();
            Changed?.Invoke(updated);
            return updated;
        }
    }

    public SpiderDefinition Enable(string id) => Update(id, d => d with { Enabled = true });

    public SpiderDefinition Disable(string id) => Update(id, d => d with { Enabled = false });

    /// <summary>Copies a spider under a new id and name (version history starts fresh).</summary>
    public SpiderDefinition Clone(string id, string newId, string? newName = null) =>
        Create(Get(id) with { Id = newId, Name = newName ?? newId, Description = $"Clone of {id}" });

    /// <summary>Makes an older version current again (as a new version).</summary>
    public SpiderDefinition Rollback(string id, int version)
    {
        var target = Versions(id).FirstOrDefault(v => v.Version == version) ?? throw new KeyNotFoundException($"Spider '{id}' has no version {version}.");
        return Update(id, _ => target);
    }

    public bool Delete(string id)
    {
        lock (_gate)
        {
            if (!_versions.Remove(id, out var removed)) return false;
            Persist();
            Changed?.Invoke(removed[^1]);
            return true;
        }
    }

    public SpiderDefinition Get(string id) => Versions(id)[^1];

    public bool TryGet(string id, out SpiderDefinition? definition)
    {
        lock (_gate)
        {
            definition = _versions.TryGetValue(id, out var v) ? v[^1] : null;
            return definition is not null;
        }
    }

    public IReadOnlyList<SpiderDefinition> Versions(string id)
    {
        lock (_gate)
        {
            return _versions.TryGetValue(id, out var v) ? [.. v] : throw new KeyNotFoundException($"Spider '{id}' not found.");
        }
    }

    /// <summary>Current version of every spider.</summary>
    public IReadOnlyList<SpiderDefinition> List()
    {
        lock (_gate) return [.. _versions.Values.Select(v => v[^1]).OrderBy(d => d.Name, StringComparer.Ordinal)];
    }

    /// <summary>Instantiates the spider a definition describes.</summary>
    public Spider CreateSpider(SpiderDefinition definition)
    {
        if (definition.SpiderType is not null)
        {
            Type? type;
            lock (_gate) type = _types.GetValueOrDefault(definition.SpiderType);
            type ??= TypeResolver.Resolve(definition.SpiderType) ?? throw new InvalidOperationException($"Spider type {definition.SpiderType} not found.");
            return (Spider)Activator.CreateInstance(type)!;
        }
        return new DeclarativeSpider(definition);
    }

    private void Persist()
    {
        if (_path is null) return;
        var dir = Path.GetDirectoryName(Path.GetFullPath(_path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(_path, JsonSerializer.Serialize(_versions, Json));
    }
}
