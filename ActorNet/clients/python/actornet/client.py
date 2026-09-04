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
from typing import Any, Dict, Optional

HEADER_BYTES = 4

# Refused above this, matching the node, so a bad length cannot make either side allocate wildly.
MAX_FRAME_BYTES = 32 * 1024 * 1024


class WireKind:
    """Frame kinds. Must match ActorNet.Serialization.WireKind."""

    MESSAGE = 1
    ASK_REQUEST = 2
    ASK_REPLY = 3
    ASK_FAILURE = 4


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
    """A connection to one ActorNet node.

    One persistent socket, not one per message. An ask needs somewhere for the reply to arrive,
    and the node addresses this client by the ``client_id`` stamped on every frame. Any node in a
    cluster is a valid entry point: it forwards to whichever node owns the target actor.

    Use as an async context manager::

        async with ActorNetClient(port=9000) as client:
            await client.tell("BankAccountActor/alice", "bank.deposit", {"Amount": 100})
            reply = await client.ask("BankAccountActor/alice", "bank.get-statement", {})
            print(reply.payload["Balance"])
    """

    def __init__(
        self,
        host: str = "127.0.0.1",
        port: int = 9000,
        client_id: Optional[str] = None,
        ask_timeout: float = 10.0,
    ) -> None:
        self.host = host
        self.port = port
        self.client_id = client_id or f"py-{uuid.uuid4().hex[:12]}"
        self.ask_timeout = ask_timeout

        self._reader: Optional[asyncio.StreamReader] = None
        self._writer: Optional[asyncio.StreamWriter] = None
        self._pending: Dict[str, asyncio.Future] = {}
        self._read_task: Optional[asyncio.Task] = None
        self._write_lock = asyncio.Lock()

    async def __aenter__(self) -> "ActorNetClient":
        await self.connect()
        return self

    async def __aexit__(self, *_exc_info: Any) -> None:
        await self.close()

    @property
    def is_connected(self) -> bool:
        return self._writer is not None and not self._writer.is_closing()

    async def connect(self) -> None:
        """Opens the connection. Called automatically by tell and ask."""
        if self.is_connected:
            return

        self._reader, self._writer = await asyncio.open_connection(self.host, self.port)
        self._read_task = asyncio.create_task(self._read_loop())

    async def tell(self, target: str, alias: str, payload: Any) -> None:
        """Fire-and-forget.

        Returns once the frame is written, not once the actor has handled it.

        Args:
            target: Actor address, ``"Type/Key"``.
            alias: Registered message alias, e.g. ``"bank.deposit"``.
            payload: The message body.
        """
        await self.connect()
        await self._write(
            {
                "k": WireKind.MESSAGE,
                "t": target,
                "a": alias,
                "p": payload,
                "f": self.client_id,
            }
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
            await self._write(
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
                }
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

    async def close(self) -> None:
        """Closes the connection and fails anything still waiting."""
        self._fail_pending(ActorNetError("The client was closed before a reply arrived."))

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
