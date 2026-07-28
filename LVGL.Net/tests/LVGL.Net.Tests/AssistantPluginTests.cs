using System.Globalization;
using Lvgl.Assistant;
using Lvgl.Assistant.Plugins;

namespace Lvgl.Tests;

/// <summary>Time, web and prompt-template behaviour that needs no network.</summary>
public class AssistantPluginTests
{
    private readonly TimePlugin _time = new();

    [Fact]
    public void Time_is_returned_in_a_machine_readable_format()
    {
        // ISO-8601 with an offset removes the ambiguity that makes date arithmetic go wrong.
        Assert.True(DateTimeOffset.TryParse(_time.Now(), CultureInfo.InvariantCulture, DateTimeStyles.None, out _));
        Assert.True(DateTimeOffset.TryParse(_time.UtcNow(), CultureInfo.InvariantCulture, DateTimeStyles.None, out _));
    }

    [Fact]
    public void AddDays_and_DaysBetween_agree_with_each_other()
    {
        var later = _time.AddDays("2026-01-01", 45);

        Assert.Equal("2026-02-15", later);
        Assert.Equal("45", _time.DaysBetween("2026-01-01", later));
    }

    [Fact]
    public void DaysBetween_is_negative_when_the_second_date_is_earlier()
    {
        Assert.Equal("-10", _time.DaysBetween("2026-01-11", "2026-01-01"));
    }

    [Fact]
    public void An_empty_date_means_today()
    {
        Assert.Equal(DateTime.Today.ToString("yyyy-MM-dd"), _time.AddDays(string.Empty, 0));
    }

    [Fact]
    public void An_unparseable_date_is_rejected_clearly()
    {
        var error = Assert.Throws<ArgumentException>(() => _time.AddDays("last tuesday", 1));
        Assert.Contains("yyyy-MM-dd", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(45, "0m 45s")]
    [InlineData(3661, "1h 1m 1s")]
    [InlineData(90061, "1d 1h 1m")]
    public void Durations_are_formatted_readably(double seconds, string expected)
    {
        Assert.Equal(expected, _time.FormatDuration(seconds));
    }

    [Theory]
    [InlineData("ftp://example.com/x")]
    [InlineData("file:///c:/windows/system32")]
    [InlineData("not a url")]
    public async Task The_web_plugin_refuses_anything_that_is_not_http(string url)
    {
        using var plugin = new WebPlugin();
        var result = await plugin.ScrapePageAsync(url);

        Assert.True(
            result.Contains("not an absolute URL", StringComparison.Ordinal) ||
            result.Contains("Only http and https", StringComparison.Ordinal),
            $"unexpected result: {result}");
    }

    [Theory]
    [InlineData("http://localhost:8080/admin")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://10.0.0.1/")]
    [InlineData("http://192.168.1.1/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    public async Task The_web_plugin_refuses_private_and_loopback_addresses(string url)
    {
        // Without this a scraped page could point the tool at the host's own network - the classic
        // way a web-fetch tool turns into a port scanner or a cloud-metadata reader.
        using var plugin = new WebPlugin();
        var result = await plugin.ScrapePageAsync(url);

        Assert.Contains("local or private address", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Html_is_reduced_to_readable_text()
    {
        using var plugin = new WebPlugin();

        var text = plugin.HtmlToText(
            "<html><head><style>p{color:red}</style></head><body>" +
            "<script>evil()</script><h1>Title</h1><p>First&nbsp;line</p><p>Second</p></body></html>");

        Assert.Contains("Title", text, StringComparison.Ordinal);
        Assert.Contains("First line", text, StringComparison.Ordinal);
        Assert.Contains("Second", text, StringComparison.Ordinal);
        Assert.DoesNotContain("evil()", text, StringComparison.Ordinal);
        Assert.DoesNotContain("color:red", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_prompt_template_is_complete()
    {
        Assert.NotEmpty(PromptTemplates.All);

        foreach (var template in PromptTemplates.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(template.Title));
            Assert.False(string.IsNullOrWhiteSpace(template.Category));
            Assert.False(string.IsNullOrWhiteSpace(template.Description));
            Assert.True(template.Prompt.Length > 40, $"'{template.Title}' has a trivial prompt");
        }
    }

    [Fact]
    public void Template_titles_are_unique()
    {
        var titles = PromptTemplates.All.Select(t => t.Title).ToList();
        Assert.Equal(titles.Count, titles.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Categories_are_derived_from_the_templates()
    {
        Assert.NotEmpty(PromptTemplates.Categories);

        foreach (var category in PromptTemplates.Categories)
        {
            Assert.NotEmpty(PromptTemplates.InCategory(category));
        }
    }

    [Fact]
    public void The_persona_names_the_assistant_and_the_traps_that_matter()
    {
        // The persona earns its length by naming the failures a model cannot infer from the API
        // surface. If these drop out, the assistant starts producing code that compiles and misbehaves.
        var persona = PromptTemplates.DefaultPersona;

        Assert.Contains("Jack The Code Bender", persona, StringComparison.Ordinal);
        Assert.Contains("LvCoord.Percent", persona, StringComparison.Ordinal);
        Assert.Contains("LvglApplication.Post", persona, StringComparison.Ordinal);
        Assert.Contains("LvStyle", persona, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(".png", true)]
    [InlineData(".jpg", true)]
    [InlineData(".webp", true)]
    [InlineData(".pdf", false)]
    [InlineData(".cs", false)]
    public void Attachment_kind_follows_the_file_extension(string extension, bool isImage)
    {
        Assert.Equal(isImage, Lvgl.Assistant.Chat.AttachmentService.IsImageExtension(extension));
    }

    [Fact]
    public void An_unknown_extension_gets_a_safe_binary_content_type()
    {
        Assert.Equal("application/octet-stream", Lvgl.Assistant.Chat.AttachmentService.MimeTypeFor(".xyz"));
    }
}
