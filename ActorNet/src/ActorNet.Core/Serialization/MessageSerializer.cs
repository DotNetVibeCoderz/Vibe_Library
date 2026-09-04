// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;

namespace ActorNet.Serialization;

/// <summary>
/// The allow-list of message types that may cross a node boundary, in both directions.
/// </summary>
/// <remarks>
/// <para>
/// Resolution is by registered alias, never by the type name in the frame. That is the whole
/// point: a transport that resolves whatever name arrives lets a peer choose which type this
/// process constructs, and deserialization gadgets are built exactly on that. An unregistered
/// alias is refused with <see cref="UnknownMessageTypeException"/>.
/// </para>
/// <para>
/// Aliases default to the full type name, so two nodes running the same assemblies agree without
/// any configuration. Pass an explicit alias when the same logical message has different CLR types
/// on either side - which is also how the Go, Python and Node clients address it.
/// </para>
/// </remarks>
public sealed class MessageTypeRegistry
{
    private readonly ConcurrentDictionary<string, Type> _byAlias = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Type, string> _byType = new();

    /// <summary>Every alias currently registered, for diagnostics and the management API.</summary>
    public IReadOnlyCollection<string> Aliases => (IReadOnlyCollection<string>)_byAlias.Keys;

    /// <summary>Registers a type under <paramref name="alias"/>, or under its full name.</summary>
    public void Register(Type type, string? alias = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        alias ??= type.FullName ?? type.Name;

        if (_byAlias.TryGetValue(alias, out var existing) && existing != type)
            throw new ActorNetException($"Alias '{alias}' is already registered to {existing.FullName}; it cannot also mean {type.FullName}.");

        _byAlias[alias] = type;
        _byType[type] = alias;
    }

    /// <inheritdoc cref="Register(Type, string?)" />
    public void Register<T>(string? alias = null) => Register(typeof(T), alias);

    /// <summary>
    /// Registers every record and class in <paramref name="assembly"/> marked with
    /// <see cref="ActorMessageAttribute"/>. The bulk-registration path for an application that
    /// keeps its contracts in one assembly.
    /// </summary>
    public int RegisterFromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var count = 0;

        foreach (var type in assembly.GetTypes())
        {
            var attribute = type.GetCustomAttribute<ActorMessageAttribute>();
            if (attribute is null) continue;
            Register(type, attribute.Alias);
            count++;
        }

        return count;
    }

    /// <summary>The alias a type travels under, registering it on first use.</summary>
    public string AliasOf(Type type) => _byType.TryGetValue(type, out var alias) ? alias : RegisterAndReturn(type);

    private string RegisterAndReturn(Type type)
    {
        // Outbound registration is implicit: the sender already trusts its own types. Only the
        // inbound direction needs the allow-list, which is why Resolve does not do this.
        Register(type);
        return _byType[type];
    }

    /// <summary>The type an alias means, or a refusal.</summary>
    public Type Resolve(string alias) =>
        _byAlias.TryGetValue(alias, out var type) ? type : throw new UnknownMessageTypeException(alias);

    /// <summary>Non-throwing <see cref="Resolve"/>.</summary>
    public bool TryResolve(string alias, out Type type) => _byAlias.TryGetValue(alias, out type!);
}

/// <summary>
/// Marks a message type for bulk registration by
/// <see cref="MessageTypeRegistry.RegisterFromAssembly"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class ActorMessageAttribute : Attribute
{
    /// <summary>Wire alias. Defaults to the type's full name.</summary>
    public string? Alias { get; init; }
}

/// <summary>Turns application messages into wire payloads and back.</summary>
public interface IMessageSerializer
{
    /// <summary>The allow-list this serializer resolves inbound payloads against.</summary>
    MessageTypeRegistry Types { get; }

    /// <summary>Serializes a message, returning its alias alongside the payload.</summary>
    (string Alias, JsonElement Payload) Serialize(object message);

    /// <summary>Materializes a payload that arrived under <paramref name="alias"/>.</summary>
    object Deserialize(string alias, JsonElement payload);
}

/// <summary>The default serializer: System.Text.Json over an explicit type allow-list.</summary>
public sealed class JsonMessageSerializer(MessageTypeRegistry? types = null, JsonSerializerOptions? options = null) : IMessageSerializer
{
    private readonly JsonSerializerOptions _options = options ?? new JsonSerializerOptions
    {
        // Records with positional parameters are the idiomatic message shape, and they only
        // round-trip if the constructor is used.
        PropertyNameCaseInsensitive = true,
        IncludeFields = true,
    };

    /// <inheritdoc />
    public MessageTypeRegistry Types { get; } = types ?? new MessageTypeRegistry();

    /// <inheritdoc />
    public (string Alias, JsonElement Payload) Serialize(object message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var type = message.GetType();
        var alias = Types.AliasOf(type);
        var payload = JsonSerializer.SerializeToElement(message, type, _options);
        return (alias, payload);
    }

    /// <inheritdoc />
    public object Deserialize(string alias, JsonElement payload)
    {
        var type = Types.Resolve(alias);
        return JsonSerializer.Deserialize(payload, type, _options)
               ?? throw new ActorNetException($"Payload for alias '{alias}' deserialized to null.");
    }
}
