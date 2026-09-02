// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Utils;

namespace RLNet.Spaces;

/// <summary>
/// Describes the shape and bounds of an observation or action.
/// </summary>
/// <remarks>
/// Spaces are what make an environment self-describing, and self-description is what lets a
/// generic agent be pointed at an unfamiliar environment: <see cref="Agents.DqnAgent"/> reads
/// <see cref="FlatSize"/> to size its input layer and <see cref="DiscreteSpace.Count"/> to size
/// its output layer, so it needs no per-environment configuration. This mirrors the role
/// <c>gymnasium.spaces</c> plays in the Python ecosystem.
/// </remarks>
public abstract class Space
{
    /// <summary>Number of <see cref="float"/> slots a sample of this space occupies.</summary>
    public abstract int FlatSize { get; }

    /// <summary>Writes a uniformly random sample of this space into <paramref name="destination"/>.</summary>
    public abstract void Sample(FastRandom random, Span<float> destination);

    /// <summary>Returns whether a value lies inside this space.</summary>
    public abstract bool Contains(ReadOnlySpan<float> value);

    /// <summary>Human-readable description used in diagnostics and the visualizer.</summary>
    public abstract override string ToString();
}
