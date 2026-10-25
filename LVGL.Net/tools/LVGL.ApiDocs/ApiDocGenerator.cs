using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Lvgl.ApiDocs;

/// <summary>
/// Writes a Markdown API reference - one page per namespace plus an index - from assemblies and
/// their XML documentation files.
/// </summary>
/// <remarks>
/// <para>
/// Visibility comes from reflection, documentation from the XML. The XML documents internal
/// members as well (the P/Invoke layer is thoroughly commented), so a member is only written when
/// the assembly exposes it publicly - the reference describes what a user can call, nothing else.
/// </para>
/// <para>
/// The output is deterministic (sorted, no timestamps), so regenerating it shows up in a diff only
/// when the API or its documentation actually changed.
/// </para>
/// </remarks>
internal static partial class ApiDocGenerator
{
    /// <summary>Generates the reference into <paramref name="outputDirectory"/>; returns the files written.</summary>
    public static IReadOnlyList<string> Generate(IEnumerable<Assembly> assemblies, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        var types = new List<DocType>();
        foreach (var assembly in assemblies)
        {
            var xmlPath = Path.ChangeExtension(assembly.Location, ".xml");
            var docs = File.Exists(xmlPath) ? LoadDocs(xmlPath) : new Dictionary<string, XElement>();
            types.AddRange(CollectTypes(assembly, docs));
        }

        var written = new List<string>();
        var knownTypes = types.ToDictionary(t => t.FullName, t => t, StringComparer.Ordinal);

        foreach (var group in types.GroupBy(t => t.Namespace).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var path = Path.Combine(outputDirectory, group.Key + ".md");
            File.WriteAllText(path, RenderNamespace(group.Key, group.OrderBy(t => t.Name, StringComparer.Ordinal).ToList(), knownTypes));
            written.Add(path);
        }

        var index = Path.Combine(outputDirectory, "README.md");
        File.WriteAllText(index, RenderIndex(types, knownTypes));
        written.Add(index);

        return written;
    }

    #region Model

    internal sealed record DocType(string FullName, string Namespace, string Name, string Kind, string Assembly, XElement? Doc, List<DocMember> Members);

    internal sealed record DocMember(string Kind, string Signature, XElement? Doc, string SortKey);

