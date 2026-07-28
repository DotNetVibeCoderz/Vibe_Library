using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lvgl.Ui;

/// <summary>
/// Loads and saves <see cref="UiDocument"/> files (<c>*.lvgl.json</c>).
/// </summary>
/// <remarks>
/// Serialisation goes through a source-generated context rather than reflection so the designer and
/// any AOT-published tool share the same code path and no trimming configuration is required.
/// </remarks>
public static class UiJson
{
    /// <summary>Conventional file extension for saved layouts.</summary>
    public const string FileExtension = ".lvgl.json";

    /// <summary>Reads a document from a JSON string.</summary>
    public static UiDocument Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        var document = JsonSerializer.Deserialize(json, UiJsonContext.Default.UiDocument)
            ?? throw new InvalidDataException("The file does not contain a UI document.");

        if (document.Version > 1)
        {
            throw new InvalidDataException(
                $"The document was written by a newer version of the designer (format {document.Version}).");
        }

        return document;
    }

    /// <summary>Serialises a document to an indented JSON string.</summary>
    public static string ToJson(UiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return JsonSerializer.Serialize(document, UiJsonContext.Default.UiDocument);
    }

    /// <summary>Reads a document from disk.</summary>
    public static UiDocument Load(string path) => Parse(File.ReadAllText(path));

    /// <summary>Writes a document to disk.</summary>
    public static void Save(UiDocument document, string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        File.WriteAllText(path, ToJson(document));
    }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(UiDocument))]
internal sealed partial class UiJsonContext : JsonSerializerContext;
