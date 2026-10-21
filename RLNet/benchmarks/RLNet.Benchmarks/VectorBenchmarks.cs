// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using BenchmarkDotNet.Attributes;
using RLNet.Agents;
using RLNet.Environments;
using RLNet.Environments.Classic;
using RLNet.Environments.Control;
using RLNet.Training;

namespace RLNet.Benchmarks;

/// <summary>
/// Times on-policy training across one to sixteen environment copies, for a fixed number of
/// total environment steps.
/// </summary>
/// <remarks>
/// <para>
/// Every row does the same amount of learning: the rollout length is the number of transitions
/// per update whatever their source, so the gradient work is identical across the column and the
/// only thing that changes is how many network passes collection takes. One copy is the
/// single-environment path; compare each benchmark across the <c>Copies</c> groups.
/// </para>
/// <para>
/// Sequential stepping throughout. These environments step in well under a microsecond, so the
/// gain here is all in the network, not in the environments.
/// </para>
/// </remarks>
[MemoryDiagnoser]
[HideColumns("Error", "StdDev", "Median", "RatioSD")]
[SimpleJob(warmupCount: 1, iterationCount: 5, invocationCount: 1, launchCount: 1)]
public class VectorBenchmarks
{
    private const int Steps = 16_384;

    [Params(1, 4, 8, 16)]
    public int Copies { get; set; }

    [Benchmark(Baseline = true, Description = "PPO / CartPole")]
    public void PpoCartPole()
    {
        var environment = new DiscreteVectorEnvironment(() => new CartPoleEnvironment(), Copies);
        var agent = new PpoAgent(environment.ObservationSpace, environment.ActionSpace, seed: 1);
        Trainer.Train(environment, agent, new TrainingOptions { MaxSteps = Steps, MaxEpisodes = int.MaxValue, Seed = 1 });
    }

    [Benchmark(Description = "PPO / Pendulum")]
    public void PpoPendulum()
    {
        var environment = new ContinuousVectorEnvironment(() => new PendulumEnvironment(), Copies);
        var agent = Catalog.CreateVectorAgent(Algorithm.Ppo, environment.ObservationSpace, environment.ActionSpace, seed: 1);
        Trainer.Train(environment, agent, new TrainingOptions { MaxSteps = Steps, MaxEpisodes = int.MaxValue, Seed = 1 });
    }

    [Benchmark(Description = "A2C / CartPole")]
    public void A2CCartPole()
    {
        // A2C's 32-step rollout is split across the copies too, so at 16 copies each update
        // sees two steps from each: the same batch size, from sixteen trajectories.
        var environment = new DiscreteVectorEnvironment(() => new CartPoleEnvironment(), Copies);
        var agent = new A2CAgent(environment.ObservationSpace, environment.ActionSpace, seed: 1);
        Trainer.Train(environment, agent, new TrainingOptions { MaxSteps = Steps, MaxEpisodes = int.MaxValue, Seed = 1 });
    }
}
