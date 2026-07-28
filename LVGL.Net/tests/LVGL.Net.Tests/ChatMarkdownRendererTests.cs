using Lvgl.Assistant;
using Lvgl.Assistant.Chat;
using Lvgl.Assistant.Rendering;

namespace Lvgl.Tests;

/// <summary>
/// Covers the markdown-to-HTML rendering of the chat transcript.
/// </summary>
public class ChatMarkdownRendererTests
{
    private readonly ChatMarkdownRenderer _renderer = new();

    [Fact]
    public void Fenced_code_keeps_its_language_class()
    {
        var html = _renderer.RenderFragment("```csharp\nvar x = 1;\n```");

        Assert.Contains("<pre>", html, StringComparison.Ordinal);
        Assert.Contains("language-csharp", html, StringComparison.Ordinal);
        Assert.Contains("var x = 1;", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Pipe_tables_render_as_tables()
    {
        var html = _renderer.RenderFragment("""
            | Widget | Use |
            |--------|-----|
            | Label  | text |
            """);

        Assert.Contains("<table", html, StringComparison.Ordinal);
        Assert.Contains("<th", html, StringComparison.Ordinal);
        Assert.Contains("Label", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Images_and_links_render()
    {
        var html = _renderer.RenderFragment("![shot](http://example/x.png) and [a link](http://example)");

        Assert.Contains("<img", html, StringComparison.Ordinal);
        Assert.Contains("<a href=\"http://example\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Raw_html_in_model_output_is_not_passed_through()
    {
        // The transcript renders untrusted model output - which may itself be repeating content
        // scraped from a web page - so markup is escaped rather than executed.
        var html = _renderer.RenderFragment("<script>alert('x')</script>");

        Assert.DoesNotContain("<script>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Task_lists_and_other_advanced_extensions_are_enabled()
    {
        var html = _renderer.RenderFragment("- [x] done\n- [ ] todo");

        Assert.Contains("type=\"checkbox\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_renders_both_sides_of_the_conversation()
    {
        var session = new ChatSession { Provider = AssistantProvider.OpenAI };
        session.Messages.Add(new ChatMessageRecord { Author = ChatAuthor.User, Text = "hello" });
        session.Messages.Add(new ChatMessageRecord { Author = ChatAuthor.Assistant, Text = "**hi**" });

        var html = _renderer.RenderDocument(session, "Jack The Code Bender");

        Assert.Contains("<!DOCTYPE html>", html, StringComparison.Ordinal);
        Assert.Contains("class=\"msg user\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"msg assistant\"", html, StringComparison.Ordinal);
        Assert.Contains("Jack The Code Bender", html, StringComparison.Ordinal);
        Assert.Contains("<strong>hi</strong>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_session_shows_the_introduction()
    {
        var html = _renderer.RenderDocument(new ChatSession(), "Jack The Code Bender");

        Assert.Contains("class=\"empty\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_streaming_reply_is_appended_with_a_caret()
    {
        var html = _renderer.RenderDocument(new ChatSession(), "Jack", "partial answer");

        Assert.Contains("partial answer", html, StringComparison.Ordinal);
        Assert.Contains("class=\"caret\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Tool_calls_are_shown_as_chips()
    {
        var session = new ChatSession();
        session.Messages.Add(new ChatMessageRecord
        {
            Author = ChatAuthor.Assistant,
            Text = "done",
            ToolCalls = ["tavily-search", "lvgl_design-create_layout"],
        });

        var html = _renderer.RenderDocument(session, "Jack");

        Assert.Contains("tavily-search", html, StringComparison.Ordinal);
        Assert.Contains("lvgl_design-create_layout", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Attachments_render_as_thumbnails_or_file_chips()
    {
        var session = new ChatSession();
        session.Messages.Add(new ChatMessageRecord
        {
            Author = ChatAuthor.User,
            Text = "see these",
            Attachments =
            [
                new ChatAttachment("a.png", "shot.png", AttachmentKind.Image, "image/png", "http://x/a.png", 10),
                new ChatAttachment("b.pdf", "spec.pdf", AttachmentKind.Document, "application/pdf", "http://x/b.pdf", 2048),
            ],
        });

        var html = _renderer.RenderDocument(session, "Jack");

        Assert.Contains("<img src=\"http://x/a.png\"", html, StringComparison.Ordinal);
        Assert.Contains("spec.pdf", html, StringComparison.Ordinal);
        Assert.Contains("2 KB", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_name_containing_markup_is_escaped()
    {
        var session = new ChatSession();
        session.Messages.Add(new ChatMessageRecord
        {
            Author = ChatAuthor.User,
            Attachments =
            [
                new ChatAttachment("x.pdf", "<img onerror=alert(1)>.pdf", AttachmentKind.Document, "application/pdf", "http://x/x.pdf", 1),
            ],
        });

        var html = _renderer.RenderDocument(session, "Jack");

        Assert.DoesNotContain("<img onerror", html, StringComparison.Ordinal);
        Assert.Contains("&lt;img", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Error_messages_are_marked_so_they_can_be_styled_differently()
    {
        var session = new ChatSession();
        session.Messages.Add(new ChatMessageRecord { Author = ChatAuthor.Assistant, Text = "no key", IsError = true });

        Assert.Contains("msg assistant error", _renderer.RenderDocument(session, "Jack"), StringComparison.Ordinal);
    }
}
