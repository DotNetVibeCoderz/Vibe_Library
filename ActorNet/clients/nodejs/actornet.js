// ActorNet client for Node.js.
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.
//
// Speaks the same wire protocol as the C# client: a 4-byte big-endian payload length followed by
// that many bytes of JSON. There is no separate HTTP gateway to keep in sync - this is the node's
// own protocol.

'use strict';

const net = require('node:net');
const { randomUUID } = require('node:crypto');

/** Frame kinds. Must match ActorNet.Serialization.WireKind. */
const WireKind = Object.freeze({
  Message: 1,
  AskRequest: 2,
  AskReply: 3,
  AskFailure: 4,
});

const HEADER_BYTES = 4;

/** Refused above this, matching the node, so a bad length cannot make either side allocate wildly. */
const MAX_FRAME_BYTES = 32 * 1024 * 1024;

class ActorNetError extends Error {}
class AskTimeoutError extends ActorNetError {}

/**
 * A connection to one ActorNet node.
 *
 * One persistent socket, not one per message: asks need somewhere for the reply to arrive, and
 * the node addresses this client by the `clientId` stamped on every frame. Any node in a cluster
 * is a valid entry point - it forwards to whichever node owns the target actor.
 */
class ActorNetClient {
  /**
   * @param {object} options
   * @param {string} [options.host='127.0.0.1']
   * @param {number} [options.port=9000]
   * @param {string} [options.clientId] Unique among this node's clients. Generated if omitted.
   * @param {number} [options.askTimeoutMs=10000]
   */
  constructor({ host = '127.0.0.1', port = 9000, clientId, askTimeoutMs = 10000 } = {}) {
    this.host = host;
    this.port = port;
    this.clientId = clientId || `node-${randomUUID().slice(0, 12)}`;
    this.askTimeoutMs = askTimeoutMs;

    this._socket = null;
    this._buffer = Buffer.alloc(0);
    this._pending = new Map();
    this._connecting = null;
  }

  /** Opens the connection. Called automatically by tell and ask. */
  connect() {
    if (this._socket && !this._socket.destroyed) return Promise.resolve();
    if (this._connecting) return this._connecting;

    this._connecting = new Promise((resolve, reject) => {
      const socket = net.createConnection({ host: this.host, port: this.port }, () => {
        this._connecting = null;
        resolve();
      });

      socket.setNoDelay(true);
      socket.on('data', (chunk) => this._onData(chunk));
      socket.on('error', (err) => {
        this._connecting = null;
        this._failPending(err);
        reject(err);
      });
      socket.on('close', () => {
        this._failPending(new ActorNetError('The connection to the node closed before a reply arrived.'));
      });

      this._socket = socket;
    });

    return this._connecting;
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
    this._write({
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

      this._write({
        k: WireKind.AskRequest,
        t: target,
        a: alias,
        p: payload,
        c: correlationId,
        // Both fields carry this client's id: `r` is what the actor's reply is routed by, and
        // `f` is what the node keys this connection under so it can find the socket again.
        r: this.clientId,
        f: this.clientId,
      });
    });
  }

  /** Closes the connection and fails anything still waiting. */
  close() {
    this._failPending(new ActorNetError('The client was closed before a reply arrived.'));
    if (this._socket) {
      this._socket.destroy();
      this._socket = null;
    }
  }

  _write(frame) {
    const payload = Buffer.from(JSON.stringify(frame), 'utf8');
    if (payload.length > MAX_FRAME_BYTES) {
      throw new ActorNetError(`Frame of ${payload.length} bytes exceeds the ${MAX_FRAME_BYTES} byte limit.`);
    }

    const header = Buffer.alloc(HEADER_BYTES);
    header.writeInt32BE(payload.length, 0);
    this._socket.write(Buffer.concat([header, payload]));
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
