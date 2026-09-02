// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace RLNet.Neural;

/// <summary>Elementwise nonlinearity applied after a dense layer.</summary>
/// <remarks>
/// The list is short on purpose. Every entry has an <em>exact</em> derivative expressible from
/// the layer's output alone, which is what lets a layer cache one buffer per forward pass
/// instead of two — and activation memory is what dominates when a PPO update pushes a
/// 2048-step rollout through the network in a single batch. A nonlinearity that needed its
/// pre-activation back (GELU, SiLU) would double that buffer for no measurable gain on
/// networks this small.
/// </remarks>
public enum Activation
{
    /// <summary>No nonlinearity. Used on output heads that emit Q-values, logits or a state value.</summary>
    Linear,

    /// <summary>Rectified linear unit. The default for hidden layers in DQN and PPO.</summary>
    ReLU,

    /// <summary>Hyperbolic tangent. Preferred for continuous-control actors; bounded output keeps early policies tame.</summary>
    Tanh,
}

/// <summary>Forward and backward passes for each <see cref="Activation"/>.</summary>
public static class Activations
{
    /// <summary>Applies the nonlinearity in place.</summary>
    public static void Forward(Activation activation, Span<float> values)
    {
        switch (activation)
        {
            case Activation.Linear:
                break;

            case Activation.ReLU:
                for (int i = 0; i < values.Length; i++)
                    if (values[i] < 0f) values[i] = 0f;
                break;

            case Activation.Tanh:
                for (int i = 0; i < values.Length; i++)
                    values[i] = MathF.Tanh(values[i]);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(activation));
        }
    }

    /// <summary>
    /// Multiplies <paramref name="gradient"/> by the nonlinearity's derivative in place, given
    /// the cached forward <paramref name="output"/>.
    /// </summary>
    public static void Backward(Activation activation, ReadOnlySpan<float> output, Span<float> gradient)
    {
        switch (activation)
        {
            case Activation.Linear:
                break;

            case Activation.ReLU:
                // The output is zero exactly where the input was negative, so it carries all the
                // information the derivative needs.
                for (int i = 0; i < gradient.Length; i++)
                    if (output[i] <= 0f) gradient[i] = 0f;
                break;

            case Activation.Tanh:
                for (int i = 0; i < gradient.Length; i++)
                    gradient[i] *= 1f - output[i] * output[i];
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(activation));
        }
    }
}
