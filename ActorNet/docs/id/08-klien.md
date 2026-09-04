# Klien

*Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.*
*[English](../en/08-clients.md) · [Indeks dokumentasi](README.md)*

Empat SDK — C#, Node.js, Python, Go — semuanya berbicara dengan protokol asli node. Tidak ada gateway
HTTP yang harus di-deploy atau dijaga tetap sinkron dengan runtime.

## Apa itu klien, dan apa yang bukan

Sebuah klien **bukan** anggota cluster. Ia terhubung ke satu node, dan node itu meneruskan ke node
mana pun yang memiliki actor tujuan, jadi node mana pun adalah titik masuk yang sah.

Yang tidak didapat klien adalah pandangan keanggotaan miliknya sendiri. Ia tidak bisa memberi tahu di
mana sebuah actor berada, dan bila node yang ia sambungi mati, ia harus menyambung ulang ke tempat
lain alih-alih melakukan failover sendiri.

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

## Node.js

```javascript
const { ActorNetClient } = require('./actornet');

const client = new ActorNetClient({ host: '127.0.0.1', port: 9000, clientId: 'web-1' });
await client.connect();

await client.tell('BankAccountActor/alice', 'bank.deposit', { Amount: 500, Reference: 'opening' });

const { alias, payload } = await client.ask('BankAccountActor/alice', 'bank.get-statement', { MaxEntries: 5 });
console.log(alias, payload.Balance);

client.close();
```

## Python

```python
from actornet import ActorNetClient

async with ActorNetClient(host="127.0.0.1", port=9000, client_id="ingest-1") as client:
    await client.tell("DeviceActor/sensor-001", "iot.reading",
                      {"DeviceId": "sensor-001", "Celsius": 21.5, "At": now})

    reply = await client.ask("DeviceActor/sensor-001", "iot.get-status", {})
    print(reply.payload["Average"])
```

## Go

```go
client := actornet.New("127.0.0.1:9000", actornet.WithClientID("worker-1"))
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

- Sambung ulang dan failover ke node lain
- Perutean sadar-cluster di sisi klien

Lihat [roadmap](../../Plan.md).

## Selanjutnya

- [Arsitektur](02-arsitektur.md) — di mana protokolnya berada
- [Clustering](06-clustering.md) — kenapa node mana pun adalah titik masuk yang sah
