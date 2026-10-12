using System.Text;
using ScrapyNet.Engine;
using Xunit;

namespace ScrapyNet.Tests;

public class DiskQueueTests
{
    private sealed class IdleSpider : Spider
    {
        public override string Name => "idle";
    }

    private static Settings JobSettings(string jobDir)
    {
        var s = new Settings();
        TestSettings.Quiet(s);
        s.Set(SettingKeys.JobDir, jobDir);
        return s;
    }

    private static string NewPath() => Path.Combine(TestSettings.TempDir(), "requests.queue.log");

    [Fact]
    public void Journal_replays_pushes_that_were_never_popped()
    {
        var path = NewPath();
        var journal = DiskRequestJournal.Open(path, out var pending);
        Assert.Empty(pending);
        var a = journal.Push(0, "{\"url\":\"a\"}");
        journal.Push(5, "{\"url\":\"b\"}");
        journal.Push(-1, "{\"url\":\"c\"}");
        Assert.Equal("{\"url\":\"a\"}", journal.Pop(a.Offset, a.Length));
        // No orderly close: only what reached the OS survives, as after a kill.
        journal.Flush();
        journal.Dispose();

        using var reopened = DiskRequestJournal.Open(path, out pending);
        Assert.Equal([5, -1], pending.Select(p => p.Priority));
        Assert.Equal("{\"url\":\"b\"}", reopened.Pop(pending[0].Offset, pending[0].Length));
    }

    [Fact]
    public void Journal_ignores_a_torn_last_line()
    {
        var path = NewPath();
        using (var journal = DiskRequestJournal.Open(path, out _))
        {
            journal.Push(0, "{\"url\":\"a\"}");
            journal.Flush();
        }
        File.AppendAllText(path, "+0\t{\"url\":\"half", Encoding.UTF8);
        using var reopened = DiskRequestJournal.Open(path, out var pending);
        Assert.Single(pending);
    }

    [Fact]
    public void Journal_compaction_keeps_live_entries_in_order()
    {
        var path = NewPath();
        using var journal = DiskRequestJournal.Open(path, out _);
        var entries = Enumerable.Range(0, 100).Select(i => (Priority: i % 3, Pos: journal.Push(i % 3, $"{{\"url\":\"u{i}\"}}"))).ToList();
        foreach (var e in entries.Take(60)) journal.Pop(e.Pos.Offset, e.Pos.Length);
        var live = entries.Skip(60).Select(e => (e.Priority, e.Pos.Offset, e.Pos.Length)).ToList();

        var before = journal.Length;
        var moved = journal.Compact(live);
        Assert.True(journal.Length < before / 2);
        Assert.Equal("{\"url\":\"u60\"}", journal.Pop(moved[0].Offset, moved[0].Length));
        Assert.Equal("{\"url\":\"u99\"}", journal.Pop(moved[^1].Offset, moved[^1].Length));
    }

    [Fact]
    public async Task Scheduler_with_jobdir_keeps_requests_on_disk_and_lambdas_in_memory()
    {
        var jobDir = TestSettings.TempDir();
        var spider = new IdleSpider();
        var crawler = new Crawler(spider, JobSettings(jobDir));
        var scheduler = new Scheduler(crawler);
        await scheduler.OpenAsync(spider);

        Assert.True(scheduler.Enqueue(new Request("https://example.com/low") { Priority = 0 }));
        Assert.True(scheduler.Enqueue(new Request("https://example.com/high") { Priority = 10 }));
        Assert.True(scheduler.Enqueue(new Request("https://example.com/lambda", _ => AsyncEnumerable.Empty<object>())));
        Assert.False(scheduler.Enqueue(new Request("https://example.com/low")));

        Assert.Equal(2L, crawler.Stats.GetValue("scheduler/enqueued/disk"));
        Assert.Equal(1L, crawler.Stats.GetValue("scheduler/enqueued/memory"));
        Assert.Equal("https://example.com/high", scheduler.Dequeue()!.Url);
        await scheduler.CloseAsync("shutdown");

        // A new scheduler over the same JOBDIR picks up the one persisted request still pending.
        var crawler2 = new Crawler(spider, JobSettings(jobDir));
        var resumed = new Scheduler(crawler2);
        await resumed.OpenAsync(spider);
        Assert.Equal(1, resumed.Count);
        Assert.Equal("https://example.com/low", resumed.Dequeue()!.Url);
        Assert.Null(resumed.Dequeue());
        await resumed.CloseAsync("finished");
        Assert.False(File.Exists(Path.Combine(jobDir, "requests.queue.log")));
    }
}
