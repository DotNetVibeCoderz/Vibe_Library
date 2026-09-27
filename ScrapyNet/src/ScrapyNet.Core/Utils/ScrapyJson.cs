using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace ScrapyNet;

/// <summary>
/// JSON options used throughout Scrapy.Net. They carry an explicit reflection-based type resolver, so
/// serialization keeps working where the runtime default disables reflection — notably .NET 10
/// file-based apps (<c>dotnet run app.cs</c>), which default to AOT-compatible settings.
/// </summary>
public static class ScrapyJson
{
    public static readonly JsonSerializerOptions Default = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static readonly JsonSerializerOptions Indented = new(Default) { WriteIndented = true };

    /// <summary>camelCase, case-insensitive (ASP.NET-style) options.</summary>
    public static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
