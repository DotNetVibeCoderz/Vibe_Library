// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Spaces;

namespace RLNet.Agents;

/// <summary>Maps a continuous observation onto a single integer key for a lookup table.</summary>
/// <remarks>
/// A delegate rather than an interface because it is called on every step of every episode and
/// wants to be as close to a function pointer as the runtime allows.
/// </remarks>
public delegate long StateKeyEncoder(ReadOnlySpan<float> observation);

/// <summary>Builds <see cref="StateKeyEncoder"/> functions for tabular agents.</summary>
/// <remarks>
/// <para>
/// A table needs a finite set of states, so a continuous observation has to be bucketed first.
/// The bucket counts are the single most consequential choice in tabular RL: too few and
/// genuinely different situations collapse into one entry the agent cannot tell apart, too many
/// and the table is so sparse that no state is ever visited twice.
/// </para>
/// <para>
/// The key is packed into a <see cref="long"/> — a mixed-radix number, one digit per dimension —
/// rather than built as a string. String keys allocate on every step and hash by walking
/// characters; an integer key does neither, and is collision-free by construction rather than by
/// hope.
/// </para>
/// </remarks>
public static class StateDiscretizer
{
    /// <summary>Buckets a value into <c>[0, bins)</c> over the range <c>[low, high]</c>.</summary>
    public static int Bucket(float value, float low, float high, int bins)
    {
        if (value <= low) return 0;
        if (value >= high) return bins - 1;
        return Math.Min(bins - 1, (int)((value - low) / (high - low) * bins));
    }

    /// <summary>
    /// Builds an encoder that buckets each dimension of a box space independently.
    /// </summary>
    /// <param name="space">The observation space; supplies the per-dimension range.</param>
    /// <param name="bins">
    /// Buckets per dimension. A dimension given 1 bucket is ignored entirely, which is the
    /// standard way to drop an observation a tabular agent cannot afford to represent.
    /// </param>
    /// <param name="clampRange">
    /// Replaces infinite bounds with a finite range. Velocities are usually declared unbounded
    /// even though their useful values are small, and an unbounded dimension cannot be bucketed.
    /// </param>
    public static StateKeyEncoder ForBox(BoxSpace space, int[] bins, float clampRange = 5f)
    {
        if (bins.Length != space.FlatSize)
            throw new ArgumentException($"Expected {space.FlatSize} bin counts, got {bins.Length}.", nameof(bins));

        var low = new float[bins.Length];
        var high = new float[bins.Length];

        long capacity = 1;
        for (int i = 0; i < bins.Length; i++)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(bins[i], 1);

            low[i] = float.IsInfinity(space.Low[i]) ? -clampRange : space.Low[i];
            high[i] = float.IsInfinity(space.High[i]) ? clampRange : space.High[i];

            capacity *= bins[i];
            if (capacity > int.MaxValue)
                throw new ArgumentException(
                    $"Bin counts describe {capacity} states, which is past the point a table is a sensible " +
                    "representation. Reduce the bins, or use a neural agent such as DqnAgent.",
                    nameof(bins));
        }

        var counts = (int[])bins.Clone();

        return observation =>
        {
            long key = 0;
            for (int i = 0; i < counts.Length; i++)
                key = key * counts[i] + Bucket(observation[i], low[i], high[i], counts[i]);
            return key;
        };
    }

    /// <summary>
    /// An encoder for one-hot observations: returns the index of the set element.
    /// </summary>
    /// <remarks>
    /// GridWorld publishes one-hot cells, and bucketing each of 25 near-binary dimensions
    /// separately would describe 2^25 states to represent 25. This reads the index directly.
    /// </remarks>
    public static StateKeyEncoder OneHot() => observation =>
    {
        for (int i = 0; i < observation.Length; i++)
            if (observation[i] > 0.5f) return i;
        return observation.Length; // an all-zero observation gets its own key rather than colliding with cell 0
    };
}
