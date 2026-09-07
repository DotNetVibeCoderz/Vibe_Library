// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ActorNet.Runtime;

namespace ActorNet.Tests;

/// <summary>
/// A second mailbox lane, for messages that cannot wait behind a backlog.
/// </summary>
/// <remarks>
/// The interesting cases are all about what happens when an actor is behind. An actor that is
/// keeping up has an empty mailbox, and every ordering rule in here is trivially satisfied by an
/// empty queue - so every test that means anything holds the actor still first.
/// </remarks>
public sealed class MailboxPriorityTests
{
    private static readonly ActorId Somewhere = ActorId.For<CounterActor>("mailbox");

    private static Envelope Ordinary(int n) => Envelope.Create(Somewhere, new Plod(n));

    private static Envelope Pressing(int n) => Envelope.Create(Somewhere, new Jump(n));

    private static object Read(IMailbox mailbox)
    {
        Assert.True(mailbox.TryRead(out var envelope), "the mailbox should have had something in it");
        return envelope.Message!;
    }

    [Fact]
    public void AnUrgentMessageOvertakesABacklog()
    {
        var mailbox = new ChannelMailbox();

        for (var i = 0; i < 20; i++) mailbox.TryPost(Ordinary(i));
        mailbox.TryPost(Pressing(1));

        // Posted last, read first. Nothing else in the design does this.
        Assert.Equal(new Jump(1), Read(mailbox));
        Assert.Equal(new Plod(0), Read(mailbox));
    }

    [Fact]
    public void OrderingHoldsWithinEachLane()
    {
        var mailbox = new ChannelMailbox();

        for (var i = 0; i < 5; i++)
        {
            mailbox.TryPost(Ordinary(i));
            mailbox.TryPost(Pressing(i));
        }

        // Overtaking is between the lanes, not inside one: two urgent messages from one sender are
        // still handled in the order they were sent, and so are two ordinary ones.
        var read = new List<object>();
        while (mailbox.TryRead(out var envelope)) read.Add(envelope.Message!);

        Assert.Equal(Enumerable.Range(0, 5).Select(i => new Jump(i)), read.OfType<Jump>());
        Assert.Equal(Enumerable.Range(0, 5).Select(i => new Plod(i)), read.OfType<Plod>());
    }

    [Fact]
    public void AFloodOfUrgentMessagesDoesNotStarveTheOrdinaryLane()
    {
        var mailbox = new ChannelMailbox();

        for (var i = 0; i < 20; i++) mailbox.TryPost(Ordinary(i));
        for (var i = 0; i < 20; i++) mailbox.TryPost(Pressing(i));

        var read = new List<object>();
        for (var i = 0; i < 10; i++) read.Add(Read(mailbox));

        // Eight urgent, then one ordinary, then urgent again. The exact number matters less than
        // the ninth read not being a ninth urgent message: under strict priority a sender that
        // keeps producing them stalls the ordinary lane indefinitely.
        Assert.Equal(Enumerable.Range(0, 8).Select(i => new Jump(i)), read.Take(8).Cast<Jump>());
        Assert.Equal(new Plod(0), read[8]);
        Assert.Equal(new Jump(8), read[9]);
    }

    [Fact]
    public void AMailboxThatNeverSeesAnUrgentMessageIsStrictlyFirstInFirstOut()
    {
        var mailbox = new ChannelMailbox();

        for (var i = 0; i < 10; i++) mailbox.TryPost(Ordinary(i));

        for (var i = 0; i < 10; i++) Assert.Equal(new Plod(i), Read(mailbox));
        Assert.False(mailbox.TryRead(out _));
    }

    [Fact]
    public async Task AReaderParkedOnAnEmptyMailboxWakesForAnUrgentMessage()
    {
        var mailbox = new ChannelMailbox();

        // The lane does not exist yet, so the reader is parked on the ordinary one - the only
        // channel there is. Nothing is ever written to it here.
        var waiting = mailbox.WaitToReadAsync(CancellationToken.None).AsTask();
        Assert.False(waiting.IsCompleted);

        mailbox.TryPost(Pressing(1));

        Assert.True(await waiting.WaitAsync(TimeSpan.FromSeconds(5)),
            "a reader parked before the urgent lane existed should still be woken by it");
        Assert.Equal(new Jump(1), Read(mailbox));
    }

    [Fact]
    public async Task ClosingAMailboxClosesBothLanes()
    {
        var mailbox = new ChannelMailbox();

        mailbox.TryPost(Ordinary(1));
        mailbox.TryPost(Pressing(1));
        mailbox.Complete();

        // Queued messages are still delivered - that is what makes a graceful stop graceful - and
        // then the wait reports the mailbox drained rather than hanging on the lane nobody closed.
        Assert.Equal(new Jump(1), Read(mailbox));
        Assert.Equal(new Plod(1), Read(mailbox));

        Assert.False(await mailbox.WaitToReadAsync(CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(mailbox.TryPost(Pressing(2)));
    }

    [Fact]
    public async Task AnUrgentMessageOvertakesInARunningActor()
    {
        await using var harness = new TestHarness();
        var system = await harness.LocalAsync();
        system.RegisterActor<QueueActor>();

        var id = ActorId.For<QueueActor>("overtaken");

        // Hold the actor still, so a backlog forms behind something that is genuinely running.
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await system.TellAsync(id, new Hold(started, release.Task));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        for (var i = 0; i < 20; i++) await system.TellAsync(id, new Plod(i));
        await system.TellAsync(id, new Jump(1));

        release.SetResult();

        var happened = await system.AskAsync<Happened>(id, new WhatHappened(), TimeSpan.FromSeconds(10));

        // Sent after twenty ordinary messages, handled before all of them.
        Assert.Equal("Jump 1", happened.Order[0]);
        Assert.Equal("Plod 0", happened.Order[1]);
        Assert.Equal(21, happened.Order.Count);
    }
}

/// <summary>Overtakes a backlog.</summary>
[Urgent]
public sealed record Jump(int N);

/// <summary>Waits its turn.</summary>
public sealed record Plod(int N);

/// <summary>Occupies the actor until the task completes, so a backlog can build behind it.</summary>
public sealed record Hold(TaskCompletionSource Started, Task Release);

/// <summary>Asks what was handled, in what order.</summary>
public sealed record WhatHappened();

/// <summary>The answer.</summary>
public sealed record Happened(IReadOnlyList<string> Order);

/// <summary>Records what it handled, in the order it handled it.</summary>
public sealed class QueueActor : ReceiveActor
{
    private readonly List<string> _order = [];

    public QueueActor()
    {
        On<Hold>(async (m, _) =>
        {
            m.Started.TrySetResult();
            await m.Release;
        });

        On<Plod>(m => _order.Add($"Plod {m.N}"));
        On<Jump>(m => _order.Add($"Jump {m.N}"));
        On<WhatHappened>(async (_, ct) => await Context.ReplyAsync(new Happened(_order.ToArray()), ct));
    }
}
