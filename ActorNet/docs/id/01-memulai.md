# Memulai

*Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.*
*[English](../en/01-getting-started.md) · [Indeks dokumentasi](README.md)*

## Kebutuhan

.NET 10 SDK. Tidak ada lagi — library ini hanya bergantung pada abstraksi `Microsoft.Extensions.*`,
jadi ia tidak akan menyeret implementasi logging atau DI tertentu ke dalam aplikasi Anda.

## Instalasi

```bash
dotnet add package ActorNet
```

Opsional, perkakas konsolnya:

```bash
dotnet tool install -g ActorNet.Cli
```

## Actor pertama

Tiga bagian: sebuah pesan, sebuah actor, dan sebuah system untuk menjalankannya.

```csharp
using ActorNet;

// Pesan berupa record. Ia hanya perlu selamat melewati perjalanan JSON kalau melintasi batas
// node; pengiriman in-process membawa objeknya langsung.
public sealed record Greet(string Name);
public sealed record Greeted(string Text, int TimesGreeted);

public sealed class GreeterActor : ReceiveActor
{
    private int _count;

    public GreeterActor()
    {
        On<Greet>(async (message, ct) =>
        {
            _count++;
            await Context.ReplyAsync(new Greeted($"Halo, {message.Name}", _count), ct);
        });
    }
}
```

`_count` tidak butuh lock. Satu aktivasi menangani satu pesan pada satu waktu, dan itulah jaminan
yang menjadi alasan keberadaan seluruh framework ini.

```csharp
var system = new ActorSystem(new ActorSystemOptions
{
    NodeId = "node-1",
    EnableNetworking = false,   // hanya in-process; tidak ada socket yang dibuka
});

system.RegisterActor<GreeterActor>();
await system.StartAsync();

var greeter = system.ActorOf<GreeterActor>("front-desk");

await greeter.TellAsync(new Greet("Fadhil"));
var reply = await greeter.AskAsync<Greeted>(new Greet("Sari"));

Console.WriteLine(reply.Text);          // Halo, Sari
Console.WriteLine(reply.TimesGreeted);  // 2

await system.DisposeAsync();
```

Perhatikan apa yang *tidak* terjadi: tidak ada yang mengonstruksi actor-nya. `ActorOf` mengembalikan
referensi ke sebuah alamat; pesan pertamalah yang mengaktifkannya. Itulah arti "virtual" di sini.

## Registrasi itu wajib

```csharp
system.RegisterActor<GreeterActor>();
```

Tanpa itu, pengiriman melempar `ActorTypeNotRegisteredException`. Ini disengaja. Alternatifnya —
memindai assembly untuk mencari nama tipe yang cocok — berarti sebuah peer di jaringan bisa menyebut
tipe apa pun di dalam proses Anda dan membuatnya dikonstruksi, dan itulah bentuk dari setiap eksploit
deserialisasi. Tipe yang belum terdaftar adalah bug perakitan, dan melempar exception memunculkannya
di tempat kejadian alih-alih mengubahnya menjadi pesan yang lenyap diam-diam.

## Tell dan ask

```csharp
await greeter.TellAsync(new Greet("Budi"));                     // kirim dan lupakan
var reply = await greeter.AskAsync<Greeted>(new Greet("Budi")); // tunggu balasan
```

`TellAsync` selesai saat pesan **diterima ke dalam mailbox**, bukan saat pesan itu ditangani. Itulah
yang membuatnya murah, dan itu pula yang perlu diingat saat menulis tes: sebuah `tell` yang langsung
diikuti assertion adalah sebuah race.

`AskAsync` menunggu actor memanggil `Context.ReplyAsync`, dan melempar `AskTimeoutException` bila
actor tidak pernah melakukannya. Karena sebuah ask diurutkan di belakang semua isi mailbox, ia
sekaligus berfungsi sebagai penghalang:

```csharp
for (var i = 0; i < 1000; i++) await greeter.TellAsync(new Greet("x"));

// Balasan ini tidak mungkin tiba sebelum 1.000 pesan di atas selesai ditangani.
var settled = await greeter.AskAsync<Greeted>(new Greet("terakhir"));
```

## Menambahkan persistensi

Ganti base class-nya dan state akan bertahan melewati deaktivasi:

```csharp
public sealed class GreeterState
{
    public int Count { get; set; }
}

public sealed class GreeterActor : PersistentActor<GreeterState>
{
    protected override Task ReceiveAsync(object message, CancellationToken ct) => message switch
    {
        Greet greet => Handle(greet, ct),
        _ => Task.CompletedTask,
    };

    private async Task Handle(Greet greet, CancellationToken ct)
    {
        State.Count++;
        await Context.ReplyAsync(new Greeted($"Halo, {greet.Name}", State.Count), ct);
    }
}
```

State dimuat saat aktivasi dan ditulis saat deaktivasi. Store bawaannya ada di memori, yang bertahan
melewati deaktivasi tapi tidak melewati restart proses — tukar dengan store berbasis file bila Anda
menginginkan yang kedua:

```csharp
options.StateStore = new FileStateStore("./data/state");
```

Lihat [Persistensi](05-persistensi.md) untuk model event-sourced, yang menyimpan riwayat alih-alih
nilai saat ini.

## Menambahkan node kedua

```csharp
var options = new ActorSystemOptions
{
    NodeId = "node-2",
    Port = 9001,
    EnableNetworking = true,
};

options.Cluster.Enabled = true;
options.Cluster.Seeds = ["127.0.0.1:9000"];
```

Node pertama sebuah cluster tidak punya seed sendiri, jadi ia perlu `Cluster.Enabled = true` dengan
daftar `Seeds` kosong — atau `--cluster` di CLI. Tanpa itu ia berjalan standalone dan tidak pernah
ber-gossip, dan peer-nya lama-lama menandai node yang sebenarnya sehat sebagai tidak terjangkau.

Setelah itu, tidak ada yang berubah di kode pemanggil Anda. `TellAsync` menanyakan ke hash ring siapa
pemilik key tersebut, lalu memasukkannya ke antrean lokal atau menyerahkannya ke transport.

## Menjalankan di dalam aplikasi

```csharp
builder.Services.AddActorNet(actors =>
{
    actors.Options.NodeId = "api-1";
    actors.Options.Port = 9000;
    actors.Actor<GreeterActor>();
    actors.Message<Greet>();
    actors.Message<Greeted>();
});
```

System-nya hidup dan mati bersama host, dan actor dikonstruksi lewat container — jadi mereka bisa
menerima repository atau `HttpClient` sebagai parameter konstruktor.

## Selanjutnya

- [Arsitektur](02-arsitektur.md) — apa yang terjadi antara pengiriman dan handler
- [Actor dan siklus hidup](03-actor.md) — aktivasi, deaktivasi, anak
- [Supervisi](04-supervisi.md) — apa yang terjadi saat handler melempar exception
- [Perkakas](09-perkakas.md) — CLI, konsol, dan sample
