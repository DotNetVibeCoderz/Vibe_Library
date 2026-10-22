# Getting started

[← Documentation index](README.md) · [Bahasa Indonesia](../id/01-memulai.md)

## Install

```bash
dotnet add package Gravicode.RLNet
```

Requires the .NET 10 SDK. There are no other dependencies — no native binaries, no Python, nothing
to install alongside it.

The GPU backend is a separate, optional package:

```bash
dotnet add package Gravicode.RLNet.Gpu   # only worth it for wide networks; see docs/en/06-gpu.md
```

## The shortest useful program

```csharp
using RLNet;
using RLNet.Training;

var environment = Catalog.CreateDiscrete("CartPole");
var agent = Catalog.CreateAgent(Algorithm.Ppo, environment.ObservationSpace, environment.ActionSpace, seed: 1);

var report = Trainer.Train(environment, agent, new TrainingOptions
{
    MaxSteps = 150_000,
    SolveThreshold = 475,
    Seed = 1,
});

Console.WriteLine(report);
Console.WriteLine($"Evaluation: {Trainer.Evaluate(environment, agent, episodes: 20)}");
```

```
333 episodes, 61,306 steps in 11.2s (5,494 steps/s), final average return 475.54, solved at episode 333
Evaluation: 500
```

The run stopped early because the trailing average passed the solve threshold. `Catalog` picked
reasonable defaults for PPO on this environment, so nothing had to be configured.

## Watching a run

`OnEpisode` fires after every completed episode. Keep it cheap — it runs inside the training loop.

```csharp
var report = Trainer.Train(environment, agent, new TrainingOptions
{
    MaxSteps = 150_000,
    OnEpisode = e =>
    {
        if (e.Episode % 25 == 0)
            Console.WriteLine($"ep {e.Episode,4}  return {e.Return,7:F1}  avg {e.AverageReturn,7:F1}");
    },
});
```

`AverageReturn` is the trailing mean over `WindowSize` episodes (100 by default). It is the number
worth watching: a single episode's return is noisy enough that it tells you almost nothing.

To stop early on your own condition:

```csharp
ShouldStop = () => DateTime.UtcNow > deadline,
```

## Logging a run

`MetricsLog` writes one row per episode, with the agent's diagnostics at that moment alongside:

```csharp
using var log = MetricsLog.Create("ppo-cartpole.csv");     // .jsonl for JSON Lines

Trainer.Train(environment, agent, new TrainingOptions
{
    MaxSteps = 150_000,
    OnEpisode = log.For(agent),
});
```

The columns are `episode, steps, return, average_return, total_steps, terminated, value_loss,
policy_loss, entropy, epsilon, temperature, td_error, updates`. A diagnostic the algorithm does not
produce — a policy loss for DQN, a temperature for anything but SAC — is an empty CSV field or a JSON
`null`, so a plot shows a gap rather than a line along zero. Numbers are written with the invariant
culture, so a log from a machine with decimal commas still parses everywhere.

## Evaluating properly

Training return and evaluation return are different numbers, and reporting the first as if it were
the second overstates a policy. An agent still exploring at 5% spends one step in twenty doing
something it already knows is wrong.

```csharp
float score = Trainer.Evaluate(environment, agent, episodes: 20, seed: 1234);
```

`Evaluate` runs with exploration off. Passing a seed makes the evaluation reproducible while still
giving each episode a different start.

## Saving and resuming

`Checkpoint` writes an agent's complete state — every network including the targets, every
optimiser's moment estimates, the random generator, normalisation statistics and the schedule
position:

```csharp
using RLNet.Persistence;

Checkpoint.Save("cartpole.rlnet", agent);
```

To load, build an agent with the same configuration and restore into it:

```csharp
var agent = new PpoAgent(environment.ObservationSpace, environment.ActionSpace);
Checkpoint.Load("cartpole.rlnet", agent);
```

Every agent in the library supports it. A restored PPO or A2C agent carries on exactly where the
saved one stopped: the test suite trains one agent straight through and another through a save and
a load, and requires the two runs to produce identical returns. What is *not* saved is experience —
DQN, SAC and TD3 come back with an empty replay buffer and wait `LearningStarts` transitions before
training again, and an on-policy agent drops a half-collected rollout.

**Loading checks everything before it changes anything.** A checkpoint from another algorithm, or
from a network of a different shape, throws `InvalidDataException` naming the entry that differs,
and the agent is left exactly as it was. The file is written beside its destination and moved into
place, so a crash mid-save never destroys the previous checkpoint.

Several components can share one file, matched by position — an agent and anything trained
alongside it:

```csharp
Checkpoint.Save("run.rlnet", learners);          // IndependentLearners: each distinct agent once
```

`ExportParameters` and `ImportParameters` remain for when only the actor's weights are wanted, to
hand to another runtime for instance. They do not carry optimiser state, normalisation statistics or
PPO's log standard deviation.

## Choosing an algorithm

