// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using CommunityToolkit.Mvvm.ComponentModel;

namespace AplNet.Gallery.Infrastructure;

/// <summary>
/// The interface's own words in English and Indonesian. Bound as <c>{Binding [Key], Source={x:Static Loc.Instance}}</c>;
/// switching language raises a change for the indexer, so every label updates in place.
/// </summary>
public sealed partial class Loc : ObservableObject
{
    public static Loc Instance { get; } = new();

    private static readonly Dictionary<string, (string En, string Id)> s_strings = new()
    {
        ["AppTitle"] = ("APL.Net Gallery", "Galeri APL.Net"),
        ["Overview"] = ("Overview", "Ikhtisar"),
        ["Run"] = ("Run benchmark", "Jalankan benchmark"),
        ["Running"] = ("Running…", "Berjalan…"),
        ["RunAll"] = ("Run every case", "Jalankan semua kasus"),
        ["RunAllHint"] = ("Times every case on this machine, one after another. Takes about a minute.", "Mengukur setiap kasus di mesin ini, satu per satu. Butuh sekitar satu menit."),
        ["Faster"] = ("faster than Parallel.For", "lebih cepat dari Parallel.For"),
        ["Slower"] = ("slower than Parallel.For", "lebih lambat dari Parallel.For"),
        ["NotRun"] = ("Not measured yet on this machine", "Belum diukur di mesin ini"),
        ["NotRunHint"] = ("Press Run benchmark: each variant is warmed up, then timed for about half a second; the bar shows the median run.", "Tekan Jalankan benchmark: setiap varian dipanaskan dulu, lalu diukur sekitar setengah detik; batang menunjukkan median."),
        ["Median"] = ("median", "median"),
        ["PerRun"] = ("per run", "per run"),
        ["Allocated"] = ("allocated", "dialokasikan"),
        ["Verified"] = ("Output matches the sequential loop", "Hasil sama dengan loop sekuensial"),
        ["NotVerified"] = ("Output differs from the sequential loop", "Hasil berbeda dari loop sekuensial"),
        ["Code"] = ("Code", "Kode"),
        ["Result"] = ("Result", "Hasil"),
        ["Lanes"] = ("Where the time went — one lane per thread", "Ke mana waktunya — satu lajur per thread"),
        ["LanesHint"] = ("Each bar is a thread doing work; gaps are threads waiting. The run ends when the last lane does.", "Setiap batang adalah thread yang bekerja; celah berarti thread menunggu. Run selesai saat lajur terakhir selesai."),
        ["Takeaway"] = ("What to look for", "Yang perlu diperhatikan"),
        ["Size"] = ("Problem size", "Ukuran masalah"),
        ["ThisMachine"] = ("This machine", "Mesin ini"),
        ["Threads"] = ("threads", "thread"),
        ["Credit"] = ("Made by Gravicode Studios, led by Kang Fadhil", "Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil"),
        ["Case"] = ("Case", "Kasus"),
        ["Speedup"] = ("vs Parallel.For", "vs Parallel.For"),
        ["SummaryTitle"] = ("Every case on this machine", "Semua kasus di mesin ini"),
        ["Hero"] = ("Parallel loops without the per-iteration tax.", "Loop paralel tanpa pajak per iterasi."),
        ["HeroSub"] = ("APL.Net replaces what Parallel.For spends on every iteration — a delegate call, a shared chunk counter, a captured ExecutionContext — with loops the JIT compiles for your exact body, then adds SIMD. Each case in this gallery runs both on your hardware, side by side.", "APL.Net mengganti apa yang dibayar Parallel.For di setiap iterasi — pemanggilan delegate, penghitung chunk bersama, ExecutionContext yang ditangkap — dengan loop yang di-compile JIT khusus untuk body Anda, lalu menambahkan SIMD. Setiap kasus di galeri ini menjalankan keduanya di perangkat Anda, berdampingan."),
        ["Layers"] = ("How a call runs", "Bagaimana sebuah panggilan berjalan"),
        ["Language"] = ("Bahasa Indonesia", "English"),
        ["Seq"] = ("Sequential", "Sekuensial"),
        ["Waiting"] = ("Waiting for the other variants…", "Menunggu varian lain…"),
        ["Gallery"] = ("GALLERY", "GALERI"),
        ["Legend"] = ("APL.Net   ·   TPL   ·   sequential", "APL.Net   ·   TPL   ·   sekuensial"),
        ["Preview"] = ("Run the benchmark to render the result.", "Jalankan benchmark untuk menampilkan hasil."),
        ["L1"] = ("Apl.For · Apl.ForEach", "Apl.For · Apl.ForEach"),
        ["L1d"] = ("The same call shape as Parallel.For. A lambda is wrapped in a struct so it rides the same fast path.", "Bentuk panggilan sama dengan Parallel.For. Lambda dibungkus struct agar melewati jalur cepat yang sama."),
        ["L2"] = ("IWorkBody structs", "Struct IWorkBody"),
        ["L2d"] = ("Generic over your struct, so the JIT compiles a loop for that exact type and inlines Invoke — no delegate call per element.", "Generik atas struct Anda, sehingga JIT meng-compile loop untuk tipe itu dan meng-inline Invoke — tanpa pemanggilan delegate per elemen."),
        ["L3"] = ("Partitioner + pooled job", "Partitioner + job dari pool"),
        ["L3d"] = ("Ranges decided once up front; one reusable work item queued without a Task or a captured ExecutionContext; the caller works too.", "Rentang ditentukan sekali di awal; satu work item yang dipakai ulang diantrikan tanpa Task maupun ExecutionContext; pemanggil ikut bekerja."),
        ["L4"] = ("SimdOps", "SimdOps"),
        ["L4d"] = ("Vector512, Vector256 or Vector128 — whichever the CPU accelerates — with a scalar tail, inside each worker's slice.", "Vector512, Vector256 atau Vector128 — mana pun yang dipercepat CPU — dengan ekor skalar, di dalam potongan tiap worker."),
        ["L5"] = ("UnsafeParallel", "UnsafeParallel"),
        ["L5d"] = ("Raw pointers and static function pointers for data that already lives in native memory. Opt-in, in its own namespace.", "Pointer mentah dan function pointer statis untuk data yang sudah berada di memori native. Opt-in, di namespace tersendiri."),
        ["ColCase"] = ("Case", "Kasus"),
        ["ColTpl"] = ("Parallel.For", "Parallel.For"),
        ["ColApl"] = ("Best APL.Net", "APL.Net terbaik"),
        ["ColRatio"] = ("Ratio", "Rasio"),
        ["Cores"] = ("One square per hardware thread — the lanes every loop here is split across.", "Satu kotak per thread perangkat keras — lajur tempat setiap loop di sini dibagi."),
    };

    [ObservableProperty]
    private bool _indonesian;

    partial void OnIndonesianChanged(bool value)
    {
        // Binding engines disagree on how an indexer announces a change; say it every way.
        OnPropertyChanged("Item");
        OnPropertyChanged("Item[]");
    }

    public string this[string key] => s_strings.TryGetValue(key, out var pair) ? (Indonesian ? pair.Id : pair.En) : key;

    public string Pick(string en, string id) => Indonesian ? id : en;
}
