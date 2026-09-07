# Actor dan siklus hidup

*Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.*
*[English](../en/03-actors.md) · [Indeks dokumentasi](README.md)*

## Tiga base class

| Base | State disimpan | Pilih saat |
| --- | --- | --- |
| `VirtualActor` | Hanya di memori | Anda ingin `ReceiveAsync` mentah dengan switch |
| `ReceiveActor` | Hanya di memori | Anda ingin handler terdaftar per tipe pesan |
| `PersistentActor<TState>` | Satu record per actor | Yang penting nilai saat ini |
| `EventSourcedActor<TState>` | Journal append-only | Yang penting riwayatnya |

`ReceiveActor` adalah titik awal yang biasa:

```csharp
public sealed class CounterActor : ReceiveActor
{
    private int _total;

    public CounterActor()
    {
        On<Add>(m => _total += m.By);
        On<GetTotal>(async (_, ct) => await Context.ReplyAsync(new Total(_total), ct));
    }
}
```

Pesan tak tertangani melempar exception secara bawaan, jadi ia sampai ke supervisor Anda alih-alih
lenyap. Override `OnUnhandledAsync` bila mengabaikannya memang yang Anda inginkan.

## Siklus hidup

```
      ┌────────────────┐
      │  tidak aktif   │  ← alamatnya ada; tidak ada yang berjalan
      └───────┬────────┘
              │ pesan pertama tiba
              ▼
     OnActivateAsync             ← ditunggu sebelum satu pesan pun ditangani
              │
              ▼
      ┌────────────────┐
      │      aktif     │  ← ReceiveAsync, satu pesan pada satu waktu
      └───────┬────────┘
              │ timeout menganggur · DeactivateAsync · stop dari supervisi
              │ rebalance cluster · node berhenti
              ▼
    OnDeactivateAsync            ← tulis state di sini
              │
              ▼
      ┌────────────────┐
      │  tidak aktif   │  ← pesan berikutnya memulai siklusnya lagi
      └────────────────┘
```

Tidak ada yang membuat atau menghancurkan actor. `ActorOf` mengembalikan referensi ke sebuah alamat,
dan referensi itu tetap sah melewati setiap deaktivasi dan setiap perpindahan node.

## Aktivasi

```csharp
protected override async Task OnActivateAsync(CancellationToken ct)
{
    _rates = await _rateService.LoadAsync(Context.Self.Key, ct);
}
```

Dijamin selesai sebelum `ReceiveAsync` pertama. Pesan yang tiba selama aktivasi mengantre; mereka
tidak mungkin ditangani oleh actor yang setengah terinisialisasi.

Bila aktivasi melempar exception, actor **tidak** dijalankan dan pesan yang menunggunya hilang. Itu
disengaja: actor yang tidak bisa memuat state-nya akan gagal dengan cara yang sama setiap kali,
sehingga me-restart-nya adalah busy-loop. Kegagalannya dicatat sebagai `ActorActivationException`.

## Deaktivasi

```csharp
protected override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct)
{
    if (reason == DeactivationReason.Supervision) return;   // jangan tulis state setelah kegagalan
    await _repository.SaveAsync(_state, ct);
}
```

`DeactivationReason` memberi tahu alasannya:

| Alasan | Arti |
| --- | --- |
| `Idle` | Tidak ada pesan dalam batas waktu menganggur. Kasus normal. |
| `Requested` | `system.DeactivateAsync(id)` atau `Context.DeactivateOnIdle()`. |
| `Supervision` | Sebuah supervisor menghentikannya setelah kegagalan. |
| `Rebalanced` | Cluster memindahkan key ini ke node lain. |
| `Shutdown` | Node sedang berhenti. |

`PersistentActor` sudah melewatkan penulisan pada `Supervision` — menuliskan kembali state yang
mungkin ditinggalkan setengah jadi oleh pesan yang gagal adalah cara sebuah bug sementara menjadi
permanen.

Deaktivasi diberi waktu 30 detik. Lewat itu loop-nya dibatalkan dan apa pun yang sedang dikerjakannya
hilang.

