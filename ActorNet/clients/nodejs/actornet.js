// ActorNet client for Node.js.
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.
//
// Speaks the same wire protocol as the C# client: a 4-byte big-endian payload length followed by
// that many bytes of JSON. There is no separate HTTP gateway to keep in sync - this is the node's
// own protocol.

'use strict';

const net = require('node:net');
const { randomUUID } = require('node:crypto');
const { HashRing } = require('./ring.js');

/** Frame kinds. Must match ActorNet.Serialization.WireKind. */
const WireKind = Object.freeze({
  Message: 1,
  AskRequest: 2,
  AskReply: 3,
  AskFailure: 4,

  /** Asks a node for the member table. Answered with an ordinary AskReply. */
  ClusterViewRequest: 12,
});

const HEADER_BYTES = 4;

/** Refused above this, matching the node, so a bad length cannot make either side allocate wildly. */
const MAX_FRAME_BYTES = 32 * 1024 * 1024;

class ActorNetError extends Error {}
class AskTimeoutError extends ActorNetError {}

/**
 * A connection to an ActorNet cluster, through whichever node answers.
 *
 * One persistent socket, not one per message: asks need somewhere for the reply to arrive, and
 * the node addresses this client by the `clientId` stamped on every frame. Any node in a cluster
 * is a valid entry point - it forwards to whichever node owns the target actor - which is also why
 * a client given several endpoints can move between them without anything else changing.
 *
 * With `clusterAware`, it asks a node for the member table, builds the same ring the cluster uses,
 * and opens a connection per node it actually addresses. That saves the forwarding hop and nothing
 * else: a client with a stale view, or none, is still correct.
 */
class ActorNetClient {
  /**
   * @param {object} options
   * @param {string} [options.host='127.0.0.1'] Ignored when `endpoints` is given.
   * @param {number} [options.port=9000] Ignored when `endpoints` is given.
   * @param {string[]} [options.endpoints] Nodes as "host:port". Any of them will do.
   * @param {string} [options.clientId] Unique among this node's clients. Generated if omitted.
   * @param {number} [options.askTimeoutMs=10000]
   * @param {boolean} [options.clusterAware=false] Send straight to the owner instead of being forwarded.
   * @param {number} [options.routesRefreshMs=30000] How long a member table is used before asking again.
   */
  constructor({
    host = '127.0.0.1',
    port = 9000,
    endpoints,
    clientId,
    askTimeoutMs = 10000,
    clusterAware = false,
    routesRefreshMs = 30000,
  } = {}) {
    const configured = endpoints && endpoints.length ? endpoints : [`${host}:${port}`];
    this.endpoints = configured.map((endpoint) => {
      const colon = endpoint.lastIndexOf(':');
      const parsed = Number.parseInt(endpoint.slice(colon + 1), 10);
      if (colon <= 0 || Number.isNaN(parsed)) {
        // Refused here rather than at the first call, which could be hours later.
        throw new ActorNetError(`Endpoint '${endpoint}' is not in 'host:port' form.`);
      }
      return { host: endpoint.slice(0, colon), port: parsed };
    });

    /** The endpoint currently in use, or null when not connected. */
    this.connectedTo = null;

    this.clientId = clientId || `node-${randomUUID().slice(0, 12)}`;
    this.askTimeoutMs = askTimeoutMs;

    this.clusterAware = clusterAware;
    this.routesRefreshMs = routesRefreshMs;

    this._socket = null;
    this._buffer = Buffer.alloc(0);
    this._pending = new Map();
    this._connecting = null;

    // One extra connection per node the ring sends this client to. The first connection stays what
    // it was - somewhere to ask for the member table, and somewhere to fall back to.
    this._direct = new Map();
    this._routes = null;
    this._routesTakenAt = 0;
    this._routing = null;

    // Sticky: it only moves when an endpoint fails. Rotating on every connect would reconnect
    // somewhere new after every blip, which is churn rather than balance - any node forwards by
    // the ring, so moving gains nothing and costs a connection.
    this._cursor = 0;
  }

