# Arsitektur

*Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.*
*[English](../en/02-architecture.md) · [Indeks dokumentasi](README.md)*

## Perjalanan satu pesan

```
system.TellAsync(ActorId("BankAccountActor", "alice"), new Deposit(100m))
│
├─ Cluster.IsLocal(id)?  ──── hash alamatnya, cari pemiliknya di ring
│
├─ ya ──→ GetOrCreateCell ─→ ActorCell.TryPostFast ─→ Channel<Envelope>
│                                                          │
│                                     loop milik actor ────┘
│                                     ├─ OnActivateAsync (hanya pesan pertama)
│                                     ├─ ReceiveAsync
│                                     └─ supervisi, bila melempar exception
│
└─ tidak ─→ serialisasi ─→ WireEnvelope ─→ TcpTransport ─→ node pemiliknya
                                                                │
                                        …dan mendarat di jalur lokal yang sama di atas
```

Ada lima hal yang layak dipahami dari gambar itu.

## 1. Alamat adalah keseluruhan cerita perutean

Sebuah `ActorId` berbentuk `Tipe/Key` — `BankAccountActor/alice`. Bagian tipe diselesaikan lewat
registry runtime untuk mengetahui class mana yang harus dibangun. Bagian key bersifat opak, dan
boleh mengandung `/` juga, sehingga penguraiannya hanya memecah pada pemisah **pertama**:
`DeviceActor/plant-3/line-2` adalah satu perangkat.

Penempatan meng-hash seluruh alamat. Dua node yang diberi daftar member yang sama akan menghitung
ring yang sama dan sepakat tentang setiap key tanpa saling bertanya. Kesepakatan itulah seluruh
anggaran koordinasi terdistribusi framework ini — tidak ada layanan direktori, tidak ada manajer
lock, dan tidak ada ronde konsensus.

Hash-nya adalah FNV-1a diikuti finalizer MurmurHash3, atas byte UTF-8. Ia sama sekali bukan
`string.GetHashCode()`, yang diacak per proses: dua node akan menghitung ring berbeda dari daftar
member yang sama dan berselisih tentang siapa memiliki apa — dan bug itu hanya muncul di cluster
sungguhan.

## 2. Jalur lokal tidak melakukan serialisasi

Pengiriman in-process memasukkan **objek pesannya sendiri** ke mailbox tujuan. Serialisasi hanya ada
untuk kabel dan tidak untuk hal lain.

Ini penyimpangan terbesar dari implementasi naif, dan asimetrinya sepadan: pengiriman lokal seharga
satu penulisan channel (dibandingkan dengan ~95 ns untuk penempatan), sementara menyerialisasi pesan
yang sama seharga ~630 ns dan 288 byte. Framework yang menyerialisasi secara lokal akan membayar itu
pada setiap pesan di deployment satu node tanpa manfaat sama sekali.

## 3. Satu actor, satu alur kendali

Setiap aktivasi adalah sebuah `ActorCell`: satu instance, satu mailbox, dan satu loop yang
menguraikannya.

```
ActorCell.RunAsync
  await OnActivateAsync          ← selesai sebelum satu pesan pun dibaca
  while (mailbox berisi pesan)
      StopCommand?    → deaktivasi
      RestartCommand? → bangun ulang instance di tempat
      selain itu      → ReceiveAsync, dan supervisi apa pun yang dilemparnya
```

Aktivasi berjalan di loop itu, setiap pesan berjalan di sana, keputusan supervisi tentang actor itu
berjalan di sana, dan deaktivasi berjalan di sana. Tidak ada yang dari luar menjangkau masuk dan
mengubah instance-nya — komponen lain *meminta*, dengan menaruh sebuah perintah ke mailbox yang sama.

Urutan itulah yang menutup race yang dimiliki implementasi naif. Kalau aktivasi ditembakkan dengan
`Task.Run` sementara loop mailbox sudah membaca, sebuah pesan bisa ditangani oleh actor yang state-nya
belum selesai dimuat. Di sini, pesan boleh mengantre selama aktivasi tapi tidak akan pernah ditangani
sebelum aktivasi selesai.

Itu juga alasan actor Anda tidak butuh lock. Field disentuh oleh tepat satu thread pada satu waktu,
dan framework tidak pernah memanggil actor Anda dari tempat lain.

## 4. Perubahan siklus hidup lewat mailbox

