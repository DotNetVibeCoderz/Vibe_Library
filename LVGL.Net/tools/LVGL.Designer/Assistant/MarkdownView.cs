using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MdInline = Markdig.Syntax.Inlines.Inline;

namespace Lvgl.Designer.Assistant;

/// <summary>
/// Renders Markdown into native Avalonia controls: headings, paragraphs, lists, quotes, tables
/// and fenced code with a copy button.
/// </summary>
/// <remarks>
/// Native rather than HTML in a web view: there is no web view in Avalonia's box, and native text
/// takes the designer's own fonts, colours and selection behaviour for free. It covers what a model
/// actually writes; raw HTML is shown as text rather than interpreted.
/// </remarks>
public static class MarkdownView
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseEmphasisExtras()
        .UseAutoLinks()
        .UseTaskLists()
        .Build();

    /// <summary>Renders <paramref name="markdown"/> into a vertical stack.</summary>
    public static Control Render(string markdown)
    {
        var panel = new StackPanel { Spacing = 8 };
        var document = Markdown.Parse(markdown ?? string.Empty, Pipeline);

        foreach (var block in document) panel.Children.Add(RenderBlock(block));
        return panel;
    }

    private static Control RenderBlock(Block block) => block switch
    {
        HeadingBlock heading => Heading(heading),
        ParagraphBlock paragraph => Text(paragraph.Inline),
        FencedCodeBlock fenced => Code(fenced.Lines.ToString(), fenced.Info),
        CodeBlock code => Code(code.Lines.ToString(), null),
        ListBlock list => List(list),
        QuoteBlock quote => Quote(quote),
        Table table => TableView(table),
        ThematicBreakBlock => new Border { Height = 1, Background = Brush("Trace"), Margin = new Thickness(0, 4) },
        HtmlBlock html => Plain(html.Lines.ToString()),
        ContainerBlock container => Stack(container),
        _ => Plain(string.Empty),
    };

    private static Control Stack(ContainerBlock container)
    {
        var panel = new StackPanel { Spacing = 6 };
        foreach (var child in container) panel.Children.Add(RenderBlock(child));
        return panel;
    }

    private static Control Heading(HeadingBlock heading)
    {
        var text = Text(heading.Inline);
        text.FontFamily = Resource<FontFamily>("HeadFont") ?? FontFamily.Default;
        text.FontWeight = FontWeight.SemiBold;
        text.FontSize = heading.Level switch { 1 => 19, 2 => 16.5, _ => 14.5 };
        text.Margin = new Thickness(0, heading.Level <= 2 ? 6 : 2, 0, 0);
        return text;
    }

    private static SelectableTextBlock Plain(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };

    private static SelectableTextBlock Text(ContainerInline? inline)
    {
        var block = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 19 };
        if (inline is not null) AppendInlines(block.Inlines!, inline, bold: false, italic: false);
        return block;
    }

    private static void AppendInlines(InlineCollection target, ContainerInline container, bool bold, bool italic)
    {
        for (MdInline? inline = container.FirstChild; inline is not null; inline = inline.NextSibling)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    target.Add(Run(literal.Content.ToString(), bold, italic));
                    break;

                case EmphasisInline emphasis:
                    var strong = emphasis.DelimiterCount >= 2;
                    AppendInlines(target, emphasis, bold || strong, italic || !strong);
                    break;

                case CodeInline code:
                    target.Add(new Run(code.Content)
                    {
                        FontFamily = Resource<FontFamily>("MonoFont") ?? FontFamily.Default,
                        FontSize = 11.5,
                        Foreground = Brush("CopperHot"),
                        Background = Brush("Mask"),
                    });
                    break;

                case LinkInline link:
                    var linkRun = new Span { TextDecorations = TextDecorations.Underline, Foreground = Brush("Live") };
                    if (link.FirstChild is null) linkRun.Inlines.Add(new Run(link.Url));
                    else AppendInlines(linkRun.Inlines, link, bold, italic);
                    target.Add(linkRun);
                    break;

                case AutolinkInline autolink:
                    target.Add(new Run(autolink.Url) { TextDecorations = TextDecorations.Underline, Foreground = Brush("Live") });
                    break;

                case LineBreakInline:
                    target.Add(new LineBreak());
                    break;

                case HtmlInline html:
                    target.Add(Run(html.Tag, bold, italic));
                    break;

                case ContainerInline nested:
                    AppendInlines(target, nested, bold, italic);
                    break;
            }
        }
    }

    private static Run Run(string text, bool bold, bool italic) => new(text)
    {
        FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
        FontStyle = italic ? FontStyle.Italic : FontStyle.Normal,
    };

    private static Control Code(string code, string? language)
    {
        code = code.TrimEnd('\r', '\n');

        var text = new SelectableTextBlock
        {
            Text = code,
            FontFamily = Resource<FontFamily>("MonoFont") ?? FontFamily.Default,
            FontSize = 11,
            TextWrapping = TextWrapping.NoWrap,
        };

        var copy = new Button { Content = "Copy", Padding = new Thickness(7, 1), FontSize = 11 };
        copy.Classes.Add("ghost");
        copy.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(copy)?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(code);
                copy.Content = "Copied";
            }
        };

        var tag = new TextBlock { Text = string.IsNullOrWhiteSpace(language) ? "code" : language.Trim(), VerticalAlignment = VerticalAlignment.Center };
        tag.Classes.Add("designator");

        var head = new DockPanel { Margin = new Thickness(10, 4, 4, 2) };
        DockPanel.SetDock(copy, Dock.Right);
        head.Children.Add(copy);
        head.Children.Add(tag);

        var body = new ScrollViewer
        {
            Content = text,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Padding = new Thickness(10, 2, 10, 10),
        };

        var layout = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        layout.Children.Add(head);
        layout.Children.Add(body);

        return new Border
        {
            Background = Brush("Mask"),
            BorderBrush = Brush("Trace"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = layout,
        };
    }

    private static Control List(ListBlock list)
    {
        var panel = new StackPanel { Spacing = 4 };
        var number = int.TryParse(list.OrderedStart, out var start) ? start : 1;

        foreach (var item in list.OfType<ListItemBlock>())
        {
            var marker = new TextBlock
            {
                Text = list.IsOrdered ? $"{number++}." : "•",
                Width = 20,
                Foreground = Brush("Copper"),
                VerticalAlignment = VerticalAlignment.Top,
            };

            var content = new StackPanel { Spacing = 4 };
            foreach (var child in item) content.Children.Add(RenderBlock(child));

            var row = new DockPanel();
            DockPanel.SetDock(marker, Dock.Left);
            row.Children.Add(marker);
            row.Children.Add(content);
            panel.Children.Add(row);
        }

        return panel;
    }

    private static Control Quote(QuoteBlock quote) => new Border
    {
        BorderBrush = Brush("Copper"),
        BorderThickness = new Thickness(2, 0, 0, 0),
        Padding = new Thickness(10, 2, 0, 2),
        Child = Stack(quote),
        Opacity = 0.85,
    };

    private static Control TableView(Table table)
    {
        var rows = table.OfType<TableRow>().ToList();
        var columns = rows.Count == 0 ? 0 : rows.Max(r => r.Count);

        var grid = new Grid();
        for (var c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        for (var r = 0; r < rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            for (var c = 0; c < rows[r].Count; c++)
            {
                var cell = (TableCell)rows[r][c];
                var content = new StackPanel();
                foreach (var block in cell) content.Children.Add(RenderBlock(block));

                var border = new Border
                {
                    Padding = new Thickness(8, 4),
                    BorderBrush = Brush("Trace"),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Background = rows[r].IsHeader ? Brush("Raised") : Brushes.Transparent,
                    Child = content,
                };

                if (rows[r].IsHeader) content.Children.OfType<SelectableTextBlock>().ToList().ForEach(t => t.FontWeight = FontWeight.SemiBold);

                Grid.SetRow(border, r);
                Grid.SetColumn(border, c);
                grid.Children.Add(border);
            }
        }

        return new ScrollViewer
        {
            Content = grid,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
    }

    private static IBrush Brush(string key) => Resource<IBrush>(key) ?? Brushes.Gray;

    private static T? Resource<T>(string key) where T : class =>
        Application.Current is { } app && app.TryFindResource(key, out var value) ? value as T : null;
}
