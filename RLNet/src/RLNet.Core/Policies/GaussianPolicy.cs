// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Utils;

namespace RLNet.Policies;

/// <summary>
/// A diagonal Gaussian over a continuous action vector, parameterised by a mean and a log
/// standard deviation per dimension.
/// </summary>
/// <remarks>
/// <para>
/// The continuous counterpart of <see cref="CategoricalPolicy"/>, used by PPO and A2C on
/// continuous actions. Unlike SAC's policy it is <em>not</em> squashed through a <c>tanh</c>:
/// on-policy methods only ever need the log-probability of an action that was actually sampled,
/// so the action is drawn from the plain Gaussian, stored unclipped, and clipped to the bounds
/// only on its way into the environment. That is what the reference implementations do, and it
/// keeps the density — and every gradient below — exact.
/// </para>
/// <para>
/// The log standard deviation is usually a free parameter vector rather than a network output.
/// A state-dependent spread gives the policy a way to become confident in states it has seen
/// little of, and on-policy methods, which cannot revisit old data, are the worst placed to
/// recover from that.
/// </para>
/// </remarks>
public static class GaussianPolicy
{
    /// <summary><c>½·ln(2π)</c>, the per-dimension constant in the Gaussian log-density.</summary>
    private const float HalfLogTwoPi = 0.9189385f;

    /// <summary>
    /// Samples an action and returns its log-probability. With <paramref name="deterministic"/>
    /// the mean is returned instead.
    /// </summary>
    public static float Sample(
        ReadOnlySpan<float> mean,
        ReadOnlySpan<float> logStd,
        Span<float> action,
        FastRandom random,
        bool deterministic)
    {
        for (int i = 0; i < mean.Length; i++)
            action[i] = deterministic ? mean[i] : mean[i] + MathF.Exp(logStd[i]) * random.NextGaussian();

        return LogProbability(mean, logStd, action);
    }

    /// <summary>Log-density of <paramref name="action"/>, summed over dimensions.</summary>
    public static float LogProbability(ReadOnlySpan<float> mean, ReadOnlySpan<float> logStd, ReadOnlySpan<float> action)
    {
        float total = 0f;
        for (int i = 0; i < mean.Length; i++)
        {
            float z = (action[i] - mean[i]) * MathF.Exp(-logStd[i]);
            total += -0.5f * z * z - logStd[i] - HalfLogTwoPi;
        }
        return total;
    }

    /// <summary>Differential entropy, in nats. Depends only on the spread.</summary>
    public static float Entropy(ReadOnlySpan<float> logStd)
    {
        float total = 0f;
        for (int i = 0; i < logStd.Length; i++) total += logStd[i] + 0.5f + HalfLogTwoPi;
        return total;
    }

    /// <summary>
    /// Accumulates the gradient of <c>coefficient · log π(action)</c> with respect to the mean
    /// and the log standard deviation.
    /// </summary>
    /// <remarks>
    /// With <c>z = (a − μ)/σ</c>: <c>∂ log π / ∂μ = z/σ</c> and <c>∂ log π / ∂ log σ = z² − 1</c>.
    /// Read as rules: move the mean toward an action that turned out well, and widen the spread if
    /// that action lay more than one standard deviation out — narrow it if it lay closer.
    /// </remarks>
    public static void AccumulateLogProbabilityGradient(
        Span<float> meanGradient,
        Span<float> logStdGradient,
        ReadOnlySpan<float> mean,
        ReadOnlySpan<float> logStd,
        ReadOnlySpan<float> action,
        float coefficient)
    {
        for (int i = 0; i < mean.Length; i++)
        {
            float inverseStd = MathF.Exp(-logStd[i]);
            float z = (action[i] - mean[i]) * inverseStd;
            meanGradient[i] += coefficient * z * inverseStd;
            logStdGradient[i] += coefficient * (z * z - 1f);
        }
    }

    /// <summary>
    /// Accumulates the gradient of <c>−coefficient · H(π)</c> with respect to the log standard
    /// deviation — the entropy bonus as it appears in a loss.
    /// </summary>
    /// <remarks><c>∂H/∂ log σ = 1</c> per dimension, so the bonus is a constant push toward a wider spread.</remarks>
    public static void AccumulateEntropyGradient(Span<float> logStdGradient, float coefficient)
    {
        for (int i = 0; i < logStdGradient.Length; i++) logStdGradient[i] -= coefficient;
    }
}
