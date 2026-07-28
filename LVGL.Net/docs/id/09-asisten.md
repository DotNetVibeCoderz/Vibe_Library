# Jack The Code Bender — asisten desain

Asisten AI yang tertanam di dalam designer. Ia merancang layar LVGL, menulis kode .NET di
sekitarnya, dan dapat menyerahkan layout jadi langsung kembali ke kanvas.

Buka lewat tombol **Ask Jack** pada toolbar designer.

## Kemampuannya

| Kemampuan | Rincian |
|---|---|
| Merancang layout | Menghasilkan `.lvgl.json` tervalidasi yang bisa dibuka di designer dengan satu klik |
| Menghasilkan kode | Memakai `CSharpUiGenerator` yang sama dengan tombol Export C#, plus kerangka handler event |
| Mencari di internet | Tavily, bila kuncinya dikonfigurasi |
| Membaca halaman dan berkas | Mengambil URL lalu menyaringnya menjadi teks, atau mengunduh berkas teks apa adanya |
| Tanggal dan aritmetika | Supaya keduanya tidak dikira-kira di kepala model |
| Melihat gambar | Lampirkan tangkapan layar atau mockup lalu minta direproduksi |

## Penyedia model

Empat penyedia didukung, dapat dipilih per percakapan:

| Penyedia | Konfigurasi | Catatan |
|---|---|---|
| OpenAI | `OPENAI_API_KEY` atau `Assistant:OpenAI:ApiKey` | Bawaan |
| Anthropic | `ANTHROPIC_API_KEY` atau `Assistant:Anthropic:ApiKey` | Lihat catatan temperature di bawah |
| Gemini | `GEMINI_API_KEY` atau `Assistant:Gemini:ApiKey` | |
| Ollama | `Assistant:Ollama:Endpoint` | Berjalan lokal, tanpa kunci |

Semantic Kernel menyediakan konektor untuk OpenAI, Gemini, dan Ollama. Untuk Anthropic tidak ada,
jadi LVGL.Net mengimplementasikan `IChatCompletionService` di atas SDK resmi Anthropic — bukan lewat
endpoint yang kompatibel-OpenAI, karena bentuk request Claude memang berbeda dan shim justru akan
menyembunyikan bagian yang penting.

### Catatan penting soal temperature

Anthropic **menghapus parameter sampling** pada Claude Opus 5, Opus 4.8, Opus 4.7, Sonnet 5, dan
Fable 5. Mengirim `temperature` ke model-model itu ditolak dengan HTTP 400 — bukan sekadar
diabaikan.

`Assistant:Temperature` berlaku untuk semua penyedia, jadi konektor Anthropic mendeteksi keluarga
model tersebut dan menghilangkan nilainya dari request alih-alih membuat request gagal. Status bar
jendela chat menampilkan `temperature ignored on this model` saat itu terjadi. Model Claude yang
lebih lama tetap menerimanya secara normal. Gunakan pengaturan effort model untuk menukar kualitas
dengan biaya.

## Konfigurasi

Semuanya berada di `app.config` milik designer, di bawah `appSettings` dengan awalan `Assistant:`.
Variabel lingkungan dengan nama yang tercantum selalu menang atas nilai di berkas, jadi kunci bisa
dibiarkan kosong dan tidak ikut masuk ke kontrol versi.

```xml
<add key="Assistant:Provider" value="Anthropic" />
<add key="Assistant:Temperature" value="0.7" />
<add key="Assistant:MaxTokens" value="4096" />
<add key="Assistant:HistoryTurnLimit" value="40" />
<add key="Assistant:EnableFunctionCalling" value="true" />
<add key="Assistant:MaxToolIterations" value="8" />
<add key="Assistant:SystemPrompt" value="" />
<add key="Assistant:Anthropic:Model" value="claude-opus-5" />
<add key="Assistant:TavilyApiKey" value="" />
```

Membiarkan `Assistant:SystemPrompt` kosong berarti memakai persona bawaan, dan itu layak
dipertahankan: persona tersebut menyebutkan jebakan khas wrapper ini — koordinat tersandi, daur
hidup widget, afinitas thread — yang tidak bisa disimpulkan model dari nama kelas, dan yang
menghasilkan kode yang lolos kompilasi lalu berperilaku salah.

## Percakapan

Setiap percakapan disimpan sebagai satu berkas JSON di `%APPDATA%\LVGL.Net\assistant`.

- **New** memulai percakapan baru.
- **Reset** menghapus isi pesan tetapi mempertahankan percakapan, judul, dan penyedianya.
- **Delete** menghapusnya permanen.

Tiap percakapan mengingat penyedianya sendiri, jadi Anda bisa menyimpan satu di model Ollama lokal
untuk pertanyaan cepat dan satu lagi di model besar untuk pekerjaan desain yang berat.

## Lampiran

**Gambar** dikirim ke model sebagai konten gambar, sehingga model benar-benar melihatnya. Lampirkan
tangkapan layar lalu minta direproduksi menjadi layout.

