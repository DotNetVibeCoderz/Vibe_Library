# ActorNet.Kubernetes

Generated - edit the `///` comments in the source, not this file.

## KubernetesSeedOptions

Where to look for the pods of this cluster, and how to talk to the API that knows.

Every value has an in-cluster default read from what the kubelet mounts into the pod, so a deployment usually sets only `LabelSelector` and `Port`.

### property `ApiServer`

The API server, as a base address. Defaults to the in-cluster endpoint.

### property `EffectiveApiServer`

The API server to use, from configuration or from the environment.

### property `EffectiveNamespace`

The namespace to use, from configuration or from the pod.

### property `EffectiveToken`

The bearer token to use, from configuration or from the pod.

### property `LabelSelector`

Which pods are this cluster, as a label selector.

Required, and deliberately not defaulted. A selector that matches nothing is a cluster that never forms; one that matches too much is a cluster that tries to join somebody else's. Neither should be something a caller gets by leaving a field alone.

### property `Namespace`

The namespace to look in. Defaults to the pod's own.

### property `Port`

The port peers listen on. Every pod is assumed to use the same one.

### property `ResyncInterval`

How often to list again regardless, in case a watch has been quietly wrong.

### field `ServiceAccount`

The files the kubelet mounts for a pod's service account.

### property `Token`

The bearer token. Defaults to the pod's service-account token.

Read on every request rather than once, because the kubelet rotates the projected token and a copy taken at startup stops working somewhere between an hour and a day later - which looks exactly like a cluster that has been fine for a day and then cannot find its peers.

### method `Validate`

Throws when a value that has no sensible default is missing.

### property `WatchRetryDelay`

How long to wait before opening a watch again after one ends.

A watch ends normally - the API server closes them on its own schedule - so this is the ordinary path rather than the error path. It is short for that reason, and backed off only when reopening fails.

## KubernetesSeedSource

Finds the cluster's peers by asking the Kubernetes API, and keeps asking.

A headless service resolved through DNS already works and needs none of this. What it cannot do is tell a node that a pod has appeared: DNS is asked, it answers with what it has, and a node that is alone has to keep asking to find out anything changed. A watch is told.

The list is what makes this correct and the watch is what makes it prompt. Both are needed: a watch is a stream of changes since a known point, so something has to establish that point, and the API server ends watches on its own schedule, so something has to notice and open another.

This node's own pod is not filtered out here. The join path already refuses to introduce a node to itself, and it does it by comparing the address it is about to dial, which is a better test than anything available from a pod's own view of itself.

### method `KubernetesSeedSource(KubernetesSeedOptions, HttpClient, ILogger)`

Creates a source. The watch starts on the first call, not here.

- `options` — Which pods, and how to reach the API.
- `client` — The client to use. One is created when this is null; supply one to control the handler, which is what the tests do and what a cluster with a private CA needs.
- `logger` — Where to report a watch that will not stay open.

### method `Apply(String)`

Applies one watch event.

### method `DisposeAsync`

### method `Endpoint(JsonElement)`

A pod's name and address, when it is one worth trying.

Running and with an address. A pod that is pending has no IP to dial, and one that is terminating will refuse the connection - both are ordinary states rather than errors, and both are worth leaving out of a list whose whole purpose is somewhere to send a join.

### method `ListAsync(CancellationToken)`

Replaces what is known with what the API server says, and returns where to watch from.

### method `SeedsAsync(CancellationToken)`

The endpoints known right now.

The first call lists before answering, because a node that starts and is told there are no peers stays alone until its next retry for no reason. After that the watch keeps it current and this returns immediately.

### method `WatchAsync(String, CancellationToken)`

Reads one watch to its end, applying events as they arrive.

### method `WatchLoopAsync(String, CancellationToken)`

Keeps a watch open, listing again between one and the next.

A watch is bounded by `ResyncInterval` and ends on the server's own schedule, so ending is the ordinary case rather than the error case. Each one is followed by a fresh list rather than a resume, which is what makes the resync interval mean what it says: an event a watch missed is corrected at the next list, where resuming from a resourceVersion would carry the mistake for as long as the process ran.

---

[Back to the index](README.md)
