# ActorNet.Cluster

Generated - edit the `///` comments in the source, not this file.

## ClusterMember

One node as seen from another.

- `NodeId` — Stable identity. This, not the address, is what the ring hashes.
- `Host` — Host the node's transport listens on.
- `Port` — Port the node's transport listens on.
- `Status` — Current status in the observer's view.
- `LastSeen` — When the observer last had evidence this node was alive.
- `Incarnation` — Monotonic per-node counter used to break gossip ties. A node that restarts comes back with a higher incarnation, so its own view of itself always beats a stale peer's memory of it.

### method `ClusterMember(String, String, Int32, MemberStatus, DateTimeOffset, Int64)`

One node as seen from another.

- `NodeId` — Stable identity. This, not the address, is what the ring hashes.
- `Host` — Host the node's transport listens on.
- `Port` — Port the node's transport listens on.
- `Status` — Current status in the observer's view.
- `LastSeen` — When the observer last had evidence this node was alive.
- `Incarnation` — Monotonic per-node counter used to break gossip ties. A node that restarts comes back with a higher incarnation, so its own view of itself always beats a stale peer's memory of it.

### property `Address`

Address in `host:port` form.

### property `Host`

Host the node's transport listens on.

### property `Incarnation`

Monotonic per-node counter used to break gossip ties. A node that restarts comes back with a higher incarnation, so its own view of itself always beats a stale peer's memory of it.

### property `IsRoutable`

True when this member should be placed on the hash ring.

### property `LastSeen`

When the observer last had evidence this node was alive.

### property `NodeId`

Stable identity. This, not the address, is what the ring hashes.

### property `Port`

Port the node's transport listens on.

### property `Status`

Current status in the observer's view.

## ClusterMembership

Membership, failure detection and key placement for one node.

The protocol is deliberately small: a joiner sends `Join` to its seeds and gets back the seed's member table; from then on every node periodically sends its whole table to `GossipFanout` of its peers, taken in rotation, and the rest of the cluster hears it second-hand. That is gossip in the loose sense - it converges in about log(members) rounds at a cost of O(members x fanout) frames per interval.

Failure detection is phi-accrual by default: suspicion is measured against how long a peer's beats have actually been taking, so the same threshold is patient with a slow link and quick with a fast one. Either way a suspected peer is only `Unreachable` and stays *on* the ring; it takes the higher threshold to take it off, because the usual cause of silence is a pause rather than a departure.

A node's own entry is never overwritten by what a peer thinks of it. If a peer reports us unreachable, we bump our incarnation, which makes our own view win everywhere it spreads. That is the one piece of SWIM worth having without the rest of it.

### method `ClusterMembership(String, String, Int32, ClusterOptions, ILogger)`

- `advertisedHost` — What peers are told to dial - not necessarily what the listener binds to. A node binding every interface still has to advertise one address a peer can actually reach.
- `advertisedPort` — The port peers dial. Superseded by `SetAdvertisedPort` once the listener has bound, unless the caller pinned one.

### method `AdoptAgreed(Int64, List<String>)`

Takes the agreed membership from a peer when the peer's is newer.

Higher epoch wins, and nothing else does - not a longer list, not a more recent frame. The epoch is only ever raised by a node that can see every member it knows of, so a higher one is evidence somebody had a complete view more recently than this node did.

This is the whole of the protocol between two halves of a partition, and it runs entirely before the partition starts. Once the halves are cut off, neither can see a complete cluster and neither raises the epoch, so both keep measuring against the last set they held in common - which is what stops them both concluding they are the majority.

### property `AdvertisedAddress`

The address this node advertises, for diagnostics and the console.

### property `AgreedMembership`

The membership every node last agreed on, and how many times it has been restated.

Exposed for the frames that carry it and for the tests that assert two nodes converged on the same one. It is only meaningful next to its epoch, which is why they travel together.

### property `Coordinator`

The member that hands out the leave token: the lowest routable node id.

