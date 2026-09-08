// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

package actornet

import (
	"context"
	"encoding/binary"
	"encoding/json"
	"io"
	"net"
	"sort"
	"strconv"
	"sync"
	"time"
)

// kindClusterViewRequest asks a node for the member table. Answered with an ordinary reply.
const kindClusterViewRequest = 12

// clusterViewMember is one member, as a client needs it: an identity and somewhere to dial.
type clusterViewMember struct {
	NodeID string `json:"NodeId"`
	Host   string `json:"Host"`
	Port   int    `json:"Port"`
}

// clusterView is enough of the cluster for a client to work out which node owns a key.
type clusterView struct {
	Members      []clusterViewMember `json:"Members"`
	VirtualNodes int                 `json:"VirtualNodes"`
}

// WithClusterAwareRouting makes the client work out which node owns a key and send straight to it.
//
// Off by default. With it off every frame goes to the node this client is connected to, which
// forwards it to the owner - correct, and one hop. With it on the client asks for the member table,
// builds the same ring the cluster uses, and opens a connection per node it actually addresses.
//
// It is an optimisation and never a requirement: a client with a stale view, or none at all, still
// reaches the right actor.
func WithClusterAwareRouting() Option {
	return func(c *Client) { c.clusterAware = true }
}

// WithRoutesRefresh sets how long a member table is used before it is asked for again.
func WithRoutesRefresh(d time.Duration) Option {
	return func(c *Client) { c.routesRefresh = d }
}

// nodeLink is one extra connection, to a node the ring sent this client to.
//
// Replies come back on whichever connection the request went out on, so every link completes the
// same pending table. That is what lets these be opened and dropped freely: a link is a route, not
// a session.
type nodeLink struct {
	endpoint string
	conn     net.Conn

	writeMu sync.Mutex

	// outstanding are the asks that went out on this link and have not been answered. The pending
	// table is shared with every other link, and a link that dies must fail its own asks and
	// nobody else's.
	outstandingMu sync.Mutex
	outstanding   map[string]struct{}
}

func (l *nodeLink) write(buffer []byte, correlationID string) error {
	// Recorded before the write, because a reply can arrive before Write returns.
	if correlationID != "" {
		l.outstandingMu.Lock()
		l.outstanding[correlationID] = struct{}{}
		l.outstandingMu.Unlock()
	}

	l.writeMu.Lock()
	defer l.writeMu.Unlock()

	_, err := l.conn.Write(buffer)
	return err
}

func (l *nodeLink) forget(correlationID string) {
	l.outstandingMu.Lock()
	delete(l.outstanding, correlationID)
	l.outstandingMu.Unlock()
}

// takeOutstanding returns and clears the asks still waiting on this link.
func (l *nodeLink) takeOutstanding() []string {
	l.outstandingMu.Lock()
	defer l.outstandingMu.Unlock()

	ids := make([]string, 0, len(l.outstanding))
	for id := range l.outstanding {
		ids = append(ids, id)
	}

	l.outstanding = make(map[string]struct{})
	return ids
}

// ConnectedNodes are the nodes this client currently holds a connection to.
func (c *Client) ConnectedNodes() []string {
	nodes := []string{}
	if to := c.ConnectedTo(); to != "" {
		nodes = append(nodes, to)
	}

	c.routesMu.Lock()
	for endpoint := range c.direct {
		nodes = append(nodes, endpoint)
	}
	c.routesMu.Unlock()

	sort.Strings(nodes)
	return nodes
}

// send writes a frame to whoever owns the target, falling back to the first connection.
//
// Falling back is not a workaround: a node forwards a client's frame to the owner, so a client that
// guesses wrong - or does not guess at all - is slower and not incorrect.
func (c *Client) send(ctx context.Context, target string, f frame) error {
	if !c.clusterAware {
		return c.write(f)
	}

	link := c.ownerLink(ctx, target)
	if link == nil {
		return c.write(f)
	}

	buffer, err := encodeFrame(f)
	if err != nil {
		return err
	}

	if err := link.write(buffer, f.CorrelationID); err != nil {
		// The owner went away between being named and being written to.
		c.dropLink(link)
		return c.write(f)
	}

	return nil
}

// ownerLink is the connection to whoever owns this key, or nil to use the first connection.
func (c *Client) ownerLink(ctx context.Context, target string) *nodeLink {
	ring, endpoints := c.routes(ctx)
	if ring.IsEmpty() {
		return nil
	}

	endpoint, known := endpoints[ring.OwnerOf(target)]
	if !known || endpoint == c.ConnectedTo() {
		return nil
	}

	c.routesMu.Lock()
	existing, open := c.direct[endpoint]
	c.routesMu.Unlock()
	if open {
		return existing
	}

	conn, err := new(net.Dialer).DialContext(ctx, "tcp", endpoint)
	if err != nil {
		// Named by the view and not accepting connections. Forwarding still works.
		return nil
	}

	if tcp, ok := conn.(*net.TCPConn); ok {
		_ = tcp.SetNoDelay(true)
	}

	link := &nodeLink{endpoint: endpoint, conn: conn, outstanding: make(map[string]struct{})}

	c.routesMu.Lock()
	c.direct[endpoint] = link
	c.routesMu.Unlock()

	go c.linkReadLoop(link)
	return link
}

