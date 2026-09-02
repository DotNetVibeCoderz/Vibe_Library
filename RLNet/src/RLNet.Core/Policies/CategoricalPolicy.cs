// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Utils;

namespace RLNet.Policies;

/// <summary>
/// A distribution over a finite action set, parameterised by logits.
/// </summary>
/// <remarks>
/// <para>
/// Shared by A2C and PPO, and the reason both are as short as they are: the awkward part of a
/// discrete policy gradient is the chain rule from a loss on the sampled action back to the
/// logits, and it is the same derivation for both algorithms.
/// </para>
/// <para>
/// Everything here writes into caller-supplied spans. A policy is evaluated once per environment
/// step during collection and again for every sample in every minibatch of every epoch during an
/// update, so an allocation per call would be the dominant cost of PPO.
/// </para>
/// </remarks>
public static class CategoricalPolicy
{
    /// <summary>
    /// Converts logits to probabilities, samples an action, and reports its log-probability.
    /// </summary>
    /// <param name="logits">Raw network output. Not modified.</param>
    /// <param name="probabilities">Receives the softmax of <paramref name="logits"/>.</param>
    /// <param name="deterministic">Take the most likely action instead of sampling.</param>
    /// <param name="logProbability">Log-probability of the chosen action.</param>
    public static int Sample(
        ReadOnlySpan<float> logits,
        Span<float> probabilities,
        FastRandom random,
        bool deterministic,
        out float logProbability)
    {
        logits.CopyTo(probabilities);
        SimdOps.SoftmaxInPlace(probabilities);

        int action;
        if (deterministic)
        {
            SimdOps.Max(probabilities, out action);
        }
        else
        {
            action = random.SampleCategorical(probabilities);
        }

        // Floored before the logarithm: a converged policy can drive an action's probability to
        // exactly zero in float, and a -infinity log-probability poisons every ratio computed
        // from it for the rest of training.
        logProbability = MathF.Log(MathF.Max(probabilities[action], 1e-8f));
        return action;
    }

    /// <summary>Shannon entropy of a probability vector, in nats.</summary>
    /// <remarks>
    /// The headline diagnostic for an on-policy run. It starts near <c>ln(actionCount)</c> and
    /// falls as the policy commits; a collapse to near zero early is the signature of premature
    /// convergence, and a value that never falls means the agent is not learning at all.
    /// </remarks>
    public static float Entropy(ReadOnlySpan<float> probabilities)
    {
        float entropy = 0f;
        for (int i = 0; i < probabilities.Length; i++)
        {
            float p = probabilities[i];
            if (p > 1e-8f) entropy -= p * MathF.Log(p);
        }
        return entropy;
    }

    /// <summary>
    /// Accumulates the gradient of <c>coefficient * log π(action)</c> with respect to the logits.
    /// </summary>
    /// <remarks>
    /// The identity behind every discrete policy gradient:
    /// <c>∂ log π(a) / ∂ logit_j = [j == a] - π(j)</c>. Reading it as a rule: raise the taken
    /// action's logit, lower every logit in proportion to how likely it already was. A positive
    /// <paramref name="coefficient"/> makes the action more likely, a negative one less.
    /// </remarks>
    public static void AccumulateLogProbabilityGradient(
        Span<float> gradient,
        ReadOnlySpan<float> probabilities,
        int action,
        float coefficient)
    {
        for (int j = 0; j < probabilities.Length; j++)
            gradient[j] += coefficient * ((j == action ? 1f : 0f) - probabilities[j]);
    }

    /// <summary>
    /// Accumulates the gradient of <c>-coefficient * H(π)</c> with respect to the logits, the
    /// entropy bonus as it appears in a loss.
    /// </summary>
    /// <remarks>
    /// From <c>∂H/∂logit_j = -π_j (log π_j + H)</c>. Since the bonus enters the loss negatively —
    /// the optimiser minimises, and high entropy is wanted — the loss gradient is
    /// <c>+coefficient * π_j (log π_j + H)</c>. Without this term a policy tends to collapse onto
    /// whichever action looked best early and stops exploring long before it has seen enough to
    /// justify that.
    /// </remarks>
    public static void AccumulateEntropyGradient(
        Span<float> gradient,
        ReadOnlySpan<float> probabilities,
        float entropy,
        float coefficient)
    {
        for (int j = 0; j < probabilities.Length; j++)
        {
            float p = probabilities[j];
            if (p <= 1e-8f) continue;
            gradient[j] += coefficient * p * (MathF.Log(p) + entropy);
        }
    }
}
