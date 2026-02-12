using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using KafkaNet.Client;
using KafkaNet.Core;
using KafkaNet.Network;
using KafkaNet.Streams;
using Microsoft.ML;
using Microsoft.ML.Data;
using Spectre.Console;

namespace KafkaNet
{
    class Program
    {
        // Client-side objects
        private static ProducerClient _producer = null!;
        private static ConsumerClient _consumer = null!;
        private static StreamProcessor _streamProcessor = null!;

        // Server-side objects
        private static Broker _broker = null!;
        private static KafkaServer _server = null!;

        public class TransactionData
        {
            public float Amount { get; set; }
            public bool Label { get; set; } // True = Fraud, False = Legit
        }

        public class TransactionPrediction
        {
            [ColumnName("PredictedLabel")]
            public bool Prediction { get; set; }
            public float Score { get; set; }
        }

        static async Task Main(string[] args)
        {
            Console.Title = "KafkaNet";

            AnsiConsole.Write(
                new FigletText("KafkaNet")
                    .LeftJustified()
                    .Color(Color.Green));
            AnsiConsole.MarkupLine("[bold]Apache Kafka Clone & Simulator[/]");

            var mode = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Select Mode:")
                    .AddChoices(new[] { "Run as Server", "Run as Client" }));

            if (mode == "Run as Server")
            {
                await RunServer();
            }
            else
            {
                await RunClient();
            }
        }

        static async Task RunServer()
        {
            AnsiConsole.MarkupLine("[bold yellow]Starting KafkaNet Server...[/]");
            _broker = new Broker(1, "./kafka-data");

            // Pre-create topics for demo
            // NOTE: For 'transactions', we use 1 partition to simplify the demo consumer logic (which reads partition 0).
            // In a real scenario, the consumer group would handle partition assignment.
            _broker.CreateTopic("transactions", 1);
            _broker.CreateTopic("logs", 1);
            _broker.CreateTopic("benchmark", 1);
            _broker.CreateTopic("transactions-ml", 1);

            _server = new KafkaServer(_broker, 9092);
            await _server.StartAsync();
        }

