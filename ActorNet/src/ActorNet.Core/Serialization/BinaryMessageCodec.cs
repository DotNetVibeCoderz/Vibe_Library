// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Buffers;
using System.Buffers.Binary;
using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text;

namespace ActorNet.Serialization;

/// <summary>
/// Encodes a registered message as bytes instead of JSON, for the node-to-node path.
/// </summary>
/// <remarks>
/// <para>
/// Measured on the framing work that came before this, a message body is 40-69% of a binary frame
/// for anything but the smallest message - so encoding the envelope and leaving the body as JSON
/// captured the smaller half. This is the other half.
/// </para>
/// <para>
/// <strong>Fields are tagged by position, not by name.</strong> A field is a varint tag - its
/// one-based index and a wire type - followed by its value, which is what lets a reader skip a
/// field it does not know instead of losing its place. Appending a property to a message is
/// therefore safe across a rolling upgrade; reordering the parameters of a record is not, but that
/// is a breaking change to the type either way.
/// </para>
/// <para>
/// <strong>Anything it cannot encode falls back to JSON</strong>, per type and decided once. That
/// fallback is what makes this safe to turn on: correctness never depends on this file covering
/// every shape a message can take, only on it being honest about which ones it covers.
/// </para>
/// </remarks>
public sealed class BinaryMessageCodec
{
    /// <summary>How a value is laid out, so an unknown field can still be stepped over.</summary>
    private enum Wire : byte
    {
        Varint = 0,
        Fixed64 = 1,
        Bytes = 2,
        Fixed32 = 3,
    }

    /// <summary>One property of a message: where it sits, how it is written, how it is read.</summary>
    private sealed record Field(
        int Index,
        string Name,
        Type Type,
        Func<object, object?> Read,
        Wire Wire);

    /// <summary>How to write and rebuild one message type, worked out once.</summary>
    private sealed record Plan(IReadOnlyList<Field> Fields, ConstructorInfo? Constructor, IReadOnlyList<PropertyInfo> Settable);

    private readonly ConcurrentDictionary<Type, Plan?> _plans = new();

    /// <summary>Whether this type can be encoded at all. Cached, including the failures.</summary>
    public bool Supports(Type type) => PlanFor(type) is not null;

    /// <summary>Encodes a message, or returns false when its shape is not supported.</summary>
    public bool TryWrite(object message, IBufferWriter<byte> buffer)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(buffer);

        var plan = PlanFor(message.GetType());
        if (plan is null) return false;

