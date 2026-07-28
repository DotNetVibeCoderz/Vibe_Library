using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Lvgl.Interop;

/// <summary>
/// Locates the <c>lvglnet</c> shared library.
/// </summary>
/// <remarks>
/// The default probing logic only finds the library when it sits next to the entry assembly or
/// arrives through a NuGet <c>runtimes/</c> folder. During development it is produced by
/// <c>native/build.sh</c> into the repository's <c>runtimes/&lt;rid&gt;/native</c>, which may be
/// several directories above the output folder, so an explicit resolver is installed.
/// </remarks>
public static class LvglNativeLibrary
{
    private static int _installed;

    /// <summary>
    /// Environment variable holding an explicit path to the shared library or to the directory
    /// containing it. Checked before every other probe path.
    /// </summary>
    public const string PathVariable = "LVGLNET_NATIVE_PATH";

    /// <summary>Paths that were probed during the last failed load, for diagnostics.</summary>
    public static IReadOnlyList<string> ProbedPaths => _probed;

    private static string[] _probed = [];

    internal static void EnsureResolverInstalled()
    {
        if (Interlocked.Exchange(ref _installed, 1) != 0) return;
        NativeLibrary.SetDllImportResolver(typeof(LvglNativeLibrary).Assembly, Resolve);
    }

    /// <summary>
    /// Loads the native library eagerly so a missing or mismatched build fails with a clear
    /// message instead of a <see cref="DllNotFoundException"/> from an arbitrary call site.
    /// </summary>
    public static void Preload()
    {
        EnsureResolverInstalled();
        var handle = Resolve(LvglNative.Library, typeof(LvglNativeLibrary).Assembly, null);
        if (handle == 0) throw new LvglNativeNotFoundException(BuildDiagnostic());
    }

    private static nint Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != LvglNative.Library) return 0;

        var explicitPath = Environment.GetEnvironmentVariable(PathVariable);
        var fileName = NativeFileName(libraryName);

        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            candidates.Add(Directory.Exists(explicitPath) ? Path.Combine(explicitPath, fileName) : explicitPath);
        }

        candidates.AddRange(EnumerateProbePaths(fileName));

        foreach (var path in candidates)
        {
            if (NativeLibrary.TryLoad(path, out var handle)) return handle;
        }

        // Last resort: let the OS loader search its own paths (LD_LIBRARY_PATH, /usr/local/lib, ...).
        candidates.Add($"<system loader>/{libraryName}");
        if (NativeLibrary.TryLoad(libraryName, assembly, searchPath, out var fallback)) return fallback;

        _probed = [.. candidates];
        return 0;
    }

    /// <summary>
    /// The directories LVGL.Net searches for a native dependency, in order: the application
    /// directory and each of its ancestors, both directly and under
    /// <c>runtimes/&lt;rid&gt;/native/</c>.
    /// </summary>
    /// <remarks>
    /// Walking up from the output directory is what lets <c>dotnet run --project samples/...</c>
    /// find a library staged at the repository root without a copy step. Shared with the backend
    /// assemblies so every native dependency - not just <c>lvglnet</c> - is deployed the same way.
    /// </remarks>
    /// <param name="fileName">Platform-specific file name, e.g. <c>SDL2.dll</c> or <c>libSDL2.so</c>.</param>
    public static IEnumerable<string> EnumerateProbePaths(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var rid = RuntimeIdentifier();
        var dir = AppContext.BaseDirectory;

        for (var i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
        {
            yield return Path.Combine(dir, fileName);
            yield return Path.Combine(dir, "runtimes", rid, "native", fileName);
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
    }

    /// <summary>
    /// Tries to load a native library by file name from the LVGL.Net probe paths, then from the
    /// OS loader's own search path.
    /// </summary>
    public static bool TryLoadFromProbePaths(string fileName, out nint handle)
    {
        foreach (var path in EnumerateProbePaths(fileName))
        {
            if (NativeLibrary.TryLoad(path, out handle)) return true;
        }

        return NativeLibrary.TryLoad(fileName, out handle);
    }

    /// <summary>Maps a bare library name onto the platform's file naming convention.</summary>
    public static string NativeFileName(string libraryName)
    {
        if (OperatingSystem.IsWindows()) return libraryName + ".dll";
        if (OperatingSystem.IsMacOS()) return "lib" + libraryName + ".dylib";
        return "lib" + libraryName + ".so";
    }

    private static string RuntimeIdentifier()
    {
        var os = OperatingSystem.IsWindows() ? "win"
               : OperatingSystem.IsMacOS() ? "osx"
               : "linux";

        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            var other => other.ToString().ToLowerInvariant(),
        };

        return $"{os}-{arch}";
    }

    private static string BuildDiagnostic()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"The native library '{NativeFileName(LvglNative.Library)}' could not be loaded.");
        sb.AppendLine();
        sb.AppendLine("Build it first:");
        sb.AppendLine(OperatingSystem.IsWindows()
            ? "    ./native/build.ps1"
            : "    ./native/build.sh          (add --pi4 on a Raspberry Pi 4)");
        sb.AppendLine();
        sb.AppendLine($"Or point {PathVariable} at an existing build.");
        sb.AppendLine();
        sb.AppendLine("Probed:");
        foreach (var path in _probed) sb.AppendLine("    " + path);
        return sb.ToString();
    }
}

/// <summary>Thrown when the <c>lvglnet</c> shared library cannot be found or loaded.</summary>
public sealed class LvglNativeNotFoundException(string message) : DllNotFoundException(message);
