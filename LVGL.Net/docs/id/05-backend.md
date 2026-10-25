# Backend

Backend adalah segala hal yang spesifik platform: tempat menaruh piksel, dan tempat mengambil input.
Library native sama sekali tidak memuat driver tampilan, jadi di sinilah satu-satunya kode platform
berada.

```csharp
public interface ILvglBackend : IDisposable
{
    int Width { get; }
    int Height { get; }
    LvPointerState Pointer { get; }

    void Blit(in LvArea area, nint pixels, bool isLastFlush);
    bool PumpEvents();          // false => pengguna meminta menutup
}
```

Seluruh anggotanya hanya dipanggil dari thread LVGL.

## SDL — `LVGL.Net.Sdl`

Keluaran berjendela di Windows, macOS, dan desktop Linux mana pun, termasuk Pi yang menjalankan X11
atau Wayland.

```csharp
using var backend = new SdlBackend(1024, 600, "Aplikasi saya", vsync: true);
```

Setiap region yang di-flush masuk ke tekstur streaming `ARGB8888`; tekstur ditampilkan sekali per
frame, pada region terakhir. Karena tekstur mempertahankan isinya antar frame, mode gambar-ulang
parsial milik LVGL bekerja benar di sini — hanya potongan yang berubah yang diunggah.

`SdlBackend` juga mengimplementasikan `ILvglKeyboardSource`, sehingga input papan ketik bekerja bila
`LvglOptions.EnableKeyboard` diaktifkan. Keycode SDL dipetakan ke kode navigasi `LV_KEY_*` milik
LVGL; ASCII yang dapat dicetak diteruskan apa adanya.

Bila tidak ada renderer berakselerasi — CI tanpa layar, sebagian konfigurasi Pi — ia otomatis
beralih ke renderer perangkat lunak milik SDL.

## Framebuffer Linux — `LVGL.Net.Linux`

Keluaran langsung ke `/dev/fb0` tanpa sistem jendela. Inilah backend untuk Pi yang menggerakkan
panel sebagai instrumen atau kiosk.

```csharp
var pointer = EvdevPointer.AutoDetect(screenWidth, screenHeight);
using var backend = new FramebufferBackend("/dev/fb0", pointer);
```

Ia memetakan framebuffer ke memori dan menyalin setiap region yang di-flush ke dalamnya, sehingga
satu gambar-ulang berarti satu `memcpy` per baris pindai. Perangkat 32-bit dan 16-bit didukung;
urutan kanal merah/biru dibaca dari driver dan hanya ditukar bila berbeda dari keluaran LVGL.

`EvdevPointer` membaca `/dev/input/eventN` dan menangani kedua jenis perangkat: panel sentuh
melaporkan posisi absolut, yang diskalakan ulang memakai rentang yang diiklankan driver melalui
`EVIOCGABS`, sedangkan tetikus melaporkan gerakan relatif, yang diakumulasi dan dibatasi.

Keduanya memerlukan keanggotaan grup:

```bash
sudo usermod -aG video,input $USER    # lalu keluar dan masuk kembali
```

## DRM/KMS — `LVGL.Net.Linux`

Raspberry Pi OS terkini menggerakkan layar melalui driver KMS milik kernel (`vc4-kms-v3d`); perangkat
`/dev/fb0` yang dipakai backend framebuffer hanyalah emulasi di atasnya, yang hanya bisa menyalin ke
gambar yang sedang ditampilkan sehingga terjadi tearing saat ada gerakan. `DrmBackend` berbicara
langsung dengan KMS:

```csharp
var pointer = EvdevPointer.AutoDetect(800, 480);
using var backend = new DrmBackend(pointer: pointer);   // kartu pertama dengan layar terhubung
Console.WriteLine($"{backend.DevicePath}: {backend.Mode}");
```

Backend ini mengalokasikan dua buffer scan-out dan bergantian di antara keduanya saat vertical
blank. LVGL merender potongan-potongan ke salinan bayangan layar; di akhir frame, region yang rusak
disalin ke buffer tersembunyi dan sebuah page flip diantrekan. Buffer tersembunyi terakhir kali
menampilkan frame *sebelum* frame sebelumnya, sehingga salinannya mencakup kerusakan frame ini dan
frame terakhir sekaligus - pembaruan kecil tetap kecil dan tidak ada buffer yang pernah basi.
Penantian flip terjadi di awal frame berikutnya, sehingga LVGL tetap merender selagi layar menyusul.

