# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Scope

`ActorNet/` is one project inside the **Vibe_Library** monorepo (the git root is one level up,
alongside `RLNet`, `LVGL.Net`, `D3Net`, `ClassicML`, …). Keep all work inside `ActorNet/`; do not
touch sibling projects.

Monorepo conventions to follow when adding infrastructure: workflows live in the **root**
`.github/workflows/`, are path-scoped (`paths: ['ActorNet/**', ...]`) and named after the single
project they cover (`actornet-ci.yml`, `actornet-publish.yml`) — see `rlnet-ci.yml` for the shape.
Release tags are namespaced (`ActorNet-v0.1.0`) because a bare `v*` tag is ambiguous across projects.
There is no root `Directory.Build.props` or `global.json`; each project owns its own.

## Commands

```bash
dotnet build -c Release      # single project, no solution file
dotnet run                   # interactive Spectre.Console menu (binds TCP :9000)
```

There is no test project, no benchmark project and no lint step — `dotnet test` has nothing to run.
The only "benchmark" is the *Run Benchmark (Throughput)* menu item in `Program.cs`; see the caveat
under **Sharp edges** before quoting a number from it.

`dotnet run` is interactive and holds the terminal. It also opens port 9000 unconditionally in
`Main`, so a second instance fails to listen.

## Current state vs. requirements.md

`requirements.md` is an aspirational spec, not a description of the code. What exists today is a
~500-line single-project demo: virtual-actor activation, channel mailboxes, one TCP hop, and a bank
account sample. **Not implemented** despite being claimed in `requirements.md` and `README.md`:
supervision trees, clustering, elastic scaling / load balancing, persistence and event sourcing,
reactive streams, the Blazor dashboard, the Avalonia samples, the Go/Python/NodeJS SDKs, the `docs/`
folder, the `Plan.md` roadmap/tracking checklist, CI, and the NuGet package.

Treat gaps as work to do, not as bugs — but do not write docs or a README that assume any of it
exists. Two conventions from `requirements.md` that the code has *not* yet adopted, and should when
files are touched: every source file carries a `Dibuat oleh Gravicode Studios, dipimpin oleh Kang
Fadhil` credit, and the README is bilingual (English + Bahasa Indonesia in one file — `README.md`
already follows this; keep both halves in sync).

If a NuGet package is added, check id availability first: the sibling project publishes as
`Gravicode.RLNet` because the bare `RLNet` id was already taken by an unrelated library, and NuGet
ids are case-insensitive.

## Architecture

```
Program.cs                      Spectre.Console menu + the throughput benchmark
Core/Interfaces.cs              IActor, IActorRef, IActorSystem, IActorContext, MessageEnvelope
Core/ActorSystem.cs             type registry, actor table, DispatchMessageAsync
Core/VirtualActor.cs            mailbox + processing loop; also holds ActorContext
Core/LocalActorRef.cs           Tell (Ask throws)
Core/Network/NodeListener.cs    TCP server
Core/Client/ActorNetClient.cs   TCP client
Core/Actors/BankAccountActor.cs sample actor + its message records
```

### The one thing to understand: every message is JSON, even locally

`VirtualActor.PushMessageAsync` serialises the message with Newtonsoft into a `MessageEnvelope`, and
`ProcessMailboxAsync` deserialises it back before calling `ReceiveAsync`. There is no local fast path.
Consequences that drive most design decisions here:

- `MessageType` is `Type.FullName + ", " + AssemblyName`. Resolution is `Type.GetType`, falling back
  to a scan of every loaded assembly, falling back to a raw `JObject`. Message types must therefore
  be reachable by name on the receiving side — with a single assembly this is free, but it is the
  constraint that will bite when the framework is actually split across nodes.
- Messages must be JSON round-trippable. The sample uses `record` types with positional parameters,
  which Newtonsoft handles via the constructor; a message that loses data through serialise/
  deserialise fails silently.
- Serialisation cost dominates, so this is where any real performance work starts.

### Addressing and activation

Actor IDs are `"{TypeName}/{key}"` — `DispatchMessageAsync` splits on `/` and looks `TypeName` up in
`_actorTypeRegistry`. `ActorOf<T>()` registers the type as a side effect; `SendMessageAsync` with a
hand-built string does not, so a message to an unregistered type is **dropped with a `Console.WriteLine`
and no exception**. Same for a malformed ID. Register types up front in `Main`.

The remote path re-enters the same `DispatchMessageAsync`: `NodeListener` hands it the whole
`MessageEnvelope`, and `PushMessageAsync` detects that case and unwraps it rather than double-encoding.
Local and remote delivery converge on one code path — keep it that way.

### Adding an actor

Derive from `VirtualActor`, override `ReceiveAsync` (and optionally `ActivateAsync`/`DeactivateAsync`),
put the message `record`s next to it as `Core/Actors/XxxActor.cs` does, then
`system.RegisterActorType<XxxActor>()` in `Program.cs`. `ReceiveAsync` runs one message at a time per
actor, so actor fields need no locking. Exceptions inside it are caught, logged and swallowed by the
mailbox loop — the actor keeps running with whatever state it had.

## Sharp edges

These are real defects in the current code, not style opinions. Know them before touching adjacent code.

- **Actors are never deactivated.** `_activeActors` only grows; nothing evicts on idle, so the
  "virtual actor" lifecycle is half-built. `DeactivateAsync` runs only from `ActorSystem.Stop`.
- **Activation races the mailbox.** `Initialize` starts the processing loop, and `ActivateAsync` is
  launched fire-and-forget via `Task.Run` inside the `GetOrAdd` factory. A message can be handled
  before state loading finishes. Anything that makes activation do real work (persistence) has to fix
  this first.
- **The benchmark measures enqueue, not processing.** `SendMessageAsync` awaits a write to an
  unbounded channel and returns; `Task.WhenAll` completes while the mailbox is still draining. The
  reported "msg/sec" is dispatch throughput. Any published figure needs a real drain barrier.
- **`Ask<TResponse>` throws `NotImplementedException`.** Only `Tell` works. `context.Reply` sends to
  `SenderActorId`, which must itself be a routable `Type/key` — the demo's `"RemoteClient"` sender is
  not, so replies to network clients go nowhere.
- **No TCP framing.** `NodeListener` treats each `ReadAsync` into a 4096-byte buffer as exactly one
  JSON envelope. Payloads over 4 KB, or two messages coalesced into one segment, corrupt the stream.
  `ActorNetClient` opens a fresh `TcpClient` per message and never reads a response. Adding a
  length-prefix is the prerequisite for any serious networking work.
- **No auth or type whitelist on the wire** (the `NodeListener` comment flags it). Deserialisation is
  driven by an attacker-supplied `MessageType`.
- **Nullable is enabled but the code is not clean** — 22 `CS86xx` warnings on a fresh build. Do not
  "fix all warnings" as a side quest, but leave new code warning-free.

`Interfaces.cs` declares `IActorSystem.Start(int port)` while `ActorSystem` also has a parameterless
`Start()` that uses the constructor's port; `Program.cs` uses the latter. Pick one when refactoring.

## Design assets

`.claude/skills/frontend-design/` is vendored into this project — `requirements.md` calls for it when
building the Blazor management dashboard and the Avalonia samples.