Computed rather than elected, so every node reaches the same answer from its own member table without a round trip, and a coordinator that leaves is replaced by the arithmetic rather than by a protocol. Ordinal comparison, because the answer has to be the same on every machine and a culture-aware one is not.

### method `CurrentSeedsAsync(CancellationToken)`

The seeds to try now, from the configured source or the configured list.

A source that throws costs this attempt and nothing more. Joining is already a thing that retries, and a directory being briefly unavailable is exactly what the retry is for.

### method `DecideLeave(LeaveRequest)`

Answers a leave request, as the coordinator.

### method `DisposeAsync`

### method `DownSelf(String)`

Takes this node off the ring and tells the host, which is expected to stop.

### method `EvaluatePartition(DateTimeOffset)`

Decides whether this node is on the side of a partition that should keep serving.

Runs after failure detection, so it judges statuses this beat has already settled. It acts only once the reachable set has held still for `SplitBrainStabilityWindow`: a partition is rarely a clean cut, and deciding on the first observation means deciding against a membership still in motion.

### method `GossipAsync(CancellationToken)`

Runs one gossip beat. Internal so a test can drive the rotation a beat at a time.

### method `HandleFrame(WireEnvelope)`

Handles a membership frame. Returns a reply frame when the protocol calls for one.

### property `IsCoordinator`

Whether this node is the one that hands out the leave token.

### method `IsLocal(ActorId)`

### method `IsMember(String)`

Whether `nodeId` is a member of this cluster.

Used to tell a peer's frame from a client's. A client stamps its own id on every frame and is never in the member table, which is the only thing that distinguishes the two at the point a frame arrives.

### property `IsSingleNode`

### method `JoinOneSeedAsync(String, String, Int32, Int32, WireEnvelope, CancellationToken)`

Resolves one seed and joins every node behind it. True when any of them answered.

Resolution belongs inside this, not in the loop that calls it: doing it up front would put every seed behind the slowest name lookup, which is the same starvation that contacting the seeds one at a time caused.

### method `Judge(String, ClusterMember, DateTimeOffset)`

What this node now believes about a peer, given how long it has been quiet.

### method `LeaveAsync(CancellationToken)`

Announces departure so peers take this node off the ring without waiting for a timeout.

Written straight to a socket rather than queued on the peer connection, for the same reason the warm handoff is: a queued frame is written by a writer loop, and `PeerConnection.DisposeAsync` cancels that loop before it drains, so anything still in the queue when this node closes its transport is dropped. A goodbye is sent moments before exactly that, and losing it costs the peer a full failure-detector deadline for a node that had said where it was going.

The window is narrow - the loop usually drains first, and no test here could force the loss - so this is closing a hazard the code plainly has rather than one that was reproduced.

### property `LeaveTokenHeldBy`

The node holding the leave token, when this node is the coordinator.

### property `Members`

### event `MembershipChanged`

### method `NextGossipTargets`

The peers this beat gossips to, taken in rotation.

Sorted by node id so every node walks the same list in the same order, and advanced by a cursor so consecutive beats cover different peers. The rotation is what makes the gap between any two nodes a fixed number of beats rather than a matter of luck - the failure detector can learn a fixed gap and cannot learn a coin flip.

### method `OwnerIsReachable(ActorId)`

Whether the node that owns `id` is currently answering.

An unreachable member stays on the ring on purpose - moving its keys on the first missed heartbeat would cost a wave of deactivations every time a node paused - so the owner of a key can be a node nobody can talk to. That is worth asking about separately from who owns it.

### method `OwnerOf(ActorId)`

### method `RecordContact(String)`

Notes that a node is alive, because a frame just arrived from it.

### method `RejoinIfAloneAsync(CancellationToken)`

Runs the seed handshake again while this node knows no routable peer.

The handshake used to be attempted exactly once, at startup. A node whose seeds were not reachable in that instant stayed alone forever, even after they came up seconds later - and "not reachable in that instant" is the normal case, not the exceptional one: nodes start in arbitrary order, and a single dropped packet on a wireless link is enough.

Found by running two nodes on two machines. Every test until then started the seed first, on loopback, where a connect does not transiently fail - so the suite could not have caught it.

