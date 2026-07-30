using Lakona.ProjectSystem;

namespace Lakona.Tool.Cli.Commands.Server;

internal sealed class ServerCommand
{
    private readonly ICliTerminal terminal;
    private readonly ILakonaProjectPackager packager;

    public ServerCommand(ICliTerminal terminal, ILakonaProjectPackager? packager = null)
    {
        this.terminal = terminal;
        this.packager = packager ?? new LakonaProjectPackager();
    }

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0)
        {
            throw new CliUsageException("Missing server subcommand.");
        }

        return args[0] switch
        {
            "pack" => await new ServerPackCommand(terminal, packager).RunAsync(args.Skip(1).ToArray(), cancellationToken).ConfigureAwait(false),
            _ => throw new CliUsageException($"Unknown server subcommand '{args[0]}'.")
        };
    }
}
