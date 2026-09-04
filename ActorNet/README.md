# ActorNet

A hybrid actor framework for .NET 10. Orleans-style virtual actors with Akka-style supervision,
clustering, persistence, event sourcing and reactive streams.

[![ActorNet CI](https://github.com/DotNetVibeCoderz/Vibe_Library/actions/workflows/actornet-ci.yml/badge.svg)](https://github.com/DotNetVibeCoderz/Vibe_Library/actions/workflows/actornet-ci.yml)

> *Bahasa Indonesia: [scroll down](#actornet-bahasa-indonesia) atau buka [docs/id/](docs/id/).*

---

## The idea

Orleans makes distributed systems approachable by hiding the lifecycle: you address an actor and it
is there. Akka.NET makes them survivable by exposing it: you decide what happens when one fails.
Most teams want both and have to pick one.

They are not actually in tension. A virtual actor can have a supervision strategy; a grain can be
event-sourced. They answer different questions — *when does this exist?* and *what happens when it
breaks?* — and ActorNet answers both.

```csharp
var system = new ActorSystem(new ActorSystemOptions { NodeId = "node-1", Port = 9000 });

system.RegisterActor<BankAccountActor>();   // how to build one
await system.StartAsync();

// Nothing was created. This message activates the actor if it is not already running,
// on whichever node owns the key.
await system.TellAsync(ActorId.For<BankAccountActor>("alice"), new Deposit(100m));

var statement = await system.AskAsync<Statement>(
    ActorId.For<BankAccountActor>("alice"), new GetStatement());
```

### What that buys you

**No locks in your domain code.** One activation per address, one message at a time. The banking
sample fires 800 concurrent deposits at one account from 16 tasks and lands on exactly the right
balance, with no synchronisation anywhere in the actor.

**No lifecycle management.** An actor is activated by its first message and swept when it has been
idle. A persistent one flushes on the way out and reloads on the way back, so the caller never
learns it went away.

**Failure as a policy, not a crash.** The same exception can be resumed past, restarted through, or
fatal — and which one is a registration argument, not something the actor codes around.

**Location transparency that means something.** Callers never branch on where an actor lives. Add a
node and roughly 1/N of the actors migrate; nothing else moves.

---

## Screenshots

The web console. The ring is the placement view the node is actually routing by — one arc per span
of hash space, coloured by its owning node, with a probe that hashes an address and shows where it
lands.

![ActorNet console, cluster page](docs/images/console-cluster.png)

![ActorNet console, overview](docs/images/console-overview.png)

The desktop samples. Four scenarios over one shared node; each states the property it demonstrates
and then proves it.

![Avalonia sample, banking](docs/images/samples-banking.png)

![Avalonia sample, supervision](docs/images/samples-supervision.png)

More in [docs/images/](docs/images/).

---

## Install

```bash
dotnet add package ActorNet
dotnet tool install -g ActorNet.Cli    # the "actornet" command
```

## Try it in a minute

```bash
actornet demo banking       # event-sourced accounts, 200 concurrent deposits
actornet demo ordering      # a saga that compensates when payment fails
actornet demo lifecycle     # activate, deactivate, reactivate from the journal
actornet monitor --load     # a live terminal dashboard
actornet bench -n 2000000   # throughput, counting messages actually handled
```

From the repository, without installing:

```bash
dotnet run --project src/ActorNet.Cli -- demo banking
```

## Run a cluster

```bash
actornet run --node-id node-a --port 9000 --cluster
actornet run --node-id node-b --port 9001 --seed 127.0.0.1:9000
actornet cluster --port 9002 --seed 127.0.0.1:9000 --watch
```

The first node needs `--cluster`: it has no seeds of its own, and without the flag it runs
standalone and never gossips.

## The console

```bash
dotnet run --project src/ActorNet.Dashboard
```

It is itself a node, so the numbers are the runtime's own counters rather than a scrape. Join it to
a cluster with `--ActorNet:Seeds:0=127.0.0.1:9000`.

---

## Writing an actor

```csharp
public sealed class BankAccountActor : EventSourcedActor<AccountState>
{
    protected override long SnapshotEvery => 200;

    // The only place state changes. Runs again on every recovery, so it must be pure.
    protected override void Apply(object domainEvent)
    {
        if (domainEvent is Deposited d) State.Balance += d.Amount;
    }

    protected override async Task ReceiveAsync(object message, CancellationToken ct)
    {
        switch (message)
        {
            case Deposit deposit:
                // Written to the journal first, then applied. Applying first would let the actor
                // acknowledge a change the journal then refused.
                await PersistAsync(new Deposited(deposit.Amount, DateTimeOffset.UtcNow), ct);
                await Context.ReplyAsync(new Accepted(State.Balance), ct);
                break;

            case GetStatement:
                await Context.ReplyAsync(new Statement(State.Balance), ct);
                break;
        }
    }
}
```

Three base classes, picked by what the actor needs:

| Base | State lives | Use it when |
| --- | --- | --- |
| `VirtualActor` / `ReceiveActor` | In memory only | State is a cache, or genuinely disposable |
| `PersistentActor<TState>` | One row per actor | The current value is what matters |
| `EventSourcedActor<TState>` | An append-only journal | The history matters — audit, replay, CQRS |

Behind them, seven storage providers - all passing one shared conformance suite, so they are
interchangeable:

```bash
dotnet add package ActorNet.Persistence.PostgreSql   # or .SqlServer, .MySql, .Sqlite, .Redis
```

```csharp
options.UsePostgreSql("Host=db;Database=actornet;Username=app;Password=…", types);
```

| Provider | Survives a restart | Shared between nodes |
| --- | --- | --- |
| In-memory (default), for development and tests | no | no |
| Files, SQLite | yes | no |
| PostgreSQL, SQL Server, MySQL/MariaDB | yes | **yes** |
| Redis | configurable | **yes** |

A cluster needs a shared store: rebalancing deactivates an actor on one node and reactivates it on
another, which only recovers state if both can read the same place.

## Supervision

```csharp
system.RegisterActor<PaymentActor>(new OneForOneStrategy(ex => ex switch
{
    InsufficientFunds => Directive.Resume,   // drop the message, keep the state
    TimeoutException  => Directive.Restart,  // fresh instance, same address
    _                 => Directive.Escalate, // let the parent decide
})
{
    MaxRestarts = 5,
    Window = TimeSpan.FromMinutes(1),
});
```

The budget matters: without it a poison message buys a fresh instance forever and burns a core.
Past the budget the directive is downgraded to `Stop`.

## Streams

```csharp
await ActorStream.From(readings)
    .Where(r => r.Celsius > -50)
    .Batch(200, within: TimeSpan.FromSeconds(1))
    .ToActorsAsync(system, batch => ActorId.For<DeviceActor>(batch[0].DeviceId));
```

Routing by key is what makes a stream fit the actor model: every reading for a device lands on that
device's single activation, so per-key ordering and single-writer state come for free.

## Hosting

```csharp
builder.Services.AddActorNet(actors =>
{
    actors.Options.NodeId = "api-1";
    actors.Options.Cluster.Seeds = ["10.0.1.4:9000"];
    actors.Actor<BankAccountActor>().MessagesFromAssembly(typeof(Deposit).Assembly);
});
```

Actors are built through the container, so they take dependencies through their constructors.

---

## Clients

Four SDKs, one protocol — a 4-byte big-endian length followed by JSON. There is no separate HTTP
gateway to deploy or keep in sync with the runtime.

```javascript
const client = new ActorNetClient({ port: 9000 });
await client.tell('BankAccountActor/alice', 'bank.deposit', { Amount: 500 });
const reply = await client.ask('BankAccountActor/alice', 'bank.get-statement', {});
```

```python
async with ActorNetClient(port=9000) as client:
    await client.tell("DeviceActor/sensor-001", "iot.reading", {"Celsius": 21.5})
    status = await client.ask("DeviceActor/sensor-001", "iot.get-status", {})
```

```go
client := actornet.New("127.0.0.1:9000")
reply, err := client.Ask(ctx, "OrderSagaActor/order-1", "order.get", map[string]any{})
```

Messages travel under a registered **alias**, not a .NET type name, and the node resolves aliases
through an explicit allow-list. That is what stops a peer from naming a type for the node to
construct, and it is also what lets a Go process address the same actors as a C# one. See
[clients/README.md](clients/README.md).

---

## Performance

Measured on an Intel i7-8650U (4 physical cores, 8 logical), .NET 10.0.11, Windows 11.

| | |
| --- | --- |
| Local messages, drained | **3.2–3.6M msg/s** (8 actors, 8 senders, 2M messages) |
| Hash one key | 39 ns, no allocation |
| Place a key on a 3-member ring | ~95 ns, no allocation |
| Serialize one message | 631 ns, 288 B |
| Serialize and deserialize | 1,230 ns, 480 B |

Two things worth reading off that table:

**The throughput figure counts messages actually handled.** The benchmark ends with an ask barrier
per actor, which is ordered behind everything already in that mailbox, so its reply proves the
queue drained. A tell completes when the message is *accepted*, so timing a loop of tells measures
how fast the process can fill a channel — a much larger number that says nothing about the runtime.

**Serialization costs 6× what placement does**, which is why the local send path carries the
message object itself and only the wire serializes.

It is an in-process micro-benchmark: no network hop, no persistence, and a handler that does
nothing but increment. It measures the runtime's floor, not an application's throughput. Reproduce
it with `actornet bench` or `dotnet run -c Release --project benchmarks/ActorNet.Benchmarks`.

---

## Documentation

| | |
| --- | --- |
| [Getting started](docs/en/01-getting-started.md) | Install, first actor, first cluster |
| [Architecture](docs/en/02-architecture.md) | How a message gets from a send to a handler |
| [Actors and lifecycle](docs/en/03-actors.md) | Activation, deactivation, context, children |
| [Supervision](docs/en/04-supervision.md) | Directives, scope, budgets, escalation |
| [Persistence](docs/en/05-persistence.md) | Grain state, event sourcing, snapshots, stores |
| [Clustering](docs/en/06-clustering.md) | Membership, the hash ring, rebalancing |
| [Streams](docs/en/07-streams.md) | Operators and routing into actors |
| [Clients](docs/en/08-clients.md) | The wire protocol and the four SDKs |
| [Tooling](docs/en/09-tooling.md) | The CLI, the console, the samples |
| [Performance](docs/en/10-performance.md) | What was measured, and what it does not show |
| [Troubleshooting](docs/en/11-troubleshooting.md) | The failures people actually hit |

[Roadmap and development checklist](Plan.md) — including an honest list of what is *not* built.

## Building

```bash
dotnet build ActorNet.slnx -c Release
dotnet run --project tests/ActorNet.Tests -c Release          # 197 tests
```

`dotnet test` does not work here: the .NET 10 SDK dropped the VSTest bridge that xunit.v3's
Microsoft.Testing.Platform runner needs. The test project is an executable — run it directly, which
is also what CI does.

## License

MIT. See [LICENSE](../LICENSE).

---

Dibuat oleh **Gravicode Studios**, dipimpin oleh **Kang Fadhil**.

---
---

# ActorNet (Bahasa Indonesia)

Framework actor hibrida untuk .NET 10. Virtual actor ala Orleans dengan supervisi ala Akka,
clustering, persistensi, event sourcing, dan reactive streams.

## Gagasannya

Orleans membuat sistem terdistribusi mudah didekati dengan menyembunyikan siklus hidup: Anda
mengalamatkan sebuah actor, dan actor itu ada. Akka.NET membuatnya tahan banting dengan
menampakkannya: Anda yang menentukan apa yang terjadi saat sebuah actor gagal. Kebanyakan tim
menginginkan keduanya, lalu terpaksa memilih salah satu.

Sebenarnya keduanya tidak bertentangan. Sebuah virtual actor bisa punya strategi supervisi; sebuah
grain bisa event-sourced. Keduanya menjawab pertanyaan yang berbeda — *kapan ini ada?* dan *apa yang
terjadi kalau ini rusak?* — dan ActorNet menjawab keduanya.

```csharp
var system = new ActorSystem(new ActorSystemOptions { NodeId = "node-1", Port = 9000 });

system.RegisterActor<BankAccountActor>();   // cara membangunnya
await system.StartAsync();

// Tidak ada yang dibuat. Pesan ini mengaktifkan actor bila belum berjalan,
// di node mana pun yang memiliki key tersebut.
await system.TellAsync(ActorId.For<BankAccountActor>("alice"), new Deposit(100m));
```

### Apa yang Anda dapat

**Tanpa lock di kode domain.** Satu aktivasi per alamat, satu pesan pada satu waktu. Sample banking
menembakkan 800 setoran bersamaan dari 16 task ke satu rekening dan mendarat pada saldo yang persis
benar, tanpa sinkronisasi apa pun di dalam actor-nya.

**Tanpa manajemen siklus hidup.** Actor diaktifkan oleh pesan pertamanya dan disapu saat menganggur.
Actor persisten menulis state-nya saat keluar dan memuatnya kembali saat masuk, sehingga pemanggil
tidak pernah tahu actor itu sempat hilang.

**Kegagalan sebagai kebijakan, bukan crash.** Exception yang sama bisa dilewati (resume), dijalani
ulang (restart), atau fatal — dan pilihannya adalah argumen registrasi, bukan sesuatu yang harus
diakali di dalam actor.

**Transparansi lokasi yang berarti.** Pemanggil tidak pernah bercabang berdasarkan lokasi actor.
Tambahkan satu node dan kira-kira 1/N actor bermigrasi; sisanya tidak bergerak.

## Instalasi

```bash
dotnet add package ActorNet
dotnet tool install -g ActorNet.Cli    # perintah "actornet"
```

## Coba dalam satu menit

```bash
actornet demo banking       # rekening event-sourced, 200 setoran bersamaan
actornet demo ordering      # saga yang melakukan kompensasi saat pembayaran gagal
actornet demo lifecycle     # aktivasi, deaktivasi, aktivasi ulang dari journal
actornet monitor --load     # dashboard terminal langsung
actornet bench -n 2000000   # throughput, menghitung pesan yang benar-benar ditangani
```

## Menjalankan cluster

```bash
actornet run --node-id node-a --port 9000 --cluster
actornet run --node-id node-b --port 9001 --seed 127.0.0.1:9000
actornet cluster --port 9002 --seed 127.0.0.1:9000 --watch
```

Node pertama membutuhkan `--cluster`: ia tidak punya seed sendiri, dan tanpa flag itu ia berjalan
standalone dan tidak pernah ber-gossip.

## Tiga base class

| Base | State disimpan | Pakai saat |
| --- | --- | --- |
| `VirtualActor` / `ReceiveActor` | Hanya di memori | State berupa cache, atau memang boleh hilang |
| `PersistentActor<TState>` | Satu baris per actor | Yang penting adalah nilai saat ini |
| `EventSourcedActor<TState>` | Journal append-only | Yang penting riwayatnya — audit, replay, CQRS |

Di belakangnya ada tujuh provider penyimpanan — semuanya lulus satu suite konformans yang sama,
sehingga bisa saling menggantikan:

```bash
dotnet add package ActorNet.Persistence.PostgreSql   # atau .SqlServer, .MySql, .Sqlite, .Redis
```

| Provider | Selamat dari restart | Dibagi antar node |
| --- | --- | --- |
| Memori (bawaan), untuk pengembangan dan tes | tidak | tidak |
| File, SQLite | ya | tidak |
| PostgreSQL, SQL Server, MySQL/MariaDB | ya | **ya** |
| Redis | bisa dikonfigurasi | **ya** |

Cluster membutuhkan store bersama: rebalancing menonaktifkan actor di satu node dan mengaktifkannya
di node lain, dan itu hanya memulihkan state bila keduanya bisa membaca tempat yang sama.

## Klien

Empat SDK, satu protokol — panjang 4 byte big-endian diikuti JSON. Tidak ada gateway HTTP terpisah
yang harus di-deploy atau dijaga tetap sinkron dengan runtime.

Pesan berjalan dengan **alias** terdaftar, bukan nama tipe .NET, dan node menyelesaikan alias lewat
allow-list eksplisit. Itulah yang mencegah sebuah peer menyebut tipe apa pun untuk dibangun node,
sekaligus yang memungkinkan proses Go mengalamatkan actor yang sama dengan proses C#. Lihat
[clients/README.md](clients/README.md).

## Performa

Diukur pada Intel i7-8650U (4 core fisik, 8 logis), .NET 10.0.11, Windows 11.

| | |
| --- | --- |
| Pesan lokal, terkuras habis | **3,2–3,6 juta pesan/detik** (8 actor, 8 pengirim, 2 juta pesan) |
| Hash satu key | 39 ns, tanpa alokasi |
| Menempatkan key di ring 3 member | ~95 ns, tanpa alokasi |
| Serialisasi satu pesan | 631 ns, 288 B |
| Serialisasi dan deserialisasi | 1.230 ns, 480 B |

**Angka throughput menghitung pesan yang benar-benar ditangani.** Benchmark diakhiri dengan
penghalang berupa satu `ask` per actor, yang antriannya berada di belakang semua isi mailbox, jadi
balasannya membuktikan antrean sudah terkuras. Sebuah `tell` selesai saat pesan *diterima*, jadi
mengukur perulangan `tell` hanya mengukur seberapa cepat proses ini mengisi channel — angka yang
jauh lebih besar namun tidak mengatakan apa pun tentang runtime-nya.

Ini benchmark in-process: tanpa hop jaringan, tanpa persistensi, dan handler-nya hanya menambah
penghitung. Ia mengukur batas bawah runtime, bukan throughput sebuah aplikasi.

## Dokumentasi

Dokumentasi lengkap dalam Bahasa Indonesia ada di [docs/id/](docs/id/), sejajar dengan versi
Inggrisnya:

| | |
| --- | --- |
| [Memulai](docs/id/01-memulai.md) | Instalasi, actor pertama, cluster pertama |
| [Arsitektur](docs/id/02-arsitektur.md) | Perjalanan pesan dari kirim sampai handler |
| [Actor dan siklus hidup](docs/id/03-actor.md) | Aktivasi, deaktivasi, context, anak |
| [Supervisi](docs/id/04-supervisi.md) | Directive, cakupan, budget, eskalasi |
| [Persistensi](docs/id/05-persistensi.md) | State grain, event sourcing, snapshot, store |
| [Clustering](docs/id/06-clustering.md) | Keanggotaan, hash ring, rebalancing |
| [Streams](docs/id/07-streams.md) | Operator dan perutean ke actor |
| [Klien](docs/id/08-klien.md) | Protokol wire dan keempat SDK |
| [Perkakas](docs/id/09-perkakas.md) | CLI, konsol, dan sample |
| [Performa](docs/id/10-performa.md) | Apa yang diukur, dan apa yang tidak ditunjukkannya |
| [Pemecahan masalah](docs/id/11-pemecahan-masalah.md) | Kegagalan yang benar-benar sering terjadi |

[Roadmap dan checklist pengembangan](Plan.md) — termasuk daftar jujur tentang apa yang *belum*
dibangun.

## Lisensi

MIT. Lihat [LICENSE](../LICENSE).

---

Dibuat oleh **Gravicode Studios**, dipimpin oleh **Kang Fadhil**.
