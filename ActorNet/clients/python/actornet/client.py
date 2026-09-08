"""ActorNet client for Python.

Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

Speaks the node's own wire protocol - a 4-byte big-endian payload length followed by that many
bytes of JSON - so there is no separate gateway to keep in sync with the runtime.
"""

from __future__ import annotations

import asyncio
import json
import struct
import uuid
from dataclasses import dataclass
from typing import Any, Dict, Optional, Sequence

HEADER_BYTES = 4

# Refused above this, matching the node, so a bad length cannot make either side allocate wildly.
MAX_FRAME_BYTES = 32 * 1024 * 1024


from .ring import HashRing


class WireKind:
    """Frame kinds. Must match ActorNet.Serialization.WireKind."""

    MESSAGE = 1
    ASK_REQUEST = 2
    ASK_REPLY = 3
    ASK_FAILURE = 4

    #: Asks a node for the member table. Answered with an ordinary ASK_REPLY.
    CLUSTER_VIEW_REQUEST = 12


class ActorNetError(Exception):
    """Any failure reported by the node or by this client."""


class AskTimeoutError(ActorNetError):
    """No reply arrived within the timeout."""


@dataclass(frozen=True)
class Reply:
    """An actor's answer: the alias it replied under, and the body."""

    alias: str
    payload: Any


