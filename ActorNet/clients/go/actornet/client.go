// Package actornet is a client for ActorNet nodes.
//
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.
//
// It speaks the node's own wire protocol - a 4-byte big-endian payload length followed by that
// many bytes of JSON - so there is no separate gateway to keep in sync with the runtime.
package actornet

import (
	"context"
	"encoding/binary"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net"
	"sync"
	"time"

	"crypto/rand"
)

// Frame kinds. Must match ActorNet.Serialization.WireKind.
const (
	kindMessage    = 1
	kindAskRequest = 2
	kindAskReply   = 3
	kindAskFailure = 4
)

const headerBytes = 4

// maxFrameBytes matches the node, so a bad length cannot make either side allocate wildly.
const maxFrameBytes = 32 * 1024 * 1024

// ErrTimeout is returned by Ask when no reply arrives in time.
var ErrTimeout = errors.New("actornet: no reply within the timeout")

// ErrClosed is returned when the connection went away before a reply arrived.
var ErrClosed = errors.New("actornet: the connection closed before a reply arrived")

// frame is one message on the wire. The short field names are the protocol's, not an abbreviation
// choice: they are what the node reads.
type frame struct {
	Kind          int             `json:"k"`
	Target        string          `json:"t,omitempty"`
	Sender        string          `json:"s,omitempty"`
	MessageAlias  string          `json:"a,omitempty"`
	Payload       json.RawMessage `json:"p,omitempty"`
	CorrelationID string          `json:"c,omitempty"`
	ReplyToNode   string          `json:"r,omitempty"`
	FromNode      string          `json:"f,omitempty"`
	Error         string          `json:"e,omitempty"`
}

// Reply is an actor's answer: the alias it replied under, and the raw body.
type Reply struct {
	Alias   string
	Payload json.RawMessage
}

// Into unmarshals the reply body into v.
func (r Reply) Into(v any) error {
	if len(r.Payload) == 0 {
		return errors.New("actornet: the reply had an empty payload")
	}
	return json.Unmarshal(r.Payload, v)
}

// Client is a connection to one ActorNet node.
//
// One persistent connection, not one per message: an Ask needs somewhere for the reply to arrive,
// and the node addresses this client by the ClientID stamped on every frame. Any node in a cluster
// is a valid entry point - it forwards to whichever node owns the target actor.
//
// A Client is safe for concurrent use.
type Client struct {
	addr        string
	clientID    string
	askTimeout  time.Duration
	conn        net.Conn
	writeMu     sync.Mutex
	connectMu   sync.Mutex
	pendingMu   sync.Mutex
	pending     map[string]chan frame
	closeOnce   sync.Once
	closed      chan struct{}
}

// Option configures a Client.
type Option func(*Client)

// WithClientID sets how this client identifies itself. It must be unique among the node's clients.
func WithClientID(id string) Option { return func(c *Client) { c.clientID = id } }

// WithAskTimeout sets the default timeout for Ask.
func WithAskTimeout(d time.Duration) Option { return func(c *Client) { c.askTimeout = d } }

// New creates a client. The connection is opened on first use.
func New(addr string, options ...Option) *Client {
	c := &Client{
		addr:       addr,
		clientID:   "go-" + randomHex(6),
		askTimeout: 10 * time.Second,
		pending:    make(map[string]chan frame),
		closed:     make(chan struct{}),
	}

	for _, option := range options {
		option(c)
	}

	return c
}

// ClientID returns how this client identifies itself to the node.
func (c *Client) ClientID() string { return c.clientID }

// Connect opens the connection. Tell and Ask call it automatically.
func (c *Client) Connect(ctx context.Context) error {
	c.connectMu.Lock()
	defer c.connectMu.Unlock()

	if c.conn != nil {
		return nil
	}

	var dialer net.Dialer
	conn, err := dialer.DialContext(ctx, "tcp", c.addr)
	if err != nil {
		return fmt.Errorf("actornet: dialling %s: %w", c.addr, err)
	}

	if tcp, ok := conn.(*net.TCPConn); ok {
		_ = tcp.SetNoDelay(true)
	}

	c.conn = conn
	go c.readLoop(conn)
	return nil
}

// Tell sends a message and returns once the frame is written - not once the actor has handled it.
//
// target is an actor address, "Type/Key". alias is a registered message alias, e.g. "bank.deposit".
func (c *Client) Tell(ctx context.Context, target, alias string, payload any) error {
	if err := c.Connect(ctx); err != nil {
		return err
	}

	body, err := json.Marshal(payload)
	if err != nil {
		return fmt.Errorf("actornet: encoding %s: %w", alias, err)
	}

	return c.write(frame{
		Kind:         kindMessage,
		Target:       target,
		MessageAlias: alias,
		Payload:      body,
		FromNode:     c.clientID,
	})
}

