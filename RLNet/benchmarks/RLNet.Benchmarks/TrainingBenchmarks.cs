// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using RLNet.Agents;
using RLNet.Buffers;
using RLNet.Environments;
using RLNet.Environments.Classic;
using RLNet.Environments.Control;
using RLNet.Utils;

namespace RLNet.Benchmarks;

/// <summary>
/// Times raw environment throughput, with no agent attached.
/// </summary>
/// <remarks>
/// This is the ceiling every agent runs against. If an environment cannot step faster than the
/// network can be evaluated, no amount of work on the network moves the needle — and the answer
/// is a vectorised environment, not a faster layer.
/// </remarks>
[MemoryDiagnoser]
[HideColumns("Error", "StdDev", "Median", "RatioSD")]
[SimpleJob(warmupCount: 2, iterationCount: 5, invocationCount: 1, launchCount: 1)]
public class EnvironmentBenchmarks
{
    private const int Steps = 10_000;

    private IDiscreteEnvironment _cartPole = null!;
    private IDiscreteEnvironment _gridWorld = null!;
    private IDiscreteEnvironment _lunarLander = null!;
    private IContinuousEnvironment _pendulum = null!;
    private float[] _continuousAction = null!;

    [GlobalSetup]
    public void Setup()
    {
        _cartPole = new CartPoleEnvironment();
        _gridWorld = new GridWorldEnvironment();
        _lunarLander = new LunarLanderEnvironment();
        _pendulum = new PendulumEnvironment();
        _continuousAction = [0.5f];
    }

    [Benchmark(Baseline = true, Description = "CartPole")]
    public void CartPole() => StepDiscrete(_cartPole);

    [Benchmark(Description = "GridWorld")]
    public void GridWorld() => StepDiscrete(_gridWorld);

    [Benchmark(Description = "LunarLander")]
    public void LunarLander() => StepDiscrete(_lunarLander);

    [Benchmark(Description = "Pendulum (continuous)")]
    public void Pendulum()
    {
        _pendulum.Reset(1);
        for (int i = 0; i < Steps; i++)
            if (_pendulum.Step(_continuousAction).Done) _pendulum.Reset();
    }

    private static void StepDiscrete(IDiscreteEnvironment environment)
    {
        environment.Reset(1);
        int actions = environment.ActionSpace.FlatSize == 1
            ? ((Spaces.DiscreteSpace)environment.ActionSpace).Count
            : 1;

        for (int i = 0; i < Steps; i++)
            if (environment.Step(i % actions).Done) environment.Reset();
    }
}

/// <summary>Times replay sampling, the other per-gradient-step cost besides the network.</summary>
/// <remarks>
/// The comparison that matters is uniform against prioritised. Prioritised replay learns from
/// far fewer transitions, but each draw walks a tree and each update writes one back — this is
/// how much that costs, so the trade can be made on numbers rather than folklore.
/// </remarks>
[MemoryDiagnoser]
[HideColumns("Error", "StdDev", "Median", "RatioSD")]
public class ReplayBenchmarks
{
    private UniformReplayBuffer _uniform = null!;
    private PrioritizedReplayBuffer _prioritized = null!;
    private ReplayBatch _batch = null!;
    private FastRandom _random = null!;
    private int[] _indices = null!;
    private float[] _errors = null!;

    /// <summary>Stored transitions. 1,000,000 is a realistic Atari-scale buffer.</summary>
    [Params(1_000_000)]
    public int Capacity { get; set; }

    [Params(256)]
    public int BatchSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        const int observationSize = 8;
        _random = new FastRandom(3);

        _uniform = new UniformReplayBuffer(Capacity, observationSize, 1);
        _prioritized = new PrioritizedReplayBuffer(Capacity, observationSize, 1);

        var observation = new float[observationSize];
        for (int i = 0; i < Capacity; i++)
        {
            for (int j = 0; j < observationSize; j++) observation[j] = _random.NextGaussian();
            _uniform.AddDiscrete(observation, i % 4, _random.NextSingle(), observation, false);
            _prioritized.AddDiscrete(observation, i % 4, _random.NextSingle(), observation, false);
        }

        _batch = new ReplayBatch(BatchSize, observationSize, 1);
        _indices = new int[BatchSize];
        _errors = new float[BatchSize];
        for (int i = 0; i < BatchSize; i++) _errors[i] = _random.NextSingle();
    }

    [Benchmark(Baseline = true, Description = "Uniform sample")]
    public void UniformSample() => _uniform.Sample(BatchSize, _batch, _random);

    [Benchmark(Description = "Prioritised sample")]
    public void PrioritizedSample() => _prioritized.Sample(BatchSize, _batch, _random);

    /// <summary>Sampling plus the priority write-back, which is the whole per-step cost.</summary>
    [Benchmark(Description = "Prioritised sample + update")]
    public void PrioritizedRoundTrip()
    {
        _prioritized.Sample(BatchSize, _batch, _random);
        _batch.Indices.AsSpan(0, BatchSize).CopyTo(_indices);
        _prioritized.UpdatePriorities(_indices, _errors);
    }
}

