# Designer

Aplikasi WPF untuk menyusun layar LVGL secara visual, dengan preview yang merupakan LVGL itu sendiri
— bukan tiruannya dalam WPF.

```bash
dotnet run --project tools/LVGL.Designer
```

Khusus Windows — itulah konsekuensi WPF. Tidak ada bagian lain repositori ini yang bergantung
padanya, dan layout yang dihasilkannya berjalan di mana saja.

## Tata letak jendela

| Area | Fungsi |
|------|--------|
| Toolbox (kiri) | Klik ganda sebuah jenis widget untuk menambahkannya |
| Preview (tengah) | Render LVGL langsung; widget terpilih diberi garis bantu |
| Outline (kanan atas) | Pohon widget; pilih, hapus, gandakan |
| Properties (kanan bawah) | Menyunting widget terpilih |
| Toolbar | New, Open, Save, Export C#, dan ukuran layar target |

## Mengapa preview-nya dapat dipercaya

Designer menjalankan instansi LVGL sungguhan melalui `HeadlessBackend` dan menyalin frame-nya ke
`WriteableBitmap`. Metrik font, radius sudut, gradien, cara LVGL membulatkan sebuah layout — semuanya
adalah kode yang sama dengan yang akan berjalan di perangkat.

LVGL bersifat single-thread, jadi ia berjalan di thread dispatcher WPF, digerakkan oleh
`DispatcherTimer` sekitar 30 Hz. Suntingan properti ditunda 150 ms lalu membangun ulang pohon widget
dari awal; pada skala designer, cara itu lebih murah sekaligus lebih jujur daripada melakukan diff.

Bila library native belum dibangun, area preview menjelaskan apa yang harus dilakukan. **Menyunting,
menyimpan, dan mengekspor C# tetap berfungsi** — hanya preview terenderlah yang membutuhkan LVGL.

## Format berkas

Layout disimpan sebagai `*.lvgl.json`: dokumen sederhana yang ramah diff.

```json
{
  "Version": 1,
  "Name": "Dashboard",
  "Width": 800,
  "Height": 480,
  "BackgroundColor": "#0F1720",
  "Children": [
    {
      "Type": "Label",
      "Name": "TitleLabel",
      "Text": "Halo",
      "Align": "TopMid",
      "Y": 28,
      "FontSize": 28
    }
  ]
}
```

Enum ditulis sebagai nama, sehingga penataan ulang di LVGL versi mendatang tidak dapat diam-diam
mengubah layout yang tersimpan. Properti opsional dihilangkan sepenuhnya bila tidak disetel — itulah
yang menjaga perbedaan antara "biarkan pada nilai bawaan LVGL" dan "nol secara eksplisit".

## Dua cara memakai layout

**Muat saat runtime**, dengan mengirim layout sebagai data:

```csharp
var document = UiJson.Load("Dashboard.lvgl.json");
var builder = new UiBuilder();
builder.Build(document, app.Screen);

var title = builder.Find<LvLabel>("TitleLabel");
var button = builder.Find<LvButton>("ActionButton");
button!.Clicked += (_, _) => title!.Text = "diklik";
```

**Atau ekspor C#** — *Export C#* pada toolbar menuliskan berkas seperti:

```csharp
public partial class Dashboard
{
    public const int DesignWidth = 800;
    public const int DesignHeight = 480;

    public LvObject Root { get; private set; } = null!;
    public LvLabel TitleLabel { get; private set; } = null!;

    public LvObject Build(LvObject? parent = null)
    {
        Root = parent ?? LvScreen.Active();
        Root.SetBackgroundColor(LvColor.FromRgb(0x0F1720u));

        var titleLabel = new LvLabel(Root, "Halo");
        titleLabel.Align(LvAlign.TopMid, 0, 28);
        titleLabel.SetFontSize(28);
        TitleLabel = titleLabel;

        OnBuilt();
        return Root;
    }

    partial void OnBuilt();
}
```

Kelasnya `partial` dan memanggil `OnBuilt()` di akhir, sehingga pemasangan event Anda tinggal di
belahan yang lain dan pembuatan ulang layout tidak pernah menimpanya:

```csharp
public partial class Dashboard
{
    partial void OnBuilt()
    {
        ActionButton.Clicked += (_, _) => TitleLabel.Text = "diklik";
    }
}
```

Kedua jalur melewati model `UiDocument` yang sama, jadi keduanya menghasilkan pohon widget yang
identik. Pilih pemuatan runtime bila layout perlu berubah tanpa build ulang; pilih C# hasil generate
bila Anda menginginkan nama yang diperiksa saat kompilasi dan tanpa parsing JSON pada perangkat
terbatas.

## Aturan penamaan

Nama widget menjadi properti C#, jadi ia harus berupa pengenal yang sah dan unik dalam dokumen.
*Export C#* menolak berjalan pada dokumen yang melanggar aturan ini dan menampilkan seluruh
masalahnya; pemeriksaan yang sama tersedia secara programatis:

```csharp
foreach (var problem in document.Validate()) Console.WriteLine(problem);
```

Validasi juga menangkap rentang terbalik dan warna yang salah format.

## Keterbatasan saat ini

- Pemilihan dan penyuntingan dilakukan lewat outline dan panel properti; preview belum mendukung
  manipulasi langsung (belum ada seret untuk memindah atau mengubah ukuran).
- Penyarangan hanya didukung untuk kontainer `Panel`.
- Handler event ditulis dalam kode, bukan ditetapkan di designer — ini disengaja, agar kode hasil
  generate tidak pernah memuat logika aplikasi.
