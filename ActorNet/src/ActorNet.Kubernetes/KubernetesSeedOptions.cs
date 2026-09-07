// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace ActorNet.Kubernetes;

/// <summary>
/// Where to look for the pods of this cluster, and how to talk to the API that knows.
/// </summary>
/// <remarks>
/// Every value has an in-cluster default read from what the kubelet mounts into the pod, so a
/// deployment usually sets only <see cref="LabelSelector"/> and <see cref="Port"/>.
/// </remarks>
public sealed class KubernetesSeedOptions
{
    /// <summary>The files the kubelet mounts for a pod's service account.</summary>
    private const string ServiceAccount = "/var/run/secrets/kubernetes.io/serviceaccount";

    /// <summary>Which pods are this cluster, as a label selector.</summary>
    /// <remarks>
    /// Required, and deliberately not defaulted. A selector that matches nothing is a cluster that
    /// never forms; one that matches too much is a cluster that tries to join somebody else's.
    /// Neither should be something a caller gets by leaving a field alone.
    /// </remarks>
    public string LabelSelector { get; set; } = string.Empty;

    /// <summary>The port peers listen on. Every pod is assumed to use the same one.</summary>
    public int Port { get; set; } = 5100;

    /// <summary>The namespace to look in. Defaults to the pod's own.</summary>
    public string? Namespace { get; set; }

    /// <summary>The API server, as a base address. Defaults to the in-cluster endpoint.</summary>
    public Uri? ApiServer { get; set; }

    /// <summary>The bearer token. Defaults to the pod's service-account token.</summary>
    /// <remarks>
    /// Read on every request rather than once, because the kubelet rotates the projected token and
    /// a copy taken at startup stops working somewhere between an hour and a day later - which
    /// looks exactly like a cluster that has been fine for a day and then cannot find its peers.
    /// </remarks>
    public Func<string?>? Token { get; set; }

    /// <summary>How long to wait before opening a watch again after one ends.</summary>
    /// <remarks>
    /// A watch ends normally - the API server closes them on its own schedule - so this is the
    /// ordinary path rather than the error path. It is short for that reason, and backed off only
    /// when reopening fails.
    /// </remarks>
    public TimeSpan WatchRetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>How often to list again regardless, in case a watch has been quietly wrong.</summary>
    public TimeSpan ResyncInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>The namespace to use, from configuration or from the pod.</summary>
    public string EffectiveNamespace =>
        Namespace is { Length: > 0 } configured ? configured : ReadFile($"{ServiceAccount}/namespace") ?? "default";

    /// <summary>The API server to use, from configuration or from the environment.</summary>
    public Uri EffectiveApiServer
    {
        get
        {
            if (ApiServer is { } configured) return configured;

            var host = Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST");
            var port = Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_PORT") ?? "443";

            return host is { Length: > 0 }
                ? new Uri($"https://{host}:{port}")
                : throw new ActorNetException(
                    "No Kubernetes API server. KUBERNETES_SERVICE_HOST is unset, which means this is not running " +
                    "in a pod - set ApiServer explicitly if that is deliberate.");
        }
    }

    /// <summary>The bearer token to use, from configuration or from the pod.</summary>
    public string? EffectiveToken => Token is { } supplied ? supplied() : ReadFile($"{ServiceAccount}/token");

    /// <summary>Throws when a value that has no sensible default is missing.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(LabelSelector))
            throw new ArgumentException(
                "LabelSelector must say which pods are this cluster. Without it the query matches every pod in the namespace.",
                nameof(LabelSelector));

        if (Port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(Port), Port, "Port must be between 1 and 65535.");

        if (ResyncInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ResyncInterval), ResyncInterval, "A resync interval of zero lists in a tight loop.");
    }

    private static string? ReadFile(string path)
    {
        // A missing file is the ordinary answer outside a pod, not a failure worth an exception:
        // the caller is about to fall back to what it was configured with.
        try { return File.Exists(path) ? File.ReadAllText(path).Trim() : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
