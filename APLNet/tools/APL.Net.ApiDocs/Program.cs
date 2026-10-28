// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AplNet.ApiDocs;

/// <summary>
/// Writes one Markdown page per public type of APL.Net, plus an index, from the XML documentation the
/// compiler emits next to the assembly. Output is deterministic (members sorted, no timestamps) so CI
/// can regenerate it and diff.
/// </summary>
public static partial class Program
{
    private static Dictionary<string, XElement> s_docs = [];
    private static HashSet<Type> s_documentedTypes = [];

    public static int Main(string[] args)
    {
        string outDir = Path.GetFullPath(args.Length > 0 ? args[0] : "docs/api");
        Assembly assembly = typeof(Apl).Assembly;
        string xmlPath = Path.ChangeExtension(assembly.Location, ".xml");
        if (!File.Exists(xmlPath))
        {
            Console.Error.WriteLine($"XML documentation not found next to the assembly: {xmlPath}");
            return 1;
        }

        s_docs = XDocument.Load(xmlPath).Root!.Element("members")!.Elements("member")
            .ToDictionary(m => (string)m.Attribute("name")!, m => m);

        Type[] types = assembly.GetExportedTypes()
            .Where(t => !t.IsNested || t.DeclaringType!.IsPublic)
            .OrderBy(t => t.Namespace, StringComparer.Ordinal)
            .ThenBy(t => DisplayName(t), StringComparer.Ordinal)
            .ToArray();
        s_documentedTypes = [.. types];

        Directory.CreateDirectory(outDir);
        foreach (string stale in Directory.GetFiles(outDir, "*.md"))
            File.Delete(stale);

        foreach (Type type in types)
            Write(Path.Combine(outDir, FileName(type)), RenderType(type));
        Write(Path.Combine(outDir, "README.md"), RenderIndex(types));

        Console.WriteLine($"Wrote {types.Length} type pages and an index to {outDir}");
        return 0;
    }

    private static void Write(string path, string content) =>
        File.WriteAllText(path, content.Replace("\r\n", "\n"), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    // ---------------------------------------------------------------- pages

    private static string RenderIndex(Type[] types)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# APL.Net API reference");
        sb.AppendLine();
        sb.AppendLine("Generated from the XML documentation by `tools/APL.Net.ApiDocs`. Do not edit by hand: run");
        sb.AppendLine("`dotnet run --project tools/APL.Net.ApiDocs -c Release -- docs/api` after changing a doc comment.");
        sb.AppendLine("CI fails when this folder is out of date.");
        sb.AppendLine();
        sb.AppendLine("Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.");
        foreach (IGrouping<string?, Type> ns in types.GroupBy(t => t.Namespace))
        {
            sb.AppendLine();
            sb.AppendLine($"## {ns.Key}");
            sb.AppendLine();
            sb.AppendLine("| Type | Kind | Summary |");
            sb.AppendLine("|---|---|---|");
            foreach (Type type in ns)
                sb.AppendLine($"| [{Escape(DisplayName(type))}]({FileName(type)}) | {Kind(type)} | {OneLine(Summary(DocId(type)))} |");
        }

        return sb.ToString();
    }

    private static string RenderType(Type type)
    {
        var sb = new StringBuilder();
        string id = DocId(type);
        sb.AppendLine($"# {Escape(DisplayName(type))} {Kind(type)}");
        sb.AppendLine();
        sb.AppendLine($"Namespace: `{type.Namespace}` · Assembly: `APL.Net` · [Index](README.md)");
        sb.AppendLine();
        sb.AppendLine("```csharp");
        sb.AppendLine(TypeDeclaration(type));
        sb.AppendLine("```");
        AppendDoc(sb, id, includeSummary: true);

        if (type.IsEnum)
        {
            sb.AppendLine();
            sb.AppendLine("## Values");
            sb.AppendLine();
            sb.AppendLine("| Name | Value | Description |");
            sb.AppendLine("|---|---|---|");
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
                sb.AppendLine($"| `{field.Name}` | {Convert.ToInt64(field.GetRawConstantValue())} | {OneLine(Summary("F:" + TypeId(type) + "." + field.Name))} |");
            return sb.ToString();
        }

        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var constructors = type.IsInterface || IsStaticClass(type) || type.IsSubclassOf(typeof(Delegate))
            ? []
            : type.GetConstructors(flags).Where(c => c.GetParameters().Length > 0 || !type.IsValueType).ToList();
        var properties = type.GetProperties(flags).OrderBy(p => p.Name, StringComparer.Ordinal).ToList();
        var fields = type.GetFields(flags).Where(f => !f.IsSpecialName).OrderBy(f => f.Name, StringComparer.Ordinal).ToList();
        var methods = type.IsSubclassOf(typeof(Delegate))
            ? []
            : type.GetMethods(flags).Where(m => !m.IsSpecialName && m.DeclaringType == type)
                .OrderBy(m => m.Name, StringComparer.Ordinal).ThenBy(m => Signature(m), StringComparer.Ordinal).ToList();

        AppendMembers(sb, "Constructors", constructors.Select(c => ((MemberInfo)c, MemberId(c), Signature(c))));
        AppendMembers(sb, "Fields", fields.Select(f => ((MemberInfo)f, MemberId(f), Signature(f))));
        AppendMembers(sb, "Properties", properties.Select(p => ((MemberInfo)p, MemberId(p), Signature(p))));
        AppendMembers(sb, "Methods", methods.Select(m => ((MemberInfo)m, MemberId(m), Signature(m))));
        return sb.ToString();
    }

