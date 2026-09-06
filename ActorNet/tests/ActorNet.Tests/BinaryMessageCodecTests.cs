// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Buffers;
using ActorNet.Serialization;

namespace ActorNet.Tests;

/// <summary>
/// The message body encoded as bytes rather than JSON.
/// </summary>
/// <remarks>
/// The framing work before this left the body as JSON, and measurement put the body at 40-69% of a
/// binary frame - the larger half for anything but the smallest message. What matters here is not
/// that it is smaller, which is arithmetic, but that everything survives the round trip and that
/// anything the codec cannot handle says so instead of guessing.
/// </remarks>
public sealed class BinaryMessageCodecTests
{
    private static readonly BinaryMessageCodec Codec = new();

    private static T Roundtrip<T>(T message) where T : notnull
    {
        var buffer = new ArrayBufferWriter<byte>();
        Assert.True(Codec.TryWrite(message, buffer), $"{typeof(T).Name} should be supported");

        return (T)Codec.Read(typeof(T), buffer.WrittenSpan);
    }

    [Fact]
    public void EveryScalarSurvivesTheRoundTrip()
    {
        var original = new Kitchen(
            Text: "opening balance",
            Flag: true,
            Small: 7,
            Count: -1234,
            Big: long.MinValue + 1,
            Ratio: 0.1 + 0.2,
            Single: 1.5f,
            Money: 12345.6789m,
            Id: Guid.NewGuid(),
            At: new DateTimeOffset(2026, 9, 6, 15, 4, 5, TimeSpan.FromHours(7)),
            Plain: new DateTime(2026, 9, 6, 15, 4, 5, DateTimeKind.Utc),
            Elapsed: TimeSpan.FromMilliseconds(1234567),
            Colour: Shade.Amber);

        Assert.Equal(original, Roundtrip(original));
    }

    [Fact]
    public void NullsComeBackAsNulls()
    {
        var original = new Optional(null, null, null);

        var restored = Roundtrip(original);

        // Absent rather than empty on the wire, which has to read back as absent rather than as a
        // value that happens to be zero.
        Assert.Null(restored.Text);
        Assert.Null(restored.Count);
        Assert.Null(restored.When);
    }

    [Fact]
    public void CollectionsKeepTheirOrderAndTheirGaps()
    {
        var original = new Basket(
            ["first", null, "third"],
            [3, 1, 2],
            [new Line("SKU-1", 2, 19.99m), new Line("SKU-2", 1, 5.50m)]);

        var restored = Roundtrip(original);

        Assert.Equal(["first", null, "third"], restored.Labels);
        Assert.Equal([3, 1, 2], restored.Quantities);
        Assert.Equal(2, restored.Lines.Count);
        Assert.Equal("SKU-2", restored.Lines[1].Sku);
        Assert.Equal(19.99m, restored.Lines[0].Price);
    }

    [Fact]
    public void AnEmptyCollectionIsNotANullOne()
    {
        var restored = Roundtrip(new Basket([], [], []));

        Assert.Empty(restored.Labels);
        Assert.Empty(restored.Quantities);
        Assert.Empty(restored.Lines);
    }

    [Fact]
    public void AFieldAppendedLaterIsSkippedByAReaderThatDoesNotHaveIt()
    {
        // Two shapes of the same message, as two versions of an assembly would be. The reader has
        // the older one; the extra field has to be stepped over rather than misread as the next.
        var buffer = new ArrayBufferWriter<byte>();
        Assert.True(Codec.TryWrite(new Extended("acct-1", 42, "added later"), buffer));

        var restored = (Original)Codec.Read(typeof(Original), buffer.WrittenSpan);

        Assert.Equal("acct-1", restored.Account);
        Assert.Equal(42, restored.Amount);
    }

    [Fact]
    public void AFieldRemovedLaterLeavesADefaultRatherThanAFailure()
    {
        var buffer = new ArrayBufferWriter<byte>();
        Assert.True(Codec.TryWrite(new Original("acct-2", 7), buffer));

        var restored = (Extended)Codec.Read(typeof(Extended), buffer.WrittenSpan);

        Assert.Equal("acct-2", restored.Account);
        Assert.Equal(7, restored.Amount);
        Assert.Null(restored.Note);
    }

    [Fact]
    public void AShapeItCannotEncodeSaysSoInsteadOfGuessing()
    {
        var buffer = new ArrayBufferWriter<byte>();

        // A dictionary is not in the supported set. Refusing is what sends this message as JSON
        // instead, which is the whole reason the fallback exists.
        Assert.False(Codec.TryWrite(new Awkward(new Dictionary<string, int> { ["a"] = 1 }), buffer));
        Assert.False(Codec.Supports(typeof(Awkward)));
    }

    [Fact]
    public void TheFrameworkMessagesAreAllSupported()
    {
        // These cross the wire on every cluster, so a fallback for them would mean the binary
        // format never applied to the traffic that never stops.
        Assert.True(Codec.Supports(typeof(Watch)));
        Assert.True(Codec.Supports(typeof(Unwatch)));
        Assert.True(Codec.Supports(typeof(Terminated)));
        Assert.True(Codec.Supports(typeof(Warm)));
        Assert.True(Codec.Supports(typeof(Metrics.NodeStatus)));
    }

    [Fact]
    public void ABodyIsSmallerThanItsJson()
    {
        var reading = new Kitchen("sensor-0042", true, 3, 21, 1_700_000_000, 21.7, 48.2f, 19.99m,
            Guid.NewGuid(), DateTimeOffset.UnixEpoch, DateTime.UnixEpoch, TimeSpan.FromSeconds(30), Shade.Amber);

        var buffer = new ArrayBufferWriter<byte>();
        Assert.True(Codec.TryWrite(reading, buffer));

        var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(reading).Length;

        Assert.True(buffer.WrittenCount < json, $"binary was {buffer.WrittenCount} bytes against JSON's {json}");
    }
}

public enum Shade { Slate, Amber, Sky }

public sealed record Kitchen(
    string Text, bool Flag, byte Small, int Count, long Big, double Ratio, float Single,
    decimal Money, Guid Id, DateTimeOffset At, DateTime Plain, TimeSpan Elapsed, Shade Colour);

public sealed record Optional(string? Text, int? Count, DateTimeOffset? When);

public sealed record Line(string Sku, int Quantity, decimal Price);

public sealed record Basket(IReadOnlyList<string?> Labels, int[] Quantities, IReadOnlyList<Line> Lines);

public sealed record Original(string Account, int Amount);

public sealed record Extended(string Account, int Amount, string? Note);

public sealed record Awkward(Dictionary<string, int> Lookup);