This also covers recovery: a node whose only peers have all gone `Down` starts seeking them out again rather than waiting for someone else to make the first move.

### method `Resolve(String)`

Address lookup for the transport.

### method `ResolveAsync(String, CancellationToken)`

Every address a seed host stands for, or the host itself when there is nothing to resolve.

An unresolvable name is returned unchanged rather than dropped, so the failure surfaces as a connect error naming the seed - which is what somebody debugging a typo needs to see - and not as a seed that silently stopped being tried.

### property `Ring`

### event `SelfDowned`

Raised when this node has decided it is on the losing side of a partition.

The node has already taken itself off the ring by the time this fires. The host is expected to stop: a node that keeps serving actors it no longer owns is the thing the whole strategy exists to prevent. Rejoining means starting again.

### property `SelfNodeId`

### method `SendJoinAsync(String, String, Int32, WireEnvelope, CancellationToken)`

Sends one join frame. Never throws: a seed that is down is an ordinary outcome.

### method `SetAdvertisedPort(Int32)`

Sets the port peers are told to dial, once the transport has bound one.

This is what resolves a configured port of zero: the real port is only known after the listener starts. A caller that pinned an advertised port passes that instead, which is the published-port case.

### method `StartAsync(ITransport, CancellationToken)`

Starts membership: contacts the seeds, then beats on a timer.

### method `SurvivesMajority(IReadOnlyCollection<String>, IReadOnlyCollection<String>)`

Whether a side holding `reachable` outvotes the membership everyone last agreed on.

An exact half survives only if it holds the lowest node id. Without that tiebreak a two-node cluster would lose both halves to a single broken link, which is a worse outcome than the split brain being guarded against.

### method `UpdateExpectedHeartbeatInterval`

Tells the detector how often a peer's beats are actually due, given the fanout.

With a fanout, a peer is heard from every `ceil(peers / fanout)` beats rather than every beat. A detector still expecting one beat per interval would suspect every newly discovered member in a large cluster before its window had filled with the truth.

## ClusterOptions

Membership, failure detection and placement settings.

### property `AcceptableHeartbeatPause`

Slack added to a peer's average interval before suspicion begins.

This is the stop-the-world allowance. On a link whose beats are metronomic the measured spread is tiny, and without slack a GC pause a fraction of a second past the usual interval would read as a failure.

### property `CoordinatedLeave`

Whether a node asks the cluster for permission before it leaves gracefully.

Off by default, and worth turning on for a rolling restart. Nothing otherwise stops two nodes stopping at the same moment: the keys of the first move to the second while the second is on its way out, so they move twice, and with a quorum configured the pair can take the cluster below it in one step.

One node holds the token at a time. The member with the lowest id hands it out - the same tiebreak the split-brain resolver uses - and every node works that out from its own member table rather than being told, so there is no election to run and nothing to fail over.

This is not consensus, and it is not a distributed lock. While the member table is in flux two nodes can briefly disagree about who the coordinator is, and then both can be granted. It orders the shutdowns of a healthy cluster, which is what a rolling restart is; it does not defend against a partition. `SplitBrainStrategy` is what does that.

### property `DownAfter`

Silence after which a peer is marked `Down` and removed from the ring, handing its keys to the remaining nodes. Must be longer than `UnreachableAfter`, or a brief GC pause would trigger a rebalance.

### property `Enabled`

False keeps the node standalone: every key is owned locally and no membership traffic is sent. A standalone node still has a one-member `IClusterView`, so application code does not branch on this.

### property `FailureDetection`

How a peer's silence is judged.

`PhiAccrual` is the default because a fixed deadline has to be set for the worst link in the cluster and is then too slow on all the others. `Deadline` remains for the case where a flat, stated number is worth more than accuracy - a test that wants a node down at a known second, most of all.

### property `GossipFanout`

How many peers this node gossips to per beat. Zero means every peer.

Gossiping to everybody costs O(members squared) frames per interval, which is nothing at ten nodes and ruinous at a hundred. A fanout spreads the same information epidemically at O(members x fanout), converging in about log(members) rounds instead of one.

