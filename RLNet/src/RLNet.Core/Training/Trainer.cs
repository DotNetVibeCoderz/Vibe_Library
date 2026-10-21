// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Diagnostics;
using RLNet.Agents;
using RLNet.Environments;
using RLNet.Environments.MultiAgent;

namespace RLNet.Training;

/// <summary>Settings for a training run.</summary>
public sealed class TrainingOptions
{
    /// <summary>Environment steps to run. Takes precedence over <see cref="MaxEpisodes"/> when both are set.</summary>
    public long MaxSteps { get; set; }

    /// <summary>Episodes to run, used when <see cref="MaxSteps"/> is 0.</summary>
    public int MaxEpisodes { get; set; } = 1_000;

    /// <summary>Episodes averaged for the trailing mean return.</summary>
    public int WindowSize { get; set; } = 100;

    /// <summary>
    /// Stop once the trailing mean return reaches this, or <see cref="float.NaN"/> to run to the
    /// end. CartPole's conventional threshold is 475.
    /// </summary>
    public float SolveThreshold { get; set; } = float.NaN;

    /// <summary>Seed for the environment, making a whole run reproducible.</summary>
    public int? Seed { get; set; }

    /// <summary>Called after every episode. Keep it cheap; it runs inside the training loop.</summary>
    public Action<EpisodeReport>? OnEpisode { get; set; }

    /// <summary>Checked between episodes; returning true stops the run.</summary>
    public Func<bool>? ShouldStop { get; set; }
}

/// <summary>
/// Runs the interaction loop that connects an environment to an agent.
/// </summary>
/// <remarks>
/// <para>
/// Every RL tutorial rewrites this loop, and the same handful of mistakes appear each time:
/// treating a time limit as termination, letting the observation span go stale across a step,
/// forgetting to tell the agent how far through training it is so its schedules never move.
/// Writing it once, correctly, is what the ease-of-use requirement actually amounts to.
/// </para>
/// <para>
/// The subtle one is the observation copy. Environments hand out spans over buffers they
/// overwrite on the next step, so a transition has to capture the observation <em>before</em>
/// stepping. The loop keeps one reusable array for exactly that and copies into it each step.
/// </para>
/// </remarks>
public static class Trainer
{
    /// <summary>Trains a discrete agent.</summary>
    public static TrainingReport Train(
        IDiscreteEnvironment environment,
        IDiscreteAgent agent,
        TrainingOptions? options = null)
    {
        var settings = options ?? new TrainingOptions();
        var state = new RunState(settings);

        var observation = new float[environment.ObservationSpace.FlatSize];
        environment.Reset(settings.Seed);

        while (!state.ShouldFinish())
        {
            agent.SetProgress(state.Progress);

            environment.Observation.CopyTo(observation);
            int action = agent.SelectAction(observation);
            var step = environment.Step(action);

            agent.Observe(observation, action, step.Reward, environment.Observation, step.Terminated, step.Truncated);

            state.RecordStep(step.Reward);

            if (step.Done)
            {
                agent.OnEpisodeEnd();
                state.CompleteEpisode(step.Terminated, agent.Metrics);
                environment.Reset();
            }
        }

        return state.Build(agent.Metrics);
    }

    /// <summary>Trains a continuous agent.</summary>
    public static TrainingReport Train(
        IContinuousEnvironment environment,
        IContinuousAgent agent,
        TrainingOptions? options = null)
    {
        var settings = options ?? new TrainingOptions();
        var state = new RunState(settings);

        var observation = new float[environment.ObservationSpace.FlatSize];
        var action = new float[environment.ActionSpace.FlatSize];
        environment.Reset(settings.Seed);

        while (!state.ShouldFinish())
        {
            agent.SetProgress(state.Progress);

            environment.Observation.CopyTo(observation);
            agent.SelectAction(observation, action);
            var step = environment.Step(action);

            agent.Observe(observation, action, step.Reward, environment.Observation, step.Terminated, step.Truncated);

            state.RecordStep(step.Reward);

            if (step.Done)
            {
                agent.OnEpisodeEnd();
                state.CompleteEpisode(step.Terminated, agent.Metrics);
                environment.Reset();
            }
        }

        return state.Build(agent.Metrics);
    }

