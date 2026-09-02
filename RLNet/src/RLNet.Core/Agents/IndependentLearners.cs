// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Environments.MultiAgent;

namespace RLNet.Agents;

/// <summary>
/// Runs one independent single-agent learner per agent in a multi-agent environment.
/// </summary>
/// <remarks>
/// <para>
/// Independent learning (Tan, 1993) is the simplest thing that works: each agent treats the
/// others as part of the environment and learns as if it were alone. It is the baseline every
/// multi-agent paper reports against, and on cooperative tasks with a shared reward — like
/// <see cref="PredatorPreyEnvironment"/> — it reaches genuinely coordinated behaviour.
/// </para>
/// <para>
/// <b>What it gives up.</b> Every single-agent convergence guarantee assumes a stationary
/// environment, and here the environment contains other learners. From any one agent's view the
/// transition dynamics change as its peers improve, so old replayed experience describes a world
/// that no longer exists. In practice that shows up as noisier learning curves and occasional
/// unlearning of behaviour that had been working. Centralised training with decentralised
/// execution — MADDPG, QMIX — exists to address this and is out of scope here.
/// </para>
/// <para>
/// <see cref="ShareParameters"/> is the practical middle ground for homogeneous agents: one
/// network, one buffer, experience pooled across every agent. It removes most of the
/// non-stationarity between peers because there is only one policy, and it trains N times faster
/// per wall-clock second — at the cost of every agent behaving identically.
/// </para>
/// </remarks>
public sealed class IndependentLearners
{
    private readonly IDiscreteAgent[] _agents;
    private readonly int[] _actions;
    private readonly float[][] _pendingObservations;

    /// <summary>Wraps one learner per agent.</summary>
    /// <param name="agents">
    /// One agent per environment slot. Pass the same instance in every slot to share parameters;
    /// <see cref="ShareParameters"/> does that more legibly.
    /// </param>
    public IndependentLearners(IReadOnlyList<IDiscreteAgent> agents, int observationSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(agents.Count, 1);

        _agents = [.. agents];
        _actions = new int[agents.Count];

        // Each agent's observation has to be copied before the environment steps: the spans it
        // hands out are views into buffers that the step overwrites, and the transition needs the
        // observation as it was when the action was chosen.
        _pendingObservations = new float[agents.Count][];
        for (int i = 0; i < agents.Count; i++)
            _pendingObservations[i] = new float[observationSize];
    }

    /// <summary>
    /// Builds a set of learners that all share one agent, pooling every agent's experience into
    /// a single policy.
    /// </summary>
    public static IndependentLearners ShareParameters(IDiscreteAgent agent, int agentCount, int observationSize)
    {
        var agents = new IDiscreteAgent[agentCount];
        Array.Fill(agents, agent);
        return new IndependentLearners(agents, observationSize);
    }

    /// <summary>The wrapped agents, in environment order.</summary>
    public IReadOnlyList<IDiscreteAgent> Agents => _agents;

    /// <summary>Whether every slot holds the same agent instance.</summary>
    public bool ParametersShared => _agents.Distinct().Count() == 1 && _agents.Length > 1;

    /// <summary>
    /// Chooses an action for every agent from its own observation, and remembers those
    /// observations for the matching <see cref="Observe"/>.
    /// </summary>
    public ReadOnlySpan<int> SelectActions(IMultiAgentEnvironment environment, bool deterministic = false)
    {
        for (int i = 0; i < _agents.Length; i++)
        {
            var observation = environment.ObservationOf(i);
            observation.CopyTo(_pendingObservations[i]);
            _actions[i] = _agents[i].SelectAction(observation, deterministic);
        }
        return _actions;
    }

    /// <summary>Hands each agent its own transition after a joint step.</summary>
    public void Observe(IMultiAgentEnvironment environment, MultiAgentStepResult result)
    {
        var rewards = environment.LastRewards;

        for (int i = 0; i < _agents.Length; i++)
        {
            _agents[i].Observe(
                _pendingObservations[i],
                _actions[i],
                rewards[i],
                environment.ObservationOf(i),
                result.Terminated,
                result.Truncated);
        }
    }

    /// <summary>Notifies every agent that the joint episode ended.</summary>
    public void OnEpisodeEnd()
    {
        // Distinct() matters when parameters are shared: notifying one instance N times would
        // decay a schedule N times faster than the episode count justifies.
        foreach (var agent in _agents.Distinct()) agent.OnEpisodeEnd();
    }

    /// <summary>Reports training progress to every agent.</summary>
    public void SetProgress(float progress)
    {
        foreach (var agent in _agents.Distinct()) agent.SetProgress(progress);
    }
}