- **Tanpa libdrm.** ioctl-nya dideklarasikan langsung; struktur argumennya adalah ABI kernel
  berlebar tetap, identik di arm, arm64, dan x64, dan dikunci oleh unit test.
- **Layar harus bebas.** Hanya satu program yang boleh menggerakkannya: boot ke konsol, atau
  hentikan desktop pada layar itu. Bila desktop sedang berjalan, pakai backend SDL.
- **Fallback.** Bila driver menolak page flip, backend menggambar ke buffer yang terlihat dan
  melaporkan `IsDoubleBuffered == false` alih-alih berhenti.
- Mode konsol dipulihkan saat backend di-dispose.

Contoh Pi memakainya dengan `--drm`.

## Headless — di dalam `LVGL.Net`

Merender ke `byte[]` managed. Tiga hal memakainya: preview langsung di designer, yang menyalin buffer
ke sebuah bitmap; test, yang dapat memeriksa piksel tanpa display server; dan screenshot - `SavePng`
menulis persis apa yang digambar LVGL.

```csharp
using var backend = new HeadlessBackend(800, 480);
using var app = LvglApplication.Create(backend, new LvglOptions { EnablePointer = false });

app.RunFrame();

if (backend.IsDirty)
{
    ReadOnlySpan<byte> frame = backend.Frame;   // XRGB8888, stride = Width * 4
    backend.ClearDirty();
}

backend.SetPointer(120, 80, pressed: true);     // input sintetis

app.Display.RefreshNow();                       // render semua yang tertunda, saat ini juga
backend.SavePng("screen.png");
```

## Membuat backend sendiri

Seluruh kontraknya hanya empat anggota. Backend untuk layar SPI, panel e-paper, atau aliran frame
jarak jauh cukup satu kelas - `DrmBackend` adalah contoh nyata backend dengan buffer ganda.

```csharp
public sealed class MyBackend : ILvglBackend
{
    public int Width => 320;
    public int Height => 240;
    public LvPointerState Pointer { get; private set; }

    public void Blit(in LvArea area, nint pixels, bool isLastFlush)
    {
        // `pixels` berformat XRGB8888, rapat, stride = area.Width * 4.
        // Hanya valid selama pemanggilan - salin, jangan disimpan.
        // Batas `area` bersifat inklusif: region 1x1 punya X1 == X2.

        if (isLastFlush) Present();
    }

    public bool PumpEvents()
    {
        Pointer = ReadTouchPanel();
        return true;
    }

    public void Dispose() { }
}
```

Tiga aturan:

- **`isLastFlush` adalah saat untuk menampilkan.** Perangkat dengan swap chain atau buffer ganda
  harus membalik buffer di sana, bukan pada setiap region.
- **Jangan menyimpan `pixels`.** Pointer itu menunjuk ke draw buffer LVGL yang langsung dipakai
  ulang.
- **Melempar exception diperbolehkan.** Exception ditangkap dan dilempar ulang dari run loop, bukan
  naik ke C.

Implementasikan juga `ILvglKeyboardSource` bila perangkat memiliki tombol.

## Mode render

| Mode | Buffer | Untuk |
|------|--------|-------|
| `Partial` (bawaan) | Dua potongan setinggi `PartialBufferLines` baris | Target tertanam; memori paling hemat |
| `Direct` | Dua buffer sepenuh layar | Blit lebih sedikit dan lebih besar saat memori berlimpah |
| `Full` | Dua buffer sepenuh layar, digambar ulang seluruhnya | Perangkat yang tidak mendukung pembaruan parsial |

```csharp
var app = LvglApplication.Create(backend, new LvglOptions
{
    RenderMode = LvRenderMode.Partial,
    PartialBufferLines = 32,     // 0 = sepersepuluh tinggi layar
});
```

`app.Display.AllocatedBytes` melaporkan biaya sesungguhnya dari pilihan tersebut.
