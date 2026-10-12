using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace ScrapyNet.Contracts;

/// <summary>Raised by a contract whose condition does not hold.</summary>
public sealed class ContractFailException(string message) : Exception(message);

/// <summary>
/// A spider contract (Scrapy's <c>scrapy.contracts.Contract</c>): an attribute on a callback that tunes the
/// test request and checks the response before, or the callback output after, the callback runs.
/// <c>scrapynet check</c> (or <see cref="ContractRunner"/>) fetches each callback's <see cref="UrlAttribute"/>
/// and evaluates its contracts. Subclass this for custom contracts.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public abstract class ContractAttribute : Attribute
{
    /// <summary>Name shown in reports, as in Scrapy's docstrings: <c>@returns</c>.</summary>
    public abstract string Name { get; }

    /// <summary>True when <see cref="PreProcess"/> checks something (reported as "pre-hook").</summary>
    public virtual bool HasPreHook => false;

    /// <summary>True when <see cref="PostProcess"/> checks something (reported as "post-hook").</summary>
    public virtual bool HasPostHook => false;

    /// <summary>Adjusts the request built for the check.</summary>
    public virtual Request AdjustRequest(Request request) => request;

    /// <summary>Checks the response before the callback; throw <see cref="ContractFailException"/> to fail.</summary>
    public virtual void PreProcess(Response response) { }

    /// <summary>Checks the callback output; throw <see cref="ContractFailException"/> to fail.</summary>
    public virtual void PostProcess(IReadOnlyList<object> output) { }
}

/// <summary><c>@url</c>: the page to check the callback against. Relative URLs resolve against <c>CONTRACTS_BASE_URL</c> or the spider's first start URL.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class UrlAttribute(string url) : ContractAttribute
{
    public string Url { get; } = url;

    public override string Name => "url";
}

/// <summary><c>@cb_kwargs</c>: keyword arguments for the callback, as a JSON object.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class CbKwargsAttribute(string json) : ContractAttribute
{
    public override string Name => "cb_kwargs";

    public override Request AdjustRequest(Request request)
    {
        foreach (var p in JsonDocument.Parse(json).RootElement.EnumerateObject()) request.CbKwargs[p.Name] = Settings.FromJson(p.Value);
        return request;
    }
}

/// <summary><c>@meta</c>: request meta, as a JSON object.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class MetaAttribute(string json) : ContractAttribute
{
    public override string Name => "meta";

    public override Request AdjustRequest(Request request)
    {
        foreach (var p in JsonDocument.Parse(json).RootElement.EnumerateObject()) request.Meta[p.Name] = Settings.FromJson(p.Value);
        return request;
    }
}

