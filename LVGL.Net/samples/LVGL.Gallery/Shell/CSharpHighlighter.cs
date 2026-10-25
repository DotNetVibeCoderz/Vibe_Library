using System.Text;
using Lvgl.Drawing;

namespace Lvgl.Gallery.Shell;

/// <summary>
/// Just enough C# lexing to colour a demo's source: comments, strings, numbers, keywords and type
/// names. Output is LVGL's in-line recolour syntax (<c>#RRGGBB text#</c>), one line at a time.
/// </summary>
internal static class CSharpHighlighter
{
    public static readonly LvColor Plain = Palette.Ink;
    public static readonly LvColor Keyword = LvColor.FromRgb(0x2F4FD0u);
    public static readonly LvColor TypeName = LvColor.FromRgb(0x0B7A61u);
    public static readonly LvColor StringLiteral = LvColor.FromRgb(0xB0452Au);
    public static readonly LvColor Number = LvColor.FromRgb(0x8E3BB8u);
    public static readonly LvColor Comment = LvColor.FromRgb(0x8A95A3u);

    private static readonly HashSet<string> Keywords =
    [
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "class", "const", "continue",
        "default", "do", "double", "else", "enum", "false", "finally", "float", "for", "foreach", "get", "if",
        "in", "init", "int", "internal", "is", "long", "namespace", "new", "null", "object", "out", "override",
        "private", "protected", "public", "readonly", "record", "ref", "return", "sealed", "set", "static",
        "string", "struct", "switch", "this", "throw", "true", "try", "uint", "using", "var", "void", "when",
        "while", "with", "yield", "async", "await", "ushort", "short", "sbyte", "ulong", "nint", "params",
        "partial", "virtual", "where", "and", "or", "not", "value",
    ];

    /// <summary>
    /// True when a line can be recoloured. LVGL's recolour syntax has no escape for a literal
    /// <c>#</c>, so such lines are shown plain instead.
    /// </summary>
    public static bool CanColour(string line) => !line.Contains('#');

    public static string Colour(string line)
    {
        var output = new StringBuilder(line.Length * 2);
        var i = 0;

        while (i < line.Length)
        {
            var c = line[i];

            if (c == '/' && i + 1 < line.Length && line[i + 1] == '/')
            {
                Emit(output, line[i..], Comment);
                break;
            }

            if (c == '"' || (c is '$' or '@' && i + 1 < line.Length && line[i + 1] == '"'))
            {
                var end = i + (c == '"' ? 1 : 2);
                while (end < line.Length && line[end] != '"')
                {
                    if (line[end] == '\\' && c != '@') end++;
                    end++;
                }

                end = Math.Min(end + 1, line.Length);
                Emit(output, line[i..end], StringLiteral);
                i = end;
                continue;
            }

            if (c == '\'' && i + 2 < line.Length)
            {
                var end = line.IndexOf('\'', i + (line[i + 1] == '\\' ? 3 : 2));
                if (end > i)
                {
                    Emit(output, line[i..(end + 1)], StringLiteral);
                    i = end + 1;
                    continue;
                }
            }

            if (char.IsDigit(c))
            {
                var end = i;
                while (end < line.Length && (char.IsLetterOrDigit(line[end]) || line[end] is '.' or '_')) end++;
                Emit(output, line[i..end], Number);
                i = end;
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                var end = i;
                while (end < line.Length && (char.IsLetterOrDigit(line[end]) || line[end] == '_')) end++;
                var word = line[i..end];

                if (Keywords.Contains(word)) Emit(output, word, Keyword);
                else if (char.IsUpper(word[0]) && IsTypeLike(line, i, end)) Emit(output, word, TypeName);
                else output.Append(word);

                i = end;
                continue;
            }

            output.Append(c);
            i++;
        }

        return output.ToString();
    }

    // A capitalised word followed by an identifier, '<' or '(' after "new" reads as a type. Method
    // calls (Word followed by '(') stay plain, which keeps the colouring calm.
    private static bool IsTypeLike(string line, int start, int end)
    {
        if (start >= 4 && line.AsSpan(start - 4, 4).SequenceEqual("new ")) return true;
        if (end < line.Length && line[end] == '<') return true;
        if (end < line.Length && line[end] == '.') return true;   // Palette.Ink, LvAlign.Center
        if (end + 1 < line.Length && line[end] == ' ' && (char.IsLetter(line[end + 1]) || line[end + 1] == '_'))
        {
            var rest = line.AsSpan(end + 1);
            return !rest.StartsWith("=>") && !rest.StartsWith("and ") && !rest.StartsWith("or ");
        }

        return end < line.Length && line[end] == '?';
    }

    private static void Emit(StringBuilder output, string text, LvColor color)
    {
        // A command spans to the next '#'; trailing spaces inside it are harmless.
        output.Append('#').Append(color.Rgb.ToString("X6")).Append(' ').Append(text).Append('#');
    }
}
