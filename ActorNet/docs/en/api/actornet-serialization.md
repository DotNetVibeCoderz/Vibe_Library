# ActorNet.Serialization

Generated - edit the `///` comments in the source, not this file.

## ActorMessageAttribute

Marks a message type for bulk registration by `RegisterFromAssembly`.

### property `Alias`

Wire alias. Defaults to the type's full name.

## BinaryMessageCodec

Encodes a registered message as bytes instead of JSON, for the node-to-node path.

Measured on the framing work that came before this, a message body is 40-69% of a binary frame for anything but the smallest message - so encoding the envelope and leaving the body as JSON captured the smaller half. This is the other half.

**Fields are tagged by position, not by name.** A field is a varint tag - its one-based index and a wire type - followed by its value, which is what lets a reader skip a field it does not know instead of losing its place. Appending a property to a message is therefore safe across a rolling upgrade; reordering the parameters of a record is not, but that is a breaking change to the type either way.

**Anything it cannot encode falls back to JSON**, per type and decided once. That fallback is what makes this safe to turn on: correctness never depends on this file covering every shape a message can take, only on it being honest about which ones it covers.

### method `Read(Type, ReadOnlySpan<Byte>)`

Rebuilds a message of `type` from bytes.

### method `Supports(Type)`

Whether this type can be encoded at all. Cached, including the failures.

### method `TryWrite(Object, IBufferWriter<Byte>)`

Encodes a message, or returns false when its shape is not supported.

### method `ZigZag(Int64)`

Maps a signed value onto an unsigned one so small negatives stay small.

## IMessageSerializer

Turns application messages into wire payloads and back.

### method `Deserialize(String, JsonElement)`

Materializes a payload that arrived under `alias`.

### method `Serialize(Object)`

Serializes a message, returning its alias alongside the payload.

### property `Types`

The allow-list this serializer resolves inbound payloads against.

## JsonMessageSerializer

The default serializer: System.Text.Json over an explicit type allow-list.

### method `JsonMessageSerializer(MessageTypeRegistry, JsonSerializerOptions)`

The default serializer: System.Text.Json over an explicit type allow-list.

### method `Deserialize(String, JsonElement)`

### method `Serialize(Object)`

### property `Types`

## MessageTypeRegistry

The allow-list of message types that may cross a node boundary, in both directions.

Resolution is by registered alias, never by the type name in the frame. That is the whole point: a transport that resolves whatever name arrives lets a peer choose which type this process constructs, and deserialization gadgets are built exactly on that. An unregistered alias is refused with `UnknownMessageTypeException`.

Aliases default to the full type name, so two nodes running the same assemblies agree without any configuration. Pass an explicit alias when the same logical message has different CLR types on either side - which is also how the Go, Python and Node clients address it.

### method `AliasOf(Type)`

The alias a type travels under, registering it on first use.

### property `Aliases`

Every alias currently registered, for diagnostics and the management API.

### method `Register(Type, String)`

Registers a type under `alias`, or under its full name.

### method `RegisterFromAssembly(Assembly)`

Registers every record and class in `assembly` marked with `ActorMessageAttribute`. The bulk-registration path for an application that keeps its contracts in one assembly.

### method `Register<T0>(String)`

### method `Resolve(String)`

The type an alias means, or a refusal.

### method `TryResolve(String, Type ref)`

Non-throwing `Resolve`.

## WireEnvelope

One frame, as it travels between nodes.

Deliberately flat and nullable rather than a discriminated hierarchy: it is serialized on every hop, and a single shape keeps both the reader and the source-generated serializer trivial.

### property `AgreedEpoch`

The membership every node last agreed on, and how many times it has been restated.

Carried on gossip so that both halves of a future partition measure themselves against the same denominator. Each node used to freeze its own snapshot of "everyone was healthy when I last looked", and two nodes can take that snapshot at different moments - so a cluster that grew shortly before a partition could have one side counting four members and the other five, and both sides then finding themselves a majority.

The epoch is what settles a disagreement: higher wins, and only a node that can see the whole cluster raises it. During a partition nobody can, so neither side moves and both keep measuring against the last set they held in common.

### property `AgreedMembers`

### property `Body`

The message itself, before it was serialized - or after it was read back.

