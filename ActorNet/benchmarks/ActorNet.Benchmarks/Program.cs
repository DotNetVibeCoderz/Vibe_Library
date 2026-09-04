// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.
//
// Run everything:
//   dotnet run -c Release --project benchmarks/ActorNet.Benchmarks
//
// Run one class:
//   dotnet run -c Release --project benchmarks/ActorNet.Benchmarks -- --filter '*RoutingBenchmarks*'

using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

/// <summary>Anchors the assembly for the switcher above.</summary>
public partial class Program;
