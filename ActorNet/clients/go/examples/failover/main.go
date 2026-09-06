// Checks that a client steps over an address that is not answering.
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.
//
// Start a node first:
//
//	dotnet run --project src/ActorNet.Cli -- run --port 9000
//
// Then:
//
//	go run ./examples/failover
//
// The first address is deliberately dead. A client that reported the whole cluster unreachable
// because the first node it tried was down would be no better than a client with one address.
package main

import (
	"context"
	"fmt"
	"os"
	"time"

	"github.com/DotNetVibeCoderz/Vibe_Library/ActorNet/clients/go/actornet"
)

func main() {
	live := os.Getenv("ACTORNET_ADDR")
	if live == "" {
		live = "127.0.0.1:9000"
	}

	// Nothing listens on this one. A refused connection is the ordinary case - a node that is
	// being restarted, or one that has been removed from a list nobody updated.
	dead := "127.0.0.1:9099"

	client := actornet.NewCluster([]string{dead, live}, actornet.WithClientID("go-failover"))
	defer client.Close()

	ctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
	defer cancel()

	if err := client.Connect(ctx); err != nil {
		fmt.Fprintln(os.Stderr, "connect:", err)
		os.Exit(1)
	}

	if got := client.ConnectedTo(); got != live {
		fmt.Fprintf(os.Stderr, "connected to %q, expected %q\n", got, live)
		os.Exit(1)
	}

	fmt.Printf("stepped over %s and connected to %s\n", dead, client.ConnectedTo())

	account := "BankAccountActor/go-failover"
	if err := client.Tell(ctx, account, "bank.deposit", map[string]any{"Amount": 25, "Reference": "failover"}); err != nil {
		fmt.Fprintln(os.Stderr, "tell:", err)
		os.Exit(1)
	}

	reply, err := client.Ask(ctx, account, "bank.get-statement", map[string]any{"MaxEntries": 1})
	if err != nil {
		fmt.Fprintln(os.Stderr, "ask:", err)
		os.Exit(1)
	}

	var statement struct {
		Balance float64
	}
	if err := reply.Into(&statement); err != nil {
		fmt.Fprintln(os.Stderr, "decode:", err)
		os.Exit(1)
	}

	fmt.Printf("the surviving node answered: balance %.2f\n", statement.Balance)
}
