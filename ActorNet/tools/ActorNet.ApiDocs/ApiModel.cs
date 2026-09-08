// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Xml.Linq;

namespace ActorNet.ApiDocs;

/// <summary>One documented member, as the compiler recorded it.</summary>
/// <param name="Kind">T, M, P, F or E - the letter the compiler puts in front of the name.</param>
/// <param name="FullName">The name without that letter.</param>
/// <param name="Summary">The summary, flattened to one paragraph.</param>
/// <param name="Remarks">The remarks, paragraph by paragraph.</param>
/// <param name="Parameters">Parameter names and what they are for.</param>
/// <param name="Returns">What the member returns, when it says.</param>
public sealed record ApiMember(
    char Kind,
    string FullName,
    string? Summary,
    IReadOnlyList<string> Remarks,
    IReadOnlyList<(string Name, string Text)> Parameters,
    string? Returns)
{
    /// <summary>The namespace, which is everything before the last dot of the declaring type.</summary>
    public string Namespace
    {
        get
        {
            var type = Kind == 'T' ? FullName : DeclaringType;
            var lastDot = type.LastIndexOf('.');
            return lastDot < 0 ? string.Empty : type[..lastDot];
        }
    }

    /// <summary>The type this belongs to. Itself, for a type.</summary>
    public string DeclaringType
    {
        get
        {
            if (Kind == 'T') return FullName;

            // Strip a parameter list before looking for the dot that separates type from member, or
            // a parameter type containing a dot is mistaken for the member name.
            var name = FullName;
            var paren = name.IndexOf('(');
            if (paren >= 0) name = name[..paren];

            var lastDot = name.LastIndexOf('.');
            return lastDot < 0 ? name : name[..lastDot];
        }
    }

    /// <summary>The member's own name, without its namespace or declaring type.</summary>
    public string ShortName
    {
        get
        {
            var name = FullName;
            var paren = name.IndexOf('(');
            var head = paren >= 0 ? name[..paren] : name;
            var tail = paren >= 0 ? name[paren..] : string.Empty;

            var lastDot = head.LastIndexOf('.');
            return (lastDot < 0 ? head : head[(lastDot + 1)..]) + tail;
        }
    }

    /// <summary>The name as somebody would write it in C#.</summary>
    /// <remarks>
    /// The compiler's own form is unreadable and, worse, unprintable: a doc id spells generics with
    /// backticks, and a backtick inside a markdown code span ends it. So this is not only nicer, it
    /// is what stops half the reference rendering as broken text.
    /// </remarks>
    public string DisplayName
    {
        get
        {
            var name = ShortName;

            var paren = name.IndexOf('(');
            var head = paren >= 0 ? name[..paren] : name;
            var arguments = paren >= 0 ? name[(paren + 1)..].TrimEnd(')') : null;

            // A constructor is written as the type it constructs, which is how it is called.
            if (head is "#ctor") head = SimpleName(DeclaringType);
            else if (head is "#cctor") head = $"static {SimpleName(DeclaringType)}";

            head = Generics(head);

            if (arguments is null) return head;
            if (arguments.Length == 0) return head + "()";

            return head + "(" + string.Join(", ", Split(arguments).Select(Parameter)) + ")";
        }
    }

    /// <summary>Turns <c>Name`2</c> into <c>Name&lt;T0, T1&gt;</c>.</summary>
    private static string Generics(string name)
    {
        var tick = name.IndexOf('`');
        if (tick < 0) return name;

        // Two backticks mark a method's own type parameters, one marks the type's. Both are written
        // the same way here, because in a signature they read the same.
        var digits = name[tick..].TrimStart('`');
        if (!int.TryParse(digits, out var count) || count <= 0) return name[..tick];

        return name[..tick] + "<" + string.Join(", ", Enumerable.Range(0, count).Select(i => $"T{i}")) + ">";
    }

    /// <summary>One parameter type, without its namespace.</summary>
    private static string Parameter(string type)
    {
        var text = type.Replace('{', '<').Replace('}', '>').Replace("@", " ref", StringComparison.Ordinal);

        // Namespaces stripped from every name in the expression, not just the outermost, so
        // IAsyncEnumerable<System.Int32> does not keep half of one.
        return string.Concat(Segments(text));

        static IEnumerable<string> Segments(string text)
        {
            var start = 0;
            for (var i = 0; i <= text.Length; i++)
            {
                if (i < text.Length && text[i] is not ('<' or '>' or ',' or ' ')) continue;

                yield return SimpleName(text[start..i]).Replace("``", "T", StringComparison.Ordinal)
                    .Replace("`", "T", StringComparison.Ordinal);

                // A comma inside a generic argument list reads as a separator, so it gets the space
                // a reader expects; the angle brackets do not.
                if (i < text.Length) yield return text[i] == ',' ? ", " : text[i].ToString();
                start = i + 1;
            }
        }
    }

    /// <summary>Splits a parameter list on the commas that are not inside a generic argument.</summary>
    private static IEnumerable<string> Split(string arguments)
    {
        var depth = 0;
        var start = 0;

        for (var i = 0; i < arguments.Length; i++)
        {
            switch (arguments[i])
            {
                case '{': depth++; break;
                case '}': depth--; break;
                case ',' when depth == 0:
                    yield return arguments[start..i];
                    start = i + 1;
                    break;
            }
        }

        if (start < arguments.Length) yield return arguments[start..];
    }

    private static string SimpleName(string name)
    {
        var lastDot = name.LastIndexOf('.');
        return lastDot < 0 ? name : name[(lastDot + 1)..];
    }
}

