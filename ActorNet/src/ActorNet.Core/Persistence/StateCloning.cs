// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Text.Json;

namespace ActorNet.Persistence;

/// <summary>
/// Deep-copies state on the way into and out of the in-memory stores.
/// </summary>
/// <remarks>
/// <para>
/// Without this, an in-memory store holds a reference to the object the actor is still mutating,
/// so a "stored" value keeps changing after it was stored. The bug that exposes it is subtle and
/// specific: an event-sourced actor writes a snapshot at sequence 20, keeps applying events to the
/// same instance, and recovery then reads a snapshot whose contents are from sequence 25 but whose
/// sequence number says 20 - so the events after 20 get applied twice and the state is silently
/// wrong.
/// </para>
/// <para>
/// A database provider gets this for free, because writing serializes. The in-memory stores have
/// to do it deliberately, which is also what keeps them an honest stand-in for a real one in
/// tests.
/// </para>
/// </remarks>
internal static class StateCloning
{
    private static readonly JsonSerializerOptions Options = new() { IncludeFields = true };

    /// <summary>Returns a copy that shares no mutable state with <paramref name="value"/>.</summary>
    public static T Clone<T>(T value)
    {
        // Strings and boxed primitives cannot be mutated behind the store's back, so the round
        // trip would be pure cost.
        if (value is null || value is string || typeof(T).IsPrimitive) return value;

        var json = JsonSerializer.Serialize(value, Options);
        return JsonSerializer.Deserialize<T>(json, Options)
               ?? throw new ActorNetException($"State of type {typeof(T).Name} did not survive a round trip; it must be JSON-serializable.");
    }
}
