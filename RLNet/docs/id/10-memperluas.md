# Memperluas RLNet

[← Daftar isi](README.md) · [English](../en/10-extending.md)

Setiap sambungan di library ini adalah interface publik. Halaman ini membahas empat hal yang layak
diperluas.

## Simulasi baru

Turunkan dari `DiscreteEnvironmentBase` atau `ContinuousEnvironmentBase`. Kelas dasarnya memiliki
buffer observasi, generator ber-seed, penghitung langkah, dan batas waktu — sehingga tidak ada
environment yang bisa lupa melaporkan truncation, persis bug yang ingin dicegah oleh pemisahan
terminated/truncated.

```csharp
using RLNet.Environments;
using RLNet.Spaces;

public sealed class ThermostatEnvironment : DiscreteEnvironmentBase
{
    private double _suhu;
    private double _target;

    public ThermostatEnvironment() : base(
        // Label bersifat opsional, tapi konsol memakainya dan biayanya nol.
        new BoxSpace([-1f, -1f, 0f], [1f, 1f, 1f],
                     ["Selisih suhu", "Laju perubahan", "Status pemanas"]),
        new DiscreteSpace(3, ["Mati", "Rendah", "Tinggi"]),
        maxEpisodeSteps: 500)
    {
        Reset();
    }

    public override string Name => "Thermostat";

    protected override void OnReset()
    {
        // `Random` adalah generator ber-seed milik kelas dasar. Pakai untuk semua hal stokastik,
        // atau Reset(seed) tidak akan benar-benar mengulang episodenya.
        _suhu = Random.NextRange(15f, 25f);
        _target = 21.0;
    }

    protected override void WriteObservation(Span<float> destination)
    {
        destination[0] = (float)Math.Clamp((_suhu - _target) / 10.0, -1, 1);
        destination[1] = /* laju perubahan */ 0f;
        destination[2] = /* status pemanas */ 0f;
    }

    protected override StepResult OnStep(int action)
    {
        _suhu += action * 0.1 - 0.05;   // panas masuk, rugi ke lingkungan keluar

        float selisih = (float)Math.Abs(_suhu - _target);
        float reward = -selisih - action * 0.01f;   // kenyamanan, dikurangi energi

        bool terminated = _suhu < 0 || _suhu > 40;   // kegagalan sungguhan

        // Advance menangani penghitung, batas waktu, dan flag truncation.
        return Advance(reward, terminated);
    }
}
```

### Detail yang harus benar

**Skala observasi.** Jaga nilainya kira-kira di `[-1, 1]`. Learning rate default para agen
mengasumsikannya, dan observasi dalam ribuan akan butuh learning rate berbeda untuk masalah yang
selebihnya sama. `Trading` dan `SupplyChain` menormalkan justru karena ini.

**Besaran periodik masuk sebagai pasangan sinus-kosinus.** Sudut, hari dalam minggu, sebuah fase —
memberi nilai mentah memberi tahu jaringan bahwa kedua ujung siklusnya paling berjauhan, yang salah,
dan umumnya ia gagal belajar karenanya. `Pendulum`, `Reacher`, dan `SupplyChain` semuanya begitu.

**Terminated berarti kondisi akhir sungguhan.** Bukan "episodenya selesai" — batas langkah adalah
urusan `Advance`, dan melaporkannya sebagai terminasi mengajarkan agen bahwa dunia berakhir di batas
itu. Kalau environment Anda tidak punya kondisi gagal sama sekali, selalu berikan `false`, seperti
`Pendulum`.

**Ekspos apa pun yang dibutuhkan renderer** sebagai properti publik. `WorldView` melakukan downcast
ke tipe konkretnya dan membacanya.

### Mendaftarkannya

Tambahkan entri ke `Catalog.Entries` agar muncul di konsol dan di `Catalog.CreateDiscrete`:

```csharp
new("Thermostat", EnvironmentKind.Discrete, "Operasi",
    "Menjaga suhu ruangan tanpa menyalakan pemanas lebih keras dari yang diperlukan.")
{
    Create = () => new ThermostatEnvironment(),
    SupportedAlgorithms = [Algorithm.Dqn, Algorithm.Ppo, Algorithm.A2C, Algorithm.QLearning],
},
```