/// <summary>Every documented type and member, grouped by namespace.</summary>
public sealed class ApiModel
{
    private readonly Dictionary<string, List<ApiMember>> _byNamespace = new(StringComparer.Ordinal);

    /// <summary>Namespaces holding at least one documented type, in order.</summary>
    public IReadOnlyList<string> Namespaces => [.. _byNamespace.Keys.Order(StringComparer.Ordinal)];

    /// <summary>Documented types across every namespace.</summary>
    public int TypeCount { get; private set; }

    /// <summary>Documented members that are not types.</summary>
    public int MemberCount { get; private set; }

    /// <summary>Everything in one namespace.</summary>
    public IReadOnlyList<ApiMember> InNamespace(string name) => _byNamespace.TryGetValue(name, out var members) ? members : [];

    /// <summary>Reads one or more compiler-produced XML files.</summary>
    public static ApiModel Read(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var model = new ApiModel();

        var documents = paths.Select(XDocument.Load).ToArray();

        // Every documented type, read first. A nested type carries its declaring type in its name
        // exactly where a namespace would be, so without this list ActorSystem becomes a namespace
        // the moment anything inside it is documented - and a private helper class ends up with a
        // page of its own next to the public API.
        var types = documents
            .SelectMany(document => document.Descendants("member"))
            .Select(element => element.Attribute("name")?.Value)
            .Where(name => name is { Length: > 2 } && name[0] == 'T' && name[1] == ':')
            .Select(name => name![2..])
            .ToHashSet(StringComparer.Ordinal);

        foreach (var document in documents)
        {
            foreach (var element in document.Descendants("member"))
            {
                if (element.Attribute("name")?.Value is not { Length: > 2 } name || name[1] != ':') continue;

                var member = new ApiMember(
                    name[0],
                    name[2..],
                    Flatten(element.Element("summary")),
                    [.. Paragraphs(element.Element("remarks"))],
                    [.. element.Elements("param")
                        .Select(p => (Name: p.Attribute("name")?.Value ?? string.Empty, Text: Flatten(p) ?? string.Empty))
                        .Where(p => p.Name.Length > 0)],
                    Flatten(element.Element("returns")));

                // Compiler-generated members carry no useful documentation and would bury the ones
                // somebody wrote.
                if (member.ShortName.Contains('<', StringComparison.Ordinal)) continue;

                // Nested: what would be its namespace is a type. Left out rather than given a page,
                // because everything nested here is an implementation detail of its container.
                if (types.Contains(member.Namespace)) continue;

                var bucket = model._byNamespace.TryGetValue(member.Namespace, out var existing)
                    ? existing
                    : model._byNamespace[member.Namespace] = [];

                bucket.Add(member);

                if (member.Kind == 'T') model.TypeCount++;
                else model.MemberCount++;
            }
        }

        foreach (var bucket in model._byNamespace.Values)
        {
            bucket.Sort((left, right) =>
            {
                var byType = string.CompareOrdinal(left.DeclaringType, right.DeclaringType);
                if (byType != 0) return byType;

                // The type itself first, then its members. Reading a member before knowing what it
                // hangs off is the wrong order.
                if (left.Kind == 'T' && right.Kind != 'T') return -1;
                if (right.Kind == 'T' && left.Kind != 'T') return 1;

                return string.CompareOrdinal(left.ShortName, right.ShortName);
            });
        }

        return model;
    }

