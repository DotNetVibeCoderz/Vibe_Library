# Klien

*Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.*
*[English](../en/08-clients.md) · [Indeks dokumentasi](README.md)*

Empat SDK — C#, Node.js, Python, Go — semuanya berbicara dengan protokol asli node. Tidak ada gateway
HTTP yang harus di-deploy atau dijaga tetap sinkron dengan runtime.

## Apa itu klien, dan apa yang bukan

Sebuah klien **bukan** anggota cluster. Ia terhubung ke satu node, dan node itu meneruskan ke node
mana pun yang memiliki actor tujuan, jadi node mana pun adalah titik masuk yang sah.

Yang tidak didapat klien adalah pandangan keanggotaan miliknya sendiri. Ia tidak bisa memberi tahu di
mana sebuah actor berada, dan ia tidak tahu node mana yang memiliki sebuah kunci — ia mengirim ke
node mana pun yang sedang tersambung dan membiarkan ring mengurus sisanya.

Bila diberi lebih dari satu endpoint, sebuah klien menyambung ulang ke node lain ketika node yang
sedang dipakainya menghilang. Keempatnya melakukannya dengan cara yang sama, dan masing-masing
diperiksa di CI terhadap alamat mati yang ditaruh di depan alamat hidup.

Karena klien tidak punya alamat yang bisa dihubungi cluster, **node menjawab lewat koneksi yang
dibuka klien**. Itulah sebabnya setiap klien memelihara satu socket berumur panjang dan terus
membacanya bahkan saat ia hanya mengirim: balasan sebuah `ask` tidak punya tempat lain untuk tiba.

## Pengalamatan

Actor dialamatkan dengan string: `"BankAccountActor/alice"` — nama tipe, sebuah `/`, lalu key-nya.

Pesan dialamatkan dengan **alias**, bukan nama tipe .NET:

```csharp
[ActorMessage(Alias = "bank.deposit")]
public sealed record Deposit(decimal Amount, string Reference = "");
```

Node menyelesaikan alias yang masuk lewat allow-list eksplisit. Alias yang tidak terdaftar ditolak
dengan `UnknownMessageTypeException`.

Penolakan itulah sifat keamanannya: transport yang menyelesaikan nama tipe apa pun yang datang
membiarkan peer memilih tipe mana yang dikonstruksi proses ini, dan itulah fondasi rantai gadget
deserialisasi. Itu juga yang memungkinkan klien lintas bahasa — `bank.deposit` berarti hal yang sama
di Go dan di C#, dan tidak ada pihak yang perlu tahu nama tipe pihak lain.

Nama field payload adalah nama properti .NET (`Amount`, `Reference`), dicocokkan tanpa membedakan
huruf besar-kecil.

## Routing di dalam cluster

Node mana pun adalah titik masuk yang sah. Frame yang datang dari sesuatu yang bukan anggota cluster
— yaitu klien — untuk kunci yang bukan miliknya akan **diteruskan sekali** ke node pemiliknya, dan
balasannya dibawa kembali lewat node yang ditanya klien tadi. Jadi setiap klien benar di dalam
cluster, tahu ring atau tidak.

Penerusan itu sengaja tidak simetris. Frame dari *anggota* selalu ditangani di tempat ia tiba, karena
peer sudah merutekan dengan pandangannya sendiri, dan memantulkan pesannya justru berisiko
menciptakan loop di antara dua node yang berbeda pendapat saat rebalance. Hanya pengirim yang tidak
ada di tabel anggota yang diperlakukan sebagai belum dirutekan.

Yang dibayar adalah satu hop. Klien C# bisa menghindarinya:

```csharp
var client = new ActorNetClient(["10.0.0.1:9000", "10.0.0.2:9000"]) { ClusterAware = true };
```

Ia meminta tabel anggota kepada sebuah node, membangun ring yang sama dengan yang dipakai cluster —
hash yang sama, jumlah virtual node yang sama, sehingga hasilnya pun sama — lalu membuka satu koneksi
per node yang benar-benar dihubunginya. Pandangannya disegarkan setiap `RoutesRefreshAfter` (30 detik
secara bawaan) dan setiap kali koneksi ke node yang disebutnya gagal.

