// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace ActorNet;

/// <summary>
/// A <see cref="VirtualActor"/> that dispatches on message type instead of making you write the
/// switch. Register handlers in the constructor with <see cref="On{TMessage}(Func{TMessage, CancellationToken, Task})"/>.
/// </summary>
/// <example>
/// <code>
/// public sealed class CounterActor : ReceiveActor
/// {
///     private int _count;
///
///     public CounterActor()
///     {
///         On&lt;Increment&gt;(m => _count += m.By);
///         On&lt;GetCount&gt;(async (_, ct) => await Context.ReplyAsync(new Count(_count), ct));
///     }
/// }
/// </code>
/// </example>
public abstract class ReceiveActor : VirtualActor
{
    private readonly Dictionary<Type, Func<object, CancellationToken, Task>> _handlers = [];
    private List<(Type Type, Func<object, CancellationToken, Task> Handler)>? _assignableHandlers;

    /// <summary>Registers an async handler for <typeparamref name="TMessage"/>.</summary>
    protected void On<TMessage>(Func<TMessage, CancellationToken, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handlers[typeof(TMessage)] = (message, ct) => handler((TMessage)message, ct);

        // Interfaces and abstract bases cannot be matched by an exact type lookup, so they get a
        // second, slower list that is only consulted on a miss.
        if (typeof(TMessage).IsInterface || typeof(TMessage).IsAbstract)
        {
            _assignableHandlers ??= [];
            _assignableHandlers.Add((typeof(TMessage), (message, ct) => handler((TMessage)message, ct)));
        }
    }

    /// <summary>Registers a synchronous handler for <typeparamref name="TMessage"/>.</summary>
    protected void On<TMessage>(Action<TMessage> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        On<TMessage>((message, _) =>
        {
            handler(message);
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Called for a message with no registered handler. The default throws, so an unhandled
    /// message reaches the supervisor rather than disappearing.
    /// </summary>
    protected virtual Task OnUnhandledAsync(object message, CancellationToken cancellationToken) =>
        throw new ActorNetException(
            $"{GetType().Name} has no handler for {message.GetType().Name}. Register one with On<{message.GetType().Name}>(), or override OnUnhandledAsync to ignore it.");

    /// <inheritdoc />
    protected sealed override Task ReceiveAsync(object message, CancellationToken cancellationToken)
    {
        if (_handlers.TryGetValue(message.GetType(), out var handler))
            return handler(message, cancellationToken);

        if (_assignableHandlers is not null)
        {
            foreach (var (type, assignable) in _assignableHandlers)
            {
                if (type.IsInstanceOfType(message)) return assignable(message, cancellationToken);
            }
        }

        return OnUnhandledAsync(message, cancellationToken);
    }
}
