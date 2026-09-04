# Clustering

*Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.*
*[English](../en/06-clustering.md) · [Indeks dokumentasi](README.md)*

## Menjalankannya

```bash
actornet run --node-id node-a --port 9000 --cluster
actornet run --node-id node-b --port 9001 --seed 127.0.0.1:9000
actornet run --node-id node-c --port 9002 --seed 127.0.0.1:9000
```

Node pertama membutuhkan `--cluster`. Ia tidak punya seed sendiri, dan tanpa flag itu ia berjalan
standalone: ia akan menjawab handshake join tapi tidak pernah ber-gossip, sehingga peer-nya lama-lama
menandai node yang sebenarnya sehat sebagai tidak terjangkau.

Dalam kode:

```csharp
options.Cluster.Enabled = true;
options.Cluster.Seeds = ["10.0.1.4:9000", "10.0.1.5:9000"];
```

Satu seed yang terjangkau sudah cukup — pihak yang bergabung menerima seluruh tabel member dan
ber-gossip dari sana. Daftarkan dua atau tiga supaya restart tidak bergantung pada satu mesin saja.

## Apa yang terlihat

![Halaman cluster: ring dan tabel member](../images/console-cluster.png)

Tiga member, masing-masing dengan 128 replika di ring, memiliki 36,2%, 32,3%, dan 31,6% dari
keyspace. Garis-garisnya adalah virtual node, dan cara mereka berselang-seling itulah intinya — lihat
di bawah.

## Penempatan: hash ring

Setiap alamat actor di-hash ke sebuah ring 64-bit. Setiap member ditempatkan di
`VirtualNodesPerMember` posisi, dan sebuah key menjadi milik posisi pertama pada atau setelah
hash-nya.

```csharp
var owner = system.Cluster.OwnerOf(ActorId.For<BankAccountActor>("alice"));
var mine  = system.Cluster.IsLocal(ActorId.For<BankAccountActor>("alice"));
```

**Kenapa consistent hashing dan bukan `hash % jumlahMember`?** Karena apa yang terjadi saat
keanggotaan berubah. Modulo mengocok ulang hampir semuanya: dari 3 node ke 4 memindahkan sekitar 3/4
key. Consistent hashing memindahkan sekitar 1/N — terukur 15–35% untuk transisi itu, dengan sebuah
tes yang memastikannya tetap di rentang tersebut. Setiap key yang berpindah adalah satu actor yang
harus dinonaktifkan di sini dan diaktifkan di sana, jadi selisihnya adalah selisih antara sebuah
rebalance dan sebuah gangguan layanan.

**Kenapa 128 virtual node?** Dengan satu posisi per member, pembagiannya sangat timpang — di mana
tiga titik acak itu kebetulan mendarat menentukan segalanya. Replika meratakannya. Pada 128, bagian
terburuk di cluster 3 node berada dalam beberapa persen dari rata, dan tesnya memastikan deviasinya
di bawah 15%.

**Kenapa hash-nya seperti itu.** FNV-1a atas UTF-8, lalu finalizer MurmurHash3. Dua sifat penting:

- *Tidak bergantung proses.* `string.GetHashCode()` diacak per proses, sehingga dua node akan
  membangun ring berbeda dari daftar member yang sama dan berselisih tentang kepemilikan. Bug itu
  hanya muncul di cluster sungguhan. Hash-nya dipatok di sebuah tes terhadap vektor yang diketahui.
- *Ber-avalanche baik.* FNV-1a mentah menggerombol buruk pada string pendek yang berbagi awalan — dan
  itu persis wujud posisi ring (`node-1#0`, `node-1#1`, …). Terukur, satu node mengambil 48% keyspace.
  Finalizer-nya memperbaiki itu.

## Keanggotaan

Protokolnya kecil:

1. Yang bergabung mengirim `Join` ke setiap seed.
2. Seed menjawab `JoinAck` berisi seluruh tabel member-nya.
3. Setelah itu setiap node secara berkala mengirim seluruh tabelnya ke setiap peer yang dikenalnya.

Itu konvergen, dan biayanya O(member²) denyut per interval — tidak berarti pada puluhan node, dan
membutuhkan batas fanout di atas itu. Ini ada di roadmap, dan ini adalah plafon nyata hari ini.