Routing-nya menurun kualitasnya, bukan gagal. Klien yang tidak bisa memperoleh pandangan, atau yang
pandangannya menyebut node yang tidak menjawab, akan mengirim ke node yang sudah terhubung dengannya
dan diteruskan dari sana.

Satu kasus yang memang memunculkan error adalah ask yang jawabannya ada di koneksi yang mati — node
lawan bicaranya pergi saat pertanyaan masih melayang. Itu gagal **segera**, bukan menunggu habisnya
timeout, dan klien membuang koneksi itu beserta pandangannya, sehingga percobaan berikutnya
dirutekan ulang. Kontraknya sama dengan klien tanpa routing sama sekali ketika sebuah endpoint
menghilang.

**Klien Node.js, Python, dan Go tidak merutekan.** Mereka tetap benar tanpa itu, dan membayar
hop-nya.

Satu hal yang perlu diketahui tentang hop itu: node yang meneruskan sebuah ask menyimpan korelasinya
di memori sampai jawabannya tiba. Bila node itu restart saat pertanyaan sedang melayang, klien
melihat timeout, bukan jawaban dari tempat lain.

## Protokol wire

Sebuah frame adalah empat byte panjang big-endian, lalu sebanyak itu byte JSON UTF-8. Nama field-nya
pendek karena ada di setiap hop:

| Field | Arti |
| --- | --- |
| `k` | Jenis: 1 pesan, 2 permintaan ask, 3 balasan ask, 4 kegagalan ask |
| `t` | Actor tujuan, `"Tipe/Key"` |
| `s` | Actor pengirim, bila ada |
| `a` | Alias pesan |
| `p` | Payload, sebagai JSON |
| `c` | Correlation id, untuk sebuah ask |
| `r` | Node tujuan balasan |
| `f` | Node atau klien yang mengirim frame ini |
| `e` | Teks galat, pada kegagalan ask |

Frame di atas 32 MiB ditolak di kedua sisi, sehingga panjang yang keliru tidak bisa membuat salah satu
ujung mengalokasi secara liar.

## C#

```csharp
await using var client = new ActorNetClient("127.0.0.1", 9000, clientId: "reporting-service");
client.RegisterMessagesFromAssembly(typeof(Deposit).Assembly);

await client.TellAsync(ActorId.Parse("BankAccountActor/alice"), new Deposit(500m));
var statement = await client.AskAsync<Statement>(ActorId.Parse("BankAccountActor/alice"), new GetStatement());
```

Beri lebih dari satu node, dan ia selamat dari matinya node yang ia hubungi:

```csharp
await using var client = new ActorNetClient(["10.0.1.5:9000", "10.0.1.6:9000", "10.0.1.7:9000"]);
```

Node mana yang dihubungi klien tidak penting — node yang tidak memiliki kunci tujuannya akan
meneruskan lewat ring — jadi setiap node adalah pintu masuk yang sama benarnya, dan klien yang
terikat hanya pada satu di antaranya adalah hal yang aneh bagi klien sebuah cluster.

Endpoint dicoba bergiliran mulai dari yang terakhir berhasil, sehingga penyambungan ulang biasa
kembali ke tempat semula dan hanya node yang benar-benar hilang yang membuatnya pindah.
`ConnectedTo` menyebutkan mana yang sedang dipakai. Berputar pada setiap penyambungan justru
keriuhan, bukan penyeimbangan: tidak ada yang didapat dari pindah, dan ada satu koneksi yang harus
dibangun ulang karenanya.

**Apa pun yang sedang di jalan saat koneksi putus tetap gagal**, berupa `ActorNetException` yang
menyebut endpoint-nya dan membawa error soketnya sebagai penyebab. Pengirimannya at-most-once, dan
mengirim ulang permintaan yang balasannya hilang diam-diam mengubahnya jadi at-least-once. Pemanggil
tahu apakah operasinya aman diulang; klien tidak — jadi panggilan setelah kegagalan itulah yang
mencari node lain.

