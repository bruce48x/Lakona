using Lakona.Tool.Server;

namespace Lakona.Tool.Cli.Commands.Server;

internal sealed class ServerCommand
{
    private readonly ICliTerminal terminal;
    private readonly IServerPackageWriter writer;

    public ServerCommand(ICliTerminal terminal, IServerPackageWriter? writer = null)
    {
        this.terminal = terminal;
        this.writer = writer ?? new ServerPackageWriter();
    }

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0)
        {
            throw new CliUsageException("Missing server subcommand.");
        }

        return args[0] switch
        {
            "pack" => await new ServerPackCommand(terminal, writer).RunAsync(args.Skip(1).ToArray(), cancellationToken).ConfigureAwait(false),
            _ => throw new CliUsageException($"Unknown server subcommand '{args[0]}'.")
        };
    }
}
