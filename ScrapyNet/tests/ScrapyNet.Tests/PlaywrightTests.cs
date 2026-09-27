using ScrapyNet.Playwright;
using Xunit;

namespace ScrapyNet.Tests;

public class PlaywrightTests(SandboxFixture sandbox)
{
    private sealed class JsQuotesSpider(string url) : Spider
    {
        public override async IAsyncEnumerable<Request> StartAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            // The same page twice: once plain HTTP (no quotes, they need JavaScript), once rendered.
            yield return new Request(url) { DontFilter = true, Meta = { ["mode"] = "http" } };
            yield return new Request(url) { DontFilter = true, Meta = { ["mode"] = "browser" } }
                .WithPlaywright(PageMethod.WaitForSelector("div.quote"), PageMethod.Evaluate("document.querySelectorAll('div.quote').length"));
            await Task.CompletedTask;
        }

        public override async IAsyncEnumerable<object> Parse(Response response)
        {
            yield return new
            {
                Mode = (string)response.Meta["mode"]!,
                Quotes = response.Css("div.quote span.text::text").GetAll(),
                Evaluated = response.Meta.GetValueOrDefault(PlaywrightMeta.EvaluateResults) as List<object?>,
            };
            await Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Browser_rendering_sees_javascript_content()
    {
        var (_, items) = await Scrapy.CrawlAndCollectAsync(new JsQuotesSpider(sandbox.Url("/quotes/js/")), s =>
        {
            TestSettings.Quiet(s);
            s.UsePlaywright(blockResourceTypes: ["image", "font"]);
        }, TestContext.Current.CancellationToken);

        dynamic? browser = items.Cast<dynamic>().FirstOrDefault(i => i.Mode == "browser");
        if (browser is null) Assert.Skip("Playwright browser not available on this machine.");
        dynamic plain = items.Cast<dynamic>().First(i => i.Mode == "http");
        Assert.Empty((List<string>)plain.Quotes);
        Assert.Equal(10, ((List<string>)browser.Quotes).Count);
        Assert.NotNull(browser.Evaluated);
    }
}