Peers are taken in a rotation rather than at random. A random subset makes the gap between two particular nodes a coin flip, and the failure detector then has to be tolerant of a silence that was merely unlucky. A rotation makes that gap exactly `ceil(peers / fanout)` beats, which is a number the detector can learn.

### property `HeartbeatInterval`

How often this node gossips its member table to a peer.

### property `HeartbeatSampleSize`

How many heartbeat intervals the detector remembers per peer.

### property `JoinTimeout`

How long the startup handshake may spend on its seeds before the node comes up anyway.

A seed behind a firewall that drops packets rather than refusing them is not refused: the connect sits there until the OS gives up, which is a minute or more. Without a deadline that is a minute of a node not starting. Coming up alone is safe, because the handshake is retried on every beat until a peer answers.

### property `LeaveTokenLease`

How long a granted leave token stays granted without being released.

The holder releases the token once it has announced its departure. This is the backstop for the holder that never does - it was killed midway - because a token nobody can release stops every other node from leaving until the coordinator itself restarts.

### property `LeaveTokenWait`

How long a node waits for the leave token before going anyway.

A shutdown that blocks forever is worse than an uncoordinated one: an orchestrator that asked a pod to stop will kill it instead, and a killed node leaves without flushing or announcing anything. So the wait is bounded, and a node that runs out of budget leaves and says in its log that it did.

### property `MinimumStandardDeviation`

A floor on the measured spread of a peer's heartbeat intervals.

Without a floor, a peer that has been perfectly regular gets a standard deviation near zero, and phi jumps from nothing to enormous a millisecond past its usual interval.

### property `PhiDownThreshold`

Suspicion at which a peer is taken off the ring, in phi. Must exceed the other one.

### property `PhiUnreachableThreshold`

Suspicion at which a peer is marked `Unreachable`, in phi.

Phi is a logarithm: 8 means the detector expects to be wrong about once in 10^8 given how this peer normally behaves. Lowering it notices failures sooner and cries wolf more often.

### property `RebalanceOnMembershipChange`

When membership changes, deactivate local actors whose keys now belong elsewhere. This is what makes scaling elastic: state is flushed on deactivation and the actor re-activates on its new owner from the store.

### property `ResolveSeedHostnames`

Whether a seed given as a hostname is expanded to every address it resolves to.

A Kubernetes headless service resolves to one address per pod. Connecting to the name gets whichever record the resolver happened to return first, so a single seed entry reaches one pod - and if that pod is the one still starting, the join fails and the node waits for its next beat to try the same coin flip again.

With this on, one seed entry means every replica behind the name, and a cluster's whole configuration is the service name. Addresses are re-resolved on each attempt, so pods that appear later are picked up without a restart.

### property `SeedSource`

Where the seeds come from, when a fixed list is not the right answer.

Null means `Seeds`, which is the usual case. Set it to something live - the Kubernetes source in `ActorNet.Kubernetes`, or your own - when the set of peers changes while the process runs, and re-reading a list of names would only ever give the same answer.

### property `Seeds`

Nodes to contact on startup, as `host:port`. Any one reachable seed is enough - the joiner receives the full member table and gossips from there.

### property `SplitBrainStabilityWindow`

How long the set of reachable members must hold still before a partition is acted on.

A partition is rarely a clean cut: nodes drop out over several seconds, and deciding on the first observation would mean deciding against a membership that is still moving. Waiting costs the availability of the losing side for this long, and buys deciding once.

### property `SplitBrainStrategy`

What this node does when it can only see part of the cluster.

Off by default. Turning it on means a node may shut itself down without being asked, which is the correct answer when divergent state would be worse than lost capacity - and the wrong one when it would not.

### property `StaticQuorumSize`

Members a side must be able to see to survive, under `StaticQuorum`.

Set it above half the intended cluster size, or two sides can both reach it and the strategy protects nothing.

### method `TryParseSeed(String, String ref, Int32 ref)`

Splits a `host:port` seed.

### property `UnreachableAfter`

