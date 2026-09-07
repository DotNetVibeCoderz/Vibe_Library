// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace ActorNet.Cluster;

/// <summary>Asks the coordinator to leave, or tells it this node has finished leaving.</summary>
/// <param name="Node">The node asking.</param>
/// <param name="Releasing">True to hand the token back rather than ask for it.</param>
public sealed record LeaveRequest(string Node, bool Releasing);

/// <summary>The coordinator's answer.</summary>
/// <param name="Granted">Whether the asking node may leave now.</param>
/// <param name="HeldBy">The node holding the token, when the answer is no. For the log, not for logic.</param>
/// <param name="RetryAfter">How long to wait before asking again.</param>
/// <remarks>
/// A refusal carries a delay rather than leaving the caller to invent one. The coordinator knows
/// how long the current holder's lease has left; the caller does not, and would either poll harder
/// than it needs to or wait longer than it has to.
/// </remarks>
public sealed record LeaveDecision(bool Granted, string? HeldBy, TimeSpan RetryAfter);

/// <summary>
/// Hands out one leave token at a time, so a rolling restart is a rolling one.
/// </summary>
/// <remarks>
/// <para>
/// Held by whichever member has the lowest node id. Every node works that out from its own member
/// table, so there is nothing to elect and nothing to fail over - when the coordinator itself
/// leaves, the next-lowest id is the coordinator from the moment its departure lands, and the token
/// starts out free there.
/// </para>
/// <para>
/// That last point is also the limit of what this promises: a token that lives in the coordinator's
/// memory is lost when the coordinator changes, so a node that held it is no longer recorded as
/// holding it. In a healthy cluster the holder is the only node leaving and finishes in seconds. In
/// a partition, both sides have a coordinator and both will grant. This orders shutdowns; it is not
/// a lock.
/// </para>
/// </remarks>
internal sealed class LeaveTokenHolder(TimeProvider time)
{
    private readonly Lock _gate = new();

    private string? _holder;
    private DateTimeOffset _expires;

    /// <summary>The node currently holding the token, if any is.</summary>
    public string? Holder
    {
        get
        {
            lock (_gate) return Expired() ? null : _holder;
        }
    }

    /// <summary>Answers one request, granting the token when it is free.</summary>
    public LeaveDecision Decide(LeaveRequest request, TimeSpan lease)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_gate)
        {
            if (request.Releasing)
            {
                // Only the holder can release, or a node that lost a race would hand back a token
                // somebody else is relying on.
                if (string.Equals(_holder, request.Node, StringComparison.Ordinal)) _holder = null;
                return new LeaveDecision(true, null, TimeSpan.Zero);
            }

            // Re-asking is not a second request. A node whose reply was lost asks again, and
            // refusing it its own token would leave it waiting for itself.
            if (Expired() || string.Equals(_holder, request.Node, StringComparison.Ordinal))
            {
                _holder = request.Node;
                _expires = time.GetUtcNow() + lease;
                return new LeaveDecision(true, request.Node, TimeSpan.Zero);
            }

            // The lease is the upper bound on the wait, and a floor keeps a nearly-expired lease
            // from turning into a tight polling loop.
            var remaining = _expires - time.GetUtcNow();
            var retry = remaining < TimeSpan.FromMilliseconds(250) ? TimeSpan.FromMilliseconds(250) : remaining;
            return new LeaveDecision(false, _holder, retry);
        }
    }

    private bool Expired() => _holder is null || time.GetUtcNow() >= _expires;
}
