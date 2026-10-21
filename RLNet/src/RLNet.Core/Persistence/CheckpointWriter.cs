// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Neural;
using RLNet.Utils;

namespace RLNet.Persistence;

/// <summary>Collects named arrays from an <see cref="ICheckpointable"/> for <see cref="Checkpoint"/> to write.</summary>
/// <remarks>
/// A checkpoint is a flat list of named entries rather than a fixed record per agent type. That
/// keeps the format independent of any one agent's fields: adding a field to an agent adds an
/// entry, and an old checkpoint that lacks it fails with the entry's name instead of reading
/// misaligned bytes.
/// </remarks>
public sealed class CheckpointWriter
{
    private readonly List<CheckpointEntry> _entries = [];
    private readonly HashSet<string> _names = [];
    private string _prefix = string.Empty;

    internal CheckpointWriter() { }

    internal IReadOnlyList<CheckpointEntry> Entries => _entries;

    /// <summary>Writes a float array whose length is fixed by the component's configuration.</summary>
    public void Write(string name, ReadOnlySpan<float> values) =>
        Add(new CheckpointEntry(_prefix + name, values.ToArray(), null, Variable: false));

    /// <summary>Writes an integer array whose length is fixed by the component's configuration.</summary>
    public void Write(string name, ReadOnlySpan<long> values) =>
        Add(new CheckpointEntry(_prefix + name, null, values.ToArray(), Variable: false));

    /// <summary>
    /// Writes a float array whose length depends on what was learned — a Q-table's rows — rather
    /// than on configuration. Read it back with <see cref="CheckpointReader.ReadSingles"/>.
    /// </summary>
    public void WriteVariable(string name, ReadOnlySpan<float> values) =>
        Add(new CheckpointEntry(_prefix + name, values.ToArray(), null, Variable: true));

    /// <summary>Integer counterpart of <see cref="WriteVariable(string, ReadOnlySpan{float})"/>.</summary>
    public void WriteVariable(string name, ReadOnlySpan<long> values) =>
        Add(new CheckpointEntry(_prefix + name, null, values.ToArray(), Variable: true));

    /// <summary>Writes one float.</summary>
    public void Write(string name, float value) => Write(name, [value]);

    /// <summary>Writes one integer.</summary>
    public void Write(string name, long value) => Write(name, [value]);

    /// <summary>
    /// Writes a network's parameters together with its layer shape, so loading into a network of
    /// the same parameter count but different layout — <c>[64, 32]</c> against <c>[32, 64]</c> —
    /// is caught instead of silently scrambling the weights.
    /// </summary>
    public void Write(string name, MlpNetwork network)
    {
        Write(name + ".shape", CheckpointShapes.Of(network));
        Write(name + ".parameters", network.ExportParameters());
    }

    /// <summary>Writes an optimiser's moment estimates and step counter.</summary>
    public void Write(string name, AdamOptimizer optimizer)
    {
        Write(name + ".m", optimizer.FirstMoments);
        Write(name + ".v", optimizer.SecondMoments);
        Write(name + ".step", optimizer.StepCount);
    }

    /// <summary>Writes a random generator's exact position in its stream.</summary>
    public void Write(string name, FastRandom random)
    {
        Span<long> state = stackalloc long[FastRandom.StateLength];
        random.ExportState(state);
        Write(name, (ReadOnlySpan<long>)state);
    }

    /// <summary>Writes a nested component with every entry name prefixed by <paramref name="name"/>.</summary>
    public void WriteComponent(string name, ICheckpointable component)
    {
        string saved = _prefix;
        _prefix = saved + name + "/";
        try { component.WriteState(this); }
        finally { _prefix = saved; }
    }

    private void Add(CheckpointEntry entry)
    {
        if (!_names.Add(entry.Name))
            throw new InvalidOperationException($"Checkpoint entry '{entry.Name}' was written twice.");
        _entries.Add(entry);
    }
}

/// <summary>One named array. Exactly one of the two payloads is set.</summary>
/// <param name="Variable">Whether the length is data-dependent, which exempts it from the shape check on load.</param>
internal sealed record CheckpointEntry(string Name, float[]? Floats, long[]? Integers, bool Variable);

internal static class CheckpointShapes
{
    /// <summary>Input width followed by every layer's output width and activation.</summary>
    public static long[] Of(MlpNetwork network)
    {
        var shape = new long[1 + network.Layers.Count * 2];
        shape[0] = network.InputSize;
        for (int i = 0; i < network.Layers.Count; i++)
        {
            shape[1 + i * 2] = network.Layers[i].OutputSize;
            shape[2 + i * 2] = (long)network.Layers[i].Activation;
        }
        return shape;
    }
}