    private static Dictionary<string, XElement> LoadDocs(string path) =>
        XDocument.Load(path).Descendants("member")
            .Where(m => m.Attribute("name") is not null)
            .GroupBy(m => m.Attribute("name")!.Value, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

    private static IEnumerable<DocType> CollectTypes(Assembly assembly, Dictionary<string, XElement> docs)
    {
        foreach (var type in assembly.GetExportedTypes())
        {
            if (type.Namespace is null || type.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute))) continue;

            var docName = DocName(type);
            docs.TryGetValue("T:" + docName, out var typeDoc);

            yield return new DocType(
                docName,
                type.Namespace,
                DisplayName(type),
                KindOf(type),
                assembly.GetName().Name ?? string.Empty,
                typeDoc,
                CollectMembers(type, docName, docs).ToList());
        }
    }

    private static IEnumerable<DocMember> CollectMembers(Type type, string docName, Dictionary<string, XElement> docs)
    {
        const BindingFlags Visible = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        if (type.IsEnum)
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                docs.TryGetValue($"F:{docName}.{field.Name}", out var doc);
                var value = Convert.ToInt64(field.GetRawConstantValue(), System.Globalization.CultureInfo.InvariantCulture);
                yield return new DocMember("Value", $"{field.Name} = {value}", doc, $"0{value:D20}");
            }

            yield break;
        }

        foreach (var constructor in type.GetConstructors(Visible))
        {
            var id = $"M:{docName}.#ctor{ParameterList(constructor.GetParameters())}";
            docs.TryGetValue(id, out var doc);
            yield return new DocMember("Constructor", $"{TypeNameOnly(type)}({ParameterDisplay(constructor.GetParameters())})", doc, "0" + id);
        }

        foreach (var property in type.GetProperties(Visible))
        {
            var indexParameters = property.GetIndexParameters();
            var id = $"P:{docName}.{property.Name}{(indexParameters.Length > 0 ? ParameterList(indexParameters) : string.Empty)}";
            docs.TryGetValue(id, out var doc);

            var accessors = (property.GetMethod?.IsPublic == true ? "get; " : string.Empty) + (property.SetMethod?.IsPublic == true ? "set; " : string.Empty);
            var signature = indexParameters.Length > 0
                ? $"{ShortType(property.PropertyType)} this[{ParameterDisplay(indexParameters)}] {{ {accessors}}}"
                : $"{ShortType(property.PropertyType)} {property.Name} {{ {accessors}}}";

            yield return new DocMember("Property", signature, doc, "1" + property.Name);
        }

        foreach (var method in type.GetMethods(Visible))
        {
            if (method.IsSpecialName) continue;   // accessors and operators

            var generic = method.IsGenericMethodDefinition ? $"``{method.GetGenericArguments().Length}" : string.Empty;
            var id = $"M:{docName}.{method.Name}{generic}{ParameterList(method.GetParameters())}";
            docs.TryGetValue(id, out var doc);

            var typeParameters = method.IsGenericMethodDefinition ? $"<{string.Join(", ", method.GetGenericArguments().Select(a => a.Name))}>" : string.Empty;
            var modifiers = method.IsStatic ? "static " : string.Empty;
            yield return new DocMember("Method", $"{modifiers}{ShortType(method.ReturnType)} {method.Name}{typeParameters}({ParameterDisplay(method.GetParameters())})", doc, "2" + method.Name + id);
        }

        foreach (var field in type.GetFields(Visible))
        {
            if (field.IsSpecialName) continue;
            docs.TryGetValue($"F:{docName}.{field.Name}", out var doc);
            var modifier = field.IsLiteral ? "const " : field.IsStatic ? "static " : string.Empty;
            yield return new DocMember("Field", $"{modifier}{ShortType(field.FieldType)} {field.Name}", doc, "3" + field.Name);
        }

        foreach (var @event in type.GetEvents(Visible))
        {
            docs.TryGetValue($"E:{docName}.{@event.Name}", out var doc);
            yield return new DocMember("Event", $"event {ShortType(@event.EventHandlerType!)} {@event.Name}", doc, "4" + @event.Name);
        }
    }

    #endregion

    #region Names

    /// <summary>The name a type has in XML documentation ids: dots for nesting, backtick arity kept.</summary>
    private static string DocName(Type type) => (type.FullName ?? type.Name).Replace('+', '.');

    private static string DisplayName(Type type)
    {
        var name = TypeNameOnly(type);
        return type.IsNested ? DisplayName(type.DeclaringType!) + "." + name : name;
    }

    private static string TypeNameOnly(Type type)
    {
        var name = type.Name;
        var tick = name.IndexOf('`');
        if (tick >= 0) name = name[..tick] + "<" + string.Join(", ", type.GetGenericArguments().Select(a => a.Name)) + ">";
        return name;
    }

    private static string KindOf(Type type) =>
        type.IsEnum ? "enum" :
        type.IsInterface ? "interface" :
        type.IsValueType ? (type.IsDefined(typeof(System.Runtime.CompilerServices.IsReadOnlyAttribute)) ? "readonly struct" : "struct") :
        typeof(Delegate).IsAssignableFrom(type) ? "delegate" :
        type.IsAbstract && type.IsSealed ? "static class" :
        type.IsSealed ? "sealed class" :
        type.IsAbstract ? "abstract class" : "class";

    /// <summary>Parameter list in documentation-id form, e.g. <c>(System.Int32,Lvgl.LvAlign)</c>.</summary>
    private static string ParameterList(ParameterInfo[] parameters) =>
        parameters.Length == 0 ? string.Empty : "(" + string.Join(",", parameters.Select(p => IdType(p.ParameterType))) + ")";

    private static string IdType(Type type)
    {
        if (type.IsByRef) return IdType(type.GetElementType()!) + "@";
        if (type.IsPointer) return IdType(type.GetElementType()!) + "*";
        if (type.IsArray) return IdType(type.GetElementType()!) + "[]";
        if (type.IsGenericParameter) return (type.DeclaringMethod is null ? "`" : "``") + type.GenericParameterPosition;

        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            var name = (definition.FullName ?? definition.Name).Replace('+', '.');
            name = name[..name.IndexOf('`')];
            return name + "{" + string.Join(",", type.GetGenericArguments().Select(IdType)) + "}";
        }

        return (type.FullName ?? type.Name).Replace('+', '.');
    }

    private static string ParameterDisplay(ParameterInfo[] parameters) =>
        string.Join(", ", parameters.Select(p =>
        {
            var type = p.ParameterType.IsByRef ? p.ParameterType.GetElementType()! : p.ParameterType;
            var prefix = p.IsOut ? "out " : p.ParameterType.IsByRef ? (p.IsIn ? "in " : "ref ") : p.IsDefined(typeof(ParamArrayAttribute)) ? "params " : string.Empty;
            var suffix = p.HasDefaultValue ? " = " + DefaultText(p.DefaultValue, type) : string.Empty;
            return $"{prefix}{ShortType(type)} {p.Name}{suffix}";
        }));

    private static string DefaultText(object? value, Type type) => value switch
    {
        null => type.IsValueType && Nullable.GetUnderlyingType(type) is null ? "default" : "null",
        string text => $"\"{text}\"",
        bool flag => flag ? "true" : "false",
        _ when type.IsEnum => $"{type.Name}.{Enum.GetName(type, value) ?? value.ToString()}",
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "default",
    };

    private static string ShortType(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } inner) return ShortType(inner) + "?";
        if (type.IsArray) return ShortType(type.GetElementType()!) + "[]";
        if (type.IsPointer) return ShortType(type.GetElementType()!) + "*";

        if (type.IsGenericType)
        {
            var name = type.Name[..type.Name.IndexOf('`')];
            return name + "<" + string.Join(", ", type.GetGenericArguments().Select(ShortType)) + ">";
        }

        return type.FullName switch
        {
            "System.Void" => "void",
            "System.Boolean" => "bool",
            "System.Byte" => "byte",
            "System.SByte" => "sbyte",
            "System.Int16" => "short",
            "System.UInt16" => "ushort",
            "System.Int32" => "int",
            "System.UInt32" => "uint",
            "System.Int64" => "long",
            "System.UInt64" => "ulong",
            "System.IntPtr" => "nint",
            "System.UIntPtr" => "nuint",
            "System.Single" => "float",
            "System.Double" => "double",
            "System.String" => "string",
            "System.Object" => "object",
            _ => type.IsNested ? ShortType(type.DeclaringType!) + "." + type.Name : type.Name,
        };
    }

    #endregion

    #region Rendering

    private static string RenderIndex(List<DocType> types, Dictionary<string, DocType> known)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# LVGL.Net API reference");
        builder.AppendLine();
        builder.AppendLine("Generated from the XML documentation by `tools/LVGL.ApiDocs`; do not edit by hand.");
        builder.AppendLine("Regenerate with `dotnet run --project tools/LVGL.ApiDocs` after changing a public API.");
        builder.AppendLine();
        builder.AppendLine("Di port oleh Kang Fadhil dari Gravicode Studios.");
        builder.AppendLine();

        foreach (var group in types.GroupBy(t => t.Namespace).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var assemblies = string.Join(", ", group.Select(t => t.Assembly).Distinct().Order(StringComparer.Ordinal));
            builder.AppendLine($"## [{group.Key}]({group.Key}.md)");
            builder.AppendLine();
            builder.AppendLine($"Package: {assemblies}");
            builder.AppendLine();
            builder.AppendLine("| Type | Kind | Summary |");
            builder.AppendLine("|------|------|---------|");

            foreach (var type in group.OrderBy(t => t.Name, StringComparer.Ordinal))
            {
                builder.AppendLine($"| [{Cell(type.Name)}]({group.Key}.md#{Anchor(type.Name)}) | {type.Kind} | {Cell(FirstSentence(Text(type.Doc?.Element("summary"), known, group.Key)))} |");
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string RenderNamespace(string @namespace, List<DocType> types, Dictionary<string, DocType> known)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# {@namespace}");
        builder.AppendLine();
        builder.AppendLine("[API reference](README.md) · generated from the XML documentation; do not edit by hand.");
        builder.AppendLine();

        foreach (var type in types)
        {
            builder.AppendLine($"## {type.Name}");
            builder.AppendLine();
            builder.AppendLine($"`{type.Kind}` in `{type.Assembly}`");
            builder.AppendLine();

            AppendBlock(builder, Text(type.Doc?.Element("summary"), known, @namespace));
            AppendBlock(builder, Text(type.Doc?.Element("remarks"), known, @namespace));
            AppendExample(builder, type.Doc);

            if (type.Members.Count > 0)
            {
                builder.AppendLine(type.Kind == "enum" ? "| Value | Description |" : "| Member | Description |");
                builder.AppendLine("|--------|-------------|");

                foreach (var member in type.Members.OrderBy(m => m.SortKey, StringComparer.Ordinal))
                {
                    var description = Text(member.Doc?.Element("summary"), known, @namespace);
                    var parameters = member.Doc?.Elements("param")
                        .Select(p => $"`{p.Attribute("name")?.Value}`: {Text(p, known, @namespace)}")
                        .ToList() ?? [];
                    var returns = Text(member.Doc?.Element("returns"), known, @namespace);

                    var cell = new StringBuilder(Cell(description));
                    if (parameters.Count > 0) cell.Append("<br>").Append(string.Join("<br>", parameters.Select(Cell)));
                    if (returns.Length > 0) cell.Append("<br>Returns: ").Append(Cell(returns));

                    builder.AppendLine($"| `{Cell(member.Signature)}` | {cell} |");
                }

                builder.AppendLine();
            }
        }

        return builder.ToString();
    }

    private static void AppendBlock(StringBuilder builder, string text)
    {
        if (text.Length == 0) return;
        builder.AppendLine(text);
        builder.AppendLine();
    }

    private static void AppendExample(StringBuilder builder, XElement? doc)
    {
        if (doc?.Element("example")?.Element("code") is not { } code) return;
        builder.AppendLine("```csharp");
        builder.AppendLine(Dedent(code.Value));
        builder.AppendLine("```");
        builder.AppendLine();
    }

    /// <summary>Renders documentation XML as Markdown.</summary>
    internal static string Text(XElement? element, IReadOnlyDictionary<string, DocType>? known = null, string? currentNamespace = null)
    {
        if (element is null) return string.Empty;

        var builder = new StringBuilder();
        foreach (var node in element.Nodes()) Append(builder, node, known, currentNamespace);
        return Whitespace().Replace(builder.ToString(), " ").Replace(" \n", "\n").Replace("\n ", "\n").Trim();
    }

    private static void Append(StringBuilder builder, XNode node, IReadOnlyDictionary<string, DocType>? known, string? currentNamespace)
    {
        switch (node)
        {
            case XText text:
                builder.Append(text.Value);
                break;

            case XElement { Name.LocalName: "c" } code:
                builder.Append('`').Append(code.Value).Append('`');
                break;

            case XElement { Name.LocalName: "code" } block:
                builder.Append("\n\n```csharp\n").Append(Dedent(block.Value)).Append("\n```\n\n");
                break;

            case XElement { Name.LocalName: "para" } paragraph:
                builder.Append("\n\n");
                foreach (var child in paragraph.Nodes()) Append(builder, child, known, currentNamespace);
                builder.Append("\n\n");
                break;

            case XElement { Name.LocalName: "see" or "seealso" } see:
                if (see.Attribute("langword")?.Value is { } word) builder.Append('`').Append(word).Append('`');
                else if (see.Attribute("cref")?.Value is { } cref) builder.Append(Reference(cref, see.Value, known, currentNamespace));
                else if (see.Attribute("href")?.Value is { } href) builder.Append($"[{(see.Value.Length > 0 ? see.Value : href)}]({href})");
                break;

            case XElement { Name.LocalName: "paramref" or "typeparamref" } reference:
                builder.Append('`').Append(reference.Attribute("name")?.Value).Append('`');
                break;

            case XElement { Name.LocalName: "list" } list:
                builder.Append("\n\n");
                foreach (var item in list.Elements("item"))
                {
                    builder.Append("- ");
                    var content = item.Element("description") ?? item;
                    foreach (var child in content.Nodes()) Append(builder, child, known, currentNamespace);
                    builder.Append('\n');
                }

                builder.Append('\n');
                break;

            case XElement { Name.LocalName: "b" or "strong" } bold:
                builder.Append("**").Append(bold.Value.Trim()).Append("**");
                break;

            case XElement { Name.LocalName: "i" or "em" } italic:
                builder.Append('*').Append(italic.Value.Trim()).Append('*');
                break;

            case XElement other:
                foreach (var child in other.Nodes()) Append(builder, child, known, currentNamespace);
                break;
        }
    }

    private static string Reference(string cref, string text, IReadOnlyDictionary<string, DocType>? known, string? currentNamespace)
    {
        var target = cref.Length > 2 && cref[1] == ':' ? cref[2..] : cref;
        var paren = target.IndexOf('(');
        if (paren >= 0) target = target[..paren];

        // Link a type, or the type a member belongs to; show the shortest readable name.
        var display = text.Length > 0 ? text : ShortName(target);
        var typeName = cref.StartsWith("T:", StringComparison.Ordinal) ? target : target[..Math.Max(0, target.LastIndexOf('.'))];

        if (known is not null && known.TryGetValue(typeName, out var type))
        {
            var page = type.Namespace == currentNamespace ? string.Empty : type.Namespace + ".md";
            return $"[`{display}`]({page}#{Anchor(type.Name)})";
        }

        return $"`{display}`";
    }

    private static string ShortName(string target)
    {
        var parts = target.Split('.');
        var name = parts.Length >= 2 && !char.IsUpper(parts[^1][0]) ? parts[^1] : parts[^1];
        name = name.Replace("#ctor", "constructor", StringComparison.Ordinal);
        var tick = name.IndexOf('`');
        return tick >= 0 ? name[..tick] : name;
    }

    private static string Dedent(string code)
    {
        var lines = code.Replace("\r", string.Empty).Split('\n').SkipWhile(string.IsNullOrWhiteSpace).Reverse().SkipWhile(string.IsNullOrWhiteSpace).Reverse().ToList();
        var indent = lines.Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
        return string.Join('\n', lines.Select(l => l.Length >= indent ? l[indent..] : l.TrimStart()));
    }

    private static string FirstSentence(string text)
    {
        var end = text.IndexOf(". ", StringComparison.Ordinal);
        var newline = text.IndexOf('\n');
        if (newline >= 0 && (end < 0 || newline < end)) return text[..newline].TrimEnd();
        return end >= 0 ? text[..(end + 1)] : text;
    }

    /// <summary>Markdown table cells cannot hold newlines or bare pipes.</summary>
    private static string Cell(string text) => text.Replace("|", "\\|", StringComparison.Ordinal).Replace("\n\n", "<br>", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

    /// <summary>GitHub's heading anchor: lower case, spaces to dashes, most punctuation dropped.</summary>
    internal static string Anchor(string heading) =>
        AnchorPunctuation().Replace(heading.ToLowerInvariant().Replace(' ', '-'), string.Empty);

    [GeneratedRegex(@"[ \t]+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"[^a-z0-9\-_]")]
    private static partial Regex AnchorPunctuation();

    #endregion
}