class ActorNetClient:
    """A connection to an ActorNet cluster, through whichever node answers.

    One persistent socket, not one per message. An ask needs somewhere for the reply to arrive,
    and the node addresses this client by the ``client_id`` stamped on every frame. Any node in a
    cluster is a valid entry point: it forwards to whichever node owns the target actor - which is
    also why a client given several endpoints can move between them without anything else changing.

    Use as an async context manager::

        async with ActorNetClient(port=9000) as client:
            await client.tell("BankAccountActor/alice", "bank.deposit", {"Amount": 100})
            reply = await client.ask("BankAccountActor/alice", "bank.get-statement", {})
            print(reply.payload["Balance"])

    Give it more than one node and it survives losing the one it dialled::

        async with ActorNetClient(endpoints=["10.0.1.5:9000", "10.0.1.6:9000"]) as client:
            ...
    """

    def __init__(
        self,
        host: str = "127.0.0.1",
        port: int = 9000,
        client_id: Optional[str] = None,
        ask_timeout: float = 10.0,
        endpoints: Optional[Sequence[str]] = None,
        cluster_aware: bool = False,
        routes_refresh: float = 30.0,
    ) -> None:
        configured = list(endpoints) if endpoints else [f"{host}:{port}"]
        self.endpoints = [self._parse(endpoint) for endpoint in configured]

        #: The endpoint currently in use as ``host:port``, or ``None`` when not connected.
        self.connected_to: Optional[str] = None

        self.client_id = client_id or f"py-{uuid.uuid4().hex[:12]}"
        self.ask_timeout = ask_timeout

        # Sticky: it only moves when an endpoint fails. Rotating on every connect would reconnect
        # somewhere new after every blip, which is churn rather than balance - any node forwards by
        # the ring, so moving gains nothing and costs a connection.
        self._cursor = 0

        #: Whether to work out which node owns a key and send straight to it.
        #:
        #: Off by default. With it off every frame goes to the node this client is connected to,
        #: which forwards it to the owner - correct, and one hop. With it on the client asks for
        #: the member table, builds the same ring the cluster uses, and opens a connection per node
        #: it actually addresses. It is an optimisation and never a requirement.
        self.cluster_aware = cluster_aware
        self.routes_refresh = routes_refresh

        self._reader: Optional[asyncio.StreamReader] = None
        self._writer: Optional[asyncio.StreamWriter] = None
        self._pending: Dict[str, asyncio.Future] = {}
        self._read_task: Optional[asyncio.Task] = None
        self._write_lock = asyncio.Lock()

        # One extra connection per node the ring sends this client to. The first connection stays
        # what it was: somewhere to ask for the member table, and somewhere to fall back to.
        self._direct: Dict[str, "_NodeLink"] = {}
        self._ring: Optional[HashRing] = None
        self._node_endpoints: Dict[str, str] = {}
        self._routes_taken_at = 0.0
        self._routing = False

    @staticmethod
    def _parse(endpoint: str) -> tuple:
        """Splits ``host:port``, refusing anything else here rather than at the first call."""
        host, separator, port = endpoint.rpartition(":")
        if not separator or not port.isdigit() or not host:
            raise ActorNetError(f"Endpoint {endpoint!r} is not in 'host:port' form.")
        return host, int(port)

    async def __aenter__(self) -> "ActorNetClient":
        await self.connect()
        return self

    async def __aexit__(self, *_exc_info: Any) -> None:
        await self.close()

    @property
    def is_connected(self) -> bool:
        return self._writer is not None and not self._writer.is_closing()

    async def connect(self) -> None:
        """Opens a connection to whichever endpoint answers.

        Called automatically by tell and ask. Endpoints are tried in rotation from the last one
        that worked, so an ordinary reconnect goes back where it was and only a node that is
        really gone costs a move.
        """
        if self.is_connected:
            return

        last: Optional[BaseException] = None

        for attempt in range(len(self.endpoints)):
            index = (self._cursor + attempt) % len(self.endpoints)
            host, port = self.endpoints[index]

            try:
                self._reader, self._writer = await asyncio.open_connection(host, port)
            except OSError as error:
                last = error
                continue

            self._read_task = asyncio.create_task(self._read_loop())
            self._cursor = index
            self.connected_to = f"{host}:{port}"
            return

        self.connected_to = None
        tried = ", ".join(f"{host}:{port}" for host, port in self.endpoints)
        raise ActorNetError(
            f"None of the {len(self.endpoints)} configured node(s) accepted a connection: {tried}."
        ) from last

    async def tell(self, target: str, alias: str, payload: Any) -> None:
        """Fire-and-forget.

        Returns once the frame is written, not once the actor has handled it.

        Args:
            target: Actor address, ``"Type/Key"``.
            alias: Registered message alias, e.g. ``"bank.deposit"``.
            payload: The message body.
        """
        await self.connect()
        await self._send(
            target,
            {
                "k": WireKind.MESSAGE,
                "t": target,
                "a": alias,
                "p": payload,
                "f": self.client_id,
            },
        )

    async def ask(
        self,
        target: str,
        alias: str,
        payload: Any,
        timeout: Optional[float] = None,
    ) -> Reply:
        """Request/response.

        Raises:
            AskTimeoutError: no reply arrived in time.
            ActorNetError: the actor failed while handling the request.
        """
        await self.connect()

        correlation_id = uuid.uuid4().hex
        window = self.ask_timeout if timeout is None else timeout

        future: asyncio.Future = asyncio.get_running_loop().create_future()
        self._pending[correlation_id] = future

        try:
            await self._send(
                target,
                {
                    "k": WireKind.ASK_REQUEST,
                    "t": target,
                    "a": alias,
                    "p": payload,
                    "c": correlation_id,
                    # Both fields carry this client's id: "r" is what the actor's reply is routed
                    # by, and "f" is what the node keys this connection under.
                    "r": self.client_id,
                    "f": self.client_id,
                },
                correlation_id,
            )

            try:
                frame = await asyncio.wait_for(future, timeout=window)
            except asyncio.TimeoutError as exc:
                raise AskTimeoutError(f"No reply from '{target}' within {window:g}s.") from exc

            if frame.get("k") == WireKind.ASK_FAILURE:
                raise ActorNetError(frame.get("e") or f"Actor '{target}' failed while handling the request.")

            return Reply(alias=frame.get("a", ""), payload=frame.get("p"))
        finally:
            self._pending.pop(correlation_id, None)

    async def _send(self, target: str, frame: Dict[str, Any], correlation_id: Optional[str] = None) -> None:
        """Writes a frame to whoever owns the target, falling back to the first connection.

        Falling back is not a workaround: a node forwards a client's frame to the owner, so a
        client that guesses wrong - or does not guess at all - is slower and not incorrect.
        """
        if not self.cluster_aware:
            await self._write(frame)
            return

        link = await self._owner_link(target)
        if link is None:
            await self._write(frame)
            return

        try:
            await link.write(frame, correlation_id)
        except OSError:
            # The owner went away between being named and being written to.
            self._forget(link)
            await self._write(frame)

    async def _owner_link(self, target: str) -> Optional["_NodeLink"]:
        """The connection to whoever owns this key, or None to use the first connection."""
        await self._refresh_routes()

        if self._ring is None or self._ring.is_empty:
            return None

        owner = self._ring.owner_of(target)
        endpoint = self._node_endpoints.get(owner or "")
        if endpoint is None or endpoint == self.connected_to:
            return None

        existing = self._direct.get(endpoint)
        if existing is not None and existing.is_open:
            return existing

        host, _, port = endpoint.rpartition(":")
        try:
            link = await _NodeLink.open(endpoint, host, int(port), self._pending, self._on_frame, self._forget)
        except OSError:
            # Named by the view and not accepting connections. Forwarding still works.
            return None

        self._direct[endpoint] = link
        return link

    def _forget(self, link: "_NodeLink") -> None:
        """Drops a link that has died and throws the view away with it.

        A link usually dies because the node behind it went away, which is exactly when the member
        table being routed by has stopped being true.
        """
        if self._direct.get(link.endpoint) is link:
            del self._direct[link.endpoint]

        self._routes_taken_at = 0.0
        link.fail(ActorNetError(f"The connection to {link.endpoint} closed before a reply arrived."))

    async def _refresh_routes(self) -> None:
        """Asks for the member table again once the one held is old enough."""
        now = asyncio.get_running_loop().time()
        if self._ring is not None and now - self._routes_taken_at < self.routes_refresh:
            return

        # Somebody else is already asking. Routing by a view a moment out of date is the premise,
        # so waiting for theirs would cost more than it saves.
        if self._routing:
            return

        self._routing = True
        try:
            view = await self._ask_cluster_view()
            if view is None:
                return

            members = view.get("Members") or []
            self._ring = HashRing((m["NodeId"] for m in members), view.get("VirtualNodes", 128))
            self._node_endpoints = {m["NodeId"]: f"{m['Host']}:{m['Port']}" for m in members}
            self._routes_taken_at = asyncio.get_running_loop().time()
        finally:
            self._routing = False

    async def _ask_cluster_view(self) -> Optional[Dict[str, Any]]:
        """Asks the node this client is connected to for the member table."""
        correlation_id = uuid.uuid4().hex
        future: asyncio.Future = asyncio.get_running_loop().create_future()
        self._pending[correlation_id] = future

        try:
            await self._write(
                {
                    "k": WireKind.CLUSTER_VIEW_REQUEST,
                    "c": correlation_id,
                    "r": self.client_id,
                    "f": self.client_id,
                }
            )

            frame = await asyncio.wait_for(future, timeout=5.0)
            return frame.get("p")
        except (asyncio.TimeoutError, OSError, ActorNetError):
            # A node that will not answer leaves this client forwarding, which is what it did
            # before routing existed. Nothing here is worth failing a send over.
            return None
        finally:
            self._pending.pop(correlation_id, None)

    @property
    def connected_nodes(self) -> List[str]:
        """The nodes this client currently holds a connection to."""
        return ([self.connected_to] if self.connected_to else []) + sorted(self._direct)

    async def close(self) -> None:
        """Closes the connection and fails anything still waiting."""
        self._fail_pending(ActorNetError("The client was closed before a reply arrived."))

        for link in list(self._direct.values()):
            self._direct.pop(link.endpoint, None)
            await link.close()

        if self._read_task is not None:
            self._read_task.cancel()
            try:
                await self._read_task
            except (asyncio.CancelledError, Exception):  # noqa: BLE001 - teardown must not raise
                pass
            self._read_task = None

        if self._writer is not None:
            self._writer.close()
            try:
                await self._writer.wait_closed()
            except Exception:  # noqa: BLE001 - the peer may already be gone
                pass
            self._writer = None

    async def _write(self, frame: Dict[str, Any]) -> None:
        body = json.dumps(frame, separators=(",", ":")).encode("utf-8")
        if len(body) > MAX_FRAME_BYTES:
            raise ActorNetError(f"Frame of {len(body)} bytes exceeds the {MAX_FRAME_BYTES} byte limit.")

        # Several coroutines may be telling and asking at once; interleaved writes would produce
        # frames neither of them sent.
        async with self._write_lock:
            assert self._writer is not None
            self._writer.write(struct.pack(">i", len(body)) + body)
            await self._writer.drain()

    async def _read_loop(self) -> None:
        try:
            while True:
                header = await self._reader.readexactly(HEADER_BYTES)  # type: ignore[union-attr]
                (length,) = struct.unpack(">i", header)

                if length <= 0 or length > MAX_FRAME_BYTES:
                    raise ActorNetError(f"Node announced a frame length of {length} bytes.")

                # readexactly is what makes this correct: TCP is a byte stream, so one reply can
                # arrive in several chunks and two replies can arrive in one.
                body = await self._reader.readexactly(length)  # type: ignore[union-attr]
                self._on_frame(json.loads(body.decode("utf-8")))
        except asyncio.CancelledError:
            raise
        except asyncio.IncompleteReadError:
            self._fail_pending(ActorNetError("The connection to the node closed before a reply arrived."))
        except Exception as exc:  # noqa: BLE001 - surfaced to every waiting caller
            self._fail_pending(exc)

    def _on_frame(self, frame: Dict[str, Any]) -> None:
        future = self._pending.pop(frame.get("c", ""), None)
        if future is not None and not future.done():
            future.set_result(frame)

    def _fail_pending(self, error: BaseException) -> None:
        for correlation_id in list(self._pending):
            future = self._pending.pop(correlation_id, None)
            if future is not None and not future.done():
                future.set_exception(error)


