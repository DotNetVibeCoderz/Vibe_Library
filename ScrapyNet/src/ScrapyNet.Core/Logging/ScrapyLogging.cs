using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace ScrapyNet;

/// <summary>One formatted log line, as delivered to <see cref="ScrapyLoggerFactory.LogWritten"/> subscribers.</summary>
public sealed record LogEntry(DateTimeOffset Timestamp, LogLevel Level, string Category, string Message, Exception? Exception)
{
    /// <summary>Scrapy's level names: DEBUG, INFO, WARNING, ERROR, CRITICAL.</summary>
    public string LevelName => ScrapyLoggerFactory.LevelName(Level);

    public override string ToString() =>
        $"{Timestamp.LocalDateTime:yyyy-MM-dd HH:mm:ss} [{Category}] {LevelName}: {Message}" + (Exception is null ? "" : Environment.NewLine + Exception);
}

/// <summary>
/// A small <see cref="ILoggerFactory"/> that formats lines the way Scrapy does
/// (<c>2026-01-01 12:00:00 [scrapynet.core.engine] INFO: Spider opened</c>), writes them to the console
/// and/or <c>LOG_FILE</c>, counts them into stats (<c>log_count/INFO</c>), and forwards to any extra
/// <see cref="ILoggerProvider"/>s the host supplies. It exists so the core package needs only
/// Microsoft.Extensions.Logging.Abstractions.
/// </summary>
public sealed class ScrapyLoggerFactory : ILoggerFactory
{
    private readonly ConcurrentDictionary<string, ScrapyLogger> _loggers = new(StringComparer.Ordinal);
    private readonly List<ILoggerProvider> _providers = [];
    private readonly object _writeGate = new();
    private StreamWriter? _file;

    public ScrapyLoggerFactory(LogLevel minimumLevel = LogLevel.Information, bool console = true, string? logFile = null, bool appendFile = true)
    {
        MinimumLevel = minimumLevel;
        WriteToConsole = console;
        if (!string.IsNullOrEmpty(logFile))
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(logFile));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            _file = new StreamWriter(new FileStream(logFile, appendFile ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
        }
    }

    /// <summary>Builds a factory from <c>LOG_ENABLED</c>, <c>LOG_LEVEL</c>, <c>LOG_FILE</c>, <c>LOG_FILE_APPEND</c>, <c>LOG_STDOUT</c>.</summary>
    public static ScrapyLoggerFactory FromSettings(Settings settings)
    {
        var enabled = settings.GetBool(SettingKeys.LogEnabled, true);
        var level = enabled ? ParseLevel(settings.GetString(SettingKeys.LogLevel) ?? "INFO") : LogLevel.None;
        var file = settings.GetString(SettingKeys.LogFile);
        return new ScrapyLoggerFactory(level, console: enabled && string.IsNullOrEmpty(file), logFile: enabled ? file : null,
            appendFile: settings.GetBool(SettingKeys.LogFileAppend, true))
        {
            ShortNames = settings.GetBool(SettingKeys.LogShortNames),
        };
    }

    public LogLevel MinimumLevel { get; set; }

    public bool WriteToConsole { get; set; }

    /// <summary>Show only the last segment of category names.</summary>
    public bool ShortNames { get; set; }

    /// <summary>Stats collector that receives <c>log_count/LEVEL</c> counters.</summary>
    public IStatsCollector? Stats { get; set; }

    /// <summary>Raised for every line at or above <see cref="MinimumLevel"/> (used by the gallery's live log).</summary>
    public event Action<LogEntry>? LogWritten;

    public void AddProvider(ILoggerProvider provider)
    {
        lock (_providers) _providers.Add(provider);
    }

    public ILogger CreateLogger(string categoryName) => _loggers.GetOrAdd(categoryName, name => new ScrapyLogger(this, name));

    public void Dispose()
    {
        lock (_writeGate)
        {
            _file?.Dispose();
            _file = null;
        }
        lock (_providers)
        {
            foreach (var p in _providers) p.Dispose();
            _providers.Clear();
        }
    }

