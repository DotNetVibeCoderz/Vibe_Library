# Multi-agent

[← Documentation index](README.md) · [Bahasa Indonesia](../id/07-multi-agent.md)

![PredatorPrey: three predators cornering a fleeing prey](../images/console-predatorprey.png)

## The contract

```csharp
public interface IMultiAgentEnvironment
{
    int AgentCount { get; }
    Space ObservationSpace { get; }
    DiscreteSpace ActionSpace { get; }

    ReadOnlySpan<float> ObservationOf(int agent);
    ReadOnlySpan<float> LastRewards { get; }

    void Reset(int? seed = null);
    MultiAgentStepResult Step(ReadOnlySpan<int> actions);
}
```

Simultaneous-move and fully synchronous: every agent submits an action, the world advances once,
everyone receives a reward. Turn-based games need a different contract and are out of scope.

**Agents keep separate observations**, because partial observability is the interesting part of the
multi-agent setting. A predator that can see the whole board is solving a different, much easier
problem than one that can see only its neighbourhood.

Rewards are exposed through `LastRewards` rather than carried in the step result, so reading them
costs no allocation on a step that happens millions of times.

## What this does not promise: stationarity

Every single-agent convergence result assumes a stationary environment. Here the environment
*contains other learners*.

```
    From agent 1's point of view:
    ┌──────────────────────────────────────────────┐
    │  "the environment"                           │
    │  ┌────────────┐  ┌──────────┐  ┌──────────┐  │
    │  │ the world  │  │ agent 2  │  │ agent 3  │  │
    │  │            │  │(learning)│  │(learning)│  │
    │  └────────────┘  └──────────┘  └──────────┘  │
    └──────────────────────────────────────────────┘
                            ▲
              these change as training proceeds,
              so agent 1's transition dynamics shift under it
```

In practice that shows up as noisier learning curves and occasional unlearning of behaviour that had
been working. Replayed experience is the sharpest case: old transitions describe a world that no
longer exists, because the other agents have improved since.

Centralised training with decentralised execution — MADDPG, QMIX — exists to address this and is out
of scope here. `IndependentLearners` ignores the problem deliberately, and this page is where that
is documented.

## Independent learners

The baseline every multi-agent paper reports against (Tan, 1993): each agent treats the others as
part of the environment and learns as if it were alone.

```csharp
var environment = new PredatorPreyEnvironment(gridSize: 9, predatorCount: 3);

var agents = Enumerable.Range(0, environment.AgentCount)
    .Select(i => (IDiscreteAgent)new DqnAgent(
        environment.ObservationSpace, environment.ActionSpace, seed: i))
    .ToList();

var learners = new IndependentLearners(agents, environment.ObservationSpace.FlatSize);
var report = Trainer.Train(environment, learners, new TrainingOptions { MaxEpisodes = 2_000 });
```

Each agent gets its own network, its own replay buffer, its own exploration schedule. They can be
different algorithms — nothing requires them to match — which is how you would set up a competitive
comparison.

## Shared parameters

For **homogeneous** agents, one policy shared by all of them is usually the better answer:

```csharp
var shared = new DqnAgent(environment.ObservationSpace, environment.ActionSpace, seed: 1);
var learners = IndependentLearners.ShareParameters(
    shared, environment.AgentCount, environment.ObservationSpace.FlatSize);
```

Two advantages, both substantial:

- **Experience pools.** Three predators produce three transitions per step into one buffer, so the
  policy sees three times the data per wall-clock second.
- **Most of the non-stationarity disappears.** There is only one policy, so agents cannot drift
  apart from each other — only the world changes under them.

The cost is that every agent behaves identically. For predators that is fine, and arguably correct.
For agents with genuinely different roles it is wrong.

This is what the console uses, which is why PredatorPrey shows progress there within a minute.

`IndependentLearners` notices when the same instance fills every slot and calls `OnEpisodeEnd` and
`SetProgress` **once** rather than per agent — otherwise a schedule would decay N times faster than
the episode count justifies.

## PredatorPrey

Three predators corner a fleeing prey on a wrapping 9×9 grid.

**Capture requires two predators on the prey's cell at the same time.** A predator that only chases
never scores. The reward is shared, the behaviour that earns it must be coordinated — that is the
entire reason the environment is here.

Design choices that matter:

| Choice | Why |
|---|---|
| The grid wraps | On a bounded grid, predators herd the prey into a corner and the task collapses. On a torus there is nowhere to pin it, so real encirclement is the only strategy. |
| 5×5 local window plus a bearing | Full state would let each predator compute the joint plan alone. The bearing stops a predator that has lost sight of the prey from wandering, which would make credit assignment far too sparse. |
| Capture pays **every** predator | Paying only the occupiers rewards the last one to arrive and teaches the others nothing about the manoeuvre that set it up. |
| Standing on the prey alone pays 0.5 | Progress is worth a nudge, but only a small one — larger and predators learn to sit on the prey and wait rather than coordinate. |
| Prey respawns instead of ending the episode | One episode can then teach several captures, and return distinguishes a lucky predator from a consistent one. |
| The prey is a fixed heuristic | Keeps the training signal stationary enough to be readable; it flees the nearest predator with occasional random moves so the predators cannot learn a purely reactive counter. |

There is no terminal state — the episode always runs to the 200-step limit — so return is "captures
per episode" and is directly comparable between runs.

## Reading the results

`Trainer` reports the **sum** across agents. On a cooperative task with a shared reward that is the
quantity being maximised. On a competitive one it would be meaningless, which is a limitation of
this loop rather than of the environment interface — for competitive work, track per-agent returns
yourself through `LastRewards`.

Early returns are dominated by the per-step cost, so learning shows up as later episodes beating
earlier ones rather than as a curve crossing zero:

```csharp
float early = report.Returns.Take(30).Average();
float late = report.Returns.TakeLast(30).Average();
```

That is exactly what `IndependentLearners_CoordinateOnPredatorPrey` asserts.

## Writing your own

```csharp
public sealed class MyMultiAgentEnvironment : IMultiAgentEnvironment
{
    private readonly float[][] _observations;   // one per agent
    private readonly float[] _rewards;

    public ReadOnlySpan<float> ObservationOf(int agent) => _observations[agent];
    public ReadOnlySpan<float> LastRewards => _rewards;

    public MultiAgentStepResult Step(ReadOnlySpan<int> actions)
    {
        Array.Clear(_rewards);
        // apply every action, then advance the world once
        WriteObservations();
        return new MultiAgentStepResult(Terminated: false, Truncated: ElapsedSteps >= MaxEpisodeSteps);
    }
}
```

Same rules as single-agent: observations are spans into memory you overwrite, and `Terminated` means
a real terminal state rather than a step limit. See [extending](10-extending.md).

## Next

- [Environments](04-environments.md#predatorprey) — the environment in detail
- [The console](08-console.md) — watching it
