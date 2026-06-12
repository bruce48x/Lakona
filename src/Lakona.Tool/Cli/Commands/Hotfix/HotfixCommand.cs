using Lakona.Tool.Hotfix;

namespace Lakona.Tool.Cli.Commands.Hotfix;

internal sealed class HotfixCommand
{
    private readonly ICliTerminal terminal;

    public HotfixCommand(ICliTerminal terminal)
    {
        this.terminal = terminal;
    }

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0)
        {
            throw new CliUsageException("Missing hotfix subcommand.");
        }

        return args[0] switch
        {
            "install" => await new HotfixInstallCommand(terminal).RunAsync(args.Skip(1).ToArray(), cancellationToken).ConfigureAwait(false),
            "activate" => await new HotfixActivateCommand(terminal).RunAsync(args.Skip(1).ToArray(), cancellationToken).ConfigureAwait(false),
            "status" => await new HotfixStatusCommand(terminal).RunAsync(args.Skip(1).ToArray(), cancellationToken).ConfigureAwait(false),
            "rollback" => await new HotfixRollbackCommand(terminal).RunAsync(args.Skip(1).ToArray(), cancellationToken).ConfigureAwait(false),
            "pack" => await new HotfixPackCommand(terminal).RunAsync(args.Skip(1).ToArray(), cancellationToken).ConfigureAwait(false),
            _ => throw new CliUsageException($"Unknown hotfix subcommand '{args[0]}'.")
        };
    }
}