Silence after which a peer is marked `Unreachable`. Unreachable members still own their slice of the ring - the assumption is a blip, not a departure.

Used when `FailureDetection` is `Deadline`.

### method `Validate`

Throws when the settings cannot produce a working cluster.

### property `VirtualNodesPerMember`

Ring positions per member. Higher spreads the keyspace more evenly at the cost of a bigger ring to search; 128 keeps a 3-node cluster inside a few percent of even.

## ClusterView

Enough of the cluster for a client to work out which node owns a key.

- `Members` — The members a client may dial. Unreachable ones are included; the ring has them too.
- `VirtualNodes` — Virtual nodes per member. Without it a client builds a differently-shaped ring from the same member list and disagrees with every node about who owns what.

Deliberately not the whole member table: a client has no use for incarnation numbers, last-seen times or statuses, and sending them would make the client's idea of membership something that could drift from the cluster's own and be believed anyway.

A client that routes by this is an optimisation, never a requirement. Every node accepts a message for any actor and forwards it, so a client with a stale view is slower and not wrong - which is what makes it safe to hand a client a view that is a few seconds behind.

### method `ClusterView(IReadOnlyList<ClusterViewMember>, Int32)`

Enough of the cluster for a client to work out which node owns a key.

- `Members` — The members a client may dial. Unreachable ones are included; the ring has them too.
- `VirtualNodes` — Virtual nodes per member. Without it a client builds a differently-shaped ring from the same member list and disagrees with every node about who owns what.

Deliberately not the whole member table: a client has no use for incarnation numbers, last-seen times or statuses, and sending them would make the client's idea of membership something that could drift from the cluster's own and be believed anyway.

A client that routes by this is an optimisation, never a requirement. Every node accepts a message for any actor and forwards it, so a client with a stale view is slower and not wrong - which is what makes it safe to hand a client a view that is a few seconds behind.

### property `Members`

The members a client may dial. Unreachable ones are included; the ring has them too.

### property `VirtualNodes`

Virtual nodes per member. Without it a client builds a differently-shaped ring from the same member list and disagrees with every node about who owns what.

## ClusterViewMember

One member, as a client needs it: an identity and somewhere to dial.

- `NodeId` — The id the ring is built from. Not the address - the two are separate on purpose.
- `Host` — The address this member advertises.
- `Port` — The port this member advertises.

### method `ClusterViewMember(String, String, Int32)`

One member, as a client needs it: an identity and somewhere to dial.

- `NodeId` — The id the ring is built from. Not the address - the two are separate on purpose.
- `Host` — The address this member advertises.
- `Port` — The port this member advertises.

### property `Host`

The address this member advertises.

### property `NodeId`

The id the ring is built from. Not the address - the two are separate on purpose.

### property `Port`

The port this member advertises.

## FailureDetection

How a peer's silence is turned into a verdict.

### field `Deadline`

A flat deadline on last contact. Predictable, and necessarily set for the worst link.

### field `PhiAccrual`

Adaptive: suspicion grows against how long this peer's beats have actually been taking.

## HashRing

Consistent hashing over cluster members, with virtual nodes. Decides which node owns an actor key without any node needing to ask another.

The point of consistent hashing here is what happens when membership changes: adding or removing one node out of N moves roughly 1/N of the keyspace instead of reshuffling all of it, so an elastic scale-out migrates a slice of actors rather than every actor.

The hash is FNV-1a over UTF-8, *not*`GetHashCode`. String hashing in .NET is randomized per process, so two nodes would compute different rings from the same member list and disagree about who owns what - a bug that only shows up in a real cluster.

Instances are immutable; membership changes build a new ring and publish it with a single reference assignment, so readers on the dispatch path never take a lock.

### method `HashRing(IEnumerable<String>, Int32)`

Builds a ring placing each node at `virtualNodes` positions.

### method `Hash(String)`

FNV-1a over the UTF-8 bytes of `value`, then an avalanche mix.

Chosen for being tiny, allocation-free for short keys, and - the part that matters - identical in every process and every run.