        WriteBody(plan, message, buffer);
        return true;
    }

    /// <summary>Rebuilds a message of <paramref name="type"/> from bytes.</summary>
    public object Read(Type type, ReadOnlySpan<byte> source)
    {
        var plan = PlanFor(type) ?? throw new ActorNetException(
            $"{type.Name} cannot be read from the binary format; it should have arrived as JSON.");

        return ReadBody(plan, type, source);
    }

    // ---------------------------------------------------------------- writing

    private void WriteBody(Plan plan, object message, IBufferWriter<byte> buffer)
    {
        foreach (var field in plan.Fields)
        {
            var value = field.Read(message);

            // Absent rather than empty. Writing a tag for every null would cost more than it saves,
            // and it would turn "no reference" into "a reference to nothing".
            if (value is null) continue;

            WriteVarint(buffer, (ulong)((field.Index << 2) | (int)field.Wire));
            WriteValue(buffer, Underlying(field.Type), value);
        }
    }

    private void WriteValue(IBufferWriter<byte> buffer, Type type, object value)
    {
        switch (value)
        {
            case bool b: WriteVarint(buffer, b ? 1UL : 0UL); return;
            case string s: WriteBytes(buffer, Encoding.UTF8.GetBytes(s)); return;
            case double d: WriteFixed64(buffer, BitConverter.DoubleToInt64Bits(d)); return;
            case float f: WriteFixed32(buffer, BitConverter.SingleToInt32Bits(f)); return;
            case decimal m: WriteDecimal(buffer, m); return;
            case Guid g: WriteBytes(buffer, g.ToByteArray()); return;
            case DateTimeOffset at: WriteDateTimeOffset(buffer, at); return;
            case DateTime at: WriteDateTime(buffer, at); return;
            case TimeSpan span: WriteVarint(buffer, ZigZag(span.Ticks)); return;
        }

        if (type.IsEnum)
        {
            WriteVarint(buffer, ZigZag(Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture)));
            return;
        }

        if (IsInteger(type))
        {
            WriteVarint(buffer, ZigZag(Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture)));
            return;
        }

        if (ElementOf(type) is { } element && value is IEnumerable sequence)
        {
            var items = new ArrayBufferWriter<byte>(64);
            var count = 0;

            foreach (var item in sequence)
            {
                count++;
                if (item is null)
                {
                    // A tag of its own, so a null inside a list is not confused with the end of it.
                    WriteVarint(items, 0);
                    continue;
                }

                WriteVarint(items, 1);
                WriteValue(items, Underlying(element), item);
            }

            var body = new ArrayBufferWriter<byte>(items.WrittenCount + 4);
            WriteVarint(body, (ulong)count);
            body.Write(items.WrittenSpan);
            WriteBytes(buffer, body.WrittenSpan);
            return;
        }

        // A nested message. Its own plan, length-delimited so a reader that does not know it can
        // step over the whole thing.
        var nested = PlanFor(type) ?? throw new ActorNetException($"{type.Name} cannot be encoded in binary.");
        var inner = new ArrayBufferWriter<byte>(64);
        WriteBody(nested, value, inner);
        WriteBytes(buffer, inner.WrittenSpan);
    }

    // ---------------------------------------------------------------- reading

    private object ReadBody(Plan plan, Type type, ReadOnlySpan<byte> source)
    {
        var values = new Dictionary<int, object?>();
        var at = 0;

        while (at < source.Length)
        {
            var tag = (int)ReadVarint(source, ref at);
            var index = tag >> 2;
            var wire = (Wire)(tag & 0b11);

            var field = plan.Fields.FirstOrDefault(f => f.Index == index);
            if (field is null)
            {
                // A field this version does not have. Skipping by wire type is the entire reason
                // fields carry one, and it is what makes appending a property safe.
                Skip(source, ref at, wire);
                continue;
            }

            values[index] = ReadValue(source, ref at, Underlying(field.Type), wire);
        }

        return Construct(plan, type, values);
    }

    private object? ReadValue(ReadOnlySpan<byte> source, ref int at, Type type, Wire wire)
    {
        if (type == typeof(bool)) return ReadVarint(source, ref at) != 0;
        if (type == typeof(string)) return Encoding.UTF8.GetString(ReadBytes(source, ref at));
        if (type == typeof(double)) return BitConverter.Int64BitsToDouble(ReadFixed64(source, ref at));
        if (type == typeof(float)) return BitConverter.Int32BitsToSingle(ReadFixed32(source, ref at));
        if (type == typeof(decimal)) return ReadDecimal(source, ref at);
        if (type == typeof(Guid)) return new Guid(ReadBytes(source, ref at));
        if (type == typeof(DateTimeOffset)) return ReadDateTimeOffset(source, ref at);
        if (type == typeof(DateTime)) return ReadDateTime(source, ref at);
        if (type == typeof(TimeSpan)) return TimeSpan.FromTicks(UnZigZag(ReadVarint(source, ref at)));

        if (type.IsEnum) return Enum.ToObject(type, UnZigZag(ReadVarint(source, ref at)));
        if (IsInteger(type)) return Convert.ChangeType(UnZigZag(ReadVarint(source, ref at)), type, System.Globalization.CultureInfo.InvariantCulture);

        if (ElementOf(type) is { } element)
        {
            var body = ReadBytes(source, ref at);
            var inner = 0;
            var count = (int)ReadVarint(body, ref inner);

            var items = Array.CreateInstance(element, count);
            for (var i = 0; i < count; i++)
            {
                if (ReadVarint(body, ref inner) == 0) continue;
                items.SetValue(ReadValue(body, ref inner, Underlying(element), Wire.Bytes), i);
            }

            return Materialize(type, element, items);
        }

        var nested = PlanFor(type) ?? throw new ActorNetException($"{type.Name} cannot be decoded from binary.");
        return ReadBody(nested, type, ReadBytes(source, ref at));
    }

    private static object Materialize(Type target, Type element, Array items)
    {
        if (target.IsArray) return items;

        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(element))!;
        foreach (var item in items) list.Add(item);
        return list;
    }

    private static object Construct(Plan plan, Type type, Dictionary<int, object?> values)
    {
        if (plan.Constructor is { } constructor)
        {
            var parameters = constructor.GetParameters();
            var arguments = new object?[parameters.Length];

            for (var i = 0; i < parameters.Length; i++)
            {
                var field = plan.Fields.FirstOrDefault(f =>
                    string.Equals(f.Name, parameters[i].Name, StringComparison.OrdinalIgnoreCase));

                arguments[i] = field is not null && values.TryGetValue(field.Index, out var value) && value is not null
                    ? value
                    : Default(parameters[i].ParameterType);
            }

            return constructor.Invoke(arguments);
        }

        var instance = Activator.CreateInstance(type)
            ?? throw new ActorNetException($"{type.Name} has no constructor this codec can use.");

        foreach (var property in plan.Settable)
        {
            var field = plan.Fields.FirstOrDefault(f => f.Name == property.Name);
            if (field is null || !values.TryGetValue(field.Index, out var value) || value is null) continue;

            property.SetValue(instance, value);
        }

        return instance;
    }

    private static object? Default(Type type) => type.IsValueType ? Activator.CreateInstance(type) : null;

    // ------------------------------------------------------------------ plans

    private Plan? PlanFor(Type type) => _plans.GetOrAdd(type, BuildPlan);

    private Plan? BuildPlan(Type type)
    {
        if (type.IsPrimitive || type == typeof(string) || type.IsEnum) return null;

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .ToArray();

        // A message with no fields at all - a signal like Warm, or a request whose whole meaning is
        // its type - encodes to nothing, which is the cheapest thing on the wire and would be a
        // strange one to refuse.
        if (properties.Length == 0)
            return type.GetConstructor(Type.EmptyTypes) is null ? null : new Plan([], null, []);

        // A record's primary constructor gives the field order, and it is the order the type
        // declares rather than one this codec invents - so two nodes compiled from the same source
        // agree without anything being written down.
        var constructor = type.GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length)
            .FirstOrDefault(c => c.GetParameters().Length > 0 &&
                c.GetParameters().All(p => properties.Any(prop =>
                    string.Equals(prop.Name, p.Name, StringComparison.OrdinalIgnoreCase))));

        var ordered = constructor is not null
            ? constructor.GetParameters()
                .Select(p => properties.First(prop => string.Equals(prop.Name, p.Name, StringComparison.OrdinalIgnoreCase)))
                .Concat(properties.Where(prop => !constructor.GetParameters()
                    .Any(p => string.Equals(prop.Name, p.Name, StringComparison.OrdinalIgnoreCase))))
                .ToArray()
            : properties;

        var fields = new List<Field>(ordered.Length);
        var index = 1;

        foreach (var property in ordered)
        {
            if (!IsSupported(property.PropertyType)) return null;

            var wire = WireFor(Underlying(property.PropertyType));
            fields.Add(new Field(index++, property.Name, property.PropertyType, property.GetValue!, wire));
        }

        var settable = constructor is null
            ? ordered.Where(p => p.CanWrite).ToArray()
            : [];

        if (constructor is null && settable.Length == 0) return null;
        if (constructor is null && type.GetConstructor(Type.EmptyTypes) is null) return null;

        return new Plan(fields, constructor, settable);
    }

    private bool IsSupported(Type type)
    {
        var underlying = Underlying(type);

        if (underlying == typeof(string) || underlying == typeof(bool) || underlying == typeof(decimal) ||
            underlying == typeof(Guid) || underlying == typeof(DateTime) || underlying == typeof(DateTimeOffset) ||
            underlying == typeof(TimeSpan) || underlying == typeof(double) || underlying == typeof(float) ||
            underlying.IsEnum || IsInteger(underlying))
        {
            return true;
        }

        if (ElementOf(underlying) is { } element) return IsSupported(element);

        // Guarded against a type that contains itself: the recursion would not end, and a message
        // shaped like that is not something to discover at the first send.
        return !_building.Value!.Contains(underlying) && BuildNested(underlying);
    }

    private readonly ThreadLocal<HashSet<Type>> _building = new(() => []);

    private bool BuildNested(Type type)
    {
        _building.Value!.Add(type);
        try { return PlanFor(type) is not null; }
        finally { _building.Value!.Remove(type); }
    }

    private static bool IsInteger(Type type) =>
        type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) ||
        type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong);

    private static Type Underlying(Type type) => Nullable.GetUnderlyingType(type) ?? type;

    private static Type? ElementOf(Type type)
    {
        if (type == typeof(string)) return null;
        if (type.IsArray) return type.GetElementType();

        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            if (definition == typeof(List<>) || definition == typeof(IReadOnlyList<>) ||
                definition == typeof(IList<>) || definition == typeof(IEnumerable<>) ||
                definition == typeof(ICollection<>) || definition == typeof(IReadOnlyCollection<>))
            {
                return type.GetGenericArguments()[0];
            }
        }

        return null;
    }

    private static Wire WireFor(Type type)
    {
        if (type == typeof(double)) return Wire.Fixed64;
        if (type == typeof(float)) return Wire.Fixed32;
        if (type == typeof(bool) || type.IsEnum || IsInteger(type) || type == typeof(TimeSpan)) return Wire.Varint;
        return Wire.Bytes;
    }

    // ------------------------------------------------------------- primitives

    private static void WriteVarint(IBufferWriter<byte> buffer, ulong value)
    {
        while (value >= 0x80)
        {
            buffer.GetSpan(1)[0] = (byte)(value | 0x80);
            buffer.Advance(1);
            value >>= 7;
        }

        buffer.GetSpan(1)[0] = (byte)value;
        buffer.Advance(1);
    }

    /// <summary>Maps a signed value onto an unsigned one so small negatives stay small.</summary>
    private static ulong ZigZag(long value) => (ulong)((value << 1) ^ (value >> 63));

    private static long UnZigZag(ulong value) => (long)(value >> 1) ^ -(long)(value & 1);

    private static void WriteBytes(IBufferWriter<byte> buffer, ReadOnlySpan<byte> value)
    {
        WriteVarint(buffer, (ulong)value.Length);
        value.CopyTo(buffer.GetSpan(value.Length));
        buffer.Advance(value.Length);
    }

    private static void WriteFixed64(IBufferWriter<byte> buffer, long value)
    {
        BinaryPrimitives.WriteInt64LittleEndian(buffer.GetSpan(8), value);
        buffer.Advance(8);
    }

    private static void WriteFixed32(IBufferWriter<byte> buffer, int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(buffer.GetSpan(4), value);
        buffer.Advance(4);
    }

    private static void WriteDecimal(IBufferWriter<byte> buffer, decimal value)
    {
        Span<byte> bytes = stackalloc byte[16];
        var bits = decimal.GetBits(value);
        for (var i = 0; i < 4; i++) BinaryPrimitives.WriteInt32LittleEndian(bytes[(i * 4)..], bits[i]);
        WriteBytes(buffer, bytes);
    }

    private static void WriteDateTimeOffset(IBufferWriter<byte> buffer, DateTimeOffset value)
    {
        Span<byte> bytes = stackalloc byte[10];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value.Ticks);
        BinaryPrimitives.WriteInt16LittleEndian(bytes[8..], (short)value.Offset.TotalMinutes);
        WriteBytes(buffer, bytes);
    }

    private static void WriteDateTime(IBufferWriter<byte> buffer, DateTime value)
    {
        Span<byte> bytes = stackalloc byte[9];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value.Ticks);
        bytes[8] = (byte)value.Kind;
        WriteBytes(buffer, bytes);
    }

    private static ulong ReadVarint(ReadOnlySpan<byte> source, ref int at)
    {
        ulong value = 0;
        var shift = 0;

        while (true)
        {
            if (at >= source.Length) throw new ActorNetException("A binary message ended inside a number.");
            if (shift > 63) throw new ActorNetException("A binary message contains an over-long number.");

            var b = source[at++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return value;

            shift += 7;
        }
    }

    private static ReadOnlySpan<byte> ReadBytes(ReadOnlySpan<byte> source, ref int at)
    {
        var length = (int)ReadVarint(source, ref at);
        if (length < 0 || at + length > source.Length)
            throw new ActorNetException("A binary message announces a field longer than the message.");

        var slice = source.Slice(at, length);
        at += length;
        return slice;
    }

    private static long ReadFixed64(ReadOnlySpan<byte> source, ref int at)
    {
        var value = BinaryPrimitives.ReadInt64LittleEndian(source[at..]);
        at += 8;
        return value;
    }

    private static int ReadFixed32(ReadOnlySpan<byte> source, ref int at)
    {
        var value = BinaryPrimitives.ReadInt32LittleEndian(source[at..]);
        at += 4;
        return value;
    }

    private static decimal ReadDecimal(ReadOnlySpan<byte> source, ref int at)
    {
        var bytes = ReadBytes(source, ref at);
        Span<int> bits = stackalloc int[4];
        for (var i = 0; i < 4; i++) bits[i] = BinaryPrimitives.ReadInt32LittleEndian(bytes[(i * 4)..]);
        return new decimal(bits);
    }

    private static DateTimeOffset ReadDateTimeOffset(ReadOnlySpan<byte> source, ref int at)
    {
        var bytes = ReadBytes(source, ref at);
        var ticks = BinaryPrimitives.ReadInt64LittleEndian(bytes);
        var offset = BinaryPrimitives.ReadInt16LittleEndian(bytes[8..]);
        return new DateTimeOffset(ticks, TimeSpan.FromMinutes(offset));
    }

    private static DateTime ReadDateTime(ReadOnlySpan<byte> source, ref int at)
    {
        var bytes = ReadBytes(source, ref at);
        return new DateTime(BinaryPrimitives.ReadInt64LittleEndian(bytes), (DateTimeKind)bytes[8]);
    }

    private static void Skip(ReadOnlySpan<byte> source, ref int at, Wire wire)
    {
        switch (wire)
        {
            case Wire.Varint: ReadVarint(source, ref at); return;
            case Wire.Fixed64: at += 8; return;
            case Wire.Fixed32: at += 4; return;
            default: ReadBytes(source, ref at); return;
        }
    }
}
