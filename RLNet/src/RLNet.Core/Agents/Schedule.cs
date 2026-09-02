// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace RLNet.Agents;

/// <summary>A value that changes over the course of training.</summary>
/// <remarks>
/// Exploration rate, learning rate and PPO's clip range all want to shrink as training
/// progresses, and all three want it expressed the same way. A schedule reads training progress
/// in <c>[0, 1]</c> and returns the value for that point, which keeps the annealing policy out
/// of every agent that needs one.
/// </remarks>
public readonly struct Schedule
{
    private readonly float _start;
    private readonly float _end;
    private readonly float _fraction;
    private readonly bool _exponential;

    private Schedule(float start, float end, float fraction, bool exponential)
    {
        _start = start;
        _end = end;
        _fraction = fraction;
        _exponential = exponential;
    }

    /// <summary>A value that never changes.</summary>
    public static Schedule Constant(float value) => new(value, value, 1f, exponential: false);

    /// <summary>
    /// Moves from <paramref name="start"/> to <paramref name="end"/> over the first
    /// <paramref name="fraction"/> of training, then holds.
    /// </summary>
    /// <remarks>
    /// Finishing the decay early rather than at the very end is deliberate: an agent still
    /// exploring on its last episode has no chance to consolidate, and the final stretch of
    /// low-exploration training is what turns a policy that works sometimes into one that works.
    /// </remarks>
    public static Schedule Linear(float start, float end, float fraction = 0.5f) =>
        new(start, end, Math.Clamp(fraction, 1e-6f, 1f), exponential: false);

    /// <summary>
    /// Decays geometrically from <paramref name="start"/> toward <paramref name="end"/>.
    /// </summary>
    /// <remarks>
    /// The classic DQN choice. It spends far more of the run at low exploration than a linear
    /// decay does, which suits value-based agents — they need dense coverage early and precision
    /// late — but starves the harder exploration problems. MountainCar is the environment where
    /// the difference is obvious.
    /// </remarks>
    public static Schedule Exponential(float start, float end, float fraction = 0.5f) =>
        new(start, end, Math.Clamp(fraction, 1e-6f, 1f), exponential: true);

    /// <summary>Returns the value at a point in training, given progress in <c>[0, 1]</c>.</summary>
    public float At(float progress)
    {
        float t = Math.Clamp(progress / _fraction, 0f, 1f);

        if (!_exponential) return _start + (_end - _start) * t;

        // Geometric interpolation in log space. Guarded because a schedule toward exactly zero
        // has no logarithm, and falling back to linear there is better than returning NaN.
        if (_start <= 0f || _end <= 0f) return _start + (_end - _start) * t;
        return _start * MathF.Pow(_end / _start, t);
    }
}