/// <summary>
/// <c>@returns items|requests [min [max]]</c>: the callback yields between <paramref name="min"/> (default 1)
/// and <paramref name="max"/> (default unbounded) items or requests.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class ReturnsAttribute(string kind, int min = 1, int max = int.MaxValue) : ContractAttribute
{
    public override string Name => "returns";

    public override bool HasPostHook => true;

    public override void PostProcess(IReadOnlyList<object> output)
    {
        var requests = kind.StartsWith("request", StringComparison.OrdinalIgnoreCase);
        if (!requests && !kind.StartsWith("item", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"@returns expects 'items' or 'requests', not '{kind}'.");
        var count = output.Count(o => requests ? o is Request : ItemAdapter.IsItem(o));
        if (count < min || count > max)
        {
            var expected = max == int.MaxValue ? $"{min}..inf" : min == max ? $"{min}" : $"{min}..{max}";
            throw new ContractFailException($"Returned {count} {(requests ? "requests" : "items")}, expected {expected}");
        }
    }
}

/// <summary><c>@scrapes field ...</c>: every item yielded has these fields set (non-null).</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class ScrapesAttribute(params string[] fields) : ContractAttribute
{
    public override string Name => "scrapes";

    public override bool HasPostHook => true;

    public override void PostProcess(IReadOnlyList<object> output)
    {
        foreach (var item in output.Where(ItemAdapter.IsItem))
        {
            var adapter = ItemAdapter.For(item);
            var missing = fields.Where(f => adapter.ResolveFieldName(f) is not { } name || adapter.Get(name) is null).ToList();
            if (missing.Count > 0) throw new ContractFailException("Missing fields: " + string.Join(", ", missing));
        }
    }
}

public enum ContractOutcome { Passed, Failed, Error }

/// <summary>One evaluated contract: <c>[spider] method (@returns post-hook)</c>.</summary>
public sealed record ContractResult(string Spider, string Method, string Check, ContractOutcome Outcome, string? Message = null)
{
    public override string ToString() => $"[{Spider}] {Method} ({Check})";
}

/// <summary>The outcome of a check run, formatted like Scrapy's (unittest) report.</summary>
public sealed class ContractReport(IReadOnlyList<ContractResult> results, TimeSpan elapsed)
{
    public IReadOnlyList<ContractResult> Results { get; } = results;

    public TimeSpan Elapsed { get; } = elapsed;

    public bool Success => Results.All(r => r.Outcome == ContractOutcome.Passed);

    public string Format(bool verbose = false)
    {
        var sb = new StringBuilder();
        if (verbose)
            foreach (var r in Results)
                sb.AppendLine($"{r} ... {r.Outcome switch { ContractOutcome.Passed => "ok", ContractOutcome.Failed => "FAIL", _ => "ERROR" }}");
        else
            sb.AppendLine(string.Concat(Results.Select(r => r.Outcome switch { ContractOutcome.Passed => ".", ContractOutcome.Failed => "F", _ => "E" })));

        foreach (var r in Results.Where(r => r.Outcome != ContractOutcome.Passed))
        {
            sb.AppendLine().AppendLine(new string('=', 70));
            sb.AppendLine($"{(r.Outcome == ContractOutcome.Failed ? "FAIL" : "ERROR")}: {r}");
            sb.AppendLine(new string('-', 70)).AppendLine(r.Message);
        }
        sb.AppendLine().AppendLine(new string('-', 70));
        sb.AppendLine(System.FormattableString.Invariant($"Ran {Results.Count} contracts in {Elapsed.TotalSeconds:0.000}s")).AppendLine();
        var failures = Results.Count(r => r.Outcome == ContractOutcome.Failed);
        var errors = Results.Count(r => r.Outcome == ContractOutcome.Error);
        if (Success) sb.Append("OK");
        else sb.Append("FAILED (" + string.Join(", ", new[] { failures > 0 ? $"failures={failures}" : null, errors > 0 ? $"errors={errors}" : null }.Where(s => s is not null)) + ")");
        return sb.ToString();
    }
}

/// <summary>
/// Runs spider contracts (Scrapy's <c>scrapy check</c>). Each callback with <see cref="UrlAttribute"/> gets one
/// request, sent through the spider's real settings and middlewares; its output is checked and discarded
/// (nothing is followed, no item reaches a pipeline).
/// </summary>
public static class ContractRunner
{
    /// <summary>Callbacks of <paramref name="spiderType"/> that carry contracts, with their contracts.</summary>
    public static IReadOnlyList<(MethodInfo Method, IReadOnlyList<ContractAttribute> Contracts)> List(Type spiderType) =>
    [
        .. spiderType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(m => (Method: m, Contracts: (IReadOnlyList<ContractAttribute>)[.. m.GetCustomAttributes<ContractAttribute>(inherit: true)]))
            .Where(x => x.Contracts.OfType<UrlAttribute>().Any())
            .OrderBy(x => x.Method.Name, StringComparer.Ordinal),
    ];

    /// <summary>Checks the contracts of each spider in turn.</summary>
    public static async Task<ContractReport> CheckAsync(IEnumerable<Spider> spiders, Settings? settings = null, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var results = new List<ContractResult>();
        foreach (var spider in spiders) results.AddRange(await CheckSpiderAsync(spider, settings ?? new Settings(), cancellationToken).ConfigureAwait(false));
        return new ContractReport([.. results.OrderBy(r => r.Spider, StringComparer.Ordinal).ThenBy(r => r.Method, StringComparer.Ordinal)], stopwatch.Elapsed);
    }

    private static async Task<List<ContractResult>> CheckSpiderAsync(Spider spider, Settings settings, CancellationToken cancellationToken)
    {
        var results = new List<ContractResult>();
        var gate = new object();
        var done = new HashSet<string>(StringComparer.Ordinal);
        var methods = List(spider.GetType());
        if (methods.Count == 0) return results;

        var baseUrl = settings.GetString(SettingKeys.ContractsBaseUrl) ?? spider.StartUrls.FirstOrDefault();
        var requests = new List<Request>();
        foreach (var (method, contracts) in methods)
        {
            var name = method.Name;
            var url = contracts.OfType<UrlAttribute>().First().Url;
            // HasScheme first: on Linux and macOS "/books/" parses as an absolute file:// URI.
            if (!UrlUtils.HasScheme(url) || !Uri.TryCreate(url, UriKind.Absolute, out _))
                url = baseUrl is null ? url : new Uri(new Uri(baseUrl), url).ToString();

            void Record(string check, ContractOutcome outcome, string? message = null)
            {
                lock (gate) results.Add(new ContractResult(spider.Name, name, check, outcome, message));
            }

            async IAsyncEnumerable<object> Check(Response response)
            {
                lock (gate) done.Add(name);
                foreach (var c in contracts.Where(c => c.HasPreHook)) Evaluate(c, $"@{c.Name} pre-hook", () => c.PreProcess(response), Record);

                var callback = Callbacks.Resolve<Response>(spider, name)!;
                List<object>? output = null;
                try
                {
                    output = [];
                    await foreach (var o in callback(response).WithCancellation(cancellationToken).ConfigureAwait(false)) output.Add(o);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Record("callback", ContractOutcome.Error, $"{ex.GetType().Name}: {ex.Message}");
                    output = null;
                }
                if (output is not null)
                    foreach (var c in contracts.Where(c => c.HasPostHook)) Evaluate(c, $"@{c.Name} post-hook", () => c.PostProcess(output), Record);
                yield break;
            }

            async IAsyncEnumerable<object> Errback(Failure failure)
            {
                lock (gate) done.Add(name);
                Record("download", ContractOutcome.Error, failure.ToString());
                await Task.CompletedTask.ConfigureAwait(false);
                yield break;
            }

            Request request;
            try
            {
                request = new Request(url, Check) { DontFilter = true, Errback = Errback };
                foreach (var c in contracts) request = c.AdjustRequest(request);
            }
            catch (Exception ex) when (ex is ArgumentException or JsonException or FormatException)
            {
                lock (gate) done.Add(name);
                Record("request", ContractOutcome.Error, ex.Message);
                continue;
            }
            requests.Add(request);
        }

        spider.StartOverride = _ => requests.ToAsyncEnumerable();
        try
        {
            await new Crawler(spider, settings).CrawlAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            spider.StartOverride = null;
        }

        foreach (var (method, _) in methods.Where(m => !done.Contains(m.Method.Name)))
            results.Add(new ContractResult(spider.Name, method.Name, "request", ContractOutcome.Error,
                "The request never reached the callback (filtered by robots.txt, offsite, an HTTP error status, or a middleware)."));
        return results;
    }

    private static void Evaluate(ContractAttribute contract, string check, Action action, Action<string, ContractOutcome, string?> record)
    {
        try
        {
            action();
            record(check, ContractOutcome.Passed, null);
        }
        catch (ContractFailException ex)
        {
            record(check, ContractOutcome.Failed, ex.Message);
        }
        catch (Exception ex)
        {
            record(check, ContractOutcome.Error, $"{contract.GetType().Name}: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
