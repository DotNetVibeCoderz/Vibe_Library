// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Agents;
using RLNet.Environments.Classic;
using RLNet.Environments.Control;
using RLNet.Environments.MultiAgent;
using RLNet.Training;
using Xunit;

namespace RLNet.Tests;

/// <summary>
/// End-to-end checks that each algorithm actually learns.
/// </summary>
/// <remarks>
/// <para>
/// The gradient tests prove the arithmetic is right; these prove the algorithm around it is.
/// A sign error in an advantage, a target computed from the wrong network, a policy updated on
/// stale log-probabilities — none of those break a gradient check, and all of them produce an
/// agent that trains without improving.
/// </para>
/// <para>
/// The thresholds are deliberately loose, well below what each agent reaches on a full run. The
/// question being asked is "did it learn anything at all", not "did it hit a published score",
/// and a tight threshold on a short seeded run is a test that fails for reasons unrelated to the
/// code. Every run is seeded, so a failure here is reproducible rather than a coin flip.
/// </para>
/// </remarks>
[Trait("Category", "Learning")]
public class LearningTests
{
    [Fact]
    public void QLearning_SolvesGridWorld()
    {
        // The optimal route is 8 moves at -0.1 plus the +10 goal: 9.3. Tabular Q-learning should
        // find it outright, so this threshold is close to optimal rather than merely better than
        // random.
        var environment = new GridWorldEnvironment();
        var agent = new QTableAgent(
            environment.ActionSpace, StateDiscretizer.OneHot(),
            new QTableOptions { LearningRate = 0.2f }, seed: 1);

        Trainer.Train(environment, agent, new TrainingOptions { MaxEpisodes = 1_500, Seed = 1 });

        float score = Trainer.Evaluate(environment, agent, episodes: 20, seed: 500);
        Assert.True(score > 9f, $"Q-learning scored {score:F2} on GridWorld, expected above 9.");
    }

    [Fact]
    public void Dqn_LearnsCartPole()
    {
        var environment = new CartPoleEnvironment();
        var agent = new DqnAgent(
            environment.ObservationSpace, environment.ActionSpace,
            new DqnOptions
            {
                LearningRate = 1e-3f,
                LearningStarts = 500,
                TargetUpdateInterval = 200,
                Epsilon = Schedule.Linear(1f, 0.05f, 0.3f),
            },
            seed: 7);

        var report = Trainer.Train(environment, agent,
            new TrainingOptions { MaxSteps = 30_000, MaxEpisodes = int.MaxValue, Seed = 7 });

        float score = Trainer.Evaluate(environment, agent, episodes: 10, seed: 900);

        // Random play scores about 22. Anything meaningfully above that is learning.
        Assert.True(score > 90f,
            $"DQN scored {score:F1} on CartPole after {report.TotalSteps} steps, expected above 90.");
    }

    [Fact]
    public void Ppo_LearnsCartPole()
    {
        var environment = new CartPoleEnvironment();
        var agent = new PpoAgent(
            environment.ObservationSpace, environment.ActionSpace,
            new PpoOptions { RolloutLength = 1_024, Epochs = 10, LearningRate = 3e-4f },
            seed: 3);

        var report = Trainer.Train(environment, agent,
            new TrainingOptions { MaxSteps = 60_000, MaxEpisodes = int.MaxValue, Seed = 3 });

        float score = Trainer.Evaluate(environment, agent, episodes: 10, seed: 901);
        Assert.True(score > 150f,
            $"PPO scored {score:F1} on CartPole after {report.TotalSteps} steps, expected above 150.");
    }

    [Fact]
    public void A2C_LearnsCartPole()
    {
        var environment = new CartPoleEnvironment();
        var agent = new A2CAgent(
            environment.ObservationSpace, environment.ActionSpace,
            new A2COptions { RolloutLength = 32, LearningRate = 7e-4f },
            seed: 5);

        var report = Trainer.Train(environment, agent,
            new TrainingOptions { MaxSteps = 60_000, MaxEpisodes = int.MaxValue, Seed = 5 });

        float score = Trainer.Evaluate(environment, agent, episodes: 10, seed: 902);
        Assert.True(score > 90f,
            $"A2C scored {score:F1} on CartPole after {report.TotalSteps} steps, expected above 90.");
    }

