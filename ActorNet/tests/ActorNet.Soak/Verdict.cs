// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace ActorNet.Soak;

/// <summary>
/// Turns a run's samples into a pass or a fail, and says which.
/// </summary>
/// <remarks>
/// A soak that prints numbers and exits zero is a soak nobody reads. These are the four questions
/// worth asking of a long run, each with a threshold that a healthy run clears by a wide margin -
/// the point is to catch a trend, not to measure one.
/// </remarks>
public static class Verdict
{
    /// <summary>Heap growth allowed between the first sample and the last, as a multiple.</summary>
    /// <remarks>
    /// Applied to the lowest reading in each window rather than to a single sample, because a
    /// managed heap is a sawtooth and either end of one says more about when the collector last ran
    /// than about what is being held. Still generous: a genuine leak clears this by a wide margin.
    /// </remarks>
    private const double HeapGrowthLimit = 3.0;

    /// <summary>Activations allowed at the end, as a multiple of the working set.</summary>
    /// <remarks>
    /// The working set is bounded by construction - a fixed list of addresses - so more activations
    /// than addresses means something is holding cells that should have been swept.
    /// </remarks>
    private const double ActorGrowthLimit = 2.5;

    /// <summary>How much of its opening throughput a run must still have at the end.</summary>
    /// <remarks>
    /// Loose, because the opening third includes a cold start - JIT, connections being made, actors
    /// activated for the first time - which is genuinely different work. It is a check for a run
    /// that is winding down, not a benchmark.
    /// </remarks>
    private const double RateFloor = 0.4;

    /// <summary>Reports, and returns the exit code.</summary>
    public static int Report(IReadOnlyList<Sample> samples, Sample last)
    {
        if (samples.Count < 2)
        {
            Console.WriteLine("Too few samples to say anything about a trend. Run for longer.");
            return 2;
        }

        var problems = new List<string>();
        var third = Math.Max(1, samples.Count / 3);

        // The lowest reading in a window, not the first or the last one. A managed heap is a
        // sawtooth - these samples swung between 2 and 11 MiB every twenty seconds on a run with no
        // leak at all - and the floor after a collection is the part that approximates the live set,
        // which is the part a leak grows. Comparing two instantaneous readings compares GC timing.
        var heapBefore = samples.Take(third).Min(s => s.ManagedBytes);
        var heapAfter = samples.TakeLast(third).Min(s => s.ManagedBytes);

        var heapGrowth = heapBefore == 0 ? 1 : (double)heapAfter / heapBefore;
        if (heapGrowth > HeapGrowthLimit)
            problems.Add($"the live heap grew {heapGrowth:N1}x, from {Mib(heapBefore)} to {Mib(heapAfter)}");

        var actorCeiling = samples.Max(s => s.LiveActors) * ActorGrowthLimit;
        if (last.LiveActors > actorCeiling)
            problems.Add($"activations ended at {last.LiveActors:N0}, above the {actorCeiling:N0} the run held");

        if (last.DeadLetters > 0)
            problems.Add($"{last.DeadLetters:N0} message(s) were dead-lettered");

        // Some failures are expected - the soak trips actors on purpose - so this is about the rate
        // rather than the presence. A percent of everything sent is far above a run that is fine.
        var failureRate = last.Sent == 0 ? 0 : (double)last.Failures / last.Sent;
        if (failureRate > 0.01)
            problems.Add($"{last.Failures:N0} of {last.Sent:N0} sends failed ({failureRate:P2})");

        // A run that stopped delivering would still look busy from the sending side, so the two
        // counts are compared rather than either being read alone.
        if (last.Handled < last.Sent / 2)
            problems.Add($"only {last.Handled:N0} of {last.Sent:N0} messages were handled");

        // The question a soak is best placed to answer: is it still as fast at the end as it was at
        // the start. Thirds are compared rather than first against last, because a single interval
        // can be a collection or a scheduler hiccup.
        var opening = samples.Take(third).Select(s => s.Rate).Where(r => r > 0).DefaultIfEmpty(0).Average();
        var closing = samples.TakeLast(third).Select(s => s.Rate).Where(r => r > 0).DefaultIfEmpty(0).Average();

        if (opening > 0 && closing < opening * RateFloor)
            problems.Add($"throughput fell from {opening:N0}/s to {closing:N0}/s ({closing / opening:P0} of where it started)");

        Console.WriteLine($"Sent {last.Sent:N0}, handled {last.Handled:N0} over {last.Elapsed:hh\\:mm\\:ss}.");
        Console.WriteLine($"Live heap {Mib(heapBefore)} -> {Mib(heapAfter)} ({heapGrowth:N2}x). " +
                          $"Activations peaked at {samples.Max(s => s.LiveActors):N0}, ended at {last.LiveActors:N0}.");
        Console.WriteLine($"Throughput {opening:N0}/s at the start, {closing:N0}/s at the end.");
        Console.WriteLine();

        if (problems.Count == 0)
        {
            Console.WriteLine("PASS - nothing drifted.");
            return 0;
        }

        Console.WriteLine("FAIL");
        foreach (var problem in problems) Console.WriteLine($"  - {problem}");
        return 1;
    }

    private static string Mib(long bytes) => $"{bytes / 1024 / 1024:N0} MiB";
}
