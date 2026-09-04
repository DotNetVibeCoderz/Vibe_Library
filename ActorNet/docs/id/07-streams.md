# Streams

*Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.*
*[English](../en/07-streams.md) · [Indeks dokumentasi](README.md)*

## Cakupannya, dinyatakan jujur

`ActorStream` adalah `IAsyncEnumerable<T>` dengan beberapa operator dan satu sink ke actor. Ia
**bukan** porting Akka Streams: tidak ada DSL graf, tidak ada materialisasi, tidak ada fan-in
dinamis.

Justru cakupan itulah yang pantas ada. Ia mencakup "tarik dari sumber, bentuk, rutekan setiap item ke
actor yang memilikinya", yang memang dibutuhkan pipeline berbasis event di sini, dan ia berpadu
dengan setiap async enumerable lain di .NET alih-alih menciptakan dunia paralel.

## Bentuknya

```csharp
await ActorStream.From(readings)
    .Where(r => r.Celsius > -50)
    .Select(r => r with { Celsius = r.Celsius + calibration })
    .Batch(200, within: TimeSpan.FromSeconds(1))
    .ToActorsAsync(system, batch => ActorId.For<DeviceActor>(batch[0].DeviceId));
```

## Operator

| | |
| --- | --- |
| `Where(predicate)` | Simpan item yang cocok |
| `Select(selector)` | Proyeksikan |
| `SelectAsync(selector)` | Proyeksikan secara asinkron, satu per satu, urutan terjaga |
| `Take(count)` | Berhenti setelah N |
| `Batch(size, within)` | Kelompokkan jadi batch, dikirim lebih awal saat tenggat tercapai |
| `Buffer(capacity)` | Pisahkan produsen dan konsumen dengan antrean berbatas |
| `Tap(effect)` | Efek samping per item, item diteruskan |

### Kenapa `Batch` menerima batas waktu

Batcher yang hanya berdasarkan ukuran meninggalkan batch setengah penuh menganggur selamanya di
stream yang sepi. `within` itulah yang mencegah 37 pembacaan terakhir hari itu tidak pernah terkirim.
Pemeriksaannya berjalan per item, bukan lewat timer — timer akan butuh thread kedua dan lock atas
list-nya, dan ini sudah cukup untuk stream yang memang sedang menghasilkan.

## Sink

```csharp
// Rutekan berdasarkan key: setiap item mendarat di aktivasi yang memilikinya.
await stream.ToActorsAsync(system, item => ActorId.For<DeviceActor>(item.DeviceId));

// Semuanya ke satu actor.
await stream.ToActorAsync(system, ActorId.For<AlarmDeskActor>("main"));

// Jalankan saja.
var count = await stream.RunAsync(async (item, ct) => await Handle(item, ct));
```

`ToActorsAsync` adalah tempat stream bertemu model actor. Merutekan berdasarkan key berarti setiap
item untuk sebuah key mendarat di aktivasi tunggal milik key itu, sehingga **urutan per key dan state
penulis-tunggal didapat gratis** — tanpa skema partisi, tanpa lock, tanpa consumer group yang perlu
dikoordinasikan.

## Backpressure

Tidak ada protokol backpressure terpisah. Stream menarik, jadi laju konsumen adalah laju produsen.

Yang perlu diperhatikan:

- **Mailbox tak berbatas (bawaan) tidak memberi backpressure.** `ToActorsAsync` akan dengan senang
  hati memenuhi memori bila actor-nya tidak sanggup mengejar.
- **Mailbox berbatas meneruskannya.** Setel `options.MailboxCapacity` dan sebuah pengiriman berhenti
  selesai secara sinkron begitu actor-nya tertinggal, yang mendorong perlambatannya kembali lewat
  stream sampai ke sumber.
- **`Buffer(n)` menyerap lonjakan**, bukan ketidakcocokan berkelanjutan. Ia peredam kejut, bukan
  perbaikan.

Bila sebuah stream memberi makan actor dari selang pemadam eksternal, batasi mailbox-nya. Itu satu
setelan yang mengubah "konsumen lambat" dari kehabisan memori menjadi sekadar perlambatan.

## Kegagalan

Kegagalan produsen menjalar ke konsumen, termasuk lewat `Buffer`:

```csharp
await Assert.ThrowsAsync<InvalidOperationException>(async () =>
    await ActorStream.From(Failing()).Buffer(4).RunAsync());
```

Stream ber-buffer yang menelan exception produsennya akan tampak seperti stream yang sekadar berakhir,
dan pemanggilnya tidak akan pernah tahu ia kehilangan data.

Sisi sink berbeda: `ToActorsAsync` memakai `TellAsync`, jadi kegagalan *di dalam* sebuah actor adalah
urusan supervisor-nya, bukan urusan stream. Stream melihat pengiriman yang sukses. Bila stream perlu
tahu, pakai ask di dalam `RunAsync`.

## Interop

```csharp
IAsyncEnumerable<T> raw = stream.AsAsyncEnumerable();   // keluar
ActorStream.From(channel.Reader.ReadAllAsync());        // masuk
ActorStream.From(File.ReadLinesAsync(path));
ActorStream.Interval(TimeSpan.FromSeconds(1), tick => new Heartbeat(tick));
```

Apa pun yang menghasilkan `IAsyncEnumerable` adalah sumber — sebuah channel, sebuah file, kueri EF
Core, atau pembungkus consumer Kafka.

## Contoh utuh

Sample telemetri, lengkap:

```csharp
await ActorStream
    .Interval(TimeSpan.FromMilliseconds(30), tick => tick)
    .Select(tick =>
    {
        var device = (int)(tick % deviceCount);
        return new SensorReading($"sensor-{device:D3}", ReadTemperature(device), DateTimeOffset.UtcNow);
    })
    .ToActorsAsync(system, reading => ActorId.For<DeviceActor>(reading.DeviceId), cancellationToken);
```

Setiap actor perangkat menyimpan agregat berjalan tanpa penguncian, mengangkat alarm ke satu actor
meja alarm saat suhunya berlebih, dan disapu saat berhenti melapor.

![Telemetri: satu stream ke satu actor per perangkat](../images/samples-telemetry.png)

## Belum dibangun

- Operator merge, split, dan fan-in
- Posisi stream yang tahan restart

Lihat [roadmap](../../Plan.md).

## Selanjutnya

- [Actor dan siklus hidup](03-actor.md)
- [Performa](10-performa.md)