/// <summary>
/// Times a complete training loop per algorithm, in environment steps per second.
/// </summary>
/// <remarks>
/// The headline number for the library, and the one a user actually feels. It folds together
/// environment stepping, action selection, buffering and gradient steps, so it is the only
/// measurement that reflects where the time really goes.
///
/// A short job on purpose: one iteration here is thousands of real gradient steps rather than a
/// microbenchmark, so a default run would take an hour, and the run-to-run spread on something
/// that long is already tiny enough that the extra iterations buy no precision worth the wait.
/// </remarks>
[MemoryDiagnoser]
[HideColumns("Error", "StdDev", "Median", "RatioSD")]
[SimpleJob(warmupCount: 1, iterationCount: 3, invocationCount: 1, launchCount: 1)]
public class AgentBenchmarks
{
    private const int Steps = 2_000;

    [Benchmark(Baseline = true, Description = "Q-learning / GridWorld")]
    public void QLearning()
    {
        var environment = new GridWorldEnvironment();
        var agent = new QTableAgent(environment.ActionSpace, StateDiscretizer.OneHot(), seed: 1);
        RunDiscrete(environment, agent);
    }

    [Benchmark(Description = "DQN / CartPole")]
    public void Dqn()
    {
        var environment = new CartPoleEnvironment();
        var agent = new DqnAgent(
            environment.ObservationSpace, environment.ActionSpace,
            new DqnOptions { LearningStarts = 500 }, seed: 1);
        RunDiscrete(environment, agent);
    }

    [Benchmark(Description = "DQN uniform replay / CartPole")]
    public void DqnUniform()
    {
        var environment = new CartPoleEnvironment();
        var agent = new DqnAgent(
            environment.ObservationSpace, environment.ActionSpace,
            new DqnOptions { LearningStarts = 500, PrioritizedReplay = false }, seed: 1);
        RunDiscrete(environment, agent);
    }

    [Benchmark(Description = "PPO / CartPole")]
    public void Ppo()
    {
        var environment = new CartPoleEnvironment();
        var agent = new PpoAgent(
            environment.ObservationSpace, environment.ActionSpace,
            new PpoOptions { RolloutLength = 1_024 }, seed: 1);
        RunDiscrete(environment, agent);
    }

    [Benchmark(Description = "A2C / CartPole")]
    public void A2C()
    {
        var environment = new CartPoleEnvironment();
        var agent = new A2CAgent(environment.ObservationSpace, environment.ActionSpace, seed: 1);
        RunDiscrete(environment, agent);
    }

    [Benchmark(Description = "SAC / Pendulum")]
    public void Sac()
    {
        var environment = new PendulumEnvironment();
        var agent = new SacAgent(
            environment.ObservationSpace, environment.ActionSpace,
            new SacOptions { LearningStarts = 500 }, seed: 1);
        RunContinuous(environment, agent);
    }

    [Benchmark(Description = "TD3 / Pendulum")]
    public void Td3()
    {
        var environment = new PendulumEnvironment();
        var agent = new Td3Agent(
            environment.ObservationSpace, environment.ActionSpace,
            new Td3Options { LearningStarts = 500 }, seed: 1);
        RunContinuous(environment, agent);
    }

    private static void RunDiscrete(IDiscreteEnvironment environment, IDiscreteAgent agent)
    {
        var observation = new float[environment.ObservationSpace.FlatSize];
        environment.Reset(1);

        for (int i = 0; i < Steps; i++)
        {
            environment.Observation.CopyTo(observation);
            int action = agent.SelectAction(observation);
            var step = environment.Step(action);
            agent.Observe(observation, action, step.Reward, environment.Observation, step.Terminated, step.Truncated);

            if (step.Done)
            {
                agent.OnEpisodeEnd();
                environment.Reset();
            }
        }
    }

    private static void RunContinuous(IContinuousEnvironment environment, IContinuousAgent agent)
    {
        var observation = new float[environment.ObservationSpace.FlatSize];
        var action = new float[environment.ActionSpace.FlatSize];
        environment.Reset(1);

        for (int i = 0; i < Steps; i++)
        {
            environment.Observation.CopyTo(observation);
            agent.SelectAction(observation, action);
            var step = environment.Step(action);
            agent.Observe(observation, action, step.Reward, environment.Observation, step.Terminated, step.Truncated);

            if (step.Done)
            {
                agent.OnEpisodeEnd();
                environment.Reset();
            }
        }
    }
}
