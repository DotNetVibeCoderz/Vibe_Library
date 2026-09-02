# Architecture

[← Documentation index](README.md) · [Bahasa Indonesia](../id/02-arsitektur.md)

## The shape of it

```
                    ┌──────────────────────────────────────────┐
                    │              Catalog                     │
                    │   name ──▶ environment ──▶ tuned agent    │
                    └──────────────────────────────────────────┘
                                       │
        ┌──────────────────────────────┼──────────────────────────────┐
        ▼                              ▼                              ▼
┌───────────────┐            ┌──────────────────┐           ┌──────────────────┐
│  Environment  │            │      Agent       │           │     Trainer      │
│               │            │                  │           │                  │
│ Observation   │───span────▶│ SelectAction     │◀──drives──│  the loop that   │
│ Step(action)  │◀──action───│ Observe          │           │  connects them   │
│ Spaces        │            │ Metrics          │           │                  │
└───────────────┘            └──────────────────┘           └──────────────────┘
                                       │
                        ┌──────────────┴──────────────┐
                        ▼                             ▼
              ┌──────────────────┐          ┌──────────────────┐
              │  IReplayBuffer   │          │   MlpNetwork     │
              │                  │          │                  │
              │ Uniform          │          │  DenseLayer[]    │
              │ Prioritized      │          │  Adam            │
              │ Rollout (on-pol) │          │  IComputeBackend │
              └──────────────────┘          └────────┬─────────┘
                                                     │
                                        ┌────────────┴────────────┐
                                        ▼                         ▼
                                 ┌─────────────┐          ┌──────────────┐
                                 │  CPU SIMD   │          │ GPU (ILGPU)  │
                                 │  (default)  │          │  (optional)  │
                                 └─────────────┘          └──────────────┘
```

Five layers, each depending only on the one below it. An environment knows nothing about agents; an
agent knows nothing about the trainer; the network knows nothing about reinforcement learning.

## The two contracts

Almost everything unusual about this library follows from two decisions. Both are about avoiding
allocation on a path that executes millions of times.

### The observation contract

`IEnvironment.Observation` returns a `ReadOnlySpan<float>` over a buffer the environment owns and
overwrites in place.

```csharp
var observation = environment.Observation;   // a view, not a copy
environment.Step(action);
// `observation` now shows the NEW state. The old values are gone.
```

The alternative — returning a fresh `float[]` per step — costs one allocation per step. At a
million steps that is a million short-lived arrays, and the collector becomes the single largest
consumer of CPU in a training run.

So the rule is: **anything that must outlive the step copies it.** The library does this at every
point where it matters:

```csharp
// Trainer.Train, the loop every agent runs inside
environment.Observation.CopyTo(observation);       // capture BEFORE stepping
int action = agent.SelectAction(observation);
var step = environment.Step(action);               // environment's buffer now overwritten
agent.Observe(observation, action, step.Reward, environment.Observation, ...);
//            ^^^^^^^^^^^ the copy                 ^^^^^^^^^^^^^^^^^^^^^^^ the new state
```

If you write your own loop, this is the one thing to get right. Symptoms of getting it wrong: the
agent trains but never improves, because every transition it stores says the state did not change.

### Termination versus truncation

`StepResult` carries two flags, not one:

```csharp
public readonly record struct StepResult(float Reward, bool Terminated, bool Truncated)
{
    public bool Done => Terminated || Truncated;
}
```

- **Terminated** — the episode reached a real terminal state. The pole fell, the lander crashed,
  the agent reached the goal. There is no future beyond it, and its value is genuinely zero.
- **Truncated** — the episode hit a step limit while still perfectly viable. There *is* a future;
  we just stopped looking at it.

Every bootstrap target in the library reads `Terminated`:

```csharp
target = terminated
    ? reward                                  // no future at all
    : reward + gamma * ValueOf(nextState);    // there is a future, count it
```

Using `Done` here instead is the most common silent bug in hand-written RL. It teaches the agent
that the world ends at step 500, which makes states near the limit look catastrophic. On Pendulum —
which has *no* terminal state, only a 200-step limit — getting this wrong costs several hundred
points of final return, and the agent still appears to train.

