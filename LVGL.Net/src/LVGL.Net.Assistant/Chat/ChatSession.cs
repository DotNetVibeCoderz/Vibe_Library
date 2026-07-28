using System.Text.Json.Serialization;

namespace Lvgl.Assistant.Chat;

/// <summary>Who produced a message.</summary>
public enum ChatAuthor
{
    User,
    Assistant,
    System,
}

/// <summary>What an attachment is, which decides how it reaches the model.</summary>
public enum AttachmentKind
{
    /// <summary>Sent to the model as image content it can actually see.</summary>
    Image,

    /// <summary>Referenced by URL in the message text; the model fetches it if it wants to.</summary>
    Document,
}

/// <summary>A file the user attached to a message.</summary>
/// <param name="Id">Stable id, also the stored file name.</param>
/// <param name="FileName">Original file name, shown in the UI.</param>
/// <param name="Kind">Image or document.</param>
/// <param name="MimeType">Content type, used for image blocks and when serving.</param>
/// <param name="Url">URL the file is served from.</param>
/// <param name="SizeBytes">Size on disk.</param>
public sealed record ChatAttachment(
    string Id,
    string FileName,
    AttachmentKind Kind,
    string MimeType,
    string Url,
    long SizeBytes)
{
    /// <summary>Path on disk. Not serialised - it is derived from the attachment directory.</summary>
    [JsonIgnore]
    public string? LocalPath { get; init; }
}

/// <summary>One turn in a conversation.</summary>
public sealed class ChatMessageRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");

    public ChatAuthor Author { get; set; }

    public string Text { get; set; } = string.Empty;

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;

    public List<ChatAttachment> Attachments { get; set; } = [];

    /// <summary>Names of tools invoked while producing this message, for display.</summary>
    public List<string> ToolCalls { get; set; } = [];

    /// <summary>Set when the message records a failure rather than a reply.</summary>
    public bool IsError { get; set; }

    /// <summary>Which model produced it, for assistant messages.</summary>
    public string? ModelId { get; set; }
}

/// <summary>
/// One conversation.
/// </summary>
/// <remarks>
/// Sessions are stored as individual JSON files rather than rows in a database: they are small,
/// a user may reasonably want to copy or delete one by hand, and it keeps the designer free of a
/// storage dependency.
/// </remarks>
public sealed class ChatSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");

    /// <summary>Title shown in the session list. Derived from the first message until renamed.</summary>
    public string Title { get; set; } = "New chat";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>Provider this session talks to; lets different sessions use different models.</summary>
    public AssistantProvider Provider { get; set; } = AssistantProvider.OpenAI;

    /// <summary>Model id in use for this session.</summary>
    public string ModelId { get; set; } = string.Empty;

    /// <summary>
    /// Per-session persona override. Null uses the one from app.config.
    /// </summary>
    public string? SystemPrompt { get; set; }

    public List<ChatMessageRecord> Messages { get; set; } = [];

    /// <summary>True when nothing has been said yet.</summary>
    [JsonIgnore]
    public bool IsEmpty => Messages.Count == 0;

    /// <summary>
    /// Clears the transcript but keeps the session, its title and its provider - the "reset"
    /// action, as distinct from deleting the session outright.
    /// </summary>
    public void Reset()
    {
        Messages.Clear();
        UpdatedAt = DateTimeOffset.Now;
    }

    /// <summary>
    /// The title, so a session shown in a list or a combo box reads as its name rather than as a
    /// type name when no display template is applied.
    /// </summary>
    public override string ToString() => Title;

    /// <summary>
    /// Names the session after its first user message, so the list is readable without the user
    /// having to title anything.
    /// </summary>
    public void UpdateTitleFromFirstMessage()
    {
        if (!Title.Equals("New chat", StringComparison.Ordinal)) return;

        var first = Messages.FirstOrDefault(m => m.Author == ChatAuthor.User);
        if (first is null || string.IsNullOrWhiteSpace(first.Text)) return;

        var text = first.Text.ReplaceLineEndings(" ").Trim();
        Title = text.Length <= 48 ? text : text[..48].TrimEnd() + "...";
    }
}
