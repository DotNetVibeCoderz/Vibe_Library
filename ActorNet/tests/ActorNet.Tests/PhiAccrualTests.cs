// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ActorNet.Cluster;

namespace ActorNet.Tests;

/// <summary>
/// Suspicion measured against a peer's own behaviour rather than one deadline for everybody.
/// </summary>
public sealed class PhiAccrualTests
{
    private static PhiAccrualFailureDetector Detector(
        double intervalMillis = 200,
        double pauseMillis = 0,
        double minimumDeviationMillis = 1,
        int sampleSize = 200) =>
        new(TimeSpan.FromMilliseconds(intervalMillis),
            TimeSpan.FromMilliseconds(pauseMillis),
            TimeSpan.FromMilliseconds(minimumDeviationMillis),
            sampleSize);

    /// <summary>Beats a node at a fixed interval, then returns where the clock ended up.</summary>
    private static DateTimeOffset Beat(PhiAccrualFailureDetector detector, string node, DateTimeOffset from, int count, double intervalMillis)
    {
        var at = from;
        detector.Heartbeat(node, at);

        for (var i = 0; i < count; i++)
        {
            at = at.AddMilliseconds(intervalMillis);
            detector.Heartbeat(node, at);
        }

        return at;
    }

    [Fact]
    public void APeerBeatingOnTimeIsNotSuspected()
    {
        var detector = Detector();
        var start = DateTimeOffset.UnixEpoch;

        var now = Beat(detector, "steady", start, 100, 200);

        // Asked at the instant its next beat is due, a punctual peer is not suspicious at all.
        Assert.True(detector.Phi("steady", now.AddMilliseconds(200)) < 1,
            $"phi was {detector.Phi("steady", now.AddMilliseconds(200))}");
    }

    [Fact]
    public void SuspicionGrowsWithSilence()
    {
        // Jittered beats on purpose. A metronomic peer's phi saturates within a beat or two of its
        // usual interval - correct, but it makes for a curve with nothing left to measure.
        var detector = Detector(minimumDeviationMillis: 10);
        var at = DateTimeOffset.UnixEpoch;
        detector.Heartbeat("quiet", at);
        for (var i = 0; i < 100; i++)
        {
            at = at.AddMilliseconds(i % 2 == 0 ? 150 : 250);
            detector.Heartbeat("quiet", at);
        }

        var readings = new[] { 300, 400, 500, 600 }
            .Select(ms => detector.Phi("quiet", at.AddMilliseconds(ms)))
            .ToArray();

        Assert.True(readings.Zip(readings.Skip(1)).All(pair => pair.Second > pair.First),
            $"phi should rise monotonically, got {string.Join(", ", readings.Select(r => r.ToString("N2")))}");
    }

    [Fact]
    public void AnErraticPeerIsGivenMoreRopeThanAPunctualOne()
    {
        var start = DateTimeOffset.UnixEpoch;

        var steady = Detector();
        var steadyEnd = Beat(steady, "steady", start, 100, 200);

        // Same average interval, wildly different spread. This is the whole point of adapting: the
        // erratic peer's silences are normal for it, so the same silence means less.
        var erratic = Detector();
        var at = start;
        erratic.Heartbeat("erratic", at);
        for (var i = 0; i < 100; i++)
        {
            at = at.AddMilliseconds(i % 2 == 0 ? 50 : 350);
            erratic.Heartbeat("erratic", at);
        }

        var steadyPhi = steady.Phi("steady", steadyEnd.AddMilliseconds(1_000));
        var erraticPhi = erratic.Phi("erratic", at.AddMilliseconds(1_000));

        Assert.True(steadyPhi > erraticPhi,
            $"steady {steadyPhi:N2} should out-suspect erratic {erraticPhi:N2} at the same silence");
    }

    [Fact]
    public void AGcPauseIsForgivenWhenTheAllowanceCoversIt()
    {
        var start = DateTimeOffset.UnixEpoch;

        var strict = Detector(pauseMillis: 0);
        var strictEnd = Beat(strict, "node", start, 100, 200);

        var lenient = Detector(pauseMillis: 3_000);
        var lenientEnd = Beat(lenient, "node", start, 100, 200);

        // A metronomic peer has almost no measured spread, so without the allowance a pause a
        // second past its usual beat already reads as a failure.
        Assert.True(strict.Phi("node", strictEnd.AddMilliseconds(1_000)) > 8);
        Assert.True(lenient.Phi("node", lenientEnd.AddMilliseconds(1_000)) < 1);
    }

    [Fact]
    public void APeerNeverHeardFromHasNoSuspicion()
    {
        var detector = Detector();

        // Zero rather than infinity on purpose: a name with no history is a peer this node has not
        // started watching, not a peer it has decided is dead.
        Assert.Equal(0, detector.Phi("stranger", DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void ForgettingAPeerClearsWhatWasLearnedAboutIt()
    {
        var detector = Detector();
        var start = DateTimeOffset.UnixEpoch;
        var now = Beat(detector, "gone", start, 100, 200);

        Assert.True(detector.Phi("gone", now.AddMilliseconds(5_000)) > 8);

        detector.Forget("gone");

        Assert.Equal(0, detector.Phi("gone", now.AddMilliseconds(5_000)));
    }

    [Fact]
    public void TheWindowForgetsOldIntervals()
    {
        var detector = Detector(sampleSize: 10);
        var start = DateTimeOffset.UnixEpoch;

        // A long, erratic past followed by a short, steady present. A window that never evicted
        // would still be pricing in intervals from minutes ago.
        var at = Beat(detector, "settled", start, 50, 2_000);
        at = Beat(detector, "settled", at, 50, 200);

        Assert.True(detector.Phi("settled", at.AddMilliseconds(2_000)) > 8,
            "a two-second silence should be suspicious once the peer has settled at 200ms");
    }

    [Fact]
    public void SuspicionIsSymmetricAroundTheMean()
    {
        // The approximation is piecewise: one branch each side of the mean. They have to meet.
        var below = PhiAccrualFailureDetector.Suspicion(999.9, 1_000, 100);
        var above = PhiAccrualFailureDetector.Suspicion(1_000.1, 1_000, 100);

        Assert.Equal(below, above, 3);
        Assert.Equal(0.301, PhiAccrualFailureDetector.Suspicion(1_000, 1_000, 100), 2);
    }
}
