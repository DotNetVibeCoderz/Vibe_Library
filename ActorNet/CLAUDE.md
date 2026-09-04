# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Scope

`ActorNet/` is one project inside the **Vibe_Library** monorepo (the git root is one level up,
alongside `RLNet`, `LVGL.Net`, `D3Net`, `ClassicML`, …). Keep all work inside `ActorNet/`; do not
touch sibling projects.

Monorepo conventions: workflows live in the **root** `.github/workflows/`, are path-scoped and named
after the single project they cover (`actornet-ci.yml`, `actornet-publish.yml`). Release tags are
namespaced (`ActorNet-v0.1.0`) because a bare `v*` tag is ambiguous across projects.

## Commands

```bash
dotnet build ActorNet.slnx -c Release
dotnet run --project tests/ActorNet.Tests -c Release                    # all 92 tests, ~3s
dotnet run --project src/ActorNet.Cli -c Release -- demo banking        # a worked scenario
dotnet run --project src/ActorNet.Cli -c Release -- bench -n 2000000    # throughput
dotnet run --project src/ActorNet.Dashboard                             # the web console
dotnet run --project samples/ActorNet.Samples.Avalonia                  # desktop samples
dotnet run -c Release --project benchmarks/ActorNet.Benchmarks -- --filter '*RoutingBenchmarks*'
dotnet pack ActorNet.slnx -c Release -o artifacts
```

**`dotnet test` does not work here.** The .NET 10 SDK dropped the VSTest bridge that xunit.v3's
Microsoft.Testing.Platform runner needs, and it fails with MSB4025 before running anything. The test
project is an executable — run it with `dotnet run`, which is what CI does. `dotnet.config` exists
for this; do not delete it assuming it is unused.

The suite is fast (~3s) and hermetic: networked tests bind port 0, so they never collide.

**Long-running processes lock their DLLs.** A `dotnet build` will fail with MSB3021 while a node,
the console or the samples are running. Kill them first (`taskkill //F //IM ActorNet.Cli.exe`).

## Architecture

Six projects. `ActorNet.Core` is the library and the only one that matters for most work.

```
src/ActorNet.Core/          the library (packs as "ActorNet")
  ActorId.cs                Type/Key addressing; splits on the FIRST separator
  ActorSystem.cs            the node: directory, routing, asks, shutdown, rebalancing
  Runtime/ActorCell.cs      one activation: instance, mailbox, loop, supervision
  Supervision/              directives, scope, restart budget
  Persistence/              PersistentActor, EventSourcedActor, in-memory + file stores
  Cluster/                  gossip membership, failure detection, HashRing
  Network/                  FrameCodec (length-prefixed), TcpTransport (persistent connections)
  Serialization/            the type allow-list
  Streams/, Metrics/, Hosting/, Client/
src/ActorNet.Demo/          banking, telemetry, ordering domains, shared by all surfaces
src/ActorNet.Cli/           Spectre.Console, packs as the "actornet" dotnet tool
src/ActorNet.Dashboard/     Blazor Server console; is itself a node
samples/…Avalonia/          four desktop scenarios over one shared node
tests/ActorNet.Tests/       xunit.v3, 92 tests
benchmarks/                 BenchmarkDotNet
clients/{nodejs,python,go}/ SDKs speaking the same wire protocol
```

### Five contracts that explain most of the design

**1. The local path does not serialize.** An in-process send puts the message object itself into the
mailbox. Serialization exists for the wire and nowhere else. Measured: placement ~95 ns, serializing
the same message ~630 ns and 288 B. Do not "unify" the paths.

**2. One actor, one thread of control — including its lifecycle.** Activation, every message,
supervision decisions and deactivation all run on the actor's own mailbox loop. Restart and stop are
posted as `SystemCommand`s into that same mailbox rather than applied from outside. This is what
closes the activation race and why application actors need no locks. Anything that reaches into an
`ActorCell` from another thread is a bug.

**3. Message types resolve through an explicit allow-list, never by name.** `MessageTypeRegistry`
maps a registered alias to a type. There is no `Type.GetType` fallback, by design — that is what
stops a peer choosing which type this process constructs, and it is what makes the cross-language
clients work.

**4. An unregistered actor type throws.** `ActorTypeNotRegisteredException`, not a dropped message
and a log line. A silently discarded message just becomes a hang somewhere else.

**5. The ring hash must be process-independent.** FNV-1a plus the MurmurHash3 finalizer, pinned in a
test against known vectors. `string.GetHashCode()` is randomised per process, so two nodes would
compute different rings and disagree about ownership — a bug that only appears in a real cluster.
The finalizer is not optional either: raw FNV-1a gave one node 48% of the keyspace, because ring
positions are short strings sharing a prefix.

## Things that have already been got wrong

Each of these is now covered by a test. Do not reintroduce them.

- **In-memory stores must deep-copy.** Holding a reference to state the actor keeps mutating made a
  snapshot taken at sequence 20 read back as the state at sequence 25, and recovery double-applied
  everything between. `StateCloning` exists for this.
- **A merged ring segment covering the whole circle has `Start == End`.** Read as a subtraction that
  is zero width, which made a single-node cluster report owning 0% of the keyspace. See
  `RingSegment.IsFullCircle`.
- **Dispatch is counted where a message is accepted for local handling**, not at the send. Counting
  forwarded messages made `InFlight` climb forever on any node that routes remotely.
- **The first node of a cluster has no seeds**, so it needs `Cluster.Enabled` set explicitly
  (`--cluster` on the CLI). Without it, clustering stays off and it never gossips, and peers mark a
  healthy seed node unreachable.
- **Razor string component parameters need `@`.** `Value="actor.Id"` passes the literal text;
  `Value="@actor.Id"` passes the value. Non-string parameters are expressions either way, which is
  why this only broke some of them.

## Benchmarking

The CLI bench reports dispatch throughput *and* drained throughput, and the drained figure is the
real one. A tell completes when the message is accepted into a mailbox, so timing a loop of tells
measures how fast the process fills a channel. Every measured path ends with an ask barrier per
actor, which is ordered behind everything already queued.

Run-to-run variance is 3.2–3.6M msg/s and 160–180 B/msg on the reference machine — larger than most
micro-optimisations. Measure several runs before believing an improvement.

Any figure quoted in the docs was measured on an Intel i7-8650U, .NET 10.0.11, Windows 11. If you
change something that moves a number, re-measure rather than adjusting the prose.

## Documentation

`docs/en/` and `docs/id/` are parallel — 12 files each, cross-linked in both directions. **Both must
be updated together**, and the README is bilingual in one file. A script-free way to check the links
still resolve is worth running after any rename.

`docs/images/` holds real screen captures of the running console and samples, not mockups. They were
taken by driving the apps and capturing the window; if a UI change makes one stale, retake it rather
than describing something the picture does not show.

`Plan.md` carries the roadmap and an honest checklist of what is *not* built. Keep it honest — a box
is ticked only when something automated proves the feature works.

## Package ids

Published as `ActorNet` (the library) and `ActorNet.Cli` (the dotnet tool). Unlike the sibling RLNet
project, the bare id was unclaimed. The CLI's assembly is `ActorNet.Cli`, not `actornet`: NuGet ids
are case-insensitive, and a project named `actornet` is ambiguous with the `ActorNet` package this
repository also produces. `ToolCommandName` gives the command its short name.

## Attribution

Every source file carries `// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.` and
`Directory.Build.props` puts the same credit into assembly metadata and NuGet fields. Keep both when
adding files.
