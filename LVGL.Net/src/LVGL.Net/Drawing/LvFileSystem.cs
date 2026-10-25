namespace Lvgl.Drawing;

/// <summary>Maps host paths onto LVGL's drive-letter file system.</summary>
/// <remarks>
/// The native library registers the C library's <c>fopen</c> as LVGL drive <c>A:</c>. Every path
/// handed to LVGL must carry that prefix: without it LVGL reads the first character as the drive,
/// so a Windows path such as <c>C:\img.png</c> would be looked up on a drive "C" that does not exist.
/// </remarks>
public static class LvFileSystem
{
    /// <summary>The drive letter configured in <c>native/lv_conf.h</c> (<c>LV_FS_STDIO_LETTER</c>).</summary>
    public const char DriveLetter = 'A';

    /// <summary>Converts a host path into the <c>"A:/full/path"</c> form LVGL expects.</summary>
    public static string ToLvglPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // Forward slashes work for fopen on every platform, and keep LVGL's own path helpers
        // (extension detection for the decoders) from tripping over backslashes.
        var full = Path.GetFullPath(path).Replace('\\', '/');
        return $"{DriveLetter}:{full}";
    }
}
