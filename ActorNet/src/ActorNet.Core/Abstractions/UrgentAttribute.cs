// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Collections.Concurrent;

namespace ActorNet;

/// <summary>
/// Marks a message type that may overtake a backlog in an actor's mailbox.
/// </summary>
/// <remarks>
/// <para>
/// A mailbox is ordered, and that ordering is a guarantee: messages from one sender to one actor
/// are handled in the order they were sent. Marking a type urgent gives that guarantee up for that
/// type against every other type - which is the entire point of it, and the reason it is declared
/// on the message rather than passed at the send. A type either overtakes or it does not, and both
/// ends can see which by reading the type. A per-send flag would mean the same message sometimes
/// overtook and sometimes did not, and nothing at the receiving end could tell you why.
/// </para>
/// <para>
/// Ordering <em>within</em> a lane still holds: two urgent messages from one sender arrive in
/// order, as do two ordinary ones. Only the relationship between the two lanes is given up.
/// </para>
/// <para>
/// It works the same way for a message that arrived over the wire, because the receiving node
/// resolves the alias to this type before posting it. Nothing in the protocol carries priority.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [Urgent]
/// public sealed record CancelBatch(string Reason);
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class UrgentAttribute : Attribute;

/// <summary>Answers whether a message type is urgent, once per type.</summary>
/// <remarks>
/// Reflection on every send would cost more than the feature saves. The answer cannot change for a
/// given type, so it is asked once and cached for the life of the process.
/// </remarks>
internal static class UrgentMessages
{
    private static readonly ConcurrentDictionary<Type, bool> Known = new();

    public static bool Is(Type type) =>
        Known.GetOrAdd(type, static t => t.IsDefined(typeof(UrgentAttribute), inherit: false));
}
