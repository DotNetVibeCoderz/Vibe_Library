using System;
using System.Diagnostics;
using System.Threading.Tasks;
using ActorNet.Core;
using ActorNet.Core.Actors;
using ActorNet.Core.Client; // Include Client Namespace
using Spectre.Console;

namespace ActorNet
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var rule = new Rule("[green]ActorNet: Distributed Actor Framework[/]");
            rule.Justification = Justify.Center;
            AnsiConsole.Write(rule);
            
            AnsiConsole.MarkupLine("[bold yellow]Initializing Actor System...[/]");

            // Initialize System on port 9000
            var system = new ActorSystem("Node1", 9000);
            
            // Register Actor Types so the system knows how to spawn them
            system.RegisterActorType<BankAccountActor>();
            
            system.Start();

            bool running = true;
            while (running)
            {
                var choice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("Select an [green]Action[/]:")
                        .PageSize(10)
                        .AddChoices(new[] {
                            "Create Account (Local)", 
                            "Deposit Funds (Local)", 
                            "Withdraw Funds (Local)", 
                            "Check Balance (Local)", 
                            "Simulate Remote Client (Network)",
                            "Run Benchmark (Throughput)",
                            "Exit"
                        }));

                switch (choice)
                {
                    case "Create Account (Local)":
                        var id = AnsiConsole.Ask<string>("Enter Account ID (e.g. user1):");
                        var refActor = system.ActorOf<BankAccountActor>(id);
                        AnsiConsole.MarkupLine($"[green]Account {id} is active via Virtual Actor activation.[/]");
                        break;

                    case "Deposit Funds (Local)":
                        var depId = AnsiConsole.Ask<string>("Enter Account ID:");
                        var amount = AnsiConsole.Ask<decimal>("Amount:");
                        await system.SendMessageAsync($"BankAccountActor/{depId}", new Deposit(amount));
                        AnsiConsole.MarkupLine($"[blue]Deposit request sent locally.[/]");
                        break;

                    case "Withdraw Funds (Local)":
                        var withId = AnsiConsole.Ask<string>("Enter Account ID:");
                        var wAmount = AnsiConsole.Ask<decimal>("Amount:");
                        await system.SendMessageAsync($"BankAccountActor/{withId}", new Withdraw(wAmount));
                        AnsiConsole.MarkupLine($"[blue]Withdrawal request sent locally.[/]");
                        break;
                    
                    case "Check Balance (Local)":
                        var balId = AnsiConsole.Ask<string>("Enter Account ID:");
                        await system.SendMessageAsync($"BankAccountActor/{balId}", new GetBalance());
                        AnsiConsole.MarkupLine("[grey](Check console output above for balance log)[/]");
                        break;

                    case "Simulate Remote Client (Network)":
                        // Simulates a separate process sending a message over TCP to this node
                        var remoteId = AnsiConsole.Ask<string>("Target Account ID:");
                        var remoteAmt = AnsiConsole.Ask<decimal>("Deposit Amount:");
                        
                        AnsiConsole.MarkupLine("[yellow]Connecting to localhost:9000 via TCP...[/]");
                        var client = new ActorNetClient("127.0.0.1", 9000);
                        await client.SendMessageAsync($"BankAccountActor/{remoteId}", new Deposit(remoteAmt), "RemoteClient");
                        AnsiConsole.MarkupLine("[green]Message sent over network![/]");
                        break;

                    case "Run Benchmark (Throughput)":
                        var count = AnsiConsole.Ask<int>("How many messages?", 100000);
                        await RunBenchmark(system, count);
                        break;

                    case "Exit":
                        running = false;
                        break;
                }
            }

            system.Stop();
        }

        static async Task RunBenchmark(ActorSystem system, int count)
        {
            AnsiConsole.MarkupLine($"[bold]Running Benchmark: {count} messages...[/]");
            
            var stopwatch = Stopwatch.StartNew();
            
            var tasks = new Task[count];
            var target = "BankAccountActor/bench1";
            
            // Pre-warm actor activation
            await system.SendMessageAsync(target, new Deposit(1));

            // Send messages in parallel (simulating high concurrency)
            for (int i = 0; i < count; i++)
            {
                 tasks[i] = system.SendMessageAsync(target, new Deposit(1));
            }

            await Task.WhenAll(tasks);
            
            stopwatch.Stop();
            var rate = count / stopwatch.Elapsed.TotalSeconds;
            
            AnsiConsole.MarkupLine($"[green]Dispatch Completed in {stopwatch.Elapsed.TotalSeconds:F2}s[/]");
            AnsiConsole.MarkupLine($"[bold yellow]Throughput: {rate:N0} msg/sec[/]");
        }
    }
}