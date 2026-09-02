// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Runtime.InteropServices;
using RLNet.Agents;
using RLNet.Environments.Classic;
using RLNet.Environments.Control;
using RLNet.Training;
using Xunit;

namespace RLNet.Tests;

/// <summary>
/// Checks that a trained policy survives a round trip through bytes and back.
/// </summary>
/// <remarks>
/// This is the flow the getting-started guide documents, so it is worth a test that runs it end to
/// end rather than one that only compares arrays. A policy that exports and imports without error
/// but scores differently afterwards would pass the weaker test and fail every reader.
/// </remarks>
public class PersistenceTests
{
    [Fact]
    public void TrainedDiscretePolicySurvivesAByteRoundTrip()
    {
        var environment = new CartPoleEnvironment();
        var agent = new PpoAgent(
            environment.ObservationSpace, environment.ActionSpace,
            new PpoOptions { RolloutLength = 512, Epochs = 4 }, seed: 5);

        Trainer.Train(environment, agent,
            new TrainingOptions { MaxSteps = 12_000, MaxEpisodes = int.MaxValue, Seed = 5 });

        float before = Trainer.Evaluate(environment, agent, episodes: 10, seed: 4242);

        // Exactly the documented path: parameters to bytes, bytes back to parameters.
        byte[] bytes = MemoryMarshal.AsBytes(agent.ExportParameters().AsSpan()).ToArray();
        float[] restoredParameters = MemoryMarshal.Cast<byte, float>(bytes).ToArray();

        var restored = new PpoAgent(
            environment.ObservationSpace, environment.ActionSpace,
            new PpoOptions { RolloutLength = 512, Epochs = 4 }, seed: 99);

        restored.ImportParameters(restoredParameters);

        float after = Trainer.Evaluate(environment, restored, episodes: 10, seed: 4242);

        // Deterministic evaluation of identical parameters must give an identical score. Anything
        // else means the export missed part of the policy.
        Assert.Equal(before, after, 3);
    }

    [Fact]
    public void TrainedContinuousPolicySurvivesARoundTrip()
    {
        var environment = new PendulumEnvironment();
        var agent = new Td3Agent(
            environment.ObservationSpace, environment.ActionSpace,
            new Td3Options { HiddenSizes = [64, 64], BatchSize = 64, LearningStarts = 300 }, seed: 7);

        Trainer.Train(environment, agent,
            new TrainingOptions { MaxSteps = 4_000, MaxEpisodes = int.MaxValue, Seed = 7 });

        float before = Trainer.Evaluate(environment, agent, episodes: 5, seed: 555);

        var restored = new Td3Agent(
            environment.ObservationSpace, environment.ActionSpace,
            new Td3Options { HiddenSizes = [64, 64], BatchSize = 64, LearningStarts = 300 }, seed: 123);

        restored.ImportParameters(agent.ExportParameters());

        float after = Trainer.Evaluate(environment, restored, episodes: 5, seed: 555);
        Assert.Equal(before, after, 3);
    }

    [Fact]
    public void ImportRejectsAMismatchedPolicy()
    {
        // Loading a policy into a differently-shaped network is the common mistake, and it must
        // fail loudly rather than silently reading garbage into half the weights.
        var environment = new CartPoleEnvironment();

        var small = new PpoAgent(environment.ObservationSpace, environment.ActionSpace,
            new PpoOptions { HiddenSizes = [32, 32] }, seed: 1);
        var large = new PpoAgent(environment.ObservationSpace, environment.ActionSpace,
            new PpoOptions { HiddenSizes = [128, 128] }, seed: 1);

        Assert.Throws<ArgumentException>(() => large.ImportParameters(small.ExportParameters()));
    }
}
