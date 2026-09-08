'use strict';

// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

/**
 * The cluster's placement ring, computed here rather than asked for.
 *
 * Every node builds this from the member table and reaches the same answer, so a client that builds
 * it too can send straight to the owner of a key instead of to whichever node it happens to hold a
 * connection to. Getting it wrong is not fatal - the node forwards - but it is a wasted hop, and a
 * ring that disagreed subtly would waste it unpredictably.
 *
 * The hash is FNV-1a followed by the MurmurHash3 finalizer, and both halves matter. FNV-1a alone
 * gave one node 48% of the keyspace, because ring positions are short strings sharing a prefix and
 * FNV-1a leaves that structure in the low bits. Nothing here may use a language's own string hash:
 * those differ between runtimes and are randomized per process in several of them, which is exactly
 * the bug that only shows up once there is more than one node.
 */

const MASK = (1n << 64n) - 1n;

const FNV_OFFSET = 14695981039346656037n;
const FNV_PRIME = 1099511628211n;

/** MurmurHash3's 64-bit finalizer, which is what spreads the low bits FNV-1a leaves alone. */
function mix(value) {
  let hash = value;
  hash = (hash ^ (hash >> 33n)) & MASK;
  hash = (hash * 0xff51afd7ed558ccdn) & MASK;
  hash = (hash ^ (hash >> 33n)) & MASK;
  hash = (hash * 0xc4ceb9fe1a85ec53n) & MASK;
  hash = (hash ^ (hash >> 33n)) & MASK;
  return hash;
}

/**
 * The 64-bit position a string maps to. Pinned against the same values the .NET tests assert:
 * hash('a') is 0x82A2A958A9BECE5B and hash('foobar') is 0x2C22194922D1672B.
 *
 * @param {string} value
 * @returns {bigint}
 */
function hash(value) {
  const bytes = Buffer.from(value, 'utf8');

  let result = FNV_OFFSET;
  for (const byte of bytes) {
    result = (result ^ BigInt(byte)) & MASK;
    result = (result * FNV_PRIME) & MASK;
  }

  return mix(result);
}

/** Consistent hashing over a set of node ids, with virtual nodes to even out the spread. */
class HashRing {
  /**
   * @param {string[]} nodeIds
   * @param {number} [virtualNodes=128] Must match the cluster's `VirtualNodesPerMember`.
   */
  constructor(nodeIds, virtualNodes = 128) {
    // Sorted and de-duplicated, so the same member set in a different order builds the same ring.
    this.nodes = [...new Set(nodeIds)].sort();

    this._positions = [];
    this._owners = [];

    const entries = [];
    for (const node of this.nodes) {
      for (let replica = 0; replica < virtualNodes; replica++) {
        entries.push({ position: hash(`${node}#${replica}`), owner: node });
      }
    }

    entries.sort((left, right) => (left.position < right.position ? -1 : left.position > right.position ? 1 : 0));

    for (const entry of entries) {
      this._positions.push(entry.position);
      this._owners.push(entry.owner);
    }
  }

  get isEmpty() {
    return this._positions.length === 0;
  }

  /**
   * The node that owns a key: the first ring position at or after the key's hash, wrapping past
   * the end back to the start - which is the part that makes it a ring rather than a list.
   *
   * @param {string} key Actor address, "Type/Key".
   * @returns {string|null} The owning node id, or null when the ring is empty.
   */
  ownerOf(key) {
    if (this.isEmpty) return null;

    const position = hash(key);

    let low = 0;
    let high = this._positions.length;
    while (low < high) {
      const middle = (low + high) >> 1;
      if (this._positions[middle] < position) low = middle + 1;
      else high = middle;
    }

    return this._owners[low === this._positions.length ? 0 : low];
  }
}

module.exports = { HashRing, hash };
