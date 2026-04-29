namespace NexCode.Cli;

/// <summary>
/// Thin entry point — argv parsing lives in <see cref="CommandRouter"/> so it can be
/// unit-tested without spawning the process.
/// </summary>
internal static class Program
{
    public static Task<int> Main(string[] args)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        return CommandRouter.ExecuteAsync(args, cts.Token);
    }
}
