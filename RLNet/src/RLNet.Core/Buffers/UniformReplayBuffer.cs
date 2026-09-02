// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Utils;

namespace RLNet.Buffers;

/// <summary>A fixed-capacity circular replay buffer sampled uniformly at random.</summary>
/// <remarks>
/// <para>
/// Stored as a structure of arrays rather than an array of transition objects. A million-step
/// buffer over a four-dimensional observation is a million small objects on the heap under the
/// obvious design, and a handful of large flat arrays under this one — which is both far less
/// memory and far friendlier to the prefetcher when a batch is gathered.
/// </para>
/// <para>
/// Successor observations are stored explicitly rather than inferred from the next slot. That
/// costs one observation per transition, and buys correctness at episode boundaries and the
/// freedom to overwrite in a circle without a special case at the wrap point.
/// </para>
/// </remarks>
public class UniformReplayBuffer : IReplayBuffer
{
    private readonly float[] _observations;
    private readonly float[] _nextObservations;
    private readonly float[] _actions;
    private readonly float[] _rewards;
    private readonly bool[] _terminated;

    private int _head;   // next slot to write
    private int _count;

    public UniformReplayBuffer(int capacity, int observationSize, int actionSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        Capacity = capacity;
        ObservationSize = observationSize;
        ActionSize = actionSize;

        _observations = new float[capacity * observationSize];
        _nextObservations = new float[capacity * observationSize];
        _actions = new float[capacity * actionSize];
        _rewards = new float[capacity];
        _terminated = new bool[capacity];
    }

    public int Capacity { get; }
    public int Count => _count;
    public int ObservationSize { get; }
    public int ActionSize { get; }

    /// <summary>Slot the next <see cref="Add"/> will write to. Prioritised subclasses need it.</summary>
    protected int Head => _head;

    public void Add(ReadOnlySpan<float> observation, ReadOnlySpan<float> action, float reward, ReadOnlySpan<float> nextObservation, bool terminated)
    {
        int slot = _head;

        observation.CopyTo(_observations.AsSpan(slot * ObservationSize, ObservationSize));
        nextObservation.CopyTo(_nextObservations.AsSpan(slot * ObservationSize, ObservationSize));
        action.CopyTo(_actions.AsSpan(slot * ActionSize, ActionSize));
        _rewards[slot] = reward;
        _terminated[slot] = terminated;

        OnAdded(slot);

        _head = slot + 1 == Capacity ? 0 : slot + 1;
        if (_count < Capacity) _count++;
    }

    /// <summary>Convenience overload for discrete agents, storing the action index as a single value.</summary>
    public void AddDiscrete(ReadOnlySpan<float> observation, int action, float reward, ReadOnlySpan<float> nextObservation, bool terminated)
    {
        Span<float> encoded = stackalloc float[1];
        encoded[0] = action;
        Add(observation, encoded, reward, nextObservation, terminated);
    }

    /// <summary>Hook for subclasses that maintain per-slot bookkeeping. Called before the head advances.</summary>
    protected virtual void OnAdded(int slot) { }

    public virtual void Sample(int batchSize, ReplayBatch batch, FastRandom random)
    {
        if (_count == 0) throw new InvalidOperationException("Cannot sample an empty replay buffer.");
        int n = Math.Min(batchSize, batch.Capacity);

        for (int i = 0; i < n; i++)
        {
            int slot = random.NextInt(_count);
            CopyInto(slot, i, batch);
            batch.Indices[i] = slot;
            batch.Weights[i] = 1f;
        }

        batch.Count = n;
    }

    /// <summary>Copies one stored transition into position <paramref name="target"/> of a batch.</summary>
    protected void CopyInto(int slot, int target, ReplayBatch batch)
    {
        _observations.AsSpan(slot * ObservationSize, ObservationSize)
            .CopyTo(batch.Observations.AsSpan(target * ObservationSize, ObservationSize));
        _nextObservations.AsSpan(slot * ObservationSize, ObservationSize)
            .CopyTo(batch.NextObservations.AsSpan(target * ObservationSize, ObservationSize));
        _actions.AsSpan(slot * ActionSize, ActionSize)
            .CopyTo(batch.Actions.AsSpan(target * ActionSize, ActionSize));

        batch.Rewards[target] = _rewards[slot];
        batch.Terminated[target] = _terminated[slot];
    }

    /// <summary>No-op: uniform sampling has no priorities to revise.</summary>
    public virtual void UpdatePriorities(ReadOnlySpan<int> indices, ReadOnlySpan<float> tdErrors) { }

    public virtual void Clear()
    {
        _head = 0;
        _count = 0;
    }
}