Never on the wire, which is what `JsonIgnoreAttribute` says here. It exists so the binary writer can encode the body itself rather than copying the JSON somebody already made of it, and so the binary reader can hand back an object instead of JSON for the receiving side to parse a second time. On the JSON path it is ignored entirely.

### property `CorrelationId`

Correlates an ask with its reply.

### property `Error`

Failure text for `AskFailure`.

### property `FromNode`

Node that sent this frame.

### property `Kind`

What this frame is.

### property `Members`

Member table, for the membership frames.

### property `MessageAlias`

Registered alias of the payload's type. Resolved through an allow-list, never through `GetType` - the sender does not get to name a type for this process to construct.

### property `Payload`

The message body, left as raw JSON. Keeping it as a `JsonElement` rather than a nested string means the payload is parsed once, not encoded and decoded a second time.

### property `ReplyToNode`

Node that owns the pending ask and must receive the reply.

### property `Sender`

Sending actor, in `Type/Key` form, when there is one.

### property `Target`

Target actor, in `Type/Key` form.

### property `TraceParent`

W3C `traceparent` of the span that sent this frame, when one was active.

The standard format rather than an ActorNet-shaped one, so a span this node starts joins the caller's trace in any viewer, and so the Go, Python and Node clients can fill it in from their own tracing without knowing anything about this runtime.

### property `TraceState`

W3C `tracestate`, carried through untouched.

## WireJsonContext

Source-generated serialization for the frame types.

Source generation rather than reflection because this runs on every remote hop, and because it keeps the library trimmable and AOT-friendly for the container images the deployment story assumes.

### method `WireJsonContext`

### method `WireJsonContext(JsonSerializerOptions)`

### property `Default`

The default `JsonSerializerContext` associated with a default `JsonSerializerOptions` instance.

### property `GeneratedSerializerOptions`

The source-generated options associated with this context.

### method `GetTypeInfo(Type)`

### property `Int32`

Defines the source generated JSON serialization contract metadata for a given type.

### property `Int64`

Defines the source generated JSON serialization contract metadata for a given type.

### property `JsonElement`

Defines the source generated JSON serialization contract metadata for a given type.

### property `ListString`

Defines the source generated JSON serialization contract metadata for a given type.

### property `ListWireMember`

Defines the source generated JSON serialization contract metadata for a given type.

### property `NullableJsonElement`

Defines the source generated JSON serialization contract metadata for a given type.

### property `String`

Defines the source generated JSON serialization contract metadata for a given type.

### property `WireEnvelope`

Defines the source generated JSON serialization contract metadata for a given type.

### property `WireKind`

Defines the source generated JSON serialization contract metadata for a given type.

### property `WireMember`

Defines the source generated JSON serialization contract metadata for a given type.

## WireKind

What a frame on the wire is for.

### field `AskFailure`

The failure that happened instead of a reply.

### field `AskReply`

The reply to an `AskRequest`.

### field `AskRequest`

A message whose sender is blocked waiting for a reply.

### field `ClusterViewRequest`

Asks a node for enough of the member table to route by. Answered with an ordinary `AskReply`.

For clients rather than peers - a peer already has the whole table through gossip. A client that routes by the answer saves a hop; one that does not is still correct, because every node forwards a message for an actor it does not own.

### field `Gossip`

A periodic exchange of member tables.

### field `Join`

A node introducing itself to a seed.

### field `JoinAck`

A seed answering a `Join` with the member table it knows.

### field `KeyDigest`

Tells one node which keys this node is holding that it would inherit. Never answered.

Addressed to a node rather than an actor, like the two above. It is advice about what to warm if this node disappears, and is never consulted for routing - the ring decides that, and a second opinion about ownership is the last thing a cluster needs.

### field `Leave`

A node announcing a graceful departure.

### field `LeaveTokenRequest`

Asks the coordinator for the leave token, or hands it back. Answered with an ordinary `AskReply`.

Addressed to a node rather than an actor, for the same reason as `NodeStatusRequest`. The payload says which of the two it is.

### field `Message`

A one-way message to an actor.

### field `NodeStatusRequest`

Asks a node what its counters say. Answered with an ordinary `AskReply`.

Addressed to a node rather than to an actor, which is why it needs a kind of its own: the ring routes by key, and there is no key that means "whichever node I am asking".

## WireMember

One cluster member as gossiped.

---

[Back to the index](README.md)
