using System.Reflection;

namespace ScrapyNet;

/// <summary>
/// Adapters between the callback shapes a spider author may write and the one shape the engine
/// runs (<c>Func&lt;T, IAsyncEnumerable&lt;object&gt;&gt;</c>).
/// </summary>
/// <remarks>
/// Adapters are small classes rather than lambdas so the original delegate stays reachable. That is
/// what lets <see cref="GetName"/> recover the spider method name, and a named callback is what the
/// scheduler writes to disk when a crawl is paused with <c>JOBDIR</c>.
/// </remarks>
public static class Callbacks
{
    public static Func<T, IAsyncEnumerable<object>> FromSync<T>(Func<T, IEnumerable<object>> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return new SyncAdapter<T>(callback).Invoke;
    }

    public static Func<T, IAsyncEnumerable<object>> FromAction<T>(Action<T> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return new ActionAdapter<T>(callback).Invoke;
    }

    /// <summary>The underlying delegate the user wrote, looking through adapters.</summary>
    public static Delegate? Unwrap(Delegate? callback) => callback?.Target switch
    {
        IAdapter adapter => adapter.Inner,
        _ => callback,
    };

    /// <summary>Method name when the callback is an instance method of a <see cref="Spider"/>.</summary>
    public static string? GetName(Delegate? callback)
    {
        var inner = Unwrap(callback);
        if (inner is null) return null;
        if (inner.Target is not Spider) return null;
        var method = inner.Method;
        // Lambdas and local functions compile to methods with angle-bracketed names; those cannot be
        // looked up again by name, so they do not count as named callbacks.
        return method.Name.Contains('<') ? null : method.Name;
    }

    /// <summary>
    /// Resolves a callback by method name on a spider, accepting either sync (<c>IEnumerable&lt;object&gt;</c>)
    /// or async (<c>IAsyncEnumerable&lt;object&gt;</c>) methods. Used when restoring requests from disk.
    /// </summary>
    public static Func<TArg, IAsyncEnumerable<object>>? Resolve<TArg>(Spider spider, string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var method = FindMethod(spider.GetType(), name, typeof(TArg))
            ?? throw new InvalidOperationException($"Spider {spider.Name} has no method {name}({typeof(TArg).Name}) to use as callback.");

        if (method.ReturnType == typeof(IAsyncEnumerable<object>))
            return (Func<TArg, IAsyncEnumerable<object>>)method.CreateDelegate(typeof(Func<TArg, IAsyncEnumerable<object>>), spider);
        if (method.ReturnType == typeof(IEnumerable<object>))
            return FromSync((Func<TArg, IEnumerable<object>>)method.CreateDelegate(typeof(Func<TArg, IEnumerable<object>>), spider));
        if (method.ReturnType == typeof(void))
            return FromAction((Action<TArg>)method.CreateDelegate(typeof(Action<TArg>), spider));
        throw new InvalidOperationException($"Callback {name} must return IEnumerable<object> or IAsyncEnumerable<object>.");
    }

    private static MethodInfo? FindMethod(Type type, string name, Type argType)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (m.Name != name) continue;
                var ps = m.GetParameters();
                if (ps.Length == 1 && ps[0].ParameterType.IsAssignableFrom(argType)) return m;
            }
        }
        return null;
    }

    private interface IAdapter
    {
        Delegate Inner { get; }
    }

    private sealed class SyncAdapter<T>(Func<T, IEnumerable<object>> inner) : IAdapter
    {
        public Delegate Inner => inner;

        public async IAsyncEnumerable<object> Invoke(T arg)
        {
            var results = inner(arg);
            if (results is null) yield break;
            foreach (var r in results) yield return r;
            await Task.CompletedTask.ConfigureAwait(false);
        }
    }

    private sealed class ActionAdapter<T>(Action<T> inner) : IAdapter
    {
        public Delegate Inner => inner;

        public async IAsyncEnumerable<object> Invoke(T arg)
        {
            inner(arg);
            await Task.CompletedTask.ConfigureAwait(false);
            yield break;
        }
    }
}