**Dokumen** dirujuk lewat URL di dalam teks pesan; model sendiri yang memutuskan apakah perlu
membaca isinya dan mengambilnya dengan tool read-file. Dengan begitu, tidak setiap lampiran memakan
token terlepas relevan atau tidak.

Berkas disalin ke direktori sesi dan dilayani lewat host HTTP loopback di `127.0.0.1`. Perhatikan
konsekuensinya: **model yang dihosting tidak dapat menjangkau URL loopback.** Justru itulah sebabnya
gambar juga dikirim sebagai byte inline — URL adalah yang Anda dan transkrip lihat, byte adalah yang
diterima model. Model Ollama yang berjalan lokal bisa mengambil URL dokumen; model terhosting akan
memberi tahu bahwa ia tidak bisa.

Bila host lokal gagal mengikat port, lampiran tetap berfungsi dan memakai URL `file://`.

## Template prompt

Tombol **Prompts** membuka galeri contoh siap pakai untuk desain layout, kode backend, pembuatan
kode, reproduksi tangkapan layar, deployment, review, debug, kinerja, dan tema. Pilih satu, isi
bagian dalam kurung siku, lalu kirim.

## Tools

Model memanggil ini sendiri; Anda tidak menjalankannya secara langsung.

| Tool | Fungsi |
|---|---|
| `lvgl_design-describe_widgets` | Daftar widget dan properti yang diterima masing-masing |
| `lvgl_design-layout_template` | Layout awal yang valid: blank, dashboard, form, atau chart |
| `lvgl_design-create_layout` | Memvalidasi layout dan menawarkannya ke designer |
| `lvgl_design-validate_layout` | Memeriksa layout tanpa menyimpannya |
| `lvgl_design-generate_csharp` | Kelas partial hasil generate |
| `lvgl_design-generate_event_handlers` | Belahan tulisan tangan, dengan kerangka per widget interaktif |
| `tavily-search` | Pencarian internet |
| `web-scrape_page` | Mengambil halaman sebagai teks terbaca |
| `web-read_file` | Mengunduh berkas teks apa adanya |
| `web-http_head` | Memeriksa apakah sebuah URL dapat dijangkau |
| `time-*` | Tanggal dan waktu kini, aritmetika tanggal, durasi |
| `math-*` | Evaluasi ekspresi, persentase, penskalaan piksel |

`create_layout` memvalidasi terhadap model dokumen yang sesungguhnya sebelum menjawab, jadi layout
yang salah bentuk kembali sebagai daftar masalah yang bisa diperbaiki model — bukan sebagai JSON
yang tampak meyakinkan tetapi gagal dibuka.

## Catatan keamanan

Layak dipahami sebelum mengarahkan asisten ke konten sembarangan:

- **Halaman yang diambil dan hasil pencarian tidak tepercaya.** Keduanya bisa memuat teks yang
  ditujukan kepada model, bukan kepada Anda. Kedua tool membungkus keluarannya dalam blok bertanda
  yang menyatakan hal itu. Ini mengurangi risiko, bukan menghilangkannya. Perlakukan asisten yang
  baru saja membaca halaman berbahaya dengan kecurigaan yang sama seperti halaman itu sendiri.
- **Tool web menolak alamat privat dan loopback**, sehingga halaman yang diambil tidak bisa
  mengarahkannya ke jaringan Anda sendiri atau ke endpoint metadata cloud.
- **Tool math adalah parser, bukan interpreter.** Ia hanya memahami angka, operator, dan daftar
  fungsi tetap; selain itu menjadi galat parsing. Ia tidak bisa dibujuk mengeksekusi kode.
- **Host lampiran hanya melayani satu direktori** dan menolak permintaan yang memuat pemisah jalur
  atau segmen traversal.
- **Kunci API tidak pernah dikirim ke model** — hanya dipakai untuk mengautentikasi permintaan.

## Pemecahan masalah

**"No API key for X"** — setel variabel lingkungan yang disebut dalam pesan, atau isi kunci
`app.config` yang bersesuaian, lalu buka ulang jendela chat.

**Area chat menyebut WebView2 tidak ada** — pasang Microsoft Edge WebView2 Runtime. Komponen ini
bawaan Windows 11, jadi kasus ini tidak lazim. Semua fungsi selain transkrip terender tetap bekerja.

**Status bar menyebut `web search off`** — `Assistant:TavilyApiKey` kosong, jadi tool pencarian
tidak didaftarkan sama sekali. Ini disengaja: tool yang diiklankan tetapi selalu gagal memboroskan
satu putaran dan mengajari model untuk tidak mempercayainya.

**Balasan berhenti dengan "I stopped after N tool rounds"** — model berputar tanpa mencapai jawaban.
Persempit permintaan, atau naikkan `Assistant:MaxToolIterations`.

**Jawaban Ollama buruk atau tool diabaikan** — model lokal kecil lemah dalam function calling. Coba
model yang lebih besar, atau matikan `Assistant:EnableFunctionCalling` dan ajukan pertanyaan
langsung.