## Deaktivasi karena menganggur

```csharp
options.IdleTimeout = TimeSpan.FromMinutes(5);
options.SweepInterval = TimeSpan.FromSeconds(15);
```

Penyapu berjalan setiap `SweepInterval` dan menghentikan actor yang menganggur melewati
`IdleTimeout` **dengan mailbox kosong** — syarat kedua itu penting, karena pekerjaan bisa datang di
antara pemeriksaan dan penghentian.

Inilah yang membuat modelnya masuk akal pada skala besar: satu juta perangkat terdaftar adalah satu
juta alamat, tapi memori hanya menampung yang sedang melapor.

Sebuah actor juga bisa mengundurkan diri:

```csharp
On<Finish>(_ => Context.DeactivateOnIdle());   // berhenti setelah pesan ini selesai
```

## Context

`Context` menggambarkan pesan yang **sedang ditangani**. Menangkapnya ke dalam task latar dan
membacanya kemudian akan memberi Anda pengirim milik orang lain.

| Anggota | |
| --- | --- |
| `Self` | Alamat actor ini |
| `Sender` | Pengirim pesan saat ini, atau `ActorId.None` |
| `Parent` | Actor yang mensupervisi, atau `ActorId.None` di akar |
| `System` | Node-nya |
| `Logger` | Logger yang tercakup ke actor ini |
| `RestartCount` | Berapa kali supervisor membangun ulang actor ini |
| `Children` | Alamat yang di-spawn actor ini dan masih hidup |
| `TellAsync` | Kirim, dengan actor ini sebagai pengirim |
| `ReplyAsync` | Jawab pesan saat ini |
| `SpawnChild<T>` | Buat anak yang disupervisi |
| `ScheduleTell` | Kirim ke diri sendiri setelah jeda |
| `DeactivateOnIdle` | Mundur setelah pesan saat ini |

## Membalas

```csharp
await Context.ReplyAsync(new Total(_total), ct);
```

`ReplyAsync` merutekan ke siapa pun yang menunggu, dengan urutan:

1. `AskAsync` yang tertunda, di node ini atau node lain — dicocokkan berdasarkan correlation id,
   bukan berdasarkan socket mana permintaannya tiba.
2. Bila tidak ada, ke `Sender`, sebagai pesan biasa.
3. Bila tidak ada juga, tidak ke mana-mana, dan ia mengembalikan `false`.

Kasus ketiga layak diperiksa bila Anda mengharapkan percakapan. `TellAsync` tanpa pengirim membuat
balasan tidak punya tujuan.

## Anak dan pohon supervisi

```csharp
On<OpenSession>(m =>
{
    var child = Context.SpawnChild<SessionActor>(m.SessionId);
    _sessions.Add(child.Id);
});
```

Seorang anak mewarisi strategi supervisi induknya kecuali diberi strategi sendiri, dan berhenti saat
induknya berhenti — anak lebih dulu, sehingga `OnDeactivateAsync` milik induk masih bisa
menjangkaunya.

Perhatikan bahwa anak adalah actor biasa yang bisa dialamatkan. `SpawnChild<SessionActor>("abc")`
membuat `SessionActor/abc`, yang bisa dialamatkan siapa pun secara langsung. Hubungan induk-anak
mengatur supervisi dan penghentian, bukan visibilitas.

## Penjadwalan

```csharp
_timer = Context.ScheduleTell(TimeSpan.FromSeconds(30), new Sweep(), repeatEvery: TimeSpan.FromSeconds(30));
```

Mengembalikan `IDisposable`; buang untuk membatalkan. Timer **tidak** selamat dari deaktivasi atau
restart node — ini untuk urusan dalam satu aktivasi, bukan penjadwalan yang tahan lama.

## Memanggil actor lewat interface

