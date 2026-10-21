// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Text;

namespace RLNet.Persistence;

/// <summary>
/// Saves and restores the complete state of agents and normalisers, so a trained policy can be
/// shipped and an interrupted run can be resumed.
/// </summary>
/// <remarks>
/// <para>
/// The format is a small versioned binary container: a magic number, a format version, the type
/// name of each component, then a list of named arrays. It is little-endian on every platform,
/// so a checkpoint written on x64 Windows loads on ARM macOS.
/// </para>
/// <para>
/// Several components can share one file — typically an agent and the normaliser it was trained
/// behind. They are matched by position and by type name, so loading a PPO checkpoint into a SAC
/// agent fails immediately with both names in the message.
/// </para>
/// <code>
/// Checkpoint.Save("cartpole.rlnet", agent);
/// ...
/// var restored = new PpoAgent(environment.ObservationSpace, environment.ActionSpace, options);
/// Checkpoint.Load("cartpole.rlnet", restored);
/// </code>
/// </remarks>
public static class Checkpoint
{
    private static ReadOnlySpan<byte> Magic => "RLNETCKP"u8;

    /// <summary>The format version this build writes. Older versions are read when compatible.</summary>
    public const int FormatVersion = 1;

    private const byte FloatTag = 1;
    private const byte IntegerTag = 2;

    /// <summary>Writes one or more components to a file, replacing it.</summary>
    public static void Save(string path, params ICheckpointable[] components)
    {
        // Written beside the target and moved over it, so a crash mid-write never leaves a
        // truncated checkpoint where a good one used to be.
        string temporary = path + ".tmp";
        using (var stream = File.Create(temporary))
            Save(stream, components);
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>Writes one or more components to a stream.</summary>
    public static void Save(Stream stream, params ICheckpointable[] components)
    {
        ArgumentOutOfRangeException.ThrowIfZero(components.Length);

        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(FormatVersion);
        writer.Write(components.Length);

        foreach (var component in components)
        {
            var collector = new CheckpointWriter();
            component.WriteState(collector);

            writer.Write(component.GetType().FullName ?? component.GetType().Name);
            writer.Write(collector.Entries.Count);

            foreach (var entry in collector.Entries)
            {
                writer.Write(entry.Name);
                if (entry.Floats is { } floats)
                {
                    writer.Write(FloatTag);
                    writer.Write(floats.Length);
                    foreach (float value in floats) writer.Write(value);
                }
                else
                {
                    var integers = entry.Integers!;
                    writer.Write(IntegerTag);
                    writer.Write(integers.Length);
                    foreach (long value in integers) writer.Write(value);
                }
            }
        }
    }

    /// <summary>Restores components from a file, in the order they were saved.</summary>
    public static void Load(string path, params ICheckpointable[] components)
    {
        using var stream = File.OpenRead(path);
        Load(stream, components);
    }

    /// <summary>Restores components from a stream, in the order they were saved.</summary>
    public static void Load(Stream stream, params ICheckpointable[] components)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        Span<byte> magic = stackalloc byte[Magic.Length];
        reader.BaseStream.ReadExactly(magic);
        if (!magic.SequenceEqual(Magic))
            throw new InvalidDataException("Not an RLNet checkpoint.");

        int version = reader.ReadInt32();
        if (version < 1 || version > FormatVersion)
            throw new InvalidDataException(
                $"Checkpoint format version {version} is not supported by this build (which reads up to {FormatVersion}).");

        int count = reader.ReadInt32();
        if (count != components.Length)
            throw new InvalidDataException(
                $"Checkpoint holds {count} component(s) but {components.Length} were passed to Load.");

        var loaded = new List<List<CheckpointEntry>>(count);
        foreach (var component in components)
        {
            string savedType = reader.ReadString();
            string expectedType = component.GetType().FullName ?? component.GetType().Name;
            if (savedType != expectedType)
                throw new InvalidDataException($"Checkpoint holds a {savedType}, which cannot be loaded into a {expectedType}.");

            int entryCount = reader.ReadInt32();
            var entries = new List<CheckpointEntry>(entryCount);
            for (int i = 0; i < entryCount; i++)
            {
                string name = reader.ReadString();
                byte tag = reader.ReadByte();
                int length = reader.ReadInt32();
                if (length < 0) throw new InvalidDataException($"Checkpoint entry '{name}' has a negative length.");

                if (tag == FloatTag)
                {
                    var values = new float[length];
                    for (int j = 0; j < length; j++) values[j] = reader.ReadSingle();
                    entries.Add(new CheckpointEntry(name, values, null, Variable: false));
                }
                else if (tag == IntegerTag)
                {
                    var values = new long[length];
                    for (int j = 0; j < length; j++) values[j] = reader.ReadInt64();
                    entries.Add(new CheckpointEntry(name, null, values, Variable: false));
                }
                else
                {
                    throw new InvalidDataException($"Checkpoint entry '{name}' has unknown type tag {tag}.");
                }
            }

            loaded.Add(entries);
        }

        // Validate everything before touching anything. Each component describes what it expects
        // by writing its own current state; a checkpoint that differs in any entry's presence,
        // type, length or network shape is rejected while every component is still intact.
        for (int c = 0; c < components.Length; c++)
            Validate(components[c], loaded[c]);

        for (int c = 0; c < components.Length; c++)
            components[c].ReadState(new CheckpointReader(loaded[c]));
    }

    private static void Validate(ICheckpointable component, List<CheckpointEntry> saved)
    {
        var expected = new CheckpointWriter();
        component.WriteState(expected);

        var byName = saved.ToDictionary(e => e.Name, StringComparer.Ordinal);
        foreach (var want in expected.Entries)
        {
            if (!byName.TryGetValue(want.Name, out var have))
                throw new InvalidDataException(
                    $"Checkpoint has no entry '{want.Name}'. It was probably saved from a different kind of agent or an older configuration.");

            if ((want.Floats is null) != (have.Floats is null))
                throw new InvalidDataException($"Checkpoint entry '{want.Name}' has the wrong element type.");

            if (want.Variable) continue;

            int wantLength = want.Floats?.Length ?? want.Integers!.Length;
            int haveLength = have.Floats?.Length ?? have.Integers!.Length;
            if (wantLength != haveLength)
                throw new InvalidDataException(
                    $"Checkpoint entry '{want.Name}' holds {haveLength} values but {wantLength} were expected. " +
                    "The agent being loaded is configured differently from the one that was saved.");

            if (want.Name.EndsWith(".shape", StringComparison.Ordinal) &&
                !want.Integers!.AsSpan().SequenceEqual(have.Integers))
                throw new InvalidDataException(
                    $"Checkpoint entry '{want.Name}' was saved from a network shaped " +
                    $"[{string.Join(", ", have.Integers!)}], not [{string.Join(", ", want.Integers!)}].");
        }
    }
}
