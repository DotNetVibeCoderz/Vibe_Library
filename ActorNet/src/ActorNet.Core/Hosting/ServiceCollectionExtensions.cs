// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ActorNet.Hosting;

/// <summary>
/// Wires an actor system into a generic host, so it starts and stops with the application.
/// </summary>
/// <remarks>
/// This is the "deep .NET integration" half of the requirements: the same registration works in a
/// console worker, an ASP.NET Core app and the Blazor dashboard, and actors get constructor
/// injection from the host's container because the system builds them through
/// <see cref="ActivatorUtilities"/>.
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers an actor system and starts it with the host.</summary>
    /// <example>
    /// <code>
    /// builder.Services.AddActorNet(actors =>
    /// {
    ///     actors.Options.NodeId = "node-1";
    ///     actors.Options.Port = 9001;
    ///     actors.Actor&lt;BankAccountActor&gt;();
    ///     actors.Message&lt;Deposit&gt;();
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddActorNet(this IServiceCollection services, Action<ActorNetBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var builder = new ActorNetBuilder();
        configure?.Invoke(builder);

        services.AddSingleton(builder);
        services.AddSingleton(provider =>
        {
            var system = new ActorSystem(
                builder.Options,
                provider.GetService<ILoggerFactory>(),
                provider,
                builder.Serializer);

            builder.Apply(system);
            return system;
        });

        services.AddSingleton<IActorSystem>(provider => provider.GetRequiredService<ActorSystem>());
        services.AddHostedService<ActorSystemHostedService>();
        return services;
    }
}

/// <summary>Collects registrations before the actor system exists, then replays them onto it.</summary>
public sealed class ActorNetBuilder
{
    private readonly List<Action<ActorSystem>> _registrations = [];

    /// <summary>Node configuration. Mutate it directly.</summary>
    public ActorSystemOptions Options { get; } = new();

    /// <summary>Overrides the default JSON serializer.</summary>
    public Serialization.IMessageSerializer? Serializer { get; set; }

    /// <summary>Registers an actor type, optionally with its own supervision strategy.</summary>
    public ActorNetBuilder Actor<TActor>(SupervisorStrategy? strategy = null) where TActor : IActor
    {
        _registrations.Add(system => system.RegisterActor<TActor>(strategy));
        return this;
    }

    /// <summary>Registers a message type for the wire.</summary>
    public ActorNetBuilder Message<TMessage>(string? alias = null)
    {
        _registrations.Add(system => system.RegisterMessage<TMessage>(alias));
        return this;
    }

    /// <summary>Registers every attributed message type in an assembly.</summary>
    public ActorNetBuilder MessagesFromAssembly(System.Reflection.Assembly assembly)
    {
        _registrations.Add(system => system.RegisterMessagesFromAssembly(assembly));
        return this;
    }

    /// <summary>Configures the node.</summary>
    public ActorNetBuilder Configure(Action<ActorSystemOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(Options);
        return this;
    }

    internal void Apply(ActorSystem system)
    {
        foreach (var registration in _registrations) registration(system);
    }
}

/// <summary>Starts the actor system with the host and stops it on shutdown.</summary>
internal sealed class ActorSystemHostedService(ActorSystem system, ILogger<ActorSystemHostedService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => system.StartAsync(cancellationToken);

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await system.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Shutdown must not throw out of the host - it would mask the real reason the app is
            // stopping, and there is nothing left to recover anyway.
            logger.LogError(ex, "The actor system did not stop cleanly.");
        }
    }
}
