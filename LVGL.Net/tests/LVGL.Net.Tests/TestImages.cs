using System.Buffers.Binary;
using System.IO.Compression;

namespace Lvgl.Tests;

/// <summary>
/// Writes small solid-colour PNG and BMP files, so the image tests exercise LVGL's real decoders
/// without binary fixtures checked into the repository.
/// </summary>
internal static class TestImages
{
    public static string WritePng(string path, int width, int height, uint rgb)
    {
        // Raw scanlines: a filter byte (0 = none) then RGB triples.
        var raw = new byte[height * (1 + width * 3)];
        for (var y = 0; y < height; y++)
        {
            var row = y * (1 + width * 3);
            for (var x = 0; x < width; x++)
            {
                var p = row + 1 + x * 3;
                raw[p] = (byte)(rgb >> 16);
                raw[p + 1] = (byte)(rgb >> 8);
                raw[p + 2] = (byte)rgb;
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;   // bit depth
        header[9] = 2;   // colour type: truecolour

        using var file = File.Create(path);
        file.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        WriteChunk(file, "IHDR", header);
        WriteChunk(file, "IDAT", compressed.ToArray());
        WriteChunk(file, "IEND", []);
        return path;
    }

    public static string WriteBmp(string path, int width, int height, uint rgb)
    {
        // 24-bit, bottom-up, rows padded to four bytes.
        var rowSize = (width * 3 + 3) & ~3;
        var pixelBytes = rowSize * height;
        var data = new byte[54 + pixelBytes];

        data[0] = (byte)'B';
        data[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(2), data.Length);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(10), 54);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(18), width);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(22), height);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(26), 1);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(28), 24);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(34), pixelBytes);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var p = 54 + y * rowSize + x * 3;
                data[p] = (byte)rgb;
                data[p + 1] = (byte)(rgb >> 8);
                data[p + 2] = (byte)(rgb >> 16);
            }
        }

        File.WriteAllBytes(path, data);
        return path;
    }

    private static void WriteChunk(Stream stream, string type, byte[] payload)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, payload.Length);
        stream.Write(length);

        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(payload);

        var crc = Crc32(typeBytes, payload);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        stream.Write(crcBytes);
    }

    private static uint Crc32(byte[] first, byte[] second)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in first) crc = Step(crc, b);
        foreach (var b in second) crc = Step(crc, b);
        return ~crc;

        static uint Step(uint crc, byte b)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            return crc;
        }
    }

    /// <summary>
    /// A TrueType font for the TinyTTF tests: the one in the LVGL checkout (present wherever the
    /// native library was built), else a common system font. Null when none can be found.
    /// </summary>
    public static string? FindTrueTypeFont()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "native", "lvgl", "examples", "libs", "tiny_ttf", "Ubuntu-Medium.ttf");
            if (File.Exists(candidate)) return candidate;
        }

        string[] system =
        [
            @"C:\Windows\Fonts\arial.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "/System/Library/Fonts/Supplemental/Arial.ttf",
        ];

        return system.FirstOrDefault(File.Exists);
    }
}
