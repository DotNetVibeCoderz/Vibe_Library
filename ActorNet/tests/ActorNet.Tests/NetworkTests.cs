// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Text.Json;
using ActorNet.Network;
using ActorNet.Serialization;

namespace ActorNet.Tests;

public sealed class FrameCodecTests
{
    private static WireEnvelope Sample(string payload) => new()
    {
        Kind = WireKind.Message,
        Target = "CounterActor/one",
        Sender = "CounterActor/two",
        MessageAlias = "ActorNet.Tests.Add",
        Payload = JsonSerializer.SerializeToElement(new { text = payload }),
        FromNode = "node-a",
    };

    [Fact]
    public async Task FramesSurviveBeingCoalescedIntoOneRead()
    {
        using var stream = new MemoryStream();
        await FrameCodec.WriteAsync(stream, Sample("first"), default);
        await FrameCodec.WriteAsync(stream, Sample("second"), default);
        stream.Position = 0;

        // Two sends landing in one TCP segment is the case a naive "one read is one message"
        // transport gets wrong.
        var first = await FrameCodec.ReadAsync(stream, default);
        var second = await FrameCodec.ReadAsync(stream, default);

        Assert.Equal("first", first!.Payload!.Value.GetProperty("text").GetString());
        Assert.Equal("second", second!.Payload!.Value.GetProperty("text").GetString());
        Assert.Null(await FrameCodec.ReadAsync(stream, default));
    }

    [Fact]
    public async Task AFrameSurvivesBeingSplitAcrossReads()
    {
        using var buffer = new MemoryStream();
        await FrameCodec.WriteAsync(buffer, Sample(new string('x', 12_000)), default);

        // A stream that hands back one byte at a time is the other half of the same problem: a
        // payload larger than the receive buffer arrives in pieces.
        using var dribbling = new DribblingStream(buffer.ToArray());
        var frame = await FrameCodec.ReadAsync(dribbling, default);

        Assert.Equal(12_000, frame!.Payload!.Value.GetProperty("text").GetString()!.Length);
    }

    [Fact]
    public async Task PayloadsLargerThanTheOldFixedBufferRoundTrip()
    {
        using var stream = new MemoryStream();
        await FrameCodec.WriteAsync(stream, Sample(new string('y', 200_000)), default);
        stream.Position = 0;

        var frame = await FrameCodec.ReadAsync(stream, default);

        Assert.Equal(200_000, frame!.Payload!.Value.GetProperty("text").GetString()!.Length);
    }

    [Fact]
    public async Task AnAbsurdAnnouncedLengthIsRefusedRatherThanAllocated()
    {
        using var stream = new MemoryStream([0x7F, 0xFF, 0xFF, 0xFF]);

        // Without this check, a peer announcing a 2 GB frame gets this process to try to allocate
        // for it.
        await Assert.ThrowsAsync<ActorNetException>(async () => await FrameCodec.ReadAsync(stream, default));
    }

    [Fact]
    public async Task ATornFrameIsAnErrorNotASilentTruncation()
    {
        using var buffer = new MemoryStream();
        await FrameCodec.WriteAsync(buffer, Sample("complete"), default);
        var bytes = buffer.ToArray();

        using var truncated = new MemoryStream(bytes[..(bytes.Length - 10)]);
        await Assert.ThrowsAsync<EndOfStreamException>(async () => await FrameCodec.ReadAsync(truncated, default));
    }

    /// <summary>A stream that returns one byte per read, to simulate fragmentation.</summary>
    private sealed class DribblingStream(byte[] data) : Stream
    {
        private int _position;

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= data.Length) return 0;
            buffer[offset] = data[_position++];
            return 1;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position >= data.Length || buffer.Length == 0) return ValueTask.FromResult(0);
            buffer.Span[0] = data[_position++];
            return ValueTask.FromResult(1);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => data.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

public sealed class MessageTypeRegistryTests
{
    [Fact]
    public void AnUnregisteredAliasIsRefused()
    {
        var registry = new MessageTypeRegistry();

        // The alias is not resolved through Type.GetType, so a peer cannot name a type for this
        // process to construct. That refusal is the security property, not a convenience check.
        var ex = Assert.Throws<UnknownMessageTypeException>(() => registry.Resolve("System.Diagnostics.Process"));
        Assert.Equal("System.Diagnostics.Process", ex.Alias);
    }

    [Fact]
    public void AnAliasCannotBeReboundToADifferentType()
    {
        var registry = new MessageTypeRegistry();
        registry.Register<Add>("op");

        Assert.Throws<ActorNetException>(() => registry.Register<Credit>("op"));
    }

    [Fact]
    public void MessagesRoundTripThroughTheSerializer()
    {
        var serializer = new JsonMessageSerializer();
        serializer.Types.Register<Credit>();

        var (alias, payload) = serializer.Serialize(new Credit(12.5m));
        var back = Assert.IsType<Credit>(serializer.Deserialize(alias, payload));

        // Positional records only round-trip if the constructor is used, which is the shape every
        // message in the samples takes.
        Assert.Equal(12.5m, back.Amount);
    }

    [Fact]
    public void AttributedTypesRegisterInBulk()
    {
        var registry = new MessageTypeRegistry();
        var count = registry.RegisterFromAssembly(typeof(AttributedMessage).Assembly);

        Assert.True(count >= 1);
        Assert.Equal(typeof(AttributedMessage), registry.Resolve("tests.attributed"));
    }
}

[ActorMessage(Alias = "tests.attributed")]
public sealed record AttributedMessage(string Value);
