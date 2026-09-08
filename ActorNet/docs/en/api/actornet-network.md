# ActorNet.Network

Generated - edit the `///` comments in the source, not this file.

## BinaryWireFormat

A binary encoding of `WireEnvelope`.

An envelope is mostly repeated short strings - the target address, the sending node, the message alias, a correlation id - and in JSON each of them carries a quoted key, quotes, commas and braces. For the small messages an actor system actually sends, that framing is a large fraction of the bytes. This drops it to a tag byte and a length.

**The payload is still JSON.** What is encoded here is the envelope around it; making the message body binary as well would mean a per-type binary codec for every registered message, which is a much larger thing than this and would not be backward compatible with the cross-language clients at all. The saving is on the framing, and it is worth stating plainly rather than implying a whole binary protocol.

Frames are self-describing: a JSON envelope starts with `{` and a binary one with `Magic`, so a reader can accept either without negotiating and a node that speaks binary can still answer a client that does not.

### field `Codec`

Encodes message bodies. Shared, because its per-type plans are worth keeping.

### method `Decode(String, Byte[], MessageTypeRegistry)`

Turns a binary body back into a message, or leaves it to fail as a dead letter.

A type this node does not have on its allow-list is not decoded and not guessed at: the envelope comes back with no body, and the ordinary unknown-message path reports it. That is the same refusal the JSON path makes, and it is the one that stops a peer choosing which type this process constructs.

### field `Magic`

First byte of a binary frame. Chosen so it cannot be confused with JSON's `{`.

### method `Read(ReadOnlySpan<Byte>, MessageTypeRegistry)`

Decodes an envelope written by `Write`.

- `types` — The allow-list, needed to turn a binary body back into a message. Null reads the frame without decoding one, which is what a caller that only wants the envelope should pass.

### field `TagAgreed`

The agreed membership and its epoch, as one length-prefixed block.

One block rather than two fields, because the reader can only skip a field it does not know if that field carries its own length. A pair of bare varints would be read as something else entirely by a peer one version behind.

### field `TagBinaryPayload`

A body encoded by `BinaryMessageCodec` rather than as JSON text.

A separate tag rather than a flag on `TagPayload`, so a reader that predates this simply skips it - it would find a message with no body, which is a clean failure rather than a JSON parser being handed bytes.

### method `Varint(ArrayBufferWriter<Byte>, UInt64)`

Seven bits at a time, high bit set while more follow.

Lengths and ports are small almost always and occasionally are not, which is the case a varint is for: a four-byte fixed length on every string would cost more than the strings.

### field `Version`

Format version, so an old reader rejects a new frame rather than misreading it.

### method `Write(WireEnvelope, IMessageSerializer)`

Encodes an envelope.

- `serializer` — Used only when the body's shape is one the codec does not support, to fall back to JSON for that message. Null means an already-serialized payload is the only fallback there is.

## ClusterAuthenticationException

Thrown when a peer cannot prove it belongs in this cluster.

### method `ClusterAuthenticationException(String)`

Thrown when a peer cannot prove it belongs in this cluster.

## ClusterSecurityOptions

Transport security between nodes: encryption, and proof that a peer belongs in this cluster.

Both are off by default, which is the honest default for a library whose first deployment is a developer's laptop - and a documented reason to keep a cluster on a trusted network until they are turned on.

They answer different questions and are independent. TLS stops a third party reading or rewriting the traffic. The shared secret stops an *unauthorised process* joining the cluster, which TLS with an unauthenticated client does not: anyone who can reach the port could otherwise send a Join and start receiving actors.

### method `AcceptAnyCertificate`

Accepts any certificate. Development only.

This encrypts the traffic and authenticates nobody, so it stops passive eavesdropping and not an active attacker. It is a deliberate method rather than a boolean so that it is greppable in a review.

### method `Answer(Byte[])`

The HMAC answer to a challenge. Shared by both sides so they cannot disagree.