`AskAsync<Balance>(id, new GetBalance())` adalah tiga hal yang harus cocok — tipe permintaan, tipe
jawaban, dan handler di seberang sana — dan tidak ada yang memeriksa bahwa ketiganya cocok. Salah
satu saja keliru, kegagalannya berupa `AskTimeoutException` pada hari sial ketika jalur kode itu
pertama kali dijalankan.

Deklarasikan protokolnya sekali, dan kompiler yang memeriksanya:

```csharp
[ActorInterface]
public interface IBankAccount
{
    Task DepositAsync(decimal amount, string reference);
    Task<decimal> GetBalanceAsync(CancellationToken cancellationToken = default);
}
```

Sebuah source generator mengubahnya menjadi satu record permintaan per method, satu record jawaban
untuk method yang mengembalikan nilai, sebuah proxy, dan sebuah base class untuk actor-nya:

```csharp
public sealed class BankAccountActor : BankAccountActorBase   // dihasilkan generator
{
    private decimal _balance;

    public override Task DepositAsync(decimal amount, string reference)
    {
        _balance += amount;
        return Task.CompletedTask;
    }

    public override Task<decimal> GetBalanceAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_balance);
}

BankAccountProtocol.Register<BankAccountActor>(system);        // dihasilkan generator

var account = BankAccountProxy.Of<BankAccountActor>(system, "acct-001");
await account.DepositAsync(250m, "opening");
var balance = await account.GetBalanceAsync();
```

Tidak ada yang aneh yang dihasilkan. Record-nya membawa alias `[ActorMessage]` yang sama seperti
pesan tulis tangan, dan proxy-nya memanggil `TellAsync` dan `AskAsync` yang sama, jadi panggilan
proxy melintasi batas node menghasilkan lalu lintas yang sama persis dengan versi manualnya. Yang
berubah: ketidakcocokan antara pemanggil dan handler kini menjadi error kompilasi.

Empat aturan yang ditegakkan generator, masing-masing dengan pesan yang menjelaskan sebabnya:

| | |
| --- | --- |
| Method mengembalikan `Task`, `Task<T>`, `ValueTask`, atau `ValueTask<T>` | Sebuah panggilan adalah pesan dan balasan, jadi harus bisa di-`await` |
| Tanpa parameter `ref`, `out`, atau `in` | Parameter menjadi field pesan yang mungkin menyeberangi proses |
| Tanpa method generik | Tipe pesannya harus terdaftar sebelum tipe itu ada |
| Tanpa property atau event | Property terbaca sebagai akses sinkron ke state di thread lain |

**Method yang mengembalikan `Task` adalah `tell`**, dan selesai ketika pesannya diterima — bukan
ketika handler-nya rampung. Method yang mengembalikan `Task<T>` adalah `ask` dan menunggu balasan.
Itu perbedaan yang memang sudah ada, kini terlihat pada tanda tangannya.

**Tipe pelaksananya disebut di tempat pemanggilan**, karena alamat sebuah actor adalah nama tipenya
plus kuncinya, dan sebuah interface tidak tahu actor mana yang mengimplementasikannya. Batasan
generiknya itulah yang mencegah proxy diarahkan ke actor yang tidak berbicara protokol ini.

**Node yang hanya memanggil actor itu memakai `Register(system)`** tanpa argumen tipe: ia butuh
pesannya ada di allow-list, tapi tidak boleh mendaftarkan tipe actor yang tak akan pernah ia
tampung, karena ring akan mengira ia bisa memiliki kunci-kunci itu.

## Mengawasi actor lain

```csharp
await Context.WatchAsync(ActorId.For<PaymentActor>(orderId));

// ... nanti, di actor yang sama
On<Terminated>(t => Logger.LogWarning("{Actor} berhenti: {Reason}", t.Actor, t.Reason));
```

`Terminated` datang ketika actor yang diawasi berhenti **karena suatu sebab**: supervisor-nya
menyerah, atau ada yang memintanya berhenti. `Context.UnwatchAsync` menarik kembali minat itu.

**Ini sengaja lebih sempit daripada `Terminated` milik Akka.** Siklus hidup actor virtual bukan
siklus hidup actor Akka. Tiga dari lima alasan deaktivasi di sini hanyalah pembukuan rutin:

