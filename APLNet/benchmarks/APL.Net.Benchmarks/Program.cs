// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using Perfolizer.Horology;

namespace AplNet.Benchmarks;

public static class Program
{
    // dotnet run -c Release --project benchmarks/APL.Net.Benchmarks -- --filter '*'
    // dotnet run -c Release --project benchmarks/APL.Net.Benchmarks -- --filter '*Simd*'
    public static void Main(string[] args) =>
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, AplBenchmarkConfig.Create());
}

/// <summary>
/// The default configuration (columns, loggers, exporters) plus: .NET 10, the memory diagnoser (the
/// zero-allocation claims are checked here, not asserted), GitHub markdown for
/// docs/benchmark-results.md, and a bounded iteration count so the full matrix finishes in
/// reasonable time on a laptop.
/// </summary>
public static class AplBenchmarkConfig
{
    public static IConfig Create() =>
        ManualConfig.Create(DefaultConfig.Instance)
            .AddJob(Job.Default
                .WithRuntime(BenchmarkDotNet.Environments.CoreRuntime.Core10_0)
                .WithWarmupCount(5)
                .WithMinIterationCount(10)
                .WithMaxIterationCount(30)
                .AsDefault())
            .AddDiagnoser(MemoryDiagnoser.Default)
            .AddExporter(MarkdownExporter.GitHub)
            .HideColumns(Column.Error, Column.RatioSD, Column.AllocRatio)
            .WithSummaryStyle(SummaryStyle.Default.WithRatioStyle(RatioStyle.Trend).WithTimeUnit(TimeUnit.Microsecond))
            .WithOrderer(new BenchmarkDotNet.Order.DefaultOrderer(BenchmarkDotNet.Order.SummaryOrderPolicy.Declared));
}
