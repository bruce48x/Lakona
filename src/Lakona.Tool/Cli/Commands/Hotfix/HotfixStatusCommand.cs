using Lakona.Tool.Hotfix;

namespace Lakona.Tool.Cli.Commands.Hotfix;

internal sealed class HotfixStatusCommand
{
    private readonly ICliTerminal terminal;

    public HotfixStatusCommand(ICliTerminal terminal)
    {
        this.terminal = terminal;
    }

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var server = ReadOption(args, "--server") ?? "http://127.0.0.1:20090";
        terminal.WriteLine(await new HotfixAdminClient().GetAsync(server, "/_lakona/hotfix/status", cancellationToken).ConfigureAwait(false));
        return 0;
    }

    private static string? ReadOption(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
