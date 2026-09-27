using System.Text;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace ScrapyGallery.Controls;

/// <summary>
/// A small C# tokenizer that turns source into coloured runs for a <c>SelectableTextBlock</c>:
/// keywords, types, strings, comments, numbers. Colours are tuned for the ink code panel.
/// </summary>
public static class CodeHighlighter
{
    private static readonly HashSet<string> Keywords =
    [
        "abstract", "as", "async", "await", "base", "bool", "break", "case", "catch", "class", "const", "continue", "decimal", "default",
        "do", "double", "else", "enum", "false", "finally", "for", "foreach", "if", "in", "init", "int", "interface", "internal", "is",
        "long", "namespace", "new", "null", "object", "out", "override", "private", "protected", "public", "readonly", "record", "return",
        "sealed", "static", "string", "switch", "this", "throw", "true", "try", "using", "var", "virtual", "void", "when", "while", "yield",
        "get", "set", "with", "not", "and", "or",
    ];

    private static readonly IBrush Plain = new SolidColorBrush(Color.Parse("#DCE2EC"));
    private static readonly IBrush Keyword = new SolidColorBrush(Color.Parse("#F2B84B"));
    private static readonly IBrush Type = new SolidColorBrush(Color.Parse("#8FD3C1"));
    private static readonly IBrush StringLiteral = new SolidColorBrush(Color.Parse("#B7C98A"));
    private static readonly IBrush Comment = new SolidColorBrush(Color.Parse("#7F8AA3"));
    private static readonly IBrush Number = new SolidColorBrush(Color.Parse("#E59A86"));

    public static InlineCollection Highlight(string code)
    {
        var inlines = new InlineCollection();
        var i = 0;
        var plain = new StringBuilder();

        void FlushPlain()
        {
            if (plain.Length == 0) return;
            inlines.Add(new Run(plain.ToString()) { Foreground = Plain });
            plain.Clear();
        }

        void Add(string text, IBrush brush, FontStyle style = FontStyle.Normal)
        {
            FlushPlain();
            inlines.Add(new Run(text) { Foreground = brush, FontStyle = style });
        }

        while (i < code.Length)
        {
            var c = code[i];
            if (c == '/' && i + 1 < code.Length && code[i + 1] == '/')
            {
                var end = code.IndexOf('\n', i);
                if (end < 0) end = code.Length;
                Add(code[i..end], Comment, FontStyle.Italic);
                i = end;
            }
            else if (c == '"' || (c is '$' or '@' && i + 1 < code.Length && code[i + 1] == '"') || (c == '$' && i + 2 < code.Length && code[i + 1] == '@'))
            {
                var start = i;
                while (i < code.Length && code[i] != '"') i++;
                var triple = i + 2 < code.Length && code[i + 1] == '"' && code[i + 2] == '"';
                if (triple)
                {
                    var close = code.IndexOf("\"\"\"", i + 3, StringComparison.Ordinal);
                    i = close < 0 ? code.Length : close + 3;
                }
                else
                {
                    i++;
                    var verbatim = code[start] == '@' || (start + 1 < code.Length && code[start + 1] == '@');
                    while (i < code.Length && code[i] != '"' && code[i] != '\n')
                    {
                        if (code[i] == '\\' && !verbatim) i++;
                        i++;
                    }
                    i = Math.Min(code.Length, i + 1);
                }
                Add(code[start..i], StringLiteral);
            }
            else if (c == '\'' && i + 2 < code.Length)
            {
                var end = code.IndexOf('\'', i + 1);
                if (end > i && end - i <= 4)
                {
                    Add(code[i..(end + 1)], StringLiteral);
                    i = end + 1;
                }
                else
                {
                    plain.Append(c);
                    i++;
                }
            }
            else if (char.IsDigit(c) && (i == 0 || !char.IsLetterOrDigit(code[i - 1])))
            {
                var start = i;
                while (i < code.Length && (char.IsLetterOrDigit(code[i]) || code[i] is '.' or '_') && !(code[i] == '.' && i + 1 < code.Length && !char.IsDigit(code[i + 1]))) i++;
                Add(code[start..i], Number);
            }
            else if (char.IsLetter(c) || c == '_')
            {
                var start = i;
                while (i < code.Length && (char.IsLetterOrDigit(code[i]) || code[i] == '_')) i++;
                var word = code[start..i];
                if (Keywords.Contains(word)) Add(word, Keyword);
                else if (char.IsUpper(word[0])) Add(word, Type);
                else plain.Append(word);
            }
            else
            {
                plain.Append(c);
                i++;
            }
        }
        FlushPlain();
        return inlines;
    }
}
