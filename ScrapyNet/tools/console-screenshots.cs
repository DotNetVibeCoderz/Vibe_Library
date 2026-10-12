// Screenshots of the Scrapy.Net Console for the docs, and a smoke test of every view.
// Start the host first:  ASPNETCORE_ENVIRONMENT=Development dotnet run --project samples/ScrapyNet.ServerHost
// Then:                  dotnet run tools/console-screenshots.cs -- docs/images
// It fails loudly on any browser console error (including Content-Security-Policy violations).
#:package Microsoft.Playwright@1.61.0
#:property JsonSerializerIsReflectionEnabledByDefault=true
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Playwright;

var baseUrl = "http://localhost:5080";
var outDir = args.Length > 0 ? args[0] : ".";
Directory.CreateDirectory(outDir);
using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
http.DefaultRequestHeaders.Add("X-Api-Key", "dev-admin");

var books = JsonDocument.Parse(await http.GetStringAsync("/api/spiders/sandbox-books")).RootElement;
var startUrl = books.GetProperty("target").GetProperty("startUrls")[0].GetString()!;
var quotesUrl = startUrl.Replace("/books/", "/quotes/");

// Finished runs for history, then two slow runs so the dashboard has live webs.
await http.PostAsJsonAsync("/api/spiders/sandbox-quotes/run", new { });
await Task.Delay(3000);
await http.PostAsJsonAsync("/api/spiders/sandbox-books/run", new { settings = new Dictionary<string, string> { ["DOWNLOAD_DELAY"] = "0.5" } });
await http.PostAsJsonAsync("/api/spiders/sandbox-quotes/run", new { settings = new Dictionary<string, string> { ["DOWNLOAD_DELAY"] = "1.5" } });

using var pw = await Playwright.CreateAsync();
await using var browser = await pw.Chromium.LaunchAsync();
var errors = new List<string>();

async Task<IPage> NewPage(int width, int height, ColorScheme scheme)
{
    var context = await browser.NewContextAsync(new() { ViewportSize = new() { Width = width, Height = height }, ColorScheme = scheme, DeviceScaleFactor = 1 });
    var page = await context.NewPageAsync();
    page.Console += (_, m) => { if (m.Type is "error" or "warning") errors.Add($"[{m.Type}] {m.Text}"); };
    page.PageError += (_, e) => errors.Add("[pageerror] " + e);
    await page.GotoAsync(baseUrl + "/admin/");
    return page;
}

var page = await NewPage(1440, 960, ColorScheme.Light);
await page.ScreenshotAsync(new() { Path = Path.Combine(outDir, "console-signin.png") });
await page.FillAsync("#api-key", "dev-admin");
await page.ClickAsync("button[type=submit]");
await page.WaitForSelectorAsync(".hero-figure");
await Task.Delay(14000);   // let the throughput chart collect samples
await page.ScreenshotAsync(new() { Path = Path.Combine(outDir, "console-dashboard.png"), FullPage = true });
Console.WriteLine("running webs: " + await page.Locator(".orb").CountAsync());

await page.GotoAsync(baseUrl + "/admin/#/spiders"); await page.WaitForSelectorAsync("tbody tr td");
await Task.Delay(500);
await page.ScreenshotAsync(new() { Path = Path.Combine(outDir, "console-spiders.png") });

await page.GotoAsync(baseUrl + "/admin/#/jobs"); await Task.Delay(1500);
await page.ScreenshotAsync(new() { Path = Path.Combine(outDir, "console-jobs.png") });
await page.Locator("tbody tr.clickable").Last.ClickAsync();
await Task.Delay(2500);
await page.ScreenshotAsync(new() { Path = Path.Combine(outDir, "console-job.png"), FullPage = true });
Console.WriteLine("job items rows: " + await page.Locator(".items-table tbody tr").CountAsync());

await page.GotoAsync(baseUrl + "/admin/#/builder"); await page.WaitForSelectorAsync("#b-url");
await page.FillAsync("#b-url", quotesUrl);
await page.FillAsync("#b-item", "div.quote");
await page.FillAsync("#b-follow", "li.next a");
await page.ClickAsync("text=Preview");
await page.WaitForSelectorAsync(".items-table");
await Task.Delay(300);
await page.ScreenshotAsync(new() { Path = Path.Combine(outDir, "console-builder.png"), FullPage = true });
Console.WriteLine("preview rows: " + await page.Locator(".items-table tbody tr").CountAsync());

await page.GotoAsync(baseUrl + "/admin/#/schedules"); await Task.Delay(800);
await page.ScreenshotAsync(new() { Path = Path.Combine(outDir, "console-schedules.png") });
await page.GotoAsync(baseUrl + "/admin/#/alerts"); await Task.Delay(1200);
await page.ScreenshotAsync(new() { Path = Path.Combine(outDir, "console-alerts.png") });

// Dark mode and a phone, on the dashboard.
await http.PostAsJsonAsync("/api/spiders/sandbox-books/run", new { settings = new Dictionary<string, string> { ["DOWNLOAD_DELAY"] = "0.5" } });
var dark = await NewPage(1440, 960, ColorScheme.Dark);
await dark.FillAsync("#api-key", "dev-viewer"); await dark.ClickAsync("button[type=submit]");
await dark.WaitForSelectorAsync(".hero-figure"); await Task.Delay(12000);
await dark.ScreenshotAsync(new() { Path = Path.Combine(outDir, "console-dashboard-dark.png"), FullPage = true });
Console.WriteLine("viewer sees Stop buttons: " + await dark.Locator("text=Stop").CountAsync());
var phone = await NewPage(390, 844, ColorScheme.Light);
await phone.FillAsync("#api-key", "dev-admin"); await phone.ClickAsync("button[type=submit]");
await phone.WaitForSelectorAsync(".hero-figure"); await Task.Delay(3000);
await phone.ScreenshotAsync(new() { Path = Path.Combine(outDir, "console-phone.png"), FullPage = true });
var overflow = await phone.EvaluateAsync<bool>("document.documentElement.scrollWidth > window.innerWidth + 1");
Console.WriteLine("phone horizontal overflow: " + overflow);

Console.WriteLine(errors.Count == 0 ? "no console errors" : string.Join("\n", errors.Distinct()));
return errors.Count == 0 && !overflow ? 0 : 1;
