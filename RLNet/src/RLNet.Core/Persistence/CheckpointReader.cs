// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Neural;
using RLNet.Utils;

namespace RLNet.Persistence;

/// <summary>Hands named arrays from a loaded <see cref="Checkpoint"/> back to an <see cref="ICheckpointable"/>.</summary>
/// <remarks>
/// Every read checks both that the entry exists and that its length matches the destination.
/// A checkpoint from a differently configured agent therefore fails on the first entry that
/// differs, naming it, rather than half-loading.
/// </remarks>
public sealed class CheckpointReader
{
    private readonly Dictionary<string, CheckpointEntry> _entries;
    private string _prefix = string.Empty;

    internal CheckpointReader(IEnumerable<CheckpointEntry> entries) =>
        _entries = entries.ToDictionary(e => e.Name, StringComparer.Ordinal);

    /// <summary>Whether an entry exists, for state that older checkpoints may not carry.</summary>
    public bool Contains(string name) => _entries.ContainsKey(_prefix + name);

    /// <summary>Reads a float array into <paramref name="destination"/>, which must match its length exactly.</summary>
    public void Read(string name, Span<float> destination)
    {
        var values = Find(name).Floats ?? throw Mismatch(name, "is not a float array");
        if (values.Length != destination.Length)
            throw Mismatch(name, $"holds {values.Length} values but {destination.Length} were expected");
        values.CopyTo(destination);
    }

    /// <summary>Reads an integer array into <paramref name="destination"/>, which must match its length exactly.</summary>
    public void Read(string name, Span<long> destination)
    {
        var values = Find(name).Integers ?? throw Mismatch(name, "is not an integer array");
        if (values.Length != destination.Length)
            throw Mismatch(name, $"holds {values.Length} values but {destination.Length} were expected");
        values.CopyTo(destination);
    }

    /// <summary>Reads a float array of whatever length was written.</summary>
    public float[] ReadSingles(string name) =>
        (float[])(Find(name).Floats ?? throw Mismatch(name, "is not a float array")).Clone();

    /// <summary>Reads an integer array of whatever length was written.</summary>
    public long[] ReadInt64s(string name) =>
        (long[])(Find(name).Integers ?? throw Mismatch(name, "is not an integer array")).Clone();

    /// <summary>Reads one float.</summary>
    public float ReadSingle(string name)
    {
        Span<float> value = stackalloc float[1];
        Read(name, value);
        return value[0];
    }

    /// <summary>Reads one integer.</summary>
    public long ReadInt64(string name)
    {
        Span<long> value = stackalloc long[1];
        Read(name, value);
        return value[0];
    }

    /// <summary>Restores a network written by <see cref="CheckpointWriter.Write(string, MlpNetwork)"/>.</summary>
    public void Read(string name, MlpNetwork network)
    {
        long[] expected = CheckpointShapes.Of(network);
        long[] actual = ReadInt64s(name + ".shape");
        if (!expected.AsSpan().SequenceEqual(actual))
            throw Mismatch(name, $"was saved from a network shaped {Describe(actual)}, not {Describe(expected)}");

        var parameters = new float[network.ParameterCount];
        Read(name + ".parameters", parameters);
        network.ImportParameters(parameters);
    }

    /// <summary>Restores an optimiser written by <see cref="CheckpointWriter.Write(string, AdamOptimizer)"/>.</summary>
    public void Read(string name, AdamOptimizer optimizer)
    {
        Read(name + ".m", optimizer.FirstMoments);
        Read(name + ".v", optimizer.SecondMoments);
        optimizer.StepCount = checked((int)ReadInt64(name + ".step"));
    }

    /// <summary>Restores a random generator written by <see cref="CheckpointWriter.Write(string, FastRandom)"/>.</summary>
    public void Read(string name, FastRandom random)
    {
        Span<long> state = stackalloc long[FastRandom.StateLength];
        Read(name, state);
        random.ImportState(state);
    }

    /// <summary>Restores a nested component written by <see cref="CheckpointWriter.WriteComponent"/>.</summary>
    public void ReadComponent(string name, ICheckpointable component)
    {
        string saved = _prefix;
        _prefix = saved + name + "/";
        try { component.ReadState(this); }
        finally { _prefix = saved; }
    }

    private CheckpointEntry Find(string name) =>
        _entries.TryGetValue(_prefix + name, out var entry)
            ? entry
            : throw new InvalidDataException(
                $"Checkpoint has no entry '{_prefix + name}'. It was probably saved from a different kind of agent or an older configuration.");

    private InvalidDataException Mismatch(string name, string detail) =>
        new($"Checkpoint entry '{_prefix + name}' {detail}. The agent being loaded is configured differently from the one that was saved.");

    private static string Describe(long[] shape)
    {
        // Input width, then (width, activation) pairs; only the widths are worth showing.
        var widths = new List<long> { shape.Length > 0 ? shape[0] : 0 };
        for (int i = 1; i < shape.Length; i += 2) widths.Add(shape[i]);
        return "[" + string.Join(", ", widths) + "]";
    }
}
