# Arsitektur

## Bentuk keseluruhan

```
        managed                                    native
  ┌───────────────────────┐              ┌────────────────────────┐
  │ Aplikasi Anda         │              │                        │
  ├───────────────────────┤   P/Invoke   │  lvglnet (satu library)│
  │ LVGL.Net              │─────────────▶│    sumber LVGL v9      │
  │  widget, style,       │◀─────────────│    + shim ABI lvn_*    │
  │  event, run loop      │   callback   │                        │
  ├───────────────────────┤              └────────────────────────┘
  │ ILvglBackend          │
  │  SDL / framebuffer /  │   piksel keluar, input masuk
  │  headless             │
  └───────────────────────┘
```

Keputusan desain terpenting adalah di mana batas platform diletakkan.

## Mengapa library native tidak memuat driver tampilan

LVGL menyediakan driver untuk SDL, X11, Wayland, framebuffer, dan lainnya. LVGL.Net **tidak**
mengompilasi satu pun. Sebagai gantinya, build native adalah LVGL polos tanpa driver: ia merender ke
sebuah buffer lalu memanggil callback flush, dan segala hal setelah itu — menaruh piksel ke layar,
membaca panel sentuh — adalah kode managed di balik `ILvglBackend`.

Tiga keuntungannya:

1. **Satu artefak native untuk semua skenario.** Biner `lvglnet` yang sama melayani jendela SDL,
   framebuffer Raspberry Pi, dan preview off-screen di designer. Menambah target keluaran baru
   berarti menulis satu kelas C#, bukan mengompilasi ulang C.
2. **Tidak ada rantai dependensi native.** Library ini hanya terhubung ke libc dan libm, jadi deploy
   ke Pi tidak menyeret tumpukan X11.
3. **Titik uji yang jelas.** `HeadlessBackend` merender ke `byte[]`, yang dipakai designer untuk
   preview sungguhan dan dipakai test untuk memeriksa hasil render.

Biayanya satu `memcpy` per region yang di-flush — persis yang akan dilakukan sebuah driver.

## Shim ABI

Sebagian besar API LVGL bisa dipanggil langsung lewat P/Invoke. Sebagian kecil tidak bisa, dan
`native/shim/lvglnet_shim.c` menangani tepat bagian itu:

| Masalah | Penyebab kegagalan | Solusi shim |
|---------|--------------------|-------------|
| `lv_color_t` dikirim by value | Struct 3 byte masuk register di SysV/AAPCS tetapi lewat pointer tersembunyi di Windows x64 | `lvn_*_color(..., uint32_t rgb, ...)` menerima `0xRRGGBB` terkemas |
| `lv_style_t` dialokasikan pemanggil | Ukurannya detail waktu kompilasi dari `lv_conf.h` | `lvn_style_create` / `lvn_style_delete` |
| `lv_indev_data_t`, `lv_area_t` | Kode managed harus menebak offset struct | `lvn_indev_data_set_*`, `lvn_area_get` |
| `lv_font_montserrat_*` | Diekspor sebagai *variabel*, yang tidak bisa diikat P/Invoke secara portabel | `lvn_font_montserrat(size)` |
| Ordinal `lv_event_code_t` | LVGL menyisipkan `LV_EVENT_ROTARY` di tengah enum pada 9.1 sehingga nilai berikutnya bergeser | `lvn_event_code(stable_id)` menerjemahkan saat runtime |
| `LV_PCT`, `LV_SIZE_CONTENT` | Makro C tidak ada di dalam shared library | Diimplementasikan ulang di `LvCoord`, diverifikasi oleh test |

Selebihnya memanggil `lv_*` secara langsung. Shim tidak menambah biaya per frame: jalur render
(`lv_timer_handler`, callback flush) tidak melewatinya.

`LVN_ABI_VERSION` diperiksa saat startup, sehingga build native yang usang gagal dengan pesan jelas
alih-alih merusak memori.

## Threading

LVGL bersifat single-thread dan tanpa sinkronisasi. LVGL.Net mencatat thread yang memanggil
`LvglRuntime.Initialize` dan menegakkannya:

```csharp
LvglRuntime.EnsureUiThread();     // melempar LvglThreadAccessException dari thread yang salah
```

Pekerjaan latar mengembalikan hasil melalui antrean aplikasi:

```csharp
// pada thread sensor
var reading = sensors.Read();
app.Post(() => dashboard.Update(reading));   // dijalankan di awal iterasi berikutnya
```

`Post` bersifat fire-and-forget; `Invoke` memblokir sampai aksi selesai dijalankan.

## Run loop

Satu iterasi `LvglApplication.RunFrame`:

1. Menguras antrean pekerjaan yang di-`Post`.
2. Memberi LVGL selisih milidetik yang sebenarnya (`lv_tick_inc`), agar animasi tetap benar ketika
   sebuah frame berjalan lama.
3. `lv_timer_handler()` — menjalankan timer, menggambar ulang area yang tidak valid, memanggil
   callback flush untuk setiap region.
4. Melempar ulang apa pun yang dilempar backend saat LVGL memanggil balik ke kode managed.
5. `backend.PumpEvents()` — event host dan status input terbaru; mengembalikan false untuk keluar.

`Run` kemudian tidur selama `min(saran idle dari LVGL, MaxIdleSleepMs)`, dibatasi bawah oleh
`MinIdleSleepMs` agar UI yang menganggur tidak menghabiskan satu inti prosesor.

## Callback ke kode managed

Callback berupa method statis `[UnmanagedCallersOnly]` dengan pointer fungsi unmanaged — tanpa stub
marshalling delegate di jalur render.

- **Flush tampilan** menemukan instansinya dari `GCHandle` yang disimpan di user data display.
  Callback ini berjalan berkali-kali per frame, jadi tidak boleh melakukan pencarian.
- **Event widget** ditemukan lewat registry statis berkunci pointer native. `GCHandle` justru salah
  di sini: LVGL memanggil callback *sambil* membongkar sebuah objek, dan membaca `GCHandle` yang
  sudah dibebaskan adalah undefined behaviour, sedangkan pencarian dictionary yang gagal hanya
  menjadi no-op yang aman.

Tidak ada exception managed yang boleh naik ke C. Jalur flush menyimpan exception-nya dan run loop
melemparnya kembali; handler event menelan exception-nya sendiri.

## Format warna

`LV_COLOR_DEPTH` dikunci pada 32. LVGL menghasilkan XRGB8888 (urutan byte B, G, R, X pada mesin
little-endian), yang disalin apa adanya ke tekstur `ARGB8888` milik SDL dan ke framebuffer 32-bit,
serta dibaca sebagai `Bgr32` milik WPF di designer. `LvglRuntime.Initialize` menolak library native
dengan kedalaman warna berbeda alih-alih menggambar warna yang tertukar.

## Batas antar proyek

- `LVGL.Net` tidak bergantung pada apa pun selain BCL. Tanpa framework UI, tanpa paket NuGet.
- `LVGL.Net.Sdl` dan `LVGL.Net.Linux` adalah assembly terpisah agar deploy ke Pi tidak membawa
  binding SDL yang tidak dipakai.
- `LVGL.Net.Ui` memuat model layout yang dipakai bersama oleh designer, loader runtime, dan
  generator kode, sehingga layout yang dipratinjau dan yang berjalan tidak dapat berbeda.
- `LVGL.Designer` adalah satu-satunya proyek khusus Windows, dan hanya karena WPF memang begitu.
