// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace ActorNet.Runtime;

/// <summary>
/// A runtime instruction to an actor, delivered through its own mailbox rather than applied from
/// outside.
/// </summary>
/// <remarks>
/// Going through the mailbox is what makes lifecycle changes safe: a restart or a stop is then
/// ordered against the messages around it and runs on the actor's own loop, so it cannot land in
/// the middle of a half-finished <c>ReceiveAsync</c>. The cost is that a stop waits behind
/// whatever is already queued, which is exactly what "graceful" means here.
/// </remarks>
internal abstract record SystemCommand;

/// <summary>Rebuild the actor instance, keeping the address and the queued messages.</summary>
internal sealed record RestartCommand(Exception Cause) : SystemCommand;

/// <summary>Deactivate the actor once the messages ahead of this one are done.</summary>
internal sealed record StopCommand(DeactivationReason Reason) : SystemCommand;
