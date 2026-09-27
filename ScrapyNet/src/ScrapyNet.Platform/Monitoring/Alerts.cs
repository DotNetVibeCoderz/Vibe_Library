using System.Globalization;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text.Json.Serialization;

namespace ScrapyNet.Platform;

[JsonConverter(typeof(JsonStringEnumConverter<AlertType>))]
public enum AlertType
{
    JobCompleted,
    JobFailed,
    SpiderErrors,
    TargetInaccessible,
    ZeroItems,
    AbnormalItemDecrease,
    AuthenticationExpired,
}

[JsonConverter(typeof(JsonStringEnumConverter<AlertSeverity>))]
public enum AlertSeverity
{
    Info,
    Warning,
    Critical,
}

public sealed record Alert(AlertType Type, AlertSeverity Severity, string SpiderId, string JobId, string Message)
{
    public DateTimeOffset Time { get; init; } = DateTimeOffset.UtcNow;

    public override string ToString() => $"[{Severity}] {Type} — {SpiderId} ({JobId}): {Message}";
}

public sealed record AlertOptions
{
    /// <summary>Alert when a run scrapes fewer than this share of the median of recent successful runs.</summary>
    public double DecreaseThreshold { get; init; } = 0.5;

    /// <summary>Successful runs needed before the decrease rule applies.</summary>
    public int BaselineRuns { get; init; } = 3;

    /// <summary>Path fragments that indicate being bounced to a login page.</summary>
    public IReadOnlyList<string> LoginPathHints { get; init; } = ["login", "signin", "sign-in", "auth"];
}

/// <summary>
/// Turns finished jobs into alerts: failures, spider errors, unreachable targets, empty runs, sudden
/// drops versus recent history, and expired logins (401/403 responses or redirects to a login page).
/// </summary>
public sealed class AlertEvaluator(AlertOptions? options = null)
{
    private readonly AlertOptions _options = options ?? new AlertOptions();

    public IReadOnlyList<Alert> Evaluate(JobRecord job, IReadOnlyList<JobRecord> history)
    {
        var alerts = new List<Alert>();
        long Stat(string key) => job.Stats.TryGetValue(key, out var v) && v is not null ? Convert.ToInt64(v is System.Text.Json.JsonElement je ? je.GetDouble() : v, CultureInfo.InvariantCulture) : 0;

        if (job.Status == JobStatus.Failed)
        {
            alerts.Add(new Alert(AlertType.JobFailed, AlertSeverity.Critical, job.SpiderId, job.Id, job.Error ?? job.FinishReason ?? "failed"));
            return alerts;
        }
        if (job.Status != JobStatus.Completed) return alerts;

        var spiderErrors = job.Stats.Keys.Where(k => k.StartsWith("spider_exceptions/", StringComparison.Ordinal)).Sum(k => Stat(k));
        if (spiderErrors > 0 || job.ErrorCount > 0)
            alerts.Add(new Alert(AlertType.SpiderErrors, AlertSeverity.Warning, job.SpiderId, job.Id, $"{spiderErrors} spider exceptions, {job.ErrorCount} error log lines"));

        var downloadFailures = Stat("downloader/exception_count");
        if (job.RequestCount > 0 && job.ResponseCount == 0 || downloadFailures > 0 && downloadFailures >= job.RequestCount)
            alerts.Add(new Alert(AlertType.TargetInaccessible, AlertSeverity.Critical, job.SpiderId, job.Id, $"{job.RequestCount} requests, {job.ResponseCount} responses, {downloadFailures} download errors"));

        var unauthorized = Stat("downloader/response_status_count/401") + Stat("downloader/response_status_count/403");
        var loginRedirect = job.Stats.Keys.Any(k => k.StartsWith("login_redirect", StringComparison.Ordinal));
        if (unauthorized > 0 || loginRedirect)
            alerts.Add(new Alert(AlertType.AuthenticationExpired, AlertSeverity.Critical, job.SpiderId, job.Id, $"{unauthorized} unauthorized responses — credentials or session may have expired"));

        if (job.ItemCount == 0)
        {
            alerts.Add(new Alert(AlertType.ZeroItems, AlertSeverity.Warning, job.SpiderId, job.Id, "The run finished without scraping any item"));
        }
        else
        {
            var baseline = history.Where(h => h.SpiderId == job.SpiderId && h.Id != job.Id && h.Status == JobStatus.Completed && h.ItemCount > 0)
                .OrderByDescending(h => h.CreatedAt).Take(10).Select(h => h.ItemCount).Order().ToList();
            if (baseline.Count >= _options.BaselineRuns)
            {
                var median = baseline[baseline.Count / 2];
                if (job.ItemCount < median * _options.DecreaseThreshold)
                    alerts.Add(new Alert(AlertType.AbnormalItemDecrease, AlertSeverity.Warning, job.SpiderId, job.Id, $"{job.ItemCount} items vs. a recent median of {median}"));
            }
        }

        alerts.Add(new Alert(AlertType.JobCompleted, AlertSeverity.Info, job.SpiderId, job.Id, $"{job.ItemCount} items in {job.Duration?.TotalSeconds:0.00}s"));
        return alerts;
    }
}

/// <summary>Delivers alerts somewhere.</summary>
public interface INotifier
{
    Task NotifyAsync(Alert alert, CancellationToken cancellationToken = default);
}

/// <summary>POSTs the alert as JSON to any URL.</summary>
public sealed class WebhookNotifier(string url, HttpClient? client = null) : INotifier
{
    private readonly HttpClient _client = client ?? new HttpClient();

