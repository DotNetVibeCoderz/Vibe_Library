using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace ScrapyNet.Cmdline;

/// <summary>Parsed command-line arguments.</summary>
public sealed class CommandArgs
{
    public string Command { get; set; } = "help";
    public List<string> Positional { get; } = [];
    public Dictionary<string, string> Settings { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> SpiderArgs { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<(string Uri, bool Overwrite)> Outputs { get; } = [];
    public Dictionary<string, string> Options { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Flags { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? Option(string name) => Options.GetValueOrDefault(name);

    public bool Flag(string name) => Flags.Contains(name);

    /// <summary>Scrapy-compatible parsing: <c>-s K=V</c>, <c>-a K=V</c>, <c>-o FILE</c>, <c>-O FILE</c>, <c>-L LEVEL</c>, <c>--opt value</c>, <c>--flag</c>.</summary>
    public static CommandArgs Parse(IReadOnlyList<string> args)
    {
        var result = new CommandArgs();
        var i = 0;
        if (args.Count > 0 && !args[0].StartsWith('-')) result.Command = args[i++].ToLowerInvariant();
        for (; i < args.Count; i++)
        {
            var a = args[i];
            string Next() => i + 1 < args.Count ? args[++i] : throw new UsageException($"Option {a} needs a value.");
            switch (a)
            {
                case "-s" or "--set":
                    var (sk, sv) = KeyValue(Next());
                    result.Settings[sk] = sv;
                    break;
                case "-a":
                    var (ak, av) = KeyValue(Next());
                    result.SpiderArgs[ak] = av;
                    break;
                case "-o" or "--output":
                    result.Outputs.Add((Next(), false));
                    break;
                case "-O" or "--overwrite-output":
                    result.Outputs.Add((Next(), true));
                    break;
                case "-L" or "--loglevel":
                    result.Settings[SettingKeys.LogLevel] = Next();
                    break;
                case "--logfile":
                    result.Settings[SettingKeys.LogFile] = Next();
                    break;
                case "--nolog":
                    result.Settings[SettingKeys.LogEnabled] = "false";
                    break;
                case "-c" or "--callback":
                    result.Options["callback"] = Next();
                    break;
                case "-t" or "--template":
                    result.Options["template"] = Next();
                    break;
                case "-d" or "--depth":
                    result.Options["depth"] = Next();
                    break;
                case "-v" or "--verbose":
                    result.Flags.Add("verbose");
                    break;
                case "-h" or "--help":
                    result.Flags.Add("help");
                    break;
                default:
                    if (a.StartsWith("--", StringComparison.Ordinal))
                    {
                        var name = a[2..];
                        var eq = name.IndexOf('=');
                        if (eq > 0) result.Options[name[..eq]] = name[(eq + 1)..];
                        else if (i + 1 < args.Count && !args[i + 1].StartsWith('-') && ValueOptions.Contains(name)) result.Options[name] = args[++i];
                        else result.Flags.Add(name);
                    }
                    else
                    {
                        result.Positional.Add(a);
                    }
                    break;
            }
        }
        return result;
    }

    private static readonly HashSet<string> ValueOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "spider", "env", "settings", "get", "getbool", "getint", "getfloat", "getlist", "meta", "cbkwargs", "output-format", "port",
    };

    private static (string, string) KeyValue(string s)
    {
        var eq = s.IndexOf('=');
        if (eq <= 0) throw new UsageException($"Expected KEY=VALUE, got '{s}'.");
        return (s[..eq], s[(eq + 1)..]);
    }
}

/// <summary>
/// The Scrapy-style command line. A Scrapy.Net project's <c>Program.cs</c> is a single line:
/// <code>return await ScrapyNet.Cmdline.ScrapyCommandLine.RunAsync(args, typeof(Program).Assembly);</code>
/// after which <c>dotnet run -- crawl NAME -o items.json</c> works as <c>scrapy crawl</c> does.
/// </summary>
public static class ScrapyCommandLine
{
    public static readonly IReadOnlyDictionary<string, string> Commands = new Dictionary<string, string>
    {
        ["crawl"] = "Run a spider:  crawl <spider> [-a k=v] [-o file] [-O file] [-s KEY=VALUE]",
        ["list"] = "List available spiders",
        ["parse"] = "Fetch a URL and show what a spider callback extracts:  parse <url> [--spider name] [-c callback] [-d depth]",
        ["fetch"] = "Download a URL and print the body:  fetch <url> [--headers] [--nolog]",
        ["view"] = "Open a URL in the browser as Scrapy.Net sees it:  view <url>",
        ["shell"] = "Interactive selector console:  shell [url|file]",
        ["settings"] = "Show setting values:  settings --get KEY",
        ["startproject"] = "Create a new project:  startproject <name> [dir]",
        ["genspider"] = "Generate a spider:  genspider [-t basic|crawl|sitemap|xmlfeed|csvfeed] <name> <domain>",
        ["version"] = "Print version:  version [-v]",
    };

    /// <summary>Runs a command. Spiders are looked up in <paramref name="spiderAssemblies"/> (default: the entry assembly).</summary>
    public static async Task<int> RunAsync(string[] args, params Assembly[] spiderAssemblies)
    {
        TextWriter output = Console.Out, error = Console.Error;
        try
        {
            var parsed = CommandArgs.Parse(args);
            if (parsed.Command is "help" or "-h" or "--help" || parsed.Flag("help") && parsed.Command == "help")
            {
                await output.WriteLineAsync(Usage()).ConfigureAwait(false);
                return 0;
            }
            var assemblies = spiderAssemblies.Length > 0 ? spiderAssemblies : [Assembly.GetEntryAssembly()!];
            return await ExecuteAsync(parsed, assemblies, output, error).ConfigureAwait(false);
        }
        catch (UsageException ex)
        {
            await error.WriteLineAsync("Usage error: " + ex.Message).ConfigureAwait(false);
            return 2;
        }
        catch (KeyNotFoundException ex)
        {
            await error.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }
    }

    public static string Usage() =>
        $"Scrapy.Net {Templates.PackageVersion} — Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil\n\nUsage:\n  <command> [options] [args]\n\nAvailable commands:\n" +
        string.Join("\n", Commands.Select(c => $"  {c.Key,-13} {c.Value}")) +
        "\n\nGlobal options:\n  -s KEY=VALUE   set a setting (repeatable)\n  -L LEVEL       log level (DEBUG, INFO, WARNING, ERROR)\n  --nolog        disable logging\n  --env NAME     also load scrapy.NAME.json\n  --settings F   load settings from a JSON file";

    /// <summary>Builds settings: defaults → scrapy.json (+ environment file) → env vars → <c>-s</c>.</summary>
    public static Settings BuildSettings(CommandArgs args, string? directory = null)
    {
        var settings = new Settings();
        directory ??= Directory.GetCurrentDirectory();
        settings.LoadProjectFiles(directory, args.Option("env"));
        if (!File.Exists(Path.Combine(directory, "scrapy.json")))
            settings.LoadProjectFiles(AppContext.BaseDirectory, args.Option("env"));
        if (args.Option("settings") is { } file) settings.LoadJsonFile(file);
        settings.LoadEnvironmentVariables();
        foreach (var (k, v) in args.Settings) settings.Set(k, ParseValue(v), SettingPriority.Cmdline);
        return settings;
    }

    private static object? ParseValue(string v)
    {
        var t = v.Trim();
        if (t.StartsWith('{') || t.StartsWith('['))
        {
            try
            {
                return ScrapyNet.Settings.FromJson(JsonDocument.Parse(t).RootElement);
            }
            catch (JsonException)
            {
            }
        }
        return v;
    }

    private static async Task<int> ExecuteAsync(CommandArgs args, Assembly[] assemblies, TextWriter output, TextWriter error)
    {
        switch (args.Command)
        {
            case "version":
                await output.WriteLineAsync($"Scrapy.Net {Templates.PackageVersion}").ConfigureAwait(false);
                if (args.Flag("verbose"))
                {
                    await output.WriteLineAsync($".NET      : {Environment.Version}").ConfigureAwait(false);
                    await output.WriteLineAsync($"OS        : {System.Runtime.InteropServices.RuntimeInformation.OSDescription}").ConfigureAwait(false);
                    await output.WriteLineAsync($"AngleSharp: {typeof(AngleSharp.IBrowsingContext).Assembly.GetName().Version}").ConfigureAwait(false);
                    await output.WriteLineAsync("Made by Gravicode Studios, led by Kang Fadhil").ConfigureAwait(false);
                }
                return 0;

            case "list":
                foreach (var name in SpiderLoader.FromAssemblies(assemblies).List()) await output.WriteLineAsync(name).ConfigureAwait(false);
                return 0;

            case "crawl":
                return await CrawlAsync(args, assemblies).ConfigureAwait(false);

            case "fetch":
                return await FetchAsync(args, output).ConfigureAwait(false);

            case "view":
            {
                var url = args.Positional.FirstOrDefault() ?? throw new UsageException("view <url>");
                var response = await Fetcher.FetchAsync(new Request(url), BuildQuietSettings(args)).ConfigureAwait(false);
                await output.WriteLineAsync(ScrapyShell.OpenInBrowser(response)).ConfigureAwait(false);
                return 0;
            }

            case "shell":
                await new ScrapyShell(Console.In, output, BuildQuietSettings(args)).RunAsync(args.Positional.FirstOrDefault()).ConfigureAwait(false);
                return 0;

            case "parse":
                return await ParseAsync(args, assemblies, output).ConfigureAwait(false);

            case "settings":
                return await SettingsCommandAsync(args, output).ConfigureAwait(false);

            case "startproject":
            {
                var name = args.Positional.FirstOrDefault() ?? throw new UsageException("startproject <name> [dir]");
                var dir = args.Positional.Count > 1 ? args.Positional[1] : name;
                Templates.CreateProject(name, dir);
                await output.WriteLineAsync($"""
                    New Scrapy.Net project '{name}' created in:
                        {Path.GetFullPath(dir)}

                    You can start your first spider with:
                        cd {dir}
                        scrapynet genspider example example.com
                        dotnet run -- crawl example -o items.json
                    """).ConfigureAwait(false);
                return 0;
            }

            case "genspider":
            {
                if (args.Positional.Count < 2) throw new UsageException("genspider [-t template] <name> <domain>");
                var template = args.Option("template") ?? "basic";
                var name = args.Positional[0];
                var ns = FindProjectNamespace(Directory.GetCurrentDirectory());
                var dir = Directory.Exists("Spiders") ? "Spiders" : ".";
                var path = Path.Combine(dir, Templates.ToPascal(name) + "Spider.cs");
                if (File.Exists(path) && !args.Flag("force")) throw new UsageException($"{path} already exists (use --force to overwrite).");
                await File.WriteAllTextAsync(path, Templates.Spider(template, name, args.Positional[1], ns)).ConfigureAwait(false);
                await output.WriteLineAsync($"Created spider '{name}' using template '{template}' in:\n  {Path.GetFullPath(path)}").ConfigureAwait(false);
                return 0;
            }

            default:
                await error.WriteLineAsync($"Unknown command: {args.Command}\n\n{Usage()}").ConfigureAwait(false);
                return 2;
        }
    }

    private static Settings BuildQuietSettings(CommandArgs args)
    {
        var s = BuildSettings(args);
        s.Set(SettingKeys.LogLevel, args.Settings.GetValueOrDefault(SettingKeys.LogLevel) ?? "WARNING", SettingPriority.Command + 1);
        s.Set(SettingKeys.StatsDump, false, SettingPriority.Command);
        s.Set(SettingKeys.LogStatsInterval, 0, SettingPriority.Command);
        s.Set(SettingKeys.HttpErrorAllowAll, true, SettingPriority.Command);
        return s;
    }

    private static async Task<int> CrawlAsync(CommandArgs args, Assembly[] assemblies)
    {
        var name = args.Positional.FirstOrDefault() ?? throw new UsageException("crawl <spider>");
        var loader = SpiderLoader.FromAssemblies(assemblies);
        var type = loader.Load(name);
        var settings = BuildSettings(args);
        foreach (var (uri, overwrite) in args.Outputs)
        {
            // "-o items.json:jsonlines" chooses the format explicitly, as in Scrapy.
            var colon = uri.LastIndexOf(':');
            var hasFormat = colon > 1 && !uri[(colon + 1)..].Contains('/') && !uri[(colon + 1)..].Contains('\\') && !uri[(colon + 1)..].Contains('.');
            var target = hasFormat ? uri[..colon] : uri;
            settings.Feeds.Add(target, new FeedOptions { Format = hasFormat ? uri[(colon + 1)..] : null, Overwrite = overwrite });
        }
        var process = new CrawlerProcess(settings);
        process.Crawl(type, args.SpiderArgs);
        var results = await process.StartAsync().ConfigureAwait(false);
        return results.Any(r => r.FinishReason is "engine_error") ? 1 : 0;
    }

    private static async Task<int> FetchAsync(CommandArgs args, TextWriter output)
    {
        var url = args.Positional.FirstOrDefault() ?? throw new UsageException("fetch <url>");
        var response = await Fetcher.FetchAsync(new Request(url), BuildQuietSettings(args)).ConfigureAwait(false);
        if (args.Flag("headers"))
        {
            await output.WriteLineAsync($"> {response.Request.Method} {response.Request.Url}").ConfigureAwait(false);
            foreach (var (k, v) in response.Request.Headers) await output.WriteLineAsync($"> {k}: {string.Join(", ", v)}").ConfigureAwait(false);
            await output.WriteLineAsync($"< {response.Protocol} {response.Status}").ConfigureAwait(false);
            foreach (var (k, v) in response.Headers) await output.WriteLineAsync($"< {k}: {string.Join(", ", v)}").ConfigureAwait(false);
            return 0;
        }
        if (response is TextResponse text) await output.WriteLineAsync(text.Text).ConfigureAwait(false);
        else
        {
            await using var stdout = Console.OpenStandardOutput();
            await stdout.WriteAsync(response.Body).ConfigureAwait(false);
        }
        return 0;
    }

    private static async Task<int> ParseAsync(CommandArgs args, Assembly[] assemblies, TextWriter output)
    {
        var url = args.Positional.FirstOrDefault() ?? throw new UsageException("parse <url> [--spider name] [-c callback]");
        var loader = SpiderLoader.FromAssemblies(assemblies);
        var spiderName = args.Option("spider") ?? loader.FindByUrl(url).FirstOrDefault()
            ?? throw new UsageException("No spider matches this URL's domain; pass --spider NAME.");
        var spider = (Spider)Activator.CreateInstance(loader.Load(spiderName))!;
        var depth = int.Parse(args.Option("depth") ?? "1", CultureInfo.InvariantCulture);
        var callbackName = args.Option("callback");
        var parser = new ParseProbeSpider(spider, url, callbackName, depth);
        var settings = BuildQuietSettings(args);
        await new Crawler(parser, settings).CrawlAsync(args.SpiderArgs).ConfigureAwait(false);

        foreach (var level in parser.Levels.OrderBy(l => l.Key))
        {
            await output.WriteLineAsync($"\n>>> STATUS DEPTH LEVEL {level.Key} <<<").ConfigureAwait(false);
            await output.WriteLineAsync($"# Scraped Items  {new string('-', 60)}").ConfigureAwait(false);
            var items = level.Value.Where(o => o is not Request).Select(o => ItemAdapter.For(o).AsDictionary()).ToList();
            await output.WriteLineAsync(JsonSerializer.Serialize(items, ScrapyJson.Indented)).ConfigureAwait(false);
            await output.WriteLineAsync($"\n# Requests  {new string('-', 65)}").ConfigureAwait(false);
            foreach (var r in level.Value.OfType<Request>()) await output.WriteLineAsync($"{r}  callback={r.CallbackName ?? "Parse"}").ConfigureAwait(false);
        }
        return 0;
    }

    private static async Task<int> SettingsCommandAsync(CommandArgs args, TextWriter output)
    {
        var settings = BuildSettings(args);
        if (args.Option("get") is { } key) await output.WriteLineAsync(Format(settings[key])).ConfigureAwait(false);
        else if (args.Option("getbool") is { } b) await output.WriteLineAsync(settings.GetBool(b).ToString()).ConfigureAwait(false);
        else if (args.Option("getint") is { } n) await output.WriteLineAsync(settings.GetInt(n).ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
        else if (args.Option("getfloat") is { } f) await output.WriteLineAsync(settings.GetDouble(f).ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
        else if (args.Option("getlist") is { } l) await output.WriteLineAsync(string.Join(", ", settings.GetList(l))).ConfigureAwait(false);
        else
            foreach (var (k, v) in settings) await output.WriteLineAsync($"{k} = {Format(v)}").ConfigureAwait(false);
        return 0;

        static string Format(object? v) => v switch
        {
            null => "None",
            string s => s,
            ComponentDictionary c => c.ToString(),
            FeedCollection f => "{" + string.Join(", ", f.Select(x => $"{x.Key}: {x.Value.Format}")) + "}",
            _ => ItemAdapter.FormatValue(v),
        };
    }

    private static string FindProjectNamespace(string directory)
    {
        var csproj = Directory.EnumerateFiles(directory, "*.csproj").FirstOrDefault();
        if (csproj is null) return Templates.ToPascal(Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar)));
        var text = File.ReadAllText(csproj);
        var start = text.IndexOf("<RootNamespace>", StringComparison.Ordinal);
        if (start >= 0)
        {
            var end = text.IndexOf("</RootNamespace>", start, StringComparison.Ordinal);
            return text[(start + 15)..end].Trim();
        }
        return Path.GetFileNameWithoutExtension(csproj);
    }

    /// <summary>Wraps a spider to run one callback on one URL and record what it yields, for <c>parse</c>.</summary>
    private sealed class ParseProbeSpider(Spider inner, string url, string? callbackName, int maxDepth) : Spider
    {
        public override string Name => inner.Name;

        public override IReadOnlyList<string> AllowedDomains => inner.AllowedDomains;

        public SortedDictionary<int, List<object>> Levels { get; } = [];

        public override async IAsyncEnumerable<Request> StartAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            var callback = callbackName is null ? inner.Parse : Callbacks.Resolve<Response>(inner, callbackName)!;
            yield return new Request(url) { DontFilter = true, Meta = { ["_parse_depth"] = 1 } }.WithCallback(r => Record(r, callback));
            await Task.CompletedTask.ConfigureAwait(false);
        }

        private async IAsyncEnumerable<object> Record(Response response, Func<Response, IAsyncEnumerable<object>> callback)
        {
            var depth = response.Request.GetMeta<int>("_parse_depth");
            if (!inner.IsBound) inner.Bind(Crawler);
            var list = new List<object>();
            lock (Levels) Levels[depth] = list;
            await foreach (var output in callback(response).ConfigureAwait(false))
            {
                lock (list) list.Add(output);
                if (output is Request next && depth < maxDepth)
                {
                    var cb = next.Callback ?? inner.Parse;
                    var child = next.WithCallback(r => Record(r, cb));
                    child.Meta["_parse_depth"] = depth + 1;
                    yield return child;
                }
            }
        }
    }
}
