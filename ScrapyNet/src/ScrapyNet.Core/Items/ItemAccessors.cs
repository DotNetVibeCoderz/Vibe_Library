using System.Collections.Concurrent;

namespace ScrapyNet;

/// <summary>
/// Marks an item class or record for compile-time generated field accessors. <see cref="ItemAdapter"/>
/// then reads and writes it through generated lambdas instead of reflection and expression compilation,
/// which keeps items working in trimmed and NativeAOT applications (and skips the one-off compile cost).
/// </summary>
/// <remarks>
/// The generator ships in the Gravicode.ScrapyNet package. The type must be a non-generic, non-abstract
/// class or record that is internal or public. Public properties and fields become fields, as with
/// reflection; init-only properties stay settable by loaders.
/// </remarks>
/// <example>
/// <code>
/// [ScrapyItem]
/// public sealed record Book(string Title, decimal Price);
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ScrapyItemAttribute : Attribute;

/// <summary>One field of a registered item type: name, CLR type and accessors.</summary>
/// <param name="Setter"><c>null</c> for read-only members.</param>
public sealed record ItemMember(string Name, Type Type, Func<object, object?> Getter, Action<object, object?>? Setter);

/// <summary>
/// Registry of item accessors produced ahead of time. The <c>[ScrapyItem]</c> generator registers into it
/// from a module initializer; hand-written registrations work the same way.
/// </summary>
public static class ItemAccessors
{
    private static readonly ConcurrentDictionary<Type, ItemMember[]> Registered = new();

    /// <summary>Registers the members of <paramref name="type"/>. Later registrations replace earlier ones.</summary>
    public static void Register(Type type, ItemMember[] members)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(members);
        Registered[type] = members;
        ItemAdapter.TypeAccessor.Invalidate(type);
    }

    /// <summary>True when <paramref name="type"/> has registered (generated) accessors.</summary>
    public static bool IsRegistered(Type type) => Registered.ContainsKey(type);

    internal static ItemMember[]? Get(Type type) => Registered.GetValueOrDefault(type);
}
