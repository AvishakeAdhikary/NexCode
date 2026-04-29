using NexCode.Cli.Commands;

namespace NexCode.Cli;

/// <summary>
/// Top-level CLI dispatcher (spec §5.2). <see cref="Program"/> calls
/// <see cref="ExecuteAsync"/> with the raw argv. Each top-level verb maps to a single
/// <c>*Command.RunAsync</c> static entry point.
/// </summary>
internal static class CommandRouter
{
    public static async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        var verb = args[0];

        // Slash-prefixed verbs (spec §11 / §12.3 / §40 / §34).
        if (verb.StartsWith("/", StringComparison.Ordinal))
        {
            return verb.ToLowerInvariant() switch
            {
                "/init" => await InitCommand.RunAsync(args[1..], cancellationToken),
                "/model" => await SlashModelCommand.RunAsync(args[1..], cancellationToken),
                _ => UnknownCommand(verb)
            };
        }

        return verb.ToLowerInvariant() switch
        {
            "--version" or "-v" or "version" => PrintVersion(),
            "--help" or "-h" or "help" => PrintUsageAndOk(),
            "service" => await ServiceCommand.RunAsync(args[1..], cancellationToken),
            "account" => await AccountCommand.RunAsync(args[1..], cancellationToken),
            "session" => await SessionCommand.RunAsync(args[1..], cancellationToken),
            "chat" => await ChatCommand.RunAsync(args[1..], cancellationToken),
            "run" => await RunCommand.RunAsync(args[1..], cancellationToken),
            "plan" => await PlanCommand.RunAsync(args[1..], cancellationToken),
            "todo" => await TodoCommand.RunAsync(args[1..], cancellationToken),
            "mcp" => await McpCommand.RunAsync(args[1..], cancellationToken),
            "git" => await GitCommand.RunAsync(args[1..], cancellationToken),
            "agent" => await AgentCommand.RunAsync(args[1..], cancellationToken),
            "memory" => await MemoryCommand.RunAsync(args[1..], cancellationToken),
            "remote" => await RemoteCommand.RunAsync(args[1..], cancellationToken),
            "config" => await ConfigCommand.RunAsync(args[1..], cancellationToken),
            "shell" => await ShellCommand.RunAsync(args[1..], cancellationToken),
            "init" => await InitCommand.RunAsync(args[1..], cancellationToken),
            "model" => await SlashModelCommand.RunAsync(args[1..], cancellationToken),
            "subagent" => await SubAgents.SubAgentMain.RunAsync(args[1..]),
            _ => UnknownCommand(verb)
        };
    }

    public static int PrintVersion()
    {
        Console.WriteLine("NexCode CLI 0.1.0");
        return 0;
    }

    private static int PrintUsageAndOk()
    {
        PrintUsage();
        return 0;
    }

    public static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'.");
        PrintUsage();
        return 1;
    }

    public static void PrintUsage()
    {
        Console.WriteLine("NexCode CLI (spec §5.2):");
        Console.WriteLine("  nexcode --version");
        Console.WriteLine("  nexcode chat [--mode <m>] [--project <path>] [--sandbox]");
        Console.WriteLine("  nexcode run \"<prompt>\" [--mode <m>] [--no-confirm]");
        Console.WriteLine("  nexcode session list|load|export <id>");
        Console.WriteLine("  nexcode plan get|confirm|reject <id>");
        Console.WriteLine("  nexcode todo get|check|uncheck <id>");
        Console.WriteLine("  nexcode mcp serve [--port <p>] [--stdio]");
        Console.WriteLine("  nexcode mcp connect <config>");
        Console.WriteLine("  nexcode git <subcommand> [--project <path>]");
        Console.WriteLine("  nexcode agent spawn <config-json>");
        Console.WriteLine("  nexcode agent kill <agent-id>");
        Console.WriteLine("  nexcode memory get|set|list [--key <k>] [--value <v>] [--scope global|project|session]");
        Console.WriteLine("  nexcode remote serve [--port <p>] | nexcode remote connect <host:port>");
        Console.WriteLine("  nexcode config providers|modes|personalities");
        Console.WriteLine("  nexcode shell [--type powershell|cmd|custom]");
        Console.WriteLine("  nexcode /init [--project <path>]");
        Console.WriteLine("  nexcode /model [<id>|?]");
        Console.WriteLine("  nexcode service ping|events");
        Console.WriteLine("  nexcode account status|sign-in|refresh-subscription");
    }
}
