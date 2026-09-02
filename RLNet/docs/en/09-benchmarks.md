# Benchmarks

[← Documentation index](README.md) · [Bahasa Indonesia](../id/09-benchmark.md)

Every number here was measured with BenchmarkDotNet on the machine below. Reproduce them with:

```bash
dotnet run -c Release --project benchmarks/RLNet.Benchmarks
dotnet run -c Release --project benchmarks/RLNet.Benchmarks -- --filter '*SimdBenchmarks*'
```

**Test machine:** .NET 10.0.11, X64 RyuJIT, AVX2 (256-bit vectors), Server GC, Windows 11 laptop.
Absolute figures will differ on your hardware; the ratios should not.

## SIMD primitives

The three operations a dense layer decomposes into, against the same loop written scalar.

| Operation | Length 256 | Length 4,096 | Length 65,536 |
|---|---:|---:|---:|
| Dot (scalar) | 343.5 ns | 4,738.8 ns | 76,813.5 ns |
| **Dot (SIMD)** | **41.8 ns** | **590.7 ns** | **12,687.0 ns** |
| *speedup* | *8.2×* | *8.0×* | *6.1×* |
| AddScaled (scalar) | 426.5 ns | 5,900.7 ns | 98,121.7 ns |
| **AddScaled (SIMD)** | **44.4 ns** | **624.5 ns** | **15,452.9 ns** |
| *speedup* | *9.6×* | *9.4×* | *6.3×* |
| Polyak blend (SIMD) | 41.8 ns | 649.2 ns | 15,387.8 ns |

All zero-allocation.

Eight floats per AVX2 vector, so 8× is the theoretical ceiling and 8–9.6× is essentially it — the
scalar loop is not auto-vectorised by the JIT here. The drop to ~6× at 65,536 elements is the
working set leaving L2: at that size the operation is memory-bound and no amount of arithmetic
throughput helps.

That is why network width matters more than it looks. A 256-wide layer's rows fit in cache and run
at full SIMD speed; a 2,048-wide layer's do not.

## Environments

Raw stepping, no agent attached — the ceiling every algorithm runs against. 10,000 steps per
measurement:

| Environment | Per 10,000 steps | Steps/sec | Allocated |
|---|---:|---:|---:|
| LunarLander | 294.5 µs | 34.0 M | 0 B |
| CartPole | 474.3 µs | 21.1 M | 0 B |
| GridWorld | 575.0 µs | 17.4 M | 0 B |
| Pendulum | 1,021.0 µs | 9.8 M | 0 B |

