// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Buffers;
using System.Buffers.Binary;
using System.Text.Json;
using ActorNet.Serialization;

namespace ActorNet.Network;

/// <summary>
/// Length-prefixed framing for the node-to-node protocol: a four-byte big-endian payload length,
/// then that many bytes of JSON.
/// </summary>
/// <remarks>
/// <para>
/// TCP is a byte stream, not a message stream. Reading "whatever one <c>ReadAsync</c> returned"
/// and treating it as one message is the classic bug - it works on localhost with small payloads
/// and corrupts the moment two sends coalesce into a segment or one message spans two. The length
/// prefix is what makes a frame a frame.
/// </para>
/// <para>
/// <see cref="MaxFrameBytes"/> is a hostile-input guard: without it a peer can announce a 4 GB
/// frame and this process will try to allocate for it.
/// </para>
/// </remarks>
public static class FrameCodec
{
    /// <summary>Bytes of length prefix on every frame.</summary>
    public const int HeaderBytes = 4;

    /// <summary>Largest payload accepted. A larger announced length closes the connection.</summary>
    public const int MaxFrameBytes = 32 * 1024 * 1024;

    /// <summary>Writes one frame.</summary>
    public static async ValueTask WriteAsync(Stream stream, WireEnvelope envelope, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(envelope, typeof(WireEnvelope), WireJsonContext.Default);
        if (payload.Length > MaxFrameBytes)
            throw new ActorNetException($"Frame of {payload.Length:N0} bytes exceeds the {MaxFrameBytes:N0} byte limit.");

        var buffer = ArrayPool<byte>.Shared.Rent(HeaderBytes + payload.Length);
        try
        {
            BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(0, HeaderBytes), payload.Length);
            payload.CopyTo(buffer.AsSpan(HeaderBytes));
            await stream.WriteAsync(buffer.AsMemory(0, HeaderBytes + payload.Length), cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Reads one frame, or returns null when the peer closed the connection cleanly between
    /// frames.
    /// </summary>
    public static async ValueTask<WireEnvelope?> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[HeaderBytes];
        if (!await ReadExactlyOrEofAsync(stream, header, cancellationToken).ConfigureAwait(false)) return null;

        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length is <= 0 or > MaxFrameBytes)
            throw new ActorNetException($"Peer announced a frame length of {length:N0} bytes, which is outside 1..{MaxFrameBytes:N0}. Closing the connection.");

        var buffer = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            if (!await ReadExactlyOrEofAsync(stream, buffer.AsMemory(0, length), cancellationToken).ConfigureAwait(false))
                throw new EndOfStreamException($"Connection closed {length:N0} bytes into a frame.");

            return JsonSerializer.Deserialize(buffer.AsSpan(0, length), typeof(WireEnvelope), WireJsonContext.Default) as WireEnvelope;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Fills <paramref name="destination"/> completely. False means a clean EOF before any byte
    /// arrived; a partial read is a torn frame and throws.
    /// </summary>
    private static async ValueTask<bool> ReadExactlyOrEofAsync(Stream stream, Memory<byte> destination, CancellationToken cancellationToken)
    {
        var read = 0;
        while (read < destination.Length)
        {
            var got = await stream.ReadAsync(destination[read..], cancellationToken).ConfigureAwait(false);
            if (got == 0)
            {
                if (read == 0) return false;
                throw new EndOfStreamException($"Connection closed after {read} of {destination.Length} expected bytes.");
            }

            read += got;
        }

        return true;
    }
}
