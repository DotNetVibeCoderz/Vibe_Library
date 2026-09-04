// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Diagnostics.CodeAnalysis;

namespace ActorNet;

/// <summary>
/// The address of a virtual actor: an actor type name plus a user-chosen key, rendered as
/// <c>Type/Key</c>.
/// </summary>
/// <remarks>
/// <para>
/// The type half is what the runtime resolves against its registry to know which class to
/// activate; the key half is opaque to the runtime and may itself contain <c>/</c>, so parsing
/// splits on the <em>first</em> separator only. That matters for hierarchical keys such as
/// <c>Device/plant-3/line-2</c>.
/// </para>
/// <para>
/// This is a struct because it is copied into every envelope and used as a dictionary key on the
/// hot path; making it a class would put an allocation in front of every message send.
/// </para>
/// </remarks>
public readonly record struct ActorId(string Type, string Key)
{
    /// <summary>Separator between the type half and the key half.</summary>
    public const char Separator = '/';

    /// <summary>An unset address. Used for "no sender" rather than a nullable, to keep envelopes flat.</summary>
    public static ActorId None => default;

    /// <summary>True when this is <see cref="None"/>.</summary>
    public bool IsEmpty => Type is null;

    /// <summary>Builds an address for the actor type <typeparamref name="T"/>.</summary>
    public static ActorId For<T>(string key) where T : IActor => new(typeof(T).Name, key);

    /// <summary>Parses <c>Type/Key</c>, throwing when the value has no separator.</summary>
    public static ActorId Parse(string value) =>
        TryParse(value, out var id) ? id : throw new FormatException(
            $"'{value}' is not a valid actor id. Expected 'Type{Separator}Key', for example 'BankAccountActor{Separator}user-1'.");

    /// <summary>Parses <c>Type/Key</c>, returning false instead of throwing.</summary>
    public static bool TryParse([NotNullWhen(true)] string? value, out ActorId id)
    {
        id = default;
        if (string.IsNullOrEmpty(value)) return false;

        var split = value.IndexOf(Separator);
        // Both halves must be non-empty: "/key" has no type to activate and "Type/" has no identity.
        if (split <= 0 || split == value.Length - 1) return false;

        id = new ActorId(value[..split], value[(split + 1)..]);
        return true;
    }

    /// <inheritdoc />
    public override string ToString() => IsEmpty ? "<none>" : string.Concat(Type, Separator.ToString(), Key);
}
