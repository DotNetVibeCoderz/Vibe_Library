// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using RLNet.Agents;

namespace RLNet.Training;

/// <summary>Output format of a <see cref="MetricsLog"/>.</summary>
public enum MetricsFormat
{
    /// <summary>Comma-separated, one header row. Opens directly in a spreadsheet or pandas.</summary>
    Csv,

    /// <summary>One JSON object per line. Appends safely and streams into most log tooling.</summary>
    JsonLines,
}

/// <summary>
/// Writes one row per finished episode — the episode itself plus the agent's diagnostics at that
/// moment — so runs can be plotted and compared outside the console.
/// </summary>
/// <remarks>
/// <para>
/// Attach it through <see cref="TrainingOptions.OnEpisode"/>:
/// </para>
/// <code>
/// using var log = MetricsLog.Create("ppo-cartpole.csv");
/// Trainer.Train(environment, agent, new TrainingOptions { MaxSteps = 100_000, OnEpisode = log.For(agent) });
/// </code>
/// <para>
/// Diagnostics an algorithm does not produce are <see cref="float.NaN"/> on
/// <see cref="AgentMetrics"/>; they are written as an empty CSV field or a JSON <c>null</c>, so a
/// plotting tool sees a gap rather than a spurious zero. Everything is formatted with the
/// invariant culture: a log written on a machine that uses decimal commas still parses everywhere.
/// </para>
/// </remarks>
public sealed class MetricsLog : IDisposable
{
    private static readonly string[] Columns =
    [
        "episode", "steps", "return", "average_return", "total_steps", "terminated",
        "value_loss", "policy_loss", "entropy", "epsilon", "temperature", "td_error", "updates",
    ];

    private readonly TextWriter _writer;
    private readonly bool _ownsWriter;
    private bool _headerWritten;

    /// <summary>Writes to an existing writer, which the log does not dispose.</summary>
    public MetricsLog(TextWriter writer, MetricsFormat format)
        : this(writer, format, ownsWriter: false)
    {
    }

    private MetricsLog(TextWriter writer, MetricsFormat format, bool ownsWriter)
    {
        _writer = writer;
        _ownsWriter = ownsWriter;
        Format = format;
    }

    /// <summary>
    /// Creates a log file, choosing the format from the extension: <c>.jsonl</c> or <c>.ndjson</c>
    /// for JSON Lines, anything else for CSV.
    /// </summary>
    public static MetricsLog Create(string path)
    {
        string extension = Path.GetExtension(path);
        var format = extension.Equals(".jsonl", StringComparison.OrdinalIgnoreCase) ||
                     extension.Equals(".ndjson", StringComparison.OrdinalIgnoreCase)
            ? MetricsFormat.JsonLines
            : MetricsFormat.Csv;

        return Create(path, format);
    }

    /// <summary>Creates a log file in the given format, replacing any existing file.</summary>
    public static MetricsLog Create(string path, MetricsFormat format) =>
        new(new StreamWriter(path, append: false), format, ownsWriter: true);

    public MetricsFormat Format { get; }

    /// <summary>Rows written so far.</summary>
    public int Count { get; private set; }

    /// <summary>A callback for <see cref="TrainingOptions.OnEpisode"/> that logs <paramref name="agent"/>'s metrics.</summary>
    public Action<EpisodeReport> For(IAgent agent) => episode => Write(episode, agent.Metrics);

    /// <summary>Appends one row.</summary>
    public void Write(EpisodeReport episode, AgentMetrics? metrics = null)
    {
        var values = new string?[]
        {
            Integer(episode.Episode),
            Integer(episode.Steps),
            Number(episode.Return),
            Number(episode.AverageReturn),
            Integer(episode.TotalSteps),
            episode.Terminated ? "true" : "false",
            Number(metrics?.ValueLoss),
            Number(metrics?.PolicyLoss),
            Number(metrics?.Entropy),
            Number(metrics?.Epsilon),
            Number(metrics?.Temperature),
            Number(metrics?.TdError),
            metrics is null ? null : Integer(metrics.UpdateCount),
        };

        if (Format == MetricsFormat.Csv) WriteCsv(values);
        else WriteJson(values);

        Count++;
    }

    /// <summary>Pushes buffered rows to the underlying file.</summary>
    public void Flush() => _writer.Flush();

    public void Dispose()
    {
        _writer.Flush();
        if (_ownsWriter) _writer.Dispose();
    }

    private void WriteCsv(string?[] values)
    {
        if (!_headerWritten)
        {
            _writer.WriteLine(string.Join(',', Columns));
            _headerWritten = true;
        }

        _writer.WriteLine(string.Join(',', values.Select(v => v ?? string.Empty)));
    }

    private void WriteJson(string?[] values)
    {
        _writer.Write('{');
        for (int i = 0; i < Columns.Length; i++)
        {
            if (i > 0) _writer.Write(',');
            _writer.Write('"');
            _writer.Write(Columns[i]);
            _writer.Write("\":");
            _writer.Write(values[i] ?? "null");
        }
        _writer.WriteLine('}');
    }

    private static string Integer(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Round-trip formatting, or null for a value the algorithm does not report.</summary>
    private static string? Number(float? value) =>
        value is { } v && float.IsFinite(v) ? v.ToString("R", CultureInfo.InvariantCulture) : null;
}
