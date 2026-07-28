using System.Net;
using System.Text;
using Lvgl.Assistant.Chat;

namespace Lvgl.Assistant.Chat;

/// <summary>
/// Stores uploaded files and serves them over a loopback HTTP endpoint.
/// </summary>
/// <remarks>
/// <para>
/// The brief asked for attachments to be uploaded and referenced by URL. This does that with a
/// small <see cref="HttpListener"/> bound to <c>127.0.0.1</c>, which means no external storage
/// account, no credentials to manage, and nothing leaving the machine.
/// </para>
/// <para>
/// <b>A loopback URL is not reachable by a hosted model.</b> That is why images are additionally
/// sent as inline image content: the URL is what the user and the transcript see, while the bytes
/// are what OpenAI, Anthropic or Gemini actually receive. Documents keep URL-only semantics, as
/// asked - the link goes into the message text, and a locally-hosted model such as Ollama can
/// fetch it, while a hosted one will say it cannot.
/// </para>
/// <para>
/// The listener refuses anything outside its own directory and only serves files it created, so a
/// crafted request cannot walk up into the file system.
/// </para>
/// </remarks>
public sealed class AttachmentService : IDisposable
{
    private static readonly IReadOnlyDictionary<string, string> MimeTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".gif"] = "image/gif",
            [".webp"] = "image/webp",
            [".bmp"] = "image/bmp",
            [".pdf"] = "application/pdf",
            [".txt"] = "text/plain",
            [".md"] = "text/markdown",
            [".json"] = "application/json",
            [".xml"] = "application/xml",
            [".csv"] = "text/csv",
            [".cs"] = "text/plain",
            [".c"] = "text/plain",
            [".h"] = "text/plain",
            [".log"] = "text/plain",
            [".yml"] = "text/yaml",
            [".yaml"] = "text/yaml",
        };

    private readonly string _directory;
    private readonly long _maxBytes;
    private readonly HttpListener? _listener;
    private readonly CancellationTokenSource _shutdown = new();
    private bool _disposed;

    /// <summary>Starts the store and, if possible, the local file host.</summary>
    /// <param name="directory">Where uploads are kept.</param>
    /// <param name="port">Port to bind; 0 picks a free one.</param>
    /// <param name="maxMegabytes">Largest accepted upload.</param>
    public AttachmentService(string directory, int port = 0, int maxMegabytes = 20)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        _directory = directory;
        _maxBytes = maxMegabytes * 1024L * 1024L;
        System.IO.Directory.CreateDirectory(_directory);

        if (port == 0) port = FreePort();

        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();

            BaseUrl = $"http://127.0.0.1:{port}";
            _ = Task.Run(() => ServeAsync(_shutdown.Token));
        }
        catch (HttpListenerException ex)
        {
            // Binding can fail on a locked-down machine. Attachments still work - they just get
            // file:// URLs, and images still reach the model as inline content.
            _listener = null;
            BaseUrl = null;
            HostError = ex.Message;
        }
    }

    /// <summary>Base URL files are served from, or null when the host could not start.</summary>
    public string? BaseUrl { get; }

    /// <summary>Why the local host could not start, if it did not.</summary>
    public string? HostError { get; }

    /// <summary>Where uploads are stored.</summary>
    public string Directory => _directory;

    /// <summary>
    /// Copies a file into the store and returns its attachment record.
    /// </summary>
    /// <exception cref="FileNotFoundException">The source does not exist.</exception>
    /// <exception cref="InvalidOperationException">The file is larger than the configured limit.</exception>
    public ChatAttachment Add(string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        var info = new FileInfo(sourcePath);
        if (!info.Exists) throw new FileNotFoundException("The attachment does not exist.", sourcePath);

        if (info.Length > _maxBytes)
        {
            throw new InvalidOperationException(
                $"{info.Name} is {info.Length / (1024 * 1024)} MB, over the " +
                $"{_maxBytes / (1024 * 1024)} MB limit (Assistant:MaxAttachmentMegabytes).");
        }

        var extension = info.Extension.ToLowerInvariant();
        var id = Guid.NewGuid().ToString("n") + extension;
        var destination = Path.Combine(_directory, id);

        File.Copy(sourcePath, destination, overwrite: false);

        var mime = MimeTypeFor(extension);

        return new ChatAttachment(
            Id: id,
            FileName: info.Name,
            Kind: mime.StartsWith("image/", StringComparison.Ordinal) ? AttachmentKind.Image : AttachmentKind.Document,
            MimeType: mime,
            Url: BaseUrl is null ? new Uri(destination).AbsoluteUri : $"{BaseUrl}/{id}",
            SizeBytes: info.Length)
        {
            LocalPath = destination,
        };
    }

    /// <summary>Reads an attachment's bytes, for sending as inline image content.</summary>
    public byte[] ReadBytes(ChatAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);

        var path = attachment.LocalPath ?? Path.Combine(_directory, attachment.Id);
        return File.ReadAllBytes(path);
    }

    /// <summary>Deletes an attachment's file. Missing files are ignored.</summary>
    public void Remove(ChatAttachment attachment)
    {
        var path = attachment.LocalPath ?? Path.Combine(_directory, attachment.Id);
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>Content type for a file extension, defaulting to a safe binary type.</summary>
    public static string MimeTypeFor(string extension) =>
        MimeTypes.TryGetValue(extension, out var mime) ? mime : "application/octet-stream";

    /// <summary>True when the extension is an image the models can read.</summary>
    public static bool IsImageExtension(string extension) =>
        MimeTypeFor(extension).StartsWith("image/", StringComparison.Ordinal);

    private async Task ServeAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener is { IsListening: true })
        {
            HttpListenerContext context;

            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
            {
                return;
            }

            try
            {
                Respond(context);
            }
            catch (Exception ex) when (ex is IOException or HttpListenerException)
            {
                // The client went away mid-response; nothing useful to do.
            }
            finally
            {
                try { context.Response.Close(); } catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
            }
        }
    }

    private void Respond(HttpListenerContext context)
    {
        var requested = context.Request.Url?.AbsolutePath.TrimStart('/') ?? string.Empty;

        // Only ever serve a bare file name from the store. Anything with a separator or a
        // traversal segment is rejected outright rather than normalised.
        if (requested.Length == 0 ||
            requested.Contains('/') ||
            requested.Contains('\\') ||
            requested.Contains("..", StringComparison.Ordinal) ||
            requested.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            context.Response.StatusCode = 400;
            return;
        }

        var path = Path.Combine(_directory, requested);

        // Belt and braces: confirm the resolved path really is inside the store.
        var full = Path.GetFullPath(path);
        var root = Path.GetFullPath(_directory);
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = 400;
            return;
        }

        if (!File.Exists(full))
        {
            context.Response.StatusCode = 404;
            return;
        }

        var bytes = File.ReadAllBytes(full);

        context.Response.StatusCode = 200;
        context.Response.ContentType = MimeTypeFor(Path.GetExtension(full));
        context.Response.ContentLength64 = bytes.Length;
        context.Response.OutputStream.Write(bytes, 0, bytes.Length);
    }

    private static int FreePort()
    {
        using var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        return port;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _shutdown.Cancel();

        if (_listener is { IsListening: true })
        {
            try { _listener.Stop(); } catch (ObjectDisposedException) { }
        }

        (_listener as IDisposable)?.Dispose();
        _shutdown.Dispose();
    }
}
