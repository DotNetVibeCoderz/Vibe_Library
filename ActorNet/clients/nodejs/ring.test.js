'use strict';

// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.
//
// Four implementations of one ring - .NET, Node.js, Python, Go - and nothing but arithmetic keeps
// them agreeing. A divergence would not throw: the client would route to a node that does not own
// the key, the node would forward it, and everything would work while quietly paying a hop it was
// added to avoid. So the same numbers are pinned in every language, and this is that file here.
//
// Run: node ring.test.js

const assert = require('node:assert');
const { HashRing, hash } = require('./ring.js');

// The two vectors the .NET suite asserts, in HashRingTests.
assert.strictEqual(hash('a').toString(16).toUpperCase(), '82A2A958A9BECE5B');
assert.strictEqual(hash('foobar').toString(16).toUpperCase(), '2C22194922D1672B');

// The ring itself, not just the hash: same members, same virtual node count, same owners.
const ring = new HashRing(['node-1', 'node-2', 'node-3'], 128);

const owners = {
  'CounterActor/a': 'node-2',
  'CounterActor/b': 'node-2',
  'CounterActor/c': 'node-2',
  'Wallet/1': 'node-2',
  'Wallet/2': 'node-1',
  'Order/xyz': 'node-3',
  'Order/abc': 'node-1',
  x: 'node-2',
  '': 'node-3',
  'Type/Key': 'node-3',
};

for (const [key, owner] of Object.entries(owners)) {
  assert.strictEqual(ring.ownerOf(key), owner, `owner of ${JSON.stringify(key)}`);
}

// Virtual nodes are what make the spread even. Without them one member routinely takes half the
// keyspace, and the hop this saves would be saved for a third of the traffic instead of all of it.
const counts = new Map();
for (let i = 0; i < 20000; i++) {
  const owner = ring.ownerOf(`CounterActor/key-${i}`);
  counts.set(owner, (counts.get(owner) ?? 0) + 1);
}

for (const [owner, count] of counts) {
  assert.ok(count > 20000 * 0.2, `${owner} holds only ${count} of 20,000 keys`);
  assert.ok(count < 20000 * 0.5, `${owner} holds ${count} of 20,000 keys`);
}

console.log('ring: hash vectors and 10 owners match .NET; spread across 3 nodes is even');