    private static void AppendMembers(StringBuilder sb, string heading, IEnumerable<(MemberInfo Member, string Id, string Signature)> members)
    {
        var list = members.ToList();
        if (list.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine($"## {heading}");
        foreach ((MemberInfo member, string id, string signature) in list)
        {
            sb.AppendLine();
            sb.AppendLine($"### {Escape(MemberTitle(member))}");
            sb.AppendLine();
            sb.AppendLine("```csharp");
            sb.AppendLine(signature);
            sb.AppendLine("```");
            AppendDoc(sb, ResolveInheritDoc(member, id), includeSummary: true);
        }
    }

    private static void AppendDoc(StringBuilder sb, string id, bool includeSummary)
    {
        if (!s_docs.TryGetValue(id, out XElement? doc))
            return;

        if (includeSummary && doc.Element("summary") is { } summary)
        {
            sb.AppendLine();
            sb.AppendLine(RenderBlock(summary));
        }

        var typeParams = doc.Elements("typeparam").ToList();
        if (typeParams.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**Type parameters**");
            sb.AppendLine();
            foreach (XElement p in typeParams)
                sb.AppendLine($"- `{p.Attribute("name")?.Value}`: {OneLine(RenderBlock(p))}");
        }

        var parameters = doc.Elements("param").ToList();
        if (parameters.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**Parameters**");
            sb.AppendLine();
            foreach (XElement p in parameters)
                sb.AppendLine($"- `{p.Attribute("name")?.Value}`: {OneLine(RenderBlock(p))}");
        }

        if (doc.Element("returns") is { } returns)
        {
            sb.AppendLine();
            sb.AppendLine($"**Returns:** {OneLine(RenderBlock(returns))}");
        }

        var exceptions = doc.Elements("exception").ToList();
        if (exceptions.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**Exceptions**");
            sb.AppendLine();
            foreach (XElement e in exceptions)
                sb.AppendLine($"- {CrefText(e.Attribute("cref")?.Value)}: {OneLine(RenderBlock(e))}");
        }

        if (doc.Element("remarks") is { } remarks)
        {
            sb.AppendLine();
            sb.AppendLine("**Remarks**");
            sb.AppendLine();
            sb.AppendLine(RenderBlock(remarks));
        }
    }

    /// <summary><c>&lt;inheritdoc/&gt;</c> on an interface implementation: use the interface member's documentation.</summary>
    private static string ResolveInheritDoc(MemberInfo member, string id)
    {
        if (!s_docs.TryGetValue(id, out XElement? doc) || doc.Element("inheritdoc") is null)
            return id;

        if (member is MethodInfo method && method.DeclaringType is { IsInterface: false } methodType)
        {
            Type definition = methodType.IsGenericType ? methodType.GetGenericTypeDefinition() : methodType;
            MethodInfo target = definition == methodType ? method : (MethodInfo)definition.Module.ResolveMethod(method.MetadataToken)!;
            foreach (Type iface in definition.GetInterfaces())
            {
                InterfaceMapping map = definition.GetInterfaceMap(iface);
                int index = Array.IndexOf(map.TargetMethods, target);
                if (index < 0)
                    continue;

                MethodInfo ifaceMethod = map.InterfaceMethods[index];
                if (ifaceMethod.DeclaringType!.IsGenericType)
                    ifaceMethod = (MethodInfo)ifaceMethod.Module.ResolveMethod(ifaceMethod.MetadataToken)!;
                return MemberId(ifaceMethod);
            }
        }

        if (member is PropertyInfo property)
        {
            Type declaring = property.DeclaringType!;
            Type definition = declaring.IsGenericType ? declaring.GetGenericTypeDefinition() : declaring;
            foreach (Type iface in definition.GetInterfaces())
            {
                Type ifaceDef = iface.IsGenericType ? iface.GetGenericTypeDefinition() : iface;
                PropertyInfo? match = ifaceDef.GetProperty(property.Name);
                if (match is not null)
                    return MemberId(match);
            }
        }

        return id;
    }

    // ---------------------------------------------------------------- XML doc text

    private static string Summary(string id) =>
        s_docs.TryGetValue(id, out XElement? doc) && doc.Element("summary") is { } summary ? RenderBlock(summary) : "";

    private static string RenderBlock(XElement element)
    {
        var sb = new StringBuilder();
        RenderNodes(element.Nodes(), sb);
        string text = sb.ToString();
        text = WhitespaceRun().Replace(text, " ");
        text = string.Join("\n\n", text.Split('\u0001', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).Where(p => p.Length > 0));
        return text.Trim();
    }

    private static void RenderNodes(IEnumerable<XNode> nodes, StringBuilder sb)
    {
        foreach (XNode node in nodes)
        {
            switch (node)
            {
                case XText text:
                    sb.Append(text.Value);
                    break;
                case XElement e:
                    switch (e.Name.LocalName)
                    {
                        case "para":
                            sb.Append('\u0001');
                            RenderNodes(e.Nodes(), sb);
                            sb.Append('\u0001');
                            break;
                        case "c":
                            sb.Append('`').Append(e.Value).Append('`');
                            break;
                        case "b":
                        case "strong":
                            sb.Append("**");
                            RenderNodes(e.Nodes(), sb);
                            sb.Append("**");
                            break;
                        case "em":
                        case "i":
                            sb.Append('*');
                            RenderNodes(e.Nodes(), sb);
                            sb.Append('*');
                            break;
                        case "paramref":
                        case "typeparamref":
                            sb.Append('`').Append(e.Attribute("name")?.Value).Append('`');
                            break;
                        case "see":
                        case "seealso":
                            if (e.Attribute("langword") is { } word)
                                sb.Append('`').Append(word.Value).Append('`');
                            else if (e.Attribute("href") is { } href)
                                sb.Append($"[{(e.IsEmpty ? href.Value : e.Value)}]({href.Value})");
                            else
                                sb.Append(CrefText(e.Attribute("cref")?.Value));
                            break;
                        default:
                            RenderNodes(e.Nodes(), sb);
                            break;
                    }

                    break;
            }
        }
    }

    /// <summary>A cref as inline code, linked when it points into a documented type.</summary>
    private static string CrefText(string? cref)
    {
        if (string.IsNullOrEmpty(cref))
            return "";

        string kind = cref.Length > 2 && cref[1] == ':' ? cref[..1] : "";
        string name = kind.Length > 0 ? cref[2..] : cref;
        string withoutArgs = name.Split('(')[0];
        string typeName = kind == "T" ? withoutArgs : withoutArgs[..Math.Max(0, withoutArgs.LastIndexOf('.'))];
        string display = Pretty(kind == "T" ? withoutArgs : withoutArgs[(typeName.Length + 1)..], kind == "T");
        if (kind != "T" && typeName.Length > 0)
            display = Pretty(typeName, true) + "." + display;

        Type? target = s_documentedTypes.FirstOrDefault(t => TypeId(t) == typeName);
        return target is null ? $"`{display}`" : $"[`{display}`]({FileName(target)})";
    }

    /// <summary>"AplNet.Simd.ExpOperator`1" to "ExpOperator&lt;T&gt;"-ish: last segment, arity as &lt;…&gt;.</summary>
    private static string Pretty(string name, bool isType)
    {
        if (isType)
            name = name[(name.LastIndexOf('.') + 1)..];
        name = GenericArity().Replace(name, m => "<" + string.Join(", ", Enumerable.Range(0, int.Parse(m.Groups[1].Value)).Select(i => i == 0 ? "T" : "T" + (i + 1))) + ">");
        return name.Replace("#ctor", "ctor");
    }

    private static string OneLine(string text) => Escape(text.Replace("\n\n", " ").Replace('\n', ' ')).Replace("|", "\\|");

    /// <summary>Angle brackets outside code spans would be read as HTML.</summary>
    private static string Escape(string text)
    {
        var sb = new StringBuilder();
        bool code = false;
        foreach (char ch in text)
        {
            if (ch == '`')
                code = !code;
            sb.Append(!code && ch == '<' ? "&lt;" : !code && ch == '>' ? "&gt;" : ch.ToString());
        }

        return sb.ToString();
    }

    [GeneratedRegex(@"[ \t\r\n]+")]
    private static partial Regex WhitespaceRun();

    [GeneratedRegex(@"``?(\d+)")]
    private static partial Regex GenericArity();
}
