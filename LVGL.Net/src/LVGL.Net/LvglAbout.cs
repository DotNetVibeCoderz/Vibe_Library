using System.Reflection;

namespace Lvgl;

/// <summary>
/// Provenance of this library, for about screens, log banners and `--version` output.
/// </summary>
/// <remarks>
/// The values come from assembly metadata written by <c>Directory.Build.props</c>, so the credit
/// travels with the binary instead of living only in the README - and there is one place to
/// change it.
/// </remarks>
public static class LvglAbout
{
    private static readonly Assembly Assembly = typeof(LvglAbout).Assembly;

    /// <summary>Who ported LVGL to .NET.</summary>
    public static string PortedBy { get; } = Metadata("PortedBy") ?? "Kang Fadhil";

    /// <summary>The organisation behind the port.</summary>
    public static string Organization { get; } = Metadata("PortedByOrganization") ?? "Gravicode Studios";

    /// <summary>Version of the LVGL.Net assemblies.</summary>
    public static string Version { get; } =
        Assembly.GetName().Version?.ToString(3) ?? "0.1.0";

    /// <summary>Single-line credit, in Bahasa Indonesia as written by the author.</summary>
    public static string Credit => $"Di port oleh {PortedBy} dari {Organization}";

    /// <summary>Single-line credit in English.</summary>
    public static string CreditEnglish => $"Ported by {PortedBy} of {Organization}";

    /// <summary>Multi-line banner suitable for a console header.</summary>
    public static string Banner =>
        $"LVGL.Net {Version}  |  {Credit}";

    private static string? Metadata(string key) => Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == key)?.Value;
}