    public static LogLevel ParseLevel(string level) => level.Trim().ToUpperInvariant() switch
    {
        "DEBUG" or "TRACE" => LogLevel.Debug,
        "INFO" or "INFORMATION" => LogLevel.Information,
        "WARNING" or "WARN" => LogLevel.Warning,
        "ERROR" => LogLevel.Error,
        "CRITICAL" or "FATAL" => LogLevel.Critical,
        "NONE" or "OFF" => LogLevel.None,
        _ => LogLevel.Information,
    };

    public static string LevelName(LogLevel level) => level switch
    {
        LogLevel.Trace or LogLevel.Debug => "DEBUG",
        LogLevel.Information => "INFO",
        LogLevel.Warning => "WARNING",
        LogLevel.Error => "ERROR",
        LogLevel.Critical => "CRITICAL",
        _ => "NONE",
    };

    internal void Write(LogEntry entry)
    {
        Stats?.Inc("log_count/" + entry.LevelName);
        var category = ShortNames ? entry.Category[(entry.Category.LastIndexOf('.') + 1)..] : entry.Category;
        var line = entry with { Category = category };
        var text = line.ToString();
        lock (_writeGate)
        {
            if (WriteToConsole)
            {
                var color = entry.Level switch
                {
                    LogLevel.Warning => ConsoleColor.Yellow,
                    LogLevel.Error or LogLevel.Critical => ConsoleColor.Red,
                    LogLevel.Debug or LogLevel.Trace => ConsoleColor.DarkGray,
                    _ => (ConsoleColor?)null,
                };
                if (color is { } c && !Console.IsErrorRedirected)
                {
                    var previous = Console.ForegroundColor;
                    Console.ForegroundColor = c;
                    Console.Error.WriteLine(text);
                    Console.ForegroundColor = previous;
                }
                else
                {
                    Console.Error.WriteLine(text);
                }
            }
            _file?.WriteLine(text);
        }
        LogWritten?.Invoke(line);
    }

    internal IReadOnlyList<ILogger> ExternalLoggers(string category)
    {
        lock (_providers)
        {
            return _providers.Count == 0 ? [] : _providers.Select(p => p.CreateLogger(category)).ToList();
        }
    }

    private sealed class ScrapyLogger(ScrapyLoggerFactory factory, string category) : ILogger
    {
        private IReadOnlyList<ILogger>? _external;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= factory.MinimumLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _external ??= factory.ExternalLoggers(category);
            foreach (var ext in _external)
                if (ext.IsEnabled(logLevel)) ext.Log(logLevel, eventId, state, exception, formatter);

            if (logLevel == LogLevel.None || logLevel < factory.MinimumLevel) return;
            var message = formatter(state, exception);
            factory.Write(new LogEntry(DateTimeOffset.Now, logLevel, category, message, exception));
        }
    }
}

/// <summary>Formats the recurring engine log messages exactly like Scrapy's <c>LogFormatter</c>.</summary>
public static class LogFormatter
{
    public static string Crawled(Request request, Response response)
    {
        var referer = request.Headers.Get("Referer") ?? "None";
        var flags = response.Flags.Count > 0 ? " [" + string.Join(", ", response.Flags) + "]" : "";
        var requestFlags = request.Flags.Count > 0 ? " [" + string.Join(", ", request.Flags) + "]" : "";
        return string.Create(CultureInfo.InvariantCulture, $"Crawled ({response.Status}) {request}{requestFlags} (referer: {referer}){flags}");
    }

    public static string Scraped(object item, Response? response) =>
        $"Scraped from {(response is null ? "<none>" : response.ToString())}{Environment.NewLine}{ItemAdapter.For(item)}";

    public static string Dropped(object item, Exception exception, Response? response) =>
        $"Dropped: {exception.Message}{Environment.NewLine}{ItemAdapter.For(item)}";
}
