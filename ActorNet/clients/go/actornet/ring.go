// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

package actornet

import "sort"

// The cluster's placement ring, computed here rather than asked for.
//
// Every node builds this from the member table and reaches the same answer, so a client that builds
// it too can send straight to the owner of a key instead of to whichever node it happens to hold a
// connection to. Getting it wrong is not fatal - the node forwards - but it is a wasted hop, and a
// ring that disagreed subtly would waste it unpredictably.
//
// The hash is FNV-1a followed by the MurmurHash3 finalizer, and both halves matter. FNV-1a alone
// gave one node 48% of the keyspace, because ring positions are short strings sharing a prefix and
// FNV-1a leaves that structure in the low bits. Nothing here may use a runtime's own string hash:
// Go's map hash is seeded per process, which is exactly the bug that only shows up once there is
// more than one node.

const (
	fnvOffset uint64 = 14695981039346656037
	fnvPrime  uint64 = 1099511628211
)

// mix is MurmurHash3's 64-bit finalizer, which spreads the low bits FNV-1a leaves alone.
func mix(value uint64) uint64 {
	value ^= value >> 33
	value *= 0xff51afd7ed558ccd
	value ^= value >> 33
	value *= 0xc4ceb9fe1a85ec53
	value ^= value >> 33
	return value
}

// RingHash is the 64-bit position a string maps to.
//
// Pinned against the values the .NET tests assert: RingHash("a") is 0x82A2A958A9BECE5B and
// RingHash("foobar") is 0x2C22194922D1672B.
func RingHash(value string) uint64 {
	result := fnvOffset
	for _, b := range []byte(value) {
		result ^= uint64(b)
		result *= fnvPrime
	}

	return mix(result)
}

// HashRing is consistent hashing over a set of node ids, with virtual nodes to even the spread.
type HashRing struct {
	nodes     []string
	positions []uint64
	owners    []string
}

// NewHashRing builds the ring. virtualNodes must match the cluster's VirtualNodesPerMember.
func NewHashRing(nodeIDs []string, virtualNodes int) *HashRing {
	if virtualNodes < 1 {
		virtualNodes = 128
	}

	// Sorted and de-duplicated, so the same member set in a different order builds the same ring.
	seen := make(map[string]struct{}, len(nodeIDs))
	nodes := make([]string, 0, len(nodeIDs))
	for _, id := range nodeIDs {
		if _, already := seen[id]; already {
			continue
		}
		seen[id] = struct{}{}
		nodes = append(nodes, id)
	}
	sort.Strings(nodes)

	type entry struct {
		position uint64
		owner    string
	}

	entries := make([]entry, 0, len(nodes)*virtualNodes)
	for _, node := range nodes {
		for replica := 0; replica < virtualNodes; replica++ {
			entries = append(entries, entry{RingHash(node + "#" + itoa(replica)), node})
		}
	}

	sort.Slice(entries, func(i, j int) bool { return entries[i].position < entries[j].position })

	ring := &HashRing{
		nodes:     nodes,
		positions: make([]uint64, len(entries)),
		owners:    make([]string, len(entries)),
	}

	for i, e := range entries {
		ring.positions[i] = e.position
		ring.owners[i] = e.owner
	}

	return ring
}

// Nodes are the member ids this ring was built from, sorted.
func (r *HashRing) Nodes() []string { return r.nodes }

// IsEmpty reports whether the ring has no members, in which case nothing owns anything.
func (r *HashRing) IsEmpty() bool { return r == nil || len(r.positions) == 0 }

// OwnerOf is the node that owns a key: the first ring position at or after the key's hash, wrapping
// past the end back to the start - which is the part that makes it a ring rather than a list.
func (r *HashRing) OwnerOf(key string) string {
	if r.IsEmpty() {
		return ""
	}

	position := RingHash(key)
	index := sort.Search(len(r.positions), func(i int) bool { return r.positions[i] >= position })
	if index == len(r.positions) {
		index = 0
	}

	return r.owners[index]
}

// itoa avoids strconv here only to keep the hashed string identical to the one every other client
// builds: base ten, no padding, no sign.
func itoa(value int) string {
	if value == 0 {
		return "0"
	}

	digits := [20]byte{}
	at := len(digits)
	for value > 0 {
		at--
		digits[at] = byte('0' + value%10)
		value /= 10
	}

	return string(digits[at:])
}