## Node.js

```javascript
const { ActorNetClient } = require('./actornet');

const client = new ActorNetClient({ endpoints: ['10.0.1.5:9000', '10.0.1.6:9000'], clientId: 'web-1' });
await client.connect();

await client.tell('BankAccountActor/alice', 'bank.deposit', { Amount: 500, Reference: 'opening' });

const { alias, payload } = await client.ask('BankAccountActor/alice', 'bank.get-statement', { MaxEntries: 5 });
console.log(alias, payload.Balance);

client.close();
```

## Python

```python
from actornet import ActorNetClient

async with ActorNetClient(endpoints=["10.0.1.5:9000", "10.0.1.6:9000"], client_id="ingest-1") as client:
    await client.tell("DeviceActor/sensor-001", "iot.reading",
                      {"DeviceId": "sensor-001", "Celsius": 21.5, "At": now})

    reply = await client.ask("DeviceActor/sensor-001", "iot.get-status", {})
    print(reply.payload["Average"])
```

## Go

```go
client := actornet.NewCluster([]string{"10.0.1.5:9000", "10.0.1.6:9000"}, actornet.WithClientID("worker-1"))
defer client.Close()

if err := client.Tell(ctx, "InventoryActor/widget", "order.restock",
    map[string]any{"Sku": "widget", "Quantity": 10}); err != nil {
    return err
}

reply, err := client.Ask(ctx, "InventoryActor/widget", "order.get-stock", map[string]any{})
if err != nil {
    return err
}

var stock stockLevel
if err := reply.Into(&stock); err != nil {
    return err
}
```

## Yang dilakukan semua klien dengan cara sama

**Satu koneksi persisten.** Menghubungi per pesan berongkos handshake setiap kali, menghabiskan
rentang port efemeral saat beban tinggi, dan membuat `ask` mustahil karena balasannya tidak punya
tempat tiba.

**Penulisan diserialkan.** Beberapa pemanggil bisa mengirim bersamaan; penulisan yang saling
menyelip akan menghasilkan frame yang tidak dikirim siapa pun.

**Pembacaan berawalan panjang.** TCP adalah aliran byte, jadi satu potongan bukan satu frame. Dua
balasan bisa tiba menyatu dan satu balasan besar tiba terpotong-potong. Setiap klien menampung sampai
satu frame utuh tersedia.

**Pencocokan correlation id.** Balasan tiba saling menyelip di satu socket. Klien yang mencocokkannya
berdasarkan urutan kedatangan akan menyerahkan jawaban milik orang lain kepada pemanggilnya — ada tes
khusus untuk ini, dengan 40 ask berjalan bersamaan.

## Status verifikasi

| Klien | Status |
| --- | --- |
| C# | Terverifikasi di test suite — tell, ask, 40 ask bersamaan, kegagalan, timeout, penolakan allow-list |
| Node.js | Terverifikasi terhadap node yang berjalan; dijalankan di CI |
| Python | Terverifikasi terhadap node yang berjalan; dijalankan di CI |
| Go | **Belum dijalankan** di mesin tempat ia ditulis — tidak ada toolchain Go di sana. CI mengompilasi, meng-vet, dan menjalankannya terhadap node sungguhan |

Berterus terang tentang baris terakhir itu lebih penting daripada membuat barisnya kosong.

## Menjalankan contohnya

```bash
dotnet run --project src/ActorNet.Cli -- run --port 9000
```

Lalu:

```bash
node clients/nodejs/examples/banking.js
python clients/python/examples/telemetry.py
cd clients/go && go run ./examples/ordering
```

Masing-masing menghormati `ACTORNET_HOST` / `ACTORNET_PORT` (Go memakai `ACTORNET_ADDR`).

## Belum dibangun

- Perutean sadar-cluster, supaya klien mengirim langsung ke node pemilik kunci

Lihat [roadmap](../../Plan.md).

## Selanjutnya

- [Arsitektur](02-arsitektur.md) — di mana protokolnya berada
- [Clustering](06-clustering.md) — kenapa node mana pun adalah titik masuk yang sah
