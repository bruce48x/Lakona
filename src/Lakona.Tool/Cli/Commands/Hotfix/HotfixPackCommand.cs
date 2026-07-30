using Lakona.ProjectSystem;

namespace Lakona.Tool.Cli.Commands.Hotfix;

internal sealed class HotfixPackCommand
{
    private readonly ICliTerminal terminal;
    private readonly ILakonaProjectPackager packager;

    public HotfixPackCommand(
        ICliTerminal terminal,
        ILakonaProjectPackager? packager = null)
    {
        this.terminal = terminal;
        this.packager = packager ?? new LakonaProjectPackager();
    }

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Contains("--version", StringComparer.Ordinal))
        {
            throw new CliUsageException("Unknown hotfix pack option '--version'.");
        }

        var project = ReadOption(args, "--project") ?? Path.Combine("Server", "Hotfix", "Server.Hotfix.csproj");
        var output = ReadOption(args, "--output") ?? "Server/Build";
        var configuration = ReadOption(args, "--configuration") ?? "Release";

        var result = await packager.PackAsync(
            new LakonaPackageRequest(
                Directory.GetCurrentDirectory(),
                LakonaPackageKind.Hotfix,
                Configuration: configuration,
                OutputDirectory: output,
                HotfixProjectPath: project),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        terminal.WriteLine($"Packed hotfix {result.ArtifactPath}.");
        return 0;
    }

    private static string? ReadOption(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
