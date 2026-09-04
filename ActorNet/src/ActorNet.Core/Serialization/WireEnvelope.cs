// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace ActorNet.Serialization;

/// <summary>What a frame on the wire is for.</summary>
public enum WireKind : byte
{
    /// <summary>A one-way message to an actor.</summary>
    Message = 1,

    /// <summary>A message whose sender is blocked waiting for a reply.</summary>
    AskRequest = 2,

    /// <summary>The reply to an <see cref="AskRequest"/>.</summary>
    AskReply = 3,

    /// <summary>The failure that happened instead of a reply.</summary>
    AskFailure = 4,

    /// <summary>A node introducing itself to a seed.</summary>
    Join = 5,

    /// <summary>A seed answering a <see cref="Join"/> with the member table it knows.</summary>
    JoinAck = 6,

    /// <summary>A periodic exchange of member tables.</summary>
    Gossip = 7,

    /// <summary>A node announcing a graceful departure.</summary>
    Leave = 8,
}

/// <summary>One frame, as it travels between nodes.</summary>
/// <remarks>
/// Deliberately flat and nullable rather than a discriminated hierarchy: it is serialized on every
/// hop, and a single shape keeps both the reader and the source-generated serializer trivial.
/// </remarks>
public sealed class WireEnvelope
{
    /// <summary>What this frame is.</summary>
    [JsonPropertyName("k")]
    public WireKind Kind { get; set; }

    /// <summary>Target actor, in <c>Type/Key</c> form.</summary>
    [JsonPropertyName("t")]
    public string? Target { get; set; }

    /// <summary>Sending actor, in <c>Type/Key</c> form, when there is one.</summary>
    [JsonPropertyName("s")]
    public string? Sender { get; set; }

    /// <summary>
    /// Registered alias of the payload's type. Resolved through an allow-list, never through
    /// <see cref="Type.GetType(string)"/> - the sender does not get to name a type for this
    /// process to construct.
    /// </summary>
    [JsonPropertyName("a")]
    public string? MessageAlias { get; set; }

    /// <summary>
    /// The message body, left as raw JSON. Keeping it as a <see cref="JsonElement"/> rather than a
    /// nested string means the payload is parsed once, not encoded and decoded a second time.
    /// </summary>
    [JsonPropertyName("p")]
    public JsonElement? Payload { get; set; }

    /// <summary>Correlates an ask with its reply.</summary>
    [JsonPropertyName("c")]
    public string? CorrelationId { get; set; }

    /// <summary>Node that owns the pending ask and must receive the reply.</summary>
    [JsonPropertyName("r")]
    public string? ReplyToNode { get; set; }

    /// <summary>Node that sent this frame.</summary>
    [JsonPropertyName("f")]
    public string? FromNode { get; set; }

    /// <summary>Failure text for <see cref="WireKind.AskFailure"/>.</summary>
    [JsonPropertyName("e")]
    public string? Error { get; set; }

    /// <summary>Member table, for the membership frames.</summary>
    [JsonPropertyName("m")]
    public List<WireMember>? Members { get; set; }
}

/// <summary>One cluster member as gossiped.</summary>
public sealed class WireMember
{
    [JsonPropertyName("n")] public string NodeId { get; set; } = string.Empty;
    [JsonPropertyName("h")] public string Host { get; set; } = string.Empty;
    [JsonPropertyName("p")] public int Port { get; set; }
    [JsonPropertyName("s")] public int Status { get; set; }
    [JsonPropertyName("i")] public long Incarnation { get; set; }
}

/// <summary>
/// Source-generated serialization for the frame types.
/// </summary>
/// <remarks>
/// Source generation rather than reflection because this runs on every remote hop, and because it
/// keeps the library trimmable and AOT-friendly for the container images the deployment story
/// assumes.
/// </remarks>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(WireEnvelope))]
[JsonSerializable(typeof(WireMember))]
internal sealed partial class WireJsonContext : JsonSerializerContext;
