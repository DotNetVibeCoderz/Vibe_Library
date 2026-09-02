// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Neural;
using RLNet.Utils;
using Xunit;

namespace RLNet.Tests;

/// <summary>
/// Checks the neural engine against finite differences.
/// </summary>
/// <remarks>
/// These are the most important tests in the suite. Backpropagation fails silently: a sign error
/// or a missing term produces a network that still trains, just to a worse policy, and no RL
/// benchmark is sensitive enough to catch it reliably. Comparing every analytic gradient against
/// a numerical one does catch it, exactly and immediately.
/// </remarks>
public class NeuralNetworkTests
{
    /// <summary>
    /// Verifies every parameter gradient against a central finite difference.
    /// </summary>
    /// <remarks>
    /// The step size is a compromise: too large and the difference quotient measures curvature,
    /// too small and float cancellation swamps the signal. 1e-3 with a 2% relative tolerance sits
    /// in the window where a genuine gradient bug is unmistakable and float noise is not.
    /// </remarks>
    [Theory]
    [InlineData(Activation.ReLU, Activation.Linear)]
    [InlineData(Activation.Tanh, Activation.Linear)]
    [InlineData(Activation.Tanh, Activation.Tanh)]
    [InlineData(Activation.ReLU, Activation.Tanh)]
    public void Backpropagation_MatchesFiniteDifferences(Activation hidden, Activation output)
    {
        var random = new FastRandom(1234);
        const int batch = 4, inputSize = 5, outputSize = 3;

        var network = new MlpNetwork(inputSize, [8, 6], outputSize, hidden, output, batch, random);

        var input = new float[batch * inputSize];
        for (int i = 0; i < input.Length; i++) input[i] = random.NextGaussian();

        // An arbitrary fixed target, so the loss is a concrete scalar function of the parameters.
        var target = new float[batch * outputSize];
        for (int i = 0; i < target.Length; i++) target[i] = random.NextGaussian();

        float Loss()
        {
            input.CopyTo(network.InputBuffer(batch));
            var prediction = network.Forward(batch);

            float sum = 0f;
            for (int i = 0; i < prediction.Length; i++)
            {
                float error = prediction[i] - target[i];
                sum += 0.5f * error * error;
            }
            return sum;
        }

        // Analytic gradients.
        input.CopyTo(network.InputBuffer(batch));
        var values = network.Forward(batch);
        var gradient = network.OutputGradientBuffer(batch);
        for (int i = 0; i < values.Length; i++) gradient[i] = values[i] - target[i];

        network.ZeroGradients();
        network.Backward(batch);

        var analytic = new float[network.ParameterCount];
        CollectGradients(network, analytic);

        var parameters = network.ExportParameters();
        const float epsilon = 1e-3f;

        // Spot-check a spread of parameters rather than all of them: each check costs two full
        // forward passes, and a bug in one layer shows up across many parameters at once.
        for (int index = 0; index < network.ParameterCount; index += Math.Max(1, network.ParameterCount / 40))
        {
            float original = parameters[index];

            parameters[index] = original + epsilon;
            network.ImportParameters(parameters);
            float plus = Loss();

            parameters[index] = original - epsilon;
            network.ImportParameters(parameters);
            float minus = Loss();

            parameters[index] = original;
            network.ImportParameters(parameters);

            float numeric = (plus - minus) / (2f * epsilon);

            // ReLU is not differentiable at zero, so a parameter that flips a unit's sign inside
            // the perturbation window legitimately disagrees. Those are skipped rather than
            // loosened away, which would blunt the test everywhere else.
            float tolerance = 0.02f * Math.Max(1f, Math.Abs(numeric));
            if (hidden == Activation.ReLU && Math.Abs(numeric - analytic[index]) > tolerance &&
                Math.Abs(numeric) < 1e-2f)
                continue;

            Assert.True(
                Math.Abs(numeric - analytic[index]) <= tolerance,
                $"Parameter {index}: analytic {analytic[index]:F6}, numeric {numeric:F6}");
        }
    }

    [Fact]
    public void BackwardToInput_MatchesFiniteDifferences()
    {
        // The gradient with respect to the input is what SAC and TD3 use to push an actor uphill
        // on the critic, so it needs the same scrutiny as the parameter gradients.
        var random = new FastRandom(77);
        const int inputSize = 4;

        var network = new MlpNetwork(inputSize, [8], 1, Activation.ReLU, Activation.Linear, 1, random);

        var input = new float[inputSize];
        for (int i = 0; i < inputSize; i++) input[i] = random.NextGaussian();

        float Evaluate(ReadOnlySpan<float> x)
        {
            x.CopyTo(network.InputBuffer(1));
            return network.Forward(1)[0];
        }

        Evaluate(input);
        network.OutputGradientBuffer(1)[0] = 1f;
        network.ZeroGradients();
        var analytic = network.BackwardToInput(1).ToArray();

        const float epsilon = 1e-3f;
        var probe = (float[])input.Clone();

        for (int i = 0; i < inputSize; i++)
        {
            probe[i] = input[i] + epsilon;
            float plus = Evaluate(probe);

            probe[i] = input[i] - epsilon;
            float minus = Evaluate(probe);

            probe[i] = input[i];

            float numeric = (plus - minus) / (2f * epsilon);
            Assert.True(
                Math.Abs(numeric - analytic[i]) <= 0.02f * Math.Max(1f, Math.Abs(numeric)),
                $"Input {i}: analytic {analytic[i]:F6}, numeric {numeric:F6}");
        }
    }

