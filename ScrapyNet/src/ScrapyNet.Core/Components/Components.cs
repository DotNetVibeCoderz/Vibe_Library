using System.Reflection;
using Microsoft.Extensions.Logging;

namespace ScrapyNet;

/// <summary>
/// What a downloader middleware method decided: continue the chain, short-circuit with a
/// <see cref="ScrapyNet.Response"/>, or replace the download with a new <see cref="ScrapyNet.Request"/>
/// (which goes back to the scheduler). Converts implicitly from either.
/// </summary>
public readonly struct DownloadResult
{
    private DownloadResult(Response? response, Request? request)
    {
        Response = response;
        Request = request;
    }

    /// <summary>Keep going: the next middleware (or the download itself) handles it.</summary>
    public static DownloadResult Continue => default;

    public Response? Response { get; }

    public Request? Request { get; }

    public bool IsContinue => Response is null && Request is null;

    public static implicit operator DownloadResult(Response response) => new(response, null);

    public static implicit operator DownloadResult(Request request) => new(null, request);

    public static implicit operator ValueTask<DownloadResult>(DownloadResult result) => new(result);
}

/// <summary>
/// Hooks into the downloader: runs <see cref="ProcessRequestAsync"/> on the way out (in ascending order)
/// and <see cref="ProcessResponseAsync"/> / <see cref="ProcessExceptionAsync"/> on the way back (in
/// descending order). Port of Scrapy's downloader middleware interface.
/// </summary>
/// <remarks>
/// Constructors may take any of <see cref="Crawler"/>, <see cref="Settings"/>, <see cref="IStatsCollector"/>,
/// <see cref="SignalManager"/>, <see cref="ILogger"/> and <see cref="ILoggerFactory"/>; the crawler supplies
/// them. Throw <see cref="NotConfiguredException"/> from the constructor to disable the middleware
/// (e.g. when its setting is off).
/// </remarks>
public abstract class DownloaderMiddleware
{
    /// <summary>
    /// Called for each request before download. Return <see cref="DownloadResult.Continue"/>, a response
    /// (skips the download), or a request (rescheduled instead). Throw <see cref="IgnoreRequestException"/> to drop it.
    /// </summary>
    public virtual ValueTask<DownloadResult> ProcessRequestAsync(Request request, Spider spider) => DownloadResult.Continue;

    /// <summary>Called with each response. Return the (possibly replaced) response, or a request to retry/redirect.</summary>
    public virtual ValueTask<DownloadResult> ProcessResponseAsync(Request request, Response response, Spider spider) => (DownloadResult)response;

    /// <summary>Called when the download or a <see cref="ProcessRequestAsync"/> throws. Return Continue to let the next middleware try.</summary>
    public virtual ValueTask<DownloadResult> ProcessExceptionAsync(Request request, Exception exception, Spider spider) => DownloadResult.Continue;

    public virtual ValueTask OpenSpiderAsync(Spider spider) => ValueTask.CompletedTask;

    public virtual ValueTask CloseSpiderAsync(Spider spider, string reason) => ValueTask.CompletedTask;
}

/// <summary>
/// Hooks around spider callbacks: <see cref="ProcessSpiderInputAsync"/> sees responses going in,
/// <see cref="ProcessSpiderOutput"/> sees items and requests coming out. Port of Scrapy's spider
/// middleware interface.
/// </summary>
public abstract class SpiderMiddleware
{
    /// <summary>Called for each response before the callback. Throw to send it to the errback instead.</summary>
    public virtual ValueTask ProcessSpiderInputAsync(Response response, Spider spider) => ValueTask.CompletedTask;

    /// <summary>Filters or transforms callback output. Must be lazy (an async iterator) to preserve streaming.</summary>
    public virtual IAsyncEnumerable<object> ProcessSpiderOutput(Response response, IAsyncEnumerable<object> result, Spider spider) => result;

    /// <summary>Called when a callback (or an earlier input hook) throws. Return replacement output, or <c>null</c> to pass.</summary>
    public virtual IAsyncEnumerable<object>? ProcessSpiderException(Response response, Exception exception, Spider spider) => null;

    /// <summary>Filters or transforms the start requests.</summary>
    public virtual IAsyncEnumerable<Request> ProcessStartRequests(IAsyncEnumerable<Request> startRequests, Spider spider) => startRequests;

    public virtual ValueTask OpenSpiderAsync(Spider spider) => ValueTask.CompletedTask;

    public virtual ValueTask CloseSpiderAsync(Spider spider, string reason) => ValueTask.CompletedTask;
}

