using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ScrapyNet.Generators;

/// <summary>
/// Generates field accessors for item types marked <c>[ScrapyItem]</c> and registers them with
/// <c>ScrapyNet.ItemAccessors</c> from a module initializer. <c>ItemAdapter</c> then reads and writes
/// those items through plain compiled lambdas instead of reflection and <c>Expression.Compile</c>,
/// which is what trimmed and NativeAOT applications need.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ItemAccessorGenerator : IIncrementalGenerator
{
    private const string AttributeName = "ScrapyNet.ScrapyItemAttribute";

    private static readonly DiagnosticDescriptor Unsupported = new(
        "SCRAPYNET001",
        "Item type cannot get generated accessors",
        "[ScrapyItem] type '{0}' is skipped: {1}",
        "ScrapyNet",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions & ~SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var models = context.SyntaxProvider.ForAttributeWithMetadataName(
            AttributeName,
            static (node, _) => node is TypeDeclarationSyntax,
            static (ctx, _) => Describe((INamedTypeSymbol)ctx.TargetSymbol));

        context.RegisterSourceOutput(models, static (spc, model) =>
        {
            if (model.Error is not null)
            {
                spc.ReportDiagnostic(Diagnostic.Create(Unsupported, model.Location?.ToLocation(), model.DisplayName, model.Error));
                return;
            }
            spc.AddSource($"ScrapyItem.{model.HintName}.g.cs", Render(model));
        });
    }

    private static ItemModel Describe(INamedTypeSymbol type)
    {
        var display = type.ToDisplayString();
        var location = LocationInfo.From(type.Locations.FirstOrDefault());
        string? error = null;
        if (type.IsValueType) error = "structs are not items (items are reference types)";
        else if (type.IsGenericType) error = "generic item types are not supported";
        else if (type.IsAbstract) error = "abstract types are never item instances";
        else if (!IsReachable(type)) error = "the type (or a containing type) must be internal or public";
        if (error is not null) return new ItemModel(display, "", "", location, error, EquatableArray<MemberModel>.Empty);

        var members = new List<MemberModel>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            foreach (var member in current.GetMembers())
            {
                if (member.IsStatic || member.DeclaredAccessibility != Accessibility.Public || !seen.Add(member.Name)) continue;
                if (IsJsonIgnored(member)) continue;
                switch (member)
                {
                    case IPropertySymbol p when !p.IsIndexer && p.GetMethod is { DeclaredAccessibility: Accessibility.Public } && p.Name != "EqualityContract":
                    {
                        var setter = p.SetMethod is { DeclaredAccessibility: Accessibility.Public } set
                            ? set.IsInitOnly ? SetterKind.Init : SetterKind.Assign
                            : SetterKind.None;
                        members.Add(new MemberModel(p.Name, p.Type.ToDisplayString(TypeFormat), setter, p.ContainingType.ToDisplayString(TypeFormat)));
                        break;
                    }
                    case IFieldSymbol f when !f.IsConst && !f.IsImplicitlyDeclared:
                        members.Add(new MemberModel(f.Name, f.Type.ToDisplayString(TypeFormat), f.IsReadOnly ? SetterKind.None : SetterKind.Assign, f.ContainingType.ToDisplayString(TypeFormat)));
                        break;
                }
            }
        }

        var full = type.ToDisplayString(TypeFormat);
        var hint = new string(type.ToDisplayString().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
        return new ItemModel(display, full, hint, location, null, new EquatableArray<MemberModel>([.. members]));
    }

    private static bool IsReachable(INamedTypeSymbol type)
    {
        for (var t = type; t is not null; t = t.ContainingType)
            if (t.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal)) return false;
        return true;
    }

    private static bool IsJsonIgnored(ISymbol member)
    {
        foreach (var attribute in member.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != "System.Text.Json.Serialization.JsonIgnoreAttribute") continue;
            // No argument means Condition = Always; JsonIgnoreCondition.Always is 1.
            var condition = attribute.NamedArguments.FirstOrDefault(a => a.Key == "Condition").Value;
            if (condition.IsNull || condition.Value is 1) return true;
        }
        return false;
    }

    private static string Render(ItemModel model)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("#pragma warning disable CA2255, CS8600, CS8601, CS8603, CS8604, CS8605, CS8625");
        sb.AppendLine("namespace ScrapyNet.Generated");
        sb.AppendLine("{");
        sb.AppendLine($"    /// <summary>Generated accessors for {Escape(model.DisplayName)}.</summary>");
        sb.AppendLine($"    [global::System.CodeDom.Compiler.GeneratedCode(\"ScrapyNet.Generators\", \"1.0\")]");
        sb.AppendLine($"    internal static class ScrapyItem_{model.HintName}");
        sb.AppendLine("    {");
        sb.AppendLine("        [global::System.Runtime.CompilerServices.ModuleInitializer]");
        sb.AppendLine("        internal static void Register()");
        sb.AppendLine("        {");
        sb.AppendLine($"            global::ScrapyNet.ItemAccessors.Register(typeof({model.FullName}), new global::ScrapyNet.ItemMember[]");
        sb.AppendLine("            {");
        foreach (var m in model.Members)
        {
            var getter = $"static o => (object?)(({model.FullName})o).{m.Name}";
            var setter = m.Setter switch
            {
                SetterKind.Assign => $"static (o, v) => (({model.FullName})o).{m.Name} = ({m.Type})v",
                SetterKind.Init => $"static (o, v) => Init_{m.Name}(({m.DeclaringType})o, ({m.Type})v)",
                _ => "null",
            };
            sb.AppendLine($"                new global::ScrapyNet.ItemMember(\"{m.Name}\", typeof({m.Type}), {getter}, {setter}),");
        }
        sb.AppendLine("            });");
        sb.AppendLine("        }");
        foreach (var m in model.Members.Where(m => m.Setter == SetterKind.Init))
        {
            // init accessors cannot be called from C# after construction, but loaders fill records
            // after construction; UnsafeAccessor reaches the setter without reflection.
            sb.AppendLine();
            sb.AppendLine($"        [global::System.Runtime.CompilerServices.UnsafeAccessor(global::System.Runtime.CompilerServices.UnsafeAccessorKind.Method, Name = \"set_{m.Name}\")]");
            sb.AppendLine($"        private static extern void Init_{m.Name}({m.DeclaringType} item, {m.Type} value);");
        }
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string Escape(string text) => text.Replace("<", "&lt;").Replace(">", "&gt;");

    private enum SetterKind { None, Assign, Init }

    private sealed record MemberModel(string Name, string Type, SetterKind Setter, string DeclaringType);

    private sealed record ItemModel(string DisplayName, string FullName, string HintName, LocationInfo? Location, string? Error, EquatableArray<MemberModel> Members);

    /// <summary>A location without the syntax tree, so models stay cacheable between compilations.</summary>
    private sealed record LocationInfo(string Path, Microsoft.CodeAnalysis.Text.TextSpan Span, Microsoft.CodeAnalysis.Text.LinePositionSpan Lines)
    {
        public static LocationInfo? From(Location? location) =>
            location?.SourceTree is null ? null : new LocationInfo(location.SourceTree.FilePath, location.SourceSpan, location.GetLineSpan().Span);

        public Location ToLocation() => Location.Create(Path, Span, Lines);
    }
}

/// <summary>An immutable array compared by contents, so incremental pipeline steps cache correctly.</summary>
internal readonly struct EquatableArray<T>(T[] items) : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    public static readonly EquatableArray<T> Empty = new([]);

    private readonly T[] _items = items;

    public bool Equals(EquatableArray<T> other) => (_items ?? []).AsSpan().SequenceEqual(other._items ?? []);

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = 17;
        foreach (var item in _items ?? []) hash = hash * 31 + (item?.GetHashCode() ?? 0);
        return hash;
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)(_items ?? [])).GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