### property `AuthenticationEnabled`

True when this node requires peers to prove they know the shared secret.

### property `ClientCertificate`

The certificate a client presents when `RequireClientCertificate` is on across the cluster.

### property `HandshakeTimeout`

How long a peer has to answer the challenge before the connection is dropped.

### method `PinnedThumbprint(String)`

Accepts exactly one certificate, by thumbprint.

The usual answer for a small cluster with a self-signed certificate: no CA to run, and nothing accepted that was not pinned. It does mean a certificate rotation is a configuration change on every node.

### property `RemoteCertificateValidation`

Accept the peer's certificate. Null means the platform default, which rejects anything untrusted.

Cluster nodes usually serve certificates from a private CA, or self-signed ones, so the default will refuse them. Supply a callback that checks whatever you actually trust - a pinned thumbprint, or your own CA. `PinnedThumbprint` is the short version.

### property `RequireClientCertificate`

Require the client to present a certificate too.

Mutual TLS is the strongest option and the most work to deploy, since every node needs a key pair and a way to rotate it. The shared secret is the cheaper answer to the same question; this is here for deployments that already have a certificate story.

### property `ServerCertificate`

The certificate this node serves. Setting it turns TLS on for every node-to-node connection.

Every node in a cluster must agree: a node with TLS on cannot talk to one with it off, and the failure is a handshake error rather than anything subtle. Roll it out to every node before enabling it anywhere.

### property `SharedSecret`

A secret every node in the cluster shares. Setting it turns on a challenge-response handshake before any frame is accepted.

The secret itself is never sent. The listening side offers a random nonce, the connecting side answers with an HMAC of it, and the answer is compared in constant time. That makes the handshake safe to run without TLS - a passive observer learns a nonce and a MAC, neither of which is reusable - and means an operator can turn on authentication without first solving certificate distribution.

It is authentication, not encryption. Without TLS the messages themselves are still in the clear.

### property `TlsEnabled`

True when this node encrypts its inter-node traffic.

### method `Validate`

Throws when the settings cannot produce a working transport.

## FrameCodec

Length-prefixed framing for the node-to-node protocol: a four-byte big-endian payload length, then that many bytes of JSON.

TCP is a byte stream, not a message stream. Reading "whatever one `ReadAsync` returned" and treating it as one message is the classic bug - it works on localhost with small payloads and corrupts the moment two sends coalesce into a segment or one message spans two. The length prefix is what makes a frame a frame.

`MaxFrameBytes` is a hostile-input guard: without it a peer can announce a 4 GB frame and this process will try to allocate for it.

### field `HeaderBytes`

Bytes of length prefix on every frame.

### field `MaxFrameBytes`

Largest payload accepted. A larger announced length closes the connection.

### method `ReadAsync(Stream, CancellationToken)`

Reads one frame, or returns null when the peer closed the connection cleanly between frames.

### method `ReadExactlyOrEofAsync(Stream, Memory<Byte>, CancellationToken)`

Fills `destination` completely. False means a clean EOF before any byte arrived; a partial read is a torn frame and throws.

### method `ReadFramedAsync(Stream, IMessageSerializer, CancellationToken)`

- `serializer` — The allow-list a binary body is resolved against. Without it a binary body is left undecoded, which the ordinary unknown-message path then reports.

### method `ReadFramedAsync(Stream, CancellationToken)`

Reads one frame and says which encoding it was in.

A frame is self-describing - JSON starts with `{`, binary with `Magic` - so nothing has to be negotiated, and a connection can answer in the encoding it was addressed in. That is what lets a node speak binary to its peers and JSON to a client that only knows JSON, on the same listener.

### method `WriteAsync(Stream, WireEnvelope, WireFormat, IMessageSerializer, CancellationToken)`

Writes one frame, encoding the body with `serializer` if it needs to.

The body is left un-serialized until here on purpose. A frame that goes out binary would otherwise be turned into JSON first and the JSON thrown away, which is the cost this whole format exists to avoid - so the encoding decision and the encoding happen in the same place.

