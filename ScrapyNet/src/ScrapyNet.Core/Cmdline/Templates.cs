using System.Globalization;
using System.Text;

namespace ScrapyNet.Cmdline;

/// <summary>Project and spider templates for <c>startproject</c> and <c>genspider</c>.</summary>
public static class Templates
{
    public static readonly IReadOnlyList<string> SpiderTemplates = ["basic", "crawl", "sitemap", "xmlfeed", "csvfeed"];

    /// <summary>Package version referenced by generated projects.</summary>
    public static string PackageVersion => typeof(Templates).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";

    /// <summary>Creates a new Scrapy.Net project directory. Returns the files written.</summary>
    public static IReadOnlyList<string> CreateProject(string name, string directory)
    {
        if (!IsIdentifier(name)) throw new UsageException($"Project name '{name}' must be a valid C# identifier (letters, digits, underscores).");
        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
            throw new UsageException($"Directory '{directory}' already exists and is not empty.");

        var files = new Dictionary<string, string>
        {
            [$"{name}.csproj"] = $"""
                <Project Sdk="Microsoft.NET.Sdk">

                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <RootNamespace>{name}</RootNamespace>
                  </PropertyGroup>

                  <ItemGroup>
                    <PackageReference Include="Gravicode.ScrapyNet" Version="{PackageVersion}" />
                  </ItemGroup>

                  <ItemGroup>
                    <None Update="scrapy*.json" CopyToOutputDirectory="PreserveNewest" />
                  </ItemGroup>

                </Project>
                """,
            ["Program.cs"] = """
                // Scrapy.Net project entry point. Run commands with:
                //   dotnet run -- list
                //   dotnet run -- crawl example -o items.json
                //   dotnet run -- shell https://example.com
                return await ScrapyNet.Cmdline.ScrapyCommandLine.RunAsync(args, typeof(Program).Assembly);
                """,
            ["Items.cs"] = $$"""
                namespace {{name}};

                // Define the shape of the data you scrape. Plain C# records and classes work as items.
                public sealed record ExampleItem(string? Title, string? Url);
                """,
            ["Pipelines.cs"] = $$"""
                using ScrapyNet;

                namespace {{name}};

                // Enable in scrapy.json:  "ITEM_PIPELINES": { "{{name}}.{{name}}Pipeline": 300 }
                public sealed class {{name}}Pipeline : ItemPipeline
                {
                    public override ValueTask<object> ProcessItemAsync(object item, Spider spider) => ValueTask.FromResult(item);
                }
                """,
            ["Middlewares.cs"] = $$"""
                using ScrapyNet;

                namespace {{name}};

                // Enable in scrapy.json:  "DOWNLOADER_MIDDLEWARES": { "{{name}}.{{name}}DownloaderMiddleware": 543 }
                public sealed class {{name}}DownloaderMiddleware : DownloaderMiddleware
                {
                    public override ValueTask<DownloadResult> ProcessRequestAsync(Request request, Spider spider) => DownloadResult.Continue;
                }
                """,
            ["scrapy.json"] = $$"""
                {
                  // Scrapy.Net settings. Keys match Scrapy's documentation.
                  "BOT_NAME": "{{name}}",
                  "ROBOTSTXT_OBEY": true,
                  "CONCURRENT_REQUESTS_PER_DOMAIN": 1,
                  "DOWNLOAD_DELAY": 1,
                  "FEED_EXPORT_ENCODING": "utf-8",
                  "ITEM_PIPELINES": {},
                  "DOWNLOADER_MIDDLEWARES": {}
                }
                """,
            ["scrapy.dev.json"] = """
                {
                  // Used when SCRAPYNET_ENV=dev (or --env dev): cache responses while you iterate on selectors.
                  "HTTPCACHE_ENABLED": true,
                  "LOG_LEVEL": "DEBUG"
                }
                """,
            ["scrapy.prod.json"] = """
                {
                  // Used when SCRAPYNET_ENV=prod (or --env prod).
                  "AUTOTHROTTLE_ENABLED": true,
                  "LOG_LEVEL": "INFO",
                  "JOBDIR": "crawls/current"
                }
                """,
            ["Spiders/README.md"] = """
                # Spiders

                Add spiders here, or generate one:

                    scrapynet genspider example example.com
                    scrapynet genspider -t crawl books books.toscrape.com
                """,
            [".gitignore"] = "bin/\nobj/\n.scrapynet/\ncrawls/\n*.json.gz\n",
        };

        var written = new List<string>();
        foreach (var (relative, content) in files)
        {
            var path = Path.Combine(directory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content.ReplaceLineEndings(Environment.NewLine) + Environment.NewLine);
            written.Add(path);
        }
        return written;
    }

