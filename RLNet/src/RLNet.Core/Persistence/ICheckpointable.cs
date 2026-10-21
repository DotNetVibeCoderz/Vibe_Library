// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace RLNet.Persistence;

/// <summary>
/// Something whose full state can be written to a <see cref="Checkpoint"/> and read back.
/// </summary>
/// <remarks>
/// <para>
/// Every agent RLNet ships implements this, as do the normalisers. It is a separate interface
/// rather than part of <see cref="Agents.IAgent"/> so that an agent written outside the library
/// does not have to support checkpoints to be trained.
/// </para>
/// <para>
/// "Full state" means everything needed to carry on training, not just to act: every network
/// including target networks, every optimiser's moment estimates, the random generator, and the
/// counters the schedules read. What is deliberately left out is experience — replay buffers
/// and a half-filled rollout — which can be hundreds of megabytes and is regathered in minutes.
/// </para>
/// </remarks>
public interface ICheckpointable
{
    /// <summary>Writes every piece of state into <paramref name="writer"/>.</summary>
    void WriteState(CheckpointWriter writer);

    /// <summary>
    /// Restores state previously written by <see cref="WriteState"/>. Throws
    /// <see cref="InvalidDataException"/> when the checkpoint was made by a differently shaped
    /// component, rather than loading what fits and leaving the rest at random.
    /// </summary>
    void ReadState(CheckpointReader reader);
}
