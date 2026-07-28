using System.Net;
using System.Text;
using Lvgl.Assistant.Chat;
using Markdig;

namespace Lvgl.Assistant.Rendering;

/// <summary>
/// Renders a chat transcript to a self-contained HTML document.
/// </summary>
/// <remarks>
/// <para>
/// The transcript is rendered as HTML and shown in a WebView2 rather than laid out with WPF
/// controls. Tables, fenced code, images, audio and video all come free that way, and they are the
/// things a model actually emits; reproducing them with WPF primitives would be a large amount of
/// work for a worse result.
/// </para>
/// <para>
/// Markdig is configured with the advanced extensions, which is what gives pipe tables, fenced
/// code with language classes, task lists, autolinks and footnotes. Raw HTML in the model's
/// output is <b>not</b> passed through: <c>DisableHtml()</c> escapes it, because the transcript
/// renders untrusted output that may itself be repeating content scraped from a web page.
/// </para>
/// </remarks>
public sealed class ChatMarkdownRenderer
{
    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseEmojiAndSmiley()
        .UseSoftlineBreakAsHardlineBreak()
        .DisableHtml()          // model output is never trusted as markup
        .Build();

    /// <summary>Renders one message body to an HTML fragment.</summary>
    public string RenderFragment(string markdown) =>
        string.IsNullOrWhiteSpace(markdown)
            ? string.Empty
            : Markdown.ToHtml(markdown, _pipeline);

    /// <summary>
    /// Renders a whole conversation to a standalone HTML page, styled to match the designer.
    /// </summary>
    /// <param name="session">Conversation to render.</param>
    /// <param name="assistantName">Name shown on assistant messages.</param>
    /// <param name="pendingText">
    /// A reply still being streamed, appended as a final assistant bubble.
    /// </param>
    public string RenderDocument(ChatSession session, string assistantName, string? pendingText = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        var body = new StringBuilder();

        foreach (var message in session.Messages)
        {
            body.Append(RenderMessage(message, assistantName));
        }

        if (!string.IsNullOrEmpty(pendingText))
        {
            body.Append(RenderBubble(
                ChatAuthor.Assistant,
                assistantName,
                RenderFragment(pendingText),
                attachments: [],
                toolCalls: [],
                isError: false,
                isPending: true));
        }

        if (session.Messages.Count == 0 && string.IsNullOrEmpty(pendingText))
        {
            body.Append(EmptyState(assistantName));
        }

        return Page(body.ToString());
    }

    private string RenderMessage(ChatMessageRecord message, string assistantName)
    {
        var author = message.Author switch
        {
            ChatAuthor.User => "You",
            ChatAuthor.Assistant => assistantName,
            _ => "System",
        };

        return RenderBubble(
            message.Author,
            author,
            RenderFragment(message.Text),
            message.Attachments,
            message.ToolCalls,
            message.IsError,
            isPending: false);
    }

    private static string RenderBubble(
        ChatAuthor author,
        string authorName,
        string html,
        IReadOnlyList<ChatAttachment> attachments,
        IReadOnlyList<string> toolCalls,
        bool isError,
        bool isPending)
    {
        var role = author.ToString().ToLowerInvariant();
        var classes = $"msg {role}" + (isError ? " error" : string.Empty) + (isPending ? " pending" : string.Empty);

        var builder = new StringBuilder();
        builder.Append($"<div class=\"{classes}\">");
        builder.Append($"<div class=\"who\">{Escape(authorName)}</div>");

        if (toolCalls.Count > 0)
        {
            builder.Append("<div class=\"tools\">");
            foreach (var tool in toolCalls)
            {
                builder.Append($"<span class=\"tool\">{Escape(tool)}</span>");
            }
            builder.Append("</div>");
        }

        builder.Append("<div class=\"body\">").Append(html);

        if (isPending) builder.Append("<span class=\"caret\"></span>");

        builder.Append("</div>");

        if (attachments.Count > 0)
        {
            builder.Append("<div class=\"attachments\">");
            foreach (var attachment in attachments)
            {
                builder.Append(RenderAttachment(attachment));
            }
            builder.Append("</div>");
        }

        builder.Append("</div>");
        return builder.ToString();
    }

