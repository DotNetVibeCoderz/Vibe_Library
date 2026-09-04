// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ActorNet.Demo.Banking;
using ActorNet.Demo.Ordering;
using ActorNet.Demo.Telemetry;
using ActorNet.Hosting;

namespace ActorNet.Demo;

/// <summary>
/// Registers every demo actor and message on a node.
/// </summary>
/// <remarks>
/// One place, shared by the CLI, the dashboard and the Avalonia samples, so that all three speak
/// the same protocol and any of them can drive actors hosted by another.
/// </remarks>
public static class DemoCatalog
{
    /// <summary>Registers the demo actor types and their messages on an existing system.</summary>
    public static IActorSystem RegisterDemoDomain(this IActorSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);

        system.RegisterActor<BankAccountActor>()
              .RegisterActor<DeviceActor>()
              .RegisterActor<AlarmDeskActor>()
              .RegisterActor<OrderSagaActor>()
              .RegisterActor<InventoryActor>()
              .RegisterActor<PaymentActor>();

        // Every message carries an [ActorMessage] alias, so one assembly scan registers the whole
        // protocol - including the aliases the Go, Python and Node clients address.
        if (system is ActorSystem concrete) concrete.RegisterMessagesFromAssembly(typeof(DemoCatalog).Assembly);

        return system;
    }

    /// <summary>The host-builder equivalent, for the dashboard and any ASP.NET Core app.</summary>
    public static ActorNetBuilder AddDemoDomain(this ActorNetBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder
            .Actor<BankAccountActor>()
            .Actor<DeviceActor>()
            .Actor<AlarmDeskActor>()
            .Actor<OrderSagaActor>()
            .Actor<InventoryActor>()
            .Actor<PaymentActor>()
            .MessagesFromAssembly(typeof(DemoCatalog).Assembly);
    }

    /// <summary>The scenarios the CLI and the samples offer, in one list both can render.</summary>
    public static IReadOnlyList<DemoScenario> Scenarios { get; } =
    [
        new("Banking",
            "Event-sourced accounts: deposits, withdrawals and transfers, with the journal as the statement.",
            "Every command for one account is handled by one activation, so a balance cannot be lost to a concurrent update."),
        new("Telemetry",
            "One actor per device, fed by a reactive stream, raising alarms to a single desk actor.",
            "A million devices are a million addresses; only the ones reporting are activated, and idle ones are swept."),
        new("Ordering",
            "An order saga across inventory and payment actors, with compensation when payment fails.",
            "No distributed transaction: the saga remembers where it got to and releases the stock it already reserved."),
    ];
}

/// <summary>One demo scenario, as shown in the CLI menu and the sample apps.</summary>
/// <param name="Name">Short name.</param>
/// <param name="Summary">What it does.</param>
/// <param name="WhyItMatters">Which property of the actor model it demonstrates.</param>
public sealed record DemoScenario(string Name, string Summary, string WhyItMatters);
