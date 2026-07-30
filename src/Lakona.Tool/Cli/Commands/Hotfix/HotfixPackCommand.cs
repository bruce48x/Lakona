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
        var project = ReadOption(args, "--project") ?? Path.Combine("Server", "Hotfix", "Server.Hotfix.csproj");
        var output = ReadOption(args, "--output") ?? Path.Combine("artifacts", "hotfix");
        var configuration = ReadOption(args, "--configuration") ?? "Release";
        var version = ReadOption(args, "--version") ?? "v" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss'Z'");

        var result = await packager.PackAsync(
            new LakonaPackageRequest(
                Directory.GetCurrentDirectory(),
                LakonaPackageKind.Hotfix,
                Configuration: configuration,
                OutputDirectory: output,
                Version: version,
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
