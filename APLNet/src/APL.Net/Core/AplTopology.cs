// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using System.Runtime.InteropServices;

namespace AplNet.Core;

/// <summary>
/// The machine's NUMA nodes and logical processors, as <see cref="SpinningWorkers"/> sees them when
/// it places its threads.
/// </summary>
/// <remarks>
/// Read once, on first use. On Windows the topology comes from the NUMA API (processors are numbered
/// <c>group * 64 + index</c>); on Linux from <c>/sys/devices/system/node</c>, limited to the
/// processors this process may run on. Elsewhere, or when the information is unavailable, the
/// machine is reported as one node holding processors <c>0 .. ProcessorCount - 1</c>. This is the
/// only place APL.Net calls native code, and only when thread affinity is asked for.
/// </remarks>
public static partial class AplTopology
{
    private static readonly Lazy<int[][]> s_nodes = new(Detect);

    /// <summary>The number of NUMA nodes. 1 on most desktops and laptops.</summary>
    public static int NumaNodeCount => s_nodes.Value.Length;

    /// <summary>The logical processors of NUMA node <paramref name="node"/>, in ascending order.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="node"/> is not a node index.</exception>
    public static IReadOnlyList<int> GetNodeProcessors(int node)
    {
        int[][] nodes = s_nodes.Value;
        ArgumentOutOfRangeException.ThrowIfNegative(node);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(node, nodes.Length);
        return Array.AsReadOnly(nodes[node]);
    }

    /// <summary>Whether this platform supports pinning threads (Windows and Linux).</summary>
    public static bool IsAffinitySupported => OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    /// <summary>A one-line summary, e.g. <c>"2 NUMA nodes: [0-15] [16-31]"</c>.</summary>
    public static string Describe()
    {
        int[][] nodes = s_nodes.Value;
        string list = string.Join(" ", nodes.Select(n => "[" + FormatCpuList(n) + "]"));
        return $"{nodes.Length} NUMA node{(nodes.Length == 1 ? "" : "s")}: {list}";
    }

    /// <summary>The processor each of <paramref name="workers"/> spinning workers is placed on: node by node, skipping the first processor, which is left to the caller.</summary>
    internal static int[] GetWorkerPlacement(int workers) => GetWorkerPlacement(s_nodes.Value, workers);

    internal static int[] GetWorkerPlacement(int[][] nodes, int workers)
    {
        int[] all = nodes.SelectMany(n => n).ToArray();
        if (all.Length == 0)
            return [];

        var placement = new int[workers];
        for (int k = 0; k < workers; k++)
            placement[k] = all[(k + 1) % all.Length];
        return placement;
    }

    internal static int NodeOf(int[][] nodes, int processor)
    {
        for (int n = 0; n < nodes.Length; n++)
        {
            if (Array.IndexOf(nodes[n], processor) >= 0)
                return n;
        }

        return -1;
    }

    /// <summary>Pins the calling thread to <paramref name="processor"/>, or to its whole node. False if the platform or the call refused.</summary>
    internal static bool TryPinCurrentThread(int processor, WorkerAffinity affinity)
    {
        int[][] nodes = s_nodes.Value;
        int[] targets = affinity switch
        {
            WorkerAffinity.Core => [processor],
            WorkerAffinity.NumaNode when NodeOf(nodes, processor) is int node and >= 0 => nodes[node],
            _ => [],
        };
        if (targets.Length == 0)
            return false;

        try
        {
            if (OperatingSystem.IsWindows())
                return Native.PinWindows(targets);
            if (OperatingSystem.IsLinux())
                return Native.PinLinux(targets);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }

        return false;
    }

    // ---------------------------------------------------------------- detection

    private static int[][] Detect()
    {
        int[][]? nodes = null;
        try
        {
            if (OperatingSystem.IsWindows())
                nodes = Native.DetectWindows();
            else if (OperatingSystem.IsLinux())
                nodes = DetectLinux();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or IOException or UnauthorizedAccessException)
        {
        }

        nodes = nodes?.Where(n => n.Length > 0).ToArray();
        return nodes is { Length: > 0 } ? nodes : [Enumerable.Range(0, Environment.ProcessorCount).ToArray()];
    }

    private static int[][]? DetectLinux()
    {
        HashSet<int>? allowed = Native.GetLinuxAllowedProcessors();
        const string root = "/sys/devices/system/node";
        var nodes = new List<(int Id, int[] Cpus)>();
        if (Directory.Exists(root))
        {
            foreach (string dir in Directory.EnumerateDirectories(root, "node*"))
            {
                string name = Path.GetFileName(dir);
                string list = Path.Combine(dir, "cpulist");
                if (!int.TryParse(name.AsSpan(4), NumberStyles.None, CultureInfo.InvariantCulture, out int id) || !File.Exists(list))
                    continue;

                int[] cpus = ParseCpuList(File.ReadAllText(list));
                if (allowed is not null)
                    cpus = cpus.Where(allowed.Contains).ToArray();
                nodes.Add((id, cpus));
            }
        }

        if (nodes.Count == 0)
            return allowed is { Count: > 0 } ? [allowed.Order().ToArray()] : null;

        return nodes.OrderBy(n => n.Id).Select(n => n.Cpus).ToArray();
    }