The finalizer is not optional. Raw FNV-1a avalanches poorly on short keys that share a prefix, which is exactly what ring positions are (`node-1#0`, `node-1#1`, …): the replicas of one member land in clumps instead of spreading, and one node ends up owning far more than its share. Measured on three members with 128 replicas, raw FNV-1a gave one node 48% of the keyspace; with this mix the worst share is within a few percent of even.

### property `IsEmpty`

True when the ring has no members and cannot answer a placement query.

### method `Mix(UInt64)`

The MurmurHash3 64-bit finalizer, which spreads every input bit across the output.

### property `Nodes`

The members on this ring, in the order they were supplied.

### method `OwnerOf(String)`

The node that owns `key`: the first ring position at or after its hash.

### method `OwnershipShare`

The share of the keyspace each member owns, as a fraction summing to one.

### method `PreferenceList(String, Int32)`

The owner plus the next `count` distinct nodes clockwise. Useful for replica placement and for choosing a fallback owner while a node is unreachable.

### method `Segments`

The ring as ownership arcs, in order: each entry is the half-open span of hash space `[Start, End)` that `Owner` is responsible for.

Adjacent replicas of the same member are merged, so the result is the ownership map rather than the raw replica list. For three members at 128 replicas that is typically a few hundred arcs, not 384.

This is what the console draws, and it is also the honest way to answer "is the keyspace evenly split?" - summing each owner's arc widths measures the ring itself, where sampling keys only estimates it.

## IClusterView

Read-only view of cluster membership and key placement.

### method `IsLocal(ActorId)`

True when this node owns the key and should activate it locally.

### property `IsSingleNode`

True when clustering is off, or on but this is the only member.

### property `Members`

Every member this node knows about, including itself.

### event `MembershipChanged`

Raised after the member table changes, on a background thread.

### method `OwnerOf(ActorId)`

Which node owns an actor key, per the consistent-hash ring.

### property `Ring`

The placement ring as this node currently sees it. Exposed so tooling can show ownership rather than infer it by sampling keys.

### property `SelfNodeId`

The observing node.

## ISeedSource

Where a node looks for peers to introduce itself to.

The configured list is the usual answer, and `StaticSeedSource` is it. The interface exists for the answers that change while the process runs - a set of pods, a service registry - where re-reading a list of names is not the same as asking again.

It is asked on every join attempt, including the retries a node makes while it is alone, so a source that has just learned about a new peer is used at the next beat without anything being restarted. It is not asked once at startup and cached, which is the whole point.

### method `SeedsAsync(CancellationToken)`

The seeds to try now, as `host:port`.

Failure is reported as an empty list rather than an exception where the source can manage it: a directory that is briefly unavailable should cost this attempt, not the node.

## KeyDigest

What one node is holding that another would inherit if it disappeared.

- `Node` — The node that is holding them.
- `Keys` — Actor addresses, in `Type/Key` form.

The warm handoff only covers a node that leaves politely, because only that node knows what it was holding. A node lost without warning takes that knowledge with it, and the survivors inherit a set of keys with no idea which were live - so every one of them pays a read at the moment traffic arrives, which is exactly when the cluster is already one node short.

This is advice and not a directory. It is a snapshot of what one node held when it last spoke, so it can name an actor that has since deactivated - warming that one costs a read and nothing else - and it can miss one activated since. Nothing routes by it: the ring decides ownership, and a second opinion about ownership is the last thing a cluster needs.

### method `KeyDigest(String, IReadOnlyList<String>)`

What one node is holding that another would inherit if it disappeared.

- `Node` — The node that is holding them.
- `Keys` — Actor addresses, in `Type/Key` form.

The warm handoff only covers a node that leaves politely, because only that node knows what it was holding. A node lost without warning takes that knowledge with it, and the survivors inherit a set of keys with no idea which were live - so every one of them pays a read at the moment traffic arrives, which is exactly when the cluster is already one node short.

This is advice and not a directory. It is a snapshot of what one node held when it last spoke, so it can name an actor that has since deactivated - warming that one costs a read and nothing else - and it can miss one activated since. Nothing routes by it: the ring decides ownership, and a second opinion about ownership is the last thing a cluster needs.

