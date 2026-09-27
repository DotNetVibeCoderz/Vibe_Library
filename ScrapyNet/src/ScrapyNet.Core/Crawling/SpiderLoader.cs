using System.Reflection;

namespace ScrapyNet;

/// <summary>
/// Finds spiders by name in assemblies (Scrapy's <c>SpiderLoader</c>), for <c>crawl NAME</c> and <c>list</c>.
/// </summary>
public sealed class SpiderLoader
{
    private readonly Dictionary<string, Type> _spiders = new(StringComparer.Ordinal);

    public static SpiderLoader FromAssemblies(params Assembly[] assemblies)
    {
        var loader = new SpiderLoader();
        foreach (var assembly in assemblies.Distinct())
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = [.. ex.Types.Where(t => t is not null).Cast<Type>()];
            }
            foreach (var type in types)
            {
                if (!typeof(Spider).IsAssignableFrom(type) || type.IsAbstract || type.GetConstructor(Type.EmptyTypes) is null) continue;
                loader.Add(type);
            }
        }
        return loader;
    }

    /// <summary>Registers a spider type. Names must be unique.</summary>
    public void Add(Type spiderType)
    {
        var name = GetName(spiderType);
        if (_spiders.TryGetValue(name, out var existing) && existing != spiderType)
            throw new InvalidOperationException($"There are several spiders with the same name '{name}': {existing.FullName} and {spiderType.FullName}.");
        _spiders[name] = spiderType;
    }

    public IReadOnlyList<string> List() => [.. _spiders.Keys.OrderBy(k => k, StringComparer.Ordinal)];

    public Type Load(string name) =>
        _spiders.TryGetValue(name, out var type) ? type : throw new KeyNotFoundException($"Spider not found: {name}");

    public bool TryLoad(string name, out Type? type) => _spiders.TryGetValue(name, out type);

    /// <summary>Spiders whose allowed domains cover the URL (for <c>parse</c> and <c>fetch --spider</c> auto-detection).</summary>
    public IReadOnlyList<string> FindByUrl(string url)
    {
        var host = UrlUtils.GetHost(url);
        return [.. _spiders.Where(kv =>
        {
            var spider = (Spider)Activator.CreateInstance(kv.Value)!;
            return spider.AllowedDomains.Count > 0 && UrlUtils.HostMatchesAny(host, spider.AllowedDomains);
        }).Select(kv => kv.Key)];
    }

    /// <summary>A spider's name, read from a throwaway instance (the name is a virtual property).</summary>
    public static string GetName(Type spiderType)
    {
        try
        {
            return ((Spider)Activator.CreateInstance(spiderType)!).Name;
        }
        catch (MissingMethodException)
        {
            return Spider.DefaultName(spiderType);
        }
    }
}