    /// <summary>Parses the kernel's list format, e.g. <c>"0-3,8,10-11"</c>.</summary>
    internal static int[] ParseCpuList(string text)
    {
        var cpus = new List<int>();
        foreach (string part in text.Trim().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int dash = part.IndexOf('-');
            if (dash < 0)
            {
                cpus.Add(int.Parse(part, CultureInfo.InvariantCulture));
                continue;
            }

            int first = int.Parse(part.AsSpan(0, dash), CultureInfo.InvariantCulture);
            int last = int.Parse(part.AsSpan(dash + 1), CultureInfo.InvariantCulture);
            for (int c = first; c <= last; c++)
                cpus.Add(c);
        }

        return cpus.Distinct().Order().ToArray();
    }

    /// <summary>The inverse of <see cref="ParseCpuList"/>: <c>[0, 1, 2, 3, 8]</c> becomes <c>"0-3,8"</c>.</summary>
    internal static string FormatCpuList(int[] cpus)
    {
        var parts = new List<string>();
        for (int i = 0; i < cpus.Length;)
        {
            int j = i;
            while (j + 1 < cpus.Length && cpus[j + 1] == cpus[j] + 1)
                j++;
            parts.Add(i == j ? cpus[i].ToString(CultureInfo.InvariantCulture) : $"{cpus[i]}-{cpus[j]}");
            i = j + 1;
        }

        return string.Join(",", parts);
    }

    // ---------------------------------------------------------------- native

    private static unsafe partial class Native
    {
        private const int LinuxCpuSetBytes = 128; // 1024 processors, glibc's CPU_SETSIZE

        [StructLayout(LayoutKind.Sequential)]
        private struct GroupAffinity
        {
            public nuint Mask;
            public ushort Group;
            public ushort Reserved0, Reserved1, Reserved2;
        }

        public static int[][]? DetectWindows()
        {
            if (!GetNumaHighestNodeNumber(out uint highest))
                return null;

            var nodes = new int[highest + 1][];
            for (uint n = 0; n <= highest; n++)
            {
                if (!GetNumaNodeProcessorMaskEx((ushort)n, out GroupAffinity affinity))
                {
                    nodes[n] = [];
                    continue;
                }

                var cpus = new List<int>();
                for (int bit = 0; bit < sizeof(nuint) * 8; bit++)
                {
                    if ((affinity.Mask & ((nuint)1 << bit)) != 0)
                        cpus.Add(affinity.Group * 64 + bit);
                }

                nodes[n] = [.. cpus];
            }

            return nodes;
        }

        public static bool PinWindows(int[] processors)
        {
            // A thread's affinity lives in one processor group; a node never spans groups.
            int group = processors[0] / 64;
            nuint mask = 0;
            foreach (int p in processors)
            {
                if (p / 64 == group)
                    mask |= (nuint)1 << (p % 64);
            }

            var affinity = new GroupAffinity { Mask = mask, Group = (ushort)group };
            return SetThreadGroupAffinity(GetCurrentThread(), &affinity, null);
        }

        public static HashSet<int>? GetLinuxAllowedProcessors()
        {
            byte* set = stackalloc byte[LinuxCpuSetBytes];
            new Span<byte>(set, LinuxCpuSetBytes).Clear();
            if (sched_getaffinity(0, LinuxCpuSetBytes, set) != 0)
                return null;

            var allowed = new HashSet<int>();
            for (int cpu = 0; cpu < LinuxCpuSetBytes * 8; cpu++)
            {
                if ((set[cpu / 8] & (1 << (cpu % 8))) != 0)
                    allowed.Add(cpu);
            }

            return allowed;
        }

        public static bool PinLinux(int[] processors)
        {
            byte* set = stackalloc byte[LinuxCpuSetBytes];
            new Span<byte>(set, LinuxCpuSetBytes).Clear();
            foreach (int p in processors)
            {
                if ((uint)p < LinuxCpuSetBytes * 8)
                    set[p / 8] |= (byte)(1 << (p % 8));
            }

            // pid 0 is the calling thread.
            return sched_setaffinity(0, LinuxCpuSetBytes, set) == 0;
        }

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetNumaHighestNodeNumber(out uint highestNodeNumber);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetNumaNodeProcessorMaskEx(ushort node, out GroupAffinity processorMask);

        [LibraryImport("kernel32.dll")]
        private static partial nint GetCurrentThread();

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetThreadGroupAffinity(nint thread, GroupAffinity* groupAffinity, GroupAffinity* previousGroupAffinity);

        [LibraryImport("libc", SetLastError = true)]
        private static partial int sched_getaffinity(int pid, nuint cpuSetSize, byte* mask);

        [LibraryImport("libc", SetLastError = true)]
        private static partial int sched_setaffinity(int pid, nuint cpuSetSize, byte* mask);
    }
}
