"""The cluster's placement ring, computed here rather than asked for.

Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

Every node builds this from the member table and reaches the same answer, so a client that builds
it too can send straight to the owner of a key instead of to whichever node it happens to hold a
connection to. Getting it wrong is not fatal - the node forwards - but it is a wasted hop, and a
ring that disagreed subtly would waste it unpredictably.

The hash is FNV-1a followed by the MurmurHash3 finalizer, and both halves matter. FNV-1a alone gave
one node 48% of the keyspace, because ring positions are short strings sharing a prefix and FNV-1a
leaves that structure in the low bits. Nothing here may use Python's own ``hash()``: it is salted
per process, which is exactly the bug that only shows up once there is more than one node.
"""

from __future__ import annotations

import bisect
from typing import Iterable, List, Optional

__all__ = ["HashRing", "ring_hash"]

_MASK = (1 << 64) - 1

_FNV_OFFSET = 14695981039346656037
_FNV_PRIME = 1099511628211


def _mix(value: int) -> int:
    """MurmurHash3's 64-bit finalizer, which spreads the low bits FNV-1a leaves alone."""
    value = (value ^ (value >> 33)) & _MASK
    value = (value * 0xFF51AFD7ED558CCD) & _MASK
    value = (value ^ (value >> 33)) & _MASK
    value = (value * 0xC4CEB9FE1A85EC53) & _MASK
    value = (value ^ (value >> 33)) & _MASK
    return value


def ring_hash(value: str) -> int:
    """The 64-bit position a string maps to.

    Pinned against the values the .NET tests assert: ``ring_hash("a")`` is 0x82A2A958A9BECE5B and
    ``ring_hash("foobar")`` is 0x2C22194922D1672B.
    """
    result = _FNV_OFFSET
    for byte in value.encode("utf-8"):
        result = (result ^ byte) & _MASK
        result = (result * _FNV_PRIME) & _MASK

    return _mix(result)


class HashRing:
    """Consistent hashing over a set of node ids, with virtual nodes to even out the spread."""

    def __init__(self, node_ids: Iterable[str], virtual_nodes: int = 128) -> None:
        # Sorted and de-duplicated, so the same member set in a different order builds the same ring.
        self.nodes: List[str] = sorted(set(node_ids))

        entries = sorted(
            (ring_hash(f"{node}#{replica}"), node)
            for node in self.nodes
            for replica in range(virtual_nodes)
        )

        self._positions = [position for position, _ in entries]
        self._owners = [owner for _, owner in entries]

    @property
    def is_empty(self) -> bool:
        return not self._positions

    def owner_of(self, key: str) -> Optional[str]:
        """The node that owns a key.

        The first ring position at or after the key's hash, wrapping past the end back to the
        start - which is the part that makes it a ring rather than a list.
        """
        if self.is_empty:
            return None

        index = bisect.bisect_left(self._positions, ring_hash(key))
        return self._owners[0 if index == len(self._positions) else index]
