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
using KafkaNet.Streams;
using Microsoft.ML;
using Microsoft.ML.Data;
using Spectre.Console;

namespace KafkaNet
{
    class Program
    {
        private static Broker _broker = null!;
        private static ProducerClient _producer = null!;
        private static ConsumerClient _consumer = null!;
        private static StreamProcessor _streamProcessor = null!;

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
            _broker = new Broker(1, "./kafka-data");
            _producer = new ProducerClient(_broker);
            _consumer = new ConsumerClient(_broker, "default-group");
            _streamProcessor = new StreamProcessor(_consumer);

            // Create topics
            _broker.CreateTopic("transactions", 3);
            _broker.CreateTopic("logs", 1);
            _broker.CreateTopic("benchmark", 1);
            _broker.CreateTopic("transactions-ml", 1);

            while (true)
            {
                Console.Clear();
                AnsiConsole.Write(
                    new FigletText("KafkaNet")
                        .LeftJustified()
                        .Color(Color.Green));
                AnsiConsole.MarkupLine("[bold]Apache Kafka Clone & Simulator[/]");
                
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
                            "Cluster Status",
                            "Exit"
                        }));

                if (choice == "Exit") break;

                switch (choice)
                {
                    case "Produce Messages":
                        await ProduceMessages();
                        break;
                    case "Consume Messages":
                        await ConsumeMessages();
                        break;
                    case "Stream Processing (Reactive)":
                        await RunStreamProcessing();
                        break;
                    case "ML Anomaly Detection":
                        await RunMLAnomalyDetection();
                        break;
                    case "Benchmark Throughput":
                        await RunBenchmark();
                        break;
                    case "Cluster Status":
                        ShowClusterStatus();
                        break;
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
                        }
                    }
                }, cts.Token);

                try 
                { 
                    await pollingTask; 
                    Console.ReadKey(true); 
                } 
                catch (OperationCanceledException) {}
            }
        }

        static async Task RunStreamProcessing()
        {
            AnsiConsole.MarkupLine("[bold]Starting Stream Processing on topic 'transactions'...[/]");
            AnsiConsole.MarkupLine("Filtering transactions > $500 (simulated logic)");

            var streamConsumer = new ConsumerClient(_broker, "stream-group");
            var processor = new StreamProcessor(streamConsumer);
            processor.Start();

            var subscription = processor.FromTopic("transactions")
                .Where(msg => msg.Value.Contains("Amount"))
                .Select(msg => 
                {
                    var parts = msg.Value.Split(new[] { "Amount: " }, StringSplitOptions.None);
                    if (parts.Length > 1 && int.TryParse(parts[1].Trim(), out int amount))
                    {
                        return amount;
                    }
                    return 0;
                })
                .Where(amount => amount > 500)
                .Subscribe(amount => 
                {
                    AnsiConsole.MarkupLine($"[purple]High Value Transaction Detected: ${amount}[/]");
                });

            var cts = new CancellationTokenSource();
            // Start producer but don't await it here, let it run in background
            Task.Run(async () => 
            {
                var rnd = new Random();
                while(!cts.Token.IsCancellationRequested)
                {
                    int amount = rnd.Next(100, 1000);
                    await _producer.SendAsync("transactions", "sys", $"TransactionID: {Guid.NewGuid()} Amount: {amount}");
                    await Task.Delay(500);
                }
            }, cts.Token);

            AnsiConsole.MarkupLine("Press any key to stop streaming...");
            Console.ReadKey(true);
            cts.Cancel();
            
            subscription.Dispose();
            processor.Stop();
        }
        
        static async Task RunMLAnomalyDetection()
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

            var mlConsumer = new ConsumerClient(_broker, "ml-group");
            var mlProcessor = new StreamProcessor(mlConsumer);
            mlProcessor.Start();
            
            var subscription = mlProcessor.FromTopic("transactions-ml")
                .Subscribe(msg => 
                {
                    if (float.TryParse(msg.Value, out float amount))
                    {
                        var prediction = predictionEngine.Predict(new TransactionData { Amount = amount });
                        var color = prediction.Prediction ? "red" : "green";
                        var label = prediction.Prediction ? "FRAUD" : "LEGIT";
                        AnsiConsole.MarkupLine($"Amount: {amount} => [{color}]{label}[/] (Score: {prediction.Score:F2})");
                    }
                });

            var cts = new CancellationTokenSource();
            Task.Run(async () => 
            {
                var rnd = new Random();
                while(!cts.Token.IsCancellationRequested)
                {
                    float amount = rnd.Next(0, 100) < 80 ? rnd.Next(10, 100) : rnd.Next(800, 2000); 
                    await _producer.SendAsync("transactions-ml", "ml-sys", amount.ToString());
                    await Task.Delay(500);
                }
            }, cts.Token);

            AnsiConsole.MarkupLine("Press any key to stop ML stream...");
            Console.ReadKey(true);
            cts.Cancel();
            
            subscription.Dispose();
            mlProcessor.Stop();
        }

        static async Task RunBenchmark()
        {
            var count = 5000;
            AnsiConsole.MarkupLine($"[bold]Benchmarking {count} messages...[/]");
            
            var topic = "benchmark";
            var sw = Stopwatch.StartNew();
            
            await AnsiConsole.Status().StartAsync("Benchmarking...", async ctx => 
            {
                var tasks = new List<Task>();
                for (int i = 0; i < count; i++)
                {
                    tasks.Add(_producer.SendAsync(topic, i.ToString(), "benchmark-payload"));
                }
                await Task.WhenAll(tasks);
            });
            
            sw.Stop();
            var rate = count / sw.Elapsed.TotalSeconds;
            
            AnsiConsole.MarkupLine($"[green]Completed in {sw.Elapsed.TotalSeconds:F2}s[/]");
            AnsiConsole.MarkupLine($"[yellow]Throughput: {rate:F2} msg/sec[/]");
            AnsiConsole.MarkupLine("Press any key to continue...");
            Console.ReadKey(true);
        }
        
        static void ShowClusterStatus()
        {
            var table = new Table();
            table.AddColumn("Broker ID");
            table.AddColumn("Status");
            table.AddColumn("Storage Path");
            
            table.AddRow("1", "[green]Online[/]", "./kafka-data");
            
            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine("Press any key to continue...");
            Console.ReadKey(true);
        }
    }
}