    private static string RenderAttachment(ChatAttachment attachment)
    {
        var url = Escape(attachment.Url);
        var name = Escape(attachment.FileName);

        return attachment.Kind == AttachmentKind.Image
            ? $"<a class=\"thumb\" href=\"{url}\" target=\"_blank\"><img src=\"{url}\" alt=\"{name}\" /></a>"
            : $"<a class=\"file\" href=\"{url}\" target=\"_blank\">{name} " +
              $"<span class=\"size\">{FormatSize(attachment.SizeBytes)}</span></a>";
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F0} KB",
        _ => $"{bytes / (1024.0 * 1024.0):F1} MB",
    };

    private static string EmptyState(string assistantName) =>
        $"""
         <div class="empty">
           <h2>{Escape(assistantName)}</h2>
           <p>Design assistant for LVGL.Net. Ask for a screen layout, the C# behind it, or help
              with a problem. Attach a screenshot to have it reproduced as a layout.</p>
           <p class="hint">Try a template from the Prompts menu to see what it can do.</p>
         </div>
         """;

    /// <summary>
    /// Escapes text for HTML. Markdig already escapes the message body; this covers the pieces
    /// built by hand around it - names, file names, URLs.
    /// </summary>
    private static string Escape(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);

    private static string Page(string body) =>
        $$"""
          <!DOCTYPE html>
          <html>
          <head>
          <meta charset="utf-8" />
          <meta name="viewport" content="width=device-width, initial-scale=1" />
          <style>
            :root {
              --bg: #11151c; --surface: #1b1f27; --surface-2: #232a35; --line: #323b49;
              --ink: #e7edf5; --muted: #95a3b5; --accent: #38bdf8; --danger: #f87171;
            }
            * { box-sizing: border-box; }
            body {
              margin: 0; padding: 16px 18px 32px;
              background: var(--bg); color: var(--ink);
              font: 14px/1.65 "Segoe UI", system-ui, sans-serif;
              overflow-wrap: anywhere;
            }
            .msg { margin: 0 0 18px; }
            .who { font-size: 11px; letter-spacing: .06em; text-transform: uppercase; color: var(--muted); margin-bottom: 6px; }
            .msg.user .body { background: var(--surface-2); }
            .body {
              background: var(--surface); border: 1px solid var(--line); border-radius: 12px;
              padding: 12px 14px;
            }
            .msg.error .body { border-color: var(--danger); }
            .msg.system .body { background: transparent; border-style: dashed; color: var(--muted); }

            .body > :first-child { margin-top: 0; }
            .body > :last-child { margin-bottom: 0; }

            h1, h2, h3, h4 { line-height: 1.3; margin: 1.2em 0 .5em; }
            h1 { font-size: 1.4em; } h2 { font-size: 1.25em; } h3 { font-size: 1.1em; }
            a { color: var(--accent); }
            blockquote { margin: .8em 0; padding: .1em 1em; border-left: 3px solid var(--line); color: var(--muted); }
            hr { border: 0; border-top: 1px solid var(--line); margin: 1.2em 0; }

            code {
              font-family: Consolas, "Cascadia Mono", monospace; font-size: .92em;
              background: #0d1117; border: 1px solid var(--line); border-radius: 4px; padding: .1em .35em;
            }
            pre {
              background: #0d1117; border: 1px solid var(--line); border-radius: 10px;
              padding: 12px 14px; overflow-x: auto;
            }
            pre code { background: none; border: 0; padding: 0; }

            /* Wide content scrolls inside its own box rather than stretching the page. */
            .table-scroll { overflow-x: auto; }
            table { border-collapse: collapse; width: 100%; margin: .8em 0; font-size: .95em; }
            th, td { border: 1px solid var(--line); padding: 6px 10px; text-align: left; vertical-align: top; }
            th { background: var(--surface-2); }
            tr:nth-child(even) td { background: rgba(255,255,255,.02); }

            img, video { max-width: 100%; border-radius: 8px; }
            audio { width: 100%; }

            ul, ol { padding-left: 1.4em; }
            li { margin: .25em 0; }
            li input[type=checkbox] { margin-right: .4em; }

            .tools { margin-bottom: 6px; display: flex; flex-wrap: wrap; gap: 6px; }
            .tool {
              font-size: 11px; color: var(--accent); border: 1px solid var(--line);
              background: #0d1117; border-radius: 999px; padding: 2px 9px;
            }

            .attachments { margin-top: 8px; display: flex; flex-wrap: wrap; gap: 8px; }
            .thumb img { max-height: 160px; border: 1px solid var(--line); }
            .file {
              display: inline-block; text-decoration: none; color: var(--ink);
              background: var(--surface-2); border: 1px solid var(--line);
              border-radius: 8px; padding: 6px 10px; font-size: 12px;
            }
            .file .size { color: var(--muted); }

            .caret {
              display: inline-block; width: 7px; height: 1.05em; margin-left: 2px;
              background: var(--accent); vertical-align: text-bottom;
              animation: blink 1s steps(2, start) infinite;
            }
            @keyframes blink { to { visibility: hidden; } }

            .empty { color: var(--muted); text-align: center; padding: 48px 24px; }
            .empty h2 { color: var(--ink); }
            .empty .hint { font-size: 12px; }
          </style>
          </head>
          <body>
          {{body}}
          <script>
            // Tables are wrapped after render so wide ones scroll inside the bubble instead of
            // forcing the whole transcript sideways.
            for (const table of document.querySelectorAll('table')) {
              if (table.parentElement.classList.contains('table-scroll')) continue;
              const wrap = document.createElement('div');
              wrap.className = 'table-scroll';
              table.replaceWith(wrap);
              wrap.appendChild(table);
            }
            window.scrollTo(0, document.body.scrollHeight);
          </script>
          </body>
          </html>
          """;
}