    /// <summary>Trains a set of independent learners in a multi-agent environment.</summary>
    /// <remarks>
    /// The reported return is the sum across agents. On a cooperative task with a shared reward
    /// that is the quantity being maximised; on a competitive one it would be meaningless, which
    /// is a limitation of this loop rather than of the environment interface.
    /// </remarks>
    public static TrainingReport Train(
        IMultiAgentEnvironment environment,
        IndependentLearners learners,
        TrainingOptions? options = null)
    {
        var settings = options ?? new TrainingOptions();
        var state = new RunState(settings);

        environment.Reset(settings.Seed);

        while (!state.ShouldFinish())
        {
            learners.SetProgress(state.Progress);

            var actions = learners.SelectActions(environment);
            var step = environment.Step(actions);
            learners.Observe(environment, step);

            float total = 0f;
            foreach (float reward in environment.LastRewards) total += reward;
            state.RecordStep(total);

            if (step.Done)
            {
                learners.OnEpisodeEnd();
                state.CompleteEpisode(step.Terminated, learners.Agents[0].Metrics);
                environment.Reset();
            }
        }

        return state.Build(learners.Agents[0].Metrics);
    }

    /// <summary>Trains a batched discrete agent across several copies of an environment.</summary>
    /// <remarks>
    /// Episodes are counted per copy, so <see cref="TrainingOptions.MaxEpisodes"/> and the
    /// solve threshold mean what they do for a single environment; <see cref="TrainingOptions.MaxSteps"/>
    /// counts every copy's steps, so eight copies reach it eight times sooner in iterations.
    /// </remarks>
    public static TrainingReport Train(
        DiscreteVectorEnvironment environment,
        IVectorDiscreteAgent agent,
        TrainingOptions? options = null)
    {
        var settings = options ?? new TrainingOptions();
        var state = new RunState(settings);
        var episodes = new VectorEpisodes(environment.Count);

        var observations = new float[environment.Count * environment.ObservationSize];
        var actions = new int[environment.Count];
        environment.Reset(settings.Seed);

        while (!state.ShouldFinish())
        {
            agent.SetProgress(state.Progress);

            environment.Observations.CopyTo(observations);
            agent.SelectActions(observations, actions);
            environment.Step(actions);

            agent.ObserveBatch(
                observations, actions, environment.Rewards, environment.NextObservations,
                environment.Terminated, environment.Truncated);

            episodes.Record(environment, agent, state);
        }

        return state.Build(agent.Metrics);
    }

    /// <summary>Trains a batched continuous agent across several copies of an environment.</summary>
    /// <remarks>Counts episodes and steps as the discrete overload does.</remarks>
    public static TrainingReport Train(
        ContinuousVectorEnvironment environment,
        IVectorContinuousAgent agent,
        TrainingOptions? options = null)
    {
        var settings = options ?? new TrainingOptions();
        var state = new RunState(settings);
        var episodes = new VectorEpisodes(environment.Count);

        var observations = new float[environment.Count * environment.ObservationSize];
        var actions = new float[environment.Count * environment.ActionSpace.FlatSize];
        environment.Reset(settings.Seed);

        while (!state.ShouldFinish())
        {
            agent.SetProgress(state.Progress);

            environment.Observations.CopyTo(observations);
            agent.SelectActions(observations, actions);
            environment.Step(actions);

            agent.ObserveBatch(
                observations, actions, environment.Rewards, environment.NextObservations,
                environment.Terminated, environment.Truncated);

            episodes.Record(environment, agent, state);
        }

        return state.Build(agent.Metrics);
    }

    /// <summary>
    /// Runs a trained discrete agent with exploration off and returns the mean return.
    /// </summary>
    /// <remarks>
    /// Training return and evaluation return are different numbers, and conflating them
    /// overstates how good a policy is: a run still exploring at 5% spends one step in twenty
    /// doing something it knows is wrong. Evaluate deterministically before reporting a result.
    /// </remarks>
    public static float Evaluate(
        IDiscreteEnvironment environment,
        IDiscreteAgent agent,
        int episodes = 10,
        int? seed = null)
    {
        float total = 0f;
        var observation = new float[environment.ObservationSpace.FlatSize];

        for (int episode = 0; episode < episodes; episode++)
        {
            // Each episode gets its own derived seed, so an evaluation is reproducible without
            // every episode being identical.
            environment.Reset(seed.HasValue ? seed.Value + episode : null);

            while (true)
            {
                environment.Observation.CopyTo(observation);
                var step = environment.Step(agent.SelectAction(observation, deterministic: true));
                total += step.Reward;
                if (step.Done) break;
            }
        }

        return total / episodes;
    }

    /// <summary>Runs a trained continuous agent with exploration off and returns the mean return.</summary>
    public static float Evaluate(
        IContinuousEnvironment environment,
        IContinuousAgent agent,
        int episodes = 10,
        int? seed = null)
    {
        float total = 0f;
        var observation = new float[environment.ObservationSpace.FlatSize];
        var action = new float[environment.ActionSpace.FlatSize];

        for (int episode = 0; episode < episodes; episode++)
        {
            environment.Reset(seed.HasValue ? seed.Value + episode : null);

            while (true)
            {
                environment.Observation.CopyTo(observation);
                agent.SelectAction(observation, action, deterministic: true);
                var step = environment.Step(action);
                total += step.Reward;
                if (step.Done) break;
            }
        }

        return total / episodes;
    }

