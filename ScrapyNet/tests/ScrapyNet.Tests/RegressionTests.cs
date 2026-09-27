using System.Text;
using ScrapyNet.AI;
using ScrapyNet.Exporters;
using Xunit;

namespace ScrapyNet.Tests;

/// <summary>Pins down bugs found while building the samples and notebooks.</summary>
public class RegressionTests
{
    [Fact]
    public void Xpath_scalars_at_document_root_see_the_whole_text()
    {
        var json = new JsonResponse("http://x/a", "{\"a\":1}");
        Assert.Equal("{\"a\":1}", json.Xpath("string(.)").Get());
        Assert.Equal("hi", new HtmlResponse("http://x/", "<p>hi</p>").Xpath("normalize-space(.)").Get());
    }

    [Fact]
    public void Json_export_of_primitives_needs_no_reflection()
    {
        using var ms = new MemoryStream();
        new JsonLinesItemExporter(ms).ExportItem(new Dictionary<string, object?>
        {
            ["d"] = 1.5m, ["n"] = double.NaN, ["t"] = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            ["g"] = Guid.Empty, ["u"] = new Uri("https://x/"), ["e"] = DayOfWeek.Monday, ["b"] = new byte[] { 1, 2 },
        });
        Assert.Equal("{\"d\":1.5,\"n\":null,\"t\":\"2026-01-02T03:04:05+00:00\",\"g\":\"00000000-0000-0000-0000-000000000000\",\"u\":\"https://x/\",\"e\":\"Monday\",\"b\":\"AQI=\"}\n",
            Encoding.UTF8.GetString(ms.ToArray()));
    }

    [Theory]
    [InlineData("bans", "ban")]
    [InlineData("banned", "ban")]
    [InlineData("proxies", "proxy")]
    [InlineData("crawlers", "crawl")]
    [InlineData("crawling", "crawl")]
    [InlineData("class", "class")]
    [InlineData("status", "status")]
    [InlineData("is", "is")]
    public void Stemmer_normalizes_word_forms(string word, string stem) => Assert.Equal(stem, TextNormalizer.Stem(word));
}
