// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace RLNet.Visualizer;

/// <summary>
/// What the console should open with, parsed from the command line.
/// </summary>
/// <remarks>
/// Being able to say <c>--env Pendulum --algo Sac --start</c> turns the console from something you
/// click through into something you can put in a script: a demo that opens on the right screen, a
/// documentation screenshot that is reproducible, a side-by-side comparison launched twice with
/// different algorithms. Every option has a sensible default, so a bare launch behaves as before.
/// </remarks>
public sealed class StartupOptions
{
    /// <summary>Environment to open, by catalog name. Defaults to CartPole.</summary>
    public string Environment { get; private set; } = "CartPole";

    /// <summary>Algorithm to open with, or null for the environment's first supported one.</summary>
    public Algorithm? Algorithm { get; private set; }

    /// <summary>Begin training immediately rather than waiting for the button.</summary>
    public bool AutoStart { get; private set; }

    /// <summary>Steps attempted per frame, or null for the slider's default.</summary>
    public int? Speed { get; private set; }

    /// <summary>Set when the command line asked for help, or could not be understood.</summary>
    public string? Message { get; private set; }

    /// <summary>Parses arguments, returning defaults for anything not given.</summary>
    public static StartupOptions Parse(string[] args)
    {
        var options = new StartupOptions();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--env" or "-e" when i + 1 < args.Length:
                    options.Environment = args[++i];
                    break;

                case "--algo" or "-a" when i + 1 < args.Length:
                    if (Enum.TryParse<Algorithm>(args[++i], ignoreCase: true, out var algorithm))
                        options.Algorithm = algorithm;
                    else
                        options.Message = $"Unknown algorithm '{args[i]}'. Known: {string.Join(", ", Enum.GetNames<Algorithm>())}.";
                    break;

                case "--start" or "-s":
                    options.AutoStart = true;
                    break;

                case "--speed" when i + 1 < args.Length:
                    if (int.TryParse(args[++i], out int speed)) options.Speed = speed;
                    break;

                case "--list":
                    options.Message = "Environments:" + System.Environment.NewLine + string.Join(
                        System.Environment.NewLine,
                        Catalog.Environments.Select(e =>
                            $"  {e.Name,-14} {e.Category,-12} {string.Join(", ", e.SupportedAlgorithms)}"));
                    break;

                case "--help" or "-h":
                    options.Message = Help;
                    break;
            }
        }

        // Fail here rather than at window construction, so a typo produces a readable message
        // instead of an exception dialog.
        if (options.Message is null &&
            !Catalog.Environments.Any(e => e.Name.Equals(options.Environment, StringComparison.OrdinalIgnoreCase)))
        {
            options.Message =
                $"Unknown environment '{options.Environment}'." + System.Environment.NewLine +
                "Known: " + string.Join(", ", Catalog.Environments.Select(e => e.Name));
        }

        return options;
    }

    /// <summary>Whether the console should print <see cref="Message"/> and exit rather than open.</summary>
    public bool ShouldExit => Message is not null;

    private const string Help = """
        RLNet Console - watch a reinforcement-learning agent train.
        Created by Gravicode Studios, led by Kang Fadhil.

          --env,   -e <name>   Environment to open (default: CartPole)
          --algo,  -a <name>   Algorithm: QLearning, Dqn, A2C, Ppo, Sac, Td3
          --start, -s          Begin training immediately
          --speed     <n>      Steps attempted per frame (1 - 65536)
          --list               List the environments and what each supports
          --help,  -h          Show this

        Examples:
          RLNet.Visualizer --env Pendulum --algo Sac --start
          RLNet.Visualizer --env PredatorPrey --start --speed 64
        """;
}
