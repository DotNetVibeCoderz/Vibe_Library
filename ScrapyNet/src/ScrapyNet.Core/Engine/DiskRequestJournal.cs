using System.Buffers;
using System.Globalization;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ScrapyNet.Engine;

/// <summary>
/// Append-only journal behind the <c>JOBDIR</c> scheduler queue (<c>requests.queue.log</c>). Every queued
/// request is written as it is scheduled, so a crash or a kill loses at most the last second of
/// scheduling; the scheduler keeps only offsets in memory, so a million-URL frontier costs a few dozen
/// bytes per pending request instead of a full <see cref="Request"/>.
/// </summary>
/// <remarks>
/// Lines are <c>+{priority}\t{request json}</c> for a push and <c>-{offset}</c> for a pop of the push at
/// that byte offset. Replaying the file yields the pending set. Popped space is reclaimed by rewriting the
/// file when more than half of it is dead.
/// </remarks>
internal sealed class DiskRequestJournal : IDisposable
{
    private const long CompactMinBytes = 4 * 1024 * 1024;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(1);

    private readonly string _path;
    private FileStream _writer;
    private SafeFileHandle _reader;
    private long _length;
    private long _flushed;
    private long _deadBytes;
    private long _lastFlush = Environment.TickCount64;

    private DiskRequestJournal(string path, FileStream writer, SafeFileHandle reader, long length)
    {
        _path = path;
        _writer = writer;
        _reader = reader;
        _length = length;
        _flushed = length;
    }

    public string Path => _path;

    /// <summary>Bytes in the file, live or dead.</summary>
    public long Length => _length;

    /// <summary>Opens (or creates) the journal and returns the pushes that were never popped, in file order.</summary>
    public static DiskRequestJournal Open(string path, out List<(int Priority, long Offset, int Length)> pending)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        pending = [];
        if (File.Exists(path)) pending = Replay(path);
        var writer = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 64 * 1024);
        var reader = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var journal = new DiskRequestJournal(path, writer, reader, writer.Length);
        var live = pending.Sum(p => (long)p.Length);
        journal._deadBytes = journal._length - live;
        return journal;
    }

    private static List<(int Priority, long Offset, int Length)> Replay(string path)
    {
        var pushes = new Dictionary<long, (int Priority, int Length, long Order)>();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024);
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        long order = 0;
        var offset = 0;
        while (offset < bytes.Length)
        {
            var end = Array.IndexOf(bytes, (byte)'\n', offset);
            // A line without its newline is a torn write from a crash: ignore it.
            if (end < 0) break;
            var line = bytes.AsSpan(offset, end - offset);
            if (line.Length > 1 && line[0] == (byte)'+')
            {
                var tab = line.IndexOf((byte)'\t');
                if (tab > 1 && int.TryParse(Encoding.ASCII.GetString(line[1..tab]), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var priority))
                    pushes[offset] = (priority, end - offset + 1, order++);
            }
            else if (line.Length > 1 && line[0] == (byte)'-' &&
                     long.TryParse(Encoding.ASCII.GetString(line[1..]), NumberStyles.None, CultureInfo.InvariantCulture, out var popped))
            {
                pushes.Remove(popped);
            }
            offset = end + 1;
        }
        return [.. pushes.OrderBy(p => p.Value.Order).Select(p => (p.Value.Priority, p.Key, p.Value.Length))];
    }

    /// <summary>Appends a request line and returns where it went.</summary>
    public (long Offset, int Length) Push(int priority, string json)
    {
        var line = string.Concat("+", priority.ToString(CultureInfo.InvariantCulture), "\t", json, "\n");
        var count = Encoding.UTF8.GetByteCount(line);
        var buffer = ArrayPool<byte>.Shared.Rent(count);
        try
        {
            Encoding.UTF8.GetBytes(line, buffer);
            var offset = _length;
            _writer.Write(buffer, 0, count);
            _length += count;
            MaybeFlush();
            return (offset, count);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>Reads the request JSON stored at <paramref name="offset"/> and records the pop.</summary>
    public string Pop(long offset, int length)
    {
        var json = Read(offset, length);
        var marker = string.Concat("-", offset.ToString(CultureInfo.InvariantCulture), "\n");
        var bytes = Encoding.ASCII.GetBytes(marker);
        _writer.Write(bytes);
        _length += bytes.Length;
        _deadBytes += length + bytes.Length;
        MaybeFlush();
        return json;
    }

    private string Read(long offset, int length)
    {
        if (offset + length > _flushed) Flush();
        var buffer = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            var read = 0;
            while (read < length)
            {
                var n = RandomAccess.Read(_reader, buffer.AsSpan(read, length - read), offset + read);
                if (n == 0) throw new EndOfStreamException($"Journal {_path} is truncated at {offset + read}.");
                read += n;
            }
            var line = buffer.AsSpan(0, length);
            var tab = line.IndexOf((byte)'\t');
            // Skip "+priority\t" and the trailing newline.
            return Encoding.UTF8.GetString(line[(tab + 1)..^1]);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>True when more than half of a sizeable file is popped entries.</summary>
    public bool ShouldCompact => _length >= CompactMinBytes && _deadBytes * 2 > _length;

    /// <summary>
    /// Rewrites the file with only <paramref name="live"/> entries and returns their new locations, in the
    /// same order. Written to a temporary file and swapped in, so a crash mid-way keeps the old journal.
    /// </summary>
    public List<(long Offset, int Length)> Compact(IReadOnlyList<(int Priority, long Offset, int Length)> live)
    {
        Flush();
        var temp = _path + ".tmp";
        var moved = new List<(long, int)>(live.Count);
        using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024))
        {
            var buffer = new byte[64 * 1024];
            long position = 0;
            foreach (var (_, offset, length) in live)
            {
                if (buffer.Length < length) buffer = new byte[length];
                RandomAccess.Read(_reader, buffer.AsSpan(0, length), offset);
                output.Write(buffer, 0, length);
                moved.Add((position, length));
                position += length;
            }
        }
        _writer.Dispose();
        _reader.Dispose();
        File.Move(temp, _path, overwrite: true);
        _writer = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read, 64 * 1024);
        _reader = File.OpenHandle(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        _length = _flushed = _writer.Length;
        _deadBytes = 0;
        return moved;
    }

    private void MaybeFlush()
    {
        if (Environment.TickCount64 - _lastFlush >= FlushInterval.TotalMilliseconds) Flush();
    }

    public void Flush()
    {
        _writer.Flush();
        _flushed = _length;
        _lastFlush = Environment.TickCount64;
    }

    /// <summary>Closes the files and removes the journal (used when the crawl finished with nothing pending).</summary>
    public void Delete()
    {
        Dispose();
        File.Delete(_path);
    }

    public void Dispose()
    {
        _writer.Dispose();
        _reader.Dispose();
    }
}
