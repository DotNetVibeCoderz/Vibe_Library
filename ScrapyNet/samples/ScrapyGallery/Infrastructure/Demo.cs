using System.Reflection;
using System.Text;

namespace ScrapyGallery.Infrastructure;

/// <summary>What a demo is about, from the reader's point of view.</summary>
public enum DemoGroup
{
    Extract,
    Crawl,
    Process,
    Operate,
    Intelligence,
    Platform,
}

/// <summary>A text field shown above the Run button (query, question, rules...).</summary>
public sealed record DemoInput(string Key, string Label, string LabelId, string Default, bool Multiline = false);

/// <summary>
/// One gallery entry: bilingual description, the code that runs (read from this demo's own source
/// file, between <c>// &lt;demo&gt;</c> markers), and <see cref="RunAsync"/>.
/// </summary>
public abstract class Demo
{
    public abstract DemoGroup Group { get; }

    public abstract string Title { get; }

    public abstract string TitleId { get; }

    public abstract string Summary { get; }

    public abstract string SummaryId { get; }

    /// <summary>Scrapy concepts demonstrated, shown as small tags.</summary>
    public abstract IReadOnlyList<string> Concepts { get; }

    public virtual IReadOnlyList<DemoInput> Inputs => [];

    /// <summary>True when the demo produces a crawl web worth watching.</summary>
    public virtual bool ShowsWeb => true;

    public abstract Task RunAsync(DemoContext context);

    public string Id => GetType().Name.Replace("Demo", "").ToLowerInvariant();

    /// <summary>The demo's source code: the region between the demo markers in its .cs file.</summary>
    public string SourceCode => SourceCache.GetOrAdd(GetType(), static t => ReadSource(t));

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, string> SourceCache = new();

    private static string ReadSource(Type type)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"demo:{type.Name}.cs");
        if (stream is null) return $"// Source of {type.Name} not embedded.";
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var text = reader.ReadToEnd().Replace("\r\n", "\n");
        var start = text.IndexOf("// <demo>", StringComparison.Ordinal);
        var end = text.IndexOf("// </demo>", StringComparison.Ordinal);
        if (start < 0 || end < start) return text.Trim();
        var region = text[(text.IndexOf('\n', start) + 1)..end];
        return Dedent(region).TrimEnd();
    }

    private static string Dedent(string code)
    {
        var lines = code.Split('\n');
        var indent = lines.Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
        return string.Join('\n', lines.Select(l => l.Length >= indent ? l[indent..] : l.TrimStart()));
    }
}
