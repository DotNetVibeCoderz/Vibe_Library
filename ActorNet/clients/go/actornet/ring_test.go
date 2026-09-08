// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

package actornet

import (
	"fmt"
	"testing"
)

// Four implementations of one ring - .NET, Node.js, Python, Go - and nothing but arithmetic keeps
// them agreeing. A divergence would not fail anything at runtime: the client would route to a node
// that does not own the key, the node would forward it, and everything would work while quietly
// paying a hop the routing was added to avoid. So the same numbers are pinned in every language,
// and for Go this file is the only thing that ever runs them - there is no Go toolchain on the
// machine this client was written on.

func TestRingHashMatchesTheOtherClients(t *testing.T) {
	// The two vectors the .NET suite asserts, in HashRingTests.
	if got := fmt.Sprintf("%X", RingHash("a")); got != "82A2A958A9BECE5B" {
		t.Fatalf(`RingHash("a") = %s, want 82A2A958A9BECE5B`, got)
	}

	if got := fmt.Sprintf("%X", RingHash("foobar")); got != "2C22194922D1672B" {
		t.Fatalf(`RingHash("foobar") = %s, want 2C22194922D1672B`, got)
	}
}

func TestRingOwnersMatchTheOtherClients(t *testing.T) {
	// The ring itself, not just the hash: same members, same virtual node count, same owners.
	ring := NewHashRing([]string{"node-1", "node-2", "node-3"}, 128)

	owners := map[string]string{
		"CounterActor/a": "node-2",
		"CounterActor/b": "node-2",
		"CounterActor/c": "node-2",
		"Wallet/1":       "node-2",
		"Wallet/2":       "node-1",
		"Order/xyz":      "node-3",
		"Order/abc":      "node-1",
		"x":              "node-2",
		"":               "node-3",
		"Type/Key":       "node-3",
	}

	for key, want := range owners {
		if got := ring.OwnerOf(key); got != want {
			t.Errorf("OwnerOf(%q) = %q, want %q", key, got, want)
		}
	}
}

func TestRingSpreadsEvenly(t *testing.T) {
	// Virtual nodes are what make the spread even. Without them one member routinely takes half
	// the keyspace, and the hop this saves would be saved for a third of the traffic instead of
	// all of it.
	ring := NewHashRing([]string{"node-1", "node-2", "node-3"}, 128)

	counts := map[string]int{}
	for i := 0; i < 20000; i++ {
		counts[ring.OwnerOf(fmt.Sprintf("CounterActor/key-%d", i))]++
	}

	for owner, count := range counts {
		if count < 4000 || count > 10000 {
			t.Errorf("%s holds %d of 20,000 keys", owner, count)
		}
	}
}

func TestAnEmptyRingOwnsNothing(t *testing.T) {
	// A client that has not got a member table yet holds one of these, and it must say so rather
	// than name a node nobody asked about.
	if owner := NewHashRing(nil, 128).OwnerOf("CounterActor/a"); owner != "" {
		t.Errorf("an empty ring named %q as an owner", owner)
	}
}