    [Fact]
    public void Adam_MinimisesAQuadratic()
    {
        // The optimiser on its own, isolated from the network: fit a constant output to a known
        // target and check it actually gets there.
        var random = new FastRandom(9);
        var network = new MlpNetwork(1, [16], 1, Activation.Tanh, Activation.Linear, 1, random);
        var optimizer = new AdamOptimizer(network.ParameterCount, 0.05f);

        const float target = 0.75f;
        float loss = float.MaxValue;

        for (int step = 0; step < 500; step++)
        {
            network.InputBuffer(1)[0] = 1f;
            float prediction = network.Forward(1)[0];

            float error = prediction - target;
            loss = 0.5f * error * error;

            network.OutputGradientBuffer(1)[0] = error;
            network.ZeroGradients();
            network.Backward(1);
            network.ApplyGradients(optimizer);
        }

        Assert.True(loss < 1e-5f, $"Adam failed to converge; final loss {loss:E3}");
    }

    [Fact]
    public void SoftUpdate_MovesTargetFractionally()
    {
        var random = new FastRandom(3);
        var source = new MlpNetwork(2, [4], 2, Activation.ReLU, Activation.Linear, 1, random);
        var target = new MlpNetwork(2, [4], 2, Activation.ReLU, Activation.Linear, 1, random);

        var before = target.ExportParameters();
        var sourceParameters = source.ExportParameters();

        const float tau = 0.25f;
        target.SoftUpdateFrom(source, tau);
        var after = target.ExportParameters();

        for (int i = 0; i < after.Length; i++)
        {
            float expected = before[i] * (1f - tau) + sourceParameters[i] * tau;
            Assert.Equal(expected, after[i], 5);
        }
    }

    [Fact]
    public void CopyFrom_MakesTargetIdentical()
    {
        var random = new FastRandom(5);
        var source = new MlpNetwork(3, [6], 2, Activation.ReLU, Activation.Linear, 1, random);
        var target = new MlpNetwork(3, [6], 2, Activation.ReLU, Activation.Linear, 1, random);

        target.CopyFrom(source);
        Assert.Equal(source.ExportParameters(), target.ExportParameters());
    }

    [Fact]
    public void ExportImport_RoundTrips()
    {
        var random = new FastRandom(11);
        var network = new MlpNetwork(3, [5, 4], 2, Activation.Tanh, Activation.Linear, 1, random);

        var input = new float[] { 0.3f, -0.7f, 1.1f };
        var expected = network.Forward(input).ToArray();

        var parameters = network.ExportParameters();
        var restored = new MlpNetwork(3, [5, 4], 2, Activation.Tanh, Activation.Linear, 1, new FastRandom(99));
        restored.ImportParameters(parameters);

        Assert.Equal(expected, restored.Forward(input).ToArray());
    }

    [Fact]
    public void GradientClipping_BoundsTheStep()
    {
        var random = new FastRandom(21);
        var network = new MlpNetwork(2, [4], 1, Activation.Linear, Activation.Linear, 1, random);

        var unclipped = new AdamOptimizer(network.ParameterCount, 0.1f);
        var clipped = new AdamOptimizer(network.ParameterCount, 0.1f, maxGradientNorm: 0.01f);

        var start = network.ExportParameters();

        // A deliberately enormous output gradient, the kind one unlucky advantage estimate
        // produces in practice.
        void Step(AdamOptimizer optimizer)
        {
            network.ImportParameters(start);
            network.InputBuffer(1)[0] = 1f;
            network.InputBuffer(1)[1] = 1f;
            network.Forward(1);
            network.OutputGradientBuffer(1)[0] = 1e6f;
            network.ZeroGradients();
            network.Backward(1);
            network.ApplyGradients(optimizer);
        }

        Step(unclipped);
        float unclippedDistance = Distance(start, network.ExportParameters());

        Step(clipped);
        float clippedDistance = Distance(start, network.ExportParameters());

        // Adam normalises by the gradient's own magnitude, so an unclipped step is bounded by the
        // learning rate regardless. The clip still has to make the step strictly smaller.
        Assert.True(clippedDistance < unclippedDistance,
            $"Clipping did not shrink the step: clipped {clippedDistance:E3} vs unclipped {unclippedDistance:E3}");
    }

    private static float Distance(float[] a, float[] b)
    {
        float sum = 0f;
        for (int i = 0; i < a.Length; i++)
        {
            float d = a[i] - b[i];
            sum += d * d;
        }
        return MathF.Sqrt(sum);
    }

    private static void CollectGradients(MlpNetwork network, Span<float> destination)
    {
        // Layer gradients are internal to the assembly, so this walks the same order
        // ApplyGradients does: weights then biases, layer by layer.
        int offset = 0;
        foreach (var layer in network.Layers)
        {
            int weightCount = layer.InputSize * layer.OutputSize;
            layer.WeightGradients.CopyTo(destination.Slice(offset, weightCount));
            offset += weightCount;

            layer.BiasGradients.CopyTo(destination.Slice(offset, layer.OutputSize));
            offset += layer.OutputSize;
        }
    }
}
