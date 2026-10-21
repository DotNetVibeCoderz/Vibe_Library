// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Agents;
using RLNet.Persistence;
using RLNet.Training;
using Xunit;

namespace RLNet.Tests;

/// <summary>
/// Every pairing the catalog advertises has to actually run — the console offers each of them in
/// its picker, so an advertised pair that throws is a broken menu item.
/// </summary>
public class CatalogTests
{
    public static TheoryData<string, Algorithm> SingleAgentPairs()
    {
        var data = new TheoryData<string, Algorithm>();
        foreach (var entry in Catalog.Environments.Where(e => e.Kind != EnvironmentKind.MultiAgent))
            foreach (var algorithm in entry.SupportedAlgorithms)
                data.Add(entry.Name, algorithm);
        return data;
    }

    [Theory]
    [MemberData(nameof(SingleAgentPairs))]
    public void EveryAdvertisedPairTrainsAndCheckpoints(string name, Algorithm algorithm)
    {
        var entry = Catalog.Find(name);
        IAgent agent;

        if (entry.Kind == EnvironmentKind.Discrete)
        {
            var environment = Catalog.CreateDiscrete(name);
            var discrete = Catalog.CreateAgent(algorithm, environment.ObservationSpace, environment.ActionSpace, seed: 1);
            Trainer.Train(environment, discrete, new TrainingOptions { MaxSteps = 300, MaxEpisodes = int.MaxValue, Seed = 1 });
            agent = discrete;
        }
        else
        {
            var environment = Catalog.CreateContinuous(name);
            var continuous = Catalog.CreateAgent(algorithm, environment.ObservationSpace, environment.ActionSpace, seed: 1);
            Trainer.Train(environment, continuous, new TrainingOptions { MaxSteps = 300, MaxEpisodes = int.MaxValue, Seed = 1 });
            agent = continuous;
        }

        Assert.Equal(300L, agent.Metrics.StepCount);

        // Every agent the catalog builds supports checkpoints.
        var checkpointable = Assert.IsAssignableFrom<ICheckpointable>(agent);
        using var stream = new MemoryStream();
        Checkpoint.Save(stream, checkpointable);
        Assert.True(stream.Length > 0);
    }

    [Fact]
    public void ContinuousEnvironmentsOfferTheOnPolicyAlgorithms()
    {
        foreach (var entry in Catalog.Environments.Where(e => e.Kind == EnvironmentKind.Continuous))
        {
            Assert.Contains(Algorithm.Ppo, entry.SupportedAlgorithms);
            Assert.Contains(Algorithm.A2C, entry.SupportedAlgorithms);
        }
    }

    [Fact]
    public void OffPolicyAlgorithmsHaveNoBatchedForm()
    {
        var environment = Catalog.CreateDiscrete("CartPole");
        Assert.Throws<ArgumentException>(() =>
            Catalog.CreateVectorAgent(Algorithm.Dqn, environment.ObservationSpace, environment.ActionSpace));
    }
}
