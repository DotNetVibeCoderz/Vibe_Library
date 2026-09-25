// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace AplNet.Gallery.Infrastructure;

/// <summary>
/// Just enough C# lexing to colour the snippets: comments, strings, numbers, keywords, and names that
/// start with a capital (types and APL.Net's own API, which is what the reader is looking for).
/// </summary>
public static class SyntaxHighlighter
{
    private static readonly HashSet<string> s_keywords =
    [
        "using", "namespace", "public", "private", "internal", "static", "readonly", "struct", "class", "record",
        "void", "int", "long", "float", "double", "bool", "byte", "uint", "var", "new", "return", "for", "foreach",
        "in", "if", "else", "while", "ref", "out", "this", "null", "true", "false", "unsafe", "fixed", "lock",
        "object", "default", "sizeof", "delegate", "stackalloc", "const", "nint", "nuint", "using", "partial",
    ];

    private static readonly IBrush s_plain = Brush.Parse("#D5DAE3");
    private static readonly IBrush s_comment = Brush.Parse("#7C8699");
    private static readonly IBrush s_keyword = Brush.Parse("#8FA3FF");
    private static readonly IBrush s_type = Brush.Parse("#6FD3C1");
    private static readonly IBrush s_string = Brush.Parse("#F2B774");
    private static readonly IBrush s_number = Brush.Parse("#F29C9C");
    private static readonly IBrush s_attribute = Brush.Parse("#C9A7F5");

    public static InlineCollection Highlight(string code)
    {
        var inlines = new InlineCollection();
        int i = 0;
        while (i < code.Length)
        {
            char c = code[i];
            int start = i;

            if (c == '/' && i + 1 < code.Length && code[i + 1] == '/')
            {
                while (i < code.Length && code[i] != '\n')
                    i++;
                Add(inlines, code[start..i], s_comment);
            }
            else if (c == '"')
            {
                i++;
                while (i < code.Length && code[i] != '"')
                    i++;
                i = Math.Min(i + 1, code.Length);
                Add(inlines, code[start..i], s_string);
            }
            else if (char.IsDigit(c))
            {
                while (i < code.Length && (char.IsLetterOrDigit(code[i]) || code[i] is '.' or '_'))
                    i++;
                Add(inlines, code[start..i], s_number);
            }
            else if (char.IsLetter(c) || c == '_')
            {
                while (i < code.Length && (char.IsLetterOrDigit(code[i]) || code[i] == '_'))
                    i++;
                string word = code[start..i];
                bool attribute = start > 0 && code[start - 1] == '[' && word.StartsWith("Apl", StringComparison.Ordinal) && word.EndsWith("Body", StringComparison.Ordinal);
                IBrush brush = attribute ? s_attribute : s_keywords.Contains(word) ? s_keyword : char.IsUpper(word[0]) ? s_type : s_plain;
                Add(inlines, word, brush);
            }
            else
            {
                while (i < code.Length && !char.IsLetterOrDigit(code[i]) && code[i] is not ('"' or '_') &&
                       !(code[i] == '/' && i + 1 < code.Length && code[i + 1] == '/'))
                    i++;
                Add(inlines, code[start..i], s_plain);
            }
        }

        return inlines;
    }

    private static void Add(InlineCollection inlines, string text, IBrush brush) =>
        inlines.Add(new Run(text) { Foreground = brush });
}
