using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lvgl.Assistant.Chat;

/// <summary>
/// Persists chat sessions as one JSON file each.
/// </summary>
/// <remarks>
/// Writes go through a temporary file and a move, so a crash mid-save leaves the previous session
/// intact rather than a truncated file. A corrupt or unreadable session is skipped on load rather
/// than taking the whole list down with it - losing one conversation is recoverable, losing the
/// list is not.
/// </remarks>
public sealed class ChatSessionStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _directory;

    public ChatSessionStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        _directory = directory;

        // Fully qualified: this class has a `Directory` property that would otherwise win.
        System.IO.Directory.CreateDirectory(_directory);
    }

    /// <summary>Where sessions are stored.</summary>
    public string Directory => _directory;

    /// <summary>Sessions on disk, most recently updated first.</summary>
    public IReadOnlyList<ChatSession> LoadAll()
    {
        var sessions = new List<ChatSession>();

        foreach (var path in System.IO.Directory.EnumerateFiles(_directory, "*.session.json"))
        {
            if (TryLoad(path, out var session)) sessions.Add(session);
        }

        return sessions.OrderByDescending(s => s.UpdatedAt).ToList();
    }

    /// <summary>Loads one session, returning false when the file is missing or unreadable.</summary>
    public bool TryLoad(string path, out ChatSession session)
    {
        session = null!;

        try
        {
            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<ChatSession>(json, Json);
            if (loaded is null) return false;

            session = loaded;
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Writes a session, replacing any previous copy.</summary>
    public void Save(ChatSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        session.UpdatedAt = DateTimeOffset.Now;

        var path = PathFor(session.Id);
        var temporary = path + ".tmp";

        // Write-then-move: a crash during the write cannot leave a half-written session behind.
        File.WriteAllText(temporary, JsonSerializer.Serialize(session, Json));
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>Deletes a session. Returns false when it was not there.</summary>
    public bool Delete(string sessionId)
    {
        var path = PathFor(sessionId);
        if (!File.Exists(path)) return false;

        File.Delete(path);
        return true;
    }

    /// <summary>Creates and persists a new session.</summary>
    public ChatSession Create(AssistantProvider provider, string modelId, string? title = null)
    {
        var session = new ChatSession
        {
            Provider = provider,
            ModelId = modelId,
        };

        if (!string.IsNullOrWhiteSpace(title)) session.Title = title;

        Save(session);
        return session;
    }

    private string PathFor(string sessionId) =>
        Path.Combine(_directory, $"{Sanitize(sessionId)}.session.json");

    /// <summary>
    /// Keeps a session id usable as a file name. Ids are generated, but a hand-edited file could
    /// carry anything, and this store must not be talked into writing outside its directory.
    /// </summary>
    private static string Sanitize(string id)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(id.Where(c => !invalid.Contains(c) && c != '.').ToArray());

        return cleaned.Length == 0 ? Guid.NewGuid().ToString("n") : cleaned;
    }
}
