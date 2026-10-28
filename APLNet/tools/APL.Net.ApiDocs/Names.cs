// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Reflection;
using System.Text;

namespace AplNet.ApiDocs;

// Two naming schemes: the compiler's documentation IDs (to find a member's XML), and C# as a reader
// would write it (for headings and signatures).
public static partial class Program
{
    private static readonly NullabilityInfoContext s_nullability = new();

    private static readonly Dictionary<Type, string> s_keywords = new()
    {
        [typeof(void)] = "void", [typeof(bool)] = "bool", [typeof(byte)] = "byte", [typeof(sbyte)] = "sbyte",
        [typeof(short)] = "short", [typeof(ushort)] = "ushort", [typeof(int)] = "int", [typeof(uint)] = "uint",
        [typeof(long)] = "long", [typeof(ulong)] = "ulong", [typeof(float)] = "float", [typeof(double)] = "double",
        [typeof(decimal)] = "decimal", [typeof(char)] = "char", [typeof(string)] = "string", [typeof(object)] = "object",
        [typeof(nint)] = "nint", [typeof(nuint)] = "nuint",
    };

    // ---------------------------------------------------------------- documentation IDs

    private static string DocId(Type type) => "T:" + TypeId(type);

    /// <summary>The type part of a documentation ID: namespace, nesting with dots, and `n for generic arity.</summary>
    private static string TypeId(Type type)
    {
        if (type.IsGenericType && !type.IsGenericTypeDefinition)
            type = type.GetGenericTypeDefinition();
        string name = type.Name;
        return type.IsNested ? TypeId(type.DeclaringType!) + "." + name : type.Namespace + "." + name;
    }

    private static string MemberId(MemberInfo member)
    {
        string type = TypeId(member.DeclaringType!);
        switch (member)
        {
            case ConstructorInfo ctor:
                return $"M:{type}.#ctor{ParameterIds(ctor.GetParameters())}";
            case MethodInfo method:
                string arity = method.IsGenericMethod ? "``" + method.GetGenericArguments().Length : "";
                return $"M:{type}.{method.Name}{arity}{ParameterIds(method.GetParameters())}";
            case PropertyInfo property:
                return $"P:{type}.{property.Name}";
            case FieldInfo field:
                return $"F:{type}.{field.Name}";
            default:
                throw new NotSupportedException(member.GetType().Name);
        }
    }

    private static string ParameterIds(ParameterInfo[] parameters) =>
        parameters.Length == 0 ? "" : "(" + string.Join(",", parameters.Select(p => ParameterTypeId(p.ParameterType))) + ")";

    private static string ParameterTypeId(Type type)
    {
        if (type.IsByRef)
            return ParameterTypeId(type.GetElementType()!) + "@";
        if (type.IsPointer)
            return ParameterTypeId(type.GetElementType()!) + "*";
        if (type.IsArray)
            return ParameterTypeId(type.GetElementType()!) + "[]";
        if (type.IsGenericParameter)
            return (type.DeclaringMethod is null ? "`" : "``") + type.GenericParameterPosition;
        if (type.IsGenericType)
        {
            string definition = TypeId(type.GetGenericTypeDefinition());
            definition = definition[..definition.LastIndexOf('`')];
            return definition + "{" + string.Join(",", type.GetGenericArguments().Select(ParameterTypeId)) + "}";
        }

        return TypeId(type);
    }

    // ---------------------------------------------------------------- C# names

    private static string FileName(Type type) => TypeId(type).Replace('`', '-') + ".md";

    private static string DisplayName(Type type)
    {
        string name = type.Name;
        int tick = name.IndexOf('`');
        if (tick >= 0)
            name = name[..tick] + "<" + string.Join(", ", type.GetGenericArguments().Select(a => a.Name)) + ">";
        return type.IsNested ? DisplayName(type.DeclaringType!) + "." + name : name;
    }

    private static string Kind(Type type) =>
        type.IsEnum ? "enum" :
        type.IsInterface ? "interface" :
        type.IsSubclassOf(typeof(Delegate)) ? "delegate" :
        type.IsValueType ? "struct" : "class";

