# PLAN — RLNet roadmap / peta jalan

*Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.*

Status per phase. ✅ done · 🔜 next · 💡 considered · ⛔ out of scope

Progress against this plan is tracked in [Progress.md](Progress.md).

## Phase 1 — Core library ✅

- ✅ Gym-shaped environment contract: `DiscreteSpace` / `BoxSpace`, seeded `Reset`, separate
  `Terminated` / `Truncated`, time limits owned by `EnvironmentBase.Advance`
- ✅ Zero-allocation observation contract (spans over environment-owned memory)
- ✅ SIMD MLP engine (`Vector<T>`): dense layers, activations, Adam (vectorised), row-major weights
- ✅ `IComputeBackend` seam; single-threaded CPU backend
- ✅ Replay buffers: uniform, prioritised (SumTree); rollout buffer with GAE
- ✅ Agents: tabular Q-learning, DQN (double + dueling + PER), A2C, PPO, SAC, TD3
- ✅ `Trainer` loop with solve threshold, `OnEpisode`, `ShouldStop`, deterministic `Evaluate`
- ✅ `Catalog`: environment name → environment → configured agent

## Phase 2 — Environments ✅

- ✅ Classic: CartPole, MountainCar, GridWorld, LunarLander (analytic)
- ✅ Control: Pendulum, Reacher (analytic stand-in for the MuJoCo task)
- ✅ Domain: Trading, SupplyChain
- ✅ Multi-agent: PredatorPrey with independent or parameter-shared learners

## Phase 3 — Tooling ✅

- ✅ Optional GPU backend (`RLNet.Gpu`, ILGPU: CUDA / OpenCL, CPU fallback)
- ✅ Avalonia console (Windows / Linux / macOS) with CLI flags for reproducible screenshots
- ✅ BenchmarkDotNet suite (SIMD, agents, device report)
- ✅ Test suite: gradient checks against finite differences, learning tests, catalog-driven
  environment property tests

## Phase 4 — Release 0.1.0 ✅

- ✅ Documentation EN / ID (11 chapters each) and bilingual README
- ✅ CI (`rlnet-ci.yml`, three OSes) and publish (`rlnet-publish.yml`, `RLNet-v*` tags)
- ✅ `Gravicode.RLNet` and `Gravicode.RLNet.Gpu` 0.1.0 on nuget.org
- ✅ NuGet icon (`docs/images/icon.png`) — wired into both packages, first shipped in 0.2.0

## Phase 5 — 0.2.0 ✅

- ✅ **Checkpoints.** `Checkpoint.Save` / `Load` for every agent: networks, targets, Adam moments,
  RNG position, counters, normalisers. Versioned little-endian format of named arrays; network
  layouts checked, whole file validated before anything is overwritten, atomic file writes.
  (Agents already had actor-only `ExportParameters`; this is the complete, resumable form.)
- ✅ **Continuous PPO and A2C.** `GaussianPolicy` (state-independent log-std, unsquashed samples),
  `ContinuousPpoAgent`, `ContinuousA2CAgent`, registered in `Catalog` for Pendulum and Reacher.
  PPO and A2C now share one implementation (`OnPolicyLearner`) over a `PolicyHead`.
- ✅ **`VectorEnvironment`.** N copies with auto-reset and separate `NextObservations`; batched
  action selection in PPO / A2C; multi-stream GAE in `RolloutBuffer`; `Trainer` vector loops;
  optional parallel stepping (off by default: ~6× slower on these environments, measured).
- ✅ **`MetricsLog`.** CSV / JSON Lines per episode, invariant culture, NaN as empty / null.
- ✅ **Normalisation.** `RunningMeanStd`, `ObservationNormalizer`, `RewardScaler`, opt-in on PPO /
  A2C, saved in the agent's checkpoint.

## Phase 6 — next 🔜

Ordered by how much each one unblocks real use.

1. 🔜 **Console: save / load / watch.** Buttons for `Checkpoint.Save` / `Load`, a "watch only"
   mode that runs a loaded agent deterministically, and a toggle that writes a `MetricsLog` next to
   the run.
2. 🔜 **Batched off-policy agents.** DQN, SAC and TD3 take one transition at a time; feeding them a
   `VectorEnvironment` would need per-copy n-step handling and a batched `SelectActions`. Worth it
   mainly for SAC on expensive environments.
3. 🔜 **Continuous A2C robustness.** It needs γ = 0.9 to learn Reacher and 500k steps on Pendulum.
   Generalised state-dependent exploration (gSDE) is what the reference implementations use to fix
   that; evaluate it against the current fixed log-std.

## Later 💡

- 💡 GPU: pipeline kernel launches so wide networks stop paying a sync per layer
  (`docs/en/06-gpu.md` names this as the current ceiling) — re-measure the crossover afterwards.
- 💡 More algorithms: QR-DQN / C51 (distributional), n-step returns for DQN, Rainbow composition.
- 💡 Multi-agent: centralised critic (MADDPG / MAPPO) on top of `IMultiAgentEnvironment`.
- 💡 Hyper-parameter sweep helper over `Catalog` + seeds, reporting mean ± spread.
- 💡 Recurrent policy (GRU) for partially observable tasks — needs BPTT in `Neural/`, so gradient
  tests first.
- 💡 NativeAOT / trimming check for `RLNet.Core` (it is reflection-free, so likely just a CI job).

## Out of scope ⛔

Decided, not forgotten — see the scope note in `docs/en/README.md`.

- ⛔ Python Gym / Gymnasium interop (would put a Python runtime in the loop)
- ⛔ Atari, pixel observations, convolutions
- ⛔ MuJoCo / Box2D physics ports
- ⛔ TorchSharp or any native dependency in `RLNet.Core`
