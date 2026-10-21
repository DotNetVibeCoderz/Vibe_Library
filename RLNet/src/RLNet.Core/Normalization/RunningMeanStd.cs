// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Persistence;

namespace RLNet.Normalization;

/// <summary>
/// Per-dimension running mean and variance, updated one sample at a time.
/// </summary>
/// <remarks>
/// Welford's algorithm, accumulated in <see cref="double"/>. The naive sum-of-squares form
/// subtracts two large nearly-equal numbers once the count reaches the millions, and the variance
/// it reports can go negative; Welford's update never forms that difference.
/// </remarks>
public sealed class RunningMeanStd : ICheckpointable
{
    private readonly double[] _mean;
    private readonly double[] _m2;
    private long _count;

    public RunningMeanStd(int dimensions)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(dimensions, 1);
        _mean = new double[dimensions];
        _m2 = new double[dimensions];
    }

    /// <summary>Number of dimensions tracked.</summary>
    public int Dimensions => _mean.Length;

    /// <summary>Samples seen.</summary>
    public long Count => _count;

    /// <summary>Folds one sample into the statistics.</summary>
    public void Update(ReadOnlySpan<float> sample)
    {
        if (sample.Length != _mean.Length)
            throw new ArgumentException($"Expected {_mean.Length} values, got {sample.Length}.", nameof(sample));

        _count++;
        for (int i = 0; i < _mean.Length; i++)
        {
            double delta = sample[i] - _mean[i];
            _mean[i] += delta / _count;
            _m2[i] += delta * (sample[i] - _mean[i]);
        }
    }

    /// <summary>Folds a single scalar in; for one-dimensional statistics such as a return.</summary>
    public void Update(float value) => Update([value]);

    /// <summary>Mean of dimension <paramref name="index"/>.</summary>
    public float Mean(int index) => (float)_mean[index];

    /// <summary>Population variance of dimension <paramref name="index"/>; 1 until two samples have been seen.</summary>
    public float Variance(int index) => _count < 2 ? 1f : (float)(_m2[index] / _count);

    /// <summary>Forgets everything seen so far.</summary>
    public void Reset()
    {
        Array.Clear(_mean);
        Array.Clear(_m2);
        _count = 0;
    }

    public void WriteState(CheckpointWriter writer)
    {
        // Stored as raw double bit patterns: a restored normaliser has to standardise exactly as
        // the saved one did, or a restored policy acts on inputs a rounding error away from the
        // ones it was trained on.
        writer.Write("count", _count);
        writer.Write("mean", ToBits(_mean));
        writer.Write("m2", ToBits(_m2));
    }

    public void ReadState(CheckpointReader reader)
    {
        var bits = new long[_mean.Length];
        reader.Read("mean", bits);
        for (int i = 0; i < bits.Length; i++) _mean[i] = BitConverter.Int64BitsToDouble(bits[i]);
        reader.Read("m2", bits);
        for (int i = 0; i < bits.Length; i++) _m2[i] = BitConverter.Int64BitsToDouble(bits[i]);
        _count = reader.ReadInt64("count");
    }

    private static long[] ToBits(double[] values)
    {
        var bits = new long[values.Length];
        for (int i = 0; i < values.Length; i++) bits[i] = BitConverter.DoubleToInt64Bits(values[i]);
        return bits;
    }
}
