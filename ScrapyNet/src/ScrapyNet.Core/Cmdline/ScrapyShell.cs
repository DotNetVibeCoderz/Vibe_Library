using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace ScrapyNet.Cmdline;

/// <summary>
/// An interactive console for trying selectors against live pages — Scrapy's <c>scrapy shell</c>.
/// Fetch a page once, then run as many CSS/XPath/regex queries as you like against it.
/// </summary>
/// <remarks>
/// C# has no built-in expression REPL, so the shell speaks a small command language instead
/// (<c>css</c>, <c>xpath</c>, <c>re</c>, <c>links</c>, ...). For full C# interactivity, use
/// <see cref="Fetcher"/> from a .NET Interactive notebook — see the notebooks folder.
/// </remarks>
public sealed class ScrapyShell(TextReader input, TextWriter output, Settings? settings = null)
{
    private Response? _response;
    private SelectorList? _last;

    public Response? Response => _response;

    public static string Help => """
        Available commands:
          fetch <url>          download a page (through the full middleware stack)
          load <file>          load a local HTML/XML file
          css <query>          run a CSS query (supports ::text and ::attr(name))
          xpath <query>        run an XPath query
          re <pattern>         apply a regex to the last selection
          text                 whitespace-normalized text of the last selection
          get [n]              full markup/value of result n of the last selection (default 0)
          links [css]          links found by the link extractor (optionally inside a region)
          json [path]          pretty-print the JSON body, or a dotted path in it (e.g. products.0.title)
          headers              response headers
          status / url         response status / final URL
          body [n]             first n characters of the body (default 2000)
          view                 open the response in a web browser
          curl                 the request as a curl command
          help                 this help
          exit                 quit
        """;

    /// <summary>Runs the read-eval-print loop until <c>exit</c> or end of input.</summary>
    public async Task RunAsync(string? initialUrl = null, CancellationToken cancellationToken = default)
    {
        await output.WriteLineAsync("Scrapy.Net shell — type 'help' for commands. Dibuat oleh Gravicode Studios.").ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(initialUrl))
            await ExecuteAsync(File.Exists(initialUrl) ? $"load {initialUrl}" : $"fetch {initialUrl}", cancellationToken).ConfigureAwait(false);

