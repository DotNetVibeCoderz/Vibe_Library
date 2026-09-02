// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Numerics;

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
        /// <remarks>
        /// <para>
        /// Vectorised, because Adam's cost is independent of batch size: it touches every parameter
        /// in the network whatever the minibatch was. At a batch of 1 on a 256-wide network that
        /// makes the optimiser step around 98% of the whole gradient step — the forward and backward
        /// passes scale down with the batch and this does not.
        /// </para>
        /// <para>
        /// The bias corrections are folded into two constants before the loop, which turns two
        /// divisions per parameter into none:
        /// <c>lr·(m/c1) / (√(v/c2) + ε)</c> is the same value as
        /// <c>(lr/c1)·m / (√v·(1/√c2) + ε)</c>, since c2 is strictly positive.
        /// </para>
        /// </remarks>
        public void Apply(Span<float> parameters, Span<float> gradients)
        {
            var m = _adam._m.AsSpan(_offset, parameters.Length);
            var v = _adam._v.AsSpan(_offset, parameters.Length);

            float b1 = _adam.Beta1, b2 = _adam.Beta2, eps = _adam.Epsilon;
            float oneMinusB1 = 1f - b1, oneMinusB2 = 1f - b2;

            float step = _adam.LearningRate / _correction1;
            float invSqrtCorrection2 = 1f / MathF.Sqrt(_correction2);

            int width = Vector<float>.Count;
            int i = 0;

            var vScale = new Vector<float>(_scale);
            var vB1 = new Vector<float>(b1);
            var vB2 = new Vector<float>(b2);
            var vOneMinusB1 = new Vector<float>(oneMinusB1);
            var vOneMinusB2 = new Vector<float>(oneMinusB2);
            var vStep = new Vector<float>(step);
            var vInvSqrtC2 = new Vector<float>(invSqrtCorrection2);
            var vEps = new Vector<float>(eps);

            for (; i <= parameters.Length - width; i += width)
            {
                var g = new Vector<float>(gradients.Slice(i, width)) * vScale;

                var mi = vB1 * new Vector<float>(m.Slice(i, width)) + vOneMinusB1 * g;
                var vi = vB2 * new Vector<float>(v.Slice(i, width)) + vOneMinusB2 * g * g;

                mi.CopyTo(m.Slice(i, width));
                vi.CopyTo(v.Slice(i, width));

                var update = vStep * mi / (Vector.SquareRoot(vi) * vInvSqrtC2 + vEps);
                (new Vector<float>(parameters.Slice(i, width)) - update).CopyTo(parameters.Slice(i, width));
            }

            for (; i < parameters.Length; i++)
            {
                float g = gradients[i] * _scale;

                m[i] = b1 * m[i] + oneMinusB1 * g;
                v[i] = b2 * v[i] + oneMinusB2 * g * g;

                parameters[i] -= step * m[i] / (MathF.Sqrt(v[i]) * invSqrtCorrection2 + eps);
            }

            _offset += parameters.Length;
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
