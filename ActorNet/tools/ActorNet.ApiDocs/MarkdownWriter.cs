// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Text;

namespace ActorNet.ApiDocs;

/// <summary>Writes the model out as one markdown file per namespace, plus an index.</summary>
/// <remarks>
/// One file per namespace rather than one per type. A type here is usually a page's worth of text
/// at most, and a directory of two hundred files nobody can scan is worse than a dozen that can be
/// searched with a browser's own find.
/// </remarks>
public static class MarkdownWriter
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Writes everything, returning the files it wrote.</summary>
    public static IReadOnlyList<string> Write(ApiModel api, string directory)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentException.ThrowIfNullOrEmpty(directory);

        Directory.CreateDirectory(directory);

        var written = new List<string>();

        foreach (var space in api.Namespaces)
        {
            var path = Path.Combine(directory, FileName(space));
            File.WriteAllText(path, Namespace(space, api.InNamespace(space)), Utf8);
            written.Add(path);
        }

        var index = Path.Combine(directory, "README.md");
        File.WriteAllText(index, Index(api), Utf8);
        written.Add(index);

        return written;
    }

    private static string FileName(string space) => $"{space.Replace('.', '-').ToLowerInvariant()}.md";

    private static string Index(ApiModel api)
    {
        var page = new StringBuilder();

        page.AppendLine("# API reference");
        page.AppendLine();
        page.AppendLine("Generated from the XML the compiler writes next to each assembly. It answers *what is on");
        page.AppendLine("this type*; the hand-written guides answer *why it exists and when to reach for it*, and");
        page.AppendLine("neither replaces the other.");
        page.AppendLine();
        page.AppendLine("> Do not edit these files. Change the `///` comment in the source and run the generator:");
        page.AppendLine("> `dotnet run --project tools/ActorNet.ApiDocs -- docs/en/api <assembly>.xml`");
        page.AppendLine();
        page.AppendLine("| Namespace | Types |");
        page.AppendLine("|---|---:|");

        foreach (var space in api.Namespaces)
        {
            var types = api.InNamespace(space).Count(m => m.Kind == 'T');
            page.AppendLine($"| [{space}]({FileName(space)}) | {types} |");
        }

        page.AppendLine();
        page.AppendLine($"{api.TypeCount} types, {api.MemberCount} members.");
        page.AppendLine();
        page.AppendLine("[Back to the documentation](../README.md)");

        return page.ToString();
    }

    private static string Namespace(string space, IReadOnlyList<ApiMember> members)
    {
        var page = new StringBuilder();

        page.AppendLine($"# {space}");
        page.AppendLine();
        page.AppendLine("Generated - edit the `///` comments in the source, not this file.");
        page.AppendLine();

        string? currentType = null;

        foreach (var member in members)
        {
            if (member.Kind == 'T')
            {
                currentType = member.FullName;
                page.AppendLine($"## {Generics(member.ShortName)}");
                page.AppendLine();
                Body(page, member);
                continue;
            }

            // A member whose type carried no documentation of its own. Its heading still has to
            // appear, or the member reads as though it belonged to whatever came before it.
            if (member.DeclaringType != currentType)
            {
                currentType = member.DeclaringType;
                var lastDot = currentType.LastIndexOf('.');
                page.AppendLine($"## {(lastDot < 0 ? currentType : currentType[(lastDot + 1)..])}");
                page.AppendLine();
            }

            page.AppendLine($"### {Kind(member.Kind)} `{member.DisplayName}`");
            page.AppendLine();
            Body(page, member);
        }

        page.AppendLine("---");
        page.AppendLine();
        page.AppendLine("[Back to the index](README.md)");

        return page.ToString();
    }

    private static void Body(StringBuilder page, ApiMember member)
    {
        if (member.Summary is { } summary)
        {
            page.AppendLine(summary);
            page.AppendLine();
        }

        if (member.Parameters.Count > 0)
        {
            foreach (var (name, text) in member.Parameters)
                page.AppendLine($"- `{name}` — {text}");

            page.AppendLine();
        }

        if (member.Returns is { } returns)
        {
            page.AppendLine($"**Returns:** {returns}");
            page.AppendLine();
        }

        foreach (var paragraph in member.Remarks)
        {
            page.AppendLine(paragraph);
            page.AppendLine();
        }
    }

    /// <summary>A type heading, with its arity written the way it is declared.</summary>
    private static string Generics(string name)
    {
        var tick = name.IndexOf('`');
        if (tick < 0) return name;

        var digits = name[(tick + 1)..];
        return int.TryParse(digits, out var count) && count > 0
            ? name[..tick] + "&lt;" + string.Join(", ", Enumerable.Range(0, count).Select(i => $"T{i}")) + "&gt;"
            : name[..tick];
    }

    private static string Kind(char kind) => kind switch
    {
        'M' => "method",
        'P' => "property",
        'F' => "field",
        'E' => "event",
        _ => "member",
    };
}