### Status

| Status | Di ring? | Arti |
| --- | --- | --- |
| `Joining` | tidak | Terlihat, handshake belum selesai |
| `Up` | ya | Sehat |
| `Unreachable` | **ya** | Melewatkan beberapa heartbeat |
| `Down` | tidak | Sudah dilepas; key-nya dibagikan ulang |
| `Leaving` | tidak | Sedang berhenti dengan anggun |

**Unreachable tetap di ring.** Penyebab umum heartbeat yang terlewat adalah jeda GC atau gangguan
jaringan sesaat, dan memindahkan key sebuah node berongkos satu gelombang deaktivasi dan aktivasi
ulang. Menunggu lebih murah daripada salah menduga.

```csharp
options.Cluster.HeartbeatInterval = TimeSpan.FromSeconds(2);
options.Cluster.UnreachableAfter  = TimeSpan.FromSeconds(10);   // dicurigai, masih dirutekan
options.Cluster.DownAfter         = TimeSpan.FromSeconds(30);   // dikeluarkan dari ring
```

`Validate()` menolak `DownAfter <= UnreachableAfter` (jeda singkat akan mengeluarkan node sehat) dan
`HeartbeatInterval >= UnreachableAfter` (sebuah node akan dinyatakan tidak terjangkau sebelum denyut
berikutnya jatuh tempo).

### Nomor inkarnasi

Entri setiap node membawa penghitung monoton. Pandangan sebuah node tentang dirinya sendiri selalu
menang: bila seorang peer meng-gossip bahwa node ini tidak terjangkau, node itu menaikkan
inkarnasinya dan menyanggah klaim itu ke mana pun ia menyebar.

Kabar dari pihak ketiga hanya menang dengan inkarnasi yang benar-benar lebih baru — selain itu kontak
langsung yang berlaku. Ini satu-satunya bagian SWIM yang layak diambil tanpa sisanya.

## Rebalancing

Saat keanggotaan berubah, actor yang key-nya tidak lagi menjadi milik node ini dinonaktifkan:

```csharp
options.Cluster.RebalanceOnMembershipChange = true;   // bawaan
```

Itulah separuh "elastis" dari penskalaan elastis. Deaktivasi menuliskan state, dan pesan berikutnya
mengaktifkan actor itu di pemilik barunya dari store — sehingga scale-out memigrasikan kira-kira 1/N
actor dan sisanya tidak bergerak.

**Ini membutuhkan store yang bisa dibaca kedua node.** Dengan store memori bawaan — atau store file
dan SQLite, yang bersifat per-proses — actor yang berpindah tidak menemukan apa pun. Pakai PostgreSQL,
SQL Server, MySQL, atau Redis; lihat [Persistensi](05-persistensi.md).

**State yang hanya di memori tidak selamat dari rebalance.** Memigrasikan state hidup berarti protokol
serah-terima terdistribusi; store sudah menyelesaikan masalahnya.

## Mengirim antar node

Tidak ada yang berubah di kode Anda:

```csharp
await system.TellAsync(ActorId.For<BankAccountActor>("alice"), new Deposit(100m));
```

Ring yang memutuskan. Lokal: satu penulisan channel. Remote: serialisasi, lalu serahkan ke transport.
Ask bekerja dengan cara yang sama — balasannya kembali lewat koneksi milik node yang menjawab dan
dicocokkan berdasarkan correlation id, bukan berdasarkan socket mana permintaannya tiba.

Pesan remote yang masuk selalu dikirimkan secara lokal, bahkan bila ring sudah memindahkan key itu.
Pengirimnya merutekan dengan pandangan yang ia punya, dan memantulkannya lebih jauh berisiko
menciptakan lingkaran antara dua node yang sedang berselisih selama rebalance.

## Transport

Satu listener TCP, satu koneksi keluar persisten per peer.

Koneksi berumur panjang secara sengaja. Satu koneksi per pesan berongkos handshake setiap kali, dan
di Windows menghabiskan rentang port efemeral saat beban tinggi — mode kegagalannya adalah node yang
bekerja saat demo dan mati saat benchmark. Penulisan diserialkan per koneksi, karena dua thread yang
menulis ke satu socket akan menyelipkan byte mereka menjadi frame yang tidak dikirim keduanya.

