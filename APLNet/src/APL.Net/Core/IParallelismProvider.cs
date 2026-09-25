// APL.Net - Another Parallel Library for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace AplNet.Core;

/// <summary>
/// Supplies the default degree of parallelism. Exists so scheduling can be tested independently of
/// the machine it runs on.
/// </summary>
public interface IParallelismProvider
{
    /// <summary>The number of workers a loop should use when none is specified.</summary>
    int DegreeOfParallelism { get; }
}

/// <summary>Uses <see cref="Environment.ProcessorCount"/>.</summary>
public sealed class DefaultParallelismProvider : IParallelismProvider
{
    /// <summary>The shared instance.</summary>
    public static DefaultParallelismProvider Instance { get; } = new();

    /// <inheritdoc />
    public int DegreeOfParallelism => Environment.ProcessorCount;
}

/// <summary>Always reports the same degree of parallelism.</summary>
public sealed class FixedParallelismProvider : IParallelismProvider
{
    /// <summary>Creates a provider that always reports <paramref name="degreeOfParallelism"/>.</summary>
    public FixedParallelismProvider(int degreeOfParallelism)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(degreeOfParallelism, 1);
        DegreeOfParallelism = degreeOfParallelism;
    }

    /// <inheritdoc />
    public int DegreeOfParallelism { get; }
}
