using System.ComponentModel;
using System.Globalization;
using Microsoft.SemanticKernel;

namespace Lvgl.Assistant.Plugins;

/// <summary>
/// Dates and times.
/// </summary>
/// <remarks>
/// A model's idea of "today" is its training cutoff, so anything date-dependent has to come from
/// here. Everything is returned in ISO-8601 with an explicit offset, which removes the ambiguity
/// that makes date arithmetic go wrong.
/// </remarks>
public sealed class TimePlugin
{
    [KernelFunction("now")]
    [Description("The current local date and time, in ISO-8601 with the UTC offset.")]
    public string Now() => DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture);

    [KernelFunction("utc_now")]
    [Description("The current UTC date and time, in ISO-8601.")]
    public string UtcNow() => DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);

    [KernelFunction("today")]
    [Description("Today's local date as yyyy-MM-dd, plus the day of the week.")]
    public string Today() =>
        $"{DateTime.Now:yyyy-MM-dd} ({DateTime.Now.DayOfWeek})";

    [KernelFunction("time_zone")]
    [Description("The machine's time zone and its current UTC offset.")]
    public string TimeZone() =>
        $"{TimeZoneInfo.Local.Id} (UTC{DateTimeOffset.Now.Offset:hh\\:mm})";

    [KernelFunction("add_days")]
    [Description("Adds a number of days to a date and returns the result as yyyy-MM-dd.")]
    public string AddDays(
        [Description("Start date, yyyy-MM-dd. Empty means today.")] string date,
        [Description("Days to add; negative subtracts.")] int days)
    {
        var start = ParseDate(date);
        return start.AddDays(days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    [KernelFunction("days_between")]
    [Description("Whole days from the first date to the second. Negative when the second is earlier.")]
    public string DaysBetween(
        [Description("First date, yyyy-MM-dd.")] string from,
        [Description("Second date, yyyy-MM-dd.")] string to)
    {
        var start = ParseDate(from);
        var end = ParseDate(to);
        return ((int)(end - start).TotalDays).ToString(CultureInfo.InvariantCulture);
    }

    [KernelFunction("format_duration")]
    [Description("Turns a number of seconds into a human-readable duration.")]
    public string FormatDuration([Description("Seconds.")] double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds)) return "not a valid duration";

        var span = TimeSpan.FromSeconds(Math.Abs(seconds));
        var sign = seconds < 0 ? "-" : string.Empty;

        return span.TotalDays >= 1
            ? $"{sign}{(int)span.TotalDays}d {span.Hours}h {span.Minutes}m"
            : span.TotalHours >= 1
                ? $"{sign}{(int)span.TotalHours}h {span.Minutes}m {span.Seconds}s"
                : $"{sign}{span.Minutes}m {span.Seconds}s";
    }

    private static DateTime ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return DateTime.Today;

        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.Date
            : throw new ArgumentException($"'{text}' is not a date I can read. Use yyyy-MM-dd.");
    }
}
