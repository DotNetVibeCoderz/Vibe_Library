# Pemecahan masalah

*Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.*
*[English](../en/11-troubleshooting.md) · [Indeks dokumentasi](README.md)*

## `ActorTypeNotRegisteredException`

```
Actor type 'BankAccountActor' is not registered. Call RegisterActor<BankAccountActor>()
on the actor system before sending to it.
```

Persis seperti bunyinya. Bagian tipe dari sebuah alamat diselesaikan lewat registry eksplisit, tidak
pernah dengan memindai assembly — jadi setiap tipe actor harus didaftarkan sebelum apa pun dikirim
kepadanya, di **setiap node** yang mungkin memiliki key-nya.

Ini melempar exception alih-alih membuang pesannya secara sengaja: tipe yang tidak terdaftar adalah bug
perakitan, dan pesan yang dibuang diam-diam hanya mengubahnya menjadi hang di tempat lain.

## `AskTimeoutException`

```
No reply from 'BankAccountActor/alice' within 10,000 ms. The actor may not call ReplyAsync
for this message type.
```

Berdasarkan urutan kemungkinan:

1. **Handler-nya tidak pernah membalas.** Ask membutuhkan `Context.ReplyAsync`. Handler tanpa case yang
   cocok tidak membalas apa pun.
2. **Mailbox-nya panjang.** Sebuah ask terurut di belakang semua yang sudah mengantre. Bila actor-nya
   tertinggal 30.000 pesan, balasannya menunggu semuanya. Periksa kedalaman mailbox di konsol.
3. **Handler-nya lambat atau terblokir.** Panggilan sinkron yang memblokir di dalam `ReceiveAsync`
   menahan satu-satunya alur kendali actor itu.
4. **Sebuah node remote tidak terjangkau.** Balasannya tidak bisa kembali. Periksa halaman cluster.

Perhatikan apa yang **bukan** timeout: bila handler-nya **melempar exception**, Anda mendapat
`ActorNetException` yang membawa exception aslinya sebagai inner exception, bukan timeout. Timeout
berarti tidak ada apa pun yang kembali.

## `AskReplyTypeMismatchException`

```
Actor 'CounterActor/x' replied with Pong but the caller asked for Total.
```

Bug di sisi pemanggil, dan sengaja bukan timeout — balasannya memang tiba, hanya bukan yang diminta.

## `UnknownMessageTypeException`

```
No message type is registered under the alias 'Bank.Deposit'. Register it with
RegisterMessage<T>() on both nodes.
```

Setiap pesan yang melintasi batas node butuh alias terdaftar di **kedua** ujung. Biasanya salah satu
dari:

- Tipenya terdaftar di pengirim tapi tidak di penerima.
- Alias-nya berbeda — `[ActorMessage(Alias = "bank.deposit")]` di satu sisi, nama tipe lengkap bawaan
  di sisi lain.
- Sebuah klien eksternal memakai alias yang tidak dikenal node.

Alias defaultnya adalah nama tipe lengkap, jadi dua node yang menjalankan assembly yang sama sepakat
tanpa konfigurasi. Setel alias eksplisit begitu ada klien non-.NET yang terlibat.

Ini allow-list, bukan pencarian. Tidak ada fallback ke `Type.GetType`, secara sengaja.

## Sebuah actor terus me-restart

Periksa kolom **Restarts** di konsol, atau log-nya:

```
Restarted BankAccountActor/alice after InvalidOperationException (restart #7).
```

Lalu:

```
BankAccountActor/alice exceeded its restart budget (10 in 00:01:00); stopping instead of restarting.
```

Baris kedua itu adalah budget yang sedang bekerja. Tanpa itu, actor-nya akan me-restart selamanya pada
pesan yang sama sambil membakar satu core.

Biasanya penyebabnya **pesan beracun**: sesuatu di kepala mailbox yang gagal identik setiap kali. Ubah
strateginya menjadi `Resume` untuk kelas exception itu supaya pesan buruknya dibuang, atau perbaiki
handler-nya agar menolak pesan itu alih-alih melempar exception.

## State hilang setelah restart

Wajar, untuk actor di memori: restart membangun instance baru. Itulah yang ditunjukkan sample supervisi.

Bila Anda tidak mengharapkannya, turunkan dari `PersistentActor<TState>` atau
`EventSourcedActor<TState>`.

## State hilang setelah restart proses

Wajar, dengan store memori bawaan: ia selamat dari deaktivasi, bukan dari restart proses.

```csharp
options.StateStore    = new FileStateStore("./data/state");
options.EventJournal  = new FileEventJournal("./data/journal", types);
options.SnapshotStore = new FileSnapshotStore("./data/snapshots");
```

Atau `--data ./data` di CLI.

## State hilang setelah rebalance