    public async Task NotifyAsync(Alert alert, CancellationToken cancellationToken = default)
    {
        using var response = await _client.PostAsJsonAsync(url, new
        {
            type = alert.Type.ToString(),
            severity = alert.Severity.ToString(),
            spider = alert.SpiderId,
            job = alert.JobId,
            message = alert.Message,
            time = alert.Time,
        }, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }
}

/// <summary>Slack incoming webhook.</summary>
public sealed class SlackNotifier(string webhookUrl, HttpClient? client = null) : INotifier
{
    private readonly HttpClient _client = client ?? new HttpClient();

    public async Task NotifyAsync(Alert alert, CancellationToken cancellationToken = default)
    {
        var icon = alert.Severity switch { AlertSeverity.Critical => ":red_circle:", AlertSeverity.Warning => ":warning:", _ => ":white_check_mark:" };
        using var response = await _client.PostAsJsonAsync(webhookUrl, new { text = $"{icon} *{alert.Type}* — `{alert.SpiderId}` ({alert.JobId})\n{alert.Message}" }, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }
}

/// <summary>Microsoft Teams incoming webhook (Workflows or legacy connector), as an Adaptive Card.</summary>
public sealed class TeamsNotifier(string webhookUrl, HttpClient? client = null) : INotifier
{
    private readonly HttpClient _client = client ?? new HttpClient();

    public async Task NotifyAsync(Alert alert, CancellationToken cancellationToken = default)
    {
        var card = new
        {
            type = "message",
            attachments = new[]
            {
                new
                {
                    contentType = "application/vnd.microsoft.card.adaptive",
                    content = new
                    {
                        type = "AdaptiveCard",
                        version = "1.4",
                        body = new object[]
                        {
                            new { type = "TextBlock", size = "Medium", weight = "Bolder", text = $"{alert.Type}: {alert.SpiderId}" },
                            new { type = "TextBlock", wrap = true, text = alert.Message },
                            new { type = "TextBlock", isSubtle = true, text = $"Job {alert.JobId} · {alert.Severity} · {alert.Time:u}" },
                        },
                    },
                },
            },
        };
        using var response = await _client.PostAsJsonAsync(webhookUrl, card, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }
}

/// <summary>Email through SMTP.</summary>
public sealed class EmailNotifier(string smtpHost, int port, string from, IReadOnlyList<string> to, System.Net.NetworkCredential? credentials = null, bool enableSsl = true) : INotifier
{
    public async Task NotifyAsync(Alert alert, CancellationToken cancellationToken = default)
    {
        using var client = new SmtpClient(smtpHost, port) { EnableSsl = enableSsl, Credentials = credentials };
        using var message = new MailMessage { From = new MailAddress(from), Subject = $"[Scrapy.Net] {alert.Severity}: {alert.Type} — {alert.SpiderId}", Body = alert.ToString() };
        foreach (var address in to) message.To.Add(address);
        await client.SendMailAsync(message, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Invokes a delegate (handy for tests, logging or custom integrations).</summary>
public sealed class CallbackNotifier(Func<Alert, Task> callback) : INotifier
{
    public Task NotifyAsync(Alert alert, CancellationToken cancellationToken = default) => callback(alert);
}

/// <summary>
/// Evaluates every finished job and routes the resulting alerts to notifiers, each with its own
/// minimum severity and alert-type filter. Notifier failures are collected, never thrown into the job.
/// </summary>
public sealed class NotificationDispatcher
{
    private readonly List<(INotifier Notifier, AlertSeverity MinSeverity, HashSet<AlertType>? Types)> _routes = [];
    private readonly AlertEvaluator _evaluator;
    private readonly JobManager _jobs;
    private readonly List<Alert> _sent = [];

    public NotificationDispatcher(JobManager jobs, AlertEvaluator? evaluator = null)
    {
        _jobs = jobs;
        _evaluator = evaluator ?? new AlertEvaluator();
        jobs.JobFinished += job => _ = DispatchAsync(job);
    }

    public event Action<Alert>? AlertRaised;

    public List<Exception> Failures { get; } = [];

    public IReadOnlyList<Alert> Alerts
    {
        get
        {
            lock (_sent) return [.. _sent];
        }
    }

    public NotificationDispatcher Route(INotifier notifier, AlertSeverity minSeverity = AlertSeverity.Warning, params AlertType[] types)
    {
        _routes.Add((notifier, minSeverity, types.Length == 0 ? null : [.. types]));
        return this;
    }

    public async Task DispatchAsync(JobRecord job)
    {
        foreach (var alert in _evaluator.Evaluate(job, _jobs.History(job.SpiderId, 50)))
        {
            lock (_sent) _sent.Add(alert);
            AlertRaised?.Invoke(alert);
            foreach (var (notifier, min, types) in _routes)
            {
                if (alert.Severity < min || types is not null && !types.Contains(alert.Type)) continue;
                try
                {
                    await notifier.NotifyAsync(alert).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    lock (Failures) Failures.Add(ex);
                }
            }
        }
    }
}

/// <summary>
/// Counts responses that arrived at a login page after a redirect (<c>login_redirect_count</c>) — the
/// usual symptom of an expired session. The job manager enables it for every job.
/// </summary>
public sealed class LoginRedirectDetector
{
    public LoginRedirectDetector(Crawler crawler)
    {
        var hints = crawler.Settings.GetList("LOGIN_PATH_HINTS", ["login", "signin", "sign-in"]);
        crawler.Signals.Connect(Signals.ResponseReceived, e =>
        {
            if (!e.Request.Meta.ContainsKey(MetaKeys.RedirectUrls)) return;
            var path = e.Response.Uri.AbsolutePath.ToLowerInvariant();
            if (hints.Any(h => path.Contains(h, StringComparison.Ordinal))) crawler.Stats.Inc("login_redirect_count");
        });
    }
}
