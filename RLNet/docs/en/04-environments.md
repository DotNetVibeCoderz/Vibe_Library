# Environments

[← Documentation index](README.md) · [Bahasa Indonesia](../id/04-simulasi.md)

Nine environments across five categories. Each is here to teach something specific, and this page
says what.

| Environment | Category | Actions | Observation | Step limit | Teaches |
|---|---|---|---|---|---|
| [GridWorld](#gridworld) | Classic | 4 discrete | 25 one-hot | 100 | The baseline. Debug against it. |
| [CartPole](#cartpole) | Classic | 2 discrete | 4 continuous | 500 | The reference benchmark |
| [MountainCar](#mountaincar) | Classic | 3 discrete | 2 continuous | 200 | Exploration under a flat reward |
| [LunarLander](#lunarlander) | Classic | 4 discrete | 6 continuous | 1000 | Reward shaping |
| [Pendulum](#pendulum) | Control | 1 continuous | 3 continuous | 200 | Continuous control; truncation |
| [Reacher](#reacher) | Robotics | 2 continuous | 8 continuous | 200 | Multi-modal value functions |
| [Trading](#trading) | Finance | 3 discrete | 6 continuous | 511 | Scale-free observations |
| [SupplyChain](#supplychain) | Operations | 7 discrete | 8 continuous | 180 | Delayed credit assignment |
| [PredatorPrey](#predatorprey) | Multi-agent | 5 discrete × 3 | 53 continuous | 200 | Coordination |

## GridWorld

Navigate a 5×5 grid from the top-left corner to the goal without stepping in a trap.

```
┌───┬───┬───┬───┬───┐
│ A │   │   │   │   │   A  agent (starts here)
├───┼───┼───┼───┼───┤   X  trap  (-10, ends the episode)
│   │ X │   │ X │   │   G  goal  (+10, ends the episode)
├───┼───┼───┼───┼───┤
│   │   │ X │   │   │   every other step: -0.1
├───┼───┼───┼───┼───┤
│   │   │   │   │   │   walking into a wall: -1, episode continues
├───┼───┼───┼───┼───┤
│   │   │   │   │ G │   optimal return: 9.3
└───┴───┴───┴───┴───┘
```

**The one to debug against.** Its optimal policy can be worked out by hand, so an agent that fails
here has a bug rather than a hyper-parameter problem. Tabular Q-learning reaches 9.3 — exactly
optimal — in a few hundred episodes.

The small per-step cost is what turns "reach the goal" into "reach the goal *quickly*". Without it,
every path that eventually arrives scores the same and the agent has no reason to prefer the short
one.

**The observation is one-hot over cells, not the `(x, y)` pair.** Feeding coordinates to a network
implies cell 4 is twice cell 2 in some meaningful sense, which is false on a grid, and neural agents
learn visibly worse from it. Tabular agents are unaffected either way.

## CartPole

Balance a pole hinged to a cart by pushing the cart left or right.

Constants and termination bounds match Gymnasium's `CartPole-v1` exactly, so a score here means the
same thing as a score from Stable-Baselines3. **500 is perfect; above 475 counts as solved.**

The reward is +1 per surviving step, so return and episode length are the same number — a rising
curve is simply the pole staying up longer. That is what makes it the clearest benchmark in the set
to read.

Ends when the pole passes ±12° or the cart passes ±2.4 units.

## MountainCar

Drive an underpowered car out of a valley by rocking up the opposite slope first.

**The exploration benchmark.** The engine cannot beat gravity directly, so the only solution is to
accelerate *away* from the goal to build momentum. Reward is −1 per step until the flag, which means
that until the very first success, every policy scores exactly −200 and the gradient carries no
signal at all.

That property is the reason it is here. Epsilon-greedy DQN often never solves it; the same agent
with prioritised replay usually does. It is the cheapest demonstration in the library that
exploration is a separate problem from optimisation.

## LunarLander

Land a spacecraft on a pad between two flags, using a main engine and two attitude thrusters.

> **Not Box2D.** Gymnasium's LunarLander is a rigid-body simulation. This is a lighter analytic
> model — point mass, torque-driven attitude, no contact solver — which keeps the library free of a
> native physics dependency. The observation layout, action set and reward shape follow the
> original, so an agent transfers, but **absolute scores are not comparable** with published
> LunarLander-v2 numbers.

The reward is shaped rather than sparse: most of it comes from a potential over distance, speed and
tilt, so the agent gets a gradient long before it ever lands. Landing pays +100, crashing costs
−100, and firing an engine costs fuel every step — which is what stops the agent hovering forever
once it discovers that not crashing pays.

The shaping is **potential-based** (Ng, Harada and Russell, 1999): the reward is the *change* in the
potential, not its value. That provably leaves the optimal policy unchanged. Rewarding the value
directly would pay the agent to loiter near the pad, which is a different task from landing on it.

Landing counts only on the pad, upright, and slow enough to survive.

## Pendulum

Swing a pendulum upright and hold it there with a torque too weak to lift it directly.

Constants match Gymnasium's `Pendulum-v1`. The reference task for the continuous agents — SAC and
TD3 both solve it in tens of thousands of steps, while no discrete agent can express the fine torque
control it needs.

**The observation is `[cos θ, sin θ, θ̇]`, not `[θ, θ̇]`.** The angle is periodic, so θ = −π and
θ = π are the same state but the furthest apart two numbers can be. A network fed the raw angle has
to learn to glue the ends together, and mostly fails. The sine-cosine pair makes the topology of the
circle explicit.

**Pendulum has no terminal state at all** — the episode ends only on the 200-step limit. That makes
it the environment where getting truncation bootstrapping right matters most: treating the cutoff as
terminal costs several hundred points of final return, and the agent still appears to train. See
[architecture](02-architecture.md#termination-versus-truncation).

Reward is a cost, so returns are negative. About −150 is solved; a policy that never swings up
scores around −1200.

## Reacher

Drive a two-joint planar arm so its fingertip reaches a target that moves each episode.

> **Not MuJoCo.** Modelled on `Reacher-v4`, but the arm here is a torque-driven double pendulum with
> damping rather than a physics-engine simulation — MuJoCo would mean a native dependency and a
> licence. Scores are not comparable with published MuJoCo numbers.

Its teaching value is **redundancy**: most targets are reachable in two distinct joint
configurations, so the value function is genuinely multi-modal and a policy that averages over both
solutions reaches neither. Watching SAC commit to one of them is the clearest picture in the library
of why a stochastic policy with an entropy term behaves differently from a deterministic one.

The target enters the observation as a **vector from the fingertip**, not as an absolute position.
That makes the observation say "which way to move", which is the quantity the policy needs, and it
generalises across targets instead of memorising them.

## Trading

Trade a single instrument over a synthetic price series — buy, hold or sell.

> **A teaching environment, not a trading system.** There is no slippage, no market impact, no
> bid-ask spread beyond a flat 10bp commission, and the price process is nothing like a real
> instrument. An agent that profits here says nothing whatsoever about live markets.

The price follows a geometric random walk with a mild mean-reverting component, so there is a
genuinely learnable edge. A pure random walk would be unlearnable by construction, which makes for a
demonstration that always fails; the mean reversion is what turns this into a task.

**The observation carries no absolute price.** It holds returns over three horizons, deviation from
a rolling mean, position and cash ratio — all scale-free, all clamped to `[-1, 1]`. Feeding the raw
price would let the agent memorise the series it trained on and learn nothing transferable.

**Reward is the log return of equity**, not its change. Log returns add over time, so the discounted
sum the agent maximises is the compound growth rate — which is what a trader actually cares about.
Raw profit would make a gain from 10,000 to 10,100 look identical to one from 100,000 to 100,100.

`BuyAndHoldValue` is the benchmark to beat, and the console shows it alongside net worth.

## SupplyChain

Decide how much stock to reorder each day under uncertain demand and a three-day delivery lag.

What makes this a reinforcement-learning problem rather than an arithmetic one is the **lead time**:
an order placed today arrives three days later, so the agent must act on demand it cannot yet see,
and the consequence of a decision only becomes visible long after it was made. That delayed credit
assignment is exactly what temporal-difference learning is for.

Demand is seasonal with noise on a 60-day cycle, so a fixed reorder quantity cannot be optimal — the
agent has to read the phase of the season out of recent demand. The season enters the observation as
a sine-cosine pair, for the same reason angles do elsewhere.

**There is a known good baseline.** `BaseStockAction()` computes what a base-stock policy would
order — top the inventory position up to a fixed level each day — so a training curve can be read
against a meaningful line rather than against zero. A competent agent should match or beat it.

Costs: 0.10 per unit held per day, 1.50 per unit of unmet demand, 5.00 to place any order, against a
1.00 margin per unit sold.

## PredatorPrey

Three predators cooperate on a wrapping 9×9 grid to corner a fleeing prey.

**Capture requires two predators on the prey's cell at the same time.** A predator that only chases
is a predator that never scores. The reward is shared, and the behaviour that earns it has to be
coordinated — which is the whole reason this environment is in the library.

**The grid wraps**, which removes corners. On a bounded grid, predators learn to herd the prey into
a corner and the task collapses into a much easier one; on a torus there is nowhere to pin it and
real encirclement is the only strategy that works.

Each predator sees only a 5×5 window around itself plus a wrapped bearing to the prey — not the full
board. Partial observability is what makes the coordination problem non-trivial: with global state,
each predator could just compute the joint plan itself.

The prey is a fixed heuristic, not a learner: it flees the nearest predator, with occasional random
moves so the predators cannot learn a purely reactive counter. Keeping it fixed leaves the training
signal stationary enough to be readable.

The capture reward goes to **every** predator, not just the ones standing on the prey. Paying only
the occupiers rewards the last one to arrive and teaches the others nothing about the manoeuvre that
set it up.

See [multi-agent](07-multi-agent.md) for how to train against it.

## Creating your own

See [extending](10-extending.md). The short version:

```csharp
public sealed class MyEnvironment : DiscreteEnvironmentBase
{
    public MyEnvironment() : base(
        BoxSpace.Uniform(4, -1f, 1f),
        new DiscreteSpace(2, ["Left", "Right"]),
        maxEpisodeSteps: 500) => Reset();

    public override string Name => "MyEnvironment";

    protected override void OnReset() { /* start state */ }
    protected override void WriteObservation(Span<float> destination) { /* publish state */ }
    protected override StepResult OnStep(int action) => Advance(reward, terminated);
}
```

`Advance` handles the step counter, the time limit and the truncation flag, so no environment can
forget to report truncation — which is exactly the bug the terminated/truncated split exists to
prevent.
