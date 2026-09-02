// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace RLNet.Buffers;

/// <summary>
/// A pre-allocated minibatch of transitions, filled in place by
/// <see cref="IReplayBuffer.Sample"/>.
/// </summary>
/// <remarks>
/// An off-policy agent samples one of these on every gradient step, which at a million steps is
/// a million minibatches. Returning a fresh object each time would make the replay sampler the
/// largest allocator in the process, so an agent creates one batch at construction and the
/// sampler overwrites it. Every array is flat: observations are <c>[batch, obsSize]</c> laid out
/// row-major so a whole batch can be memcpy'd into a network input buffer in one pass.
/// </remarks>
public sealed class ReplayBatch
{
    public ReplayBatch(int capacity, int observationSize, int actionSize)
    {
        Capacity = capacity;
        ObservationSize = observationSize;
        ActionSize = actionSize;

        Observations = new float[capacity * observationSize];
        NextObservations = new float[capacity * observationSize];
        Actions = new float[capacity * actionSize];
        Rewards = new float[capacity];
        Terminated = new bool[capacity];
        Indices = new int[capacity];
        Weights = new float[capacity];
    }

    /// <summary>Largest batch this container can hold.</summary>
    public int Capacity { get; }

    /// <summary>Number of transitions actually filled by the last sample.</summary>
    public int Count { get; internal set; }

    public int ObservationSize { get; }
    public int ActionSize { get; }

    /// <summary>Observations, row-major.</summary>
    public float[] Observations { get; }

    /// <summary>Successor observations, row-major.</summary>
    public float[] NextObservations { get; }

    /// <summary>Actions, row-major. Discrete actions occupy one slot holding the index.</summary>
    public float[] Actions { get; }

    public float[] Rewards { get; }

    /// <summary>
    /// Whether each successor was a true terminal state. Never set for time-limit truncation —
    /// see <see cref="Environments.StepResult"/> for why that distinction has to survive into
    /// the buffer.
    /// </summary>
    public bool[] Terminated { get; }

    /// <summary>Buffer slot each transition came from, needed to write priorities back.</summary>
    public int[] Indices { get; }

    /// <summary>
    /// Importance-sampling weights. Uniform sampling fills these with 1; prioritised sampling
    /// fills them with the correction for its non-uniform draw.
    /// </summary>
    public float[] Weights { get; }

    /// <summary>The action index of sample <paramref name="i"/>, for discrete agents.</summary>
    public int DiscreteAction(int i) => (int)Actions[i * ActionSize];

    /// <summary>The observation of sample <paramref name="i"/>.</summary>
    public ReadOnlySpan<float> Observation(int i) => Observations.AsSpan(i * ObservationSize, ObservationSize);

    /// <summary>The successor observation of sample <paramref name="i"/>.</summary>
    public ReadOnlySpan<float> NextObservation(int i) => NextObservations.AsSpan(i * ObservationSize, ObservationSize);

    /// <summary>The action vector of sample <paramref name="i"/>.</summary>
    public ReadOnlySpan<float> Action(int i) => Actions.AsSpan(i * ActionSize, ActionSize);
}
