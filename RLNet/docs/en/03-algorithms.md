# Algorithms

[← Documentation index](README.md) · [Bahasa Indonesia](../id/03-algoritma.md)

Six algorithms. This page covers what each one is actually doing, when it is the right choice, and
which knobs matter.

## Choosing

```
                    ┌─ discrete actions ─┬─ small, enumerable state ──▶ QLearning
                    │                    │
  What kind of      │                    ├─ unfamiliar problem ───────▶ PPO
  action space?  ───┤                    │
                    │                    ├─ sample efficiency matters ▶ DQN
                    │                    │
                    │                    └─ want fast visible progress ▶ A2C
                    │
                    └─ continuous ───────┬─ first attempt ────────────▶ SAC
                                         │
                                         ├─ willing to tune ─────────▶ TD3
                                         │
                                         └─ cheap env, many copies ──▶ PPO
```

| | Family | On/off-policy | Sample efficiency | Tuning needed |
|---|---|---|---|---|
| **Q-Learning** | value, tabular | off | very high (small problems) | almost none |
| **DQN** | value, neural | off | high | moderate |
| **A2C** | policy gradient | on | low | moderate |
| **PPO** | policy gradient | on | medium | low |

PPO and A2C take discrete *or* continuous actions; SAC and TD3 take continuous only. The on-policy
pair are also the only ones that batch across [several environments](01-getting-started.md#several-environments-at-once).
| **SAC** | actor-critic | off | high | very low |
| **TD3** | actor-critic | off | high | high |

## Q-Learning (tabular)

Watkins (1989). One stored value per state-action pair, no function approximation.

```csharp
var agent = new QTableAgent(
    environment.ActionSpace,
    StateDiscretizer.OneHot(),                      // GridWorld publishes one-hot cells
    new QTableOptions { LearningRate = 0.2f });
```

Still the right tool for a small discrete problem: it converges to the optimal policy with
probability 1 under mild conditions, which none of the neural agents can claim. On GridWorld it
finds the optimal path in a few hundred episodes, far faster than DQN on the same task.

Its limit is not speed but memory. The table has an entry per distinct state, so it cannot
generalise across similar states and cannot handle a continuous space without bucketing one first:

```csharp
// CartPole has 4 continuous dimensions. Bucket them, coarsely.
var encoder = StateDiscretizer.ForBox(box, [1, 1, 12, 6]);
//                                          ^  ^  ^^  ^
//                    cart position and velocity get 1 bin each - ignored entirely.
//                    Pole angle gets 12, angular velocity 6. Those are what matter.
```

Bin counts are the whole game in tabular RL. Too few and genuinely different situations collapse
into one entry; too many and the table is so sparse no state is ever visited twice. When you find
yourself wanting finer bins, that is the signal to switch to DQN.

**Key options:** `LearningRate`, `Epsilon` (a `Schedule`), `InitialValue`.

`InitialValue` above any achievable return gives *optimistic initialisation*: every unvisited action
looks better than every visited one, so the agent explores systematically rather than by chance. On
small deterministic problems it beats epsilon-greedy outright. It defaults to 0 because on a
stochastic environment it merely slows things down.

## DQN

Mnih et al. (2015), with double Q-learning and dueling heads on by default.

```csharp
var agent = new DqnAgent(obs, act, new DqnOptions
{
    HiddenSizes = [128, 128],
    LearningRate = 5e-4f,
    BatchSize = 64,
    TargetUpdateInterval = 500,
    TrainFrequency = 1,
    Epsilon = Schedule.Linear(1f, 0.05f, 0.5f),
    DoubleQ = true,
    Dueling = true,
    PrioritizedReplay = true,
});
```

**Two networks are the core trick.** Regressing toward a target computed by the network being
updated is a moving target, and it diverges. Freezing a copy for a few hundred steps makes the
regression stationary enough to converge.

**Double Q-learning** splits action selection from action evaluation. A single network taking a
`max` over its own noisy estimates systematically overestimates — the max of noise is biased upward
— and that bias compounds through bootstrapping. The online network picks the successor action, the
target network scores it.

**Dueling heads** factor Q into a state value and an advantage:
`Q(s,a) = V(s) + A(s,a) − mean_a A(s,a)`. This lets the network learn that a state is bad without
discovering it separately for every action. Subtracting the mean is not cosmetic — without it V and
A are only defined up to a constant that slides freely between them.

**Prioritised replay** samples transitions in proportion to TD error, concentrating the gradient
budget where the value function is still wrong. On sparse-reward problems like MountainCar it is
often the difference between learning and not. It biases the update, which is paid back through
importance-sampling weights annealed over training.

**Key options in order of impact:** `TrainFrequency` (throughput), `LearningRate`,
`TargetUpdateInterval`, `Epsilon` schedule, `PrioritizedReplay`.

## A2C

The synchronous form of Mnih et al.'s A3C (2016). One gradient step per short rollout, strictly
on-policy.

```csharp
var agent = new A2CAgent(obs, act, new A2COptions
{
    RolloutLength = 32,          // short by design
    LearningRate = 7e-4f,
    EntropyCoefficient = 0.01f,
});
```

Against PPO, the difference is what happens after a rollout: A2C takes exactly one gradient step and
throws the data away, so the policy never drifts from the one that collected it and no clipping is
needed. Simpler, and markedly less sample-efficient — every transition contributes to exactly one
update.

It earns its place twice over. It is the shortest complete actor-critic in the library, so it is the
one to read first — its update is `UpdateA2C` in `OnPolicyLearner`, which PPO shares everything
else with; and its 32-step rollout means it updates far more often than PPO, so early learning is
visible within seconds in the console rather than after the first 2048-step rollout completes.

**On continuous actions** (`ContinuousA2CAgent`) it is the most sensitive algorithm here. With no
trust region, every rollout's noise goes straight into the step, and the cure is a shorter horizon:
`Catalog` uses `Gamma = 0.9` and `GaeLambda = 0.9` for it. Measured on Reacher over 200,000 steps,
that is the difference between −31 and −141, where an untrained arm scores about −220.

## PPO

Schulman et al. (2017). The default worth reaching for first on an unfamiliar discrete task — not
because it peaks highest, but because it works across a wide range of problems without per-task
tuning.

```csharp
var agent = new PpoAgent(obs, act, new PpoOptions
{
    RolloutLength = 2_048,
    MinibatchSize = 64,
    Epochs = 10,
    ClipRange = 0.2f,
    GaeLambda = 0.95f,
    TargetKl = 0.02f,
});
```

**The idea.** A policy-gradient step is only valid near the policy that collected the data, and a
large step invalidates the very samples justifying it. PPO takes several gradient steps per rollout
anyway — which is what makes it sample-efficient — and keeps them honest by clipping the probability
ratio. Once an action has become more than `1+ε` times as likely as it was at collection, the
objective flattens and its gradient vanishes:

```
                    unclipped ────╱
  objective                    ╱
              ───────────────╱────────── clipped: flat, zero gradient
                           ╱
                    1-ε   1   1+ε        ratio
```

That flat region is the entire mechanism. Without it, PPO is vanilla policy gradient run ten times
on the same data, which diverges.

**`TargetKl` is a second safety net.** The clip bounds each action's ratio but not the distribution
as a whole; a rollout can drift far in KL while every individual ratio stays in range. When the mean
KL passes the target, the update stops early.

**GAE** (`GaeLambda`) trades bias against variance in the advantage estimate. At 0 it is the
one-step TD error — low variance, high bias. At 1 it is the full Monte-Carlo return — unbiased, high
variance. 0.95 is the usual compromise.

**Actor and critic are separate networks.** Sharing a trunk saves parameters but couples the two
losses through it, and the value loss — much larger in magnitude — tends to dominate the policy
gradient. Separate networks cost more memory and are markedly easier to tune.

**Key options:** `RolloutLength`, `Epochs`, `ClipRange`, `EntropyCoefficient`.

### PPO on continuous actions

`ContinuousPpoAgent` is the same algorithm with the categorical distribution replaced by a diagonal
Gaussian: the actor emits a mean per action dimension, and a free, state-independent log standard
deviation per dimension sets the spread (`InitialLogStd`, default 0 — a spread of 1 on the unit
action scale).

```csharp
var agent = new ContinuousPpoAgent(obs, act, new PpoOptions
{
    LearningRate = 1e-3f,
    EntropyCoefficient = 0f,        // the Gaussian already explores through its spread
    NormalizeObservations = true,
    NormalizeRewards = true,        // Pendulum pays up to -16 a step; CartPole pays +1
});
```

Two choices worth knowing:

- **The action is not squashed.** SAC needs a `tanh` because it differentiates through the sampled
  action; PPO only ever needs the log-probability of an action it actually drew. So the sample is
  stored unclipped, and only the copy sent to the environment is clipped to the bounds. Storing the
  clipped value would make the log-probability describe an action that was never taken.
- **The spread does not depend on the state.** A state-dependent spread lets the policy become
  confident in states it has barely seen, and on-policy methods, which cannot revisit old data, are
  the worst placed to recover from that.

With `Catalog`'s settings it reaches about −170 on Pendulum in 100,000 steps — ten seconds on a
laptop — and about −26 on Reacher in 200,000.

## SAC

Haarnoja et al. (2018). Continuous control, off-policy, with a stochastic policy.

```csharp
var agent = new SacAgent(obs, act, new SacOptions
{
    HiddenSizes = [256, 256],
    BatchSize = 256,
    Tau = 0.005f,
    AutoTuneTemperature = true,     // leave this on
});
```

**The objective is return plus policy entropy**, so the agent is rewarded for staying uncertain
wherever uncertainty is cheap. That turns exploration from something bolted on — injected noise,
decayed by hand — into part of what is being optimised, which is why SAC needs so much less tuning
than the alternatives.

**Automatic temperature tuning is the single most useful thing about it.** The right trade-off
between reward and exploration is not a constant — it differs per environment and changes over a
run. Learning it against a target entropy removes the library's most annoying hyper-parameter. Leave
`AutoTuneTemperature` on unless you have a specific reason.

**The tanh correction matters.** The policy is a Gaussian squashed through a `tanh` so actions land
in `[-1, 1]`. That squashing changes the density, and the `log(1 − tanh²)` correction is not
optional bookkeeping — without it the reported log-probability is wrong, the temperature is tuned
against a fiction, and the policy quietly saturates at the action bounds.

**Twin critics** (shared with TD3) fix overestimation: a single bootstrapped critic is biased upward
because the actor is trained to maximise its own noisy output. Two critics with independent errors,
minimum taken, biases downward instead — the harmless direction.

**Key options:** `HiddenSizes` and `BatchSize` (both dominate cost), `TrainFrequency`, `Tau`.

## TD3

Fujimoto et al. (2018). DDPG works when it works and diverges when it does not; TD3 is three
specific fixes for why.

```csharp
var agent = new Td3Agent(obs, act, new Td3Options
{
    PolicyDelay = 2,
    PolicyNoise = 0.2f,
    NoiseClip = 0.5f,
    ExplorationNoise = 0.1f,
});
```

1. **Twin critics** — the pessimistic minimum, as in SAC.
2. **Delayed policy updates** (`PolicyDelay`) — the critics settle for a couple of steps before the
   actor chases them, so the actor is not climbing a surface still moving under it.
3. **Target policy smoothing** (`PolicyNoise`, `NoiseClip`) — clipped noise on the successor action
   stops the actor exploiting a narrow spike where the critic happens to be wrong. It forces the
   critic to be right over a neighbourhood, not a point.

Against SAC: TD3's policy is deterministic, so all exploration is `ExplorationNoise`, whose
magnitude you have to choose. TD3 is often the stronger of the two once tuned; SAC is far more
likely to work on the first attempt.

## Schedules

Exploration rate, learning rate and clip range all want to shrink over a run, and all express it the
same way:

```csharp
Schedule.Constant(0.1f)
Schedule.Linear(1f, 0.05f, fraction: 0.5f)       // decay over the first half, then hold
Schedule.Exponential(1f, 0.05f, fraction: 0.6f)  // geometric; the classic DQN choice
```

Finishing the decay early rather than at the very end is deliberate: an agent still exploring on its
last episode has no chance to consolidate, and that final stretch of low-exploration training is
what turns a policy that works sometimes into one that works.

Schedules only move if the agent is told how far along it is. `Trainer` calls `SetProgress`
automatically; a hand-written loop must do it.

## Next

- [Environments](04-environments.md) — what to point these at
- [Troubleshooting](11-troubleshooting.md) — when an agent will not learn
- [Neural engine](05-neural-network.md) — the layer underneath