    private static bool IsStaticClass(Type type) => type.IsAbstract && type.IsSealed && !type.IsInterface;

    private static string TypeDeclaration(Type type)
    {
        if (type.IsSubclassOf(typeof(Delegate)))
        {
            MethodInfo invoke = type.GetMethod("Invoke")!;
            return $"public delegate {TypeName(invoke.ReturnType)} {DisplayName(type)}({Parameters(invoke.GetParameters())});";
        }

        var sb = new StringBuilder("public ");
        if (IsStaticClass(type))
            sb.Append("static ");
        else if (type.IsSealed && !type.IsValueType && !type.IsEnum)
            sb.Append("sealed ");
        else if (type.IsAbstract && !type.IsInterface)
            sb.Append("abstract ");
        if (type.IsValueType && !type.IsEnum && type.GetCustomAttributesData().Any(a => a.AttributeType.Name == "IsReadOnlyAttribute"))
            sb.Append("readonly ");
        sb.Append(Kind(type)).Append(' ').Append(DisplayName(type));

        var bases = new List<string>();
        if (type.BaseType is { } baseType && baseType != typeof(object) && baseType != typeof(ValueType) && baseType != typeof(Enum))
            bases.Add(TypeName(baseType));
        Type[] declared = type.GetInterfaces();
        Type[] inherited = declared.SelectMany(i => i.GetInterfaces()).Concat(type.BaseType?.GetInterfaces() ?? []).ToArray();
        bases.AddRange(declared.Except(inherited).Select(TypeName).OrderBy(n => n, StringComparer.Ordinal));
        if (bases.Count > 0)
            sb.Append(" : ").Append(string.Join(", ", bases));
        sb.Append(Constraints(type.IsGenericTypeDefinition ? type.GetGenericArguments() : []));
        return sb.ToString();
    }

    private static string MemberTitle(MemberInfo member) => member switch
    {
        ConstructorInfo ctor => $"{DisplayName(ctor.DeclaringType!).Split('<')[0]}({ParameterTypes(ctor.GetParameters())})",
        MethodInfo method => $"{method.Name}{(method.IsGenericMethod ? "<" + string.Join(", ", method.GetGenericArguments().Select(a => a.Name)) + ">" : "")}({ParameterTypes(method.GetParameters())})",
        _ => member.Name,
    };

    private static string ParameterTypes(ParameterInfo[] parameters) => string.Join(", ", parameters.Select(p => TypeName(p.ParameterType)));

    private static string Signature(ConstructorInfo ctor) =>
        $"public {DisplayName(ctor.DeclaringType!).Split('<')[0]}({Parameters(ctor.GetParameters())});";

    private static string Signature(MethodInfo method)
    {
        var sb = new StringBuilder("public ");
        if (method.IsStatic)
            sb.Append("static ");
        if (method.GetCustomAttributesData().Any(a => a.AttributeType.Name == "IsReadOnlyAttribute"))
            sb.Append("readonly ");
        sb.Append(ReturnTypeName(method)).Append(' ').Append(method.Name);
        if (method.IsGenericMethod)
            sb.Append('<').Append(string.Join(", ", method.GetGenericArguments().Select(a => a.Name))).Append('>');
        sb.Append('(').Append(Parameters(method.GetParameters())).Append(')');
        sb.Append(Constraints(method.IsGenericMethod ? method.GetGenericArguments() : []));
        return sb.Append(';').ToString();
    }

    private static string Signature(PropertyInfo property)
    {
        bool isStatic = (property.GetMethod ?? property.SetMethod)!.IsStatic;
        string accessors = (property.GetMethod is { IsPublic: true } ? "get; " : "") +
                           (property.SetMethod is { IsPublic: true } setter
                               ? (setter.ReturnParameter.GetRequiredCustomModifiers().Any(m => m.Name == "IsExternalInit") ? "init; " : "set; ")
                               : "");
        string type = TypeName(property.PropertyType) + (IsNullable(s_nullability.Create(property).ReadState, property.PropertyType) ? "?" : "");
        return $"public {(isStatic ? "static " : "")}{type} {property.Name} {{ {accessors}}}";
    }

