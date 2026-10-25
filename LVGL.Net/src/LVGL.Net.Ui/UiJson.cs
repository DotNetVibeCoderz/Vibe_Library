using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

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

        if (document.Version > UiDocument.CurrentVersion)
        {
            throw new InvalidDataException(
                $"The document was written by a newer version of the designer (format {document.Version}).");
        }

        // Version 1 is a strict subset of version 2, so migrating is only re-stamping. Done on
        // load so a re-save writes the current version and the file stays honest about its shape.
        if (document.Version < UiDocument.CurrentVersion) document.Version = UiDocument.CurrentVersion;

        return document;
    }

    /// <summary>
    /// Writing omits what was never set - null values, empty lists, false flags - so a saved layout
    /// holds only the decisions that were made, and a diff of it shows only real changes. Reading
    /// is unaffected: an omitted list comes back empty and an omitted flag false.
    /// </summary>
    private static readonly JsonTypeInfo<UiDocument> CompactDocument = (JsonTypeInfo<UiDocument>)new JsonSerializerOptions(UiJsonContext.Default.Options)
    {
        TypeInfoResolver = UiJsonContext.Default.WithAddedModifier(OmitDefaults),
    }.GetTypeInfo(typeof(UiDocument));

    private static void OmitDefaults(JsonTypeInfo info)
    {
        foreach (var property in info.Properties)
        {
            if (typeof(ICollection).IsAssignableFrom(property.PropertyType))
            {
                property.ShouldSerialize = static (_, value) => value is ICollection { Count: > 0 };
            }
            else if (property.PropertyType == typeof(bool))
            {
                property.ShouldSerialize = static (_, value) => value is true;
            }
        }
    }

    /// <summary>Serialises a document to an indented JSON string.</summary>
    public static string ToJson(UiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return JsonSerializer.Serialize(document, CompactDocument);
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