        static async Task RunClient()
        {
            var host = AnsiConsole.Ask<string>("Enter Server Host:", "127.0.0.1");
            var port = AnsiConsole.Ask<int>("Enter Server Port:", 9092);

            try
            {
                _producer = new ProducerClient(host, port);
                _consumer = new ConsumerClient(host, port, "default-group");
                _streamProcessor = new StreamProcessor(_consumer); // Note: StreamProcessor takes ConsumerClient

                AnsiConsole.MarkupLine($"[green]Connected to {host}:{port}[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Failed to connect: {ex.Message}[/]");
                return;
            }

            while (true)
            {
                Console.Clear();
                AnsiConsole.Write(
                    new FigletText("KafkaNet Client")
                        .LeftJustified()
                        .Color(Color.Blue));

                var choice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("Select an option:")
                        .PageSize(10)
                        .AddChoices(new[] {
                            "Produce Messages",
                            "Consume Messages",
                            "Stream Processing (Reactive)",
                            "ML Anomaly Detection",
                            "Benchmark Throughput",
                            "Exit"
                        }));

                if (choice == "Exit") break;

                try
                {
                    switch (choice)
                    {
                        case "Produce Messages":
                            await ProduceMessages();
                            break;
                        case "Consume Messages":
                            await ConsumeMessages();
                            break;
                        case "Stream Processing (Reactive)":
                            await RunStreamProcessing(host, port);
                            break;
                        case "ML Anomaly Detection":
                            await RunMLAnomalyDetection(host, port);
                            break;
                        case "Benchmark Throughput":
                            await RunBenchmark();
                            break;
                    }
                }
                catch (Exception ex)
                {
                    AnsiConsole.WriteException(ex);
                    AnsiConsole.MarkupLine("Press any key to continue...");
                    Console.ReadKey(true);
                }
            }
        }

        static async Task ProduceMessages()
        {
            var topic = AnsiConsole.Ask<string>("Enter Topic Name (e.g. transactions):");
            var count = AnsiConsole.Ask<int>("How many messages to send?");
            var keyPrefix = AnsiConsole.Ask<string>("Enter Key Prefix (e.g. user):");

            await AnsiConsole.Status()
                .StartAsync("Sending messages...", async ctx =>
                {
                    for (int i = 0; i < count; i++)
                    {
                        var key = $"{keyPrefix}-{i}";
                        var value = $"Message {i} at {DateTime.Now}";
                        await _producer.SendAsync(topic, key, value);
                        if (i % 10 == 0) ctx.Status($"Sent {i}/{count}");
                    }
                });

            AnsiConsole.MarkupLine("[green]Messages sent successfully![/]");
            AnsiConsole.MarkupLine("Press any key to return...");
            Console.ReadKey(true);
        }

        static async Task ConsumeMessages()
        {
            var topic = AnsiConsole.Ask<string>("Enter Topic Name to consume:");
            _consumer.Subscribe(topic);

            AnsiConsole.MarkupLine("[yellow]Listening for messages... Press any key to stop.[/]");

            using (var cts = new CancellationTokenSource())
            {
                var pollingTask = Task.Run(async () =>
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        await _consumer.PollAsync(TimeSpan.FromMilliseconds(500), (t, msg) =>
                        {
                            AnsiConsole.MarkupLine($"[blue]{t}[/] Key: {msg.Key}, Value: {msg.Value}, Offset: {msg.Offset}");
                        });

                        if (Console.KeyAvailable)
                        {
                            cts.Cancel();
                            Console.ReadKey(true); // Consume key
                        }
                    }
                }, cts.Token);

                try
                {
                    await pollingTask;
                }
                catch (OperationCanceledException) { }
            }
        }

        static async Task RunStreamProcessing(string host, int port)
        {
            AnsiConsole.MarkupLine("[bold]Starting Stream Processing on topic 'transactions'...[/]");
            AnsiConsole.MarkupLine("Filtering transactions > $500 (simulated logic)");

            // Create a dedicated consumer for streaming to avoid conflict with main consumer
            using var streamConsumer = new ConsumerClient(host, port, "stream-group");
            var processor = new StreamProcessor(streamConsumer);

            // Subscribe logic FIRST (to set topic and consumers)
            var subscription = processor.FromTopic("transactions")
                .Where(msg => msg.Value.Contains("Amount"))
                .Select(msg =>
                {
                    try
                    {
                        var parts = msg.Value.Split(new[] { "Amount: " }, StringSplitOptions.None);
                        if (parts.Length > 1 && int.TryParse(parts[1].Trim(), out int amount))
                        {
                            return amount;
                        }
                    }
                    catch { }
                    return 0;
                })
                .Where(amount => amount > 500)
                .Subscribe(amount =>
                {
                    AnsiConsole.MarkupLine($"[purple]High Value Transaction Detected: ${amount}[/]");
                });

            // Start processing AFTER subscription is set up
            processor.Start();

            var cts = new CancellationTokenSource();
            // Start producer but don't await it here, let it run in background
            var producerTask = Task.Run(async () =>
            {
                var rnd = new Random();
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        int amount = rnd.Next(100, 1000);
                        // Using "sys" key, which maps to partition 0 if topic has 1 partition (or if lucky with >1)
                        await _producer.SendAsync("transactions", "sys", $"TransactionID: {Guid.NewGuid()} Amount: {amount}");
                        await Task.Delay(500);
                    }
                    catch (Exception ex)
                    {
                        // Log producer errors if any
                        AnsiConsole.MarkupLine($"[red]Producer Error: {ex.Message}[/]");
                    }
                }
            }, cts.Token);

            AnsiConsole.MarkupLine("Press any key to stop streaming...");
            Console.ReadKey(true);
            cts.Cancel();

            subscription.Dispose();
            processor.Stop();
            try { await producerTask; } catch { }
        }

        static async Task RunMLAnomalyDetection(string host, int port)
        {
            AnsiConsole.MarkupLine("[bold]Running ML Anomaly Detection Simulation[/]");

            var mlContext = new MLContext();
            var data = new List<TransactionData>
            {
                new TransactionData { Amount = 10f, Label = false },
                new TransactionData { Amount = 20f, Label = false },
                new TransactionData { Amount = 1000f, Label = true },
                new TransactionData { Amount = 15f, Label = false },
                new TransactionData { Amount = 50f, Label = false },
                new TransactionData { Amount = 2000f, Label = true }
            };

            var trainingData = mlContext.Data.LoadFromEnumerable(data);
            var pipeline = mlContext.Transforms.Concatenate("Features", "Amount")
                .Append(mlContext.BinaryClassification.Trainers.SdcaLogisticRegression(labelColumnName: "Label", featureColumnName: "Features"));

            AnsiConsole.MarkupLine("Training model...");
            var model = pipeline.Fit(trainingData);
            var predictionEngine = mlContext.Model.CreatePredictionEngine<TransactionData, TransactionPrediction>(model);

            AnsiConsole.MarkupLine("[green]Model Trained![/]");
            AnsiConsole.MarkupLine("Streaming transactions and predicting fraud...");

            using var mlConsumer = new ConsumerClient(host, port, "ml-group");
            var mlProcessor = new StreamProcessor(mlConsumer);

            // Subscribe logic FIRST
            var subscription = mlProcessor.FromTopic("transactions-ml")
                .Subscribe(msg =>
                {
                    // Clean up the value string in case it has extra whitespace
                    var valStr = msg.Value.Trim();
                    if (float.TryParse(valStr, out float amount))
                    {
                        var prediction = predictionEngine.Predict(new TransactionData { Amount = amount });
                        var color = prediction.Prediction ? "red" : "green";
                        var label = prediction.Prediction ? "FRAUD" : "LEGIT";
                        AnsiConsole.MarkupLine($"Amount: {amount} => [{color}]{label}[/] (Score: {prediction.Score:F2})");
                    }
                });

            // Start processor AFTER subscription
            mlProcessor.Start();

            var cts = new CancellationTokenSource();
            var producerTask = Task.Run(async () =>
            {
                var rnd = new Random();
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        float amount = rnd.Next(0, 100) < 80 ? rnd.Next(10, 100) : rnd.Next(800, 2000);
                        await _producer.SendAsync("transactions-ml", "ml-sys", amount.ToString());
                        await Task.Delay(500);
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[red]Producer Error: {ex.Message}[/]");
                    }
                }
            }, cts.Token);

            AnsiConsole.MarkupLine("Press any key to stop ML stream...");
            Console.ReadKey(true);
            cts.Cancel();

            subscription.Dispose();
            mlProcessor.Stop();
            try { await producerTask; } catch { }
        }

        static async Task RunBenchmark()
        {
            var count = 5_000;
            AnsiConsole.MarkupLine($"[bold]Benchmarking {count} messages...[/]");

            var topic = "benchmark";
            var sw = Stopwatch.StartNew();

            await AnsiConsole.Status().StartAsync("Benchmarking...", async ctx =>
            {
                // Note: We run sequentially because the current ProducerClient (and underlying KafkaClient)
                // uses a single TCP connection/stream which is not thread-safe for concurrent writes.
                // Using Parallel.For or Task.WhenAll without a connection pool or lock would cause
                // race conditions and protocol corruption.
                for (int i = 0; i < count; i++)
                {
                    await _producer.SendAsync(topic, i.ToString(), "benchmark-payload");
                    
                    if (i % 500 == 0)
                    {
                        ctx.Status($"Sent {i}/{count} messages...");
                    }
                }
            });

            sw.Stop();
            var rate = count / sw.Elapsed.TotalSeconds;

            AnsiConsole.MarkupLine($"[green]Completed in {sw.Elapsed.TotalSeconds:F2}s[/]");
            AnsiConsole.MarkupLine($"[yellow]Throughput: {rate:F2} msg/sec[/]");
            AnsiConsole.MarkupLine("Press any key to continue...");
            Console.ReadKey(true);
        }
    }
}