Store bawaannya per-proses. Saat key sebuah actor berpindah ke node lain, ia diaktifkan di sana dan
tidak menemukan apa pun.

Cluster sungguhan butuh store yang bisa dibaca kedua node. Store memori, file, dan SQLite semuanya
bersifat per-proses; beralihlah ke PostgreSQL, SQL Server, MySQL, atau Redis:

```csharp
options.UsePostgreSql("Host=db;Database=actornet;Username=app;Password=…", types);
```

## Sebuah node tampil sebagai `Unreachable`

Heartbeat terlewat. Member yang tidak terjangkau **tetap di ring**, karena penyebab umumnya adalah jeda
GC atau gangguan sesaat dan memindahkan key-nya berongkos satu gelombang deaktivasi.

Bila ia tetap tidak terjangkau, periksa:

- `Host`/`Port` yang diiklankan node itu terjangkau *dari peer*, bukan sekadar ter-bind lokal.
- Firewall di antara keduanya.
- `HeartbeatInterval` terhadap `UnreachableAfter` — validator menolak kombinasi yang jelas salah, tapi
  node yang sangat terbebani tetap bisa melewatkan denyut.

## Sebuah seed node ditandai tidak terjangkau padahal sehat

Node pertama sebuah cluster tidak punya seed sendiri, jadi `Cluster.Enabled` tetap false kecuali Anda
menyatakan sebaliknya — dan node dengan clustering mati akan menjawab handshake join tapi tidak pernah
ber-gossip. Peer-nya lalu men-timeout-nya.

```bash
actornet run --node-id node-a --port 9000 --cluster
```

```csharp
options.Cluster.Enabled = true;
options.Cluster.Seeds = [];   // tanpa seed; yang lain bergabung ke node ini
```

## Dua node berselisih tentang siapa pemilik sebuah key

Semestinya tidak terjadi — hash-nya tidak bergantung proses dan ring-nya dibangun dari daftar member
yang terurut dan tanpa duplikat. Bila tetap terjadi:

- **Tabel member-nya berbeda.** Periksa kedua halaman cluster; mungkin belum konvergen.
- **`VirtualNodesPerMember` berbeda antar node.** Nilainya harus sama di mana-mana.
- **Dua node memakai `NodeId` yang sama.** Nilainya harus unik dan stabil.

## Memori tumbuh tanpa batas

Dua penyebab yang biasa.

**Mailbox tak berbatas dengan konsumen lambat.** Mailbox bawaannya tak berbatas, dan itulah yang
membuat sebuah `tell` murah sekaligus yang membiarkan selang pemadam memenuhi heap. Batasi:

```csharp
options.MailboxCapacity = 10_000;
```

Perhatikan angka **In flight** di konsol — kalau terus naik berarti pekerjaan datang lebih cepat
daripada yang diselesaikan.

**Actor yang tidak pernah menganggur.** Penyapu hanya menghentikan actor yang menganggur melewati
`IdleTimeout` dengan mailbox kosong. Actor yang menerima pesan setiap detik tidak pernah memenuhi
syarat. Bila Anda punya jutaan actor semacam itu, yang Anda butuhkan adalah lebih banyak node, bukan
batas waktu yang lebih pendek.

## `dotnet test` gagal dengan MSB4025

Wajar di sini. .NET 10 SDK menghapus jembatan VSTest yang dibutuhkan runner Microsoft.Testing.Platform
milik xunit.v3. Proyek tesnya adalah executable tersendiri:

```bash
dotnet run --project tests/ActorNet.Tests -c Release
```

`dotnet.config` di akar repositori ada untuk ini. Jangan menghapusnya dengan asumsi tidak terpakai.

## Konsol tidak menampilkan apa-apa

- Tidak ada actor yang aktif. Sebuah actor baru muncul setelah pesan pertamanya. Jalankan konsol dengan
  `ActorNet:GenerateLoad=true`, atau kirim sesuatu.
- Konsol menampilkan **satu node** — dirinya sendiri. Penghitung dan daftar actor bersifat lokal; hanya
  ring dan keanggotaan yang mencakup seluruh cluster. Agregasi lintas node ada di roadmap.

## Mendapatkan detail lebih banyak

```bash
actornet run -v                # logging runtime pada level debug
ACTORNET_DEBUG=1 actornet ...  # stack trace lengkap dari CLI
```

Pada level debug, runtime mencatat satu aktivasi dan satu deaktivasi untuk setiap actor, yang pada node
sibuk berarti ribuan baris per detik — dan itulah sebabnya ia bukan bawaan.

## Selanjutnya

- [Supervisi](04-supervisi.md)
- [Clustering](06-clustering.md)
- [Pelacakan pengembangan](../../Progress.md) — apa yang diketahui masih kurang
