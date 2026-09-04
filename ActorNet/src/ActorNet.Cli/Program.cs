// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ActorNet.Cli;
using ActorNet.Cli.Commands;
using Spectre.Console;
using Spectre.Console.Cli;

var app = new CommandApp();

app.Configure(config =>
{
    config.SetApplicationName("actornet");
    config.SetApplicationVersion(typeof(Theme).Assembly.GetName().Version?.ToString(3) ?? "0.1.0");

    config.AddCommand<RunCommand>("run")
        .WithDescription("Start a node and keep it running.")
        .WithExample("run", "--port", "9001")
        .WithExample("run", "--port", "9002", "--seed", "127.0.0.1:9001");

    config.AddCommand<MonitorCommand>("monitor")
        .WithDescription("Start a node with a live dashboard in the terminal.")
        .WithExample("monitor", "--load");

    config.AddCommand<DemoCommand>("demo")
        .WithDescription("Run a worked scenario: banking, telemetry, ordering, or lifecycle.")
        .WithExample("demo", "banking")
        .WithExample("demo", "ordering");

    config.AddCommand<ClusterCommand>("cluster")
        .WithDescription("Join a cluster and show the member table and key placement.")
        .WithExample("cluster", "--port", "9002", "--seed", "127.0.0.1:9001", "--watch");

    config.AddCommand<BenchCommand>("bench")
        .WithDescription("Measure local message throughput, counting only messages actually handled.")
        .WithExample("bench", "-n", "2000000", "-a", "16");

    config.AddCommand<ScenariosCommand>("scenarios")
        .WithDescription("List the demo scenarios and what each one demonstrates.");

    // A stack trace is the right thing for a framework bug and the wrong thing for "port 9000 is
    // already in use", which is most of what goes wrong here. Both stay available behind -v.
    config.SetExceptionHandler((ex, _) =>
    {
        AnsiConsole.WriteLine();
        Theme.Fail(ex.Message.Safe());

        if (Environment.GetEnvironmentVariable("ACTORNET_DEBUG") is "1" or "true")
            AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
        else
            AnsiConsole.MarkupLine($"[{Theme.Muted}]Set ACTORNET_DEBUG=1 for the full stack trace.[/]");

        return 1;
    });
});

return await app.RunAsync(args);
