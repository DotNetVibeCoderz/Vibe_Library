// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Net;
using System.Text;
using ActorNet.Kubernetes;

namespace ActorNet.Tests;

/// <summary>
/// Finding the cluster's peers by asking the Kubernetes API.
/// </summary>
/// <remarks>
/// <para>
/// Driven against a real HTTP listener serving canned responses rather than a mocked client. The
/// interesting parts here are the shape of the request, the streaming of a watch, and what is done
/// with each event - and a mock that returns a parsed object skips all three.
/// </para>
/// <para>
/// What this cannot cover is a real cluster: RBAC, the in-cluster CA, a projected token being
/// rotated. There is no Kubernetes on the machine this was written on, and a test that pretends
/// otherwise would be worse than one that says so.
/// </para>
/// </remarks>
public sealed class KubernetesSeedSourceTests : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly string _prefix;
    private readonly List<string> _requested = [];

    /// <summary>Bodies to serve, in order, one per request.</summary>
    private readonly Queue<Func<Stream, CancellationToken, Task>> _responses = new();

    public KubernetesSeedSourceTests()
    {
        // Port zero is not available to HttpListener, so a port is picked and retried on collision.
        for (var attempt = 0; ; attempt++)
        {
            _prefix = $"http://127.0.0.1:{Random.Shared.Next(20000, 60000)}/";
            _listener.Prefixes.Clear();
            _listener.Prefixes.Add(_prefix);

            try
            {
                _listener.Start();
                break;
            }
            catch (HttpListenerException) when (attempt < 20)
            {
                // Taken. Try another.
            }
        }

        _ = Task.Run(ServeAsync);
    }

    private async Task ServeAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync(); }
            catch (Exception) { return; }

            lock (_requested) _requested.Add(context.Request.Url!.PathAndQuery);

            Func<Stream, CancellationToken, Task>? body;
            lock (_responses) body = _responses.Count > 0 ? _responses.Dequeue() : null;

            try
            {
                if (body is null)
                {
                    // Nothing left to say. A watch that hangs open is what the API server does
                    // between events, and it is what the source has to tolerate.
                    context.Response.StatusCode = 200;
                    await Task.Delay(TimeSpan.FromSeconds(30));
                }
                else
                {
                    await body(context.Response.OutputStream, CancellationToken.None);
                }
            }
            catch (Exception)
            {
                // The client hung up, which is ordinary here.
            }
            finally
            {
                try { context.Response.Close(); } catch (Exception) { /* already gone */ }
            }
        }
    }

    private void Respond(string text) => Respond(async (stream, ct) =>
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await stream.WriteAsync(bytes, ct);
        await stream.FlushAsync(ct);
    });

    private void Respond(Func<Stream, CancellationToken, Task> body)
    {
        lock (_responses) _responses.Enqueue(body);
    }

    // Three braces for interpolation, because two consecutive literal braces close a JSON object
    // here and would otherwise be read as the end of an interpolation.
    private static string Pod(string name, string ip, string phase = "Running", string version = "1") =>
        $$$"""
           {"metadata":{"name":"{{{name}}}","resourceVersion":"{{{version}}}"},"status":{"phase":"{{{phase}}}","podIP":"{{{ip}}}"}}
           """;

    private static string PodList(string version, params string[] pods) =>
        $$$"""
           {"metadata":{"resourceVersion":"{{{version}}}"},"items":[{{{string.Join(",", pods)}}}]}
           """;

    private KubernetesSeedSource Source(KubernetesSeedOptions? options = null)
    {
        options ??= new KubernetesSeedOptions { LabelSelector = "app=actornet", Port = 5100 };
        options.Namespace ??= "demo";
        options.Token ??= static () => "test-token";
        options.WatchRetryDelay = TimeSpan.FromMilliseconds(50);

        return new KubernetesSeedSource(options, new HttpClient { BaseAddress = new Uri(_prefix) });
    }

    private string[] Requested()
    {
        lock (_requested) return [.. _requested];
    }

    [Fact]
    public async Task ThePodsBecomeSeeds()
    {
        Respond(PodList("10", Pod("a", "10.1.0.1"), Pod("b", "10.1.0.2")));

        await using var source = Source();
        var seeds = await source.SeedsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["10.1.0.1:5100", "10.1.0.2:5100"], seeds);

        // The namespace and the selector are in the path, not applied after the fact - a query that
        // matches every pod in the namespace and filters here would work and would be wrong.
        Assert.Contains("/api/v1/namespaces/demo/pods", Requested()[0], StringComparison.Ordinal);
        Assert.Contains("labelSelector=app%3Dactornet", Requested()[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task APodWithNoAddressYetIsNotSomewhereToSendAJoin()
    {
        Respond(PodList("10",
            Pod("up", "10.1.0.1"),
            Pod("starting", "", "Pending"),
            Pod("gone", "10.1.0.9", "Succeeded")));

        await using var source = Source();
        var seeds = await source.SeedsAsync(TestContext.Current.CancellationToken);

        // Pending has no address to dial and Succeeded will refuse the connection. Both are
        // ordinary states, and both are useless in a list of places to send a join.
        Assert.Equal(["10.1.0.1:5100"], seeds);
    }

    [Fact]
    public async Task APodThatAppearsIsPickedUpByTheWatch()
    {
        Respond(PodList("10", Pod("a", "10.1.0.1")));

        // The watch: one event, then the stream stays open, which is what a real one does.
        Respond(async (stream, ct) =>
        {
            var line = $$"""{"type":"ADDED","object":{{Pod("b", "10.1.0.2", version: "11")}}}""" + "\n";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(line), ct);
            await stream.FlushAsync(ct);
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
        });

        await using var source = Source();
        Assert.Equal(["10.1.0.1:5100"], await source.SeedsAsync(TestContext.Current.CancellationToken));

        // This is the whole point of a watch over re-resolving a name: nothing asked, and the
        // answer changed anyway.
        await TestHarness.AssertEventuallyAsync(
            () => source.SeedsAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult().Count == 2,
            "the watch should have added the new pod", TimeSpan.FromSeconds(10));

        Assert.Equal(["10.1.0.1:5100", "10.1.0.2:5100"], await source.SeedsAsync(TestContext.Current.CancellationToken));

        var watch = Requested()[1];
        Assert.Contains("watch=true", watch, StringComparison.Ordinal);
        Assert.Contains("resourceVersion=10", watch, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APodThatGoesAwayStopsBeingASeed()
    {
        Respond(PodList("10", Pod("a", "10.1.0.1"), Pod("b", "10.1.0.2")));

        Respond(async (stream, ct) =>
        {
            var deleted = $$"""{"type":"DELETED","object":{{Pod("b", "10.1.0.2", version: "12")}}}""" + "\n";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(deleted), ct);
            await stream.FlushAsync(ct);
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
        });

        await using var source = Source();
        Assert.Equal(2, (await source.SeedsAsync(TestContext.Current.CancellationToken)).Count);

        await TestHarness.AssertEventuallyAsync(
            () => source.SeedsAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult().Count == 1,
            "the watch should have dropped the deleted pod", TimeSpan.FromSeconds(10));

        Assert.Equal(["10.1.0.1:5100"], await source.SeedsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task APodThatIsTerminatingIsDroppedBeforeItIsDeleted()
    {
        Respond(PodList("10", Pod("a", "10.1.0.1"), Pod("b", "10.1.0.2")));

        Respond(async (stream, ct) =>
        {
            // A terminating pod is still Running and still has an address, and will refuse the
            // connection. The deletionTimestamp is the only thing that says so.
            const string terminating =
                """{"type":"MODIFIED","object":{"metadata":{"name":"b","resourceVersion":"13","deletionTimestamp":"2026-09-07T10:00:00Z"},"status":{"phase":"Running","podIP":"10.1.0.2"}}}""";

            await stream.WriteAsync(Encoding.UTF8.GetBytes(terminating + "\n"), ct);
            await stream.FlushAsync(ct);
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
        });

        await using var source = Source();
        Assert.Equal(2, (await source.SeedsAsync(TestContext.Current.CancellationToken)).Count);

        await TestHarness.AssertEventuallyAsync(
            () => source.SeedsAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult().Count == 1,
            "a terminating pod should stop being a seed", TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task AWatchThatDropsIsReopenedFromAFreshList()
    {
        Respond(PodList("10", Pod("a", "10.1.0.1")));
        Respond((_, _) => Task.CompletedTask);                        // the watch, closing at once
        Respond(PodList("20", Pod("a", "10.1.0.1"), Pod("c", "10.1.0.3")));

        await using var source = Source();
        Assert.Equal(["10.1.0.1:5100"], await source.SeedsAsync(TestContext.Current.CancellationToken));

        // A watch ending is the ordinary case - the API server closes them on its own schedule -
        // so the loop has to reopen rather than treat it as the end of discovery.
        await TestHarness.AssertEventuallyAsync(
            () => source.SeedsAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult().Count == 2,
            "the source should have listed again after the watch ended", TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void ASelectorIsRequired()
    {
        // A selector that matches nothing is a cluster that never forms; one that matches too much
        // is a cluster that tries to join somebody else's. Neither should come from a default.
        var options = new KubernetesSeedOptions { Namespace = "demo", ApiServer = new Uri("https://localhost") };
        Assert.Throws<ArgumentException>(options.Validate);
    }

    public async ValueTask DisposeAsync()
    {
        try { _listener.Stop(); } catch (Exception) { /* already stopped */ }
        _listener.Close();
        await ValueTask.CompletedTask;
    }
}