Penyambungan ulang memakai backoff eksponensial berbatas: node yang mati sejam tidak boleh dihubungi
ribuan kali per detik, dan node yang mati 200 ms tidak boleh menunggu semenit.

## Menerapkan di produksi

- **`NodeId` harus stabil dan unik.** Itulah yang di-hash ring, jadi node yang kembali dengan id
  berbeda mengambil irisan keyspace yang berbeda. Di Kubernetes, pakai nama pod dari StatefulSet,
  bukan nama acak.
- **`Host` dan `Port` harus terjangkau oleh peer**, bukan sekadar ter-bind lokal. Peer menghubungi
  alamat yang diiklankan sebuah node.
- **Seed masih berupa string statis.** Penemuan lewat DNS atau API Kubernetes ada di roadmap.
- **Tidak ada TLS dan tidak ada autentikasi antar node.** Jalankan cluster di jaringan tepercaya.
  Allow-list tipe membatasi apa yang bisa dibuat peer, tapi itu bukan pengganti jaringan tertutup.

## Menerapkan lintas mesin

`Host` merangkap dua tugas: alamat yang di-bind listener **dan** alamat yang diberitahukan ke peer
untuk dihubungi. Itu punya satu konsekuensi yang perlu diketahui sebelum menerapkannya.

### Mesin atau VM terpisah

Setel `Host` ke alamat yang benar-benar dimiliki mesin itu dan bisa dirutekan peer:

```bash
# di mesin 10.0.1.5
actornet run --node-id a --host 10.0.1.5 --port 9000 --cluster

# di mesin 10.0.1.6
actornet run --node-id b --host 10.0.1.6 --port 9000 --seed 10.0.1.5:9000
```

Sudah diuji pada antarmuka jaringan sungguhan, bukan loopback: dua node yang terikat ke alamat LAN
konvergen dan masing-masing melihat yang lain `Up`.

### Docker atau Kubernetes

Pakai nama yang bisa diselesaikan container lain:

```yaml
services:
  node-a:
    command: run --node-id a --host node-a --port 9000 --cluster
  node-b:
    command: run --node-id b --host node-b --port 9000 --seed node-a:9000
```

`Host` yang tidak bisa di-parse sebagai IP membuat listener bind ke semua antarmuka sambil tetap
mengiklankan namanya, dan itu persis yang dibutuhkan container. Sudah diuji dengan hostname di satu
mesin; belum diuji lintas container sungguhan.

### `--host 0.0.0.0` tidak bekerja

Ia bind dengan benar lalu mengiklankan `0.0.0.0`, yang tidak bisa dihubungi peer. Hasil yang teramati:
sebuah node ditemukan sekali lalu ditandai `Unreachable`, dengan percobaan koneksi gagal berulang.

Saat ini tidak ada cara mem-bind satu alamat dan mengiklankan alamat lain, yang juga berarti container
yang port-nya **dipublikasikan dengan nomor berbeda** tidak bisa mengiklankan port publikasinya.
Keduanya butuh `AdvertisedHost` / `AdvertisedPort` terpisah, yang ada di [roadmap](../../Plan.md).

Sampai saat itu: bind-lah alamat yang Anda ingin dipakai peer.

## Batas yang diketahui

- **Split brain belum tertangani.** Dua belahan dari sebuah partisi masing-masing meyakini memiliki
  seluruh ring, yang berarti dua aktivasi untuk actor yang sama.
- **Keanggotaan bersifat kuadratik** terhadap jumlah member per ronde heartbeat.
- **Deteksi kegagalan berupa tenggat tetap**, bukan phi-accrual. Ia akan menyebut jeda GC panjang
  sebagai kegagalan; `UnreachableAfter` yang mempertahankan node semacam itu di ring adalah
  mitigasinya.
- **`PreferenceList` ada dan tidak dipakai apa pun.** Penempatan replika belum diimplementasikan.

Keempatnya ada di [roadmap](../../Plan.md).

## Selanjutnya

- [Persistensi](05-persistensi.md) — kenapa store bersama adalah prasyaratnya
- [Perkakas](09-perkakas.md) — menyaksikan sebuah cluster konvergen
