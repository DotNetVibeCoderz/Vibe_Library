using System.Buffers.Binary;
using System.IO.Compression;

namespace Lvgl.Backends;

/// <summary>
/// Encodes an XRGB8888 surface as a PNG, using only the zlib stream in the base class library -
/// enough for screenshots and visual tests without pulling an imaging package into the core.
/// </summary>
internal static class PngWriter
{
    /// <summary>Writes <paramref name="pixels"/> (B, G, R, X per pixel) as an 8-bit RGB PNG.</summary>
    public static void Write(Stream output, ReadOnlySpan<byte> pixels, int width, int height, int stride)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            var row = new byte[1 + width * 3];   // filter byte 0 (none), then RGB triples

            for (var y = 0; y < height; y++)
            {
                var source = pixels.Slice(y * stride, width * 4);
                for (var x = 0; x < width; x++)
                {
                    row[1 + x * 3] = source[x * 4 + 2];
                    row[2 + x * 3] = source[x * 4 + 1];
                    row[3 + x * 3] = source[x * 4];
                }

                zlib.Write(row);
            }
        }

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
        header[8] = 8;    // bits per channel
        header[9] = 2;    // colour type: RGB
        header[10] = 0;   // deflate
        header[11] = 0;   // adaptive filtering
        header[12] = 0;   // no interlace

        output.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        WriteChunk(output, "IHDR"u8, header);
        WriteChunk(output, "IDAT"u8, compressed.GetBuffer().AsSpan(0, (int)compressed.Length));
        WriteChunk(output, "IEND"u8, []);
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> payload)
    {
        Span<byte> word = stackalloc byte[4];

        BinaryPrimitives.WriteInt32BigEndian(word, payload.Length);
        output.Write(word);
        output.Write(type);
        output.Write(payload);

        var crc = Crc32.Update(Crc32.Update(0xFFFFFFFFu, type), payload) ^ 0xFFFFFFFFu;
        BinaryPrimitives.WriteUInt32BigEndian(word, crc);
        output.Write(word);
    }

    /// <summary>The CRC-32 PNG chunks carry (the same polynomial as zip).</summary>
    private static class Crc32
    {
        private static readonly uint[] Table = Build();

        private static uint[] Build()
        {
            var table = new uint[256];
            for (var n = 0u; n < 256; n++)
            {
                var c = n;
                for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[n] = c;
            }

            return table;
        }

        public static uint Update(uint crc, ReadOnlySpan<byte> data)
        {
            foreach (var b in data) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc;
        }
    }
}
