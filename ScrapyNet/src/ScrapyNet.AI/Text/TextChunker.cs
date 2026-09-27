using System.Text;

namespace ScrapyNet.AI;

/// <summary>How <see cref="TextChunker"/> splits text.</summary>
public enum ChunkStrategy
{
    /// <summary>Pack whole paragraphs, falling back to sentences for long ones (best default for prose).</summary>
    Paragraph,

    /// <summary>Pack sentences.</summary>
    Sentence,

    /// <summary>Fixed windows of words, ignoring structure.</summary>
    FixedWords,

    /// <summary>Split at Markdown headings first, then by paragraph; each chunk carries its heading path.</summary>
    Markdown,
}

public sealed record ChunkingOptions
{
    /// <summary>Target maximum chunk size in words (≈ 0.75 tokens each for English).</summary>
    public int MaxWords { get; init; } = 200;

    /// <summary>Words repeated from the end of the previous chunk, so answers spanning a boundary survive.</summary>
    public int OverlapWords { get; init; } = 30;

    public ChunkStrategy Strategy { get; init; } = ChunkStrategy.Paragraph;

    /// <summary>Chunks shorter than this (in words) are merged into their neighbour.</summary>
    public int MinWords { get; init; } = 20;
}

/// <summary>A piece of a document sized for embedding.</summary>
public sealed record TextChunk(int Index, string Text, int WordCount)
{
    /// <summary>Markdown heading path, e.g. "Install › Windows".</summary>
    public string? Section { get; init; }
}

/// <summary>Splits documents into overlapping chunks for embedding and retrieval.</summary>
public static class TextChunker
{
    public static List<TextChunk> Chunk(string text, ChunkingOptions? options = null)
    {
        options ??= new ChunkingOptions();
        if (string.IsNullOrWhiteSpace(text)) return [];
        return options.Strategy switch
        {
            ChunkStrategy.FixedWords => Fixed(text, options),
            ChunkStrategy.Sentence => Pack(TextNormalizer.SplitSentences(TextUtils.NormalizeWhitespace(text)), options, null, 0),
            ChunkStrategy.Markdown => Markdown(text, options),
            _ => Pack(Units(text, options), options, null, 0),
        };
    }

    private static List<string> Units(string text, ChunkingOptions options)
    {
        var units = new List<string>();
        foreach (var paragraph in text.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var p = TextUtils.NormalizeWhitespace(paragraph);
            if (p.Length == 0) continue;
            if (TextNormalizer.CountWords(p) <= options.MaxWords) units.Add(p);
            else units.AddRange(TextNormalizer.SplitSentences(p));
        }
        return units;
    }

    private static List<TextChunk> Pack(List<string> units, ChunkingOptions options, string? section, int startIndex)
    {
        var chunks = new List<TextChunk>();
        var current = new List<string>();
        var words = 0;
        var hasNew = false;

        void Flush()
        {
            if (!hasNew) return;
            var body = string.Join(" ", current);
            chunks.Add(new TextChunk(startIndex + chunks.Count, body, TextNormalizer.CountWords(body)) { Section = section });
            // The next chunk starts with the tail of this one.
            var overlap = TailWords(body, options.OverlapWords);
            current = overlap.Length > 0 ? [overlap] : [];
            words = TextNormalizer.CountWords(overlap);
            hasNew = false;
        }

        // A single sentence longer than a chunk is cut into word windows that fit beside the overlap.
        var pieceSize = Math.Max(1, options.MaxWords - options.OverlapWords);
        var expanded = units.SelectMany(u => TextNormalizer.CountWords(u) > options.MaxWords
            ? Fixed(u, options with { MaxWords = pieceSize, OverlapWords = 0 }).Select(c => c.Text)
            : [u]);

        foreach (var unit in expanded)
        {
            var w = TextNormalizer.CountWords(unit);
            if (words + w > options.MaxWords && hasNew) Flush();
            current.Add(unit);
            words += w;
            hasNew = true;
        }
        Flush();

        // Merge a runt last chunk into its predecessor.
        if (chunks.Count > 1 && chunks[^1].WordCount < options.MinWords)
        {
            var last = chunks[^1];
            var prev = chunks[^2];
            var merged = prev.Text + " " + last.Text[Math.Min(last.Text.Length, TailWords(prev.Text, options.OverlapWords).Length)..];
            chunks[^2] = prev with { Text = merged.Trim(), WordCount = TextNormalizer.CountWords(merged) };
            chunks.RemoveAt(chunks.Count - 1);
        }
        return chunks;
    }

    private static List<TextChunk> Fixed(string text, ChunkingOptions options)
    {
        var words = TextUtils.NormalizeWhitespace(text).Split(' ');
        var chunks = new List<TextChunk>();
        var step = Math.Max(1, options.MaxWords - options.OverlapWords);
        for (var start = 0; start < words.Length; start += step)
        {
            var count = Math.Min(options.MaxWords, words.Length - start);
            chunks.Add(new TextChunk(chunks.Count, string.Join(' ', words, start, count), count));
            if (start + count >= words.Length) break;
        }
        return chunks;
    }

    private static List<TextChunk> Markdown(string text, ChunkingOptions options)
    {
        var chunks = new List<TextChunk>();
        var path = new List<(int Level, string Title)>();
        var body = new StringBuilder();

        void Emit()
        {
            var content = body.ToString().Trim();
            body.Clear();
            if (content.Length == 0) return;
            var section = path.Count == 0 ? null : string.Join(" › ", path.Select(p => p.Title));
            chunks.AddRange(Pack(Units(content, options), options, section, chunks.Count));
        }

        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            var trimmed = line.TrimStart();
            var level = trimmed.TakeWhile(c => c == '#').Count();
            if (level is > 0 and <= 6 && trimmed.Length > level && trimmed[level] == ' ')
            {
                Emit();
                path.RemoveAll(p => p.Level >= level);
                path.Add((level, trimmed[(level + 1)..].Trim()));
                continue;
            }
            body.Append(line).Append('\n');
        }
        Emit();
        return chunks;
    }

    private static string TailWords(string text, int count)
    {
        if (count <= 0) return "";
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length <= count ? "" : string.Join(' ', words[^count..]);
    }
}
