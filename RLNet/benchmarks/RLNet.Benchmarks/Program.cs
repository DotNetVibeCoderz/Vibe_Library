// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

namespace RLNet.Benchmarks;

/// <summary>
/// Entry point for the benchmark suite.
/// </summary>
/// <remarks>
/// <para>
/// Run everything with <c>dotnet run -c Release --project benchmarks/RLNet.Benchmarks</c>, or a
/// single suite with <c>-- --filter *AgentBenchmarks*</c>.
/// </para>
/// <para>
/// <c>--filter *</c> is the default when no arguments are given, so a bare run does the whole
/// suite instead of printing a menu and waiting — which matters because this is also what CI runs.
/// </para>
/// </remarks>
public static class Program
{
    public static void Main(string[] args)
    {
        // A quick way to see what ILGPU can find, without starting a benchmark run.
        if (args.Length == 1 && args[0] == "--devices")
        {
            var devices = Gpu.GpuComputeBackend.AvailableDevices();
            Console.WriteLine(devices.Count == 0
                ? "No GPU accelerator found; RLNet will use the CPU backend."
                : "Accelerators:" + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", devices));
            return;
        }

        Run(args);
    }

    private static void Run(string[] args) =>
        BenchmarkSwitcher
            .FromAssembly(typeof(Program).Assembly)
            .Run(args.Length > 0 ? args : ["--filter", "*"], DefaultConfig.Instance.WithOptions(ConfigOptions.JoinSummary));
}
