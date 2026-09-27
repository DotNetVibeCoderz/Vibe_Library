using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace ScrapyNet;

/// <summary>
/// A 160-bit request fingerprint (SHA-1), stored as three machine words so a million seen requests
/// cost ~24 MB in a <see cref="HashSet{T}"/> instead of a million 40-character hex strings.
/// </summary>
public readonly record struct RequestFingerprint(ulong A, ulong B, uint C)
{
    public static RequestFingerprint FromBytes(ReadOnlySpan<byte> hash) =>
        new(BinaryPrimitives.ReadUInt64BigEndian(hash), BinaryPrimitives.ReadUInt64BigEndian(hash[8..]), BinaryPrimitives.ReadUInt32BigEndian(hash[16..]));

    public string ToHex()
    {
        Span<byte> bytes = stackalloc byte[20];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, A);
        BinaryPrimitives.WriteUInt64BigEndian(bytes[8..], B);
        BinaryPrimitives.WriteUInt32BigEndian(bytes[16..], C);
        return Convert.ToHexStringLower(bytes);
    }

    public static bool TryParse(ReadOnlySpan<char> hex, out RequestFingerprint fingerprint)
    {
        fingerprint = default;
        if (hex.Length != 40) return false;
        Span<byte> bytes = stackalloc byte[20];
        if (Convert.FromHexString(hex, bytes, out _, out var written) != OperationStatus.Done || written != 20) return false;
        fingerprint = FromBytes(bytes);
        return true;
    }

    public override string ToString() => ToHex();
}

/// <summary>
/// Computes request fingerprints: SHA-1 over the method, the canonical URL and the body, plus any
/// headers listed in <c>REQUEST_FINGERPRINTER_INCLUDE_HEADERS</c>. Two requests with the same
/// fingerprint are duplicates. Port of Scrapy's default <c>RequestFingerprinter</c>.
/// </summary>
public class RequestFingerprinter(IReadOnlyList<string>? includeHeaders = null)
{
    private readonly string[] _headers = includeHeaders is null ? [] : [.. includeHeaders.Select(h => h.ToLowerInvariant()).Order(StringComparer.Ordinal)];

    /// <summary>Fingerprint of the request, computed once and cached on the request.</summary>
    public RequestFingerprint Fingerprint(Request request)
    {
        if (request.CachedFingerprint is { } cached) return cached;
        var fp = Compute(request);
        request.CachedFingerprint = fp;
        return fp;
    }

    protected virtual RequestFingerprint Compute(Request request)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        AppendString(hash, request.Method);
        hash.AppendData([0]);
        AppendString(hash, UrlUtils.Canonicalize(request.Url));
        hash.AppendData([0]);
        hash.AppendData(request.Body);
        foreach (var name in _headers)
        {
            hash.AppendData([0]);
            AppendString(hash, name);
            foreach (var value in request.Headers.GetValues(name))
            {
                hash.AppendData([(byte)':']);
                AppendString(hash, value);
            }
        }
        Span<byte> digest = stackalloc byte[20];
        hash.GetHashAndReset(digest);
        return RequestFingerprint.FromBytes(digest);
    }

    private static void AppendString(IncrementalHash hash, string value)
    {
        var max = Encoding.UTF8.GetMaxByteCount(value.Length);
        if (max <= 512)
        {
            Span<byte> buffer = stackalloc byte[max];
            var n = Encoding.UTF8.GetBytes(value, buffer);
            hash.AppendData(buffer[..n]);
        }
        else
        {
            var rented = ArrayPool<byte>.Shared.Rent(max);
            try
            {
                var n = Encoding.UTF8.GetBytes(value, rented);
                hash.AppendData(rented, 0, n);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }
}