Sebuah restart atau stop tidak diterapkan dari luar. Ia dikirim sebagai `SystemCommand` ke antrean
milik actor itu sendiri, sehingga ia *terurut* terhadap pesan-pesan di sekitarnya dan berjalan di loop
milik actor itu.

Ongkosnya: sebuah stop menunggu di belakang apa pun yang sudah mengantre — dan itu persis arti kata
"anggun". Manfaatnya: perubahan siklus hidup tidak akan pernah mendarat di tengah `ReceiveAsync` yang
belum selesai.

## 5. Kabelnya berawalan panjang dan ber-allow-list

Setiap frame adalah empat byte panjang big-endian, lalu sebanyak itu byte JSON.

TCP adalah aliran byte, bukan aliran pesan. Memperlakukan "apa pun yang dikembalikan satu pembacaan"
sebagai satu pesan adalah bug klasik: ia bekerja di localhost dengan payload kecil dan rusak begitu
dua pengiriman menyatu dalam satu segmen atau satu pesan terbelah menjadi dua. Awalan panjanglah yang
membuat sebuah frame menjadi frame, dan ada batas ukuran supaya peer bermusuhan tidak bisa mengumumkan
frame 4 GB.

Payload masuk diselesaikan lewat **allow-list tipe eksplisit**, dikunci oleh alias terdaftar:

```csharp
[ActorMessage(Alias = "bank.deposit")]
public sealed record Deposit(decimal Amount, string Reference = "");
```

Tidak pernah `Type.GetType(namaDiKabel)`. Transport yang menyelesaikan nama apa pun yang datang
membiarkan peer memilih tipe mana yang dikonstruksi proses ini, dan itulah fondasi rantai gadget
deserialisasi. Itu juga yang membuat klien Go, Python, dan Node bisa bekerja: `bank.deposit` berarti
hal yang sama di mana pun, dan tidak ada pihak yang perlu tahu nama tipe pihak lain.

## Komponen

```
src/ActorNet.Core/
  ActorId.cs                 alamat
  ActorSystem.cs             node: direktori, perutean, ask, shutdown
  VirtualActor.cs            yang Anda turunkan
  ReceiveActor.cs            handler berdasarkan tipe pesan
  ActorSystemOptions.cs      semua yang bisa dikonfigurasi, divalidasi saat konstruksi

  Abstractions/              IActor, IActorRef, IActorContext, IActorSystem, exception
  Runtime/                   ActorCell (siklus hidup + supervisi), Mailbox, Envelope, ActorRef
  Supervision/               SupervisorStrategy, directive, cakupan, budget restart
  Persistence/               PersistentActor, EventSourcedActor, store memori dan file
  Serialization/             allow-list tipe dan serializer JSON
  Network/                   FrameCodec (framing), TcpTransport (koneksi)
  Cluster/                   gossip keanggotaan, deteksi kegagalan, HashRing
  Streams/                   operator ActorStream dan sink ke actor
  Metrics/                   penghitung dan snapshot
  Hosting/                   AddActorNet dan hosted service-nya
  Client/                    ActorNetClient, untuk proses yang bukan node
```

## Batas yang disengaja

**Deaktivasi punya jendela tumpang tindih kecil.** Saat sebuah cell berhenti, ia menutup mailbox-nya
dan menguras apa yang sudah diterimanya, sementara pengiriman baru membangun cell baru. Setiap pesan
tetap ditangani tepat sekali oleh tepat satu instance, tapi pesan yang diterima sesaat sebelum stop
bisa ditangani setelah pesan yang dikirim belakangan. Actor yang mempedulikan hal ini sebaiknya
persisten, supaya instance baru memuat ulang.

**Rebalancing melakukan deaktivasi, bukan migrasi.** Actor yang key-nya berpindah ke node lain
di-flush dan diaktifkan kembali di sana dari store. State yang hanya ada di memori tidak selamat dari
itu, secara sengaja: memigrasikan state hidup berarti protokol serah-terima terdistribusi, dan store
sudah menyelesaikan masalahnya.

**Pengiriman bersifat at-most-once.** Pesan yang sudah diterima ke mailbox akan ditangani kecuali
prosesnya mati. Tidak ada ack, tidak ada retry, tidak ada pengiriman ulang. At-most-once ditambah
handler idempoten adalah primitif yang jujur; lebih dari itu butuh log transaksi pada setiap
pengiriman.

## Selanjutnya

- [Actor dan siklus hidup](03-actor.md)
- [Clustering](06-clustering.md) — ring dan keanggotaan secara rinci
- [Performa](10-performa.md) — pengukuran di balik klaim di atas
