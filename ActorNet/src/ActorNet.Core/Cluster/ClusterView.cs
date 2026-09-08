// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace ActorNet.Cluster;

/// <summary>One member, as a client needs it: an identity and somewhere to dial.</summary>
/// <param name="NodeId">The id the ring is built from. Not the address - the two are separate on purpose.</param>
/// <param name="Host">The address this member advertises.</param>
/// <param name="Port">The port this member advertises.</param>
public sealed record ClusterViewMember(string NodeId, string Host, int Port);

/// <summary>
/// Enough of the cluster for a client to work out which node owns a key.
/// </summary>
/// <param name="Members">The members a client may dial. Unreachable ones are included; the ring has them too.</param>
/// <param name="VirtualNodes">
/// Virtual nodes per member. Without it a client builds a differently-shaped ring from the same
/// member list and disagrees with every node about who owns what.
/// </param>
/// <remarks>
/// <para>
/// Deliberately not the whole member table: a client has no use for incarnation numbers, last-seen
/// times or statuses, and sending them would make the client's idea of membership something that
/// could drift from the cluster's own and be believed anyway.
/// </para>
/// <para>
/// A client that routes by this is an optimisation, never a requirement. Every node accepts a
/// message for any actor and forwards it, so a client with a stale view is slower and not wrong -
/// which is what makes it safe to hand a client a view that is a few seconds behind.
/// </para>
/// </remarks>
public sealed record ClusterView(IReadOnlyList<ClusterViewMember> Members, int VirtualNodes);
