"""Checks this client's ring against the numbers every other client pins.

Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

Four implementations of one ring - .NET, Node.js, Python, Go - and nothing but arithmetic keeps them
agreeing. A divergence would not raise: the client would route to a node that does not own the key,
the node would forward it, and everything would work while quietly paying a hop this was added to
avoid. So the same numbers are pinned in every language.

Run: python ring_check.py
"""

from collections import Counter

from actornet.ring import HashRing, ring_hash

# The two vectors the .NET suite asserts, in HashRingTests.
assert format(ring_hash("a"), "X") == "82A2A958A9BECE5B"
assert format(ring_hash("foobar"), "X") == "2C22194922D1672B"

# The ring itself, not just the hash: same members, same virtual node count, same owners.
ring = HashRing(["node-1", "node-2", "node-3"], 128)

OWNERS = {
    "CounterActor/a": "node-2",
    "CounterActor/b": "node-2",
    "CounterActor/c": "node-2",
    "Wallet/1": "node-2",
    "Wallet/2": "node-1",
    "Order/xyz": "node-3",
    "Order/abc": "node-1",
    "x": "node-2",
    "": "node-3",
    "Type/Key": "node-3",
}

for key, owner in OWNERS.items():
    actual = ring.owner_of(key)
    assert actual == owner, f"owner of {key!r} is {actual}, expected {owner}"

# Virtual nodes are what make the spread even. Without them one member routinely takes half the
# keyspace, and the hop this saves would be saved for a third of the traffic instead of all of it.
counts = Counter(ring.owner_of(f"CounterActor/key-{i}") for i in range(20_000))
for owner, count in counts.items():
    assert 0.2 < count / 20_000 < 0.5, f"{owner} holds {count} of 20,000 keys"

print("ring: hash vectors and 10 owners match .NET; spread across 3 nodes is even")
