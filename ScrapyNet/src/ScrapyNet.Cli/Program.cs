using System.Diagnostics;
using ScrapyNet;
using ScrapyNet.Cmdline;
using ScrapyNet.Sandbox;

// The global tool. Project-independent commands run here; commands that need the project's spiders
// (crawl, list, parse) are forwarded to "dotnet run" inside the project, whose Program.cs calls the
// same ScrapyCommandLine with its own assembly.
var parsed = CommandArgs.Parse(args);
switch (parsed.Command)
{
    case "sandbox":
        return await RunSandboxAsync(parsed);
    case "bench":
        return await BenchAsync(parsed);
    case "crawl" or "list" or "parse" or "check":
        var project = FindProject(Directory.GetCurrentDirectory());
        if (project is null)
        {
            Console.Error.WriteLine($"The '{parsed.Command}' command needs a Scrapy.Net project (a folder with a .csproj and scrapy.json).");
            Console.Error.WriteLine("Create one with: scrapynet startproject myproject");
            return 1;
        }
        return await ForwardAsync(project, args);
    case "help" when args.Length == 0:
    case "help":
        Console.WriteLine(ScrapyCommandLine.Usage());
        Console.WriteLine("\nTool-only commands:\n  sandbox       Run the offline practice website:  sandbox [--port 8080]\n  bench         Quick benchmark against the local sandbox");
        return 0;
    default:
        return await ScrapyCommandLine.RunAsync(args, typeof(Program).Assembly);
}

static string? FindProject(string dir)
{
    for (var d = new DirectoryInfo(dir); d is not null; d = d.Parent)
    {
        var csproj = d.GetFiles("*.csproj").FirstOrDefault();
        if (csproj is not null && File.Exists(Path.Combine(d.FullName, "scrapy.json"))) return csproj.FullName;
        if (csproj is not null) return csproj.FullName;
    }
    return null;
}

static async Task<int> ForwardAsync(string project, string[] args)
{
    var psi = new ProcessStartInfo("dotnet") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(project)! };
    psi.ArgumentList.Add("run");
    psi.ArgumentList.Add("--project");
    psi.ArgumentList.Add(project);
    psi.ArgumentList.Add("--");
    foreach (var a in args) psi.ArgumentList.Add(a);
    using var process = Process.Start(psi)!;
    await process.WaitForExitAsync();
    return process.ExitCode;
}

static async Task<int> RunSandboxAsync(CommandArgs args)
{
    var port = int.Parse(args.Option("port") ?? "8080");
    await using var site = await SandboxSite.StartAsync(new SandboxOptions { Port = port });
    Console.WriteLine($"Scrapy.Net sandbox running at {site.BaseUrl}/  (Ctrl+C to stop)");
    Console.WriteLine($"  Quotes  {site.Url("/quotes/")}\n  Books   {site.Url("/books/")}\n  News    {site.Url("/news/")}\n  Login   {site.Url("/login")}  (user '{SandboxSite.Username}', password '{SandboxSite.Password}')");
    var done = new TaskCompletionSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; done.TrySetResult(); };
    await done.Task;
    return 0;
}

static async Task<int> BenchAsync(CommandArgs args)
{
    await using var site = await SandboxSite.StartAsync();
    var settings = ScrapyCommandLine.BuildSettings(args);
    settings.Set(SettingKeys.LogLevel, "INFO", SettingPriority.Command);
    settings.Set(SettingKeys.LogStatsInterval, 1.0, SettingPriority.Command);
    settings.Set(SettingKeys.CloseSpiderTimeout, double.Parse(args.Option("seconds") ?? "10"), SettingPriority.Command);
    var result = await new Crawler(new BenchSpider(site.Url("/graph/0")), settings).CrawlAsync();
    var minutes = Math.Max(result.Elapsed.TotalMinutes, 1e-9);
    Console.WriteLine($"\nBench: {result.ResponseCount} pages in {result.Elapsed.TotalSeconds:0.00}s = {result.ResponseCount / minutes:N0} pages/min");
    return 0;
}

sealed class BenchSpider(string start) : Spider
{
    public override string Name => "bench";
    public override IReadOnlyList<string> StartUrls => [start];

    public override async IAsyncEnumerable<object> Parse(Response response)
    {
        foreach (var r in response.FollowAllCss("a")) yield return r with { DontFilter = response.Request.Depth < 3 };
        await Task.CompletedTask;
    }
}
