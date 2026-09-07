// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace ActorNet.Cluster;

/// <summary>
/// Where a node looks for peers to introduce itself to.
/// </summary>
/// <remarks>
/// <para>
/// The configured list is the usual answer, and <see cref="StaticSeedSource"/> is it. The interface
/// exists for the answers that change while the process runs - a set of pods, a service registry -
/// where re-reading a list of names is not the same as asking again.
/// </para>
/// <para>
/// It is asked on every join attempt, including the retries a node makes while it is alone, so a
/// source that has just learned about a new peer is used at the next beat without anything being
/// restarted. It is not asked once at startup and cached, which is the whole point.
/// </para>
/// </remarks>
public interface ISeedSource
{
    /// <summary>The seeds to try now, as <c>host:port</c>.</summary>
    /// <remarks>
    /// Failure is reported as an empty list rather than an exception where the source can manage
    /// it: a directory that is briefly unavailable should cost this attempt, not the node.
    /// </remarks>
    ValueTask<IReadOnlyList<string>> SeedsAsync(CancellationToken cancellationToken);
}

/// <summary>The configured list, unchanged.</summary>
public sealed class StaticSeedSource(IReadOnlyList<string> seeds) : ISeedSource
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<string>> SeedsAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(seeds);
}
