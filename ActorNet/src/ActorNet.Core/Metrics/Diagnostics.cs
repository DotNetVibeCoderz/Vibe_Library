// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace ActorNet.Metrics;

/// <summary>
/// The OpenTelemetry surface: one <see cref="ActivitySource"/> and one <see cref="Meter"/>.
/// </summary>
/// <remarks>
/// <para>
/// The runtime's own counters feed the console, which is fine for looking at one node and useless
/// for anything that aggregates. These are the standard .NET primitives, so an application already
/// exporting telemetry picks them up by adding the names below - ActorNet does not take a
/// dependency on OpenTelemetry to provide them.
/// </para>
/// <para>
/// Nothing here costs anything when nobody is listening. <c>StartActivity</c>
/// returns null with no listener, and an <see cref="Instrument{T}"/> with no collector does not
/// record. That is why tracing can sit on the per-message path at all.
/// </para>
/// </remarks>
public static class ActorNetDiagnostics
{
    /// <summary>The name to add to a tracer provider.</summary>
    public const string ActivitySourceName = "ActorNet";

    /// <summary>The name to add to a meter provider.</summary>
    public const string MeterName = "ActorNet";

    /// <summary>Version reported alongside both, taken from the assembly.</summary>
    public static string Version { get; } =
        typeof(ActorNetDiagnostics).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>Spans for message handling.</summary>
    public static ActivitySource Source { get; } = new(ActivitySourceName, Version);

    /// <summary>Instruments for throughput, failures and latency.</summary>
    public static Meter Meter { get; } = new(MeterName, Version);

    /// <summary>Messages handled successfully.</summary>
    public static Counter<long> MessagesProcessed { get; } =
        Meter.CreateCounter<long>("actornet.messages.processed", "{message}", "Messages handled by an actor.");

    /// <summary>Messages whose handler threw.</summary>
    public static Counter<long> MessagesFailed { get; } =
        Meter.CreateCounter<long>("actornet.messages.failed", "{message}", "Messages whose handler threw.");

    /// <summary>Actor activations.</summary>
    public static Counter<long> Activations { get; } =
        Meter.CreateCounter<long>("actornet.actors.activated", "{actor}", "Actors activated.");

    /// <summary>Actor deactivations, tagged with the reason.</summary>
    public static Counter<long> Deactivations { get; } =
        Meter.CreateCounter<long>("actornet.actors.deactivated", "{actor}", "Actors deactivated.");

    /// <summary>Actor restarts ordered by a supervisor.</summary>
    public static Counter<long> Restarts { get; } =
        Meter.CreateCounter<long>("actornet.actors.restarted", "{actor}", "Actors restarted by a supervisor.");

    /// <summary>Messages that could not be delivered, tagged with the reason.</summary>
    public static Counter<long> DeadLetters { get; } =
        Meter.CreateCounter<long>("actornet.deadletters", "{message}", "Messages that could not be delivered.");

    /// <summary>How long a handler took.</summary>
    public static Histogram<double> ProcessingDuration { get; } =
        Meter.CreateHistogram<double>("actornet.message.duration", "ms", "Time spent in an actor's handler.");

    /// <summary>
    /// How long a message waited in a mailbox before being handled.
    /// </summary>
    /// <remarks>
    /// The more useful of the two histograms for capacity work. Handler time says how expensive the
    /// work is; queue time says whether the node is keeping up with it.
    /// </remarks>
    public static Histogram<double> QueueLatency { get; } =
        Meter.CreateHistogram<double>("actornet.message.queue_time", "ms", "Time a message waited in a mailbox.");

    /// <summary>Starts a span for one message, or returns null when nothing is listening.</summary>
    /// <remarks>
    /// <see cref="ActivityKind.Consumer"/> because handling a message is consuming from a queue,
    /// which is what makes a trace viewer draw it as the receiving half of a send.
    /// </remarks>
    public static Activity? StartReceive(ActorId actor, string messageType)
    {
        var activity = Source.StartActivity($"{actor.Type} receive", ActivityKind.Consumer);
        if (activity is null) return null;

        activity.SetTag("actornet.actor.type", actor.Type);
        activity.SetTag("actornet.actor.key", actor.Key);
        activity.SetTag("actornet.message.type", messageType);
        return activity;
    }
}
