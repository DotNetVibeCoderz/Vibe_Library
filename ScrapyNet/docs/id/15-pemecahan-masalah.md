# 15. Pemecahan masalah

**Spider langsung selesai dengan 0 item.**
Lihat log di level `INFO`: filter offsite (`AllowedDomains` tidak mencakup host URL awal), robots.txt
(`ROBOTSTXT_OBEY` dengan aturan yang melarang — statistik `robotstxt/forbidden`), atau error HTTP
(`Ignoring response <404 ...>`). Jalankan `scrapynet parse URL --spider NAMA` untuk melihat persis apa
yang di-yield callback.

**Selektor tidak menemukan apa-apa padahal browser menampilkan datanya.**
Kontennya mungkin dibangun JavaScript. Periksa dengan `scrapynet view URL`; bila datanya tidak ada di
sana, gunakan [Playwright](09-javascript.md). Periksa juga panggilan JSON yang dilakukan halaman —
mengambil langsung dari API-nya lebih cepat.

**`Missing scheme in request url`.**
Request butuh URL absolut. Gunakan `response.Follow(href)` atau `response.UrlJoin(href)` untuk tautan relatif.

**Request sepertinya dilewati.**
Filter duplikat membuang request yang sudah pernah terlihat (`dupefilter/filtered` di statistik). Set
`DontFilter = true` pada request yang memang harus diulang, atau pastikan URL Anda tidak hanya berbeda
pada parameter yang tidak penting.

**Run jeda/lanjut mulai dari awal lagi.**
Berhentilah dengan rapi (Ctrl+C sekali), gunakan `JOBDIR` yang sama, dan jadikan callback sebagai method
bernama milik spider — lambda tidak bisa disimpan (`scheduler/unserializable` menghitungnya).

**`NotConfiguredException` di log / middleware tidak berjalan.**
Komponen bawaan menonaktifkan diri bila setting-nya tidak ada (`FILES_STORE`, `HTTPCACHE_ENABLED`,
`PROXY_LIST`, ...). Set nilainya, dan periksa baris "Enabled downloader middlewares" yang dicatat saat mulai.

**Timeout pada situs lambat.**
Naikkan `DOWNLOAD_TIMEOUT` (atau `meta["download_timeout"]` untuk satu request), turunkan konkurensi per
domain, dan aktifkan AutoThrottle.

**Mendapat 403 atau 429.**
Perkenalkan diri dengan `USER_AGENT` yang jelas, perlambat (`DOWNLOAD_DELAY`, AutoThrottle), patuhi
robots.txt, dan tambahkan `RETRY_BACKOFF_BASE` untuk 429. Bila situs mengizinkan crawl lewat proxy,
gunakan `ProxyRotationMiddleware`.

**Playwright: `Executable doesn't exist`.**
Pasang browser sekali: `pwsh bin/Debug/net10.0/playwright.ps1 install chromium` atau
`PlaywrightSetup.InstallBrowsers("chromium")`.

**Error serialisasi JSON di skrip `dotnet run file.cs`.**
Aplikasi file-based mematikan JSON berbasis refleksi. Scrapy.Net tidak terpengaruh; untuk tipe Anda
sendiri tambahkan `#:property JsonSerializerIsReflectionEnabledByDefault=true`.

**Test: `dotnet test` gagal dengan MSB4025.**
xunit.v3 berjalan di Microsoft.Testing.Platform; jalankan proyek test secara langsung:
`dotnet run --project tests/ScrapyNet.Tests`.

**Panggilan LLM gagal dengan "Unsupported parameter: max_tokens" atau "temperature".**
Model reasoning butuh `max_completion_tokens` dan temperature default: gunakan
`OpenAiChatModel.ForAzure(...)`, atau set `TokenLimitParameter = "max_completion_tokens"` dan
`Temperature = null`.
