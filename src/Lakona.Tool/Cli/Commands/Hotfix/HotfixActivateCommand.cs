using Lakona.Tool.Hotfix;

namespace Lakona.Tool.Cli.Commands.Hotfix;

internal sealed class HotfixActivateCommand
{
    private readonly ICliTerminal terminal;

    public HotfixActivateCommand(ICliTerminal terminal)
    {
        this.terminal = terminal;
    }

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0)
        {
            throw new CliUsageException("Missing hotfix version.");
        }

        var server = ReadOption(args, "--server") ?? "http://127.0.0.1:20090";
        var json = await new HotfixAdminClient().PostAsync(
            server,
            "/_lakona/hotfix/activate",
            new { version = args[0], expectedCurrentVersion = ReadOption(args, "--expected-current-version"), operationId = args[0] },
            cancellationToken).ConfigureAwait(false);
        terminal.WriteLine(json);
        return 0;
    }

    private static string? ReadOption(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
