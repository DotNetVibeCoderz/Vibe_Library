// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Agents;

namespace RLNet.Training;

/// <summary>A snapshot of one finished episode.</summary>
/// <param name="Episode">Episode number, from 1.</param>
/// <param name="Steps">Steps the episode lasted.</param>
/// <param name="Return">Undiscounted sum of rewards.</param>
/// <param name="AverageReturn">Mean return over a trailing window, the number worth plotting.</param>
/// <param name="TotalSteps">Environment steps taken across the whole run.</param>
/// <param name="Terminated">Whether the episode reached a terminal state rather than a time limit.</param>
/// <remarks>
/// A record struct so that reporting an episode allocates nothing — a run can be tens of
/// thousands of episodes, and the visualizer subscribes to every one of them.
/// </remarks>
public readonly record struct EpisodeReport(
    int Episode,
    int Steps,
    float Return,
    float AverageReturn,
    long TotalSteps,
    bool Terminated);

/// <summary>The result of a completed training run.</summary>
public sealed class TrainingReport
{
    /// <summary>Episodes completed.</summary>
    public int Episodes { get; init; }

    /// <summary>Environment steps taken.</summary>
    public long TotalSteps { get; init; }

    /// <summary>Wall-clock duration.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Mean return over the final window, the headline number for a run.</summary>
    public float FinalAverageReturn { get; init; }

    /// <summary>Best single-episode return seen.</summary>
    public float BestReturn { get; init; }

    /// <summary>Return of every episode, in order.</summary>
    public IReadOnlyList<float> Returns { get; init; } = [];

    /// <summary>Whether the run stopped early because the solve threshold was met.</summary>
    public bool Solved { get; init; }

    /// <summary>Episode the solve threshold was first met at, or -1.</summary>
    public int SolvedAtEpisode { get; init; } = -1;

    /// <summary>The agent's final diagnostics.</summary>
    public AgentMetrics? Metrics { get; init; }

    /// <summary>Environment steps per second, the number to compare across configurations.</summary>
    public double StepsPerSecond => Duration.TotalSeconds > 0 ? TotalSteps / Duration.TotalSeconds : 0;

    public override string ToString() =>
        $"{Episodes} episodes, {TotalSteps:N0} steps in {Duration.TotalSeconds:F1}s " +
        $"({StepsPerSecond:N0} steps/s), final average return {FinalAverageReturn:F2}" +
        (Solved ? $", solved at episode {SolvedAtEpisode}" : string.Empty);
}
