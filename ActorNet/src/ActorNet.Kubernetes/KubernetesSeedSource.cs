// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using ActorNet.Cluster;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ActorNet.Kubernetes;

/// <summary>
/// Finds the cluster's peers by asking the Kubernetes API, and keeps asking.
/// </summary>
/// <remarks>
/// <para>
/// A headless service resolved through DNS already works and needs none of this. What it cannot do
/// is tell a node that a pod has appeared: DNS is asked, it answers with what it has, and a node
/// that is alone has to keep asking to find out anything changed. A watch is told.
/// </para>
/// <para>
/// The list is what makes this correct and the watch is what makes it prompt. Both are needed: a
/// watch is a stream of changes since a known point, so something has to establish that point, and
/// the API server ends watches on its own schedule, so something has to notice and open another.
/// </para>
/// <para>
/// This node's own pod is not filtered out here. The join path already refuses to introduce a node
/// to itself, and it does it by comparing the address it is about to dial, which is a better test
/// than anything available from a pod's own view of itself.
/// </para>
/// </remarks>
public sealed class KubernetesSeedSource : ISeedSource, IAsyncDisposable
{
    private readonly KubernetesSeedOptions _options;
    private readonly HttpClient _client;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stopping = new();

    // Pod name to endpoint. Keyed by name because that is what a DELETED event carries, and a
    // deleted pod's address is not in the event body to match on.
    private readonly ConcurrentDictionary<string, string> _endpoints = new(StringComparer.Ordinal);

    private Task? _watcher;
    private volatile bool _listed;

    /// <summary>Creates a source. The watch starts on the first call, not here.</summary>
    /// <param name="options">Which pods, and how to reach the API.</param>
    /// <param name="client">
    /// The client to use. One is created when this is null; supply one to control the handler,
    /// which is what the tests do and what a cluster with a private CA needs.
    /// </param>
    /// <param name="logger">Where to report a watch that will not stay open.</param>
    public KubernetesSeedSource(KubernetesSeedOptions options, HttpClient? client = null, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _options = options;
        _logger = logger ?? NullLogger.Instance;

        _client = client ?? new HttpClient { BaseAddress = options.EffectiveApiServer };
        _client.BaseAddress ??= options.EffectiveApiServer;

        // A watch is a response that never finishes. The default timeout would end it on a clock
        // rather than when the server does, and every one of those looks like a network fault.
        _client.Timeout = Timeout.InfiniteTimeSpan;
    }

    /// <summary>The endpoints known right now.</summary>
    /// <remarks>
    /// The first call lists before answering, because a node that starts and is told there are no
    /// peers stays alone until its next retry for no reason. After that the watch keeps it current
    /// and this returns immediately.
    /// </remarks>
    public async ValueTask<IReadOnlyList<string>> SeedsAsync(CancellationToken cancellationToken)
    {
        if (!_listed)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopping.Token);
            var version = await ListAsync(linked.Token).ConfigureAwait(false);
            _listed = true;

