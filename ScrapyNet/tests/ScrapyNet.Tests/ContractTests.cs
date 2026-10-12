using ScrapyNet.Cmdline;
using ScrapyNet.Contracts;
using ScrapyNet.Sandbox;
using Xunit;

namespace ScrapyNet.Tests;

public sealed class ContractBooksSpider : Spider
{
    public string BaseUrl { get; set; } = "http://localhost/";

    public override string Name => "contract-books";
    public override IReadOnlyList<string> StartUrls => [BaseUrl];

    [Url("/books/")]
    [Returns("requests", 20)]
    [Returns("items", 0, 0)]
    public override async IAsyncEnumerable<object> Parse(Response response)
    {
        foreach (var href in response.Css("article.product_pod h3 a::attr(href)").GetAll())
            yield return response.Follow(href, ParseBook);
        if (response.Css("li.next a::attr(href)").Get() is { } next)
            yield return response.Follow(next, Parse);
        await Task.CompletedTask;
    }

    [Url("/books/")]
    [Returns("items", 1, 1)]
    [Scrapes("title", "price", "isbn")]
    public IEnumerable<object> ParseListingAsBook(Response response)
    {
        yield return new Dictionary<string, object?> { ["title"] = response.Css("h1::text").Get(), ["price"] = 1m };
    }

    [Url("/books/catalogue/missing-page/index.html")]
    [Returns("items", 1)]
    public IEnumerable<object> ParseMissing(Response response)
    {
        yield break;
    }

    [Url("/books/")]
    [CbKwargs("""{"boom": true}""")]
    [Returns("items", 0)]
    public IEnumerable<object> ParseThatThrows(Response response)
    {
        if (response.CbKwargs["boom"] is true) throw new InvalidOperationException("kaboom");
        yield break;
    }

    public async IAsyncEnumerable<object> ParseBook(Response response)
    {
        yield return new { Title = response.Css("h1::text").Get(), Price = response.Css("p.price_color::text").Get() };
        await Task.CompletedTask;
    }
}

public class ContractTests(SandboxFixture sandbox)
{
    private Settings Quiet()
    {
        var s = new Settings();
        TestSettings.Quiet(s);
        s.Set(SettingKeys.LogLevel, "CRITICAL");
        return s;
    }

    [Fact]
    public void Lists_callbacks_with_contracts()
    {
        var methods = ContractRunner.List(typeof(ContractBooksSpider)).Select(m => m.Method.Name).ToList();
        Assert.Equal(["Parse", "ParseListingAsBook", "ParseMissing", "ParseThatThrows"], methods);
    }

    [Fact]
    public async Task Check_reports_passes_failures_and_errors()
    {
        var spider = new ContractBooksSpider { BaseUrl = sandbox.Url("/") };
        var report = await ContractRunner.CheckAsync([spider], Quiet(), TestContext.Current.CancellationToken);

        Assert.False(report.Success);
        string Outcome(string method, string check) => report.Results.Single(r => r.Method == method && r.Check == check).Outcome.ToString();

        Assert.Equal("Passed", report.Results.Where(r => r.Method == "Parse").Select(r => r.Outcome.ToString()).Distinct().Single());
        Assert.Equal("Passed", Outcome("ParseListingAsBook", "@returns post-hook"));
        Assert.Equal("Failed", Outcome("ParseListingAsBook", "@scrapes post-hook"));
        Assert.Contains("Missing fields: isbn", report.Results.Single(r => r.Check == "@scrapes post-hook").Message);
        // A 404 reaches the errback (HttpErrorMiddleware), which is an error, not a pass.
        Assert.Equal("Error", Outcome("ParseMissing", "download"));
        Assert.Contains("HttpErrorException", report.Results.Single(r => r.Method == "ParseMissing").Message);
        Assert.Equal("Error", Outcome("ParseThatThrows", "callback"));
        Assert.Contains("kaboom", report.Results.Single(r => r.Check == "callback").Message);


        var text = report.Format();
        Assert.Contains("FAIL: [contract-books] ParseListingAsBook (@scrapes post-hook)", text);
        Assert.EndsWith("FAILED (failures=1, errors=2)", text);
    }

    [Fact]
    public async Task Check_command_returns_exit_code_and_report()
    {
        using var output = new StringWriter();
        var original = Console.Out;
        Console.SetOut(output);
        int code;
        try
        {
            code = await ScrapyCommandLine.RunAsync(["check", "contract-books", "-s", $"CONTRACTS_BASE_URL={sandbox.Url("/")}", "-L", "CRITICAL", "-v"], typeof(ContractTests).Assembly);
        }
        finally
        {
            Console.SetOut(original);
        }
        Assert.Equal(1, code);
        Assert.Contains("[contract-books] Parse (@returns post-hook) ... ok", output.ToString());
        Assert.Contains("Ran 6 contracts", output.ToString());
    }
}
