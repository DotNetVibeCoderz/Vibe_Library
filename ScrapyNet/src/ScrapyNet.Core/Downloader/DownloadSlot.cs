using System.Diagnostics;

namespace ScrapyNet.Engine;

/// <summary>
/// Per-domain (or per-IP) download lane: caps concurrent downloads and spaces them by
/// <see cref="Delay"/>. AutoThrottle adjusts <see cref="Delay"/> at run time.
/// </summary>
/// <remarks>
/// The delay is enforced by reserving start times: each download claims the slot's next free instant
/// and pushes it forward by one delay, so concurrent waiters queue up without a background loop and
/// without holding a lock while sleeping.
/// </remarks>
public sealed class DownloadSlot
{
    private readonly SemaphoreSlim _semaphore;
    private readonly object _gate = new();
    private long _nextStartTicks;
    private int _active;
    private int _transferring;

    public DownloadSlot(string key, int concurrency, double delaySeconds, bool randomizeDelay)
    {
        Key = key;
        Concurrency = Math.Max(1, concurrency);
        Delay = delaySeconds;
        RandomizeDelay = randomizeDelay;
        _semaphore = new SemaphoreSlim(Concurrency, Concurrency);
    }

    public string Key { get; }

    public int Concurrency { get; }

    /// <summary>Seconds between download starts in this slot.</summary>
    public double Delay { get; set; }

    public bool RandomizeDelay { get; }

    /// <summary>Requests queued for or downloading in this slot.</summary>
    public int Active => Volatile.Read(ref _active);

    /// <summary>Requests downloading right now.</summary>
    public int Transferring => Volatile.Read(ref _transferring);

    /// <summary>When the slot last started a download (for idle-slot cleanup).</summary>
    public DateTimeOffset LastSeen { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>Waits for a concurrency permit and the delay; dispose the result to release the permit.</summary>
    public async ValueTask<Lease> AcquireAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _active);
        try
        {
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Interlocked.Decrement(ref _active);
            throw;
        }

        try
        {
            var wait = ReserveStart();
            if (wait > TimeSpan.Zero) await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _semaphore.Release();
            Interlocked.Decrement(ref _active);
            throw;
        }

        Interlocked.Increment(ref _transferring);
        LastSeen = DateTimeOffset.UtcNow;
        return new Lease(this);
    }

    private TimeSpan ReserveStart()
    {
        var delay = Delay;
        if (delay <= 0) return TimeSpan.Zero;
        if (RandomizeDelay) delay *= 0.5 + Random.Shared.NextDouble();
        var delayTicks = (long)(delay * Stopwatch.Frequency);
        lock (_gate)
        {
            var now = Stopwatch.GetTimestamp();
            var start = Math.Max(now, _nextStartTicks);
            _nextStartTicks = start + delayTicks;
            return Stopwatch.GetElapsedTime(now, start);
        }
    }

    private void Release()
    {
        Interlocked.Decrement(ref _transferring);
        Interlocked.Decrement(ref _active);
        _semaphore.Release();
    }

    public readonly struct Lease(DownloadSlot slot) : IDisposable
    {
        public void Dispose() => slot.Release();
    }

    public override string ToString() => $"<DownloadSlot {Key} concurrency={Concurrency} delay={Delay:0.00} active={Active} transferring={Transferring}>";
}
