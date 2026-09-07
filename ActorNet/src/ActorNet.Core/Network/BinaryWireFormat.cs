// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using ActorNet.Serialization;

namespace ActorNet.Network;

/// <summary>Which encoding a frame is written in.</summary>
public enum WireFormat
{
    /// <summary>
    /// JSON. The default, and the only thing the Go, Python and Node clients speak.
    /// </summary>
    Json,

    /// <summary>
    /// A compact binary envelope. Node to node only.
    /// </summary>
    Binary,
}

/// <summary>
/// A binary encoding of <see cref="WireEnvelope"/>.
/// </summary>
/// <remarks>
/// <para>
/// An envelope is mostly repeated short strings - the target address, the sending node, the message
/// alias, a correlation id - and in JSON each of them carries a quoted key, quotes, commas and
/// braces. For the small messages an actor system actually sends, that framing is a large fraction
/// of the bytes. This drops it to a tag byte and a length.
/// </para>
/// <para>
/// <strong>The payload is still JSON.</strong> What is encoded here is the envelope around it;
/// making the message body binary as well would mean a per-type binary codec for every registered
/// message, which is a much larger thing than this and would not be backward compatible with the
/// cross-language clients at all. The saving is on the framing, and it is worth stating plainly
/// rather than implying a whole binary protocol.
/// </para>
/// <para>
/// Frames are self-describing: a JSON envelope starts with <c>{</c> and a binary one with
/// <see cref="Magic"/>, so a reader can accept either without negotiating and a node that speaks
/// binary can still answer a client that does not.
/// </para>
/// </remarks>
public static class BinaryWireFormat
{
    /// <summary>First byte of a binary frame. Chosen so it cannot be confused with JSON's <c>{</c>.</summary>
    public const byte Magic = 0xAC;

    /// <summary>Format version, so an old reader rejects a new frame rather than misreading it.</summary>
    public const byte Version = 1;

    // Tag-per-field rather than a fixed layout: a field added later is skipped by an older reader
    // instead of shifting everything after it.
    private const byte EndOfFrame = 0;
    private const byte TagKind = 1;
    private const byte TagTarget = 2;
    private const byte TagSender = 3;
    private const byte TagAlias = 4;
    private const byte TagPayload = 5;
    private const byte TagCorrelationId = 6;
    private const byte TagReplyToNode = 7;
    private const byte TagFromNode = 8;
    private const byte TagError = 9;
    private const byte TagMembers = 10;
    private const byte TagTraceParent = 11;
    private const byte TagTraceState = 12;

    /// <summary>A body encoded by <see cref="BinaryMessageCodec"/> rather than as JSON text.</summary>
    /// <remarks>
    /// A separate tag rather than a flag on <see cref="TagPayload"/>, so a reader that predates
    /// this simply skips it - it would find a message with no body, which is a clean failure rather
    /// than a JSON parser being handed bytes.
    /// </remarks>
    private const byte TagBinaryPayload = 13;

    /// <summary>The agreed membership and its epoch, as one length-prefixed block.</summary>
    /// <remarks>
    /// One block rather than two fields, because the reader can only skip a field it does not know
    /// if that field carries its own length. A pair of bare varints would be read as something else
    /// entirely by a peer one version behind.
    /// </remarks>
    private const byte TagAgreed = 14;

    /// <summary>Encodes message bodies. Shared, because its per-type plans are worth keeping.</summary>
    private static readonly BinaryMessageCodec Codec = new();

    /// <summary>Encodes an envelope.</summary>
    /// <param name="serializer">
    /// Used only when the body's shape is one the codec does not support, to fall back to JSON for
    /// that message. Null means an already-serialized payload is the only fallback there is.
    /// </param>
    public static byte[] Write(WireEnvelope envelope, IMessageSerializer? serializer = null)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var buffer = new ArrayBufferWriter<byte>(256);

        Byte(buffer, Magic);
        Byte(buffer, Version);

        Byte(buffer, TagKind);
        Byte(buffer, (byte)envelope.Kind);

        String(buffer, TagTarget, envelope.Target);
        String(buffer, TagSender, envelope.Sender);
        String(buffer, TagAlias, envelope.MessageAlias);