  /** Opens a connection to whichever endpoint answers. Called automatically by tell and ask. */
  connect() {
    if (this._socket && !this._socket.destroyed) return Promise.resolve();
    if (this._connecting) return this._connecting;

    this._connecting = this._connectAny().finally(() => {
      this._connecting = null;
    });

    return this._connecting;
  }

  async _connectAny() {
    let last = null;

    for (let attempt = 0; attempt < this.endpoints.length; attempt++) {
      const index = (this._cursor + attempt) % this.endpoints.length;
      const { host, port } = this.endpoints[index];

      try {
        await this._dial(host, port);
        this._cursor = index;
        this.connectedTo = `${host}:${port}`;
        return;
      } catch (err) {
        last = err;
      }
    }

    this.connectedTo = null;
    const tried = this.endpoints.map((e) => `${e.host}:${e.port}`).join(', ');
    throw new ActorNetError(
      `None of the ${this.endpoints.length} configured node(s) accepted a connection: ${tried}.`,
      { cause: last },
    );
  }

  /** One connection attempt. Rejects rather than throwing, so the caller can try the next one. */
  _dial(host, port) {
    return new Promise((resolve, reject) => {
      const socket = net.createConnection({ host, port }, () => {
        // Handed over: from here an error is a live-connection failure rather than a failed dial,
        // and must fail whatever is waiting instead of rejecting a promise nobody is holding.
        socket.removeListener('error', onDialError);
        socket.on('error', (err) => this._failPending(err));
        socket.on('close', () => {
          this.connectedTo = null;
          this._failPending(new ActorNetError('The connection to the node closed before a reply arrived.'));
        });

        resolve();
      });

      const onDialError = (err) => {
        socket.destroy();
        reject(err);
      };

      socket.setNoDelay(true);
      socket.on('data', (chunk) => this._onData(chunk));
      socket.once('error', onDialError);

      this._socket = socket;
    });
  }

  /**
   * Fire-and-forget. Resolves once the frame is written, not when the actor has handled it.
   *
   * @param {string} target Actor address, "Type/Key".
   * @param {string} alias Registered message alias, e.g. "bank.deposit".
   * @param {object} payload The message body.
   */
  async tell(target, alias, payload) {
    await this.connect();
    await this._send(target, {
      k: WireKind.Message,
      t: target,
      a: alias,
      p: payload,
      f: this.clientId,
    });
  }

