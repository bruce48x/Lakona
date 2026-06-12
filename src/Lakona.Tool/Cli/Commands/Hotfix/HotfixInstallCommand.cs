using Lakona.Tool.Hotfix;

namespace Lakona.Tool.Cli.Commands.Hotfix;

internal sealed class HotfixInstallCommand
{
    private readonly ICliTerminal terminal;

    public HotfixInstallCommand(ICliTerminal terminal)
    {
        this.terminal = terminal;
    }

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0)
        {
            throw new CliUsageException("Missing hotfix package path.");
        }

        var root = ReadOption(args, "--root") ?? "hotfix";
        var version = await new HotfixPackageInstaller().InstallAsync(args[0], root, cancellationToken).ConfigureAwait(false);
        terminal.WriteLine($"Installed hotfix {version}.");
        return 0;
    }

    private static string? ReadOption(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