        while (!cancellationToken.IsCancellationRequested)
        {
            await output.WriteAsync(">>> ").ConfigureAwait(false);
            var line = await input.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null) break;
            line = line.Trim();
            if (line.Length == 0) continue;
            if (line is "exit" or "quit") break;
            await ExecuteAsync(line, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Executes one shell command (exposed for tests and for embedding the shell).</summary>
    public async Task ExecuteAsync(string line, CancellationToken cancellationToken = default)
    {
        var space = line.IndexOf(' ');
        var command = (space < 0 ? line : line[..space]).ToLowerInvariant();
        var arg = space < 0 ? "" : line[(space + 1)..].Trim();
        try
        {
            switch (command)
            {
                case "help" or "?":
                    await output.WriteLineAsync(Help).ConfigureAwait(false);
                    break;
                case "fetch":
                    Require(arg, "fetch <url>");
                    var url = UrlUtils.HasScheme(arg) ? arg : "https://" + arg;
                    var sw = Stopwatch.StartNew();
                    _response = await Fetcher.FetchAsync(new Request(url), settings ?? DefaultSettings(), cancellationToken).ConfigureAwait(false);
                    _last = null;
                    await output.WriteLineAsync($"[s] {_response} ({_response.Body.Length:N0} bytes, {_response.GetType().Name}, {sw.ElapsedMilliseconds} ms)").ConfigureAwait(false);
                    break;
                case "load":
                    Require(arg, "load <file>");
                    var path = Path.GetFullPath(arg);
                    var body = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
                    _response = ResponseTypes.Create(new Uri(path).AbsoluteUri, 200, new Headers(), body);
                    _last = null;
                    await output.WriteLineAsync($"[s] loaded {path} ({body.Length:N0} bytes)").ConfigureAwait(false);
                    break;
                case "css":
                    Require(arg, "css <query>");
                    Print(_last = RequireResponse().Css(arg));
                    break;
                case "xpath":
                    Require(arg, "xpath <query>");
                    Print(_last = RequireResponse().Xpath(arg));
                    break;
                case "re":
                    Require(arg, "re <pattern>");
                    var source = _last ?? new SelectorList([RequireResponse().Selector]);
                    PrintStrings(source.Re(arg));
                    break;
                case "text":
                    await output.WriteLineAsync((_last ?? new SelectorList([RequireResponse().Selector])).GetText()).ConfigureAwait(false);
                    break;
                case "get":
                    var index = arg.Length == 0 ? 0 : int.Parse(arg, System.Globalization.CultureInfo.InvariantCulture);
                    await output.WriteLineAsync(_last is { Count: > 0 } ? _last[index].Get() : "(no selection)").ConfigureAwait(false);
                    break;
                case "links":
                    var extractor = arg.Length == 0 ? new LinkExtractor() : new LinkExtractor { RestrictCss = [arg] };
                    var links = extractor.ExtractLinks(RequireResponse());
                    for (var i = 0; i < links.Count; i++)
                        await output.WriteLineAsync($"[{i}] {links[i].Url}  {Trim(links[i].Text, 60)}").ConfigureAwait(false);
                    await output.WriteLineAsync($"{links.Count} links").ConfigureAwait(false);
                    break;
                case "json":
                    await output.WriteLineAsync(JsonPath(RequireResponse(), arg)).ConfigureAwait(false);
                    break;
                case "headers":
                    var r = RequireResponse();
                    await output.WriteLineAsync($"{r.Protocol ?? "HTTP"} {r.Status}").ConfigureAwait(false);
                    await output.WriteLineAsync(r.Headers.ToString()).ConfigureAwait(false);
                    break;
                case "status":
                    await output.WriteLineAsync(RequireResponse().Status.ToString(System.Globalization.CultureInfo.InvariantCulture)).ConfigureAwait(false);
                    break;
                case "url":
                    await output.WriteLineAsync(RequireResponse().Url).ConfigureAwait(false);
                    break;
                case "body":
                    var n = arg.Length == 0 ? 2000 : int.Parse(arg, System.Globalization.CultureInfo.InvariantCulture);
                    await output.WriteLineAsync(Trim(RequireResponse().Text, n)).ConfigureAwait(false);
                    break;
                case "view":
                    await output.WriteLineAsync("Opened " + OpenInBrowser(RequireResponse())).ConfigureAwait(false);
                    break;
                case "curl":
                    await output.WriteLineAsync(RequireResponse().Request.ToCurl()).ConfigureAwait(false);
                    break;
                default:
                    await output.WriteLineAsync($"Unknown command '{command}'. Type 'help'.").ConfigureAwait(false);
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await output.WriteLineAsync($"Error: {ex.Message}").ConfigureAwait(false);
        }
    }

    private static Settings DefaultSettings()
    {
        var s = new Settings();
        s.Set(SettingKeys.LogLevel, "WARNING", SettingPriority.Command);
        s.Set(SettingKeys.StatsDump, false, SettingPriority.Command);
        s.Set(SettingKeys.LogStatsInterval, 0, SettingPriority.Command);
        s.Set(SettingKeys.HttpErrorAllowAll, true, SettingPriority.Command);
        return s;
    }

    private void Print(SelectorList list)
    {
        for (var i = 0; i < Math.Min(list.Count, 50); i++)
            output.WriteLine($"[{i}] {Trim(list[i].Get().Replace("\n", "\\n"), 160)}");
        output.WriteLine(list.Count > 50 ? $"... {list.Count} results" : $"{list.Count} result(s)");
    }

    private void PrintStrings(IReadOnlyList<string> values)
    {
        for (var i = 0; i < values.Count; i++) output.WriteLine($"[{i}] {values[i]}");
        output.WriteLine($"{values.Count} match(es)");
    }

    private Response RequireResponse() => _response ?? throw new InvalidOperationException("No response yet: use 'fetch <url>' first.");

    private static void Require(string arg, string usage)
    {
        if (string.IsNullOrWhiteSpace(arg)) throw new UsageException("Usage: " + usage);
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    internal static string JsonPath(Response response, string path)
    {
        if (response is not TextResponse text) return "Response is binary, not JSON.";
        System.Text.Json.Nodes.JsonNode? node;
        try
        {
            node = text.Json();
        }
        catch (JsonException)
        {
            return "Response body is not JSON.";
        }
        foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
            node = int.TryParse(part, out var i) ? node?[i] : node?[part];
        return node?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null";
    }

    /// <summary>Writes the body to a temporary file (with a <c>&lt;base&gt;</c> so assets resolve) and opens it.</summary>
    public static string OpenInBrowser(Response response)
    {
        var ext = response is HtmlResponse ? ".html" : response is XmlResponse ? ".xml" : response is JsonResponse ? ".json" : ".txt";
        var path = Path.Combine(Path.GetTempPath(), $"scrapynet-{Guid.NewGuid():N}{ext}");
        var body = response.Body;
        if (response is HtmlResponse html && !html.Text.Contains("<base", StringComparison.OrdinalIgnoreCase))
        {
            var text = html.Text;
            var head = text.IndexOf("<head>", StringComparison.OrdinalIgnoreCase);
            text = head >= 0 ? text.Insert(head + 6, $"<base href=\"{response.Url}\">") : $"<base href=\"{response.Url}\">" + text;
            body = Encoding.UTF8.GetBytes(text);
        }
        File.WriteAllBytes(path, body);
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Headless machine: the file path is still useful.
        }
        return path;
    }
}
