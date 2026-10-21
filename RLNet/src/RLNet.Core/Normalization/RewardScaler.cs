// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Persistence;

namespace RLNet.Normalization;

/// <summary>
/// Divides rewards by the running standard deviation of the discounted return.
/// </summary>
/// <remarks>
/// <para>
/// Pendulum pays up to -16 per step, CartPole +1; a critic learning values in the hundreds needs
/// a different learning rate from one learning values near 1. Scaling by the return's spread puts
/// every task on roughly the same footing, which is most of what lets one set of PPO
/// hyper-parameters work on both.
/// </para>
/// <para>
/// The divisor is the spread of the <em>discounted return</em>, not of the reward. That is the
/// quantity the critic actually regresses on, and it is what the reference implementation
/// (<c>VecNormalize</c> in Stable-Baselines3) uses. The mean is deliberately not subtracted:
/// shifting rewards changes which policy is optimal whenever episodes vary in length, scaling
/// them does not.
/// </para>
/// <para>
/// Each parallel environment carries its own running return, because the return is a property
/// of one trajectory; the statistics it feeds are shared.
/// </para>
/// </remarks>
public sealed class RewardScaler : ICheckpointable
{
    private readonly RunningMeanStd _statistics = new(1);
    private float[] _returns;

    /// <param name="gamma">Discount used for the running return; match the agent's.</param>
    /// <param name="clip">Scaled rewards are clipped to <c>±clip</c>.</param>
    public RewardScaler(float gamma, float clip = 10f, int streams = 1)
    {
        Gamma = gamma;
        Clip = clip;
        _returns = new float[streams];
    }

    public float Gamma { get; }
    public float Clip { get; }

    /// <summary>Number of independent trajectories tracked.</summary>
    public int Streams => _returns.Length;

    /// <summary>Changes how many trajectories are tracked, discarding their running returns.</summary>
    internal void Resize(int streams)
    {
        if (streams != _returns.Length) _returns = new float[streams];
    }

    /// <summary>
    /// Folds a raw reward into stream <paramref name="stream"/>'s return, updates the statistics,
    /// and returns the scaled reward.
    /// </summary>
    public float Scale(int stream, float reward, bool episodeEnded)
    {
        _returns[stream] = _returns[stream] * Gamma + reward;
        _statistics.Update(_returns[stream]);
        if (episodeEnded) _returns[stream] = 0f;

        float std = MathF.Sqrt(_statistics.Variance(0) + 1e-8f);
        return Math.Clamp(reward / std, -Clip, Clip);
    }

    public void WriteState(CheckpointWriter writer) => _statistics.WriteState(writer);

    public void ReadState(CheckpointReader reader)
    {
        _statistics.ReadState(reader);
        Array.Clear(_returns);
    }
}