// Ask sends a message and waits for the actor's reply.
func (c *Client) Ask(ctx context.Context, target, alias string, payload any) (Reply, error) {
	if err := c.Connect(ctx); err != nil {
		return Reply{}, err
	}

	body, err := json.Marshal(payload)
	if err != nil {
		return Reply{}, fmt.Errorf("actornet: encoding %s: %w", alias, err)
	}

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

	err = c.write(frame{
		Kind:          kindAskRequest,
		Target:        target,
		MessageAlias:  alias,
		Payload:       body,
		CorrelationID: correlationID,
		// Both fields carry this client's id: ReplyToNode is what the actor's reply is routed by,
		// and FromNode is what the node keys this connection under.
		ReplyToNode: c.clientID,
		FromNode:    c.clientID,
	})
	if err != nil {
		return Reply{}, err
	}

	timer := time.NewTimer(c.askTimeout)
	defer timer.Stop()

	select {
	case reply, ok := <-replies:
		// failPending closes the channel when the connection drops. Without this check a closed
		// channel would yield a zero-value frame and be handed back as a successful empty reply.
		if !ok {
			return Reply{}, ErrClosed
		}
		if reply.Kind == kindAskFailure {
			message := reply.Error
			if message == "" {
				message = "the actor failed while handling the request"
			}
			return Reply{}, fmt.Errorf("actornet: %s", message)
		}
		return Reply{Alias: reply.MessageAlias, Payload: reply.Payload}, nil
	case <-timer.C:
		return Reply{}, fmt.Errorf("%w: %s after %s", ErrTimeout, target, c.askTimeout)
	case <-ctx.Done():
		return Reply{}, ctx.Err()
	case <-c.closed:
		return Reply{}, ErrClosed
	}
}

// Close shuts the connection down and releases anything still waiting.
func (c *Client) Close() error {
	var err error
	c.closeOnce.Do(func() {
		close(c.closed)
		c.connectMu.Lock()
		defer c.connectMu.Unlock()
		if c.conn != nil {
			err = c.conn.Close()
			c.conn = nil
		}
	})
	return err
}

func (c *Client) write(f frame) error {
	body, err := json.Marshal(f)
	if err != nil {
		return fmt.Errorf("actornet: encoding frame: %w", err)
	}

	if len(body) > maxFrameBytes {
		return fmt.Errorf("actornet: frame of %d bytes exceeds the %d byte limit", len(body), maxFrameBytes)
	}

	buffer := make([]byte, headerBytes+len(body))
	binary.BigEndian.PutUint32(buffer[:headerBytes], uint32(len(body)))
	copy(buffer[headerBytes:], body)

	// Serialized writes: several goroutines may be telling and asking at once, and interleaved
	// bytes would produce frames neither of them sent.
	c.writeMu.Lock()
	defer c.writeMu.Unlock()

	if c.conn == nil {
		return ErrClosed
	}

	_, err = c.conn.Write(buffer)
	return err
}

func (c *Client) readLoop(conn net.Conn) {
	header := make([]byte, headerBytes)

	for {
		// ReadFull is what makes this correct: TCP is a byte stream, so one reply can arrive in
		// several reads and two replies can arrive in one.
		if _, err := io.ReadFull(conn, header); err != nil {
			c.failPending()
			return
		}

		length := int(binary.BigEndian.Uint32(header))
		if length <= 0 || length > maxFrameBytes {
			c.failPending()
			return
		}

		body := make([]byte, length)
		if _, err := io.ReadFull(conn, body); err != nil {
			c.failPending()
			return
		}

		var f frame
		if err := json.Unmarshal(body, &f); err != nil {
			continue
		}

		c.pendingMu.Lock()
		waiting, ok := c.pending[f.CorrelationID]
		c.pendingMu.Unlock()

		if ok {
			select {
			case waiting <- f:
			default:
			}
		}
	}
}

func (c *Client) failPending() {
	c.pendingMu.Lock()
	defer c.pendingMu.Unlock()
	for id, waiting := range c.pending {
		close(waiting)
		delete(c.pending, id)
	}
}

func randomHex(bytes int) string {
	buffer := make([]byte, bytes)
	if _, err := rand.Read(buffer); err != nil {
		// crypto/rand failing is not recoverable and not worth an error path on a client id.
		return fmt.Sprintf("%d", time.Now().UnixNano())
	}
	return fmt.Sprintf("%x", buffer)
}