        // The body, encoded if this codec knows the shape and copied as JSON if it does not. The
        // fallback is per type and decided once, which is what makes turning this on safe: nothing
        // depends on the codec covering every shape a message can take.
        var body = new ArrayBufferWriter<byte>(128);
        if (envelope.Body is { } message && Codec.TryWrite(message, body))
        {
            Byte(buffer, TagBinaryPayload);
            Bytes(buffer, body.WrittenSpan);
        }
        else
        {
            // The codec did not know this shape, so the body travels as JSON inside a binary
            // envelope. Serialized here rather than earlier, because until this point nobody knew
            // it would be needed.
            var json = envelope.Payload
                ?? (envelope.Body is { } fallback && serializer is not null ? serializer.Serialize(fallback).Payload : null);

            if (json is { } text)
            {
                Byte(buffer, TagPayload);
                Bytes(buffer, Encoding.UTF8.GetBytes(text.GetRawText()));
            }
        }

        String(buffer, TagCorrelationId, envelope.CorrelationId);
        String(buffer, TagReplyToNode, envelope.ReplyToNode);
        String(buffer, TagFromNode, envelope.FromNode);
        String(buffer, TagError, envelope.Error);
        String(buffer, TagTraceParent, envelope.TraceParent);
        String(buffer, TagTraceState, envelope.TraceState);

        if (envelope.Members is { Count: > 0 } members)
        {
            Byte(buffer, TagMembers);
            Varint(buffer, (uint)members.Count);

            foreach (var member in members)
            {
                Text(buffer, member.NodeId);
                Text(buffer, member.Host);
                Varint(buffer, (uint)member.Port);
                Varint(buffer, (uint)member.Status);
                Varint(buffer, (ulong)member.Incarnation);
            }
        }

        if (envelope.AgreedEpoch > 0 && envelope.AgreedMembers is { Count: > 0 } agreed)
        {
            var inner = new ArrayBufferWriter<byte>(64);
            Varint(inner, (ulong)envelope.AgreedEpoch);
            Varint(inner, (uint)agreed.Count);
            foreach (var member in agreed) Text(inner, member);

            Byte(buffer, TagAgreed);
            Bytes(buffer, inner.WrittenSpan);
        }

        Byte(buffer, EndOfFrame);
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Decodes an envelope written by <see cref="Write"/>.</summary>
    /// <param name="types">
    /// The allow-list, needed to turn a binary body back into a message. Null reads the frame
    /// without decoding one, which is what a caller that only wants the envelope should pass.
    /// </param>
    public static WireEnvelope Read(ReadOnlySpan<byte> source, MessageTypeRegistry? types = null)
    {
        if (source.Length < 3 || source[0] != Magic)
            throw new ActorNetException("Not a binary ActorNet frame.");

        if (source[1] != Version)
            throw new ActorNetException($"Binary frame version {source[1]} is not supported; this node speaks version {Version}.");

        var envelope = new WireEnvelope();
        byte[]? binaryBody = null;
        var at = 2;

        while (at < source.Length)
        {
            var tag = source[at++];
            if (tag == EndOfFrame) break;

            switch (tag)
            {
                case TagKind:
                    envelope.Kind = (WireKind)source[at++];
                    break;
                case TagTarget: envelope.Target = ReadText(source, ref at); break;
                case TagSender: envelope.Sender = ReadText(source, ref at); break;
                case TagAlias: envelope.MessageAlias = ReadText(source, ref at); break;
                case TagPayload:
                    // Copied and cloned on purpose. The span points into a pooled buffer that the
                    // caller returns as soon as this method comes back, and a JsonElement still
                    // referencing it would read whatever the next frame put there.
                    using (var document = JsonDocument.Parse(ReadBytes(source, ref at).ToArray()))
                    {
                        envelope.Payload = document.RootElement.Clone();
                    }

                    break;
                case TagBinaryPayload:
                    // Kept as bytes here. Turning it into a message needs the alias to say which
                    // type, and the alias may not have been read yet - the frame does not promise
                    // an order, and inventing one would be a rule for the sake of this line.
                    binaryBody = ReadBytes(source, ref at).ToArray();
                    break;
                case TagCorrelationId: envelope.CorrelationId = ReadText(source, ref at); break;
                case TagReplyToNode: envelope.ReplyToNode = ReadText(source, ref at); break;
                case TagFromNode: envelope.FromNode = ReadText(source, ref at); break;
                case TagError: envelope.Error = ReadText(source, ref at); break;
                case TagTraceParent: envelope.TraceParent = ReadText(source, ref at); break;
                case TagTraceState: envelope.TraceState = ReadText(source, ref at); break;
                case TagAgreed:
                {
                    var block = ReadBytes(source, ref at);
                    var inner = 0;
                    envelope.AgreedEpoch = (long)ReadVarint(block, ref inner);

                    var agreedCount = (int)ReadVarint(block, ref inner);
                    var agreed = new List<string>(agreedCount);
                    for (var i = 0; i < agreedCount; i++) agreed.Add(ReadText(block, ref inner) ?? string.Empty);

                    envelope.AgreedMembers = agreed;
                    break;
                }

                case TagMembers:
                    var count = (int)ReadVarint(source, ref at);
                    var members = new List<WireMember>(count);
                    for (var i = 0; i < count; i++)
                    {
                        members.Add(new WireMember
                        {
                            NodeId = ReadText(source, ref at) ?? string.Empty,
                            Host = ReadText(source, ref at) ?? string.Empty,
                            Port = (int)ReadVarint(source, ref at),
                            Status = (int)ReadVarint(source, ref at),
                            Incarnation = (long)ReadVarint(source, ref at),
                        });
                    }

                    envelope.Members = members;
                    break;
                default:
                    // A field this version does not know. It carries its own length, so skipping it
                    // is what makes the format survive a peer one version ahead.
                    ReadBytes(source, ref at);
                    break;
            }
        }

        if (binaryBody is not null) envelope.Body = Decode(envelope.MessageAlias, binaryBody, types);

        return envelope;
    }