  /**
   * Request/response.
   *
   * @param {string} target Actor address, "Type/Key".
   * @param {string} alias Registered message alias.
   * @param {object} payload The message body.
   * @param {number} [timeoutMs] Overrides the client default.
   * @returns {Promise<{alias: string, payload: object}>} The reply's alias and body.
   */
  async ask(target, alias, payload, timeoutMs) {
    await this.connect();

    const correlationId = randomUUID().replace(/-/g, '');
    const timeout = timeoutMs ?? this.askTimeoutMs;

    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        this._pending.delete(correlationId);
        reject(new AskTimeoutError(`No reply from '${target}' within ${timeout} ms.`));
      }, timeout);

      this._pending.set(correlationId, { resolve, reject, timer });

      this._send(target, {
        k: WireKind.AskRequest,
        t: target,
        a: alias,
        p: payload,
        c: correlationId,
        // Both fields carry this client's id: `r` is what the actor's reply is routed by, and
        // `f` is what the node keys this connection under so it can find the socket again.
        r: this.clientId,
        f: this.clientId,
      }).catch((err) => {
        const waiting = this._pending.get(correlationId);
        if (!waiting) return;

        this._pending.delete(correlationId);
        clearTimeout(waiting.timer);
        waiting.reject(err);
      });
    });
  }

  /**
   * Writes a frame to whoever owns the target, falling back to the first connection.
   *
   * Falling back is not a workaround: a node forwards a client's frame to the owner, so a client
   * that guesses wrong - or does not guess at all - is slower and not incorrect.
   */
  async _send(target, frame) {
    if (!this.clusterAware) {
      this._write(frame);
      return;
    }

    const link = await this._ownerLink(target);
    if (!link) {
      this._write(frame);
      return;
    }

    try {
      this._writeTo(link.socket, frame);
    } catch (err) {
      // The owner went away between being named and being written to.
      this._forget(link);
      this._write(frame);
    }
  }

  /** The connection to whoever owns this key, or null to use the first connection. */
  async _ownerLink(target) {
    const routes = await this._routesFor();
    if (!routes || routes.ring.isEmpty) return null;

    const owner = routes.ring.ownerOf(target);
    const endpoint = routes.endpoints.get(owner);
    if (!endpoint || endpoint === this.connectedTo) return null;

    const existing = this._direct.get(endpoint);
    if (existing && !existing.socket.destroyed) return existing;

    const colon = endpoint.lastIndexOf(':');
    try {
      return await this._dialDirect(endpoint, endpoint.slice(0, colon), Number.parseInt(endpoint.slice(colon + 1), 10));
    } catch (err) {
      // Named by the view and not accepting connections. Forwarding still works.
      return null;
    }
  }

  /** A second socket, feeding the same pending table - a link is a route, not a session. */
  _dialDirect(endpoint, host, port) {
    return new Promise((resolve, reject) => {
      let buffer = Buffer.alloc(0);
      const link = { endpoint, socket: null, outstanding: new Set() };

      const socket = net.createConnection({ host, port }, () => {
        socket.removeListener('error', onDialError);
        socket.on('error', () => this._forget(link));
        socket.on('close', () => this._forget(link));

        this._direct.set(endpoint, link);
        resolve(link);
      });

      const onDialError = (err) => {
        socket.destroy();
        reject(err);
      };

      socket.setNoDelay(true);
      socket.once('error', onDialError);
      socket.on('data', (chunk) => {
        buffer = Buffer.concat([buffer, chunk]);

        while (buffer.length >= HEADER_BYTES) {
          const length = buffer.readInt32BE(0);
          if (length <= 0 || length > MAX_FRAME_BYTES) {
            this._forget(link);
            return;
          }

          if (buffer.length < HEADER_BYTES + length) return;

          const body = buffer.subarray(HEADER_BYTES, HEADER_BYTES + length);
          buffer = buffer.subarray(HEADER_BYTES + length);

          const frame = JSON.parse(body.toString('utf8'));
          link.outstanding.delete(frame.c);
          this._onFrame(frame);
        }
      });

      link.socket = socket;
    });
  }

  /**
   * Drops a link that has died and throws the view away with it.
   *
   * Only this link's asks are failed. The pending table is shared with every other connection, and
   * failing all of them would take down asks travelling on ones that are perfectly healthy.
   */
  _forget(link) {
    if (this._direct.get(link.endpoint) === link) this._direct.delete(link.endpoint);

    // A link usually dies because the node behind it went away, which is exactly when the member
    // table being routed by has stopped being true.
    this._routesTakenAt = 0;

    for (const correlationId of link.outstanding) {
      const waiting = this._pending.get(correlationId);
      if (!waiting) continue;

      this._pending.delete(correlationId);
      clearTimeout(waiting.timer);
      waiting.reject(new ActorNetError(`The connection to ${link.endpoint} closed before a reply arrived.`));
    }

    link.outstanding.clear();
    link.socket.destroy();
  }

  /** The member table, asked for again once it is old enough. */
  async _routesFor() {
    if (this._routes && Date.now() - this._routesTakenAt < this.routesRefreshMs) return this._routes;

    // Somebody else is already asking. Routing by a view a moment out of date is the premise, so
    // waiting for theirs would cost more than it saves.
    if (this._routing) return this._routes;

    this._routing = this._askClusterView().finally(() => {
      this._routing = null;
    });

    const view = await this._routing;
    if (!view) return this._routes;

    this._routes = {
      ring: new HashRing(view.Members.map((m) => m.NodeId), view.VirtualNodes),
      endpoints: new Map(view.Members.map((m) => [m.NodeId, `${m.Host}:${m.Port}`])),
    };

    this._routesTakenAt = Date.now();
    return this._routes;
  }

  /** Asks the node this client is connected to for the member table. */
  _askClusterView() {
    return new Promise((resolve) => {
      const correlationId = randomUUID().replace(/-/g, '');

      const timer = setTimeout(() => {
        this._pending.delete(correlationId);
        resolve(null);
      }, 5000);

      this._pending.set(correlationId, {
        resolve: (reply) => resolve(reply.payload),
        // A node that will not answer leaves this client forwarding, which is what it did before
        // routing existed. Nothing here is worth failing a send over.
        reject: () => resolve(null),
        timer,
      });

      try {
        this._write({
          k: WireKind.ClusterViewRequest,
          c: correlationId,
          r: this.clientId,
          f: this.clientId,
        });
      } catch (err) {
        this._pending.delete(correlationId);
        clearTimeout(timer);
        resolve(null);
      }
    });
  }

  /** Closes the connection and fails anything still waiting. */
  close() {
    this._failPending(new ActorNetError('The client was closed before a reply arrived.'));

    for (const link of [...this._direct.values()]) {
      this._direct.delete(link.endpoint);
      link.socket.destroy();
    }

    if (this._socket) {
      this._socket.destroy();
      this._socket = null;
    }
  }

  /** The nodes this client currently holds a connection to. */
  get connectedNodes() {
    return [...(this.connectedTo ? [this.connectedTo] : []), ...[...this._direct.keys()].sort()];
  }

  _write(frame) {
    this._writeTo(this._socket, frame);
  }

  _writeTo(socket, frame) {
    const payload = Buffer.from(JSON.stringify(frame), 'utf8');
    if (payload.length > MAX_FRAME_BYTES) {
      throw new ActorNetError(`Frame of ${payload.length} bytes exceeds the ${MAX_FRAME_BYTES} byte limit.`);
    }

    // Recorded before the write, because a reply can arrive before write() returns.
    if (frame.c && socket !== this._socket) {
      const link = [...this._direct.values()].find((candidate) => candidate.socket === socket);
      if (link) link.outstanding.add(frame.c);
    }

    const header = Buffer.alloc(HEADER_BYTES);
    header.writeInt32BE(payload.length, 0);
    socket.write(Buffer.concat([header, payload]));
  }

  /**
   * TCP is a byte stream, so a chunk is not a frame: two replies can arrive coalesced and one
   * large reply arrives in pieces. Buffer until a whole length-prefixed frame is present.
   */
  _onData(chunk) {
    this._buffer = Buffer.concat([this._buffer, chunk]);

    while (this._buffer.length >= HEADER_BYTES) {
      const length = this._buffer.readInt32BE(0);
      if (length <= 0 || length > MAX_FRAME_BYTES) {
        this._failPending(new ActorNetError(`Node announced a frame length of ${length} bytes.`));
        this.close();
        return;
      }

      if (this._buffer.length < HEADER_BYTES + length) return;

      const body = this._buffer.subarray(HEADER_BYTES, HEADER_BYTES + length);
      this._buffer = this._buffer.subarray(HEADER_BYTES + length);
      this._onFrame(JSON.parse(body.toString('utf8')));
    }
  }

  _onFrame(frame) {
    const waiting = this._pending.get(frame.c);
    if (!waiting) return;

    this._pending.delete(frame.c);
    clearTimeout(waiting.timer);

    if (frame.k === WireKind.AskFailure) {
      waiting.reject(new ActorNetError(frame.e || 'The actor failed while handling the request.'));
      return;
    }

    waiting.resolve({ alias: frame.a, payload: frame.p });
  }

  _failPending(error) {
    for (const [id, waiting] of this._pending) {
      this._pending.delete(id);
      clearTimeout(waiting.timer);
      waiting.reject(error);
    }
  }
}

module.exports = { ActorNetClient, ActorNetError, AskTimeoutError, WireKind };