Lalu tambahkan `case` di `WorldView.Render` dan metode `DrawThermostat` kalau ingin digambar.

Pendaftaran tidak wajib — kelas biasa berfungsi baik dengan `Trainer` — tapi konsol hanya menampilkan
apa yang diketahui katalog.

## Replay buffer baru

```csharp
public interface IReplayBuffer
{
    int Capacity { get; }
    int Count { get; }
    int ObservationSize { get; }
    int ActionSize { get; }

    void Add(ReadOnlySpan<float> observation, ReadOnlySpan<float> action, float reward,
             ReadOnlySpan<float> nextObservation, bool terminated);
    void Sample(int batchSize, ReplayBatch batch, FastRandom random);
    void UpdatePriorities(ReadOnlySpan<int> indices, ReadOnlySpan<float> tdErrors);
    void Clear();
}
```

Cara termudah adalah menurunkan dari `UniformReplayBuffer`, yang sudah menangani penyimpanan
melingkar sebagai structure of arrays, lalu meng-override dua metode yang penting.
`PrioritizedReplayBuffer` adalah pola itu dalam sekitar 100 baris:

```csharp
public sealed class DemonstrationBuffer(int capacity, int obs, int act)
    : UniformReplayBuffer(capacity, obs, act)
{
    private readonly UniformReplayBuffer _demonstrations = new(10_000, obs, act);

    /// <summary>Menambah transisi dari ahli, yang tidak pernah tergusur pengalaman biasa.</summary>
    public void AddDemonstration(ReadOnlySpan<float> obs, ReadOnlySpan<float> act,
                                 float reward, ReadOnlySpan<float> next, bool terminated) =>
        _demonstrations.Add(obs, act, reward, next, terminated);

    public override void Sample(int batchSize, ReplayBatch batch, FastRandom random)
    {
        // Seperempat setiap batch dari demonstrasi, sisanya dari pengalaman agen sendiri.
        int ahli = Math.Min(batchSize / 4, _demonstrations.Count);
        if (ahli > 0) _demonstrations.Sample(ahli, batch, random);
        base.Sample(batchSize - ahli, batch, random);
    }
}
```

Lalu berikan — tidak ada agen yang perlu diubah:

```csharp
var agent = new DqnAgent(obs, act, buffer: new DemonstrationBuffer(100_000, 4, 1));
```

`OnAdded(int slot)` adalah kait untuk pembukuan per slot; ia dipanggil sebelum head bergerak.

## Compute backend baru

Implementasikan `IComputeBackend` — dua metode, forward dan backward sebuah layer dense:

```csharp
public interface IComputeBackend : IDisposable
{
    string Name { get; }
    bool IsAccelerated { get; }

    void DenseForward(ReadOnlySpan<float> weights, ReadOnlySpan<float> biases,
                      ReadOnlySpan<float> input, Span<float> output,
                      int batch, int inputSize, int outputSize, Activation activation);

    void DenseBackward(ReadOnlySpan<float> weights, ReadOnlySpan<float> input,
                       ReadOnlySpan<float> output, Span<float> gradOutput, Span<float> gradInput,
                       Span<float> weightGrad, Span<float> biasGrad,
                       int batch, int inputSize, int outputSize, Activation activation);
}
```

Tiga kontrak yang harus dipatuhi:

1. **Weight-nya row-major `[inputSize, outputSize]`.**
2. **`gradInput` boleh kosong** — itu berarti layer pertama sebuah jaringan, dan Anda boleh melewati
   perkalian itu sepenuhnya.
3. **`weightGrad` dan `biasGrad` terakumulasi**, bukan ditimpa. `ZeroGradients` membersihkannya di
   antara pembaruan.

Verifikasi terhadap backend CPU seperti yang dilakukan `GpuBackendTests`: input identik lewat
keduanya, kesepakatan sampai sekitar 1e-3 relatif. Backend yang salahnya halus menghasilkan agen
yang terlatih sedikit lebih buruk, dan tidak ada benchmark yang akan menangkapnya.

## Algoritma baru