            _watcher ??= Task.Run(() => WatchLoopAsync(version, _stopping.Token), CancellationToken.None);
        }

        return _endpoints.Values.OrderBy(endpoint => endpoint, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Replaces what is known with what the API server says, and returns where to watch from.</summary>
    private async Task<string?> ListAsync(CancellationToken cancellationToken)
    {
        var url = $"/api/v1/namespaces/{_options.EffectiveNamespace}/pods" +
                  $"?labelSelector={Uri.EscapeDataString(_options.LabelSelector)}";

        using var request = Request(HttpMethod.Get, url);
        using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken).ConfigureAwait(false);

        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (document.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var pod in items.EnumerateArray())
            {
                if (Endpoint(pod) is not { } found) continue;

                _endpoints[found.Name] = found.Endpoint;
                seen.Add(found.Name);
            }
        }

        // A list is the whole truth, so anything not in it is gone. This is what makes a resync
        // able to correct a watch that has quietly missed something.
        foreach (var name in _endpoints.Keys)
            if (!seen.Contains(name)) _endpoints.TryRemove(name, out _);

        _logger.LogInformation("Kubernetes reports {Count} peer(s) matching {Selector}.", _endpoints.Count, _options.LabelSelector);

        return document.RootElement.TryGetProperty("metadata", out var metadata) &&
               metadata.TryGetProperty("resourceVersion", out var version)
            ? version.GetString()
            : null;
    }

    /// <summary>Keeps a watch open, listing again between one and the next.</summary>
    /// <remarks>
    /// A watch is bounded by <see cref="KubernetesSeedOptions.ResyncInterval"/> and ends on the
    /// server's own schedule, so ending is the ordinary case rather than the error case. Each one
    /// is followed by a fresh list rather than a resume, which is what makes the resync interval
    /// mean what it says: an event a watch missed is corrected at the next list, where resuming
    /// from a resourceVersion would carry the mistake for as long as the process ran.
    /// </remarks>
    private async Task WatchLoopAsync(string? version, CancellationToken cancellationToken)
    {
        var backoff = _options.WatchRetryDelay;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await WatchAsync(version, cancellationToken).ConfigureAwait(false);
                backoff = _options.WatchRetryDelay;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                // Losing the watch is not losing the cluster: the endpoints already known stay
                // known, and joining retries on its own schedule regardless.
                _logger.LogWarning(ex, "The Kubernetes watch failed; reopening in {Backoff}.", backoff);
                backoff = TimeSpan.FromMilliseconds(Math.Min(backoff.TotalMilliseconds * 2, 30_000));
            }

            // Paced even on the clean path, because a server that ends watches immediately would
            // otherwise be asked again as fast as the loop can run.
            try { await Task.Delay(backoff, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }

            try
            {
                version = await ListAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                // Without a version there is nothing to watch from, so the next pass starts a
                // watch with none - which the server answers from now rather than refusing.
                _logger.LogWarning(ex, "Could not list pods; the next watch starts without a resourceVersion.");
                version = null;
            }
        }
    }

    /// <summary>Reads one watch to its end, applying events as they arrive.</summary>
    private async Task WatchAsync(string? version, CancellationToken cancellationToken)
    {
        var url = $"/api/v1/namespaces/{_options.EffectiveNamespace}/pods" +
                  $"?labelSelector={Uri.EscapeDataString(_options.LabelSelector)}&watch=true" +
                  (version is { Length: > 0 } ? $"&resourceVersion={Uri.EscapeDataString(version)}" : string.Empty) +
                  $"&timeoutSeconds={(int)_options.ResyncInterval.TotalSeconds}";

        using var request = Request(HttpMethod.Get, url);
        using var response = await _client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(body);

        // One JSON object per line, for as long as the server keeps it open.
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (line.Length == 0) continue;

            try { Apply(line); }
            catch (JsonException ex) { _logger.LogDebug(ex, "Skipped a watch event that would not parse."); }
        }
    }

    /// <summary>Applies one watch event.</summary>
    private void Apply(string line)
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;

        if (!root.TryGetProperty("type", out var type) || !root.TryGetProperty("object", out var pod)) return;
        if (!pod.TryGetProperty("metadata", out var metadata)) return;

        var name = metadata.TryGetProperty("name", out var podName) ? podName.GetString() : null;
        if (name is not { Length: > 0 }) return;

        switch (type.GetString())
        {
            case "ADDED":
            case "MODIFIED":
                // MODIFIED removes as well as adds: a pod that has begun terminating is still in
                // the API and is no longer somewhere to send a join.
                if (Endpoint(pod) is { } running) _endpoints[name] = running.Endpoint;
                else _endpoints.TryRemove(name, out _);
                break;

            case "DELETED":
                _endpoints.TryRemove(name, out _);
                break;
        }
    }

    /// <summary>A pod's name and address, when it is one worth trying.</summary>
    /// <remarks>
    /// Running and with an address. A pod that is pending has no IP to dial, and one that is
    /// terminating will refuse the connection - both are ordinary states rather than errors, and
    /// both are worth leaving out of a list whose whole purpose is somewhere to send a join.
    /// </remarks>
    private (string Name, string Endpoint)? Endpoint(JsonElement pod)
    {
        if (!pod.TryGetProperty("metadata", out var metadata)) return null;
        if (metadata.TryGetProperty("deletionTimestamp", out _)) return null;
        if (!metadata.TryGetProperty("name", out var name) || name.GetString() is not { Length: > 0 } podName) return null;

        if (!pod.TryGetProperty("status", out var status)) return null;
        if (!status.TryGetProperty("phase", out var phase) || phase.GetString() != "Running") return null;
        if (!status.TryGetProperty("podIP", out var address) || address.GetString() is not { Length: > 0 } ip) return null;

        return (podName, $"{ip}:{_options.Port}");
    }

    private HttpRequestMessage Request(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);

        // Read per request, because the kubelet rotates a projected token and a copy taken at
        // startup stops working hours later - which looks like a cluster that was fine all day.
        if (_options.EffectiveToken is { Length: > 0 } token)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return request;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync().ConfigureAwait(false);

        if (_watcher is { } watcher)
        {
            try { await watcher.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
            catch (Exception ex) when (ex is TimeoutException or OperationCanceledException) { /* forced */ }
        }

        _stopping.Dispose();
        _client.Dispose();
    }
}