// dropLink forgets a link that has died and throws the view away with it.
//
// A link usually dies because the node behind it went away, which is exactly when the member table
// being routed by has stopped being true.
func (c *Client) dropLink(link *nodeLink) {
	c.routesMu.Lock()
	if c.direct[link.endpoint] == link {
		delete(c.direct, link.endpoint)
	}
	c.routesTakenAt = time.Time{}
	c.routesMu.Unlock()

	_ = link.conn.Close()

	// Only this link's asks. Failing every pending one would take down asks travelling on
	// connections that are perfectly healthy.
	for _, correlationID := range link.takeOutstanding() {
		c.pendingMu.Lock()
		waiting, found := c.pending[correlationID]
		if found {
			delete(c.pending, correlationID)
		}
		c.pendingMu.Unlock()

		if found {
			close(waiting)
		}
	}
}

func (c *Client) linkReadLoop(link *nodeLink) {
	defer c.dropLink(link)

	header := make([]byte, headerBytes)

	for {
		if _, err := io.ReadFull(link.conn, header); err != nil {
			return
		}

		length := int(binary.BigEndian.Uint32(header))
		if length <= 0 || length > maxFrameBytes {
			return
		}

		body := make([]byte, length)
		if _, err := io.ReadFull(link.conn, body); err != nil {
			return
		}

		var f frame
		if err := json.Unmarshal(body, &f); err != nil {
			continue
		}

		link.forget(f.CorrelationID)
		c.deliver(f)
	}
}

// routes returns the ring and the addresses behind it, asking again once the view is old enough.
func (c *Client) routes(ctx context.Context) (*HashRing, map[string]string) {
	c.routesMu.Lock()
	ring, endpoints := c.ring, c.nodeEndpoints
	fresh := ring != nil && time.Since(c.routesTakenAt) < c.routesRefresh
	asking := c.routing
	if !fresh && !asking {
		c.routing = true
	}
	c.routesMu.Unlock()

	// Somebody else is already asking. Routing by a view a moment out of date is the premise, so
	// waiting for theirs would cost more than it saves.
	if fresh || asking {
		return ring, endpoints
	}

	view := c.askClusterView(ctx)

	c.routesMu.Lock()
	defer c.routesMu.Unlock()
	c.routing = false

	if view == nil {
		return c.ring, c.nodeEndpoints
	}

	ids := make([]string, 0, len(view.Members))
	addresses := make(map[string]string, len(view.Members))
	for _, member := range view.Members {
		ids = append(ids, member.NodeID)
		addresses[member.NodeID] = member.Host + ":" + strconv.Itoa(member.Port)
	}

	c.ring = NewHashRing(ids, view.VirtualNodes)
	c.nodeEndpoints = addresses
	c.routesTakenAt = time.Now()

	return c.ring, c.nodeEndpoints
}

// askClusterView asks the node this client is connected to for the member table.
//
// A node that will not answer leaves the client forwarding, which is what it did before routing
// existed. Nothing here is worth failing a send over, so every failure is a nil view.
func (c *Client) askClusterView(ctx context.Context) *clusterView {
	correlationID := randomHex(16)
	replies := make(chan frame, 1)

	c.pendingMu.Lock()
	c.pending[correlationID] = replies
	c.pendingMu.Unlock()

	defer func() {
		c.pendingMu.Lock()
		delete(c.pending, correlationID)
		c.pendingMu.Unlock()
	}()

	err := c.write(frame{
		Kind:          kindClusterViewRequest,
		CorrelationID: correlationID,
		ReplyToNode:   c.clientID,
		FromNode:      c.clientID,
	})
	if err != nil {
		return nil
	}

	timer := time.NewTimer(5 * time.Second)
	defer timer.Stop()

	select {
	case reply, ok := <-replies:
		if !ok || reply.Kind == kindAskFailure {
			return nil
		}

		var view clusterView
		if err := json.Unmarshal(reply.Payload, &view); err != nil {
			return nil
		}

		return &view
	case <-timer.C:
		return nil
	case <-ctx.Done():
		return nil
	case <-c.closed:
		return nil
	}
}

// closeLinks shuts every extra connection down. Called from Close.
func (c *Client) closeLinks() {
	c.routesMu.Lock()
	links := make([]*nodeLink, 0, len(c.direct))
	for endpoint, link := range c.direct {
		links = append(links, link)
		delete(c.direct, endpoint)
	}
	c.routesMu.Unlock()

	for _, link := range links {
		_ = link.conn.Close()
	}
}

func encodeFrame(f frame) ([]byte, error) {
	body, err := json.Marshal(f)
	if err != nil {
		return nil, err
	}

	if len(body) > maxFrameBytes {
		return nil, ErrClosed
	}

	buffer := make([]byte, headerBytes+len(body))
	binary.BigEndian.PutUint32(buffer[:headerBytes], uint32(len(body)))
	copy(buffer[headerBytes:], body)
	return buffer, nil
}
