using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ScrapyNet;

/// <summary>
/// Base class for spiders: where crawling starts and how responses are turned into items and
/// follow-up requests. Port of <c>scrapy.Spider</c>.
/// </summary>
/// <remarks>
/// <para>
/// Callbacks yield a mix of items (any object that is not a <see cref="Request"/>) and requests.
/// Write them as iterators — either synchronous (<c>IEnumerable&lt;object&gt;</c>) or async
/// (<c>async IAsyncEnumerable&lt;object&gt;</c>) when the callback needs to await something:
/// </para>
/// <code>
/// public sealed class QuotesSpider : Spider
/// {
///     public override string Name => "quotes";
///     public override IReadOnlyList&lt;string&gt; StartUrls => ["https://quotes.toscrape.com/"];
///
///     public override async IAsyncEnumerable&lt;object&gt; Parse(Response response)
///     {
///         foreach (var quote in response.Css("div.quote"))
///             yield return new { Text = quote.Css("span.text::text").Get(), Author = quote.Css("small.author::text").Get() };
///
///         if (response.Css("li.next a::attr(href)").Get() is { } next)
///             yield return response.Follow(next, Parse);
///     }
/// }
/// </code>
/// </remarks>
public abstract class Spider
{
    private Crawler? _crawler;
    private ILogger? _logger;
    private Dictionary<string, string> _arguments = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Unique spider name used by <c>scrapynet crawl NAME</c> and in logs and stats. Defaults to the
    /// class name without a <c>Spider</c> suffix, lower-cased (<c>QuotesSpider</c> → <c>quotes</c>).
    /// </summary>
    public virtual string Name => DefaultName(GetType());

    /// <summary>Domains this spider may crawl; requests elsewhere are filtered by the offsite middleware. Empty = no restriction.</summary>
    public virtual IReadOnlyList<string> AllowedDomains => [];

    /// <summary>URLs to start from when <see cref="StartAsync"/> is not overridden.</summary>
    public virtual IReadOnlyList<string> StartUrls => [];

    /// <summary>Extra HTTP statuses this spider's callbacks want to receive (instead of the HTTP error middleware filtering them).</summary>
    public virtual IReadOnlyList<int>? HandleHttpStatusList => null;

    /// <summary>
    /// Per-spider settings (Scrapy's <c>custom_settings</c>). The <paramref name="settings"/> object starts
    /// empty; whatever is set here is applied at <see cref="SettingPriority.Spider"/>, above the project
    /// settings and below the command line.
    /// </summary>
    public virtual void ConfigureSettings(Settings settings) { }

    /// <summary>The crawler running this spider.</summary>
    public Crawler Crawler => _crawler ?? throw new InvalidOperationException($"Spider {Name} is not bound to a crawler yet.");

    public bool IsBound => _crawler is not null;

    public Settings Settings => Crawler.Settings;

    /// <summary>Logger named after the spider.</summary>
    public ILogger Logger => _logger ?? NullLogger.Instance;

    /// <summary>
    /// State persisted between runs when <c>JOBDIR</c> is set (Scrapy's <c>spider.state</c>). Values must be
    /// JSON-serializable.
    /// </summary>
    public Dictionary<string, object?> State { get; internal set; } = new(StringComparer.Ordinal);

    /// <summary>Spider arguments (<c>-a key=value</c>). Arguments are also assigned to matching public settable properties.</summary>
    public IReadOnlyDictionary<string, string> Arguments => _arguments;

    /// <summary>
    /// Produces the initial requests (Scrapy's <c>start()</c>/<c>start_requests()</c>). The default yields
    /// a request per <see cref="StartUrls"/> entry, not subject to duplicate filtering, handled by <see cref="Parse"/>.
    /// Consumed lazily: a spider can yield millions of start requests without holding them in memory.
    /// </summary>
    public virtual async IAsyncEnumerable<Request> StartAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var url in StartUrls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new Request(url) { DontFilter = true };
        }
        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>Default callback for responses whose request has no explicit callback.</summary>
    public virtual IAsyncEnumerable<object> Parse(Response response) =>
        throw new NotImplementedException($"{GetType().Name}.Parse callback is not defined");

    /// <summary>Called after the spider opens (Scrapy's <c>spider_opened</c> for the spider itself).</summary>
    public virtual ValueTask OnOpenedAsync() => ValueTask.CompletedTask;

    /// <summary>Called when the spider closes, with the reason (<c>finished</c>, <c>shutdown</c>, <c>closespider_itemcount</c>, ...).</summary>
    public virtual ValueTask OnClosedAsync(string reason) => ValueTask.CompletedTask;

    /// <summary>Logs at information level through the spider's logger.</summary>
    protected void Log(string message, LogLevel level = LogLevel.Information) => Logger.Log(level, "{Message}", message);

    internal void Bind(Crawler crawler)
    {
        _crawler = crawler;
        _logger = crawler.LoggerFactory.CreateLogger(Name);
    }

    /// <summary>Applies <c>-a</c> arguments: stored in <see cref="Arguments"/> and assigned to matching properties.</summary>
    internal void ApplyArguments(IReadOnlyDictionary<string, string>? arguments)
    {
        if (arguments is null) return;
        foreach (var (key, value) in arguments)
        {
            _arguments[key] = value;
            var property = GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(p => p.SetMethod is { IsPublic: true } && ItemAdapter.NormalizeName(p.Name) == ItemAdapter.NormalizeName(key));
            if (property is null) continue;
            object? converted;
            if (property.PropertyType == typeof(IReadOnlyList<string>) || property.PropertyType == typeof(List<string>) || property.PropertyType == typeof(string[]))
                converted = ItemAdapter.ConvertTo(value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries), property.PropertyType);
            else
                converted = ItemAdapter.ConvertTo(value, property.PropertyType);
            property.SetValue(this, converted);
        }
    }

    internal static string DefaultName(Type type)
    {
        var name = type.Name;
        if (name.EndsWith("Spider", StringComparison.Ordinal) && name.Length > 6) name = name[..^6];
        return name.ToLower(CultureInfo.InvariantCulture);
    }

    public override string ToString() => $"<{GetType().Name} '{Name}'>";
}
