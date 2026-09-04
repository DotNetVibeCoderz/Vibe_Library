# Persistensi

*Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.*
*[English](../en/05-persistence.md) · [Indeks dokumentasi](README.md)*

## Dua model

| | `PersistentActor<TState>` | `EventSourcedActor<TState>` |
| --- | --- | --- |
| Menyimpan | State saat ini | Semua yang pernah terjadi |
| Menulis | Saat deaktivasi, atau saat Anda minta | Sekali per event |
| Pemulihan | Satu pembacaan | Snapshot, lalu putar ulang sisanya |
| Menjawab | "Berapa saldonya?" | "Kenapa saldonya segini?" |
| Ongkos | Satu baris per actor | Satu stream yang tumbuh per actor |

Pilih persistensi state bila nilai saat ini adalah kebenarannya. Pilih event sourcing bila riwayatnya
yang benar — buku besar, jejak audit, apa pun yang "bagaimana kita sampai di sini" adalah pertanyaan
nyata.

## Persistensi state

```csharp
public sealed class DeviceState
{
    public double Latest { get; set; }
    public long Readings { get; set; }
}

public sealed class DeviceActor : PersistentActor<DeviceState>
{
    protected override int SaveEvery => 100;   // checkpoint, selain penulisan saat keluar

    protected override async Task ReceiveAsync(object message, CancellationToken ct)
    {
        if (message is SensorReading reading)
        {
            State.Latest = reading.Celsius;
            State.Readings++;
        }
    }
}
```

`State` dimuat sebelum pesan pertama dan dituliskan kembali saat deaktivasi. `IsNew` memberi tahu
apakah sebelumnya sudah ada isinya.

**Menulis-saat-deaktivasi adalah bawaan karena ia mengubah N pembaruan menjadi satu penulisan
store.** Ia juga berarti proses yang dimatikan paksa kehilangan semua yang terjadi sejak penulisan
terakhir. Tiga cara mempersempitnya:

```csharp
protected override int SaveEvery => 100;         // setiap 100 pesan
await SaveStateAsync(ct);                        // sekarang juga
protected override string PersistenceKey => ...; // key khusus, bila alamatnya tidak tepat
```

Stop dari supervisi melewatkan penulisan sepenuhnya — lihat [Supervisi](04-supervisi.md).

## Event sourcing

```csharp
public sealed class BankAccountActor : EventSourcedActor<AccountState>
{
    protected override long SnapshotEvery => 200;

    // Satu-satunya tempat state berubah. Dijalankan lagi pada setiap pemulihan, jadi harus murni.
    protected override void Apply(object domainEvent)
    {
        switch (domainEvent)
        {
            case Deposited d: State.Balance += d.Amount; break;
            case Withdrawn w: State.Balance -= w.Amount; break;
        }
    }

    protected override async Task ReceiveAsync(object message, CancellationToken ct)
    {
        switch (message)
        {
            case Withdraw w when w.Amount > State.Balance:
                // Ditolak, jadi tidak ada yang ditulis. Penarikan yang tidak terjadi bukan riwayat.
                await Context.ReplyAsync(new Declined("Saldo tidak cukup.", State.Balance), ct);
                break;

            case Withdraw w:
                await PersistAsync(new Withdrawn(w.Amount, DateTimeOffset.UtcNow), ct);
                await Context.ReplyAsync(new Accepted(State.Balance), ct);
                break;
        }
    }
}
```

Tiga aturan membuat ini bekerja.

**Perintah divalidasi; event adalah fakta.** `Withdraw` boleh ditolak. `Withdrawn` tidak bisa — ia
sudah terjadi, dan `Apply` harus menerimanya tanpa syarat.

**`PersistAsync` menulis sebelum menerapkan.** Menerapkan lebih dulu akan membuat actor mengakui
perubahan yang kemudian ditolak journal, dan itulah satu-satunya kegagalan yang tidak boleh dimiliki
actor event-sourced.

**`Apply` harus murni.** Ia dijalankan lagi pada setiap pemulihan. Sebuah penagihan, email, atau
panggilan HTTP di dalam fold akan terjadi lagi setiap kali actor-nya aktif. Jaga efek samping dengan
`IsRecovering`, atau taruh di handler perintah tempatnya semestinya.

