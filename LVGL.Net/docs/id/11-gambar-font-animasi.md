# Gambar, font, dan animasi

## Gambar

`LvImage` menampilkan berkas, piksel yang dibuat saat runtime, atau simbol bawaan.

```csharp
// Berkas: PNG dan BMP didekode oleh library native dan di-cache.
var logo = new LvImage(app.Screen);
logo.SetFile("assets/logo.png");

// Piksel yang dihasilkan aplikasi: 0xAARRGGBB, alpha dihormati.
var pixels = new uint[64 * 64];
// ... isi ...
var source = LvImageSource.FromArgb(64, 64, pixels);
var preview = new LvImage(app.Screen, source);

// Glif simbol, digambar dengan warna teks.
var icon = new LvImage(app.Screen);
icon.SetSymbol(LvSymbols.Wifi);
icon.SetTextColor(LvColor.White);
```

| Properti | Arti |
|---|---|
| `Rotation` | Sepersepuluh derajat; 900 adalah seperempat putaran, di sekitar titik pivot (`SetPivot`) |
| `Scale` | 256 adalah 100 %, 512 adalah 200 % |
| `InnerAlign` | Bagaimana piksel diletakkan dalam widget yang ukurannya berbeda: `Center`, `Stretch`, `Tile`, `Contain`, `Cover`... |
| `Antialias` | Menghaluskan gambar yang diputar dan diskalakan, dengan sedikit biaya |
| `SourceWidth`, `SourceHeight` | Ukuran sumber setelah LVGL membacanya |

**Daur hidup.** Sumber piksel membungkus memori unmanaged yang langsung digambar oleh LVGL, jadi ia
mengikuti aturan `LvStyle`: dispose setelah setiap gambar yang menampilkannya dihapus. Dispose juga
mengeluarkannya dari cache gambar LVGL, sehingga alamatnya tidak akan pernah tertukar dengan gambar
lain di kemudian hari. Sumber berkas hanyalah sebuah jalur dan tidak perlu di-dispose.

**Jalur.** LVGL mengalamatkan berkas melalui huruf drive; build native memetakan `fopen` milik
library C ke drive `A:`. Setiap API LVGL.Net menambahkan awalan itu untuk Anda
(`LvFileSystem.ToLvglPath`) - berikan saja jalur biasa. Berkas yang tidak ada melempar
`FileNotFoundException` sebelum LVGL melihatnya.

## Font

Library native membawa Montserrat ukuran 12, 14, 16, 20, 24, 28, dan 36 px (`SetFontSize`). Untuk
ukuran lain, font merek, atau aksara yang tidak dicakup Montserrat - Arab, Thai, CJK - muat font
TrueType, yang dirasterisasi saat runtime oleh TinyTTF milik LVGL:

```csharp
_brand = LvFont.FromFile("assets/Inter.ttf", 18);
title.SetFont(_brand);

// atau dari resource yang disematkan
_symbols = LvFont.FromData(File.ReadAllBytes("NotoSansThai.ttf"), 20);

_brand.Size = 22;     // rasterisasi ulang pada ukuran lain
```

Glif dirender saat pertama kali dipakai lalu di-cache, sehingga frame pertama yang menampilkan
karakter baru sedikit lebih mahal daripada frame berikutnya. Seperti style, font TrueType harus hidup
lebih lama daripada widget yang memakainya: hapus widget-nya, lalu dispose font-nya.
`LvFont.Montserrat(size)` mengembalikan font bawaan sebagai `LvFont`, yang tidak pernah dibebaskan.

`LvStyle.Font(font)` menerapkan font melalui style bersama. Dalam dokumen layout, `Font` pada sebuah
node atau style bersama menyebut aset TrueType, dengan ukuran dari `FontSize`.

## Animasi

`LvAnimation` menganimasikan sebuah integer dari satu nilai ke nilai lain memakai timer animasi milik
LVGL sendiri, dan memanggil Anda dengan nilai setiap frame - jadi apa pun yang bisa digerakkan oleh
integer dapat dianimasikan.

```csharp
new LvAnimation(card, x => card.X = x, from: -200, to: 16)
{
    Duration = 400,
    Path = LvAnimPath.EaseOut,
}.Start();
```

| Properti | Arti |
|---|---|
| `Duration`, `Delay` | Milidetik |
| `Path` | `Linear`, `EaseIn`, `EaseOut`, `EaseInOut`, `Overshoot`, `Bounce`, `Step` |
| `ReverseDuration`, `ReverseDelay` | Berjalan kembali ke awal setelah setiap putaran maju |
| `RepeatCount`, `RepeatDelay` | `LvAnimation.Infinite` mengulang selamanya |
| `ApplyFromImmediately` | Menerapkan nilai awal seketika meski ada jeda, agar widget tidak melompat |
| `Completed` | Dipicu saat animasi selesai dengan sendirinya (bukan saat `Stop`) |

Pintasan mencakup kasus-kasus yang umum:

```csharp
panel.AnimateX(200);
panel.AnimateOpacity(from: 0, to: 255, durationMs: 250);
bar.AnimateValue(80);
```

**Daur hidup.** Animasi yang sedang berjalan dijaga tetap hidup oleh LVGL.Net - tidak perlu field.
Berikan widget yang digerakkannya sebagai target (pintasan melakukannya), dan menghapus widget itu
menghentikan animasinya; tanpa itu, frame berikutnya akan menulis ke widget yang sudah dihapus.
Animasi dikenali LVGL melalui sebuah id, bukan melalui widget-nya, sehingga `Stop()` menghentikan
tepat satu animasi meskipun beberapa animasi berjalan pada widget yang sama.

## Timer

`LvTimer` menjalankan callback secara berkala **di thread LVGL, di dalam run loop**, sehingga ia boleh
menyentuh widget secara langsung - tidak perlu `Post`:

```csharp
_clock = new LvTimer(1000, _ => time.Text = DateTime.Now.ToString("HH:mm:ss"));
_blink = new LvTimer(500, _ => led.Toggle(), repeatCount: 6);   // berhenti sejenak setelah 6 tick
```

`Pause`, `Resume`, `Period`, `FireSoon` (tick pada putaran loop berikutnya), `Restart(repeatCount)`,
dan `TickCount` melakukan persis seperti namanya. Dispose timer untuk menghentikan dan
membebaskannya. Timer yang jumlah pengulangannya habis akan berhenti sejenak (pause) alih-alih
membebaskan dirinya sendiri - auto-delete milik LVGL dimatikan, sehingga wrapper tidak pernah
memegang pointer yang sudah dibebaskan.

Pekerjaan yang memblokir - I/O berkas atau jaringan, sensor yang lambat - tetap tempatnya di thread
latar dengan `Post`: tick timer berjalan di antara frame, dan tick yang lambat menahan rendering.

## Saat callback melempar exception

Exception dari callback animasi atau timer (atau handler event) ditangkap sebelum sempat naik ke kode
C milik LVGL, lalu dilaporkan:

```csharp
LvglRuntime.CallbackFailed += (_, e) => Console.Error.WriteLine($"{e.Source}: {e.Exception}");
```

`e.Source` menyebutkan apa yang sedang berjalan - `"timer"`, `"animation"`,
`"event Clicked on LvButton"`.