    [Fact]
    public void Sac_LearnsPendulum()
    {
        // Pendulum's reward is a cost, so a good policy approaches zero from below. A policy that
        // never swings up scores about -1200; -900 is a low bar that random control cannot clear.
        var environment = new PendulumEnvironment();
        var agent = new SacAgent(
            environment.ObservationSpace, environment.ActionSpace,
            new SacOptions { HiddenSizes = [128, 128], BatchSize = 128, LearningStarts = 500 },
            seed: 11);

        Trainer.Train(environment, agent,
            new TrainingOptions { MaxSteps = 12_000, MaxEpisodes = int.MaxValue, Seed = 11 });

        float score = Trainer.Evaluate(environment, agent, episodes: 5, seed: 903);
        Assert.True(score > -900f, $"SAC scored {score:F1} on Pendulum, expected above -900.");
    }

    [Fact]
    public void Td3_LearnsPendulum()
    {
        var environment = new PendulumEnvironment();
        var agent = new Td3Agent(
            environment.ObservationSpace, environment.ActionSpace,
            new Td3Options { HiddenSizes = [128, 128], BatchSize = 128, LearningStarts = 500 },
            seed: 13);

        Trainer.Train(environment, agent,
            new TrainingOptions { MaxSteps = 12_000, MaxEpisodes = int.MaxValue, Seed = 13 });

        float score = Trainer.Evaluate(environment, agent, episodes: 5, seed: 904);
        Assert.True(score > -900f, $"TD3 scored {score:F1} on Pendulum, expected above -900.");
    }

    [Fact]
    public void IndependentLearners_CoordinateOnPredatorPrey()
    {
        var environment = new PredatorPreyEnvironment(gridSize: 7, predatorCount: 3);

        // Shared parameters: the predators are homogeneous, so pooling their experience trains
        // three times faster per episode and removes the peers' non-stationarity from each
        // other's view.
        var shared = new DqnAgent(
            environment.ObservationSpace, environment.ActionSpace,
            // A smaller network than the default: this test runs three agents through 200-step
            // episodes, so it dominates the suite's runtime otherwise, and the task does not need
            // the capacity.
            new DqnOptions { LearningStarts = 500, LearningRate = 1e-3f, HiddenSizes = [64, 64] },
            seed: 17);

        var learners = IndependentLearners.ShareParameters(
            shared, environment.AgentCount, environment.ObservationSpace.FlatSize);

        var report = Trainer.Train(environment, learners,
            new TrainingOptions { MaxEpisodes = 150, Seed = 17 });

        // The return is summed across predators and dominated by the per-step cost early on.
        // Learning shows up as the later episodes beating the first ones.
        var returns = report.Returns;
        float early = returns.Take(30).Average();
        float late = returns.TakeLast(30).Average();

        Assert.True(late > early,
            $"Predators did not improve: first 30 episodes {early:F1}, last 30 {late:F1}.");
    }

    [Fact]
    public void DeterministicEvaluationBeatsExploringPolicy()
    {
        // Training return and evaluation return are different numbers, and the difference is the
        // exploration still switched on. Confirms SelectAction actually honours the flag.
        var environment = new CartPoleEnvironment();
        var agent = new DqnAgent(
            environment.ObservationSpace, environment.ActionSpace,
            new DqnOptions
            {
                LearningStarts = 500,
                LearningRate = 1e-3f,
                Epsilon = Schedule.Constant(0.5f),
            },
            seed: 19);

        Trainer.Train(environment, agent,
            new TrainingOptions { MaxSteps = 20_000, MaxEpisodes = int.MaxValue, Seed = 19 });

        float exploring = Trainer.Evaluate(environment, agent, episodes: 20, seed: 905);
        // Evaluate always passes deterministic: true, so this compares against a hand-rolled
        // exploring rollout.
        float greedy = exploring;
        float noisy = RunWithExploration(environment, agent, episodes: 20, seed: 905);

        Assert.True(greedy > noisy,
            $"Greedy policy ({greedy:F1}) did not beat the exploring one ({noisy:F1}) at epsilon 0.5.");
    }

    private static float RunWithExploration(CartPoleEnvironment environment, IDiscreteAgent agent, int episodes, int seed)
    {
        float total = 0f;
        var observation = new float[environment.ObservationSpace.FlatSize];

        for (int episode = 0; episode < episodes; episode++)
        {
            environment.Reset(seed + episode);
            while (true)
            {
                environment.Observation.CopyTo(observation);
                var step = environment.Step(agent.SelectAction(observation, deterministic: false));
                total += step.Reward;
                if (step.Done) break;
            }
        }

        return total / episodes;
    }
}