Melakukan persist *selama* pemulihan ditolak mentah-mentah dengan `ActorNetException`. Menambah ke
stream yang sedang Anda putar ulang menumbuhkannya satu event per aktivasi, dan masing-masing akan
diputar ulang lain kali.

## Snapshot

```csharp
protected override long SnapshotEvery => 200;
protected override bool TruncateOnSnapshot => false;   // bawaan
```

Pemulihan memuat snapshot terbaru dan hanya memutar ulang event setelahnya. Snapshot adalah
**optimasi, tidak pernah sumber kebenaran** — menghapus semua snapshot harus tetap membuat sistemnya
benar, hanya lebih lambat.

`TruncateOnSnapshot` mati secara bawaan karena jejak audit biasanya justru inti dari event sourcing,
dan snapshot bukan jejak audit.

## Store

Tiga sambungan, ditukar lewat options:

```csharp
options.StateStore    = new FileStateStore("./data/state");
options.EventJournal  = new FileEventJournal("./data/journal", types);
options.SnapshotStore = new FileSnapshotStore("./data/snapshots");
```

| Store | Selamat dari deaktivasi | Selamat dari restart | Dibagi antar node |
| --- | --- | --- | --- |
| `InMemory*` (bawaan) | ya | tidak | tidak |
| `File*` | ya | ya | tidak |
| Provider basis data | ya | ya | ya — **belum dibangun** |

Store di memori menjadi bawaan karena ia membuat framework langsung bekerja dan membuat tes cepat.
Ia menjadi pilihan yang salah begitu state-nya penting. Store file membuat "matikan, jalankan lagi,
saldonya masih ada" bisa diperagakan alih-alih sekadar diklaim — ia menulis ke file sementara lalu
memindahkannya, karena file JSON yang tertulis separuh tidak bisa dipulihkan.

**Keduanya tidak dibagi antar node.** Di cluster sungguhan, actor yang di-rebalance ke node lain harus
menemukan state-nya di sana, dan itu berarti store yang bisa dibaca kedua node. Provider PostgreSQL
adalah item teratas di [roadmap](../../Plan.md).

### Satu kehalusan pada store di memori

Keduanya menyalin dalam (deep copy) saat masuk dan keluar. Tanpa itu, store memegang referensi ke
objek yang terus diubah actor — sehingga snapshot yang diambil pada sequence 20 terbaca sebagai state
pada sequence 25, dan pemulihan menerapkan dua kali semua yang ada di antaranya. Bug itu pernah ada,
sebuah tes menangkapnya, dan penyalinan itulah yang didapat provider basis data secara cuma-cuma
lewat serialisasi.

## Kontrol konkurensi

Setiap penulisan membawa versi saat ia dibaca:

```csharp
_version = await _store.WriteAsync(key, State, _version, ct);
```

Ketidakcocokan melempar `StateConcurrencyException`. Runtime menjaga satu aktivasi per key per
cluster, jadi ini semestinya tidak terjadi dalam operasi normal — ia muncul pada jendela tumpang
tindih singkat saat sebuah actor berpindah node. Memunculkannya lebih baik daripada membiarkan yang
kalah menimpa yang menang diam-diam.

## Menulis provider

```csharp
public interface IStateStore
{
    Task<StoredState<T>?> ReadAsync<T>(string key, CancellationToken ct = default);
    Task<long> WriteAsync<T>(string key, T state, long expectedVersion = -1, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
}
```

Tiga metode. `IEventJournal` menambahkan append, baca-maju, sequence tertinggi, dan truncate;
sequence yang diharapkan itulah yang mencegah dua aktivasi menyelipkan event ke satu stream.

Dua hal yang harus benar di sebuah provider: **serialisasi saat menulis** (lihat kehalusan di atas),
dan **hormati versi yang diharapkan** — store yang mengabaikannya tidak bisa mendeteksi penulisan
split-brain.

## Selanjutnya

- [Clustering](06-clustering.md) — kenapa store bersama itu penting
- [Supervisi](04-supervisi.md) — kenapa actor yang gagal tidak menulis state-nya
