# 9. Halaman JavaScript

![Rendering JavaScript](../images/gallery-browser.png)

Sebagian halaman membangun kontennya dengan JavaScript, sehingga HTTP biasa hanya melihat kerangka
kosong. `Gravicode.ScrapyNet.Playwright` hanya merender request yang Anda tandai, di Chromium, Firefox,
atau WebKit headless, lalu memberikan DOM hasil render ke callback sebagai `HtmlResponse` biasa.

```bash
dotnet add package Gravicode.ScrapyNet.Playwright
pwsh bin/Debug/net10.0/playwright.ps1 install chromium     # sekali per mesin
```

atau dari kode: `PlaywrightSetup.InstallBrowsers("chromium");`.

```csharp
using ScrapyNet.Playwright;

settings.UsePlaywright(browser: "chromium", headless: true, maxPages: 8, blockResourceTypes: ["image", "font", "media"]);

public override async IAsyncEnumerable<Request> StartAsync(CancellationToken ct = default)
{
    yield return new Request("https://shop.example/app")
        .WithPlaywright(
            PageMethod.WaitForSelector("div.product"),
            PageMethod.ScrollToBottom(times: 5, pauseMs: 800),   // infinite scroll
            PageMethod.Click("button.load-more"),
            PageMethod.Screenshot());
    await Task.CompletedTask;
}
```

Request tanpa `.WithPlaywright()` tetap memakai HTTP biasa yang cepat melalui handler yang sama.

| Page method | Fungsi |
|---|---|
| `WaitForSelector(css, timeoutMs)` | Menunggu sampai elemen ada |
| `Click(css)`, `Fill(css, value)`, `Press(css, key)` | Interaksi |
| `Evaluate(js)` | Menjalankan JavaScript; hasil masuk ke `meta["playwright_evaluate_results"]` |
| `ScrollToBottom(times, pauseMs)` | Memicu lazy loading |
| `WaitForTimeout(ms)`, `WaitForLoadState(state)` | Menunggu |
| `Screenshot(fullPage)` | Byte PNG di `meta["playwright_screenshot"]` |

Kunci meta: `playwright`, `playwright_page_methods`, `playwright_context` (cookie/storage terpisah per
nama), `playwright_wait_until` (`load`, `domcontentloaded`, `networkidle`, `commit`),
`playwright_include_page` (simpan `IPage` di `meta["playwright_page"]`; tutup sendiri).

Setting: `PLAYWRIGHT_BROWSER_TYPE`, `PLAYWRIGHT_HEADLESS`, `PLAYWRIGHT_MAX_PAGES`,
`PLAYWRIGHT_NAVIGATION_TIMEOUT` (ms), `PLAYWRIGHT_BLOCKED_RESOURCE_TYPES`.

Browser dijalankan secara malas pada request pertama yang dirender dan ditutup bersama spider. Jumlah
halaman dibatasi semaphore, dan memblokir gambar serta font sering memangkas waktu render hingga separuh.
