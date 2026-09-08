// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Diagnostics;
using System.Globalization;
using ActorNet;
using ActorNet.Persistence;
using ActorNet.Soak;

// A soak is not a longer unit test. A unit test asks whether something works; this asks whether it
// keeps working - whether anything grows without bound, leaks an activation, or quietly stops
// delivering after the first few million messages. Those only show up over time, which is why this
// is a program rather than a [Fact] with a big timeout.

var duration = TimeSpan.FromMinutes(Argument(args, "--minutes", 10));
var actors = (int)Argument(args, "--actors", 500);
var report = TimeSpan.FromSeconds(Argument(args, "--report-seconds", 30));

Console.WriteLine($"ActorNet soak: {duration}, {actors:N0} actors, reporting every {report}.");
Console.WriteLine();

await using var run = await SoakRun.StartAsync(actors);

var started = Stopwatch.StartNew();
var samples = new List<Sample>();
var next = report;

while (started.Elapsed < duration)
{
    await run.WorkAsync(TimeSpan.FromMilliseconds(200));

    if (started.Elapsed < next) continue;
    next += report;

    var sample = run.Sample(started.Elapsed);
    samples.Add(sample);
    Console.WriteLine(sample);
}

Console.WriteLine();
return Verdict.Report(samples, run.Sample(started.Elapsed));

static double Argument(string[] arguments, string name, double fallback)
{
    var at = Array.IndexOf(arguments, name);
    return at >= 0 && at + 1 < arguments.Length &&
           double.TryParse(arguments[at + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
        ? value
        : fallback;
}
