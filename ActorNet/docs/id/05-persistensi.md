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

Tiga sambungan, ditukar lewat options. Setiap provider lulus suite konformans yang sama, jadi mereka
bisa saling menggantikan.

| Provider | Paket | Selamat dari restart | Dibagi antar node | Diverifikasi |
| --- | --- | --- | --- | --- |
| Memori (bawaan) | bawaan | tidak | tidak | di suite |
| File | bawaan | ya | tidak | di suite |
| SQLite | `ActorNet.Persistence.Sqlite` | ya | tidak | di suite, setiap kali dijalankan |
| PostgreSQL | `ActorNet.Persistence.PostgreSql` | ya | **ya** | di CI, terhadap server sungguhan |
| SQL Server | `ActorNet.Persistence.SqlServer` | ya | **ya** | di CI, terhadap server sungguhan |
| MySQL / MariaDB | `ActorNet.Persistence.MySql` | ya | **ya** | di CI, terhadap server sungguhan |
| Redis | `ActorNet.Persistence.Redis` | bisa dikonfigurasi | **ya** | di CI, terhadap server sungguhan |

```csharp
options.UsePostgreSql("Host=db;Database=actornet;Username=app;Password=…", system.Serializer.Types);
options.UseSqlServer("Server=db;Database=actornet;…", types);
options.UseMySql("Server=db;Database=actornet;…", types);
options.UseSqlite("Data Source=./data/actornet.db", types);
options.UseRedis(ConnectionMultiplexer.Connect("localhost:6379"), types);
```

Atau pasang ketiga store secara terpisah, yang Anda inginkan saat journal dan state sebaiknya berada
di tempat berbeda:

```csharp
options.StateStore    = PostgreSqlPersistence.StateStore(connectionString);
options.EventJournal  = PostgreSqlPersistence.EventJournal(connectionString, types);
options.SnapshotStore = RedisPersistence.SnapshotStore(redis);
```

### Memilih salah satu

**Memori** menjadi bawaan karena ia membuat framework langsung bekerja dan membuat tes cepat. Ia
selamat dari deaktivasi, bukan dari restart proses, dan tidak ada yang dibagi. Hanya untuk
pengembangan dan tes.

**File** membuat "matikan, jalankan lagi, saldonya masih ada" bisa diperagakan alih-alih sekadar
diklaim. Hanya satu node.

**SQLite** adalah janji yang sama dengan basis data sungguhan di bawahnya — SQL sungguhan, constraint
sungguhan, konkurensi sungguhan. Tetap satu node: sebuah file tidak dibagi. Ia juga yang dijalani
suite konformans pada setiap kali tes dijalankan, sehingga ia provider yang paling terlatih di sini.

**PostgreSQL, SQL Server, MySQL** adalah jawaban untuk cluster. Rebalancing bekerja dengan
menonaktifkan actor di satu node dan mengaktifkannya di node lain, dan itu hanya memulihkan state
bila kedua node bisa membaca store yang sama.

**Redis** cepat dan bisa dibagi, dengan satu catatan yang perlu dinyatakan terus terang: persistensi
Redis bisa dikonfigurasi dan sering dimatikan, dan dengan snapshot RDB bawaan sebuah crash kehilangan
tulisan beberapa detik terakhir. Cocok untuk proyeksi atau actor berbentuk cache; tidak cocok untuk
buku besar.

### Skemanya

Tiga tabel, dibuat saat pertama dipakai:

```
actornet_state      actor_key, state_version, payload, updated_at
actornet_events     persistence_id, seq_no, type_alias, payload, created_at   PK (persistence_id, seq_no)
actornet_snapshots  persistence_id, seq_no, payload, created_at
```

Matikan pembuatan otomatisnya dan serahkan DDL-nya ke apa pun yang mengelola skema Anda:

```csharp
var options = new RelationalStoreOptions { AutoCreateSchema = false, TablePrefix = "app_" };
foreach (var statement in RelationalSchema.StatementsFor(PostgreSqlDialect.Instance, options))
    Console.WriteLine(statement);
```

Dua detail yang bukan pilihan sembarangan:

**Key actor dibatasi 400 karakter.** Kolomnya adalah primary key di empat basis data sekaligus. SQL
Server membatasi key indeks pada 900 byte dan `NVARCHAR`-nya dua byte per karakter, yang menaruh
plafonnya di 450; InnoDB milik MySQL membatasinya pada 3072 byte, yang dengan `utf8mb4` berarti 768.
400 melewati keduanya, dan alamat actor sepanjang itu sudah merupakan masalah desain tersendiri.

**Primary key journal adalah `(persistence_id, seq_no)`, dan itulah yang membuat append bersamaan
aman** — bukan lock dan bukan tingkat isolasi. Sebuah append membaca ujung stream, memeriksanya, lalu
menyisipkan di ujung+1; bila aktivasi lain sampai lebih dulu, basis datanya menolak sisipan itu dan
yang kalah diberi tahu.

**Timestamp berupa milidetik Unix dalam `BIGINT`.** Tipe tanggal dan waktu adalah tempat keempat basis
data paling berbeda, dan tidak satu pun store membandingkan atau merentang pada kolom itu — ia hanya
informatif.

### Menulis provider

```csharp
public interface ISqlDialect
{
    string Name { get; }
    DbConnection CreateConnection(string connectionString);
    IReadOnlyList<string> SchemaStatements(RelationalStoreOptions options);
    bool IsUniqueViolation(DbException exception);
}
```

Empat anggota, karena setiap perintah yang dikeluarkan store adalah SQL biasa yang diterima keempat
basis data tanpa perubahan. Dialek yang harus menulis ulang DML-nya adalah tanda DML itu sudah melenceng
ke sesuatu yang tidak portabel.

`IsUniqueViolation` bersifat menopang, bukan kosmetik: kedua store mengandalkan primary key agar
penulisan bersamaan gagal alih-alih saling menyelip, jadi salah melaporkan bentrokan key sebagai galat
biasa mengubah konflik yang terdeteksi menjadi tulisan yang hilang.

Untuk store non-relasional, implementasikan `IStateStore`, `IEventJournal`, dan `ISnapshotStore`
langsung — provider Redis adalah contoh jadinya — lalu jalankan `PersistenceProviderConformance`
terhadapnya.

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
