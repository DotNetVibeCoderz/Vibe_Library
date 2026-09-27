using Xunit;

namespace ScrapyNet.Tests;

public class SelectorTests
{
    private const string Html = """
        <html><head><title>Test page</title><base href="https://example.com/sub/"></head>
        <body>
          <div class="quote first" id="q1"><span class="text">Hello &amp; welcome</span> <small class="author">Ada</small>
            <a href="/author/ada">about</a> <a class="tag" href="t/life">life</a></div>
          <div class="quote"><span class="text">Second <b>bold</b> quote</span><small class="author">Rumi</small></div>
          <p class="price">Price: £1,299.50</p>
          <ul><li data-n="1">one</li><li data-n="2">two</li></ul>
        </body></html>
        """;

    private static readonly Selector Doc = Selector.FromHtml(Html);

    [Fact]
    public void Css_text_pseudo_element_returns_direct_text_nodes()
    {
        Assert.Equal("Hello & welcome", Doc.Css("div.quote span.text::text").Get());
        Assert.Equal(["Second ", " quote"], Doc.Css("div.quote:nth-of-type(2) span.text::text").GetAll());
    }

    [Fact]
    public void Css_descendant_text_includes_nested_elements()
    {
        Assert.Equal(["Second ", "bold", " quote"], Doc.Css("div.quote:nth-of-type(2) span.text ::text").GetAll());
    }

    [Fact]
    public void Css_attr_pseudo_element()
    {
        Assert.Equal(["/author/ada", "t/life"], Doc.Css("a::attr(href)").GetAll());
        Assert.Equal("q1", Doc.Css("div.quote::attr(id)").Get());
    }

    [Fact]
    public void Css_on_element_includes_itself_like_parsel()
    {
        var quote = Doc.Css("div.quote")[0];
        Assert.Single(quote.Css("div.quote"));
        Assert.Equal("Ada", quote.Css("small.author::text").Get());
    }

    [Fact]
    public void Xpath_text_attributes_and_scalars()
    {
        Assert.Equal("Ada", Doc.Xpath("//small[@class='author']/text()").Get());
        Assert.Equal(["1", "2"], Doc.Xpath("//li/@data-n").GetAll());
        Assert.Equal("2", Doc.Xpath("count(//div[@class='quote' or contains(@class,'quote')])").Get());
        Assert.Equal("true", Doc.Xpath("boolean(//ul)").Get());
    }

    [Fact]
    public void Xpath_relative_to_selection()
    {
        var first = Doc.Xpath("//div[contains(@class,'quote')]")[0];
        Assert.Equal("Ada", first.Xpath("./small/text()").Get());
        Assert.Equal("Rumi", first.Xpath("following-sibling::div/small/text()").Get());
    }

    [Fact]
    public void Xpath_has_class_and_exslt_regex()
    {
        Assert.Equal("q1", Doc.Xpath("//div[has-class('quote', 'first')]/@id").Get());
        Assert.Equal(2, Doc.Xpath("//li[re:test(text(), '^t|^o')]").Count);
        Assert.Equal("two", Doc.Xpath("//li[re:test(@data-n, '2')]/text()").Get());
    }

    [Fact]
    public void Xpath_variables()
    {
        var vars = new Dictionary<string, object> { ["n"] = "2" };
        Assert.Equal("two", Doc.Xpath("//li[@data-n=$n]/text()", vars).Get());
    }

    [Fact]
    public void Regex_extraction_follows_scrapy_rules()
    {
        Assert.Equal("1,299.50", Doc.Css("p.price::text").ReFirst(@"£([\d,\.]+)"));
        Assert.Equal(["Price", "1", "299", "50"], Doc.Css("p.price::text").Re(@"\w+"));
        Assert.Equal("299", Doc.Css("p.price::text").ReFirst(@",(?<extract>\d+)\."));
    }

    [Fact]
    public void Get_serializes_elements_and_default_value()
    {
        Assert.StartsWith("<small class=\"author\">Ada</small>", Doc.Css("small.author").Get());
        Assert.Null(Doc.Css("h6").Get());
        Assert.Equal("none", Doc.Css("h6::text").Get("none"));
    }

    [Fact]
    public void Attrib_and_text_helpers()
    {
        Assert.Equal("quote first", Doc.Css("div#q1").Attrib["class"]);
        Assert.Equal("Second bold quote", Doc.Css("div.quote span.text")[1].GetText());
    }

    [Fact]
    public void Xml_selectors_ignore_namespaces_by_default()
    {
        var xml = Selector.FromXml("""<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9"><url><loc>http://a/1</loc></url><url><loc>http://a/2</loc></url></urlset>""");
        Assert.Equal(["http://a/1", "http://a/2"], xml.Xpath("//url/loc/text()").GetAll());
        Assert.Equal(2, xml.Css("url loc").Count);
    }

    [Fact]
    public void Invalid_queries_throw_argument_exceptions()
    {
        Assert.Throws<ArgumentException>(() => Doc.Xpath("//div[").GetAll());
        Assert.Throws<ArgumentException>(() => Doc.Css("a::before").GetAll());
    }

    [Fact]
    public void Response_resolves_relative_urls_against_base_tag()
    {
        var response = new HtmlResponse("https://example.com/page", Html);
        Assert.Equal("https://example.com/sub/t/life", response.UrlJoin("t/life"));
        var follow = response.Follow(response.Css("a.tag")[0]);
        Assert.Equal("https://example.com/sub/t/life", follow.Url);
    }
}