Tens of millions of steps a second, allocating nothing. The environment is never the bottleneck —
which is the point of the [observation contract](02-architecture.md#the-observation-contract). If it
ever becomes one, the answer is a vectorised environment, not a faster layer.

GridWorld is slower than CartPole despite being simpler, because its observation is 25 one-hot
floats to CartPole's 4 — the clear beats the physics. Pendulum is slowest of the four because
`Math.Sin`, `Math.Cos` and the angle normalisation are transcendental calls on every step.

## The network itself

Forward, forward+backward, and the complete gradient step including the optimiser:

| Batch | Width | Forward | + backward | + optimiser | Allocated |
|---:|---:|---:|---:|---:|---:|
| 1 | 64 | 1.3 µs | 3.9 µs | 63.0 µs | 0 B |
| 1 | 256 | 8.2 µs | 36.4 µs | 283.1 µs | 0 B |
| 64 | 64 | 123.5 µs | 313.7 µs | 313.2 µs | 0 B |
| 64 | 256 | 797.5 µs | 2,290.5 µs | 2,621.3 µs | 0 B |
| 256 | 64 | 513.8 µs | 1,284.3 µs | 1,262.5 µs | 0 B |
| 256 | 256 | 2,951.2 µs | 9,396.4 µs | 9,676.8 µs | 0 B |

**Zero bytes allocated, in every configuration.** This is the table that backs the claim; the agent
benchmarks cannot show it because they construct their replay buffer inside the measurement.

Backward costs about 2.5–3× forward, which is the expected ratio: it computes two products
(gradient with respect to input, and with respect to weights) where forward computes one.

### Adam's cost does not scale with batch size

Look at the batch-1 rows against the batch-64 rows. At batch 64 the optimiser is nearly free — 313.7
to 313.2 µs, indistinguishable. At batch 1 with a 256-wide network it is 36 µs of network against
283 µs total: **the optimiser is 87% of the step.**

That is inherent to Adam rather than a defect. It touches every parameter in the network whatever
the minibatch was, so its cost is fixed while the forward and backward passes shrink with the batch.
The practical consequence: very small batches are inefficient in a way that has nothing to do with
the network.

It is also why `AdamOptimizer.Apply` is vectorised rather than the obvious scalar loop. Making that
change measured:

| | Scalar Adam | Vectorised | |
|---|---:|---:|---|
| Optimiser step, batch 1, width 64 | 290.5 µs | 59.1 µs | **4.9× faster** |
| Optimiser step, batch 1, width 256 | 1,924.4 µs | 246.7 µs | **7.8× faster** |
| Full gradient step, batch 1, width 256 | 1,969.5 µs | 283.1 µs | **7.0× faster** |

At a batch of 64 the same change is worth about 10%, and at 256 it disappears into the noise — but
A2C's short rollouts and any online single-step update sit squarely in the range where it matters.

## Replay buffers

256 transitions drawn from a **1,000,000-entry** buffer:

| Operation | Time | Ratio | Allocated |
|---|---:|---:|---:|
| Uniform sample | 22.6 µs | 1.00 | 0 B |
| Prioritised sample | 83.4 µs | 3.69 | 0 B |
| Prioritised sample + priority update | 143.7 µs | 6.36 | 0 B |

Prioritised replay costs about 6× uniform for a full round trip, and both allocate nothing.

Read that against what a gradient step costs, not in isolation. A DQN gradient step on this machine
is around 1.1 ms, so the extra ~120 µs is roughly 11% — and on a sparse-reward problem like
MountainCar, prioritised replay is frequently the difference between learning and not learning at
all. It is close to always worth it.

The `SumTree` is what makes this affordable. The naive alternative — building a cumulative
distribution and binary-searching it — is O(n) per draw against a million entries, on all 256 draws,
on every gradient step. The tree turns that into about twenty comparisons, and tracks the minimum in
the same walk so the importance-sampling weights need no scan either.

## Complete training loops

Environment stepping, action selection, buffering and gradient steps together — the number a user
actually feels. 2,000 steps per measurement, at **library default settings**:

| Algorithm / environment | Per 2,000 steps | Steps/sec |
|---|---:|---:|
| Q-learning / GridWorld | 5.5 ms | 365,000 |
| A2C / CartPole | 43.4 ms | 46,100 |
| PPO / CartPole | 114.6 ms | 17,450 |
| DQN, uniform replay / CartPole | 2.08 s | 963 |
| DQN / CartPole | 2.13 s | 941 |
| TD3 / Pendulum | 56.8 s | 35 |
| SAC / Pendulum | 99.5 s | 20 |

Four orders of magnitude across the set, and the reason is what each does per environment step:

- **Q-learning** does a dictionary lookup and one arithmetic update. No network at all.
- **A2C** runs a small forward pass per step, and one gradient step per 32.
- **PPO** collects 2,048 steps, then does 10 epochs over them in 64-sample minibatches.
- **DQN** does a **full gradient step on every environment step** by default, including a target
  network forward pass and a prioritised sample.
- **SAC and TD3** do the same, but one update touches an actor *and four critics* at 256×256, with
  a batch of 256.

The two DQN rows differ by 2.3%, with prioritised the slower of the pair — the right direction, and
about the size the replay table above predicts once the sampling cost is set against a ~1 ms
gradient step.

### On comparing these across runs

This table was re-measured after the Adam work, and most rows moved by 10–44%. **Almost none of that
is attributable to the change**, and it is worth saying why rather than banking the improvement.

Q-learning is the control: it uses no neural network and no optimiser at all, so the Adam rewrite
cannot touch it — and it still moved 27%. SAC moved 12% in the *wrong* direction, which the change
also cannot cause. Both point at the same thing: this is a laptop that had been running benchmarks
continuously for hours, and run-to-run variance at three iterations is larger than the effect being
looked for.

The clean measurement of the Adam change is [the network table](#the-network-itself), which isolates
the operation instead of burying it inside a training loop. At the batch sizes these agents use
(32–256) that table predicts 10% or less, which is exactly the regime where agent-level timings
cannot resolve it.

Treat the numbers here as *this machine, this afternoon* — useful for the shape of the ranking
across algorithms, which is stable and enormous, and not for 20% comparisons between runs.

### If this is too slow

`TrainFrequency` is the single biggest lever. A gradient step costs far more than an environment
step, so one update every 4 steps roughly quadruples throughput:

```csharp
new SacOptions { TrainFrequency = 4 }        // ~4× the steps per second
```

Then network size and batch. The console's [demo presets](08-console.md) use `[64, 64]` with a batch
of 64 and `TrainFrequency = 2` — about **ten times** SAC's default throughput, and still enough to
learn Pendulum. That is the difference between watching an agent learn and watching a still image.

The published defaults are tuned for the best final result over a long run, which is the right
target for a real training job and the wrong one for a demonstration.

## Allocation

The `Allocated` column on the agent benchmarks is **not** zero, and that is expected: those
benchmarks construct the agent inside the measured method, so the figure is the replay buffer and
the networks, allocated once. A DQN with a 100,000-entry buffer over a 4-dimensional observation
reserves about 4 MB for the buffer alone.

Steady-state allocation is zero, which is what the SIMD, environment and replay tables above show —
all `0 B` across millions of operations. `NeuralBenchmarks` isolates the forward-backward-update
cycle for the same reason.

Buffer memory is predictable:

```
bytes ≈ capacity × (2 × observationSize + actionSize + 2) × 4
```

A million transitions over an 8-dimensional observation is about 76 MB.

## GPU

Measured on this machine's integrated Intel UHD 620 over OpenCL, at a batch of 256 — the GPU never
wins here, but the gap narrows steadily with width. The full table and what it means for a discrete
card is on the [GPU page](06-gpu.md#read-this-first).

## Reproducing

```bash
# everything (about 40 minutes; the agent suite dominates)
dotnet run -c Release --project benchmarks/RLNet.Benchmarks

# one suite
dotnet run -c Release --project benchmarks/RLNet.Benchmarks -- --filter '*SimdBenchmarks*'
dotnet run -c Release --project benchmarks/RLNet.Benchmarks -- --filter '*ReplayBenchmarks*'
dotnet run -c Release --project benchmarks/RLNet.Benchmarks -- --filter '*GpuBenchmarks*'

# what accelerators are visible
dotnet run -c Release --project benchmarks/RLNet.Benchmarks -- --devices
```

`AgentBenchmarks` runs a short job — one warmup, three iterations — because a single iteration is
thousands of real gradient steps rather than a microbenchmark. A default run would take an hour, and
the run-to-run spread on something that long is already small enough that the extra iterations buy
no precision worth the wait. The DQN rows above are the caveat to that: at three iterations, a 6%
difference is not distinguishable from noise.

Release configuration is mandatory. BenchmarkDotNet refuses to run a Debug build, and rightly so.

## Next

- [Neural engine](05-neural-network.md) — why the SIMD numbers look like that
- [GPU](06-gpu.md) — where the accelerator crossover is
- [Troubleshooting](11-troubleshooting.md) — if your numbers are far from these
