using ScrapyNet.Samples;
using ScrapyNet.Sandbox;

// Scrapy.Net samples — every sample runs against the offline sandbox site started here, except
// "toscrape", which crawls the public quotes.toscrape.com practice site.
//
//   dotnet run --project samples/ScrapyNet.Samples               # list samples
//   dotnet run --project samples/ScrapyNet.Samples -- quotes     # run one
//   dotnet run --project samples/ScrapyNet.Samples -- all        # run every offline sample
//
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

var samples = SampleRegistry.All;
if (args.Length == 0)
{
    Console.WriteLine("Scrapy.Net samples. Usage: dotnet run -- <name> | all\n");
    foreach (var s in samples) Console.WriteLine($"  {s.Name,-14} {s.Description}");
    return 0;
}

await using var site = await SandboxSite.StartAsync();
var selected = args[0] == "all" ? samples.Where(s => !s.Online).ToList() : samples.Where(s => s.Name == args[0]).ToList();
if (selected.Count == 0)
{
    Console.Error.WriteLine($"Unknown sample '{args[0]}'.");
    return 1;
}

foreach (var sample in selected)
{
    Console.WriteLine($"\n=== {sample.Name}: {sample.Description} ===");
    await sample.Run(site);
}
return 0;
