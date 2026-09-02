# The console

[← Documentation index](README.md) · [Bahasa Indonesia](../id/08-konsol.md)

```bash
dotnet run --project src/RLNet.Visualizer
```

![RLNet console running PPO on CartPole](../images/console-cartpole.png)

An Avalonia desktop app for watching an agent learn, on Windows, Linux and macOS. It answers two
questions at once: **what is the agent doing**, on the left, and **is it working**, on the right.

## Reading the panel

### The status ribbon

`ENVIRONMENT · ALGORITHM · EPISODE · STEPS · STEPS/SEC · BEST RETURN`

`STEPS / SEC` is worth watching for its own sake. It tells you where the time is going: tabular
Q-learning on GridWorld runs at several hundred steps a second, DQN at around 130 with a gradient
step every other environment step, and SAC lower still because one SAC update touches an actor and
four critics.

### The viewport

Each environment draws itself, and the drawing is meant to show the *task*, not just the state.
CartPole marks the position bounds where the episode ends, so a cart drifting toward the dashed
line reads as trouble. LunarLander lights the engine plumes only while they fire, so the policy's
moment-to-moment decisions are visible. MountainCar draws the flag the car cannot reach directly.

The **action lamps** below the viewport light the action just chosen. The recorder stack shows
whether the policy is improving; the lamps show what it is actually doing, which no aggregate can.
Continuous environments have no discrete action to light, so they say so and point at the viewport
— Pendulum draws the applied torque as a violet arc around the pivot.

### The recorder stack

Three traces on one shared episode axis, butted together and separated only by hairlines so the
whole thing reads as a single instrument rather than three charts. That layout is the point: the
story of a training run is the *relationship* between the three.

| Trace | Colour | What it says |
|---|---|---|
| **Episode return** | teal | The score. The only trace that directly answers "is it working". |
| **Value loss** | rust | How wrong the critic still is. |
| **Epsilon / policy entropy** | violet | How much the agent is still exploring. |

Return and loss carry a **smoothed trend line** over a faint raw series. Per-episode return is
genuinely noisy — CartPole can score 9 then 400 under one unchanged policy — so the raw trace alone
cannot answer the question being asked of it. The smoothed line answers it; the raw one stays
visible so the noise is demoted rather than hidden.

The third trace relabels itself. Value-based agents explore by epsilon; policy-gradient agents
explore through the entropy of their own distribution. Both answer "how much is this agent still
trying things", so they share the trace and the legend says which is shown.

**A healthy run looks like this:** exploration falling steadily, return climbing behind it, loss
rising at first — the critic is being asked about states it has never seen — then levelling.

**Value loss going up is usually fine.** As the policy improves it visits new, higher-value states,
and the critic has to catch up. Loss that never comes back down while return has plateaued is the
signal worth chasing; see [troubleshooting](11-troubleshooting.md).

### The transport bar

Environment and algorithm pickers, start/stop, reset, and a speed slider.

The **speed slider is geometric**, from 1 step per frame to 65,536. The interesting range spans four
orders of magnitude — one step per frame to watch a single decision, tens of thousands to get
through early training — and a linear slider would spend nearly all its travel in territory that
looks identical. At the low end you can follow individual actions; at the high end the viewport
becomes a blur and the recorder stack is the thing to read.

**Reset** rebuilds the session from scratch. Every session starts from the same seed, so switching
algorithm and switching back replays the same run — which is what makes comparing two algorithms
meaningful rather than a comparison of two different random worlds.

## Command line

The console takes arguments, so a demonstration can be scripted and a screenshot reproduced:

```bash
RLNet.Visualizer --env Pendulum --algo Sac --start
RLNet.Visualizer --env PredatorPrey --start --speed 64
RLNet.Visualizer --list        # every environment and what it supports
RLNet.Visualizer --help
```

| | |
|---|---|
| `--env`, `-e` | Environment by catalog name (default CartPole) |
| `--algo`, `-a` | `QLearning`, `Dqn`, `A2C`, `Ppo`, `Sac`, `Td3` |
| `--start`, `-s` | Begin training immediately |
| `--speed` | Steps per frame; snapped to the nearest slider position |
| `--list` | Print the environments and exit |

## The environments, as they appear

### Continuous control — Pendulum with SAC

![Pendulum under SAC](../images/console-pendulum.png)

The violet arc around the pivot is the applied torque: its length is the magnitude, its side the
sign. A continuous action has no natural shape, and a bare number does not convey that the policy is
pushing gently rather than slamming the limit.

Note the third trace has relabelled itself to **POLICY ENTROPY**, and that return is climbing from
about −1950 toward −842 while loss falls. Pendulum's reward is a cost, so returns are negative and a
good policy approaches zero from below.

### Tabular Q-learning — GridWorld

![GridWorld under tabular Q-learning](../images/console-gridworld.png)

Best return 9.3, which is exactly optimal: seven intermediate steps at −0.1 plus the +10 goal. The
value loss trace sits at zero with occasional spikes — the table has converged, and each spike is
the agent revisiting a state it had not yet settled.

This is the environment to debug against. Its optimal policy can be worked out by hand, so an agent
that fails here has a bug rather than a hyper-parameter problem.

### Multi-agent — PredatorPrey

![PredatorPrey with three shared-parameter predators](../images/console-predatorprey.png)

Three amber predators, one teal prey, on a grid that wraps at every edge. Capture needs two
predators on the prey's cell **at the same time**, so it cannot be solved by any agent alone.

The console runs these with shared parameters — one policy, experience pooled across all three —
which trains roughly three times faster per wall-clock second than three independent learners and
removes most of the non-stationarity they would otherwise create for each other. See
[multi-agent](07-multi-agent.md).

### Finance — Trading

![Trading against a mean-reverting price series](../images/console-trading.png)

The marker is filled when the agent holds stock and hollow when it is flat, because position is the
state that matters and is otherwise buried in a number. The readout carries the benchmark —
`net worth`, `buy & hold`, `edge` — because beating buy-and-hold is the only result that means
anything here, and it turns teal or rust with the sign.

This is a teaching environment, not a trading system. See
[environments](04-environments.md#trading) for exactly what it does and does not model.

## How it runs

Training happens on the UI thread, in time-budgeted slices. A background thread would train faster,
but the renderer reads live environment state — cart position, lander attitude, joint angles — and
reading that while another thread mutates it is a race that would force every environment to publish
an immutable snapshot per step.

`FrameBudget` (8 ms by default) is the safety valve: however many steps are requested, the slice
yields when the budget is spent, so a slow environment can never freeze the window.

The console also uses **lighter agent settings than the library defaults** — narrower networks,
smaller batches, a gradient step every second environment step. The published hyper-parameters are
tuned for the best final result over a long run, which is the wrong trade for something whose job is
to show what learning looks like: SAC at its defaults runs at about 20 steps a second here, and a
viewer would watch a still image. See `DemoPresets.cs`, which documents each departure.

Anyone training for real should use `Catalog`'s defaults or their own.

## Next

- [Environments](04-environments.md) — what each of the nine teaches
- [Algorithms](03-algorithms.md) — what you are watching
- [Troubleshooting](11-troubleshooting.md) — when the traces look wrong
