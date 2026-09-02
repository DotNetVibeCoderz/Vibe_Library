// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Utils;

namespace RLNet.Buffers;

/// <summary>
/// Storage for off-policy transitions, swappable under any off-policy agent.
/// </summary>
/// <remarks>
/// This is the seam the modularity requirement asks for: <see cref="Agents.DqnAgent"/> and the
/// continuous-control agents take an <see cref="IReplayBuffer"/> rather than constructing one,
/// so moving from uniform to prioritised replay — or to a custom buffer that, say, keeps a
/// separate stream of demonstrations — is a constructor argument, not a fork of the agent.
/// </remarks>
public interface IReplayBuffer
{
    /// <summary>Maximum transitions retained before the oldest are overwritten.</summary>
    int Capacity { get; }

    /// <summary>Transitions currently stored.</summary>
    int Count { get; }

    /// <summary>Width of a stored observation.</summary>
    int ObservationSize { get; }

    /// <summary>Width of a stored action.</summary>
    int ActionSize { get; }

    /// <summary>Stores one transition, evicting the oldest if the buffer is full.</summary>
    /// <param name="terminated">
    /// True only for a real terminal state. A transition cut off by a time limit is stored with
    /// false, so that the agent still bootstraps through it.
    /// </param>
    void Add(ReadOnlySpan<float> observation, ReadOnlySpan<float> action, float reward, ReadOnlySpan<float> nextObservation, bool terminated);

    /// <summary>Fills <paramref name="batch"/> with <paramref name="batchSize"/> transitions.</summary>
    void Sample(int batchSize, ReplayBatch batch, FastRandom random);

    /// <summary>
    /// Reports fresh TD errors for the slots named in <paramref name="indices"/>. Uniform
    /// buffers ignore this; prioritised buffers use it to re-rank.
    /// </summary>
    void UpdatePriorities(ReadOnlySpan<int> indices, ReadOnlySpan<float> tdErrors);

    /// <summary>Drops every stored transition.</summary>
    void Clear();
}
