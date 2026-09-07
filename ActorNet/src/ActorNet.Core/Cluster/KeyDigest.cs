// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace ActorNet.Cluster;

/// <summary>
/// What one node is holding that another would inherit if it disappeared.
/// </summary>
/// <param name="Node">The node that is holding them.</param>
/// <param name="Keys">Actor addresses, in <c>Type/Key</c> form.</param>
/// <remarks>
/// <para>
/// The warm handoff only covers a node that leaves politely, because only that node knows what it
/// was holding. A node lost without warning takes that knowledge with it, and the survivors inherit
/// a set of keys with no idea which were live - so every one of them pays a read at the moment
/// traffic arrives, which is exactly when the cluster is already one node short.
/// </para>
/// <para>
/// This is advice and not a directory. It is a snapshot of what one node held when it last spoke,
/// so it can name an actor that has since deactivated - warming that one costs a read and nothing
/// else - and it can miss one activated since. Nothing routes by it: the ring decides ownership,
/// and a second opinion about ownership is the last thing a cluster needs.
/// </para>
/// </remarks>
public sealed record KeyDigest(string Node, IReadOnlyList<string> Keys);