    /// <summary>The text of an element, with tags turned into markdown and whitespace normalised.</summary>
    private static string? Flatten(XElement? element)
    {
        if (element is null) return null;

        var text = Render(element).Trim();
        return text.Length == 0 ? null : string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Remarks split at their paragraph tags, because that is what they were written as.</summary>
    private static IEnumerable<string> Paragraphs(XElement? element)
    {
        if (element is null) yield break;

        var paragraphs = element.Elements("para").ToArray();
        if (paragraphs.Length == 0)
        {
            if (Flatten(element) is { } single) yield return single;
            yield break;
        }

        foreach (var paragraph in paragraphs)
            if (Flatten(paragraph) is { } text) yield return text;
    }

    private static string Render(XNode node) => node switch
    {
        XText text => text.Value,

        // A cref is rendered as the name it points at rather than as a link. The reference is one
        // file per namespace, so half of these would be anchors into a file the reader is already
        // in and the other half would need a mapping that goes stale the moment a type moves.
        XElement { Name.LocalName: "see" or "seealso" } link =>
            Code(Simplify(link.Attribute("cref")?.Value ?? link.Attribute("langword")?.Value ?? link.Value)),

        XElement { Name.LocalName: "paramref" or "typeparamref" } reference =>
            Code(reference.Attribute("name")?.Value ?? string.Empty),

        XElement { Name.LocalName: "c" or "code" } code => Code(code.Value.Trim()),
        XElement { Name.LocalName: "em" or "i" } emphasis => $"*{string.Concat(emphasis.Nodes().Select(Render)).Trim()}*",
        XElement { Name.LocalName: "strong" or "b" } strong => $"**{string.Concat(strong.Nodes().Select(Render)).Trim()}**",

        XElement element => string.Concat(element.Nodes().Select(Render)),
        _ => string.Empty,
    };

    private static string Code(string value) => value.Length == 0 ? string.Empty : $"`{value}`";

    /// <summary>Turns <c>T:ActorNet.Runtime.ActorCell</c> into <c>ActorCell</c>.</summary>
    private static string Simplify(string cref)
    {
        var name = cref.Length > 2 && cref[1] == ':' ? cref[2..] : cref;
        var paren = name.IndexOf('(');
        if (paren >= 0) name = name[..paren];

        var lastDot = name.LastIndexOf('.');
        name = lastDot < 0 ? name : name[(lastDot + 1)..];

        // The arity goes too. A cref is rendered inside a markdown code span, and a backtick there
        // closes it - so IAsyncEnumerable`1 does not merely read badly, it breaks the line it is on.
        var tick = name.IndexOf('`');
        return tick < 0 ? name : name[..tick];
    }
}
