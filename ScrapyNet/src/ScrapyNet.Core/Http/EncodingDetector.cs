using System.Text;

namespace ScrapyNet;

/// <summary>Where to look for an in-document encoding declaration.</summary>
public enum DeclarationKind
{
    None,
    Html,
    Xml,
}

/// <summary>
/// Response encoding detection in the order browsers use: byte-order mark, Content-Type charset,
/// in-document declaration, then UTF-8 with a Windows-1252 fallback for bytes that are not valid UTF-8.
/// </summary>
public static class EncodingDetector
{
    /// <summary>UTF-8 byte-order mark. (A <c>"\xEF..."u8</c> literal would encode U+00EF as two bytes.)</summary>
    internal static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    static EncodingDetector()
    {
        // Legacy code pages (windows-1252, shift_jis, gb2312, ...) ship with the runtime but are not
        // registered by default.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static Encoding Detect(string? contentType, ReadOnlySpan<byte> body, DeclarationKind kind = DeclarationKind.Html)
    {
        var bom = FromBom(body, out _);
        if (bom is not null) return bom;

        var fromHeader = FromContentType(contentType);
        if (fromHeader is not null) return fromHeader;

        var declared = kind switch
        {
            DeclarationKind.Html => FromHtmlMeta(body),
            DeclarationKind.Xml => FromXmlDeclaration(body),
            _ => null,
        };
        if (declared is not null) return declared;

        return IsValidUtf8(body) ? Encoding.UTF8 : GetEncoding("windows-1252") ?? Encoding.Latin1;
    }

    /// <summary>Decodes, skipping a BOM and never throwing on bad bytes.</summary>
    public static string Decode(byte[] body, Encoding encoding)
    {
        FromBom(body, out var bomLength);
        return encoding.GetString(body, bomLength, body.Length - bomLength);
    }

    public static Encoding? FromBom(ReadOnlySpan<byte> body, out int length)
    {
        length = 0;
        if (body.StartsWith(Utf8Bom)) { length = 3; return Encoding.UTF8; }
        if (body.Length >= 4 && body[0] == 0xFF && body[1] == 0xFE && body[2] == 0 && body[3] == 0) { length = 4; return Encoding.UTF32; }
        if (body.Length >= 2 && body[0] == 0xFF && body[1] == 0xFE) { length = 2; return Encoding.Unicode; }
        if (body.Length >= 2 && body[0] == 0xFE && body[1] == 0xFF) { length = 2; return Encoding.BigEndianUnicode; }
        return null;
    }

    public static Encoding? FromContentType(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType)) return null;
        var idx = contentType.IndexOf("charset=", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var value = contentType[(idx + 8)..].Trim().Trim('"', '\'');
        var end = value.IndexOfAny([';', ' ', ',']);
        if (end >= 0) value = value[..end];
        return GetEncoding(value);
    }

    public static Encoding? GetEncoding(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        name = name.Trim().ToLowerInvariant();
        // HTML5 says these labels mean windows-1252, which is a superset that browsers really use.
        if (name is "iso-8859-1" or "latin1" or "latin-1" or "ascii" or "us-ascii") name = "windows-1252";
        if (name is "utf8") name = "utf-8";
        try
        {
            var enc = Encoding.GetEncoding(name);
            return enc.CodePage == Encoding.UTF8.CodePage ? Encoding.UTF8 : enc;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static Encoding? FromHtmlMeta(ReadOnlySpan<byte> body)
    {
        var head = body[..Math.Min(body.Length, 4096)];
        var text = Encoding.ASCII.GetString(head);
        var idx = text.IndexOf("charset", StringComparison.OrdinalIgnoreCase);
        while (idx >= 0)
        {
            // Only inside a <meta> tag.
            var tagStart = text.LastIndexOf('<', idx);
            if (tagStart >= 0 && text.AsSpan(tagStart).StartsWith("<meta", StringComparison.OrdinalIgnoreCase))
            {
                var i = idx + 7;
                while (i < text.Length && (text[i] == ' ' || text[i] == '=' || text[i] == '"' || text[i] == '\'')) i++;
                var start = i;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '-' or '_' or ':' or '.')) i++;
                var enc = GetEncoding(text[start..i]);
                if (enc is not null) return enc;
            }
            idx = text.IndexOf("charset", idx + 7, StringComparison.OrdinalIgnoreCase);
        }
        return null;
    }

    private static Encoding? FromXmlDeclaration(ReadOnlySpan<byte> body)
    {
        var head = body[..Math.Min(body.Length, 256)];
        var text = Encoding.ASCII.GetString(head);
        if (!text.TrimStart().StartsWith("<?xml", StringComparison.Ordinal)) return null;
        var end = text.IndexOf("?>", StringComparison.Ordinal);
        if (end < 0) return null;
        var decl = text[..end];
        var idx = decl.IndexOf("encoding", StringComparison.Ordinal);
        if (idx < 0) return null;
        var q = decl.IndexOfAny(['"', '\''], idx);
        if (q < 0) return null;
        var q2 = decl.IndexOf(decl[q], q + 1);
        return q2 < 0 ? null : GetEncoding(decl[(q + 1)..q2]);
    }

    private static bool IsValidUtf8(ReadOnlySpan<byte> body) => System.Text.Unicode.Utf8.IsValid(body);

}
