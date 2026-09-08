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
  /** Default '127.0.0.1'. Ignored when `endpoints` is given. */
  host?: string;
  /** Default 9000. Ignored when `endpoints` is given. */
  port?: number;
  /** Nodes as `"host:port"`. Any of them will do; they are tried in rotation. */
  endpoints?: string[];
  /** Must be unique among a node's clients. Generated if omitted. */
  clientId?: string;
  /** Default 10000. */
  askTimeoutMs?: number;
  /**
   * Send straight to the node that owns a key instead of being forwarded. Default false.
   *
   * With it off every frame goes to the node this client is connected to, which forwards it to the
   * owner - correct, and one hop. With it on the client asks for the member table, builds the same
   * ring the cluster uses, and opens a connection per node it addresses. It is an optimisation and
   * never a requirement.
   */
  clusterAware?: boolean;
  /** How long a member table is used before it is asked for again. Default 30000. */
  routesRefreshMs?: number;
}

/** An actor's answer: the alias it replied under, and the body. */
export interface Reply<T = unknown> {
  alias: string;
  payload: T;
}

/**
 * A connection to an ActorNet cluster, through whichever node answers.
 *
 * One persistent socket, not one per message: an ask needs somewhere for the reply to arrive, and
 * the node addresses this client by the `clientId` stamped on every frame. Any node in a cluster is
 * a valid entry point - it forwards to whichever node owns the target actor - which is why a client
 * given several endpoints can move between them without anything else changing.
 *
 * With `clusterAware`, it builds the cluster's ring itself and opens a connection per node it
 * addresses, which saves the forwarding hop and nothing else.
 */
export declare class ActorNetClient {
  constructor(options?: ActorNetClientOptions);

  readonly clientId: string;
  askTimeoutMs: number;
  clusterAware: boolean;
  routesRefreshMs: number;

  /** The endpoints this client may use, as `"host:port"`. */
  readonly endpoints: Array<{ host: string; port: number }>;

  /** The endpoint currently in use, or null when not connected. */
  readonly connectedTo: string | null;

  /** The nodes this client currently holds a connection to. */
  readonly connectedNodes: string[];

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