/// <summary>
/// Processes scraped items in sequence: validate, clean, deduplicate, store. Return the item (or a
/// replacement) to pass it on; throw <see cref="DropItemException"/> to discard it. Port of Scrapy's
/// item pipeline interface.
/// </summary>
public abstract class ItemPipeline
{
    public virtual ValueTask OpenSpiderAsync(Spider spider) => ValueTask.CompletedTask;

    public abstract ValueTask<object> ProcessItemAsync(object item, Spider spider);

    public virtual ValueTask CloseSpiderAsync(Spider spider) => ValueTask.CompletedTask;
}

/// <summary>A pipeline written as a lambda: <c>settings.ItemPipelines.Add(new LambdaPipeline(item => ...), 300)</c>.</summary>
public sealed class LambdaPipeline(Func<object, Spider, ValueTask<object>> process) : ItemPipeline
{
    public LambdaPipeline(Func<object, object> process) : this((item, _) => ValueTask.FromResult(process(item))) { }

    public override ValueTask<object> ProcessItemAsync(object item, Spider spider) => process(item, spider);
}

/// <summary>
/// Builds components from types, injecting crawler services into constructor parameters (Scrapy's
/// <c>from_crawler</c>). A public static <c>FromCrawler(Crawler)</c> method takes precedence when present.
/// </summary>
public static class ComponentFactory
{
    /// <summary>Creates the component, or returns <c>null</c> when it throws <see cref="NotConfiguredException"/>.</summary>
    public static T? Create<T>(ComponentDictionary.Entry entry, Crawler crawler) where T : class
    {
        if (entry.Component is not Type and not string)
            return entry.Component as T ?? throw new InvalidOperationException($"{entry.Component.GetType().Name} is not a {typeof(T).Name}.");

        var type = entry.ComponentType ?? throw new InvalidOperationException($"Cannot resolve component type '{entry.Component}'.");
        return (T?)Create(type, crawler);
    }

    public static object? Create(Type type, Crawler crawler)
    {
        try
        {
            var factory = type.GetMethod("FromCrawler", BindingFlags.Public | BindingFlags.Static, [typeof(Crawler)]);
            if (factory is not null && type.IsAssignableFrom(factory.ReturnType))
                return factory.Invoke(null, [crawler]);

            // Prefer the constructor with the most parameters we can satisfy.
            foreach (var ctor in type.GetConstructors().OrderByDescending(c => c.GetParameters().Length))
            {
                var parameters = ctor.GetParameters();
                var args = new object?[parameters.Length];
                var ok = true;
                for (var i = 0; i < parameters.Length && ok; i++)
                {
                    var service = Resolve(parameters[i].ParameterType, type, crawler);
                    if (service is not null) args[i] = service;
                    else if (parameters[i].HasDefaultValue) args[i] = parameters[i].DefaultValue;
                    else ok = false;
                }
                if (ok) return ctor.Invoke(args);
            }
            throw new InvalidOperationException($"No usable constructor for {type.FullName}. Constructors may take Crawler, Settings, IStatsCollector, SignalManager, ILogger, ILoggerFactory or Spider.");
        }
        catch (TargetInvocationException tie) when (tie.InnerException is NotConfiguredException nce)
        {
            LogDisabled(type, crawler, nce);
            return null;
        }
        catch (NotConfiguredException nce)
        {
            LogDisabled(type, crawler, nce);
            return null;
        }
        catch (TargetInvocationException tie) when (tie.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
            throw;
        }
    }

    private static void LogDisabled(Type type, Crawler crawler, NotConfiguredException ex)
    {
        var logger = crawler.LoggerFactory.CreateLogger("scrapynet.middleware");
        if (ex.Message != "Component not configured")
            logger.LogDebug("Disabled {Component}: {Reason}", type.Name, ex.Message);
    }

    private static object? Resolve(Type parameterType, Type componentType, Crawler crawler)
    {
        if (parameterType == typeof(Crawler)) return crawler;
        if (parameterType == typeof(Settings)) return crawler.Settings;
        if (parameterType == typeof(IStatsCollector)) return crawler.Stats;
        if (parameterType == typeof(SignalManager)) return crawler.Signals;
        if (parameterType == typeof(ILoggerFactory)) return crawler.LoggerFactory;
        if (parameterType == typeof(ILogger)) return crawler.LoggerFactory.CreateLogger(componentType.FullName ?? componentType.Name);
        if (parameterType.IsGenericType && parameterType.GetGenericTypeDefinition() == typeof(ILogger<>))
            return Activator.CreateInstance(typeof(Logger<>).MakeGenericType(parameterType.GetGenericArguments()[0]), crawler.LoggerFactory);
        if (typeof(Spider).IsAssignableFrom(parameterType) && crawler.Spider is not null && parameterType.IsInstanceOfType(crawler.Spider)) return crawler.Spider;
        return null;
    }
}
