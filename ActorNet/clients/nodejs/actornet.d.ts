// Type definitions for actornet-client.
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

/** Frame kinds. Must match ActorNet.Serialization.WireKind. */
export declare const WireKind: {
  readonly Message: 1;
  readonly AskRequest: 2;
  readonly AskReply: 3;
  readonly AskFailure: 4;
};

/** Any failure reported by the node or by this client. */
export declare class ActorNetError extends Error {}

/** No reply arrived within the timeout. */
export declare class AskTimeoutError extends ActorNetError {}

export interface ActorNetClientOptions {
  /** Default '127.0.0.1'. */
  host?: string;
  /** Default 9000. */
  port?: number;
  /** Must be unique among a node's clients. Generated if omitted. */
  clientId?: string;
  /** Default 10000. */
  askTimeoutMs?: number;
}

/** An actor's answer: the alias it replied under, and the body. */
export interface Reply<T = unknown> {
  alias: string;
  payload: T;
}

/**
 * A connection to one ActorNet node.
 *
 * One persistent socket, not one per message: an ask needs somewhere for the reply to arrive, and
 * the node addresses this client by the `clientId` stamped on every frame. Any node in a cluster is
 * a valid entry point - it forwards to whichever node owns the target actor.
 */
export declare class ActorNetClient {
  constructor(options?: ActorNetClientOptions);

  readonly host: string;
  readonly port: number;
  readonly clientId: string;
  askTimeoutMs: number;

  /** Opens the connection. `tell` and `ask` call it automatically. */
  connect(): Promise<void>;

  /**
   * Fire-and-forget. Resolves once the frame is written, not once the actor has handled it.
   *
   * @param target Actor address, `"Type/Key"`.
   * @param alias Registered message alias, e.g. `"bank.deposit"`.
   * @param payload The message body; keys are the .NET property names.
   */
  tell(target: string, alias: string, payload: unknown): Promise<void>;

  /**
   * Request/response.
   *
   * @throws {AskTimeoutError} No reply arrived in time.
   * @throws {ActorNetError} The actor failed while handling the request.
   */
  ask<T = unknown>(target: string, alias: string, payload: unknown, timeoutMs?: number): Promise<Reply<T>>;

  /** Closes the connection and rejects anything still waiting. */
  close(): void;
}