    /// <summary>Source of a new spider from one of <see cref="SpiderTemplates"/>.</summary>
    public static string Spider(string template, string name, string domain, string @namespace)
    {
        var className = ToPascal(name) + "Spider";
        var url = domain.Contains("://") ? domain : "https://" + domain;
        var host = new Uri(url).Host;
        return template switch
        {
            "basic" => $$"""
                using ScrapyNet;

                namespace {{@namespace}}.Spiders;

                public sealed class {{className}} : Spider
                {
                    public override string Name => "{{name}}";
                    public override IReadOnlyList<string> AllowedDomains => ["{{host}}"];
                    public override IReadOnlyList<string> StartUrls => ["{{url}}/"];

                    public override async IAsyncEnumerable<object> Parse(Response response)
                    {
                        yield return new { Title = response.Css("title::text").Get(), response.Url };

                        foreach (var request in response.FollowAllCss("a"))
                            yield return request;
                        await Task.CompletedTask;
                    }
                }
                """,
            "crawl" => $$"""
                using ScrapyNet;

                namespace {{@namespace}}.Spiders;

                public sealed class {{className}} : CrawlSpider
                {
                    public override string Name => "{{name}}";
                    public override IReadOnlyList<string> AllowedDomains => ["{{host}}"];
                    public override IReadOnlyList<string> StartUrls => ["{{url}}/"];

                    protected override IReadOnlyList<Rule> Rules =>
                    [
                        new(new LinkExtractor { Allow = ["Items/"] }, ParseItem) { Follow = true },
                    ];

                    private IEnumerable<object> ParseItem(Response response)
                    {
                        yield return new
                        {
                            Title = response.Css("h1::text").Get(),
                            response.Url,
                        };
                    }
                }
                """,
            "sitemap" => $$"""
                using ScrapyNet;

                namespace {{@namespace}}.Spiders;

                public sealed class {{className}} : SitemapSpider
                {
                    public override string Name => "{{name}}";
                    public override IReadOnlyList<string> AllowedDomains => ["{{host}}"];
                    public override IReadOnlyList<string> SitemapUrls => ["{{url}}/robots.txt"];

                    protected override IReadOnlyList<SitemapRule> SitemapRules => [new("", ParsePage)];

                    private IEnumerable<object> ParsePage(Response response)
                    {
                        yield return new { Title = response.Css("title::text").Get(), response.Url };
                    }
                }
                """,
            "xmlfeed" => $$"""
                using ScrapyNet;

                namespace {{@namespace}}.Spiders;

                public sealed class {{className}} : XmlFeedSpider
                {
                    public override string Name => "{{name}}";
                    public override IReadOnlyList<string> AllowedDomains => ["{{host}}"];
                    public override IReadOnlyList<string> StartUrls => ["{{url}}/feed.xml"];
                    public override string IterTag => "item";

                    protected override IEnumerable<object> ParseNode(Response response, Selector node)
                    {
                        yield return new
                        {
                            Title = node.Xpath("//title/text()").Get(),
                            Link = node.Xpath("//link/text()").Get(),
                        };
                    }
                }
                """,
            "csvfeed" => $$"""
                using ScrapyNet;

                namespace {{@namespace}}.Spiders;

                public sealed class {{className}} : CsvFeedSpider
                {
                    public override string Name => "{{name}}";
                    public override IReadOnlyList<string> AllowedDomains => ["{{host}}"];
                    public override IReadOnlyList<string> StartUrls => ["{{url}}/feed.csv"];

                    protected override IEnumerable<object> ParseRow(Response response, IReadOnlyDictionary<string, string> row)
                    {
                        yield return row;
                    }
                }
                """,
            _ => throw new UsageException($"Unknown template '{template}'. Available: {string.Join(", ", SpiderTemplates)}"),
        };
    }

    public static string ToPascal(string name)
    {
        var sb = new StringBuilder();
        var upper = true;
        foreach (var c in name)
        {
            if (!char.IsLetterOrDigit(c))
            {
                upper = true;
                continue;
            }
            sb.Append(upper ? char.ToUpper(c, CultureInfo.InvariantCulture) : c);
            upper = false;
        }
        if (sb.Length == 0 || char.IsDigit(sb[0])) sb.Insert(0, 'S');
        return sb.ToString();
    }

    private static bool IsIdentifier(string name) =>
        name.Length > 0 && (char.IsLetter(name[0]) || name[0] == '_') && name.All(c => char.IsLetterOrDigit(c) || c == '_');
}