### method `WriteAsync(Stream, WireEnvelope, WireFormat, CancellationToken)`

Writes one frame in `format`.

### method `WriteAsync(Stream, WireEnvelope, CancellationToken)`

Writes one frame.

## ITransport

How a node reaches another node.

### property `BoundPort`

The port actually bound, which differs from the requested one when port 0 was asked for.

### method `SendAsync(String, WireEnvelope, CancellationToken)`

Sends to a node, resolving its address through the membership table.

### method `SendToAddressAsync(String, Int32, WireEnvelope, CancellationToken)`

Sends to a raw address. Used for the join handshake, before a node id is known.

### method `StartAsync(CancellationToken)`

Begins listening.

### method `StopAsync(CancellationToken)`

Stops listening and closes every connection.

## SecureChannel

Turns a raw socket into the stream the framing layer reads: TLS if configured, then the authentication handshake if configured.

Both steps happen before a single frame is exchanged, and both sides run them in the same order, so a node that rejects a peer does so without ever having parsed anything the peer sent.

The challenge deliberately does not identify who is answering. It proves membership of the cluster, not identity - identity is what mutual TLS is for. Conflating the two would make the shared secret look like more than it is.

### method `AcceptAsync(TcpClient, ClusterSecurityOptions, CancellationToken)`

Wraps an accepted connection: server-side TLS, then challenge the caller.

### method `AnswerAsync(Stream, ClusterSecurityOptions, CancellationToken)`

Client side: take the nonce, send back the HMAC.

### method `ChallengeAsync(Stream, ClusterSecurityOptions, CancellationToken)`

Server side: offer a nonce, check the answer, hang up if it is wrong.

### method `ConnectAsync(TcpClient, String, ClusterSecurityOptions, CancellationToken)`

Wraps an outbound connection: client-side TLS, then answer the challenge.

### method `ReadExactlyAsync(Stream, Byte[], CancellationToken)`

Fills the buffer, or throws. A short handshake read is a refusal, not a retry.

## TcpTransport

The default transport: one TCP listener, plus one persistent outbound connection per peer.

Connections are long-lived and per-peer, not per-message. A connection per message costs a three-way handshake on every send, and on Windows it burns through the ephemeral port range under load - the failure mode is a node that works in a demo and dies in a benchmark.

Sends go through a per-connection channel drained by a single writer loop. Two threads writing to the same socket would interleave their bytes and produce frames neither of them sent, so serializing writes is a correctness requirement, not a throughput choice.

Every node listens and every node dials, so replies travel on the sender's own outbound connection to the peer rather than back down the inbound one. That keeps connection identity out of the protocol at the cost of assuming peers are mutually dialable, which holds for the cluster deployments this targets.

### property `BoundPort`

### method `DisposeAsync`

### method `SendAsync(String, WireEnvelope, CancellationToken)`

### method `SendToAddressAsync(String, Int32, WireEnvelope, CancellationToken)`

### method `StartAsync(CancellationToken)`

### method `StopAsync(CancellationToken)`

### field `_inbound`

Inbound connections, keyed by the id the peer stamps on its frames.

Cluster peers are mutually dialable, so a reply to one normally travels on this node's own outbound connection. An external client is not: it dialled in, it is not in the membership table, and there is no address to dial back. Keeping its inbound connection addressable is what lets an SDK client use ask at all.

### field `_serializer`

The allow-list and encoder for message bodies, when this node encodes them itself.

The transport does not otherwise care what a frame contains. It needs this only because the binary format encodes and decodes the body in the same place it encodes the envelope, which is what keeps a frame from being serialized twice.

## WireFormat

Which encoding a frame is written in.

### field `Binary`

A compact binary envelope. Node to node only.

### field `Json`

JSON. The default, and the only thing the Go, Python and Node clients speak.

---

[Back to the index](README.md)