class _NodeLink:
    """One extra connection, to a node the ring sent this client to.

    Replies come back on whichever connection the request went out on, so every link completes the
    same pending table. That is what lets these be opened and dropped freely: a link is a route,
    not a session.
    """

    def __init__(self, endpoint: str, reader, writer, pending, on_frame, died) -> None:
        self.endpoint = endpoint

        self._reader = reader
        self._writer = writer
        self._pending = pending
        self._on_frame = on_frame
        self._died = died
        self._lock = asyncio.Lock()

        # The asks that went out on this link and have not been answered. The pending table is
        # shared with every other link, and a link that dies must fail its own asks and nobody
        # else's.
        self._outstanding: set = set()

        self._task = asyncio.create_task(self._read_loop())

    @classmethod
    async def open(cls, endpoint: str, host: str, port: int, pending, on_frame, died) -> "_NodeLink":
        reader, writer = await asyncio.open_connection(host, port)
        return cls(endpoint, reader, writer, pending, on_frame, died)

    @property
    def is_open(self) -> bool:
        return not self._writer.is_closing()

    async def write(self, frame: Dict[str, Any], correlation_id: Optional[str]) -> None:
        # Recorded before the write, because a reply can arrive before the write returns.
        if correlation_id:
            self._outstanding.add(correlation_id)

        body = json.dumps(frame, separators=(",", ":")).encode("utf-8")
        if len(body) > MAX_FRAME_BYTES:
            raise ActorNetError(f"Frame of {len(body)} bytes exceeds the {MAX_FRAME_BYTES} byte limit.")

        async with self._lock:
            self._writer.write(len(body).to_bytes(HEADER_BYTES, "big") + body)
            await self._writer.drain()

    async def _read_loop(self) -> None:
        try:
            while True:
                header = await self._reader.readexactly(HEADER_BYTES)
                length = int.from_bytes(header, "big")
                if length <= 0 or length > MAX_FRAME_BYTES:
                    break

                frame = json.loads(await self._reader.readexactly(length))
                self._outstanding.discard(frame.get("c", ""))
                self._on_frame(frame)
        except (asyncio.IncompleteReadError, OSError, asyncio.CancelledError, ValueError):
            pass
        finally:
            self._died(self)

    def fail(self, error: BaseException) -> None:
        """Fails every ask still waiting on this link, and only those."""
        for correlation_id in list(self._outstanding):
            self._outstanding.discard(correlation_id)
            future = self._pending.pop(correlation_id, None)
            if future is not None and not future.done():
                future.set_exception(error)

    async def close(self) -> None:
        self._task.cancel()
        try:
            await self._task
        except (asyncio.CancelledError, Exception):  # noqa: BLE001 - teardown must not raise
            pass

        self._writer.close()
        try:
            await self._writer.wait_closed()
        except Exception:  # noqa: BLE001 - the peer may already be gone
            pass
