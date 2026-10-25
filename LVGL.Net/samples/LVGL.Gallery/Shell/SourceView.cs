using System.Text;
using Lvgl.Drawing;
using Lvgl.Widgets;

namespace Lvgl.Gallery.Shell;

/// <summary>
/// Shows a demo's own C# file: a line-number gutter and the code, coloured, in a scrolling sheet.
/// </summary>
/// <remarks>
/// Consecutive lines go into one label to keep the object count low on a Pi. A line containing
/// <c>#</c> cannot be recoloured (LVGL has no escape for it) and gets a plain label of its own.
/// </remarks>
internal static class SourceView
{
    public static string Load(string fileName)
    {
        using var stream = typeof(SourceView).Assembly.GetManifestResourceStream("source/" + fileName);
        if (stream is null) return $"// The source of {fileName} was not embedded in this build.";

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\t", "    ", StringComparison.Ordinal).TrimEnd();
    }

    public static void Build(LvObject parent, int width, int height, string fileName, Typeface fonts)
    {
        const int fontSize = 12;
        var mono = fonts[Face.Mono, fontSize];
        var lines = Load(fileName).Split('\n');

        var scroller = new LvPanel(parent, width, height);
        scroller.SetBackgroundColor(Palette.Card);
        scroller.SetBorderWidth(1);
        scroller.SetBorderColor(Palette.Rule);
        scroller.SetRadius(12);
        scroller.SetPadding(0);
        scroller.SetScrollbarMode(LvScrollbarMode.Auto);

        // Header: the file name, like an editor tab.
        var tab = new LvLabel(scroller, $"{fileName}  ·  {lines.Length} lines");
        tab.SetFont(fonts[Face.Mono, 11]);
        tab.SetTextColor(Palette.InkMuted);
        tab.SetPosition(16, 12);

        var gutter = new LvLabel(scroller, string.Join('\n', Enumerable.Range(1, lines.Length)));
        gutter.SetFont(mono);
        gutter.SetTextColor(Palette.InkFaint);
        gutter.SetTextAlign(LvTextAlign.Right);
        gutter.SetSize(36, LvCoord.SizeContent);
        gutter.SetPosition(4, 40);

        var code = new LvPanel(scroller);
        code.SetSize(LvCoord.SizeContent, LvCoord.SizeContent);
        code.SetPosition(54, 40);
        code.SetBackgroundOpacity(0);
        code.SetBorderWidth(0);
        code.SetPadding(0);
        code.SetFlexFlow(LvFlexFlow.Column);
        code.SetGap(0, 0);
        code.IsScrollable = false;

        var run = new StringBuilder();
        foreach (var line in lines)
        {
            if (CSharpHighlighter.CanColour(line))
            {
                if (run.Length > 0) run.Append('\n');
                run.Append(CSharpHighlighter.Colour(line));
                continue;
            }

            Flush(code, run, mono, recolor: true);
            run.Append(line);
            Flush(code, run, mono, recolor: false);
        }

        Flush(code, run, mono, recolor: true);

        // Room under the last line so it can be scrolled clear of the edge.
        var spacer = new LvPanel(scroller, 1, 24);
        spacer.SetBackgroundOpacity(0);
        spacer.SetBorderWidth(0);
        spacer.SetPosition(0, 40 + lines.Length * (mono.Size + 4));
    }

    private static void Flush(LvObject code, StringBuilder run, Drawing.LvFont font, bool recolor)
    {
        if (run.Length == 0) return;

        var label = new LvLabel(code);
        label.SetFont(font);
        label.SetTextColor(CSharpHighlighter.Plain);
        label.Recolor = recolor;
        label.Text = run.ToString();
        run.Clear();
    }
}