    private static string Signature(FieldInfo field) =>
        $"public {(field.IsLiteral ? "const " : field.IsStatic ? "static readonly " : field.IsInitOnly ? "readonly " : "")}{TypeName(field.FieldType)} {field.Name}" +
        (field.IsLiteral ? $" = {Literal(field.GetRawConstantValue())}" : "") + ";";

    private static string ReturnTypeName(MethodInfo method) =>
        TypeName(method.ReturnType) + (IsNullable(s_nullability.Create(method.ReturnParameter).ReadState, method.ReturnType) ? "?" : "");

    private static string Parameters(ParameterInfo[] parameters) => string.Join(", ", parameters.Select(p =>
    {
        Type type = p.ParameterType;
        string modifier = "";
        if (type.IsByRef)
        {
            modifier = p.IsOut ? "out " : p.IsIn ? "in " : "ref ";
            type = type.GetElementType()!;
        }

        string name = TypeName(type) + (IsNullable(s_nullability.Create(p).ReadState, type) ? "?" : "");
        string value = !p.HasDefaultValue ? "" :
            type.IsEnum && p.DefaultValue is not null ? $" = {TypeName(type)}.{Enum.GetName(type, p.DefaultValue)}" :
            " = " + Literal(p.DefaultValue);
        return $"{modifier}{name} {p.Name}{value}";
    }));

    private static bool IsNullable(NullabilityState state, Type type) =>
        state == NullabilityState.Nullable && !type.IsValueType && !type.IsGenericParameter;

    private static string Constraints(Type[] parameters)
    {
        var sb = new StringBuilder();
        foreach (Type p in parameters)
        {
            var parts = new List<string>();
            GenericParameterAttributes attributes = p.GenericParameterAttributes;
            bool isStruct = attributes.HasFlag(GenericParameterAttributes.NotNullableValueTypeConstraint);
            bool unmanaged = isStruct && p.GetCustomAttributesData().Any(a => a.AttributeType.Name == "IsUnmanagedAttribute");
            if (unmanaged)
                parts.Add("unmanaged");
            else if (isStruct)
                parts.Add("struct");
            else if (attributes.HasFlag(GenericParameterAttributes.ReferenceTypeConstraint))
                parts.Add("class");
            parts.AddRange(p.GetGenericParameterConstraints().Where(c => c != typeof(ValueType)).Select(TypeName));
            if (attributes.HasFlag(GenericParameterAttributes.DefaultConstructorConstraint) && !isStruct)
                parts.Add("new()");
            if (parts.Count > 0)
                sb.Append($"\n    where {p.Name} : {string.Join(", ", parts)}");
        }

        return sb.ToString();
    }

    private static string TypeName(Type type)
    {
        if (s_keywords.TryGetValue(type, out string? keyword))
            return keyword;
        if (type.IsGenericParameter)
            return type.Name;
        if (type.IsArray)
            return TypeName(type.GetElementType()!) + "[]";
        if (type.IsPointer)
            return TypeName(type.GetElementType()!) + "*";
        if (type.IsByRef)
            return TypeName(type.GetElementType()!);
        if (Nullable.GetUnderlyingType(type) is { } underlying)
            return TypeName(underlying) + "?";

        string name = type.Name;
        int tick = name.IndexOf('`');
        if (tick >= 0)
            name = name[..tick];
        if (type.IsGenericType)
            name += "<" + string.Join(", ", type.GetGenericArguments().Select(TypeName)) + ">";
        return type.IsNested && !type.DeclaringType!.IsGenericType ? TypeName(type.DeclaringType!) + "." + name : name;
    }

    private static string Literal(object? value) => value switch
    {
        null => "null",
        string s => $"\"{s}\"",
        bool b => b ? "true" : "false",
        float f => f.ToString(System.Globalization.CultureInfo.InvariantCulture) + "f",
        double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };
}
