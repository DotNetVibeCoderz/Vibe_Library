// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace ActorNet;

/// <summary>Base for every failure the runtime raises on purpose.</summary>
public class ActorNetException : Exception
{
    public ActorNetException(string message) : base(message) { }
    public ActorNetException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Thrown when an actor id's type half has not been registered with
/// <see cref="IActorSystem.RegisterActor{TActor}"/>.
/// </summary>
/// <remarks>
/// Deliberately an exception rather than a dropped message and a log line. An unregistered type is
/// a wiring bug, and silently discarding the message only turns it into a hang somewhere else.
/// </remarks>
public sealed class ActorTypeNotRegisteredException(string typeName) : ActorNetException(
    $"Actor type '{typeName}' is not registered. Call RegisterActor<{typeName}>() on the actor system before sending to it.")
{
    public string TypeName { get; } = typeName;
}

/// <summary>Thrown when activation fails and the supervisor gave up on retrying it.</summary>
public sealed class ActorActivationException(ActorId id, Exception inner)
    : ActorNetException($"Actor '{id}' failed to activate.", inner)
{
    public ActorId ActorId { get; } = id;
}

/// <summary>Thrown by an ask when no reply arrived in time.</summary>
public sealed class AskTimeoutException(ActorId target, TimeSpan timeout) : ActorNetException(
    $"No reply from '{target}' within {timeout.TotalMilliseconds:N0} ms. The actor may not call ReplyAsync for this message type.")
{
    public ActorId Target { get; } = target;
    public TimeSpan Timeout { get; } = timeout;
}

/// <summary>
/// Thrown when an ask completed with a reply that is not of the expected type - almost always an
/// actor replying with the wrong record.
/// </summary>
public sealed class AskReplyTypeMismatchException(ActorId target, Type expected, Type actual) : ActorNetException(
    $"Actor '{target}' replied with {actual.Name} but the caller asked for {expected.Name}.")
{
    public ActorId Target { get; } = target;
}

/// <summary>Thrown when a message type arrives over the wire without a registered alias.</summary>
public sealed class UnknownMessageTypeException(string alias) : ActorNetException(
    $"No message type is registered under the alias '{alias}'. Register it with RegisterMessage<T>() on both nodes. " +
    "Types are resolved through an explicit allow-list rather than by name, so a remote peer cannot choose which type gets constructed.")
{
    public string Alias { get; } = alias;
}

/// <summary>Thrown when the transport cannot reach the node that owns a key.</summary>
public sealed class NodeUnreachableException(string nodeId, Exception? inner = null) : ActorNetException(
    $"Node '{nodeId}' is unreachable.", inner ?? new IOException("no connection"))
{
    public string NodeId { get; } = nodeId;
}

/// <summary>Thrown when a supervisor escalated a failure all the way to the root.</summary>
public sealed class ActorFailureEscalatedException(ActorId id, Exception inner)
    : ActorNetException($"Failure in '{id}' was escalated to the root guardian and the actor was stopped.", inner)
{
    public ActorId ActorId { get; } = id;
}