| Alasan | Memberi tahu | Sebabnya |
| --- | --- | --- |
| `Supervision` | **ya** | Supervisor menghentikannya setelah gagal |
| `Requested` | **ya** | Ada yang memintanya berhenti |
| `Idle` | tidak | Ia kehabisan waktu; pesan berikutnya membawanya kembali di alamat yang sama |
| `Rebalanced` | tidak | Ia pindah node; alamatnya tidak berubah |
| `Shutdown` | tidak | Node-nya berhenti; ia aktif lagi di tempat lain |

Melaporkan tiga yang terakhir akan melatih setiap pengawas untuk mengabaikan notifikasinya, dan itu
membuat dua yang bermakna ikut tak berguna.

Dua hal yang perlu diketahui:

**Pengawasan itu tersimpan di aktivasi milik target**, di mana pun ia berada dalam cluster.
`WatchAsync` adalah pesan biasa yang dirutekan oleh ring, jadi mengawasi actor di node lain sama saja
dengan mengawasi tetangga sebelah, dan pemberitahuannya kembali lewat kabel.

**Pengawasan tidak selamat dari matinya node yang memegangnya.** Kalau node pemilik target mati,
tidak ada yang datang — pendaftarannya ikut mati. Kehilangan node adalah peristiwa tingkat cluster
dan halaman cluster-lah tempatnya terlihat; notifikasi per-actor akan menjanjikan sesuatu yang tidak
bisa dipenuhi lapisan keanggotaan.

## Dependency injection

```csharp
public sealed class PricingActor(IPriceFeed feed, ILogger<PricingActor> logger) : ReceiveActor
{
    public PricingActor(...) { On<GetPrice>(...); }
}
```

Actor dibangun lewat `ActivatorUtilities` bila system punya `IServiceProvider`, jadi parameter
konstruktor diselesaikan dari container. Tanpa itu, actor butuh konstruktor tanpa parameter.

Inilah perbedaan antara actor yang bisa di-unit-test dan actor yang meraih variabel statis.

## Menanyakan isi sebuah actor

```csharp
var inspected = await system.InspectAsync(ActorId.For<WalletActor>("acc-1"));
Console.WriteLine(inspected.State);   // JSON
```

Tanpa ini, menjawab satu pertanyaan tentang actor yang sedang berjalan berarti menulis pesan
khusus, menanganinya, lalu mendaftarkan keduanya — masuk akal untuk pertanyaan yang sudah Anda
duga, tidak berguna pada pukul tiga pagi untuk pertanyaan yang tidak Anda duga.

Ini pesan biasa: dirutekan oleh ring, jadi ia menjawab untuk actor di node mana pun, dan ditangani
di loop milik actor itu sendiri, jadi ia mengantre di belakang apa pun yang sedang dikerjakan actor
tersebut dan tidak pernah membaca state yang sedang setengah ditulis sebuah handler. Actor yang
tidak menjawab berarti sedang sibuk atau macet, dan timeout-nya menyatakan itu.

`IInspectable` membuat actor menentukan sendiri apa yang ditampilkannya — lihat
[Perkakas](09-perkakas.md#actors) untuk apa yang dilakukan konsol dan endpoint HTTP terhadapnya.

## Konkurensi, dinyatakan dengan tepat

**Dijamin:** satu aktivasi per alamat per cluster; satu pesan pada satu waktu dalam satu aktivasi;
pesan dari satu pengirim ke satu actor tiba berurutan.

**Tidak dijamin:** urutan antar pengirim berbeda; urutan melintasi batas deaktivasi (lihat
[Arsitektur](02-arsitektur.md)); pengiriman sama sekali, bila prosesnya mati.

## Selanjutnya

- [Supervisi](04-supervisi.md) — apa yang terjadi saat `ReceiveAsync` melempar exception
- [Persistensi](05-persistensi.md) — membuat state hidup lebih lama dari aktivasinya
