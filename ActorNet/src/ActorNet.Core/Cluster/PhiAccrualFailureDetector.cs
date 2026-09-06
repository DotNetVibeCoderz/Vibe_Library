// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace ActorNet.Cluster;

/// <summary>
/// Suspicion as a number rather than a yes-or-no deadline.
/// </summary>
/// <remarks>
/// <para>
/// A fixed deadline has to be set for the worst link in the cluster, which makes it slow to notice
/// a real failure on the good ones. This keeps a window of how long each peer's heartbeats have
/// actually been taking and reports phi: roughly the number of nines of confidence that a peer this
/// quiet has stopped. Phi 8 is "wrong about one time in 10^8 given how this peer normally behaves".
/// </para>
/// <para>
/// The value of adapting is that the same threshold means different things on different links. A
/// peer over a wireless hop whose beats vary by half a second is given that slack; a peer on the
/// same switch beating every 200ms is suspected far sooner, because for it a two-second silence is
/// genuinely abnormal. The three-machine test that motivated this had healthy nodes crossing a
/// fixed 10s deadline while a co-located pair would not have crossed it in a minute.
/// </para>
/// <para>
/// The distribution is approximated with the logistic function rather than a real normal CDF. The
/// error is well under a tenth of a phi and it costs one <c>exp</c>, which matters because this
/// runs for every member on every beat.
/// </para>
/// </remarks>
public sealed class PhiAccrualFailureDetector
{
    /// <summary>One peer's heartbeat history: a bounded window kept as running sums.</summary>
    /// <remarks>
    /// Sums rather than a re-scan, so mean and variance cost the same at a window of 1000 as at a
    /// window of 10 - the whole point of a bounded window is that the cost stops growing.
    /// </remarks>
    private sealed class History
    {
        private readonly Queue<double> _intervals = new();
        private readonly int _capacity;
        private double _sum;
        private double _sumOfSquares;

        public History(int capacity) => _capacity = capacity;

        public DateTimeOffset LastArrival { get; set; }

        public int Count => _intervals.Count;

        public double Mean => _intervals.Count == 0 ? 0 : _sum / _intervals.Count;

        public double StandardDeviation
        {
            get
            {
                if (_intervals.Count == 0) return 0;

                var mean = Mean;
                var variance = (_sumOfSquares / _intervals.Count) - (mean * mean);
                return variance <= 0 ? 0 : Math.Sqrt(variance);
            }
        }

        public void Add(double interval)
        {
            _intervals.Enqueue(interval);
            _sum += interval;
            _sumOfSquares += interval * interval;

            if (_intervals.Count <= _capacity) return;

            var evicted = _intervals.Dequeue();
            _sum -= evicted;
            _sumOfSquares -= evicted * evicted;
        }
    }

    private readonly Dictionary<string, History> _nodes = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();
    private readonly double _firstHeartbeatMillis;
    private readonly double _acceptablePauseMillis;
    private readonly double _minimumStandardDeviation;
    private readonly int _sampleSize;

    /// <param name="firstHeartbeatEstimate">
    /// What to assume about a peer that has only been heard from once. Without a guess, the first
    /// beat after a join would have no distribution to sit in and every new member would look dead.
    /// </param>
    /// <param name="acceptableHeartbeatPause">
    /// Added to the mean before suspicion starts, which is what buys a stop-the-world GC pause the
    /// benefit of the doubt on an otherwise metronomic link.
    /// </param>
    /// <param name="minimumStandardDeviation">
    /// A floor on the spread. A peer whose beats are suspiciously regular would otherwise get a
    /// near-zero standard deviation, and phi would leap from nothing to enormous within a
    /// millisecond of its usual interval.
    /// </param>
    /// <param name="sampleSize">How many intervals the window keeps.</param>
    public PhiAccrualFailureDetector(
        TimeSpan firstHeartbeatEstimate,
        TimeSpan acceptableHeartbeatPause,
        TimeSpan minimumStandardDeviation,
        int sampleSize)
    {
        _firstHeartbeatMillis = firstHeartbeatEstimate.TotalMilliseconds;
        _acceptablePauseMillis = acceptableHeartbeatPause.TotalMilliseconds;
        _minimumStandardDeviation = minimumStandardDeviation.TotalMilliseconds;
        _sampleSize = sampleSize;
    }

    /// <summary>Records that a peer was heard from.</summary>
    public void Heartbeat(string nodeId, DateTimeOffset at)
    {
        lock (_gate)
        {
            if (!_nodes.TryGetValue(nodeId, out var history))
            {
                _nodes[nodeId] = Bootstrap(at);
                return;
            }

            var interval = (at - history.LastArrival).TotalMilliseconds;
            history.LastArrival = at;

            // Time can step backwards across a clock adjustment, and a negative interval would
            // corrupt the variance for the rest of the window's life.
            if (interval > 0) history.Add(interval);
        }
    }

    /// <summary>
    /// How suspicious this peer's current silence is. Zero for a peer never heard from.
    /// </summary>
    public double Phi(string nodeId, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (!_nodes.TryGetValue(nodeId, out var history) || history.Count == 0) return 0;

            var silence = (now - history.LastArrival).TotalMilliseconds;
            if (silence <= 0) return 0;

            var mean = history.Mean + _acceptablePauseMillis;
            var deviation = Math.Max(history.StandardDeviation, _minimumStandardDeviation);
            return Suspicion(silence, mean, deviation);
        }
    }

    /// <summary>Drops a peer's history, so a node that comes back starts from a clean estimate.</summary>
    /// <remarks>
    /// Called when a member is declared down. Keeping the history would fold the whole outage into
    /// the window as one enormous interval, and the peer would then be trusted through silences it
    /// should be suspected for.
    /// </remarks>
    public void Forget(string nodeId)
    {
        lock (_gate) _nodes.Remove(nodeId);
    }

    /// <summary>Seeds a new peer's window with a plausible spread around the expected interval.</summary>
    private History Bootstrap(DateTimeOffset at)
    {
        var history = new History(_sampleSize) { LastArrival = at };
        var deviation = _firstHeartbeatMillis / 4;

        // Two samples either side of the estimate: one alone would give a zero standard deviation,
        // which the floor would then have to rescue on every peer's first beat.
        history.Add(_firstHeartbeatMillis - deviation);
        history.Add(_firstHeartbeatMillis + deviation);
        return history;
    }

    /// <summary>The logistic approximation to <c>-log10(1 - F(silence))</c>.</summary>
    internal static double Suspicion(double silence, double mean, double standardDeviation)
    {
        var y = (silence - mean) / standardDeviation;
        var e = Math.Exp(-y * (1.5976 + (0.070566 * y * y)));

        return silence > mean
            ? -Math.Log10(e / (1 + e))
            : -Math.Log10(1 - (1 / (1 + e)));
    }
}
