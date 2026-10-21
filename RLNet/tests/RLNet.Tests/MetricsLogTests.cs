// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using System.Text.Json;
using RLNet.Agents;
using RLNet.Environments.Classic;
using RLNet.Training;
using Xunit;

namespace RLNet.Tests;

public class MetricsLogTests
{
    [Fact]
    public void CsvHasAHeaderAndOneRowPerEpisode()
    {
        var writer = new StringWriter();
        var log = new MetricsLog(writer, MetricsFormat.Csv);
        var agent = TrainLogged(log, episodes: 5);

        var lines = writer.ToString().TrimEnd().Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        Assert.Equal(6, lines.Length);
        Assert.StartsWith("episode,steps,return,", lines[0]);
        Assert.StartsWith("5,", lines[^1]);

        // Q-learning reports no policy loss: an empty field, not "NaN" and not 0.
        int policyLoss = Array.IndexOf(lines[0].Split(','), "policy_loss");
        Assert.Equal(string.Empty, lines[^1].Split(',')[policyLoss]);
        Assert.Equal(agent.Metrics.UpdateCount.ToString(CultureInfo.InvariantCulture), lines[^1].Split(',')[^1]);
    }

    [Fact]
    public void JsonLinesParseAndUseNullForMissingMetrics()
    {
        var writer = new StringWriter();
        TrainLogged(new MetricsLog(writer, MetricsFormat.JsonLines), episodes: 3);

        var rows = writer.ToString().TrimEnd().Split('\n').Select(l => JsonDocument.Parse(l).RootElement).ToArray();
        Assert.Equal(3, rows.Length);
        Assert.Equal(3, rows[^1].GetProperty("episode").GetInt32());
        Assert.Equal(JsonValueKind.Null, rows[^1].GetProperty("policy_loss").ValueKind);
        Assert.Equal(JsonValueKind.Number, rows[^1].GetProperty("epsilon").ValueKind);
    }

    [Fact]
    public void NumbersAreCultureInvariant()
    {
        // Built by hand rather than as "de-DE": the build runs with invariant globalization, where
        // named cultures are unavailable. The decimal comma is the part that matters.
        var decimalComma = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        decimalComma.NumberFormat.NumberDecimalSeparator = ",";

        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = decimalComma;
        try
        {
            var writer = new StringWriter(decimalComma);
            var log = new MetricsLog(writer, MetricsFormat.Csv);
            log.Write(new EpisodeReport(1, 10, 1.5f, 1.5f, 10, true));

            Assert.Contains(",1.5,1.5,", writer.ToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void FormatFollowsTheExtension()
    {
        string directory = Directory.CreateTempSubdirectory("rlnet-metrics").FullName;
        try
        {
            using (var json = MetricsLog.Create(Path.Combine(directory, "run.jsonl"))) Assert.Equal(MetricsFormat.JsonLines, json.Format);
            using (var csv = MetricsLog.Create(Path.Combine(directory, "run.csv"))) Assert.Equal(MetricsFormat.Csv, csv.Format);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static QTableAgent TrainLogged(MetricsLog log, int episodes)
    {
        var environment = new GridWorldEnvironment();
        var agent = new QTableAgent(environment.ActionSpace, StateDiscretizer.OneHot(), seed: 1);
        Trainer.Train(environment, agent, new TrainingOptions { MaxEpisodes = episodes, Seed = 1, OnEpisode = log.For(agent) });
        log.Flush();
        return agent;
    }
}
