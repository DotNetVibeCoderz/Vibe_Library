// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Runtime.InteropServices;
using AplNet.Simd;

namespace AplNet.Gallery.Infrastructure;

/// <summary>What the numbers on screen were measured on.</summary>
public static class MachineInfo
{
    public static int Threads => Environment.ProcessorCount;

    public static string Cpu { get; } = ReadCpuName();

    public static string Simd => SimdCapabilities.Describe();

    public static string Runtime => $"{RuntimeInformation.FrameworkDescription} · {RuntimeInformation.ProcessArchitecture}";

    public static string Os => RuntimeInformation.OSDescription;

    private static string ReadCpuName()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                string? name = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                return (key?.GetValue("ProcessorNameString") as string)?.Trim() ?? name ?? "Unknown CPU";
            }

            if (OperatingSystem.IsLinux() && File.Exists("/proc/cpuinfo"))
            {
                string? line = File.ReadLines("/proc/cpuinfo").FirstOrDefault(l => l.StartsWith("model name", StringComparison.Ordinal));
                if (line is not null)
                    return line[(line.IndexOf(':') + 1)..].Trim();
            }
        }
        catch (Exception)
        {
            // Purely informational; fall through to the architecture.
        }

        return RuntimeInformation.ProcessArchitecture.ToString();
    }
}