### property `Keys`

Actor addresses, in `Type/Key` form.

### property `Node`

The node that is holding them.

## LeaveDecision

The coordinator's answer.

- `Granted` — Whether the asking node may leave now.
- `HeldBy` — The node holding the token, when the answer is no. For the log, not for logic.
- `RetryAfter` — How long to wait before asking again.

A refusal carries a delay rather than leaving the caller to invent one. The coordinator knows how long the current holder's lease has left; the caller does not, and would either poll harder than it needs to or wait longer than it has to.

### method `LeaveDecision(Boolean, String, TimeSpan)`

The coordinator's answer.

- `Granted` — Whether the asking node may leave now.
- `HeldBy` — The node holding the token, when the answer is no. For the log, not for logic.
- `RetryAfter` — How long to wait before asking again.

A refusal carries a delay rather than leaving the caller to invent one. The coordinator knows how long the current holder's lease has left; the caller does not, and would either poll harder than it needs to or wait longer than it has to.

### property `Granted`

Whether the asking node may leave now.

### property `HeldBy`

The node holding the token, when the answer is no. For the log, not for logic.

### property `RetryAfter`

How long to wait before asking again.

## LeaveRequest

Asks the coordinator to leave, or tells it this node has finished leaving.

- `Node` — The node asking.
- `Releasing` — True to hand the token back rather than ask for it.

### method `LeaveRequest(String, Boolean)`

Asks the coordinator to leave, or tells it this node has finished leaving.

- `Node` — The node asking.
- `Releasing` — True to hand the token back rather than ask for it.

### property `Node`

The node asking.

### property `Releasing`

True to hand the token back rather than ask for it.

## LeaveTokenHolder

Hands out one leave token at a time, so a rolling restart is a rolling one.

Held by whichever member has the lowest node id. Every node works that out from its own member table, so there is nothing to elect and nothing to fail over - when the coordinator itself leaves, the next-lowest id is the coordinator from the moment its departure lands, and the token starts out free there.

That last point is also the limit of what this promises: a token that lives in the coordinator's memory is lost when the coordinator changes, so a node that held it is no longer recorded as holding it. In a healthy cluster the holder is the only node leaving and finishes in seconds. In a partition, both sides have a coordinator and both will grant. This orders shutdowns; it is not a lock.

### method `LeaveTokenHolder(TimeProvider)`

Hands out one leave token at a time, so a rolling restart is a rolling one.

Held by whichever member has the lowest node id. Every node works that out from its own member table, so there is nothing to elect and nothing to fail over - when the coordinator itself leaves, the next-lowest id is the coordinator from the moment its departure lands, and the token starts out free there.

That last point is also the limit of what this promises: a token that lives in the coordinator's memory is lost when the coordinator changes, so a node that held it is no longer recorded as holding it. In a healthy cluster the holder is the only node leaving and finishes in seconds. In a partition, both sides have a coordinator and both will grant. This orders shutdowns; it is not a lock.

### method `Decide(LeaveRequest, TimeSpan)`

Answers one request, granting the token when it is free.

### property `Holder`

The node currently holding the token, if any is.

## MemberStatus

Where a peer stands in this node's view of the cluster.

### field `Down`

Off the ring. Its keys have been redistributed.

### field `Joining`

Seen, but has not completed a join handshake yet. Not on the ring.

### field `Leaving`

Shutting down gracefully; it has asked to be taken off the ring.

### field `Unreachable`

Missed heartbeats. Still on the ring - the bet is that it comes back.

### field `Up`

Healthy and carrying its share of the keyspace.

## PhiAccrualFailureDetector

Suspicion as a number rather than a yes-or-no deadline.

A fixed deadline has to be set for the worst link in the cluster, which makes it slow to notice a real failure on the good ones. This keeps a window of how long each peer's heartbeats have actually been taking and reports phi: roughly the number of nines of confidence that a peer this quiet has stopped. Phi 8 is "wrong about one time in 10^8 given how this peer normally behaves".

