// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace RLNet.Neural;

/// <summary>Adam optimiser with bias correction and optional global gradient-norm clipping.</summary>
/// <remarks>
/// <para>
/// Adam rather than plain SGD because RL gradients are non-stationary by construction: the data
/// distribution shifts as the policy changes, so a per-parameter adaptive step is not a
/// convenience here, it is what makes the methods work at all. Every published
/// hyper-parameter set for DQN, PPO, SAC and TD3 assumes it.
/// </para>
/// <para>
/// The moment buffers are flat arrays sized to the whole network, indexed by a running offset as
/// the update walks the layers. One allocation each, at construction.
/// </para>
/// </remarks>
public sealed class AdamOptimizer
{
    private readonly float[] _m;
    private readonly float[] _v;
    private int _step;

    /// <summary>Step size.</summary>
    public float LearningRate { get; set; }

    /// <summary>Exponential decay for the first moment.</summary>
    public float Beta1 { get; }

    /// <summary>Exponential decay for the second moment.</summary>
    public float Beta2 { get; }

    /// <summary>Denominator floor, guarding the division by the second-moment root.</summary>
    public float Epsilon { get; }

    /// <summary>Global gradient-norm clip, or 0 to disable.</summary>
    /// <remarks>
    /// On by default for the policy-gradient agents. A single unlucky advantage estimate can
    /// produce a gradient orders of magnitude larger than the rest of the batch, and without a
    /// clip that one step is enough to destroy a policy that took a million steps to learn.
    /// </remarks>
    public float MaxGradientNorm { get; set; }

    public AdamOptimizer(int parameterCount, float learningRate, float beta1 = 0.9f, float beta2 = 0.999f, float epsilon = 1e-8f, float maxGradientNorm = 0f)
    {
        _m = new float[parameterCount];
        _v = new float[parameterCount];
        LearningRate = learningRate;
        Beta1 = beta1;
        Beta2 = beta2;
        Epsilon = epsilon;
        MaxGradientNorm = maxGradientNorm;
    }

    /// <summary>Begins a parameter update, advancing the bias-correction step counter.</summary>
    /// <param name="gradientScale">
    /// Factor applied to every gradient, typically <c>1 / batchSize</c> so that the step size is
    /// independent of how many samples were accumulated, and the global clip scale when one applies.
    /// </param>
    public UpdateScope BeginUpdate(float gradientScale = 1f)
    {
        _step++;
        float correction1 = 1f - MathF.Pow(Beta1, _step);
        float correction2 = 1f - MathF.Pow(Beta2, _step);
        return new UpdateScope(this, gradientScale, correction1, correction2);
    }

    /// <summary>Tracks the running parameter offset across one network-wide update.</summary>
    /// <remarks>
    /// A struct so that walking a dozen layers costs nothing. It is passed by reference
    /// throughout; copying it would silently reset the offset and corrupt the moment estimates.
    /// </remarks>
    public struct UpdateScope
    {
        private readonly AdamOptimizer _adam;
        private readonly float _scale;
        private readonly float _correction1;
        private readonly float _correction2;
        private int _offset;

        internal UpdateScope(AdamOptimizer adam, float scale, float correction1, float correction2)
        {
            _adam = adam;
            _scale = scale;
            _correction1 = correction1;
            _correction2 = correction2;
            _offset = 0;
        }

        /// <summary>Applies one Adam step to a contiguous parameter block and its gradients.</summary>
        public void Apply(Span<float> parameters, Span<float> gradients)
        {
            float[] m = _adam._m, v = _adam._v;
            float lr = _adam.LearningRate, b1 = _adam.Beta1, b2 = _adam.Beta2, eps = _adam.Epsilon;
            int offset = _offset;

            for (int i = 0; i < parameters.Length; i++)
            {
                float g = gradients[i] * _scale;
                int k = offset + i;

                m[k] = b1 * m[k] + (1f - b1) * g;
                v[k] = b2 * v[k] + (1f - b2) * g * g;

                float mHat = m[k] / _correction1;
                float vHat = v[k] / _correction2;
                parameters[i] -= lr * mHat / (MathF.Sqrt(vHat) + eps);
            }

            _offset = offset + parameters.Length;
        }
    }

    /// <summary>Resets both moment estimates and the step counter.</summary>
    public void Reset()
    {
        Array.Clear(_m);
        Array.Clear(_v);
        _step = 0;
    }
}