    /// <summary>Turns a binary body back into a message, or leaves it to fail as a dead letter.</summary>
    /// <remarks>
    /// A type this node does not have on its allow-list is not decoded and not guessed at: the
    /// envelope comes back with no body, and the ordinary unknown-message path reports it. That is
    /// the same refusal the JSON path makes, and it is the one that stops a peer choosing which
    /// type this process constructs.
    /// </remarks>
    private static object? Decode(string? alias, byte[] body, MessageTypeRegistry? types)
    {
        if (alias is null || types is null) return null;
        if (!types.TryResolve(alias, out var type)) return null;

        return Codec.Read(type, body);
    }

    private static void Byte(ArrayBufferWriter<byte> buffer, byte value)
    {
        buffer.GetSpan(1)[0] = value;
        buffer.Advance(1);
    }

    private static void String(ArrayBufferWriter<byte> buffer, byte tag, string? value)
    {
        if (value is null) return;

        Byte(buffer, tag);
        Text(buffer, value);
    }

    private static void Text(ArrayBufferWriter<byte> buffer, string value)
    {
        var count = Encoding.UTF8.GetByteCount(value);
        Varint(buffer, (uint)count);

        var span = buffer.GetSpan(count);
        Encoding.UTF8.GetBytes(value, span);
        buffer.Advance(count);
    }

    private static void Bytes(ArrayBufferWriter<byte> buffer, ReadOnlySpan<byte> value)
    {
        Varint(buffer, (uint)value.Length);
        value.CopyTo(buffer.GetSpan(value.Length));
        buffer.Advance(value.Length);
    }

    /// <summary>Seven bits at a time, high bit set while more follow.</summary>
    /// <remarks>
    /// Lengths and ports are small almost always and occasionally are not, which is the case a
    /// varint is for: a four-byte fixed length on every string would cost more than the strings.
    /// </remarks>
    private static void Varint(ArrayBufferWriter<byte> buffer, ulong value)
    {
        while (value >= 0x80)
        {
            Byte(buffer, (byte)(value | 0x80));
            value >>= 7;
        }

        Byte(buffer, (byte)value);
    }

    private static ulong ReadVarint(ReadOnlySpan<byte> source, ref int at)
    {
        ulong value = 0;
        var shift = 0;

        while (true)
        {
            if (at >= source.Length) throw new ActorNetException("Binary frame ended inside a number.");
            if (shift > 63) throw new ActorNetException("Binary frame contains an over-long number.");

            var b = source[at++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return value;

            shift += 7;
        }
    }

    private static string? ReadText(ReadOnlySpan<byte> source, ref int at)
    {
        var bytes = ReadBytes(source, ref at);
        return Encoding.UTF8.GetString(bytes);
    }

    private static ReadOnlySpan<byte> ReadBytes(ReadOnlySpan<byte> source, ref int at)
    {
        var length = (int)ReadVarint(source, ref at);
        if (length < 0 || at + length > source.Length)
            throw new ActorNetException("Binary frame announces a field longer than the frame.");

        var slice = source.Slice(at, length);
        at += length;
        return slice;
    }
}