The value of adapting is that the same threshold means different things on different links. A peer over a wireless hop whose beats vary by half a second is given that slack; a peer on the same switch beating every 200ms is suspected far sooner, because for it a two-second silence is genuinely abnormal. The three-machine test that motivated this had healthy nodes crossing a fixed 10s deadline while a co-located pair would not have crossed it in a minute.

The distribution is approximated with the logistic function rather than a real normal CDF. The error is well under a tenth of a phi and it costs one `exp`, which matters because this runs for every member on every beat.

### method `PhiAccrualFailureDetector(TimeSpan, TimeSpan, TimeSpan, Int32)`

- `firstHeartbeatEstimate` — What to assume about a peer that has only been heard from once. Without a guess, the first beat after a join would have no distribution to sit in and every new member would look dead.
- `acceptableHeartbeatPause` — Added to the mean before suspicion starts, which is what buys a stop-the-world GC pause the benefit of the doubt on an otherwise metronomic link.
- `minimumStandardDeviation` — A floor on the spread. A peer whose beats are suspiciously regular would otherwise get a near-zero standard deviation, and phi would leap from nothing to enormous within a millisecond of its usual interval.
- `sampleSize` — How many intervals the window keeps.

### method `Bootstrap(DateTimeOffset)`

Seeds a new peer's window with a plausible spread around the expected interval.

### property `FirstHeartbeatEstimate`

What to assume about a peer heard from only once.

Settable because the expected gap depends on the gossip fanout and on how many members there are, neither of which is known when the detector is built.

### method `Forget(String)`

Drops a peer's history, so a node that comes back starts from a clean estimate.

Called when a member is declared down. Keeping the history would fold the whole outage into the window as one enormous interval, and the peer would then be trusted through silences it should be suspected for.

### method `Heartbeat(String, DateTimeOffset)`

Records that a peer was heard from.

### method `Phi(String, DateTimeOffset)`

How suspicious this peer's current silence is. Zero for a peer never heard from.

### method `Suspicion(Double, Double, Double)`

The logistic approximation to `-log10(1 - F(silence))`.

## RingSegment

One arc of the ring: the half-open hash span `[Start, End)` owned by a member.

Arcs wrap. The first arc of a ring starts past the last position and runs through zero, so its `Start` exceeds its `End`; `Width` accounts for that where subtracting the two directly does not.

### method `RingSegment(UInt64, UInt64, String)`

One arc of the ring: the half-open hash span `[Start, End)` owned by a member.

Arcs wrap. The first arc of a ring starts past the last position and runs through zero, so its `Start` exceeds its `End`; `Width` accounts for that where subtracting the two directly does not.

### property `IsFullCircle`

True when this arc covers the entire ring, which happens with a single member.

Every raw arc has non-zero width, so `Start == End` after merging can only mean the arc went all the way round - never that it is empty.

### property `Width`

How much of the hash space this arc covers, wrapping and full circles included.

## SplitBrainStrategy

What a node does when it can only see part of the cluster.

### field `KeepMajority`

The side that can still see more than half of the last agreed membership survives; the other side takes itself down.

An even split is settled by the lowest node id, so a two-node cluster does not lose both halves. Membership counts against the last state everyone agreed on, not against what each side can currently see - otherwise each side would count itself a majority of itself.

### field `None`

Nothing. Both sides of a partition keep serving, which means two activations of the same actor and two divergent versions of its state.

The default, because the alternative is a node shutting itself down on its own judgement, and whether availability or consistency is the thing to protect is not a framework's call.

### field `StaticQuorum`

A side survives only if it can see at least `StaticQuorumSize` members.

The right choice when the cluster size is fixed and known, and safer than a majority when nodes are added and removed often: a majority of a membership that has itself drifted is not the guarantee it looks like.

## StaticSeedSource

The configured list, unchanged.

### method `StaticSeedSource(IReadOnlyList<String>)`

The configured list, unchanged.

### method `SeedsAsync(CancellationToken)`

---

[Back to the index](README.md)
