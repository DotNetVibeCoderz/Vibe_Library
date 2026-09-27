using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace ScrapyNet.AI;

/// <summary>Exact content fingerprints: SHA-256 of normalized text, so whitespace or quote-style changes don't count as edits.</summary>
public static class ContentHasher
{
    public static string Hash(string text)
    {
        var normalized = string.Join(' ', TextNormalizer.Tokenize(TextNormalizer.Normalize(text)));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }
}

/// <summary>
/// 64-bit SimHash (Charikar) over word bigrams. Near-identical texts get fingerprints a few bits
/// apart; on article-length text a copy with a line added lands within ~4 bits while unrelated
/// articles sit 20+ bits apart, which is why <see cref="NearDuplicateIndex"/> defaults to 7.
/// </summary>
public static class SimHash
{
    public static ulong Compute(string text, int shingleSize = 2)
    {
        var tokens = TextNormalizer.Tokenize(text).ToArray();
        if (tokens.Length == 0) return 0;
        Span<int> weights = stackalloc int[64];
        var n = Math.Min(shingleSize, tokens.Length);
        for (var i = 0; i + n <= tokens.Length; i++)
        {
            var hash = Fnv1a64(tokens, i, n);
            for (var bit = 0; bit < 64; bit++) weights[bit] += ((hash >> bit) & 1) == 1 ? 1 : -1;
        }
        ulong fingerprint = 0;
        for (var bit = 0; bit < 64; bit++)
            if (weights[bit] > 0) fingerprint |= 1UL << bit;
        return fingerprint;
    }

    /// <summary>Hamming distance between two fingerprints (0 = identical).</summary>
    public static int Distance(ulong a, ulong b) => BitOperations.PopCount(a ^ b);

    /// <summary>Similarity in [0, 1].</summary>
    public static double Similarity(ulong a, ulong b) => 1 - Distance(a, b) / 64.0;

    private static ulong Fnv1a64(string[] tokens, int start, int count)
    {
        var hash = 14695981039346656037UL;
        for (var t = start; t < start + count; t++)
        {
            foreach (var c in tokens[t])
            {
                hash ^= c;
                hash *= 1099511628211UL;
            }
            hash ^= ' ';
            hash *= 1099511628211UL;
        }
        // Final avalanche so neighbouring shingles don't correlate bit-wise.
        hash ^= hash >> 33;
        hash *= 0xff51afd7ed558ccdUL;
        hash ^= hash >> 33;
        return hash;
    }
}

/// <summary>
/// Finds near-duplicates among many documents in sub-linear time: fingerprints are split into
/// <c>maxDistance + 1</c> bands, and only documents sharing a band exactly are compared. By pigeonhole, two
/// fingerprints at most <c>maxDistance</c> bits apart must agree on at least one band. The default of 7
/// bits suits article-length text; short snippets differ more and may need a larger value.
/// </summary>
public sealed class NearDuplicateIndex
{
    private readonly Dictionary<(int Band, ulong Value), List<(string Id, ulong Hash)>> _bands = [];
    private readonly int _maxDistance;
    private readonly int _bandCount;
    private readonly int _bandBits;

    public NearDuplicateIndex(int maxDistance = 7)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxDistance);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxDistance, 31);
        _maxDistance = maxDistance;
        _bandCount = maxDistance + 1;
        _bandBits = 64 / _bandCount;
    }

    public int MaxDistance => _maxDistance;

    private ulong Band(ulong hash, int band) => _bandBits >= 64 ? hash : (hash >> (band * _bandBits)) & ((1UL << _bandBits) - 1);

    private readonly object _gate = new();

    public int Count { get; private set; }

    /// <summary>Adds a document; returns the id of an existing near-duplicate (and does not add), or <c>null</c>.</summary>
    public string? AddOrFindDuplicate(string id, string text) => AddOrFindDuplicate(id, SimHash.Compute(text));

    public string? AddOrFindDuplicate(string id, ulong hash)
    {
        lock (_gate)
        {
            if (FindDuplicate(hash) is { } existing) return existing;
            for (var band = 0; band < _bandCount; band++)
            {
                var key = (band, Band(hash, band));
                if (!_bands.TryGetValue(key, out var list)) _bands[key] = list = [];
                list.Add((id, hash));
            }
            Count++;
            return null;
        }
    }

    public string? FindDuplicate(ulong hash)
    {
        lock (_gate)
        {
            for (var band = 0; band < _bandCount; band++)
            {
                if (!_bands.TryGetValue((band, Band(hash, band)), out var list)) continue;
                foreach (var (id, other) in list)
                    if (SimHash.Distance(hash, other) <= _maxDistance) return id;
            }
            return null;
        }
    }
}
