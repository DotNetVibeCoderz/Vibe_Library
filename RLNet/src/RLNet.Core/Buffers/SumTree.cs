// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace RLNet.Buffers;

/// <summary>
/// A complete binary tree over a fixed number of leaves that maintains both the sum and the
/// minimum of every subtree, supporting O(log n) update, O(log n) proportional sampling, and
/// O(1) minimum.
/// </summary>
/// <remarks>
/// <para>
/// This is the data structure that makes prioritised replay affordable. The naive alternative —
/// building a cumulative distribution over every stored priority and binary-searching it — is
/// O(n) per draw against a buffer of a million entries, on every one of the 32 to 256 draws in a
/// minibatch, on every gradient step. The tree turns that into about twenty comparisons.
/// </para>
/// <para>
/// The minimum is tracked in the same walk rather than scanned for, because prioritised replay
/// needs it on every <em>sample</em> to normalise importance-sampling weights. Scanning would
/// put the O(n) back exactly where the tree was meant to remove it.
/// </para>
/// </remarks>
public sealed class SumTree
{
    private readonly float[] _sum; // 1-indexed heap: node i has children 2i and 2i+1
    private readonly float[] _min;
    private readonly int _leafOffset;

    /// <summary>Number of usable leaves.</summary>
    public int Length { get; }

    public SumTree(int length)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 1);

        Length = length;

        // Round the leaf count up to a power of two so the tree is perfect and a leaf's index is
        // just its position plus a fixed offset — no bounds arithmetic in the hot loops.
        int leaves = 1;
        while (leaves < length) leaves <<= 1;

        _leafOffset = leaves;
        _sum = new float[leaves * 2];
        _min = new float[leaves * 2];

        // Unwritten leaves — both the padding past Length and slots not yet filled — must not
        // win the minimum, so the min tree starts at infinity rather than zero.
        Array.Fill(_min, float.PositiveInfinity);
    }

    /// <summary>Total of every leaf.</summary>
    public float Total => _sum[1];

    /// <summary>Smallest value among leaves that have been written, or infinity if none have.</summary>
    public float Min => _min[1];

    /// <summary>Largest leaf value written so far, so new entries can enter at maximum priority.</summary>
    public float Max { get; private set; }

    /// <summary>Reads one leaf.</summary>
    public float this[int index] => _sum[_leafOffset + index];

    /// <summary>Sets one leaf and repairs the sums and minima on the path to the root.</summary>
    public void Set(int index, float value)
    {
        int node = _leafOffset + index;
        float delta = value - _sum[node];
        _sum[node] = value;
        _min[node] = value;

        if (value > Max) Max = value;

        while ((node >>= 1) >= 1)
        {
            // The sum walks up with a delta rather than re-reading both children, which halves
            // its memory traffic. The minimum has no such trick — lowering a leaf can raise a
            // parent — so it does read both.
            _sum[node] += delta;
            _min[node] = MathF.Min(_min[node << 1], _min[(node << 1) | 1]);
        }
    }

    /// <summary>
    /// Returns the leaf whose cumulative interval contains <paramref name="target"/>, which must
    /// lie in <c>[0, Total)</c>. Leaves are selected in proportion to their value.
    /// </summary>
    public int Find(float target)
    {
        int node = 1;
        while (node < _leafOffset)
        {
            int left = node << 1;
            if (target < _sum[left])
            {
                node = left;
            }
            else
            {
                target -= _sum[left];
                node = left + 1;
            }
        }

        // Float rounding can walk past the last populated leaf when target sits right at Total.
        return Math.Min(node - _leafOffset, Length - 1);
    }

    /// <summary>Resets every leaf.</summary>
    public void Clear()
    {
        Array.Clear(_sum);
        Array.Fill(_min, float.PositiveInfinity);
        Max = 0f;
    }
}