Implementasikan `IDiscreteAgent` atau `IContinuousAgent`. Interface-nya kecil; pekerjaannya ada di
algoritmanya.

```csharp
public sealed class MyAgent : IDiscreteAgent
{
    public string Name => "MyAgent";
    public AgentMetrics Metrics { get; } = new();

    public int SelectAction(ReadOnlySpan<float> observation, bool deterministic = false) { ... }

    public void Observe(ReadOnlySpan<float> observation, int action, float reward,
                        ReadOnlySpan<float> nextObservation, bool terminated, bool truncated)
    {
        // Hanya `terminated` yang menolkan bootstrap. Jangan pernah `terminated || truncated`.
        float target = terminated ? reward : reward + gamma * ValueOf(nextObservation);
        ...
        Metrics.StepCount++;
    }

    public void OnEpisodeEnd() { }
    public void SetProgress(float progress) { /* gerakkan jadwal Anda */ }
}
```

Hal-hal yang mudah salah, yang semuanya dicontohkan agen-agen yang sudah ada:

- **Jangan menyimpan span observasi.** Ia tidak valid pada langkah berikutnya. `Observe` menerima
  salinan dari pemanggil, tapi kalau Anda menyanggahnya, salin lagi.
- **`deterministic` harus benar-benar mematikan eksplorasi.** Evaluasi bergantung padanya.
- **Isi `Metrics`.** Konsol membacanya, dan `float.NaN` berarti "tidak bermakna untuk algoritma ini"
  — begitulah cara sebuah grafik tahu untuk tidak menggambar sebuah trace.
- **Baca `SetProgress`.** Tanpanya jadwal Anda tidak pernah bergerak.

Baca `A2CAgent` lebih dulu — ia actor-critic terlengkap yang paling ringkas di sini, dan kerangka
yang sama menskala sampai PPO.

Untuk algoritma berbasis nilai, `MlpNetwork` memberi Anda segalanya kecuali loss-nya:

```csharp
_online = new MlpNetwork(obsSize, [128, 128], actionCount,
                         Activation.ReLU, Activation.Linear, batchSize, random);
_target = new MlpNetwork(obsSize, [128, 128], actionCount,
                         Activation.ReLU, Activation.Linear, batchSize, random);
_target.CopyFrom(_online);
```

## Encoder state baru

Untuk agen tabular, encoder memetakan observasi ke satu bilangan bulat:

```csharp
public delegate long StateKeyEncoder(ReadOnlySpan<float> observation);
```

```csharp
// Hanya dua dimensi yang penting, dibagi kotak dengan halus.
StateKeyEncoder hanyaTongkat = observation =>
    StateDiscretizer.Bucket(observation[2], -0.21f, 0.21f, 24) * 12 +
    StateDiscretizer.Bucket(observation[3], -3f, 3f, 12);

var agent = new QTableAgent(actionSpace, hanyaTongkat);
```

Kuncinya dikemas ke dalam `long` sebagai bilangan basis campuran alih-alih dibangun sebagai string:
kunci string mengalokasikan pada setiap langkah dan di-hash dengan menelusuri karakter, sementara
kunci bilangan bulat tidak melakukan keduanya dan bebas tabrakan secara konstruksi, bukan karena
berharap.

## Menguji perluasan Anda

Suite yang sudah ada adalah templatnya:

| Yang Anda tambahkan | Uji seperti |
|---|---|
| Simulasi | `EnvironmentTests` — determinisme ber-seed, batas observasi, eksklusivitas flag |
| Compute backend | `GpuBackendTests` — kesetaraan terhadap backend CPU |
| Algoritma | `LearningTests` — apakah ia benar-benar belajar, pada run pendek ber-seed |
| Apa pun yang punya gradien | `NeuralNetworkTests` — beda hingga |

`EnvironmentTests` digerakkan oleh `Catalog`, jadi environment yang terdaftar otomatis tercakup oleh
setiap uji properti — determinisme, batas, dan kontrak terminated/truncated — tanpa satu baris kode
tes baru.

## Selanjutnya

- [Arsitektur](02-arsitektur.md) — kontrak yang sedang Anda perluas
- [Algoritma](03-algoritma.md) — bagaimana keenamnya dibangun
