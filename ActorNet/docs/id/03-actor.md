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

## Konkurensi, dinyatakan dengan tepat

**Dijamin:** satu aktivasi per alamat per cluster; satu pesan pada satu waktu dalam satu aktivasi;
pesan dari satu pengirim ke satu actor tiba berurutan.

**Tidak dijamin:** urutan antar pengirim berbeda; urutan melintasi batas deaktivasi (lihat
[Arsitektur](02-arsitektur.md)); pengiriman sama sekali, bila prosesnya mati.

## Selanjutnya

- [Supervisi](04-supervisi.md) — apa yang terjadi saat `ReceiveAsync` melempar exception
- [Persistensi](05-persistensi.md) — membuat state hidup lebih lama dari aktivasinya