| Situation | Start with |
|---|---|
| Small discrete state space, want the optimal policy | `QLearning` |
| Discrete actions, unfamiliar problem | `Ppo` |
| Discrete actions, want sample efficiency | `Dqn` |
| Discrete actions, want to see learning immediately | `A2C` |
| Continuous actions, first attempt | `Sac` |
| Continuous actions, willing to tune | `Td3` |
| Continuous actions, cheap environment, many copies | `Ppo` |

[Algorithms](03-algorithms.md) covers what each one is actually doing and what its knobs mean.

## Continuous control

The API is the same shape, with an action vector instead of an index:

```csharp
var environment = Catalog.CreateContinuous("Pendulum");
var agent = Catalog.CreateAgent(Algorithm.Sac, environment.ObservationSpace, environment.ActionSpace);

var report = Trainer.Train(environment, agent, new TrainingOptions { MaxSteps = 40_000 });
```

Pendulum's reward is a cost, so returns are negative and a good policy approaches zero from below.
Around -150 is solved; a policy that never swings up scores about -1200.

PPO and A2C work here too, with a Gaussian policy. They need more environment steps than SAC and
far less compute per step — no replay, no critic update on every step — so on a cheap environment
they often finish sooner by the clock:

```csharp
var agent = Catalog.CreateAgent(Algorithm.Ppo, environment.ObservationSpace, environment.ActionSpace);
var report = Trainer.Train(environment, agent, new TrainingOptions { MaxSteps = 100_000 });
```


## Several environments at once

Every neural agent can collect from several copies of an environment together, choosing every
copy's action in one network pass:

```csharp
var environments = Catalog.CreateDiscreteVector("CartPole", count: 8);
var agent = Catalog.CreateVectorAgent(Algorithm.Ppo, environments.ObservationSpace, environments.ActionSpace, seed: 1);

Trainer.Train(environments, agent, new TrainingOptions { MaxSteps = 150_000, Seed = 1 });

// Still an ordinary agent: evaluate it on a single copy.
float score = Trainer.Evaluate(Catalog.CreateDiscrete("CartPole"), agent, episodes: 20);
```

`RolloutLength` stays the number of transitions per update, shared evenly between the copies, so
the same options do the same amount of learning, from a less correlated sample. Do not expect a
large speed-up on these environments: [measured](09-benchmarks.md#several-environments-at-once), PPO
gains about 10% because its update dominates, and A2C none. Episodes are
counted per copy, so `MaxEpisodes` and `SolveThreshold` mean what they always do.

DQN, SAC and TD3 batch the same way: every copy's transition goes into the one replay buffer, and
`TrainFrequency` counts transitions, so the ratio of gradient steps to samples does not change with
the copy count. That also means no speed-up — the gradient steps are the cost
([measured](09-benchmarks.md#several-environments-at-once): DQN takes 24 seconds for 16,384 steps
at any copy count). Batch them when the environment's own step is the expensive part, with
`parallel: true`. Tabular Q-learning is the one algorithm with no batched form.

## Configuring an agent

`Catalog.CreateAgent` picks defaults. When you want control, construct directly — every constructor
is public and every option has a documented default:

```csharp
var agent = new DqnAgent(
    environment.ObservationSpace,
    environment.ActionSpace,
    new DqnOptions
    {
        HiddenSizes = [256, 256],
        LearningRate = 1e-4f,
        BatchSize = 128,
        TrainFrequency = 4,                              // gradient step every 4 env steps
        Epsilon = Schedule.Linear(1f, 0.02f, 0.4f),      // decay over the first 40% of training
        PrioritizedReplay = true,
    },
    seed: 42);
```

`TrainFrequency` is the biggest lever on wall-clock speed. A gradient step costs far more than an
environment step, so raising it from 1 to 4 roughly quadruples throughput at some cost in sample
efficiency.

## Writing your own loop

`Trainer` exists so you do not have to, but if you need the control:

```csharp
var observation = new float[environment.ObservationSpace.FlatSize];
environment.Reset(seed: 1);

for (long step = 0; step < 100_000; step++)
{
    agent.SetProgress(step / 100_000f);          // drives epsilon and LR schedules

    environment.Observation.CopyTo(observation);  // COPY, before stepping
    int action = agent.SelectAction(observation);
    var result = environment.Step(action);

    agent.Observe(observation, action, result.Reward, environment.Observation,
                  result.Terminated, result.Truncated);   // both flags, separately

    if (result.Done)
    {
        agent.OnEpisodeEnd();
        environment.Reset();
    }
}
```

Three things to get right, all covered in [architecture](02-architecture.md):

1. Copy the observation **before** stepping — the environment overwrites its buffer.
2. Pass `Terminated` and `Truncated` separately — never collapse them into one flag.
3. Call `SetProgress` — without it, no schedule ever moves.

## Next

- [Architecture](02-architecture.md) — why the API looks like this
- [Environments](04-environments.md) — what all nine are for
- [The console](08-console.md) — watching a run instead of reading numbers
