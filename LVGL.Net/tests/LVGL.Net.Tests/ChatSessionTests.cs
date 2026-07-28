using Lvgl.Assistant;
using Lvgl.Assistant.Chat;

namespace Lvgl.Tests;

/// <summary>Session model and on-disk store.</summary>
public sealed class ChatSessionTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "lvglnet-tests",
        Guid.NewGuid().ToString("n"));

    private ChatSessionStore NewStore() => new(_directory);

    [Fact]
    public void A_session_round_trips_through_the_store()
    {
        var store = NewStore();

        var session = store.Create(AssistantProvider.Anthropic, "claude-opus-5", "Layout work");
        session.Messages.Add(new ChatMessageRecord { Author = ChatAuthor.User, Text = "hello" });
        session.Messages.Add(new ChatMessageRecord
        {
            Author = ChatAuthor.Assistant,
            Text = "hi",
            ToolCalls = ["lvgl_design-create_layout"],
            ModelId = "claude-opus-5",
        });
        store.Save(session);

        var restored = NewStore().LoadAll().Single();

        Assert.Equal(session.Id, restored.Id);
        Assert.Equal("Layout work", restored.Title);
        Assert.Equal(AssistantProvider.Anthropic, restored.Provider);
        Assert.Equal(2, restored.Messages.Count);
        Assert.Equal(ChatAuthor.Assistant, restored.Messages[1].Author);
        Assert.Equal("lvgl_design-create_layout", restored.Messages[1].ToolCalls.Single());
    }

    [Fact]
    public void Attachments_survive_the_round_trip()
    {
        var store = NewStore();
        var session = store.Create(AssistantProvider.OpenAI, "gpt-4o");

        session.Messages.Add(new ChatMessageRecord
        {
            Author = ChatAuthor.User,
            Text = "look at this",
            Attachments =
            [
                new ChatAttachment("abc.png", "mockup.png", AttachmentKind.Image, "image/png", "http://127.0.0.1:8080/abc.png", 1234),
            ],
        });

        store.Save(session);

        var attachment = NewStore().LoadAll().Single().Messages.Single().Attachments.Single();

        Assert.Equal("mockup.png", attachment.FileName);
        Assert.Equal(AttachmentKind.Image, attachment.Kind);
        Assert.Equal(1234, attachment.SizeBytes);
    }

    [Fact]
    public void Sessions_are_listed_most_recently_updated_first()
    {
        var store = NewStore();

        var older = store.Create(AssistantProvider.OpenAI, "gpt-4o", "Older");
        Thread.Sleep(15);
        var newer = store.Create(AssistantProvider.OpenAI, "gpt-4o", "Newer");

        var listed = store.LoadAll();

        Assert.Equal(newer.Id, listed[0].Id);
        Assert.Equal(older.Id, listed[1].Id);
    }

    [Fact]
    public void Delete_removes_the_session_and_reports_whether_it_existed()
    {
        var store = NewStore();
        var session = store.Create(AssistantProvider.OpenAI, "gpt-4o");

        Assert.True(store.Delete(session.Id));
        Assert.False(store.Delete(session.Id));
        Assert.Empty(store.LoadAll());
    }

    [Fact]
    public void Reset_clears_the_transcript_but_keeps_the_session()
    {
        var session = new ChatSession { Title = "Kept", Provider = AssistantProvider.Gemini };
        session.Messages.Add(new ChatMessageRecord { Author = ChatAuthor.User, Text = "hello" });

        session.Reset();

        Assert.Empty(session.Messages);
        Assert.Equal("Kept", session.Title);
        Assert.Equal(AssistantProvider.Gemini, session.Provider);
    }

    [Fact]
    public void The_title_is_taken_from_the_first_user_message()
    {
        var session = new ChatSession();
        session.Messages.Add(new ChatMessageRecord { Author = ChatAuthor.User, Text = "Design a dashboard for a Pi" });

        session.UpdateTitleFromFirstMessage();

        Assert.Equal("Design a dashboard for a Pi", session.Title);
    }

    [Fact]
    public void A_long_first_message_is_truncated_for_the_title()
    {
        var session = new ChatSession();
        session.Messages.Add(new ChatMessageRecord { Author = ChatAuthor.User, Text = new string('x', 200) });

        session.UpdateTitleFromFirstMessage();

        Assert.True(session.Title.Length <= 52);
        Assert.EndsWith("...", session.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void A_title_the_user_set_is_not_overwritten()
    {
        var session = new ChatSession { Title = "My layout work" };
        session.Messages.Add(new ChatMessageRecord { Author = ChatAuthor.User, Text = "something else" });

        session.UpdateTitleFromFirstMessage();

        Assert.Equal("My layout work", session.Title);
    }

    [Fact]
    public void An_unreadable_file_is_skipped_rather_than_failing_the_whole_list()
    {
        var store = NewStore();
        store.Create(AssistantProvider.OpenAI, "gpt-4o", "Good");

        // Losing one corrupt conversation is recoverable; losing the list is not.
        File.WriteAllText(Path.Combine(_directory, "broken.session.json"), "{ not json");

        var listed = store.LoadAll();

        Assert.Single(listed);
        Assert.Equal("Good", listed[0].Title);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
