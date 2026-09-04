// Drives the ActorNet order saga from Go.
//
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.
//
// Start a node first:
//
//	dotnet run --project src/ActorNet.Cli -- run --port 9000
//
// Then:
//
//	cd clients/go && go run ./examples/ordering
package main

import (
	"context"
	"fmt"
	"os"
	"time"

	"github.com/DotNetVibeCoderz/Vibe_Library/ActorNet/clients/go/actornet"
)

type orderSnapshot struct {
	OrderID       string  `json:"OrderId"`
	Status        string  `json:"Status"`
	Sku           string  `json:"Sku"`
	Quantity      int     `json:"Quantity"`
	Total         float64 `json:"Total"`
	FailureReason *string `json:"FailureReason"`
}

type stockLevel struct {
	Sku       string `json:"Sku"`
	Available int    `json:"Available"`
	Reserved  int    `json:"Reserved"`
}

func main() {
	if err := run(); err != nil {
		fmt.Fprintf(os.Stderr, "actornet: %v\n", err)
		os.Exit(1)
	}
}

func run() error {
	addr := os.Getenv("ACTORNET_ADDR")
	if addr == "" {
		addr = "127.0.0.1:9000"
	}

	client := actornet.New(addr, actornet.WithClientID("go-example"))
	defer client.Close()

	ctx, cancel := context.WithTimeout(context.Background(), 30*time.Second)
	defer cancel()

	if err := client.Connect(ctx); err != nil {
		return err
	}
	fmt.Printf("connected to %s as %s\n", addr, client.ClientID())

	// Aliases, not .NET type names. The node resolves them through its own allow-list, which is
	// what lets a Go process address the same actors as the C# ones.
	if err := client.Tell(ctx, "InventoryActor/gadget", "order.restock", map[string]any{
		"Sku": "gadget", "Quantity": 4,
	}); err != nil {
		return err
	}

	if err := client.Tell(ctx, "PaymentActor/cust-go", "order.set-limit", map[string]any{
		"Limit": 400,
	}); err != nil {
		return err
	}

	orders := []struct {
		id       string
		quantity int
		total    float64
		note     string
	}{
		{"go-order-1", 2, 250, "succeeds"},
		{"go-order-2", 2, 900, "fails at payment, so the reserved stock is released"},
		{"go-order-3", 999, 50, "fails at stock, so there is nothing to compensate"},
	}

	for _, order := range orders {
		err := client.Tell(ctx, "OrderSagaActor/"+order.id, "order.place", map[string]any{
			"CustomerId": "cust-go",
			"Sku":        "gadget",
			"Quantity":   order.quantity,
			"Total":      order.total,
		})
		if err != nil {
			return err
		}
		fmt.Printf("placed %s (%s)\n", order.id, order.note)
	}

	// The saga takes several hops across the inventory and payment actors, and a tell returns as
	// soon as the node accepts it.
	time.Sleep(time.Second)
	fmt.Println()

	for _, order := range orders {
		reply, err := client.Ask(ctx, "OrderSagaActor/"+order.id, "order.get", map[string]any{})
		if err != nil {
			return err
		}

		var snapshot orderSnapshot
		if err := reply.Into(&snapshot); err != nil {
			return err
		}

		reason := "-"
		if snapshot.FailureReason != nil {
			reason = *snapshot.FailureReason
		}

		fmt.Printf("%-12s %-10s qty %-4d total %8.2f  %s\n",
			snapshot.OrderID, snapshot.Status, snapshot.Quantity, snapshot.Total, reason)
	}

	reply, err := client.Ask(ctx, "InventoryActor/gadget", "order.get-stock", map[string]any{})
	if err != nil {
		return err
	}

	var stock stockLevel
	if err := reply.Into(&stock); err != nil {
		return err
	}

	fmt.Printf("\nstock for %s: %d available, %d reserved\n", stock.Sku, stock.Available, stock.Reserved)
	return nil
}