    /// <summary>Per-copy episode accumulators for the vector loops.</summary>
    private sealed class VectorEpisodes(int count)
    {
        private readonly float[] _returns = new float[count];
        private readonly int[] _lengths = new int[count];

        public void Record(VectorEnvironment environment, IAgent agent, RunState state)
        {
            var rewards = environment.Rewards;
            var terminated = environment.Terminated;
            var truncated = environment.Truncated;

            for (int i = 0; i < rewards.Length; i++)
            {
                _returns[i] += rewards[i];
                _lengths[i]++;
                state.CountSteps(1);

                if (!terminated[i] && !truncated[i]) continue;

                agent.OnEpisodeEnd();

                // Several copies can finish on the same step. Once the run's episode budget or
                // solve condition is met, the rest are not counted, so MaxEpisodes is exact.
                if (!state.EpisodesExhausted)
                    state.CompleteEpisode(terminated[i], agent.Metrics, _returns[i], _lengths[i]);

                _returns[i] = 0f;
                _lengths[i] = 0;
            }
        }
    }

    /// <summary>Bookkeeping shared by every training loop.</summary>
    private sealed class RunState(TrainingOptions options)
    {
        private readonly List<float> _returns = [];
        private readonly Queue<float> _window = new();
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

        private float _windowSum;
        private float _episodeReturn;
        private int _episodeSteps;
        private bool _stopped;

        public int Episodes { get; private set; }
        public long TotalSteps { get; private set; }
        public float BestReturn { get; private set; } = float.NegativeInfinity;
        public bool Solved { get; private set; }
        public int SolvedAtEpisode { get; private set; } = -1;

        /// <summary>How far through the run, in <c>[0, 1]</c>, for the agents' schedules.</summary>
        public float Progress => options.MaxSteps > 0
            ? Math.Clamp(TotalSteps / (float)options.MaxSteps, 0f, 1f)
            : Math.Clamp(Episodes / (float)Math.Max(1, options.MaxEpisodes), 0f, 1f);

        /// <summary>
        /// Whether no further episode should be counted: stopped, solved, or out of episodes. Unlike
        /// <see cref="ShouldFinish"/> it ignores the step limit, which the single-environment loop
        /// also lets an episode finish on.
        /// </summary>
        public bool EpisodesExhausted =>
            _stopped || Solved || (options.MaxSteps <= 0 && Episodes >= options.MaxEpisodes);

        public bool ShouldFinish()
        {
            if (_stopped || Solved) return true;
            return options.MaxSteps > 0 ? TotalSteps >= options.MaxSteps : Episodes >= options.MaxEpisodes;
        }

        public void RecordStep(float reward)
        {
            _episodeReturn += reward;
            _episodeSteps++;
            TotalSteps++;
        }

        /// <summary>Counts steps whose rewards the caller is accumulating itself.</summary>
        public void CountSteps(int steps) => TotalSteps += steps;

        public void CompleteEpisode(bool terminated, AgentMetrics metrics)
        {
            CompleteEpisode(terminated, metrics, _episodeReturn, _episodeSteps);
            _episodeReturn = 0f;
            _episodeSteps = 0;
        }

        public void CompleteEpisode(bool terminated, AgentMetrics metrics, float episodeReturn, int episodeSteps)
        {
            Episodes++;
            _returns.Add(episodeReturn);
            BestReturn = MathF.Max(BestReturn, episodeReturn);

            _window.Enqueue(episodeReturn);
            _windowSum += episodeReturn;
            if (_window.Count > options.WindowSize) _windowSum -= _window.Dequeue();

            float average = _windowSum / _window.Count;

            options.OnEpisode?.Invoke(new EpisodeReport(
                Episodes, episodeSteps, episodeReturn, average, TotalSteps, terminated));

            // The threshold is only meaningful once the window is full; a single lucky episode
            // early on would otherwise end the run and report it as solved.
            if (!float.IsNaN(options.SolveThreshold) &&
                _window.Count >= options.WindowSize &&
                average >= options.SolveThreshold)
            {
                Solved = true;
                SolvedAtEpisode = Episodes;
            }

            if (options.ShouldStop?.Invoke() == true) _stopped = true;
        }

        public TrainingReport Build(AgentMetrics metrics) => new()
        {
            Episodes = Episodes,
            TotalSteps = TotalSteps,
            Duration = _stopwatch.Elapsed,
            FinalAverageReturn = _window.Count > 0 ? _windowSum / _window.Count : 0f,
            BestReturn = _returns.Count > 0 ? BestReturn : 0f,
            Returns = _returns,
            Solved = Solved,
            SolvedAtEpisode = SolvedAtEpisode,
            Metrics = metrics,
        };
    }
}
