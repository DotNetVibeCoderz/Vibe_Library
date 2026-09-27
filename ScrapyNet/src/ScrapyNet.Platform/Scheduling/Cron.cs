using System.Globalization;

namespace ScrapyNet.Platform;

/// <summary>
/// A standard five-field cron expression — minute, hour, day of month, month, day of week — with
/// ranges (<c>1-5</c>), lists (<c>1,15</c>), steps (<c>*/10</c>, <c>0-30/5</c>), names (<c>MON</c>,
/// <c>JAN</c>) and the macros <c>@hourly</c>, <c>@daily</c>, <c>@weekly</c>, <c>@monthly</c>, <c>@yearly</c>.
/// As in Vixie cron, when both day fields are restricted a day matches if either does.
/// </summary>
public sealed class CronExpression
{
    private readonly bool[] _minutes = new bool[60];
    private readonly bool[] _hours = new bool[24];
    private readonly bool[] _days = new bool[32];
    private readonly bool[] _months = new bool[13];
    private readonly bool[] _weekdays = new bool[8];
    private readonly bool _dayRestricted;
    private readonly bool _weekdayRestricted;

    private static readonly string[] MonthNames = ["JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"];
    private static readonly string[] DayNames = ["SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT"];

    public CronExpression(string expression)
    {
        Expression = expression.Trim();
        var text = Expression.ToLowerInvariant() switch
        {
            "@hourly" => "0 * * * *",
            "@daily" or "@midnight" => "0 0 * * *",
            "@weekly" => "0 0 * * 0",
            "@monthly" => "0 0 1 * *",
            "@yearly" or "@annually" => "0 0 1 1 *",
            _ => Expression,
        };
        var fields = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 5) throw new FormatException($"Cron expression '{expression}' must have 5 fields (minute hour day month weekday).");
        Parse(fields[0], _minutes, 0, 59, null);
        Parse(fields[1], _hours, 0, 23, null);
        _dayRestricted = Parse(fields[2], _days, 1, 31, null);
        Parse(fields[3], _months, 1, 12, MonthNames);
        _weekdayRestricted = Parse(fields[4], _weekdays, 0, 7, DayNames);
        if (_weekdays[7]) _weekdays[0] = true; // 7 is Sunday too
    }

    public string Expression { get; }

    public static bool TryParse(string expression, out CronExpression? cron)
    {
        try
        {
            cron = new CronExpression(expression);
            return true;
        }
        catch (FormatException)
        {
            cron = null;
            return false;
        }
    }

    /// <summary>The first occurrence strictly after <paramref name="after"/>, in <paramref name="zone"/> (default UTC).</summary>
    public DateTimeOffset? GetNextOccurrence(DateTimeOffset after, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Utc;
        var local = TimeZoneInfo.ConvertTime(after, zone).DateTime;
        var start = new DateTime(local.Year, local.Month, local.Day, local.Hour, local.Minute, 0).AddMinutes(1);
        for (var day = start.Date; day < start.Date.AddYears(5); day = day.AddDays(1))
        {
            if (!_months[day.Month] || !DayMatches(day)) continue;
            for (var h = day == start.Date ? start.Hour : 0; h < 24; h++)
            {
                if (!_hours[h]) continue;
                for (var m = day == start.Date && h == start.Hour ? start.Minute : 0; m < 60; m++)
                {
                    if (!_minutes[m]) continue;
                    var candidate = day.AddHours(h).AddMinutes(m);
                    if (zone.IsInvalidTime(candidate)) continue;
                    return new DateTimeOffset(candidate, zone.GetUtcOffset(candidate));
                }
            }
        }
        return null;
    }

    /// <summary>The next <paramref name="count"/> occurrences.</summary>
    public IEnumerable<DateTimeOffset> GetOccurrences(DateTimeOffset after, int count, TimeZoneInfo? zone = null)
    {
        var current = after;
        for (var i = 0; i < count; i++)
        {
            var next = GetNextOccurrence(current, zone);
            if (next is null) yield break;
            yield return next.Value;
            current = next.Value;
        }
    }

    private bool DayMatches(DateTime day)
    {
        var dom = _days[day.Day];
        var dow = _weekdays[(int)day.DayOfWeek];
        if (_dayRestricted && _weekdayRestricted) return dom || dow;
        if (_dayRestricted) return dom;
        if (_weekdayRestricted) return dow;
        return true;
    }

    /// <summary>Fills <paramref name="target"/>; returns true if the field is restricted (not <c>*</c>).</summary>
    private static bool Parse(string field, bool[] target, int min, int max, string[]? names)
    {
        var restricted = field != "*" && field != "?";
        foreach (var part in field.Split(','))
        {
            var step = 1;
            var range = part;
            var slash = part.IndexOf('/');
            if (slash >= 0)
            {
                step = int.Parse(part[(slash + 1)..], CultureInfo.InvariantCulture);
                if (step <= 0) throw new FormatException($"Invalid step in '{field}'.");
                range = part[..slash];
            }
            int lo, hi;
            if (range is "*" or "?")
            {
                lo = min;
                hi = max;
            }
            else if (range.Contains('-'))
            {
                var bounds = range.Split('-', 2);
                lo = Value(bounds[0], names, min);
                hi = Value(bounds[1], names, min);
            }
            else
            {
                lo = Value(range, names, min);
                hi = slash >= 0 ? max : lo;
            }
            if (lo < min || hi > max || lo > hi) throw new FormatException($"Value out of range in '{field}' ({min}-{max}).");
            for (var v = lo; v <= hi; v += step) target[v] = true;
        }
        return restricted;
    }

    private static int Value(string token, string[]? names, int min)
    {
        if (names is not null)
        {
            var idx = Array.IndexOf(names, token.ToUpperInvariant());
            if (idx >= 0) return idx + (min == 1 ? 1 : 0);
        }
        return int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : throw new FormatException($"Invalid cron value '{token}'.");
    }

    public override string ToString() => Expression;
}

/// <summary>Friendly constructors for common schedules.</summary>
public static class Schedules
{
    public static string EveryMinutes(int minutes) => minutes is > 0 and < 60 ? $"*/{minutes} * * * *" : throw new ArgumentOutOfRangeException(nameof(minutes));

    public static string Hourly(int minute = 0) => $"{minute} * * * *";

    public static string Daily(int hour, int minute = 0) => $"{minute} {hour} * * *";

    public static string Weekly(DayOfWeek day, int hour, int minute = 0) => $"{minute} {hour} * * {(int)day}";

    public static string Monthly(int dayOfMonth, int hour, int minute = 0) => $"{minute} {hour} {dayOfMonth} * *";
}