`Done` exists only for loop control:

```csharp
if (step.Done) environment.Reset();   // correct use
```

## Spaces

An environment describes its own shapes, so a generic agent can be pointed at an unfamiliar one
with no configuration:

```csharp
public abstract class Space
{
    public abstract int FlatSize { get; }
    public abstract void Sample(FastRandom random, Span<float> destination);
    public abstract bool Contains(ReadOnlySpan<float> value);
}
```

- `DiscreteSpace(count)` — a finite action set, optionally with labels for the console.
- `BoxSpace(low[], high[])` — a bounded continuous vector, one interval per dimension.

`DqnAgent` reads `observationSpace.FlatSize` to size its input layer and `actionSpace.Count` to
size its output layer. That is the whole of its configuration. This mirrors what `gymnasium.spaces`
does in the Python ecosystem, and is what "environment standardisation" amounts to in practice.

`BoxSpace` also carries the two conversions continuous agents need:

```csharp
space.Clamp(action);          // fold an out-of-range action back into bounds
space.ScaleFromUnit(action);  // map tanh output [-1, 1] onto the real bounds
```

SAC and TD3 both emit actions through a `tanh`, so their raw output is always `[-1, 1]`. Keeping
the rescaling in one place means no environment and no algorithm has to know about the other's
units.

## Agents

Two interfaces, split by action type:

```csharp
public interface IDiscreteAgent : IAgent
{
    int SelectAction(ReadOnlySpan<float> observation, bool deterministic = false);
    void Observe(ReadOnlySpan<float> observation, int action, float reward,
                 ReadOnlySpan<float> nextObservation, bool terminated, bool truncated);
}
```

`Observe` is where the algorithm lives. Whether it learns immediately (Q-learning), buffers and
learns on a schedule (DQN, SAC, TD3), or accumulates a rollout and updates in bulk (PPO, A2C) is
entirely internal — the calling loop is identical for all six.

`deterministic` matters more than it looks. Training return and evaluation return are different
numbers: an agent still exploring at 5% spends one step in twenty doing something it knows is
wrong. Always evaluate deterministically before reporting a result.

`SetProgress(float)` tells an agent how far through training the run is, so schedules — epsilon
decay, learning-rate annealing, prioritised-replay beta — can follow it. `Trainer` calls it
automatically. Without it, every schedule sits at its starting value forever.

## The swappable seams

Three things are interfaces the agents *take* rather than construct, which is what makes the
library modular in the sense the requirements asked for:

```csharp
// Replay strategy
var agent = new DqnAgent(obs, act, buffer: new PrioritizedReplayBuffer(1_000_000, 4, 1));

// Compute device
var agent = new DqnAgent(obs, act, backend: GpuComputeBackend.TryCreate());

// State encoding, for tabular agents
var agent = new QTableAgent(act, StateDiscretizer.ForBox(box, [12, 12, 20, 20]));
```

Each has a sensible default, so none of this is required to get started — but none of it requires
forking an agent either.

## Memory model

Every buffer a run needs is allocated at construction:

| Component | Allocated once | Sized by |
|---|---|---|
| `MlpNetwork` | input, per-layer activations, per-layer gradients | `maxBatch` |
| `DenseLayer` | weights, biases, weight/bias gradients, activation cache | layer shape |
| `AdamOptimizer` | two moment arrays | parameter count |
| `UniformReplayBuffer` | five flat arrays, structure-of-arrays | capacity |
| `RolloutBuffer` | ten flat arrays | rollout length |
| `ReplayBatch` | one minibatch, reused every sample | batch size |

A full forward-backward-update cycle allocates **zero bytes**. The
[benchmarks](09-benchmarks.md) include a memory column specifically so a regression here is visible.

The replay buffer is a structure of arrays rather than an array of transition objects. A
million-step buffer over a four-dimensional observation is a million small heap objects under the
obvious design, and a handful of large flat arrays under this one — far less memory, and far
friendlier to the prefetcher when a batch is gathered.

## Where to go next

- [Algorithms](03-algorithms.md) — what each of the six actually does
- [Neural engine](05-neural-network.md) — the layer below the agents
- [Extending](10-extending.md) — adding your own environment or algorithm
